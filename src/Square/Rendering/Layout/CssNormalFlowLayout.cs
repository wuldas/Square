using System.Text;
using Square.Controls;
using Square.Graphics;
using Square.UI;
using Square.Html;
using ControlText = Square.Controls.Text;

namespace Square.Rendering;

public sealed partial class LayoutEngine
{
    private static bool UsesCssNormalFlow(Element element)
    {
        var display = CssKeyword(element, "display");
        if (display is "block" or "inline" or "inline-block" or "list-item") return true;
        if (display is "flex" or "grid" or "none") return false;
        // DOM Text only participates in the CSS line layout; a text-bearing HTML host must not
        // fall back to the Yoga path, which cannot see text nodes.
        if (element is HTMLElement { HasDirectTextContent: true, AcceptsDirectTextContent: true }) return true;
        if (CssKeyword(element, "float") is "left" or "right") return true;
        if (CssKeyword(element, "clear") is "left" or "right" or "both") return true;

        foreach (var child in element.Children)
            if (UsesCssNormalFlow(child)) return true;
        // HTML sidecars (list marker, native proxies) keep their host in normal flow,
        // exactly like when they were still laid out as children.
        if (element is HTMLElement host)
            foreach (var sidecar in host.VisualSidecars)
                if (UsesCssNormalFlow(sidecar)) return true;
        return false;
    }

    private void MeasureCssNormalFlow(Element element, Size availableSize)
    {
        var width = ResolveRootWidth(element, availableSize.Width);
        var plan = BuildCssPlan(element, new Rect(0, 0, width, float.NaN));
        ElementLayoutStore.Get(element).CssDesiredSize = plan.DesiredSize;
    }

    private void ArrangeCssNormalFlow(Element element, Rect finalRect)
    {
        ClearFixedRootFlags(element);
        var plan = BuildCssPlan(element, finalRect);
        ElementLayoutStore.Get(element).CssDesiredSize = plan.DesiredSize;

        foreach (var entry in plan.Entries)
        {
            if (entry.Element is ControlText text)
                ElementLayoutStore.Get(text).CssTextFragments = plan.TextFragments.TryGetValue(text, out var fragments)
                    ? fragments
                    : null;
            entry.Element.Arrange(entry.Bounds);
        }

        // DOM Text fragments are keyed by the source node; the arrange pass replaces the
        // previous geometry so cleared/edited text never leaves stale fragments behind.
        foreach (var pair in plan.HtmlTextFragments)
        {
            var data = HtmlTextLayoutStore.Get(pair.Key);
            data.Fragments.Clear();
            data.Fragments.AddRange(pair.Value);
        }

        foreach (var external in plan.ExternalLayouts)
            ArrangeCore(external.Element, external.Bounds);

        foreach (var fixedRoot in plan.FixedRoots)
            ElementLayoutStore.Get(fixedRoot).IsFixedRoot = true;

        foreach (var entry in plan.Entries)
        {
            if (ReferenceEquals(entry.Element, element) || entry.Element.IsScrollContainer())
                UpdateCssScrollContentSize(entry.Element, entry.Bounds);
        }
    }

    private CssLayoutPlan BuildCssPlan(Element root, Rect requestedBounds)
    {
        var plan = new CssLayoutPlan();
        if (CssKeyword(root, "position") == "fixed") plan.FixedRoots.Add(root);
        var rootStyle = ResolveCssBox(root, requestedBounds.Width, requestedBounds.Height);
        var rootWidth = float.IsFinite(requestedBounds.Width)
            ? Math.Max(0, requestedBounds.Width)
            : ResolveOuterWidth(root, rootStyle, float.MaxValue, out _, out _);
        if (!float.IsFinite(rootWidth)) rootWidth = 0;

        var rootBounds = new Rect(requestedBounds.X, requestedBounds.Y, rootWidth,
            float.IsFinite(requestedBounds.Height) ? Math.Max(0, requestedBounds.Height) : 0);
        var containingBlock = EstablishesContainingBlock(root)
            ? new CssContainingBlock(PaddingBox(rootBounds, rootStyle))
            : new CssContainingBlock(new Rect(0, 0, Math.Max(0, _viewportWidth), Math.Max(0, _viewportHeight)));
        var contentHeight = LayoutContainerContents(root, rootBounds, rootStyle, containingBlock, plan);
        var naturalHeight = contentHeight + rootStyle.PaddingTop + rootStyle.PaddingBottom + rootStyle.BorderTop + rootStyle.BorderBottom;
        var rootHeight = float.IsFinite(requestedBounds.Height)
            ? Math.Max(0, requestedBounds.Height)
            : ResolveOuterHeight(rootStyle, naturalHeight);
        rootBounds = new Rect(rootBounds.X, rootBounds.Y, rootBounds.Width, rootHeight);
        plan.Set(root, rootBounds);
        plan.DesiredSize = new Size(rootBounds.Width, rootBounds.Height);
        return plan;
    }

