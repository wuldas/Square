using System.Globalization;
using Square.CSS.Tokenizer;
using Square.CSS.Values;
using Square.Graphics;

namespace Square.CSS.Properties;

internal readonly record struct CssPropertyDefinition(
    string InitialValue,
    bool Inherited,
    Func<string, bool> Validate);

internal static class CssPropertyRegistry
{
    private static readonly Dictionary<string, CssPropertyDefinition> Properties = CreateProperties();
    internal static IReadOnlyList<string> InheritedPropertyNames { get; } = Properties
        .Where(pair => pair.Value.Inherited)
        .Select(pair => pair.Key)
        .ToArray();
    private static readonly string[] LengthUnits = ["rem", "px", "em", "ex", "pt", "pc", "in", "cm", "mm", "vw", "vh", "rp"];
    private static readonly string[] LengthUnitsWithPercent = [.. LengthUnits, "%"];

    public static bool TryGet(string property, out CssPropertyDefinition definition) =>
        Properties.TryGetValue(property, out definition);

    public static bool IsInherited(string property) =>
        Properties.TryGetValue(property, out var definition) && definition.Inherited;

    public static string? GetInitialValue(string property) =>
        Properties.TryGetValue(property, out var definition) ? definition.InitialValue : null;

    public static bool IsValid(string property, string value)
    {
        value = value.Trim();
        if (value.Length == 0) return false;
        if (!Properties.TryGetValue(property, out var definition)) return true;
        return CssValueSyntax.IsGlobalKeyword(value) || CssValueSyntax.ContainsVariable(value) || definition.Validate(value);
    }

