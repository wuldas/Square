using System.Collections.Generic;
using Square.CSS.Ast;
using Square.CSS.Tokenizer;
using Square.CSS.Engine;
using Square.Controls;
using Xunit;

namespace Square.CSS.Tests;

public class CssTokenizerTests
{
    [Fact]
    public void TokenizeSelector()
    {
        var tokens = new CssTokenizer(".my-class { }").Tokenize();
        Assert.Contains(tokens, t => t.Type == CssTokenType.Dot);
        Assert.Contains(tokens, t => t.Type == CssTokenType.Identifier && t.Text == "my-class");
    }

    [Fact]
    public void TokenizeHash()
    {
        var tokens = new CssTokenizer("#main { }").Tokenize();
        Assert.Contains(tokens, t => t.Type == CssTokenType.Hash && t.Text == "main");
    }

    [Fact]
    public void TokenizeAtKeyword()
    {
        var tokens = new CssTokenizer("@keyframes fade { }").Tokenize();
        Assert.Contains(tokens, t => t.Type == CssTokenType.AtKeyword && t.Text == "keyframes");
    }

    [Fact]
    public void TokenizeNumber()
    {
        var tokens = new CssTokenizer("16px").Tokenize();
        Assert.Contains(tokens, t => t.Type == CssTokenType.Number && t.Text == "16");
        Assert.Contains(tokens, t => t.Type == CssTokenType.Unit && t.Text == "px");
    }

    [Fact]
    public void PreservePercentageDeclaration()
    {
        var tokens = new CssTokenizer("View { width: 100%; }").Tokenize();
        var sheet = new CssParser(tokens).Parse();

        Assert.Contains(tokens, t => t.Type == CssTokenType.Percentage && t.Text == "%");
        Assert.Equal("100%", sheet.Rules[0].Declarations[0].Value);
    }

    [Fact]
    public void TokenizeString()
    {
        var tokens = new CssTokenizer("\"hello\"").Tokenize();
        Assert.Contains(tokens, t => t.Type == CssTokenType.String && t.Text == "hello");
    }

    [Fact]
    public void TokenizeComment()
    {
        var tokens = new CssTokenizer("/* comment */ View { }").Tokenize();
        Assert.DoesNotContain(tokens, t => t.Type == CssTokenType.Comment);
        Assert.Contains(tokens, t => t.Type == CssTokenType.Identifier && t.Text == "View");
    }
}

public class CssParserTests
{
    [Fact]
    public void ParseImportStringAndUrlRulesAtTopOfStyleSheet()
    {
        var css = "@import \"base.css\"; @import url('./theme.css'); Button { color: red; }";
        var sheet = new CssParser(new CssTokenizer(css).Tokenize()).Parse();

        Assert.Equal(2, sheet.Imports.Count);
        Assert.Equal("base.css", sheet.Imports[0].Href);
        Assert.Equal("./theme.css", sheet.Imports[1].Href);
        Assert.Single(sheet.Rules);
    }

    [Fact]
    public void ImportAfterStyleRuleIsIgnoredWithoutConsumingFollowingRules()
    {
        var css = "Text { color: red; } @import \"late.css\"; Button { color: blue; }";
        var sheet = new CssParser(new CssTokenizer(css).Tokenize()).Parse();

        Assert.Empty(sheet.Imports);
        Assert.Equal(2, sheet.Rules.Count);
    }

    [Fact]
    public void ParseImportConditionsForLoaderValidation()
    {
        var sheet = new CssParser(new CssTokenizer(
            "@import url(\"theme.css\") layer(theme) supports(display: grid) screen;").Tokenize()).Parse();

        var import = Assert.Single(sheet.Imports);
        Assert.Equal("theme.css", import.Href);
        Assert.Equal("layer(theme) supports(display: grid) screen", import.Conditions);
    }

    [Fact]
    public void ParseSingleRule()
    {
        var tokens = new CssTokenizer("View { color: red; }").Tokenize();
        var sheet = new CssParser(tokens).Parse();
        Assert.Single(sheet.Rules);
        Assert.Equal("color", sheet.Rules[0].Declarations[0].Property);
        Assert.Equal("red", sheet.Rules[0].Declarations[0].Value.Trim());
    }

    [Fact]
    public void ParseMultipleDeclarations()
    {
        var tokens = new CssTokenizer("View { color: red; padding: 16px; }").Tokenize();
        var sheet = new CssParser(tokens).Parse();
        Assert.Equal(2, sheet.Rules[0].Declarations.Count);
    }