    private float LayoutContainerContents(Element container, Rect borderBounds, CssBox box,
        CssContainingBlock containingBlock, CssLayoutPlan plan)
    {
        var scrollbarInsets = container.GetReservedScrollbarInsets();
        var content = new Rect(
            borderBounds.X + box.BorderLeft + box.PaddingLeft + scrollbarInsets.Left,
            borderBounds.Y + box.BorderTop + box.PaddingTop + scrollbarInsets.Top,
            Math.Max(0, borderBounds.Width - box.BorderLeft - box.BorderRight - box.PaddingLeft - box.PaddingRight - scrollbarInsets.Left - scrollbarInsets.Right),
            borderBounds.Height > 0 && float.IsFinite(borderBounds.Height)
                ? Math.Max(0, borderBounds.Height - box.BorderTop - box.BorderBottom - box.PaddingTop - box.PaddingBottom - scrollbarInsets.Top - scrollbarInsets.Bottom)
                : float.MaxValue);
        var floats = new List<CssFloatArea>();
        var absolute = new List<Element>();
        var fixedPositioned = new List<Element>();
        var inline = new List<CssFlowItem>();
        var y = content.Y;
        var previousBottomMargin = 0f;
        var hasBlock = false;

        // The li list marker is a visual sidecar (never a DOM child); the line layout still needs
        // it as the first inline piece so it consumes width and text flows after it.
        HTMLElement? sidecarHost = container as HTMLElement;
        if (sidecarHost != null && sidecarHost.TryGetListMarker(out var listMarker))
        {
            inline.Add(new CssFlowItem(listMarker));
            sidecarHost.MarkListMarkerLaidByLine();
        }

        void FlushInline()
        {
            if (inline.Count == 0) return;
            // A whitespace-only run of DOM text between blocks must not open a line box nor
            // break margin collapsing (Normal/Nowrap only; preserved modes keep their boxes).
            if (inline.All(item => item.TextNode is { } text && text.Data.AsSpan().IsWhiteSpace()) &&
                CssKeyword(container, "white-space") is not ("pre" or "pre-wrap" or "pre-line"))
                return;
            y = LayoutInlineGroup(container, inline, content, y, floats, containingBlock, plan);
            inline.Clear();
            previousBottomMargin = 0;
            hasBlock = true;
        }

        // HTML containers walk ChildNodes so DOM Text participates in original order;
        // Square containers only ever hold element children.
        foreach (var node in container.ChildNodes)
        {
            if (node is not Element child)
            {
                if (node is Square.UI.Text text &&
                    container is HTMLElement { AcceptsDirectTextContent: true })
                    inline.Add(new CssFlowItem(container, text));
                continue;
            }
            if (!child.IsVisible || CssKeyword(child, "display") == "none") continue;
            var position = CssKeyword(child, "position");
            if (position == "fixed")
            {
                plan.FixedRoots.Add(child);
                fixedPositioned.Add(child);
                continue;
            }
            if (position == "absolute")
            {
                absolute.Add(child);
                continue;
            }

            var childBox = ResolveCssBox(child, content.Width, float.NaN);
            var floatSide = CssKeyword(child, "float");
            if (floatSide is "left" or "right")
            {
                FlushInline();
                LayoutFloat(child, childBox, floatSide, content, y, floats, containingBlock, plan);
                continue;
            }
            if (CssKeyword(child, "display") is "inline" or "inline-block" or "inline-table" ||
                container is HTMLElement && child is Square.UI.Svg.SVGSVGElement)
            {
                inline.Add(new CssFlowItem(child));
                continue;
            }

            FlushInline();
            var beforeClear = y;
            y = ApplyClear(child, y, floats);
            var hasClearance = y > beforeClear;
            var collapsed = hasBlock && !hasClearance
                ? CollapseMargins(previousBottomMargin, childBox.MarginTop)
                : childBox.MarginTop;
            y += collapsed;
            var normalY = y;
            var outerWidth = ResolveOuterWidth(child, childBox, content.Width, out var marginLeft, out _);
            var childX = content.X + marginLeft;
            var shifted = ApplyRelativeOffset(child, childX, normalY, content.Width, float.NaN);
            var childHeight = LayoutBlock(child, childBox, new Rect(shifted.X, shifted.Y, outerWidth, float.NaN), containingBlock, plan);
            y = normalY + childHeight;
            previousBottomMargin = childBox.MarginBottom;
            hasBlock = true;
        }

        FlushInline();
        if (hasBlock && !CanCollapseThrough(box)) y += previousBottomMargin;

        // Control sidecars (input/select/img proxies) position at the host content box via
        // HTMLElement.Arrange; here they only contribute their flow height, e.g. when the host
        // itself is the normal-flow root inside a table cell or grid lane.
        if (sidecarHost != null)
            foreach (var sidecar in sidecarHost.VisualSidecars)
            {
                if (sidecarHost.IsListMarker(sidecar) ||
                    !sidecar.IsVisible || CssKeyword(sidecar, "display") == "none") continue;
                var sidecarBox = ResolveCssBox(sidecar, content.Width, float.NaN);
                var sidecarOuterWidth = ResolveOuterWidth(sidecar, sidecarBox, content.Width, out _, out _);
                var sidecarHeight = ResolveAtomicOuterHeight(sidecar, sidecarBox, sidecarOuterWidth, float.MaxValue);
                y += sidecarBox.MarginTop + sidecarHeight + sidecarBox.MarginBottom;
            }

        foreach (var child in absolute)
            LayoutAbsolute(child, containingBlock.Rect, plan);
        var viewport = new Rect(0, 0, Math.Max(0, _viewportWidth), Math.Max(0, _viewportHeight));
        foreach (var child in fixedPositioned)
            LayoutAbsolute(child, viewport, plan);

        var floatBottom = floats.Count == 0 ? content.Y : floats.Max(area => area.Bottom);
        return Math.Max(y, floatBottom) - content.Y;
    }

    private static void ClearFixedRootFlags(Element element)
    {
        if (ElementLayoutStore.TryGet(element, out var data)) data.IsFixedRoot = false;
        foreach (var child in element.Children)
            ClearFixedRootFlags(child);
    }

    private static void UpdateCssScrollContentSize(Element element, Rect rect)
    {
        if (!element.IsScrollContainer())
        {
            element.SetScrollContentSize(rect.Size);
            return;
        }

        var viewport = element.GetScrollViewportRect();
        var right = viewport.Width;
        var bottom = viewport.Height;
        foreach (var child in element.Children)
        {
            if (!child.IsVisible ||
                ElementLayoutStore.TryGet(child, out var data) && data.IsFixedRoot) continue;
            right = Math.Max(right, child.Geometry.Right - viewport.X);
            bottom = Math.Max(bottom, child.Geometry.Bottom - viewport.Y);
        }

        element.SetScrollContentSize(new Size(right, bottom));
    }

    private float LayoutBlock(Element element, CssBox box, Rect proposed,
        CssContainingBlock containingBlock, CssLayoutPlan plan)
    {
        var display = CssKeyword(element, "display");
        if (IsTableRoot(ParseDisplayMode(display)))
        {
            var measured = new TableLayoutEngine(this).Measure(element,
                new Size(Math.Max(0, proposed.Width), float.PositiveInfinity));
            var width = float.IsNaN(box.Width) ? Math.Max(proposed.Width, measured.Width) : proposed.Width;
            var bounds = new Rect(proposed.X, proposed.Y, width, measured.Height);
            plan.ExternalLayouts.Add(new CssLayoutEntry(element, bounds));
            return bounds.Height;
        }

        if (display is "flex" or "grid")
        {
            var atomicHeight = ResolveAtomicOuterHeight(element, box, proposed.Width, float.MaxValue);
            var bounds = new Rect(proposed.X, proposed.Y, proposed.Width, atomicHeight);
            plan.ExternalLayouts.Add(new CssLayoutEntry(element, bounds));
            return atomicHeight;
        }

        var borderBounds = new Rect(proposed.X, proposed.Y, proposed.Width,
            float.IsNaN(box.Height) ? 0 : BorderHeightFromSpecified(box));
        float naturalHeight;
        var childContainingBlock = EstablishesContainingBlock(element)
            ? new CssContainingBlock(PaddingBox(borderBounds, box))
            : containingBlock;
        if (element is HTMLElement && element.TagName is "img" or "input" or "textarea" or "select" or "video")
        {
            // The host is atomic; its control sidecars position at the content box when the
            // entry below arranges the host (HTMLElement.Arrange).
            var proxyHeight = float.IsFinite(proposed.Height)
                ? proposed.Height
                : ResolveAtomicOuterHeight(element, box, proposed.Width, float.MaxValue);
            var bounds = new Rect(proposed.X, proposed.Y, proposed.Width, proxyHeight);
            plan.Set(element, bounds);
            return proxyHeight;
        }

        if (element.Children.Count > 0 || HasFlowContent(element))
        {
            var contentHeight = LayoutContainerContents(element, borderBounds, box, childContainingBlock, plan);
            naturalHeight = contentHeight + box.PaddingTop + box.PaddingBottom + box.BorderTop + box.BorderBottom;
        }
        else
        {
            naturalHeight = ResolveAtomicOuterHeight(element, box, proposed.Width, float.MaxValue);
        }

        var height = ResolveOuterHeight(box, naturalHeight);
        borderBounds = new Rect(proposed.X, proposed.Y, proposed.Width, height);
        plan.Set(element, borderBounds);
        return height;
    }

