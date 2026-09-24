using System.Globalization;
using Square.Controls;
using Square.Graphics;
using Square.UI.ElementApi;
using ImageControl = Square.Controls.Image;

namespace Square.UI.Html;

/// <summary>
/// A conforming HTML element acting as the semantic host inside Square's ordinary layout and event tree.
/// Replaced and form elements delegate painting and interaction to an internal Square control child
/// (the "native proxy"); <see cref="IsInternalHtmlProxy"/> lets the Web exporter skip those children.
/// Active behaviors that Square does not implement (playback, canvas scripting, document/plugin loading,
/// network form submission, script execution) are rendered as clearly labeled static placeholders.
/// </summary>
public sealed partial class HtmlElement : HTMLElement
{
    private readonly string _tagName;
    private Dictionary<string, string>? _attributes;

    /// <summary>Native-only control children synthesized for replaced/form hosts.</summary>
    private readonly List<UIElement> _proxies = [];
    /// <summary>Last option entries mirrored into a select proxy; rebuild guard.</summary>
    private List<(string Value, bool Selected)>? _syncedSelectOptions;
    /// <summary>Attribute or child changes that the next measure/paint must reconcile.</summary>
    private bool _syncPending = true;
    /// <summary>Display value last written by the dynamic UA display rule (hidden/dialog/input[type=hidden]).</summary>
    private string? _dynamicDisplay;

    /// <summary>Creates the HTML element for a catalog tag name (case-normalized to lowercase).</summary>
    public HtmlElement(string tagName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
        var canonical = tagName.ToLowerInvariant();
        if (!HtmlTagCatalog.IsTag(canonical))
            throw new ArgumentException($"Unknown HTML element '{tagName}'.", nameof(tagName));
        _tagName = canonical;
        ApplyUserAgentStyle();
        WireInteractions();
    }

    public override string TagName => _tagName;

    public bool HasAttribute(string name)
    {
        name = Normalize(name);
        return name switch
        {
            "id" => Id != null,
            "class" => ClassList.GetAll().Count != 0,
            "style" => Style.CssText.Length != 0,
            _ => _attributes?.ContainsKey(name) ?? false
        };
    }

    public string? GetAttribute(string name)
    {
        name = Normalize(name);
        return name switch
        {
            "id" => Id,
            "class" => HasAttribute("class") ? ClassList.ToClassString() : null,
            "style" => HasAttribute("style") ? Style.CssText : null,
            _ => _attributes != null && _attributes.TryGetValue(name, out var value) ? value : null
        };
    }

    public void SetAttribute(string name, string value)
    {
        name = Normalize(name);
        ArgumentNullException.ThrowIfNull(value);
        if (name == "id") { Id = value; return; }
        if (name is "class" or "style") { SetProperty(name, value); return; }
        SetProperty(name, value);
    }

    public bool RemoveAttribute(string name)
    {
        name = Normalize(name);
        if (name == "id") { if (Id == null) return false; Id = null; return true; }
        if (name == "class") { if (!HasAttribute(name)) return false; ClassList.Clear(); return true; }
        if (name == "style") { if (!HasAttribute(name)) return false; Style.CssText = ""; return true; }
        return RemoveProperty(name);
    }

    /// <summary>Snapshot only on request; runtime queries never allocate a dictionary.</summary>
    public IReadOnlyDictionary<string, string> GetAttributes()
    {
        var snapshot = _attributes == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(_attributes, StringComparer.Ordinal);
        if (Id != null) snapshot["id"] = Id;
        if (HasAttribute("class")) snapshot["class"] = ClassList.ToClassString();
        if (HasAttribute("style")) snapshot["style"] = Style.CssText;
        return snapshot;
    }

    /// <summary>
    /// Reports native-only children: synthesized image/form/media proxies and Square's list marker.
    /// Browsers draw their own list marker; neither kind should become an extra HTML child.
    /// </summary>
    public bool IsInternalHtmlProxy(Element? child) => child != null &&
        (_proxies.Contains(child) || _tagName == "li" &&
            child is Square.CSS.Engine.CssGeneratedPseudoElement { PseudoElementName: "marker" });

    /// <inheritdoc />
    /// <remarks>
    /// Returns the content-box size (padding and border are added by the CSS layout engine):
    /// intrinsic sizes for replaced/media elements and proxy controls, the concatenated or stacked
    /// child size for ordinary hosts, and zero for line-break/rule boxes. This replaces the
    /// <see cref="UIElement.Measure"/> default which would report the unconstrained available size
    /// (infinite height) for empty boxes.
    /// </remarks>
    public override Size Measure(Size availableSize)
    {
        EnsureSynchronized();
        return MeasureNative(availableSize);
    }

