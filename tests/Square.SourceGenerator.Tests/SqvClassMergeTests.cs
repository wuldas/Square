using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Square.Compiler.LanguageServices;
using Square.Compiler.Parser;
using Square.Runtime.Binding;
using Square.Runtime.State;
using Square.UI;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class SqvClassMergeTests
{
    [Theory]
    [InlineData("View", ":class", false)]
    [InlineData("View", "v-bind:class", true)]
    [InlineData("div", ":class", true)]
    [InlineData("div", "v-bind:class", false)]
    public void SqvStaticClassesSurviveDynamicUpdates(string tag, string binding, bool dynamicFirst)
    {
        var dynamicAttribute = binding + "=\"Sources[0]\"";
        var attributes = dynamicFirst
            ? dynamicAttribute + " class=\"base shared\""
            : "class=\"base shared\" " + dynamicAttribute;
        var source = new ObservableValue<string?>("active shared");
        var component = BuildComponent("<" + tag + " " + attributes + " />", "sqv", source);
        var element = Assert.Single(component.Children);

        AssertClasses(element, "base", "shared", "active");
        source.Value = "next";
        AssertClasses(element, "base", "shared", "next");
        source.Value = "";
        AssertClasses(element, "base", "shared");
        source.Value = null;
        AssertClasses(element, "base", "shared");
        source.Value = "last\tshared\nlast";
        AssertClasses(element, "base", "shared", "last");

        component.DiscardGeneratedSubtree();
        source.Value = "after-discard";
        AssertClasses(element, "base", "shared", "last");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SqxSharedEmitterMergesClassesInEitherAttributeOrder(bool dynamicFirst)
    {
        var attributes = dynamicFirst
            ? "class={Sources[0]} class=\"base shared\""
            : "class=\"base shared\" class={Sources[0]}";
        var source = new ObservableValue<string?>("active");
        var component = BuildComponent("<View " + attributes + " />", "sqx", source);
        var element = Assert.Single(component.Children);

        AssertClasses(element, "base", "shared", "active");
        source.Value = "next";
        AssertClasses(element, "base", "shared", "next");
        source.Value = null;
        AssertClasses(element, "base", "shared");
        component.DiscardGeneratedSubtree();
    }

    [Theory]
    [InlineData("Sources[0].Value", "active")]
    [InlineData("() => Sources[0].Value", "active")]
    [InlineData("null", null)]
    public void StaticClassWhitespaceAndStringExpressionsAreMerged(string expression, string? dynamicClass)
    {
        var template = "<View :class=\"" + expression + "\" class=\"base\tshared\" />";
        var component = BuildComponent(template, "sqv", new ObservableValue<string?>("active"));

        var expected = dynamicClass == null ? new[] { "base", "shared" } : new[] { "base", "shared", dynamicClass };
        AssertClasses(Assert.Single(component.Children), expected);
        component.DiscardGeneratedSubtree();
    }

    [Fact]
    public void MultipleNodesKeepTheirOwnStaticAndDynamicClasses()
    {
        const string template = """
            <View class="first shared" :class="Sources[0]" />
            <View :class="Sources[1]" class="second shared" />
            """;
        var first = new ObservableValue<string?>("one");
        var second = new ObservableValue<string?>("two");
        var component = BuildComponent(template, "sqv", first, second);
        Assert.Equal(2, component.Children.Count);

        first.Value = "changed";
        AssertClasses(component.Children[0], "first", "shared", "changed");
        AssertClasses(component.Children[1], "second", "shared", "two");
        second.Value = "";
        AssertClasses(component.Children[0], "first", "shared", "changed");
        AssertClasses(component.Children[1], "second", "shared");
        component.DiscardGeneratedSubtree();
    }

    [Theory]
    [InlineData("sqv", "<View v-for=\"source in Sources\" class=\"row shared\" :class=\"source\" />")]
    [InlineData("sqx", "<For each={Sources}>{(it)=><View class=\"row shared\" class={it} />}</For>")]
    public void LoopRowsUpdateAndDisposeIndependently(string dialect, string template)
    {
        var first = new ObservableValue<string?>("one shared");
        var second = new ObservableValue<string?>("two");
        var component = BuildComponent(template, dialect, first, second);
        Assert.Equal(2, component.Children.Count);
        var firstRow = component.Children[0];
        var secondRow = component.Children[1];

        AssertClasses(firstRow, "row", "shared", "one");
        AssertClasses(secondRow, "row", "shared", "two");
        first.Value = "changed";
        AssertClasses(firstRow, "row", "shared", "changed");
        AssertClasses(secondRow, "row", "shared", "two");
        firstRow.DiscardGeneratedSubtree();
        first.Value = "after-discard";
        second.Value = null;
        AssertClasses(firstRow, "row", "shared", "changed");
        AssertClasses(secondRow, "row", "shared");
        component.DiscardGeneratedSubtree();
    }

    [Theory]
    [InlineData("class=\"a\" class=\"b\"")]
    [InlineData(":class=\"First\" :class=\"Second\"")]
    [InlineData(":class=\"First\" v-bind:class=\"Second\"")]
    [InlineData("v-bind:class=\"First\" v-bind:class=\"Second\"")]
    [InlineData("class=\"base\" :class=\"First\" class=\"duplicate\"")]
    [InlineData(":class=\"First\" class=\"base\" :class=\"Second\"")]
    [InlineData("class=\"base\" :class=\"First\" v-bind:class=\"Second\"")]
    [InlineData("class=\"base\" class=\"duplicate\" :class=\"First\"")]
    [InlineData(":class=\"First\" :class=\"Second\" class=\"base\"")]
    [InlineData("id=\"static\" :id=\"DynamicId\"")]
    [InlineData("class=\"base\" :class=\"First\" value=\"static\" :value=\"DynamicValue\"")]
    [InlineData("class=\"base\" :class=\"First\" :disabled=\"First\" :IsDisabled=\"Second\"")]
    public void DuplicateBindingsStillReportSqv0005(string attributes)
    {
        var source = "<template><View " + attributes + " /></template>";
        var exception = Assert.Throws<SqxParseException>(() => SqvDocumentParser.Parse(source, "ClassMerge.sqv"));

        Assert.Equal("SQV0005", exception.DiagnosticId);
    }

    [Theory]
    [InlineData("class=\"a\" class=\"b\"", false)]
    [InlineData("class={First} class={Second}", false)]
    [InlineData("class=\"base\" class={First} class=\"duplicate\"", false)]
    [InlineData("class={First} class=\"base\" class={Second}", false)]
    [InlineData("class=\"base\" class=\"duplicate\" class={First}", false)]
    [InlineData("class={First} class={Second} class=\"base\"", false)]
    [InlineData("Class=\"base\" class=\"duplicate\" class={First}", false)]
    [InlineData("id=\"static\" id={DynamicId}", false)]
    [InlineData("class=\"base\" class={First} value=\"static\" value={DynamicValue}", false)]
    [InlineData("class=\"base\" class={First} class={Second}", true)]
    public void SqxDuplicateBindingsStillReportSqx0001(string attributes, bool inLoop)
    {
        var body = "<View " + attributes + " />";
        if (inLoop) body = "<For each={Items}>{(it)=>" + body + "}</For>";
        var result = SquareDocumentService.Parse("<template>" + body + "</template>", "DuplicateClass.sqx");
        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal("SQX0001", diagnostic.Id);
        Assert.Contains("Duplicate attribute", diagnostic.Message);
    }

    [Fact]
    public void SqxMergedClassStillValidatesItsExpression()
    {
        const string source = "<template><View class=\"base\" class={invalid +} /></template>";
        var result = SquareDocumentService.Parse(source, "InvalidClass.sqx");
        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal("SQX0001", diagnostic.Id);
        Assert.Contains("not valid C#", diagnostic.Message);
    }

    [Fact]
    public void MergedClassStillValidatesItsExpression()
    {
        const string source = "<template><View class=\"base\" :class=\"invalid +\" /></template>";
        var exception = Assert.Throws<SqxParseException>(() => SqvDocumentParser.Parse(source, "ClassMerge.sqv"));

        Assert.Equal("SQV0009", exception.DiagnosticId);
    }

    [Fact]
    public void ReactiveInterfaceBindingRetainsStaticClassesAndStopsAfterDispose()
    {
        using var store = new Store<string?>("active shared");
        IReactiveValue<string?> source = store;
        var target = new Square.Controls.View();
        var binding = SqvObjectBinding.BindClass(target, "base shared", source);

        AssertClasses(target, "base", "shared", "active");
        store.Set("next");
        AssertClasses(target, "base", "shared", "next");
        store.Set(null);
        AssertClasses(target, "base", "shared");
        binding.Dispose();
        binding.Dispose();
        store.Set("after-dispose");
        AssertClasses(target, "base", "shared");
    }

    [Fact]
    public void PlainAndGetterClassBindingsSupportEmptyAndNullValues()
    {
        var target = new Square.Controls.View();
        using var plain = SqvObjectBinding.BindClass(target, "base", (string?)null);
        AssertClasses(target, "base");
        using var empty = SqvObjectBinding.BindClass(target, "base", "");
        AssertClasses(target, "base");
        using var getter = SqvObjectBinding.BindClass(target, "base shared", () => "shared active");
        AssertClasses(target, "base", "shared", "active");
    }

    [Fact]
    public void ReactiveClassUpdatesUseElementDispatcherAndStopAfterDisposal()
    {
        var document = new UIDocument();
        var target = document.CreateElement<Square.Controls.View>();
        document.Body.Children.Add(target);
        var source = new ObservableValue<string?>("initial");
        var binding = SqvObjectBinding.BindClass(target, "base", source);
        var worker = new Thread(() => source.Value = "queued");
        worker.Start();
        worker.Join();
        AssertClasses(target, "base", "initial");

        document.FlushPendingUpdates();
        AssertClasses(target, "base", "queued");

        worker = new Thread(() => source.Value = "discarded");
        worker.Start();
        worker.Join();
        binding.Dispose();
        document.FlushPendingUpdates();
        AssertClasses(target, "base", "queued");
    }

    [Fact]
    public void OrdinaryClassPropertyWritesKeepReplacementSemantics()
    {
        var target = new Square.Controls.View();
        target.ClassList.Add("previous");
        target.SetProperty("class", "replacement");
        AssertClasses(target, "replacement");
        var source = new ObservableValue<string?>("bound");
        target.BindProperty("class", source);
        AssertClasses(target, "bound");
        source.Value = null;
        AssertClasses(target);
        target.DiscardGeneratedSubtree();
    }

    private static void AssertClasses(Element element, params string[] expected) =>
        Assert.Equal(expected.OrderBy(name => name), element.ClassList.GetAll().OrderBy(name => name));

    private static UIElement BuildComponent(string body, string dialect, params ObservableValue<string?>[] sources)
    {
        var template = "<template>" + body + "</template>" + """
            <script namespace="ClassMergeFixtures">
              public ObservableValue<string?>[] Sources = [];
            </script>
            """;
        const string factory = """
            using Square.Runtime.Binding;
            using Square.UI;
            namespace ClassMergeFixtures;
            public static class Factory
            {
                public static UIElement Create(ObservableValue<string?>[] sources)
                {
                    var component = new ClassMerge { Sources = sources };
                    component.BuildElementTree();
                    return component;
                }
            }
            """;
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(ObservableValue<>).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(System.Collections.Specialized.INotifyCollectionChanged).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "SqvClassMerge_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(factory)],
            references.GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase).Select(group => group.First()),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("ClassMerge." + dialect, template)],
            (CSharpParseOptions)compilation.SyntaxTrees.First().Options);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(driver.GetRunResult().GeneratedTrees, tree => tree.ToString().Contains("SqvObjectBinding.BindClass("));

        using var stream = new MemoryStream();
        var result = output.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        var create = assembly.GetType("ClassMergeFixtures.Factory")!.GetMethod("Create")!
            .CreateDelegate<Func<ObservableValue<string?>[], UIElement>>();
        return create(sources);
    }

    private sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
    {
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(content);
    }
}
