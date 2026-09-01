using System.Globalization;
using System.Text;
using Square.Controls.Animation;
using Square.Events;
using Square.Graphics;
using Square.Platform;
using Square.UI;

namespace Square.Controls;

/// <summary>文本编辑器公共契约，描述光标、选区与输入处理。</summary>
public interface ITextEditor
{
    /// <summary>光标位置索引。</summary>
    int CaretIndex { get; }
    /// <summary>选区起始索引。</summary>
    int SelectionStart { get; }
    /// <summary>选区长度。</summary>
    int SelectionLength { get; }
    /// <summary>选中文本。</summary>
    string SelectedText { get; }
    /// <summary>是否允许复制当前选区。</summary>
    bool CanCopySelection { get; }
    /// <summary>是否允许剪切当前选区。</summary>
    bool CanCutSelection { get; }
    /// <summary>复制和粘贴快捷键是否需要同时按下 Shift。</summary>
    bool ClipboardShortcutsRequireShift => false;
    /// <summary>光标矩形（屏幕坐标）。</summary>
    Rect CaretRect { get; }

    /// <summary>处理文本输入。</summary>
    void HandleTextInput(string text);
    /// <summary>处理键盘按键。</summary>
    void HandleKey(int keyCode, bool shift = false, bool control = false);
    /// <summary>
    /// 处理指针按下。
    /// 返回 <c>true</c> 表示进入文本拖选；返回 <c>false</c> 表示点击已被控件消费（如 gutter 折叠 / Alt 多光标），宿主不应开始拖选。
    /// </summary>
    /// <param name="extendSelection">Shift 扩展选区。</param>
    /// <param name="addCursor">Alt 添加光标（多光标编辑器可选支持）。</param>
    bool HandlePointerDown(Point point, bool extendSelection = false, bool addCursor = false);
    /// <summary>处理指针移动。</summary>
    void HandlePointerMove(Point point);
    /// <summary>处理指针抬起。</summary>
    void HandlePointerUp(Point point);
    /// <summary>选中指定位置处的单词。</summary>
    void SelectWordAt(Point point);
    /// <summary>全选。</summary>
    void SelectAll();
    /// <summary>删除当前选区，返回是否实际删除。</summary>
    bool DeleteSelection();
    /// <summary>切换光标闪烁，返回是否触发了视觉变化。</summary>
    bool ToggleCaretBlink();
    /// <summary>重置光标闪烁状态。</summary>
    void ResetCaretBlink();

    /// <summary>
    /// 按指针位置解析光标样式；返回 <c>null</c> 时宿主使用默认文本光标。
    /// 用于行号/折叠槽等非编辑区域显示箭头。
    /// </summary>
    CursorKind? ResolveCursorAt(Point point) => null;
}

/// <summary>文本编辑器基类，提供光标、选区、键盘与指针交互的通用实现。</summary>
public abstract class TextEditorBase : UIElement, ITextEditor
{
    private const float DefaultFontSize = 14f;
    private const float ContentPaddingX = 8f;
    private const float ContentPaddingY = 8f;
    private const int MaxHistoryEntries = 100;
    private int _caretIndex;
    private int _selectionAnchor;
    private bool _isDragging;
    private float _horizontalScroll;
    private float _verticalScroll;
    private float? _preferredX;
    private float _caretOpacity = 1f;
    private float _caretBlinkTarget;
    private double _nextCaretTransitionSeconds;
    private Animation<float>? _caretBlinkAnimation;
    private readonly System.Diagnostics.Stopwatch _caretClock = System.Diagnostics.Stopwatch.StartNew();
    private readonly List<EditorSnapshot> _undoHistory = [];
    private readonly List<EditorSnapshot> _redoHistory = [];
    private bool _updatingValue;
    private string _knownValue = "";
    private string _changeBaseline = "";
    private bool _hasChangeBaseline;
    private bool _hasUserEditSinceFocus;

    protected abstract bool IsMultiline { get; }
    protected virtual bool CanEditText => true;
    protected virtual bool PaintEditorChrome => true;
    protected virtual bool ShowCaret => true;
    protected virtual float TextPaddingX => ContentPaddingX;
    protected virtual float TextPaddingY => ContentPaddingY;

    protected TextEditorBase()
    {
        AddEventListener("focus", ResetCaretBlink);
        AddEventListener("blur", CollapseSelectionOnBlur);
    }

    /// <summary>当前文本值。</summary>
    public string Value
    {
        get => GetProperty<string>(nameof(Value)) ?? "";
        set => SetProperty(nameof(Value), NormalizeNewlines(value ?? ""));
    }

    /// <summary>占位提示文本。</summary>
    public string Placeholder
    {
        get => GetProperty<string>(nameof(Placeholder)) ?? "";
        set => SetProperty(nameof(Placeholder), value);
    }

    /// <summary>用于绘制的显示文本（如密码框替换为星号）。</summary>
    protected virtual string DisplayValue => Value;

