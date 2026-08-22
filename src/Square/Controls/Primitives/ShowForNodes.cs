using System.Collections.Specialized;
using Square.Runtime.Binding;
using Square.Runtime.State;
using Square.UI;

namespace Square.Controls.Primitives;

/// <summary>条件渲染节点，按布尔条件或可观察值显示主元素或后备元素。</summary>
public sealed class ShowNode : IDisposable
{
    private readonly ObservableValue<bool>? _source;
    private readonly Func<bool> _condition;
    private readonly Func<Element?> _build;
    private readonly Func<Element?>? _fallbackBuild;
    private IDisposable? _subscription;
    private bool _lastValue;
    private Element? _child;
    private Element? _fallback;
    private Element? _parent;
    private int _index;
    private bool _disposed;

    /// <summary>以可观察布尔值与构造器初始化 <see cref="ShowNode"/>。</summary>
    public ShowNode(ObservableValue<bool> source, Func<Element?> build)
        : this(() => source.Value, build)
    {
        _source = source;
        _subscription = source.Subscribe(_ => ScheduleUpdate());
    }

    /// <summary>以可观察布尔值、主构造器与后备构造器初始化 <see cref="ShowNode"/>。</summary>
    public ShowNode(ObservableValue<bool> source, Func<Element?> build, Func<Element?> fallbackBuild)
        : this(() => source.Value, build, fallbackBuild)
    {
        _source = source;
        _subscription = source.Subscribe(_ => ScheduleUpdate());
    }

    /// <summary>以反应值与构造器初始化 <see cref="ShowNode"/>。</summary>
    public ShowNode(IReactiveValue<bool> source, Func<Element?> build)
        : this(() => source.Value, build)
    {
        _subscription = source.Subscribe(_ => ScheduleUpdate());
    }

    /// <summary>以反应值、主构造器与后备构造器初始化 <see cref="ShowNode"/>。</summary>
    public ShowNode(IReactiveValue<bool> source, Func<Element?> build, Func<Element?> fallbackBuild)
        : this(() => source.Value, build, fallbackBuild)
    {
        _subscription = source.Subscribe(_ => ScheduleUpdate());
    }

    /// <summary>以布尔条件函数与构造器初始化 <see cref="ShowNode"/>。</summary>
    public ShowNode(Func<bool> condition, Func<Element?> build)
        : this(condition, build, null)
    {
    }

    /// <summary>以布尔条件函数、主构造器与可选后备构造器初始化 <see cref="ShowNode"/>。</summary>
    public ShowNode(Func<bool> condition, Func<Element?> build, Func<Element?>? fallbackBuild)
    {
        _condition = condition;
        _build = build;
        _fallbackBuild = fallbackBuild;
        _lastValue = condition();
        if (_lastValue) _child = _build();
        else if (_fallbackBuild != null) _fallback = _fallbackBuild();
    }

    /// <summary>把节点挂载到指定父元素。</summary>
    public void AttachTo(Element parent)
    {
        _parent = parent;
        _index = parent.Children.Count;
        if (_child != null)
        {
            parent.Children.Insert(_index, _child);
            _child.BuildElementTree();
        }
        else if (_fallback != null)
        {
            parent.Children.Insert(_index, _fallback);
            _fallback.BuildElementTree();
        }
    }

    /// <summary>通过 Reconciler 批处理，而非即时修改树。</summary>
    private void ScheduleUpdate()
    {
        if (_disposed) return;
        (_parent?.Reconciler ?? Reconciler.Current).ScheduleUpdate(Update);
    }

