using Square.CSS.Properties;
using Square.Graphics;
using Square.UI;
using System.Numerics;
using System.Globalization;
using Square.Rendering.Commands;
using Square.Rendering.Paint;

namespace Square.Rendering.Tree;

/// <summary>显示节点：对应一个文档元素，承载其绘制命令与子树结构。</summary>
public sealed class DisplayNode
{
    private readonly List<DrawCommand> _beforeContentCommands = [];
    private readonly List<DrawCommand> _afterContentCommands = [];
    private readonly List<DrawCommand> _afterChildrenCommands = [];

    /// <summary>节点布局边界。</summary>
    public Rect Bounds { get; set; }
    /// <summary>Source document element for this display node.</summary>
    public Element? Source
    {
        get => Element;
        set => Element = value;
    }

    /// <summary>关联的文档元素。</summary>
    public Element? Element { get; set; }
    /// <summary>子节点列表。</summary>
    public List<DisplayNode> Children { get; } = [];
    /// <summary>本节点的元素内容绘制命令列表，不含 CSS 框与后代。</summary>
    public List<DrawCommand> Commands { get; } = [];
    /// <summary>本节点及其绘制命令的可视边界。</summary>
    public Rect VisualBounds { get; private set; }
    /// <summary>最近一次构建绘制命令时应用到子树的滚动偏移。</summary>
    internal Point ChildScrollOffset { get; private set; }
    /// <summary>最近一次构建绘制命令时是否对孩子映射了滚动偏移。</summary>
    internal bool MapsChildScrollOffset { get; private set; }
    /// <summary>弹出层可视边界。</summary>
    public Rect PopupBounds { get; set; }

    /// <summary>是否需要重建命令并重绘。</summary>
    public bool IsDirty { get; set; } = true;

    // ---- 复合视觉状态缓存（transform/opacity 每次样式变化只解析一次，渲染复用） ----
    private Matrix3x2 _compositeLocalTransform = Matrix3x2.Identity;
    private bool _compositeStyleDirty = true;
    /// <summary>节点是否有非恒等 CSS transform（结果矩阵见 <see cref="CompositeTransform"/>）。</summary>
    internal bool HasCompositeTransform { get; private set; }
    /// <summary>绕布局盒中心的完整 transform 矩阵（局部 → 父空间）。</summary>
    internal Matrix3x2 CompositeTransform { get; private set; } = Matrix3x2.Identity;
    /// <summary>节点是否带组透明度（≠1 的 clamp 后 opacity）。</summary>
    internal bool HasCompositeOpacity { get; private set; }
    internal float CompositeOpacity { get; private set; } = 1f;

    // ---- 每帧屏幕空间状态（client 逻辑像素；与渲染使用同一累计矩阵链） ----
    private Rect _innerLocalBounds;
    /// <summary>最近一次准备的帧戳（DisplayTree.UpdateDirty 递增；独立渲染入口用 -1 强制重新准备）。</summary>
    internal long PreparedStamp { get; private set; } = long.MinValue;
    /// <summary>最近一次准备时父级传入的累计矩阵（局部 → client 屏幕）。</summary>
    internal Matrix3x2 PreparedMatrix { get; private set; }
    /// <summary>本节点局部坐标 → client 屏幕空间的累计矩阵（含自身 transform 与祖先 scroll/transform）。</summary>
    internal Matrix3x2 ScreenTransform { get; private set; } = Matrix3x2.Identity;
    /// <summary>自身可视边界经 <see cref="ScreenTransform"/> 后的屏幕空间包围盒。</summary>
    internal Rect ScreenOwnBounds { get; private set; }
    /// <summary>自身 ∪ 裁剪后子树的屏幕空间包围盒（含自身 transform）。</summary>
    internal Rect ScreenSubtreeBounds { get; private set; }
    /// <summary>
    /// PushLayer 使用的层包围盒（父空间 AABB，已含自身 transform 对子树内容的变换；
    /// 弹出层附加的 <c>PopupBounds</c> 不并入——弹出层表面由顶层 PaintPopup 在层外重绘）。
    /// </summary>
    internal Rect LayerBounds { get; private set; }
    /// <summary>上次成功 Present 提交的子树屏幕包围盒（damage 的“旧”侧；仅 CommitPresentedFrame 推进）。</summary>
    internal Rect PresentedScreenSubtreeBounds { get; set; }
    internal Rect PresentedScreenOwnBounds { get; set; }

