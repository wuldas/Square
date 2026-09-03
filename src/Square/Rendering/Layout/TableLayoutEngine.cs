using System.Globalization;
using Square.Controls;
using Square.Graphics;
using Square.Rendering.Paint;
using Square.UI;

namespace Square.Rendering;

internal sealed class TableLayoutEngine
{
    private readonly LayoutEngine _layout;

    public TableLayoutEngine(LayoutEngine layout) => _layout = layout;

    public Size Measure(Element table, Size availableSize, Size scrollbarGutter = default)
    {
        var model = BuildModel(table);
        return Compute(table, model, DeflateScrollbarGutter(availableSize, scrollbarGutter), arrange: false, default,
            scrollbarGutter);
    }

    public void Arrange(Element table, Rect finalRect, Size scrollbarGutter = default)
    {
        var model = BuildModel(table);
        var insets = table.GetReservedScrollbarInsets();
        Compute(table, model, DeflateScrollbarGutter(finalRect.Size, scrollbarGutter), arrange: true, finalRect,
            scrollbarGutter,
            insets.Left, insets.Top);
    }

    private static Size DeflateScrollbarGutter(Size size, Size gutter) => new(
        Deflate(size.Width, gutter.Width),
        Deflate(size.Height, gutter.Height));

    private static float Deflate(float value, float amount) =>
        float.IsFinite(value) ? Math.Max(0, value - Math.Max(0, amount)) : value;

    private static float InflateScrollbarGutter(float value, float amount) =>
        float.IsFinite(value) ? value + Math.Max(0, amount) : value;

