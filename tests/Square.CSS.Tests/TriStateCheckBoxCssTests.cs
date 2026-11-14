using Square.Backends;
using Square.CSS.Engine;
using Square.CSS.Tokenizer;
using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Square.UI;
using Xunit;

namespace Square.CSS.Tests;

public sealed class TriStateCheckBoxCssTests
{
    [Fact]
    public void UserAgentStylesMatchCheckBoxDefaults()
    {
        var engine = new CssEngine();
        var check = new CheckBox();
        var triState = new TriStateCheckBox();
        check.Focus();
        triState.Focus();
        engine.ApplyStyles(check);
        engine.ApplyStyles(triState);

        foreach (var property in new[]
                 {
                     "appearance", "box-sizing", "cursor", "margin-top", "margin-right",
                     "margin-bottom", "margin-left", "outline-offset"
                 })
            Assert.Equal(check.Style.Get(property), triState.Style.Get(property));
        Assert.Equal("auto", triState.Style.Get("appearance"));
        Assert.Equal("2px", triState.Style.Get("outline-offset"));
        Assert.Equal(new Size(13, 13), triState.Measure(new Size(100, 100)));

        triState.IsDisabled = true;
        engine.ApplyStyles(triState);
        Assert.Equal("GrayText", triState.Style.Get("color"));
    }

    [Fact]
    public void CheckedAndIndeterminateSelectorsTrackNullableState()
    {
        var engine = CreateEngine("""
            TriStateCheckBox { background-color: white; }
            TriStateCheckBox:checked { background-color: blue; }
            TriStateCheckBox:indeterminate { background-color: orange; }
            TriStateCheckBox:checked:indeterminate { background-color: red; }
            """);
        var check = new TriStateCheckBox();
        try
        {
            engine.ApplyStylesToTree(check);
            Assert.Equal("white", check.Style.Get("background-color"));

            check.IsChecked = true;
            CssStyleReconciler.Flush();
            Assert.Equal("blue", check.Style.Get("background-color"));

            check.IsChecked = null;
            CssStyleReconciler.Flush();
            Assert.Equal("orange", check.Style.Get("background-color"));

            check.IsChecked = false;
            CssStyleReconciler.Flush();
            Assert.Equal("white", check.Style.Get("background-color"));
        }
        finally
        {
            CssStyleReconciler.UnregisterScopesForTree(check);
        }
    }

    [Fact]
    public void IndeterminateSelectorComposesWithDisabled()
    {
        var engine = CreateEngine("""
            TriStateCheckBox:indeterminate:disabled { color: purple; }
            """);
        var check = new TriStateCheckBox { IsChecked = null, IsDisabled = true };

        engine.ApplyStyles(check);

        Assert.Equal("purple", check.Style.Get("color"));
    }

    [Fact]
    public void StateOnlyChangeInvalidatesPaintWithoutLayout()
    {
        var engine = new CssEngine();
        var check = new TriStateCheckBox();
        try
        {
            engine.ApplyStylesToTree(check);
            check.ClearLayoutDirty();
            check.ClearPaintDirty();

            check.IsChecked = null;
            CssStyleReconciler.Flush();

            Assert.False(check.IsLayoutDirty);
            Assert.True(check.NeedsPaint);
        }
        finally
        {
            CssStyleReconciler.UnregisterScopesForTree(check);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    public void BooleanStatesHaveSamePixelsAsExistingCheckBox(bool isChecked, bool disabled, bool active)
    {
        var check = new CheckBox { IsChecked = isChecked, IsDisabled = disabled };
        var triState = new TriStateCheckBox { IsChecked = isChecked, IsDisabled = disabled };
        check.SetState(ElementState.Active, active);
        triState.SetState(ElementState.Active, active);
        using var expected = Render(check);
        using var actual = Render(triState);

        Assert.True(expected.Pixels.AsSpan().SequenceEqual(actual.Pixels));
    }

    [Fact]
    public void IndeterminatePaintsCenteredHorizontalMark()
    {
        using var bitmap = Render(new TriStateCheckBox { IsChecked = null });

        AssertPixel(bitmap, 14, 10, Color.FromRgb(0, 117, 255));
        AssertPixel(bitmap, 14, 14, Color.White);
        AssertPixel(bitmap, 11, 14, Color.White);
        AssertPixel(bitmap, 17, 14, Color.White);
        AssertPixel(bitmap, 14, 18, Color.FromRgb(0, 117, 255));
    }

    [Fact]
    public void DisabledIndeterminatePaintsGrayHorizontalMark()
    {
        using var bitmap = Render(new TriStateCheckBox { IsChecked = null, IsDisabled = true });

        AssertPixel(bitmap, 14, 10, Color.FromRgb(248, 248, 248));
        AssertPixel(bitmap, 14, 14, Color.FromRgb(209, 209, 209));
        AssertPixel(bitmap, 14, 18, Color.FromRgb(248, 248, 248));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(null)]
    public void AppearanceNoneUsesAuthorBoxWithoutNativeIndicator(bool? isChecked)
    {
        var check = new TriStateCheckBox { IsChecked = isChecked };
        check.Style.CssText = "appearance: none; background-color: #123456; border: 1px solid #abcdef;";
        using var bitmap = Render(check);

        Assert.Equal("none", check.Style.Get("appearance"));
        AssertPixel(bitmap, 14, 14, Color.FromRgb(18, 52, 86));
        AssertPixel(bitmap, 8, 14, Color.FromRgb(171, 205, 239));
    }

    private static CssEngine CreateEngine(string css)
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(css).Tokenize()).Parse());
        return engine;
    }

    private static Bitmap Render(UIElement element)
    {
        element.Geometry = new Rect(8, 8, 13, 13);
        new CssEngine().ApplyStyles(element);
        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(32, 32)
        });
        context.Clear(Color.FromRgb(255, 0, 255));
        var tree = new DisplayTree();
        tree.BuildFrom(element);
        tree.Render(context);
        return ((IRenderBitmapSource)context).CaptureBitmap();
    }

    private static void AssertPixel(Bitmap bitmap, int x, int y, Color expected)
    {
        var pixel = bitmap.GetPixel(x, y);
        Assert.Equal(expected.B, pixel[0]);
        Assert.Equal(expected.G, pixel[1]);
        Assert.Equal(expected.R, pixel[2]);
        Assert.Equal(expected.A, pixel[3]);
    }
}
