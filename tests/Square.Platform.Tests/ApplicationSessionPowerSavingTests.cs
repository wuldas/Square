using Square.Controls;
using Square.Events;
using Square.Hosting;
using Square.UI;
using Xunit;

namespace Square.Platform.Tests;

public sealed partial class ApplicationSessionTests
{
    [Fact]
    public void IdleSessionHasNoDeadlineAndInvalidationWakesWithoutSelfWakeDuringPaint()
    {
        var root = new View();
        var window = new AppWindow("idle", 64, 48);
        window.Load(root);
        var host = new FakeHost();
        using var session = new ApplicationSession(window, host);
        session.Attach();
        session.ProcessFrame();
        Assert.Null(session.NextFrameDelay);
        Assert.Null(host.FrameDelayQuery!());

        var wakeCount = host.WakeCount;
        root.InvalidatePaint();
        Assert.True(host.WakeCount > wakeCount);
        Assert.Equal(TimeSpan.Zero, session.NextFrameDelay);
        wakeCount = host.WakeCount;
        var presents = host.PresentCount;
        session.Tick();
        Assert.Equal(presents + 1, host.PresentCount);
        Assert.Equal(wakeCount, host.WakeCount);
        Assert.Null(session.NextFrameDelay);
    }

    [Fact]
    public void BackgroundDispatcherWorkWakesIdleSessionWithoutForcingAPresent()
    {
        var window = new AppWindow("dispatcher", 64, 48);
        window.Load(new View());
        var host = new FakeHost();
        using var session = new ApplicationSession(window, host);
        session.Attach();
        session.ProcessFrame();
        Assert.Null(session.NextFrameDelay);
        var completed = false;
        var wakeCount = host.WakeCount;
        var worker = new Thread(() => window.Dispatcher.Invoke(() => completed = true));
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
        Assert.True(host.WakeCount > wakeCount);
        Assert.Equal(TimeSpan.Zero, session.NextFrameDelay);
        var presents = host.PresentCount;
        session.Tick();
        Assert.True(completed);
        Assert.Equal(presents, host.PresentCount);
        Assert.Null(session.NextFrameDelay);
    }

    [Fact]
    public void ReconcilerQueueWakesIdleSessionAndDetachingRemovesWakeSources()
    {
        var root = new View();
        var window = new AppWindow("reconciler", 64, 48);
        window.Load(root);
        var host = new FakeHost();
        using var session = new ApplicationSession(window, host);
        session.Attach();
        session.ProcessFrame();
        var wakeCount = host.WakeCount;
        ((UIDocument)window.Document).Context.Reconciler.ScheduleUpdate(() => root.Style.Set("background", "#0000ff"));
        Assert.True(host.WakeCount > wakeCount);
        session.Tick();
        Assert.Equal(255, host.LastFrame!.GetPixel(10, 10)[0]);
        session.Detach();
        wakeCount = host.WakeCount;
        window.Dispatcher.Invoke(() => { });
        ((UIDocument)window.Document).Context.Reconciler.ScheduleUpdate(() => { });
        Assert.Equal(wakeCount, host.WakeCount);
        Assert.Null(host.FrameDelayQuery);
        Assert.Null(session.NextFrameDelay);
    }

    [Fact]
    public void FutureControlFrameUsesItsDeadlineAndHiddenControlDoesNotPoll()
    {
        var root = new ScheduledView();
        var window = new AppWindow("deadline", 64, 48);
        window.Load(root);
        using var session = new ApplicationSession(window, new FakeHost());
        session.Attach();
        session.ProcessFrame();
        root.DispatchEvent(StandardEvents.CreateRequestFrame(TimeSpan.FromMinutes(1)));
        Assert.InRange(session.NextFrameDelay!.Value.TotalSeconds, 50, 60);
        session.Tick();
        Assert.Equal(0, root.FrameCount);
        Assert.InRange(session.NextFrameDelay!.Value.TotalSeconds, 50, 60);

        root.IsVisible = false;
        session.Tick();
        Assert.Null(session.NextFrameDelay);
        root.IsVisible = true;
        session.Tick();
        Assert.NotNull(session.NextFrameDelay);
    }

    [Fact]
    public void FocusedInputUsesCaretDeadlineInsteadOfContinuousFrames()
    {
        var input = new Input();
        var window = new AppWindow("caret", 64, 48);
        window.Load(input);
        using var session = new ApplicationSession(window, new FakeHost());
        session.Attach();
        input.Focus();
        session.Tick();
        Assert.InRange(session.NextFrameDelay!.Value.TotalMilliseconds, 1, 700);
        input.Unfocus();
        session.Tick();
        Assert.Null(session.NextFrameDelay);
    }

    [Fact]
    public void PowerSavingCanBeDisabledAndMinimizedWorkStillRunsWithoutDrawing()
    {
        var window = new AppWindow("minimized", 64, 48);
        window.Load(new View());
        var host = new FakeHost();
        using var session = new ApplicationSession(window, host);
        session.Attach();
        session.ProcessFrame();
        window.PowerSavingEnabled = false;
        session.Tick();
        Assert.Equal(TimeSpan.Zero, session.NextFrameDelay);
        window.PowerSavingEnabled = true;
        session.Tick();
        Assert.Null(session.NextFrameDelay);

        host.ChangeState(AppWindowState.Minimized);
        var presents = host.PresentCount;
        var completed = false;
        window.Dispatcher.Invoke(() => { completed = true; window.RequestRender(); });
        session.Tick();
        Assert.True(completed);
        Assert.Equal(presents, host.PresentCount);
        Assert.Null(session.NextFrameDelay);
        host.ChangeState(AppWindowState.Normal);
        Assert.Equal(TimeSpan.Zero, session.NextFrameDelay);
        session.Tick();
        Assert.True(host.PresentCount > presents);
    }

    [Fact]
    public void StartingAnimationDuringAControlDeadlineWaitDoesNotConsumeTheSleepInterval()
    {
        var root = new ScheduledView();
        var window = new AppWindow("animation-after-sleep", 64, 48);
        window.Load(root);
        window.LoadGlobalCssText("@keyframes fade { from { opacity: 0; } to { opacity: 1; } } " +
            ".animated { animation: fade 1s linear; }");
        using var session = new ApplicationSession(window, new FakeHost());
        session.Attach();
        session.Tick();
        root.DispatchEvent(StandardEvents.CreateRequestFrame(TimeSpan.FromMinutes(1)));
        Assert.True(session.NextFrameDelay > TimeSpan.FromSeconds(50));
        Thread.Sleep(100);

        root.ClassList.Add("animated");
        session.ProcessFrame();
        session.Tick();

        Assert.Equal("0", root.Style.Get("opacity"));
        Assert.Equal(TimeSpan.Zero, session.NextFrameDelay);
    }

    [Fact]
    public void CssAnimationDemandsNativeFramesUntilItIsRemoved()
    {
        var root = new View();
        root.ClassList.Add("animated");
        var window = new AppWindow("animation-demand", 64, 48);
        window.Load(root);
        window.LoadGlobalCssText("@keyframes fade { from { opacity: 0; } to { opacity: 1; } } " +
            ".animated { animation: fade 60s linear; }");
        using var session = new ApplicationSession(window, new FakeHost());
        session.Attach();
        session.Tick();
        Assert.Equal(TimeSpan.Zero, session.NextFrameDelay);
        root.ClassList.Remove("animated");
        session.Tick();
        Assert.Null(session.NextFrameDelay);
    }
}
