using Square.Extensions.RichText;
using Square.Extensions.Routing;
using Square.UI;

[assembly: ElementExport("urn:square:extensions", "ext", "RichTextEditor", typeof(RichTextEditor))]
[assembly: ElementExport("urn:square:extensions", "ext", "RouterView", typeof(RouterView))]
[assembly: ElementExport("urn:square:extensions", "ext", "RouterLink", typeof(RouterLink))]

namespace Square.Extensions;

public static class ExtensionRegistration
{
    private static bool _registered;

    public static void RegisterDefaults()
    {
        if (_registered) return;
        _registered = true;

        ElementRegistry.Register("RichTextEditor", static () => new RichTextEditor());
        ElementRegistry.Register("RouterView", static () => new RouterView());
        ElementRegistry.Register("RouterLink", static () => new RouterLink());
    }
}
