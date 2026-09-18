namespace LinkDetect;

public static class BackgroundThreshold
{
    public const double DefaultPercent = 60;
    public const double MinPercent = 50;
    public const double MaxPercent = 90;
    public const double StepPercent = 5;

    public static double Normalize(double percent)
    {
        if (!double.IsFinite(percent))
        {
            return DefaultPercent;
        }

        var clamped = Math.Clamp(percent, MinPercent, MaxPercent);
        var steps = Math.Round(clamped / StepPercent, MidpointRounding.AwayFromZero);
        return Math.Clamp(steps * StepPercent, MinPercent, MaxPercent);
    }
}
