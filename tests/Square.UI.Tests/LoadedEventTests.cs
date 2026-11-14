using System.Collections.Generic;
using Square.Controls;
using Square.Events;
using Square.Runtime;
using Square.UI;
using Xunit;

namespace Square.UI.Tests;

public class LoadedEventTests
{
    [Fact]
    public void BareControlsReceiveTheirOwnNonBubblingLoadedEvent()
    {
        var parent = new View();
        var child = new Button("Ready");
        parent.Children.Add(child);
        var parentEvents = new List<Event>();
        var childEvents = new List<Event>();
        parent.AddEventListener(StandardEvents.Loaded, e =>
        {
            Assert.Equal(EventPhase.AtTarget, e.EventPhase);
            Assert.Same(parent, e.CurrentTarget);
            parentEvents.Add(e);
        });
        child.AddEventListener(StandardEvents.Loaded, e =>
        {
            Assert.Equal(EventPhase.AtTarget, e.EventPhase);
            Assert.Same(child, e.CurrentTarget);
            childEvents.Add(e);
        });

        ((IComponentLifecycle)parent).OnLoaded();

        var parentEvent = Assert.Single(parentEvents);
        var childEvent = Assert.Single(childEvents);
        Assert.Same(parent, parentEvent.Target);
        Assert.Same(child, childEvent.Target);
        Assert.False(parentEvent.Bubbles);
        Assert.False(childEvent.Bubbles);
        Assert.False(parentEvent.Cancelable);
        Assert.False(childEvent.Cancelable);
    }

    [Fact]
    public void LoadedEventsFollowHooksAndCompletedChildSubtrees()
    {
        var calls = new List<string>();
        var parent = new RecordingView("parent", calls);
        var child = new RecordingView("child", calls);
        var grandchild = new RecordingView("grandchild", calls);
        var sibling = new RecordingView("sibling", calls);
        child.Children.Add(grandchild);
        parent.Children.Add(child);
        parent.Children.Add(sibling);
        parent.AddEventListener(StandardEvents.Loaded, () =>
        {
            Assert.True(child.IsLoaded);
            Assert.True(grandchild.IsLoaded);
            Assert.True(sibling.IsLoaded);
        });

        ((IComponentLifecycle)parent).OnLoaded();

        Assert.Equal(
        [
            "parent.hook", "child.hook", "grandchild.hook", "grandchild.loaded",
            "child.loaded", "sibling.hook", "sibling.loaded", "parent.loaded"
        ], calls);
    }

    [Fact]
    public void UnloadAndReloadDispatchLoadedAgainForTheSameInstances()
    {
        var calls = new List<string>();
        var parent = new RecordingView("parent", calls);
        var child = new RecordingView("child", calls);
        parent.Children.Add(child);
        var lifecycle = (IComponentLifecycle)parent;
        lifecycle.OnLoaded();
        calls.Clear();

        lifecycle.OnUnloaded();
        Assert.False(parent.IsLoaded);
        Assert.False(child.IsLoaded);
        Assert.Empty(calls);
        lifecycle.OnLoaded();

        Assert.True(parent.IsLoaded);
        Assert.True(child.IsLoaded);
        Assert.Equal(["parent.hook", "child.hook", "child.loaded", "parent.loaded"], calls);
    }

    [Fact]
    public void MountingIntoLoadedDocumentDispatchesAfterTheBuiltSubtreeLoads()
    {
        var document = new UIDocument();
        ((IComponentLifecycle)document.Ui).OnAttached();
        ((IComponentLifecycle)document.Ui).OnLoaded();
        var component = new MountedComponent();
        var calls = new List<string>();
        var parentLoads = 0;
        document.Body.AddEventListener(StandardEvents.Loaded, () => parentLoads++);
        component.Content.AddEventListener(StandardEvents.Loaded, () => calls.Add("child.loaded"));
        component.AddEventListener(StandardEvents.Loaded, () =>
        {
            Assert.Equal("Ready", component.LoadedText);
            Assert.Same(component.Content, Assert.Single(component.Children));
            Assert.True(component.Content.IsLoaded);
            calls.Add("component.loaded");
        });

        using var mount = document.Body.Mount(component);

        Assert.True(component.IsAttached);
        Assert.True(component.IsLoaded);
        Assert.Equal(["child.loaded", "component.loaded"], calls);
        Assert.Equal(0, parentLoads);
    }

