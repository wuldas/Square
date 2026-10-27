using Square.Compiler.LanguageServices;
using Square.Html;
using Square.UI;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class HtmlConcreteTypesTests
{
    [Fact]
    public void EveryCatalogTagHasItsOwnConcreteElementAndCompilerDescriptor()
    {
        var descriptors = TemplateCatalog.BuiltIn.Components
            .Where(component => component.NamespaceUri == TemplateCatalog.HtmlNamespaceUri)
            .ToDictionary(component => component.LocalName, StringComparer.Ordinal);
        Assert.Equal(113, descriptors.Count);

        var concreteTypes = new HashSet<Type>();
        var document = new UIDocument();
        foreach (var tag in descriptors.Keys)
        {
            var element = HtmlElementFactory.Create(tag);
            var type = element.GetType();
            var created = document.CreateElement(tag);
            Assert.Equal(type, created.GetType());
            Assert.Same(document, created.OwnerDocument);
            Assert.True(concreteTypes.Add(type), $"{tag} reuses {type.FullName}");
            Assert.Equal(tag, element.TagName);
            Assert.Equal(tag, element.LocalName);
            Assert.Equal(TemplateCatalog.HtmlNamespaceUri, element.NamespaceURI);
            Assert.False(typeof(UIElement).IsAssignableFrom(type));
            Assert.False(type.IsAbstract || type.IsSealed);
            Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
            Assert.Equal(type.FullName, descriptors[tag].TypeName);
        }
    }

    [Theory]
    [InlineData("h1", typeof(HTMLHeadingElement))]
    [InlineData("h6", typeof(HTMLHeadingElement))]
    [InlineData("blockquote", typeof(HTMLQuoteElement))]
    [InlineData("q", typeof(HTMLQuoteElement))]
    [InlineData("col", typeof(HTMLTableColElement))]
    [InlineData("colgroup", typeof(HTMLTableColElement))]
    [InlineData("del", typeof(HTMLModElement))]
    [InlineData("ins", typeof(HTMLModElement))]
    [InlineData("tbody", typeof(HTMLTableSectionElement))]
    [InlineData("tfoot", typeof(HTMLTableSectionElement))]
    [InlineData("thead", typeof(HTMLTableSectionElement))]
    [InlineData("td", typeof(HTMLTableCellElement))]
    [InlineData("th", typeof(HTMLTableCellElement))]
    public void SharedDomInterfacesRemainAbstractBases(string tag, Type baseType)
    {
        Assert.True(baseType.IsAbstract);
        Assert.Equal(baseType, HtmlElementFactory.Create(tag).GetType().BaseType);
    }
}
