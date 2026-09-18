using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace LinkDetect.Services;

internal static class AppIconFactory
{
    private const int IconSize = 32;

    public static Icon CreateTrayIcon(bool paused)
    {
        using var bitmap = new Bitmap(IconSize, IconSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);

        var baseColor = paused ? Color.FromArgb(100, 116, 139) : Color.FromArgb(97, 141, 255);

        using (var backgroundPath = CreateRoundedRectangle(new RectangleF(2, 2, 28, 28), 8))
        using (var backgroundBrush = new SolidBrush(baseColor))
        {
            graphics.FillPath(backgroundBrush, backgroundPath);
        }

        graphics.TranslateTransform(16, 16);
        graphics.RotateTransform(-45);

        using var linkPen = new Pen(Color.White, 4)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        graphics.DrawEllipse(linkPen, -11, -5f, 10, 10);
        graphics.DrawEllipse(linkPen, 1, -5f, 10, 10);
        graphics.DrawLine(linkPen, -3, 0, 3, 0);

        graphics.ResetTransform();

        var nativeHandle = bitmap.GetHicon();
        try
        {
            using var temporaryIcon = Icon.FromHandle(nativeHandle);
            return (Icon)temporaryIcon.Clone();
        }
        finally
        {
            DestroyIcon(nativeHandle);
        }
    }

    private static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);
}
