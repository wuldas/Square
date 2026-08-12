using System.Diagnostics;
using System.Text;
using Xunit;

namespace Square.LanguageServer.Tests;

public sealed class LanguageServerLifecycleTests
{
    [Fact]
    public async Task InitializeShutdownExitUsesLspFramingAndCleanlyExits()
    {
        var project = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "tools", "Square.LanguageServer", "Square.LanguageServer.csproj"));
        using var process = new Process
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

        await WriteMessageAsync(process, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        var initialize = await ReadMessageAsync(process.StandardOutput);
        Assert.Contains("\"id\":1", initialize, StringComparison.Ordinal);
        Assert.Contains("\"capabilities\"", initialize, StringComparison.Ordinal);

        await WriteMessageAsync(process, """{"jsonrpc":"2.0","method":"initialized","params":{}}""");
        await WriteMessageAsync(process, """{"jsonrpc":"2.0","id":2,"method":"shutdown","params":null}""");
        var shutdown = await ReadMessageAsync(process.StandardOutput);
        Assert.Contains("\"id\":2", shutdown, StringComparison.Ordinal);
        Assert.Contains("\"result\":null", shutdown, StringComparison.Ordinal);

        await WriteMessageAsync(process, """{"jsonrpc":"2.0","method":"exit","params":null}""");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
        Assert.DoesNotContain("\"jsonrpc\"", await process.StandardError.ReadToEndAsync(), StringComparison.Ordinal);
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
