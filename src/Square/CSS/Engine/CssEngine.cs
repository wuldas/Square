using Square.CSS.Ast;
using Square.Graphics;
using Square.Text.Fonts;
using Square.UI;
using Square.UI.ElementApi;
using System.Globalization;
using System.Text;

namespace Square.CSS.Engine;

/// <summary>CSS 引擎，负责加载样式表、解析变量与匹配选择器并将声明应用到元素树。</summary>
public sealed class CssEngine
{
    private readonly List<(CssRule Rule, CssCascadeOrigin Origin)> _rules = [];
    private readonly List<(CssStyleSheet Sheet, CssCascadeOrigin Origin)> _styleSheets = [];
    private readonly List<CssFontFaceDescriptor> _fontFaceDescriptors = [];
    private readonly Dictionary<(CssFontFaceDescriptor Descriptor, string Path), FontFace> _fontFaces = [];
    private readonly Dictionary<string, KeyFramesRule> _keyFrames = new();
    private readonly Dictionary<string, Dictionary<string, string>> _themes = new();
    private string? _activeTheme;

    internal bool HasSiblingCombinators { get; private set; }
    public string MediaType { get; private set; } = "screen";

    /// <summary>与此 CSS 引擎关联的字体面集合；加载样式表时不自动加载字体。</summary>
    public FontFaceSet Fonts { get; } = new();

    /// <summary>已从所加载样式表中解析出的有效 <c>@font-face</c> 描述符。</summary>
    public IReadOnlyList<CssFontFaceDescriptor> FontFaceDescriptors => _fontFaceDescriptors;

