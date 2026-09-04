using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Square.Controls;
using Square.Controls.Animation;
using Square.Events;
using Square.Graphics;
using Square.Platform;
using Square.Rendering.Paint;
using Square.Text;
using Square.UI;
using Square.UI.Scrolling;

namespace Square.Extensions.CodeEditor;

/// <summary>
/// 多行代码编辑控件：PieceTable 模型、视口绘制、TextMate 高亮、VS Code 风格语言配置。
/// </summary>
public sealed class CodeEditor : UIElement, ITextEditor
{
    private const float DefaultFontSize = 13f;
    private const float DefaultPadding = 8f;
    private const float LineNumberPadding = 8f;
    private const float FoldGutterWidth = 14f;
    private const float GlyphGutterWidth = 16f;
    private const float GutterColorBarWidth = 3f;
    private const float OverviewRulerWidth = 10f;
    private const int OverscanLines = 5;
    private const float ScrollBarLineStep = 40f;
    private const float ScrollBarFadeDelaySeconds = 0.5f;
    private const float ScrollBarFadeDurationSeconds = 0.2f;

    private readonly CodeEditorTextModel _model = new();
    private readonly FoldingEngine _folding = new();
    private readonly CodeEditorViewLayout _viewLayout = new();
    private readonly Dictionary<string, CodeEditorLineDecoration> _decorations = new(StringComparer.Ordinal);
    private readonly Dictionary<int, List<CodeEditorLineDecoration>> _decorationsByLine = [];
    /// <summary>附加光标（不含主光标 _caretIndex/_selectionAnchor）。</summary>
    private readonly List<CodeEditorCursor> _extraCursors = [];
    private TokenizationCache? _tokens;
    private string _tokenizerLanguage = "";
    private int _caretIndex;
    private int _selectionAnchor;
    private bool _dragging;
    private bool _draggingVScroll;
    private bool _draggingHScroll;
    private float _scrollDragAnchor;
    private float _scrollDragOrigin;
    private float _scrollY;
    private float _scrollX;
    private float? _preferredX;
    private string _findQuery = "";
    private string _replaceQuery = "";
    private bool _findMatchCase;
    private int _foldModelVersion = -1;
    private int _contentVersion;
    private float _caretOpacity = 1f;
    private float _caretBlinkTarget;
    private double _nextCaretTransitionSeconds;
    private Animation<float>? _caretBlinkAnimation;
    private readonly System.Diagnostics.Stopwatch _caretClock = System.Diagnostics.Stopwatch.StartNew();
    private float _scrollbarOpacity;
    private float _scrollbarFadeElapsed;
    private long _scrollbarFadeLastTimestamp;
    private bool _scrollbarFadeActive;
    private ScrollBarHit _scrollbarRepeatHit;
    private Point _scrollbarRepeatPoint;
    private bool _scrollbarRepeatPointerInside;
    private ScrollbarPart _scrollbarPressedPart;
    private ScrollbarPart _scrollbarHoverPart;

    private bool IsTransientScrollbar => ScrollbarVisibility == ScrollbarVisibilityMode.Scroll ||
        ScrollbarVisibility == ScrollbarVisibilityMode.Auto && IsMobileScrollbar;

    internal new float ScrollbarOpacity
    {
        get
        {
            if (ScrollbarVisibility == ScrollbarVisibilityMode.Hidden || IsScrollbarCssHidden() ||
                GetScrollbarWidthMode() == ScrollbarWidthMode.None || IsScrollbarPseudoDisplayNone() || !ShowScrollBars)
                return 0;
            if (ScrollbarVisibility == ScrollbarVisibilityMode.Always ||
                ScrollbarVisibility == ScrollbarVisibilityMode.Auto && !IsMobileScrollbar)
                return 1f;
            if (ScrollbarVisibility is ScrollbarVisibilityMode.Hover or ScrollbarVisibilityMode.Scroll &&
                (HasState(ElementState.Hover) || _scrollbarHoverPart != ScrollbarPart.None))
                return 1f;
            return _scrollbarOpacity;
        }
    }

    /// <summary>当前私有编辑器滚动偏移。</summary>
    public Point EditorScrollOffset => new(_scrollX, _scrollY);
    /// <summary>当前水平滚动偏移。</summary>
    public float HorizontalScrollOffset => _scrollX;
    /// <summary>当前垂直滚动偏移。</summary>
    public float VerticalScrollOffset => _scrollY;
    /// <summary>最大水平滚动偏移。</summary>
    public float HorizontalScrollRange => GetScrollMetrics(ResolveFont(), GetLineHeight(ResolveFont())).MaxScrollX;
    /// <summary>最大垂直滚动偏移。</summary>
    public float VerticalScrollRange => GetScrollMetrics(ResolveFont(), GetLineHeight(ResolveFont())).MaxScrollY;

    /// <summary>初始化默认等宽样式。</summary>
    public CodeEditor()
    {
        if (string.IsNullOrEmpty(Style.Get("font-family")))
            Style.Set("font-family", "monospace");
        if (string.IsNullOrEmpty(Style.Get("font-size")))
            Style.Set("font-size", "13px");
        AddEventListener("focus", ResetCaretBlink);
        AddEventListener("blur", OnBlur);
        AddEventListener("wheel", OnWheel);
        _model.Changed += (_, _) =>
        {
            _contentVersion++;
            _viewLayout.Invalidate();
            InvalidateTokensFromCaret();
            ScheduleFoldRecompute();
            InvalidateLayout();
        };
    }


    private bool IsMobileScrollbar =>
        (AppWindow?.ScrollbarProfile ?? ScrollbarDeviceProfile.Auto) == ScrollbarDeviceProfile.Mobile;

    /// <summary>文档模型。</summary>
    public ICodeEditorTextModel Model => _model;

    /// <summary>是否可撤销。</summary>
    public bool CanUndo => _model.CanUndo;

    /// <summary>是否可重做。</summary>
    public bool CanRedo => _model.CanRedo;

    /// <summary>纯文本。</summary>
    public string Value
    {
        get => _model.GetValue();
        set
        {
            _model.SetValue(value ?? "");
            _extraCursors.Clear();
            ClampSelection();
            _tokens?.Reset();
            _contentVersion++;
            _viewLayout.Invalidate();
            ScheduleFoldRecompute();
            InvalidateLayout();
        }
    }

    /// <summary>占位符。</summary>
    public string Placeholder
    {
        get => GetProperty<string>(nameof(Placeholder)) ?? "";
        set => SetProperty(nameof(Placeholder), value ?? "");
    }

    /// <summary>languageId。</summary>
    public string Language
    {
        get => GetProperty<string>(nameof(Language)) ?? "plaintext";
        set
        {
            SetProperty(nameof(Language), string.IsNullOrWhiteSpace(value) ? "plaintext" : value.Trim());
            _tokens = null;
            _viewLayout.Invalidate();
            ScheduleFoldRecompute();
            InvalidatePaint();
        }
    }

    /// <summary>主题 id。</summary>
    public string? ThemeId
    {
        get => GetProperty<string?>(nameof(ThemeId));
        set
        {
            SetProperty(nameof(ThemeId), value);
            InvalidatePaint();
        }
    }

    /// <summary>Tab 宽度。</summary>
    public int TabSize
    {
        get => Math.Clamp(GetProperty<int?>(nameof(TabSize)) ?? 4, 1, 16);
        set
        {
            var next = Math.Clamp(value, 1, 16);
            if (TabSize == next) return;
            SetProperty(nameof(TabSize), next);
            _viewLayout.Invalidate();
            InvalidatePaint();
        }
    }

    /// <summary>Tab 插入空格。</summary>
    public bool InsertSpaces
    {
        get => GetBooleanProperty(nameof(InsertSpaces), true);
        set => SetProperty(nameof(InsertSpaces), value);
    }

    /// <summary>是否显示行号 gutter；关闭后编辑区左缘无行号列。</summary>
    public bool ShowLineNumbers
    {
        get => GetBooleanProperty(nameof(ShowLineNumbers), true);
        set
        {
            if (ShowLineNumbers == value) return;
            SetProperty(nameof(ShowLineNumbers), value);
            InvalidatePaint();
        }
    }

    /// <summary>切换行号显示。</summary>
    public void ToggleLineNumbers() => ShowLineNumbers = !ShowLineNumbers;

    /// <summary>当前行号列宽度（关闭行号时为 0）。</summary>
    public float LineNumberGutterWidth
    {
        get
        {
            if (!ShowLineNumbers) return 0;
            return MeasureLineNumberGutterWidth(ResolveFont());
        }
    }

    /// <summary>是否显示 glyph margin（断点/书签/自定义图标列）。</summary>
    public bool ShowGlyphMargin
    {
        get => GetBooleanProperty(nameof(ShowGlyphMargin), true);
        set
        {
            if (ShowGlyphMargin == value) return;
            SetProperty(nameof(ShowGlyphMargin), value);
            InvalidatePaint();
        }
    }

    /// <summary>切换 glyph margin。</summary>
    public void ToggleGlyphMargin() => ShowGlyphMargin = !ShowGlyphMargin;

    /// <summary>glyph margin 宽度（关闭时为 0）。</summary>
    public float GlyphMarginWidth => ShowGlyphMargin ? GlyphGutterWidth : 0;

    /// <summary>当前行装饰数量。</summary>
    public int DecorationCount => _decorations.Count;

    /// <summary>gutter 点击（glyph / 行号 / 折叠列）。</summary>
    public event EventHandler<CodeEditorGutterClickEventArgs>? GutterClick;

    /// <summary>选区或光标位置变化（含折叠后仍保持的文档选区）。</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>设置或替换行装饰（同 Id 覆盖）。</summary>
    public void SetDecoration(CodeEditorLineDecoration decoration)
    {
        ArgumentNullException.ThrowIfNull(decoration);
        if (string.IsNullOrWhiteSpace(decoration.Id))
            throw new ArgumentException("Decoration Id is required.", nameof(decoration));
        _decorations[decoration.Id] = decoration;
        RebuildDecorationIndex();
        InvalidatePaint();
    }

    /// <summary>批量设置装饰（同 Id 覆盖，不清除其它 Id）。</summary>
    public void SetDecorations(IEnumerable<CodeEditorLineDecoration> decorations)
    {
        ArgumentNullException.ThrowIfNull(decorations);
        foreach (var decoration in decorations)
        {
            if (decoration == null || string.IsNullOrWhiteSpace(decoration.Id)) continue;
            _decorations[decoration.Id] = decoration;
        }
        RebuildDecorationIndex();
        InvalidatePaint();
    }

    /// <summary>移除指定 Id 的装饰。</summary>
    public bool RemoveDecoration(string id)
    {
        if (string.IsNullOrEmpty(id) || !_decorations.Remove(id)) return false;
        RebuildDecorationIndex();
        InvalidatePaint();
        return true;
    }

    /// <summary>清空全部行装饰。</summary>
    public void ClearDecorations()
    {
        if (_decorations.Count == 0) return;
        _decorations.Clear();
        _decorationsByLine.Clear();
        InvalidatePaint();
    }

    /// <summary>读取当前全部装饰（快照）。</summary>
    public IReadOnlyList<CodeEditorLineDecoration> GetDecorations()
        => _decorations.Values.OrderBy(d => d.Line).ThenBy(d => d.Id, StringComparer.Ordinal).ToArray();

    /// <summary>读取指定行的装饰。</summary>
    public IReadOnlyList<CodeEditorLineDecoration> GetDecorationsAt(int line)
        => _decorationsByLine.TryGetValue(line, out var list) ? list : Array.Empty<CodeEditorLineDecoration>();

    /// <summary>是否显示折叠槽（括号/标签层级折叠）。</summary>
    public bool ShowFolding
    {
        get => GetBooleanProperty(nameof(ShowFolding), true);
        set
        {
            if (ShowFolding == value) return;
            SetProperty(nameof(ShowFolding), value);
            if (!value) _folding.ExpandAll();
            else ScheduleFoldRecompute();
            InvalidatePaint();
        }
    }

    /// <summary>切换折叠槽显示。</summary>
    public void ToggleFolding() => ShowFolding = !ShowFolding;

    /// <summary>折叠槽宽度（关闭时为 0）。</summary>
    public float FoldingGutterWidth => ShowFolding ? FoldGutterWidth : 0;

    /// <summary>当前可折叠区间数量。</summary>
    public int FoldRegionCount
    {
        get
        {
            EnsureFolds();
            return _folding.Regions.Count;
        }
    }

    /// <summary>折叠指定起始行（若可折叠）。</summary>
    public bool CollapseFoldAt(int startLine)
    {
        EnsureFolds();
        if (!_folding.CanFoldAt(startLine) || _folding.IsCollapsed(startLine)) return false;
        var selStart = SelectionStart;
        var selLen = SelectionLength;
        var caret = _caretIndex;
        var anchor = _selectionAnchor;
        _folding.ToggleAt(startLine);
        _viewLayout.Invalidate();
        EnsureCaretNotInHidden();
        // 折叠不得改写文档选区
        if (selLen > 0)
        {
            _selectionAnchor = anchor;
            _caretIndex = caret;
        }
        _ = selStart;
        InvalidatePaint();
        NotifySelectionChanged();
        return true;
    }

    /// <summary>展开指定起始行折叠。</summary>
    public bool ExpandFoldAt(int startLine)
    {
        EnsureFolds();
        if (!_folding.IsCollapsed(startLine)) return false;
        var caret = _caretIndex;
        var anchor = _selectionAnchor;
        _folding.ToggleAt(startLine);
        _viewLayout.Invalidate();
        _selectionAnchor = anchor;
        _caretIndex = caret;
        InvalidatePaint();
        NotifySelectionChanged();
        return true;
    }

    /// <summary>切换指定起始行折叠状态。</summary>
    public bool ToggleFoldAt(int startLine)
    {
        EnsureFolds();
        var caret = _caretIndex;
        var anchor = _selectionAnchor;
        var hadSelection = SelectionLength > 0;
        if (!_folding.ToggleAt(startLine)) return false;
        _viewLayout.Invalidate();
        EnsureCaretNotInHidden();
        if (hadSelection)
        {
            _selectionAnchor = anchor;
            _caretIndex = caret;
        }
        InvalidatePaint();
        NotifySelectionChanged();
        return true;
    }

    /// <summary>折叠全部可折叠区间。</summary>
    public void CollapseAllFolds()
    {
        EnsureFolds();
        var caret = _caretIndex;
        var anchor = _selectionAnchor;
        var hadSelection = SelectionLength > 0;
        _folding.CollapseAll();
        _viewLayout.Invalidate();
        EnsureCaretNotInHidden();
        if (hadSelection)
        {
            _selectionAnchor = anchor;
            _caretIndex = caret;
        }
        InvalidatePaint();
        NotifySelectionChanged();
    }

    /// <summary>展开全部折叠。</summary>
    public void ExpandAllFolds()
    {
        var caret = _caretIndex;
        var anchor = _selectionAnchor;
        _folding.ExpandAll();
        _viewLayout.Invalidate();
        _selectionAnchor = anchor;
        _caretIndex = caret;
        InvalidatePaint();
        NotifySelectionChanged();
    }

    private void NotifySelectionChanged() => SelectionChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>是否按可视宽度软换行（不改动文档换行符）。</summary>
    public bool WordWrap
    {
        get => GetBooleanProperty(nameof(WordWrap), false);
        set
        {
            if (WordWrap == value) return;
            var beforeScrollX = _scrollX;
            SetProperty(nameof(WordWrap), value);
            if (value) _scrollX = 0;
            if (Math.Abs(beforeScrollX - _scrollX) > 0.01f)
                DispatchEvent(StandardEvents.CreateScroll());
            InvalidateLayout();
        }
    }

    /// <summary>切换软换行。</summary>
    public void ToggleWordWrap() => WordWrap = !WordWrap;

    /// <summary>是否显示滚动条（内容溢出时绘制；可关闭）。</summary>
    public bool ShowScrollBars
    {
        get => GetBooleanProperty(nameof(ShowScrollBars), true);
        set
        {
            if (ShowScrollBars == value) return;
            SetProperty(nameof(ShowScrollBars), value);
            InvalidatePaint();
        }
    }

    /// <summary>切换滚动条显示。</summary>
    public void ToggleScrollBars() => ShowScrollBars = !ShowScrollBars;

    /// <summary>是否显示右侧 overview ruler（装饰/查找标记迷你条）。</summary>
    public bool ShowOverviewRuler
    {
        get => GetBooleanProperty(nameof(ShowOverviewRuler), true);
        set
        {
            if (ShowOverviewRuler == value) return;
            SetProperty(nameof(ShowOverviewRuler), value);
            InvalidatePaint();
        }
    }

    /// <summary>切换 overview ruler。</summary>
    public void ToggleOverviewRuler() => ShowOverviewRuler = !ShowOverviewRuler;

    /// <summary>overview ruler 宽度（关闭时为 0）。</summary>
    public float OverviewRulerGutterWidth => ShowOverviewRuler ? OverviewRulerWidth : 0;

    /// <summary>查找面板是否视为打开（宿主可绑定 UI；编辑器据此暴露 FindQuery 等）。</summary>
    public bool FindPanelVisible
    {
        get => GetBooleanProperty(nameof(FindPanelVisible), false);
        set
        {
            if (FindPanelVisible == value) return;
            SetProperty(nameof(FindPanelVisible), value);
            InvalidatePaint();
        }
    }

    /// <summary>切换查找面板可见性。</summary>
    public void ToggleFindPanel() => FindPanelVisible = !FindPanelVisible;

