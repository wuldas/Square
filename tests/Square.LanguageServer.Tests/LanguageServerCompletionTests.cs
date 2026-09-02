using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Square.LanguageServer.Tests;

public sealed class LanguageServerCompletionTests
{
    [Fact]
    public async Task CompletionOffersCatalogTagsAndEvents()
    {
        using var process = StartServer();
        await Write(process, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"initialized","params":{}}""");

        await Write(process, """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqx","languageId":"sqx","version":1,"text":"<template><Bu"}}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","id":2,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqx"},"position":{"line":0,"character":13}}}""");
        var tags = await Read(process.StandardOutput);
        Assert.Contains("\"label\":\"Button\"", tags, StringComparison.Ordinal);

        await Write(process, """{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqx","version":2},"contentChanges":[{"text":"<template><Button on"}]}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","id":3,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqx"},"position":{"line":0,"character":20}}}""");
        var events = await Read(process.StandardOutput);
        Assert.Contains("\"label\":\"onClick\"", events, StringComparison.Ordinal);
        Assert.Contains("\"kind\":23", events, StringComparison.Ordinal);

        await Write(process, """{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqx","version":3},"contentChanges":[{"text":"<template><Sh"}]}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","id":4,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqx"},"position":{"line":0,"character":13}}}""");
        var controlFlow = await Read(process.StandardOutput);
        Assert.Contains("\"label\":\"Show\"", controlFlow, StringComparison.Ordinal);

        await Write(process, """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqv","languageId":"sqv","version":1,"text":"<template><Text v-"}}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","id":5,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqv"},"position":{"line":0,"character":18}}}""");
        var directives = await Read(process.StandardOutput);
        Assert.Contains("\"label\":\"v-if\"", directives, StringComparison.Ordinal);
        Assert.Contains("Vue directive", directives, StringComparison.Ordinal);

        await Write(process, """{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqv","version":2},"contentChanges":[{"text":"<template><Button @"}]}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","id":6,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqv"},"position":{"line":0,"character":19}}}""");
        var vueEvents = await Read(process.StandardOutput);
        Assert.Contains("\"label\":\"click\"", vueEvents, StringComparison.Ordinal);

