using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Square.Graphics;
using Square.Graphics.Codecs;
using Square.Hosting;

namespace Square.DevTools;

internal sealed class CdpSession
{
    private readonly WebSocket _socket;
    private readonly AppWindow _window;
    private readonly DevToolsServer _server;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Dictionary<int, int> _elementNodeIds = [];
    private readonly Dictionary<int, int> _nodeElementIds = [];
    private readonly Dictionary<int, int> _textNodeIds = [];
    private int _nextNodeId = 2;

    private CdpSession(WebSocket socket, AppWindow window, DevToolsServer server)
    {
        _socket = socket;
        _window = window;
        _server = server;
    }

    public static async Task RunAsync(
        WebSocket socket,
        AppWindow window,
        DevToolsServer server,
        CancellationToken cancellationToken = default)
    {
        var session = new CdpSession(socket, window, server);
        window.InspectorNodeSelected += session.HandleInspectorNodeSelected;
        try
        {
            await session.RunCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.InternalServerError,
                        $"CDP session error: {exception.Message}",
                        CancellationToken.None);
                }
                catch { }
            }
        }
        finally
        {
            window.InspectorNodeSelected -= session.HandleInspectorNodeSelected;
            session._writeGate.Dispose();
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None); }
                catch { }
            }
            socket.Dispose();
        }
    }

    private async Task RunCoreAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await ReceiveTextAsync(cancellationToken);
            if (message == null) return;
            await DispatchAsync(message, cancellationToken);
        }
    }

    private void HandleInspectorNodeSelected(int debugId)
    {
        var nodeId = GetElementNodeId(debugId);
        _ = SendInspectorNodeRequestedAsync(nodeId);
    }

    private async Task SendInspectorNodeRequestedAsync(int nodeId)
    {
        try
        {
            await SendEventAsync("Overlay.inspectNodeRequested", writer =>
            {
                writer.WriteNumber("backendNodeId", nodeId);
            }, CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (WebSocketException)
        {
        }
    }

    private async Task DispatchAsync(string message, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("id", out var idElement) ||
            !idElement.TryGetInt32(out var id) ||
            !root.TryGetProperty("method", out var methodElement) ||
            methodElement.ValueKind != JsonValueKind.String)
        {
            await SendErrorAsync(0, -32600, "Invalid CDP request.", cancellationToken);
            return;
        }

        var method = methodElement.GetString() ?? "";
        var parameters = root.TryGetProperty("params", out var paramsElement)
            ? paramsElement.Clone()
            : default;
        try
        {
            await DispatchCommandAsync(id, method, parameters, cancellationToken);
        }
        catch (Exception exception)
        {
            await SendErrorAsync(id, -32000, exception.Message, cancellationToken);
        }
    }

    private async Task DispatchCommandAsync(
        int id,
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        switch (method)
        {
            case "Runtime.enable":
                await SendEmptyResultAsync(id, cancellationToken);
                await SendExecutionContextCreatedAsync(cancellationToken);
                return;
            case "Runtime.disable":
            case "Runtime.releaseObject":
            case "Runtime.releaseObjectGroup":
            case "Runtime.runIfWaitingForDebugger":
            case "Runtime.addBinding":
            case "DOM.disable":
            case "Page.disable":
            case "Target.setAutoAttach":
            case "Target.setDiscoverTargets":
            case "Inspector.enable":
            case "Inspector.disable":
            case "Console.enable":
            case "Console.disable":
            case "Log.enable":
            case "Log.disable":
            case "Network.enable":
            case "Network.disable":
            case "CSS.enable":
            case "CSS.disable":
            case "Overlay.enable":
            case "Overlay.disable":
            case "Overlay.setShowViewportSizeOnResize":
            case "Overlay.setShowGridOverlays":
            case "Overlay.setShowFlexOverlays":
            case "Overlay.setShowScrollSnapOverlays":
            case "Overlay.setShowContainerQueryOverlays":
            case "Overlay.setShowIsolatedElements":
            case "Overlay.highlightRect":
                await SendEmptyResultAsync(id, cancellationToken);
                return;
            case "Overlay.highlightNode":
                await HighlightNodeAsync(id, parameters, cancellationToken);
                return;
            case "Overlay.hideHighlight":
                await _window.ClearInspectorHighlightAsync();
                await SendEmptyResultAsync(id, cancellationToken);
                return;
            case "Overlay.setInspectMode":
                await SetInspectModeAsync(id, parameters, cancellationToken);
                return;
            case "Runtime.getProperties":
                await SendResultAsync(id, writer =>
                {
                    writer.WritePropertyName("result");
                    writer.WriteStartArray();
                    writer.WriteEndArray();
                    writer.WritePropertyName("internalProperties");
                    writer.WriteStartArray();
                    writer.WriteEndArray();
                }, cancellationToken);
                return;
            case "Runtime.callFunctionOn":
                await SendResultAsync(id, static writer =>
                {
                    writer.WritePropertyName("result");
                    writer.WriteStartObject();
                    writer.WriteString("type", "undefined");
                    writer.WriteEndObject();
                }, cancellationToken);
                return;
            case "DOM.enable":
                await SendEmptyResultAsync(id, cancellationToken);
                await SendEventAsync("DOM.documentUpdated", static writer => { }, cancellationToken);
                return;
            case "Page.enable":
                await SendEmptyResultAsync(id, cancellationToken);
                return;
            case "DOM.getDocument":
            case "DOM.getFlattenedDocument":
                await SendDocumentAsync(id, cancellationToken);
                return;
            case "DOM.requestChildNodes":
                await SendChildNodesAsync(id, parameters, cancellationToken);
                return;
            case "DOM.describeNode":
                await SendDescribeNodeAsync(id, parameters, cancellationToken);
                return;
            case "DOM.getAttributes":
                await SendAttributesAsync(id, parameters, cancellationToken);
                return;
            case "DOM.getNodeForLocation":
                await SendNodeForLocationAsync(id, parameters, cancellationToken);
                return;
            case "DOM.getBoxModel":
                await SendBoxModelAsync(id, parameters, cancellationToken);
                return;
            case "DOM.getOuterHTML":
                await SendOuterHtmlAsync(id, parameters, cancellationToken);
                return;
            case "DOM.setInspectedNode":
                await SendEmptyResultAsync(id, cancellationToken);
                return;
            case "DOM.pushNodesByBackendIdsToFrontend":
                await SendResultAsync(id, static writer =>
                {
                    writer.WritePropertyName("nodeIds");
                    writer.WriteStartArray();
                    writer.WriteEndArray();
                }, cancellationToken);
                return;
            case "DOM.resolveNode":
                await SendResolveNodeAsync(id, parameters, cancellationToken);
                return;
            case "CSS.getComputedStyleForNode":
                await SendComputedStyleAsync(id, parameters, cancellationToken);
                return;
            case "CSS.getMatchedStylesForNode":
                await SendMatchedStylesAsync(id, parameters, cancellationToken);
                return;
            case "CSS.getAnimatedStylesForNode":
                await SendAnimatedStylesAsync(id, parameters, cancellationToken);
                return;
            case "CSS.trackComputedStyleUpdatesForNode":
            case "CSS.takeComputedStyleUpdates":
            case "CSS.trackComputedStyleUpdates":
                await SendResultAsync(id, writer =>
                {
                    if (method == "CSS.takeComputedStyleUpdates")
                    {
                        writer.WritePropertyName("nodeIds");
                        writer.WriteStartArray();
                        writer.WriteEndArray();
                    }
                }, cancellationToken);
                return;
            case "CSS.getInlineStylesForNode":
                await SendInlineStylesAsync(id, parameters, cancellationToken);
                return;
            case "CSS.getPlatformFontsForNode":
                await SendResultAsync(id, static writer =>
                {
                    writer.WritePropertyName("fonts");
                    writer.WriteStartArray();
                    writer.WriteEndArray();
                }, cancellationToken);
                return;
            case "CSS.getEnvironmentVariables":
                await SendResultAsync(id, static writer =>
                {
                    writer.WritePropertyName("environmentVariables");
                    writer.WriteStartArray();
                    writer.WriteEndArray();
                }, cancellationToken);
                return;
            case "Page.getFrameTree":
                await SendFrameTreeAsync(id, cancellationToken);
                return;
            case "Page.getResourceTree":
                await SendResourceTreeAsync(id, cancellationToken);
                return;
            case "Page.getNavigationHistory":
                await SendNavigationHistoryAsync(id, cancellationToken);
                return;
            case "Page.getLayoutMetrics":
                await SendLayoutMetricsAsync(id, cancellationToken);
                return;
            case "Page.captureScreenshot":
                await SendScreenshotAsync(id, cancellationToken);
                return;
            case "Page.startScreencast":
            case "Page.stopScreencast":
                await SendEmptyResultAsync(id, cancellationToken);
                return;
            case "Page.addScriptToEvaluateOnNewDocument":
                await SendResultAsync(id, static writer => writer.WriteString("identifier", "square-noop"), cancellationToken);
                return;
            case "Target.getTargetInfo":
                await SendTargetInfoAsync(id, cancellationToken);
                return;
            case "Schema.getDomains":
                await SendResultAsync(id, static writer => writer.WriteStartArray("domains"), cancellationToken, closeArray: true);
                return;
            default:
                await SendErrorAsync(id, -32601, $"Method '{method}' is not supported.", cancellationToken);
                return;
        }
    }

    private async Task HighlightNodeAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var nodeId = ReadNodeId(parameters);
        if (!_nodeElementIds.TryGetValue(nodeId, out var debugId) ||
            !await _window.SetInspectorHighlightAsync(debugId))
        {
            await SendErrorAsync(id, -32000, "Node not found.", cancellationToken);
            return;
        }
        await SendEmptyResultAsync(id, cancellationToken);
    }

    private async Task SetInspectModeAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var mode = parameters.ValueKind == JsonValueKind.Object &&
                   parameters.TryGetProperty("mode", out var modeElement)
            ? modeElement.GetString()
            : null;
        await _window.SetInspectorModeAsync(!string.Equals(mode, "none", StringComparison.OrdinalIgnoreCase));
        await SendEmptyResultAsync(id, cancellationToken);
    }

    private async Task SendDocumentAsync(int id, CancellationToken cancellationToken)
    {
        var snapshot = await _window.CaptureInspectionSnapshotAsync(includeSourcePaths: false, includeTextContent: true);
        var document = new CdpNode(1, 1, 9, "#document", "", "", 0, "square://application");
        document.Children.Add(BuildElement(snapshot.Root, document.NodeId));
        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("root");
            WriteNode(writer, document, includeChildren: true);
        }, cancellationToken);
    }

    private async Task SendChildNodesAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var nodeId = ReadNodeId(parameters);
        var snapshot = await _window.CaptureInspectionSnapshotAsync(false, true);
        var document = new CdpNode(1, 1, 9, "#document", "", "", 0, "square://application");
        document.Children.Add(BuildElement(snapshot.Root, document.NodeId));
        var node = FindNode(document, nodeId);
        if (node == null)
        {
            await SendErrorAsync(id, -32000, "Node not found.", cancellationToken);
            return;
        }

        await SendEventAsync("DOM.setChildNodes", writer =>
        {
            writer.WriteNumber("parentId", node.NodeId);
            writer.WritePropertyName("nodes");
            writer.WriteStartArray();
            foreach (var child in node.Children) WriteNode(writer, child, includeChildren: false);
            writer.WriteEndArray();
        }, cancellationToken);
        await SendEmptyResultAsync(id, cancellationToken);
    }

    private async Task SendDescribeNodeAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var nodeId = ReadNodeId(parameters);
        var snapshot = await _window.CaptureInspectionSnapshotAsync(false, true);
        var document = new CdpNode(1, 1, 9, "#document", "", "", 0, "square://application");
        document.Children.Add(BuildElement(snapshot.Root, document.NodeId));
        var node = FindNode(document, nodeId);
        if (node == null)
        {
            await SendErrorAsync(id, -32000, "Node not found.", cancellationToken);
            return;
        }

        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("node");
            WriteNode(writer, node, includeChildren: true);
        }, cancellationToken);
    }

    private async Task SendAttributesAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var nodeId = ReadNodeId(parameters);
        var snapshot = await _window.CaptureInspectionSnapshotAsync(false, true);
        var document = new CdpNode(1, 1, 9, "#document", "", "", 0, "square://application");
        document.Children.Add(BuildElement(snapshot.Root, document.NodeId));
        var node = FindNode(document, nodeId);
        if (node == null)
        {
            await SendErrorAsync(id, -32000, "Node not found.", cancellationToken);
            return;
        }

        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("attributes");
            writer.WriteStartArray();
            foreach (var attribute in node.Attributes)
            {
                writer.WriteStringValue(attribute.Name);
                writer.WriteStringValue(attribute.Value);
            }
            writer.WriteEndArray();
        }, cancellationToken);
    }

    private async Task SendNodeForLocationAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var x = ReadFloat(parameters, "x");
        var y = ReadFloat(parameters, "y");
        var hit = await _window.HitTestInspectionAsync(new Point(x, y), false, true);
        if (hit == null)
        {
            await SendResultAsync(id, static writer => { }, cancellationToken);
            return;
        }

        var nodeId = GetElementNodeId(hit.Id);
        await SendResultAsync(id, writer =>
        {
            writer.WriteNumber("backendNodeId", nodeId);
            writer.WriteNumber("nodeId", nodeId);
            writer.WriteString("frameId", _server.TargetId);
        }, cancellationToken);
    }

    private async Task SendBoxModelAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var nodeId = ReadNodeId(parameters);
        if (!_nodeElementIds.TryGetValue(nodeId, out var debugId))
        {
            await SendErrorAsync(id, -32000, "Node not found.", cancellationToken);
            return;
        }

        var node = await _window.InspectElementAsync(debugId, false, true);
        if (node == null)
        {
            await SendErrorAsync(id, -32000, "Node not found.", cancellationToken);
            return;
        }

        await SendResultAsync(id, writer =>
        {
            var boxModel = node.BoxModel ?? new ElementInspectionBoxModel(
                node.Bounds, node.Bounds, node.Bounds, node.Bounds);
            writer.WritePropertyName("model");
            writer.WriteStartObject();
            writer.WritePropertyName("content");
            WriteQuad(writer, boxModel.Content);
            writer.WritePropertyName("padding");
            WriteQuad(writer, boxModel.Padding);
            writer.WritePropertyName("border");
            WriteQuad(writer, boxModel.Border);
            writer.WritePropertyName("margin");
            WriteQuad(writer, boxModel.Margin);
            writer.WriteNumber("width", node.Bounds.Width);
            writer.WriteNumber("height", node.Bounds.Height);
            writer.WriteEndObject();
        }, cancellationToken);
    }

    private async Task SendOuterHtmlAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var nodeId = ReadNodeId(parameters);
        var snapshot = await _window.CaptureInspectionSnapshotAsync(false, true);
        var document = new CdpNode(1, 1, 9, "#document", "", "", 0, "square://application");
        document.Children.Add(BuildElement(snapshot.Root, document.NodeId));
        var node = FindNode(document, nodeId);
        if (node == null)
        {
            await SendErrorAsync(id, -32000, "Node not found.", cancellationToken);
            return;
        }

        var html = new StringBuilder();
        WriteOuterHtml(html, node);
        await SendResultAsync(id, writer => writer.WriteString("outerHTML", html.ToString()), cancellationToken);
    }

    private async Task SendInlineStylesAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var styles = await GetElementStylesAsync(id, parameters, cancellationToken);
        if (styles == null) return;

        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("inlineStyle");
            WriteInlineStyle(writer, styles.InlineCssText);
            writer.WritePropertyName("attributesStyle");
            writer.WriteNullValue();
        }, cancellationToken);
    }

    private static void WriteQuad(Utf8JsonWriter writer, Rect bounds)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(bounds.Left);
        writer.WriteNumberValue(bounds.Top);
        writer.WriteNumberValue(bounds.Right);
        writer.WriteNumberValue(bounds.Top);
        writer.WriteNumberValue(bounds.Right);
        writer.WriteNumberValue(bounds.Bottom);
        writer.WriteNumberValue(bounds.Left);
        writer.WriteNumberValue(bounds.Bottom);
        writer.WriteEndArray();
    }

    private static void WriteOuterHtml(StringBuilder builder, CdpNode node)
    {
        if (node.NodeType == 9)
        {
            foreach (var child in node.Children) WriteOuterHtml(builder, child);
            return;
        }
        if (node.NodeType == 3)
        {
            builder.Append(System.Net.WebUtility.HtmlEncode(node.NodeValue));
            return;
        }

        builder.Append('<').Append(node.LocalName.ToLowerInvariant());
        foreach (var attribute in node.Attributes)
            builder.Append(' ').Append(attribute.Name).Append("=\"")
                .Append(System.Net.WebUtility.HtmlEncode(attribute.Value)).Append('\"');
        builder.Append('>');
        foreach (var child in node.Children) WriteOuterHtml(builder, child);
        builder.Append("</").Append(node.LocalName.ToLowerInvariant()).Append('>');
    }

    private async Task SendResolveNodeAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var nodeId = ReadNodeId(parameters);
        if (!_nodeElementIds.ContainsKey(nodeId))
        {
            var snapshot = await _window.CaptureInspectionSnapshotAsync(false, true);
            var document = new CdpNode(1, 1, 9, "#document", "", "", 0, "square://application");
            document.Children.Add(BuildElement(snapshot.Root, document.NodeId));
        }

        if (!_nodeElementIds.ContainsKey(nodeId))
        {
            await SendErrorAsync(id, -32000, "Node not found.", cancellationToken);
            return;
        }

        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("object");
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WriteString("objectId", $"square-node-{nodeId}");
            writer.WriteString("description", "Square Element");
            writer.WriteEndObject();
        }, cancellationToken);
    }

    private async Task SendComputedStyleAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var styles = await GetElementStylesAsync(id, parameters, cancellationToken);
        if (styles == null) return;

        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("computedStyle");
            writer.WriteStartArray();
            foreach (var pair in styles.Computed)
            {
                writer.WriteStartObject();
                writer.WriteString("name", pair.Key);
                writer.WriteString("value", pair.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }, cancellationToken);
    }

    private async Task SendMatchedStylesAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var styles = await GetElementStylesAsync(id, parameters, cancellationToken);
        if (styles == null) return;

        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("matchedCSSRules");
            writer.WriteStartArray();
            foreach (var rule in styles.MatchedRules ?? [])
            {
                writer.WriteStartObject();
                writer.WritePropertyName("rule");
                WriteMatchedCssRule(writer, rule);
                writer.WritePropertyName("matchingSelectors");
                writer.WriteStartArray();
                writer.WriteNumberValue(0);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("pseudoElements");
            writer.WriteStartArray();
            writer.WriteEndArray();
            writer.WritePropertyName("inherited");
            writer.WriteStartArray();
            writer.WriteEndArray();
            writer.WritePropertyName("inheritedPseudoElements");
            writer.WriteStartArray();
            writer.WriteEndArray();
            writer.WritePropertyName("cssKeyframesRules");
            writer.WriteStartArray();
            writer.WriteEndArray();
            writer.WritePropertyName("inlineStyle");
            WriteInlineStyle(writer, styles.InlineCssText);
        }, cancellationToken);
    }

    private async Task SendAnimatedStylesAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var styles = await GetElementStylesAsync(id, parameters, cancellationToken);
        if (styles == null) return;

        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("animatedStyles");
            writer.WriteStartArray();
            writer.WriteEndArray();
            writer.WritePropertyName("transitionsStyle");
            WriteInlineStyle(writer, "");
            writer.WritePropertyName("inherited");
            writer.WriteStartArray();
            writer.WriteEndArray();
            writer.WritePropertyName("inlineStyle");
            WriteInlineStyle(writer, styles.InlineCssText);
        }, cancellationToken);
    }

    private async Task<ElementInspectionStyleSnapshot?> GetElementStylesAsync(
        int requestId,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        var nodeId = ReadNodeId(parameters);
        if (!_nodeElementIds.ContainsKey(nodeId))
        {
            var snapshot = await _window.CaptureInspectionSnapshotAsync(false, true);
            var document = new CdpNode(1, 1, 9, "#document", "", "", 0, "square://application");
            document.Children.Add(BuildElement(snapshot.Root, document.NodeId));
        }

        if (!_nodeElementIds.TryGetValue(nodeId, out var debugId))
        {
            await SendErrorAsync(requestId, -32000, "Node not found.", cancellationToken);
            return null;
        }

        var styles = await _window.InspectElementStylesAsync(debugId);
        if (styles == null)
        {
            await SendErrorAsync(requestId, -32000, "Node styles not found.", cancellationToken);
            return null;
        }
        return styles;
    }

    private static void WriteMatchedCssRule(Utf8JsonWriter writer, ElementInspectionStyleRule rule)
    {
        writer.WriteStartObject();
        writer.WriteString("styleSheetId", "square-css");
        writer.WritePropertyName("selectorList");
        writer.WriteStartObject();
        writer.WritePropertyName("selectors");
        writer.WriteStartArray();
        writer.WriteStringValue(rule.Selector);
        writer.WriteEndArray();
        writer.WriteString("text", rule.Selector);
        writer.WriteEndObject();
        writer.WriteString("origin", "regular");
        writer.WritePropertyName("style");
        WriteDeclarationStyle(writer, "square-css", rule.Declarations);
        writer.WriteEndObject();
    }

    private static void WriteDeclarationStyle(
        Utf8JsonWriter writer,
        string styleSheetId,
        IReadOnlyList<ElementInspectionStyleDeclaration> declarations)
    {
        writer.WriteStartObject();
        writer.WriteString("styleSheetId", styleSheetId);
        writer.WritePropertyName("cssProperties");
        writer.WriteStartArray();
        foreach (var declaration in declarations)
        {
            writer.WriteStartObject();
            writer.WriteString("name", declaration.Property);
            writer.WriteString("value", declaration.Value);
            writer.WriteBoolean("important", declaration.Important);
            writer.WriteBoolean("implicit", false);
            writer.WriteBoolean("parsedOk", true);
            writer.WriteBoolean("disabled", false);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WritePropertyName("shorthandEntries");
        writer.WriteStartArray();
        writer.WriteEndArray();
        writer.WriteString("cssText", string.Join(" ", declarations.Select(static declaration =>
            $"{declaration.Property}: {declaration.Value}{(declaration.Important ? " !important" : "")};")));
        writer.WriteEndObject();
    }

    private static void WriteInlineStyle(Utf8JsonWriter writer, string cssText)
    {
        writer.WriteStartObject();
        writer.WriteString("styleSheetId", "square-inline");
        writer.WritePropertyName("cssProperties");
        writer.WriteStartArray();
        foreach (var declaration in cssText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = declaration.IndexOf(':');
            if (separator <= 0) continue;
            var name = declaration[..separator].Trim();
            var value = declaration[(separator + 1)..].Trim();
            var important = value.EndsWith("!important", StringComparison.OrdinalIgnoreCase);
            if (important) value = value[..^10].TrimEnd();
            writer.WriteStartObject();
            writer.WriteString("name", name);
            writer.WriteString("value", value);
            writer.WriteBoolean("important", important);
            writer.WriteBoolean("implicit", false);
            writer.WriteBoolean("parsedOk", true);
            writer.WriteBoolean("disabled", false);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WritePropertyName("shorthandEntries");
        writer.WriteStartArray();
        writer.WriteEndArray();
        writer.WriteString("cssText", cssText);
        writer.WriteEndObject();
    }

    private async Task SendResourceTreeAsync(int id, CancellationToken cancellationToken)
    {
        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("frameTree");
            writer.WriteStartObject();
            writer.WritePropertyName("frame");
            WriteFrame(writer);
            writer.WritePropertyName("resources");
            writer.WriteStartArray();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }, cancellationToken);
    }

    private async Task SendNavigationHistoryAsync(int id, CancellationToken cancellationToken)
    {
        await SendResultAsync(id, writer =>
        {
            writer.WriteNumber("currentIndex", 0);
            writer.WritePropertyName("entries");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteNumber("id", 0);
            writer.WriteString("url", $"square://application/{_server.TargetId}");
            writer.WriteString("userTypedURL", $"square://application/{_server.TargetId}");
            writer.WriteString("title", "Square DevTools");
            writer.WriteString("transitionType", "typed");
            writer.WriteEndObject();
            writer.WriteEndArray();
        }, cancellationToken);
    }

    private async Task SendFrameTreeAsync(int id, CancellationToken cancellationToken)
    {
        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("frameTree");
            writer.WriteStartObject();
            writer.WritePropertyName("frame");
            WriteFrame(writer);
            writer.WriteEndObject();
        }, cancellationToken);
    }

    private async Task SendLayoutMetricsAsync(int id, CancellationToken cancellationToken)
    {
        var size = _window.ClientSize;
        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("layoutViewport");
            WriteViewport(writer, size.Width, size.Height);
            writer.WritePropertyName("visualViewport");
            writer.WriteStartObject();
            writer.WriteNumber("offsetX", 0);
            writer.WriteNumber("offsetY", 0);
            writer.WriteNumber("pageX", 0);
            writer.WriteNumber("pageY", 0);
            writer.WriteNumber("clientWidth", size.Width);
            writer.WriteNumber("clientHeight", size.Height);
            writer.WriteNumber("scale", 1);
            writer.WriteEndObject();
            writer.WritePropertyName("contentSize");
            writer.WriteStartObject();
            writer.WriteNumber("x", 0);
            writer.WriteNumber("y", 0);
            writer.WriteNumber("width", size.Width);
            writer.WriteNumber("height", size.Height);
            writer.WriteEndObject();
        }, cancellationToken);
    }

    private async Task SendScreenshotAsync(int id, CancellationToken cancellationToken)
    {
        using var bitmap = await _window.CaptureRendererBitmapAsync();
        using var stream = new MemoryStream();
        BitmapPngEncoder.Save(bitmap, stream);
        var base64 = Convert.ToBase64String(stream.ToArray());
        await SendResultAsync(id, writer => writer.WriteString("data", base64), cancellationToken);
    }

    private async Task SendTargetInfoAsync(int id, CancellationToken cancellationToken)
    {
        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("targetInfo");
            writer.WriteStartObject();
            writer.WriteString("targetId", _server.TargetId);
            writer.WriteString("type", "page");
            writer.WriteString("title", "Square DevTools");
            writer.WriteString("url", $"square://application/{_server.TargetId}");
            writer.WriteBoolean("attached", true);
            writer.WriteBoolean("canAccessOpener", false);
            writer.WriteString("openerId", "");
            writer.WriteEndObject();
        }, cancellationToken);
    }

    private async Task SendExecutionContextCreatedAsync(CancellationToken cancellationToken)
    {
        await SendEventAsync("Runtime.executionContextCreated", writer =>
        {
            writer.WritePropertyName("context");
            writer.WriteStartObject();
            writer.WriteNumber("id", 1);
            writer.WriteString("origin", "square://application");
            writer.WriteString("name", "Square");
            writer.WritePropertyName("auxData");
            writer.WriteStartObject();
            writer.WriteBoolean("isDefault", true);
            writer.WriteString("type", "default");
            writer.WriteString("frameId", _server.TargetId);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }, cancellationToken);
    }

    private void WriteFrame(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("id", _server.TargetId);
        writer.WriteString("loaderId", _server.TargetId);
        writer.WriteString("url", $"square://application/{_server.TargetId}");
        writer.WriteString("domainAndRegistry", "");
        writer.WriteString("securityOrigin", "square://application");
        writer.WriteString("mimeType", "text/html");
        writer.WriteString("secureContextType", "Secure");
        writer.WriteString("crossOriginIsolatedContextType", "NotIsolated");
        writer.WritePropertyName("gatedAPIFeatures");
        writer.WriteStartArray();
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteViewport(Utf8JsonWriter writer, float width, float height)
    {
        writer.WriteStartObject();
        writer.WriteNumber("pageX", 0);
        writer.WriteNumber("pageY", 0);
        writer.WriteNumber("clientWidth", width);
        writer.WriteNumber("clientHeight", height);
        writer.WriteEndObject();
    }

    private CdpNode BuildElement(ElementInspectionNode node, int parentId)
    {
        var nodeId = GetElementNodeId(node.Id);
        var element = new CdpNode(
            nodeId,
            nodeId,
            1,
            node.TagName.ToUpperInvariant(),
            node.TagName,
            "",
            parentId,
            "");
        if (!string.IsNullOrEmpty(node.ElementId))
            element.Attributes.Add(new CdpAttribute("id", node.ElementId));
        if (node.ClassNames is { Count: > 0 })
            element.Attributes.Add(new CdpAttribute("class", string.Join(' ', node.ClassNames)));
        if (!string.IsNullOrEmpty(node.ComponentName))
            element.Attributes.Add(new CdpAttribute("data-square-component", node.ComponentName));
        foreach (var child in node.Children)
            element.Children.Add(BuildElement(child, nodeId));
        if (node.Text != null)
        {
            var textId = GetTextNodeId(node.Id);
            element.Children.Add(new CdpNode(textId, textId, 3, "#text", "", node.Text, nodeId, ""));
        }
        return element;
    }

    private int GetElementNodeId(int debugId)
    {
        if (_elementNodeIds.TryGetValue(debugId, out var nodeId)) return nodeId;
        nodeId = _nextNodeId++;
        _elementNodeIds[debugId] = nodeId;
        _nodeElementIds[nodeId] = debugId;
        return nodeId;
    }

    private int GetTextNodeId(int debugId)
    {
        if (_textNodeIds.TryGetValue(debugId, out var nodeId)) return nodeId;
        nodeId = _nextNodeId++;
        _textNodeIds[debugId] = nodeId;
        return nodeId;
    }

    private static CdpNode? FindNode(CdpNode node, int nodeId)
    {
        if (node.NodeId == nodeId) return node;
        foreach (var child in node.Children)
        {
            var found = FindNode(child, nodeId);
            if (found != null) return found;
        }
        return null;
    }

    private static void WriteNode(Utf8JsonWriter writer, CdpNode node, bool includeChildren)
    {
        writer.WriteStartObject();
        writer.WriteNumber("nodeId", node.NodeId);
        writer.WriteNumber("backendNodeId", node.BackendNodeId);
        writer.WriteNumber("nodeType", node.NodeType);
        writer.WriteString("nodeName", node.NodeName);
        writer.WriteString("localName", node.LocalName);
        writer.WriteString("nodeValue", node.NodeValue);
        if (node.ParentId != 0) writer.WriteNumber("parentId", node.ParentId);
        if (node.DocumentUrl.Length > 0) writer.WriteString("documentURL", node.DocumentUrl);
        writer.WritePropertyName("attributes");
        writer.WriteStartArray();
        foreach (var attribute in node.Attributes)
        {
            writer.WriteStringValue(attribute.Name);
            writer.WriteStringValue(attribute.Value);
        }
        writer.WriteEndArray();
        writer.WriteNumber("childNodeCount", node.Children.Count);
        if (includeChildren)
        {
            writer.WritePropertyName("children");
            writer.WriteStartArray();
            foreach (var child in node.Children) WriteNode(writer, child, true);
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }

    private async Task SendEmptyResultAsync(int id, CancellationToken cancellationToken) =>
        await SendResultAsync(id, static _ => { }, cancellationToken);

    private async Task SendResultAsync(
        int id,
        Action<Utf8JsonWriter> writeResult,
        CancellationToken cancellationToken,
        bool closeArray = false)
    {
        await SendJsonAsync(writer =>
        {
            writer.WriteNumber("id", id);
            writer.WritePropertyName("result");
            writer.WriteStartObject();
            writeResult(writer);
            if (closeArray) writer.WriteEndArray();
            writer.WriteEndObject();
        }, cancellationToken);
    }

    private async Task SendErrorAsync(int id, int code, string message, CancellationToken cancellationToken)
    {
        await SendJsonAsync(writer =>
        {
            writer.WriteNumber("id", id);
            writer.WritePropertyName("error");
            writer.WriteStartObject();
            writer.WriteNumber("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
        }, cancellationToken);
    }

    private async Task SendEventAsync(string method, Action<Utf8JsonWriter> writeParameters, CancellationToken cancellationToken)
    {
        await SendJsonAsync(writer =>
        {
            writer.WriteString("method", method);
            writer.WritePropertyName("params");
            writer.WriteStartObject();
            writeParameters(writer);
            writer.WriteEndObject();
        }, cancellationToken);
    }

    private async Task SendJsonAsync(Action<Utf8JsonWriter> write, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
            writer.Flush();
        }

        var payload = stream.ToArray();
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await _socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task<string?> ReceiveTextAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            if (result.MessageType != WebSocketMessageType.Text)
                throw new InvalidOperationException("Only text CDP WebSocket messages are supported.");
            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static int ReadNodeId(JsonElement parameters)
    {
        if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("nodeId", out var nodeId) && nodeId.TryGetInt32(out var result))
            return result;
        if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("backendNodeId", out var backendNodeId) && backendNodeId.TryGetInt32(out result))
            return result;
        throw new InvalidOperationException("'nodeId' or 'backendNodeId' is required.");
    }

    private static float ReadFloat(JsonElement parameters, string name)
    {
        if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty(name, out var value) && value.TryGetSingle(out var result))
            return result;
        throw new InvalidOperationException($"'{name}' must be a number.");
    }

    private sealed class CdpNode(
        int nodeId,
        int backendNodeId,
        int nodeType,
        string nodeName,
        string localName,
        string nodeValue,
        int parentId,
        string documentUrl)
    {
        public int NodeId { get; } = nodeId;
        public int BackendNodeId { get; } = backendNodeId;
        public int NodeType { get; } = nodeType;
        public string NodeName { get; } = nodeName;
        public string LocalName { get; } = localName;
        public string NodeValue { get; } = nodeValue;
        public int ParentId { get; } = parentId;
        public string DocumentUrl { get; } = documentUrl;
        public List<CdpAttribute> Attributes { get; } = [];
        public List<CdpNode> Children { get; } = [];
    }

    private sealed record CdpAttribute(string Name, string Value);
}
