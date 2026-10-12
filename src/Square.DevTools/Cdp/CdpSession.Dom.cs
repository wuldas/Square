using System.Text;
using System.Text.Json;
using Square.Graphics;
using Square.Hosting;

namespace Square.DevTools;

// DOM domain: publication maps, document commands and incremental snapshot diffing.
internal sealed partial class CdpSession
{
    // Explicit node identity maps: (DebugId, kind) -> frontend/backend IDs and back.
    // Frontend IDs exist only for nodes published to this connection; backend IDs exist
    // for any live node this session has referenced. IDs are never reused after resets.
    private readonly Dictionary<NodeKey, int> _frontendIds = [];
    private readonly Dictionary<int, NodeKey> _frontendKeys = [];
    private readonly Dictionary<NodeKey, int> _backendIds = [];
    private readonly Dictionary<int, NodeKey> _backendKeys = [];
    private readonly HashSet<int> _publishedNodes = [];
    private readonly HashSet<int> _expandedParents = [];
    private readonly Dictionary<NodeKey, ElementInspectionNode> _publishedSnapshots = [];
    private readonly Dictionary<string, NodeKey> _objectKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<int, int> _frontendParents = [];
    private int _nextFrontendId = 1;
    private int _nextBackendId = 1;
    private bool _firstFrontendDocumentAssigned;
    private bool _firstBackendDocumentAssigned;

    // Last captured inspection tree used as the diff baseline for incremental events.
    private ElementInspectionNode? _baselineRoot;
    private bool _documentRequested;
    private bool _domEnabled;
    /// <summary>
    /// Rebuilds the DOM diff baseline after merged Structure/Attributes/Text dirt. Styles-only
    /// signals skip the DOM snapshot; the CSS domain refreshes its own tracking instead.
    /// </summary>
    private async Task SyncDomAsync(ElementInspectionChangeKind kinds, CancellationToken cancellationToken)
    {
        if (!_domEnabled || kinds == 0 || kinds == ElementInspectionChangeKind.Styles) return;
        if (_publishedNodes.Count == 0) return;

        var snapshot = await _window.CaptureInspectionSnapshotAsync(includeSourcePaths: false, includeTextContent: true)
            .WaitAsync(cancellationToken);
        if (_baselineRoot == null)
        {
            SetBaseline(snapshot);
            return;
        }
        if (_baselineRoot.Id != snapshot.Root.Id)
        {
            // Whole-document reset: every published node, mapping and object handle dies.
            ResetNodeMaps();
            await SendEventAsync("DOM.documentUpdated", static writer => { }, cancellationToken);
            SetBaseline(snapshot);
            return;
        }

        var liveKeys = new HashSet<NodeKey>();
        CollectKeys(snapshot.Root, liveKeys);
        liveKeys.Add(NodeKeyOf(snapshot.Root, CdpNodeKind.Document));
        await RemoveReparentedNodesAsync(snapshot, cancellationToken);
        await DiffNodeAsync(_baselineRoot, snapshot.Root, CdpNodeKind.Document, liveKeys, cancellationToken);
        foreach (var key in _backendIds.Keys.Where(key => !liveKeys.Contains(key)).ToArray()) PruneKey(key);
        SetBaseline(snapshot);
    }

    /// <summary>Implicitly enables DOM (no documentUpdated) and answers getDocument/getFlattenedDocument.</summary>
    private async Task SendDocumentAsync(int id, JsonElement parameters, bool flattened, CancellationToken cancellationToken)
    {
        var depth = ReadDepth(parameters, defaultDepth: 1);
        if (depth != -1 && depth <= 0)
            throw new CdpRequestException(-32602, "Invalid depth.");
        EnableDom();

        var snapshot = await _window.CaptureInspectionSnapshotAsync(includeSourcePaths: false, includeTextContent: true)
            .WaitAsync(cancellationToken);
        if (_baselineRoot is { } baseline && baseline.Id != snapshot.Root.Id)
            ResetNodeMaps();
        _publishedNodes.Clear();
        _expandedParents.Clear();
        _frontendParents.Clear();
        _publishedSnapshots.Clear();
        _documentRequested = true;

        await SendResultAsync(id, writer =>
        {
            if (flattened)
            {
                writer.WritePropertyName("nodes");
                writer.WriteStartArray();
                var pending = new Stack<(ElementInspectionNode Node, CdpNodeKind Kind, int ParentFrontendId, int Remaining)>();
                pending.Push((snapshot.Root, CdpNodeKind.Document, 0, depth));
                while (pending.Count > 0)
                {
                    var (node, kind, parentFrontendId, remaining) = pending.Pop();
                    var frontendId = WriteDomNode(writer, node, kind, parentFrontendId, depth: 0, writeParentId: true);
                    if (remaining == 0) continue;
                    var childDepth = remaining < 0 ? -1 : remaining - 1;
                    if (kind == CdpNodeKind.Text) continue;
                    _expandedParents.Add(frontendId);
                    var children = BuildChildEntries(node, kind);
                    for (var index = children.Count - 1; index >= 0; index--)
                        pending.Push((children[index].Node, children[index].Kind, frontendId, childDepth));
                }
                writer.WriteEndArray();
            }
            else
            {
                writer.WritePropertyName("root");
                WriteDomNode(writer, snapshot.Root, CdpNodeKind.Document, parentFrontendId: 0, depth, writeParentId: false);
            }
        }, cancellationToken);
        // The response is the full current state, so it becomes the new diff baseline.
        SetBaseline(snapshot);
        await EstablishBatchBaselinesAsync(cancellationToken);
    }

