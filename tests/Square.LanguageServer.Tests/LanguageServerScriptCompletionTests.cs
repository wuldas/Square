using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Square.LanguageServer.Tests;

public sealed class LanguageServerScriptCompletionTests
{
    [Fact]
    public async Task CompletesUsingNamespacesAndScriptMembers()
    {
        using var process = StartServer();
        await Write(process, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        _ = await Read(process.StandardOutput);
        await Write(process, """{"jsonrpc":"2.0","method":"initialized","params":{}}""");

        const string uri = "file:///C:/Square/ScriptCompletion.sqx";
        const string usingText = "<template><View /></template><script>using Square.Con</script>";
        await Open(process, uri, usingText);
        await Complete(process, 2, uri, usingText.IndexOf("</script>", StringComparison.Ordinal));
        var namespaces = await Read(process.StandardOutput);
        Assert.Contains("\"label\":\"Square.Controls\"", namespaces, StringComparison.Ordinal);
        Assert.Contains("\"newText\":\"Square.Controls\"", namespaces, StringComparison.Ordinal);

        const string memberText = "<template><View /></template><script>private string Title = \"Square\"; private void Save(Event e) { e.St }</script>";
        await Change(process, uri, 2, memberText);
        await Complete(process, 3, uri, memberText.IndexOf("e.St", StringComparison.Ordinal) + "e.St".Length);
        var members = await Read(process.StandardOutput);
        Assert.Contains("\"label\":\"StopPropagation\"", members, StringComparison.Ordinal);
        Assert.Contains("\"newText\":\"StopPropagation()\"", members, StringComparison.Ordinal);
        Assert.DoesNotContain("\"label\":\"StartsWith\"", members, StringComparison.Ordinal);

        await Write(process, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 4,
            method = "textDocument/hover",
            @params = new
            {
                textDocument = new { uri },
                position = new
                {
                    line = 0,
                    character = memberText.IndexOf("Title", StringComparison.Ordinal) + 2
                }
            }
        }));
        var hover = await Read(process.StandardOutput);
        Assert.Contains("string Title", hover, StringComparison.Ordinal);
        Assert.Contains("csharp", hover, StringComparison.Ordinal);

        await Write(process, """{"jsonrpc":"2.0","id":5,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"file:///C:/Square/ScriptCompletion.sqx"}}}""");
        var symbols = await Read(process.StandardOutput);
        Assert.Contains("\"name\":\"Title\"", symbols, StringComparison.Ordinal);
        Assert.Contains("\"kind\":8", symbols, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"Save\"", symbols, StringComparison.Ordinal);
        Assert.Contains("\"kind\":6", symbols, StringComparison.Ordinal);

        const string attributeText = "<template><View /></template><script>[Prop(Re</script>";
        await Change(process, uri, 3, attributeText);
        await Complete(process, 6, uri, attributeText.IndexOf("</script>", StringComparison.Ordinal));
        var attributes = await Read(process.StandardOutput);
        Assert.Contains("\"label\":\"Required\"", attributes, StringComparison.Ordinal);
        Assert.Contains("\"newText\":\"Required\"", attributes, StringComparison.Ordinal);
        Assert.DoesNotContain("\"label\":\"Default\"", attributes, StringComparison.Ordinal);

        await Write(process, """{"jsonrpc":"2.0","id":7,"method":"shutdown","params":null}""");
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
