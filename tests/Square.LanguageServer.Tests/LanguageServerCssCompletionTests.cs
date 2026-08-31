using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Square.LanguageServer.Tests;

public sealed class LanguageServerCssCompletionTests
{
    [Fact]
    public async Task CompletesCssPropertiesValuesAndSelectors()
    {
        using var process = StartServer();
        await Write(process, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"initialized","params":{}}""");

        const string uri = "file:///C:/Square/CssCompletion.sqx";
        const string propertyText = "<template><View class=\"panel\" /></template><style>.panel { flex-di }</style>";
        await Open(process, uri, propertyText);
        await Complete(process, 2, uri, propertyText.IndexOf("flex-di", StringComparison.Ordinal) + "flex-di".Length);
        var properties = await Read(process.StandardOutput);
        Assert.Contains("\"label\":\"flex-direction\"", properties, StringComparison.Ordinal);
        Assert.Contains("\"newText\":\"flex-direction\"", properties, StringComparison.Ordinal);

        const string valueText = "<template><View class=\"panel\" /></template><style>.panel { display: fl }</style>";
        await Change(process, uri, 2, valueText);
        await Complete(process, 3, uri, valueText.IndexOf("fl }", StringComparison.Ordinal) + "fl".Length);
        var values = await Read(process.StandardOutput);
        Assert.Contains("\"label\":\"flex\"", values, StringComparison.Ordinal);
        Assert.DoesNotContain("\"label\":\"column\"", values, StringComparison.Ordinal);

        const string selectorText = "<template><View class=\"panel\" /></template><style>.pa</style>";
        await Change(process, uri, 3, selectorText);
        await Complete(process, 4, uri, selectorText.IndexOf(".pa", StringComparison.Ordinal) + ".pa".Length);
        var selectors = await Read(process.StandardOutput);
        Assert.Contains("\"label\":\".panel\"", selectors, StringComparison.Ordinal);

        await Write(process, """{"jsonrpc":"2.0","id":5,"method":"shutdown","params":null}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"exit","params":null}""");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
    }

    private static async Task Open(Process process, string uri, string text)
    {
        await Write(process, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "textDocument/didOpen",
            @params = new { textDocument = new { uri, languageId = "sqx", version = 1, text } }
        }));
        _ = await Read(process.StandardOutput);
    }

    private static async Task Change(Process process, string uri, int version, string text)
    {
        await Write(process, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "textDocument/didChange",
            @params = new { textDocument = new { uri, version }, contentChanges = new[] { new { text } } }
        }));
        _ = await Read(process.StandardOutput);
    }

    private static Task Complete(Process process, int id, string uri, int character) =>
        Write(process, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            method = "textDocument/completion",
            @params = new { textDocument = new { uri }, position = new { line = 0, character } }
        }));

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
