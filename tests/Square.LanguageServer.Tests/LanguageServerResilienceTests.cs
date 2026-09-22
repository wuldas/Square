using Xunit;

namespace Square.LanguageServer.Tests;

/// <summary>
/// 服务端必须在畸形输入下存活：此前的实现会抛出未处理异常并以 0xE0434352 退出，
/// 编辑器侧表现为语言功能整体失效。
/// </summary>
public sealed class LanguageServerResilienceTests
{
    [Fact]
    public async Task MissingContentLengthHeaderIsReportedAndServerStaysAlive()
    {
        using var session = LanguageServerSession.Start();
        await session.SendRawAsync("X-Square-Probe: 1\r\n\r\n");

        await session.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        var response = await session.ReadResponseAsync(1);
        Assert.Contains("\"capabilities\"", response, StringComparison.Ordinal);
        Assert.Contains(session.ReceivedMessages, message => message.Contains("\"code\":-32700", StringComparison.Ordinal));
        Assert.False(session.Process.HasExited);
        Assert.Equal(0, await session.ShutdownAsync());
    }

    [Fact]
    public async Task InvalidJsonBodyIsReportedAndServerStaysAlive()
    {
        using var session = LanguageServerSession.Start();
        await session.SendFramedRawAsync("{not json");

        await session.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        var response = await session.ReadResponseAsync(1);
        Assert.Contains("\"capabilities\"", response, StringComparison.Ordinal);
        Assert.Contains(session.ReceivedMessages, message => message.Contains("\"code\":-32700", StringComparison.Ordinal));
        Assert.Equal(0, await session.ShutdownAsync());
    }

    [Fact]
    public async Task ClientResponsesAreIgnoredInsteadOfRejected()
    {
        using var session = LanguageServerSession.Start();
        await session.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        _ = await session.ReadResponseAsync(1);

        // 服务端自己的 client/registerCapability 收到客户端响应时不得回 -32601。
        await session.SendAsync("""{"jsonrpc":"2.0","id":"square-file-watchers","result":null}""");
        await session.SendAsync("""{"jsonrpc":"2.0","method":"$/cancelRequest","params":{"id":4242}}""");
        await session.SendAsync("""{"jsonrpc":"2.0","id":2,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"file:///C:/Square/Absent.sqx"}}}""");

        var response = await session.ReadResponseAsync(2);
        Assert.Contains("\"result\"", response, StringComparison.Ordinal);
        Assert.DoesNotContain(session.ReceivedMessages, message => message.Contains("-32601", StringComparison.Ordinal));
        Assert.Equal(0, await session.ShutdownAsync());
    }

    [Fact]
    public async Task WatchedFileAndConfigurationNotificationsKeepSessionUsable()
    {
        using var session = LanguageServerSession.Start();
        await session.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        _ = await session.ReadResponseAsync(1);

        const string uri = "file:///C:/Square/Watched.sqx";
        await session.OpenAsync(uri, "<template><Button /></template>");

        await session.SendAsync("""{"jsonrpc":"2.0","method":"workspace/didChangeWatchedFiles","params":{"changes":[{"uri":"file:///C:/Square/Watched.sqx","type":2},{"uri":"file:///C:/Square/Other.sqx","type":3},{"uri":"file:///C:/Square/Proj.csproj","type":1}]}}""");
        await session.SendAsync("""{"jsonrpc":"2.0","method":"workspace/didChangeConfiguration","params":{"settings":{}}}""");

        var response = await session.RequestAsync(
            "textDocument/documentSymbol",
            "{\"textDocument\":{\"uri\":\"" + uri + "\"}}");
        Assert.Contains("\"result\"", response, StringComparison.Ordinal);
        Assert.False(session.Process.HasExited);
        Assert.Equal(0, await session.ShutdownAsync());
    }

    /// <summary>
    /// 无工程拥有的模板文件必须快速返回（此前会 MSBuild 逐个加载工作区内所有工程，
    /// 本仓库 58 个工程时 didOpen 超过 180 秒无响应），且补全必须以 isIncomplete
    /// 告知客户端结果不完整，否则工程级组件会被静默丢弃。
    /// </summary>
    [Fact]
    public async Task DocumentWithoutOwningProjectResolvesQuickly()
    {
        using var session = LanguageServerSession.Start();
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var uri = new Uri(Path.Combine(repoRoot, ".lsp-no-owner", "NoOwnership.sqx")).AbsoluteUri;
        await session.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"rootUri\":\"" +
                                new Uri(repoRoot).AbsoluteUri + "\"}}");
        _ = await session.ReadResponseAsync(1);

        var diagnostics = await session
            .OpenAndReadDiagnosticsAsync(uri, "<template><Button /></template>", TimeSpan.FromSeconds(60));

        Assert.Contains("\"diagnostics\"", diagnostics, StringComparison.Ordinal);
        var completion = await session.RequestAsync(
            "textDocument/completion",
            "{\"textDocument\":{\"uri\":\"" + uri + "\"},\"position\":{\"line\":0,\"character\":10}}");
        Assert.Contains("\"isIncomplete\":true", completion, StringComparison.Ordinal);
        Assert.Equal(0, await session.ShutdownAsync());
    }
}
