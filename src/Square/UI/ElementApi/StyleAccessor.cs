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
    private long _inlineStyleRevision;
    private bool? _displayNone;

    internal long ScrollStyleRevision => _scrollStyleRevision;

    internal bool IsDisplayNone => _displayNone ??=
        string.Equals(Get("display")?.Trim(), "none", StringComparison.OrdinalIgnoreCase);

    /// <summary>每次外层内联写入通知（含等值显式 CssText 替换）递增一次，用于识别 application 写回。</summary>
    internal long InlineStyleRevision => _inlineStyleRevision;

    internal StyleAccessor(Element owner) { _owner = owner; }

    /// <summary>内联声明块文本（对齐 <c>element.style.cssText</c>）；只序列化 authored 声明，
    /// 由 shorthand 展开的隐藏条目不重复输出。</summary>
    public string CssText
    {
        get
        {
            if (_inlineStyles == null || _inlineStyles.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var pair in _inlineStyles)
            {
                if (pair.Value.ShorthandSource != null) continue;
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
                foreach (var entry in CssDeclarationText.Parse(value))
                    ApplyDeclarationEntry(entry);
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
        // 显式 CSSOM 写入是替换语义：同块 !important 折叠只用于声明块整体应用。
        ApplyAuthoredDeclarations(property, assignments, important: priority.Length > 0, foldBlockDuplicates: false);
    }

    /// <summary>
    /// 运行时样式编辑的声明块提交路径：整体清空后按已准备的声明行顺序应用，
    /// disabled/invalid 行保留原样但不应用；整批只产生一次外层通知。
    /// </summary>
    internal void SetInspectionDeclarations(IReadOnlyList<CssDeclarationTextEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _cssTextDepth++;
        try
        {
            Clear();
            foreach (var entry in entries)
                ApplyDeclarationEntry(entry);
        }
        finally
        {
            _cssTextDepth--;
            NotifyInlineStyleChanged();
        }
    }

    private void ApplyDeclarationEntry(CssDeclarationTextEntry entry)
    {
        if (entry.Disabled || !entry.ParsedOk || entry.Assignments.Length == 0) return;
        ApplyAuthoredDeclarations(entry.Property, entry.Assignments, entry.Important, foldBlockDuplicates: true);
    }

    /// <summary>
    /// 应用一组 authored 赋值：源敏感的未变化快路径、显式 longhand 写入时收回父 shorthand、
    /// 按原有效值失效规则逐属性失效，最后统一通知。
    /// </summary>
    private void ApplyAuthoredDeclarations(string property, CssPropertyAssignment[] assignments, bool important,
        bool foldBlockDuplicates)
    {
        // 源敏感的未变化快路径：由 shorthand 展开的同值条目不能当作已 authored 跳过。
        if (IsUnchangedAuthored(property, assignments, important)) return;

        var effective = foldBlockDuplicates ? SelectBlockWinners(assignments, important) : assignments;
        if (effective.Count == 0) return;

        RetireParentShorthands(property);
        var previous = new Dictionary<string, StyleState>(effective.Count, StringComparer.Ordinal);
        foreach (var assignment in effective)
            previous[assignment.Property] = CaptureStyleState(assignment.Property);
        _inlineStyles ??= [];
        foreach (var assignment in effective)
        {
            _inlineStyles[assignment.Property] = new InlineStyleEntry(assignment.Value, important,
                GetExpansionSource(property, assignment.Property));
            RemoveComputedStyle(assignment.Property);
        }
        foreach (var assignment in effective)
            InvalidateIfEffectiveStyleChanged(assignment.Property, previous[assignment.Property]);
        NotifyInlineStyleChanged();
    }

    private bool IsUnchangedAuthored(string property, CssPropertyAssignment[] assignments, bool important)
    {
        if (_inlineStyles == null) return false;
        foreach (var assignment in assignments)
        {
            if (!_inlineStyles.TryGetValue(assignment.Property, out var current) ||
                current.Value != assignment.Value || current.Important != important ||
                current.ShorthandSource != GetExpansionSource(property, assignment.Property))
                return false;
        }
        return true;
    }

    /// <summary>声明块内同属性重复声明按优先级折叠：!important 胜者不被后面的普通重复覆盖。</summary>
    private IReadOnlyList<CssPropertyAssignment> SelectBlockWinners(CssPropertyAssignment[] assignments, bool important)
    {
        if (important || _inlineStyles == null) return assignments;
        List<CssPropertyAssignment>? winners = null;
        for (var index = 0; index < assignments.Length; index++)
        {
            var assignment = assignments[index];
            if (_inlineStyles.TryGetValue(assignment.Property, out var existing) && existing.Important)
            {
                winners ??= assignments.Take(index).ToList();
                continue;
            }
            winners?.Add(assignment);
        }
        return winners is null ? assignments : winners;
    }

    /// <summary>赋值条目的预期来源身份：shorthand 自身为 authored，展开条目标记来源 shorthand。</summary>
    private static string? GetExpansionSource(string property, string assignmentProperty) =>
        assignmentProperty != property && CssShorthandExpander.IsShorthand(property) ? property : null;

    /// <summary>
    /// 显式写入/移除 longhand 时：移除残留父 shorthand 条目，并把该 shorthand 其它仍存在的
    /// 展开项提升为 authored（值不变，无额外有效值布局变化），再写本次值。
    /// </summary>
    private void RetireParentShorthands(string property)
    {
        if (_inlineStyles == null || _inlineStyles.Count == 0) return;
        foreach (var shorthand in ParentShorthands(property))
        {
            if (!_inlineStyles.TryGetValue(shorthand, out var shorthandEntry) ||
                shorthandEntry.ShorthandSource != null)
                continue;
            _inlineStyles.Remove(shorthand);
            RemoveComputedStyle(shorthand);
            foreach (var key in _inlineStyles.Keys.ToArray())
            {
                var promoted = _inlineStyles[key];
                if (promoted.ShorthandSource != shorthand) continue;
                _inlineStyles[key] = promoted with { ShorthandSource = null };
            }
        }
    }

    private static readonly Dictionary<string, string[]> ParentShorthandsMap = CreateParentShorthandsMap();

    private static string[] ParentShorthands(string property) =>
        ParentShorthandsMap.TryGetValue(property, out var parents) ? parents : [];

    /// <summary>longhand → 声明它的 shorthand 集合（与 <see cref="CssShorthandExpander"/> 展开互为镜像）。</summary>
    private static Dictionary<string, string[]> CreateParentShorthandsMap()
    {
        var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Add(string shorthand, IEnumerable<string> longhands)
        {
            foreach (var longhand in longhands)
            {
                if (!map.TryGetValue(longhand, out var parents))
                    map[longhand] = [shorthand];
                else if (!parents.Contains(shorthand))
                    map[longhand] = [.. parents, shorthand];
            }
        }

        string[] sides = ["top", "right", "bottom", "left"];
        Add("margin", sides.Select(side => $"margin-{side}"));
        Add("padding", sides.Select(side => $"padding-{side}"));
        foreach (var side in sides)
        {
            string[] longhands = [$"border-{side}-width", $"border-{side}-style", $"border-{side}-color"];
            Add("border", longhands);
            Add($"border-{side}", longhands);
            Add("border-width", [$"border-{side}-width"]);
            Add("border-style", [$"border-{side}-style"]);
            Add("border-color", [$"border-{side}-color"]);
        }
        string[] corners =
        [
            "border-top-left-radius", "border-top-right-radius",
            "border-bottom-right-radius", "border-bottom-left-radius"
        ];
        Add("border-radius", corners);
        Add("background", ["background-color"]);
        Add("font", ["font-style", "font-variant", "font-weight", "font-size", "line-height", "font-family"]);
        Add("outline", ["outline-width", "outline-style", "outline-color"]);
        Add("list-style", ["list-style-type", "list-style-position", "list-style-image"]);
        return map;
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

    /// <summary>移除内联声明并返回原值；显式移除 longhand 时收回残留父 shorthand。</summary>
    public string RemoveProperty(string property)
    {
        property = NormalizePropertyName(property);
        if (_inlineStyles == null || !_inlineStyles.TryGetValue(property, out var entry)) return "";
        RetireParentShorthands(property);
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
        _owner.AppWindow?.NotifyInspectionChanged(Square.Hosting.ElementInspectionChangeKind.Styles);
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
        if (_cssTextDepth == 0)
        {
            _inlineStyleRevision++;
            if (_owner is Square.Html.HTMLElement html)
                html.NotifyDomAttributeMutation("style");
        }
        _owner.AppWindow?.NotifyInspectionChanged(
            Square.Hosting.ElementInspectionChangeKind.Attributes |
            Square.Hosting.ElementInspectionChangeKind.Styles);
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
        _owner.AppWindow?.NotifyInspectionChanged(Square.Hosting.ElementInspectionChangeKind.Styles);
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
        _displayNone = null;
        _computedStyles?.Clear();
        _parentDependentStyles?.Clear();
        foreach (var child in _owner.Children)
            child.Style.ClearComputedStylesRecursive();
    }

    private void RemoveComputedStyle(string property)
    {
        if (property == "display") _displayNone = null;
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

    /// <summary>准备属性赋值：校验值合法性，shorthand 展开为 [自身, ..longhands]；调用方共用同一应用入口。</summary>
    internal static bool TryGetAssignments(string property, string value, out CssPropertyAssignment[] assignments)
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

    /// <summary>内联条目：<see cref="ShorthandSource"/> 标记由 shorthand 展开的隐藏条目（authored 为 null）。</summary>
    private readonly record struct InlineStyleEntry(string? Value, bool Important, string? ShorthandSource = null);
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
        // Covers Single's fixed-point range and invariant NaN / Infinity spellings.
        Span<char> number = stackalloc char[64];
        Value.TryFormat(number, out var written, "0.###", CultureInfo.InvariantCulture);
        return this with { Value = float.Parse(number[..written], NumberStyles.Float, CultureInfo.InvariantCulture) };
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