    /// <inheritdoc/>
    public int CaretIndex => _caretIndex;
    /// <inheritdoc/>
    public int SelectionStart => Math.Min(_caretIndex, _selectionAnchor);
    /// <inheritdoc/>
    public int SelectionLength => Math.Abs(_caretIndex - _selectionAnchor);
    /// <inheritdoc/>
    public string SelectedText => SelectionLength == 0 ? "" : Value.Substring(SelectionStart, SelectionLength);
    /// <inheritdoc/>
    public virtual bool CanCopySelection => true;
    /// <inheritdoc/>
    public virtual bool CanCutSelection => true;
    /// <summary>是否存在可撤销的编辑。</summary>
    public bool CanUndo => _undoHistory.Count > 0;
    /// <summary>是否存在可重做的编辑。</summary>
    public bool CanRedo => _redoHistory.Count > 0;
    /// <inheritdoc/>
    public Rect CaretRect => GetCaretRect();
    /// <summary>选区背景色。</summary>
    public Color SelectionBackground
    {
        get => Properties.HasValue(nameof(SelectionBackground))
            ? GetProperty<Color>(nameof(SelectionBackground))
            : Color.FromRgb(51, 144, 255);
        set => SetProperty(nameof(SelectionBackground), value);
    }
    /// <summary>选区前景色。</summary>
    public Color SelectionForeground
    {
        get => Properties.HasValue(nameof(SelectionForeground)) ? GetProperty<Color>(nameof(SelectionForeground)) : Color.White;
        set => SetProperty(nameof(SelectionForeground), value);
    }

    /// <inheritdoc/>
    public override void Paint(IRenderContext context)
    {
        if (PaintEditorChrome && string.IsNullOrWhiteSpace(Style.Get("appearance")))
            ControlDrawing.DrawInputFrame(context, this);
        EnsureCaretVisible();
        context.PushClip(PaintEditorChrome
            ? new Rect(Geometry.X + 1, Geometry.Y + 1, Math.Max(0, Geometry.Width - 2), Math.Max(0, Geometry.Height - 2))
            : Geometry);

        var fontSize = GetFontSize();
        var lineHeight = GetLineHeight(fontSize);
        var textColor = ControlDrawing.GetStyledColor(this, "color", Color.Black);
        var textMaxSize = new Size(Math.Max(1, Geometry.Width - TextPaddingX * 2 - 2), float.MaxValue);
        var selectionBackground = ControlDrawing.GetStyledColor(
            this,
            Style.Get("selection-background-color") != null ? "selection-background-color" : "selection-background",
            SelectionBackground);
        var selectionForeground = ControlDrawing.GetStyledColor(this, "selection-color", SelectionForeground);

        if (string.IsNullOrEmpty(Value))
        {
            if (!string.IsNullOrEmpty(Placeholder))
                ControlDrawing.DrawText(
                    context, this, Placeholder, GetTextOrigin(fontSize, lineHeight),
                    Color.FromRgb(117, 117, 117), fontSize, lineHeight, useStyledColor: false,
                    maxSize: textMaxSize);
        }
        else
        {
            var displayValue = DisplayValue;
            var selectionRects = GetSelectionRects(displayValue);
            ControlDrawing.DrawText(context, this, displayValue, GetTextOrigin(fontSize, lineHeight), textColor,
                fontSize, lineHeight, maxSize: textMaxSize);
            foreach (var rect in selectionRects)
                context.FillRect(rect, new SolidColorBrush(selectionBackground));
            foreach (var rect in selectionRects)
            {
                context.PushClip(rect);
                ControlDrawing.DrawText(
                    context, this, displayValue, GetTextOrigin(fontSize, lineHeight),
                    selectionForeground, fontSize, lineHeight, useStyledColor: false, maxSize: textMaxSize);
                context.PopClip();
            }
        }

        if (ShowCaret && IsFocused && SelectionLength == 0 && _caretOpacity > 0.01f)
        {
            var caretColor = ControlDrawing.GetStyledColor(this, "caret-color", textColor);
            context.FillRect(CaretRect, new SolidColorBrush(Color.FromRgba(caretColor.R, caretColor.G, caretColor.B, (byte)Math.Clamp(_caretOpacity * 255f, 0f, 255f))));
        }

        context.PopClip();
    }

    /// <inheritdoc/>
    public void HandleTextInput(string text)
    {
        if (!CanEditText || !IsEnabled || string.IsNullOrEmpty(text)) return;
        text = NormalizeNewlines(text);
        if (!IsMultiline) text = text.Replace("\n", "");
        text = FilterInput(text);
        if (text.Length == 0) return;
        ReplaceSelection(text);
    }

    /// <summary>过滤用户输入文本，默认原样返回。</summary>
    protected virtual string FilterInput(string text) => text;

