using System.Text.Json;
using Xunit;

namespace Square.LanguageServer.Tests;

/// <summary>
/// 客户端能力协商：positionEncoding、hover.contentFormat、definition.linkSupport，
/// 以及 hover 的 markdown 形态（真换行 + 标签名转义）与 sqx 事件/属性覆盖。
/// </summary>
public sealed class LanguageServerCapabilitiesTests
{
    [Fact]
    public async Task ReportsPositionEncodingAndHonoursPlaintextHoverFormat()
    {
        using var session = LanguageServerSession.Start();
        await session.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{"general":{"positionEncodings":["utf-8"]},"textDocument":{"hover":{"contentFormat":["plaintext"]}}}}}""");
        var initialize = await session.ReadResponseAsync(1);
        Assert.Contains("\"positionEncoding\":\"utf-16\"", initialize, StringComparison.Ordinal);

        const string uri = "file:///C:/Square/Capabilities.sqx";
        await session.OpenAsync(uri, "<template><Button /></template>");
        var hover = await session.RequestAsync(
            "textDocument/hover",
            "{\"textDocument\":{\"uri\":\"" + uri + "\"},\"position\":{\"line\":0,\"character\":12}}");

        Assert.Contains("\"kind\":\"plaintext\"", hover, StringComparison.Ordinal);
        Assert.Contains("Square.Controls.Button", hover, StringComparison.Ordinal);
        Assert.DoesNotContain("markdown", hover, StringComparison.Ordinal);
        Assert.Equal(0, await session.ShutdownAsync());
    }

    [Fact]
    public async Task HoverMarkdownUsesRealNewlinesAndEscapesTagName()
    {
        using var session = LanguageServerSession.Start();
        await session.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        _ = await session.ReadResponseAsync(1);

        const string uri = "file:///C:/Square/Hover.sqx";
        await session.OpenAsync(uri, "<template><Button /></template>");
        var hover = await session.RequestAsync(
            "textDocument/hover",
            "{\"textDocument\":{\"uri\":\"" + uri + "\"},\"position\":{\"line\":0,\"character\":12}}");

        // 解析 MarkupContent 取值后断言，避免受 JSON 的 HTML 字符转义（< → \u003C）影响。
        var markdown = ReadHoverValue(hover);
        Assert.Contains("**`<Button>`**\n\n", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("\\n", markdown, StringComparison.Ordinal);
        Assert.Equal(0, await session.ShutdownAsync());
    }

    private static string ReadHoverValue(string response)
    {
        using var document = JsonDocument.Parse(response);
        return document.RootElement.GetProperty("result").GetProperty("contents").GetProperty("value").GetString()!;
    }

    [Fact]
    public async Task HoverDescribesSqxEventAndAttributeNames()
    {
        using var session = LanguageServerSession.Start();
        await session.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        _ = await session.ReadResponseAsync(1);

        const string uri = "file:///C:/Square/SqxHover.sqx";
        const string text = "<template>\n  <Button onClick={OnSave} text=\"Go\">Go</Button>\n</template>\n<script>\nprivate void OnSave(Event e) { }\n</script>\n";
        await session.OpenAsync(uri, text);

        // 光标落在属性名 onClick 上（sqx 的真实事件语法，修复前返回 null）。
        var onClick = await session.RequestAsync(
            "textDocument/hover",
            "{\"textDocument\":{\"uri\":\"" + uri + "\"},\"position\":{\"line\":1,\"character\":13}}");
        Assert.Contains("Square event", onClick, StringComparison.Ordinal);

        // 属性名 text 命中内置属性别名 TextContent。
        var textHover = await session.RequestAsync(
            "textDocument/hover",
            "{\"textDocument\":{\"uri\":\"" + uri + "\"},\"position\":{\"line\":1,\"character\":28}}");
        Assert.Contains("TextContent", textHover, StringComparison.Ordinal);

        Assert.Equal(0, await session.ShutdownAsync());
    }

    [Fact]
    public async Task DefinitionHonoursLocationLinkSupport()
    {
        using var linkSession = LanguageServerSession.Start();
        await linkSession.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{"textDocument":{"definition":{"linkSupport":true}}}}}""");
        _ = await linkSession.ReadResponseAsync(1);
        var linked = await DefinitionAsync(linkSession);
        Assert.Contains("targetUri", linked, StringComparison.Ordinal);
        Assert.Contains("targetSelectionRange", linked, StringComparison.Ordinal);
        Assert.Equal(0, await linkSession.ShutdownAsync());

        using var locationSession = LanguageServerSession.Start();
        await locationSession.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{"textDocument":{"definition":{"linkSupport":false}}}}}""");
        _ = await locationSession.ReadResponseAsync(1);
        var location = await DefinitionAsync(locationSession);
        Assert.Contains("\"uri\"", location, StringComparison.Ordinal);
        Assert.DoesNotContain("targetUri", location, StringComparison.Ordinal);
        Assert.Equal(0, await locationSession.ShutdownAsync());
    }

    /// <summary>补全浮层里的文档显示：标签项给出类型与来源，成员项给出类型与所属组件。</summary>
    [Fact]
    public async Task CompletionItemsCarryMarkdownDocumentation()
    {
        using var session = LanguageServerSession.Start();
        await session.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        _ = await session.ReadResponseAsync(1);

        const string uri = "file:///C:/Square/Docs.sqx";
        await session.OpenAsync(uri, "<template><But</template>");
        var completion = await session.RequestAsync(
            "textDocument/completion",
            "{\"textDocument\":{\"uri\":\"" + uri + "\"},\"position\":{\"line\":0,\"character\":14}}");

        var button = ItemDocumentation(completion, "Button");
        Assert.Equal("markdown", button.Kind);
        Assert.Contains("Square.Controls.Button", button.Value, StringComparison.Ordinal);
        Assert.Contains("built-in", button.Value, StringComparison.Ordinal);
        Assert.Equal(0, await session.ShutdownAsync());
    }

    [Fact]
    public async Task CompletionMemberDocumentationNamesDeclaringComponent()
    {
        using var session = LanguageServerSession.Start();
        await session.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        _ = await session.ReadResponseAsync(1);

        const string uri = "file:///C:/Square/Members.sqx";
        await session.OpenAsync(uri, "<template><Button </template>");
        var completion = await session.RequestAsync(
            "textDocument/completion",
            "{\"textDocument\":{\"uri\":\"" + uri + "\"},\"position\":{\"line\":0,\"character\":18}}");

        using var document = JsonDocument.Parse(completion);
        var documented = document.RootElement.GetProperty("result").GetProperty("items").EnumerateArray()
            .Where(item => item.TryGetProperty("documentation", out _))
            .Select(item => item.GetProperty("documentation"))
            .ToArray();

        Assert.NotEmpty(documented);
        Assert.All(documented, entry => Assert.Equal("markdown", entry.GetProperty("kind").GetString()));
        Assert.Contains(documented, entry =>
            entry.GetProperty("value").GetString()!.Contains("Available on `<Button>`", StringComparison.Ordinal));
        Assert.Equal(0, await session.ShutdownAsync());
    }

    private static (string Kind, string Value) ItemDocumentation(string response, string label)
    {
        using var document = JsonDocument.Parse(response);
        var item = document.RootElement.GetProperty("result").GetProperty("items").EnumerateArray()
            .First(candidate => candidate.GetProperty("label").GetString() == label);
        var documentation = item.GetProperty("documentation");
        return (documentation.GetProperty("kind").GetString()!, documentation.GetProperty("value").GetString()!);
    }

    private static async Task<string> DefinitionAsync(LanguageServerSession session)
    {
        const string cardUri = "file:///C:/Square/Card.sqx";
        const string pageUri = "file:///C:/Square/Page.sqx";
        await session.OpenAsync(cardUri, "<template><Text /></template>");
        await session.OpenAsync(pageUri, "<template><Card /></template>");
        return await session.RequestAsync(
            "textDocument/definition",
            "{\"textDocument\":{\"uri\":\"" + pageUri + "\"},\"position\":{\"line\":0,\"character\":12}}");
    }
}
