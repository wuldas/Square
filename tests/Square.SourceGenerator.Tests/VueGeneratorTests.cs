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
    public void SqvFileGeneratesComponent()
    {
        const string source = """
            <template>
              <View>
                <Text>{{ Title }}</Text>
              </View>
            </template>
            <script lang="csharp">
              public ObservableValue<string> Title = new("Hello");
            </script>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("VueCard.sqv", source));

        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Contains(result.GeneratedTrees, tree => tree.FilePath.Contains("VueCard", StringComparison.Ordinal));
        Assert.Contains(result.GeneratedTrees, tree => tree.GetText().ToString().Contains("partial class VueCard", StringComparison.Ordinal));
    }

    [Fact]
    public void SqvScriptUsingsAreEmittedOutsideTheGeneratedClass()
    {
        const string source = """
            <template>
              <MarkdownViewer />
            </template>
            <script lang="csharp">
              using Square.Extensions.Markdown;

              public string Title = "Markdown";
            </script>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("MarkdownCard.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Contains("using Square.Extensions.Markdown;", generated);
        Assert.True(
            generated.IndexOf("using Square.Extensions.Markdown;", StringComparison.Ordinal) <
            generated.IndexOf("partial class MarkdownCard", StringComparison.Ordinal));
        Assert.Contains("new MarkdownViewer()", generated);
        Assert.Contains("public string Title = \"Markdown\";", generated);
    }

    [Fact]
    public void MultilineScriptUsingIsEmittedOutsideTheGeneratedClass()
    {
        const string source = """
            <template><View /></template>
            <script>
              using
                  System;

              private DateTime CreatedAt = DateTime.UtcNow;
            </script>
            """;

        var generated = Assert.Single(RunGenerator(
            new InMemoryAdditionalText("MultilineUsing.sqx", source)).GeneratedTrees)
            .GetText().ToString();

        var usingPosition = generated.IndexOf("using System;", StringComparison.Ordinal);
        var classPosition = generated.IndexOf("partial class MultilineUsing", StringComparison.Ordinal);
        var scriptPosition = generated.IndexOf("#region Script", StringComparison.Ordinal);
        Assert.InRange(usingPosition, 0, classPosition - 1);
        Assert.DoesNotContain("using\n", generated.Substring(scriptPosition), StringComparison.Ordinal);
    }

    [Fact]
    public void TemplateDirectoryContributesToDefaultNamespace()
    {
        const string source = "<template><View /></template>";

        var generated = Assert.Single(RunGenerator(
            new InMemoryAdditionalText("Components/Forms/LoginCard.sqv", source)).GeneratedTrees)
            .GetText().ToString();

        Assert.Contains("namespace Square.Sample.Components.Forms;", generated);
        Assert.Contains("partial class LoginCard", generated);
    }

    [Fact]
    public void ExplicitScriptNamespaceOverridesTemplateDirectory()
    {
        const string source = """
            <template><View /></template>
            <script namespace="Custom.Components"></script>
            """;

        var generated = Assert.Single(RunGenerator(
            new InMemoryAdditionalText("Components/Forms/LoginCard.sqv", source)).GeneratedTrees)
            .GetText().ToString();

        Assert.Contains("namespace Custom.Components;", generated);
        Assert.DoesNotContain("namespace Square.Sample.Components.Forms;", generated);
    }

    [Fact]
    public void TemplateDirectorySegmentsAreValidCSharpIdentifiers()
    {
        const string source = "<template><View /></template>";

        var generated = Assert.Single(RunGenerator(
            new InMemoryAdditionalText("feature-pages/class/LoginCard.sqv", source)).GeneratedTrees)
            .GetText().ToString();

        Assert.Contains("namespace Square.Sample.feature_pages._class;", generated);
    }

    [Fact]
    public void ComponentsInDifferentDirectoriesCanShareAName()
    {
        const string source = "<template><View /></template>";

        var result = RunGenerator(
            new InMemoryAdditionalText("Admin/Card.sqv", source),
            new InMemoryAdditionalText("Store/Card.sqv", source));
        var generated = result.GeneratedTrees.Select(tree => tree.GetText().ToString()).ToArray();

        Assert.Equal(2, generated.Length);
        Assert.Contains(generated, code => code.Contains("namespace Square.Sample.Admin;", StringComparison.Ordinal));
        Assert.Contains(generated, code => code.Contains("namespace Square.Sample.Store;", StringComparison.Ordinal));
    }

    [Fact]
    public void CrossDirectoryComponentCanBeImportedFromScript()
    {
        const string card = "<template><View /></template>";
        const string page = """
            <template><Card /></template>
            <script lang="csharp">
              using Square.Sample.Shared;
            </script>
            """;

        var result = RunGenerator(
            new InMemoryAdditionalText("Shared/Card.sqv", card),
            new InMemoryAdditionalText("Pages/Page.sqv", page));
        var pageCode = result.GeneratedTrees
            .Select(tree => tree.GetText().ToString())
            .Single(code => code.Contains("partial class Page", StringComparison.Ordinal));

        Assert.Contains("using Square.Sample.Shared;", pageCode);
        Assert.Contains("namespace Square.Sample.Pages;", pageCode);
        Assert.Contains("new Card()", pageCode);
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
    public void SqvBindingsAndEventsUseExistingEmitterSemantics()
    {
        const string source = """
            <template>
              <View>
                <Text :text="Title" />
                <Button @click="OnClick">Save</Button>
              </View>
            </template>
            <script lang="csharp">
              public ObservableValue<string> Title = new("Hello");
              private void OnClick() { }
            </script>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("Bindings.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains(".BindProperty(\"TextContent\", Title);", generated);
        Assert.Contains("AddEventListener", generated);
        Assert.Contains("\"click\"", generated);
        Assert.Contains("OnClick", generated);
    }

    [Fact]
    public void SqvObjectBindingsUseRuntimeBindingProtocol()
    {
        const string source = """
            <template>
              <Button v-bind="Props" v-on="Listeners">Save</Button>
            </template>
            <script lang="csharp">
              public IReadOnlyDictionary<string, object?> Props = new Dictionary<string, object?>();
              public IReadOnlyDictionary<string, Action<Event>> Listeners = new Dictionary<string, Action<Event>>();
            </script>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("ObjectBindings.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("SqvObjectBinding.BindProperties", generated);
        Assert.Contains("SqvObjectBinding.BindEvents", generated);
        Assert.Contains(".RegisterGeneratedResource(SqvObjectBinding.BindProperties", generated);
        Assert.Contains(".RegisterGeneratedResource(SqvObjectBinding.BindEvents", generated);
        Assert.DoesNotContain("_generatedBindings", generated);
    }

    [Fact]
    public void SqvReactiveObjectBindingsGeneratedCodeCompiles()
    {
        const string template = """
            <template><Button v-bind="Props" v-on="Listeners">Save</Button></template>
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
    public void SqvLowercaseViewTextLowersToBuiltInControls()
    {
        const string source = """
            <template>
              <view style="user-select: text">hello</view>
            </template>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("LowercaseView.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("new Square.Controls.View()", generated);
        Assert.Contains("new Square.Controls.Text(\"hello\")", generated);
        Assert.Contains(".Children.Add", generated);
        Assert.Contains(".Style.CssText = \"user-select: text\"", generated);
        Assert.DoesNotContain("new view", generated);
    }

    [Fact]
    public void SqvInlineSvgLowersToSvgDomElements()
    {
        const string source = """
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

        var result = RunGenerator(new InMemoryAdditionalText("InlineSvg.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("new Square.UI.Svg.SVGSVGElement()", generated);
        Assert.Contains("new Square.UI.Svg.SVGGElement()", generated);
        Assert.Contains("new Square.UI.Svg.SVGRectElement()", generated);
        Assert.Contains("new Square.UI.Svg.SVGCircleElement()", generated);
        Assert.Contains("new Square.UI.Svg.SVGPathElement()", generated);
        Assert.Contains("SetProperty(\"ViewBox\", \"0 0 100 100\")", generated);
        Assert.Contains("SetProperty(\"StrokeWidth\", 2)", generated);
        Assert.Contains("SetProperty(\"FillOpacity\", \"0.5\")", generated);
        Assert.DoesNotContain("new svg", generated);
        Assert.DoesNotContain(".Slots.Set", generated);
    }

    [Fact]
    public void SqvScrollViewerLowersToBuiltInControl()
    {
        const string source = """
            <template>
              <ScrollViewer>
                <Text>Scrollable</Text>
              </ScrollViewer>
            </template>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("Scroller.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("new Square.Controls.ScrollViewer()", generated);
        Assert.DoesNotContain("new ScrollViewer", generated);
    }

    [Fact]
    public void SqvVirtualizedControlsLowerToBuiltInTypes()
    {
        const string source = """
            <template>
              <VirtualList item-height="24" overscan-count="2" />
              <VirtualTree item-height="28" overscan-count="3" indent-size="16" />
            </template>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("Virtualized.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("new Square.Controls.VirtualList()", generated);
        Assert.Contains("new Square.Controls.VirtualTree()", generated);
        Assert.Contains("SetProperty(\"ItemHeight\", 24)", generated);
        Assert.Contains("SetProperty(\"OverscanCount\", 2)", generated);
        Assert.Contains("SetProperty(\"IndentSize\", 16)", generated);
    }

    [Fact]
    public void SqvPopupLowersToBuiltInControl()
    {
        const string source = """
            <template>
              <Popup>
                <Text>Floating</Text>
              </Popup>
            </template>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("PopupCard.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("new Square.Controls.Popup()", generated);
        Assert.DoesNotContain("new Popup", generated);
    }

    [Fact]
    public void SqvDialogLowersToBuiltInControl()
    {
        const string source = """
            <template>
              <Dialog>
                <Button>Close</Button>
              </Dialog>
            </template>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("DialogCard.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("new Square.Controls.Dialog()", generated);
        Assert.DoesNotContain("new Dialog", generated);
    }

    [Fact]
    public void SqvMenuTreeLowersToBuiltInControlsAndProperties()
    {
        const string source = """
            <template>
              <MenuBar>
                <MenuItem text="View">
                  <Menu>
                    <MenuItem text="Grid" checkable="true" shortcut="Ctrl+G" stays-open-on-click="true" />
                    <MenuSeparator />
                    <MenuItem text="Dark" group="theme" />
                  </Menu>
                </MenuItem>
              </MenuBar>
            </template>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("Menus.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("new Square.Controls.MenuBar()", generated);
        Assert.Contains("new Square.Controls.MenuItem()", generated);
        Assert.Contains("new Square.Controls.Menu()", generated);
        Assert.Contains("new Square.Controls.MenuSeparator()", generated);
        Assert.Contains("SetProperty(\"IsCheckable\", true)", generated);
        Assert.Contains("SetProperty(\"ShortcutText\", \"Ctrl+G\")", generated);
        Assert.Contains("SetProperty(\"StaysOpenOnClick\", true)", generated);
        Assert.Contains("SetProperty(\"GroupName\", \"theme\")", generated);
    }

    [Fact]
    public void SqvVIfLowersToShowDirective()
    {
        const string source = """
            <template>
              <View>
                <Text v-if="Visible">Visible</Text>
              </View>
            </template>
            <script lang="csharp">
              public ObservableValue<bool> Visible = new(true);
            </script>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("Conditional.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("new ShowNode(Visible", generated);
    }

    [Fact]
    public void SqvTemplateSlotSyntaxLowersToSquareSlots()
    {
        const string source = """
            <template>
              <Card>
                <template v-slot:header>
                  <Text>Header</Text>
                </template>
                <Text>Body</Text>
              </Card>
            </template>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("SlotUsage.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains(".Slots.Set(\"header\"", generated);
        Assert.Contains(".Slots.Set(\"\"", generated);
        Assert.DoesNotContain("new template", generated);
        Assert.DoesNotContain("new Fragment", generated);
    }

    [Fact]
    public void SqvDynamicArgumentsAndScopedSlotsUseRuntimeProtocols()
    {
        const string source = """
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

        var result = RunGenerator(new InMemoryAdditionalText("DynamicSlots.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Contains(".Slots.Set(SlotName, (__slotParent", generated);
        Assert.Contains("SqvObjectBinding.BindProperty", generated);
        Assert.Contains("SqvObjectBinding.BindEvent", generated);
        Assert.Contains("e.StopPropagation();", generated);
        Assert.Contains("e.PreventDefault();", generated);
        Assert.Contains("slotProps.Get<string>(\"label\")", generated);
        Assert.DoesNotContain(".Slots.Set(\"SlotName\"", generated);
    }

    [Fact]
    public void SqvSlotOutletEmitsDynamicNameAndScopedProperties()
    {
        const string source = """
            <template>
              <slot :name="ActiveSlot" :item="CurrentItem" label="Row" />
            </template>
            <script lang="csharp">
              public string ActiveSlot = "row";
              public int CurrentItem = 4;
            </script>
            """;

        var generated = Assert.Single(RunGenerator(
            new InMemoryAdditionalText("SlotProvider.sqv", source)).GeneratedTrees).GetText().ToString();

        Assert.Contains("new SlotProps()", generated);
        Assert.Contains(".Set(\"item\", CurrentItem);", generated);
        Assert.Contains(".Set(\"label\", \"Row\");", generated);
        Assert.Contains("Slots.Render(ActiveSlot", generated);
        Assert.DoesNotContain("Slots.Render(\"ActiveSlot\"", generated);
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
                  <Text>{{ row }}: {{ label }}</Text>
                </template>
              </ContractCard>
            </template>
            """;

        var compilation = CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("TypedSlot.sqv", template)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult();
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("__slotProps.Get<int>(\"item\")", generated);
        Assert.Contains("__slotProps.Get<string>(\"label\")", generated);
        Assert.Contains("var row =", generated);
    }

    [Fact]
    public void SqvInputVModelBindsValueAndWritesBackOnInput()
    {
        const string source = """
            <template>
              <Input type="password" v-model="Password" />
            </template>
            <script lang="csharp">
              public ObservableValue<string> Password = new("square123");
            </script>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("PasswordModel.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains(".BindProperty(\"Value\", Password);", generated);
        Assert.Contains(".AddEventListener(\"input\", e => Password.Value = ((Square.Controls.Input)e.Target!).Value);", generated);
    }

    [Fact]
    public void SqvVModelModifiersAffectTextInputWriteBack()
    {
        const string source = """
            <template>
              <View>
                <Input v-model.trim.lazy="Name" />
                <Input v-model.number="Age" />
              </View>
            </template>
            <script lang="csharp">
              public ObservableValue<string> Name = new("Ada");
              public ObservableValue<double> Age = new(12);
            </script>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("ModelModifiers.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains(".AddEventListener(\"change\", e => Name.Value = ((Square.Controls.Input)e.Target!).Value.Trim());", generated);
        Assert.Contains(".AddEventListener(\"input\", e => Age.Value = double.Parse(((Square.Controls.Input)e.Target!).Value, System.Globalization.CultureInfo.InvariantCulture));", generated);
    }

    [Fact]
    public void SqvControlVModelUsesControlSpecificPropertyAndEvent()
    {
        const string source = """
            <template>
              <View>
                <CheckBox v-model="RememberMe" />
                <Select v-model="Plan" />
              </View>
            </template>
            <script lang="csharp">
              public ObservableValue<bool> RememberMe = new(true);
              public ObservableValue<string> Plan = new("Pro");
            </script>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("ControlModel.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains(".BindProperty(\"IsChecked\", RememberMe);", generated);
        Assert.Contains(".AddEventListener(\"change\", e => RememberMe.Value = ((Square.Controls.CheckBox)e.Target!).IsChecked);", generated);
        Assert.Contains(".BindProperty(\"Value\", Plan);", generated);
        Assert.Contains(".AddEventListener(\"change\", e => Plan.Value = ((Square.Controls.Select)e.Target!).Value);", generated);
    }

    [Fact]
    public void SqvVModelCanHaveAdditionalInputHandler()
    {
        const string source = """
            <template>
              <Input v-model="Password" @input="OnPasswordChanged" />
            </template>
            <script lang="csharp">
              public ObservableValue<string> Password = new("");
              private void OnPasswordChanged() { }
            </script>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("ModelWithHandler.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "SQV0005");
        Assert.Contains("Password.Value = ((Square.Controls.Input)e.Target!).Value", generated);
        Assert.Contains("AddEventListener(\"input\", OnPasswordChanged);", generated);
    }

    [Fact]
    public void GeneratedResourcesSurviveOrdinaryDetach()
    {
        const string source = """
            <template><Button ref={SaveButton}>Save</Button></template>
            <script lang="csharp">
              protected override void OnDetachedCore() { }
            </script>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("Cleanup.sqx", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.DoesNotContain("protected override void OnGeneratedDetachedCore()", generated);
        Assert.Equal(1, generated.Split("SaveButton = null!;").Length - 1);
        Assert.Contains("protected override void OnDetachedCore()", generated);
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
            "<template><Button " + refAttribute + " " + eventAttribute + ">Save</Button></template>" +
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
            [new InMemoryAdditionalText(path, template)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void TitleBarLowersToBuiltInControl()
    {
        const string source = """
            <template>
              <TitleBar>
                <Text slot="icon" text="I" />
                <Text text="App" />
                <Button slot="control" text="X" />
              </TitleBar>
            </template>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("WindowTitle.sqx", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("new Square.Controls.TitleBar()", generated);
        Assert.DoesNotContain("new TitleBar()", generated);
        Assert.Contains(".Slots.Set(\"icon\"", generated);
        Assert.Contains(".Slots.Set(\"\"", generated);
        Assert.Contains(".Slots.Set(\"control\"", generated);
        Assert.True(
            generated.IndexOf(".Children.Add(", StringComparison.Ordinal) <
            generated.LastIndexOf(".BuildElementTree();", StringComparison.Ordinal));
    }

    [Fact]
    public void FontIconLowersToBuiltInControlAndMapsIconProperties()
    {
        const string source = """
            <template>
              <FontIcon font-family="Product Icons" glyph="\uE000" />
              <PiIcon icon="Search" />
            </template>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("Icon.sqx", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("new Square.Controls.FontIcon()", generated);
        Assert.Contains("new PiIcon()", generated);
        Assert.Contains(".SetProperty(\"FontFamily\", \"Product Icons\")", generated);
        Assert.Contains(".SetProperty(\"Glyph\", \"\\\\uE000\")", generated);
        Assert.Contains(".SetProperty(\"Icon\", \"Search\")", generated);
    }

    [Fact]
    public void IdAttributeMapsToElementIdProperty()
    {
        const string source = "<template><Input id=\"search-box\" /></template>";

        var generated = Assert.Single(RunGenerator(new InMemoryAdditionalText("Search.sqv", source)).GeneratedTrees)
            .GetText().ToString();

        Assert.Contains(".SetProperty(\"Id\", \"search-box\")", generated);
    }

    [Fact]
    public void SplitterLowersToBuiltInControlAndMapsSizingProperties()
    {
        const string source = """
            <template>
              <Splitter minimum="240" maximum="440" vertical="true" reversed="true" />
            </template>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("Splitter.sqx", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("new Square.Controls.Splitter()", generated);
        Assert.Contains(".SetProperty(\"Minimum\", 240)", generated);
        Assert.Contains(".SetProperty(\"Maximum\", 440)", generated);
        Assert.Contains(".SetProperty(\"IsVertical\", true)", generated);
        Assert.Contains(".SetProperty(\"IsReversed\", true)", generated);
    }

    [Fact]
    public void SqvVForLowersToForNodeWithItemName()
    {
        const string source = """
            <template>
              <View>
                <Text v-for="item in Items">{{ item }}</Text>
              </View>
            </template>
            <script lang="csharp">
              public ObservableCollection<string> Items = new();
            </script>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("List.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("ForNode.Create(Items, item =>", generated);
        Assert.Contains(".AttachTo(", generated);
    }

    [Fact]
    public void SqvVForWithIndexLowersToIndexedForNode()
    {
        const string source = """
            <template>
              <View>
                <Text v-for="(item, index) in Items">{{ item }}</Text>
              </View>
            </template>
            <script lang="csharp">
              public ObservableCollection<string> Items = new();
            </script>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("IndexedList.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("ForNode.Create(Items, (item, index) =>", generated);
        Assert.Contains(".AttachTo(", generated);
    }

    [Fact]
    public void SqvVForKeyLowersToKeyedForNode()
    {
        const string source = """
            <template>
              <View>
                <Text v-for="item in Items" :key="item.Id">{{ item.Name }}</Text>
              </View>
            </template>
            <script lang="csharp">
              public ObservableCollection<Row> Items = new();
            </script>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("KeyedList.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("ForNode.Create(Items, item => item.Id, item =>", generated);
        Assert.DoesNotContain("SetProperty(\"key\"", generated, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqvIndexedVForKeyUsesParenthesizedSelectors()
    {
        const string source = """
            <template>
              <Text v-for="(item, index) in Items" :key="item.Id + index">{{ item.Name }}</Text>
            </template>
            <script lang="csharp">
              public ObservableCollection<Row> Items = new();
            </script>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("IndexedKeyedList.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("ForNode.Create(Items, (item, index) => item.Id + index, (item, index) =>", generated);
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
    public void SqvVElseIfAndVElseLowersToExclusiveShowChain()
    {
        const string source = """
            <template>
              <View>
                <Text v-if="State == 0">A</Text>
                <Text v-else-if="State == 1">B</Text>
                <Text v-else>C</Text>
              </View>
            </template>
            <script lang="csharp">
              public int State = 0;
            </script>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("Chain.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("new ShowNode(State == 0", generated);
        Assert.Contains("new ShowNode(!((State == 0)) && (State == 1)", generated);
        Assert.Contains("new ShowNode(!((State == 0) || (State == 1))", generated);
    }

    [Fact]
    public void SqvEventModifiersEmitStopAndPreventWrapper()
    {
        const string source = """
            <template>
              <Button @click.stop.prevent="OnClick">Save</Button>
            </template>
            <script lang="csharp">
              private void OnClick(Square.Events.Event e) { }
            </script>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("Modifiers.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("e.StopPropagation(); e.PreventDefault(); OnClick(e);", generated);
    }

    [Fact]
    public void SqvVShowBindsIsVisible()
    {
        const string source = """
            <template>
              <View>
                <Text v-show="Visible">Hi</Text>
              </View>
            </template>
            <script lang="csharp">
              public ObservableValue<bool> Visible = new(true);
            </script>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("Show.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains(".BindProperty(\"IsVisible\", Visible);", generated);
    }

    [Fact]
    public void SqvNestedVForAndVIfEmitIndependentNodes()
    {
        const string source = """
            <template>
              <View>
                <View v-for="row in Rows">
                  <Text v-if="row.Active">{{ row.Name }}</Text>
                </View>
              </View>
            </template>
            <script lang="csharp">
              public ObservableCollection<Row> Rows = new();
            </script>
            """;

        var result = RunGenerator(new InMemoryAdditionalText("Nested.sqv", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("ForNode.Create(Rows, row =>", generated);
        Assert.Contains("new ShowNode(row.Active", generated);
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
    public void SqxControlFlowFallbackAndIndexGenerateExpectedNodes()
    {
        const string source = """
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

        var result = RunGenerator(new InMemoryAdditionalText("Fallbacks.sqx", source));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("new ShowNode(Visible", generated);
        Assert.Contains("ForNode.Create(Items", generated);
        Assert.Contains("IndexNode.Create(Items", generated);
        Assert.Contains("new SwitchNode()", generated);
        Assert.Contains("hidden", generated);
        Assert.Contains("empty index", generated);
        Assert.Contains("unknown", generated);
    }

    [Fact]
    public void SqxNestedNativeDirectivesAreEmittedInsideFactories()
    {
        const string source = """
            <template>
              <Show when={Outer}>
                <For each={Items}>{(it)=><Show when={Inner}><Text>{it}</Text></Show>}</For>
              </Show>
            </template>
            <script lang="csharp">
              public ObservableValue<bool> Outer = new(true);
              public ObservableValue<bool> Inner = new(true);
              public ObservableCollection<string> Items = new();
            </script>
            """;

        var generated = Assert.Single(RunGenerator(new InMemoryAdditionalText("NestedSqx.sqx", source)).GeneratedTrees)
            .GetText().ToString();

        Assert.Equal(2, generated.Split("new ShowNode(").Length - 1);
        Assert.Contains("ForNode.Create(Items", generated);
        Assert.Contains(".RegisterGeneratedResource(_show", generated);
        Assert.Contains(".RegisterGeneratedResource(_for", generated);
        Assert.DoesNotContain("_generatedDirectives", generated);
    }

    [Fact]
    public void SqxMixedTextInterpolationPreservesSpacesAndSubscribesSources()
    {
        const string source = """
            <template><Text>Hello {FirstName} {LastName}</Text></template>
            <script lang="csharp">
              public ObservableValue<string> FirstName = new("Ada");
              public ObservableValue<string> LastName = new("Lovelace");
            </script>
            """;

        var generated = Assert.Single(RunGenerator(new InMemoryAdditionalText("MixedText.sqx", source)).GeneratedTrees)
            .GetText().ToString();

        Assert.Contains("\"Hello \"", generated);
        Assert.Contains("BindProperty(\"TextContent\", () =>", generated);
        Assert.Contains(", FirstName, LastName);", generated);
    }

    private static GeneratorDriverRunResult RunGenerator(params AdditionalText[] files)
    {
        var compilation = CreateCompilation("public sealed class Placeholder { }");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            files,
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
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
}
