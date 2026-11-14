using Square.Backends;
using Square.CSS.Engine;
using Square.CSS.Tokenizer;
using Square.Graphics;
using Square.Graphics.Svg;
using Square.Rendering;
using Square.Rendering.Commands;
using Square.Rendering.Tree;
using Square.Controls;
using Xunit;
using ImageControl = Square.Controls.Image;

namespace Square.CSS.Tests;

public sealed class ObjectFitTests
{
    [Theory]
    [InlineData("fill")]
    [InlineData("contain")]
    [InlineData("cover")]
    [InlineData("none")]
    [InlineData("scale-down")]
    [InlineData("CoNtAiN")]
    public void StylesheetAcceptsObjectFit(string value)
    {
        var image = new ImageControl();
        Engine($"Image {{ object-fit: {value}; }}").ApplyStyles(image);

        Assert.Equal(value, image.Style.Get("object-fit"));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("stretch")]
    [InlineData("contain cover")]
    [InlineData("10px")]
    public void InvalidDeclarationsKeepPreviousValue(string value)
    {
        var image = new ImageControl();
        Engine($"Image {{ object-fit: cover; object-fit: {value}; }}").ApplyStyles(image);
        Assert.Equal("cover", image.Style.Get("object-fit"));

        image.Style.Set("object-fit", "contain");
        image.Style.Set("object-fit", value);
        Assert.Equal("contain", image.Style.Get("object-fit"));
    }

    [Fact]
    public void ObjectFitIsNotInheritedAndSupportsGlobalKeywordsAndVariables()
    {
        var parent = new View();
        var image = new ImageControl();
        parent.Children.Add(image);
        parent.Style.Set("object-fit", "cover");
        image.Style.Set("--fit", "contain");
        Assert.Null(image.Style.Get("object-fit"));

        image.Style.Set("object-fit", "inherit");
        Assert.Equal("cover", image.Style.Get("object-fit"));
        image.Style.Set("object-fit", "initial");
        Assert.Equal("fill", image.Style.Get("object-fit"));
        image.Style.Set("object-fit", "unset");
        Assert.Equal("fill", image.Style.Get("object-fit"));
        image.Style.Set("object-fit", "var(--fit)");
        Assert.Equal("contain", image.Style.Get("object-fit"));
        image.Style.Set("object-fit", "var(--missing)");
        Assert.Equal("fill", image.Style.Get("object-fit"));
    }

    [Theory]
    [InlineData(null, 200, 100, 10, 20, 100, 100)]
    [InlineData("fill", 200, 100, 10, 20, 100, 100)]
    [InlineData("contain", 200, 100, 10, 45, 100, 50)]
    [InlineData("cover", 200, 100, -40, 20, 200, 100)]
    [InlineData("none", 200, 100, -40, 20, 200, 100)]
    [InlineData("scale-down", 200, 100, 10, 45, 100, 50)]
    [InlineData("contain", 40, 20, 10, 45, 100, 50)]
    [InlineData("scale-down", 40, 20, 40, 60, 40, 20)]
    [InlineData("none", 40, 20, 40, 60, 40, 20)]
    [InlineData("CoNtAiN", 100, 200, 35, 20, 50, 100)]
    [InlineData("cover", 100, 200, 10, -30, 100, 200)]
    [InlineData("scale-down", 100, 200, 35, 20, 50, 100)]
    public void BitmapUsesCenteredObjectRect(
        string? fit, int width, int height, float x, float y, float objectWidth, float objectHeight)
    {
        using var bitmap = new Bitmap(width, height);
        var image = new ImageControl { ImageContent = bitmap, Geometry = new Rect(10, 20, 100, 100) };
        if (fit != null) image.Style.Set("object-fit", fit);
        var commands = new List<DrawCommand>();
        image.Paint(new CommandCollector(commands));

        Assert.Equal(image.Geometry, Assert.IsType<PushClipCommand>(commands[0]).Rect);
        Assert.Equal(new Rect(x, y, objectWidth, objectHeight), Assert.Single(commands.OfType<DrawImageCommand>()).Dest);
        Assert.IsType<PopClipCommand>(commands[^1]);
    }

    [Theory]
    [InlineData("fill", 8, 8, 80, 80)]
    [InlineData("contain", 8, 28, 80, 40)]
    [InlineData("cover", 8, 8, 80, 80)]
    [InlineData("none", 28, 38, 40, 20)]
    [InlineData("scale-down", 28, 38, 40, 20)]
    public void BitmapAndSvgPaintInsideContentBox(string fit, int x, int y, int width, int height)
    {
        using var bitmap = new Bitmap(40, 20);
        for (var i = 0; i < bitmap.Pixels.Length; i += 4)
        {
            bitmap.Pixels[i + 2] = 255;
            bitmap.Pixels[i + 3] = 255;
        }
        using var svg = SvgImage.Parse("""
            <svg width="40" height="20" viewBox="0 0 40 20">
              <rect width="40" height="20" fill="red" />
            </svg>
            """);
        foreach (var source in new Square.Graphics.Image[] { bitmap, svg })
        {
            var image = new ImageControl { ImageContent = source, Geometry = new Rect(2, 2, 92, 92) };
            image.Style.CssText = $"object-fit: {fit}; padding: 4px; border: 2px solid blue;";
            using var context = CreateContext();
            context.Clear(Color.White);
            var tree = new DisplayTree();
            tree.BuildFrom(image);
            tree.Render(context);
            using var rendered = ((IRenderBitmapSource)context).CaptureBitmap();

            for (var py = 0; py < 96; py++)
            for (var px = 0; px < 96; px++)
            {
                var pixel = rendered.GetPixel(px, py);
                var isRed = pixel[2] == 255 && pixel[1] == 0 && pixel[0] == 0;
                Assert.Equal(px >= x && px < x + width && py >= y && py < y + height, isRed);
            }
        }
    }