    /// <summary>重新评估条件并同步 DOM 子树。</summary>
    public void Update()
    {
        var val = _condition();
        if (val == _lastValue) return;
        _lastValue = val;

        if (val)
        {
            if (_fallback != null && _parent != null) _parent.Children.Remove(_fallback);
            _child ??= _build();
            if (_child != null && _parent != null)
            {
                _parent.Children.Insert(Math.Min(_index, _parent.Children.Count), _child);
                _child.BuildElementTree();
            }
        }
        else
        {
            if (_child != null && _parent != null) _parent.Children.Remove(_child);
            _fallback ??= _fallbackBuild?.Invoke();
            if (_fallback != null && _parent != null)
            {
                _parent.Children.Insert(Math.Min(_index, _parent.Children.Count), _fallback);
                _fallback.BuildElementTree();
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _subscription?.Dispose();
        _subscription = null;
        if (_child != null && _parent != null) _parent.Children.Remove(_child);
        if (_fallback != null && _parent != null) _parent.Children.Remove(_fallback);
        _child?.DiscardGeneratedSubtree();
        _fallback?.DiscardGeneratedSubtree();
        _child = null;
        _fallback = null;
        _parent = null;
    }
}

/// <summary>循环渲染节点的公共契约。</summary>
public interface IForNode : IDisposable
{
    /// <summary>把节点挂载到指定父元素。</summary>
    void AttachTo(Element parent);
    /// <summary>重新同步 DOM 子树与数据源。</summary>
    void Update();
}

/// <summary>列表循环渲染节点的工厂，按数据源类型派生对应实现。</summary>
public static class ForNode
{
    /// <summary>创建基于可观察集合的循环节点。</summary>
    public static IForNode Create<T>(ObservableCollection<T> source, Func<T, Element?> build) =>
        new ForNode<T>(() => source, build, source);

    /// <summary>创建基于可观察集合且带后备的循环节点。</summary>
    public static IForNode Create<T>(ObservableCollection<T> source, Func<T, Element?> build, Func<Element?> fallbackBuild) =>
        new ForFallbackNode<T>(new ForNode<T>(() => source, build, source), () => source, fallbackBuild, source);

    /// <summary>创建基于可观察集合的下标循环节点。</summary>
    public static IForNode Create<T>(ObservableCollection<T> source, Func<T, int, Element?> build) =>
        new ForNode<T>(() => source, build, source);

    /// <summary>创建基于可观察集合且带后备的下标循环节点。</summary>
    public static IForNode Create<T>(ObservableCollection<T> source, Func<T, int, Element?> build, Func<Element?> fallbackBuild) =>
        new ForFallbackNode<T>(new ForNode<T>(() => source, build, source), () => source, fallbackBuild, source);

    /// <summary>创建基于可枚举集合的循环节点。</summary>
    public static IForNode Create<T>(IEnumerable<T> source, Func<T, Element?> build) =>
        new ForNode<T>(() => source, build, source as INotifyCollectionChanged);

    /// <summary>创建基于可枚举集合且带后备的循环节点。</summary>
    public static IForNode Create<T>(IEnumerable<T> source, Func<T, Element?> build, Func<Element?> fallbackBuild) =>
        new ForFallbackNode<T>(new ForNode<T>(() => source, build, source as INotifyCollectionChanged), () => source, fallbackBuild, source as INotifyCollectionChanged);

    /// <summary>创建基于可枚举集合的下标循环节点。</summary>
    public static IForNode Create<T>(IEnumerable<T> source, Func<T, int, Element?> build) =>
        new ForNode<T>(() => source, build, source as INotifyCollectionChanged);

    /// <summary>创建基于可枚举集合且带后备的下标循环节点。</summary>
    public static IForNode Create<T>(IEnumerable<T> source, Func<T, int, Element?> build, Func<Element?> fallbackBuild) =>
        new ForFallbackNode<T>(new ForNode<T>(() => source, build, source as INotifyCollectionChanged), () => source, fallbackBuild, source as INotifyCollectionChanged);

    /// <summary>创建基于反应值列表的循环节点。</summary>
    public static IForNode Create<T>(IReactiveValue<IReadOnlyList<T>> source, Func<T, Element?> build) =>
        new ReactiveForNode<T>(source, build);

    /// <summary>创建基于反应值列表的下标循环节点。</summary>
    public static IForNode Create<T>(IReactiveValue<IReadOnlyList<T>> source, Func<T, int, Element?> build) =>
        new ReactiveForNode<T>(source, build);

    /// <summary>创建基于键选择器的可观察集合循环节点。</summary>
    public static IForNode Create<T, TKey>(ObservableCollection<T> source, Func<T, TKey> keySelector, Func<T, Element?> build) where TKey : notnull =>
        new KeyedForNode<T, TKey>(() => source, keySelector, build, source);

    /// <summary>创建基于键选择器且带后备的可观察集合循环节点。</summary>
    public static IForNode Create<T, TKey>(ObservableCollection<T> source, Func<T, TKey> keySelector, Func<T, Element?> build, Func<Element?> fallbackBuild) where TKey : notnull =>
        new ForFallbackNode<T>(new KeyedForNode<T, TKey>(() => source, keySelector, build, source), () => source, fallbackBuild, source);

    /// <summary>创建基于下标键选择器且带后备的可观察集合循环节点。</summary>
    public static IForNode Create<T, TKey>(ObservableCollection<T> source, Func<T, int, TKey> keySelector, Func<T, int, Element?> build, Func<Element?> fallbackBuild) where TKey : notnull =>
        new ForFallbackNode<T>(new KeyedForNode<T, TKey>(() => source, keySelector, build, source), () => source, fallbackBuild, source);

    /// <summary>创建基于下标键选择器的可观察集合循环节点。</summary>
    public static IForNode Create<T, TKey>(ObservableCollection<T> source, Func<T, int, TKey> keySelector, Func<T, int, Element?> build) where TKey : notnull =>
        new KeyedForNode<T, TKey>(() => source, keySelector, build, source);

    /// <summary>创建基于键选择器的可枚举集合循环节点。</summary>
    public static IForNode Create<T, TKey>(IEnumerable<T> source, Func<T, TKey> keySelector, Func<T, Element?> build) where TKey : notnull =>
        new KeyedForNode<T, TKey>(() => source, keySelector, build, source as INotifyCollectionChanged);

    /// <summary>创建基于键选择器且带后备的可枚举集合循环节点。</summary>
    public static IForNode Create<T, TKey>(IEnumerable<T> source, Func<T, TKey> keySelector, Func<T, Element?> build, Func<Element?> fallbackBuild) where TKey : notnull =>
        new ForFallbackNode<T>(new KeyedForNode<T, TKey>(() => source, keySelector, build, source as INotifyCollectionChanged), () => source, fallbackBuild, source as INotifyCollectionChanged);

    /// <summary>创建基于下标键选择器的可枚举集合循环节点。</summary>
    public static IForNode Create<T, TKey>(IEnumerable<T> source, Func<T, int, TKey> keySelector, Func<T, int, Element?> build) where TKey : notnull =>
        new KeyedForNode<T, TKey>(() => source, keySelector, build, source as INotifyCollectionChanged);

    /// <summary>创建基于下标键选择器且带后备的可枚举集合循环节点。</summary>
    public static IForNode Create<T, TKey>(IEnumerable<T> source, Func<T, int, TKey> keySelector, Func<T, int, Element?> build, Func<Element?> fallbackBuild) where TKey : notnull =>
        new ForFallbackNode<T>(new KeyedForNode<T, TKey>(() => source, keySelector, build, source as INotifyCollectionChanged), () => source, fallbackBuild, source as INotifyCollectionChanged);

    /// <summary>创建基于反应值列表与键选择器的循环节点。</summary>
    public static IForNode Create<T, TKey>(IReactiveValue<IReadOnlyList<T>> source, Func<T, TKey> keySelector, Func<T, Element?> build) where TKey : notnull =>
        new KeyedForNode<T, TKey>(source, keySelector, build);

    /// <summary>创建基于反应值列表与下标键选择器的循环节点。</summary>
    public static IForNode Create<T, TKey>(IReactiveValue<IReadOnlyList<T>> source, Func<T, int, TKey> keySelector, Func<T, int, Element?> build) where TKey : notnull =>
        new KeyedForNode<T, TKey>(source, keySelector, build);
}

internal sealed class ForFallbackNode<T> : IForNode
{
    private readonly IForNode _inner;
    private readonly Func<IEnumerable<T>> _source;
    private readonly Func<Element?> _fallbackBuild;
    private readonly INotifyCollectionChanged? _observableSource;
    private Element? _fallback;
    private Element? _parent;
    private int _index;

    public ForFallbackNode(IForNode inner, Func<IEnumerable<T>> source, Func<Element?> fallbackBuild, INotifyCollectionChanged? observableSource)
    {
        _inner = inner;
        _source = source;
        _fallbackBuild = fallbackBuild;
        _observableSource = observableSource;
        if (_observableSource != null) _observableSource.CollectionChanged += OnChanged;
    }

    public void AttachTo(Element parent)
    {
        _parent = parent;
        _index = parent.Children.Count;
        _inner.AttachTo(parent);
        UpdateFallback();
    }

    public void Update()
    {
        _inner.Update();
        UpdateFallback();
    }

    private void OnChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        (_parent?.Reconciler ?? Reconciler.Current).ScheduleUpdate(UpdateFallback);

    private void UpdateFallback()
    {
        if (_parent == null) return;
        var empty = !_source().Any();
        if (empty)
        {
            _fallback ??= _fallbackBuild();
            if (_fallback is { Parent: null } fallback)
            {
                _parent.Children.Insert(Math.Min(_index, _parent.Children.Count), fallback);
                fallback.BuildElementTree();
            }
        }
        else if (_fallback?.Parent == _parent)
        {
            _parent.Children.Remove(_fallback);
        }
    }

    public void Dispose()
    {
        if (_observableSource != null) _observableSource.CollectionChanged -= OnChanged;
        if (_fallback != null && _parent != null && _fallback.Parent == _parent) _parent.Children.Remove(_fallback);
        _inner.Dispose();
        _fallback?.DiscardGeneratedSubtree();
        _fallback = null;
        _parent = null;
    }
}

/// <summary>按下标重建的循环渲染节点工厂，集合变更时整体重排。</summary>
public static class IndexNode
{
    /// <summary>创建基于可观察集合的下标循环节点。</summary>
    public static IForNode Create<T>(ObservableCollection<T> source, Func<T, Element?> build) =>
        new IndexNode<T>(() => source, build, source);

    /// <summary>创建基于可观察集合且带后备的下标循环节点。</summary>
    public static IForNode Create<T>(ObservableCollection<T> source, Func<T, Element?> build, Func<Element?> fallbackBuild) =>
        new ForFallbackNode<T>(new IndexNode<T>(() => source, build, source), () => source, fallbackBuild, source);

    /// <summary>创建基于可观察集合的下标循环节点（带索引构造器）。</summary>
    public static IForNode Create<T>(ObservableCollection<T> source, Func<T, int, Element?> build) =>
        new IndexNode<T>(() => source, build, source);

    /// <summary>创建基于可观察集合且带后备的下标循环节点（带索引构造器）。</summary>
    public static IForNode Create<T>(ObservableCollection<T> source, Func<T, int, Element?> build, Func<Element?> fallbackBuild) =>
        new ForFallbackNode<T>(new IndexNode<T>(() => source, build, source), () => source, fallbackBuild, source);

    /// <summary>创建基于可枚举集合的下标循环节点。</summary>
    public static IForNode Create<T>(IEnumerable<T> source, Func<T, Element?> build) =>
        new IndexNode<T>(() => source, build, source as INotifyCollectionChanged);

    /// <summary>创建基于可枚举集合的下标循环节点（带索引构造器）。</summary>
    public static IForNode Create<T>(IEnumerable<T> source, Func<T, int, Element?> build) =>
        new IndexNode<T>(() => source, build, source as INotifyCollectionChanged);
}

internal sealed class IndexNode<T> : IForNode
{
    private readonly Func<IEnumerable<T>> _source;
    private readonly Func<T, Element?>? _build;
    private readonly Func<T, int, Element?>? _buildIndexed;
    private readonly INotifyCollectionChanged? _observableSource;
    private readonly List<Element?> _nodes = [];
    private Element? _parent;
    private int _index;
    private bool _disposed;

    public IndexNode(Func<IEnumerable<T>> source, Func<T, Element?> build, INotifyCollectionChanged? observableSource)
    {
        _source = source;
        _build = build;
        _observableSource = observableSource;
        Reconcile();
        if (_observableSource != null) _observableSource.CollectionChanged += OnCollectionChanged;
    }

    public IndexNode(Func<IEnumerable<T>> source, Func<T, int, Element?> build, INotifyCollectionChanged? observableSource)
    {
        _source = source;
        _buildIndexed = build;
        _observableSource = observableSource;
        Reconcile();
        if (_observableSource != null) _observableSource.CollectionChanged += OnCollectionChanged;
    }

    public void AttachTo(Element parent)
    {
        _parent = parent;
        _index = parent.Children.Count;
        ApplyTree();
    }

    public void Update() => Reconcile();

    private Element? Build(T item, int index) =>
        _build != null ? _build(item) : _buildIndexed?.Invoke(item, index);

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_disposed) return;
        (_parent?.Reconciler ?? Reconciler.Current).ScheduleUpdate(Reconcile);
    }

