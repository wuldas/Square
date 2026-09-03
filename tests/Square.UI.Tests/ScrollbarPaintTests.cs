using System.Numerics;
using Square.Controls;
using Square.Graphics;
using Square.Hosting;
using Square.Rendering.Paint;
using Square.Rendering.Tree;
using Square.UI.Scrolling;
using Xunit;

namespace Square.UI.Tests;

public sealed class ScrollbarPaintTests
{
    [Fact]
    public void ScrollbarIsPaintedAfterScrolledChildrenWithCustomThumbColor()
    {
        var scroller = new View { Geometry = new Rect(0, 0, 100, 100) };
        scroller.Style.Set("overflow-y", "auto");
        scroller.Style.Set("scrollbar-color", "#102030 #405060");
        scroller.SetScrollContentSize(new Size(100, 300));
        var child = new PaintedView { Geometry = new Rect(0, 0, 100, 300) };
        scroller.Children.Add(child);

        var node = new DisplayNode { Element = scroller };
        node.Children.Add(new DisplayNode { Element = child });
        var context = new RecordingRenderContext();

        node.Render(context);

        var thumbIndex = Assert.Single(context.Fills.FindAll(fill =>
            fill.Geometry is RoundedRectGeometry && fill.Color == Color.FromRgb(16, 32, 48))).Index;
        var contentIndex = Assert.Single(context.Fills.FindAll(fill =>
            fill.Geometry is RectGeometry && fill.Color == Color.Blue)).Index;
        Assert.True(contentIndex < thumbIndex);
    }

    [Fact]
    public void ScrollbarThumbUsesHoverStateColor()
    {
        var scroller = new View { Geometry = new Rect(0, 0, 100, 100) };
        scroller.Style.Set("overflow-y", "auto");
        scroller.SetScrollContentSize(new Size(100, 300));
        var metrics = scroller.GetScrollbarMetrics();
        var point = new Point(metrics.VerticalThumb.X + 2, metrics.VerticalThumb.Y + 2);
        var context = new RecordingRenderContext();
        var node = new DisplayNode { Element = scroller };

        node.Render(context);
        var normal = Assert.Single(context.Fills.FindAll(fill => fill.Geometry is RoundedRectGeometry)).Color;
        context.Fills.Clear();
        scroller.UpdateScrollbarHover(point);
        node.RebuildCommands();
        node.Render(context);
        var hovered = Assert.Single(context.Fills.FindAll(fill => fill.Geometry is RoundedRectGeometry)).Color;

        Assert.True(hovered.R > normal.R);
    }

    [Fact]
    public void ScrollbarColorAcceptsFunctionNotationWithSpaces()
    {
        var scroller = new View { Geometry = new Rect(0, 0, 100, 100) };
        scroller.Style.Set("overflow-y", "auto");
        scroller.Style.Set("scrollbar-color", "rgba(16, 32, 48, .5) #405060");
        scroller.SetScrollContentSize(new Size(100, 300));
        var node = new DisplayNode { Element = scroller };
        var context = new RecordingRenderContext();

        node.Render(context);

        Assert.Contains(context.Fills, fill =>
            fill.Geometry is RoundedRectGeometry && fill.Color == Color.FromRgba(16, 32, 48, 128));
    }

    [Fact]
    public void ScrollbarTrackUsesPressedStateColor()
    {
        var scroller = new View { Geometry = new Rect(0, 0, 100, 100) };
        scroller.Style.Set("overflow-y", "auto");
        scroller.SetScrollContentSize(new Size(100, 400));
        var metrics = scroller.GetScrollbarMetrics();
        var point = new Point(metrics.VerticalTrack.X + 2, metrics.VerticalTrack.Y + 2);
        var context = new RecordingRenderContext();
        var node = new DisplayNode { Element = scroller };

        node.Render(context);
        var normal = Assert.Single(context.Fills.FindAll(fill => fill.Geometry is RectGeometry)).Color;
        context.Fills.Clear();
        Assert.Equal(ScrollbarPart.VerticalTrack, scroller.StartScrollbarInteraction(point));
        node.RebuildCommands();
        node.Render(context);
        var pressed = Assert.Single(context.Fills.FindAll(fill => fill.Geometry is RectGeometry)).Color;

        Assert.True(pressed.R < normal.R);
    }

