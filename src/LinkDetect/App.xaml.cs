using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LinkDetect.Services;
using LinkDetect.Windows;

namespace LinkDetect;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\LinkDetect.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private ClipboardMonitor? _clipboardMonitor;
    private FloatingLinkWindow? _floatingWindow;
    private TrayIconService? _trayIcon;
    private SettingsService? _settingsService;
    private StartupManager? _startupManager;
    private SettingsWindow? _settingsWindow;
    private WordTokenizer? _tokenizer;
    private CancellationTokenSource? _imageProcessingCts;
    private string? _recentlyCopiedText;
    private DateTime _recentCopyTimestamp;
    private ImagePixelData? _processedImage;
    private double _backgroundThresholdPercent = BackgroundThreshold.DefaultPercent;
    private bool _isPaused;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        _settingsService = new SettingsService();
        _backgroundThresholdPercent = _settingsService.Current.BackgroundCropThresholdPercent;
        _startupManager = new StartupManager();
        _tokenizer = new WordTokenizer();
        _floatingWindow = new FloatingLinkWindow(_settingsService, _tokenizer);
        _floatingWindow.CopyImageRequested += OnCopyImageRequested;
        _floatingWindow.CopyTextRequested += OnCopyTextRequested;
        _clipboardMonitor = new ClipboardMonitor(Dispatcher);
        _clipboardMonitor.ClipboardChanged += OnClipboardChanged;

        _trayIcon = new TrayIconService(
            Dispatcher,
            onTogglePause: TogglePause,
            onToggleStartup: ToggleStartup,
            onOpenSettings: OpenSettings,
            onExit: ExitApplication,
            startupEnabled: _startupManager.IsEnabled);

        _clipboardMonitor.Start();

        // 词典加载在后台完成，避免第一次点“分词”卡半秒。
        var tokenizer = _tokenizer;
        _ = Task.Run(tokenizer.WarmUp);
    }

    private void OnClipboardChanged(object? sender, ClipboardChangedEventArgs e)
    {
        if (_isPaused || _floatingWindow is null)
        {
            return;
        }

        var text = e.HasText && !string.IsNullOrWhiteSpace(e.Text) ? e.Text : null;

        // “复制所选”写入剪贴板会再触发一次更新；此时保持卡片上的分词结果不变。
        if (text is not null &&
            string.Equals(text, _recentlyCopiedText, StringComparison.Ordinal) &&
            DateTime.UtcNow - _recentCopyTimestamp < TimeSpan.FromSeconds(3))
        {
            return;
        }

        _recentlyCopiedText = null;

        CancelImageProcessing();
        _processedImage = null;

        var links = LinkExtractor.Extract(text);
        if (links.Count > 0)
        {
            _floatingWindow.ShowLinks(links);
        }
        else
        {
            _floatingWindow.HideLinks();
        }

        if (text is null)
        {
            _floatingWindow.HideText();
        }
        else
        {
            _floatingWindow.ShowText(text);
        }

        if (e.HasImage && e.Image is not null)
        {
            StartImageProcessing(e.Image);
        }
        else
        {
            _floatingWindow.HideImage();
        }
    }

    private void StartImageProcessing(BitmapSource source)
    {
        if (_floatingWindow is null)
        {
            return;
        }

        try
        {
            if (source.CanFreeze)
            {
                source.Freeze();
            }

            var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var width = bgra.PixelWidth;
            var height = bgra.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            bgra.CopyPixels(pixels, stride, 0);
            var data = new ImagePixelData(width, height, stride, pixels, bgra.DpiX, bgra.DpiY);

            _floatingWindow.ShowImageProcessing(source);

            var cts = new CancellationTokenSource();
            _imageProcessingCts = cts;
            var threshold = _backgroundThresholdPercent / 100.0;

            _ = Task.Run(
                () =>
                {
                    var result = ImageBackgroundProcessor.Process(
                        data,
                        ImageBackgroundProcessor.DefaultTolerance,
                        threshold);

                    if (cts.IsCancellationRequested)
                    {
                        return;
                    }

                    Dispatcher.InvokeAsync(() =>
                    {
                        if (cts.IsCancellationRequested ||
                            !ReferenceEquals(_imageProcessingCts, cts) ||
                            _floatingWindow is null)
                        {
                            return;
                        }

                        _processedImage = result.CanProcess ? result.Processed : null;
                        var resultInfo =
                            $"{data.Width}×{data.Height} → {result.Processed.Width}×{result.Processed.Height}";
                        var edgeInfo = string.Join(
                            "；",
                            result.Diagnostics.Select(d =>
                                $"{d.Side}: {d.RunLength}/{d.EdgeLength} 深{d.Depth}" +
                                (d.Qualified ? " 合格" : " 不合格")));
                        _floatingWindow.ShowImage(
                            CreateBitmapSource(result.Processed),
                            result.CanProcess,
                            resultInfo,
                            edgeInfo);
                        AppendImageDiagnostics(data, result, threshold, edgeInfo);
                    });
                },
                cts.Token);
        }
        catch (Exception)
        {
            _floatingWindow.ShowImage(source, canCopy: false);
        }
    }

    private static BitmapSource CreateBitmapSource(ImagePixelData data)
    {
        var bitmap = BitmapSource.Create(
            data.Width,
            data.Height,
            data.DpiX,
            data.DpiY,
            PixelFormats.Bgra32,
            null,
            data.Pixels.ToArray(),
            data.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static void AppendImageDiagnostics(
        ImagePixelData source,
        ImageProcessResult result,
        double threshold,
        string edgeInfo)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LinkDetect");
            Directory.CreateDirectory(directory);
            var line =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} " +
                $"threshold={threshold:P0} " +
                $"source={source.Width}x{source.Height} " +
                $"processed={result.Processed.Width}x{result.Processed.Height} " +
                $"has={result.HasBackground} can={result.CanProcess} {edgeInfo}";
            File.AppendAllText(Path.Combine(directory, "image-processing.log"), line + Environment.NewLine);
        }
        catch
        {
            // Diagnostics are best-effort and must never interrupt clipboard handling.
        }
    }

    private void OnCopyImageRequested(object? sender, EventArgs e)    {
        if (_processedImage is null || _clipboardMonitor is null || _floatingWindow is null)
        {
            return;
        }

        _clipboardMonitor.SuppressNextClipboardUpdate();
        try
        {
            System.Windows.Clipboard.SetImage(CreateBitmapSource(_processedImage));
            _floatingWindow.MarkImageCopied();
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            _clipboardMonitor.ClearSuppression();
        }
    }

    private void OnCopyTextRequested(object? sender, string text)
    {
        if (_clipboardMonitor is null || _floatingWindow is null || string.IsNullOrEmpty(text))
        {
            return;
        }

        _recentlyCopiedText = text;
        _recentCopyTimestamp = DateTime.UtcNow;
        _clipboardMonitor.SuppressNextClipboardUpdate();

        if (ClipboardTextWriter.TryWrite(text))
        {
            _floatingWindow.MarkTextCopied();
            return;
        }

        _clipboardMonitor.ClearSuppression();
        _floatingWindow.MarkTextCopyFailed();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settingsService!, SaveBackgroundThreshold);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        if (!_settingsWindow.IsVisible)
        {
            _settingsWindow.Show();
        }

        _settingsWindow.Activate();
    }

    private void SaveBackgroundThreshold(double percent)
    {
        _backgroundThresholdPercent = BackgroundThreshold.Normalize(percent);
    }

    private void TogglePause()
    {
        _isPaused = !_isPaused;
        _clipboardMonitor?.SetEnabled(!_isPaused);

        if (_isPaused)
        {
            CancelImageProcessing();
            _processedImage = null;
            _floatingWindow?.HideAll();
        }

        _trayIcon?.SetPaused(_isPaused);
    }

    private void ToggleStartup()
    {
        if (_startupManager is null)
        {
            return;
        }

        try
        {
            _startupManager.SetEnabled(!_startupManager.IsEnabled);
        }
        catch
        {
            // Keep the current setting when Windows rejects the registry update.
        }

        _trayIcon?.SetStartupEnabled(_startupManager.IsEnabled);
    }

    private void CancelImageProcessing()
    {
        _imageProcessingCts?.Cancel();
        _imageProcessingCts = null;
    }

    private void ExitApplication()
    {
        CancelImageProcessing();
        _clipboardMonitor?.Dispose();
        _clipboardMonitor = null;

        _trayIcon?.Dispose();
        _trayIcon = null;

        _settingsWindow?.Close();
        _settingsWindow = null;

        _floatingWindow?.CloseForExit();
        _floatingWindow = null;

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CancelImageProcessing();
        _clipboardMonitor?.Dispose();
        _trayIcon?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