    [Fact]
    public void ChildLoadedCanRemoveItselfAndAddASiblingWithoutInterruptingParentLoad()
    {
        var parent = new View();
        var child = new Button("Remove me");
        var remaining = new Button("Remain");
        var added = new Button("Added");
        parent.Children.Add(child);
        parent.Children.Add(remaining);
        var calls = new List<string>();
        child.AddEventListener(StandardEvents.Loaded, () =>
        {
            calls.Add("child.loaded");
            parent.Children.Remove(child);
            parent.Children.Add(added);
        });
        remaining.AddEventListener(StandardEvents.Loaded, () => calls.Add("remaining.loaded"));
        added.AddEventListener(StandardEvents.Loaded, () => calls.Add("added.loaded"));
        parent.AddEventListener(StandardEvents.Loaded, () => calls.Add("parent.loaded"));

        ((IComponentLifecycle)parent).OnLoaded();

        Assert.Equal(["child.loaded", "added.loaded", "remaining.loaded", "parent.loaded"], calls);
        Assert.Null(child.Parent);
        Assert.False(child.IsLoaded);
        Assert.True(remaining.IsLoaded);
        Assert.True(added.IsLoaded);
        Assert.True(parent.IsLoaded);
    }

    [Fact]
    public void ChildLoadedCanRemoveAnUnloadedSiblingWithoutLoadingTheRemovedNode()
    {
        var parent = new View();
        var child = new Button("First");
        var removed = new Button("Remove before load");
        var remaining = new Button("Remain");
        parent.Children.Add(child);
        parent.Children.Add(removed);
        parent.Children.Add(remaining);
        var calls = new List<string>();
        child.AddEventListener(StandardEvents.Loaded, () =>
        {
            calls.Add("child.loaded");
            parent.Children.Remove(removed);
        });
        removed.AddEventListener(StandardEvents.Loaded, () => calls.Add("removed.loaded"));
        remaining.AddEventListener(StandardEvents.Loaded, () => calls.Add("remaining.loaded"));
        parent.AddEventListener(StandardEvents.Loaded, () => calls.Add("parent.loaded"));

        ((IComponentLifecycle)parent).OnLoaded();

        Assert.Equal(["child.loaded", "remaining.loaded", "parent.loaded"], calls);
        Assert.Null(removed.Parent);
        Assert.False(removed.IsLoaded);
        Assert.True(remaining.IsLoaded);
        Assert.True(parent.IsLoaded);
    }

    [Fact]
    public void ChildAddedByLoadedHookReceivesLoadedOnlyOnce()
    {
        var child = new Button("Added by hook");
        var parent = new AddingLoadedView(child);
        var calls = new List<string>();
        child.AddEventListener(StandardEvents.Loaded, () => calls.Add("child.loaded"));
        parent.AddEventListener(StandardEvents.Loaded, () => calls.Add("parent.loaded"));

        ((IComponentLifecycle)parent).OnLoaded();

        Assert.Equal(["child.loaded", "parent.loaded"], calls);
        Assert.Same(parent, child.Parent);
        Assert.True(child.IsLoaded);
    }

    [Fact]
    public void ImageLifecycleLoadedDoesNotDispatchImageResourceLoad()
    {
        var image = new Image();
        var loaded = 0;
        var resourceLoads = 0;
        image.AddEventListener(StandardEvents.Loaded, () => loaded++);
        image.AddEventListener("load", () => resourceLoads++);

        ((IComponentLifecycle)image).OnLoaded();

        Assert.Equal(1, loaded);
        Assert.Equal(0, resourceLoads);
    }

    private sealed class RecordingView : View
    {
        private readonly string _name;
        private readonly List<string> _calls;

        public RecordingView(string name, List<string> calls)
        {
            _name = name;
            _calls = calls;
            AddEventListener(StandardEvents.Loaded, () => calls.Add(name + ".loaded"));
        }

        protected override void OnLoadedCore()
        {
            Assert.True(IsLoaded);
            _calls.Add(_name + ".hook");
        }
    }

    private sealed class AddingLoadedView(Element child) : View
    {
        protected override void OnLoadedCore() => Children.Add(child);
    }

    private sealed class MountedComponent : UIElement
    {
        public Button Content { get; } = new("Ready");
        public string? LoadedText { get; private set; }

        public override void BuildElementTree() => Children.Add(Content);

        protected override void OnLoadedCore() =>
            LoadedText = Assert.IsType<Button>(Assert.Single(Children)).TextContent;
    }
}
