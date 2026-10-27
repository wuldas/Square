using System.Text;
using System.Text.Json;
using Square.Controls;
using Square.DevTools;
using Xunit;

namespace Square.DevTools.Tests;

/// <summary>
/// CDP overlay behavior: highlight configuration parsing, live-element resolution for
/// text/document targets, inspect mode validation, and real native selection/takeover
/// routing through <see cref="InspectionTestHost"/>. Native drawing and raw-input
/// regression proofs are owned by the Square.UI.Tests inspector suites.
/// </summary>
public sealed class CdpOverlayTests
{
    // ------------------------------------------------------------------
    // Wire-level parsing and resolution against the static runtime host.
    // ------------------------------------------------------------------

    [Fact]
    public async Task HighlightRejectsInvalidConfigurationsWithoutClosingConnection()
    {
        var window = CdpSessionTests.CreateWindow(out _);
        await using var server = DevToolsServer.Start(window, new DevToolsOptions { AllowChromeInspect = true });
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);
        var (nodeId, _, _) = await RequestButtonIds(connection, elementId: "ok");

        foreach (var config in new[]
                 {
                     """{"contentColor":{"r":256,"g":10,"b":10}}""",
                     """{"contentColor":{"r":-1,"g":10,"b":10}}""",
                     """{"contentColor":{"r":10.5,"g":10,"b":10}}""",
                     """{"contentColor":{"g":10,"b":10}}""",
                     """{"contentColor":{"r":"10","g":10,"b":10}}""",
                     """{"contentColor":{"r":10,"g":10,"b":10,"a":1.5}}""",
                     """{"contentColor":{"r":10,"g":10,"b":10,"a":-0.5}}""",
                     """{"contentColor":"#ff0000"}""",
                     """{"marginColor":{"r":10,"g":10,"b":10},"borderColor":5}""",
                     "5",
                     "null",
                     """{"contentColor":null}""",
                     """{"contentColor":{"r":10,"g":10,"b":10,"a":null}}""",
                 })
        {
            using var response = await connection.RequestAsync("Overlay.highlightNode",
                new { nodeId, highlightConfig = JsonSerializer.Deserialize<JsonElement>(config) });
            AssertError(response, -32602, "Invalid highlight configuration.");
        }

