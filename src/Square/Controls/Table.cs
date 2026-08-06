using System.Runtime.CompilerServices;
using Square.Graphics;
using Square.UI;

namespace Square.Controls;

/// <summary>A CSS table formatting root.</summary>
public class Table : View
{
    /// <summary>Creates a block-level table.</summary>
    public Table() => Style.SetCascaded("display", "table", int.MinValue);
}

/// <summary>An inline-level CSS table formatting root.</summary>
public class InlineTable : Table
{
    /// <summary>Creates an inline table.</summary>
    public InlineTable() => Style.SetCascaded("display", "inline-table", int.MinValue);
}

/// <summary>A table body row group.</summary>
public class TableRowGroup : View
{
    /// <summary>Creates a table row group.</summary>
    public TableRowGroup() => Style.SetCascaded("display", "table-row-group", int.MinValue);
}

/// <summary>A table header row group.</summary>
public class TableHeaderGroup : View
{
    /// <summary>Creates a table header group.</summary>
    public TableHeaderGroup() => Style.SetCascaded("display", "table-header-group", int.MinValue);
}

/// <summary>A table footer row group.</summary>
public class TableFooterGroup : View
{
    /// <summary>Creates a table footer group.</summary>
    public TableFooterGroup() => Style.SetCascaded("display", "table-footer-group", int.MinValue);
}

/// <summary>A table row.</summary>
public class TableRow : View
{
    /// <summary>Creates a table row.</summary>
    public TableRow() => Style.SetCascaded("display", "table-row", int.MinValue);
}

/// <summary>A table cell with typed column and row spans.</summary>
public class TableCell : View
{
    /// <summary>Number of columns occupied by this cell.</summary>
    public int ColSpan
    {
        get => Properties.HasValue(nameof(ColSpan)) ? Math.Max(1, GetProperty<int>(nameof(ColSpan))) : 1;
        set => SetProperty(nameof(ColSpan), Math.Max(1, value));
    }

    /// <summary>Number of rows occupied by this cell.</summary>
    public int RowSpan
    {
        get => Properties.HasValue(nameof(RowSpan)) ? Math.Max(1, GetProperty<int>(nameof(RowSpan))) : 1;
        set => SetProperty(nameof(RowSpan), Math.Max(1, value));
    }

    /// <summary>Creates a table cell.</summary>
    public TableCell() => Style.SetCascaded("display", "table-cell", int.MinValue);

}

/// <summary>A table caption, placed above or below the table grid using <c>caption-side</c>.</summary>
public class TableCaption : View
{
    /// <summary>Creates a table caption.</summary>
    public TableCaption() => Style.SetCascaded("display", "table-caption", int.MinValue);
}

internal readonly record struct TableBorderFragment(Rect Bounds, Color Color);

internal sealed class TablePaintMetadata
{
    public Element? Table { get; set; }
    public bool SuppressCssBox { get; set; }
    public bool UseCollapsedBorderFragments { get; set; }
    public List<TableBorderFragment> CollapsedBorderFragments { get; } = [];

    public void Reset(Element table)
    {
        Table = table;
        SuppressCssBox = false;
        UseCollapsedBorderFragments = false;
        CollapsedBorderFragments.Clear();
    }

    public void Clear()
    {
        Table = null;
        SuppressCssBox = false;
        UseCollapsedBorderFragments = false;
        CollapsedBorderFragments.Clear();
    }
}

internal static class TablePaintMetadataStore
{
    private static readonly ConditionalWeakTable<Element, TablePaintMetadata> Data = new();

    public static TablePaintMetadata Reset(Element element, Element table)
    {
        var metadata = Data.GetOrCreateValue(element);
        metadata.Reset(table);
        return metadata;
    }

    public static TablePaintMetadata Get(Element element) => Data.GetOrCreateValue(element);

    public static void ClearForTable(Element table)
    {
        foreach (var child in table.Children) ClearForTable(table, child);
    }

    public static bool TryGetActive(Element element, out TablePaintMetadata metadata)
    {
        if (!Data.TryGetValue(element, out metadata!) || metadata.Table == null) return false;
        for (Element? current = element.Parent; current != null; current = current.Parent)
            if (ReferenceEquals(current, metadata.Table)) return true;
        return false;
    }

    private static void ClearForTable(Element table, Element element)
    {
        if (Data.TryGetValue(element, out var metadata) && ReferenceEquals(metadata.Table, table))
        {
            metadata.Clear();
            element.InvalidatePaint();
        }
        foreach (var child in element.Children) ClearForTable(table, child);
    }
}
