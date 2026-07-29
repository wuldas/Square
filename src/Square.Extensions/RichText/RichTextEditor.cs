using Square.Controls;
using Square.Events;
using Square.Graphics;
using Square.Text;
using Square.UI;

namespace Square.Extensions.RichText;

public sealed class RichTextEditor : UIElement, ITextEditor, ITextSelectable
{
    private const float DefaultFontSize = 14f;
    private const float DefaultPadding = 8f;
    private readonly RichTextEditorState _state;
    private readonly Square.UI.Text _domText = new();
    private bool _isDragging;

    public RichTextEditor()
        : this(new RichTextDocument())
    {
    }

    public RichTextEditor(RichTextDocument document)
    {
        _state = new RichTextEditorState(document);
        ChildNodes.Add(_domText);
        SyncDomTextAndSelection();
        AddEventListener("focus", ResetCaretBlink);
    }

    public RichTextDocument Document => _state.Document;
    public RichTextSelection RichSelection => _state.Selection;

    public string PlainText
    {
        get => Document.PlainText;
        set
        {
            Document.Blocks.Clear();
            Document.Blocks.AddRange(RichTextDocument.FromPlainText(value).Blocks);
            Document.Normalize();
            _state.SetSelection(RichTextSelection.Collapsed(new RichTextPosition(0, 0)));
            SyncDomTextAndSelection();
            InvalidateLayout();
        }
    }

    public int CaretIndex => ToLinearOffset(_state.Selection.Focus);
    public int SelectionStart => ToLinearOffset(_state.Selection.Start);
    public int SelectionLength => ToLinearOffset(_state.Selection.End) - SelectionStart;
    public string SelectedText => GetSelectedText();
    public string SelectableText => PlainText;
    public Rect SelectableTextBounds => Geometry;
    public bool CanCopySelection => true;
    public bool CanCutSelection => true;
    public bool CanUndo => _state.CanUndo;
    public bool CanRedo => _state.CanRedo;
    public Rect CaretRect => GetCaretRect();
    public Color SelectionBackground
    {
        get => Properties.HasValue(nameof(SelectionBackground))
            ? GetProperty<Color>(nameof(SelectionBackground))
            : Color.FromRgb(51, 144, 255);
        set => SetProperty(nameof(SelectionBackground), value);
    }
    public Color SelectionForeground
    {
        get => Properties.HasValue(nameof(SelectionForeground))
            ? GetProperty<Color>(nameof(SelectionForeground))
            : Color.White;
        set => SetProperty(nameof(SelectionForeground), value);
    }

    public override Size Measure(Size availableSize)
    {
        var font = ResolveFont();
        var lineHeight = GetLineHeight(font);
        var padding = ResolvePadding();
        var contentWidth = float.IsFinite(availableSize.Width)
            ? Math.Max(1, availableSize.Width - padding * 2)
            : float.PositiveInfinity;
        var layouts = BuildLayouts(font, lineHeight, new Point(0, 0), contentWidth);
        var width = layouts.Count == 0 ? 0 : layouts.Max(layout => layout.Bounds.Width);
        var height = layouts.Count == 0 ? lineHeight : layouts.Sum(layout => layout.Bounds.Height);
        return new Size(
            ConstrainWidth(float.IsFinite(availableSize.Width) ? availableSize.Width : Math.Max(width + padding * 2, MinWidth)),
            ConstrainHeight(Math.Max(height + padding * 2, MinHeight)));
    }

    public override void Paint(IRenderContext context)
    {
        var background = IsEnabled ? Color.White : Color.FromRgb(240, 240, 240);
        var border = IsFocused ? Color.FromRgb(0, 95, 184) : Color.FromRgb(165, 170, 176);
        context.FillRect(Geometry, new SolidColorBrush(background));
        context.DrawRect(Geometry, Pen.FromColor(border, IsFocused ? 2 : 1));

        var font = ResolveFont();
        var lineHeight = GetLineHeight(font);
        var padding = ResolvePadding();
        var color = IsEnabled ? Color.Black : Color.FromRgb(125, 130, 136);
        var selectionBackground = GetSelectionColor("selection-background-color", "selection-background", SelectionBackground);
        var selectionForeground = GetSelectionColor("selection-color", null, SelectionForeground);
        var layouts = BuildLayouts(
            font,
            lineHeight,
            new Point(Geometry.X + padding, Geometry.Y + padding),
            Math.Max(1, Geometry.Width - padding * 2));
        for (var i = 0; i < layouts.Count; i++)
        {
            PaintBlock(context, layouts[i], color);
            PaintSelectionForBlock(context, i, layouts[i], selectionBackground);
            PaintSelectionForegroundForBlock(context, i, layouts[i], selectionForeground);
        }

        if (IsFocused && _state.Selection.IsCollapsed)
            context.FillRect(GetCaretRect(layouts), new SolidColorBrush(Color.Black));
    }

