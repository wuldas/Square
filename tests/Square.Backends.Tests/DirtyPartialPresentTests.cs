using System;
using System.Collections.Generic;
using Square.Backends;
using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Square.Rendering.Tree;
using Square.UI;
using System.Numerics;
using Xunit;

namespace Square.Backends.Tests;

public class DirtyPartialPresentTests
{
    [Fact]
    public void PaintInvalidationRaisedDuringCommandCollectionIsPreserved()
    {
        var element = new ReinvalidatingElement();
        element.ClearPaintDirty();
        element.InvalidatePaint();
        var node = new DisplayNode { Element = element };

        node.RebuildCommands();

        Assert.True(element.NeedsPaint);
    }

    [Fact]
    public void ClearRectOnlyTouchesPixelsInsideRect()
    {
        var bmp = new Bitmap(10, 10);
        var ctx = new RenderContext(bmp, 1f);
        ctx.Clear(Color.Black);
        ctx.Clear(Color.White, new Rect(2, 2, 3, 3));

        // Outside remains black (BGRA: B=0,G=0,R=0,A=255)
        Assert.Equal(0, bmp.Pixels[0]);
        Assert.Equal(255, bmp.Pixels[3]);

        // Inside (2,2) is white premultiplied
        var idx = 2 * bmp.Stride + 2 * 4;
        Assert.Equal(255, bmp.Pixels[idx]);     // B
        Assert.Equal(255, bmp.Pixels[idx + 1]); // G
        Assert.Equal(255, bmp.Pixels[idx + 2]); // R
        Assert.Equal(255, bmp.Pixels[idx + 3]); // A
    }

    [Fact]
    public void ClearRectAtFractionalDpiDoesNotEscapeMatchingClip()
    {
        var bmp = new Bitmap(12, 12);
        var ctx = new RenderContext(bmp, new Size(8, 8), 1.5f);
        ctx.Clear(Color.Black);

        var dirty = new Rect(1, 1, 4, 4);
        ctx.Clear(Color.White, dirty);
        ctx.PushClip(dirty);
        ctx.FillRect(new Rect(0, 0, 8, 8), new SolidColorBrush(Color.Red));
        ctx.PopClip();

        var outside = 3 * bmp.Stride + 7 * 4;
        Assert.Equal(0, bmp.Pixels[outside]);
        Assert.Equal(0, bmp.Pixels[outside + 1]);
        Assert.Equal(0, bmp.Pixels[outside + 2]);
        Assert.Equal(255, bmp.Pixels[outside + 3]);
    }

    [Fact]
    public void PresentWithDirtyRectsForwardsListToHandler()
    {
        IReadOnlyList<Rect>? received = null;
        Bitmap? frame = null;
        var bmp = new Bitmap(8, 8);
        var ctx = new RenderContext(bmp, 1f, (bitmap, dirty) =>
        {
            frame = bitmap;
            received = dirty;
        });

        var rects = new List<Rect> { new(1, 2, 3, 4) };
        ctx.Present(rects);

        Assert.Same(bmp, frame);
        Assert.NotNull(received);
        Assert.Single(received!);
        Assert.Equal(new Rect(1, 2, 3, 4), received![0]);
    }

    [Fact]
    public void PresentEmptyDirtyListIsNoOp()
    {
        var calls = 0;
        var bmp = new Bitmap(4, 4);
        var ctx = new RenderContext(bmp, 1f, (_, _) => calls++);
        ctx.Present(Array.Empty<Rect>());
        Assert.Equal(0, calls);
    }

    [Fact]
    public void PresentNullDirtyMeansFullWindow()
    {
        var calls = 0;
        IReadOnlyList<Rect>? received = new List<Rect>(); // sentinel non-null
        var bmp = new Bitmap(4, 4);
        var ctx = new RenderContext(bmp, 1f, (_, dirty) =>
        {
            calls++;
            received = dirty;
        });
        ctx.Present();
        Assert.Equal(1, calls);
        Assert.Null(received);
    }

