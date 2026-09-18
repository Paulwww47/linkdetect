using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LinkDetect.Services;
using Button = System.Windows.Controls.Button;
using MediaBrush = System.Windows.Media.Brush;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace LinkDetect.Windows;

public partial class FloatingLinkWindow : Window
{
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const double WorkAreaMargin = 12;

    private readonly SettingsService _settingsService;
    private readonly LinkPresentationState _state = new();
    private bool _allowClose;
    private bool _positionInitialized;
    private bool _hasImage;
    private bool _imageCanCopy;
    private bool _imageCopied;
    private string? _failedLink;
    private Point? _closeDragOrigin;
    private bool _isDraggingFromClose;

    public event EventHandler? CopyImageRequested;

    public FloatingLinkWindow(SettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeComponent();
    }

    public void ShowLinks(IReadOnlyList<string> links)
    {
        _state.Replace(links);
        ClearError();
        RebuildLinkButtons();
        RefreshLayoutAndVisibility();
    }

    public void HideLinks()
    {
        _state.Clear();
        LinksList.Items.Clear();
        ClearError();
        RefreshLayoutAndVisibility();
    }

    public void ShowImageProcessing(BitmapSource image)
    {
        _hasImage = true;
        _imageCanCopy = false;
        _imageCopied = false;
        ImagePreview.Source = image;
        ImagePreviewSection.Visibility = Visibility.Visible;
        ImageButtonBar.Visibility = Visibility.Visible;
        SetImageStatus("正在处理图片…", "正在检查边缘背景", "AccentBrush");
        CopyImageButton.IsEnabled = false;
        CopyImageButton.Content = "复制图片";
        RefreshLayoutAndVisibility();
    }

    public void ShowImage(
        BitmapSource image,
        bool canCopy,
        string? resultInfo = null,
        string? edgeInfo = null)
    {
        _hasImage = true;
        _imageCanCopy = canCopy;
        _imageCopied = false;
        ImagePreview.Source = image;
        ImagePreviewSection.Visibility = Visibility.Visible;
        ImageButtonBar.Visibility = Visibility.Visible;
        SetImageStatus(
            canCopy ? "裁剪完成" : "无需裁剪",
            resultInfo ?? (canCopy ? "已裁去边缘纯色背景" : "未发现可裁剪的纯色背景"),
            canCopy ? "SuccessBrush" : "TextSecondaryBrush");
        CopyImageButton.IsEnabled = canCopy;
        CopyImageButton.Content = "复制图片";
        RefreshLayoutAndVisibility();
    }

    public void HideImage()
    {
        _hasImage = false;
        _imageCanCopy = false;
        _imageCopied = false;
        ImagePreview.Source = null;
        ImagePreviewSection.Visibility = Visibility.Collapsed;
        ImageButtonBar.Visibility = Visibility.Collapsed;
        RefreshLayoutAndVisibility();
    }

    public void MarkImageCopied()
    {
        if (!_hasImage)
        {
            return;
        }

        _imageCopied = true;
        SetImageStatus("已复制到剪贴板", "可以直接粘贴使用", "SuccessBrush");
        CopyImageButton.IsEnabled = false;
        CopyImageButton.Content = "已复制";
    }

    private void SetImageStatus(string status, string detail, string brushKey)
    {
        ImageStatusText.Text = status;
        ImageStatusIndicator.Fill = (MediaBrush)FindResource(brushKey);
        ImageDetailsText.Text = detail;
        ImageDetailsText.ToolTip = detail;
    }