    /// <inheritdoc/>
    public void HandleKey(int keyCode, bool shift = false, bool control = false)
    {
        if (!IsEnabled) return;
        switch (keyCode)
        {
            case 90 when CanEditText && control && shift:
                Redo();
                return;
            case 90 when CanEditText && control:
                Undo();
                return;
            case 89 when CanEditText && control:
                Redo();
                return;
            case 8 when CanEditText:
                Backspace();
                return;
            case 13 when CanEditText && IsMultiline:
                ReplaceSelection("\n");
                return;
            case 35:
                MoveCaret(control ? Value.Length : CurrentLine().End, shift);
                return;
            case 36:
                MoveCaret(control ? 0 : CurrentLine().Start, shift);
                return;
            case 37:
                MoveHorizontal(-1, shift, control);
                return;
            case 38 when IsMultiline:
                MoveVertical(-1, shift);
                return;
            case 39:
                MoveHorizontal(1, shift, control);
                return;
            case 40 when IsMultiline:
                MoveVertical(1, shift);
                return;
            case 38 or 40:
                return;
            case 46 when CanEditText:
                DeleteForward();
                return;
            case 65 when control:
                SelectAll();
                return;
            case 88 when CanEditText && control:
                DeleteSelection();
                return;
        }
    }

    /// <inheritdoc/>
    public bool HandlePointerDown(Point point, bool extendSelection = false, bool addCursor = false)
    {
        if (!IsEnabled) return false;
        _ = addCursor;
        var index = HitTestIndex(point);
        if (!extendSelection) _selectionAnchor = index;
        _caretIndex = index;
        _isDragging = true;
        _preferredX = null;
        ResetCaretBlink();
        InvalidatePaint();
        return true;
    }

    /// <inheritdoc/>
    public void HandlePointerMove(Point point)
    {
        if (!_isDragging) return;
        _caretIndex = HitTestIndex(point);
        _preferredX = null;
        ResetCaretBlink();
        InvalidatePaint();
    }

    /// <inheritdoc/>
    public void HandlePointerUp(Point point)
    {
        if (!_isDragging) return;
        _caretIndex = HitTestIndex(point);
        _isDragging = false;
        _preferredX = null;
        ResetCaretBlink();
        InvalidatePaint();
    }

    /// <inheritdoc/>
    public void SelectWordAt(Point point)
    {
        var index = HitTestIndex(point);
        var (start, end) = FindWordAt(Value, index);
        _selectionAnchor = start;
        _caretIndex = end;
        _isDragging = false;
        _preferredX = null;
        ResetCaretBlink();
        InvalidatePaint();
    }

    /// <inheritdoc/>
    public void SelectAll()
    {
        _selectionAnchor = 0;
        _caretIndex = Value.Length;
        _preferredX = null;
        ResetCaretBlink();
        InvalidatePaint();
    }

    /// <inheritdoc/>
    public bool DeleteSelection()
    {
        if (!CanEditText || SelectionLength == 0) return false;
        ReplaceSelection("");
        return true;
    }

    /// <summary>撤销最近一次用户编辑。</summary>
    public bool Undo()
    {
        if (!CanEditText || _undoHistory.Count == 0) return false;
        var snapshot = Pop(_undoHistory);
        PushHistory(_redoHistory, CaptureSnapshot());
        RestoreSnapshot(snapshot);
        return true;
    }

    /// <summary>重做最近一次已撤销的用户编辑。</summary>
    public bool Redo()
    {
        if (!CanEditText || _redoHistory.Count == 0) return false;
        var snapshot = Pop(_redoHistory);
        PushHistory(_undoHistory, CaptureSnapshot());
        RestoreSnapshot(snapshot);
        return true;
    }

    /// <inheritdoc/>
    public bool ToggleCaretBlink()
    {
        if (!IsFocused || SelectionLength > 0) return false;
        var now = _caretClock.Elapsed.TotalSeconds;
        if ((_caretBlinkAnimation == null || _caretBlinkAnimation.IsComplete) && now < _nextCaretTransitionSeconds)
            return false;
        if (_caretBlinkAnimation == null || _caretBlinkAnimation.IsComplete)
        {
            _caretBlinkTarget = _caretOpacity > 0.5f ? 0f : 1f;
            _caretBlinkAnimation = CreateCaretBlinkAnimation(_caretOpacity, _caretBlinkTarget);
            _caretBlinkAnimation.Start();
        }
        _caretBlinkAnimation.Update(1f / 30f);
        if (_caretBlinkAnimation.IsComplete)
            _nextCaretTransitionSeconds = now + (_caretBlinkTarget <= 0.01f ? 0.45d : 0.7d);
        InvalidatePaint();
        return true;
    }

    /// <inheritdoc/>
    public void ResetCaretBlink()
    {
        _caretOpacity = 1f;
        _caretBlinkTarget = 0f;
        _nextCaretTransitionSeconds = _caretClock.Elapsed.TotalSeconds + 0.7d;
        _caretBlinkAnimation = null;
        InvalidatePaint();
    }

    private Animation<float> CreateCaretBlinkAnimation(float from, float to) => new(
        Interpolate,
        from,
        to,
        0.28f,
        t => t,
        value => _caretOpacity = value);

    private static float Interpolate(float from, float to, float t) => from + (to - from) * t;

