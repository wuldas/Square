using Square.Compiler.Parser;
using Square.Compiler.Syntax;
using Square.Compiler.Template.Lowering;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class SqvTemplateSyntaxTests
{
    [Fact]
    public void SqvSourceSyntaxPreservesDirectiveShorthandsAndModifiers()
    {
        const string source = "<template><Button :value=\"Name\" @click.stop.prevent=\"OnSave\" #header=\"slot\" v-if=\"Visible\" /></template>";
        var section = ComponentSectionScanner.Scan(source, "Card.sqv", ComponentDialect.Sqv, tolerant: false)
            .Document.Template;

        var syntax = SqvTemplateSyntaxParser.Parse(section.ContentText, section.ContentRange.Offset, tolerant: false);
        var button = Assert.IsType<SqvElementSyntax>(Assert.Single(syntax.Roots));

        Assert.Equal("Button", button.TagName);
        Assert.True(button.IsSelfClosing);
        Assert.Collection(
            button.Attributes,
            attribute => AssertAttribute(source, attribute, ":value", "Name"),
            attribute => AssertAttribute(source, attribute, "@click.stop.prevent", "OnSave"),
            attribute => AssertAttribute(source, attribute, "#header", "slot"),
            attribute => AssertAttribute(source, attribute, "v-if", "Visible"));
        Assert.Equal(new[] { "stop", "prevent" }, button.Attributes[1].Modifiers);
    }

    [Fact]
    public void SqvSourceSyntaxPreservesDynamicArgumentAndModifierRanges()
    {
        const string source = "<template><Button :[propertyName].camel=\"Value\" @[eventName].stop=\"Handle\" /></template>";
        var section = ComponentSectionScanner.Scan(source, "Card.sqv", ComponentDialect.Sqv, tolerant: false)
            .Document.Template;

        var syntax = SqvTemplateSyntaxParser.Parse(section.ContentText, section.ContentRange.Offset, tolerant: false);
        var button = Assert.IsType<SqvElementSyntax>(Assert.Single(syntax.Roots));
        var property = button.Attributes[0];
        var @event = button.Attributes[1];

        Assert.Equal("bind", property.DirectiveName);
        Assert.True(property.ArgumentIsDynamic);
        Assert.Equal("propertyName", property.Argument);
        Assert.Equal("propertyName", source.Substring(property.ArgumentRange.Offset, property.ArgumentRange.Length));
        var camel = Assert.Single(property.ModifierSyntaxes);
        Assert.Equal("camel", source.Substring(camel.Range.Offset, camel.Range.Length));
        Assert.Equal("on", @event.DirectiveName);
        Assert.True(@event.ArgumentIsDynamic);
        Assert.Equal("eventName", @event.Argument);
        Assert.Equal("eventName", source.Substring(@event.ArgumentRange.Offset, @event.ArgumentRange.Length));
        var stop = Assert.Single(@event.ModifierSyntaxes);
        Assert.Equal("stop", source.Substring(stop.Range.Offset, stop.Range.Length));
    }

    [Fact]
    public void SqvInterpolationRangeIncludesDelimiterWhitespace()
    {
        const string source = "<template><Text>{{  名称  }}</Text></template>";
        var template = ComponentSectionScanner.Scan(
            source,
            "Card.sqv",
            ComponentDialect.Sqv,
            tolerant: false).Document.Template;

        var text = Assert.IsType<SqvElementSyntax>(Assert.Single(template.SqvSyntax.Roots));
        var interpolation = Assert.IsType<SqvInterpolationSyntax>(Assert.Single(text.Children));

        Assert.Equal("名称", interpolation.Expression);
        Assert.Equal("{{  名称  }}", source.Substring(interpolation.Origin.Offset, interpolation.Origin.Length));
    }

    [Fact]
    public void UnsupportedVueDirectiveFormsSyntaxBeforeLoweringDiagnostic()
    {
        var syntax = SqvTemplateSyntaxParser.Parse("<Text v-html=\"Markup\" />", tolerant: false);
        var text = Assert.IsType<SqvElementSyntax>(Assert.Single(syntax.Roots));

        Assert.Equal("v-html", Assert.Single(text.Attributes).Name);
        var exception = Assert.Throws<SqxParseException>(() => SqvTemplateLowerer.Lower(syntax));
        Assert.Equal("SQV0002", exception.DiagnosticId);
    }

    [Fact]
    public void ComponentSyntaxRetainsSqvSourceAstWhenLoweringRejectsDirective()
    {
        const string source = "<template><Text v-html=\"Markup\" /></template>";

        var template = ComponentSectionScanner.Scan(
            source,
            "Card.sqv",
            ComponentDialect.Sqv,
            tolerant: false).Document.Template;

        var text = Assert.IsType<SqvElementSyntax>(Assert.Single(template.SqvSyntax.Roots));
        Assert.Equal("v-html", Assert.Single(text.Attributes).Name);
        Assert.Empty(template.Ir.Roots);
    }

    [Theory]
    [InlineData("<Card ", "Card")]
    [InlineData("<a:card ", "a:card")]
    [InlineData("<a:", "a:")]
    public void TolerantSqvTemplateKeepsElementReachingEndOfInput(string content, string expectedTagName)
    {
        var syntax = SqvTemplateSyntaxParser.Parse(content, tolerant: true);

        var element = Assert.IsType<SqvElementSyntax>(Assert.Single(syntax.Roots));
        Assert.Equal(expectedTagName, element.TagName);
        Assert.False(element.IsSelfClosing);
        Assert.Equal(content.Length, element.Origin.Offset + element.Origin.Length);
        Assert.Throws<SqxParseException>(() => SqvTemplateSyntaxParser.Parse(content, tolerant: false));
    }

    [Fact]
    public void TolerantSqvTemplateRecordsDiagnosticsAndLowersRecoverableTree()
    {
        const string content = "<Card><View></Card>";
        const string source = "<template>" + content + "</template>";
        var tolerant = ComponentSectionScanner.Scan(source, "Card.sqv", ComponentDialect.Sqv, tolerant: true).Document.Template;

        Assert.True(tolerant.HasErrors);
        Assert.NotEmpty(tolerant.Diagnostics);
        Assert.All(tolerant.Diagnostics, diagnostic =>
        {
            Assert.Equal("SQV0001", diagnostic.Id);
            Assert.True(diagnostic.Range.Length > 0);
            Assert.InRange(diagnostic.Range.Offset, tolerant.ContentRange.Offset, source.Length);
        });
        var root = Assert.IsType<SqvElementSyntax>(Assert.Single(tolerant.SqvSyntax.Roots));
        Assert.Equal("Card", root.TagName);
        var ir = Assert.IsType<Square.Compiler.Template.Ir.TemplateIrElement>(Assert.Single(tolerant.Ir.Roots));
        Assert.Equal("Card", ir.TagName);

        var strict = Assert.Throws<SqxParseException>(() => SqvTemplateSyntaxParser.Parse(content, tolerant: false));
        Assert.Equal("SQV0001", strict.DiagnosticId);

        var strictSection = ComponentSectionScanner.Scan(source, "Card.sqv", ComponentDialect.Sqv, tolerant: false).Document.Template;
        Assert.True(strictSection.HasErrors);
        Assert.Contains(strictSection.Diagnostics, diagnostic => diagnostic.Id == "SQV0001");
    }

    private static void AssertAttribute(
        string source,
        SqvAttributeSyntax attribute,
        string expectedName,
        string expectedValue)
    {
        Assert.Equal(expectedName, attribute.Name);
        Assert.Equal(expectedValue, attribute.Value);
        Assert.Equal(expectedName, source.Substring(attribute.NameRange.Offset, attribute.NameRange.Length));
        Assert.Equal(expectedValue, source.Substring(attribute.ValueRange.Offset, attribute.ValueRange.Length));
    }
}
