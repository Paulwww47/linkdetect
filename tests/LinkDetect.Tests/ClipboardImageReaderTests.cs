using System.Windows.Media;
using System.Windows.Media.Imaging;
using LinkDetect.Services;

namespace LinkDetect.Tests;

public class ClipboardImageReaderTests
{
    private static readonly (byte B, byte G, byte R, byte A) Red = (0, 0, 255, 0);
    private static readonly (byte B, byte G, byte R, byte A) Blue = (255, 0, 0, 0);

    [Fact]
    public void DecodeOpaqueDib_DecodesBottomUpRowsInVisualOrder()
    {
        // A bottom-up DIB stores its last visual row first, like Telegram's clipboard payload.
        var dib = BuildDib(2, 2, topDown: false, rows: [Red, Blue]);

        var bitmap = ClipboardImageReader.DecodeOpaqueDib(dib);

        Assert.NotNull(bitmap);
        Assert.Equal(PixelFormats.Bgr32, bitmap!.Format);
        Assert.Equal(2, bitmap.PixelWidth);
        Assert.Equal(2, bitmap.PixelHeight);
        Assert.Equal(new byte[] { 0, 0, 255, 0, 0, 0, 255, 0 }, ReadRow(bitmap, 0));
        Assert.Equal(new byte[] { 255, 0, 0, 0, 255, 0, 0, 0 }, ReadRow(bitmap, 1));
    }

    [Fact]
    public void DecodeOpaqueDib_DecodesTopDownRows()
    {
        var dib = BuildDib(2, 2, topDown: true, rows: [Red, Blue]);

        var bitmap = ClipboardImageReader.DecodeOpaqueDib(dib);

        Assert.NotNull(bitmap);
        Assert.Equal(new byte[] { 0, 0, 255, 0, 0, 0, 255, 0 }, ReadRow(bitmap!, 0));
        Assert.Equal(new byte[] { 255, 0, 0, 0, 255, 0, 0, 0 }, ReadRow(bitmap!, 1));
    }

    [Fact]
    public void DecodeOpaqueDib_IgnoresUndefinedFourthByte()
    {
        var dib = BuildDib(1, 1, topDown: false, rows: [Red]);

        var bitmap = ClipboardImageReader.DecodeOpaqueDib(dib);

        Assert.NotNull(bitmap);
        Assert.Equal(PixelFormats.Bgr32, bitmap!.Format);
        Assert.Equal(new byte[] { 0, 0, 255, 0 }, ReadRow(bitmap, 0));
    }

    [Fact]
    public void DecodeOpaqueDib_AcceptsCompleteBmpFile()
    {
        var dib = BuildDib(1, 1, topDown: false, rows: [Blue], fileHeader: true);

        var bitmap = ClipboardImageReader.DecodeOpaqueDib(dib);

        Assert.NotNull(bitmap);
        Assert.Equal(new byte[] { 255, 0, 0, 0 }, ReadRow(bitmap!, 0));
    }

    [Theory]
    [InlineData(24)]
    [InlineData(8)]
    public void DecodeOpaqueDib_ReturnsNullForOtherBitDepths(int bitCount)
    {
        var dib = BuildDib(2, 2, topDown: false, rows: [Red, Blue], bitCount: bitCount);

        Assert.Null(ClipboardImageReader.DecodeOpaqueDib(dib));
    }

    [Fact]
    public void DecodeOpaqueDib_ReturnsNullForBitfieldsDib()
    {
        var dib = BuildDib(2, 2, topDown: false, rows: [Red, Blue], compression: 3);

        Assert.Null(ClipboardImageReader.DecodeOpaqueDib(dib));
    }

    [Fact]
    public void DecodeOpaqueDib_ReturnsNullForTruncatedPayload()
    {
        var dib = BuildDib(4, 4, topDown: false, rows: [Red, Blue, Red, Blue]);
        var truncated = dib[..(dib.Length - 8)];

        Assert.Null(ClipboardImageReader.DecodeOpaqueDib(truncated));
    }

    [Fact]
    public void DecodeOpaqueDib_ReturnsNullForGarbage()
    {
        Assert.Null(ClipboardImageReader.DecodeOpaqueDib([1, 2, 3]));
    }

    [Fact]
    public void TryReadDibHeader_ReportsUndefinedAlphaForBitmapInfoHeader()
    {
        var dib = BuildDib(2, 2, topDown: false, rows: [Red, Blue]);

        Assert.True(ClipboardImageReader.TryReadDibHeader(dib, out var header));
        Assert.True(header.IsUndefinedAlpha32Bit);
        Assert.False(header.HasDefinedAlpha);
        Assert.Equal(40, header.HeaderSize);
        Assert.Equal(2, header.PixelHeight);
    }

