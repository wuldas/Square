using Square.CSS.Ast;
using Square.UI;
using Square.UI.ElementApi;
using System.Globalization;

namespace Square.CSS.Engine;

/// <summary>CSS 引擎，负责加载样式表、解析变量与匹配选择器并将声明应用到元素树。</summary>
public sealed class CssEngine
{
    private readonly List<CssRule> _rules = [];
    private readonly Dictionary<string, KeyFramesRule> _keyFrames = new();
    private readonly Dictionary<string, Dictionary<string, string>> _themes = new();
    private string? _activeTheme;

    internal bool HasSiblingCombinators { get; private set; }

    /// <summary>加载样式表规则与关键帧。</summary>
    /// <param name="sheet">待加载的样式表。</param>
    public void LoadStyleSheet(CssStyleSheet sheet)
    {
        foreach (var rule in sheet.Rules)
        {
            _rules.Add(rule);
            HasSiblingCombinators |= rule.Selector.Steps.Any(step =>
                step.Combinator is Combinator.Adjacent or Combinator.GeneralSibling);
        }
        foreach (var kf in sheet.KeyFrames) _keyFrames[kf.Name] = kf;
    }

    /// <summary>按名称查找关键帧规则。</summary>
    /// <param name="name">动画名称。</param>
    /// <returns>匹配的关键帧规则；未找到返回 null。</returns>
    public KeyFramesRule? GetKeyFrames(string name) =>
        _keyFrames.TryGetValue(name, out var kf) ? kf : null;

    /// <summary>设置当前激活的主题名称。</summary>
    /// <param name="name">主题名称，为 null 表示取消主题。</param>
    public void SetTheme(string? name)
    {
        _activeTheme = name;
    }

    /// <summary>注册一个主题及其变量集合。</summary>
    /// <param name="name">主题名称。</param>
    /// <param name="variables">主题变量键值集合。</param>
    public void RegisterTheme(string name, Dictionary<string, string> variables)
    {
        _themes[name] = variables;
    }

    /// <summary>获取当前激活主题的变量集合。</summary>
    /// <returns>激活主题变量集合；未激活主题返回 null。</returns>
    public IReadOnlyDictionary<string, string>? GetActiveThemeVariables() =>
        _activeTheme != null && _themes.TryGetValue(_activeTheme, out var v) ? v : null;

    /// <summary>将匹配规则的声明应用到指定元素。</summary>
    /// <param name="Element">目标元素。</param>
    public void ApplyStyles(Element Element)
    {
        ApplyInheritedProperties(Element);
        var matched = new List<(CssRule rule, CssSpecificity specificity, int order)>();

        for (var i = 0; i < _rules.Count; i++)
        {
            var rule = _rules[i];
            if (TryMatchSelector(rule.Selector, Element, out var spec))
                matched.Add((rule, spec, i));
        }

        matched.Sort((a, b) =>
        {
            var specificity = a.specificity.CompareTo(b.specificity);
            return specificity != 0 ? specificity : a.order.CompareTo(b.order);
        });

        foreach (var (rule, specificity, _) in matched)
        {
            var isSelectionRule = IsSelectionRule(rule.Selector);
            foreach (var decl in rule.Declarations)
            {
                var property = isSelectionRule ? MapSelectionProperty(decl.Property) : decl.Property;
                if (property == null) continue;
                ApplyDeclaration(Element, property, decl.Value, specificity, decl.Important);
            }
        }

        ApplyThemeVariables(Element);
    }

    /// <summary>对整棵元素树应用样式并刷新动画。</summary>
    /// <param name="Element">根元素。</param>
    public void ApplyStylesToTree(Element Element)
    {
        CssStyleReconciler.ApplyScope(this, Element);
    }

    internal void ApplyStylesToTreeCore(Element Element)
    {
        ApplyStyles(Element);
        foreach (var child in Element.Children)
            ApplyStylesToTreeCore(child);
    }

    /// <summary>根据元素的动画属性创建动画时间线。</summary>
    /// <param name="Element">目标元素。</param>
    /// <returns>动画时间线；无有效动画返回 null。</returns>
    public CssAnimationTimeline? CreateAnimationTimeline(Element Element)
    {
        var name = Element.Style.Get("animation-name");
        if (string.IsNullOrWhiteSpace(name) || !_keyFrames.TryGetValue(name, out var keyFrames)) return null;
        var duration = ParseDurationSeconds(Element.Style.Get("animation-duration"));
        var delay = ParseDurationSeconds(Element.Style.Get("animation-delay"));
        var iterationCount = ParseIterationCount(Element.Style.Get("animation-iteration-count"));
        var direction = Element.Style.Get("animation-direction") ?? "normal";
        var easing = ResolveEasing(Element.Style.Get("animation-timing-function"));
        return new CssAnimationTimeline(Element, keyFrames, duration, easing, delay, iterationCount, direction);
    }

