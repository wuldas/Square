using System.Text;
using System.Text.Json;
using Square.Compiler.LanguageServices;

namespace Square.LanguageServer;

public sealed class LanguageServerHost
{
    private const int DiagnosticDelayMilliseconds = 120;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly DocumentStore _documents = new();
    private readonly WorkspaceComponentIndex _componentIndex = new();
    private readonly object _diagnosticGate = new();
    private readonly Dictionary<string, CancellationTokenSource> _pendingDiagnostics = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _outputGate = new(1, 1);
    private bool _shutdownRequested;

    public LanguageServerHost(Stream input, Stream output)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
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
                    await PublishDiagnosticsAsync(root, cancellationToken);
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
                    await WriteResponseAsync(id, BuildCompletion(root), cancellationToken);
                    break;
                case "textDocument/hover" when hasId:
                    await WriteResponseAsync(id, BuildHover(root), cancellationToken);
                    break;
                case "textDocument/documentSymbol" when hasId:
                    await WriteResponseAsync(id, BuildDocumentSymbols(root), cancellationToken);
                    break;
                case "textDocument/definition" when hasId:
                    await WriteResponseAsync(id, BuildDefinition(root), cancellationToken);
                    break;
                case "textDocument/semanticTokens/full" when hasId:
                    await WriteResponseAsync(id, BuildSemanticTokens(root), cancellationToken);
                    break;
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

    private void HandleDidOpen(JsonElement root, CancellationToken cancellationToken)
    {
        var textDocument = root.GetProperty("params").GetProperty("textDocument");
        var uri = textDocument.GetProperty("uri").GetString() ?? string.Empty;
        var text = textDocument.GetProperty("text").GetString() ?? string.Empty;
        _componentIndex.Update(GetSourcePath(uri), text, cancellationToken: cancellationToken);
        _documents.Open(
            uri,
            textDocument.GetProperty("version").GetInt32(),
            text);
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
        _componentIndex.Update(GetSourcePath(uri), text, cancellationToken: cancellationToken);
        _documents.Change(
            uri,
            textDocument.GetProperty("version").GetInt32(),
            text);
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
    }

    private async Task PublishDiagnosticsAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var uri = root.GetProperty("params").GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
        if (!_documents.TryGet(uri, out var document) || document == null) return;

        var sourcePath = GetSourcePath(uri);
        var result = SquareDocumentService.Parse(document.Text, sourcePath);
        var diagnostics = result.Diagnostics.Select(diagnostic =>
        {
            var lineSpan = diagnostic.GetLinePositionSpan(result.SourceText);
            return new
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
        }).ToArray();