    public void HandleTextInput(string text)
    {
        if (!IsEnabled || string.IsNullOrEmpty(text)) return;
        _state.InsertText(text);
        SyncDomTextAndSelection();
        DispatchEvent(StandardEvents.CreateInput());
        InvalidateLayout();
    }

    public void HandleKey(int keyCode, bool shift = false, bool control = false)
    {
        if (!IsEnabled) return;
        switch (keyCode)
        {
            case 8:
                if (_state.DeleteBackward()) DispatchInputChanged();
                return;
            case 13:
                _state.InsertParagraph();
                DispatchInputChanged();
                return;
            case 37:
                MoveHorizontal(-1, shift, control);
                return;
            case 38:
                MoveVertical(-1, shift);
                return;
            case 39:
                MoveHorizontal(1, shift, control);
                return;
            case 40:
                MoveVertical(1, shift);
                return;
            case 35:
                MoveLineBoundary(toStart: false, shift);
                return;
            case 36:
                MoveLineBoundary(toStart: true, shift);
                return;
            case 46:
                if (_state.DeleteForward()) DispatchInputChanged();
                return;
            case 65 when control:
                SelectAll();
                return;
            case 90 when control:
                Undo();
                return;
            case 89 when control:
                Redo();
                return;
            case 88 when control:
                DeleteSelection();
                return;
            case 66 when control:
                ToggleBold();
                return;
            case 73 when control:
                ToggleItalic();
                return;
            case 85 when control:
                ToggleUnderline();
                return;
        }
    }

    public void ToggleBold() => ToggleMarks(new RichTextMarks(Bold: true));

    public void ToggleItalic() => ToggleMarks(new RichTextMarks(Italic: true));

    public void ToggleUnderline() => ToggleMarks(new RichTextMarks(Underline: true));

    public bool Undo()
    {
        if (!_state.Undo()) return false;
        DispatchInputChanged();
        return true;
    }

    public bool Redo()
    {
        if (!_state.Redo()) return false;
        DispatchInputChanged();
        return true;
    }

    public void ClearFormatting()
    {
        if (_state.Selection.IsCollapsed) return;
        _state.SetMarks(RichTextMarks.Empty);
        DispatchInputChanged();
    }

    public void SetForeground(string color)
    {
        if (_state.Selection.IsCollapsed) return;
        _state.ToggleMarks(new RichTextMarks(Foreground: color));
        DispatchInputChanged();
    }

    public RichTextFragment GetSelectedFragment() => _state.GetSelectedFragment();

    public string GetSelectedRichText() => RichTextFragmentCodec.Serialize(_state.GetSelectedFragment());

    public bool InsertRichText(string json)
    {
        if (!RichTextFragmentCodec.TryDeserialize(json, out var fragment) || fragment == null) return false;
        _state.InsertFragment(fragment);
        DispatchInputChanged();
        return true;
    }

    public void InsertFragment(RichTextFragment fragment)
    {
        _state.InsertFragment(fragment);
        DispatchInputChanged();
    }

    public bool HandlePointerDown(Point point, bool extendSelection = false, bool addCursor = false)
    {
        _ = addCursor;
        var position = HitTestPosition(point);
        SetSelection(extendSelection
            ? new RichTextSelection(_state.Selection.Anchor, position)
            : RichTextSelection.Collapsed(position));
        _isDragging = true;
        return true;
    }

    public void HandlePointerMove(Point point)
    {
        if (!_isDragging) return;
        SetSelection(new RichTextSelection(_state.Selection.Anchor, HitTestPosition(point)));
    }