    private static Dictionary<string, CssPropertyDefinition> CreateProperties()
    {
        var properties = new Dictionary<string, CssPropertyDefinition>(StringComparer.Ordinal);

        void Add(string name, string initial, bool inherited, Func<string, bool> validate) =>
            properties[name] = new CssPropertyDefinition(initial, inherited, validate);

        void AddMany(IEnumerable<string> names, string initial, bool inherited, Func<string, bool> validate)
        {
            foreach (var name in names) Add(name, initial, inherited, validate);
        }

        Add("background", "transparent", false, Any);
        Add("background-attachment", "scroll", false, value => IsKeyword(value, "scroll", "fixed"));
        Add("background-color", "transparent", false, IsColor);
        Add("background-image", "none", false, IsImage);
        Add("background-position", "0% 0%", false, IsBackgroundPosition);
        Add("background-repeat", "repeat", false, value => IsKeyword(value, "repeat", "repeat-x", "repeat-y", "no-repeat"));
        Add("border", "medium none currentcolor", false, Any);
        Add("border-collapse", "separate", true, value => IsKeyword(value, "collapse", "separate"));
        Add("border-color", "currentcolor", false, Any);
        Add("border-spacing", "0", true, IsOneOrTwoLengths);
        Add("border-style", "none", false, Any);
        Add("border-width", "medium", false, Any);
        Add("border-radius", "0", false, CssBorderRadiusParser.IsValid);
        foreach (var side in new[] { "top", "right", "bottom", "left" })
        {
            Add($"border-{side}", "medium none currentcolor", false, Any);
            Add($"border-{side}-color", "currentcolor", false, IsColor);
            Add($"border-{side}-style", "none", false, IsBorderStyle);
            Add($"border-{side}-width", "medium", false, IsBorderWidth);
        }
        foreach (var corner in new[] { "top-left", "top-right", "bottom-right", "bottom-left" })
            Add($"border-{corner}-radius", "0", false, CssBorderRadiusParser.IsCornerValid);
        Add("bottom", "auto", false, IsInset);
        Add("box-sizing", "content-box", false, value => IsKeyword(value, "content-box", "border-box"));
        Add("appearance", "none", false, value => IsKeyword(value, "none", "auto"));
        Add("caption-side", "top", true, value => IsKeyword(value, "top", "bottom"));
        Add("clear", "none", false, value => IsKeyword(value, "none", "left", "right", "both"));
        Add("clip", "auto", false, value => IsKeyword(value, "auto") || IsFunction(value, "rect"));
        Add("color", "black", true, IsColor);
        Add("content", "normal", false, Any);
        Add("counter-increment", "none", false, Any);
        Add("counter-reset", "none", false, Any);
        Add("cursor", "auto", true, Any);
        Add("direction", "ltr", true, value => IsKeyword(value, "ltr", "rtl"));
        Add("display", "inline", false, value => IsKeyword(value,
            "none", "inline", "block", "list-item", "inline-block", "table", "inline-table", "table-row-group",
            "table-header-group", "table-footer-group", "table-row", "table-column-group", "table-column",
            "table-cell", "table-caption", "flex", "grid"));
        Add("empty-cells", "show", true, value => IsKeyword(value, "show", "hide"));
        Add("float", "none", false, value => IsKeyword(value, "none", "left", "right"));
        Add("font", "normal normal normal medium/normal sans-serif", true, Any);
        Add("font-family", "sans-serif", true, IsFontFamily);
        Add("font-size", "medium", true, IsFontSize);
        Add("font-style", "normal", true, value => IsKeyword(value, "normal", "italic", "oblique"));
        Add("font-variant", "normal", true, value => IsKeyword(value, "normal", "small-caps"));
        Add("font-weight", "normal", true, IsFontWeight);
        Add("height", "auto", false, IsDimension);
        Add("left", "auto", false, IsInset);
        Add("letter-spacing", "normal", true, value => IsKeyword(value, "normal") || IsLength(value));
        Add("line-height", "normal", true, IsLineHeight);
        Add("list-style", "disc outside none", true, Any);
        Add("list-style-image", "none", true, IsImage);
        Add("list-style-position", "outside", true, value => IsKeyword(value, "inside", "outside"));
        Add("list-style-type", "disc", true, IsListStyleType);
        Add("margin", "0", false, Any);
        AddMany(new[] { "margin-top", "margin-right", "margin-bottom", "margin-left" }, "0", false, IsMargin);
        AddMany(new[] { "max-width", "max-height" }, "none", false,
            value => IsKeyword(value, "none") || IsNonNegativeLength(value, allowPercent: true));
        AddMany(new[] { "min-width", "min-height" }, "0", false,
            value => IsNonNegativeLength(value, allowPercent: true));
        Add("opacity", "1", false, IsOpacity);
        Add("orphans", "2", true, IsPositiveInteger);
        Add("outline", "invert none medium", false, Any);
        Add("outline-color", "invert", false, value => IsKeyword(value, "invert") || IsColor(value));
        Add("outline-style", "none", false, IsBorderStyle);
        Add("outline-width", "medium", false, IsBorderWidth);
        Add("outline-offset", "0", false, value => IsLength(value) || IsKeyword(value, "0"));
        Add("overflow", "visible", false, value => IsKeyword(value, "visible", "hidden", "scroll", "auto", "clip"));
        AddMany(new[] { "overflow-x", "overflow-y" }, "visible", false,
            value => IsKeyword(value, "visible", "hidden", "scroll", "auto", "clip"));
        Add("scrollbar-gutter", "auto", false, value => IsKeyword(value, "auto", "stable", "stable both-edges"));
        Add("scrollbar-color", "auto", true, IsScrollbarColor);
        Add("scrollbar-width", "auto", false, value => IsKeyword(value, "auto", "thin", "none"));
        Add("padding", "0", false, Any);
        AddMany(new[] { "padding-top", "padding-right", "padding-bottom", "padding-left" }, "0", false,
            value => IsNonNegativeLength(value, allowPercent: true));
        Add("page-break-after", "auto", false, IsPageBreak);
        Add("page-break-before", "auto", false, IsPageBreak);
        Add("page-break-inside", "auto", false, value => IsKeyword(value, "auto", "avoid"));
        Add("position", "static", false, value => IsKeyword(value, "static", "relative", "absolute", "fixed"));
        Add("quotes", "none", true, Any);
        Add("right", "auto", false, IsInset);
        Add("table-layout", "auto", false, value => IsKeyword(value, "auto", "fixed"));
        Add("text-align", "left", true, value => IsKeyword(value, "left", "right", "center", "justify", "start", "end"));
        Add("text-decoration", "none", false, IsTextDecoration);
        Add("text-decoration-line", "none", false, IsTextDecoration);
        Add("text-indent", "0", true, value => IsLength(value, allowPercent: true));
        Add("text-transform", "none", true, value => IsKeyword(value, "none", "capitalize", "uppercase", "lowercase"));
        Add("top", "auto", false, IsInset);
        Add("unicode-bidi", "normal", false, value => IsKeyword(value, "normal", "embed", "bidi-override"));
        Add("vertical-align", "baseline", false, IsVerticalAlign);
        Add("visibility", "visible", true, value => IsKeyword(value, "visible", "hidden", "collapse"));
        Add("white-space", "normal", true, value => IsKeyword(value, "normal", "pre", "nowrap", "pre-wrap", "pre-line"));
        Add("widows", "2", true, IsPositiveInteger);
        Add("width", "auto", false, IsDimension);
        Add("word-spacing", "normal", true, value => IsKeyword(value, "normal") || IsLength(value));
        Add("z-index", "auto", false, value => IsKeyword(value, "auto") || IsInteger(value));

        return properties;
    }

