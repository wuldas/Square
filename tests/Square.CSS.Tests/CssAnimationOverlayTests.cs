using Square.CSS.Ast;
using Square.CSS.Engine;
using Square.CSS.Tokenizer;
using Square.UI;
using Xunit;

namespace Square.CSS.Tests;

/// <summary>动画数值 overlay：插值输出、!important 优先级与取消恢复行为。</summary>
public class CssAnimationOverlayTests
{
    private static CssAnimationTimeline StartAnimation(string css, Square.Controls.Text text)
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(css).Tokenize()).Parse());
        engine.ApplyStyles(text);
        var timeline = engine.CreateAnimationTimeline(text);
        Assert.NotNull(timeline);
        timeline!.Start();
        return timeline;
    }

    [Fact]
    public void ImportantInlineDeclarationBeatsAnimatedOverlay()
    {
        var text = new Square.Controls.Text();
        var timeline = StartAnimation(
            "@keyframes grow { from { width: 8px; } to { width: 30px; } } Text { animation: grow 1s linear; }", text);
        text.Style.SetProperty("width", "100px", "important");

        timeline.Tick(0.5f);

        Assert.Equal("100px", text.Style.Get("width"));

        text.Style.RemoveProperty("width");
        Assert.Equal("19", text.Style.Get("width"));
    }

    [Fact]
    public void CancelRestoresUnderlyingValueAndClearsOverlay()
    {
        var text = new Square.Controls.Text();
        var timeline = StartAnimation(
            "@keyframes grow { from { width: 8px; } to { width: 30px; } } Text { width: 20px; animation: grow 1s linear; }", text);

        timeline.Tick(0.5f);
        Assert.Equal("19", text.Style.Get("width"));

        timeline.Cancel();

        Assert.Equal("20px", text.Style.Get("width"));
    }

    [Fact]
    public void NumericAnimationPreservesDecimalMidpointRoundingAndNegativeZero()
    {
        var text = new Square.Controls.Text();

        text.Style.SetAnimatedNumeric("width", new(1.0005f, "", ""));
        Assert.Equal("1.001", text.Style.Get("width"));

        text.Style.SetAnimatedNumeric("width", new(-0.0001f, "", ""));
        Assert.Equal("-0", text.Style.Get("width"));
    }

    [Theory]
    [InlineData(float.MaxValue)]
    [InlineData(float.MinValue)]
    [InlineData(float.Epsilon)]
    [InlineData(-float.Epsilon)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void NumericAnimationPreservesExtremeAndSpecialNumberRoundTrips(float value)
    {
        var text = new Square.Controls.Text();
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var expected = float.Parse(value.ToString("0.###", culture), culture);

        text.Style.SetAnimatedNumeric("width", new(value, "", ""));

        Assert.True(text.Style.TryGetEffectiveAnimatedNumeric("width", out var animated));
        Assert.Equal(BitConverter.SingleToInt32Bits(expected), BitConverter.SingleToInt32Bits(animated.Value));
    }

    [Fact]
    public void InlineAnimationNameChangeStopsAndRestartsTheScopedAnimation()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer("""
            @keyframes grow { from { width: 8px; } to { width: 30px; } }
            Text { width: 20px; animation: grow 1s linear infinite; }
            """).Tokenize()).Parse());
        var text = new Square.Controls.Text();
        using var scope = CssStyleReconciler.ApplyOwnedScope(engine, text);
        CssStyleReconciler.Flush(text);
        Assert.True(CssStyleReconciler.TickAnimations(text, 0.25f));
        Assert.Equal("13.5", text.Style.Get("width"));

        text.Style.Set("animation-name", "none");
        CssStyleReconciler.Flush(text);

        Assert.False(CssStyleReconciler.TickAnimations(text, 0.5f));
        Assert.Equal("20px", text.Style.Get("width"));

        text.Style.RemoveProperty("animation-name");
        CssStyleReconciler.Flush(text);

        Assert.True(CssStyleReconciler.TickAnimations(text, 0.5f));
        Assert.Equal("19", text.Style.Get("width"));
    }

    [Fact]
    public void InlineAnimationDurationChangeReplacesTheRunningTiming()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer("""
            @keyframes grow { from { width: 8px; } to { width: 30px; } }
            Text { width: 20px; animation: grow 1s linear infinite; }
            """).Tokenize()).Parse());
        var text = new Square.Controls.Text();
        using var scope = CssStyleReconciler.ApplyOwnedScope(engine, text);
        CssStyleReconciler.Flush(text);
        Assert.True(CssStyleReconciler.TickAnimations(text, 0.25f));

        text.Style.Set("animation-duration", "2s");
        CssStyleReconciler.Flush(text);

        Assert.True(CssStyleReconciler.TickAnimations(text, 0.5f));
        Assert.Equal("13.5", text.Style.Get("width"));
        Assert.True(CssStyleReconciler.TickAnimations(text, 0.5f));
        Assert.Equal("19", text.Style.Get("width"));
    }

    [Fact]
    public void ManagerCancelsAlreadyCompletedTimelineBeforeRetiringIt()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer("""
            @keyframes fade { from { opacity: 0; } to { opacity: 1; } }
            .expired { opacity: 0.4; animation-name: fade; animation-duration: 1s; animation-delay: -2s; }
            .live { animation-name: fade; animation-duration: 2s; animation-iteration-count: infinite; }
            """).Tokenize()).Parse());
        var root = new Square.Controls.View();
        var expired = new Square.Controls.Text();
        expired.ClassList.Add("expired");
        var live = new Square.Controls.Text();
        live.ClassList.Add("live");
        root.Children.Add(expired);
        root.Children.Add(live);
        engine.ApplyStylesToTree(root);
        var manager = new CssAnimationManager(engine);
        manager.Attach(root);

        manager.Tick(0.25f);
        manager.Clear();

        Assert.Equal("0.4", expired.Style.Get("opacity"));
    }

    [Fact]
    public void ReentrantAttachDefersEveryNewTimelineUntilNextTick()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer("""
            @keyframes pulse { from { --pulse: 0; } to { --pulse: 1; } }
            @keyframes grow { from { width: 10px; } to { width: 30px; } }
            .pulse { animation-name: pulse; animation-duration: 2s; animation-iteration-count: infinite; }
            .grow { width: 10px; animation-name: grow; animation-duration: 2s; animation-iteration-count: infinite; }
            """).Tokenize()).Parse());
        var oldRoot = new Square.Controls.View();
        var pulse = new Square.Controls.Text();
        pulse.ClassList.Add("pulse");
        oldRoot.Children.Add(pulse);
        var newRoot = new Square.Controls.View();
        var first = new Square.Controls.Text();
        first.ClassList.Add("grow");
        var second = new Square.Controls.Text();
        second.ClassList.Add("grow");
        newRoot.Children.Add(first);
        newRoot.Children.Add(second);
        engine.ApplyStylesToTree(oldRoot);
        engine.ApplyStylesToTree(newRoot);
        var manager = new CssAnimationManager(engine);
        manager.Attach(oldRoot);
        var switched = false;
        void SwitchRoot(Element element)
        {
            if (switched || !ReferenceEquals(element, pulse)) return;
            switched = true;
            manager.Attach(newRoot);
        }
        Element.StyleInvalidated += SwitchRoot;
        try
        {
            manager.Tick(0.25f);
            Assert.Equal("10px", first.Style.Get("width"));
            Assert.Equal("10px", second.Style.Get("width"));

            manager.Tick(0.25f);
            Assert.Equal("12.5", first.Style.Get("width"));
            Assert.Equal("12.5", second.Style.Get("width"));
        }
        finally
        {
            Element.StyleInvalidated -= SwitchRoot;
        }
    }
}
