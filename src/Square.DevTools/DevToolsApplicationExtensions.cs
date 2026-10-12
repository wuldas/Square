using Square.Hosting;

namespace Square.DevTools;

public static class DevToolsApplicationExtensions
{
    public static DevToolsServer UseDevToolsServer(this AppWindow window, DevToolsOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        // DevToolsServer.Start owns the window.Closed -> Dispose subscription; there
        // must not be a second one here or Dispose would run twice per close.
        return DevToolsServer.Start(window, options);
    }
}
