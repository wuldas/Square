using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Square.Compiler.LanguageServices;

namespace Square.LanguageServer;

public sealed class LanguageServerHost
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly DocumentStore _documents = new();
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
                    await WriteResponseAsync(id, new
                    {
                        capabilities = new
                        {
                            textDocumentSync = 1,
                            completionProvider = new { triggerCharacters = new[] { "<", "@" } },
                            hoverProvider = true,
                            documentSymbolProvider = true,
                            definitionProvider = true
                        },
                        serverInfo = new { name = "Square Language Server", version = "0.1.0" }
                    }, cancellationToken);
                    break;
                case "shutdown" when hasId:
                    _shutdownRequested = true;
                    await WriteResponseAsync(id, null, cancellationToken);
                    break;
                case "textDocument/didOpen":
                    HandleDidOpen(root);
                    await PublishDiagnosticsAsync(root, cancellationToken);
                    break;
                case "textDocument/didChange":
                    HandleDidChange(root);
                    await PublishDiagnosticsAsync(root, cancellationToken);
                    break;
                case "textDocument/didClose":
                    HandleDidClose(root);
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

    private void HandleDidOpen(JsonElement root)
    {
        var textDocument = root.GetProperty("params").GetProperty("textDocument");
        _documents.Open(
            textDocument.GetProperty("uri").GetString() ?? string.Empty,
            textDocument.GetProperty("version").GetInt32(),
            textDocument.GetProperty("text").GetString() ?? string.Empty);
    }

    private void HandleDidChange(JsonElement root)
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
        _documents.Change(
            uri,
            textDocument.GetProperty("version").GetInt32(),
            text);
    }

    private void HandleDidClose(JsonElement root)
    {
        var uri = root.GetProperty("params").GetProperty("textDocument").GetProperty("uri").GetString();
        if (uri != null) _documents.Close(uri);
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
        var prefix = GetCompletionPrefix(document.Text, offset, out var eventContext);
        if (eventContext)
        {
            var items = TemplateCatalog.BuiltIn.Events
                .Where(eventDescriptor => eventDescriptor.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(eventDescriptor => new
                {
                    label = eventDescriptor.Name,
                    kind = 23,
                    detail = "Square event",
                    insertText = eventDescriptor.Name
                })
                .Cast<object>()
                .ToArray();
            return new { isIncomplete = false, items };
        }

        if (TryGetClassCompletionContext(document.Text, offset, out var classPrefix))
        {
            var classes = ExtractCssClassNames(document.Text)
                .Where(name => name.StartsWith(classPrefix, StringComparison.OrdinalIgnoreCase))
                .Select(name => new
                {
                    label = name,
                    kind = 12,
                    detail = "CSS class",
                    insertText = name
                })
                .Cast<object>()
                .ToArray();
            return new { isIncomplete = false, items = classes };
        }

        if (IsInsideAttributeList(document.Text, offset))
        {
            var properties = TemplateCatalog.BuiltIn.Properties
                .Where(property => property.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(property => new
                {
                    label = property.Name,
                    kind = 10,
                    detail = property.CanonicalName,
                    insertText = property.Name
                })
                .Cast<object>()
                .ToArray();
            return new { isIncomplete = false, items = properties };
        }

        var components = TemplateCatalog.BuiltIn.Components
            .Where(component => component.TagName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(component => new
            {
                label = component.TagName,
                kind = 7,
                detail = component.TypeName,
                insertText = component.TagName
            })
            .Cast<object>()
            .ToArray();
        return new { isIncomplete = false, items = components };
    }

    private static string GetCompletionPrefix(string text, int offset, out bool eventContext)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var start = offset;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] is '-' or '_'))
            start--;

        eventContext = start > 0 && text[start - 1] == '@';
        if (eventContext) return text[start..offset];
        if (start > 0 && text[start - 1] == '<') return text[start..offset];
        return string.Empty;
    }

    private static bool IsInsideAttributeList(string text, int offset)
    {
        var tagStart = -1;
        for (var index = Math.Min(offset - 1, text.Length - 1); index >= 0; index--)
        {
            if (text[index] == '<')
            {
                tagStart = index;
                break;
            }
            if (text[index] is '>' or '\n' or '\r') return false;
        }

        if (tagStart < 0) return false;
        var indexAfterName = tagStart + 1;
        if (indexAfterName < text.Length && text[indexAfterName] == '/') indexAfterName++;
        while (indexAfterName < offset && IsTokenCharacter(text[indexAfterName])) indexAfterName++;
        return indexAfterName < offset && char.IsWhiteSpace(text[indexAfterName]);
    }

    private static bool TryGetClassCompletionContext(string text, int offset, out string prefix)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var start = offset;
        while (start > 0 && IsTokenCharacter(text[start - 1])) start--;

        var quote = start - 1;
        while (quote >= 0 && text[quote] is not ('"' or '\'')) quote--;
        if (quote < 0)
        {
            prefix = string.Empty;
            return false;
        }

        var tagStart = text.LastIndexOf('<', quote);
        var tagEnd = text.LastIndexOf('>', quote);
        if (tagStart <= tagEnd)
        {
            prefix = string.Empty;
            return false;
        }

        var beforeQuote = text.Substring(tagStart + 1, quote - tagStart - 1);
        if (!Regex.IsMatch(beforeQuote, @"\bclass\s*=\s*$", RegexOptions.IgnoreCase))
        {
            prefix = string.Empty;
            return false;
        }

        prefix = text[start..offset];
        return true;
    }

    private static IReadOnlyCollection<string> ExtractCssClassNames(string text)
    {
        var style = Regex.Match(
            text,
            @"<style\b[^>]*>(?<body>[\s\S]*?)</style\s*>",
            RegexOptions.IgnoreCase);
        if (!style.Success) return Array.Empty<string>();

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(
            style.Groups["body"].Value,
            @"(?<![A-Za-z0-9_-])\.([A-Za-z_][A-Za-z0-9_-]*)"))
        {
            names.Add(match.Groups[1].Value);
        }
        return names;
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

        var children = new List<Dictionary<string, object?>>();
        var stack = new Stack<(string Name, Dictionary<string, object?> Node)>();
        var index = 0;
        while (index < document.Text.Length)
        {
            var open = document.Text.IndexOf('<', index);
            if (open < 0) break;
            index = open + 1;
            if (index >= document.Text.Length) break;

            var closing = document.Text[index] == '/';
            if (closing) index++;
            while (index < document.Text.Length && char.IsWhiteSpace(document.Text[index])) index++;
            var nameStart = index;
            while (index < document.Text.Length && IsTokenCharacter(document.Text[index])) index++;
            if (nameStart == index)
            {
                index++;
                continue;
            }

            var name = document.Text[nameStart..index];
            var tagEnd = document.Text.IndexOf('>', index);
            if (tagEnd < 0) tagEnd = document.Text.Length - 1;
            if (closing)
            {
                if (stack.Count > 0 && stack.Peek().Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    stack.Pop();
                index = tagEnd + 1;
                continue;
            }

            if (name is "template" or "script" or "style")
            {
                index = tagEnd + 1;
                continue;
            }

            var nodeChildren = new List<Dictionary<string, object?>>();
            var node = new Dictionary<string, object?>
            {
                ["name"] = name,
                ["detail"] = TemplateCatalog.BuiltIn.GetComponent(name).TypeName,
                ["kind"] = 19,
                ["range"] = ToRange(document.Text, open, Math.Min(document.Text.Length, tagEnd + 1)),
                ["selectionRange"] = ToRange(document.Text, nameStart, index),
                ["children"] = nodeChildren
            };
            if (stack.Count > 0)
                ((List<Dictionary<string, object?>>)stack.Peek().Node["children"]!).Add(node);
            else
                children.Add(node);

            var selfClosing = tagEnd > open && document.Text[tagEnd - 1] == '/';
            if (!selfClosing) stack.Push((name, node));
            index = tagEnd + 1;
        }

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

    private object BuildDefinition(JsonElement root)
    {
        var parameters = root.GetProperty("params");
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
        if (!_documents.TryGet(uri, out var document) || document == null)
            return Array.Empty<object>();

        var position = parameters.GetProperty("position");
        var offset = GetOffset(document.Text, position.GetProperty("line").GetInt32(), position.GetProperty("character").GetInt32());
        var token = GetTokenAt(document.Text, offset, out _, out _);
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
        await _output.WriteAsync(header, cancellationToken);
        await _output.WriteAsync(payload, cancellationToken);
        await _output.FlushAsync(cancellationToken);
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
