using Square.Events;
using Square.Graphics;
using Square.Runtime;
using Square.Runtime.Binding;
using Square.Runtime.State;
using Square.Hosting;
using Square.UI.ElementApi;
using Square.UI.Properties;

namespace Square.UI;

/// <summary>
/// 文档树中的元素节点（对齐 DOM <c>Element</c> 身份，并承载 Square 保留模式布局/绘制扩展）。
/// <para>继承：<see cref="EventTarget"/> → <see cref="Node"/> → <see cref="Element"/>。</para>
/// <para>Web API 对应：<c>tagName</c> / <c>id</c> / <c>classList</c> / <c>style</c> / 树关系 / 事件。</para>
/// <para>Square 扩展：<see cref="Geometry"/>、<see cref="Measure"/>/<see cref="Arrange"/>/<see cref="Paint"/>、脏标记与绑定等。</para>
/// </summary>
public abstract class Element : Node, IComponentLifecycle, ILayoutLifecycle
{
    private Rect _geometry;
    private bool _isVisible = true;
    private bool _isLayoutDirty = true;
    private bool _needsPaint = true;
    private bool _paintFullDirty = true;
    private List<Rect>? _paintDirtyRects;
    private Size _scrollContentSize;
    private Point _scrollOffset;
    private int _zIndex;
    private HitTestEntry[]? _hitTestChildren;
    private readonly List<IDisposable> _bindings = [];
    private List<IDisposable>? _generatedResources;
    private int _debugId;

    [ThreadStatic]
    private static int _invalidationSuppressionDepth;

    /// <summary>样式失效时触发的全局事件（Square 扩展）。</summary>
    public static event Action<Element>? StyleInvalidated;

    private static int NextDebugId;

    /// <summary>调试用唯一标识（懒加载）。</summary>
    public int DebugId => _debugId != 0 ? _debugId : _debugId = Interlocked.Increment(ref NextDebugId);

    /// <summary>调试来源信息。</summary>
    public ElementDebugInfo? DebugInfo { get; private set; }

    /// <summary>设置调试来源信息。</summary>
    public void SetDebugInfo(ElementDebugInfo? debugInfo) => DebugInfo = debugInfo;

    /// <summary>布局是否失效（Square 扩展；引擎在脏时重新 Measure/Arrange）。</summary>
    public bool IsLayoutDirty => _isLayoutDirty;

    /// <summary>绘制是否失效（Square 扩展；DisplayTree 据此重建 DrawCommand）。</summary>
    public bool NeedsPaint => _needsPaint;

    /// <summary>层叠顺序（Square 扩展；类似 CSS <c>z-index</c>）。</summary>
    public virtual int ZIndex
    {
        get => _zIndex;
        set
        {
            if (_zIndex == value) return;
            _zIndex = value;
            Parent?.InvalidateHitTestOrder();
            Parent?.InvalidatePaint();
        }
    }

    /// <summary>强类型属性存储（Square 扩展；非 DOM Attr 系统）。</summary>
    public PropertyStore Properties { get; } = new();

    /// <summary>内联样式访问器（对齐 CSSOM <c>element.style</c>）。</summary>
    public StyleAccessor Style { get; }

    /// <summary>类名列表（对齐 DOMTokenList <c>classList</c>）。</summary>
    public ClassListAccessor ClassList { get; }

    /// <summary>子节点集合（对齐 <c>childNodes</c>；包含 Text 等非元素节点）。</summary>
    public ChildNodeCollection ChildNodes { get; }

    /// <summary>子元素集合（对齐 <c>children</c>；仅包含元素节点）。</summary>
    public ChildrenCollection Children { get; }

    /// <summary>标签名（对齐 <c>tagName</c>；默认取运行时类型名）。</summary>
    public virtual string TagName => GetType().Name;

    /// <inheritdoc />
    public override NodeType NodeTypeValue => NodeType.Element;

    /// <inheritdoc />
    public override string NodeName => TagName;

    /// <summary>命名空间 URI（对齐 <c>namespaceURI</c>；Square UI 元素为 null）。</summary>
    public virtual string? NamespaceURI => null;

    /// <summary>元素 id（对齐 <c>id</c>）。</summary>
    public string? Id
    {
        get => GetProperty<string>(nameof(Id));
        set => SetProperty(nameof(Id), value);
    }