    private Size Compute(
        Element table,
        TableModel model,
        Size availableSize,
        bool arrange,
        Rect finalRect,
        Size scrollbarGutter,
        float leadingLeft = 0,
        float leadingTop = 0)
    {
        PrepareLayoutElement(table, availableSize);

        var padding = ResolveBox(table, "padding", availableSize.Width, availableSize.Height);
        var border = ResolveBorder(table, availableSize.Width, availableSize.Height);
        var horizontalInsets = padding.Left + padding.Right + border.Left + border.Right;
        var verticalInsets = padding.Top + padding.Bottom + border.Top + border.Bottom;
        var explicitReferenceSize = new Size(
            InflateScrollbarGutter(availableSize.Width, scrollbarGutter.Width),
            InflateScrollbarGutter(availableSize.Height, scrollbarGutter.Height));
        var explicitWidth = ResolveLength(table, table.Style.Get("width"),
            explicitReferenceSize.Width, explicitReferenceSize.Height);
        var explicitHeight = ResolveLength(table, table.Style.Get("height"),
            explicitReferenceSize.Width, explicitReferenceSize.Height);
        var constrainedWidth = IsFinite(availableSize.Width)
            ? Math.Max(0, availableSize.Width - horizontalInsets)
            : float.PositiveInfinity;
        var specifiedGridWidth = IsFinite(explicitWidth)
            ? Math.Max(0, explicitWidth - horizontalInsets - scrollbarGutter.Width)
            : float.NaN;
        var captionConstraint = IsFinite(specifiedGridWidth) ? specifiedGridWidth : constrainedWidth;

        var captionSizes = new Dictionary<Element, Size>();
        var topCaptionHeight = 0f;
        var bottomCaptionHeight = 0f;
        var captionWidth = 0f;
        foreach (var caption in model.Captions)
        {
            PrepareLayoutElement(caption, new Size(captionConstraint, availableSize.Height));
            var size = MeasureElement(caption, captionConstraint, availableSize.Height);
            var margin = ResolveBox(caption, "margin", captionConstraint, availableSize.Height);
            size = new Size(size.Width + margin.Left + margin.Right, size.Height + margin.Top + margin.Bottom);
            captionSizes[caption] = size;
            captionWidth = Math.Max(captionWidth, size.Width);
            if (IsBottomCaption(caption)) bottomCaptionHeight += size.Height;
            else topCaptionHeight += size.Height;
        }

        var columnCount = Math.Max(1, model.ColumnCount);
        var rowCount = model.Rows.Count;
        var collapseBorders = IsCollapsed(table);
        var collapsedBorders = collapseBorders ? ResolveCollapsedBorders(model) : null;
        var (horizontalSpacing, verticalSpacing) = collapseBorders
            ? (0f, 0f)
            : ResolveBorderSpacing(table, availableSize.Width, availableSize.Height);
        var horizontalSpacingTotal = horizontalSpacing * (columnCount + 1);
        var verticalSpacingTotal = rowCount > 0 ? verticalSpacing * (rowCount + 1) : 0;
        var columnWidths = new float[columnCount];
        var intrinsicWidths = new Dictionary<CellSlot, float>();
        var fixedLayout = string.Equals(table.Style.Get("table-layout")?.Trim(), "fixed", StringComparison.OrdinalIgnoreCase) &&
            IsFinite(specifiedGridWidth);

        if (fixedLayout)
        {
            ApplyFixedColumnHints(model, columnWidths, specifiedGridWidth, horizontalSpacingTotal, availableSize.Height);
            DistributeRemainingWidth(columnWidths, Math.Max(0, specifiedGridWidth - horizontalSpacingTotal));
        }
        else
        {
            ApplyAutoColumnWidths(model, columnWidths, intrinsicWidths, constrainedWidth, availableSize.Height,
                horizontalSpacing, collapseBorders);
            var contentWidth = Sum(columnWidths);
            var targetColumnsWidth = IsFinite(specifiedGridWidth)
                ? Math.Max(0, specifiedGridWidth - horizontalSpacingTotal)
                : contentWidth;
            if (targetColumnsWidth > contentWidth)
                GrowColumns(columnWidths, targetColumnsWidth - contentWidth);
        }

        var gridWidth = Sum(columnWidths) + horizontalSpacingTotal;
        var contentWidthWithCaptions = Math.Max(gridWidth, captionWidth);
        var outerWidth = contentWidthWithCaptions + horizontalInsets;
        if (IsFinite(explicitWidth)) outerWidth = Math.Max(0, explicitWidth);

        var contentBoxWidth = Math.Max(0, outerWidth - horizontalInsets -
            (IsFinite(explicitWidth) ? scrollbarGutter.Width : 0));
        if (contentBoxWidth > gridWidth)
        {
            GrowColumns(columnWidths, contentBoxWidth - gridWidth);
            gridWidth = Sum(columnWidths) + horizontalSpacingTotal;
        }

        var rowHeights = new float[rowCount];
        foreach (var cell in model.Cells)
        {
            var width = SpanSize(columnWidths, cell.Column, cell.ColSpan, horizontalSpacing);
            PrepareLayoutElement(cell.Element, new Size(width, availableSize.Height));
            var measured = MeasureElement(cell.Element, width, float.PositiveInfinity,
                collapseBorders ? cell.CollapsedBorder : null);
            cell.MeasuredSize = measured;
            cell.ContentSize = MeasureContent(cell.Element, width, float.PositiveInfinity,
                collapseBorders ? cell.CollapsedBorder : null);
            if (cell.RowSpan == 1 && cell.Row < rowHeights.Length)
                rowHeights[cell.Row] = Math.Max(rowHeights[cell.Row], measured.Height);
        }

        for (var row = 0; row < rowHeights.Length; row++)
        {
            var rowElement = model.Rows[row].Element;
            if (rowElement == null) continue;
            var rowHeight = ResolveLength(rowElement, rowElement.Style.Get("height"), gridWidth, availableSize.Height);
            if (IsFinite(rowHeight)) rowHeights[row] = Math.Max(rowHeights[row], rowHeight);
        }

        foreach (var cell in model.Cells.Where(cell => cell.RowSpan > 1))
        {
            var current = SpanSize(rowHeights, cell.Row, cell.RowSpan, verticalSpacing);
            if (cell.MeasuredSize.Height > current)
                GrowSpan(rowHeights, cell.Row, cell.RowSpan, cell.MeasuredSize.Height - current);
        }

        var gridHeight = Sum(rowHeights) + verticalSpacingTotal;
        var naturalHeight = topCaptionHeight + gridHeight + bottomCaptionHeight + verticalInsets;
        var outerHeight = IsFinite(explicitHeight) ? Math.Max(explicitHeight, naturalHeight) : naturalHeight;
        if (!arrange) return new Size(outerWidth, outerHeight);

        table.Arrange(finalRect);
        var contentX = finalRect.X + border.Left + padding.Left + leadingLeft;
        var contentY = finalRect.Y + border.Top + padding.Top + leadingTop;
        var captionWidthForArrange = Math.Max(gridWidth, contentBoxWidth);
        var y = contentY;
        foreach (var caption in model.Captions.Where(caption => !IsBottomCaption(caption)))
        {
            var height = captionSizes[caption].Height;
            var margin = ResolveBox(caption, "margin", captionWidthForArrange, height);
            ArrangeElementContents(caption, new Rect(contentX + margin.Left, y + margin.Top,
                Math.Max(0, captionWidthForArrange - margin.Left - margin.Right),
                Math.Max(0, height - margin.Top - margin.Bottom)));
            y += height;
        }

        var gridX = contentX;
        var gridY = y;
        var columnX = new float[columnCount];
        var x = gridX + horizontalSpacing;
        for (var column = 0; column < columnCount; column++)
        {
            columnX[column] = x;
            x += columnWidths[column] + horizontalSpacing;
        }

        var rowY = new float[rowCount];
        y = gridY + (rowCount > 0 ? verticalSpacing : 0);
        for (var row = 0; row < rowCount; row++)
        {
            rowY[row] = y;
            y += rowHeights[row] + verticalSpacing;
        }

        ApplyTablePaintMetadata(table, model, collapseBorders, collapsedBorders, gridX, gridWidth,
            columnX, columnWidths, rowY, rowHeights);
        ArrangeRowsAndGroups(model, gridX, gridWidth, rowY, rowHeights);
        foreach (var cell in model.Cells)
        {
            var width = SpanSize(columnWidths, cell.Column, cell.ColSpan, horizontalSpacing);
            var height = SpanSize(rowHeights, cell.Row, cell.RowSpan, verticalSpacing);
            var cellRect = new Rect(columnX[cell.Column], rowY[cell.Row], width, height);
            cell.Element.Arrange(cellRect);
            ArrangeCellContents(cell.Element, cellRect, cell.ContentSize,
                collapseBorders ? cell.CollapsedBorder : null);
        }

        y = gridY + gridHeight;
        foreach (var caption in model.Captions.Where(IsBottomCaption))
        {
            var height = captionSizes[caption].Height;
            var margin = ResolveBox(caption, "margin", captionWidthForArrange, height);
            ArrangeElementContents(caption, new Rect(contentX + margin.Left, y + margin.Top,
                Math.Max(0, captionWidthForArrange - margin.Left - margin.Right),
                Math.Max(0, height - margin.Top - margin.Bottom)));
            y += height;
        }

        ClearDirtyRecursive(table);
        return finalRect.Size;
    }

