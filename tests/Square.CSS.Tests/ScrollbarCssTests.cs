using Square.CSS.Properties;
using Square.Controls;
using Xunit;

namespace Square.CSS.Tests;

public sealed class ScrollbarCssTests
{
    [Fact]
    public void ScrollbarWidthAcceptsOnlySupportedKeywords()
    {
        Assert.Equal("auto", CssPropertyRegistry.GetInitialValue("scrollbar-width"));
        Assert.False(CssPropertyRegistry.IsInherited("scrollbar-width"));
        Assert.True(CssPropertyRegistry.IsValid("scrollbar-width", "auto"));
        Assert.True(CssPropertyRegistry.IsValid("scrollbar-width", "thin"));
        Assert.True(CssPropertyRegistry.IsValid("scrollbar-width", "none"));
        Assert.False(CssPropertyRegistry.IsValid("scrollbar-width", "15px"));
        Assert.False(CssPropertyRegistry.IsValid("scrollbar-width", "wide"));
    }

    [Fact]
    public void ScrollbarColorRequiresAutoOrTwoColors()
    {
        Assert.Equal("auto", CssPropertyRegistry.GetInitialValue("scrollbar-color"));
        Assert.True(CssPropertyRegistry.IsInherited("scrollbar-color"));
        Assert.True(CssPropertyRegistry.IsValid("scrollbar-color", "auto"));
        Assert.True(CssPropertyRegistry.IsValid("scrollbar-color", "#c1c1c1 #f1f1f1"));
        Assert.True(CssPropertyRegistry.IsValid("scrollbar-color", "rgba(128, 128, 128, .5) transparent"));
        Assert.False(CssPropertyRegistry.IsValid("scrollbar-color", "#c1c1c1"));
        Assert.False(CssPropertyRegistry.IsValid("scrollbar-color", "#c1c1c1 #f1f1f1 extra"));
        Assert.False(CssPropertyRegistry.IsValid("scrollbar-color", "hsl(0, 100%, 50%) #f1f1f1"));
        Assert.False(CssPropertyRegistry.IsValid("scrollbar-color", "not-a-color #f1f1f1"));
    }

    [Fact]
    public void ScrollbarWidthInvalidatesLayoutWhileColorOnlyInvalidatesPaint()
    {
        var width = new View();
        width.ClearLayoutDirty();
        width.ClearPaintDirty();
        width.Style.Set("scrollbar-width", "thin");

        Assert.True(width.IsLayoutDirty);
        Assert.True(width.NeedsPaint);

        var color = new View();
        color.ClearLayoutDirty();
        color.ClearPaintDirty();
        color.Style.Set("scrollbar-color", "#c1c1c1 #f1f1f1");

        Assert.False(color.IsLayoutDirty);
        Assert.True(color.NeedsPaint);
    }

    [Fact]
    public void OverflowInvalidatesLayoutBecauseScrollbarViewportCanChange()
    {
        var view = new View();
        view.ClearLayoutDirty();
        view.ClearPaintDirty();

        view.Style.Set("overflow-y", "auto");

        Assert.True(view.IsLayoutDirty);
        Assert.True(view.NeedsPaint);
    }

    [Fact]
    public void InheritedScrollbarColorInvalidatesDescendantPaint()
    {
        var parent = new View();
        var child = new View();
        parent.Children.Add(child);
        _ = child.Style.Get("scrollbar-color");
        parent.ClearPaintDirty();
        child.ClearPaintDirty();

        parent.Style.Set("scrollbar-color", "#102030 #405060");

        Assert.True(parent.NeedsPaint);
        Assert.True(child.NeedsPaint);
    }

    [Fact]
    public void ScrollbarGutterAcceptsAutoOrStable()
    {
        Assert.Equal("auto", CssPropertyRegistry.GetInitialValue("scrollbar-gutter"));
        Assert.False(CssPropertyRegistry.IsInherited("scrollbar-gutter"));
        Assert.True(CssPropertyRegistry.IsValid("scrollbar-gutter", "auto"));
        Assert.True(CssPropertyRegistry.IsValid("scrollbar-gutter", "stable"));
        Assert.True(CssPropertyRegistry.IsValid("scrollbar-gutter", "stable both-edges"));
    }
}
