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
    private HeaderHost? _headerHost;
    private Element? _appliedHeader;
    private bool _headerSlotRendered;
    private float? _fixedRowHeight;

    /// <summary>
    /// 自定义行头；为空时绘制 TextContent/LeadingIcon。显式设置的行头优先于 header 插槽。
    /// 行头与子项独立布局，替换或清空会立即更新元素树。
    /// </summary>
    public Element? Header
    {
        get => GetProperty<Element>(nameof(Header));
        set => SetProperty(nameof(Header), value);
    }

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
    public string SelectableText => Header == null ? TextContent : "";

    /// <inheritdoc/>
    public Rect SelectableTextBounds => Header == null
        ? ControlDrawing.GetTextBounds(this, TextContent, FontSize,
            new Point(Geometry.X + GetTextOffset(), Geometry.Y + GetTextTop()))
        : Rect.Empty;

    /// <inheritdoc/>
    public override Size Measure(Size availableSize)
    {
        // Custom headers are measured by their flex subtree, rather than the fallback label.
        if (Header != null) return new Size(TextOffset, _fixedRowHeight ?? RowHeight);
        var text = ControlDrawing.MeasureText(this, TextContent, FontSize, availableSize);
        return new Size(text.Width + GetTextOffset() + 4, _fixedRowHeight ?? RowHeight);
    }

    /// <inheritdoc/>
    public override void BuildElementTree()
    {
        base.BuildElementTree();
        if (_headerSlotRendered || Header != null || !Slots.Contains("header")) return;
        _headerSlotRendered = true;
        var content = new HeaderHost();
        content.Style.SetCascaded("display", "flex", int.MinValue);
        content.Style.SetCascaded("flex-direction", "row", int.MinValue);
        content.Style.SetCascaded("align-items", "center", int.MinValue);
        content.Style.SetCascaded("min-width", "0", int.MinValue);
        Header = content;
        Slots.Render("header", content);
    }

    /// <inheritdoc/>
    public override void Paint(IRenderContext ctx)
    {
        var row = new Rect(Geometry.X, Geometry.Y, Geometry.Width, Math.Min(GetRowHeight(), Geometry.Height));
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

        if (Header != null) return;

        if (!string.IsNullOrEmpty(LeadingIcon))
        {
            var iconColor = LeadingIconColor.A == 0 ? foreground : LeadingIconColor;
            var iconFont = new Font(LeadingIconFontFamily, FontSize + 1);
            ctx.DrawText(
                new TextLayout(LeadingIcon, iconFont),
                new Point(row.X + TextOffset, row.Y + GetTextTop() - 1),
                new SolidColorBrush(iconColor));
        }

        if (!string.IsNullOrEmpty(TextContent))
            ControlDrawing.DrawText(ctx, this, TextContent, new Point(row.X + GetTextOffset(), row.Y + GetTextTop()), foreground, FontSize);
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
        if (name == nameof(Header)) UpdateHeader();
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

    private void UpdateHeader()
    {
        var candidate = Header;
        if (ReferenceEquals(candidate, _appliedHeader)) return;

        // BindProperty/SetProperty write the property bag before reaching this hook, so all
        // entry points validate here and restore the applied value before reporting an error.
        string? error = candidate?.ParentNode != null ? "The header already has a parent." : null;
        for (Element? current = this; current != null; current = current.Parent)
            if (ReferenceEquals(current, candidate))
                error = "The header cannot be the item or one of its ancestors.";
        if (error != null)
        {
            Properties.SetValue(nameof(Header), _appliedHeader);
            throw new InvalidOperationException(error);
        }

        if (_headerHost != null)
        {
            _headerHost.Children.Clear();
            if (candidate == null)
            {
                Children.Remove(_headerHost);
                _headerHost = null;
            }
        }
        if (candidate != null)
        {
            if (_headerHost == null)
            {
                _headerHost = new HeaderHost(this);
                _headerHost.ClassList.Add("tree-item-header");
                _headerHost.Style.SetCascaded("display", "flex", int.MinValue);
                _headerHost.Style.SetCascaded("flex-direction", "row", int.MinValue);
                _headerHost.Style.SetCascaded("align-items", "center", int.MinValue);
                _headerHost.Style.SetCascaded("padding-left", $"{TextOffset}px", int.MinValue);
                _headerHost.Style.SetCascaded("flex-shrink", "0", int.MinValue);
                ApplyHeaderHeight();
                Children.Insert(0, _headerHost);
            }
            _headerHost.Children.Add(candidate);
        }
        _appliedHeader = candidate;
        UpdateHeaderPadding();
        InvalidateLayout();
    }

    internal void SetFixedRowHeight(float height)
    {
        _fixedRowHeight = height;
        ApplyHeaderHeight();
        InvalidateLayout();
    }

    private void ApplyHeaderHeight()
    {
        if (_headerHost == null) return;
        var height = (_fixedRowHeight ?? RowHeight).ToString(System.Globalization.CultureInfo.InvariantCulture) + "px";
        _headerHost.Style.Set("min-height", height);
        if (_fixedRowHeight != null)
        {
            _headerHost.Style.Set("height", height);
            _headerHost.Style.Set("max-height", height);
            _headerHost.Style.Set("overflow", "hidden");
        }
    }

    // Keep embedded controls' pointer and keyboard behavior independent from the tree row.
    internal bool IsHeaderInteraction(Element target)
    {
        if (_headerHost == null) return false;
        var interactive = false;
        for (Element? current = target; current != null && current != this; current = current.Parent)
        {
            if (current is Button or CheckBox or TriStateCheckBox or Radio or Select or ITextEditor or Link or
                List or Swiper or ScrollViewer or Splitter ||
                current is Square.Html.HTMLElement { LocalName: "button" or "input" or "textarea" or "select" or "a" or "summary" })
                interactive = true;
            if (ReferenceEquals(current, _headerHost)) return interactive;
        }
        return false;
    }

    private void UpdateHeaderPadding() =>
        Style.SetCascaded("padding-top", Header == null && IsExpanded && Items.Count > 0 ? $"{RowHeight}px" : "0px", int.MinValue);

    private float GetRowHeight() => _fixedRowHeight ??
        (_headerHost != null && _headerHost.Geometry.Height > 0 ? _headerHost.Geometry.Height : RowHeight);

    private float GetTextTop() => 5 + (GetRowHeight() - RowHeight) / 2f;

    private float GetTextOffset() => string.IsNullOrEmpty(LeadingIcon) ? TextOffset : IconTextOffset;

    private sealed class HeaderHost(TreeItem? owner = null) : View
    {
        // A container's natural size comes from its children, never View.Measure's available size.
        public override bool HasCustomMeasure => false;
        public override Size Measure(Size availableSize) => Size.Zero;

        public override void Arrange(Rect finalRect)
        {
            var previousHeight = Geometry.Height;
            base.Arrange(finalRect);
            // The owner's total height may be constrained while its header row still changes.
            if (previousHeight != finalRect.Height) owner?.InvalidatePaint();
        }
    }
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
            if (e.DefaultPrevented || e.Target is Element target && FindItemAncestor(target)?.IsHeaderInteraction(target) == true) return;
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
        if (item == null || item.IsHeaderInteraction(target)) return;
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
        Element? current = item;
        while (current is TreeItem) current = current.Parent;
        return ReferenceEquals(current, this);
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