    private void ApplyAutoColumnWidths(TableModel model, float[] widths, Dictionary<CellSlot, float> intrinsicWidths,
        float availableWidth, float availableHeight, float horizontalSpacing, bool collapseBorders)
    {
        foreach (var cell in model.Cells)
        {
            var widthHint = ResolveLength(cell.Element, cell.Element.Style.Get("width"), availableWidth, availableHeight);
            PrepareLayoutElement(cell.Element, new Size(availableWidth, availableHeight));
            var measured = MeasureElement(cell.Element, float.PositiveInfinity, float.PositiveInfinity,
                collapseBorders ? cell.CollapsedBorder : null);
            var intrinsic = IsFinite(widthHint) ? Math.Max(widthHint, measured.Width) : measured.Width;
            intrinsicWidths[cell] = intrinsic;
            if (cell.ColSpan == 1)
                widths[cell.Column] = Math.Max(widths[cell.Column], intrinsic);
        }

        foreach (var cell in model.Cells.Where(cell => cell.ColSpan > 1))
        {
            var current = SpanSize(widths, cell.Column, cell.ColSpan, horizontalSpacing);
            if (intrinsicWidths[cell] > current)
                GrowSpan(widths, cell.Column, cell.ColSpan, intrinsicWidths[cell] - current);
        }
    }

    private static void ApplyFixedColumnHints(TableModel model, float[] widths, float tableWidth,
        float spacingTotal, float availableHeight)
    {
        if (model.Rows.Count == 0) return;
        foreach (var cell in model.Cells.Where(cell => cell.Row == 0))
        {
            var hint = ResolveLength(cell.Element, cell.Element.Style.Get("width"), tableWidth, availableHeight);
            if (!IsFinite(hint)) continue;
            var perColumn = hint / cell.ColSpan;
            for (var column = cell.Column; column < Math.Min(widths.Length, cell.Column + cell.ColSpan); column++)
                widths[column] = Math.Max(widths[column], perColumn);
        }

        var availableColumnsWidth = Math.Max(0, tableWidth - spacingTotal);
        var used = Sum(widths);
        if (used <= availableColumnsWidth) return;
        var scale = used > 0 ? availableColumnsWidth / used : 0;
        for (var column = 0; column < widths.Length; column++) widths[column] *= scale;
    }

    private static void ArrangeRowsAndGroups(TableModel model, float gridX, float gridWidth,
        float[] rowY, float[] rowHeights)
    {
        foreach (var row in model.Rows)
        {
            if (row.Element == null) continue;
            var rowRect = new Rect(gridX, rowY[row.Index], gridWidth, rowHeights[row.Index]);
            row.Element.Arrange(rowRect);
        }

        foreach (var group in model.Groups)
        {
            if (group.Rows.Count == 0) continue;
            var first = group.Rows[0].Index;
            var last = group.Rows[^1].Index;
            var height = rowY[last] + rowHeights[last] - rowY[first];
            group.Element.Arrange(new Rect(gridX, rowY[first], gridWidth, height));
        }
    }

    private void ArrangeCellContents(Element cell, Rect cellRect, Size measured, Box? collapsedBorder)
    {
        var padding = ResolveBox(cell, "padding", cellRect.Width, cellRect.Height);
        var border = collapsedBorder ?? ResolveBorder(cell, cellRect.Width, cellRect.Height);
        var inner = Inset(cellRect,
            padding.Left + border.Left,
            padding.Top + border.Top,
            padding.Right + border.Right,
            padding.Bottom + border.Bottom);
        var contentHeight = Math.Min(inner.Height, measured.Height);
        var offset = (cell.Style.Get("vertical-align")?.Trim().ToLowerInvariant()) switch
        {
            "middle" => (inner.Height - contentHeight) / 2,
            "bottom" => inner.Height - contentHeight,
            _ => 0
        };
        ArrangeChildren(cell, new Rect(inner.X, inner.Y + Math.Max(0, offset), inner.Width, contentHeight));
    }