    public void HandlePointerUp(Point point)
    {
        if (!_isDragging) return;
        _isDragging = false;
        SetSelection(new RichTextSelection(_state.Selection.Anchor, HitTestPosition(point)));
    }

    public void SelectWordAt(Point point)
    {
        var position = HitTestPosition(point);
        var text = Document.Blocks[position.BlockIndex].PlainText;
        var (start, end) = RichTextBoundaries.WordAt(text, position.Offset);
        SetSelection(new RichTextSelection(
            new RichTextPosition(position.BlockIndex, start),
            new RichTextPosition(position.BlockIndex, end)));
        _isDragging = false;
    }

    public void SelectAll()
    {
        var lastBlockIndex = Document.Blocks.Count - 1;
        SetSelection(new RichTextSelection(
            new RichTextPosition(0, 0),
            new RichTextPosition(lastBlockIndex, Document.Blocks[lastBlockIndex].PlainText.Length)));
    }

    public void CollapseSelectionToEnd()
    {
        var end = _state.Selection.End;
        SetSelection(RichTextSelection.Collapsed(end));
        _isDragging = false;
    }

    public bool DeleteSelection()
    {
        if (_state.Selection.IsCollapsed) return false;
        _state.DeleteSelection();
        DispatchInputChanged();
        return true;
    }

    public bool ToggleCaretBlink() => false;

    public void ResetCaretBlink() => InvalidatePaint();

    protected override void OnAttachedCore()
    {
        base.OnAttachedCore();
        SyncDomTextAndSelection();
    }

    private void ToggleMarks(RichTextMarks marks)
    {
        if (_state.Selection.IsCollapsed) return;
        _state.ToggleMarks(marks);
        DispatchInputChanged();
    }

    private void DispatchInputChanged()
    {
        SyncDomTextAndSelection();
        DispatchEvent(StandardEvents.CreateInput());
        InvalidateLayout();
    }

    private void MoveHorizontal(int direction, bool extend, bool byWord)
    {
        var offset = CaretIndex;
        var nextOffset = byWord
            ? direction < 0
                ? RichTextBoundaries.PreviousWord(PlainText, offset)
                : RichTextBoundaries.NextWord(PlainText, offset)
            : direction < 0
                ? RichTextBoundaries.PreviousTextElement(PlainText, offset)
                : RichTextBoundaries.NextTextElement(PlainText, offset);
        var next = FromLinearOffset(nextOffset);
        SetSelection(extend ? new RichTextSelection(_state.Selection.Anchor, next) : RichTextSelection.Collapsed(next));
    }

    private void MoveLineBoundary(bool toStart, bool extend)
    {
        var font = ResolveFont();
        var lineHeight = GetLineHeight(font);
        var padding = ResolvePadding();
        var layouts = BuildLayouts(
            font,
            lineHeight,
            new Point(Geometry.X + padding, Geometry.Y + padding),
            Math.Max(1, Geometry.Width - padding * 2));
        var position = _state.Selection.Focus;
        var blockLayout = layouts[position.BlockIndex];
        var line = blockLayout.Lines[blockLayout.GetLineIndex(position.Offset)];
        var next = new RichTextPosition(position.BlockIndex, toStart ? line.StartOffset : line.EndOffset);
        SetSelection(extend ? new RichTextSelection(_state.Selection.Anchor, next) : RichTextSelection.Collapsed(next));
    }

