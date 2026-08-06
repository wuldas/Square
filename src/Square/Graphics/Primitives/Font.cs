using System.Globalization;

namespace Square.Graphics;

/// <summary>字重（对齐 CSS <c>font-weight</c> 数值）。</summary>
public enum FontWeight : ushort
{
    /// <summary>100，极细。</summary>
    Thin = 100,
    /// <summary>200，特轻。</summary>
    ExtraLight = 200,
    /// <summary>300，轻。</summary>
    Light = 300,
    /// <summary>400，常规。</summary>
    Normal = 400,
    /// <summary>500，中等。</summary>
    Medium = 500,
    /// <summary>600，半粗。</summary>
    SemiBold = 600,
    /// <summary>700，粗体。</summary>
    Bold = 700,
    /// <summary>800，特粗。</summary>
    ExtraBold = 800,
    /// <summary>900，黑体。</summary>
    Black = 900
}

/// <summary>字体样式（对齐 CSS <c>font-style</c>）。</summary>
public enum FontStyle : byte
{
    /// <summary>常规直立。</summary>
    Normal,
    /// <summary>斜体（使用字体斜体字形）。</summary>
    Italic,
    /// <summary>倾斜（合成倾斜）。</summary>
    Oblique
}

/// <summary>文本对齐（对齐 CSS <c>text-align</c> 子集）。</summary>
public enum TextAlignment : byte
{
    /// <summary>左对齐。</summary>
    Left,
    /// <summary>居中对齐。</summary>
    Center,
    /// <summary>右对齐。</summary>
    Right,
    /// <summary>两端对齐。</summary>
    Justify
}

/// <summary>
/// 字体描述（绘图原语）。
/// 属性语义对齐 CSS Fonts：family / size / weight / style。
/// 从元素样式解析请使用 <see cref="Font.FromCss"/> 或 <see cref="Font.ResolveFromStyle"/>。
/// </summary>
public sealed class Font
{
    /// <summary>已解析的主字体族名（不含 CSS 列表语法）。</summary>
    public string Family { get; set; } = "sans-serif";

    /// <summary>字号（CSS px 逻辑像素）。</summary>
    public float Size { get; set; } = 16f;

    /// <summary>字重。</summary>
    public FontWeight Weight { get; set; } = FontWeight.Normal;

    /// <summary>斜体/倾斜。</summary>
    public FontStyle Style { get; set; } = FontStyle.Normal;

    /// <summary>使用默认 sans-serif 16px。</summary>
    public Font() { }

    /// <summary>指定族名与字号。</summary>
    public Font(string family, float size)
    {
        Family = family;
        Size = size;
    }

    /// <summary>指定族名、字号、字重与样式。</summary>
    public Font(string family, float size, FontWeight weight, FontStyle style = FontStyle.Normal)
    {
        Family = family;
        Size = size;
        Weight = weight;
        Style = style;
    }

    /// <summary>复制并改字号。</summary>
    public Font WithSize(float size) => new(Family, size, Weight, Style);

    /// <summary>复制并改字重。</summary>
    public Font WithWeight(FontWeight weight) => new(Family, Size, weight, Style);

    /// <summary>
    /// 解析 CSS <c>font-family</c> 列表，返回按优先级排列的族名（去掉引号）。
    /// 例：<c>"Segoe UI", Tahoma, sans-serif</c>。
    /// </summary>
    public static IReadOnlyList<string> ParseFamilyList(string? fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
            return ["sans-serif"];

        return ParseFamilyEntries(fontFamily).Select(entry => entry.Name).ToArray();
    }

    private static IReadOnlyList<(string Name, bool Quoted)> ParseFamilyEntries(string fontFamily)
    {
        var result = new List<(string Name, bool Quoted)>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        var quoted = false;
        char quoteChar = '\0';

        for (var i = 0; i < fontFamily.Length; i++)
        {
            var c = fontFamily[i];
            if (inQuotes)
            {
                if (c == '\\' && i + 1 < fontFamily.Length)
                {
                    current.Append(fontFamily[++i]);
                    continue;
                }
                if (c == quoteChar) inQuotes = false;
                else current.Append(c);
                continue;
            }

            if (c is '"' or '\'')
            {
                inQuotes = true;
                quoted = true;
                quoteChar = c;
                continue;
            }

            if (c == ',')
            {
                AddFamilyToken(result, current, quoted);
                quoted = false;
                continue;
            }

            current.Append(c);
        }

        AddFamilyToken(result, current, quoted);
        return result.Count > 0 ? result : [("sans-serif", false)];
    }

