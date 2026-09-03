using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Square.Graphics;
using Square.Graphics.Codecs;
using Square.Hosting;
using Square.Platform;

namespace Square.DevTools;

public sealed class DevToolsServer : IAsyncDisposable, IDisposable
{
    public const string TokenHeader = "X-Square-DevTools-Token";

    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _shutdown = new();
    private Task _acceptLoop;
    private int _disposed;

    private DevToolsServer(HttpListener listener, Task acceptLoop, string accessToken, int port, string targetId)
    {
        _listener = listener;
        _acceptLoop = acceptLoop;
        AccessToken = accessToken;
        Port = port;
        TargetId = targetId;
    }

    public string AccessToken { get; }
    public int Port { get; }
    public string TargetId { get; }
    public string BaseAddress => $"http://127.0.0.1:{Port}";
    internal string CdpWebSocketAddress => $"ws://127.0.0.1:{Port}/devtools/page/{TargetId}";

    public static DevToolsServer Start(AppWindow window, DevToolsOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        options ??= new DevToolsOptions();
        if (options.Port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(options.Port));

        var token = string.IsNullOrWhiteSpace(options.AccessToken)
            ? Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()
            : options.AccessToken;
        var listener = StartListener(options.Port, out var port);

        var server = new DevToolsServer(listener, Task.CompletedTask, token, port, Guid.NewGuid().ToString("N"));
        server._acceptLoop = Task.Run(() => server.AcceptLoopAsync(window, options));
        return server;
    }

