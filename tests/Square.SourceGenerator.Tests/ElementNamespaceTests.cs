using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Square.Compiler.Diagnostics;
using Square.Compiler.LanguageServices;
using Square.UI;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class ElementNamespaceTests
{
    private static readonly TemplateResolutionContext EmptyContext =
        new(string.Empty, Array.Empty<string>());






    [Theory]
    [InlineData("a:b:c")]
    [InlineData(":button")]
    [InlineData("ui:")]
    public void RejectsMalformedQualifiedNames(string tagName)
    {
        Assert.Equal(
            TemplateElementResolutionStatus.InvalidName,
            TemplateCatalog.BuiltIn.ResolveComponent(tagName, EmptyContext).Status);
    }

    [Theory]
    [InlineData("sqx")]
    [InlineData("sqv")]
    public void QualifiedAndGlobalElementNamesCompile(string extension)
    {
        var template = "<template xmlns:ui=\"urn:square:ui\"><ui:button /><ui:Table /><ui:Body /><global::Square.Controls.Button /></template>";

        var result = RunGenerator("Qualified." + extension, template, out var output);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("sqx")]
    [InlineData("sqv")]
    public void ConformingHtmlVocabularyCompilesInBothDialects(string extension)
    {
        var content = string.Join("", TemplateCatalog.BuiltIn.Components
            .Where(descriptor => descriptor.NamespaceUri == TemplateCatalog.HtmlNamespaceUri)
            .Select(descriptor => descriptor.TagName).Select(tag =>
            tag == "template" ? "<html:template></html:template>" : $"<{tag} />"));
        var result = RunGenerator("HtmlVocabulary." + extension,
            "<template xmlns:html=\"http://www.w3.org/1999/xhtml\"><View>" + content + "</View></template>", out var output);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("sqx")]
    [InlineData("sqv")]
    public void HtmlVoidTagsLeaveFollowingSiblingsIntact(string extension)
    {
        var result = RunGenerator("HtmlMixed." + extension,
            "<template><article><p>Hello <strong>世界</strong> !<br>next</p><input type=\"checkbox\" checked><svg><circle r=\"8\" /></svg></article></template>",
            out var output);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HtmlFormModelBindingsCompileWithValueAndCheckedTypes()
    {
        const string source = """
            <template><article>
              <input v-model="Name"><textarea v-model="Notes"></textarea>
              <select v-model="Choice"><option value="A">A</option></select>
              <input type="checkbox" v-model="Agreed">
            </article></template>
            <script lang="csharp">
              public ObservableValue<string> Name = new("");
              public ObservableValue<string> Notes = new("");
              public ObservableValue<string> Choice = new("A");
              public ObservableValue<bool> Agreed = new(false);
            </script>
            """;

        var result = RunGenerator("HtmlModel.sqv", source, out var output);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("sqx")]
    [InlineData("sqv")]
    public void QualifiedHtmlScriptAndStyleStayInsideTemplate(string extension)
    {
        var result = RunGenerator("InertSections." + extension,
            "<template xmlns:html=\"http://www.w3.org/1999/xhtml\"><article><html:script>not executed</html:script><html:style>disabled</html:style></article></template>",
            out var output);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("sqx")]
    [InlineData("sqv")]
    public void StaticHtmlInlineEventsAreRejected(string extension)
    {
        var result = RunGenerator("Unsafe." + extension,
            "<template xmlns:html=\"http://www.w3.org/1999/xhtml\"><html:button onclick=\"alert(1)\">Click</html:button></template>", out _);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "SQXE007");
    }

    [Theory]
    [InlineData("a:b:c")]
    [InlineData(":button")]
    [InlineData("ui:")]
    public void MalformedTemplateElementReportsElementDiagnostic(string tagName)
    {
        var result = RunGenerator("Invalid.sqx", "<template><" + tagName + " /></template>", out _);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "SQXE001");
    }



    [Fact]
    public void ReferencedPropsEventsAndSlotsUseExportedTypeContracts()
    {
        var package = CompilePackageWithGenerator("ContractPackage", """
            using Square.Events;
            using Square.Runtime.Binding;
            using Square.UI;
            [assembly: ElementExport("urn:contracts", "badge", typeof(Contracts.Badge))]
            [assembly: ElementEventContract(typeof(Contracts.Badge), "ActivatedEvent", "activated")]
            namespace Contracts
            {
                public sealed class RowSlotProps { public int Item { get; init; } }
                [SlotContract("row", typeof(RowSlotProps))]
                public sealed class Badge : Square.Controls.View
                {
                    [Prop(Required = true)] public string Label { get; set; } = "";
                    public static readonly ComponentEvent<int> ActivatedEvent = new("activated");
                }
            }
            """);

        var missing = RunGenerator(
            "MissingProp.sqx",
            "<template xmlns:events=\"urn:contracts\"><events:badge /></template>",
            out _,
            new[] { package });
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Id == "SQX0003");

        const string valid = """
            <template xmlns:events="urn:contracts"><events:badge Label="ok" onActivated={OnActivated} /></template>
            <script>private void OnActivated(global::Square.Events.CustomEvent<int> e) { }</script>
            """;
        var validResult = RunGenerator("TypedEvent.sqx", valid, out var validOutput, new[] { package });
        Assert.DoesNotContain(validResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(validOutput.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        const string slot = """
            <template xmlns:events="urn:contracts">
              <events:badge Label="ok">
                <template #row="{ item: row }"><View /></template>
              </events:badge>
            </template>
            """;
        var slotResult = RunGenerator("TypedSlot.sqv", slot, out var slotOutput, new[] { package });
        Assert.DoesNotContain(slotResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(slotOutput.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void MissingReferencedEventMetadataIsAnError()
    {
        var package = CompilePackage("MissingEventMetadata", """
            using Square.Events;
            using Square.UI;
            [assembly: ElementExport("urn:events", "badge", typeof(Events.Badge))]
            namespace Events
            {
                public sealed class Badge : Square.Controls.View
                {
                    public static readonly ComponentEvent<int> ActivatedEvent = new("activated");
                }
            }
            """);

        var result = RunGenerator(
            "MissingEvent.sqx",
            "<template xmlns:events=\"urn:events\"><events:badge /></template>",
            out _,
            new[] { package });

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "SQXE006");
    }





    [Fact]
    public void ClosedGenericExportsResolveAndOpenGenericExportsAreRejected()
    {
        var package = CompilePackage("GenericPackage", """
            using Square.UI;
            [assembly: ElementExport("urn:generic", "closed", typeof(Components.Card<int>))]
            [assembly: ElementExport("urn:generic", "open", typeof(Components.Card<>))]
            namespace Components { public sealed class Card<T> : Square.Controls.View { } }
            """);
        var catalog = CreateCatalog("public sealed class Consumer { }", package);

        Assert.Contains(catalog.Diagnostics, diagnostic => diagnostic.Id == "SQXE001");
        var context = new TemplateResolutionContext(string.Empty, Array.Empty<string>(), "urn:generic");
        Assert.Equal("Components.Card<int>", catalog.ResolveComponent("closed", context).Component.TypeName);
        Assert.Equal(TemplateElementResolutionStatus.UnknownElement, catalog.ResolveComponent("open", context).Status);
    }

    [Theory]
    [InlineData("public Badge(int value) { }")]
    [InlineData("private Badge() { }")]
    public void ExportedGeneratedTemplateUsesScriptConstructors(string constructor)
    {
        var template = """
            <template><View /></template>
            <script namespace="Generated">CONSTRUCTOR</script>
            """.Replace("CONSTRUCTOR", constructor, StringComparison.Ordinal);
        var result = RunGenerator(
            "Badge.sqx",
            template,
            out _,
            source: """
                using Square.UI;
                [assembly: ElementExport("urn:generated", "badge", typeof(Generated.Badge))]
                public sealed class Placeholder { }
                """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "SQXE001");
    }

    [Fact]
    public void ExportedGeneratedTemplateUsesScriptAccess()
    {
        var result = RunGenerator(
            "Badge.sqx",
            "<template><View /></template><script namespace=\"Generated\" access=\"internal\"></script>",
            out _,
            source: """
                using Square.UI;
                [assembly: ElementExport("urn:generated", "badge", typeof(Generated.Badge))]
                public sealed class Placeholder { }
                """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "SQXE001");
    }

    [Fact]
    public void NonLiteralGeneratedEventRequiresMatchingMetadata()
    {
        const string template = """
            <template><View /></template>
            <script namespace="Generated">
              public static readonly ComponentEvent<int> ActivatedEvent = CreateEvent();
              private static ComponentEvent<int> CreateEvent() => new("activated");
            </script>
            """;
        const string export = """
            using Square.UI;
            [assembly: ElementExport("urn:generated", "badge", typeof(Generated.Badge))]
            public sealed class Placeholder { }
            """;
        var missing = RunGenerator("Badge.sqx", template, out _, source: export);
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Id == "SQXE006");

        var matching = RunGenerator("Badge.sqx", template, out var output, source: """
            using Square.UI;
            [assembly: ElementExport("urn:generated", "badge", typeof(Generated.Badge))]
            [assembly: ElementEventContract(typeof(Generated.Badge), "ActivatedEvent", "activated")]
            public sealed class Placeholder { }
            """);
        Assert.DoesNotContain(matching.Diagnostics, diagnostic => diagnostic.Id == "SQXE006");
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ThreeConflictingEventContractsCannotRecover()
    {
        const string template = """
            <template><View /></template>
            <script namespace="Generated">
              public static readonly ComponentEvent<int> ActivatedEvent = CreateEvent();
              private static ComponentEvent<int> CreateEvent() => new("activated");
            </script>
            """;
        var result = RunGenerator("Badge.sqx", template, out _, source: """
            using Square.UI;
            [assembly: ElementExport("urn:generated", "badge", typeof(Generated.Badge))]
            [assembly: ElementEventContract(typeof(Generated.Badge), "ActivatedEvent", "activated")]
            [assembly: ElementEventContract(typeof(Generated.Badge), "ActivatedEvent", "changed")]
            [assembly: ElementEventContract(typeof(Generated.Badge), "ActivatedEvent", "activated")]
            public sealed class Placeholder { }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "SQXE006");
    }

    [Fact]
    public void SqxSemanticHandlerDiagnosticsUseElementDiagnosticIds()
    {
        var missing = RunGenerator(
            "MissingHandler.sqx",
            "<template xmlns:ui=\"urn:square:ui\"><ui:Button onClick={OnMissing} /></template>",
            out _);
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Id == "SQX0004");

        var mismatch = RunGenerator(
            "BadHandler.sqx",
            "<template xmlns:ui=\"urn:square:ui\"><ui:Button onClick={OnBad} /></template><script>private void OnBad(int value) { }</script>",
            out _);
        Assert.Contains(mismatch.Diagnostics, diagnostic => diagnostic.Id == "SQX0005");
    }




    private static MetadataReference CompilePackageWithGenerator(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            FrameworkReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new SqxGenerator().AsSourceGenerator() },
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return MetadataReference.CreateFromImage(ImmutableArray.Create(stream.ToArray()));
    }

    [Fact]
    public void IdenticalExportsDeduplicateButIdentityCollisionsFail()
    {
        var duplicate = CompilePackage("DuplicateExports", """
            using Square.UI;
            [assembly: ElementExport("urn:duplicate", "card", typeof(Duplicate.Card))]
            [assembly: ElementExport("urn:duplicate", "card", typeof(Duplicate.Card))]
            namespace Duplicate { public sealed class Card : Square.Controls.View { } }
            """);
        var duplicateCatalog = CreateCatalog("public sealed class Consumer { }", duplicate);
        Assert.Equal(
            "Duplicate.Card",
            duplicateCatalog.ResolveComponent("card", new TemplateResolutionContext(string.Empty, Array.Empty<string>(), "urn:duplicate")).Component.TypeName);
        Assert.DoesNotContain(duplicateCatalog.Diagnostics, diagnostic => diagnostic.Id == "SQXE005");

        var collision = CompilePackage("CollidingExports", """
            using Square.UI;
            [assembly: ElementExport("urn:collision", "card", typeof(Collision.First))]
            [assembly: ElementExport("urn:collision", "card", typeof(Collision.Second))]
            namespace Collision
            {
                public sealed class First : Square.Controls.View { }
                public sealed class Second : Square.Controls.View { }
            }
            """);
        var collisionCatalog = CreateCatalog("public sealed class Consumer { }", collision);
        Assert.Contains(collisionCatalog.Diagnostics, diagnostic => diagnostic.Id == "SQXE005");
        Assert.Equal(TemplateElementResolutionStatus.Ambiguous,
            collisionCatalog.ResolveComponent("card", new TemplateResolutionContext(string.Empty, Array.Empty<string>(), "urn:collision")).Status);
    }

    private static GeneratorDriverRunResult RunGenerator(
        string path,
        string template,
        out Compilation output,
        IEnumerable<MetadataReference>? packageReferences = null,
        string source = "public sealed class Placeholder { }")
    {
        var references = FrameworkReferences().Concat(packageReferences ?? Array.Empty<MetadataReference>());
        var compilation = CSharpCompilation.Create(
            "GeneratorConsumer",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new SqxGenerator().AsSourceGenerator() },
            new AdditionalText[] { new InMemoryAdditionalText(path, template) },
            (CSharpParseOptions)compilation.SyntaxTrees.First().Options);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out output, out _);
        return driver.GetRunResult();
    }

    private static TemplateCatalog CreateCatalog(string source, params MetadataReference[] packageReferences)
    {
        var compilation = CSharpCompilation.Create(
            "Consumer",
            new[] { CSharpSyntaxTree.ParseText(source) },
            FrameworkReferences().Concat(packageReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        AssertNoErrors(compilation);
        return TemplateCatalog.FromCompilation(compilation, Array.Empty<(string, string, string)>());
    }

    private static MetadataReference CompilePackage(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            FrameworkReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage(ImmutableArray.Create(stream.ToArray()));
    }

    private static IReadOnlyList<MetadataReference> FrameworkReferences()
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();
        var squarePath = typeof(ElementExportAttribute).Assembly.Location;
        if (references.All(reference => !string.Equals(reference.Display, squarePath, StringComparison.OrdinalIgnoreCase)))
            references.Add(MetadataReference.CreateFromFile(squarePath));
        return references
            .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
    {
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(content);
    }

    private static void AssertNoErrors(Compilation compilation) =>
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
