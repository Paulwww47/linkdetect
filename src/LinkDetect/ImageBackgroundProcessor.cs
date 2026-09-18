namespace LinkDetect;

public sealed class ImagePixelData
{
    public ImagePixelData(
        int width,
        int height,
        int stride,
        ReadOnlyMemory<byte> pixels,
        double dpiX = 96,
        double dpiY = 96)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (stride < width * 4)
        {
            throw new ArgumentOutOfRangeException(nameof(stride));
        }

        var requiredLength = stride * (height - 1) + width * 4;
        if (pixels.Length < requiredLength)
        {
            throw new ArgumentException("像素数据长度不足。", nameof(pixels));
        }

        Width = width;
        Height = height;
        Stride = stride;
        Pixels = pixels;
        DpiX = dpiX;
        DpiY = dpiY;
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public ReadOnlyMemory<byte> Pixels { get; }

    public double DpiX { get; }

    public double DpiY { get; }
}

public sealed class ImageProcessResult
{
    public ImageProcessResult(
        bool hasBackground,
        bool canProcess,
        ImagePixelData processed,
        IReadOnlyList<ImageEdgeDiagnostics>? diagnostics = null)
    {
        HasBackground = hasBackground;
        CanProcess = canProcess;
        Processed = processed ?? throw new ArgumentNullException(nameof(processed));
        Diagnostics = diagnostics ?? Array.Empty<ImageEdgeDiagnostics>();
    }

    public bool HasBackground { get; }

    public bool CanProcess { get; }

    public ImagePixelData Processed { get; }

    public IReadOnlyList<ImageEdgeDiagnostics> Diagnostics { get; }
}

public sealed class ImageEdgeDiagnostics
{
    public ImageEdgeDiagnostics(string side, int runLength, int edgeLength, int depth, bool qualified)
    {
        Side = side;
        RunLength = runLength;
        EdgeLength = edgeLength;
        Depth = depth;
        Qualified = qualified;
    }

    public string Side { get; }

    public int RunLength { get; }

    public int EdgeLength { get; }

    public int Depth { get; }

    public bool Qualified { get; }
}

public static class ImageBackgroundProcessor
{
    public const int DefaultTolerance = 10;
    public const double DefaultThreshold = 0.60;

    public static ImageProcessResult Process(
        ImagePixelData source,
        int tolerance = DefaultTolerance,
        double threshold = DefaultThreshold)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (tolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        }