    private float LayoutInlineGroup(Element container, List<CssFlowItem> items, Rect content, float startY,
        List<CssFloatArea> floats, CssContainingBlock containingBlock, CssLayoutPlan plan)
    {
        var pieces = new List<CssInlinePiece>();
        foreach (var item in items)
        {
            if (item.TextNode is { } textNode)
            {
                plan.HtmlTextFragments.TryAdd(textNode, []);
                AddTextPieces(container, textNode.Data, CssBox.Empty, content.Width, pieces, null, null, textNode);
                continue;
            }
            var element = item.Element;
            var box = ResolveCssBox(element, content.Width, float.NaN);
            if (element is HTMLElement { TagName: "br" })
            {
                pieces.Add(CssInlinePiece.LineBreak(element));
                continue;
            }
            if (element is HTMLElement { TagName: "wbr" }) continue;
            if (element is ControlText text)
            {
                plan.TextFragments.TryAdd(text, []);
                AddTextPieces(text, text.TextContent, box, content.Width, pieces, null, text, null);
            }
            else if (element is HTMLElement && CssKeyword(element, "display") == "inline")
                CollectHtmlInlinePieces(element, content.Width, [element], pieces, plan);
            else
                pieces.Add(CreateAtomicInlinePiece(element, box, content.Width));
        }

        var y = startY;
        var index = 0;
        var lineIndex = 0;
        while (index < pieces.Count)
        {
            var lineHeightHint = pieces[index].Height;
            var (left, right) = GetLineBounds(content, y, lineHeightHint, floats);
            if (right <= left)
            {
                y = NextFloatBottom(y, floats);
                continue;
            }

            var line = new List<CssInlinePiece>();
            var used = 0f;
            while (index < pieces.Count)
            {
                var piece = pieces[index];
                if (piece.ForceBreak)
                {
                    index++;
                    break;
                }
                if (piece.IsCollapsibleSpace && line.Count == 0)
                {
                    index++;
                    continue;
                }
                if (line.Count > 0 && !piece.AllowWrap && used + piece.Width > right - left)
                    break;
                if (line.Count > 0 && used + piece.Width > right - left)
                    break;
                if (line.Count == 0 && piece.Width > right - left && piece.Text != null && piece.AllowWrap)
                {
                    var split = SplitTextPiece(piece, right - left);
                    piece = split.Head;
                    if (string.IsNullOrEmpty(split.Tail.Text)) index++;
                    else pieces[index] = split.Tail;
                }
                else
                {
                    index++;
                }
                line.Add(piece);
                used += piece.Width;
            }

            if (line.Count == 0) continue;
            while (line.Count > 0 && line[^1].IsCollapsibleSpace)
            {
                used -= line[^1].Width;
                line.RemoveAt(line.Count - 1);
            }
            if (line.Count == 0) continue;

            var baseline = line.Max(piece => piece.Baseline);
            var descent = line.Max(piece => piece.Height - piece.Baseline);
            var lineHeight = baseline + descent;
            var indent = lineIndex == 0
                ? ControlDrawing.ResolveTextLength(container, "text-indent", GetFontSize(container))
                : 0;
            var alignOffset = ResolveTextAlign(container, Math.Max(0, right - left - indent), used);
            var x = left + indent + alignOffset;
            foreach (var piece in line)
            {
                var top = y + baseline - piece.Baseline;
                var bounds = new Rect(x + piece.MarginLeft, top + piece.MarginTop,
                    Math.Max(0, piece.Width - piece.MarginLeft - piece.MarginRight),
                    Math.Max(0, piece.Height - piece.MarginTop - piece.MarginBottom));
                if (piece.SourceNode is { } sourceNode)
                {
                    plan.AddHtmlTextFragment(sourceNode, new HtmlTextFragment(
                        piece.Text!, bounds, piece.SourceOffset, piece.SourceCharOffsets, piece.SourceLength,
                        ControlDrawing.ResolveTextDirection(piece.Element),
                        ControlDrawing.ResolveUnicodeBidi(piece.Element)));
                }
                else if (piece.Text != null && piece.Element is ControlText text)
                {
                    plan.AddTextFragment(text, new TextLayoutFragment(
                        piece.Text,
                        bounds,
                        ControlDrawing.ResolveTextDirection(text),
                        ControlDrawing.ResolveUnicodeBidi(text)));
                    plan.Union(text, bounds);
                }
                else
                {
                    var shifted = ApplyRelativeOffset(piece.Element, bounds.X, bounds.Y, content.Width, lineHeight);
                    bounds = new Rect(shifted.X, shifted.Y, bounds.Width, bounds.Height);
                    var display = CssKeyword(piece.Element, "display");
                    if (display == "inline-block")
                        LayoutBlock(piece.Element, ResolveCssBox(piece.Element, content.Width, float.NaN), bounds,
                            containingBlock, plan);
                    else
                        plan.Set(piece.Element, bounds);
                    if (display == "inline-table")
                        plan.ExternalLayouts.Add(new CssLayoutEntry(piece.Element, bounds));
                }
                if (piece.InlineAncestors != null)
                    foreach (var ancestor in piece.InlineAncestors)
                        plan.Union(ancestor, bounds);
                x += piece.Width;
            }
            y += lineHeight;
            lineIndex++;
        }
        return y;
    }

