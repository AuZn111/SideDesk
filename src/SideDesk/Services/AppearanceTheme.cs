using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SideDesk.Services;

internal static class AppearanceTheme
{
    public static readonly Color DefaultAccent = Color.FromRgb(0x8B, 0x5C, 0xF6);

    public static Color GetAccent(AppSettings settings)
        => ColorUtil.Parse(settings.SidebarColor, DefaultAccent);

    public static void ApplySidebar(
        AppSettings settings,
        Border panel,
        Border logo,
        Border arrow,
        Border accentDot)
    {
        var accent = GetAccent(settings);
        var opacity = Math.Clamp(settings.SidebarOpacity, 0.35, 1.0);

        panel.Background = CreatePanelGradient(accent, opacity);
        panel.BorderBrush = ColorUtil.Brush(ColorUtil.WithAlpha(ColorUtil.Lighten(accent, 0.55), 0.28));
        logo.Background = CreateAccentGradient(accent);
        arrow.Background = CreateArrowGradient(accent);
        arrow.BorderBrush = ColorUtil.Brush(ColorUtil.WithAlpha(ColorUtil.Lighten(accent, 0.55), 0.48));
        accentDot.Background = ColorUtil.Brush(accent);

        UpdateResources(accent);
    }

    public static LinearGradientBrush CreatePanelGradient(Color accent, double opacity)
    {
        var alpha = Math.Clamp(opacity, 0.35, 1.0);
        var top = ColorUtil.Blend(Color.FromRgb(0x1B, 0x1E, 0x29), accent, 0.30);
        var middle = ColorUtil.Blend(Color.FromRgb(0x14, 0x17, 0x22), accent, 0.20);
        var bottom = ColorUtil.Blend(Color.FromRgb(0x0B, 0x0D, 0x13), accent, 0.09);

        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(top, alpha), 0));
        brush.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(middle, alpha), 0.54));
        brush.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(bottom, alpha), 1));
        brush.Freeze();
        return brush;
    }

    public static LinearGradientBrush CreateAccentGradient(Color accent)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop(ColorUtil.Lighten(accent, 0.32), 0));
        brush.GradientStops.Add(new GradientStop(ColorUtil.Darken(accent, 0.16), 1));
        brush.Freeze();
        return brush;
    }

    public static LinearGradientBrush CreateArrowGradient(Color accent)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(accent, 0.78), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xF2, 0x19, 0x1C, 0x27), 1));
        brush.Freeze();
        return brush;
    }

    public static void UpdateResources(Color accent)
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        resources["AccentBrush"] = ColorUtil.Brush(accent);
        resources["AccentSoftBrush"] = ColorUtil.Brush(ColorUtil.Lighten(accent, 0.42));
        resources["AccentGradient"] = CreateAccentGradient(accent);
        resources["AccentFillBrush"] = ColorUtil.Brush(ColorUtil.WithAlpha(accent, 0.17));
        resources["TileSelectedBrush"] = ColorUtil.Brush(ColorUtil.WithAlpha(accent, 0.14));
        resources["TileSelectedBorderBrush"] = ColorUtil.Brush(ColorUtil.WithAlpha(accent, 0.36));
        resources["TextSelectionBrush"] = ColorUtil.Brush(ColorUtil.WithAlpha(accent, 0.42));
        resources["ArrowGradient"] = CreateArrowGradient(accent);
        resources["ArrowBorderBrush"] = ColorUtil.Brush(ColorUtil.WithAlpha(ColorUtil.Lighten(accent, 0.55), 0.48));
    }
}
