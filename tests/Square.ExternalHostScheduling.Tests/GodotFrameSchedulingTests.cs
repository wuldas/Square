using Godot;
using Square.Hosting;
using Square.Platform.Godot;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Square.ExternalHostScheduling.Tests;

public sealed class GodotFrameSchedulingTests
{
    [Fact]
    public void IdleEngineFramesDoNotTickQuerySessionOrScanScene()
    {
        var control = Attach();
        control._Process(0);
        var session = control.Session!;
        var queries = session.DelayQueries;
        var sceneQueries = control.SceneQueries;
        var metrics = GodotPlatformHost.LastCreated!.MetricUpdates;
        for (var i = 0; i < 1000; i++) control._Process(1d / 60);
        Assert.Equal(1, session.Ticks);
        Assert.Equal(queries, session.DelayQueries);
        Assert.Equal(sceneQueries, control.SceneQueries);
        Assert.Equal(metrics, GodotPlatformHost.LastCreated.MetricUpdates);
        control.Detach();
    }

    [Fact]
    public void PositiveDeadlineDoesNotTickUntilDueAndZeroBringsItForward()
    {
        var control = Attach();
        var session = control.Session!;
        session.DelayAfterTick = TimeSpan.FromDays(1);
        control._Process(0);
        for (var i = 0; i < 1000; i++) control._Process(1d / 60);
        Assert.Equal(1, session.Ticks);
        session.DelayAfterTick = null;
        session.RequestFrame(TimeSpan.Zero);
        control.DrainDeferred();
        control._Process(0);
        Assert.Equal(2, session.Ticks);
        control.Detach();
    }

    [Fact]
    public async Task BackgroundWakeupsAreCoalescedAndOnlyQueryDelayOnMainThread()
    {
        var control = Attach();
        control._Process(0);
        var session = control.Session!;
        var queries = session.DelayQueries;
        await Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++) session.RequestFrame(TimeSpan.Zero);
        });
        var mainThread = System.Environment.CurrentManagedThreadId;
        Assert.Equal(queries, session.DelayQueries);
        Assert.Equal(1, control.DeferredCount);
        control.DrainDeferred();
        Assert.Equal(mainThread, session.LastQueryThread);
        control._Process(0);
        Assert.Equal(2, session.Ticks);
        control.Detach();
    }

    [Fact]
    public void InFrameAndQueryNotificationsDoNotLeaveAnIdleWakeupLoop()
    {
        var control = Attach();
        var session = control.Session!;
        session.DuringTick = () => session.RequestFrame(TimeSpan.Zero);
        session.DuringQuery = () => session.RequestFrame(session.Delay);
        control._Process(0);
        Assert.Equal(0, control.DeferredCount);
        for (var i = 0; i < 100; i++) control._Process(0);
        Assert.Equal(1, session.Ticks);
        control.Detach();
    }

    [Fact]
    public void HiddenControlCancelsWorkAndBecomesProcessableWithAResumeFrame()
    {
        var control = Attach();
        var session = control.Session!;
        control.ChangeVisibility(false);
        Assert.True(session.IsSuspended);
        session.RequestFrame(TimeSpan.Zero);
        control.DrainDeferred();
        control._Process(0);
        Assert.Equal(0, session.Ticks);
        control.ChangeVisibility(true);
        control.DrainDeferred();
        Assert.False(session.IsSuspended);
        control._Process(0);
        Assert.Equal(1, session.Ticks);
        control.Detach();
    }

    [Fact]
    public void PauseAndResizeSignalsWorkWhileIdleAndDetachIgnoresQueuedWakeup()
    {
        var control = Attach();
        control._Process(0);
        var session = control.Session!;
        var updates = GodotPlatformHost.LastCreated!.MetricUpdates;
        control.Resize(new Vector2(800, 600));
        Assert.True(GodotPlatformHost.LastCreated.MetricUpdates > updates);
        control.Processable = false;
        control._Notification((int)Control.NotificationPaused);
        Assert.True(session.IsSuspended);
        control.Processable = true;
        control._Notification((int)Control.NotificationUnpaused);
        control.DrainDeferred();
        control._Process(0);
        Assert.Equal(2, session.Ticks);
        session.RequestFrame(TimeSpan.Zero);
        control.Detach();
        control.DrainDeferred();
        control._Process(0);
        Assert.Null(control.Session);
        Assert.Equal(2, session.Ticks);
    }

    [Fact]
    public void HiddenWindowPausesVisibleControlAndVisibilitySignalResumesIt()
    {
        var control = Attach();
        var session = control.Session!;
        var window = control.GetWindow();
        window.ChangeVisibility(false);
        Assert.True(control.Visible);
        Assert.True(session.IsSuspended);
        session.RequestFrame(TimeSpan.Zero);
        control.DrainDeferred();
        control._Process(0);
        Assert.Equal(0, session.Ticks);
        window.ChangeVisibility(true);
        control.DrainDeferred();
        Assert.False(session.IsSuspended);
        control._Process(0);
        Assert.Equal(1, session.Ticks);
        control.Detach();
    }

    [Fact]
    public void MinimizedWindowPausesAnimationAndWindowResizeRestoresFrame()
    {
        var control = Attach();
        var session = control.Session!;
        session.DelayAfterTick = TimeSpan.Zero;
        control._Process(0);
        var window = control.GetWindow();
        window.Mode = Window.ModeEnum.Minimized;
        window.ExitFocus();
        Assert.True(window.Visible);
        Assert.True(control.Visible);
        Assert.True(session.IsSuspended);
        for (var i = 0; i < 100; i++) control._Process(0);
        Assert.Equal(1, session.Ticks);
        window.Mode = Window.ModeEnum.Windowed;
        window.Resize();
        control.DrainDeferred();
        Assert.False(session.IsSuspended);
        control._Process(0);
        Assert.Equal(2, session.Ticks);
        control.Detach();
    }

    [Fact]
    public void NormalWindowFocusLossKeepsAnimationAndFocusGainResumesMinimizedWindow()
    {
        var control = Attach();
        var session = control.Session!;
        session.DelayAfterTick = TimeSpan.Zero;
        var window = control.GetWindow();
        window.ExitFocus();
        Assert.False(session.IsSuspended);
        control._Process(0);
        Assert.Equal(1, session.Ticks);
        window.Mode = Window.ModeEnum.Minimized;
        window.ExitFocus();
        Assert.True(session.IsSuspended);
        window.Mode = Window.ModeEnum.Windowed;
        window.EnterFocus();
        control.DrainDeferred();
        Assert.False(session.IsSuspended);
        control._Process(0);
        Assert.Equal(2, session.Ticks);
        control.Detach();
    }

    private static SquareControl Attach()
    {
        var control = new SquareControl();
        control.Attach(new AppWindow());
        return control;
    }
}
