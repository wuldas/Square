using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Square.DevTools;
using Square.Graphics;
using Square.Hosting;
using Square.Runtime;
using Xunit;

namespace Square.DevTools.Tests;

public sealed class CdpSessionTests
{
    [Fact]
    public async Task WebSocketServesRuntimeAndDomDocumentCommands()
    {
        var window = CreateWindow(out var runtime);
        await using var server = DevToolsServer.Start(window, new DevToolsOptions { AllowChromeInspect = true });
        using var client = new HttpClient { BaseAddress = new Uri(server.BaseAddress) };
        using var list = JsonDocument.Parse(await client.GetStringAsync("/json/list"));
        var websocketUrl = list.RootElement[0].GetProperty("webSocketDebuggerUrl").GetString();
        Assert.False(string.IsNullOrWhiteSpace(websocketUrl));

        using var websocket = new ClientWebSocket();
        await websocket.ConnectAsync(new Uri(websocketUrl!), CancellationToken.None);

        await SendAsync(websocket, "{\"id\":1,\"method\":\"Runtime.enable\"}");
        using var runtimeResponse = await ReceiveResponseAsync(websocket, 1);
        Assert.Equal(1, runtimeResponse.RootElement.GetProperty("id").GetInt32());
        Assert.True(runtimeResponse.RootElement.GetProperty("result").ValueKind == JsonValueKind.Object);

        await SendAsync(websocket, "{\"id\":2,\"method\":\"DOM.getDocument\",\"params\":{\"depth\":-1}} ");
        using var documentResponse = await ReceiveResponseAsync(websocket, 2);
        var root = documentResponse.RootElement.GetProperty("result").GetProperty("root");
        Assert.Equal(9, root.GetProperty("nodeType").GetInt32());
        var squareRoot = root.GetProperty("children")[0];
        Assert.Equal("VIEW", squareRoot.GetProperty("nodeName").GetString());
        Assert.Equal(2, squareRoot.GetProperty("childNodeCount").GetInt32());
        Assert.Equal("BUTTON", squareRoot.GetProperty("children")[0].GetProperty("nodeName").GetString());
        Assert.Equal("OK", squareRoot.GetProperty("children")[0].GetProperty("children")[0].GetProperty("nodeValue").GetString());
        var buttonAttributes = new Dictionary<string, string>();
        var attributeValues = squareRoot.GetProperty("children")[0].GetProperty("attributes").EnumerateArray().ToArray();
        for (var index = 0; index < attributeValues.Length; index += 2)
            buttonAttributes[attributeValues[index].GetString()!] = attributeValues[index + 1].GetString()!;
        Assert.Equal("primary rounded", buttonAttributes["class"]);

        await SendAsync(websocket, "{\"id\":3,\"method\":\"CSS.getComputedStyleForNode\",\"params\":{\"nodeId\":3}}");
        using var styleResponse = await ReceiveResponseAsync(websocket, 3);
        var computedStyle = styleResponse.RootElement.GetProperty("result").GetProperty("computedStyle");
        Assert.Contains(computedStyle.EnumerateArray(), item => item.GetProperty("name").GetString() == "color");

        await SendAsync(websocket, "{\"id\":4,\"method\":\"CSS.getMatchedStylesForNode\",\"params\":{\"nodeId\":3}}");
        using var matchedStyleResponse = await ReceiveResponseAsync(websocket, 4);
        var matchedRule = matchedStyleResponse.RootElement.GetProperty("result")
            .GetProperty("matchedCSSRules")[0].GetProperty("rule");
        Assert.Equal(".primary", matchedRule.GetProperty("selectorList").GetProperty("text").GetString());

        await SendAsync(websocket, "{\"id\":8,\"method\":\"DOM.getBoxModel\",\"params\":{\"nodeId\":3}}");
        using var boxModelResponse = await ReceiveResponseAsync(websocket, 8);
        var boxModel = boxModelResponse.RootElement.GetProperty("result").GetProperty("model");
        Assert.Equal(new[] { 15f, 15f, 85f, 15f, 85f, 45f, 15f, 45f },
            boxModel.GetProperty("content").EnumerateArray().Select(value => value.GetSingle()).ToArray());
        Assert.Equal(new[] { 12f, 12f, 88f, 12f, 88f, 48f, 12f, 48f },
            boxModel.GetProperty("padding").EnumerateArray().Select(value => value.GetSingle()).ToArray());
        Assert.Equal(new[] { 10f, 10f, 90f, 10f, 90f, 50f, 10f, 50f },
            boxModel.GetProperty("border").EnumerateArray().Select(value => value.GetSingle()).ToArray());
        Assert.Equal(new[] { 5f, 5f, 95f, 5f, 95f, 55f, 5f, 55f },
            boxModel.GetProperty("margin").EnumerateArray().Select(value => value.GetSingle()).ToArray());

        await SendAsync(websocket, "{\"id\":5,\"method\":\"Overlay.highlightNode\",\"params\":{\"nodeId\":3}}");
        using var highlightResponse = await ReceiveResponseAsync(websocket, 5);
        Assert.True(highlightResponse.RootElement.GetProperty("result").ValueKind == JsonValueKind.Object);
        Assert.Equal(11, runtime.HighlightDebugId);
        Assert.True(runtime.RequestRenderCount > 0);

        await SendAsync(websocket, "{\"id\":6,\"method\":\"Overlay.hideHighlight\"}");
        using var hideResponse = await ReceiveResponseAsync(websocket, 6);
        Assert.True(hideResponse.RootElement.GetProperty("result").ValueKind == JsonValueKind.Object);
        Assert.Null(runtime.HighlightDebugId);

        await SendAsync(websocket, "{\"id\":7,\"method\":\"Overlay.setInspectMode\",\"params\":{\"mode\":\"searchForNode\"}}");
        using var inspectResponse = await ReceiveResponseAsync(websocket, 7);
        Assert.True(inspectResponse.RootElement.GetProperty("result").ValueKind == JsonValueKind.Object);
        Assert.True(runtime.InspectorModeEnabled);
    }