    /// <summary>渲染本节点及子树。</summary>
    public void Render(IRenderContext ctx) => Render(ctx, dirtyClip: null);

    /// <summary>
    /// 渲染本节点及子树。<paramref name="dirtyClip"/> 非 null 时作为屏幕空间裁剪区应用。
    /// </summary>
    public void Render(IRenderContext ctx, Rect? dirtyClip) => Render(ctx, dirtyClip, null);

    internal void Render(IRenderContext ctx, Rect? dirtyClip, IReadOnlySet<DisplayNode>? excludedRoots) =>
        RenderPrepared(ctx, dirtyClip, excludedRoots, Matrix3x2.Identity, stamp: -1);

    /// <summary>按需准备本节点及子树的屏幕空间状态（独立于 DisplayTree 帧流程的懒路径）。</summary>
    internal void EnsurePrepared(long stamp, in Matrix3x2 parentMatrix, IReadOnlySet<DisplayNode>? excludedRoots)
    {
        if (PreparedStamp == stamp && PreparedMatrix == parentMatrix) return;
        if (Element?.IsCssDisplayed() == false)
        {
            ResetPreparedState(parentMatrix);
            MarkPrepared(stamp, parentMatrix);
            return;
        }
        if (Element != null) Bounds = Element.Geometry;
        if (IsDirty) RebuildCommands();
        UpdateCompositeState();
        RefreshScrollState();
        var childMatrix = ComputeChildMatrix(parentMatrix);
        foreach (var child in Children)
        {
            if (excludedRoots?.Contains(child) == true) continue;
            child.EnsurePrepared(stamp, childMatrix, excludedRoots);
        }
        FinishPrepare(parentMatrix, excludedRoots);
        MarkPrepared(stamp, parentMatrix);
    }

    /// <summary>
    /// 渲染本节点及子树。<paramref name="screenClip"/> 为 client 屏幕空间裁剪区，沿子树原样传递；
    /// 节点剔除用累计矩阵变换后的屏幕空间包围盒，不再做局部滚动平移假设。
    /// </summary>
    internal void RenderPrepared(
        IRenderContext ctx,
        Rect? screenClip,
        IReadOnlySet<DisplayNode>? excludedRoots,
        in Matrix3x2 parentMatrix,
        long stamp)
    {
        if (Element?.IsCssDisplayed() == false) return;
        if (PreparedStamp != stamp || PreparedMatrix != parentMatrix)
            EnsurePrepared(stamp, parentMatrix, excludedRoots);
        RenderPreparedCore(ctx, screenClip, excludedRoots, parentMatrix);
    }

