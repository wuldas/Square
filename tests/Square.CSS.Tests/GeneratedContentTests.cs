using Square.Controls;
using Square.CSS.Engine;
using Square.CSS.Tokenizer;
using Xunit;

namespace Square.CSS.Tests;

public sealed class GeneratedContentTests
{
    [Fact]
    public void DefaultMediaTypeAppliesUnconditionalAllAndScreenRules()
    {
        var engine = CreateEngine("""
            View { base-rule: yes; }
            @media all { View { all-rule: yes; } }
            @media screen { View { screen-rule: yes; } }
            @media print { View { print-rule: yes; } }
            @media speech { View { speech-rule: yes; } }
            """);
        var view = new View();

        engine.ApplyStyles(view);

        Assert.Equal("screen", engine.MediaType);
        Assert.Equal("yes", view.Style.Get("base-rule"));
        Assert.Equal("yes", view.Style.Get("all-rule"));
        Assert.Equal("yes", view.Style.Get("screen-rule"));
        Assert.Null(view.Style.Get("print-rule"));
        Assert.Null(view.Style.Get("speech-rule"));
    }

    [Fact]
    public void ChangingMediaTypeReappliesRegisteredTree()
    {
        var engine = CreateEngine("""
            @media screen { View { mode: screen; } }
            @media print { View { mode: print; } }
            """);
        var view = new View();
        engine.ApplyStylesToTree(view);
        Assert.Equal("screen", view.Style.Get("mode"));

        engine.SetMediaType("print");
        CssStyleReconciler.Flush();

        Assert.Equal("print", view.Style.Get("mode"));
    }

    [Fact]
    public void GeneratedContentEvaluatesAttributesAndQuoteDepthTokens()
    {
        var engine = CreateEngine("""
            .target { quotes: "<" ">" "[" "]"; }
            .target::before { content: open-quote attr(label) no-open-quote "nested" close-quote no-close-quote close-quote; }
            """);
        var target = new View();
        target.ClassList.Add("target");
        target.SetProperty("label", "item");

        engine.ApplyStylesToTree(target);

        Assert.Equal("<itemnested]>", GeneratedText(target, 0));

        target.SetProperty("label", "updated");
        CssStyleReconciler.Flush();

        Assert.Equal("<updatednested]>", GeneratedText(target, 0));
    }

    [Fact]
    public void NestedCountersUseDecimalScopesAndRecomputeAfterClassChanges()
    {
        var engine = CreateEngine("""
            .root, .nested { counter-reset: section; }
            .item::before { counter-increment: section; content: counters(section, ".") " "; }
            .item::after { content: counter(section); }
            """);
        var root = new View();
        root.ClassList.Add("root");
        var first = Item();
        var nested = new View();
        nested.ClassList.Add("nested");
        var nestedFirst = Item();
        var nestedSecond = Item();
        nested.Children.Add(nestedFirst);
        nested.Children.Add(nestedSecond);
        first.Children.Add(nested);
        var second = Item();
        root.Children.Add(first);
        root.Children.Add(second);

        engine.ApplyStylesToTree(root);

        Assert.Equal("1 ", GeneratedText(first, 0));
        Assert.Equal("1.1 ", GeneratedText(nestedFirst, 0));
        Assert.Equal("1.2 ", GeneratedText(nestedSecond, 0));
        Assert.Equal("2 ", GeneratedText(second, 0));
        Assert.Equal("2", GeneratedText(second, second.Children.Count - 1));

        first.ClassList.Remove("item");
        CssStyleReconciler.Flush();

        Assert.Equal("1 ", GeneratedText(second, 0));
        Assert.Equal("1", GeneratedText(second, second.Children.Count - 1));
    }

    [Theory]
    [InlineData("disc", "\u2022 ")]
    [InlineData("circle", "\u25e6 ")]
    [InlineData("square", "\u25aa ")]
    [InlineData("decimal", "1. ")]
    [InlineData("lower-alpha", "a. ")]
    [InlineData("upper-alpha", "A. ")]
    [InlineData("lower-roman", "i. ")]
    [InlineData("upper-roman", "I. ")]
    public void ListItemGeneratesMarkerFromListStyleType(string listStyleType, string expected)
    {
        var engine = CreateEngine($".item {{ display: list-item; list-style-type: {listStyleType}; }}");
        var root = new View();
        var item = Item();
        root.Children.Add(item);

        engine.ApplyStylesToTree(root);

        Assert.Equal(expected, GeneratedText(item, 0));
    }

    [Fact]
    public void MarkerUsesListIndexOrExplicitContentAndDoesNotAffectStructuralSelectors()
    {
        var engine = CreateEngine("""
            .item { display: list-item; list-style-type: decimal; }
            .custom::marker { content: "(" attr(label) ") "; color: red; }
            View > .item:first-child { selected: yes; }
            """);
        var root = new View();
        var first = Item();
        first.SetProperty("label", "x");
        first.ClassList.Add("custom");
        var second = Item();
        root.Children.Add(first);
        root.Children.Add(second);

        engine.ApplyStylesToTree(root);

        Assert.Equal("(x) ", GeneratedText(first, 0));
        Assert.Equal("red", first.Children[0].Style.Get("color"));
        Assert.Equal("2. ", GeneratedText(second, 0));
        Assert.Equal("yes", first.Style.Get("selected"));
        Assert.Null(second.Style.Get("selected"));
    }

    private static CssEngine CreateEngine(string css)
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(css).Tokenize()).Parse());
        return engine;
    }

    private static View Item()
    {
        var item = new View();
        item.ClassList.Add("item");
        return item;
    }

    private static string GeneratedText(View owner, int index) =>
        Assert.IsAssignableFrom<Square.Controls.Text>(owner.Children[index]).TextContent;
}
