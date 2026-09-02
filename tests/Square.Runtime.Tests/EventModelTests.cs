using System.Collections.Generic;
using Square.Events;
using Square.UI;
using Xunit;

namespace Square.Runtime.Tests;

public class EventModelTests
{
    [Fact]
    public void EventCarriesTypeAndInitFlags()
    {
        var e = new Event("save", new EventInit { Bubbles = true, Cancelable = true });

        Assert.Equal("save", e.Type);
        Assert.True(e.Bubbles);
        Assert.True(e.Cancelable);
        Assert.Equal(EventPhase.None, e.EventPhase);
    }

    [Fact]
    public void PreventDefaultOnlyWorksWhenCancelable()
    {
        var cancelable = new Event("click", new EventInit { Cancelable = true });
        cancelable.PreventDefault();
        Assert.True(cancelable.DefaultPrevented);

        var plain = new Event("change", new EventInit { Bubbles = true });
        plain.PreventDefault();
        Assert.False(plain.DefaultPrevented);
    }

    [Fact]
    public void DispatchEventBubblesToParent()
    {
        var root = new TestNode();
        var child = new TestNode { Parent = root };
        var phases = new List<string>();

        root.AddEventListener("click", e => phases.Add($"root:{e.EventPhase}"));
        child.AddEventListener("click", e => phases.Add($"child:{e.EventPhase}"));

        child.DispatchEvent(new Event("click", new EventInit { Bubbles = true }));

        Assert.Equal(new[]
        {
            $"child:{EventPhase.AtTarget}",
            $"root:{EventPhase.BubblingPhase}"
        }, phases);
    }

    [Fact]
    public void CaptureListenersRunBeforeTarget()
    {
        var root = new TestNode();
        var child = new TestNode { Parent = root };
        var calls = new List<string>();

        root.AddEventListener("click", _ => calls.Add("root-capture"), useCapture: true);
        root.AddEventListener("click", _ => calls.Add("root-bubble"));
        child.AddEventListener("click", _ => calls.Add("child"));

        child.DispatchEvent(new Event("click", new EventInit { Bubbles = true }));

        Assert.Equal(new[] { "root-capture", "child", "root-bubble" }, calls);
    }

    [Fact]
    public void StopPropagationPreventsParentHandlers()
    {
        var root = new TestNode();
        var child = new TestNode { Parent = root };
        var rootCalls = 0;

        root.AddEventListener("click", _ => rootCalls++);
        child.AddEventListener("click", e => e.StopPropagation());

        child.DispatchEvent(new Event("click", new EventInit { Bubbles = true }));

        Assert.Equal(0, rootCalls);
    }

    [Fact]
    public void StopImmediatePropagationSkipsSameTargetListeners()
    {
        var target = new TestNode();
        var calls = 0;

        target.AddEventListener("click", e =>
        {
            calls++;
            e.StopImmediatePropagation();
        });
        target.AddEventListener("click", _ => calls++);

        target.DispatchEvent(new Event("click", new EventInit { Bubbles = true }));

        Assert.Equal(1, calls);
    }

    [Fact]
    public void DispatchEventReturnsFalseWhenDefaultPrevented()
    {
        var target = new TestNode();
        target.AddEventListener("click", e => e.PreventDefault());

        var result = target.DispatchEvent(new Event("click", new EventInit { Bubbles = true, Cancelable = true }));

        Assert.False(result);
    }

    [Fact]
    public void PassiveListenerCannotPreventDefault()
    {
        var target = new TestNode();
        target.AddEventListener("wheel", e => e.PreventDefault(), new AddEventListenerOptions { Passive = true });

        var result = target.DispatchEvent(new Event("wheel", new EventInit { Bubbles = true, Cancelable = true }));

        Assert.True(result);
    }

    [Fact]
    public void OnceListenerIsRemovedAfterInvoke()
    {
        var target = new TestNode();
        var calls = 0;
        target.AddEventListener("click", _ => calls++, new AddEventListenerOptions { Once = true });

        target.DispatchEvent(StandardEvents.CreateClick());
        target.DispatchEvent(StandardEvents.CreateClick());

        Assert.Equal(1, calls);
    }

