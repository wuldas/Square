using System.Text;
using System.Text.Json;
using Square.Compiler.LanguageServices;
using Square.Compiler.Template.Ir;

namespace Square.LanguageServer;

public sealed class LanguageServerHost
{
    private const int DiagnosticDelayMilliseconds = 120;
    private const int RequestCancelledCode = -32800;
    private const int ParseErrorCode = -32700;
    /// <summary>本服务端的位置换算基于 .NET 字符串（UTF-16 代码单元）。</summary>
    private const string Utf16EncodingName = "utf-16";
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly DocumentStore _documents = new();
    private readonly WorkspaceComponentIndex _componentIndex = new();
    private readonly ProjectTemplateWorkspace _projectWorkspace;
    private readonly HashSet<string> _reportedWarnings = new(StringComparer.Ordinal);
    private readonly object _diagnosticGate = new();
    private readonly Dictionary<string, CancellationTokenSource> _pendingDiagnostics = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task> _pendingDiagnosticTasks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _diagnosticSequence = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _documentProjects = new(StringComparer.Ordinal);
    private readonly object _requestGate = new();
    private readonly HashSet<Task> _pendingRequestTasks = new();
    private readonly Dictionary<string, CancellationTokenSource> _pendingRequestCancellations = new(StringComparer.Ordinal);
    private long _nextDiagnosticSequence;
    private readonly SemaphoreSlim _outputGate = new(1, 1);
    private bool _shutdownRequested;
    private bool _hoverSupportsMarkdown = true;
    private bool _definitionSupportsLocationLinks;
    private bool _watchRegistrationSupported;
    private string? _clientCapabilityWarning;
    private CancellationToken _lifetimeToken;

    public LanguageServerHost(Stream input, Stream output)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _projectWorkspace = new ProjectTemplateWorkspace();
        _projectWorkspace.Invalidated += OnWorkspaceInvalidated;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        _lifetimeToken = cancellationToken;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
        {
            var frame = await ReadFrameAsync(cancellationToken);
            if (frame.Payload == null)
            {
                // 协议错误按 LSP 规范回 -32700 并继续服务；流结束则退出。
                if (frame.Malformed)
                {
                    await WriteParseErrorAsync(cancellationToken);
                    continue;
                }
                return _shutdownRequested ? 0 : 1;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(frame.Payload);
            }
            catch (JsonException)
            {
                await WriteParseErrorAsync(cancellationToken);
                continue;
            }

            using (document)
            {
            var root = document.RootElement;
            var method = root.TryGetProperty("method", out var methodElement)
                ? methodElement.GetString()
                : null;
            var hasId = root.TryGetProperty("id", out var id);
            // 客户端对本服务器发起请求（如 client/registerCapability）的响应没有 method，忽略即可。
            if (method == null) continue;

            switch (method)
            {
                case "initialize" when hasId:
                    ReadClientCapabilities(root);
                    IndexWorkspaceComponents(root, cancellationToken);
                    await WriteResponseAsync(id, new
                    {
                        capabilities = new
                        {
                            textDocumentSync = 1,
                            positionEncoding = Utf16EncodingName,
                            completionProvider = new
                            {
                                triggerCharacters = new[]
                                    { "<", "/", "@", ":", "#", "v", "-", ".", " ", "{", "\"", ";", "[" }
                            },
                            hoverProvider = true,
                            documentSymbolProvider = true,
                            definitionProvider = true,
                            foldingRangeProvider = true,
                            colorProvider = true,
                            semanticTokensProvider = new
                            {
                                legend = new
                                {
                                    tokenTypes = TemplateSemanticTokens.TokenTypes,
                                    tokenModifiers = TemplateSemanticTokens.TokenModifiers
                                },
                                full = true
                            }
                        },
                        serverInfo = new { name = "Square Language Server", version = "0.1.0" }
                    }, cancellationToken);
                    break;
                case "initialized":
                    await ReportClientCapabilityWarningAsync(cancellationToken);
                    await RegisterFileWatchersAsync(cancellationToken);
                    break;
                case "shutdown" when hasId:
                    _shutdownRequested = true;
                    CancelAllPendingDiagnostics();
                    await WriteResponseAsync(id, null, cancellationToken);
                    break;
                case "$/cancelRequest":
                    CancelRequest(root);
                    break;
                case "workspace/didChangeWatchedFiles":
                    HandleWatchedFiles(root, cancellationToken);
                    break;
                case "workspace/didChangeConfiguration":
                    // 语言服务器的启动路径/参数变更需要客户端重启服务端，此处无状态可更新。
                    break;
                case "textDocument/didOpen":
                    HandleDidOpen(root, cancellationToken);
                    ScheduleDiagnostics(root, cancellationToken);
                    break;
                case "textDocument/didChange":
                    HandleDidChange(root, cancellationToken);
                    ScheduleDiagnostics(root, cancellationToken);
                    break;
                case "textDocument/didClose":
                    HandleDidClose(root, cancellationToken);
                    await PublishEmptyDiagnosticsAsync(root, cancellationToken);
                    break;
                case "textDocument/completion" when hasId:
                    {
                        var requestRoot = root.Clone();
                        var documentSnapshot = SnapshotDocument(root);
                        DispatchRequest(id, token => BuildCompletionAsync(requestRoot, documentSnapshot, token));
                        break;
                    }
                case "textDocument/hover" when hasId:
                    {
                        var requestRoot = root.Clone();
                        var documentSnapshot = SnapshotDocument(root);
                        DispatchRequest(id, token => BuildHoverAsync(requestRoot, documentSnapshot, token));
                        break;
                    }
                case "textDocument/documentSymbol" when hasId:
                    {
                        var requestRoot = root.Clone();
                        var documentSnapshot = SnapshotDocument(root);
                        DispatchRequest(id, token => BuildDocumentSymbolsAsync(requestRoot, documentSnapshot, token));
                        break;
                    }
                case "textDocument/definition" when hasId:
                    {
                        var requestRoot = root.Clone();
                        var documentSnapshot = SnapshotDocument(root);
                        DispatchRequest(id, token => BuildDefinitionAsync(requestRoot, documentSnapshot, token));
                        break;
                    }
                case "textDocument/semanticTokens/full" when hasId:
                    {
                        var requestRoot = root.Clone();
                        var documentSnapshot = SnapshotDocument(root);
                        DispatchRequest(id, token => BuildSemanticTokensAsync(requestRoot, documentSnapshot, token));
                        break;
                    }
                case "textDocument/foldingRange" when hasId:
                    await WriteResponseAsync(id, BuildFoldingRanges(root), cancellationToken);
                    break;
                case "textDocument/documentColor" when hasId:
                    await WriteResponseAsync(id, BuildDocumentColors(root), cancellationToken);
                    break;
                case "textDocument/colorPresentation" when hasId:
                    await WriteResponseAsync(id, BuildColorPresentations(root), cancellationToken);
                    break;
                case "exit":
                    return _shutdownRequested ? 0 : 1;
                default:
                    if (hasId)
                        await WriteErrorAsync(id, -32601, "Method not found", cancellationToken);
                    break;
            }
            }
        }

