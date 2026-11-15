#if PLATFORM_WIN32
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Square.Platform.Win32;
using Xunit;

namespace Square.Platform.Tests;

public sealed class Win32FrameSchedulingTests
{
    [Fact]
    public void WakeBeforeShowRunsTickWithoutRenderRequest()
    {
        var ticks = 0;
        var renderRequests = 0;
        RunHost(host =>
        {
            var uiThread = Environment.CurrentManagedThreadId;
            host.RenderRequested += () => renderRequests++;
            host.Tick += () =>
            {
                Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
                ticks++;
                host.Close();
            };
            host.Wake();
        }, () =>
        {
            Assert.Equal(1, ticks);
            Assert.Equal(0, renderRequests);
        });
    }

    [Fact]
    public void IdleMinimizedHostProcessesBackgroundWake()
    {
        using var queried = new ManualResetEventSlim();
        var ticks = 0;
        Task? producer = null;
        RunHost(host =>
        {
            var uiThread = Environment.CurrentManagedThreadId;
            host.FrameDelayQuery = () =>
            {
                Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
                queried.Set();
                return null;
            };
            host.Tick += () =>
            {
                Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
                Assert.Equal(AppWindowState.Minimized, host.State);
                Interlocked.Increment(ref ticks);
                host.Close();
            };
            producer = Task.Run(() =>
            {
                Assert.True(queried.Wait(TimeSpan.FromSeconds(3)));
                // Observe a genuine idle wait before supplying Dispatcher work.
                Thread.Sleep(100);
                Assert.Equal(0, Volatile.Read(ref ticks));
                host.Wake();
            });
        }, () =>
        {
            producer!.GetAwaiter().GetResult();
            Assert.Equal(1, ticks);
        }, minimize: true);
    }

    [Fact]
    public void WakeDuringTickRemainsPendingForNextTick()
    {
        var ticks = 0;
        RunHost(host =>
        {
            host.Tick += () =>
            {
                if (++ticks == 1)
                    Task.Run(host.Wake).GetAwaiter().GetResult();
                else
                    host.Close();
            };
            host.Wake();
        }, () => Assert.Equal(2, ticks));
    }

    [Fact]
    public void PositiveDelayWaitsForDeadlineWithoutIntermediateTicks()
    {
        var clock = new Stopwatch();
        var ticks = 0;
        var target = TimeSpan.FromMilliseconds(100);
        RunHost(host =>
        {
            clock.Start();
            host.FrameDelayQuery = () => target - clock.Elapsed;
            host.Tick += () =>
            {
                Assert.True(clock.Elapsed >= target);
                ticks++;
                host.Close();
            };
        }, () => Assert.Equal(1, ticks));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContinuousDemandKeepsNativeFrameRateLimit(bool provideQuery)
    {
        var clock = new Stopwatch();
        var ticks = 0;
        RunHost(host =>
        {
            host.FrameDelayQuery = provideQuery ? () => TimeSpan.Zero : null;
            host.Tick += () =>
            {
                if (++ticks == 1) clock.Start();
                if (ticks == 4) host.Close();
            };
        }, () =>
        {
            Assert.Equal(4, ticks);
            Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(35));
        });
    }

    private static void RunHost(Action<Win32Host> prepare, Action verify, bool minimize = false)
    {
        if (!OperatingSystem.IsWindows()) return;
        Exception? failure = null;
        // A separate message queue keeps WM_QUIT isolated from other window tests.
        var thread = new Thread(() =>
        {
            try
            {
                using var host = new Win32Host(new PlatformHostCreateInfo
                {
                    Title = "Square frame scheduling test", Width = 160, Height = 120
                });
                host.FrameDelayQuery = () => null;
                var timedOut = 0;
                using var timeout = new Timer(_ =>
                {
                    Interlocked.Exchange(ref timedOut, 1);
                    host.Wake();
                }, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
                host.Tick += () =>
                {
                    if (Volatile.Read(ref timedOut) != 0) host.Close();
                };
                prepare(host);
                host.Show();
                if (minimize) host.Minimize();
                host.PumpEvents();
                Assert.Equal(0, Volatile.Read(ref timedOut));
                verify();
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Native event loop did not exit");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
#endif
