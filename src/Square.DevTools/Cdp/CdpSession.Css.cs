using System.Text.Json;
using Square.CSS.Properties;
using Square.Hosting;
using Square.UI.ElementApi;

namespace Square.DevTools;

// CSS domain: style reads and serialization, runtime resource lifecycle, editing and computed tracking.
internal sealed partial class CdpSession
{
    // CSS virtual runtime resources announced to this frontend; guarded by _stateGate.
    private readonly Dictionary<string, ElementInspectionStyleText> _announcedStyleSheets = new(StringComparer.Ordinal);

    // ForNode notifications and filtered batch takes share only published live Elements.
    private NodeKey? _trackedForNodeKey;
    private IReadOnlyDictionary<string, string>? _forNodeBaseline;
    private readonly Dictionary<string, HashSet<string>> _trackedProperties = new(StringComparer.Ordinal);
    private readonly Dictionary<NodeKey, IReadOnlyDictionary<string, string>> _batchBaselines = new();
    private readonly HashSet<int> _pendingTakeNodeIds = new();
    private int? _pendingTakeId;
    private Task? _forNodeDelayTask;
    private CancellationTokenSource? _forNodeDelayCancellation;
    private long _forNodeDelayEpoch;
    private long _forNodeDelayTaskEpoch;
    private bool _forNodeDelayPending;
    private bool _cssEnabled;
    /// <summary>
    /// Refreshes CSS runtime resources and computed tracking after merged Styles dirt;
    /// signals without a Styles component never touch the CSS domain.
    /// </summary>
    private async Task SyncCssAsync(ElementInspectionChangeKind kinds, CancellationToken cancellationToken)
    {
        if (!_cssEnabled || (kinds & ElementInspectionChangeKind.Styles) == 0) return;
        if (_server.AllowChromeStyleEditing) await RefreshStyleSheetsAsync(cancellationToken);
        await SyncComputedTrackingAsync(cancellationToken);
    }

