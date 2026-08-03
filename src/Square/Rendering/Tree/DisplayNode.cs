using Square.Graphics;
using Square.UI;
using System.Numerics;
using Square.Rendering.Commands;

namespace Square.Rendering.Tree;

/// <summary>显示节点：对应一个文档元素，承载其绘制命令与子树结构。</summary>
public sealed class DisplayNode
{
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
    /// <summary>本节点的绘制命令列表。</summary>
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
    public void Render(IRenderContext ctx, Rect? dirtyClip)
    {
        // 使用最新 Geometry 作为 Bounds（局部 Present 依赖）
        if (Element != null)
            Bounds = Element.Geometry;

        if (IsDirty || Commands.Count == 0)
        {
            RebuildCommands();
        }

        var visualBounds = VisualBounds.IsEmpty ? Bounds : VisualBounds;
        if (dirtyClip == null || visualBounds.IntersectsWith(dirtyClip.Value))
            ExecuteCommands(ctx);

        // Popup-hosted children are replayed later by DisplayTree's top-level popup layer.
        if (Element is IPopupElement)
            return;

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
            child.Render(ctx, childDirtyClip);
        if (scrollsChildren) ctx.PopTransform();
        if (clipsChildren) ctx.PopClip();
    }

    internal void RebuildCommands()
    {
        if (Element != null)
            Bounds = Element.Geometry;
        Commands.Clear();
        CollectCommands(Element, Commands);
        VisualBounds = DrawCommandBounds.Calculate(Commands, Bounds);
        SortChildrenByZIndex();
        // Clear before Paint so a frame callback can invalidate/request the next frame
        // without that new dirty state being erased after command collection.
        IsDirty = false;
    }

    private static void CollectCommands(Element? element, List<DrawCommand> commands)
    {
        if (element == null || !element.IsVisible) return;
        element.ClearPaintDirty();
        var collector = new CommandCollector(commands);
        if (element is not IPopupElement && BoxShadow.TryParseList(element.Style.GetPropertyValue("box-shadow"), out var shadows))
            BoxShadowRendering.Draw(collector, element.Geometry, GetCornerRadius(element), shadows);
        element.Paint(collector);
    }

    private static float GetCornerRadius(Element element)
    {
        var raw = element.Style.GetPropertyValue("border-radius");
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        var token = raw.Trim().Split([' ', '/'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token)) return 0;
        var max = MathF.Max(0, MathF.Min(element.Geometry.Width, element.Geometry.Height) / 2f);
        if (token.EndsWith('%') && float.TryParse(token[..^1], out var percent))
            return Math.Clamp(max * percent / 100f, 0, max);
        if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase)) token = token[..^2];
        return float.TryParse(token, out var pixels) ? Math.Clamp(pixels, 0, max) : 0;
    }

    private void SortChildrenByZIndex()
    {
        if (Children.Count < 2) return;
        Children.Sort(static (left, right) =>
            (left.Element?.ZIndex ?? 0).CompareTo(right.Element?.ZIndex ?? 0));
    }

    private void ExecuteCommands(IRenderContext ctx)
    {
        foreach (var cmd in Commands)
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