    private void Reconcile()
    {
        if (_disposed) return;
        var items = _source().ToList();
        var common = Math.Min(items.Count, _nodes.Count);
        for (var i = 0; i < common; i++) ReplaceNode(i, Build(items[i], i));
        while (_nodes.Count > items.Count) RemoveNode(_nodes.Count - 1);
        for (var i = common; i < items.Count; i++) _nodes.Add(Build(items[i], i));
        ApplyTree();
    }

    private void ReplaceNode(int index, Element? replacement)
    {
        var previous = _nodes[index];
        if (previous != null && _parent != null && previous.Parent == _parent) _parent.Children.Remove(previous);
        previous?.DiscardGeneratedSubtree();
        _nodes[index] = replacement;
    }

    private void RemoveNode(int index)
    {
        var node = _nodes[index];
        if (node != null && _parent != null && node.Parent == _parent) _parent.Children.Remove(node);
        node?.DiscardGeneratedSubtree();
        _nodes.RemoveAt(index);
    }

    private void ApplyTree()
    {
        if (_parent == null) return;
        var childIndex = Math.Min(_index, _parent.Children.Count);
        foreach (var node in _nodes)
        {
            if (node == null) continue;
            if (node.Parent == null)
            {
                _parent.Children.Insert(childIndex, node);
                node.BuildElementTree();
            }
            else if (node.Parent == _parent)
            {
                var current = _parent.Children.IndexOf(node);
                if (current != childIndex) _parent.Children.Move(current, childIndex);
            }
            childIndex++;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_observableSource != null) _observableSource.CollectionChanged -= OnCollectionChanged;
        if (_parent != null)
            foreach (var node in _nodes)
                if (node?.Parent == _parent) _parent.Children.Remove(node);
        foreach (var node in _nodes) node?.DiscardGeneratedSubtree();
        _nodes.Clear();
        _parent = null;
    }
}