    [Fact]
    public void PressingHoveredThumbInvalidatesPaintForPressedChrome()
    {
        var scroller = new View { Geometry = new Rect(0, 0, 100, 100) };
        scroller.Style.Set("overflow-y", "auto");
        scroller.SetScrollContentSize(new Size(100, 400));
        var metrics = scroller.GetScrollbarMetrics();
        var node = new DisplayNode { Element = scroller };
        var context = new RecordingRenderContext();

        node.Render(context);
        scroller.UpdateScrollbarHover(metrics.VerticalThumb.Center);
        node.RebuildCommands();
        node.Render(context);
        scroller.ClearPaintDirty();

        Assert.Equal(ScrollbarPart.VerticalThumb,
            scroller.StartScrollbarInteraction(metrics.VerticalThumb.Center));

        Assert.True(scroller.NeedsPaint);
    }

    [Fact]
    public void FocusedTextAreaStillPaintsScrollbarChrome()
    {
        var textArea = new TextArea { Geometry = new Rect(0, 0, 100, 100) };
        textArea.Style.Set("appearance", "auto");
        textArea.Style.Set("overflow-y", "auto");
        textArea.SetScrollContentSize(new Size(100, 300));
        textArea.SetState(ElementState.Focus, true);
        var node = new DisplayNode { Element = textArea };
        var context = new RecordingRenderContext();

        node.Render(context);

        Assert.Contains(context.Fills, fill => fill.Geometry is RoundedRectGeometry);
    }

    [Fact]
    public void TextAreaScrollTopSynchronizesTextScrollState()
    {
        var textArea = new TextArea
        {
            Geometry = new Rect(0, 0, 100, 100),
            Value = string.Join("\n", Enumerable.Range(0, 30).Select(static i => $"line-{i}"))
        };
        textArea.Style.Set("overflow-y", "auto");
        textArea.SetScrollContentSize(new Size(100, 600));
        textArea.ScrollTop = 80;
        var node = new DisplayNode { Element = textArea };
        node.Render(new RecordingRenderContext());
        var field = typeof(TextEditorBase).GetField(
            "_verticalScroll", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);

        Assert.Equal(80, (float)field!.GetValue(textArea)!);
    }

    [Fact]
    public void TextAreaPaintClipExcludesDesktopScrollbarGutter()
    {
        var textArea = new TextArea
        {
            Geometry = new Rect(0, 0, 100, 100),
            Value = string.Join("\n", Enumerable.Range(0, 30).Select(static i => $"line-{i}"))
        };
        textArea.Style.Set("appearance", "auto");
        textArea.Style.Set("overflow-y", "auto");
        textArea.SetScrollContentSize(new Size(100, 600));
        var context = new RecordingRenderContext();

        new DisplayNode { Element = textArea }.Render(context);

        Assert.Contains(new Rect(1, 1, 84, 98), context.ClipRects);
    }

    [Fact]
    public void MultilineTextAreaReportsIntrinsicScrollExtentOnPaint()
    {
        var textArea = new TextArea
        {
            Geometry = new Rect(0, 0, 100, 60),
            Value = string.Join("\n", Enumerable.Range(0, 20).Select(static i => $"line-{i}"))
        };
        textArea.Style.Set("overflow-y", "auto");

        new Rendering.LayoutEngine().MeasureAndArrange(textArea, new Size(100, 60));
        new DisplayNode { Element = textArea }.Render(new RecordingRenderContext());

        var metrics = textArea.GetScrollbarMetrics();
        Assert.True(metrics.HasVertical, $"extent={metrics.MaxScrollY}");
        Assert.True(metrics.MaxScrollY > 0, $"extent={metrics.MaxScrollY}");
    }

