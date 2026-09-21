using System.Text;
using System.Text.Json;
using Square.Compiler.LanguageServices;
using Square.Compiler.Template.Ir;

namespace Square.LanguageServer;

public sealed class LanguageServerHost
{
    private const int DiagnosticDelayMilliseconds = 120;
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
    private long _nextDiagnosticSequence;
    private readonly SemaphoreSlim _outputGate = new(1, 1);
    private bool _shutdownRequested;
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
            var message = await ReadMessageAsync(cancellationToken);
            if (message == null) return _shutdownRequested ? 0 : 1;

            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            var method = root.TryGetProperty("method", out var methodElement)
                ? methodElement.GetString()
                : null;
            var hasId = root.TryGetProperty("id", out var id);

            switch (method)
            {
                case "initialize" when hasId:
                    IndexWorkspaceComponents(root, cancellationToken);
                    await WriteResponseAsync(id, new
                    {
                        capabilities = new
                        {
                            textDocumentSync = 1,
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
                case "shutdown" when hasId:
                    _shutdownRequested = true;
                    CancelAllPendingDiagnostics();
                    await WriteResponseAsync(id, null, cancellationToken);
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
                        DispatchRequest(id, () => BuildCompletionAsync(requestRoot, documentSnapshot, cancellationToken));
                        break;
                    }
                case "textDocument/hover" when hasId:
                    {
                        var requestRoot = root.Clone();
                        var documentSnapshot = SnapshotDocument(root);
                        DispatchRequest(id, () => BuildHoverAsync(requestRoot, documentSnapshot, cancellationToken));
                        break;
                    }
                case "textDocument/documentSymbol" when hasId:
                    {
                        var requestRoot = root.Clone();
                        var documentSnapshot = SnapshotDocument(root);
                        DispatchRequest(id, () => BuildDocumentSymbolsAsync(requestRoot, documentSnapshot, cancellationToken));
                        break;
                    }
                case "textDocument/definition" when hasId:
                    {
                        var requestRoot = root.Clone();
                        var documentSnapshot = SnapshotDocument(root);
                        DispatchRequest(id, () => BuildDefinitionAsync(requestRoot, documentSnapshot, cancellationToken));
                        break;
                    }
                case "textDocument/semanticTokens/full" when hasId:
                    {
                        var requestRoot = root.Clone();
                        var documentSnapshot = SnapshotDocument(root);
                        DispatchRequest(id, () => BuildSemanticTokensAsync(requestRoot, documentSnapshot, cancellationToken));
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

            return 0;
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
        if (!sourcePath.EndsWith(".sqx", StringComparison.OrdinalIgnoreCase) &&
            !sourcePath.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase))
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
                    character = clamped - document.Text.LastIndexOf('\n', clamped - 1) - 1;
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
            if (!sourcePath.EndsWith(".sqx", StringComparison.OrdinalIgnoreCase) &&
                !sourcePath.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase)) continue;
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
    private void DispatchRequest<T>(JsonElement id, Func<Task<T>> build)
    {
        var requestId = id.Clone();
        var task = Task.Run(async () =>
        {
            try
            {
                var result = await build().ConfigureAwait(false);
                await WriteResponseAsync(requestId, result, _lifetimeToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception exception)
            {
                await WriteErrorAsync(requestId, -32603, exception.Message, _lifetimeToken).ConfigureAwait(false);
            }
        }, CancellationToken.None);
        lock (_requestGate) _pendingRequestTasks.Add(task);
        _ = task.ContinueWith(
            completed =>
            {
                lock (_requestGate) _pendingRequestTasks.Remove(completed);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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
        return new { isIncomplete = project != null && !project.IsComplete, items };
    }

    private static IEnumerable<TemplateCompletionItem> GetProjectTagItems(
        TemplateCatalog catalog,
        TemplateResolutionContext context,
        string prefix)
    {
        foreach (var component in catalog.Components)
        {
            var shortResolution = catalog.ResolveComponent(component.LocalName, context);
            if (shortResolution.Status == TemplateElementResolutionStatus.Resolved &&
                SameComponent(shortResolution.Component, component) &&
                component.LocalName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                yield return new TemplateCompletionItem(component.LocalName, component.IsBuiltIn ? 7 : 14, component.TypeName, component.LocalName);
            foreach (var qualifiedName in catalog.GetQualifiedNames(component))
                if (qualifiedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    yield return new TemplateCompletionItem(qualifiedName, component.IsBuiltIn ? 7 : 14, component.TypeName, qualifiedName);
        }
    }

    private static bool SameComponent(TemplateComponentDescriptor left, TemplateComponentDescriptor right) =>
        left.AssemblyName == right.AssemblyName && left.TypeMetadataName == right.TypeMetadataName;

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
        if (string.IsNullOrWhiteSpace(project?.Warning)) return;
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
        {
            return new
            {
                contents = new { kind = "markdown", value = "```csharp\n" + scriptDetail + "\n```" },
                range = new
                {
                    start = ToPosition(document.Text, tokenStart),
                    end = ToPosition(document.Text, tokenEnd)
                }
            };
        }

        var lexicalContext = tokenStart > 0 ? document.Text[tokenStart - 1] : '\0';
        string? markdown;
        if (lexicalContext == '@')
        {
            var completionContext = TemplateCompletionService.GetContext(document.Text, offset, sourcePath);
            resolutionContext = CreateResolutionContext(project, document.Text, sourcePath);
            var resolution = project?.Catalog?.ResolveComponent(completionContext.TagName, resolutionContext);
            TemplateComponentEventDescriptor? componentEvent = null;
            if (resolution?.Status == TemplateElementResolutionStatus.Resolved)
                componentEvent = project!.Catalog!.GetEvents(resolution.Component)
                    .FirstOrDefault(item => item.Name.Equals(token, StringComparison.OrdinalIgnoreCase));
            var eventDescriptor = TemplateCatalog.BuiltIn.Events.FirstOrDefault(eventItem =>
                eventItem.Name.Equals(token, StringComparison.OrdinalIgnoreCase));
            markdown = componentEvent != null
                ? $"**@{componentEvent.Name}**\\n\\n`{(componentEvent.HasDetail ? "CustomEvent<" + componentEvent.DetailTypeName + ">" : "Event")}`"
                : eventDescriptor == null ? null : $"**@{eventDescriptor.Name}**\\n\\nSquare event.";
        }
        else if (lexicalContext == '<' || IsInsideTagName(document.Text, tokenStart))
        {
            var tagName = TemplateDefinitionService.GetTagNameAt(document.Text, sourcePath, offset);
            if (string.IsNullOrWhiteSpace(tagName)) tagName = token;
            var resolution = project?.Catalog?.ResolveComponent(
                tagName,
                CreateResolutionContext(project, document.Text, sourcePath));
            TemplateComponentDescriptor? component = resolution?.Status == TemplateElementResolutionStatus.Resolved
                ? resolution.Component
                : null;
            if (component == null && !TemplateCatalog.BuiltIn.TryGetBuiltInComponent(tagName, out component)) return null;
            markdown = $"**<{tagName}>**\\n\\n`{component.TypeName}`";
        }
        else
        {
            return null;
        }

        return new
        {
            contents = new { kind = "markdown", value = markdown },
            range = new
            {
                start = ToPosition(document.Text, tokenStart),
                end = ToPosition(document.Text, tokenEnd)
            }
        };
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
            return new[] { CreateLocationLink(definitionUri, ReadSourceText(targetPath), resolution.Component.LocalName) };
        }

        if (project?.IsComplete == true && project.Analysis != null) return Array.Empty<object>();
        foreach (var candidate in _documents.All)
        {
            if (candidate.Uri.Equals(uri, StringComparison.OrdinalIgnoreCase)) continue;
            var name = Path.GetFileNameWithoutExtension(GetSourcePath(candidate.Uri));
            if (!name.Equals(token, StringComparison.OrdinalIgnoreCase)) continue;
            return new[] { CreateLocationLink(candidate.Uri, candidate.Text, name) };
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

    private static object CreateLocationLink(string targetUri, string text, string componentName)
    {
        var (start, length) = FindDeclarationRange(text, componentName);
        return new
        {
            targetUri,
            targetRange = ToRange(text, 0, text.Length),
            targetSelectionRange = ToRange(text, start, start + length)
        };
    }

    private static (int Start, int Length) FindDeclarationRange(string text, string componentName)
    {
        if (text.Length == 0) return (0, 0);
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

    private async Task<string?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var contentLength = -1;
        while (true)
        {
            var line = await ReadAsciiLineAsync(cancellationToken);
            if (line == null) return null;
            if (line.Length == 0) break;

            const string prefix = "Content-Length:";
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(line[prefix.Length..].Trim(), out contentLength) || contentLength < 0)
                    throw new InvalidDataException("Invalid Content-Length header.");
            }
        }

        if (contentLength < 0)
            throw new InvalidDataException("Missing Content-Length header.");

        var bytes = new byte[contentLength];
        await ReadExactlyAsync(_input, bytes, cancellationToken);
        return Encoding.UTF8.GetString(bytes);
    }

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
