using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Square.Runtime.Signals;
using Xunit;

namespace Square.Runtime.Tests;

public class SignalTests
{
    [Fact]
    public void PublishNotifiesAndSuppressesEqualValues()
    {
        var signal = new Signal<int>(1);
        var values = new List<int>();
        using var subscription = signal.Subscribe(values.Add, emitCurrent: true);

        Assert.False(signal.Publish(1));
        Assert.True(signal.Publish(2));
        Assert.True(signal.Publish(2, force: true));

        Assert.Equal([1, 2, 2], values);
    }

    [Fact]
    public void UpdateIsAtomicAcrossThreads()
    {
        var signal = new Signal<int>(0);

        Parallel.For(0, 1000, _ => signal.Update(value => value + 1));

        Assert.Equal(1000, signal.Value);
    }

    [Fact]
    public void DispatcherSubscriptionMovesBackgroundPublishToOwnerThread()
    {
        var dispatcher = new Dispatcher();
        var signal = new Signal<string>("ready");
        var received = "";
        var callbackThread = 0;
        using var subscription = signal.Subscribe(value =>
        {
            received = value;
            callbackThread = Environment.CurrentManagedThreadId;
        }, dispatcher);

        var publisher = new Thread(() => signal.Publish("background"));
        publisher.Start();
        publisher.Join();

        Assert.Equal("", received);
        Assert.True(dispatcher.HasWork);
        dispatcher.Run();
        Assert.Equal("background", received);
        Assert.Equal(Environment.CurrentManagedThreadId, callbackThread);
    }

    [Fact]
    public void DisposedSubscriptionIgnoresQueuedDelivery()
    {
        var dispatcher = new Dispatcher();
        var signal = new Signal<int>(0);
        var received = 0;
        var subscription = signal.Subscribe(value => received = value, dispatcher);

        var publisher = new Thread(() => signal.Publish(5));
        publisher.Start();
        publisher.Join();
        subscription.Dispose();
        dispatcher.Run();

        Assert.Equal(0, received);
    }

    [Fact]
    public void HubSharesTypedSignalAndRejectsTypeCollisions()
    {
        var hub = new SignalHub();
        var first = hub.Get("status", "ready");
        var second = hub.Get("status", "ignored");

        Assert.Same(first, second);
        Assert.Equal("ready", second.Value);
        Assert.Throws<InvalidOperationException>(() => hub.Get("status", 1));
        Assert.True(hub.Remove<string>("status"));
        Assert.False(hub.Remove<string>("status"));
    }
}
