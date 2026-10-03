using Square.Controls;
using Square.Html;
using Square.Graphics;
using Square.Rendering;
using Xunit;

namespace Square.UI.Tests;

public class CssBlockFormattingTests
{
    [Fact]
    public void ContentBoxWidthAndAutoMarginsCenterBlock()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var child = new View();
        child.Style.Set("display", "block");
        child.Style.Set("width", "100px");
        child.Style.Set("height", "20px");
        child.Style.Set("padding", "10px");
        child.Style.Set("margin-left", "auto");
        child.Style.Set("margin-right", "auto");
        root.Children.Add(child);

        Layout(root, 300, 100);

        Assert.Equal(new Rect(90, 0, 120, 40), child.Geometry);
    }

    [Fact]
    public void InspectionBoxModelUsesAsymmetricCssEdges()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var child = new View();
        child.Style.Set("display", "block");
        child.Style.Set("width", "80px");
        child.Style.Set("height", "40px");
        child.Style.Set("padding", "2px 4px 6px 8px");
        child.Style.Set("border-width", "1px 2px 3px 4px");
        child.Style.Set("margin", "5px 7px 9px 11px");
        root.Children.Add(child);

        var layout = LayoutAndReturn(root, 200, 100);
        var box = layout.GetInspectionBoxModel(child);
        Assert.NotNull(box);

        Assert.Equal(new Rect(23, 8, 80, 40), box.Value.Content);
        Assert.Equal(new Rect(15, 6, 92, 48), box.Value.Padding);
        Assert.Equal(new Rect(11, 5, 98, 52), box.Value.Border);
        Assert.Equal(new Rect(0, 0, 116, 66), box.Value.Margin);
    }

    [Fact]
    public void AutoWidthFillsContainingBlockMarginBox()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var child = new View();
        child.Style.Set("display", "block");
        child.Style.Set("height", "10px");
        child.Style.Set("margin", "0 15px");
        child.Style.Set("padding", "0 10px");
        root.Children.Add(child);

        Layout(root, 200, 50);

        Assert.Equal(new Rect(15, 0, 170, 10), child.Geometry);
    }

    [Fact]
    public void AdjacentVerticalMarginsCollapse()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var first = Block(20);
        first.Style.Set("margin-bottom", "30px");
        var second = Block(20);
        second.Style.Set("margin-top", "20px");
        root.Children.Add(first);
        root.Children.Add(second);

        Layout(root, 100, 100);

        Assert.Equal(0, first.Geometry.Y);
        Assert.Equal(50, second.Geometry.Y);
    }

    [Fact]
    public void ClearancePreventsMarginCollapseWithPreviousBlock()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var floated = Block(20);
        floated.Style.Set("float", "left");
        var first = Block(10);
        first.Style.Set("margin-bottom", "30px");
        var cleared = Block(10);
        cleared.Style.Set("clear", "both");
        cleared.Style.Set("margin-top", "20px");
        root.Children.Add(floated);
        root.Children.Add(first);
        root.Children.Add(cleared);

        Layout(root, 100, 100);

        Assert.Equal(40, cleared.Geometry.Y);
    }

    [Fact]
    public void MinHeightPreventsLastChildMarginFromCollapsingThroughContainer()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var container = Block(10);
        container.Style.Set("min-height", "30px");
        var child = Block(10);
        child.Style.Set("margin-bottom", "20px");
        container.Children.Add(child);
        root.Children.Add(container);

        Layout(root, 100, 100);

        Assert.Equal(30, container.Geometry.Height);
        Assert.Equal(100, root.Geometry.Height);
    }

    [Fact]
    public void FlexRowInHtmlBlockFlowUsesLaidOutCrossAxisHeight()
    {
        var article = new HTMLArticleElement();
        var row = new HTMLDivElement();
        row.Style.Set("display", "flex");
        row.Style.Set("flex-direction", "row");
        row.Style.Set("gap", "12px");
        for (var i = 0; i < 2; i++)
        {
            var panel = new HTMLDivElement();
            panel.Style.Set("min-height", "72px");
            panel.Style.Set("min-width", "0");
            panel.Style.Set("flex-grow", "1");
            panel.Style.Set("flex-basis", "0");
            panel.Style.Set("box-sizing", "border-box");
            panel.Style.Set("padding", "10px");
            panel.Style.Set("border", "2px solid #2563eb");
            panel.ChildNodes.Add(new Square.UI.Text("Panel"));
            row.Children.Add(panel);
        }
        var following = new HTMLDivElement();
        following.Style.Set("height", "10px");
        article.Children.Add(row);
        article.Children.Add(following);

        new LayoutEngine().MeasureAndArrange(article, new Size(830, 200));

        Assert.Equal(72, row.Geometry.Height);
        Assert.Equal(row.Geometry.Bottom, following.Geometry.Y);
    }

    [Fact]
    public void RetainedExternalFlexRowWrapTracksWidthChangesAcrossFrames()
    {
        var article = new HTMLArticleElement();
        var row = new HTMLDivElement();
        row.Style.Set("display", "flex");
        row.Style.Set("flex-wrap", "wrap");
        row.Style.Set("width", "300px");
        var leaves = new List<HTMLDivElement>();
        foreach (var width in new[] { "120px", "120px", "120px" })
        {
            var leaf = new HTMLDivElement();
            leaf.Style.Set("width", width);
            leaf.Style.Set("height", "40px");
            row.Children.Add(leaf);
            leaves.Add(leaf);
        }
        var following = new HTMLDivElement();
        following.Style.Set("height", "10px");
        article.Children.Add(row);
        article.Children.Add(following);

        var retained = new LayoutEngine();
        retained.EnableRetainedFlexLayout();

        retained.MeasureAndArrange(article, new Rect(0, 0, 830, 400));
        var frame = CaptureExternalFixture(article, row, following, leaves);
        Assert.Equal(new Rect(0, 0, 120, 40), frame.LeafA);
        Assert.Equal(new Rect(0, 40, 120, 40), frame.LeafC);
        Assert.Equal(80, frame.Row.Height);
        Assert.Equal(80, frame.Following.Y);
        AssertExternalFixtureMatchesTransient(article, row, following, leaves, frame);

        // 跨折返点：自然测高与绝对排列在同一缓存会话内更新，行高与后续 section Y 必须一致。
        leaves[0].Style.Set("width", "160px");
        retained.MeasureAndArrange(article, new Rect(0, 0, 830, 400));
        frame = CaptureExternalFixture(article, row, following, leaves);
        Assert.Equal(new Rect(0, 0, 160, 40), frame.LeafA);
        Assert.Equal(new Rect(160, 0, 120, 40), frame.LeafB);
        Assert.Equal(new Rect(0, 40, 120, 40), frame.LeafC);
        Assert.Equal(80, frame.Following.Y);
        AssertExternalFixtureMatchesTransient(article, row, following, leaves, frame);

        // 三项同排：外部根的自然高度必须塌缩，后续 section 随之上移。
        leaves[0].Style.Set("width", "60px");
        retained.MeasureAndArrange(article, new Rect(0, 0, 830, 400));
        frame = CaptureExternalFixture(article, row, following, leaves);
        Assert.Equal(new Rect(180, 0, 120, 40), frame.LeafC);
        Assert.Equal(40, frame.Row.Height);
        Assert.Equal(40, frame.Following.Y);
        AssertExternalFixtureMatchesTransient(article, row, following, leaves, frame);

        // 结构插入/删除后回到两行布局。
        var extra = new HTMLDivElement();
        extra.Style.Set("width", "100px");
        extra.Style.Set("height", "40px");
        row.Children.Add(extra);
        retained.MeasureAndArrange(article, new Rect(0, 0, 830, 400));
        frame = CaptureExternalFixture(article, row, following, leaves);
        Assert.Equal(80, frame.Row.Height);
        Assert.Equal(80, frame.Following.Y);
        Assert.Equal(new Rect(0, 40, 100, 40), extra.Geometry);
        AssertExternalFixtureMatchesTransient(article, row, following, leaves, frame);

        row.Children.Remove(extra);
        leaves[0].Style.Set("width", "120px");
        retained.MeasureAndArrange(article, new Rect(0, 0, 830, 400));
        frame = CaptureExternalFixture(article, row, following, leaves);
        Assert.Equal(new Rect(0, 40, 120, 40), frame.LeafC);
        Assert.Equal(80, frame.Row.Height);
        Assert.Equal(80, frame.Following.Y);
        AssertExternalFixtureMatchesTransient(article, row, following, leaves, frame);
    }

    [Fact]
    public void RetainedFlexWidthReflowPreservesAuthoredHeightAndFollowingBlock()
    {
        var article = new HTMLArticleElement();
        article.Style.Set("display", "block");
        var row = new HTMLDivElement();
        row.Style.CssText = "display:flex;flex-wrap:wrap;height:100px";
        for (var i = 0; i < 4; i++)
        {
            var child = new HTMLDivElement();
            child.Style.CssText = "flex:none;width:30px;height:20px";
            row.Children.Add(child);
        }
        var following = new HTMLDivElement();
        following.Style.Set("display", "block");
        article.Children.Add(row);
        article.Children.Add(following);
        var layout = new LayoutEngine();
        layout.EnableRetainedFlexLayout();
        layout.MeasureAndArrange(article, new Size(100, 200));
        foreach (var child in row.Children) child.Style.Set("width", "60px");
        layout.MeasureAndArrange(article, new Size(100, 200));
        Assert.Equal(100, row.Geometry.Height);
        Assert.Equal(100, following.Geometry.Y);
        Assert.Equal(60, row.Children[3].Geometry.Y);
        layout.ClearCachedYoga();
    }

    [Fact]
    public void RetainedExternalFlexRowResolvesChildPercentageHeightAtArrange()
    {
        // 自然测高（auto 行高 + min-height）与排列（精确行高）的百分比子项约束不等价：
        // 行内容高 0、min-height 100 → 排列期 100；子项 height:50% 只能在排列精确高度下
        // 解析为 50，不得复用自然测高结果。
        var article = new HTMLArticleElement();
        var row = new HTMLDivElement();
        row.Style.Set("display", "flex");
        row.Style.Set("min-height", "100px");
        row.Style.Set("align-items", "flex-start");
        var panel = new HTMLDivElement();
        panel.Style.Set("width", "40px");
        panel.Style.Set("height", "50%");
        row.Children.Add(panel);
        var following = new HTMLDivElement();
        following.Style.Set("height", "10px");
        article.Children.Add(row);
        article.Children.Add(following);

        var retained = new LayoutEngine();
        retained.EnableRetainedFlexLayout();
        retained.MeasureAndArrange(article, new Rect(0, 0, 400, 300));

        Assert.Equal(100, row.Geometry.Height, 3);
        Assert.Equal(50, panel.Geometry.Height, 3);
        Assert.Equal(row.Geometry.Bottom, following.Geometry.Y, 3);

        // 未变化的第二帧：整帧跳过仍必须保留排列期的百分比解析结果。
        retained.MeasureAndArrange(article, new Rect(0, 0, 400, 300));
        Assert.Equal(50, panel.Geometry.Height, 3);

        // 宽度点数变化走原位更新；百分比高度语义不受影响。
        panel.Style.Set("width", "80px");
        retained.MeasureAndArrange(article, new Rect(0, 0, 400, 300));
        Assert.Equal(80, panel.Geometry.Width, 3);
        Assert.Equal(50, panel.Geometry.Height, 3);

        new LayoutEngine().MeasureAndArrange(article, new Rect(0, 0, 400, 300));
        Assert.Equal(80, panel.Geometry.Width, 3);
        Assert.Equal(50, panel.Geometry.Height, 3);
        Assert.Equal(100, row.Geometry.Height, 3);
    }

    [Fact]
    public void RetainedExternalFlexRowMatchesTransientCrossAxisHeightWithTextPanels()
    {
        var retainedArticle = CreateTextPanelArticle();
        var plainArticle = CreateTextPanelArticle();
        var retained = new LayoutEngine();
        retained.EnableRetainedFlexLayout();

        retained.MeasureAndArrange(retainedArticle, new Rect(0, 0, 830, 200));
        var retainedRow = retainedArticle.Children[0];
        var retainedFollowing = retainedArticle.Children[1];
        Assert.Equal(72, retainedRow.Geometry.Height, 3);
        Assert.Equal(retainedRow.Geometry.Bottom, retainedFollowing.Geometry.Y, 3);

        // 带 DOM 文本的 flex 面板不合格于原位宽度更新：样式变化必须整棵重建并保持结果一致。
        retainedRow.Children[1].Style.Set("flex-grow", "3");
        retained.MeasureAndArrange(retainedArticle, new Rect(0, 0, 830, 200));
        Assert.True(retainedRow.Children[1].Geometry.Width > retainedRow.Children[0].Geometry.Width,
            "expected the grown panel to take more width after rebuild");

        var plainRow = plainArticle.Children[0];
        plainRow.Children[1].Style.Set("flex-grow", "3");
        new LayoutEngine().MeasureAndArrange(plainArticle, new Rect(0, 0, 830, 200));

        Assert.Equal(plainRow.Geometry, retainedRow.Geometry);
        Assert.Equal(plainArticle.Children[1].Geometry, retainedFollowing.Geometry);
        Assert.Equal(plainRow.Children[1].Geometry, retainedRow.Children[1].Geometry);
    }

    private static HTMLArticleElement CreateTextPanelArticle()
    {
        var article = new HTMLArticleElement();
        var row = new HTMLDivElement();
        row.Style.Set("display", "flex");
        row.Style.Set("flex-direction", "row");
        row.Style.Set("gap", "12px");
        for (var i = 0; i < 2; i++)
        {
            var panel = new HTMLDivElement();
            panel.Style.Set("min-height", "72px");
            panel.Style.Set("min-width", "0");
            panel.Style.Set("flex-grow", "1");
            panel.Style.Set("flex-basis", "0");
            panel.Style.Set("box-sizing", "border-box");
            panel.Style.Set("padding", "10px");
            panel.Style.Set("border", "2px solid #2563eb");
            panel.ChildNodes.Add(new Square.UI.Text("Panel"));
            row.Children.Add(panel);
        }
        var following = new HTMLDivElement();
        following.Style.Set("height", "10px");
        article.Children.Add(row);
        article.Children.Add(following);
        return article;
    }

    private sealed record ExternalFlexCapture(
        Rect Row, Rect Following, Size ArticleExtents, Rect LeafA, Rect LeafB, Rect LeafC);

    private static ExternalFlexCapture CaptureExternalFixture(
        Element article, Element row, Element following, IReadOnlyList<Element> leaves) => new(
        row.Geometry,
        following.Geometry,
        article.ScrollContentSize,
        leaves[0].Geometry,
        leaves[1].Geometry,
        leaves[2].Geometry);

    private static void AssertExternalFixtureMatchesTransient(
        Element article, Element row, Element following, IReadOnlyList<Element> leaves,
        ExternalFlexCapture retained)
    {
        new LayoutEngine().MeasureAndArrange(article, new Rect(0, 0, 830, 400));
        var transient = CaptureExternalFixture(article, row, following, leaves);
        Assert.Equal(retained.Row, transient.Row);
        Assert.Equal(retained.Following, transient.Following);
        Assert.Equal(retained.ArticleExtents, transient.ArticleExtents);
        Assert.Equal(retained.LeafA, transient.LeafA);
        Assert.Equal(retained.LeafB, transient.LeafB);
        Assert.Equal(retained.LeafC, transient.LeafC);
    }


    [Fact]
    public void RetainedExternalFlexEvictsReplacedRootAndLaysOutNewGrid()
    {
        var article = new HTMLArticleElement();
        var row1 = CreateWrapRow("120px", "120px", "120px");
        var following = new HTMLDivElement();
        following.Style.Set("height", "10px");
        article.Children.Add(row1);
        article.Children.Add(following);

        var retained = new LayoutEngine();
        retained.EnableRetainedFlexLayout();
        retained.MeasureAndArrange(article, new Rect(0, 0, 830, 400));
        Assert.Equal(80, following.Geometry.Y);

        // 换根：旧外部网格移除（缓存按文档根连通性驱逐），新网格接管布局。
        article.Children.Remove(row1);
        var row2 = CreateWrapRow("160px", "120px", "120px");
        article.Children.Add(row2);
        retained.MeasureAndArrange(article, new Rect(0, 0, 830, 400));
        Assert.Equal(0, following.Geometry.Y);
        Assert.Equal(10, row2.Geometry.Y);
        Assert.Equal(80, row2.Geometry.Height);

        var plainArticle = new HTMLArticleElement();
        var plainFollowing = new HTMLDivElement();
        plainFollowing.Style.Set("height", "10px");
        var plainRow = CreateWrapRow("160px", "120px", "120px");
        plainArticle.Children.Add(plainFollowing);
        plainArticle.Children.Add(plainRow);
        new LayoutEngine().MeasureAndArrange(plainArticle, new Rect(0, 0, 830, 400));

        Assert.Equal(plainRow.Geometry, row2.Geometry);
        Assert.Equal(plainFollowing.Geometry, following.Geometry);
    }

    [Fact]
    public void RetainedFlexSessionRootStaysConstraintSizedAcrossInPlaceEligibleWidthChanges()
    {
        // 会话根自身的 Yoga 宽度由约束宽度接管：根的 px→px 宽度声明变化即便满足
        // 原位更新形态，也必须整棵重建，使节点宽度继续跟随 ArrangeRect 约束而非声明值。
        var retainedRoot = new HTMLDivElement();
        retainedRoot.Style.Set("display", "flex");
        retainedRoot.Style.Set("width", "300px");
        var retained = new LayoutEngine();
        retained.EnableRetainedFlexLayout();

        retained.MeasureAndArrange(retainedRoot, new Rect(0, 0, 900, 600));
        Assert.Equal(900, retainedRoot.Geometry.Width, 3);

        retainedRoot.Style.Set("width", "200px");
        retained.MeasureAndArrange(retainedRoot, new Rect(0, 0, 900, 600));
        Assert.Equal(900, retainedRoot.Geometry.Width, 3);
        Assert.Equal(0, retainedRoot.Geometry.X, 3);

        var plainRoot = new HTMLDivElement();
        plainRoot.Style.Set("display", "flex");
        plainRoot.Style.Set("width", "200px");
        new LayoutEngine().MeasureAndArrange(plainRoot, new Rect(0, 0, 900, 600));
        Assert.Equal(plainRoot.Geometry, retainedRoot.Geometry);
        retained.ClearCachedYoga();
    }

    private static HTMLDivElement CreateWrapRow(params string[] widths)
    {
        var row = new HTMLDivElement();
        row.Style.Set("display", "flex");
        row.Style.Set("flex-wrap", "wrap");
        row.Style.Set("width", "300px");
        foreach (var width in widths)
        {
            var leaf = new HTMLDivElement();
            leaf.Style.Set("width", width);
            leaf.Style.Set("height", "40px");
            row.Children.Add(leaf);
        }
        return row;
    }

    [Fact]
    public void UnspecifiedTreeKeepsLegacyYogaBehavior()
    {
        var root = new View();
        var child = new CssMeasuredBox(25, 12);
        root.Children.Add(child);

        Layout(root, 100, 50);

        Assert.Equal(100, child.Geometry.Width);
        Assert.Equal(12, child.Geometry.Height);
    }

    private static View Block(float height)
    {
        var result = new View();
        result.Style.Set("display", "block");
        result.Style.Set("height", $"{height}px");
        return result;
    }

    internal static void Layout(View root, float width, float height)
    {
        var engine = new LayoutEngine();
        engine.Measure(root, new Size(width, height));
        engine.Arrange(root, new Rect(0, 0, width, height));
    }

    private static LayoutEngine LayoutAndReturn(View root, float width, float height)
    {
        var engine = new LayoutEngine();
        engine.MeasureAndArrange(root, new Size(width, height));
        return engine;
    }
}

internal sealed class CssMeasuredBox : UIElement
{
    private readonly Size _size;

    public CssMeasuredBox(float width, float height) => _size = new Size(width, height);

    public override Size Measure(Size availableSize) => _size;
}