    private void RenderPreparedCore(
        IRenderContext ctx,
        Rect? screenClip,
        IReadOnlySet<DisplayNode>? excludedRoots,
        in Matrix3x2 parentMatrix)
    {
        if (screenClip is { } subtreeClip &&
            (ScreenSubtreeBounds.IsEmpty || !ScreenSubtreeBounds.IntersectsWith(subtreeClip))) return;
        var paintsNode = Element?.IsCssVisibilityHidden() != true &&
            (screenClip == null || ScreenOwnBounds.IntersectsWith(screenClip.Value));
        var wrapsOpacity = HasCompositeOpacity;
        var wrapsTransform = HasCompositeTransform;
        if (wrapsOpacity && paintsNode && ctx is ISolidLeafOpacityRenderer simple &&
            TryGetSingleSolidFill(out var fill) && IsPixelAlignedFill(fill.Rect, ctx.DpiScale))
        {
            if (wrapsTransform) ctx.PushTransform(CompositeTransform);
            var filled = simple.TryFillSolidLeafOpacity(fill.Rect, ((SolidColorBrush)fill.Brush).Color, CompositeOpacity);
            if (wrapsTransform) ctx.PopTransform();
            if (filled) return;
        }
        if (wrapsOpacity) ctx.PushLayer(LayerBounds, CompositeOpacity);
        if (wrapsTransform) ctx.PushTransform(CompositeTransform);

        if (paintsNode)
        {
            ExecuteCommands(ctx, _beforeContentCommands);
            ExecuteCommands(ctx, Commands);
            ExecuteCommands(ctx, _afterContentCommands);
        }

        // Popup-hosted children are replayed later by DisplayTree's top-level popup layer.
        if (Element is IPopupElement)
        {
            if (paintsNode) ExecuteCommands(ctx, _afterChildrenCommands);
            if (wrapsTransform) ctx.PopTransform();
            if (wrapsOpacity) ctx.PopLayer();
            return;
        }

        var overflowClip = Element?.GetOverflowClipRect() ?? Rect.Empty;
        var clipsChildren = !overflowClip.IsEmpty;
        if (clipsChildren) ctx.PushClip(overflowClip);
        var scrollsChildren = Element?.MapsScrollOffsetForChildren() == true;
        var scrollOffset = Element?.ScrollOffset ?? default;
        if (scrollsChildren) ctx.PushTransform(Matrix3x2.CreateTranslation(-scrollOffset.X, -scrollOffset.Y));
        var childMatrix = ComputeChildMatrix(parentMatrix);
        foreach (var child in Children)
        {
            if (excludedRoots?.Contains(child) == true) continue;
            // 屏幕空间裁剪区原样下传：剔除用孩子自身缓存的屏幕空间包围盒。
            child.RenderPrepared(ctx, screenClip, excludedRoots, childMatrix, PreparedStamp);
        }
        if (scrollsChildren) ctx.PopTransform();
        if (clipsChildren) ctx.PopClip();
        if (paintsNode) ExecuteCommands(ctx, _afterChildrenCommands);
        if (wrapsTransform) ctx.PopTransform();
        if (wrapsOpacity) ctx.PopLayer();
    }

    /// <summary>计算孩子局部坐标 → 屏幕空间的累计矩阵（与渲染压栈顺序一致：自身 transform 在内，滚动平移在外）。</summary>
    internal Matrix3x2 ComputeChildMatrix(in Matrix3x2 parentMatrix)
    {
        var matrix = HasCompositeTransform ? CompositeTransform * parentMatrix : parentMatrix;
        if (Element?.MapsScrollOffsetForChildren() == true)
        {
            var scroll = Element.ScrollOffset;
            matrix = Matrix3x2.CreateTranslation(-scroll.X, -scroll.Y) * matrix;
        }
        return matrix;
    }

    /// <summary>标记复合样式需要重新解析（DisplayTree 在元素 NeedsCompositeUpdate 或节点新建时调用）。</summary>
    internal void MarkCompositeStyleDirty() => _compositeStyleDirty = true;

    /// <summary>
    /// 解析并缓存 transform/opacity（样式串只在标记脏时解析一次），
    /// 随后按当前 Bounds 重算最终矩阵（transform-origin 依赖布局盒，几何变化后必须刷新）。
    /// </summary>
    internal void UpdateCompositeState()
    {
        if (_compositeStyleDirty)
        {
            _compositeStyleDirty = false;
            if (TryGetTransform(out var local) && !local.IsIdentity)
            {
                _compositeLocalTransform = local;
                HasCompositeTransform = true;
            }
            else
            {
                _compositeLocalTransform = Matrix3x2.Identity;
                HasCompositeTransform = false;
            }

            if (TryGetOpacity(out var opacity))
            {
                opacity = Math.Clamp(opacity, 0f, 1f);
                HasCompositeOpacity = opacity != 1f;
                CompositeOpacity = opacity;
            }
            else
            {
                HasCompositeOpacity = false;
                CompositeOpacity = 1f;
            }
        }

        if (HasCompositeTransform)
        {
            if (Bounds.IsEmpty)
            {
                CompositeTransform = _compositeLocalTransform;
            }
            else
            {
                var originX = Bounds.X + Bounds.Width / 2f;
                var originY = Bounds.Y + Bounds.Height / 2f;
                CompositeTransform = Matrix3x2.CreateTranslation(-originX, -originY) * _compositeLocalTransform *
                    Matrix3x2.CreateTranslation(originX, originY);
            }
        }
        else
        {
            CompositeTransform = Matrix3x2.Identity;
        }
    }

