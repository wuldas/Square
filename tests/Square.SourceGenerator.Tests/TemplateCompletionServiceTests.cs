using Square.Compiler.LanguageServices;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class TemplateCompletionServiceTests
{
    [Fact]
    public void IncompleteTagUsesSyntaxTreeForControlFlowCompletion()
    {
        const string source = "<template><Sh";

        var items = TemplateCompletionService.GetItems(source, source.Length, "Editing.sqx");

        Assert.Contains(items, item => item.Label == "Show" && item.Detail == "Show");
        Assert.DoesNotContain(items, item => item.Label == "Button");
    }

    [Fact]
    public void IncompleteAttributeUsesOwningElementFromSyntaxTree()
    {
        const string source = "<template><Button te";

        var context = TemplateCompletionService.GetContext(source, source.Length, "Editing.sqx");

        Assert.Equal(TemplateCompletionKind.Attribute, context.Kind);
        Assert.Equal("Button", context.TagName);
        Assert.Equal("te", context.Prefix);
        Assert.Contains(
            TemplateCompletionService.GetItems(context, source),
            item => item.Label == "text");
    }

    [Fact]
    public void VueDirectivePrefixCompletesSupportedDirectives()
    {
        const string source = "<template><Text v-";

        var items = TemplateCompletionService.GetItems(source, source.Length, "Editing.sqv");

        Assert.Contains(items, item => item.Label == "v-if" && item.Detail == "Vue directive");
        Assert.Contains(items, item => item.Label == "v-for");
        Assert.DoesNotContain(items, item => item.Label == "v-html");
    }
}