    /// <summary>
    /// Flattens HTML display:inline descendants into ordered text pieces and atomic inline boxes.
    /// DOM Text nodes interleave in original order; Square (non-HTML) inline elements stay
    /// atomic, preserving their existing behavior.
    /// </summary>
    private void CollectHtmlInlinePieces(Element container, float availableWidth, List<Element>? ancestors,
        List<CssInlinePiece> pieces, CssLayoutPlan plan)
    {
        foreach (var node in container.ChildNodes)
        {
            if (node is not Element child)
            {
                if (node is Square.UI.Text textNode &&
                    container is HTMLElement { AcceptsDirectTextContent: true })
                {
                    plan.HtmlTextFragments.TryAdd(textNode, []);
                    AddTextPieces(container, textNode.Data, CssBox.Empty, availableWidth, pieces, ancestors, null, textNode);
                }
                continue;
            }
            if (!child.IsVisible || CssKeyword(child, "display") == "none") continue;
            if (child is ControlText text)
            {
                plan.TextFragments.TryAdd(text, []);
                AddTextPieces(text, text.TextContent, ResolveCssBox(text, availableWidth, float.NaN), availableWidth,
                    pieces, ancestors, text, null);
                continue;
            }
            if (child is HTMLElement)
            {
                // <br> forces a line break; <wbr> contributes no box — the piece boundary it creates
                // between adjacent runs is already a wrapping opportunity in the line breaker.
                if (child.TagName == "br")
                {
                    pieces.Add(CssInlinePiece.LineBreak(child, ancestors));
                    continue;
                }
                if (child.TagName == "wbr") continue;
                if (CssKeyword(child, "display") == "inline")
                {
                    List<Element> chain = ancestors == null ? [child] : [.. ancestors, child];
                    CollectHtmlInlinePieces(child, availableWidth, chain, pieces, plan);
                    continue;
                }
            }
            var box = ResolveCssBox(child, availableWidth, float.NaN);
            var piece = CreateAtomicInlinePiece(child, box, availableWidth);
            pieces.Add(ancestors == null ? piece : piece with { InlineAncestors = ancestors });
        }
    }

    /// <summary>
    /// Splits one text run into inline pieces. For DOM Text sources every produced character
    /// records its UTF-16 offset in the node's <c>data</c>, so whitespace collapse, text
    /// transforms and later wrap splits keep original positions addressable.
    /// </summary>
    private void AddTextPieces(Element styleSource, string rawData, CssBox box, float availableWidth,
        List<CssInlinePiece> pieces, List<Element>? ancestors, ControlText? controlText, Square.UI.Text? sourceNode)
    {
        var (value, valueToData) = NormalizeLineBreaks(rawData);
        var pieceElement = controlText ?? (Element)styleSource;
        var defaultSize = controlText != null ? controlText.FontSize : HTMLElement.HtmlTextDefaultFontSize;
        var font = ControlDrawing.ResolveFont(styleSource, defaultSize);
        var lineHeight = ControlDrawing.GetStyledLineHeight(styleSource, font.Size);
        var baseline = TextMetrics.GetBaselineOffset(font, lineHeight);
        var whiteSpace = ControlDrawing.ResolveWhiteSpace(styleSource);
        var letterSpacing = ControlDrawing.ResolveTextLength(styleSource, "letter-spacing", font.Size);
        var wordSpacing = ControlDrawing.ResolveTextLength(styleSource, "word-spacing", font.Size);
        var textTransform = ControlDrawing.ResolveTextTransform(styleSource);

        // Per produced character, the UTF-16 offset of the source rune that yielded it.
        // Kept in lockstep with the pending text builder; identity runs emit no array.
        var pieceSources = new List<int>();
        var valueIndex = 0;
        var lastConsumedEnd = 0;

        int DataIndexOf(int index) => valueToData == null
            ? index
            : index < valueToData.Length ? valueToData[index] : rawData.Length;

        void AppendTo(StringBuilder builder, int sourceIndex, string transformed)
        {
            for (var i = 0; i < transformed.Length; i++) pieceSources.Add(sourceIndex);
            builder.Append(transformed);
        }

        void StartCollapsedSpace(int sourceIndex)
        {
            pieceSources.Clear();
            pieceSources.Add(sourceIndex);
        }

        CssInlinePiece MakePiece(string token, float tokenWidth, bool collapsible, bool preserve, bool allowWrap,
            int sourceEnd)
        {
            int[]? charOffsets = null;
            var sourceOffset = 0;
            var sourceLength = 0;
            if (sourceNode != null && pieceSources.Count > 0)
            {
                sourceOffset = pieceSources[0];
                var identity = true;
                for (var i = 0; i < pieceSources.Count; i++)
                    if (pieceSources[i] != sourceOffset + i) { identity = false; break; }
                if (!identity) charOffsets = pieceSources.ToArray();
                sourceLength = Math.Max(0, sourceEnd - sourceOffset);
            }
            return new CssInlinePiece(pieceElement, token, tokenWidth, lineHeight, baseline,
                box.MarginLeft, box.MarginTop, box.MarginRight, box.MarginBottom,
                collapsible, false, preserve, allowWrap, ancestors,
                sourceNode, sourceOffset, charOffsets, sourceLength);
        }

        if (whiteSpace == TextWhiteSpaceMode.Nowrap)
        {
            var normalized = new StringBuilder();
            var pendingSpace = false;
            var pendingSpaceSource = 0;
            var transformWordStart = true;
            foreach (var rune in value.EnumerateRunes())
            {
                var sourceIndex = DataIndexOf(valueIndex);
                valueIndex += rune.Utf16SequenceLength;
                lastConsumedEnd = DataIndexOf(valueIndex);
                if (Rune.IsWhiteSpace(rune))
                {
                    if (!pendingSpace)
                    {
                        pendingSpace = normalized.Length > 0;
                        if (pendingSpace) pendingSpaceSource = sourceIndex;
                    }
                    transformWordStart = true;
                    continue;
                }

                if (pendingSpace)
                {
                    normalized.Append(' ');
                    pieceSources.Add(pendingSpaceSource);
                }
                AppendTo(normalized, sourceIndex, TextWrapping.TransformRune(rune, textTransform, ref transformWordStart));
                pendingSpace = false;
            }

            if (normalized.Length > 0)
            {
                var token = normalized.ToString();
                var width = ControlDrawing.MeasureRenderedTextWidth(token, font, letterSpacing, wordSpacing);
                pieces.Add(MakePiece(token, width, collapsible: false, preserve: false, allowWrap: false, lastConsumedEnd));
            }
            return;
        }

        if (whiteSpace == TextWhiteSpaceMode.Pre)
        {
            var preserved = new StringBuilder();
            var transformWordStart = true;

            void FlushPreserved(int sourceEnd)
            {
                if (preserved.Length == 0) return;
                var token = preserved.ToString();
                var width = ControlDrawing.MeasureRenderedTextWidth(token, font, letterSpacing, wordSpacing);
                pieces.Add(MakePiece(token, width, collapsible: false, preserve: true, allowWrap: false, sourceEnd));
                preserved.Clear();
                pieceSources.Clear();
            }

            foreach (var rune in value.EnumerateRunes())
            {
                var previousEnd = lastConsumedEnd;
                var sourceIndex = DataIndexOf(valueIndex);
                valueIndex += rune.Utf16SequenceLength;
                lastConsumedEnd = DataIndexOf(valueIndex);
                if (rune.Value == '\n')
                {
                    FlushPreserved(previousEnd);
                    pieces.Add(CssInlinePiece.LineBreak(pieceElement, ancestors));
                    transformWordStart = true;
                    continue;
                }
                AppendTo(preserved, sourceIndex, TextWrapping.TransformRune(rune, textTransform, ref transformWordStart));
            }
            FlushPreserved(lastConsumedEnd);
            return;
        }

        var segment = new StringBuilder();
        var whitespace = false;
        var atWordStart = true;
        var preserveWhitespace = whiteSpace is TextWhiteSpaceMode.PreWrap;
        var preserveNewlines = whiteSpace is TextWhiteSpaceMode.Pre or TextWhiteSpaceMode.PreWrap or TextWhiteSpaceMode.PreLine;

        void Flush(int sourceEnd)
        {
            if (segment.Length == 0) return;
            var token = segment.ToString();
            var width = ControlDrawing.MeasureRenderedTextWidth(token, font, letterSpacing, wordSpacing);
            pieces.Add(MakePiece(token, width, whitespace && !preserveWhitespace, preserveWhitespace,
                whiteSpace is not (TextWhiteSpaceMode.Pre or TextWhiteSpaceMode.Nowrap), sourceEnd));
            segment.Clear();
            pieceSources.Clear();
        }

        foreach (var rune in value.EnumerateRunes())
        {
            var previousEnd = lastConsumedEnd;
            var sourceIndex = DataIndexOf(valueIndex);
            valueIndex += rune.Utf16SequenceLength;
            lastConsumedEnd = DataIndexOf(valueIndex);
            if (rune.Value == '\n')
            {
                // A pending collapsed space keeps absorbing the newline; a word token or a
                // preserved space ends here.
                if (preserveWhitespace || !whitespace) Flush(previousEnd);
                if (preserveNewlines)
                {
                    pieces.Add(CssInlinePiece.LineBreak(pieceElement, ancestors));
                    whitespace = false;
                }
                else if (!preserveWhitespace)
                {
                    if (!whitespace)
                    {
                        whitespace = true;
                        segment.Clear();
                        segment.Append(' ');
                        StartCollapsedSpace(sourceIndex);
                    }
                }
                else
                    whitespace = false;
                atWordStart = true;
                continue;
            }
            var isSpace = Rune.IsWhiteSpace(rune);
            if (segment.Length > 0 && isSpace != whitespace) Flush(previousEnd);
            if (isSpace && !preserveWhitespace)
            {
                if (whitespace) continue;
                whitespace = true;
                segment.Clear();
                segment.Append(' ');
                StartCollapsedSpace(sourceIndex);
                continue;
            }
            if (isSpace && whitespace) Flush(previousEnd);
            whitespace = isSpace;
            AppendTo(segment, sourceIndex, TextWrapping.TransformRune(rune, textTransform, ref atWordStart));
        }
        Flush(lastConsumedEnd);
    }

