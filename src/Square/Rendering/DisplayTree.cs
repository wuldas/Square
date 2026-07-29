using Square.Controls;
using Square.Graphics;
using Square.UI;
using Square.Rendering.Tree;
using System.Text;

namespace Square.Rendering;

/// <summary>显示树：将文档元素树映射为可渲染的节点树，并维护脏区。</summary>
public sealed class DisplayTree
{
    private readonly DisplayNode _root = new();
    private readonly List<Rect> _dirtyRects = [];
    private readonly List<IPopupElement> _popups = [];

    /// <summary>以指定元素为根重建整棵显示树。</summary>
    public void BuildFrom(Element element)
    {
        _root.Element = null;
        _root.Children.Clear();
        _dirtyRects.Clear();
        Synchronize(element);
    }

    /// <summary>Synchronizes element structure while preserving display nodes for unchanged elements.</summary>
    public void Synchronize(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!ReferenceEquals(_root.Element, element))
        {
            _root.Element = element;
            _root.Bounds = element.Geometry;
            _root.RebuildCommands();
            _root.PopupBounds = GetPopupVisualBounds(element);
            _root.IsDirty = true;
        }
        SynchronizeChildren(_root, element);
        RebuildPopupList();
    }

    private static void SynchronizeChildren(DisplayNode parent, Element element)
    {
        var existing = new Dictionary<Element, DisplayNode>();
        foreach (var child in parent.Children)
            if (child.Element != null) existing[child.Element] = child;

        var ordered = element.Children
            .Select(static (child, index) => (Child: child, Index: index))
            .Where(static item => item.Child.IsVisible)
            .OrderBy(static item => item.Child.ZIndex)
            .ThenBy(static item => item.Index)
            .Select(static item => item.Child)
            .ToList();
        var synchronized = new List<DisplayNode>(ordered.Count);
        foreach (var child in ordered)
        {
            if (!existing.TryGetValue(child, out var node))
            {
                node = new DisplayNode { Element = child, Bounds = child.Geometry, IsDirty = true };
                node.RebuildCommands();
                node.PopupBounds = GetPopupVisualBounds(child);
                node.IsDirty = true;
            }
            SynchronizeChildren(node, child);
            synchronized.Add(node);
        }

        parent.Children.Clear();
        parent.Children.AddRange(synchronized);
    }

    /// <summary>将指定矩形加入脏区队列。</summary>
    public void Invalidate(Rect rect)
    {
        if (!rect.IsEmpty)
            _dirtyRects.Add(rect);
    }

    /// <summary>遍历显示树更新脏节点与几何变化。</summary>
    public void UpdateDirty() => UpdateDirty(_root, default);

    private void UpdateDirty(DisplayNode node, Point visualOffset)
    {
        visualOffset = GetNodeVisualOffset(node, visualOffset);
        if (node.Element != null)
        {
            var bounds = node.Element.Geometry;
            var oldVisualBounds = node.VisualBounds.IsEmpty ? node.Bounds : node.VisualBounds;
            var oldPopupBounds = node.PopupBounds;
            if (node.Bounds != bounds)
            {
                _dirtyRects.Add(PadAndSnap(Translate(Union(oldVisualBounds, bounds), visualOffset)));
                node.Bounds = bounds;
            }
            if (node.Element.NeedsPaint)
            {
                node.IsDirty = true;
                var partial = !node.Element.IsPaintFullDirty && node.Element.PaintDirtyRects.Count > 0;
                var partialRects = partial ? node.Element.PaintDirtyRects.ToArray() : null;
                node.RebuildCommands();
                if (partialRects is { Length: > 0 })
                {
                    var origin = node.Element.Geometry;
                    foreach (var local in partialRects)
                    {
                        var absolute = new Rect(
                            origin.X + local.X,
                            origin.Y + local.Y,
                            local.Width,
                            local.Height);
                        _dirtyRects.Add(PadAndSnap(Translate(absolute, visualOffset)));
                    }
                }
                else
                {
                    _dirtyRects.Add(PadAndSnap(Translate(Union(oldVisualBounds, node.VisualBounds), visualOffset)));
                }
            }

            var popupBounds = GetPopupVisualBounds(node.Element);
            if (oldPopupBounds != popupBounds)
            {
                _dirtyRects.Add(PadAndSnap(Union(oldPopupBounds, popupBounds)));
                node.PopupBounds = popupBounds;
            }
        }
        var childOffset = GetChildVisualOffset(node, visualOffset);
        foreach (var child in node.Children)
            UpdateDirty(child, childOffset);
    }

    private void RebuildPopupList()
    {
        _popups.Clear();
        CollectPopups(_root);
    }

    private void CollectPopups(DisplayNode node)
    {
        if (node.Element is IPopupElement popup)
            _popups.Add(popup);
        foreach (var child in node.Children)
            CollectPopups(child);
    }

    /// <summary>
    /// 收集本帧需要重画的矩形（NeedsPaint / IsDirty 节点的 Geometry，1px 外扩取整）。
    /// </summary>
    public List<Rect> CollectDirtyRects()
    {
        CollectDirtyRects(_root, _dirtyRects, default);
        var dirty = MergeDirtyRects(_dirtyRects);
        _dirtyRects.Clear();
        return dirty;
    }

    private static bool CollectDirtyRects(DisplayNode node, List<Rect> dest, Point visualOffset)
    {
        visualOffset = GetNodeVisualOffset(node, visualOffset);
        var subtreeDirty = node.IsDirty || (node.Element != null && node.Element.NeedsPaint);
        if (subtreeDirty)
        {
            var g = node.VisualBounds.IsEmpty ? node.Element?.Geometry ?? node.Bounds : node.VisualBounds;
            // Geometry 尚未 arrange 时用 Bounds；仍空则跳过（父/兄弟可能有有效区）
            if (!g.IsEmpty)
                dest.Add(PadAndSnap(Translate(g, visualOffset)));
            var popupBounds = GetPopupVisualBounds(node.Element);
            if (!popupBounds.IsEmpty) dest.Add(PadAndSnap(popupBounds));
        }
        var childOffset = GetChildVisualOffset(node, visualOffset);
        foreach (var child in node.Children)
            subtreeDirty |= CollectDirtyRects(child, dest, childOffset);
        if (subtreeDirty && node.Element is IPopupElement { IsPopupOpen: true })
        {
            var popupBounds = GetPopupVisualBounds(node.Element);
            if (!popupBounds.IsEmpty) dest.Add(PadAndSnap(popupBounds));
        }
        return subtreeDirty;
    }

    private static Point GetChildVisualOffset(DisplayNode node, Point current)
    {
        if (node.Element?.MapsScrollOffsetForChildren() != true) return current;
        return new Point(current.X - node.Element.ScrollLeft, current.Y - node.Element.ScrollTop);
    }

    private static Point GetNodeVisualOffset(DisplayNode node, Point current)
    {
        if (node.Element is not IPopupElement { IsPopupOpen: true } popup)
            return current;
        var geometry = node.Element.Geometry;
        var bounds = popup.PopupBounds;
        return new Point(bounds.X - geometry.X, bounds.Y - geometry.Y);
    }

    private static Rect Translate(Rect rect, Point offset) =>
        new(rect.X + offset.X, rect.Y + offset.Y, rect.Width, rect.Height);

    /// <summary>外扩 1 逻辑像素并 snap 到整数像素，减少抗锯齿残影。</summary>
    private static Rect PadAndSnap(Rect g)
    {
        var x0 = MathF.Floor(g.X) - 1;
        var y0 = MathF.Floor(g.Y) - 1;
        var x1 = MathF.Ceiling(g.Right) + 1;
        var y1 = MathF.Ceiling(g.Bottom) + 1;
        return new Rect(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }

    /// <summary>
    /// 合并相交/相邻脏矩形（简单 O(n²) 迭代合并，动画场景 n 通常很小）。
    /// </summary>
    public static List<Rect> MergeDirtyRects(List<Rect> rects)
    {
        var list = new List<Rect>(rects.Count);
        for (var i = 0; i < rects.Count; i++)
        {
            if (!rects[i].IsEmpty)
                list.Add(rects[i]);
        }
        if (list.Count <= 1) return list;
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < list.Count; i++)
            {
                for (var j = i + 1; j < list.Count; j++)
                {
                    if (!RectsShouldMerge(list[i], list[j])) continue;
                    list[i] = Union(list[i], list[j]);
                    list.RemoveAt(j);
                    changed = true;
                    break;
                }
                if (changed) break;
            }
        }
        return list;
    }

    private static bool RectsShouldMerge(Rect a, Rect b)
    {
        // 相交或间距 ≤ 2px 的相邻矩形合并，减少 Present 次数
        var inflated = a.Inflate(2, 2);
        return inflated.IntersectsWith(b);
    }

    /// <summary>计算两矩形的并集（空矩形视为另一矩形）。</summary>
    public static Rect Union(Rect a, Rect b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        var x0 = Math.Min(a.X, b.X);
        var y0 = Math.Min(a.Y, b.Y);
        var x1 = Math.Max(a.Right, b.Right);
        var y1 = Math.Max(a.Bottom, b.Bottom);
        return new Rect(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>计算矩形面积（空矩形返回 0）。</summary>
    public static float Area(Rect r) => r.IsEmpty ? 0 : r.Width * r.Height;

    /// <summary>渲染整棵显示树。</summary>
    public void Render(IRenderContext ctx) => Render(ctx, dirtyClip: null);

    /// <summary>
    /// 渲染显示树。<paramref name="dirtyClip"/> 非 null 时仅绘制与之相交的节点。
    /// </summary>
    public void Render(IRenderContext ctx, Rect? dirtyClip)
    {
        if (dirtyClip is { IsEmpty: true })
        {
            _dirtyRects.Clear();
            return;
        }
        if (dirtyClip is { } clip)
        {
            ctx.PushClip(clip);
            _root.Render(ctx, clip);
            RenderPopups(ctx, clip);
            ctx.PopClip();
        }
        else
        {
            _root.Render(ctx, dirtyClip);
            RenderPopups(ctx, dirtyClip);
        }
        _dirtyRects.Clear();
    }

    /// <summary>对所有打开的弹出层进行命中测试。</summary>
    public Element? HitTestPopups(Point point)
    {
        for (var i = _popups.Count - 1; i >= 0; i--)
        {
            if (!_popups[i].IsPopupOpen) continue;
            var hit = _popups[i].HitTestPopup(point);
            if (hit != null) return hit;
        }
        return null;
    }

    /// <summary>将指针移动事件分发至相关弹出层，返回是否有状态变化。</summary>
    public bool HandlePointerMove(Point point)
    {
        var changed = false;
        for (var i = _popups.Count - 1; i >= 0; i--)
        {
            if (_popups[i] is Select select)
                changed |= select.HandlePointerMove(point);
        }
        return changed;
    }

    /// <summary>关闭不包含指定点且需在按下外部关闭的弹出层。</summary>
    public bool DismissPopupsOutside(Point point)
    {
        var changed = false;
        for (var i = _popups.Count - 1; i >= 0; i--)
        {
            var popup = _popups[i];
            if (!popup.IsPopupOpen || !popup.DismissOnPointerDownOutside || popup.ContainsPopupInteraction(point))
                continue;
            popup.ClosePopup();
            changed = true;
        }
        return changed;
    }

    /// <summary>关闭最顶层支持 Esc 关闭的弹出层。</summary>
    public bool DismissTopmostPopupOnEscape()
    {
        for (var i = _popups.Count - 1; i >= 0; i--)
        {
            var popup = _popups[i];
            if (!popup.IsPopupOpen || !popup.CloseOnEscape) continue;
            popup.ClosePopup();
            return true;
        }
        return false;
    }

    /// <summary>将按键事件转发给最顶层打开的弹出层。</summary>
    public bool HandlePopupKey(int keyCode, bool shift, bool control, bool alt)
    {
        for (var i = _popups.Count - 1; i >= 0; i--)
        {
            var popup = _popups[i];
            if (!popup.IsPopupOpen) continue;
            if (popup.HandlePopupKey(keyCode, shift, control, alt)) return true;
        }
        return false;
    }

    /// <summary>收集指定根元素子树内所有文本片段。</summary>
    public List<TextFragment> CollectTextFragments(Element root)
    {
        var fragments = new List<TextFragment>();
        CollectTextFragments(_root, root, fragments);
        return fragments;
    }

    private static void CollectTextFragments(DisplayNode node, Element root, List<TextFragment> fragments)
    {
        if (node.Element != null && IsDescendantOrSelf(node.Element, root))
        {
            foreach (var command in node.Commands.OfType<Commands.DrawTextCommand>())
            {
                var fragment = CreateTextFragment(node.Element, command);
                if (fragment != null) fragments.Add(fragment);
            }
        }

        foreach (var child in node.Children)
            CollectTextFragments(child, root, fragments);
    }

    private static TextFragment? CreateTextFragment(Element element, Commands.DrawTextCommand command)
    {
        var text = command.Text.Text;
        if (string.IsNullOrEmpty(text)) return null;

        var lineHeight = TextMetrics.GetLineHeight(command.Text.Font, command.Text.LineHeight);
        var maxWidth = command.Text.MaxSize.Width;
        var characters = new List<TextCharacterFragment>();
        var advances = new Dictionary<int, float>();
        var lines = TextWrapping.Wrap(text, maxWidth, (offset, rune) =>
        {
            var advance = TextMetrics.GetGlyphMetrics(command.Text.Font, rune).AdvanceX;
            advances[offset] = advance;
            return advance;
        });
        var maxRight = command.Origin.X;

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var x = command.Origin.X;
            var y = command.Origin.Y + lineIndex * lineHeight;
            for (var offset = line.StartOffset; offset < line.EndOffset;)
            {
                var status = Rune.DecodeFromUtf16(text.AsSpan(offset), out var rune, out var consumed);
                if (status != System.Buffers.OperationStatus.Done) break;
                var startOffset = offset;
                var advance = advances[offset];
                offset += consumed;
                var glyphBounds = TextMetrics.GetGlyphBoundsInLine(command.Text.Font, rune, lineHeight);
                var selectionTop = Math.Min(y, y + glyphBounds.Top);
                var selectionBottom = Math.Max(y + lineHeight, y + glyphBounds.Bottom);
                var bounds = new Rect(x, y, advance, Math.Max(lineHeight, glyphBounds.Bottom));
                var selectionLeft = Math.Min(x, x + glyphBounds.Left);
                var selectionRight = Math.Max(x + advance, x + glyphBounds.Right);
                var selectionBounds = new Rect(
                    selectionLeft,
                    selectionTop,
                    selectionRight - selectionLeft,
                    selectionBottom - selectionTop);
                characters.Add(new TextCharacterFragment(startOffset, offset, bounds, selectionBounds));
                x += advance;
            }
            maxRight = Math.Max(maxRight, x);
        }

        if (characters.Count == 0) return null;
        var bottom = characters.Max(character => character.Bounds.Bottom);
        var boundsAll = new Rect(
            command.Origin.X,
            command.Origin.Y,
            maxRight - command.Origin.X,
            bottom - command.Origin.Y);
        return new TextFragment(element, text, command.Text.Font, boundsAll, characters);
    }

    private static bool IsDescendantOrSelf(Element element, Element root)
    {
        for (var current = element; current != null; current = current.Parent)
            if (ReferenceEquals(current, root)) return true;
        return false;
    }

    private void RenderPopups(IRenderContext ctx, Rect? dirtyClip)
    {
        foreach (var popup in _popups)
        {
            if (!popup.IsPopupOpen) continue;
            var visualBounds = popup is Element element ? GetPopupVisualBounds(element) : popup.PopupBounds;
            if (dirtyClip is { } clip && !visualBounds.IntersectsWith(clip)) continue;
            popup.PaintPopup(ctx);
        }
    }

    private static Rect GetPopupVisualBounds(Element? element)
    {
        if (element is not IPopupElement { IsPopupOpen: true } popup) return Rect.Empty;
        var bounds = popup.PopupBounds;
        return BoxShadow.TryParse(element.Style.GetPropertyValue("box-shadow"), out var shadow)
            ? BoxShadowRendering.GetVisualBounds(bounds, shadow)
            : bounds;
    }
}