        if (threshold <= 0 || threshold > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold));
        }

        var result = FindCropRect(source, tolerance, threshold);
        if (!result.HasBackground)
        {
            return new ImageProcessResult(false, false, source, result.Diagnostics);
        }

        var rect = result.Rect;
        if (rect is null ||
            rect.Value.Width <= 0 ||
            rect.Value.Height <= 0 ||
            (rect.Value.Width == source.Width && rect.Value.Height == source.Height))
        {
            return new ImageProcessResult(true, false, source, result.Diagnostics);
        }

        return new ImageProcessResult(true, true, Crop(source, rect.Value), result.Diagnostics);
    }

    private static CropResult FindCropRect(ImagePixelData source, int tolerance, double threshold)
    {
        var left = 0;
        var top = 0;
        var right = source.Width;
        var bottom = source.Height;
        var hasBackground = false;
        var diagnostics = new List<ImageEdgeDiagnostics>(4);

        var topRun = FindLongestRun(
            source,
            source.Width,
            i => i * 4,
            tolerance);
        var topQualified = IsQualified(topRun.Length, source.Width, threshold);
        var topDepth = topQualified ? GetTopDepth(source, topRun, tolerance) : 0;
        diagnostics.Add(new ImageEdgeDiagnostics(
            "上",
            topRun.Length,
            source.Width,
            topDepth,
            topQualified));
        if (topQualified)
        {
            hasBackground = true;
            if (topDepth > 0)
            {
                top = Math.Max(top, topDepth);
            }
        }

        var bottomRun = FindLongestRun(
            source,
            source.Width,
            i => (source.Height - 1) * source.Stride + i * 4,
            tolerance);
        var bottomQualified = IsQualified(bottomRun.Length, source.Width, threshold);
        var bottomDepth = bottomQualified ? GetBottomDepth(source, bottomRun, tolerance) : 0;
        diagnostics.Add(new ImageEdgeDiagnostics(
            "下",
            bottomRun.Length,
            source.Width,
            bottomDepth,
            bottomQualified));
        if (bottomQualified)
        {
            hasBackground = true;
            if (bottomDepth > 0)
            {
                bottom = Math.Min(bottom, source.Height - bottomDepth);
            }
        }

        var leftRun = FindLongestRun(
            source,
            source.Height,
            i => i * source.Stride,
            tolerance);
        var leftQualified = IsQualified(leftRun.Length, source.Height, threshold);
        var leftDepth = leftQualified ? GetLeftDepth(source, leftRun, tolerance) : 0;
        diagnostics.Add(new ImageEdgeDiagnostics(
            "左",
            leftRun.Length,
            source.Height,
            leftDepth,
            leftQualified));
        if (leftQualified)
        {
            hasBackground = true;
            if (leftDepth > 0)
            {
                left = Math.Max(left, leftDepth);
            }
        }

        var rightRun = FindLongestRun(
            source,
            source.Height,
            i => i * source.Stride + (source.Width - 1) * 4,
            tolerance);
        var rightQualified = IsQualified(rightRun.Length, source.Height, threshold);
        var rightDepth = rightQualified ? GetRightDepth(source, rightRun, tolerance) : 0;
        diagnostics.Add(new ImageEdgeDiagnostics(
            "右",
            rightRun.Length,
            source.Height,
            rightDepth,
            rightQualified));
        if (rightQualified)
        {
            hasBackground = true;
            if (rightDepth > 0)
            {
                right = Math.Min(right, source.Width - rightDepth);
            }
        }

        if (!hasBackground)
        {
            return new CropResult(false, null, diagnostics);
        }

        var width = right - left;
        var height = bottom - top;
        if (width <= 0 || height <= 0)
        {
            return new CropResult(true, null, diagnostics);
        }

        return new CropResult(true, new CropRect(left, top, width, height), diagnostics);
    }

    private static EdgeRun FindLongestRun(
        ImagePixelData source,
        int length,
        Func<int, int> offsetAt,
        int tolerance)
    {
        var pixels = source.Pixels.Span;
        var bestStart = 0;
        var bestLength = 0;
        var runStart = 0;

        for (var i = 1; i < length; i++)
        {
            if (AreClose(pixels, offsetAt(i), offsetAt(runStart), tolerance))
            {
                continue;
            }

            var runLength = i - runStart;
            if (runLength > bestLength)
            {
                bestStart = runStart;
                bestLength = runLength;
            }

            runStart = i;
        }

        var finalLength = length - runStart;
        if (finalLength > bestLength)
        {
            bestStart = runStart;
            bestLength = finalLength;
        }

        return new EdgeRun(bestStart, bestLength, offsetAt(bestStart));
    }

    private static bool IsQualified(int runLength, int edgeLength, double threshold)
        => runLength >= edgeLength * threshold - 1e-9;

    private static int GetTopDepth(ImagePixelData source, EdgeRun run, int tolerance)
    {
        var pixels = source.Pixels.Span;
        var depths = new List<int>(run.Length);

        for (var x = run.Start; x < run.Start + run.Length; x++)
        {
            var depth = 0;
            var offset = x * 4;
            while (depth < source.Height && AreClose(pixels, offset, run.AnchorOffset, tolerance))
            {
                depth++;
                offset += source.Stride;
            }

            depths.Add(depth);
        }

        return GetDominantDepth(depths);
    }

    private static int GetBottomDepth(ImagePixelData source, EdgeRun run, int tolerance)
    {
        var pixels = source.Pixels.Span;
        var depths = new List<int>(run.Length);

        for (var x = run.Start; x < run.Start + run.Length; x++)
        {
            var depth = 0;
            var offset = (source.Height - 1) * source.Stride + x * 4;
            while (depth < source.Height && AreClose(pixels, offset, run.AnchorOffset, tolerance))
            {
                depth++;
                offset -= source.Stride;
            }

            depths.Add(depth);
        }

        return GetDominantDepth(depths);
    }

    private static int GetLeftDepth(ImagePixelData source, EdgeRun run, int tolerance)
    {
        var pixels = source.Pixels.Span;
        var depths = new List<int>(run.Length);

        for (var y = run.Start; y < run.Start + run.Length; y++)
        {
            var depth = 0;
            var offset = y * source.Stride;
            while (depth < source.Width && AreClose(pixels, offset, run.AnchorOffset, tolerance))
            {
                depth++;
                offset += 4;
            }

            depths.Add(depth);
        }

        return GetDominantDepth(depths);
    }

    private static int GetRightDepth(ImagePixelData source, EdgeRun run, int tolerance)
    {
        var pixels = source.Pixels.Span;
        var depths = new List<int>(run.Length);

        for (var y = run.Start; y < run.Start + run.Length; y++)
        {
            var depth = 0;
            var offset = y * source.Stride + (source.Width - 1) * 4;
            while (depth < source.Width && AreClose(pixels, offset, run.AnchorOffset, tolerance))
            {
                depth++;
                offset -= 4;
            }

            depths.Add(depth);
        }

        return GetDominantDepth(depths);
    }

    private static int GetDominantDepth(IReadOnlyList<int> depths)
    {
        var counts = new Dictionary<int, int>();
        var bestDepth = 0;
        var bestCount = -1;

        foreach (var depth in depths)
        {
            counts.TryGetValue(depth, out var count);
            count++;
            counts[depth] = count;

            if (count > bestCount || (count == bestCount && depth < bestDepth))
            {
                bestCount = count;
                bestDepth = depth;
            }
        }

        return bestDepth;
    }

    private static bool AreClose(ReadOnlySpan<byte> pixels, int left, int right, int tolerance)
    {
        for (var i = 0; i < 4; i++)
        {
            if (Math.Abs(pixels[left + i] - pixels[right + i]) > tolerance)
            {
                return false;
            }
        }

        return true;
    }

    private static ImagePixelData Crop(ImagePixelData source, CropRect rect)
    {
        var newStride = rect.Width * 4;
        var cropped = new byte[newStride * rect.Height];
        var sourcePixels = source.Pixels.Span;
        var targetPixels = cropped.AsSpan();

        for (var y = 0; y < rect.Height; y++)
        {
            var sourceOffset = (rect.Top + y) * source.Stride + rect.Left * 4;
            var targetOffset = y * newStride;
            sourcePixels.Slice(sourceOffset, newStride).CopyTo(targetPixels[targetOffset..]);
        }

        return new ImagePixelData(
            rect.Width,
            rect.Height,
            newStride,
            cropped,
            source.DpiX,
            source.DpiY);
    }

    private readonly record struct EdgeRun(int Start, int Length, int AnchorOffset);

    private readonly record struct CropRect(int Left, int Top, int Width, int Height);

    private readonly record struct CropResult(
        bool HasBackground,
        CropRect? Rect,
        IReadOnlyList<ImageEdgeDiagnostics> Diagnostics);
}
