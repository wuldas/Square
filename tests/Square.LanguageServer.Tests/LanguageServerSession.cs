using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Square.LanguageServer.Tests;

/// <summary>
/// LSP 会话：按字节分帧读写，并按 response id / notification method 分派消息。
/// </summary>
internal sealed class LanguageServerSession : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly Process _process;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly List<JsonDocument> _notifications = new();
    private readonly List<string> _received = new();
    private readonly Dictionary<int, JsonDocument> _responses = new();
    private int? _lastRequestId;
    private int _nextRequestId = 100;

    private LanguageServerSession(Process process)
    {
        _process = process;
        _input = process.StandardInput.BaseStream;
        _output = process.StandardOutput.BaseStream;
    }

    public Process Process => _process;

    /// <summary>截至当前收到的全部原始报文（含带 id 的请求/响应与通知），按到达顺序。</summary>
    public IReadOnlyList<string> ReceivedMessages => _received;

    public static LanguageServerSession Start()
    {
        var project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "Square.LanguageServer", "Square.LanguageServer.csproj"));
        var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                Arguments = "run --no-restore --project \"" + project + "\" --no-launch-profile",
                WorkingDirectory = Path.GetDirectoryName(project)!,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        Assert.True(process.Start());
        return new LanguageServerSession(process);
    }

    public async Task SendAsync(string json)
    {
        if (TryReadRequestId(json, out var id)) _lastRequestId = id;
        var payload = Encoding.UTF8.GetBytes(json);
        await _input.WriteAsync(Encoding.ASCII.GetBytes("Content-Length: " + payload.Length + "\r\n\r\n"));
        await _input.WriteAsync(payload);
        await _input.FlushAsync();
    }

    /// <summary>打开文档并等待其诊断通知（诊断去抖 120ms 后到达）。</summary>
    public Task OpenAsync(string uri, string text, string languageId = "sqx") =>
        OpenAndReadDiagnosticsAsync(uri, text, TimeSpan.FromSeconds(30), languageId);

    /// <summary>打开文档并在给定预算内等待其诊断通知，用于耗时回归断言。</summary>
    public async Task<string> OpenAndReadDiagnosticsAsync(
        string uri,
        string text,
        TimeSpan budget,
        string languageId = "sqx")
    {
        var json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "textDocument/didOpen",
            @params = new { textDocument = new { uri, languageId, version = 1, text } }
        });
        await SendAsync(json);
        return await ReadNotificationAsync("textDocument/publishDiagnostics", uri).WaitAsync(budget);
    }

    /// <summary>发送请求并读取其响应原文；id 自动分配。</summary>
    public async Task<string> RequestAsync(string method, string parametersJson)
    {
        var id = _nextRequestId++;
        await SendAsync("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"" + method + "\",\"params\":" + parametersJson + "}");
        return await ReadResponseAsync(id);
    }

    /// <summary>按原样写入字节（不补 Content-Length 头），用于构造畸形 framing 的健壮性测试。</summary>
    public async Task SendRawAsync(string raw)
    {
        await _input.WriteAsync(Encoding.UTF8.GetBytes(raw));
        await _input.FlushAsync();
    }

    /// <summary>写入带正确 Content-Length 头但正文不受约束的报文，用于构造非法 JSON 的健壮性测试。</summary>
    public async Task SendFramedRawAsync(string body)
    {
        var payload = Encoding.UTF8.GetBytes(body);
        await _input.WriteAsync(Encoding.ASCII.GetBytes("Content-Length: " + payload.Length + "\r\n\r\n"));
        await _input.WriteAsync(payload);
        await _input.FlushAsync();
    }

    /// <summary>最近一次已发送请求的响应；期间到达的通知与其他响应会被跳过并暂存。</summary>
    public Task<string> ReadResponseAsync()
    {
        Assert.True(_lastRequestId.HasValue, "No request has been sent.");
        return ReadResponseAsync(_lastRequestId.Value);
    }

    /// <summary>指定 request id 的响应；期间到达的通知与其他响应会被跳过并暂存。</summary>
    public async Task<string> ReadResponseAsync(int id)
    {
        if (_responses.Remove(id, out var cached))
        {
            var cachedText = cached.RootElement.GetRawText();
            cached.Dispose();
            return cachedText;
        }
        while (true)
        {
            var message = await ReadMessageAsync();
            if (!TryReadId(message.RootElement, out var received))
            {
                _notifications.Add(message);
                continue;
            }
            if (received == id)
            {
                var text = message.RootElement.GetRawText();
                message.Dispose();
                return text;
            }
            _responses[received] = message;
        }
    }

    public async Task<string> ReadNotificationAsync(string method, string? uri = null)
    {
        for (var index = 0; index < _notifications.Count; index++)
        {
            if (!MatchesNotification(_notifications[index].RootElement, method, uri)) continue;
            var matched = _notifications[index];
            _notifications.RemoveAt(index);
            var text = matched.RootElement.GetRawText();
            matched.Dispose();
            return text;
        }
        while (true)
        {
            var message = await ReadMessageAsync();
            if (TryReadId(message.RootElement, out var id))
            {
                _responses[id] = message;
                continue;
            }
            if (MatchesNotification(message.RootElement, method, uri))
            {
                var text = message.RootElement.GetRawText();
                message.Dispose();
                return text;
            }
            _notifications.Add(message);
        }
    }

    /// <summary>发送 shutdown/exit 并返回进程退出码。</summary>
    public async Task<int> ShutdownAsync()
    {
        await SendAsync("""{"jsonrpc":"2.0","id":-2147483647,"method":"shutdown","params":null}""");
        _ = await ReadResponseAsync();
        await SendAsync("""{"jsonrpc":"2.0","method":"exit","params":null}""");
        await WaitForExitAsync();
        return _process.ExitCode;
    }

    public Task WaitForExitAsync() => _process.WaitForExitAsync().WaitAsync(Timeout);

    public void Dispose()
    {
        foreach (var document in _notifications) document.Dispose();
        foreach (var document in _responses.Values) document.Dispose();
        _notifications.Clear();
        _responses.Clear();
        if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        _process.Dispose();
    }

    private static bool MatchesNotification(JsonElement root, string method, string? uri)
    {
        if (!root.TryGetProperty("method", out var methodElement) ||
            !string.Equals(methodElement.GetString(), method, StringComparison.Ordinal)) return false;
        if (uri == null) return true;
        if (!root.TryGetProperty("params", out var parameters)) return false;
        if (parameters.TryGetProperty("uri", out var directUri))
            return string.Equals(directUri.GetString(), uri, StringComparison.Ordinal);
        return parameters.TryGetProperty("textDocument", out var textDocument) &&
               textDocument.TryGetProperty("uri", out var nestedUri) &&
               string.Equals(nestedUri.GetString(), uri, StringComparison.Ordinal);
    }

    private static bool TryReadRequestId(string json, out int id)
    {
        id = -1;
        using var document = JsonDocument.Parse(json);
        return TryReadId(document.RootElement, out id);
    }

    private static bool TryReadId(JsonElement root, out int id)
    {
        id = -1;
        return root.TryGetProperty("id", out var element) &&
               element.ValueKind == JsonValueKind.Number &&
               element.TryGetInt32(out id);
    }

    private async Task<JsonDocument> ReadMessageAsync()
    {
        var length = -1;
        while (true)
        {
            var header = await ReadAsciiLineAsync();
            Assert.NotNull(header);
            if (header!.Length == 0) break;
            if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                length = int.Parse(header["Content-Length:".Length..].Trim());
        }
        Assert.True(length >= 0, "Missing Content-Length header.");
        var buffer = new byte[length];
        await ReadExactlyAsync(buffer);
        var document = JsonDocument.Parse(buffer);
        _received.Add(document.RootElement.GetRawText());
        return document;
    }

    private async Task<string?> ReadAsciiLineAsync()
    {
        var bytes = new List<byte>(32);
        while (true)
        {
            var value = await ReadByteAsync();
            if (value < 0) return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
            if (value == '\n') return Encoding.ASCII.GetString(bytes.ToArray());
            if (value == '\r') continue;
            bytes.Add((byte)value);
        }
    }

    private async Task<int> ReadByteAsync()
    {
        var buffer = new byte[1];
        var read = await _output.ReadAsync(buffer, 0, 1).WaitAsync(Timeout);
        return read == 0 ? -1 : buffer[0];
    }

    private async Task ReadExactlyAsync(byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await _output.ReadAsync(buffer, offset, buffer.Length - offset).WaitAsync(Timeout);
            Assert.True(read > 0, "Unexpected end of server output.");
            offset += read;
        }
    }
}