    [Fact]
    public void FocusDoesNotBubbleButFocusInDoes()
    {
        var root = new TestNode();
        var child = new TestNode { Parent = root };
        var focusOnRoot = 0;
        var focusInOnRoot = 0;

        root.AddEventListener(StandardEvents.Focus, _ => focusOnRoot++);
        root.AddEventListener(StandardEvents.FocusIn, _ => focusInOnRoot++);

        child.DispatchEvent(StandardEvents.CreateFocus());
        child.DispatchEvent(StandardEvents.CreateFocusIn());

        Assert.Equal(0, focusOnRoot);
        Assert.Equal(1, focusInOnRoot);
    }

    [Fact]
    public void RemoveEventListenerByActionDelegate()
    {
        var target = new TestNode();
        var calls = 0;
        void Handler() => calls++;

        target.AddEventListener("click", Handler);
        target.DispatchEvent(StandardEvents.CreateClick());
        target.RemoveEventListener("click", Handler);
        target.DispatchEvent(StandardEvents.CreateClick());

        Assert.Equal(1, calls);
    }

    [Fact]
    public void ListenActionDisposalNormalizesWhitespace()
    {
        var target = new TestNode();
        var calls = 0;
        void Handler() => calls++;

        var subscription = target.Listen(" click ", Handler);
        subscription.Dispose();
        target.DispatchEvent(StandardEvents.CreateClick());

        Assert.Equal(0, calls);
        Assert.Empty(target.RegisteredEventTypes);
    }

    [Fact]
    public void ListenTypedDisposalNormalizesWhitespace()
    {
        var target = new TestNode();
        var calls = 0;
        void Handler(KeyboardEvent _) => calls++;

        var subscription = target.Listen<KeyboardEvent>(" keydown ", Handler);
        subscription.Dispose();
        target.DispatchEvent(StandardEvents.CreateKeyDown(13));

        Assert.Equal(0, calls);
        Assert.Empty(target.RegisteredEventTypes);
    }

    [Fact]
    public void GenericEventListenDisposesItsAdapterRegistration()
    {
        var target = new TestNode();
        var calls = 0;
        void Handler(Event _) => calls++;

        var subscription = target.Listen<Event>("click", Handler);
        subscription.Dispose();
        target.DispatchEvent(StandardEvents.CreateClick());

        Assert.Equal(0, calls);
        Assert.Empty(target.RegisteredEventTypes);
    }

    [Fact]
    public void OnceActionCanBeRegisteredAgainAfterAutomaticRemoval()
    {
        var target = new TestNode();
        var calls = 0;
        void Handler() => calls++;

        target.AddEventListener("click", Handler, new AddEventListenerOptions { Once = true });
        target.DispatchEvent(StandardEvents.CreateClick());
        target.AddEventListener("click", Handler);
        target.DispatchEvent(StandardEvents.CreateClick());

        Assert.Equal(2, calls);
    }

    [Fact]
    public void SignaledActionCanBeRegisteredAgainAfterAutomaticRemoval()
    {
        var target = new TestNode();
        var calls = 0;
        void Handler() => calls++;
        using var cancellation = new CancellationTokenSource();

        target.AddEventListener("click", Handler,
            new AddEventListenerOptions { Signal = cancellation.Token });
        cancellation.Cancel();
        target.AddEventListener("click", Handler);
        target.DispatchEvent(StandardEvents.CreateClick());

        Assert.Equal(1, calls);
    }

    [Fact]
    public void OnceListenerIsRemovedBeforeReentrantDispatch()
    {
        var target = new TestNode();
        var calls = 0;
        target.AddEventListener("click", _ =>
        {
            calls++;
            target.DispatchEvent(StandardEvents.CreateClick());
        }, new AddEventListenerOptions { Once = true });

        target.DispatchEvent(StandardEvents.CreateClick());

        Assert.Equal(1, calls);
    }