    private void ArrangeElementContents(Element element, Rect rect)
    {
        element.Arrange(rect);
        var padding = ResolveBox(element, "padding", rect.Width, rect.Height);
        var border = ResolveBorder(element, rect.Width, rect.Height);
        ArrangeChildren(element, Inset(rect,
            padding.Left + border.Left,
            padding.Top + border.Top,
            padding.Right + border.Right,
            padding.Bottom + border.Bottom));
    }

    private void ArrangeChildren(Element parent, Rect contentRect)
    {
        var visible = parent.Children.Where(child => child.IsVisible && !IsDisplayNone(child)).ToList();
        if (visible.Count == 0) return;

        var y = contentRect.Y;
        foreach (var child in visible)
        {
            var measured = Sanitize(child.Measure(new Size(contentRect.Width, Math.Max(0, contentRect.Bottom - y))));
            var height = measured.Height;
            _layout.Measure(child, new Size(contentRect.Width, height));
            _layout.Arrange(child, new Rect(contentRect.X, y, contentRect.Width, Math.Max(0, height)));
            y += height;
        }
    }

    private static Size MeasureElement(Element element, float availableWidth, float availableHeight, Box? collapsedBorder = null)
    {
        var padding = ResolveBox(element, "padding", availableWidth, availableHeight);
        var border = collapsedBorder ?? ResolveBorder(element, availableWidth, availableHeight);
        var horizontalInsets = padding.Left + padding.Right + border.Left + border.Right;
        var verticalInsets = padding.Top + padding.Bottom + border.Top + border.Bottom;
        var explicitWidth = ResolveLength(element, element.Style.Get("width"), availableWidth, availableHeight);
        var explicitHeight = ResolveLength(element, element.Style.Get("height"), availableWidth, availableHeight);

        var contentWidth = 0f;
        var contentHeight = 0f;
        var hasChildren = false;
        foreach (var child in element.Children)
        {
            if (!child.IsVisible || IsDisplayNone(child)) continue;
            hasChildren = true;
            var childAvailableWidth = IsFinite(availableWidth)
                ? Math.Max(0, availableWidth - horizontalInsets)
                : float.PositiveInfinity;
            var measured = Sanitize(child.Measure(new Size(childAvailableWidth, availableHeight)));
            contentWidth = Math.Max(contentWidth, measured.Width);
            contentHeight += measured.Height;
        }

        if (!hasChildren)
        {
            var measured = Sanitize(element.Measure(new Size(availableWidth, availableHeight)));
            contentWidth = measured.Width;
            contentHeight = measured.Height;
        }

        var width = IsFinite(explicitWidth) ? explicitWidth : contentWidth + horizontalInsets;
        var height = IsFinite(explicitHeight) ? explicitHeight : contentHeight + verticalInsets;
        return new Size(Math.Max(0, width), Math.Max(0, height));
    }

    private static Size MeasureContent(Element element, float availableWidth, float availableHeight, Box? collapsedBorder = null)
    {
        var padding = ResolveBox(element, "padding", availableWidth, availableHeight);
        var border = collapsedBorder ?? ResolveBorder(element, availableWidth, availableHeight);
        var contentWidth = IsFinite(availableWidth)
            ? Math.Max(0, availableWidth - padding.Left - padding.Right - border.Left - border.Right)
            : float.PositiveInfinity;
        var width = 0f;
        var height = 0f;
        var hasChildren = false;
        foreach (var child in element.Children)
        {
            if (!child.IsVisible || IsDisplayNone(child)) continue;
            hasChildren = true;
            var measured = Sanitize(child.Measure(new Size(contentWidth, availableHeight)));
            width = Math.Max(width, measured.Width);
            height += measured.Height;
        }
        return hasChildren ? new Size(width, height) : Sanitize(element.Measure(new Size(contentWidth, availableHeight)));
    }

    private static TableModel BuildModel(Element table)
    {
        var model = new TableModel();
        var anonymousCells = new List<Element>();
        foreach (var child in table.Children)
        {
            if (!child.IsVisible || IsDisplayNone(child)) continue;
            switch (GetRole(child))
            {
                case DisplayMode.TableCaption:
                    FlushAnonymousCells(model, null, anonymousCells);
                    model.Captions.Add(child);
                    break;
                case DisplayMode.TableHeaderGroup:
                case DisplayMode.TableRowGroup:
                case DisplayMode.TableFooterGroup:
                    FlushAnonymousCells(model, null, anonymousCells);
                    AddGroup(model, child);
                    break;
                case DisplayMode.TableRow:
                    FlushAnonymousCells(model, null, anonymousCells);
                    AddRow(model, child, null);
                    break;
                default:
                    anonymousCells.Add(child);
                    break;
            }
        }
        FlushAnonymousCells(model, null, anonymousCells);

        PlaceCells(model);
        return model;
    }

