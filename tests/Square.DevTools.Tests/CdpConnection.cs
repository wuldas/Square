using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Square.DevTools.Tests;

internal sealed class CdpConnection : IDisposable
{
    private readonly List<JsonDocument> _messages = [];
    private int _nextRequestId;
    public ClientWebSocket Socket { get; } = new();

    public async Task ConnectAsync(DevToolsServer server, string? origin = null)
    {
        if (origin != null) Socket.Options.SetRequestHeader("Origin", origin);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Socket.ConnectAsync(new Uri($"ws://127.0.0.1:{server.Port}/devtools/page/{server.TargetId}"), deadline.Token);
    }

    public async Task SendAsync(string message)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, deadline.Token);
    }

    public async Task<JsonDocument> RequestAsync(string method, object? parameters = null)
    {
        var id = ++_nextRequestId;
        await SendAsync(JsonSerializer.Serialize(new { id, method, @params = parameters ?? new { } }));
        return await ReceiveResponseAsync(id);
    }

    public Task<JsonDocument> ReceiveResponseAsync(int id) => ReceiveAsync(root =>
        root.TryGetProperty("id", out var value) && value.GetInt32() == id);

    public Task<JsonDocument> ReceiveEventAsync(string method) => ReceiveAsync(root =>
        root.TryGetProperty("method", out var value) && value.GetString() == method);

    public bool HasBufferedEvent(string method) => _messages.Any(message =>
        message.RootElement.TryGetProperty("method", out var value) && value.GetString() == method);

    public IReadOnlyList<JsonDocument> TakeBufferedEvents(string method)
    {
        var events = new List<JsonDocument>();
        for (var index = 0; index < _messages.Count;)
        {
            var message = _messages[index];
            if (message.RootElement.TryGetProperty("method", out var value) && value.GetString() == method)
            {
                events.Add(message);
                _messages.RemoveAt(index);
            }
            else index++;
        }
        return events;
    }

    private async Task<JsonDocument> ReceiveAsync(Func<JsonElement, bool> matches)
    {
        for (var i = 0; i < _messages.Count; i++)
        {
            if (!matches(_messages[i].RootElement)) continue;
            var buffered = _messages[i];
            _messages.RemoveAt(i);
            return buffered;
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var buffer = new byte[32 * 1024];
            using var stream = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await Socket.ReceiveAsync(buffer, deadline.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new InvalidOperationException($"CDP server closed the connection: {result.CloseStatusDescription}");
                stream.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);
            var document = JsonDocument.Parse(stream.ToArray());
            if (matches(document.RootElement)) return document;
            _messages.Add(document);
        }
    }

    public void Dispose()
    {
        Socket.Dispose();
        foreach (var message in _messages) message.Dispose();
    }
}
