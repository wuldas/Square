using Square.Events;
using Square.Graphics;
using Square.UI;

namespace Square.Controls;

/// <summary>A hierarchical item with an expandable child collection.</summary>
public class TreeItem : UIElement, ITextSelectable
{
    private const float RowHeight = 28f;
    private const float TextOffset = 20f;
    private const float IconTextOffset = 40f;

    /// <summary>初始化 <see cref="TreeItem"/> 的新实例。</summary>
    public TreeItem()
    {
        Style.SetCascaded("display", "flex", int.MinValue);
        Style.SetCascaded("flex-direction", "column", int.MinValue);
    }

    /// <summary>初始化 <see cref="TreeItem"/> 的新实例并设置文本。</summary>
    public TreeItem(string text) : this() => TextContent = text;

    /// <summary>文本内容。</summary>
    public string TextContent
    {
        get => GetProperty<string>(nameof(TextContent)) ?? "";
        set => SetProperty(nameof(TextContent), value);
    }

    /// <summary>文字颜色。</summary>
    public Color Color
    {
        get => Properties.HasValue(nameof(Color)) ? GetProperty<Color>(nameof(Color)) : Color.Black;
        set => SetProperty(nameof(Color), value);
    }

    /// <summary>字号（像素）。</summary>
    public float FontSize
    {
        get => Properties.HasValue(nameof(FontSize)) ? GetProperty<float>(nameof(FontSize)) : 14f;
        set => SetProperty(nameof(FontSize), value);
    }

    /// <summary>显示在文本前的可选字体图标字形。</summary>
    public string LeadingIcon
    {
        get => GetProperty<string>(nameof(LeadingIcon)) ?? "";
        set => SetProperty(nameof(LeadingIcon), value ?? "");
    }

    /// <summary>前导图标使用的字体族。</summary>
    public string LeadingIconFontFamily
    {
        get => GetProperty<string>(nameof(LeadingIconFontFamily)) ?? "Segoe Fluent Icons";
        set => SetProperty(nameof(LeadingIconFontFamily), value ?? "");
    }

    /// <summary>前导图标颜色；透明时跟随文本颜色。</summary>
    public Color LeadingIconColor
    {
        get => Properties.HasValue(nameof(LeadingIconColor)) ? GetProperty<Color>(nameof(LeadingIconColor)) : Color.Transparent;
        set => SetProperty(nameof(LeadingIconColor), value);
    }

    /// <summary>是否展开子项。</summary>
    public bool IsExpanded
    {
        get => Properties.HasValue(nameof(IsExpanded)) && GetProperty<bool>(nameof(IsExpanded));
        set => SetProperty(nameof(IsExpanded), value);
    }

    /// <summary>是否处于选中状态。</summary>
    public bool IsSelected
    {
        get => GetProperty<bool>(nameof(IsSelected));
        set => SetProperty(nameof(IsSelected), value);
    }

    /// <summary>子项集合。</summary>
    public IReadOnlyList<TreeItem> Items => Children.OfType<TreeItem>().ToArray();
    /// <summary>是否包含子项。</summary>
    public bool HasItems => HasVirtualItems || Items.Count > 0;
    internal bool HasVirtualItems { get; set; }
    /// <inheritdoc/>
    public string SelectableText => TextContent;

    /// <inheritdoc/>
    public Rect SelectableTextBounds => ControlDrawing.GetTextBounds(
        this, TextContent, FontSize, new Point(Geometry.X + GetTextOffset(), Geometry.Y + 5));

    /// <inheritdoc/>
    public override Size Measure(Size availableSize)
    {
        var text = ControlDrawing.MeasureText(this, TextContent, FontSize, availableSize);
        return new Size(text.Width + GetTextOffset() + 4, RowHeight);
    }