    /// <summary>是否高亮匹配括号。</summary>
    public bool HighlightMatchingBrackets
    {
        get => GetBooleanProperty(nameof(HighlightMatchingBrackets), true);
        set
        {
            if (HighlightMatchingBrackets == value) return;
            SetProperty(nameof(HighlightMatchingBrackets), value);
            InvalidatePaint();
        }
    }

    /// <summary>是否高亮所有查找匹配（需有 FindQuery）。</summary>
    public bool HighlightFindMatches
    {
        get => GetBooleanProperty(nameof(HighlightFindMatches), true);
        set
        {
            if (HighlightFindMatches == value) return;
            SetProperty(nameof(HighlightFindMatches), value);
            InvalidatePaint();
        }
    }

    /// <summary>只读：禁止输入、删除、替换、撤销/重做；仍可选择、复制、滚动与折叠。</summary>
    public bool ReadOnly
    {
        get => GetBooleanProperty(nameof(ReadOnly), false);
        set
        {
            if (ReadOnly == value) return;
            SetProperty(nameof(ReadOnly), value);
            InvalidatePaint();
        }
    }

    /// <summary>切换只读。</summary>
    public void ToggleReadOnly() => ReadOnly = !ReadOnly;

    private bool GetBooleanProperty(string name, bool defaultValue) =>
        Properties.HasValue(name) ? GetProperty<bool>(name) : defaultValue;

    /// <inheritdoc/>
    public int CaretIndex => _caretIndex;
    /// <inheritdoc/>
    public int SelectionStart => Math.Min(_caretIndex, _selectionAnchor);
    /// <inheritdoc/>
    public int SelectionLength => Math.Abs(_caretIndex - _selectionAnchor);
    /// <inheritdoc/>
    /// <remarks>
    /// 若选区与折叠块相交，返回值包含隐藏行（与剪切/删除范围一致）。
    /// </remarks>
    public string SelectedText
    {
        get
        {
            if (SelectionLength == 0) return "";
            var (start, end) = GetEffectiveSelectionRange();
            return _model.GetValue().Substring(start, end - start);
        }
    }
    /// <inheritdoc/>
    public bool CanCopySelection => true;
    /// <inheritdoc/>
    public bool CanCutSelection => !ReadOnly && IsEnabled;
    /// <inheritdoc/>
    public Rect CaretRect => ComputeCaretRect();

    /// <summary>是否存在附加多光标。</summary>
    public bool HasMultiCursors => _extraCursors.Count > 0;

    /// <summary>光标总数（主光标 + 附加）。</summary>
    public int CursorCount => 1 + _extraCursors.Count;

    /// <summary>全部光标快照（主光标在前，按文档 offset 排序去重后的视图）。</summary>
    public IReadOnlyList<CodeEditorCursor> Cursors
    {
        get
        {
            var list = new List<CodeEditorCursor>(1 + _extraCursors.Count)
            {
                new(_caretIndex, _selectionAnchor)
            };
            list.AddRange(_extraCursors);
            return list;
        }
    }

    /// <summary>在指定 offset 添加附加光标（与已有重合则忽略）。</summary>
    public bool AddCursor(int offset)
    {
        offset = Math.Clamp(offset, 0, _model.Length);
        if (offset == _caretIndex && _selectionAnchor == _caretIndex) return false;
        foreach (var c in _extraCursors)
        {
            if (c.IsCollapsed && c.Caret == offset) return false;
        }
        _extraCursors.Add(CodeEditorCursor.Collapsed(offset));
        DeduplicateCursors();
        InvalidatePaint();
        NotifySelectionChanged();
        return true;
    }

    /// <summary>清除所有附加光标，仅保留主光标。</summary>
    public void ClearExtraCursors()
    {
        if (_extraCursors.Count == 0) return;
        _extraCursors.Clear();
        InvalidatePaint();
        NotifySelectionChanged();
    }

    /// <summary>用一组光标替换当前多光标状态；第一项成为主光标。</summary>
    public void SetCursors(IEnumerable<CodeEditorCursor> cursors)
    {
        ArgumentNullException.ThrowIfNull(cursors);
        var list = cursors.Select(c => c.Clamp(_model.Length)).ToList();
        if (list.Count == 0)
        {
            ClearExtraCursors();
            return;
        }
        // 主光标 = 最后一个（通常是最新添加的）
        var primary = list[^1];
        _caretIndex = primary.Caret;
        _selectionAnchor = primary.Anchor;
        _extraCursors.Clear();
        for (var i = 0; i < list.Count - 1; i++)
            _extraCursors.Add(list[i]);
        DeduplicateCursors();
        EnsureCaretVisible();
        ResetCaretBlink(fullRepaint: true);
        NotifySelectionChanged();
    }

    /// <inheritdoc/>
    public override Size Measure(Size availableSize) => new(
        ConstrainWidth(float.IsFinite(availableSize.Width) ? availableSize.Width : 480),
        ConstrainHeight(float.IsFinite(availableSize.Height) ? availableSize.Height : 280));

    /// <inheritdoc/>
    public override void Paint(IRenderContext context)
    {
        EnsureFolds();
        var theme = CodeEditorThemeRegistry.Get(ThemeId);
        var font = ResolveFont();
        var lineHeight = GetLineHeight(font);
        var padding = DefaultPadding;
        var glyphGutter = GlyphMarginWidth;
        var lineGutter = ShowLineNumbers ? MeasureLineNumberGutterWidth(font) : 0f;
        var foldGutter = FoldingGutterWidth;
        var leftGutter = glyphGutter + lineGutter + foldGutter;
        var rightGutter = OverviewRulerGutterWidth;
        var contentWidth = Math.Max(1, Geometry.Width - padding * 2 - leftGutter - rightGutter);
        EnsureViewLayout(font, contentWidth);

        context.FillRect(Geometry, new SolidColorBrush(theme.EditorBackground));
        var border = IsFocused ? Color.FromRgb(0, 95, 184) : Color.FromRgb(165, 170, 176);
        context.DrawRect(Geometry, Pen.FromColor(border, IsFocused ? 2 : 1));

        context.PushClip(new Rect(Geometry.X + 1, Geometry.Y + 1, Math.Max(0, Geometry.Width - 2), Math.Max(0, Geometry.Height - 2)));

        var contentHeight = Math.Max(0, Geometry.Height - padding * 2);
        EnsureScroll(lineHeight, contentHeight, font, contentWidth);
        var scrollMetrics = GetScrollMetrics(font, lineHeight);
        var leadingLeft = scrollMetrics.ViewportRect.Left - (Geometry.X + padding + leftGutter);
        var leadingTop = scrollMetrics.ViewportRect.Top - (Geometry.Y + padding);
        var contentTop = scrollMetrics.ViewportRect.Top;
        var contentLeft = scrollMetrics.ViewportRect.Left - (WordWrap ? 0f : _scrollX);
        contentWidth = scrollMetrics.ViewportRect.Width;
        contentHeight = scrollMetrics.ViewportRect.Height;
        var contentRight = scrollMetrics.ViewportRect.Right - 1;
        var contentBottom = scrollMetrics.ViewportRect.Bottom - 1;

        // 文本区单独裁剪，避免横向滚动时画进 gutter / overview
        var textClipLeft = Geometry.X + leftGutter + 1 + leadingLeft;
        var textClipTop = Geometry.Y + 1 + leadingTop;
        context.PushClip(new Rect(
            textClipLeft,
            textClipTop,
            Math.Max(0, contentRight - textClipLeft),
            Math.Max(0, contentBottom - textClipTop)));

        var firstRow = Math.Max(0, (int)MathF.Floor(_scrollY / lineHeight) - OverscanLines);
        var visibleCount = (int)MathF.Ceiling(contentHeight / lineHeight) + OverscanLines * 2 + 1;
        var lastRow = Math.Min(_viewLayout.RowCount - 1, firstRow + visibleCount);
        var caretLine = _model.GetLineNumberAt(_caretIndex);

        var tokens = EnsureTokens();

        for (var rowIndex = firstRow; rowIndex <= lastRow; rowIndex++)
        {
            var row = _viewLayout[rowIndex];
            var line = row.DocumentLine;
            var y = contentTop + rowIndex * lineHeight - _scrollY;
            var isActiveLine = line == caretLine && IsFocused;
            if (isActiveLine)
            {
                context.FillRect(
                    new Rect(textClipLeft, y,
                        Math.Max(0, contentRight - textClipLeft), lineHeight),
                    new SolidColorBrush(theme.EditorCurrentLineBackground));
            }

            if (row.IsFirstOfDocumentLine)
                PaintDecorationLineBackground(context, line, textClipLeft, y,
                    Math.Max(0, contentRight - textClipLeft), lineHeight);

            if (HighlightFindMatches && !string.IsNullOrEmpty(_findQuery))
                PaintFindMatchesForRow(context, theme, font, row, contentLeft, y, lineHeight);

            PaintSelectionForRow(context, theme, font, row, contentLeft, y, lineHeight);
            PaintExtraCursorSelectionsForRow(context, theme, font, row, contentLeft, y, lineHeight);

            if (HighlightMatchingBrackets && SelectionLength == 0 && !HasMultiCursors && IsFocused)
                PaintBracketMatchForRow(context, theme, font, row, contentLeft, y, lineHeight);

            PaintRowText(context, theme, font, tokens, row, contentLeft, y);

            if (ShowFolding && IsFoldPlaceholderRow(row))
                PaintFoldPlaceholder(context, theme, font, line, contentLeft, y, lineHeight);
        }

        context.PopClip();

        // gutter 覆盖在文本之上（glyph/行号/折叠不横向滚动）
        if (leftGutter > 0)
        {
            context.FillRect(
                new Rect(Geometry.X + 1, Geometry.Y + 1, leftGutter, Math.Max(0, Geometry.Height - 2)),
                new SolidColorBrush(theme.EditorGutterBackground));
            var sep = theme.EditorLineNumberForeground;
            context.FillRect(
                new Rect(Geometry.X + leftGutter, Geometry.Y + 1, 1, Math.Max(0, Geometry.Height - 2)),
                new SolidColorBrush(Color.FromRgba(sep.R, sep.G, sep.B, 60)));
        }

        var lineNumberLeft = Geometry.X + glyphGutter;
        var foldLeft = lineNumberLeft + lineGutter;

        for (var rowIndex = firstRow; rowIndex <= lastRow; rowIndex++)
        {
            var row = _viewLayout[rowIndex];
            var line = row.DocumentLine;
            var y = contentTop + rowIndex * lineHeight - _scrollY;
            var isActiveLine = line == caretLine && IsFocused;

            if (row.IsFirstOfDocumentLine)
                PaintGlyphMarginForLine(context, theme, font, line, Geometry.X, y, glyphGutter, lineHeight);

            if (ShowLineNumbers && lineGutter > 0 && row.IsFirstOfDocumentLine)
            {
                var num = (line + 1).ToString();
                var numLayout = new TextLayout(num, font);
                var numWidth = MeasureTextWidth(num, font);
                var numberColor = isActiveLine
                    ? (theme.EditorLineNumberActiveForeground.A > 0
                        ? theme.EditorLineNumberActiveForeground
                        : theme.EditorForeground)
                    : theme.EditorLineNumberForeground;
                context.DrawText(
                    numLayout,
                    new Point(lineNumberLeft + lineGutter - LineNumberPadding - numWidth, y),
                    new SolidColorBrush(numberColor));
            }

            if (ShowFolding && foldGutter > 0 && row.IsFirstOfDocumentLine && _folding.CanFoldAt(line))
            {
                var collapsed = _folding.IsCollapsed(line);
                var glyph = collapsed ? "▸" : "▾";
                var glyphLayout = new TextLayout(glyph, font);
                var glyphWidth = MeasureTextWidth(glyph, font);
                var gx = foldLeft + (foldGutter - glyphWidth) / 2f;
                context.DrawText(
                    glyphLayout,
                    new Point(gx, y),
                    new SolidColorBrush(theme.EditorLineNumberForeground));
            }
        }

        if (_model.Length == 0 && !string.IsNullOrEmpty(Placeholder) && !IsFocused)
        {
            var ph = new TextLayout(Placeholder, font);
            context.DrawText(ph, new Point(contentLeft, contentTop), new SolidColorBrush(Color.FromRgb(125, 130, 136)));
        }

        PaintCarets(context, theme, font, lineHeight, padding, leftGutter);

        if (ShowScrollBars)
            PaintScrollBars(context, theme, font, lineHeight, padding, leftGutter, contentWidth, contentHeight);


        if (ShowOverviewRuler)
            PaintOverviewRuler(context, theme, font, lineHeight, rightGutter);

        context.PopClip();
    }

    private void InvalidateCaretPaint()
    {
        var caret = ComputeCaretRect();
        if (caret.IsEmpty || Geometry.IsEmpty)
        {
            InvalidatePaint();
            return;
        }
        // local to Geometry
        var local = new Rect(
            caret.X - Geometry.X - 1,
            caret.Y - Geometry.Y - 1,
            Math.Max(2, caret.Width + 2),
            Math.Max(2, caret.Height + 2));
        InvalidatePaint(local);
    }

    private void PaintOverviewRuler(
        IRenderContext context,
        CodeEditorTheme theme,
        Font font,
        float lineHeight,
        float rightGutter)
    {
        if (rightGutter <= 0) return;
        var track = new Rect(
            Geometry.Right - rightGutter - 1,
            Geometry.Y + 1,
            rightGutter,
            Math.Max(0, Geometry.Height - 2));
        if (track.IsEmpty) return;

        var bg = theme.OverviewRulerBackground.A > 0
            ? theme.OverviewRulerBackground
            : Color.FromRgba(theme.EditorGutterBackground.R, theme.EditorGutterBackground.G, theme.EditorGutterBackground.B, 200);
        context.FillRect(track, new SolidColorBrush(bg));

        var lineCount = Math.Max(1, _model.LineCount);
        var markHeight = Math.Max(2f, track.Height / lineCount);

        void PaintLineMark(int line, Color color)
        {
            if (line < 0 || line >= lineCount) return;
            var y = track.Y + track.Height * (line / (float)lineCount);
            var h = Math.Min(markHeight + 1, track.Bottom - y);
            if (h <= 0) return;
            context.FillRect(new Rect(track.X + 2, y, Math.Max(2, track.Width - 4), h), new SolidColorBrush(color));
        }

        foreach (var decoration in _decorations.Values)
        {
            var color = decoration.OverviewRulerColor
                        ?? decoration.GutterColor
                        ?? decoration.GlyphColor;
            if (color is not { } c || c.A == 0) continue;
            PaintLineMark(decoration.Line, c);
        }

        if (HighlightFindMatches && !string.IsNullOrEmpty(_findQuery))
        {
            var findColor = theme.FindMatchBackground.A > 0
                ? theme.FindMatchBackground
                : Color.FromRgba(255, 213, 0, 120);
            foreach (var line in GetFindMatchLines())
                PaintLineMark(line, findColor);
        }

        // viewport indicator
        EnsureViewLayout(font, Math.Max(1, Geometry.Width - DefaultPadding * 2 - GetLeftGutterWidth() - rightGutter));
        var totalRows = Math.Max(1, _viewLayout.RowCount);
        var visibleRows = Math.Max(1, (int)MathF.Ceiling(Math.Max(1, Geometry.Height - DefaultPadding * 2) / Math.Max(1, lineHeight)));
        var first = Math.Clamp((int)MathF.Floor(_scrollY / Math.Max(1, lineHeight)), 0, totalRows - 1);
        var top = track.Y + track.Height * (first / (float)totalRows);
        var height = Math.Max(4, track.Height * (Math.Min(visibleRows, totalRows) / (float)totalRows));
        var indicator = theme.OverviewRulerBorder.A > 0
            ? theme.OverviewRulerBorder
            : Color.FromRgba(160, 160, 160, 90);
        context.FillRect(
            new Rect(track.X, top, track.Width, Math.Min(height, track.Bottom - top)),
            new SolidColorBrush(indicator));
    }

    /// <inheritdoc/>
    public void HandleTextInput(string text)
    {
        if (!CanEdit() || string.IsNullOrEmpty(text)) return;
        text = Normalize(text);
        if (text.Length == 0) return;

        if (HasMultiCursors)
        {
            ApplyEditToAllCursors(text, isBackspace: false, isDelete: false);
            return;
        }

        // auto-closing pairs
        if (text.Length == 1 && SelectionLength == 0)
        {
            var config = LanguageRegistry.ResolveConfiguration(Language);
            var ch = text[0];
            if (config.AutoClosingPairs != null)
            {
                foreach (var (open, close) in config.AutoClosingPairs)
                {
                    if (open.Length == 1 && open[0] == ch)
                    {
                        ReplaceSelection(open + close);
                        _caretIndex = SelectionStart - close.Length;
                        _selectionAnchor = _caretIndex;
                        AfterEdit();
                        return;
                    }
                }
            }
        }

        ReplaceSelection(text);
        AfterEdit();
    }

