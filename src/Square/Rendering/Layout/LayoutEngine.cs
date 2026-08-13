using System.Globalization;
using System.Buffers;
using Facebook.Yoga;
using Square.Graphics;
using Square.UI;
using static Facebook.Yoga.YGConfigAPI;
using static Facebook.Yoga.YGNodeAPI;
using static Facebook.Yoga.YGNodeLayoutAPI;
using static Facebook.Yoga.YGNodeStyleAPI;
using YogaNode = Facebook.Yoga.Node;

namespace Square.Rendering;

/// <summary>显示模式（对齐 CSS <c>display</c> 子集）。</summary>
public enum DisplayMode
{
    Block,
    Flex,
    Grid,
    None,
    Table,
    InlineTable,
    TableRowGroup,
    TableHeaderGroup,
    TableFooterGroup,
    TableRow,
    TableCell,
    TableCaption
}

/// <summary>Flex 主轴方向（对齐 <c>flex-direction</c>）。</summary>
public enum FlexDirection { Row, Column, RowReverse, ColumnReverse }

/// <summary>主轴对齐（对齐 <c>justify-content</c> 子集）。</summary>
public enum JustifyContent { FlexStart, Center, FlexEnd, SpaceBetween, SpaceAround }

/// <summary>交叉轴对齐（对齐 <c>align-items</c> 子集）。</summary>
public enum AlignItems { Stretch, FlexStart, Center, FlexEnd }

/// <summary>CSS 盒尺寸计算方式。</summary>
public enum BoxSizing { BorderBox, ContentBox }

/// <summary>从 CSS 推导的布局样式快照。</summary>
public sealed class ComputedStyle
{
    /// <summary>显示模式。</summary>
    public DisplayMode Display { get; set; } = DisplayMode.Block;
    /// <summary>Flex 主轴方向。</summary>
    public FlexDirection FlexDirection { get; set; } = FlexDirection.Row;
    /// <summary>主轴对齐方式。</summary>
    public JustifyContent JustifyContent { get; set; } = JustifyContent.FlexStart;
    /// <summary>交叉轴对齐方式。</summary>
    public AlignItems AlignItems { get; set; } = AlignItems.Stretch;
    /// <summary>放大因子。</summary>
    public float FlexGrow { get; set; }
    /// <summary>收缩因子。</summary>
    public float FlexShrink { get; set; } = 1f;
    /// <summary>基准尺寸（NaN 表示自动）。</summary>
    public float FlexBasis { get; set; } = float.NaN;
    /// <summary>子项间距。</summary>
    public float Gap { get; set; }
    /// <summary>行间距。</summary>
    public float RowGap { get; set; }
    /// <summary>列间距。</summary>
    public float ColumnGap { get; set; }
    /// <summary>宽度（NaN 表示自动）。</summary>
    public float Width { get; set; } = float.NaN;
    /// <summary>高度（NaN 表示自动）。</summary>
    public float Height { get; set; } = float.NaN;
    /// <summary>四向内边距简写值。</summary>
    public float Padding { get; set; }
    /// <summary>左内边距。</summary>
    public float PaddingLeft { get; set; }
    /// <summary>上内边距。</summary>
    public float PaddingTop { get; set; }
    /// <summary>右内边距。</summary>
    public float PaddingRight { get; set; }
    /// <summary>下内边距。</summary>
    public float PaddingBottom { get; set; }
    /// <summary>四向外边距简写值。</summary>
    public float Margin { get; set; }
    /// <summary>左外边距。</summary>
    public float MarginLeft { get; set; }
    /// <summary>上外边距。</summary>
    public float MarginTop { get; set; }
    /// <summary>右外边距。</summary>
    public float MarginRight { get; set; }
    /// <summary>下外边距。</summary>
    public float MarginBottom { get; set; }
    /// <summary>盒尺寸计算方式。</summary>
    public BoxSizing BoxSizing { get; set; } = BoxSizing.BorderBox;
    /// <summary>Grid 列模板。</summary>
    public string GridTemplateColumns { get; set; } = "";
    /// <summary>Grid 行模板。</summary>
    public string GridTemplateRows { get; set; } = "";
    /// <summary>Grid 起始列号。</summary>
    public int GridColumn { get; set; } = 1;
    /// <summary>Grid 起始行号。</summary>
    public int GridRow { get; set; } = 1;
    /// <summary>Grid 列跨度。</summary>
    public int GridColumnSpan { get; set; } = 1;
    /// <summary>Grid 行跨度。</summary>
    public int GridRowSpan { get; set; } = 1;
    /// <summary>Grid 区域名称。</summary>
    public string GridArea { get; set; } = "";
}

/// <summary>
/// 布局引擎：Flex/Block 由 Meta Yoga（Yoga.Net）计算；Grid 使用内置实现。
/// CSS 经 <see cref="Element.Style"/> 映射到 Yoga 样式或 Grid 算法。
/// </summary>
public sealed partial class LayoutEngine
{
    [ThreadStatic]
    private static int _layoutDepth;
    [ThreadStatic]
    private static float _viewportWidth;
    [ThreadStatic]
    private static float _viewportHeight;

    private readonly Config _yogaConfig;

    /// <summary>创建布局引擎实例。</summary>
    public LayoutEngine()
    {
        _yogaConfig = YGConfigNew();
        YGConfigSetUseWebDefaults(_yogaConfig, true);
    }

    /// <summary>返回已布局元素的真实 content/padding/border/margin 四层几何。</summary>
    internal LayoutBoxModel? GetInspectionBoxModel(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var parentSize = element.Parent is { } parent
            ? GetInspectionBoxModel(parent)?.Content.Size ?? parent.Geometry.Size
            : element.Geometry.Size;
        if (!CanProveInspectionEdges(element)) return null;
        var box = ResolveCssBox(element, parentSize.Width, parentSize.Height);
        var border = element.Geometry;
        var padding = Inset(border, box.BorderLeft, box.BorderTop, box.BorderRight, box.BorderBottom);
        var content = Inset(padding, box.PaddingLeft, box.PaddingTop, box.PaddingRight, box.PaddingBottom);
        var margin = new Rect(
            border.X - box.MarginLeft,
            border.Y - box.MarginTop,
            border.Width + box.MarginLeft + box.MarginRight,
            border.Height + box.MarginTop + box.MarginBottom);
        return new LayoutBoxModel(content, padding, border, margin);
    }

    private static bool CanProveInspectionEdges(Element element)
    {
        foreach (var property in new[]
                 {
                     "padding", "padding-top", "padding-right", "padding-bottom", "padding-left",
                     "border-width", "border-top-width", "border-right-width", "border-bottom-width", "border-left-width",
                     "margin", "margin-top", "margin-right", "margin-bottom", "margin-left"
                 })
        {
            var value = element.Style.Get(property);
            if (value == null) continue;
            foreach (var token in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (token.Equals("auto", StringComparison.OrdinalIgnoreCase) || token.EndsWith('%'))
                    return false;
            }
        }
        return true;
    }

    /// <summary>测量元素在给定可用尺寸下的期望尺寸。</summary>
    public void Measure(Element element, Size availableSize)
    {
        var outermost = _layoutDepth++ == 0;
        if (outermost)
        {
            _viewportWidth = availableSize.Width;
            _viewportHeight = availableSize.Height;
        }
        try
        {
            MeasureCore(element, availableSize);
        }
        finally
        {
            _layoutDepth--;
        }
    }

    private void MeasureCore(Element element, Size availableSize)
    {
        var style = GetComputedStyle(element, availableSize.Width, availableSize.Height);
        if (!element.IsVisible || style.Display == DisplayMode.None)
        {
            element.ClearLayoutDirty();
            return;
        }

        if (style.Display == DisplayMode.Grid)
        {
            MeasureGrid(element, style, availableSize);
            element.ClearLayoutDirty();
            return;
        }

        if (IsTableRoot(style.Display))
        {
            new TableLayoutEngine(this).Measure(element, availableSize);
            element.ClearLayoutDirty();
            return;
        }

        if (UsesCssNormalFlow(element))
        {
            MeasureCssNormalFlow(element, availableSize);
            ClearDirtyRecursive(element);
            return;
        }

        // Flex / Block：Yoga 一次计算
        using var session = BuildYogaTree(element, availableSize.Width, availableSize.Height);
        element.ClearLayoutDirty();
        foreach (var child in element.Children)
            ClearDirtyRecursive(child);
    }

    /// <summary>按最终矩形排列元素及其子树。</summary>
    public void Arrange(Element element, Rect finalRect)
    {
        var outermost = _layoutDepth++ == 0;
        if (outermost)
        {
            _viewportWidth = finalRect.Width;
            _viewportHeight = finalRect.Height;
        }
        try
        {
            ArrangeCore(element, finalRect);
        }
        finally
        {
            _layoutDepth--;
        }
    }