    [Fact]
    public void DisplayTreeCollectDirtyRectsIncludesNeedsPaintGeometry()
    {
        var root = new View { Geometry = new Rect(0, 0, 200, 200) };
        var staticChild = new View { Geometry = new Rect(0, 0, 50, 50) };
        var canvas = new Canvas { Geometry = new Rect(40, 60, 80, 100) };
        root.Children.Add(staticChild);
        root.Children.Add(canvas);

        var tree = new DisplayTree();
        tree.BuildFrom(root);
        // Clear paint dirty from build path
        staticChild.ClearPaintDirty();
        canvas.ClearPaintDirty();
        tree.UpdateDirty();

        canvas.InvalidatePaint();
        tree.UpdateDirty();
        var dirty = tree.CollectDirtyRects();

        Assert.NotEmpty(dirty);
        // Canvas geometry should be covered by some dirty rect (with pad)
        Assert.Contains(dirty, r =>
            r.X <= canvas.Geometry.X &&
            r.Y <= canvas.Geometry.Y &&
            r.Right >= canvas.Geometry.Right &&
            r.Bottom >= canvas.Geometry.Bottom);
    }

    [Fact]
    public void DisplayTreeMergeUnionsOverlappingRects()
    {
        var a = new Rect(0, 0, 10, 10);
        var b = new Rect(5, 5, 10, 10);
        var merged = DisplayTree.MergeDirtyRects([a, b]);
        Assert.Single(merged);
        var u = merged[0];
        Assert.Equal(0, u.X);
        Assert.Equal(0, u.Y);
        Assert.Equal(15, u.Width);
        Assert.Equal(15, u.Height);
    }

    [Fact]
    public void DisplayTreeGeometryChangeDirtiesOldAndNewBounds()
    {
        var root = new View { Geometry = new Rect(0, 0, 200, 200) };
        var child = new View { Geometry = new Rect(10, 20, 30, 40) };
        root.Children.Add(child);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        root.ClearPaintDirty();
        child.ClearPaintDirty();
        tree.Render(new RenderContext(new Bitmap(200, 200), 1f));

        child.Geometry = new Rect(100, 120, 30, 40);
        tree.UpdateDirty();
        var dirty = tree.CollectDirtyRects();

        Assert.Contains(dirty, r =>
            r.X <= 10 && r.Y <= 20 &&
            r.Right >= 130 && r.Bottom >= 160);
    }

    [Fact]
    public void DisplayTreeSynchronizationReusesUnchangedNodesAndBuildsNewNodes()
    {
        var root = new View { Geometry = new Rect(0, 0, 100, 100) };
        var existing = new CountingPaintElement { Geometry = new Rect(0, 0, 20, 20) };
        root.Children.Add(existing);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        var existingPaintCount = existing.PaintCount;

        var added = new CountingPaintElement { Geometry = new Rect(20, 0, 20, 20) };
        root.Children.Add(added);
        tree.Synchronize(root);

        Assert.Equal(existingPaintCount, existing.PaintCount);
        Assert.Equal(1, added.PaintCount);
    }

    [Fact]
    public void DisplayTreeSynchronizationPreservesDomOrderForEqualZIndex()
    {
        var root = new View { Geometry = new Rect(0, 0, 20, 20) };
        var red = new ColorPaintElement(Color.Red) { Geometry = root.Geometry };
        var blue = new ColorPaintElement(Color.Blue) { Geometry = root.Geometry };
        root.Children.Add(red);
        root.Children.Add(blue);
        var tree = new DisplayTree();
        tree.BuildFrom(root);

        root.Children.Move(1, 0);
        tree.Synchronize(root);
        var bitmap = new Bitmap(20, 20);
        tree.Render(new RenderContext(bitmap, 1f));

        var pixel = 10 * bitmap.Stride + 10 * 4;
        Assert.Equal(0, bitmap.Pixels[pixel]);
        Assert.Equal(0, bitmap.Pixels[pixel + 1]);
        Assert.Equal(255, bitmap.Pixels[pixel + 2]);
        Assert.Equal(255, bitmap.Pixels[pixel + 3]);
    }