    private void MoveVertical(int direction, bool extend)
    {
        var font = ResolveFont();
        var lineHeight = GetLineHeight(font);
        var padding = ResolvePadding();
        var layouts = BuildLayouts(
            font,
            lineHeight,
            new Point(Geometry.X + padding, Geometry.Y + padding),
            Math.Max(1, Geometry.Width - padding * 2));
        var position = _state.Selection.Focus;
        var blockLayout = layouts[position.BlockIndex];
        var lineIndex = blockLayout.GetLineIndex(position.Offset);
        var caret = blockLayout.GetCaretRect(position.Offset);

        RichTextPosition next;
        if (direction < 0 && lineIndex > 0)
        {
            var target = blockLayout.Lines[lineIndex - 1];
            next = new RichTextPosition(position.BlockIndex, blockLayout.HitTestOffset(new Point(caret.X, target.Bounds.Y + target.Bounds.Height / 2f)));
        }
        else if (direction > 0 && lineIndex < blockLayout.Lines.Count - 1)
        {
            var target = blockLayout.Lines[lineIndex + 1];
            next = new RichTextPosition(position.BlockIndex, blockLayout.HitTestOffset(new Point(caret.X, target.Bounds.Y + target.Bounds.Height / 2f)));
        }
        else
        {
            var blockIndex = position.BlockIndex + direction;
            if (blockIndex < 0 || blockIndex >= layouts.Count) return;
            var targetLayout = layouts[blockIndex];
            var target = direction < 0 ? targetLayout.Lines[^1] : targetLayout.Lines[0];
            next = new RichTextPosition(blockIndex, targetLayout.HitTestOffset(new Point(caret.X, target.Bounds.Y + target.Bounds.Height / 2f)));
        }

        SetSelection(extend ? new RichTextSelection(_state.Selection.Anchor, next) : RichTextSelection.Collapsed(next));
    }

    private void SetSelection(RichTextSelection selection)
    {
        if (_state.Selection == selection) return;
        _state.SetSelection(selection);
        SyncDomTextAndSelection();
        DispatchEvent(StandardEvents.CreateSelectionChange());
        InvalidatePaint();
    }

    private void SyncDomTextAndSelection()
    {
        _domText.Data = PlainText;
        var ownerDocument = OwnerDocument;
        if (ownerDocument == null) return;

        var range = ownerDocument.CreateRange();
        range.SetStart(_domText, SelectionStart);
        range.SetEnd(_domText, SelectionStart + SelectionLength);
        ownerDocument.GetSelection().AddRange(range);
    }

    private string GetSelectedText()
    {
        if (_state.Selection.IsCollapsed) return "";
        var start = SelectionStart;
        return PlainText.Substring(start, SelectionLength);
    }

    private Rect GetCaretRect()
    {
        var font = ResolveFont();
        var lineHeight = GetLineHeight(font);
        var padding = ResolvePadding();
        var layouts = BuildLayouts(
            font,
            lineHeight,
            new Point(Geometry.X + padding, Geometry.Y + padding),
            Math.Max(1, Geometry.Width - padding * 2));
        return GetCaretRect(layouts);
    }

    private Rect GetCaretRect(IReadOnlyList<RichTextBlockLayout> layouts)
    {
        var position = _state.Selection.Focus;
        return layouts[position.BlockIndex].GetCaretRect(position.Offset);
    }

    private void PaintSelectionForBlock(IRenderContext context, int blockIndex, RichTextBlockLayout layout, Color selectionBackground)
    {
        if (_state.Selection.IsCollapsed) return;
        var start = _state.Selection.Start;
        var end = _state.Selection.End;
        if (blockIndex < start.BlockIndex || blockIndex > end.BlockIndex) return;

        var text = Document.Blocks[blockIndex].PlainText;
        var startOffset = blockIndex == start.BlockIndex ? start.Offset : 0;
        var endOffset = blockIndex == end.BlockIndex ? end.Offset : text.Length;
        foreach (var rect in layout.GetSelectionRects(startOffset, endOffset))
            context.FillRect(rect, new SolidColorBrush(selectionBackground));
    }

    private void PaintSelectionForegroundForBlock(IRenderContext context, int blockIndex, RichTextBlockLayout layout, Color selectionForeground)
    {
        if (_state.Selection.IsCollapsed) return;
        var start = _state.Selection.Start;
        var end = _state.Selection.End;
        if (blockIndex < start.BlockIndex || blockIndex > end.BlockIndex) return;

        var text = Document.Blocks[blockIndex].PlainText;
        var startOffset = blockIndex == start.BlockIndex ? start.Offset : 0;
        var endOffset = blockIndex == end.BlockIndex ? end.Offset : text.Length;
        foreach (var rect in layout.GetSelectionRects(startOffset, endOffset))
        {
            context.PushClip(rect);
            PaintBlock(context, layout, selectionForeground, useRunForeground: false);
            context.PopClip();
        }
    }