    /// <summary>
    /// 读取本地 transform：赢得级联的动画数值 overlay 直接建矩阵，
    /// 否则按 <see cref="CssTransformParser"/> 既有语义读字符串（含失败→无变换语义）。
    /// </summary>
    private bool TryGetTransform(out Matrix3x2 local)
    {
        local = Matrix3x2.Identity;
        var style = Element?.Style;
        if (style == null) return false;
        if (style.TryGetEffectiveAnimatedNumeric("transform", out var animated) &&
            TryCreateAnimatedTransform(animated, out local))
            return true;
        var transformValue = style.Get("transform");
        return !string.IsNullOrWhiteSpace(transformValue) && CssTransformParser.TryParse(transformValue, out local);
    }

    /// <summary>读取 opacity 数值：动画数值 overlay 直接供值，否则按既有字符串解析语义。</summary>
    private bool TryGetOpacity(out float opacity)
    {
        opacity = 0f;
        var style = Element?.Style;
        if (style == null) return false;
        if (style.TryGetEffectiveAnimatedNumeric("opacity", out var animated) &&
            animated.Prefix.Length == 0 && animated.Suffix.Length == 0)
        {
            opacity = animated.Value;
            return float.IsFinite(opacity);
        }
        return style.Get("opacity") is { } opacityValue &&
            float.TryParse(opacityValue, NumberStyles.Float, CultureInfo.InvariantCulture, out opacity) &&
            float.IsFinite(opacity);
    }

    // 与 CssTransformParser.TryCreateFunction 的单参数语义逐项一致；未知函数/非法单位返回 false
    // 回退字符串路径（解析失败→无变换的语义由字符串路径原样提供）。
    private static bool TryCreateAnimatedTransform(Square.UI.ElementApi.AnimatedNumericValue animated, out Matrix3x2 matrix)
    {
        matrix = Matrix3x2.Identity;
        var value = animated.Value;
        // 插值上溢可能产生 NaN/±∞；字符串路径的数字扫描无法解析非有限值（等价"解析失败→无变换"），
        // 快路径必须一致拒绝并回退字符串路径，否则会把 NaN 矩阵推进渲染管线。
        if (!float.IsFinite(value)) return false;
        if (!animated.Prefix.EndsWith('(') || !animated.Suffix.EndsWith(')')) return false;
        var name = animated.Prefix[..^1];
        var unit = animated.Suffix[..^1];
        switch (name)
        {
            case "translate":
            case "translatex":
                if (unit is not ("" or "px")) return false;
                matrix = Matrix3x2.CreateTranslation(value, 0);
                return true;
            case "translatey":
                if (unit is not ("" or "px")) return false;
                matrix = Matrix3x2.CreateTranslation(0, value);
                return true;
            case "scale":
                if (unit.Length != 0) return false;
                matrix = Matrix3x2.CreateScale(value, value);
                return true;
            case "scalex":
                if (unit.Length != 0) return false;
                matrix = Matrix3x2.CreateScale(value, 1);
                return true;
            case "scaley":
                if (unit.Length != 0) return false;
                matrix = Matrix3x2.CreateScale(1, value);
                return true;
            case "rotate":
                if (unit is not ("" or "deg" or "rad" or "grad" or "turn")) return false;
                matrix = Matrix3x2.CreateRotation(ToRadians(value, unit));
                return true;
            case "skew":
            case "skewx":
                if (unit is not ("" or "deg" or "rad" or "grad" or "turn")) return false;
                matrix = Matrix3x2.CreateSkew(ToRadians(value, unit), 0);
                return true;
            case "skewy":
                if (unit is not ("" or "deg" or "rad" or "grad" or "turn")) return false;
                matrix = Matrix3x2.CreateSkew(0, ToRadians(value, unit));
                return true;
            default:
                return false;
        }
    }