    private static bool Any(string value) => value.Trim().Length > 0;

    private static bool IsKeyword(string value, params string[] keywords) =>
        keywords.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    private static bool IsFunction(string value, string name) =>
        value.Trim().StartsWith(name + "(", StringComparison.OrdinalIgnoreCase) && value.Trim().EndsWith(')');

    private static bool IsColor(string value)
    {
        value = value.Trim();
        if (value.StartsWith('#'))
            return value.Length is 4 or 5 or 7 or 9 && value[1..].All(Uri.IsHexDigit);
        if (IsFunction(value, "rgb") || IsFunction(value, "rgba") || IsFunction(value, "hsl") || IsFunction(value, "hsla"))
            return true;
        return value.All(c => char.IsLetter(c) || c == '-');
    }

    private static bool IsScrollbarColor(string value)
    {
        if (IsKeyword(value, "auto")) return true;
        return CssValueSyntax.TrySplitWhitespace(value, out var tokens) &&
            tokens.Length == 2 && tokens.All(token => Color.TryParse(token, out _));
    }

    private static bool IsImage(string value) =>
        IsKeyword(value, "none") || IsUrlFunction(value);

    private static bool IsFontFamily(string value)
    {
        var tokens = new CssTokenizer(value).Tokenize();
        var families = new List<List<CssToken>>();
        var current = new List<CssToken>();

        foreach (var token in tokens)
        {
            if (token.Type is CssTokenType.Whitespace or CssTokenType.Eof) continue;
            if (token.Type == CssTokenType.Comma)
            {
                if (current.Count == 0) return false;
                families.Add(current);
                current = [];
                continue;
            }
            current.Add(token);
        }

        if (current.Count == 0) return false;
        families.Add(current);

        foreach (var family in families)
        {
            if (family.Count == 1 && family[0].Type == CssTokenType.String) continue;
            if (family.Any(token => token.Type != CssTokenType.Identifier)) return false;
            if (family.Any(token => token.Text.Equals("default", StringComparison.OrdinalIgnoreCase))) return false;
            if (family.Any(token => IsFontFamilyGlobalKeyword(token.Text)))
                return families.Count == 1 && family.Count == 1;
        }
        return true;
    }

    private static bool IsFontFamilyGlobalKeyword(string value) => value.Equals("inherit", StringComparison.OrdinalIgnoreCase) ||
                                                                    value.Equals("initial", StringComparison.OrdinalIgnoreCase) ||
                                                                    value.Equals("unset", StringComparison.OrdinalIgnoreCase) ||
                                                                    value.Equals("default", StringComparison.OrdinalIgnoreCase);