    /// <inheritdoc />
    /// <remarks>Draws disclosure markers, link/text decoration, gauges and disabled-content labels
    /// that the CSS box painter cannot express.</remarks>
    public override void Paint(IRenderContext ctx)
    {
        EnsureSynchronized();
        PaintNative(ctx);
    }

    protected override void OnPropertyChanged(string name)
    {
        base.OnPropertyChanged(name);
        if (name == nameof(Id)) return;
        var normalized = name.ToLowerInvariant();
        if (normalized == "id")
        {
            Id = Properties.TryGetValue<object>(name, out var boundId) && boundId != null
                ? Convert.ToString(boundId, CultureInfo.InvariantCulture)
                : null;
            return;
        }
        if (normalized is "class" or "style") return;
        if (!string.Equals(name, normalized, StringComparison.Ordinal) && name != nameof(Id)) return;
        if (!Properties.TryGetValue<object>(name, out var value) || value == null ||
            value is bool boolean && !boolean && HtmlTagCatalog.BooleanAttributes.Contains(normalized))
        {
            _attributes?.Remove(normalized);
        }
        else
        {
            (_attributes ??= new Dictionary<string, string>(StringComparer.Ordinal))[normalized] =
                value is bool present && HtmlTagCatalog.BooleanAttributes.Contains(normalized)
                    ? ""
                    : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }
        if (normalized == "disabled") IsDisabled = HasAttribute("disabled");
        if (normalized is "checked" or "open")
            SetState(normalized == "checked" ? ElementState.Checked : ElementState.Open, HasAttribute(normalized));
        if (normalized == "title") Tooltip = GetAttribute("title");
        if (_tagName == "details" && normalized == "open") SyncDisclosure();
        _syncPending = true;
        Invalidate(ElementInvalidation.Style | ElementInvalidation.Layout | ElementInvalidation.Paint);
    }

    internal override void OnChildAdded(Element child)
    {
        base.OnChildAdded(child);
        if (_tagName == "details")
            SetChildVisible(child, HasAttribute("open") || child is HtmlElement { TagName: "summary" });
        // Late-attached children can change select options and the details disclosure state.
        _syncPending = true;
    }

    private static string Normalize(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.ToLowerInvariant();
    }

    /// <summary>Lowest-priority persistent UA declaration; author CSS and classes override it.</summary>
    private void SetUserAgentStyle(string property, string value) =>
        Style.SetCascaded(property, value, new CssSpecificity(0, 0, 0), important: false, persistent: true,
            origin: CssCascadeOrigin.UserAgent, authorSpecified: false);

