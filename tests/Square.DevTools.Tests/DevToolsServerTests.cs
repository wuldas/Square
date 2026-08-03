using System.Net;
using System.Net.Http.Json;
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
        Assert.Equal(48, server.AccessToken.Length);
    }

    [Theory]
    [InlineData("/api/v1/input/text", HttpStatusCode.Forbidden)]
    [InlineData("/api/v1/inspect/tree", HttpStatusCode.Forbidden)]
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
        public Task<ElementInspectionNode?> HitTestInspectionAsync(Point point, bool includeSourcePaths, bool includeTextContent) =>
            throw new NotSupportedException();
    }

    private sealed record HealthResponse(
        string Status,
        int ProcessId,
        int Port,
        string BaseAddress,
        bool InputInjection);
}