    /// <inheritdoc/>
    public override void Paint(IRenderContext ctx)
    {
        var row = new Rect(Geometry.X, Geometry.Y, Geometry.Width, Math.Min(RowHeight, Geometry.Height));
        var background = ControlDrawing.GetStyledColor(this, "background",
            IsSelected ? Color.FromRgb(0, 120, 212) : Color.Transparent);
        if (background.A > 0) ctx.FillRect(row, new SolidColorBrush(background));

        var foreground = IsSelected
            ? ControlDrawing.GetStyledColor(this, "color", Color.White)
            : ControlDrawing.GetStyledColor(this, "color", Color);
        if (HasItems)
        {
            var centerY = row.Y + row.Height / 2f;
            var marker = IsExpanded
                ? PathGeometry.Create()
                    .MoveTo(new Point(row.X + 6, centerY - 2))
                    .LineTo(new Point(row.X + 10, centerY + 2))
                    .LineTo(new Point(row.X + 14, centerY - 2))
                : PathGeometry.Create()
                    .MoveTo(new Point(row.X + 8, centerY - 4))
                    .LineTo(new Point(row.X + 12, centerY))
                    .LineTo(new Point(row.X + 8, centerY + 4));
            ctx.DrawPath(marker, Pen.FromColor(foreground, 1.5f));
        }

        if (!string.IsNullOrEmpty(LeadingIcon))
        {
            var iconColor = LeadingIconColor.A == 0 ? foreground : LeadingIconColor;
            var iconFont = new Font(LeadingIconFontFamily, FontSize + 1);
            ctx.DrawText(
                new TextLayout(LeadingIcon, iconFont),
                new Point(row.X + TextOffset, row.Y + 4),
                new SolidColorBrush(iconColor));
        }

        if (!string.IsNullOrEmpty(TextContent))
            ControlDrawing.DrawText(ctx, this, TextContent, new Point(row.X + GetTextOffset(), row.Y + 5), foreground, FontSize);
    }

    /// <summary>展开当前节点，返回是否实际发生展开。</summary>
    public bool Expand()
    {
        if (!HasItems || IsExpanded) return false;
        IsExpanded = true;
        DispatchEvent(new Event("expand", new EventInit { Bubbles = true }));
        return true;
    }

    /// <summary>折叠当前节点，返回是否实际发生折叠。</summary>
    public bool Collapse()
    {
        if (!HasItems || !IsExpanded) return false;
        IsExpanded = false;
        DispatchEvent(new Event("collapse", new EventInit { Bubbles = true }));
        return true;
    }

    /// <summary>切换展开/折叠状态。</summary>
    public bool Toggle() => IsExpanded ? Collapse() : Expand();

    /// <inheritdoc/>
    protected override void OnPropertyChanged(string name)
    {
        base.OnPropertyChanged(name);
        if (name == nameof(IsSelected)) SetState(ElementState.Checked, IsSelected);
        if (name == nameof(IsExpanded)) UpdateChildLayout();
    }

    internal override void OnChildAdded(Element child)
    {
        base.OnChildAdded(child);
        if (child is not TreeItem item) return;
        item.Style.SetCascaded("margin-left", "18px", int.MinValue);
        item.IsVisible = IsExpanded;
        UpdateHeaderPadding();
    }

    internal override void OnChildRemoved(Element child)
    {
        base.OnChildRemoved(child);
        if (child is TreeItem && !HasItems)
        {
            if (IsExpanded) IsExpanded = false;
            UpdateHeaderPadding();
        }
    }

    private void UpdateChildLayout()
    {
        foreach (var child in Items) child.IsVisible = IsExpanded;
        UpdateHeaderPadding();
        InvalidateLayout();
    }

    private void UpdateHeaderPadding() =>
        Style.SetCascaded("padding-top", IsExpanded && Items.Count > 0 ? $"{RowHeight}px" : "0px", int.MinValue);

    private float GetTextOffset() => string.IsNullOrEmpty(LeadingIcon) ? TextOffset : IconTextOffset;
}

/// <summary>Scrollable single-selection hierarchical tree.</summary>
public class Tree : ScrollViewer
{
    private TreeItem? _activeItem;

    /// <summary>初始化 <see cref="Tree"/> 的新实例。</summary>
    public Tree()
    {
        Style.SetCascaded("display", "flex", int.MinValue);
        Style.SetCascaded("flex-direction", "column", int.MinValue);
        AddEventListener(StandardEvents.Click, OnItemClick);
        AddEventListener<KeyboardEvent>(StandardEvents.KeyDown, e =>
        {
            if (!HandleKey(e.KeyCode)) return;
            e.PreventDefault();
        });
    }

    /// <summary>当前选中的节点。</summary>
    public TreeItem? SelectedItem => GetAllItems().FirstOrDefault(item => item.IsSelected);

