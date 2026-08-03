using Square.CSS.Ast;
using Square.CSS.Tokenizer;

namespace Square.CSS.Engine;

/// <summary>将 CSS 令牌流解析为样式表抽象语法树。</summary>
public sealed class CssParser
{
    private readonly List<CssToken> _tokens;
    private int _i;
    private Combinator _pendingCombinator = Combinator.Descendant;

    /// <summary>初始化 CssParser 的新实例。</summary>
    /// <param name="tokens">待解析的令牌列表。</param>
    public CssParser(List<CssToken> tokens) { _tokens = tokens; }

    /// <summary>解析令牌流并生成 CSS 样式表。</summary>
    /// <returns>解析得到的样式表。</returns>
    public CssStyleSheet Parse()
    {
        var rules = new List<CssRule>();
        var atRules = new List<CssAtRule>();
        var imports = new List<CssImportRule>();
        var keyFrames = new List<KeyFramesRule>();
        var importsAllowed = true;
        while (Peek().Type != CssTokenType.Eof)
        {
            if (Peek().Type == CssTokenType.Whitespace)
            {
                Advance();
                continue;
            }
            if (Peek().Type == CssTokenType.AtKeyword)
            {
                var atName = Peek().Text;
                if (string.Equals(atName, "import", StringComparison.OrdinalIgnoreCase))
                {
                    var import = ParseImportRule();
                    if (import != null && importsAllowed) imports.Add(import);
                }
                else if (string.Equals(atName, "keyframes", StringComparison.OrdinalIgnoreCase))
                {
                    importsAllowed = false;
                    var kf = ParseKeyFrames();
                    if (kf != null) keyFrames.Add(kf);
                }
                else
                {
                    var importPrefixRule =
                        (string.Equals(atName, "charset", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(atName, "layer", StringComparison.OrdinalIgnoreCase)) &&
                        IsSemicolonTerminatedAtRule();
                    if (!importPrefixRule)
                        importsAllowed = false;
                    var atRule = ParseAtRule();
                    if (atRule != null) atRules.Add(atRule);
                }
            }
            else
            {
                importsAllowed = false;
                var parsedRules = ParseRules();
                if (parsedRules.Count > 0) rules.AddRange(parsedRules);
            }
        }
        return new CssStyleSheet(rules, atRules) { Imports = imports, KeyFrames = keyFrames };
    }

    private CssImportRule? ParseImportRule()
    {
        Advance(); // @import
        var tokens = new List<CssToken>();
        while (Peek().Type is not (CssTokenType.Semicolon or CssTokenType.Eof))
            tokens.Add(Advance());
        if (Peek().Type == CssTokenType.Semicolon) Advance();
        if (tokens.Count == 0) return null;

        var index = 0;
        SkipWhitespace(tokens, ref index);
        string? href = null;
        if (index < tokens.Count && tokens[index].Type == CssTokenType.String)
        {
            href = tokens[index++].Text;
        }
        else if (index < tokens.Count && tokens[index].Type == CssTokenType.Identifier &&
                 string.Equals(tokens[index].Text, "url", StringComparison.OrdinalIgnoreCase))
        {
            index++;
            SkipWhitespace(tokens, ref index);
            if (index >= tokens.Count || tokens[index].Type != CssTokenType.OpenParen) return null;
            index++;
            SkipWhitespace(tokens, ref index);
            if (index < tokens.Count && tokens[index].Type == CssTokenType.String)
                href = tokens[index++].Text;
            else
            {
                var url = new System.Text.StringBuilder();
                while (index < tokens.Count && tokens[index].Type != CssTokenType.CloseParen)
                {
                    if (tokens[index].Type != CssTokenType.Whitespace) url.Append(tokens[index].Text);
                    index++;
                }
                href = url.ToString();
            }
            while (index < tokens.Count && tokens[index].Type != CssTokenType.CloseParen) index++;
            if (index < tokens.Count) index++;
        }

        if (string.IsNullOrWhiteSpace(href)) return null;
        var conditions = FormatTokens(tokens, index);
        return new CssImportRule(href, conditions);
    }

    private bool IsSemicolonTerminatedAtRule()
    {
        for (var index = _i + 1; index < _tokens.Count; index++)
        {
            if (_tokens[index].Type == CssTokenType.Semicolon) return true;
            if (_tokens[index].Type is CssTokenType.OpenBrace or CssTokenType.Eof) return false;
        }
        return false;
    }

    private KeyFramesRule? ParseKeyFrames()
    {
        Advance(); // @keyframes
        var name = "";
        while (Peek().Type is not (CssTokenType.OpenBrace or CssTokenType.Eof))
        {
            if (Peek().Type == CssTokenType.Identifier)
                name = Advance().Text;
            else
                Advance();
        }
        if (Peek().Type != CssTokenType.OpenBrace) return null;
        Advance();

        var stops = new List<KeyFrameStop>();
        while (Peek().Type is not (CssTokenType.CloseBrace or CssTokenType.Eof))
        {
            var selector = new System.Text.StringBuilder();
            while (Peek().Type is not (CssTokenType.OpenBrace or CssTokenType.CloseBrace or CssTokenType.Eof))
            {
                var t = Advance();
                if (t.Type != CssTokenType.Whitespace)
                    selector.Append(t.Text).Append(' ');
            }
            if (Peek().Type == CssTokenType.OpenBrace)
            {
                Advance();
                var decls = ParseDeclarations();
                stops.Add(new KeyFrameStop(selector.ToString().Trim(), decls));
            }
        }
        if (Peek().Type == CssTokenType.CloseBrace) Advance();
        return new KeyFramesRule(name, stops);
    }

    private CssAtRule? ParseAtRule()
    {
        var name = Advance().Text;
        var sb = new System.Text.StringBuilder();
        while (Peek().Type is not (CssTokenType.OpenBrace or CssTokenType.Semicolon or CssTokenType.Eof))
            sb.Append(Advance().Text).Append(' ');
        if (Peek().Type == CssTokenType.Semicolon)
        {
            Advance();
            return new CssAtRule(name, sb.ToString().Trim(), []);
        }
        if (Peek().Type != CssTokenType.OpenBrace) return null;
        Advance();
        var decls = ParseDeclarations();
        return new CssAtRule(name, sb.ToString().Trim(), decls);
    }

    private static void SkipWhitespace(List<CssToken> tokens, ref int index)
    {
        while (index < tokens.Count && tokens[index].Type == CssTokenType.Whitespace) index++;
    }

    private static string FormatTokens(List<CssToken> tokens, int start)
    {
        var result = new System.Text.StringBuilder();
        var pendingSpace = false;
        for (var i = start; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Type == CssTokenType.Whitespace)
            {
                pendingSpace = result.Length > 0;
                continue;
            }
            if (pendingSpace) result.Append(' ');
            result.Append(token.Text);
            pendingSpace = false;
        }
        return result.ToString().Trim();
    }

