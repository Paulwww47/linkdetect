namespace LinkDetect.Tests;

public sealed class ImageBackgroundProcessorTests
{
    private static readonly (byte B, byte G, byte R, byte A) White = (255, 255, 255, 255);
    private static readonly (byte B, byte G, byte R, byte A) Red = (0, 0, 255, 255);
    private static readonly (byte B, byte G, byte R, byte A) Blue = (255, 0, 0, 255);
    private static readonly (byte B, byte G, byte R, byte A) Green = (0, 255, 0, 255);
    private static readonly (byte B, byte G, byte R, byte A) Orange = (0, 128, 255, 255);
    private static readonly (byte B, byte G, byte R, byte A) Purple = (128, 0, 255, 255);
    private static readonly (byte B, byte G, byte R, byte A) Cyan = (255, 255, 0, 255);
    private static readonly (byte B, byte G, byte R, byte A) Magenta = (255, 0, 255, 255);
    private static readonly (byte B, byte G, byte R, byte A) Black = (0, 0, 0, 255);
    private static readonly (byte B, byte G, byte R, byte A) Gray = (128, 128, 128, 255);

    [Fact]
    public void ExactThresholdSegmentIsQualifiedAndCropped()
    {
        var image = CreateTopRunImage(whiteLength: 6, depthAt: x => 2 + x % 3);

        var result = ImageBackgroundProcessor.Process(image, threshold: 0.60);

        Assert.True(result.HasBackground);
        Assert.True(result.CanProcess);
        Assert.Equal(10, result.Processed.Width);
        Assert.Equal(8, result.Processed.Height);
    }

    [Fact]
    public void BelowThresholdSegmentIsNotQualified()
    {
        var image = CreateTopRunImage(whiteLength: 5, depthAt: x => 2 + x % 3);

        var result = ImageBackgroundProcessor.Process(image, threshold: 0.60);

        Assert.False(result.HasBackground);
        Assert.False(result.CanProcess);
        Assert.Equal(10, result.Processed.Width);
        Assert.Equal(10, result.Processed.Height);
    }

    [Fact]
    public void TwoThirtyPercentSegmentsAreNotQualified()
    {
        var image = CreateImage(10, 10, (x, y) =>
        {
            if (y == 0)
            {
                if (x < 3)
                {
                    return White;
                }

                if (x < 7)
                {
                    return Red;
                }

                return White;
            }

            if (x == 0)
            {
                return y % 2 == 0 ? Blue : Green;
            }

            if (x == 9)
            {
                return y % 2 == 0 ? Orange : Purple;
            }

            if (y == 9)
            {
                return x % 2 == 0 ? Cyan : Magenta;
            }

            return Gray;
        });

        var result = ImageBackgroundProcessor.Process(image, threshold: 0.60);

        Assert.False(result.HasBackground);
        Assert.False(result.CanProcess);
    }

    [Fact]
    public void SlightColorDifferenceWithinToleranceIsQualified()
    {
        var image = CreateImage(10, 10, (x, y) =>
        {
            if (y == 0)
            {
                if (x < 6)
                {
                    var value = (byte)(250 + x % 2);
                    return (value, value, value, (byte)255);
                }

                return Red;
            }

            if (x == 0)
            {
                return y % 2 == 0 ? Blue : Green;
            }

            if (x == 9)
            {
                return y % 2 == 0 ? Orange : Purple;
            }

            if (y == 9)
            {
                return x % 2 == 0 ? Cyan : Magenta;
            }

            return Gray;
        });

        var result = ImageBackgroundProcessor.Process(image, threshold: 0.60);

        Assert.True(result.HasBackground);
        Assert.True(result.CanProcess);
    }

