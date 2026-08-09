namespace Square.Extensions.Terminal;

/// <summary>
/// Owns the primary and alternate terminal buffers plus parser-visible cursor and style state.
/// </summary>
public sealed class TerminalScreen
{
    private readonly TerminalBuffer _primary;
    private readonly TerminalBuffer _alternate;
    private SavedState _primarySaved;
    private SavedState _alternateSaved;

    /// <summary>Creates a terminal screen.</summary>
    public TerminalScreen(int columns = 80, int rows = 24, int maxScrollback = 1000)
    {
        _primary = new TerminalBuffer(columns, rows, maxScrollback, collectScrollback: true);
        _alternate = new TerminalBuffer(columns, rows, 0, collectScrollback: false);
    }

    /// <summary>The currently active terminal buffer.</summary>
    public TerminalBuffer Buffer => IsAlternateBuffer ? _alternate : _primary;
    /// <summary>The primary terminal buffer, including its scrollback.</summary>
    public TerminalBuffer PrimaryBuffer => _primary;
    /// <summary>The alternate terminal buffer.</summary>
    public TerminalBuffer AlternateBuffer => _alternate;
    /// <summary>Whether the alternate buffer is active.</summary>
    public bool IsAlternateBuffer { get; private set; }
    /// <summary>Whether the cursor should be painted.</summary>
    public bool CursorVisible { get; set; } = true;
    /// <summary>The style assigned to subsequently written cells.</summary>
    public TerminalStyle CurrentStyle { get; set; } = TerminalStyle.Default;

    /// <summary>Saves the active cursor position and style.</summary>
    public void SaveCursor()
    {
        var state = new SavedState(Buffer.CursorRow, Buffer.CursorColumn, CurrentStyle);
        if (IsAlternateBuffer) _alternateSaved = state;
        else _primarySaved = state;
    }

    /// <summary>Restores the active cursor position and style.</summary>
    public void RestoreCursor()
    {
        var state = IsAlternateBuffer ? _alternateSaved : _primarySaved;
        Buffer.SetCursor(state.Row, state.Column);
        CurrentStyle = state.Style;
    }

    /// <summary>Switches between primary and alternate buffers.</summary>
    public void UseAlternateBuffer(bool enabled, bool clear = true)
    {
        if (enabled == IsAlternateBuffer) return;
        if (enabled)
        {
            SaveCursor();
            IsAlternateBuffer = true;
            if (clear) _alternate.Clear(TerminalStyle.Default, clearScrollback: true);
            _alternate.SetCursor(0, 0);
        }
        else
        {
            IsAlternateBuffer = false;
            RestoreCursor();
        }
    }

    /// <summary>Resizes both primary and alternate buffers.</summary>
    public void Resize(int columns, int rows)
    {
        _primary.Resize(columns, rows, TerminalStyle.Default);
        _alternate.Resize(columns, rows, TerminalStyle.Default);
    }

    /// <summary>Changes the primary buffer scrollback limit.</summary>
    public void SetMaxScrollback(int maxScrollback) => _primary.SetMaxScrollback(maxScrollback);

    /// <summary>Returns a text snapshot from the active buffer.</summary>
    public string GetTextSnapshot(bool includeScrollback = false, bool trimTrailingWhitespace = true) =>
        Buffer.GetTextSnapshot(includeScrollback && !IsAlternateBuffer, trimTrailingWhitespace);

    private readonly record struct SavedState(int Row, int Column, TerminalStyle Style);
}
