using Square.CSS.Ast;
using Square.CSS.Engine;
using Square.CSS.Tokenizer;
using Square.UI;
using Xunit;

namespace Square.CSS.Tests;

public class PseudoClassTests
{
    [Fact]
    public void ParsePseudoClass()
    {
        var tokens = new CssTokenizer("Button:hover { color: red; }").Tokenize();
        var sheet = new CssParser(tokens).Parse();
        Assert.Single(sheet.Rules);
        var parts = sheet.Rules[0].Selector.Steps[0].Selector.Parts;
        Assert.Contains(parts, p => p.Kind == SimpleSelectorKind.PseudoClass && p.Name == "hover");
    }

    [Fact]
    public void ParseMultiplePseudoClasses()
    {
        var tokens = new CssTokenizer("Button:hover:focus { color: red; }").Tokenize();
        var sheet = new CssParser(tokens).Parse();
        var parts = sheet.Rules[0].Selector.Steps[0].Selector.Parts;
        Assert.Equal(3, parts.Count);
        Assert.Contains(parts, p => p.Kind == SimpleSelectorKind.PseudoClass && p.Name == "hover");
        Assert.Contains(parts, p => p.Kind == SimpleSelectorKind.PseudoClass && p.Name == "focus");
    }

    [Fact]
    public void MatchHoverState()
    {
        var tokens = new CssTokenizer("Button:hover { color: red; }").Tokenize();
        var sheet = new CssParser(tokens).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);

