namespace Square.UI;

/// <summary>将内容投影到父元素的委托（组件 Slot 片段）。</summary>
/// <param name="parent">插入子节点的父元素。</param>
public delegate void RenderFragment(Element parent);

/// <summary>将带属性的作用域内容投影到父元素的委托。</summary>
public delegate void ScopedRenderFragment(Element parent, SlotProps props);

/// <summary>作用域插槽的 AOT-safe 属性包。</summary>
public sealed class SlotProps
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    /// <summary>属性数量。</summary>
    public int Count => _values.Count;

    /// <summary>设置属性值。</summary>
    public void Set<T>(string name, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _values[name] = value;
    }

    /// <summary>判断是否包含属性。</summary>
    public bool Contains(string name) => _values.ContainsKey(name);

    /// <summary>尝试取得强类型属性值。</summary>
    public bool TryGet<T>(string name, out T value)
    {
        if (_values.TryGetValue(name, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }
        value = default!;
        return false;
    }

    /// <summary>取得强类型属性值；缺失或类型不匹配时抛出异常。</summary>
    public T Get<T>(string name)
    {
        if (!_values.TryGetValue(name, out var raw))
            throw new KeyNotFoundException($"Slot property '{name}' was not provided.");
        if (raw is T typed) return typed;
        throw new InvalidCastException($"Slot property '{name}' is not assignable to {typeof(T).FullName}.");
    }
}

/// <summary>
/// 组件插槽集合：调用方设置具名/默认片段，组件内 <c>Slot</c> 出口渲染一次。
/// </summary>
public sealed class SlotCollection
{
    private readonly Dictionary<string, ScopedRenderFragment> _fragments = new(StringComparer.Ordinal);
    private readonly HashSet<string> _rendered = new(StringComparer.Ordinal);

    /// <summary>设置具名或默认（name 为空）插槽内容；已渲染后不可再改。</summary>
    public void Set(string? name, RenderFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        Set(name, (parent, _) => fragment(parent));
    }

    /// <summary>设置具名或默认作用域插槽内容；已渲染后不可再改。</summary>
    public void Set(string? name, ScopedRenderFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        var key = NormalizeName(name);
        if (_rendered.Contains(key))
            throw new InvalidOperationException($"Slot '{DisplayName(key)}' has already been rendered.");
        _fragments[key] = fragment;
    }

    /// <summary>是否存在指定插槽片段。</summary>
    public bool Contains(string? name) => _fragments.ContainsKey(NormalizeName(name));

    /// <summary>
    /// 渲染插槽到 <paramref name="parent"/>；无内容返回 false（调用方应渲染 fallback）。
    /// 每个插槽每个实例仅允许渲染一次。
    /// </summary>
    public bool Render(string? name, Element parent) => Render(name, parent, new SlotProps());

    /// <summary>使用指定属性渲染作用域插槽。</summary>
    public bool Render(string? name, Element parent, SlotProps props)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(props);
        var key = NormalizeName(name);
        if (!_fragments.TryGetValue(key, out var fragment)) return false;
        if (!_rendered.Add(key))
            throw new InvalidOperationException($"Slot '{DisplayName(key)}' can only be rendered once.");
        fragment(parent, props);
        return true;
    }

    /// <summary>Allows a generated component to render its existing fragments after a Debug hot reload.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public void ResetRendered() => _rendered.Clear();

    private static string NormalizeName(string? name) => name?.Trim() ?? "";
    private static string DisplayName(string name) => name.Length == 0 ? "default" : name;
}