    public CssEngine(string mediaType = "screen")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        MediaType = mediaType.Trim().ToLowerInvariant();
        LoadStyleSheet(CssUserAgentStyles.Sheet, CssCascadeOrigin.UserAgent);
    }

    /// <summary>加载样式表规则与关键帧。</summary>
    /// <param name="sheet">待加载的样式表。</param>
    public void LoadStyleSheet(CssStyleSheet sheet)
        => LoadStyleSheet(sheet, CssCascadeOrigin.Author);

    private void LoadStyleSheet(CssStyleSheet sheet, CssCascadeOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        _styleSheets.Add((sheet, origin));
        foreach (var atRule in sheet.AtRules)
        {
            if (!string.Equals(atRule.Name, "font-face", StringComparison.OrdinalIgnoreCase)) continue;
            var descriptor = ParseFontFaceDescriptor(atRule.Declarations);
            if (descriptor != null) _fontFaceDescriptors.Add(descriptor);
        }
        RebuildRules();
        foreach (var kf in sheet.KeyFrames) _keyFrames[kf.Name] = kf;
    }

    /// <summary>
    /// 加载样式表中的本地 <c>@font-face</c> 字体并注册到字体管理器。
    /// 网络、data 和其它非文件 URL 会被跳过；相对路径需要提供基目录。
    /// </summary>
    /// <param name="baseDirectory">相对字体路径的解析基目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task LoadFontsAsync(string? baseDirectory = null, CancellationToken cancellationToken = default)
    {
        var resolvedBaseDirectory = baseDirectory == null ? null : Path.GetFullPath(baseDirectory);
        foreach (var descriptor in _fontFaceDescriptors)
        {
            if (!descriptor.IsLocal) continue;
            var path = ResolveLocalFontPath(descriptor.Source, resolvedBaseDirectory);
            if (path == null) continue;

            if (!_fontFaces.TryGetValue((descriptor, path), out var face))
            {
                face = new FontFace(descriptor.Family, path, descriptor.Weight, descriptor.Style);
                _fontFaces[(descriptor, path)] = face;
                Fonts.Add(face);
            }

            await face.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void SetMediaType(string mediaType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        mediaType = mediaType.Trim().ToLowerInvariant();
        if (MediaType == mediaType) return;
        MediaType = mediaType;
        RebuildRules();
        CssStyleReconciler.InvalidateScopes(this);
    }

    private void RebuildRules()
    {
        _rules.Clear();
        HasSiblingCombinators = false;
        foreach (var (sheet, origin) in _styleSheets)
        {
            AddRules(sheet.Rules, origin);
            foreach (var mediaRule in sheet.MediaRules)
                if (mediaRule.MediaTypes.Any(MatchesMediaType))
                    AddRules(mediaRule.Rules, origin);
        }
    }

    private bool MatchesMediaType(string mediaType)
    {
        mediaType = mediaType.Trim();
        if (mediaType.Equals("all", StringComparison.OrdinalIgnoreCase)) return true;
        if (!mediaType.Equals("screen", StringComparison.OrdinalIgnoreCase) &&
            !mediaType.Equals("print", StringComparison.OrdinalIgnoreCase)) return false;
        return mediaType.Equals(MediaType, StringComparison.OrdinalIgnoreCase);
    }

    private void AddRules(IEnumerable<CssRule> rules, CssCascadeOrigin origin)
    {
        foreach (var rule in rules)
        {
            _rules.Add((rule, origin));
            HasSiblingCombinators |= rule.Selector.Steps.Any(step =>
                step.Combinator is Combinator.Adjacent or Combinator.GeneralSibling);
        }
    }

    private static CssFontFaceDescriptor? ParseFontFaceDescriptor(IReadOnlyList<Declaration> declarations)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in declarations)
        {
            if (declaration.Property.Equals("font-family", StringComparison.OrdinalIgnoreCase) ||
                declaration.Property.Equals("src", StringComparison.OrdinalIgnoreCase) ||
                declaration.Property.Equals("font-weight", StringComparison.OrdinalIgnoreCase) ||
                declaration.Property.Equals("font-style", StringComparison.OrdinalIgnoreCase))
                values[declaration.Property] = declaration.Value;
        }

        if (!values.TryGetValue("font-family", out var family) ||
            !TryNormalizeFamily(family, out family) ||
            !values.TryGetValue("src", out var source) ||
            !TryFindSource(source, out source) ||
            !TryParseWeight(values.GetValueOrDefault("font-weight"), out var weight) ||
            !TryParseStyle(values.GetValueOrDefault("font-style"), out var style))
            return null;

        return new CssFontFaceDescriptor(family, source, weight, style, IsLocalSource(source));
    }

    private static bool TryNormalizeFamily(string value, out string family)
    {
        family = value.Trim();
        if (family.Length >= 2 && family[0] is '\'' or '"' && family[^1] == family[0])
            family = family[1..^1];
        return !string.IsNullOrWhiteSpace(family) &&
               !family.Contains(',', StringComparison.Ordinal);
    }

    private static bool TryFindSource(string value, out string source)
    {
        source = "";
        string? firstCandidate = null;
        var index = 0;
        var depth = 0;
        var quote = '\0';
        while (index < value.Length)
        {
            var character = value[index];
            if (quote != '\0')
            {
                if (character == '\\') index += 2;
                else
                {
                    if (character == quote) quote = '\0';
                    index++;
                }
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                index++;
                continue;
            }

            if (character == '(')
            {
                depth++;
                index++;
                continue;
            }

            if (character == ')')
            {
                if (depth > 0) depth--;
                index++;
                continue;
            }

            if (depth != 0 || index + 3 > value.Length ||
                !value.AsSpan(index, 3).Equals("url".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                (index > 0 && IsIdentifierCharacter(value[index - 1])) ||
                (index + 3 < value.Length && IsIdentifierCharacter(value[index + 3])))
            {
                index++;
                continue;
            }

            var urlIndex = index;
            var openParen = urlIndex + 3;
            while (openParen < value.Length && char.IsWhiteSpace(value[openParen])) openParen++;
            if (openParen >= value.Length || value[openParen] != '(')
            {
                index = urlIndex + 3;
                continue;
            }

            var closeParen = FindClosingParen(value, openParen + 1);
            if (closeParen < 0) return false;
            var candidate = value[(openParen + 1)..closeParen].Trim();
            if (candidate.Length >= 2 && candidate[0] is '\'' or '"' && candidate[^1] == candidate[0])
                candidate = candidate[1..^1];
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                firstCandidate ??= candidate;
                if (IsLocalSource(candidate))
                {
                    source = candidate;
                    return true;
                }
            }

            index = closeParen + 1;
        }

        source = firstCandidate ?? "";
        return source.Length > 0;
    }

    private static int FindClosingParen(string value, int start)
    {
        var quote = '\0';
        for (var index = start; index < value.Length; index++)
        {
            var character = value[index];
            if (quote != '\0')
            {
                if (character == '\\') index++;
                else if (character == quote) quote = '\0';
                continue;
            }

            if (character is '\'' or '"') quote = character;
            else if (character == ')') return index;
        }

        return -1;
    }

    private static bool IsIdentifierCharacter(char character) =>
        char.IsLetterOrDigit(character) || character is '_' or '-';

    private static bool IsLocalSource(string source)
    {
        if (Path.IsPathRooted(source)) return true;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)) return true;
        return uri.IsFile;
    }

    private static string? ResolveLocalFontPath(string source, string? baseDirectory)
    {
        try
        {
            if (Path.IsPathRooted(source)) return Path.GetFullPath(source);
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
            {
                if (!uri.IsFile) return null;
                return Path.GetFullPath(uri.LocalPath);
            }

            if (baseDirectory == null) return null;
            return Path.GetFullPath(Path.Combine(baseDirectory, source.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool TryParseWeight(string? value, out FontWeight weight)
    {
        weight = FontWeight.Normal;
        if (string.IsNullOrWhiteSpace(value)) return true;
        value = value.Trim();
        if (value.Equals("normal", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals("bold", StringComparison.OrdinalIgnoreCase))
        {
            weight = FontWeight.Bold;
            return true;
        }

        if (!ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ||
            number is < 100 or > 900)
            return false;
        weight = (FontWeight)(number / 100 * 100);
        return true;
    }

    private static bool TryParseStyle(string? value, out FontStyle style)
    {
        style = FontStyle.Normal;
        if (string.IsNullOrWhiteSpace(value) || value.Equals("normal", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals("italic", StringComparison.OrdinalIgnoreCase))
        {
            style = FontStyle.Italic;
            return true;
        }
        if (value.Equals("oblique", StringComparison.OrdinalIgnoreCase))
        {
            style = FontStyle.Oblique;
            return true;
        }

        return false;
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

    /// <summary>返回当前元素匹配到的普通 CSS 规则，按级联顺序排列。</summary>
    public IReadOnlyList<CssInspectionRule> GetMatchedRules(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var matched = new List<(CssRule Rule, CssCascadeOrigin Origin, CssSpecificity Specificity, int Order)>();
        for (var index = 0; index < _rules.Count; index++)
        {
            var (rule, origin) = _rules[index];
            if (GetPseudoElement(rule.Selector) != null ||
                !TryMatchSelector(rule.Selector, element, out var specificity))
                continue;
            matched.Add((rule, origin, specificity, index));
        }

        matched.Sort((left, right) =>
        {
            var origin = left.Origin.CompareTo(right.Origin);
            if (origin != 0) return origin;
            var specificity = left.Specificity.CompareTo(right.Specificity);
            return specificity != 0 ? specificity : left.Order.CompareTo(right.Order);
        });

        return matched.Select(match => new CssInspectionRule(
            FormatSelector(match.Rule.Selector),
            match.Rule.Declarations.ToArray())).ToArray();
    }

    private static string FormatSelector(ComplexSelector selector)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < selector.Steps.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(selector.Steps[index - 1].Combinator switch
                {
                    Combinator.Child => " > ",
                    Combinator.Adjacent => " + ",
                    Combinator.GeneralSibling => " ~ ",
                    _ => " "
                });
            }

            foreach (var part in selector.Steps[index].Selector.Parts)
            {
                builder.Append(part.Kind switch
                {
                    SimpleSelectorKind.Type => part.Name,
                    SimpleSelectorKind.Class => "." + part.Name,
                    SimpleSelectorKind.Id => "#" + part.Name,
                    SimpleSelectorKind.Universal => "*",
                    SimpleSelectorKind.PseudoClass => ":" + part.Name,
                    SimpleSelectorKind.PseudoElement => "::" + part.Name,
                    SimpleSelectorKind.Attribute => FormatAttributeSelector(part),
                    _ => part.Name
                });
            }
        }
        return builder.ToString();
    }

    private static string FormatAttributeSelector(SimpleSelector selector)
    {
        if (selector.AttributeOperator == AttributeSelectorOperator.Presence)
            return $"[{selector.Name}]";

        var operation = selector.AttributeOperator switch
        {
            AttributeSelectorOperator.Equals => "=",
            AttributeSelectorOperator.Includes => "~=",
            AttributeSelectorOperator.DashMatch => "|=",
            AttributeSelectorOperator.PrefixMatch => "^=",
            AttributeSelectorOperator.SuffixMatch => "$=",
            AttributeSelectorOperator.SubstringMatch => "*=",
            _ => "?="
        };
        var value = selector.AttributeValue ?? "";
        var flag = selector.AttributeCaseSensitivity == AttributeCaseSensitivity.Insensitive ? " i" :
            selector.AttributeCaseSensitivity == AttributeCaseSensitivity.Sensitive ? " s" : "";
        return $"[{selector.Name}{operation}\"{value}\"{flag}]";
    }

    /// <summary>将匹配规则的声明应用到指定元素。</summary>
    /// <param name="Element">目标元素。</param>
    public void ApplyStyles(Element Element)
    {
        ApplyStylesCore(Element);
        foreach (var changed in FinalizePseudoElements(Element))
            changed.Invalidate(ElementInvalidation.Layout | ElementInvalidation.DisplayTree | ElementInvalidation.HitTest);
    }

    private void ApplyStylesCore(Element Element)
    {
        if (Element is CssGeneratedPseudoElement) return;
        ApplyInheritedProperties(Element);
        var matched = new List<(CssRule rule, CssCascadeOrigin origin, CssSpecificity specificity, int order, string? pseudoElement)>();

        for (var i = 0; i < _rules.Count; i++)
        {
            var (rule, origin) = _rules[i];
            if (TryMatchSelector(rule.Selector, Element, out var spec))
                matched.Add((rule, origin, spec, i, GetPseudoElement(rule.Selector)));
        }

        matched.Sort((a, b) =>
        {
            var origin = a.origin.CompareTo(b.origin);
            if (origin != 0) return origin;
            var specificity = a.specificity.CompareTo(b.specificity);
            return specificity != 0 ? specificity : a.order.CompareTo(b.order);
        });

        foreach (var (rule, origin, specificity, _, pseudoElement) in matched)
        {
            if (pseudoElement is "before" or "after" or "marker" or "invalid") continue;
            var isSelectionRule = string.Equals(pseudoElement, "selection", StringComparison.OrdinalIgnoreCase) ||
                                  IsLegacySelectionRule(rule.Selector);
            foreach (var decl in rule.Declarations)
            {
                var property = isSelectionRule ? MapSelectionProperty(decl.Property) : decl.Property;
                if (property == null) continue;
                ApplyDeclaration(Element, property, decl.Value, specificity, decl.Important, origin);
            }
        }

        ApplyThemeVariables(Element);

        foreach (var pseudoElement in new[] { "marker", "before", "after" })
        {
            if (pseudoElement == "marker" && !IsListItem(Element)) continue;
            CssGeneratedPseudoElement? generated = null;
            foreach (var (rule, origin, specificity, _, _) in matched.Where(match =>
                         string.Equals(match.pseudoElement, pseudoElement, StringComparison.OrdinalIgnoreCase)))
            {
                generated ??= EnsurePseudoElement(Element, pseudoElement);
                ApplyInheritedProperties(generated);
                foreach (var declaration in rule.Declarations)
                    ApplyDeclaration(
                        generated, declaration.Property, declaration.Value, specificity, declaration.Important, origin);
                ApplyThemeVariables(generated);
            }
        }

        if (IsListItem(Element))
        {
            var marker = EnsurePseudoElement(Element, "marker");
            ApplyInheritedProperties(marker);
            ApplyThemeVariables(marker);
        }
    }

    /// <summary>对整棵元素树应用样式并刷新动画。</summary>
    /// <param name="Element">根元素。</param>
    public void ApplyStylesToTree(Element Element)
    {
        CssStyleReconciler.ApplyScope(this, Element);
    }

    internal void ApplyStylesToTreeCore(Element Element)
    {
        ApplyStylesCore(Element);
        foreach (var child in Element.Children.ToArray())
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
                Element.Style.SetCascaded(
                    property, inherited, new CssSpecificity(-1, 0, 0), important: false,
                    origin: CssCascadeOrigin.Inherited,
                    authorSpecified: Element.Parent.Style.IsAuthorSpecified(property));
        }

        foreach (var pair in Element.Parent.Style.GetAll())
            if (pair.Key.StartsWith("--", StringComparison.Ordinal))
                Element.Style.SetCascaded(
                    pair.Key, pair.Value, new CssSpecificity(-1, 0, 0), important: false,
                    origin: CssCascadeOrigin.Inherited,
                    authorSpecified: Element.Parent.Style.IsAuthorSpecified(pair.Key));
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
                var siblings = SelectorChildren(parent);
                var currentIndex = siblings.IndexOf(current);
                var matchedSibling = false;
                for (var siblingIndex = currentIndex - 1; siblingIndex >= 0; siblingIndex--)
                {
                    var s = default(CssSpecificity);
                    var sibling = siblings[siblingIndex];
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
        var siblings = SelectorChildren(parent);
        var index = siblings.IndexOf(Element);
        return index > 0 ? siblings[index - 1] : null;
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
                case SimpleSelectorKind.PseudoElement:
                    if (!IsSupportedPseudoElement(part.Name)) return false;
                    specificity += new CssSpecificity(0, 0, 1);
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
            var index = SelectorChildren(Element.Parent).IndexOf(Element) + 1;
            return MatchesNthChild(argument, index);
        }

        if (lower.StartsWith("not(", StringComparison.Ordinal) && lower.EndsWith(')'))
            return !MatchSimpleArgument(Element, name[4..^1].Trim());

        return lower switch
        {
            "hover" => Element.HasState(ElementState.Hover),
            "focus" => Element.HasState(ElementState.Focus),
            "focus-visible" => Element.HasState(ElementState.Focus),
            "active" => Element.HasState(ElementState.Active),
            "disabled" => Element.HasState(ElementState.Disabled),
            "checked" => Element.HasState(ElementState.Checked),
            "open" => Element.HasState(ElementState.Open),
            "empty" => Element.ChildNodes.All(node => node is CssGeneratedPseudoElement),
            "first-child" => Element.Parent != null && SelectorChildren(Element.Parent).FirstOrDefault() == Element,
            "last-child" => Element.Parent != null && SelectorChildren(Element.Parent).LastOrDefault() == Element,
            "only-child" => Element.Parent != null && SelectorChildren(Element.Parent).Count == 1,
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

    private static bool IsLegacySelectionRule(ComplexSelector selector) => selector.Steps.Any(step =>
        step.Selector.Parts.Any(part => part.Kind == SimpleSelectorKind.PseudoClass &&
            string.Equals(part.Name, "selection", StringComparison.OrdinalIgnoreCase)));

    private static string? GetPseudoElement(ComplexSelector selector)
    {
        string? result = null;
        for (var stepIndex = 0; stepIndex < selector.Steps.Count; stepIndex++)
        {
            foreach (var part in selector.Steps[stepIndex].Selector.Parts)
            {
                if (part.Kind != SimpleSelectorKind.PseudoElement) continue;
                if (stepIndex != selector.Steps.Count - 1 || result != null) return "invalid";
                result = part.Name.ToLowerInvariant();
            }
        }
        return result;
    }

    private static bool IsSupportedPseudoElement(string name) =>
        name.Equals("before", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("after", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("marker", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("selection", StringComparison.OrdinalIgnoreCase);

    private static List<Element> SelectorChildren(Element parent) =>
        parent.Children.Where(child => child is not CssGeneratedPseudoElement).ToList();

    private static CssGeneratedPseudoElement EnsurePseudoElement(Element owner, string name)
    {
        var existing = owner.Children.OfType<CssGeneratedPseudoElement>()
            .FirstOrDefault(child => child.PseudoElementName == name);
        if (existing != null) return existing;

        var generated = new CssGeneratedPseudoElement(name);
        using (Element.SuppressInvalidation())
        {
            if (name is "before" or "marker") owner.Children.Insert(0, generated);
            else owner.Children.Add(generated);
        }
        return generated;
    }

    internal static IReadOnlyCollection<Element> FinalizePseudoElements(Element root) =>
        CssGeneratedContentEvaluator.Evaluate(root);

    private static bool IsListItem(Element element) =>
        string.Equals(element.Style.Get("display")?.Trim(), "list-item", StringComparison.OrdinalIgnoreCase);

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
        bool important,
        CssCascadeOrigin origin)
    {
        if (string.Equals(property, "animation", StringComparison.OrdinalIgnoreCase))
        {
            ApplyAnimationShorthand(Element, value, specificity, important, origin);
            return;
        }

        Element.Style.SetCascaded(property, value, specificity, important, origin: origin);
    }

    private static void ApplyAnimationShorthand(
        Element Element,
        string value,
        CssSpecificity specificity,
        bool important,
        CssCascadeOrigin origin)
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

        if (name.Length > 0) Element.Style.SetCascaded("animation-name", name, specificity, important, origin: origin);
        if (duration.Length > 0) Element.Style.SetCascaded("animation-duration", duration, specificity, important, origin: origin);
        if (timingFunction.Length > 0) Element.Style.SetCascaded("animation-timing-function", timingFunction, specificity, important, origin: origin);
        if (delay.Length > 0) Element.Style.SetCascaded("animation-delay", delay, specificity, important, origin: origin);
        if (iterationCount.Length > 0) Element.Style.SetCascaded("animation-iteration-count", iterationCount, specificity, important, origin: origin);
        if (direction.Length > 0) Element.Style.SetCascaded("animation-direction", direction, specificity, important, origin: origin);
    }

    private static bool IsTime(string value) =>
        value.EndsWith("ms", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith('s') && float.TryParse(value[..^1], out _);
}