    [Fact]
    public void OnceActionCanReregisterInsideItsCallback()
    {
        var target = new TestNode();
        var calls = 0;
        void Handler()
        {
            calls++;
            if (calls == 1) target.AddEventListener("click", Handler);
        }
        target.AddEventListener("click", Handler, new AddEventListenerOptions { Once = true });

        target.DispatchEvent(StandardEvents.CreateClick());
        target.DispatchEvent(StandardEvents.CreateClick());

        Assert.Equal(2, calls);
    }

    [Fact]
    public void ThrowingOnceListenerRemainsRemoved()
    {
        var target = new TestNode();
        var calls = 0;
        target.AddEventListener("click", _ =>
        {
            calls++;
            throw new InvalidOperationException("boom");
        }, new AddEventListenerOptions { Once = true });

        Assert.Throws<InvalidOperationException>(() => target.DispatchEvent(StandardEvents.CreateClick()));
        target.DispatchEvent(StandardEvents.CreateClick());

        Assert.Equal(1, calls);
    }

    [Fact]
    public void StaleListenSubscriptionDoesNotRemoveLaterRegistration()
    {
        var target = new TestNode();
        var calls = 0;
        void Handler(Event _) => calls++;
        var stale = target.Listen("click", Handler, new AddEventListenerOptions { Once = true });
        target.DispatchEvent(StandardEvents.CreateClick());
        target.AddEventListener("click", Handler);

        stale.Dispose();
        target.DispatchEvent(StandardEvents.CreateClick());

        Assert.Equal(2, calls);
    }

    [Fact]
    public void PreCancelledListenSubscriptionDoesNotRemoveLaterRegistration()
    {
        var target = new TestNode();
        var calls = 0;
        void Handler() => calls++;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var stale = target.Listen("click", Handler,
            new AddEventListenerOptions { Signal = cancellation.Token });
        target.AddEventListener("click", Handler);

        stale.Dispose();
        target.DispatchEvent(StandardEvents.CreateClick());

        Assert.Equal(1, calls);
    }

    [Fact]
    public void PreCancelledDuplicateListenDoesNotOwnExistingRegistration()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var options = new AddEventListenerOptions { Signal = cancellation.Token };

        var directTarget = new TestNode();
        var directCalls = 0;
        Action<Event> direct = _ => directCalls++;
        directTarget.AddEventListener("click", direct);
        directTarget.Listen(" CLICK ", direct, options).Dispose();
        directTarget.DispatchEvent(StandardEvents.CreateClick());

        var actionTarget = new TestNode();
        var actionCalls = 0;
        void ActionHandler() => actionCalls++;
        actionTarget.AddEventListener("click", ActionHandler);
        actionTarget.Listen(" click ", ActionHandler, options).Dispose();
        actionTarget.DispatchEvent(StandardEvents.CreateClick());

        var typedTarget = new TestNode();
        var typedCalls = 0;
        void TypedHandler(KeyboardEvent _) => typedCalls++;
        typedTarget.AddEventListener<KeyboardEvent>("keydown", TypedHandler);
        typedTarget.Listen<KeyboardEvent>(" KEYDOWN ", TypedHandler, options).Dispose();
        typedTarget.DispatchEvent(StandardEvents.CreateKeyDown(13));