    /// <summary>
    /// 父元素（对齐 <c>parentElement</c>）。
    /// 底层存储为 <see cref="Node.ParentNode"/>。
    /// </summary>
    public Element? Parent
    {
        get => ParentNode as Element;
        internal set => ParentNode = value;
    }

    /// <summary>第一个子元素（对齐 <c>firstElementChild</c>）。</summary>
    public Element? FirstElementChild => Children.Count > 0 ? Children[0] : null;

    /// <summary>最后一个子元素（对齐 <c>lastElementChild</c>）。</summary>
    public Element? LastElementChild => Children.Count > 0 ? Children[^1] : null;

    /// <summary>子元素个数（对齐 <c>childElementCount</c>）。</summary>
    public int ChildElementCount => Children.Count;

    /// <summary>
    /// 布局后的几何（Square 扩展：位置与尺寸）。
    /// 接近 <c>getBoundingClientRect()</c> 的结果缓存，非 Web 只读属性。
    /// </summary>
    public Rect Geometry
    {
        get => _geometry;
        set
        {
            if (_geometry == value) return;
            _geometry = value;
            InvalidatePaint();
        }
    }

    /// <summary>当前滚动偏移（Square 扩展）。</summary>
    public Point ScrollOffset => _scrollOffset;
    /// <summary>滚动内容总尺寸（Square 扩展）。</summary>
    public Size ScrollContentSize => _scrollContentSize;

    /// <summary>水平滚动位置（对齐 <c>scrollLeft</c>）。</summary>
    public float ScrollLeft
    {
        get => _scrollOffset.X;
        set => SetScrollOffset(value, _scrollOffset.Y);
    }

    /// <summary>垂直滚动位置（对齐 <c>scrollTop</c>）。</summary>
    public float ScrollTop
    {
        get => _scrollOffset.Y;
        set => SetScrollOffset(_scrollOffset.X, value);
    }

