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
        var mediaRules = new List<CssMediaRule>();
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
                else if (string.Equals(atName, "media", StringComparison.OrdinalIgnoreCase))
                {
                    importsAllowed = false;
                    var mediaRule = ParseMediaRule();
                    if (mediaRule != null) mediaRules.Add(mediaRule);
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
        return new CssStyleSheet(rules, atRules)
        {
            Imports = imports,
            KeyFrames = keyFrames,
            MediaRules = mediaRules
        };
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
        var decls = IsDeclarationAtRule(name) ? ParseDeclarationBlock() : SkipAtRuleBlock();
        return new CssAtRule(name, sb.ToString().Trim(), decls);
    }

    private CssMediaRule? ParseMediaRule()
    {
        Advance(); // @media
        var prelude = new List<CssToken>();
        while (Peek().Type is not (CssTokenType.OpenBrace or CssTokenType.Semicolon or CssTokenType.Eof))
            prelude.Add(Advance());
        if (Peek().Type == CssTokenType.Semicolon)
        {
            Advance();
            return null;
        }
        if (Peek().Type != CssTokenType.OpenBrace) return null;
        Advance();

        var mediaTypes = SplitCommaSeparated(prelude);
        var rules = new List<CssRule>();
        while (Peek().Type is not (CssTokenType.CloseBrace or CssTokenType.Eof))
        {
            if (Peek().Type == CssTokenType.Whitespace)
            {
                Advance();
                continue;
            }
            if (Peek().Type == CssTokenType.AtKeyword)
            {
                SkipAtRule();
                continue;
            }

            rules.AddRange(ParseRules());
        }
        if (Peek().Type == CssTokenType.CloseBrace) Advance();
        return new CssMediaRule(mediaTypes, rules);
    }

    private List<Declaration> ParseDeclarationBlock()
    {
        Advance();
        return ParseDeclarations();
    }

    private List<Declaration> SkipAtRuleBlock()
    {
        SkipBlock();
        return [];
    }

    private static bool IsDeclarationAtRule(string name) =>
        string.Equals(name, "font-face", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "page", StringComparison.OrdinalIgnoreCase);

    private static List<string> SplitCommaSeparated(List<CssToken> tokens)
    {
        var result = new List<string>();
        var start = 0;
        var depth = 0;
        for (var index = 0; index <= tokens.Count; index++)
        {
            if (index < tokens.Count)
            {
                if (tokens[index].Type is CssTokenType.OpenParen or CssTokenType.OpenBracket) depth++;
                else if (tokens[index].Type is CssTokenType.CloseParen or CssTokenType.CloseBracket && depth > 0) depth--;
                if (tokens[index].Type != CssTokenType.Comma || depth != 0) continue;
            }

            var mediaType = FormatTokens(tokens.GetRange(start, index - start), 0);
            if (mediaType.Length > 0) result.Add(mediaType);
            start = index + 1;
        }
        return result;
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
        var selectors = ParseSelectors(out var valid);
        if (Peek().Type != CssTokenType.OpenBrace) return [];
        if (!valid || selectors.Count == 0)
        {
            SkipBlock();
            return [];
        }
        Advance();
        var decls = ParseDeclarations();
        return selectors.Select(selector => new CssRule(selector, decls)).ToList();
    }

    private List<ComplexSelector> ParseSelectors(out bool valid)
    {
        var result = new List<ComplexSelector>();
        var steps = new List<CompoundStep>();
        var parts = new List<SimpleSelector>();
        valid = true;
        var explicitCombinatorPending = false;
        _pendingCombinator = Combinator.Descendant;

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
                if (steps.Count == 0 || explicitCombinatorPending) valid = false;
                else result.Add(new ComplexSelector(new List<CompoundStep>(steps)));
                steps.Clear();
                _pendingCombinator = Combinator.Descendant;
                explicitCombinatorPending = false;
                Advance();
                continue;
            }

            if (token.Type is CssTokenType.Greater or CssTokenType.Plus or CssTokenType.Tilde)
            {
                FlushCompound(parts, steps);
                if (steps.Count > 0 && !explicitCombinatorPending)
                {
                    var combinator = token.Type switch
                    {
                        CssTokenType.Greater => Combinator.Child,
                        CssTokenType.Plus => Combinator.Adjacent,
                        _ => Combinator.GeneralSibling
                    };
                    _pendingCombinator = combinator;
                    explicitCombinatorPending = true;
                }
                else valid = false;
                Advance();
                while (Peek().Type == CssTokenType.Whitespace) Advance();
                continue;
            }

            if (token.Type == CssTokenType.OpenBracket)
            {
                parts.Add(ParseAttributeSelector(out var attributeValid));
                valid &= attributeValid;
                explicitCombinatorPending = false;
                continue;
            }

            if (token.Type == CssTokenType.Identifier)
            {
                if (parts.Count > 0) valid = false;
                parts.Add(new SimpleSelector(SimpleSelectorKind.Type, Advance().Text));
                explicitCombinatorPending = false;
            }
            else if (token.Type == CssTokenType.Asterisk)
            {
                if (parts.Count > 0) valid = false;
                Advance();
                parts.Add(new SimpleSelector(SimpleSelectorKind.Universal, "*"));
                explicitCombinatorPending = false;
            }
            else if (token.Type == CssTokenType.Dot)
            {
                Advance();
                if (Peek().Type == CssTokenType.Identifier)
                {
                    parts.Add(new SimpleSelector(SimpleSelectorKind.Class, Advance().Text));
                    explicitCombinatorPending = false;
                }
                else valid = false;
            }
            else if (token.Type == CssTokenType.Hash)
            {
                var name = Advance().Text;
                if (name.Length == 0) valid = false;
                else
                {
                    parts.Add(new SimpleSelector(SimpleSelectorKind.Id, name));
                    explicitCombinatorPending = false;
                }
            }
            else if (token.Type is CssTokenType.Colon or CssTokenType.DoubleColon)
            {
                var pseudoTokenType = token.Type;
                Advance();
                while (Peek().Type == CssTokenType.Whitespace) Advance();
                if (Peek().Type != CssTokenType.Identifier)
                {
                    valid = false;
                    continue;
                }
                var name = Advance().Text;
                if (Peek().Type == CssTokenType.OpenParen)
                {
                    Advance();
                    var argument = new System.Text.StringBuilder();
                    var depth = 1;
                    while (depth > 0 && Peek().Type != CssTokenType.Eof)
                    {
                        var argumentToken = Advance();
                        if (argumentToken.Type == CssTokenType.OpenParen) depth++;
                        else if (argumentToken.Type == CssTokenType.CloseParen && --depth == 0) break;
                        argument.Append(argumentToken.Text);
                    }
                    if (depth != 0) valid = false;
                    name += "(" + argument + ")";
                }
                var kind = pseudoTokenType == CssTokenType.DoubleColon ||
                           string.Equals(name, "before", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(name, "after", StringComparison.OrdinalIgnoreCase)
                    ? SimpleSelectorKind.PseudoElement
                    : SimpleSelectorKind.PseudoClass;
                parts.Add(new SimpleSelector(kind, name));
                explicitCombinatorPending = false;
            }
            else
            {
                valid = false;
                Advance();
            }
        }

        FlushCompound(parts, steps);
        if (steps.Count == 0 || explicitCombinatorPending) valid = false;
        else result.Add(new ComplexSelector(steps));
        if (!valid) result.Clear();
        return result;
    }

    private SimpleSelector ParseAttributeSelector(out bool selectorValid)
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

        selectorValid = valid;
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
            if (Peek().Type == CssTokenType.AtKeyword)
            {
                SkipAtRule();
                continue;
            }
            if (Peek().Type == CssTokenType.Whitespace)
            {
                Advance();
                continue;
            }
            var propToken = Peek();
            if (propToken.Type != CssTokenType.Identifier)
            {
                SkipMalformedDeclaration();
                continue;
            }
            Advance();
            if (Peek().Type != CssTokenType.Colon)
            {
                SkipMalformedDeclaration();
                continue;
            }
            Advance();
            var valueTokens = new List<CssToken>();
            var malformedValue = false;
            var valueDepth = 0;
            while (Peek().Type is not (CssTokenType.CloseBrace or CssTokenType.Eof))
            {
                if (Peek().Type == CssTokenType.Semicolon && valueDepth == 0) break;
                if (Peek().Type == CssTokenType.OpenBrace)
                {
                    malformedValue = true;
                    SkipBlock();
                    break;
                }

                if (Peek().Type is CssTokenType.OpenParen or CssTokenType.OpenBracket)
                    valueDepth++;
                else if (Peek().Type is CssTokenType.CloseParen or CssTokenType.CloseBracket && valueDepth > 0)
                    valueDepth--;
                valueTokens.Add(Advance());
            }
            if (Peek().Type == CssTokenType.Semicolon) Advance();
            if (malformedValue) continue;
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

    private void SkipMalformedDeclaration()
    {
        var parentheses = 0;
        var brackets = 0;
        var braces = 0;
        while (Peek().Type != CssTokenType.Eof)
        {
            var token = Peek();
            if (token.Type == CssTokenType.Semicolon && parentheses == 0 && brackets == 0 && braces == 0)
            {
                Advance();
                return;
            }

            switch (token.Type)
            {
                case CssTokenType.OpenParen:
                    parentheses++;
                    break;
                case CssTokenType.CloseParen:
                    if (parentheses > 0) parentheses--;
                    break;
                case CssTokenType.OpenBracket:
                    brackets++;
                    break;
                case CssTokenType.CloseBracket:
                    if (brackets > 0) brackets--;
                    break;
                case CssTokenType.OpenBrace:
                    braces++;
                    break;
                case CssTokenType.CloseBrace:
                    if (braces == 0 && parentheses == 0 && brackets == 0) return;
                    if (braces > 0) braces--;
                    break;
            }

            Advance();
        }
    }

    private void SkipAtRule()
    {
        Advance();
        var depth = 0;
        while (Peek().Type is not (CssTokenType.CloseBrace or CssTokenType.Eof))
        {
            if (Peek().Type is CssTokenType.OpenParen or CssTokenType.OpenBracket)
                depth++;
            else if (Peek().Type is CssTokenType.CloseParen or CssTokenType.CloseBracket && depth > 0)
                depth--;
            else if (depth == 0 && Peek().Type is (CssTokenType.OpenBrace or CssTokenType.Semicolon))
                break;
            Advance();
        }
        if (Peek().Type == CssTokenType.Semicolon)
        {
            Advance();
            return;
        }
        if (Peek().Type == CssTokenType.OpenBrace) SkipBlock();
    }

    private void SkipBlock()
    {
        if (Peek().Type != CssTokenType.OpenBrace) return;
        var depth = 0;
        do
        {
            var token = Advance();
            if (token.Type == CssTokenType.OpenBrace) depth++;
            else if (token.Type == CssTokenType.CloseBrace) depth--;
        } while (depth > 0 && Peek().Type != CssTokenType.Eof);
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
