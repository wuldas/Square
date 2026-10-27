using System.Text.Json;
using Square.Hosting;
using Xunit;

namespace Square.DevTools.Tests;

public sealed class CdpProtocolTests
{
    [Fact]
    public async Task AdvertisedProtocolHasResolvableTypesAndRuntimeDomains()
    {
        await using var server = DevToolsServer.Start(new AppWindow("protocol"), new() { AllowChromeInspect = true });
        using var http = new HttpClient { BaseAddress = new Uri(server.BaseAddress) };
        using var protocol = JsonDocument.Parse(await http.GetStringAsync("/json/protocol"));
        var domains = protocol.RootElement.GetProperty("domains").EnumerateArray().ToArray();
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var domain in domains)
        {
            if (!domain.TryGetProperty("types", out var types)) continue;
            foreach (var type in types.EnumerateArray())
                declared.Add(domain.GetProperty("domain").GetString() + "." + type.GetProperty("id").GetString());
        }
        foreach (var domain in domains)
            ValidateReferences(domain, domain.GetProperty("domain").GetString()!, declared);
        using var connection = new CdpConnection();
        await connection.ConnectAsync(server);
        using var response = await connection.RequestAsync("Schema.getDomains");
        var runtimeDomains = response.RootElement.GetProperty("result").GetProperty("domains").EnumerateArray().ToArray();
        Assert.Equal(domains.Select(domain => domain.GetProperty("domain").GetString()),
            runtimeDomains.Select(domain => domain.GetProperty("name").GetString()));
        Assert.All(runtimeDomains, domain => Assert.Equal("1.3", domain.GetProperty("version").GetString()));
        Assert.DoesNotContain(runtimeDomains, domain => domain.GetProperty("name").GetString() == "Debugger");
        Assert.DoesNotContain(runtimeDomains, domain => domain.GetProperty("name").GetString() == "Memory");
    }

    private static void ValidateReferences(JsonElement node, string domain, HashSet<string> declared)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("$ref", out var reference))
            {
                var name = reference.GetString()!;
                Assert.Contains(name.Contains('.') ? name : domain + "." + name, declared);
            }
            foreach (var property in node.EnumerateObject()) ValidateReferences(property.Value, domain, declared);
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var child in node.EnumerateArray()) ValidateReferences(child, domain, declared);
    }
}
