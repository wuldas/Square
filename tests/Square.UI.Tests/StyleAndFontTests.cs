using System;
using Square.Controls;
using Square.Graphics;
using Square.UI;
using Xunit;

namespace Square.UI.Tests;

public class StyleAndFontTests
{
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
