using Square.Rendering.Yoga;
using Xunit;

namespace Square.UI.Tests;

public class YogaStyleLengthTests
{
    [Fact]
    public void DefaultStyleLengthBehavesExactlyLikeUndefined()
    {
        var length = default(StyleLength);

        Assert.True(length.IsUndefined());
        Assert.False(length.IsDefined());
        Assert.True(length.Value().IsUndefined());
        Assert.True(length.Resolve(200f).IsUndefined());

        var undefined = StyleLength.Undefined();
        Assert.True(length == undefined);
        Assert.False(length != undefined);
        Assert.True(length.Equals(undefined));
        Assert.True(length.Equals((object)undefined));
        Assert.Equal(undefined.GetHashCode(), length.GetHashCode());
        Assert.True(length.InexactEquals(undefined));

        var value = (YGValue)length;
        Assert.Equal(Unit.Undefined, value.Unit);
        Assert.True(float.IsNaN(value.Value));
        Assert.Equal(YogaValue.YGValueUndefined, value);
    }

    [Fact]
    public void UndefinedAndAutoAreDistinctKeywordsWithoutValue()
    {
        var undefined = StyleLength.Undefined();
        Assert.True(undefined.IsUndefined());
        Assert.False(undefined.IsAuto());
        Assert.False(undefined.IsPoints());
        Assert.False(undefined.IsPercent());
        Assert.True(undefined.Value().IsUndefined());
        Assert.True(undefined.Resolve(100f).IsUndefined());

        var auto = StyleLength.OfAuto();
        Assert.True(auto.IsAuto());
        Assert.False(auto.IsUndefined());
        Assert.True(auto.IsDefined());
        Assert.False(auto.IsPoints());
        Assert.False(auto.IsPercent());
        Assert.True(auto.Value().IsUndefined());
        Assert.True(auto.Resolve(100f).IsUndefined());

        Assert.True(auto != undefined);
        Assert.False(auto == undefined);
        Assert.False(auto.Equals(undefined));
        Assert.False(auto.InexactEquals(undefined));

        var value = (YGValue)auto;
        Assert.Equal(Unit.Auto, value.Unit);
        Assert.True(float.IsNaN(value.Value));
        Assert.Equal(YogaValue.YGValueAuto, value);
    }

    [Fact]
    public void PointsCarryValueAndRejectNaNAndInfinity()
    {
        var points = StyleLength.Points(12.5f);
        Assert.True(points.IsPoints());
        Assert.False(points.IsUndefined());
        Assert.False(points.IsAuto());
        Assert.False(points.IsPercent());
        Assert.Equal(12.5f, points.Value().Unwrap());
        Assert.Equal(12.5f, points.Resolve(1000f).Unwrap());

        Assert.True(StyleLength.Points(float.NaN).IsUndefined());
        Assert.True(StyleLength.Points(float.PositiveInfinity).IsUndefined());
        Assert.True(StyleLength.Points(float.NegativeInfinity).IsUndefined());
        Assert.True(StyleLength.Points(float.NaN).Equals(StyleLength.Undefined()));
    }

    [Fact]
    public void PercentResolvesAgainstReferenceLength()
    {
        var percent = StyleLength.Percent(25f);
        Assert.True(percent.IsPercent());
        Assert.False(percent.IsPoints());
        Assert.False(percent.IsUndefined());
        Assert.Equal(25f, percent.Value().Unwrap());
        Assert.Equal(50f, percent.Resolve(200f).Unwrap());
        Assert.Equal(0f, StyleLength.Percent(0f).Resolve(200f).Unwrap());

        Assert.True(StyleLength.Percent(float.NaN).IsUndefined());
        Assert.True(StyleLength.Percent(float.PositiveInfinity).IsUndefined());
        Assert.True(StyleLength.Percent(float.NegativeInfinity).IsUndefined());
    }

    [Fact]
    public void EqualityDistinguishesValueAndUnit()
    {
        Assert.True(StyleLength.Points(10f) == StyleLength.Points(10f));
        Assert.Equal(StyleLength.Points(10f).GetHashCode(), StyleLength.Points(10f).GetHashCode());
        Assert.True(StyleLength.Points(10f) != StyleLength.Points(11f));
        Assert.False(StyleLength.Points(10f).Equals(StyleLength.Percent(10f)));
        Assert.False(StyleLength.Points(10f).InexactEquals(StyleLength.Percent(10f)));
        Assert.True(StyleLength.Percent(10f) == StyleLength.Percent(10f));
    }

    [Fact]
    public void InexactEqualsMatchesUpstreamTolerance()
    {
        Assert.True(StyleLength.Points(10f).InexactEquals(StyleLength.Points(10.00005f)));
        Assert.True(StyleLengthExtensions.InexactEquals(StyleLength.Points(10f), StyleLength.Points(10.00005f)));
        Assert.False(StyleLength.Points(10f) == StyleLength.Points(10.00005f));
        Assert.False(StyleLength.Points(10f).InexactEquals(StyleLength.Points(10.001f)));

        Assert.True(StyleLength.Undefined().InexactEquals(StyleLength.Undefined()));
        Assert.False(StyleLength.Undefined().InexactEquals(StyleLength.Points(10f)));
        Assert.True(StyleLength.Percent(50f).InexactEquals(StyleLength.Percent(50.00005f)));
    }

    [Fact]
    public void YGValueConversionPreservesUnitAndValue()
    {
        Assert.Equal(new YGValue(12f, Unit.Point), (YGValue)StyleLength.Points(12f));
        Assert.Equal(new YGValue(50f, Unit.Percent), (YGValue)StyleLength.Percent(50f));
        Assert.Equal(YogaValue.YGValueUndefined, (YGValue)StyleLength.Undefined());
        Assert.NotEqual(new YGValue(12f, Unit.Point), new YGValue(12f, Unit.Percent));
    }
}
