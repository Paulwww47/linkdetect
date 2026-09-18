using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LinkDetect.Services;

namespace ClipboardProbe;

/// <summary>
/// Reproduces the Telegram/WeChat clipboard payload (40-byte BITMAPINFOHEADER, 32bpp BI_RGB,
/// fourth byte zero) on the real clipboard and compares WPF's decoder with ClipboardImageReader.
/// </summary>
internal static class Program
{
    private const uint CfDib = 8;

    [STAThread]
    private static int Main()
    {
        var originalText = Clipboard.ContainsText() ? Clipboard.GetText() : null;

        try
        {
            SetClipboardDib(BuildDib(64, 48));
            Console.WriteLine("clipboard now holds a synthetic CF_DIB (32bpp BI_RGB, header 40, fourth byte 0)");

            var data = Retry(() => Clipboard.GetDataObject())!;
            Console.WriteLine("formats: " + string.Join(" | ", data.GetFormats(false)));
            foreach (var format in new[] { "DeviceIndependentBitmap", "Format17", "Bitmap", "PNG" })
            {
                var present = Retry(() => data.GetDataPresent(format, false));
                var payload = present ? Retry(() => data.GetData(format, false)) : null;
                var header = string.Empty;
                if (payload is Stream stream && stream.CanSeek)
                {
                    var bytes = new byte[stream.Length];
                    stream.Position = 0;
                    stream.ReadExactly(bytes);
                    if (ClipboardImageReader.TryReadDibHeader(bytes, out var parsed))
                    {
                        header = $" header={parsed.HeaderSize} {parsed.BitCount}bpp compression={parsed.Compression} alphaMask=0x{parsed.AlphaMask:X}";
                    }
                }

                Console.WriteLine(
                    $"  {format}: present={present} payload={Describe(payload)}{header}");
            }

            var rawDib = GetClipboardDib();
            Console.WriteLine(rawDib is null
                ? "  raw GetClipboardData(CF_DIB) = null"
                : $"  raw GetClipboardData(CF_DIB) = {rawDib.Length} bytes, headerSize={BitConverter.ToInt32(rawDib, 0)}, bitCount={BitConverter.ToInt16(rawDib, 14)}");

            var wpf = Retry(() => Clipboard.GetImage());
            Console.WriteLine(wpf is null
                ? "WPF Clipboard.GetImage() = null"
                : $"WPF Clipboard.GetImage()   : {Describe(wpf)}");

            var reader = Retry(() => ClipboardImageReader.Read());
            Console.WriteLine(reader is null
                ? "ClipboardImageReader.Read() = null"
                : $"ClipboardImageReader.Read() : {Describe(reader)}");

            if (reader is null)
            {
                Console.WriteLine("RESULT: FAIL (reader returned null)");
                return 1;
            }

            var pixels = ToBgra(reader);
            var topLeft = PixelAt(pixels, reader.PixelWidth, 2, 2);
            var topRight = PixelAt(pixels, reader.PixelWidth, reader.PixelWidth - 3, 2);
            Console.WriteLine($"pixel(2,2)={topLeft} pixel(w-3,2)={topRight}");
            Console.WriteLine(
                topLeft.Alpha == 255 && topLeft.Red > 200 && topRight.Blue > 200
                    ? "RESULT: PASS (opaque, colours preserved)"
                    : "RESULT: FAIL (unexpected pixel values)");
            return 0;
        }
        finally
        {
            if (originalText is not null)
            {
                Clipboard.SetText(originalText);
            }
        }
    }

    private static string Describe(object? payload)
    {
        switch (payload)
        {
            case null:
                return "null";
            case byte[] bytes:
                return $"byte[{bytes.Length}]";
            case Stream stream:
                return $"{stream.GetType().Name}[{stream.Length}]";
            case BitmapSource bitmap:
                return $"{bitmap.PixelWidth}x{bitmap.PixelHeight} {bitmap.Format}";
            default:
                return payload.GetType().FullName ?? "unknown";
        }
    }

    private static T? Retry<T>(Func<T?> action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (COMException) when (attempt < 9)
            {
                Thread.Sleep(80);
            }
        }
    }

    private static string Describe(BitmapSource source)
    {
        var bgra = ToBgra(source);
        var minAlpha = 255;
        var maxAlpha = 0;
        for (var i = 3; i < bgra.Length; i += 4)
        {
            minAlpha = Math.Min(minAlpha, bgra[i]);
            maxAlpha = Math.Max(maxAlpha, bgra[i]);
        }

        return $"{source.PixelWidth}x{source.PixelHeight} format={source.Format} alpha[min={minAlpha} max={maxAlpha}]";
    }

    private static byte[] ToBgra(BitmapSource source)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static (byte Blue, byte Green, byte Red, byte Alpha) PixelAt(byte[] bgra, int width, int x, int y)
    {
        var offset = y * width * 4 + x * 4;
        return (bgra[offset], bgra[offset + 1], bgra[offset + 2], bgra[offset + 3]);
    }

    /// <summary>Bottom-up 32bpp BI_RGB DIB: left half red, right half blue, fourth byte zero.</summary>
    private static byte[] BuildDib(int width, int height)
    {
        const int headerSize = 40;
        var stride = width * 4;
        var dib = new byte[headerSize + stride * height];

        BitConverter.TryWriteBytes(dib.AsSpan(0, 4), headerSize);
        BitConverter.TryWriteBytes(dib.AsSpan(4, 4), width);
        BitConverter.TryWriteBytes(dib.AsSpan(8, 4), height);
        BitConverter.TryWriteBytes(dib.AsSpan(12, 2), (short)1);
        BitConverter.TryWriteBytes(dib.AsSpan(14, 2), (short)32);
        BitConverter.TryWriteBytes(dib.AsSpan(20, 4), stride * height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = headerSize + y * stride + x * 4;
                var leftHalf = x < width / 2;
                dib[offset] = leftHalf ? (byte)0 : (byte)255;
                dib[offset + 1] = 0;
                dib[offset + 2] = leftHalf ? (byte)255 : (byte)0;
                dib[offset + 3] = 0;
            }
        }

        return dib;
    }

    private static byte[]? GetClipboardDib()
    {
        return Retry(() =>
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                throw new COMException($"OpenClipboard failed: {Marshal.GetLastWin32Error()}");
            }

            try
            {
                var handle = GetClipboardData(CfDib);
                if (handle == IntPtr.Zero)
                {
                    return null;
                }

                var size = (int)GlobalSize(handle);
                var pointer = GlobalLock(handle);
                if (pointer == IntPtr.Zero || size <= 0)
                {
                    return null;
                }

                try
                {
                    var bytes = new byte[size];
                    Marshal.Copy(pointer, bytes, 0, size);
                    return bytes;
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }
            finally
            {
                CloseClipboard();
            }
        });
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr GlobalSize(IntPtr handle);

    private static void SetClipboardDib(byte[] dib)
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            throw new InvalidOperationException($"OpenClipboard failed: {Marshal.GetLastWin32Error()}");
        }

        try
        {
            EmptyClipboard();
            var handle = Marshal.AllocHGlobal(dib.Length);
            Marshal.Copy(dib, 0, handle, dib.Length);
            if (SetClipboardData(CfDib, handle) == IntPtr.Zero)
            {
                Marshal.FreeHGlobal(handle);
                throw new InvalidOperationException($"SetClipboardData failed: {Marshal.GetLastWin32Error()}");
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();
}