    private async Task AcceptLoopAsync(AppWindow window, DevToolsOptions options)
    {
        while (!_shutdown.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (HttpListenerException) when (_shutdown.IsCancellationRequested)
            {
                break;
            }
            catch (InvalidOperationException) when (_shutdown.IsCancellationRequested)
            {
                break;
            }

            _ = Task.Run(() => HandleRequestAsync(context, window, options));
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, AppWindow window, DevToolsOptions options)
    {
        try
        {
            var method = context.Request.HttpMethod;
            var path = context.Request.Url?.AbsolutePath ?? "/";
            if (IsCdpPath(path))
            {
                if (!options.AllowChromeInspect)
                {
                    await WriteJsonAsync(context.Response, StatusCodes.NotFound, "{\"error\":\"not_found\"}");
                    return;
                }

                await HandleCdpRequestAsync(context, window, method, path);
                return;
            }

            if (!IsAuthorized(context.Request, AccessToken))
            {
                await WriteJsonAsync(context.Response, StatusCodes.Unauthorized, "{\"error\":\"unauthorized\"}");
                return;
            }

            if (method == "GET" && path == "/api/v1/health")
            {
                var json = $"{{\"status\":\"ok\",\"processId\":{Environment.ProcessId},\"port\":{Port}," +
                           $"\"baseAddress\":\"{BaseAddress}\",\"inputInjection\":{Bool(options.AllowInputInjection)}," +
                           $"\"memoryDiagnostics\":{Bool(options.AllowMemoryDiagnostics)}," +
                           $"\"chromeInspect\":{Bool(options.AllowChromeInspect)}}}";
                await WriteJsonAsync(context.Response, StatusCodes.Ok, json);
                return;
            }

            if (method == "GET" && path == "/api/v1/memory")
            {
                if (!options.AllowMemoryDiagnostics) { await WriteJsonAsync(context.Response, StatusCodes.Forbidden, "{}"); return; }
                var snapshot = MemorySnapshotCollector.Capture();
                await WriteJsonAsync(context.Response, StatusCodes.Ok, MemorySnapshotJson.Serialize(snapshot));
                return;
            }

            if (method == "GET" && path == "/api/v1/screenshot")
            {
                using var bitmap = await window.CaptureRendererBitmapAsync();
                using var stream = new MemoryStream();
                BitmapPngEncoder.Save(bitmap, stream);
                await WriteBytesAsync(context.Response, StatusCodes.Ok, stream.ToArray(), "image/png", "square-screenshot.png");
                return;
            }

            if (method == "POST" && path == "/api/v1/input/pointer")
            {
                if (!options.AllowInputInjection) { await WriteJsonAsync(context.Response, StatusCodes.Forbidden, "{}"); return; }
                var payload = await ReadJsonAsync(context.Request);
                var input = new DevToolsPointerInput(
                    new Point(ReadFloat(payload, "x"), ReadFloat(payload, "y")),
                    ReadEnum<MouseAction>(payload, "action"),
                    ReadModifiers(payload),
                    payload.TryGetProperty("button", out _)
                        ? ReadEnum<MouseButton>(payload, "button")
                        : MouseButton.Left);
                await window.InjectPointerAsync(input);
                await WriteNoContentAsync(context.Response);
                return;
            }

            if (method == "POST" && path == "/api/v1/input/key")
            {
                if (!options.AllowInputInjection) { await WriteJsonAsync(context.Response, StatusCodes.Forbidden, "{}"); return; }
                var payload = await ReadJsonAsync(context.Request);
                var input = new DevToolsKeyInput(
                    ReadInt(payload, "keyCode"),
                    ReadEnum<KeyAction>(payload, "action"),
                    ReadModifiers(payload));
                await window.InjectKeyAsync(input);
                await WriteNoContentAsync(context.Response);
                return;
            }

            if (method == "POST" && path == "/api/v1/input/text")
            {
                if (!options.AllowInputInjection) { await WriteJsonAsync(context.Response, StatusCodes.Forbidden, "{}"); return; }
                var payload = await ReadJsonAsync(context.Request);
                await window.InjectTextAsync(ReadString(payload, "text"));
                await WriteNoContentAsync(context.Response);
                return;
            }

            if (method == "POST" && path == "/api/v1/input/wheel")
            {
                if (!options.AllowInputInjection) { await WriteJsonAsync(context.Response, StatusCodes.Forbidden, "{}"); return; }
                var payload = await ReadJsonAsync(context.Request);
                var deltaX = ReadOptionalFloat(payload, "deltaX") ?? 0;
                // The legacy delta field uses the native wheel direction, where positive
                // means up; normalize it to the DOM/content direction at this boundary.
                var deltaY = ReadOptionalFloat(payload, "deltaY")
                    ?? -(ReadOptionalFloat(payload, "delta") ?? throw new BadHttpRequestException("'deltaY' or 'delta' must be a number."));
                var input = new DevToolsWheelInput(
                    new Point(ReadFloat(payload, "x"), ReadFloat(payload, "y")),
                    deltaX,
                    deltaY,
                    ReadOptionalBool(payload, "isPrecise"),
                    ReadOptionalBool(payload, "isInertial"),
                    ReadModifiers(payload));
                await window.InjectWheelAsync(input);
                await WriteNoContentAsync(context.Response);
                return;
            }

            if (method == "GET" && path == "/api/v1/inspect/tree")
            {
                if (!options.AllowInspector) { await WriteJsonAsync(context.Response, StatusCodes.Forbidden, "{}"); return; }
                var snapshot = await window.CaptureInspectionSnapshotAsync(options.IncludeSourcePaths, options.IncludeTextContent);
                await WriteJsonAsync(context.Response, StatusCodes.Ok, SerializeSnapshot(snapshot));
                return;
            }

            if (method == "GET" && path == "/api/v1/inspect/hit-test")
            {
                if (!options.AllowInspector) { await WriteJsonAsync(context.Response, StatusCodes.Forbidden, "{}"); return; }
                var x = ReadFloat(context.Request.QueryString, "x");
                var y = ReadFloat(context.Request.QueryString, "y");
                var result = await window.HitTestInspectionAsync(new Point(x, y), options.IncludeSourcePaths, options.IncludeTextContent);
                if (result == null) await WriteJsonAsync(context.Response, StatusCodes.NotFound, "{\"error\":\"not_found\"}");
                else await WriteJsonAsync(context.Response, StatusCodes.Ok, SerializeNode(result));
                return;
            }

            if (method == "GET" && TryReadElementRoute(path, out var elementId))
            {
                if (!options.AllowInspector) { await WriteJsonAsync(context.Response, StatusCodes.Forbidden, "{}"); return; }
                var result = await window.InspectElementAsync(elementId, options.IncludeSourcePaths, options.IncludeTextContent);
                if (result == null) await WriteJsonAsync(context.Response, StatusCodes.NotFound, "{\"error\":\"not_found\"}");
                else await WriteJsonAsync(context.Response, StatusCodes.Ok, SerializeNode(result));
                return;
            }

            await WriteJsonAsync(context.Response, StatusCodes.NotFound, "{\"error\":\"not_found\"}");
        }
        catch (BadHttpRequestException exception)
        {
            await WriteJsonAsync(context.Response, StatusCodes.BadRequest, $"{{\"error\":\"{Escape(exception.Message)}\"}}");
        }
        catch (Exception exception)
        {
            await WriteJsonAsync(context.Response, StatusCodes.InternalServerError, $"{{\"error\":\"{Escape(exception.Message)}\"}}");
        }
    }

    private async Task HandleCdpRequestAsync(HttpListenerContext context, AppWindow window, string method, string path)
    {

        if (method == "GET" && path is "/json" or "/json/list")
        {
            await WriteJsonAsync(context.Response, StatusCodes.Ok, CdpTargetDiscovery.SerializeList(this));
            return;
        }

        if (method == "GET" && path == "/json/version")
        {
            await WriteJsonAsync(context.Response, StatusCodes.Ok, CdpTargetDiscovery.SerializeVersion(this));
            return;
        }

        if (method == "GET" && path == "/json/protocol")
        {
            await WriteJsonAsync(context.Response, StatusCodes.Ok, CdpTargetDiscovery.SerializeProtocol());
            return;
        }

        if (method == "GET" && path == "/devtools/inspector.html")
        {
            context.Response.StatusCode = (int)HttpStatusCode.Redirect;
            context.Response.RedirectLocation = $"devtools://devtools/bundled/inspector.html?ws=127.0.0.1:{Port}/devtools/page/{TargetId}";
            context.Response.Close();
            return;
        }

        if (path == $"/devtools/page/{TargetId}")
        {
            if (!context.Request.IsWebSocketRequest)
            {
                await WriteJsonAsync(context.Response, StatusCodes.BadRequest, "{\"error\":\"websocket_required\"}");
                return;
            }

            var origin = context.Request.Headers["Origin"];
            if (!string.IsNullOrEmpty(origin) && !IsAllowedCdpOrigin(origin))
            {
                await WriteJsonAsync(context.Response, StatusCodes.Forbidden, "{\"error\":\"origin_forbidden\"}");
                return;
            }

            var websocket = await context.AcceptWebSocketAsync(null);
            await CdpSession.RunAsync(websocket.WebSocket, window, this, _shutdown.Token);
            return;
        }

        await WriteJsonAsync(context.Response, StatusCodes.NotFound, "{\"error\":\"not_found\"}");
    }

    private static bool IsCdpPath(string path) =>
        path is "/json" or "/json/list" or "/json/version" or "/json/protocol" or "/devtools/inspector.html" ||
        path.StartsWith("/devtools/page/", StringComparison.Ordinal);

    private static bool IsAllowedCdpOrigin(string origin) =>
        origin.Equals("devtools://devtools", StringComparison.OrdinalIgnoreCase) ||
        origin.Equals("chrome://inspect", StringComparison.OrdinalIgnoreCase) ||
        origin.Equals("chrome-devtools://devtools", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _shutdown.Cancel();
        if (_listener.IsListening) _listener.Stop();
        _listener.Close();
        try { _acceptLoop.GetAwaiter().GetResult(); } catch { }
        _shutdown.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static HttpListener StartListener(int requestedPort, out int port)
    {
        const int attempts = 5;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            port = requestedPort == 0 ? ReserveLoopbackPort() : requestedPort;
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                listener.Start();
                return listener;
            }
            catch (HttpListenerException) when (requestedPort == 0 && attempt + 1 < attempts)
            {
                listener.Close();
            }
        }

        throw new InvalidOperationException("Unable to bind the Square DevTools loopback listener.");
    }

