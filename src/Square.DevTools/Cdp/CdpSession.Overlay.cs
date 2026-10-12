using System.Text.Json;
using Square.Graphics;
using Square.Hosting;

namespace Square.DevTools;

// Overlay domain: highlight configuration parsing, inspect modes and live element resolution.
internal sealed partial class CdpSession
{
    // CDP sessions always own the window overlay through a process-wide monotonic,
    // non-zero owner ID; 0 stays reserved for the application's own inspector control.
    private static long _nextOwnerId;

    private readonly long _ownerId = Interlocked.Increment(ref _nextOwnerId);
    private bool _overlayEnabled;
    private async Task HighlightNodeAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var options = ParseHighlightConfig(parameters);
        var key = ResolveNodeKey(parameters);
        var debugId = await ResolveLiveElementDebugIdAsync(key, cancellationToken);
        if (!await _window
            .SetInspectorHighlightAsync(debugId, options, _ownerId, cancellationToken)
            .WaitAsync(cancellationToken))
        {
            await SendErrorAsync(id, -32000, "Node not found.", cancellationToken);
            return;
        }
        _overlayEnabled = true;
        await SendEmptyResultAsync(id, cancellationToken);
    }

    private async Task SetInspectModeAsync(int id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var mode = parameters.ValueKind == JsonValueKind.Object &&
                   parameters.TryGetProperty("mode", out var modeElement) && modeElement.ValueKind == JsonValueKind.String
            ? modeElement.GetString()
            : null;
        switch (mode)
        {
            case "none":
            case "searchForNode":
            case "searchForUAShadowDOM":
                break;
            case "captureAreaScreenshot":
                // Rejected before any state change: this never enables inspect mode.
                throw new CdpRequestException(-32601, "Overlay capture-area inspection is not supported.");
            default:
                throw new CdpRequestException(-32602, "Invalid inspect mode.");
        }
        var options = ParseHighlightConfig(parameters);
        var enabled = mode != "none";
        await _window.SetInspectorModeAsync(enabled, options, _ownerId, cancellationToken).WaitAsync(cancellationToken);
        if (enabled) _overlayEnabled = true;
        await SendEmptyResultAsync(id, cancellationToken);
    }

    /// <summary>
    /// Resolves an overlay target to a live element debug ID before any native mutation.
    /// Native highlights elements only, so text and document keys never masquerade as their
    /// element by ID collision: they are mapped through a fresh snapshot (text to its owning
    /// element, document to the root element) and must still exist.
    /// </summary>
    private async Task<int> ResolveLiveElementDebugIdAsync(NodeKey key, CancellationToken cancellationToken)
    {
        if (key.Kind == CdpNodeKind.Element) return key.DebugId;
        var snapshot = await _window.CaptureInspectionSnapshotAsync(includeSourcePaths: false, includeTextContent: true)
            .WaitAsync(cancellationToken);
        if (key.Kind == CdpNodeKind.Document)
        {
            if (snapshot.Root.Id != key.DebugId) throw new CdpRequestException(-32000, "Node not found.");
            return snapshot.Root.Id;
        }
        if (!TryFindNode(snapshot, key, out var node)) throw new CdpRequestException(-32000, "Node not found.");
        return node.Id;
    }

    /// <summary>
    /// Parses Overlay highlightConfig: an absent config uses the standard default
    /// colors, a present config renders missing layers transparent, RGBA channels take
    /// integers 0-255 and alpha defaults to 1.
    /// </summary>
    private static ElementInspectionHighlightOptions ParseHighlightConfig(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("highlightConfig", out var config))
        {
            return DefaultHighlightOptions;
        }
        if (config.ValueKind != JsonValueKind.Object)
            throw new CdpRequestException(-32602, "Invalid highlight configuration.");
        return new ElementInspectionHighlightOptions(
            ReadHighlightLayerColor(config, "contentColor"),
            ReadHighlightLayerColor(config, "paddingColor"),
            ReadHighlightLayerColor(config, "borderColor"),
            ReadHighlightLayerColor(config, "marginColor"));
    }

    private static readonly ElementInspectionHighlightOptions DefaultHighlightOptions = new(
        new Color(111, 168, 220, AlphaByte(0.66)),
        new Color(147, 196, 125, AlphaByte(0.55)),
        new Color(250, 215, 132, AlphaByte(0.65)),
        new Color(246, 178, 107, AlphaByte(0.55)));

    private static byte AlphaByte(double alpha) => (byte)Math.Round(alpha * 255d);

    /// <summary>Reads one config layer; a missing layer is fully transparent.</summary>
    private static Color ReadHighlightLayerColor(JsonElement config, string name)
    {
        if (!config.TryGetProperty(name, out var element))
            return Color.Transparent;
        if (element.ValueKind != JsonValueKind.Object)
            throw new CdpRequestException(-32602, "Invalid highlight configuration.");
        var alpha = 1d;
        if (element.TryGetProperty("a", out var alphaElement))
        {
            if (alphaElement.ValueKind != JsonValueKind.Number || !alphaElement.TryGetDouble(out alpha) ||
                !double.IsFinite(alpha) || alpha is < 0d or > 1d)
                throw new CdpRequestException(-32602, "Invalid highlight configuration.");
        }
        return new Color(
            ReadHighlightChannel(element, "r"),
            ReadHighlightChannel(element, "g"),
            ReadHighlightChannel(element, "b"),
            AlphaByte(alpha));
    }

    private static byte ReadHighlightChannel(JsonElement color, string name)
    {
        if (!color.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out var channel) || channel is < 0 or > 255)
            throw new CdpRequestException(-32602, "Invalid highlight configuration.");
        return (byte)channel;
    }
}