    /// <summary>Normalizes CR/CRLF to LF; returns the per-character value→data offset map when non-identity.</summary>
    private static (string Value, int[]? Map) NormalizeLineBreaks(string data)
    {
        if (!data.Contains('\r')) return (data, null);
        var builder = new StringBuilder(data.Length);
        var map = new int[data.Length];
        var length = 0;
        for (var i = 0; i < data.Length; i++)
        {
            var c = data[i];
            if (c == '\r')
            {
                builder.Append('\n');
                map[length++] = i;
                if (i + 1 < data.Length && data[i + 1] == '\n') i++;
            }
            else
            {
                builder.Append(c);
                map[length++] = i;
            }
        }
        return (builder.ToString(), map);
    }

    private CssInlinePiece CreateAtomicInlinePiece(Element element, CssBox box, float availableWidth)
    {
        var tableSize = IsTableRoot(ParseDisplayMode(CssKeyword(element, "display")))
            ? new TableLayoutEngine(this).Measure(element, new Size(availableWidth, float.PositiveInfinity))
            : Size.Zero;
        var outerWidth = tableSize != Size.Zero ? tableSize.Width : ResolveInlineOuterWidth(element, box, availableWidth);
        var outerHeight = tableSize != Size.Zero
            ? tableSize.Height
            : ResolveAtomicOuterHeight(element, box, outerWidth, float.MaxValue);
        return new CssInlinePiece(element, null,
            outerWidth + box.MarginLeft + box.MarginRight,
            outerHeight + box.MarginTop + box.MarginBottom,
            outerHeight + box.MarginTop,
            box.MarginLeft, box.MarginTop, box.MarginRight, box.MarginBottom, false, false, true, true);
    }

    private CssTextSplit SplitTextPiece(CssInlinePiece piece, float availableWidth)
    {
        var text = piece.Element as ControlText;
        if (piece.Text == null || (text == null && piece.SourceNode == null) || piece.Text.Length <= 1)
            return new CssTextSplit(piece, piece with { Text = "", Width = 0 });
        var font = text != null
            ? ControlDrawing.ResolveFont(text, text.FontSize)
            : ControlDrawing.ResolveFont(piece.Element, HTMLElement.HtmlTextDefaultFontSize);
        var width = 0f;
        var offset = 0;
        foreach (var rune in piece.Text.EnumerateRunes())
        {
            var advance = ControlDrawing.MeasureRenderedRuneAdvance(rune, font);
            if (offset > 0 && width + advance > availableWidth) break;
            width += advance;
            offset += rune.Utf16SequenceLength;
        }
        if (offset == 0) offset = char.IsSurrogatePair(piece.Text, 0) ? 2 : 1;
        var headText = piece.Text[..offset];
        var tailText = piece.Text[offset..];
        var letterSpacing = ControlDrawing.ResolveTextLength(piece.Element, "letter-spacing", font.Size);
        var wordSpacing = ControlDrawing.ResolveTextLength(piece.Element, "word-spacing", font.Size);
        var headLength = SourceLengthUpTo(piece, offset);
        var head = piece with
        {
            Text = headText,
            Width = ControlDrawing.MeasureRenderedTextWidth(headText, font, letterSpacing, wordSpacing),
            SourceCharOffsets = SliceSourceOffsets(piece.SourceCharOffsets, 0, offset),
            SourceLength = headLength
        };
        var tail = piece with
        {
            Text = tailText,
            Width = ControlDrawing.MeasureRenderedTextWidth(tailText, font, letterSpacing, wordSpacing),
            SourceOffset = piece.SourceOffset + headLength,
            SourceCharOffsets = SliceSourceOffsets(piece.SourceCharOffsets, offset, piece.Text.Length),
            SourceLength = Math.Max(0, piece.SourceLength - headLength)
        };
        return new CssTextSplit(head, tail);
    }

