using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Xunit;

namespace Square.Backends.Tests;

public sealed class CssFixedLayerTests
{
    [Fact]
    public void FixedElementPaintsAfterNormalLayerAndIsHitThroughFixedLayer()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var normal = new PaintedBox(Color.Blue) { Geometry = new Rect(0, 0, 20, 20) };
        normal.Style.Set("display", "block");
        var fixedBox = new PaintedBox(Color.Red) { Geometry = new Rect(0, 0, 20, 20) };
        fixedBox.Style.Set("display", "block");
        fixedBox.Style.Set("position", "fixed");
        root.Children.Add(normal);
        root.Children.Add(fixedBox);

        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(20, 20);
        using var context = new RenderContext(bitmap, 1f);
        tree.Render(context);

        var pixel = bitmap.GetPixel(10, 10);
        Assert.Equal(Color.Red.R, pixel[2]);
        Assert.Equal(Color.Red.G, pixel[1]);
        Assert.Equal(Color.Red.B, pixel[0]);
        Assert.Equal(Color.Red.A, pixel[3]);
        Assert.Same(fixedBox, tree.HitTestFixed(new Point(10, 10)));
        Assert.NotSame(fixedBox, tree.HitTestRoot(new Point(10, 10)));
    }

    private sealed class PaintedBox(Color color) : View
    {
        public override void Paint(IRenderContext context) =>
            context.FillRect(Geometry, new SolidColorBrush(color));
    }
}
