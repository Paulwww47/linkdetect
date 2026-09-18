using Microsoft.Win32;

namespace LinkDetect.Services;

public sealed class StartupManager
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LinkDetect";

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
                return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
            }
            catch
            {
                return false;
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
            ?? throw new InvalidOperationException("无法打开当前用户的启动项注册表位置。");

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("无法确定程序路径。");
        }

        key.SetValue(ValueName, $"\"{executablePath}\"");
    }
}
