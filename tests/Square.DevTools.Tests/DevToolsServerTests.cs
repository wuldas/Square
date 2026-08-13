using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Square.DevTools;
using Square.Graphics;
using Square.Hosting;
using Square.Runtime;
using Xunit;

namespace Square.DevTools.Tests;

public sealed class DevToolsServerTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void StartRejectsInvalidPorts(int port)
    {
        var window = CreateWindow();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DevToolsServer.Start(window, new DevToolsOptions { Port = port }));
    }

    [Fact]
    public async Task HealthRequiresTokenAndReportsSecureDefaults()
    {
        var window = CreateWindow();
        await using var server = DevToolsServer.Start(window);
        using var client = new HttpClient { BaseAddress = new Uri(server.BaseAddress) };

        using var unauthorized = await client.GetAsync("/api/v1/health");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        client.DefaultRequestHeaders.Add(DevToolsServer.TokenHeader, server.AccessToken);
        using var response = await client.GetAsync("/api/v1/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(json);
        Assert.Equal("ok", json.Status);
        Assert.Equal(server.Port, json.Port);
        Assert.Equal(server.BaseAddress, json.BaseAddress);
        Assert.False(json.InputInjection);
        Assert.False(json.MemoryDiagnostics);
        Assert.False(json.ChromeInspect);
        Assert.Equal(48, server.AccessToken.Length);
    }

    [Theory]
    [InlineData("/api/v1/input/text", HttpStatusCode.Forbidden)]
    [InlineData("/api/v1/inspect/tree", HttpStatusCode.Forbidden)]
    [InlineData("/api/v1/memory", HttpStatusCode.Forbidden)]
    [InlineData("/unknown", HttpStatusCode.NotFound)]
    public async Task RoutesApplyFeatureGatesAndReturnNotFound(string path, HttpStatusCode expected)
    {
        var window = CreateWindow();
        await using var server = DevToolsServer.Start(window, new DevToolsOptions { AccessToken = "test-token" });
        using var client = new HttpClient { BaseAddress = new Uri(server.BaseAddress) };
        client.DefaultRequestHeaders.Add(DevToolsServer.TokenHeader, server.AccessToken);

        using var response = path.Contains("/input/", StringComparison.Ordinal)
            ? await client.PostAsJsonAsync(path, new { text = "hello" })
            : await client.GetAsync(path);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task MemoryEndpointReportsRuntimeSnapshotWhenEnabled()
    {
        var window = CreateWindow();
        await using var server = DevToolsServer.Start(window, new DevToolsOptions
        {
            AccessToken = "test-token",
            AllowMemoryDiagnostics = true
        });
        using var client = new HttpClient { BaseAddress = new Uri(server.BaseAddress) };
        client.DefaultRequestHeaders.Add(DevToolsServer.TokenHeader, server.AccessToken);

        using var healthResponse = await client.GetAsync("/api/v1/health");
        using var response = await client.GetAsync("/api/v1/memory");

        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
        var health = await healthResponse.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(health);
        Assert.True(health.MemoryDiagnostics);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<MemoryResponse>();
        Assert.NotNull(json);
        Assert.Equal(Environment.ProcessId, json.ProcessId);
        Assert.True(json.SampledAtUnixMilliseconds > 0);
        Assert.True(json.Process.WorkingSetBytes > 0);
        Assert.True(json.Process.PrivateMemoryBytes >= 0);
        Assert.True(json.Process.VirtualMemoryBytes > 0);
        Assert.True(json.Managed.CurrentBytes >= 0);
        Assert.True(json.Managed.ApproximateTotalAllocatedBytes > 0);
        Assert.True(json.Managed.HeapSizeAfterLastGcBytes >= 0);
        Assert.True(json.Managed.FragmentedAfterLastGcBytes >= 0);
        Assert.True(json.Managed.TotalCommittedBytes >= 0);
        Assert.True(json.Managed.TotalAvailableMemoryBytes > 0);
        Assert.True(json.Managed.MemoryLoadBytes >= 0);
        Assert.True(json.Managed.HighMemoryLoadThresholdBytes >= 0);
        Assert.True(json.Managed.PendingFinalizers >= 0);
        Assert.True(json.Managed.PinnedObjects >= 0);
        Assert.InRange(json.Managed.PauseTimePercentage, 0, 100);
        Assert.True(json.Collections.Gen0 >= 0);
        Assert.True(json.Collections.Gen1 >= 0);
        Assert.True(json.Collections.Gen2 >= 0);
    }

    [Fact]
    public async Task ChromeDiscoveryIsDisabledByDefault()
    {
        var window = CreateWindow();
        await using var server = DevToolsServer.Start(window, new DevToolsOptions { AccessToken = "test-token" });
        using var client = new HttpClient { BaseAddress = new Uri(server.BaseAddress) };

        using var response = await client.GetAsync("/json/list");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ChromeDiscoveryReportsTargetWhenEnabled()
    {
        var window = CreateWindow();
        await using var server = DevToolsServer.Start(window, new DevToolsOptions
        {
            AccessToken = "test-token",
            AllowChromeInspect = true
        });
        using var client = new HttpClient { BaseAddress = new Uri(server.BaseAddress) };

        using var versionResponse = await client.GetAsync("/json/version");
        using var listResponse = await client.GetAsync("/json/list");
        using var protocolResponse = await client.GetAsync("/json/protocol");

        Assert.Equal(HttpStatusCode.OK, versionResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, protocolResponse.StatusCode);
        using var protocol = JsonDocument.Parse(await protocolResponse.Content.ReadAsStringAsync());
        Assert.Equal("1", protocol.RootElement.GetProperty("version").GetProperty("major").GetString());
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var target = Assert.Single(list.RootElement.EnumerateArray());
        Assert.Equal("page", target.GetProperty("type").GetString());
        Assert.Equal(server.TargetId, target.GetProperty("id").GetString());
        Assert.Contains("/devtools/page/", target.GetProperty("webSocketDebuggerUrl").GetString());
        Assert.Equal("Square DevTools", target.GetProperty("title").GetString());

        using var redirectClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(server.BaseAddress)
        };
        using var frontendResponse = await redirectClient.GetAsync("/devtools/inspector.html");
        Assert.Equal(HttpStatusCode.Redirect, frontendResponse.StatusCode);
        Assert.StartsWith("devtools://devtools/bundled/inspector.html", frontendResponse.Headers.Location?.ToString());

        client.DefaultRequestHeaders.Add(DevToolsServer.TokenHeader, server.AccessToken);
        using var healthResponse = await client.GetAsync("/api/v1/health");
        var health = await healthResponse.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(health);
        Assert.True(health.ChromeInspect);
    }

    private static AppWindow CreateWindow()
    {
        var window = new AppWindow("DevTools test");
        window.BindApplication(new Dispatcher(), new FakeRuntime());
        return window;
    }

    private sealed class FakeRuntime : IAppWindowRuntime
    {
        public bool IsRunning => true;
        public void RequestRender() { }
        public Task InjectPointerAsync(DevToolsPointerInput input) => Task.CompletedTask;
        public Task InjectKeyAsync(DevToolsKeyInput input) => Task.CompletedTask;
        public Task InjectTextAsync(string text) => Task.CompletedTask;
        public Task InjectWheelAsync(DevToolsWheelInput input) => Task.CompletedTask;
        public Task<Bitmap> CaptureRendererBitmapAsync() => Task.FromResult(new Bitmap(1, 1));
        public Task<ElementInspectionSnapshot> CaptureInspectionSnapshotAsync(bool includeSourcePaths, bool includeTextContent) =>
            throw new NotSupportedException();
        public Task<ElementInspectionNode?> InspectElementAsync(int debugId, bool includeSourcePaths, bool includeTextContent) =>
            throw new NotSupportedException();
        public Task<ElementInspectionStyleSnapshot?> InspectElementStylesAsync(int debugId) =>
            throw new NotSupportedException();
        public Task<bool> SetInspectorHighlightAsync(int debugId) =>
            throw new NotSupportedException();
        public Task ClearInspectorHighlightAsync() =>
            throw new NotSupportedException();
        public Task SetInspectorModeAsync(bool enabled) =>
            throw new NotSupportedException();
        public Task<ElementInspectionNode?> HitTestInspectionAsync(Point point, bool includeSourcePaths, bool includeTextContent) =>
            throw new NotSupportedException();
    }

    private sealed record HealthResponse(
        string Status,
        int ProcessId,
        int Port,
        string BaseAddress,
        bool InputInjection,
        bool MemoryDiagnostics,
        bool ChromeInspect);

    private sealed record MemoryResponse(
        int ProcessId,
        long SampledAtUnixMilliseconds,
        ProcessMemoryResponse Process,
        ManagedMemoryResponse Managed,
        CollectionResponse Collections);

    private sealed record ProcessMemoryResponse(
        long WorkingSetBytes,
        long PrivateMemoryBytes,
        long VirtualMemoryBytes);

    private sealed record ManagedMemoryResponse(
        long CurrentBytes,
        long ApproximateTotalAllocatedBytes,
        long HeapSizeAfterLastGcBytes,
        long FragmentedAfterLastGcBytes,
        long TotalCommittedBytes,
        long TotalAvailableMemoryBytes,
        long MemoryLoadBytes,
        long HighMemoryLoadThresholdBytes,
        long PendingFinalizers,
        long PinnedObjects,
        double PauseTimePercentage);

    private sealed record CollectionResponse(int Gen0, int Gen1, int Gen2);
}
