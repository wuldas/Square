namespace Square.CSS.Tokenizer;

/// <summary>将 CSS 源文本扫描为令牌列表。</summary>
public sealed class CssTokenizer
{
    private readonly string _source;
    private int _pos;
    private int _line = 1;

    /// <summary>初始化 CssTokenizer 的新实例。</summary>
    /// <param name="source">待扫描的 CSS 源文本。</param>
    public CssTokenizer(string source) { _source = source; }

    /// <summary>扫描源文本并返回令牌列表。</summary>
    /// <returns>CSS 令牌列表。</returns>
    public List<CssToken> Tokenize()
    {
        var tokens = new List<CssToken>();
        while (_pos < _source.Length)
        {
            var c = _source[_pos];
            if (c == '/' && Peek(1) == '*') { SkipComment(); continue; }
            if (IsCssWhitespace(c))
            {
                var line = _line;
                SkipWhitespace();
                tokens.Add(new CssToken(CssTokenType.Whitespace, " ", line));
                continue;
            }
            if (c == '{') { tokens.Add(new CssToken(CssTokenType.OpenBrace, "{", _line)); _pos++; continue; }
            if (c == '}') { tokens.Add(new CssToken(CssTokenType.CloseBrace, "}", _line)); _pos++; continue; }
            if (c == '(') { tokens.Add(new CssToken(CssTokenType.OpenParen, "(", _line)); _pos++; continue; }
            if (c == ')') { tokens.Add(new CssToken(CssTokenType.CloseParen, ")", _line)); _pos++; continue; }
            if (c == '[') { tokens.Add(new CssToken(CssTokenType.OpenBracket, "[", _line)); _pos++; continue; }
            if (c == ']') { tokens.Add(new CssToken(CssTokenType.CloseBracket, "]", _line)); _pos++; continue; }
            if (c == ';') { tokens.Add(new CssToken(CssTokenType.Semicolon, ";", _line)); _pos++; continue; }
            if (c == ',') { tokens.Add(new CssToken(CssTokenType.Comma, ",", _line)); _pos++; continue; }
            if (c == '>') { tokens.Add(new CssToken(CssTokenType.Greater, ">", _line)); _pos++; continue; }
            if (WouldStartNumber())
            {
                var line = _line;
                var (num, unit) = ReadNumber();
                tokens.Add(new CssToken(CssTokenType.Number, num, line));
                if (unit != null) tokens.Add(new CssToken(CssTokenType.Unit, unit, line));
                else if (_pos < _source.Length && _source[_pos] == '%')
                {
                    tokens.Add(new CssToken(CssTokenType.Percentage, "%", _line));
                    _pos++;
                }
                continue;
            }
            if (c == '+') { tokens.Add(new CssToken(CssTokenType.Plus, "+", _line)); _pos++; continue; }
            if (c == '~') { tokens.Add(new CssToken(CssTokenType.Tilde, "~", _line)); _pos++; continue; }
            if (c == '!') { tokens.Add(new CssToken(CssTokenType.Bang, "!", _line)); _pos++; continue; }
            if (c == '*') { tokens.Add(new CssToken(CssTokenType.Asterisk, "*", _line)); _pos++; continue; }
            if (c == '=') { tokens.Add(new CssToken(CssTokenType.Equals, "=", _line)); _pos++; continue; }
            if (c == ':') { tokens.Add(Peek(1) == ':' ? new CssToken(CssTokenType.DoubleColon, "::", _line) : new CssToken(CssTokenType.Colon, ":", _line)); _pos += Peek(1) == ':' ? 2 : 1; continue; }
            if (c == '.') { tokens.Add(new CssToken(CssTokenType.Dot, ".", _line)); _pos++; continue; }
            if (c == '#')
            {
                var line = _line;
                _pos++;
                if (_pos < _source.Length && (IsIdentChar(_source[_pos]) || IsValidEscape()))
                {
                    var name = ReadIdent();
                    tokens.Add(new CssToken(CssTokenType.Hash, name, line));
                }
                else tokens.Add(new CssToken(CssTokenType.Delimiter, "#", line));
                continue;
            }
            if (c == '@')
            {
                var line = _line;
                _pos++;
                if (_pos < _source.Length && WouldStartIdentifier())
                    tokens.Add(new CssToken(CssTokenType.AtKeyword, ReadIdent(), line));
                else tokens.Add(new CssToken(CssTokenType.Delimiter, "@", line));
                continue;
            }
            if (c == '"' || c == '\'')
            {
                var line = _line;
                var s = ReadString(c);
                tokens.Add(new CssToken(CssTokenType.String, s, line));
                continue;
            }
            if (WouldStartIdentifier()) { var name = ReadIdent(); tokens.Add(new CssToken(CssTokenType.Identifier, name, _line)); continue; }
            tokens.Add(new CssToken(CssTokenType.Delimiter, c.ToString(), _line));
            _pos++;
        }
        tokens.Add(new CssToken(CssTokenType.Eof, "", _line));
        return tokens;
    }

