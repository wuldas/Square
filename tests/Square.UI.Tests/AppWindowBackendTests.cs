using Square.Graphics;
using Square.Hosting;
using Xunit;

namespace Square.UI.Tests;

public sealed class AppWindowBackendTests
{
    [Fact]
    public void RenderBackendCannotChangeWhileRuntimeIsRunning()
    {
        var window = new AppWindow("backend");
        window.BindApplication(new Square.Runtime.Dispatcher(), new RunningRuntime());

        Assert.Throws<InvalidOperationException>(() => window.RenderBackend = "Skia");
        Assert.Equal("Software", window.RenderBackend);
    }

    private sealed class RunningRuntime : IAppWindowRuntime
    {
        public bool IsRunning => true;
        public void RequestRender() { }
        public Task InjectPointerAsync(DevToolsPointerInput input) => Task.CompletedTask;
        public Task InjectKeyAsync(DevToolsKeyInput input) => Task.CompletedTask;
        public Task InjectTextAsync(string text) => Task.CompletedTask;
        public Task InjectWheelAsync(DevToolsWheelInput input) => Task.CompletedTask;
        public Task<Bitmap> CaptureRendererBitmapAsync() => Task.FromResult(new Bitmap(1, 1));
        public Task<ElementInspectionSnapshot> CaptureInspectionSnapshotAsync(bool includeSourcePaths,
            bool includeTextContent) => throw new NotSupportedException();
        public Task<ElementInspectionNode?> InspectElementAsync(int debugId, bool includeSourcePaths,
            bool includeTextContent) => Task.FromResult<ElementInspectionNode?>(null);
        public Task<ElementInspectionStyleSnapshot?> InspectElementStylesAsync(int debugId) =>
            Task.FromResult<ElementInspectionStyleSnapshot?>(null);
        public Task<bool> SetInspectorHighlightAsync(int debugId) => Task.FromResult(false);
        public Task ClearInspectorHighlightAsync() => Task.CompletedTask;
        public Task SetInspectorModeAsync(bool enabled) => Task.CompletedTask;
        public Task<ElementInspectionNode?> HitTestInspectionAsync(Point point, bool includeSourcePaths,
            bool includeTextContent) => Task.FromResult<ElementInspectionNode?>(null);
    }
}
