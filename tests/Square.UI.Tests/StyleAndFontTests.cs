using System;
using Square.Controls;
using Square.Graphics;
using Square.UI;
using Square.UI.ElementApi;
using Xunit;

namespace Square.UI.Tests;

public class StyleAndFontTests
{
    [Theory]
    [InlineData("display")]
    [InlineData("visibility")]
    [InlineData("cursor")]
    [InlineData("overflow-x")]
    [InlineData("background-color")]
    public void CanonicalAsciiPropertyNameUsesAllocationFreeFastPath(string property)
    {
        Assert.Same(property, StyleAccessor.NormalizePropertyName(property));

        _ = StyleAccessor.NormalizePropertyName(property);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var totalLength = 0;
        for (var i = 0; i < 10_000; i++)
            totalLength += StyleAccessor.NormalizePropertyName(property).Length;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(property.Length * 10_000, totalLength);
        Assert.InRange(allocated, 0, 1_024);
    }

    [Fact]
    public void PropertyNameNormalizationPreservesExistingNameSemantics()
    {
        Assert.Equal("font-size", StyleAccessor.NormalizePropertyName(" fontSize "));
        Assert.Equal("--Accent", StyleAccessor.NormalizePropertyName(" --Accent "));
        Assert.NotEqual(
            StyleAccessor.NormalizePropertyName("--Accent"),
            StyleAccessor.NormalizePropertyName("--accent"));
    }

    [Fact]
    public void StyleAccessorCssomSetPropertyAndGetPropertyValue()
    {
        var view = new View();
        view.Style.SetProperty("fontSize", "18px");
        view.Style.SetProperty("color", "#112233");

        Assert.Equal("18px", view.Style.GetPropertyValue("font-size"));
        Assert.Equal("18px", view.Style.GetPropertyValue("fontSize"));
        Assert.Equal("#112233", view.Style.GetPropertyValue("color"));
        Assert.Equal("", view.Style.GetPropertyValue("missing"));
    }

    [Fact]
    public void CssZIndexUpdatesElementStackingOrder()
    {
        var view = new View();

        view.Style.Set("z-index", "30");

        Assert.Equal(30, view.ZIndex);

        view.Style.Remove("z-index");

        Assert.Equal(0, view.ZIndex);
    }

    [Fact]
    public void StyleClassAndContentChangesInvalidateLayoutToRoot()
    {
        var root = new View();
        var text = new Square.Controls.Text("short");
        root.Children.Add(text);
        root.ClearLayoutDirty();
        text.ClearLayoutDirty();

        text.Style.SetProperty("width", "200px");
        Assert.True(text.IsLayoutDirty);
        Assert.True(root.IsLayoutDirty);

        root.ClearLayoutDirty();
        text.ClearLayoutDirty();
        text.ClassList.Add("wide");
        Assert.True(text.IsLayoutDirty);
        Assert.True(root.IsLayoutDirty);

        root.ClearLayoutDirty();
        text.ClearLayoutDirty();
        text.TextContent = "content with a different intrinsic width";
        Assert.True(text.IsLayoutDirty);
        Assert.True(root.IsLayoutDirty);
    }

    [Fact]
    public void PaintOnlyStyleChangesDoNotInvalidateLayout()
    {
        var root = new View();
        var child = new View();
        root.Children.Add(child);
        root.ClearLayoutDirty();
        child.ClearLayoutDirty();
        root.ClearPaintDirty();
        child.ClearPaintDirty();

        child.Style.SetProperty("background", "#ff0000");

        Assert.False(child.IsLayoutDirty);
        Assert.False(root.IsLayoutDirty);
        Assert.True(child.NeedsPaint);
    }

    [Fact]
    public void BorderRadiusStyleChangesDoNotInvalidateLayout()
    {
        var root = new View();
        var child = new View();
        root.Children.Add(child);
        root.ClearLayoutDirty();
        child.ClearLayoutDirty();
        child.ClearPaintDirty();

        child.Style.SetProperty("border-radius", "8px");

        Assert.False(child.IsLayoutDirty);
        Assert.False(root.IsLayoutDirty);
        Assert.True(child.NeedsPaint);
    }

    [Fact]
    public void StyleAccessorCssTextRoundTrip()
    {
        var view = new View();
        view.Style.CssText = "color: red; font-size: 20px";

        Assert.Equal("red", view.Style.GetPropertyValue("color"));
        Assert.Equal("20px", view.Style.GetPropertyValue("font-size"));
        Assert.Contains("color:", view.Style.CssText, StringComparison.Ordinal);
        Assert.Contains("font-size:", view.Style.CssText, StringComparison.Ordinal);

        var removed = view.Style.RemoveProperty("color");
        Assert.Equal("red", removed);
        Assert.Equal("", view.Style.GetPropertyValue("color"));
    }

