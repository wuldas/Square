using System.Diagnostics;
using System.Text;
using Xunit;

namespace Square.LanguageServer.Tests;

public sealed class LanguageServerPropertyCompletionTests
{
    [Fact]
    public async Task CompletionOffersCatalogPropertiesInsideAnElement()
    {
        using var process = StartServer();
        await Write(process, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"initialized","params":{}}""");
        await Write(process, """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///C:/Square/Properties.sqx","languageId":"sqx","version":1,"text":"<template><Button te"}}}""");
        _ = await Read(process.StandardOutput);

        await Write(process, """{"jsonrpc":"2.0","id":2,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///C:/Square/Properties.sqx"},"position":{"line":0,"character":20}}}""");
        var properties = await Read(process.StandardOutput);
        Assert.Contains("\"label\":\"text\"", properties, StringComparison.Ordinal);
        Assert.Contains("TextContent", properties, StringComparison.Ordinal);
        Assert.Contains("\"kind\":10", properties, StringComparison.Ordinal);

        await Write(process, """{"jsonrpc":"2.0","id":3,"method":"shutdown","params":null}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"exit","params":null}""");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task CompletionOffersEmbeddedPropsFromAnOpenComponentDocument()
    {
        using var process = StartServer();
        await Write(process, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"initialized","params":{}}""");
        const string card = "<template><View /></template><script>[Prop(Required = true)] public ObservableValue<string> Value { get; set; } = new(\"\");</script>";
        await Write(process, System.Text.Json.JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "textDocument/didOpen",
            @params = new { textDocument = new { uri = "file:///C:/Square/Card.sqx", languageId = "sqx", version = 1, text = card } }
        }));
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///C:/Square/Page.sqx","languageId":"sqx","version":1,"text":"<template><Card Va"}}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","id":2,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///C:/Square/Page.sqx"},"position":{"line":0,"character":18}}}""");

        var completion = await Read(process.StandardOutput);

        Assert.Contains("\"label\":\"Value\"", completion, StringComparison.Ordinal);
        Assert.Contains("ObservableValue\\u003Cstring\\u003E (required)", completion, StringComparison.Ordinal);
        Assert.Contains("\"kind\":10", completion, StringComparison.Ordinal);

        await Write(process, """{"jsonrpc":"2.0","id":3,"method":"shutdown","params":null}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"exit","params":null}""");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task SqvBindingCompletionOffersCustomPropsWithAnExactTextEdit()
    {
        using var process = StartServer();
        await Write(process, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"initialized","params":{}}""");
        const string card = "<template><View /></template><script>[Prop] public ObservableValue<string> Value { get; set; } = new(\"\");</script>";
        await Write(process, System.Text.Json.JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "textDocument/didOpen",
            @params = new { textDocument = new { uri = "file:///C:/Square/Card.sqv", languageId = "sqv", version = 1, text = card } }
        }));
        _ = await Read(process.StandardOutput);
        const string page = "<template><Card :Va";
        await Write(process, System.Text.Json.JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "textDocument/didOpen",
            @params = new { textDocument = new { uri = "file:///C:/Square/Page.sqv", languageId = "sqv", version = 1, text = page } }
        }));
        _ = await Read(process.StandardOutput);
        await Write(process, System.Text.Json.JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "textDocument/completion",
            @params = new { textDocument = new { uri = "file:///C:/Square/Page.sqv" }, position = new { line = 0, character = page.Length } }
        }));

        using var response = System.Text.Json.JsonDocument.Parse(await Read(process.StandardOutput));
        var item = response.RootElement.GetProperty("result").GetProperty("items")
            .EnumerateArray()
            .Single(candidate => candidate.GetProperty("label").GetString() == "Value");
        var edit = item.GetProperty("textEdit");

        Assert.Equal(page.IndexOf("Va", StringComparison.Ordinal),
            edit.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(page.Length,
            edit.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32());
        Assert.Equal("Value", edit.GetProperty("newText").GetString());

        const string blankPage = "<template><Card  />";
        await Write(process, System.Text.Json.JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "textDocument/didChange",
            @params = new
            {
                textDocument = new { uri = "file:///C:/Square/Page.sqv", version = 2 },
                contentChanges = new[] { new { text = blankPage } }
            }
        }));
        _ = await Read(process.StandardOutput);
        var blankOffset = blankPage.IndexOf("  />", StringComparison.Ordinal) + 1;
        await Write(process, System.Text.Json.JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "textDocument/completion",
            @params = new { textDocument = new { uri = "file:///C:/Square/Page.sqv" }, position = new { line = 0, character = blankOffset } }
        }));
        using var blankResponse = System.Text.Json.JsonDocument.Parse(await Read(process.StandardOutput));
        var labels = blankResponse.RootElement.GetProperty("result").GetProperty("items")
            .EnumerateArray()
            .Select(candidate => candidate.GetProperty("label").GetString())
            .ToArray();
        Assert.Contains("Value", labels);
        Assert.Contains(":Value", labels);

        await Write(process, """{"jsonrpc":"2.0","id":4,"method":"shutdown","params":null}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"exit","params":null}""");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
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
