using Square.CSS.Engine;
using Square.CSS.Tokenizer;
using Square.Controls;
using Square.UI;
using Xunit;

namespace Square.CSS.Tests;

public sealed class ChromeConformanceTests
{
    [Fact]
    public void SpecificityUsesLexicographicColumns()
    {
        var classes = string.Concat(Enumerable.Range(0, 11).Select(index => $".c{index}"));
        var engine = Engine($"{classes} {{ color: blue; }} #target {{ color: red; }}");
        var text = new Square.Controls.Text { Id = "target" };
        for (var index = 0; index < 11; index++) text.ClassList.Add($"c{index}");

        engine.ApplyStyles(text);

        Assert.Equal("red", text.Style.Get("color"));
    }

    [Fact]
    public void InlineImportantWinsAndStylesheetImportantIsRemovedAfterMismatch()
    {
        var engine = Engine(".active { color: blue !important; }");
        var text = new Square.Controls.Text();
        text.ClassList.Add("active");
        text.Style.SetProperty("color", "red", "important");
        engine.ApplyStylesToTree(text);

        Assert.Equal("red", text.Style.Get("color"));

        text.Style.RemoveProperty("color");
        Assert.Equal("blue", text.Style.Get("color"));

        text.ClassList.Remove("active");
        CssStyleReconciler.Flush();
        Assert.Null(text.Style.Get("color"));
    }

    [Fact]
    public void CustomPropertiesCascadeAndInheritPerElement()
    {
        var engine = Engine("View { --accent: red; } .branch { --accent: blue; } Text { color: var(--accent); }");
        var root = new View();
        var branch = new View();
        branch.ClassList.Add("branch");
        var branchText = new Square.Controls.Text();
        var siblingText = new Square.Controls.Text();
        branch.Children.Add(branchText);
        root.Children.Add(branch);
        root.Children.Add(siblingText);

        engine.ApplyStylesToTree(root);

        Assert.Equal("blue", branchText.Style.Get("color"));
        Assert.Equal("red", siblingText.Style.Get("color"));
    }

    [Fact]
    public void NestedVariableFallbackAndCyclesFollowComputedValueRules()
    {
        var engine = Engine("View { --fallback: green; color: var(--missing, var(--fallback)); } Text { --a: var(--b); --b: var(--a); color: var(--a); }");
        var root = new View();
        var text = new Square.Controls.Text();
        root.Children.Add(text);

        engine.ApplyStylesToTree(root);

        Assert.Equal("green", root.Style.Get("color"));
        Assert.Equal("green", text.Style.Get("color"));
    }

    [Fact]
    public void NthChildSupportsAnPlusB()
    {
        var engine = Engine("Text:nth-child(2n+1) { color: red; }");
        var root = new View();
        var children = Enumerable.Range(0, 4).Select(_ => new Square.Controls.Text()).ToArray();
        foreach (var child in children) root.Children.Add(child);

        engine.ApplyStylesToTree(root);

        Assert.Equal("red", children[0].Style.Get("color"));
        Assert.Null(children[1].Style.Get("color"));
        Assert.Equal("red", children[2].Style.Get("color"));
        Assert.Null(children[3].Style.Get("color"));
    }

    [Fact]
    public void EmptyIncludesDomTextNodes()
    {
        var engine = Engine("View:empty { color: red; }");
        var empty = new View();
        var withText = new View();
        withText.AppendChild(new Square.UI.Text("content"));

        engine.ApplyStyles(empty);
        engine.ApplyStyles(withText);

        Assert.Equal("red", empty.Style.Get("color"));
        Assert.Null(withText.Style.Get("color"));
    }

    [Fact]
    public void AttributeValuesAreCaseSensitiveUnlessIFlagIsPresent()
    {
        var engine = Engine("Button[label='save'] { exact: yes; } Button[label='save' i] { insensitive: yes; }");
        var button = new Button();
        button.SetProperty("label", "SAVE");

        engine.ApplyStyles(button);

        Assert.Null(button.Style.Get("exact"));
        Assert.Equal("yes", button.Style.Get("insensitive"));
    }

    [Fact]
    public void AnimationDoesNotOverrideImportantAndRestoresUnderlyingValue()
    {
        var engine = Engine("@keyframes fade { from { opacity: 0; } to { opacity: 1; } } Text { opacity: 0.25 !important; animation: fade 1s linear; }");
        var text = new Square.Controls.Text();
        engine.ApplyStyles(text);
        var timeline = engine.CreateAnimationTimeline(text)!;

        timeline.Start();
        timeline.Tick(0.5f);
        Assert.Equal("0.25", text.Style.Get("opacity"));

        timeline.Tick(0.5f);
        Assert.Equal("0.25", text.Style.Get("opacity"));
    }

    private static CssEngine Engine(string css)
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(css).Tokenize()).Parse());
        return engine;
    }
}