    [Fact]
    public void ObviousColorDifferenceSplitsRunAndIsRejected()
    {
        var image = CreateImage(10, 10, (x, y) =>
        {
            if (y == 0)
            {
                if (x == 3)
                {
                    return Black;
                }

                if (x < 6)
                {
                    return (250, 250, 250, (byte)255);
                }

                return Red;
            }

            if (x == 0)
            {
                return y % 2 == 0 ? Blue : Green;
            }

            if (x == 9)
            {
                return y % 2 == 0 ? Orange : Purple;
            }

            if (y == 9)
            {
                return x % 2 == 0 ? Cyan : Magenta;
            }

            return Gray;
        });

        var result = ImageBackgroundProcessor.Process(image, threshold: 0.60);

        Assert.False(result.HasBackground);
        Assert.False(result.CanProcess);
    }

    [Fact]
    public void AllFourEdgesAreCroppedTogether()
    {
        var image = CreateImage(10, 10, (x, y) =>
        {
            if (x == 0)
            {
                return Blue;
            }

            if (x == 9)
            {
                return Green;
            }

            if (y == 0)
            {
                return White;
            }

            if (y == 9)
            {
                return Black;
            }

            return Gray;
        });

        var result = ImageBackgroundProcessor.Process(image, threshold: 0.60);

        Assert.True(result.HasBackground);
        Assert.True(result.CanProcess);
        Assert.Equal(8, result.Processed.Width);
        Assert.Equal(8, result.Processed.Height);

        var pixel = GetPixel(result.Processed, 0, 0);
        Assert.Equal(Gray.B, pixel.B);
        Assert.Equal(Gray.G, pixel.G);
        Assert.Equal(Gray.R, pixel.R);
        Assert.Equal(Gray.A, pixel.A);
    }

    [Fact]
    public void PartialSegmentCropsWholeEdgeToMinimumDepth()
    {
        var image = CreateTopRunImage(whiteLength: 6, depthAt: x => 1 + x % 2);

        var result = ImageBackgroundProcessor.Process(image, threshold: 0.60);

        Assert.True(result.HasBackground);
        Assert.True(result.CanProcess);
        Assert.Equal(10, result.Processed.Width);
        Assert.Equal(9, result.Processed.Height);
    }

    [Fact]
    public void SeventyPercentSegmentIsQualifiedAtSeventyThreshold()
    {
        var image = CreateImage(10, 10, (x, y) =>
        {
            if (y == 0)
            {
                return x < 7 ? White : Red;
            }

            if (x < 7 && y < 3)
            {
                return White;
            }

            if (x == 0)
            {
                return y % 2 == 0 ? Blue : Green;
            }

            if (x == 9)
            {
                return y % 2 == 0 ? Orange : Purple;
            }

            if (y == 9)
            {
                return x % 2 == 0 ? Cyan : Magenta;
            }

            return Gray;
        });

        var result = ImageBackgroundProcessor.Process(image, threshold: 0.70);

        Assert.True(result.HasBackground);
        Assert.True(result.CanProcess);
        Assert.Equal(10, result.Processed.Width);
        Assert.Equal(7, result.Processed.Height);
    }

    [Fact]
    public void MiddleSeventyPercentSegmentIsQualifiedAtSeventyThreshold()
    {
        var image = CreateImage(10, 10, (x, y) =>
        {
            if (y == 0)
            {
                if (x >= 1 && x < 8)
                {
                    return White;
                }

                return Red;
            }

            if (x >= 1 && x < 8 && y < 4)
            {
                return White;
            }

            if (x == 0)
            {
                return y % 2 == 0 ? Blue : Green;
            }

            if (x == 9)
            {
                return y % 2 == 0 ? Orange : Purple;
            }

            if (y == 9)
            {
                return x % 2 == 0 ? Cyan : Magenta;
            }

            return Gray;
        });

        var result = ImageBackgroundProcessor.Process(image, threshold: 0.70);

        Assert.True(result.HasBackground);
        Assert.True(result.CanProcess);
        Assert.Equal(10, result.Processed.Width);
        Assert.Equal(6, result.Processed.Height);
    }

