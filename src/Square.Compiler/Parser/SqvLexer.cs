namespace Square.Compiler.Parser;

/// <summary>Vue 模板词法 token 种类（独立于 SQX 词法器）。</summary>
internal enum SqvTokenType
{
    OpenTag,
    CloseSelfTag,
    CloseTag,
    EndTag,
    Equals,
    Identifier,
    StringLiteral,
    /// <summary>{{ expr }} 插值，Text 为去除外层花括号并修剪后的表达式。</summary>
    Interpolation,
    Text,
    Eof
}

internal struct SqvToken
{
    public SqvTokenType Type;
    public string Text;
    public int Line;
    public int Column;
    public int Offset;
}

/// <summary>
/// Vue 模板词法器：识别标签、属性名（含 :/@/#/v- 前缀与 .修饰符）、字符串、{{ }} 插值与纯文本。
/// 不依赖 <c>SqxCoreLexer</c>。
/// </summary>
internal sealed class SqvLexer
{
    private readonly string _source;
    private readonly int _baseOffset;
    private readonly bool _tolerant;
    private int _position;
    private int _line = 1;
    private int _column = 1;
    private bool _inTag;

    public SqvLexer(string source, int baseOffset = 0, bool tolerant = false)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _baseOffset = baseOffset;
        _tolerant = tolerant;
    }

    public List<SqvToken> Tokenize()
    {
        var tokens = new List<SqvToken>();
        while (_position < _source.Length)
        {
            var c = _source[_position];

            // 插值 {{ ... }} 仅在标签外识别
            if (!_inTag && c == '{' && Peek(1) == '{')
            {
                var (line, column, offset) = (_line, _column, _position);
                AdvanceChar();
                AdvanceChar();
                var expr = ReadUntilDoubleBrace();
                tokens.Add(New(SqvTokenType.Interpolation, expr, line, column, offset));
                continue;
            }

            if (c == '<')
            {
                var (line, column, offset) = (_line, _column, _position);
                if (Peek(1) == '!')
                {
                    // 注释 <!-- ... -->
                    var end = _source.IndexOf("-->", _position + 4, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        if (_tolerant) break;
                        throw Error("Unclosed Vue comment", offset);
                    }
                    else
                    {
                        _position = end + 3;
                        while (_position > 0 && _source[_position - 1] != '\n') _column++;
                    }
                    continue;
                }
                if (Peek(1) == '/')
                {
                    AdvanceChar();
                    AdvanceChar();
                    _inTag = true;
                    var name = ReadName();
                    SkipWhitespace();
                    if (_position < _source.Length && _source[_position] == '>') AdvanceChar();
                    tokens.Add(New(SqvTokenType.EndTag, name, line, column, offset));
                    _inTag = false;
                }
                else
                {
                    AdvanceChar();
                    _inTag = true;
                    tokens.Add(New(SqvTokenType.OpenTag, "<", line, column, offset));
                }
                continue;
            }

            if (c == '/' && Peek(1) == '>')
            {
                var token = New(SqvTokenType.CloseSelfTag, "/>", _line, _column, _position);
                AdvanceChar();
                AdvanceChar();
                tokens.Add(token);
                _inTag = false;
                continue;
            }

            if (c == '>')
            {
                var token = New(SqvTokenType.CloseTag, ">", _line, _column, _position);
                AdvanceChar();
                tokens.Add(token);
                _inTag = false;
                continue;
            }

            if (c == '=')
            {
                var token = New(SqvTokenType.Equals, "=", _line, _column, _position);
                AdvanceChar();
                tokens.Add(token);
                continue;
            }

            if (c == '"' || c == '\'')
            {
                var (line, column, offset) = (_line, _column, _position);
                tokens.Add(New(SqvTokenType.StringLiteral, ReadString(c), line, column, offset));
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                SkipWhitespace();
                continue;
            }

            if (_inTag && IsNameStart(c))
            {
                var (line, column, offset) = (_line, _column, _position);
                tokens.Add(New(SqvTokenType.Identifier, ReadName(), line, column, offset));
                continue;
            }

            if (!_inTag)
            {
                var (line, column, offset) = (_line, _column, _position);
                var text = ReadText();
                if (!string.IsNullOrWhiteSpace(text))
                    tokens.Add(New(SqvTokenType.Text, text, line, column, offset));
                continue;
            }

            // 标签内无法识别的字符：跳过
            AdvanceChar();
        }

        tokens.Add(New(SqvTokenType.Eof, "", _line, _column, _position));
        return tokens;
    }

    private string ReadUntilDoubleBrace()
    {
        var start = _position;
        while (_position < _source.Length)
        {
            if (_source[_position] == '}' && Peek(1) == '}')
            {
                var result = _source.Substring(start, _position - start).Trim();
                AdvanceChar();
                AdvanceChar();
                return result;
            }
            AdvanceChar();
        }
        if (_tolerant)
            return _source.Substring(start, _position - start).Trim();
        throw Error("Unclosed interpolation; expected '}}'", start - 2);
    }

    private string ReadName()
    {
        var start = _position;
        while (_position < _source.Length && IsNameChar(_source[_position])) AdvanceChar();
        return _source.Substring(start, _position - start);
    }

    private string ReadString(char quote)
    {
        AdvanceChar();
        var start = _position;
        while (_position < _source.Length && _source[_position] != quote)
        {
            if (_source[_position] == '\\' && _position + 1 < _source.Length)
            {
                AdvanceChar();
                AdvanceChar();
            }
            else
            {
                AdvanceChar();
            }
        }
        var result = _source.Substring(start, _position - start);
        if (_position >= _source.Length)
        {
            if (_tolerant) return _source.Substring(start, _position - start);
            throw Error("Unclosed attribute string", start - 1);
        }
        AdvanceChar();
        return result;
    }

    private string ReadText()
    {
        var start = _position;
        while (_position < _source.Length)
        {
            var c = _source[_position];
            if (c == '<' || (c == '{' && Peek(1) == '{')) break;
            AdvanceChar();
        }
        return _source.Substring(start, _position - start);
    }

    private char Peek(int offset) =>
        _position + offset < _source.Length ? _source[_position + offset] : '\0';

    private void SkipWhitespace()
    {
        while (_position < _source.Length && char.IsWhiteSpace(_source[_position])) AdvanceChar();
    }

    private void AdvanceChar()
    {
        if (_source[_position] == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }
        _position++;
    }

    private static SqvToken New(SqvTokenType type, string text, int line, int column, int offset) =>
        new() { Type = type, Text = text, Line = line, Column = column, Offset = offset };

    private static bool IsNameStart(char c) =>
        char.IsLetter(c) || c == '_' || c == ':' || c == '@' || c == '#' || c == '-';

    private static bool IsNameChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == ':' || c == '@' || c == '#' || c == '[' || c == ']';

    private SqxParseException Error(string message, int position) =>
        new(message, _baseOffset + position, "SQV0001");
}