    private void ArrangeCore(Element element, Rect finalRect)
    {
        var style = GetComputedStyle(element, finalRect.Width, finalRect.Height);
        if (!element.IsVisible || style.Display == DisplayMode.None)
        {
            element.Arrange(finalRect);
            return;
        }

        if (style.Display == DisplayMode.Grid)
        {
            var inner = Inset(finalRect, style.PaddingLeft, style.PaddingTop, style.PaddingRight, style.PaddingBottom);
            ArrangeGrid(element, style, inner);
            element.Arrange(finalRect);
            return;
        }

        if (IsTableRoot(style.Display))
        {
            new TableLayoutEngine(this).Arrange(element, finalRect);
            return;
        }

        if (UsesCssNormalFlow(element))
        {
            ArrangeCssNormalFlow(element, finalRect);
            ArrangePopupSubtrees(element);
            ClearDirtyRecursive(element);
            return;
        }

        using var session = BuildYogaTree(element, finalRect.Width, finalRect.Height);
        ApplyYogaLayout(element, session.Root, finalRect.X, finalRect.Y);
        ArrangePopupSubtrees(element);
        ClearDirtyRecursive(element);
    }

    /// <summary>在可用尺寸等于最终尺寸时复用同一棵 Yoga 树完成测量和排列。</summary>
    public void MeasureAndArrange(Element element, Size availableSize)
    {
        var outermost = _layoutDepth++ == 0;
        if (outermost)
        {
            _viewportWidth = availableSize.Width;
            _viewportHeight = availableSize.Height;
        }

        try
        {
            var style = GetComputedStyle(element, availableSize.Width, availableSize.Height);
            if (!element.IsVisible || style.Display == DisplayMode.None)
            {
                element.ClearLayoutDirty();
                element.Arrange(new Rect(0, 0, 0, 0));
                return;
            }

            if (style.Display == DisplayMode.Grid)
            {
                MeasureGrid(element, style, availableSize);
                var inner = Inset(new Rect(0, 0, availableSize.Width, availableSize.Height),
                    style.PaddingLeft, style.PaddingTop, style.PaddingRight, style.PaddingBottom);
                ArrangeGrid(element, style, inner);
                element.Arrange(new Rect(0, 0, availableSize.Width, availableSize.Height));
                element.ClearLayoutDirty();
                return;
            }

            if (IsTableRoot(style.Display))
            {
                var tableLayout = new TableLayoutEngine(this);
                tableLayout.Measure(element, availableSize);
                tableLayout.Arrange(element, new Rect(0, 0, availableSize.Width, availableSize.Height));
                element.ClearLayoutDirty();
                return;
            }

            if (UsesCssNormalFlow(element))
            {
                MeasureCssNormalFlow(element, availableSize);
                ArrangeCssNormalFlow(element, new Rect(0, 0, availableSize.Width, availableSize.Height));
                ArrangePopupSubtrees(element);
                ClearDirtyRecursive(element);
                return;
            }

            using var session = BuildYogaTree(element, availableSize.Width, availableSize.Height);
            ApplyYogaLayout(element, session.Root, 0, 0);
            ArrangePopupSubtrees(element);
            ClearDirtyRecursive(element);
        }
        finally
        {
            _layoutDepth--;
        }
    }

    // ——— Yoga Flex/Block ———

    private YogaSession BuildYogaTree(Element root, float width, float height)
    {
        var session = new YogaSession();
        var rootFontSize = GetRootFontSize(root);
        var yogaRoot = CreateYogaSubtree(root, session, width, height, rootFontSize, isRoot: true);
        session.Root = yogaRoot;

        if (!float.IsNaN(width) && !float.IsInfinity(width) && width >= 0)
            YGNodeStyleSetWidth(yogaRoot, width);
        if (!float.IsNaN(height) && !float.IsInfinity(height) && height >= 0)
            YGNodeStyleSetHeight(yogaRoot, height);

        YGNodeCalculateLayout(
            yogaRoot,
            float.IsNaN(width) || float.IsInfinity(width) ? float.NaN : width,
            float.IsNaN(height) || float.IsInfinity(height) ? float.NaN : height,
            YGDirection.LTR);
        return session;
    }

    private YogaNode CreateYogaSubtree(
        Element element, YogaSession session, float parentW, float parentH, float rem, bool isRoot)
    {
        if (element is ILayoutPreparingElement preparing)
            preparing.PrepareLayout(new Size(parentW, parentH));

        var node = YGNodeNewWithConfig(_yogaConfig);
        YGNodeSetContext(node, element);
        session.Map[element] = node;

        var em = GetFontSize(element);
        var display = element.Style.Get("display")?.Trim();

        if (string.Equals(display, "none", StringComparison.OrdinalIgnoreCase))
        {
            YGNodeStyleSetDisplay(node, YGDisplay.None);
            return node;
        }

        if (string.Equals(display, "grid", StringComparison.OrdinalIgnoreCase))
        {
            // Grid 子树：作为叶子，用内置 Grid 引擎在 measure 中排版
            YGNodeStyleSetDisplay(node, YGDisplay.Flex);
            YGNodeStyleSetFlexDirection(node, YGFlexDirection.Column);
            ApplyBoxModel(element, node, parentW, parentH, em, rem);
            YGNodeSetMeasureFunc(node, GridHostMeasureCallback);
            return node;
        }

        if (IsTableRoot(ParseDisplayMode(display)))
        {
            YGNodeStyleSetDisplay(node, YGDisplay.Flex);
            YGNodeStyleSetFlexDirection(node, YGFlexDirection.Column);
            ApplyBoxModel(element, node, parentW, parentH, em, rem);
            YGNodeSetMeasureFunc(node, TableHostMeasureCallback);
            return node;
        }

        if (string.Equals(display, "flex", StringComparison.OrdinalIgnoreCase))
        {
            YGNodeStyleSetDisplay(node, YGDisplay.Flex);
            ApplyFlexContainer(element, node);
        }
        else
        {
            // block → column flex
            YGNodeStyleSetDisplay(node, YGDisplay.Flex);
            YGNodeStyleSetFlexDirection(node, YGFlexDirection.Column);
            YGNodeStyleSetAlignItems(node, YGAlign.Stretch);
            YGNodeStyleSetJustifyContent(node, YGJustify.FlexStart);
        }

        ApplyFlexItem(element, node, parentW, parentH, em, rem);
        ApplyDirectionAndAspectRatio(element, node);
        ApplyBoxModel(element, node, parentW, parentH, em, rem);
        ApplyBorder(element, node, parentW, parentH, em, rem);
        ApplyGap(element, node, parentW, parentH, em, rem);
        ApplyPosition(element, node, parentW, parentH, em, rem);
        ApplyOverflow(element, node);

        var (visibleChildren, visibleCount) = RentVisibleChildren(element);
        if (element is Square.UI.Svg.SVGSVGElement) visibleCount = 0;
        try
        {
            if (visibleCount == 0)
            {
                YGNodeSetMeasureFunc(node, LeafMeasureCallback);
            }
            else
            {
                var refW = ResolveRefSize(element.Style.Get("width"), parentW, parentH, em, rem, parentW);
                var refH = ResolveRefSize(element.Style.Get("height"), parentW, parentH, em, rem, parentH);
                uint i = 0;
                for (var j = 0; j < visibleCount; j++)
                {
                    var child = visibleChildren[j];
                    if (child is IPopupElement { IsLayoutOverlay: true }) continue;
                    var childNode = CreateYogaSubtree(child, session, refW, refH, rem, isRoot: false);
                    if (element.IsScrollContainer() && child.Style.Get("flex-shrink") == null)
                        YGNodeStyleSetFlexShrink(childNode, 0);
                    YGNodeInsertChild(node, childNode, i++);
                }
            }
        }
        finally
        {
            ReturnVisibleChildren(visibleChildren);
        }
        ApplyIntrinsicLeafMinSize(element, node);
        ApplyIntrinsicRowItemSize(element, node);

        return node;
    }

    private static void ApplyIntrinsicRowItemSize(Element element, YogaNode node)
    {
        if (!element.HasCustomMeasure || element.Style.Get("width") != null || element.Parent == null) return;
        if (!string.Equals(element.Parent.Style.Get("flex-direction")?.Trim(), "row", StringComparison.OrdinalIgnoreCase)) return;
        foreach (var child in element.Children)
        {
            if (child.IsVisible && !string.Equals(child.Style.Get("position")?.Trim(), "absolute", StringComparison.OrdinalIgnoreCase))
                return;
        }

        var measured = element.Measure(new Size(float.MaxValue, float.MaxValue));
        if (IsFiniteLayoutSize(measured.Width)) YGNodeStyleSetWidth(node, measured.Width);
    }

    private static void ApplyIntrinsicLeafMinSize(Element element, YogaNode node)
    {
        if (!element.HasCustomMeasure) return;

        var measured = element.Measure(new Size(float.MaxValue, float.MaxValue));
        var preservesIntrinsicWidth = element is not Square.Controls.Text || IsDirectRowItem(element);
        if (preservesIntrinsicWidth && element.Style.Get("min-width") == null &&
            element.Style.Get("width") == null && IsFiniteLayoutSize(measured.Width))
            YGNodeStyleSetMinWidth(node, measured.Width);
        if (element.Style.Get("min-height") == null && element.Style.Get("height") == null && IsFiniteLayoutSize(measured.Height))
            YGNodeStyleSetMinHeight(node, measured.Height);
    }

    private static bool IsDirectRowItem(Element element) =>
        element.Parent != null &&
        (element.Parent.Style.Get("flex-direction")?.Trim().ToLowerInvariant() is "row" or "row-reverse");

