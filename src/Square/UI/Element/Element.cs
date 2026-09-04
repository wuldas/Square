using System.Diagnostics;
using System.Globalization;
using Square.Events;
using Square.Graphics;
using Square.Runtime;
using Square.Runtime.Binding;
using Square.Runtime.State;
using Square.Hosting;
using Square.UI.ElementApi;
using Square.UI.Properties;
using Square.UI.Scrolling;

namespace Square.UI;

/// <summary>
/// 文档树中的元素节点（对齐 DOM <c>Element</c> 身份，并承载 Square 保留模式布局/绘制扩展）。
/// <para>继承：<see cref="EventTarget"/> → <see cref="Node"/> → <see cref="Element"/>。</para>
/// <para>Web API 对应：<c>tagName</c> / <c>id</c> / <c>classList</c> / <c>style</c> / 树关系 / 事件。</para>
/// <para>Square 扩展：<see cref="Geometry"/>、<see cref="Measure"/>/<see cref="Arrange"/>/<see cref="Paint"/>、脏标记与绑定等。</para>
/// </summary>
public abstract class Element : Node, IComponentLifecycle, ILayoutLifecycle, IFrameScheduledElement
{
    private const float SmoothScrollDurationSeconds = 0.18f;
    private const float ScrollbarFadeDelaySeconds = 0.5f;
    private const float ScrollbarFadeDurationSeconds = 0.2f;
    private const float ScrollbarLineStep = 40f;

    private Rect _geometry;
    private bool _isVisible = true;
    private bool _isLayoutDirty = true;
    private bool _needsPaint = true;
    private bool _paintFullDirty = true;
    private List<Rect>? _paintDirtyRects;
    private Size _scrollContentSize;
    private Point _scrollOffset;
    private Point _smoothScrollStart;
    private Point _smoothScrollTarget;
    private float _smoothScrollElapsed;
    private long _smoothScrollLastTimestamp;
    private bool _smoothScrollActive;
    private long _scrollbarFadeLastTimestamp;
    private float _scrollbarFadeElapsed;
    private float _scrollbarOpacity;
    private bool _scrollbarFadeActive;
    private ScrollbarPart _scrollbarInteractionPart;
    private bool _scrollbarRepeatForward;
    private bool _scrollbarRepeatPointerInside;
    private Point _scrollbarRepeatPoint;
    private Point _scrollbarDragStartPoint;
    private Point _scrollbarDragStartOffset;
    private Dictionary<string, ScrollbarPseudoStyleEntry>? _scrollbarPseudoStyles;
    private static long _scrollbarPseudoStyleSequence;
    private int _zIndex;
    private HitTestEntry[]? _hitTestChildren;
    private readonly List<IDisposable> _bindings = [];
    private List<IDisposable>? _generatedResources;
    private int _debugId;

    [ThreadStatic]
    private static int _invalidationSuppressionDepth;

    /// <summary>样式失效时触发的全局事件（Square 扩展）。</summary>
    public static event Action<Element>? StyleInvalidated;

