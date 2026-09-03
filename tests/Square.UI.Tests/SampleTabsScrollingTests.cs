using Square.Controls;
using Square.Events;
using Square.Graphics;
using Square.Rendering;
using Square.Runtime;
using Square.Sample;
using Square.Sample.Components;
using Square.UI;
using Xunit;

namespace Square.UI.Tests;

public class SampleTabsScrollingTests
{
    [Fact]
    public void SmallWindowScrollsOnlyTheActiveTabPanel()
    {
        var component = new Main();
        component.BuildElementTree();
        ((IComponentLifecycle)component).OnAttached();
        var app = Assert.IsType<View>(Assert.Single(component.Children));
        var tabList = Assert.Single(app.QueryAll<View>(), view => view.ClassList.Contains("tab-list"));
        var tabPanels = Assert.Single(app.QueryAll<View>(), view => view.ClassList.Contains("tab-panels"));
        var textPage = Assert.Single(app.QueryAll<TextSamplesPage>());
        var layout = new LayoutEngine();

        layout.Measure(app, new Size(900, 400));
        layout.Arrange(app, new Rect(0, 0, 900, 400));

        Assert.Equal(42, tabList.Geometry.Height);
        Assert.True(tabPanels.Geometry.Bottom <= app.Geometry.Bottom);
        Assert.True(tabPanels.ScrollContentSize.Height > tabPanels.Geometry.Height);
        var appTop = app.Geometry.Top;
        var tabListTop = tabList.Geometry.Top;

        textPage.DispatchTrusted(StandardEvents.CreateWheel(0, 120));

        tabPanels.AdvanceSmoothScroll(0.08f);
        Assert.True(tabPanels.ScrollTop > 0);
        Assert.Equal(appTop, app.Geometry.Top);
        Assert.Equal(tabListTop, tabList.Geometry.Top);
        ((IComponentLifecycle)component).OnDetached();
    }
}
