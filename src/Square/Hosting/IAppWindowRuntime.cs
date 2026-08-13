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
    Task<ElementInspectionStyleSnapshot?> InspectElementStylesAsync(int debugId);
    Task<bool> SetInspectorHighlightAsync(int debugId);
    Task ClearInspectorHighlightAsync();
    Task SetInspectorModeAsync(bool enabled);
    Task<ElementInspectionNode?> HitTestInspectionAsync(Point point, bool includeSourcePaths, bool includeTextContent);
}
