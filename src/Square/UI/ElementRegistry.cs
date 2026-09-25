using Square.Html;

namespace Square.UI;

/// <summary>Explicit AOT factories keyed by resolution URI and local name.</summary>
public static class ElementRegistry
{
    public const string HtmlNamespaceUri = "http://www.w3.org/1999/xhtml";
    public const string SquareNamespaceUri = "urn:square:ui";
    public const string SvgNamespaceUri = "http://www.w3.org/2000/svg";

    private static readonly Dictionary<(string Uri, string Local), Func<Element>> Factories = new();
    private static readonly object Gate = new();

    public static void Register(string namespaceUri, string localName, Func<Element> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceUri);
        ValidateLocalName(localName);
        ArgumentNullException.ThrowIfNull(factory);
        if (!Uri.TryCreate(namespaceUri, UriKind.Absolute, out _))
            throw new ArgumentException("An absolute namespace URI is required.", nameof(namespaceUri));
        var key = (namespaceUri, Normalize(namespaceUri, localName));
        lock (Gate)
        {
            if (Factories.TryGetValue(key, out var previous))
            {
                if (previous.Equals(factory)) return;
                throw new InvalidOperationException($"Element '{localName}' is already registered for '{namespaceUri}'.");
            }
            Factories.Add(key, factory);
        }
    }

    internal static Element Create(string defaultNamespaceUri, string localName, Func<Element>? htmlCustomFactory = null)
    {
        ValidateLocalName(localName);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultNamespaceUri);
        Func<Element>? chosen = null;
        string? chosenUri = null;
        List<string>? conflicts = null;
        lock (Gate)
        {
            if (Factories.TryGetValue((defaultNamespaceUri, Normalize(defaultNamespaceUri, localName)), out chosen))
                chosenUri = defaultNamespaceUri;
            else if (defaultNamespaceUri == HtmlNamespaceUri && htmlCustomFactory != null)
            {
                chosen = htmlCustomFactory;
                chosenUri = HtmlNamespaceUri;
            }
            else
                foreach (var pair in Factories)
                {
                    if (pair.Key.Local != Normalize(pair.Key.Uri, localName)) continue;
                    if (chosen == null)
                    {
                        chosen = pair.Value;
                        chosenUri = pair.Key.Uri;
                    }
                    else
                    {
                        conflicts ??= [chosenUri!];
                        if (!conflicts.Contains(pair.Key.Uri, StringComparer.Ordinal)) conflicts.Add(pair.Key.Uri);
                    }
                }
            if (chosenUri != defaultNamespaceUri && defaultNamespaceUri != HtmlNamespaceUri && htmlCustomFactory != null)
            {
                if (chosen == null)
                {
                    chosen = htmlCustomFactory;
                    chosenUri = HtmlNamespaceUri;
                }
                else
                {
                    conflicts ??= [chosenUri!];
                    if (!conflicts.Contains(HtmlNamespaceUri, StringComparer.Ordinal)) conflicts.Add(HtmlNamespaceUri);
                }
            }
        }
        if (conflicts != null)
            throw new InvalidOperationException($"Element '{localName}' is ambiguous in namespaces: {string.Join(", ", conflicts)}.");
        if (chosen == null)
            throw new InvalidOperationException($"Unknown element '{localName}' for default namespace '{defaultNamespaceUri}'.");
        return CreateInstance(chosenUri!, localName, chosen);
    }

    internal static Element CreateNamespace(string namespaceUri, string localName, Func<Element>? htmlCustomFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceUri);
        ValidateLocalName(localName);
        Func<Element> factory;
        lock (Gate)
            if (!Factories.TryGetValue((namespaceUri, Normalize(namespaceUri, localName)), out factory!))
            {
                if (namespaceUri != HtmlNamespaceUri || htmlCustomFactory == null)
                    throw new InvalidOperationException($"Unknown element '{localName}' in namespace '{namespaceUri}'.");
                factory = htmlCustomFactory;
            }
        return CreateInstance(namespaceUri, localName, factory);
    }

    private static Element CreateInstance(string namespaceUri, string localName, Func<Element> factory)
    {
        var element = factory() ?? throw new InvalidOperationException($"Factory for '{namespaceUri}#{localName}' returned null.");
        if (namespaceUri is not (HtmlNamespaceUri or SquareNamespaceUri or SvgNamespaceUri))
            element.MarkTemplateExport(namespaceUri, localName);
        return element;
    }

    internal static void ValidateLocalName(string localName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localName);
        if (localName.Contains(':'))
            throw new ArgumentException("Qualified names are not accepted; use CreateElementNS or CreateComponentElement with a URI.", nameof(localName));
    }

    internal static string Normalize(string namespaceUri, string localName)
    {
        if (namespaceUri is not (HtmlNamespaceUri or SquareNamespaceUri)) return localName;
        char[]? lowered = null;
        for (var i = 0; i < localName.Length; i++)
            if (localName[i] is >= 'A' and <= 'Z')
            {
                lowered ??= localName.ToCharArray();
                lowered[i] = (char)(localName[i] + ('a' - 'A'));
            }
        return lowered == null ? localName : new string(lowered);
    }
}