    protected override void OnPropertyChanged(string name)
    {
        base.OnPropertyChanged(name);
        if (name != nameof(Value)) return;
        var value = Value;
        if (!_updatingValue && !string.Equals(value, _knownValue, StringComparison.Ordinal))
        {
            _undoHistory.Clear();
            _redoHistory.Clear();
        }
        _knownValue = value;
        if (!IsFocused)
        {
            _caretIndex = Value.Length;
            _selectionAnchor = _caretIndex;
        }
        else
        {
            _caretIndex = Math.Clamp(_caretIndex, 0, Value.Length);
            _selectionAnchor = Math.Clamp(_selectionAnchor, 0, Value.Length);
        }
    }

    private void ReplaceSelection(string replacement)
    {
        var before = CaptureSnapshot();
        var start = SelectionStart;
        var length = SelectionLength;
        var nextValue = Value.Remove(start, length).Insert(start, replacement);
        if (nextValue == Value && length == 0) return;
        PushHistory(_undoHistory, before);
        _redoHistory.Clear();
        SetValueFromEdit(nextValue);
        _caretIndex = start + replacement.Length;
        _selectionAnchor = _caretIndex;
        _preferredX = null;
        ResetCaretBlink();
        EnsureCaretVisible();
        DispatchEvent(StandardEvents.CreateInput());
        InvalidatePaint();
    }

    private EditorSnapshot CaptureSnapshot() => new(Value, _caretIndex);

    private void RestoreSnapshot(EditorSnapshot snapshot)
    {
        SetValueFromEdit(snapshot.Value);
        _caretIndex = Math.Clamp(snapshot.Caret, 0, Value.Length);
        _selectionAnchor = _caretIndex;
        _preferredX = null;
        ResetCaretBlink();
        EnsureCaretVisible();
        DispatchEvent(StandardEvents.CreateInput());
        InvalidatePaint();
    }

    private void SetValueFromEdit(string value)
    {
        _updatingValue = true;
        try
        {
            Value = value;
            if (_hasChangeBaseline) _hasUserEditSinceFocus = true;
        }
        finally
        {
            _updatingValue = false;
        }
    }

    private static EditorSnapshot Pop(List<EditorSnapshot> history)
    {
        var index = history.Count - 1;
        var snapshot = history[index];
        history.RemoveAt(index);
        return snapshot;
    }

    private static void PushHistory(List<EditorSnapshot> history, EditorSnapshot snapshot)
    {
        if (history.Count == MaxHistoryEntries)
            history.RemoveAt(0);
        history.Add(snapshot);
    }

    private void Backspace()
    {
        if (DeleteSelection()) return;
        if (_caretIndex == 0) return;
        var previous = PreviousCaretStop(Value, _caretIndex);
        _selectionAnchor = previous;
        ReplaceSelection("");
    }

    private void DeleteForward()
    {
        if (DeleteSelection()) return;
        if (_caretIndex >= Value.Length) return;
        _selectionAnchor = NextCaretStop(Value, _caretIndex);
        ReplaceSelection("");
    }

    private void MoveHorizontal(int direction, bool extend, bool byWord)
    {
        if (!extend && SelectionLength > 0)
        {
            MoveCaret(direction < 0 ? SelectionStart : SelectionStart + SelectionLength, false);
            return;
        }
        var target = direction < 0
            ? byWord ? PreviousWordIndex(Value, _caretIndex) : PreviousCaretStop(Value, _caretIndex)
            : byWord ? NextWordIndex(Value, _caretIndex) : NextCaretStop(Value, _caretIndex);
        MoveCaret(target, extend);
    }

    private void MoveVertical(int direction, bool extend)
    {
        var lines = GetLines(Value);
        var currentLineIndex = FindLineIndex(lines, _caretIndex);
        var targetLineIndex = Math.Clamp(currentLineIndex + direction, 0, lines.Count - 1);
        if (targetLineIndex == currentLineIndex) return;
        var current = lines[currentLineIndex];
        var target = lines[targetLineIndex];
        _preferredX ??= MeasureRange(Value, current.Start, Math.Max(0, _caretIndex - current.Start));
        var targetIndex = target.Start + HitTestLine(Value.AsSpan(target.Start, target.Length), _preferredX.Value);
        MoveCaret(targetIndex, extend, preservePreferredX: true);
    }

    private void MoveCaret(int index, bool extend, bool preservePreferredX = false)
    {
        _caretIndex = Math.Clamp(index, 0, Value.Length);
        if (!extend) _selectionAnchor = _caretIndex;
        if (!preservePreferredX) _preferredX = null;
        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidatePaint();
    }

    private int HitTestIndex(Point point)
    {
        var displayValue = DisplayValue;
        var fontSize = GetFontSize();
        var lineHeight = GetLineHeight(fontSize);
        var authoritative = CreateEditorTextLayout(displayValue, fontSize, lineHeight);
        if (authoritative.TryGetAuthoritativeSnapshot(out var snapshot))
        {
            var origin = GetTextOrigin(fontSize, lineHeight);
            return Math.Clamp(snapshot.HitTestPoint(new Point(point.X - origin.X, point.Y - origin.Y)),
                0, displayValue.Length);
        }
        var lines = GetLines(displayValue);
        var lineIndex = IsMultiline
            ? Math.Clamp((int)MathF.Floor((point.Y - GetFirstLineTop(lineHeight)) / lineHeight), 0, lines.Count - 1)
            : 0;
        var line = lines[lineIndex];
        var localX = point.X - Geometry.X - TextPaddingX + _horizontalScroll;
        return line.Start + HitTestLine(displayValue.AsSpan(line.Start, line.Length), localX);
    }

