using System.Text.Json;
using Square.Controls;
using Square.UI;
using Xunit;

namespace Square.DevTools.Tests;

public sealed class CdpDomTests
{
    [Fact]
    public async Task DepthAndReverseSelectionPublishAncestorsWithoutReusingBackendIds()
    {
        using var host = CreateHost(out _, out _, out _);
        await using var server = DevToolsServer.Start(host.Window, new() { AllowChromeInspect = true });
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);
        using (var notRequested = await connection.RequestAsync("DOM.pushNodesByBackendIdsToFrontend", new { backendNodeIds = new[] { 7 } }))
            AssertError(notRequested, -32000, "Document has not been requested.");
        using var full = await connection.RequestAsync("DOM.getDocument", new { depth = -1 });
        var root = full.RootElement.GetProperty("result").GetProperty("root");
        var deep = FindElement(root, "activity");
        var backendId = deep.GetProperty("backendNodeId").GetInt32();
        var frontendId = deep.GetProperty("nodeId").GetInt32();
        var allBackendIds = Flatten(root).Select(node => node.GetProperty("backendNodeId").GetInt32()).ToArray();
        Assert.Equal(allBackendIds.Length, allBackendIds.Distinct().Count());
        using var shallow = await connection.RequestAsync("DOM.getDocument");
        var shallowRoot = shallow.RootElement.GetProperty("result").GetProperty("root");
        Assert.False(shallowRoot.GetProperty("children")[0].TryGetProperty("children", out _));
        using var pushed = await connection.RequestAsync("DOM.pushNodesByBackendIdsToFrontend", new { backendNodeIds = new[] { backendId, int.MaxValue, backendId } });
        Assert.Equal(new[] { frontendId, 0, frontendId }, pushed.RootElement.GetProperty("result").GetProperty("nodeIds").EnumerateArray().Select(id => id.GetInt32()));
        var known = Flatten(shallowRoot).Select(node => node.GetProperty("nodeId").GetInt32()).ToHashSet();
        while (!known.Contains(frontendId))
        {
            using var ancestors = await connection.ReceiveEventAsync("DOM.setChildNodes");
            var data = ancestors.RootElement.GetProperty("params");
            Assert.Contains(data.GetProperty("parentId").GetInt32(), known);
            foreach (var child in data.GetProperty("nodes").EnumerateArray())
                foreach (var published in Flatten(child)) known.Add(published.GetProperty("nodeId").GetInt32());
        }
        using var described = await connection.RequestAsync("DOM.describeNode", new { backendNodeId = backendId });
        Assert.Equal(frontendId, described.RootElement.GetProperty("result").GetProperty("node").GetProperty("nodeId").GetInt32());
        foreach (var depth in new[] { 0, -2 })
        {
            using var invalid = await connection.RequestAsync("DOM.getDocument", new { depth });
            AssertError(invalid, -32602, "Invalid depth.");
        }
        using var fractional = await connection.RequestAsync("DOM.getDocument", new { depth = 1.5 });
        AssertError(fractional, -32602, "Invalid depth.");
        using var flattened = await connection.RequestAsync("DOM.getFlattenedDocument", new { depth = -1 });
        Assert.Contains(flattened.RootElement.GetProperty("result").GetProperty("nodes").EnumerateArray(), node => node.GetProperty("nodeId").GetInt32() == frontendId);
        using var badNode = await connection.RequestAsync("DOM.describeNode", new { backendNodeId = int.MaxValue });
        AssertError(badNode, -32000, "Node not found.");
        using var badParams = await connection.RequestAsync("DOM.describeNode", new { nodeId = "bad" });
        Assert.Equal(-32602, badParams.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        using var valid = await connection.RequestAsync("DOM.describeNode", new { nodeId = frontendId });
        Assert.Equal("BUTTON", valid.RootElement.GetProperty("result").GetProperty("node").GetProperty("nodeName").GetString());
    }