    private static AppWindow CreateWindow(out StaticRuntime runtime)
    {
        var window = new AppWindow("CDP test");
        runtime = new StaticRuntime();
        window.BindApplication(new Dispatcher(), runtime);
        return window;
    }

    private static async Task SendAsync(ClientWebSocket websocket, string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await websocket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<JsonDocument> ReceiveResponseAsync(ClientWebSocket websocket, int id)
    {
        while (true)
        {
            var buffer = new byte[32 * 1024];
            using var stream = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await websocket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new InvalidOperationException($"CDP server closed the connection: {result.CloseStatusDescription}");
                stream.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            var document = JsonDocument.Parse(stream.ToArray());
            if (document.RootElement.TryGetProperty("id", out var responseId) && responseId.GetInt32() == id)
                return document;
            document.Dispose();
        }
    }

    private sealed class StaticRuntime : IAppWindowRuntime
    {
        public int? HighlightDebugId { get; private set; }
        public bool InspectorModeEnabled { get; private set; }
        public int RequestRenderCount { get; private set; }

        private static readonly ElementInspectionSnapshot Snapshot = new(
            new ElementInspectionNode(
                10,
                "View",
                "root",
                "Main",
                new Rect(0, 0, 320, 200),
                new ElementInspectionState(false, false, false, false),
                null,
                null,
                2,
                [
                    new ElementInspectionNode(
                        11,
                        "Button",
                        "ok",
                        null,
                        new Rect(10, 10, 80, 32),
                        new ElementInspectionState(false, false, false, false),
                        null,
                        "OK",
                        0,
                        [],
                        ["primary", "rounded"],
                        new ElementInspectionBoxModel(
                            new Rect(15, 15, 70, 30),
                            new Rect(12, 12, 76, 36),
                            new Rect(10, 10, 80, 40),
                            new Rect(5, 5, 90, 50))),
                    new ElementInspectionNode(
                        12,
                        "Text",
                        null,
                        null,
                        new Rect(10, 60, 100, 24),
                        new ElementInspectionState(false, false, false, false),
                        null,
                        "Description",
                        0,
                        [])
                ]));

        public bool IsRunning => true;
        public void RequestRender() => RequestRenderCount++;
        public Task InjectPointerAsync(DevToolsPointerInput input) => Task.CompletedTask;
        public Task InjectKeyAsync(DevToolsKeyInput input) => Task.CompletedTask;
        public Task InjectTextAsync(string text) => Task.CompletedTask;
        public Task InjectWheelAsync(DevToolsWheelInput input) => Task.CompletedTask;
        public Task<Bitmap> CaptureRendererBitmapAsync() => Task.FromResult(new Bitmap(1, 1));
        public Task<ElementInspectionSnapshot> CaptureInspectionSnapshotAsync(bool includeSourcePaths, bool includeTextContent) =>
            Task.FromResult(Snapshot);
        public Task<ElementInspectionNode?> InspectElementAsync(int debugId, bool includeSourcePaths, bool includeTextContent) =>
            Task.FromResult<ElementInspectionNode?>(debugId == 11 ? Snapshot.Root.Children[0] : null);
        public Task<bool> SetInspectorHighlightAsync(int debugId)
        {
            HighlightDebugId = debugId;
            RequestRender();
            return Task.FromResult(true);
        }
        public Task ClearInspectorHighlightAsync()
        {
            HighlightDebugId = null;
            RequestRender();
            return Task.CompletedTask;
        }
        public Task SetInspectorModeAsync(bool enabled)
        {
            InspectorModeEnabled = enabled;
            RequestRender();
            return Task.CompletedTask;
        }
        public Task<ElementInspectionStyleSnapshot?> InspectElementStylesAsync(int debugId) =>
            Task.FromResult<ElementInspectionStyleSnapshot?>(new(
                new Dictionary<string, string> { ["color"] = "red" },
                "color: red;",
                [new ElementInspectionStyleRule(
                    ".primary",
                    [new ElementInspectionStyleDeclaration("color", "red", false)])]));
        public Task<ElementInspectionNode?> HitTestInspectionAsync(Point point, bool includeSourcePaths, bool includeTextContent) =>
            Task.FromResult<ElementInspectionNode?>(null);
    }
}