    private List<Rect> GetSelectionRects(string displayValue)
    {
        var result = new List<Rect>();
        if (SelectionLength == 0) return result;
        var fontSize = GetFontSize();
        var lineHeight = GetLineHeight(fontSize);
        var origin = GetTextOrigin(fontSize, lineHeight);
        var authoritative = CreateEditorTextLayout(displayValue, fontSize, lineHeight);
        if (authoritative.TryGetAuthoritativeSnapshot(out var snapshot))
        {
            foreach (var rect in snapshot.GetSelectionRects(SelectionStart, SelectionLength))
                result.Add(rect.Offset(origin.X, origin.Y));
            return result;
        }
        var lines = GetLines(displayValue);
        var selectionEnd = SelectionStart + SelectionLength;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var start = Math.Max(SelectionStart, line.Start);
            var end = Math.Min(selectionEnd, line.End);
            var includesNewline = i < lines.Count - 1 && selectionEnd > line.End;
            if (end < start || end == start && !includesNewline) continue;
            var x = MeasureRange(displayValue, line.Start, start - line.Start);
            var width = MeasureRange(displayValue, start, end - start);
            if (end > start)
            {
                var font = ControlDrawing.ResolveFont(this, fontSize);
                var firstRune = DecodeRuneAt(displayValue, start);
                var lastRune = DecodeRuneBefore(displayValue, end);
                var firstInk = ControlDrawing.MeasureRenderedRuneInkBounds(firstRune, font);
                var lastInk = ControlDrawing.MeasureRenderedRuneInkBounds(lastRune, font);
                x += firstInk.Left;
                width += lastInk.Right - MeasureCharacterAdvanceBefore(displayValue, end) - firstInk.Left;
            }
            if (includesNewline) width += 6;
            var visualLineBox = GetVisualLineBox(fontSize, lineHeight, i);
            var lineFont = ControlDrawing.ResolveFont(this, fontSize);
            var metrics = TextMetrics.GetFontMetrics(lineFont);
            var ascent = Math.Max(0, -metrics.Ascent);
            var descent = Math.Max(0, metrics.Descent);
            // 渲染基线 = 文本 origin（GetFirstLineTop(lineHeight) + 行偏移）+ 基线偏移；
            // 选区矩形必须覆盖字形墨迹的完整边界：当字体 ascent+descent 大于 line-height
            // （如大 ascender 的 CJK 字体）时，字形顶部/底部会越出按 line-height 定位的行盒，
            // 否则选区外的字形像素会透出原始文字色。
            var baseline = origin.Y + i * lineHeight + TextMetrics.GetBaselineOffset(lineFont, lineHeight);
            var selectionTop = Math.Min(visualLineBox.Top, baseline - ascent);
            var selectionBottom = Math.Max(visualLineBox.Top + visualLineBox.Height, baseline + descent);
            result.Add(new Rect(
                origin.X + x,
                selectionTop,
                Math.Max(2, width),
                Math.Max(1, selectionBottom - selectionTop)));
        }
        return result;
    }

    private Rect GetCaretRect()
    {
        EnsureCaretVisible();
        var fontSize = GetFontSize();
        var lineHeight = GetLineHeight(fontSize);
        var displayValue = DisplayValue;
        var origin = GetTextOrigin(fontSize, lineHeight);
        var authoritative = CreateEditorTextLayout(displayValue, fontSize, lineHeight);
        if (authoritative.TryGetAuthoritativeSnapshot(out var snapshot))
        {
            var caret = snapshot.GetCaretPoint(_caretIndex);
            return new Rect(
                MathF.Round(origin.X + caret.X),
                MathF.Round(origin.Y + caret.Y),
                1,
                Math.Max(1, Math.Min(lineHeight, Geometry.Bottom - origin.Y - caret.Y - 1)));
        }
        var lines = GetLines(displayValue);
        var lineIndex = FindLineIndex(lines, _caretIndex);
        var line = lines[lineIndex];
        var width = MeasureRange(displayValue, line.Start, Math.Max(0, _caretIndex - line.Start));
        var visualLineBox = GetVisualLineBox(fontSize, lineHeight, lineIndex);
        return new Rect(
            MathF.Round(Geometry.X + TextPaddingX - _horizontalScroll + width),
            MathF.Round(visualLineBox.Top),
            1,
            Math.Max(1, Math.Min(visualLineBox.Height, Geometry.Bottom - visualLineBox.Top - 1)));
    }

    private Point GetTextOrigin(float fontSize, float lineHeight)
        => new(
            Geometry.X + TextPaddingX - _horizontalScroll,
            GetFirstLineTop(lineHeight));

    private float GetFirstLineTop(float lineHeight) => IsMultiline
        ? Geometry.Y + TextPaddingY - _verticalScroll
        : Geometry.Y + Math.Max(1, (Geometry.Height - lineHeight) / 2f);

    private (float Top, float Height) GetVisualLineBox(float fontSize, float lineHeight, int lineIndex)
    {
        var font = ControlDrawing.ResolveFont(this, fontSize);
        var naturalLineHeight = TextMetrics.GetFontMetrics(font).Height;
        var visualHeight = Math.Max(lineHeight, naturalLineHeight);
        var lineTop = GetFirstLineTop(lineHeight) + lineIndex * lineHeight;
        return (MathF.Round(lineTop + (lineHeight - visualHeight) / 2f), visualHeight);
    }

    private protected float GetFontSize() => ControlDrawing.GetStyledFloat(this, "font-size", DefaultFontSize);

    private protected float GetLineHeight(float fontSize) => ControlDrawing.GetStyledLineHeight(this, fontSize);

    private void EnsureCaretVisible()
    {
        if (IsMultiline)
        {
            _horizontalScroll = 0;
            if (!IsFocused)
            {
                _verticalScroll = 0;
                return;
            }
            if (Geometry.Width <= 0 || Geometry.Height <= 0)
            {
                _verticalScroll = 0;
                return;
            }
            var fontSize = GetFontSize();
            var lineHeight = GetLineHeight(fontSize);
            var lines = GetLines(DisplayValue);
            var lineIndex = FindLineIndex(lines, _caretIndex);
            var visualLineBox = GetVisualLineBox(fontSize, lineHeight, lineIndex);
            var viewportTop = Geometry.Y + 1;
            var viewportBottom = Geometry.Bottom - 1;
            var viewportHeight = Math.Max(0, viewportBottom - viewportTop);
            if (visualLineBox.Height >= viewportHeight)
                _verticalScroll = Math.Max(0, _verticalScroll + visualLineBox.Top - viewportTop);
            else if (visualLineBox.Top < viewportTop)
                _verticalScroll = Math.Max(0, _verticalScroll - (viewportTop - visualLineBox.Top));
            else if (visualLineBox.Top + visualLineBox.Height > viewportBottom)
                _verticalScroll += visualLineBox.Top + visualLineBox.Height - viewportBottom;
            return;
        }
        var width = MeasureRange(DisplayValue, 0, _caretIndex);
        var viewport = Math.Max(0, Geometry.Width - TextPaddingX * 2 - 2);
        if (width - _horizontalScroll > viewport) _horizontalScroll = width - viewport;
        if (width - _horizontalScroll < 0) _horizontalScroll = width;
        _horizontalScroll = Math.Max(0, _horizontalScroll);
    }

    private LineRange CurrentLine()
    {
        var lines = GetLines(Value);
        return lines[FindLineIndex(lines, _caretIndex)];
    }

    private static List<LineRange> GetLines(string text)
    {
        var lines = new List<LineRange>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            lines.Add(new LineRange(start, i - start));
            start = i + 1;
        }
        lines.Add(new LineRange(start, text.Length - start));
        return lines;
    }

    private static int FindLineIndex(List<LineRange> lines, int index)
    {
        for (var i = 0; i < lines.Count; i++)
            if (index <= lines[i].End || i == lines.Count - 1) return i;
        return lines.Count - 1;
    }

    private int HitTestLine(ReadOnlySpan<char> line, float x)
    {
        if (x <= 0) return 0;
        var text = line.ToString();
        var fontSize = GetFontSize();
        var authoritative = CreateEditorTextLayout(text, fontSize, GetLineHeight(fontSize));
        if (authoritative.TryGetAuthoritativeSnapshot(out var snapshot))
            return snapshot.HitTestPoint(new Point(x, 0));
        var width = 0f;
        var index = 0;
        while (index < line.Length)
        {
            var length = char.IsHighSurrogate(line[index]) && index + 1 < line.Length && char.IsLowSurrogate(line[index + 1]) ? 2 : 1;
            var advance = MeasureCharacterAdvance(line.Slice(index, length));
            if (x < width + advance / 2f) return index;
            width += advance;
            index += length;
        }
        return line.Length;
    }

    private float MeasureRange(string text, int start, int length)
    {
        if (length <= 0) return 0;
        var fontSize = GetFontSize();
        var authoritative = CreateEditorTextLayout(text, fontSize, GetLineHeight(fontSize));
        if (authoritative.TryGetAuthoritativeSnapshot(out var snapshot))
        {
            var first = snapshot.GetCaretPoint(start);
            var last = snapshot.GetCaretPoint(start + length, trailing: true);
            return MathF.Abs(last.X - first.X);
        }
        var width = 0f;
        var span = text.AsSpan(start, length);
        var index = 0;
        while (index < span.Length)
        {
            var characterLength = char.IsHighSurrogate(span[index]) && index + 1 < span.Length && char.IsLowSurrogate(span[index + 1]) ? 2 : 1;
            width += MeasureCharacterAdvance(span.Slice(index, characterLength));
            index += characterLength;
        }
        return width;
    }

    private float MeasureCharacterAdvance(ReadOnlySpan<char> character)
    {
        var editorFont = ControlDrawing.ResolveFont(this, GetFontSize());
        Rune.DecodeFromUtf16(character, out var rune, out _);
        return ControlDrawing.MeasureRenderedRuneAdvance(rune, editorFont);
    }

    private TextLayout CreateEditorTextLayout(string text, float fontSize, float lineHeight)
    {
        var font = ControlDrawing.ResolveFont(this, fontSize);
        return new TextLayout(text, font)
        {
            MaxSize = new Size(Math.Max(1, Geometry.Width - TextPaddingX * 2 - 2), float.MaxValue),
            Alignment = ControlDrawing.ResolveTextAlignment(this),
            Direction = ControlDrawing.ResolveTextDirection(this),
            UnicodeBidi = ControlDrawing.ResolveUnicodeBidi(this),
            WhiteSpace = IsMultiline ? ControlDrawing.ResolveWhiteSpace(this) : TextWhiteSpaceMode.Nowrap,
            LetterSpacing = ControlDrawing.ResolveTextLength(this, "letter-spacing", font.Size),
            WordSpacing = ControlDrawing.ResolveTextLength(this, "word-spacing", font.Size),
            TextTransform = ControlDrawing.ResolveTextTransform(this),
            LineHeight = lineHeight / Math.Max(1, font.Size)
        };
    }

    private float MeasureCharacterAdvanceBefore(string text, int end)
    {
        var start = PreviousCodePointIndex(text, end);
        return MeasureCharacterAdvance(text.AsSpan(start, end - start));
    }

    private static Rune DecodeRuneAt(string text, int start)
    {
        Rune.DecodeFromUtf16(text.AsSpan(start), out var rune, out _);
        return rune;
    }

    private static Rune DecodeRuneBefore(string text, int end)
    {
        var start = PreviousCodePointIndex(text, end);
        Rune.DecodeFromUtf16(text.AsSpan(start, end - start), out var rune, out _);
        return rune;
    }

    private static int PreviousCodePointIndex(string text, int index)
    {
        if (index <= 0) return 0;
        index--;
        if (index > 0 && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1])) index--;
        return index;
    }

    private static int NextCodePointIndex(string text, int index)
    {
        if (index >= text.Length) return text.Length;
        return index + (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1);
    }

    private int PreviousCaretStop(string text, int index)
    {
        if (index <= 0) return 0;
        if (TryGetEditorSnapshot(text, out var snapshot))
            return snapshot.Lines.SelectMany(line => line.Clusters)
                .SelectMany(cluster => new[] { cluster.StartOffset, cluster.EndOffset })
                .Where(offset => offset < index)
                .DefaultIfEmpty(0)
                .Max();
        var starts = StringInfo.ParseCombiningCharacters(text);
        return starts.LastOrDefault(offset => offset < index);
    }

    private int NextCaretStop(string text, int index)
    {
        if (index >= text.Length) return text.Length;
        if (TryGetEditorSnapshot(text, out var snapshot))
            return snapshot.Lines.SelectMany(line => line.Clusters)
                .SelectMany(cluster => new[] { cluster.StartOffset, cluster.EndOffset })
                .Where(offset => offset > index)
                .DefaultIfEmpty(text.Length)
                .Min();
        var starts = StringInfo.ParseCombiningCharacters(text);
        return starts.FirstOrDefault(offset => offset > index, text.Length);
    }

    private bool TryGetEditorSnapshot(string text, out ITextLayoutSnapshot snapshot)
    {
        var fontSize = GetFontSize();
        return CreateEditorTextLayout(text, fontSize, GetLineHeight(fontSize))
            .TryGetAuthoritativeSnapshot(out snapshot);
    }

    private static int PreviousWordIndex(string text, int index)
    {
        while (index > 0 && char.IsWhiteSpace(text[index - 1])) index--;
        while (index > 0 && !char.IsWhiteSpace(text[index - 1])) index--;
        return index;
    }

    private static int NextWordIndex(string text, int index)
    {
        while (index < text.Length && !char.IsWhiteSpace(text[index])) index++;
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        return index;
    }

    private static (int Start, int End) FindWordAt(string text, int index)
    {
        if (text.Length == 0) return (0, 0);
        index = Math.Clamp(index, 0, text.Length - 1);
        if (!char.IsLetterOrDigit(text[index]) && text[index] != '_') return (index, index + 1);
        var start = index;
        var end = index + 1;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) start--;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
        return (start, end);
    }

    private static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private void CollapseSelectionOnBlur()
    {
        _selectionAnchor = _caretIndex;
        _isDragging = false;
        _caretOpacity = 0f;
        _caretBlinkAnimation = null;
        InvalidatePaint();
    }

    private void BeginChangeSession()
    {
        _changeBaseline = Value;
        _hasChangeBaseline = true;
        _hasUserEditSinceFocus = false;
    }

    private void CommitChangeSession()
    {
        var changed = _hasChangeBaseline && _hasUserEditSinceFocus &&
            !string.Equals(Value, _changeBaseline, StringComparison.Ordinal);
        _changeBaseline = Value;
        _hasChangeBaseline = false;
        _hasUserEditSinceFocus = false;
        if (changed) DispatchEvent(StandardEvents.CreateChange());
    }

    /// <inheritdoc />
    protected override void OnBeforeUnfocus()
    {
        CommitChangeSession();
        base.OnBeforeUnfocus();
    }

    /// <inheritdoc />
    protected override void OnBeforeFocus()
    {
        BeginChangeSession();
        base.OnBeforeFocus();
    }

    /// <inheritdoc />
    protected override void OnDetachedCore()
    {
        _changeBaseline = Value;
        _hasChangeBaseline = false;
        _hasUserEditSinceFocus = false;
        base.OnDetachedCore();
    }

    private readonly record struct LineRange(int Start, int Length)
    {
        internal int End => Start + Length;
    }

    private readonly record struct EditorSnapshot(string Value, int Caret);
}

