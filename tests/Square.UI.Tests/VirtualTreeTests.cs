using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Xunit;

namespace Square.UI.Tests;

public class VirtualTreeTests
{
    [Fact]
    public void ExpansionUpdatesFlattenedExtentWithoutRealizingEveryNode()
    {
        var roots = CreateTree();
        var tree = new VirtualTree { ItemHeight = 20, OverscanCount = 0 };
        tree.SetItemsSource(roots, node => node.Children, node => node.Id,
            (node, _) => new TreeItem(node.Name));
        var layout = new LayoutEngine();
        layout.MeasureAndArrange(tree, new Size(200, 60));

        Assert.Equal(2, tree.VisibleItemCount);
        Assert.Equal(2, tree.RealizedItemCount);

        Assert.True(tree.ExpandIndex(0));
        layout.MeasureAndArrange(tree, new Size(200, 60));

        Assert.Equal(5, tree.VisibleItemCount);
        Assert.Equal(100, tree.ExtentHeight);
        Assert.True(tree.RealizedItemCount < tree.VisibleItemCount);
    }

    [Fact]
    public void KeyboardNavigationUsesLogicalParentAndChildren()
    {
        var tree = new VirtualTree { ItemHeight = 20, OverscanCount = 1 };
        tree.SetItemsSource(CreateTree(), node => node.Children, node => node.Id,
            (node, _) => new TreeItem(node.Name));
        new LayoutEngine().MeasureAndArrange(tree, new Size(200, 60));

        Assert.True(tree.HandleKey(40));
        Assert.Equal("Root", ((Node)tree.SelectedValue!).Name);
        Assert.True(tree.HandleKey(39));
        Assert.True(tree.HandleKey(39));
        Assert.Equal("Child 1", ((Node)tree.SelectedValue!).Name);
        Assert.True(tree.HandleKey(37));
        Assert.Equal("Root", ((Node)tree.SelectedValue!).Name);
    }

    [Fact]
    public void RealizedRowsAreIndentedByLogicalDepth()
    {
        var tree = new VirtualTree { ItemHeight = 20, IndentSize = 12, OverscanCount = 5 };
        tree.SetItemsSource(CreateTree(), node => node.Children, node => node.Id,
            (node, _) => new TreeItem(node.Name));
        tree.ExpandIndex(0);
        var layout = new LayoutEngine();
        layout.MeasureAndArrange(tree, new Size(200, 120));

        var rows = tree.QueryAll<TreeItem>();
        Assert.Equal(0, rows[0].Geometry.X);
        Assert.Equal(12, rows[1].Geometry.X);
    }

    [Fact]
    public void CollapseFallsSelectionBackToCollapsedParent()
    {
        var tree = new VirtualTree { ItemHeight = 20 };
        tree.SetItemsSource(CreateTree(), node => node.Children, node => node.Id,
            (node, _) => new TreeItem(node.Name));
        tree.ExpandIndex(0);
        tree.SelectIndex(1);

        Assert.True(tree.CollapseIndex(0));

        Assert.Equal("Root", ((Node)tree.SelectedValue!).Name);
    }

    private static Node[] CreateTree() =>
    [
        new(1, "Root",
        [
            new(2, "Child 1", []),
            new(3, "Child 2", []),
            new(4, "Child 3", [])
        ]),
        new(5, "Sibling", [])
    ];

    private sealed record Node(int Id, string Name, IReadOnlyList<Node> Children);
}
