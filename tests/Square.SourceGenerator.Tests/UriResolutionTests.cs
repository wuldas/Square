using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Square.Compiler.LanguageServices;
using Square.Html;
using Square.UI;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class UriResolutionTests
{
    [Theory]
    [InlineData("sqx")]
    [InlineData("sqv")]
    public void RootNamespaceOverridesProjectAndPreservesElementFamilies(string dialect)
    {
        var path = "UriChoice." + dialect;
        const string template = """
            <template xmlns="http://www.w3.org/1999/xhtml" xmlns:ui="urn:square:ui">
              <View><Button/><ui:Button/><article/><svg><circle r="8" /></svg><template><p>inner</p></template></View>
            </template>
            """;
        var analysis = Analyze(path, template, TemplateCatalog.SquareNamespaceUri);
        Assert.DoesNotContain(analysis.Diagnostics, item => item.Severity == SquareDiagnosticSeverity.Error);
        Assert.DoesNotContain(analysis.OutputCompilation.GetDiagnostics(), item => item.Severity == DiagnosticSeverity.Error);
        var decisions = analysis.Documents[path].Resolutions;
        Assert.Equal(typeof(HTMLButtonElement).FullName, decisions[At(template, "<Button")].Component.TypeName);
        Assert.Equal("Square.Controls.Button", decisions[At(template, "<ui:Button")].Component.TypeName);
        Assert.Equal(typeof(HTMLArticleElement).FullName, decisions[At(template, "<article")].Component.TypeName);
        Assert.Equal(typeof(HTMLTemplateElement).FullName, decisions[At(template, "<template><p")].Component.TypeName);
        Assert.Equal("Square.UI.Svg.SVGCircleElement", decisions[At(template, "<circle")].Component.TypeName);
    }

    [Theory]
    [InlineData("sqx")]
    [InlineData("sqv")]
    public void UndeclaredPrefixHasLocalizedDiagnostic(string dialect)
    {
        var path = "Missing." + dialect;
        const string template = "<template><my:element /></template>";
        var analysis = Analyze(path, template);
        Assert.Contains(analysis.Diagnostics, diagnostic => diagnostic.Id == "SQXE002" &&
            diagnostic.Range.Offset == At(template, "<my:element"));
    }

    [Theory]
    [InlineData("sqx")]
    [InlineData("sqv")]
    public void ProjectDefaultSelectsSquareAndFallsBackOnlyWhenUnique(string dialect)
    {
        var path = "ProjectDefault." + dialect;
        const string template = "<template><Button/><button/><article/></template>";
        var analysis = Analyze(path, template, TemplateCatalog.SquareNamespaceUri);
        Assert.DoesNotContain(analysis.Diagnostics, item => item.Severity == SquareDiagnosticSeverity.Error);
        Assert.Equal("Square.Controls.Button", analysis.Documents[path].Resolutions[At(template, "<Button")].Component.TypeName);
        Assert.Equal("Square.Controls.Button", analysis.Documents[path].Resolutions[At(template, "<button")].Component.TypeName);
        Assert.Equal(typeof(HTMLArticleElement).FullName, analysis.Documents[path].Resolutions[At(template, "<article")].Component.TypeName);
    }

    [Theory]
    [InlineData("sqx")]
    [InlineData("sqv")]
    public void PackageDefaultRequiresPrefixesOnlyOnCollision(string dialect)
    {
        var package = PackageReference();
        var path = "PackageDefault." + dialect;
        const string template = """
            <template xmlns:my="urn:acme:widgets" xmlns:html="http://www.w3.org/1999/xhtml" xmlns:ui="urn:square:ui">
              <element/><my:element/><button/><html:button/><ui:button/>
            </template>
            """;
        var analysis = Analyze(path, template, "urn:acme:widgets", package);
        var decisions = analysis.Documents[path].Resolutions;
        Assert.Equal("Acme.Widget", decisions[At(template, "<element")].Component.TypeName);
        Assert.Equal("Acme.Widget", decisions[At(template, "<my:element")].Component.TypeName);
        Assert.Equal(TemplateElementResolutionStatus.Ambiguous, decisions[At(template, "<button")].Status);
        Assert.Equal(typeof(HTMLButtonElement).FullName, decisions[At(template, "<html:button")].Component.TypeName);
        Assert.Equal("Square.Controls.Button", decisions[At(template, "<ui:button")].Component.TypeName);
        Assert.Contains(analysis.Diagnostics, item => item.Id == "SQXE004" && item.Range.Offset == At(template, "<button"));
    }

    [Theory]
    [InlineData("sqx")]
    [InlineData("sqv")]
    public void SvgDescendantsDoNotBorrowHtmlIdentityAtAnotherPosition(string dialect)
    {
        var path = "SvgIdentity." + dialect;
        const string template = "<template><article><title>outside</title><svg><title>inside</title></svg></article></template>";
        var analysis = Analyze(path, template);
        var first = At(template, "<title");
        var second = template.IndexOf("<title", first + 1, StringComparison.Ordinal) + 1;
        Assert.Equal(typeof(HTMLTitleElement).FullName, analysis.Documents[path].Resolutions[first].Component.TypeName);
        Assert.Equal(TemplateElementResolutionStatus.UnknownElement, analysis.Documents[path].Resolutions[second].Status);
        Assert.Contains(analysis.Diagnostics, item => item.Id == "SQXE003" && item.Range.Offset == second);
    }

    [Theory]
    [InlineData("sqx")]
    [InlineData("sqv")]
    public void HtmlSlotAndSquareSlotAreDistinct(string dialect)
    {
        var path = "SlotKinds." + dialect;
        const string template = "<template xmlns:ui=\"urn:square:ui\"><slot></slot><ui:Slot/><ui:Fragment/></template>";
        var analysis = Analyze(path, template);
        Assert.DoesNotContain(analysis.Diagnostics, item => item.Severity == SquareDiagnosticSeverity.Error);
        Assert.Equal(typeof(HTMLSlotElement).FullName, analysis.Documents[path].Resolutions[At(template, "<slot")].Component.TypeName);
        Assert.Equal(TemplateCatalog.SquareNamespaceUri,
            analysis.Documents[path].Resolutions[At(template, "<ui:Slot")].Component.NamespaceUri);
    }

    [Theory]
    [InlineData("sqx")]
    [InlineData("sqv")]
    public void RootOnlyNamespaceDeclarationsHaveLocalizedErrors(string dialect)
    {
        var invalid = "<template xmlns:ui=\"urn:wrong\"><ui:Button/></template>";
        var badBinding = Analyze("Reserved." + dialect, invalid);
        Assert.Contains(badBinding.Diagnostics, item => item.Id == "SQXE002" &&
            item.Range.Offset == invalid.IndexOf("xmlns:ui", StringComparison.Ordinal));
        var duplicate = "<template xmlns:ui=\"urn:square:ui\" xmlns:ui=\"urn:square:ui\"><ui:Button/></template>";
        var duplicateResult = Analyze("Duplicate." + dialect, duplicate);
        Assert.Contains(duplicateResult.Diagnostics, item => item.Id == "SQXE001" &&
            item.Range.Offset == duplicate.LastIndexOf("xmlns:ui", StringComparison.Ordinal));

        var relative = "<template xmlns=\"relative/path\"><Button/></template>";
        var relativeResult = Analyze("Relative." + dialect, relative);
        Assert.Contains(relativeResult.Diagnostics, item => item.Id == "SQXE001" &&
            item.Range.Offset == relative.IndexOf("xmlns=", StringComparison.Ordinal));

        var nested = "<template><article xmlns=\"urn:square:ui\"/></template>";
        var nestedResult = Analyze("Nested." + dialect, nested);
        Assert.Contains(nestedResult.Diagnostics, item => item.Id == "SQXE001" &&
            item.Range.Offset == nested.IndexOf("xmlns=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("sqx")]
    [InlineData("sqv")]
    public void LanguageServicesDescribeTheResolvedHtmlType(string dialect)
    {
        var path = "LanguageService." + dialect;
        const string template = "<template xmlns=\"http://www.w3.org/1999/xhtml\"><View><Button/></View></template>";
        var analysis = Analyze(path, template, TemplateCatalog.SquareNamespaceUri);
        var context = analysis.Documents[path].Context;
        var symbols = TemplateDocumentSymbols.GetSymbols(template, path, analysis.Catalog, context);
        var button = Assert.Single(Assert.Single(symbols).Children);
        Assert.Equal(typeof(HTMLButtonElement).FullName, button.Detail);

        var attributeContext = new TemplateCompletionContext(TemplateCompletionKind.Attribute,
            string.Empty, "Button", dialect == "sqv");
        var items = TemplateCompletionService.GetItems(attributeContext, template,
            catalog: analysis.Catalog, resolutionContext: context);
        Assert.Contains(items, item => item.Label == "title");
        Assert.DoesNotContain(items, item => item.Label == "width");
    }

    [Theory]
    [InlineData("sqx")]
    [InlineData("sqv")]
    public void PackageLocalNamesAreOrdinalAndDuplicateIdentitiesFail(string dialect)
    {
        const string strictTemplate = "<template><Element/></template>";
        var strict = Analyze("Strict." + dialect, strictTemplate, "urn:acme:widgets", PackageReference());
        Assert.Contains(strict.Diagnostics, item => item.Id == "SQXE003" &&
            item.Range.Offset == At(strictTemplate, "<Element"));

        const string source = """
            using Square.UI;
            [assembly: ElementExport("urn:acme:widgets", "element", typeof(Acme.First))]
            [assembly: ElementExport("urn:acme:widgets", "element", typeof(Acme.Second))]
            namespace Acme { public class First : Square.Controls.View { } public class Second : Square.Controls.View { } }
            """;
        var package = CSharpCompilation.Create("AcmeDuplicates", new[] { CSharpSyntaxTree.ParseText(source) },
            FrameworkReferences(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        Assert.True(package.Emit(stream).Success);
        var collided = Analyze("Collision." + dialect, "<template><element/></template>",
            "urn:acme:widgets", MetadataReference.CreateFromImage(stream.ToArray()));
        Assert.Contains(collided.Diagnostics, item => item.Id == "SQXE005");
    }

    [Theory]
    [InlineData("sqx")]
    [InlineData("sqv")]
    public void CustomElementsAndExplicitPackageAliasEmitTheirConcreteTypes(string dialect)
    {
        var path = "CustomElements." + dialect;
        const string template = """
            <template xmlns="http://www.w3.org/1999/xhtml" xmlns:my="urn:acme:widgets">
              <article><my:element status="ready"/><acme-badge status="ready"/><button is="acme-button">Custom button</button></article>
            </template>
            """;
        var analysis = Analyze(path, template, null, CustomPackageReference());
        Assert.DoesNotContain(analysis.Diagnostics, item => item.Severity == SquareDiagnosticSeverity.Error);
        Assert.DoesNotContain(analysis.OutputCompilation.GetDiagnostics(), item => item.Severity == DiagnosticSeverity.Error);
        var resolutions = analysis.Documents[path].Resolutions;
        Assert.Equal("Acme.Badge", resolutions[At(template, "<my:element")].Component.TypeName);
        Assert.Equal("Acme.Badge", resolutions[At(template, "<acme-badge")].Component.TypeName);
        Assert.Equal("Acme.Button", resolutions[At(template, "<button")].Component.TypeName);
        Assert.Equal("urn:acme:widgets", resolutions[At(template, "<my:element")].Component.NamespaceUri);
        Assert.Equal(TemplateCatalog.HtmlNamespaceUri,
            resolutions[At(template, "<acme-badge")].Component.NamespaceUri);
    }

    private static MetadataReference CustomPackageReference()
    {
        const string source = """
            using Square.Html;
            using Square.UI;
            [assembly: HtmlCustomElementExport("acme-badge", typeof(Acme.Badge), ObservedAttributes = new[] { "status" })]
            [assembly: HtmlCustomElementExport("acme-button", typeof(Acme.Button), ExtendsTag = "button")]
            [assembly: ElementExport("urn:acme:widgets", "element", typeof(Acme.Badge))]
            namespace Acme
            {
                public class Badge : HTMLElement, IHtmlStaticRepresentation
                {
                    public Badge() : base("acme-badge") { }
                    public HTMLElement CreateHtmlRepresentation() => new HTMLSpanElement();
                }
                public class Button : HTMLButtonElement, IHtmlStaticRepresentation
                {
                    public HTMLElement CreateHtmlRepresentation() => new HTMLButtonElement();
                }
            }
            """;
        var assembly = CSharpCompilation.Create("AcmeCustomWidgets", new[] { CSharpSyntaxTree.ParseText(source) },
            FrameworkReferences(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = assembly.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static MetadataReference PackageReference()
    {
        const string source = """
            using Square.UI;
            [assembly: ElementExport("urn:acme:widgets", "element", typeof(Acme.Widget))]
            namespace Acme { public class Widget : Square.Controls.View { } }
            """;
        var assembly = CSharpCompilation.Create("AcmeWidgets", new[] { CSharpSyntaxTree.ParseText(source) },
            FrameworkReferences(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = assembly.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static int At(string content, string open) => content.IndexOf(open, StringComparison.Ordinal) + 1;

    private static MetadataReference[] FrameworkReferences() => AppDomain.CurrentDomain.GetAssemblies()
        .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
        .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
        .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .ToArray();

    private static TemplateProjectAnalysis Analyze(string path, string template, string? projectUri = null,
        params MetadataReference[] packages)
    {
        var compilation = CSharpCompilation.Create("UriChoiceAnalysis",
            new[] { CSharpSyntaxTree.ParseText("public sealed class Placeholder { }") },
            FrameworkReferences().Concat(packages), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return new TemplateSemanticAnalyzer().AnalyzeProject(compilation,
            new[] { (path, template, "Square.Sample") }, CancellationToken.None, projectUri!);
    }
}
