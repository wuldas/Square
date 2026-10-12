using System.Net.WebSockets;
using System.Text.Json;
using Square.Controls;
using Square.UI;
using Xunit;

namespace Square.DevTools.Tests;

/// <summary>
/// Real-window CSS.setStyleTexts / runtime resource tests: every edit drives the actual
/// native editor through CDP on a rendered InspectionTestHost; no echo runtime participates.
/// </summary>
public sealed class CdpStyleTests
{
    [Fact]
    public async Task SetStyleTextsWithoutStyleEditingIsRejected()
    {
        using var host = CreateHost(out _, out _);
        await using var server = DevToolsServer.Start(host.Window, new() { AllowChromeInspect = true });
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);
        using var response = await connection.RequestAsync("CSS.setStyleTexts", new
        {
            edits = new[]
            {
                new
                {
                    styleSheetId = "square-inline-1",
                    range = new { startLine = 0, startColumn = 0, endLine = 0, endColumn = 10 },
                    text = "color: blue;"
                }
            }
        });
        AssertError(response, -32000, "Chrome style editing is disabled.");
    }

    [Fact]
    public async Task EnableAnnouncesResourcesOnceWithTruthfulHeaders()
    {
        using var host = CreateHost(out var button, out _);
        await using var server = DevToolsServer.Start(host.Window, new()
        {
            AllowChromeInspect = true,
            AllowChromeStyleEditing = true
        });
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);
        using var document = await connection.RequestAsync("DOM.getDocument", new { depth = -1 });
        var target = CdpDomTests.FindElement(document.RootElement.GetProperty("result").GetProperty("root"), "first");
        var nodeId = target.GetProperty("nodeId").GetInt32();
        var backendId = target.GetProperty("backendNodeId").GetInt32();

        using (var enabled = await connection.RequestAsync("CSS.enable"))
            Assert.True(enabled.RootElement.TryGetProperty("result", out _));
        using var barrier = await connection.RequestAsync("Page.enable");
        var headers = TakeStyleSheetHeaders(connection);
        foreach (var header in headers)
        {
            Assert.Equal(server.TargetId, header.GetProperty("frameId").GetString());
            Assert.StartsWith("square://runtime-style/", header.GetProperty("sourceURL").GetString());
            Assert.Equal("Square runtime styles", header.GetProperty("title").GetString());
            Assert.Equal("regular", header.GetProperty("origin").GetString());
            Assert.False(header.GetProperty("disabled").GetBoolean());
            // The virtual resource is not a parser-created STYLE tag.
            Assert.False(header.GetProperty("isInline").GetBoolean());
            Assert.True(header.GetProperty("isMutable").GetBoolean());
            Assert.False(header.GetProperty("isConstructed").GetBoolean());
        }
        Assert.Equal(headers.Select(header => header.GetProperty("styleSheetId").GetString()).Count(),
            headers.Select(header => header.GetProperty("styleSheetId").GetString()).Distinct().Count());

        // Matched styles carry truthful runtime resource identities; UA rules stay uneditable.
        using var matched = await connection.RequestAsync("CSS.getMatchedStylesForNode", new { nodeId });
        var result = matched.RootElement.GetProperty("result");
        var rules = result.GetProperty("matchedCSSRules").EnumerateArray().ToArray();
        var authorRule = rules.Select(rule => rule.GetProperty("rule"))
            .First(rule => rule.GetProperty("origin").GetString() == "regular" &&
                           rule.GetProperty("selectorList").GetProperty("text").GetString() == ".rule-target");
        Assert.Equal(JsonValueKind.Object, authorRule.GetProperty("selectorList").GetProperty("selectors")[0].ValueKind);
        Assert.Equal(".rule-target", authorRule.GetProperty("selectorList").GetProperty("selectors")[0].GetProperty("text").GetString());
        var ruleStyle = authorRule.GetProperty("style");
        var ruleId = ruleStyle.GetProperty("styleSheetId").GetString();
        Assert.True(ruleStyle.TryGetProperty("range", out _));
        Assert.All(rules.Select(rule => rule.GetProperty("rule")).Where(rule =>
                rule.GetProperty("origin").GetString() == "user-agent"),
            rule => Assert.False(rule.TryGetProperty("styleSheetId", out _)));
        var inlineStyle = result.GetProperty("inlineStyle");
        var inlineId = inlineStyle.GetProperty("styleSheetId").GetString();
        Assert.Equal($"square-inline-{button.DebugId}", inlineId);

        headers.AddRange(TakeStyleSheetHeaders(connection));
        // The inline header names its owner node with this session's backend ID.
        var inlineHeader = headers.Single(header => header.GetProperty("styleSheetId").GetString() == inlineId);
        Assert.Equal(backendId, inlineHeader.GetProperty("ownerNode").GetInt32());

        // getStyleSheetText returns the live full resource text and unknown IDs fail.
        string inlineText;
        using (var inlineTextResponse = await connection.RequestAsync("CSS.getStyleSheetText", new { styleSheetId = inlineId }))
        {
            inlineText = inlineTextResponse.RootElement.GetProperty("result").GetProperty("text").GetString()!;
            Assert.Equal(inlineStyle.GetProperty("cssText").GetString(), inlineText);
            Assert.Equal(inlineHeader.GetProperty("length").GetInt64(), inlineText.Length);
        }
        using (var ruleText = await connection.RequestAsync("CSS.getStyleSheetText", new { styleSheetId = ruleId }))
            Assert.Contains(ruleStyle.GetProperty("cssText").GetString()!, ruleText.RootElement.GetProperty("result").GetProperty("text").GetString());
        using (var unknown = await connection.RequestAsync("CSS.getStyleSheetText", new { styleSheetId = "square-rule-999" }))
            AssertError(unknown, -32000, "Style sheet not found.");

        // A repeated enable never re-announces live resources.
        using (await connection.RequestAsync("CSS.enable")) { }
        Assert.Empty(TakeStyleSheetHeaders(connection));
    }

    [Fact]
    public async Task InlineStyleEditAppliesComputedAndStyleEvents()
    {
        using var host = CreateHost(out var button, out _);
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
        using var before = await connection.RequestAsync("CSS.getMatchedStylesForNode", new { nodeId });
        var inlineBefore = before.RootElement.GetProperty("result").GetProperty("inlineStyle");
        var inlineId = inlineBefore.GetProperty("styleSheetId").GetString();
        var emptyRange = inlineBefore.GetProperty("range");
        Assert.Equal(0, emptyRange.GetProperty("startLine").GetInt32());
        Assert.Equal(0, emptyRange.GetProperty("startColumn").GetInt32());
        Assert.Equal(0, emptyRange.GetProperty("endLine").GetInt32());
        Assert.Equal(0, emptyRange.GetProperty("endColumn").GetInt32());

        using var edit = await connection.RequestAsync("CSS.setStyleTexts", new
        {
            edits = new[]
            {
                new
                {
                    styleSheetId = inlineId,
                    range = new { startLine = 0, startColumn = 0, endLine = 0, endColumn = 0 },
                    text = "color: blue; width: 240px;"
                }
            }
        });
        var style = edit.RootElement.GetProperty("result").GetProperty("styles")[0];
        Assert.Equal(inlineId, style.GetProperty("styleSheetId").GetString());
        Assert.Equal(2, style.GetProperty("cssProperties").GetArrayLength());
        var color = style.GetProperty("cssProperties").EnumerateArray()
            .First(property => property.GetProperty("name").GetString() == "color");
        Assert.Equal("blue", color.GetProperty("value").GetString());
        Assert.True(color.GetProperty("parsedOk").GetBoolean());
        Assert.False(color.GetProperty("disabled").GetBoolean());
        Assert.False(color.GetProperty("important").GetBoolean());
        Assert.Equal(JsonValueKind.String, color.GetProperty("text").ValueKind);
        Assert.True(color.GetProperty("range").GetProperty("startLine").GetInt32() == 0);

        // The computed panel reads the real applied value through the dedicated native API.
        using var computed = await connection.RequestAsync("CSS.getComputedStyleForNode", new { nodeId });
        Assert.Contains(computed.RootElement.GetProperty("result").GetProperty("computedStyle").EnumerateArray(),
            property => property.GetProperty("name").GetString() == "color" &&
                        property.GetProperty("value").GetString() == "blue");

        // The declared text change is announced as a styleSheetChanged event.
        using var changed = await connection.ReceiveEventAsync("CSS.styleSheetChanged");
        Assert.Equal(inlineId, changed.RootElement.GetProperty("params").GetProperty("styleSheetId").GetString());
        using var text = await connection.RequestAsync("CSS.getStyleSheetText", new { styleSheetId = inlineId });
        Assert.Contains("color: blue;", text.RootElement.GetProperty("result").GetProperty("text").GetString());
    }

    [Fact]
    public async Task MatchedRuleEditUpdatesWholeSelectorGroup()
    {
        using var host = CreateHost(out var first, out var second);
        await host.InvokeAsync(() =>
        {
            first.ClassList.Add("rule-target");
            second.ClassList.Add("rule-target");
        });
        await using var server = DevToolsServer.Start(host.Window, new()
        {
            AllowChromeInspect = true,
            AllowChromeStyleEditing = true
        });
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);
        using var document = await connection.RequestAsync("DOM.getDocument", new { depth = -1 });
        var root = document.RootElement.GetProperty("result").GetProperty("root");
        var firstId = CdpDomTests.FindElement(root, "first").GetProperty("nodeId").GetInt32();
        var secondId = CdpDomTests.FindElement(root, "second").GetProperty("nodeId").GetInt32();
        using (await connection.RequestAsync("CSS.enable")) { }
        using var before = await connection.RequestAsync("CSS.getMatchedStylesForNode", new { nodeId = firstId });
        var ruleBefore = before.RootElement.GetProperty("result").GetProperty("matchedCSSRules")
            .EnumerateArray().Select(rule => rule.GetProperty("rule"))
            .First(rule => rule.GetProperty("selectorList").GetProperty("text").GetString() == ".rule-target");
        var ruleId = ruleBefore.GetProperty("style").GetProperty("styleSheetId").GetString();
        var range = ruleBefore.GetProperty("style").GetProperty("range");

        using var edit = await connection.RequestAsync("CSS.setStyleTexts", new
        {
            edits = new[]
            {
                new
                {
                    styleSheetId = ruleId,
                    range = new
                    {
                        startLine = range.GetProperty("startLine").GetInt32(),
                        startColumn = range.GetProperty("startColumn").GetInt32(),
                        endLine = range.GetProperty("endLine").GetInt32(),
                        endColumn = range.GetProperty("endColumn").GetInt32()
                    },
                    text = "color: green;"
                }
            }
        });
        Assert.Equal("green", edit.RootElement.GetProperty("result").GetProperty("styles")[0]
            .GetProperty("cssProperties").EnumerateArray()
            .First(property => property.GetProperty("name").GetString() == "color")
            .GetProperty("value").GetString());

        foreach (var nodeId in new[] { firstId, secondId })
        {
            using var computed = await connection.RequestAsync("CSS.getComputedStyleForNode", new { nodeId });
            Assert.Contains(computed.RootElement.GetProperty("result").GetProperty("computedStyle").EnumerateArray(),
                property => property.GetProperty("name").GetString() == "color" &&
                            property.GetProperty("value").GetString() == "green");
        }
        using var text = await connection.RequestAsync("CSS.getStyleSheetText", new { styleSheetId = ruleId });
        Assert.Contains("color: green;", text.RootElement.GetProperty("result").GetProperty("text").GetString());
    }

    [Fact]
    public async Task StaleOrInvalidEditsRejectWholeBatchAtomically()
    {
        using var host = CreateHost(out var button, out _);
        await host.InvokeAsync(() => button.Style.SetProperty("color", "blue"));
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
        using var before = await connection.RequestAsync("CSS.getMatchedStylesForNode", new { nodeId });
        var inlineBefore = before.RootElement.GetProperty("result").GetProperty("inlineStyle");
        var inlineId = inlineBefore.GetProperty("styleSheetId").GetString();

        // A valid first edit paired with an unknown resource mutates nothing.
        using (var staleBatch = await connection.RequestAsync("CSS.setStyleTexts", new
        {
            edits = new object[]
            {
                new
                {
                    styleSheetId = inlineId,
                    range = new { startLine = 0, startColumn = 0, endLine = 0, endColumn = 12 },
                    text = "color: purple;"
                },
                new
                {
                    styleSheetId = "square-rule-404",
                    range = new { startLine = 0, startColumn = 0, endLine = 0, endColumn = 12 },
                    text = "color: purple;"
                }
            }
        }))
        {
            AssertError(staleBatch, -32000, "Style sheet not found.");
        }
        await AssertComputedColor(connection, nodeId, "blue");

        // A range that splits an existing token mid-identifier is rejected with the exact error.
        using (var cutRange = await connection.RequestAsync("CSS.setStyleTexts", new
        {
            edits = new[]
            {
                new
                {
                    styleSheetId = inlineId,
                    range = new { startLine = 0, startColumn = 0, endLine = 0, endColumn = 3 },
                    text = "color: purple;"
                }
            }
        }))
        {
            AssertError(cutRange, -32602, "Invalid style edit range.");
        }
        await AssertComputedColor(connection, nodeId, "blue");

        // Malformed range geometry never reaches the native editor.
        using (var badGeometry = await connection.RequestAsync("CSS.setStyleTexts", new
        {
            edits = new[]
            {
                new
                {
                    styleSheetId = inlineId,
                    range = new { startLine = 2, startColumn = 0, endLine = 1, endColumn = 0 },
                    text = "color: purple;"
                }
            }
        }))
        {
            Assert.Equal(-32602, badGeometry.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        }
        await AssertComputedColor(connection, nodeId, "blue");

        // An empty edits array answers with an empty styles list and mutates nothing.
        using (var empty = await connection.RequestAsync("CSS.setStyleTexts", new { edits = Array.Empty<object>() }))
            Assert.Equal(0, empty.RootElement.GetProperty("result").GetProperty("styles").GetArrayLength());
        await AssertComputedColor(connection, nodeId, "blue");
    }

    [Fact]
    public async Task DisabledInvalidAndImportantRowsStayTruthful()
    {
        using var host = CreateHost(out var button, out _);
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
        using var before = await connection.RequestAsync("CSS.getMatchedStylesForNode", new { nodeId });
        var inlineId = before.RootElement.GetProperty("result").GetProperty("inlineStyle")
            .GetProperty("styleSheetId").GetString();
        var currentRange = before.RootElement.GetProperty("result").GetProperty("inlineStyle").GetProperty("range").Clone();

        // Wrapping the single declaration in a comment produces a disabled row whose
        // value no longer applies: the matched rule wins again.
        using (var disable = await connection.RequestAsync("CSS.setStyleTexts", new
        {
            edits = new[]
            {
                new { styleSheetId = inlineId, range = currentRange, text = "/* color: blue; */" }
            }
        }))
        {
            var color = disable.RootElement.GetProperty("result").GetProperty("styles")[0]
                .GetProperty("cssProperties").EnumerateArray()
                .Single(property => property.GetProperty("name").GetString() == "color");
            Assert.True(color.GetProperty("disabled").GetBoolean());
            currentRange = disable.RootElement.GetProperty("result").GetProperty("styles")[0].GetProperty("range").Clone();
        }
        await AssertComputedColor(connection, nodeId, "red");

        // A syntactically invalid row stays in the resource as parsedOk=false without
        // becoming a native error, and it does not apply either.
        using (var invalid = await connection.RequestAsync("CSS.setStyleTexts", new
        {
            edits = new[]
            {
                new { styleSheetId = inlineId, range = currentRange, text = "color blue;" }
            }
        }))
        {
            Assert.Contains(invalid.RootElement.GetProperty("result").GetProperty("styles")[0]
                .GetProperty("cssProperties").EnumerateArray(),
                property => !property.GetProperty("parsedOk").GetBoolean());
            currentRange = invalid.RootElement.GetProperty("result").GetProperty("styles")[0].GetProperty("range").Clone();
        }
        await AssertComputedColor(connection, nodeId, "red");

        // Important author-level inline wins over the matched rule again.
        using (var restore = await connection.RequestAsync("CSS.setStyleTexts", new
        {
            edits = new[]
            {
                new { styleSheetId = inlineId, range = currentRange, text = "color: blue !important;" }
            }
        }))
        {
            var color = restore.RootElement.GetProperty("result").GetProperty("styles")[0]
                .GetProperty("cssProperties").EnumerateArray()
                .Single(property => property.GetProperty("name").GetString() == "color");
            Assert.True(color.GetProperty("important").GetBoolean());
            Assert.False(color.GetProperty("disabled").GetBoolean());
            Assert.True(color.GetProperty("parsedOk").GetBoolean());
        }
        await AssertComputedColor(connection, nodeId, "blue");
    }

    [Fact]
    public async Task ReadonlyGateHidesEditableIdentityAndRanges()
    {
        using var host = CreateHost(out _, out _);
        await using var server = DevToolsServer.Start(host.Window, new() { AllowChromeInspect = true });
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);
        using var document = await connection.RequestAsync("DOM.getDocument", new { depth = -1 });
        var nodeId = CdpDomTests.FindElement(document.RootElement.GetProperty("result").GetProperty("root"), "first")
            .GetProperty("nodeId").GetInt32();
        using (await connection.RequestAsync("CSS.enable")) { }
        Assert.False(connection.HasBufferedEvent("CSS.styleSheetAdded"));

        using var matched = await connection.RequestAsync("CSS.getMatchedStylesForNode", new { nodeId });
        var result = matched.RootElement.GetProperty("result");
        Assert.All(result.GetProperty("matchedCSSRules").EnumerateArray(), entry =>
        {
            var rule = entry.GetProperty("rule");
            Assert.False(rule.TryGetProperty("styleSheetId", out _));
            Assert.False(rule.GetProperty("style").TryGetProperty("range", out _));
            Assert.All(rule.GetProperty("style").GetProperty("cssProperties").EnumerateArray(), property =>
            {
                Assert.False(property.TryGetProperty("text", out _));
                Assert.False(property.TryGetProperty("range", out _));
            });
        });
        var inlineStyle = result.GetProperty("inlineStyle");
        Assert.False(inlineStyle.TryGetProperty("styleSheetId", out _));
        Assert.False(inlineStyle.TryGetProperty("range", out _));
        // The truthful inline block text is still reported without an editable identity.
        Assert.True(inlineStyle.TryGetProperty("cssText", out _));
    }

    [Fact]
    public async Task RemovedElementRemovesItsRuntimeResource()
    {
        using var host = CreateHost(out var button, out _);
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
        using var before = await connection.RequestAsync("CSS.getMatchedStylesForNode", new { nodeId });
        var inlineId = before.RootElement.GetProperty("result").GetProperty("inlineStyle")
            .GetProperty("styleSheetId").GetString();

        await host.InvokeAsync(() => button.Parent!.Children.Remove(button));
        using (var removed = await connection.ReceiveEventAsync("DOM.childNodeRemoved"))
            Assert.Equal(nodeId, removed.RootElement.GetProperty("params").GetProperty("nodeId").GetInt32());
        using var resourceRemoved = await connection.ReceiveEventAsync("CSS.styleSheetRemoved");
        Assert.Equal(inlineId, resourceRemoved.RootElement.GetProperty("params").GetProperty("styleSheetId").GetString());
        using var stale = await connection.RequestAsync("CSS.getStyleSheetText", new { styleSheetId = inlineId });
        AssertError(stale, -32000, "Style sheet not found.");
    }

    private static async Task AssertComputedColor(CdpConnection connection, int nodeId, string expected)
    {
        using var computed = await connection.RequestAsync("CSS.getComputedStyleForNode", new { nodeId });
        Assert.Contains(computed.RootElement.GetProperty("result").GetProperty("computedStyle").EnumerateArray(),
            property => property.GetProperty("name").GetString() == "color" &&
                        property.GetProperty("value").GetString() == expected);
    }

    private static List<JsonElement> TakeStyleSheetHeaders(CdpConnection connection)
    {
        var headers = new List<JsonElement>();
        foreach (var message in connection.TakeBufferedEvents("CSS.styleSheetAdded"))
        {
            using (message) headers.Add(message.RootElement.GetProperty("params").GetProperty("header").Clone());
        }
        return headers;
    }

    private static InspectionTestHost CreateHost(out Button first, out Button second)
    {
        Button? firstButton = null;
        Button? secondButton = null;
        var host = new InspectionTestHost(window =>
        {
            window.LoadGlobalCssText(".rule-target { color: red; }");
            var page = new View { Id = "page" };
            firstButton = new Button("First") { Id = "first" };
            firstButton.ClassList.Add("rule-target");
            secondButton = new Button("Second") { Id = "second" };
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