    private static float ToRadians(float value, string unit) => unit switch
    {
        "rad" => value,
        "grad" => value * MathF.PI / 200f,
        "turn" => value * 2f * MathF.PI,
        _ => value * MathF.PI / 180f
    };

    /// <summary>刷新滚动映射缓存（damage 检测用；渲染始终读取元素实时滚动偏移）。</summary>
    internal void RefreshScrollState()
    {
        MapsChildScrollOffset = Element?.MapsScrollOffsetForChildren() == true;
        ChildScrollOffset = MapsChildScrollOffset ? Element!.ScrollOffset : default;
    }

    private bool TryGetSingleSolidFill(out FillRectCommand fill)
    {
        fill = null!;
        if (Children.Count != 0 || Element is IPopupElement ||
            !(Element?.GetOverflowClipRect() ?? Rect.Empty).IsEmpty ||
            _beforeContentCommands.Count + Commands.Count + _afterContentCommands.Count + _afterChildrenCommands.Count != 1)
            return false;
        var command = _beforeContentCommands.Count != 0 ? _beforeContentCommands[0] :
            Commands.Count != 0 ? Commands[0] : _afterContentCommands.Count != 0 ?
            _afterContentCommands[0] : _afterChildrenCommands[0];
        if (command is not FillRectCommand { Brush: SolidColorBrush } solid) return false;
        fill = solid;
        return true;
    }

    private bool IsPixelAlignedFill(Rect rect, float dpi)
    {
        if (ScreenTransform.M12 != 0 || ScreenTransform.M21 != 0) return false;
        var screen = DrawCommandBounds.TransformBounds(rect, ScreenTransform);
        return PixelAligned(screen.Left * dpi) && PixelAligned(screen.Top * dpi) &&
            PixelAligned(screen.Right * dpi) && PixelAligned(screen.Bottom * dpi);
    }

    private static bool PixelAligned(float value) => float.IsFinite(value) && value == MathF.Round(value);

    internal void RebuildCommands()
    {
        if (Element != null)
            Bounds = Element.Geometry;
        _beforeContentCommands.Clear();
        Commands.Clear();
        _afterContentCommands.Clear();
        _afterChildrenCommands.Clear();
        CollectCommands(
            Element,
            _beforeContentCommands,
            Commands,
            _afterContentCommands,
            _afterChildrenCommands);
        var beforeBounds = DrawCommandBounds.Calculate(_beforeContentCommands, Bounds, fallbackWhenEmpty: false);
        var contentBounds = DrawCommandBounds.Calculate(Commands, Bounds, fallbackWhenEmpty: false);
        var afterContentBounds = DrawCommandBounds.Calculate(_afterContentCommands, Bounds, fallbackWhenEmpty: false);
        var afterChildrenBounds = DrawCommandBounds.Calculate(_afterChildrenCommands, Bounds, fallbackWhenEmpty: false);
        VisualBounds = Union(Union(beforeBounds, contentBounds), Union(afterContentBounds, afterChildrenBounds));
        if (VisualBounds.IsEmpty) VisualBounds = Bounds;
        MapsChildScrollOffset = Element?.MapsScrollOffsetForChildren() == true;
        ChildScrollOffset = MapsChildScrollOffset ? Element!.ScrollOffset : default;
        SortChildrenByZIndex();
        // Clear before Paint so a frame callback can invalidate/request the next frame
        // without that new dirty state being erased after command collection.
        IsDirty = false;
    }

    private static void CollectCommands(
        Element? element,
        List<DrawCommand> beforeContent,
        List<DrawCommand> content,
        List<DrawCommand> afterContent,
        List<DrawCommand> afterChildren)
    {
        if (element == null || !element.IsVisible || !element.IsCssDisplayed()) return;
        element.ClearPaintDirty();
        if (element.IsCssVisibilityHidden()) return;
        CssBoxPainter.PaintBeforeContent(new CommandCollector(beforeContent), element);
        element.Paint(new CommandCollector(content));
        CssBoxPainter.PaintAfterContent(new CommandCollector(afterContent), element);
        CssBoxPainter.PaintAfterChildren(new CommandCollector(afterChildren), element);
    }

