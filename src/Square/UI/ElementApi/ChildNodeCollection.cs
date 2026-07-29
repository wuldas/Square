namespace Square.UI.ElementApi;

using Square.Runtime;

/// <summary>
/// Child node list, aligned with DOM <c>childNodes</c>.
/// </summary>
public sealed class ChildNodeCollection : IList<Node>
{
    private readonly Element _owner;
    private readonly List<Node> _list = [];

    internal ChildNodeCollection(Element owner) { _owner = owner; }

    /// <summary>获取或设置指定索引处的子节点（设置不支持，抛出异常）。</summary>
    public Node this[int index]
    {
        get => _list[index];
        set => throw new NotSupportedException("Use Insert/RemoveAt to manage child nodes");
    }

    /// <summary>子节点数量。</summary>
    public int Count => _list.Count;

    /// <summary>是否只读。</summary>
    public bool IsReadOnly => false;

    /// <summary>追加子节点。</summary>
    public void Add(Node item)
    {
        ValidateNewChild(item);
        _list.Add(item);
        Attach(item);
    }

    /// <summary>批量追加子节点。</summary>
    public void AddRange(IEnumerable<Node> items)
    {
        foreach (var item in items) Add(item);
    }

    /// <summary>在指定索引处插入子节点。</summary>
    public void Insert(int index, Node item)
    {
        ValidateNewChild(item);
        _list.Insert(index, item);
        Attach(item);
    }

    /// <summary>在参考子节点之前插入子节点。</summary>
    public void InsertBefore(Node newChild, Node refChild)
    {
        var index = _list.IndexOf(refChild);
        if (index < 0) throw new ArgumentException("refChild not found");
        Insert(index, newChild);
    }

    internal void Move(int oldIndex, int newIndex)
    {
        if (oldIndex == newIndex) return;
        var item = _list[oldIndex];
        _list.RemoveAt(oldIndex);
        _list.Insert(newIndex, item);
        InvalidateStructure();
    }

    /// <summary>移除指定子节点。</summary>
    public bool Remove(Node item)
    {
        var index = _list.IndexOf(item);
        if (index < 0) return false;
        RemoveAt(index);
        return true;
    }

    /// <summary>移除指定索引处的子节点。</summary>
    public void RemoveAt(int index)
    {
        var item = _list[index];
        DetachIfNeeded(item);
        _list.RemoveAt(index);
        item.ParentNode = null;
        if (item is Element element) _owner.OnChildRemoved(element);
        InvalidateStructure();
    }

    /// <summary>清空所有子节点。</summary>
    public void Clear()
    {
        foreach (var item in _list)
        {
            DetachIfNeeded(item);
            item.ParentNode = null;
            if (item is Element element) _owner.OnChildRemoved(element);
        }
        _list.Clear();
        InvalidateStructure();
    }

    /// <summary>返回指定子节点的索引。</summary>
    public int IndexOf(Node item) => _list.IndexOf(item);

    /// <summary>是否包含指定子节点。</summary>
    public bool Contains(Node item) => _list.Contains(item);

    /// <summary>复制到数组。</summary>
    public void CopyTo(Node[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);

    /// <summary>返回子节点枚举器。</summary>
    public IEnumerator<Node> GetEnumerator() => _list.GetEnumerator();

    /// <inheritdoc />
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _list.GetEnumerator();

    private void ValidateNewChild(Node item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.ParentNode != null) throw new InvalidOperationException("Node already has a parent");
        if (ReferenceEquals(item, _owner)) throw new InvalidOperationException("Cannot add an element as its own child");
    }

    private void Attach(Node item)
    {
        item.ParentNode = _owner;
        item.OwnerDocument = _owner.OwnerDocument;
        if (_owner.OwnerDocument != null)
            _owner.OwnerDocument.AssignOwnerDocument(item);
        if (item is Element element)
        {
            _owner.OnChildAdded(element);
            AttachIfNeeded(element);
        }
        InvalidateStructure();
    }

    private void InvalidateStructure() =>
        _owner.Invalidate(ElementInvalidation.Style | ElementInvalidation.Layout);

    private void AttachIfNeeded(Element item)
    {
        if (_owner.IsAttached) ((IComponentLifecycle)item).OnAttached();
        if (_owner.IsLoaded) ((IComponentLifecycle)item).OnLoaded();
    }

    private static void DetachIfNeeded(Node item)
    {
        if (item is not Element element) return;
        if (element.IsLoaded) ((IComponentLifecycle)element).OnUnloaded();
        if (element.IsAttached) ((IComponentLifecycle)element).OnDetached();
    }
}