    private static void AddGroup(TableModel model, Element groupElement)
    {
        var group = new RowGroup(groupElement);
        model.Groups.Add(group);
        var anonymousCells = new List<Element>();
        foreach (var child in groupElement.Children)
        {
            if (!child.IsVisible || IsDisplayNone(child)) continue;
            if (GetRole(child) == DisplayMode.TableRow)
            {
                FlushAnonymousCells(model, group, anonymousCells);
                AddRow(model, child, group);
            }
            else
            {
                anonymousCells.Add(child);
            }
        }
        FlushAnonymousCells(model, group, anonymousCells);
    }

    private static void FlushAnonymousCells(TableModel model, RowGroup? group, List<Element> cells)
    {
        if (cells.Count == 0) return;
        AddRow(model, null, group, cells);
        cells.Clear();
    }

    private static void AddRow(TableModel model, Element? rowElement, RowGroup? group, IReadOnlyList<Element>? suppliedCells = null)
    {
        var row = new TableRowSlot(model.Rows.Count, rowElement);
        model.Rows.Add(row);
        group?.Rows.Add(row);
        var children = suppliedCells ?? rowElement?.Children.Where(child => child.IsVisible && !IsDisplayNone(child)).ToList() ?? [];
        foreach (var child in children)
            row.Cells.Add(new CellSlot(child, row.Index, GetColSpan(child), GetRowSpan(child)));
    }

    private static void PlaceCells(TableModel model)
    {
        var occupied = new List<HashSet<int>>();
        for (var rowIndex = 0; rowIndex < model.Rows.Count; rowIndex++)
        {
            while (occupied.Count <= rowIndex) occupied.Add([]);
            var column = 0;
            foreach (var cell in model.Rows[rowIndex].Cells)
            {
                while (!Fits(occupied, rowIndex, column, cell.RowSpan, cell.ColSpan)) column++;
                cell.Column = column;
                cell.SourceOrder = model.Cells.Count;
                model.Cells.Add(cell);
                model.ColumnCount = Math.Max(model.ColumnCount, column + cell.ColSpan);
                for (var row = rowIndex; row < rowIndex + cell.RowSpan; row++)
                {
                    while (occupied.Count <= row) occupied.Add([]);
                    for (var col = column; col < column + cell.ColSpan; col++) occupied[row].Add(col);
                }
                column += cell.ColSpan;
            }
        }
    }

    private static bool Fits(List<HashSet<int>> occupied, int row, int column, int rowSpan, int colSpan)
    {
        for (var y = row; y < row + rowSpan; y++)
        {
            if (y >= occupied.Count) continue;
            for (var x = column; x < column + colSpan; x++)
                if (occupied[y].Contains(x)) return false;
        }
        return true;
    }

    private static int GetColSpan(Element element) => element is TableCell cell
        ? cell.ColSpan
        : ParsePositiveInt(element.Style.Get("column-span"));

    private static int GetRowSpan(Element element) => element is TableCell cell
        ? cell.RowSpan
        : ParsePositiveInt(element.Style.Get("row-span"));

    private static int ParsePositiveInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? Math.Max(1, parsed) : 1;

    private static DisplayMode GetRole(Element element) => LayoutEngine.ParseDisplayMode(element.Style.Get("display"));

    private static bool IsDisplayNone(Element element) => GetRole(element) == DisplayMode.None;

    private static bool IsBottomCaption(Element caption) =>
        string.Equals(caption.Style.Get("caption-side")?.Trim(), "bottom", StringComparison.OrdinalIgnoreCase);

    private static bool IsCollapsed(Element table) =>
        string.Equals(table.Style.Get("border-collapse")?.Trim(), "collapse", StringComparison.OrdinalIgnoreCase);

    private static CollapsedBorderModel ResolveCollapsedBorders(TableModel model)
    {
        var result = new CollapsedBorderModel();
        foreach (var cell in model.Cells)
        {
            CssBoxPainter.TryGetBorderEdges(cell.Element, cell.Element.Style.GetAll(), out var edges);
            if (edges.Length == 0)
            {
                edges =
                [
                    new CssBoxPainter.BorderEdge(0, Color.Transparent, CssBoxPainter.BorderStyle.None),
                    new CssBoxPainter.BorderEdge(0, Color.Transparent, CssBoxPainter.BorderStyle.None),
                    new CssBoxPainter.BorderEdge(0, Color.Transparent, CssBoxPainter.BorderStyle.None),
                    new CssBoxPainter.BorderEdge(0, Color.Transparent, CssBoxPainter.BorderStyle.None)
                ];
            }

            for (var row = cell.Row; row < cell.Row + cell.RowSpan && row < model.Rows.Count; row++)
            {
                result.Add(new VerticalEdgeKey(cell.Column, row), new CollapsedBorderCandidate(cell, 3, edges[3]));
                result.Add(new VerticalEdgeKey(cell.Column + cell.ColSpan, row), new CollapsedBorderCandidate(cell, 1, edges[1]));
            }
            for (var column = cell.Column; column < cell.Column + cell.ColSpan && column < model.ColumnCount; column++)
            {
                result.Add(new HorizontalEdgeKey(cell.Row, column), new CollapsedBorderCandidate(cell, 0, edges[0]));
                result.Add(new HorizontalEdgeKey(cell.Row + cell.RowSpan, column), new CollapsedBorderCandidate(cell, 2, edges[2]));
            }
        }

        result.ResolveInsets();
        return result;
    }