    /// <summary>解析 CSS <c>font-weight</c>（normal/bold/lighter/bolder/100–900）。</summary>
    public static FontWeight ParseWeight(string? value, FontWeight fallback = FontWeight.Normal)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        value = value.Trim();
        if (string.Equals(value, "normal", StringComparison.OrdinalIgnoreCase)) return FontWeight.Normal;
        if (string.Equals(value, "bold", StringComparison.OrdinalIgnoreCase)) return FontWeight.Bold;
        if (string.Equals(value, "lighter", StringComparison.OrdinalIgnoreCase)) return FontWeight.Light;
        if (string.Equals(value, "bolder", StringComparison.OrdinalIgnoreCase)) return FontWeight.Bold;
        if (ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            n = (ushort)(Math.Clamp(n, (ushort)100, (ushort)900) / 100 * 100);
            return (FontWeight)n;
        }
        return fallback;
    }

    /// <summary>解析 CSS <c>font-style</c>。</summary>
    public static FontStyle ParseStyle(string? value, FontStyle fallback = FontStyle.Normal)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        value = value.Trim();
        if (string.Equals(value, "italic", StringComparison.OrdinalIgnoreCase)) return FontStyle.Italic;
        if (string.Equals(value, "oblique", StringComparison.OrdinalIgnoreCase)) return FontStyle.Oblique;
        return FontStyle.Normal;
    }

    /// <summary>解析 CSS <c>font-size</c>（支持 px 与纯数字，单位逻辑像素）。</summary>
    public static float ParseSize(string? value, float fallback = 16f)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        value = value.Trim();
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            value = value[..^2].Trim();
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && size > 0
            ? size
            : fallback;
    }

    /// <summary>
    /// 从 CSS 字体相关属性构造 <see cref="Font"/>。
    /// <paramref name="resolveFamily"/> 将 CSS 族名/通用族映射为系统可用族（缺省时通用族映射到常见默认）。
    /// </summary>
    public static Font FromCss(
        string? fontFamily,
        string? fontSize,
        string? fontWeight = null,
        string? fontStyle = null,
        float defaultSize = 16f,
        Func<string, string>? resolveFamily = null)
    {
        // CSS font matching 简化：
        // - 若提供 resolveFamily（如 FontManager），对列表逐项解析，优先已知/通用族
        // - 默认：列表中第一项；若为通用族则映射；未知族名保留，直至遇到通用族
        // 无 resolveFamily：取列表第一项；若为通用族则映射。完整回退链由 FontManager 负责。
        string family;
        if (resolveFamily != null)
        {
            var first = ParseFamilyEntries(fontFamily ?? "sans-serif")[0];
            family = first.Quoted && (IsGenericFamily(first.Name) || IsFontFamilyKeyword(first.Name))
                ? first.Name
                : !first.Quoted && IsFontFamilyKeyword(first.Name)
                    ? resolveFamily("sans-serif")
                    : resolveFamily(fontFamily ?? "sans-serif");
        }
        else
        {
            var families = ParseFamilyEntries(fontFamily ?? "sans-serif");
            var first = families[0];
            family = first.Quoted
                ? first.Name
                : IsGenericFamily(first.Name)
                    ? ResolveGenericFamily(first.Name)
                    : IsFontFamilyKeyword(first.Name)
                        ? "sans-serif"
                        : first.Name;
        }

        var size = ParseSize(fontSize, defaultSize);
        var weight = ParseWeight(fontWeight);
        var style = ParseStyle(fontStyle);
        return new Font(family, size, weight, style);
    }

    /// <summary>是否为 CSS 通用字体族关键字。</summary>
    public static bool IsGenericFamily(string family)
    {
        family = family.Trim();
        return string.Equals(family, "sans-serif", StringComparison.OrdinalIgnoreCase)
            || string.Equals(family, "serif", StringComparison.OrdinalIgnoreCase)
            || string.Equals(family, "monospace", StringComparison.OrdinalIgnoreCase)
            || string.Equals(family, "cursive", StringComparison.OrdinalIgnoreCase)
            || string.Equals(family, "fantasy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(family, "system-ui", StringComparison.OrdinalIgnoreCase)
            || string.Equals(family, "ui-sans-serif", StringComparison.OrdinalIgnoreCase)
            || string.Equals(family, "ui-serif", StringComparison.OrdinalIgnoreCase)
            || string.Equals(family, "ui-monospace", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>CSS 通用字体族到默认族名的映射（可被 FontManager 覆盖）。</summary>
    public static string ResolveGenericFamily(string family)
    {
        if (string.Equals(family, "sans-serif", StringComparison.OrdinalIgnoreCase)) return "Segoe UI";
        if (string.Equals(family, "serif", StringComparison.OrdinalIgnoreCase)) return "Times New Roman";
        if (string.Equals(family, "monospace", StringComparison.OrdinalIgnoreCase)) return "Consolas";
        if (string.Equals(family, "cursive", StringComparison.OrdinalIgnoreCase)) return "Segoe Script";
        if (string.Equals(family, "fantasy", StringComparison.OrdinalIgnoreCase)) return "Segoe UI";
        if (string.Equals(family, "system-ui", StringComparison.OrdinalIgnoreCase)) return "Segoe UI";
        if (string.Equals(family, "ui-sans-serif", StringComparison.OrdinalIgnoreCase)) return "Segoe UI";
        if (string.Equals(family, "ui-serif", StringComparison.OrdinalIgnoreCase)) return "Times New Roman";
        if (string.Equals(family, "ui-monospace", StringComparison.OrdinalIgnoreCase)) return "Consolas";
        return family;
    }

    private static bool IsFontFamilyKeyword(string family) => family.Equals("inherit", StringComparison.OrdinalIgnoreCase) ||
                                                               family.Equals("initial", StringComparison.OrdinalIgnoreCase) ||
                                                               family.Equals("unset", StringComparison.OrdinalIgnoreCase) ||
                                                               family.Equals("default", StringComparison.OrdinalIgnoreCase);

    private static void AddFamilyToken(List<(string Name, bool Quoted)> result,
        System.Text.StringBuilder current, bool quoted)
    {
        var token = current.ToString().Trim();
        current.Clear();
        if (token.Length > 0)
            result.Add((token, quoted));
    }
}
