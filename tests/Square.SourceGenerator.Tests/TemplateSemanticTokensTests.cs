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

    [Fact]
    public void SqvDirectiveTokenUsesOriginalNameRange()
    {
        const string source = "<template><Button @click.stop=\"OnSave\" /></template>";

        var data = TemplateSemanticTokens.Encode(source, "Event.sqv");
        var character = 0;
        var found = false;
        for (var index = 0; index < data.Count; index += 5)
        {
            Assert.Equal(0, data[index]);
            character += data[index + 1];
            if (data[index + 3] != 4) continue;
            found = character == source.IndexOf("@click.stop", StringComparison.Ordinal) &&
                    data[index + 2] == "@click.stop".Length;
        }

        Assert.True(found);
    }
}