    private List<CssRule> ParseRules()
    {
        var selectors = ParseSelectors();
        if (selectors.Count == 0) return [];
        if (Peek().Type != CssTokenType.OpenBrace) return [];
        Advance();
        var decls = ParseDeclarations();
        return selectors.Select(selector => new CssRule(selector, decls)).ToList();
    }

    private List<ComplexSelector> ParseSelectors()
    {
        var result = new List<ComplexSelector>();
        var steps = new List<CompoundStep>();
        var parts = new List<SimpleSelector>();

        while (Peek().Type is not (CssTokenType.OpenBrace or CssTokenType.Eof))
        {
            var token = Peek();
            if (token.Type == CssTokenType.Whitespace)
            {
                FlushCompound(parts, steps);
                Advance();
                while (Peek().Type == CssTokenType.Whitespace) Advance();
                if (Peek().Type is CssTokenType.Greater or CssTokenType.Plus or CssTokenType.Tilde)
                    continue;
                _pendingCombinator = Combinator.Descendant;
                continue;
            }

            if (token.Type == CssTokenType.Comma)
            {
                FlushCompound(parts, steps);
                if (steps.Count > 0) result.Add(new ComplexSelector(new List<CompoundStep>(steps)));
                steps.Clear();
                _pendingCombinator = Combinator.Descendant;
                Advance();
                continue;
            }

            if (token.Type is CssTokenType.Greater or CssTokenType.Plus or CssTokenType.Tilde)
            {
                FlushCompound(parts, steps);
                if (steps.Count > 0)
                {
                    var combinator = token.Type switch
                    {
                        CssTokenType.Greater => Combinator.Child,
                        CssTokenType.Plus => Combinator.Adjacent,
                        _ => Combinator.GeneralSibling
                    };
                    _pendingCombinator = combinator;
                }
                Advance();
                while (Peek().Type == CssTokenType.Whitespace) Advance();
                continue;
            }

            if (token.Type == CssTokenType.OpenBracket)
            {
                parts.Add(ParseAttributeSelector());
                continue;
            }

            if (token.Type == CssTokenType.Identifier)
            {
                parts.Add(new SimpleSelector(SimpleSelectorKind.Type, Advance().Text));
            }
            else if (token.Type == CssTokenType.Asterisk)
            {
                Advance();
                parts.Add(new SimpleSelector(SimpleSelectorKind.Universal, "*"));
            }
            else if (token.Type == CssTokenType.Dot)
            {
                Advance();
                parts.Add(new SimpleSelector(SimpleSelectorKind.Class, Advance().Text));
            }
            else if (token.Type == CssTokenType.Hash)
            {
                parts.Add(new SimpleSelector(SimpleSelectorKind.Id, Advance().Text));
            }
            else if (token.Type is CssTokenType.Colon or CssTokenType.DoubleColon)
            {
                Advance();
                while (Peek().Type == CssTokenType.Whitespace) Advance();
                var name = Advance().Text;
                if (Peek().Type == CssTokenType.OpenParen)
                {
                    Advance();
                    var argument = new System.Text.StringBuilder();
                    while (Peek().Type is not (CssTokenType.CloseParen or CssTokenType.Eof))
                        argument.Append(Advance().Text);
                    if (Peek().Type == CssTokenType.CloseParen) Advance();
                    name += "(" + argument + ")";
                }
                parts.Add(new SimpleSelector(SimpleSelectorKind.PseudoClass, name));
            }
            else
            {
                Advance();
            }
        }

        FlushCompound(parts, steps);
        if (steps.Count > 0) result.Add(new ComplexSelector(steps));
        return result;
    }