    private static bool IsAuthorized(HttpListenerRequest request, string token)
    {
        var supplied = request.Headers[TokenHeader];
        if (string.IsNullOrEmpty(supplied)) return false;
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        var expectedBytes = Encoding.UTF8.GetBytes(token);
        return suppliedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpListenerRequest request)
    {
        using var document = await JsonDocument.ParseAsync(request.InputStream);
        return document.RootElement.Clone();
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, string json)
    {
        await WriteBytesAsync(response, statusCode, Encoding.UTF8.GetBytes(json), "application/json");
    }

    private static async Task WriteBytesAsync(
        HttpListenerResponse response, int statusCode, byte[] bytes, string contentType, string? fileName = null)
    {
        response.StatusCode = statusCode;
        response.ContentType = contentType;
        response.ContentLength64 = bytes.Length;
        if (!string.IsNullOrEmpty(fileName))
            response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static Task WriteNoContentAsync(HttpListenerResponse response)
    {
        response.StatusCode = StatusCodes.NoContent;
        response.ContentLength64 = 0;
        response.Close();
        return Task.CompletedTask;
    }

    private static bool TryReadElementRoute(string path, out int id)
    {
        const string prefix = "/api/v1/inspect/elements/";
        id = 0;
        return path.StartsWith(prefix, StringComparison.Ordinal) &&
               int.TryParse(path[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out id);
    }

    private static string SerializeSnapshot(ElementInspectionSnapshot snapshot)
        => $"{{\"root\":{SerializeNode(snapshot.Root)}}}";

    private static string SerializeNode(ElementInspectionNode node)
    {
        var builder = new StringBuilder();
        builder.Append('{');
        builder.Append($"\"id\":{node.Id},");
        builder.Append($"\"tagName\":\"{Escape(node.TagName)}\",");
        AppendNullableString(builder, "elementId", node.ElementId); builder.Append(',');
        AppendNullableString(builder, "componentName", node.ComponentName); builder.Append(',');
        builder.Append("\"bounds\":"); AppendRect(builder, node.Bounds); builder.Append(',');
        builder.Append("\"boxModel\":"); AppendBoxModel(builder, node.BoxModel); builder.Append(',');
        builder.Append("\"state\":"); AppendState(builder, node.State); builder.Append(',');
        builder.Append("\"source\":"); AppendSource(builder, node.Source); builder.Append(',');
        AppendNullableString(builder, "text", node.Text); builder.Append(',');
        builder.Append($"\"childCount\":{node.ChildCount},");
        builder.Append("\"children\":[");
        for (var i = 0; i < node.Children.Count; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append(SerializeNode(node.Children[i]));
        }
        builder.Append(']');
        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendRect(StringBuilder builder, Rect rect)
    {
        builder.Append('{');
        AppendNumber(builder, "x", rect.X); builder.Append(',');
        AppendNumber(builder, "y", rect.Y); builder.Append(',');
        AppendNumber(builder, "width", rect.Width); builder.Append(',');
        AppendNumber(builder, "height", rect.Height);
        builder.Append('}');
    }

    private static void AppendBoxModel(StringBuilder builder, ElementInspectionBoxModel? boxModel)
    {
        if (boxModel == null) { builder.Append("null"); return; }
        builder.Append('{');
        builder.Append("\"content\":"); AppendRect(builder, boxModel.Content); builder.Append(',');
        builder.Append("\"padding\":"); AppendRect(builder, boxModel.Padding); builder.Append(',');
        builder.Append("\"border\":"); AppendRect(builder, boxModel.Border); builder.Append(',');
        builder.Append("\"margin\":"); AppendRect(builder, boxModel.Margin);
        builder.Append('}');
    }

    private static void AppendState(StringBuilder builder, ElementInspectionState state)
    {
        builder.Append('{');
        builder.Append($"\"hover\":{Bool(state.Hover)},");
        builder.Append($"\"focus\":{Bool(state.Focus)},");
        builder.Append($"\"active\":{Bool(state.Active)},");
        builder.Append($"\"disabled\":{Bool(state.Disabled)}");
        builder.Append('}');
    }

    private static void AppendSource(StringBuilder builder, ElementInspectionSource? source)
    {
        if (source == null) { builder.Append("null"); return; }
        builder.Append('{');
        builder.Append($"\"sourceId\":{source.SourceId},");
        AppendNullableString(builder, "file", source.File); builder.Append(',');
        builder.Append($"\"startLine\":{source.StartLine},");
        builder.Append($"\"startColumn\":{source.StartColumn},");
        builder.Append($"\"endLine\":{source.EndLine},");
        builder.Append($"\"endColumn\":{source.EndColumn},");
        builder.Append($"\"kind\":\"{Escape(source.Kind)}\"");
        builder.Append('}');
    }

    private static void AppendNullableString(StringBuilder builder, string name, string? value)
    {
        builder.Append('"').Append(name).Append("\":");
        if (value == null) builder.Append("null");
        else builder.Append('"').Append(Escape(value)).Append('"');
    }

    private static void AppendNumber(StringBuilder builder, string name, float value)
    {
        builder.Append('"').Append(name).Append("\":");
        builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
    }

    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (char.IsControl(ch)) builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else builder.Append(ch);
                    break;
            }
        }
        return builder.ToString();
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static string ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new BadHttpRequestException($"'{name}' must be a string.");
        return value.GetString() ?? "";
    }