    /// <summary>UTF-16 source units consumed by the first <paramref name="charOffset"/> characters of a piece.</summary>
    private static int SourceLengthUpTo(CssInlinePiece piece, int charOffset)
    {
        if (charOffset >= piece.Text!.Length) return Math.Max(0, piece.SourceLength);
        return piece.SourceCharOffsets == null
            ? charOffset
            : Math.Max(0, piece.SourceCharOffsets[charOffset] - piece.SourceOffset);
    }

    private static int[]? SliceSourceOffsets(int[]? offsets, int start, int end)
    {
        if (offsets == null) return null;
        var slice = new int[Math.Max(0, end - start)];
        Array.Copy(offsets, start, slice, 0, slice.Length);
        return slice;
    }

    /// <summary>Whether the element lays out flow content beyond <see cref="Element.Children"/>:
    /// visual sidecars or direct DOM Text of an HTML host.</summary>
    private static bool HasFlowContent(Element element) =>
        element is HTMLElement host && (host.VisualSidecars.Count > 0 ||
            host.HasDirectTextContent && host.AcceptsDirectTextContent);

    private void LayoutFloat(Element element, CssBox box, string side, Rect content, float y,
        List<CssFloatArea> floats, CssContainingBlock containingBlock, CssLayoutPlan plan)
    {
        y = ApplyClear(element, y, floats) + box.MarginTop;
        var width = ResolveInlineOuterWidth(element, box, content.Width);
        var height = ResolveAtomicOuterHeight(element, box, width, float.MaxValue);
        var (left, right) = GetLineBounds(content, y, height, floats);
        if (right - left < width + box.MarginLeft + box.MarginRight)
        {
            y = NextFloatBottom(y, floats);
            (left, right) = GetLineBounds(content, y, height, floats);
        }
        var x = side == "right"
            ? right - width - box.MarginRight
            : left + box.MarginLeft;
        var shifted = ApplyRelativeOffset(element, x, y, content.Width, height);
        var bounds = new Rect(shifted.X, shifted.Y, width, height);
        if (element.Children.Count > 0 || HasFlowContent(element))
            LayoutBlock(element, box, bounds, containingBlock, plan);
        else
            plan.Set(element, bounds);
        floats.Add(new CssFloatArea(side, x - box.MarginLeft, y - box.MarginTop,
            width + box.MarginLeft + box.MarginRight, height + box.MarginTop + box.MarginBottom));
    }

    private void LayoutAbsolute(Element element, Rect containing, CssLayoutPlan plan)
    {
        var box = ResolveCssBox(element, containing.Width, containing.Height);
        var left = ResolveInset(element, "left", containing.Width, containing.Height);
        var right = ResolveInset(element, "right", containing.Width, containing.Height);
        var top = ResolveInset(element, "top", containing.Width, containing.Height);
        var bottom = ResolveInset(element, "bottom", containing.Width, containing.Height);
        var width = !float.IsNaN(box.Width)
            ? BorderWidthFromSpecified(box)
            : !float.IsNaN(left) && !float.IsNaN(right)
                ? Math.Max(0, containing.Width - left - right - box.MarginLeft - box.MarginRight)
                : ResolveInlineOuterWidth(element, box, containing.Width);
        var height = !float.IsNaN(box.Height)
            ? BorderHeightFromSpecified(box)
            : !float.IsNaN(top) && !float.IsNaN(bottom)
                ? Math.Max(0, containing.Height - top - bottom - box.MarginTop - box.MarginBottom)
                : ResolveAtomicOuterHeight(element, box, width, containing.Height);
        var x = !float.IsNaN(left) ? containing.X + left + box.MarginLeft
            : !float.IsNaN(right) ? containing.Right - right - width - box.MarginRight
            : containing.X + box.MarginLeft;
        var y = !float.IsNaN(top) ? containing.Y + top + box.MarginTop
            : !float.IsNaN(bottom) ? containing.Bottom - bottom - height - box.MarginBottom
            : containing.Y + box.MarginTop;
        var bounds = new Rect(x, y, width, height);
        if ((element.Children.Count > 0 || HasFlowContent(element)) &&
            CssKeyword(element, "display") is not ("flex" or "grid"))
            LayoutBlock(element, box, bounds, new CssContainingBlock(containing), plan);
        else if (CssKeyword(element, "display") is "flex" or "grid")
            plan.ExternalLayouts.Add(new CssLayoutEntry(element, bounds));
        else
            plan.Set(element, bounds);
    }

    private static float ApplyClear(Element element, float y, List<CssFloatArea> floats)
    {
        var clear = CssKeyword(element, "clear");
        if (clear is not ("left" or "right" or "both")) return y;
        foreach (var area in floats)
            if (clear == "both" || clear == area.Side)
                y = Math.Max(y, area.Bottom);
        return y;
    }

    private static (float Left, float Right) GetLineBounds(Rect content, float y, float height, List<CssFloatArea> floats)
    {
        var left = content.X;
        var right = content.Right;
        foreach (var area in floats)
        {
            if (area.Bottom <= y || area.Top >= y + height) continue;
            if (area.Side == "left") left = Math.Max(left, area.Right);
            else right = Math.Min(right, area.Left);
        }
        return (left, right);
    }

    private static float NextFloatBottom(float y, List<CssFloatArea> floats)
    {
        var next = floats.Where(area => area.Bottom > y).Select(area => area.Bottom).DefaultIfEmpty(y).Min();
        return next > y ? next : y + 1;
    }

    private static float ResolveTextAlign(Element element, float available, float used)
    {
        var align = CssKeyword(element, "text-align");
        return align switch
        {
            "center" => Math.Max(0, available - used) / 2f,
            "right" or "end" => Math.Max(0, available - used),
            _ => 0
        };
    }

    private static Point ApplyRelativeOffset(Element element, float x, float y, float parentWidth, float parentHeight)
    {
        if (CssKeyword(element, "position") != "relative") return new Point(x, y);
        var left = ResolveInset(element, "left", parentWidth, parentHeight);
        var right = ResolveInset(element, "right", parentWidth, parentHeight);
        var top = ResolveInset(element, "top", parentWidth, parentHeight);
        var bottom = ResolveInset(element, "bottom", parentWidth, parentHeight);
        if (!float.IsNaN(left)) x += left;
        else if (!float.IsNaN(right)) x -= right;
        if (!float.IsNaN(top)) y += top;
        else if (!float.IsNaN(bottom)) y -= bottom;
        return new Point(x, y);
    }

    private static float ResolveRootWidth(Element element, float availableWidth)
    {
        var box = ResolveCssBox(element, availableWidth, float.NaN);
        if (!float.IsNaN(box.Width)) return BorderWidthFromSpecified(box);
        return float.IsFinite(availableWidth) ? Math.Max(0, availableWidth) : 0;
    }

