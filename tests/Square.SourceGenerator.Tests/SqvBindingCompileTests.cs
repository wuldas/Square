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

    [Fact]
    public void IfChainSubscribesToThePropertiesItsConditionsReference()
    {
        const string source = """
            <template>
              <View>
                <Text v-if="Loading">loading</Text>
                <Text v-else-if="HasError">failed</Text>
                <Text v-else>done</Text>
              </View>
            </template>
            <script lang="csharp">
              public bool Loading = true;
              public bool HasError;
            </script>
            """;

        var output = Generate(("Conditional.sqv", source), out var generated);

        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("new SwitchNode(this, \"Loading\", \"HasError\")", generated);
    }

    [Fact]
    public void RootConditionalChainSelectsFirstMatchAndPreservesSiblingOrder()
    {
        const string source = """
            <template>
              <Text v-if="Optional">optional</Text>
              <Text v-if="Loading">loading</Text>

              <!-- Conditions may be separated by comments and blank lines. -->
              <Text v-else-if="HasError">failed</Text>
              <Text v-else-if="ShowPending">pending</Text>
              <Text v-else>done</Text>
              <Text>tail</Text>
            </template>
            <script lang="csharp">
              using System.Linq;
              using Square.Runtime.State;
              using Xunit;

              public ObservableValue<bool> Optional { get; } = new(false);
              public ObservableValue<bool> Loading { get; } = new(false);
              private readonly ObservableValue<bool> _hasError = new(false);
              public IReactiveValue<bool> HasError => _hasError;
              public bool ShowPending => GetProperty<bool>(nameof(ShowPending));

              public static void Verify()
              {
                var doc = new UIDocument();
                var page = new Conditional();
                doc.Body.Children.Add(page);
                page.BuildElementTree();
                void Check(params string[] expected)
                {
                  doc.Context.Reconciler.Flush();
                  Assert.Equal(expected, page.Children.Select(child => Assert.IsType<Square.Controls.Text>(child).TextContent));
                }
                Check("done", "tail");
                page._hasError.Value = true;
                Check("failed", "tail");
                page.Loading.Value = true;
                Check("loading", "tail");
                page._hasError.Value = false;
                page.SetProperty(nameof(ShowPending), true);
                page.Optional.Value = true;
                Check("optional", "loading", "tail");
                page.Loading.Value = false;
                Check("optional", "pending", "tail");
                page.Optional.Value = false;
                page.SetProperty(nameof(ShowPending), false);
                Check("done", "tail");
                page.DiscardGeneratedSubtree();
                page.Loading.Value = true;
                Check("tail");
                Assert.Single(page.ChildNodes);
              }
            </script>
            """;

        var output = Generate(("Conditional.sqv", source), out _);
        VerifyGeneratedComponent(output, "Square.Sample.Conditional");
    }

    [Fact]
    public void OptedInPropSetterReceivesInitialAndReactiveValues()
    {
        const string target = """
            <template><View /></template>
            <script lang="csharp">
              using System.Collections.Generic;

              public int SetterCalls { get; private set; }
              public int LegacySetterCalls { get; private set; }

              [Prop(UseSetter = true)]
              public IReadOnlyList<string> Tracks
              {
                get => GetProperty<IReadOnlyList<string>>(nameof(Tracks)) ?? [];
                set { SetterCalls++; SetProperty(nameof(Tracks), value); }
              }

              [Prop]
              public IReadOnlyList<string> Legacy
              {
                get => GetProperty<IReadOnlyList<string>>(nameof(Legacy)) ?? [];
                set { LegacySetterCalls++; SetProperty(nameof(Legacy), value); }
              }
            </script>
            """;
        const string parent = """
            <template>
              <TrackList ref="List" :tracks="Rows" :legacy="Rows" />
              <TrackList ref="Other" :tracks="OtherRows" />
              <TrackList ref="Snapshot" :tracks="Collection" />
            </template>
            <script lang="csharp">
              using Square.Runtime.State;
              using Xunit;

              public ObservableValue<string[]> Rows { get; } = new(new[] { "one" });
              private readonly ObservableValue<string[]> _otherRows = new(new[] { "other" });
              public IReactiveValue<string[]> OtherRows => _otherRows;
              public ObservableCollection<string> Collection { get; } = new() { "snapshot" };

              public static void Verify()
              {
                var page = new TrackPage();
                page.BuildElementTree();
                var child = page.List;
                Assert.Equal(new[] { "one" }, child.Tracks);
                Assert.Equal(new[] { "one" }, child.Legacy);
                Assert.Equal(1, child.SetterCalls);
                Assert.Equal(0, child.LegacySetterCalls);
                Assert.Equal(new[] { "other" }, page.Other.Tracks);
                Assert.Equal(1, page.Other.SetterCalls);
                Assert.Same(page.Collection, page.Snapshot.Tracks);
                Assert.Equal(1, page.Snapshot.SetterCalls);
                page.Rows.Value = new[] { "two" };
                page._otherRows.Value = new[] { "updated" };
                Assert.Equal(new[] { "two" }, child.Tracks);
                Assert.Equal(new[] { "two" }, child.Legacy);
                Assert.Equal(2, child.SetterCalls);
                Assert.Equal(0, child.LegacySetterCalls);
                Assert.Equal(new[] { "updated" }, page.Other.Tracks);
                Assert.Equal(2, page.Other.SetterCalls);
                page.DiscardGeneratedSubtree();
                page.Rows.Value = new[] { "discarded" };
                Assert.Equal(2, child.SetterCalls);
              }
            </script>
            """;

        var output = Generate(("TrackList.sqv", target), ("TrackPage.sqv", parent), out _);
        VerifyGeneratedComponent(output, "Square.Sample.TrackPage");
    }

    [Fact]
    public void InaccessibleOptedInSetterReportsACompileErrorAtTheBinding()
    {
        const string target = """
            <template><View /></template>
            <script lang="csharp">
              [Prop(UseSetter = true)]
              public string Title { get; private set; } = "";
            </script>
            """;
        const string parent = """
            <template><Card :title="Title" /></template>
            <script lang="csharp">
              public ObservableValue<string> Title { get; } = new("changed");
            </script>
            """;

        var output = Generate(("Card.sqv", target), ("Page.sqv", parent), out _);
        var error = Assert.Single(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("CS0272", error.Id);
        Assert.Equal("Page.sqv", error.Location.GetMappedLineSpan().Path);
    }

    private static void VerifyGeneratedComponent(Compilation compilation, string typeName)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        var verify = assembly.GetType(typeName)!.GetMethod("Verify")!.CreateDelegate<Action>();
        verify();
    }

    [Fact]
    public void ParameterizedEventHandlerRegistersALambdaInsteadOfCallingAtBuildTime()
    {
        const string source = """
            <template>
              <View>
                <Button v-for="p in Items" @click="Pick(p)">x</Button>
              </View>
            </template>
            <script lang="csharp">
              public string[] Items = ["a", "b"];
              private void Pick(string value) { }
            </script>
            """;

        var output = Generate(("Events.sqv", source), out var generated);

        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("Listen(\"click\", e => { Pick(p); })", generated);
    }

    [Fact]
    public void StopModifierAcceptsLambdasAndParameterizedCalls()
    {
        const string source = """
            <template>
              <View>
                <Button @click.stop="() => Pick(Name)">a</Button>
                <Button @click.stop="Pick(Name)">b</Button>
                <Button @click.stop="OnClick">c</Button>
              </View>
            </template>
            <script lang="csharp">
              public string Name = "x";
              private void Pick(string value) { }
              private void OnClick(Square.Events.Event e) { }
            </script>
            """;

        var output = Generate(("Stop.sqv", source), out var generated);

        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("e.StopPropagation(); ((System.Action)(() => Pick(Name)))();", generated);
        Assert.Contains("e.StopPropagation(); Pick(Name);", generated);
        Assert.Contains("e.StopPropagation(); OnClick(e);", generated);
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