    [Fact]
    public async Task NativeAttributesTextMovesAndDeletionUpdatePublishedTree()
    {
        using var host = CreateHost(out var left, out var right, out var button);
        await using var server = DevToolsServer.Start(host.Window, new() { AllowChromeInspect = true });
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);
        using var document = await connection.RequestAsync("DOM.getDocument", new { depth = -1 });
        var root = document.RootElement.GetProperty("result").GetProperty("root");
        var node = FindElement(root, "activity");
        var nodeId = node.GetProperty("nodeId").GetInt32();
        var backendId = node.GetProperty("backendNodeId").GetInt32();
        var rightId = FindElement(root, "right").GetProperty("nodeId").GetInt32();
        var leftId = FindElement(root, "left").GetProperty("nodeId").GetInt32();
        using var resolved = await connection.RequestAsync("DOM.resolveNode", new { nodeId });
        var objectId = resolved.RootElement.GetProperty("result").GetProperty("object").GetProperty("objectId").GetString();
        await host.InvokeAsync(() => button.ClassList.Add("selected"));
        using (var attribute = await connection.ReceiveEventAsync("DOM.attributeModified"))
        {
            var data = attribute.RootElement.GetProperty("params");
            Assert.Equal(nodeId, data.GetProperty("nodeId").GetInt32());
            Assert.Equal("class", data.GetProperty("name").GetString());
            Assert.Contains("selected", data.GetProperty("value").GetString());
        }
        await host.InvokeAsync(() => button.TextContent = "Changed activity");
        using (var text = await connection.ReceiveEventAsync("DOM.characterDataModified"))
            Assert.Equal("Changed activity", text.RootElement.GetProperty("params").GetProperty("characterData").GetString());
        await host.InvokeAsync(() => button.Style.SetProperty("color", "blue"));
        using (var style = await connection.ReceiveEventAsync("DOM.attributeModified"))
            Assert.Equal("style", style.RootElement.GetProperty("params").GetProperty("name").GetString());
        await host.InvokeAsync(() =>
        {
            left.Children.Remove(button);
            right.Children.Add(button);
        });
        using (var removed = await connection.ReceiveEventAsync("DOM.childNodeRemoved"))
            Assert.Equal(nodeId, removed.RootElement.GetProperty("params").GetProperty("nodeId").GetInt32());
        using (var inserted = await connection.ReceiveEventAsync("DOM.childNodeInserted"))
        {
            var data = inserted.RootElement.GetProperty("params");
            Assert.Equal(rightId, data.GetProperty("parentNodeId").GetInt32());
            Assert.Equal(nodeId, data.GetProperty("node").GetProperty("nodeId").GetInt32());
            Assert.Equal(backendId, data.GetProperty("node").GetProperty("backendNodeId").GetInt32());
        }
        await host.InvokeAsync(() =>
        {
            right.Children.Remove(button);
            left.Children.Add(button);
        });
        using (var removed = await connection.ReceiveEventAsync("DOM.childNodeRemoved"))
            Assert.Equal(rightId, removed.RootElement.GetProperty("params").GetProperty("parentNodeId").GetInt32());
        using (var inserted = await connection.ReceiveEventAsync("DOM.childNodeInserted"))
        {
            var data = inserted.RootElement.GetProperty("params");
            Assert.Equal(leftId, data.GetProperty("parentNodeId").GetInt32());
            Assert.Equal(nodeId, data.GetProperty("node").GetProperty("nodeId").GetInt32());
        }
        await host.InvokeAsync(() => button.RemoveProperty(nameof(Element.Id)));
        using (var removedAttribute = await connection.ReceiveEventAsync("DOM.attributeRemoved"))
            Assert.Equal("id", removedAttribute.RootElement.GetProperty("params").GetProperty("name").GetString());
        await host.InvokeAsync(() => left.Children.Remove(button));
        using (var removed = await connection.ReceiveEventAsync("DOM.childNodeRemoved"))
            Assert.Equal(nodeId, removed.RootElement.GetProperty("params").GetProperty("nodeId").GetInt32());
        await host.InvokeAsync(() => right.Children.Add(new Button("Replacement") { Id = "replacement" }));
        using (var inserted = await connection.ReceiveEventAsync("DOM.childNodeInserted"))
            Assert.NotEqual(nodeId, inserted.RootElement.GetProperty("params").GetProperty("node").GetProperty("nodeId").GetInt32());
        using var stale = await connection.RequestAsync("DOM.describeNode", new { nodeId });
        AssertError(stale, -32000, "Node not found.");
        using var staleObject = await connection.RequestAsync("DOM.describeNode", new { objectId });
        AssertError(staleObject, -32000, "Node not found.");
        using var missing = await connection.RequestAsync("DOM.pushNodesByBackendIdsToFrontend", new { backendNodeIds = new[] { backendId } });
        Assert.Equal(0, missing.RootElement.GetProperty("result").GetProperty("nodeIds")[0].GetInt32());
    }

    [Fact]
    public async Task RepeatedEnableKeepsLiveNodesAndDisableStopsAttributeEvents()
    {
        using var host = CreateHost(out _, out _, out var button);
        await using var server = DevToolsServer.Start(host.Window, new() { AllowChromeInspect = true });
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);
        using var enabled = await connection.RequestAsync("DOM.enable");
        using var initialized = await connection.ReceiveEventAsync("DOM.documentUpdated");
        using var document = await connection.RequestAsync("DOM.getDocument", new { depth = -1 });
        var nodeId = FindElement(document.RootElement.GetProperty("result").GetProperty("root"), "activity").GetProperty("nodeId").GetInt32();
        using var repeated = await connection.RequestAsync("DOM.enable");
        using var described = await connection.RequestAsync("DOM.describeNode", new { nodeId });
        Assert.Equal(nodeId, described.RootElement.GetProperty("result").GetProperty("node").GetProperty("nodeId").GetInt32());
        Assert.False(connection.HasBufferedEvent("DOM.documentUpdated"));
        using var disabled = await connection.RequestAsync("DOM.disable");
        await host.InvokeAsync(() => button.Id = "disabled-change");
        using var current = await connection.RequestAsync("DOM.getDocument", new { depth = -1 });
        Assert.NotEqual(nodeId, FindElement(current.RootElement.GetProperty("result").GetProperty("root"), "disabled-change").GetProperty("nodeId").GetInt32());
        Assert.False(connection.HasBufferedEvent("DOM.attributeModified"));
    }

    private static InspectionTestHost CreateHost(out View left, out View right, out Button button)
    {
        View? first = null, second = null;
        Button? target = null;
        var host = new InspectionTestHost(window =>
        {
            var content = new View { Id = "content" };
            first = new View { Id = "left" };
            second = new View { Id = "right" };
            target = new Button("Activity") { Id = "activity" };
            first.Children.Add(target);
            content.Children.Add(first);
            content.Children.Add(second);
            window.Load(content);
        });
        left = first!;
        right = second!;
        button = target!;
        return host;
    }

    internal static JsonElement FindElement(JsonElement root, string id) => Flatten(root).First(node =>
    {
        if (!node.TryGetProperty("attributes", out var attributes)) return false;
        for (var i = 0; i + 1 < attributes.GetArrayLength(); i += 2)
            if (attributes[i].GetString() == "id" && attributes[i + 1].GetString() == id) return true;
        return false;
    });

    internal static IEnumerable<JsonElement> Flatten(JsonElement root)
    {
        yield return root;
        if (!root.TryGetProperty("children", out var children)) yield break;
        foreach (var child in children.EnumerateArray())
            foreach (var node in Flatten(child)) yield return node;
    }

    private static void AssertError(JsonDocument response, int code, string message)
    {
        var error = response.RootElement.GetProperty("error");
        Assert.Equal(code, error.GetProperty("code").GetInt32());
        Assert.Equal(message, error.GetProperty("message").GetString());
    }
}
