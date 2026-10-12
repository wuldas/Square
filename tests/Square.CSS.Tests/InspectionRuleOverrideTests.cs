using Square.CSS.Ast;
using Square.CSS.Engine;
using Square.CSS.Tokenizer;
using Square.Controls;
using Square.UI;
using Square.UI.ElementApi;
using Square.UI.Scrolling;
using Xunit;

namespace Square.CSS.Tests;

public sealed class InspectionRuleOverrideTests
{
    [Fact]
    public void MatchedRulesExposeRealSourceProvenance()
    {
        var engine = Engine(".target { color: red; }");
        var text = new Square.Controls.Text("item");
        text.ClassList.Add("target");

        var matched = engine.GetMatchedRules(text);

        var rule = Assert.Single(matched, candidate => candidate.Source != null);
        Assert.NotNull(rule.Source);
        Assert.Same(engine, rule.Source!.Value.Engine);
        Assert.Equal(CssCascadeOrigin.Author, rule.Source.Value.Origin);
        Assert.Equal("color", Assert.Single(rule.Source.Value.Rule.Declarations).Property);
        Assert.Equal(".target", rule.Selector);
        Assert.Equal("red", Assert.Single(rule.Declarations).Value);

        // The engine always loads the user agent sheet; its rules carry the UA origin.
        Assert.Contains(engine.GetInspectionRules(), candidate =>
            candidate.Source != null && candidate.Source.Value.Origin == CssCascadeOrigin.UserAgent);
    }

    [Fact]
    public void SharedSelectorGroupWithinEngineChangesTogether()
    {
        var engine = Engine(".a, .b { color: red; }");
        var first = new Square.Controls.Text("first");
        first.ClassList.Add("a");
        var second = new Square.Controls.Text("second");
        second.ClassList.Add("b");
        engine.ApplyStyles(first);
        engine.ApplyStyles(second);
        Assert.Equal("red", first.Style.Get("color"));
        Assert.Equal("red", second.Style.Get("color"));

        var firstSource = engine.GetMatchedRules(first).Single().Source!.Value;
        var secondSource = engine.GetMatchedRules(second).Single().Source!.Value;
        Assert.Same(firstSource.Rule.Declarations, secondSource.Rule.Declarations);

        Assert.True(engine.SetInspectionDeclarations(
            firstSource.Rule, "color: blue;", [new Declaration("color", "blue")]));

        Assert.Equal("color: blue;", engine.GetInspectionDeclarationText(secondSource.Rule));
        Assert.Equal("blue", Assert.Single(engine.GetMatchedRules(second).Single().Declarations).Value);

        engine.ApplyStyles(first);
        engine.ApplyStyles(second);
        Assert.Equal("blue", first.Style.Get("color"));
        Assert.Equal("blue", second.Style.Get("color"));
    }

    [Fact]
    public void OverrideIsEngineLocalWhenAstIsShared()
    {
        var sheet = new CssParser(new CssTokenizer(".target { color: red; }").Tokenize()).Parse();
        var editedEngine = new CssEngine();
        editedEngine.LoadStyleSheet(sheet);
        var untouchedEngine = new CssEngine();
        untouchedEngine.LoadStyleSheet(sheet);
        var edited = new Square.Controls.Text("edited");
        edited.ClassList.Add("target");
        var untouched = new Square.Controls.Text("untouched");
        untouched.ClassList.Add("target");

        var rule = editedEngine.GetMatchedRules(edited).Single().Source!.Value.Rule;
        Assert.True(editedEngine.SetInspectionDeclarations(
            rule, "color: blue;", [new Declaration("color", "blue")]));

        editedEngine.ApplyStyles(edited);
        untouchedEngine.ApplyStyles(untouched);
        Assert.Equal("blue", edited.Style.Get("color"));
        Assert.Equal("red", untouched.Style.Get("color"));
        Assert.Null(untouchedEngine.GetInspectionDeclarationText(rule));
        Assert.Equal("red", Assert.Single(untouchedEngine.GetMatchedRules(untouched).Single().Declarations).Value);
    }

