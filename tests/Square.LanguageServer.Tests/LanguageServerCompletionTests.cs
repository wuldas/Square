using System.Text.Json;
using Xunit;

namespace Square.LanguageServer.Tests;

public sealed class LanguageServerCompletionTests
{
    [Fact]
    public async Task CompletionOffersCatalogTagsAndEvents()
    {
        using var session = StartServer();
        await session.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        await session.SendAsync("""{"jsonrpc":"2.0","method":"initialized","params":{}}""");

        await session.SendAsync("""{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqx","languageId":"sqx","version":1,"text":"<template><Bu"}}}""");
        await session.SendAsync("""{"jsonrpc":"2.0","id":2,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqx"},"position":{"line":0,"character":13}}}""");
        var tags = await session.ReadResponseAsync();
        Assert.Contains("\"label\":\"Button\"", tags, StringComparison.Ordinal);

        await session.SendAsync("""{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqx","version":2},"contentChanges":[{"text":"<template><Button on"}]}}""");
        await session.SendAsync("""{"jsonrpc":"2.0","id":3,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqx"},"position":{"line":0,"character":20}}}""");
        var events = await session.ReadResponseAsync();
        Assert.Contains("\"label\":\"onClick\"", events, StringComparison.Ordinal);
        Assert.Contains("\"kind\":23", events, StringComparison.Ordinal);

        await session.SendAsync("""{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqx","version":3},"contentChanges":[{"text":"<template><Sh"}]}}""");
        await session.SendAsync("""{"jsonrpc":"2.0","id":4,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqx"},"position":{"line":0,"character":13}}}""");
        var controlFlow = await session.ReadResponseAsync();
        Assert.Contains("\"label\":\"Show\"", controlFlow, StringComparison.Ordinal);

        await session.SendAsync("""{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqv","languageId":"sqv","version":1,"text":"<template><Text v-"}}}""");
        await session.SendAsync("""{"jsonrpc":"2.0","id":5,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqv"},"position":{"line":0,"character":18}}}""");
        var directives = await session.ReadResponseAsync();
        Assert.Contains("\"label\":\"v-if\"", directives, StringComparison.Ordinal);
        Assert.Contains("Vue directive", directives, StringComparison.Ordinal);

        await session.SendAsync("""{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqv","version":2},"contentChanges":[{"text":"<template><Button @"}]}}""");
        await session.SendAsync("""{"jsonrpc":"2.0","id":6,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqv"},"position":{"line":0,"character":19}}}""");
        var vueEvents = await session.ReadResponseAsync();
        Assert.Contains("\"label\":\"click\"", vueEvents, StringComparison.Ordinal);

        Assert.Equal(0, await session.ShutdownAsync());
    }

