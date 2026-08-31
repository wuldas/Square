using Square.Compiler.LanguageServices;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class CSharpScriptCompletionServiceTests
{
    [Fact]
    public void CompletesNamespacesInUsingDirective()
    {
        const string source = "<template><View /></template><script>using Square.Con</script>";
        var offset = source.IndexOf("</script>", StringComparison.Ordinal);

        var context = TemplateCompletionService.GetContext(source, offset, "Editing.sqx");
        var items = TemplateCompletionService.GetItems(context, source);

        Assert.Equal(TemplateCompletionKind.ScriptNamespace, context.Kind);
        Assert.Equal("Square.Con", context.Prefix);
        Assert.Contains(items, item => item.Label == "Square.Controls" && item.Kind == 9);
    }

    [Fact]
    public void CompletesCurrentScriptMembersAndTypes()
    {
        const string source = """
            <template><View /></template>
            <script>
            private ObservableValue<string> Title = new("");
            private void Save() { Ti }
            </script>
            """;
        var offset = source.IndexOf("Ti }", StringComparison.Ordinal) + "Ti".Length;

        var context = TemplateCompletionService.GetContext(source, offset, "Editing.sqx");
        var items = TemplateCompletionService.GetItems(context, source);

        Assert.Equal(TemplateCompletionKind.ScriptGeneral, context.Kind);
        Assert.Contains(items, item => item.Label == "Title" && item.Kind == 5);
        Assert.DoesNotContain(items, item => item.Label == "Save");

        const string typeSource = "<template><View /></template><script>private But</script>";
        var typeOffset = typeSource.IndexOf("</script>", StringComparison.Ordinal);
        var typeItems = TemplateCompletionService.GetItems(typeSource, typeOffset, "Editing.sqx");
        Assert.Contains(typeItems, item => item.Label == "Button" && item.Kind == 7);
    }

    [Fact]
    public void CompletesMethodParametersAndLocalVariableMembers()
    {
        const string source = """
            <template><View /></template>
            <script>
            private void Save(Event e)
            {
                var title = "Square";
                title.St
            }
            </script>
            """;
        var memberOffset = source.IndexOf("title.St", StringComparison.Ordinal) + "title.St".Length;
        var generalOffset = source.IndexOf("var title", StringComparison.Ordinal);

        var memberContext = TemplateCompletionService.GetContext(source, memberOffset, "Editing.sqx");
        var memberItems = TemplateCompletionService.GetItems(memberContext, source);

        Assert.Equal(TemplateCompletionKind.ScriptMember, memberContext.Kind);
        Assert.Equal("title", memberContext.AttributeName);
        Assert.Contains(memberItems, item => item.Label == "StartsWith" && item.Kind == 2);
        Assert.DoesNotContain(memberItems, item => item.Label == "StopPropagation");

        var parameterItems = TemplateCompletionService.GetItems(source, generalOffset, "Editing.sqx");
        Assert.DoesNotContain(parameterItems, item => item.Label == "title");
    }

    [Fact]
    public void CompletesEventAndTemplateRefMembers()
    {
        const string eventSource = "<template><View /></template><script>private void Save(Event e) { e.St }</script>";
        var eventOffset = eventSource.IndexOf("e.St", StringComparison.Ordinal) + "e.St".Length;
        var eventItems = TemplateCompletionService.GetItems(eventSource, eventOffset, "Editing.sqx");
        Assert.Contains(eventItems, item => item.Label == "StopPropagation");

        const string refSource = "<template><Button ref={SaveButton} /></template><script>private void Save() { SaveButton.Te }</script>";
        var refOffset = refSource.IndexOf("SaveButton.Te", StringComparison.Ordinal) + "SaveButton.Te".Length;
        var refContext = TemplateCompletionService.GetContext(refSource, refOffset, "Editing.sqx");
        var refItems = TemplateCompletionService.GetItems(refContext, refSource);

        Assert.Equal(TemplateCompletionKind.ScriptMember, refContext.Kind);
        Assert.Contains(refItems, item => item.Label == "TextContent" && item.Kind == 10);
    }

    [Fact]
    public void CompletesNestedSquareAndReactiveMembers()
    {
        const string source = """
            <template><Button ref={SaveButton} /></template>
            <script>
            private ObservableValue<string> Title = new("");
            private void Save()
            {
                SaveButton.Style.Se
                Title.Va
                Color.Fr
            }
            </script>
            """;
        var styleOffset = source.IndexOf("Style.Se", StringComparison.Ordinal) + "Style.Se".Length;
        var valueOffset = source.IndexOf("Title.Va", StringComparison.Ordinal) + "Title.Va".Length;
        var colorOffset = source.IndexOf("Color.Fr", StringComparison.Ordinal) + "Color.Fr".Length;

        var styleItems = TemplateCompletionService.GetItems(source, styleOffset, "Editing.sqx");
        var valueItems = TemplateCompletionService.GetItems(source, valueOffset, "Editing.sqx");
        var colorItems = TemplateCompletionService.GetItems(source, colorOffset, "Editing.sqx");

        Assert.Contains(styleItems, item => item.Label == "Set" && item.Kind == 2);
        Assert.Contains(valueItems, item => item.Label == "Value" && item.Kind == 10);
        Assert.Contains(colorItems, item => item.Label == "FromRgb" && item.Kind == 2);
    }

    [Fact]
    public void CompletesLifecycleOverridesOnlyAtComponentScope()
    {
        const string source = "<template><View /></template><script>OnA</script>";
        var offset = source.IndexOf("</script>", StringComparison.Ordinal);

        var items = TemplateCompletionService.GetItems(source, offset, "Editing.sqx");

        var attached = Assert.Single(items, item => item.Label == "OnAttachedCore");
        Assert.Contains("protected override void OnAttachedCore()", attached.InsertText, StringComparison.Ordinal);
        Assert.Contains("base.OnAttachedCore();", attached.InsertText, StringComparison.Ordinal);

        const string methodSource = "<template><View /></template><script>private void Save() { OnA }</script>";
        var methodOffset = methodSource.IndexOf("OnA }", StringComparison.Ordinal) + "OnA".Length;
        var methodItems = TemplateCompletionService.GetItems(methodSource, methodOffset, "Editing.sqx");
        Assert.DoesNotContain(methodItems, item => item.Label == "OnAttachedCore");
    }

    [Fact]
    public void CompletesCSharpAttributes()
    {
        const string source = "<template><View /></template><script>[</script>";
        var offset = source.IndexOf("</script>", StringComparison.Ordinal);

        var context = TemplateCompletionService.GetContext(source, offset, "Editing.sqx");
        var items = TemplateCompletionService.GetItems(context, source);

        Assert.Equal(TemplateCompletionKind.ScriptAttribute, context.Kind);
        Assert.Contains(items, item => item.Label == "Prop" && item.Kind == 7);
        Assert.Contains(items, item => item.Label == "SlotContract" && item.Kind == 7);
        Assert.Contains(items, item => item.Label == "SqxDirective" && item.Kind == 7);
    }

    [Theory]
    [InlineData("<template><View /></template><script>[Prop(Re</script>", "Required")]
    [InlineData("<template><View /></template><script>[SqxDirective(Al</script>", "Aliases")]
    public void CompletesSquareAttributeNamedArguments(string source, string expected)
    {
        var offset = source.IndexOf("</script>", StringComparison.Ordinal);

        var context = TemplateCompletionService.GetContext(source, offset, "Editing.sqx");
        var items = TemplateCompletionService.GetItems(context, source);

        Assert.Equal(TemplateCompletionKind.ScriptAttributeArgument, context.Kind);
        Assert.Contains(items, item => item.Label == expected && item.Kind == 10);
    }

    [Fact]
    public void IndexerExpressionIsNotTreatedAsAnAttribute()
    {
        const string source = "<template><View /></template><script>private void Save() { var items = new List<int>(); var value = items[In }</script>";
        var offset = source.IndexOf("In }", StringComparison.Ordinal) + "In".Length;

        var context = TemplateCompletionService.GetContext(source, offset, "Editing.sqx");

        Assert.Equal(TemplateCompletionKind.ScriptGeneral, context.Kind);
    }

    [Theory]
    [InlineData("<template><View /></template><script>private string Text = \"But\";</script>")]
    [InlineData("<template><View /></template><script>// But\nprivate int Count;</script>")]
    public void DoesNotCompleteInsideScriptStringsOrComments(string source)
    {
        var marker = source.IndexOf("But", StringComparison.Ordinal) + "But".Length;

        var context = TemplateCompletionService.GetContext(source, marker, "Editing.sqx");

        Assert.Equal(TemplateCompletionKind.None, context.Kind);
    }
}
