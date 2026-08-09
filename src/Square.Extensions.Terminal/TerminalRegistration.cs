using Square.UI;

namespace Square.Extensions.Terminal;

/// <summary>Registers the Square terminal control tag.</summary>
public static class TerminalRegistration
{
    private static bool _registered;

    /// <summary>Registers the <c>TerminalView</c> element tag. Repeated calls are safe.</summary>
    public static void RegisterDefaults()
    {
        if (_registered) return;
        _registered = true;
        ElementRegistry.Register("TerminalView", static () => new TerminalView());
    }
}
