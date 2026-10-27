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

        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);

        await connection.SendAsync("{\"id\":1,\"method\":\"Runtime.enable\"}");
        using var runtimeResponse = await connection.ReceiveResponseAsync(1);
        Assert.Equal(1, runtimeResponse.RootElement.GetProperty("id").GetInt32());
        Assert.True(runtimeResponse.RootElement.GetProperty("result").ValueKind == JsonValueKind.Object);

        await connection.SendAsync("{\"id\":2,\"method\":\"DOM.getDocument\",\"params\":{\"depth\":-1}} ");
        using var documentResponse = await connection.ReceiveResponseAsync(2);
        var root = documentResponse.RootElement.GetProperty("result").GetProperty("root");
        Assert.Equal(9, root.GetProperty("nodeType").GetInt32());
        var squareRoot = root.GetProperty("children")[0];
        Assert.Equal("VIEW", squareRoot.GetProperty("nodeName").GetString());
        Assert.Equal(2, squareRoot.GetProperty("childNodeCount").GetInt32());
        Assert.Equal("BUTTON", squareRoot.GetProperty("children")[0].GetProperty("nodeName").GetString());
        var button = squareRoot.GetProperty("children")[0];
        var buttonNodeId = button.GetProperty("nodeId").GetInt32();
        Assert.NotEqual(root.GetProperty("backendNodeId").GetInt32(), button.GetProperty("backendNodeId").GetInt32());
        Assert.NotEqual(button.GetProperty("backendNodeId").GetInt32(), button.GetProperty("children")[0].GetProperty("backendNodeId").GetInt32());
        Assert.Equal("OK", squareRoot.GetProperty("children")[0].GetProperty("children")[0].GetProperty("nodeValue").GetString());
        var buttonAttributes = new Dictionary<string, string>();
        var attributeValues = squareRoot.GetProperty("children")[0].GetProperty("attributes").EnumerateArray().ToArray();
        for (var index = 0; index < attributeValues.Length; index += 2)
            buttonAttributes[attributeValues[index].GetString()!] = attributeValues[index + 1].GetString()!;
        Assert.Equal("primary rounded", buttonAttributes["class"]);

        await connection.SendAsync($"{{\"id\":3,\"method\":\"CSS.getComputedStyleForNode\",\"params\":{{\"nodeId\":{buttonNodeId}}}}}");
        using var styleResponse = await connection.ReceiveResponseAsync(3);
        var computedStyle = styleResponse.RootElement.GetProperty("result").GetProperty("computedStyle");
        Assert.Contains(computedStyle.EnumerateArray(), item => item.GetProperty("name").GetString() == "color");

        await connection.SendAsync($"{{\"id\":4,\"method\":\"CSS.getMatchedStylesForNode\",\"params\":{{\"nodeId\":{buttonNodeId}}}}}");
        using var matchedStyleResponse = await connection.ReceiveResponseAsync(4);
        var matchedRule = matchedStyleResponse.RootElement.GetProperty("result")
            .GetProperty("matchedCSSRules")[0].GetProperty("rule");
        Assert.Equal(".primary", matchedRule.GetProperty("selectorList").GetProperty("text").GetString());

        await connection.SendAsync($"{{\"id\":8,\"method\":\"DOM.getBoxModel\",\"params\":{{\"nodeId\":{buttonNodeId}}}}}");
        using var boxModelResponse = await connection.ReceiveResponseAsync(8);
        var boxModel = boxModelResponse.RootElement.GetProperty("result").GetProperty("model");
        Assert.Equal(new[] { 15f, 15f, 85f, 15f, 85f, 45f, 15f, 45f },
            boxModel.GetProperty("content").EnumerateArray().Select(value => value.GetSingle()).ToArray());
        Assert.Equal(new[] { 12f, 12f, 88f, 12f, 88f, 48f, 12f, 48f },
            boxModel.GetProperty("padding").EnumerateArray().Select(value => value.GetSingle()).ToArray());
        Assert.Equal(new[] { 10f, 10f, 90f, 10f, 90f, 50f, 10f, 50f },
            boxModel.GetProperty("border").EnumerateArray().Select(value => value.GetSingle()).ToArray());
        Assert.Equal(new[] { 5f, 5f, 95f, 5f, 95f, 55f, 5f, 55f },
            boxModel.GetProperty("margin").EnumerateArray().Select(value => value.GetSingle()).ToArray());

        await connection.SendAsync($"{{\"id\":5,\"method\":\"Overlay.highlightNode\",\"params\":{{\"nodeId\":{buttonNodeId}}}}}");
        using var highlightResponse = await connection.ReceiveResponseAsync(5);
        Assert.True(highlightResponse.RootElement.GetProperty("result").ValueKind == JsonValueKind.Object);
        Assert.Equal(11, runtime.HighlightDebugId);

        await connection.SendAsync("{\"id\":6,\"method\":\"Overlay.hideHighlight\"}");
        using var hideResponse = await connection.ReceiveResponseAsync(6);
        Assert.True(hideResponse.RootElement.GetProperty("result").ValueKind == JsonValueKind.Object);
        Assert.Null(runtime.HighlightDebugId);

        await connection.SendAsync("{\"id\":7,\"method\":\"Overlay.setInspectMode\",\"params\":{\"mode\":\"searchForNode\"}}");
        using var inspectResponse = await connection.ReceiveResponseAsync(7);
        Assert.True(inspectResponse.RootElement.GetProperty("result").ValueKind == JsonValueKind.Object);
        Assert.True(runtime.InspectorModeEnabled);

        await connection.SendAsync("{\"id\":9,\"method\":\"Overlay.disable\"}");
        using var disableResponse = await connection.ReceiveResponseAsync(9);
        Assert.True(disableResponse.RootElement.GetProperty("result").ValueKind == JsonValueKind.Object);
        Assert.False(runtime.InspectorModeEnabled);

        await connection.SendAsync("{\"id\":10,\"method\":\"Overlay.highlightRect\",\"params\":{\"x\":0,\"y\":0,\"width\":10,\"height\":10}}");
        using var highlightRectResponse = await connection.ReceiveResponseAsync(10);
        Assert.Equal(-32601, highlightRectResponse.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    internal static AppWindow CreateWindow(out StaticRuntime runtime)
    {
        var window = new AppWindow("CDP test");
        runtime = new StaticRuntime();
        window.BindApplication(new Dispatcher(), runtime);
        return window;
    }

    internal sealed class StaticRuntime : IAppWindowRuntime
    {
        public int? HighlightDebugId { get; private set; }
        public bool InspectorModeEnabled { get; private set; }

        private static readonly ElementInspectionStyleSnapshot Styles = new(
            new Dictionary<string, string> { ["color"] = "red" },
            "color: red;",
            [new ElementInspectionStyleRule(
                ".primary",
                [new ElementInspectionStyleDeclaration("color", "red", false)])]);

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
        public void RequestRender() { }
        public Task InjectPointerAsync(DevToolsPointerInput input) => Task.CompletedTask;
        public Task InjectKeyAsync(DevToolsKeyInput input) => Task.CompletedTask;
        public Task InjectTextAsync(string text) => Task.CompletedTask;
        public Task InjectWheelAsync(DevToolsWheelInput input) => Task.CompletedTask;
        public Task<Bitmap> CaptureRendererBitmapAsync() => Task.FromResult(new Bitmap(1, 1));
        public Task<ElementInspectionSnapshot> CaptureInspectionSnapshotAsync(bool includeSourcePaths, bool includeTextContent) =>
            Task.FromResult(Snapshot);
        public Task<ElementInspectionNode?> InspectElementAsync(int debugId, bool includeSourcePaths, bool includeTextContent) =>
            Task.FromResult<ElementInspectionNode?>(debugId == 11 ? Snapshot.Root.Children[0] : null);
        public Task<bool> SetInspectorHighlightAsync(
            int debugId,
            ElementInspectionHighlightOptions? options = null,
            long ownerId = 0,
            CancellationToken cancellationToken = default)
        {
            HighlightDebugId = debugId;
            RequestRender();
            return Task.FromResult(true);
        }
        public Task ClearInspectorHighlightAsync(long ownerId = 0, CancellationToken cancellationToken = default)
        {
            HighlightDebugId = null;
            RequestRender();
            return Task.CompletedTask;
        }
        public Task SetInspectorModeAsync(
            bool enabled,
            ElementInspectionHighlightOptions? options = null,
            long ownerId = 0,
            CancellationToken cancellationToken = default)
        {
            InspectorModeEnabled = enabled;
            RequestRender();
            return Task.CompletedTask;
        }
        public void ReleaseInspectorSession(long ownerId)
        {
        }
        public Task<ElementInspectionStyleSnapshot?> InspectElementStylesAsync(
            int debugId,
            bool includeStyleText = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ElementInspectionStyleSnapshot?>(debugId == 11 ? Styles : null);
        public Task<IReadOnlyList<ElementInspectionStyleText>> CaptureInspectionStyleSheetsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ElementInspectionStyleText?> InspectStyleSheetAsync(string styleSheetId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<ElementInspectionStyleText>> ApplyInspectionStyleEditsAsync(
            IReadOnlyList<ElementInspectionStyleTextEdit> edits,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<ElementInspectionComputedStyleSnapshot>> InspectComputedStylesAsync(
            IReadOnlyList<int> debugIds,
            IReadOnlyList<string>? propertyNames = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ElementInspectionComputedStyleSnapshot>>(debugIds
                .Where(debugId => debugId == 11)
                .Select(debugId => new ElementInspectionComputedStyleSnapshot(
                    debugId,
                    propertyNames == null
                        ? Styles.Computed
                        : Styles.Computed.Where(pair => propertyNames.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value)))
                .ToArray());
        public Task<ElementInspectionNode?> HitTestInspectionAsync(Point point, bool includeSourcePaths, bool includeTextContent) =>
            Task.FromResult<ElementInspectionNode?>(null);
    }
}