internal sealed class KeyedForNode<T, TKey> : IForNode where TKey : notnull
{
    private readonly Func<IEnumerable<T>> _source;
    private readonly Func<T, TKey>? _keySelector;
    private readonly Func<T, int, TKey>? _keySelectorIndexed;
    private readonly Func<T, Element?>? _build;
    private readonly Func<T, int, Element?>? _buildIndexed;
    private readonly INotifyCollectionChanged? _observableSource;
    private readonly IReactiveValue<IReadOnlyList<T>>? _reactiveSource;
    private List<Entry> _entries = [];
    private IDisposable? _subscription;
    private Element? _parent;
    private int _index;
    private bool _disposed;

    internal KeyedForNode(
        Func<IEnumerable<T>> source,
        Func<T, TKey> keySelector,
        Func<T, Element?> build,
        INotifyCollectionChanged? observableSource)
    {
        _source = source;
        _keySelector = keySelector;
        _build = build;
        _observableSource = observableSource;
        Reconcile();
        if (_observableSource != null) _observableSource.CollectionChanged += OnCollectionChanged;
    }

    internal KeyedForNode(
        Func<IEnumerable<T>> source,
        Func<T, int, TKey> keySelector,
        Func<T, int, Element?> build,
        INotifyCollectionChanged? observableSource)
    {
        _source = source;
        _keySelectorIndexed = keySelector;
        _buildIndexed = build;
        _observableSource = observableSource;
        Reconcile();
        if (_observableSource != null) _observableSource.CollectionChanged += OnCollectionChanged;
    }

