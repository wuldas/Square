using Square.Controls;
using Square.Graphics;
using Square.Hosting;
using Square.Runtime;
using Square.UI;
using Xunit;

namespace Square.UI.Tests;

public sealed class NativeViewHostingTests
{
    [Fact]
    public void LoadedLifecycleInvokesLoadedCoreAfterMarkingElementLoaded()
    {
        var probe = new LoadedProbe();

        ((IComponentLifecycle)probe).OnLoaded();

        Assert.True(probe.IsLoaded);
        Assert.True(probe.LoadedCoreCalled);
        Assert.True(probe.WasLoadedWhenCoreRan);
    }

    [Fact]
    public void NativeViewSynchronizerVisitsVisibleNativeElements()
    {
        var root = new View();
        var probe = new NativeProbe();
        root.Children.Add(probe);
        root.Arrange(new Rect(0, 0, 800, 600));
        probe.Arrange(new Rect(12, 24, 320, 180));

        NativeViewSynchronizer.Synchronize(root, 1.25f);

        Assert.Equal(new NativeViewLayout(new Rect(12, 24, 320, 180), 1.25f, true), probe.Layout);
    }

    private sealed class LoadedProbe : UIElement
    {
        public bool LoadedCoreCalled { get; private set; }
        public bool WasLoadedWhenCoreRan { get; private set; }

        protected override void OnLoadedCore()
        {
            LoadedCoreCalled = true;
            WasLoadedWhenCoreRan = IsLoaded;
        }
    }

    private sealed class NativeProbe : UIElement, INativeViewElement
    {
        public NativeViewLayout Layout { get; private set; }

        public void SynchronizeNativeView(NativeViewLayout layout) => Layout = layout;
    }
}
