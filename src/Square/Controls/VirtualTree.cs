using System.Collections.Specialized;
using Square.Events;
using Square.Graphics;
using Square.UI;

namespace Square.Controls;

/// <summary>Fixed-row-height virtualized tree backed by a flattened logical hierarchy.</summary>
public sealed class VirtualTree : ScrollViewer, ILayoutPreparingElement
{
    private readonly View _topSpacer = new();
    private readonly View _bottomSpacer = new();
    private readonly List<VirtualTreeEntry> _visibleEntries = [];
    private readonly HashSet<object> _expandedKeys = [];
    private readonly Dictionary<TreeItem, int> _realizedIndexes = [];
    private Func<int, object?>? _getRoot;
    private Func<int>? _getRootCount;
    private Func<object, IReadOnlyList<object?>>? _getChildren;
    private Func<object, object>? _getKey;
    private Func<object, int, TreeItem>? _itemTemplate;
    private INotifyCollectionChanged? _observableRoots;
    private bool _sourceSubscribed;
    private object? _selectedKey;
    private int _activeIndex = -1;
    private VirtualizingStackRange _realizedRange = VirtualizingStackRange.Empty;
    private bool _realizing;

    public VirtualTree()
    {
        Style.SetCascaded("display", "flex", int.MinValue);
        Style.SetCascaded("flex-direction", "column", int.MinValue);
        ConfigureSpacer(_topSpacer);
        ConfigureSpacer(_bottomSpacer);
        AddEventListener(StandardEvents.Scroll, () => InvalidateLayout());
        AddEventListener(StandardEvents.Click, OnItemClick);
        AddEventListener<KeyboardEvent>(StandardEvents.KeyDown, e =>
        {
            if (!HandleKey(e.KeyCode)) return;
            e.PreventDefault();
        });
    }

    public float ItemHeight
    {
        get => Properties.HasValue(nameof(ItemHeight)) ? GetProperty<float>(nameof(ItemHeight)) : 28f;
        set => SetProperty(nameof(ItemHeight), value);
    }

    public int OverscanCount
    {
        get => Properties.HasValue(nameof(OverscanCount)) ? GetProperty<int>(nameof(OverscanCount)) : 3;
        set => SetProperty(nameof(OverscanCount), Math.Max(0, value));
    }

    public float IndentSize
    {
        get => Properties.HasValue(nameof(IndentSize)) ? GetProperty<float>(nameof(IndentSize)) : 18f;
        set => SetProperty(nameof(IndentSize), Math.Max(0, value));
    }

    public int VisibleItemCount => _visibleEntries.Count;
    public int RealizedItemCount => _realizedIndexes.Count;
    public int FirstRealizedIndex => _realizedRange.Count == 0 ? -1 : _realizedRange.FirstIndex;
    public int LastRealizedIndex => _realizedRange.Count == 0 ? -1 : _realizedRange.LastIndex;
    public TreeItem? SelectedItem => GetRealizedItem(SelectedIndex);
    public object? SelectedValue => SelectedIndex >= 0 ? _visibleEntries[SelectedIndex].Item : null;
    public int SelectedIndex => FindIndexByKey(_selectedKey);

    public void SetItemsSource<T>(
        IReadOnlyList<T>? roots,
        Func<T, IReadOnlyList<T>?> childSelector,
        Func<T, int, TreeItem>? itemTemplate = null)
        where T : notnull =>
        SetItemsSource(roots, childSelector, static item => item, itemTemplate);

    public void SetItemsSource<T, TKey>(
        IReadOnlyList<T>? roots,
        Func<T, IReadOnlyList<T>?> childSelector,
        Func<T, TKey> keySelector,
        Func<T, int, TreeItem>? itemTemplate = null)
        where T : notnull
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(childSelector);
        ArgumentNullException.ThrowIfNull(keySelector);
        UnsubscribeSource();
        if (roots == null)
        {
            _getRoot = null;
            _getRootCount = null;
            _getChildren = null;
            _getKey = null;
            _itemTemplate = null;
            _observableRoots = null;
        }
        else
        {
            _getRoot = index => roots[index];
            _getRootCount = () => roots.Count;
            _getChildren = item => childSelector((T)item)?.Cast<object?>().ToArray() ?? [];
            _getKey = item => keySelector((T)item)!;
            _itemTemplate = itemTemplate == null
                ? static (item, _) => new TreeItem(item.ToString() ?? "")
                : (item, depth) => itemTemplate((T)item, depth);
            _observableRoots = roots as INotifyCollectionChanged;
        }

