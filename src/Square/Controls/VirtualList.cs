using System.Collections.Specialized;
using Square.Events;
using Square.Graphics;
using Square.UI;

namespace Square.Controls;

/// <summary>Fixed-row-height virtualized list that realizes only the visible range and overscan.</summary>
public sealed class VirtualList : ScrollViewer, ILayoutPreparingElement
{
    private readonly View _topSpacer = new();
    private readonly View _bottomSpacer = new();
    private readonly SortedSet<int> _selectedIndices = [];
    private readonly Dictionary<ListItem, int> _realizedIndexes = [];
    private Func<int, object?>? _getItem;
    private Func<int>? _getCount;
    private Func<object?, int, ListItem>? _itemTemplate;
    private INotifyCollectionChanged? _observableSource;
    private bool _sourceSubscribed;
    private int _itemCount;
    private int _activeIndex = -1;
    private int _selectionAnchor = -1;
    private VirtualizingStackRange _realizedRange = VirtualizingStackRange.Empty;
    private bool _realizing;

    public VirtualList()
    {
        Style.SetCascaded("display", "flex", int.MinValue);
        Style.SetCascaded("flex-direction", "column", int.MinValue);
        ConfigureSpacer(_topSpacer);
        ConfigureSpacer(_bottomSpacer);
        AddEventListener(StandardEvents.Scroll, () => InvalidateLayout());
        AddEventListener(StandardEvents.Click, OnItemClick);
        AddEventListener<KeyboardEvent>(StandardEvents.KeyDown, e =>
        {
            if (!HandleKey(e.KeyCode, e.ShiftKey, e.ControlKey)) return;
            e.PreventDefault();
        });
    }

    /// <summary>Logical row height used for range calculation and layout.</summary>
    public float ItemHeight
    {
        get => Properties.HasValue(nameof(ItemHeight)) ? GetProperty<float>(nameof(ItemHeight)) : 28f;
        set => SetProperty(nameof(ItemHeight), value);
    }

    /// <summary>Extra rows realized before and after the visible viewport.</summary>
    public int OverscanCount
    {
        get => Properties.HasValue(nameof(OverscanCount)) ? GetProperty<int>(nameof(OverscanCount)) : 3;
        set => SetProperty(nameof(OverscanCount), Math.Max(0, value));
    }

    public SelectionMode SelectionMode
    {
        get => Properties.HasValue(nameof(SelectionMode))
            ? GetProperty<SelectionMode>(nameof(SelectionMode))
            : SelectionMode.Single;
        set => SetProperty(nameof(SelectionMode), value);
    }

    public int ItemCount => _itemCount;
    public int RealizedItemCount => _realizedIndexes.Count;
    public int FirstRealizedIndex => _realizedRange.Count == 0 ? -1 : _realizedRange.FirstIndex;
    public int LastRealizedIndex => _realizedRange.Count == 0 ? -1 : _realizedRange.LastIndex;
    public int SelectedIndex { get => _selectedIndices.Count == 0 ? -1 : _selectedIndices.Min; set => SelectIndex(value); }
    public IReadOnlyList<int> SelectedIndices => _selectedIndices.ToArray();
    public object? SelectedValue => SelectedIndex >= 0 ? _getItem?.Invoke(SelectedIndex) : null;
    public IReadOnlyList<object?> SelectedValues => _selectedIndices.Select(index => _getItem?.Invoke(index)).ToArray();
    public ListItem? SelectedItem => GetRealizedItem(SelectedIndex);
    public IReadOnlyList<ListItem> SelectedItems => _realizedIndexes
        .Where(entry => _selectedIndices.Contains(entry.Value))
        .OrderBy(entry => entry.Value)
        .Select(entry => entry.Key)
        .ToArray();

    public void SetItemsSource<T>(IReadOnlyList<T>? source, Func<T, int, ListItem>? itemTemplate = null)
    {
        UnsubscribeSource();
        if (source == null)
        {
            _getItem = null;
            _getCount = null;
            _itemTemplate = null;
            _observableSource = null;
            _itemCount = 0;
        }
        else
        {
            _getItem = index => source[index];
            _getCount = () => source.Count;
            _itemTemplate = itemTemplate == null
                ? static (item, _) => new ListItem(item?.ToString() ?? "") { Marker = "" }
                : (item, index) => itemTemplate((T)item!, index);
            _observableSource = source as INotifyCollectionChanged;
            _itemCount = source.Count;
        }

        _selectedIndices.Clear();
        _activeIndex = -1;
        _selectionAnchor = -1;
        _realizedRange = VirtualizingStackRange.Empty;
        SubscribeSource();
        UpdateExtent();
        Realize(force: true);
    }