    private static bool IsUrlFunction(string value)
    {
        var tokens = new CssTokenizer(value).Tokenize();
        var index = 0;
        while (tokens[index].Type == CssTokenType.Whitespace) index++;
        if (tokens[index].Type != CssTokenType.Identifier ||
            !tokens[index].Text.Equals("url", StringComparison.OrdinalIgnoreCase)) return false;
        index++;
        if (tokens[index].Type != CssTokenType.OpenParen) return false;

        var depth = 0;
        for (; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.Type == CssTokenType.OpenParen) depth++;
            else if (token.Type == CssTokenType.CloseParen && --depth == 0)
            {
                index++;
                while (tokens[index].Type == CssTokenType.Whitespace) index++;
                return tokens[index].Type == CssTokenType.Eof;
            }
        }
        return false;
    }

    private static bool IsBackgroundPosition(string value)
    {
        if (!CssValueSyntax.TrySplitWhitespace(value, out var tokens) || tokens.Length is < 1 or > 2) return false;
        return tokens.All(token => IsKeyword(token, "left", "center", "right", "top", "bottom") || IsLength(token, allowPercent: true));
    }

    private static bool IsOneOrTwoLengths(string value) =>
        CssValueSyntax.TrySplitWhitespace(value, out var tokens) && tokens.Length is 1 or 2 && tokens.All(IsNonNegativeLength);

    private static bool IsBorderStyle(string value) => IsKeyword(value,
        "none", "hidden", "dotted", "dashed", "solid", "double", "groove", "ridge", "inset", "outset");

    private static bool IsBorderWidth(string value) =>
        IsKeyword(value, "thin", "medium", "thick") || IsNonNegativeLength(value);

    private static bool IsInset(string value) => IsKeyword(value, "auto") || IsLength(value, allowPercent: true);

    private static bool IsDimension(string value) =>
        IsKeyword(value, "auto") || IsNonNegativeLength(value, allowPercent: true);

    private static bool IsMargin(string value) =>
        IsKeyword(value, "auto") || IsLength(value, allowPercent: true);

    private static bool IsFontSize(string value) => IsKeyword(value,
        "xx-small", "x-small", "small", "medium", "large", "x-large", "xx-large", "larger", "smaller") ||
        IsNonNegativeLength(value, allowPercent: true);

    private static bool IsFontWeight(string value)
    {
        if (IsKeyword(value, "normal", "bold", "bolder", "lighter")) return true;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var weight) &&
               weight is >= 100 and <= 900 && weight % 100 == 0;
    }

    private static bool IsLineHeight(string value) =>
        IsKeyword(value, "normal") || IsNonNegativeNumber(value) || IsNonNegativeLength(value, allowPercent: true);

    private static bool IsListStyleType(string value) => IsKeyword(value,
        "none", "disc", "circle", "square", "decimal", "decimal-leading-zero", "lower-roman", "upper-roman",
        "lower-greek", "lower-latin", "upper-latin", "armenian", "georgian", "lower-alpha", "upper-alpha");

    private static bool IsOpacity(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity) && opacity is >= 0 and <= 1;

    private static bool IsPositiveInteger(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0;

    private static bool IsInteger(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    private static bool IsPageBreak(string value) => IsKeyword(value, "auto", "always", "avoid", "left", "right");

    private static bool IsTextDecoration(string value)
    {
        if (!CssValueSyntax.TrySplitWhitespace(value, out var tokens) || tokens.Length == 0) return false;
        return tokens.All(token => IsKeyword(token, "none", "underline", "overline", "line-through", "blink"));
    }

    private static bool IsVerticalAlign(string value) => IsKeyword(value,
        "baseline", "sub", "super", "top", "text-top", "middle", "bottom", "text-bottom") ||
        IsLength(value, allowPercent: true);

    private static bool IsNonNegativeNumber(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number >= 0;

    private static bool IsNonNegativeLength(string value) => IsNonNegativeLength(value, allowPercent: false);

    private static bool IsNonNegativeLength(string value, bool allowPercent) =>
        IsLength(value, allowPercent, allowNegative: false);

    private static bool IsLength(string value) => IsLength(value, allowPercent: false);

    private static bool IsLength(string value, bool allowPercent) => IsLength(value, allowPercent, allowNegative: true);

    private static bool IsLength(string value, bool allowPercent, bool allowNegative)
    {
        value = value.Trim();
        if (value == "0") return true;
        var units = allowPercent ? LengthUnitsWithPercent : LengthUnits;
        var unit = units.FirstOrDefault(unit => value.EndsWith(unit, StringComparison.OrdinalIgnoreCase));
        if (unit == null ||
            !double.TryParse(value[..^unit.Length], NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
            !double.IsFinite(number))
            return false;
        if (!allowNegative && number < 0) return false;
        return true;
    }
}
