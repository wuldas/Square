using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Square.LanguageServer.Tests;

public sealed class LanguageServerCssClassCompletionTests
{
    [Fact]
    public async Task CompletionOffersClassesDeclaredInTheCurrentStyleSection()
    {
        using var process = StartServer();
        await Write(process, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"initialized","params":{}}""");

        const string text = "<template><View class=\"split" + " /></template>\n<style>\n.panel-left { color: red; }\n.splitter-page { display: flex; }\n</style>";
        await Write(process, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "textDocument/didOpen",
            @params = new
            {
                textDocument = new
                {
                    uri = "file:///C:/Square/CssClasses.sqx",
                    languageId = "sqx",
                    version = 1,
                    text
                }
            }
        }));
        _ = await Read(process.StandardOutput);

        var position = text.IndexOf("split", StringComparison.Ordinal) + "split".Length;
        await Write(process, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "textDocument/completion",
            @params = new
            {
                textDocument = new { uri = "file:///C:/Square/CssClasses.sqx" },
                position = new { line = 0, character = position }
            }
        }));
        var completion = await Read(process.StandardOutput);
        Assert.Contains("\"label\":\"splitter-page\"", completion, StringComparison.Ordinal);
        Assert.Contains("\"kind\":12", completion, StringComparison.Ordinal);
        Assert.Contains("CSS class", completion, StringComparison.Ordinal);

        await Write(process, """{"jsonrpc":"2.0","id":3,"method":"shutdown","params":null}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"exit","params":null}""");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task CompletionFiltersOnlyTheCurrentClassToken()
    {
        using var process = StartServer();
        await Write(process, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"initialized","params":{}}""");

        const string text = "<template><View class=\"panel-left split\" /></template>\n<style>.panel-left {} .splitter-page {} .sample-page {}</style>";
        await Write(process, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "textDocument/didOpen",
            @params = new { textDocument = new { uri = "file:///C:/Square/Filter.sqx", languageId = "sqx", version = 1, text } }
        }));
        _ = await Read(process.StandardOutput);

        var position = text.IndexOf("split\"", StringComparison.Ordinal) + "split".Length;
        await Write(process, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "textDocument/completion",
            @params = new { textDocument = new { uri = "file:///C:/Square/Filter.sqx" }, position = new { line = 0, character = position } }
        }));
        var completion = await Read(process.StandardOutput);
        Assert.Contains("\"label\":\"splitter-page\"", completion, StringComparison.Ordinal);
        Assert.DoesNotContain("\"label\":\"panel-left\"", completion, StringComparison.Ordinal);

        await Write(process, """{"jsonrpc":"2.0","id":3,"method":"shutdown","params":null}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"exit","params":null}""");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task CompletionDoesNotInventClassesWithoutAStyleSection()
    {
        using var process = StartServer();
        await Write(process, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"initialized","params":{}}""");

        const string text = "<template><View class=\"panel\" /></template>";
        await Write(process, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "textDocument/didOpen",
            @params = new { textDocument = new { uri = "file:///C:/Square/NoStyle.sqx", languageId = "sqx", version = 1, text } }
        }));
        _ = await Read(process.StandardOutput);

        var position = text.IndexOf("panel\"", StringComparison.Ordinal) + "panel".Length;
        await Write(process, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "textDocument/completion",
            @params = new { textDocument = new { uri = "file:///C:/Square/NoStyle.sqx" }, position = new { line = 0, character = position } }
        }));
        var completion = await Read(process.StandardOutput);
        Assert.Contains("\"items\":[]", completion, StringComparison.Ordinal);

        await Write(process, """{"jsonrpc":"2.0","id":3,"method":"shutdown","params":null}""");
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
