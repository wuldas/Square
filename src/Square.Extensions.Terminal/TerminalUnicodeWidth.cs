using System.Globalization;
using System.Text;

namespace Square.Extensions.Terminal;

internal static class TerminalUnicodeWidth
{
    public static int GetWidth(string text)
    {
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
            width = Math.Max(width, GetWidth(rune));
        return width;
    }

    public static int GetWidth(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format)
            return 0;
        var value = rune.Value;
        return value is >= 0x1100 and <= 0x115f or
            0x231a or 0x231b or 0x2329 or 0x232a or
            >= 0x2e80 and <= 0xa4cf or
            >= 0xac00 and <= 0xd7a3 or
            >= 0xf900 and <= 0xfaff or
            >= 0xfe10 and <= 0xfe19 or
            >= 0xfe30 and <= 0xfe6f or
            >= 0xff01 and <= 0xff60 or
            >= 0xffe0 and <= 0xffe6 or
            >= 0x1f300 and <= 0x1faff or
            >= 0x20000 and <= 0x3fffd ? 2 : 1;
    }
}