    [Fact]
    public void DirtyRectsRemainAvailableAfterDisplayTreeRender()
    {
        var root = new View { Geometry = new Rect(0, 0, 100, 100) };
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        root.ClearPaintDirty();
        tree.Render(new RenderContext(new Bitmap(100, 100), 1f));

        root.InvalidatePaint();
        tree.UpdateDirty();
        var dirty = tree.CollectDirtyRects();
        tree.Render(new RenderContext(new Bitmap(100, 100), 1f), dirty[0]);

        Assert.Single(dirty);
    }

    [Fact]
    public void DisplayTreeDirtyRectsUseVisualBoundsWhenTextExceedsGeometry()
    {
        var root = new View { Geometry = new Rect(0, 0, 260, 80) };
        var text = new Square.Controls.Text("This text is wider than geometry")
        {
            Geometry = new Rect(10, 10, 20, 24)
        };
        root.Children.Add(text);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        root.ClearPaintDirty();
        text.ClearPaintDirty();
        tree.Render(new RenderContext(new Bitmap(260, 80), 1f));

        text.InvalidatePaint();
        tree.UpdateDirty();
        var dirty = tree.CollectDirtyRects();

        Assert.Contains(dirty, r => r.Bottom > text.Geometry.Bottom + 40);
    }

    [Fact]
    public void DisplayTreeDirtyRectsUsePathVisualBoundsOutsideGeometry()
    {
        var root = new View { Geometry = new Rect(0, 0, 160, 80) };
        var element = new PathPaintElement
        {
            Geometry = new Rect(0, 0, 10, 10)
        };
        root.Children.Add(element);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        root.ClearPaintDirty();
        element.ClearPaintDirty();
        tree.Render(new RenderContext(new Bitmap(160, 80), 1f));

        element.InvalidatePaint();
        tree.UpdateDirty();
        var dirty = tree.CollectDirtyRects();

        Assert.Contains(dirty, r => r.Right >= 90 && r.Bottom >= 30);
    }

    [Fact]
    public void DisplayTreeVisualBoundsRespectPushClip()
    {
        var root = new View { Geometry = new Rect(0, 0, 220, 80) };
        var element = new ClippedPaintElement
        {
            Geometry = new Rect(0, 0, 10, 10)
        };
        root.Children.Add(element);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        root.ClearPaintDirty();
        element.ClearPaintDirty();
        tree.Render(new RenderContext(new Bitmap(220, 80), 1f));

        element.InvalidatePaint();
        tree.UpdateDirty();
        var dirty = tree.CollectDirtyRects();

        Assert.DoesNotContain(dirty, r => r.Right > 80);
        Assert.Contains(dirty, r => r.Right >= 40 && r.Bottom >= 40);
    }

    [Fact]
    public void DisplayTreeVisualBoundsApplyPushTransform()
    {
        var root = new View { Geometry = new Rect(0, 0, 200, 100) };
        var element = new TransformedPaintElement
        {
            Geometry = new Rect(0, 0, 10, 10)
        };
        root.Children.Add(element);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        root.ClearPaintDirty();
        element.ClearPaintDirty();
        tree.Render(new RenderContext(new Bitmap(200, 100), 1f));

        element.InvalidatePaint();
        tree.UpdateDirty();
        var dirty = tree.CollectDirtyRects();

        Assert.Contains(dirty, r => r.X <= 70 && r.Right >= 90 && r.Bottom >= 30);
    }

    [Fact]
    public void DisplayTreeDirtyRectsIncludeBoxShadowVisualBounds()
    {
        var root = new View { Geometry = new Rect(0, 0, 160, 100) };
        var view = new View { Geometry = new Rect(40, 30, 30, 20) };
        view.Style.Set("background", "#ffffff");
        view.Style.Set("box-shadow", "5px 7px 10px 2px rgba(0,0,0,0.5)");
        root.Children.Add(view);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        root.ClearPaintDirty();
        view.ClearPaintDirty();
        tree.Render(new RenderContext(new Bitmap(160, 100), 1f));

        view.InvalidatePaint();
        tree.UpdateDirty();
        var dirty = tree.CollectDirtyRects();

        Assert.Contains(dirty, rect => rect.Right >= 87 && rect.Bottom >= 69);
    }