    private char Peek(int o) => _pos + o < _source.Length ? _source[_pos + o] : '\0';
    private void SkipWhitespace()
    {
        while (_pos < _source.Length && IsCssWhitespace(_source[_pos]))
        {
            if (_source[_pos] == '\r')
            {
                _line++;
                _pos++;
                if (_pos < _source.Length && _source[_pos] == '\n') _pos++;
                continue;
            }
            if (_source[_pos] is '\n' or '\f') _line++;
            _pos++;
        }
    }
    private void SkipComment() { _pos += 2; while (_pos < _source.Length && !(_source[_pos] == '*' && Peek(1) == '/')) { if (_source[_pos] == '\n') _line++; _pos++; } if (_pos < _source.Length) _pos += 2; }
    private string ReadIdent()
    {
        var result = new System.Text.StringBuilder();
        while (_pos < _source.Length)
        {
            if (IsIdentChar(_source[_pos]))
            {
                result.Append(_source[_pos++]);
                continue;
            }
            if (!IsValidEscape()) break;
            result.Append(ReadEscape());
        }
        return result.ToString();
    }
    private string ReadString(char q)
    {
        _pos++;
        var result = new System.Text.StringBuilder();
        while (_pos < _source.Length && _source[_pos] != q)
        {
            if (_source[_pos] == '\\' && _pos + 1 < _source.Length)
            {
                if (Peek(1) is '\r' or '\n' or '\f')
                {
                    _pos++;
                    if (_source[_pos] == '\r' && Peek(1) == '\n') _pos++;
                    _line++;
                    _pos++;
                    continue;
                }
                result.Append(ReadEscape());
                continue;
            }
            if (_source[_pos] == '\n') _line++;
            result.Append(_source[_pos++]);
        }
        if (_pos < _source.Length) _pos++;
        return result.ToString();
    }
    private (string, string?) ReadNumber()
    {
        var start = _pos;
        if (_source[_pos] is '+' or '-') _pos++;
        while (_pos < _source.Length && char.IsDigit(_source[_pos])) _pos++;
        if (_pos < _source.Length && _source[_pos] == '.' && char.IsDigit(Peek(1)))
        {
            _pos++;
            while (_pos < _source.Length && char.IsDigit(_source[_pos])) _pos++;
        }
        if (_pos < _source.Length && _source[_pos] is ('e' or 'E') && ExponentHasDigits())
        {
            _pos++;
            if (_source[_pos] is '+' or '-') _pos++;
            while (_pos < _source.Length && char.IsDigit(_source[_pos])) _pos++;
        }

        var number = _source[start.._pos];
        string? unit = null;
        if (_pos < _source.Length && (IsIdentStart(_source[_pos]) || IsValidEscape()))
            unit = ReadIdent();
        return (number, unit);
    }

    private bool WouldStartNumber()
    {
        var c = Peek(0);
        if (char.IsDigit(c)) return true;
        if (c == '.') return char.IsDigit(Peek(1));
        return c is '+' or '-' &&
               (char.IsDigit(Peek(1)) || Peek(1) == '.' && char.IsDigit(Peek(2)));
    }

    private bool ExponentHasDigits()
    {
        var offset = 1;
        if (Peek(offset) is '+' or '-') offset++;
        return char.IsDigit(Peek(offset));
    }

    private bool IsValidEscape() => _pos < _source.Length && _source[_pos] == '\\' &&
                                    Peek(1) is not ('\0' or '\r' or '\n' or '\f');

    private string ReadEscape()
    {
        _pos++;
        var hexStart = _pos;
        while (_pos < _source.Length && _pos - hexStart < 6 && Uri.IsHexDigit(_source[_pos])) _pos++;
        if (_pos == hexStart) return _source[_pos++].ToString();

        var codePoint = Convert.ToInt32(_source[hexStart.._pos], 16);
        if (_pos < _source.Length && IsCssWhitespace(_source[_pos]))
        {
            if (_source[_pos] == '\r' && Peek(1) == '\n') _pos++;
            if (_source[_pos] is '\r' or '\n' or '\f') _line++;
            _pos++;
        }
        return codePoint is > 0 and <= 0x10ffff and not (>= 0xd800 and <= 0xdfff)
            ? char.ConvertFromUtf32(codePoint)
            : "\ufffd";
    }

    private bool WouldStartIdentifier()
    {
        var c = Peek(0);
        return IsNameStart(c) || IsValidEscape() ||
               c == '-' && (IsNameStart(Peek(1)) || Peek(1) == '-' || IsValidEscapeAt(_pos + 1));
    }

    private bool IsValidEscapeAt(int position) => position < _source.Length && _source[position] == '\\' &&
                                                   position + 1 < _source.Length &&
                                                   _source[position + 1] is not ('\0' or '\r' or '\n' or '\f');

    private static bool IsNameStart(char c) => char.IsLetter(c) || c >= '\u0080' || c == '_';
    private static bool IsIdentStart(char c) => IsNameStart(c) || c == '-';
    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c >= '\u0080' || c == '_' || c == '-';
    private static bool IsCssWhitespace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';
}
