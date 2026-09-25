using Square.Compiler.Syntax;

namespace Square.Compiler.LanguageServices;

public sealed class TemplateResolutionContext
{
    public TemplateResolutionContext(
        string currentNamespace,
        IReadOnlyList<string> usingNamespaces,
        string defaultElementNamespaceUri = null,
        IReadOnlyDictionary<string, string> prefixNamespaces = null)
    {
        CurrentNamespace = currentNamespace ?? string.Empty;
        UsingNamespaces = Array.AsReadOnly((usingNamespaces ?? Array.Empty<string>()).ToArray());
        DefaultElementNamespaceUri = string.IsNullOrWhiteSpace(defaultElementNamespaceUri)
            ? TemplateCatalog.HtmlNamespaceUri : defaultElementNamespaceUri;
        PrefixNamespaces = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            prefixNamespaces?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, string>(StringComparer.Ordinal));
    }

    public string CurrentNamespace { get; }
    public IReadOnlyList<string> UsingNamespaces { get; }
    public string DefaultElementNamespaceUri { get; }
    public IReadOnlyDictionary<string, string> PrefixNamespaces { get; }

    public TemplateResolutionContext WithDefaultElementNamespace(string namespaceUri) =>
        new(CurrentNamespace, UsingNamespaces, namespaceUri, PrefixNamespaces);

    internal static TemplateResolutionContext FromTemplateText(string text, string sourcePath)
    {
        var dialect = sourcePath?.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase) == true
            ? ComponentDialect.Sqv : ComponentDialect.Sqx;
        var sections = ComponentSectionScanner.Scan(text ?? string.Empty, sourcePath ?? string.Empty,
            dialect, tolerant: true, parseTemplateBody: false).Document;
        var script = sections.Script?.CSharp;
        var usings = script?.Usings
            .Where(directive => directive.Alias == null && directive.StaticKeyword.RawKind == 0)
            .Select(directive => directive.Name?.ToString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name)
            .ToArray() ?? Array.Empty<string>();
        return Create(sections.Script?.Metadata.Namespace ?? string.Empty, usings, null,
            sections.Template?.XmlnsDeclarations, new List<SquareDiagnostic>(), sourcePath);
    }

    internal static TemplateResolutionContext Create(
        string currentNamespace,
        IReadOnlyList<string> usingNamespaces,
        string projectDefaultUri,
        IReadOnlyList<TemplateXmlnsDeclaration> declarations,
        ICollection<SquareDiagnostic> diagnostics,
        string sourcePath)
    {
        var defaultUri = TemplateCatalog.HtmlNamespaceUri;
        if (projectDefaultUri != null)
        {
            if (Uri.TryCreate(projectDefaultUri, UriKind.Absolute, out _)) defaultUri = projectDefaultUri;
            else diagnostics.Add(new SquareDiagnostic("SQXE001", SquareDiagnosticSeverity.Error,
                "SquareDefaultElementNamespace must be a non-empty absolute URI.", new SquareSourceRange(0, 0), sourcePath));
        }
        var prefixes = new Dictionary<string, string>(StringComparer.Ordinal);
        var declaredDefault = false;
        foreach (var declaration in declarations ?? Array.Empty<TemplateXmlnsDeclaration>())
        {
            var prefix = declaration.Prefix;
            var uri = declaration.NamespaceUri;
            if (prefix.Length == 0 ? declaredDefault : prefixes.ContainsKey(prefix))
            {
                diagnostics.Add(new SquareDiagnostic("SQXE001", SquareDiagnosticSeverity.Error,
                    "Duplicate xmlns binding for '" + (prefix.Length == 0 ? "default" : prefix) + "'.",
                    declaration.Range, sourcePath));
                continue;
            }
            if (prefix.Length == 0) declaredDefault = true;
            if (!Uri.TryCreate(uri, UriKind.Absolute, out _) ||
                prefix.Length > 0 && !IsValidPrefix(prefix))
            {
                diagnostics.Add(new SquareDiagnostic("SQXE001", SquareDiagnosticSeverity.Error,
                    "xmlns declarations require a valid prefix and a non-empty absolute URI.",
                    declaration.Range, sourcePath));
                continue;
            }
            var reservedUri = prefix switch
            {
                "html" => TemplateCatalog.HtmlNamespaceUri,
                "ui" => TemplateCatalog.SquareNamespaceUri,
                "svg" => TemplateCatalog.SvgNamespaceUri,
                _ => null
            };
            if (reservedUri != null && uri != reservedUri)
            {
                diagnostics.Add(new SquareDiagnostic("SQXE002", SquareDiagnosticSeverity.Error,
                    "Prefix '" + prefix + "' must bind to '" + reservedUri + "'.",
                    declaration.Range, sourcePath));
                continue;
            }
            if (prefix.Length == 0) defaultUri = uri;
            else prefixes.Add(prefix, uri);
        }
        return new TemplateResolutionContext(currentNamespace, usingNamespaces, defaultUri, prefixes);
    }

    private static bool IsValidPrefix(string prefix) =>
        prefix.Length > 0 && (prefix[0] == '_' || char.IsLetter(prefix[0])) &&
        prefix.Skip(1).All(character => character == '_' || character == '-' || char.IsLetterOrDigit(character));
}