        await WriteNotificationAsync("textDocument/publishDiagnostics", new { uri, version = document.Version, diagnostics }, cancellationToken);
    }

    private void ScheduleDiagnostics(JsonElement root, CancellationToken cancellationToken)
    {
        var snapshot = root.Clone();
        var uri = snapshot.GetProperty("params").GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_diagnosticGate)
        {
            if (_pendingDiagnostics.TryGetValue(uri, out var previous))
            {
                previous.Cancel();
            }
            _pendingDiagnostics[uri] = source;
        }
        _ = PublishDiagnosticsAfterDelayAsync(snapshot, uri, source);
    }

    private async Task PublishDiagnosticsAfterDelayAsync(
        JsonElement root,
        string uri,
        CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(DiagnosticDelayMilliseconds, source.Token);
            await PublishDiagnosticsAsync(root, source.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lock (_diagnosticGate)
            {
                if (_pendingDiagnostics.TryGetValue(uri, out var current) && ReferenceEquals(current, source))
                    _pendingDiagnostics.Remove(uri);
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

    private static int ToLspSeverity(SquareDiagnosticSeverity severity) => severity switch
    {
        SquareDiagnosticSeverity.Warning => 2,
        SquareDiagnosticSeverity.Information => 3,
        SquareDiagnosticSeverity.Hint => 4,
        _ => 1
    };

    private object BuildCompletion(JsonElement root)
    {
        var parameters = root.GetProperty("params");
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
        if (!_documents.TryGet(uri, out var document) || document == null)
            return new { isIncomplete = false, items = Array.Empty<object>() };

        var position = parameters.GetProperty("position");
        var offset = GetOffset(document.Text, position.GetProperty("line").GetInt32(), position.GetProperty("character").GetInt32());
        var context = TemplateCompletionService.GetContext(document.Text, offset, GetSourcePath(uri));
        var completionItems = TemplateCompletionService.GetItems(context, document.Text).ToList();
        if (context.Kind is TemplateCompletionKind.Tag or
            TemplateCompletionKind.Attribute or
            TemplateCompletionKind.Binding)
        {
            if (context.Kind == TemplateCompletionKind.Tag)
            {
                completionItems.AddRange(_componentIndex.Components
                    .Where(component => component.TagName.StartsWith(
                        context.Prefix,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(component => new TemplateCompletionItem(
                        component.TagName,
                        14,
                        component.TypeName,
                        component.TagName)));
            }
            else
            {
                if (_componentIndex.TryGetProps(context.TagName, out var props))
                {
                    var existing = new HashSet<string>(
                        context.ExistingAttributes.Select(NormalizeComponentPropertyName),
                        StringComparer.OrdinalIgnoreCase);
                    var availableProps = props
                        .Where(prop => !existing.Contains(prop.Name))
                        .ToArray();
                    completionItems.AddRange(availableProps
                        .Where(prop => prop.Name.StartsWith(
                            context.Prefix,
                            StringComparison.OrdinalIgnoreCase))
                        .Select(prop => new TemplateCompletionItem(
                            prop.Name,
                            10,
                            prop.TypeName + (prop.Required ? " (required)" : string.Empty),
                            prop.Name)));
                    if (context.IsSqv && context.Kind == TemplateCompletionKind.Attribute)
                        completionItems.AddRange(availableProps
                            .Select(prop => (Prop: prop, Name: ":" + prop.Name))
                            .Where(item => item.Name.StartsWith(
                                context.Prefix,
                                StringComparison.OrdinalIgnoreCase))
                            .Select(item => new TemplateCompletionItem(
                                item.Name,
                                10,
                                "Dynamic " + item.Prop.TypeName +
                                (item.Prop.Required ? " (required)" : string.Empty),
                                item.Name)));
                }
            }
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
                    range = ToRange(
                        document.Text,
                        Math.Max(0, offset - context.Prefix.Length),
                        offset),
                    newText = item.InsertText
                }
            })
            .Cast<object>()
            .ToArray();
        return new { isIncomplete = false, items };
    }

    private void IndexWorkspaceComponents(
        JsonElement initializeRequest,
        CancellationToken cancellationToken) =>
        _componentIndex.Index(
            EnumerateWorkspaceRoots(initializeRequest, cancellationToken),
            cancellationToken);

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

    private object? BuildHover(JsonElement root)
    {
        var parameters = root.GetProperty("params");
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
        if (!_documents.TryGet(uri, out var document) || document == null)
            return null;

        var position = parameters.GetProperty("position");
        var offset = GetOffset(document.Text, position.GetProperty("line").GetInt32(), position.GetProperty("character").GetInt32());
        var token = GetTokenAt(document.Text, offset, out var tokenStart, out var tokenEnd);
        if (token.Length == 0) return null;

        var scriptDetail = CSharpScriptCompletionService.GetHoverDetail(
            document.Text,
            offset,
            GetSourcePath(uri));
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

        var context = tokenStart > 0 ? document.Text[tokenStart - 1] : '\0';
        string? markdown;
        if (context == '@')
        {
            var eventDescriptor = TemplateCatalog.BuiltIn.Events.FirstOrDefault(eventItem =>
                eventItem.Name.Equals(token, StringComparison.OrdinalIgnoreCase));
            markdown = eventDescriptor == null
                ? null
                : $"**@{eventDescriptor.Name}**\\n\\nSquare event.";
        }
        else if (context == '<' || IsInsideTagName(document.Text, tokenStart))
        {
            var component = TemplateCatalog.BuiltIn.GetComponent(token);
            markdown = $"**<{component.TagName}>**\\n\\n`{component.TypeName}`";
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

    private static bool IsTokenCharacter(char value) => char.IsLetterOrDigit(value) || value is '-' or '_';

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

    private object BuildDocumentSymbols(JsonElement root)
    {
        var parameters = root.GetProperty("params");
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
        if (!_documents.TryGet(uri, out var document) || document == null)
            return Array.Empty<object>();

        var children = TemplateDocumentSymbols.GetSymbols(document.Text, GetSourcePath(uri))
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

    private object BuildSemanticTokens(JsonElement root)
    {
        var parameters = root.GetProperty("params");
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
        if (!_documents.TryGet(uri, out var document) || document == null)
            return new { data = Array.Empty<int>() };

        return new { data = TemplateSemanticTokens.Encode(document.Text, GetSourcePath(uri)) };
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

    private object BuildDefinition(JsonElement root)
    {
        var parameters = root.GetProperty("params");
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
        if (!_documents.TryGet(uri, out var document) || document == null)
            return Array.Empty<object>();

        var position = parameters.GetProperty("position");
        var offset = GetOffset(document.Text, position.GetProperty("line").GetInt32(), position.GetProperty("character").GetInt32());
        var token = TemplateDefinitionService.GetTagNameAt(document.Text, GetSourcePath(uri), offset);
        if (string.IsNullOrWhiteSpace(token)) return Array.Empty<object>();

        foreach (var candidate in _documents.All)
        {
            if (candidate.Uri.Equals(uri, StringComparison.OrdinalIgnoreCase)) continue;
            var name = Path.GetFileNameWithoutExtension(GetSourcePath(candidate.Uri));
            if (!name.Equals(token, StringComparison.OrdinalIgnoreCase)) continue;

            return new[]
            {
                new
                {
                    uri = candidate.Uri,
                    range = ToRange(candidate.Text, 0, candidate.Text.Length),
                    targetSelectionRange = ToRange(candidate.Text, 0, 0)
                }
            };
        }

        return Array.Empty<object>();
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