    public void HideAll()
    {
        _state.Clear();
        LinksList.Items.Clear();
        ClearError();
        _hasImage = false;
        _imageCanCopy = false;
        _imageCopied = false;
        ImagePreview.Source = null;
        ImagePreviewSection.Visibility = Visibility.Collapsed;
        ImageButtonBar.Visibility = Visibility.Collapsed;
        Hide();
    }

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            HideAll();
        }

        base.OnClosing(e);
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideAll();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private void RebuildLinkButtons()
    {
        LinksList.Items.Clear();

        foreach (var link in _state.Links)
        {
            var isFailed = string.Equals(link, _failedLink, StringComparison.Ordinal);
            var host = link;
            var path = string.Empty;
            if (Uri.TryCreate(link, UriKind.Absolute, out var address) &&
                !string.IsNullOrEmpty(address.Host))
            {
                var hostname = address.Host;
                try
                {
                    hostname = address.HostNameType == UriHostNameType.IPv6
                        ? $"[{address.IdnHost}]"
                        : address.IdnHost;
                }
                catch (UriFormatException)
                {
                    // A parsed URI can contain a host that cannot be converted to IDN.
                }

                host = address.IsDefaultPort ? hostname : $"{hostname}:{address.Port}";
                path = address.GetComponents(
                    UriComponents.PathAndQuery | UriComponents.Fragment,
                    UriFormat.SafeUnescaped);
                if (path == "/")
                {
                    path = string.Empty;
                }
            }

            var displayHost = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
            var label = new TextBlock
            {
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                FontSize = 14
            };
            label.Inlines.Add(new Run(displayHost)
            {
                Foreground = (MediaBrush)FindResource(isFailed ? "ErrorBrush" : "TextPrimaryBrush"),
                FontSize = 14,
                FontWeight = FontWeights.Medium
            });
            label.Inlines.Add(new Run(path)
            {
                Foreground = (MediaBrush)FindResource("TextSecondaryBrush"),
                FontSize = 13,
                FontWeight = FontWeights.Normal
            });
            Typography.SetStandardLigatures(label, false);
            Typography.SetContextualLigatures(label, false);

            object content = label;
            if (isFailed)
            {
                var errorRow = new Grid();
                errorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                errorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                errorRow.Children.Add(label);
                var failure = new TextBlock
                {
                    Text = "打开失败，重试",
                    Foreground = (MediaBrush)FindResource("ErrorBrush"),
                    FontSize = 12,
                    FontWeight = FontWeights.Normal,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(failure, 1);
                errorRow.Children.Add(failure);
                content = errorRow;
            }

            var button = new Button
            {
                Tag = link,
                Content = content,
                ToolTip = new TextBlock { Text = link, MaxWidth = 380, TextWrapping = TextWrapping.Wrap },
                Style = (Style)FindResource("LinkButtonStyle")
            };
            AutomationProperties.SetName(button, $"打开链接：{link}");
            AutomationProperties.SetHelpText(button, isFailed ? "打开失败，点击重试" : "在默认浏览器中打开");

            if (isFailed)
            {
                button.Background = (MediaBrush)FindResource("ErrorSoftBrush");
                button.BorderBrush = (MediaBrush)FindResource("ErrorBrush");
            }

            button.Click += LinkButton_OnClick;
            LinksList.Items.Add(button);
        }

        if (LinksList.Items.Count > 0)
        {
            ((Button)LinksList.Items[^1]).Margin = new Thickness(2, 0, 2, 0);
        }
    }

    private void LinkButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string link })
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
            _state.Remove(link);
            ClearError();

            if (!_state.IsVisible && !_hasImage)
            {
                HideAll();
                return;
            }

            RebuildLinkButtons();
            RefreshLayoutAndVisibility();
        }
        catch
        {
            ShowError(link);
        }
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_isDraggingFromClose)
        {
            HideAll();
        }
    }

    private void CopyImageButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_imageCanCopy || _imageCopied)
        {
            return;
        }

        CopyImageRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CloseAllButton_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _closeDragOrigin = e.GetPosition(this);
    }

    private void CloseAllButton_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        _closeDragOrigin = null;
    }

    private void CloseAllButton_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_closeDragOrigin is not Point origin || e.LeftButton != MouseButtonState.Pressed ||
            !CloseAllButton.IsMouseCaptured)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _closeDragOrigin = null;
        _isDraggingFromClose = true;
        e.Handled = true;
        // Cancel the button's pressed state before entering the native move loop.
        CloseAllButton.ReleaseMouseCapture();
        try
        {
            // Preserve movement already made before entering the native move loop.
            Left += position.X - origin.X;
            Top += position.Y - origin.Y;
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove can fail if the mouse button is released before WPF starts the move.
        }
        finally
        {
            _isDraggingFromClose = false;
            ClampToCurrentWorkArea();
            _settingsService.SaveWindowPosition(Left, Top);
            _positionInitialized = true;
        }
    }

    private void ShowError(string link)
    {
        _failedLink = link;
        RebuildLinkButtons();
    }

    private void ClearError()
    {
        _failedLink = null;
    }

    private void RefreshLayoutAndVisibility()
    {
        if (!_hasImage && !_state.IsVisible)
        {
            Hide();
            return;
        }

        AutomationProperties.SetName(this, _hasImage ? "图片预览" : $"{_state.Links.Count} 个链接");

        if (!IsVisible)
        {
            Opacity = 0;
            Show();
            UpdateLayout();
            ApplyInitialOrSavedPosition();
            ClampToCurrentWorkArea();
            Opacity = 1;
        }
        else
        {
            UpdateLayout();
            ClampToCurrentWorkArea();
        }
    }

    private void ApplyInitialOrSavedPosition()
    {
        if (_positionInitialized)
        {
            return;
        }

        var settings = _settingsService.Current;
        if (settings.WindowLeft is double left && double.IsFinite(left) &&
            settings.WindowTop is double top && double.IsFinite(top))
        {
            Left = left;
            Top = top;
        }
        else
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - ActualWidth - WorkAreaMargin;
            Top = workArea.Bottom - ActualHeight - WorkAreaMargin;
        }

        _positionInitialized = true;
    }

    private void ClampToCurrentWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var workLeft = monitorInfo.WorkArea.Left / dpi.DpiScaleX;
        var workTop = monitorInfo.WorkArea.Top / dpi.DpiScaleY;
        var workRight = monitorInfo.WorkArea.Right / dpi.DpiScaleX;
        var workBottom = monitorInfo.WorkArea.Bottom / dpi.DpiScaleY;

        var maxHeight = Math.Max(1, Math.Min(580, workBottom - workTop - 2 * WorkAreaMargin));
        if (MaxHeight != maxHeight)
        {
            MaxHeight = maxHeight;
            UpdateLayout();
        }

        var maxLeft = Math.Max(workLeft + WorkAreaMargin, workRight - ActualWidth - WorkAreaMargin);
        var maxTop = Math.Max(workTop + WorkAreaMargin, workBottom - ActualHeight - WorkAreaMargin);

        Left = Math.Clamp(Left, workLeft + WorkAreaMargin, maxLeft);
        Top = Math.Clamp(Top, workTop + WorkAreaMargin, maxTop);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }
}
