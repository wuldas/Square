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

        await Write(process, """{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqx","version":2},"contentChanges":[{"text":"<template><Button @"}]}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","id":3,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///C:/Square/Completion.sqx"},"position":{"line":0,"character":19}}}""");
        var events = await Read(process.StandardOutput);
        Assert.Contains("\"label\":\"click\"", events, StringComparison.Ordinal);
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

        await Write(process, """{"jsonrpc":"2.0","id":6,"method":"shutdown","params":null}""");
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
