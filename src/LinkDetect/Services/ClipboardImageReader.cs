using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IDataObject = System.Windows.IDataObject;

namespace LinkDetect.Services;

/// <summary>
/// Header of a device-independent bitmap (DIB) as stored on the Windows clipboard.
/// </summary>
public readonly record struct ClipboardDibHeader(
    int HeaderSize,
    int Width,
    int Height,
    int BitCount,
    uint Compression,
    uint AlphaMask,
    int PixelDataOffset)
{
    /// <summary>
    /// True for the classic 40-byte <c>BITMAPINFOHEADER</c> with 32bpp <c>BI_RGB</c> pixels.
    /// In that layout the fourth byte of every pixel is undefined by the format, and applications
    /// (Telegram, WeChat, and others) conventionally write zero there.
    /// </summary>
    public bool IsUndefinedAlpha32Bit => HeaderSize == 40 && BitCount == 32 && Compression == 0;

    /// <summary>True when the header declares an alpha channel that WPF is allowed to honour.</summary>
    public bool HasDefinedAlpha => AlphaMask != 0;

    public bool IsTopDown => Height < 0;

    public int PixelHeight => Math.Abs(Height);
}

/// <summary>
/// Reads images from the Windows clipboard while repairing the alpha channel of DIBs whose
/// fourth byte is undefined. Without this, images copied from Telegram or WeChat decode to a
/// fully transparent bitmap and the floating card shows nothing.
/// </summary>
public static class ClipboardImageReader
{
    private const string DibFormat = "DeviceIndependentBitmap";
    private const string DibV5Format = "Format17";
    private const string PngFormat = "PNG";
    private const double DefaultDpi = 96;
    private const double PixelsPerMeterToDpi = 0.0254;

    public static BitmapSource? Read()
    {
        var data = System.Windows.Clipboard.GetDataObject();

        ClipboardDibHeader? dibHeader = null;
        var dibBytes = ReadBytes(data, DibFormat);
        if (dibBytes is not null)
        {
            var opaque = DecodeOpaqueDib(dibBytes);
            if (opaque is not null)
            {
                return opaque;
            }

            if (TryReadDibHeader(dibBytes, out var parsed))
            {
                dibHeader = parsed;
            }
        }

        var alphaDefined = dibHeader?.HasDefinedAlpha == true
            || ReadHeader(data, DibV5Format)?.HasDefinedAlpha == true
            || HasFormat(data, PngFormat);

        var image = System.Windows.Clipboard.GetImage();
        if (image is null)
        {
            return null;
        }

        // An undefined alpha channel can still surface through WPF's own DIB conversion.
        return alphaDefined || !IsEntirelyTransparent(image) ? image : MakeOpaque(image);
    }

    /// <summary>
    /// Decodes a 32bpp <c>BI_RGB</c> DIB whose fourth byte carries no alpha information into an
    /// opaque <see cref="PixelFormats.Bgr32"/> bitmap. Returns null for any other DIB layout,
    /// leaving the caller to fall back to WPF's decoder.
    /// </summary>
    public static BitmapSource? DecodeOpaqueDib(byte[] dib)
    {
        ArgumentNullException.ThrowIfNull(dib);

        if (!TryReadDibHeader(dib, out var header) || !header.IsUndefinedAlpha32Bit)
        {
            return null;
        }

        var width = header.Width;
        var height = header.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var stride = width * 4;
        var required = (long)header.PixelDataOffset + (long)stride * height;
        if (required > dib.Length)
        {
            return null;
        }

        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var sourceRow = header.IsTopDown ? y : height - 1 - y;
            Buffer.BlockCopy(
                dib,
                header.PixelDataOffset + sourceRow * stride,
                pixels,
                y * stride,
                stride);
        }