    [Fact]
    public async Task CompletionOffersWorkspaceComponentEventsForBothDialects()
    {
        using var session = StartServer();
        await session.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        await session.SendAsync("""{"jsonrpc":"2.0","method":"initialized","params":{}}""");

        const string card = "<template><View /></template><script>public static readonly ComponentEvent<int> ItemSelectedEvent = new(\"item-selected\");</script>";
        await OpenDocument(session, "file:///C:/Square/Card.sqx", "sqx", card);
        const string sqxUsage = "<template><Card on";
        await OpenDocument(session, "file:///C:/Square/Page.sqx", "sqx", sqxUsage);
        await session.SendAsync(JsonSerializer.Serialize(new
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
        var sqxCompletion = await session.ReadResponseAsync();
        AssertCompletionItem(sqxCompletion, "onItemSelected", "CustomEvent<int>");

        const string sqxWithExistingEvent = "<template><Card onItemSelected={OnSelected} on";
        await session.SendAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "textDocument/didChange",
            @params = new
            {
                textDocument = new { uri = "file:///C:/Square/Page.sqx", version = 2 },
                contentChanges = new[] { new { text = sqxWithExistingEvent } }
            }
        }));
        await session.SendAsync(JsonSerializer.Serialize(new
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
        var sqxDeduplicated = await session.ReadResponseAsync();
        Assert.DoesNotContain("\"label\":\"onItemSelected\"", sqxDeduplicated, StringComparison.Ordinal);

        const string handlerPage = "<template><Card onItemSelected={On} /></template><script>private void OnTyped(CustomEvent<int> e) { } private void OnWrong(CustomEvent<string> e) { }</script>";
        await OpenDocument(session, "file:///C:/Square/HandlerPage.sqx", "sqx", handlerPage);
        var handlerOffset = handlerPage.IndexOf("{On}", StringComparison.Ordinal) + "{On".Length;
        await session.SendAsync(JsonSerializer.Serialize(new
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
        var handlerCompletion = await session.ReadResponseAsync();
        Assert.Contains("\"label\":\"OnTyped\"", handlerCompletion, StringComparison.Ordinal);
        Assert.DoesNotContain("\"label\":\"OnWrong\"", handlerCompletion, StringComparison.Ordinal);

        await OpenDocument(session, "file:///C:/Square/VueCard.sqv", "sqv", card);
        const string sqvUsage = "<template><VueCard @";
        await OpenDocument(session, "file:///C:/Square/Page.sqv", "sqv", sqvUsage);
        await session.SendAsync(JsonSerializer.Serialize(new
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
        var sqvCompletion = await session.ReadResponseAsync();
        AssertCompletionItem(sqvCompletion, "item-selected", "CustomEvent<int>");

        Assert.Equal(0, await session.ShutdownAsync());
    }

    [Fact]
    public async Task EventExpressionCompletionOffersCurrentScriptMethods()
    {
        using var session = StartServer();
        await session.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        await session.SendAsync("""{"jsonrpc":"2.0","method":"initialized","params":{}}""");
        const string source = "<template><Button onClick={OnS} /></template><script>private Event? OnState { get; set; } private void OnSave(Event e) { }</script>";
        await session.SendAsync(JsonSerializer.Serialize(new
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
        var character = source.IndexOf("OnS}", StringComparison.Ordinal) + "OnS".Length;
        await session.SendAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"textDocument/completion\",\"params\":{\"textDocument\":{\"uri\":\"file:///C:/Square/Handlers.sqx\"},\"position\":{\"line\":0,\"character\":" + character + "}}}");

        var completion = await session.ReadResponseAsync();

        Assert.Contains("\"label\":\"OnSave\"", completion, StringComparison.Ordinal);
        Assert.Contains("\"kind\":3", completion, StringComparison.Ordinal);
        Assert.DoesNotContain("\"label\":\"OnState\"", completion, StringComparison.Ordinal);

        Assert.Equal(0, await session.ShutdownAsync());
    }

    [Fact]
    public async Task TagCompletionOffersComponentsFromOtherOpenDocuments()
    {
        using var session = StartServer();
        await session.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        await session.SendAsync("""{"jsonrpc":"2.0","method":"initialized","params":{}}""");

        await session.SendAsync("""{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///C:/Square/Card.sqx","languageId":"sqx","version":1,"text":"<template><View /></template>"}}}""");
        await session.SendAsync("""{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///C:/Square/Page.sqx","languageId":"sqx","version":1,"text":"<template><Ca"}}}""");
        await session.SendAsync("""{"jsonrpc":"2.0","id":2,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///C:/Square/Page.sqx"},"position":{"line":0,"character":13}}}""");

        var completion = await session.ReadResponseAsync();

        Assert.Contains("\"label\":\"Card\"", completion, StringComparison.Ordinal);
        Assert.Contains("\"detail\":\"Card\"", completion, StringComparison.Ordinal);

        await session.SendAsync("""{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///C:/Square/Card.sqx","version":2},"contentChanges":[{"text":"<template><"}]}}""");
        await session.SendAsync("""{"jsonrpc":"2.0","id":3,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///C:/Square/Page.sqx"},"position":{"line":0,"character":13}}}""");
        var completionDuringInvalidEdit = await session.ReadResponseAsync();
        Assert.Contains("\"label\":\"Card\"", completionDuringInvalidEdit, StringComparison.Ordinal);

        Assert.Equal(0, await session.ShutdownAsync());
    }

    [Fact]
    public async Task CompletionUsesExactTextEditForTheCurrentPrefix()
    {
        using var session = StartServer();
        await session.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        await session.SendAsync("""{"jsonrpc":"2.0","method":"initialized","params":{}}""");
        const string source = "<template><Button onCl";
        await session.SendAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "textDocument/didOpen",
            @params = new { textDocument = new { uri = "file:///C:/Square/Edit.sqx", languageId = "sqx", version = 1, text = source } }
        }));
        await session.SendAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "textDocument/completion",
            @params = new { textDocument = new { uri = "file:///C:/Square/Edit.sqx" }, position = new { line = 0, character = source.Length } }
        }));

        using var response = JsonDocument.Parse(await session.ReadResponseAsync());
        var item = response.RootElement.GetProperty("result").GetProperty("items")
            .EnumerateArray()
            .Single(candidate => candidate.GetProperty("label").GetString() == "onClick");
        var textEdit = item.GetProperty("textEdit");

        Assert.Equal(source.IndexOf("onCl", StringComparison.Ordinal),
            textEdit.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(source.Length,
            textEdit.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32());
        Assert.Equal("onClick", textEdit.GetProperty("newText").GetString());

        Assert.Equal(0, await session.ShutdownAsync());
    }

    [Fact]
    public async Task OutstandingRequestsAreAnsweredByIdWhileEditsKeepFlowing()
    {
        using var session = StartServer();
        await session.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        await session.SendAsync("""{"jsonrpc":"2.0","method":"initialized","params":{}}""");

        const string uri = "file:///C:/Square/Flow.sqx";
        const string source = "<template><Bu";
        await OpenDocument(session, uri, "sqx", source);

        // Two requests are left outstanding; the message loop must keep accepting edits.
        await session.SendAsync("""{"jsonrpc":"2.0","id":10,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///C:/Square/Flow.sqx"},"position":{"line":0,"character":13}}}""");
        await session.SendAsync("""{"jsonrpc":"2.0","id":11,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///C:/Square/Flow.sqx"},"position":{"line":0,"character":13}}}""");

        const string broken = "<template><Button></Frag></template>";
        await session.SendAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "textDocument/didChange",
            @params = new
            {
                textDocument = new { uri, version = 2 },
                contentChanges = new[] { new { text = broken } }
            }
        }));

        var diagnostics = await session.ReadNotificationAsync("textDocument/publishDiagnostics", uri);
        using (var document = JsonDocument.Parse(diagnostics))
        {
            var parameters = document.RootElement.GetProperty("params");
            Assert.Equal(2, parameters.GetProperty("version").GetInt32());
            Assert.NotEmpty(parameters.GetProperty("diagnostics").EnumerateArray().ToArray());
        }

        Assert.Contains("\"label\":\"Button\"", await session.ReadResponseAsync(10), StringComparison.Ordinal);
        Assert.Contains("\"label\":\"Button\"", await session.ReadResponseAsync(11), StringComparison.Ordinal);

        Assert.Equal(0, await session.ShutdownAsync());
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
        using var session = StartServer();
        try
        {
            await session.SendAsync(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new { rootUri = new Uri(workspace + Path.DirectorySeparatorChar).AbsoluteUri }
            }));
            await session.SendAsync("""{"jsonrpc":"2.0","method":"initialized","params":{}}""");
            var pageUri = new Uri(Path.Combine(workspace, "Page.sqx")).AbsoluteUri;
            const string page = "<template><";
            await session.SendAsync(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "textDocument/didOpen",
                @params = new { textDocument = new { uri = pageUri, languageId = "sqx", version = 1, text = page } }
            }));
            await session.SendAsync(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "textDocument/completion",
                @params = new { textDocument = new { uri = pageUri }, position = new { line = 0, character = page.Length } }
            }));

            var completion = await session.ReadResponseAsync();

            Assert.Contains("\"label\":\"Card\"", completion, StringComparison.Ordinal);
            Assert.DoesNotContain("\"label\":\"Hidden\"", completion, StringComparison.Ordinal);

            Assert.Equal(0, await session.ShutdownAsync());
        }
        finally
        {
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
        using var session = StartServer();
        try
        {
            await session.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
            await session.SendAsync("""{"jsonrpc":"2.0","method":"initialized","params":{}}""");
            await session.SendAsync(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "textDocument/didClose",
                @params = new { textDocument = new { uri = new Uri(secretPath).AbsoluteUri } }
            }));

            var pageUri = new Uri(Path.Combine(directory, "Page.sqx")).AbsoluteUri;
            const string page = "<template><";
            await session.SendAsync(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "textDocument/didOpen",
                @params = new { textDocument = new { uri = pageUri, languageId = "sqx", version = 1, text = page } }
            }));
            await session.SendAsync(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "textDocument/completion",
                @params = new { textDocument = new { uri = pageUri }, position = new { line = 0, character = page.Length } }
            }));

            var completion = await session.ReadResponseAsync();

            Assert.DoesNotContain("\"label\":\"Secret\"", completion, StringComparison.Ordinal);

            Assert.Equal(0, await session.ShutdownAsync());
        }
        finally
        {
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
        using var session = StartServer();
        try
        {
            await session.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
            await session.SendAsync("""{"jsonrpc":"2.0","method":"initialized","params":{}}""");
            await session.SendAsync(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "textDocument/didOpen",
                @params = new { textDocument = new { uri = externalUri, languageId = "sqx", version = 1, text = "<template><View /></template>" } }
            }));
            await session.SendAsync(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "textDocument/didClose",
                @params = new { textDocument = new { uri = externalUri } }
            }));

            var pageUri = new Uri(Path.Combine(directory, "Page.sqx")).AbsoluteUri;
            const string page = "<template><";
            await session.SendAsync(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "textDocument/didOpen",
                @params = new { textDocument = new { uri = pageUri, languageId = "sqx", version = 1, text = page } }
            }));
            await session.SendAsync(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "textDocument/completion",
                @params = new { textDocument = new { uri = pageUri }, position = new { line = 0, character = page.Length } }
            }));

            var completion = await session.ReadResponseAsync();

            Assert.DoesNotContain("\"label\":\"External\"", completion, StringComparison.Ordinal);

            Assert.Equal(0, await session.ShutdownAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CompletionUsesOwningProjectReferencesForQualifiedTags()
    {
        using var session = StartServer();
        var projectDirectory = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "Square.Sample.Vue"));
        var documentPath = Path.Combine(projectDirectory, "Components", "MarkdownSamplesPage.sqv");
        var rootUri = new Uri(projectDirectory + Path.DirectorySeparatorChar).AbsoluteUri;
        var documentUri = new Uri(documentPath).AbsoluteUri;
        await session.SendAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new { rootUri }
        }));
        await session.SendAsync("""{"jsonrpc":"2.0","method":"initialized","params":{}}""");

        const string source = "<template><markdown:";
        await OpenDocument(session, documentUri, "sqv", source);
        await session.SendAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "textDocument/completion",
            @params = new
            {
                textDocument = new { uri = documentUri },
                position = new { line = 0, character = source.Length }
            }
        }));
        var completion = await session.ReadResponseAsync();

        Assert.Contains("\"label\":\"markdown:MarkdownViewer\"", completion, StringComparison.Ordinal);

        const string propertySource = "<template><markdown:MarkdownViewer Co";
        await session.SendAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "textDocument/didChange",
            @params = new
            {
                textDocument = new { uri = documentUri, version = 2 },
                contentChanges = new[] { new { text = propertySource } }
            }
        }));
        await session.SendAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "textDocument/completion",
            @params = new
            {
                textDocument = new { uri = documentUri },
                position = new { line = 0, character = propertySource.Length }
            }
        }));
        var propertyCompletion = await session.ReadResponseAsync();
        Assert.True(propertyCompletion.Contains("\"label\":\"Content\"", StringComparison.Ordinal), propertyCompletion);

        Assert.Equal(0, await session.ShutdownAsync());
    }

    [Fact]
    public async Task SqvAttributeModeCompletionInsertsAtPrefixedProjectEvents()
    {
        using var session = StartServer();
        await session.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        await session.SendAsync("""{"jsonrpc":"2.0","method":"initialized","params":{}}""");

        const string card = "<template><View /></template><script>public static readonly ComponentEvent<int> ItemSelectedEvent = new(\"item-selected\");</script>";
        await OpenDocument(session, "file:///C:/Square/ExtraCard.sqv", "sqv", card);
        const string usage = "<template><ExtraCard ";
        await OpenDocument(session, "file:///C:/Square/ExtraPage.sqv", "sqv", usage);
        await session.SendAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "textDocument/completion",
            @params = new
            {
                textDocument = new { uri = "file:///C:/Square/ExtraPage.sqv" },
                position = new { line = 0, character = usage.Length }
            }
        }));
        var completion = await session.ReadResponseAsync();

        Assert.Contains("\"label\":\"@item-selected\"", completion, StringComparison.Ordinal);
        Assert.Equal(0, await session.ShutdownAsync());
    }

    [Fact]
    public async Task DefinitionReturnsLocationLinkForAnUnopenedProjectComponent()
    {
        using var session = StartServer();
        var projectDirectory = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "Square.Sample.Vue"));
        var rootUri = new Uri(projectDirectory + Path.DirectorySeparatorChar).AbsoluteUri;
        await session.SendAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new { rootUri }
        }));
        await session.SendAsync("""{"jsonrpc":"2.0","method":"initialized","params":{}}""");

        var documentUri = new Uri(Path.Combine(projectDirectory, "Components", "Main.sqv")).AbsoluteUri;
        const string source = "<template><SlotCard /></template>";
        await OpenDocument(session, documentUri, "sqv", source);
        var offset = source.IndexOf("SlotCard", StringComparison.Ordinal) + 3;
        await session.SendAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "textDocument/definition",
            @params = new
            {
                textDocument = new { uri = documentUri },
                position = new { line = 0, character = offset }
            }
        }));

        var definition = await session.ReadResponseAsync();
        using var document = JsonDocument.Parse(definition);
        var link = Assert.Single(document.RootElement.GetProperty("result").EnumerateArray());

        Assert.EndsWith("SlotCard.sqv", link.GetProperty("targetUri").GetString()!, StringComparison.OrdinalIgnoreCase);
        var selection = link.GetProperty("targetSelectionRange");
        Assert.Equal(1, selection.GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(9, selection.GetProperty("end").GetProperty("character").GetInt32());
        Assert.True(link.GetProperty("targetRange").GetProperty("end").GetProperty("line").GetInt32() > 0);

        Assert.Equal(0, await session.ShutdownAsync());
    }

    private static async Task OpenDocument(LanguageServerSession session, string uri, string languageId, string text)
    {
        await session.SendAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "textDocument/didOpen",
            @params = new
            {
                textDocument = new { uri, languageId, version = 1, text }
            }
        }));
    }

    private static void AssertCompletionItem(string response, string label, string detail)
    {
        using var document = JsonDocument.Parse(response);
        var item = document.RootElement.GetProperty("result").GetProperty("items")
            .EnumerateArray()
            .Single(candidate => candidate.GetProperty("label").GetString() == label);
        Assert.Equal(detail, item.GetProperty("detail").GetString());
    }

    private static LanguageServerSession StartServer() => LanguageServerSession.Start();
}