    [Fact]
    public void EditedDeclarationsCascadeWithRealPriority()
    {
        var engine = Engine(".active { color: red; }");
        var text = new Square.Controls.Text("item");
        text.ClassList.Add("active");
        text.Style.Set("color", "green");
        engine.ApplyStylesToTree(text);
        Assert.Equal("green", text.Style.Get("color"));

        var rule = engine.GetMatchedRules(text).Single().Source!.Value.Rule;

        // A non-important override still loses to the inline value.
        Assert.True(engine.SetInspectionDeclarations(rule, "color: blue;", [new Declaration("color", "blue")]));
        CssStyleReconciler.Flush();
        Assert.Equal("green", text.Style.Get("color"));

        // An important override beats the inline normal value.
        Assert.True(engine.SetInspectionDeclarations(rule, "color: blue !important;",
            [new Declaration("color", "blue", Important: true)]));
        CssStyleReconciler.Flush();
        Assert.Equal("blue", text.Style.Get("color"));

        // Inline important still wins over the edited author important.
        text.Style.SetProperty("color", "purple", "important");
        CssStyleReconciler.Flush();
        Assert.Equal("purple", text.Style.Get("color"));
    }

    [Fact]
    public void RebuildClearsOverridesAndRestoresOriginalCascade()
    {
        var sheet = new CssParser(new CssTokenizer(".target { color: red; }").Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        var text = new Square.Controls.Text("item");
        text.ClassList.Add("target");
        engine.ApplyStylesToTree(text);

        var rule = engine.GetMatchedRules(text).Single().Source!.Value.Rule;
        Assert.True(engine.SetInspectionDeclarations(
            rule, "color: blue;", [new Declaration("color", "blue")]));
        CssStyleReconciler.Flush();
        Assert.Equal("blue", text.Style.Get("color"));

        engine.LoadStyleSheet(sheet);
        Assert.Null(engine.GetInspectionDeclarationText(rule));

        // The override invalidation re-applies styles through the reconciler without manual replay.
        CssStyleReconciler.Flush();
        Assert.Equal("red", text.Style.Get("color"));
    }

    [Fact]
    public void SetInspectionDeclarationsRejectsNonAuthorAndUnknownRules()
    {
        var engine = Engine(".target { color: red; }");
        var text = new Square.Controls.Text("item");
        text.ClassList.Add("target");
        engine.ApplyStyles(text);

        var userAgentRule = engine.GetInspectionRules()
            .First(candidate => candidate.Source!.Value.Origin == CssCascadeOrigin.UserAgent);
        Assert.False(engine.SetInspectionDeclarations(
            userAgentRule.Source!.Value.Rule, "color: blue;", [new Declaration("color", "blue")]));
        Assert.Null(engine.GetInspectionDeclarationText(userAgentRule.Source!.Value.Rule));

        var unknownRule = new CssParser(new CssTokenizer(".other { color: green; }").Tokenize()).Parse().Rules[0];
        Assert.False(engine.SetInspectionDeclarations(
            unknownRule, "color: blue;", [new Declaration("color", "blue")]));
        Assert.Null(engine.GetInspectionDeclarationText(unknownRule));
    }

    [Fact]
    public void OrdinaryGeneratedPseudoAndScrollbarOutputsUseOverrides()
    {
        var engine = Engine(
            ".target::before { content: '['; color: red; } " +
            "Text::-webkit-scrollbar-thumb { opacity: 0.4; }");
        var host = new View();
        host.ClassList.Add("target");
        engine.ApplyStyles(host);

        var before = host.Children.OfType<CssGeneratedPseudoElement>()
            .Single(candidate => candidate.PseudoElementName == "before");
        Assert.Equal("red", before.Style.Get("color"));
        // String tokens keep their authored double quotes through FormatValue.
        var contentValue = "\"[\"";
        Assert.Equal(contentValue, before.Style.Get("content"));

        var pseudoSource = engine.GetInspectionRules()
            .Select(candidate => candidate.Source!.Value)
            .Single(candidate => candidate.Origin == CssCascadeOrigin.Author &&
                candidate.Rule.Declarations.Any(declaration =>
                    declaration.Property.Equals("content", StringComparison.OrdinalIgnoreCase)));
        Assert.True(engine.SetInspectionDeclarations(
            pseudoSource.Rule,
            "content: '['; color: blue;",
            [new Declaration("content", contentValue), new Declaration("color", "blue")]));
        engine.ApplyStyles(host);
        before = host.Children.OfType<CssGeneratedPseudoElement>()
            .Single(candidate => candidate.PseudoElementName == "before");
        Assert.Equal("blue", before.Style.Get("color"));
        Assert.Equal(contentValue, before.Style.Get("content"));

        var thumbText = new Square.Controls.Text("scroll");
        engine.ApplyStyles(thumbText);
        Assert.Equal("0.4", thumbText.GetScrollbarPseudoStyle(
            ScrollbarPseudoElements.Thumb, "", "opacity"));
        var scrollbarRule = engine.GetInspectionRules()
            .Select(candidate => candidate.Source!.Value)
            .Single(candidate => candidate.Origin == CssCascadeOrigin.Author &&
                candidate.Rule.Declarations.Any(declaration =>
                    declaration.Property.Equals("opacity", StringComparison.OrdinalIgnoreCase)))
            .Rule;
        Assert.True(engine.SetInspectionDeclarations(
            scrollbarRule, "opacity: 0.9;", [new Declaration("opacity", "0.9")]));
        engine.ApplyStyles(thumbText);
        Assert.Equal("0.9", thumbText.GetScrollbarPseudoStyle(
            ScrollbarPseudoElements.Thumb, "", "opacity"));
    }

    [Fact]
    public void GetInspectionRulesWrapsCurrentRulesInOrderWithEffectiveDeclarations()
    {
        var engine = Engine(".first { color: red; } .second { color: green; }");
        var rules = engine.GetInspectionRules();

        var authorRules = rules.Where(candidate => candidate.Source!.Value.Origin == CssCascadeOrigin.Author).ToArray();
        Assert.Equal(2, authorRules.Length);
        Assert.Equal(".first", authorRules[0].Selector);
        Assert.Equal(".second", authorRules[1].Selector);
        Assert.All(authorRules, candidate => Assert.Same(engine, candidate.Source!.Value.Engine));
        Assert.Equal("red", Assert.Single(authorRules[0].Declarations).Value);

        var firstRule = authorRules[0].Source!.Value.Rule;
        Assert.True(engine.SetInspectionDeclarations(
            firstRule, "color: blue;", [new Declaration("color", "blue")]));
        Assert.Equal("blue", Assert.Single(engine.GetInspectionRules()
            .Where(candidate => candidate.Source!.Value.Origin == CssCascadeOrigin.Author)
            .First().Declarations).Value);
    }

    [Fact]
    public void FontFaceAtRuleIsUntouchedByOverrides()
    {
        var engine = Engine(
            "@font-face { font-family: 'Custom'; src: url('fonts/custom.ttf'); } .target { color: red; }");
        var text = new Square.Controls.Text("item");
        text.ClassList.Add("target");
        engine.ApplyStyles(text);

        Assert.Single(engine.FontFaceDescriptors);
        Assert.Equal("Custom", engine.FontFaceDescriptors[0].Family);
        Assert.DoesNotContain(engine.GetInspectionRules(), candidate =>
            candidate.Selector.Contains("font-face", StringComparison.OrdinalIgnoreCase));

        var rule = engine.GetMatchedRules(text).Single().Source!.Value.Rule;
        Assert.True(engine.SetInspectionDeclarations(
            rule, "color: blue;", [new Declaration("color", "blue")]));
        Assert.Single(engine.FontFaceDescriptors);
    }

    [Fact]
    public void GetInspectionEnginesReturnsDistinctTreeEngines()
    {
        var sheet = new CssParser(new CssTokenizer(".target { color: red; }").Tokenize()).Parse();
        var mainEngine = new CssEngine();
        mainEngine.LoadStyleSheet(sheet);
        var componentEngine = new CssEngine();
        componentEngine.LoadStyleSheet(sheet);
        var otherEngine = new CssEngine();
        otherEngine.LoadStyleSheet(sheet);

        var root = new View();
        var child = new View();
        child.ClassList.Add("target");
        root.Children.Add(child);
        var unrelated = new View();
        mainEngine.ApplyStylesToTree(root);
        using (componentEngine.ApplyGeneratedStylesToTree(child))
        using (componentEngine.ApplyGeneratedStylesToTree(root))
        using (otherEngine.ApplyGeneratedStylesToTree(unrelated))
        {
            var engines = CssStyleReconciler.GetInspectionEngines(root);
            Assert.Equal(2, engines.Count);
            Assert.Same(mainEngine, engines[0]);
            Assert.Same(componentEngine, engines[1]);

            CssStyleReconciler.UnregisterScopesForTree(root);
            Assert.Empty(CssStyleReconciler.GetInspectionEngines(root));
        }
    }

    private static CssEngine Engine(string css)
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(css).Tokenize()).Parse());
        return engine;
    }
}
