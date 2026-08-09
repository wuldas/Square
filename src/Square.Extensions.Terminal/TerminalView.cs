using System.Globalization;
using System.Text;
using Square.Controls;
using Square.Events;
using Square.Graphics;
using Square.Platform;
using Square.Text;
using Square.UI;

namespace Square.Extensions.Terminal;

/// <summary>Provides terminal input bytes represented as a .NET string.</summary>
public sealed class TerminalInputEventArgs : EventArgs
{
    /// <summary>Creates terminal input event data.</summary>
    public TerminalInputEventArgs(string data)
    {
        Data = data;
    }

    /// <summary>The control sequence or pasted text to write to the terminal transport.</summary>
    public string Data { get; }
}

/// <summary>
/// Square terminal control that paints an ANSI/VT screen and translates keyboard, text,
/// pointer selection, and wheel input for an SSH or process transport.
/// </summary>
public sealed class TerminalView : UIElement, ITextEditor
{
    private const float DefaultFontSize = 14f;
    private const float DefaultPadding = 6f;
    private readonly TerminalScreen _screen;
    private readonly AnsiVtParser _parser;
    private int _selectionAnchor;
    private int _selectionCaret;
    private bool _dragging;
    private int _scrollbackOffset;
    private bool _caretVisible = true;

    /// <summary>Creates an 80 by 24 terminal view with 1000 scrollback rows.</summary>
    public TerminalView() : this(80, 24, 1000) { }

    /// <summary>Creates a terminal view with an explicit grid and scrollback limit.</summary>
    public TerminalView(int columns, int rows, int maxScrollback = 1000)
    {
        _screen = new TerminalScreen(columns, rows, maxScrollback);
        _parser = new AnsiVtParser(_screen);
        Style.Set("font-family", "monospace");
        Style.Set("font-size", "14px");
        AddEventListener("wheel", OnWheel);
        AddEventListener("focus", ResetCaretBlink);
        AddEventListener("blur", ResetCaretBlink);
        SyncSelectionToCursor();
    }

    /// <summary>Raised when local input should be written to the SSH or process stream.</summary>
    public event EventHandler<TerminalInputEventArgs>? Input;

    /// <summary>The terminal screen model.</summary>
    public TerminalScreen Screen => _screen;
    /// <summary>The active terminal buffer.</summary>
    public TerminalBuffer Buffer => _screen.Buffer;
    /// <summary>Current grid width.</summary>
    public int Columns => Buffer.Columns;
    /// <summary>Current grid height.</summary>
    public int Rows => Buffer.Rows;
    /// <summary>Maximum number of retained primary-screen rows.</summary>
    public int MaxScrollback
    {
        get => _screen.PrimaryBuffer.MaxScrollback;
        set
        {
            _screen.SetMaxScrollback(value);
            ClampScrollbackOffset();
            InvalidatePaint();
        }
    }
    /// <summary>Whether arrange automatically derives rows and columns from the monospaced cell size.</summary>
    public bool AutoResize { get; set; } = true;
    /// <summary>Default terminal foreground color.</summary>
    public Color DefaultForeground { get; set; } = Color.FromRgb(220, 220, 220);
    /// <summary>Default terminal background color.</summary>
    public Color DefaultBackground { get; set; } = Color.FromRgb(30, 30, 30);
    /// <summary>Selection background color.</summary>
    public Color SelectionBackground { get; set; } = Color.FromRgba(38, 79, 120, 210);
    /// <summary>Number of historical rows currently displayed above the live screen.</summary>
    public int ScrollbackOffset => _scrollbackOffset;

    /// <inheritdoc/>
    public int CaretIndex => _selectionCaret;
    /// <inheritdoc/>
    public int SelectionStart => Math.Min(_selectionAnchor, _selectionCaret);
    /// <inheritdoc/>
    public int SelectionLength => Math.Abs(_selectionCaret - _selectionAnchor);
    /// <inheritdoc/>
    public string SelectedText => GetSelectedText();
    /// <inheritdoc/>
    public bool CanCopySelection => true;
    /// <inheritdoc/>
    public bool CanCutSelection => false;
    /// <inheritdoc/>
    public Rect CaretRect => ComputeCaretRect();

