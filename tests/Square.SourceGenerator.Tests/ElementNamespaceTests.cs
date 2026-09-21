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
    [InlineData("ui:button", "Square.Controls.Button")]
    [InlineData("ui:Table", "Square.Controls.Table")]
    [InlineData("ui:Body", "Square.UI.UIBodyElement")]
    public void ResolvesQualifiedSquareElements(string tagName, string typeName)
    {
        var resolution = TemplateCatalog.BuiltIn.ResolveComponent(tagName, EmptyContext);

        Assert.Equal(TemplateElementResolutionStatus.Resolved, resolution.Status);
        Assert.Equal(typeName, resolution.Component.TypeName);
    }

    [Fact]
    public void ResolvesMetadataExportsAndReportsUnorderedConflict()
    {
        var packageA = CompilePackage("PackageA", """
            using Square.UI;
            [assembly: ElementExport("urn:a", "a", "chart", typeof(A.Chart))]
            [assembly: ElementExport("urn:a", "a", "badge", typeof(A.Badge))]
            namespace A { public sealed class Chart : Square.Controls.View { } public sealed class Badge : Square.Controls.View { } }
            """);
        var packageB = CompilePackage("PackageB", """
            using Square.UI;
            [assembly: ElementExport("urn:b", "b", "chart", typeof(B.Chart))]
            namespace B { public sealed class Chart : Square.Controls.View { } }
            """);
        var catalog = CreateCatalog("public sealed class Consumer { }", packageB, packageA);

        Assert.Equal("A.Badge", catalog.ResolveComponent("badge", EmptyContext).Component.TypeName);
        Assert.Equal("A.Chart", catalog.ResolveComponent("a:chart", EmptyContext).Component.TypeName);
        var ambiguous = catalog.ResolveComponent("chart", EmptyContext);
        Assert.Equal(TemplateElementResolutionStatus.Ambiguous, ambiguous.Status);
        Assert.Equal(new[] { "A.Chart", "B.Chart" }, ambiguous.Candidates.Select(item => item.TypeName).OrderBy(item => item));
    }

    [Fact]
    public void ApplicationOrderSelectsNamespaceRegardlessOfReferenceOrder()
    {
        var packageA = CompilePackage("PackageA", """
            using Square.UI;
            [assembly: ElementExport("urn:a", "a", "chart", typeof(A.Chart))]
            namespace A { public sealed class Chart : Square.Controls.View { } }
            """);
        var packageB = CompilePackage("PackageB", """
            using Square.UI;
            [assembly: ElementExport("urn:b", "b", "chart", typeof(B.Chart))]
            namespace B { public sealed class Chart : Square.Controls.View { } }
            """);
        const string consumer = """
            using Square.UI;
            [assembly: ElementNamespaceOrder("urn:b", "urn:a")]
            public sealed class Consumer { }
            """;

        var first = CreateCatalog(consumer, packageA, packageB).ResolveComponent("chart", EmptyContext);
        var second = CreateCatalog(consumer, packageB, packageA).ResolveComponent("chart", EmptyContext);

        Assert.Equal("B.Chart", first.Component.TypeName);
        Assert.Equal("B.Chart", second.Component.TypeName);
    }

    [Fact]
    public void AliasOverridesPackagePrefixAndUnknownPrefixDoesNotFallback()
    {
        var package = CompilePackage("PackageA", """
            using Square.UI;
            [assembly: ElementExport("urn:a", "a", "chart", typeof(A.Chart))]
            namespace A { public sealed class Chart : Square.Controls.View { } }
            """);
        var catalog = CreateCatalog("""
            using Square.UI;
            [assembly: ElementNamespaceAlias("urn:a", "charts")]
            public sealed class Consumer { }
            """, package);

        Assert.Equal("A.Chart", catalog.ResolveComponent("charts:chart", EmptyContext).Component.TypeName);
        Assert.Equal(TemplateElementResolutionStatus.UnknownPrefix, catalog.ResolveComponent("a:chart", EmptyContext).Status);
        Assert.Equal(TemplateElementResolutionStatus.UnknownPrefix, catalog.ResolveComponent("missing:chart", EmptyContext).Status);
    }

    [Fact]
    public void FixedNamespacesCannotBeOverridden()
    {
        var package = CompilePackage("PackageA", """
            using Square.UI;
            [assembly: ElementExport("urn:a", "a", "button", typeof(A.Button))]
            namespace A { public sealed class Button : Square.Controls.View { } }
            """);
        var catalog = CreateCatalog("public sealed class Consumer { }", package);

        Assert.Equal("Square.Controls.Button", catalog.ResolveComponent("button", EmptyContext).Component.TypeName);
        Assert.Equal("A.Button", catalog.ResolveComponent("a:button", EmptyContext).Component.TypeName);
        Assert.Equal(TemplateElementResolutionStatus.UnknownElement, catalog.ResolveComponent("html:button", EmptyContext).Status);
    }

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
        var template = "<template><ui:button /><ui:Table /><ui:Body /><global::Square.Controls.Button /></template>";

        var result = RunGenerator("Qualified." + extension, template, out var output);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
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
    public void GeneratorConstructsMetadataExportWinners()
    {
        var packageA = CompilePackage("PackageA", """
            using Square.UI;
            [assembly: ElementExport("urn:a", "a", "chart", typeof(A.Chart))]
            [assembly: ElementExport("urn:a", "a", "badge", typeof(A.Badge))]
            namespace A { public sealed class Chart : Square.Controls.View { } public sealed class Badge : Square.Controls.View { } }
            """);
        var packageB = CompilePackage("PackageB", """
            using Square.UI;
            [assembly: ElementExport("urn:b", "b", "chart", typeof(B.Chart))]
            namespace B { public sealed class Chart : Square.Controls.View { } }
            """);
        const string source = """
            using Square.UI;
            [assembly: ElementNamespaceOrder("urn:b", "urn:a")]
            public sealed class Placeholder { }
            """;

        var result = RunGenerator(
            "PackageConsumer.sqx",
            "<template><badge /><chart /><a:chart /><b:chart /></template>",
            out var output,
            new[] { packageA, packageB },
            source);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void GeneratorReportsAmbiguousMetadataExport()
    {
        var packageA = CompilePackage("PackageA", """
            using Square.UI;
            [assembly: ElementExport("urn:a", "a", "chart", typeof(A.Chart))]
            namespace A { public sealed class Chart : Square.Controls.View { } }
            """);
        var packageB = CompilePackage("PackageB", """
            using Square.UI;
            [assembly: ElementExport("urn:b", "b", "chart", typeof(B.Chart))]
            namespace B { public sealed class Chart : Square.Controls.View { } }
            """);

        var result = RunGenerator(
            "Ambiguous.sqx",
            "<template><chart /></template>",
            out _,
            new[] { packageA, packageB });

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "SQXE004");
        Assert.Contains("a:chart", diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("b:chart", diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReferencedPropsEventsAndSlotsUseExportedTypeContracts()
    {
        var package = CompilePackageWithGenerator("ContractPackage", """
            using Square.Events;
            using Square.Runtime.Binding;
            using Square.UI;
            [assembly: ElementExport("urn:contracts", "events", "badge", typeof(Contracts.Badge))]
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
            "<template><events:badge /></template>",
            out _,
            new[] { package });
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Id == "SQX0003");

        const string valid = """
            <template><events:badge Label="ok" onActivated={OnActivated} /></template>
            <script>private void OnActivated(global::Square.Events.CustomEvent<int> e) { }</script>
            """;
        var validResult = RunGenerator("TypedEvent.sqx", valid, out var validOutput, new[] { package });
        Assert.DoesNotContain(validResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(validOutput.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        const string slot = """
            <template>
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
            [assembly: ElementExport("urn:events", "events", "badge", typeof(Events.Badge))]
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
            "<template><events:badge /></template>",
            out _,
            new[] { package });

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "SQXE006");
    }

    [Fact]
    public void ConflictingDefaultPrefixesRequireAliasAndAliasReplacesBoth()
    {
        var package = CompilePackage("PrefixPackage", """
            using Square.UI;
            [assembly: ElementExport("urn:cards", "a", "card", typeof(Cards.Card))]
            [assembly: ElementExport("urn:cards", "z", "card", typeof(Cards.Card))]
            namespace Cards { public sealed class Card : Square.Controls.View { } }
            """);

        var invalid = CreateCatalog("public sealed class Consumer { }", package);
        Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic.Id == "SQXE001");
        Assert.Equal(TemplateElementResolutionStatus.UnknownPrefix, invalid.ResolveComponent("a:card", EmptyContext).Status);
        Assert.Equal(TemplateElementResolutionStatus.UnknownPrefix, invalid.ResolveComponent("z:card", EmptyContext).Status);

        var aliased = CreateCatalog("""
            using Square.UI;
            [assembly: ElementNamespaceAlias("urn:cards", "q")]
            public sealed class Consumer { }
            """, package);
        Assert.DoesNotContain(aliased.Diagnostics, diagnostic => diagnostic.Id == "SQXE001");
        Assert.Equal("Cards.Card", aliased.ResolveComponent("q:card", EmptyContext).Component.TypeName);
        Assert.Equal(TemplateElementResolutionStatus.UnknownPrefix, aliased.ResolveComponent("a:card", EmptyContext).Status);
        Assert.Equal(TemplateElementResolutionStatus.UnknownPrefix, aliased.ResolveComponent("z:card", EmptyContext).Status);
    }

    [Fact]
    public void AliasCannotCollideWithAnotherNamespaceDefaultPrefix()
    {
        var package = CompilePackage("AliasCollisionPackage", """
            using Square.UI;
            [assembly: ElementExport("urn:a", "shared", "card", typeof(A.Card))]
            [assembly: ElementExport("urn:b", "b", "card", typeof(B.Card))]
            namespace A { public sealed class Card : Square.Controls.View { } }
            namespace B { public sealed class Card : Square.Controls.View { } }
            """);
        var catalog = CreateCatalog("""
            using Square.UI;
            [assembly: ElementNamespaceAlias("urn:b", "shared")]
            public sealed class Consumer { }
            """, package);

        Assert.Contains(catalog.Diagnostics, diagnostic => diagnostic.Id == "SQXE001");
        Assert.Equal(TemplateElementResolutionStatus.Ambiguous, catalog.ResolveComponent("shared:card", EmptyContext).Status);
    }

    [Fact]
    public void NullNamespaceOrderReportsDeclarationDiagnostic()
    {
        var catalog = CreateCatalog("""
            using Square.UI;
            [assembly: ElementNamespaceOrder(null)]
            public sealed class Consumer { }
            """);

        Assert.Contains(catalog.Diagnostics, diagnostic => diagnostic.Id == "SQXE001");
    }

    [Fact]
    public void LocalResolutionIsCaseSensitiveAccessibleAndCannotBypassExports()
    {
        var package = CompilePackage("ExportedLocalPackage", """
            using Square.UI;
            [assembly: ElementExport("urn:cards", "cards", "package-card", typeof(My.App.Card))]
            namespace My.App { public sealed class Card : Square.Controls.View { } }
            """);
        var catalog = CreateCatalog("""
            namespace Current
            {
                internal sealed class Card : Square.Controls.View { }
                public sealed class Consumer { }
            }
            """, package);
        var context = new TemplateResolutionContext("Current", Array.Empty<string>());

        Assert.Equal("Current.Card", catalog.ResolveComponent("Card", context).Component.TypeName);
        Assert.Equal(TemplateElementResolutionStatus.UnknownElement, catalog.ResolveComponent("card", context).Status);
        Assert.Equal(TemplateElementResolutionStatus.UnknownElement, catalog.ResolveComponent("local:My.App.Card", context).Status);
        Assert.Equal("My.App.Card", catalog.ResolveComponent("global::My.App.Card", context).Component.TypeName);
    }

    [Fact]
    public void ClosedGenericExportsResolveAndOpenGenericExportsAreRejected()
    {
        var package = CompilePackage("GenericPackage", """
            using Square.UI;
            [assembly: ElementExport("urn:generic", "g", "closed", typeof(Components.Card<int>))]
            [assembly: ElementExport("urn:generic", "g", "open", typeof(Components.Card<>))]
            namespace Components { public sealed class Card<T> : Square.Controls.View { } }
            """);
        var catalog = CreateCatalog("public sealed class Consumer { }", package);

        Assert.Contains(catalog.Diagnostics, diagnostic => diagnostic.Id == "SQXE001");
        Assert.Equal("Components.Card<int>", catalog.ResolveComponent("g:closed", EmptyContext).Component.TypeName);
        Assert.Equal(TemplateElementResolutionStatus.UnknownElement, catalog.ResolveComponent("g:open", EmptyContext).Status);
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
                [assembly: ElementExport("urn:generated", "generated", "badge", typeof(Generated.Badge))]
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
                [assembly: ElementExport("urn:generated", "generated", "badge", typeof(Generated.Badge))]
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
            [assembly: ElementExport("urn:generated", "generated", "badge", typeof(Generated.Badge))]
            public sealed class Placeholder { }
            """;
        var missing = RunGenerator("Badge.sqx", template, out _, source: export);
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Id == "SQXE006");

        var matching = RunGenerator("Badge.sqx", template, out var output, source: """
            using Square.UI;
            [assembly: ElementExport("urn:generated", "generated", "badge", typeof(Generated.Badge))]
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
            [assembly: ElementExport("urn:generated", "generated", "badge", typeof(Generated.Badge))]
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
            "<template><Button onClick={OnMissing} /></template>",
            out _);
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Id == "SQX0004");

        var mismatch = RunGenerator(
            "BadHandler.sqx",
            "<template><Button onClick={OnBad} /></template><script>private void OnBad(int value) { }</script>",
            out _);
        Assert.Contains(mismatch.Diagnostics, diagnostic => diagnostic.Id == "SQX0005");
    }

    [Fact]
    public void MissingElementUnderAmbiguousPrefixReportsUnknownElementWithNamespaces()
    {
        var packageA = CompilePackage("PackageA", """
            using Square.UI;
            [assembly: ElementExport("urn:a", "p", "chart", typeof(A.Chart))]
            namespace A { public sealed class Chart : Square.Controls.View { } }
            """);
        var packageB = CompilePackage("PackageB", """
            using Square.UI;
            [assembly: ElementExport("urn:b", "p", "gauge", typeof(B.Gauge))]
            namespace B { public sealed class Gauge : Square.Controls.View { } }
            """);
        var catalog = CreateCatalog("public sealed class Consumer { }", packageA, packageB);

        var missing = catalog.ResolveComponent("p:missing", EmptyContext);
        Assert.Equal(TemplateElementResolutionStatus.UnknownElement, missing.Status);
        Assert.Empty(missing.Candidates);
        Assert.Equal(new[] { "urn:a", "urn:b" }, missing.CandidateNamespaceUris);

        var ambiguous = catalog.ResolveComponent("p:chart", EmptyContext);
        Assert.Equal(TemplateElementResolutionStatus.Ambiguous, ambiguous.Status);
        Assert.Equal(new[] { "urn:a", "urn:b" }, ambiguous.CandidateNamespaceUris);
    }

    [Fact]
    public void ElementDiagnosticsCentralizeResolutionIdMessageAndRange()
    {
        var packageA = CompilePackage("PackageA", """
            using Square.UI;
            [assembly: ElementExport("urn:a", "a", "chart", typeof(A.Chart))]
            namespace A { public sealed class Chart : Square.Controls.View { } }
            """);
        var packageB = CompilePackage("PackageB", """
            using Square.UI;
            [assembly: ElementExport("urn:b", "b", "chart", typeof(B.Chart))]
            namespace B { public sealed class Chart : Square.Controls.View { } }
            """);
        var catalog = CreateCatalog("public sealed class Consumer { }", packageA, packageB);

        var resolution = catalog.ResolveComponent("chart", EmptyContext);
        var diagnostic = ElementDiagnostics.ForResolution(
            "chart", resolution, catalog, new SquareSourceRange(7, 5), "Page.sqx");

        Assert.Equal("SQXE004", diagnostic.Id);
        Assert.Equal("Page.sqx", diagnostic.SourcePath);
        Assert.Equal(7, diagnostic.Range.Offset);
        Assert.Equal(5, diagnostic.Range.Length);
        Assert.Contains("urn:a", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("urn:b", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AliasPrefixConflictsReportEachDeclarationLocation()
    {
        var packageA = CompilePackage("PackageA", """
            using Square.UI;
            [assembly: ElementExport("urn:a", "a", "chart", typeof(A.Chart))]
            namespace A { public sealed class Chart : Square.Controls.View { } }
            """);
        var packageB = CompilePackage("PackageB", """
            using Square.UI;
            [assembly: ElementExport("urn:b", "b", "gauge", typeof(B.Gauge))]
            namespace B { public sealed class Gauge : Square.Controls.View { } }
            """);
        var catalog = CreateCatalog("""
            using Square.UI;
            [assembly: ElementNamespaceAlias("urn:a", "shared")]
            [assembly: ElementNamespaceAlias("urn:b", "shared")]
            public sealed class Consumer { }
            """, packageA, packageB);

        var conflicts = catalog.Diagnostics
            .Where(diagnostic => diagnostic.Message.Contains("assigned to multiple URIs", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(2, conflicts.Length);
        Assert.All(conflicts, diagnostic =>
        {
            Assert.Equal("SQXE001", diagnostic.Id);
            Assert.True(diagnostic.Range.Length > 0);
        });
        Assert.Equal(2, conflicts.Select(diagnostic => diagnostic.Range.Offset).Distinct().Count());
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
            [assembly: ElementExport("urn:duplicate", "dup", "card", typeof(Duplicate.Card))]
            [assembly: ElementExport("urn:duplicate", "dup", "card", typeof(Duplicate.Card))]
            namespace Duplicate { public sealed class Card : Square.Controls.View { } }
            """);
        var duplicateCatalog = CreateCatalog("public sealed class Consumer { }", duplicate);
        Assert.Equal(
            "Duplicate.Card",
            duplicateCatalog.ResolveComponent("dup:card", EmptyContext).Component.TypeName);
        Assert.DoesNotContain(duplicateCatalog.Diagnostics, diagnostic => diagnostic.Id == "SQXE005");

        var collision = CompilePackage("CollidingExports", """
            using Square.UI;
            [assembly: ElementExport("urn:collision", "collision", "card", typeof(Collision.First))]
            [assembly: ElementExport("urn:collision", "collision", "card", typeof(Collision.Second))]
            namespace Collision
            {
                public sealed class First : Square.Controls.View { }
                public sealed class Second : Square.Controls.View { }
            }
            """);
        var collisionCatalog = CreateCatalog("public sealed class Consumer { }", collision);
        Assert.Contains(collisionCatalog.Diagnostics, diagnostic => diagnostic.Id == "SQXE005");
        Assert.Equal(TemplateElementResolutionStatus.Ambiguous,
            collisionCatalog.ResolveComponent("collision:card", EmptyContext).Status);
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