    [Fact]
    public void TryReadDibHeader_ReportsDefinedAlphaForV5Header()
    {
        var dib = BuildDib(
            2,
            2,
            topDown: false,
            rows: [Red, Blue],
            headerSize: 124,
            alphaMask: 0xFF000000);

        Assert.True(ClipboardImageReader.TryReadDibHeader(dib, out var header));
        Assert.True(header.HasDefinedAlpha);
        Assert.False(header.IsUndefinedAlpha32Bit);
        Assert.Equal(124, header.HeaderSize);
    }

    [Fact]
    public void MakeOpaque_KeepsColoursAndDropsTransparentAlpha()
    {
        var transparent = CreateBgra(2, 1, [0, 0, 255, 0, 255, 0, 0, 0]);

        var opaque = ClipboardImageReader.MakeOpaque(transparent);

        Assert.Equal(PixelFormats.Bgr32, opaque.Format);
        Assert.Equal(new byte[] { 0, 0, 255, 0, 255, 0, 0, 0 }, ReadRow(opaque, 0));
    }

    private static byte[] ReadRow(BitmapSource source, int row)
    {
        var stride = (source.PixelWidth * source.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);
        return pixels[(row * stride)..((row + 1) * stride)];
    }

    private static BitmapSource CreateBgra(int width, int height, byte[] pixels)
    {
        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] BuildDib(
        int width,
        int height,
        bool topDown,
        (byte B, byte G, byte R, byte A)[] rows,
        int bitCount = 32,
        uint compression = 0,
        int headerSize = 40,
        uint alphaMask = 0,
        bool fileHeader = false)
    {
        var bytesPerPixel = bitCount / 8;
        var stride = ((width * bitCount) + 31) / 32 * 4;
        var maskBytes = headerSize == 40 && compression == 3 ? 12 : 0;
        var prefix = fileHeader ? 14 : 0;
        var payload = new byte[stride * height];

        for (var y = 0; y < height; y++)
        {
            var visualRow = topDown ? y : height - 1 - y;
            var storedRow = y;
            var pixel = rows[visualRow];
            for (var x = 0; x < width; x++)
            {
                var offset = storedRow * stride + x * bytesPerPixel;
                if (bytesPerPixel == 4)
                {
                    payload[offset] = pixel.B;
                    payload[offset + 1] = pixel.G;
                    payload[offset + 2] = pixel.R;
                    payload[offset + 3] = pixel.A;
                }
                else if (bytesPerPixel == 3)
                {
                    payload[offset] = pixel.B;
                    payload[offset + 1] = pixel.G;
                    payload[offset + 2] = pixel.R;
                }
                else
                {
                    payload[offset] = pixel.B;
                }
            }
        }

        var header = new byte[prefix + headerSize + maskBytes];
        if (fileHeader)
        {
            header[0] = (byte)'B';
            header[1] = (byte)'M';
        }

        BitConverter.TryWriteBytes(header.AsSpan(prefix, 4), headerSize);
        BitConverter.TryWriteBytes(header.AsSpan(prefix + 4, 4), width);
        BitConverter.TryWriteBytes(header.AsSpan(prefix + 8, 4), topDown ? -height : height);
        BitConverter.TryWriteBytes(header.AsSpan(prefix + 14, 2), (short)bitCount);
        BitConverter.TryWriteBytes(header.AsSpan(prefix + 16, 4), compression);
        if (headerSize == 40 && compression == 3)
        {
            BitConverter.TryWriteBytes(header.AsSpan(prefix + 40, 4), 0x00FF0000u);
            BitConverter.TryWriteBytes(header.AsSpan(prefix + 44, 4), 0x0000FF00u);
            BitConverter.TryWriteBytes(header.AsSpan(prefix + 48, 4), 0x000000FFu);
        }

        if (alphaMask != 0 && headerSize >= 56)
        {
            BitConverter.TryWriteBytes(header.AsSpan(prefix + 40, 4), 0x00FF0000u);
            BitConverter.TryWriteBytes(header.AsSpan(prefix + 44, 4), 0x0000FF00u);
            BitConverter.TryWriteBytes(header.AsSpan(prefix + 48, 4), 0x000000FFu);
            BitConverter.TryWriteBytes(header.AsSpan(prefix + 52, 4), alphaMask);
        }

        return [.. header, .. payload];
    }
}
