using Square.Graphics;
using Square.Text;
using Square.UI;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace Square.Extensions.Markdown;

internal sealed class MarkdownCodeText : UIElement, ITextSelectable
{
    private static readonly object GrammarGate = new();
    private static readonly RegistryOptions GrammarOptions = new(ThemeName.DarkPlus);
    private static readonly Registry GrammarRegistry = new(GrammarOptions);
    private readonly global::Square.UI.Text _textNode = new();
    private IReadOnlyList<IReadOnlyList<CodeToken>>? _tokens;
    private string _textContent = "";
    private string? _language;

    public MarkdownCodeText() => ChildNodes.Add(_textNode);

    public string TextContent
    {
        get => _textContent;
        set
        {
            _textContent = value ?? "";
            _textNode.Data = _textContent;
            _tokens = null;
        }
    }

    public string? Language
    {
        get => _language;
        set
        {
            _language = value;
            _tokens = null;
        }
    }
    public string SelectableText => TextContent;
    public Rect SelectableTextBounds => Geometry;

    public override Size Measure(Size availableSize)
    {
        var font = ResolveFont();
        var lineHeight = GetLineHeight(font);
        var width = 0f;
        var lines = GetLines();
        foreach (var line in lines)
            width = Math.Max(width, MeasureTextWidth(line, font));
        return new Size(
            Math.Min(width, float.IsFinite(availableSize.Width) ? availableSize.Width : width),
            lines.Length * lineHeight);
    }

    public override void Paint(IRenderContext context)
    {
        var font = ResolveFont();
        var lineHeight = GetLineHeight(font);
        var lines = GetLines();
        var tokens = _tokens ??= Tokenize(lines);
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var y = Geometry.Y + lineIndex * lineHeight;
            foreach (var token in tokens[lineIndex])
            {
                if (token.Length <= 0 || token.Start >= line.Length) continue;
                var start = Math.Clamp(token.Start, 0, line.Length);
                var end = Math.Clamp(start + token.Length, start, line.Length);
                var prefix = line[..start];
                var text = line[start..end];
                var x = Geometry.X + MeasureTextWidth(prefix, font);
                context.DrawText(
                    new TextLayout(text, font) { WhiteSpace = TextWhiteSpaceMode.Pre, TextDecorationLines = TextDecorationLine.None },
                    new Point(x, y),
                    new SolidColorBrush(ResolveColor(token.Type)));
            }
        }
    }

    private IReadOnlyList<IReadOnlyList<CodeToken>> Tokenize(string[] lines)
    {
        var grammar = ResolveGrammar(Language);
        if (grammar == null)
            return lines.Select(line => (IReadOnlyList<CodeToken>)[new CodeToken(0, line.Length, "source")]).ToArray();

        IStateStack? state = null;
        var result = new List<IReadOnlyList<CodeToken>>(lines.Length);
        foreach (var line in lines)
        {
            var lineResult = grammar.TokenizeLine(line, state, TimeSpan.MaxValue);
            state = lineResult.RuleStack;
            var lineTokens = new List<CodeToken>(lineResult.Tokens.Length);
            foreach (var token in lineResult.Tokens)
            {
                var start = Math.Clamp(token.StartIndex, 0, line.Length);
                var end = Math.Clamp(token.EndIndex, start, line.Length);
                if (end > start)
                    lineTokens.Add(new CodeToken(start, end - start, MapScopes(token.Scopes)));
            }
            if (lineTokens.Count == 0 && line.Length > 0)
                lineTokens.Add(new CodeToken(0, line.Length, "source"));
            result.Add(lineTokens);
        }
        return result;
    }

    private static IGrammar? ResolveGrammar(string? language)
    {
        language = NormalizeLanguage(language);
        if (language.Length == 0) return null;
        lock (GrammarGate)
        {
            var scope = GrammarOptions.GetScopeByLanguageId(language);
            return string.IsNullOrEmpty(scope) ? null : GrammarRegistry.LoadGrammar(scope);
        }
    }

    private static string NormalizeLanguage(string? language) => language?.Trim().ToLowerInvariant() switch
    {
        "cs" or "c#" => "csharp",
        "js" => "javascript",
        "ts" => "typescript",
        "sh" or "bash" => "shellscript",
        "yml" => "yaml",
        "md" => "markdown",
        null => "",
        var value => value,
    };

    private static string MapScopes(IList<string> scopes)
    {
        for (var i = scopes.Count - 1; i >= 0; i--)
        {
            var scope = scopes[i];
            if (scope.StartsWith("comment", StringComparison.Ordinal)) return "comment";
            if (scope.StartsWith("string", StringComparison.Ordinal)) return "string";
            if (scope.StartsWith("constant.numeric", StringComparison.Ordinal)) return "number";
            if (scope.StartsWith("constant", StringComparison.Ordinal)) return "constant";
            if (scope.StartsWith("keyword", StringComparison.Ordinal) || scope.StartsWith("storage", StringComparison.Ordinal)) return "keyword";
            if (scope.StartsWith("entity.name.type", StringComparison.Ordinal) || scope.StartsWith("support.type", StringComparison.Ordinal)) return "type";
            if (scope.StartsWith("entity.name.function", StringComparison.Ordinal) || scope.StartsWith("support.function", StringComparison.Ordinal)) return "function";
            if (scope.StartsWith("variable", StringComparison.Ordinal)) return "variable";
            if (scope.StartsWith("entity.name.tag", StringComparison.Ordinal)) return "tag";
            if (scope.StartsWith("entity.other.attribute-name", StringComparison.Ordinal)) return "attribute";
            if (scope.StartsWith("invalid", StringComparison.Ordinal)) return "invalid";
        }
        return "source";
    }

    private static Color ResolveColor(string type) => type switch
    {
        "comment" => Color.FromRgb(106, 153, 85),
        "string" => Color.FromRgb(163, 21, 21),
        "number" or "constant" => Color.FromRgb(9, 134, 88),
        "keyword" => Color.FromRgb(0, 0, 255),
        "type" => Color.FromRgb(38, 127, 153),
        "function" => Color.FromRgb(121, 94, 38),
        "variable" => Color.FromRgb(0, 16, 128),
        "tag" => Color.FromRgb(128, 0, 0),
        "attribute" => Color.FromRgb(255, 0, 0),
        "invalid" => Color.FromRgb(205, 49, 49),
        _ => Color.FromRgb(33, 37, 41),
    };

    private string[] GetLines() => TextContent.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private Font ResolveFont() => FontManager.Instance.FromCss(
        Style.Get("font-family") ?? "",
        Style.Get("font-size") ?? "",
        Style.Get("font-weight") ?? "",
        Style.Get("font-style") ?? "",
        13f);

    private float GetLineHeight(Font font)
    {
        var value = (Style.Get("line-height") ?? "").Trim();
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(value[..^2], System.Globalization.CultureInfo.InvariantCulture, out var px) && px > 0)
            return px;
        return TextMetrics.GetLineHeight(font, TextLayout.DefaultLineHeight);
    }

    private static float MeasureTextWidth(string text, Font font)
    {
        var width = 0f;
        foreach (var rune in text.EnumerateRunes())
            width += TextMetrics.GetGlyphMetrics(font, rune).AdvanceX;
        return width;
    }

    private readonly record struct CodeToken(int Start, int Length, string Type);
}