    /// <summary>
    /// Diffs the announced virtual runtime stylesheets against the current native set:
    /// new resources announce a header, removed resources announce removal and changed
    /// declaration text announces a change event.
    /// </summary>
    private async Task RefreshStyleSheetsAsync(CancellationToken cancellationToken)
    {
        var sheets = await _window.CaptureInspectionStyleSheetsAsync(cancellationToken).WaitAsync(cancellationToken);
        var current = new Dictionary<string, ElementInspectionStyleText>(StringComparer.Ordinal);
        foreach (var sheet in sheets) current[sheet.StyleSheetId] = sheet;

        foreach (var announcedId in _announcedStyleSheets.Keys.ToArray())
        {
            if (current.ContainsKey(announcedId)) continue;
            _announcedStyleSheets.Remove(announcedId);
            await SendEventAsync("CSS.styleSheetRemoved", writer =>
            {
                writer.WriteString("styleSheetId", announcedId);
            }, cancellationToken);
        }

        foreach (var sheet in sheets)
        {
            if (!_announcedStyleSheets.TryGetValue(sheet.StyleSheetId, out var announced))
            {
                _announcedStyleSheets[sheet.StyleSheetId] = sheet;
                await SendEventAsync("CSS.styleSheetAdded", writer => WriteStyleSheetHeader(writer, sheet), cancellationToken);
            }
            else if (!string.Equals(announced.Text, sheet.Text, StringComparison.Ordinal))
            {
                _announcedStyleSheets[sheet.StyleSheetId] = sheet;
                await SendEventAsync("CSS.styleSheetChanged", writer =>
                {
                    writer.WriteString("styleSheetId", sheet.StyleSheetId);
                }, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Compares computed snapshots for tracked nodes against their baselines: the ForNode
    /// target schedules its single throttled notification, the batch configuration feeds
    /// the pending take set, and a resolvable pending take is completed here.
    /// </summary>
    private async Task SyncComputedTrackingAsync(CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string>? forNodeCurrent = null;
        if (_trackedForNodeKey is { } trackedKey)
        {
            if (_frontendIds.TryGetValue(trackedKey, out var trackedFrontendId) &&
                _publishedNodes.Contains(trackedFrontendId))
            {
                var snapshots = await _window
                    .InspectComputedStylesAsync([trackedKey.DebugId], null, cancellationToken)
                    .WaitAsync(cancellationToken);
                if (snapshots.Count > 0)
                {
                    var current = snapshots[0].Properties;
                    forNodeCurrent = current;
                    if (_forNodeBaseline == null)
                    {
                        // Newly tracked node: only the baseline is established.
                        _forNodeBaseline = current;
                    }
                    else if (!ComputedEquals(_forNodeBaseline, current))
                    {
                        await ScheduleForNodeNotificationAsync(cancellationToken);
                    }
                }
                else
                {
                    CancelForNodeDelay();
                    _trackedForNodeKey = null;
                    _forNodeBaseline = null;
                }
            }
            else
            {
                CancelForNodeDelay();
                _trackedForNodeKey = null;
                _forNodeBaseline = null;
            }
        }

        if (_trackedProperties.Count > 0)
        {
            var published = new List<(NodeKey Key, int FrontendId, int DebugId)>();
            foreach (var pair in _frontendKeys)
            {
                if (pair.Value.Kind != CdpNodeKind.Element || !_publishedNodes.Contains(pair.Key)) continue;
                published.Add((pair.Value, pair.Key, pair.Value.DebugId));
            }

            // Removed nodes leave both the baseline map and the pending take set.
            var liveKeys = new HashSet<NodeKey>(published.Select(entry => entry.Key));
            foreach (var key in _batchBaselines.Keys.Where(key => !liveKeys.Contains(key)).ToArray())
                _batchBaselines.Remove(key);
            _pendingTakeNodeIds.RemoveWhere(frontendId =>
                !_frontendKeys.TryGetValue(frontendId, out var pendingKey) || !liveKeys.Contains(pendingKey));

            if (published.Count > 0)
            {
                var byDebugId = new Dictionary<int, IReadOnlyDictionary<string, string>>();
                if (forNodeCurrent != null && _trackedForNodeKey is { } fullKey) byDebugId[fullKey.DebugId] = forNodeCurrent;
                var debugIds = published.Where(entry => !byDebugId.ContainsKey(entry.DebugId)).Select(entry => entry.DebugId).ToArray();
                if (debugIds.Length > 0)
                {
                    var propertyNames = _trackedProperties.Keys.ToArray();
                    var snapshots = await _window.InspectComputedStylesAsync(debugIds, propertyNames, cancellationToken)
                        .WaitAsync(cancellationToken);
                    foreach (var snapshot in snapshots) byDebugId[snapshot.Id] = snapshot.Properties;
                }
                foreach (var entry in published)
                {
                    if (!byDebugId.TryGetValue(entry.DebugId, out var current)) continue;
                    if (!_batchBaselines.TryGetValue(entry.Key, out var baseline))
                    {
                        // Newly published node: only the baseline is established.
                        _batchBaselines[entry.Key] = current;
                        continue;
                    }
                    if (TrackingMatches(baseline, current)) _pendingTakeNodeIds.Add(entry.FrontendId);
                    _batchBaselines[entry.Key] = current;
                }
            }
        }

        if (_pendingTakeId.HasValue && _pendingTakeNodeIds.Count > 0)
            await SendTakeResultAsync(_pendingTakeId.Value, cancellationToken);
    }

    private async Task SendComputedStyleAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var key = ResolveNodeKey(parameters);
        // The dedicated native computed API reads the live element directly; matched rule
        // and inline data are never copied for computed queries.
        var snapshots = await _window.InspectComputedStylesAsync([key.DebugId], null, cancellationToken)
            .WaitAsync(cancellationToken);
        if (snapshots.Count == 0) throw new CdpRequestException(-32000, "Node not found.");

        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("computedStyle");
            WriteComputedStyle(writer, snapshots[0].Properties);
        }, cancellationToken);
    }

    private async Task SendMatchedStylesAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var styles = await GetElementStylesAsync(id, parameters, cancellationToken);
        if (styles == null) return;
        await EnsureSnapshotStylesAnnouncedAsync(styles, cancellationToken);

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
            WriteInlineStyleValue(writer, styles);
        }, cancellationToken);
    }

    private async Task SendAnimatedStylesAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        // The shared selector reuses matched-styles node resolution, so an unknown node
        // fails identically; the payload carries only the declared animation returns.
        if (await GetElementStylesAsync(id, parameters, cancellationToken) == null) return;
        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("animationStyles");
            writer.WriteStartArray();
            writer.WriteEndArray();
            writer.WritePropertyName("transitionsStyle");
            WritePlainCssStyle(writer, []);
            writer.WritePropertyName("inherited");
            writer.WriteStartArray();
            writer.WriteEndArray();
        }, cancellationToken);
    }

    private async Task SendInlineStylesAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var styles = await GetElementStylesAsync(id, parameters, cancellationToken);
        if (styles == null) return;
        await EnsureSnapshotStylesAnnouncedAsync(styles, cancellationToken);

        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("inlineStyle");
            WriteInlineStyleValue(writer, styles);
            writer.WritePropertyName("attributesStyle");
            writer.WriteNullValue();
        }, cancellationToken);
    }

    private async Task<ElementInspectionStyleSnapshot?> GetElementStylesAsync(
        int requestId,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        var key = ResolveNodeKey(parameters);
        var styles = await _window
            .InspectElementStylesAsync(key.DebugId, _server.AllowChromeStyleEditing, cancellationToken)
            .WaitAsync(cancellationToken);
        if (styles == null)
        {
            await SendErrorAsync(requestId, -32000, "Node styles not found.", cancellationToken);
            return null;
        }
        return styles;
    }

    /// <summary>
    /// Serializes one matched rule: UA rules keep their user-agent origin without any
    /// editable stylesheet identity, author rules reference their runtime resource.
    /// </summary>
    private static void WriteMatchedCssRule(Utf8JsonWriter writer, ElementInspectionStyleRule rule)
    {
        writer.WriteStartObject();
        if (rule.Origin != "user-agent" && rule.Style is { } ruleStyle)
            writer.WriteString("styleSheetId", ruleStyle.StyleSheetId);
        writer.WritePropertyName("selectorList");
        writer.WriteStartObject();
        writer.WritePropertyName("selectors");
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteString("text", rule.Selector);
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteString("text", rule.Selector);
        writer.WriteEndObject();
        writer.WriteString("origin", rule.Origin);
        writer.WritePropertyName("style");
        if (rule.Origin != "user-agent" && rule.Style is { } style) WriteResourceCssStyle(writer, style);
        else WritePlainCssStyle(writer, rule.Declarations);
        writer.WriteEndObject();
    }

    /// <summary>Resource-backed declaration block: truthful IDs, block text and resource coordinates.</summary>
    private static void WriteResourceCssStyle(Utf8JsonWriter writer, ElementInspectionStyleText style)
    {
        writer.WriteStartObject();
        writer.WriteString("styleSheetId", style.StyleSheetId);
        WriteCssProperties(writer, style.Declarations);
        WriteShorthandEntries(writer, style.Declarations);
        writer.WriteString("cssText", ExtractRangeText(style.Text, style.Range));
        WriteSourceRange(writer, "range", style.Range);
        writer.WriteEndObject();
    }

    /// <summary>Declaration block without an editable resource: no IDs and no ranges.</summary>
    private static void WritePlainCssStyle(Utf8JsonWriter writer, IReadOnlyList<ElementInspectionStyleDeclaration> declarations)
    {
        writer.WriteStartObject();
        WriteCssProperties(writer, declarations);
        WriteShorthandEntries(writer, declarations);
        writer.WriteEndObject();
    }

    private static void WriteInlineStyleValue(Utf8JsonWriter writer, ElementInspectionStyleSnapshot styles)
    {
        if (styles.InlineStyle is { } inline)
        {
            WriteResourceCssStyle(writer, inline);
            return;
        }
        // Readonly gate: no editor metadata exists; the truthful inline block text is
        // parsed with the shared internal declaration parser and carries no IDs/ranges.
        var declarations = ParseInlineDeclarations(styles.InlineCssText);
        writer.WriteStartObject();
        WriteCssProperties(writer, declarations);
        WriteShorthandEntries(writer, declarations);
        writer.WriteString("cssText", styles.InlineCssText);
        writer.WriteEndObject();
    }

    private static ElementInspectionStyleDeclaration[] ParseInlineDeclarations(string cssText) =>
        CssDeclarationText.Parse(cssText)
            .Select(entry => new ElementInspectionStyleDeclaration(
                entry.Property,
                entry.Value,
                entry.Important,
                Range: null,
                Text: entry.Text,
                Disabled: entry.Disabled,
                ParsedOk: entry.ParsedOk))
            .ToArray();

    private static void WriteCssProperties(Utf8JsonWriter writer, IReadOnlyList<ElementInspectionStyleDeclaration> declarations)
    {
        writer.WritePropertyName("cssProperties");
        writer.WriteStartArray();
        foreach (var declaration in declarations)
        {
            writer.WriteStartObject();
            writer.WriteString("name", declaration.Property);
            writer.WriteString("value", declaration.Value);
            writer.WriteBoolean("important", declaration.Important);
            writer.WriteBoolean("implicit", false);
            if (declaration.Text != null) writer.WriteString("text", declaration.Text);
            if (declaration.Range is { } range) WriteSourceRange(writer, "range", range);
            writer.WriteBoolean("parsedOk", declaration.ParsedOk);
            writer.WriteBoolean("disabled", declaration.Disabled);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    /// <summary>Shorthand entries are generated from effective authored shorthand declarations only.</summary>
    private static void WriteShorthandEntries(Utf8JsonWriter writer, IReadOnlyList<ElementInspectionStyleDeclaration> declarations)
    {
        writer.WritePropertyName("shorthandEntries");
        writer.WriteStartArray();
        foreach (var declaration in declarations)
        {
            if (declaration.Disabled || !declaration.ParsedOk) continue;
            if (!CssShorthandExpander.IsShorthand(declaration.Property)) continue;
            writer.WriteStartObject();
            writer.WriteString("name", declaration.Property);
            writer.WriteString("value", declaration.Value);
            writer.WriteBoolean("important", declaration.Important);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteSourceRange(Utf8JsonWriter writer, string name, ElementInspectionStyleRange range)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteNumber("startLine", range.StartLine);
        writer.WriteNumber("startColumn", range.StartColumn);
        writer.WriteNumber("endLine", range.EndLine);
        writer.WriteNumber("endColumn", range.EndColumn);
        writer.WriteEndObject();
    }

    private static void WriteComputedStyle(Utf8JsonWriter writer, IReadOnlyDictionary<string, string> properties)
    {
        writer.WriteStartArray();
        foreach (var pair in properties)
        {
            writer.WriteStartObject();
            writer.WriteString("name", pair.Key);
            writer.WriteString("value", pair.Value);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    /// <summary>End position (line/column, CRLF counts once) just past the last character.</summary>
    private static (int Line, int Column) TextEndPosition(string text)
    {
        var line = 0;
        var lastNewline = -1;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n') continue;
            line++;
            lastNewline = index;
        }
        return (line, text.Length - lastNewline - 1);
    }

    private static string ExtractRangeText(string text, ElementInspectionStyleRange range)
    {
        var start = OffsetOf(text, range.StartLine, range.StartColumn);
        var end = OffsetOf(text, range.EndLine, range.EndColumn);
        if (start < 0 || end < start || end > text.Length) return text;
        return text[start..end];
    }

    private static int OffsetOf(string text, int line, int column)
    {
        if (line < 0 || column < 0) return -1;
        var currentLine = 0;
        var lineStart = 0;
        while (currentLine < line)
        {
            var newline = text.IndexOf('\n', lineStart);
            if (newline < 0) return -1;
            lineStart = newline + 1;
            currentLine++;
        }
        return lineStart + column;
    }

    /// <summary>
    /// Enumerates all current runtime resources after CSS.enable and announces a full
    /// header for each; repeated enables never announce the same resource twice.
    /// </summary>
    private async Task AnnounceStyleSheetsAsync(CancellationToken cancellationToken)
    {
        var sheets = await _window.CaptureInspectionStyleSheetsAsync(cancellationToken).WaitAsync(cancellationToken);
        foreach (var sheet in sheets)
        {
            if (!_announcedStyleSheets.TryAdd(sheet.StyleSheetId, sheet)) continue;
            await SendEventAsync("CSS.styleSheetAdded", writer => WriteStyleSheetHeader(writer, sheet), cancellationToken);
        }
    }

    /// <summary>Announces (or refreshes) a resource header before metadata about it is returned.</summary>
    private async Task EnsureStyleSheetAnnouncedAsync(ElementInspectionStyleText style, CancellationToken cancellationToken)
    {
        if (!_cssEnabled || !_server.AllowChromeStyleEditing || style.Origin != "regular") return;
        if (!_announcedStyleSheets.TryGetValue(style.StyleSheetId, out var announced))
        {
            _announcedStyleSheets[style.StyleSheetId] = style;
            await SendEventAsync("CSS.styleSheetAdded", writer => WriteStyleSheetHeader(writer, style), cancellationToken);
            return;
        }
        if (!string.Equals(announced.Text, style.Text, StringComparison.Ordinal))
        {
            _announcedStyleSheets[style.StyleSheetId] = style;
            await SendEventAsync("CSS.styleSheetChanged", writer =>
            {
                writer.WriteString("styleSheetId", style.StyleSheetId);
            }, cancellationToken);
        }
    }

    private async Task EnsureSnapshotStylesAnnouncedAsync(ElementInspectionStyleSnapshot styles, CancellationToken cancellationToken)
    {
        if (!_server.AllowChromeStyleEditing || !_cssEnabled) return;
        if (styles.InlineStyle is { } inline) await EnsureStyleSheetAnnouncedAsync(inline, cancellationToken);
        foreach (var rule in styles.MatchedRules ?? [])
            if (rule.Style is { } style) await EnsureStyleSheetAnnouncedAsync(style, cancellationToken);
    }

    /// <summary>
    /// Full CSSStyleSheetHeader for one virtual runtime resource: the frame target, an
    /// unambiguous square:// runtime URL, mutable editing state and the inline owner node.
    /// </summary>
    private void WriteStyleSheetHeader(Utf8JsonWriter writer, ElementInspectionStyleText style)
    {
        writer.WritePropertyName("header");
        writer.WriteStartObject();
        writer.WriteString("styleSheetId", style.StyleSheetId);
        writer.WriteString("frameId", _server.TargetId);
        writer.WriteString("sourceURL", $"square://runtime-style/{style.StyleSheetId}");
        writer.WriteString("origin", style.Origin);
        writer.WriteString("title", "Square runtime styles");
        if (style.IsInline && style.OwnerDebugId is { } ownerDebugId)
            writer.WriteNumber("ownerNode", GetOrCreateBackendId(new NodeKey(ownerDebugId, CdpNodeKind.Element)));
        writer.WriteBoolean("disabled", false);
        // The virtual resource is not a parser-created STYLE tag, so isInline stays false.
        writer.WriteBoolean("isInline", false);
        writer.WriteBoolean("isMutable", true);
        writer.WriteBoolean("isConstructed", false);
        var (endLine, endColumn) = TextEndPosition(style.Text);
        writer.WriteNumber("startLine", 0);
        writer.WriteNumber("startColumn", 0);
        writer.WriteNumber("endLine", endLine);
        writer.WriteNumber("endColumn", endColumn);
        writer.WriteNumber("length", style.Text.Length);
        writer.WriteEndObject();
    }

    /// <summary>
    /// Applies staged declaration block edits in one native atomic batch; any unknown,
    /// stale, read-only or invalid-range edit rejects the whole batch without mutation.
    /// </summary>
    private async Task SetStyleTextsAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!_server.AllowChromeStyleEditing)
            throw new CdpRequestException(-32000, "Chrome style editing is disabled.");
        var edits = ParseStyleTextEdits(parameters);
        IReadOnlyList<ElementInspectionStyleText> results = edits.Count == 0
            ? []
            : await _window.ApplyInspectionStyleEditsAsync(edits, cancellationToken).WaitAsync(cancellationToken);
        if (_cssEnabled && results.Count > 0) await RefreshStyleSheetsAsync(cancellationToken);
        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("styles");
            writer.WriteStartArray();
            foreach (var style in results)
                WriteResourceCssStyle(writer, style);
            writer.WriteEndArray();
        }, cancellationToken);
    }

    private static List<ElementInspectionStyleTextEdit> ParseStyleTextEdits(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("edits", out var editsElement) ||
            editsElement.ValueKind != JsonValueKind.Array)
        {
            throw new CdpRequestException(-32602, "Invalid CSS.setStyleTexts parameters.");
        }
        var edits = new List<ElementInspectionStyleTextEdit>(editsElement.GetArrayLength());
        foreach (var entry in editsElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("styleSheetId", out var idElement) || idElement.ValueKind != JsonValueKind.String ||
                !entry.TryGetProperty("range", out var rangeElement) || rangeElement.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("text", out var textElement) || textElement.ValueKind != JsonValueKind.String)
            {
                throw new CdpRequestException(-32602, "Invalid CSS.setStyleTexts parameters.");
            }
            edits.Add(new ElementInspectionStyleTextEdit(
                idElement.GetString()!,
                ReadSourceRange(rangeElement),
                textElement.GetString()!));
        }
        return edits;
    }

    private static ElementInspectionStyleRange ReadSourceRange(JsonElement element)
    {
        int Read(string name)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number ||
                !value.TryGetInt32(out var number) || number < 0)
            {
                throw new CdpRequestException(-32602, "Invalid style edit range.");
            }
            return number;
        }
        var range = new ElementInspectionStyleRange(
            Read("startLine"), Read("startColumn"), Read("endLine"), Read("endColumn"));
        if (range.EndLine < range.StartLine ||
            (range.EndLine == range.StartLine && range.EndColumn < range.StartColumn))
        {
            throw new CdpRequestException(-32602, "Invalid style edit range.");
        }
        return range;
    }

    private async Task SendStyleSheetTextAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("styleSheetId", out var idElement) ||
            idElement.ValueKind != JsonValueKind.String)
        {
            throw new CdpRequestException(-32602, "'styleSheetId' is required.");
        }
        // Unknown or stale resources are rejected by the native editor as
        // KeyNotFoundException("Style sheet not found.") and surface as -32000.
        var style = await _window.InspectStyleSheetAsync(idElement.GetString()!, cancellationToken)
            .WaitAsync(cancellationToken);
        if (style == null) throw new CdpRequestException(-32000, "Style sheet not found.");
        if (_cssEnabled) await EnsureStyleSheetAnnouncedAsync(style, cancellationToken);
        await SendResultAsync(id, writer => writer.WriteString("text", style.Text), cancellationToken);
    }

    /// <summary>Starts or replaces the single ForNode tracking target; an omitted nodeId stops it.</summary>
    private async Task TrackComputedStyleUpdatesForNodeAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        NodeKey? target = null;
        if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("nodeId", out var nodeIdElement))
        {
            if (nodeIdElement.ValueKind == JsonValueKind.Number)
            {
                if (!nodeIdElement.TryGetInt32(out var nodeId) ||
                    !_frontendKeys.TryGetValue(nodeId, out var key) ||
                    key.Kind != CdpNodeKind.Element ||
                    !_publishedNodes.Contains(nodeId))
                {
                    throw new CdpRequestException(-32000, "Node not found.");
                }
                target = key;
            }
            else if (nodeIdElement.ValueKind != JsonValueKind.Null)
            {
                throw new CdpRequestException(-32602, "Invalid node ID.");
            }
        }

        if (target is { } tracked)
        {
            if (_trackedForNodeKey != tracked)
            {
                CancelForNodeDelay();
                var snapshots = await _window.InspectComputedStylesAsync([tracked.DebugId], null, cancellationToken)
                    .WaitAsync(cancellationToken);
                if (snapshots.Count == 0) throw new CdpRequestException(-32000, "Node not found.");
                _trackedForNodeKey = tracked;
                _forNodeBaseline = snapshots[0].Properties;
            }
        }
        else
        {
            CancelForNodeDelay();
            _trackedForNodeKey = null;
            _forNodeBaseline = null;
        }
        await SendEmptyResultAsync(id, cancellationToken);
    }

    /// <summary>Replaces the whole name -> allowed values batch configuration.</summary>
    private async Task TrackComputedStyleUpdatesAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var tracked = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("propertiesToTrack", out var arrayElement) || arrayElement.ValueKind != JsonValueKind.Array)
            throw new CdpRequestException(-32602, "Invalid tracking configuration.");
        foreach (var entry in arrayElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String ||
                !entry.TryGetProperty("value", out var valueElement) || valueElement.ValueKind != JsonValueKind.String)
                throw new CdpRequestException(-32602, "Invalid tracking configuration.");
            var name = nameElement.GetString()!;
            if (!tracked.TryGetValue(name, out var allowed)) tracked.Add(name, allowed = new HashSet<string>(StringComparer.Ordinal));
            allowed.Add(valueElement.GetString()!);
        }

        _trackedProperties.Clear();
        foreach (var pair in tracked) _trackedProperties[pair.Key] = pair.Value;
        _batchBaselines.Clear();
        await EstablishBatchBaselinesAsync(cancellationToken);
        if (_trackedProperties.Count == 0)
        {
            // An empty configuration completes a pending take, then stops batch tracking.
            await CompletePendingTakeAsync(cancellationToken);
            _pendingTakeNodeIds.Clear();
        }
        await SendEmptyResultAsync(id, cancellationToken);
    }

    private async Task TakeComputedStyleUpdatesAsync(int id, CancellationToken cancellationToken)
    {
        if (_trackedProperties.Count == 0)
            throw new CdpRequestException(-32000, "No computed styles are being tracked right now.");
        if (_pendingTakeId.HasValue)
            throw new CdpRequestException(-32000, "A previous request has not been resolved yet.");
        if (_pendingTakeNodeIds.Count > 0)
        {
            await SendTakeResultAsync(id, cancellationToken);
            return;
        }
        // Registered and left unanswered: the event pump completes this request when a
        // qualifying change arrives; the dispatch loop keeps serving other commands.
        _pendingTakeId = id;
    }

    private async Task CompletePendingTakeAsync(CancellationToken cancellationToken)
    {
        if (_pendingTakeId is not { } pendingId) return;
        await SendTakeResultAsync(pendingId, cancellationToken);
    }

    private async Task SendTakeResultAsync(int id, CancellationToken cancellationToken)
    {
        var nodeIds = _pendingTakeNodeIds.ToArray();
        _pendingTakeNodeIds.Clear();
        _pendingTakeId = null;
        Array.Sort(nodeIds);
        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("nodeIds");
            writer.WriteStartArray();
            foreach (var nodeId in nodeIds) writer.WriteNumberValue(nodeId);
            writer.WriteEndArray();
        }, cancellationToken);
    }

    private async Task ScheduleForNodeNotificationAsync(CancellationToken cancellationToken)
    {
        if (_forNodeDelayPending) return;
        await DrainForNodeDelayAsync(cancel: true);
        cancellationToken.ThrowIfCancellationRequested();
        _forNodeDelayPending = true;
        _forNodeDelayCancellation = CancellationTokenSource.CreateLinkedTokenSource(_sessionShutdown.Token);
        _forNodeDelayTaskEpoch = ++_forNodeDelayEpoch;
        _forNodeDelayTask = RunForNodeDelayAsync(_forNodeDelayTaskEpoch, _forNodeDelayCancellation.Token);
    }

    private async Task RunForNodeDelayAsync(long epoch, CancellationToken cancellationToken)
    {
        try { await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            if (!_stopping && !_events.Writer.TryWrite(new CdpEvent(CdpEventKind.ComputedStyleNotification, OwnerId: epoch))) CancelSession();
        }
    }

    private void CancelForNodeDelay()
    {
        _forNodeDelayPending = false;
        ++_forNodeDelayEpoch;
        _forNodeDelayCancellation?.Cancel();
    }

    private async Task DrainForNodeDelayAsync(bool cancel)
    {
        if (_forNodeDelayTask == null) return;
        if (cancel) _forNodeDelayCancellation!.Cancel();
        await _forNodeDelayTask;
        _forNodeDelayTask = null;
        _forNodeDelayCancellation!.Dispose();
        _forNodeDelayCancellation = null;
    }

    private async Task EstablishBatchBaselinesAsync(CancellationToken cancellationToken)
    {
        if (!_cssEnabled || _trackedProperties.Count == 0) return;
        var keys = new Dictionary<int, NodeKey>();
        foreach (var id in _publishedNodes)
            if (_frontendKeys.TryGetValue(id, out var key) && key.Kind == CdpNodeKind.Element && !_batchBaselines.ContainsKey(key))
                keys[key.DebugId] = key;
        if (keys.Count == 0) return;
        var snapshots = await _window.InspectComputedStylesAsync(keys.Keys.ToArray(), _trackedProperties.Keys.ToArray(), cancellationToken)
            .WaitAsync(cancellationToken);
        foreach (var snapshot in snapshots)
            if (keys.TryGetValue(snapshot.Id, out var key)) _batchBaselines[key] = snapshot.Properties;
    }

    /// <summary>Single throttled notification for the ForNode tracking target with real value compare.</summary>
    private async Task SendForNodeUpdateAsync(CancellationToken cancellationToken)
    {
        if (_trackedForNodeKey is not { } key ||
            !_frontendIds.TryGetValue(key, out var frontendId) ||
            !_publishedNodes.Contains(frontendId))
        {
            return;
        }
        var snapshots = await _window
            .InspectComputedStylesAsync([key.DebugId], null, cancellationToken)
            .WaitAsync(cancellationToken);
        if (snapshots.Count == 0) return;
        var current = snapshots[0].Properties;
        if (_forNodeBaseline == null)
        {
            // A stale delay switched to a fresh target only establishes its baseline.
            _forNodeBaseline = current;
            return;
        }
        if (ComputedEquals(_forNodeBaseline, current)) return;
        _forNodeBaseline = current;
        await SendEventAsync("CSS.computedStyleUpdated", writer =>
        {
            writer.WriteNumber("nodeId", frontendId);
        }, cancellationToken);
    }

    private static bool ComputedEquals(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count) return false;
        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) ||
                !string.Equals(pair.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Only changed strings whose old or new value belongs to the configured set qualify.</summary>
    private bool TrackingMatches(
        IReadOnlyDictionary<string, string> baseline,
        IReadOnlyDictionary<string, string> current)
    {
        foreach (var pair in _trackedProperties)
        {
            baseline.TryGetValue(pair.Key, out var oldValue);
            current.TryGetValue(pair.Key, out var newValue);
            if (string.Equals(oldValue, newValue, StringComparison.Ordinal)) continue;
            if ((oldValue != null && pair.Value.Contains(oldValue)) ||
                (newValue != null && pair.Value.Contains(newValue)))
            {
                return true;
            }
        }
        return false;
    }
}