    [Theory]
    [InlineData("fill", 8, 8, 80, 80, 80)]
    [InlineData("contain", 8, 28, 80, 40, 60)]
    [InlineData("cover", 8, 8, 80, 80, 80)]
    [InlineData("none", 28, 38, 40, 20, 48)]
    [InlineData("scale-down", 28, 38, 40, 20, 48)]
    public void SvgVisualBoundsAllowPartialRepaint(
        string fit, float x, float y, float width, float height, float dirtyY)
    {
        using var svg = SvgImage.Parse("""
            <svg width="40" height="20" viewBox="0 0 40 20">
              <rect width="40" height="20" fill="red" />
            </svg>
            """);
        var image = new ImageControl { ImageContent = svg, Geometry = new Rect(8, 8, 80, 80) };
        image.Style.Set("object-fit", fit);
        var node = new DisplayNode { Element = image };
        node.RebuildCommands();
        Assert.Equal(new Rect(x, y, width, height), node.VisualBounds);

        using var context = CreateContext();
        context.Clear(Color.White);
        var dirtyRect = new Rect(48, dirtyY, 4, 4);
        context.PushClip(dirtyRect);
        node.Render(context, dirtyRect);
        context.PopClip();
        using var rendered = ((IRenderBitmapSource)context).CaptureBitmap();
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, rendered.GetPixel(49, (int)dirtyY + 1).ToArray());
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, rendered.GetPixel(47, (int)dirtyY + 1).ToArray());
    }

    [Theory]
    [InlineData("cover")]
    [InlineData("none")]
    public void OversizedBitmapIsCroppedFromTheCenter(string fit)
    {
        using var bitmap = new Bitmap(160, 80);
        for (var y = 0; y < 80; y++)
        for (var x = 0; x < 160; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            pixel[x < 40 ? 0 : x >= 120 ? 1 : 2] = 255;
            pixel[3] = 255;
        }
        var image = new ImageControl { ImageContent = bitmap, Geometry = new Rect(8, 8, 80, 80) };
        image.Style.Set("object-fit", fit);
        using var context = CreateContext();
        context.Clear(Color.White);
        image.Paint(context);
        using var rendered = ((IRenderBitmapSource)context).CaptureBitmap();

        Assert.Equal(new byte[] { 255, 255, 255, 255 }, rendered.GetPixel(7, 48).ToArray());
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, rendered.GetPixel(8, 48).ToArray());
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, rendered.GetPixel(87, 48).ToArray());
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, rendered.GetPixel(88, 48).ToArray());
    }

    [Fact]
    public void DynamicClassAndInlineChangesRepaintWithoutChangingLayout()
    {
        using var bitmap = new Bitmap(200, 100);
        var image = new ImageControl { ImageContent = bitmap, Geometry = new Rect(10, 20, 100, 100) };
        var engine = Engine("Image { object-fit: contain; } .crop { object-fit: cover; }");
        try
        {
            engine.ApplyStylesToTree(image);
            var desiredSize = image.Measure(new Size(100, 100));
            var tree = new DisplayTree();
            tree.BuildFrom(image);
            image.ClearLayoutDirty();
            image.ClearPaintDirty();

            image.ClassList.Add("crop");
            // class 本身沿通用路径标记布局；这里只检查级联样式差异引起的失效。
            image.ClearLayoutDirty();
            image.ClearPaintDirty();
            CssStyleReconciler.Flush();

            Assert.True(image.NeedsPaint);
            Assert.False(image.IsLayoutDirty);
            tree.UpdateDirty();
            var commands = new List<DrawCommand>();
            tree.Render(new CommandCollector(commands));
            Assert.Equal(new Rect(-40, 20, 200, 100), Assert.Single(commands.OfType<DrawImageCommand>()).Dest);

            image.Style.Set("object-fit", "none");
            Assert.True(image.NeedsPaint);
            Assert.False(image.IsLayoutDirty);
            Assert.Equal(desiredSize, image.Measure(new Size(100, 100)));
            image.Style.Remove("object-fit");
            image.ClassList.Remove("crop");
            CssStyleReconciler.Flush();
            Assert.Equal("contain", image.Style.Get("object-fit"));
        }
        finally
        {
            CssStyleReconciler.UnregisterScopesForTree(image);
        }
    }

    [Theory]
    [InlineData(0, 100, 40, 20)]
    [InlineData(100, 0, 40, 20)]
    [InlineData(100, 100, 0, 20)]
    [InlineData(100, 100, 40, 0)]
    public void EmptyContentOrImageDoesNotEmitDrawCommands(int width, int height, int imageWidth, int imageHeight)
    {
        using var bitmap = new Bitmap(imageWidth, imageHeight);
        var image = new ImageControl { ImageContent = bitmap, Geometry = new Rect(0, 0, width, height) };
        image.Style.Set("object-fit", "cover");
        var commands = new List<DrawCommand>();
        image.Paint(new CommandCollector(commands));
        Assert.Empty(commands);
    }

    private static IRenderContext CreateContext() => new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
    {
        CanvasSize = new Size(96, 96)
    });

    private static CssEngine Engine(string css)
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(css).Tokenize()).Parse());
        return engine;
    }
}