        var btn = new Square.Controls.Button();
        btn.SetState(ElementState.Hover, true);
        engine.ApplyStyles(btn);
        Assert.Equal("red", btn.Style.Get("color"));
    }

    [Fact]
    public void NoMatchWhenNoHover()
    {
        var tokens = new CssTokenizer("Button:hover { color: red; }").Tokenize();
        var sheet = new CssParser(tokens).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);

        var btn = new Square.Controls.Button();
        engine.ApplyStyles(btn);
        Assert.Null(btn.Style.Get("color"));
    }

    [Fact]
    public void MatchOpenState()
    {
        var sheet = new CssParser(new CssTokenizer("MenuItem:open { background: navy; }").Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        var item = new Square.Controls.MenuItem();

        item.SetState(ElementState.Open, true);
        engine.ApplyStyles(item);

        Assert.Equal("navy", item.Style.Get("background"));
    }

    [Fact]
    public void StyleReconcilerReappliesDynamicFocusPseudoClass()
    {
        var sheet = new CssParser(new CssTokenizer("Button:focus { color: red; width: 180px; }").Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        var btn = new Square.Controls.Button();
        engine.ApplyStylesToTree(btn);

        btn.Focus();
        CssStyleReconciler.Flush();
        Assert.Equal("red", btn.Style.Get("color"));
        Assert.Equal("180px", btn.Style.Get("width"));

        btn.Unfocus();
        CssStyleReconciler.Flush();
        Assert.Null(btn.Style.Get("color"));
        Assert.Null(btn.Style.Get("width"));
    }

    [Fact]
    public void StyleReconcilerReappliesDynamicHoverAndActivePseudoClasses()
    {
        var sheet = new CssParser(new CssTokenizer("Button:hover { color: red; } Button:active { background: blue; }").Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        var btn = new Square.Controls.Button();
        engine.ApplyStylesToTree(btn);

        btn.SetState(ElementState.Hover, true);
        CssStyleReconciler.Flush();
        Assert.Equal("red", btn.Style.Get("color"));

        btn.SetState(ElementState.Active, true);
        CssStyleReconciler.Flush();
        Assert.Equal("blue", btn.Style.Get("background"));

        btn.SetState(ElementState.Hover, false);
        btn.SetState(ElementState.Active, false);
        CssStyleReconciler.Flush();
        Assert.Null(btn.Style.Get("color"));
        Assert.Null(btn.Style.Get("background"));
    }

    [Fact]
    public void PaintOnlyHoverDoesNotInvalidateLayout()
    {
        var sheet = new CssParser(new CssTokenizer("Button:hover { background: blue; }").Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        var btn = new Square.Controls.Button();
        engine.ApplyStylesToTree(btn);
        btn.ClearLayoutDirty();

        btn.SetState(ElementState.Hover, true);
        CssStyleReconciler.Flush();

        Assert.Equal("blue", btn.Style.Get("background"));
        Assert.False(btn.IsLayoutDirty);
        Assert.True(btn.NeedsPaint);
    }

    [Fact]
    public void TextDecorationHoverDoesNotInvalidateLayout()
    {
        var sheet = new CssParser(new CssTokenizer("Text:hover { text-decoration: underline; }").Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        var root = new Square.Controls.View();
        var text = new Square.Controls.Text("hover me");
        root.Children.Add(text);
        engine.ApplyStylesToTree(root);
        root.ClearLayoutDirty();
        text.ClearLayoutDirty();

        text.SetState(ElementState.Hover, true);
        CssStyleReconciler.Flush();

        Assert.Equal("underline", text.Style.Get("text-decoration"));
        Assert.False(text.IsLayoutDirty);
        Assert.False(root.IsLayoutDirty);
        Assert.True(text.NeedsPaint);
    }

    [Fact]
    public void ConcurrentScopeFlushesDoNotDropStyleInvalidations()
    {
        const int iterations = 100;
        var failures = 0;

        Parallel.For(0, iterations, _ =>
        {
            var sheet = new CssParser(new CssTokenizer("Button:hover { background: blue; }").Tokenize()).Parse();
            var engine = new CssEngine();
            engine.LoadStyleSheet(sheet);
            var button = new Square.Controls.Button();
            engine.ApplyStylesToTree(button);

            button.SetState(ElementState.Hover, true);
            CssStyleReconciler.Flush();
            if (button.Style.Get("background") != "blue")
                Interlocked.Increment(ref failures);
        });

        Assert.Equal(0, failures);
    }

    [Fact]
    public void HoverWithoutDynamicRulesKeepsLayoutClean()
    {
        var sheet = new CssParser(new CssTokenizer("View { width: 120px; color: red; }").Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        var root = new Square.Controls.View();
        var view = new Square.Controls.View();
        root.Children.Add(view);
        engine.ApplyStylesToTree(root);
        root.ClearLayoutDirty();
        view.ClearLayoutDirty();
        root.ClearPaintDirty();
        view.ClearPaintDirty();

        view.SetState(ElementState.Hover, true);
        CssStyleReconciler.Flush();

        Assert.Equal("120px", view.Style.Get("width"));
        Assert.Equal("red", view.Style.Get("color"));
        Assert.False(view.IsLayoutDirty);
        Assert.False(root.IsLayoutDirty);
        Assert.False(view.NeedsPaint);
        Assert.False(root.NeedsPaint);
    }

    [Fact]
    public void ButtonHoverStillInvalidatesNativeHoverVisual()
    {
        var button = new Square.Controls.Button();
        button.ClearPaintDirty();

        button.SetState(ElementState.Hover, true);

        Assert.True(button.NeedsPaint);
    }

    [Fact]
    public void CustomElementHoverKeepsAutomaticPaintInvalidation()
    {
        var element = new CustomHoverElement();
        element.ClearPaintDirty();

        element.SetState(ElementState.Hover, true);

        Assert.True(element.NeedsPaint);
    }

    private sealed class CustomHoverElement : UIElement
    {
    }

    [Fact]
    public void InheritedFontPropertiesWithSameFinalValuesKeepLayoutClean()
    {
        var sheet = new CssParser(new CssTokenizer(
            "View { font-family: Segoe UI; font-size: 16px; } Button:hover { background: blue; }").Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        var root = new Square.Controls.View();
        var componentRoot = new Square.Controls.View();
        var button = new Square.Controls.Button();
        root.Children.Add(componentRoot);
        componentRoot.Children.Add(button);
        engine.ApplyStylesToTree(componentRoot);
        root.ClearLayoutDirty();
        componentRoot.ClearLayoutDirty();
        button.ClearLayoutDirty();

        button.SetState(ElementState.Hover, true);
        CssStyleReconciler.Flush();

        Assert.Equal("Segoe UI", button.Style.Get("font-family"));
        Assert.Equal("16px", button.Style.Get("font-size"));
        Assert.False(button.IsLayoutDirty);
        Assert.False(componentRoot.IsLayoutDirty);
        Assert.False(root.IsLayoutDirty);
    }

    [Fact]
    public void NestedScopePaintOnlyHoverDoesNotDirtyOuterLayoutRoot()
    {
        var sheet = new CssParser(new CssTokenizer("Button:hover { background: blue; }").Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        var outer = new Square.Controls.View();
        var componentRoot = new Square.Controls.View();
        var btn = new Square.Controls.Button();
        outer.Children.Add(componentRoot);
        componentRoot.Children.Add(btn);
        engine.ApplyStylesToTree(componentRoot);
        outer.ClearLayoutDirty();
        componentRoot.ClearLayoutDirty();
        btn.ClearLayoutDirty();

        btn.SetState(ElementState.Hover, true);
        CssStyleReconciler.Flush();

        Assert.Equal("blue", btn.Style.Get("background"));
        Assert.False(btn.IsLayoutDirty);
        Assert.False(componentRoot.IsLayoutDirty);
        Assert.False(outer.IsLayoutDirty);
    }

    [Fact]
    public void LeavingNestedComponentPreservesStylesFromBothScopes()
    {
        var outerEngine = new CssEngine();
        outerEngine.LoadStyleSheet(new CssParser(new CssTokenizer(
            ".component { display: flex; flex-direction: column; } Button:hover { background: blue; }").Tokenize()).Parse());
        var innerEngine = new CssEngine();
        innerEngine.LoadStyleSheet(new CssParser(new CssTokenizer(
            ".component { gap: 8px; } .label { font-size: 18px; }").Tokenize()).Parse());
        var outer = new Square.Controls.View();
        var component = new Square.Controls.View();
        component.ClassList.Add("component");
        var button = new Square.Controls.Button();
        var label = new Square.Controls.Text("label");
        label.ClassList.Add("label");
        outer.Children.Add(component);
        component.Children.Add(button);
        component.Children.Add(label);
        outerEngine.ApplyStylesToTree(outer);
        innerEngine.ApplyStylesToTree(component);

        button.SetState(ElementState.Hover, true);
        CssStyleReconciler.Flush();
        button.SetState(ElementState.Hover, false);
        CssStyleReconciler.Flush();

        Assert.Equal("flex", component.Style.Get("display"));
        Assert.Equal("column", component.Style.Get("flex-direction"));
        Assert.Equal("8px", component.Style.Get("gap"));
        Assert.Equal("18px", label.Style.Get("font-size"));
    }

    [Fact]
    public void HoverReplayDoesNotEraseSiblingComponentScopeStyles()
    {
        var outerEngine = new CssEngine();
        outerEngine.LoadStyleSheet(new CssParser(new CssTokenizer(
            "Button:hover { background: blue; }").Tokenize()).Parse());
        var leftEngine = new CssEngine();
        leftEngine.LoadStyleSheet(new CssParser(new CssTokenizer(
            ".left { display: flex; flex-direction: column; }").Tokenize()).Parse());
        var rightEngine = new CssEngine();
        rightEngine.LoadStyleSheet(new CssParser(new CssTokenizer(
            ".right { display: flex; gap: 12px; } .label { font-size: 18px; }").Tokenize()).Parse());
        var root = new Square.Controls.View();
        var left = new Square.Controls.View();
        left.ClassList.Add("left");
        var button = new Square.Controls.Button();
        left.Children.Add(button);
        var right = new Square.Controls.View();
        right.ClassList.Add("right");
        var label = new Square.Controls.Text("sibling");
        label.ClassList.Add("label");
        right.Children.Add(label);
        root.Children.Add(left);
        root.Children.Add(right);
        outerEngine.ApplyStylesToTree(root);
        leftEngine.ApplyStylesToTree(left);
        rightEngine.ApplyStylesToTree(right);

        button.SetState(ElementState.Hover, true);
        CssStyleReconciler.Flush();

        Assert.Equal("flex", right.Style.Get("display"));
        Assert.Equal("12px", right.Style.Get("gap"));
        Assert.Equal("18px", label.Style.Get("font-size"));
    }

    [Fact]
    public void HoverReplayUpdatesDescendantsAndSiblingSelectors()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(
            ".trigger:hover .child { color: red; } .trigger:hover + .sibling { background: blue; }").Tokenize()).Parse());
        var root = new Square.Controls.View();
        var trigger = new Square.Controls.View();
        trigger.ClassList.Add("trigger");
        var child = new Square.Controls.Text("child");
        child.ClassList.Add("child");
        trigger.Children.Add(child);
        var sibling = new Square.Controls.View();
        sibling.ClassList.Add("sibling");
        root.Children.Add(trigger);
        root.Children.Add(sibling);
        engine.ApplyStylesToTree(root);

        trigger.SetState(ElementState.Hover, true);
        CssStyleReconciler.Flush();

        Assert.Equal("red", child.Style.Get("color"));
        Assert.Equal("blue", sibling.Style.Get("background"));
    }

    [Fact]
    public void SiblingHoverReplayPreservesNestedSiblingScopeStyles()
    {
        var outerEngine = new CssEngine();
        outerEngine.LoadStyleSheet(new CssParser(new CssTokenizer(
            ".trigger:hover + .sibling { background: blue; }").Tokenize()).Parse());
        var siblingEngine = new CssEngine();
        siblingEngine.LoadStyleSheet(new CssParser(new CssTokenizer(
            ".sibling { gap: 12px; } .label { font-size: 18px; }").Tokenize()).Parse());
        var root = new Square.Controls.View();
        var trigger = new Square.Controls.View();
        trigger.ClassList.Add("trigger");
        var sibling = new Square.Controls.View();
        sibling.ClassList.Add("sibling");
        var label = new Square.Controls.Text("label");
        label.ClassList.Add("label");
        sibling.Children.Add(label);
        root.Children.Add(trigger);
        root.Children.Add(sibling);
        outerEngine.ApplyStylesToTree(root);
        siblingEngine.ApplyStylesToTree(sibling);

        trigger.SetState(ElementState.Hover, true);
        CssStyleReconciler.Flush();

        Assert.Equal("blue", sibling.Style.Get("background"));
        Assert.Equal("12px", sibling.Style.Get("gap"));
        Assert.Equal("18px", label.Style.Get("font-size"));
    }

    [Fact]
    public void LayoutHoverInvalidatesLayoutOnlyWhenComputedValueChanges()
    {
        var sheet = new CssParser(new CssTokenizer("Button { width: 100px; } Button:hover { width: 180px; }").Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        var btn = new Square.Controls.Button();
        engine.ApplyStylesToTree(btn);
        btn.ClearLayoutDirty();

        btn.SetState(ElementState.Hover, true);
        CssStyleReconciler.Flush();

        Assert.Equal("180px", btn.Style.Get("width"));
        Assert.True(btn.IsLayoutDirty);
    }

    [Fact]
    public void ParseKeyFrames()
    {
        var css = "@keyframes fade { from { opacity: 0; } to { opacity: 1; } }";
        var tokens = new CssTokenizer(css).Tokenize();
        var sheet = new CssParser(tokens).Parse();
        Assert.Single(sheet.KeyFrames);
        Assert.Equal("fade", sheet.KeyFrames[0].Name);
        Assert.Equal(2, sheet.KeyFrames[0].Stops.Count);
    }

    [Fact]
    public void AnimationShorthandExpandsIntoComputedAnimationProperties()
    {
        var css = "@keyframes fade { from { opacity: 0; } to { opacity: 1; } } Text { animation: fade 0.3s ease-in 100ms 2 reverse; }";
        var sheet = new CssParser(new CssTokenizer(css).Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        var text = new Square.Controls.Text();

        engine.ApplyStyles(text);

        Assert.NotNull(engine.GetKeyFrames("fade"));
        Assert.Equal("fade", text.Style.Get("animation-name"));
        Assert.Equal("0.3s", text.Style.Get("animation-duration"));
        Assert.Equal("ease-in", text.Style.Get("animation-timing-function"));
        Assert.Equal("100ms", text.Style.Get("animation-delay"));
        Assert.Equal("2", text.Style.Get("animation-iteration-count"));
        Assert.Equal("reverse", text.Style.Get("animation-direction"));
    }

    [Fact]
    public void AnimationRuntimeTicksKeyframesIntoVisualStyles()
    {
        var css = "@keyframes fade { from { opacity: 0; } to { opacity: 1; } } Text { animation: fade 1s linear; }";
        var sheet = new CssParser(new CssTokenizer(css).Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        var text = new Square.Controls.Text();
        engine.ApplyStyles(text);

        var timeline = engine.CreateAnimationTimeline(text);
        Assert.NotNull(timeline);

        timeline!.Start();
        timeline.Tick(0.5f);

        Assert.Equal("0.5", text.Style.Get("opacity"));

        timeline.Tick(0.5f);
        Assert.Equal("1", text.Style.Get("opacity"));
        Assert.True(timeline.IsComplete);
    }

    [Fact]
    public void AnimationTimelineHonorsDelayIterationsAndReverseDirection()
    {
        var css = "@keyframes fade { from { opacity: 0; } to { opacity: 1; } } Text { animation: fade 1s linear 0.5s 2 reverse; }";
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(css).Tokenize()).Parse());
        var text = new Square.Controls.Text();
        engine.ApplyStyles(text);
        var timeline = engine.CreateAnimationTimeline(text);

        timeline!.Start();
        Assert.Equal("1", text.Style.Get("opacity"));

        timeline.Tick(0.25f);
        Assert.Equal("1", text.Style.Get("opacity"));

        timeline.Tick(0.5f);
        Assert.Equal("0.75", text.Style.Get("opacity"));

        timeline.Tick(1f);
        Assert.Equal("0.75", text.Style.Get("opacity"));
        Assert.False(timeline.IsComplete);

        timeline.Tick(0.75f);
        Assert.Equal("0", text.Style.Get("opacity"));
        Assert.True(timeline.IsComplete);
    }

    [Fact]
    public void AnimationManagerStartsAndTicksAnimationsInVisualTree()
    {
        var css = "@keyframes fade { from { opacity: 0; } to { opacity: 1; } } Text { animation: fade 1s linear; }";
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(css).Tokenize()).Parse());
        var root = new Square.Controls.View();
        var text = new Square.Controls.Text("animated");
        root.Children.Add(text);
        engine.ApplyStylesToTree(root);
        var manager = new CssAnimationManager(engine);

        manager.Attach(root);
        manager.Tick(0.25f);

        Assert.Equal("0.25", text.Style.Get("opacity"));
        Assert.True(manager.HasRunningAnimations);

        manager.Tick(0.75f);
        Assert.Equal("1", text.Style.Get("opacity"));
        Assert.False(manager.HasRunningAnimations);
    }

    [Fact]
    public void AnimationTimelineInterpolatesAcrossIntermediateKeyframes()
    {
        var css = "@keyframes pulse { 0% { opacity: 0; } 50% { opacity: 1; } 100% { opacity: 0; } } Text { animation: pulse 1s linear; }";
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(css).Tokenize()).Parse());
        var text = new Square.Controls.Text();
        engine.ApplyStyles(text);
        var timeline = engine.CreateAnimationTimeline(text)!;

        timeline.Start();
        timeline.Tick(0.25f);
        Assert.Equal("0.5", text.Style.Get("opacity"));

        timeline.Tick(0.25f);
        Assert.Equal("1", text.Style.Get("opacity"));

        timeline.Tick(0.25f);
        Assert.Equal("0.5", text.Style.Get("opacity"));
    }

    [Fact]
    public void StyleScopeTicksAnimationsAfterStylesAreApplied()
    {
        var css = "@keyframes fade { from { opacity: 0; } to { opacity: 1; } } Text { animation: fade 1s linear; }";
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(css).Tokenize()).Parse());
        var root = new Square.Controls.View();
        var text = new Square.Controls.Text("animated");
        root.Children.Add(text);

        engine.ApplyStylesToTree(root);

        Assert.True(CssStyleReconciler.TickAnimations(root, 0.25f));
        Assert.Equal("0.25", text.Style.Get("opacity"));
        Assert.True(CssStyleReconciler.TickAnimations(root, 0.75f));
        Assert.Equal("1", text.Style.Get("opacity"));
        Assert.False(CssStyleReconciler.TickAnimations(root, 0.1f));
    }
}
