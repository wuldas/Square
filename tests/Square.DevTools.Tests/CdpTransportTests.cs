using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Square.Hosting;
using Xunit;

namespace Square.DevTools.Tests;

public sealed class CdpTransportTests
{
    [Fact]
    public async Task InvalidJsonAndInvalidEnvelopeDoNotEndConnection()
    {
        await using var server = DevToolsServer.Start(new AppWindow("transport"), new() { AllowChromeInspect = true });
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);
        await connection.SendAsync("{broken");
        using var malformed = await connection.ReceiveResponseAsync(0);
        Assert.Equal(-32700, malformed.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal("Invalid JSON.", malformed.RootElement.GetProperty("error").GetProperty("message").GetString());
        await connection.SendAsync("{\"id\":\"not-a-number\",\"method\":7}");
        using var envelope = await connection.ReceiveResponseAsync(0);
        Assert.Equal(-32600, envelope.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        using var valid = await connection.RequestAsync("Page.enable");
        Assert.True(valid.RootElement.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task FragmentedUtf8UnknownCommandAndPeerCloseRemainValid()
    {
        await using var server = DevToolsServer.Start(new AppWindow("transport"), new() { AllowChromeInspect = true });
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server, "devtools://devtools");
        var bytes = Encoding.UTF8.GetBytes("{\"id\":41,\"method\":\"missing.方法\"}");
        var split = Array.IndexOf(bytes, (byte)0xe6) + 1;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await connection.Socket.SendAsync(bytes.AsMemory(0, split), WebSocketMessageType.Text, false, deadline.Token);
        await connection.Socket.SendAsync(bytes.AsMemory(split), WebSocketMessageType.Text, true, deadline.Token);
        using var unknown = await connection.ReceiveResponseAsync(41);
        Assert.Equal(-32601, unknown.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Contains("方法", unknown.RootElement.GetProperty("error").GetProperty("message").GetString());
        using var valid = await connection.RequestAsync("Page.enable");
        Assert.True(valid.RootElement.TryGetProperty("result", out _));
        await connection.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", deadline.Token);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, connection.Socket.CloseStatus);
    }

    [Fact]
    public async Task BinaryMessagesCloseWithInvalidMessageType()
    {
        await using var server = DevToolsServer.Start(new AppWindow("transport"), new() { AllowChromeInspect = true });
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await connection.Socket.SendAsync(new byte[] { 1 }, WebSocketMessageType.Binary, true, deadline.Token);
        var result = await connection.Socket.ReceiveAsync(new byte[256], deadline.Token);
        Assert.Equal(WebSocketMessageType.Close, result.MessageType);
        Assert.Equal(WebSocketCloseStatus.InvalidMessageType, result.CloseStatus);
    }

    [Fact]
    public async Task ShutdownWithOpenConnectionFinishesWithoutPeerClose()
    {
        var window = new AppWindow("transport");
        var server = DevToolsServer.Start(window, new() { AllowChromeInspect = true });
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);
        using var valid = await connection.RequestAsync("Page.enable");
        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(server.BaseAddress + "/json/list"));
    }

    [Fact]
    public async Task WebOriginsAreRejectedAndRepeatedNativeConnectionsClose()
    {
        await using var server = DevToolsServer.Start(new AppWindow("transport"), new() { AllowChromeInspect = true });
        using (var forbidden = new CdpConnection())
        {
            var error = await Assert.ThrowsAsync<WebSocketException>(() => forbidden.ConnectAsync(server, "https://example.com"));
            Assert.Contains("403", error.Message);
        }
        for (var i = 0; i < 8; i++)
        {
            using var connection = new CdpConnection();
            await connection.ConnectAsync(server);
            using var response = await connection.RequestAsync("Page.enable");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await connection.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", deadline.Token);
            Assert.Equal(WebSocketCloseStatus.NormalClosure, connection.Socket.CloseStatus);
        }
    }
}