/// <summary>单行输入框，支持 <c>text</c>、<c>password</c>、<c>number</c> 类型。</summary>
public class Input : TextEditorBase
{
    private static readonly string[] AuthorVerticalTextProperties =
    [
        "font", "font-family", "font-size", "font-weight", "font-style", "line-height"
    ];

    /// <inheritdoc/>
    protected override bool IsMultiline => false;

    /// <inheritdoc/>
    protected override float TextPaddingX =>
        ControlDrawing.GetStyledFloat(this, "border-left-width", 0) +
        ControlDrawing.GetStyledFloat(this, "padding-left", 8);

    /// <summary>输入类型。</summary>
    public string Type
    {
        get => GetProperty<string>(nameof(Type)) ?? "text";
        set => SetProperty(nameof(Type), NormalizeType(value));
    }

    /// <inheritdoc/>
    protected override string DisplayValue => Type == "password" ? new string('*', Value.Length) : Value;

    /// <inheritdoc/>
    public override bool CanCopySelection => Type != "password";

    /// <inheritdoc/>
    public override bool CanCutSelection => Type != "password";

    /// <inheritdoc/>
    protected override string FilterInput(string text) => Type == "number" ? FilterNumberInput(text) : text;

    /// <inheritdoc/>
    protected override void OnPropertyChanged(string name)
    {
        base.OnPropertyChanged(name);
        if (name == nameof(Type) && Type == "number")
            Value = NormalizeNumber(Value);
        if (name == nameof(Value) && Type == "number")
        {
            var normalized = NormalizeNumber(Value);
            if (normalized != Value) Value = normalized;
        }
    }