    internal KeyedForNode(IReactiveValue<IReadOnlyList<T>> source, Func<T, TKey> keySelector, Func<T, Element?> build)
    {
        _reactiveSource = source;
        _source = () => source.Value;
        _keySelector = keySelector;
        _build = build;
        Reconcile();
    }

    internal KeyedForNode(IReactiveValue<IReadOnlyList<T>> source, Func<T, int, TKey> keySelector, Func<T, int, Element?> build)
    {
        _reactiveSource = source;
        _source = () => source.Value;
        _keySelectorIndexed = keySelector;
        _buildIndexed = build;
        Reconcile();
    }

    public void AttachTo(Element parent)
    {
        _parent = parent;
        _index = parent.Children.Count;
        ApplyTreeOrder();
        if (_reactiveSource != null)
        {
            _subscription = _reactiveSource.Subscribe(
                _ => parent.Reconciler.ScheduleUpdate(Reconcile),
                new ReactiveSubscriptionOptions { Dispatcher = parent.Dispatcher });
        }
    }

    public void Update() => Reconcile();

    private TKey SelectKey(T item, int index) =>
        _keySelector != null ? _keySelector(item) : _keySelectorIndexed!(item, index);

    private Element? Build(T item, int index) =>
        _build != null ? _build(item) : _buildIndexed?.Invoke(item, index);

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_disposed) return;
        (_parent?.Reconciler ?? Reconciler.Current).ScheduleUpdate(Reconcile);
    }

    private void Reconcile()
    {
        if (_disposed) return;

        var oldByKey = new Dictionary<TKey, Entry>(EqualityComparer<TKey>.Default);
        foreach (var entry in _entries)
            oldByKey.Add(entry.Key, entry);

        var next = new List<Entry>();
        var seen = new HashSet<TKey>(EqualityComparer<TKey>.Default);
        try
        {
            var index = 0;
            foreach (var item in _source())
            {
                var key = SelectKey(item, index);
                if (key is null)
                    throw new InvalidOperationException("ForNode keys cannot be null.");
                if (!seen.Add(key))
                    throw new InvalidOperationException("Duplicate key '" + key + "' in ForNode source.");

                if (oldByKey.TryGetValue(key, out var existing) && SameItem(existing.Item, item))
                    next.Add(existing);
                else
                    next.Add(new Entry(key, item, Build(item, index)));
                index++;
            }
        }
        catch
        {
            var existingEntries = new HashSet<Entry>(_entries);
            foreach (var entry in next)
                if (!existingEntries.Contains(entry)) entry.Node?.DiscardGeneratedSubtree();
            throw;
        }

        if (_parent != null)
        {
            var retained = new HashSet<Element>(next.Where(entry => entry.Node != null).Select(entry => entry.Node!));
            foreach (var entry in _entries)
            {
                if (entry.Node != null && entry.Node.Parent == _parent && !retained.Contains(entry.Node))
                    _parent.Children.Remove(entry.Node);
            }
        }

        var retainedEntries = new HashSet<Entry>(next);
        foreach (var entry in _entries)
            if (!retainedEntries.Contains(entry)) entry.Node?.DiscardGeneratedSubtree();

        _entries = next;
        ApplyTreeOrder();
    }

    private void ApplyTreeOrder()
    {
        if (_parent == null) return;
        var childIndex = Math.Min(_index, _parent.Children.Count);
        foreach (var entry in _entries)
        {
            if (entry.Node == null) continue;
            if (entry.Node.Parent == null)
            {
                _parent.Children.Insert(childIndex, entry.Node);
                entry.Node.BuildElementTree();
            }
            else if (entry.Node.Parent == _parent)
            {
                var currentIndex = _parent.Children.IndexOf(entry.Node);
                if (currentIndex != childIndex)
                    _parent.Children.Move(currentIndex, childIndex);
            }
            childIndex++;
        }
    }

    private static bool SameItem(T left, T right) =>
        typeof(T).IsValueType
            ? EqualityComparer<T>.Default.Equals(left, right)
            : ReferenceEquals(left, right);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_observableSource != null) _observableSource.CollectionChanged -= OnCollectionChanged;
        _subscription?.Dispose();
        _subscription = null;
        if (_parent != null)
        {
            foreach (var entry in _entries)
                if (entry.Node?.Parent == _parent) _parent.Children.Remove(entry.Node);
        }
        foreach (var entry in _entries) entry.Node?.DiscardGeneratedSubtree();
        _entries.Clear();
        _parent = null;
    }

    private sealed class Entry(TKey key, T item, Element? node)
    {
        public TKey Key { get; } = key;
        public T Item { get; } = item;
        public Element? Node { get; } = node;
    }
}

