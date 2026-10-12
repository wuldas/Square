using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Square.Graphics.Codecs;
using Square.Hosting;

namespace Square.DevTools;

/// <summary>
/// Single CDP connection: serial command/event state model with real element-tree DOM mapping.
/// Node identity is (native DebugId, Document/Element/Text); frontend and backend IDs are
/// separate monotonic positive sequences (the first Document is 1/1) that are never reused.
/// </summary>
internal sealed partial class CdpSession
{
    private const int InitialReceiveBufferSize = 16 * 1024;


    private readonly WebSocket _socket;
    private readonly AppWindow _window;
    private readonly DevToolsServer _server;
    // Serializes command and event handling for the session model; always acquired
    // before _writeGate, never from UI callbacks.
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _sessionShutdown;
    private readonly Channel<CdpEvent> _events = Channel.CreateBounded<CdpEvent>(new BoundedChannelOptions(256)
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
        FullMode = BoundedChannelFullMode.Wait,
    });
    private readonly ArrayBufferWriter<byte> _sendBuffer = new(1024);
    private byte[] _receiveBuffer = new byte[InitialReceiveBufferSize];
    private int _receiveLength;
    private Task _eventPumpTask = Task.CompletedTask;
    private volatile bool _stopping;


    // Domain flags; DOM and CSS jointly own exactly one InspectionChanged subscription.
    private bool _runtimeEnabled;
    private bool _inspectionSubscribed;

    // Dirty coalescing: UI callbacks OR change kinds atomically and enqueue one signal;
    // the pump clears the queued marker before exchanging the flags and taking the snapshot.
    private int _pendingInspectionKinds;
    private int _inspectionSignalQueued;


    private CdpSession(WebSocket socket, AppWindow window, DevToolsServer server, CancellationToken cancellationToken)
    {
        _socket = socket;
        _window = window;
        _server = server;
        _sessionShutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    public static async Task RunAsync(
        WebSocket socket,
        AppWindow window,
        DevToolsServer server,
        CancellationToken cancellationToken = default)
    {
        var session = new CdpSession(socket, window, server, cancellationToken);
        window.InspectorNodeSelected += session.HandleInspectorNodeSelected;
        window.InspectorModeCanceled += session.HandleInspectorModeCanceled;
        session._eventPumpTask = Task.Run(session.RunEventPumpAsync);
        var closeStatus = WebSocketCloseStatus.NormalClosure;
        var closeDescription = "closed";
        try
        {
            await session.RunCoreAsync(session._sessionShutdown.Token);
        }
        catch (CdpBinaryMessageException)
        {
            closeStatus = WebSocketCloseStatus.InvalidMessageType;
            closeDescription = "Binary frames are not supported on the CDP connection.";
        }
        catch (OperationCanceledException) when (session._sessionShutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            closeStatus = WebSocketCloseStatus.InternalServerError;
            closeDescription = $"CDP session error: {exception.Message}";
        }
        finally
        {
            window.InspectorNodeSelected -= session.HandleInspectorNodeSelected;
            window.InspectorModeCanceled -= session.HandleInspectorModeCanceled;
            if (session._inspectionSubscribed)
            {
                window.InspectionChanged -= session.HandleInspectionChanged;
                session._inspectionSubscribed = false;
            }
            session._stopping = true;
            session._sessionShutdown.Cancel();
            session._logEnabled = false;
            Interlocked.Exchange(ref session._logSubscription, null)?.Dispose();
            session._events.Writer.TryComplete();
            try { await session._eventPumpTask; }
            catch { }
            await session.DrainForNodeDelayAsync(cancel: true);
            // Clears only this owner's native highlight/inspect state; the window handles
            // owner-thread cleanup itself and never makes a closed window queue work.
            try
            {
                window.ReleaseInspectorSession(session._ownerId);
            }
            finally
            {
                await session.CloseOutputAsync(closeStatus, closeDescription);
                session._stateGate.Dispose();
                session._writeGate.Dispose();
                session._sessionShutdown.Dispose();
                socket.Dispose();
            }
        }
    }

    private async Task RunCoreAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var frameLength = await ReceiveFrameAsync(cancellationToken);
            if (frameLength < 0) return;
            await ProcessFrameAsync(frameLength, cancellationToken);
        }
    }

    /// <summary>
    /// Receives one complete text frame into the reusable session buffer and returns its
    /// length, or -1 when the peer closed. The buffer must not be reused until the
    /// <see cref="JsonDocument"/> parsed from it has been disposed.
    /// </summary>
    private async Task<int> ReceiveFrameAsync(CancellationToken cancellationToken)
    {
        _receiveLength = 0;
        while (true)
        {
            if (_receiveLength == _receiveBuffer.Length)
                Array.Resize(ref _receiveBuffer, _receiveBuffer.Length * 2);
            var result = await _socket.ReceiveAsync(_receiveBuffer.AsMemory(_receiveLength), cancellationToken);
            _receiveLength += result.Count;
            if (result.MessageType == WebSocketMessageType.Close) return -1;
            if (result.MessageType == WebSocketMessageType.Binary)
                throw new CdpBinaryMessageException();
            if (result.EndOfMessage) return _receiveLength;
        }
    }

    private async Task ProcessFrameAsync(int length, CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            // Parses in place over the receive buffer; the document owns the bytes
            // until it is disposed at the end of dispatch.
            document = JsonDocument.Parse(new ReadOnlyMemory<byte>(_receiveBuffer, 0, length));
        }
        catch (JsonException)
        {
            await SendErrorAsync(0, -32700, "Invalid JSON.", cancellationToken);
            return;
        }

        using (document)
        {
            await DispatchAsync(document, cancellationToken);
        }
    }

    private void HandleInspectorNodeSelected(long ownerId, int debugId)
    {
        // UI-thread callback: only enqueues; a full queue cancels this slow connection
        // instead of silently dropping DOM identity events. Other owners' selections
        // belong to their own connections and are ignored here.
        if (ownerId != _ownerId || _stopping) return;
        if (_events.Writer.TryWrite(new CdpEvent(CdpEventKind.InspectorNodeSelected, OwnerId: ownerId, DebugId: debugId))) return;
        CancelSession();
    }

    private void HandleInspectorModeCanceled(long ownerId)
    {
        if (ownerId != _ownerId || _stopping) return;
        if (_events.Writer.TryWrite(new CdpEvent(CdpEventKind.InspectorModeCanceled, OwnerId: ownerId))) return;
        CancelSession();
    }

    private void HandleInspectionChanged(ElementInspectionChangeKind kind)
    {
        // UI-thread callback: atomic OR of dirty kinds plus a single in-flight signal.
        if (_stopping) return;
        Interlocked.Or(ref _pendingInspectionKinds, (int)kind);
        if (Interlocked.CompareExchange(ref _inspectionSignalQueued, 1, 0) != 0) return;
        if (_events.Writer.TryWrite(new CdpEvent(CdpEventKind.InspectionChanged))) return;
        CancelSession();
    }

    private void CancelSession()
    {
        _stopping = true;
        try
        {
            _sessionShutdown.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task RunEventPumpAsync()
    {
        try
        {
            await foreach (var @event in _events.Reader.ReadAllAsync(_sessionShutdown.Token))
            {
                try
                {
                    await HandleEventAsync(@event, _sessionShutdown.Token);
                }
                catch (WebSocketException)
                {
                    // A failed send ends only this connection.
                    _stopping = true;
                    _sessionShutdown.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (_sessionShutdown.IsCancellationRequested)
        {
        }
        catch (ChannelClosedException)
        {
        }
    }

    private async Task HandleEventAsync(CdpEvent @event, CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            switch (@event.Kind)
            {
                case CdpEventKind.InspectionChanged:
                {
                    // Reset the queued marker before exchanging the flags so changes that
                    // arrive while the snapshot is taken can re-enqueue a fresh signal.
                    Volatile.Write(ref _inspectionSignalQueued, 0);
                    var kinds = (ElementInspectionChangeKind)Interlocked.Exchange(ref _pendingInspectionKinds, 0);
                    await SyncDomAsync(kinds, cancellationToken);
                    await SyncCssAsync(kinds, cancellationToken);
                    return;
                }
                case CdpEventKind.ComputedStyleNotification:
                {
                    if (@event.OwnerId != _forNodeDelayTaskEpoch) return;
                    var notify = _forNodeDelayPending && @event.OwnerId == _forNodeDelayEpoch;
                    await DrainForNodeDelayAsync(cancel: false);
                    _forNodeDelayPending = false;
                    if (notify && _cssEnabled) await SendForNodeUpdateAsync(cancellationToken);
                    return;
                }
                case CdpEventKind.InspectorNodeSelected:
                {
                    if (!_overlayEnabled) return;
                    var key = new NodeKey(@event.DebugId, CdpNodeKind.Element);
                    var backendNodeId = GetOrCreateBackendId(key);
                    await SendEventAsync("Overlay.inspectNodeRequested", writer =>
                    {
                        writer.WriteNumber("backendNodeId", backendNodeId);
                    }, cancellationToken);
                    return;
                }
                case CdpEventKind.InspectorModeCanceled:
                {
                    if (!_overlayEnabled) return;
                    await SendEventAsync("Overlay.inspectModeCanceled", static writer => { }, cancellationToken);
                    return;
                }
                case CdpEventKind.LogEntry:
                    await SendLogEntryAsync(@event.LogEntry, cancellationToken);
                    return;
            }
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async Task DispatchAsync(JsonDocument document, CancellationToken cancellationToken)
    {
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("id", out var idElement) ||
            idElement.ValueKind != JsonValueKind.Number ||
            !idElement.TryGetInt32(out var id) ||
            !root.TryGetProperty("method", out var methodElement) ||
            methodElement.ValueKind != JsonValueKind.String ||
            (root.TryGetProperty("params", out var envelopeParams) && envelopeParams.ValueKind != JsonValueKind.Object))
        {
            await SendErrorAsync(0, -32600, "Invalid CDP request.", cancellationToken);
            return;
        }

        var method = methodElement.GetString() ?? "";
        var parameters = root.TryGetProperty("params", out var paramsElement) ? paramsElement : default;
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                await DispatchCommandAsync(id, method, parameters, cancellationToken);
            }
            catch (CdpRequestException exception)
            {
                await SendErrorAsync(id, exception.Code, exception.Message, cancellationToken);
            }
            catch (ArgumentException exception) when (exception.Message == "Invalid style edit range.")
            {
                // Native range validation for CSS.setStyleTexts carries the CDP code.
                await SendErrorAsync(id, -32602, exception.Message, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                await SendErrorAsync(id, -32000, exception.Message, cancellationToken);
            }
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <summary>Responds to the peer with an independent one second deadline; aborts on timeout or failure.</summary>
    private async Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string closeDescription)
    {
        if (_socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived)) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await _socket.CloseOutputAsync(closeStatus, closeDescription, timeout.Token);
        }
        catch
        {
            _socket.Abort();
        }
    }

    private async Task DispatchCommandAsync(
        int id,
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        switch (method)
        {
            case "Runtime.enable":
                // Idempotent: the execution context is announced exactly once, on the
                // disabled -> enabled transition only.
                if (_runtimeEnabled)
                {
                    await SendEmptyResultAsync(id, cancellationToken);
                    return;
                }
                _runtimeEnabled = true;
                await SendEmptyResultAsync(id, cancellationToken);
                await SendExecutionContextCreatedAsync(cancellationToken);
                return;
            case "Log.enable":
                await EnableLogAsync(id, cancellationToken);
                return;
            case "Log.disable":
                await DisableLogAsync(id, cancellationToken);
                return;
            case "Log.clear":
                _server.LogCollector?.Clear();
                await SendEmptyResultAsync(id, cancellationToken);
                return;
            case "Runtime.disable":
                _runtimeEnabled = false;
                await SendEmptyResultAsync(id, cancellationToken);
                return;
            case "Runtime.releaseObject":
            case "Runtime.releaseObjectGroup":
            case "Runtime.runIfWaitingForDebugger":
            case "Runtime.addBinding":
            case "Page.disable":
            case "Target.setAutoAttach":
            case "Target.setDiscoverTargets":
            case "Inspector.enable":
            case "Inspector.disable":
            case "Console.enable":
            case "Console.disable":
            case "Network.enable":
            case "Network.disable":
            case "Overlay.setShowViewportSizeOnResize":
            case "Overlay.setShowGridOverlays":
            case "Overlay.setShowFlexOverlays":
            case "Overlay.setShowScrollSnapOverlays":
            case "Overlay.setShowContainerQueryOverlays":
            case "Overlay.setShowIsolatedElements":
                await SendEmptyResultAsync(id, cancellationToken);
                return;
            case "DOM.enable":
            {
                var transitioned = !_domEnabled;
                EnableDom();
                await SendEmptyResultAsync(id, cancellationToken);
                if (transitioned)
                    await SendEventAsync("DOM.documentUpdated", static writer => { }, cancellationToken);
                return;
            }
            case "DOM.disable":
                _domEnabled = false;
                ResetNodeMaps();
                MaybeUnsubscribeInspection();
                await SendEmptyResultAsync(id, cancellationToken);
                return;
            case "Overlay.enable":
                _overlayEnabled = true;
                await SendEmptyResultAsync(id, cancellationToken);
                return;
            case "Overlay.disable":
                // Hiding and disabling only affect this session's owner state.
                _overlayEnabled = false;
                await _window.ClearInspectorHighlightAsync(_ownerId, cancellationToken).WaitAsync(cancellationToken);
                await _window.SetInspectorModeAsync(false, options: null, _ownerId, cancellationToken).WaitAsync(cancellationToken);
                await SendEmptyResultAsync(id, cancellationToken);
                return;
            case "Overlay.highlightNode":
                await HighlightNodeAsync(id, parameters, cancellationToken);
                return;
            case "Overlay.hideHighlight":
                await _window.ClearInspectorHighlightAsync(_ownerId, cancellationToken).WaitAsync(cancellationToken);
                await SendEmptyResultAsync(id, cancellationToken);
                return;
            case "Overlay.highlightRect":
                await SendErrorAsync(id, -32601, "Overlay.highlightRect is not supported.", cancellationToken);
                return;
            case "Overlay.setInspectMode":
                await SetInspectModeAsync(id, parameters, cancellationToken);
                return;
            case "Runtime.getProperties":
                await SendResultAsync(id, writer =>
                {
                    writer.WritePropertyName("result");
                    writer.WriteStartArray();
                    writer.WriteEndArray();
                    writer.WritePropertyName("internalProperties");
                    writer.WriteStartArray();
                    writer.WriteEndArray();
                }, cancellationToken);
                return;
            case "Runtime.callFunctionOn":
                await SendResultAsync(id, static writer =>
                {
                    writer.WritePropertyName("result");
                    writer.WriteStartObject();
                    writer.WriteString("type", "undefined");
                    writer.WriteEndObject();
                }, cancellationToken);
                return;
            case "DOM.getDocument":
                await SendDocumentAsync(id, parameters, flattened: false, cancellationToken);
                return;
            case "DOM.getFlattenedDocument":
                await SendDocumentAsync(id, parameters, flattened: true, cancellationToken);
                return;
            case "DOM.requestChildNodes":
                await RequestChildNodesAsync(id, parameters, cancellationToken);
                return;
            case "DOM.describeNode":
                await DescribeNodeAsync(id, parameters, cancellationToken);
                return;
            case "DOM.getAttributes":
                await SendAttributesAsync(id, parameters, cancellationToken);
                return;
            case "DOM.getNodeForLocation":
                await SendNodeForLocationAsync(id, parameters, cancellationToken);
                return;
            case "DOM.getBoxModel":
                await SendBoxModelAsync(id, parameters, cancellationToken);
                return;
            case "DOM.getOuterHTML":
                await SendOuterHtmlAsync(id, parameters, cancellationToken);
                return;
            case "DOM.setInspectedNode":
                await SendEmptyResultAsync(id, cancellationToken);
                return;
            case "DOM.pushNodesByBackendIdsToFrontend":
                await PushNodesByBackendIdsAsync(id, parameters, cancellationToken);
                return;
            case "DOM.resolveNode":
                await SendResolveNodeAsync(id, parameters, cancellationToken);
                return;
            case "CSS.enable":
            {
                var transitioned = !_cssEnabled;
                _cssEnabled = true;
                EnsureInspectionSubscription();
                await SendEmptyResultAsync(id, cancellationToken);
                if (transitioned && _server.AllowChromeStyleEditing)
                    await AnnounceStyleSheetsAsync(cancellationToken);
                if (transitioned) await EstablishBatchBaselinesAsync(cancellationToken);
                return;
            }
            case "CSS.disable":
            {
                _cssEnabled = false;
                // A registered pending take is completed before the tracking state dies.
                await CompletePendingTakeAsync(cancellationToken);
                CancelForNodeDelay();
                await DrainForNodeDelayAsync(cancel: true);
                _trackedForNodeKey = null;
                _forNodeBaseline = null;
                _trackedProperties.Clear();
                _batchBaselines.Clear();
                _pendingTakeNodeIds.Clear();
                _announcedStyleSheets.Clear();
                MaybeUnsubscribeInspection();
                await SendEmptyResultAsync(id, cancellationToken);
                return;
            }
            case "CSS.getComputedStyleForNode":
                await SendComputedStyleAsync(id, parameters, cancellationToken);
                return;
            case "CSS.getMatchedStylesForNode":
                await SendMatchedStylesAsync(id, parameters, cancellationToken);
                return;
            case "CSS.getAnimatedStylesForNode":
                await SendAnimatedStylesAsync(id, parameters, cancellationToken);
                return;
            case "CSS.getInlineStylesForNode":
                await SendInlineStylesAsync(id, parameters, cancellationToken);
                return;
            case "CSS.setStyleTexts":
                await SetStyleTextsAsync(id, parameters, cancellationToken);
                return;
            case "CSS.getStyleSheetText":
                await SendStyleSheetTextAsync(id, parameters, cancellationToken);
                return;
            case "CSS.trackComputedStyleUpdatesForNode":
                await TrackComputedStyleUpdatesForNodeAsync(id, parameters, cancellationToken);
                return;
            case "CSS.trackComputedStyleUpdates":
                await TrackComputedStyleUpdatesAsync(id, parameters, cancellationToken);
                return;
            case "CSS.takeComputedStyleUpdates":
                await TakeComputedStyleUpdatesAsync(id, cancellationToken);
                return;
            case "CSS.getPlatformFontsForNode":
                await SendResultAsync(id, static writer =>
                {
                    writer.WritePropertyName("fonts");
                    writer.WriteStartArray();
                    writer.WriteEndArray();
                }, cancellationToken);
                return;
            case "CSS.getEnvironmentVariables":
                await SendResultAsync(id, static writer =>
                {
                    writer.WritePropertyName("environmentVariables");
                    writer.WriteStartArray();
                    writer.WriteEndArray();
                }, cancellationToken);
                return;
            case "Page.enable":
                await SendEmptyResultAsync(id, cancellationToken);
                return;
            case "Page.getFrameTree":
                await SendFrameTreeAsync(id, cancellationToken);
                return;
            case "Page.getResourceTree":
                await SendResourceTreeAsync(id, cancellationToken);
                return;
            case "Page.getNavigationHistory":
                await SendNavigationHistoryAsync(id, cancellationToken);
                return;
            case "Page.getLayoutMetrics":
                await SendLayoutMetricsAsync(id, cancellationToken);
                return;
            case "Page.captureScreenshot":
                await SendScreenshotAsync(id, cancellationToken);
                return;
            case "Page.startScreencast":
            case "Page.stopScreencast":
                await SendEmptyResultAsync(id, cancellationToken);
                return;
            case "Page.addScriptToEvaluateOnNewDocument":
                await SendResultAsync(id, static writer => writer.WriteString("identifier", "square-noop"), cancellationToken);
                return;
            case "Target.getTargetInfo":
                await SendTargetInfoAsync(id, cancellationToken);
                return;
            case "Schema.getDomains":
                await SendResultAsync(id, CdpTargetDiscovery.WriteDomains, cancellationToken);
                return;
            default:
                await SendErrorAsync(id, -32601, $"Method '{method}' is not supported.", cancellationToken);
                return;
        }
    }

    private async Task SendResourceTreeAsync(int id, CancellationToken cancellationToken)
    {
        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("frameTree");
            writer.WriteStartObject();
            writer.WritePropertyName("frame");
            WriteFrame(writer);
            writer.WritePropertyName("resources");
            writer.WriteStartArray();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }, cancellationToken);
    }

    private async Task SendNavigationHistoryAsync(int id, CancellationToken cancellationToken)
    {
        await SendResultAsync(id, writer =>
        {
            writer.WriteNumber("currentIndex", 0);
            writer.WritePropertyName("entries");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteNumber("id", 0);
            writer.WriteString("url", $"square://application/{_server.TargetId}");
            writer.WriteString("userTypedURL", $"square://application/{_server.TargetId}");
            writer.WriteString("title", "Square DevTools");
            writer.WriteString("transitionType", "typed");
            writer.WriteEndObject();
            writer.WriteEndArray();
        }, cancellationToken);
    }

    private async Task SendFrameTreeAsync(int id, CancellationToken cancellationToken)
    {
        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("frameTree");
            writer.WriteStartObject();
            writer.WritePropertyName("frame");
            WriteFrame(writer);
            writer.WriteEndObject();
        }, cancellationToken);
    }

    private async Task SendLayoutMetricsAsync(int id, CancellationToken cancellationToken)
    {
        var size = _window.ClientSize;
        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("layoutViewport");
            WriteViewport(writer, size.Width, size.Height);
            writer.WritePropertyName("visualViewport");
            writer.WriteStartObject();
            writer.WriteNumber("offsetX", 0);
            writer.WriteNumber("offsetY", 0);
            writer.WriteNumber("clientWidth", size.Width);
            writer.WriteNumber("clientHeight", size.Height);
            writer.WriteNumber("scale", 1);
            writer.WriteEndObject();
            writer.WritePropertyName("contentSize");
            writer.WriteStartObject();
            writer.WriteNumber("x", 0);
            writer.WriteNumber("y", 0);
            writer.WriteNumber("width", size.Width);
            writer.WriteNumber("height", size.Height);
            writer.WriteEndObject();
        }, cancellationToken);
    }

    private async Task SendScreenshotAsync(int id, CancellationToken cancellationToken)
    {
        using var bitmap = await _window.CaptureRendererBitmapAsync().WaitAsync(cancellationToken);
        using var stream = new MemoryStream();
        BitmapPngEncoder.Save(bitmap, stream);
        var base64 = Convert.ToBase64String(stream.ToArray());
        await SendResultAsync(id, writer => writer.WriteString("data", base64), cancellationToken);
    }

    private async Task SendTargetInfoAsync(int id, CancellationToken cancellationToken)
    {
        await SendResultAsync(id, writer =>
        {
            writer.WritePropertyName("targetInfo");
            writer.WriteStartObject();
            writer.WriteString("targetId", _server.TargetId);
            writer.WriteString("type", "page");
            writer.WriteString("title", "Square DevTools");
            writer.WriteString("url", $"square://application/{_server.TargetId}");
            writer.WriteBoolean("attached", true);
            writer.WriteBoolean("canAccessOpener", false);
            writer.WriteString("openerId", "");
            writer.WriteEndObject();
        }, cancellationToken);
    }

    private async Task SendExecutionContextCreatedAsync(CancellationToken cancellationToken)
    {
        await SendEventAsync("Runtime.executionContextCreated", writer =>
        {
            writer.WritePropertyName("context");
            writer.WriteStartObject();
            writer.WriteNumber("id", 1);
            writer.WriteString("origin", "square://application");
            writer.WriteString("name", "Square");
            writer.WritePropertyName("auxData");
            writer.WriteStartObject();
            writer.WriteBoolean("isDefault", true);
            writer.WriteString("type", "default");
            writer.WriteString("frameId", _server.TargetId);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }, cancellationToken);
    }

    private void WriteFrame(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("id", _server.TargetId);
        writer.WriteString("loaderId", _server.TargetId);
        writer.WriteString("url", $"square://application/{_server.TargetId}");
        writer.WriteString("domainAndRegistry", "");
        writer.WriteString("securityOrigin", "square://application");
        writer.WriteString("mimeType", "text/html");
        writer.WriteString("secureContextType", "Secure");
        writer.WriteString("crossOriginIsolatedContextType", "NotIsolated");
        writer.WritePropertyName("gatedAPIFeatures");
        writer.WriteStartArray();
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteViewport(Utf8JsonWriter writer, float width, float height)
    {
        writer.WriteStartObject();
        writer.WriteNumber("pageX", 0);
        writer.WriteNumber("pageY", 0);
        writer.WriteNumber("clientWidth", width);
        writer.WriteNumber("clientHeight", height);
        writer.WriteEndObject();
    }

    private void EnsureInspectionSubscription()
    {
        if (_inspectionSubscribed) return;
        _window.InspectionChanged += HandleInspectionChanged;
        _inspectionSubscribed = true;
    }

    private void MaybeUnsubscribeInspection()
    {
        if (!_inspectionSubscribed || _domEnabled || _cssEnabled) return;
        _window.InspectionChanged -= HandleInspectionChanged;
        _inspectionSubscribed = false;
        Interlocked.Exchange(ref _pendingInspectionKinds, 0);
        Volatile.Write(ref _inspectionSignalQueued, 0);
    }

    private async Task SendEmptyResultAsync(int id, CancellationToken cancellationToken) =>
        await SendResultAsync(id, static _ => { }, cancellationToken);

    private async Task SendResultAsync(
        int id,
        Action<Utf8JsonWriter> writeResult,
        CancellationToken cancellationToken)
    {
        await SendJsonAsync(writer =>
        {
            writer.WriteNumber("id", id);
            writer.WritePropertyName("result");
            writer.WriteStartObject();
            writeResult(writer);
            writer.WriteEndObject();
        }, cancellationToken);
    }

    private async Task SendErrorAsync(int id, int code, string message, CancellationToken cancellationToken)
    {
        await SendJsonAsync(writer =>
        {
            writer.WriteNumber("id", id);
            writer.WritePropertyName("error");
            writer.WriteStartObject();
            writer.WriteNumber("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
        }, cancellationToken);
    }

    private async Task SendEventAsync(string method, Action<Utf8JsonWriter> writeParameters, CancellationToken cancellationToken)
    {
        await SendJsonAsync(writer =>
        {
            writer.WriteString("method", method);
            writer.WritePropertyName("params");
            writer.WriteStartObject();
            writeParameters(writer);
            writer.WriteEndObject();
        }, cancellationToken);
    }

    private async Task SendJsonAsync(Action<Utf8JsonWriter> write, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            _sendBuffer.Clear();
            using (var writer = new Utf8JsonWriter(_sendBuffer))
            {
                writer.WriteStartObject();
                write(writer);
                writer.WriteEndObject();
                writer.Flush();
            }

            await _socket.SendAsync(_sendBuffer.WrittenMemory, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private enum CdpEventKind
    {
        InspectionChanged,
        InspectorNodeSelected,
        InspectorModeCanceled,
        ComputedStyleNotification,
        LogEntry,
    }

    private readonly record struct CdpEvent(
        CdpEventKind Kind,
        long OwnerId = 0,
        int DebugId = 0,
        DevToolsLogEntry LogEntry = default);

    /// <summary>Carries an explicit CDP error code for a parameter-level request failure.</summary>
    private sealed class CdpRequestException(int code, string message) : Exception(message)
    {
        public int Code { get; } = code;
    }

    private sealed class CdpBinaryMessageException : Exception
    {
    }
}
