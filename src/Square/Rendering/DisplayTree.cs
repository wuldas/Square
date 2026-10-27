using Square.Controls;
using Square.Graphics;
using Square.Html;
using Square.UI;
using Square.UI.Scrolling;
using Square.Rendering.Tree;
using System.Numerics;
using System.Text;
using Square.Rendering.Commands;

namespace Square.Rendering;

/// <summary>显示树：将文档元素树映射为可渲染的节点树，并维护脏区。</summary>
public sealed class DisplayTree
{
    private const float TileSize = 32f;

    private readonly DisplayNode _root = new();
    private readonly List<Rect> _pendingDamage = [];
    private readonly List<DisplayNode> _fixedRoots = [];
    private readonly HashSet<DisplayNode> _fixedRootSet = [];
    private readonly List<IPopupElement> _popups = [];
    private readonly List<(int Start, int End, bool Continued)> _rowRuns = [];
    private readonly List<(int Start, int End, int Row, int Rows)> _activeRuns = [];
    private bool[] _tileMarks = [];
    private int _tileColumns;
    private int _tileRows;
    private long _frameStamp;
    private long _syncedStructureRevision = -1;
    private bool _hasSynchronized;
    private Size _rootSize;

    /// <summary>
    /// 下一帧是否必须整帧重绘：首次构建、结构增删、display/可见性/z 序变化、根尺寸变化时置 true，
    /// 仅在成功 Present 后由 <see cref="CommitPresentedFrame"/> 清除。
    /// </summary>
    public bool RequiresFullFrame { get; private set; } = true;

    /// <summary>以指定元素为根重建整棵显示树。</summary>
    public void BuildFrom(Element element)
    {
        _root.Element = null;
        _root.Children.Clear();
        _pendingDamage.Clear();
        _syncedStructureRevision = -1;
        _hasSynchronized = false;
        RequiresFullFrame = true;
        Synchronize(element);
    }

