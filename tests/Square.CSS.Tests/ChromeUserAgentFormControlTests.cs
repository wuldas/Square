using Square.CSS.Engine;
using Square.Controls;
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
}