        var (dpiX, dpiY) = ReadDpi(dib, header);
        var bitmap = BitmapSource.Create(
            width,
            height,
            dpiX,
            dpiY,
            PixelFormats.Bgr32,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Repaints an image as opaque, keeping its colour channels untouched.</summary>
    public static BitmapSource MakeOpaque(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var opaque = new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
        opaque.Freeze();
        return opaque;
    }

    public static bool TryReadDibHeader(byte[] dib, out ClipboardDibHeader header)
    {
        ArgumentNullException.ThrowIfNull(dib);

        header = default;
        var offset = 0;

        // Some producers hand over a complete .bmp file instead of a bare DIB.
        if (dib.Length >= 14 && dib[0] == (byte)'B' && dib[1] == (byte)'M')
        {
            offset = 14;
        }

        if (dib.Length - offset < 40)
        {
            return false;
        }

        var headerSize = BitConverter.ToInt32(dib, offset);
        if (headerSize < 40 || dib.Length - offset < headerSize)
        {
            return false;
        }

        var width = BitConverter.ToInt32(dib, offset + 4);
        var height = BitConverter.ToInt32(dib, offset + 8);
        var bitCount = BitConverter.ToInt16(dib, offset + 14);
        var compression = BitConverter.ToUInt32(dib, offset + 16);

        // Only BITMAPV3INFOHEADER and later keep their colour masks inside the header, where the
        // fourth one is the alpha mask. A 40-byte BITMAPINFOHEADER with BI_BITFIELDS stores three
        // external masks and no alpha channel at all.
        var alphaMask = headerSize >= 56 ? BitConverter.ToUInt32(dib, offset + 52) : 0u;

        var paletteEntries = bitCount <= 8 ? 1 << bitCount : 0;
        var pixelDataOffset = offset + headerSize + paletteEntries * 4 + MaskStorageSize(headerSize, compression);
        header = new ClipboardDibHeader(
            headerSize,
            width,
            height,
            bitCount,
            compression,
            alphaMask,
            pixelDataOffset);
        return true;
    }

    private static int MaskStorageSize(int headerSize, uint compression)
        => headerSize == 40 && compression == 3 ? 12 : 0;

    private static (double DpiX, double DpiY) ReadDpi(byte[] dib, ClipboardDibHeader header)
    {
        // biXPelsPerMeter / biYPelsPerMeter live at offsets 24 and 28 of the 40-byte core header.
        var offset = header.PixelDataOffset - header.HeaderSize - MaskStorageSize(header.HeaderSize, header.Compression);
        if (dib.Length < offset + 32)
        {
            return (DefaultDpi, DefaultDpi);
        }

        var dpiX = BitConverter.ToInt32(dib, offset + 24) * PixelsPerMeterToDpi;
        var dpiY = BitConverter.ToInt32(dib, offset + 28) * PixelsPerMeterToDpi;
        return (dpiX > 0 ? dpiX : DefaultDpi, dpiY > 0 ? dpiY : DefaultDpi);
    }

    private static ClipboardDibHeader? ReadHeader(IDataObject? data, string format)
    {
        var bytes = ReadBytes(data, format);
        return bytes is not null && TryReadDibHeader(bytes, out var header) ? header : null;
    }

    private static byte[]? ReadBytes(IDataObject? data, string format)
    {
        if (!HasFormat(data, format))
        {
            return null;
        }

        try
        {
            return data!.GetData(format, false) switch
            {
                byte[] bytes => bytes,
                Stream stream => ReadAll(stream),
                _ => null,
            };
        }
        catch (ExternalException)
        {
            // The clipboard is locked by another process; surfacing this lets the monitor retry.
            throw;
        }
        catch
        {
            // An unreadable format is a reason to fall back, not to fail the whole read.
            return null;
        }
    }

    private static bool HasFormat(IDataObject? data, string format)
    {
        if (data is null)
        {
            return false;
        }

        try
        {
            return data.GetDataPresent(format, false);
        }
        catch (ExternalException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ReadAll(Stream stream)
    {
        if (stream is MemoryStream memory)
        {
            return memory.ToArray();
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static bool IsEntirelyTransparent(BitmapSource source)
    {
        BitmapSource bgra = source;
        if (source.Format != PixelFormats.Bgra32 && source.Format != PixelFormats.Pbgra32)
        {
            if (source.Format == PixelFormats.Bgr32 || source.Format == PixelFormats.Bgr24)
            {
                return false;
            }

            bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        }

        var stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);

        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                return false;
            }
        }

        return true;
    }
}
