using Square.Graphics;
using Square.Hosting;
using Square.Platform.MacOS;
using Xunit;

namespace Square.Platform.Tests;

public sealed class MacOSHostPolicyTests
{
    public static TheoryData<bool, bool, nuint, AppWindowState> WindowStates => new()
    {
        { false, false, 0, AppWindowState.Normal },
        { false, true, 0, AppWindowState.Maximized },
        { false, false, 1 << 14, AppWindowState.Maximized },
        { true, true, 1 << 14, AppWindowState.Minimized }
    };

    [Theory]
    [MemberData(nameof(WindowStates))]
    public void ResolveStateUsesCocoaWindowFlags(
        bool miniaturized,
        bool zoomed,
        nuint styleMask,
        AppWindowState expected)
    {
        Assert.Equal(expected, MacOSHostPolicy.ResolveState(miniaturized, zoomed, styleMask));
    }

    [Fact]
    public void MissingFrameQueryKeepsTheLegacyContinuousCadence()
    {
        var scheduler = new MacOSFrameScheduler();

        Assert.Equal(TimeSpan.FromMilliseconds(16), scheduler.GetWaitDelay(TimeSpan.Zero, null));
        Assert.Equal(TimeSpan.FromMilliseconds(6), scheduler.GetWaitDelay(TimeSpan.FromMilliseconds(10), null));
        Assert.True(scheduler.IsFrameDue(TimeSpan.FromMilliseconds(16)));
    }

    [Fact]
    public void IdleFrameQueryWaitsIndefinitelyAndCancelsPreviousDeadline()
    {
        var scheduler = new MacOSFrameScheduler();
        scheduler.GetWaitDelay(TimeSpan.Zero, () => TimeSpan.FromMilliseconds(100));

        Assert.Null(scheduler.GetWaitDelay(TimeSpan.FromMilliseconds(10), () => null));
        Assert.False(scheduler.IsFrameDue(TimeSpan.FromHours(1)));
    }

    [Fact]
    public void ZeroFrameDelayWaitsForTheNormalFrameCadence()
    {
        var scheduler = new MacOSFrameScheduler();

        Assert.Equal(MacOSFrameScheduler.FrameInterval, scheduler.GetWaitDelay(TimeSpan.Zero, () => TimeSpan.Zero));
        Assert.False(scheduler.IsFrameDue(TimeSpan.Zero));
        Assert.True(scheduler.IsFrameDue(MacOSFrameScheduler.FrameInterval));
    }

    [Fact]
    public void NativeEventBatchesDoNotPostponeDelayedFrames()
    {
        var scheduler = new MacOSFrameScheduler();
        Func<TimeSpan?> query = () => TimeSpan.FromMilliseconds(250);

        Assert.Equal(TimeSpan.FromMilliseconds(250), scheduler.GetWaitDelay(TimeSpan.Zero, query));
        Assert.Equal(TimeSpan.FromMilliseconds(150), scheduler.GetWaitDelay(TimeSpan.FromMilliseconds(100), query));
        Assert.Equal(TimeSpan.Zero, scheduler.GetWaitDelay(TimeSpan.FromMilliseconds(250), query));
        Assert.True(scheduler.IsFrameDue(TimeSpan.FromMilliseconds(250)));

        scheduler.FrameCompleted();
        Assert.False(scheduler.IsFrameDue(TimeSpan.FromMilliseconds(250)));
        Assert.Equal(TimeSpan.FromMilliseconds(250), scheduler.GetWaitDelay(TimeSpan.FromMilliseconds(250), query));
    }

    [Fact]
    public void EarlierFrameDemandShortensAnExistingDeadline()
    {
        var scheduler = new MacOSFrameScheduler();
        scheduler.GetWaitDelay(TimeSpan.Zero, () => TimeSpan.FromSeconds(5));

        Assert.Equal(MacOSFrameScheduler.FrameInterval,
            scheduler.GetWaitDelay(TimeSpan.FromSeconds(1), () => TimeSpan.Zero));
    }

    [Fact]
    public void VeryLongDelaysDoNotOverflowTheDeadline()
    {
        var scheduler = new MacOSFrameScheduler();

        Assert.Equal(TimeSpan.MaxValue - TimeSpan.FromSeconds(1),
            scheduler.GetWaitDelay(TimeSpan.FromSeconds(1), () => TimeSpan.MaxValue));
    }

    [Fact]
    public void WakeCoalescesConcurrentRequestsAndInterruptsIndefiniteWait()
    {
        var scheduler = new MacOSFrameScheduler();
        var posts = 0;
        Parallel.For(0, 256, _ =>
        {
            if (scheduler.RequestWake()) Interlocked.Increment(ref posts);
        });

        Assert.Equal(1, posts);
        Assert.Equal(TimeSpan.Zero, scheduler.GetWaitDelay(TimeSpan.Zero, () => null));
        Assert.True(scheduler.ConsumeWake());
        Assert.False(scheduler.ConsumeWake());
        Assert.Null(scheduler.GetWaitDelay(TimeSpan.Zero, () => null));
    }

    [Fact]
    public void WakeDuringTickSurvivesFrameCompletion()
    {
        var scheduler = new MacOSFrameScheduler();
        scheduler.RequestWake();
        Assert.True(scheduler.ConsumeWake());
        scheduler.FrameCompleted();

        Assert.True(scheduler.RequestWake());
        Assert.True(scheduler.HasPendingWake);
        Assert.Equal(TimeSpan.Zero, scheduler.GetWaitDelay(TimeSpan.Zero, () => null));
    }

    [Fact]
    public void WakeRacingWithConsumptionIsEitherObservedOrRemainsPending()
    {
        for (var i = 0; i < 100; i++)
        {
            var scheduler = new MacOSFrameScheduler();
            var observed = false;
            Parallel.Invoke(
                () => observed = scheduler.ConsumeWake(),
                () => scheduler.RequestWake());

            Assert.True(observed || scheduler.HasPendingWake);
        }
    }

    [Fact]
    public void WakeBeforeShowUsesNoAppKitAndDoesNotInvokeUiCallbacks()
    {
        using var host = new MacOSHost(new PlatformHostCreateInfo { Title = "Wake test" });
        host.FrameDelayQuery = () => throw new InvalidOperationException("Only the UI pump may query demand.");
        host.Tick += () => throw new InvalidOperationException("Wake must not execute Tick inline.");

        Parallel.For(0, 256, _ => host.Wake());
        host.Dispose();
        host.Wake();
        Assert.Null(host.FrameDelayQuery);
    }

    [Fact]
    public void TextInputRectConvertsTopLeftCoordinatesToCocoaCoordinates()
    {
        var result = MacOSHostPolicy.ToCocoaTextInputRect(
            new Rect(25, 20, 2, 20),
            new Size(800, 600));

        Assert.Equal(new Rect(25, 560, 2, 20), result);
    }

    [Fact]
    public void TextInputRectKeepsANonEmptyNativeInputClient()
    {
        var result = MacOSHostPolicy.ToCocoaTextInputRect(Rect.Empty, new Size(800, 600));

        Assert.Equal(1, result.Width);
        Assert.Equal(1, result.Height);
    }
}
