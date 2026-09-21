using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Square.Compiler.LanguageServices;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class TemplateSemanticAnalyzerTests
{
    [Fact]
    public void ExtractsTemplatePropContractsFromEmbeddedScript()
    {
        const string source = """
            <template><View /></template>
            <script lang="csharp">
              [Prop(Required = true)]
              public ObservableValue<string> Title { get; set; } = new("");
              [Prop]
              public ObservableValue<int> Count { get; set; } = new(0);
            </script>
            """;

        var compilation = CreateCompilation();
        var inputs = new[] { ("Card.sqx", source, "Sample") };
        var catalog = TemplateCatalog.FromCompilation(compilation, inputs);
        var component = Assert.Single(catalog.Components, item => item.TypeName == "Sample.Card");

        var props = catalog.GetProps(component);
        Assert.Equal(2, props.Count);
        Assert.Contains(props, prop => prop.Name == "Title" && prop.Required && prop.TypeName.Contains("System.String"));
        Assert.Contains(props, prop => prop.Name == "Count" && !prop.Required && prop.TypeName.Contains("System.Int32"));
    }

    [Fact]
    public void ExtractsPropContractsFromRoslynPropertySyntax()
    {
        const string source = """
            <template><View /></template>
            <script>
              [Prop(
                  Required = true)]
              public global::Square.Runtime.Binding.ObservableValue<string> Title { get; } = new("");
            </script>
            """;

        var compilation = CreateCompilation();
        var inputs = new[] { ("QualifiedCard.sqx", source, "Sample") };
        var catalog = TemplateCatalog.FromCompilation(compilation, inputs);
        var component = Assert.Single(catalog.Components, item => item.TypeName == "Sample.QualifiedCard");

        var prop = Assert.Single(catalog.GetProps(component));
        Assert.Equal("Title", prop.Name);
        Assert.True(prop.Required);
        Assert.Equal("global::Square.Runtime.Binding.ObservableValue<global::System.String>", prop.TypeName);
    }

    [Fact]
    public void ExtractsGeneratedComponentMetadataWithFullAndShortNames()
    {
        const string source = "<template><View /></template>";
        var analyzer = new TemplateSemanticAnalyzer();

        var components = analyzer.BuildGeneratedComponents(
            new[] { ("Pages/Card.sqx", source, "Sample.Pages") });

        var descriptor = Assert.Single(components.Values);
        Assert.Equal("Card", descriptor.TagName);
        Assert.Equal("Sample.Pages.Card", descriptor.TypeName);
        Assert.False(descriptor.IsBuiltIn);
    }

    [Fact]
    public void ExtractsComponentEventContractsFromEmbeddedScript()
    {
        const string source = """
            <template><View /></template>
            <script lang="csharp">
              public static readonly ComponentEvent<int> ItemSelectedEvent = new("item-selected");
              internal static readonly ComponentEvent ClosedEvent = new("closed");
              private static readonly ComponentEvent IgnoredEvent = new("ignored");
              public static readonly Other.ComponentEvent<int> UnrelatedEvent = new("unrelated");
            </script>
            """;

        var analyzer = new TemplateSemanticAnalyzer();
        var contracts = analyzer.BuildEmbeddedEventContracts(
            new[] { ("Card.sqx", source, "Sample") });

        var events = Assert.Single(contracts).Value;
        Assert.Collection(
            events.OrderBy(item => item.Name),
            closed =>
            {
                Assert.Equal("closed", closed.Name);
                Assert.Equal("ClosedEvent", closed.MemberName);
                Assert.Equal("onClosed", closed.SqxName);
                Assert.Equal("@closed", closed.SqvName);
                Assert.False(closed.HasDetail);
            },
            selected =>
            {
                Assert.Equal("item-selected", selected.Name);
                Assert.Equal("ItemSelectedEvent", selected.MemberName);
                Assert.Equal("onItemSelected", selected.SqxName);
                Assert.Equal("@item-selected", selected.SqvName);
                Assert.True(selected.HasDetail);
                Assert.Equal("int", selected.DetailTypeName);
            });
    }

    [Fact]
    public void MergesComponentEventContractsFromCodeBehind()
    {
        const string codeBehind = """
            namespace Square.Events
            {
                public sealed class ComponentEvent<T>
                {
                    public ComponentEvent(string name) { }
                }
            }
            namespace Sample
            {
                public partial class Card
                {
                    public static readonly Square.Events.ComponentEvent<string> SavedEvent = new("saved");
                }
            }
            """;
        const string component = """
            <template><View /></template>
            <script>
              internal static readonly ComponentEvent ClosedEvent = new("closed");
            </script>
            """;

        var compilation = CreateCompilation(codeBehind);
        var inputs = new[] { ("Card.sqx", component, "Sample") };
        var catalog = TemplateCatalog.FromCompilation(compilation, inputs);
        var card = Assert.Single(catalog.Components, item => item.TypeName == "Sample.Card");

        var events = catalog.GetEvents(card);
        Assert.Contains(events, item => item.MemberName == "ClosedEvent" && item.Name == "closed");
        Assert.Contains(events, item =>
            item.MemberName == "SavedEvent" &&
            item.Name == "saved" &&
            item.DetailTypeName == "global::System.String");
    }

    [Fact]
    public void AmbiguousComponentEventAliasesAreExcluded()
    {
        const string source = """
            <template><View /></template>
            <script>
              public static readonly ComponentEvent<int> DashedEvent = new("item-selected");
              public static readonly ComponentEvent<string> CompactEvent = new("itemselected");
            </script>
            """;

        var contracts = new TemplateSemanticAnalyzer().BuildEmbeddedEventContracts(
            new[] { ("Card.sqx", source, "Sample") });

        Assert.Empty(Assert.Single(contracts).Value);
    }

    [Fact]
    public void DuplicateComponentEventMembersDoNotThrowDuringEditing()
    {
        const string source = """
            <template><View /></template>
            <script>
              public static readonly ComponentEvent FirstEvent = new("first");
              public static readonly ComponentEvent FirstEvent = new("second");
            </script>
            """;

        var compilation = CreateCompilation();
        var inputs = new[] { ("Card.sqx", source, "Sample") };
        var catalog = TemplateCatalog.FromCompilation(compilation, inputs);
        var card = Assert.Single(catalog.Components, item => item.TypeName == "Sample.Card");
        var exception = Record.Exception(() => catalog.GetEvents(card));
        Assert.Null(exception);
    }

    private static Compilation CreateCompilation(string source = "public class Consumer { }")
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var references = (trustedPlatformAssemblies ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path))
            .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        return CSharpCompilation.Create(
            "TemplateCatalogTests",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
