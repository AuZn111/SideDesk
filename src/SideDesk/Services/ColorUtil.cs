using System.Globalization;
using System.Windows.Media;

namespace SideDesk.Services;

internal static class ColorUtil
{
    public static Color Parse(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return fallback;
        }

        var text = hex.Trim();
        if (text.StartsWith('#'))
        {
            text = text[1..];
        }

        if (text.Length == 3)
        {
            text = string.Concat(text[0], text[0], text[1], text[1], text[2], text[2]);
        }

        if (text.Length != 6
            || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return fallback;
        }

        return Color.FromRgb(
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF));
    }

    public static string NormalizeHex(string? hex, string fallback)
    {
        var fallbackColor = Parse(fallback, Color.FromRgb(0x8B, 0x5C, 0xF6));
        var color = Parse(hex, fallbackColor);
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    public static bool TryNormalizeHex(string? hex, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            normalized = string.Empty;
            return false;
        }

        var text = hex.Trim();
        if (text.StartsWith('#'))
        {
            text = text[1..];
        }

        if (text.Length == 3)
        {
            text = string.Concat(text[0], text[0], text[1], text[1], text[2], text[2]);
        }

        if (text.Length != 6
            || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            normalized = string.Empty;
            return false;
        }

        var red = (byte)((value >> 16) & 0xFF);
        var green = (byte)((value >> 8) & 0xFF);
        var blue = (byte)(value & 0xFF);
        normalized = $"#{red:X2}{green:X2}{blue:X2}";
        return true;
    }

    public static Color Blend(Color from, Color to, double amount)
    {
        var t = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(from.R + (to.R - from.R) * t),
            (byte)Math.Round(from.G + (to.G - from.G) * t),
            (byte)Math.Round(from.B + (to.B - from.B) * t));
    }

    public static Color Lighten(Color color, double amount)
        => Blend(color, Colors.White, amount);

    public static Color Darken(Color color, double amount)
        => Blend(color, Colors.Black, amount);

    public static Color WithAlpha(Color color, double opacity)
    {
        var alpha = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    public static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
