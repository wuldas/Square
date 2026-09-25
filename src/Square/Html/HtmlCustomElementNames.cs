#nullable enable

namespace Square.Html;

/// <summary>WHATWG custom-element name validation shared by the analyzer and AOT runtime.</summary>
public static class HtmlCustomElementNames
{
    public static bool IsValid(string? name)
    {
        if (name == null || name.Length == 0) return false;
        if (name[0] is not (>= 'a' and <= 'z') ||
            name.IndexOf('-') < 0 || name.StartsWith("xml", StringComparison.OrdinalIgnoreCase)) return false;
        if (name is "annotation-xml" or "color-profile" or "font-face" or "font-face-src" or
            "font-face-uri" or "font-face-format" or "font-face-name" or "missing-glyph") return false;
        for (var index = 0; index < name.Length; index++)
        {
            var code = (int)name[index];
            if (char.IsHighSurrogate(name[index]))
            {
                if (index + 1 >= name.Length || !char.IsLowSurrogate(name[index + 1])) return false;
                code = char.ConvertToUtf32(name[index], name[++index]);
            }
            else if (char.IsLowSurrogate(name[index])) return false;
            if (code is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.' or '_' or
                0xB7 or >= 0xC0 and <= 0xD6 or >= 0xD8 and <= 0xF6 or
                >= 0xF8 and <= 0x37D or >= 0x37F and <= 0x1FFF or 0x200C or 0x200D or
                >= 0x203F and <= 0x2040 or >= 0x2070 and <= 0x218F or
                >= 0x2C00 and <= 0x2FEF or >= 0x3001 and <= 0xD7FF or
                >= 0xF900 and <= 0xFDCF or >= 0xFDF0 and <= 0xFFFD or
                >= 0x10000 and <= 0xEFFFF) continue;
            return false;
        }
        return true;
    }
}
