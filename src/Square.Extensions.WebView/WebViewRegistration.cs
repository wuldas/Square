using Square.UI;

namespace Square.Extensions.WebView;

/// <summary>Registers the native <c>WebView</c> element tag.</summary>
public static class WebViewRegistration
{
    private static bool _registered;

    /// <summary>Registers the native WebView tag. Repeated calls are safe.</summary>
    public static void RegisterDefaults()
    {
        if (_registered) return;
        _registered = true;
        ElementRegistry.Register("WebView", static () => new WebView());
    }
}
