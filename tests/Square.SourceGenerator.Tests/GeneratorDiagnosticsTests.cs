using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Square.Compiler;
using Square.Runtime.Binding;
using Xunit;

namespace Square.Compiler.Tests;

public class GeneratorDiagnosticsTests
{
    [Fact]
    public void ReportsMissingRequiredPropAtCustomComponentUsage()
    {
        const string source = """
            <template><RequiredCard /></template>
            <script lang="csharp">
              [Prop(Required = true)]
              public ObservableValue<string> Title { get; set; } = new("");
            </script>
            """;
        const string usage = "<template><RequiredCard /></template>";

        var diagnostics = RunGenerator(
            new InMemoryAdditionalText("RequiredCard.sqx", source),
            new InMemoryAdditionalText("Usage.sqx", usage));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SQX0003");
    }

    [Fact]
    public void AcceptsRequiredPropWhenCallerProvidesIt()
    {
        const string component = """
            <template><View /></template>
            <script lang="csharp">
              [Prop(Required = true)]
              public ObservableValue<string> Title { get; set; } = new("");
            </script>
            """;

        var diagnostics = RunGenerator(
            new InMemoryAdditionalText("RequiredCard.sqx", component),
            new InMemoryAdditionalText("Usage.sqx", "<template><RequiredCard Title=\"Hello\" /></template>"));

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "SQX0003");
    }

    [Fact]
    public void RequiredPropResolutionUsesCurrentDirectoryNamespace()
    {
        const string required = """
            <template><View /></template>
            <script lang="csharp">
              [Prop(Required = true)]
              public ObservableValue<string> Title { get; set; } = new("");
            </script>
            """;

        var diagnostics = RunGenerator(
            new InMemoryAdditionalText("Admin/Card.sqv", required),
            new InMemoryAdditionalText("Store/Card.sqv", "<template><View /></template>"),
            new InMemoryAdditionalText("Admin/Page.sqv", "<template><Card /></template>"),
            new InMemoryAdditionalText("Store/Page.sqv", "<template><Card /></template>"));

        Assert.Single(diagnostics, diagnostic => diagnostic.Id == "SQX0003");
    }

    [Fact]
    public void RequiredPropResolutionUsesScriptNamespaceImports()
    {
        const string required = """
            <template><View /></template>
            <script lang="csharp">
              [Prop(Required = true)]
              public ObservableValue<string> Title { get; set; } = new("");
            </script>
            """;
        const string usage = """
            <template><Card /></template>
            <script lang="csharp">
              using Square.Sample.Shared;
            </script>
            """;

        var diagnostics = RunGenerator(
            new InMemoryAdditionalText("Shared/Card.sqv", required),
            new InMemoryAdditionalText("Pages/Page.sqv", usage));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SQX0003");
    }

    [Fact]
    public void ReportsDuplicateRefNamesInSameComponent()
    {
        const string source = "<template><View ref={MyBtn}><Button ref={MyBtn} /></View></template>";

        var diagnostics = RunGenerator(new InMemoryAdditionalText("DuplicateRef.sqx", source));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SQX0006");
    }

    [Fact]
    public void AcceptsUniqueRefNames()
    {
        const string source = "<template><View ref={Root}><Button ref={SaveBtn} /></View></template>";

        var diagnostics = RunGenerator(new InMemoryAdditionalText("UniqueRef.sqx", source));

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "SQX0006");
    }

    [Fact]
    public void ReportsMatchOutsideSwitchAsInvalidParent()
    {
        const string source = "<template><Match when={true}><Text text=\"x\" /></Match></template>";

        var diagnostics = RunGenerator(new InMemoryAdditionalText("BadMatch.sqx", source));

        // 根级 SkipStandalone → SQXD005（也可伴随 SQXD003）
        Assert.Contains(diagnostics, d => d.Id is "SQXD003" or "SQXD005");
    }

    [Fact]
    public void ReportsShowMissingWhenAttribute()
    {
        const string source = "<template><Show><Text text=\"x\" /></Show></template>";

        var diagnostics = RunGenerator(new InMemoryAdditionalText("BadShow.sqx", source));

        Assert.Contains(diagnostics, d => d.Id == "SQXD002");
    }

    [Fact]
    public void AcceptsShowWithWhenAttribute()
    {
        const string source = "<template><Show when={true}><Text text=\"x\" /></Show></template>";

        var diagnostics = RunGenerator(new InMemoryAdditionalText("OkShow.sqx", source));

        Assert.DoesNotContain(diagnostics, d => d.Id is "SQXD002" or "SQXD003" or "SQXD005");
    }

    [Fact]
    public void ReportsPropTypeMismatchForIntPropWithStringConstant()
    {
        const string component = """
            <template><View /></template>
            <script lang="csharp">
              [Prop]
              public ObservableValue<int> Count { get; set; } = new(0);
            </script>
            """;

        var diagnostics = RunGenerator(
            new InMemoryAdditionalText("TypedCard.sqx", component),
            new InMemoryAdditionalText("Usage.sqx", "<template><TypedCard Count=\"hello\" /></template>"));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SQX0007");
    }

    [Fact]
    public void AcceptsCorrectPropTypeForIntWithStringLiteral()
    {
        const string component = """
            <template><View /></template>
            <script lang="csharp">
              [Prop]
              public ObservableValue<int> Count { get; set; } = new(0);
            </script>
            """;

        var diagnostics = RunGenerator(
            new InMemoryAdditionalText("TypedCard.sqx", component),
            new InMemoryAdditionalText("Usage.sqx", "<template><TypedCard Count=\"42\" /></template>"));

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "SQX0007");
    }

    [Fact]
    public void CodeBehindPropParticipatesInValidation()
    {
        const string codeBehind = """
            using Square.Runtime.Binding;
            namespace TestApp;
            public partial class RequiredCard
            {
                [Prop(Required = true)]
                public ObservableValue<int> Count { get; } = new(0);
            }
            """;

        var diagnostics = RunGeneratorWithSource(
            codeBehind,
            new InMemoryAdditionalText(
                "RequiredCard.sqx",
                "<template><View /></template><script namespace=\"TestApp\"></script>"),
            new InMemoryAdditionalText("Usage.sqx", "<template><RequiredCard Count=\"bad\" /></template>"));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SQX0007");
    }

    [Fact]
    public void ReportsMismatchedSqvClosingTagAtClosingTag()
    {
        const string source = """
            <template>
              <View>
                <Text>broken</View>
              </Text>
            </template>
            """;

        var diagnostic = Assert.Single(RunGenerator(new InMemoryAdditionalText("Mismatch.sqv", source))
            .Where(d => d.Id == "SQV0001"));

        Assert.Contains("does not match", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal(2, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void ReportsInvalidSqvVFor()
    {
        const string source = "<template><Text v-for=\"item Items\">x</Text></template>";

        var diagnostics = RunGenerator(new InMemoryAdditionalText("BadFor.sqv", source));

        Assert.Contains(diagnostics, d => d.Id == "SQV0003");
    }

    [Theory]
    [InlineData("v-else")]
    [InlineData("v-else-if=\"Ready\"")]
    public void ReportsSqvBranchWithoutPrecedingIf(string directive)
    {
        var source = "<template><Text " + directive + ">x</Text></template>";

        var diagnostics = RunGenerator(new InMemoryAdditionalText("OrphanedElse.sqv", source));

        Assert.Contains(diagnostics, d => d.Id == "SQV0004");
    }

    [Theory]
    [InlineData("v-html=\"Markup\"", "SQV0002")]
    [InlineData(":key=\"Id\"", "SQV0002")]
    [InlineData("@click.once=\"Handle\"", "SQV0002")]
    [InlineData("v-model=\"Value\"", "SQV0002")]
    [InlineData("#header=\"{ item: class }\"", "SQV0008")]
    [InlineData("v-custom=\"Value\"", "SQV0002")]
    public void ReportsUnsupportedSqvSyntax(string attribute, string diagnosticId)
    {
        var source = "<template><Text " + attribute + ">x</Text></template>";

        var diagnostics = RunGenerator(new InMemoryAdditionalText("Unsupported.sqv", source));

        Assert.Contains(diagnostics, d => d.Id == diagnosticId);
    }

    [Theory]
    [InlineData("v-bind=\"Props\" v-bind=\"OtherProps\"")]
    [InlineData("v-on=\"Listeners\" v-on=\"OtherListeners\"")]
    public void ReportsDuplicateSqvObjectBindings(string attributes)
    {
        var source = "<template><View " + attributes + " /></template>";

        var diagnostics = RunGenerator(new InMemoryAdditionalText("DuplicateObjectBinding.sqv", source));

        Assert.Contains(diagnostics, d => d.Id == "SQV0005");
    }

    [Theory]
    [InlineData("<template><Text>{{ Name</Text></template>")]
    [InlineData("<template><Text title=\"broken></Text></template>")]
    [InlineData("<template><Text><!-- broken</Text></template>")]
    [InlineData("<template><Text /></template><script lang=\"javascript\"></script>")]
    public void ReportsMalformedSqvAsSqvSyntaxError(string source)
    {
        var diagnostics = RunGenerator(new InMemoryAdditionalText("Malformed.sqv", source));

        Assert.Contains(diagnostics, d => d.Id == "SQV0001");
    }

    [Theory]
    [InlineData("value=\"a\" value=\"b\"")]
    [InlineData("value=\"a\" :value=\"Name\"")]
    [InlineData("@click=\"First\" @click=\"Second\"")]
    [InlineData(":value=\"Other\" v-model=\"Name\"")]
    [InlineData("v-for=\"item in Items\" :key=\"item.Id\" v-bind:key=\"item.OtherId\"")]
    public void ReportsDuplicateSqvBindings(string attributes)
    {
        var source = "<template><Input " + attributes + " /></template>";

        var diagnostics = RunGenerator(new InMemoryAdditionalText("DuplicateBinding.sqv", source));

        Assert.Contains(diagnostics, d => d.Id == "SQV0005");
    }

    [Theory]
    [InlineData("component")]
    [InlineData("Teleport")]
    [InlineData("Transition")]
    [InlineData("TransitionGroup")]
    [InlineData("KeepAlive")]
    [InlineData("Suspense")]
    public void ReportsUnsupportedVueBuiltInComponents(string tagName)
    {
        var source = "<template><" + tagName + " /></template>";

        var diagnostics = RunGenerator(new InMemoryAdditionalText("BuiltIn.sqv", source));

        Assert.Contains(diagnostics, d => d.Id == "SQV0007");
    }

    [Theory]
    [InlineData("<template><Text>{{ Name + }}</Text></template>")]
    [InlineData("<template><Text :text=\"Name +\" /></template>")]
    [InlineData("<template><Text v-if=\"Ready &&\">x</Text></template>")]
    [InlineData("<template><Text v-for=\"item in Items.\">x</Text></template>")]
    [InlineData("<template><Button @click=\"Handle(\" /></template>")]
    public void ReportsInvalidCSharpTemplateExpressions(string source)
    {
        var diagnostics = RunGenerator(new InMemoryAdditionalText("InvalidExpression.sqv", source));

        Assert.Contains(diagnostics, d => d.Id == "SQV0009");
    }

    [Fact]
    public void ReportsSqvMemberBindingSemanticErrors()
    {
        const string source = """
            <template><Button :disabled="MissingFlag" /></template>
            <script lang="csharp">
              public bool ExistingFlag = true;
            </script>
            """;

        var diagnostics = RunGenerator(new InMemoryAdditionalText("Semantic.sqv", source));

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "SQV0013" && diagnostic.GetMessage().Contains("MissingFlag", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsMissingScopedSlotContract()
    {
        const string source = "<template><Card><template #row=\"{ item }\"></template></Card></template>";

        var diagnostics = RunGenerator(new InMemoryAdditionalText("MissingContract.sqv", source));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SQV0010");
    }

    [Theory]
    [InlineData("<template><View></Text></template>")]
    [InlineData("<template><View></template>")]
    [InlineData("<template><Text text=\"broken /></template>")]
    [InlineData("<template><Text text={Name /></template>")]
    [InlineData("<template><!-- broken<View /></template>")]
    [InlineData("<template></View></template>")]
    public void ReportsMalformedSqxAsSqxSyntaxError(string source)
    {
        var diagnostics = RunGenerator(new InMemoryAdditionalText("Malformed.sqx", source));

        Assert.Contains(diagnostics, d => d.Id == "SQX0001");
    }

    [Fact]
    public void ReportsInvalidSwitchChild()
    {
        var diagnostics = RunGenerator(new InMemoryAdditionalText(
            "InvalidSwitch.sqx",
            "<template><Switch><Text text=\"bad\" /></Switch></template>"));

        Assert.Contains(diagnostics, d => d.Id == "SQXD006");
    }

    [Theory]
    [InlineData("<template><Text>{Name +}</Text></template>")]
    [InlineData("<template><Text text={Name +} /></template>")]
    public void ReportsInvalidSqxCSharpExpressions(string source)
    {
        var diagnostics = RunGenerator(new InMemoryAdditionalText("InvalidExpression.sqx", source));

        Assert.Contains(diagnostics, d => d.Id == "SQX0001");
    }

    [Fact]
    public void ReportsDuplicateSqxAttributes()
    {
        var diagnostics = RunGenerator(new InMemoryAdditionalText(
            "Duplicate.sqx",
            "<template><Input value={First} value={Second} /></template>"));

        Assert.Contains(diagnostics, d => d.Id == "SQX0001");
    }

    [Fact]
    public void ReportsUnsupportedCustomControlFlowAttachShape()
    {
        const string source = """
            using Square.Directives;
            [SqxDirective("Broken", Pattern = "ControlFlowAttach", FieldPrefix = "_broken", PrimaryAttribute = "when")]
            public sealed class BrokenDirective { }
            """;

        var diagnostics = RunGeneratorWithSource(
            source,
            new InMemoryAdditionalText("BrokenUsage.sqx", "<template><Broken when={true} /></template>"));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SQXD007");
    }

    private static ImmutableArray<Diagnostic> RunGenerator(params AdditionalText[] files)
        => RunGeneratorWithSource("public sealed class Placeholder { }", files);

    private static ImmutableArray<Diagnostic> RunGeneratorWithSource(string source, params AdditionalText[] files)
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var references = (trustedPlatformAssemblies ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path))
            .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (references.All(reference => reference.Display != typeof(PropAttribute).Assembly.Location))
            references.Add(MetadataReference.CreateFromFile(typeof(PropAttribute).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            files,
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult().Diagnostics;
    }

    private sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
    {
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(content, Encoding.UTF8);
    }
}