        await Write(process, """{"jsonrpc":"2.0","id":7,"method":"shutdown","params":null}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"exit","params":null}""");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task CompletionOffersWorkspaceComponentEventsForBothDialects()
    {
        using var process = StartServer();
        await Write(process, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"initialized","params":{}}""");

        const string card = "<template><View /></template><script>public static readonly ComponentEvent<int> ItemSelectedEvent = new(\"item-selected\");</script>";
        await OpenDocument(process, "file:///C:/Square/Card.sqx", "sqx", card);
        const string sqxUsage = "<template><Card on";
        await OpenDocument(process, "file:///C:/Square/Page.sqx", "sqx", sqxUsage);
        await Write(process, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "textDocument/completion",
            @params = new
            {
                textDocument = new { uri = "file:///C:/Square/Page.sqx" },
                position = new { line = 0, character = sqxUsage.Length }
            }
        }));
        var sqxCompletion = await Read(process.StandardOutput);
        AssertCompletionItem(sqxCompletion, "onItemSelected", "CustomEvent<int>");

        const string sqxWithExistingEvent = "<template><Card onItemSelected={OnSelected} on";
        await Write(process, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "textDocument/didChange",
            @params = new
            {
                textDocument = new { uri = "file:///C:/Square/Page.sqx", version = 2 },
                contentChanges = new[] { new { text = sqxWithExistingEvent } }
            }
        }));
        _ = await Read(process.StandardOutput);
        await Write(process, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "textDocument/completion",
            @params = new
            {
                textDocument = new { uri = "file:///C:/Square/Page.sqx" },
                position = new { line = 0, character = sqxWithExistingEvent.Length }
            }
        }));
        var sqxDeduplicated = await Read(process.StandardOutput);
        Assert.DoesNotContain("\"label\":\"onItemSelected\"", sqxDeduplicated, StringComparison.Ordinal);

        const string handlerPage = "<template><Card onItemSelected={On} /></template><script>private void OnTyped(CustomEvent<int> e) { } private void OnWrong(CustomEvent<string> e) { }</script>";
        await OpenDocument(process, "file:///C:/Square/HandlerPage.sqx", "sqx", handlerPage);
        var handlerOffset = handlerPage.IndexOf("{On}", StringComparison.Ordinal) + "{On".Length;
        await Write(process, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 4,
            method = "textDocument/completion",
            @params = new
            {
                textDocument = new { uri = "file:///C:/Square/HandlerPage.sqx" },
                position = new { line = 0, character = handlerOffset }
            }
        }));
        var handlerCompletion = await Read(process.StandardOutput);
        Assert.Contains("\"label\":\"OnTyped\"", handlerCompletion, StringComparison.Ordinal);
        Assert.DoesNotContain("\"label\":\"OnWrong\"", handlerCompletion, StringComparison.Ordinal);

        await OpenDocument(process, "file:///C:/Square/VueCard.sqv", "sqv", card);
        const string sqvUsage = "<template><VueCard @";
        await OpenDocument(process, "file:///C:/Square/Page.sqv", "sqv", sqvUsage);
        await Write(process, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 5,
            method = "textDocument/completion",
            @params = new
            {
                textDocument = new { uri = "file:///C:/Square/Page.sqv" },
                position = new { line = 0, character = sqvUsage.Length }
            }
        }));
        var sqvCompletion = await Read(process.StandardOutput);
        AssertCompletionItem(sqvCompletion, "item-selected", "CustomEvent<int>");

        await Write(process, """{"jsonrpc":"2.0","id":6,"method":"shutdown","params":null}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"exit","params":null}""");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task EventExpressionCompletionOffersCurrentScriptMethods()
    {
        using var process = StartServer();
        await Write(process, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"initialized","params":{}}""");
        const string source = "<template><Button onClick={OnS} /></template><script>private Event? OnState { get; set; } private void OnSave(Event e) { }</script>";
        await Write(process, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "textDocument/didOpen",
            @params = new
            {
                textDocument = new
                {
                    uri = "file:///C:/Square/Handlers.sqx",
                    languageId = "sqx",
                    version = 1,
                    text = source
                }
            }
        }));
        _ = await Read(process.StandardOutput);
        var character = source.IndexOf("OnS}", StringComparison.Ordinal) + "OnS".Length;
        await Write(process,
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"textDocument/completion\",\"params\":{\"textDocument\":{\"uri\":\"file:///C:/Square/Handlers.sqx\"},\"position\":{\"line\":0,\"character\":" + character + "}}}");

        var completion = await Read(process.StandardOutput);

        Assert.Contains("\"label\":\"OnSave\"", completion, StringComparison.Ordinal);
        Assert.Contains("\"kind\":3", completion, StringComparison.Ordinal);
        Assert.DoesNotContain("\"label\":\"OnState\"", completion, StringComparison.Ordinal);

        await Write(process, """{"jsonrpc":"2.0","id":3,"method":"shutdown","params":null}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"exit","params":null}""");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task TagCompletionOffersComponentsFromOtherOpenDocuments()
    {
        using var process = StartServer();
        await Write(process, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"initialized","params":{}}""");

        await Write(process, """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///C:/Square/Card.sqx","languageId":"sqx","version":1,"text":"<template><View /></template>"}}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///C:/Square/Page.sqx","languageId":"sqx","version":1,"text":"<template><Ca"}}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","id":2,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///C:/Square/Page.sqx"},"position":{"line":0,"character":13}}}""");

        var completion = await Read(process.StandardOutput);

        Assert.Contains("\"label\":\"Card\"", completion, StringComparison.Ordinal);
        Assert.Contains("\"detail\":\"Card\"", completion, StringComparison.Ordinal);

        await Write(process, """{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///C:/Square/Card.sqx","version":2},"contentChanges":[{"text":"<template><"}]}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","id":3,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///C:/Square/Page.sqx"},"position":{"line":0,"character":13}}}""");
        var completionDuringInvalidEdit = await Read(process.StandardOutput);
        Assert.Contains("\"label\":\"Card\"", completionDuringInvalidEdit, StringComparison.Ordinal);

        await Write(process, """{"jsonrpc":"2.0","id":4,"method":"shutdown","params":null}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"exit","params":null}""");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task CompletionUsesExactTextEditForTheCurrentPrefix()
    {
        using var process = StartServer();
        await Write(process, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"initialized","params":{}}""");
        const string source = "<template><Button onCl";
        await Write(process, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "textDocument/didOpen",
            @params = new { textDocument = new { uri = "file:///C:/Square/Edit.sqx", languageId = "sqx", version = 1, text = source } }
        }));
        _ = await Read(process.StandardOutput);
        await Write(process, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "textDocument/completion",
            @params = new { textDocument = new { uri = "file:///C:/Square/Edit.sqx" }, position = new { line = 0, character = source.Length } }
        }));

        using var response = JsonDocument.Parse(await Read(process.StandardOutput));
        var item = response.RootElement.GetProperty("result").GetProperty("items")
            .EnumerateArray()
            .Single(candidate => candidate.GetProperty("label").GetString() == "onClick");
        var textEdit = item.GetProperty("textEdit");

        Assert.Equal(source.IndexOf("onCl", StringComparison.Ordinal),
            textEdit.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(source.Length,
            textEdit.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32());
        Assert.Equal("onClick", textEdit.GetProperty("newText").GetString());

        await Write(process, """{"jsonrpc":"2.0","id":3,"method":"shutdown","params":null}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"exit","params":null}""");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task TagCompletionIndexesUnopenedComponentsFromTheWorkspaceRoot()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "square-lsp-completion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "Card.sqx"),
            "<template><View /></template>");
        var ignored = Path.Combine(workspace, "obj");
        Directory.CreateDirectory(ignored);
        await File.WriteAllTextAsync(
            Path.Combine(ignored, "Hidden.sqx"),
            "<template><View /></template>");
        using var process = StartServer();
        try
        {
            await Write(process, JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new { rootUri = new Uri(workspace + Path.DirectorySeparatorChar).AbsoluteUri }
            }));
            _ = await Read(process.StandardOutput);
            await Write(process, """{"jsonrpc":"2.0","method":"initialized","params":{}}""");
            var pageUri = new Uri(Path.Combine(workspace, "Page.sqx")).AbsoluteUri;
            const string page = "<template><";
            await Write(process, JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "textDocument/didOpen",
                @params = new { textDocument = new { uri = pageUri, languageId = "sqx", version = 1, text = page } }
            }));
            _ = await Read(process.StandardOutput);
            await Write(process, JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "textDocument/completion",
                @params = new { textDocument = new { uri = pageUri }, position = new { line = 0, character = page.Length } }
            }));

            var completion = await Read(process.StandardOutput);

            Assert.Contains("\"label\":\"Card\"", completion, StringComparison.Ordinal);
            Assert.DoesNotContain("\"label\":\"Hidden\"", completion, StringComparison.Ordinal);

            await Write(process, """{"jsonrpc":"2.0","id":3,"method":"shutdown","params":null}""");
            _ = await Read(process.StandardOutput);
            await Write(process, """{"jsonrpc":"2.0","method":"exit","params":null}""");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task DidCloseDoesNotIndexADocumentThatWasNeverOpened()
    {
        var directory = Path.Combine(Path.GetTempPath(), "square-lsp-close-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var secretPath = Path.Combine(directory, "Secret.sqx");
        await File.WriteAllTextAsync(secretPath, "<template><View /></template>");
        using var process = StartServer();
        try
        {
            await Write(process, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
            _ = await Read(process.StandardOutput);
            await Write(process, """{"jsonrpc":"2.0","method":"initialized","params":{}}""");
            await Write(process, JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "textDocument/didClose",
                @params = new { textDocument = new { uri = new Uri(secretPath).AbsoluteUri } }
            }));
            _ = await Read(process.StandardOutput);

            var pageUri = new Uri(Path.Combine(directory, "Page.sqx")).AbsoluteUri;
            const string page = "<template><";
            await Write(process, JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "textDocument/didOpen",
                @params = new { textDocument = new { uri = pageUri, languageId = "sqx", version = 1, text = page } }
            }));
            _ = await Read(process.StandardOutput);
            await Write(process, JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "textDocument/completion",
                @params = new { textDocument = new { uri = pageUri }, position = new { line = 0, character = page.Length } }
            }));

            var completion = await Read(process.StandardOutput);

            Assert.DoesNotContain("\"label\":\"Secret\"", completion, StringComparison.Ordinal);

            await Write(process, """{"jsonrpc":"2.0","id":3,"method":"shutdown","params":null}""");
            _ = await Read(process.StandardOutput);
            await Write(process, """{"jsonrpc":"2.0","method":"exit","params":null}""");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DidCloseDoesNotRestoreAnOpenedDocumentOutsideTheWorkspace()
    {
        var directory = Path.Combine(Path.GetTempPath(), "square-lsp-close-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var externalPath = Path.Combine(directory, "External.sqx");
        await File.WriteAllTextAsync(
            externalPath,
            "<template><View /></template><script>[Prop] public string SecretValue { get; set; }</script>");
        var externalUri = new Uri(externalPath).AbsoluteUri;
        using var process = StartServer();
        try
        {
            await Write(process, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
            _ = await Read(process.StandardOutput);
            await Write(process, """{"jsonrpc":"2.0","method":"initialized","params":{}}""");
            await Write(process, JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "textDocument/didOpen",
                @params = new { textDocument = new { uri = externalUri, languageId = "sqx", version = 1, text = "<template><View /></template>" } }
            }));
            _ = await Read(process.StandardOutput);
            await Write(process, JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "textDocument/didClose",
                @params = new { textDocument = new { uri = externalUri } }
            }));
            _ = await Read(process.StandardOutput);

            var pageUri = new Uri(Path.Combine(directory, "Page.sqx")).AbsoluteUri;
            const string page = "<template><";
            await Write(process, JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "textDocument/didOpen",
                @params = new { textDocument = new { uri = pageUri, languageId = "sqx", version = 1, text = page } }
            }));
            _ = await Read(process.StandardOutput);
            await Write(process, JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "textDocument/completion",
                @params = new { textDocument = new { uri = pageUri }, position = new { line = 0, character = page.Length } }
            }));

            var completion = await Read(process.StandardOutput);

            Assert.DoesNotContain("\"label\":\"External\"", completion, StringComparison.Ordinal);

            await Write(process, """{"jsonrpc":"2.0","id":3,"method":"shutdown","params":null}""");
            _ = await Read(process.StandardOutput);
            await Write(process, """{"jsonrpc":"2.0","method":"exit","params":null}""");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task OpenDocument(Process process, string uri, string languageId, string text)
    {
        await Write(process, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "textDocument/didOpen",
            @params = new
            {
                textDocument = new { uri, languageId, version = 1, text }
            }
        }));
        _ = await Read(process.StandardOutput);
    }

    private static void AssertCompletionItem(string response, string label, string detail)
    {
        using var document = JsonDocument.Parse(response);
        var item = document.RootElement.GetProperty("result").GetProperty("items")
            .EnumerateArray()
            .Single(candidate => candidate.GetProperty("label").GetString() == label);
        Assert.Equal(detail, item.GetProperty("detail").GetString());
    }

    private static Process StartServer()
    {
        var project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "Square.LanguageServer", "Square.LanguageServer.csproj"));
        var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                Arguments = "run --no-restore --project \"" + project + "\" --no-launch-profile",
                WorkingDirectory = Path.GetDirectoryName(project)!,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        Assert.True(process.Start());
        return process;
    }

    private static async Task Write(Process process, string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        await process.StandardInput.WriteAsync("Content-Length: " + payload.Length + "\r\n\r\n" + json);
        await process.StandardInput.FlushAsync();
    }

    private static async Task<string> Read(StreamReader reader)
    {
        var length = -1;
        while (true)
        {
            var header = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(header);
            if (header!.Length == 0) break;
            if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                length = int.Parse(header["Content-Length:".Length..].Trim());
        }
        Assert.True(length >= 0);
        var chars = new char[length];
        var offset = 0;
        while (offset < chars.Length)
        {
            var read = await reader.ReadAsync(chars.AsMemory(offset));
            Assert.True(read > 0);
            offset += read;
        }
        return new string(chars);
    }
}