    private static float ResolveOuterWidth(Element element, CssBox box, float containingWidth,
        out float marginLeft, out float marginRight)
    {
        var width = float.IsNaN(box.Width)
            ? Math.Max(0, containingWidth - (box.MarginLeftAuto ? 0 : box.MarginLeft) - (box.MarginRightAuto ? 0 : box.MarginRight))
            : BorderWidthFromSpecified(box);
        var remaining = containingWidth - width - (box.MarginLeftAuto ? 0 : box.MarginLeft) - (box.MarginRightAuto ? 0 : box.MarginRight);
        marginLeft = box.MarginLeft;
        marginRight = box.MarginRight;
        if (box.MarginLeftAuto && box.MarginRightAuto)
            marginLeft = marginRight = Math.Max(0, remaining) / 2f;
        else if (box.MarginLeftAuto)
            marginLeft = Math.Max(0, remaining);
        else if (box.MarginRightAuto)
            marginRight = Math.Max(0, remaining);
        return width;
    }

    private static float ResolveInlineOuterWidth(Element element, CssBox box, float availableWidth)
    {
        if (!float.IsNaN(box.Width)) return BorderWidthFromSpecified(box);
        if (!HasIntrinsicMeasure(element)) return box.BorderLeft + box.BorderRight + box.PaddingLeft + box.PaddingRight;
        var measured = element.Measure(new Size(Math.Max(0, availableWidth), float.MaxValue));
        return Math.Max(0, measured.Width) + box.BorderLeft + box.BorderRight + box.PaddingLeft + box.PaddingRight;
    }

    private static float ResolveAtomicOuterHeight(Element element, CssBox box, float borderWidth, float availableHeight)
    {
        if (!float.IsNaN(box.Height)) return BorderHeightFromSpecified(box);
        if (!HasIntrinsicMeasure(element)) return box.BorderTop + box.BorderBottom + box.PaddingTop + box.PaddingBottom;
        var contentWidth = Math.Max(0, borderWidth - box.BorderLeft - box.BorderRight - box.PaddingLeft - box.PaddingRight);
        var measured = element.Measure(new Size(contentWidth, availableHeight));
        return Math.Max(0, measured.Height) + box.BorderTop + box.BorderBottom + box.PaddingTop + box.PaddingBottom;
    }

    private static bool HasIntrinsicMeasure(Element element) => element.HasCustomMeasure && element is not View;

    private static float ResolveOuterHeight(CssBox box, float naturalHeight)
    {
        var minHeight = float.IsNaN(box.MinHeight) ? 0 : box.ContentBox
            ? box.MinHeight + box.PaddingTop + box.PaddingBottom + box.BorderTop + box.BorderBottom
            : box.MinHeight;
        var maxHeight = float.IsNaN(box.MaxHeight) ? float.PositiveInfinity : box.ContentBox
            ? box.MaxHeight + box.PaddingTop + box.PaddingBottom + box.BorderTop + box.BorderBottom
            : box.MaxHeight;
        if (maxHeight < minHeight) maxHeight = minHeight;

        return Math.Min(Math.Max(float.IsNaN(box.Height) ? naturalHeight : BorderHeightFromSpecified(box), minHeight), maxHeight);
    }

    private static float BorderWidthFromSpecified(CssBox box) => box.ContentBox
        ? Math.Max(0, box.Width) + box.PaddingLeft + box.PaddingRight + box.BorderLeft + box.BorderRight
        : Math.Max(0, box.Width);

    private static float BorderHeightFromSpecified(CssBox box) => box.ContentBox
        ? Math.Max(0, box.Height) + box.PaddingTop + box.PaddingBottom + box.BorderTop + box.BorderBottom
        : Math.Max(0, box.Height);

    private static Rect PaddingBox(Rect borderBounds, CssBox box) => new(
        borderBounds.X + box.BorderLeft,
        borderBounds.Y + box.BorderTop,
        Math.Max(0, borderBounds.Width - box.BorderLeft - box.BorderRight),
        Math.Max(0, borderBounds.Height - box.BorderTop - box.BorderBottom));

    /// <summary>
    /// Content-box rect of an element's laid-out border box. HTML hosts use it to place their
    /// visual sidecars (native proxies, list marker fallback) at the content origin.
    /// </summary>
    internal static Rect ResolveContentRect(Element element)
    {
        var geometry = element.Geometry;
        var box = ResolveCssBox(element, geometry.Width, float.NaN);
        return new Rect(
            geometry.X + box.BorderLeft + box.PaddingLeft,
            geometry.Y + box.BorderTop + box.PaddingTop,
            Math.Max(0, geometry.Width - box.BorderLeft - box.BorderRight - box.PaddingLeft - box.PaddingRight),
            Math.Max(0, geometry.Height - box.BorderTop - box.BorderBottom - box.PaddingTop - box.PaddingBottom));
    }

    private static bool EstablishesContainingBlock(Element element)
    {
        var display = CssKeyword(element, "display");
        if (display is not ("" or "block" or "flow-root" or "flex" or "grid" or "table" or "inline-table"))
            return false;
        var position = CssKeyword(element, "position");
        return position is "relative" or "absolute" or "fixed" || display is "flex" or "grid";
    }

    private static bool CanCollapseThrough(CssBox box) =>
        float.IsNaN(box.Height) && (float.IsNaN(box.MinHeight) || box.MinHeight <= 0) &&
        box.PaddingTop == 0 && box.PaddingBottom == 0 && box.BorderTop == 0 && box.BorderBottom == 0;

    private static float CollapseMargins(float first, float second)
    {
        var positive = Math.Max(0, Math.Max(first, second));
        var negative = Math.Min(0, Math.Min(first, second));
        return positive + negative;
    }

    private static float ResolveInset(Element element, string name, float parentWidth, float parentHeight)
    {
        var value = element.Style.Get(name);
        if (value == null)
        {
            var inset = element.Style.Get("inset");
            if (TryParseInsetShorthand(inset, out var parts))
                value = name switch
                {
                    "top" => parts.Top,
                    "right" => parts.Right,
                    "bottom" => parts.Bottom,
                    _ => parts.Left
                };
        }
        return ParseLength(value, name is "left" or "right" ? parentWidth : parentHeight,
            parentHeight, GetFontSize(element), GetRootFontSize(element));
    }

