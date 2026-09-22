using System.Text.Json;
using Xunit;

namespace Square.LanguageServer.Tests;

/// <summary>
/// 语义 token 此前没有任何测试覆盖。这里同时钉住协议不变式（已排序、不重叠、不跨行、下标合法）
/// 与关键 token 的类型/修饰符。
/// </summary>
public sealed class LanguageServerSemanticTokensTests
{
    private const string Uri = "file:///C:/Square/Tokens.sqx";

    private static readonly string Text = string.Join('\n', new[]
    {
        "<template>",
        "  <Button Title=\"ok\" />",
        "  <Show when={Visible}>",
        "    <Text text=\"Ready\" />",
        "  </Show>",
        "</template>",
        "<script>",
        "private string Title = \"x\";",
        "private void Save(Event e) { var total = 1; }",
        "</script>",
        ""
    });

    [Fact]
    public async Task SemanticTokensSatisfyProtocolInvariants()
    {
        using var session = LanguageServerSession.Start();
        await session.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        _ = await session.ReadResponseAsync(1);
        await session.OpenAsync(Uri, Text);
        var response = await session.RequestAsync(
            "textDocument/semanticTokens/full",
            "{\"textDocument\":{\"uri\":\"" + Uri + "\"}}");
        Assert.Equal(0, await session.ShutdownAsync());

        var tokens = Decode(response);
        Assert.NotEmpty(tokens);
        var lines = Text.Split('\n');
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            Assert.InRange(token.Type, 0, 8);
            Assert.InRange(token.Modifiers, 0, 3);
            Assert.InRange(token.Line, 0, lines.Length - 1);
            Assert.True(token.Character + token.Length <= lines[token.Line].Length,
                $"token {index}（{token.Text}）跨行尾：{Describe(token)}");
            if (index == 0) continue;
            var previous = tokens[index - 1];
            Assert.False(token.Line < previous.Line ||
                         (token.Line == previous.Line && token.Character < previous.Character),
                $"token {index}（{token.Text}）未按位置排序：{Describe(previous)} -> {Describe(token)}");
            Assert.False(token.Line == previous.Line && token.Character < previous.Character + previous.Length,
                $"token {index}（{token.Text}）与前一个 token 重叠：{Describe(previous)} -> {Describe(token)}");
        }

        Assert.Contains(tokens, token => token.Text == "Button" && token.Type == 0);
        Assert.Contains(tokens, token => token.Text == "Show" && token.Type == 1);
        Assert.Contains(tokens, token => token.Text == "Save" && token.Type == 5);
        Assert.Contains(tokens, token => token.Text == "Title" && token.Type == 6 && token.Modifiers == 1);
    }

    private static string Describe(Token token) =>
        token.Text + "@" + token.Line + ":" + token.Character + "+" + token.Length;

    private static List<Token> Decode(string response)
    {
        using var document = JsonDocument.Parse(response);
        var data = document.RootElement.GetProperty("result").GetProperty("data");
        var lines = Text.Split('\n');
        var tokens = new List<Token>();
        var line = 0;
        var character = 0;
        for (var index = 0; index + 4 < data.GetArrayLength(); index += 5)
        {
            var deltaLine = data[index].GetInt32();
            var deltaStart = data[index + 1].GetInt32();
            var length = data[index + 2].GetInt32();
            line += deltaLine;
            character = deltaLine == 0 ? character + deltaStart : deltaStart;
            var lineText = line >= 0 && line < lines.Length ? lines[line] : string.Empty;
            var text = character >= 0 && character + length <= lineText.Length
                ? lineText.Substring(character, length)
                : string.Empty;
            tokens.Add(new Token(line, character, length, data[index + 3].GetInt32(), data[index + 4].GetInt32(), text));
        }
        return tokens;
    }

    private sealed record Token(int Line, int Character, int Length, int Type, int Modifiers, string Text);
}