    /// <inheritdoc/>
    public void HandleKey(int keyCode, bool shift = false, bool control = false)
    {
        if (!IsEnabled) return;
        // Escape：清除附加多光标
        if (keyCode == 27 && HasMultiCursors)
        {
            ClearExtraCursors();
            return;
        }

        switch (keyCode)
        {
            case 33:
                ScrollEditorPage(-1);
                return;
            case 34:
                ScrollEditorPage(1);
                return;
            case 8 when CanEdit():
                if (HasMultiCursors) ApplyEditToAllCursors("", isBackspace: true, isDelete: false);
                else Backspace();
                return;
            case 9 when CanEdit():
                if (HasMultiCursors) ApplyEditToAllCursors(InsertSpaces ? new string(' ', TabSize) : "\t", false, false);
                else HandleTab(shift);
                return;
            case 13 when CanEdit():
                if (HasMultiCursors) ApplyEditToAllCursors("\n", false, false);
                else HandleEnter();
                return;
            case 35:
                if (HasMultiCursors) MoveAllCursorsToLineBoundary(toStart: false, control, shift);
                else MoveToLineBoundary(toStart: false, shift, control);
                return;
            case 36:
                if (HasMultiCursors) MoveAllCursorsToLineBoundary(toStart: true, control, shift);
                else MoveToLineBoundary(toStart: true, shift, control);
                return;
            case 37:
                if (HasMultiCursors) MoveAllCursorsHorizontal(-1, control, shift);
                else MoveHorizontal(-1, shift, control);
                return;
            case 38:
                if (HasMultiCursors) MoveAllCursorsVertical(-1, shift);
                else MoveVertical(-1, shift);
                return;
            case 39:
                if (HasMultiCursors) MoveAllCursorsHorizontal(1, control, shift);
                else MoveHorizontal(1, shift, control);
                return;
            case 40:
                if (HasMultiCursors) MoveAllCursorsVertical(1, shift);
                else MoveVertical(1, shift);
                return;
            case 46 when CanEdit():
                if (HasMultiCursors) ApplyEditToAllCursors("", isBackspace: false, isDelete: true);
                else DeleteForward();
                return;
            case 65 when control:
                ClearExtraCursors();
                SelectAll();
                return;
            case 70 when control && shift:
                FindPrevious(_findQuery.Length > 0 ? _findQuery : SelectedText);
                return;
            case 70 when control:
                FindNext(_findQuery.Length > 0 ? _findQuery : SelectedText);
                return;
            case 72 when control && CanEdit(): // Ctrl+H replace next
                ReplaceNext(_findQuery.Length > 0 ? _findQuery : SelectedText, _replaceQuery);
                return;
            case 90 when control && !shift:
                if (CanEdit()) Undo();
                return;
            case 89 when control:
            case 90 when control && shift:
                if (CanEdit()) Redo();
                return;
            case 191 when control && CanEdit(): // Ctrl+/
                ToggleLineComment();
                return;
            case 219 when control && shift: // Ctrl+Shift+[
                CollapseAllFolds();
                return;
            case 221 when control && shift: // Ctrl+Shift+]
                ExpandAllFolds();
                return;
            case 219 when control: // Ctrl+[
                ToggleFoldAt(_model.GetLineNumberAt(_caretIndex));
                return;
        }
    }

    /// <inheritdoc/>
    public bool HandlePointerDown(Point point, bool extendSelection = false, bool addCursor = false)
    {
        if (!IsEnabled) return false;
        if (TryHandleScrollBarPointerDown(point))
        {
            _dragging = false;
            return _draggingVScroll || _draggingHScroll;
        }
        if (TryHandleGutterClick(point, extendSelection))
        {
            _dragging = false;
            return false;
        }
        var index = HitTestOffset(point);
        if (addCursor && !extendSelection)
        {
            // Alt+点击：添加/切换附加光标，不开始拖选
            if (index == _caretIndex && _selectionAnchor == _caretIndex)
            {
                // 点在主光标上：忽略
            }
            else if (_extraCursors.RemoveAll(c => c.IsCollapsed && c.Caret == index) > 0)
            {
                // 点在已有附加光标上：移除
            }
            else
            {
                // 若当前主光标有选区，先把主光标折叠为点再添加
                if (SelectionLength > 0)
                    _selectionAnchor = _caretIndex;
                _extraCursors.Add(CodeEditorCursor.Collapsed(index));
                DeduplicateCursors();
            }
            _dragging = false;
            ResetCaretBlink(fullRepaint: true);
            NotifySelectionChanged();
            return false;
        }

        // 普通点击：清除附加光标，进入单光标拖选
        if (!extendSelection)
            _extraCursors.Clear();
        if (!extendSelection) _selectionAnchor = index;
        _caretIndex = index;
        _dragging = true;
        _preferredX = null;
        EnsureCaretVisible();
        // 选区起止变化必须全量刷新，不能只脏 caret（否则旧选区残留）
        ResetCaretBlink(fullRepaint: true);
        NotifySelectionChanged();
        return true;
    }

    /// <inheritdoc/>
    public bool IsTextSelectionDragActive => _dragging;

    /// <inheritdoc/>
    public bool IsScrollbarInteractionAt(Point point) =>
        HitTestScrollBar(point) != ScrollBarHit.None;

    /// <inheritdoc/>
    public bool OwnsScrollbarChrome => true;

    /// <inheritdoc/>
    public ScrollbarPart GetScrollbarPartAt(Point point) => ToScrollbarPart(HitTestScrollBar(point));

    /// <inheritdoc/>
    public new bool UpdateScrollbarHover(Point point)
    {
        var next = ToScrollbarPart(HitTestScrollBar(point));
        if (next == _scrollbarHoverPart) return false;
        _scrollbarHoverPart = next;
        InvalidatePaint();
        return true;
    }

    /// <inheritdoc/>
    public new void ClearScrollbarHover()
    {
        if (_scrollbarHoverPart == ScrollbarPart.None) return;
        _scrollbarHoverPart = ScrollbarPart.None;
        InvalidatePaint();
    }

    public bool IsScrollbarInteractionUsable(ScrollbarPart part) =>
        IsEffectivelyVisible && IsCssDisplayedForScrollbar() && !IsScrollbarCssHidden() &&
        !IsMobileScrollbar && ShowScrollBars && !IsScrollbarPseudoDisplayNone() &&
        GetScrollbarWidthMode() != ScrollbarWidthMode.None &&
        ScrollbarVisibility != ScrollbarVisibilityMode.Hidden && part != ScrollbarPart.None;

    /// <inheritdoc/>
    public new bool RepeatScrollbarInteraction()
    {
        if (_scrollbarRepeatHit == ScrollBarHit.None || !_scrollbarRepeatPointerInside)
            return false;
        if (!IsScrollbarInteractionUsable(ToScrollbarPart(_scrollbarRepeatHit)))
        {
            ClearScrollbarCapture();
            return false;
        }
        var beforeX = _scrollX;
        var beforeY = _scrollY;
        ApplyScrollbarPart(_scrollbarRepeatHit, _scrollbarRepeatPoint);
        return Math.Abs(beforeX - _scrollX) > 0.01f || Math.Abs(beforeY - _scrollY) > 0.01f;
    }

    /// <inheritdoc/>
    public new bool UpdateScrollbarInteractionPointer(Point point)
    {
        if (_scrollbarRepeatHit == ScrollBarHit.None) return false;
        var hitPart = ToScrollbarPart(HitTestScrollBar(point));
        _scrollbarHoverPart = hitPart;
        var inside = hitPart == ToScrollbarPart(_scrollbarRepeatHit);
        if (inside == _scrollbarRepeatPointerInside) return false;
        _scrollbarRepeatPointerInside = inside;
        InvalidatePaint();
        return true;
    }

    /// <inheritdoc/>
    public new void EndScrollbarInteraction() => ClearScrollbarCapture();

    /// <inheritdoc/>
    public void HandlePointerMove(Point point)
    {
        if (_draggingVScroll || _draggingHScroll)
        {
            if (!IsEffectivelyVisible || !IsCssDisplayedForScrollbar() || IsScrollbarCssHidden() ||
                IsMobileScrollbar || !ShowScrollBars || IsScrollbarPseudoDisplayNone() ||
                GetScrollbarWidthMode() == ScrollbarWidthMode.None ||
                ScrollbarVisibility == ScrollbarVisibilityMode.Hidden)
            {
                ClearScrollbarCapture();
                return;
            }
            HandleScrollBarDrag(point);
            return;
        }
        UpdateScrollbarHover(point);
        if (!_dragging) return;
        var scrolled = AutoScrollDuringDrag(point);
        var next = HitTestOffset(point);
        if (!scrolled && next == _caretIndex) return;
        _caretIndex = next;
        EnsureCaretVisible();
        // 选区拖动：全量刷新，避免局部 caret 脏区留下旧选区
        ResetCaretBlink(fullRepaint: true);
        NotifySelectionChanged();
    }

    /// <inheritdoc/>
    public void HandlePointerUp(Point point)
    {
        _scrollbarRepeatHit = ScrollBarHit.None;
        _scrollbarRepeatPointerInside = false;
        _scrollbarPressedPart = ScrollbarPart.None;
        if (_draggingVScroll || _draggingHScroll)
        {
            _draggingVScroll = false;
            _draggingHScroll = false;
            InvalidatePaint();
            return;
        }
        if (!_dragging) return;
        _caretIndex = HitTestOffset(point);
        _dragging = false;
        ResetCaretBlink(fullRepaint: true);
        NotifySelectionChanged();
    }

    /// <inheritdoc/>
    public void SelectWordAt(Point point)
    {
        // 双击折叠 ⋯ 或折叠头行：选中整块折叠内容
        if (TrySelectCollapsedFoldAtPoint(point))
        {
            _dragging = false;
            return;
        }
        var index = HitTestOffset(point);
        var (start, end) = WordAt(index);
        _selectionAnchor = start;
        _caretIndex = end;
        _dragging = false;
        ResetCaretBlink(fullRepaint: true);
        NotifySelectionChanged();
    }

    /// <inheritdoc/>
    public void SelectAll()
    {
        _extraCursors.Clear();
        _selectionAnchor = 0;
        _caretIndex = _model.Length;
        ResetCaretBlink(fullRepaint: true);
        NotifySelectionChanged();
    }

    /// <inheritdoc/>
    public bool DeleteSelection()
    {
        if (!CanEdit() || SelectionLength == 0) return false;
        ReplaceSelection("");
        AfterEdit();
        return true;
    }

    /// <summary>
    /// 选中指定折叠头对应的整块折叠内容（含折叠头行到折叠尾行）。
    /// 折叠已展开时仍选中该区间。
    /// </summary>
    public bool SelectCollapsedFoldAt(int startLine)
    {
        EnsureFolds();
        if (!TryGetFoldDocumentRange(startLine, out var start, out var end)) return false;
        _selectionAnchor = start;
        _caretIndex = end;
        EnsureCaretVisible();
        ResetCaretBlink(fullRepaint: true);
        NotifySelectionChanged();
        return true;
    }

    /// <summary>当前折叠头行（caret 所在）是否处于折叠状态。</summary>
    public bool IsCaretOnCollapsedFold
    {
        get
        {
            EnsureFolds();
            var line = _model.GetLineNumberAt(_caretIndex);
            return _folding.IsCollapsed(line);
        }
    }

