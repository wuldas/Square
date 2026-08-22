using Square.Backends;
using Square.CSS.Engine;
using Square.CSS.Tokenizer;
using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Square.UI;
using Xunit;

namespace Square.CSS.Tests;

public sealed class AppearanceCssTests
{
    [Fact]
    public void UserAgentStylesGiveFormControlsAppearanceAuto()
    {
        var engine = new CssEngine();
        var button = new Button("Save");
        var input = new Input();
        var view = new View();

        engine.ApplyStyles(button);
        engine.ApplyStyles(input);
        engine.ApplyStyles(view);

        Assert.Equal("auto", button.Style.Get("appearance"));
        Assert.Equal("auto", input.Style.Get("appearance"));
        Assert.Equal("ButtonFace", button.Style.Get("background-color"));
        Assert.Null(button.Style.Get("border-radius"));
        Assert.Equal("Field", input.Style.Get("background-color"));
        Assert.Equal("2px", input.Style.Get("border-top-width"));
        Assert.Equal("inset", input.Style.Get("border-top-style"));
        Assert.Null(view.Style.Get("appearance"));
        Assert.Null(view.Style.Get("background"));
    }

    [Fact]
    public void AuthorAppearanceNoneOverridesUserAgentAuto()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(
            "Button { appearance: none; background: #112233; border-radius: 0; border: none; }").Tokenize()).Parse());
        var button = new Button("Save");

        engine.ApplyStyles(button);

        Assert.Equal("none", button.Style.Get("appearance"));
        Assert.Equal("#112233", button.Style.Get("background"));
        Assert.Equal("0", button.Style.Get("border-radius"));
        Assert.Equal("none", button.Style.Get("border-top-style"));
    }

    [Fact]
    public void AppearanceAutoPaintsRoundedChromeThroughCssBoxOnSoftwareRenderer()
    {
        var engine = new CssEngine();
        var button = new Button("Save") { Geometry = new Rect(2, 2, 80, 28) };
        engine.ApplyStyles(button);

        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(90, 36)
        });
        context.Clear(Color.FromRgb(255, 0, 0));
        var tree = new DisplayTree();
        tree.BuildFrom(button);
        tree.Render(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var corner = bitmap.GetPixel(2, 2);
        var interior = bitmap.GetPixel(20, 16);

        Assert.True(interior[2] > 200 && interior[1] > 200 && interior[0] > 200,
            "appearance:auto should fill the button interior from Chrome ButtonFace");
        Assert.True(corner[2] > 180 && corner[1] > 180 && corner[0] > 180,
            "Chrome outset button chrome should cover the corner instead of leaving a rounded cutout");
    }

    [Fact]
    public void AppearanceNoneKeepsAuthorBoxWithoutWidgetRadius()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(
            "Button { appearance: none; background: #112233; border-radius: 0; border: none; }").Tokenize()).Parse());
        var button = new Button("Save") { Geometry = new Rect(2, 2, 80, 28) };
        engine.ApplyStyles(button);

        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(90, 36)
        });
        context.Clear(Color.FromRgb(255, 0, 0));
        var tree = new DisplayTree();
        tree.BuildFrom(button);
        tree.Render(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var corner = bitmap.GetPixel(2, 2);
        Assert.Equal(0x11, corner[2]);
        Assert.Equal(0x22, corner[1]);
        Assert.Equal(0x33, corner[0]);
    }
}