    /// <summary>是否参与布局与命中（Square 扩展；可映射 CSS 可见性）。</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            var wasEffectivelyVisible = IsEffectivelyVisible;
            _isVisible = value;
            OnIsVisibleChanged(value);
            NotifyEffectiveVisibilityChanged(wasEffectivelyVisible, IsEffectivelyVisible);
            InvalidateLayout();
        }
    }

    /// <summary>当前元素及其全部祖先均可见时为 true。</summary>
    public bool IsEffectivelyVisible
    {
        get
        {
            for (Element? current = this; current != null; current = current.Parent)
                if (!current.IsVisible) return false;
            return true;
        }
    }

    /// <summary>交互/伪类状态位（Square 扩展，供 CSS 伪类匹配）。</summary>
    public ElementState State { get; private set; }

    /// <summary>设置或清除状态标志（Square 扩展）。</summary>
    public void SetState(ElementState flag, bool on)
    {
        var previous = State;
        if (on) State |= flag;
        else State &= ~flag;
        if (State == previous) return;
        OnStateChanged(flag, on);
        if (flag == ElementState.Hover)
        {
            InvalidateStyle();
            if (RequiresStatePaintInvalidation(flag)) InvalidatePaint();
        }
        else
            Invalidate(ElementInvalidation.Style);
    }

    /// <summary>交互/伪类状态变化扩展点。</summary>
    protected virtual void OnStateChanged(ElementState flag, bool on) { }

    /// <summary>状态切换是否直接影响元素自绘；外部自定义元素默认保持原有自动重绘行为。</summary>
    protected virtual bool RequiresStatePaintInvalidation(ElementState flag) =>
        flag != ElementState.Hover || GetType().Assembly != typeof(Element).Assembly;

    /// <summary>可见性变化扩展点。</summary>
    protected virtual void OnIsVisibleChanged(bool isVisible) { }

    /// <summary>当前元素或祖先的可见性导致实际可见状态变化时触发。</summary>
    protected virtual void OnEffectiveVisibilityChanged(bool isVisible) { }

    private void NotifyEffectiveVisibilityChanged(bool wasEffectivelyVisible, bool isEffectivelyVisible)
    {
        if (wasEffectivelyVisible != isEffectivelyVisible)
            OnEffectiveVisibilityChanged(isEffectivelyVisible);
        foreach (var child in Children)
            child.NotifyEffectiveVisibilityChanged(
                wasEffectivelyVisible && child.IsVisible,
                isEffectivelyVisible && child.IsVisible);
    }

    /// <summary>是否包含指定状态标志（Square 扩展）。</summary>
    public bool HasState(ElementState flag) => State.Has(flag);

    /// <summary>是否已挂载到活动文档树（Square 生命周期）。</summary>
    public bool IsAttached { get; private set; }

    /// <summary>是否已完成加载（Square 生命周期）。</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>承载当前文档的应用窗口。</summary>
    public AppWindow? AppWindow => (OwnerDocument as UIDocument)?.AppWindow;

    /// <summary>当前文档的 Store 作用域。</summary>
    public StoreScope Stores => (OwnerDocument as UIDocument)?.Context.Stores
        ?? throw new InvalidOperationException("The element is not owned by a UIDocument.");

    internal Reconciler Reconciler => (OwnerDocument as UIDocument)?.Context.Reconciler
        ?? Square.UI.Reconciler.Current;

    internal Dispatcher? Dispatcher => (OwnerDocument as UIDocument)?.Context.Dispatcher;

    /// <summary>初始化样式、类列表与子节点集合。</summary>
    protected Element()
    {
        Style = new StyleAccessor(this);
        ClassList = new ClassListAccessor(this);
        ChildNodes = new ChildNodeCollection(this);
        Children = new ChildrenCollection(ChildNodes);
    }

    /// <summary>
    /// 追加子元素（对齐 <c>appendChild</c>）。
    /// 已有父节点时抛出 <see cref="InvalidOperationException"/>。
    /// </summary>
    /// <returns>被追加的 <paramref name="child"/>。</returns>
    public Element AppendChild(Element child)
    {
        ArgumentNullException.ThrowIfNull(child);
        ChildNodes.Add(child);
        return child;
    }

    /// <summary>追加子节点（对齐 <c>appendChild</c>）。</summary>
    public Node AppendChild(Node child)
    {
        ArgumentNullException.ThrowIfNull(child);
        ChildNodes.Add(child);
        return child;
    }

    /// <summary>
    /// 在参考子节点之前插入（对齐 <c>insertBefore</c>）。
    /// <paramref name="referenceChild"/> 为 null 时等价于 <see cref="AppendChild"/>。
    /// </summary>
    public Element InsertBefore(Element newChild, Element? referenceChild)
    {
        ArgumentNullException.ThrowIfNull(newChild);
        if (referenceChild == null)
            return AppendChild(newChild);
        ChildNodes.InsertBefore(newChild, referenceChild);
        return newChild;
    }

    /// <summary>在参考子节点之前插入子节点（对齐 <c>insertBefore</c>）。</summary>
    public Node InsertBefore(Node newChild, Node? referenceChild)
    {
        ArgumentNullException.ThrowIfNull(newChild);
        if (referenceChild == null)
            return AppendChild(newChild);
        ChildNodes.InsertBefore(newChild, referenceChild);
        return newChild;
    }

    /// <summary>移除子元素（对齐 <c>removeChild</c>）；非本节点子元素时抛错。</summary>
    /// <returns>被移除的 <paramref name="child"/>。</returns>
    public Element RemoveChild(Element child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!ChildNodes.Remove(child))
            throw new InvalidOperationException("The node to be removed is not a child of this element.");
        return child;
    }

    /// <summary>移除子节点（对齐 <c>removeChild</c>）。</summary>
    public Node RemoveChild(Node child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!ChildNodes.Remove(child))
            throw new InvalidOperationException("The node to be removed is not a child of this element.");
        return child;
    }

    /// <summary>用新子节点列表替换全部子节点（对齐 <c>replaceChildren</c> 简化版）。</summary>
    public void ReplaceChildren(params Element[] nodes)
    {
        ChildNodes.Clear();
        if (nodes is { Length: > 0 })
            ChildNodes.AddRange(nodes);
    }

    /// <summary>用新子节点列表替换全部子节点（对齐 <c>replaceChildren</c>）。</summary>
    public void ReplaceChildren(params Node[] nodes)
    {
        ChildNodes.Clear();
        if (nodes is { Length: > 0 })
            ChildNodes.AddRange(nodes);
    }

    /// <summary>
    /// 返回布局后的边界矩形（对齐 <c>getBoundingClientRect</c>；当前返回 <see cref="Geometry"/> 副本语义）。
    /// </summary>
    public Rect GetBoundingClientRect() => Geometry;

    /// <summary>读取强类型属性（Square 扩展）。</summary>
    public T? GetProperty<T>(string name)
    {
        if (Properties.TryGetValue(name, out T value)) return value;
        return default;
    }

    /// <summary>写入强类型属性并触发变更通知与重绘（Square 扩展）。</summary>
    public void SetProperty<T>(string name, T value)
    {
        if (TrySetSpecialBoundValue(name, value)) return;
        Properties.SetValue(name, value);
        OnPropertyChanged(name);
        ((IComponentLifecycle)this).OnPropChanged(name);
        Invalidate(PropertyInvalidation.ForProperty(name));
    }

    /// <summary>移除属性值并触发与写入相同的变更通知链。</summary>
    public bool RemoveProperty(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Properties.RemoveValue(name)) return false;
        Properties.UnmarkBound(name);
        OnPropertyChanged(name);
        ((IComponentLifecycle)this).OnPropChanged(name);
        Invalidate(PropertyInvalidation.ForProperty(name));
        return true;
    }

    /// <summary>用委托取值写入属性（Square 扩展；用于表达式绑定）。</summary>
    public void BindProperty<T>(string name, Func<T> getter)
    {
        var value = getter();
        if (TrySetSpecialBoundValue(name, value)) return;
        Properties.MarkBound(name);
        Properties.SetValue(name, value);
        OnPropertyChanged(name);
        ((IComponentLifecycle)this).OnPropChanged(name);
        Invalidate(PropertyInvalidation.ForProperty(name));
    }

    /// <summary>用委托取值写入属性，并订阅多个响应源同步更新（Square 扩展）。</summary>
    public void BindProperty<T>(string name, Func<T> getter, params IReactiveSource[] sources)
    {
        ArgumentNullException.ThrowIfNull(getter);
        Properties.MarkBound(name);
        void Update() => SetBoundValue(name, getter());
        Update();
        foreach (var source in sources.Distinct())
            _bindings.Add(source.SubscribeChanged(Update, new ReactiveSubscriptionOptions { Dispatcher = Dispatcher }));
    }

    /// <summary>订阅 <see cref="ObservableValue{T}"/> 并同步到属性（Square 扩展）。</summary>
    public void BindProperty<T>(string name, ObservableValue<T> source)
    {
        Properties.MarkBound(name);
        SetBoundValue(name, source.Value);
        _bindings.Add(source.Subscribe(value => SetBoundValue(name, value)));
    }

    /// <summary>订阅任意响应值，并在元素所属 UI Dispatcher 上同步属性。</summary>
    public void BindProperty<T>(string name, IReactiveValue<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Properties.MarkBound(name);
        SetBoundValue(name, source.Value);
        var target = new WeakReference<Element>(this);
        IDisposable? subscription = null;
        subscription = source.Subscribe(
            value =>
            {
                if (target.TryGetTarget(out var element))
                    element.SetBoundValue(name, value);
                else
                    subscription?.Dispose();
            },
            new ReactiveSubscriptionOptions { Dispatcher = Dispatcher });
        _bindings.Add(subscription!);
    }

    private void SetBoundValue<T>(string name, T value)
    {
        if (TrySetSpecialBoundValue(name, value)) return;
        Properties.SetValue(name, value);
        OnPropertyChanged(name);
        ((IComponentLifecycle)this).OnPropChanged(name);
        Invalidate(PropertyInvalidation.ForProperty(name));
    }

    private bool TrySetSpecialBoundValue<T>(string name, T value)
    {
        if (string.Equals(name, "class", StringComparison.OrdinalIgnoreCase))
        {
            ClassList.Clear();
            var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            foreach (var className in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                ClassList.Add(className);
            return true;
        }
        if (string.Equals(name, "style", StringComparison.OrdinalIgnoreCase))
        {
            Style.CssText = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            return true;
        }
        return false;
    }

    /// <summary>登记与此元素子树同生命周期的生成资源。</summary>
    public void RegisterGeneratedResource(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        (_generatedResources ??= []).Add(resource);
    }

    /// <summary>
    /// 永久丢弃此生成子树并释放响应式绑定与生成资源。
    /// 普通移除和重新挂载不会调用此方法。
    /// </summary>
    public void DiscardGeneratedSubtree()
    {
        var children = Children.ToArray();

        if (_generatedResources != null)
        {
            for (var i = _generatedResources.Count - 1; i >= 0; i--)
                _generatedResources[i].Dispose();
            _generatedResources.Clear();
        }

        for (var i = _bindings.Count - 1; i >= 0; i--)
            _bindings[i].Dispose();
        _bindings.Clear();

        foreach (var child in children)
            child.DiscardGeneratedSubtree();
    }

    /// <summary>
    /// 命中测试：返回包含指定点的最上层后代，或自身（Square 扩展，类似 document.elementFromPoint 的节点侧实现）。
    /// </summary>
    public virtual Element? HitTest(Point point)
    {
        if (!IsVisible || !IsCssDisplayed()) return null;
        var visibilityHidden = IsCssVisibilityHidden();
        var inside = Geometry.Contains(point);
        if (!inside && ClipsOverflowAt(point)) return null;

        var childPoint = MapsScrollOffsetForChildren()
            ? new Point(point.X + _scrollOffset.X, point.Y + _scrollOffset.Y)
            : point;

        var orderedChildren = GetHitTestChildren();
        for (var i = 0; i < orderedChildren.Length; i++)
        {
            var child = orderedChildren[i].Element;
            if (child is IPopupElement { IsPopupOpen: false }) continue;
            var hit = child.HitTest(childPoint);
            if (hit != null) return hit;
        }

        return inside && !visibilityHidden ? this : null;
    }

    /// <summary>当前元素及其祖先是否参与 CSS display 渲染与命中。</summary>
    internal bool IsCssDisplayed()
    {
        for (Element? current = this; current != null; current = current.Parent)
            if (string.Equals(current.Style.Get("display")?.Trim(), "none", StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    internal bool IsFixedPositioned() =>
        string.Equals(Style.Get("position")?.Trim(), "fixed", StringComparison.OrdinalIgnoreCase);

    /// <summary>按最近声明解析 CSS visibility；隐藏自身但不阻止后代覆盖为 visible。</summary>
    internal bool IsCssVisibilityHidden()
    {
        for (Element? current = this; current != null; current = current.Parent)
        {
            var value = current.Style.Get("visibility")?.Trim();
            if (string.IsNullOrEmpty(value)) continue;
            return string.Equals(value, "hidden", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "collapse", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private HitTestEntry[] GetHitTestChildren()
    {
        if (_hitTestChildren != null) return _hitTestChildren;
        _hitTestChildren = Children
            .Select((element, index) => new HitTestEntry(element, index))
            .ToArray();
        Array.Sort(_hitTestChildren, HitTestEntryComparer.Instance);
        return _hitTestChildren;
    }

    internal void InvalidateHitTestOrder() => _hitTestChildren = null;

    private readonly record struct HitTestEntry(Element Element, int Index);

    private sealed class HitTestEntryComparer : IComparer<HitTestEntry>
    {
        public static HitTestEntryComparer Instance { get; } = new();

        public int Compare(HitTestEntry left, HitTestEntry right)
        {
            var zIndex = right.Element.ZIndex.CompareTo(left.Element.ZIndex);
            return zIndex != 0 ? zIndex : right.Index.CompareTo(left.Index);
        }
    }

    /// <summary>是否裁剪溢出内容（由 CSS overflow 推导；渲染用）。</summary>
    public bool ClipsOverflow()
    {
        var (clipX, clipY) = GetOverflowClipAxes();
        return clipX || clipY;
    }

    /// <summary>溢出裁剪矩形（渲染用）。</summary>
    public Rect GetOverflowClipRect()
    {
        var (clipX, clipY) = GetOverflowClipAxes();
        if (!clipX && !clipY) return Rect.Empty;
        const float unbounded = 1_000_000f;
        return new Rect(
            clipX ? Geometry.X : -unbounded,
            clipY ? Geometry.Y : -unbounded,
            clipX ? Geometry.Width : unbounded * 2,
            clipY ? Geometry.Height : unbounded * 2);
    }

    private bool ClipsOverflowAt(Point point)
    {
        var (clipX, clipY) = GetOverflowClipAxes();
        return clipX && (point.X < Geometry.Left || point.X > Geometry.Right) ||
            clipY && (point.Y < Geometry.Top || point.Y > Geometry.Bottom);
    }

    private (bool clipX, bool clipY) GetOverflowClipAxes()
    {
        var isTable = IsTableFormattingBox();
        if (!IsOverflowContainer() && !isTable) return (false, false);
        var overflow = Style.Get("overflow");
        var clipBoth = ClipsOverflowValue(overflow, isTable);
        return (clipBoth || ClipsOverflowValue(Style.Get("overflow-x"), isTable),
            clipBoth || ClipsOverflowValue(Style.Get("overflow-y"), isTable));
    }

    private bool IsTableFormattingBox()
    {
        var display = Style.Get("display")?.Trim().ToLowerInvariant();
        return display is "table" or "inline-table";
    }

    private static bool ClipsOverflowValue(string? value, bool isTable) =>
        IsClippingOverflow(value) && (!isTable || !IsScrollingOverflow(value));

    private static bool IsClippingOverflow(string? value) =>
        string.Equals(value, "hidden", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "clip", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "scroll", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase);

    /// <summary>是否为滚动容器（由 CSS overflow 推导）。</summary>
    public bool IsScrollContainer()
    {
        if (!IsOverflowContainer()) return false;
        var (scrollX, scrollY) = GetScrollAxes();
        return scrollX || scrollY;
    }

    private bool IsOverflowContainer()
    {
        var display = Style.Get("display")?.Trim().ToLowerInvariant();
        return display is null or "" or "block" or "inline-block" or "flow-root" or "flex" or "grid";
    }

    /// <summary>当前元素或祖先的 <c>user-select</c> 是否允许文本选择。</summary>
    public bool IsUserSelectText()
    {
        for (var current = this; current != null; current = current.Parent)
        {
            var value = current.Style.Get("user-select")?.Trim();
            if (string.Equals(value, "text", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)) return false;
        }

        return false;
    }

    /// <summary>是否需要为子元素映射滚动偏移。</summary>
    public bool MapsScrollOffsetForChildren() =>
        IsScrollContainer() && (_scrollOffset.X != 0 || _scrollOffset.Y != 0);

    /// <summary>判断指定方向增量是否仍可滚动。</summary>
    public bool CanScroll(float deltaX, float deltaY)
    {
        var (maxX, maxY) = GetMaxScrollOffset();
        var (scrollX, scrollY) = GetScrollAxes();
        return scrollX && (deltaX < 0 && _scrollOffset.X > 0 || deltaX > 0 && _scrollOffset.X < maxX) ||
            scrollY && (deltaY < 0 && _scrollOffset.Y > 0 || deltaY > 0 && _scrollOffset.Y < maxY);
    }

    /// <summary>按增量滚动；返回是否实际发生滚动。</summary>
    public bool ScrollBy(float deltaX, float deltaY)
    {
        var old = _scrollOffset;
        SetScrollOffset(_scrollOffset.X + deltaX, _scrollOffset.Y + deltaY);
        return old.X != _scrollOffset.X || old.Y != _scrollOffset.Y;
    }

    /// <summary>设置滚动内容总尺寸。</summary>
    public void SetScrollContentSize(Size size)
    {
        _scrollContentSize = new Size(Math.Max(0, size.Width), Math.Max(0, size.Height));
        SetScrollOffset(_scrollOffset.X, _scrollOffset.Y);
    }

    private void SetScrollOffset(float x, float y)
    {
        var (maxX, maxY) = GetMaxScrollOffset();
        var (scrollX, scrollY) = GetScrollAxes();
        if (!scrollX) x = 0;
        if (!scrollY) y = 0;
        x = Math.Clamp(float.IsNaN(x) ? 0 : x, 0, maxX);
        y = Math.Clamp(float.IsNaN(y) ? 0 : y, 0, maxY);
        if (Math.Abs(_scrollOffset.X - x) < 0.01f && Math.Abs(_scrollOffset.Y - y) < 0.01f) return;
        _scrollOffset = new Point(x, y);
        InvalidatePaint();
        DispatchEvent(StandardEvents.CreateScroll());
    }

    private (float maxX, float maxY) GetMaxScrollOffset() =>
        (Math.Max(0, _scrollContentSize.Width - Geometry.Width),
            Math.Max(0, _scrollContentSize.Height - Geometry.Height));

    private static bool IsScrollingOverflow(string? value) =>
        string.Equals(value, "scroll", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase);

    private (bool scrollX, bool scrollY) GetScrollAxes()
    {
        var overflow = Style.Get("overflow");
        var scrollBoth = IsScrollingOverflow(overflow);
        return (scrollBoth || IsScrollingOverflow(Style.Get("overflow-x")),
            scrollBoth || IsScrollingOverflow(Style.Get("overflow-y")));
    }

    /// <summary>
    /// 按类型与可选 class 查询第一个匹配后代（Square 强类型查询；接近 <c>querySelector</c>）。
    /// </summary>
    public T? Query<T>(string? className = null) where T : Element
    {
        return QueryInternal<T>(className);
    }

    /// <summary>
    /// 按类型与可选 class 查询所有匹配后代（Square 强类型查询；接近 <c>querySelectorAll</c>）。
    /// </summary>
    public List<T> QueryAll<T>(string? className = null) where T : Element
    {
        var result = new List<T>();
        QueryAllInternal(className, result);
        return result;
    }

    /// <summary>
    /// 按 CSS 选择器子集查找第一个匹配后代（对齐 <c>querySelector</c>；不含自身）。
    /// 支持标签、<c>#id</c>、<c>.class</c>、后代、子代 <c>&gt;</c>、逗号列表。
    /// </summary>
    public Element? QuerySelector(string selectors) =>
        CssSelector.QuerySelector(this, selectors, includeRoot: false);

    /// <summary>
    /// 按 CSS 选择器子集查找所有匹配后代（对齐 <c>querySelectorAll</c>；不含自身）。
    /// </summary>
    public List<Element> QuerySelectorAll(string selectors) =>
        CssSelector.QuerySelectorAll(this, selectors, includeRoot: false);

    private T? QueryInternal<T>(string? className) where T : Element
    {
        if (this is T typed && (className == null || ClassList.Contains(className)))
            return typed;
        foreach (var child in Children)
        {
            var found = child.QueryInternal<T>(className);
            if (found != null) return found;
        }
        return null;
    }

    private void QueryAllInternal<T>(string? className, List<T> result) where T : Element
    {
        if (this is T typed && (className == null || ClassList.Contains(className)))
            result.Add(typed);
        foreach (var child in Children)
            child.QueryAllInternal(className, result);
    }

    /// <summary>标记布局与绘制失效，并向父级传播布局脏（Square 扩展）。</summary>
    public void InvalidateLayout()
    {
        _isLayoutDirty = true;
        _needsPaint = true;
        _paintFullDirty = true;
        _paintDirtyRects?.Clear();
        if (Parent != null)
            Parent.InvalidateLayout();
        else
            RequestRenderIfAttached();
    }

    /// <summary>仅标记绘制失效（Square 扩展；整控件脏）。</summary>
    public virtual void InvalidatePaint()
    {
        _needsPaint = true;
        _paintFullDirty = true;
        _paintDirtyRects?.Clear();
        RequestRenderIfAttached();
    }

    /// <summary>
    /// 标记局部绘制失效（Square 扩展）。
    /// 命令仍会整节点重建，但 DisplayTree 可只 Present 这些矩形（用于光标闪烁等）。
    /// <paramref name="localRect"/> 为相对本节点 Geometry 的局部坐标。
    /// </summary>
    public virtual void InvalidatePaint(Rect localRect)
    {
        if (localRect.IsEmpty)
        {
            InvalidatePaint();
            return;
        }
        if (_paintFullDirty)
        {
            _needsPaint = true;
            RequestRenderIfAttached();
            return;
        }
        _needsPaint = true;
        _paintDirtyRects ??= [];
        _paintDirtyRects.Add(localRect);
        RequestRenderIfAttached();
    }

    private void RequestRenderIfAttached()
    {
        AppWindow?.RequestRenderIfBound();
    }

    /// <summary>是否已标记为整控件绘制脏。</summary>
    public bool IsPaintFullDirty => _paintFullDirty;

    /// <summary>局部绘制脏矩形（相对 Geometry；整控件脏时为空）。</summary>
    public IReadOnlyList<Rect> PaintDirtyRects =>
        _paintFullDirty || _paintDirtyRects == null ? Array.Empty<Rect>() : _paintDirtyRects;

    /// <summary>按失效标志触发对应的重绘/重布局/样式失效流程（Square 扩展）。</summary>
    public void Invalidate(ElementInvalidation invalidation)
    {
        if (_invalidationSuppressionDepth > 0) return;

        if ((invalidation & ElementInvalidation.Style) != 0)
            StyleInvalidated?.Invoke(this);

        if ((invalidation & ElementInvalidation.Layout) != 0)
        {
            InvalidateLayout();
            return;
        }
        if ((invalidation & (ElementInvalidation.Paint | ElementInvalidation.Style | ElementInvalidation.DisplayTree)) != 0)
            InvalidatePaint();
    }

    private void InvalidateStyle()
    {
        if (_invalidationSuppressionDepth > 0) return;
        StyleInvalidated?.Invoke(this);
    }

    internal static IDisposable SuppressInvalidation()
    {
        _invalidationSuppressionDepth++;
        return new InvalidationSuppression();
    }

    private sealed class InvalidationSuppression : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _invalidationSuppressionDepth--;
        }
    }

    /// <summary>
    /// 标记此元素需要协调（结构或属性变更），由 Reconciler 在下次 flush 时统一处理。
    /// 调用方在批量修改属性/子节点后调用此方法，避免每次修改都即时触发重绘。
    /// </summary>
    public void ScheduleReconcile()
    {
        Reconciler.MarkDirty(this);
    }

    /// <summary>清除布局脏标记（由布局引擎调用）。</summary>
    public void ClearLayoutDirty() => _isLayoutDirty = false;

    /// <summary>清除绘制脏标记（由 DisplayTree 在收集命令后调用）。</summary>
    public void ClearPaintDirty()
    {
        _needsPaint = false;
        _paintFullDirty = false;
        _paintDirtyRects?.Clear();
    }

    /// <summary>属性变更时的扩展点。</summary>
    protected virtual void OnPropertyChanged(string name) { }

    /// <summary>子节点加入时的内部通知。</summary>
    internal virtual void OnChildAdded(Element child) { }

    /// <summary>子节点移除时的内部通知。</summary>
    internal virtual void OnChildRemoved(Element child) { }

    /// <summary>是否拥有自定义 Measure 实现（供布局引擎判断是否需要调用）。</summary>
    public virtual bool HasCustomMeasure => false;

    /// <summary>测量期望尺寸（Square 布局协议）。</summary>
    public virtual Size Measure(Size availableSize) => Size.Zero;

    /// <summary>在最终矩形内排列自身（Square 布局协议）。</summary>
    public virtual void Arrange(Rect finalRect) { Geometry = finalRect; }

    /// <summary>向渲染上下文绘制本节点（Square 扩展；由 DisplayTree 经 CommandCollector 调用）。</summary>
    public virtual void Paint(IRenderContext ctx) { }

    /// <summary>构建元素子树（由 Source Generator 重写；组件初始化入口）。</summary>
    public virtual void BuildElementTree() { }

    /// <summary>Props 变更钩子（组件生命周期）。</summary>
    protected virtual void OnPropChanged(string name) { }

    protected override void OnDefaultAction(Event e)
    {
        if (e is not WheelEvent wheel) return;
        for (Element? current = this; current != null; current = current.Parent)
        {
            if (!current.IsScrollContainer() || !current.CanScroll(wheel.DeltaX, wheel.DeltaY)) continue;
            current.ScrollBy(wheel.DeltaX, wheel.DeltaY);
            e.PreventDefault();
            return;
        }
    }

    /// <summary>挂载完成钩子。</summary>
    protected virtual void OnAttachedCore() { }

    /// <summary>卸载完成钩子。</summary>
    protected virtual void OnDetachedCore() { }

    /// <summary>生成代码专用的卸载清理钩子，在用户卸载钩子之前执行。</summary>
    protected virtual void OnGeneratedDetachedCore() { }

    void IComponentLifecycle.OnPropChanged(string name) => OnPropChanged(name);

    void IComponentLifecycle.OnAttached()
    {
        if (IsAttached) return;
        IsAttached = true;
        OnAttachedCore();
        foreach (var child in Children) ((IComponentLifecycle)child).OnAttached();
    }

    void IComponentLifecycle.OnDetached()
    {
        if (!IsAttached) return;
        foreach (var child in Children) ((IComponentLifecycle)child).OnDetached();
        OnGeneratedDetachedCore();
        OnDetachedCore();
        IsAttached = false;
    }

    void IComponentLifecycle.OnLoaded()
    {
        IsLoaded = true;
        foreach (var child in Children) ((IComponentLifecycle)child).OnLoaded();
    }

    void IComponentLifecycle.OnUnloaded()
    {
        IsLoaded = false;
        foreach (var child in Children) ((IComponentLifecycle)child).OnUnloaded();
    }

    void ILayoutLifecycle.OnMeasure() => Measure(_geometry.Size);
    void ILayoutLifecycle.OnArrange() => Arrange(_geometry);
}