internal sealed class ReactiveForNode<T> : IForNode
{
    private readonly IReactiveValue<IReadOnlyList<T>> _source;
    private readonly Func<T, Element?>? _build;
    private readonly Func<T, int, Element?>? _buildIndexed;
    private readonly List<Element> _children = [];
    private IDisposable? _subscription;
    private Element? _parent;
    private int _index;

    public ReactiveForNode(IReactiveValue<IReadOnlyList<T>> source, Func<T, Element?> build)
    {
        _source = source;
        _build = build;
    }

    public ReactiveForNode(IReactiveValue<IReadOnlyList<T>> source, Func<T, int, Element?> build)
    {
        _source = source;
        _buildIndexed = build;
    }

    public void AttachTo(Element parent)
    {
        _parent = parent;
        _index = parent.Children.Count;
        Rebuild();
        _subscription = _source.Subscribe(
            _ => parent.Reconciler.ScheduleUpdate(Rebuild),
            new ReactiveSubscriptionOptions { Dispatcher = parent.Dispatcher });
    }

    public void Update() => Rebuild();

    private Element? Build(T item, int index) =>
        _build != null ? _build(item) : _buildIndexed?.Invoke(item, index);

    private void Rebuild()
    {
        if (_parent == null) return;
        foreach (var child in _children)
            if (child.Parent == _parent) _parent.Children.Remove(child);
        foreach (var child in _children) child.DiscardGeneratedSubtree();
        _children.Clear();
        var i = 0;
        foreach (var item in _source.Value)
        {
            if (Build(item, i++) is not { } child) continue;
            _parent.Children.Insert(Math.Min(_index + _children.Count, _parent.Children.Count), child);
            child.BuildElementTree();
            _children.Add(child);
        }
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
        if (_parent != null)
            foreach (var child in _children)
                if (child.Parent == _parent) _parent.Children.Remove(child);
        foreach (var child in _children) child.DiscardGeneratedSubtree();
        _children.Clear();
        _parent = null;
    }
}

/// <summary>列表循环渲染节点；监听集合变更并增量更新对应子元素。</summary>
public sealed class ForNode<T> : IForNode
{
    private readonly Func<IEnumerable<T>> _source;
    private readonly Func<T, Element?>? _build;
    private readonly Func<T, int, Element?>? _buildIndexed;
    private readonly List<(T item, Element? node)> _nodes = new();
    private readonly INotifyCollectionChanged? _observableSource;
    private Element? _parent;
    private int _index;

    /// <summary>初始化 <see cref="ForNode{T}"/> 的新实例。</summary>
    public ForNode(Func<IEnumerable<T>> source, Func<T, Element?> build)
        : this(source, build, source() as INotifyCollectionChanged)
    {
    }

