using System.Globalization;
using Square.Controls;
using Square.Graphics;
using Square.Runtime;
using Square.UI;
using Square.UI.ElementApi;
using ImageControl = Square.Controls.Image;

namespace Square.Html;

/// <summary>
/// Shared semantic host for WHATWG HTML elements inside Square's ordinary layout and event tree.
/// Each built-in tag maps to one concrete <c>HTML*Element</c> class (see <c>HtmlElements</c>) that
/// derives from this class — directly or through the six shared semantic bases — with a fixed
/// lowercase local name; registered custom elements reuse the protected constructor. Replaced and
/// form elements delegate painting and interaction to internal Square control <em>sidecars</em>
/// (<see cref="VisualSidecars"/>): visual children that never enter <see cref="Square.UI.Element.ChildNodes"/>,
/// <see cref="Square.UI.Element.Children"/>, DOM queries or the Web exporter — they ride on
/// <see cref="Square.UI.Element.VisualParent"/> for invalidation and event bubbling instead.
/// The CSS list marker of <c>li</c> is a sidecar of the same kind. Active behaviors that Square
/// does not implement (playback, canvas scripting, document/plugin loading, network form
/// submission, script execution) render as clearly labeled static placeholders.
/// </summary>
public abstract partial class HTMLElement : Square.UI.Element, IFocusableElement, ITextDataDependent
{
    private readonly string _localName;
    private readonly ElementFocusState _focusState = new();
    private HashSet<string>? _observedAttributes;
    private Dictionary<string, string?>? _lastObservedValues;
    private List<Exception>? _pendingCustomErrors;
    private bool _customConnected;

    public string? CustomElementName { get; private set; }
    public string? CustomElementExtendsTag { get; private set; }
    public bool IsDefinedCustomElement => CustomElementName != null;

