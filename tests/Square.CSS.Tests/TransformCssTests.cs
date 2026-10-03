using System.Numerics;
using Square.CSS.Engine;
using Square.CSS.Properties;
using Square.CSS.Tokenizer;
using Xunit;

namespace Square.CSS.Tests;

public class TransformCssTests
{
    [Fact]
    public void TransformParsesSupportedFunctions()
    {
        Assert.True(CssTransformParser.TryParse("rotate(90deg)", out var rotate));
        AssertMatrix(rotate, Matrix3x2.CreateRotation(MathF.PI / 2));

        Assert.True(CssTransformParser.TryParse("translate(10px, 20px)", out var translate));
        AssertMatrix(translate, Matrix3x2.CreateTranslation(10, 20));

        Assert.True(CssTransformParser.TryParse("scale(2)", out var scale));
        AssertMatrix(scale, Matrix3x2.CreateScale(2, 2));

        Assert.True(CssTransformParser.TryParse("matrix(1, 0, 0, 1, 5, 6)", out var matrix));
        AssertMatrix(matrix, Matrix3x2.CreateTranslation(5, 6));
    }

    [Fact]
    public void TransformComposesFunctionsRightToLeft()
    {
        Assert.True(CssTransformParser.TryParse("translate(100px, 0) rotate(90deg)", out var transform));
        AssertMatrix(transform, Matrix3x2.CreateRotation(MathF.PI / 2) * Matrix3x2.CreateTranslation(100, 0));
    }

    [Fact]
    public void TransformRejectsUnsupportedValues()
    {
        Assert.True(CssTransformParser.IsValid("none"));
        Assert.False(CssTransformParser.IsValid("bogus(1)"));
        Assert.False(CssTransformParser.IsValid("rotate(none)"));
        Assert.False(CssTransformParser.IsValid("matrix(1, 2, 3)"));
        Assert.False(CssTransformParser.IsValid("translate(50%)"));
        Assert.False(CssTransformParser.IsValid("rotate3d(1, 0, 0, 45deg)"));
    }

    [Fact]
    public void TransformDeclarationAppliesToElementStyle()
    {
        var engine = Engine("Text { transform: rotate(45deg); }");
        var text = new Square.Controls.Text();

        engine.ApplyStyles(text);

        Assert.Equal("rotate(45deg)", text.Style.Get("transform"));
    }

    [Fact]
    public void InvalidTransformDeclarationIsDropped()
    {
        var engine = Engine("Text { transform: bogus(1); }");
        var text = new Square.Controls.Text();

        engine.ApplyStyles(text);

        Assert.Null(text.Style.Get("transform"));
    }

    [Fact]
    public void TransformAnimationInterpolatesAngles()
    {
        var engine = Engine(
            "@keyframes spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } } " +
            "Text { animation: spin 1s linear; }");
        var text = new Square.Controls.Text();
        engine.ApplyStyles(text);
        var timeline = engine.CreateAnimationTimeline(text);
        Assert.NotNull(timeline);

        timeline!.Start();
        timeline.Tick(0.25f);
        Assert.Equal("rotate(90deg)", text.Style.Get("transform"));

        timeline.Tick(0.25f);
        Assert.Equal("rotate(180deg)", text.Style.Get("transform"));
    }

    private static CssEngine Engine(string css)
    {
        var sheet = new CssParser(new CssTokenizer(css).Tokenize()).Parse();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        return engine;
    }

    private static void AssertMatrix(Matrix3x2 actual, Matrix3x2 expected)
    {
        Assert.Equal(expected.M11, actual.M11, 5);
        Assert.Equal(expected.M12, actual.M12, 5);
        Assert.Equal(expected.M21, actual.M21, 5);
        Assert.Equal(expected.M22, actual.M22, 5);
        Assert.Equal(expected.M31, actual.M31, 5);
        Assert.Equal(expected.M32, actual.M32, 5);
    }
}
