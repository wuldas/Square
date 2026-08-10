using System.Globalization;
using System.Text;

namespace Square.Extensions.Terminal;

/// <summary>
/// Stateful ANSI/VT parser supporting common control characters, cursor/edit commands,
/// scrolling regions, DEC private modes, and SGR colors and attributes.
/// </summary>
public sealed class AnsiVtParser
{
    private readonly TerminalScreen _screen;
    private readonly StringBuilder _csi = new();
    private ParserState _state;
    private char? _pendingHighSurrogate;

    /// <summary>Creates a parser that writes to <paramref name="screen"/>.</summary>
    public AnsiVtParser(TerminalScreen screen)
    {
        _screen = screen ?? throw new ArgumentNullException(nameof(screen));
    }

    /// <summary>Feeds a string into the parser.</summary>
    public void Feed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Feed(text.AsSpan());
    }

    /// <summary>Feeds a span into the parser while preserving incomplete escape sequences.</summary>
    public void Feed(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (_pendingHighSurrogate is { } high)
            {
                _pendingHighSurrogate = null;
                if (char.IsLowSurrogate(character))
                {
                    ProcessRune(new Rune(high, character));
                    continue;
                }
                ProcessRune(Rune.ReplacementChar);
            }
            if (char.IsHighSurrogate(character))
                _pendingHighSurrogate = character;
            else
                ProcessRune(char.IsLowSurrogate(character) ? Rune.ReplacementChar : new Rune(character));
        }
    }

    /// <summary>Resets parser state and active rendition without clearing screen contents.</summary>
    public void Reset()
    {
        _state = ParserState.Ground;
        _csi.Clear();
        _pendingHighSurrogate = null;
        _screen.CurrentStyle = TerminalStyle.Default;
        _screen.CursorVisible = true;
        _screen.Buffer.SetScrollRegion(0, _screen.Buffer.Rows - 1);
    }

    private void ProcessRune(Rune rune)
    {
        if (rune.IsAscii)
        {
            Process((char)rune.Value);
            return;
        }
        if (_state == ParserState.Ground)
            _screen.Buffer.Write(rune.ToString(), _screen.CurrentStyle);
    }

    private void Process(char character)
    {
        switch (_state)
        {
            case ParserState.Escape:
                ProcessEscape(character);
                return;
            case ParserState.Csi:
                ProcessCsi(character);
                return;
        }

        switch (character)
        {
            case '\x1b':
                _state = ParserState.Escape;
                break;
            case '\r':
                _screen.Buffer.CarriageReturn();
                break;
            case '\n' or '\v' or '\f':
                _screen.Buffer.LineFeed(_screen.CurrentStyle);
                break;
            case '\b':
                _screen.Buffer.Backspace();
                break;
            case '\t':
                _screen.Buffer.Tab();
                break;
            default:
                if (character >= ' ' && character != '\x7f')
                    _screen.Buffer.Write(character.ToString(), _screen.CurrentStyle);
                break;
        }
    }

    private void ProcessEscape(char character)
    {
        _state = ParserState.Ground;
        switch (character)
        {
            case '[':
                _csi.Clear();
                _state = ParserState.Csi;
                break;
            case '7':
                _screen.SaveCursor();
                break;
            case '8':
                _screen.RestoreCursor();
                break;
            case 'D':
                _screen.Buffer.LineFeed(_screen.CurrentStyle);
                break;
            case 'E':
                _screen.Buffer.CarriageReturn();
                _screen.Buffer.LineFeed(_screen.CurrentStyle);
                break;
            case 'M':
                if (_screen.Buffer.CursorRow == _screen.Buffer.ScrollTop)
                    _screen.Buffer.ScrollDown(1, _screen.CurrentStyle);
                else
                    _screen.Buffer.MoveCursor(-1, 0);
                break;
            case 'c':
                _screen.UseAlternateBuffer(false);
                _screen.CurrentStyle = TerminalStyle.Default;
                _screen.CursorVisible = true;
                _screen.PrimaryBuffer.Clear(TerminalStyle.Default, clearScrollback: true);
                break;
        }
    }

    private void ProcessCsi(char character)
    {
        if (character is >= '@' and <= '~')
        {
            ExecuteCsi(character, _csi.ToString());
            _csi.Clear();
            _state = ParserState.Ground;
            return;
        }

        if (character == '\x1b')
        {
            _csi.Clear();
            _state = ParserState.Escape;
            return;
        }

        if (_csi.Length < 128) _csi.Append(character);
    }

    private void ExecuteCsi(char command, string raw)
    {
        var privateMode = raw.StartsWith("?", StringComparison.Ordinal);
        if (privateMode) raw = raw[1..];
        var parameters = ParseParameters(raw);
        var buffer = _screen.Buffer;
        var count = Positive(parameters, 0, 1);

        if (privateMode && command is 'h' or 'l')
        {
            SetPrivateModes(parameters, command == 'h');
            return;
        }

        switch (command)
        {
            case 'A': buffer.MoveCursor(-count, 0); break;
            case 'B': buffer.MoveCursor(count, 0); break;
            case 'C': buffer.MoveCursor(0, count); break;
            case 'D': buffer.MoveCursor(0, -count); break;
            case 'E': buffer.SetCursor(buffer.CursorRow + count, 0); break;
            case 'F': buffer.SetCursor(buffer.CursorRow - count, 0); break;
            case 'G': buffer.SetCursor(buffer.CursorRow, Positive(parameters, 0, 1) - 1); break;
            case 'H' or 'f':
                buffer.SetCursor(Positive(parameters, 0, 1) - 1, Positive(parameters, 1, 1) - 1);
                break;
            case 'J': buffer.EraseDisplay(Value(parameters, 0, 0), _screen.CurrentStyle); break;
            case 'K': buffer.EraseLine(Value(parameters, 0, 0), _screen.CurrentStyle); break;
            case 'L': buffer.InsertLines(count, _screen.CurrentStyle); break;
            case 'M': buffer.DeleteLines(count, _screen.CurrentStyle); break;
            case 'P': buffer.DeleteCharacters(count, _screen.CurrentStyle); break;
            case '@': buffer.InsertCharacters(count, _screen.CurrentStyle); break;
            case 'S': buffer.ScrollUp(count, _screen.CurrentStyle); break;
            case 'T': buffer.ScrollDown(count, _screen.CurrentStyle); break;
            case 'r':
                buffer.SetScrollRegion(
                    Positive(parameters, 0, 1) - 1,
                    Positive(parameters, 1, buffer.Rows) - 1);
                break;
            case 's': _screen.SaveCursor(); break;
            case 'u': _screen.RestoreCursor(); break;
            case 'm': ApplySgr(parameters); break;
        }
    }

    private void SetPrivateModes(IReadOnlyList<int?> parameters, bool enabled)
    {
        foreach (var parameter in parameters)
        {
            switch (parameter)
            {
                case 25:
                    _screen.CursorVisible = enabled;
                    break;
                case 1049:
                    _screen.UseAlternateBuffer(enabled, clear: enabled);
                    break;
            }
        }
    }

    private void ApplySgr(IReadOnlyList<int?> parameters)
    {
        if (parameters.Count == 0) parameters = [0];
        var style = _screen.CurrentStyle;
        for (var i = 0; i < parameters.Count; i++)
        {
            var code = parameters[i] ?? 0;
            switch (code)
            {
                case 0: style = TerminalStyle.Default; break;
                case 1: style = style with { Bold = true }; break;
                case 2: style = style with { Dim = true }; break;
                case 3: style = style with { Italic = true }; break;
                case 4: style = style with { Underline = true }; break;
                case 7: style = style with { Inverse = true }; break;
                case 9: style = style with { Strike = true }; break;
                case 21 or 22: style = style with { Bold = false, Dim = false }; break;
                case 23: style = style with { Italic = false }; break;
                case 24: style = style with { Underline = false }; break;
                case 27: style = style with { Inverse = false }; break;
                case 29: style = style with { Strike = false }; break;
                case >= 30 and <= 37: style = style with { Foreground = TerminalColor.FromIndex((byte)(code - 30)) }; break;
                case 39: style = style with { Foreground = TerminalColor.Default }; break;
                case >= 40 and <= 47: style = style with { Background = TerminalColor.FromIndex((byte)(code - 40)) }; break;
                case 49: style = style with { Background = TerminalColor.Default }; break;
                case >= 90 and <= 97: style = style with { Foreground = TerminalColor.FromIndex((byte)(code - 90 + 8)) }; break;
                case >= 100 and <= 107: style = style with { Background = TerminalColor.FromIndex((byte)(code - 100 + 8)) }; break;
                case 38:
                    style = style with { Foreground = ReadExtendedColor(parameters, ref i, style.Foreground) };
                    break;
                case 48:
                    style = style with { Background = ReadExtendedColor(parameters, ref i, style.Background) };
                    break;
            }
        }
        _screen.CurrentStyle = style;
    }

    private static TerminalColor ReadExtendedColor(
        IReadOnlyList<int?> parameters,
        ref int index,
        TerminalColor fallback)
    {
        if (index + 2 < parameters.Count && parameters[index + 1] == 5 && parameters[index + 2] is { } palette)
        {
            index += 2;
            return TerminalColor.FromIndex((byte)Math.Clamp(palette, 0, 255));
        }
        if (index + 4 < parameters.Count && parameters[index + 1] == 2 &&
            parameters[index + 2] is { } red && parameters[index + 3] is { } green && parameters[index + 4] is { } blue)
        {
            index += 4;
            return TerminalColor.FromRgb(
                (byte)Math.Clamp(red, 0, 255),
                (byte)Math.Clamp(green, 0, 255),
                (byte)Math.Clamp(blue, 0, 255));
        }
        return fallback;
    }

    private static List<int?> ParseParameters(string raw)
    {
        if (raw.Length == 0) return [];
        var result = new List<int?>();
        foreach (var token in raw.Split(';'))
        {
            result.Add(int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? value
                : null);
        }
        return result;
    }

    private static int Value(IReadOnlyList<int?> parameters, int index, int fallback) =>
        index < parameters.Count && parameters[index].HasValue ? parameters[index]!.Value : fallback;

    private static int Positive(IReadOnlyList<int?> parameters, int index, int fallback)
    {
        var value = Value(parameters, index, fallback);
        return value <= 0 ? fallback : value;
    }

    private enum ParserState
    {
        Ground,
        Escape,
        Csi,
    }
}