    /// <summary>初始化 <see cref="ForNode{T}"/> 的新实例（带索引构造器）。</summary>
    public ForNode(Func<IEnumerable<T>> source, Func<T, int, Element?> build)
        : this(source, build, source() as INotifyCollectionChanged)
    {
    }

    internal ForNode(Func<IEnumerable<T>> source, Func<T, Element?> build, INotifyCollectionChanged? observableSource)
    {
        _source = source;
        _build = build;
        _observableSource = observableSource;
        Rebuild();
        if (_observableSource != null) _observableSource.CollectionChanged += OnCollectionChanged;
    }

    internal ForNode(Func<IEnumerable<T>> source, Func<T, int, Element?> build, INotifyCollectionChanged? observableSource)
    {
        _source = source;
        _buildIndexed = build;
        _observableSource = observableSource;
        Rebuild();
        if (_observableSource != null) _observableSource.CollectionChanged += OnCollectionChanged;
    }

    private Element? Build(T item, int index) =>
        _build != null ? _build(item) : _buildIndexed?.Invoke(item, index);

    /// <inheritdoc/>
    public void AttachTo(Element parent)
    {
        _parent = parent;
        _index = parent.Children.Count;
        for (var i = 0; i < _nodes.Count; i++)
            InsertNode(i);
    }

    /// <inheritdoc/>
    public void Update()
    {
        Rebuild();
    }

    private void Rebuild()
    {
        if (_parent != null)
        {
            foreach (var (_, node) in _nodes)
                if (node != null) _parent.Children.Remove(node);
        }
        foreach (var (_, node) in _nodes) node?.DiscardGeneratedSubtree();
        _nodes.Clear();

        var i = 0;
        foreach (var item in _source())
        {
            var node = Build(item, i++);
            _nodes.Add((item, node));
            if (node != null && _parent != null)
            {
                _parent.Children.Insert(Math.Min(_index + _nodes.Count - 1, _parent.Children.Count), node);
                node.BuildElementTree();
            }
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 通过 Reconciler 批处理集合变更，而非即时操作树
        (_parent?.Reconciler ?? Square.UI.Reconciler.Current).ScheduleUpdate(() => ApplyCollectionChange(e));
    }

    private void ApplyCollectionChange(NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems != null:
                var addIndex = e.NewStartingIndex >= 0 ? e.NewStartingIndex : _nodes.Count;
                for (var i = 0; i < e.NewItems.Count; i++)
                {
                    var item = (T)e.NewItems[i]!;
                    _nodes.Insert(addIndex + i, (item, Build(item, addIndex + i)));
                    InsertNode(addIndex + i);
                }
                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems != null:
                var removeIndex = e.OldStartingIndex;
                for (var i = 0; i < e.OldItems.Count; i++) RemoveNode(removeIndex);
                break;
            case NotifyCollectionChangedAction.Move:
                MoveNode(e.OldStartingIndex, e.NewStartingIndex);
                break;
            case NotifyCollectionChangedAction.Replace when e.NewItems != null:
                var replaceIndex = e.NewStartingIndex;
                for (var i = 0; i < e.NewItems.Count; i++)
                {
                    RemoveNode(replaceIndex);
                    var item = (T)e.NewItems[i]!;
                    _nodes.Insert(replaceIndex, (item, Build(item, replaceIndex)));
                    InsertNode(replaceIndex);
                }
                break;
            default:
                Rebuild();
                break;
        }
    }

    private void InsertNode(int nodeIndex)
    {
        var node = _nodes[nodeIndex].node;
        if (node != null && _parent != null)
        {
            _parent.Children.Insert(GetInsertionIndex(nodeIndex), node);
            node.BuildElementTree();
        }
    }

    private int GetInsertionIndex(int nodeIndex)
    {
        if (_parent == null) return 0;
        for (var i = nodeIndex - 1; i >= 0; i--)
        {
            var previous = _nodes[i].node;
            if (previous?.Parent == _parent) return _parent.Children.IndexOf(previous) + 1;
        }
        for (var i = nodeIndex + 1; i < _nodes.Count; i++)
        {
            var next = _nodes[i].node;
            if (next?.Parent == _parent) return _parent.Children.IndexOf(next);
        }
        return Math.Min(_index, _parent.Children.Count);
    }

    private void RemoveNode(int nodeIndex)
    {
        if (nodeIndex < 0 || nodeIndex >= _nodes.Count) return;
        var node = _nodes[nodeIndex].node;
        if (node != null && _parent != null) _parent.Children.Remove(node);
        node?.DiscardGeneratedSubtree();
        _nodes.RemoveAt(nodeIndex);
    }