    [Fact]
    public void WideTopRunWithFullLeftEdgeCropsBothEdges()
    {
        var image = CreateImage(700, 500, (x, y) =>
        {
            if (x == 0)
            {
                return White;
            }

            if (y == 499)
            {
                return x % 2 == 0 ? Red : Blue;
            }

            if (x == 699)
            {
                return y % 2 == 0 ? Green : Purple;
            }

            if (y == 0)
            {
                return x < 20 ? Black : White;
            }

            if (x >= 20 && y < 50)
            {
                return White;
            }

            return Gray;
        });

        var result = ImageBackgroundProcessor.Process(image, threshold: 0.70);

        Assert.True(result.HasBackground);
        Assert.True(result.CanProcess);
        Assert.Equal(699, result.Processed.Width);
        Assert.Equal(450, result.Processed.Height);
    }

    [Fact]
    public void TextInsideTopBandKeepsDominantBackgroundDepth()
    {
        var image = CreateImage(700, 500, (x, y) =>
        {
            if (x == 0)
            {
                return White;
            }

            if (y == 499)
            {
                return x % 2 == 0 ? Red : Blue;
            }

            if (x == 699)
            {
                return y % 2 == 0 ? Green : Purple;
            }

            if (y == 0)
            {
                return White;
            }

            if (x < 20 && y < 30)
            {
                return Black;
            }

            if (y < 50)
            {
                return White;
            }

            return Gray;
        });

        var result = ImageBackgroundProcessor.Process(image, threshold: 0.70);

        Assert.True(result.HasBackground);
        Assert.True(result.CanProcess);
        Assert.Equal(699, result.Processed.Width);
        Assert.Equal(450, result.Processed.Height);
    }

    [Fact]
    public void SolidColorImageKeepsOriginalAndCannotProcess()
    {
        var image = CreateImage(10, 10, (_, _) => White);

        var result = ImageBackgroundProcessor.Process(image, threshold: 0.60);

        Assert.True(result.HasBackground);
        Assert.False(result.CanProcess);
        Assert.Same(image, result.Processed);
        Assert.Equal(10, result.Processed.Width);
        Assert.Equal(10, result.Processed.Height);
    }

    private static ImagePixelData CreateTopRunImage(int whiteLength, Func<int, int> depthAt)
    {
        return CreateImage(10, 10, (x, y) =>
        {
            if (y == 0)
            {
                return x < whiteLength ? White : Red;
            }

            if (x < whiteLength && y < depthAt(x))
            {
                return White;
            }

            if (x == 0)
            {
                return y % 2 == 0 ? Blue : Green;
            }

            if (x == 9)
            {
                return y % 2 == 0 ? Orange : Purple;
            }

            if (y == 9)
            {
                return x % 2 == 0 ? Cyan : Magenta;
            }

            return ((x * 31 + y * 17) % 200) switch
            {
                < 50 => ((byte)(50 + x), (byte)(60 + y), (byte)70, (byte)255),
                _ => ((byte)(120 + x), (byte)(130 + y), (byte)140, (byte)255)
            };
        });
    }

    private static ImagePixelData CreateImage(
        int width,
        int height,
        Func<int, int, (byte B, byte G, byte R, byte A)> colorAt)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        var span = pixels.AsSpan();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = colorAt(x, y);
                var offset = y * stride + x * 4;
                span[offset] = color.B;
                span[offset + 1] = color.G;
                span[offset + 2] = color.R;
                span[offset + 3] = color.A;
            }
        }

        return new ImagePixelData(width, height, stride, pixels);
    }

    private static (byte B, byte G, byte R, byte A) GetPixel(ImagePixelData image, int x, int y)
    {
        var offset = y * image.Stride + x * 4;
        var span = image.Pixels.Span;
        return (span[offset], span[offset + 1], span[offset + 2], span[offset + 3]);
    }
}