    private static void ApplyTablePaintMetadata(
        Element table,
        TableModel model,
        bool collapseBorders,
        CollapsedBorderModel? collapsedBorders,
        float gridX,
        float gridWidth,
        float[] columnX,
        float[] columnWidths,
        float[] rowY,
        float[] rowHeights)
    {
        TablePaintMetadataStore.ClearForTable(table);
        foreach (var cell in model.Cells)
        {
            var metadata = TablePaintMetadataStore.Reset(cell.Element, table);
            metadata.SuppressCssBox = !collapseBorders && IsEmptyCell(cell.Element) &&
                string.Equals(cell.Element.Style.Get("empty-cells")?.Trim(), "hide", StringComparison.OrdinalIgnoreCase);
            metadata.UseCollapsedBorderFragments = collapseBorders;
            cell.Element.InvalidatePaint();
        }

        if (!collapseBorders || collapsedBorders == null) return;

        foreach (var pair in collapsedBorders.VerticalEdges)
        {
            var edge = pair.Value;
            if (edge.Width <= 0) continue;
            var boundary = pair.Key.Column == columnX.Length ? gridX + gridWidth : columnX[pair.Key.Column];
            var top = rowY[pair.Key.Row];
            var height = rowHeights[pair.Key.Row];
            foreach (var candidate in edge.Candidates)
            {
                var share = edge.GetShare(candidate.Side);
                if (share <= 0) continue;
                var left = candidate.Side == 3 ? boundary : boundary - share;
                TablePaintMetadataStore.Get(candidate.Cell.Element)
                    .CollapsedBorderFragments.Add(new TableBorderFragment(new Rect(left, top, share, height), edge.Color));
            }
        }

        foreach (var pair in collapsedBorders.HorizontalEdges)
        {
            var edge = pair.Value;
            if (edge.Width <= 0) continue;
            var boundary = pair.Key.Row == rowY.Length
                ? rowY[^1] + rowHeights[^1]
                : rowY[pair.Key.Row];
            var left = columnX[pair.Key.Column];
            var width = columnWidths[pair.Key.Column];
            foreach (var candidate in edge.Candidates)
            {
                var share = edge.GetShare(candidate.Side);
                if (share <= 0) continue;
                var top = candidate.Side == 0 ? boundary : boundary - share;
                TablePaintMetadataStore.Get(candidate.Cell.Element)
                    .CollapsedBorderFragments.Add(new TableBorderFragment(new Rect(left, top, width, share), edge.Color));
            }
        }
    }

    private static bool IsEmptyCell(Element cell)
    {
        if (cell.Children.Any(child => child.IsVisible && !IsDisplayNone(child))) return false;
        return !cell.ChildNodes.OfType<CharacterData>().Any(text => !string.IsNullOrWhiteSpace(text.Data));
    }

    private static (float Horizontal, float Vertical) ResolveBorderSpacing(Element table, float parentWidth, float parentHeight)
    {
        var value = table.Style.Get("border-spacing");
        if (string.IsNullOrWhiteSpace(value)) return (0, 0);
        var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var horizontal = ResolveLength(table, parts[0], parentWidth, parentHeight);
        var vertical = parts.Length > 1 ? ResolveLength(table, parts[1], parentWidth, parentHeight) : horizontal;
        return (IsFinite(horizontal) ? Math.Max(0, horizontal) : 0, IsFinite(vertical) ? Math.Max(0, vertical) : 0);
    }

    private static Box ResolveBox(Element element, string property, float parentWidth, float parentHeight)
    {
        var result = new Box();
        var value = element.Style.Get(property);
        if (!string.IsNullOrWhiteSpace(value))
        {
            var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length is >= 1 and <= 4)
            {
                var top = ResolveLength(element, parts[0], parentWidth, parentHeight);
                var right = ResolveLength(element, parts.Length > 1 ? parts[1] : parts[0], parentWidth, parentHeight);
                var bottom = ResolveLength(element, parts.Length > 2 ? parts[2] : parts[0], parentWidth, parentHeight);
                var left = ResolveLength(element, parts.Length > 3 ? parts[3] : parts.Length > 1 ? parts[1] : parts[0], parentWidth, parentHeight);
                result = new Box(SanitizeLength(top), SanitizeLength(right), SanitizeLength(bottom), SanitizeLength(left));
            }
        }

