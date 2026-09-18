using System.Drawing;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace LinkDetect.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action _onTogglePause;
    private readonly Action _onToggleStartup;
    private readonly Action _onOpenSettings;
    private readonly Action _onExit;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private readonly Forms.ToolStripMenuItem _startupItem;
    private Icon _icon;

    public TrayIconService(
        Dispatcher dispatcher,
        Action onTogglePause,
        Action onToggleStartup,
        Action onOpenSettings,
        Action onExit,
        bool startupEnabled)
    {
        _dispatcher = dispatcher;
        _onTogglePause = onTogglePause;
        _onToggleStartup = onToggleStartup;
        _onOpenSettings = onOpenSettings;
        _onExit = onExit;

        _pauseItem = new Forms.ToolStripMenuItem("暂停监听");
        _pauseItem.Click += (_, _) => Invoke(_onTogglePause);

        _startupItem = new Forms.ToolStripMenuItem("开机自动运行")
        {
            Checked = startupEnabled,
            CheckOnClick = false
        };
        _startupItem.Click += (_, _) => Invoke(_onToggleStartup);

        var settingsItem = new Forms.ToolStripMenuItem("设置");
        settingsItem.Click += (_, _) => Invoke(_onOpenSettings);

        var exitItem = new Forms.ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => Invoke(_onExit);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _icon = AppIconFactory.CreateTrayIcon(paused: false);
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "LinkDetect - 正在监听剪贴板",
            Icon = _icon,
            ContextMenuStrip = menu,
            Visible = true
        };
    }

    public void SetPaused(bool paused)
    {
        _pauseItem.Text = paused ? "继续监听" : "暂停监听";
        _notifyIcon.Text = paused ? "LinkDetect - 已暂停" : "LinkDetect - 正在监听剪贴板";

        var previousIcon = _icon;
        _icon = AppIconFactory.CreateTrayIcon(paused);
        _notifyIcon.Icon = _icon;
        previousIcon.Dispose();
    }

    public void SetStartupEnabled(bool enabled) => _startupItem.Checked = enabled;

    private void Invoke(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.BeginInvoke(action);
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}