    /// <summary>子树准备完成后由 DisplayTree 调用：孩子已准备，本轮聚合包围盒并缓存屏幕空间状态。</summary>
    internal void FinishPrepare(in Matrix3x2 parentMatrix, IReadOnlySet<DisplayNode>? excludedRoots)
    {
        if (Element?.IsCssDisplayed() == false)
        {
            ResetPreparedState(parentMatrix);
            return;
        }
        if (Element != null) Bounds = Element.Geometry;

        var own = VisualBounds.IsEmpty ? Bounds : VisualBounds;
        var bounds = own;
        if (Element is not IPopupElement)
        {
            var overflowClip = Element?.GetOverflowClipRect() ?? Rect.Empty;
            var scrollsChildren = Element?.MapsScrollOffsetForChildren() == true;
            var scrollOffset = Element?.ScrollOffset ?? default;
            foreach (var child in Children)
            {
                if (excludedRoots?.Contains(child) == true) continue;
                var childBounds = child._innerLocalBounds;
                if (!childBounds.IsEmpty && child.HasCompositeTransform)
                    childBounds = DrawCommandBounds.TransformBounds(childBounds, child.CompositeTransform);
                if (scrollsChildren)
                    childBounds = Translate(childBounds, -scrollOffset.X, -scrollOffset.Y);
                if (!overflowClip.IsEmpty && !childBounds.IsEmpty)
                    childBounds = Rect.Intersect(childBounds, overflowClip);
                bounds = Union(bounds, childBounds);
            }
        }
        _innerLocalBounds = bounds;
        ScreenTransform = HasCompositeTransform ? CompositeTransform * parentMatrix : parentMatrix;
        ScreenOwnBounds = DrawCommandBounds.TransformBounds(own, ScreenTransform);
        ScreenSubtreeBounds = DrawCommandBounds.TransformBounds(bounds, ScreenTransform);
        // PushLayer 会再应用当前祖先矩阵：这里仅包含节点自身 transform，
        // 不可传屏幕空间 AABB，否则父级平移/滚动/旋转会被应用两次。
        LayerBounds = HasCompositeTransform
            ? DrawCommandBounds.TransformBounds(bounds, CompositeTransform)
            : bounds;
        if (Element is IPopupElement { IsPopupOpen: true })
        {
            var popupBounds = GetPopupVisualBounds(Element);
            if (!popupBounds.IsEmpty)
            {
                ScreenOwnBounds = Union(ScreenOwnBounds, popupBounds);
                ScreenSubtreeBounds = Union(ScreenSubtreeBounds, popupBounds);
                // LayerBounds 不并入 PopupBounds：层只包裹节点自身命令，
                // 弹出层内容由顶层 PaintPopup 在本层之外重绘。
            }
        }
    }

    private void ResetPreparedState(in Matrix3x2 parentMatrix)
    {
        _innerLocalBounds = Rect.Empty;
        ScreenTransform = parentMatrix;
        ScreenOwnBounds = Rect.Empty;
        ScreenSubtreeBounds = Rect.Empty;
        LayerBounds = Rect.Empty;
    }

    internal void MarkPrepared(long stamp, in Matrix3x2 parentMatrix)
    {
        PreparedStamp = stamp;
        PreparedMatrix = parentMatrix;
    }

    private void SortChildrenByZIndex()
    {
        if (Children.Count < 2) return;
        Children.Sort(static (left, right) =>
            (left.Element?.ZIndex ?? 0).CompareTo(right.Element?.ZIndex ?? 0));
    }

    internal static Rect GetPopupVisualBounds(Element? element)
    {
        if (element is not IPopupElement { IsPopupOpen: true } popup) return Rect.Empty;
        var bounds = popup.PopupBounds;
        return BoxShadow.TryParseList(element.Style.Get("box-shadow"), out var shadows)
            ? BoxShadowRendering.GetVisualBounds(bounds, shadows)
            : bounds;
    }

    private static Rect Translate(Rect rect, float x, float y) => rect.IsEmpty
        ? rect
        : new Rect(rect.X + x, rect.Y + y, rect.Width, rect.Height);

