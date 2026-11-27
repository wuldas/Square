using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Xunit;

namespace Square.Backends.Tests;

public sealed class DescendantOverflowClipTests
{
    private static readonly Rect Box = new(8, 8, 40, 32);

    private static (View Parent, View Child, DisplayTree Tree) Build(string style)
    {
        var parent = new View { Geometry = Box };
        parent.Style.CssText = style;
        var child = new View { Geometry = new Rect(0, 0, 64, 48) };
        child.Style.Set("background", "#0000ff");
        parent.Children.Add(child);
        var tree = new DisplayTree();
        tree.BuildFrom(parent);
        return (parent, child, tree);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("clip")]
    [InlineData("auto")]
    [InlineData("scroll")]
    public void ClippedChildrenRespectTheRoundedPaddingEdge(string overflow)
    {
        var (parent, child, tree) = Build($"overflow:{overflow}; scrollbar-width:none; border:2px solid #ff0000; border-radius:10px; padding:6px;");
        using var bitmap = new Bitmap(64, 48);
        using var context = new RenderContext(bitmap, 1f);
        context.Clear(Color.White);
        tree.Render(context);
        Assert.Equal(new Rect(10, 10, 36, 28), parent.GetOverflowClipRect());
        foreach (var (x, y) in new[] { (10, 10), (45, 10), (45, 37), (10, 37) })
        {
            Assert.NotEqual(new byte[] { 255, 0, 0, 255 }, bitmap.GetPixel(x, y).ToArray());
            var point = new Point(x + 0.5f, y + 0.5f);
            Assert.Same(parent, parent.HitTest(point));
            Assert.Same(parent, tree.HitTestRoot(point));
        }

        foreach (var (x, y) in new[] { (28, 8), (47, 24), (28, 39), (8, 24) })
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, bitmap.GetPixel(x, y).ToArray());
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, bitmap.GetPixel(11, 24).ToArray());
        Assert.Same(child, tree.HitTestRoot(new Point(11, 24)));
    }

    [Fact]
    public void VisibleOverflowCanCoverTheBorderAndHitOutsideTheBox()
    {
        var (parent, child, tree) = Build("overflow:visible; border:2px solid red; border-radius:10px;");
        using var bitmap = new Bitmap(64, 48);
        using var context = new RenderContext(bitmap, 1f);
        tree.Render(context);
        foreach (var x in new[] { 4, 8, 26 })
        {
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, bitmap.GetPixel(x, 8).ToArray());
            Assert.Same(child, parent.HitTest(new Point(x, 8)));
            Assert.Same(child, tree.HitTestRoot(new Point(x, 8)));
        }
    }

    [Fact]
    public void SingleAxisClipLeavesOtherAxisOpenAndGutterCutsFlat()
    {
        var (parent, child, tree) = Build("overflow-x:hidden; overflow-y:visible; border:2px solid red; border-radius:12px;");
        Assert.Same(child, tree.HitTestRoot(new Point(11, 2)));
        Assert.Null(parent.HitTest(new Point(8, 2)));
        parent.Style.CssText = "overflow-x:hidden; overflow-y:auto; scrollbar-gutter:stable; border:2px solid red; border-radius:10px;";
        Assert.Equal(new Rect(10, 10, 23, 28), parent.GetOverflowClipRect());
        Assert.Same(child, parent.HitTest(new Point(32, 11)));
        Assert.Same(parent, parent.HitTest(new Point(34, 20)));
    }

    [Fact]
    public void AsymmetricRoundedClipUpdatesWhenBorderStyleChanges()
    {
        var (parent, child, tree) = Build("overflow:hidden; border:2px solid transparent; border-left-width:6px; border-radius:16px 0 0 0 / 12px 0 0 0;");
        Assert.Equal(new Rect(14, 10, 32, 28), parent.GetOverflowClipRect());
        Assert.Same(parent, tree.HitTestRoot(new Point(14.5f, 10.5f)));
        Assert.Same(child, tree.HitTestRoot(new Point(45, 11)));
        using var bitmap = new Bitmap(64, 48);
        using var context = new RenderContext(bitmap, 1f);
        context.Clear(Color.White);
        tree.Render(context);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, bitmap.GetPixel(14, 10).ToArray());
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, bitmap.GetPixel(45, 11).ToArray());
        parent.Style.Set("border-radius", "0");
        Assert.Same(child, parent.HitTest(new Point(14.5f, 10.5f)));
        parent.Style.Set("border-left-width", "50px");
        Assert.True(parent.GetOverflowClipRect().IsEmpty);
        Assert.Same(parent, parent.HitTest(new Point(20, 20)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PopupAndItsNestedClipUseTheSamePaddingEdgeAfterTranslation(bool modal)
    {
        var anchor = new View { Geometry = modal ? new Rect(40, 1, 60, 52) : new Rect(50, 1, 20, 10) };
        Popup popup = modal ? new Dialog { BackdropColor = Color.FromRgb(255, 255, 0) } : new Popup();
        popup.Anchor = anchor;
        popup.Geometry = new Rect(0, 0, 40, 32);
        popup.VerticalOffset = 1;
        popup.Style.CssText = "border:2px solid #ff0000; border-radius:8px; background:#ffffff; box-shadow:none;";

        var overhang = new View { Geometry = new Rect(-3, -3, 46, 38) };
        overhang.Style.Set("background", "#0000ff");
        popup.Children.Add(overhang);
        var nested = new View { Geometry = new Rect(4, 4, 28, 20) };
        nested.Style.CssText = "border:2px solid #ff0000; border-radius:6px; overflow:hidden; background:#ffffff;";
        var child = new View { Geometry = new Rect(0, 0, 40, 32) };
        child.Style.Set("background", "#00ff00");
        nested.Children.Add(child);
        popup.Children.Add(nested);
        popup.Open();

        using var context = new Square.Backends.RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(100, 60),
            DpiScale = 1f
        });
        context.Clear(Color.White);
        popup.PaintPopup(context);
        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();

        var red = new byte[] { 0, 0, 255, 255 };
        Assert.Equal(red, bitmap.GetPixel(50, 26).ToArray());
        Assert.Equal(red, bitmap.GetPixel(54, 26).ToArray());
        Assert.Equal(255, bitmap.GetPixel(52, 14)[2]);
        Assert.Equal(255, bitmap.GetPixel(56, 18)[2]);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, bitmap.GetPixel(85, 26).ToArray());
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, bitmap.GetPixel(62, 24).ToArray());
        Assert.Equal(modal ? new byte[] { 0, 255, 255, 255 } : new byte[] { 255, 255, 255, 255 },
            bitmap.GetPixel(45, 6).ToArray());

        Assert.Same(popup, popup.HitTestPopup(new Point(50.5f, 26.5f)));
        Assert.Same(popup, popup.HitTestPopup(new Point(52.5f, 14.5f)));
        Assert.Same(nested, popup.HitTestPopup(new Point(54.5f, 26.5f)));
        Assert.Same(nested, popup.HitTestPopup(new Point(56.5f, 18.5f)));
        Assert.Same(overhang, popup.HitTestPopup(new Point(85.5f, 26.5f)));
        Assert.Same(child, popup.HitTestPopup(new Point(62.5f, 24.5f)));
        Assert.Same(modal ? popup : null, popup.HitTestPopup(new Point(45.5f, 6.5f)));
    }
}
