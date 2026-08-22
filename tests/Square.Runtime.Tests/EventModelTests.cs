using System.Collections.Generic;
using Square.Events;
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

    private sealed class TestNode : EventTarget
    {
        public TestNode? Parent { get; set; }
        protected override EventTarget? GetEventParent() => Parent;
    }
}
