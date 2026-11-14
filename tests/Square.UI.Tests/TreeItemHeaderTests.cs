using System.Globalization;
using Square.Controls;
using Square.Events;
using Square.Graphics;
using Square.Rendering;
using Square.Runtime;
using Square.Runtime.Binding;
using Square.UI;
using Xunit;
using ControlText = Square.Controls.Text;

namespace Square.UI.Tests;

public class TreeItemHeaderTests
{
    [Fact]
    public void HeaderUsesNaturalHeightAndOnlyChildItemsCollapse()
    {
        var header = Sized(120, 48);
        var item = new TreeItem("Fallback") { Header = header, IsExpanded = true };
        var child = new TreeItem("Child");
        item.Children.Add(child);
        var tree = new Tree();
        tree.Children.Add(item);
        var layout = new LayoutEngine();

        layout.MeasureAndArrange(tree, new Size(300, 200));

        Assert.Same(header, item.Header);
        Assert.Same(child, Assert.Single(item.Items));
        Assert.Equal(48, header.Geometry.Height);
        Assert.Equal(item.Geometry.Y + 48, child.Geometry.Y);
        Assert.Equal(item.Geometry.X + 18, child.Geometry.X);
        Assert.Equal(item.Geometry.X + 20, header.Geometry.X);
        Assert.Equal(76, item.Geometry.Height);

        item.Collapse();
        layout.MeasureAndArrange(tree, new Size(300, 200));

        Assert.True(header.IsEffectivelyVisible);
        Assert.False(child.IsVisible);
        Assert.Equal(48, item.Geometry.Height);
    }

