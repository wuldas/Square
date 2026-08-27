using Square.Backends;
using Square.CSS.Engine;
using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Square.UI;
using Xunit;

namespace Square.CSS.Tests;

public sealed class ChromeUserAgentFormControlTests
{
    [Fact]
    public void UserAgentMatchesChromeHtmlCssFormControls()
    {
        var engine = new CssEngine();
        var button = new Button("Save");
        var input = new Input();
        var textArea = new TextArea();
        var select = new Select();
        var check = new CheckBox();
        var radio = new Radio();
        var view = new View();

        engine.ApplyStyles(button);
        engine.ApplyStyles(input);
        engine.ApplyStyles(textArea);
        engine.ApplyStyles(select);
        engine.ApplyStyles(check);
        engine.ApplyStyles(radio);
        engine.ApplyStyles(view);

        Assert.Equal("auto", button.Style.Get("appearance"));
        Assert.Equal("ButtonFace", button.Style.Get("background-color"));
        Assert.Equal("ButtonText", button.Style.Get("color"));
        Assert.Equal("2px", button.Style.Get("border-top-width"));
        Assert.Equal("outset", button.Style.Get("border-top-style"));
        Assert.Equal("ButtonBorder", button.Style.Get("border-top-color"));
        Assert.Equal("1px", button.Style.Get("padding-top"));
        Assert.Equal("6px", button.Style.Get("padding-left"));
        Assert.Null(button.Style.Get("border-radius"));

        Assert.Equal("auto", input.Style.Get("appearance"));
        Assert.Equal("Field", input.Style.Get("background-color"));
        Assert.Equal("FieldText", input.Style.Get("color"));
        Assert.Equal("2px", input.Style.Get("border-top-width"));
        Assert.Equal("inset", input.Style.Get("border-top-style"));
        Assert.Equal("#767676", input.Style.Get("border-top-color"));

        Assert.Equal("auto", textArea.Style.Get("appearance"));
        Assert.Equal("Field", textArea.Style.Get("background-color"));
        Assert.Equal("1px", textArea.Style.Get("border-top-width"));
        Assert.Equal("solid", textArea.Style.Get("border-top-style"));
        Assert.Equal("#767676", textArea.Style.Get("border-top-color"));

        Assert.Equal("auto", select.Style.Get("appearance"));
        Assert.Equal("Field", select.Style.Get("background-color"));
        Assert.Equal("1px", select.Style.Get("border-top-width"));
        Assert.Equal("solid", select.Style.Get("border-top-style"));
        Assert.Equal("#767676", select.Style.Get("border-top-color"));
        Assert.Equal("0", select.Style.Get("border-radius"));

        Assert.Equal("auto", check.Style.Get("appearance"));
        Assert.Equal("auto", radio.Style.Get("appearance"));
        Assert.Null(view.Style.Get("appearance"));
        Assert.Null(view.Style.Get("background-color"));
    }

    [Fact]
    public void UserAgentButtonActiveUsesInsetBorder()
    {
        var engine = new CssEngine();
        var button = new Button("Save");
        button.SetState(ElementState.Active, true);
        engine.ApplyStyles(button);

        Assert.Equal("inset", button.Style.Get("border-top-style"));
        Assert.Equal("ButtonFace", button.Style.Get("background-color"));
    }

    [Fact]
    public void UserAgentDisabledButtonUsesChromeDisabledColors()
    {
        var engine = new CssEngine();
        var button = new Button("Save") { IsDisabled = true };
        engine.ApplyStyles(button);

        Assert.Equal("rgba(239, 239, 239, 0.3)", button.Style.Get("background-color"));
        Assert.Equal("rgba(16, 16, 16, 0.3)", button.Style.Get("color"));
        Assert.Equal("rgba(118, 118, 118, 0.3)", button.Style.Get("border-top-color"));
    }

