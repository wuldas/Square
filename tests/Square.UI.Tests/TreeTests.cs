using Square.Controls;
using Square.Events;
using Square.Graphics;
using Square.Rendering;
using Xunit;

namespace Square.UI.Tests;

public class TreeTests
{
    [Fact]
    public void ExpandingAndCollapsingControlsDirectChildVisibility()
    {
        var parent = new TreeItem("Parent");
        var child = new TreeItem("Child");
        parent.Children.Add(child);

        Assert.False(parent.IsExpanded);
        Assert.False(child.IsVisible);
        Assert.True(parent.Expand());
        Assert.True(child.IsVisible);
        Assert.True(parent.Collapse());
        Assert.False(child.IsVisible);
    }

    [Fact]
    public void SelectionIsExclusiveAndRaisesTreeEvents()
    {
        var tree = new Tree();
        var first = new TreeItem("First");
        var second = new TreeItem("Second");
        tree.Children.Add(first);
        tree.Children.Add(second);
        var changes = 0;
        tree.AddEventListener(StandardEvents.SelectionChange, () => changes++);

        Assert.True(tree.SelectItem(first));
        Assert.True(tree.SelectItem(second));

        Assert.False(first.IsSelected);
        Assert.True(second.IsSelected);
        Assert.Same(second, tree.SelectedItem);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void KeyboardNavigatesVisibleHierarchyAndPreservesExpansion()
    {
        var tree = new Tree();
        var parent = new TreeItem("Parent");
        var child = new TreeItem("Child");
        var grandchild = new TreeItem("Grandchild");
        child.Children.Add(grandchild);
        parent.Children.Add(child);
        tree.Children.Add(parent);
        tree.Children.Add(new TreeItem("Sibling"));

        Assert.True(tree.HandleKey(40));
        Assert.Same(parent, tree.SelectedItem);
        Assert.True(tree.HandleKey(39));
        Assert.True(parent.IsExpanded);
        Assert.True(tree.HandleKey(39));
        Assert.Same(child, tree.SelectedItem);
        Assert.True(tree.HandleKey(39));
        Assert.True(child.IsExpanded);
        Assert.True(tree.HandleKey(40));
        Assert.Same(grandchild, tree.SelectedItem);
        Assert.True(tree.HandleKey(37));
        Assert.Same(child, tree.SelectedItem);
        Assert.True(tree.HandleKey(37));
        Assert.False(child.IsExpanded);
        Assert.True(parent.IsExpanded);
    }

    [Fact]
    public void ClickSelectsTogglesBranchAndFocusesTree()
    {
        var tree = new Tree();
        var parent = new TreeItem("Parent");
        parent.Children.Add(new TreeItem("Child"));
        tree.Children.Add(parent);

        parent.DispatchEvent(StandardEvents.CreateClick());

        Assert.Same(parent, tree.SelectedItem);
        Assert.True(parent.IsExpanded);
        Assert.True(tree.IsFocused);
    }

    [Fact]
    public void RemovingSelectedBranchClearsSelectionState()
    {
        var tree = new Tree();
        var parent = new TreeItem("Parent") { IsExpanded = true };
        var child = new TreeItem("Child");
        parent.Children.Add(child);
        tree.Children.Add(parent);
        tree.SelectItem(child);
        var changes = 0;
        tree.AddEventListener(StandardEvents.SelectionChange, () => changes++);

        tree.Children.Remove(parent);

        Assert.Null(tree.SelectedItem);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void LayoutReservesOneHeaderRowAndIndentsExpandedChildren()
    {
        var tree = new Tree();
        var parent = new TreeItem("Parent") { IsExpanded = true };
        var child = new TreeItem("Child");
        parent.Children.Add(child);
        tree.Children.Add(parent);
        var layout = new LayoutEngine();

        layout.Measure(tree, new Size(300, 200));
        layout.Arrange(tree, new Rect(0, 0, 300, 200));

        Assert.InRange(parent.Geometry.Height, 55, 57);
        Assert.InRange(child.Geometry.Height, 27, 29);
        Assert.Equal(parent.Geometry.Y + 28, child.Geometry.Y);
        Assert.Equal(parent.Geometry.X + 18, child.Geometry.X);
    }

    [Fact]
    public void CollapsedBranchDoesNotReserveAHiddenChildRow()
    {
        var tree = new Tree();
        var parent = new TreeItem("Parent");
        parent.Children.Add(new TreeItem("Child"));
        tree.Children.Add(parent);
        var layout = new LayoutEngine();

        layout.Measure(tree, new Size(300, 200));
        layout.Arrange(tree, new Rect(0, 0, 300, 200));
        Assert.InRange(parent.Geometry.Height, 27, 29);

        parent.Expand();
        layout.Measure(tree, new Size(300, 200));
        layout.Arrange(tree, new Rect(0, 0, 300, 200));
        Assert.InRange(parent.Geometry.Height, 55, 57);

        parent.Collapse();
        layout.Measure(tree, new Size(300, 200));
        layout.Arrange(tree, new Rect(0, 0, 300, 200));
        Assert.InRange(parent.Geometry.Height, 27, 29);
    }

    [Fact]
    public void LeadingIconReservesSpaceWithoutChangingSelectableText()
    {
        var item = new TreeItem("report.txt") { LeadingIcon = "\uE8A5" };
        item.Geometry = new Rect(10, 20, 200, 28);

        Assert.Equal("report.txt", item.SelectableText);
        Assert.Equal(50, item.SelectableTextBounds.X);
        Assert.True(item.Measure(new Size(300, 30)).Width > 40);
    }
}
