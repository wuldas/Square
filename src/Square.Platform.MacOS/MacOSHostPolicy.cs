using Square.Graphics;
using Square.Hosting;

namespace Square.Platform.MacOS;

internal static class MacOSHostPolicy
{
    internal static AppWindowState ResolveState(bool miniaturized, bool zoomed, nuint styleMask)
    {
        if (miniaturized) return AppWindowState.Minimized;
        return zoomed || (styleMask & MacOSApi.WindowStyleFullScreen) != 0
            ? AppWindowState.Maximized
            : AppWindowState.Normal;
    }

    internal static Rect ToCocoaTextInputRect(Rect rect, Size clientSize)
    {
        var width = Math.Max(1, rect.Width);
        var height = Math.Max(1, rect.Height);
        return new Rect(rect.X, clientSize.Height - rect.Y - height, width, height);
    }
}
