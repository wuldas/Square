using System.Text.Json;
using Square.Controls;
using Square.UI;
using Xunit;

namespace Square.DevTools.Tests;

/// <summary>
/// CSS computed tracking tests over a real rendered window: ForNode throttling, batch
/// long-poll take semantics and published-node scoping all run against actual computed
/// values from the native computed API.
/// </summary>
public sealed class CdpComputedTrackingTests
{
    [Fact]
    public async Task GetComputedStyleForNodeReadsPublishedLiveNode()
    {
        using var host = CreateHost(out _, out _);
        await using var server = DevToolsServer.Start(host.Window, new() { AllowChromeInspect = true });
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);
        using var document = await connection.RequestAsync("DOM.getDocument", new { depth = -1 });
        var nodeId = CdpDomTests.FindElement(document.RootElement.GetProperty("result").GetProperty("root"), "first")
            .GetProperty("nodeId").GetInt32();
        using (await connection.RequestAsync("CSS.enable")) { }
        using var computed = await connection.RequestAsync("CSS.getComputedStyleForNode", new { nodeId });
        Assert.Contains(computed.RootElement.GetProperty("result").GetProperty("computedStyle").EnumerateArray(),
            property => property.GetProperty("name").GetString() == "color" &&
                        property.GetProperty("value").GetString() == "red");
        using var unknown = await connection.RequestAsync("CSS.getComputedStyleForNode", new { nodeId = 987654 });
        AssertError(unknown, -32000, "Node not found.");
    }

    [Fact]
    public async Task TakeBeforeAnyTrackingIsRejected()
    {
        using var host = CreateHost(out _, out _);
        await using var server = DevToolsServer.Start(host.Window, new() { AllowChromeInspect = true });
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);
        using var take = await connection.RequestAsync("CSS.takeComputedStyleUpdates");
        AssertError(take, -32000, "No computed styles are being tracked right now.");
    }

    [Fact]
    public async Task ForNodeTrackingSendsThrottledUpdatesAndStopsWhenOmitted()
    {
        using var host = CreateHost(out var first, out _);
        await using var server = DevToolsServer.Start(host.Window, new() { AllowChromeInspect = true });
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);
        using var document = await connection.RequestAsync("DOM.getDocument", new { depth = -1 });
        var nodeId = CdpDomTests.FindElement(document.RootElement.GetProperty("result").GetProperty("root"), "first")
            .GetProperty("nodeId").GetInt32();
        using (await connection.RequestAsync("CSS.enable")) { }
        using (await connection.RequestAsync("CSS.trackComputedStyleUpdatesForNode", new { nodeId })) { }
        using (var invalid = await connection.RequestAsync("CSS.trackComputedStyleUpdatesForNode", new { nodeId = 987654 }))
            AssertError(invalid, -32000, "Node not found.");
        // Re-point the target back after the failed request.
        using (await connection.RequestAsync("CSS.trackComputedStyleUpdatesForNode", new { nodeId })) { }

        await host.InvokeAsync(() => first.Style.SetProperty("color", "blue"));
        using (var barrier = await connection.ReceiveEventAsync("DOM.attributeModified"))
            Assert.Equal("style", barrier.RootElement.GetProperty("params").GetProperty("name").GetString());
        using (var updated = await connection.ReceiveEventAsync("CSS.computedStyleUpdated"))
            Assert.Equal(nodeId, updated.RootElement.GetProperty("params").GetProperty("nodeId").GetInt32());
        await host.InvokeAsync(() => first.Style.SetProperty("color", "green"));
        using (var barrier = await connection.ReceiveEventAsync("DOM.attributeModified"))
            Assert.Equal("style", barrier.RootElement.GetProperty("params").GetProperty("name").GetString());
        using (var updated = await connection.ReceiveEventAsync("CSS.computedStyleUpdated"))
            Assert.Equal(nodeId, updated.RootElement.GetProperty("params").GetProperty("nodeId").GetInt32());

        // Omitting nodeId stops tracking: the next change produces no computed event.
        using (await connection.RequestAsync("CSS.trackComputedStyleUpdatesForNode")) { }
        await host.InvokeAsync(() => first.Style.SetProperty("color", "yellow"));
        using (var barrier = await connection.ReceiveEventAsync("DOM.attributeModified"))
            Assert.Equal("style", barrier.RootElement.GetProperty("params").GetProperty("name").GetString());
        Assert.False(connection.HasBufferedEvent("CSS.computedStyleUpdated"));
    }

    [Fact]
    public async Task ForNodeSwitchReplacesTheTrackedTarget()
    {
        using var host = CreateHost(out var first, out var second);
        await using var server = DevToolsServer.Start(host.Window, new() { AllowChromeInspect = true });
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);
        using var document = await connection.RequestAsync("DOM.getDocument", new { depth = -1 });
        var root = document.RootElement.GetProperty("result").GetProperty("root");
        var firstId = CdpDomTests.FindElement(root, "first").GetProperty("nodeId").GetInt32();
        var secondId = CdpDomTests.FindElement(root, "second").GetProperty("nodeId").GetInt32();
        using (await connection.RequestAsync("CSS.enable")) { }
        using (await connection.RequestAsync("CSS.trackComputedStyleUpdatesForNode", new { nodeId = firstId })) { }

        await host.InvokeAsync(() => first.Style.SetProperty("color", "orange"));
        using (var barrier = await connection.ReceiveEventAsync("DOM.attributeModified"))
            Assert.Equal("style", barrier.RootElement.GetProperty("params").GetProperty("name").GetString());
        using (var updated = await connection.ReceiveEventAsync("CSS.computedStyleUpdated"))
            Assert.Equal(firstId, updated.RootElement.GetProperty("params").GetProperty("nodeId").GetInt32());

        using (await connection.RequestAsync("CSS.trackComputedStyleUpdatesForNode", new { nodeId = secondId })) { }
        await host.InvokeAsync(() => first.Style.SetProperty("color", "purple"));
        using (var barrier = await connection.ReceiveEventAsync("DOM.attributeModified"))
            Assert.Equal("style", barrier.RootElement.GetProperty("params").GetProperty("name").GetString());
        Assert.False(connection.HasBufferedEvent("CSS.computedStyleUpdated"));

        await host.InvokeAsync(() => second.Style.SetProperty("color", "teal"));
        using (var barrier = await connection.ReceiveEventAsync("DOM.attributeModified"))
            Assert.Equal("style", barrier.RootElement.GetProperty("params").GetProperty("name").GetString());
        using (var updated = await connection.ReceiveEventAsync("CSS.computedStyleUpdated"))
            Assert.Equal(secondId, updated.RootElement.GetProperty("params").GetProperty("nodeId").GetInt32());
    }

    [Fact]
    public async Task LongPollTakeCompletesAfterEditAndDomCommands()
    {
        using var host = CreateHost(out _, out _);
        await using var server = DevToolsServer.Start(host.Window, new()
        {
            AllowChromeInspect = true,
            AllowChromeStyleEditing = true
        });
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);
        using var document = await connection.RequestAsync("DOM.getDocument", new { depth = -1 });
        var nodeId = CdpDomTests.FindElement(document.RootElement.GetProperty("result").GetProperty("root"), "first")
            .GetProperty("nodeId").GetInt32();
        using (await connection.RequestAsync("CSS.enable")) { }
        using var matched = await connection.RequestAsync("CSS.getMatchedStylesForNode", new { nodeId });
        var inlineStyle = matched.RootElement.GetProperty("result").GetProperty("inlineStyle");
        var inlineId = inlineStyle.GetProperty("styleSheetId").GetString();
        var range = inlineStyle.GetProperty("range");

        using (await connection.RequestAsync("CSS.trackComputedStyleUpdates", new
        {
            propertiesToTrack = new[] { new { name = "color", value = "red" } }
        })) { }

        // Long poll: the take registers without responding and later commands still run.
        const int takeId = 4242;
        await connection.SendAsync("{\"id\":4242,\"method\":\"CSS.takeComputedStyleUpdates\"}");
        using (var domDuringPoll = await connection.RequestAsync("DOM.getDocument", new { depth = -1 }))
            Assert.True(domDuringPoll.RootElement.TryGetProperty("result", out _));

        using var edit = await connection.RequestAsync("CSS.setStyleTexts", new
        {
            edits = new[]
            {
                new
                {
                    styleSheetId = inlineId,
                    range = new
                    {
                        startLine = range.GetProperty("startLine").GetInt32(),
                        startColumn = range.GetProperty("startColumn").GetInt32(),
                        endLine = range.GetProperty("endLine").GetInt32(),
                        endColumn = range.GetProperty("endColumn").GetInt32()
                    },
                    text = "color: blue;"
                }
            }
        });
        Assert.True(edit.RootElement.TryGetProperty("result", out _));
        using var take = await connection.ReceiveResponseAsync(takeId);
        Assert.Equal(new[] { nodeId }, take.RootElement.GetProperty("result").GetProperty("nodeIds")
            .EnumerateArray().Select(value => value.GetInt32()).ToArray());
    }

    [Fact]
    public async Task BlueToGreenDoesNotQualifyAndSecondTakeErrors()
    {
        using var host = CreateHost(out _, out _);
        await using var server = DevToolsServer.Start(host.Window, new()
        {
            AllowChromeInspect = true,
            AllowChromeStyleEditing = true
        });
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);
        using var document = await connection.RequestAsync("DOM.getDocument", new { depth = -1 });
        var nodeId = CdpDomTests.FindElement(document.RootElement.GetProperty("result").GetProperty("root"), "first")
            .GetProperty("nodeId").GetInt32();
        using (await connection.RequestAsync("CSS.enable")) { }
        using var matched = await connection.RequestAsync("CSS.getMatchedStylesForNode", new { nodeId });
        var inlineStyle = matched.RootElement.GetProperty("result").GetProperty("inlineStyle");
        var inlineId = inlineStyle.GetProperty("styleSheetId").GetString();

        using (await connection.RequestAsync("CSS.trackComputedStyleUpdates", new
        {
            propertiesToTrack = new[] { new { name = "color", value = "red" } }
        })) { }
        const int firstTake = 5001;
        const int secondTake = 5002;
        await connection.SendAsync($"{{\"id\":{firstTake},\"method\":\"CSS.takeComputedStyleUpdates\"}}");

        // red -> blue qualifies: the old value is in the allowed set.
        await EditInlineAsync(connection, inlineId, "color: blue;");
        using (var take = await connection.ReceiveResponseAsync(firstTake))
            Assert.Equal(new[] { nodeId }, take.RootElement.GetProperty("result").GetProperty("nodeIds")
                .EnumerateArray().Select(value => value.GetInt32()).ToArray());

        // Second take registers; blue -> green matches neither allowed value.
        await connection.SendAsync($"{{\"id\":{secondTake},\"method\":\"CSS.takeComputedStyleUpdates\"}}");
        await EditInlineAsync(connection, inlineId, "color: green;");

        // A second concurrent take is rejected while the first is still pending.
        using (var duplicate = await connection.RequestAsync("CSS.takeComputedStyleUpdates"))
            AssertError(duplicate, -32000, "A previous request has not been resolved yet.");

        // The empty configuration completes the pending take with the current set...
        using (await connection.RequestAsync("CSS.trackComputedStyleUpdates", new { propertiesToTrack = Array.Empty<object>() })) { }
        using (var completed = await connection.ReceiveResponseAsync(secondTake))
            Assert.Empty(completed.RootElement.GetProperty("result").GetProperty("nodeIds").EnumerateArray());

        // ...and stopped tracking makes further takes an error.
        using (var stopped = await connection.RequestAsync("CSS.takeComputedStyleUpdates"))
            AssertError(stopped, -32000, "No computed styles are being tracked right now.");
    }

    [Fact]
    public async Task DeletedNodesNeverAppearInTakeResults()
    {
        using var host = CreateHost(out var first, out _);
        await using var server = DevToolsServer.Start(host.Window, new() { AllowChromeInspect = true });
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);
        using var document = await connection.RequestAsync("DOM.getDocument", new { depth = -1 });
        var firstId = CdpDomTests.FindElement(document.RootElement.GetProperty("result").GetProperty("root"), "first")
            .GetProperty("nodeId").GetInt32();
        using (await connection.RequestAsync("CSS.enable")) { }
        using (await connection.RequestAsync("CSS.trackComputedStyleUpdates", new
        {
            propertiesToTrack = new[] { new { name = "color", value = "red" } }
        })) { }
        const int takeId = 6001;
        await connection.SendAsync($"{{\"id\":{takeId},\"method\":\"CSS.takeComputedStyleUpdates\"}}");

        // Deleting the node removes its baseline and pending entries; the replacement is
        // published with a baseline only and its first real change qualifies.
        Button? replacement = null;
        await host.InvokeAsync(() =>
        {
            var parent = first.Parent!;
            parent.Children.Remove(first);
            replacement = new Button("Replacement") { Id = "replacement" };
            replacement.Style.SetProperty("color", "red");
            parent.Children.Add(replacement);
        });
        using (var removed = await connection.ReceiveEventAsync("DOM.childNodeRemoved"))
            Assert.Equal(firstId, removed.RootElement.GetProperty("params").GetProperty("nodeId").GetInt32());
        int replacementId;
        using (var inserted = await connection.ReceiveEventAsync("DOM.childNodeInserted"))
            replacementId = inserted.RootElement.GetProperty("params").GetProperty("node").GetProperty("nodeId").GetInt32();

        // Changing the replacement's own inline color through the host qualifies it.
        var target = replacement!;
        await host.InvokeAsync(() => target.Style.SetProperty("color", "blue"));
        using (var barrier = await connection.ReceiveEventAsync("DOM.attributeModified"))
            Assert.Equal("style", barrier.RootElement.GetProperty("params").GetProperty("name").GetString());
        using (var completed = await connection.ReceiveResponseAsync(takeId))
        {
            var nodeIds = completed.RootElement.GetProperty("result").GetProperty("nodeIds")
                .EnumerateArray().Select(value => value.GetInt32()).ToArray();
            Assert.Equal(new[] { replacementId }, nodeIds);
            Assert.DoesNotContain(firstId, nodeIds);
        }
    }

    private static async Task EditInlineAsync(CdpConnection connection, string? inlineId, string text)
    {
        // The inline resource text is the declaration block itself, so its live extent
        // is a whole-block SourceRange over the current text.
        using var owner = await connection.RequestAsync("CSS.getStyleSheetText", new { styleSheetId = inlineId });
        var body = owner.RootElement.GetProperty("result").GetProperty("text").GetString();
        var range = RangeOfFullText(body ?? "");
        using var edit = await connection.RequestAsync("CSS.setStyleTexts", new
        {
            edits = new[] { new { styleSheetId = inlineId, range, text } }
        });
        Assert.True(edit.RootElement.TryGetProperty("result", out _));
    }

    private static object RangeOfFullText(string text)
    {
        var line = 0;
        var lastNewline = -1;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n') continue;
            line++;
            lastNewline = index;
        }
        return new { startLine = 0, startColumn = 0, endLine = line, endColumn = text.Length - lastNewline - 1 };
    }

    private static InspectionTestHost CreateHost(out Button first, out Button second)
    {
        Button? firstButton = null;
        Button? secondButton = null;
        var host = new InspectionTestHost(window =>
        {
            var page = new View { Id = "page" };
            firstButton = new Button("First") { Id = "first" };
            firstButton.Style.SetProperty("color", "red");
            secondButton = new Button("Second") { Id = "second" };
            secondButton.Style.SetProperty("color", "blue");
            page.Children.Add(firstButton);
            page.Children.Add(secondButton);
            window.Load(page);
        }, render: true);
        first = firstButton!;
        second = secondButton!;
        return host;
    }

    private static void AssertError(JsonDocument response, int code, string message)
    {
        var error = response.RootElement.GetProperty("error");
        Assert.Equal(code, error.GetProperty("code").GetInt32());
        Assert.Equal(message, error.GetProperty("message").GetString());
    }
}
