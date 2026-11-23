using TextMateSharp.Grammars;
using TextMateSharp.Internal.Grammars.Reader;
using TextMateSharp.Internal.Themes.Reader;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace Square.Extensions.Markdown;

// Index bundled grammars without loading editor-only language configurations or snippets.
internal sealed class MarkdownGrammarOptions : IRegistryOptions
{
    private const string GrammarPrefix = "TextMateSharp.Grammars.Resources.Grammars.";
    private const string ThemePrefix = "TextMateSharp.Grammars.Resources.Themes.";
    private const string PackageSuffix = ".package.json";
    private static readonly System.Reflection.Assembly Resources = typeof(RegistryOptions).Assembly;
    private readonly Dictionary<string, string> _scopes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _grammars = new(StringComparer.Ordinal);

    public MarkdownGrammarOptions()
    {
        var names = Resources.GetManifestResourceNames();
        Array.Sort(names, StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (!name.StartsWith(GrammarPrefix, StringComparison.Ordinal) ||
                !name.EndsWith(PackageSuffix, StringComparison.Ordinal)) continue;

            using var stream = Resources.GetManifestResourceStream(name)!;
            var definition = GrammarDefinition.Parse(stream);
            var package = name[..^PackageSuffix.Length];
            foreach (var grammar in definition.Contributes.Grammars)
            {
                if (!string.IsNullOrEmpty(grammar.Language))
                    _scopes.TryAdd(grammar.Language, grammar.ScopeName);
                var path = grammar.Path;
                if (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];
                _grammars.TryAdd(grammar.ScopeName, package + "." + path.Replace('/', '.'));
            }
        }
    }

    public string? GetScopeByLanguageId(string languageId) => _scopes.GetValueOrDefault(languageId);

    public IRawGrammar? GetGrammar(string scopeName)
    {
        if (!_grammars.TryGetValue(scopeName, out var resource)) return null;
        using var stream = Resources.GetManifestResourceStream(resource);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return GrammarReader.ReadGrammarSync(reader);
    }

    public IRawTheme? GetTheme(string scopeName)
    {
        using var stream = Resources.GetManifestResourceStream(
            ThemePrefix + scopeName.Replace("./", "", StringComparison.Ordinal));
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return ThemeReader.ReadThemeSync(reader);
    }

    public IRawTheme? GetDefaultTheme() => GetTheme("dark_plus.json");

    public ICollection<string>? GetInjections(string scopeName) => null;
}
