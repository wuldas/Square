using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Xunit;

namespace Square.UI.Tests;

public class TableLayoutTests
{
    [Fact]
    public void FixedLayoutArrangesRowsCellsAndBorderSpacing()
    {
        var table = new Table();
        table.Style.Set("width", "300px");
        table.Style.Set("table-layout", "fixed");
        table.Style.Set("border-spacing", "10px 5px");
        var body = new TableRowGroup();
        var firstRow = new TableRow();
        var first = Cell(20, 20);
        first.Style.Set("width", "100px");
        var second = Cell(20, 20);
        firstRow.Children.Add(first);
        firstRow.Children.Add(second);
        var secondRow = new TableRow();
        var third = Cell(20, 30);
        var fourth = Cell(20, 30);
        secondRow.Children.Add(third);
        secondRow.Children.Add(fourth);
        body.Children.Add(firstRow);
        body.Children.Add(secondRow);
        table.Children.Add(body);

        Layout(table, new Size(300, 200), new Rect(0, 0, 300, 200));

        Assert.Equal(new Rect(10, 5, 100, 20), first.Geometry);
        Assert.Equal(new Rect(120, 5, 170, 20), second.Geometry);
        Assert.Equal(new Rect(10, 30, 100, 30), third.Geometry);
        Assert.Equal(new Rect(120, 30, 170, 30), fourth.Geometry);
        Assert.Equal(new Rect(0, 5, 300, 20), firstRow.Geometry);
        Assert.Equal(new Rect(0, 5, 300, 55), body.Geometry);
    }

    [Fact]
    public void AutoLayoutUsesIntrinsicWidthsAndColSpan()
    {
        var table = new Table();
        var firstRow = new TableRow();
        var wide = Cell(120, 20);
        var narrow = Cell(40, 20);
        firstRow.Children.Add(wide);
        firstRow.Children.Add(narrow);
        var secondRow = new TableRow();
        var spanning = Cell(220, 25);
        spanning.ColSpan = 2;
        secondRow.Children.Add(spanning);
        table.Children.Add(firstRow);
        table.Children.Add(secondRow);

        Layout(table, new Size(float.PositiveInfinity, 100), new Rect(0, 0, 220, 45));

        Assert.Equal(new Rect(0, 0, 150, 20), wide.Geometry);
        Assert.Equal(new Rect(150, 0, 70, 20), narrow.Geometry);
        Assert.Equal(new Rect(0, 20, 220, 25), spanning.Geometry);
    }

    [Fact]
    public void RowSpanOccupiesFollowingRows()
    {
        var table = new Table();
        table.Style.Set("width", "200px");
        table.Style.Set("table-layout", "fixed");
        var firstRow = new TableRow();
        var spanning = Cell(20, 80);
        spanning.RowSpan = 2;
        var topRight = Cell(20, 30);
        firstRow.Children.Add(spanning);
        firstRow.Children.Add(topRight);
        var secondRow = new TableRow();
        var bottomRight = Cell(20, 30);
        secondRow.Children.Add(bottomRight);
        table.Children.Add(firstRow);
        table.Children.Add(secondRow);

        Layout(table, new Size(200, 100), new Rect(0, 0, 200, 80));

        Assert.Equal(new Rect(0, 0, 100, 80), spanning.Geometry);
        Assert.Equal(new Rect(100, 0, 100, 40), topRight.Geometry);
        Assert.Equal(new Rect(100, 40, 100, 40), bottomRight.Geometry);
    }

    [Fact]
    public void DirectCellsFormOneAnonymousRow()
    {
        var table = new Table();
        table.Style.Set("width", "200px");
        table.Style.Set("table-layout", "fixed");
        var first = Cell(10, 20);
        var second = Cell(10, 30);
        table.Children.Add(first);
        table.Children.Add(second);

        Layout(table, new Size(200, 100), new Rect(0, 0, 200, 30));

        Assert.Equal(new Rect(0, 0, 100, 30), first.Geometry);
        Assert.Equal(new Rect(100, 0, 100, 30), second.Geometry);
    }

    [Fact]
    public void CssDisplayRolesNormalizeOrdinaryViews()
    {
        var table = new View();
        table.Style.Set("display", "inline-table");
        table.Style.Set("width", "180px");
        table.Style.Set("table-layout", "fixed");
        var group = new View();
        group.Style.Set("display", "table-header-group");
        var row = new View();
        row.Style.Set("display", "table-row");
        var first = CellView(20, 18);
        var second = CellView(20, 18);
        row.Children.Add(first);
        row.Children.Add(second);
        group.Children.Add(row);
        table.Children.Add(group);

        Layout(table, new Size(400, 100), new Rect(20, 10, 180, 18));

        Assert.Equal(new Rect(20, 10, 90, 18), first.Geometry);
        Assert.Equal(new Rect(110, 10, 90, 18), second.Geometry);
        Assert.Equal(new Rect(20, 10, 180, 18), group.Geometry);
    }