        result.Top = ResolveEdge(element, property + "-top", result.Top, parentWidth, parentHeight);
        result.Right = ResolveEdge(element, property + "-right", result.Right, parentWidth, parentHeight);
        result.Bottom = ResolveEdge(element, property + "-bottom", result.Bottom, parentWidth, parentHeight);
        result.Left = ResolveEdge(element, property + "-left", result.Left, parentWidth, parentHeight);
        return result;
    }

    private static Box ResolveBorder(Element element, float parentWidth, float parentHeight)
    {
        var result = ResolveBox(element, "border-width", parentWidth, parentHeight);
        result.Top = ResolveEdge(element, "border-top-width", result.Top, parentWidth, parentHeight);
        result.Right = ResolveEdge(element, "border-right-width", result.Right, parentWidth, parentHeight);
        result.Bottom = ResolveEdge(element, "border-bottom-width", result.Bottom, parentWidth, parentHeight);
        result.Left = ResolveEdge(element, "border-left-width", result.Left, parentWidth, parentHeight);
        return result;
    }

    private static float ResolveEdge(Element element, string property, float fallback, float parentWidth, float parentHeight)
    {
        var value = ResolveLength(element, element.Style.Get(property), parentWidth, parentHeight);
        return IsFinite(value) ? Math.Max(0, value) : fallback;
    }

    private static float ResolveLength(Element element, string? value, float parentWidth, float parentHeight)
    {
        if (string.IsNullOrWhiteSpace(value)) return float.NaN;
        var text = value.Replace(" ", "", StringComparison.Ordinal).Trim();
        if (text.EndsWith("vw", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var vw) && IsFinite(parentWidth))
            return parentWidth * vw / 100f;
        if (text.EndsWith("vh", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var vh) && IsFinite(parentHeight))
            return parentHeight * vh / 100f;
        var fontSize = ResolveFontSize(element);
        if (text.EndsWith("rem", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(text[..^3], NumberStyles.Float, CultureInfo.InvariantCulture, out var rem))
            return rem * ResolveRootFontSize(element);
        if (text.EndsWith("em", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var em))
            return em * fontSize;
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var px))
            return px;
        if (text.EndsWith('%') &&
            float.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) && IsFinite(parentWidth))
            return parentWidth * percent / 100f;
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var raw) ? raw : float.NaN;
    }

    private static float ResolveFontSize(Element element)
    {
        var value = element.Style.Get("font-size");
        if (value != null)
        {
            var parsed = ResolveLengthWithoutFont(value, 16);
            if (IsFinite(parsed)) return parsed;
        }
        return element.Parent != null ? ResolveFontSize(element.Parent) : 16;
    }

    private static float ResolveRootFontSize(Element element)
    {
        while (element.Parent != null) element = element.Parent;
        return ResolveFontSize(element);
    }

    private static float ResolveLengthWithoutFont(string value, float fallback)
    {
        var text = value.Trim();
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var px)) return px;
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var raw) ? raw : fallback;
    }

    private static void PrepareLayoutElement(Element element, Size availableSize)
    {
        if (element is ILayoutPreparingElement preparing) preparing.PrepareLayout(availableSize);
    }

    private static void DistributeRemainingWidth(float[] widths, float target)
    {
        var unset = widths.Count(width => width <= 0);
        var remaining = Math.Max(0, target - Sum(widths));
        if (unset > 0)
        {
            var share = remaining / unset;
            for (var index = 0; index < widths.Length; index++)
                if (widths[index] <= 0) widths[index] = share;
            return;
        }
        if (remaining > 0) GrowColumns(widths, remaining);
    }

    private static void GrowColumns(float[] widths, float extra)
    {
        if (widths.Length == 0 || extra <= 0) return;
        var share = extra / widths.Length;
        for (var index = 0; index < widths.Length; index++) widths[index] += share;
    }

    private static void GrowSpan(float[] sizes, int start, int span, float extra)
    {
        var count = Math.Min(span, sizes.Length - start);
        if (count <= 0 || extra <= 0) return;
        var share = extra / count;
        for (var index = start; index < start + count; index++) sizes[index] += share;
    }

    private static float SpanSize(float[] sizes, int start, int span, float spacing)
    {
        var count = Math.Min(span, sizes.Length - start);
        var result = spacing * Math.Max(0, count - 1);
        for (var index = start; index < start + count; index++) result += sizes[index];
        return result;
    }

    private static float Sum(float[] values)
    {
        var result = 0f;
        foreach (var value in values) result += value;
        return result;
    }

    private static Rect Inset(Rect rect, float left, float top, float right, float bottom) =>
        new(rect.X + left, rect.Y + top, Math.Max(0, rect.Width - left - right), Math.Max(0, rect.Height - top - bottom));

    private static Size Sanitize(Size size) => new(SanitizeLength(size.Width), SanitizeLength(size.Height));

    private static float SanitizeLength(float value) => IsFinite(value) ? Math.Max(0, value) : 0;

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static void ClearDirtyRecursive(Element element)
    {
        element.ClearLayoutDirty();
        foreach (var child in element.Children) ClearDirtyRecursive(child);
    }

    private sealed class TableModel
    {
        public List<Element> Captions { get; } = [];
        public List<RowGroup> Groups { get; } = [];
        public List<TableRowSlot> Rows { get; } = [];
        public List<CellSlot> Cells { get; } = [];
        public int ColumnCount { get; set; }
    }

    private sealed class RowGroup(Element element)
    {
        public Element Element { get; } = element;
        public List<TableRowSlot> Rows { get; } = [];
    }

    private sealed class TableRowSlot(int index, Element? element)
    {
        public int Index { get; } = index;
        public Element? Element { get; } = element;
        public List<CellSlot> Cells { get; } = [];
    }

    private sealed class CellSlot(Element element, int row, int colSpan, int rowSpan)
    {
        public Element Element { get; } = element;
        public int Row { get; } = row;
        public int Column { get; set; }
        public int SourceOrder { get; set; }
        public int ColSpan { get; } = colSpan;
        public int RowSpan { get; } = rowSpan;
        public Size MeasuredSize { get; set; }
        public Size ContentSize { get; set; }
        public Box CollapsedBorder { get; } = new();
    }

    private readonly record struct VerticalEdgeKey(int Column, int Row);
    private readonly record struct HorizontalEdgeKey(int Row, int Column);
    private readonly record struct CollapsedBorderCandidate(CellSlot Cell, int Side, CssBoxPainter.BorderEdge Border);

    private sealed class CollapsedBorderModel
    {
        public Dictionary<VerticalEdgeKey, CollapsedEdge> VerticalEdges { get; } = [];
        public Dictionary<HorizontalEdgeKey, CollapsedEdge> HorizontalEdges { get; } = [];

        public void Add(VerticalEdgeKey key, CollapsedBorderCandidate candidate) =>
            GetOrAdd(VerticalEdges, key).Candidates.Add(candidate);

        public void Add(HorizontalEdgeKey key, CollapsedBorderCandidate candidate) =>
            GetOrAdd(HorizontalEdges, key).Candidates.Add(candidate);

        public void ResolveInsets()
        {
            foreach (var edge in VerticalEdges.Values) Resolve(edge);
            foreach (var edge in HorizontalEdges.Values) Resolve(edge);
        }

        private static void Resolve(CollapsedEdge edge)
        {
            edge.Resolve();
            if (edge.Width <= 0) return;
            foreach (var candidate in edge.Candidates)
            {
                var share = edge.GetShare(candidate.Side);
                switch (candidate.Side)
                {
                    case 0: candidate.Cell.CollapsedBorder.Top = Math.Max(candidate.Cell.CollapsedBorder.Top, share); break;
                    case 1: candidate.Cell.CollapsedBorder.Right = Math.Max(candidate.Cell.CollapsedBorder.Right, share); break;
                    case 2: candidate.Cell.CollapsedBorder.Bottom = Math.Max(candidate.Cell.CollapsedBorder.Bottom, share); break;
                    case 3: candidate.Cell.CollapsedBorder.Left = Math.Max(candidate.Cell.CollapsedBorder.Left, share); break;
                }
            }
        }

        private static CollapsedEdge GetOrAdd<TKey>(Dictionary<TKey, CollapsedEdge> edges, TKey key)
            where TKey : notnull
        {
            if (edges.TryGetValue(key, out var edge)) return edge;
            edge = new CollapsedEdge();
            edges[key] = edge;
            return edge;
        }
    }

    private sealed class CollapsedEdge
    {
        public List<CollapsedBorderCandidate> Candidates { get; } = [];
        public float Width { get; private set; }
        public Color Color { get; private set; }

        public void Resolve()
        {
            CollapsedBorderCandidate? winner = null;
            foreach (var candidate in Candidates)
            {
                var width = candidate.Border.Style == CssBoxPainter.BorderStyle.Solid && candidate.Border.Color.A > 0
                    ? Math.Max(0, candidate.Border.Width)
                    : 0;
                if (winner == null || width > Width || width == Width && candidate.Cell.SourceOrder > winner.Value.Cell.SourceOrder)
                {
                    winner = candidate;
                    Width = width;
                    Color = candidate.Border.Color;
                }
            }
        }

        public float GetShare(int side)
        {
            var hasOpposite = side switch
            {
                0 => Candidates.Any(candidate => candidate.Side == 2),
                1 => Candidates.Any(candidate => candidate.Side == 3),
                2 => Candidates.Any(candidate => candidate.Side == 0),
                _ => Candidates.Any(candidate => candidate.Side == 1)
            };
            return hasOpposite ? Width / 2 : Width;
        }
    }

    private sealed class Box
    {
        public Box() { }
        public Box(float top, float right, float bottom, float left)
        {
            Top = top;
            Right = right;
            Bottom = bottom;
            Left = left;
        }

        public float Top { get; set; }
        public float Right { get; set; }
        public float Bottom { get; set; }
        public float Left { get; set; }
    }
}
