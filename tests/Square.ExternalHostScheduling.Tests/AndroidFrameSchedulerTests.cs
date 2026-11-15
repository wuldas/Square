using Android.OS;
using Android.Views;
using Square.Platform.Android;
using Xunit;

namespace Square.ExternalHostScheduling.Tests;

public sealed class AndroidFrameSchedulerTests
{
    public AndroidFrameSchedulerTests()
    {
        SystemClock.Now = 1000;
        Choreographer.Instance = new();
    }

    [Fact]
    public void DelayedRequestWaitsWithoutPollingAndStopsWhenIdle()
    {
        var frames = 0;
        using var scheduler = new AndroidFrameScheduler(() => { frames++; return false; });
        scheduler.RequestFrame(TimeSpan.FromMilliseconds(500));
        for (; SystemClock.Now < 1500; SystemClock.Now++) Choreographer.Instance.Dispatch();
        Assert.Equal(0, frames);
        Assert.Equal(1, Choreographer.Instance.Posts);
        Choreographer.Instance.Dispatch();
        Assert.Equal(1, frames);
        Assert.False(scheduler.IsFramePending);
    }

    [Fact]
    public void RequestsKeepEarliestAbsoluteDeadlineAndOnlyOnePendingCallback()
    {
        using var scheduler = new AndroidFrameScheduler(() => false);
        scheduler.RequestFrame(TimeSpan.FromMilliseconds(500));
        SystemClock.Now = 1100;
        scheduler.RequestFrame(TimeSpan.FromMilliseconds(600));
        Assert.Equal(1, Choreographer.Instance.Posts);
        scheduler.RequestFrame(TimeSpan.FromMilliseconds(200));
        Assert.Equal(1300, Choreographer.Instance.Deadline);
        Assert.Equal(2, Choreographer.Instance.Posts);
        Assert.Equal(1, Choreographer.Instance.Removes);
        scheduler.RequestFrame();
        Assert.Equal(1100, Choreographer.Instance.Deadline);
    }

    [Fact]
    public void FractionalMillisecondsRoundUpAndNullCancels()
    {
        using var scheduler = new AndroidFrameScheduler(() => false);
        scheduler.RequestFrame(TimeSpan.FromTicks(1));
        Assert.Equal(1001, Choreographer.Instance.Deadline);
        scheduler.RequestFrame(null);
        Assert.False(scheduler.IsFramePending);
        Assert.Null(Choreographer.Instance.Pending);
    }

    [Fact]
    public void PauseAndDisposeCancelDelayedCallbacksAndResumeDoesNotInventWork()
    {
        var frames = 0;
        var scheduler = new AndroidFrameScheduler(() => { frames++; return false; });
        scheduler.RequestFrame(TimeSpan.FromSeconds(1));
        scheduler.Pause();
        scheduler.RequestFrame();
        Assert.Null(Choreographer.Instance.Pending);
        scheduler.Resume();
        Assert.False(scheduler.IsFramePending);
        scheduler.RequestFrame();
        scheduler.Dispose();
        SystemClock.Now += 2000;
        Choreographer.Instance.Dispatch();
        Assert.Equal(0, frames);
        scheduler.RequestFrame();
        Assert.Null(Choreographer.Instance.Pending);
    }

    [Fact]
    public void ExistingBoolCallbackCanContinueAtNativeCadence()
    {
        var frames = 0;
        using var scheduler = new AndroidFrameScheduler(() => ++frames < 2);
        scheduler.RequestFrame();
        Choreographer.Instance.Dispatch();
        Assert.True(scheduler.IsFramePending);
        Choreographer.Instance.Dispatch();
        Assert.Equal(2, frames);
        Assert.False(scheduler.IsFramePending);
    }

    [Fact]
    public void FrameTailCanReplaceInFrameRequestWithDelayOrIdle()
    {
        AndroidFrameScheduler? scheduler = null;
        scheduler = new AndroidFrameScheduler(() =>
        {
            scheduler!.RequestFrame();
            scheduler.RequestFrame(null);
            scheduler.RequestFrame(TimeSpan.FromSeconds(1));
            return false;
        });
        using (scheduler)
        {
            scheduler.RequestFrame();
            Choreographer.Instance.Dispatch();
            Assert.Equal(2000, Choreographer.Instance.Deadline);
            scheduler.RequestFrame(null);
            Assert.Null(Choreographer.Instance.Pending);
        }
    }
}