    [Fact]
    public void DisplayTreeDirtyRectsIncludePopupShadowWhenClosing()
    {
        var root = new View { Geometry = new Rect(0, 0, 200, 120) };
        var anchor = new View { Geometry = new Rect(50, 30, 20, 10) };
        var popup = new Popup { Geometry = new Rect(0, 0, 60, 30) };
        popup.Style.Set("box-shadow", "0 6px 12px rgba(0,0,0,0.5)");
        popup.Anchor = anchor;
        root.Children.Add(anchor);
        root.Children.Add(popup);
        popup.Open();
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        root.ClearPaintDirty();
        popup.ClearPaintDirty();
        tree.Render(new RenderContext(new Bitmap(200, 120), 1f));

        popup.Close();
        tree.UpdateDirty();
        var dirty = tree.CollectDirtyRects();

        Assert.Contains(dirty, rect => rect.X <= 38 && rect.Right >= 122 && rect.Bottom >= 92);
    }

    [Fact]
    public void PopupItemDirtyRectsUsePopupScreenCoordinates()
    {
        var root = new View { Geometry = new Rect(0, 0, 320, 180) };
        var menu = new Menu { Geometry = new Rect(0, 0, 160, 64) };
        var first = new MenuItem { TextContent = "First", Geometry = new Rect(0, 0, 160, 32) };
        var second = new MenuItem { TextContent = "Second", Geometry = new Rect(0, 32, 160, 32) };
        menu.Children.Add(first);
        menu.Children.Add(second);
        root.Children.Add(menu);
        menu.OpenAt(new Point(90, 30));
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        tree.Render(new CountingRenderContext());
        root.ClearPaintDirty();
        menu.ClearPaintDirty();
        first.ClearPaintDirty();
        second.ClearPaintDirty();

        first.SetState(ElementState.Hover, true);
        tree.UpdateDirty();
        var dirty = tree.CollectDirtyRects();

        Assert.Contains(dirty, rect => rect.X <= 90 && rect.Right >= 250 && rect.Y <= 30 && rect.Bottom >= 62);
    }

    [Fact]
    public void PopupHoverDirtyRenderMatchesFullFramePixels()
    {
        var (root, menu, first, second) = CreatePopupHoverTree();
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(320, 180);
        using var context = new RenderContext(bitmap, 1f);
        context.Clear(Color.White);
        tree.Render(context);
        root.ClearPaintDirty();
        menu.ClearPaintDirty();
        first.ClearPaintDirty();
        second.ClearPaintDirty();

        first.SetState(ElementState.Hover, true);
        tree.UpdateDirty();
        var firstDirty = tree.CollectDirtyRects().Aggregate(DisplayTree.Union);
        context.Clear(Color.White, firstDirty);
        tree.Render(context, firstDirty);

        first.SetState(ElementState.Hover, false);
        second.SetState(ElementState.Hover, true);
        tree.UpdateDirty();
        var secondDirty = tree.CollectDirtyRects().Aggregate(DisplayTree.Union);
        context.Clear(Color.White, secondDirty);
        tree.Render(context, secondDirty);

        var (expectedRoot, _, _, expectedSecond) = CreatePopupHoverTree();
        expectedSecond.SetState(ElementState.Hover, true);
        var expectedTree = new DisplayTree();
        expectedTree.BuildFrom(expectedRoot);
        using var expectedBitmap = new Bitmap(320, 180);
        using var expectedContext = new RenderContext(expectedBitmap, 1f);
        expectedContext.Clear(Color.White);
        expectedTree.Render(expectedContext);

        Assert.Equal(expectedBitmap.Pixels, bitmap.Pixels);
    }

