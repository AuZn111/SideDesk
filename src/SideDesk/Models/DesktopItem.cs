using System.Globalization;
using System.Text;
using System.Windows.Media;

namespace SideDesk.Models;

public sealed class DesktopItem
{
    public required string Path { get; init; }
    public required string DisplayName { get; init; }
    public required string ShortName { get; init; }
    public required string ToolTipText { get; init; }
    public required string KindLabel { get; init; }
    public required int KindRank { get; init; }
    public required bool IsFolder { get; init; }
    public ImageSource? Icon { get; init; }

    public static string MakeShortName(string name, int budget = 24)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var length = new StringInfo(name).LengthInTextElements;
        if (length <= 2)
        {
            return name;
        }

        var used = 0.0;
        var take = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(name);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            var width = IsWide(element) ? 1.85 : 1.0;
            if (used + width > budget)
            {
                break;
            }

            used += width;
            take++;
        }

        if (take >= length)
        {
            return name;
        }

        var prefix = new StringInfo(name).SubstringByTextElements(0, Math.Max(1, take)).TrimEnd();
        return prefix + "…";
    }

    private static bool IsWide(string textElement)
    {
        if (string.IsNullOrEmpty(textElement))
        {
            return false;
        }

        var rune = Rune.GetRuneAt(textElement, 0);
        var value = rune.Value;
        return (value >= 0x1100 && value <= 0x115F)
               || (value >= 0x2E80 && value <= 0xA4CF)
               || (value >= 0xAC00 && value <= 0xD7A3)
               || (value >= 0xF900 && value <= 0xFAFF)
               || (value >= 0xFE10 && value <= 0xFE6F)
               || (value >= 0xFF00 && value <= 0xFF60)
               || (value >= 0x1F300 && value <= 0x1FAFF);
    }
}
