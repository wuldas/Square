namespace Square.Compiler.LanguageServices;

public sealed class TemplateResolutionContext
{
    public TemplateResolutionContext(string currentNamespace, IReadOnlyList<string> usingNamespaces)
    {
        CurrentNamespace = currentNamespace ?? string.Empty;
        UsingNamespaces = Array.AsReadOnly((usingNamespaces ?? Array.Empty<string>()).ToArray());
    }

    public string CurrentNamespace { get; }
    public IReadOnlyList<string> UsingNamespaces { get; }
}
