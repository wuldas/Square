using System.Text;

namespace Square.Extensions.Terminal;

/// <summary>
/// Mutable fixed-size terminal grid with cursor, scroll region, resize, and bounded scrollback.
/// </summary>
public sealed class TerminalBuffer
{
    private TerminalCell[][] _lines;
    private readonly List<TerminalCell[]> _scrollback = [];
    private bool _wrapPending;

    /// <summary>Creates a terminal buffer.</summary>
    public TerminalBuffer(int columns, int rows, int maxScrollback = 1000, bool collectScrollback = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(maxScrollback);
        Columns = columns;
        Rows = rows;
        MaxScrollback = maxScrollback;
        CollectScrollback = collectScrollback;
        _lines = CreateLines(rows, columns, TerminalStyle.Default);
        ScrollBottom = rows - 1;
    }

    /// <summary>Number of columns in the visible grid.</summary>
    public int Columns { get; private set; }
    /// <summary>Number of rows in the visible grid.</summary>
    public int Rows { get; private set; }
    /// <summary>Zero-based cursor column.</summary>
    public int CursorColumn { get; private set; }
    /// <summary>Zero-based cursor row.</summary>
    public int CursorRow { get; private set; }
    /// <summary>First row in the inclusive scrolling region.</summary>
    public int ScrollTop { get; private set; }
    /// <summary>Last row in the inclusive scrolling region.</summary>
    public int ScrollBottom { get; private set; }
    /// <summary>Maximum retained scrollback rows.</summary>
    public int MaxScrollback { get; private set; }
    /// <summary>Whether full-screen upward scrolling contributes to scrollback.</summary>
    public bool CollectScrollback { get; }
    /// <summary>Number of retained scrollback rows.</summary>
    public int ScrollbackCount => _scrollback.Count;

    /// <summary>Returns a visible-grid cell.</summary>
    public TerminalCell GetCell(int row, int column)
    {
        ValidateCell(row, column);
        return _lines[row][column];
    }

