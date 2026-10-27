using Square.Controls;
using Square.Hosting;
using Xunit;

namespace Square.DevTools.Tests;

[Collection("ConsoleCapture")]
public sealed class CdpLogLifecycleTests
{
    [Fact]
    public async Task EnableDisableReenableAndTargetClearDoNotRepeatHistory()
    {
        var saved = System.Console.Out;
        System.Console.SetOut(new StringWriter());
        try
        {
            await using var server = DevToolsServer.Start(new AppWindow("Log lifecycle"), new() { AllowChromeInspect = true, AllowChromeConsole = true });
            using var first = new CdpConnection();
            await first.ConnectAsync(server);
            System.Console.WriteLine("before");
            using (await first.RequestAsync("Log.enable")) { }
            Assert.Equal("before", await ReadText(first));
            System.Console.WriteLine("live");
            Assert.Equal("live", await ReadText(first));
            using (await first.RequestAsync("Log.enable")) { }
            Assert.False(first.HasBufferedEvent("Log.entryAdded"));
            using (await first.RequestAsync("Log.disable")) { }
            System.Console.WriteLine("disabled");
            using (await first.RequestAsync("Page.enable")) { }
            Assert.False(first.HasBufferedEvent("Log.entryAdded"));
            using (await first.RequestAsync("Log.enable")) { }
            Assert.Equal("disabled", await ReadText(first));
            using var second = new CdpConnection();
            await second.ConnectAsync(server);
            using (await second.RequestAsync("Log.enable")) { }
            Assert.Equal("before", await ReadText(second));
            Assert.Equal("live", await ReadText(second));
            Assert.Equal("disabled", await ReadText(second));
            using (await first.RequestAsync("Log.clear")) { }
            System.Console.WriteLine("fresh");
            Assert.Equal("fresh", await ReadText(first));
            Assert.Equal("fresh", await ReadText(second));
            using (await first.RequestAsync("Log.disable")) { }
            using (await first.RequestAsync("Log.enable")) { }
            Assert.False(first.HasBufferedEvent("Log.entryAdded"));
        }
        finally { System.Console.SetOut(saved); }
    }

    [Fact]
    public async Task ClearingTargetFiltersAnotherSessionsQueuedUnsentEntries()
    {
        var saved = System.Console.Out;
        System.Console.SetOut(new StringWriter());
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            using var host = new InspectionTestHost(window => window.Load(new View()));
            await using var server = DevToolsServer.Start(host.Window, new() { AllowChromeInspect = true, AllowChromeConsole = true });
            using var blocked = new CdpConnection();
            using var clearing = new CdpConnection();
            await blocked.ConnectAsync(server);
            await clearing.ConnectAsync(server);
            using (await blocked.RequestAsync("Log.enable")) { }
            var blocker = host.InvokeAsync(() =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Blocked owner was not released.");
            });
            Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(5))));
            var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void MarkQueued() => queued.TrySetResult();
            host.Application.Dispatcher.WorkAvailable += MarkQueued;
            try
            {
                await blocked.SendAsync("{\"id\":7001,\"method\":\"DOM.getDocument\"}");
                await queued.Task.WaitAsync(TimeSpan.FromSeconds(5));
                System.Console.WriteLine("must not revive");
                using (await clearing.RequestAsync("Log.clear")) { }
            }
            finally
            {
                host.Application.Dispatcher.WorkAvailable -= MarkQueued;
                release.Set();
            }
            await blocker;
            using (await blocked.ReceiveResponseAsync(7001)) { }
            System.Console.WriteLine("after clear");
            Assert.Equal("after clear", await ReadText(blocked));
            using var barrier = await blocked.RequestAsync("Page.enable");
            Assert.False(blocked.HasBufferedEvent("Log.entryAdded"));
        }
        finally
        {
            release.Set();
            System.Console.SetOut(saved);
        }
    }

    private static async Task<string> ReadText(CdpConnection connection)
    {
        using var message = await connection.ReceiveEventAsync("Log.entryAdded");
        return message.RootElement.GetProperty("params").GetProperty("entry").GetProperty("text").GetString()!;
    }
}