    private static int ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
            throw new BadHttpRequestException($"'{name}' must be an integer.");
        return result;
    }

    private static float ReadFloat(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || !value.TryGetSingle(out var result))
            throw new BadHttpRequestException($"'{name}' must be a number.");
        return result;
    }
    private static float? ReadOptionalFloat(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        if (!value.TryGetSingle(out var result))
            throw new BadHttpRequestException($"'{name}' must be a number.");
        return result;
    }

    private static bool ReadOptionalBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return false;
        if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
            throw new BadHttpRequestException($"'{name}' must be a boolean.");
        return value.GetBoolean();
    }

    private static float ReadFloat(System.Collections.Specialized.NameValueCollection query, string name)
    {
        var value = query[name];
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            throw new BadHttpRequestException($"'{name}' must be a number.");
        return result;
    }

    private static T ReadEnum<T>(JsonElement element, string name) where T : struct, Enum
    {
        var value = ReadString(element, name);
        if (!Enum.TryParse<T>(value, ignoreCase: true, out var result))
            throw new BadHttpRequestException($"'{name}' has an unsupported value.");
        return result;
    }

    private static KeyModifiers ReadModifiers(JsonElement element)
    {
        if (!element.TryGetProperty("modifiers", out var value) || value.ValueKind != JsonValueKind.Array)
            return KeyModifiers.None;
        var result = KeyModifiers.None;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String &&
                Enum.TryParse<KeyModifiers>(item.GetString(), ignoreCase: true, out var modifier))
                result |= modifier;
        }
        return result;
    }

    private static class StatusCodes
    {
        public const int Ok = 200;
        public const int NoContent = 204;
        public const int BadRequest = 400;
        public const int Unauthorized = 401;
        public const int Forbidden = 403;
        public const int NotFound = 404;
        public const int InternalServerError = 500;
    }

    private sealed class BadHttpRequestException(string message) : Exception(message);
}
