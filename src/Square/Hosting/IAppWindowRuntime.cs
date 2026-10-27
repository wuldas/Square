using Square.Graphics;

namespace Square.Hosting;

internal interface IAppWindowRuntime
{
    bool IsRunning { get; }
    void RequestRender();
    Task InjectPointerAsync(DevToolsPointerInput input);
    Task InjectKeyAsync(DevToolsKeyInput input);
    Task InjectTextAsync(string text);
    Task InjectWheelAsync(DevToolsWheelInput input);
    Task<Bitmap> CaptureRendererBitmapAsync();
    Task<ElementInspectionSnapshot> CaptureInspectionSnapshotAsync(bool includeSourcePaths, bool includeTextContent);
    Task<ElementInspectionNode?> InspectElementAsync(int debugId, bool includeSourcePaths, bool includeTextContent);
    Task<ElementInspectionStyleSnapshot?> InspectElementStylesAsync(int debugId, bool includeStyleText = false,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ElementInspectionStyleText>> CaptureInspectionStyleSheetsAsync(
        CancellationToken cancellationToken = default);
    Task<ElementInspectionStyleText?> InspectStyleSheetAsync(string styleSheetId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ElementInspectionStyleText>> ApplyInspectionStyleEditsAsync(
        IReadOnlyList<ElementInspectionStyleTextEdit> edits, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ElementInspectionComputedStyleSnapshot>> InspectComputedStylesAsync(
        IReadOnlyList<int> debugIds, IReadOnlyList<string>? propertyNames = null,
        CancellationToken cancellationToken = default);
    Task<bool> SetInspectorHighlightAsync(int debugId, ElementInspectionHighlightOptions? options = null,
        long ownerId = 0, CancellationToken cancellationToken = default);
    Task ClearInspectorHighlightAsync(long ownerId = 0, CancellationToken cancellationToken = default);
    Task SetInspectorModeAsync(bool enabled, ElementInspectionHighlightOptions? options = null,
        long ownerId = 0, CancellationToken cancellationToken = default);
    void ReleaseInspectorSession(long ownerId);
    Task<ElementInspectionNode?> HitTestInspectionAsync(Point point, bool includeSourcePaths, bool includeTextContent);
}