    /// <summary>
    /// 判断显示树是否需要结构同步：根元素更换、尚未同步过，或根的
    /// <see cref="Square.UI.Element.StructureRevision"/>（子树拓扑/可见性/z 序/display 修订戳）已变化。
    /// 纯布局、transform/opacity、滚动与普通绘制失效不触发同步。
    /// </summary>
    public bool NeedsSynchronization(Element root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!ReferenceEquals(_root.Element, root)) return true;
        if (!_hasSynchronized) return true;
        return _syncedStructureRevision != root.StructureRevision;
    }

    /// <summary>Synchronizes element structure while preserving display nodes for unchanged elements.</summary>
    public void Synchronize(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var structureChanged = !ReferenceEquals(_root.Element, element);
        if (!ReferenceEquals(_root.Element, element))
        {
            _root.Element = element;
            _root.Bounds = element.Geometry;
            _root.RebuildCommands();
            _root.PopupBounds = DisplayNode.GetPopupVisualBounds(element);
            _root.IsDirty = true;
            _root.MarkCompositeStyleDirty();
            _root.PresentedScreenSubtreeBounds = Rect.Empty;
        }
        structureChanged |= SynchronizeChildren(_root, element);
        RebuildLayerLists();
        _syncedStructureRevision = element.StructureRevision;
        _hasSynchronized = true;
        // 结构同步后让所有节点在下次渲染/UpdateDirty 时重新准备（新增节点、z 序与层列表已变化）。
        _frameStamp++;
        if (structureChanged)
            RequiresFullFrame = true;
    }

    private bool SynchronizeChildren(DisplayNode parent, Element element)
    {
        var changed = false;
        if (!element.IsCssDisplayed())
        {
            changed = parent.Children.Count > 0;
            parent.Children.Clear();
            return changed;
        }

        var existing = new Dictionary<Element, DisplayNode>();
        foreach (var child in parent.Children)
            if (child.Element != null) existing[child.Element] = child;

        DisplayNode GetOrCreateNode(Element child)
        {
            if (!existing.TryGetValue(child, out var node))
            {
                node = new DisplayNode { Element = child, Bounds = child.Geometry, IsDirty = true };
                node.RebuildCommands();
                node.PopupBounds = DisplayNode.GetPopupVisualBounds(child);
                node.IsDirty = true;
            }
            return node;
        }

        var ordered = element.Children
            .Select(static (child, index) => (Child: child, Index: index))
            .Where(static item => item.Child.IsVisible && item.Child.IsCssDisplayed())
            .OrderBy(static item => item.Child.ZIndex)
            .ThenBy(static item => item.Index)
            .Select(static item => item.Child)
            .ToList();
        var synchronized = new List<DisplayNode>(ordered.Count);
        foreach (var child in ordered)
        {
            var node = GetOrCreateNode(child);
            changed |= SynchronizeChildren(node, child);
            synchronized.Add(node);
        }

        // HTML visual sidecars (native proxies, list marker) render after the semantic children,
        // like the controls did while they were still DOM children — but never enter the DOM.
        if (element is HTMLElement host)
            foreach (var sidecar in host.VisualSidecars)
            {
                if (!sidecar.IsVisible || !sidecar.IsCssDisplayed()) continue;
                var node = GetOrCreateNode(sidecar);
                changed |= SynchronizeChildren(node, sidecar);
                synchronized.Add(node);
            }

        // 保留递归结果：深层子树的结构增删/display/可见性变化必须传到根，否则
        // RequiresFullFrame 不会置位，被移除节点占用的屏幕区域将永远无人清除。
        changed |= parent.Children.Count != synchronized.Count;
        if (!changed)
        {
            for (var i = 0; i < synchronized.Count; i++)
            {
                if (ReferenceEquals(parent.Children[i].Element, synchronized[i].Element)) continue;
                changed = true;
                break;
            }
        }
        parent.Children.Clear();
        parent.Children.AddRange(synchronized);
        return changed;
    }

    /// <summary>将指定矩形加入脏区队列（client 屏幕空间）。</summary>
    public void Invalidate(Rect rect)
    {
        if (!rect.IsEmpty)
            _pendingDamage.Add(rect);
    }

    /// <summary>
    /// 遍历显示树：重建真实 Paint 变化节点的命令、消费复合视觉变化（每帧解析一次 transform/opacity）、
    /// 以与渲染一致的累计矩阵计算每节点屏幕空间包围盒，并累积 old∪new damage。
    /// </summary>
    public void UpdateDirty()
    {
        _frameStamp++;
        _fixedRoots.Clear();
        _fixedRootSet.Clear();
        _popups.Clear();

        var rootElement = _root.Element;
        if (rootElement != null)
        {
            var rootGeometry = rootElement.Geometry;
            if (rootGeometry.Size.Width != _rootSize.Width || rootGeometry.Size.Height != _rootSize.Height)
            {
                // 根尺寸（窗口客户区）变化：上次 Present 的内容整体失效，回退全帧。
                _rootSize = rootGeometry.Size;
                RequiresFullFrame = true;
            }
        }

        UpdateWalk(_root, Matrix3x2.Identity, ancestorEmitted: false, insidePopupSubtree: false);
    }

    /// <returns>本节点子树是否发出了 damage。</returns>
    private bool UpdateWalk(DisplayNode node, Matrix3x2 parentMatrix, bool ancestorEmitted, bool insidePopupSubtree)
    {
        var element = node.Element;
        if (element == null)
        {
            foreach (var child in node.Children)
                UpdateWalk(child, parentMatrix, ancestorEmitted, insidePopupSubtree);
            return false;
        }

        var isFixedRoot = IsFixedRoot(node);
        if (isFixedRoot)
        {
            // 固定层与弹出层一样是独立变换上下文，累计矩阵在此重置。
            parentMatrix = Matrix3x2.Identity;
            ancestorEmitted = false;
            insidePopupSubtree = false;
            _fixedRoots.Add(node);
            _fixedRootSet.Add(node);
        }
        if (element is IPopupElement popup)
        {
            _popups.Add(popup);
            ancestorEmitted = false;
            insidePopupSubtree = false;
        }

        if (!element.IsCssDisplayed())
        {
            node.FinishPrepare(parentMatrix, _fixedRootSet);
            node.MarkPrepared(_frameStamp, parentMatrix);
            return false;
        }

        // ---- 捕获上一帧状态 ----
        var oldBounds = node.Bounds;
        var oldMapsScroll = node.MapsChildScrollOffset;
        var oldScrollOffset = node.ChildScrollOffset;
        var oldPopupBounds = node.PopupBounds;

        var paintWasDirty = node.IsDirty || element.NeedsPaint;
        List<Rect>? partialRects = null;
        if (element.NeedsPaint && !element.IsPaintFullDirty && element.PaintDirtyRects.Count > 0)
            partialRects = new List<Rect>(element.PaintDirtyRects);

        // ---- 命令保留：只有真实 Paint 变化重建命令；Composite 只更新视觉状态缓存 ----
        if (paintWasDirty)
            node.RebuildCommands();
        var compositeChanged = element.NeedsCompositeUpdate;
        element.ClearCompositeDirty();
        if (compositeChanged)
            node.MarkCompositeStyleDirty();
        node.UpdateCompositeState();

        var geometryChanged = element.Geometry != oldBounds;
        node.RefreshScrollState();
        var scrollChanged = oldMapsScroll != node.MapsChildScrollOffset ||
            (node.MapsChildScrollOffset && oldScrollOffset != node.ChildScrollOffset);

        // 几何改变也会改变 transform-origin 或子树 clip，必须覆盖上次提交的整棵子树。
        var emitSubtree = !ancestorEmitted && (compositeChanged || scrollChanged || geometryChanged);

        var childMatrix = node.ComputeChildMatrix(parentMatrix);
        var childAncestorEmitted = ancestorEmitted || emitSubtree;
        var childInsidePopup = insidePopupSubtree || element is IPopupElement;
        var subtreeEmitted = emitSubtree;
        foreach (var child in node.Children)
            subtreeEmitted |= UpdateWalk(child, childMatrix, childAncestorEmitted, childInsidePopup);

        node.FinishPrepare(parentMatrix, _fixedRootSet);
        node.MarkPrepared(_frameStamp, parentMatrix);

        // ---- damage 累积（client 屏幕空间；旧值 = 上次成功 Present 提交的包围盒） ----
        var isOpenPopup = element is IPopupElement { IsPopupOpen: true };
        if (!insidePopupSubtree)
        {
            if (emitSubtree)
            {
                Emit(PadAndSnap(Union(node.PresentedScreenSubtreeBounds, node.ScreenSubtreeBounds)));
            }
            else if (!ancestorEmitted && (geometryChanged || paintWasDirty))
            {
                if (isOpenPopup)
                {
                    // 弹出层内容由顶层 PaintPopup 整体重绘，以弹出层可视边界整体作为新旧 damage。
                    Emit(PadAndSnap(Union(node.PresentedScreenSubtreeBounds, node.ScreenSubtreeBounds)));
                }
                else if (partialRects is { Count: > 0 } && !geometryChanged)
                {
                    var origin = element.Geometry;
                    foreach (var local in partialRects)
                    {
                        var absolute = new Rect(
                            origin.X + local.X,
                            origin.Y + local.Y,
                            local.Width,
                            local.Height);
                        Emit(PadAndSnap(DrawCommandBounds.TransformBounds(absolute, node.ScreenTransform)));
                    }
                }
                else
                {
                    Emit(PadAndSnap(Union(node.PresentedScreenOwnBounds, node.ScreenOwnBounds)));
                }
                subtreeEmitted = true;
            }

            var popupBounds = DisplayNode.GetPopupVisualBounds(element);
            if (oldPopupBounds != popupBounds)
            {
                Emit(PadAndSnap(Union(oldPopupBounds, popupBounds)));
                subtreeEmitted = true;
            }
            node.PopupBounds = popupBounds;
            if ((subtreeEmitted || paintWasDirty) && isOpenPopup && !popupBounds.IsEmpty)
                Emit(PadAndSnap(popupBounds));
        }
        else
        {
            // 弹出层子树：节点级 damage 由弹出层 bounds 覆盖，仅维护缓存。
            node.PopupBounds = DisplayNode.GetPopupVisualBounds(element);
            subtreeEmitted |= paintWasDirty || compositeChanged || geometryChanged || scrollChanged;
        }

        return subtreeEmitted;
    }

    /// <summary>成功 Present 后由宿主调用：推进已提交的屏幕空间包围盒、清空累积 damage 并解除全帧要求。</summary>
    public void CommitPresentedFrame()
    {
        CommitPresentedBounds(_root);
        _rootSize = _root.Element?.Geometry.Size ?? _root.Bounds.Size;
        _pendingDamage.Clear();
        RequiresFullFrame = false;
    }

    private static void CommitPresentedBounds(DisplayNode node)
    {
        node.PresentedScreenSubtreeBounds = node.ScreenSubtreeBounds;
        node.PresentedScreenOwnBounds = node.ScreenOwnBounds;
        foreach (var child in node.Children)
            CommitPresentedBounds(child);
    }

    /// <summary>收集本帧需要重画的矩形（32×32 逻辑像素 tile 标记合并，client 屏幕空间，1px 外扩取整保守覆盖）。</summary>
    public List<Rect> CollectDirtyRects()
    {
        var bounds = Rect.Empty;
        foreach (var rect in _pendingDamage)
            bounds = Union(bounds, rect);
        if (bounds.IsEmpty) return [];
        // 无 client 边界：以 damage 包围盒外扩对齐的 tile 网格为界。
        var grid = AlignTileGrid(bounds);
        EnsureTileGrid(
            (int)MathF.Ceiling(grid.Width / TileSize),
            (int)MathF.Ceiling(grid.Height / TileSize));
        MarkTiles(grid);
        return ExtractTileRects(grid);
    }

    /// <summary>收集本帧需要重画的矩形，tile 全部 clamp 到 client 边界。</summary>
    public List<Rect> CollectDirtyRects(Size clientSize)
    {
        var client = new Rect(0, 0, clientSize.Width, clientSize.Height);
        if (client.IsEmpty || _pendingDamage.Count == 0) return [];
        EnsureTileGrid(
            (int)MathF.Ceiling(clientSize.Width / TileSize),
            (int)MathF.Ceiling(clientSize.Height / TileSize));
        MarkTiles(client);
        return ExtractTileRects(client);
    }

    private static Rect AlignTileGrid(Rect bounds)
    {
        var x0 = MathF.Floor(bounds.X / TileSize) * TileSize;
        var y0 = MathF.Floor(bounds.Y / TileSize) * TileSize;
        var right = MathF.Ceiling(bounds.Right / TileSize) * TileSize;
        var bottom = MathF.Ceiling(bounds.Bottom / TileSize) * TileSize;
        return new Rect(x0, y0, right - x0, bottom - y0);
    }

    private void EnsureTileGrid(int columns, int rows)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);
        if ((long)columns * rows > _tileMarks.Length)
            _tileMarks = new bool[columns * rows];
        _tileColumns = columns;
        _tileRows = rows;
        Array.Clear(_tileMarks, 0, columns * rows);
    }

    private void MarkTiles(Rect bounds)
    {
        foreach (var rect in _pendingDamage)
        {
            var clipped = Rect.Intersect(rect, bounds);
            if (clipped.IsEmpty) continue;
            // tile 索引相对网格原点计算（无 client 重载的网格原点可为负）。
            var x0 = (int)MathF.Floor((clipped.X - bounds.X) / TileSize);
            var y0 = (int)MathF.Floor((clipped.Y - bounds.Y) / TileSize);
            var x1 = (int)MathF.Ceiling((clipped.Right - bounds.X) / TileSize) - 1;
            var y1 = (int)MathF.Ceiling((clipped.Bottom - bounds.Y) / TileSize) - 1;
            if (x1 < x0 || y1 < y0) continue;
            if (x1 >= _tileColumns) x1 = _tileColumns - 1;
            if (y1 >= _tileRows) y1 = _tileRows - 1;
            for (var ty = y0; ty <= y1; ty++)
            {
                var rowBase = ty * _tileColumns;
                for (var tx = x0; tx <= x1; tx++)
                    _tileMarks[rowBase + tx] = true;
            }
        }
    }

    private List<Rect> ExtractTileRects(Rect bounds)
    {
        var result = new List<Rect>();
        _activeRuns.Clear();
        for (var ty = 0; ty < _tileRows; ty++)
        {
            _rowRuns.Clear();
            var rowBase = ty * _tileColumns;
            var tx = 0;
            while (tx < _tileColumns)
            {
                if (!_tileMarks[rowBase + tx])
                {
                    tx++;
                    continue;
                }
                var start = tx;
                while (tx < _tileColumns && _tileMarks[rowBase + tx]) tx++;
                _rowRuns.Add((start, tx - 1, false));
            }
            if (_rowRuns.Count == 0) continue;

            // 延续上一行完全相同的横向 run，否则冲刷成矩形；同 run 纵向合并保守覆盖整像素。
            for (var i = _activeRuns.Count - 1; i >= 0; i--)
            {
                var active = _activeRuns[i];
                var continued = false;
                for (var r = 0; r < _rowRuns.Count; r++)
                {
                    var rowRun = _rowRuns[r];
                    if (rowRun.Start != active.Start || rowRun.End != active.End) continue;
                    continued = true;
                    _rowRuns[r] = (rowRun.Start, rowRun.End, true);
                    break;
                }
                if (continued)
                    _activeRuns[i] = (active.Start, active.End, active.Row, active.Rows + 1);
                else
                {
                    FlushTileRun(active, bounds, result);
                    _activeRuns.RemoveAt(i);
                }
            }
            foreach (var rowRun in _rowRuns)
            {
                if (!rowRun.Continued)
                    _activeRuns.Add((rowRun.Start, rowRun.End, ty, 1));
            }
        }
        foreach (var active in _activeRuns)
            FlushTileRun(active, bounds, result);
        _activeRuns.Clear();
        return result;
    }

    private void FlushTileRun((int Start, int End, int Row, int Rows) run, Rect bounds, List<Rect> result)
    {
        var x = bounds.X + run.Start * TileSize;
        var y = bounds.Y + run.Row * TileSize;
        var width = Math.Min((run.End - run.Start + 1) * TileSize, bounds.Right - x);
        var height = Math.Min(run.Rows * TileSize, bounds.Bottom - y);
        if (width > 0 && height > 0)
            result.Add(new Rect(x, y, width, height));
    }

    private void RebuildLayerLists()
    {
        _fixedRoots.Clear();
        _fixedRootSet.Clear();
        _popups.Clear();
        CollectLayers(_root);
    }

    private void CollectLayers(DisplayNode node)
    {
        if (IsFixedRoot(node))
        {
            _fixedRoots.Add(node);
            _fixedRootSet.Add(node);
        }
        if (node.Element is IPopupElement popup)
            _popups.Add(popup);
        foreach (var child in node.Children)
            CollectLayers(child);
    }

    private static bool IsFixedRoot(DisplayNode node) =>
        node.Element != null && (node.Element.IsFixedPositioned() ||
            ElementLayoutStore.TryGet(node.Element, out var data) && data.IsFixedRoot);

    /// <summary>
    /// 合并相交/相邻脏矩形（简单 O(n²) 迭代合并；保留为公共 API，tile 收集路径不再使用）。
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

    private void Emit(Rect rect)
    {
        if (!float.IsFinite(rect.X) || !float.IsFinite(rect.Y) ||
            !float.IsFinite(rect.Width) || !float.IsFinite(rect.Height))
        {
            RequiresFullFrame = true;
            return;
        }
        if (!rect.IsEmpty)
            _pendingDamage.Add(rect);
    }

    /// <summary>外扩 1 逻辑像素并 snap 到整数像素，减少抗锯齿残影。</summary>
    private static Rect PadAndSnap(Rect g)
    {
        var x0 = MathF.Floor(g.X) - 1;
        var y0 = MathF.Floor(g.Y) - 1;
        var x1 = MathF.Ceiling(g.Right) + 1;
        var y1 = MathF.Ceiling(g.Bottom) + 1;
        return new Rect(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }

    /// <summary>渲染整棵显示树。</summary>
    public void Render(IRenderContext ctx) => Render(ctx, dirtyClip: null);

    /// <summary>
    /// 渲染显示树。<paramref name="dirtyClip"/> 为 client 屏幕空间裁剪区；仅重放与之相交的节点。
    /// 本方法不修改 damage/提交状态。
    /// </summary>
    public void Render(IRenderContext ctx, Rect? dirtyClip)
    {
        if (dirtyClip is { IsEmpty: true })
            return;
        RenderRegion(ctx, dirtyClip);
    }

    /// <summary>分别裁剪并渲染多个脏区，避免绘制它们之间未变化的区域。</summary>
    public void Render(IRenderContext ctx, IReadOnlyList<Rect> dirtyClips)
    {
        foreach (var dirtyClip in dirtyClips)
        {
            if (!dirtyClip.IsEmpty)
                RenderRegion(ctx, dirtyClip);
        }
    }

    private void RenderRegion(IRenderContext ctx, Rect? dirtyClip)
    {
        if (dirtyClip is { } clip)
        {
            ctx.PushClip(clip);
            RenderNormal(ctx, clip);
            RenderFixed(ctx, clip);
            RenderPopups(ctx, clip);
            ctx.PopClip();
        }
        else
        {
            RenderNormal(ctx, dirtyClip);
            RenderFixed(ctx, dirtyClip);
            RenderPopups(ctx, dirtyClip);
        }
    }

    private void RenderNormal(IRenderContext ctx, Rect? dirtyClip)
    {
        if (!_fixedRootSet.Contains(_root))
            _root.RenderPrepared(ctx, dirtyClip, _fixedRootSet, Matrix3x2.Identity, _frameStamp);
    }

    private void RenderFixed(IRenderContext ctx, Rect? dirtyClip)
    {
        foreach (var root in _fixedRoots)
            root.RenderPrepared(ctx, dirtyClip, _fixedRootSet, Matrix3x2.Identity, _frameStamp);
    }

    private void RenderPopups(IRenderContext ctx, Rect? dirtyClip)
    {
        foreach (var popup in _popups)
        {
            if (!popup.IsPopupOpen) continue;
            if (popup is Element { IsVisible: false } ||
                popup is Element hidden && (!hidden.IsCssDisplayed() || hidden.IsCssVisibilityHidden())) continue;
            var visualBounds = popup is Element element ? DisplayNode.GetPopupVisualBounds(element) : popup.PopupBounds;
            if (dirtyClip is { } clip && !visualBounds.IntersectsWith(clip)) continue;
            popup.PaintPopup(ctx);
        }
    }

    /// <summary>对所有打开的弹出层进行命中测试。</summary>
    public Element? HitTestPopups(Point point)
    {
        for (var i = _popups.Count - 1; i >= 0; i--)
        {
            if (_popups[i] is Element { IsVisible: false } ||
                _popups[i] is Element hidden && (!hidden.IsCssDisplayed() || hidden.IsCssVisibilityHidden())) continue;
            if (!_popups[i].IsPopupOpen) continue;
            var hit = _popups[i].HitTestPopup(point);
            if (hit != null) return hit;
        }
        return null;
    }

    /// <summary>对视口固定层进行命中测试。</summary>
    public Element? HitTestFixed(Point point)
    {
        for (var i = _fixedRoots.Count - 1; i >= 0; i--)
        {
            var hit = HitTestLayer(_fixedRoots[i], point, allowFixedRoot: true);
            if (hit != null) return hit;
        }
        return null;
    }

    /// <summary>对普通文档层进行命中测试，不包含固定层和顶层弹出层。</summary>
    public Element? HitTestRoot(Point point) => HitTestLayer(_root, point, allowFixedRoot: false);

    /// <summary>命中 UA scrollbar chrome；滚动条不作为文档树节点返回。</summary>
    public Element? HitTestScrollbar(Point point)
        => HitTestScrollbar(point, includeOwnedChrome: false);

    internal Element? HitTestOwnedScrollbar(Point point)
        => HitTestScrollbar(point, includeOwnedChrome: true);

    private Element? HitTestScrollbar(Point point, bool includeOwnedChrome)
    {
        for (var i = _popups.Count - 1; i >= 0; i--)
        {
            var popup = _popups[i];
            if (!IsVisibleOpenPopup(popup) || popup.HitTestPopup(point) == null) continue;
            return HitTestPopupScrollbar(popup, point, includeOwnedChrome: includeOwnedChrome);
        }
        for (var i = _fixedRoots.Count - 1; i >= 0; i--)
        {
            var root = _fixedRoots[i];
            var scrollbar = HitTestScrollbarLayer(root, point, allowFixedRoot: true, includeOwnedChrome: includeOwnedChrome);
            if (scrollbar != null) return scrollbar;
            if (HitTestLayer(root, point, allowFixedRoot: true) != null) return null;
        }
        return HitTestScrollbarLayer(_root, point, allowFixedRoot: false, includeOwnedChrome: includeOwnedChrome);
    }

    private static Element? HitTestPopupScrollbar(IPopupElement popup, Point point, bool includeOwnedChrome)
    {
        if (popup is not Element element || !popup.PopupBounds.Contains(point)) return null;
        var localPoint = popup.MapPointToContent(point);
        if (IsScrollbarHit(element, localPoint, includeOwnedChrome))
            return element;
        var childPoint = element.MapsScrollOffsetForChildren()
            ? new Point(localPoint.X + element.ScrollLeft, localPoint.Y + element.ScrollTop)
            : localPoint;
        foreach (var child in EnumerateChildrenTopmostFirst(element))
        {
            if (child.HitTest(childPoint) == null) continue;
            return HitTestScrollbarSubtree(child, childPoint, includeOwnedChrome: includeOwnedChrome);
        }
        return null;
    }

    private static IEnumerable<Element> EnumerateChildrenTopmostFirst(Element element) =>
        element.Children
            .Select((child, index) => (Child: child, Index: index))
            .OrderByDescending(item => item.Child.ZIndex)
            .ThenByDescending(item => item.Index)
            .Select(item => item.Child);

    private static Element? HitTestScrollbarSubtree(Element element, Point point, bool includeOwnedChrome)
    {
        if (!element.IsVisible || !element.IsCssDisplayed() ||
            element is IPopupElement) return null;
        if (IsScrollbarHit(element, point, includeOwnedChrome))
            return element;

        var overflowClip = element.GetOverflowClipRect();
        if (!overflowClip.IsEmpty && !overflowClip.Contains(point)) return null;
        var childPoint = element.MapsScrollOffsetForChildren()
            ? new Point(point.X + element.ScrollLeft, point.Y + element.ScrollTop)
            : point;
        foreach (var child in EnumerateChildrenTopmostFirst(element))
        {
            if (child.HitTest(childPoint) == null) continue;
            return HitTestScrollbarSubtree(child, childPoint, includeOwnedChrome: includeOwnedChrome);
        }
        return null;
    }

    private static bool IsScrollbarHit(Element element, Point point, bool includeOwnedChrome)
    {
        if (element.IsCssVisibilityHidden()) return false;
        if (element is ITextEditor { OwnsScrollbarChrome: true } editor)
            return includeOwnedChrome && editor.GetScrollbarPartAt(point) != ScrollbarPart.None;
        return element.GetScrollbarMetrics().HitTest(point) != ScrollbarPart.None;
    }

    private static bool IsVisibleOpenPopup(IPopupElement popup) =>
        popup.IsPopupOpen &&
        popup is not Element { IsVisible: false } &&
        (popup is not Element element || element.IsCssDisplayed() && !element.IsCssVisibilityHidden());

    private Element? HitTestScrollbarLayer(
        DisplayNode node, Point point, bool allowFixedRoot, bool includeOwnedChrome)
    {
        if (!allowFixedRoot && _fixedRootSet.Contains(node) || node.Element == null ||
            !node.Element.IsVisible || !node.Element.IsCssDisplayed() ||
            node.Element is IPopupElement { IsLayoutOverlay: true }) return null;

        var element = node.Element;
        if (IsScrollbarHit(element, point, includeOwnedChrome))
            return element;

        var overflowClip = element.GetOverflowClipRect();
        if (!overflowClip.IsEmpty && !overflowClip.Contains(point)) return null;
        var childPoint = element.MapsScrollOffsetForChildren()
            ? new Point(point.X + element.ScrollLeft, point.Y + element.ScrollTop)
            : point;
        for (var i = node.Children.Count - 1; i >= 0; i--)
        {
            var child = node.Children[i];
            var scrollbar = HitTestScrollbarLayer(child, childPoint, allowFixedRoot: false, includeOwnedChrome: includeOwnedChrome);
            if (scrollbar != null) return scrollbar;
            if (HitTestLayer(child, childPoint, allowFixedRoot: false) != null) return null;
        }
        return null;
    }

    private Element? HitTestLayer(DisplayNode node, Point point, bool allowFixedRoot)
    {
        if (!allowFixedRoot && _fixedRootSet.Contains(node) || node.Element == null || !node.Element.IsVisible ||
            !node.Element.IsCssDisplayed() || node.Element is IPopupElement { IsLayoutOverlay: true }) return null;

        var element = node.Element;
        var inside = element.Geometry.Contains(point);
        var overflowClip = element.GetOverflowClipRect();
        if (!overflowClip.IsEmpty && !overflowClip.Contains(point)) return null;

        var childPoint = element.MapsScrollOffsetForChildren()
            ? new Point(point.X + element.ScrollLeft, point.Y + element.ScrollTop)
            : point;
        for (var i = node.Children.Count - 1; i >= 0; i--)
        {
            var hit = HitTestLayer(node.Children[i], childPoint, allowFixedRoot: false);
            if (hit != null) return hit;
        }

        if (!inside || element.IsCssVisibilityHidden()) return null;
        return element is MenuSeparator ? null : element;
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
        // HTML mixed text remembers the DOM Text node it came from so selection ranges can be
        // built directly against the source node and its UTF-16 offsets.
        var sourceNode = command.Text.SourceNode;
        var sourceIdentity = sourceNode != null;
        if (command.Text.TryGetAuthoritativeSnapshot(out var authoritative))
        {
            var authoritativeCharacters = authoritative.Lines
                .SelectMany(line => line.Clusters)
                .Select(cluster =>
                {
                    var bounds = cluster.Bounds.Offset(command.Origin.X, command.Origin.Y);
                    var glyph = TextMetrics.GetGlyphMetrics(command.Text.Font, cluster.Rune).InkBounds;
                    var ink = new Rect(
                        bounds.X + glyph.X,
                        bounds.Y + TextMetrics.GetBaselineOffset(command.Text.Font, cluster.Bounds.Height) + glyph.Y,
                        glyph.Width,
                        glyph.Height);
                    var selection = ink.IsEmpty ? bounds : Rect.Union(bounds, ink);
                    return new TextCharacterFragment(
                        cluster.StartOffset,
                        cluster.EndOffset,
                        bounds,
                        selection)
                    {
                        Direction = cluster.Direction
                    };
                })
                .ToArray();
            if (authoritativeCharacters.Length == 0) return null;
            return new TextFragment(
                element,
                text,
                command.Text.Font,
                new Rect(command.Origin, authoritative.Size),
                authoritativeCharacters)
            {
                Layout = command.Text,
                LayoutOrigin = command.Origin,
                TextNode = sourceNode,
                TextNodeOffset = sourceIdentity ? command.Text.SourceOffset : 0,
                TextNodeCharOffsets = sourceIdentity ? command.Text.SourceCharOffsets : null,
                TextNodeLength = sourceIdentity ? command.Text.SourceLength : -1
            };
        }

        var lineHeight = TextMetrics.GetLineHeight(command.Text.Font, command.Text.LineHeight);
        var maxWidth = command.Text.MaxSize.Width;
        var characters = new List<TextCharacterFragment>();
        var lines = TextWrapping.Wrap(text, maxWidth, (offset, rune) =>
        {
            var advance = TextMetrics.GetGlyphMetrics(command.Text.Font, rune).AdvanceX;
            return advance;
        }, command.Text.WrappingOptions);
        var maxRight = command.Origin.X;

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var indent = command.Text.GetLineIndent(lineIndex);
            var x = command.Text.GetLineOriginX(command.Origin.X, lineIndex, line.Width);
            var y = command.Origin.Y + lineIndex * lineHeight;
            foreach (var visualRune in command.Text.EnumerateVisualRunes(line))
            {
                var startOffset = visualRune.StartOffset;
                var endOffset = visualRune.EndOffset;
                var advance = visualRune.Advance;
                var glyphBounds = TextMetrics.GetGlyphBoundsInLine(command.Text.Font, visualRune.Glyph, lineHeight);
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
                characters.Add(new TextCharacterFragment(startOffset, endOffset, bounds, selectionBounds)
                {
                    Direction = visualRune.Direction
                });
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
        return new TextFragment(element, text, command.Text.Font, boundsAll, characters)
        {
            Layout = command.Text,
            LayoutOrigin = command.Origin,
            TextNode = sourceNode,
            TextNodeOffset = sourceIdentity ? command.Text.SourceOffset : 0,
            TextNodeCharOffsets = sourceIdentity ? command.Text.SourceCharOffsets : null,
            TextNodeLength = sourceIdentity ? command.Text.SourceLength : -1
        };
    }

    private static float GetTextAlignmentOffset(TextLayout text, float lineWidth)
    {
        if (!float.IsFinite(text.MaxSize.Width) || text.MaxSize.Width <= lineWidth) return 0;
        return text.Alignment switch
        {
            TextAlignment.Center => (text.MaxSize.Width - lineWidth) / 2f,
            TextAlignment.Right => text.MaxSize.Width - lineWidth,
            _ => 0
        };
    }

    private static bool IsDescendantOrSelf(Element element, Element root)
    {
        for (var current = element; current != null; current = current.Parent)
            if (ReferenceEquals(current, root)) return true;
        return false;
    }
}
