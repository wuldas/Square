using System.Globalization;
using System.Text;

namespace Square.UI.ElementApi;

/// <summary>
/// 元素样式访问器，对齐 CSSOM <c>CSSStyleDeclaration</c> / <c>element.style</c>，
/// 并支持带 specificity 的级联写入（样式表引擎用）。
/// </summary>
public sealed class StyleAccessor
{
    private static readonly Dictionary<string, string> NormalizedPropertyCache = new(StringComparer.Ordinal);
    private static readonly object NormalizeGate = new();

    private readonly Element _owner;
    private Dictionary<string, StyleEntry>? _styles;

    internal StyleAccessor(Element owner) { _owner = owner; }

    /// <summary>
    /// 声明块文本（对齐 <c>cssText</c>）。
    /// 读取时序列化为 <c>prop: value; ...</c>；写入时清空后解析。
    /// </summary>
    public string CssText
    {
        get
        {
            if (_styles == null || _styles.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var pair in _styles)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(pair.Key).Append(": ").Append(pair.Value.Value).Append(';');
            }
            return sb.ToString();
        }
        set
        {
            Clear();
            if (string.IsNullOrWhiteSpace(value)) return;
            foreach (var part in value.Split(';'))
            {
                var separator = part.IndexOf(':');
                if (separator <= 0) continue;
                var property = part[..separator].Trim();
                var propValue = part[(separator + 1)..].Trim();
                if (property.Length == 0 || propValue.Length == 0) continue;
                SetProperty(property, propValue);
            }
        }
    }

    /// <summary>已设置的样式属性个数（对齐 <c>length</c>）。</summary>
    public int Length => _styles?.Count ?? 0;

    /// <summary>按索引取属性名（对齐 <c>item</c>）；越界返回空字符串。</summary>
    public string Item(int index)
    {
        if (_styles == null || index < 0 || index >= _styles.Count) return "";
        return _styles.Keys.ElementAt(index);
    }

    /// <summary>以内联最高优先级设置样式（对齐 <c>setProperty</c>，无 priority）。</summary>
    public void SetProperty(string property, string value) =>
        Set(NormalizePropertyName(property), value);

    /// <summary>
    /// 设置样式并可带 <c>important</c>（对齐 <c>setProperty(property, value, priority)</c>）。
    /// 当前级联对 important 使用高于普通样式表的 specificity。
    /// </summary>
    public void SetProperty(string property, string value, string? priority)
    {
        property = NormalizePropertyName(property);
        if (string.Equals(priority, "important", StringComparison.OrdinalIgnoreCase))
            SetCascaded(property, value, int.MaxValue - 1); // 仍低于 Style.Set 的 int.MaxValue 内联
        else
            Set(property, value);
    }

    /// <summary>读取样式属性值（对齐 <c>getPropertyValue</c>）；未设置返回空字符串。</summary>
    public string GetPropertyValue(string property)
    {
        var value = Get(NormalizePropertyName(property));
        return value ?? "";
    }

    /// <summary>移除属性并返回原值（对齐 <c>removeProperty</c>）。</summary>
    public string RemoveProperty(string property)
    {
        property = NormalizePropertyName(property);
        var previous = Get(property) ?? "";
        Remove(property);
        return previous;
    }

    /// <summary>以内联最高优先级设置样式属性（Square 便捷 API，等同 <see cref="SetProperty(string, string)"/>）。</summary>
    public void Set(string property, string value)
    {
        SetCascaded(NormalizePropertyName(property), value, int.MaxValue);
    }

    /// <summary>Sets a value produced by an animation without scheduling a cascade pass for every frame.</summary>
    public bool SetAnimated(string property, string value)
    {
        property = NormalizePropertyName(property);
        _styles ??= [];
        if (_styles.TryGetValue(property, out var current) &&
            current.Value == value && current.Specificity == int.MaxValue - 1)
            return false;
        _styles[property] = new StyleEntry(value, int.MaxValue - 1);
        _owner.InvalidatePaint();
        return true;
    }

    /// <summary>
    /// 按 specificity 写入级联样式；若现有条目优先级更高则忽略并返回 false。
    /// </summary>
    public bool SetCascaded(string property, string value, int specificity)
    {
        property = NormalizePropertyName(property);
        _styles ??= [];
        if (_styles.TryGetValue(property, out var current) && current.Specificity > specificity)
            return false;
        if (_styles.TryGetValue(property, out current) && current.Value == value && current.Specificity == specificity)
            return false;
        _styles[property] = new StyleEntry(value, specificity);
        if (property == "z-index") SyncOwnerZIndex();
        _owner.Invalidate(StyleInvalidation.ForProperty(property));
        return true;
    }

    /// <summary>读取样式属性值；未设置时返回 null（Square 便捷 API）。</summary>
    public string? Get(string property)
    {
        property = NormalizePropertyName(property);
        if (_styles != null && _styles.TryGetValue(property, out var entry))
            return entry.Value;
        return null;
    }

    /// <summary>移除样式属性。</summary>
    public void Remove(string property)
    {
        property = NormalizePropertyName(property);
        if (_styles == null) return;
        if (_styles.Remove(property))
        {
            if (property == "z-index") SyncOwnerZIndex();
            _owner.Invalidate(StyleInvalidation.ForProperty(property));
        }
    }

    /// <summary>清空全部样式条目。</summary>
    public void Clear()
    {
        if (_styles == null) return;
        if (_styles.Count == 0) return;
        var invalidation = ElementInvalidation.None;
        foreach (var property in _styles.Keys)
            invalidation |= StyleInvalidation.ForProperty(property);
        _styles.Clear();
        _owner.ZIndex = 0;
        _owner.Invalidate(invalidation);
    }

    /// <summary>清除非内联（specificity &lt; int.MaxValue）的级联条目。</summary>
    public void ClearCascaded()
    {
        if (_styles == null) return;
        var changed = false;
        var invalidation = ElementInvalidation.None;
        foreach (var property in _styles.Where(pair => pair.Value.Specificity < int.MaxValue).Select(pair => pair.Key).ToArray())
        {
            if (!_styles.Remove(property)) continue;
            changed = true;
            invalidation |= StyleInvalidation.ForProperty(property);
        }
        if (changed)
        {
            SyncOwnerZIndex();
            _owner.Invalidate(invalidation);
        }
    }

    private void SyncOwnerZIndex()
    {
        var value = Get("z-index");
        _owner.ZIndex = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zIndex)
            ? zIndex
            : 0;
    }

    /// <summary>返回当前全部样式的只读快照。</summary>
    public IReadOnlyDictionary<string, string> GetAll() => _styles == null
        ? new Dictionary<string, string>()
        : _styles.ToDictionary(pair => pair.Key, pair => pair.Value.Value);

    /// <summary>
    /// 将 camelCase / PascalCase 属性名规范为 CSS kebab-case（如 <c>fontSize</c> → <c>font-size</c>）。
    /// 已是 kebab-case 或自定义属性 <c>--</c> 则保持小写形式。
    /// </summary>
    public static string NormalizePropertyName(string property)
    {
        if (string.IsNullOrEmpty(property)) return property ?? "";
        property = TrimPropertyName(property);
        if (property.Length == 0) return "";
        if (property.StartsWith("--", StringComparison.Ordinal))
            return property;

        if (IsAlreadyNormalizedPropertyName(property))
            return property;

        lock (NormalizeGate)
        {
            if (NormalizedPropertyCache.TryGetValue(property, out var cached))
                return cached;
        }

        var normalized = NormalizePropertyNameSlow(property);
        lock (NormalizeGate)
        {
            if (NormalizedPropertyCache.Count < 512)
                NormalizedPropertyCache[property] = normalized;
        }
        return normalized;
    }

    private static bool IsAlreadyNormalizedPropertyName(string property)
    {
        for (var i = 0; i < property.Length; i++)
        {
            var c = property[i];
            if (c is >= 'A' and <= 'Z') return false;
            if (c <= ' ' || c == '\u007f') return false;
            if (c > '\u007f') return false;
        }
        return true;
    }

    private static string TrimPropertyName(string property)
    {
        var start = 0;
        var end = property.Length - 1;

        while (start <= end && IsPropertyNameTrimChar(property[start])) start++;
        while (end >= start && IsPropertyNameTrimChar(property[end])) end--;

        if (start == 0 && end == property.Length - 1) return property;
        return start > end ? "" : property[start..(end + 1)];
    }

    private static bool IsPropertyNameTrimChar(char c)
    {
        if (c <= ' ' || c == '\u007f') return true;
        return c > '\u007f' && char.IsWhiteSpace(c);
    }

    private static string NormalizePropertyNameSlow(string property)
    {
        // 已含连字符：统一小写
        if (property.Contains('-', StringComparison.Ordinal))
            return property.ToLowerInvariant();

        var sb = new StringBuilder(property.Length + 4);
        for (var i = 0; i < property.Length; i++)
        {
            var c = property[i];
            if (char.IsUpper(c))
            {
                if (i > 0) sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private readonly record struct StyleEntry(string Value, int Specificity);
}