    private SimpleSelector ParseAttributeSelector()
    {
        Advance();
        SkipWhitespace();
        var name = Peek().Type == CssTokenType.Identifier ? Advance().Text : "";
        SkipWhitespace();

        var attributeOperator = AttributeSelectorOperator.Presence;
        string? value = null;
        var caseSensitivity = AttributeCaseSensitivity.Default;
        var valid = name.Length > 0;
        if (Peek().Type != CssTokenType.CloseBracket)
        {
            valid &= TryParseAttributeOperator(out attributeOperator);
            SkipWhitespace();
            var valueToken = Peek();
            if (valueToken.Type is CssTokenType.Identifier or CssTokenType.String or CssTokenType.Number or CssTokenType.Hash)
            {
                value = Advance().Text;
            }
            else
            {
                valid = false;
            }
            SkipWhitespace();
            if (Peek().Type == CssTokenType.Identifier &&
                (string.Equals(Peek().Text, "i", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(Peek().Text, "s", StringComparison.OrdinalIgnoreCase)))
            {
                caseSensitivity = string.Equals(Advance().Text, "i", StringComparison.OrdinalIgnoreCase)
                    ? AttributeCaseSensitivity.Insensitive
                    : AttributeCaseSensitivity.Sensitive;
                SkipWhitespace();
            }
            valid &= Peek().Type == CssTokenType.CloseBracket;
        }

        while (Peek().Type is not (CssTokenType.CloseBracket or CssTokenType.Eof)) Advance();
        if (Peek().Type == CssTokenType.CloseBracket) Advance();
        else valid = false;

        return new SimpleSelector(
            SimpleSelectorKind.Attribute,
            name,
            valid ? attributeOperator : AttributeSelectorOperator.Invalid,
            value,
            caseSensitivity);
    }

    private bool TryParseAttributeOperator(out AttributeSelectorOperator attributeOperator)
    {
        attributeOperator = AttributeSelectorOperator.Invalid;
        var token = Peek();
        if (token.Type == CssTokenType.Equals)
        {
            Advance();
            attributeOperator = AttributeSelectorOperator.Equals;
            return true;
        }

        if (Peek(1).Type != CssTokenType.Equals) return false;
        attributeOperator = token.Type switch
        {
            CssTokenType.Tilde => AttributeSelectorOperator.Includes,
            CssTokenType.Asterisk => AttributeSelectorOperator.SubstringMatch,
            CssTokenType.Delimiter when token.Text == "|" => AttributeSelectorOperator.DashMatch,
            CssTokenType.Delimiter when token.Text == "^" => AttributeSelectorOperator.PrefixMatch,
            CssTokenType.Delimiter when token.Text == "$" => AttributeSelectorOperator.SuffixMatch,
            _ => AttributeSelectorOperator.Invalid
        };
        if (attributeOperator == AttributeSelectorOperator.Invalid) return false;
        Advance();
        Advance();
        return true;
    }

    private void SkipWhitespace()
    {
        while (Peek().Type == CssTokenType.Whitespace) Advance();
    }

    private void FlushCompound(List<SimpleSelector> parts, List<CompoundStep> steps)
    {
        if (parts.Count == 0) return;
        steps.Add(new CompoundStep(new CompoundSelector(new List<SimpleSelector>(parts)), _pendingCombinator));
        _pendingCombinator = Combinator.Descendant;
        parts.Clear();
    }

    private List<Declaration> ParseDeclarations()
    {
        var decls = new List<Declaration>();
        while (Peek().Type is not (CssTokenType.CloseBrace or CssTokenType.Eof))
        {
            var propToken = Peek();
            if (propToken.Type != CssTokenType.Identifier) { Advance(); continue; }
            Advance();
            if (Peek().Type != CssTokenType.Colon) { while (Peek().Type is not (CssTokenType.Semicolon or CssTokenType.CloseBrace or CssTokenType.Eof)) Advance(); if (Peek().Type == CssTokenType.Semicolon) Advance(); continue; }
            Advance();
            var valueTokens = new List<CssToken>();
            while (Peek().Type is not (CssTokenType.Semicolon or CssTokenType.CloseBrace or CssTokenType.Eof))
                valueTokens.Add(Advance());
            if (Peek().Type == CssTokenType.Semicolon) Advance();
            var important = false;
            for (var i = valueTokens.Count - 1; i >= 0 && valueTokens[i].Type == CssTokenType.Whitespace; i--)
                valueTokens.RemoveAt(i);
            if (valueTokens.Count > 0 &&
                valueTokens[^1].Type == CssTokenType.Identifier &&
                string.Equals(valueTokens[^1].Text, "important", StringComparison.OrdinalIgnoreCase))
            {
                var bangIndex = valueTokens.Count - 2;
                while (bangIndex >= 0 && valueTokens[bangIndex].Type == CssTokenType.Whitespace) bangIndex--;
                if (bangIndex >= 0 && valueTokens[bangIndex].Type == CssTokenType.Bang)
                {
                    important = true;
                    valueTokens.RemoveRange(bangIndex, valueTokens.Count - bangIndex);
                    while (valueTokens.Count > 0 && valueTokens[^1].Type == CssTokenType.Whitespace)
                        valueTokens.RemoveAt(valueTokens.Count - 1);
                }
            }
            decls.Add(new(propToken.Text, FormatValue(valueTokens), important));
        }
        if (Peek().Type == CssTokenType.CloseBrace) Advance();
        return decls;
    }

    private static string FormatValue(List<CssToken> tokens)
    {
        var result = new System.Text.StringBuilder();
        CssTokenType? previous = null;
        foreach (var token in tokens)
        {
            if (token.Type == CssTokenType.Whitespace)
            {
                if (result.Length > 0 && result[result.Length - 1] != ' ') result.Append(' ');
                previous = token.Type;
                continue;
            }

            if (token.Type == CssTokenType.Hash) result.Append('#').Append(token.Text);
            else if (token.Type == CssTokenType.String) result.Append('"').Append(token.Text.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
            else if (token.Type == CssTokenType.OpenParen) result.Append('(');
            else if (token.Type == CssTokenType.CloseParen) { while (result.Length > 0 && result[result.Length - 1] == ' ') result.Length--; result.Append(')'); }
            else if (token.Type == CssTokenType.Comma) result.Append(',');
            else if (token.Type is CssTokenType.Unit or CssTokenType.Percentage) result.Append(token.Text);
            else result.Append(token.Text);
            previous = token.Type;
        }
        return result.ToString().Trim();
    }

    private CssToken Peek(int offset = 0) => _i + offset < _tokens.Count ? _tokens[_i + offset] : _tokens[^1];
    private CssToken Advance() => _i < _tokens.Count ? _tokens[_i++] : _tokens[^1];
}
