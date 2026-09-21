using Square.Compiler.Parser;
using Square.Compiler.Syntax;
using Square.Compiler.Directives;
using Square.Compiler.Emit;
using Square.Compiler.LanguageServices;
using Square.Compiler.Template;
using Square.Compiler.Template.Compatibility;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class TemplateIrCompatibilityAdapterTests
{
    private static readonly DirectiveCatalog Catalog = DirectiveCatalog.BuiltIn;
    private static readonly TemplateCatalog ElementCatalog = TemplateCatalog.BuiltIn;

    private static string EmitLegacy(SqxDocument parsed)
    {
        var legacy = TemplateDocument.From(parsed);
        legacy.Ir = null!;
        return new ComponentEmitter(
            legacy, parsed.Namespace ?? "Square.Sample", Catalog, null!, ElementCatalog,
            new TemplateResolutionContext(parsed.Namespace ?? string.Empty, Array.Empty<string>()),
            Resolve(parsed)).Emit();
    }

    private static string EmitIr(SqxDocument parsed) => new ComponentEmitter(
        parsed, parsed.Namespace ?? "Square.Sample", Catalog, null!, ElementCatalog,
        new TemplateResolutionContext(parsed.Namespace ?? string.Empty, Array.Empty<string>()),
        Resolve(parsed)).Emit();

    private static IReadOnlyDictionary<string, TemplateElementResolution> Resolve(SqxDocument parsed)
    {
        var context = new TemplateResolutionContext(parsed.Namespace ?? string.Empty, Array.Empty<string>());
        var result = new Dictionary<string, TemplateElementResolution>(StringComparer.Ordinal);
        Collect(parsed.Template.Roots, context, result);
        return result;

        static void Collect(IEnumerable<SqxNode> nodes, TemplateResolutionContext ctx,
            Dictionary<string, TemplateElementResolution> target)
        {
            foreach (var node in nodes)
            {
                if (node is SqxElement element)
                {
                    target[element.TagName] = ElementCatalog.ResolveComponent(element.TagName, ctx);
                    Collect(element.Children, ctx, target);
                }
                else if (node is TemplateForDirective forDirective)
                {
                    Collect(forDirective.Children, ctx, target);
                }
                else if (node is TemplateIfChainDirective chain)
                {
                    foreach (var branch in chain.Branches) Collect(branch.Children, ctx, target);
                }
            }
        }
    }

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
        var expected = EmitLegacy(parsed);

        var actual = EmitIr(parsed);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ComponentEmitterUsesSectionIrWhenLegacyRootsAreEmpty()
    {
        const string source = "<template><Button text=\"Save\" onClick={OnSave} /></template>";
        var parsed = SqxParser.Parse(source, "Parity.sqx");
        var expected = EmitLegacy(parsed);
        var resolutions = Resolve(parsed);
        var document = TemplateDocument.From(parsed);
        document.Roots.Clear();

        var actual = new ComponentEmitter(
            document, parsed.Namespace ?? "Square.Sample", Catalog, null!, ElementCatalog,
            new TemplateResolutionContext(parsed.Namespace ?? string.Empty, Array.Empty<string>()),
            resolutions).Emit();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DirectiveValidatorUsesIrWhenLegacyRootsAreEmpty()
    {
        const string source = "<template><Show><Text /></Show></template>";
        var document = SqxParser.Parse(source, "MissingWhen.sqx");
        document.Template.Roots.Clear();

        var diagnostics = DirectiveValidator.CollectDiagnostics(
            "MissingWhen.sqx",
            source,
            document,
            DirectiveCatalog.BuiltIn);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SQXD002");
    }
}