    [Fact]
    public void MultilineTextAreaReportsWrappedLongLineExtentOnPaint()
    {
        var textArea = new TextArea
        {
            Geometry = new Rect(0, 0, 100, 60),
            Value = new string('x', 600)
        };
        textArea.Style.Set("overflow-y", "auto");

        new DisplayNode { Element = textArea }.Render(new RecordingRenderContext());

        var metrics = textArea.GetScrollbarMetrics();
        Assert.True(metrics.HasVertical, $"extent={metrics.MaxScrollY}");
        Assert.True(metrics.MaxScrollY > 0, $"extent={metrics.MaxScrollY}");
    }

    [Fact]
    public void MultilineTextAreaShrinksIntrinsicScrollExtentAfterValueShortens()
    {
        var textArea = new TextArea
        {
            Geometry = new Rect(0, 0, 100, 60),
            Value = string.Join("\n", Enumerable.Range(0, 20).Select(static i => $"line-{i}"))
        };
        textArea.Style.Set("overflow-y", "auto");
        var node = new DisplayNode { Element = textArea };

        node.Render(new RecordingRenderContext());
        Assert.True(textArea.GetScrollbarMetrics().HasVertical);

        textArea.Value = "short";
        node.RebuildCommands();
        node.Render(new RecordingRenderContext());

        var finalMetrics = textArea.GetScrollbarMetrics();
        Assert.False(finalMetrics.HasVertical,
            $"extent={finalMetrics.MaxScrollY}");
    }

