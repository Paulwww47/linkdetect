using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LinkDetect.Models;
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
    private const double FullCardWidth = 456;

    private readonly SettingsService _settingsService;
    private readonly WordTokenizer _tokenizer;
    private readonly LinkPresentationState _state = new();
    private readonly ObservableCollection<TokenChip> _chips = [];
    private bool _allowClose;
    private bool _positionInitialized;
    private bool _hasImage;
    private bool _imageCanCopy;
    private bool _imageCopied;
    private string? _text;
    private double _anchoredRight = double.NaN;
    private bool? _paintSelection;
    private bool _isPainting;
    private int _dragAnchorIndex = -1;
    private (int Low, int High) _paintedRange = (-1, -1);
    private bool[] _dragOriginalSelection = [];
    private DispatcherTimer? _copyFeedbackTimer;
    private string? _failedLink;
    private Point? _closeDragOrigin;
    private bool _isDraggingFromClose;

    public event EventHandler? CopyImageRequested;

    public event EventHandler<string>? CopyTextRequested;

    public FloatingLinkWindow(SettingsService settingsService, WordTokenizer tokenizer)
    {
        _settingsService = settingsService;
        _tokenizer = tokenizer;
        InitializeComponent();
        TokensList.ItemsSource = _chips;
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

    public void ShowText(string text)
    {
        _text = text;
        EndPainting();
        _chips.Clear();
        TokensList.Visibility = Visibility.Collapsed;
        ResetCopyFeedback();
        CopyTextButton.Visibility = Visibility.Collapsed;
        CopyTextButton.IsEnabled = false;
        CopyCountText.Text = string.Empty;
        TokenizeButton.Visibility = Visibility.Visible;

        var tooLong = text.Length > WordTokenizer.MaxTextLength;
        TokenizeButton.IsEnabled = !tooLong;
        TokenizeButton.ToolTip = tooLong
            ? $"文本 {text.Length} 字，超过 {WordTokenizer.MaxTextLength} 字上限，无法分词；链接识别不受影响。"
            : "按词切分这段文本";

        RefreshLayoutAndVisibility();
    }

    public void HideText()
    {
        _text = null;
        EndPainting();
        _chips.Clear();
        TokensList.Visibility = Visibility.Collapsed;
        ResetCopyFeedback();
        TokenizeButton.Visibility = Visibility.Collapsed;
        CopyTextButton.Visibility = Visibility.Collapsed;
        RefreshLayoutAndVisibility();
    }

    public void MarkTextCopied()
    {
        ShowCopyFeedback("✓", "已写入剪贴板，可直接粘贴。", true);
    }

    public void MarkTextCopyFailed()
    {
        ShowCopyFeedback("!", "剪贴板被其他程序占用，请重试。", false);
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
        _text = null;
        EndPainting();
        _chips.Clear();
        TokensList.Visibility = Visibility.Collapsed;
        ResetCopyFeedback();
        TokenizeButton.Visibility = Visibility.Collapsed;
        CopyTextButton.Visibility = Visibility.Collapsed;
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

    private async void TokenizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        var text = _text;
        if (text is null || text.Length > WordTokenizer.MaxTextLength)
        {
            return;
        }

        TokenizeButton.IsEnabled = false;
        TokenizeButton.ToolTip = "正在分词…";

        try
        {
            var tokens = await Task.Run(() => _tokenizer.Tokenize(text));
            if (!ReferenceEquals(_text, text))
            {
                return;
            }

            // 一次性挂数据源：上千个词块逐个 Add 会触发上千次布局。
            TokensList.ItemsSource = null;
            _chips.Clear();
            foreach (var token in tokens)
            {
                _chips.Add(new TokenChip(token.Text, token.Start, token.Length));
            }

            TokensList.ItemsSource = _chips;

            if (_chips.Count == 0)
            {
                TokenizeButton.IsEnabled = true;
                TokenizeButton.ToolTip = "按词切分这段文本";
                return;
            }

            TokenizeButton.Visibility = Visibility.Collapsed;
            CopyTextButton.Visibility = Visibility.Visible;
            TokensList.Visibility = Visibility.Visible;
            UpdateSelectionStatus();
            RefreshLayoutAndVisibility();
        }
        catch (Exception)
        {
            TokenizeButton.IsEnabled = true;
            TokenizeButton.ToolTip = "词典资源不可用，请重新安装程序。";
        }
    }

    private void CopyTextButton_OnClick(object sender, RoutedEventArgs e)
    {
        CopySelection();
    }

    private void TokensList_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var chip = FindChip(e.OriginalSource as DependencyObject);
        if (chip is null)
        {
            return;
        }

        if (e.ChangedButton == MouseButton.Middle)
        {
            SplitChip(chip);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var index = _chips.IndexOf(chip);
        if (index < 0)
        {
            return;
        }

        _paintSelection = !chip.IsSelected;
        _dragOriginalSelection = _chips.Select(item => item.IsSelected).ToArray();
        _dragAnchorIndex = index;
        _paintedRange = (index, index);
        chip.IsSelected = _paintSelection.Value;
        _isPainting = true;
        TokensList.CaptureMouse();
        UpdateSelectionStatus();
        e.Handled = true;
    }

    private void TokensList_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPainting || _paintSelection is not bool selection ||
            _dragAnchorIndex < 0 || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var chip = FindChip(TokensList.InputHitTest(e.GetPosition(TokensList)) as DependencyObject);
        if (chip is null)
        {
            return;
        }

        var index = _chips.IndexOf(chip);
        if (index < 0)
        {
            return;
        }

        // 按词块顺序取区间：跨行拖动时中间整段都选中，而不是只选鼠标划过的几块。
        var low = Math.Min(_dragAnchorIndex, index);
        var high = Math.Max(_dragAnchorIndex, index);
        if (_paintedRange == (low, high))
        {
            return;
        }

        ApplyRange(low, high, selection);
    }

    private void TokensList_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 鼠标快速移动时最后一次移动可能没赶上，抬起时按当前位置补一次区间。
        if (_isPainting && _paintSelection is bool selection && _dragAnchorIndex >= 0)
        {
            var chip = FindChip(TokensList.InputHitTest(e.GetPosition(TokensList)) as DependencyObject);
            var index = chip is null ? -1 : _chips.IndexOf(chip);
            if (index >= 0)
            {
                var low = Math.Min(_dragAnchorIndex, index);
                var high = Math.Max(_dragAnchorIndex, index);
                if (_paintedRange != (low, high))
                {
                    ApplyRange(low, high, selection);
                }
            }
        }

        EndPainting();
    }

    private void TokensList_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        _isPainting = false;
        _paintSelection = null;
        _dragAnchorIndex = -1;
        _paintedRange = (-1, -1);
        _dragOriginalSelection = [];
    }

    private void EndPainting()
    {
        _isPainting = false;
        _paintSelection = null;
        _dragAnchorIndex = -1;
        _paintedRange = (-1, -1);
        _dragOriginalSelection = [];
        if (TokensList.IsMouseCaptured)
        {
            TokensList.ReleaseMouseCapture();
        }
    }

    private void SplitChip(TokenChip chip)
    {
        IReadOnlyList<WordToken> pieces;
        try
        {
            pieces = _tokenizer.Split(chip.Text);
        }
        catch (Exception)
        {
            return;
        }

        if (pieces.Count <= 1)
        {
            return;
        }

        var index = _chips.IndexOf(chip);
        if (index < 0)
        {
            return;
        }

        _chips.RemoveAt(index);
        foreach (var piece in pieces)
        {
            _chips.Insert(index++, new TokenChip(piece.Text, chip.Start + piece.Start, piece.Length));
        }

        UpdateSelectionStatus();
        RefreshLayoutAndVisibility();
    }

    private void CopySelection()
    {
        var selected = _chips.Where(chip => chip.IsSelected).OrderBy(chip => chip.Start).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var builder = new StringBuilder();
        int? previousEnd = null;
        foreach (var chip in selected)
        {
            if (previousEnd is int end && chip.Start != end)
            {
                builder.Append(' ');
            }

            builder.Append(chip.Text);
            previousEnd = chip.End;
        }

        CopyTextRequested?.Invoke(this, builder.ToString());
    }

    private void ApplyRange(int low, int high, bool selection)
    {
        var (previousLow, previousHigh) = _paintedRange;
        var restore = _dragOriginalSelection.Length == _chips.Count;

        for (var index = 0; index < _chips.Count; index++)
        {
            if (index >= low && index <= high)
            {
                _chips[index].IsSelected = selection;
            }
            else if (restore && index >= previousLow && index <= previousHigh)
            {
                _chips[index].IsSelected = _dragOriginalSelection[index];
            }
        }

        _paintedRange = (low, high);
        UpdateSelectionStatus();
    }

    private void UpdateSelectionStatus()
    {
        var selected = _chips.Count(chip => chip.IsSelected);
        CopyTextButton.Style = (Style)FindResource(
            selected > 0 ? "FilledCountButtonStyle" : "RingIconButtonStyle");
        CopyCountText.Text = selected.ToString();
        CopyCountText.FontSize = selected < 10000 ? 12 : 10;
        CopyTextButton.IsEnabled = selected > 0;
    }

    private void ShowCopyFeedback(string symbol, string detail, bool success)
    {
        _copyFeedbackTimer?.Stop();
        CopyTextButton.Style = (Style)FindResource("RingIconButtonStyle");
        CopyTextButton.Background = (MediaBrush)FindResource(success ? "SuccessBrush" : "ErrorSoftBrush");
        CopyTextButton.BorderBrush = (MediaBrush)FindResource(success ? "SuccessBrush" : "ErrorBrush");
        CopyTextButton.Foreground = (MediaBrush)FindResource(success ? "SurfaceBrush" : "ErrorBrush");
        CopyTextButton.IsHitTestVisible = false;
        CopyTextButton.ToolTip = detail;
        CopyCountText.Text = symbol;
        CopyCountText.FontSize = 12;
        _copyFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
        _copyFeedbackTimer.Tick += (_, _) => ResetCopyFeedback();
        _copyFeedbackTimer.Start();
    }

    private void ResetCopyFeedback()
    {
        _copyFeedbackTimer?.Stop();
        _copyFeedbackTimer = null;
        CopyTextButton.IsHitTestVisible = true;
        CopyTextButton.ClearValue(BackgroundProperty);
        CopyTextButton.ClearValue(BorderBrushProperty);
        CopyTextButton.ClearValue(ForegroundProperty);
        CopyTextButton.ToolTip = "复制选中的词";
        UpdateSelectionStatus();
    }

    private static TokenChip? FindChip(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: TokenChip chip })
            {
                return chip;
            }

            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }

        return null;
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
            _anchoredRight = Left + ActualWidth;
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
        if (!_hasImage && !_state.IsVisible && _text is null)
        {
            Hide();
            return;
        }

        // 只有“分词”按钮时按内容收窄卡片，不要留一条空白长条，也不要裁掉关闭按钮。
        var compact = !_hasImage && !_state.IsVisible && _chips.Count == 0;
        ApplySizeMode(compact);

        AutomationProperties.SetName(
            this,
            _hasImage ? "图片预览" : _state.IsVisible ? $"{_state.Links.Count} 个链接" : "文本分词");

        if (!IsVisible)
        {
            Opacity = 0;
            Show();
            UpdateLayout();
            ApplyInitialOrSavedPosition();
            UpdateLayout();
            ClampToCurrentWorkArea();
            Opacity = 1;
        }
        else
        {
            UpdateLayout();
            if (double.IsFinite(_anchoredRight))
            {
                Left = _anchoredRight - ActualWidth;
            }

            ClampToCurrentWorkArea();
        }

        // 右边缘锚点：宽度变化时卡片贴着右边缘伸缩，不左右跳动。
        _anchoredRight = Left + ActualWidth;
    }

    private void ApplySizeMode(bool compact)
    {
        if (compact)
        {
            // 宽度按顶部按钮行的实际需要算，既不裁掉关闭按钮，也不留空白长条。
            HeaderRow.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            var width = Math.Ceiling(HeaderRow.DesiredSize.Width) + 68;
            if (SizeToContent != SizeToContent.Height)
            {
                SizeToContent = SizeToContent.Height;
            }

            if (Math.Abs(Width - width) >= 0.5)
            {
                Width = width;
            }

            return;
        }

        if (SizeToContent != SizeToContent.Height)
        {
            SizeToContent = SizeToContent.Height;
        }

        if (Math.Abs(Width - FullCardWidth) >= 0.5)
        {
            Width = FullCardWidth;
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
