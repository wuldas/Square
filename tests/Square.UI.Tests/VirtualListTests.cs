using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Square.Runtime.Binding;
using Xunit;

namespace Square.UI.Tests;

public class VirtualListTests
{
    [Fact]
    public void RealizesOnlyViewportAndOverscanWhilePreservingExtent()
    {
        var source = Enumerable.Range(0, 1000).Select(index => $"Item {index}").ToArray();
        var list = new VirtualList { ItemHeight = 20, OverscanCount = 2 };
        list.SetItemsSource(source);
        var layout = new LayoutEngine();

        layout.MeasureAndArrange(list, new Size(200, 100));

        Assert.Equal(7, list.RealizedItemCount);
        Assert.Equal(0, list.FirstRealizedIndex);
        Assert.Equal(6, list.LastRealizedIndex);
        Assert.Equal(20_000, list.ExtentHeight);
        Assert.Equal(7, list.QueryAll<ListItem>().Count);
    }

    [Fact]
    public void ScrollingRealizesANewLogicalRange()
    {
        var list = new VirtualList { ItemHeight = 20, OverscanCount = 1 };
        list.SetItemsSource(Enumerable.Range(0, 100).ToArray());
        var layout = new LayoutEngine();
        layout.MeasureAndArrange(list, new Size(200, 100));

        list.ScrollTo(0, 400);
        layout.MeasureAndArrange(list, new Size(200, 100));

        Assert.Equal(19, list.FirstRealizedIndex);
        Assert.Equal("19", list.QueryAll<ListItem>()[0].TextContent);
        Assert.True(list.RealizedItemCount < list.ItemCount);
    }

    [Fact]
    public void LogicalSelectionSurvivesContainerRecycling()
    {
        var list = new VirtualList { ItemHeight = 20, OverscanCount = 0 };
        list.SetItemsSource(Enumerable.Range(0, 100).Select(index => $"Item {index}").ToArray());
        var layout = new LayoutEngine();
        layout.MeasureAndArrange(list, new Size(200, 100));

        list.SelectIndex(2);
        list.ScrollTo(0, 600);
        layout.MeasureAndArrange(list, new Size(200, 100));

        Assert.Equal(2, list.SelectedIndex);
        Assert.Equal("Item 2", list.SelectedValue);
        Assert.Null(list.SelectedItem);

        list.ScrollIntoView(2);
        layout.MeasureAndArrange(list, new Size(200, 100));

        Assert.True(list.SelectedItem?.IsSelected);
    }

    [Fact]
    public void ObservableSourceMapsSelectionAfterInsert()
    {
        var source = new ObservableCollection<string> { "A", "B", "C" };
        var list = new VirtualList { ItemHeight = 20 };
        list.SetItemsSource(source);
        list.SelectIndex(1);

        source.Insert(0, "Before");

        Assert.Equal(2, list.SelectedIndex);
        Assert.Equal("B", list.SelectedValue);
        Assert.Equal(4, list.ItemCount);
    }

    [Fact]
    public void TenThousandItemsRemainVirtualizedWhileScrollingAcrossTheExtent()
    {
        const int itemCount = 10_000;
        const float itemHeight = 20;
        var list = new VirtualList { ItemHeight = itemHeight, OverscanCount = 2 };
        list.SetItemsSource(Enumerable.Range(0, itemCount).Select(index => $"Row {index}").ToArray());
        var layout = new LayoutEngine();
        var viewport = new Size(320, 100);
        layout.MeasureAndArrange(list, viewport);

        Assert.Equal(itemCount * itemHeight, list.ExtentHeight);
        Assert.Equal(7, list.RealizedItemCount);
        Assert.Equal("Row 0", list.QueryAll<ListItem>()[0].TextContent);

        foreach (var targetIndex in new[] { 500, 5_000, 9_500 })
        {
            list.ScrollTo(0, targetIndex * itemHeight);
            layout.MeasureAndArrange(list, viewport);

            Assert.InRange(list.FirstRealizedIndex, targetIndex - list.OverscanCount, targetIndex);
            Assert.InRange(targetIndex, list.FirstRealizedIndex, list.LastRealizedIndex);
            Assert.True(list.RealizedItemCount <= 5 + list.OverscanCount * 2);
            Assert.Contains(list.QueryAll<ListItem>(), item => item.TextContent == $"Row {targetIndex}");
        }

        list.ScrollToBottom();
        layout.MeasureAndArrange(list, viewport);

        Assert.Equal(itemCount - 1, list.LastRealizedIndex);
        Assert.True(list.RealizedItemCount <= 5 + list.OverscanCount * 2);
        Assert.Equal("Row 9999", list.QueryAll<ListItem>()[^1].TextContent);
    }
}
