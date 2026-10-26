using System.Runtime.InteropServices;
using System.Text;

namespace LinkDetect.Services;

/// <summary>
/// 用 Win32 直接写 CF_UNICODETEXT：自带短重试，失败时不动现有剪贴板内容。
/// WPF 的 Clipboard.SetText 会先清空再写，且在占用时每次重试阻塞约 1 秒。
/// </summary>
internal static class ClipboardTextWriter
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const int MaxAttempts = 10;
    private const int RetryDelayMilliseconds = 20;

    public static bool TryWrite(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var bytes = Encoding.Unicode.GetBytes(text + '\0');
        var handle = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var transferred = false;
        try
        {
            var target = GlobalLock(handle);
            if (target == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                Marshal.Copy(bytes, 0, target, bytes.Length);
            }
            finally
            {
                GlobalUnlock(handle);
            }

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                if (!OpenClipboard(IntPtr.Zero))
                {
                    Thread.Sleep(RetryDelayMilliseconds);
                    continue;
                }

                try
                {
                    EmptyClipboard();
                    if (SetClipboardData(CfUnicodeText, handle) != IntPtr.Zero)
                    {
                        transferred = true;
                        return true;
                    }
                }
                finally
                {
                    CloseClipboard();
                }

                Thread.Sleep(RetryDelayMilliseconds);
            }

            return false;
        }
        finally
        {
            if (!transferred)
            {
                GlobalFree(handle);
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr handle);
}
