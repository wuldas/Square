using Square.Compiler.Parser;
using Square.Compiler.Syntax;
using Square.Compiler.Emit;
using Square.Compiler.Template;
using Square.Compiler.Template.Compatibility;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class TemplateIrCompatibilityAdapterTests
{
    [Fact]
    public void AdapterRestoresLegacyControlFlowAndDynamicBindingShape()
    {
        const string source = "<template><View><Text v-for=\"item in Items\" :key=\"item.Id\" :[propertyName]=\"item\" /><Button v-if=\"Ready\" @[eventName]=\"Handle\" /></View></template>";
        var syntax = ComponentSectionScanner.Scan(source, "Card.sqv", ComponentDialect.Sqv, tolerant: false)
            .Document.Template;

        var roots = TemplateIrCompatibilityAdapter.ToSqxNodes(syntax.Ir, source);
        var view = Assert.IsType<SqxElement>(Assert.Single(roots));
        var loop = Assert.IsType<TemplateForDirective>(view.Children[0]);
        var item = Assert.IsType<SqxElement>(Assert.Single(loop.Children));
        var condition = Assert.IsType<TemplateIfChainDirective>(view.Children[1]);
        var button = Assert.IsType<SqxElement>(Assert.Single(Assert.Single(condition.Branches).Children));

        Assert.Equal("Items", loop.SourceExpression);
        Assert.Equal("item.Id", loop.KeyExpression);
        Assert.Equal("propertyName", Assert.Single(item.Attributes, attribute => attribute.IsDynamicProperty).ArgumentExpression);
        Assert.Equal("eventName", Assert.Single(button.Attributes, attribute => attribute.IsDynamicEvent).ArgumentExpression);
        Assert.True(view.Line > 0);
        Assert.True(view.Column > 0);
    }

    [Theory]
    [InlineData("Parity.sqx", "<template><Show when={Ready} fallback={<Text text=\"no\" />}><For each={Items}>{(item)=><Text text={item} />}</For></Show></template>")]
    [InlineData("Parity.sqv", "<template><View><Text v-for=\"item in Items\" :key=\"item.Id\">{{ item.Name }}</Text><Button v-if=\"Ready\" @click=\"OnSave\" /></View></template>")]
    public void AdapterPreservesLegacyGeneratedOutput(string fileName, string source)
    {
        var parsed = fileName.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase)
            ? SqvDocumentParser.Parse(source, fileName)
            : SqxParser.Parse(source, fileName);
        var expected = new ComponentEmitter(parsed).Emit();
        var adapted = TemplateDocument.From(parsed);
        adapted.Roots = TemplateIrCompatibilityAdapter.ToSqxNodes(
            parsed.Syntax.Template.Ir,
            source,
            parsed.Syntax.Dialect,
            parsed.Syntax.Template.ContentRange.Offset);

        var actual = new ComponentEmitter(adapted).Emit();

        Assert.Equal(expected, actual);
    }
}