    [Fact]
    public void UserAgentDisabledInputAndSelectMatchChrome()
    {
        var engine = new CssEngine();
        var input = new Input { IsDisabled = true };
        var textArea = new TextArea { IsDisabled = true };
        var select = new Select { IsDisabled = true };
        engine.ApplyStyles(input);
        engine.ApplyStyles(textArea);
        engine.ApplyStyles(select);

        Assert.Equal("default", input.Style.Get("cursor"));
        Assert.Equal("rgba(239, 239, 239, 0.3)", input.Style.Get("background-color"));
        Assert.Equal("#545454", input.Style.Get("color"));
        Assert.Equal("rgba(118, 118, 118, 0.3)", input.Style.Get("border-top-color"));

        Assert.Equal("default", textArea.Style.Get("cursor"));
        Assert.Equal("rgba(239, 239, 239, 0.3)", textArea.Style.Get("background-color"));
        Assert.Equal("#545454", textArea.Style.Get("color"));
        Assert.Equal("rgba(118, 118, 118, 0.3)", textArea.Style.Get("border-top-color"));

        Assert.Equal("0.7", select.Style.Get("opacity"));
        Assert.Equal("GrayText", select.Style.Get("color"));
        Assert.Equal("rgba(118, 118, 118, 0.3)", select.Style.Get("border-top-color"));
    }

    [Fact]
    public void UserAgentInputFocusPaintsCapturedChromiumInnerBorder()
    {
        var engine = new CssEngine();
        var input = new Input { Geometry = new Rect(8, 8, 80, 24) };
        input.Focus();
        engine.ApplyStyles(input);

        Assert.Equal("solid", input.Style.Get("outline-style"));
        Assert.Equal("1px", input.Style.Get("outline-width"));
        Assert.Equal("Highlight", input.Style.Get("outline-color"));

        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(100, 40)
        });
        context.Clear(Color.FromRgb(255, 0, 0));
        var tree = new DisplayTree();
        tree.BuildFrom(input);
        tree.Render(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var outside = bitmap.GetPixel(7, 20);
        Assert.Equal(255, outside[2]);
        Assert.Equal(0, outside[1]);
        Assert.Equal(0, outside[0]);
        var border = bitmap.GetPixel(8, 20);
        Assert.Equal(16, border[2]);
        Assert.Equal(16, border[1]);
        Assert.Equal(16, border[0]);
    }

    [Fact]
    public void UserAgentDisabledActiveButtonKeepsOutsetBorder()
    {
        var engine = new CssEngine();
        var button = new Button("Save") { IsDisabled = true };
        button.SetState(ElementState.Active, true);
        engine.ApplyStyles(button);

        Assert.Equal("outset", button.Style.Get("border-top-style"));
    }

    [Fact]
    public void UserAgentFocusVisibleMatchesChromeOffsets()
    {
        var engine = new CssEngine();
        var input = new Input();
        input.Focus();
        engine.ApplyStyles(input);
        Assert.Equal("0", input.Style.Get("outline-offset"));

        var check = new CheckBox();
        check.Focus();
        engine.ApplyStyles(check);
        Assert.Equal("2px", check.Style.Get("outline-offset"));

        var radio = new Radio();
        radio.Focus();
        engine.ApplyStyles(radio);
        Assert.Equal("2px", radio.Style.Get("outline-offset"));
    }

    [Fact]
    public void UserAgentPlaceholderColorMatchesChrome()
    {
        var engine = new CssEngine();
        var input = new Input
        {
            Geometry = new Rect(4, 4, 160, 28),
            Placeholder = "MMMM",
            Value = ""
        };
        engine.ApplyStyles(input);

        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(180, 40)
        });
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(input);
        tree.Render(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var foundPlaceholder = false;
        for (var y = 12; y < 24; y++)
        {
            for (var x = 16; x < 70; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel[2] == 0x75 && pixel[1] == 0x75 && pixel[0] == 0x75)
                    foundPlaceholder = true;
            }
        }

        Assert.True(foundPlaceholder, "empty Input placeholder should use Chrome #757575");
    }

    [Fact]
    public void CheckBoxFocusChromeStaysInsideIndicatorBorderBox()
    {
        var engine = new CssEngine();
        var check = new CheckBox
        {
            Geometry = new Rect(8, 8, 120, 24),
            TextContent = ""
        };
        check.Focus();
        engine.ApplyStyles(check);

        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(140, 40)
        });
        context.Clear(Color.FromRgb(255, 0, 0));
        var tree = new DisplayTree();
        tree.BuildFrom(check);
        tree.Render(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var outlinePixel = bitmap.GetPixel(5, 20);
        Assert.Equal(255, outlinePixel[2]);
        Assert.Equal(0, outlinePixel[1]);
        Assert.Equal(0, outlinePixel[0]);
        var gap = bitmap.GetPixel(6, 20);
        Assert.Equal(255, gap[2]);
        Assert.Equal(0, gap[1]);
        Assert.Equal(0, gap[0]);
    }
}