    /// <summary>
    /// 获取折叠区间在文档中的 [start, end) offset。
    /// 包含折叠头行到 EndLine 行末（若非文档末行则含换行）。
    /// </summary>
    public bool TryGetFoldDocumentRange(int startLine, out int startOffset, out int endOffset)
    {
        startOffset = endOffset = 0;
        EnsureFolds();
        var region = _folding.GetRegionStartingAt(startLine);
        if (region is not { } fold || fold.HiddenLineCount <= 0) return false;
        startOffset = _model.GetLineStart(fold.StartLine);
        var endLine = Math.Min(fold.EndLine, _model.LineCount - 1);
        endOffset = _model.GetLineStart(endLine) + _model.GetLineContent(endLine).Length;
        // 包含 EndLine 后的换行，便于整块删除后不留下空行缝隙
        if (endLine < _model.LineCount - 1)
            endOffset = _model.GetLineStart(endLine + 1);
        return endOffset > startOffset;
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
            _caretBlinkAnimation = new Animation<float>(
                static (from, to, t) => from + (to - from) * t,
                _caretOpacity,
                _caretBlinkTarget,
                0.28f,
                static t => t,
                value => _caretOpacity = value);
            _caretBlinkAnimation.Start();
        }
        _caretBlinkAnimation.Update(1f / 30f);
        if (_caretBlinkAnimation.IsComplete)
            _nextCaretTransitionSeconds = now + (_caretBlinkTarget <= 0.01f ? 0.45d : 0.7d);
        InvalidateCaretPaint();
        return true;
    }

    /// <inheritdoc/>
    public void ResetCaretBlink() => ResetCaretBlink(fullRepaint: false);

    /// <summary>
    /// 重置光标闪烁；选区变化时请传 <paramref name="fullRepaint"/> = true，
    /// 避免局部 caret 脏区导致选区高亮残影。
    /// </summary>
    public void ResetCaretBlink(bool fullRepaint)
    {
        _caretOpacity = 1f;
        _caretBlinkTarget = 0f;
        _nextCaretTransitionSeconds = _caretClock.Elapsed.TotalSeconds + 0.7d;
        _caretBlinkAnimation = null;
        if (fullRepaint || SelectionLength > 0 || _dragging)
            InvalidatePaint();
        else
            InvalidateCaretPaint();
    }

    private void OnBlur()
    {
        _selectionAnchor = _caretIndex;
        _extraCursors.Clear();
        _dragging = false;
        _draggingVScroll = false;
        _draggingHScroll = false;
        _caretOpacity = 0f;
        _caretBlinkAnimation = null;
        InvalidatePaint();
    }

    /// <inheritdoc/>
    public CursorKind? ResolveCursorAt(Point point)
    {
        if (IsInNonTextGutter(point)) return CursorKind.Arrow;
        if (ShowScrollBars && HitTestScrollBar(point) != ScrollBarHit.None) return CursorKind.Arrow;
        return CursorKind.Text;
    }

    /// <summary>撤销。</summary>
    public bool Undo()
    {
        if (!CanEdit() || !_model.Undo(out var caret, out var carets)) return false;
        ApplyRestoredCarets(carets, caret);
        _tokens?.Reset();
        AfterEdit();
        NotifySelectionChanged();
        return true;
    }

    /// <summary>重做。</summary>
    public bool Redo()
    {
        if (!CanEdit() || !_model.Redo(out var caret, out var carets)) return false;
        ApplyRestoredCarets(carets, caret);
        _tokens?.Reset();
        AfterEdit();
        NotifySelectionChanged();
        return true;
    }

    private void ApplyRestoredCarets(int[] carets, int fallback)
    {
        _extraCursors.Clear();
        if (carets == null || carets.Length == 0)
        {
            _caretIndex = _selectionAnchor = Math.Clamp(fallback, 0, _model.Length);
            return;
        }
        var sorted = carets.Select(c => Math.Clamp(c, 0, _model.Length)).Distinct().OrderBy(c => c).ToArray();
        _caretIndex = _selectionAnchor = sorted[^1];
        for (var i = 0; i < sorted.Length - 1; i++)
            _extraCursors.Add(CodeEditorCursor.Collapsed(sorted[i]));
    }

    /// <summary>当前查找串。</summary>
    public string FindQuery
    {
        get => _findQuery;
        set => _findQuery = value ?? "";
    }

    /// <summary>当前替换串。</summary>
    public string ReplaceQuery
    {
        get => _replaceQuery;
        set => _replaceQuery = value ?? "";
    }

    /// <summary>查找是否区分大小写。</summary>
    public bool FindMatchCase
    {
        get => _findMatchCase;
        set => _findMatchCase = value;
    }

    /// <summary>当前查找串匹配总数（无查询时为 0）。</summary>
    public int FindMatchCount
    {
        get
        {
            if (string.IsNullOrEmpty(_findQuery)) return 0;
            var comparison = _findMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var text = _model.GetValue();
            var count = 0;
            var start = 0;
            while (start <= text.Length)
            {
                var idx = text.IndexOf(_findQuery, start, comparison);
                if (idx < 0) break;
                count++;
                start = idx + Math.Max(1, _findQuery.Length);
            }
            return count;
        }
    }

    /// <summary>当前匹配在全部匹配中的 1-based 序号；无匹配时为 0。</summary>
    public int FindMatchIndex
    {
        get
        {
            if (string.IsNullOrEmpty(_findQuery) || SelectionLength == 0) return 0;
            var comparison = _findMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            if (!string.Equals(SelectedText, _findQuery, comparison)) return 0;
            var text = _model.GetValue();
            var index = 0;
            var start = 0;
            while (start <= text.Length)
            {
                var idx = text.IndexOf(_findQuery, start, comparison);
                if (idx < 0) break;
                index++;
                if (idx == SelectionStart) return index;
                start = idx + Math.Max(1, _findQuery.Length);
            }
            return 0;
        }
    }

    /// <summary>返回包含查找匹配的文档行号（0-based，去重）。</summary>
    public IReadOnlyList<int> GetFindMatchLines()
    {
        if (string.IsNullOrEmpty(_findQuery)) return Array.Empty<int>();
        var comparison = _findMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var text = _model.GetValue();
        var lines = new List<int>();
        var start = 0;
        while (start <= text.Length)
        {
            var idx = text.IndexOf(_findQuery, start, comparison);
            if (idx < 0) break;
            var line = _model.GetLineNumberAt(idx);
            if (lines.Count == 0 || lines[^1] != line)
                lines.Add(line);
            start = idx + Math.Max(1, _findQuery.Length);
        }
        return lines;
    }

    /// <summary>查找下一处（从当前光标/选区末尾向后）。</summary>
    public bool FindNext(string? query = null)
    {
        if (!string.IsNullOrEmpty(query)) _findQuery = query;
        if (string.IsNullOrEmpty(_findQuery)) return false;
        var comparison = _findMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var text = _model.GetValue();
        var start = Math.Min(_model.Length, Math.Max(SelectionStart + (SelectionLength > 0 ? 1 : 0), _caretIndex));
        if (SelectionLength > 0 && start <= SelectionStart)
            start = SelectionStart + SelectionLength;
        var idx = start < text.Length ? text.IndexOf(_findQuery, start, comparison) : -1;
        if (idx < 0) idx = text.IndexOf(_findQuery, comparison);
        if (idx < 0) return false;
        SelectRange(idx, idx + _findQuery.Length);
        return true;
    }

    /// <summary>查找上一处。</summary>
    public bool FindPrevious(string? query = null)
    {
        if (!string.IsNullOrEmpty(query)) _findQuery = query;
        if (string.IsNullOrEmpty(_findQuery)) return false;
        var comparison = _findMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var text = _model.GetValue();
        var end = Math.Max(0, SelectionStart);
        var idx = end > 0 ? text.LastIndexOf(_findQuery, end - 1, comparison) : -1;
        if (idx < 0) idx = text.LastIndexOf(_findQuery, comparison);
        if (idx < 0) return false;
        SelectRange(idx, idx + _findQuery.Length);
        return true;
    }

    /// <summary>若当前选区匹配查找串则替换，并跳到下一处；否则仅查找下一处。</summary>
    public bool ReplaceNext(string? find = null, string? replace = null)
    {
        if (!CanEdit()) return false;
        if (!string.IsNullOrEmpty(find)) _findQuery = find;
        if (replace != null) _replaceQuery = replace;
        if (string.IsNullOrEmpty(_findQuery)) return false;

        var comparison = _findMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (SelectionLength > 0 &&
            string.Equals(SelectedText, _findQuery, comparison))
        {
            var start = SelectionStart;
            ReplaceSelection(_replaceQuery);
            _caretIndex = _selectionAnchor = start + _replaceQuery.Length;
            AfterEdit();
            FindNext();
            return true;
        }

        return FindNext();
    }

    /// <summary>替换全部匹配。</summary>
    public int ReplaceAll(string? find = null, string? replace = null)
    {
        if (!CanEdit()) return 0;
        if (!string.IsNullOrEmpty(find)) _findQuery = find;
        if (replace != null) _replaceQuery = replace;
        if (string.IsNullOrEmpty(_findQuery)) return 0;

        var comparison = _findMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var text = _model.GetValue();
        var count = 0;
        var idx = 0;
        var sb = new StringBuilder(text.Length);
        while (idx < text.Length)
        {
            var found = text.IndexOf(_findQuery, idx, comparison);
            if (found < 0)
            {
                sb.Append(text, idx, text.Length - idx);
                break;
            }
            sb.Append(text, idx, found - idx);
            sb.Append(_replaceQuery);
            idx = found + _findQuery.Length;
            count++;
        }

        if (count == 0) return 0;
        _model.SetValue(sb.ToString());
        _tokens?.Reset();
        _contentVersion++;
        _viewLayout.Invalidate();
        ScheduleFoldRecompute();
        ClampSelection();
        AfterEdit();
        return count;
    }

    /// <summary>设置文档选区 [start, end)（UTF-16 offset）。</summary>
    public void SelectRange(int start, int end)
    {
        start = Math.Clamp(start, 0, _model.Length);
        end = Math.Clamp(end, 0, _model.Length);
        _selectionAnchor = start;
        _caretIndex = end;
        EnsureCaretVisible();
        ResetCaretBlink(fullRepaint: true);
        NotifySelectionChanged();
    }

    private void HandleTab(bool shift)
    {
        if (shift)
        {
            UnindentCurrentLine();
            return;
        }
        var insert = InsertSpaces ? new string(' ', TabSize) : "\t";
        ReplaceSelection(insert);
        AfterEdit();
    }

    private void HandleEnter()
    {
        var indent = GetLineIndent(_model.GetLineNumberAt(SelectionStart));
        ReplaceSelection("\n" + indent);
        AfterEdit();
    }

    private void UnindentCurrentLine()
    {
        var line = _model.GetLineNumberAt(_caretIndex);
        var start = _model.GetLineStart(line);
        var content = _model.GetLineContent(line);
        var remove = 0;
        if (content.StartsWith('\t')) remove = 1;
        else
        {
            while (remove < content.Length && remove < TabSize && content[remove] == ' ')
                remove++;
        }
        if (remove == 0) return;
        var caretInLine = _caretIndex - start;
        _model.Replace(start, remove, "");
        _caretIndex = start + Math.Max(0, caretInLine - remove);
        _selectionAnchor = _caretIndex;
        AfterEdit();
    }

    private void ToggleLineComment()
    {
        var config = LanguageRegistry.ResolveConfiguration(Language);
        var prefix = config.LineComment;
        if (string.IsNullOrEmpty(prefix)) return;

        var startLine = _model.GetLineNumberAt(SelectionStart);
        var endLine = _model.GetLineNumberAt(SelectionStart + SelectionLength);
        var allCommented = true;
        for (var line = startLine; line <= endLine; line++)
        {
            var content = _model.GetLineContent(line).TrimStart();
            if (content.Length > 0 && !content.StartsWith(prefix, StringComparison.Ordinal))
            {
                allCommented = false;
                break;
            }
        }

        for (var line = endLine; line >= startLine; line--)
        {
            var start = _model.GetLineStart(line);
            var content = _model.GetLineContent(line);
            var trimStart = 0;
            while (trimStart < content.Length && char.IsWhiteSpace(content[trimStart])) trimStart++;
            if (allCommented)
            {
                if (content.AsSpan(trimStart).StartsWith(prefix, StringComparison.Ordinal))
                {
                    var extra = prefix.Length;
                    if (trimStart + extra < content.Length && content[trimStart + extra] == ' ')
                        extra++;
                    _model.Replace(start + trimStart, extra, "");
                }
            }
            else if (content.Length > 0)
            {
                _model.Replace(start + trimStart, 0, prefix + " ");
            }
        }
        AfterEdit();
    }

    private void Backspace()
    {
        if (SelectionLength > 0)
        {
            ReplaceSelection("");
            AfterEdit();
            return;
        }
        // 光标在折叠头行：Backspace 删除整块折叠（与 VS Code 类似）
        if (TrySelectCollapsedFoldAtCaretForEdit())
        {
            ReplaceSelection("");
            AfterEdit();
            return;
        }
        if (_caretIndex == 0) return;
        var prev = PreviousIndex(_caretIndex);
        // 若前一位置跨入折叠隐藏区，扩展删除到整个折叠
        if (TryExpandDeleteRangeToCollapsedFold(prev, _caretIndex, out var delStart, out var delEnd))
        {
            _selectionAnchor = delStart;
            _caretIndex = delEnd;
            ReplaceSelection("");
            AfterEdit();
            return;
        }
        _model.SetNextPreCaret(_caretIndex);
        _model.Replace(prev, _caretIndex - prev, "");
        _caretIndex = _selectionAnchor = prev;
        AfterEdit();
    }

    private void DeleteForward()
    {
        if (SelectionLength > 0)
        {
            ReplaceSelection("");
            AfterEdit();
            return;
        }
        // 光标在折叠头：Delete 删除整块折叠内容
        if (TrySelectCollapsedFoldAtCaretForEdit())
        {
            ReplaceSelection("");
            AfterEdit();
            return;
        }
        if (_caretIndex >= _model.Length) return;
        var next = NextIndex(_caretIndex);
        if (TryExpandDeleteRangeToCollapsedFold(_caretIndex, next, out var delStart, out var delEnd))
        {
            _selectionAnchor = delStart;
            _caretIndex = delEnd;
            ReplaceSelection("");
            AfterEdit();
            return;
        }
        _model.SetNextPreCaret(_caretIndex);
        _model.Replace(_caretIndex, next - _caretIndex, "");
        _selectionAnchor = _caretIndex;
        AfterEdit();
    }

    private void ReplaceSelection(string text)
    {
        ExpandSelectionToCoverCollapsedFolds();
        var start = SelectionStart;
        var length = SelectionLength;
        _model.SetNextPreCaret(_caretIndex);
        _model.Replace(start, length, text);
        _caretIndex = _selectionAnchor = start + text.Length;
    }

    /// <summary>
    /// 文档选区 [start, end)，若与折叠块相交则扩展到整块折叠。
    /// 用于 SelectedText / 删除 / 覆盖，保证剪切板与编辑范围一致。
    /// </summary>
    private (int Start, int End) GetEffectiveSelectionRange()
    {
        var start = SelectionStart;
        var end = SelectionStart + SelectionLength;
        if (end <= start) return (start, end);
        EnsureFolds();
        foreach (var region in _folding.Regions)
        {
            if (!_folding.IsCollapsed(region.StartLine) || region.HiddenLineCount <= 0) continue;
            if (!TryGetFoldDocumentRange(region.StartLine, out var foldStart, out var foldEnd)) continue;
            if (end <= foldStart || start >= foldEnd) continue;
            if (start > foldStart) start = foldStart;
            if (end < foldEnd) end = foldEnd;
        }
        return (start, end);
    }

    /// <summary>
    /// 编辑前将选区扩展到所有相交的折叠块，确保剪切/删除/覆盖包含隐藏行。
    /// </summary>
    private void ExpandSelectionToCoverCollapsedFolds()
    {
        if (SelectionLength == 0) return;
        var (start, end) = GetEffectiveSelectionRange();
        if (start == SelectionStart && end == SelectionStart + SelectionLength) return;
        _selectionAnchor = start;
        _caretIndex = end;
        NotifySelectionChanged();
    }

    private bool TrySelectCollapsedFoldAtCaretForEdit()
    {
        EnsureFolds();
        var line = _model.GetLineNumberAt(_caretIndex);
        if (!_folding.IsCollapsed(line)) return false;
        if (!TryGetFoldDocumentRange(line, out var start, out var end)) return false;
        // 仅当光标在折叠头行上时整块删除
        var lineStart = _model.GetLineStart(line);
        var lineEnd = lineStart + _model.GetLineContent(line).Length;
        if (_caretIndex < lineStart || _caretIndex > lineEnd) return false;
        _selectionAnchor = start;
        _caretIndex = end;
        return true;
    }

    private bool TryExpandDeleteRangeToCollapsedFold(int from, int to, out int delStart, out int delEnd)
    {
        delStart = Math.Min(from, to);
        delEnd = Math.Max(from, to);
        EnsureFolds();
        foreach (var region in _folding.Regions)
        {
            if (!_folding.IsCollapsed(region.StartLine)) continue;
            if (!TryGetFoldDocumentRange(region.StartLine, out var foldStart, out var foldEnd)) continue;
            if (delEnd <= foldStart || delStart >= foldEnd) continue;
            delStart = Math.Min(delStart, foldStart);
            delEnd = Math.Max(delEnd, foldEnd);
            return true;
        }
        return false;
    }

    private void MoveHorizontal(int direction, bool extend, bool byWord)
    {
        if (!extend && SelectionLength > 0)
        {
            _caretIndex = direction < 0 ? SelectionStart : SelectionStart + SelectionLength;
            _selectionAnchor = _caretIndex;
            InvalidatePaint();
            return;
        }
        var target = direction < 0
            ? byWord ? PreviousWord(_caretIndex) : PreviousIndex(_caretIndex)
            : byWord ? NextWord(_caretIndex) : NextIndex(_caretIndex);
        SetCaret(target, extend);
    }

    private void MoveVertical(int direction, bool extend)
    {
        EnsureFolds();
        var font = ResolveFont();
        var contentWidth = GetContentWidth();
        EnsurePaintConsistentViewLayout(font, contentWidth);
        var (line, col) = _model.GetPositionAt(_caretIndex);
        var content = _model.GetLineContent(line);
        var rowIndex = _viewLayout.OffsetToRow(_model, _caretIndex);
        var row = _viewLayout[rowIndex];
        var localCol = Math.Clamp(col - row.Start, 0, Math.Max(0, row.End - row.Start));
        var rowText = content.Length == 0 ? "" : content[row.Start..Math.Min(row.End, content.Length)];
        _preferredX ??= CodeEditorMetrics.XAtColumn(rowText, font, TabSize, localCol);

        var targetRowIndex = Math.Clamp(rowIndex + direction, 0, _viewLayout.RowCount - 1);
        if (targetRowIndex == rowIndex)
        {
            SetCaret(direction < 0 ? 0 : _model.Length, extend, preservePreferred: true);
            return;
        }

        var targetRow = _viewLayout[targetRowIndex];
        var targetContent = _model.GetLineContent(targetRow.DocumentLine);
        var targetSeg = targetContent.Length == 0
            ? ""
            : targetContent[targetRow.Start..Math.Min(targetRow.End, targetContent.Length)];
        var targetLocal = CodeEditorMetrics.ColumnAtX(targetSeg, font, TabSize, _preferredX.Value);
        var targetCol = targetRow.Start + targetLocal;
        SetCaret(_model.GetOffsetAt(targetRow.DocumentLine, targetCol), extend, preservePreferred: true);
    }

    private void MoveToLineBoundary(bool toStart, bool extend, bool control)
    {
        if (control)
        {
            SetCaret(toStart ? 0 : _model.Length, extend);
            return;
        }

        EnsureFolds();
        var font = ResolveFont();
        var contentWidth = GetContentWidth();
        EnsurePaintConsistentViewLayout(font, contentWidth);
        var rowIndex = _viewLayout.OffsetToRow(_model, _caretIndex);
        var row = _viewLayout[rowIndex];
        if (toStart)
            SetCaret(_model.GetOffsetAt(row.DocumentLine, row.Start), extend);
        else
            SetCaret(_model.GetOffsetAt(row.DocumentLine, row.End), extend);
    }

    private void SetCaret(int index, bool extend, bool preservePreferred = false)
    {
        _caretIndex = Math.Clamp(index, 0, _model.Length);
        if (!extend) _selectionAnchor = _caretIndex;
        if (!preservePreferred) _preferredX = null;
        EnsureCaretVisible();
        // Shift 扩展选区时必须全量刷新选区高亮
        ResetCaretBlink(fullRepaint: extend || SelectionLength > 0);
        NotifySelectionChanged();
    }

    private void AfterEdit(bool scrollOnly = false)
    {
        ClampSelection();
        if (!scrollOnly)
        {
            DispatchEvent(StandardEvents.CreateInput());
            InvalidateTokensFromCaret();
            ScheduleFoldRecompute();
        }
        EnsureCaretNotInHidden();
        EnsureCaretVisible();
        ResetCaretBlink();
        InvalidateLayout();
    }

    private void InvalidateTokensFromCaret()
    {
        if (_tokens == null) return;
        var line = _model.GetLineNumberAt(Math.Min(_caretIndex, _model.Length));
        _tokens.InvalidateFromLine(Math.Max(0, line - 1));
    }

    private TokenizationCache EnsureTokens()
    {
        if (_tokens == null || !string.Equals(_tokenizerLanguage, Language, StringComparison.OrdinalIgnoreCase))
        {
            _tokenizerLanguage = Language;
            _tokens = new TokenizationCache(LanguageRegistry.ResolveTokenizer(Language));
        }
        return _tokens;
    }

    private void PaintRowText(
        IRenderContext context,
        CodeEditorTheme theme,
        Font font,
        TokenizationCache tokens,
        CodeEditorViewRow row,
        float x,
        float y)
    {
        var content = _model.GetLineContent(row.DocumentLine);
        if (content.Length == 0 || row.Start >= content.Length) return;
        var end = Math.Min(row.End, content.Length);
        if (ShowFolding && _folding.IsCollapsed(row.DocumentLine) &&
            _folding.GetRegionStartingAt(row.DocumentLine) is { } fold && fold.StartColumn >= 0)
            end = Math.Min(end, fold.StartColumn);
        if (end <= row.Start) return;

        var spans = tokens.GetLineTokens(_model, row.DocumentLine);
        if (spans.Count == 0)
        {
            var slice = content[row.Start..end];
            DrawSegment(context, font, CodeEditorMetrics.ExpandTabs(slice, TabSize), x, y, theme.EditorForeground);
            return;
        }

        foreach (var span in spans)
        {
            var spanEnd = span.Start + span.Length;
            var from = Math.Max(span.Start, row.Start);
            var to = Math.Min(spanEnd, end);
            if (to <= from) continue;
            var segment = content[from..to];
            var expanded = CodeEditorMetrics.ExpandTabs(segment, TabSize);
            var color = theme.ResolveTokenColor(span.Type);
            var prefixInRow = content[row.Start..from];
            var sx = x + CodeEditorMetrics.XAtColumn(prefixInRow, font, TabSize, prefixInRow.Length);
            DrawSegment(context, font, expanded, sx, y, color);
        }
    }

    private static void DrawSegment(IRenderContext context, Font font, string text, float x, float y, Color color)
    {
        if (string.IsNullOrEmpty(text)) return;
        context.DrawText(new TextLayout(text, font), new Point(x, y), new SolidColorBrush(color));
    }

    private void PaintFoldPlaceholder(
        IRenderContext context,
        CodeEditorTheme theme,
        Font font,
        int line,
        float contentLeft,
        float y,
        float lineHeight)
    {
        if (_folding.GetRegionStartingAt(line) is not { } fold) return;
        var content = _model.GetLineContent(line);
        var column = fold.StartColumn >= 0 ? Math.Min(fold.StartColumn, content.Length) : content.Length;
        var prefix = content[..column];
        var x = contentLeft + CodeEditorMetrics.XAtColumn(prefix, font, TabSize, prefix.Length);
        var text = fold.Placeholder;
        var textWidth = MeasureTextWidth(text, font);
        var bounds = new Rect(x - 2, y + 1, textWidth + 4, Math.Max(1, lineHeight - 2));
        var color = theme.EditorLineNumberForeground;
        var selected = false;
        if (SelectionLength > 0 && TryGetFoldDocumentRange(line, out var foldStart, out var foldEnd))
        {
            var selectionStart = SelectionStart;
            var selectionEnd = selectionStart + SelectionLength;
            selected = selectionEnd > foldStart && selectionStart < foldEnd;
        }

        context.FillGeometry(
            new RoundedRectGeometry(bounds, 2, 2),
            new SolidColorBrush(selected
                ? theme.EditorSelectionBackground
                : Color.FromRgba(color.R, color.G, color.B, 24)));
        context.DrawGeometry(
            new RoundedRectGeometry(bounds, 2, 2),
            Pen.FromColor(Color.FromRgba(color.R, color.G, color.B, 72), 1));
        context.DrawText(new TextLayout(text, font), new Point(x, y), new SolidColorBrush(color));
    }

    private bool IsFoldPlaceholderRow(CodeEditorViewRow row)
    {
        if (!_folding.IsCollapsed(row.DocumentLine) ||
            _folding.GetRegionStartingAt(row.DocumentLine) is not { } fold)
            return false;
        var contentLength = _model.GetLineContent(row.DocumentLine).Length;
        var column = fold.StartColumn >= 0 ? Math.Min(fold.StartColumn, contentLength) : contentLength;
        return column >= row.Start && column <= row.End;
    }

    private void PaintSelectionForRow(
        IRenderContext context,
        CodeEditorTheme theme,
        Font font,
        CodeEditorViewRow row,
        float contentLeft,
        float y,
        float lineHeight)
    {
        if (SelectionLength == 0) return;
        var selStart = SelectionStart;
        var selEnd = SelectionStart + SelectionLength;
        PaintRangeHighlightOnRow(
            context,
            theme.EditorSelectionBackground,
            null,
            font,
            row,
            contentLeft,
            y,
            lineHeight,
            selStart,
            selEnd,
            expandNewline: true);

        // 折叠头：若选区覆盖折叠块（含隐藏行），高亮延伸到占位块，表示整块被选中
        if (!ShowFolding || !IsFoldPlaceholderRow(row))
            return;
        if (!TryGetFoldDocumentRange(row.DocumentLine, out var foldStart, out var foldEnd)) return;
        if (selEnd <= foldStart || selStart >= foldEnd) return;

        if (_folding.GetRegionStartingAt(row.DocumentLine) is not { } fold) return;
        var content = _model.GetLineContent(row.DocumentLine);
        var column = fold.StartColumn >= 0 ? Math.Min(fold.StartColumn, content.Length) : content.Length;
        var prefix = content[..column];
        var lineEndX = contentLeft + CodeEditorMetrics.XAtColumn(prefix, font, TabSize, prefix.Length);
        var ellipsisWidth = Math.Max(12f, MeasureTextWidth(fold.Placeholder, font) + 4f);
        context.FillRect(
            new Rect(lineEndX, y, ellipsisWidth, lineHeight),
            new SolidColorBrush(theme.EditorSelectionBackground));
    }

    private void PaintFindMatchesForRow(
        IRenderContext context,
        CodeEditorTheme theme,
        Font font,
        CodeEditorViewRow row,
        float contentLeft,
        float y,
        float lineHeight)
    {
        var query = _findQuery;
        if (string.IsNullOrEmpty(query)) return;
        var comparison = _findMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var content = _model.GetLineContent(row.DocumentLine);
        if (content.Length == 0) return;
        var lineStart = _model.GetLineStart(row.DocumentLine);
        var rowAbsStart = lineStart + row.Start;
        var rowAbsEnd = lineStart + row.End;
        var currentStart = SelectionStart;
        var currentEnd = SelectionStart + SelectionLength;

        // Search within this document line, paint overlaps with this view row.
        var searchFrom = 0;
        while (searchFrom < content.Length)
        {
            var local = content.IndexOf(query, searchFrom, comparison);
            if (local < 0) break;
            var absStart = lineStart + local;
            var absEnd = absStart + query.Length;
            searchFrom = local + Math.Max(1, query.Length);

            if (absEnd <= rowAbsStart || absStart >= rowAbsEnd) continue;
            var isCurrent = absStart == currentStart && absEnd == currentEnd && SelectionLength == query.Length;
            var bg = isCurrent
                ? (theme.FindMatchCurrentBackground.A > 0 ? theme.FindMatchCurrentBackground : theme.EditorSelectionBackground)
                : (theme.FindMatchBackground.A > 0 ? theme.FindMatchBackground : Color.FromRgba(255, 213, 0, 80));
            PaintRangeHighlightOnRow(context, bg, null, font, row, contentLeft, y, lineHeight, absStart, absEnd, expandNewline: false);
        }
    }

    private void PaintBracketMatchForRow(
        IRenderContext context,
        CodeEditorTheme theme,
        Font font,
        CodeEditorViewRow row,
        float contentLeft,
        float y,
        float lineHeight)
    {
        var config = LanguageRegistry.ResolveConfiguration(Language);
        if (!BracketMatcher.TryFindMatch(_model, config, _caretIndex, out var open, out var close))
            return;

        var bg = theme.BracketMatchBackground.A > 0
            ? theme.BracketMatchBackground
            : Color.FromRgba(180, 210, 255, 140);

        // 仅背景高亮，不画边框（对齐常见编辑器 pairing 样式）
        PaintRangeHighlightOnRow(context, bg, null, font, row, contentLeft, y, lineHeight, open, open + 1, expandNewline: false);
        PaintRangeHighlightOnRow(context, bg, null, font, row, contentLeft, y, lineHeight, close, close + 1, expandNewline: false);
    }

    private void PaintRangeHighlightOnRow(
        IRenderContext context,
        Color background,
        Color? border,
        Font font,
        CodeEditorViewRow row,
        float contentLeft,
        float y,
        float lineHeight,
        int rangeStart,
        int rangeEnd,
        bool expandNewline)
    {
        if (rangeEnd <= rangeStart) return;
        var lineStart = _model.GetLineStart(row.DocumentLine);
        var content = _model.GetLineContent(row.DocumentLine);
        var rowStart = lineStart + row.Start;
        var rowEnd = lineStart + row.End;
        var isLastRowOfLine = row.End >= content.Length;
        var hasNewline = row.DocumentLine < _model.LineCount - 1;
        var rangeEndEff = expandNewline && isLastRowOfLine && hasNewline ? rowEnd + 1 : rowEnd;
        var start = Math.Max(rangeStart, rowStart);
        var end = Math.Min(rangeEnd, rangeEndEff);
        if (end <= start && !(expandNewline && isLastRowOfLine && hasNewline && rangeEnd > rowEnd && rangeStart <= rowEnd))
            return;

        var startCol = Math.Clamp(start - lineStart - row.Start, 0, Math.Max(0, row.End - row.Start));
        var endCol = Math.Clamp(Math.Min(end, rowEnd) - lineStart - row.Start, 0, Math.Max(0, row.End - row.Start));
        var rowText = content.Length == 0 ? "" : content[row.Start..Math.Min(row.End, content.Length)];
        var x0 = contentLeft + CodeEditorMetrics.XAtColumn(rowText, font, TabSize, startCol);
        var x1 = contentLeft + CodeEditorMetrics.XAtColumn(rowText, font, TabSize, endCol);
        if (expandNewline && isLastRowOfLine && hasNewline && rangeEnd > rowEnd && end > rowEnd)
            x1 += 6;
        if (x1 <= x0) x1 = x0 + Math.Max(2, font.Size * 0.5f);
        var rect = new Rect(x0, y, x1 - x0, lineHeight);
        context.FillRect(rect, new SolidColorBrush(background));
        if (border is { } b && b.A > 0)
            context.DrawRect(rect, Pen.FromColor(b, 1));
    }

    private int HitTestOffset(Point point)
    {
        EnsureFolds();
        var font = ResolveFont();
        var lineHeight = GetLineHeight(font);
        var padding = DefaultPadding;
        var leftGutter = GetLeftGutterWidth();
        var contentWidth = GetContentWidth(padding);
        EnsurePaintConsistentViewLayout(font, contentWidth);
        var contentTop = Geometry.Y + padding;
        var contentLeft = Geometry.X + padding + leftGutter - (WordWrap ? 0f : _scrollX);
        var rowIndex = (int)MathF.Floor((point.Y - contentTop + _scrollY) / lineHeight);
        rowIndex = Math.Clamp(rowIndex, 0, _viewLayout.RowCount - 1);
        var row = _viewLayout[rowIndex];
        var content = _model.GetLineContent(row.DocumentLine);
        var rowText = content.Length == 0 ? "" : content[row.Start..Math.Min(row.End, content.Length)];
        var localCol = CodeEditorMetrics.ColumnAtX(rowText, font, TabSize, point.X - contentLeft);
        return _model.GetOffsetAt(row.DocumentLine, row.Start + localCol);
    }

    private bool IsInNonTextGutter(Point point)
    {
        if (!Geometry.Contains(point)) return false;
        var leftGutter = GetLeftGutterWidth();
        var rightGutter = GetRightGutterWidth();
        if (leftGutter > 0 && point.X < Geometry.X + leftGutter) return true;
        if (rightGutter > 0 && point.X >= Geometry.Right - rightGutter) return true;
        return false;
    }

    private float GetLeftGutterWidth()
    {
        var lineGutter = ShowLineNumbers ? MeasureLineNumberGutterWidth(ResolveFont()) : 0f;
        return GlyphMarginWidth + lineGutter + FoldingGutterWidth;
    }

    private float GetRightGutterWidth() => OverviewRulerGutterWidth;

    private float GetContentWidth(float padding = DefaultPadding)
        => Math.Max(1, Geometry.Width - padding * 2 - GetLeftGutterWidth() - GetRightGutterWidth());

    private bool TryHandleGutterClick(Point point, bool extendSelection = false)
    {
        if (!Geometry.Contains(point)) return false;
        var font = ResolveFont();
        var lineHeight = GetLineHeight(font);
        var padding = DefaultPadding;
        var glyphGutter = GlyphMarginWidth;
        var lineGutter = ShowLineNumbers ? MeasureLineNumberGutterWidth(font) : 0f;
        var foldGutter = FoldingGutterWidth;
        var leftGutter = glyphGutter + lineGutter + foldGutter;
        if (leftGutter <= 0 || point.X >= Geometry.X + leftGutter) return false;

        var contentWidth = GetContentWidth(padding);
        EnsurePaintConsistentViewLayout(font, contentWidth);
        EnsureFolds();
        var contentTop = Geometry.Y + padding;
        var rowIndex = (int)MathF.Floor((point.Y - contentTop + _scrollY) / lineHeight);
        rowIndex = Math.Clamp(rowIndex, 0, Math.Max(0, _viewLayout.RowCount - 1));
        var row = _viewLayout[rowIndex];
        var line = row.DocumentLine;
        var localX = point.X - Geometry.X;

        CodeEditorGutterLane lane;
        if (glyphGutter > 0 && localX < glyphGutter)
            lane = CodeEditorGutterLane.Glyph;
        else if (lineGutter > 0 && localX < glyphGutter + lineGutter)
            lane = CodeEditorGutterLane.LineNumbers;
        else
            lane = CodeEditorGutterLane.Folding;

        if (lane == CodeEditorGutterLane.Folding && ShowFolding && row.IsFirstOfDocumentLine && _folding.CanFoldAt(line))
        {
            // Shift+点击折叠槽：选中折叠内容（不切换折叠）
            if (extendSelection)
            {
                SelectCollapsedFoldAt(line);
                GutterClick?.Invoke(this, new CodeEditorGutterClickEventArgs(line, point, lane));
                return true;
            }
            ToggleFoldAt(line);
        }

        GutterClick?.Invoke(this, new CodeEditorGutterClickEventArgs(line, point, lane));
        return true;
    }

    private bool TrySelectCollapsedFoldAtPoint(Point point)
    {
        if (!ShowFolding || !Geometry.Contains(point)) return false;
        EnsureFolds();
        var font = ResolveFont();
        var lineHeight = GetLineHeight(font);
        var padding = DefaultPadding;
        var leftGutter = GetLeftGutterWidth();
        var contentWidth = GetContentWidth(padding);
        EnsurePaintConsistentViewLayout(font, contentWidth);
        var contentTop = Geometry.Y + padding;
        var contentLeft = Geometry.X + padding + leftGutter - (WordWrap ? 0f : _scrollX);
        var rowIndex = (int)MathF.Floor((point.Y - contentTop + _scrollY) / lineHeight);
        rowIndex = Math.Clamp(rowIndex, 0, Math.Max(0, _viewLayout.RowCount - 1));
        var row = _viewLayout[rowIndex];
        if (!row.IsFirstOfDocumentLine || !_folding.IsCollapsed(row.DocumentLine)) return false;

        var content = _model.GetLineContent(row.DocumentLine);
        var lineEndX = contentLeft + CodeEditorMetrics.XAtColumn(content, font, TabSize, content.Length);
        // 点击行尾 ⋯ 区域，或行内任意位置双击折叠头
        var hitEllipsis = point.X >= lineEndX - 4;
        var hitFoldLine = point.X >= Geometry.X + leftGutter;
        if (!hitEllipsis && !hitFoldLine) return false;
        return SelectCollapsedFoldAt(row.DocumentLine);
    }

    private void RebuildDecorationIndex()
    {
        _decorationsByLine.Clear();
        foreach (var decoration in _decorations.Values)
        {
            if (!_decorationsByLine.TryGetValue(decoration.Line, out var list))
            {
                list = [];
                _decorationsByLine[decoration.Line] = list;
            }
            list.Add(decoration);
        }
    }

    private void PaintDecorationLineBackground(
        IRenderContext context,
        int line,
        float x,
        float y,
        float width,
        float lineHeight)
    {
        if (!_decorationsByLine.TryGetValue(line, out var list)) return;
        foreach (var decoration in list)
        {
            if (decoration.LineBackground is not { } bg) continue;
            context.FillRect(new Rect(x, y, width, lineHeight), new SolidColorBrush(bg));
        }
    }

    private void PaintGlyphMarginForLine(
        IRenderContext context,
        CodeEditorTheme theme,
        Font font,
        int line,
        float gutterLeft,
        float y,
        float glyphGutter,
        float lineHeight)
    {
        if (!_decorationsByLine.TryGetValue(line, out var list) || list.Count == 0) return;

        Color? barColor = null;
        CodeEditorLineDecoration? glyphDecoration = null;
        foreach (var decoration in list)
        {
            if (decoration.GutterColor is { } color)
                barColor = color;
            if (!string.IsNullOrEmpty(decoration.Glyph))
                glyphDecoration = decoration;
        }

        if (barColor is { } bar)
        {
            var barX = glyphGutter > 0 ? gutterLeft + 1 : gutterLeft + 1;
            context.FillRect(
                new Rect(barX, y, GutterColorBarWidth, lineHeight),
                new SolidColorBrush(bar));
        }

        if (glyphGutter <= 0 || glyphDecoration == null || string.IsNullOrEmpty(glyphDecoration.Glyph))
            return;

        var glyph = glyphDecoration.Glyph!;
        var glyphColor = glyphDecoration.GlyphColor ?? theme.EditorLineNumberActiveForeground;
        if (glyphColor.A == 0) glyphColor = theme.EditorForeground;
        var glyphWidth = MeasureTextWidth(glyph, font);
        var gx = gutterLeft + (glyphGutter - glyphWidth) / 2f;
        context.DrawText(new TextLayout(glyph, font), new Point(gx, y), new SolidColorBrush(glyphColor));
    }

    private Rect ComputeCaretRect()
    {
        var font = ResolveFont();
        var leftGutter = GetLeftGutterWidth();
        return ComputeCaretRect(font, GetLineHeight(font), DefaultPadding, leftGutter);
    }

    private Rect ComputeCaretRect(Font font, float lineHeight, float padding, float gutter)
    {
        EnsureFolds();
        var metrics = GetScrollMetrics(font, lineHeight);
        var contentWidth = metrics.ViewportRect.Width;
        EnsurePaintConsistentViewLayout(font, contentWidth);
        var (line, col) = _model.GetPositionAt(_caretIndex);
        if (_folding.IsLineHidden(line))
        {
            line = _folding.VisualToDocument(_folding.DocumentToVisual(line));
            col = Math.Min(col, _model.GetLineContent(line).Length);
        }
        var rowIndex = _viewLayout.OffsetToRow(_model, _model.GetOffsetAt(line, col));
        var row = _viewLayout[rowIndex];
        var content = _model.GetLineContent(row.DocumentLine);
        var localCol = Math.Clamp(col - row.Start, 0, Math.Max(0, row.End - row.Start));
        var rowText = content.Length == 0 ? "" : content[row.Start..Math.Min(row.End, content.Length)];
        var x = metrics.ViewportRect.Left - (WordWrap ? 0f : _scrollX)
                + CodeEditorMetrics.XAtColumn(rowText, font, TabSize, localCol);
        var y = metrics.ViewportRect.Top + rowIndex * lineHeight - _scrollY;
        return new Rect(MathF.Round(x), MathF.Round(y), 1, Math.Max(1, lineHeight));
    }

    private void EnsureCaretVisible()
    {
        var beforeScrollX = _scrollX;
        var beforeScrollY = _scrollY;
        EnsureFolds();
        var font = ResolveFont();
        var lineHeight = GetLineHeight(font);
        var scrollMetrics = GetScrollMetrics(font, lineHeight);
        var contentHeight = scrollMetrics.ViewportRect.Height;
        if (contentHeight <= 0) return;
        var contentWidth = scrollMetrics.ViewportRect.Width;
        EnsurePaintConsistentViewLayout(font, contentWidth);
        var rowIndex = _viewLayout.OffsetToRow(_model, _caretIndex);
        var caretTop = rowIndex * lineHeight;
        var caretBottom = caretTop + lineHeight;
        if (caretTop < _scrollY)
            _scrollY = caretTop;
        else if (caretBottom > _scrollY + contentHeight)
            _scrollY = caretBottom - contentHeight;

        if (!WordWrap)
        {
            var row = _viewLayout[rowIndex];
            var content = _model.GetLineContent(row.DocumentLine);
            var col = _model.GetPositionAt(_caretIndex).Column;
            var localCol = Math.Clamp(col - row.Start, 0, Math.Max(0, row.End - row.Start));
            var rowText = content.Length == 0 ? "" : content[row.Start..Math.Min(row.End, content.Length)];
            var caretX = CodeEditorMetrics.XAtColumn(rowText, font, TabSize, localCol);
            if (caretX < _scrollX)
                _scrollX = caretX;
            else if (caretX > _scrollX + contentWidth - 2)
                _scrollX = caretX - contentWidth + 2;
        }
        else
        {
            _scrollX = 0;
        }

        EnsureScroll(lineHeight, contentHeight, font, contentWidth, beforeScrollX, beforeScrollY);
    }

    /// <summary>
    /// 拖选靠近视口边缘时自动滚动，使选区能越过当前可见范围。
    /// </summary>
    private bool AutoScrollDuringDrag(Point point)
    {
        var font = ResolveFont();
        var lineHeight = Math.Max(1f, GetLineHeight(font));
        var padding = DefaultPadding;
        var leftGutter = GetLeftGutterWidth();
        var rightGutter = GetRightGutterWidth();
        var contentTop = Geometry.Y + padding;
        var contentBottom = Geometry.Bottom - padding;
        var contentLeft = Geometry.X + padding + leftGutter;
        var contentRight = Geometry.Right - padding - rightGutter;
        var edgeY = Math.Max(8f, lineHeight);
        var edgeX = Math.Max(12f, font.Size);

        var beforeY = _scrollY;
        var beforeX = _scrollX;

        if (point.Y < contentTop + edgeY)
            _scrollY -= lineHeight * Math.Clamp((contentTop + edgeY - point.Y) / edgeY, 0.25f, 3f);
        else if (point.Y > contentBottom - edgeY)
            _scrollY += lineHeight * Math.Clamp((point.Y - (contentBottom - edgeY)) / edgeY, 0.25f, 3f);

        if (!WordWrap)
        {
            if (point.X < contentLeft + edgeX)
                _scrollX -= edgeX * Math.Clamp((contentLeft + edgeX - point.X) / edgeX, 0.25f, 3f);
            else if (point.X > contentRight - edgeX)
                _scrollX += edgeX * Math.Clamp((point.X - (contentRight - edgeX)) / edgeX, 0.25f, 3f);
        }

        var contentHeight = Math.Max(0, Geometry.Height - padding * 2);
        var contentWidth = GetContentWidth(padding);
        EnsureScroll(lineHeight, contentHeight, font, contentWidth, beforeX, beforeY);
        return Math.Abs(_scrollY - beforeY) > 0.01f || Math.Abs(_scrollX - beforeX) > 0.01f;
    }

    private void EnsureScroll(
        float lineHeight,
        float contentHeight,
        Font? font = null,
        float? contentWidth = null,
        float? previousScrollX = null,
        float? previousScrollY = null)
    {
        var beforeScrollX = previousScrollX ?? _scrollX;
        var beforeScrollY = previousScrollY ?? _scrollY;
        EnsureFolds();
        font ??= ResolveFont();
        var metrics = GetScrollMetrics(font, lineHeight);
        var viewportHeight = metrics.ViewportRect.Height;
        var viewportWidth = metrics.ViewportRect.Width;
        EnsureViewLayout(font, Math.Max(1, viewportWidth));

        var maxScrollY = Math.Max(0, _viewLayout.RowCount * lineHeight - viewportHeight);
        _scrollY = Math.Clamp(_scrollY, 0, maxScrollY);

        if (WordWrap)
        {
            _scrollX = 0;
        }
        else
        {
            var maxContentWidth = MeasureMaxContentWidth(font);
            var maxScrollX = Math.Max(0, maxContentWidth - viewportWidth);
            _scrollX = Math.Clamp(_scrollX, 0, maxScrollX);
        }

        SetScrollContentSize(new Size(
            Geometry.Width + (WordWrap ? 0 : Math.Max(0, MeasureMaxContentWidth(font) - viewportWidth)),
            _viewLayout.RowCount * lineHeight + DefaultPadding * 2));
        if (Math.Abs(_scrollX - beforeScrollX) > 0.01f || Math.Abs(_scrollY - beforeScrollY) > 0.01f)
            DispatchEvent(StandardEvents.CreateScroll());
    }
    private float MeasureMaxContentWidth(Font font)
    {
        var max = 0f;
        // sample visible-ish bound: scan all document lines (ok for moderate docs)
        for (var line = 0; line < _model.LineCount; line++)
        {
            if (_folding.IsLineHidden(line)) continue;
            var content = _model.GetLineContent(line);
            max = Math.Max(max, CodeEditorMetrics.MeasureLineWidth(content, font, TabSize));
        }
        return max + 24f;
    }

    private void EnsureViewLayout(Font font, float contentWidth)
    {
        EnsureFolds();
        _viewLayout.Ensure(
            _model,
            _folding,
            font,
            TabSize,
            contentWidth,
            WordWrap,
            _contentVersion);
    }

    /// <summary>按绘制一致的宽度刷新视图布局；非 WordWrap 保持调用方宽度。</summary>
    private void EnsurePaintConsistentViewLayout(Font font, float contentWidth)
    {
        if (WordWrap)
        {
            var metrics = GetScrollMetrics(font, GetLineHeight(font));
            contentWidth = Math.Max(1, metrics.ViewportRect.Width);
        }
        EnsureViewLayout(font, contentWidth);
    }

    private void ScheduleFoldRecompute() => _foldModelVersion = -1; // dirty

    private void EnsureFolds()
    {
        if (!ShowFolding)
        {
            if (_foldModelVersion != 0)
            {
                _folding.ExpandAll();
                // empty region list + identity maps
                _folding.Recompute(_model, LanguageConfiguration.PlainText, "plaintext");
                _foldModelVersion = 0;
            }
            return;
        }

        // -1 means dirty (edit/language change).
        if (_foldModelVersion > 0)
            return;

        var config = LanguageRegistry.ResolveConfiguration(Language);
        _folding.Recompute(_model, config, Language);
        _foldModelVersion = 1;
    }

    /// <summary>
    /// 折叠后保证光标可显示。
    /// 有选区时绝不改动 caret/anchor：SelectionStart/Length 由二者算出，
    /// 移动任一端都会改变选区范围（用户反馈“折叠后选择范围会变”）。
    /// 仅无选区且 caret 落在隐藏行时，把折叠点移到可见折叠头。
    /// </summary>
    private void EnsureCaretNotInHidden()
    {
        EnsureFolds();
        if (SelectionLength > 0) return;
        var caretLine = _model.GetLineNumberAt(_caretIndex);
        if (!_folding.IsLineHidden(caretLine)) return;
        _caretIndex = ClampOffsetToVisibleLine(_caretIndex);
        _selectionAnchor = _caretIndex;
    }

    private int ClampOffsetToVisibleLine(int offset)
    {
        offset = Math.Clamp(offset, 0, _model.Length);
        var line = _model.GetLineNumberAt(offset);
        if (!_folding.IsLineHidden(line)) return offset;
        var visual = _folding.DocumentToVisual(line);
        var visibleLine = _folding.VisualToDocument(visual);
        var col = Math.Min(_model.GetPositionAt(offset).Column, _model.GetLineContent(visibleLine).Length);
        return _model.GetOffsetAt(visibleLine, col);
    }

    protected override void OnFrameDueCore()
    {
        base.OnFrameDueCore();
        if (!_scrollbarFadeActive) return;
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var deltaSeconds = (now - _scrollbarFadeLastTimestamp) / (float)System.Diagnostics.Stopwatch.Frequency;
        _scrollbarFadeLastTimestamp = now;
        AdvanceScrollbarFade(deltaSeconds);
    }

    internal new void AdvanceScrollbarFade(float deltaSeconds)
    {
        if (!_scrollbarFadeActive) return;
        _scrollbarFadeElapsed += Math.Max(0, float.IsFinite(deltaSeconds) ? deltaSeconds : 0);
        var fadeProgress = _scrollbarFadeElapsed <= ScrollBarFadeDelaySeconds
            ? 0f
            : Math.Clamp(
                (_scrollbarFadeElapsed - ScrollBarFadeDelaySeconds) / ScrollBarFadeDurationSeconds,
                0,
                1);
        var fadeComplete = _scrollbarFadeElapsed + 0.0001f >=
            ScrollBarFadeDelaySeconds + ScrollBarFadeDurationSeconds;
        var nextOpacity = fadeComplete ? 0f : 1f - fadeProgress;
        var changed = Math.Abs(_scrollbarOpacity - nextOpacity) > 0.001f;
        _scrollbarOpacity = nextOpacity;
        if (fadeComplete)
            _scrollbarFadeActive = false;
        if (changed) InvalidatePaint();
        if (_scrollbarFadeActive) DispatchEvent(StandardEvents.CreateRequestFrame());
    }

    private void ShowEditorScrollbar()
    {
        if (!IsTransientScrollbar || !ShowScrollBars || ScrollbarVisibility == ScrollbarVisibilityMode.Hidden) return;
        var font = ResolveFont();
        var metrics = GetScrollMetrics(font, GetLineHeight(font));
        if (!metrics.HasVertical && !metrics.HasHorizontal) return;
        _scrollbarOpacity = 1f;
        _scrollbarFadeElapsed = 0;
        _scrollbarFadeLastTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        _scrollbarFadeActive = true;
        if (IsAttached && IsEffectivelyVisible)
            DispatchEvent(StandardEvents.CreateRequestFrame());
        InvalidatePaint();
    }

    protected override void OnAttachedCore()
    {
        base.OnAttachedCore();
        if (_scrollbarFadeActive)
        {
            _scrollbarFadeLastTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            DispatchEvent(StandardEvents.CreateRequestFrame());
        }
    }

    protected override void OnEffectiveVisibilityChanged(bool isVisible)
    {
        base.OnEffectiveVisibilityChanged(isVisible);
        if (!isVisible)
        {
            ClearScrollbarCapture();
            return;
        }
        if (_scrollbarFadeActive)
        {
            _scrollbarFadeLastTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            DispatchEvent(StandardEvents.CreateRequestFrame());
        }
    }

    private void ClearScrollbarCapture()
    {
        if (!_draggingVScroll && !_draggingHScroll && _scrollbarRepeatHit == ScrollBarHit.None) return;
        _draggingVScroll = false;
        _draggingHScroll = false;
        _scrollbarRepeatHit = ScrollBarHit.None;
        _scrollbarRepeatPointerInside = false;
        InvalidatePaint();
        _scrollbarPressedPart = ScrollbarPart.None;
    }

    private bool IsCssDisplayedForScrollbar()
    {
        for (Element? current = this; current != null; current = current.Parent)
            if (string.Equals(current.Style.Get("display")?.Trim(), "none", StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    private bool IsScrollbarCssHidden()
    {
        for (Element? current = this; current != null; current = current.Parent)
        {
            var value = current.Style.Get("visibility")?.Trim();
            if (string.IsNullOrEmpty(value)) continue;
            return string.Equals(value, "hidden", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "collapse", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
    private ScrollbarWidthMode GetScrollbarWidthMode() =>
        Style.Get("scrollbar-width")?.Trim().ToLowerInvariant() switch
        {
            "thin" => ScrollbarWidthMode.Thin,
            "none" => ScrollbarWidthMode.None,
            _ => ScrollbarWidthMode.Auto
        };

    private ScrollbarGutterMode GetScrollbarGutterMode() =>
        Style.Get("scrollbar-gutter")?.Trim().ToLowerInvariant() switch
        {
            "stable" => ScrollbarGutterMode.Stable,
            "stable both-edges" => ScrollbarGutterMode.StableBothEdges,
            _ => ScrollbarGutterMode.Auto
        };

    private bool IsEditorScrollbarVisible(ScrollbarMetrics metrics)
    {
        if (!ShowScrollBars || !metrics.HasVertical && !metrics.HasHorizontal ||
            GetScrollbarWidthMode() == ScrollbarWidthMode.None || IsScrollbarCssHidden() ||
            !IsCssDisplayedForScrollbar() || ScrollbarVisibility == ScrollbarVisibilityMode.Hidden)
            return false;
        var hovered = HasState(ElementState.Hover) || _scrollbarHoverPart != ScrollbarPart.None;
        return ScrollbarVisibility switch
        {
            ScrollbarVisibilityMode.Always => true,
            ScrollbarVisibilityMode.Hover => hovered,
            ScrollbarVisibilityMode.Scroll => hovered || _scrollbarOpacity > 0.001f,
            _ => metrics.IsOverlay ? _scrollbarOpacity > 0.001f : true
        };
    }

    private bool TryParseScrollbarColors(out Color thumb, out Color track)
    {
        thumb = default;
        track = default;
        var value = Style.Get("scrollbar-color")?.Trim();
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
            return false;
        var depth = 0;
        var split = -1;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '(') depth++;
            else if (value[i] == ')') depth = Math.Max(0, depth - 1);
            else if (depth == 0 && char.IsWhiteSpace(value[i])) { split = i; break; }
        }
        if (split < 0) return false;
        var first = value[..split].Trim();
        var second = value[split..].Trim();
        return first.Length > 0 && second.Length > 0 &&
            Color.TryParse(first, out thumb) && Color.TryParse(second, out track);
    }

    private bool IsScrollbarPseudoDisplayNone(string pseudoElement = ScrollbarPseudoElements.Scrollbar) =>
        string.Equals(GetScrollbarPseudoStyle(pseudoElement, "", "display")?.Trim(),
            "none", StringComparison.OrdinalIgnoreCase);

    private static float? ParseScrollbarLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase)) text = text[..^2].Trim();
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) &&
               float.IsFinite(result) && result >= 0
            ? result
            : null;
    }

    private Color ResolvePseudoColor(
        string pseudoElement,
        ScrollbarPart part,
        ScrollbarPart pressedPart,
        ScrollbarPart hoverPart,
        Color fallback) =>
        TryResolvePseudoColor(pseudoElement, part, pressedPart, hoverPart, out var color)
            ? color
            : fallback;

    private bool TryResolvePseudoColor(
        string pseudoElement,
        ScrollbarPart part,
        ScrollbarPart pressedPart,
        ScrollbarPart hoverPart,
        out Color color)
    {
        var state = GetScrollbarPseudoState(pressedPart, hoverPart, part);
        foreach (var property in new[] { "background-color", "background" })
        {
            var value = state.Length == 0 ? null : GetScrollbarPseudoStyle(pseudoElement, state, property);
            value ??= GetScrollbarPseudoStyle(pseudoElement, "", property);
            if (TryParsePseudoColor(value, out color)) return true;
        }
        color = default;
        return false;
    }

    private static string GetScrollbarPseudoState(
        ScrollbarPart pressedPart,
        ScrollbarPart hoverPart,
        ScrollbarPart part) =>
        pressedPart == part ? "active" : hoverPart == part ? "hover" : "";

    private bool TryParsePseudoColor(string? value, out Color color)
    {
        if (string.Equals(value?.Trim(), "currentcolor", StringComparison.OrdinalIgnoreCase))
            return Color.TryParse(Style.Get("color"), out color);
        return Color.TryParse(value, out color);
    }

    private static float ParseScrollbarOpacity(string? value) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity) &&
        float.IsFinite(opacity)
            ? Math.Clamp(opacity, 0, 1)
            : 1;

    private static float? ParseScrollbarRadius(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase)) text = text[..^2].Trim();
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var radius) &&
               float.IsFinite(radius) && radius >= 0
            ? radius
            : null;
    }

    private static Color WithOpacity(Color color, float opacity) =>
        Color.FromRgba(color.R, color.G, color.B, (byte)Math.Clamp(color.A * opacity, 0, 255));

    private void ScrollEditorPage(int direction)
    {
        var beforeX = _scrollX;
        var beforeY = _scrollY;
        var font = ResolveFont();
        var lineHeight = GetLineHeight(font);
        var metrics = GetScrollMetrics(font, lineHeight);
        var page = Math.Max(lineHeight, metrics.ViewportRect.Height * 0.9f);
        _scrollY = Math.Clamp(_scrollY + direction * page, 0, metrics.MaxScrollY);
        EnsureScroll(lineHeight, metrics.ViewportRect.Height, font, metrics.ViewportRect.Width, beforeX, beforeY);
        ShowEditorScrollbar();
        InvalidatePaint();
    }

    protected override void OnDetachedCore()
    {
        _draggingVScroll = false;
        _draggingHScroll = false;
        _dragging = false;
        _scrollbarFadeActive = false;
        _scrollbarFadeElapsed = 0;
        _scrollbarOpacity = 0;
        _scrollbarRepeatHit = ScrollBarHit.None;
        _scrollbarRepeatPointerInside = false;
        _scrollbarPressedPart = ScrollbarPart.None;
        _scrollbarHoverPart = ScrollbarPart.None;
        base.OnDetachedCore();
    }

    private void OnWheel(Event e)
    {
        if (e is not WheelEvent wheel) return;
        var beforeX = _scrollX;
        var beforeY = _scrollY;
        var font = ResolveFont();
        var lineHeight = GetLineHeight(font);
        var metrics = GetScrollMetrics(font, lineHeight);
        _scrollY = Math.Max(0, _scrollY + wheel.DeltaY);
        if (!WordWrap && Math.Abs(wheel.DeltaX) > 0.01f)
            _scrollX = Math.Max(0, _scrollX + wheel.DeltaX);
        EnsureScroll(lineHeight, metrics.ViewportRect.Height, font, metrics.ViewportRect.Width, beforeX, beforeY);
        var changed = Math.Abs(_scrollY - beforeY) > 0.01f || Math.Abs(_scrollX - beforeX) > 0.01f;
        if (!changed) return;
        ShowEditorScrollbar();
        InvalidatePaint();
        e.PreventDefault();
        e.StopPropagation();
    }

    private enum ScrollBarHit
    {
        None,
        VerticalBackButton,
        VerticalTrack,
        VerticalThumb,
        VerticalForwardButton,
        HorizontalBackButton,
        HorizontalTrack,
        HorizontalThumb,
        HorizontalForwardButton,
        Corner
    }

    private ScrollbarMetrics GetScrollMetrics(Font font, float lineHeight)
    {
        var padding = DefaultPadding;
        var leftGutter = GetLeftGutterWidth();
        var rightGutter = GetRightGutterWidth();
        var baseWidth = Math.Max(1, Geometry.Width - padding * 2 - leftGutter - rightGutter);
        var baseHeight = Math.Max(1, Geometry.Height - padding * 2);
        var scrollBounds = new Rect(
            Geometry.X + padding + leftGutter,
            Geometry.Y + padding,
            baseWidth,
            baseHeight);
        EnsureViewLayout(font, baseWidth);

        var contentHeight = _viewLayout.RowCount * lineHeight;
        var contentWidth = WordWrap ? baseWidth : MeasureMaxContentWidth(font);

        var widthMode = ShowScrollBars && ScrollbarVisibility != ScrollbarVisibilityMode.Hidden
            ? GetScrollbarWidthMode()
            : ScrollbarWidthMode.None;
        if (IsScrollbarPseudoDisplayNone())
            widthMode = ScrollbarWidthMode.None;
        var gutterMode = GetScrollbarGutterMode();
        var verticalThickness = ParseScrollbarLength(
            GetScrollbarPseudoStyle(ScrollbarPseudoElements.Scrollbar, "", "width"));
        var horizontalThickness = ParseScrollbarLength(
            GetScrollbarPseudoStyle(ScrollbarPseudoElements.Scrollbar, "", "height"));
        var hideButtons = IsScrollbarPseudoDisplayNone(ScrollbarPseudoElements.Button);
        var metrics = ScrollbarGeometry.Calculate(
            scrollBounds,
            new Size(contentWidth, contentHeight),
            new Point(_scrollX, _scrollY),
            verticalEnabled: ShowScrollBars,
            horizontalEnabled: ShowScrollBars && !WordWrap,
            profile: AppWindow?.ScrollbarProfile ?? ScrollbarDeviceProfile.Auto,
            width: widthMode,
            gutter: gutterMode,
            hideButtons: hideButtons,
            verticalThicknessOverride: verticalThickness,
            horizontalThicknessOverride: horizontalThickness,
            hideThumb: IsScrollbarPseudoDisplayNone(ScrollbarPseudoElements.Thumb),
            hideTrack: IsScrollbarPseudoDisplayNone(ScrollbarPseudoElements.Track) ||
                IsScrollbarPseudoDisplayNone(ScrollbarPseudoElements.TrackPiece),
            hideCorner: IsScrollbarPseudoDisplayNone(ScrollbarPseudoElements.Corner));
        if (WordWrap && ShowScrollBars && metrics.HasVertical &&
            metrics.ViewportRect.Width < scrollBounds.Width)
        {
            EnsureViewLayout(font, Math.Max(1, metrics.ViewportRect.Width));
            contentHeight = _viewLayout.RowCount * lineHeight;
            metrics = ScrollbarGeometry.Calculate(
                scrollBounds,
                new Size(baseWidth, contentHeight),
                new Point(_scrollX, _scrollY),
                verticalEnabled: ShowScrollBars,
                horizontalEnabled: false,
                profile: AppWindow?.ScrollbarProfile ?? ScrollbarDeviceProfile.Auto,
                width: widthMode,
                gutter: gutterMode,
                hideButtons: hideButtons,
                verticalThicknessOverride: verticalThickness,
                horizontalThicknessOverride: horizontalThickness,
                hideThumb: IsScrollbarPseudoDisplayNone(ScrollbarPseudoElements.Thumb),
                hideTrack: IsScrollbarPseudoDisplayNone(ScrollbarPseudoElements.Track) ||
                    IsScrollbarPseudoDisplayNone(ScrollbarPseudoElements.TrackPiece),
                hideCorner: IsScrollbarPseudoDisplayNone(ScrollbarPseudoElements.Corner));
        }

        return metrics with { };
    }

    private void PaintScrollBars(
        IRenderContext context,
        CodeEditorTheme theme,
        Font font,
        float lineHeight,
        float padding,
        float leftGutter,
        float contentWidth,
        float contentHeight)
    {
        var metrics = GetScrollMetrics(font, lineHeight);
        if (!IsEditorScrollbarVisible(metrics)) return;

        var track = theme.ScrollBarTrack.A > 0 ? theme.ScrollBarTrack : Color.FromRgba(80, 80, 80, 180);
        var thumb = theme.ScrollBarThumb.A > 0 ? theme.ScrollBarThumb : Color.FromRgba(140, 140, 140, 180);
        var thumbActive = theme.ScrollBarThumbActive.A > 0 ? theme.ScrollBarThumbActive : Color.FromRgba(180, 180, 180, 220);
        if (TryParseScrollbarColors(out var cssThumb, out var cssTrack))
        {
            thumb = cssThumb;
            track = cssTrack;
        }
        var activePart = _scrollbarPressedPart;
        var hoverPart = _scrollbarHoverPart;
        var hasThumbStyles = HasScrollbarPseudoStylesFor(ScrollbarPseudoElements.Thumb);
        var hasTrackStyles = HasScrollbarPseudoStylesFor(ScrollbarPseudoElements.Track) ||
            HasScrollbarPseudoStylesFor(ScrollbarPseudoElements.TrackPiece);
        var scrollbarBackground = TryResolvePseudoColor(
            ScrollbarPseudoElements.Scrollbar, ScrollbarPart.None, activePart, hoverPart, out var scrollbarColor)
            ? scrollbarColor
            : (Color?)null;
        var verticalThumb = ResolvePseudoColor(
            ScrollbarPseudoElements.Thumb, ScrollbarPart.VerticalThumb, activePart, hoverPart, thumb);
        var horizontalThumb = ResolvePseudoColor(
            ScrollbarPseudoElements.Thumb, ScrollbarPart.HorizontalThumb, activePart, hoverPart, thumb);
        var verticalTrack = ResolvePseudoColor(
            ScrollbarPseudoElements.Track, ScrollbarPart.VerticalTrack, activePart, hoverPart,
            scrollbarBackground ?? track);
        verticalTrack = ResolvePseudoColor(
            ScrollbarPseudoElements.TrackPiece, ScrollbarPart.VerticalTrack, activePart, hoverPart, verticalTrack);
        var horizontalTrack = ResolvePseudoColor(
            ScrollbarPseudoElements.Track, ScrollbarPart.HorizontalTrack, activePart, hoverPart,
            scrollbarBackground ?? track);
        horizontalTrack = ResolvePseudoColor(
            ScrollbarPseudoElements.TrackPiece, ScrollbarPart.HorizontalTrack, activePart, hoverPart, horizontalTrack);
        var buttonBackground = TryResolvePseudoColor(
            ScrollbarPseudoElements.Button, ScrollbarPart.None, activePart, hoverPart, out var buttonColor)
            ? buttonColor
            : (Color?)null;
        Color? cornerBackground = null;
        if (TryResolvePseudoColor(
                ScrollbarPseudoElements.Corner, ScrollbarPart.Corner, activePart, hoverPart, out var cornerColor) ||
            TryResolvePseudoColor(
                ScrollbarPseudoElements.Resizer, ScrollbarPart.Corner, activePart, hoverPart, out cornerColor))
            cornerBackground = cornerColor;
        if (IsScrollbarPseudoDisplayNone(ScrollbarPseudoElements.Thumb))
        {
            verticalThumb = Color.Transparent;
            horizontalThumb = Color.Transparent;
        }
        if (IsScrollbarPseudoDisplayNone(ScrollbarPseudoElements.Track) ||
            IsScrollbarPseudoDisplayNone(ScrollbarPseudoElements.TrackPiece))
        {
            verticalTrack = Color.Transparent;
            horizontalTrack = Color.Transparent;
        }
        verticalThumb = WithOpacity(verticalThumb, ParseScrollbarOpacity(GetScrollbarPseudoStyle(
            ScrollbarPseudoElements.Thumb,
            GetScrollbarPseudoState(activePart, hoverPart, ScrollbarPart.VerticalThumb), "opacity")));
        horizontalThumb = WithOpacity(horizontalThumb, ParseScrollbarOpacity(GetScrollbarPseudoStyle(
            ScrollbarPseudoElements.Thumb,
            GetScrollbarPseudoState(activePart, hoverPart, ScrollbarPart.HorizontalThumb), "opacity")));
        verticalTrack = WithOpacity(verticalTrack, ParseScrollbarOpacity(GetScrollbarPseudoStyle(
            ScrollbarPseudoElements.Track,
            GetScrollbarPseudoState(activePart, hoverPart, ScrollbarPart.VerticalTrack), "opacity")));
        horizontalTrack = WithOpacity(horizontalTrack, ParseScrollbarOpacity(GetScrollbarPseudoStyle(
            ScrollbarPseudoElements.Track,
            GetScrollbarPseudoState(activePart, hoverPart, ScrollbarPart.HorizontalTrack), "opacity")));
        if (buttonBackground.HasValue)
            buttonBackground = WithOpacity(buttonBackground.Value, ParseScrollbarOpacity(
                GetScrollbarPseudoStyle(ScrollbarPseudoElements.Button, "", "opacity")));
        var cornerOpacity = GetScrollbarPseudoStyle(ScrollbarPseudoElements.Corner, "", "opacity") ??
            GetScrollbarPseudoStyle(ScrollbarPseudoElements.Resizer, "", "opacity");
        if (cornerBackground.HasValue || cornerOpacity != null)
            cornerBackground = WithOpacity(cornerBackground ?? track, ParseScrollbarOpacity(cornerOpacity));
        var buttonGlyph = WithOpacity(Color.FromRgba(0, 0, 0, 110), ParseScrollbarOpacity(
            GetScrollbarPseudoStyle(ScrollbarPseudoElements.Button, "", "opacity")));
        var verticalThumbRadius = ParseScrollbarRadius(GetScrollbarPseudoStyle(
            ScrollbarPseudoElements.Thumb,
            GetScrollbarPseudoState(activePart, hoverPart, ScrollbarPart.VerticalThumb), "border-radius"));
        var horizontalThumbRadius = ParseScrollbarRadius(GetScrollbarPseudoStyle(
            ScrollbarPseudoElements.Thumb,
            GetScrollbarPseudoState(activePart, hoverPart, ScrollbarPart.HorizontalThumb), "border-radius"));
        var chromeOpacity = ScrollbarOpacity * ParseScrollbarOpacity(GetScrollbarPseudoStyle(
            ScrollbarPseudoElements.Scrollbar, "", "opacity"));
        ScrollbarPainter.Paint(
            context,
            metrics,
            thumb,
            track,
            buttonGlyph,
            chromeOpacity,
            pressedPart: activePart,
            hoverPart: hoverPart,
            pressedThumb: hasThumbStyles ? null : thumbActive,
            verticalThumbRadius: verticalThumbRadius,
            horizontalThumbRadius: horizontalThumbRadius,
            buttonBackground: buttonBackground,
            cornerBackground: cornerBackground,
            applyThumbStateColors: !hasThumbStyles,
            applyTrackStateColors: !hasTrackStyles,
            verticalThumbColor: hasThumbStyles ? verticalThumb : null,
            horizontalThumbColor: hasThumbStyles ? horizontalThumb : null,
            verticalTrackColor: hasTrackStyles ? verticalTrack : null,
            horizontalTrackColor: hasTrackStyles ? horizontalTrack : null);
    }

    private ScrollBarHit HitTestScrollBar(Point point)
    {
        if (!ShowScrollBars || ScrollbarVisibility == ScrollbarVisibilityMode.Hidden ||
            IsScrollbarCssHidden() || !IsCssDisplayedForScrollbar() || IsScrollbarPseudoDisplayNone() ||
            GetScrollbarWidthMode() == ScrollbarWidthMode.None)
            return ScrollBarHit.None;
        var font = ResolveFont();
        var lineHeight = GetLineHeight(font);
        var metrics = GetScrollMetrics(font, lineHeight);
        return metrics.HitTest(point) switch
        {
            ScrollbarPart.VerticalBackButton => ScrollBarHit.VerticalBackButton,
            ScrollbarPart.VerticalTrack => ScrollBarHit.VerticalTrack,
            ScrollbarPart.VerticalThumb => ScrollBarHit.VerticalThumb,
            ScrollbarPart.VerticalForwardButton => ScrollBarHit.VerticalForwardButton,
            ScrollbarPart.HorizontalBackButton => ScrollBarHit.HorizontalBackButton,
            ScrollbarPart.HorizontalTrack => ScrollBarHit.HorizontalTrack,
            ScrollbarPart.HorizontalThumb => ScrollBarHit.HorizontalThumb,
            ScrollbarPart.HorizontalForwardButton => ScrollBarHit.HorizontalForwardButton,
            ScrollbarPart.Corner => ScrollBarHit.Corner,
            _ => ScrollBarHit.None
        };
    }

    private static ScrollbarPart ToScrollbarPart(ScrollBarHit hit) => hit switch
    {
        ScrollBarHit.VerticalBackButton => ScrollbarPart.VerticalBackButton,
        ScrollBarHit.VerticalTrack => ScrollbarPart.VerticalTrack,
        ScrollBarHit.VerticalThumb => ScrollbarPart.VerticalThumb,
        ScrollBarHit.VerticalForwardButton => ScrollbarPart.VerticalForwardButton,
        ScrollBarHit.HorizontalBackButton => ScrollbarPart.HorizontalBackButton,
        ScrollBarHit.HorizontalTrack => ScrollbarPart.HorizontalTrack,
        ScrollBarHit.HorizontalThumb => ScrollbarPart.HorizontalThumb,
        ScrollBarHit.HorizontalForwardButton => ScrollbarPart.HorizontalForwardButton,
        ScrollBarHit.Corner => ScrollbarPart.Corner,
        _ => ScrollbarPart.None
    };

    private bool TryHandleScrollBarPointerDown(Point point)
    {
        var hit = HitTestScrollBar(point);
        if (hit == ScrollBarHit.None)
        {
            _scrollbarRepeatHit = ScrollBarHit.None;
            _scrollbarRepeatPointerInside = false;
            _scrollbarPressedPart = ScrollbarPart.None;
            return false;
        }
        _scrollbarPressedPart = ToScrollbarPart(hit);
        _scrollbarHoverPart = _scrollbarPressedPart;
        ShowEditorScrollbar();
        _scrollbarRepeatHit = hit is ScrollBarHit.VerticalThumb or ScrollBarHit.HorizontalThumb or ScrollBarHit.Corner
            ? ScrollBarHit.None
            : hit;
        _scrollbarRepeatPoint = point;
        _scrollbarRepeatPointerInside = _scrollbarRepeatHit != ScrollBarHit.None;
        if (hit == ScrollBarHit.VerticalThumb)
        {
            _draggingVScroll = true;
            _scrollDragAnchor = point.Y;
            _scrollDragOrigin = _scrollY;
        }
        else if (hit == ScrollBarHit.HorizontalThumb)
        {
            _draggingHScroll = true;
            _scrollDragAnchor = point.X;
            _scrollDragOrigin = _scrollX;
        }
        ApplyScrollbarPart(hit, point);
        InvalidatePaint();
        return true;
    }

    private void ApplyScrollbarPart(ScrollBarHit hit, Point point)
    {
        var beforeX = _scrollX;
        var beforeY = _scrollY;
        var font = ResolveFont();
        var lineHeight = GetLineHeight(font);
        var metrics = GetScrollMetrics(font, lineHeight);
        switch (hit)
        {
            case ScrollBarHit.VerticalBackButton:
                _scrollY = Math.Max(0, _scrollY - ScrollBarLineStep);
                break;
            case ScrollBarHit.VerticalForwardButton:
                _scrollY = Math.Min(metrics.MaxScrollY, _scrollY + ScrollBarLineStep);
                break;
            case ScrollBarHit.HorizontalBackButton:
                _scrollX = Math.Max(0, _scrollX - ScrollBarLineStep);
                break;
            case ScrollBarHit.HorizontalForwardButton:
                _scrollX = Math.Min(metrics.MaxScrollX, _scrollX + ScrollBarLineStep);
                break;
            case ScrollBarHit.VerticalTrack:
            {
                var page = metrics.ViewportRect.Height * 0.9f;
                _scrollY = point.Y < metrics.VerticalThumb.Y
                    ? Math.Max(0, _scrollY - page)
                    : Math.Min(metrics.MaxScrollY, _scrollY + page);
                break;
            }
            case ScrollBarHit.HorizontalTrack:
            {
                var page = metrics.ViewportRect.Width * 0.9f;
                _scrollX = point.X < metrics.HorizontalThumb.X
                    ? Math.Max(0, _scrollX - page)
                    : Math.Min(metrics.MaxScrollX, _scrollX + page);
                break;
            }
        }
        EnsureScroll(lineHeight, metrics.ViewportRect.Height, font, metrics.ViewportRect.Width, beforeX, beforeY);
        InvalidatePaint();
    }

    private void HandleScrollBarDrag(Point point)
    {
        var beforeX = _scrollX;
        var beforeY = _scrollY;
        var font = ResolveFont();
        var lineHeight = GetLineHeight(font);
        var metrics = GetScrollMetrics(font, lineHeight);

        if (_draggingVScroll && metrics.HasVertical && metrics.MaxScrollY > 0)
        {
            var travel = Math.Max(1, metrics.VerticalTrack.Height - metrics.VerticalThumb.Height);
            var delta = point.Y - _scrollDragAnchor;
            _scrollY = Math.Clamp(_scrollDragOrigin + delta / travel * metrics.MaxScrollY, 0, metrics.MaxScrollY);
        }
        else if (_draggingHScroll && metrics.HasHorizontal && metrics.MaxScrollX > 0)
        {
            var travel = Math.Max(1, metrics.HorizontalTrack.Width - metrics.HorizontalThumb.Width);
            var delta = point.X - _scrollDragAnchor;
            _scrollX = Math.Clamp(_scrollDragOrigin + delta / travel * metrics.MaxScrollX, 0, metrics.MaxScrollX);
        }

        EnsureScroll(lineHeight, metrics.ViewportRect.Height, font, metrics.ViewportRect.Width, beforeX, beforeY);
        InvalidatePaint();
    }
    private float MeasureLineNumberGutterWidth(Font font)
    {
        var digits = Math.Max(2, _model.LineCount.ToString().Length);
        var sample = new string('0', digits);
        return MeasureTextWidth(sample, font) + LineNumberPadding * 2;
    }

    private static float MeasureTextWidth(string text, Font font)
    {
        var width = 0f;
        foreach (var rune in text.EnumerateRunes())
            width += TextMetrics.GetGlyphMetrics(font, rune).AdvanceX;
        return width;
    }

    private Font ResolveFont() => FontManager.Instance.FromCss(
        Style.Get("font-family") ?? "",
        Style.Get("font-size") ?? "",
        Style.Get("font-weight") ?? "",
        Style.Get("font-style") ?? "",
        DefaultFontSize);

    private float GetLineHeight(Font font)
    {
        var value = (Style.Get("line-height") ?? "").Trim();
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(value[..^2], System.Globalization.CultureInfo.InvariantCulture, out var px) && px > 0)
            return px;
        if (float.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var mult) && mult > 0)
            return font.Size * mult;
        return TextMetrics.GetLineHeight(font, TextLayout.DefaultLineHeight);
    }

    private bool CanEdit() => IsEnabled && !ReadOnly;

    private void ClampSelection()
    {
        _caretIndex = Math.Clamp(_caretIndex, 0, _model.Length);
        _selectionAnchor = Math.Clamp(_selectionAnchor, 0, _model.Length);
        for (var i = 0; i < _extraCursors.Count; i++)
            _extraCursors[i] = _extraCursors[i].Clamp(_model.Length);
        DeduplicateCursors();
    }

    private void DeduplicateCursors()
    {
        if (_extraCursors.Count == 0) return;
        var seen = new HashSet<long>();
        // 主光标位置占用
        seen.Add(((long)_caretIndex << 32) | (uint)_selectionAnchor);
        for (var i = _extraCursors.Count - 1; i >= 0; i--)
        {
            var c = _extraCursors[i].Clamp(_model.Length);
            var key = ((long)c.Caret << 32) | (uint)c.Anchor;
            if (!seen.Add(key) || (c.IsCollapsed && c.Caret == _caretIndex && _selectionAnchor == _caretIndex))
                _extraCursors.RemoveAt(i);
            else
                _extraCursors[i] = c;
        }
    }

    private void PaintCarets(
        IRenderContext context,
        CodeEditorTheme theme,
        Font font,
        float lineHeight,
        float padding,
        float leftGutter)
    {
        if (!IsFocused || _caretOpacity <= 0.01f) return;
        var c = theme.EditorCursorForeground;
        var alpha = (byte)Math.Clamp(_caretOpacity * c.A, 0f, 255f);
        var brush = new SolidColorBrush(Color.FromRgba(c.R, c.G, c.B, alpha));
        if (SelectionLength == 0)
        {
            var caret = ComputeCaretRect(font, lineHeight, padding, leftGutter);
            context.FillRect(caret, brush);
        }
        foreach (var extra in _extraCursors)
        {
            if (!extra.IsCollapsed) continue;
            var rect = ComputeCaretRectAt(extra.Caret, font, lineHeight, padding, leftGutter);
            context.FillRect(rect, brush);
        }
    }

    private void PaintExtraCursorSelectionsForRow(
        IRenderContext context,
        CodeEditorTheme theme,
        Font font,
        CodeEditorViewRow row,
        float contentLeft,
        float y,
        float lineHeight)
    {
        if (_extraCursors.Count == 0) return;
        foreach (var extra in _extraCursors)
        {
            if (extra.IsCollapsed) continue;
            PaintRangeHighlightOnRow(
                context,
                theme.EditorSelectionBackground,
                null,
                font,
                row,
                contentLeft,
                y,
                lineHeight,
                extra.SelectionStart,
                extra.SelectionStart + extra.SelectionLength,
                expandNewline: true);
        }
    }

    private Rect ComputeCaretRectAt(int offset, Font font, float lineHeight, float padding, float gutter)
    {
        EnsureFolds();
        var metrics = GetScrollMetrics(font, lineHeight);
        var contentWidth = metrics.ViewportRect.Width;
        EnsurePaintConsistentViewLayout(font, contentWidth);
        offset = Math.Clamp(offset, 0, _model.Length);
        var (line, col) = _model.GetPositionAt(offset);
        if (_folding.IsLineHidden(line))
        {
            line = _folding.VisualToDocument(_folding.DocumentToVisual(line));
            col = Math.Min(col, _model.GetLineContent(line).Length);
        }
        var rowIndex = _viewLayout.OffsetToRow(_model, _model.GetOffsetAt(line, col));
        var row = _viewLayout[rowIndex];
        var content = _model.GetLineContent(row.DocumentLine);
        var localCol = Math.Clamp(col - row.Start, 0, Math.Max(0, row.End - row.Start));
        var rowText = content.Length == 0 ? "" : content[row.Start..Math.Min(row.End, content.Length)];
        var x = metrics.ViewportRect.Left - (WordWrap ? 0f : _scrollX)
                + CodeEditorMetrics.XAtColumn(rowText, font, TabSize, localCol);
        var y = metrics.ViewportRect.Top + rowIndex * lineHeight - _scrollY;
        return new Rect(MathF.Round(x), MathF.Round(y), 1, Math.Max(1, lineHeight));
    }

    /// <summary>
    /// 对所有光标（主 + 附加）应用同一编辑。
    /// 多点编辑合并为一次 undo；光标位置按文档 delta 正确重映射。
    /// </summary>
    private void ApplyEditToAllCursors(string insert, bool isBackspace, bool isDelete)
    {
        if (!CanEdit()) return;

        var cursors = new List<CodeEditorCursor>(1 + _extraCursors.Count) { new(_caretIndex, _selectionAnchor) };
        cursors.AddRange(_extraCursors);
        cursors.Sort((a, b) => a.SelectionStart.CompareTo(b.SelectionStart));

        var edits = new List<TextEdit>();
        var preCaretsForHistory = new List<int>();
        var postLocalCarets = new List<int>();
        foreach (var cursor in cursors)
        {
            var preCaret = cursor.Caret;
            var start = cursor.SelectionStart;
            var end = start + cursor.SelectionLength;
            if (cursor.IsCollapsed)
            {
                if (isBackspace)
                {
                    if (start == 0)
                    {
                        postLocalCarets.Add(0);
                        continue;
                    }
                    start = PreviousIndex(start);
                    end = cursor.Caret;
                }
                else if (isDelete)
                {
                    if (end >= _model.Length)
                    {
                        postLocalCarets.Add(end);
                        continue;
                    }
                    start = cursor.Caret;
                    end = NextIndex(end);
                }
            }

            var length = Math.Max(0, end - start);
            if (length > 0 || insert.Length > 0)
            {
                edits.Add(new TextEdit(start, length, insert));
                preCaretsForHistory.Add(preCaret);
            }
            postLocalCarets.Add(start + insert.Length);
        }

        if (edits.Count > 0)
            _model.ReplaceMany(edits, preCaretsForHistory);

        var finalCarets = new List<int>(postLocalCarets.Count);
        foreach (var pre in postLocalCarets)
        {
            var mapped = pre;
            foreach (var e in edits)
            {
                if (e.Offset < pre)
                    mapped += e.Text.Length - e.Length;
            }
            finalCarets.Add(Math.Clamp(mapped, 0, _model.Length));
        }

        finalCarets.Sort();
        for (var i = finalCarets.Count - 1; i > 0; i--)
            if (finalCarets[i] == finalCarets[i - 1]) finalCarets.RemoveAt(i);

        if (finalCarets.Count == 0)
        {
            _extraCursors.Clear();
            AfterEdit();
            return;
        }

        _caretIndex = _selectionAnchor = finalCarets[^1];
        _extraCursors.Clear();
        for (var i = 0; i < finalCarets.Count - 1; i++)
            _extraCursors.Add(CodeEditorCursor.Collapsed(finalCarets[i]));
        AfterEdit();
        NotifySelectionChanged();
    }

    private static void DeduplicateCursorList(List<CodeEditorCursor> list)
    {
        if (list.Count < 2) return;
        // 按选区起点排序后合并完全相同或高度重叠的光标
        for (var i = list.Count - 1; i > 0; i--)
        {
            var a = list[i];
            var b = list[i - 1];
            if (a.Caret == b.Caret && a.Anchor == b.Anchor)
            {
                list.RemoveAt(i);
                continue;
            }
            // 同 caret 不同 anchor：保留选区更长的
            if (a.Caret == b.Caret)
            {
                list[i - 1] = a.SelectionLength >= b.SelectionLength ? a : b;
                list.RemoveAt(i);
            }
        }
    }

    private void MoveAllCursorsHorizontal(int direction, bool byWord, bool extend)
    {
        var all = new List<CodeEditorCursor>(1 + _extraCursors.Count) { new(_caretIndex, _selectionAnchor) };
        all.AddRange(_extraCursors);
        for (var i = 0; i < all.Count; i++)
            all[i] = MoveCursorHorizontal(all[i], direction, byWord, extend);
        ApplyCursorList(all);
    }

    private CodeEditorCursor MoveCursorHorizontal(CodeEditorCursor c, int direction, bool byWord, bool extend)
    {
        // 无 Shift：有选区时折叠到选区边界（与单光标行为一致）
        if (!extend && !c.IsCollapsed)
        {
            var edge = direction < 0 ? c.SelectionStart : c.SelectionStart + c.SelectionLength;
            return CodeEditorCursor.Collapsed(edge);
        }

        var from = c.Caret;
        var next = direction < 0
            ? (byWord ? PreviousWord(from) : PreviousIndex(from))
            : (byWord ? NextWord(from) : NextIndex(from));
        var anchor = extend ? c.Anchor : next;
        return new CodeEditorCursor(next, anchor).Clamp(_model.Length);
    }

    private void MoveAllCursorsVertical(int direction, bool extend)
    {
        EnsureFolds();
        var font = ResolveFont();
        var contentWidth = GetContentWidth();
        EnsurePaintConsistentViewLayout(font, contentWidth);
        var all = new List<CodeEditorCursor>(1 + _extraCursors.Count) { new(_caretIndex, _selectionAnchor) };
        all.AddRange(_extraCursors);
        for (var i = 0; i < all.Count; i++)
            all[i] = MoveCursorVertical(all[i], direction, extend, font);
        ApplyCursorList(all);
    }

    private CodeEditorCursor MoveCursorVertical(CodeEditorCursor c, int direction, bool extend, Font font)
    {
        if (!extend && !c.IsCollapsed)
        {
            var edge = direction < 0 ? c.SelectionStart : c.SelectionStart + c.SelectionLength;
            return CodeEditorCursor.Collapsed(edge);
        }

        var rowIndex = _viewLayout.OffsetToRow(_model, c.Caret);
        var (line, col) = _model.GetPositionAt(c.Caret);
        var content = _model.GetLineContent(line);
        var row = _viewLayout[rowIndex];
        var localCol = Math.Clamp(col - row.Start, 0, Math.Max(0, row.End - row.Start));
        var rowText = content.Length == 0 ? "" : content[row.Start..Math.Min(row.End, content.Length)];
        var preferredX = CodeEditorMetrics.XAtColumn(rowText, font, TabSize, localCol);
        var targetRowIndex = Math.Clamp(rowIndex + direction, 0, _viewLayout.RowCount - 1);
        int next;
        if (targetRowIndex == rowIndex)
        {
            next = direction < 0 ? 0 : _model.Length;
        }
        else
        {
            var targetRow = _viewLayout[targetRowIndex];
            var targetContent = _model.GetLineContent(targetRow.DocumentLine);
            var targetSeg = targetContent.Length == 0
                ? ""
                : targetContent[targetRow.Start..Math.Min(targetRow.End, targetContent.Length)];
            var targetLocal = CodeEditorMetrics.ColumnAtX(targetSeg, font, TabSize, preferredX);
            next = _model.GetOffsetAt(targetRow.DocumentLine, targetRow.Start + targetLocal);
        }
        var anchor = extend ? c.Anchor : next;
        return new CodeEditorCursor(next, anchor).Clamp(_model.Length);
    }

    private void MoveAllCursorsToLineBoundary(bool toStart, bool control, bool extend)
    {
        EnsureFolds();
        var font = ResolveFont();
        EnsurePaintConsistentViewLayout(font, GetContentWidth());
        var all = new List<CodeEditorCursor>(1 + _extraCursors.Count) { new(_caretIndex, _selectionAnchor) };
        all.AddRange(_extraCursors);
        for (var i = 0; i < all.Count; i++)
        {
            var c = all[i];
            if (!extend && !c.IsCollapsed)
            {
                var edge = toStart ? c.SelectionStart : c.SelectionStart + c.SelectionLength;
                all[i] = CodeEditorCursor.Collapsed(edge);
                continue;
            }

            int next;
            if (control)
            {
                next = toStart ? 0 : _model.Length;
            }
            else
            {
                var rowIndex = _viewLayout.OffsetToRow(_model, c.Caret);
                var row = _viewLayout[rowIndex];
                next = toStart
                    ? _model.GetOffsetAt(row.DocumentLine, row.Start)
                    : _model.GetOffsetAt(row.DocumentLine, row.End);
            }
            var anchor = extend ? c.Anchor : next;
            all[i] = new CodeEditorCursor(next, anchor).Clamp(_model.Length);
        }
        ApplyCursorList(all);
    }

    private void ApplyCursorList(List<CodeEditorCursor> all)
    {
        if (all.Count == 0) return;
        // 按 caret 排序；相同 caret 时合并选区（取并集）
        all.Sort((a, b) =>
        {
            var cmp = a.Caret.CompareTo(b.Caret);
            return cmp != 0 ? cmp : a.Anchor.CompareTo(b.Anchor);
        });
        DeduplicateCursorList(all);
        var primary = all[^1];
        _caretIndex = primary.Caret;
        _selectionAnchor = primary.Anchor;
        _extraCursors.Clear();
        for (var i = 0; i < all.Count - 1; i++)
            _extraCursors.Add(all[i]);
        EnsureCaretVisible();
        ResetCaretBlink(fullRepaint: true);
        NotifySelectionChanged();
    }

    private string GetLineIndent(int line)
    {
        var content = _model.GetLineContent(line);
        var i = 0;
        while (i < content.Length && (content[i] == ' ' || content[i] == '\t')) i++;
        return content[..i];
    }

    private int PreviousIndex(int index)
    {
        if (index <= 0) return 0;
        index--;
        if (index > 0 && char.IsLowSurrogate(_model.GetValue()[index]) && char.IsHighSurrogate(_model.GetValue()[index - 1]))
            index--;
        return index;
    }

    private int NextIndex(int index)
    {
        if (index >= _model.Length) return _model.Length;
        var text = _model.GetValue();
        return index + (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1);
    }

    private int PreviousWord(int index)
    {
        var text = _model.GetValue();
        while (index > 0 && char.IsWhiteSpace(text[index - 1])) index--;
        while (index > 0 && IsWordChar(text[index - 1])) index--;
        return index;
    }

    private int NextWord(int index)
    {
        var text = _model.GetValue();
        while (index < text.Length && IsWordChar(text[index])) index++;
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        return index;
    }

    private (int Start, int End) WordAt(int index)
    {
        var text = _model.GetValue();
        if (text.Length == 0) return (0, 0);
        index = Math.Clamp(index, 0, text.Length - 1);
        var config = LanguageRegistry.ResolveConfiguration(Language);
        if (!string.IsNullOrEmpty(config.WordPattern))
        {
            try
            {
                var regex = new Regex(config.WordPattern, RegexOptions.CultureInvariant);
                foreach (Match m in regex.Matches(text))
                {
                    if (index >= m.Index && index < m.Index + m.Length)
                        return (m.Index, m.Index + m.Length);
                }
            }
            catch
            {
                // fall through
            }
        }
        if (!IsWordChar(text[index])) return (index, index + 1);
        var start = index;
        var end = index + 1;
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        while (end < text.Length && IsWordChar(text[end])) end++;
        return (start, end);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
