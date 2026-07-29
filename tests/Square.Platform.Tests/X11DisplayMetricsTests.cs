using Square.Graphics;
using Square.Platform.X11;
using Xunit;

namespace Square.Platform.Tests;

public class X11DisplayMetricsTests
{
    [Fact]
    public void SelectMonitorUsesLargestWindowIntersection()
    {
        X11MonitorMetrics[] monitors =
        [
            new(new Rect(0, 0, 1920, 1080), 509, 286, 60, true),
            new(new Rect(1920, 0, 2560, 1440), 597, 336, 144)
        ];

        Assert.Equal(1, X11DisplayMetrics.SelectMonitor(new Rect(1700, 100, 800, 600), monitors));
    }

    [Fact]
    public void SelectMonitorUsesNearestThenPrimaryWhenWindowDoesNotIntersect()
    {
        X11MonitorMetrics[] monitors =
        [
            new(new Rect(0, 0, 100, 100), 300, 200, 60),
            new(new Rect(200, 0, 100, 100), 300, 200, 60, true)
        ];

        Assert.Equal(1, X11DisplayMetrics.SelectMonitor(new Rect(140, 40, 20, 20), monitors));
        Assert.Equal(-1, X11DisplayMetrics.SelectMonitor(Rect.Empty, []));
    }

    [Fact]
    public void SelectedMonitorPoliciesResolveDpiAndRefreshFallbacks()
    {
        var monitor = new X11MonitorMetrics(new Rect(0, 0, 2560, 1440), 597, 336, 144);

        Assert.Equal(120, X11DisplayMetrics.ResolveMonitorDpi("Xft.dpi: 120", monitor), 6);
        Assert.Equal(144, X11DisplayMetrics.ResolveMonitorRefreshRate(monitor), 6);
        Assert.Equal(60, X11DisplayMetrics.ResolveMonitorRefreshRate(monitor with { RefreshRate = 0 }), 6);
    }

    [Theory]
    [InlineData("Xft.dpi:\t144\nXft.antialias: 1", 144)]
    [InlineData("Xft*dpi: 120.5", 120.5)]
    [InlineData("xft.dpi: 192", 192)]
    public void TryParseXftDpiAcceptsCommonResourceForms(string resources, double expected)
    {
        Assert.True(X11DisplayMetrics.TryParseXftDpi(resources, out var actual));
        Assert.Equal(expected, actual, 6);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Xft.dpi: nope")]
    [InlineData("Xft.dpi: 0")]
    [InlineData("Other.dpi: 144")]
    public void TryParseXftDpiRejectsMissingOrInvalidValues(string? resources)
    {
        Assert.False(X11DisplayMetrics.TryParseXftDpi(resources, out _));
    }

    [Fact]
    public void ResolveDpiPrefersXftDpiOverPhysicalDimensions()
    {
        var dpi = X11DisplayMetrics.ResolveDpi("Xft.dpi: 144", 1920, 1080, 508, 286);

        Assert.Equal(144, dpi, 6);
    }

    [Fact]
    public void CalculatePhysicalDpiAveragesBothAxes()
    {
        var dpi = X11DisplayMetrics.CalculatePhysicalDpi(1920, 1080, 508, 285);

        var expected = (1920d * 25.4 / 508 + 1080d * 25.4 / 285) / 2;
        Assert.Equal(expected, dpi, 6);
    }

    [Fact]
    public void CalculatePhysicalDpiUsesValidAxisAndDefaultsWhenUnavailable()
    {
        Assert.Equal(96, X11DisplayMetrics.CalculatePhysicalDpi(1920, 0, 508, 0), 6);
        Assert.Equal(96, X11DisplayMetrics.CalculatePhysicalDpi(0, 0, 0, 0), 6);
    }

    [Theory]
    [InlineData(96, 1)]
    [InlineData(144, 1.5)]
    [InlineData(192, 2)]
    [InlineData(0, 1)]
    public void DpiToScaleNormalizesDpi(double dpi, float expected)
    {
        Assert.Equal(expected, X11DisplayMetrics.DpiToScale(dpi), 6);
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(59.94, 59.94)]
    [InlineData(144, 144)]
    [InlineData(0, 60)]
    [InlineData(10, 60)]
    [InlineData(500, 60)]
    [InlineData(double.NaN, 60)]
    public void NormalizeRefreshRateRejectsImplausibleValues(double value, double expected)
    {
        Assert.Equal(expected, X11DisplayMetrics.NormalizeRefreshRate(value), 6);
    }

    [Theory]
    [InlineData(60, 1_000_000, 16_667)]
    [InlineData(120, 1_000_000, 8_333)]
    [InlineData(144, 1_000_000, 6_944)]
    [InlineData(0, 1_000_000, 16_667)]
    public void FrameIntervalTicksUsesNormalizedRefreshRate(
        double refreshRate, long frequency, long expected)
    {
        Assert.Equal(expected, X11DisplayMetrics.FrameIntervalTicks(refreshRate, frequency));
    }

    [Fact]
    public void FrameIntervalTicksRejectsInvalidFrequency()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => X11DisplayMetrics.FrameIntervalTicks(60, 0));
    }

    [Theory]
    [InlineData(0, 100, 16, 116)]
    [InlineData(116, 100, 16, 116)]
    [InlineData(116, 116, 16, 132)]
    [InlineData(116, 117, 16, 132)]
    [InlineData(116, 148, 16, 164)]
    public void NextFrameDeadlineAccumulatesAndSkipsMissedFrames(
        long deadline, long now, long interval, long expected)
    {
        Assert.Equal(expected, X11DisplayMetrics.NextFrameDeadline(deadline, now, interval));
    }

    [Fact]
    public void NextFrameDeadlineRejectsInvalidInterval()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => X11DisplayMetrics.NextFrameDeadline(0, 0, 0));
    }
}