    /// <inheritdoc/>
    public override Size Measure(Size availableSize)
    {
        var defaultSize = ControlDrawing.UsesWidgetAppearance(this) ? new Size(169, 15) : new Size(200, 36);
        if (!AuthorVerticalTextProperties.Any(Style.IsAuthorSpecified)) return defaultSize;

        var fontSize = GetFontSize();
        var fontHeight = TextMetrics.GetFontMetrics(ControlDrawing.ResolveFont(this, fontSize)).Height;
        var height = MathF.Ceiling(Math.Max(defaultSize.Height, Math.Max(GetLineHeight(fontSize), fontHeight)));
        return new Size(defaultSize.Width, height);
    }

    private string FilterNumberInput(string text)
    {
        var current = Value.Remove(SelectionStart, SelectionLength);
        var result = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            var candidate = current.Insert(SelectionStart, result.ToString() + ch);
            if (IsNumberCandidate(candidate)) result.Append(ch);
        }
        return result.ToString();
    }

    private static string NormalizeType(string? value)
    {
        value = value?.Trim().ToLowerInvariant();
        return value is "password" or "number" ? value : "text";
    }

    private static string NormalizeNumber(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            var candidate = result.ToString() + ch;
            if (IsNumberCandidate(candidate)) result.Append(ch);
        }
        return result.ToString();
    }

    private static bool IsNumberCandidate(string value)
    {
        if (value.Length == 0 || value == "-" || value == "." || value == "-.") return true;
        return double.TryParse(
            value,
            System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture,
            out _);
    }
}

/// <summary>多行文本输入区域。</summary>
public class TextArea : TextEditorBase
{
    /// <inheritdoc/>
    protected override bool IsMultiline => true;
    /// <inheritdoc/>
    protected override float TextPaddingX =>
        ControlDrawing.GetStyledFloat(this, "border-left-width", 0) +
        ControlDrawing.GetStyledFloat(this, "padding-left", 8);
    /// <inheritdoc/>
    protected override float TextPaddingY =>
        ControlDrawing.GetStyledFloat(this, "border-top-width", 0) +
        ControlDrawing.GetStyledFloat(this, "padding-top", 8);
    /// <inheritdoc/>
    public override Size Measure(Size availableSize) =>
        ControlDrawing.UsesWidgetAppearance(this) ? new Size(155, 30) : new Size(300, 88);
}
