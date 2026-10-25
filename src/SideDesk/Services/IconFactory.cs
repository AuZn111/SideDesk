using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SideDesk.Services;

internal static class IconFactory
{
    public static ImageSource CreateFallback(string name, bool isFolder)
    {
        const int size = 128;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var colors = PickColors(name);
            var background = new LinearGradientBrush(colors.Item1, colors.Item2, 45);
            var bounds = new Rect(7, 7, size - 14, size - 14);
            dc.DrawRoundedRectangle(
                background,
                new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 2),
                bounds,
                25,
                25);

            if (isFolder)
            {
                var folder = new StreamGeometry();
                using (var ctx = folder.Open())
                {
                    ctx.BeginFigure(new Point(32, 50), true, true);
                    ctx.LineTo(new Point(55, 50), true, false);
                    ctx.LineTo(new Point(64, 61), true, false);
                    ctx.LineTo(new Point(96, 61), true, false);
                    ctx.LineTo(new Point(96, 86), true, false);
                    ctx.LineTo(new Point(32, 86), true, false);
                }

                folder.Freeze();
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(232, 255, 255, 255)), null, folder);
            }
            else
            {
                var initial = GetInitial(name);
                var typeface = new Typeface(
                    new FontFamily("Segoe UI"),
                    FontStyles.Normal,
                    FontWeights.SemiBold,
                    FontStretches.Normal);
                var text = new FormattedText(
                    initial,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    52,
                    new SolidColorBrush(Color.FromArgb(238, 255, 255, 255)),
                    1.0);
                dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2 - 4));
            }
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static string GetInitial(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "?";
        }

        var info = new StringInfo(name.Trim());
        var first = info.SubstringByTextElements(0, 1);
        return first.ToUpperInvariant();
    }

    private static (Color, Color) PickColors(string name)
    {
        var hash = 17;
        foreach (var ch in name)
        {
            hash = unchecked(hash * 31 + ch);
        }

        var colors = new (Color Top, Color Bottom)[]
        {
            (Color.FromRgb(124, 92, 255), Color.FromRgb(67, 56, 202)),
            (Color.FromRgb(34, 211, 238), Color.FromRgb(14, 116, 144)),
            (Color.FromRgb(251, 146, 60), Color.FromRgb(194, 65, 12)),
            (Color.FromRgb(52, 211, 153), Color.FromRgb(5, 150, 105)),
            (Color.FromRgb(244, 114, 182), Color.FromRgb(190, 24, 93)),
            (Color.FromRgb(96, 165, 250), Color.FromRgb(37, 99, 235))
        };

        return colors[(hash & 0x7FFFFFFF) % colors.Length];
    }
}