    public bool SelectIndex(int index, bool control = false, bool shift = false)
    {
        if (SelectionMode == SelectionMode.None || index < 0 || index >= _itemCount) return false;
        var before = _selectedIndices.ToArray();
        if (SelectionMode == SelectionMode.Single)
        {
            _selectedIndices.Clear();
            _selectedIndices.Add(index);
        }
        else if (shift)
        {
            var anchor = _selectionAnchor >= 0 ? _selectionAnchor : index;
            if (!control) _selectedIndices.Clear();
            for (var current = Math.Min(anchor, index); current <= Math.Max(anchor, index); current++)
                _selectedIndices.Add(current);
        }
        else if (control)
        {
            if (!_selectedIndices.Remove(index)) _selectedIndices.Add(index);
        }
        else
        {
            _selectedIndices.Clear();
            _selectedIndices.Add(index);
        }

        _activeIndex = index;
        if (!shift) _selectionAnchor = index;
        ApplySelectionToRealizedItems();
        if (before.SequenceEqual(_selectedIndices)) return false;
        DispatchSelectionEvents();
        return true;
    }

    public void ClearSelection()
    {
        if (_selectedIndices.Count == 0) return;
        _selectedIndices.Clear();
        _activeIndex = -1;
        _selectionAnchor = -1;
        ApplySelectionToRealizedItems();
        DispatchSelectionEvents();
    }

    public bool HandleKey(int keyCode, bool shift = false, bool control = false)
    {
        if (!IsEnabled || SelectionMode == SelectionMode.None || _itemCount == 0) return false;
        var next = keyCode switch
        {
            38 => Math.Max(0, _activeIndex >= 0 ? _activeIndex - 1 : _itemCount - 1),
            40 => Math.Min(_itemCount - 1, _activeIndex >= 0 ? _activeIndex + 1 : 0),
            36 => 0,
            35 => _itemCount - 1,
            32 when _activeIndex >= 0 => _activeIndex,
            _ => -1
        };
        if (next < 0) return false;
        if (control && keyCode != 32) _activeIndex = next;
        else SelectIndex(next, control, shift);
        ScrollIntoView(next);
        return true;
    }

    public void ScrollIntoView(int index)
    {
        if (index < 0 || index >= _itemCount) return;
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
        if (name is nameof(ItemHeight) or nameof(OverscanCount))
        {
            UpdateExtent();
            Realize(force: true);
        }
        else if (name == nameof(SelectionMode) && SelectionMode != SelectionMode.Multiple && _selectedIndices.Count > 1)
        {
            SelectIndex(SelectedIndex);
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
        if (!_realizing && child is ListItem item) _realizedIndexes.Remove(item);
    }

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs change)
    {
        var before = _selectedIndices.ToArray();
        RemapIndexes(change, _selectedIndices);
        _itemCount = _getCount?.Invoke() ?? 0;
        _activeIndex = ClampIndex(_activeIndex);
        _selectionAnchor = ClampIndex(_selectionAnchor);
        UpdateExtent();
        Realize(force: true);
        if (!before.SequenceEqual(_selectedIndices)) DispatchSelectionEvents();
    }

    private void Realize(bool force) => Realize(GetViewportHeight(default), force);

