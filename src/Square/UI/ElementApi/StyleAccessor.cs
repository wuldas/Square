using System.Globalization;
using System.Text;
using Square.CSS.Properties;

namespace Square.UI.ElementApi;

/// <summary>
/// 元素样式访问器。CSSOM 成员只暴露内联声明；<see cref="Get"/> 读取最终应用值。
/// </summary>
public sealed class StyleAccessor
{
    private static readonly Dictionary<string, string> NormalizedPropertyCache = new(StringComparer.Ordinal);
    private static readonly object NormalizeGate = new();
    private static long _cascadeSequence;

    private readonly Element _owner;
    private Dictionary<string, InlineStyleEntry>? _inlineStyles;
    private Dictionary<string, CascadedStyleEntry>? _cascadedStyles;
    private Dictionary<string, string>? _animatedStyles;
    private Dictionary<string, AnimatedNumericValue>? _animatedNumericStyles;
    private Dictionary<string, string?>? _computedStyles;
    private HashSet<string>? _parentDependentStyles;
    private int _cssTextDepth;
    private long _scrollStyleRevision;

    internal long ScrollStyleRevision => _scrollStyleRevision;

    internal StyleAccessor(Element owner) { _owner = owner; }

    /// <summary>内联声明块文本（对齐 <c>element.style.cssText</c>）。</summary>
    public string CssText
    {
        get
        {
            if (_inlineStyles == null || _inlineStyles.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var pair in _inlineStyles)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(pair.Key).Append(": ").Append(pair.Value.Value);
                if (pair.Value.Important) sb.Append(" !important");
                sb.Append(';');
            }
            return sb.ToString();
        }
        set
        {
            _cssTextDepth++;
            try
            {
                Clear();
                if (!string.IsNullOrWhiteSpace(value))
                    foreach (var declaration in SplitDeclarations(value))
                    {
                        var separator = FindTopLevelColon(declaration);
                        if (separator <= 0) continue;
                        var property = declaration[..separator].Trim();
                        var propertyValue = declaration[(separator + 1)..].Trim();
                        if (property.Length == 0 || propertyValue.Length == 0) continue;
                        var important = TryRemoveImportant(ref propertyValue);
                        SetProperty(property, propertyValue, important ? "important" : "");
                    }
            }
            finally
            {
                _cssTextDepth--;
                NotifyInlineStyleChanged();
            }
        }
    }

    /// <summary>内联声明个数。</summary>
    public int Length => _inlineStyles?.Count ?? 0;

    /// <summary>按声明顺序返回内联属性名；越界返回空字符串。</summary>
    public string Item(int index)
    {
        if (_inlineStyles == null || index < 0 || index >= _inlineStyles.Count) return "";
        return _inlineStyles.Keys.ElementAt(index);
    }

    /// <summary>设置普通内联声明。</summary>
    public void SetProperty(string property, string value) => SetProperty(property, value, "");

    /// <summary>设置内联声明；priority 仅接受空字符串或 <c>important</c>。</summary>
    public void SetProperty(string property, string value, string? priority)
    {
        property = NormalizePropertyName(property);
        if (property.Length == 0) return;
        value ??= "";
        priority = priority?.Trim() ?? "";
        if (value.Length == 0)
        {
            RemoveProperty(property);
            return;
        }
        if (priority.Length > 0 && !string.Equals(priority, "important", StringComparison.OrdinalIgnoreCase))
            return;

        value = value.Trim();
        if (!TryGetAssignments(property, value, out var assignments)) return;
        var important = priority.Length > 0;
        if (_inlineStyles != null && assignments.All(assignment =>
                _inlineStyles.TryGetValue(assignment.Property, out var current) &&
                current.Value == assignment.Value && current.Important == important)) return;
        var previous = assignments.ToDictionary(assignment => assignment.Property, assignment => CaptureStyleState(assignment.Property),
            StringComparer.Ordinal);
        _inlineStyles ??= [];
        foreach (var assignment in assignments)
        {
            _inlineStyles[assignment.Property] = new InlineStyleEntry(assignment.Value, important);
            RemoveComputedStyle(assignment.Property);
        }
        foreach (var assignment in assignments)
            InvalidateIfEffectiveStyleChanged(assignment.Property, previous[assignment.Property]);
        NotifyInlineStyleChanged();
    }

    /// <summary>读取内联声明值；未设置返回空字符串。</summary>
    public string GetPropertyValue(string property)
    {
        property = NormalizePropertyName(property);
        return _inlineStyles != null && _inlineStyles.TryGetValue(property, out var entry) ? entry.Value ?? "" : "";
    }

    /// <summary>读取内联声明 priority。</summary>
    public string GetPropertyPriority(string property)
    {
        property = NormalizePropertyName(property);
        return _inlineStyles != null && _inlineStyles.TryGetValue(property, out var entry) && entry.Important
            ? "important"
            : "";
    }

    /// <summary>移除内联声明并返回原值。</summary>
    public string RemoveProperty(string property)
    {
        property = NormalizePropertyName(property);
        if (_inlineStyles == null || !_inlineStyles.TryGetValue(property, out var entry)) return "";
        var properties = GetDeclarationProperties(property).Where(_inlineStyles.ContainsKey).ToArray();
        var previous = properties.ToDictionary(name => name, CaptureStyleState, StringComparer.Ordinal);
        foreach (var name in properties)
        {
            _inlineStyles.Remove(name);
            RemoveComputedStyle(name);
        }
        foreach (var name in properties) InvalidateIfEffectiveStyleChanged(name, previous[name], allowWidthUpdate: false);
        NotifyInlineStyleChanged();
        return entry.Value ?? "";
    }

    /// <summary>Square 便捷 API，设置普通内联声明。</summary>
    public void Set(string property, string value) => SetProperty(property, value);

    /// <summary>设置动画覆盖值。</summary>
    public bool SetAnimated(string property, string value)
    {
        property = NormalizePropertyName(property);
        var previous = CaptureStyleState(property);
        var hadNumeric = _animatedNumericStyles?.ContainsKey(property) == true;
        _animatedStyles ??= [];
        if (!hadNumeric && _animatedStyles.TryGetValue(property, out var current) && current == value) return false;
        _animatedNumericStyles?.Remove(property);
        _animatedStyles[property] = value;
        RemoveComputedStyle(property);
        InvalidateIfEffectiveStyleChanged(property, previous);
        return true;
    }

    /// <summary>
    /// 设置动画数值覆盖值（纯数字/px/单函数包装）。写入值按 FormatNumber("0.###", InvariantCulture)
    /// 量化（经同语义字符串往返），变化检测与输出保持量化字符串语义；动画值未赢得现有 !important
    /// 级联时只记录不生效。calc/多函数/非数字与自定义属性仍走 <see cref="SetAnimated"/> 字符串路径。
    /// </summary>
    internal bool SetAnimatedNumeric(string property, AnimatedNumericValue value)
    {
        property = NormalizePropertyName(property);
        if (property.StartsWith("--", StringComparison.Ordinal))
            return SetAnimated(property, value.Format());

        var canonical = value.Quantized();
        _animatedNumericStyles ??= [];
        var hadNumeric = _animatedNumericStyles.TryGetValue(property, out var previousNumeric);
        var replacedString = _animatedStyles?.ContainsKey(property) == true;
        if (hadNumeric && !replacedString && previousNumeric.SameQuantized(canonical)) return false;

        var importantBeats = ImportantBeats(property);
        if (hadNumeric && !replacedString)
        {
            // 同为动画数值覆盖：量化不同即输出字符串不同，有效值必变，无需格式化旧值。
            _animatedNumericStyles[property] = canonical;
            RemoveComputedStyle(property);
            if (importantBeats) return true;
            InvalidateAnimatedNumericChange(property, previousNumeric.IsPlainPixelPoints, canonical.IsPlainPixelPoints);
            return true;
        }

        // 过渡写入（字符串动画/普通声明 → 数值动画）：按既有字符串语义比较旧有效值。
        var previous = CaptureStyleState(property);
        _animatedStyles?.Remove(property);
        _animatedNumericStyles[property] = canonical;
        RemoveComputedStyle(property);
        if (importantBeats) return true;
        var currentText = canonical.Format();
        if (previous.AuthorSpecified && string.Equals(previous.Value, currentText, StringComparison.Ordinal)) return true;
        InvalidateAnimatedNumericChange(property,
            TryGetFinitePixelPoints(previous.Value, out _), canonical.IsPlainPixelPoints);
        return true;
    }

    /// <summary>
    /// 读取赢得现有 !important 级联的动画数值覆盖值；未命中或被重要声明压制时返回 false。
    /// </summary>
    internal bool TryGetEffectiveAnimatedNumeric(string property, out AnimatedNumericValue value)
    {
        property = NormalizePropertyName(property);
        if (_animatedNumericStyles != null && _animatedNumericStyles.TryGetValue(property, out value) &&
            !ImportantBeats(property))
            return true;
        value = default;
        return false;
    }

    private bool ImportantBeats(string property) =>
        (_inlineStyles != null && _inlineStyles.TryGetValue(property, out var inline) && inline.Important) ||
        (_cascadedStyles != null && _cascadedStyles.TryGetValue(property, out var cascaded) && cascaded.Important);

    private void InvalidateAnimatedNumericChange(string property, bool previousPointsLike, bool currentPointsLike)
    {
        var invalidation = StyleInvalidation.ForProperty(property);
        RemoveDependentDescendantComputedStyle(property, invalidation);
        if (property == "z-index") SyncOwnerZIndex();
        var kind = property == "width" && previousPointsLike && currentPointsLike
            ? LayoutDirtyKind.WidthPoints
            : LayoutDirtyKind.Other;
        _owner.Invalidate(invalidation, kind);
    }

    internal void RemoveAnimated(string property)
    {
        property = NormalizePropertyName(property);
        if (_animatedNumericStyles?.TryGetValue(property, out var numeric) == true)
        {
            var previous = ImportantBeats(property)
                ? CaptureStyleState(property)
                : new StyleState(numeric.Format(), true);
            _animatedNumericStyles.Remove(property);
            RemoveComputedStyle(property);
            InvalidateIfEffectiveStyleChanged(property, previous, allowWidthUpdate: false);
            return;
        }
        if (_animatedStyles == null || !_animatedStyles.ContainsKey(property)) return;
        var captured = CaptureStyleState(property);
        _animatedStyles.Remove(property);
        RemoveComputedStyle(property);
        InvalidateIfEffectiveStyleChanged(property, captured, allowWidthUpdate: false);
    }

    /// <summary>兼容旧调用的级联写入。</summary>
    public bool SetCascaded(string property, string value, int specificity) =>
        SetCascaded(property, value, CssSpecificity.FromLegacy(specificity), important: false, persistent: true);

    internal bool SetCascaded(string property, string value, CssSpecificity specificity, bool important,
        bool persistent = false, CssCascadeOrigin origin = CssCascadeOrigin.Author, bool? authorSpecified = null)
    {
        property = NormalizePropertyName(property);
        value = value.Trim();
        if (!TryGetAssignments(property, value, out var assignments)) return false;
        _cascadedStyles ??= [];
        var previous = assignments.ToDictionary(assignment => assignment.Property, assignment => CaptureStyleState(assignment.Property),
            StringComparer.Ordinal);
        var changed = new List<string>(assignments.Length);
        foreach (var assignment in assignments)
        {
            var candidate = new CascadedStyleEntry(assignment.Value, specificity, important, persistent, origin,
                authorSpecified ?? origin == CssCascadeOrigin.Author, Interlocked.Increment(ref _cascadeSequence));
            if (_cascadedStyles.TryGetValue(assignment.Property, out var current) && current.ComparePriority(candidate) > 0)
                continue;
            if (_cascadedStyles.TryGetValue(assignment.Property, out current) && current.SameValueAndPriority(candidate))
                continue;
            _cascadedStyles[assignment.Property] = candidate;
            changed.Add(assignment.Property);
        }
        foreach (var name in changed) RemoveComputedStyle(name);
        foreach (var name in changed) InvalidateIfEffectiveStyleChanged(name, previous[name]);
        return changed.Count > 0;
    }

    /// <summary>读取最终应用值；未设置或变量解析失败时返回 null。</summary>
    public string? Get(string property)
    {
        property ??= "";
        if (_computedStyles != null && _computedStyles.TryGetValue(property, out var cached)) return cached;
        property = NormalizePropertyName(property);
        if (_computedStyles != null && _computedStyles.TryGetValue(property, out cached)) return cached;
        var raw = GetRaw(property);
        if (property.StartsWith("--", StringComparison.Ordinal)) return raw;
        var inherited = CssPropertyRegistry.IsInherited(property);
        var dependsOnParent = false;
        string? result;
        if (raw == null)
        {
            dependsOnParent = inherited;
            result = inherited ? _owner.Parent?.Style.Get(property) : null;
        }
        else if (string.Equals(raw, "inherit", StringComparison.OrdinalIgnoreCase))
        {
            dependsOnParent = true;
            result = _owner.Parent?.Style.Get(property) ?? CssPropertyRegistry.GetInitialValue(property);
        }
        else if (string.Equals(raw, "initial", StringComparison.OrdinalIgnoreCase))
            result = CssPropertyRegistry.GetInitialValue(property);
        else if (string.Equals(raw, "unset", StringComparison.OrdinalIgnoreCase))
        {
            dependsOnParent = inherited;
            result = inherited
                ? _owner.Parent?.Style.Get(property) ?? CssPropertyRegistry.GetInitialValue(property)
                : CssPropertyRegistry.GetInitialValue(property);
        }
        else
        {
            var resolved = ResolveVariables(raw, []);
            dependsOnParent = resolved == null && inherited;
            result = resolved ?? (inherited
                ? _owner.Parent?.Style.Get(property) ?? CssPropertyRegistry.GetInitialValue(property)
                : CssPropertyRegistry.GetInitialValue(property));
        }
        _computedStyles ??= [];
        _computedStyles[property] = result;
        if (dependsOnParent)
        {
            _parentDependentStyles ??= [];
            _parentDependentStyles.Add(property);
        }
        else
        {
            _parentDependentStyles?.Remove(property);
        }
        return result;
    }

    internal string? ResolveValue(string value) => ResolveVariables(value, []);

    /// <summary>移除内联属性。</summary>
    public void Remove(string property) => RemoveProperty(property);

    /// <summary>清空全部内联声明，保留样式表、默认样式和动画结果。</summary>
    public void Clear()
    {
        if (_inlineStyles == null || _inlineStyles.Count == 0) return;
        var properties = _inlineStyles.Keys.ToArray();
        var previous = properties.ToDictionary(property => property, CaptureStyleState, StringComparer.Ordinal);
        _inlineStyles.Clear();
        foreach (var property in properties) RemoveComputedStyle(property);
        foreach (var property in properties)
            InvalidateIfEffectiveStyleChanged(property, previous[property], allowWidthUpdate: false);
        NotifyInlineStyleChanged();
    }
    private void NotifyInlineStyleChanged()
    {
        if (_cssTextDepth == 0 && _owner is Square.Html.HTMLElement html)
            html.NotifyDomAttributeMutation("style");
    }


    /// <summary>清除全部非内联级联候选。</summary>
    public void ClearCascaded()
    {
        if (_cascadedStyles == null || _cascadedStyles.Count == 0) return;
        var properties = _cascadedStyles
            .Where(pair => !pair.Value.Persistent)
            .Select(pair => pair.Key)
            .ToArray();
        if (properties.Length == 0) return;
        var previous = properties.ToDictionary(property => property, CaptureStyleState, StringComparer.Ordinal);
        foreach (var property in properties) _cascadedStyles.Remove(property);
        foreach (var property in properties) RemoveComputedStyle(property);
        foreach (var property in properties)
            InvalidateIfEffectiveStyleChanged(property, previous[property], allowWidthUpdate: false);
    }

    /// <summary>返回最终应用样式快照。</summary>
    public IReadOnlyDictionary<string, string> GetAll()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (_inlineStyles != null) keys.UnionWith(_inlineStyles.Keys);
        if (_cascadedStyles != null) keys.UnionWith(_cascadedStyles.Keys);
        if (_animatedStyles != null) keys.UnionWith(_animatedStyles.Keys);
        if (_animatedNumericStyles != null) keys.UnionWith(_animatedNumericStyles.Keys);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var value = Get(key);
            if (value != null) result[key] = value;
        }
        return result;
    }

    internal bool HasAuthorDeclarations
    {
        get
        {
            if (_inlineStyles?.Count > 0 || _animatedStyles?.Count > 0 || _animatedNumericStyles?.Count > 0) return true;
            if (_cascadedStyles != null)
                foreach (var entry in _cascadedStyles.Values)
                    if (entry.AuthorSpecified) return true;
            return false;
        }
    }

    internal bool IsAuthorSpecified(string property)
    {
        property = NormalizePropertyName(property);
        if (TryGetRawAuthorSpecified(property, out var authorSpecified)) return authorSpecified;
        return (CssPropertyRegistry.IsInherited(property) || property.StartsWith("--", StringComparison.Ordinal)) &&
               _owner.Parent?.Style.IsAuthorSpecified(property) == true;
    }

    private bool TryGetRawAuthorSpecified(string property, out bool authorSpecified)
    {
        var inline = default(InlineStyleEntry);
        var cascaded = default(CascadedStyleEntry);
        _inlineStyles?.TryGetValue(property, out inline);
        _cascadedStyles?.TryGetValue(property, out cascaded);
        var animated = _animatedStyles?.ContainsKey(property) == true ||
            _animatedNumericStyles?.ContainsKey(property) == true;

        if (inline.Important)
        {
            authorSpecified = true;
            return true;
        }
        if (cascaded.Important)
        {
            authorSpecified = cascaded.AuthorSpecified;
            return true;
        }
        if (animated)
        {
            authorSpecified = true;
            return true;
        }
        if (inline.Value != null)
        {
            authorSpecified = true;
            return true;
        }
        if (cascaded.Value != null)
        {
            authorSpecified = cascaded.AuthorSpecified;
            return true;
        }
        authorSpecified = false;
        return false;
    }

    private string? GetRaw(string property)
    {
        var inline = default(InlineStyleEntry);
        var cascaded = default(CascadedStyleEntry);
        string? animated = null;
        _inlineStyles?.TryGetValue(property, out inline);
        _cascadedStyles?.TryGetValue(property, out cascaded);
        _animatedStyles?.TryGetValue(property, out animated);

        if (inline.Important) return inline.Value;
        if (cascaded.Important) return cascaded.Value;
        if (_animatedNumericStyles?.TryGetValue(property, out var numeric) == true) return numeric.Format();
        if (animated != null) return animated;
        if (inline.Value != null) return inline.Value;
        return cascaded.Value;
    }

    private string? ResolveVariables(string value, HashSet<string> resolving)
    {
        var searchFrom = 0;
        while (true)
        {
            var start = value.IndexOf("var(", searchFrom, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return value;
            var end = FindMatchingParen(value, start + 3);
            if (end < 0) return null;
            var inner = value[(start + 4)..end];
            var comma = FindTopLevelComma(inner);
            var name = (comma < 0 ? inner : inner[..comma]).Trim();
            var fallback = comma < 0 ? null : inner[(comma + 1)..].Trim();
            if (!name.StartsWith("--", StringComparison.Ordinal) || !resolving.Add(name)) return null;
            var replacement = GetRaw(name);
            if (replacement != null) replacement = ResolveVariables(replacement, resolving);
            resolving.Remove(name);
            if (replacement == null && fallback != null) replacement = ResolveVariables(fallback, resolving);
            if (replacement == null) return null;
            value = value[..start] + replacement + value[(end + 1)..];
            searchFrom = start + replacement.Length;
        }
    }

    private StyleState CaptureStyleState(string property) =>
        new(Get(property), IsAuthorSpecified(property));

    private void InvalidateIfEffectiveStyleChanged(string property, StyleState previous, bool allowWidthUpdate = true)
    {
        var current = Get(property);
        var currentAuthorSpecified = IsAuthorSpecified(property);
        if (string.Equals(previous.Value, current, StringComparison.Ordinal) &&
            previous.AuthorSpecified == currentAuthorSpecified) return;
        var invalidation = StyleInvalidation.ForProperty(property);
        if (property.StartsWith("--", StringComparison.Ordinal))
            ClearComputedStylesRecursive();
        else
            RemoveDependentDescendantComputedStyle(property, invalidation);
        if (property == "z-index") SyncOwnerZIndex();
        if (property.StartsWith("--", StringComparison.Ordinal)) invalidation |= ElementInvalidation.Style;
        var kind = allowWidthUpdate && property == "width" &&
                   TryGetFinitePixelPoints(previous.Value, out _) &&
                   TryGetFinitePixelPoints(current, out _)
            ? LayoutDirtyKind.WidthPoints
            : LayoutDirtyKind.Other;
        _owner.Invalidate(invalidation, kind);
    }

    /// <summary>
    /// 判定值是否为有限像素/无单位数值长度（如 <c>240</c>、<c>12.5px</c>）。
    /// auto、百分比、calc/min/max、em/rem 等单位、移除（null）一律否定。零分配。
    /// </summary>
    private static bool TryGetFinitePixelPoints(string? value, out float points)
    {
        points = 0f;
        if (string.IsNullOrEmpty(value)) return false;
        var span = value.AsSpan().Trim();
        if (span.IsEmpty) return false;
        var index = 0;
        if (span[index] is '+' or '-') index++;
        var digits = 0;
        while (index < span.Length && char.IsAsciiDigit(span[index]))
        {
            index++;
            digits++;
        }
        if (index < span.Length && span[index] == '.')
        {
            index++;
            while (index < span.Length && char.IsAsciiDigit(span[index]))
            {
                index++;
                digits++;
            }
        }
        if (digits == 0) return false;
        var numericEnd = index;
        if (index < span.Length && span[index] is 'e' or 'E')
        {
            var exponentStart = index;
            index++;
            if (index < span.Length && span[index] is '+' or '-') index++;
            var exponentDigits = 0;
            while (index < span.Length && char.IsAsciiDigit(span[index]))
            {
                index++;
                exponentDigits++;
            }
            if (exponentDigits > 0) numericEnd = index;
            else index = exponentStart;
        }
        if (index != span.Length && !span[index..].Equals("px", StringComparison.OrdinalIgnoreCase)) return false;
        return float.TryParse(span[..numericEnd], NumberStyles.Float, CultureInfo.InvariantCulture, out points) &&
               float.IsFinite(points);
    }

    internal void ClearComputedStylesRecursive()
    {
        _scrollStyleRevision++;
        _computedStyles?.Clear();
        _parentDependentStyles?.Clear();
        foreach (var child in _owner.Children)
            child.Style.ClearComputedStylesRecursive();
    }

    private void RemoveComputedStyle(string property)
    {
        if (property is "display" or "overflow" or "overflow-x" or "overflow-y" or "scrollbar-width" or "scrollbar-gutter")
            _scrollStyleRevision++;
        _computedStyles?.Remove(property);
        _parentDependentStyles?.Remove(property);
    }

    private void RemoveDependentDescendantComputedStyle(string property, ElementInvalidation invalidation)
    {
        foreach (var child in _owner.Children)
        {
            if (child.Style._parentDependentStyles?.Contains(property) != true) continue;
            child.Style.RemoveComputedStyle(property);
            child.Invalidate(invalidation);
            child.Style.RemoveDependentDescendantComputedStyle(property, invalidation);
        }
    }

    private void SyncOwnerZIndex()
    {
        var value = Get("z-index");
        _owner.ZIndex = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zIndex)
            ? zIndex
            : 0;
    }

    private static IEnumerable<string> SplitDeclarations(string value)
    {
        var start = 0;
        var depth = 0;
        char quote = '\0';
        for (var i = 0; i <= value.Length; i++)
        {
            if (i < value.Length)
            {
                var c = value[i];
                if (quote != '\0')
                {
                    if (c == '\\') { i++; continue; }
                    if (c == quote) quote = '\0';
                    continue;
                }
                if (c is '\'' or '"') { quote = c; continue; }
                if (c == '(') depth++;
                else if (c == ')') depth = Math.Max(0, depth - 1);
                if (c != ';' || depth > 0) continue;
            }
            var declaration = value[start..i].Trim();
            if (declaration.Length > 0) yield return declaration;
            start = i + 1;
        }
    }

    private static int FindTopLevelColon(string value)
    {
        var depth = 0;
        char quote = '\0';
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (quote != '\0')
            {
                if (c == '\\') { i++; continue; }
                if (c == quote) quote = '\0';
                continue;
            }
            if (c is '\'' or '"') quote = c;
            else if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (c == ':' && depth == 0) return i;
        }
        return -1;
    }

    private static bool TryRemoveImportant(ref string value)
    {
        var index = value.LastIndexOf('!');
        if (index < 0 || !string.Equals(value[(index + 1)..].Trim(), "important", StringComparison.OrdinalIgnoreCase))
            return false;
        value = value[..index].TrimEnd();
        return value.Length > 0;
    }

    private static int FindMatchingParen(string value, int openParen)
    {
        var depth = 0;
        for (var i = openParen; i < value.Length; i++)
        {
            if (value[i] == '(') depth++;
            else if (value[i] == ')' && --depth == 0) return i;
        }
        return -1;
    }

    private static int FindTopLevelComma(string value)
    {
        var depth = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '(') depth++;
            else if (value[i] == ')') depth--;
            else if (value[i] == ',' && depth == 0) return i;
        }
        return -1;
    }

    private static bool TryGetAssignments(string property, string value, out CssPropertyAssignment[] assignments)
    {
        if (property.Length == 0 || value.Length == 0)
        {
            assignments = [];
            return false;
        }
        if (!CssPropertyRegistry.IsValid(property, value))
        {
            assignments = [];
            return false;
        }
        if (!CssShorthandExpander.IsShorthand(property))
        {
            assignments = [new CssPropertyAssignment(property, value)];
            return true;
        }
        if (!CssShorthandExpander.TryExpand(property, value, out var expanded))
        {
            assignments = [];
            return false;
        }
        assignments = [new CssPropertyAssignment(property, value), .. expanded];
        return true;
    }

    private static IEnumerable<string> GetDeclarationProperties(string property)
    {
        yield return property;
        if (!CssShorthandExpander.IsShorthand(property) ||
            !CssShorthandExpander.TryExpand(property, "initial", out var expanded)) yield break;
        foreach (var assignment in expanded) yield return assignment.Property;
    }

    /// <summary>将 camelCase / PascalCase 属性名规范为 kebab-case。</summary>
    public static string NormalizePropertyName(string property)
    {
        if (string.IsNullOrEmpty(property)) return property ?? "";
        property = TrimPropertyName(property);
        if (property.Length == 0 || property.StartsWith("--", StringComparison.Ordinal)) return property;
        if (IsAlreadyNormalizedPropertyName(property)) return property;
        if (!ContainsAsciiUppercase(property)) return property.ToLowerInvariant();
        lock (NormalizeGate)
            if (NormalizedPropertyCache.TryGetValue(property, out var cached)) return cached;
        var sb = new StringBuilder(property.Length + 4);
        for (var i = 0; i < property.Length; i++)
        {
            var c = property[i];
            if (char.IsUpper(c))
            {
                if (i > 0) sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            else sb.Append(c);
        }
        var normalized = sb.ToString();
        lock (NormalizeGate)
            if (NormalizedPropertyCache.Count < 512) NormalizedPropertyCache[property] = normalized;
        return normalized;
    }

    private static bool IsAlreadyNormalizedPropertyName(string property)
    {
        for (var i = 0; i < property.Length; i++)
        {
            var c = property[i];
            if (c is >= 'A' and <= 'Z' || c <= ' ' || c == '\u007f' || c > '\u007f') return false;
        }
        return true;
    }

    private static bool ContainsAsciiUppercase(string property)
    {
        for (var i = 0; i < property.Length; i++)
            if (property[i] is >= 'A' and <= 'Z') return true;
        return false;
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

    private static bool IsPropertyNameTrimChar(char c) =>
        c <= ' ' || c == '\u007f' || c > '\u007f' && char.IsWhiteSpace(c);

    private readonly record struct InlineStyleEntry(string? Value, bool Important);
    private readonly record struct CascadedStyleEntry(
        string? Value,
        CssSpecificity Specificity,
        bool Important,
        bool Persistent,
        CssCascadeOrigin Origin,
        bool AuthorSpecified,
        long Sequence)
    {
        public int ComparePriority(CascadedStyleEntry other)
        {
            var important = Important.CompareTo(other.Important);
            if (important != 0) return important;
            var origin = Origin.CompareTo(other.Origin);
            if (origin != 0) return origin;
            var specificity = Specificity.CompareTo(other.Specificity);
            return specificity != 0 ? specificity : Sequence.CompareTo(other.Sequence);
        }

        public bool SameValueAndPriority(CascadedStyleEntry other) =>
            Value == other.Value && Important == other.Important && Origin == other.Origin &&
            AuthorSpecified == other.AuthorSpecified && Specificity == other.Specificity;
    }

    private readonly record struct StyleState(string? Value, bool AuthorSpecified);
}

/// <summary>
/// 动画数值覆盖值（纯数字/px/单函数包装）。<see cref="Value"/> 由
/// <c>StyleAccessor.SetAnimatedNumeric</c> 按 FormatNumber("0.###", InvariantCulture) 量化
/// （经同语义字符串往返）后存储，使消费端数值与“格式化再解析”逐位一致；
/// <see cref="Prefix"/>/<see cref="Suffix"/> 为单函数包装（如 <c>"rotate("</c>/<c>"deg)"</c>），
/// 纯数字为 <c>""</c>/<c>""</c>。
/// </summary>
internal readonly record struct AnimatedNumericValue(float Value, string Prefix, string Suffix)
{
    /// <summary>按量化字符串语义格式化为样式值（与 FormatNumber("0.###") 输出逐字节一致）。</summary>
    internal string Format() => Prefix + Value.ToString("0.###", CultureInfo.InvariantCulture) + Suffix;

    /// <summary>量化为 FormatNumber("0.###") 字符串的往返值（负零保留符号）。</summary>
    internal AnimatedNumericValue Quantized()
    {
        var number = Value.ToString("0.###", CultureInfo.InvariantCulture);
        return this with { Value = float.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture) };
    }

    /// <summary>输出字符串等价判定（含负零符号差异）。</summary>
    internal bool SameQuantized(AnimatedNumericValue other)
    {
        var sameValue = Value == other.Value || (float.IsNaN(Value) && float.IsNaN(other.Value));
        return sameValue && MathF.CopySign(1f, Value) == MathF.CopySign(1f, other.Value) &&
            Prefix == other.Prefix && Suffix == other.Suffix;
    }

    /// <summary>是否可直接作为像素点值（无单位或 px 的有限数值）。</summary>
    internal bool IsPlainPixelPoints =>
        Prefix.Length == 0 && (Suffix.Length == 0 || Suffix == "px") && float.IsFinite(Value);
}

internal enum CssCascadeOrigin
{
    Inherited,
    UserAgent,
    Author
}

internal readonly record struct CssSpecificity(int Ids, int Classes, int Types) : IComparable<CssSpecificity>
{
    public int CompareTo(CssSpecificity other)
    {
        var ids = Ids.CompareTo(other.Ids);
        if (ids != 0) return ids;
        var classes = Classes.CompareTo(other.Classes);
        return classes != 0 ? classes : Types.CompareTo(other.Types);
    }

    public static CssSpecificity operator +(CssSpecificity left, CssSpecificity right) =>
        new(left.Ids + right.Ids, left.Classes + right.Classes, left.Types + right.Types);

    public static CssSpecificity FromLegacy(int value) => value switch
    {
        int.MinValue => new(-2, 0, 0),
        < 0 => new(-1, 0, 0),
        _ => new(value / 100, value % 100 / 10, value % 10)
    };
}
