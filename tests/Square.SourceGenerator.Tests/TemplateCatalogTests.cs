using Square.Compiler.LanguageServices;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class TemplateCatalogTests
{
    [Theory]
    [InlineData("View", "Square.Controls.View")]
    [InlineData("BUTTON", "Square.Controls.Button")]
    [InlineData("svg", "Square.UI.Svg.SVGSVGElement")]
    [InlineData("SplitContainer", "Square.Controls.SplitContainer")]
    public void ResolvesBuiltInComponentType(string tagName, string expectedTypeName)
    {
        var catalog = TemplateCatalog.BuiltIn;

        var descriptor = Assert.IsType<TemplateComponentDescriptor>(
            catalog.GetComponent(tagName));

        Assert.Equal(expectedTypeName, descriptor.TypeName);
        Assert.True(descriptor.IsBuiltIn);
    }

    [Fact]
    public void UnknownComponentRemainsCustomAndUsesItsTagName()
    {
        var descriptor = TemplateCatalog.BuiltIn.GetComponent("MyCard");

        Assert.Equal("MyCard", descriptor.TagName);
        Assert.Equal("MyCard", descriptor.TypeName);
        Assert.False(descriptor.IsBuiltIn);
        Assert.True(descriptor.RequiresBuildAfterAttach);
    }

    [Theory]
    [InlineData("id", "Id")]
    [InlineData("font-family", "FontFamily")]
    [InlineData("selected-index", "SelectedIndex")]
    [InlineData("stroke-width", "StrokeWidth")]
    [InlineData("unknown-prop", "unknown-prop")]
    public void MapsMarkupPropertyAliases(string markupName, string expectedPropertyName)
    {
        Assert.Equal(expectedPropertyName, TemplateCatalog.BuiltIn.MapPropertyName(markupName));
    }

    [Theory]
    [InlineData("Text")]
    [InlineData("Button")]
    [InlineData("Link")]
    [InlineData("ListItem")]
    [InlineData("TreeItem")]
    public void IdentifiesTextContentComponents(string tagName)
    {
        Assert.True(TemplateCatalog.BuiltIn.GetComponent(tagName).IsTextContentElement);
    }

    [Fact]
    public void CatalogExposesDistinctBuiltInComponentDescriptors()
    {
        var descriptors = TemplateCatalog.BuiltIn.Components
            .Where(descriptor => descriptor.IsBuiltIn)
            .ToArray();

        Assert.NotEmpty(descriptors);
        Assert.Equal(
            descriptors.Length,
            descriptors.Select(descriptor => descriptor.TagName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(descriptors, descriptor => descriptor.TagName == "SplitContainer");
    }

    [Fact]
    public void CatalogExposesStandardEventsForLanguageTooling()
    {
        var names = TemplateCatalog.BuiltIn.Events
            .Select(eventDescriptor => eventDescriptor.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("click", names);
        Assert.Contains("input", names);
        Assert.Contains("requestframe", names);
    }
}