    /// <summary>选中指定节点，返回是否实际改变了选择。</summary>
    public bool SelectItem(TreeItem? item)
    {
        if (item == null || !item.IsEnabled || !item.IsVisible || !ContainsItem(item)) return false;
        var previous = SelectedItem;
        if (ReferenceEquals(previous, item))
        {
            _activeItem = item;
            return false;
        }

        if (previous != null) previous.IsSelected = false;
        item.IsSelected = true;
        _activeItem = item;
        DispatchEvent(StandardEvents.CreateSelectionChange());
        DispatchEvent(StandardEvents.CreateChange());
        return true;
    }

    /// <summary>清除选择。</summary>
    public void ClearSelection()
    {
        var selected = SelectedItem;
        if (selected == null) return;
        selected.IsSelected = false;
        _activeItem = null;
        DispatchEvent(StandardEvents.CreateSelectionChange());
        DispatchEvent(StandardEvents.CreateChange());
    }

    /// <summary>处理键盘导航，返回是否已处理。</summary>
    public bool HandleKey(int keyCode)
    {
        if (!IsEnabled) return false;
        var visible = GetVisibleItems();
        if (visible.Count == 0) return false;
        var current = _activeItem != null && visible.Contains(_activeItem)
            ? _activeItem
            : SelectedItem;
        var index = current == null ? -1 : visible.IndexOf(current);

        switch (keyCode)
        {
            case 38:
                return SelectItem(visible[Math.Max(0, index < 0 ? visible.Count - 1 : index - 1)]);
            case 40:
                return SelectItem(visible[Math.Min(visible.Count - 1, index + 1)]);
            case 36:
                return SelectItem(visible[0]);
            case 35:
                return SelectItem(visible[^1]);
            case 39 when current != null:
                if (current.Expand()) return true;
                return current.Items.Count > 0 && SelectItem(current.Items[0]);
            case 37 when current != null:
                if (current.Collapse()) return true;
                return SelectItem(GetParentItem(current));
            case 13 or 32 when current != null:
                return current.Toggle();
            default:
                return false;
        }
    }

    internal override void OnChildRemoved(Element child)
    {
        base.OnChildRemoved(child);
        if (_activeItem == null || ContainsItem(_activeItem)) return;
        _activeItem = null;
        if (child is TreeItem item && ContainsSelectedItem(item))
        {
            DispatchEvent(StandardEvents.CreateSelectionChange());
            DispatchEvent(StandardEvents.CreateChange());
        }
    }

    private void OnItemClick(Event e)
    {
        if (!IsEnabled || e.Target is not Element target) return;
        var item = FindItemAncestor(target);
        if (item == null) return;
        SelectItem(item);
        if (item.HasItems) item.Toggle();
        Focus();
    }

    private TreeItem? FindItemAncestor(Element target)
    {
        for (Element? current = target; current != null && current != this; current = current.Parent)
            if (current is TreeItem item && ContainsItem(item))
                return item;
        return null;
    }

    private bool ContainsItem(TreeItem item)
    {
        for (Element? current = item; current != null; current = current.Parent)
            if (ReferenceEquals(current, this))
                return true;
        return false;
    }

    private static bool ContainsSelectedItem(TreeItem item) =>
        item.IsSelected || item.Items.Any(ContainsSelectedItem);

    private static TreeItem? GetParentItem(TreeItem item) => item.Parent as TreeItem;

    private System.Collections.Generic.List<TreeItem> GetAllItems()
    {
        var result = new System.Collections.Generic.List<TreeItem>();
        foreach (var item in Children.OfType<TreeItem>()) AddItems(item, result, visibleOnly: false);
        return result;
    }

    private System.Collections.Generic.List<TreeItem> GetVisibleItems()
    {
        var result = new System.Collections.Generic.List<TreeItem>();
        foreach (var item in Children.OfType<TreeItem>()) AddItems(item, result, visibleOnly: true);
        return result;
    }

    private static void AddItems(TreeItem item, ICollection<TreeItem> result, bool visibleOnly)
    {
        if (visibleOnly && !item.IsVisible) return;
        result.Add(item);
        if (visibleOnly && !item.IsExpanded) return;
        foreach (var child in item.Items) AddItems(child, result, visibleOnly);
    }
}