    private static float ParseIterationCount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 1;
        var text = value.Trim();
        if (string.Equals(text, "infinite", StringComparison.OrdinalIgnoreCase)) return int.MaxValue;
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var count)
            ? Math.Max(0, count)
            : 1;
    }

    private static float ParseDurationSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0f;
        var text = value.Trim();
        if (text.EndsWith("ms", StringComparison.OrdinalIgnoreCase) && float.TryParse(text[..^2], out var ms))
            return ms / 1000f;
        if (text.EndsWith('s') && float.TryParse(text[..^1], out var s))
            return s;
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0f;
    }

    private static Func<float, float> ResolveEasing(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "ease-in" => t => t * t * t,
        "ease-out" => t => 1f - MathF.Pow(1f - t, 3),
        "ease-in-out" => t => t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3) / 2f,
        _ => t => t
    };

    private void ApplyInheritedProperties(Element Element)
    {
        if (Element.Parent == null) return;
        foreach (var property in new[]
                 {
                     "color", "font-family", "font-size", "font-weight", "font-style", "line-height", "text-align", "visibility"
                 })
        {
            var inherited = Element.Parent.Style.Get(property);
            if (inherited != null)
                Element.Style.SetCascaded(property, inherited, new CssSpecificity(-1, 0, 0), important: false);
        }

        foreach (var pair in Element.Parent.Style.GetAll())
            if (pair.Key.StartsWith("--", StringComparison.Ordinal))
                Element.Style.SetCascaded(pair.Key, pair.Value, new CssSpecificity(-1, 0, 0), important: false);
    }

    private void ApplyThemeVariables(Element Element)
    {
        if (_activeTheme == null || !_themes.TryGetValue(_activeTheme, out var theme)) return;
        foreach (var pair in theme)
            Element.Style.SetCascaded(pair.Key, pair.Value, new CssSpecificity(int.MaxValue - 1, 0, 0), important: false);
    }

    private static bool TryMatchSelector(ComplexSelector selector, Element Element, out CssSpecificity specificity)
    {
        specificity = default;
        if (selector.Steps.Count == 0) return false;

        var last = selector.Steps[^1];
        if (!MatchCompound(last.Selector, Element, ref specificity)) return false;

        var current = Element;
        for (int i = selector.Steps.Count - 2; i >= 0; i--)
        {
            var step = selector.Steps[i];
            var relation = selector.Steps[i + 1].Combinator;
            Element? candidate = relation switch
            {
                Combinator.Child => current.Parent,
                Combinator.Adjacent => PreviousSibling(current),
                _ => null
            };

            if (relation is Combinator.Child or Combinator.Adjacent)
            {
                var s = default(CssSpecificity);
                if (candidate == null || !MatchCompound(step.Selector, candidate, ref s)) return false;
                specificity += s;
                current = candidate;
                continue;
            }

            if (relation == Combinator.GeneralSibling)
            {
                var parent = current.Parent;
                if (parent == null) return false;
                var currentIndex = parent.Children.IndexOf(current);
                var matchedSibling = false;
                for (var siblingIndex = currentIndex - 1; siblingIndex >= 0; siblingIndex--)
                {
                    var s = default(CssSpecificity);
                    var sibling = parent.Children[siblingIndex];
                    if (!MatchCompound(step.Selector, sibling, ref s)) continue;
                    specificity += s;
                    current = sibling;
                    matchedSibling = true;
                    break;
                }
                if (!matchedSibling) return false;
                continue;
            }

            var matched = false;
            var p = current.Parent;
            while (p != null)
            {
                var s = default(CssSpecificity);
                if (MatchCompound(step.Selector, p, ref s))
                {
                    specificity += s;
                    current = p;
                    matched = true;
                    break;
                }
                p = p.Parent;
            }
            if (!matched) return false;
        }
        return true;
    }

    private static Element? PreviousSibling(Element Element)
    {
        var parent = Element.Parent;
        if (parent == null) return null;
        var index = parent.Children.IndexOf(Element);
        return index > 0 ? parent.Children[index - 1] : null;
    }

    private static bool MatchCompound(CompoundSelector compound, Element Element, ref CssSpecificity specificity)
    {
        foreach (var part in compound.Parts)
        {
            switch (part.Kind)
            {
                case SimpleSelectorKind.Type:
                    if (!string.Equals(Element.TagName, part.Name, StringComparison.OrdinalIgnoreCase))
                        return false;
                    specificity += new CssSpecificity(0, 0, 1);
                    break;
                case SimpleSelectorKind.Class:
                    if (!Element.ClassList.Contains(part.Name)) return false;
                    specificity += new CssSpecificity(0, 1, 0);
                    break;
                case SimpleSelectorKind.Id:
                    if (Element.Id != part.Name) return false;
                    specificity += new CssSpecificity(1, 0, 0);
                    break;
                case SimpleSelectorKind.Universal:
                    break;
                case SimpleSelectorKind.PseudoClass:
                    if (!MatchPseudoClass(Element, part.Name)) return false;
                    specificity += GetPseudoClassSpecificity(part.Name);
                    break;
                case SimpleSelectorKind.Attribute:
                    if (!MatchAttribute(Element, part)) return false;
                    specificity += new CssSpecificity(0, 1, 0);
                    break;
            }
        }
        return true;
    }

    private static CssSpecificity GetPseudoClassSpecificity(string name)
    {
        if (name.StartsWith("not(", StringComparison.OrdinalIgnoreCase) && name.EndsWith(')'))
            return GetSimpleArgumentSpecificity(name[4..^1].Trim());
        return new CssSpecificity(0, 1, 0);
    }

    private static CssSpecificity GetSimpleArgumentSpecificity(string selector)
    {
        if (selector.StartsWith('#')) return new CssSpecificity(1, 0, 0);
        if (selector.StartsWith('.')) return new CssSpecificity(0, 1, 0);
        if (selector == "*") return default;
        return new CssSpecificity(0, 0, 1);
    }

    private static bool MatchAttribute(Element Element, SimpleSelector selector)
    {
        if (selector.AttributeOperator == AttributeSelectorOperator.Invalid || selector.Name.Length == 0)
            return false;
        if (selector.AttributeOperator == AttributeSelectorOperator.Presence)
            return Element.Properties.HasValue(selector.Name);

        var actualValue = Element.GetProperty<object>(selector.Name);
        var expected = selector.AttributeValue;
        if (actualValue == null || expected == null) return false;
        var actual = Convert.ToString(actualValue, CultureInfo.InvariantCulture) ?? "";
        var comparison = selector.AttributeCaseSensitivity == AttributeCaseSensitivity.Insensitive
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return selector.AttributeOperator switch
        {
            AttributeSelectorOperator.Equals => string.Equals(actual, expected, comparison),
            AttributeSelectorOperator.Includes => expected.Length > 0 &&
                actual.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Any(value => string.Equals(value, expected, comparison)),
            AttributeSelectorOperator.DashMatch => expected.Length > 0 &&
                (string.Equals(actual, expected, comparison) || actual.StartsWith(expected + "-", comparison)),
            AttributeSelectorOperator.PrefixMatch => expected.Length > 0 && actual.StartsWith(expected, comparison),
            AttributeSelectorOperator.SuffixMatch => expected.Length > 0 && actual.EndsWith(expected, comparison),
            AttributeSelectorOperator.SubstringMatch => expected.Length > 0 && actual.Contains(expected, comparison),
            _ => false
        };
    }

    private static bool MatchPseudoClass(Element Element, string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.StartsWith("nth-child(", StringComparison.Ordinal) && lower.EndsWith(')'))
        {
            var argument = lower[10..^1].Trim();
            if (Element.Parent == null) return false;
            var index = Element.Parent.Children.IndexOf(Element) + 1;
            return MatchesNthChild(argument, index);
        }

        if (lower.StartsWith("not(", StringComparison.Ordinal) && lower.EndsWith(')'))
            return !MatchSimpleArgument(Element, name[4..^1].Trim());

        return lower switch
        {
            "hover" => Element.HasState(ElementState.Hover),
            "focus" => Element.HasState(ElementState.Focus),
            "active" => Element.HasState(ElementState.Active),
            "disabled" => Element.HasState(ElementState.Disabled),
            "checked" => Element.HasState(ElementState.Checked),
            "open" => Element.HasState(ElementState.Open),
            "empty" => Element.ChildNodes.Count == 0,
            "first-child" => Element.Parent?.Children[0] == Element,
            "last-child" => Element.Parent?.Children[^1] == Element,
            "only-child" => Element.Parent?.Children.Count == 1,
            "root" => Element.Parent == null,
            "selection" => true,
            _ => false
        };
    }

    private static bool MatchesNthChild(string expression, int index)
    {
        expression = expression.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
        if (expression == "odd") expression = "2n+1";
        else if (expression == "even") expression = "2n";
        if (int.TryParse(expression, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exact))
            return index == exact;

        var n = expression.IndexOf('n');
        if (n < 0) return false;
        var coefficientText = expression[..n];
        var offsetText = expression[(n + 1)..];
        var coefficient = coefficientText switch
        {
            "" or "+" => 1,
            "-" => -1,
            _ when int.TryParse(coefficientText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => int.MinValue
        };
        if (coefficient == int.MinValue) return false;
        var offset = 0;
        if (offsetText.Length > 0 &&
            !int.TryParse(offsetText, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset))
            return false;
        if (coefficient == 0) return index == offset;
        var delta = index - offset;
        return delta % coefficient == 0 && delta / coefficient >= 0;
    }

    private static bool IsSelectionRule(ComplexSelector selector) => selector.Steps.Any(step =>
        step.Selector.Parts.Any(part => part.Kind == SimpleSelectorKind.PseudoClass &&
            string.Equals(part.Name, "selection", StringComparison.OrdinalIgnoreCase)));

    private static string? MapSelectionProperty(string property) => property.ToLowerInvariant() switch
    {
        "background" or "background-color" => "selection-background-color",
        "color" => "selection-color",
        "selection-background" or "selection-background-color" or "selection-color" => property,
        _ => null
    };

    private static bool MatchSimpleArgument(Element Element, string selector)
    {
        if (selector.StartsWith('.')) return Element.ClassList.Contains(selector[1..]);
        if (selector.StartsWith('#')) return Element.Id == selector[1..];
        if (selector == "*") return true;
        return string.Equals(Element.TagName, selector, StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyDeclaration(
        Element Element,
        string property,
        string value,
        CssSpecificity specificity,
        bool important)
    {
        if (string.Equals(property, "animation", StringComparison.OrdinalIgnoreCase))
        {
            ApplyAnimationShorthand(Element, value, specificity, important);
            return;
        }

        Element.Style.SetCascaded(property, value, specificity, important);
    }

    private static void ApplyAnimationShorthand(
        Element Element,
        string value,
        CssSpecificity specificity,
        bool important)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return;

        var timingFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "linear", "ease", "ease-in", "ease-out", "ease-in-out", "step-start", "step-end"
        };
        var directions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "normal", "reverse", "alternate", "alternate-reverse"
        };

        var name = "";
        var duration = "";
        var timingFunction = "";
        var delay = "";
        var iterationCount = "";
        var direction = "";

        foreach (var part in parts)
        {
            if (IsTime(part))
            {
                if (duration.Length == 0) duration = part;
                else if (delay.Length == 0) delay = part;
                continue;
            }

            if (timingFunction.Length == 0 && (timingFunctions.Contains(part) || part.StartsWith("cubic-bezier(", StringComparison.OrdinalIgnoreCase) || part.StartsWith("steps(", StringComparison.OrdinalIgnoreCase)))
            {
                timingFunction = part;
                continue;
            }

            if (iterationCount.Length == 0 && (string.Equals(part, "infinite", StringComparison.OrdinalIgnoreCase) || float.TryParse(part, out _)))
            {
                iterationCount = part;
                continue;
            }

            if (direction.Length == 0 && directions.Contains(part))
            {
                direction = part;
                continue;
            }

            if (name.Length == 0) name = part;
        }

        if (name.Length > 0) Element.Style.SetCascaded("animation-name", name, specificity, important);
        if (duration.Length > 0) Element.Style.SetCascaded("animation-duration", duration, specificity, important);
        if (timingFunction.Length > 0) Element.Style.SetCascaded("animation-timing-function", timingFunction, specificity, important);
        if (delay.Length > 0) Element.Style.SetCascaded("animation-delay", delay, specificity, important);
        if (iterationCount.Length > 0) Element.Style.SetCascaded("animation-iteration-count", iterationCount, specificity, important);
        if (direction.Length > 0) Element.Style.SetCascaded("animation-direction", direction, specificity, important);
    }

    private static bool IsTime(string value) =>
        value.EndsWith("ms", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith('s') && float.TryParse(value[..^1], out _);
}