    internal static event Action<Element>? GeneralStyleInvalidated;
    internal static event Action<Element>? HoverStyleInvalidated;

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
    /// <summary>滚动条 chrome 的显示策略。</summary>
    public ScrollbarVisibilityMode ScrollbarVisibility
    {
        get => GetProperty<ScrollbarVisibilityMode?>(nameof(ScrollbarVisibility)) ?? ScrollbarVisibilityMode.Auto;
        set
        {
            var normalized = value is ScrollbarVisibilityMode.Auto or ScrollbarVisibilityMode.Always or
                ScrollbarVisibilityMode.Hover or ScrollbarVisibilityMode.Scroll or ScrollbarVisibilityMode.Hidden
                ? value
                : ScrollbarVisibilityMode.Auto;
            var previous = ScrollbarVisibility;
            if (previous == normalized) return;
            SetProperty(nameof(ScrollbarVisibility), normalized);
            if (normalized is ScrollbarVisibilityMode.Hidden or ScrollbarVisibilityMode.Always or ScrollbarVisibilityMode.Hover ||
                previous == ScrollbarVisibilityMode.Always &&
                (normalized == ScrollbarVisibilityMode.Auto || normalized == ScrollbarVisibilityMode.Scroll))
                CancelScrollbarFade();
        }
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
            InvalidateHoverStyle();
            if (RequiresStatePaintInvalidation(flag)) InvalidatePaint();
        }
        else
            Invalidate(ElementInvalidation.Style);
    }

    /// <summary>交互/伪类状态变化扩展点。</summary>
    protected virtual void OnStateChanged(ElementState flag, bool on) { }

    /// <summary>是否为只承载模板子树的编译器生成组件。</summary>
    protected virtual bool IsGeneratedComponent => false;

    protected virtual bool RequiresStatePaintInvalidation(ElementState flag) =>
        flag != ElementState.Hover || (!IsGeneratedComponent && GetType().Assembly != typeof(Element).Assembly) ||
        ScrollbarVisibility is ScrollbarVisibilityMode.Hover or ScrollbarVisibilityMode.Scroll;

    /// <summary>可见性变化扩展点。</summary>
    protected virtual void OnIsVisibleChanged(bool isVisible) { }

    /// <summary>当前元素或祖先的可见性导致实际可见状态变化时触发。</summary>
    protected virtual void OnEffectiveVisibilityChanged(bool isVisible)
    {
        if (!isVisible) return;
        var now = Stopwatch.GetTimestamp();
        if (_smoothScrollActive) _smoothScrollLastTimestamp = now;
        if (_scrollbarFadeActive) _scrollbarFadeLastTimestamp = now;
        if (_smoothScrollActive || _scrollbarFadeActive)
            DispatchEvent(StandardEvents.CreateRequestFrame());
    }

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
        DiscardGeneratedResources();
        foreach (var child in Children.ToArray())
            child.DiscardGeneratedSubtree();
        Square.CSS.Engine.CssStyleReconciler.ClearPendingForSubtree(this);
    }

    /// <summary>Detaches and permanently discards the generated children while preserving this element.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public void PrepareGeneratedSubtreeRebuild()
    {
        DiscardGeneratedResources();
        var children = Children.ToArray();
        ChildNodes.Clear();
        foreach (var child in children)
            child.DiscardGeneratedSubtree();
        Square.CSS.Engine.CssStyleReconciler.ClearPendingForSubtree(this);
    }

    private void DiscardGeneratedResources()
    {
        if (_generatedResources != null)
        {
            for (var i = _generatedResources.Count - 1; i >= 0; i--)
                _generatedResources[i].Dispose();
            _generatedResources.Clear();
        }

        for (var i = _bindings.Count - 1; i >= 0; i--)
            _bindings[i].Dispose();
        _bindings.Clear();
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
        var viewport = GetScrollbarMetrics().ViewportRect;
        return new Rect(
            clipX ? viewport.X : -unbounded,
            clipY ? viewport.Y : -unbounded,
            clipX ? viewport.Width : unbounded * 2,
            clipY ? viewport.Height : unbounded * 2);
    }

    private bool ClipsOverflowAt(Point point)
    {
        var (clipX, clipY) = GetOverflowClipAxes();
        var viewport = GetScrollbarMetrics().ViewportRect;
        return clipX && (point.X < viewport.Left || point.X > viewport.Right) ||
            clipY && (point.Y < viewport.Top || point.Y > viewport.Bottom);
    }

    private (bool clipX, bool clipY) GetOverflowClipAxes()
    {
        var isTable = IsTableFormattingBox();
        if (!IsOverflowContainer() && !isTable) return (false, false);
        var tableHasScrollOverflow = isTable && HasActualTableScrollOverflow();
        var overflow = Style.Get("overflow");
        var tableOverflow = isTable && !tableHasScrollOverflow;
        var clipBoth = ClipsOverflowValue(overflow, tableOverflow);
        return (clipBoth || ClipsOverflowValue(Style.Get("overflow-x"), tableOverflow),
            clipBoth || ClipsOverflowValue(Style.Get("overflow-y"), tableOverflow));
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
        if (!IsOverflowContainer() && !IsTableFormattingBox()) return false;
        var (scrollX, scrollY) = GetScrollAxes();
        return (scrollX || scrollY) &&
            (!IsTableFormattingBox() || HasActualTableScrollOverflow());
    }

    private bool IsOverflowContainer()
    {
        var display = Style.Get("display")?.Trim().ToLowerInvariant();
        return display is null or "" or "block" or "inline-block" or "flow-root" or "flex" or "grid";
    }

    internal bool IsScrollLayoutContainer() => IsOverflowContainer() || IsTableFormattingBox();

    private bool HasActualTableScrollOverflow()
    {
        if (!IsTableFormattingBox()) return false;
        var axes = GetScrollAxes();
        var viewport = GetScrollbarMetrics().ViewportRect;
        return axes.scrollX && _scrollContentSize.Width > viewport.Width + 0.01f ||
            axes.scrollY && _scrollContentSize.Height > viewport.Height + 0.01f;
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

    /// <summary>获取当前滚动容器的统一 scrollbar 几何。</summary>
    internal ScrollbarMetrics GetScrollbarMetrics()
    {
        var axes = GetScrollAxes();
        var canExposeScrollbar = IsScrollLayoutContainer();
        var profile = AppWindow?.ScrollbarProfile ?? ScrollbarDeviceProfile.Auto;
        var width = GetScrollbarWidthMode();
        if (ScrollbarVisibility == ScrollbarVisibilityMode.Hidden || IsScrollbarPseudoDisplayNone())
            width = ScrollbarWidthMode.None;
        var verticalThickness = ParseScrollbarLength(
            GetScrollbarPseudoStyle(ScrollbarPseudoElements.Scrollbar, "", "width"));
        var horizontalThickness = ParseScrollbarLength(
            GetScrollbarPseudoStyle(ScrollbarPseudoElements.Scrollbar, "", "height"));
        return ScrollbarGeometry.Calculate(
            Geometry,
            _scrollContentSize,
            _scrollOffset,
            verticalEnabled: canExposeScrollbar && axes.scrollY,
            horizontalEnabled: canExposeScrollbar && axes.scrollX,
            alwaysShowVertical: IsAlwaysScrolling("overflow-y"),
            alwaysShowHorizontal: IsAlwaysScrolling("overflow-x"),
            profile,
            width,
            GetScrollbarGutterMode(),
            hideButtons: IsScrollbarPseudoDisplayNone(ScrollbarPseudoElements.Button),
            verticalThicknessOverride: verticalThickness,
            horizontalThicknessOverride: horizontalThickness,
            hideThumb: IsScrollbarPseudoDisplayNone(ScrollbarPseudoElements.Thumb),
            hideTrack: IsScrollbarPseudoDisplayNone(ScrollbarPseudoElements.Track) ||
                IsScrollbarPseudoDisplayNone(ScrollbarPseudoElements.TrackPiece),
            hideCorner: IsScrollbarPseudoDisplayNone(ScrollbarPseudoElements.Corner));
    }
    /// <summary>当前滚动内容可用的视口矩形；desktop gutter 已从中扣除。</summary>
    protected internal Rect GetScrollViewportRect() => GetScrollbarMetrics().ViewportRect;

    /// <summary>当前 scrollbar chrome 是否应绘制。</summary>
    internal bool IsScrollbarChromeVisible
    {
        get
        {
            var metrics = GetScrollbarMetrics();
            if ((!metrics.HasVertical && !metrics.HasHorizontal) ||
                GetScrollbarWidthMode() == ScrollbarWidthMode.None ||
                ScrollbarVisibility == ScrollbarVisibilityMode.Hidden ||
                IsScrollbarPseudoDisplayNone())
                return false;

            var hovered = HasState(ElementState.Hover) || ScrollbarHoverPart != ScrollbarPart.None;
            return ScrollbarVisibility switch
            {
                ScrollbarVisibilityMode.Always => true,
                ScrollbarVisibilityMode.Hover => hovered,
                ScrollbarVisibilityMode.Scroll => hovered || _scrollbarOpacity > 0.001f,
                _ => metrics.IsOverlay ? _scrollbarOpacity > 0.001f : true
            };
        }
    }

    /// <summary>当前 scrollbar chrome 的绘制不透明度。</summary>
    internal float ScrollbarOpacity
    {
        get
        {
            var metrics = GetScrollbarMetrics();
            if (ScrollbarVisibility == ScrollbarVisibilityMode.Hidden ||
                GetScrollbarWidthMode() == ScrollbarWidthMode.None || IsScrollbarPseudoDisplayNone())
                return 0;
            if (ScrollbarVisibility == ScrollbarVisibilityMode.Always ||
                ScrollbarVisibility == ScrollbarVisibilityMode.Auto && !metrics.IsOverlay)
                return 1;
            if (ScrollbarVisibility is ScrollbarVisibilityMode.Hover or ScrollbarVisibilityMode.Scroll &&
                (HasState(ElementState.Hover) || ScrollbarHoverPart != ScrollbarPart.None))
                return 1;
            return Math.Clamp(_scrollbarOpacity, 0, 1);
        }
    }

    internal ScrollbarPart ScrollbarInteractionPart =>
        _scrollbarInteractionPart is ScrollbarPart.VerticalThumb or ScrollbarPart.HorizontalThumb ||
        _scrollbarRepeatPointerInside
            ? _scrollbarInteractionPart
            : ScrollbarPart.None;
    internal ScrollbarPart ScrollbarHoverPart { get; private set; }

    internal Size GetReservedScrollbarGutter()
    {
        var insets = GetReservedScrollbarInsets();
        return new Size(insets.Left + insets.Right, insets.Top + insets.Bottom);
    }

    internal (float Left, float Top, float Right, float Bottom) GetReservedScrollbarInsets()
    {
        var metrics = GetScrollbarMetrics();
        if (metrics.IsOverlay) return (0, 0, 0, 0);
        var bothEdges = GetScrollbarGutterMode() == ScrollbarGutterMode.StableBothEdges;
        var left = metrics.ReservesVerticalGutter && bothEdges ? metrics.VerticalScrollbarThickness : 0;
        var top = metrics.ReservesHorizontalGutter && bothEdges ? metrics.HorizontalScrollbarThickness : 0;
        var right = metrics.ReservesVerticalGutter ? metrics.VerticalScrollbarThickness : 0;
        var bottom = metrics.ReservesHorizontalGutter ? metrics.HorizontalScrollbarThickness : 0;
        return (left, top, right, bottom);
    }

    internal bool ClearScrollbarPseudoStyles()
    {
        if (_scrollbarPseudoStyles is not { Count: > 0 }) return false;
        _scrollbarPseudoStyles.Clear();
        return true;
    }

    internal bool SetScrollbarPseudoStyle(
        string pseudoElement,
        string state,
        string property,
        string value,
        CssSpecificity specificity,
        bool important,
        CssCascadeOrigin origin)
    {
        if (!ScrollbarPseudoElements.IsSupported(pseudoElement)) return false;
        property = StyleAccessor.NormalizePropertyName(property);
        value = value.Trim();
        if (property.Length == 0 || value.Length == 0) return false;
        var candidate = new ScrollbarPseudoStyleEntry(
            value,
            specificity,
            important,
            origin,
            Interlocked.Increment(ref _scrollbarPseudoStyleSequence));
        var key = ScrollbarPseudoStyleKey(pseudoElement, state, property);
        _scrollbarPseudoStyles ??= new(StringComparer.Ordinal);
        if (_scrollbarPseudoStyles.TryGetValue(key, out var current) && current.ComparePriority(candidate) > 0)
            return false;
        if (_scrollbarPseudoStyles.TryGetValue(key, out current) && current.SameValueAndPriority(candidate))
            return false;
        _scrollbarPseudoStyles[key] = candidate;
        return true;
    }

    internal string? GetScrollbarPseudoStyle(string pseudoElement, string state, string property)
    {
        if (_scrollbarPseudoStyles == null) return null;
        var states = state.ToLowerInvariant() switch
        {
            "active" => new[] { "active", "hover", "" },
            "hover" => new[] { "hover", "" },
            _ => new[] { state.ToLowerInvariant() }
        };
        ScrollbarPseudoStyleEntry? best = null;
        foreach (var candidateState in states)
        {
            var key = ScrollbarPseudoStyleKey(pseudoElement, candidateState, property);
            if (!_scrollbarPseudoStyles.TryGetValue(key, out var candidate)) continue;
            if (best == null || candidate.ComparePriority(best.Value) > 0)
                best = candidate;
        }
        return best is { } entry ? Style.ResolveValue(entry.Value) : null;
    }

    internal bool HasScrollbarPseudoStyles => _scrollbarPseudoStyles is { Count: > 0 };

    internal bool HasScrollbarPseudoStylesFor(string pseudoElement) =>
        _scrollbarPseudoStyles?.Keys.Any(key => key.StartsWith(
            $"{pseudoElement.ToLowerInvariant()}\u001f", StringComparison.Ordinal)) == true;

    private bool IsScrollbarPseudoDisplayNone(string pseudoElement = ScrollbarPseudoElements.Scrollbar) =>
        string.Equals(GetScrollbarPseudoStyle(pseudoElement, "", "display")?.Trim(), "none", StringComparison.OrdinalIgnoreCase);

    private static float? ParseScrollbarLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase)) text = text[..^2].Trim();
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) &&
               float.IsFinite(result) && result >= 0
            ? result
            : null;
    }

    private static string ScrollbarPseudoStyleKey(string pseudoElement, string state, string property) =>
        $"{pseudoElement.ToLowerInvariant()}\u001f{state.ToLowerInvariant()}\u001f{StyleAccessor.NormalizePropertyName(property)}";

    private readonly record struct ScrollbarPseudoStyleEntry(
        string Value,
        CssSpecificity Specificity,
        bool Important,
        CssCascadeOrigin Origin,
        long Sequence)
    {
        public int ComparePriority(ScrollbarPseudoStyleEntry other)
        {
            var important = Important.CompareTo(other.Important);
            if (important != 0) return important;
            var origin = Origin.CompareTo(other.Origin);
            if (origin != 0) return origin;
            var specificity = Specificity.CompareTo(other.Specificity);
            return specificity != 0 ? specificity : Sequence.CompareTo(other.Sequence);
        }

        public bool SameValueAndPriority(ScrollbarPseudoStyleEntry other) =>
            Value == other.Value && Important == other.Important && Origin == other.Origin &&
            Specificity == other.Specificity;
    }

    private ScrollbarWidthMode GetScrollbarWidthMode() =>
        Style.Get("scrollbar-width")?.Trim().ToLowerInvariant() switch
        {
            "thin" => ScrollbarWidthMode.Thin,
            "none" => ScrollbarWidthMode.None,
            _ => ScrollbarWidthMode.Auto
        };

    private ScrollbarGutterMode GetScrollbarGutterMode() =>
        Style.Get("scrollbar-gutter")?.Trim().ToLowerInvariant() switch
        {
            "stable" => ScrollbarGutterMode.Stable,
            "stable both-edges" => ScrollbarGutterMode.StableBothEdges,
            _ => ScrollbarGutterMode.Auto
        };

    private bool IsAlwaysScrolling(string property) =>
        IsForcedScrolling(Style.Get("overflow")) || IsForcedScrolling(Style.Get(property));

    private static bool IsForcedScrolling(string? value) =>
        string.Equals(value, "scroll", StringComparison.OrdinalIgnoreCase);

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

    private bool ScrollBySmooth(float deltaX, float deltaY)
    {
        var (maxX, maxY) = GetMaxScrollOffset();
        var (scrollX, scrollY) = GetScrollAxes();
        var origin = _smoothScrollActive ? _smoothScrollTarget : _scrollOffset;
        var targetX = scrollX ? Math.Clamp(origin.X + deltaX, 0, maxX) : 0;
        var targetY = scrollY ? Math.Clamp(origin.Y + deltaY, 0, maxY) : 0;
        if (Math.Abs(origin.X - targetX) < 0.01f && Math.Abs(origin.Y - targetY) < 0.01f)
            return false;

        _smoothScrollStart = _scrollOffset;
        _smoothScrollTarget = new Point(targetX, targetY);
        _smoothScrollElapsed = 0;
        _smoothScrollLastTimestamp = Stopwatch.GetTimestamp();
        _smoothScrollActive = true;
        DispatchEvent(StandardEvents.CreateRequestFrame(TimeSpan.Zero));
        return true;
    }

    internal void AdvanceSmoothScroll(float deltaSeconds)
    {
        if (!_smoothScrollActive) return;

        _smoothScrollElapsed += Math.Max(0, float.IsFinite(deltaSeconds) ? deltaSeconds : 0);
        var progress = Math.Clamp(_smoothScrollElapsed / SmoothScrollDurationSeconds, 0, 1);
        var remaining = 1 - progress;
        var easedProgress = 1 - remaining * remaining * remaining;
        SetScrollOffsetCore(
            _smoothScrollStart.X + (_smoothScrollTarget.X - _smoothScrollStart.X) * easedProgress,
            _smoothScrollStart.Y + (_smoothScrollTarget.Y - _smoothScrollStart.Y) * easedProgress,
            force: progress >= 1);

        if (progress >= 1)
        {
            _smoothScrollActive = false;
            return;
        }

        DispatchEvent(StandardEvents.CreateRequestFrame(TimeSpan.Zero));
    }

    void IFrameScheduledElement.OnFrameDue() => OnFrameDueCore();

    /// <summary>处理宿主调度到期的帧；派生控件应调用基类以保留平滑滚动。</summary>
    protected virtual void OnFrameDueCore()
    {
        var now = Stopwatch.GetTimestamp();
        if (_smoothScrollActive)
        {
            var deltaSeconds = (now - _smoothScrollLastTimestamp) / (float)Stopwatch.Frequency;
            _smoothScrollLastTimestamp = now;
            AdvanceSmoothScroll(deltaSeconds);
        }
        if (_scrollbarFadeActive)
        {
            var deltaSeconds = (now - _scrollbarFadeLastTimestamp) / (float)Stopwatch.Frequency;
            _scrollbarFadeLastTimestamp = now;
            AdvanceScrollbarFade(deltaSeconds);
        }
        if (!_smoothScrollActive && !_scrollbarFadeActive)
            InvalidatePaint();
    }

    /// <summary>设置滚动内容总尺寸。</summary>
    public void SetScrollContentSize(Size size)
    {
        var normalized = new Size(Math.Max(0, size.Width), Math.Max(0, size.Height));
        var oldMetrics = GetScrollbarMetrics();
        _scrollContentSize = normalized;
        SetScrollOffset(_scrollOffset.X, _scrollOffset.Y);
        var newMetrics = GetScrollbarMetrics();
        if (oldMetrics.ViewportRect != newMetrics.ViewportRect)
            InvalidateLayout();
        else if (oldMetrics != newMetrics)
            InvalidatePaint();
    }

    private void SetScrollOffset(float x, float y)
    {
        CancelSmoothScroll();
        SetScrollOffsetCore(x, y);
    }

    private void SetScrollOffsetCore(float x, float y, bool force = false)
    {
        var (maxX, maxY) = GetMaxScrollOffset();
        var (scrollX, scrollY) = GetScrollAxes();
        if (!scrollX) x = 0;
        if (!scrollY) y = 0;
        x = Math.Clamp(float.IsNaN(x) ? 0 : x, 0, maxX);
        y = Math.Clamp(float.IsNaN(y) ? 0 : y, 0, maxY);
        if (_scrollOffset.X == x && _scrollOffset.Y == y ||
            !force && Math.Abs(_scrollOffset.X - x) < 0.01f && Math.Abs(_scrollOffset.Y - y) < 0.01f) return;
        _scrollOffset = new Point(x, y);
        ShowScrollbar();
        InvalidatePaint();
        DispatchEvent(StandardEvents.CreateScroll());
    }

    internal void AdvanceScrollbarFade(float deltaSeconds)
    {
        if (!_scrollbarFadeActive) return;
        _scrollbarFadeElapsed += Math.Max(0, float.IsFinite(deltaSeconds) ? deltaSeconds : 0);
        var nextOpacity = _scrollbarFadeElapsed <= ScrollbarFadeDelaySeconds
            ? 1f
            : 1f - Math.Clamp(
                (_scrollbarFadeElapsed - ScrollbarFadeDelaySeconds) / ScrollbarFadeDurationSeconds,
                0,
                1);
        var changed = Math.Abs(_scrollbarOpacity - nextOpacity) > 0.001f;
        _scrollbarOpacity = nextOpacity;
        if (_scrollbarFadeElapsed >= ScrollbarFadeDelaySeconds + ScrollbarFadeDurationSeconds)
            _scrollbarFadeActive = false;
        if (changed) InvalidatePaint();
        if (_scrollbarFadeActive) DispatchEvent(StandardEvents.CreateRequestFrame());
    }

    internal ScrollbarPart StartScrollbarInteraction(Point point)
    {
        var metrics = GetScrollbarMetrics();
        if (metrics.IsOverlay) return ScrollbarPart.None;
        var part = metrics.HitTest(point);
        if (part is ScrollbarPart.None or ScrollbarPart.Corner) return ScrollbarPart.None;
        _scrollbarInteractionPart = part;
        _scrollbarRepeatPoint = point;
        _scrollbarRepeatPointerInside = true;
        InvalidatePaint();

        switch (part)
        {
            case ScrollbarPart.VerticalThumb:
            case ScrollbarPart.HorizontalThumb:
                CancelSmoothScroll();
                _scrollbarInteractionPart = part;
                _scrollbarDragStartPoint = point;
                _scrollbarDragStartOffset = _scrollOffset;
                break;
            case ScrollbarPart.VerticalBackButton:
                ScrollBy(0, -ScrollbarLineStep);
                break;
            case ScrollbarPart.VerticalForwardButton:
                ScrollBy(0, ScrollbarLineStep);
                break;
            case ScrollbarPart.HorizontalBackButton:
                ScrollBy(-ScrollbarLineStep, 0);
                break;
            case ScrollbarPart.HorizontalForwardButton:
                ScrollBy(ScrollbarLineStep, 0);
                break;
            case ScrollbarPart.VerticalTrack:
                _scrollbarRepeatForward = point.Y >= metrics.VerticalThumb.Top;
                ScrollBy(0, point.Y < metrics.VerticalThumb.Top
                    ? -metrics.ViewportRect.Height
                    : metrics.ViewportRect.Height);
                break;
            case ScrollbarPart.HorizontalTrack:
                _scrollbarRepeatForward = point.X >= metrics.HorizontalThumb.Left;
                ScrollBy(point.X < metrics.HorizontalThumb.Left
                    ? -metrics.ViewportRect.Width
                    : metrics.ViewportRect.Width, 0);
                break;
        }
        return part;
    }

    internal bool RepeatScrollbarInteraction()
    {
        if (!_scrollbarRepeatPointerInside) return false;
        var metrics = GetScrollbarMetrics();
        switch (_scrollbarInteractionPart)
        {
            case ScrollbarPart.VerticalBackButton:
                return ScrollBy(0, -ScrollbarLineStep);
            case ScrollbarPart.VerticalForwardButton:
                return ScrollBy(0, ScrollbarLineStep);
            case ScrollbarPart.HorizontalBackButton:
                return ScrollBy(-ScrollbarLineStep, 0);
            case ScrollbarPart.HorizontalForwardButton:
                return ScrollBy(ScrollbarLineStep, 0);
            case ScrollbarPart.VerticalTrack:
                if (_scrollbarRepeatForward && metrics.VerticalThumb.Bottom >= _scrollbarRepeatPoint.Y ||
                    !_scrollbarRepeatForward && metrics.VerticalThumb.Top <= _scrollbarRepeatPoint.Y)
                    return false;
                return ScrollBy(0, _scrollbarRepeatForward
                    ? metrics.ViewportRect.Height
                    : -metrics.ViewportRect.Height);
            case ScrollbarPart.HorizontalTrack:
                if (_scrollbarRepeatForward && metrics.HorizontalThumb.Right >= _scrollbarRepeatPoint.X ||
                    !_scrollbarRepeatForward && metrics.HorizontalThumb.Left <= _scrollbarRepeatPoint.X)
                    return false;
                return ScrollBy(_scrollbarRepeatForward
                    ? metrics.ViewportRect.Width
                    : -metrics.ViewportRect.Width, 0);
            default:
                return false;
        }
    }

    internal bool UpdateScrollbarInteractionPointer(Point point)
    {
        var metrics = GetScrollbarMetrics();
        var inside = _scrollbarInteractionPart switch
        {
            ScrollbarPart.VerticalBackButton => metrics.VerticalBackButton.Contains(point),
            ScrollbarPart.VerticalForwardButton => metrics.VerticalForwardButton.Contains(point),
            ScrollbarPart.HorizontalBackButton => metrics.HorizontalBackButton.Contains(point),
            ScrollbarPart.HorizontalForwardButton => metrics.HorizontalForwardButton.Contains(point),
            ScrollbarPart.VerticalTrack => metrics.VerticalTrack.Contains(point),
            ScrollbarPart.HorizontalTrack => metrics.HorizontalTrack.Contains(point),
            _ => true
        };
        if (inside == _scrollbarRepeatPointerInside) return false;
        _scrollbarRepeatPointerInside = inside;
        InvalidatePaint();
        return true;
    }

    internal bool UpdateScrollbarHover(Point point)
    {
        var next = GetScrollbarMetrics().HitTest(point);
        if (next == ScrollbarHoverPart) return false;
        ScrollbarHoverPart = next;
        InvalidatePaint();
        return true;
    }

    internal void ClearScrollbarHover()
    {
        if (ScrollbarHoverPart == ScrollbarPart.None) return;
        ScrollbarHoverPart = ScrollbarPart.None;
        InvalidatePaint();
    }

    internal bool ContinueScrollbarInteraction(Point point)
    {
        var metrics = GetScrollbarMetrics();
        if (_scrollbarInteractionPart == ScrollbarPart.VerticalThumb)
        {
            var travel = metrics.VerticalTrack.Height - metrics.VerticalThumb.Height;
            if (travel <= 0) return false;
            SetScrollOffset(
                _scrollbarDragStartOffset.X,
                _scrollbarDragStartOffset.Y + (point.Y - _scrollbarDragStartPoint.Y) * metrics.MaxScrollY / travel);
            return true;
        }
        if (_scrollbarInteractionPart == ScrollbarPart.HorizontalThumb)
        {
            var travel = metrics.HorizontalTrack.Width - metrics.HorizontalThumb.Width;
            if (travel <= 0) return false;
            SetScrollOffset(
                _scrollbarDragStartOffset.X + (point.X - _scrollbarDragStartPoint.X) * metrics.MaxScrollX / travel,
                _scrollbarDragStartOffset.Y);
            return true;
        }
        return false;
    }

    internal void EndScrollbarInteraction()
    {
        if (_scrollbarInteractionPart == ScrollbarPart.None) return;
        _scrollbarInteractionPart = ScrollbarPart.None;
        _scrollbarRepeatPointerInside = false;
        InvalidatePaint();
    }

    private void ShowScrollbar()
    {
        var metrics = GetScrollbarMetrics();
        if ((!metrics.HasVertical && !metrics.HasHorizontal) ||
            ScrollbarVisibility is ScrollbarVisibilityMode.Hidden or ScrollbarVisibilityMode.Always or ScrollbarVisibilityMode.Hover ||
            ScrollbarVisibility == ScrollbarVisibilityMode.Auto && !metrics.IsOverlay)
            return;
        _scrollbarOpacity = 1f;
        _scrollbarFadeElapsed = 0;
        _scrollbarFadeLastTimestamp = Stopwatch.GetTimestamp();
        _scrollbarFadeActive = true;
        DispatchEvent(StandardEvents.CreateRequestFrame());
    }

    private void CancelSmoothScroll()
    {
        _smoothScrollActive = false;
        _smoothScrollElapsed = 0;
    }

    private void CancelScrollbarFade()
    {
        _scrollbarFadeActive = false;
        _scrollbarFadeElapsed = 0;
        _scrollbarOpacity = 0;
    }

    private (float maxX, float maxY) GetMaxScrollOffset() =>
        (GetScrollbarMetrics().MaxScrollX, GetScrollbarMetrics().MaxScrollY);

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
            InvalidateStyle();

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
        GeneralStyleInvalidated?.Invoke(this);
    }

    private void InvalidateHoverStyle()
    {
        if (_invalidationSuppressionDepth > 0) return;
        StyleInvalidated?.Invoke(this);
        HoverStyleInvalidated?.Invoke(this);
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
            if (!current.IsScrollContainer()) continue;
            var scrolled = wheel.IsPrecise || wheel.IsInertial
                ? current.ScrollBy(wheel.DeltaX, wheel.DeltaY)
                : current.ScrollBySmooth(wheel.DeltaX, wheel.DeltaY);
            if (!scrolled) continue;
            e.PreventDefault();
            return;
        }
    }

    /// <summary>挂载完成钩子。</summary>
    protected virtual void OnAttachedCore() { }

    /// <summary>加载完成钩子。</summary>
    protected virtual void OnLoadedCore() { }

    /// <summary>卸载完成钩子。</summary>
    protected virtual void OnUnloadedCore() { }

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
        var now = Stopwatch.GetTimestamp();
        if (_smoothScrollActive) _smoothScrollLastTimestamp = now;
        if (_scrollbarFadeActive) _scrollbarFadeLastTimestamp = now;
        if (_smoothScrollActive || _scrollbarFadeActive)
            DispatchEvent(StandardEvents.CreateRequestFrame());
    }

    void IComponentLifecycle.OnDetached()
    {
        if (!IsAttached) return;
        foreach (var child in Children) ((IComponentLifecycle)child).OnDetached();
        CancelSmoothScroll();
        CancelScrollbarFade();
        EndScrollbarInteraction();
        ScrollbarHoverPart = ScrollbarPart.None;
        OnGeneratedDetachedCore();
        OnDetachedCore();
        IsAttached = false;
    }

    void IComponentLifecycle.OnLoaded()
    {
        IsLoaded = true;
        OnLoadedCore();
        foreach (var child in Children) ((IComponentLifecycle)child).OnLoaded();
    }

    void IComponentLifecycle.OnUnloaded()
    {
        IsLoaded = false;
        OnUnloadedCore();
        foreach (var child in Children) ((IComponentLifecycle)child).OnUnloaded();
    }

    void ILayoutLifecycle.OnMeasure() => Measure(_geometry.Size);
    void ILayoutLifecycle.OnArrange() => Arrange(_geometry);
}
