using Square.Extensions.CodeEditor;
using Xunit;

namespace Square.Extensions.CodeEditor.Tests;

public class TextMateTests
{
    public TextMateTests()
    {
        CodeEditorRegistration.RegisterDefaults();
    }

    [Fact]
    public void Theme_ResolvesTokenColors()
    {
        var theme = CodeEditorThemeRegistry.Get("default-dark");
        var keyword = theme.ResolveTokenColor("keyword");
        var fallback = theme.ResolveTokenColor("unknown.token");
        Assert.NotEqual(keyword, fallback);
        Assert.Equal(theme.EditorForeground, fallback);
    }

    [Fact]
    public void Configuration_CLike_HasPairs()
    {
        Assert.True(LanguageRegistry.TryGet("csharp", out var contribution));
        Assert.NotNull(contribution?.Configuration);
        Assert.Equal("//", contribution!.Configuration!.LineComment);
        Assert.Contains(contribution.Configuration.AutoClosingPairs!, pair => pair.Open == "{");

        var config = LanguageRegistry.ResolveConfiguration("csharp");
        Assert.Equal("//", config.LineComment);
    }

    [Fact]
    public void Registry_ResolvesCSharpTextMateTokenizer()
    {
        Assert.IsType<TextMateTokenizer>(LanguageRegistry.ResolveTokenizer("csharp"));
    }

    [Fact]
    public void BuiltInLanguageCatalog_DoesNotInstantiateEveryGrammar()
    {
        var contributions = TextMateLanguageProvider.GetBuiltInContributions();

        Assert.NotEmpty(contributions);
        Assert.All(contributions, contribution => Assert.Null(contribution.Tokenizer));
    }

    [Fact]
    public void TextMateTokenizer_MapsCSharpScopesAndPreservesMultilineState()
    {
        var cache = new TokenizationCache(LanguageRegistry.ResolveTokenizer("csharp"));
        var model = new CodeEditor { Value = "/* first\nsecond */ public class Sample\n" }.Model;

        var first = cache.GetLineTokens(model, 0);
        var second = cache.GetLineTokens(model, 1);

        Assert.Contains(first, token => token.Type == "comment");
        Assert.Contains(second, token => token.Type == "comment");
        Assert.Contains(second, token => token.Type == "keyword");
        Assert.Contains(second, token => token.Type == "type");
    }

    [Fact]
    public void TokenizationCache_JumpingLinesPreservesMultilineState()
    {
        var lines = new[] { "/* first" }
            .Concat(Enumerable.Repeat("inside", 128))
            .Append("inside */ public class Sample")
            .ToArray();
        var model = new CodeEditor { Value = string.Join('\n', lines) }.Model;
        var cache = new TokenizationCache(LanguageRegistry.ResolveTokenizer("csharp"));

        _ = cache.GetLineTokens(model, 0);
        var last = cache.GetLineTokens(model, lines.Length - 1);

        Assert.Contains(last, token => token.Type == "comment");
        Assert.Contains(last, token => token.Type == "keyword");
        Assert.Contains(last, token => token.Type == "type");
    }

    [Fact]
    public void TextMateTokenizer_HighlightsJson()
    {
        var cache = new TokenizationCache(LanguageRegistry.ResolveTokenizer("json"));
        var model = new CodeEditor { Value = "{\"name\": true, \"count\": 2}\n" }.Model;

        var tokens = cache.GetLineTokens(model, 0);

        Assert.Contains(tokens, token => token.Type is "constant" or "number");
    }

    [Fact]
    public void TextMateGrammarDatabase_RegistersAdditionalLanguages()
    {
        Assert.True(LanguageRegistry.TryGet("rust", out var rust));
        Assert.NotNull(rust);
        Assert.IsType<TextMateTokenizer>(rust!.Tokenizer);
        Assert.Equal("rust", LanguageRegistry.GuessLanguage("main.rs"));
    }

    [Fact]
    public void UnknownLanguage_FallsBackToPlainText()
    {
        Assert.IsType<PlainTextTokenizer>(LanguageRegistry.ResolveTokenizer("not-a-language"));
    }

    [Fact]
    public void TextMateProvider_RegistersVsCodeExtensionDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"square-textmate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "syntaxes"));
        try
        {
            File.WriteAllText(Path.Combine(root, "package.json"), """
                {
                  "name": "square-test-language",
                  "publisher": "square",
                  "version": "1.0.0",
                  "engines": { "vscode": "*" },
                  "contributes": {
                    "languages": [
                      { "id": "square-test", "aliases": ["Square Test"], "extensions": [".sqt"] }
                    ],
                    "grammars": [
                      { "language": "square-test", "scopeName": "source.square-test", "path": "./syntaxes/square-test.tmLanguage.json" }
                    ]
                  }
                }
                """);
            File.WriteAllText(Path.Combine(root, "syntaxes", "square-test.tmLanguage.json"), """
                {
                  "scopeName": "source.square-test",
                  "patterns": [
                    { "name": "keyword.control.square-test", "match": "\\bBEGIN\\b" },
                    { "name": "string.quoted.double.square-test", "begin": "\"", "end": "\"" }
                  ]
                }
                """);

            Assert.Equal(1, TextMateLanguageProvider.RegisterExtension(root));
            Assert.Equal("square-test", LanguageRegistry.GuessLanguage("sample.sqt"));
            var tokenizer = LanguageRegistry.ResolveTokenizer("square-test");
            Assert.IsType<TextMateTokenizer>(tokenizer);
            var state = "root";
            var tokens = tokenizer.TokenizeLine("BEGIN \"value\"", ref state);
            Assert.Contains(tokens, token => token.Type == "keyword");
            Assert.Contains(tokens, token => token.Type == "string");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
