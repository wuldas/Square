using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Square.Compiler.LanguageServices;
using Square.Controls;
using Square.Events;
using Square.Runtime.Binding;
using Square.UI;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class RichControlTemplateTests
{
    [Theory]
    [InlineData("sqv", "<template #header><Image /><Text :text=\"Label\" /><Button @click=\"Act\">Action</Button></template>")]
    [InlineData("sqx", "<View slot=\"header\"><Image /><Text text={Label} /><Button onClick={Act}>Action</Button></View>")]
    public void HeaderProjectionKeepsTreeHierarchyAndReactiveContent(string dialect, string header)
    {
        var component = Build("<Tree><TreeItem text=\"Fallback\" expanded=\"true\">" + header +
            "<TreeItem text=\"Child\" /></TreeItem></Tree>", dialect);
        var tree = Assert.Single(component.QueryAll<Tree>());
        var parent = Assert.IsType<TreeItem>(Assert.Single(tree.Children));
        var child = Assert.Single(parent.Items);
        Assert.Equal("Fallback", parent.TextContent);
        Assert.Equal("Child", child.TextContent);
        Assert.Same(parent, child.Parent);
        Assert.NotNull(parent.Header);
        Assert.Single(parent.Header.QueryAll<Square.Controls.Image>());
        var text = Assert.Single(parent.Header.QueryAll<Square.Controls.Text>());
        Assert.Equal("initial", text.TextContent);
        Field<ObservableValue<string>>(component, "Label").Value = "updated";
        Assert.Equal("updated", text.TextContent);

        var action = Assert.Single(parent.Header.QueryAll<Button>());
        action.DispatchEvent(StandardEvents.CreateClick());
        Assert.Equal(1, Field<int>(component, "Actions"));
        Assert.True(parent.IsExpanded);
        Assert.Null(tree.SelectedItem);
        parent.Collapse();
        Assert.False(child.IsVisible);
        Assert.True(parent.Header.IsVisible);
        component.DiscardGeneratedSubtree();
        Field<ObservableValue<string>>(component, "Label").Value = "discarded";
        Assert.Equal("updated", text.TextContent);
    }

    [Theory]
    [InlineData("sqv", ":header=\"CustomHeader\"")]
    [InlineData("sqx", "header={CustomHeader}")]
    public void HeaderPropertyBindingCanReplaceAndClearItsContent(string dialect, string attribute)
    {
        var component = Build("<Tree><TreeItem text=\"Fallback\" " + attribute + "><TreeItem text=\"Child\" /></TreeItem></Tree>", dialect);
        var tree = Assert.Single(component.QueryAll<Tree>());
        var item = Assert.IsType<TreeItem>(Assert.Single(tree.Children));
        var source = Field<ObservableValue<Element?>>(component, "CustomHeader");
        var original = Assert.IsType<Square.Controls.Text>(item.Header);
        Assert.Equal("Original header", original.TextContent);
        var replacement = new Square.Controls.Text("Replacement header");
        source.Value = replacement;
        Assert.Same(replacement, item.Header);
        Assert.Null(original.Parent);
        source.Value = null;
        Assert.Null(item.Header);
        Assert.Null(replacement.Parent);
        Assert.Equal("Fallback", item.TextContent);
        Assert.Equal("Child", Assert.Single(item.Items).TextContent);
        component.DiscardGeneratedSubtree();
    }

    [Theory]
    [InlineData("sqv")]
    [InlineData("sqx")]
    public void TextBodyAndExplicitTextBothKeepNestedItems(string dialect)
    {
        var component = Build("<Tree><TreeItem>Folder<TreeItem text=\"Child\" /></TreeItem>" +
            "<TreeItem text=\"Explicit\"><TreeItem text=\"Other child\" /></TreeItem></Tree>", dialect);
        var tree = Assert.Single(component.QueryAll<Tree>());
        Assert.Equal(2, tree.Children.Count);
        var folder = Assert.IsType<TreeItem>(tree.Children[0]);
        var explicitLabel = Assert.IsType<TreeItem>(tree.Children[1]);
        Assert.Equal("Folder", folder.TextContent);
        Assert.Equal("Child", Assert.Single(folder.Items).TextContent);
        Assert.Equal("Explicit", explicitLabel.TextContent);
        Assert.Equal("Other child", Assert.Single(explicitLabel.Items).TextContent);
        Assert.Null(folder.Header);
        Assert.Null(explicitLabel.Header);
        component.DiscardGeneratedSubtree();
    }

    [Theory]
    [InlineData("sqv", "<TriStateCheckBox text=\"All\" v-model=\"Selection\" @change=\"Capture\" />")]
    [InlineData("sqx", "<TriStateCheckBox text=\"All\" checked={Selection} onChange={CaptureNative} />")]
    public void NullableCheckedBindingsPreserveAllThreeStates(string dialect, string body)
    {
        var component = Build(body, dialect);
        var box = Assert.Single(component.QueryAll<TriStateCheckBox>());
        var selection = Field<ObservableValue<bool?>>(component, "Selection");
        Assert.Null(box.IsChecked);
        selection.Value = false;
        Assert.Equal(false, box.IsChecked);
        foreach (var expected in new bool?[] { true, null, false })
        {
            box.DispatchEvent(StandardEvents.CreateClick());
            Assert.Equal(expected, box.IsChecked);
            Assert.Equal(expected, selection.Value);
            Assert.Equal(expected, Field<bool?>(component, "LastObserved"));
        }
        Assert.Equal(3, Field<int>(component, "Changes"));
        selection.Value = null;
        Assert.Null(box.IsChecked);
        Assert.Equal(3, Field<int>(component, "Changes"));
        component.DiscardGeneratedSubtree();
    }

    [Fact]
    public void TriStateTagAndModelAndHeaderPropertyAppearInCompletion()
    {
        const string tagSource = "<template><TriState";
        var tags = TemplateCompletionService.GetItems(tagSource, tagSource.Length, "Editing.sqv");
        Assert.Contains(tags, item => item.Label == "TriStateCheckBox");
        const string modelSource = "<template><TriStateCheckBox v-mo";
        var models = TemplateCompletionService.GetItems(modelSource, modelSource.Length, "Editing.sqv");
        Assert.Contains(models, item => item.Label == "v-model");
        const string headerSource = "<template><TreeItem hea";
        var properties = TemplateCompletionService.GetItems(headerSource, headerSource.Length, "Editing.sqv");
        Assert.Contains(properties, item => item.Label == "header");
    }

    private static T Field<T>(UIElement component, string name) =>
        (T)component.GetType().GetField(name)!.GetValue(component)!;

    private static UIElement Build(string body, string dialect)
    {
        var template = "<template xmlns=\"urn:square:ui\">" + body + "</template>" + """
            <script namespace="RichControlFixtures">
              public ObservableValue<string> Label = new("initial");
              public ObservableValue<Element?> CustomHeader = new(new Square.Controls.Text("Original header"));
              public ObservableValue<bool?> Selection = new(null);
              public int Actions;
              public int Changes;
              public bool? LastObserved;
              private void Act() { Actions++; }
              private void Capture() { Changes++; LastObserved = Selection.Value; }
              private void CaptureNative(Event e)
              {
                  Selection.Value = ((TriStateCheckBox)e.Target!).IsChecked;
                  Capture();
              }
            </script>
            """;
        const string factory = """
            using Square.UI;
            namespace RichControlFixtures;
            public static class Factory
            {
                public static UIElement Create()
                {
                    var component = new RichControls();
                    component.BuildElementTree();
                    return component;
                }
            }
            """;
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "";
        var references = trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(typeof(UIElement).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("RichControl_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(factory)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("RichControls." + dialect, template)],
            (CSharpParseOptions)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        using var stream = new MemoryStream();
        var emitted = output.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        return (UIElement)assembly.GetType("RichControlFixtures.Factory")!.GetMethod("Create")!.Invoke(null, null)!;
    }

    private sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
    {
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(content);
    }
}