    /// <summary>Feeds remote terminal output into the VT parser.</summary>
    public void Feed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Feed(text.AsSpan());
    }

    /// <summary>Feeds remote terminal output into the VT parser.</summary>
    public void Feed(ReadOnlySpan<char> text)
    {
        var followOutput = _scrollbackOffset == 0;
        _parser.Feed(text);
        if (followOutput) _scrollbackOffset = 0;
        else ClampScrollbackOffset();
        if (SelectionLength == 0) SyncSelectionToCursor();
        else ClampSelection();
        ResetCaretBlink();
        InvalidatePaint();
    }

    /// <summary>Resizes the primary and alternate terminal grids.</summary>
    public void Resize(int columns, int rows)
    {
        _screen.Resize(columns, rows);
        ClampScrollbackOffset();
        ClampSelection();
        InvalidatePaint();
    }

    /// <summary>Returns an active-screen text snapshot.</summary>
    public string GetTextSnapshot(bool includeScrollback = false, bool trimTrailingWhitespace = true) =>
        _screen.GetTextSnapshot(includeScrollback, trimTrailingWhitespace);

    /// <inheritdoc/>
    public override Size Measure(Size availableSize) => new(
        ConstrainWidth(float.IsFinite(availableSize.Width) ? availableSize.Width : 640),
        ConstrainHeight(float.IsFinite(availableSize.Height) ? availableSize.Height : 384));

    /// <inheritdoc/>
    public override void Arrange(Rect finalRect)
    {
        base.Arrange(finalRect);
        if (!AutoResize || finalRect.IsEmpty) return;
        var (font, cellWidth, lineHeight) = GetMetrics();
        _ = font;
        var columns = Math.Max(1, (int)MathF.Floor(Math.Max(0, finalRect.Width - DefaultPadding * 2) / cellWidth));
        var rows = Math.Max(1, (int)MathF.Floor(Math.Max(0, finalRect.Height - DefaultPadding * 2) / lineHeight));
        if (columns != Columns || rows != Rows) Resize(columns, rows);
    }

    /// <inheritdoc/>
    public override void Paint(IRenderContext context)
    {
        var (font, cellWidth, lineHeight) = GetMetrics();
        context.FillRect(Geometry, new SolidColorBrush(DefaultBackground));
        var border = IsFocused ? Color.FromRgb(0, 122, 204) : Color.FromRgb(75, 75, 75);
        context.DrawRect(Geometry, Pen.FromColor(border, IsFocused ? 2 : 1));
        var viewport = new Rect(
            Geometry.X + DefaultPadding,
            Geometry.Y + DefaultPadding,
            Math.Max(0, Geometry.Width - DefaultPadding * 2),
            Math.Max(0, Geometry.Height - DefaultPadding * 2));
        context.PushClip(viewport);

        var visibleRows = Math.Max(1, (int)MathF.Ceiling(viewport.Height / lineHeight));
        var totalRows = GetTotalRows();
        var firstRow = Math.Max(0, totalRows - visibleRows - _scrollbackOffset);
        var lastRow = Math.Min(totalRows, firstRow + visibleRows);
        for (var absoluteRow = firstRow; absoluteRow < lastRow; absoluteRow++)
        {
            var y = viewport.Y + (absoluteRow - firstRow) * lineHeight;
            PaintRow(context, font, cellWidth, lineHeight, absoluteRow, y, viewport.X);
        }

        if (IsFocused && SelectionLength == 0 && _screen.CursorVisible && _caretVisible && _scrollbackOffset == 0)
        {
            var caret = ComputeTerminalCursorRect(cellWidth, lineHeight);
            context.FillRect(caret, new SolidColorBrush(Color.FromRgba(220, 220, 220, 150)));
        }
        context.PopClip();
    }

    /// <inheritdoc/>
    public void HandleTextInput(string text)
    {
        if (!IsEnabled || string.IsNullOrEmpty(text)) return;
        RaiseInput(text);
    }

    /// <inheritdoc/>
    public void HandleKey(int keyCode, bool shift = false, bool control = false)
    {
        if (!IsEnabled) return;
        if (control && keyCode is >= 65 and <= 90)
        {
            RaiseInput(((char)(keyCode - 64)).ToString(CultureInfo.InvariantCulture));
            return;
        }

        var sequence = keyCode switch
        {
            8 => "\x7f",
            9 => shift ? "\x1b[Z" : "\t",
            13 => "\r",
            27 => "\x1b",
            33 => "\x1b[5~",
            34 => "\x1b[6~",
            35 => "\x1b[F",
            36 => "\x1b[H",
            37 => ModifierSequence('D', shift, control),
            38 => ModifierSequence('A', shift, control),
            39 => ModifierSequence('C', shift, control),
            40 => ModifierSequence('B', shift, control),
            45 => "\x1b[2~",
            46 => "\x1b[3~",
            >= 112 and <= 115 => $"\x1bO{(char)('P' + keyCode - 112)}",
            116 => "\x1b[15~",
            117 => "\x1b[17~",
            118 => "\x1b[18~",
            119 => "\x1b[19~",
            120 => "\x1b[20~",
            121 => "\x1b[21~",
            122 => "\x1b[23~",
            123 => "\x1b[24~",
            _ => null,
        };
        if (sequence != null) RaiseInput(sequence);
    }

    /// <inheritdoc/>
    public bool HandlePointerDown(Point point, bool extendSelection = false, bool addCursor = false)
    {
        if (!IsEnabled) return false;
        _ = addCursor;
        var index = HitTestIndex(point);
        if (!extendSelection) _selectionAnchor = index;
        _selectionCaret = index;
        _dragging = true;
        _caretVisible = true;
        InvalidatePaint();
        return true;
    }

    /// <inheritdoc/>
    public void HandlePointerMove(Point point)
    {
        if (!_dragging) return;
        _selectionCaret = HitTestIndex(point);
        InvalidatePaint();
    }

    /// <inheritdoc/>
    public void HandlePointerUp(Point point)
    {
        if (!_dragging) return;
        _selectionCaret = HitTestIndex(point);
        _dragging = false;
        InvalidatePaint();
    }

    /// <inheritdoc/>
    public void SelectWordAt(Point point)
    {
        var index = HitTestIndex(point);
        var total = GetTotalRows() * Columns;
        if (total == 0) return;
        index = Math.Clamp(index, 0, total - 1);
        var row = index / Columns;
        var column = index % Columns;
        var start = column;
        var end = column;
        while (start > 0 && IsWord(GetDisplayCell(row, start - 1).Character)) start--;
        while (end < Columns && IsWord(GetDisplayCell(row, end).Character)) end++;
        _selectionAnchor = row * Columns + start;
        _selectionCaret = row * Columns + end;
        InvalidatePaint();
    }

    /// <inheritdoc/>
    public void SelectAll()
    {
        _selectionAnchor = 0;
        _selectionCaret = GetTotalRows() * Columns;
        InvalidatePaint();
    }

    /// <inheritdoc/>
    public bool DeleteSelection() => false;

    /// <inheritdoc/>
    public bool ToggleCaretBlink()
    {
        if (!IsFocused || SelectionLength > 0 || !_screen.CursorVisible) return false;
        _caretVisible = !_caretVisible;
        InvalidatePaint();
        return true;
    }

    /// <inheritdoc/>
    public void ResetCaretBlink()
    {
        if (_caretVisible) return;
        _caretVisible = true;
        InvalidatePaint();
    }

    /// <inheritdoc/>
    public CursorKind? ResolveCursorAt(Point point) => Geometry.Contains(point) ? CursorKind.Text : null;

    private void PaintRow(
        IRenderContext context,
        Font baseFont,
        float cellWidth,
        float lineHeight,
        int absoluteRow,
        float y,
        float x)
    {
        for (var column = 0; column < Columns; column++)
        {
            var cell = GetDisplayCell(absoluteRow, column);
            var foreground = ResolveColor(cell.Style.Foreground, DefaultForeground);
            var background = ResolveColor(cell.Style.Background, DefaultBackground);
            if (cell.Style.Inverse) (foreground, background) = (background, foreground);
            var rect = new Rect(x + column * cellWidth, y, cellWidth, lineHeight);
            if (background != DefaultBackground)
                context.FillRect(rect, new SolidColorBrush(background));
            if (IsSelected(absoluteRow * Columns + column))
                context.FillRect(rect, new SolidColorBrush(SelectionBackground));
            if (cell.Character == ' ') continue;

            var font = baseFont;
            if (cell.Style.Bold) font = font.WithWeight(FontWeight.Bold);
            if (cell.Style.Italic)
                font = new Font(font.Family, font.Size, font.Weight, Square.Graphics.FontStyle.Italic);
            if (cell.Style.Dim)
                foreground = Color.FromRgba(foreground.R, foreground.G, foreground.B, (byte)(foreground.A / 2));
            context.DrawText(
                new TextLayout(cell.Character.ToString(), font),
                new Point(rect.X, rect.Y),
                new SolidColorBrush(foreground));
            if (cell.Style.Underline)
                context.FillRect(new Rect(rect.X, rect.Bottom - 2, rect.Width, 1), new SolidColorBrush(foreground));
            if (cell.Style.Strike)
                context.FillRect(new Rect(rect.X, rect.Y + rect.Height * 0.55f, rect.Width, 1), new SolidColorBrush(foreground));
        }
    }

    private Rect ComputeCaretRect()
    {
        var (_, cellWidth, lineHeight) = GetMetrics();
        if (SelectionLength == 0) return ComputeTerminalCursorRect(cellWidth, lineHeight);
        var totalRows = GetTotalRows();
        var row = Math.Clamp(_selectionCaret / Columns, 0, Math.Max(0, totalRows - 1));
        var column = Math.Clamp(_selectionCaret % Columns, 0, Columns - 1);
        var visibleRows = Math.Max(1, (int)MathF.Ceiling(Math.Max(0, Geometry.Height - DefaultPadding * 2) / lineHeight));
        var firstRow = Math.Max(0, totalRows - visibleRows - _scrollbackOffset);
        return new Rect(
            Geometry.X + DefaultPadding + column * cellWidth,
            Geometry.Y + DefaultPadding + (row - firstRow) * lineHeight,
            Math.Max(1, cellWidth),
            lineHeight);
    }

    private Rect ComputeTerminalCursorRect(float cellWidth, float lineHeight)
    {
        var visibleRows = Math.Max(1, (int)MathF.Ceiling(Math.Max(0, Geometry.Height - DefaultPadding * 2) / lineHeight));
        var totalRows = GetTotalRows();
        var firstRow = Math.Max(0, totalRows - visibleRows - _scrollbackOffset);
        var scrollback = _screen.IsAlternateBuffer ? 0 : _screen.PrimaryBuffer.ScrollbackCount;
        var absoluteRow = scrollback + Buffer.CursorRow;
        return new Rect(
            Geometry.X + DefaultPadding + Buffer.CursorColumn * cellWidth,
            Geometry.Y + DefaultPadding + (absoluteRow - firstRow) * lineHeight,
            Math.Max(1, cellWidth),
            lineHeight);
    }

    private int HitTestIndex(Point point)
    {
        var (_, cellWidth, lineHeight) = GetMetrics();
        var visibleRows = Math.Max(1, (int)MathF.Ceiling(Math.Max(0, Geometry.Height - DefaultPadding * 2) / lineHeight));
        var totalRows = GetTotalRows();
        var firstRow = Math.Max(0, totalRows - visibleRows - _scrollbackOffset);
        var row = firstRow + (int)MathF.Floor((point.Y - Geometry.Y - DefaultPadding) / lineHeight);
        var column = (int)MathF.Round((point.X - Geometry.X - DefaultPadding) / cellWidth);
        row = Math.Clamp(row, 0, Math.Max(0, totalRows - 1));
        column = Math.Clamp(column, 0, Columns);
        return Math.Min(totalRows * Columns, row * Columns + column);
    }

    private string GetSelectedText()
    {
        if (SelectionLength == 0) return "";
        var start = SelectionStart;
        var end = Math.Min(SelectionStart + SelectionLength, GetTotalRows() * Columns);
        var builder = new StringBuilder();
        for (var index = start; index < end; index++)
        {
            if (index > start && index % Columns == 0)
            {
                while (builder.Length > 0 && builder[^1] == ' ') builder.Length--;
                builder.Append('\n');
            }
            var row = index / Columns;
            var column = index % Columns;
            builder.Append(GetDisplayCell(row, column).Character);
        }
        while (builder.Length > 0 && builder[^1] == ' ') builder.Length--;
        return builder.ToString();
    }

    private TerminalCell GetDisplayCell(int absoluteRow, int column)
    {
        var scrollback = _screen.IsAlternateBuffer ? 0 : _screen.PrimaryBuffer.ScrollbackCount;
        return absoluteRow < scrollback
            ? _screen.PrimaryBuffer.GetScrollbackCell(absoluteRow, column)
            : Buffer.GetCell(absoluteRow - scrollback, column);
    }

    private int GetTotalRows() =>
        (_screen.IsAlternateBuffer ? 0 : _screen.PrimaryBuffer.ScrollbackCount) + Buffer.Rows;

    private bool IsSelected(int cellIndex) => cellIndex >= SelectionStart && cellIndex < SelectionStart + SelectionLength;

    private void SyncSelectionToCursor()
    {
        var scrollback = _screen.IsAlternateBuffer ? 0 : _screen.PrimaryBuffer.ScrollbackCount;
        _selectionAnchor = _selectionCaret = (scrollback + Buffer.CursorRow) * Columns + Buffer.CursorColumn;
    }

    private void ClampSelection()
    {
        var maximum = GetTotalRows() * Columns;
        _selectionAnchor = Math.Clamp(_selectionAnchor, 0, maximum);
        _selectionCaret = Math.Clamp(_selectionCaret, 0, maximum);
    }

    private void ClampScrollbackOffset()
    {
        var available = _screen.IsAlternateBuffer ? 0 : _screen.PrimaryBuffer.ScrollbackCount;
        _scrollbackOffset = Math.Clamp(_scrollbackOffset, 0, available);
    }

    private void OnWheel(Event e)
    {
        if (e is not WheelEvent wheel) return;
        var (_, _, lineHeight) = GetMetrics();
        var lines = Math.Max(1, (int)MathF.Ceiling(Math.Abs(wheel.DeltaY) / lineHeight));
        _scrollbackOffset += wheel.DeltaY < 0 ? lines : -lines;
        ClampScrollbackOffset();
        InvalidatePaint();
        e.StopPropagation();
    }

    private (Font Font, float CellWidth, float LineHeight) GetMetrics()
    {
        var font = FontManager.Instance.FromCss(
            Style.GetPropertyValue("font-family"),
            Style.GetPropertyValue("font-size"),
            Style.GetPropertyValue("font-weight"),
            Style.GetPropertyValue("font-style"),
            DefaultFontSize);
        var cellWidth = Math.Max(1, TextMetrics.GetGlyphMetrics(font, new Rune('M')).AdvanceX);
        var lineHeight = ResolveLineHeight(font);
        return (font, cellWidth, lineHeight);
    }

    private float ResolveLineHeight(Font font)
    {
        var value = Style.GetPropertyValue("line-height").Trim();
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(value[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels) && pixels > 0)
            return pixels;
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var multiplier) && multiplier > 0)
            return font.Size * multiplier;
        return TextMetrics.GetLineHeight(font, TextLayout.DefaultLineHeight);
    }

    private void RaiseInput(string data) => Input?.Invoke(this, new TerminalInputEventArgs(data));

    private static bool IsWord(char character) => char.IsLetterOrDigit(character) || character == '_';

    private static string ModifierSequence(char final, bool shift, bool control)
    {
        var modifier = 1 + (shift ? 1 : 0) + (control ? 4 : 0);
        return modifier == 1 ? $"\x1b[{final}" : $"\x1b[1;{modifier}{final}";
    }

    private static Color ResolveColor(TerminalColor color, Color fallback) => color.Kind switch
    {
        TerminalColorKind.Indexed => ResolveIndexedColor(color.Index),
        TerminalColorKind.Rgb => Color.FromRgb(color.Red, color.Green, color.Blue),
        _ => fallback,
    };

    private static Color ResolveIndexedColor(byte index)
    {
        ReadOnlySpan<Color> basic =
        [
            Color.FromRgb(0, 0, 0), Color.FromRgb(205, 49, 49), Color.FromRgb(13, 188, 121), Color.FromRgb(229, 229, 16),
            Color.FromRgb(36, 114, 200), Color.FromRgb(188, 63, 188), Color.FromRgb(17, 168, 205), Color.FromRgb(229, 229, 229),
            Color.FromRgb(102, 102, 102), Color.FromRgb(241, 76, 76), Color.FromRgb(35, 209, 139), Color.FromRgb(245, 245, 67),
            Color.FromRgb(59, 142, 234), Color.FromRgb(214, 112, 214), Color.FromRgb(41, 184, 219), Color.FromRgb(255, 255, 255),
        ];
        if (index < 16) return basic[index];
        if (index >= 232)
        {
            var gray = (byte)(8 + (index - 232) * 10);
            return Color.FromRgb(gray, gray, gray);
        }
        var cube = index - 16;
        var red = cube / 36;
        var green = cube / 6 % 6;
        var blue = cube % 6;
        static byte Component(int value) => (byte)(value == 0 ? 0 : 55 + value * 40);
        return Color.FromRgb(Component(red), Component(green), Component(blue));
    }
}
