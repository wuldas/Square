namespace Square.UI.ElementApi;

/// <summary>
/// Element-only children view, aligned with DOM <c>children</c>.
/// </summary>
public sealed class ChildrenCollection : IList<Element>
{
    private readonly ChildNodeCollection _nodes;

    internal ChildrenCollection(ChildNodeCollection nodes) { _nodes = nodes; }

    /// <summary>获取或设置指定索引处的子元素（设置不支持，抛出异常）。</summary>
    public Element this[int index]
    {
        get
        {
            var currentElementIndex = 0;
            for (var i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i] is not Element element) continue;
                if (currentElementIndex == index) return element;
                currentElementIndex++;
            }
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        set => throw new NotSupportedException("Use Insert/RemoveAt to manage children");
    }

    /// <summary>子元素数量。</summary>
    public int Count
    {
        get
        {
            var count = 0;
            for (var i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i] is Element) count++;
            }
            return count;
        }
    }

    /// <summary>是否只读。</summary>
    public bool IsReadOnly => false;

    /// <summary>追加子元素。</summary>
    public void Add(Element item) => _nodes.Add(item);

    /// <summary>批量追加子元素。</summary>
    public void AddRange(IEnumerable<Element> items)
    {
        foreach (var item in items) Add(item);
    }

    /// <summary>在指定索引处插入子元素。</summary>
    public void Insert(int index, Element item) => _nodes.Insert(ToNodeInsertIndex(index), item);

    /// <summary>在参考子元素之前插入子元素。</summary>
    public void InsertBefore(Element newChild, Element refChild) => _nodes.InsertBefore(newChild, refChild);

    /// <summary>将子元素从旧索引移动到新索引。</summary>
    public void Move(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= Count) throw new ArgumentOutOfRangeException(nameof(oldIndex));
        if (newIndex < 0 || newIndex >= Count) throw new ArgumentOutOfRangeException(nameof(newIndex));
        _nodes.Move(ToNodeIndex(oldIndex), ToNodeIndex(newIndex));
    }

    /// <summary>移除指定子元素。</summary>
    public bool Remove(Element item) => _nodes.Remove(item);

    /// <summary>移除指定索引处的子元素。</summary>
    public void RemoveAt(int index) => _nodes.RemoveAt(ToNodeIndex(index));

    /// <summary>清空所有子元素（保留 Text 等非元素节点）。</summary>
    public void Clear()
    {
        foreach (var element in this.ToArray()) _nodes.Remove(element);
    }

    /// <summary>返回指定子元素的索引。</summary>
    public int IndexOf(Element item)
    {
        var index = 0;
        for (var i = 0; i < _nodes.Count; i++)
        {
            if (_nodes[i] is not Element element) continue;
            if (ReferenceEquals(element, item)) return index;
            index++;
        }
        return -1;
    }

    /// <summary>是否包含指定子元素。</summary>
    public bool Contains(Element item) => IndexOf(item) >= 0;

    /// <summary>复制到数组。</summary>
    public void CopyTo(Element[] array, int arrayIndex)
    {
        for (var i = 0; i < _nodes.Count; i++)
        {
            if (_nodes[i] is Element element) array[arrayIndex++] = element;
        }
    }

    /// <summary>返回子元素枚举器（结构体枚举器，foreach 不分配；底层节点枚举器延迟到首次推进时创建）。</summary>
    public Enumerator GetEnumerator() => new(_nodes);

    /// <inheritdoc />
    IEnumerator<Element> IEnumerable<Element>.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    private int ToNodeIndex(int elementIndex)
    {
        if (elementIndex < 0) throw new ArgumentOutOfRangeException(nameof(elementIndex));
        var currentElementIndex = 0;
        for (var i = 0; i < _nodes.Count; i++)
        {
            if (_nodes[i] is not Element) continue;
            if (currentElementIndex == elementIndex) return i;
            currentElementIndex++;
        }
        throw new ArgumentOutOfRangeException(nameof(elementIndex));
    }

    private int ToNodeInsertIndex(int elementIndex)
    {
        if (elementIndex < 0) throw new ArgumentOutOfRangeException(nameof(elementIndex));
        if (elementIndex == Count) return _nodes.Count;
        return ToNodeIndex(elementIndex);
    }

    /// <summary>
    /// 仅遍历元素子节点的结构体枚举器（跳过 Text 等非元素节点；首次推进时才捕获底层列表版本）。
    /// </summary>
    public struct Enumerator : IEnumerator<Element>
    {
        private ChildNodeCollection? _nodes;
        private List<Node>.Enumerator _nodeEnumerator;
        private Element? _current;
        private bool _started;

        internal Enumerator(ChildNodeCollection nodes)
        {
            _nodes = nodes;
            _nodeEnumerator = default;
            _current = null;
            _started = false;
        }

        /// <summary>当前子元素。</summary>
        public Element Current => _current!;

        /// <summary>推进到下一个子元素。</summary>
        public bool MoveNext()
        {
            if (_nodes == null) return false;
            if (!_started)
            {
                _nodeEnumerator = _nodes!.GetEnumerator();
                _started = true;
            }
            while (_nodeEnumerator.MoveNext())
            {
                if (_nodeEnumerator.Current is Element element)
                {
                    _current = element;
                    return true;
                }
            }
            Dispose();
            return false;
        }

        /// <summary>终止枚举并释放底层节点列表引用。</summary>
        public void Dispose()
        {
            _nodeEnumerator = default;
            _nodes = null;
            _current = null;
        }

        object? System.Collections.IEnumerator.Current => _current;

        void System.Collections.IEnumerator.Reset() => throw new NotSupportedException();

    }
}
