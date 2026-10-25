using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using MediaImageSource = System.Windows.Media.ImageSource;
using SideDesk.Interop;

namespace SideDesk.Services;

internal static class TrayIconFactory
{
    public static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var path = CreateRoundedRectangle(new RectangleF(4, 4, 56, 56), 17);
            using var gradient = new LinearGradientBrush(
                new PointF(6, 6),
                new PointF(58, 58),
                Color.FromArgb(255, 139, 92, 246),
                Color.FromArgb(255, 34, 211, 238));
            graphics.FillPath(gradient, path);

            using var white = new SolidBrush(Color.FromArgb(246, 255, 255, 255));
            using var soft = new SolidBrush(Color.FromArgb(205, 255, 255, 255));
            using var faint = new SolidBrush(Color.FromArgb(150, 255, 255, 255));
            graphics.FillRectangle(white, 21, 22, 24, 4.5f);
            graphics.FillRectangle(soft, 21, 30, 17, 4.5f);
            graphics.FillRectangle(faint, 21, 38, 10, 4.5f);
        }

        var hIcon = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(hIcon);
            return (Icon)temporary.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    public static MediaImageSource CreateImageSource()
    {
        using var icon = CreateIcon();
        var source = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromWidthAndHeight(64, 64));
        source.Freeze();
        return source;
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
}