        // The errors are parameter-level: the same connection keeps serving commands.
        using var followUp = await connection.RequestAsync("Overlay.hideHighlight");
        Assert.True(followUp.RootElement.TryGetProperty("result", out _));
    }


    [Fact]
    public async Task HighlightRejectsUnknownNodesAndMissingTarget()
    {
        var window = CdpSessionTests.CreateWindow(out _);
        await using var server = DevToolsServer.Start(window, new DevToolsOptions { AllowChromeInspect = true });
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);
        using var unknown = await connection.RequestAsync("Overlay.highlightNode", new { backendNodeId = 9999 });
        AssertError(unknown, -32000, "Node not found.");
        using var missingTarget = await connection.RequestAsync("Overlay.highlightNode");
        AssertError(missingTarget, -32602, "'nodeId' or 'backendNodeId' is required.");
    }

    [Fact]
    public async Task SetInspectModeValidatesModeAndNeverEnablesCaptureAreaInspection()
    {
        var window = CdpSessionTests.CreateWindow(out var runtime);
        await using var server = DevToolsServer.Start(window, new DevToolsOptions { AllowChromeInspect = true });
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);

        using (var missing = await connection.RequestAsync("Overlay.setInspectMode"))
            AssertError(missing, -32602, "Invalid inspect mode.");
        using (var nonString = await connection.RequestAsync("Overlay.setInspectMode", new { mode = 7 }))
            AssertError(nonString, -32602, "Invalid inspect mode.");
        using (var unknown = await connection.RequestAsync("Overlay.setInspectMode", new { mode = "searchForElement" }))
            AssertError(unknown, -32602, "Invalid inspect mode.");
        using (var capture = await connection.RequestAsync("Overlay.setInspectMode", new { mode = "captureAreaScreenshot" }))
            AssertError(capture, -32601, "Overlay capture-area inspection is not supported.");
        Assert.False(runtime.InspectorModeEnabled);

        using (var shadow = await connection.RequestAsync("Overlay.setInspectMode", new { mode = "searchForUAShadowDOM" }))
            Assert.True(shadow.RootElement.TryGetProperty("result", out _));
        Assert.True(runtime.InspectorModeEnabled);
        using (var none = await connection.RequestAsync("Overlay.setInspectMode", new { mode = "none" }))
            Assert.True(none.RootElement.TryGetProperty("result", out _));
        Assert.False(runtime.InspectorModeEnabled);
    }

    // ------------------------------------------------------------------
    // Real native application behavior through InspectionTestHost.
    // ------------------------------------------------------------------

    [Fact]
    public async Task InspectModeSelectionReachesOwnerAndEscapeCancels()
    {
        using var host = CreateHost();
        await using var server = StartInspectableServer(host);
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);
        var (_, _, center) = await RequestButtonIds(connection, server);

        using (await connection.RequestAsync("Overlay.setInspectMode", new { mode = "searchForNode" })) { }
        await PostInputAsync(server, "input/pointer", PointerPayload(center, "Move"));
        await PostInputAsync(server, "input/pointer", PointerPayload(center, "Down"));
        using (var selected = await connection.ReceiveEventAsync("Overlay.inspectNodeRequested"))
        {
            // The event carries this session's backend mapping for the selected native node.
            var backendNodeId = selected.RootElement.GetProperty("params").GetProperty("backendNodeId").GetInt32();
            using var described = await connection.RequestAsync("DOM.describeNode", new { backendNodeId });
            Assert.Equal("BUTTON",
                described.RootElement.GetProperty("result").GetProperty("node").GetProperty("nodeName").GetString());
        }
        await PostInputAsync(server, "input/pointer", PointerPayload(center, "Up"));

        using (await connection.RequestAsync("Overlay.setInspectMode", new { mode = "searchForNode" })) { }
        await PostInputAsync(server, "input/key", """{"keyCode":27,"action":"Down"}""");
        using (var canceled = await connection.ReceiveEventAsync("Overlay.inspectModeCanceled"))
            Assert.True(canceled.RootElement.GetProperty("params").ValueKind == JsonValueKind.Object);

        // After cancellation normal input flows again and selects nothing.
        await PostInputAsync(server, "input/pointer", PointerPayload(center, "Down"));
        await Task.Delay(250);
        using var probe = await connection.RequestAsync("Page.enable");
        Assert.False(connection.HasBufferedEvent("Overlay.inspectNodeRequested"));
    }

    [Fact]
    public async Task NewConnectionTakeoverCancelsOldOwnerAndRoutesSelection()
    {
        using var host = CreateHost();
        await using var server = StartInspectableServer(host);
        using var first = new CdpConnection();
        await first.ConnectAsync(server);
        using var second = new CdpConnection();
        await second.ConnectAsync(server);
        var (_, _, center) = await RequestButtonIds(first, server);

        using (await first.RequestAsync("Overlay.setInspectMode", new { mode = "searchForNode" })) { }
        using (await second.RequestAsync("Overlay.setInspectMode", new { mode = "searchForNode" })) { }

        // Taking over cancels only the previous owner's inspection.
        using (var canceled = await first.ReceiveEventAsync("Overlay.inspectModeCanceled"))
            Assert.True(canceled.RootElement.GetProperty("params").ValueKind == JsonValueKind.Object);
        Assert.False(second.HasBufferedEvent("Overlay.inspectModeCanceled"));

        await PostInputAsync(server, "input/pointer", PointerPayload(center, "Down"));
        using (var selected = await second.ReceiveEventAsync("Overlay.inspectNodeRequested"))
        {
            var backendNodeId = selected.RootElement.GetProperty("params").GetProperty("backendNodeId").GetInt32();
            using var described = await second.RequestAsync("DOM.describeNode", new { backendNodeId });
            Assert.Equal("BUTTON",
                described.RootElement.GetProperty("result").GetProperty("node").GetProperty("nodeName").GetString());
        }
        await Task.Delay(250);
        using var firstProbe = await first.RequestAsync("Page.enable");
        Assert.False(first.HasBufferedEvent("Overlay.inspectNodeRequested"));
    }

    [Fact]
    public async Task DisconnectingOldOwnerKeepsNewOwnerInspectionAlive()
    {
        using var host = CreateHost();
        await using var server = StartInspectableServer(host);
        using var first = new CdpConnection();
        await first.ConnectAsync(server);
        using var second = new CdpConnection();
        await second.ConnectAsync(server);
        var (_, _, center) = await RequestButtonIds(first, server);

        using (await first.RequestAsync("Overlay.setInspectMode", new { mode = "searchForNode" })) { }
        using (await second.RequestAsync("Overlay.setInspectMode", new { mode = "searchForNode" })) { }
        using (await first.ReceiveEventAsync("Overlay.inspectModeCanceled")) { }

        // The disconnected owner's release must not clear the new owner's inspection.
        first.Dispose();
        await PostInputAsync(server, "input/pointer", PointerPayload(center, "Down"));
        using (var selected = await second.ReceiveEventAsync("Overlay.inspectNodeRequested"))
        {
            var backendNodeId = selected.RootElement.GetProperty("params").GetProperty("backendNodeId").GetInt32();
            using var described = await second.RequestAsync("DOM.describeNode", new { backendNodeId });
            Assert.Equal("BUTTON",
                described.RootElement.GetProperty("result").GetProperty("node").GetProperty("nodeName").GetString());
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static DevToolsServer StartInspectableServer(InspectionTestHost host) =>
        DevToolsServer.Start(host.Window, new DevToolsOptions
        {
            AllowChromeInspect = true,
            AllowInputInjection = true
        });

    private static InspectionTestHost CreateHost()
    {
        return new InspectionTestHost(window =>
        {
            var content = new View { Id = "content" };
            content.Children.Add(new Button("Activity") { Id = "activity" });
            window.Load(content);
        }, render: true);
    }

    private static string PointerPayload((float X, float Y) center, string action) =>
        $$"""{"x":{{center.X}},"y":{{center.Y}},"action":"{{action}}"}""";

    /// <summary>Requests the document, then a button's node/backend IDs and live box center.</summary>
    private static async Task<(int NodeId, int BackendNodeId, (float X, float Y) Center)> RequestButtonIds(
        CdpConnection connection, DevToolsServer? server = null, string elementId = "activity")
    {
        using var document = await connection.RequestAsync("DOM.getDocument", new { depth = -1 });
        var button = FindByElementId(document.RootElement.GetProperty("result").GetProperty("root"), elementId);
        var nodeId = button.GetProperty("nodeId").GetInt32();
        var backendNodeId = button.GetProperty("backendNodeId").GetInt32();
        float x = 0, y = 0;
        if (server != null)
        {
            using var box = await connection.RequestAsync("DOM.getBoxModel", new { nodeId });
            var content = box.RootElement.GetProperty("result").GetProperty("model").GetProperty("content")
                .EnumerateArray().Select(value => value.GetSingle()).ToArray();
            x = (content[0] + content[2]) / 2f;
            y = (content[1] + content[5]) / 2f;
        }
        return (nodeId, backendNodeId, (x, y));
    }

    private static JsonElement FindByElementId(JsonElement root, string id)
    {
        foreach (var node in Flatten(root))
        {
            if (!node.TryGetProperty("attributes", out var attributes)) continue;
            for (var i = 0; i + 1 < attributes.GetArrayLength(); i += 2)
            {
                if (attributes[i].GetString() == "id" && attributes[i + 1].GetString() == id)
                    return node;
            }
        }
        throw new InvalidOperationException($"Element '{id}' was not published.");
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement root)
    {
        yield return root;
        if (!root.TryGetProperty("children", out var children)) yield break;
        foreach (var child in children.EnumerateArray())
            foreach (var node in Flatten(child))
                yield return node;
    }

    private static async Task PostInputAsync(DevToolsServer server, string path, string json)
    {
        using var client = new HttpClient { BaseAddress = new Uri(server.BaseAddress) };
        client.DefaultRequestHeaders.Add(DevToolsServer.TokenHeader, server.AccessToken);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync($"/api/v1/{path}", content);
        response.EnsureSuccessStatusCode();
    }

    private static void AssertError(JsonDocument response, int code, string message)
    {
        var error = response.RootElement.GetProperty("error");
        Assert.Equal(code, error.GetProperty("code").GetInt32());
        Assert.Equal(message, error.GetProperty("message").GetString());
    }
}