        _expandedKeys.Clear();
        _selectedKey = null;
        _activeIndex = -1;
        _realizedRange = VirtualizingStackRange.Empty;
        RebuildVisibleEntries();
        SubscribeSource();
    }

    public bool SelectIndex(int index)
    {
        if (index < 0 || index >= _visibleEntries.Count) return false;
        var key = _visibleEntries[index].Key;
        if (Equals(_selectedKey, key))
        {
            _activeIndex = index;
            return false;
        }
        _selectedKey = key;
        _activeIndex = index;
        ApplySelectionToRealizedItems();
        DispatchEvent(StandardEvents.CreateSelectionChange());
        DispatchEvent(StandardEvents.CreateChange());
        return true;
    }

    public void ClearSelection()
    {
        if (_selectedKey == null) return;
        _selectedKey = null;
        _activeIndex = -1;
        ApplySelectionToRealizedItems();
        DispatchEvent(StandardEvents.CreateSelectionChange());
        DispatchEvent(StandardEvents.CreateChange());
    }

    public bool ExpandIndex(int index) => SetExpanded(index, true);
    public bool CollapseIndex(int index) => SetExpanded(index, false);
    public bool ToggleIndex(int index) => index >= 0 && index < _visibleEntries.Count &&
        SetExpanded(index, !_expandedKeys.Contains(_visibleEntries[index].Key));

    public bool HandleKey(int keyCode)
    {
        if (!IsEnabled || _visibleEntries.Count == 0) return false;
        var index = _activeIndex >= 0 && _activeIndex < _visibleEntries.Count
            ? _activeIndex
            : SelectedIndex;
        switch (keyCode)
        {
            case 38:
                return SelectAndReveal(Math.Max(0, index < 0 ? _visibleEntries.Count - 1 : index - 1));
            case 40:
                return SelectAndReveal(Math.Min(_visibleEntries.Count - 1, index + 1));
            case 36:
                return SelectAndReveal(0);
            case 35:
                return SelectAndReveal(_visibleEntries.Count - 1);
            case 39 when index >= 0:
                if (ExpandIndex(index)) return true;
                return index + 1 < _visibleEntries.Count && _visibleEntries[index + 1].ParentKey != null &&
                    Equals(_visibleEntries[index + 1].ParentKey, _visibleEntries[index].Key) && SelectAndReveal(index + 1);
            case 37 when index >= 0:
                if (CollapseIndex(index)) return true;
                var parentIndex = FindIndexByKey(_visibleEntries[index].ParentKey);
                return parentIndex >= 0 && SelectAndReveal(parentIndex);
            case 13 or 32 when index >= 0:
                return ToggleIndex(index);
            default:
                return false;
        }
    }

    public void ScrollIntoView(int index)
    {
        if (index < 0 || index >= _visibleEntries.Count) return;
        var itemHeight = VirtualizingStackRangeCalculator.NormalizeItemHeight(ItemHeight);
        UpdateExtent();
        var top = index * itemHeight;
        var bottom = top + itemHeight;
        if (top < VerticalOffset) ScrollTop = top;
        else if (bottom > VerticalOffset + Math.Max(itemHeight, ViewportHeight))
            ScrollTop = bottom - Math.Max(itemHeight, ViewportHeight);
        Realize(force: true);
    }

    void ILayoutPreparingElement.PrepareLayout(Size availableSize) =>
        Realize(GetViewportHeight(availableSize), force: false);

    protected override void OnPropertyChanged(string name)
    {
        base.OnPropertyChanged(name);
        if (name is nameof(ItemHeight) or nameof(OverscanCount) or nameof(IndentSize))
        {
            UpdateExtent();
            Realize(force: true);
        }
    }

    protected override void OnAttachedCore()
    {
        base.OnAttachedCore();
        SubscribeSource();
    }

    protected override void OnDetachedCore()
    {
        UnsubscribeSource();
        base.OnDetachedCore();
    }

    internal override void OnChildRemoved(Element child)
    {
        base.OnChildRemoved(child);
        if (!_realizing && child is TreeItem item) _realizedIndexes.Remove(item);
    }

    private bool SetExpanded(int index, bool expanded)
    {
        if (index < 0 || index >= _visibleEntries.Count) return false;
        var entry = _visibleEntries[index];
        if (!entry.HasChildren) return false;
        var changed = expanded ? _expandedKeys.Add(entry.Key) : _expandedKeys.Remove(entry.Key);
        if (!changed) return false;

        var selectedKey = _selectedKey;
        RebuildVisibleEntries();
        _selectedKey = selectedKey != null && FindIndexByKey(selectedKey) >= 0 ? selectedKey : entry.Key;
        _activeIndex = FindIndexByKey(entry.Key);
        ApplySelectionToRealizedItems();
        var realized = GetRealizedItem(_activeIndex);
        realized?.DispatchEvent(new Event(expanded ? "expand" : "collapse", new EventInit { Bubbles = true }));
        return true;
    }

    private void RebuildVisibleEntries()
    {
        _visibleEntries.Clear();
        if (_getRoot != null && _getRootCount != null && _getChildren != null && _getKey != null)
        {
            for (var index = 0; index < _getRootCount(); index++)
            {
                var item = _getRoot(index);
                if (item != null) AddVisibleEntry(item, depth: 0, parentKey: null);
            }
        }
        _activeIndex = Math.Min(_activeIndex, _visibleEntries.Count - 1);
        UpdateExtent();
        Realize(force: true);
    }

    private void AddVisibleEntry(object item, int depth, object? parentKey)
    {
        var key = _getKey!(item);
        var children = _getChildren!(item);
        _visibleEntries.Add(new VirtualTreeEntry(item, key, depth, parentKey, children.Count > 0));
        if (!_expandedKeys.Contains(key)) return;
        foreach (var child in children)
            if (child != null) AddVisibleEntry(child, depth + 1, key);
    }

    private void Realize(bool force) => Realize(GetViewportHeight(default), force);

    private void Realize(float viewportHeight, bool force)
    {
        var itemHeight = VirtualizingStackRangeCalculator.NormalizeItemHeight(ItemHeight);
        var range = VirtualizingStackRangeCalculator.Calculate(
            _visibleEntries.Count, VerticalOffset, viewportHeight, itemHeight, OverscanCount);
        if (!force && range == _realizedRange) return;

        _realizing = true;
        try
        {
            Children.Clear();
            _realizedIndexes.Clear();
            SetSpacerHeight(_topSpacer, range.Count == 0 ? 0 : range.FirstIndex * itemHeight);
            SetSpacerHeight(_bottomSpacer, range.Count == 0 ? 0 : (_visibleEntries.Count - range.LastIndex - 1) * itemHeight);
            Children.Add(_topSpacer);
            if (_itemTemplate != null)
            {
                for (var index = range.FirstIndex; index <= range.LastIndex; index++)
                {
                    var entry = _visibleEntries[index];
                    var row = _itemTemplate(entry.Item, entry.Depth);
                    row.HasVirtualItems = entry.HasChildren;
                    row.IsExpanded = _expandedKeys.Contains(entry.Key);
                    row.IsSelected = Equals(_selectedKey, entry.Key);
                    row.Style.SetCascaded("height", $"{itemHeight}px", int.MinValue);
                    row.Style.SetCascaded("margin-left", $"{entry.Depth * IndentSize}px", int.MinValue);
                    row.Style.SetCascaded("flex-shrink", "0", int.MinValue);
                    _realizedIndexes[row] = index;
                    Children.Add(row);
                }
            }
            Children.Add(_bottomSpacer);
            _realizedRange = range;
        }
        finally
        {
            _realizing = false;
        }
    }

    private void OnItemClick(Event e)
    {
        if (!IsEnabled || e.Target is not Element target) return;
        for (Element? current = target; current != null && current != this; current = current.Parent)
        {
            if (current is not TreeItem item || !_realizedIndexes.TryGetValue(item, out var index)) continue;
            SelectIndex(index);
            if (_visibleEntries[index].HasChildren) ToggleIndex(index);
            Focus();
            return;
        }
    }

    private bool SelectAndReveal(int index)
    {
        var changed = SelectIndex(index);
        ScrollIntoView(index);
        return changed || index >= 0;
    }

    private void ApplySelectionToRealizedItems()
    {
        foreach (var (item, index) in _realizedIndexes)
            item.IsSelected = Equals(_selectedKey, _visibleEntries[index].Key);
    }

    private TreeItem? GetRealizedItem(int index) =>
        _realizedIndexes.FirstOrDefault(entry => entry.Value == index).Key;

    private int FindIndexByKey(object? key)
    {
        if (key == null) return -1;
        for (var index = 0; index < _visibleEntries.Count; index++)
            if (Equals(_visibleEntries[index].Key, key)) return index;
        return -1;
    }

    private float GetViewportHeight(Size availableSize)
    {
        if (float.IsFinite(availableSize.Height) && availableSize.Height > 0) return availableSize.Height;
        if (Geometry.Height > 0) return Geometry.Height;
        return VirtualizingStackRangeCalculator.NormalizeItemHeight(ItemHeight);
    }

    private void UpdateExtent()
    {
        var height = _visibleEntries.Count * VirtualizingStackRangeCalculator.NormalizeItemHeight(ItemHeight);
        SetScrollContentSize(new Size(Math.Max(Geometry.Width, ScrollContentSize.Width), height));
    }

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var selected = _selectedKey;
        RebuildVisibleEntries();
        _selectedKey = selected != null && FindIndexByKey(selected) >= 0 ? selected : null;
        ApplySelectionToRealizedItems();
    }

    private void SubscribeSource()
    {
        if (_observableRoots == null || _sourceSubscribed) return;
        _observableRoots.CollectionChanged += OnSourceChanged;
        _sourceSubscribed = true;
    }

    private void UnsubscribeSource()
    {
        if (_observableRoots == null || !_sourceSubscribed) return;
        _observableRoots.CollectionChanged -= OnSourceChanged;
        _sourceSubscribed = false;
    }

    private static void ConfigureSpacer(View spacer) =>
        spacer.Style.SetCascaded("flex-shrink", "0", int.MinValue);

    private static void SetSpacerHeight(View spacer, float height) =>
        spacer.Style.SetCascaded("height", $"{Math.Max(0, height)}px", int.MinValue);

    private sealed record VirtualTreeEntry(object Item, object Key, int Depth, object? ParentKey, bool HasChildren);
}
