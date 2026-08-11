namespace Square.Extensions.CodeEditor;

/// <summary>语言贡献注册表（对齐 Monaco/VS Code languageId）。</summary>
public static class LanguageRegistry
{
    private static readonly object Gate = new();

    private static readonly Dictionary<string, LanguageContribution> Languages =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> ExtensionMap =
        new(StringComparer.OrdinalIgnoreCase);

    private static bool _builtIns;

    /// <summary>注册语言；同 id 覆盖。</summary>
    public static void Register(LanguageContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        ArgumentException.ThrowIfNullOrWhiteSpace(contribution.Id);

        lock (Gate)
        {
            Languages[contribution.Id] = contribution;
            if (contribution.Extensions == null) return;
            foreach (var ext in contribution.Extensions)
            {
                var key = NormalizeExtension(ext);
                if (key.Length > 0)
                    ExtensionMap[key] = contribution.Id;
            }
        }
    }

    /// <summary>按 languageId 查找。</summary>
    public static bool TryGet(string languageId, out LanguageContribution? contribution)
    {
        if (string.IsNullOrWhiteSpace(languageId))
        {
            contribution = null;
            return false;
        }
        lock (Gate)
            return Languages.TryGetValue(languageId.Trim(), out contribution);
    }

    /// <summary>根据路径或扩展名猜测 languageId。</summary>
    public static string? GuessLanguage(string filePathOrExtension)
    {
        if (string.IsNullOrWhiteSpace(filePathOrExtension)) return null;
        var ext = filePathOrExtension.Contains('.', StringComparison.Ordinal)
            ? Path.GetExtension(filePathOrExtension)
            : filePathOrExtension;
        ext = NormalizeExtension(ext);
        return ExtensionMap.TryGetValue(ext, out var id) ? id : null;
    }

    /// <summary>解析 tokenizer。</summary>
    public static ITokenizer ResolveTokenizer(string languageId)
    {
        if (TryGet(languageId, out var c) && c?.Tokenizer != null)
            return c.Tokenizer;
        return new PlainTextTokenizer();
    }

    /// <summary>解析 configuration。</summary>
    public static LanguageConfiguration ResolveConfiguration(string languageId)
    {
        if (TryGet(languageId, out var c) && c?.Configuration != null)
            return c.Configuration;
        return LanguageConfiguration.PlainText;
    }

    /// <summary>确保内置语言已注册。</summary>
    public static void EnsureBuiltIns()
    {
        lock (Gate)
        {
            if (_builtIns && Languages.ContainsKey("csharp") && Languages.ContainsKey("plaintext"))
                return;

            foreach (var contribution in TextMateLanguageProvider.GetBuiltInContributions())
            {
                var languageId = contribution.Id;
                contribution.SetLazyTokenizer(() =>
                    TextMateLanguageProvider.TryCreateTokenizer(languageId, out var tokenizer)
                        ? tokenizer
                        : null);
                Register(contribution);
            }

            RegisterPlainText();

            _builtIns = true;
        }
    }

    private static void RegisterPlainText()
    {
        // Caller holds Gate.
        Languages["plaintext"] = new LanguageContribution
        {
            Id = "plaintext",
            Aliases = ["Plain Text", "text"],
            Extensions = [".txt"],
            Configuration = LanguageConfiguration.PlainText,
            Tokenizer = new PlainTextTokenizer(),
        };
        ExtensionMap[".txt"] = "plaintext";
    }

    private static string NormalizeExtension(string ext)
    {
        ext = ext.Trim();
        if (ext.Length == 0) return "";
        return ext[0] == '.' ? ext.ToLowerInvariant() : "." + ext.ToLowerInvariant();
    }
}

/// <summary>语言贡献。</summary>
public sealed class LanguageContribution
{
    private ITokenizer? _tokenizer;
    private Lazy<ITokenizer?>? _lazyTokenizer;

    /// <summary>languageId。</summary>
    public required string Id { get; init; }
    /// <summary>别名。</summary>
    public IReadOnlyList<string>? Aliases { get; init; }
    /// <summary>扩展名。</summary>
    public IReadOnlyList<string>? Extensions { get; init; }
    /// <summary>编辑配置。</summary>
    public LanguageConfiguration? Configuration { get; init; }
    /// <summary>分词器。</summary>
    public ITokenizer? Tokenizer
    {
        get => _tokenizer ??= _lazyTokenizer?.Value;
        init => _tokenizer = value;
    }

    internal void SetLazyTokenizer(Func<ITokenizer?> factory)
        => _lazyTokenizer = new Lazy<ITokenizer?>(factory);
}

/// <summary>VS Code language-configuration 子集。</summary>
public sealed class LanguageConfiguration
{
    /// <summary>纯文本默认。</summary>
    public static LanguageConfiguration PlainText { get; } = new();

    /// <summary>行注释。</summary>
    public string? LineComment { get; init; }
    /// <summary>块注释。</summary>
    public (string Open, string Close)? BlockComment { get; init; }
    /// <summary>括号。</summary>
    public IReadOnlyList<(string Open, string Close)>? Brackets { get; init; }
    /// <summary>自动闭合。</summary>
    public IReadOnlyList<(string Open, string Close)>? AutoClosingPairs { get; init; }
    /// <summary>选区包裹。</summary>
    public IReadOnlyList<(string Open, string Close)>? SurroundingPairs { get; init; }
    /// <summary>词模式。</summary>
    public string? WordPattern { get; init; }

    /// <summary>C 风格语言配置工厂。</summary>
    public static LanguageConfiguration CLike(string? lineComment, (string, string)? blockComment) => new()
    {
        LineComment = lineComment,
        BlockComment = blockComment,
        Brackets = [("{", "}"), ("[", "]"), ("(", ")")],
        AutoClosingPairs =
        [
            ("{", "}"), ("[", "]"), ("(", ")"),
            ("\"", "\""), ("'", "'"),
        ],
        SurroundingPairs =
        [
            ("{", "}"), ("[", "]"), ("(", ")"),
            ("\"", "\""), ("'", "'"),
        ],
        WordPattern = @"(-?\d*\.\d\w*)|([^\`\~\!\@\#\%\^\&\*\(\)\-\=\+\[\{\]\}\\\|\;\:\'\""\,\.\<\>\/\?\s]+)",
    };
}
