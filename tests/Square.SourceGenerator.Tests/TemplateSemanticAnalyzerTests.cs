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

        var analyzer = new TemplateSemanticAnalyzer();
        var contracts = analyzer.BuildPropContracts(
            CreateCompilation(),
            new[] { ("Card.sqx", source, "Sample") });

        var props = Assert.Single(contracts);
        Assert.Equal("Sample.Card", props.Key);
        Assert.Equal(2, props.Value.Length);
        Assert.Contains(props.Value, prop => prop.Name == "Title" && prop.Required && prop.TypeName.Contains("string"));
        Assert.Contains(props.Value, prop => prop.Name == "Count" && !prop.Required && prop.TypeName.Contains("int"));
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

        var analyzer = new TemplateSemanticAnalyzer();
        var contracts = analyzer.BuildPropContracts(
            CreateCompilation(),
            new[] { ("QualifiedCard.sqx", source, "Sample") });

        var prop = Assert.Single(Assert.Single(contracts).Value);
        Assert.Equal("Title", prop.Name);
        Assert.True(prop.Required);
        Assert.Equal("global::Square.Runtime.Binding.ObservableValue<string>", prop.TypeName);
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

    private static Compilation CreateCompilation()
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
            [CSharpSyntaxTree.ParseText("public class Consumer { }")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