    private void MoveNode(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= _nodes.Count || newIndex < 0 || newIndex >= _nodes.Count) return;
        var entry = _nodes[oldIndex];
        _nodes.RemoveAt(oldIndex);
        _nodes.Insert(newIndex, entry);
        if (entry.node != null && _parent != null && entry.node.Parent == _parent)
        {
            var current = _parent.Children.IndexOf(entry.node);
            var target = GetInsertionIndex(newIndex);
            if (target > current) target--;
            if (current != target) _parent.Children.Move(current, target);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_observableSource != null)
            _observableSource.CollectionChanged -= OnCollectionChanged;
        if (_parent != null)
        {
            foreach (var (_, node) in _nodes)
                if (node != null) _parent.Children.Remove(node);
        }
        foreach (var (_, node) in _nodes) node?.DiscardGeneratedSubtree();
        _nodes.Clear();
        _parent = null;
    }
}

/// <summary>多分支条件渲染节点，按分支条件选择首个匹配项渲染。</summary>
public sealed class SwitchNode : IDisposable
{
    private readonly List<MatchBranch> _branches = [];
    private Element? _parent;
    private int _index;
    private int _activeBranch = -1;
    private bool _disposed;

    /// <summary>初始化 <see cref="SwitchNode"/> 的新实例。</summary>
    public SwitchNode()
    {
    }

    /// <summary>初始化 <see cref="SwitchNode"/> 的新实例并指定分支选择器。</summary>
    public SwitchNode(Func<int> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
    }

    /// <summary>添加一个条件分支。</summary>
    public void AddBranch(Func<bool> condition, Func<Element?> build)
    {
        _branches.Add(new MatchBranch(condition, build));
    }

    /// <summary>添加一个带初始值的条件分支。</summary>
    public void AddBranch(bool initialValue, Func<bool> condition, Func<Element?> build)
    {
        _branches.Add(new MatchBranch(condition, build));
    }

    /// <summary>添加一个绑定可观察值的条件分支。</summary>
    public void AddBranch(ObservableValue<bool> source, Func<bool> condition, Func<Element?> build)
    {
        var branch = new MatchBranch(condition, build);
        branch.Subscription = source.Subscribe(_ => ScheduleUpdate());
        _branches.Add(branch);
    }

    /// <summary>添加一个绑定反应值的条件分支。</summary>
    public void AddBranch(IReactiveValue<bool> source, Func<bool> condition, Func<Element?> build)
    {
        var branch = new MatchBranch(condition, build);
        branch.Subscription = source.Subscribe(_ => ScheduleUpdate());
        _branches.Add(branch);
    }

    /// <summary>添加默认分支（无条件）。</summary>
    public void AddDefault(Func<Element?> build)
    {
        _branches.Add(new MatchBranch(null, build));
    }

    /// <summary>把节点挂载到指定父元素。</summary>
    public void AttachTo(Element parent)
    {
        _parent = parent;
        _index = parent.Children.Count;
        UpdateCore();
    }

    /// <summary>重新评估分支并同步 DOM 子树。</summary>
    public void Update()
    {
        if (_disposed || _parent == null) return;
        ScheduleUpdate();
    }

    private void ScheduleUpdate()
    {
        if (_disposed) return;
        (_parent?.Reconciler ?? Square.UI.Reconciler.Current).ScheduleUpdate(UpdateCore);
    }

    private void UpdateCore()
    {
        if (_disposed || _parent == null) return;
        var match = FindMatch();
        if (match == _activeBranch) return;

        if (_activeBranch >= 0 && _activeBranch < _branches.Count)
        {
            var child = _branches[_activeBranch].Child;
            if (child != null) _parent.Children.Remove(child);
        }

        _activeBranch = match;
        if (match >= 0 && match < _branches.Count)
        {
            var branch = _branches[match];
            branch.Child ??= branch.Build();
            if (branch.Child != null)
            {
                branch.Child.BuildElementTree();
                _parent.Children.Insert(Math.Min(_index, _parent.Children.Count), branch.Child);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_parent != null)
        {
            foreach (var branch in _branches)
                if (branch.Child != null) _parent.Children.Remove(branch.Child);
        }
        foreach (var branch in _branches) branch.Child?.DiscardGeneratedSubtree();
        foreach (var branch in _branches) branch.Subscription?.Dispose();
        _branches.Clear();
        _parent = null;
    }

    private int FindMatch()
    {
        for (var i = 0; i < _branches.Count; i++)
        {
            var branch = _branches[i];
            if (branch.Condition == null || branch.Condition())
                return i;
        }
        return -1;
    }

    private sealed class MatchBranch(Func<bool>? condition, Func<Element?> build)
    {
        public Func<bool>? Condition { get; } = condition;
        public Func<Element?> Build { get; } = build;
        public Element? Child { get; set; }
        public IDisposable? Subscription { get; set; }
    }
}
