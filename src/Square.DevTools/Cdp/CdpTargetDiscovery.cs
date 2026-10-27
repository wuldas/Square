using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Square.DevTools;

/// <summary>
/// HTTP discovery payloads: /json/version, /json/list and the explicit /json/protocol
/// description of the implemented Chrome DevTools subset. Every declared command,
/// parameter, return, event and type below mirrors the actual dispatcher in
/// <see cref="CdpSession"/>; probes the frontend sends for compatibility are documented
/// as acknowledged without effect instead of being presented as implemented behavior.
/// </summary>
internal static class CdpTargetDiscovery
{
    /// <summary>
    /// The complete implemented domain set, fixed and shared by Schema.getDomains and the
    /// protocol metadata. SerializeProtocol writes its domains in exactly this order.
    /// </summary>
    internal static readonly string[] DomainNames =
    [
        "DOM", "Runtime", "Page", "Target", "CSS", "Overlay",
        "Inspector", "Console", "Log", "Network", "Schema",
    ];

    public static string SerializeVersion(DevToolsServer server)
    {
        var websocket = server.CdpWebSocketAddress;
        return $"{{\"Browser\":\"Square.DevTools/0.1\",\"Protocol-Version\":\"1.3\"," +
               $"\"User-Agent\":\"Square.DevTools\",\"V8-Version\":\"\",\"WebKit-Version\":\"\"," +
               $"\"webSocketDebuggerUrl\":\"{Escape(websocket)}\"}}";
    }

    public static string SerializeList(DevToolsServer server)
    {
        var websocket = server.CdpWebSocketAddress;
        var frontend = $"/devtools/inspector.html?ws=127.0.0.1:{server.Port}/devtools/page/{server.TargetId}";
        return $"[{{\"description\":\"Square application target\",\"devtoolsFrontendUrl\":\"{Escape(frontend)}\"," +
               $"\"id\":\"{Escape(server.TargetId)}\",\"title\":\"Square DevTools\",\"type\":\"page\"," +
               $"\"url\":\"square://application/{server.TargetId}\",\"webSocketDebuggerUrl\":\"{Escape(websocket)}\"}}]";
    }