    [Fact]
    public void StyleAccessorCssomPriorityAndEmptyValueMatchChrome()
    {
        var view = new View();
        view.Style.SetProperty("color", "red", "important");

        Assert.Equal("red", view.Style.GetPropertyValue("color"));
        Assert.Equal("important", view.Style.GetPropertyPriority("color"));
        Assert.Contains("color: red !important;", view.Style.CssText, StringComparison.Ordinal);

        view.Style.SetProperty("color", "blue", "invalid");
        Assert.Equal("red", view.Style.GetPropertyValue("color"));

        view.Style.SetProperty("color", "");
        Assert.Equal("", view.Style.GetPropertyValue("color"));
        Assert.Equal(0, view.Style.Length);
    }

    [Fact]
    public void CssomMembersExposeOnlyInlineDeclarations()
    {
        var view = new View();
        view.Style.SetCascaded("color", "blue", 10);

        Assert.Equal("blue", view.Style.Get("color"));
        Assert.Equal("", view.Style.GetPropertyValue("color"));
        Assert.Equal("", view.Style.CssText);
        Assert.Equal(0, view.Style.Length);
    }

    [Fact]
    public void CssInitialInheritAndUnsetUsePropertyRegistry()
    {
        var parent = new View();
        var child = new View();
        parent.Children.Add(child);
        parent.Style.SetProperty("color", "red");
        parent.Style.SetProperty("white-space", "pre");
        parent.Style.SetProperty("width", "120px");

        Assert.Equal("red", child.Style.Get("color"));
        Assert.Equal("pre", child.Style.Get("white-space"));

        child.Style.SetProperty("width", "inherit");
        Assert.Equal("120px", child.Style.Get("width"));

        child.Style.SetProperty("color", "initial");
        child.Style.SetProperty("width", "initial");
        Assert.Equal("black", child.Style.Get("color"));
        Assert.Equal("auto", child.Style.Get("width"));

        child.Style.SetProperty("color", "unset");
        child.Style.SetProperty("width", "unset");
        Assert.Equal("red", child.Style.Get("color"));
        Assert.Equal("auto", child.Style.Get("width"));
    }

    [Fact]
    public void CssShorthandsExpandToValidatedLonghands()
    {
        var view = new View();

        view.Style.SetProperty("margin", "1px 2px 3px 4px", "important");
        view.Style.SetProperty("padding", "5px 6px");
        view.Style.SetProperty("border-width", "1px 2px 3px 4px");
        view.Style.SetProperty("border-color", "red green blue black");
        view.Style.SetProperty("border-style", "solid dashed dotted double");
        view.Style.SetProperty("border", "7px solid #123456");
        view.Style.SetProperty("border-left", "9px dashed red");
        view.Style.SetProperty("background", "#abcdef");
        view.Style.SetProperty("font", "italic small-caps 700 16px/24px 'Segoe UI', sans-serif");
        view.Style.SetProperty("outline", "2px dotted blue");
        view.Style.SetProperty("list-style", "square inside");

        Assert.Equal("1px", view.Style.GetPropertyValue("margin-top"));
        Assert.Equal("2px", view.Style.GetPropertyValue("margin-right"));
        Assert.Equal("3px", view.Style.GetPropertyValue("margin-bottom"));
        Assert.Equal("4px", view.Style.GetPropertyValue("margin-left"));
        Assert.Equal("important", view.Style.GetPropertyPriority("margin-left"));
        Assert.Equal("5px", view.Style.GetPropertyValue("padding-top"));
        Assert.Equal("6px", view.Style.GetPropertyValue("padding-left"));
        Assert.Equal("7px", view.Style.GetPropertyValue("border-top-width"));
        Assert.Equal("solid", view.Style.GetPropertyValue("border-right-style"));
        Assert.Equal("#123456", view.Style.GetPropertyValue("border-bottom-color"));
        Assert.Equal("9px", view.Style.GetPropertyValue("border-left-width"));
        Assert.Equal("dashed", view.Style.GetPropertyValue("border-left-style"));
        Assert.Equal("red", view.Style.GetPropertyValue("border-left-color"));
        Assert.Equal("#abcdef", view.Style.GetPropertyValue("background-color"));
        Assert.Equal("italic", view.Style.GetPropertyValue("font-style"));
        Assert.Equal("small-caps", view.Style.GetPropertyValue("font-variant"));
        Assert.Equal("700", view.Style.GetPropertyValue("font-weight"));
        Assert.Equal("16px", view.Style.GetPropertyValue("font-size"));
        Assert.Equal("24px", view.Style.GetPropertyValue("line-height"));
        Assert.Equal("'Segoe UI', sans-serif", view.Style.GetPropertyValue("font-family"));
        Assert.Equal("2px", view.Style.GetPropertyValue("outline-width"));
        Assert.Equal("dotted", view.Style.GetPropertyValue("outline-style"));
        Assert.Equal("blue", view.Style.GetPropertyValue("outline-color"));
        Assert.Equal("square", view.Style.GetPropertyValue("list-style-type"));
        Assert.Equal("inside", view.Style.GetPropertyValue("list-style-position"));
        Assert.Equal("none", view.Style.GetPropertyValue("list-style-image"));
    }