    [Fact]
    public void RenderContextAppliesPushTransformToFillRect()
    {
        var bmp = new Bitmap(40, 30);
        var ctx = new RenderContext(bmp, 1f);
        ctx.Clear(Color.Transparent);

        ctx.PushTransform(Matrix3x2.CreateTranslation(10, 5));
        ctx.FillRect(new Rect(0, 0, 4, 4), Brush.FromColor(Color.Red));
        ctx.PopTransform();
        ctx.FillRect(new Rect(0, 0, 2, 2), Brush.FromColor(Color.Blue));

        AssertPixel(bmp, 11, 6, Color.Red);
        AssertPixel(bmp, 1, 1, Color.Blue);
        AssertPixel(bmp, 5, 5, Color.Transparent);
    }

    [Fact]
    public void DirtyRenderSkipsNodesOutsideDirtyClip()
    {
        var root = new View { Geometry = new Rect(0, 0, 200, 100) };
        var inside = new CountingPaintElement { Geometry = new Rect(10, 10, 20, 20) };
        var outside = new CountingPaintElement { Geometry = new Rect(150, 10, 20, 20) };
        root.Children.Add(inside);
        root.Children.Add(outside);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var context = new CountingRenderContext();

        tree.Render(context, new Rect(8, 8, 24, 24));

        Assert.Equal(1, context.FillCount);
    }

    [Fact]
    public void DirtyRenderUsesViewportCoordinatesForScrolledChildren()
    {
        var root = new View { Geometry = new Rect(0, 0, 100, 60) };
        root.Style.Set("overflow-y", "auto");
        root.SetScrollContentSize(new Size(100, 160));
        var child = new CountingPaintElement { Geometry = new Rect(0, 100, 100, 20) };
        root.Children.Add(child);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var context = new CountingRenderContext();

        root.ScrollTop = 80;
        tree.UpdateDirty();
        tree.Render(context, new Rect(0, 20, 100, 20));

        Assert.Equal(1, context.FillCount);
    }

    [Fact]
    public void DirtyRectsUseViewportCoordinatesForScrolledChildren()
    {
        var root = new View { Geometry = new Rect(0, 0, 100, 60) };
        root.Style.Set("overflow-y", "auto");
        root.SetScrollContentSize(new Size(100, 160));
        var child = new CountingPaintElement { Geometry = new Rect(0, 100, 100, 20) };
        root.Children.Add(child);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        tree.Render(new CountingRenderContext());
        root.ClearPaintDirty();
        child.ClearPaintDirty();

        root.ScrollTop = 80;
        tree.UpdateDirty();
        _ = tree.CollectDirtyRects();
        root.ClearPaintDirty();
        child.InvalidatePaint();
        tree.UpdateDirty();
        var dirty = tree.CollectDirtyRects();

        Assert.Contains(dirty, rect => rect.Top <= 20 && rect.Bottom >= 40);
        Assert.DoesNotContain(dirty, rect => rect.Top >= 90);
    }

    [Fact]
    public void ScrolledDirtyRenderMatchesFullFramePixels()
    {
        var root = CreateScrolledPixelTree();
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(100, 60);
        using var context = new RenderContext(bitmap, 1f);
        context.Clear(Color.White);
        tree.Render(context);

        root.ScrollTop = 80;
        tree.UpdateDirty();
        var dirty = tree.CollectDirtyRects();
        var union = dirty.Aggregate(DisplayTree.Union);
        context.Clear(Color.White, union);
        tree.Render(context, union);

        var expectedRoot = CreateScrolledPixelTree();
        expectedRoot.ScrollTop = 80;
        var expectedTree = new DisplayTree();
        expectedTree.BuildFrom(expectedRoot);
        using var expectedBitmap = new Bitmap(100, 60);
        using var expectedContext = new RenderContext(expectedBitmap, 1f);
        expectedContext.Clear(Color.White);
        expectedTree.Render(expectedContext);

        Assert.Equal(expectedBitmap.Pixels, bitmap.Pixels);
    }

