namespace Square.UI;

/// <summary>
/// 协调器（Reconciler）：批处理 Element 树的结构与属性变更，
/// 在下一次布局/绘制前统一 flush，避免中间态触发多次全量重绘。
/// 借鉴 React Reconciler 的「调度 → diff → commit」分阶段模型。
/// </summary>
public sealed class Reconciler
{
    private static Reconciler? _current;

    /// <summary>进程级默认实例（单文档桌面应用足够）。</summary>
    public static Reconciler Current => _current ??= new Reconciler();

    private readonly object _gate = new();
    private readonly HashSet<Element> _dirtyElements = [];
    private readonly List<Action> _pendingUpdates = [];
    private bool _flushing;

    /// <summary>工作队列从空变为非空时在锁外触发，用于唤醒按需宿主。</summary>
    public event Action? WorkAvailable;

    /// <summary>是否有待 flush 的变更。</summary>
    public bool HasWork
    {
        get { lock (_gate) return _dirtyElements.Count > 0 || _pendingUpdates.Count > 0; }
    }

    /// <summary>
    /// 标记元素需要协调（结构或属性已变，需在下一次 flush 时处理）。
    /// 幂等：重复标记同一元素不会重复处理。
    /// </summary>
    public void MarkDirty(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        bool wasEmpty;
        lock (_gate)
        {
            wasEmpty = _dirtyElements.Count == 0 && _pendingUpdates.Count == 0;
            _dirtyElements.Add(element);
        }
        if (wasEmpty) WorkAvailable?.Invoke();
    }

    /// <summary>
    /// 排队一个原子更新操作（如 Show 条件切换、For 列表增删）。
    /// 在 <see cref="Flush"/> 时按入队顺序执行，全部完成后统一标记脏。
    /// </summary>
    public void ScheduleUpdate(Action update)
    {
        ArgumentNullException.ThrowIfNull(update);
        bool wasEmpty;
        lock (_gate)
        {
            wasEmpty = _dirtyElements.Count == 0 && _pendingUpdates.Count == 0;
            _pendingUpdates.Add(update);
        }
        if (wasEmpty) WorkAvailable?.Invoke();
    }

    /// <summary>
    /// 执行所有排队的更新，然后对脏元素标记 <see cref="Element.InvalidateLayout"/>。
    /// 在 Host 的 RenderFrame / Tick 中布局前调用。
    /// </summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (_flushing) return;
            _flushing = true;
        }

        try
        {
            while (true)
            {
                List<Action>? updates = null;
                List<Element>? dirty = null;

                lock (_gate)
                {
                    if (_pendingUpdates.Count > 0)
                    {
                        updates = new List<Action>(_pendingUpdates);
                        _pendingUpdates.Clear();
                    }
                    if (_dirtyElements.Count > 0)
                    {
                        dirty = new List<Element>(_dirtyElements);
                        _dirtyElements.Clear();
                    }
                }

                if (updates == null && dirty == null) break;

                // Phase 1: 执行排队的结构更新（可能产生新的工作）
                if (updates != null)
                {
                    foreach (var update in updates)
                        update();
                }

                // Phase 2: 对显式标记的脏元素传播 Invalidation
                if (dirty == null) continue;
                foreach (var element in dirty)
                    element.InvalidateLayout();
            }
        }
        finally
        {
            lock (_gate) _flushing = false;
        }
    }

    /// <summary>重置（测试/文档切换用）。</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _dirtyElements.Clear();
            _pendingUpdates.Clear();
            _flushing = false;
        }
    }
}