    [Fact]
    public void SmallHeaderKeepsTheLegacyMinimumRowHeight()
    {
        var header = Sized(80, 12);
        var item = new TreeItem { Header = header };
        var tree = new Tree();
        tree.Children.Add(item);

        new LayoutEngine().MeasureAndArrange(tree, new Size(200, 100));

        Assert.Equal(28, item.Geometry.Height);
        Assert.Equal(item.Geometry.Y + 8, header.Geometry.Y);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangingHeaderSizeReplacingAndClearingItInvalidatesLayout(bool retained)
    {
        var header = Sized(100, 36);
        var item = new TreeItem("Fallback") { Header = header, IsExpanded = true };
        var child = new TreeItem("Child");
        item.Children.Add(child);
        var tree = new Tree();
        tree.Children.Add(item);
        var layout = new LayoutEngine();
        if (retained) layout.EnableRetainedFlexLayout();
        layout.MeasureAndArrange(tree, new Size(300, 240));

        header.Style.Set("height", "64px");
        Assert.True(tree.IsLayoutDirty);
        layout.MeasureAndArrange(tree, new Size(300, 240));
        Assert.Equal(item.Geometry.Y + 64, child.Geometry.Y);

        var replacement = Sized(90, 40);
        item.Header = replacement;
        Assert.Null(header.Parent);
        Assert.True(tree.IsLayoutDirty);
        layout.MeasureAndArrange(tree, new Size(300, 240));
        Assert.Equal(item.Geometry.Y + 40, child.Geometry.Y);

        item.Header = null;
        Assert.Null(replacement.Parent);
        layout.MeasureAndArrange(tree, new Size(300, 240));
        Assert.Equal(item.Geometry.Y + 28, child.Geometry.Y);
        Assert.Equal("Fallback", item.SelectableText);
    }

    [Fact]
    public void ChangingHeaderHeightRepaintsEvenWhenTheItemsTotalHeightIsFixed()
    {
        var header = Sized(100, 36);
        var item = new TreeItem { Header = header, IsSelected = true };
        item.Style.Set("height", "100px");
        var tree = new Tree();
        tree.Children.Add(item);
        var layout = new LayoutEngine();
        layout.MeasureAndArrange(tree, new Size(300, 200));
        item.ClearPaintDirty();

        header.Style.Set("height", "64px");
        layout.MeasureAndArrange(tree, new Size(300, 200));

        Assert.Equal(100, item.Geometry.Height);
        Assert.True(item.NeedsPaint);
        var context = new RecordingContext();
        item.Paint(context);
        Assert.Equal(64, Assert.Single(context.Fills).Height);
    }

    [Fact]
    public void HeaderSlotProjectsACompositionOnceWithoutConsumingChildItems()
    {
        var item = new TreeItem("Fallback") { IsExpanded = true };
        var child = new TreeItem("Child");
        item.Children.Add(child);
        var image = new Square.Controls.Image();
        image.Style.Set("width", "16px");
        image.Style.Set("height", "44px");
        var text = new ControlText("Title");
        var button = new Button("Action");
        button.Style.Set("width", "70px");
        button.Style.Set("height", "32px");
        var projections = 0;
        item.Slots.Set("header", parent =>
        {
            projections++;
            parent.Children.Add(image);
            parent.Children.Add(text);
            parent.Children.Add(button);
        });

        item.BuildElementTree();
        item.BuildElementTree();
        var tree = new Tree();
        tree.Children.Add(item);
        new LayoutEngine().MeasureAndArrange(tree, new Size(300, 200));

        Assert.Equal(1, projections);
        Assert.NotNull(item.Header);
        Assert.Same(item.Header, image.Parent);
        Assert.Same(item.Header, text.Parent);
        Assert.Same(item.Header, button.Parent);
        Assert.Same(child, Assert.Single(item.Items));
        Assert.Equal(item.Geometry.Y + 44, child.Geometry.Y);
        Assert.True(text.Geometry.X >= image.Geometry.Right);
        Assert.True(button.Geometry.X >= text.Geometry.Right);
    }

    [Fact]
    public void EmptyHeaderSlotKeepsTheMinimumRowHeight()
    {
        var item = new TreeItem("Hidden fallback");
        item.Slots.Set("header", _ => { });
        item.BuildElementTree();
        var tree = new Tree();
        tree.Children.Add(item);

        new LayoutEngine().MeasureAndArrange(tree, new Size(300, 200));

        Assert.Equal(28, item.Geometry.Height);
        Assert.NotNull(item.Header);
        Assert.Empty(item.SelectableText);
    }

    [Fact]
    public void ExplicitHeaderOverridesTheSlotAndClearingRestoresFallback()
    {
        var explicitHeader = Sized(100, 40);
        var item = new TreeItem("Fallback") { Header = explicitHeader };
        var projections = 0;
        item.Slots.Set("header", parent =>
        {
            projections++;
            parent.Children.Add(new ControlText("Slot"));
        });
        item.BuildElementTree();

        Assert.Same(explicitHeader, item.Header);
        Assert.Equal(0, projections);

        // Once a slot has actually rendered, clearing its replacement does not replay it.
        item.Header = null;
        item.BuildElementTree();
        Assert.Equal(1, projections);
        item.Header = new ControlText("Replacement");
        item.Header = null;
        item.BuildElementTree();
        Assert.Null(item.Header);
        Assert.Equal("Fallback", item.SelectableText);
        Assert.Equal(1, projections);
    }

    [Fact]
    public void CustomHeaderSuppressesFallbackPaintAndSelectableText()
    {
        var item = new TreeItem("Hidden fallback")
        {
            LeadingIcon = "!", Header = Sized(100, 46), IsSelected = true, IsExpanded = true
        };
        item.Children.Add(new TreeItem("Child"));
        var tree = new Tree();
        tree.Children.Add(item);
        new LayoutEngine().MeasureAndArrange(tree, new Size(300, 200));
        var context = new RecordingContext();

        item.Paint(context);

        Assert.Empty(context.Texts);
        Assert.Empty(item.SelectableText);
        Assert.Equal(Rect.Empty, item.SelectableTextBounds);
        Assert.Equal(46, Assert.Single(context.Fills).Height);
        var marker = Assert.Single(context.Paths);
        Assert.Equal(item.Geometry.Y + 21, Assert.IsType<MoveToCmd>(marker.Commands[0]).Point.Y);
    }

    [Fact]
    public void ButtonDescendantClickRunsItsHandlerWithoutSelectingOrExpandingTheTree()
    {
        var label = new ControlText("Action");
        var button = new Button();
        button.Children.Add(label);
        var item = new TreeItem { Header = button };
        item.Children.Add(new TreeItem("Child"));
        var tree = new Tree();
        tree.Children.Add(item);
        var clicks = 0;
        button.AddEventListener(StandardEvents.Click, () => clicks++);

        label.DispatchEvent(StandardEvents.CreateClick());

        Assert.Equal(1, clicks);
        Assert.Null(tree.SelectedItem);
        Assert.False(item.IsExpanded);
        Assert.False(tree.IsFocused);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckBoxHeaderDoesNotToggleTheBranch(bool triState)
    {
        UIElement control = triState ? new TriStateCheckBox() : new CheckBox();
        var item = new TreeItem { Header = control };
        item.Children.Add(new TreeItem("Child"));
        var tree = new Tree();
        tree.Children.Add(item);

        control.DispatchEvent(StandardEvents.CreateClick());

        Assert.False(item.IsExpanded);
        Assert.Null(tree.SelectedItem);
    }

    [Fact]
    public void PlainHeaderClicksSelectAndToggleWhileStopPropagationStillWorks()
    {
        var header = new ControlText("Title");
        var item = new TreeItem { Header = header };
        item.Children.Add(new TreeItem("Child"));
        var tree = new Tree();
        tree.Children.Add(item);

        header.DispatchEvent(StandardEvents.CreateClick());
        Assert.Same(item, tree.SelectedItem);
        Assert.True(item.IsExpanded);
        Assert.True(tree.IsFocused);

        header.AddEventListener(StandardEvents.Click, e => e.StopPropagation());
        header.DispatchEvent(StandardEvents.CreateClick());
        Assert.True(item.IsExpanded);
    }

    [Fact]
    public void HeaderControlKeysStayLocalAndTreeKeyboardNavigationStillWorks()
    {
        var input = new Input();
        var item = new TreeItem { Header = input };
        var child = new TreeItem("Child");
        item.Children.Add(child);
        var tree = new Tree();
        tree.Children.Add(item);
        tree.SelectItem(item);

        input.DispatchEvent(StandardEvents.CreateKeyDown(39));
        Assert.False(item.IsExpanded);
        tree.DispatchEvent(StandardEvents.CreateKeyDown(39));
        Assert.True(item.IsExpanded);
        tree.DispatchEvent(StandardEvents.CreateKeyDown(40));
        Assert.Same(child, tree.SelectedItem);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(40)]
    public void VirtualRowsClipTallHeadersAndAlignPaintAndHitTestingToItemHeight(float height)
    {
        var tree = CreateVirtualTree(height, _ => Sized(80, 70));
        var layout = new LayoutEngine();
        layout.MeasureAndArrange(tree, new Size(300, height * 2));
        tree.SelectIndex(0);
        var rows = tree.Children.OfType<TreeItem>().ToArray();
        var first = rows[0];
        var second = rows[1];
        var header = first.Header!;
        var host = header.Parent!;
        var context = new RecordingContext();

        first.Paint(context);

        Assert.Equal(height, first.Geometry.Height);
        Assert.Equal(height, host.Geometry.Height);
        Assert.Equal(height, host.GetOverflowClipRect().Height);
        Assert.Equal(first.Geometry.Y + height, second.Geometry.Y);
        Assert.Equal(height * 2, tree.ExtentHeight);
        Assert.Equal(height, Assert.Single(context.Fills).Height);
        var marker = Assert.Single(context.Paths);
        Assert.Equal(first.Geometry.Y + height / 2 - 4,
            Assert.IsType<MoveToCmd>(marker.Commands[0]).Point.Y);
        Assert.Same(second.Header, tree.HitTest(new Point(second.Geometry.X + 25, second.Geometry.Y + 1)));

        first.Header = Sized(80, 100);
        layout.MeasureAndArrange(tree, new Size(300, height * 2));
        Assert.Equal(height, first.Header.Parent!.Geometry.Height);
        Assert.Equal(height, first.Header.Parent.GetOverflowClipRect().Height);
        Assert.Equal(height * 2, tree.ExtentHeight);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(40)]
    public void VirtualFallbackPaintUsesTheConfiguredRowHeight(float height)
    {
        var tree = new VirtualTree { ItemHeight = height, OverscanCount = 2 };
        tree.SetItemsSource(CreateNodes(), node => node.Children,
            (node, _) => new TreeItem(node.Name));
        new LayoutEngine().MeasureAndArrange(tree, new Size(300, 180));
        tree.SelectIndex(0);
        var row = tree.Children.OfType<TreeItem>().First();
        var context = new RecordingContext();

        row.Paint(context);

        Assert.Equal(height, Assert.Single(context.Fills).Height);
        Assert.Equal(row.Geometry.Y + 5 + (height - 28) / 2, row.SelectableTextBounds.Y);
    }

    [Fact]
    public void VirtualTreeHeaderControlsDoNotChangeLogicalSelectionOrExpansion()
    {
        var tree = CreateVirtualTree(40, _ => new Button("Action"));
        new LayoutEngine().MeasureAndArrange(tree, new Size(300, 160));
        var first = tree.Children.OfType<TreeItem>().First();

        first.Header!.DispatchEvent(StandardEvents.CreateClick());
        first.Header.DispatchEvent(StandardEvents.CreateKeyDown(39));

        Assert.Equal(-1, tree.SelectedIndex);
        Assert.Equal(2, tree.VisibleItemCount);

        // The row itself and normal header content retain tree behavior.
        first.Header = new ControlText("Root");
        first.Header.DispatchEvent(StandardEvents.CreateClick());
        Assert.Equal(0, tree.SelectedIndex);
        Assert.Equal(3, tree.VisibleItemCount);
        Assert.True(tree.HandleKey(39));
        Assert.Equal("Child", Assert.IsType<Node>(tree.SelectedValue).Name);
    }

    [Theory]
    [InlineData("parent", false)]
    [InlineData("self", false)]
    [InlineData("ancestor", false)]
    [InlineData("parent", true)]
    [InlineData("self", true)]
    [InlineData("ancestor", true)]
    public void GenericHeaderWritesValidateBeforeUnmountingThePreviousHeader(string invalidKind, bool bind)
    {
        var oldHeader = new LifecycleHeader();
        var item = new TreeItem { Header = oldHeader };
        var tree = new Tree();
        tree.Children.Add(item);
        ((IComponentLifecycle)tree).OnAttached();
        ((IComponentLifecycle)tree).OnLoaded();
        var originalHost = oldHeader.Parent!;
        var candidate = InvalidHeader(invalidKind, item, tree);

        Assert.Throws<InvalidOperationException>(() =>
        {
            if (bind) item.BindProperty(nameof(TreeItem.Header), () => candidate);
            else item.SetProperty(nameof(TreeItem.Header), candidate);
        });

        Assert.Same(oldHeader, item.Header);
        Assert.Same(oldHeader, item.GetProperty<Element>(nameof(TreeItem.Header)));
        Assert.Same(originalHost, oldHeader.Parent);
        Assert.Same(oldHeader, Assert.Single(originalHost.Children));
        Assert.Same(item, Assert.Single(tree.Children.OfType<TreeItem>()));
        Assert.Null(tree.Parent);
        Assert.True(oldHeader.IsAttached);
        Assert.True(oldHeader.IsLoaded);
        Assert.Equal(1, oldHeader.Attaches);
        Assert.Equal(1, oldHeader.Loads);
        Assert.Equal(0, oldHeader.Unloads);
        Assert.Equal(0, oldHeader.Detaches);
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("self")]
    [InlineData("ancestor")]
    public void ReactiveHeaderWritesRestoreTheAppliedValueAndIgnoreRepeatedInstances(string invalidKind)
    {
        var oldHeader = new LifecycleHeader();
        var source = new ObservableValue<Element?>(oldHeader);
        var item = new TreeItem();
        item.BindProperty(nameof(TreeItem.Header), source);
        var tree = new Tree();
        tree.Children.Add(item);
        ((IComponentLifecycle)tree).OnAttached();
        ((IComponentLifecycle)tree).OnLoaded();
        var originalHost = oldHeader.Parent!;

        source.Notify();
        item.SetProperty(nameof(TreeItem.Header), oldHeader);
        item.BindProperty(nameof(TreeItem.Header), () => oldHeader);
        Assert.Equal(1, oldHeader.Attaches);
        Assert.Equal(1, oldHeader.Loads);
        Assert.Equal(0, oldHeader.Unloads);
        Assert.Equal(0, oldHeader.Detaches);

        var candidate = InvalidHeader(invalidKind, item, tree);
        Assert.Throws<InvalidOperationException>(() => source.Value = candidate);
        Assert.Throws<InvalidOperationException>(() => source.Notify());
        Assert.Same(oldHeader, item.Header);
        Assert.Same(oldHeader, item.GetProperty<Element>(nameof(TreeItem.Header)));
        Assert.Same(originalHost, oldHeader.Parent);
        Assert.Same(item, Assert.Single(tree.Children.OfType<TreeItem>()));
        Assert.Null(tree.Parent);
        Assert.True(oldHeader.IsAttached);
        Assert.True(oldHeader.IsLoaded);
        Assert.Equal(0, oldHeader.Unloads);
        Assert.Equal(0, oldHeader.Detaches);

        source.Value = oldHeader;
        source.Notify();
        Assert.Equal(1, oldHeader.Attaches);
        Assert.Equal(1, oldHeader.Loads);
        Assert.Equal(0, oldHeader.Unloads);
        Assert.Equal(0, oldHeader.Detaches);

        var replacement = new LifecycleHeader();
        source.Value = replacement;
        source.Notify();
        Assert.Same(replacement, item.Header);
        Assert.Equal(1, oldHeader.Unloads);
        Assert.Equal(1, oldHeader.Detaches);
        Assert.Equal(1, replacement.Attaches);
        Assert.Equal(1, replacement.Loads);
        Assert.Equal(0, replacement.Unloads);
        Assert.Equal(0, replacement.Detaches);
    }

    private static Element InvalidHeader(string kind, TreeItem item, Tree tree)
    {
        if (kind == "self") return item;
        if (kind == "ancestor") return tree;
        var candidate = new View();
        new View().Children.Add(candidate);
        return candidate;
    }

    [Fact]
    public void ReplacingAnAttachedHeaderRunsTheNormalUnmountAndMountLifecycle()
    {
        var oldHeader = new LifecycleHeader();
        var item = new TreeItem { Header = oldHeader };
        ((IComponentLifecycle)item).OnAttached();
        ((IComponentLifecycle)item).OnLoaded();
        var replacement = new LifecycleHeader();

        item.Header = replacement;

        Assert.Equal(1, oldHeader.Attaches);
        Assert.Equal(1, oldHeader.Loads);
        Assert.Equal(1, oldHeader.Unloads);
        Assert.Equal(1, oldHeader.Detaches);
        Assert.False(oldHeader.IsAttached);
        Assert.Null(oldHeader.Parent);
        Assert.Equal(1, replacement.Attaches);
        Assert.Equal(1, replacement.Loads);

        item.Header = null;
        Assert.Equal(1, replacement.Unloads);
        Assert.Equal(1, replacement.Detaches);
        Assert.Null(replacement.Parent);
    }

    [Fact]
    public void VirtualTreeUnloadsOldHeadersWhenRowsLeaveTheRealizedRange()
    {
        var tree = new VirtualTree { ItemHeight = 40, OverscanCount = 0 };
        tree.SetItemsSource(Enumerable.Range(0, 20).ToArray(), _ => Array.Empty<int>(),
            (value, _) => new TreeItem(value.ToString(CultureInfo.InvariantCulture)) { Header = new LifecycleHeader() });
        var layout = new LayoutEngine();
        layout.MeasureAndArrange(tree, new Size(300, 80));
        ((IComponentLifecycle)tree).OnAttached();
        ((IComponentLifecycle)tree).OnLoaded();
        var oldRow = tree.Children.OfType<TreeItem>().First();
        var oldHeader = Assert.IsType<LifecycleHeader>(oldRow.Header);
        tree.SelectIndex(0);

        tree.ScrollIntoView(15);
        layout.MeasureAndArrange(tree, new Size(300, 80));

        Assert.False(oldRow.IsAttached);
        Assert.Equal(1, oldHeader.Unloads);
        Assert.Equal(1, oldHeader.Detaches);
        Assert.Equal(0, tree.SelectedIndex);
        Assert.All(tree.Children.OfType<TreeItem>(), row => Assert.True(row.Header!.IsAttached));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetiredVirtualRowsReleaseHeaderBindingsAndGeneratedResources(bool replaceSource)
    {
        var textSource = new ObservableValue<string>("before");
        var checkSource = new ObservableValue<bool?>(false);
        var created = new List<BoundRow>();
        TreeItem Template(int value, int depth)
        {
            var state = CreateBoundRow(textSource, checkSource);
            created.Add(state);
            return state.Row;
        }
        var tree = new VirtualTree { ItemHeight = 40, OverscanCount = 0 };
        tree.SetItemsSource(Enumerable.Range(0, 20).ToArray(), _ => Array.Empty<int>(), Template);
        var layout = new LayoutEngine();
        layout.MeasureAndArrange(tree, new Size(300, 80));
        ((IComponentLifecycle)tree).OnAttached();
        ((IComponentLifecycle)tree).OnLoaded();
        var oldRow = tree.Children.OfType<TreeItem>().First();
        var retired = created.Single(state => ReferenceEquals(state.Row, oldRow));
        textSource.Value = "active";
        checkSource.Value = true;
        Assert.Equal("active", retired.Text.TextContent);
        Assert.True(retired.Check.IsChecked);

        if (replaceSource)
            tree.SetItemsSource(Enumerable.Range(100, 20).ToArray(), _ => Array.Empty<int>(), Template);
        else
            tree.ScrollIntoView(15);
        layout.MeasureAndArrange(tree, new Size(300, 80));
        textSource.Value = "after retirement";
        checkSource.Value = null;

        Assert.True(retired.Row.IsGeneratedSubtreeDiscarded);
        Assert.True(retired.Text.IsGeneratedSubtreeDiscarded);
        Assert.True(retired.Check.IsGeneratedSubtreeDiscarded);
        Assert.Equal("active", retired.Row.TextContent);
        Assert.Equal("active", retired.Text.TextContent);
        Assert.True(retired.Check.IsChecked);
        Assert.Equal(1, retired.RowResource.DisposeCount);
        Assert.Equal(1, retired.HeaderResource.DisposeCount);
        foreach (var row in tree.Children.OfType<TreeItem>())
        {
            var active = created.Single(state => ReferenceEquals(state.Row, row));
            Assert.Equal("after retirement", active.Text.TextContent);
            Assert.Null(active.Check.IsChecked);
            Assert.Equal(0, active.RowResource.DisposeCount);
            Assert.Equal(0, active.HeaderResource.DisposeCount);
        }

        tree.OverscanCount = 1;
        layout.MeasureAndArrange(tree, new Size(300, 80));
        ((IComponentLifecycle)tree).OnUnloaded();
        ((IComponentLifecycle)tree).OnDetached();
        tree.DiscardGeneratedSubtree();
        Assert.All(created, state =>
        {
            Assert.Equal(1, state.RowResource.DisposeCount);
            Assert.Equal(1, state.HeaderResource.DisposeCount);
        });
    }

    [Fact]
    public void RetainedRowsAndOrdinaryTreeDetachKeepBindingsUntilActualRetirement()
    {
        var textSource = new ObservableValue<string>("initial");
        var checkSource = new ObservableValue<bool?>(false);
        var state = CreateBoundRow(textSource, checkSource);
        var tree = new VirtualTree { ItemHeight = 40, OverscanCount = 0 };
        tree.SetItemsSource(new[] { 0 }, _ => Array.Empty<int>(), (_, _) => state.Row);
        var layout = new LayoutEngine();
        layout.MeasureAndArrange(tree, new Size(300, 80));
        ((IComponentLifecycle)tree).OnAttached();
        ((IComponentLifecycle)tree).OnLoaded();

        // Re-realization may retain the same still-live instance from an item template.
        tree.ItemHeight = 50;
        layout.MeasureAndArrange(tree, new Size(300, 80));
        Assert.Same(state.Row, Assert.Single(tree.Children.OfType<TreeItem>()));
        Assert.False(state.Row.IsGeneratedSubtreeDiscarded);
        Assert.Equal(0, state.RowResource.DisposeCount);
        Assert.Equal(0, state.HeaderResource.DisposeCount);

        ((IComponentLifecycle)tree).OnUnloaded();
        ((IComponentLifecycle)tree).OnDetached();
        textSource.Value = "detached";
        checkSource.Value = null;
        Assert.Equal("detached", state.Text.TextContent);
        Assert.Null(state.Check.IsChecked);
        Assert.Equal(0, state.RowResource.DisposeCount);
        Assert.Equal(0, state.HeaderResource.DisposeCount);

        ((IComponentLifecycle)tree).OnAttached();
        ((IComponentLifecycle)tree).OnLoaded();
        textSource.Value = "reattached";
        Assert.Equal("reattached", state.Row.TextContent);
        Assert.Equal("reattached", state.Text.TextContent);
        tree.SetItemsSource<int>(null, _ => Array.Empty<int>());
        Assert.Equal(1, state.RowResource.DisposeCount);
        Assert.Equal(1, state.HeaderResource.DisposeCount);
        textSource.Value = "retired";
        Assert.Equal("reattached", state.Text.TextContent);
        Assert.Throws<InvalidOperationException>(() =>
            tree.SetItemsSource(new[] { 0 }, _ => Array.Empty<int>(), (_, _) => state.Row));
        Assert.Equal(1, state.RowResource.DisposeCount);
        Assert.Equal(1, state.HeaderResource.DisposeCount);
    }

    private static BoundRow CreateBoundRow(ObservableValue<string> textSource, ObservableValue<bool?> checkSource)
    {
        var header = new View();
        header.Style.Set("display", "flex");
        header.Style.Set("flex-direction", "row");
        var text = new ControlText();
        text.BindProperty(nameof(ControlText.TextContent), textSource);
        var check = new TriStateCheckBox();
        check.BindProperty(nameof(TriStateCheckBox.IsChecked), checkSource);
        header.Children.Add(text);
        header.Children.Add(check);
        var row = new TreeItem { Header = header };
        row.BindProperty(nameof(TreeItem.TextContent), textSource);
        var rowResource = new CountingResource();
        var headerResource = new CountingResource();
        row.RegisterGeneratedResource(rowResource);
        header.RegisterGeneratedResource(headerResource);
        return new BoundRow(row, text, check, rowResource, headerResource);
    }

    private sealed record BoundRow(TreeItem Row, ControlText Text, TriStateCheckBox Check,
        CountingResource RowResource, CountingResource HeaderResource);

    private sealed class CountingResource : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    private static View Sized(float width, float height)
    {
        var view = new View();
        view.Style.Set("width", width.ToString(CultureInfo.InvariantCulture) + "px");
        view.Style.Set("height", height.ToString(CultureInfo.InvariantCulture) + "px");
        view.Style.Set("flex-shrink", "0");
        return view;
    }

    private static VirtualTree CreateVirtualTree(float height, Func<Node, Element> header)
    {
        var tree = new VirtualTree { ItemHeight = height, OverscanCount = 3 };
        tree.SetItemsSource(CreateNodes(), node => node.Children,
            (node, _) => new TreeItem(node.Name) { Header = header(node) });
        return tree;
    }

    private static Node[] CreateNodes() =>
    [
        new("Root", [new("Child", [])]),
        new("Sibling", [])
    ];

    private sealed record Node(string Name, IReadOnlyList<Node> Children);

    private sealed class LifecycleHeader : View
    {
        public int Attaches { get; private set; }
        public int Loads { get; private set; }
        public int Unloads { get; private set; }
        public int Detaches { get; private set; }
        protected override void OnAttachedCore() { base.OnAttachedCore(); Attaches++; }
        protected override void OnLoadedCore() { base.OnLoadedCore(); Loads++; }
        protected override void OnUnloadedCore() { Unloads++; base.OnUnloadedCore(); }
        protected override void OnDetachedCore() { Detaches++; base.OnDetachedCore(); }
    }

    private sealed class RecordingContext : IRenderContext
    {
        public List<Rect> Fills { get; } = [];
        public List<PathGeometry> Paths { get; } = [];
        public List<TextLayout> Texts { get; } = [];
        public Size CanvasSize => new(300, 200);
        public float DpiScale => 1;
        public void PushTransform(System.Numerics.Matrix3x2 matrix) { }
        public void PopTransform() { }
        public void PushClip(Rect rect) { }
        public void PushClip(Geometry geometry) { }
        public void PopClip() { }
        public void FillRect(Rect rect, Brush brush) => Fills.Add(rect);
        public void DrawRect(Rect rect, Pen pen) { }
        public void FillPath(PathGeometry path, Brush brush) { }
        public void DrawPath(PathGeometry path, Pen pen) => Paths.Add(path);
        public void FillGeometry(Geometry geometry, Brush brush) { }
        public void DrawGeometry(Geometry geometry, Pen pen) { }
        public void DrawText(TextLayout text, Point origin, Brush brush) => Texts.Add(text);
        public void DrawImage(Square.Graphics.Image image, Rect dest, Rect? source = null) { }
        public void PushLayer(Rect bounds, float opacity) { }
        public void PopLayer() { }
        public void Clear(Color color) { }
        public void Clear(Color color, Rect rect) { }
        public void Flush() { }
        public void Present() { }
        public void Present(IReadOnlyList<Rect>? dirtyRects) { }
        public void Dispose() { }
    }
}
