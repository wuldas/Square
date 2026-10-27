using Square.Controls;
using Square.CSS.Engine;
using Square.Graphics;
using Square.Rendering;
using Xunit;

namespace Square.UI.Tests;

public class FlexSizingTests
{
    [Fact]
    public void ExplicitColumnHeightsDoNotShrinkByDefault()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "column");
        var tabList = new View();
        tabList.Style.Set("height", "42px");
        var panels = new View();
        panels.Style.Set("height", "792px");
        root.Children.Add(tabList);
        root.Children.Add(panels);

        Layout(root, new Size(400, 500));

        Assert.Equal(42, tabList.Geometry.Height);
        Assert.Equal(792, panels.Geometry.Height);
    }

    [Fact]
    public void ExplicitFlexShrinkStillAllowsFixedHeightToShrink()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "column");
        var first = new View();
        first.Style.Set("height", "300px");
        first.Style.Set("flex-shrink", "1");
        var second = new View();
        second.Style.Set("height", "300px");
        second.Style.Set("flex-shrink", "1");
        root.Children.Add(first);
        root.Children.Add(second);

        Layout(root, new Size(400, 400));

        Assert.Equal(200, first.Geometry.Height);
        Assert.Equal(200, second.Geometry.Height);
    }

    [Fact]
    public void OverflowAutoContentKeepsExplicitChildHeights()
    {
        var scroller = new View();
        scroller.Style.Set("display", "flex");
        scroller.Style.Set("flex-direction", "column");
        scroller.Style.Set("overflow-y", "auto");
        var first = new View();
        first.Style.Set("height", "180px");
        var second = new View();
        second.Style.Set("height", "180px");
        scroller.Children.Add(first);
        scroller.Children.Add(second);

        Layout(scroller, new Size(400, 200));

        Assert.Equal(180, first.Geometry.Height);
        Assert.Equal(180, second.Geometry.Height);
        Assert.Equal(360, scroller.ScrollContentSize.Height);
        Assert.Equal(160, scroller.ScrollContentSize.Height - scroller.Geometry.Height);
    }

    [Fact]
    public void WrappedTextGeometryUsesTheWidthThatProducedItsHeight()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "column");
        var text = new Square.Controls.Text("Investigation, tools, changes and review share one quiet desktop surface.")
        {
            FontSize = 14
        };
        text.Style.Set("line-height", "22px");
        root.Children.Add(text);

        Layout(root, new Size(220, 120));

        var measuredAtFinalWidth = text.Measure(new Size(text.Geometry.Width, 120));
        Assert.InRange(text.Geometry.Width, 0, root.Geometry.Width);
        Assert.Equal(measuredAtFinalWidth.Height, text.Geometry.Height, 3);
        Assert.True(text.Geometry.Height > 22);
    }

    [Fact]
    public void WrappedButtonAutoHeightTracksWidthAndRespectsExplicitHeight()
    {
        var root = new View();
        root.Style.CssText = "display: flex; flex-direction: column;";
        var button = new Button("Tap this button to check that wrapped text increases its height");
        button.Style.CssText = "width: 800px; box-sizing: border-box; padding: 10px 12px; " +
            "border: 2px solid black; font-size: 16px; line-height: 22px; white-space: normal;";
        root.Children.Add(button);
        new CssEngine().ApplyStylesToTree(root);
        Layout(root, new Size(900, 600));
        Assert.Equal(46, button.Geometry.Height);

        button.Style.Set("width", "180px");
        Layout(root, new Size(900, 600));
        var font = Square.Text.FontManager.Instance.FromCss(button.Style.Get("font-family"), "16px", null, null, 16);
        var text = new TextLayout(button.TextContent, font)
        {
            MaxSize = new Size(152, float.MaxValue),
            LineHeight = 22f / 16
        };
        var lineCount = text.GetVisualLines().Count;
        Assert.True(lineCount > 1);
        Assert.Equal(lineCount * 22 + 24, button.Geometry.Height);
        Assert.Equal(lineCount * 22, button.SelectableTextBounds.Height);
        Assert.True(button.SelectableTextBounds.Width <= 152);

        button.Style.Set("height", "48px");
        Layout(root, new Size(900, 600));
        Assert.Equal(48, button.Geometry.Height);

        button.Style.Set("height", "auto");
        button.Style.Set("width", "800px");
        Layout(root, new Size(900, 600));
        Assert.Equal(46, button.Geometry.Height);
    }

    [Fact]
    public void TextDirectlyInsideRowKeepsIntrinsicSingleLineWidth()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "row");
        root.Style.Set("align-items", "center");
        var text = new Square.Controls.Text("PiSquared") { FontSize = 15 };
        root.Children.Add(text);
        var unconstrained = text.Measure(new Size(float.MaxValue, float.MaxValue));

        Layout(root, new Size(40, 40));

        Assert.InRange(text.Geometry.Width - unconstrained.Width, 0, 1);
        Assert.Equal(unconstrained.Height, text.Geometry.Height, 3);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("none")]
    public void InputAutoHeightGrowsWithFontMetrics(string appearance)
    {
        var root = new View();
        root.Style.CssText = "display: flex; flex-direction: column;";
        var input = new Input { Placeholder = "KeepAlive note" };
        input.Style.Set("appearance", appearance);
        root.Children.Add(input);
        var styles = new CssEngine();
        styles.ApplyStylesToTree(root);

        Layout(root, new Size(300, 100));
        var defaultHeight = input.Geometry.Height;

        input.Style.Set("font-size", "48px");
        Layout(root, new Size(300, 100));

        Assert.Equal(appearance == "auto" ? 21 : 42, defaultHeight);
        Assert.True(input.Geometry.Height > defaultHeight);
        Assert.True(input.Geometry.Height >= 62);
    }

    [Fact]
    public void BareInputUsesAReadableUserAgentHeight()
    {
        var root = new View();
        root.Style.CssText = "display: flex; flex-direction: column;";
        var input = new Input { Placeholder = "KeepAlive note" };
        root.Children.Add(input);
        new CssEngine().ApplyStylesToTree(root);

        Layout(root, new Size(300, 100));

        Assert.Equal(21, input.Geometry.Height);
    }

    [Fact]
    public void BareInputKeepsItsUserAgentMinimumWhenColumnIsConstrained()
    {
        var root = new View();
        root.Style.CssText = "display: flex; flex-direction: column; height: 48px; gap: 6px;";
        var routeLabel = new Square.Controls.Text("Current route") { Style = { CssText = "height: 17px;" } };
        var input = new Input { Placeholder = "KeepAlive note" };
        var draftLabel = new Square.Controls.Text("Saved") { Style = { CssText = "height: 14px;" } };
        root.Children.Add(routeLabel);
        root.Children.Add(input);
        root.Children.Add(draftLabel);
        new CssEngine().ApplyStylesToTree(root);

        Layout(root, new Size(300, 48));

        Assert.Equal(21, input.Geometry.Height);
    }

    [Fact]
    public void ExplicitInputHeightOverridesFontBasedIntrinsicHeight()
    {
        var root = new View();
        root.Style.CssText = "display: flex; flex-direction: column;";
        var input = new Input();
        input.Style.CssText = "height: 30px; font-size: 28px;";
        root.Children.Add(input);
        new CssEngine().ApplyStylesToTree(root);

        Layout(root, new Size(300, 100));

        Assert.Equal(30, input.Geometry.Height);
    }

    [Theory]
    [InlineData("margin-left", "-20px", 40)]
    [InlineData("margin-right", "-20px", 60)]
    public void NegativeHorizontalMarginMatchesCenteredFlexGeometry(
        string property,
        string value,
        float expectedX)
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "row");
        root.Style.Set("justify-content", "center");
        root.Style.Set("align-items", "flex-start");
        var child = new View();
        child.Style.Set("width", "100px");
        child.Style.Set("height", "20px");
        child.Style.Set(property, value);
        root.Children.Add(child);

        Layout(root, new Size(200, 80));

        Assert.Equal(new Rect(expectedX, 0, 100, 20), child.Geometry);
    }

    [Fact]
    public void SplitterNegativeHorizontalMarginsRemoveGapBetweenAdjacentPanels()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "row");
        var left = new View();
        left.Style.Set("width", "100px");
        var splitter = new Splitter();
        splitter.Style.Set("margin-left", "-3px");
        splitter.Style.Set("margin-right", "-3px");
        var right = new View();
        right.Style.Set("width", "100px");
        root.Children.Add(left);
        root.Children.Add(splitter);
        root.Children.Add(right);

        Layout(root, new Size(200, 40));

        Assert.Equal(left.Geometry.Right, right.Geometry.X);
        Assert.Equal(new Rect(97, 0, 6, 40), splitter.Geometry);
        Assert.Equal(3, left.Geometry.Right - splitter.Geometry.X);
        Assert.Equal(3, splitter.Geometry.Right - right.Geometry.X);
    }

    [Fact]
    public void HtmlTextHostFlexItemsExpandEquallyAndWrapAtAssignedWidth()
    {
        var root = new Square.Html.HTMLDivElement();
        var row = new Square.Html.HTMLDivElement();
        // Author min-width:0 keeps the row at its assigned width; the intrinsic max-content
        // floor would otherwise exceed the container because of the long wrapped copy.
        row.Style.CssText = "display: flex; flex-direction: row; gap: 12px; min-width: 0;";
        var first = new Square.Html.HTMLDivElement();
        first.Style.CssText = "flex-grow: 1; flex-basis: 0; min-width: 0; box-sizing: border-box; " +
            "padding: 10px; border: 2px solid black; min-height: 72px;";
        var second = new Square.Html.HTMLDivElement();
        second.Style.CssText = first.Style.CssText;
        // The short copy stays one line at the expanded width; the long copy must wrap into
        // several lines at 409px.
        first.ChildNodes.Add(new Square.UI.Text("Equal expansion text"));
        second.ChildNodes.Add(new Square.UI.Text(
            "Flex panel copy that is long enough to wrap into several lines at the expanded " +
            "width so the host grows past its minimum height like Chrome does. More sentences " +
            "make the wrapped height clearly exceed the 72px floor for every font metric."));
        row.Children.Add(first);
        row.Children.Add(second);
        root.Children.Add(row);
        new CssEngine().ApplyStylesToTree(root);

        new LayoutEngine().MeasureAndArrange(root, new Size(830, 400));

        // flex-basis 0 + equal grow: both DOM-text hosts split 830 - 12 gap evenly, like Chrome.
        Assert.Equal(409, first.Geometry.Width, 3);
        Assert.Equal(409, second.Geometry.Width, 3);
        Assert.Equal(0, first.Geometry.X, 3);
        Assert.Equal(421, second.Geometry.X, 3);

        // Text wraps at the assigned width: the long copy needs more lines than the short one
        // and each line stays inside its own panel's content box.
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        var firstFragments = tree.CollectTextFragments(first);
        var secondFragments = tree.CollectTextFragments(second);
        Assert.True(secondFragments.Count > firstFragments.Count,
            $"expected wrapped copy to use more lines: first={firstFragments.Count} second={secondFragments.Count}");
        AssertTextInsidePanelColumns(firstFragments, first.Geometry);
        AssertTextInsidePanelColumns(secondFragments, second.Geometry);
    }

    [Fact]
    public void HtmlTextHostRowSizesCrossAxisFromItemContentWhenYogaResolvesIt()
    {
        var row = new Square.Html.HTMLDivElement();
        row.Style.CssText = "display: flex; flex-direction: row; gap: 12px; min-width: 0; align-items: flex-start;";
        var first = new Square.Html.HTMLDivElement();
        first.Style.CssText = "flex-grow: 1; flex-basis: 0; min-width: 0; box-sizing: border-box; " +
            "padding: 10px; border: 2px solid black; min-height: 72px;";
        var second = new Square.Html.HTMLDivElement();
        second.Style.CssText = first.Style.CssText;
        first.ChildNodes.Add(new Square.UI.Text("Equal expansion text"));
        second.ChildNodes.Add(new Square.UI.Text(
            "Flex panel copy that is long enough to wrap into several lines at the expanded " +
            "width so the host grows past its minimum height like Chrome does. More sentences " +
            "make the wrapped height clearly exceed the 72px floor for every font metric."));
        row.Children.Add(first);
        row.Children.Add(second);
        new CssEngine().ApplyStylesToTree(row);

        new LayoutEngine().MeasureAndArrange(row, new Size(830, 400));

        Assert.Equal(409, first.Geometry.Width, 3);
        Assert.Equal(409, second.Geometry.Width, 3);
        // Yoga resolves this cross size from the item measures themselves: the single short
        // line sits on the 72px min-height floor while the wrapped copy's border box
        // (measured lines + padding + border) grows past it.
        Assert.Equal(72, first.Geometry.Height, 3);
        Assert.True(second.Geometry.Height > first.Geometry.Height,
            $"expected wrapped copy to exceed the minimum height, got {second.Geometry.Height}");
    }

    [Fact]
    public void RetainedFlexWidthChangesAcrossWrapBoundaryMatchTransient()
    {
        var (root, row, leaves, following) = CreateWrapFixture("120px", "120px", "120px");
        var retained = new LayoutEngine();
        retained.EnableRetainedFlexLayout();

        retained.MeasureAndArrange(root, new Rect(0, 0, 300, 400));
        var frame = CaptureWrapFixture(root, row, following, leaves);
        Assert.Equal(new Rect(0, 0, 120, 40), frame.LeafA);
        Assert.Equal(new Rect(120, 0, 120, 40), frame.LeafB);
        Assert.Equal(new Rect(0, 40, 120, 40), frame.LeafC);
        Assert.Equal(80, frame.Row.Height);
        Assert.Equal(80, frame.Following.Y);
        AssertWrapFixtureMatchesTransient(root, row, following, leaves, frame);

        // 跨折返点一：A 变宽后 B 仍在第一行，C 继续换行。
        leaves[0].Style.Set("width", "160px");
        retained.MeasureAndArrange(root, new Rect(0, 0, 300, 400));
        frame = CaptureWrapFixture(root, row, following, leaves);
        Assert.Equal(new Rect(0, 0, 160, 40), frame.LeafA);
        Assert.Equal(new Rect(160, 0, 120, 40), frame.LeafB);
        Assert.Equal(new Rect(0, 40, 120, 40), frame.LeafC);
        AssertWrapFixtureMatchesTransient(root, row, following, leaves, frame);

        // 跨折返点二：三项同排，行高与后续 section Y 必须同步塌缩。
        leaves[0].Style.Set("width", "60px");
        retained.MeasureAndArrange(root, new Rect(0, 0, 300, 400));
        frame = CaptureWrapFixture(root, row, following, leaves);
        Assert.Equal(new Rect(0, 0, 60, 40), frame.LeafA);
        Assert.Equal(new Rect(60, 0, 120, 40), frame.LeafB);
        Assert.Equal(new Rect(180, 0, 120, 40), frame.LeafC);
        Assert.Equal(40, frame.Row.Height);
        Assert.Equal(40, frame.Following.Y);
        AssertWrapFixtureMatchesTransient(root, row, following, leaves, frame);

        // 回到最初宽度：多次原位更新后仍必须恢复两行布局。
        leaves[0].Style.Set("width", "120px");
        retained.MeasureAndArrange(root, new Rect(0, 0, 300, 400));
        frame = CaptureWrapFixture(root, row, following, leaves);
        Assert.Equal(new Rect(0, 40, 120, 40), frame.LeafC);
        Assert.Equal(80, frame.Row.Height);
        Assert.Equal(80, frame.Following.Y);
        AssertWrapFixtureMatchesTransient(root, row, following, leaves, frame);
    }

    [Fact]
    public void RetainedFlexRebuildsOnDeclarationRemovalMinMaxFontAndStructureChanges()
    {
        var (root, row, leaves, following) = CreateWrapFixture("120px", "8rem", "120px");
        var leafA = leaves[0];
        var leafB = leaves[1];
        var leafC = leaves[2];
        var retained = new LayoutEngine();
        retained.EnableRetainedFlexLayout();

        retained.MeasureAndArrange(root, new Rect(0, 0, 300, 500));
        var frame = CaptureWrapFixture(root, row, following, leaves);
        Assert.Equal(128, frame.LeafB.Width, 3);
        Assert.Equal(new Rect(0, 40, 120, 40), frame.LeafC);
        AssertWrapFixtureMatchesTransient(root, row, following, leaves, frame);

        // 声明移除必须整棵重建：空叶宽度回到内容尺寸 0，不得遗留旧 Yoga 宽度样式。
        leafA.Style.Remove("width");
        retained.MeasureAndArrange(root, new Rect(0, 0, 300, 500));
        frame = CaptureWrapFixture(root, row, following, leaves);
        Assert.Equal(0, frame.LeafA.Width, 3);
        Assert.Equal(128, frame.LeafB.Width, 3);
        Assert.Equal(0, frame.LeafC.Y, 3);
        AssertWrapFixtureMatchesTransient(root, row, following, leaves, frame);

        // min-width 属 Other 类别：同样重建并约束 B。
        leafB.Style.Set("min-width", "200px");
        retained.MeasureAndArrange(root, new Rect(0, 0, 300, 500));
        frame = CaptureWrapFixture(root, row, following, leaves);
        Assert.Equal(200, frame.LeafB.Width, 3);
        Assert.Equal(40, frame.LeafC.Y, 3);
        AssertWrapFixtureMatchesTransient(root, row, following, leaves, frame);

        // 根字号变化改变 rem 解析：B 的 8rem 随之变化并保持 min-width 下限。
        root.Style.Set("font-size", "24px");
        retained.MeasureAndArrange(root, new Rect(0, 0, 300, 500));
        frame = CaptureWrapFixture(root, row, following, leaves);
        Assert.Equal(200, frame.LeafB.Width, 3);
        AssertWrapFixtureMatchesTransient(root, row, following, leaves, frame);

        // max-width 属 Other 类别：限制 C 并把它拉回第一行。
        leafC.Style.Set("max-width", "90px");
        retained.MeasureAndArrange(root, new Rect(0, 0, 300, 500));
        frame = CaptureWrapFixture(root, row, following, leaves);
        Assert.Equal(new Rect(200, 0, 90, 40), frame.LeafC);
        AssertWrapFixtureMatchesTransient(root, row, following, leaves, frame);

        // 声明移除 min-width：B 回到 8rem 解析值 192。
        leafB.Style.Remove("min-width");
        retained.MeasureAndArrange(root, new Rect(0, 0, 300, 500));
        frame = CaptureWrapFixture(root, row, following, leaves);
        Assert.Equal(192, frame.LeafB.Width, 3);
        Assert.Equal(new Rect(192, 0, 90, 40), frame.LeafC);
        AssertWrapFixtureMatchesTransient(root, row, following, leaves, frame);

        // 结构插入：新叶子放不进第一行，换到第二行。
        var leafD = new Square.Html.HTMLDivElement();
        leafD.Style.Set("width", "100px");
        leafD.Style.Set("height", "40px");
        row.Children.Add(leafD);
        retained.MeasureAndArrange(root, new Rect(0, 0, 300, 500));
        frame = CaptureWrapFixture(root, row, following, leaves);
        Assert.Equal(new Rect(0, 40, 100, 40), leafD.Geometry);
        AssertWrapFixtureMatchesTransient(root, row, following, leaves, frame);

        // 结构删除：移除 C 后其余项回到单行。
        row.Children.Remove(leafC);
        retained.MeasureAndArrange(root, new Rect(0, 0, 300, 500));
        frame = CaptureWrapFixture(root, row, following, leaves);
        Assert.Equal(40, frame.Row.Height);
        Assert.Equal(40, frame.Following.Y);
        AssertWrapFixtureMatchesTransient(root, row, following, leaves, frame);
    }

    [Fact]
    public void RetainedFlexWrappedButtonIntrinsicSizingTracksWidthSequence()
    {
        var (retainedRoot, retainedButton) = CreateButtonFixture();
        var (plainRoot, plainButton) = CreateButtonFixture();
        var retained = new LayoutEngine();
        retained.EnableRetainedFlexLayout();
        var plain = new LayoutEngine();

        RunButtonSequence(retained, retainedRoot, retainedButton);
        RunButtonSequence(plain, plainRoot, plainButton);

        Assert.Equal(plainButton.Geometry, retainedButton.Geometry);
        Assert.Equal(plainRoot.ScrollContentSize, retainedRoot.ScrollContentSize);
    }

    private static void RunButtonSequence(LayoutEngine engine, View root, Button button)
    {
        new CssEngine().ApplyStylesToTree(root);
        engine.MeasureAndArrange(root, new Rect(0, 0, 900, 600));
        Assert.Equal(46, button.Geometry.Height);

        button.Style.Set("width", "180px");
        engine.MeasureAndArrange(root, new Rect(0, 0, 900, 600));
        Assert.True(button.Geometry.Height > 46,
            $"expected wrapped button copy to grow the height, got {button.Geometry.Height}");

        button.Style.Set("height", "48px");
        engine.MeasureAndArrange(root, new Rect(0, 0, 900, 600));
        Assert.Equal(48, button.Geometry.Height);

        button.Style.Set("height", "auto");
        button.Style.Set("width", "800px");
        engine.MeasureAndArrange(root, new Rect(0, 0, 900, 600));
        Assert.Equal(46, button.Geometry.Height);
    }

    private static (View Root, Button Button) CreateButtonFixture()
    {
        var root = new View();
        root.Style.CssText = "display: flex; flex-direction: column;";
        var button = new Button("Tap this button to check that wrapped text increases its height");
        button.Style.CssText = "width: 800px; box-sizing: border-box; padding: 10px 12px; " +
            "border: 2px solid black; font-size: 16px; line-height: 22px; white-space: normal;";
        root.Children.Add(button);
        return (root, button);
    }

    private static (View Root, Square.Html.HTMLDivElement Row, List<Square.Html.HTMLDivElement> Leaves,
        Square.Html.HTMLDivElement Following) CreateWrapFixture(string widthA, string widthB, string widthC)
        => CreateWrapFixture<View, Square.Html.HTMLDivElement>(widthA, widthB, widthC);

    private static (TRoot, TRow, List<TRow>, TRow) CreateWrapFixture<TRoot, TRow>(
        string widthA, string widthB, string widthC)
        where TRoot : Element, new()
        where TRow : Element, new()
    {
        var root = new TRoot();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "column");
        var row = new TRow();
        row.Style.Set("display", "flex");
        row.Style.Set("flex-wrap", "wrap");
        var leaves = new List<TRow> { new(), new(), new() };
        var widths = new[] { widthA, widthB, widthC };
        for (var i = 0; i < leaves.Count; i++)
        {
            leaves[i].Style.Set("width", widths[i]);
            leaves[i].Style.Set("height", "40px");
            row.Children.Add(leaves[i]);
        }
        var following = new TRow();
        following.Style.Set("height", "10px");
        root.Children.Add(row);
        root.Children.Add(following);
        return (root, row, leaves, following);
    }

    private sealed record WrapFixtureCapture(
        Rect Row, Rect Following, Size RootExtents, Rect LeafA, Rect LeafB, Rect LeafC);

    private static WrapFixtureCapture CaptureWrapFixture(
        Element root, Element row, Element following, IReadOnlyList<Element> leaves) => new(
        row.Geometry,
        following.Geometry,
        root.ScrollContentSize,
        leaves[0].Geometry,
        leaves[1].Geometry,
        leaves[2].Geometry);

    private static void AssertWrapFixtureMatchesTransient(
        Element root, Element row, Element following, IReadOnlyList<Element> leaves,
        WrapFixtureCapture retained)
    {
        new LayoutEngine().MeasureAndArrange(root, root.Geometry);
        var transient = CaptureWrapFixture(root, row, following, leaves);
        Assert.Equal(retained.Row, transient.Row);
        Assert.Equal(retained.Following, transient.Following);
        Assert.Equal(retained.RootExtents, transient.RootExtents);
        Assert.Equal(retained.LeafA, transient.LeafA);
        Assert.Equal(retained.LeafB, transient.LeafB);
        Assert.Equal(retained.LeafC, transient.LeafC);
    }


    private static void AssertTextInsidePanelColumns(List<TextFragment> fragments, Rect panel)
    {
        Assert.NotEmpty(fragments);
        Assert.All(fragments, fragment =>
        {
            Assert.True(fragment.Bounds.X >= panel.X + 10,
                $"fragment {fragment.Bounds} left of panel {panel} content box");
            Assert.True(fragment.Bounds.Right <= panel.Right - 10,
                $"fragment {fragment.Bounds} right of panel {panel} content box");
        });
    }

    private static void AssertTextInsidePanel(List<TextFragment> fragments, Rect panel)
    {
        Assert.NotEmpty(fragments);
        Assert.All(fragments, fragment =>
        {
            Assert.True(fragment.Bounds.X >= panel.X + 10,
                $"fragment {fragment.Bounds} left of panel {panel} content box");
            Assert.True(fragment.Bounds.Right <= panel.Right - 10,
                $"fragment {fragment.Bounds} right of panel {panel} content box");
            Assert.True(fragment.Bounds.Y >= panel.Y,
                $"fragment {fragment.Bounds} above panel {panel}");
            Assert.True(fragment.Bounds.Bottom <= panel.Bottom,
                $"fragment {fragment.Bounds} below panel {panel}");
        });
    }

    private static void Layout(View root, Size size)
    {
        var layout = new LayoutEngine();
        layout.Measure(root, size);
        layout.Arrange(root, new Rect(0, 0, size.Width, size.Height));
    }
}
