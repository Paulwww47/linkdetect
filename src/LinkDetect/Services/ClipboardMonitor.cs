using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LinkDetect.Services;

public sealed class ClipboardChangedEventArgs(
    string? text,
    bool hasText,
    uint sequenceNumber,
    BitmapSource? image = null,
    bool hasImage = false) : EventArgs
{
    public string? Text { get; } = text;

    public bool HasText { get; } = hasText;

    public uint SequenceNumber { get; } = sequenceNumber;

    public BitmapSource? Image { get; } = image;

    public bool HasImage { get; } = hasImage;
}

public sealed class ClipboardMonitor : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private const int RetryCount = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

    private readonly Dispatcher _dispatcher;
    private readonly CancellationTokenSource _disposeToken = new();
    private HwndSource? _messageSource;
    private uint _lastSequenceNumber;
    private bool _suppressNextUpdate;
    private bool _enabled = true;
    private bool _disposed;

    public ClipboardMonitor(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_messageSource is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("LinkDetect.ClipboardListener")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000)
        };

        _messageSource = new HwndSource(parameters);
        _messageSource.AddHook(WindowProcedure);

        if (!AddClipboardFormatListener(_messageSource.Handle))
        {
            var error = Marshal.GetLastWin32Error();
            _messageSource.RemoveHook(WindowProcedure);
            _messageSource.Dispose();
            _messageSource = null;
            throw new Win32Exception(error, "无法注册剪贴板监听。");
        }

        _lastSequenceNumber = GetClipboardSequenceNumber();
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (enabled)
        {
            _suppressNextUpdate = false;
            _lastSequenceNumber = GetClipboardSequenceNumber();
        }
    }

    public void SuppressNextClipboardUpdate()
    {
        _suppressNextUpdate = true;
    }

    public void ClearSuppression()
    {
        _suppressNextUpdate = false;
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmClipboardUpdate || !_enabled || _disposed)
        {
            return IntPtr.Zero;
        }

        var sequenceNumber = GetClipboardSequenceNumber();
        if (_suppressNextUpdate)
        {
            _suppressNextUpdate = false;
            _lastSequenceNumber = sequenceNumber;
            return IntPtr.Zero;
        }

        if (sequenceNumber == _lastSequenceNumber)
        {
            return IntPtr.Zero;
        }

        _lastSequenceNumber = sequenceNumber;
        _dispatcher.BeginInvoke(() => _ = ReadClipboardAsync(sequenceNumber));
        return IntPtr.Zero;
    }

    private async Task ReadClipboardAsync(uint sequenceNumber)
    {
        for (var attempt = 0; attempt < RetryCount; attempt++)
        {
            if (_disposed || !_enabled || _disposeToken.IsCancellationRequested)
            {
                return;
            }

            if (GetClipboardSequenceNumber() != sequenceNumber)
            {
                return;
            }

            try
            {
                var hasText = System.Windows.Clipboard.ContainsText(System.Windows.TextDataFormat.UnicodeText);
                var text = hasText
                    ? System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.UnicodeText)
                    : null;

                // ClipboardImageReader repairs DIBs whose fourth byte is undefined but was written
                // as zero (Telegram, WeChat, ...); otherwise the image decodes fully transparent.
                var image = ClipboardImageReader.Read();
                if (image is null && System.Windows.Clipboard.ContainsFileDropList())
                {
                    image = TryLoadFirstImageFile();
                }

                var hasImage = image is not null;
                if (image is not null && image.CanFreeze)
                {
                    image.Freeze();
                }

                if (GetClipboardSequenceNumber() != sequenceNumber)
                {
                    return;
                }

                ClipboardChanged?.Invoke(
                    this,
                    new ClipboardChangedEventArgs(text, hasText, sequenceNumber, image, hasImage));
                return;
            }
            catch (ExternalException)
            {
                if (attempt >= RetryCount - 1)
                {
                    return;
                }

                try
                {
                    await Task.Delay(RetryDelay, _disposeToken.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeToken.Cancel();

        if (_messageSource is not null)
        {
            RemoveClipboardFormatListener(_messageSource.Handle);
            _messageSource.RemoveHook(WindowProcedure);
            _messageSource.Dispose();
            _messageSource = null;
        }

        _disposeToken.Dispose();
    }

    private static BitmapSource? TryLoadFirstImageFile()
    {
        System.Collections.Specialized.StringCollection? files;
        try
        {
            files = System.Windows.Clipboard.GetFileDropList();
        }
        catch
        {
            return null;
        }

        var path = ClipboardImageFiles.PickFirstSupportedFile(files?.Cast<string>());
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}
