using Square.CSS.Engine;
using Square.Graphics;
using Square.CSS.Tokenizer;
using Square.Controls;
using Xunit;

namespace Square.CSS.Tests;

public sealed class CssParserUpgradeTests
{
    [Fact]
    public void MediaRulePreservesMediaTypesAndNestedNormalRules()
    {
        var sheet = Parse("@media screen, print { View { color: red; } .item { width: .5e2px; } }");

        var media = Assert.Single(sheet.MediaRules);
        Assert.Equal(["screen", "print"], media.MediaTypes);
        Assert.Equal(2, media.Rules.Count);
        Assert.Equal("red", media.Rules[0].Declarations[0].Value);
        Assert.Equal(".5e2px", media.Rules[1].Declarations[0].Value);
        Assert.Empty(sheet.Rules);
    }

    [Fact]
    public void MediaRuleSkipsUnknownNestedAtRuleBlockAndContinues()
    {
        var css = "@media screen { @supports (display: grid) { .ignored { color: red; } } Button { color: blue; } } Text { color: green; }";
        var sheet = Parse(css);

        var media = Assert.Single(sheet.MediaRules);
        var mediaRule = Assert.Single(media.Rules);
        Assert.Equal("Button", mediaRule.Selector.Steps[0].Selector.Parts[0].Name);
        Assert.Equal("blue", mediaRule.Declarations[0].Value);
        Assert.Single(sheet.Rules);
        Assert.Equal("Text", sheet.Rules[0].Selector.Steps[0].Selector.Parts[0].Name);
    }

    [Fact]
    public void UnknownAtRuleBlockDoesNotLeakNestedRulesOrFollowingRules()
    {
        var sheet = Parse("@supports (display: grid) { @unknown test { View { color: red; } } } Button { color: blue; }");

        Assert.Single(sheet.AtRules);
        Assert.Empty(sheet.AtRules[0].Declarations);
        var rule = Assert.Single(sheet.Rules);
        Assert.Equal("Button", rule.Selector.Steps[0].Selector.Parts[0].Name);
    }

    [Fact]
    public void UnknownAtRuleInsideDeclarationBlockIsSkippedAsOneBlock()
    {
        var sheet = Parse("View { color: red; @unknown test { bogus: value; } background: blue; }");

        var rule = Assert.Single(sheet.Rules);
        Assert.Collection(rule.Declarations,
            declaration => Assert.Equal(("color", "red"), (declaration.Property, declaration.Value)),
            declaration => Assert.Equal(("background", "blue"), (declaration.Property, declaration.Value)));
    }

    [Fact]
    public void InvalidSelectorGroupRejectsPartialRulesAndRecoversAtNextRule()
    {
        var sheet = Parse("View, . { color: red; } Button { color: blue; }");

        var rule = Assert.Single(sheet.Rules);
        Assert.Equal("Button", rule.Selector.Steps[0].Selector.Parts[0].Name);
        Assert.Equal("blue", rule.Declarations[0].Value);
    }

    [Fact]
    public void TokenizerHandlesLeadingDotExponentAndEscapedIdentifiers()
    {
        var tokens = new CssTokenizer(".icon\\+active { opacity: -.5e+2; width: 1.25e-1px; }").Tokenize();

        Assert.Contains(tokens, token => token.Type == CssTokenType.Identifier && token.Text == "icon+active");
        Assert.Contains(tokens, token => token.Type == CssTokenType.Number && token.Text == "-.5e+2");
        Assert.Contains(tokens, token => token.Type == CssTokenType.Number && token.Text == "1.25e-1");
        Assert.Contains(tokens, token => token.Type == CssTokenType.Unit && token.Text == "px");
    }

    [Fact]
    public void TokenizerCombinesSignedExponentNumbersWithoutSpaces()
    {
        var tokens = new CssTokenizer("width: +1.25e-2em; height: -3E+4%;").Tokenize();

        Assert.Contains(tokens, token => token.Type == CssTokenType.Number && token.Text == "+1.25e-2");
        Assert.Contains(tokens, token => token.Type == CssTokenType.Number && token.Text == "-3E+4");
        Assert.Contains(tokens, token => token.Type == CssTokenType.Unit && token.Text == "em");
        Assert.Contains(tokens, token => token.Type == CssTokenType.Percentage && token.Text == "%");
    }

    [Fact]
    public void SignedExponentLengthsValidateAndApply()
    {
        var sheet = Parse("View { width: 1e2px; margin-left: -1.5e+1px; }");
        var view = new View();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        engine.ApplyStyles(view);

        Assert.Equal("1e2px", view.Style.Get("width"));
        Assert.Equal("-1.5e+1px", view.Style.Get("margin-left"));
    }