    private static Rect Union(Rect left, Rect right)
    {
        if (left.IsEmpty) return right;
        if (right.IsEmpty) return left;
        return Rect.Union(left, right);
    }

    private static void ExecuteCommands(IRenderContext ctx, IReadOnlyList<DrawCommand> commands)
    {
        foreach (var cmd in commands)
        {
            ExecuteCommand(ctx, cmd);
        }
    }

    private static void ExecuteCommand(IRenderContext ctx, DrawCommand cmd)
    {
        switch (cmd)
        {
            case ClearCommand c: ctx.Clear(c.Color); break;
            case FillRectCommand f: ctx.FillRect(f.Rect, f.Brush); break;
            case DrawRectCommand d: ctx.DrawRect(d.Rect, d.Pen); break;
            case FillPathCommand f: ctx.FillPath(f.Path, f.Brush); break;
            case DrawPathCommand d: ctx.DrawPath(d.Path, d.Pen); break;
            case FillGeometryCommand f: ctx.FillGeometry(f.Geometry, f.Brush); break;
            case DrawGeometryCommand d: ctx.DrawGeometry(d.Geometry, d.Pen); break;
            case DrawTextCommand t: ctx.DrawText(t.Text, t.Origin, t.Brush); break;
            case DrawImageCommand i: ctx.DrawImage(i.Image, i.Dest, i.Source); break;
            case PushClipCommand p: ctx.PushClip(p.Rect); break;
            case PushGeometryClipCommand p: ctx.PushClip(p.Geometry); break;
            case PopClipCommand: ctx.PopClip(); break;
            case PushTransformCommand pt: ctx.PushTransform(pt.Matrix); break;
            case PopTransformCommand: ctx.PopTransform(); break;
            case PushLayerCommand p: ctx.PushLayer(p.Bounds, p.Opacity); break;
            case PopLayerCommand: ctx.PopLayer(); break;
        }
    }
}

internal sealed class CommandCollector : IRenderContext
{
    private readonly List<DrawCommand> _commands;
    private Size _canvasSize = new(1920, 1080);

    public CommandCollector(List<DrawCommand> commands) { _commands = commands; }

    public Size CanvasSize => _canvasSize;
    public float DpiScale => 1f;

    public void PushTransform(Matrix3x2 matrix) => _commands.Add(new PushTransformCommand(matrix));
    public void PopTransform() => _commands.Add(new PopTransformCommand());
    public void PushClip(Rect rect) => _commands.Add(new PushClipCommand(rect));
    public void PushClip(Geometry geometry) => _commands.Add(new PushGeometryClipCommand(geometry));
    public void PopClip() => _commands.Add(new PopClipCommand());
    public void FillRect(Rect rect, Brush brush) => _commands.Add(new FillRectCommand(rect, brush));
    public void DrawRect(Rect rect, Pen pen) => _commands.Add(new DrawRectCommand(rect, pen));
    public void FillPath(PathGeometry path, Brush brush) => _commands.Add(new FillPathCommand(path, brush));
    public void DrawPath(PathGeometry path, Pen pen) => _commands.Add(new DrawPathCommand(path, pen));
    public void FillGeometry(Geometry geometry, Brush brush) => _commands.Add(new FillGeometryCommand(geometry, brush));
    public void DrawGeometry(Geometry geometry, Pen pen) => _commands.Add(new DrawGeometryCommand(geometry, pen));
    public void DrawText(TextLayout text, Point origin, Brush brush) => _commands.Add(new DrawTextCommand(text, origin, brush));
    public void DrawImage(Image image, Rect dest, Rect? source = null) => _commands.Add(new DrawImageCommand(image, dest, source));
    public void PushLayer(Rect bounds, float opacity) => _commands.Add(new PushLayerCommand(bounds, opacity));
    public void PopLayer() => _commands.Add(new PopLayerCommand());
    public void Clear(Color color) => _commands.Add(new ClearCommand(color));
    public void Clear(Color color, Rect rect) => _commands.Add(new FillRectCommand(rect, new SolidColorBrush(color)));
    public void Flush() { }
    public void Present() { }
    public void Present(IReadOnlyList<Rect>? dirtyRects) { }
    public void Dispose() { }
}
