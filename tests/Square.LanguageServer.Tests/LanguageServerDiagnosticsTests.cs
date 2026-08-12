using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Square.LanguageServer.Tests;

public sealed class LanguageServerDiagnosticsTests
{
    [Fact]
    public async Task DidOpenPublishesDiagnosticsAndDidCloseClearsThem()
    {
        using var process = StartServer();
        const string uri = "file:///C:/Square/Editing.sqx";

        await WriteMessageAsync(process, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        _ = await ReadMessageAsync(process.StandardOutput);
        await WriteMessageAsync(process, """{"jsonrpc":"2.0","method":"initialized","params":{}}""");

        await WriteMessageAsync(process, """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///C:/Square/Editing.sqx","languageId":"sqx","version":1,"text":"<template><View>"}}}""");
        var openedDiagnostics = await ReadMessageAsync(process.StandardOutput);
        using (var opened = JsonDocument.Parse(openedDiagnostics))
        {
            Assert.Equal("textDocument/publishDiagnostics", opened.RootElement.GetProperty("method").GetString());
            var parameters = opened.RootElement.GetProperty("params");
            Assert.Equal(uri, parameters.GetProperty("uri").GetString());
            Assert.NotEmpty(parameters.GetProperty("diagnostics").EnumerateArray());
            var diagnostic = parameters.GetProperty("diagnostics")[0];
            Assert.Equal("SQX0001", diagnostic.GetProperty("code").GetString());
            Assert.Equal(0, diagnostic.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        }

        await WriteMessageAsync(process, """{"jsonrpc":"2.0","method":"textDocument/didClose","params":{"textDocument":{"uri":"file:///C:/Square/Editing.sqx"}}}""");
        var closedDiagnostics = await ReadMessageAsync(process.StandardOutput);
        using (var closed = JsonDocument.Parse(closedDiagnostics))
        {
            Assert.Equal("textDocument/publishDiagnostics", closed.RootElement.GetProperty("method").GetString());
            Assert.Empty(closed.RootElement.GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        }

        await WriteMessageAsync(process, """{"jsonrpc":"2.0","id":2,"method":"shutdown","params":null}""");
        _ = await ReadMessageAsync(process.StandardOutput);
        await WriteMessageAsync(process, """{"jsonrpc":"2.0","method":"exit","params":null}""");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
    }

    private static Process StartServer()
    {
        var project = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "tools", "Square.LanguageServer", "Square.LanguageServer.csproj"));
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

    private static async Task WriteMessageAsync(Process process, string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        await process.StandardInput.WriteAsync("Content-Length: " + payload.Length + "\r\n\r\n" + json);
        await process.StandardInput.FlushAsync();
    }

    private static async Task<string> ReadMessageAsync(StreamReader reader)
    {
        var contentLength = -1;
        while (true)
        {
            var header = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(header);
            if (header!.Length == 0) break;
            if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                contentLength = int.Parse(header.Substring("Content-Length:".Length).Trim());
        }

        Assert.True(contentLength >= 0);
        var buffer = new char[contentLength];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(read, buffer.Length - read));
            Assert.True(count > 0);
            read += count;
        }
        return new string(buffer);
    }
}