    private async Task RequestChildNodesAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var key = ResolveNodeKey(parameters);
        // Only published element/document nodes can have their children requested.
        if (key.Kind == CdpNodeKind.Text ||
            !_frontendIds.TryGetValue(key, out var parentFrontendId) || !_publishedNodes.Contains(parentFrontendId))
        {
            throw new CdpRequestException(-32000, "Node not found.");
        }
        var depth = ReadDepth(parameters, defaultDepth: 1);
        if (depth != -1 && depth <= 0)
            throw new CdpRequestException(-32602, "Invalid depth.");

        var snapshot = await _window.CaptureInspectionSnapshotAsync(includeSourcePaths: false, includeTextContent: true)
            .WaitAsync(cancellationToken);
        if (!TryFindNode(snapshot, key, out var node))
            throw new CdpRequestException(-32000, "Node not found.");

        await SendSetChildNodesAsync(parentFrontendId, node, depth < 0 ? -1 : depth - 1, cancellationToken, key.Kind);
        await EstablishBatchBaselinesAsync(cancellationToken);
        await SendEmptyResultAsync(id, cancellationToken);
    }

    private async Task DescribeNodeAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var key = ResolveNodeKey(parameters);
        // describeNode defaults to depth 0 (node only); -1 and >= 0 are valid.
        var depth = ReadDepth(parameters, defaultDepth: 0);
        if (depth < -1)
            throw new CdpRequestException(-32602, "Invalid depth.");

        var snapshot = await _window.CaptureInspectionSnapshotAsync(includeSourcePaths: false, includeTextContent: true)
            .WaitAsync(cancellationToken);
        if (!TryFindNode(snapshot, key, out var node))
            throw new CdpRequestException(-32000, "Node not found.");

        var parentFrontendId = key.Kind == CdpNodeKind.Text &&
                               _frontendIds.TryGetValue(new NodeKey(key.DebugId, CdpNodeKind.Element), out var parentId)
            ? parentId
            : 0;
        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("node");
            WriteDomNode(writer, node, key.Kind, parentFrontendId, depth, writeParentId: parentFrontendId != 0, publish: false);
        }, cancellationToken);
    }

    private async Task SendAttributesAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var key = ResolveNodeKey(parameters);
        var snapshot = await _window.CaptureInspectionSnapshotAsync(includeSourcePaths: false, includeTextContent: true)
            .WaitAsync(cancellationToken);
        if (!TryFindNode(snapshot, key, out var node) || key.Kind == CdpNodeKind.Text)
            throw new CdpRequestException(-32000, "Node not found.");

        // Only element nodes carry attributes; the document reports an empty list.
        var attributes = key.Kind == CdpNodeKind.Element ? MergeAttributes(node) : new List<CdpAttribute>();
        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("attributes");
            writer.WriteStartArray();
            foreach (var attribute in attributes)
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
        var hit = await _window.HitTestInspectionAsync(new Point(x, y), false, true).WaitAsync(cancellationToken);
        if (hit == null)
        {
            await SendResultAsync(id, static writer => { }, cancellationToken);
            return;
        }

        var key = new NodeKey(hit.Id, CdpNodeKind.Element);
        var backendNodeId = GetOrCreateBackendId(key);
        var nodeId = _frontendIds.TryGetValue(key, out var frontendId) ? frontendId : 0;
        await SendResultAsync(id, writer =>
        {
            writer.WriteNumber("backendNodeId", backendNodeId);
            writer.WriteNumber("nodeId", nodeId);
            writer.WriteString("frameId", _server.TargetId);
        }, cancellationToken);
    }

    private async Task SendBoxModelAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var key = ResolveNodeKey(parameters);
        if (key.Kind != CdpNodeKind.Element) throw new CdpRequestException(-32000, "Box Model is not available for this node.");
        var node = await _window.InspectElementAsync(key.DebugId, false, true).WaitAsync(cancellationToken);
        if (node == null)
        {
            await SendErrorAsync(id, -32000, "Node not found.", cancellationToken);
            return;
        }

        if (node.BoxModel == null)
        {
            await SendErrorAsync(id, -32000, "Box Model is not available for this node.", cancellationToken);
            return;
        }

        await SendResultAsync(id, writer =>
        {
            var boxModel = node.BoxModel;
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

    private async Task SendOuterHtmlAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var key = ResolveNodeKey(parameters);
        var snapshot = await _window.CaptureInspectionSnapshotAsync(includeSourcePaths: false, includeTextContent: true)
            .WaitAsync(cancellationToken);
        if (!TryFindNode(snapshot, key, out var node))
            throw new CdpRequestException(-32000, "Node not found.");

        var html = new StringBuilder();
        WriteOuterHtml(html, node, key.Kind);
        await SendResultAsync(id, writer => writer.WriteString("outerHTML", html.ToString()), cancellationToken);
    }

    private async Task PushNodesByBackendIdsAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var backendNodeIds = ReadBackendIdArray(parameters);
        if (!_documentRequested)
            throw new CdpRequestException(-32000, "Document has not been requested.");

        var snapshot = await _window.CaptureInspectionSnapshotAsync(includeSourcePaths: false, includeTextContent: true)
            .WaitAsync(cancellationToken);
        var lookup = CollectLookup(snapshot);
        var nodeIds = new int[backendNodeIds.Length];
        for (var index = 0; index < backendNodeIds.Length; index++)
        {
            if (!_backendKeys.TryGetValue(backendNodeIds[index], out var key) ||
                !lookup.TryGetValue(key, out var node))
                continue;
            nodeIds[index] = await PublishNodePathAsync(key, node, snapshot, cancellationToken);
        }

        await EstablishBatchBaselinesAsync(cancellationToken);
        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("nodeIds");
            writer.WriteStartArray();
            foreach (var nodeId in nodeIds)
                writer.WriteNumberValue(nodeId);
            writer.WriteEndArray();
        }, cancellationToken);
    }

    private async Task<int> PublishNodePathAsync(
        NodeKey key,
        ElementInspectionNode node,
        ElementInspectionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var documentKey = NodeKeyOf(snapshot.Root, CdpNodeKind.Document);
        var parentId = _frontendIds[documentKey];
        if (key.Kind == CdpNodeKind.Document) return parentId;
        var chain = new List<ElementInspectionNode> { snapshot.Root };
        if (key.DebugId != snapshot.Root.Id && !TryCollectChain(snapshot.Root, key.DebugId, chain)) return 0;
        var parentNode = snapshot.Root;
        var parentKind = CdpNodeKind.Document;
        foreach (var ancestor in chain)
        {
            var ancestorKey = NodeKeyOf(ancestor, CdpNodeKind.Element);
            if (!_frontendIds.TryGetValue(ancestorKey, out var ancestorId) || !_publishedNodes.Contains(ancestorId))
            {
                await SendSetChildNodesAsync(parentId, parentNode, 0, cancellationToken, parentKind);
                ancestorId = _frontendIds[ancestorKey];
            }
            parentId = ancestorId;
            parentNode = ancestor;
            parentKind = CdpNodeKind.Element;
        }
        if (key.Kind == CdpNodeKind.Text &&
            (!_frontendIds.TryGetValue(key, out var textId) || !_publishedNodes.Contains(textId)))
            await SendSetChildNodesAsync(parentId, node, 0, cancellationToken);
        return _frontendIds[key];
    }

    /// <summary>Publishes and sends the children of an expanded parent; depthBelow follows DOM depth semantics.</summary>
    private async Task SendSetChildNodesAsync(
        int parentFrontendId,
        ElementInspectionNode node,
        int depthBelow,
        CancellationToken cancellationToken,
        CdpNodeKind kind = CdpNodeKind.Element)
    {
        _expandedParents.Add(parentFrontendId);
        await SendEventAsync("DOM.setChildNodes", writer =>
        {
            writer.WriteNumber("parentId", parentFrontendId);
            writer.WritePropertyName("nodes");
            writer.WriteStartArray();
            foreach (var child in BuildChildEntries(node, kind))
                WriteDomNode(writer, child.Node, child.Kind, parentFrontendId, depthBelow, writeParentId: false);
            writer.WriteEndArray();
        }, cancellationToken);
    }

    private async Task SendResolveNodeAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var key = ResolveNodeKey(parameters);
        var snapshot = await _window.CaptureInspectionSnapshotAsync(false, true).WaitAsync(cancellationToken);
        if (!TryFindNode(snapshot, key, out _)) throw new CdpRequestException(-32000, "Node not found.");
        var resolvedNodeId = GetOrCreateFrontendId(key);
        var objectId = $"square-node-{resolvedNodeId}";
        _objectKeys[objectId] = key;
        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("object");
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WriteString("subtype", "node");
            writer.WriteString("objectId", objectId);
            writer.WriteString("description", "Square Element");
            writer.WriteEndObject();
        }, cancellationToken);
    }

    private static float ReadFloat(JsonElement parameters, string name)
    {
        if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var result) && float.IsFinite(result))
            return result;
        throw new CdpRequestException(-32602, $"'{name}' must be a number.");
    }

    private sealed record CdpAttribute(string Name, string Value);

    #region DOM model

    private enum CdpNodeKind : byte
    {
        Document,
        Element,
        Text,
    }

    private readonly record struct NodeKey(int DebugId, CdpNodeKind Kind);

    private readonly record struct ChildEntry(NodeKey Key, ElementInspectionNode Node, CdpNodeKind Kind);

    private static NodeKey NodeKeyOf(ElementInspectionNode node, CdpNodeKind kind) => new(node.Id, kind);

    /// <summary>Children sequence of a snapshot node: element children first, then the synthetic text child.</summary>
    private static List<ChildEntry> BuildChildEntries(ElementInspectionNode node, CdpNodeKind kind = CdpNodeKind.Element)
    {
        if (kind == CdpNodeKind.Document)
            return [new ChildEntry(NodeKeyOf(node, CdpNodeKind.Element), node, CdpNodeKind.Element)];
        var entries = new List<ChildEntry>(node.Children.Count + 1);
        foreach (var child in node.Children)
            entries.Add(new ChildEntry(new NodeKey(child.Id, CdpNodeKind.Element), child, CdpNodeKind.Element));
        if (node.Text != null)
            entries.Add(new ChildEntry(new NodeKey(node.Id, CdpNodeKind.Text), node, CdpNodeKind.Text));
        return entries;
    }

    /// <summary>Merges real element attributes with id/class component metadata without duplicates.</summary>
    private static List<CdpAttribute> MergeAttributes(ElementInspectionNode node)
    {
        var attributes = new List<CdpAttribute>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string name, string? value)
        {
            if (value == null || !seen.Add(name)) return;
            attributes.Add(new CdpAttribute(name, value!));
        }
        if (!string.IsNullOrEmpty(node.ElementId)) Add("id", node.ElementId);
        if (node.ClassNames is { Count: > 0 })
            Add("class", string.Join(' ', node.ClassNames));
        if (node.Attributes != null)
        {
            foreach (var pair in node.Attributes)
                Add(pair.Key, pair.Value);
        }
        if (!string.IsNullOrEmpty(node.ComponentName)) Add("data-square-component", node.ComponentName);
        return attributes;
    }

    private static void CollectKeys(ElementInspectionNode node, HashSet<NodeKey> keys)
    {
        keys.Add(NodeKeyOf(node, CdpNodeKind.Element));
        if (node.Text != null)
            keys.Add(NodeKeyOf(node, CdpNodeKind.Text));
        foreach (var child in node.Children)
            CollectKeys(child, keys);
    }

    private static Dictionary<NodeKey, ElementInspectionNode> CollectLookup(ElementInspectionSnapshot snapshot)
    {
        var lookup = new Dictionary<NodeKey, ElementInspectionNode>();
        lookup.Add(new NodeKey(snapshot.Root.Id, CdpNodeKind.Document), snapshot.Root);
        CollectLookupChildren(snapshot.Root, lookup);
        return lookup;
    }

    private static void CollectLookupChildren(ElementInspectionNode node, Dictionary<NodeKey, ElementInspectionNode> lookup)
    {
        lookup.Add(NodeKeyOf(node, CdpNodeKind.Element), node);
        if (node.Text != null)
            lookup.Add(NodeKeyOf(node, CdpNodeKind.Text), node);
        foreach (var child in node.Children)
            CollectLookupChildren(child, lookup);
    }

    private static bool TryFindNode(ElementInspectionSnapshot snapshot, NodeKey key, out ElementInspectionNode node)
    {
        if (key.Kind == CdpNodeKind.Document)
        {
            node = snapshot.Root;
            return node.Id == key.DebugId;
        }
        return TryFindElement(snapshot.Root, key, out node);
    }

    private static bool TryFindElement(ElementInspectionNode current, NodeKey key, out ElementInspectionNode node)
    {
        if (current.Id == key.DebugId && (key.Kind == CdpNodeKind.Element || current.Text != null))
        {
            node = current;
            return true;
        }
        foreach (var child in current.Children)
            if (TryFindElement(child, key, out node)) return true;
        node = null!;
        return false;
    }

    /// <summary>Collects the element chain from below the root down to (and excluding) the document itself.</summary>
    private static bool TryCollectChain(ElementInspectionNode node, int debugId, List<ElementInspectionNode> chain)
    {
        foreach (var child in node.Children)
        {
            chain.Add(child);
            if (child.Id == debugId) return true;
            if (TryCollectChain(child, debugId, chain)) return true;
            chain.RemoveAt(chain.Count - 1);
        }
        return false;
    }

    private int GetOrCreateFrontendId(NodeKey key)
    {
        if (_frontendIds.TryGetValue(key, out var id)) return id;
        if (key.Kind == CdpNodeKind.Document && !_firstFrontendDocumentAssigned)
        {
            _firstFrontendDocumentAssigned = true;
            id = 1;
        }
        else id = ++_nextFrontendId;
        _frontendIds[key] = id;
        _frontendKeys[id] = key;
        return id;
    }

    private int GetOrCreateBackendId(NodeKey key)
    {
        if (_backendIds.TryGetValue(key, out var id)) return id;
        if (key.Kind == CdpNodeKind.Document && !_firstBackendDocumentAssigned)
        {
            _firstBackendDocumentAssigned = true;
            id = 1;
        }
        else id = ++_nextBackendId;
        _backendIds[key] = id;
        _backendKeys[id] = key;
        return id;
    }

    private void ResetNodeMaps()
    {
        _frontendIds.Clear();
        _frontendKeys.Clear();
        _backendIds.Clear();
        _backendKeys.Clear();
        _frontendParents.Clear();
        _publishedNodes.Clear();
        _expandedParents.Clear();
        _publishedSnapshots.Clear();
        _objectKeys.Clear();
        _documentRequested = false;
        _baselineRoot = null;
        // Reset node identity invalidates every computed tracking target and baseline.
        CancelForNodeDelay();
        _trackedForNodeKey = null;
        _forNodeBaseline = null;
        _batchBaselines.Clear();
        _pendingTakeNodeIds.Clear();
    }

    private void SetBaseline(ElementInspectionSnapshot snapshot)
    {
        _baselineRoot = snapshot.Root;
    }

    private void EnableDom()
    {
        if (_domEnabled) return;
        _domEnabled = true;
        ResetNodeMaps();
        EnsureInspectionSubscription();
    }

    /// <summary>
    /// Serializes a snapshot node as a CDP node, assigning frontend/backend IDs and updating
    /// publication bookkeeping to match exactly what is written. depth: -1 = whole subtree,
    /// 0 = node only, n = children included down to n levels below this node.
    /// </summary>
    private int WriteDomNode(
        Utf8JsonWriter writer,
        ElementInspectionNode node,
        CdpNodeKind kind,
        int parentFrontendId,
        int depth,
        bool writeParentId,
        bool publish = true)
    {
        var key = NodeKeyOf(node, kind);
        var frontendId = GetOrCreateFrontendId(key);
        if (publish)
        {
            _publishedNodes.Add(frontendId);
            _frontendParents[frontendId] = parentFrontendId;
            _publishedSnapshots[key] = node;
        }
        var backendNodeId = GetOrCreateBackendId(key);

        writer.WriteStartObject();
        writer.WriteNumber("nodeId", frontendId);
        writer.WriteNumber("backendNodeId", backendNodeId);
        writer.WriteNumber("nodeType", kind switch
        {
            CdpNodeKind.Document => 9,
            CdpNodeKind.Text => 3,
            _ => 1,
        });
        writer.WriteString("nodeName", kind switch
        {
            CdpNodeKind.Document => "#document",
            CdpNodeKind.Text => "#text",
            _ => node.TagName.ToUpperInvariant(),
        });
        writer.WriteString("localName", kind == CdpNodeKind.Element ? node.TagName.ToLowerInvariant() : "");
        writer.WriteString("nodeValue", kind == CdpNodeKind.Text ? node.Text ?? "" : "");
        if (writeParentId && parentFrontendId != 0)
            writer.WriteNumber("parentId", parentFrontendId);
        if (kind == CdpNodeKind.Document)
            writer.WriteString("documentURL", "square://application");
        if (kind == CdpNodeKind.Element)
        {
            writer.WritePropertyName("attributes");
            writer.WriteStartArray();
            foreach (var attribute in MergeAttributes(node))
            {
                writer.WriteStringValue(attribute.Name);
                writer.WriteStringValue(attribute.Value);
            }
            writer.WriteEndArray();
        }
        if (kind == CdpNodeKind.Text)
        {
            writer.WriteEndObject();
            return frontendId;
        }

        var childCount = kind == CdpNodeKind.Document ? 1 : node.Children.Count + (node.Text != null ? 1 : 0);
        writer.WriteNumber("childNodeCount", childCount);
        if (depth != 0)
        {
            if (publish) _expandedParents.Add(frontendId);
            writer.WritePropertyName("children");
            writer.WriteStartArray();
            var childDepth = depth < 0 ? -1 : depth - 1;
            foreach (var child in BuildChildEntries(node, kind))
                WriteDomNode(writer, child.Node, child.Kind, frontendId, childDepth, writeParentId: false, publish);
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
        return frontendId;
    }

    private async Task RemoveReparentedNodesAsync(ElementInspectionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var parents = new Dictionary<NodeKey, NodeKey>();
        var rootKey = NodeKeyOf(snapshot.Root, CdpNodeKind.Element);
        parents[rootKey] = NodeKeyOf(snapshot.Root, CdpNodeKind.Document);
        CollectParentKeys(snapshot.Root, parents);
        var oldLookup = CollectLookup(new ElementInspectionSnapshot(_baselineRoot!));
        foreach (var pair in _frontendParents.ToArray())
        {
            if (!_publishedNodes.Contains(pair.Key) || pair.Value == 0 ||
                !_frontendKeys.TryGetValue(pair.Key, out var key) || !parents.TryGetValue(key, out var parent)) continue;
            if (_frontendIds.TryGetValue(parent, out var currentParent) && currentParent == pair.Value) continue;
            if (_publishedNodes.Contains(pair.Value))
                await SendEventAsync("DOM.childNodeRemoved", writer =>
                {
                    writer.WriteNumber("parentNodeId", pair.Value);
                    writer.WriteNumber("nodeId", pair.Key);
                }, cancellationToken);
            if (oldLookup.TryGetValue(key, out var oldNode)) UnpublishSubtree(oldNode, key.Kind);
        }
    }

    private static void CollectParentKeys(ElementInspectionNode node, Dictionary<NodeKey, NodeKey> parents)
    {
        var key = NodeKeyOf(node, CdpNodeKind.Element);
        if (node.Text != null) parents[NodeKeyOf(node, CdpNodeKind.Text)] = key;
        foreach (var child in node.Children)
        {
            parents[NodeKeyOf(child, CdpNodeKind.Element)] = key;
            CollectParentKeys(child, parents);
        }
    }

    /// <summary>
    /// Diffs one identity-stable node between the previous and current snapshots, emitting
    /// removals before insertions per expanded parent so previousSibling order stays valid.
    /// </summary>
    private async Task DiffNodeAsync(
        ElementInspectionNode oldNode,
        ElementInspectionNode newNode,
        CdpNodeKind kind,
        HashSet<NodeKey> liveKeys,
        CancellationToken cancellationToken)
    {
        var key = NodeKeyOf(newNode, kind);
        if (!_frontendIds.TryGetValue(key, out var frontendId) || !_publishedNodes.Contains(frontendId)) return;
        var publishedNode = _publishedSnapshots.TryGetValue(key, out var published) ? published : oldNode;

        if (kind == CdpNodeKind.Text)
        {
            if (!string.Equals(publishedNode.Text, newNode.Text, StringComparison.Ordinal))
            {
                await SendEventAsync("DOM.characterDataModified", writer =>
                {
                    writer.WriteNumber("nodeId", frontendId);
                    writer.WriteString("characterData", newNode.Text ?? "");
                }, cancellationToken);
            }
            _publishedSnapshots[key] = newNode;
            return;
        }

        if (kind == CdpNodeKind.Element)
            await DiffAttributesAsync(frontendId, publishedNode, newNode, cancellationToken);
        _publishedSnapshots[key] = newNode;

        var oldEntries = BuildChildEntries(oldNode, kind);
        var newEntries = BuildChildEntries(newNode, kind);
        if (!_expandedParents.Contains(frontendId))
        {
            if (oldEntries.Count != newEntries.Count)
            {
                await SendEventAsync("DOM.childNodeCountUpdated", writer =>
                {
                    writer.WriteNumber("nodeId", frontendId);
                    writer.WriteNumber("childNodeCount", newEntries.Count);
                }, cancellationToken);
            }
            return;
        }

        var oldByKey = new Dictionary<NodeKey, ElementInspectionNode>();
        foreach (var entry in oldEntries)
            oldByKey[entry.Key] = entry.Node;
        var newKeySet = new HashSet<NodeKey>();
        foreach (var entry in newEntries)
            newKeySet.Add(entry.Key);
        var oldKeys = new List<NodeKey>(oldEntries.Count);
        foreach (var entry in oldEntries)
            oldKeys.Add(entry.Key);
        var newKeys = new List<NodeKey>(newEntries.Count);
        foreach (var entry in newEntries)
            newKeys.Add(entry.Key);
        var keptOld = oldKeys.Where(newKeySet.Contains).ToList();
        var oldKeySet = new HashSet<NodeKey>(oldKeys);
        var keptNew = newKeys.Where(oldKeySet.Contains).ToList();
        var anchor = keptOld.SequenceEqual(keptNew)
            ? new HashSet<NodeKey>(keptOld)
            : LcsAnchor(keptOld, keptNew);

        // Phase one: removals (moved nodes keep their live identity until re-inserted below).
        foreach (var entry in oldEntries)
        {
            if (anchor.Contains(entry.Key) || !_frontendIds.TryGetValue(entry.Key, out var removedNodeId) ||
                !_publishedNodes.Contains(removedNodeId) ||
                !_frontendParents.TryGetValue(removedNodeId, out var parent) || parent != frontendId) continue;
            await SendEventAsync("DOM.childNodeRemoved", writer =>
            {
                writer.WriteNumber("parentNodeId", frontendId);
                writer.WriteNumber("nodeId", removedNodeId);
            }, cancellationToken);
            UnpublishSubtree(entry.Node, entry.Kind);
        }

        // Phase two: insertions in new order, each after its (already published) previous sibling.
        // A mapped key is skipped only when the frontend already shows it under THIS parent;
        // otherwise it moved here (its removal was emitted in phase one) or was published as a
        // floating/other-parent node by describe/push and must be attached now.
        for (var index = 0; index < newEntries.Count; index++)
        {
            var entry = newEntries[index];
            if (anchor.Contains(entry.Key)) continue;
            if (_frontendIds.TryGetValue(entry.Key, out var attachedId) &&
                _frontendParents.TryGetValue(attachedId, out var attachedParent) &&
                attachedParent == frontendId)
            {
                continue;
            }
            var previousNodeId = 0;
            for (var previous = index - 1; previous >= 0; previous--)
            {
                if (_frontendIds.TryGetValue(newEntries[previous].Key, out previousNodeId)) break;
            }
            await SendEventAsync("DOM.childNodeInserted", writer =>
            {
                writer.WriteNumber("parentNodeId", frontendId);
                writer.WriteNumber("previousNodeId", previousNodeId);
                writer.WritePropertyName("node");
                var depth = _frontendIds.TryGetValue(entry.Key, out var movedId) && _expandedParents.Contains(movedId) ? -1 : 1;
                WriteDomNode(writer, entry.Node, entry.Kind, frontendId, depth, writeParentId: false);
            }, cancellationToken);
        }

        foreach (var entry in newEntries)
        {
            if (!anchor.Contains(entry.Key)) continue;
            await DiffNodeAsync(oldByKey[entry.Key], entry.Node, entry.Kind, liveKeys, cancellationToken);
        }
    }

    private async Task DiffAttributesAsync(
        int frontendId,
        ElementInspectionNode oldNode,
        ElementInspectionNode newNode,
        CancellationToken cancellationToken)
    {
        var oldAttributes = MergeAttributes(oldNode);
        var newAttributes = MergeAttributes(newNode);
        if (oldAttributes.Count == 0 && newAttributes.Count == 0) return;
        var newValues = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attribute in newAttributes)
            newValues[attribute.Name] = attribute.Value;
        var oldValues = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attribute in oldAttributes)
            oldValues[attribute.Name] = attribute.Value;

        foreach (var attribute in oldAttributes)
        {
            if (!newValues.TryGetValue(attribute.Name, out var value))
            {
                await SendEventAsync("DOM.attributeRemoved", writer =>
                {
                    writer.WriteNumber("nodeId", frontendId);
                    writer.WriteString("name", attribute.Name);
                }, cancellationToken);
            }
            else if (!string.Equals(value, attribute.Value, StringComparison.Ordinal))
            {
                await SendEventAsync("DOM.attributeModified", writer =>
                {
                    writer.WriteNumber("nodeId", frontendId);
                    writer.WriteString("name", attribute.Name);
                    writer.WriteString("value", value);
                }, cancellationToken);
            }
        }
        foreach (var attribute in newAttributes)
        {
            if (oldValues.ContainsKey(attribute.Name)) continue;
            await SendEventAsync("DOM.attributeModified", writer =>
            {
                writer.WriteNumber("nodeId", frontendId);
                writer.WriteString("name", attribute.Name);
                writer.WriteString("value", attribute.Value);
            }, cancellationToken);
        }
    }

    private void UnpublishSubtree(ElementInspectionNode node, CdpNodeKind kind)
    {
        var key = NodeKeyOf(node, kind);
        if (_frontendIds.TryGetValue(key, out var id))
        {
            _publishedNodes.Remove(id);
            _frontendParents.Remove(id);
            _publishedSnapshots.Remove(key);
        }
        if (kind == CdpNodeKind.Text) return;
        foreach (var child in BuildChildEntries(node, kind)) UnpublishSubtree(child.Node, child.Kind);
    }

    private void PruneKey(NodeKey key)
    {
        if (_frontendIds.Remove(key, out var frontendId))
        {
            _frontendKeys.Remove(frontendId);
            _frontendParents.Remove(frontendId);
            _publishedNodes.Remove(frontendId);
            _expandedParents.Remove(frontendId);
            if (_objectKeys.Count != 0) _objectKeys.Remove($"square-node-{frontendId}");
            _pendingTakeNodeIds.Remove(frontendId);
        }
        if (_backendIds.Remove(key, out var backendId))
            _backendKeys.Remove(backendId);
        _publishedSnapshots.Remove(key);
        _batchBaselines.Remove(key);
        if (_trackedForNodeKey == key)
        {
            CancelForNodeDelay();
            _trackedForNodeKey = null;
            _forNodeBaseline = null;
        }
    }

    /// <summary>
    /// Anchors the longest common subsequence of the kept child keys so only genuinely
    /// moved/added/removed children produce events. Falls back to prefix/suffix anchoring
    /// when the DP table would be too large.
    /// </summary>
    private static HashSet<NodeKey> LcsAnchor(List<NodeKey> a, List<NodeKey> b)
    {
        var anchor = new HashSet<NodeKey>();
        var start = 0;
        var endA = a.Count;
        var endB = b.Count;
        while (start < endA && start < endB && a[start].Equals(b[start]))
        {
            anchor.Add(a[start]);
            start++;
        }
        while (endA > start && endB > start && a[endA - 1].Equals(b[endB - 1]))
        {
            endA--;
            endB--;
            anchor.Add(a[endA]);
        }
        var coreA = endA - start;
        var coreB = endB - start;
        if (coreA == 0 || coreB == 0 || (long)coreA * coreB > 262144) return anchor;

        var lengths = new int[coreA + 1, coreB + 1];
        for (var i = 1; i <= coreA; i++)
        {
            for (var j = 1; j <= coreB; j++)
            {
                lengths[i, j] = a[start + i - 1].Equals(b[start + j - 1])
                    ? lengths[i - 1, j - 1] + 1
                    : Math.Max(lengths[i - 1, j], lengths[i, j - 1]);
            }
        }
        var x = coreA;
        var y = coreB;
        while (x > 0 && y > 0)
        {
            if (a[start + x - 1].Equals(b[start + y - 1]))
            {
                anchor.Add(a[start + x - 1]);
                x--;
                y--;
            }
            else if (lengths[x - 1, y] >= lengths[x, y - 1])
            {
                x--;
            }
            else
            {
                y--;
            }
        }
        return anchor;
    }

    /// <summary>
    /// Resolves nodeId/backendNodeId parameters to a node key via the explicit maps only;
    /// unknown IDs are errors, never aliases of each other.
    /// </summary>
    private NodeKey ResolveNodeKey(JsonElement parameters)
    {
        if (parameters.ValueKind == JsonValueKind.Object)
        {
            if (parameters.TryGetProperty("objectId", out var objectId))
            {
                if (objectId.ValueKind != JsonValueKind.String) throw new CdpRequestException(-32602, "Invalid object ID.");
                if (_objectKeys.TryGetValue(objectId.GetString()!, out var objectKey)) return objectKey;
                throw new CdpRequestException(-32000, "Node not found.");
            }
            if (parameters.TryGetProperty("nodeId", out var nodeIdElement) &&
                nodeIdElement.ValueKind == JsonValueKind.Number &&
                nodeIdElement.TryGetInt32(out var nodeId))
            {
                if (_frontendKeys.TryGetValue(nodeId, out var frontendKey)) return frontendKey;
                throw new CdpRequestException(-32000, "Node not found.");
            }
            if (parameters.TryGetProperty("backendNodeId", out var backendElement) &&
                backendElement.ValueKind == JsonValueKind.Number &&
                backendElement.TryGetInt32(out var backendNodeId))
            {
                if (_backendKeys.TryGetValue(backendNodeId, out var backendKey)) return backendKey;
                throw new CdpRequestException(-32000, "Node not found.");
            }
        }
        throw new CdpRequestException(-32602, "'nodeId' or 'backendNodeId' is required.");
    }

    /// <summary>Reads a DOM depth parameter: default when missing; -1, or a positive integer for document commands.</summary>
    private static int ReadDepth(JsonElement parameters, int defaultDepth)
    {
        if (parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("depth", out var depthElement)) return defaultDepth;
        if (depthElement.ValueKind != JsonValueKind.Number || !depthElement.TryGetInt32(out var depth))
            throw new CdpRequestException(-32602, "Invalid depth.");
        return depth;
    }

    private static int[] ReadBackendIdArray(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("backendNodeIds", out var arrayElement) ||
            arrayElement.ValueKind != JsonValueKind.Array)
        {
            throw new CdpRequestException(-32602, "'backendNodeIds' must be an array.");
        }
        var backendNodeIds = new int[arrayElement.GetArrayLength()];
        for (var index = 0; index < backendNodeIds.Length; index++)
        {
            var entry = arrayElement[index];
            if (entry.ValueKind != JsonValueKind.Number || !entry.TryGetInt32(out backendNodeIds[index]))
                throw new CdpRequestException(-32602, "'backendNodeIds' must be an array of integers.");
        }
        return backendNodeIds;
    }

    private static void WriteOuterHtml(StringBuilder builder, ElementInspectionNode node, CdpNodeKind kind)
    {
        if (kind == CdpNodeKind.Document)
        {
            WriteOuterHtml(builder, node, CdpNodeKind.Element);
            return;
        }
        if (kind == CdpNodeKind.Text)
        {
            builder.Append(System.Net.WebUtility.HtmlEncode(node.Text));
            return;
        }

        builder.Append('<').Append(node.TagName.ToLowerInvariant());
        foreach (var attribute in MergeAttributes(node))
        {
            builder.Append(' ').Append(attribute.Name).Append("=\"")
                .Append(System.Net.WebUtility.HtmlEncode(attribute.Value)).Append('\"');
        }
        builder.Append('>');
        foreach (var child in node.Children)
            WriteOuterHtml(builder, child, CdpNodeKind.Element);
        if (node.Text != null)
            WriteOuterHtml(builder, node, CdpNodeKind.Text);
        builder.Append("</").Append(node.TagName.ToLowerInvariant()).Append('>');
    }

    #endregion
}