    [Fact]
    public void CaptionsAndVerticalAlignArrangeCellContents()
    {
        var table = new Table();
        table.Style.Set("width", "200px");
        table.Style.Set("table-layout", "fixed");
        var topCaption = new TableCaption();
        topCaption.Children.Add(new MeasuredBox(100, 15));
        var bottomCaption = new TableCaption();
        bottomCaption.Style.Set("caption-side", "bottom");
        bottomCaption.Children.Add(new MeasuredBox(100, 10));
        var row = new TableRow();
        var top = new TableCell();
        top.Style.Set("height", "60px");
        var topContent = new MeasuredBox(20, 20);
        top.Children.Add(topContent);
        var middle = new TableCell();
        middle.Style.Set("height", "60px");
        middle.Style.Set("vertical-align", "middle");
        var middleContent = new MeasuredBox(20, 20);
        middle.Children.Add(middleContent);
        var bottom = new TableCell();
        bottom.Style.Set("height", "60px");
        bottom.Style.Set("vertical-align", "bottom");
        var bottomContent = new MeasuredBox(20, 20);
        bottom.Children.Add(bottomContent);
        row.Children.Add(top);
        row.Children.Add(middle);
        row.Children.Add(bottom);
        table.Children.Add(topCaption);
        table.Children.Add(row);
        table.Children.Add(bottomCaption);

        Layout(table, new Size(200, 100), new Rect(0, 0, 200, 85));

        Assert.Equal(new Rect(0, 0, 200, 15), topCaption.Geometry);
        Assert.Equal(15, topContent.Geometry.Top);
        Assert.Equal(35, middleContent.Geometry.Top);
        Assert.Equal(55, bottomContent.Geometry.Top);
        Assert.Equal(new Rect(0, 75, 200, 10), bottomCaption.Geometry);
    }

    [Fact]
    public void TableInternalElementsIgnoreMarginsButCaptionKeepsItsOwnBox()
    {
        var table = new Table();
        table.Style.Set("width", "100px");
        table.Style.Set("table-layout", "fixed");
        var caption = new TableCaption();
        caption.Style.Set("margin", "7px");
        caption.Children.Add(new MeasuredBox(10, 10));
        var row = new TableRow();
        row.Style.Set("margin", "20px");
        var cell = new TableCell();
        cell.Style.Set("margin", "15px");
        cell.Children.Add(new MeasuredBox(10, 10));
        row.Children.Add(cell);
        table.Children.Add(caption);
        table.Children.Add(row);

        Layout(table, new Size(100, 100), new Rect(0, 0, 100, 30));

        Assert.Equal(new Rect(7, 7, 86, 10), caption.Geometry);
        Assert.Equal(new Rect(0, 24, 100, 10), row.Geometry);
        Assert.Equal(new Rect(0, 24, 100, 10), cell.Geometry);
    }

    [Theory]
    [InlineData("visible", false)]
    [InlineData("hidden", true)]
    [InlineData("clip", true)]
    [InlineData("scroll", false)]
    [InlineData("auto", false)]
    public void TableOverflowClipsOnlyForHiddenOrClip(string overflow, bool shouldClip)
    {
        var table = new Table();
        table.Style.Set("width", "100px");
        table.Style.Set("height", "20px");
        table.Style.Set("overflow", overflow);

        Layout(table, new Size(100, 20), new Rect(0, 0, 100, 20));

        Assert.Equal(shouldClip, table.ClipsOverflow());
        Assert.Equal(shouldClip ? table.Geometry : Rect.Empty, table.GetOverflowClipRect());
        Assert.False(table.IsScrollContainer());
    }

    [Theory]
    [InlineData("flex")]
    [InlineData("grid")]
    public void ExistingContainerLayoutsHostTableRoots(string display)
    {
        var root = new View();
        root.Style.Set("display", display);
        if (display == "grid")
        {
            root.Style.Set("grid-template-columns", "120px");
            root.Style.Set("grid-template-rows", "40px");
        }

        var table = new Table();
        table.Style.Set("width", "120px");
        table.Style.Set("table-layout", "fixed");
        var row = new TableRow();
        var first = Cell(10, 20);
        var second = Cell(10, 20);
        row.Children.Add(first);
        row.Children.Add(second);
        table.Children.Add(row);
        root.Children.Add(table);

        Layout(root, new Size(200, 40), new Rect(0, 0, 200, 40));

        Assert.Equal(60, first.Geometry.Width);
        Assert.Equal(60, second.Geometry.Width);
        Assert.Equal(first.Geometry.Right, second.Geometry.Left);
    }

    [Fact]
    public void CollapsedBordersIgnoreSpacingAndUseHalfSharedInsets()
    {
        var table = new Table();
        table.Style.Set("width", "100px");
        table.Style.Set("table-layout", "fixed");
        table.Style.Set("border-collapse", "collapse");
        table.Style.Set("border-spacing", "20px");
        var row = new TableRow();
        var first = new TableCell();
        first.Style.Set("height", "20px");
        first.Style.Set("border-right", "4px solid red");
        var firstContent = new MeasuredBox(10, 10);
        first.Children.Add(firstContent);
        var second = new TableCell();
        second.Style.Set("height", "20px");
        second.Style.Set("border-left", "8px solid blue");
        var secondContent = new MeasuredBox(10, 10);
        second.Children.Add(secondContent);
        row.Children.Add(first);
        row.Children.Add(second);
        table.Children.Add(row);

        Layout(table, new Size(100, 20), new Rect(0, 0, 100, 20));

        Assert.Equal(new Rect(0, 0, 50, 20), first.Geometry);
        Assert.Equal(new Rect(50, 0, 50, 20), second.Geometry);
        Assert.Equal(46, firstContent.Geometry.Width);
        Assert.Equal(54, secondContent.Geometry.Left);
        Assert.Equal(46, secondContent.Geometry.Width);
    }

    private static TableCell Cell(float width, float height)
    {
        var cell = new TableCell();
        cell.Children.Add(new MeasuredBox(width, height));
        return cell;
    }

    private static View CellView(float width, float height)
    {
        var cell = new View();
        cell.Style.Set("display", "table-cell");
        cell.Children.Add(new MeasuredBox(width, height));
        return cell;
    }

    private static void Layout(Square.UI.Element table, Size available, Rect rect)
    {
        var layout = new LayoutEngine();
        layout.Measure(table, available);
        layout.Arrange(table, rect);
    }
}
