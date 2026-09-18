using System.Text.Json;
using System.IO;
using LinkDetect.Models;

namespace LinkDetect.Services;

public sealed class SettingsService
{
    private readonly string _settingsPath;

    public SettingsService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LinkDetect");
        _settingsPath = Path.Combine(directory, "settings.json");
        Current = Load();
    }

    public AppSettings Current { get; }

    public void SaveWindowPosition(double left, double top)
    {
        Current.WindowLeft = left;
        Current.WindowTop = top;

        Save();
    }

    public void SaveThreshold(double percent)
    {
        Current.BackgroundCropThresholdPercent = BackgroundThreshold.Normalize(percent);
        Save();
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // Settings are best-effort and must never interrupt clipboard monitoring.
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            settings.BackgroundCropThresholdPercent =
                BackgroundThreshold.Normalize(settings.BackgroundCropThresholdPercent);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }
}