    [Fact]
    public void EscapedUrlKeywordIsRecognizedInImportsAndValues()
    {
        var sheet = Parse("@import \\75\\72\\6c(\"theme.css\"); View { background-image: \\75\\72\\6c(theme.png); }");

        var import = Assert.Single(sheet.Imports);
        Assert.Equal("theme.css", import.Href);
        Assert.Equal("url(theme.png)", Assert.Single(sheet.Rules).Declarations[0].Value);

        var view = new View();
        view.Style.SetProperty("background-image", "\\75\\72\\6c(theme.png)");
        Assert.Equal("\\75\\72\\6c(theme.png)", view.Style.GetPropertyValue("background-image"));
    }

    [Fact]
    public void TokenizerPreservesUnicodeIdentifiersAndUriText()
    {
        var tokens = new CssTokenizer("\u6587\\4e2d { background-image: url(https://例え.テスト/画像.png); }").Tokenize();

        Assert.Contains(tokens, token => token.Type == CssTokenType.Identifier && token.Text == "文中");
        Assert.Contains(tokens, token => token.Type == CssTokenType.Identifier && token.Text == "例え");
        Assert.Contains(tokens, token => token.Type == CssTokenType.Identifier && token.Text == "テスト");
    }

    [Fact]
    public void MalformedDeclarationContainingNestedAtRuleRecoversFollowingDeclarations()
    {
        var sheet = Parse("View { color: red; broken: red @nested { ignored: value; } background: blue; }");

        var rule = Assert.Single(sheet.Rules);
        Assert.Collection(rule.Declarations,
            declaration => Assert.Equal(("color", "red"), (declaration.Property, declaration.Value)),
            declaration => Assert.Equal(("background", "blue"), (declaration.Property, declaration.Value)));
    }

    [Fact]
    public void FontFamilyKeywordsDoNotCaptureQuotedFamilyNames()
    {
        Assert.True(Font.IsGenericFamily("serif"));
        Assert.False(Font.IsGenericFamily("\"serif\""));
        Assert.Equal("Times New Roman", Font.FromCss("serif", "16px").Family);
        Assert.Equal("serif", Font.FromCss("\"serif\"", "16px").Family);
        Assert.Equal("inherit", Font.FromCss("\"inherit\"", "16px").Family);
        Assert.Equal("sans-serif", Font.FromCss("default", "16px").Family);
        Assert.Equal("inherit", Font.FromCss("\"inherit\"", "16px", resolveFamily: Font.ResolveGenericFamily).Family);
        Assert.Equal("Segoe UI", Font.FromCss("default", "16px", resolveFamily: Font.ResolveGenericFamily).Family);
    }

    [Fact]
    public void FontFamilyRegistryRejectsMixedGlobalKeywordAndFamilyName()
    {
        var valid = new Square.Controls.Text();
        valid.Style.SetProperty("font-family", "inherit");
        Assert.Equal("inherit", valid.Style.GetPropertyValue("font-family"));

        var quoted = new Square.Controls.Text();
        quoted.Style.SetProperty("font-family", "\"inherit\"");
        Assert.Equal("\"inherit\"", quoted.Style.GetPropertyValue("font-family"));

        var invalid = new Square.Controls.Text();
        invalid.Style.SetProperty("font-family", "inherit, sans-serif");
        Assert.Equal("", invalid.Style.GetPropertyValue("font-family"));
    }

    [Fact]
    public void FontFamilyRegistryRejectsUnquotedDefaultButAcceptsQuotedDefault()
    {
        var unquoted = new Square.Controls.Text();
        unquoted.Style.SetProperty("font-family", "default");
        Assert.Equal("", unquoted.Style.GetPropertyValue("font-family"));

        var quoted = new Square.Controls.Text();
        quoted.Style.SetProperty("font-family", "\"default\"");
        Assert.Equal("\"default\"", quoted.Style.GetPropertyValue("font-family"));
    }

    [Fact]
    public void TokenizerDecodesHexAndSimpleStringEscapes()
    {
        var tokens = new CssTokenizer("content: '\\41 B\\\\C';").Tokenize();

        Assert.Contains(tokens, token => token.Type == CssTokenType.String && token.Text == "AB\\C");
    }

    private static Square.CSS.Ast.CssStyleSheet Parse(string css) =>
        new CssParser(new CssTokenizer(css).Tokenize()).Parse();
}