    [Fact]
    public void CascadedShorthandLonghandsKeepDeclarationPriority()
    {
        var view = new View();
        view.Style.SetCascaded("margin-left", "9px", new CssSpecificity(0, 1, 0), important: false);
        view.Style.SetCascaded("margin", "1px 2px", new CssSpecificity(0, 0, 1), important: false);

        Assert.Equal("1px", view.Style.Get("margin-top"));
        Assert.Equal("9px", view.Style.Get("margin-left"));

        view.Style.SetCascaded("margin", "3px", new CssSpecificity(0, 0, 0), important: true);

        Assert.Equal("3px", view.Style.Get("margin-top"));
        Assert.Equal("3px", view.Style.Get("margin-left"));
    }

    [Fact]
    public void InvalidCssDeclarationsAreRejectedWithoutPartialMutation()
    {
        var view = new View();
        view.Style.SetProperty("width", "20px");
        view.Style.SetProperty("padding", "4px");

        view.Style.SetProperty("width", "wide");
        view.Style.SetProperty("padding", "1px -2px 3px");
        view.Style.SetProperty("border", "1px solid red extra");
        view.Style.SetProperty("background", "url(image.png)");

        Assert.Equal("20px", view.Style.GetPropertyValue("width"));
        Assert.Equal("4px", view.Style.GetPropertyValue("padding"));
        Assert.Equal("4px", view.Style.GetPropertyValue("padding-top"));
        Assert.Equal("", view.Style.GetPropertyValue("border"));
        Assert.Equal("", view.Style.GetPropertyValue("border-top-width"));
        Assert.Equal("", view.Style.GetPropertyValue("background"));
        Assert.Equal("", view.Style.GetPropertyValue("background-color"));
        Assert.False(view.Style.SetCascaded("display", "sideways", 10));
        Assert.Null(view.Style.Get("display"));
    }

    [Fact]
    public void FontParseFamilyListAndWeight()
    {
        var list = Font.ParseFamilyList("\"Segoe UI\", Tahoma, sans-serif");
        Assert.Equal(new[] { "Segoe UI", "Tahoma", "sans-serif" }, list);

        Assert.Equal(FontWeight.Bold, Font.ParseWeight("bold"));
        Assert.Equal(FontWeight.Normal, Font.ParseWeight("normal"));
        Assert.Equal((FontWeight)600, Font.ParseWeight("600"));
        Assert.Equal(18f, Font.ParseSize("18px"));
        Assert.Equal(FontStyle.Italic, Font.ParseStyle("italic"));
    }

    [Fact]
    public void FontFromCssUsesFirstFamilyOrGeneric()
    {
        var segoe = Font.FromCss("\"Segoe UI\", sans-serif", "14px", "bold", "italic");
        Assert.Equal("Segoe UI", segoe.Family);
        Assert.Equal(14f, segoe.Size);
        Assert.Equal(FontWeight.Bold, segoe.Weight);
        Assert.Equal(FontStyle.Italic, segoe.Style);

        var generic = Font.FromCss("sans-serif", "16px");
        Assert.Equal("Segoe UI", generic.Family);

        // FontManager：未知族后回退到列表中已知通用族
        var viaManager = Square.Text.FontManager.Instance.FromCss(
            "\"My Missing\", sans-serif", "14px", "700");
        Assert.Equal("Segoe UI", viaManager.Family);
        Assert.Equal(14f, viaManager.Size);
        Assert.Equal(FontWeight.Bold, viaManager.Weight);
    }

    [Fact]
    public void ControlDrawingResolvesFontFromElementStyle()
    {
        var text = new Square.Controls.Text("hi");
        text.Style.SetProperty("font-family", "monospace");
        text.Style.SetProperty("font-size", "12px");
        text.Style.SetProperty("font-weight", "700");

        // 通过 Measure 间接验证不会抛错且尺寸随字号变化
        var small = text.Measure(new Size(1000, 1000));
        text.Style.SetProperty("font-size", "24px");
        var large = text.Measure(new Size(1000, 1000));
        Assert.True(large.Width >= small.Width);
        Assert.True(large.Height >= small.Height);
    }

    [Fact]
    public void TextMeasureUsesCssLineHeight()
    {
        var text = new Square.Controls.Text("first\nsecond");
        text.Style.SetProperty("font-size", "16px");
        text.Style.SetProperty("line-height", "30px");

        var measured = text.Measure(new Size(300, 300));

        Assert.Equal(60, measured.Height);
    }

    [Fact]
    public void QuerySelectorFindsByIdClassAndDescendant()
    {
        var root = new View { Id = "root" };
        var child = new View();
        child.ClassList.Add("panel");
        var deep = new Square.Controls.Text("x") { Id = "label" };
        root.AppendChild(child);
        child.AppendChild(deep);

        Assert.Same(deep, root.QuerySelector("#label"));
        Assert.Same(child, root.QuerySelector(".panel"));
        Assert.Same(deep, root.QuerySelector("View Text"));
        Assert.Same(deep, root.QuerySelector("View > Text"));
        Assert.Null(root.QuerySelector("#missing"));
    }
}
