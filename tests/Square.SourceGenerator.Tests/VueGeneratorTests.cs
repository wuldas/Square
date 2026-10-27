using System;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Square.Compiler;
using Square.Runtime.Binding;
using Xunit;

namespace Square.Compiler.Tests;

public class VueGeneratorTests
{

    [Fact]
    public void SqvScriptUsingsCompileIntoGeneratedComponent()
    {
        // The script using must be hoisted to a valid scope: StringBuilder only resolves when
        // the directive is emitted outside the generated class body.
        const string template = """
            <template><View /></template>
            <script lang="csharp">
              using System.Text;

              public StringBuilder Buffer = new();
            </script>
            """;

        var compilation = CreateCompilation("public sealed class Placeholder { }");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("MarkdownCard.sqv", template)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void MultilineScriptUsingCompiles()
    {
        const string template = """
            <template><View /></template>
            <script>
              using
                  System;

              private DateTime CreatedAt = DateTime.UtcNow;
            </script>
            """;

        var compilation = CreateCompilation("public sealed class Placeholder { }");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("MultilineUsing.sqx", template)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ComponentStyleSectionGeneratedCodeCompiles()
    {
        const string template = """
            <template><View class="page" /></template>
            <style>
              .page { color: #123456; gap: 8px !important; }
              @media screen { .page:hover { opacity: 0.5; } }
              @keyframes fade { from { opacity: 0; } to { opacity: 1; } }
            </style>
            """;

        var compilation = CreateCompilation("public sealed class Placeholder { }");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("StyledCard.sqx", template)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }


    [Fact]
    public void TemplateDirectoryContributesToDefaultNamespace()
    {
        // The code-behind partial only merges (and sees the script field) when the generator
        // places the component in the directory-derived namespace.
        const string template = """
            <template><View /></template>
            <script>
              public int Width = 3;
            </script>
            """;
        const string codeBehind = """
            namespace Square.Sample.Components.Forms;
            public partial class LoginCard
            {
                public int ReadWidth() => Width;
            }
            """;

        var compilation = CreateCompilation(codeBehind);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("Components/Forms/LoginCard.sqv", template)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ExplicitScriptNamespaceOverridesTemplateDirectory()
    {
        const string template = """
            <template><View /></template>
            <script namespace="Custom.Components">
              public int Width = 3;
            </script>
            """;
        const string codeBehind = """
            namespace Custom.Components;
            public partial class LoginCard
            {
                public int ReadWidth() => Width;
            }
            """;

        var compilation = CreateCompilation(codeBehind);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("Components/Forms/LoginCard.sqv", template)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void TemplateDirectorySegmentsAreValidCSharpIdentifiers()
    {
        const string template = """
            <template><View /></template>
            <script>
              public int Width = 3;
            </script>
            """;
        const string codeBehind = """
            namespace Square.Sample.feature_pages._class;
            public partial class LoginCard
            {
                public int ReadWidth() => Width;
            }
            """;

        var compilation = CreateCompilation(codeBehind);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("feature-pages/class/LoginCard.sqv", template)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ComponentsInDifferentDirectoriesCanShareAName()
    {
        // Same-named script members are only legal while the two Card components stay in their
        // distinct directory-derived namespaces; collapsing them would duplicate the member.
        const string card = """
            <template><View /></template>
            <script>public int Value = 1;</script>
            """;

        var compilation = CreateCompilation("public sealed class Placeholder { }");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("Admin/Card.sqv", card), new InMemoryAdditionalText("Store/Card.sqv", card)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void CrossDirectoryComponentCanBeImportedFromScript()
    {
        // The script using must make <Card> resolvable for the template; otherwise the
        // analyzer reports an unknown element.
        const string card = "<template><View /></template>";
        const string page = """
            <template><Card /></template>
            <script lang="csharp">
              using Square.Sample.Shared;
            </script>
            """;

        var compilation = CreateCompilation("public sealed class Placeholder { }");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("Shared/Card.sqv", card), new InMemoryAdditionalText("Pages/Page.sqv", page)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void TemplatesUnderResourceDirectoriesAreIgnored()
    {
        const string source = "<template><View /></template>";

        var result = RunGenerator(
            new InMemoryAdditionalText("Public/Static.sqv", source),
            new InMemoryAdditionalText("Assets/Embedded.sqv", source));

        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void SqvReactiveObjectBindingsGeneratedCodeCompiles()
    {
        const string template = """
            <template xmlns="urn:square:ui"><Button v-bind="Props" v-on="Listeners">Save</Button></template>
            <script namespace="TestApp"></script>
            """;
        const string codeBehind = """
            using System;
            using System.Collections.Generic;
            using Square.Events;
            using Square.Runtime.Binding;
            namespace TestApp;
            public partial class ObjectBindings
            {
                public ObservableValue<IReadOnlyDictionary<string, object?>> Props =
                    new(new Dictionary<string, object?> { ["disabled"] = true });
                public ObservableValue<IReadOnlyDictionary<string, Action<Event>>> Listeners =
                    new(new Dictionary<string, Action<Event>>());
            }
            """;

        var compilation = CreateCompilation(codeBehind);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("ObjectBindings.sqv", template)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SqvInlineSvgGeneratedCodeCompiles()
    {
        const string template = """
            <template>
              <svg viewBox="0 0 100 100" width="100" height="100">
                <g transform="translate(5 10)" fill="#123456">
                  <rect x="0" y="0" width="20" height="10" />
                  <circle cx="50" cy="50" r="10" stroke="red" stroke-width="2" />
                  <path d="M0 0 L10 10 Z" fill-opacity="0.5" />
                </g>
              </svg>
            </template>
            """;

        var compilation = CreateCompilation("public sealed class Placeholder { }");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("InlineSvg.sqv", template)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }


    [Fact]
    public void SqvDynamicArgumentsAndScopedSlotsCompile()
    {
        const string card = "<template><View /></template>";
        const string template = """
            <template>
              <Card>
                <template #[SlotName]="slotProps">
                  <Button :[PropertyName]="Value" @[EventName].stop.prevent="OnEvent">
                    {{ slotProps.Get<string>("label") }}
                  </Button>
                </template>
              </Card>
            </template>
            <script lang="csharp">
              public string SlotName = "header";
              public string PropertyName = "disabled";
              public string EventName = "click";
              public bool Value = true;
              private void OnEvent(Event e) { }
            </script>
            """;

        var compilation = CreateCompilation("public sealed class Placeholder { }");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("Card.sqv", card), new InMemoryAdditionalText("DynamicSlots.sqv", template)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SqvScopedSlotDestructuringUsesDeclaredContractTypes()
    {
        const string source = """
            using Square.UI;
            namespace Square.Sample;
            public sealed class RowSlotProps
            {
                public int Item { get; init; }
                public string Label { get; init; } = "";
            }
            [SlotContract("row", typeof(RowSlotProps))]
            public sealed class ContractCard : UIElement { }
            """;
        const string template = """
            <template>
              <ContractCard>
                <template #row="{ item: row, label }">
                  <Button @click="() => Consume(row + 1, label.ToUpperInvariant())" />
                </template>
              </ContractCard>
            </template>
            <script>private void Consume(int item, string label) { }</script>
            """;

        var compilation = CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("TypedSlot.sqv", template)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SqvHtmlInputVModelCompiles()
    {
        // <Input> resolves to the native HTML input under the XHTML default namespace; the
        // generated value binding and input write-back must produce valid C#.
        const string template = """
            <template>
              <Input type="password" v-model="Password" />
            </template>
            <script lang="csharp">
              public ObservableValue<string> Password = new("square123");
            </script>
            """;

        var compilation = CreateCompilation("public sealed class Placeholder { }");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("PasswordModel.sqv", template)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SqvVModelModifiersGeneratedCodeCompiles()
    {
        const string template = """
            <template xmlns="urn:square:ui">
              <View>
                <Input v-model.trim.lazy="Name" @change="OnNameChanged" />
                <Input v-model.number="Age" />
              </View>
            </template>
            <script lang="csharp">
              public ObservableValue<string> Name = new("Ada");
              public ObservableValue<double> Age = new(12);
              private void OnNameChanged() { }
            </script>
            """;

        var compilation = CreateCompilation("public sealed class Placeholder { }");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("ModelModifiers.sqv", template)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SqvControlVModelGeneratedCodeCompiles()
    {
        const string template = """
            <template xmlns="urn:square:ui">
              <View>
                <CheckBox v-model="RememberMe" @change="OnRememberChanged" />
                <Select @change="OnPlanChanged" v-model="Plan" />
              </View>
            </template>
            <script lang="csharp">
              public ObservableValue<bool> RememberMe = new(true);
              public ObservableValue<string> Plan = new("Pro");
              private void OnRememberChanged() { }
              private void OnPlanChanged() { }
            </script>
            """;

        var compilation = CreateCompilation("public sealed class Placeholder { }");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("ControlModel.sqv", template)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SqvVModelWithAdditionalHandlersCompiles()
    {
        const string normalizing = """
            <template xmlns="urn:square:ui"><View /></template>
            <script lang="csharp">
              public string Value = "";
              public static readonly ComponentEvent<string> ChangeEvent = new("change");
            </script>
            """;
        const string template = """
            <template xmlns="urn:square:ui">
              <View>
                <Input v-model="Password" @input="OnPasswordChanged" />
                <Input @input="OnNameChanged" v-model="Name" />
                <NormalizingControl v-model="Custom" @change="OnCustomChanged" />
              </View>
            </template>
            <script lang="csharp">
              public ObservableValue<string> Password = new("");
              public ObservableValue<string> Name = new("");
              public ObservableValue<string> Custom = new("");
              private void OnPasswordChanged() { }
              private void OnNameChanged() { }
              private void OnCustomChanged() { }
            </script>
            """;

        var compilation = CreateCompilation("public sealed class Placeholder { }");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("NormalizingControl.sqv", normalizing),
             new InMemoryAdditionalText("ModelWithHandler.sqv", template)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Id == "SQV0005");
        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SqvVModelOnBatchComponentCompiles()
    {
        const string card = """
            <template><View /></template>
            <script lang="csharp">
              public string Value = "";
              public static readonly ComponentEvent<string> ChangeEvent = new("change");
            </script>
            """;
        const string usage = """
            <template>
              <Card v-model="Title" />
            </template>
            <script lang="csharp">
              public ObservableValue<string> Title = new("");
            </script>
            """;

        var compilation = CreateCompilation("public sealed class Placeholder { }");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("Card.sqv", card), new InMemoryAdditionalText("ScopedModel.sqv", usage)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SqvUndefinedComponentEventReportsElementDiagnostic()
    {
        const string source = """
            <template>
              <EventlessCard onMissing={OnMissing} />
            </template>
            <script lang="csharp">
              private void OnMissing() { }
            </script>
            """;

        var result = RunGenerator(
            new InMemoryAdditionalText("EventlessCard.sqv", """
                <template><View /></template>
                <script lang="csharp">
                  public static readonly ComponentEvent<int> ChangeEvent = new("change");
                </script>
                """),
            new InMemoryAdditionalText("UndefinedEvent.sqv", source));

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "SQX0005");
        Assert.Contains("declares no event 'missing'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CodeBehind.sqx")]
    [InlineData("CodeBehind.sqv")]
    public void CodeBehindPartialCompilesWithEventsAndRefs(string path)
    {
        var isVue = path.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase);
        var eventAttribute = isVue
            ? "@click=\"OnClick\""
            : "onClick={OnClick}";
        var refAttribute = isVue ? "ref=\"SaveButton\"" : "ref={SaveButton}";
        var template =
            "<template xmlns=\"urn:square:ui\"><Button " + refAttribute + " " + eventAttribute + ">Save</Button></template>" +
            "<script namespace=\"TestApp\"></script>";
        const string codeBehind = """
            namespace TestApp;
            public partial class CodeBehind
            {
                private void OnClick(Square.Events.Event e)
                {
                    SaveButton.TextContent = "Saved";
                }
            }
            """;

        var compilation = CreateCompilation(codeBehind);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("CodeBehind.sqv", template)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void CodeBehindPaintOverrideCompiles()
    {
        const string template = "<template><View /></template><script namespace=\"TestApp\"></script>";
        const string codeBehind = """
            namespace TestApp;
            public partial class PaintedComponent
            {
                public override void Paint(Square.Graphics.IRenderContext context) { }
            }
            """;

        var compilation = CreateCompilation(codeBehind);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("PaintedComponent.sqv", template)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SqvKeyedVForGeneratedCodeCompiles()
    {
        const string template = """
            <template>
              <Text v-for="item in Items" :key="item.Id">{{ item.Name }}</Text>
            </template>
            <script namespace="TestApp"></script>
            """;
        const string codeBehind = """
            using Square.Runtime.Binding;
            namespace TestApp;
            public partial class KeyedList
            {
                public ObservableCollection<Row> Items = new();
            }
            public sealed record Row(int Id, string Name);
            """;

        var compilation = CreateCompilation(codeBehind);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("KeyedList.sqv", template)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SqvNestedVForAndVIfGeneratedCodeCompiles()
    {
        const string template = """
            <template>
              <View v-for="row in Rows">
                <Text v-if="row.Active" ref="ActiveText">{{ row.Name }}</Text>
              </View>
            </template>
            <script namespace="TestApp"></script>
            """;
        const string codeBehind = """
            using Square.Runtime.Binding;
            namespace TestApp;
            public partial class Nested
            {
                public ObservableCollection<Row> Rows = new();
            }
            public sealed record Row(bool Active, string Name);
            """;

        var compilation = CreateCompilation(codeBehind);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("Nested.sqv", template)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SqxControlFlowFallbacksCompile()
    {
        const string template = """
            <template>
              <View>
                <Show when={Visible} fallback={<Text text="hidden" />}>
                  <Text text="shown" />
                </Show>
                <For each={Items} fallback={<Text text="empty" />}>{(it)=><Text>{it}</Text>}</For>
                <Index each={Items} fallback={<Text text="empty index" />}>{(it)=><Text>{it}</Text>}</Index>
                <Switch fallback={<Text text="unknown" />}>
                  <Match when={Visible}><Text text="matched" /></Match>
                </Switch>
              </View>
            </template>
            <script lang="csharp">
              public ObservableValue<bool> Visible = new(false);
              public ObservableCollection<string> Items = new();
            </script>
            """;

        var compilation = CreateCompilation("public sealed class Placeholder { }");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("Fallbacks.sqx", template)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("Child.sqx", "Parent.sqx", "onItemSelected={OnSelected}")]
    [InlineData("Child.sqv", "Parent.sqv", "@item-selected=\"OnSelected\"")]
    public void CustomComponentTypedEventHandlersCompile(
        string childPath,
        string parentPath,
        string eventAttribute)
    {
        const string child = "<template><View /></template><script>public static readonly ComponentEvent<int> ItemSelectedEvent = new(\"item-selected\");</script>";
        var parent = "<template><Child " + eventAttribute + " /></template>" +
            "<script>private void OnSelected(CustomEvent<int> e) { _ = e.Detail; }</script>";
        var compilation = CreateCompilation("public sealed class Placeholder { }");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText(childPath, child), new InMemoryAdditionalText(parentPath, parent)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void NoDetailComponentEventHandlersCompile()
    {
        const string child = """
            <template><View /></template>
            <script>
              public static readonly ComponentEvent ClosedEvent = new("closed");
              public static readonly ComponentEvent SavedEvent = new("saved");
            </script>
            """;
        const string parent = """
            <template><Child onClosed={OnClosed} onSaved={OnSaved} /></template>
            <script>
              private void OnClosed() { }
              private void OnSaved(Event e) { }
            </script>
            """;
        var compilation = CreateCompilation("public sealed class Placeholder { }");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("Child.sqx", child), new InMemoryAdditionalText("Parent.sqx", parent)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }


    private static GeneratorResult RunGenerator(params AdditionalText[] files)
    {
        var compilation = CreateCompilation("public sealed class Placeholder { }");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            files,
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult();
        return new GeneratorResult(
            result.GeneratedTrees
                .Where(tree => !tree.FilePath.EndsWith("SquareHotReload.g.cs", StringComparison.Ordinal))
                .ToImmutableArray(),
            result.GeneratedTrees,
            result.Diagnostics);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (references.All(reference => reference.Display != typeof(PropAttribute).Assembly.Location))
            references.Add(MetadataReference.CreateFromFile(typeof(PropAttribute).Assembly.Location));
        var objectModelAssembly = typeof(System.Collections.Specialized.INotifyCollectionChanged).Assembly.Location;
        if (references.All(reference => reference.Display != objectModelAssembly))
            references.Add(MetadataReference.CreateFromFile(objectModelAssembly));
        return CSharpCompilation.Create(
            "VueGeneratorTests",
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

    private sealed record GeneratorResult(
        ImmutableArray<SyntaxTree> GeneratedTrees,
        ImmutableArray<SyntaxTree> AllGeneratedTrees,
        ImmutableArray<Diagnostic> Diagnostics);
}