    /// <summary>Returns a cell from a retained scrollback row.</summary>
    public TerminalCell GetScrollbackCell(int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, _scrollback.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Columns);
        return _scrollback[row][column];
    }

    /// <summary>Changes the bounded scrollback capacity.</summary>
    public void SetMaxScrollback(int maxScrollback)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxScrollback);
        MaxScrollback = maxScrollback;
        TrimScrollback();
    }

    /// <summary>Writes a printable character at the cursor and advances it.</summary>
    public void Write(char character, TerminalStyle style)
    {
        if (_wrapPending)
        {
            _wrapPending = false;
            CursorColumn = 0;
            LineFeed(style);
        }
        _lines[CursorRow][CursorColumn] = new TerminalCell(character, style);
        if (CursorColumn + 1 < Columns)
        {
            CursorColumn++;
            return;
        }
        _wrapPending = true;
    }

    /// <summary>Moves the cursor to column zero.</summary>
    public void CarriageReturn()
    {
        _wrapPending = false;
        CursorColumn = 0;
    }

    /// <summary>Moves down one row, scrolling the active region when necessary.</summary>
    public void LineFeed(TerminalStyle fillStyle)
    {
        _wrapPending = false;
        if (CursorRow == ScrollBottom)
            ScrollUp(1, fillStyle);
        else if (CursorRow + 1 < Rows)
            CursorRow++;
    }

    /// <summary>Moves the cursor one column left.</summary>
    public void Backspace()
    {
        _wrapPending = false;
        if (CursorColumn > 0) CursorColumn--;
    }

    /// <summary>Moves to the next 8-column tab stop.</summary>
    public void Tab()
    {
        _wrapPending = false;
        CursorColumn = Math.Min(Columns - 1, ((CursorColumn / 8) + 1) * 8);
    }

    /// <summary>Sets the cursor position, clamped to the visible grid.</summary>
    public void SetCursor(int row, int column)
    {
        _wrapPending = false;
        CursorRow = Math.Clamp(row, 0, Rows - 1);
        CursorColumn = Math.Clamp(column, 0, Columns - 1);
    }

    /// <summary>Moves the cursor by a relative row and column delta.</summary>
    public void MoveCursor(int rowDelta, int columnDelta) =>
        SetCursor(CursorRow + rowDelta, CursorColumn + columnDelta);

    /// <summary>Sets the inclusive scrolling region and homes the cursor.</summary>
    public void SetScrollRegion(int top, int bottom)
    {
        top = Math.Clamp(top, 0, Rows - 1);
        bottom = Math.Clamp(bottom, 0, Rows - 1);
        if (top >= bottom)
        {
            top = 0;
            bottom = Rows - 1;
        }
        ScrollTop = top;
        ScrollBottom = bottom;
        SetCursor(0, 0);
    }

    /// <summary>Erases part or all of the display using CSI J semantics.</summary>
    public void EraseDisplay(int mode, TerminalStyle style)
    {
        switch (mode)
        {
            case 1:
                for (var row = 0; row < CursorRow; row++) ClearLine(row, 0, Columns, style);
                ClearLine(CursorRow, 0, CursorColumn + 1, style);
                break;
            case 2 or 3:
                for (var row = 0; row < Rows; row++) ClearLine(row, 0, Columns, style);
                if (mode == 3) _scrollback.Clear();
                break;
            default:
                ClearLine(CursorRow, CursorColumn, Columns - CursorColumn, style);
                for (var row = CursorRow + 1; row < Rows; row++) ClearLine(row, 0, Columns, style);
                break;
        }
    }

    /// <summary>Erases part or all of the current line using CSI K semantics.</summary>
    public void EraseLine(int mode, TerminalStyle style)
    {
        switch (mode)
        {
            case 1:
                ClearLine(CursorRow, 0, CursorColumn + 1, style);
                break;
            case 2:
                ClearLine(CursorRow, 0, Columns, style);
                break;
            default:
                ClearLine(CursorRow, CursorColumn, Columns - CursorColumn, style);
                break;
        }
    }

    /// <summary>Inserts blank rows at the cursor within the scrolling region.</summary>
    public void InsertLines(int count, TerminalStyle style)
    {
        if (CursorRow < ScrollTop || CursorRow > ScrollBottom) return;
        count = Math.Clamp(count, 1, ScrollBottom - CursorRow + 1);
        for (var row = ScrollBottom; row >= CursorRow + count; row--)
            _lines[row] = _lines[row - count];
        for (var row = CursorRow; row < CursorRow + count; row++)
            _lines[row] = CreateLine(Columns, style);
    }

    /// <summary>Deletes rows at the cursor within the scrolling region.</summary>
    public void DeleteLines(int count, TerminalStyle style)
    {
        if (CursorRow < ScrollTop || CursorRow > ScrollBottom) return;
        count = Math.Clamp(count, 1, ScrollBottom - CursorRow + 1);
        for (var row = CursorRow; row <= ScrollBottom - count; row++)
            _lines[row] = _lines[row + count];
        for (var row = ScrollBottom - count + 1; row <= ScrollBottom; row++)
            _lines[row] = CreateLine(Columns, style);
    }

    /// <summary>Inserts blank cells at the cursor.</summary>
    public void InsertCharacters(int count, TerminalStyle style)
    {
        count = Math.Clamp(count, 1, Columns - CursorColumn);
        var line = _lines[CursorRow];
        Array.Copy(line, CursorColumn, line, CursorColumn + count, Columns - CursorColumn - count);
        Array.Fill(line, TerminalCell.Blank(style), CursorColumn, count);
    }

    /// <summary>Deletes cells at the cursor and fills the right edge with blanks.</summary>
    public void DeleteCharacters(int count, TerminalStyle style)
    {
        count = Math.Clamp(count, 1, Columns - CursorColumn);
        var line = _lines[CursorRow];
        Array.Copy(line, CursorColumn + count, line, CursorColumn, Columns - CursorColumn - count);
        Array.Fill(line, TerminalCell.Blank(style), Columns - count, count);
    }

    /// <summary>Scrolls the active region upward.</summary>
    public void ScrollUp(int count, TerminalStyle style)
    {
        count = Math.Clamp(count, 1, ScrollBottom - ScrollTop + 1);
        for (var i = 0; i < count; i++)
        {
            if (CollectScrollback && ScrollTop == 0 && ScrollBottom == Rows - 1)
                AddScrollback(_lines[ScrollTop]);
            for (var row = ScrollTop; row < ScrollBottom; row++)
                _lines[row] = _lines[row + 1];
            _lines[ScrollBottom] = CreateLine(Columns, style);
        }
    }

    /// <summary>Scrolls the active region downward.</summary>
    public void ScrollDown(int count, TerminalStyle style)
    {
        count = Math.Clamp(count, 1, ScrollBottom - ScrollTop + 1);
        for (var i = 0; i < count; i++)
        {
            for (var row = ScrollBottom; row > ScrollTop; row--)
                _lines[row] = _lines[row - 1];
            _lines[ScrollTop] = CreateLine(Columns, style);
        }
    }

    /// <summary>Resizes the grid while preserving its top-left content.</summary>
    public void Resize(int columns, int rows, TerminalStyle fillStyle)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        if (columns == Columns && rows == Rows) return;

        var next = CreateLines(rows, columns, fillStyle);
        var copyRows = Math.Min(rows, Rows);
        var copyColumns = Math.Min(columns, Columns);
        for (var row = 0; row < copyRows; row++)
            Array.Copy(_lines[row], next[row], copyColumns);

        if (columns != Columns)
        {
            for (var i = 0; i < _scrollback.Count; i++)
            {
                var resized = CreateLine(columns, fillStyle);
                Array.Copy(_scrollback[i], resized, Math.Min(columns, _scrollback[i].Length));
                _scrollback[i] = resized;
            }
        }

        _lines = next;
        Columns = columns;
        Rows = rows;
        CursorColumn = Math.Clamp(CursorColumn, 0, columns - 1);
        CursorRow = Math.Clamp(CursorRow, 0, rows - 1);
        _wrapPending = false;
        ScrollTop = 0;
        ScrollBottom = rows - 1;
    }

    /// <summary>Clears the visible grid and optionally the retained scrollback.</summary>
    public void Clear(TerminalStyle style, bool clearScrollback = false)
    {
        _lines = CreateLines(Rows, Columns, style);
        CursorColumn = 0;
        CursorRow = 0;
        _wrapPending = false;
        ScrollTop = 0;
        ScrollBottom = Rows - 1;
        if (clearScrollback) _scrollback.Clear();
    }

    /// <summary>Returns a text snapshot of the visible grid.</summary>
    public string GetTextSnapshot(bool includeScrollback = false, bool trimTrailingWhitespace = true)
    {
        var builder = new StringBuilder();
        if (includeScrollback)
        {
            foreach (var line in _scrollback)
                AppendLine(builder, line, trimTrailingWhitespace);
        }
        for (var row = 0; row < Rows; row++)
        {
            AppendLine(builder, _lines[row], trimTrailingWhitespace);
            if (row == Rows - 1 && builder.Length > 0 && builder[^1] == '\n') builder.Length--;
        }
        return builder.ToString();
    }

    internal TerminalCell[] GetLine(int row) => _lines[row];
    internal TerminalCell[] GetScrollbackLine(int row) => _scrollback[row];

    private void AddScrollback(TerminalCell[] line)
    {
        if (MaxScrollback == 0) return;
        _scrollback.Add((TerminalCell[])line.Clone());
        TrimScrollback();
    }

    private void TrimScrollback()
    {
        var excess = _scrollback.Count - MaxScrollback;
        if (excess > 0) _scrollback.RemoveRange(0, excess);
    }

    private void ClearLine(int row, int start, int count, TerminalStyle style)
    {
        if (count > 0) Array.Fill(_lines[row], TerminalCell.Blank(style), start, count);
    }

    private void ValidateCell(int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Rows);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Columns);
    }

    private static TerminalCell[][] CreateLines(int rows, int columns, TerminalStyle style)
    {
        var lines = new TerminalCell[rows][];
        for (var row = 0; row < rows; row++) lines[row] = CreateLine(columns, style);
        return lines;
    }

    private static TerminalCell[] CreateLine(int columns, TerminalStyle style)
    {
        var line = new TerminalCell[columns];
        Array.Fill(line, TerminalCell.Blank(style));
        return line;
    }

    private static void AppendLine(StringBuilder builder, TerminalCell[] line, bool trimTrailingWhitespace)
    {
        var length = line.Length;
        if (trimTrailingWhitespace)
            while (length > 0 && line[length - 1].Character == ' ') length--;
        for (var i = 0; i < length; i++) builder.Append(line[i].Character);
        builder.Append('\n');
    }
}
