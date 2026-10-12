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
        public Task<ElementInspectionStyleSnapshot?> InspectElementStylesAsync(int debugId, bool includeStyleText = false,
            CancellationToken cancellationToken = default) => Task.FromResult<ElementInspectionStyleSnapshot?>(null);
        public Task<IReadOnlyList<ElementInspectionStyleText>> CaptureInspectionStyleSheetsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ElementInspectionStyleText>>([]);
        public Task<ElementInspectionStyleText?> InspectStyleSheetAsync(string styleSheetId,
            CancellationToken cancellationToken = default) => Task.FromResult<ElementInspectionStyleText?>(null);
        public Task<IReadOnlyList<ElementInspectionStyleText>> ApplyInspectionStyleEditsAsync(
            IReadOnlyList<ElementInspectionStyleTextEdit> edits, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ElementInspectionStyleText>>([]);
        public Task<IReadOnlyList<ElementInspectionComputedStyleSnapshot>> InspectComputedStylesAsync(
            IReadOnlyList<int> debugIds, IReadOnlyList<string>? propertyNames = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ElementInspectionComputedStyleSnapshot>>([]);
        public Task<bool> SetInspectorHighlightAsync(int debugId, ElementInspectionHighlightOptions? options = null,
            long ownerId = 0, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task ClearInspectorHighlightAsync(long ownerId = 0, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task SetInspectorModeAsync(bool enabled, ElementInspectionHighlightOptions? options = null,
            long ownerId = 0, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void ReleaseInspectorSession(long ownerId) { }
        public Task<ElementInspectionNode?> HitTestInspectionAsync(Point point, bool includeSourcePaths,
            bool includeTextContent) => Task.FromResult<ElementInspectionNode?>(null);
    }
}
