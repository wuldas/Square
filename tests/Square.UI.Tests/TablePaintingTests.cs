using Square.Backends;
using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Xunit;

namespace Square.UI.Tests;

public class TablePaintingTests
{
    [Fact]
    public void CollapsedSharedBorderUsesWiderCellBorderWithoutDoubleThickness()
    {
        var table = TwoCellTable();
        table.Style.Set("border-collapse", "collapse");
        table.Style.Set("border-spacing", "12px");
        var first = (TableCell)table.Children[0].Children[0];
        var second = (TableCell)table.Children[0].Children[1];
        first.Style.Set("background-color", "green");
        first.Style.Set("border-right", "4px solid red");
        second.Style.Set("background-color", "white");
        second.Style.Set("border-left", "8px solid blue");

        using var rendered = Render(table, 40, 20);
        var bitmap = rendered.Bitmap;

        AssertPixel(bitmap, 15, 10, Color.Green);
        for (var x = 16; x < 24; x++) AssertPixel(bitmap, x, 10, Color.Blue);
        AssertPixel(bitmap, 24, 10, Color.White);
    }

    [Fact]
    public void EqualCollapsedBordersUseLaterCellSourceOrder()
    {
        var table = TwoCellTable();
        table.Style.Set("border-collapse", "collapse");
        var first = (TableCell)table.Children[0].Children[0];
        var second = (TableCell)table.Children[0].Children[1];
        first.Style.Set("border-right", "4px solid red");
        second.Style.Set("border-left", "4px solid blue");

        using var rendered = Render(table, 40, 20);
        var bitmap = rendered.Bitmap;

        for (var x = 18; x < 22; x++) AssertPixel(bitmap, x, 10, Color.Blue);
    }

    [Fact]
    public void EmptyCellsHideSuppressesOnlyTheEmptyCellCssBox()
    {
        var table = TwoCellTable();
        table.Style.Set("empty-cells", "hide");
        var hidden = (TableCell)table.Children[0].Children[0];
        var shown = (TableCell)table.Children[0].Children[1];
        hidden.Style.Set("background-color", "red");
        hidden.Style.Set("border", "2px solid blue");
        shown.Style.Set("empty-cells", "show");
        shown.Style.Set("background-color", "red");
        shown.Style.Set("border", "2px solid blue");

        using var rendered = Render(table, 40, 20);
        var bitmap = rendered.Bitmap;

        AssertPixel(bitmap, 0, 10, Color.Transparent);
        AssertPixel(bitmap, 10, 10, Color.Transparent);
        AssertPixel(bitmap, 20, 10, Color.Blue);
        AssertPixel(bitmap, 30, 10, Color.Red);
    }

    [Fact]
    public void EmptyCellsHideAlsoSuppressesCssRoleViewPainting()
    {
        var table = new Table();
        table.Style.Set("width", "20px");
        table.Style.Set("empty-cells", "hide");
        var row = new TableRow();
        var cell = new View();
        cell.Style.Set("display", "table-cell");
        cell.Style.Set("height", "20px");
        cell.Style.Set("background-color", "red");
        row.Children.Add(cell);
        table.Children.Add(row);

        using var rendered = Render(table, 20, 20);

        AssertPixel(rendered.Bitmap, 10, 10, Color.Transparent);
    }

    private static Table TwoCellTable()
    {
        var table = new Table();
        table.Style.Set("width", "40px");
        table.Style.Set("table-layout", "fixed");
        var row = new TableRow();
        var first = new TableCell();
        first.Style.Set("height", "20px");
        var second = new TableCell();
        second.Style.Set("height", "20px");
        row.Children.Add(first);
        row.Children.Add(second);
        table.Children.Add(row);
        return table;
    }

    private static RenderedTable Render(Table table, int width, int height)
    {
        var layout = new LayoutEngine();
        layout.Measure(table, new Size(width, height));
        layout.Arrange(table, new Rect(0, 0, width, height));
        var tree = new DisplayTree();
        tree.BuildFrom(table);
        var bitmap = new Bitmap(width, height);
        var context = new RenderContext(bitmap, 1f);
        context.Clear(Color.Transparent);
        tree.Render(context);
        return new RenderedTable(bitmap, context);
    }

    private static void AssertPixel(Bitmap bitmap, int x, int y, Color color)
    {
        var pixel = bitmap.GetPixel(x, y);
        Assert.Equal(color.B, pixel[0]);
        Assert.Equal(color.G, pixel[1]);
        Assert.Equal(color.R, pixel[2]);
        Assert.Equal(color.A, pixel[3]);
    }

    private sealed class RenderedTable(Bitmap bitmap, RenderContext context) : IDisposable
    {
        public Bitmap Bitmap { get; } = bitmap;

        public void Dispose() => context.Dispose();
    }
}