            return 0;
        }
        catch (EndOfStreamException)
        {
            return 1;
        }
        catch (IOException)
        {
            return 1;
        }
        finally
        {
            CancelAllPendingDiagnostics();
            await DrainDiagnosticTasksAsync().ConfigureAwait(false);
            await DrainRequestTasksAsync().ConfigureAwait(false);
            _projectWorkspace.Invalidated -= OnWorkspaceInvalidated;
            _projectWorkspace.Dispose();
        }
    }

    private void HandleDidOpen(JsonElement root, CancellationToken cancellationToken)
    {
        var textDocument = root.GetProperty("params").GetProperty("textDocument");
        var uri = textDocument.GetProperty("uri").GetString() ?? string.Empty;
        var text = textDocument.GetProperty("text").GetString() ?? string.Empty;
        var version = textDocument.GetProperty("version").GetInt32();
        cancellationToken.ThrowIfCancellationRequested();
        if (_documents.TryGet(uri, out var existing) && existing != null && version < existing.Version) return;
        var sourcePath = GetSourcePath(uri);
        _componentIndex.Update(sourcePath, text, cancellationToken: cancellationToken);
        _documents.Open(uri, version, text);
        InvalidateBuffer(sourcePath);
    }

    private void HandleDidChange(JsonElement root, CancellationToken cancellationToken)
    {
        var parameters = root.GetProperty("params");
        var textDocument = parameters.GetProperty("textDocument");
        var changes = parameters.GetProperty("contentChanges");
        var text = string.Empty;
        var uri = textDocument.GetProperty("uri").GetString() ?? string.Empty;
        if (changes.GetArrayLength() == 0)
        {
            if (_documents.TryGet(uri, out var current) && current != null)
                text = current.Text;
        }
        else
        {
            text = changes[0].GetProperty("text").GetString() ?? string.Empty;
        }
        var version = textDocument.GetProperty("version").GetInt32();
        cancellationToken.ThrowIfCancellationRequested();
        if (_documents.TryGet(uri, out var previous) && previous != null && version <= previous.Version) return;
        var sourcePath = GetSourcePath(uri);
        _componentIndex.Update(sourcePath, text, cancellationToken: cancellationToken);
        _documents.Change(uri, version, text);
        InvalidateBuffer(sourcePath);
    }

    private void HandleDidClose(JsonElement root, CancellationToken cancellationToken)
    {
        var uri = root.GetProperty("params").GetProperty("textDocument").GetProperty("uri").GetString();
        if (uri == null || !_documents.TryGet(uri, out var document) || document == null) return;
        var sourcePath = GetSourcePath(uri);
        CancelPendingDiagnostics(uri);
        lock (_diagnosticGate)
        {
            _documentProjects.Remove(uri);
            _diagnosticSequence.Remove(uri);
        }
        _componentIndex.Close(sourcePath, cancellationToken);
        _documents.Close(uri);
        SquareDocumentService.InvalidateSyntaxTree(sourcePath);
        InvalidateBuffer(sourcePath);
    }

    private void InvalidateBuffer(string sourcePath)
    {
        _projectWorkspace.InvalidateBuffer(sourcePath);
    }

    private async Task PublishDiagnosticsAsync(JsonElement root, CancellationToken cancellationToken, long sequence = 0)
    {
        var uri = root.GetProperty("params").GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
        if (!_documents.TryGet(uri, out var document) || document == null) return;
        var sourcePath = GetSourcePath(uri);
        if (!IsTemplatePath(sourcePath))
        {
            await WriteNotificationAsync("textDocument/publishDiagnostics", new
            {
                uri,
                version = document.Version,
                diagnostics = Array.Empty<object>()
            }, cancellationToken);
            return;
        }


        var result = SquareDocumentService.Parse(document.Text, sourcePath);
        var diagnostics = new List<object>();
        diagnostics.AddRange(result.Diagnostics.Select(diagnostic =>
        {
            var lineSpan = diagnostic.GetLinePositionSpan(result.SourceText);
            return (object)new
            {
                range = new
                {
                    start = new { line = lineSpan.Start.Line, character = lineSpan.Start.Character },
                    end = new { line = lineSpan.End.Line, character = lineSpan.End.Character }
                },
                severity = ToLspSeverity(diagnostic.Severity),
                code = diagnostic.Id,
                source = "square",
                message = diagnostic.Message
            };
        }));

        var project = await GetProjectContextAsync(sourcePath, cancellationToken);
        lock (_diagnosticGate)
        {
            _documentProjects[uri] = project?.ProjectPath;
        }
        await ReportWorkspaceWarningAsync(project, cancellationToken);
        if (project?.IsComplete == true && project.Analysis != null)
        {
            static string DiagnosticKey(string id, int line, int character, string message) =>
                id + "|" + line + ":" + character + "|" + message;
            var localKeys = new HashSet<string>(result.Diagnostics.Select(d =>
            {
                var span = d.GetLinePositionSpan(result.SourceText);
                return DiagnosticKey(d.Id, span.Start.Line, span.Start.Character, d.Message);
            }), StringComparer.Ordinal);
            int OffsetToLine(int offset)
            {
                var line = 0;
                for (var index = 0; index < offset && index < document.Text.Length; index++)
                    if (document.Text[index] == '\n') line++;
                return line;
            }
            foreach (var diagnostic in project.Analysis.Diagnostics.Where(item =>
                         string.IsNullOrWhiteSpace(item.SourcePath) || PathEquals(item.SourcePath, sourcePath)))
            {
                if (!result.IsSuccess && diagnostic.Id is "SQX0001" or "SQV0001") continue;
                int line, character;
                if (diagnostic.Range.Offset <= 0)
                {
                    line = 0;
                    character = 0;
                }
                else
                {
                    var clamped = Math.Min(diagnostic.Range.Offset, document.Text.Length);
                    line = OffsetToLine(clamped);
                    var lastNewline = clamped > 0 ? document.Text.LastIndexOf('\n', clamped - 1) : -1;
                    character = clamped - lastNewline - 1;
                }
                if (localKeys.Contains(DiagnosticKey(diagnostic.Id, line, character, diagnostic.Message))) continue;
                var range = string.IsNullOrWhiteSpace(diagnostic.SourcePath)
                    ? ToRange(document.Text, 0, 0)
                    : ToRange(document.Text, diagnostic.Range.Offset, diagnostic.Range.End);
                diagnostics.Add(new
                {
                    range,
                    severity = ToLspSeverity(diagnostic.Severity),
                    code = diagnostic.Id,
                    source = "square",
                    message = diagnostic.Message
                });
            }
        }

        if (!_documents.TryGet(uri, out var current) || current == null || current.Version != document.Version) return;
        if (IsSuperseded(uri, sequence)) return;
        await _outputGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_documents.TryGet(uri, out current) || current == null || current.Version != document.Version) return;
            if (IsSuperseded(uri, sequence)) return;
            var payload = new { jsonrpc = "2.0", method = "textDocument/publishDiagnostics", @params = new { uri, version = document.Version, diagnostics = diagnostics.ToArray() } };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            var header = Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");
            await _output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _outputGate.Release();
        }
    }

    private bool IsSuperseded(string uri, long sequence)
    {
        if (sequence == 0) return false;
        lock (_diagnosticGate)
        {
            return _diagnosticSequence.TryGetValue(uri, out var latest) && latest != sequence;
        }
    }

    private void OnWorkspaceInvalidated(IReadOnlyCollection<string> paths)
    {
        var affected = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        List<DocumentStore.DocumentState> documents;
        lock (_diagnosticGate)
        {
            documents = _documents.All.ToList();
        }
        foreach (var document in documents)
        {
            var sourcePath = GetSourcePath(document.Uri);
            if (!IsTemplatePath(sourcePath)) continue;
            string? projectPath;
            lock (_diagnosticGate)
            {
                _documentProjects.TryGetValue(document.Uri, out projectPath);
            }
            if (affected.Count > 0 && projectPath != null && !affected.Contains(projectPath)) continue;
            ScheduleDiagnosticsSnapshot(document.Uri, _lifetimeToken);
        }
    }

    private void ScheduleDiagnosticsSnapshot(string uri, CancellationToken cancellationToken)
    {
        if (!_documents.TryGet(uri, out var document) || document == null) return;
        using var message = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "textDocument/didChange",
            @params = new
            {
                textDocument = new { uri, version = document.Version },
                contentChanges = new[] { new { text = document.Text } }
            }
        }));
        ScheduleDiagnostics(message.RootElement.Clone(), cancellationToken);
    }

    private void ScheduleDiagnostics(JsonElement root, CancellationToken cancellationToken)
    {
        var snapshot = root.Clone();
        var uri = snapshot.GetProperty("params").GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        long sequence;
        lock (_diagnosticGate)
        {
            if (_pendingDiagnostics.TryGetValue(uri, out var previous))
            {
                previous.Cancel();
            }
            sequence = ++_nextDiagnosticSequence;
            _pendingDiagnostics[uri] = source;
            _diagnosticSequence[uri] = sequence;
        }
        var task = PublishDiagnosticsAfterDelayAsync(snapshot, uri, source, sequence);
        lock (_diagnosticGate)
        {
            _pendingDiagnosticTasks[uri] = task;
        }
    }

    private async Task PublishDiagnosticsAfterDelayAsync(
        JsonElement root,
        string uri,
        CancellationTokenSource source,
        long sequence)
    {
        try
        {
            await Task.Delay(DiagnosticDelayMilliseconds, source.Token);
            await PublishDiagnosticsAsync(root, source.Token, sequence);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            lock (_diagnosticGate)
            {
                if (_pendingDiagnostics.TryGetValue(uri, out var current) && ReferenceEquals(current, source))
                    _pendingDiagnostics.Remove(uri);
                if (_diagnosticSequence.TryGetValue(uri, out var latest) && latest == sequence)
                    _pendingDiagnosticTasks.Remove(uri);
            }
            source.Dispose();
        }
    }

    private void CancelPendingDiagnostics(string uri)
    {
        lock (_diagnosticGate)
        {
            if (!_pendingDiagnostics.TryGetValue(uri, out var source)) return;
            _pendingDiagnostics.Remove(uri);
            source.Cancel();
        }
    }

    private void CancelAllPendingDiagnostics()
    {
        lock (_diagnosticGate)
        {
            foreach (var source in _pendingDiagnostics.Values) source.Cancel();
            _pendingDiagnostics.Clear();
            _diagnosticSequence.Clear();
        }
    }

    private async Task DrainDiagnosticTasksAsync()
    {
        Task[] tasks;
        lock (_diagnosticGate)
        {
            tasks = _pendingDiagnosticTasks.Values.ToArray();
            _pendingDiagnosticTasks.Clear();
        }
        await DrainTasksAsync(tasks).ConfigureAwait(false);
    }

    private async Task DrainRequestTasksAsync()
    {
        Task[] tasks;
        lock (_requestGate)
        {
            tasks = _pendingRequestTasks.ToArray();
            _pendingRequestTasks.Clear();
        }
        await DrainTasksAsync(tasks).ConfigureAwait(false);
    }

    private static async Task DrainTasksAsync(IEnumerable<Task> tasks)
    {
        foreach (var task in tasks)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    /// <summary>在后台任务中计算请求并按 request id 写响应，避免阻塞消息循环。</summary>
    private void DispatchRequest<T>(JsonElement id, Func<CancellationToken, Task<T>> build)
    {
        var requestId = id.Clone();
        var key = RequestKey(requestId);
        var source = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
        lock (_requestGate) _pendingRequestCancellations[key] = source;
        var task = Task.Run(async () =>
        {
            var cancelledByClient = false;
            try
            {
                var result = await build(source.Token).ConfigureAwait(false);
                await WriteResponseAsync(requestId, result, _lifetimeToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelledByClient = !_lifetimeToken.IsCancellationRequested;
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception exception)
            {
                await TryWriteErrorAsync(requestId, -32603, exception.Message).ConfigureAwait(false);
            }

            if (cancelledByClient)
                await TryWriteErrorAsync(requestId, RequestCancelledCode, "Request cancelled").ConfigureAwait(false);
        }, CancellationToken.None);
        lock (_requestGate) _pendingRequestTasks.Add(task);
        _ = task.ContinueWith(
            completed =>
            {
                lock (_requestGate)
                {
                    _pendingRequestTasks.Remove(completed);
                    _pendingRequestCancellations.Remove(key);
                }
                source.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>写错误响应；服务端不得因写入竞争或关停竞态而抛出未处理异常。</summary>
    private async Task TryWriteErrorAsync(JsonElement id, int code, string message)
    {
        try
        {
            await WriteErrorAsync(id, code, message, _lifetimeToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
    }

    private async Task PublishEmptyDiagnosticsAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var uri = root.GetProperty("params").GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
        await WriteNotificationAsync("textDocument/publishDiagnostics", new { uri, diagnostics = Array.Empty<object>() }, cancellationToken);
    }

    private static string GetSourcePath(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile)
            return parsed.LocalPath;
        return uri;
    }

    private DocumentStore.DocumentState? SnapshotDocument(JsonElement root)
    {
        var uri = root.GetProperty("params").GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
        return _documents.TryGet(uri, out var document) ? document : null;
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static int ToLspSeverity(SquareDiagnosticSeverity severity) => severity switch
    {
        SquareDiagnosticSeverity.Warning => 2,
        SquareDiagnosticSeverity.Information => 3,
        SquareDiagnosticSeverity.Hint => 4,
        _ => 1
    };

    private async Task<object> BuildCompletionAsync(JsonElement root, DocumentStore.DocumentState? document, CancellationToken cancellationToken)
    {
        var parameters = root.GetProperty("params");
        if (document == null)
            return new { isIncomplete = false, items = Array.Empty<object>() };
        var uri = document.Uri;

        var sourcePath = GetSourcePath(uri);
        var project = await GetProjectContextAsync(sourcePath, cancellationToken);
        await ReportWorkspaceWarningAsync(project, cancellationToken);
        var position = parameters.GetProperty("position");
        var offset = GetOffset(document.Text, position.GetProperty("line").GetInt32(), position.GetProperty("character").GetInt32());
        var context = TemplateCompletionService.GetContext(document.Text, offset, sourcePath);
        var resolutionContext = CreateResolutionContext(project, document.Text, sourcePath);
        var resolved = project?.Catalog?.ResolveComponent(context.TagName, resolutionContext);
        TemplateComponentEventDescriptor? currentComponentEvent = null;
        TemplateComponentEventDescriptor[] componentEvents = Array.Empty<TemplateComponentEventDescriptor>();
        TemplatePropDescriptor[] componentProps = Array.Empty<TemplatePropDescriptor>();
        if (resolved?.Status == TemplateElementResolutionStatus.Resolved)
        {
            componentEvents = project!.Catalog!.GetEvents(resolved.Component).ToArray();
            componentProps = project.Catalog.GetProps(resolved.Component).ToArray();
        }
        else if (project?.Catalog == null)
        {
            _componentIndex.TryGetEvents(context.TagName, out componentEvents);
            _componentIndex.TryGetProps(context.TagName, out componentProps);
        }
        if (context.Kind == TemplateCompletionKind.EventHandler)
        {
            var eventName = NormalizeComponentEventName(context.AttributeName);
            currentComponentEvent = componentEvents.FirstOrDefault(componentEvent =>
                NormalizeEventAlias(componentEvent.Name).Equals(eventName, StringComparison.OrdinalIgnoreCase));
        }

        var completionItems = (currentComponentEvent == null
                ? TemplateCompletionService.GetItems(
                    context,
                    document.Text,
                    catalog: project?.Catalog ?? TemplateCatalog.BuiltIn,
                    resolutionContext: resolutionContext)
                : TemplateCompletionService.GetItems(
                    context,
                    document.Text,
                    currentComponentEvent,
                    project?.Catalog ?? TemplateCatalog.BuiltIn,
                    resolutionContext))
            .ToList();
        if (context.Kind == TemplateCompletionKind.Tag && project?.Catalog == null)
        {
            completionItems.AddRange(_componentIndex.Components
                .Where(component => component.TagName.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
                .Select(component => new TemplateCompletionItem(component.TagName, 14, component.TypeName, component.TagName)));
        }
        if (context.Kind is TemplateCompletionKind.Attribute or TemplateCompletionKind.Binding)
        {
            var existing = new HashSet<string>(
                context.ExistingAttributes.Select(NormalizeComponentPropertyName),
                StringComparer.OrdinalIgnoreCase);
            var availableProps = componentProps.Where(prop => !existing.Contains(prop.Name)).ToArray();
            completionItems.AddRange(availableProps
                .Where(prop => prop.Name.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
                .Select(prop => new TemplateCompletionItem(
                    prop.Name, 10, prop.TypeName + (prop.Required ? " (required)" : string.Empty), prop.Name)));
            if (context.IsSqv && context.Kind == TemplateCompletionKind.Attribute)
                completionItems.AddRange(availableProps
                    .Select(prop => (Prop: prop, Name: ":" + prop.Name))
                    .Where(item => item.Name.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
                    .Select(item => new TemplateCompletionItem(
                        item.Name, 10, "Dynamic " + item.Prop.TypeName + (item.Prop.Required ? " (required)" : string.Empty), item.Name)));
        }
        if (context.Kind is TemplateCompletionKind.Attribute or TemplateCompletionKind.Event)
        {
            var existingEvents = new HashSet<string>(
                context.ExistingAttributes.Select(NormalizeComponentEventName),
                StringComparer.OrdinalIgnoreCase);
            completionItems.AddRange(componentEvents
                .Where(componentEvent => !existingEvents.Contains(NormalizeEventAlias(componentEvent.Name)))
                .Select(componentEvent => new
                {
                    Event = componentEvent,
                    Name = context.IsSqv
                        ? context.Kind == TemplateCompletionKind.Attribute
                            ? "@" + componentEvent.Name
                            : componentEvent.Name
                        : componentEvent.SqxName
                })
                .Where(item => item.Name.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
                .Select(item => new TemplateCompletionItem(
                    item.Name,
                    23,
                    item.Event.HasDetail ? "CustomEvent<" + item.Event.DetailTypeName + ">" : "Event",
                    item.Name)));
        }

        var items = completionItems
            .GroupBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(item => new
            {
                label = item.Label,
                kind = item.Kind,
                detail = item.Detail,
                insertText = item.InsertText,
                textEdit = new
                {
                    range = ToRange(document.Text, Math.Max(0, offset - context.Prefix.Length), offset),
                    newText = item.InsertText
                }
            })
            .Cast<object>()
            .ToArray();
        // 没有工程上下文（未提供工作区根、无工程拥有该文档或 MSBuild 不可用）时列表必然是残缺的，
        // 必须告知客户端可继续请求，否则会丢掉工程级组件。
        return new { isIncomplete = project?.IsComplete != true, items };
    }

    private async Task<ProjectTemplateContext?> GetProjectContextAsync(string sourcePath, CancellationToken cancellationToken)
    {
        var buffers = _documents.All
            .Select(document => new ProjectBufferSnapshot(GetSourcePath(document.Uri), document.Version, document.Text))
            .ToArray();
        return await _projectWorkspace.GetContextAsync(sourcePath, buffers, cancellationToken);
    }

    private static TemplateResolutionContext CreateResolutionContext(
        ProjectTemplateContext? project,
        string text,
        string sourcePath)
    {
        var parsed = SquareDocumentService.ParseSyntaxTree(text, sourcePath).ParsedSqxDocument;
        var currentNamespace = !string.IsNullOrWhiteSpace(parsed?.Namespace)
            ? parsed.Namespace
            : project?.CurrentNamespace ?? string.Empty;
        var usings = parsed?.Syntax?.Script?.CSharp.Usings
            .Where(directive => directive.Alias == null && directive.StaticKeyword.RawKind == 0)
            .Select(directive => directive.Name?.ToString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray() ?? Array.Empty<string>();
        return new TemplateResolutionContext(currentNamespace, usings);
    }

    private async Task ReportWorkspaceWarningAsync(ProjectTemplateContext? project, CancellationToken cancellationToken)
    {
        if (project == null || !project.ShouldReportWarning || string.IsNullOrWhiteSpace(project.Warning)) return;
        lock (_reportedWarnings)
        {
            if (!_reportedWarnings.Add(project.Warning)) return;
        }
        await WriteNotificationAsync("window/logMessage", new { type = 2, message = project.Warning }, cancellationToken);
    }

    private static IEnumerable<TemplateIrElement> EnumerateTemplateElements(IEnumerable<TemplateIrNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is TemplateIrElement element)
            {
                yield return element;
                foreach (var child in EnumerateTemplateElements(element.Children)) yield return child;
            }
            else if (node is TemplateIrFor loop)
            {
                foreach (var child in EnumerateTemplateElements(loop.Children)) yield return child;
                foreach (var child in EnumerateTemplateElements(loop.Fallback)) yield return child;
            }
            else if (node is TemplateIrIfChain chain)
                foreach (var branch in chain.Branches)
                    foreach (var child in EnumerateTemplateElements(branch.Children)) yield return child;
            else if (node is TemplateIrSlot slot)
                foreach (var child in EnumerateTemplateElements(slot.Children)) yield return child;
        }
    }

    private static bool IsStructuralTag(string tagName) => tagName.IndexOf(':') < 0 &&
        (tagName.Equals("Show", StringComparison.OrdinalIgnoreCase) ||
         tagName.Equals("For", StringComparison.OrdinalIgnoreCase) ||
         tagName.Equals("Index", StringComparison.OrdinalIgnoreCase) ||
         tagName.Equals("Switch", StringComparison.OrdinalIgnoreCase) ||
         tagName.Equals("Match", StringComparison.OrdinalIgnoreCase) ||
         tagName.Equals("Slot", StringComparison.OrdinalIgnoreCase) ||
         tagName.Equals("Outlet", StringComparison.OrdinalIgnoreCase) ||
         tagName.Equals("Fragment", StringComparison.OrdinalIgnoreCase) ||
         tagName.Equals("template", StringComparison.OrdinalIgnoreCase));

    private void IndexWorkspaceComponents(
        JsonElement initializeRequest,
        CancellationToken cancellationToken)
    {
        var roots = EnumerateWorkspaceRoots(initializeRequest, cancellationToken).ToArray();
        _componentIndex.Index(roots, cancellationToken);
        _projectWorkspace.SetRoots(roots);
    }

    internal static IEnumerable<string> EnumerateWorkspaceRoots(
        JsonElement initializeRequest,
        CancellationToken cancellationToken)
    {
        if (!initializeRequest.TryGetProperty("params", out var parameters)) yield break;
        var hasWorkspaceFolders = false;
        if (parameters.TryGetProperty("workspaceFolders", out var folders) &&
            folders.ValueKind == JsonValueKind.Array)
        {
            foreach (var folder in folders.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!folder.TryGetProperty("uri", out var uri)) continue;
                var value = uri.GetString();
                if (string.IsNullOrWhiteSpace(value)) continue;
                hasWorkspaceFolders = true;
                yield return GetSourcePath(value);
            }
        }
        if (hasWorkspaceFolders) yield break;

        cancellationToken.ThrowIfCancellationRequested();
        if (parameters.TryGetProperty("rootUri", out var rootUri) &&
            rootUri.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(rootUri.GetString()))
        {
            yield return GetSourcePath(rootUri.GetString()!);
            yield break;
        }
        if (parameters.TryGetProperty("rootPath", out var rootPath) &&
            rootPath.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(rootPath.GetString()))
            yield return rootPath.GetString()!;
    }

    private static string NormalizeComponentPropertyName(string name)
    {
        if (name.StartsWith(":", StringComparison.Ordinal)) name = name.Substring(1);
        else if (name.StartsWith("v-bind:", StringComparison.OrdinalIgnoreCase))
            name = name.Substring("v-bind:".Length);
        var modifier = name.IndexOf('.');
        return modifier < 0 ? name : name.Substring(0, modifier);
    }

    private static string NormalizeComponentEventName(string name)
    {
        if (name.StartsWith("@", StringComparison.Ordinal)) name = name.Substring(1);
        else if (name.StartsWith("v-on:", StringComparison.OrdinalIgnoreCase))
            name = name.Substring("v-on:".Length);
        else if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase) && name.Length > 2)
            name = name.Substring(2);
        var modifier = name.IndexOf('.');
        if (modifier >= 0) name = name.Substring(0, modifier);
        return new string(name
            .Where(character => character != '-')
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static string NormalizeEventAlias(string name) =>
        new string((name ?? string.Empty)
            .Where(character => character != '-')
            .Select(char.ToLowerInvariant)
            .ToArray());

    private static int GetOffset(string text, int line, int character)
    {
        if (line <= 0) return Math.Clamp(character, 0, text.Length);
        var currentLine = 0;
        var offset = 0;
        while (offset < text.Length && currentLine < line)
        {
            if (text[offset++] == '\n') currentLine++;
        }
        return Math.Clamp(offset + character, 0, text.Length);
    }

    private async Task<object?> BuildHoverAsync(JsonElement root, DocumentStore.DocumentState? document, CancellationToken cancellationToken)
    {
        var parameters = root.GetProperty("params");
        if (document == null) return null;
        var uri = document.Uri;

        var sourcePath = GetSourcePath(uri);
        var project = await GetProjectContextAsync(sourcePath, cancellationToken);
        await ReportWorkspaceWarningAsync(project, cancellationToken);
        var position = parameters.GetProperty("position");
        var offset = GetOffset(document.Text, position.GetProperty("line").GetInt32(), position.GetProperty("character").GetInt32());
        var resolutionContext = CreateResolutionContext(project, document.Text, sourcePath);
        var token = GetTokenAt(document.Text, offset, out var tokenStart, out var tokenEnd);
        if (token.Length == 0) return null;

        var scriptDetail = CSharpScriptCompletionService.GetHoverDetail(
            document.Text,
            offset,
            sourcePath,
            project?.Catalog ?? TemplateCatalog.BuiltIn,
            resolutionContext);
        if (!string.IsNullOrEmpty(scriptDetail))
            return CreateHoverResult(document.Text, "```csharp\n" + scriptDetail + "\n```", scriptDetail, tokenStart, tokenEnd);

        var lexicalContext = tokenStart > 0 ? document.Text[tokenStart - 1] : '\0';
        var completionContext = TemplateCompletionService.GetContext(document.Text, offset, sourcePath);
        (string? Markdown, string? Plain) hover;
        if (lexicalContext == '@')
            hover = DescribeEventHover(project, token, completionContext, resolutionContext);
        else if (lexicalContext == '<' || IsInsideTagName(document.Text, tokenStart))
        {
            var tagName = TemplateDefinitionService.GetTagNameAt(document.Text, sourcePath, offset);
            if (string.IsNullOrWhiteSpace(tagName)) tagName = token;
            hover = DescribeTagHover(tagName, project, CreateResolutionContext(project, document.Text, sourcePath));
        }
        else
            hover = DescribeAttributeHover(project, token, completionContext, resolutionContext);

        if (hover.Markdown == null) return null;
        return CreateHoverResult(document.Text, hover.Markdown, hover.Plain ?? hover.Markdown, tokenStart, tokenEnd);
    }

    private object CreateHoverResult(string text, string markdown, string plain, int start, int end) => new
    {
        contents = _hoverSupportsMarkdown
            ? (object)new { kind = "markdown", value = markdown }
            : new { kind = "plaintext", value = plain },
        range = new
        {
            start = ToPosition(text, start),
            end = ToPosition(text, end)
        }
    };

    private static (string? Markdown, string? Plain) DescribeTagHover(
        string tagName,
        ProjectTemplateContext? project,
        TemplateResolutionContext resolutionContext)
    {
        var component = ResolveComponentDescriptor(project?.Catalog, tagName, resolutionContext);
        if (component == null) return (null, null);
        // 标签名放入反引号，避免 markdown 渲染器把 <Button> 当原始 HTML 吞掉。
        return ("**`<" + tagName + ">`**\n\n`" + component.TypeName + "`",
            "<" + tagName + "> — " + component.TypeName);
    }

    private (string? Markdown, string? Plain) DescribeEventHover(
        ProjectTemplateContext? project,
        string token,
        TemplateCompletionContext completionContext,
        TemplateResolutionContext resolutionContext)
    {
        var component = ResolveComponentDescriptor(project?.Catalog, completionContext.TagName, resolutionContext);
        var componentEvent = component == null || project?.Catalog == null
            ? null
            : project.Catalog.GetEvents(component).FirstOrDefault(item =>
                NormalizeEventAlias(item.Name).Equals(NormalizeComponentEventName(token), StringComparison.OrdinalIgnoreCase));
        if (componentEvent != null)
        {
            var detail = componentEvent.HasDetail ? "CustomEvent<" + componentEvent.DetailTypeName + ">" : "Event";
            return ("**`@" + token + "`**\n\n`" + detail + "`", "@" + token + " — " + detail);
        }
        var standardEvent = TemplateCatalog.BuiltIn.Events.FirstOrDefault(item =>
            NormalizeEventAlias(item.Name).Equals(NormalizeComponentEventName(token), StringComparison.OrdinalIgnoreCase));
        if (standardEvent == null) return (null, null);
        return ("**`@" + token + "`**\n\n`Square event: " + standardEvent.CanonicalName + "`",
            "@" + token + " — Square event: " + standardEvent.CanonicalName);
    }

    /// <summary>属性名 hover：先按组件事件、组件属性、标准事件、内置属性别名的顺序匹配。</summary>
    private (string? Markdown, string? Plain) DescribeAttributeHover(
        ProjectTemplateContext? project,
        string token,
        TemplateCompletionContext completionContext,
        TemplateResolutionContext resolutionContext)
    {
        if (completionContext.Kind is TemplateCompletionKind.None or TemplateCompletionKind.Tag or
            TemplateCompletionKind.ClosingTag or TemplateCompletionKind.CssClass or
            TemplateCompletionKind.CssProperty or TemplateCompletionKind.CssValue or
            TemplateCompletionKind.CssSelector or TemplateCompletionKind.CssPseudoClass or
            TemplateCompletionKind.CssPseudoElement) return (null, null);

        var attributeName = string.IsNullOrWhiteSpace(completionContext.AttributeName)
            ? token
            : completionContext.AttributeName;
        var propertyName = NormalizeComponentPropertyName(attributeName);
        // 只在光标位于属性名上时给出说明，避免在属性值/表达式里张冠李戴。
        if (!propertyName.EndsWith(token, StringComparison.OrdinalIgnoreCase) &&
            !attributeName.EndsWith(token, StringComparison.OrdinalIgnoreCase)) return (null, null);

        var component = ResolveComponentDescriptor(project?.Catalog, completionContext.TagName, resolutionContext);
        TemplateComponentEventDescriptor[] events = Array.Empty<TemplateComponentEventDescriptor>();
        TemplatePropDescriptor[] props = Array.Empty<TemplatePropDescriptor>();
        if (component != null && project?.Catalog != null)
        {
            events = project.Catalog.GetEvents(component).ToArray();
            props = project.Catalog.GetProps(component).ToArray();
        }
        else if (project?.Catalog == null)
        {
            _componentIndex.TryGetEvents(completionContext.TagName, out events);
            _componentIndex.TryGetProps(completionContext.TagName, out props);
        }

        var eventName = NormalizeComponentEventName(attributeName);
        var componentEvent = events.FirstOrDefault(item =>
            NormalizeEventAlias(item.Name).Equals(eventName, StringComparison.OrdinalIgnoreCase));
        if (componentEvent != null)
        {
            var detail = componentEvent.HasDetail ? "CustomEvent<" + componentEvent.DetailTypeName + ">" : "Event";
            return ("**`" + attributeName + "`**\n\n`" + detail + "`", attributeName + " — " + detail);
        }

        var property = props.FirstOrDefault(item => item.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
        if (property != null)
        {
            var detail = property.TypeName + (property.Required ? " (required)" : string.Empty);
            return ("**`" + attributeName + "`**\n\n`" + detail + "`", attributeName + " — " + detail);
        }

        var standardEvent = TemplateCatalog.BuiltIn.Events.FirstOrDefault(item =>
            NormalizeEventAlias(item.Name).Equals(eventName, StringComparison.OrdinalIgnoreCase));
        if (standardEvent != null)
            return ("**`" + attributeName + "`**\n\n`Square event: " + standardEvent.CanonicalName + "`",
                attributeName + " — Square event: " + standardEvent.CanonicalName);

        var alias = TemplateCatalog.BuiltIn.Properties.FirstOrDefault(item =>
            item.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
        if (alias == null) return (null, null);
        var aliasDetail = alias.CanonicalName + " (" + alias.ValueKind + ")";
        return ("**`" + attributeName + "`**\n\n`" + aliasDetail + "`", attributeName + " — " + aliasDetail);
    }

    private static TemplateComponentDescriptor? ResolveComponentDescriptor(
        TemplateCatalog? catalog,
        string tagName,
        TemplateResolutionContext resolutionContext)
    {
        if (string.IsNullOrWhiteSpace(tagName)) return null;
        var resolution = catalog?.ResolveComponent(tagName, resolutionContext);
        if (resolution?.Status == TemplateElementResolutionStatus.Resolved) return resolution.Component;
        return TemplateCatalog.BuiltIn.TryGetBuiltInComponent(tagName, out var builtIn) ? builtIn : null;
    }

    private static bool IsInsideTagName(string text, int tokenStart)
    {
        for (var index = tokenStart - 1; index >= 0; index--)
        {
            if (text[index] == '<') return true;
            if (text[index] is '>' or '\n' or '\r') return false;
            if (char.IsWhiteSpace(text[index])) return false;
        }
        return false;
    }

    private static string GetTokenAt(string text, int offset, out int start, out int end)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        start = offset;
        if (start == text.Length || (start > 0 && !IsTokenCharacter(text[start]))) start--;
        while (start >= 0 && IsTokenCharacter(text[start])) start--;
        start++;
        end = Math.Min(text.Length, Math.Max(offset, start));
        while (end < text.Length && IsTokenCharacter(text[end])) end++;
        return start < end ? text[start..end] : string.Empty;
    }

    private static bool IsTokenCharacter(char value) => char.IsLetterOrDigit(value) || value is '-' or '_' or ':' or '.';

    private static object ToPosition(string text, int offset)
    {
        var line = 0;
        var lineStart = 0;
        for (var index = 0; index < offset && index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                lineStart = index + 1;
            }
        }
        return new { line, character = offset - lineStart };
    }

    private async Task<object> BuildDocumentSymbolsAsync(JsonElement root, DocumentStore.DocumentState? document, CancellationToken cancellationToken)
    {
        var parameters = root.GetProperty("params");
        if (document == null) return Array.Empty<object>();
        var uri = document.Uri;

        var sourcePath = GetSourcePath(uri);
        var project = await GetProjectContextAsync(sourcePath, cancellationToken);
        var resolutionContext = CreateResolutionContext(project, document.Text, sourcePath);
        var children = TemplateDocumentSymbols.GetSymbols(
                document.Text,
                sourcePath,
                project?.Catalog ?? TemplateCatalog.BuiltIn,
                resolutionContext)
            .Select(MapSymbol)
            .ToList();

        Dictionary<string, object?> MapSymbol(TemplateDocumentSymbol symbol) => new()
        {
            ["name"] = symbol.Name,
            ["detail"] = symbol.Detail,
            ["kind"] = symbol.Kind,
            ["range"] = ToRange(document.Text, symbol.Range.Offset, symbol.Range.End),
            ["selectionRange"] = ToRange(
                document.Text,
                symbol.SelectionRange.Offset,
                symbol.SelectionRange.End),
            ["children"] = symbol.Children.Select(MapSymbol).ToList()
        };

        var componentName = GetSourcePath(uri);
        componentName = Path.GetFileNameWithoutExtension(componentName);
        var component = new Dictionary<string, object?>
        {
            ["name"] = string.IsNullOrWhiteSpace(componentName) ? "Document" : componentName,
            ["detail"] = "Square component",
            ["kind"] = 5,
            ["range"] = ToRange(document.Text, 0, document.Text.Length),
            ["selectionRange"] = ToRange(document.Text, 0, Math.Min(document.Text.Length, componentName.Length)),
            ["children"] = children
        };
        return new[] { component };
    }

    private static object ToRange(string text, int start, int end) => new
    {
        start = ToPosition(text, start),
        end = ToPosition(text, end)
    };

    private async Task<object> BuildSemanticTokensAsync(JsonElement root, DocumentStore.DocumentState? document, CancellationToken cancellationToken)
    {
        var parameters = root.GetProperty("params");
        if (document == null)
            return new { data = Array.Empty<int>() };
        var uri = document.Uri;

        var sourcePath = GetSourcePath(uri);
        var project = await GetProjectContextAsync(sourcePath, cancellationToken);
        return new
        {
            data = TemplateSemanticTokens.Encode(
                document.Text,
                sourcePath,
                project?.Catalog ?? TemplateCatalog.BuiltIn,
                CreateResolutionContext(project, document.Text, sourcePath))
        };
    }

    private object BuildFoldingRanges(JsonElement root)
    {
        var parameters = root.GetProperty("params");
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
        if (!_documents.TryGet(uri, out var document) || document == null)
            return Array.Empty<object>();

        return TemplateFoldingService.GetRanges(document.Text, GetSourcePath(uri))
            .Select(range => new
            {
                startLine = range.StartLine,
                endLine = range.EndLine,
                kind = range.Kind
            })
            .Cast<object>()
            .ToArray();
    }

    private object BuildDocumentColors(JsonElement root)
    {
        var parameters = root.GetProperty("params");
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
        if (!_documents.TryGet(uri, out var document) || document == null)
            return Array.Empty<object>();

        return TemplateColorService.GetColors(document.Text, GetSourcePath(uri))
            .Select(color => new
            {
                range = ToRange(document.Text, color.Start, color.Start + color.Length),
                color = new { red = color.Red, green = color.Green, blue = color.Blue, alpha = color.Alpha }
            })
            .Cast<object>()
            .ToArray();
    }

    private object BuildColorPresentations(JsonElement root)
    {
        var parameters = root.GetProperty("params");
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
        if (!_documents.TryGet(uri, out var document) || document == null)
            return Array.Empty<object>();

        var color = parameters.GetProperty("color");
        var range = parameters.GetProperty("range");
        var start = GetOffset(document.Text, range.GetProperty("start").GetProperty("line").GetInt32(),
            range.GetProperty("start").GetProperty("character").GetInt32());
        var end = GetOffset(document.Text, range.GetProperty("end").GetProperty("line").GetInt32(),
            range.GetProperty("end").GetProperty("character").GetInt32());
        return TemplateColorService.GetPresentations(
                document.Text,
                start,
                Math.Max(0, end - start),
                color.GetProperty("red").GetDouble(),
                color.GetProperty("green").GetDouble(),
                color.GetProperty("blue").GetDouble(),
                color.GetProperty("alpha").GetDouble())
            .Select(presentation => new
            {
                label = presentation.Label,
                textEdit = new
                {
                    range = ToRange(document.Text, presentation.Start, presentation.Start + presentation.Length),
                    newText = presentation.Label
                }
            })
            .Cast<object>()
            .ToArray();
    }

    private async Task<object> BuildDefinitionAsync(JsonElement root, DocumentStore.DocumentState? document, CancellationToken cancellationToken)
    {
        var parameters = root.GetProperty("params");
        if (document == null) return Array.Empty<object>();
        var uri = document.Uri;

        var sourcePath = GetSourcePath(uri);
        var position = parameters.GetProperty("position");
        var offset = GetOffset(document.Text, position.GetProperty("line").GetInt32(), position.GetProperty("character").GetInt32());
        var token = TemplateDefinitionService.GetTagNameAt(document.Text, sourcePath, offset);
        if (string.IsNullOrWhiteSpace(token)) return Array.Empty<object>();
        var project = await GetProjectContextAsync(sourcePath, cancellationToken);
        await ReportWorkspaceWarningAsync(project, cancellationToken);
        var resolution = project?.Catalog?.ResolveComponent(token, CreateResolutionContext(project, document.Text, sourcePath));
        if (resolution?.Status == TemplateElementResolutionStatus.Resolved &&
            !string.IsNullOrWhiteSpace(resolution.Component.SourcePath) &&
            File.Exists(resolution.Component.SourcePath))
        {
            var targetPath = Path.GetFullPath(resolution.Component.SourcePath);
            var definitionUri = new Uri(targetPath).AbsoluteUri;
            return new[] { CreateDefinitionResult(definitionUri, ReadSourceText(targetPath), resolution.Component.LocalName) };
        }

        if (project?.IsComplete == true && project.Analysis != null) return Array.Empty<object>();
        foreach (var candidate in _documents.All)
        {
            if (candidate.Uri.Equals(uri, StringComparison.OrdinalIgnoreCase)) continue;
            var name = Path.GetFileNameWithoutExtension(GetSourcePath(candidate.Uri));
            if (!name.Equals(token, StringComparison.OrdinalIgnoreCase)) continue;
            return new[] { CreateDefinitionResult(candidate.Uri, candidate.Text, name) };
        }
        return Array.Empty<object>();
    }

    private string ReadSourceText(string sourcePath)
    {
        var open = _documents.All.FirstOrDefault(candidate =>
            string.Equals(GetSourcePath(candidate.Uri), sourcePath, StringComparison.OrdinalIgnoreCase));
        if (open != null) return open.Text;
        try
        {
            return File.Exists(sourcePath) ? File.ReadAllText(sourcePath) : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <summary>客户端声明 textDocument.definition.linkSupport 时返回 LocationLink，否则返回 Location。</summary>
    private object CreateDefinitionResult(string targetUri, string text, string componentName)
    {
        var (start, length) = FindDeclarationRange(text, componentName);
        if (_definitionSupportsLocationLinks)
            return new
            {
                targetUri,
                targetRange = ToRange(text, 0, text.Length),
                targetSelectionRange = ToRange(text, start, start + length)
            };
        return new { uri = targetUri, range = ToRange(text, start, start + length) };
    }

    private static (int Start, int Length) FindDeclarationRange(string text, string componentName)
    {
        if (text.Length == 0) return (0, 0);
        if (!string.IsNullOrWhiteSpace(componentName))
        {
            // 代码后置文件里优先选中类型声明，避免命中文档注释中的同名引用。
            foreach (var keyword in new[] { "class ", "struct ", "record " })
            {
                var declaration = text.IndexOf(keyword + componentName, StringComparison.Ordinal);
                if (declaration >= 0) return (declaration + keyword.Length, componentName.Length);
            }
        }
        const string template = "<template";
        var index = text.IndexOf(template, StringComparison.OrdinalIgnoreCase);
        if (index >= 0) return (index + 1, template.Length - 1);
        if (!string.IsNullOrWhiteSpace(componentName))
        {
            index = text.IndexOf(componentName, StringComparison.Ordinal);
            if (index >= 0) return (index, componentName.Length);
        }
        return (0, Math.Min(1, text.Length));
    }

    private readonly record struct FrameReadResult(string? Payload, bool Malformed);

    /// <summary>
    /// 读取一条 JSON-RPC 报文。Payload 为 null 且 Malformed 为 false 表示流已结束；
    /// Malformed 为 true 表示头部缺失或非法（已消费头部，调用方应回 -32700 后继续）。
    /// </summary>
    private async Task<FrameReadResult> ReadFrameAsync(CancellationToken cancellationToken)
    {
        var contentLength = -1;
        while (true)
        {
            var line = await ReadAsciiLineAsync(cancellationToken);
            if (line == null) return new FrameReadResult(null, false);
            if (line.Length == 0) break;

            const string prefix = "Content-Length:";
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(line[prefix.Length..].Trim(), out contentLength) && contentLength >= 0) continue;
            contentLength = -1;
            break;
        }

        if (contentLength < 0) return new FrameReadResult(null, true);

        var bytes = new byte[contentLength];
        await ReadExactlyAsync(_input, bytes, cancellationToken);
        return new FrameReadResult(Encoding.UTF8.GetString(bytes), false);
    }

    private Task WriteParseErrorAsync(CancellationToken cancellationToken) =>
        WriteJsonAsync(
            new { jsonrpc = "2.0", id = (object?)null, error = new { code = ParseErrorCode, message = "Parse error" } },
            cancellationToken);

    private static string RequestKey(JsonElement id) => id.ValueKind == JsonValueKind.String
        ? "s:" + id.GetString()
        : "n:" + id.GetRawText();

    private void CancelRequest(JsonElement root)
    {
        if (!root.TryGetProperty("params", out var parameters) ||
            !parameters.TryGetProperty("id", out var id)) return;
        CancellationTokenSource? source;
        lock (_requestGate) _pendingRequestCancellations.TryGetValue(RequestKey(id), out source);
        if (source == null) return;
        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ReadClientCapabilities(JsonElement initializeRequest)
    {
        _hoverSupportsMarkdown = true;
        _definitionSupportsLocationLinks = false;
        _watchRegistrationSupported = false;
        _clientCapabilityWarning = null;
        if (!initializeRequest.TryGetProperty("params", out var parameters) ||
            !parameters.TryGetProperty("capabilities", out var capabilities) ||
            capabilities.ValueKind != JsonValueKind.Object) return;

        if (capabilities.TryGetProperty("general", out var general) &&
            general.ValueKind == JsonValueKind.Object &&
            general.TryGetProperty("positionEncodings", out var encodings) &&
            encodings.ValueKind == JsonValueKind.Array)
        {
            var declared = encodings.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .ToArray();
            if (declared.Length > 0 && !declared.Contains(Utf16EncodingName, StringComparer.OrdinalIgnoreCase))
                _clientCapabilityWarning = "Client does not support '" + Utf16EncodingName +
                    "' in general.positionEncodings (declared: " + string.Join(", ", declared) +
                    "); positions are reported as UTF-16 code units and may be misaligned.";
        }

        if (!capabilities.TryGetProperty("textDocument", out var textDocument) ||
            textDocument.ValueKind != JsonValueKind.Object) return;

        if (textDocument.TryGetProperty("hover", out var hover) &&
            hover.ValueKind == JsonValueKind.Object &&
            hover.TryGetProperty("contentFormat", out var contentFormat) &&
            contentFormat.ValueKind == JsonValueKind.Array)
        {
            _hoverSupportsMarkdown = contentFormat.EnumerateArray()
                .Any(item => item.ValueKind == JsonValueKind.String &&
                             item.GetString()!.Equals("markdown", StringComparison.OrdinalIgnoreCase));
        }

        if (textDocument.TryGetProperty("definition", out var definition) &&
            definition.ValueKind == JsonValueKind.Object &&
            definition.TryGetProperty("linkSupport", out var linkSupport) &&
            linkSupport.ValueKind is JsonValueKind.True or JsonValueKind.False)
            _definitionSupportsLocationLinks = linkSupport.GetBoolean();

        if (capabilities.TryGetProperty("workspace", out var workspace) &&
            workspace.ValueKind == JsonValueKind.Object &&
            workspace.TryGetProperty("didChangeWatchedFiles", out var watchedFiles) &&
            watchedFiles.ValueKind == JsonValueKind.Object &&
            watchedFiles.TryGetProperty("dynamicRegistration", out var dynamicRegistration) &&
            dynamicRegistration.ValueKind is JsonValueKind.True or JsonValueKind.False)
            _watchRegistrationSupported = dynamicRegistration.GetBoolean();
    }

    private async Task ReportClientCapabilityWarningAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_clientCapabilityWarning)) return;
        await WriteNotificationAsync("window/logMessage",
            new { type = 2, message = _clientCapabilityWarning }, cancellationToken);
    }

    /// <summary>注册模板文件与工程文件变更监视，避免编辑器外部改动导致索引过期。</summary>
    private async Task RegisterFileWatchersAsync(CancellationToken cancellationToken)
    {
        if (!_watchRegistrationSupported) return;
        await WriteJsonAsync(new
        {
            jsonrpc = "2.0",
            id = "square-file-watchers",
            method = "client/registerCapability",
            @params = new
            {
                registrations = new object[]
                {
                    new
                    {
                        id = "square-file-watchers",
                        method = "workspace/didChangeWatchedFiles",
                        registerOptions = new
                        {
                            watchers = new object[]
                            {
                                new { globPattern = "**/*.sqx" },
                                new { globPattern = "**/*.sqv" },
                                new { globPattern = "**/*.csproj" }
                            }
                        }
                    }
                }
            }
        }, cancellationToken);
    }

    private void HandleWatchedFiles(JsonElement root, CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("params", out var parameters) ||
            !parameters.TryGetProperty("changes", out var changes) ||
            changes.ValueKind != JsonValueKind.Array) return;

        foreach (var change in changes.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!change.TryGetProperty("uri", out var uriElement)) continue;
            var uri = uriElement.GetString();
            if (string.IsNullOrWhiteSpace(uri)) continue;
            var sourcePath = GetSourcePath(uri);

            if (sourcePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                _projectWorkspace.Invalidate(sourcePath);
                continue;
            }
            if (!IsTemplatePath(sourcePath)) continue;
            // 已打开的文档以编辑器缓冲区为准，磁盘状态不覆盖它。
            if (_documents.TryGet(uri, out var open) && open != null) continue;
            _componentIndex.RestoreFromDisk(sourcePath, cancellationToken);
            _projectWorkspace.InvalidateBuffer(sourcePath);
        }
    }

    private static bool IsTemplatePath(string sourcePath) =>
        sourcePath.EndsWith(".sqx", StringComparison.OrdinalIgnoreCase) ||
        sourcePath.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase);

    private async Task WriteResponseAsync(JsonElement id, object? result, CancellationToken cancellationToken)
    {
        await WriteJsonAsync(new { jsonrpc = "2.0", id, result }, cancellationToken);
    }

    private async Task WriteErrorAsync(JsonElement id, int code, string message, CancellationToken cancellationToken)
    {
        await WriteJsonAsync(new { jsonrpc = "2.0", id, error = new { code, message } }, cancellationToken);
    }

    private async Task WriteNotificationAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        await WriteJsonAsync(new { jsonrpc = "2.0", method, @params = parameters }, cancellationToken);
    }

    private async Task WriteJsonAsync(object message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
        await _outputGate.WaitAsync(cancellationToken);
        try
        {
            await _output.WriteAsync(header, cancellationToken);
            await _output.WriteAsync(payload, cancellationToken);
            await _output.FlushAsync(cancellationToken);
        }
        finally
        {
            _outputGate.Release();
        }
    }

    private async Task<string?> ReadAsciiLineAsync(CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        while (true)
        {
            var value = await ReadByteAsync(cancellationToken);
            if (value < 0)
            {
                if (bytes.Count == 0) return null;
                break;
            }
            if (value == '\n') break;
            if (value != '\r') bytes.Add((byte)value);
        }
        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private async Task<int> ReadByteAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        var read = await _input.ReadAsync(buffer, cancellationToken);
        return read == 0 ? -1 : buffer[0];
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }
}