    private void Realize(float viewportHeight, bool force)
    {
        var itemHeight = VirtualizingStackRangeCalculator.NormalizeItemHeight(ItemHeight);
        var range = VirtualizingStackRangeCalculator.Calculate(
            _itemCount, VerticalOffset, viewportHeight, itemHeight, OverscanCount);
        if (!force && range == _realizedRange) return;

        _realizing = true;
        try
        {
            Children.Clear();
            _realizedIndexes.Clear();
            SetSpacerHeight(_topSpacer, range.Count == 0 ? 0 : range.FirstIndex * itemHeight);
            SetSpacerHeight(_bottomSpacer, range.Count == 0 ? 0 : (_itemCount - range.LastIndex - 1) * itemHeight);
            Children.Add(_topSpacer);
            if (_getItem != null && _itemTemplate != null)
            {
                for (var index = range.FirstIndex; index <= range.LastIndex; index++)
                {
                    var row = _itemTemplate(_getItem(index), index);
                    row.Marker = row.Marker == "• " ? "" : row.Marker;
                    row.IsSelected = _selectedIndices.Contains(index);
                    row.Style.SetCascaded("height", $"{itemHeight}px", int.MinValue);
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
            if (current is not ListItem item || !_realizedIndexes.TryGetValue(item, out var index)) continue;
            SelectIndex(index);
            Focus();
            return;
        }
    }

    private void ApplySelectionToRealizedItems()
    {
        foreach (var (item, index) in _realizedIndexes) item.IsSelected = _selectedIndices.Contains(index);
    }

    private ListItem? GetRealizedItem(int index) =>
        _realizedIndexes.FirstOrDefault(entry => entry.Value == index).Key;

    private float GetViewportHeight(Size availableSize)
    {
        if (float.IsFinite(availableSize.Height) && availableSize.Height > 0) return availableSize.Height;
        if (Geometry.Height > 0) return Geometry.Height;
        return VirtualizingStackRangeCalculator.NormalizeItemHeight(ItemHeight);
    }

    private void UpdateExtent()
    {
        var height = _itemCount * VirtualizingStackRangeCalculator.NormalizeItemHeight(ItemHeight);
        SetScrollContentSize(new Size(Math.Max(Geometry.Width, ScrollContentSize.Width), height));
    }

    private int ClampIndex(int index) => index < 0 || _itemCount == 0 ? -1 : Math.Min(index, _itemCount - 1);

    private static void ConfigureSpacer(View spacer)
    {
        spacer.Style.SetCascaded("flex-shrink", "0", int.MinValue);
    }

    private static void SetSpacerHeight(View spacer, float height) =>
        spacer.Style.SetCascaded("height", $"{Math.Max(0, height)}px", int.MinValue);

    private void SubscribeSource()
    {
        if (_observableSource == null || _sourceSubscribed) return;
        _observableSource.CollectionChanged += OnSourceChanged;
        _sourceSubscribed = true;
    }

    private void UnsubscribeSource()
    {
        if (_observableSource == null || !_sourceSubscribed) return;
        _observableSource.CollectionChanged -= OnSourceChanged;
        _sourceSubscribed = false;
    }

    private void DispatchSelectionEvents()
    {
        DispatchEvent(StandardEvents.CreateSelectionChange());
        DispatchEvent(StandardEvents.CreateChange());
    }

    private static void RemapIndexes(NotifyCollectionChangedEventArgs change, SortedSet<int> indexes)
    {
        var mapped = indexes.ToList();
        switch (change.Action)
        {
            case NotifyCollectionChangedAction.Add:
                var added = change.NewItems?.Count ?? 0;
                for (var i = 0; i < mapped.Count; i++) if (mapped[i] >= change.NewStartingIndex) mapped[i] += added;
                break;
            case NotifyCollectionChangedAction.Remove:
                var removed = change.OldItems?.Count ?? 0;
                mapped.RemoveAll(index => index >= change.OldStartingIndex && index < change.OldStartingIndex + removed);
                for (var i = 0; i < mapped.Count; i++) if (mapped[i] >= change.OldStartingIndex + removed) mapped[i] -= removed;
                break;
            case NotifyCollectionChangedAction.Move when change.OldStartingIndex >= 0 && change.NewStartingIndex >= 0:
                for (var i = 0; i < mapped.Count; i++)
                {
                    var index = mapped[i];
                    if (index == change.OldStartingIndex) mapped[i] = change.NewStartingIndex;
                    else if (change.OldStartingIndex < change.NewStartingIndex && index > change.OldStartingIndex && index <= change.NewStartingIndex) mapped[i]--;
                    else if (change.NewStartingIndex < change.OldStartingIndex && index >= change.NewStartingIndex && index < change.OldStartingIndex) mapped[i]++;
                }
                break;
            case NotifyCollectionChangedAction.Reset:
                mapped.Clear();
                break;
        }
        indexes.Clear();
        foreach (var index in mapped) indexes.Add(index);
    }
}