    private void ApplyUserAgentStyle()
    {
        SetUserAgentStyle("display", ResolveStaticDisplay());
        if (_tagName is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
        {
            SetUserAgentStyle("font-weight", "bold");
            SetUserAgentStyle("font-size", _tagName switch
            {
                "h1" => "2em", "h2" => "1.5em", "h3" => "1.17em", "h5" => "0.83em", "h6" => "0.67em", _ => "1em"
            });
            SetUserAgentStyle("margin-top", "0.67em");
            SetUserAgentStyle("margin-bottom", "0.67em");
        }
        if (_tagName is "p" or "ul" or "ol" or "dl" or "pre" or "blockquote" or "figure" or "details")
        { SetUserAgentStyle("margin-top", "1em"); SetUserAgentStyle("margin-bottom", "1em"); }
        if (_tagName is "ul" or "ol" or "menu") SetUserAgentStyle("padding-left", "40px");
        if (_tagName == "ol") SetUserAgentStyle("list-style-type", "decimal");
        if (_tagName is "ul" or "menu") SetUserAgentStyle("list-style-type", "disc");
        if (_tagName == "dd" || _tagName == "blockquote") SetUserAgentStyle("margin-left", "40px");
        if (_tagName is "pre" or "code" or "kbd" or "samp") SetUserAgentStyle("font-family", "monospace");
        if (_tagName is "pre" or "textarea") SetUserAgentStyle("white-space", "pre-wrap");
        if (_tagName is "strong" or "b" or "th") SetUserAgentStyle("font-weight", "bold");
        if (_tagName is "em" or "i" or "address" or "var" or "cite" or "dfn") SetUserAgentStyle("font-style", "italic");
        if (_tagName == "small") SetUserAgentStyle("font-size", "0.83em");
        if (_tagName is "sub" or "sup") SetUserAgentStyle("font-size", "0.83em");
        if (_tagName == "mark") SetUserAgentStyle("background-color", "#ffff00");
        if (_tagName == "a") SetUserAgentStyle("color", "#0000ee");
        if (_tagName == "summary")
        {
            SetUserAgentStyle("padding-left", "1.2em");
            SetUserAgentStyle("cursor", "default");
        }
        if (_tagName == "fieldset")
        {
            SetUserAgentStyle("margin", "0 2px");
            SetUserAgentStyle("border", "1px solid #a9a9a9");
            SetUserAgentStyle("padding", "0.35em 0.75em 0.625em");
        }
        if (_tagName == "button")
        {
            SetUserAgentStyle("font-size", "13.3333px");
            SetUserAgentStyle("text-align", "center");
            SetUserAgentStyle("padding", "1px 6px");
            SetUserAgentStyle("border", "2px outset #d4d4d4");
            SetUserAgentStyle("background-color", "#efefef");
        }
        if (_tagName == "hr")
        {
            SetUserAgentStyle("border-top", "1px solid #808080");
            SetUserAgentStyle("margin-top", "0.5em");
            SetUserAgentStyle("margin-bottom", "0.5em");
        }
    }

    /// <summary>
    /// Reconciles attributes and late-attached children with the native proxy controls and
    /// visibility-dependent states. Every write is guarded so a converged pass performs no
    /// invalidation, which keeps the call from Measure/Paint loop-free.
    /// </summary>
    private void EnsureSynchronized()
    {
        if (!_syncPending) return;
        _syncPending = false;
        SyncDynamicDisplay();
        SyncDirectionOverride();
        switch (_tagName)
        {
            case "img": SyncImage(); break;
            case "video": SyncPosterProxy(GetAttribute("poster")); break;
            case "input" or "textarea": SyncTextControl(); break;
            case "select": SyncSelectControl(); break;
            case "details": SyncDisclosure(); break;
            case "progress" or "meter" or "iframe": SyncFallbackHidden(); break;
            case "td" or "th":
                SyncTableSpan("colspan", "column-span");
                SyncTableSpan("rowspan", "row-span");
                break;
        }
    }

    /// <summary>
    /// Intrinsic content-box size. br/wbr/hr are zero boxes (the engine breaks the line and the
    /// rule's border paints), replaced elements report their proxy or default replaced size, and
    /// ordinary hosts concatenate inline or stack block children.
    /// </summary>
    private Size MeasureNative(Size availableSize)
    {
        switch (_tagName)
        {
            case "br" or "wbr" or "hr": return Size.Zero;
            case "img": return MeasureImage(availableSize);
            case "video":
                var poster = ProxyOf<ImageControl>();
                return poster is { IsVisible: true } ? poster.Measure(availableSize) : new Size(300, 150);
            case "audio": return new Size(300, 54);
            case "iframe" or "embed": return new Size(300, 150);
            case "progress" or "meter": return new Size(160, 16);
            case "input" or "textarea" or "select":
                return MeasureProxy(availableSize) ?? Size.Zero;
            // canvas keeps its 300x150 replaced default unless fallback children give it size;
            // object shows its text fallback the same way.
            case "canvas" or "object":
            {
                var content = MeasureContent(availableSize);
                return content == Size.Zero ? new Size(300, 150) : content;
            }
            default: return MeasureContent(availableSize);
        }
    }

    private Size MeasureImage(Size availableSize)
    {
        var proxy = ProxyOf<ImageControl>();
        if (proxy is { IsVisible: true }) return proxy.Measure(availableSize);
        var alt = GetAttribute("alt");
        if (string.IsNullOrEmpty(alt)) return Size.Zero;
        return ControlDrawing.MeasureText(this, alt, 13.3333f);
    }

    private Size? MeasureProxy(Size availableSize) => ProxyOf<UIElement>()?.Measure(availableSize);

    /// <summary>Concatenates inline children horizontally; stacks block children vertically.</summary>
    private Size MeasureContent(Size availableSize)
    {
        var inline = string.Equals((Style.Get("display") ?? "").Trim(), "inline", StringComparison.Ordinal);
        var width = 0f;
        var height = 0f;
        foreach (var child in Children)
        {
            if (!child.IsVisible || IsDisplayNone(child)) continue;
            var measured = child.Measure(new Size(
                inline ? float.MaxValue : Math.Max(0f, availableSize.Width),
                float.MaxValue));
            if (inline)
            {
                width += Math.Max(0f, measured.Width);
                height = Math.Max(height, measured.Height);
            }
            else
            {
                height += Math.Max(0f, measured.Height);
                width = Math.Max(width, measured.Width);
            }
        }
        return new Size(width, height);
    }

    private static bool IsDisplayNone(Element element) =>
        string.Equals((element.Style.Get("display") ?? "").Trim(), "none", StringComparison.Ordinal);

    private T? ProxyOf<T>() where T : UIElement
    {
        foreach (var proxy in _proxies)
            if (proxy is T typed) return typed;
        return default;
    }

    private bool HasVisibleHtmlContent()
    {
        foreach (var child in Children)
            if (child.IsVisible && !IsDisplayNone(child)) return true;
        return false;
    }

    /// <summary>Draws everything the CSS box painter and laid-out children cannot.</summary>
    private void PaintNative(IRenderContext ctx)
    {
        switch (_tagName)
        {
            case "summary": HtmlElementPainting.PaintDisclosureMarker(ctx, this); return;
            case "q": HtmlElementPainting.PaintQuotes(ctx, this); return;
            case "a" or "u" or "ins": HtmlElementPainting.PaintRunDecoration(ctx, this, lineThrough: false); return;
            case "s" or "del": HtmlElementPainting.PaintRunDecoration(ctx, this, lineThrough: true); return;
            case "img":
                if (ProxyOf<ImageControl>() is not { IsVisible: true }) HtmlElementPainting.PaintAltText(ctx, this);
                return;
            case "audio":
                HtmlElementPainting.PaintUnavailableRegion(ctx, this, "Playback unavailable", dark: true);
                return;
            case "video":
                if (ProxyOf<ImageControl>() is not { IsVisible: true })
                    HtmlElementPainting.PaintUnavailableRegion(ctx, this, "Playback unavailable", dark: true);
                return;
            case "iframe" or "embed":
                HtmlElementPainting.PaintUnavailableRegion(ctx, this, "Embedded content disabled", dark: false);
                return;
            case "object":
                if (!HasVisibleHtmlContent())
                    HtmlElementPainting.PaintUnavailableRegion(ctx, this, "Embedded content disabled", dark: false);
                return;
            case "progress": HtmlElementPainting.PaintProgress(ctx, this); return;
            case "meter": HtmlElementPainting.PaintMeter(ctx, this); return;
        }
    }

    /// <summary>hidden attribute, dialog open state and input[type=hidden] drive a UA display override.</summary>
    private void SyncDynamicDisplay()
    {
        string? desired = null;
        if (HasAttribute("hidden")) desired = "none";
        else if (_tagName == "dialog") desired = HasAttribute("open") ? "block" : "none";
        else if (_tagName == "input" && (GetAttribute("type") ?? "text") == "hidden") desired = "none";
        if (Style.IsAuthorSpecified("display"))
        {
            _dynamicDisplay = null;
            return;
        }
        if (_dynamicDisplay == desired) return;
        _dynamicDisplay = desired;
        SetUserAgentStyle("display", desired ?? ResolveStaticDisplay());
    }

    /// <summary>The display value assigned once at construction (mirrors ApplyUserAgentStyle).</summary>
    private string ResolveStaticDisplay() => _tagName switch
    {
        "table" => "table",
        "thead" => "table-header-group", "tbody" => "table-row-group", "tfoot" => "table-footer-group",
        "tr" => "table-row", "th" or "td" => "table-cell", "caption" => "table-caption",
        "colgroup" => "table-column-group", "col" => "table-column",
        "li" => "list-item",
        "noscript" => "block",
        "dialog" => "none",
        "map" or "area" => "none",
        _ when HtmlTagCatalog.IsMetadata(_tagName) || _tagName is "datalist" or "option" or "optgroup" or "source" or "track" or "selectedcontent" => "none",
        _ when HtmlTagCatalog.IsReplaced(_tagName) || _tagName is "button" => "inline-block",
        _ when HtmlTagCatalog.IsBlock(_tagName) => "block",
        _ => "inline"
    };

    /// <summary>bdo/bdi map their dir attribute onto the CSS direction the text layout reads.</summary>
    private void SyncDirectionOverride()
    {
        if (_tagName is not ("bdo" or "bdi")) return;
        var dir = (GetAttribute("dir") ?? "").Trim().ToLowerInvariant();
        var desired = dir == "rtl" ? "rtl" : "ltr";
        if (string.Equals(Style.Get("direction"), desired, StringComparison.Ordinal)) return;
        SetUserAgentStyle("direction", desired);
    }

    /// <summary>Replaced hosts keep fallback children invisible while the native widget paints.</summary>
    private void SyncFallbackHidden()
    {
        foreach (var child in Children)
            SetChildVisible(child, false);
    }

    private static void SetChildVisible(Element child, bool visible)
    {
        if (child.IsVisible != visible) child.IsVisible = visible;
    }

    /// <summary>Maps colspan/rowspan attributes onto the style keys the table layout engine reads.</summary>
    private void SyncTableSpan(string attribute, string styleProperty)
    {
        var raw = GetAttribute(attribute);
        var current = Style.Get(styleProperty);
        if (raw == null)
        {
            if (current != null && !Style.IsAuthorSpecified(styleProperty)) SetUserAgentStyle(styleProperty, "1");
            return;
        }
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var span) || span < 1) span = 1;
        if (current == span.ToString(CultureInfo.InvariantCulture)) return;
        SetUserAgentStyle(styleProperty, span.ToString(CultureInfo.InvariantCulture));
    }
}
