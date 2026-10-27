using Square.Extensions.RichText;
using Square.Extensions.Routing;
using Square.UI;

[assembly: ElementExport("urn:square:extensions", "RichTextEditor", typeof(RichTextEditor))]
[assembly: ElementExport("urn:square:extensions", "RouterView", typeof(RouterView))]
[assembly: ElementExport("urn:square:extensions", "RouterLink", typeof(RouterLink))]

namespace Square.Extensions;

public static class ExtensionRegistration
{
    private static bool _registered;

    public static void RegisterDefaults()
    {
        if (_registered) return;
        _registered = true;

        ElementRegistry.Register("urn:square:extensions", "RichTextEditor", static () => new RichTextEditor());
        ElementRegistry.Register("urn:square:extensions", "RouterView", static () => new RouterView());
        ElementRegistry.Register("urn:square:extensions", "RouterLink", static () => new RouterLink());
    }
}
