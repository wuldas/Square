using Square.Controls;
using Xunit;

namespace Square.UI.Tests;

public class CssDisplayStateTests
{
    [Fact]
    public void AncestorDisplayChangesAffectAlreadyQueriedDescendants()
    {
        var parent = new View();
        var child = new View();
        parent.Children.Add(child);
        Assert.True(child.IsCssDisplayed());

        parent.Style.Set("display", "none");
        Assert.False(child.IsCssDisplayed());

        parent.Style.Set("display", "block");
        Assert.True(child.IsCssDisplayed());

        child.Style.Set("display", "none");
        Assert.False(child.IsCssDisplayed());
        child.Style.Remove("display");
        Assert.True(child.IsCssDisplayed());
    }

    [Fact]
    public void LocalCustomPropertyChangesRefreshDisplayState()
    {
        var child = new View();
        child.Style.Set("--mode", "none");
        child.Style.Set("display", "var(--mode)");
        Assert.False(child.IsCssDisplayed());

        child.Style.Set("--mode", "block");
        Assert.True(child.IsCssDisplayed());

        child.Style.Set("--mode", "none");
        Assert.False(child.IsCssDisplayed());
    }

    [Fact]
    public void ReparentingRefreshesExplicitlyInheritedDisplay()
    {
        var hidden = new View();
        hidden.Style.Set("display", "none");
        var visible = new View();
        visible.Style.Set("display", "block");
        var child = new View();
        child.Style.Set("display", "inherit");
        hidden.Children.Add(child);
        Assert.False(child.IsCssDisplayed());

        hidden.Children.Remove(child);
        visible.Children.Add(child);

        Assert.True(child.IsCssDisplayed());
    }
}
