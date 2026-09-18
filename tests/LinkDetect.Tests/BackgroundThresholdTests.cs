namespace LinkDetect.Tests;

public sealed class BackgroundThresholdTests
{
    [Fact]
    public void DefaultIsSixtyPercent()
    {
        Assert.Equal(60, BackgroundThreshold.DefaultPercent);
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(90, 90)]
    public void KeepsAllowedBoundaries(double input, double expected)
    {
        Assert.Equal(expected, BackgroundThreshold.Normalize(input));
    }

    [Theory]
    [InlineData(20, 50)]
    [InlineData(200, 90)]
    public void ClampsOutOfRangeValues(double input, double expected)
    {
        Assert.Equal(expected, BackgroundThreshold.Normalize(input));
    }

    [Theory]
    [InlineData(double.NaN, 60)]
    [InlineData(double.PositiveInfinity, 60)]
    [InlineData(double.NegativeInfinity, 60)]
    public void HandlesNonFiniteValues(double input, double expected)
    {
        Assert.Equal(expected, BackgroundThreshold.Normalize(input));
    }

    [Theory]
    [InlineData(52, 50)]
    [InlineData(53, 55)]
    [InlineData(62, 60)]
    [InlineData(63, 65)]
    [InlineData(87, 85)]
    [InlineData(88, 90)]
    public void SnapsToNearestFivePercent(double input, double expected)
    {
        Assert.Equal(expected, BackgroundThreshold.Normalize(input));
    }
}
