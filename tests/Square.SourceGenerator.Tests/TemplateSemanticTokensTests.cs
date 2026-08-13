using Square.Compiler.LanguageServices;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class TemplateSemanticTokensTests
{
    [Fact]
    public void EncodesBuiltInControlAndEventFromSyntaxTree()
    {
        const string source = "<template><Button onClick={OnSave} /></template>";

        var data = TemplateSemanticTokens.Encode(source, "Hover.sqx");

        Assert.NotEmpty(data);
        Assert.Equal(0, data.Count % 5);
    }

    [Fact]
    public void EncodesControlFlowTagAsTypeToken()
    {
        const string source = "<template><Show when={Visible}><Text /></Show></template>";

        var data = TemplateSemanticTokens.Encode(source, "Show.sqx");

        Assert.NotEmpty(data);
        Assert.Contains(1, data.Where((_, index) => index % 5 == 3));
    }
}