    private static bool IsFiniteLayoutSize(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0 && value < 1_000_000f;

    private static YGSize LeafMeasureCallback(
        YogaNode node, float availableWidth, MeasureMode widthMode,
        float availableHeight, MeasureMode heightMode)
    {
        var element = YGNodeGetContext(node) as Element;
        if (element == null) return new YGSize { Width = 0, Height = 0 };

        var availW = widthMode == MeasureMode.Undefined ? float.MaxValue : Math.Max(0, availableWidth);
        var availH = heightMode == MeasureMode.Undefined ? float.MaxValue : Math.Max(0, availableHeight);
        var measured = element.Measure(new Size(availW, availH));

        var w = measured.Width;
        var h = measured.Height;
        if (widthMode == MeasureMode.Exactly) w = availableWidth;
        else if (widthMode == MeasureMode.AtMost) w = Math.Min(w, availableWidth);
        if (heightMode == MeasureMode.Exactly) h = availableHeight;
        else if (heightMode == MeasureMode.AtMost) h = Math.Min(h, availableHeight);

        return new YGSize
        {
            Width = Sanitize(w),
            Height = Sanitize(h)
        };
    }

    private YGSize GridHostMeasureCallback(
        YogaNode node, float availableWidth, MeasureMode widthMode,
        float availableHeight, MeasureMode heightMode)
    {
        var element = YGNodeGetContext(node) as Element;
        if (element == null) return new YGSize { Width = 0, Height = 0 };

        var w = widthMode == MeasureMode.Undefined ? 0 : availableWidth;
        var h = heightMode == MeasureMode.Undefined ? 0 : availableHeight;
        if (widthMode == MeasureMode.Undefined || heightMode == MeasureMode.Undefined)
        {
            // 尽量给出合理默认
            if (widthMode == MeasureMode.Undefined) w = 0;
            if (heightMode == MeasureMode.Undefined) h = 0;
        }

        var style = GetComputedStyle(element, w, h);
        MeasureGrid(element, style, new Size(
            widthMode == MeasureMode.Undefined ? float.MaxValue : w,
            heightMode == MeasureMode.Undefined ? float.MaxValue : h));

        // 用子项排布估算占位尺寸
        return new YGSize
        {
            Width = widthMode == MeasureMode.Exactly ? availableWidth : Sanitize(w),
            Height = heightMode == MeasureMode.Exactly ? availableHeight : Sanitize(h)
        };
    }

    private YGSize TableHostMeasureCallback(
        YogaNode node, float availableWidth, MeasureMode widthMode,
        float availableHeight, MeasureMode heightMode)
    {
        var element = YGNodeGetContext(node) as Element;
        if (element == null) return new YGSize { Width = 0, Height = 0 };

        var available = new Size(
            widthMode == MeasureMode.Undefined ? float.PositiveInfinity : Math.Max(0, availableWidth),
            heightMode == MeasureMode.Undefined ? float.PositiveInfinity : Math.Max(0, availableHeight));
        var measured = new TableLayoutEngine(this).Measure(element, available);
        return new YGSize
        {
            Width = widthMode == MeasureMode.Exactly ? availableWidth :
                widthMode == MeasureMode.AtMost ? Math.Min(measured.Width, availableWidth) : measured.Width,
            Height = heightMode == MeasureMode.Exactly ? availableHeight :
                heightMode == MeasureMode.AtMost ? Math.Min(measured.Height, availableHeight) : measured.Height
        };
    }

    private void ApplyYogaLayout(Element element, YogaNode yoga, float parentAbsX, float parentAbsY)
    {
        var left = YGNodeLayoutGetLeft(yoga);
        var top = YGNodeLayoutGetTop(yoga);
        var width = YGNodeLayoutGetWidth(yoga);
        var height = YGNodeLayoutGetHeight(yoga);
        var absX = parentAbsX + left;
        var absY = parentAbsY + top;
        var rect = new Rect(absX, absY, width, height);

        var display = element.Style.Get("display")?.Trim();
        if (string.Equals(display, "grid", StringComparison.OrdinalIgnoreCase))
        {
            var style = GetComputedStyle(element, width, height);
            var inner = Inset(rect, style.PaddingLeft, style.PaddingTop, style.PaddingRight, style.PaddingBottom);
            ArrangeGrid(element, style, inner);
            element.Arrange(rect);
            return;
        }

        if (IsTableRoot(ParseDisplayMode(display)))
        {
            new TableLayoutEngine(this).Arrange(element, rect);
            return;
        }

        element.Arrange(rect);

        var (visibleChildren, visibleCount) = RentVisibleChildren(element);
        try
        {
            var count = (int)YGNodeGetChildCount(yoga);
            var yogaIndex = 0;
            for (var i = 0; i < visibleCount && yogaIndex < count; i++)
            {
                if (visibleChildren[i] is IPopupElement { IsLayoutOverlay: true }) continue;
                var childYoga = YGNodeGetChild(yoga, (nuint) yogaIndex++);
                if (childYoga != null)
                    ApplyYogaLayout(visibleChildren[i], childYoga, absX, absY);
            }
        }
        finally
        {
            ReturnVisibleChildren(visibleChildren);
        }
        UpdateScrollContentSize(element, rect);
    }

    private void ArrangePopupSubtrees(Element element)
    {
        foreach (var child in element.Children)
        {
            if (child is IPopupElement { IsLayoutOverlay: true })
            {
                var measured = child.Measure(new Size(float.MaxValue, float.MaxValue));
                var width = ResolvePopupDimension(child.Style.Get("width"), measured.Width);
                var height = ResolvePopupDimension(child.Style.Get("height"), measured.Height);
                using var session = BuildYogaTree(child, width, height);
                ApplyYogaLayout(child, session.Root, 0, 0);
            }
            ArrangePopupSubtrees(child);
        }
    }

    private static float ResolvePopupDimension(string? value, float measured)
    {
        if (TryParsePoints(value, float.MaxValue, float.MaxValue, 16, 16, out var points)) return points;
        return IsFiniteLayoutSize(measured) ? measured : 0;
    }

    private static void UpdateScrollContentSize(Element element, Rect rect)
    {
        if (!element.IsScrollContainer())
        {
            element.SetScrollContentSize(rect.Size);
            return;
        }

        var right = rect.Width;
        var bottom = rect.Height;
        foreach (var child in element.Children)
        {
            if (!child.IsVisible) continue;
            right = Math.Max(right, child.Geometry.Right - rect.X);
            bottom = Math.Max(bottom, child.Geometry.Bottom - rect.Y);
        }

        element.SetScrollContentSize(new Size(right, bottom));
    }

    private static void ApplyFlexContainer(Element element, YogaNode node)
    {
        YGNodeStyleSetFlexDirection(node, element.Style.Get("flex-direction")?.Trim() switch
        {
            "column" => YGFlexDirection.Column,
            "column-reverse" => YGFlexDirection.ColumnReverse,
            "row-reverse" => YGFlexDirection.RowReverse,
            _ => YGFlexDirection.Row
        });
        YGNodeStyleSetJustifyContent(node, element.Style.Get("justify-content")?.Trim() switch
        {
            "center" => YGJustify.Center,
            "flex-end" or "end" => YGJustify.FlexEnd,
            "space-between" => YGJustify.SpaceBetween,
            "space-around" => YGJustify.SpaceAround,
            "space-evenly" => YGJustify.SpaceEvenly,
            _ => YGJustify.FlexStart
        });
        YGNodeStyleSetAlignItems(node, element.Style.Get("align-items")?.Trim() switch
        {
            "center" => YGAlign.Center,
            "flex-start" or "start" => YGAlign.FlexStart,
            "flex-end" or "end" => YGAlign.FlexEnd,
            "baseline" => YGAlign.Baseline,
            _ => YGAlign.Stretch
        });
        YGNodeStyleSetAlignContent(node, element.Style.Get("align-content")?.Trim() switch
        {
            "center" => YGAlign.Center,
            "flex-start" or "start" => YGAlign.FlexStart,
            "flex-end" or "end" => YGAlign.FlexEnd,
            "space-between" => YGAlign.SpaceBetween,
            "space-around" => YGAlign.SpaceAround,
            "space-evenly" => YGAlign.SpaceEvenly,
            "stretch" => YGAlign.Stretch,
            _ => YGAlign.Auto
        });
        YGNodeStyleSetFlexWrap(node, element.Style.Get("flex-wrap")?.Trim() switch
        {
            "wrap" => YGWrap.Wrap,
            "wrap-reverse" => YGWrap.WrapReverse,
            _ => YGWrap.NoWrap
        });
    }

    private static void ApplyFlexItem(Element element, YogaNode node, float parentW, float parentH, float em, float rem)
    {
        var flex = element.Style.Get("flex");
        ApplyFlexShorthand(flex, node, parentW, parentH, em, rem);

        var grow = element.Style.Get("flex-grow");
        if (grow != null && float.TryParse(grow, NumberStyles.Float, CultureInfo.InvariantCulture, out var g))
            YGNodeStyleSetFlexGrow(node, g);
        var shrink = element.Style.Get("flex-shrink");
        if (shrink != null && float.TryParse(shrink, NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
            YGNodeStyleSetFlexShrink(node, s);
        else if (flex == null && HasExplicitMainAxisSize(element))
            YGNodeStyleSetFlexShrink(node, 0);
        var basis = element.Style.Get("flex-basis");
        if (basis != null)
        {
            var t = basis.Trim();
            if (string.Equals(t, "auto", StringComparison.OrdinalIgnoreCase))
                YGNodeStyleSetFlexBasisAuto(node);
            else if (t.EndsWith('%') && float.TryParse(t[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                YGNodeStyleSetFlexBasisPercent(node, pct);
            else if (TryParsePoints(t, parentW, parentH, em, rem, out var pts))
                YGNodeStyleSetFlexBasis(node, pts);
        }
        var alignSelf = element.Style.Get("align-self")?.Trim();
        if (alignSelf != null)
        {
            YGNodeStyleSetAlignSelf(node, alignSelf switch
            {
                "center" => YGAlign.Center,
                "flex-start" or "start" => YGAlign.FlexStart,
                "flex-end" or "end" => YGAlign.FlexEnd,
                "stretch" => YGAlign.Stretch,
                "baseline" => YGAlign.Baseline,
                _ => YGAlign.Auto
            });
        }
    }

    private static bool HasExplicitMainAxisSize(Element element)
    {
        var parentDirection = element.Parent?.Style.Get("flex-direction")?.Trim();
        var mainAxisSize = parentDirection is "row" or "row-reverse" ? "width" : "height";
        var value = element.Style.Get(mainAxisSize)?.Trim();
        return !string.IsNullOrEmpty(value) && !string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyFlexShorthand(string? value, YogaNode node, float parentW, float parentH, float em, float rem)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var text = value.Trim();
        if (string.Equals(text, "none", StringComparison.OrdinalIgnoreCase))
        {
            YGNodeStyleSetFlexGrow(node, 0);
            YGNodeStyleSetFlexShrink(node, 0);
            YGNodeStyleSetFlexBasisAuto(node);
            return;
        }
        if (string.Equals(text, "auto", StringComparison.OrdinalIgnoreCase))
        {
            YGNodeStyleSetFlexGrow(node, 1);
            YGNodeStyleSetFlexShrink(node, 1);
            YGNodeStyleSetFlexBasisAuto(node);
            return;
        }

        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var singleGrow))
        {
            YGNodeStyleSetFlexGrow(node, singleGrow);
            YGNodeStyleSetFlexShrink(node, 1);
            YGNodeStyleSetFlexBasisPercent(node, 0);
            return;
        }

        var numericIndex = 0;
        foreach (var part in parts)
        {
            if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                if (numericIndex == 0) YGNodeStyleSetFlexGrow(node, number);
                else if (numericIndex == 1) YGNodeStyleSetFlexShrink(node, number);
                else ApplyFlexBasis(part, node, parentW, parentH, em, rem);
                numericIndex++;
                continue;
            }

            ApplyFlexBasis(part, node, parentW, parentH, em, rem);
        }
    }

    private static void ApplyFlexBasis(string? value, YogaNode node, float parentW, float parentH, float em, float rem)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var t = value.Trim();
        if (string.Equals(t, "auto", StringComparison.OrdinalIgnoreCase))
        {
            YGNodeStyleSetFlexBasisAuto(node);
            return;
        }
        if (TryParsePercent(t, out var pct))
        {
            YGNodeStyleSetFlexBasisPercent(node, pct);
            return;
        }
        if (string.Equals(t, "0", StringComparison.Ordinal))
        {
            YGNodeStyleSetFlexBasis(node, 0);
            return;
        }
        if (TryParsePoints(t, parentW, parentH, em, rem, out var pts))
            YGNodeStyleSetFlexBasis(node, pts);
    }

    private static void ApplyDirectionAndAspectRatio(Element element, YogaNode node)
    {
        var direction = element.Style.Get("direction")?.Trim();
        if (string.Equals(direction, "rtl", StringComparison.OrdinalIgnoreCase))
            YGNodeStyleSetDirection(node, YGDirection.RTL);
        else if (string.Equals(direction, "ltr", StringComparison.OrdinalIgnoreCase))
            YGNodeStyleSetDirection(node, YGDirection.LTR);

        var aspectRatio = element.Style.Get("aspect-ratio")?.Trim();
        if (!string.IsNullOrWhiteSpace(aspectRatio) && TryParseAspectRatio(aspectRatio, out var ratio))
            YGNodeStyleSetAspectRatio(node, ratio);
    }

    private static void ApplyBoxModel(Element element, YogaNode node, float parentW, float parentH, float em, float rem)
    {
        var padding = ResolvePadding(element, parentW, parentH, em, rem);
        var contentBox = ParseBoxSizing(element.Style.Get("box-sizing")) == BoxSizing.ContentBox;
        var horizontalPadding = contentBox ? padding.Left + padding.Right : 0;
        var verticalPadding = contentBox ? padding.Top + padding.Bottom : 0;

        ApplyDim(element.Style.Get("width"), parentW, parentH, em, rem, horizontalPadding,
            v => YGNodeStyleSetWidth(node, v), v => YGNodeStyleSetWidthPercent(node, v), () => YGNodeStyleSetWidthAuto(node));
        ApplyDim(element.Style.Get("height"), parentW, parentH, em, rem, verticalPadding,
            v => YGNodeStyleSetHeight(node, v), v => YGNodeStyleSetHeightPercent(node, v), () => YGNodeStyleSetHeightAuto(node));
        ApplyMinMax(element.Style.Get("min-width"), parentW, parentH, em, rem, horizontalPadding,
            v => YGNodeStyleSetMinWidth(node, v), v => YGNodeStyleSetMinWidthPercent(node, v));
        ApplyMinMax(element.Style.Get("min-height"), parentW, parentH, em, rem, verticalPadding,
            v => YGNodeStyleSetMinHeight(node, v), v => YGNodeStyleSetMinHeightPercent(node, v));
        ApplyMinMax(element.Style.Get("max-width"), parentW, parentH, em, rem, horizontalPadding,
            v => YGNodeStyleSetMaxWidth(node, v), v => YGNodeStyleSetMaxWidthPercent(node, v));
        ApplyMinMax(element.Style.Get("max-height"), parentW, parentH, em, rem, verticalPadding,
            v => YGNodeStyleSetMaxHeight(node, v), v => YGNodeStyleSetMaxHeightPercent(node, v));

        ApplyBoxShorthand(element.Style.Get("padding"), node, isPadding: true, parentW, parentH, em, rem);
        ApplyEdge(element.Style.Get("padding-left"), YGEdge.Left, true, node, parentW, parentH, em, rem);
        ApplyEdge(element.Style.Get("padding-top"), YGEdge.Top, true, node, parentW, parentH, em, rem);
        ApplyEdge(element.Style.Get("padding-right"), YGEdge.Right, true, node, parentW, parentH, em, rem);
        ApplyEdge(element.Style.Get("padding-bottom"), YGEdge.Bottom, true, node, parentW, parentH, em, rem);

        ApplyBoxShorthand(element.Style.Get("margin"), node, isPadding: false, parentW, parentH, em, rem);
        ApplyEdge(element.Style.Get("margin-left"), YGEdge.Left, false, node, parentW, parentH, em, rem);
        ApplyEdge(element.Style.Get("margin-top"), YGEdge.Top, false, node, parentW, parentH, em, rem);
        ApplyEdge(element.Style.Get("margin-right"), YGEdge.Right, false, node, parentW, parentH, em, rem);
        ApplyEdge(element.Style.Get("margin-bottom"), YGEdge.Bottom, false, node, parentW, parentH, em, rem);
    }

    private static void ApplyGap(Element element, YogaNode node, float parentW, float parentH, float em, float rem)
    {
        if (TryParsePoints(element.Style.Get("gap"), parentW, parentH, em, rem, out var gap))
            YGNodeStyleSetGap(node, YGGutter.All, gap);
        if (TryParsePoints(element.Style.Get("row-gap"), parentW, parentH, em, rem, out var rowGap))
            YGNodeStyleSetGap(node, YGGutter.Row, rowGap);
        if (TryParsePoints(element.Style.Get("column-gap"), parentW, parentH, em, rem, out var colGap))
            YGNodeStyleSetGap(node, YGGutter.Column, colGap);
    }

    private static void ApplyBorder(Element element, YogaNode node, float parentW, float parentH, float em, float rem)
    {
        if (TryParseBoxShorthand(element.Style.Get("border-width"), parentW, parentH, em, rem, allowAuto: false, out var border))
        {
            YGNodeStyleSetBorder(node, YGEdge.Top, border.Top);
            YGNodeStyleSetBorder(node, YGEdge.Right, border.Right);
            YGNodeStyleSetBorder(node, YGEdge.Bottom, border.Bottom);
            YGNodeStyleSetBorder(node, YGEdge.Left, border.Left);
        }
        ApplyBorderEdge(element.Style.Get("border-left-width"), YGEdge.Left, node, parentW, parentH, em, rem);
        ApplyBorderEdge(element.Style.Get("border-top-width"), YGEdge.Top, node, parentW, parentH, em, rem);
        ApplyBorderEdge(element.Style.Get("border-right-width"), YGEdge.Right, node, parentW, parentH, em, rem);
        ApplyBorderEdge(element.Style.Get("border-bottom-width"), YGEdge.Bottom, node, parentW, parentH, em, rem);
    }

    private static void ApplyBorderEdge(string? value, YGEdge edge, YogaNode node, float parentW, float parentH, float em, float rem)
    {
        if (TryParsePoints(value, parentW, parentH, em, rem, out var points))
            YGNodeStyleSetBorder(node, edge, points);
    }

    private static void ApplyPosition(Element element, YogaNode node, float parentW, float parentH, float em, float rem)
    {
        var pos = element.Style.Get("position")?.Trim();
        if (string.Equals(pos, "absolute", StringComparison.OrdinalIgnoreCase))
            YGNodeStyleSetPositionType(node, YGPositionType.Absolute);
        else if (string.Equals(pos, "relative", StringComparison.OrdinalIgnoreCase))
            YGNodeStyleSetPositionType(node, YGPositionType.Relative);

        ApplyInsetShorthand(element.Style.Get("inset"), node, parentW, parentH, em, rem);
        ApplyInset(element.Style.Get("inset-block-start") ?? element.Style.Get("top"), YGEdge.Top, node, parentW, parentH, em, rem);
        ApplyInset(element.Style.Get("inset-inline-end") ?? element.Style.Get("right"), YGEdge.Right, node, parentW, parentH, em, rem);
        ApplyInset(element.Style.Get("inset-block-end") ?? element.Style.Get("bottom"), YGEdge.Bottom, node, parentW, parentH, em, rem);
        ApplyInset(element.Style.Get("inset-inline-start") ?? element.Style.Get("left"), YGEdge.Left, node, parentW, parentH, em, rem);
    }

    private static void ApplyOverflow(Element element, YogaNode node)
    {
        var o = element.Style.Get("overflow")?.Trim();
        if (o is "hidden" or "clip") YGNodeStyleSetOverflow(node, YGOverflow.Hidden);
        else if (o is "scroll" or "auto") YGNodeStyleSetOverflow(node, YGOverflow.Scroll);
    }

    // ——— 原有 Grid 实现（Yoga Grid 暂不可靠）———

    private void MeasureGrid(Element element, ComputedStyle style, Size available)
    {
        var rowGap = style.RowGap;
        var columnGap = style.ColumnGap;
        var cols = ParseGridTemplate(style.GridTemplateColumns, available.Width, columnGap);
        var rows = ParseGridTemplate(style.GridTemplateRows, available.Height, rowGap);
        ApplyIntrinsicGridTracks(element, style.GridTemplateColumns, cols, isColumns: true);
        ApplyIntrinsicGridTracks(element, style.GridTemplateRows, rows, isColumns: false);
        RecomputeFlexibleGridTracks(style.GridTemplateColumns, cols, available.Width, columnGap);
        RecomputeFlexibleGridTracks(style.GridTemplateRows, rows, available.Height, rowGap);

        var colCount = Math.Max(1, cols.Length);
        var rowCount = Math.Max(1, rows.Length);
        var effectiveCols = new float[colCount];
        var effectiveRows = new float[rowCount];
        for (int i = 0; i < colCount; i++)
            effectiveCols[i] = cols.Length > i ? cols[i] : Math.Max(0, available.Width - columnGap * Math.Max(0, colCount - 1)) / colCount;
        for (int i = 0; i < rowCount; i++)
            effectiveRows[i] = rows.Length > i ? rows[i] : Math.Max(0, available.Height - rowGap * Math.Max(0, rowCount - 1)) / rowCount;

        var areas = ParseGridAreas(element.Style.Get("grid-template-areas"));
        var placements = ResolveGridPlacements(element, available.Width, available.Height, colCount, rowCount, areas);
        foreach (var (child, cs) in placements)
        {
            var col = Math.Min(Math.Max(0, cs.GridColumn - 1), colCount - 1);
            var row = Math.Min(Math.Max(0, cs.GridRow - 1), rowCount - 1);
            var colSpan = Math.Min(cs.GridColumnSpan, colCount - col);
            var rowSpan = Math.Min(cs.GridRowSpan, rowCount - row);
            var w = 0f; for (int i = 0; i < colSpan; i++) w += effectiveCols[col + i];
            w += columnGap * Math.Max(0, colSpan - 1);
            var h = 0f; for (int i = 0; i < rowSpan; i++) h += effectiveRows[row + i];
            h += rowGap * Math.Max(0, rowSpan - 1);
            Measure(child, new Size(w, h));
        }
    }

    private void ArrangeGrid(Element element, ComputedStyle style, Rect inner)
    {
        var rowGap = style.RowGap;
        var columnGap = style.ColumnGap;
        var cols = ParseGridTemplate(style.GridTemplateColumns, inner.Width, columnGap);
        var rows = ParseGridTemplate(style.GridTemplateRows, inner.Height, rowGap);
        ApplyIntrinsicGridTracks(element, style.GridTemplateColumns, cols, isColumns: true);
        ApplyIntrinsicGridTracks(element, style.GridTemplateRows, rows, isColumns: false);
        RecomputeFlexibleGridTracks(style.GridTemplateColumns, cols, inner.Width, columnGap);
        RecomputeFlexibleGridTracks(style.GridTemplateRows, rows, inner.Height, rowGap);

        var colCount = Math.Max(1, cols.Length);
        var rowCount = Math.Max(1, rows.Length);
        var colX = new float[colCount + 1];
        colX[0] = inner.Left;
        for (int i = 0; i < colCount; i++)
            colX[i + 1] = colX[i] + (cols.Length > i ? cols[i] : Math.Max(0, inner.Width - columnGap * Math.Max(0, colCount - 1)) / colCount) + columnGap;
        var rowY = new float[rowCount + 1];
        rowY[0] = inner.Top;
        for (int i = 0; i < rowCount; i++)
            rowY[i + 1] = rowY[i] + (rows.Length > i ? rows[i] : Math.Max(0, inner.Height - rowGap * Math.Max(0, rowCount - 1)) / rowCount) + rowGap;

        var areas = ParseGridAreas(element.Style.Get("grid-template-areas"));
        var placements = ResolveGridPlacements(element, inner.Width, inner.Height, colCount, rowCount, areas);
        foreach (var (child, cs) in placements)
        {
            var col = Math.Min(Math.Max(0, cs.GridColumn - 1), colCount - 1);
            var row = Math.Min(Math.Max(0, cs.GridRow - 1), rowCount - 1);
            var colEnd = Math.Min(col + cs.GridColumnSpan, colCount);
            var rowEnd = Math.Min(row + cs.GridRowSpan, rowCount);
            var x = colX[col];
            var y = rowY[row];
            var w = colX[colEnd] - colX[col] - columnGap;
            var h = rowY[rowEnd] - rowY[row] - rowGap;
            Arrange(child, new Rect(x, y, w, h));
        }
    }

    private static float[] ParseGridTemplate(string template, float available, float gap)
    {
        if (string.IsNullOrEmpty(template)) return [];
        var parts = SplitGridTemplate(template);
        var result = new float[parts.Length];
        var fixedSize = 0f;
        var frTotal = 0f;

        foreach (var raw in parts)
        {
            var p = raw.Trim();
            if (TryParseMinMaxLegacy(p, out var min, out var frMax))
            {
                fixedSize += min;
                frTotal += frMax;
            }
            else if (p.EndsWith("fr", StringComparison.OrdinalIgnoreCase))
                frTotal += float.TryParse(p[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var fr) ? fr : 1f;
            else if (p.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(p[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var px)) fixedSize += px;
            }
            else if (p.EndsWith('%'))
            {
                if (float.TryParse(p[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                    fixedSize += available * pct / 100f;
            }
        }

        var flexible = Math.Max(0, available - gap * Math.Max(0, parts.Length - 1) - fixedSize);
        for (var i = 0; i < parts.Length; i++)
        {
            var p = parts[i].Trim();
            if (TryParseMinMaxLegacy(p, out var min, out var frMax))
                result[i] = min + (frTotal > 0 ? flexible * frMax / frTotal : 0);
            else if (p.EndsWith("fr", StringComparison.OrdinalIgnoreCase))
            {
                var fr = float.TryParse(p[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 1f;
                result[i] = frTotal > 0 ? flexible * fr / frTotal : 0;
            }
            else if (p.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
                     float.TryParse(p[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var px))
                result[i] = px;
            else if (p.EndsWith('%') &&
                     float.TryParse(p[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                result[i] = available * pct / 100f;
            else if (p is "min-content" or "max-content" or "fit-content" or "auto")
                result[i] = 0;
            else if (float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var rawVal))
                result[i] = rawVal;
        }
        return result;
    }

    private static void ApplyIntrinsicGridTracks(Element element, string template, float[] tracks, bool isColumns)
    {
        if (tracks.Length == 0 || !template.Contains("content", StringComparison.OrdinalIgnoreCase)) return;
        var parts = SplitGridTemplate(template);
        for (var i = 0; i < Math.Min(parts.Length, tracks.Length); i++)
        {
            var keyword = parts[i].Trim();
            if (keyword is not ("min-content" or "max-content" or "fit-content")) continue;
            var trackIndex = i + 1;
            var size = 0f;
            foreach (var child in element.Children)
            {
                if (!child.IsVisible) continue;
                var childStyle = GetComputedStyle(child, float.NaN, float.NaN);
                var childTrack = isColumns ? childStyle.GridColumn : childStyle.GridRow;
                if (childTrack != trackIndex && childTrack != 1) continue;
                // 未显式放置的第一个子项落入 track 1
                if (child.Style.Get(isColumns ? "grid-column" : "grid-row") == null &&
                    string.IsNullOrEmpty(childStyle.GridArea) && trackIndex != 1)
                    continue;
                if (child.Style.Get(isColumns ? "grid-column" : "grid-row") == null &&
                    string.IsNullOrEmpty(childStyle.GridArea) && trackIndex == 1)
                {
                    // ok
                }
                else if (childTrack != trackIndex) continue;

                var measured = child.Measure(Size.Zero);
                size = Math.Max(size, isColumns ? measured.Width : measured.Height);
            }
            // 简化：对未指定 grid-column 的项，按文档序落入列
            if (size <= 0)
            {
                var idx = 0;
                foreach (var child in element.Children)
                {
                    if (!child.IsVisible) continue;
                    var cs = GetComputedStyle(child, float.NaN, float.NaN);
                    var explicitTrack = isColumns
                        ? child.Style.Get("grid-column") != null || !string.IsNullOrEmpty(cs.GridArea)
                        : child.Style.Get("grid-row") != null || !string.IsNullOrEmpty(cs.GridArea);
                    var track = explicitTrack
                        ? (isColumns ? cs.GridColumn : cs.GridRow)
                        : (isColumns ? idx % Math.Max(1, tracks.Length) + 1 : idx / Math.Max(1, tracks.Length) + 1);
                    if (!explicitTrack) idx++;
                    if (track != trackIndex) continue;
                    var measured = child.Measure(Size.Zero);
                    size = Math.Max(size, isColumns ? measured.Width : measured.Height);
                }
            }
            tracks[i] = size;
        }
    }

    private static void RecomputeFlexibleGridTracks(string template, float[] tracks, float available, float gap)
    {
        if (tracks.Length == 0) return;
        var parts = SplitGridTemplate(template);
        var frTotal = 0f;
        var fixedSize = 0f;
        for (var i = 0; i < Math.Min(parts.Length, tracks.Length); i++)
        {
            var part = parts[i].Trim();
            if (TryParseMinMaxLegacy(part, out var minMaxMin, out var minMaxFr))
            {
                fixedSize += minMaxMin;
                frTotal += minMaxFr;
            }
            else if (part.EndsWith("fr", StringComparison.OrdinalIgnoreCase))
                frTotal += float.TryParse(part[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var fr) ? fr : 1f;
            else
                fixedSize += tracks[i];
        }
        if (frTotal <= 0) return;

        var flexibleSpace = Math.Max(0, available - gap * Math.Max(0, tracks.Length - 1) - fixedSize);
        for (var i = 0; i < Math.Min(parts.Length, tracks.Length); i++)
        {
            var part = parts[i].Trim();
            if (TryParseMinMaxLegacy(part, out var min, out var minMaxFr))
            {
                tracks[i] = min + flexibleSpace * minMaxFr / frTotal;
                continue;
            }
            if (!part.EndsWith("fr", StringComparison.OrdinalIgnoreCase)) continue;
            var fr = float.TryParse(part[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 1f;
            tracks[i] = flexibleSpace * fr / frTotal;
        }
    }

    private static List<(Element Child, ComputedStyle Style)> ResolveGridPlacements(
        Element element, float width, float height, int colCount, int rowCount,
        Dictionary<string, (int col, int row, int colSpan, int rowSpan)> areas)
    {
        var placements = CollectVisibleChildrenList(element)
            .Select(child => (Child: child, Style: GetComputedStyle(child, width, height)))
            .ToList();
        var occupied = new bool[rowCount, colCount];

        foreach (var (child, style) in placements)
        {
            ApplyAreaPlacement(style, areas);
            if (child.Style.Get("grid-column") == null && child.Style.Get("grid-row") == null &&
                string.IsNullOrWhiteSpace(style.GridArea))
                continue;
            if (child.Style.Get("grid-column") == null || child.Style.Get("grid-row") == null)
                continue;
            MarkOccupied(occupied, style, colCount, rowCount);
        }

        foreach (var (child, style) in placements)
        {
            ApplyAreaPlacement(style, areas);
            var hasColumn = child.Style.Get("grid-column") != null ||
                            !string.IsNullOrWhiteSpace(style.GridArea) && areas.ContainsKey(style.GridArea.Trim());
            var hasRow = child.Style.Get("grid-row") != null ||
                         !string.IsNullOrWhiteSpace(style.GridArea) && areas.ContainsKey(style.GridArea.Trim());
            if (!hasColumn || !hasRow)
            {
                var (column, row) = FindAvailableCell(occupied, style, colCount, rowCount, hasColumn, hasRow);
                style.GridColumn = column + 1;
                style.GridRow = row + 1;
            }
            MarkOccupied(occupied, style, colCount, rowCount);
        }
        return placements;
    }

    private static void ApplyAreaPlacement(ComputedStyle style,
        Dictionary<string, (int col, int row, int colSpan, int rowSpan)> areas)
    {
        if (string.IsNullOrWhiteSpace(style.GridArea) || style.GridArea.Contains('/') ||
            !areas.TryGetValue(style.GridArea.Trim(), out var area)) return;
        style.GridColumn = area.col;
        style.GridRow = area.row;
        style.GridColumnSpan = area.colSpan;
        style.GridRowSpan = area.rowSpan;
    }

    private static (int Column, int Row) FindAvailableCell(bool[,] occupied, ComputedStyle style,
        int colCount, int rowCount, bool hasColumn, bool hasRow)
    {
        for (var row = 0; row < rowCount; row++)
        for (var column = 0; column < colCount; column++)
        {
            if (hasColumn && column != Math.Clamp(style.GridColumn - 1, 0, colCount - 1)) continue;
            if (hasRow && row != Math.Clamp(style.GridRow - 1, 0, rowCount - 1)) continue;
            if (Fits(occupied, column, row, style.GridColumnSpan, style.GridRowSpan))
                return (column, row);
        }
        return (hasColumn ? Math.Clamp(style.GridColumn - 1, 0, colCount - 1) : 0,
            hasRow ? Math.Clamp(style.GridRow - 1, 0, rowCount - 1) : 0);
    }

    private static bool Fits(bool[,] occupied, int column, int row, int columnSpan, int rowSpan)
    {
        var colEnd = Math.Min(occupied.GetLength(1), column + Math.Max(1, columnSpan));
        var rowEnd = Math.Min(occupied.GetLength(0), row + Math.Max(1, rowSpan));
        if (column + Math.Max(1, columnSpan) > occupied.GetLength(1) ||
            row + Math.Max(1, rowSpan) > occupied.GetLength(0)) return false;
        for (var y = row; y < rowEnd; y++)
        for (var x = column; x < colEnd; x++)
            if (occupied[y, x]) return false;
        return true;
    }

    private static void MarkOccupied(bool[,] occupied, ComputedStyle style, int colCount, int rowCount)
    {
        var column = Math.Clamp(style.GridColumn - 1, 0, colCount - 1);
        var row = Math.Clamp(style.GridRow - 1, 0, rowCount - 1);
        var colEnd = Math.Min(colCount, column + Math.Max(1, style.GridColumnSpan));
        var rowEnd = Math.Min(rowCount, row + Math.Max(1, style.GridRowSpan));
        for (var y = row; y < rowEnd; y++)
        for (var x = column; x < colEnd; x++)
            occupied[y, x] = true;
    }

    private static Dictionary<string, (int col, int row, int colSpan, int rowSpan)> ParseGridAreas(string? value)
    {
        var result = new Dictionary<string, (int col, int row, int colSpan, int rowSpan)>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(value)) return result;

        var rows = ParseGridAreaRows(value);
        for (var row = 0; row < rows.Length; row++)
        {
            var cells = rows[row].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var col = 0; col < cells.Length; col++)
            {
                var name = cells[col];
                if (name == ".") continue;
                if (!result.TryGetValue(name, out var area))
                    result[name] = (col + 1, row + 1, 1, 1);
                else
                    result[name] = (
                        Math.Min(area.col, col + 1),
                        Math.Min(area.row, row + 1),
                        Math.Max(area.colSpan, col - area.col + 2),
                        Math.Max(area.rowSpan, row - area.row + 2));
            }
        }
        return result;
    }

    private static string[] ParseGridAreaRows(string value)
    {
        var quoted = new List<string>();
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] is not ('\'' or '"')) continue;
            var quote = value[index++];
            var start = index;
            while (index < value.Length && value[index] != quote) index++;
            quoted.Add(value[start..Math.Min(index, value.Length)]);
        }
        return quoted.Count > 0
            ? quoted.ToArray()
            : value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static string[] SplitGridTemplate(string template)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (char.IsWhiteSpace(c) && depth == 0)
            {
                if (i > start) parts.Add(template[start..i]);
                start = i + 1;
            }
        }
        if (start < template.Length) parts.Add(template[start..]);
        return parts.Where(p => p.Length > 0).ToArray();
    }

    private static bool TryParseMinMaxLegacy(string part, out float minPts, out float maxFr)
    {
        minPts = 0;
        maxFr = 0;
        if (!part.StartsWith("minmax(", StringComparison.OrdinalIgnoreCase) || !part.EndsWith(')'))
            return false;
        var inner = part[7..^1];
        var comma = inner.IndexOf(',');
        if (comma < 0) return false;
        var minPart = inner[..comma].Trim();
        var maxPart = inner[(comma + 1)..].Trim();
        if (minPart.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            float.TryParse(minPart[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out minPts);
        if (maxPart.EndsWith("fr", StringComparison.OrdinalIgnoreCase))
            float.TryParse(maxPart[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out maxFr);
        return true;
    }

    // ——— CSS 解析工具 ———

    private static ComputedStyle GetComputedStyle(Element element, float parentWidth, float parentHeight)
    {
        var style = new ComputedStyle();
        style.Display = ParseDisplayMode(element.Style.Get("display"));
        style.BoxSizing = ParseBoxSizing(element.Style.Get("box-sizing"));

        style.FlexDirection = element.Style.Get("flex-direction")?.Trim() switch
        {
            "column" => FlexDirection.Column,
            "row-reverse" => FlexDirection.RowReverse,
            "column-reverse" => FlexDirection.ColumnReverse,
            _ => FlexDirection.Row
        };
        style.JustifyContent = element.Style.Get("justify-content")?.Trim() switch
        {
            "center" => JustifyContent.Center,
            "flex-end" => JustifyContent.FlexEnd,
            "space-between" => JustifyContent.SpaceBetween,
            "space-around" => JustifyContent.SpaceAround,
            _ => JustifyContent.FlexStart
        };
        style.AlignItems = element.Style.Get("align-items")?.Trim() switch
        {
            "center" => AlignItems.Center,
            "flex-start" => AlignItems.FlexStart,
            "flex-end" => AlignItems.FlexEnd,
            _ => AlignItems.Stretch
        };

        var emSize = GetFontSize(element);
        var remSize = GetRootFontSize(element);
        if (TryParsePoints(element.Style.Get("gap"), parentWidth, parentHeight, emSize, remSize, out var gapVal))
            style.Gap = gapVal;
        style.RowGap = style.Gap;
        style.ColumnGap = style.Gap;
        var gapParts = element.Style.Get("gap")?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (gapParts is { Length: 2 })
        {
            if (TryParsePoints(gapParts[0], parentWidth, parentHeight, emSize, remSize, out var rowGap)) style.RowGap = rowGap;
            if (TryParsePoints(gapParts[1], parentWidth, parentHeight, emSize, remSize, out var columnGap)) style.ColumnGap = columnGap;
        }
        if (TryParsePoints(element.Style.Get("row-gap"), parentWidth, parentHeight, emSize, remSize, out var rowGapValue))
            style.RowGap = rowGapValue;
        if (TryParsePoints(element.Style.Get("column-gap"), parentWidth, parentHeight, emSize, remSize, out var columnGapValue))
            style.ColumnGap = columnGapValue;
        if (TryParsePoints(element.Style.Get("padding"), parentWidth, parentHeight, emSize, remSize, out var paddingVal))
            style.Padding = paddingVal;
        if (TryParseBoxShorthand(element.Style.Get("padding"), parentWidth, parentHeight, emSize, remSize, allowAuto: false, out var padding))
        {
            style.PaddingTop = padding.Top;
            style.PaddingRight = padding.Right;
            style.PaddingBottom = padding.Bottom;
            style.PaddingLeft = padding.Left;
        }
        if (TryParsePoints(element.Style.Get("margin"), parentWidth, parentHeight, emSize, remSize, out var marginVal))
            style.Margin = marginVal;
        if (TryParseBoxShorthand(element.Style.Get("margin"), parentWidth, parentHeight, emSize, remSize, allowAuto: false, out var margin))
        {
            style.MarginTop = margin.Top;
            style.MarginRight = margin.Right;
            style.MarginBottom = margin.Bottom;
            style.MarginLeft = margin.Left;
        }
        if (TryParsePoints(element.Style.Get("width"), parentWidth, parentHeight, emSize, remSize, out var widthVal))
            style.Width = widthVal;
        if (TryParsePoints(element.Style.Get("height"), parentWidth, parentHeight, emSize, remSize, out var heightVal))
            style.Height = heightVal;

        var flexGrow = element.Style.Get("flex-grow");
        if (flexGrow != null && float.TryParse(flexGrow, NumberStyles.Float, CultureInfo.InvariantCulture, out var grow))
            style.FlexGrow = grow;
        var flexShrink = element.Style.Get("flex-shrink");
        if (flexShrink != null && float.TryParse(flexShrink, NumberStyles.Float, CultureInfo.InvariantCulture, out var shrink))
            style.FlexShrink = shrink;
        if (TryParsePoints(element.Style.Get("flex-basis"), parentWidth, parentHeight, emSize, remSize, out var basis))
            style.FlexBasis = basis;

        var gridCols = element.Style.Get("grid-template-columns");
        if (gridCols != null) style.GridTemplateColumns = gridCols;
        var gridRows = element.Style.Get("grid-template-rows");
        if (gridRows != null) style.GridTemplateRows = gridRows;

        var gridCol = element.Style.Get("grid-column");
        if (gridCol != null) ApplyGridPlacement(gridCol, v => style.GridColumn = v, v => style.GridColumnSpan = v);
        if (int.TryParse(element.Style.Get("grid-column-span"), out var gridColumnSpan))
            style.GridColumnSpan = Math.Max(1, gridColumnSpan);
        var gridRow = element.Style.Get("grid-row");
        if (gridRow != null) ApplyGridPlacement(gridRow, v => style.GridRow = v, v => style.GridRowSpan = v);
        if (int.TryParse(element.Style.Get("grid-row-span"), out var gridRowSpan))
            style.GridRowSpan = Math.Max(1, gridRowSpan);
        var gridArea = element.Style.Get("grid-area");
        if (gridArea != null) style.GridArea = gridArea;

        return style;
    }

    internal static DisplayMode ParseDisplayMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "flex" => DisplayMode.Flex,
        "grid" => DisplayMode.Grid,
        "none" => DisplayMode.None,
        "table" => DisplayMode.Table,
        "inline-table" => DisplayMode.InlineTable,
        "table-row-group" => DisplayMode.TableRowGroup,
        "table-header-group" => DisplayMode.TableHeaderGroup,
        "table-footer-group" => DisplayMode.TableFooterGroup,
        "table-row" => DisplayMode.TableRow,
        "table-cell" => DisplayMode.TableCell,
        "table-caption" => DisplayMode.TableCaption,
        _ => DisplayMode.Block
    };

    private static bool IsTableRoot(DisplayMode display) => display is DisplayMode.Table or DisplayMode.InlineTable;

    private static void ApplyGridPlacement(string value, Action<int> setStart, Action<int> setSpan)
    {
        var parts = value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && int.TryParse(parts[0], out var start)) setStart(start);
        if (parts.Length <= 1) return;
        var spanPart = parts[1];
        if (spanPart.StartsWith("span ", StringComparison.OrdinalIgnoreCase))
            spanPart = spanPart[5..].Trim();
        if (!int.TryParse(spanPart, out var endOrSpan)) return;
        if (parts[1].StartsWith("span ", StringComparison.OrdinalIgnoreCase))
            setSpan(Math.Max(1, endOrSpan));
        else if (parts.Length > 0 && int.TryParse(parts[0], out var startLine))
            setSpan(Math.Max(1, endOrSpan - startLine));
    }

    private static void ApplyDim(string? value, float parentW, float parentH, float em, float rem, float paddingAdjustment,
        Action<float> setPts, Action<float> setPercent, Action setAuto)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var t = value.Trim();
        if (string.Equals(t, "auto", StringComparison.OrdinalIgnoreCase)) { setAuto(); return; }
        if (paddingAdjustment == 0 && TryParsePercent(t, out var pct))
        { setPercent(pct); return; }
        if (TryParsePercent(t, out pct))
        { setPts(parentW * pct / 100f + paddingAdjustment); return; }
        if (TryParsePoints(t, parentW, parentH, em, rem, out var pts)) setPts(pts + paddingAdjustment);
    }

    private static void ApplyBoxShorthand(string? value, YogaNode node, bool isPadding,
        float parentW, float parentH, float em, float rem)
    {
        if (!TryParseBoxShorthand(value, parentW, parentH, em, rem, allowAuto: !isPadding, out var box))
            return;

        ApplyBoxEdge(node, YGEdge.Top, box.Top, box.TopAuto, isPadding);
        ApplyBoxEdge(node, YGEdge.Right, box.Right, box.RightAuto, isPadding);
        ApplyBoxEdge(node, YGEdge.Bottom, box.Bottom, box.BottomAuto, isPadding);
        ApplyBoxEdge(node, YGEdge.Left, box.Left, box.LeftAuto, isPadding);
    }

    private static void ApplyBoxEdge(YogaNode node, YGEdge edge, float value, bool auto, bool isPadding)
    {
        if (auto)
        {
            if (!isPadding) YGNodeStyleSetMarginAuto(node, edge);
            return;
        }

        if (isPadding) YGNodeStyleSetPadding(node, edge, value);
        else YGNodeStyleSetMargin(node, edge, value);
    }

    private static void ApplyMinMax(string? value, float parentW, float parentH, float em, float rem, float paddingAdjustment,
        Action<float> setPts, Action<float> setPercent)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var t = value.Trim();
        if (paddingAdjustment == 0 && TryParsePercent(t, out var pct))
        { setPercent(pct); return; }
        if (TryParsePercent(t, out pct))
        { setPts(parentW * pct / 100f + paddingAdjustment); return; }
        if (TryParsePoints(t, parentW, parentH, em, rem, out var pts)) setPts(pts + paddingAdjustment);
    }

    private static BoxSizing ParseBoxSizing(string? value) => value?.Trim() switch
    {
        "content-box" => BoxSizing.ContentBox,
        _ => BoxSizing.BorderBox
    };

    private static BoxEdges ResolvePadding(Element element, float parentW, float parentH, float em, float rem)
    {
        var result = new BoxEdges(0, 0, 0, 0, false, false, false, false);
        if (TryParseBoxShorthand(element.Style.Get("padding"), parentW, parentH, em, rem, allowAuto: false, out var shorthand))
            result = shorthand;
        ApplyResolvedPaddingEdge(element.Style.Get("padding-top"), parentW, parentH, em, rem, v => result = result with { Top = v });
        ApplyResolvedPaddingEdge(element.Style.Get("padding-right"), parentW, parentH, em, rem, v => result = result with { Right = v });
        ApplyResolvedPaddingEdge(element.Style.Get("padding-bottom"), parentW, parentH, em, rem, v => result = result with { Bottom = v });
        ApplyResolvedPaddingEdge(element.Style.Get("padding-left"), parentW, parentH, em, rem, v => result = result with { Left = v });
        return result;
    }

    private static void ApplyResolvedPaddingEdge(string? value, float parentW, float parentH, float em, float rem, Action<float> set)
    {
        if (TryParsePoints(value, parentW, parentH, em, rem, out var points))
            set(points);
    }

    private static void ApplyEdge(string? value, YGEdge edge, bool isPadding, YogaNode node,
        float parentW, float parentH, float em, float rem)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var t = value.Trim();
        if (string.Equals(t, "auto", StringComparison.OrdinalIgnoreCase) && !isPadding)
        {
            YGNodeStyleSetMarginAuto(node, edge);
            return;
        }
        if (TryParsePercent(t, out var pct))
        {
            if (isPadding) YGNodeStyleSetPaddingPercent(node, edge, pct);
            else YGNodeStyleSetMarginPercent(node, edge, pct);
            return;
        }
        if (!TryParsePoints(t, parentW, parentH, em, rem, out var pts)) return;
        if (isPadding) YGNodeStyleSetPadding(node, edge, pts);
        else YGNodeStyleSetMargin(node, edge, pts);
    }

    private static void ApplyInsetShorthand(string? value, YogaNode node, float parentW, float parentH, float em, float rem)
    {
        if (!TryParseInsetShorthand(value, out var parts)) return;
        ApplyInset(parts.Top, YGEdge.Top, node, parentW, parentH, em, rem);
        ApplyInset(parts.Right, YGEdge.Right, node, parentW, parentH, em, rem);
        ApplyInset(parts.Bottom, YGEdge.Bottom, node, parentW, parentH, em, rem);
        ApplyInset(parts.Left, YGEdge.Left, node, parentW, parentH, em, rem);
    }

    private static void ApplyInset(string? value, YGEdge edge, YogaNode node, float parentW, float parentH, float em, float rem)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var t = value.Trim();
        if (string.Equals(t, "auto", StringComparison.OrdinalIgnoreCase))
        {
            YGNodeStyleSetPositionAuto(node, edge);
            return;
        }
        if (t.EndsWith('%') && float.TryParse(t[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
        {
            YGNodeStyleSetPositionPercent(node, edge, pct);
            return;
        }
        if (TryParsePoints(t, parentW, parentH, em, rem, out var pts))
            YGNodeStyleSetPosition(node, edge, pts);
    }

    private static bool TryParsePoints(string? value, float parentW, float parentH, float em, float rem, out float result)
    {
        result = ParseLength(value, parentW, parentH, em, rem);
        return !float.IsNaN(result);
    }

    private static bool TryParsePercent(string? value, out float percent)
    {
        percent = 0;
        var text = value?.Trim();
        return !string.IsNullOrEmpty(text) &&
            text.EndsWith('%') &&
            float.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out percent);
    }

    private static bool TryParseAspectRatio(string value, out float ratio)
    {
        ratio = 0;
        var parts = value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 &&
            float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var width) &&
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var height) &&
            width > 0 && height > 0)
        {
            ratio = width / height;
            return true;
        }
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out ratio) && ratio > 0;
    }

    private static bool TryParseInsetShorthand(string? value, out InsetValues result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 4) return false;

        var top = parts[0];
        var right = parts.Length > 1 ? parts[1] : top;
        var bottom = parts.Length > 2 ? parts[2] : top;
        var left = parts.Length > 3 ? parts[3] : right;
        result = new InsetValues(top, right, bottom, left);
        return true;
    }

    private static bool TryParseBoxShorthand(string? value, float parentW, float parentH, float em, float rem,
        bool allowAuto, out BoxEdges result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 4) return false;

        Span<float> values = stackalloc float[4];
        Span<bool> autos = stackalloc bool[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (allowAuto && string.Equals(parts[i], "auto", StringComparison.OrdinalIgnoreCase))
            {
                autos[i] = true;
                continue;
            }

            if (!TryParsePoints(parts[i], parentW, parentH, em, rem, out values[i]))
                return false;
        }

        var top = 0;
        var right = parts.Length > 1 ? 1 : 0;
        var bottom = parts.Length > 2 ? 2 : 0;
        var left = parts.Length > 3 ? 3 : right;
        result = new BoxEdges(
            values[top], values[right], values[bottom], values[left],
            autos[top], autos[right], autos[bottom], autos[left]);
        return true;
    }

    private static float ParseLength(string? value, float parentW, float parentH, float em, float rem)
    {
        if (string.IsNullOrWhiteSpace(value)) return float.NaN;
        var text = value.Replace(" ", "", StringComparison.Ordinal).Trim();
        if (text.EndsWith("vw", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var vw) &&
            float.IsFinite(_viewportWidth))
            return _viewportWidth * vw / 100f;
        if (text.EndsWith("vh", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var vh) &&
            float.IsFinite(_viewportHeight))
            return _viewportHeight * vh / 100f;
        if (text.EndsWith("rp", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var rp) &&
            float.IsFinite(parentW))
            return parentW * rp / 100f;
        if (text.EndsWith("rem", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(text[..^3], NumberStyles.Float, CultureInfo.InvariantCulture, out var remV))
            return remV * rem;
        if (text.EndsWith("em", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var emV))
            return emV * em;
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var px))
            return px;
        if (text.EndsWith('%') && float.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct) &&
            !float.IsNaN(parentW))
            return parentW * pct / 100f;
        if (text is "auto" or "min-content" or "max-content" or "fit-content")
            return float.NaN;
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var raw))
            return raw;
        return float.NaN;
    }

    private static float ResolveRefSize(string? css, float parentW, float parentH, float em, float rem, float fallback)
    {
        var v = ParseLength(css, parentW, parentH, em, rem);
        return float.IsNaN(v) ? fallback : v;
    }

    private static Rect Inset(Rect rect, float left, float top, float right, float bottom)
    {
        var width = Math.Max(0, rect.Width - left - right);
        var height = Math.Max(0, rect.Height - top - bottom);
        return new Rect(rect.X + left, rect.Y + top, width, height);
    }

    private static float GetFontSize(Element element)
    {
        var value = element.Style.Get("font-size");
        if (value != null)
        {
            var parsed = ParseLength(value, float.NaN, float.NaN, 16f, 16f);
            if (!float.IsNaN(parsed)) return parsed;
        }
        return 16f;
    }

    private static float GetRootFontSize(Element element)
    {
        var root = element;
        while (root.Parent != null) root = root.Parent;
        return GetFontSize(root);
    }

    private static float Sanitize(float v) =>
        float.IsNaN(v) || float.IsInfinity(v) ? 0 : Math.Max(0, v);

    private static void ClearDirtyRecursive(Element element)
    {
        element.ClearLayoutDirty();
        foreach (var child in element.Children)
            ClearDirtyRecursive(child);
    }

    private static int CollectVisibleChildren(Element element, Span<Element> destination)
    {
        var count = 0;
        foreach (var child in element.Children)
        {
            if (child.IsVisible)
                destination[count++] = child;
        }
        return count;
    }

    private static (Element[] Array, int Count) RentVisibleChildren(Element element)
    {
        var total = 0;
        foreach (var _ in element.Children) total++;
        var array = ArrayPool<Element>.Shared.Rent(Math.Max(1, total));
        var count = 0;
        foreach (var child in element.Children)
        {
            if (child.IsVisible)
                array[count++] = child;
        }
        return (array, count);
    }

    private static void ReturnVisibleChildren(Element[] array) =>
        ArrayPool<Element>.Shared.Return(array);

    private static List<Element> CollectVisibleChildrenList(Element element)
    {
        var result = new List<Element>();
        foreach (var child in element.Children)
        {
            if (child.IsVisible)
                result.Add(child);
        }
        return result;
    }

    private sealed class YogaSession : IDisposable
    {
        public YogaNode Root { get; set; } = null!;
        public Dictionary<Element, YogaNode> Map { get; } = new();
        public void Dispose()
        {
            if (Root != null)
                YGNodeFreeRecursive(Root);
        }
    }

    private readonly record struct BoxEdges(
        float Top,
        float Right,
        float Bottom,
        float Left,
        bool TopAuto,
        bool RightAuto,
        bool BottomAuto,
        bool LeftAuto);

    private readonly record struct InsetValues(string Top, string Right, string Bottom, string Left);
}

internal readonly record struct LayoutBoxModel(
    Rect Content,
    Rect Padding,
    Rect Border,
    Rect Margin);