    private static View CreateScrolledPixelTree()
    {
        var root = new View { Geometry = new Rect(0, 0, 100, 60) };
        root.Style.Set("overflow-y", "auto");
        root.SetScrollContentSize(new Size(100, 160));
        root.Children.Add(new ColorPaintElement(Color.Red) { Geometry = new Rect(0, 0, 100, 60) });
        root.Children.Add(new ColorPaintElement(Color.Blue) { Geometry = new Rect(0, 100, 100, 20) });
        return root;
    }

    private static (View Root, Menu Menu, MenuItem First, MenuItem Second) CreatePopupHoverTree()
    {
        var root = new View { Geometry = new Rect(0, 0, 320, 180) };
        var menu = new Menu { Geometry = new Rect(0, 0, 160, 64) };
        menu.Style.Set("box-shadow", "none");
        var first = new MenuItem { TextContent = "First", Geometry = new Rect(0, 0, 160, 32) };
        var second = new MenuItem { TextContent = "Second", Geometry = new Rect(0, 32, 160, 32) };
        menu.Children.Add(first);
        menu.Children.Add(second);
        root.Children.Add(menu);
        menu.OpenAt(new Point(90, 30));
        return (root, menu, first, second);
    }

    private sealed class PathPaintElement : UIElement
    {
        public override void Paint(IRenderContext ctx)
        {
            ctx.DrawPath(
                PathGeometry.Create()
                    .MoveTo(new Point(50, 20))
                    .LineTo(new Point(90, 30)),
                Pen.FromColor(Color.Red, 2));
        }
    }

    private sealed class CountingPaintElement : UIElement
    {
        public int PaintCount { get; private set; }

        public override void Paint(IRenderContext ctx)
        {
            PaintCount++;
            ctx.FillRect(Geometry, Brush.FromColor(Color.Red));
        }
    }

    private sealed class ColorPaintElement(Color color) : UIElement
    {
        public override void Paint(IRenderContext ctx) => ctx.FillRect(Geometry, Brush.FromColor(color));
    }

    private sealed class ReinvalidatingElement : UIElement
    {
        public override void Paint(IRenderContext ctx) => InvalidatePaint();
    }

    private sealed class CountingRenderContext : IRenderContext
    {
        public int FillCount { get; private set; }
        public Size CanvasSize => new(200, 100);
        public float DpiScale => 1;
        public void FillRect(Rect rect, Brush brush) => FillCount++;
        public void PushTransform(Matrix3x2 matrix) { }
        public void PopTransform() { }
        public void PushClip(Rect rect) { }
        public void PushClip(Geometry geometry) { }
        public void PopClip() { }
        public void DrawRect(Rect rect, Pen pen) { }
        public void FillPath(PathGeometry path, Brush brush) { }
        public void DrawPath(PathGeometry path, Pen pen) { }
        public void FillGeometry(Geometry geometry, Brush brush) { }
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
    }

    private static void AssertPixel(Bitmap bmp, int x, int y, Color color)
    {
        var idx = y * bmp.Stride + x * 4;
        Assert.Equal(color.B, bmp.Pixels[idx]);
        Assert.Equal(color.G, bmp.Pixels[idx + 1]);
        Assert.Equal(color.R, bmp.Pixels[idx + 2]);
        Assert.Equal(color.A, bmp.Pixels[idx + 3]);
    }

    private sealed class ClippedPaintElement : UIElement
    {
        public override void Paint(IRenderContext ctx)
        {
            ctx.PushClip(new Rect(20, 20, 20, 20));
            ctx.FillRect(new Rect(20, 20, 160, 40), Brush.FromColor(Color.Blue));
            ctx.PopClip();
        }
    }

    private sealed class TransformedPaintElement : UIElement
    {
        public override void Paint(IRenderContext ctx)
        {
            ctx.PushTransform(Matrix3x2.CreateTranslation(70, 20));
            ctx.FillRect(new Rect(0, 0, 20, 10), Brush.FromColor(Color.Green));
            ctx.PopTransform();
        }
    }
}
