using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace Square.Extensions.CodeEditor;

/// <summary>TextMate grammar 数据库与 VS Code 扩展加载入口。</summary>
public static class TextMateLanguageProvider
{
    private static readonly object Gate = new();
    private static readonly RegistryOptions Options = new(ThemeName.DarkPlus);
    private static readonly Registry Registry = new(Options);

    /// <summary>尝试为 languageId 创建内置 TextMate tokenizer。</summary>
    public static bool TryCreateTokenizer(string languageId, out ITokenizer? tokenizer)
    {
        tokenizer = null;
        if (string.IsNullOrWhiteSpace(languageId)) return false;
        lock (Gate)
        {
            var scope = Options.GetScopeByLanguageId(languageId.Trim());
            if (string.IsNullOrEmpty(scope)) return false;
            var grammar = Registry.LoadGrammar(scope);
            if (grammar == null) return false;
            tokenizer = new TextMateTokenizer(grammar);
            return true;
        }
    }

    internal static IReadOnlyList<LanguageContribution> GetBuiltInContributions()
    {
        lock (Gate)
        {
            var contributions = new List<LanguageContribution>();
            foreach (var language in Options.GetAvailableLanguages())
            {
                if (string.IsNullOrWhiteSpace(language.Id)) continue;
                var scope = Options.GetScopeByLanguageId(language.Id);
                if (string.IsNullOrEmpty(scope)) continue;
                contributions.Add(new LanguageContribution
                {
                    Id = language.Id,
                    Aliases = language.Aliases,
                    Extensions = language.Extensions,
                    Configuration = ConvertConfiguration(language.Configuration),
                });
            }
            return contributions;
        }
    }

    /// <summary>加载本地 VS Code 扩展目录，并将其中语言注册到 CodeEditor。</summary>
    public static int RegisterExtension(string extensionDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionDirectory);
        var fullPath = Path.GetFullPath(extensionDirectory);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException(fullPath);

        var packageFiles = File.Exists(Path.Combine(fullPath, "package.json"))
            ? [new FileInfo(Path.Combine(fullPath, "package.json"))]
            : new DirectoryInfo(fullPath).GetDirectories()
                .Select(directory => new FileInfo(Path.Combine(directory.FullName, "package.json")))
                .Where(file => file.Exists)
                .ToArray();
        var contributions = new List<LanguageContribution>();
        lock (Gate)
        {
            foreach (var packageFile in packageFiles)
            {
                var definition = GrammarDefinition.Parse(File.ReadAllText(packageFile.FullName));
                var languages = definition?.Contributes?.Languages;
                if (languages == null) continue;
                var key = $"{packageFile.Directory!.Name}-{packageFile.FullName.GetHashCode(StringComparison.Ordinal)}";
                Options.LoadFromLocalFile(key, packageFile, true);

                foreach (var declaredLanguage in languages)
                {
                    if (string.IsNullOrWhiteSpace(declaredLanguage.Id)) continue;
                    var language = Options.GetAvailableLanguages()
                        .LastOrDefault(candidate => candidate.Id.Equals(declaredLanguage.Id, StringComparison.Ordinal));
                    if (language == null) continue;
                    var scope = Options.GetScopeByLanguageId(language.Id);
                    if (string.IsNullOrEmpty(scope)) continue;
                    var grammar = Registry.LoadGrammar(scope);
                    if (grammar == null) continue;
                    contributions.Add(new LanguageContribution
                    {
                        Id = language.Id,
                        Aliases = language.Aliases,
                        Extensions = language.Extensions,
                        Configuration = ConvertConfiguration(language.Configuration),
                        Tokenizer = new TextMateTokenizer(grammar),
                    });
                }
            }
        }
        foreach (var contribution in contributions)
            LanguageRegistry.Register(contribution);
        return contributions.Count;
    }

    private static LanguageConfiguration ConvertConfiguration(TextMateSharp.Grammars.LanguageConfiguration? source)
    {
        if (source == null) return LanguageConfiguration.PlainText;
        var brackets = ToPairs(source.Brackets);
        return new LanguageConfiguration
        {
            LineComment = source.Comments?.LineComment,
            BlockComment = ToPair(source.Comments?.BlockComment),
            Brackets = brackets,
            AutoClosingPairs = MergePairs(brackets, ToAutoPairs(source.AutoClosingPairs)),
            SurroundingPairs = MergePairs(brackets, ToCharPairs(source.SurroundingPairs)),
        };
    }

    private static (string Open, string Close)? ToPair(IList<string>? pair)
        => pair is { Count: >= 2 } ? (pair[0], pair[1]) : null;

    private static IReadOnlyList<(string Open, string Close)>? ToPairs(IList<string>[]? pairs)
        => pairs?.Where(pair => pair.Count >= 2).Select(pair => (pair[0], pair[1])).ToArray();

    private static IReadOnlyList<(string Open, string Close)>? ToAutoPairs(AutoClosingPairs? pairs)
    {
        if (pairs?.AutoPairs is { Length: > 0 })
            return pairs.AutoPairs.Select(pair => (pair.Open, pair.Close)).ToArray();
        return pairs?.CharPairs?
            .Where(pair => pair.Count >= 2)
            .Select(pair => (pair[0].ToString(), pair[1].ToString()))
            .ToArray();
    }

    private static IReadOnlyList<(string Open, string Close)>? ToCharPairs(IList<char>[]? pairs)
        => pairs?.Where(pair => pair.Count >= 2)
            .Select(pair => (pair[0].ToString(), pair[1].ToString()))
            .ToArray();

    private static IReadOnlyList<(string Open, string Close)>? MergePairs(
        IReadOnlyList<(string Open, string Close)>? first,
        IReadOnlyList<(string Open, string Close)>? second)
    {
        if (first == null || first.Count == 0) return second;
        if (second == null || second.Count == 0) return first;
        return first.Concat(second).Distinct().ToArray();
    }
}
