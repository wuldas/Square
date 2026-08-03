using Square.Controls;
using Square.Runtime;
using Square.Runtime.Binding;
using Xunit;

namespace Square.UI.Tests;

public sealed class DynamicClassStyleBindingTests
{
    [Fact]
    public void ClassPropertyExpressionUpdatesClassList()
    {
        var view = new View();

        view.SetProperty("class", "primary large");

        Assert.True(view.ClassList.Contains("primary"));
        Assert.True(view.ClassList.Contains("large"));
        Assert.False(view.Properties.HasValue("class"));
    }

    [Fact]
    public void ReactiveClassAndStyleBindingsUpdateDedicatedAccessors()
    {
        var view = new View();
        var classes = new ObservableValue<string>("primary");
        var style = new ObservableValue<string>("color: red");

        view.BindProperty("class", classes);
        view.BindProperty("style", style);
        classes.Value = "secondary selected";
        style.Value = "color: blue !important; width: 20px";

        Assert.False(view.ClassList.Contains("primary"));
        Assert.True(view.ClassList.Contains("secondary"));
        Assert.True(view.ClassList.Contains("selected"));
        Assert.Equal("blue", view.Style.GetPropertyValue("color"));
        Assert.Equal("important", view.Style.GetPropertyPriority("color"));
        Assert.Equal("20px", view.Style.GetPropertyValue("width"));
    }
}