    /// <summary>
    /// Creates the element with a fixed local name. HTML names are ASCII case-insensitive, so the
    /// name is stored ASCII-lowercased. Built-ins pass their catalog tag; custom element
    /// definitions pass their registered name.
    /// </summary>
    protected HTMLElement(string localName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localName);
        _localName = ElementRegistry.Normalize(ElementRegistry.HtmlNamespaceUri, localName);
        ApplyUserAgentStyle();
        WireInteractions();
    }

    /// <inheritdoc />
    public sealed override string TagName => _localName;

    /// <inheritdoc />
    public sealed override string LocalName => _localName;

    /// <inheritdoc />
    public sealed override string? NamespaceURI => "http://www.w3.org/1999/xhtml";

    /// <inheritdoc />
    public sealed override bool HasCustomMeasure => true;

    /// <summary>Default font size for HTML text without an explicit CSS <c>font-size</c> (16px).</summary>
    internal const float HtmlTextDefaultFontSize = 16f;

    /// <summary>
    /// Replaced/form hosts render their proxy or a placeholder instead of markup text; their
    /// DOM Text fallback children stay unpainted like their hidden element children.
    /// </summary>
    internal bool AcceptsDirectTextContent =>
        TagName is not ("img" or "video" or "audio" or "iframe" or "embed" or "progress" or "meter" or
            "input" or "textarea" or "select");

    /// <summary>Whether any direct DOM Text child carries visible content.</summary>
    internal bool HasDirectTextContent
    {
        get
        {
            foreach (var node in ChildNodes)
                if (node is Square.UI.Text { Data.Length: > 0 }) return true;
            return false;
        }
    }

    /// <summary>是否禁用（对齐表单 disabled 语义）。</summary>
    public bool IsDisabled
    {
        get => GetProperty<bool>(nameof(IsDisabled));
        set => SetProperty(nameof(IsDisabled), value);
    }

    /// <summary>是否启用（与 <see cref="IsDisabled"/> 互反）。</summary>
    public bool IsEnabled
    {
        get => !IsDisabled;
        set => IsDisabled = !value;
    }

    /// <summary>是否拥有键盘焦点。</summary>
    public bool IsFocused => _focusState.IsFocused;

    /// <summary>悬停提示文本（Square 扩展；HTML 的 <c>title</c> 属性写入此表面）。</summary>
    public string? Tooltip { get; set; }

    /// <summary>获取焦点：派发不冒泡的 <c>focus</c> 与冒泡的 <c>focusin</c>（对齐 DOM 焦点事件）。</summary>
    public void Focus() => Focus(focusVisible: true);

    internal void Focus(bool focusVisible) => _focusState.Focus(this, focusVisible);

    /// <summary>失去焦点：派发不冒泡的 <c>blur</c> 与冒泡的 <c>focusout</c>。</summary>
    public void Unfocus() => _focusState.Unfocus(this);

    bool IFocusableElement.IsFocused => IsFocused;

    bool IFocusableElement.IsEnabled => IsEnabled;

    void IFocusableElement.Focus(bool focusVisible) => Focus(focusVisible);

    void IFocusableElement.Unfocus() => Unfocus();
    protected virtual void ConnectedCallback() { }
    protected virtual void DisconnectedCallback() { }
    protected virtual void AttributeChangedCallback(string name, string? oldValue, string? newValue) { }

    internal void InitializeCustomDefinition(string name, string? extendsTag, IReadOnlyList<string> observedAttributes)
    {
        if (CustomElementName != null)
        {
            if (CustomElementName == name && CustomElementExtendsTag == extendsTag) return;
            throw new InvalidOperationException("A custom element definition cannot be changed after initialization.");
        }
        CustomElementName = name;
        CustomElementExtendsTag = extendsTag;
        _observedAttributes = new HashSet<string>(observedAttributes, StringComparer.Ordinal);
        _lastObservedValues = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var attribute in _observedAttributes)
            _lastObservedValues.Add(attribute, GetAttribute(attribute));
        if (IsAttached) ConnectCustomElement();
    }

    internal void NotifyDomAttributeMutation(string name) => NotifyObservedAttribute(name);

    private void NotifyObservedAttribute(string name)
    {
        if (_observedAttributes?.Contains(name) != true) return;
        var next = GetAttribute(name);
        _lastObservedValues!.TryGetValue(name, out var previous);
        if (previous == next) return;
        _lastObservedValues[name] = next;
        try { AttributeChangedCallback(name, previous, next); }
        catch (Exception exception) { ReportCustomError(exception); }
    }

    private void ConnectCustomElement()
    {
        if (CustomElementName == null || _customConnected) return;
        if (OwnerDocument is UIDocument document && _pendingCustomErrors != null)
        {
            foreach (var exception in _pendingCustomErrors) document.CustomElements.Report(this, exception);
            _pendingCustomErrors.Clear();
        }
        _customConnected = true;
        try { ConnectedCallback(); }
        catch (Exception exception) { ReportCustomError(exception); }
    }

    private void ReportCustomError(Exception exception)
    {
        if (IsAttached && OwnerDocument is UIDocument document) document.CustomElements.Report(this, exception);
        else (_pendingCustomErrors ??= []).Add(exception);
    }


    /// <inheritdoc />
    /// <remarks>Sidecar 随宿主脱离文档：仅卸载/脱离生命周期（宿主可能重新挂载）；
    /// 代理替换时才释放监听与列表成员（见 <see cref="DetachSidecar"/>）。</remarks>
    protected override void OnDetachedCore()
    {
        foreach (var sidecar in _visualSidecars)
        {
            if (sidecar.IsLoaded) ((IComponentLifecycle)sidecar).OnUnloaded();
            if (sidecar.IsAttached) ((IComponentLifecycle)sidecar).OnDetached();
        }
        if (_customConnected)
        {
            _customConnected = false;
            try { DisconnectedCallback(); }
            catch (Exception exception) { ReportCustomError(exception); }
        }
        _focusState.Reset(this);
        base.OnDetachedCore();
    }

    /// <inheritdoc />
    /// <remarks>Sidecar 跟随宿主进入文档：先传播 OwnerDocument，再按宿主状态挂载/加载。</remarks>
    protected override void OnAttachedCore()
    {
        base.OnAttachedCore();
        if (OwnerDocument != null)
            foreach (var sidecar in _visualSidecars)
                if (!ReferenceEquals(sidecar.OwnerDocument, OwnerDocument))
                    OwnerDocument.AssignOwnerDocument(sidecar);
        foreach (var sidecar in _visualSidecars)
        {
            if (!sidecar.IsAttached) ((IComponentLifecycle)sidecar).OnAttached();
            if (IsLoaded && !sidecar.IsLoaded) ((IComponentLifecycle)sidecar).OnLoaded();
        }
        ConnectCustomElement();
    }

    /// <inheritdoc />
    protected override void OnLoadedCore()
    {
        base.OnLoadedCore();
        foreach (var sidecar in _visualSidecars)
            if (!sidecar.IsLoaded) ((IComponentLifecycle)sidecar).OnLoaded();
    }

    /// <inheritdoc />
    protected override void OnUnloadedCore()
    {
        base.OnUnloadedCore();
        foreach (var sidecar in _visualSidecars)
            if (sidecar.IsLoaded) ((IComponentLifecycle)sidecar).OnUnloaded();
    }


    // ----- attribute storage -----

    private Dictionary<string, string>? _attributes;

    /// <summary>Visual sidecars: synthesized image/form controls plus the CSS list marker.
    /// Never members of <see cref="Square.UI.Element.Children"/>/<see cref="Square.UI.Element.ChildNodes"/>.</summary>
    private readonly List<Element> _visualSidecars = [];
    /// <summary>The li list-marker pseudo element, when the host currently renders one.</summary>
    private Square.CSS.Engine.CssGeneratedPseudoElement? _listMarker;
    /// <summary>Set while the normal-flow line layout positioned the marker; the next Arrange consumes it.</summary>
    private bool _markerLaidByLine;
    /// <summary>Per-proxy listener handles; disposed when the proxy is replaced or removed.</summary>
    private Dictionary<Element, List<IDisposable>>? _proxyListeners;
    /// <summary>Last option entries mirrored into a select proxy; rebuild guard.</summary>
    private List<(string Value, bool Selected)>? _syncedSelectOptions;
    /// <summary>Attribute or child changes that the next measure/paint must reconcile.</summary>
    private bool _syncPending = true;
    /// <summary>Display value last written by the dynamic UA display rule (hidden/dialog/input[type=hidden]).</summary>
    private string? _dynamicDisplay;

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
    /// Internal visual sidecars: the synthesized image/form controls and the CSS list marker.
    /// Exposed for the renderer (DisplayTree), layout engine and platform focus routing — they
    /// paint, hit-test and measure alongside <see cref="Square.UI.Element.Children"/> but are
    /// invisible to the DOM surface (<c>childNodes</c>/<c>children</c>, queries, Web export).
    /// </summary>
    internal override IReadOnlyList<Element> VisualSidecars => _visualSidecars;

    /// <summary>Attaches a sidecar to this host: visual parent, owner document, lifecycle and invalidation.</summary>
    private void AttachSidecar(Element sidecar)
    {
        sidecar.VisualParent = this;
        if (OwnerDocument != null && !ReferenceEquals(sidecar.OwnerDocument, OwnerDocument))
            OwnerDocument.AssignOwnerDocument(sidecar);
        _visualSidecars.Add(sidecar);
        InvalidateHitTestOrder();
        Invalidate(ElementInvalidation.Style | ElementInvalidation.Layout);
        if (IsAttached) ((IComponentLifecycle)sidecar).OnAttached();
        if (IsLoaded && !sidecar.IsLoaded) ((IComponentLifecycle)sidecar).OnLoaded();
    }

    /// <summary>
    /// Removes a sidecar: cancels its listeners, runs its unload/detach lifecycle exactly once,
    /// releases it from the host and restores invalidation to the semantic parent chain.
    /// </summary>
    private void DetachSidecar(Element sidecar)
    {
        if (_proxyListeners != null && _proxyListeners.Remove(sidecar, out var handles))
            foreach (var handle in handles) handle.Dispose();
        if (sidecar.IsLoaded) ((IComponentLifecycle)sidecar).OnUnloaded();
        if (sidecar.IsAttached) ((IComponentLifecycle)sidecar).OnDetached();
        _visualSidecars.Remove(sidecar);
        if (ReferenceEquals(_listMarker, sidecar)) _listMarker = null;
        sidecar.VisualParent = null;
        InvalidateHitTestOrder();
        Invalidate(ElementInvalidation.Style | ElementInvalidation.Layout);
    }

    /// <summary>Registers the CSS-generated list marker as this host's visual sidecar (never a DOM child).</summary>
    internal void AttachListMarker(Square.CSS.Engine.CssGeneratedPseudoElement marker)
    {
        if (ReferenceEquals(_listMarker, marker)) return;
        if (_listMarker != null) DetachSidecar(_listMarker);
        _listMarker = marker;
        _markerLaidByLine = false;
        AttachSidecar(marker);
    }

    /// <summary>Current list-marker sidecar, or null when the host renders none.</summary>
    internal Square.CSS.Engine.CssGeneratedPseudoElement? FindListMarker() => _listMarker;

    /// <summary>Removes the list-marker sidecar (e.g. the element stopped being a list item).</summary>
    internal void DetachListMarker()
    {
        if (_listMarker == null) return;
        _markerLaidByLine = false;
        DetachSidecar(_listMarker);
    }

    /// <summary>Whether the given sidecar is this host's list marker.</summary>
    internal bool IsListMarker(Element sidecar) => ReferenceEquals(sidecar, _listMarker);

    /// <summary>Returns the visible list marker so the line layout can seed it as the first inline piece.</summary>
    internal bool TryGetListMarker(out Element marker)
    {
        if (_listMarker is { IsVisible: true } visible && !IsDisplayNone(visible))
        {
            marker = visible;
            return true;
        }
        marker = null!;
        return false;
    }

    /// <summary>Records that the normal-flow line layout positioned the marker for the pending Arrange.</summary>
    internal void MarkListMarkerLaidByLine() => _markerLaidByLine = true;

    /// <summary>First synthesized control sidecar (label targeting); the list marker does not count.</summary>
    private UIElement? FirstProxySidecar
    {
        get
        {
            foreach (var sidecar in _visualSidecars)
                if (sidecar is UIElement control && sidecar is not Square.CSS.Engine.CssGeneratedPseudoElement)
                    return control;
            return null;
        }
    }

    /// <summary>
    /// Positions the sidecars inside the host content box: controls at the content origin, the
    /// list marker only when the line layout did not already give it its inline bounds.
    /// </summary>
    public override void Arrange(Rect finalRect)
    {
        base.Arrange(finalRect);
        if (_visualSidecars.Count == 0) return;
        var markerLaidByLine = _markerLaidByLine;
        _markerLaidByLine = false;
        var content = Square.Rendering.LayoutEngine.ResolveContentRect(this);
        foreach (var sidecar in _visualSidecars)
        {
            if (ReferenceEquals(sidecar, _listMarker) && markerLaidByLine) continue;
            sidecar.Arrange(content);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns the content-box size (padding and border are added by the CSS layout engine):
    /// intrinsic sizes for replaced/media elements and proxy controls, the concatenated or stacked
    /// child size for ordinary hosts, and zero for line-break/rule boxes. This replaces the
    /// <see cref="Square.UI.Element.Measure"/> default which would report the unconstrained
    /// available size (infinite height) for empty boxes.
    /// </remarks>
    public override Size Measure(Size availableSize)
    {
        EnsureSynchronized();
        return MeasureNative(availableSize);
    }

    /// <inheritdoc />
    /// <remarks>Draws the laid-out fragments of its direct DOM Text children, then the
    /// disclosure markers, link/text decoration, gauges and disabled-content labels
    /// that the CSS box painter cannot express.</remarks>
    public override void Paint(IRenderContext ctx)
    {
        EnsureSynchronized();
        PaintDirectText(ctx);
        PaintNative(ctx);
    }

    /// <summary>
    /// Paints the fragments the normal-flow layout produced for this element's direct Text
    /// children, each carrying its source node identity and UTF-16 offsets for selection sync.
    /// Descendant text is painted by the descendant element's own Paint pass.
    /// </summary>
    private void PaintDirectText(IRenderContext ctx)
    {
        if (!AcceptsDirectTextContent || _localName == "details" && !HasAttribute("open")) return;
        var font = ControlDrawing.ResolveFont(this, HtmlTextDefaultFontSize);
        var wrapping = new TextWrappingOptions(
            TextWhiteSpaceMode.Nowrap,
            ControlDrawing.ResolveTextLength(this, "letter-spacing", font.Size),
            ControlDrawing.ResolveTextLength(this, "word-spacing", font.Size),
            TextTransformMode.None,
            0,
            CollapseNewlines: false,
            TextDecorationLines: ControlDrawing.ResolveTextDecorationLines(this));
        foreach (var node in ChildNodes)
        {
            if (node is not Square.UI.Text { Data.Length: > 0 } text) continue;
            if (!Square.Rendering.HtmlTextLayoutStore.TryGet(text, out var data)) continue;
            foreach (var fragment in data.Fragments)
            {
                if (fragment.Text.Length == 0) continue;
                ControlDrawing.DrawText(ctx, this, fragment.Text, fragment.Bounds.Position, Color.Black,
                    HtmlTextDefaultFontSize,
                    maxSize: fragment.Bounds.Size,
                    direction: fragment.Direction,
                    unicodeBidi: fragment.UnicodeBidi,
                    wrappingOptions: wrapping,
                    sourceNode: text,
                    sourceOffset: fragment.SourceOffset,
                    sourceCharOffsets: fragment.SourceCharOffsets,
                    sourceLength: fragment.SourceLength);
            }
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(string name)
    {
        base.OnPropertyChanged(name);
        if (name == nameof(IsDisabled))
        {
            SetState(ElementState.Disabled, IsDisabled);
            return;
        }
        if (name == nameof(Id)) { NotifyObservedAttribute("id"); return; }
        var normalized = ElementRegistry.Normalize(ElementRegistry.HtmlNamespaceUri, name);
        if (normalized == "id")
        {
            Id = Properties.TryGetValue<object>(name, out var boundId) && boundId != null
                ? Convert.ToString(boundId, CultureInfo.InvariantCulture)
                : null;
            return;
        }
        if (normalized is "class" or "style") { NotifyObservedAttribute(normalized); return; }
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
        if (_localName == "details" && normalized == "open") SyncDisclosure();
        _syncPending = true;
        Invalidate(ElementInvalidation.Style | ElementInvalidation.Layout | ElementInvalidation.Paint);
        NotifyObservedAttribute(normalized);
    }

    internal override void OnChildAdded(Element child)
    {
        base.OnChildAdded(child);
        if (_localName == "details")
            SetChildVisible(child, HasAttribute("open") || child is HTMLElement { TagName: "summary" });
        // Late-attached children can change select options and the details disclosure state.
        _syncPending = true;
    }

    private static string Normalize(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return ElementRegistry.Normalize(ElementRegistry.HtmlNamespaceUri, name);
    }

    /// <summary>Lowest-priority persistent UA declaration; author CSS and classes override it.</summary>
    private void SetUserAgentStyle(string property, string value) =>
        Style.SetCascaded(property, value, new CssSpecificity(0, 0, 0), important: false, persistent: true,
            origin: CssCascadeOrigin.UserAgent, authorSpecified: false);

    private void ApplyUserAgentStyle()
    {
        SetUserAgentStyle("display", ResolveStaticDisplay());
        if (_localName is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
        {
            SetUserAgentStyle("font-weight", "bold");
            SetUserAgentStyle("font-size", _localName switch
            {
                "h1" => "2em",
                "h2" => "1.5em",
                "h3" => "1.17em",
                "h5" => "0.83em",
                "h6" => "0.67em",
                _ => "1em"
            });
            SetUserAgentStyle("margin-top", "0.67em");
            SetUserAgentStyle("margin-bottom", "0.67em");
        }
        if (_localName is "p" or "ul" or "ol" or "dl" or "pre" or "blockquote" or "figure" or "details")
        { SetUserAgentStyle("margin-top", "1em"); SetUserAgentStyle("margin-bottom", "1em"); }
        if (_localName is "ul" or "ol" or "menu") SetUserAgentStyle("padding-left", "40px");
        if (_localName == "ol") SetUserAgentStyle("list-style-type", "decimal");
        if (_localName is "ul" or "menu") SetUserAgentStyle("list-style-type", "disc");
        if (_localName == "dd" || _localName == "blockquote") SetUserAgentStyle("margin-left", "40px");
        if (_localName is "pre" or "code" or "kbd" or "samp") SetUserAgentStyle("font-family", "monospace");
        if (_localName is "pre" or "textarea") SetUserAgentStyle("white-space", "pre-wrap");
        if (_localName is "strong" or "b" or "th") SetUserAgentStyle("font-weight", "bold");
        if (_localName is "em" or "i" or "address" or "var" or "cite" or "dfn") SetUserAgentStyle("font-style", "italic");
        if (_localName == "small") SetUserAgentStyle("font-size", "0.83em");
        if (_localName is "sub" or "sup") SetUserAgentStyle("font-size", "0.83em");
        if (_localName == "mark") SetUserAgentStyle("background-color", "#ffff00");
        if (_localName == "a") SetUserAgentStyle("color", "#0000ee");
        if (_localName == "summary")
        {
            SetUserAgentStyle("padding-left", "1.2em");
            SetUserAgentStyle("cursor", "default");
        }
        if (_localName == "fieldset")
        {
            SetUserAgentStyle("margin", "0 2px");
            SetUserAgentStyle("border", "1px solid #a9a9a9");
            SetUserAgentStyle("padding", "0.35em 0.75em 0.625em");
        }
        if (_localName == "button")
        {
            SetUserAgentStyle("font-size", "13.3333px");
            SetUserAgentStyle("text-align", "center");
            SetUserAgentStyle("padding", "1px 6px");
            SetUserAgentStyle("border", "2px outset #d4d4d4");
            SetUserAgentStyle("background-color", "#efefef");
        }
        if (_localName == "hr")
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
        if (_syncPending)
        {
            _syncPending = false;
            SyncDynamicDisplay();
            SyncDirectionOverride();
            switch (_localName)
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
        SyncProxyAppearance();
    }

    /// <summary>
    /// Intrinsic content-box size. br/wbr/hr are zero boxes (the engine breaks the line and the
    /// rule's border paints), replaced elements report their proxy or default replaced size, and
    /// ordinary hosts concatenate inline or stack block children.
    /// </summary>
    private Size MeasureNative(Size availableSize)
    {
        switch (_localName)
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

    /// <summary>Concatenates inline children horizontally; stacks block children vertically.
    /// Direct DOM Text and sidecars (the li marker) count like regular children, keeping
    /// intrinsic sizes stable.</summary>
    private Size MeasureContent(Size availableSize)
    {
        var inline = string.Equals((Style.Get("display") ?? "").Trim(), "inline", StringComparison.Ordinal);
        var width = 0f;
        var height = 0f;

        void Accumulate(Size measured)
        {
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

        foreach (var child in Children)
        {
            if (!child.IsVisible || IsDisplayNone(child)) continue;
            var measured = child.Measure(new Size(
                inline ? float.MaxValue : Math.Max(0f, availableSize.Width),
                float.MaxValue));
            Accumulate(measured);
        }
        if (AcceptsDirectTextContent)
            foreach (var node in ChildNodes)
            {
                if (node is not Square.UI.Text { Data.Length: > 0 } text) continue;
                Accumulate(ControlDrawing.MeasureText(this, text.Data, HtmlTextDefaultFontSize,
                    new Size(inline ? float.MaxValue : Math.Max(0f, availableSize.Width), float.MaxValue)));
            }
        foreach (var sidecar in _visualSidecars)
        {
            if (!sidecar.IsVisible || IsDisplayNone(sidecar)) continue;
            var measured = sidecar.Measure(new Size(
                inline ? float.MaxValue : Math.Max(0f, availableSize.Width),
                float.MaxValue));
            Accumulate(measured);
        }
        return new Size(width, height);
    }

    private static bool IsDisplayNone(Element element) =>
        string.Equals((element.Style.Get("display") ?? "").Trim(), "none", StringComparison.Ordinal);

    private T? ProxyOf<T>() where T : UIElement
    {
        foreach (var sidecar in _visualSidecars)
            if (sidecar is T typed && sidecar is not Square.CSS.Engine.CssGeneratedPseudoElement)
                return typed;
        return default;
    }

    private bool HasVisibleHtmlContent()
    {
        foreach (var child in Children)
            if (child.IsVisible && !IsDisplayNone(child)) return true;
        if (AcceptsDirectTextContent)
            foreach (var node in ChildNodes)
                if (node is Square.UI.Text { Data.Length: > 0 } text && !string.IsNullOrWhiteSpace(text.Data))
                    return true;
        return false;
    }

    /// <summary>Draws everything the CSS box painter and laid-out children cannot.</summary>
    private void PaintNative(IRenderContext ctx)
    {
        switch (_localName)
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
        else if (_localName == "dialog") desired = HasAttribute("open") ? "block" : "none";
        else if (_localName == "input" && (GetAttribute("type") ?? "text") == "hidden") desired = "none";
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
    private string ResolveStaticDisplay() => _localName switch
    {
        "table" => "table",
        "thead" => "table-header-group",
        "tbody" => "table-row-group",
        "tfoot" => "table-footer-group",
        "tr" => "table-row",
        "th" or "td" => "table-cell",
        "caption" => "table-caption",
        "colgroup" => "table-column-group",
        "col" => "table-column",
        "li" => "list-item",
        "noscript" => "block",
        "dialog" => "none",
        "map" or "area" => "none",
        _ when HtmlTagCatalog.IsMetadata(_localName) || _localName is "datalist" or "option" or "optgroup" or "source" or "track" or "selectedcontent" => "none",
        _ when HtmlTagCatalog.IsReplaced(_localName) || _localName is "button" => "inline-block",
        _ when HtmlTagCatalog.IsBlock(_localName) => "block",
        _ => "inline"
    };

    /// <summary>bdo/bdi map their dir attribute onto the CSS direction the text layout reads.</summary>
    private void SyncDirectionOverride()
    {
        if (_localName is not ("bdo" or "bdi")) return;
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