    /// <summary>
    /// Writes the complete "domains" property array (the eleven implemented domains, each
    /// with version 1.3) into the caller's current object. Consumed by Schema.getDomains.
    /// </summary>
    internal static void WriteDomains(Utf8JsonWriter writer)
    {
        writer.WritePropertyName("domains");
        writer.WriteStartArray();
        foreach (var name in DomainNames)
        {
            writer.WriteStartObject();
            writer.WriteString("name", name);
            writer.WriteString("version", "1.3");
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    public static string SerializeProtocol()
    {
        var buffer = new ArrayBufferWriter<byte>(96 * 1024);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("version");
            writer.WriteString("major", "1");
            writer.WriteString("minor", "3");
            writer.WriteEndObject();
            writer.WritePropertyName("domains");
            writer.WriteStartArray();
            // Order mirrors DomainNames (DOM Runtime Page Target CSS Overlay Inspector
            // Console Log Network Schema).
            WriteDomDomain(writer);
            WriteRuntimeDomain(writer);
            WritePageDomain(writer);
            WriteTargetDomain(writer);
            WriteCssDomain(writer);
            WriteOverlayDomain(writer);
            WriteInspectorDomain(writer);
            WriteConsoleDomain(writer);
            WriteLogDomain(writer);
            WriteNetworkDomain(writer);
            WriteSchemaDomain(writer);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteDomDomain(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("domain", "DOM");
        writer.WriteString("version", "1.3");
        writer.WriteString("description",
            "Read-only inspection of the live Square element tree. Node identity is a pair of " +
            "(native debug id, node kind) mapped to a session-local frontend nodeId and a monotonic " +
            "backendNodeId; synthetic text nodes (kind 3) are children of their element and the " +
            "document root is kind 9. Structural and attribute changes are streamed incrementally " +
            "while the document stays alive. Mutating DOM commands are not implemented.");
        writer.WritePropertyName("types");
        writer.WriteStartArray();
        WriteObjectType(writer, "Node",
            "One serialized node: the document root, a real element or a synthetic text leaf of " +
            "its element. Both id spaces are monotonic and never reused within a session; a full " +
            "document reset invalidates them all and is announced with DOM.documentUpdated.", static writer =>
        {
            WriteInteger(writer, "nodeId",
                "Session-local frontend node id. 0 appears in event references (previousNodeId, " +
                "unpublished results) to mean 'unknown'.");
            WriteInteger(writer, "backendNodeId",
                "Backend id mapped from the native node identity; stable while the node lives.");
            WriteInteger(writer, "nodeType",
                "9 for the document, 1 for elements, 3 for synthetic text.");
            WriteString(writer, "nodeName",
                "\"#document\", \"#text\" or the uppercased element tag name.");
            WriteString(writer, "localName",
                "Lowercased element tag name; empty for documents and text.");
            WriteString(writer, "nodeValue",
                "Text content of text nodes, otherwise empty.");
            WriteInteger(writer, "parentId", "Present when the parent is published to this session.", optional: true);
            WriteString(writer, "documentURL", "Document nodes only: square://application.", optional: true);
            WriteStringArray(writer, "attributes",
                "Element nodes only: interleaved attribute names and values, merged from the real " +
                "element attributes and id/class/component metadata without duplicates.",
                optional: true);
            WriteInteger(writer, "childNodeCount",
                "Element and document nodes only; synthetic text children are counted.",
                optional: true);
            WriteRefArray(writer, "children", "Node",
                "Present when the subtree was serialized at the requested depth.", optional: true);
        });
        WriteArrayType(writer, "Quad", "number",
            "Eight numbers: the x,y pairs of the top-left, top-right, bottom-right and bottom-left " +
            "corners in window client coordinates.");
        WriteObjectType(writer, "BoxModel",
            "The four layer boxes of a live element. Missing or unprovable layers still report the " +
            "measured quads; commands that cannot measure the node fail with -32000.", static writer =>
        {
            WriteRef(writer, "content", "Quad");
            WriteRef(writer, "padding", "Quad");
            WriteRef(writer, "border", "Quad");
            WriteRef(writer, "margin", "Quad");
            WriteNumber(writer, "width", "Border-box width.");
            WriteNumber(writer, "height", "Border-box height.");
        });
        writer.WriteEndArray();
        writer.WritePropertyName("commands");
        writer.WriteStartArray();
        WriteCommand(writer, "enable", "Enables DOM event delivery for this session. Idempotent.");
        WriteCommand(writer, "disable",
            "Stops DOM events and invalidates this session's node maps and object handles.");
        WriteCommandWith(writer, "getDocument",
            "Returns the document root and establishes the incremental-diff baseline; implicitly " +
            "enables the DOM domain without emitting documentUpdated.",
            parameters: static writer => WriteInteger(writer, "depth",
                "Subtree depth to include. Default 1; -1 returns the whole tree; other values must " +
                "be positive integers or the request fails with -32602.",
                optional: true),
            returns: static writer => WriteRef(writer, "root", "Node"));
        WriteCommandWith(writer, "getFlattenedDocument",
            "Returns the document subtree as a flat node list with parentId references.",
            parameters: static writer => WriteInteger(writer, "depth",
                "Default 1; -1 returns the whole tree; other values must be positive integers.",
                optional: true),
            returns: static writer => WriteRefArray(writer, "nodes", "Node"));
        WriteCommandWith(writer, "requestChildNodes",
            "Publishes the children of a known node and delivers them as DOM.setChildNodes before " +
            "the (empty) response. Only published element/document nodes are accepted.",
            parameters: static writer =>
            {
                WriteNodeSelector(writer);
                WriteInteger(writer, "depth",
                    "Depth below the parent. Default 1; -1 fetches everything below.",
                    optional: true);
            });
        WriteCommandWith(writer, "describeNode",
            "Returns a single node without publishing it. Default depth 0 (node only); -1 includes " +
            "the whole subtree; other negative values fail with -32602.",
            parameters: static writer =>
            {
                WriteNodeSelector(writer);
                WriteInteger(writer, "depth", "Default 0; -1 includes the subtree.", optional: true);
            },
            returns: static writer => WriteRef(writer, "node", "Node"));
        WriteCommandWith(writer, "getAttributes",
            "Returns the attributes of a known element node; documents report an empty list.",
            parameters: WriteNodeSelector,
            returns: static writer => WriteStringArray(writer, "attributes",
                "Interleaved attribute names and values."));
        WriteCommandWith(writer, "getNodeForLocation",
            "Hit-tests the live window content area.",
            parameters: static writer =>
            {
                WriteNumber(writer, "x", "Client x coordinate.");
                WriteNumber(writer, "y", "Client y coordinate.");
            },
            returns: static writer =>
            {
                WriteInteger(writer, "backendNodeId", "Backend id of the hit element.", optional: true);
                WriteInteger(writer, "nodeId",
                    "Frontend id of the hit element; 0 when it has not been published yet.",
                    optional: true);
                WriteString(writer, "frameId", "The target id.", optional: true);
            });
        WriteCommandWith(writer, "getBoxModel",
            "Element nodes only; text and document keys fail with -32000, as do unmeasurable nodes.",
            parameters: WriteNodeSelector,
            returns: static writer => WriteRef(writer, "model", "BoxModel"));
        WriteCommandWith(writer, "getOuterHTML",
            "Serializes the node (with its subtree for elements) to an HTML-like string.",
            parameters: WriteNodeSelector,
            returns: static writer => WriteString(writer, "outerHTML"));
        WriteCommandWith(writer, "resolveNode",
            "Returns a RemoteObject-shaped handle for the node. Square has no script engine: the " +
            "objectId only names the node for later DOM commands.",
            parameters: WriteNodeSelector,
            returns: static writer => WriteObject(writer, "object",
                "Object handle; type is always \"object\" and subtype \"node\".", static writer =>
                {
                    WriteString(writer, "type", "Always \"object\".");
                    WriteString(writer, "subtype", "Always \"node\".");
                    WriteString(writer, "objectId",
                        "Handle of the form square-node-{frontendNodeId}; released when the node dies.");
                    WriteString(writer, "description", "Always \"Square Element\".");
                }));
        WriteCommandWith(writer, "setInspectedNode",
            "Acknowledged without effect; console $0 substitution is not available.",
            parameters: static writer => WriteInteger(writer, "nodeId"));
        WriteCommandWith(writer, "pushNodesByBackendIdsToFrontend",
            "Publishes unknown ancestors along each root-to-node path (via DOM.setChildNodes) and " +
            "returns real frontend ids. Fails with -32000 when no document was requested yet.",
            parameters: static writer => WriteIntegerArray(writer, "backendNodeIds",
                "Backend ids in request order."),
            returns: static writer => WriteIntegerArray(writer, "nodeIds",
                "Same length and order as the input; 0 for ids that do not resolve to a live node."));
        writer.WriteEndArray();
        writer.WritePropertyName("events");
        writer.WriteStartArray();
        WriteEvent(writer, "documentUpdated",
            "The whole document was reset (e.g. the window content was rebuilt); every previous " +
            "node id, backend id and object handle is invalid.");
        WriteEventWith(writer, "setChildNodes", static writer =>
        {
            WriteInteger(writer, "parentId");
            WriteRefArray(writer, "nodes", "Node");
        });
        WriteEventWith(writer, "attributeModified", static writer =>
        {
            WriteInteger(writer, "nodeId");
            WriteString(writer, "name");
            WriteString(writer, "value");
        });
        WriteEventWith(writer, "attributeRemoved", static writer =>
        {
            WriteInteger(writer, "nodeId");
            WriteString(writer, "name");
        });
        WriteEventWith(writer, "characterDataModified", static writer =>
        {
            WriteInteger(writer, "nodeId", "The synthetic text node id.");
            WriteString(writer, "characterData", "The full new text.");
        });
        WriteEventWith(writer, "childNodeCountUpdated", static writer =>
        {
            WriteInteger(writer, "nodeId");
            WriteInteger(writer, "childNodeCount", "New child count, including synthetic text.");
        }, "Emitted for parents that have not been expanded by the frontend.");
        WriteEventWith(writer, "childNodeInserted", static writer =>
        {
            WriteInteger(writer, "parentNodeId");
            WriteInteger(writer, "previousNodeId", "0 when inserted as the first child.");
            WriteRef(writer, "node", "Node", description: "The inserted subtree root.");
        }, "Removals of the same batch are always emitted before insertions; moved nodes keep " +
           "their identity.");
        WriteEventWith(writer, "childNodeRemoved", static writer =>
        {
            WriteInteger(writer, "parentNodeId");
            WriteInteger(writer, "nodeId");
        }, "The removed ids are never reused.");
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteRuntimeDomain(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("domain", "Runtime");
        writer.WriteString("version", "1.3");
        writer.WriteString("description",
            "Compatibility surface without a JavaScript runtime: DOM node handles resolve through " +
            "objectId, but no script evaluation, property enumeration, bindings or exception " +
            "state exist. Probes are acknowledged without effect as noted per command.");
        writer.WritePropertyName("types");
        writer.WriteStartArray();
        WriteObjectType(writer, "ExecutionContextDescription",
            "The single synthetic context announced after Runtime.enable.", static writer =>
        {
            WriteInteger(writer, "id", "Always 1.");
            WriteString(writer, "origin", "Always square://application.");
            WriteString(writer, "name", "Always Square.");
            WriteObject(writer, "auxData", null, static writer =>
            {
                WriteBoolean(writer, "isDefault", "Always true.");
                WriteString(writer, "type", "Always \"default\".");
                WriteString(writer, "frameId", "The target id.");
            });
        });
        writer.WriteEndArray();
        writer.WritePropertyName("commands");
        writer.WriteStartArray();
        WriteCommand(writer, "enable",
            "Registers the session and sends Runtime.executionContextCreated once. Idempotent.");
        WriteCommand(writer, "disable", "Acknowledged without effect.");
        WriteCommandWith(writer, "getProperties",
            "Returns empty arrays: there is no JavaScript object model to enumerate.",
            returns: static writer =>
            {
                WriteObjectArray(writer, "result", "Always empty.");
                WriteObjectArray(writer, "internalProperties", "Always empty.");
            });
        WriteCommandWith(writer, "callFunctionOn",
            "Functions are never evaluated.",
            returns: static writer => WriteObject(writer, "result",
                "Always {\"type\":\"undefined\"}.", static writer =>
                {
                    WriteString(writer, "type", "Always \"undefined\".");
                }));
        WriteCommandWith(writer, "releaseObject",
            "Acknowledged; handles live for the whole session and need no explicit release.",
            parameters: static writer => WriteString(writer, "objectId", optional: true));
        WriteCommand(writer, "releaseObjectGroup",
            "Acknowledged; handles live for the whole session and need no explicit release.");
        WriteCommand(writer, "runIfWaitingForDebugger",
            "Acknowledged; Square never pauses waiting for a debugger.");
        WriteCommand(writer, "addBinding",
            "Acknowledged without effect; no script contexts exist to bind into.");
        writer.WriteEndArray();
        writer.WritePropertyName("events");
        writer.WriteStartArray();
        WriteEventWith(writer, "executionContextCreated", static writer =>
        {
            WriteRef(writer, "context", "ExecutionContextDescription");
        });
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WritePageDomain(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("domain", "Page");
        writer.WriteString("version", "1.3");
        writer.WriteString("description",
            "Frame, layout and screenshot information for the single Square window target. " +
            "Navigation and script injection are acknowledged without effect: there is no " +
            "document navigation and no script engine.");
        writer.WritePropertyName("types");
        writer.WriteStartArray();
        WriteObjectType(writer, "Frame",
            "The single synthetic frame of the Square application.", static writer =>
        {
            WriteString(writer, "id", "The target id.");
            WriteString(writer, "loaderId", "The target id; it changes when the window reloads.");
            WriteString(writer, "url", "square://application/{targetId}.");
            WriteString(writer, "domainAndRegistry", "Always empty.");
            WriteString(writer, "securityOrigin", "Always square://application.");
            WriteString(writer, "mimeType", "Always text/html.");
            WriteString(writer, "secureContextType", "Always \"Secure\".");
            WriteString(writer, "crossOriginIsolatedContextType", "Always \"NotIsolated\".");
            WriteStringArray(writer, "gatedAPIFeatures", "Always empty.");
        });
        WriteObjectType(writer, "LayoutViewport",
            "Layout viewport of the window client area; the origin is always 0,0.", static writer =>
        {
            WriteNumber(writer, "pageX", "Always 0.");
            WriteNumber(writer, "pageY", "Always 0.");
            WriteNumber(writer, "clientWidth", "Window client width.");
            WriteNumber(writer, "clientHeight", "Window client height.");
        });
        WriteObjectType(writer, "VisualViewport",
            "Visual viewport of the window; Square does not pinch-zoom, so this mirrors the " +
            "layout viewport at scale 1.", static writer =>
        {
            WriteNumber(writer, "offsetX", "Always 0.");
            WriteNumber(writer, "offsetY", "Always 0.");
            WriteNumber(writer, "clientWidth", "Window client width.");
            WriteNumber(writer, "clientHeight", "Window client height.");
            WriteNumber(writer, "scale", "Always 1.");
        });
        WriteObjectType(writer, "Rect", "Content size: the full client area from 0,0.", static writer =>
        {
            WriteNumber(writer, "x", "Always 0.");
            WriteNumber(writer, "y", "Always 0.");
            WriteNumber(writer, "width", "Window client width.");
            WriteNumber(writer, "height", "Window client height.");
        });
        WriteObjectType(writer, "NavigationEntry",
            "The single synthetic history entry of the application.", static writer =>
        {
            WriteInteger(writer, "id", "Always 0.");
            WriteString(writer, "url", "square://application/{targetId}.");
            WriteString(writer, "userTypedURL", "Same as url.");
            WriteString(writer, "title", "Always \"Square DevTools\".");
            WriteString(writer, "transitionType", "Always \"typed\".");
        });
        writer.WriteEndArray();
        writer.WritePropertyName("commands");
        writer.WriteStartArray();
        WriteCommand(writer, "enable", "Acknowledged; the Page domain produces no events.");
        WriteCommand(writer, "disable", "Acknowledged without effect.");
        WriteCommandWith(writer, "getFrameTree",
            "Returns the single frame with no child frames.",
            returns: static writer => WriteObject(writer, "frameTree", null, static writer =>
            {
                WriteRef(writer, "frame", "Frame");
            }));
        WriteCommandWith(writer, "getResourceTree",
            "Returns the frame tree with an empty resource list; Square exposes no subresources.",
            returns: static writer => WriteObject(writer, "frameTree", null, static writer =>
            {
                WriteRef(writer, "frame", "Frame");
                WriteObjectArray(writer, "resources", "Always empty.");
            }));
        WriteCommandWith(writer, "getNavigationHistory",
            "Returns the single synthetic entry.",
            returns: static writer =>
            {
                WriteInteger(writer, "currentIndex", "Always 0.");
                WriteRefArray(writer, "entries", "NavigationEntry", "The single entry.");
            });
        WriteCommandWith(writer, "getLayoutMetrics",
            "Reads the current window client size.",
            returns: static writer =>
            {
                WriteRef(writer, "layoutViewport", "LayoutViewport");
                WriteRef(writer, "visualViewport", "VisualViewport");
                WriteRef(writer, "contentSize", "Rect");
            });
        WriteCommandWith(writer, "captureScreenshot",
            "Captures the current renderer frame.",
            returns: static writer => WriteString(writer, "data",
                "Base64-encoded PNG of the current frame."));
        WriteCommand(writer, "startScreencast",
            "Acknowledged without effect; no screencast frames are produced. Use " +
            "Page.captureScreenshot for on-demand frames.");
        WriteCommand(writer, "stopScreencast", "Acknowledged without effect.");
        WriteCommandWith(writer, "addScriptToEvaluateOnNewDocument",
            "Scripts are not evaluated; Square has no script engine.",
            returns: static writer => WriteString(writer, "identifier",
                "Always \"square-noop\"."));
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteTargetDomain(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("domain", "Target");
        writer.WriteString("version", "1.3");
        writer.WriteString("description",
            "The single fixed page target describing the Square application window. There are no " +
            "child targets, so discovery and auto-attach probes are acknowledged without effect.");
        writer.WritePropertyName("types");
        writer.WriteStartArray();
        WriteObjectType(writer, "TargetInfo", null, static writer =>
        {
            WriteString(writer, "targetId", "The stable target id of the DevTools server.");
            WriteString(writer, "type", "Always \"page\".");
            WriteString(writer, "title", "Always \"Square DevTools\".");
            WriteString(writer, "url", "square://application/{targetId}.");
            WriteBoolean(writer, "attached", "Always true: the DevTools connection is the attach point.");
            WriteBoolean(writer, "canAccessOpener", "Always false.");
            WriteString(writer, "openerId", "Always empty.");
        });
        writer.WriteEndArray();
        writer.WritePropertyName("commands");
        writer.WriteStartArray();
        WriteCommandWith(writer, "getTargetInfo",
            "Returns the fixed target info.",
            returns: static writer => WriteRef(writer, "targetInfo", "TargetInfo"));
        WriteCommand(writer, "setDiscoverTargets",
            "Acknowledged without effect; there is a single fixed target and no targetCreated or " +
            "targetDestroyed events.");
        WriteCommand(writer, "setAutoAttach",
            "Acknowledged without effect; no child targets exist.");
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteCssDomain(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("domain", "CSS");
        writer.WriteString("version", "1.3");
        writer.WriteString("description",
            "Reads computed, matched and inline styles from the real Square cascade. With the " +
            "Chrome style editing gate (AllowChromeStyleEditing) open, inline and matched author " +
            "declaration blocks are exposed as virtual runtime resources under " +
            "square://runtime-style/{styleSheetId} that setStyleTexts edits atomically; edits " +
            "affect only the running application, never source files, and survive disconnection. " +
            "With the gate closed no resource ids, ranges or headers are published and " +
            "setStyleTexts fails with -32000. Selector editing, new rules and " +
            "setEffectivePropertyValueForNode are not implemented.");
        writer.WritePropertyName("types");
        writer.WriteStartArray();
        WriteObjectType(writer, "SourceRange",
            "Line/column coordinates into the virtual resource text. Columns count UTF-16 code " +
            "units, CRLF counts as one line break, and the end is exclusive.", static writer =>
        {
            WriteInteger(writer, "startLine");
            WriteInteger(writer, "startColumn");
            WriteInteger(writer, "endLine");
            WriteInteger(writer, "endColumn");
        });
        WriteObjectType(writer, "CSSProperty",
            "One declaration as authored (or as edited at runtime).", static writer =>
        {
            WriteString(writer, "name");
            WriteString(writer, "value");
            WriteBoolean(writer, "important", "True when the authored declaration carries !important.");
            WriteBoolean(writer, "implicit", "Always false.");
            WriteString(writer, "text",
                "The original declaration text when the block is resource-backed.", optional: true);
            WriteRef(writer, "range", "SourceRange",
                "Coordinates inside the full virtual resource; resource-backed declarations only.",
                optional: true);
            WriteBoolean(writer, "parsedOk",
                "False rows keep their original text; they are shown but never applied.");
            WriteBoolean(writer, "disabled",
                "True for commented-out rows; disabled rows do not affect the cascade.");
        });
        WriteObjectType(writer, "ShorthandEntry",
            "Effective authored shorthand declarations only.", static writer =>
        {
            WriteString(writer, "name");
            WriteString(writer, "value");
            WriteBoolean(writer, "important");
        });
        WriteObjectType(writer, "ComputedStyleEntry", null, static writer =>
        {
            WriteString(writer, "name");
            WriteString(writer, "value");
        });
        WriteObjectType(writer, "Selector", null, static writer =>
        {
            WriteString(writer, "text");
        });
        WriteObjectType(writer, "SelectorList", null, static writer =>
        {
            WriteRefArray(writer, "selectors", "Selector", "Each entry is {\"text\": ...}.");
            WriteString(writer, "text", "The whole selector group joined with \", \".");
        });
        WriteObjectType(writer, "CSSStyle",
            "A declaration block. Resource-backed blocks carry styleSheetId, cssText and range; " +
            "UA rules and read-only inline blocks carry none of them.", static writer =>
        {
            WriteString(writer, "styleSheetId",
                "Only when the block is backed by an editable runtime resource.", optional: true);
            WriteRefArray(writer, "cssProperties", "CSSProperty");
            WriteRefArray(writer, "shorthandEntries", "ShorthandEntry");
            WriteString(writer, "cssText",
                "The declaration block text for resource-backed styles.", optional: true);
            WriteRef(writer, "range", "SourceRange",
                "Location of the block inside the full virtual resource text.", optional: true);
        });
        WriteObjectType(writer, "CSSRule", null, static writer =>
        {
            WriteString(writer, "styleSheetId",
                "Author rules with an editable resource only; UA rules have none.", optional: true);
            WriteRef(writer, "selectorList", "SelectorList");
            WriteString(writer, "origin", "\"regular\" for author rules, \"user-agent\" for UA rules.");
            WriteRef(writer, "style", "CSSStyle");
        });
        WriteObjectType(writer, "RuleMatch", null, static writer =>
        {
            WriteRef(writer, "rule", "CSSRule");
            WriteIntegerArray(writer, "matchingSelectors", "Always [0]: one selector per group is reported.");
        });
        WriteObjectType(writer, "CSSStyleSheetHeader",
            "Full header of one virtual runtime resource. Headers are published only while the " +
            "style editing gate is open.", static writer =>
        {
            WriteString(writer, "styleSheetId");
            WriteString(writer, "frameId", "The target id.");
            WriteString(writer, "sourceURL", "square://runtime-style/{styleSheetId}; this is a " +
                "runtime resource, not an original source file location.");
            WriteString(writer, "origin", "\"regular\" for author and inline resources.");
            WriteString(writer, "title", "Always \"Square runtime styles\".");
            WriteInteger(writer, "ownerNode",
                "BackendNodeId of the owning element for inline styles.", optional: true);
            WriteBoolean(writer, "disabled", "Always false.");
            WriteBoolean(writer, "isInline",
                "Always false: resources are virtual, not parser-created STYLE tags.");
            WriteBoolean(writer, "isMutable", "Always true for published resources.");
            WriteBoolean(writer, "isConstructed", "Always false.");
            WriteInteger(writer, "startLine", "Always 0.");
            WriteInteger(writer, "startColumn", "Always 0.");
            WriteInteger(writer, "endLine", "Line of the position just past the last text character.");
            WriteInteger(writer, "endColumn", "Column just past the last text character.");
            WriteInteger(writer, "length", "UTF-16 length of the current resource text.");
        });
        WriteObjectType(writer, "StyleTextEdit", null, static writer =>
        {
            WriteString(writer, "styleSheetId");
            WriteRef(writer, "range", "SourceRange",
                "Must select a whole set of declarations inside the block without splitting a " +
                "quote, comment or token.");
            WriteString(writer, "text", "Replacement text; empty text deletes the range.");
        });
        writer.WriteEndArray();
        writer.WritePropertyName("commands");
        writer.WriteStartArray();
        WriteCommand(writer, "enable",
            "Announces styleSheetAdded headers for every current runtime resource when the " +
            "editing gate is open; announces nothing otherwise. Idempotent.");
        WriteCommand(writer, "disable",
            "Stops CSS events, clears this session's tracking configuration and completes a " +
            "pending takeComputedStyleUpdates request.");
        WriteCommandWith(writer, "getComputedStyleForNode",
            "Computed values read from the live element; never derived from matched or inline data.",
            parameters: WriteNodeSelector,
            returns: static writer => WriteRefArray(writer, "computedStyle", "ComputedStyleEntry"));
        WriteCommandWith(writer, "getMatchedStylesForNode",
            "Matched author and UA rules plus the inline block for one node.",
            parameters: WriteNodeSelector,
            returns: static writer =>
            {
                WriteRefArray(writer, "matchedCSSRules", "RuleMatch");
                WriteObjectArray(writer, "pseudoElements", "Always empty.");
                WriteObjectArray(writer, "inherited", "Always empty.");
                WriteObjectArray(writer, "inheritedPseudoElements", "Always empty.");
                WriteObjectArray(writer, "cssKeyframesRules", "Always empty.");
                WriteRef(writer, "inlineStyle", "CSSStyle");
            });
        WriteCommandWith(writer, "getAnimatedStylesForNode",
            "Square reports no keyframe animation styles; the animation array is always empty.",
            parameters: WriteNodeSelector,
            returns: static writer =>
            {
                WriteObjectArray(writer, "animationStyles", "Always empty.");
                WriteRef(writer, "transitionsStyle", "CSSStyle", "Empty declaration block.");
                WriteObjectArray(writer, "inherited", "Always empty.");
            });
        WriteCommandWith(writer, "getInlineStylesForNode",
            "The element.style declaration block.",
            parameters: WriteNodeSelector,
            returns: static writer =>
            {
                WriteRef(writer, "inlineStyle", "CSSStyle");
                WriteRef(writer, "attributesStyle", "CSSStyle",
                    "Always omitted; attribute-backed styles are not modeled.", optional: true);
            });
        WriteCommandWith(writer, "getPlatformFontsForNode",
            "Font resolution internals are not exposed.",
            parameters: WriteNodeSelector,
            returns: static writer => WriteObjectArray(writer, "fonts", "Always empty."));
        WriteCommandWith(writer, "getEnvironmentVariables",
            "Not implemented; always returns an empty list.",
            returns: static writer => WriteObjectArray(writer, "environmentVariables", "Always empty."));
        WriteCommandWith(writer, "setStyleTexts",
            "Applies the whole batch atomically on the UI thread: any unknown, stale, read-only or " +
            "invalid-range edit rejects every edit and leaves the element untouched. Fails with " +
            "-32000 \"Chrome style editing is disabled.\" when the gate is closed, -32000 for " +
            "unknown or stale resources, -32000 for user-agent resources (read-only) and -32602 " +
            "\"Invalid style edit range.\" for out-of-bounds or token-splitting ranges. Rows that " +
            "do not parse are kept as parsedOk:false instead of claiming success.",
            parameters: static writer => WriteRefArray(writer, "edits", "StyleTextEdit",
                "Applied in array order against the staged text."),
            returns: static writer => WriteRefArray(writer, "styles", "CSSStyle",
                "One resource-backed block per edit, in the same order."));
        WriteCommandWith(writer, "getStyleSheetText",
            "Full current text of one virtual resource; unknown or stale ids fail with -32000.",
            parameters: static writer => WriteString(writer, "styleSheetId"),
            returns: static writer => WriteString(writer, "text"));
        WriteCommandWith(writer, "trackComputedStyleUpdatesForNode",
            "Watches one published element; real computed changes emit CSS.computedStyleUpdated, " +
            "throttled to at most one event per 50ms. A new nodeId replaces the old target and " +
            "resets the baseline; omitting (or nulling) nodeId stops tracking; removed nodes stop " +
            "tracking implicitly. Unknown node ids fail with -32000.",
            parameters: static writer => WriteInteger(writer, "nodeId",
                "Omit or pass null to stop tracking.", optional: true));
        WriteCommandWith(writer, "trackComputedStyleUpdates",
            "Replaces the whole batch configuration: a node qualifies when a tracked property's " +
            "old or new value differs and either string belongs to the allowed set. An empty " +
            "array stops batch tracking and completes a pending take. Only published nodes are " +
            "watched; newly published nodes just establish a baseline.",
            parameters: static writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("name", "propertiesToTrack");
                writer.WriteString("type", "array");
                writer.WritePropertyName("items");
                writer.WriteStartObject();
                writer.WriteString("type", "object");
                writer.WritePropertyName("properties");
                writer.WriteStartArray();
                WriteString(writer, "name");
                WriteString(writer, "value", "One allowed value; repeated names accumulate values.");
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteEndObject();
            });
        WriteCommandWith(writer, "takeComputedStyleUpdates",
            "Returns the deduplicated ascending nodeIds of published elements whose tracked " +
            "computed styles changed since the baseline. Without a qualifying change the response " +
            "is deferred until one arrives while the session keeps serving other commands; a " +
            "second pending take fails with -32000, as does taking with no tracking configured.",
            returns: static writer => WriteIntegerArray(writer, "nodeIds"));
        writer.WriteEndArray();
        writer.WritePropertyName("events");
        writer.WriteStartArray();
        WriteEventWith(writer, "styleSheetAdded", static writer =>
        {
            WriteRef(writer, "header", "CSSStyleSheetHeader");
        }, "Published when the editing gate is open: all resources after CSS.enable, each new " +
           "resource before its first use, and never twice for the same id.");
        WriteEventWith(writer, "styleSheetChanged", static writer =>
        {
            WriteString(writer, "styleSheetId");
        }, "The resource text changed at runtime (edit or application write-back).");
        WriteEventWith(writer, "styleSheetRemoved", static writer =>
        {
            WriteString(writer, "styleSheetId");
        }, "The owning element detached or the resource went stale; the id is never reused.");
        WriteEventWith(writer, "computedStyleUpdated", static writer =>
        {
            WriteInteger(writer, "nodeId", "The ForNode tracking target.");
        });
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteOverlayDomain(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("domain", "Overlay");
        writer.WriteString("version", "1.3");
        writer.WriteString("description",
            "Native highlight rendering and pointer/Escape inspect mode over the real window. " +
            "Each session owns its overlay state: the last caller of highlightNode or " +
            "setInspectMode owns the window overlay, a new owner ends and cancels the previous " +
            "owner's inspect mode, and disconnecting or disabling only clears the calling " +
            "session's own state.");
        writer.WritePropertyName("types");
        writer.WriteStartArray();
        WriteObjectType(writer, "RGBA", null, static writer =>
        {
            WriteInteger(writer, "r", "0-255.");
            WriteInteger(writer, "g", "0-255.");
            WriteInteger(writer, "b", "0-255.");
            WriteNumber(writer, "a", "0-1; defaults to 1 when omitted.", optional: true);
        });
        WriteObjectType(writer, "HighlightConfig",
            "Layer colors for highlight rendering. A missing layer renders fully transparent; an " +
            "entirely absent config uses the standard palette: content rgba(111,168,220,0.66), " +
            "padding rgba(147,196,125,0.55), border rgba(250,215,132,0.65), margin " +
            "rgba(246,178,107,0.55). Invalid values fail with -32602.", static writer =>
        {
            WriteRef(writer, "contentColor", "RGBA", optional: true);
            WriteRef(writer, "paddingColor", "RGBA", optional: true);
            WriteRef(writer, "borderColor", "RGBA", optional: true);
            WriteRef(writer, "marginColor", "RGBA", optional: true);
        });
        writer.WriteEndArray();
        writer.WritePropertyName("commands");
        writer.WriteStartArray();
        WriteCommand(writer, "enable", "Enables Overlay event delivery for this session. Idempotent.");
        WriteCommand(writer, "disable",
            "Clears this session's highlight and inspect mode only; other sessions keep theirs.");
        WriteCommand(writer, "hideHighlight", "Clears this owner's highlight.");
        WriteCommandWith(writer, "highlightNode",
            "Renders the four-layer content/padding/border/margin rings (non-overlapping, " +
            "clipped where they intersect) on the live element. Text and document keys resolve " +
            "to their owning element or the root element; unknown nodes fail with -32000.",
            parameters: static writer =>
            {
                WriteNodeSelector(writer);
                WriteRef(writer, "highlightConfig", "HighlightConfig", optional: true);
            });
        WriteCommandWith(writer, "setInspectMode",
            "searchForNode and searchForUAShadowDOM both start native element picking (pointer " +
            "selection never triggers application clicks; Escape cancels); none stops it. " +
            "captureAreaScreenshot is rejected with -32601 before any state changes; unknown " +
            "modes fail with -32602.",
            parameters: static writer =>
            {
                WriteEnum(writer, "mode", ["none", "searchForNode", "searchForUAShadowDOM"]);
                WriteRef(writer, "highlightConfig", "HighlightConfig", optional: true);
            });
        WriteCommand(writer, "setShowGridOverlays", "Acknowledged without effect; no debug overlay " +
            "rendering exists beyond node highlights.");
        WriteCommand(writer, "setShowFlexOverlays", "Acknowledged without effect.");
        WriteCommand(writer, "setShowScrollSnapOverlays", "Acknowledged without effect.");
        WriteCommand(writer, "setShowContainerQueryOverlays", "Acknowledged without effect.");
        WriteCommand(writer, "setShowIsolatedElements", "Acknowledged without effect.");
        WriteCommand(writer, "setShowViewportSizeOnResize", "Acknowledged without effect.");
        writer.WriteEndArray();
        writer.WritePropertyName("events");
        writer.WriteStartArray();
        WriteEventWith(writer, "inspectNodeRequested", static writer =>
        {
            WriteInteger(writer, "backendNodeId", "The picked element; resolving it to a nodeId may " +
                "require DOM.requestChildNodes or pushNodesByBackendIdsToFrontend.");
        }, "The user picked an element while this session owned inspect mode. Only the owning " +
           "session receives the event.");
        WriteEvent(writer, "inspectModeCanceled",
            "Inspect mode ended natively (e.g. Escape) rather than through setInspectMode/none. " +
            "Only the owning session receives the event.");
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteInspectorDomain(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("domain", "Inspector");
        writer.WriteString("version", "1.3");
        writer.WriteString("description",
            "Acknowledged compatibility probes; the session never detaches and no Inspector " +
            "events (detached, targetReloadedAfterCrash) are produced.");
        writer.WritePropertyName("commands");
        writer.WriteStartArray();
        WriteCommand(writer, "enable", "Acknowledged without effect.");
        WriteCommand(writer, "disable", "Acknowledged without effect.");
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteConsoleDomain(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("domain", "Console");
        writer.WriteString("version", "1.3");
        writer.WriteString("description",
            "Acknowledged compatibility probes. Managed Console.Out/Error output and framework " +
            "diagnostics are forwarded through the Log domain (Log.entryAdded); no " +
            "consoleAPICalled or Console.messageAdded events exist and none are produced.");
        writer.WritePropertyName("commands");
        writer.WriteStartArray();
        WriteCommand(writer, "enable", "Acknowledged without effect; see the Log domain instead.");
        WriteCommand(writer, "disable", "Acknowledged without effect.");
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteLogDomain(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("domain", "Log");
        writer.WriteString("version", "1.3");
        writer.WriteString("description",
            "Process-managed managed Console.Out/Error lines and Square framework diagnostics. " +
            "Entries are per session: enable replays the history this connection has not seen and " +
            "then streams live entries, clear drops the shared ring buffer, and disable stops " +
            "delivery. When the server was started without the console gate the domain " +
            "initializes empty and captures nothing.");
        writer.WritePropertyName("types");
        writer.WriteStartArray();
        WriteObjectType(writer, "LogEntry", null, static writer =>
        {
            WriteString(writer, "source", "Always \"other\": entries originate from managed Console " +
                "output or framework diagnostics, not a JavaScript engine or network layer.");
            WriteEnum(writer, "level", ["info", "error", "warning", "verbose"],
                "stdout is info, stderr is error; framework diagnostics map Critical/Error to " +
                "error, Warning to warning and Verbose to verbose.");
            WriteString(writer, "text", "The complete logical line, reassembled across Write calls " +
                "and split only at 8192 UTF-16 units without breaking surrogate pairs.");
            WriteNumber(writer, "timestamp", "Unix epoch milliseconds.");
        });
        writer.WriteEndArray();
        writer.WritePropertyName("commands");
        writer.WriteStartArray();
        WriteCommand(writer, "enable",
            "Replays ring-buffer entries this connection has not seen (older than the connection's " +
            "last sent sequence and not cleared), then streams live entries. Idempotent.");
        WriteCommand(writer, "disable",
            "Stops delivery for this session immediately; re-enabling replays entries the " +
            "connection has not seen.");
        WriteCommand(writer, "clear",
            "Drops the shared ring buffer history and filters still-unsent old entries in every " +
            "session; already-delivered entries are unaffected and other servers' collectors keep " +
            "their own buffers.");
        writer.WriteEndArray();
        writer.WritePropertyName("events");
        writer.WriteStartArray();
        WriteEventWith(writer, "entryAdded", static writer =>
        {
            WriteRef(writer, "entry", "LogEntry");
        });
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteNetworkDomain(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("domain", "Network");
        writer.WriteString("version", "1.3");
        writer.WriteString("description",
            "Acknowledged compatibility probes; Square exposes no HTTP resource instrumentation, " +
            "no request/response events and no cookie or cache state.");
        writer.WritePropertyName("commands");
        writer.WriteStartArray();
        WriteCommand(writer, "enable", "Acknowledged without effect; no Network events exist.");
        WriteCommand(writer, "disable", "Acknowledged without effect.");
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteSchemaDomain(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("domain", "Schema");
        writer.WriteString("version", "1.3");
        writer.WriteString("description",
            "Reports exactly the domains described by this protocol document.");
        writer.WritePropertyName("types");
        writer.WriteStartArray();
        WriteObjectType(writer, "Domain", null, static writer =>
        {
            WriteString(writer, "name");
            WriteString(writer, "version", "Always 1.3.");
        });
        writer.WriteEndArray();
        writer.WritePropertyName("commands");
        writer.WriteStartArray();
        WriteCommandWith(writer, "getDomains",
            "Returns the implemented domain list, identical to the domains array of this document.",
            returns: static writer => WriteRefArray(writer, "domains", "Domain"));
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>Writes the shared node selector parameters (all optional, one required).</summary>
    private static void WriteNodeSelector(Utf8JsonWriter writer)
    {
        WriteInteger(writer, "nodeId", "Session-local frontend node id.", optional: true);
        WriteInteger(writer, "backendNodeId", "Backend node id.", optional: true);
        WriteString(writer, "objectId", "Handle returned by DOM.resolveNode.", optional: true);
    }

    private static void WriteCommand(Utf8JsonWriter writer, string name, string? description = null)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        if (description != null) writer.WriteString("description", description);
        writer.WriteEndObject();
    }

    private static void WriteCommandWith(
        Utf8JsonWriter writer,
        string name,
        string? description,
        Action<Utf8JsonWriter>? parameters = null,
        Action<Utf8JsonWriter>? returns = null)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        if (description != null) writer.WriteString("description", description);
        if (parameters != null)
        {
            writer.WritePropertyName("parameters");
            writer.WriteStartArray();
            parameters(writer);
            writer.WriteEndArray();
        }
        if (returns != null)
        {
            writer.WritePropertyName("returns");
            writer.WriteStartArray();
            returns(writer);
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }

    private static void WriteEvent(
        Utf8JsonWriter writer,
        string name,
        string? description = null)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        if (description != null) writer.WriteString("description", description);
        writer.WriteEndObject();
    }

    private static void WriteEventWith(
        Utf8JsonWriter writer,
        string name,
        Action<Utf8JsonWriter> parameters,
        string? description = null)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        if (description != null) writer.WriteString("description", description);
        writer.WritePropertyName("parameters");
        writer.WriteStartArray();
        parameters(writer);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteObjectType(
        Utf8JsonWriter writer,
        string id,
        string? description,
        Action<Utf8JsonWriter> properties)
    {
        writer.WriteStartObject();
        writer.WriteString("id", id);
        writer.WriteString("type", "object");
        if (description != null) writer.WriteString("description", description);
        writer.WritePropertyName("properties");
        writer.WriteStartArray();
        properties(writer);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteArrayType(
        Utf8JsonWriter writer,
        string id,
        string itemType,
        string? description)
    {
        writer.WriteStartObject();
        writer.WriteString("id", id);
        writer.WriteString("type", "array");
        if (description != null) writer.WriteString("description", description);
        writer.WritePropertyName("items");
        writer.WriteStartObject();
        writer.WriteString("type", itemType);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteInteger(
        Utf8JsonWriter writer,
        string name,
        string? description = null,
        bool optional = false)
    {
        WritePrimitive(writer, name, "integer", optional, description);
    }

    private static void WriteNumber(
        Utf8JsonWriter writer,
        string name,
        string? description = null,
        bool optional = false)
    {
        WritePrimitive(writer, name, "number", optional, description);
    }

    private static void WriteString(
        Utf8JsonWriter writer,
        string name,
        string? description = null,
        bool optional = false)
    {
        WritePrimitive(writer, name, "string", optional, description);
    }

    private static void WriteBoolean(
        Utf8JsonWriter writer,
        string name,
        string? description = null,
        bool optional = false)
    {
        WritePrimitive(writer, name, "boolean", optional, description);
    }

    private static void WritePrimitive(
        Utf8JsonWriter writer,
        string name,
        string type,
        bool optional,
        string? description)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        writer.WriteString("type", type);
        if (optional) writer.WriteBoolean("optional", true);
        if (description != null) writer.WriteString("description", description);
        writer.WriteEndObject();
    }

    private static void WriteEnum(
        Utf8JsonWriter writer,
        string name,
        string[] values,
        string? description = null,
        bool optional = false)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        writer.WriteString("type", "string");
        writer.WritePropertyName("enum");
        writer.WriteStartArray();
        foreach (var value in values)
            writer.WriteStringValue(value);
        writer.WriteEndArray();
        if (optional) writer.WriteBoolean("optional", true);
        if (description != null) writer.WriteString("description", description);
        writer.WriteEndObject();
    }

    private static void WriteRef(
        Utf8JsonWriter writer,
        string name,
        string @ref,
        string? description = null,
        bool optional = false)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        writer.WriteString("$ref", @ref);
        if (optional) writer.WriteBoolean("optional", true);
        if (description != null) writer.WriteString("description", description);
        writer.WriteEndObject();
    }

    private static void WriteObject(
        Utf8JsonWriter writer,
        string name,
        string? description,
        Action<Utf8JsonWriter> properties,
        bool optional = false)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        writer.WriteString("type", "object");
        if (optional) writer.WriteBoolean("optional", true);
        if (description != null) writer.WriteString("description", description);
        writer.WritePropertyName("properties");
        writer.WriteStartArray();
        properties(writer);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteIntegerArray(
        Utf8JsonWriter writer,
        string name,
        string? description = null,
        bool optional = false)
    {
        WriteArray(writer, name, description, "integer", null, optional);
    }

    private static void WriteStringArray(
        Utf8JsonWriter writer,
        string name,
        string? description = null,
        bool optional = false)
    {
        WriteArray(writer, name, description, "string", null, optional);
    }

    private static void WriteObjectArray(
        Utf8JsonWriter writer,
        string name,
        string? description = null,
        bool optional = false)
    {
        WriteArray(writer, name, description, "object", null, optional);
    }

    private static void WriteRefArray(
        Utf8JsonWriter writer,
        string name,
        string itemRef,
        string? description = null,
        bool optional = false)
    {
        WriteArray(writer, name, description, null, itemRef, optional);
    }

    private static void WriteArray(
        Utf8JsonWriter writer,
        string name,
        string? description,
        string? itemType,
        string? itemRef,
        bool optional)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        writer.WriteString("type", "array");
        if (optional) writer.WriteBoolean("optional", true);
        if (description != null) writer.WriteString("description", description);
        writer.WritePropertyName("items");
        writer.WriteStartObject();
        if (itemRef != null) writer.WriteString("$ref", itemRef);
        else writer.WriteString("type", itemType!);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (char.IsControl(ch))
                        builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        builder.Append(ch);
                    break;
            }
        }
        return builder.ToString();
    }
}