    [Fact]
    public void ParseMultipleRules()
    {
        var tokens = new CssTokenizer("View { color: red; } .cls { padding: 8px; }").Tokenize();
        var sheet = new CssParser(tokens).Parse();
        Assert.Equal(2, sheet.Rules.Count);
    }

    [Fact]
    public void ParseCompoundSelector()
    {
        var tokens = new CssTokenizer("View Text { color: red; }").Tokenize();
        var sheet = new CssParser(tokens).Parse();
        Assert.Single(sheet.Rules);
        Assert.Equal(2, sheet.Rules[0].Selector.Steps.Count);
    }

    [Fact]
    public void ApplyDescendantSelectorVariablesAndInheritance()
    {
        var css = "View { --accent: #123456; color: var(--accent); } View Text { font-size: 20px; }";
        var sheet = new CssParser(new CssTokenizer(css).Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        var root = new View();
        var child = new Square.Controls.Text("child");
        root.Children.Add(child);

        engine.ApplyStylesToTree(root);

        Assert.Equal("#123456", root.Style.Get("color"));
        Assert.Equal("#123456", child.Style.Get("color"));
        Assert.Equal("20px", child.Style.Get("font-size"));
    }

    [Fact]
    public void LaterRuleWinsWhenSpecificityMatches()
    {
        var sheet = new CssParser(new CssTokenizer("Text { color: #111111; } Text { color: #222222; }").Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        var text = new Square.Controls.Text();

        engine.ApplyStyles(text);

        Assert.Equal("#222222", text.Style.Get("color"));
    }

    [Fact]
    public void SpecificitySurvivesStylesAppliedByNestedComponentEngines()
    {
        var innerSheet = new CssParser(new CssTokenizer(".route-links { display: flex; flex-direction: row; }").Tokenize()).Parse();
        var outerSheet = new CssParser(new CssTokenizer("View { display: flex; flex-direction: column; }").Tokenize()).Parse();
        var view = new View();
        view.ClassList.Add("route-links");
        var innerEngine = new CssEngine();
        innerEngine.LoadStyleSheet(innerSheet);
        var outerEngine = new CssEngine();
        outerEngine.LoadStyleSheet(outerSheet);

        innerEngine.ApplyStyles(view);
        outerEngine.ApplyStyles(view);

        Assert.Equal("row", view.Style.Get("flex-direction"));
    }

    [Fact]
    public void InlineStyleRemainsHigherPriorityThanStyleSheets()
    {
        var sheet = new CssParser(new CssTokenizer(".target { color: red; }").Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        var text = new Square.Controls.Text();
        text.ClassList.Add("target");
        text.Style.Set("color", "green");

        engine.ApplyStyles(text);

        Assert.Equal("green", text.Style.Get("color"));
    }

    [Fact]
    public void SelectionPseudoElementMapsToSelectionStyles()
    {
        var sheet = new CssParser(new CssTokenizer(".target::selection { background: #123456; color: #ffffff; }").Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        var text = new Square.Controls.Text("item");
        text.ClassList.Add("target");

        engine.ApplyStyles(text);

        Assert.Equal("#123456", text.Style.Get("selection-background-color"));
        Assert.Equal("#ffffff", text.Style.Get("selection-color"));
        Assert.Null(text.Style.Get("background"));
    }

    [Fact]
    public void ParseBeforeAndAfterPseudoElements()
    {
        var sheet = new CssParser(new CssTokenizer("View::before, View::after { content: '*'; }").Tokenize()).Parse();

        Assert.Equal(2, sheet.Rules.Count);
        Assert.All(sheet.Rules, rule => Assert.Contains(rule.Selector.Steps[0].Selector.Parts,
            part => part.Kind == SimpleSelectorKind.PseudoElement));
    }

    [Fact]
    public void BeforeAndAfterGenerateStyledContentInVisualOrder()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(
            ".target::before { content: '['; color: red; } .target::after { content: ']'; color: blue; }").Tokenize()).Parse());
        var host = new View();
        host.ClassList.Add("target");
        var content = new Square.Controls.Text("content");
        host.Children.Add(content);

        engine.ApplyStylesToTree(host);

        Assert.Equal(3, host.Children.Count);
        var before = Assert.IsAssignableFrom<Square.Controls.Text>(host.Children[0]);
        var after = Assert.IsAssignableFrom<Square.Controls.Text>(host.Children[^1]);
        Assert.Equal("[", before.TextContent);
        Assert.Equal("red", before.Style.Get("color"));
        Assert.Same(content, host.Children[1]);
        Assert.Equal("]", after.TextContent);
        Assert.Equal("blue", after.Style.Get("color"));
        Assert.Null(host.Style.Get("content"));
        Assert.Null(host.Style.Get("color"));
    }

    [Fact]
    public void PseudoElementsRequireContentAndReconcileRemoval()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(
            ".active::before { content: 'prefix'; } .active::after { content: none; }").Tokenize()).Parse());
        var host = new View();
        host.ClassList.Add("active");
        engine.ApplyStylesToTree(host);
        Assert.Single(host.Children);

        host.ClassList.Remove("active");
        CssStyleReconciler.Flush();

        Assert.Empty(host.Children);
    }

    [Fact]
    public void PseudoElementWithEmptyContentIsKeptAsDecorationBox()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(
            ".target::after { content: \"\"; position: absolute; width: 4px; height: 20px; background: #ff0000; }").Tokenize()).Parse());
        var host = new View();
        host.ClassList.Add("target");
        engine.ApplyStylesToTree(host);

        var after = Assert.IsAssignableFrom<Square.Controls.Text>(Assert.Single(host.Children));
        Assert.Equal("", after.TextContent);
        Assert.Equal("#ff0000", after.Style.Get("background"));
        Assert.Equal("4px", after.Style.Get("width"));
        Assert.Equal("20px", after.Style.Get("height"));
    }

    [Fact]
    public void PseudoElementWithoutContentIsRemoved()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(
            ".target::after { position: absolute; width: 4px; height: 20px; background: #ff0000; }").Tokenize()).Parse());
        var host = new View();
        host.ClassList.Add("target");
        engine.ApplyStylesToTree(host);

        Assert.Empty(host.Children);
    }

    [Fact]
    public void SplitterTypeSelectorAppliesWidthAndNegativeMargins()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(
            "Splitter { width: 8px; margin-left: -2px; margin-right: -2px; }").Tokenize()).Parse());
        var splitter = new Splitter();
        engine.ApplyStyles(splitter);

        Assert.Equal("8px", splitter.Style.Get("width"));
        Assert.Equal("-2px", splitter.Style.Get("margin-left"));
        Assert.Equal("-2px", splitter.Style.Get("margin-right"));
    }

    [Fact]
    public void DoubleColonPseudoElementSplitterDoesNotMatch()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(
            "Splitter::Splitter { width: 8px; }").Tokenize()).Parse());
        var splitter = new Splitter();
        engine.ApplyStyles(splitter);

        Assert.Null(splitter.Style.Get("width"));
    }

    [Fact]
    public void GeneratedPseudoElementsDoNotAffectStructuralPseudoClasses()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(
            "View::before { content: 'prefix'; } View > Text:first-child { color: red; } View > Text:nth-child(2) { background: blue; }").Tokenize()).Parse());
        var host = new View();
        var first = new Square.Controls.Text("first");
        var second = new Square.Controls.Text("second");
        host.Children.Add(first);
        host.Children.Add(second);

        engine.ApplyStylesToTree(host);

        Assert.Equal("red", first.Style.Get("color"));
        Assert.Equal("blue", second.Style.Get("background"));
    }

    [Fact]
    public void LegacySingleColonBeforeSupportsCssEscapedGlyphContent()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(
            ".icon:before { content: '\\e669'; }").Tokenize()).Parse());
        var host = new View();
        host.ClassList.Add("icon");

        engine.ApplyStylesToTree(host);

        var generated = Assert.IsAssignableFrom<Square.Controls.Text>(Assert.Single(host.Children));
        Assert.Equal("\ue669", generated.TextContent);
    }

    [Fact]
    public void StyleReconcilerReappliesDynamicClassMatchesAndRemovals()
    {
        var sheet = new CssParser(new CssTokenizer(".active { color: red; width: 120px; }").Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        var text = new Square.Controls.Text("item");
        engine.ApplyStylesToTree(text);

        text.ClassList.Add("active");
        CssStyleReconciler.Flush();
        Assert.Equal("red", text.Style.Get("color"));
        Assert.Equal("120px", text.Style.Get("width"));

        text.ClassList.Remove("active");
        CssStyleReconciler.Flush();
        Assert.Null(text.Style.Get("color"));
        Assert.Null(text.Style.Get("width"));
    }

    [Fact]
    public void IdSelectorMatchesElementIdProperty()
    {
        var sheet = new CssParser(new CssTokenizer("#target { color: blue; }").Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        var text = new Square.Controls.Text("item") { Id = "target" };

        engine.ApplyStylesToTree(text);

        Assert.Equal("blue", text.Style.Get("color"));
    }

    [Fact]
    public void ChildCombinatorOnlyMatchesDirectChildren()
    {
        var css = "View > Text { padding: 7px; }";
        var sheet = new CssParser(new CssTokenizer(css).Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);

        var root = new View();
        var mid = new Square.Controls.Text("mid");
        var directChild = new Square.Controls.Text("direct");
        var grandChild = new Square.Controls.Text("grand");
        root.Children.Add(mid);
        root.Children.Add(directChild);
        mid.Children.Add(grandChild);

        engine.ApplyStylesToTree(root);

        Assert.Equal("7px", directChild.Style.Get("padding"));
        Assert.Null(grandChild.Style.Get("padding"));
    }

    [Fact]
    public void ImportantDeclarationOverridesSpecificity()
    {
        var css = ".high-specificity { color: blue; } Text { color: red !important; }";
        var sheet = new CssParser(new CssTokenizer(css).Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);

        var text = new Square.Controls.Text();
        text.ClassList.Add("high-specificity");

        engine.ApplyStyles(text);

        Assert.Equal("red", text.Style.Get("color"));
    }

    [Fact]
    public void NthChildPseudoClassMatchesCorrectIndex()
    {
        var css = "View > Text:nth-child(2) { color: red; }";
        var sheet = new CssParser(new CssTokenizer(css).Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);

        var root = new View();
        var t1 = new Square.Controls.Text("1");
        var t2 = new Square.Controls.Text("2");
        var t3 = new Square.Controls.Text("3");
        root.Children.Add(t1);
        root.Children.Add(t2);
        root.Children.Add(t3);

        engine.ApplyStylesToTree(root);

        Assert.Null(t1.Style.Get("color"));
        Assert.Equal("red", t2.Style.Get("color"));
        Assert.Null(t3.Style.Get("color"));
    }

    [Fact]
    public void AttributeSelectorMatchesPropertyPresenceAndValue()
    {
        var css = "Button[IsDisabled] { opacity: 0.5; } Button[variant=primary] { color: blue; }";
        var sheet = new CssParser(new CssTokenizer(css).Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);

        var disabled = new Button();
        disabled.IsDisabled = true;
        var primary = new Button();
        primary.SetProperty("variant", "primary");
        var secondary = new Button();
        secondary.SetProperty("variant", "secondary");

        engine.ApplyStyles(disabled);
        engine.ApplyStyles(primary);
        engine.ApplyStyles(secondary);

        Assert.Equal("0.5", disabled.Style.Get("opacity"));
        Assert.Equal("blue", primary.Style.Get("color"));
        Assert.Equal("ButtonText", secondary.Style.Get("color"));
    }

    [Fact]
    public void ParseAdvancedAttributeSelectorOperators()
    {
        var css = "Button[role][variant=primary][tags ~= 'rounded'][lang |= en][code ^= pre][code $= suffix][code *= middle] { color: red; }";
        var sheet = new CssParser(new CssTokenizer(css).Tokenize()).Parse();

        var attributes = sheet.Rules[0].Selector.Steps[0].Selector.Parts
            .Where(part => part.Kind == SimpleSelectorKind.Attribute)
            .ToArray();

        Assert.Collection(attributes,
            selector => AssertAttribute(selector, "role", AttributeSelectorOperator.Presence, null),
            selector => AssertAttribute(selector, "variant", AttributeSelectorOperator.Equals, "primary"),
            selector => AssertAttribute(selector, "tags", AttributeSelectorOperator.Includes, "rounded"),
            selector => AssertAttribute(selector, "lang", AttributeSelectorOperator.DashMatch, "en"),
            selector => AssertAttribute(selector, "code", AttributeSelectorOperator.PrefixMatch, "pre"),
            selector => AssertAttribute(selector, "code", AttributeSelectorOperator.SuffixMatch, "suffix"),
            selector => AssertAttribute(selector, "code", AttributeSelectorOperator.SubstringMatch, "middle"));
    }

    [Fact]
    public void AdvancedAttributeSelectorsMatchWordsPrefixesSuffixesAndSubstrings()
    {
        var css = """
                  Button[tags~=primary] { includes: yes; }
                  Button[lang|=en] { dash: yes; }
                  Button[code^=prefix] { prefix: yes; }
                  Button[code$=suffix] { suffix: yes; }
                  Button[code*=middle] { substring: yes; }
                  Button[label*='hello world' i] { quoted: yes; }
                  """;
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(css).Tokenize()).Parse());
        var button = new Button();
        button.SetProperty("tags", "small primary rounded");
        button.SetProperty("lang", "en-US");
        button.SetProperty("code", "prefix-middle-suffix");
        button.SetProperty("label", "SAY HELLO WORLD NOW");

        engine.ApplyStyles(button);

        Assert.Equal("yes", button.Style.Get("includes"));
        Assert.Equal("yes", button.Style.Get("dash"));
        Assert.Equal("yes", button.Style.Get("prefix"));
        Assert.Equal("yes", button.Style.Get("suffix"));
        Assert.Equal("yes", button.Style.Get("substring"));
        Assert.Equal("yes", button.Style.Get("quoted"));
    }

    [Fact]
    public void AdvancedAttributeSelectorsRejectNearMissesAndMalformedRules()
    {
        var css = """
                  Button[tags~=primary] { includes: yes; }
                  Button[lang|=en] { dash: yes; }
                  Button[code^=prefix] { prefix: yes; }
                  Button[code$=suffix] { suffix: yes; }
                  Button[code*=middle] { substring: yes; }
                  Button[tags~=] { malformed: yes; }
                  """;
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(css).Tokenize()).Parse());
        var button = new Button();
        button.SetProperty("tags", "primaryish");
        button.SetProperty("lang", "english");
        button.SetProperty("code", "middle-prefix-suffix-extra");

        engine.ApplyStyles(button);

        Assert.Null(button.Style.Get("includes"));
        Assert.Null(button.Style.Get("dash"));
        Assert.Null(button.Style.Get("prefix"));
        Assert.Null(button.Style.Get("suffix"));
        Assert.Equal("yes", button.Style.Get("substring"));
        Assert.Null(button.Style.Get("malformed"));
    }

    [Fact]
    public void AttributeSelectorReconcilesAfterAncestorPropertyMutationAndRemoval()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(
            "View[theme=dark] Text { color: white; }").Tokenize()).Parse());
        var root = new View();
        var text = new Square.Controls.Text("item");
        root.Children.Add(text);
        engine.ApplyStylesToTree(root);
        Assert.Null(text.Style.Get("color"));

        root.SetProperty("theme", "dark");
        CssStyleReconciler.Flush();
        Assert.Equal("white", text.Style.Get("color"));

        root.RemoveProperty("theme");
        CssStyleReconciler.Flush();
        Assert.Null(text.Style.Get("color"));
    }

    [Fact]
    public void AdvancedAttributeSelectorUsesClassSpecificityAndSourceOrder()
    {
        var css = """
                  Button[tags~=primary] { color: red; }
                  Button { color: blue; }
                  Button[code^=prefix] { background: green; }
                  Button[code*=middle] { background: yellow; }
                  """;
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(css).Tokenize()).Parse());
        var button = new Button();
        button.SetProperty("tags", "primary");
        button.SetProperty("code", "prefix-middle");

        engine.ApplyStyles(button);

        Assert.Equal("red", button.Style.Get("color"));
        Assert.Equal("yellow", button.Style.Get("background"));
    }

    private static void AssertAttribute(
        SimpleSelector selector,
        string name,
        AttributeSelectorOperator attributeOperator,
        string? value)
    {
        Assert.Equal(name, selector.Name);
        Assert.Equal(attributeOperator, selector.AttributeOperator);
        Assert.Equal(value, selector.AttributeValue);
    }

    [Fact]
    public void ActiveThemeVariablesOverrideStylesheetVariablesWhenStylesAreReapplied()
    {
        var css = ":root { --primary: #111111; } Text { color: var(--primary); }";
        var sheet = new CssParser(new CssTokenizer(css).Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        engine.RegisterTheme("dark", new Dictionary<string, string> { ["--primary"] = "#eeeeee" });
        var text = new Square.Controls.Text();

        engine.ApplyStyles(text);
        Assert.Equal("#111111", text.Style.Get("color"));

        engine.SetTheme("dark");
        engine.ApplyStyles(text);

        Assert.Equal("#eeeeee", text.Style.Get("color"));
    }

    [Fact]
    public void ThemeProviderSwitchesThemeAndReappliesStylesToTree()
    {
        var css = ":root { --primary: #111111; } Text { color: var(--primary); }";
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(css).Tokenize()).Parse());
        engine.RegisterTheme("dark", new Dictionary<string, string> { ["--primary"] = "#eeeeee" });
        var root = new View();
        var text = new Square.Controls.Text("hello");
        root.Children.Add(text);
        var provider = new ThemeProvider(engine, root);

        provider.ApplyTheme(null);
        Assert.Equal("#111111", text.Style.Get("color"));

        provider.ApplyTheme("dark");

        Assert.Equal("#eeeeee", text.Style.Get("color"));
    }
}
