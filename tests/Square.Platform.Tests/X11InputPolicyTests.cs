using System.Text;
using Square.Graphics;
using Square.Platform.X11;
using Xunit;

namespace Square.Platform.Tests;

public class X11InputPolicyTests
{
    [Fact]
    public void EventPumpYieldsAfterBoundedBatch()
    {
        Assert.False(X11InputPolicy.ShouldYieldEventPump(X11InputPolicy.MaxEventsPerPump - 1));
        Assert.True(X11InputPolicy.ShouldYieldEventPump(X11InputPolicy.MaxEventsPerPump));
    }

    [Fact]
    public void SelectStylePrefersPositionAndFallsBackToNothing()
    {
        Assert.Equal(X11InputPolicy.PreferredStyle,
            X11InputPolicy.SelectStyle([X11InputPolicy.FallbackStyle, X11InputPolicy.PreferredStyle]));
        Assert.Equal(X11InputPolicy.FallbackStyle,
            X11InputPolicy.SelectStyle([X11InputPolicy.FallbackStyle]));
        Assert.Equal(0, X11InputPolicy.SelectStyle([X11Api.XIMStatusNothing]));
    }

    [Fact]
    public void DecodeCommittedTextKeepsUtf8CommitAtomic()
    {
        const string committed = "A\u4F60\u597D\U0001F642";
        var bytes = Encoding.UTF8.GetBytes(committed);

        var text = X11InputPolicy.DecodeCommittedText(bytes, bytes.Length, X11Api.XLookupBoth);

        Assert.Equal(committed, text);
        Assert.True(X11InputPolicy.HasDispatchableText(text));
        Assert.False(X11InputPolicy.HasDispatchableText("\r\n"));
    }

    [Theory]
    [InlineData(X11Api.XLookupNone)]
    [InlineData(X11Api.XLookupKeySym)]
    public void DecodeCommittedTextRejectsNonCharacterStatuses(int status)
    {
        Assert.Null(X11InputPolicy.DecodeCommittedText([0x41], 1, status));
    }

    [Fact]
    public void CaretSpotConvertsScreenLogicalBottomToClientPhysicalCoordinates()
    {
        var spot = X11InputPolicy.ToClientPhysicalSpot(
            new Rect(25, 20, 2, 20), 1.5f);

        Assert.Equal(new X11CaretSpot(38, 60), spot);
    }

    [Fact]
    public void CaretSpotClampsToXPointRange()
    {
        var spot = X11InputPolicy.ToClientPhysicalSpot(
            new Rect(100_000, -100_000, 1, 1), 2f);

        Assert.Equal(short.MaxValue, spot.X);
        Assert.Equal(short.MinValue, spot.Y);
    }
}
