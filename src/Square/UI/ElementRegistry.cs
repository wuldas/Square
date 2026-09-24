using Square.UI.Html;

namespace Square.UI;

/// <summary>AOT-friendly registry used by window documents to create elements by tag name.</summary>
public static class ElementRegistry
{
    private static readonly Dictionary<string, Func<Element>> Factories =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>按标签名注册元素工厂。</summary>
    public static void Register(string tagName, Func<Element> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
        ArgumentNullException.ThrowIfNull(factory);
        Factories[tagName] = factory;
    }

    internal static Element Create(string tagName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
        if (tagName.StartsWith("html:", StringComparison.OrdinalIgnoreCase))
        {
            var htmlName = tagName[5..].ToLowerInvariant();
            if (HtmlTagCatalog.IsTag(htmlName)) return new HtmlElement(htmlName);
            throw new InvalidOperationException($"Unknown element tag '{tagName}'. Register it with ElementRegistry.Register.");
        }
        if (HtmlTagCatalog.IsTag(tagName))
            return new HtmlElement(tagName);
        if (!Factories.TryGetValue(tagName, out var factory))
            throw new InvalidOperationException(
                $"Unknown element tag '{tagName}'. Register it with ElementRegistry.Register.");
        return factory();
    }
}
