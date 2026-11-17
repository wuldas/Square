using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Square.Compiler;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class SqvBindingCompileTests
{
    [Fact]
    public void SingleMethodCallInterpolationCompilesThroughBindText()
    {
        const string source = """
            <template><Text>{{ ShortName(Name) }}</Text></template>
            <script lang="csharp">
              public string Name = "Track (Live)";
              private static string ShortName(string value) => value.Split(' ')[0];
            </script>
            """;

        var output = Generate(("Page.sqv", source), out var generated);

        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("BindText(ShortName(Name))", generated);
    }

    [Fact]
    public void LiteralPropBindingAssignsThePropertyDirectly()
    {
        const string card = """
            <template><View /></template>
            <script lang="csharp">
              [Prop]
              public float Size
              {
                get => GetProperty<float>(nameof(Size));
                set => SetProperty(nameof(Size), value);
              }
            </script>
            """;
        const string page = """
            <template><CoverArt :size="40" /></template>
            <script lang="csharp">
              using Square.Sample.Shared;
            </script>
            """;

        var output = Generate(("Shared/CoverArt.sqv", card), ("Pages/Page.sqv", page), out var generated);

        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        // Direct assignment runs the CLR setter and lets C# convert int to float.
        Assert.Matches(@"(?m)^\s+\w+\.Size = 40;", generated);
    }

    [Fact]
    public void ObservableCollectionPropertyBindingCompiles()
    {
        const string source = """
            <template><List class="tracks" :items="Rows" /></template>
            <script lang="csharp">
              public ObservableCollection<string> Rows = new() { "one", "two" };
            </script>
            """;

        var output = Generate(("TrackList.sqv", source), out _);

        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static Compilation Generate(
        (string Path, string Source) file,
        out string generatedSources) =>
        Generate(new[] { file }, out generatedSources);

    private static Compilation Generate(
        (string Path, string Source) first,
        (string Path, string Source) second,
        out string generatedSources) =>
        Generate(new[] { first, second }, out generatedSources);

    private static Compilation Generate(
        (string Path, string Source)[] files,
        out string generatedSources)
    {
        var compilation = CreateCompilation("public sealed class Placeholder { }");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            files.Select(file => (AdditionalText)new InMemoryAdditionalText(file.Path, file.Source)),
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var generated = output.SyntaxTrees.Skip(1).Select(tree => tree.ToString());
        generatedSources = string.Join("\n", generated);
        return output;
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var references = (trustedPlatformAssemblies ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path))
            .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        return CSharpCompilation.Create(
            "SqvBindingCompileTests",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
    {
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(content, Encoding.UTF8);
    }
}