    [Fact]
    public void TextAreaHorizontalScrollSurvivesPaintWhenEnabled()
    {
        var textArea = new TextArea
        {
            Geometry = new Rect(0, 0, 100, 100),
            Value = string.Join("\n", Enumerable.Range(0, 10).Select(static _ => new string('x', 80)))
        };
        textArea.Style.Set("overflow", "auto");
        textArea.SetScrollContentSize(new Size(600, 100));
        textArea.ScrollLeft = 80;
        var field = typeof(TextEditorBase).GetField(
            "_horizontalScroll", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        new DisplayNode { Element = textArea }.Render(new RecordingRenderContext());

        Assert.Equal(80, (float)field!.GetValue(textArea)!);
    }

    [Fact]
    public void TextAreaEditorLayoutWidthExcludesDesktopScrollbarGutter()
    {
        var textArea = new TextArea { Geometry = new Rect(0, 0, 100, 100), Value = "wrapped text" };
        textArea.Style.Set("overflow-y", "auto");
        textArea.SetScrollContentSize(new Size(100, 600));
        var method = typeof(TextEditorBase).GetMethod(
            "CreateEditorTextLayout", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);

        var layout = Assert.IsType<TextLayout>(method!.Invoke(textArea, [textArea.Value, 13f, 16f]));

        Assert.Equal(66, layout.MaxSize.Width);
    }

    [Fact]
    public void FocusedTextAreaCaretAutoScrollUpdatesSharedScrollTop()
    {
        var textArea = new TextArea
        {
            Geometry = new Rect(0, 0, 100, 100),
            Value = string.Join("\n", Enumerable.Range(0, 40).Select(static i => $"line-{i}"))
        };
        textArea.Style.Set("overflow-y", "auto");
        textArea.SetScrollContentSize(new Size(100, 800));
        textArea.Focus();
        textArea.SelectAll();
        new DisplayNode { Element = textArea }.Render(new RecordingRenderContext());

        var vertical = typeof(TextEditorBase).GetField(
            "_verticalScroll", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(vertical);
        Assert.True(textArea.ScrollTop > 0,
            $"focused={textArea.IsFocused}, state={textArea.State}, public={textArea.ScrollTop}, private={(float)vertical!.GetValue(textArea)!}, content={textArea.ScrollContentSize}");
    }

    [Fact]
    public void TextAreaStableBothEdgesUsesLeadingGutterForCaret()
    {
        var textArea = new TextArea { Geometry = new Rect(0, 0, 100, 100), Value = "text" };
        textArea.Style.Set("overflow-y", "auto");
        textArea.Style.Set("scrollbar-gutter", "stable both-edges");
        textArea.SetScrollContentSize(new Size(100, 600));

        Assert.True(textArea.CaretRect.X >= 15);
    }

    [Fact]
    public void PopupRootScrollbarIsPaintedAfterScrolledChildren()
    {
        var popup = new Popup { Geometry = new Rect(0, 0, 100, 100) };
        popup.Style.Set("overflow-y", "auto");
        popup.SetScrollContentSize(new Size(100, 400));
        popup.Children.Add(new View { Geometry = new Rect(0, 0, 100, 400) });
        popup.Open();
        var metrics = popup.GetScrollbarMetrics();
        var context = new RecordingRenderContext();

        popup.PaintPopup(context);

        Assert.Contains(context.Fills, fill =>
            fill.Geometry is RoundedRectGeometry rounded && rounded.Rect == metrics.VerticalThumb);
    }

    [Fact]
    public void PopupRootContentClipExcludesDesktopScrollbarGutter()
    {
        var popup = new Popup { Geometry = new Rect(0, 0, 100, 100) };
        popup.Style.Set("overflow-y", "auto");
        popup.SetScrollContentSize(new Size(100, 400));
        popup.Children.Add(new View { Geometry = new Rect(0, 0, 100, 400) });
        popup.Open();
        var context = new RecordingRenderContext();

        popup.PaintPopup(context);

        Assert.Contains(new Rect(0, 0, 85, 100), context.ClipRects);
    }

    [Fact]
    public void PressingVerticalThumbDoesNotChangeHorizontalThumbColor()
    {
        var scroller = new View { Geometry = new Rect(0, 0, 100, 100) };
        scroller.Style.Set("overflow", "auto");
        scroller.SetScrollContentSize(new Size(300, 300));
        var metrics = scroller.GetScrollbarMetrics();
        var node = new DisplayNode { Element = scroller };
        var context = new RecordingRenderContext();

        node.Render(context);
        var normalVertical = Assert.Single(context.Fills, fill =>
            fill.Geometry is RoundedRectGeometry rounded && rounded.Rect == metrics.VerticalThumb).Color;
        var normalHorizontal = Assert.Single(context.Fills, fill =>
            fill.Geometry is RoundedRectGeometry rounded && rounded.Rect == metrics.HorizontalThumb).Color;
        context.Fills.Clear();

        Assert.Equal(ScrollbarPart.VerticalThumb,
            scroller.StartScrollbarInteraction(metrics.VerticalThumb.Center));
        node.RebuildCommands();
        node.Render(context);

        var pressedVertical = Assert.Single(context.Fills, fill =>
            fill.Geometry is RoundedRectGeometry rounded && rounded.Rect == metrics.VerticalThumb).Color;
        var unpressedHorizontal = Assert.Single(context.Fills, fill =>
            fill.Geometry is RoundedRectGeometry rounded && rounded.Rect == metrics.HorizontalThumb).Color;
        Assert.NotEqual(normalVertical, pressedVertical);
        Assert.Equal(normalHorizontal, unpressedHorizontal);
    }

    [Fact]
    public void PressingVerticalTrackDoesNotChangeHorizontalTrackOrCornerColor()
    {
        var scroller = new View { Geometry = new Rect(0, 0, 100, 100) };
        scroller.Style.Set("overflow", "auto");
        scroller.SetScrollContentSize(new Size(300, 300));
        var metrics = scroller.GetScrollbarMetrics();
        var verticalTrackPaint = InsetTrack(metrics.VerticalGutter);
        var horizontalTrackPaint = InsetTrack(metrics.HorizontalGutter);
        var cornerPaint = InsetTrack(metrics.Corner);
        var node = new DisplayNode { Element = scroller };
        var context = new RecordingRenderContext();

        node.Render(context);
        var normalVertical = FindRectColor(context, verticalTrackPaint);
        var normalHorizontal = FindRectColor(context, horizontalTrackPaint);
        var normalCorner = FindRectColor(context, cornerPaint);
        context.Fills.Clear();
        var trackPoint = new Point(metrics.VerticalTrack.Center.X, metrics.VerticalThumb.Bottom + 1);

        Assert.Equal(ScrollbarPart.VerticalTrack, scroller.StartScrollbarInteraction(trackPoint));
        node.RebuildCommands();
        node.Render(context);

        Assert.NotEqual(normalVertical, FindRectColor(context, verticalTrackPaint));
        Assert.Equal(normalHorizontal, FindRectColor(context, horizontalTrackPaint));
        Assert.Equal(normalCorner, FindRectColor(context, cornerPaint));
    }

    [Fact]
    public void PressingOneScrollbarButtonDoesNotChangeOtherButtonColors()
    {
        var scroller = new View { Geometry = new Rect(0, 0, 100, 100) };
        scroller.Style.Set("overflow", "auto");
        scroller.SetScrollContentSize(new Size(300, 300));
        var metrics = scroller.GetScrollbarMetrics();
        var context = new RecordingRenderContext();

        ScrollbarPainter.Paint(context, metrics, Color.FromRgb(128, 128, 128), Color.FromRgb(128, 128, 128), Color.FromRgb(100, 100, 100));
        var normal = context.PathFillColors.ToArray();
        Assert.Equal(4, normal.Length);
        context.PathFillColors.Clear();
        context.FilledPaths.Clear();

        ScrollbarPainter.Paint(
            context,
            metrics,
            Color.FromRgb(128, 128, 128),
            Color.FromRgb(128, 128, 128),
            Color.FromRgb(100, 100, 100),
            pressedPart: ScrollbarPart.VerticalBackButton);

        Assert.Equal(4, context.PathFillColors.Count);
        Assert.NotEqual(normal[0], context.PathFillColors[0]);
        Assert.Equal(normal[1], context.PathFillColors[1]);
        Assert.Equal(normal[2], context.PathFillColors[2]);
        Assert.Equal(normal[3], context.PathFillColors[3]);
    }

    [Fact]
    public void MobileScrollbarPaintsOnlyFourDipOverlayThumb()
    {
        var window = new AppWindow("mobile-scrollbar")
        {
            ScrollbarProfile = ScrollbarDeviceProfile.Mobile
        };
        var scroller = new View { Geometry = new Rect(0, 0, 100, 100) };
        window.Load(scroller);
        scroller.Style.Set("overflow-y", "auto");
        scroller.SetScrollContentSize(new Size(100, 300));
        scroller.ScrollTop = 50;
        var node = new DisplayNode { Element = scroller };
        var context = new RecordingRenderContext();

        node.Render(context);

        var thumb = Assert.Single(context.Fills.FindAll(fill => fill.Geometry is RoundedRectGeometry));
        var geometry = Assert.IsType<RoundedRectGeometry>(thumb.Geometry);
        Assert.Equal(4, geometry.Rect.Width);
        Assert.Equal(Color.FromRgba(128, 128, 128, 128), thumb.Color);
        Assert.DoesNotContain(context.Fills, fill => fill.Geometry is RectGeometry);
    }

    [Fact]
    public void StableGutterDoesNotPaintScrollbarWithoutOverflow()
    {
        var scroller = new View { Geometry = new Rect(0, 0, 100, 100) };
        scroller.Style.Set("overflow-y", "auto");
        scroller.Style.Set("scrollbar-gutter", "stable");
        scroller.SetScrollContentSize(new Size(85, 100));
        var node = new DisplayNode { Element = scroller };
        var context = new RecordingRenderContext();

        node.Render(context);

        Assert.DoesNotContain(context.Fills, fill => fill.Geometry is RoundedRectGeometry);
        Assert.Equal(0, context.PathFillCount);
    }

    [Fact]
    public void DesktopScrollbarPaintsRoundedButtonGlyphs()
    {
        var scroller = new View { Geometry = new Rect(0, 0, 100, 100) };
        scroller.Style.Set("overflow-y", "auto");
        scroller.SetScrollContentSize(new Size(85, 300));
        var node = new DisplayNode { Element = scroller };
        var context = new RecordingRenderContext();

        node.Render(context);

        Assert.Equal(2, context.FilledPaths.Count);
        Assert.All(context.FilledPaths, path =>
            Assert.Equal(3, path.Commands.Count(command => command is ArcToCmd)));
    }

    [Fact]
    public void MobileFadeOpacityAffectsPaintAndEventuallyRemovesThumb()
    {
        var window = new AppWindow("mobile-scrollbar-fade")
        {
            ScrollbarProfile = ScrollbarDeviceProfile.Mobile
        };
        var scroller = new View { Geometry = new Rect(0, 0, 100, 100) };
        window.Load(scroller);
        scroller.Style.Set("overflow-y", "auto");
        scroller.SetScrollContentSize(new Size(100, 300));
        scroller.ScrollTop = 50;
        scroller.AdvanceScrollbarFade(0.6f);
        var node = new DisplayNode { Element = scroller };
        var context = new RecordingRenderContext();

        node.Render(context);

        var faded = Assert.Single(context.Fills, fill => fill.Geometry is RoundedRectGeometry);
        Assert.InRange(faded.Color.A, (byte)63, (byte)65);

        scroller.AdvanceScrollbarFade(0.1f);
        node.RebuildCommands();
        context.Fills.Clear();
        node.Render(context);
        Assert.DoesNotContain(context.Fills, fill => fill.Geometry is RoundedRectGeometry);
    }

    private static Rect InsetTrack(Rect rect) =>
        new(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);

    private static Color FindRectColor(RecordingRenderContext context, Rect rect) =>
        Assert.Single(context.Fills, fill => fill.Geometry is RectGeometry geometry && geometry.Rect == rect).Color;

    private sealed class PaintedView : View
    {
        public override void Paint(IRenderContext context) =>
            context.FillRect(Geometry, Brush.FromColor(Color.Blue));
    }

    private sealed class RecordingRenderContext : IRenderContext
    {
        public readonly List<FillRecord> Fills = [];
        public readonly List<Rect> ClipRects = [];
        public readonly List<PathGeometry> FilledPaths = [];
        public readonly List<Color> PathFillColors = [];
        public int PathFillCount { get; private set; }
        public Size CanvasSize => new(100, 100);
        public float DpiScale => 1;
        public void PushTransform(Matrix3x2 matrix) { }
        public void PopTransform() { }
        public void PushClip(Rect rect) => ClipRects.Add(rect);
        public void PushClip(Geometry geometry) { }
        public void PopClip() { }
        public void FillRect(Rect rect, Brush brush) => Record(new RectGeometry(rect), brush);
        public void DrawRect(Rect rect, Pen pen) { }
        public void FillPath(PathGeometry path, Brush brush)
        {
            PathFillCount++;
            FilledPaths.Add(path);
            if (brush is SolidColorBrush solid) PathFillColors.Add(solid.Color);
        }
        public void DrawPath(PathGeometry path, Pen pen) { }
        public void FillGeometry(Geometry geometry, Brush brush) => Record(geometry, brush);
        public void DrawGeometry(Geometry geometry, Pen pen) { }
        public void DrawText(TextLayout text, Point origin, Brush brush) { }
        public void DrawImage(Square.Graphics.Image image, Rect dest, Rect? source = null) { }
        public void PushLayer(Rect bounds, float opacity) { }
        public void PopLayer() { }
        public void Clear(Color color) { }
        public void Clear(Color color, Rect rect) { }
        public void Flush() { }
        public void Present() { }
        public void Present(IReadOnlyList<Rect>? dirtyRects) { }
        public void Dispose() { }

        private void Record(Geometry geometry, Brush brush)
        {
            if (brush is SolidColorBrush solid)
                Fills.Add(new FillRecord(geometry, solid.Color, Fills.Count));
        }
    }

    private readonly record struct FillRecord(Geometry Geometry, Color Color, int Index);
}
