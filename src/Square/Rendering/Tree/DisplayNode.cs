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
    private Rect _subtreeVisualBounds;

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
    /// <summary>弹出层可视边界。</summary>
    public Rect PopupBounds { get; set; }

    /// <summary>是否需要重建命令并重绘。</summary>
    public bool IsDirty { get; set; } = true;

    /// <summary>渲染本节点及子树。</summary>
    public void Render(IRenderContext ctx) => Render(ctx, dirtyClip: null);

    /// <summary>
    /// 渲染本节点及子树。<paramref name="dirtyClip"/> 非 null 时由 DisplayTree 作为真实裁剪区应用。
    /// </summary>
    public void Render(IRenderContext ctx, Rect? dirtyClip) => Render(ctx, dirtyClip, null);

    internal void Render(IRenderContext ctx, Rect? dirtyClip, IReadOnlySet<DisplayNode>? excludedRoots)
    {
        if (Element?.IsCssDisplayed() == false) return;
        PrepareSubtreeVisualBounds(excludedRoots);
        RenderPrepared(ctx, dirtyClip, excludedRoots);
    }

    private void RenderPrepared(IRenderContext ctx, Rect? dirtyClip, IReadOnlySet<DisplayNode>? excludedRoots)
    {
        if (dirtyClip is { } subtreeClip && !_subtreeVisualBounds.IntersectsWith(subtreeClip)) return;
        var wrapsOpacity = TryGetOpacity(out var opacity);
        if (wrapsOpacity) ctx.PushLayer(_subtreeVisualBounds, opacity);

        var visualBounds = VisualBounds.IsEmpty ? Bounds : VisualBounds;
        var paintsNode = Element?.IsCssVisibilityHidden() != true &&
            (dirtyClip == null || visualBounds.IntersectsWith(dirtyClip.Value));
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
            if (wrapsOpacity) ctx.PopLayer();
            return;
        }

        var overflowClip = Element?.GetOverflowClipRect() ?? Rect.Empty;
        var clipsChildren = !overflowClip.IsEmpty;
        if (clipsChildren) ctx.PushClip(overflowClip);
        var scrollOffset = Element?.ScrollOffset ?? default;
        var scrollsChildren = Element?.MapsScrollOffsetForChildren() == true;
        if (scrollsChildren) ctx.PushTransform(Matrix3x2.CreateTranslation(-scrollOffset.X, -scrollOffset.Y));
        var childDirtyClip = scrollsChildren && dirtyClip is { } clip
            ? new Rect(clip.X + scrollOffset.X, clip.Y + scrollOffset.Y, clip.Width, clip.Height)
            : dirtyClip;
        foreach (var child in Children)
        {
            if (excludedRoots?.Contains(child) == true) continue;
            child.RenderPrepared(ctx, childDirtyClip, excludedRoots);
        }
        if (scrollsChildren) ctx.PopTransform();
        if (clipsChildren) ctx.PopClip();
        if (paintsNode) ExecuteCommands(ctx, _afterChildrenCommands);
        if (wrapsOpacity) ctx.PopLayer();
    }

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

    private void SortChildrenByZIndex()
    {
        if (Children.Count < 2) return;
        Children.Sort(static (left, right) =>
            (left.Element?.ZIndex ?? 0).CompareTo(right.Element?.ZIndex ?? 0));
    }

    private Rect PrepareSubtreeVisualBounds(IReadOnlySet<DisplayNode>? excludedRoots)
    {
        if (Element?.IsCssDisplayed() == false) return _subtreeVisualBounds = Rect.Empty;
        if (Element != null) Bounds = Element.Geometry;
        if (IsDirty) RebuildCommands();

        var bounds = VisualBounds.IsEmpty ? Bounds : VisualBounds;
        if (Element is IPopupElement) return _subtreeVisualBounds = bounds;

        var overflowClip = Element?.GetOverflowClipRect() ?? Rect.Empty;
        var scrollOffset = Element?.ScrollOffset ?? default;
        var scrollsChildren = Element?.MapsScrollOffsetForChildren() == true;
        foreach (var child in Children)
        {
            if (excludedRoots?.Contains(child) == true) continue;
            var childBounds = child.PrepareSubtreeVisualBounds(excludedRoots);
            if (scrollsChildren)
                childBounds = Translate(childBounds, -scrollOffset.X, -scrollOffset.Y);
            if (!overflowClip.IsEmpty)
                childBounds = Rect.Intersect(childBounds, overflowClip);
            bounds = Union(bounds, childBounds);
        }
        return _subtreeVisualBounds = bounds;
    }

    private bool TryGetOpacity(out float opacity)
    {
        opacity = 1f;
        var value = Element?.Style.Get("opacity");
        return value != null &&
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out opacity) &&
            float.IsFinite(opacity) &&
            (opacity = Math.Clamp(opacity, 0f, 1f)) != 1f;
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