    private static CssBox ResolveCssBox(Element element, float parentWidth, float parentHeight)
    {
        var em = GetFontSize(element);
        var rem = GetRootFontSize(element);
        var padding = ResolvePadding(element, parentWidth, parentHeight, em, rem);
        var margin = ResolveEdges(element, "margin", parentWidth, parentHeight, em, rem, true);
        var border = ResolveEdges(element, "border", parentWidth, parentHeight, em, rem, false);
        return new CssBox(
            ParseLength(element.Style.Get("width"), parentWidth, parentHeight, em, rem),
            ParseLength(element.Style.Get("height"), parentHeight, parentHeight, em, rem),
            ParseLength(element.Style.Get("min-height"), parentHeight, parentHeight, em, rem),
            ParseLength(element.Style.Get("max-height"), parentHeight, parentHeight, em, rem),
            padding.Top, padding.Right, padding.Bottom, padding.Left,
            margin.Top, margin.Right, margin.Bottom, margin.Left,
            margin.TopAuto, margin.RightAuto, margin.BottomAuto, margin.LeftAuto,
            border.Top, border.Right, border.Bottom, border.Left,
            !string.Equals(element.Style.Get("box-sizing")?.Trim(), "border-box", StringComparison.OrdinalIgnoreCase));
    }

    private static BoxEdges ResolveEdges(Element element, string prefix, float parentWidth, float parentHeight,
        float em, float rem, bool allowAuto)
    {
        var result = new BoxEdges(0, 0, 0, 0, false, false, false, false);
        var shorthandName = prefix == "border" ? "border-width" : prefix;
        if (TryParseBoxShorthand(element.Style.Get(shorthandName), parentWidth, parentHeight, em, rem, allowAuto, out var shorthand))
            result = shorthand;
        ApplyResolvedEdge(element.Style.Get(prefix == "border" ? "border-top-width" : prefix + "-top"), parentWidth, parentHeight, em, rem, allowAuto,
            (value, auto) => result = result with { Top = value, TopAuto = auto });
        ApplyResolvedEdge(element.Style.Get(prefix == "border" ? "border-right-width" : prefix + "-right"), parentWidth, parentHeight, em, rem, allowAuto,
            (value, auto) => result = result with { Right = value, RightAuto = auto });
        ApplyResolvedEdge(element.Style.Get(prefix == "border" ? "border-bottom-width" : prefix + "-bottom"), parentWidth, parentHeight, em, rem, allowAuto,
            (value, auto) => result = result with { Bottom = value, BottomAuto = auto });
        ApplyResolvedEdge(element.Style.Get(prefix == "border" ? "border-left-width" : prefix + "-left"), parentWidth, parentHeight, em, rem, allowAuto,
            (value, auto) => result = result with { Left = value, LeftAuto = auto });
        return result;
    }

    private static void ApplyResolvedEdge(string? raw, float parentWidth, float parentHeight, float em, float rem,
        bool allowAuto, Action<float, bool> apply)
    {
        if (allowAuto && string.Equals(raw?.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
        {
            apply(0, true);
            return;
        }
        if (TryParsePoints(raw, parentWidth, parentHeight, em, rem, out var value)) apply(value, false);
    }

    private static string CssKeyword(Element element, string name) =>
        (element.Style.Get(name) ?? "").Trim().ToLowerInvariant();

    private sealed class CssLayoutPlan
    {
        private readonly Dictionary<Element, int> _indices = new();
        public List<CssLayoutEntry> Entries { get; } = [];
        public List<CssLayoutEntry> ExternalLayouts { get; } = [];
        public Dictionary<ControlText, List<TextLayoutFragment>> TextFragments { get; } = new();
        public Dictionary<Square.UI.Text, List<HtmlTextFragment>> HtmlTextFragments { get; } = new();
        public HashSet<Element> FixedRoots { get; } = [];
        public Size DesiredSize { get; set; }

        public void Set(Element element, Rect bounds)
        {
            if (_indices.TryGetValue(element, out var index)) Entries[index] = new CssLayoutEntry(element, bounds);
            else
            {
                _indices[element] = Entries.Count;
                Entries.Add(new CssLayoutEntry(element, bounds));
            }
        }

        public void Union(Element element, Rect bounds)
        {
            if (!_indices.TryGetValue(element, out var index))
            {
                Set(element, bounds);
                return;
            }
            Entries[index] = new CssLayoutEntry(element, Rect.Union(Entries[index].Bounds, bounds));
        }

        public void AddTextFragment(ControlText text, TextLayoutFragment fragment)
        {
            if (!TextFragments.TryGetValue(text, out var fragments))
                TextFragments[text] = fragments = [];
            fragments.Add(fragment);
        }

        public void AddHtmlTextFragment(Square.UI.Text node, HtmlTextFragment fragment)
        {
            if (!HtmlTextFragments.TryGetValue(node, out var fragments))
                HtmlTextFragments[node] = fragments = [];
            fragments.Add(fragment);
        }
    }

    /// <summary>An inline-group member: either an element or a DOM Text node styled by its parent.</summary>
    private readonly record struct CssFlowItem(Element Element, Square.UI.Text? TextNode = null);

    private readonly record struct CssLayoutEntry(Element Element, Rect Bounds);
    private readonly record struct CssFloatArea(string Side, float Left, float Top, float Width, float Height)
    {
        public float Right => Left + Width;
        public float Bottom => Top + Height;
    }
    private readonly record struct CssTextSplit(CssInlinePiece Head, CssInlinePiece Tail);
    private readonly record struct CssInlinePiece(Element Element, string? Text, float Width, float Height, float Baseline,
        float MarginLeft, float MarginTop, float MarginRight, float MarginBottom, bool IsCollapsibleSpace, bool ForceBreak,
        bool PreserveWhitespace, bool AllowWrap, List<Element>? InlineAncestors = null,
        Square.UI.Text? SourceNode = null, int SourceOffset = 0, int[]? SourceCharOffsets = null, int SourceLength = 0)
    {
        public static CssInlinePiece LineBreak(Element element, List<Element>? ancestors = null) =>
            new(element, null, 0, 0, 0, 0, 0, 0, 0, false, true, true, true, ancestors);
    }
    private readonly record struct CssBox(float Width, float Height, float MinHeight, float MaxHeight,
        float PaddingTop, float PaddingRight, float PaddingBottom, float PaddingLeft,
        float MarginTop, float MarginRight, float MarginBottom, float MarginLeft,
        bool MarginTopAuto, bool MarginRightAuto, bool MarginBottomAuto, bool MarginLeftAuto,
        float BorderTop, float BorderRight, float BorderBottom, float BorderLeft,
        bool ContentBox)
    {
        /// <summary>Zero-decoration box for DOM Text pieces (their parent already contributes its own box).</summary>
        internal static readonly CssBox Empty = new(float.NaN, float.NaN, float.NaN, float.NaN,
            0, 0, 0, 0, 0, 0, 0, 0, false, false, false, false, 0, 0, 0, 0, true);
    }

    private readonly record struct CssContainingBlock(Rect Rect);
}