        Assert.Equal(1, directCalls);
        Assert.Equal(1, actionCalls);
        Assert.Equal(1, typedCalls);
    }

    [Fact]
    public void ValueEqualEventListenerObjectsRegisterIndependently()
    {
        var target = new TestNode();
        var first = new ValueEqualListener();
        var second = new ValueEqualListener();

        target.AddEventListener("click", first);
        target.AddEventListener("click", second);
        target.DispatchEvent(StandardEvents.CreateClick());

        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    [Fact]
    public void StandardEventsCreateUsesDefaultInit()
    {
        var click = StandardEvents.CreateClick();
        Assert.Equal(StandardEvents.Click, click.Type);
        Assert.True(click.Bubbles);
        Assert.True(click.Cancelable);

        var focus = StandardEvents.CreateFocus();
        Assert.False(focus.Bubbles);
    }

    [Fact]
    public void DispatchTrustedSetsIsTrusted()
    {
        var target = new TestNode();
        Event? seen = null;
        target.AddEventListener("click", e => seen = e);

        target.DispatchTrusted(StandardEvents.CreateClick());

        Assert.NotNull(seen);
        Assert.True(seen!.IsTrusted);
    }

    [Fact]
    public void RegisteredEventTypesTracksCurrentListeners()
    {
        var target = new TestNode();
        Action<Event> handler = _ => { };
        target.AddEventListener("click", handler);
        target.AddEventListener("CLICK", _ => { });

        Assert.Equal(new[] { "click" }, target.RegisteredEventTypes);

        target.RemoveEventListener("click", handler);
        Assert.Equal(new[] { "CLICK" }, target.RegisteredEventTypes);
    }

    [Fact]
    public void ComponentEmitDeliversTypedDetailWithoutBubbling()
    {
        var parent = new TestComponent();
        var child = new TestComponent();
        parent.Children.Add(child);
        CustomEvent<int>? received = null;
        Event? baseReceived = null;
        var parentCalls = 0;
        var parentCaptureCalls = 0;
        using var subscription = child.Listen(TestComponent.SelectedEvent, e => received = e);
        using var baseSubscription = child.Listen(TestComponent.SelectedEvent, (Event e) => baseReceived = e);
        parent.AddEventListener("selected", _ => parentCalls++);
        parent.AddEventListener("selected", _ => parentCaptureCalls++, useCapture: true);

        child.EmitSelected(7);

        Assert.NotNull(received);
        Assert.Equal(7, received!.Detail);
        Assert.Same(received, baseReceived);
        Assert.Same(child, received.Target);
        Assert.False(received.Bubbles);
        Assert.False(received.Cancelable);
        Assert.Equal(0, parentCalls);
        Assert.Equal(0, parentCaptureCalls);
    }

    [Fact]
    public void ComponentEmitCreatesFreshEventInstances()
    {
        var component = new TestComponent();
        var events = new List<CustomEvent<int>>();
        component.Listen(TestComponent.SelectedEvent, events.Add);

        component.EmitSelected(1);
        component.EmitSelected(2);

        Assert.Equal(2, events.Count);
        Assert.NotSame(events[0], events[1]);
        Assert.Equal(new[] { 1, 2 }, events.Select(item => item.Detail));
    }

    [Fact]
    public void ComponentEventSubscriptionDisposalStopsNoDetailHandler()
    {
        var component = new TestComponent();
        var calls = 0;
        var subscription = component.Listen(TestComponent.ClosedEvent, () => calls++);

        component.EmitClosed();
        subscription.Dispose();
        component.EmitClosed();

        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ItemSelected")]
    [InlineData("item--selected")]
    [InlineData("item_selected")]
    public void ComponentEventRejectsNonKebabCaseNames(string name)
    {
        Assert.Throws<ArgumentException>(() => new ComponentEvent(name));
    }

    private sealed class TestNode : EventTarget
    {
        public TestNode? Parent { get; set; }
        protected override EventTarget? GetEventParent() => Parent;
    }

    private sealed class TestComponent : UIElement
    {
        public static readonly ComponentEvent<int> SelectedEvent = new("selected");
        public static readonly ComponentEvent ClosedEvent = new("closed");

        public void EmitSelected(int value) => Emit(SelectedEvent, value);
        public void EmitClosed() => Emit(ClosedEvent);

        public override void BuildElementTree() { }
    }

    private sealed class ValueEqualListener : IEventListener
    {
        public int Calls { get; private set; }
        public void HandleEvent(Event e) => Calls++;
        public override bool Equals(object? obj) => obj is ValueEqualListener;
        public override int GetHashCode() => 1;
    }
}