    private static void PaintBlock(IRenderContext context, RichTextBlockLayout layout, Color defaultColor, bool useRunForeground = true)
    {
        foreach (var line in layout.Lines)
        {
            foreach (var fragment in line.Fragments)
            {
                var color = useRunForeground ? ParseColor(fragment.Run.Marks.Foreground) ?? defaultColor : defaultColor;
                context.DrawText(
                    new TextLayout(fragment.Run.Text, fragment.Font),
                    fragment.Bounds.Position,
                    new SolidColorBrush(color));
                if (fragment.Run.Marks.Underline)
                    context.FillRect(
                        new Rect(fragment.Bounds.X, fragment.Bounds.Bottom - 1, fragment.Bounds.Width, 1),
                        new SolidColorBrush(color));
            }
        }
    }

    private Color GetSelectionColor(string primaryProperty, string? fallbackProperty, Color defaultColor)
    {
        var value = Style.Get(primaryProperty);
        if (string.IsNullOrWhiteSpace(value) && fallbackProperty != null)
            value = Style.Get(fallbackProperty);
        if (string.IsNullOrWhiteSpace(value)) return defaultColor;
        return Color.TryParse(value, out var color) ? color : defaultColor;
    }

    private RichTextPosition HitTestPosition(Point point)
    {
        var font = ResolveFont();
        var lineHeight = GetLineHeight(font);
        var padding = ResolvePadding();
        var layouts = BuildLayouts(
            font,
            lineHeight,
            new Point(Geometry.X + padding, Geometry.Y + padding),
            Math.Max(1, Geometry.Width - padding * 2));
        var blockIndex = layouts.FindIndex(layout => point.Y <= layout.Bounds.Bottom);
        if (blockIndex < 0) blockIndex = layouts.Count - 1;
        return new RichTextPosition(blockIndex, layouts[blockIndex].HitTestOffset(point));
    }

    private List<RichTextBlockLayout> BuildLayouts(Font font, float lineHeight, Point origin, float maxWidth)
    {
        var layouts = new List<RichTextBlockLayout>(Document.Blocks.Count);
        var y = origin.Y;
        foreach (var block in Document.Blocks)
        {
            var layout = RichTextLayoutEngine.LayoutBlock(block, font, new Point(origin.X, y), maxWidth, lineHeight);
            layouts.Add(layout);
            y += layout.Bounds.Height;
        }
        return layouts;
    }

    private int ToLinearOffset(RichTextPosition position)
    {
        var offset = 0;
        for (var i = 0; i < position.BlockIndex; i++)
            offset += Document.Blocks[i].PlainText.Length + 1;
        return offset + position.Offset;
    }

    private RichTextPosition FromLinearOffset(int linearOffset)
    {
        var remaining = linearOffset;
        for (var i = 0; i < Document.Blocks.Count; i++)
        {
            var length = Document.Blocks[i].PlainText.Length;
            if (remaining <= length) return new RichTextPosition(i, remaining);
            remaining -= length + 1;
        }
        var last = Document.Blocks.Count - 1;
        return new RichTextPosition(last, Document.Blocks[last].PlainText.Length);
    }

    private Font ResolveFont()
    {
        return FontManager.Instance.FromCss(
            Style.GetPropertyValue("font-family"),
            Style.GetPropertyValue("font-size"),
            Style.GetPropertyValue("font-weight"),
            Style.GetPropertyValue("font-style"),
            DefaultFontSize);
    }

    private float GetLineHeight(Font font)
    {
        var value = Style.GetPropertyValue("line-height").Trim();
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(value[..^2], System.Globalization.CultureInfo.InvariantCulture, out var pixels) && pixels > 0)
            return pixels;
        if (float.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var multiplier) && multiplier > 0)
            return font.Size * multiplier;
        return TextMetrics.GetLineHeight(font, TextLayout.DefaultLineHeight);
    }

    private float ResolvePadding()
    {
        var value = Style.GetPropertyValue("padding").Trim();
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            value = value[..^2];
        return float.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var padding) && padding >= 0
            ? padding
            : DefaultPadding;
    }

    private static Color? ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 7 || value[0] != '#') return null;
        return byte.TryParse(value.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
               byte.TryParse(value.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
               byte.TryParse(value.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var b)
            ? Color.FromRgb(r, g, b)
            : null;
    }
}
