using System.Globalization;
using System.Text.RegularExpressions;
using Square.Compiler.Parser;
using Square.Compiler.Syntax;

namespace Square.Compiler.LanguageServices;

public sealed class TemplateColorPresentation
{
    public TemplateColorPresentation(string label, int start, int length)
    {
        Label = label;
        Start = start;
        Length = length;
    }

    public string Label { get; }
    public int Start { get; }
    public int Length { get; }
}

public sealed class TemplateDocumentColor
{
    public TemplateDocumentColor(int start, int length, double red, double green, double blue, double alpha)
    {
        Start = start;
        Length = length;
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }

    public int Start { get; }
    public int Length { get; }
    public double Red { get; }
    public double Green { get; }
    public double Blue { get; }
    public double Alpha { get; }
}

public static class TemplateColorService
{
    private static readonly Regex HexColor = new(
        @"#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{4}|[0-9A-Fa-f]{3})\b",
        RegexOptions.Compiled);

    private static readonly Regex RgbColor = new(
        @"rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})(?:\s*,\s*(0|1|0?\.\d+))?\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<TemplateDocumentColor> GetColors(string text)
    {
        text ??= string.Empty;
        var colors = new List<TemplateDocumentColor>();
        var document = SquareDocumentService.ParseSyntaxTree(text, "Colors.sqx").ParsedSqxDocument;
        if (document?.Template != null)
        {
            foreach (var element in EnumerateElements(document.Template.Roots))
            {
                foreach (var attribute in element.Attributes.Where(attribute =>
                             attribute.Name.Equals("style", StringComparison.OrdinalIgnoreCase) &&
                             !attribute.IsExpression && attribute.RawValue != null))
                {
                    var valueOffset = FindAttributeValueOffset(text, attribute);
                    if (valueOffset >= 0) AddColors(attribute.RawValue, valueOffset, colors);
                }
            }
        }
        var style = ComponentSectionScanner.Scan(
            text,
            "Colors.sqx",
            ComponentDialect.Sqx,
            tolerant: true).Document.Style?.Css;
        if (style == null) return colors;
        foreach (var declaration in EnumerateDeclarations(style))
            AddColors(declaration.Value, declaration.ValueRange.Offset, colors);
        return colors
            .OrderBy(color => color.Start)
            .ToArray();
    }

    private static void AddColors(string value, int offset, List<TemplateDocumentColor> colors)
    {
        foreach (Match match in HexColor.Matches(value))
        {
            if (!TryParseHex(match.Value, out var red, out var green, out var blue, out var alpha))
                continue;
            colors.Add(new TemplateDocumentColor(
                offset + match.Index,
                match.Length,
                red,
                green,
                blue,
                alpha));
        }
        foreach (Match match in RgbColor.Matches(value))
        {
            colors.Add(new TemplateDocumentColor(
                offset + match.Index,
                match.Length,
                ClampChannel(match.Groups[1].Value) / 255d,
                ClampChannel(match.Groups[2].Value) / 255d,
                ClampChannel(match.Groups[3].Value) / 255d,
                match.Groups[4].Success ? ParseAlpha(match.Groups[4].Value) : 1d));
        }
    }

    private static int FindAttributeValueOffset(string text, SqxAttribute attribute)
    {
        var start = Math.Min(Math.Max(attribute.Position, 0), text.Length);
        var tagEnd = text.IndexOf('>', start);
        if (tagEnd < 0) tagEnd = text.Length;
        var equals = text.IndexOf('=', start, tagEnd - start);
        if (equals < 0) return -1;
        var position = equals + 1;
        while (position < tagEnd && char.IsWhiteSpace(text[position])) position++;
        if (position >= tagEnd || text[position] is not ('"' or '\'')) return -1;
        return position + 1;
    }

    private static IEnumerable<SqxElement> EnumerateElements(IEnumerable<SqxNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is SqxElement element)
            {
                yield return element;
                foreach (var child in EnumerateElements(element.Children)) yield return child;
            }
            else if (node is TemplateForDirective loop)
            {
                foreach (var child in EnumerateElements(loop.Children)) yield return child;
            }
            else if (node is TemplateIfChainDirective chain)
            {
                foreach (var branch in chain.Branches)
                    foreach (var child in EnumerateElements(branch.Children)) yield return child;
            }
        }
    }

    private static IEnumerable<CssDeclarationSyntax> EnumerateDeclarations(CssStyleSheetSyntax style)
    {
        foreach (var declaration in style.Rules.SelectMany(rule => rule.Declarations)) yield return declaration;
        foreach (var atRule in style.AtRules)
        {
            foreach (var declaration in EnumerateDeclarations(atRule)) yield return declaration;
        }
    }

    private static IEnumerable<CssDeclarationSyntax> EnumerateDeclarations(CssAtRuleSyntax atRule)
    {
        foreach (var declaration in atRule.Declarations) yield return declaration;
        foreach (var declaration in atRule.Rules.SelectMany(rule => rule.Declarations)) yield return declaration;
        foreach (var child in atRule.AtRules)
        {
            foreach (var declaration in EnumerateDeclarations(child)) yield return declaration;
        }
    }

    public static IReadOnlyList<TemplateColorPresentation> GetPresentations(
        string text,
        int start,
        int length,
        double red,
        double green,
        double blue,
        double alpha)
    {
        text ??= string.Empty;
        start = Math.Min(Math.Max(start, 0), text.Length);
        length = Math.Min(Math.Max(length, 0), text.Length - start);
        var hex = ToHex(red, green, blue, alpha);
        var rgb = alpha >= 0.999
            ? $"rgb({ToByte(red)}, {ToByte(green)}, {ToByte(blue)})"
            : $"rgba({ToByte(red)}, {ToByte(green)}, {ToByte(blue)}, {alpha.ToString("0.###", CultureInfo.InvariantCulture)})";
        return new[]
        {
            new TemplateColorPresentation(hex, start, length),
            new TemplateColorPresentation(rgb, start, length)
        };
    }

    private static bool TryParseHex(string value, out double red, out double green, out double blue, out double alpha)
    {
        red = green = blue = 0;
        alpha = 1;
        var hex = value.TrimStart('#');
        if (hex.Length is 3 or 4)
            hex = string.Concat(hex.Select(character => new string(character, 2)));
        if (hex.Length is not (6 or 8)) return false;
        if (!int.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !int.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !int.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            return false;
        red = r / 255d;
        green = g / 255d;
        blue = b / 255d;
        if (hex.Length == 8 &&
            int.TryParse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a))
            alpha = a / 255d;
        return true;
    }

    private static string ToHex(double red, double green, double blue, double alpha)
    {
        var value = $"#{ToByte(red):X2}{ToByte(green):X2}{ToByte(blue):X2}";
        return alpha >= 0.999 ? value : value + $"{ToByte(alpha):X2}";
    }

    private static int ToByte(double value) =>
        Math.Min(Math.Max((int)Math.Round(value * 255d), 0), 255);

    private static int ClampChannel(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? Math.Min(Math.Max(number, 0), 255)
            : 0;

    private static double ParseAlpha(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? Math.Min(Math.Max(number, 0d), 1d)
            : 1d;
}
