using LinkDetect;

namespace LinkDetect.Models;

public sealed class AppSettings
{
    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double BackgroundCropThresholdPercent { get; set; } = BackgroundThreshold.DefaultPercent;
}
