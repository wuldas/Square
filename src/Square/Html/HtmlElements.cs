using System.Globalization;

namespace Square.Html;

/// <summary>
/// One concrete CLR type per frozen WHATWG HTML tag (113 tags, see <see cref="HtmlTagCatalog"/>).
/// Shared WHATWG interfaces that serve several tags are abstract semantic bases here; tags whose
/// WHATWG interface is plain <c>HTMLElement</c> derive from <see cref="HTMLElement"/> directly.
/// Every concrete class is public and unsealed (custom elements may extend it) with a fixed
/// lowercase tag and a public parameterless constructor; there is deliberately no public
/// arbitrary-string tag constructor.
/// </summary>

/// <summary>Shared abstract base for the six heading elements (<c>h1</c>–<c>h6</c>).</summary>
public abstract class HTMLHeadingElement : HTMLElement
{
    /// <summary>Initializes the heading with its fixed tag.</summary>
    protected HTMLHeadingElement(string localName) : base(localName) { }
}

/// <summary>Shared abstract base for <c>blockquote</c> and <c>q</c>.</summary>
public abstract class HTMLQuoteElement : HTMLElement
{
    /// <summary>Initializes the quote element with its fixed tag.</summary>
    protected HTMLQuoteElement(string localName) : base(localName) { }
}

/// <summary>Shared abstract base for <c>col</c> and <c>colgroup</c>.</summary>
public abstract class HTMLTableColElement : HTMLElement
{
    /// <summary>Initializes the column element with its fixed tag.</summary>
    protected HTMLTableColElement(string localName) : base(localName) { }
}

/// <summary>Shared abstract base for <c>del</c> and <c>ins</c>.</summary>
public abstract class HTMLModElement : HTMLElement
{
    /// <summary>Initializes the modification element with its fixed tag.</summary>
    protected HTMLModElement(string localName) : base(localName) { }
}

/// <summary>Shared abstract base for <c>thead</c>, <c>tbody</c> and <c>tfoot</c>.</summary>
public abstract class HTMLTableSectionElement : HTMLElement
{
    /// <summary>Initializes the table section with its fixed tag.</summary>
    protected HTMLTableSectionElement(string localName) : base(localName) { }
}

/// <summary>Shared abstract base for <c>td</c> and <c>th</c>.</summary>
public abstract class HTMLTableCellElement : HTMLElement
{
    /// <summary>Initializes the table cell with its fixed tag.</summary>
    protected HTMLTableCellElement(string localName) : base(localName) { }

    /// <summary>The <c>colspan</c> attribute; absent or non-numeric is <c>null</c> (host degrades to span 1).</summary>
    public int? ColSpan
    {
        get => HtmlAttributeValues.GetInt32(this, "colspan");
        set => HtmlAttributeValues.SetInt32(this, "colspan", value);
    }

    /// <summary>The <c>rowspan</c> attribute; absent or non-numeric is <c>null</c> (host degrades to span 1).</summary>
    public int? RowSpan
    {
        get => HtmlAttributeValues.GetInt32(this, "rowspan");
        set => HtmlAttributeValues.SetInt32(this, "rowspan", value);
    }
}

/// <summary>Concrete element for the <c>a</c> tag.</summary>
public class HTMLAnchorElement : HTMLElement
{
    /// <summary>Creates the fixed <c>a</c> element.</summary>
    public HTMLAnchorElement() : base("a") { }

    /// <summary>The <c>href</c> attribute; absent is <c>null</c>.</summary>
    public string? Href
    {
        get => GetAttribute("href");
        set => HtmlAttributeValues.Set(this, "href", value);
    }
}

/// <summary>Concrete element for the <c>abbr</c> tag.</summary>
public class HTMLAbbrElement : HTMLElement
{
    /// <summary>Creates the fixed <c>abbr</c> element.</summary>
    public HTMLAbbrElement() : base("abbr") { }
}

/// <summary>Concrete element for the <c>address</c> tag.</summary>
public class HTMLAddressElement : HTMLElement
{
    /// <summary>Creates the fixed <c>address</c> element.</summary>
    public HTMLAddressElement() : base("address") { }
}

/// <summary>Concrete element for the <c>area</c> tag.</summary>
public class HTMLAreaElement : HTMLElement
{
    /// <summary>Creates the fixed <c>area</c> element.</summary>
    public HTMLAreaElement() : base("area") { }

    /// <summary>The <c>href</c> attribute; absent is <c>null</c>.</summary>
    public string? Href
    {
        get => GetAttribute("href");
        set => HtmlAttributeValues.Set(this, "href", value);
    }

    /// <summary>The <c>shape</c> attribute; absent is <c>null</c>.</summary>
    public string? Shape
    {
        get => GetAttribute("shape");
        set => HtmlAttributeValues.Set(this, "shape", value);
    }

    /// <summary>The <c>coords</c> attribute; absent is <c>null</c>.</summary>
    public string? Coords
    {
        get => GetAttribute("coords");
        set => HtmlAttributeValues.Set(this, "coords", value);
    }
}

/// <summary>Concrete element for the <c>article</c> tag.</summary>
public class HTMLArticleElement : HTMLElement
{
    /// <summary>Creates the fixed <c>article</c> element.</summary>
    public HTMLArticleElement() : base("article") { }
}

/// <summary>Concrete element for the <c>aside</c> tag.</summary>
public class HTMLAsideElement : HTMLElement
{
    /// <summary>Creates the fixed <c>aside</c> element.</summary>
    public HTMLAsideElement() : base("aside") { }
}

/// <summary>Concrete element for the <c>audio</c> tag; playback stays a documented disabled behavior.</summary>
public class HTMLAudioElement : HTMLElement
{
    /// <summary>Creates the fixed <c>audio</c> element.</summary>
    public HTMLAudioElement() : base("audio") { }
}

/// <summary>Concrete element for the <c>b</c> tag.</summary>
public class HTMLBElement : HTMLElement
{
    /// <summary>Creates the fixed <c>b</c> element.</summary>
    public HTMLBElement() : base("b") { }
}

/// <summary>Concrete element for the <c>base</c> tag.</summary>
public class HTMLBaseElement : HTMLElement
{
    /// <summary>Creates the fixed <c>base</c> element.</summary>
    public HTMLBaseElement() : base("base") { }
}

/// <summary>Concrete element for the <c>bdi</c> tag.</summary>
public class HTMLBdiElement : HTMLElement
{
    /// <summary>Creates the fixed <c>bdi</c> element.</summary>
    public HTMLBdiElement() : base("bdi") { }
}

/// <summary>Concrete element for the <c>bdo</c> tag.</summary>
public class HTMLBdoElement : HTMLElement
{
    /// <summary>Creates the fixed <c>bdo</c> element.</summary>
    public HTMLBdoElement() : base("bdo") { }
}

/// <summary>Concrete element for the <c>blockquote</c> tag.</summary>
public class HTMLBlockquoteElement : HTMLQuoteElement
{
    /// <summary>Creates the fixed <c>blockquote</c> element.</summary>
    public HTMLBlockquoteElement() : base("blockquote") { }
}

/// <summary>Concrete element for the <c>body</c> tag.</summary>
public class HTMLBodyElement : HTMLElement
{
    /// <summary>Creates the fixed <c>body</c> element.</summary>
    public HTMLBodyElement() : base("body") { }
}

/// <summary>Concrete element for the <c>br</c> tag.</summary>
public class HTMLBRElement : HTMLElement
{
    /// <summary>Creates the fixed <c>br</c> element.</summary>
    public HTMLBRElement() : base("br") { }
}

/// <summary>Concrete element for the <c>button</c> tag.</summary>
public class HTMLButtonElement : HTMLElement
{
    /// <summary>Creates the fixed <c>button</c> element.</summary>
    public HTMLButtonElement() : base("button") { }

    /// <summary>Whether the <c>disabled</c> attribute is present.</summary>
    public bool Disabled
    {
        get => HtmlAttributeValues.GetFlag(this, "disabled");
        set => HtmlAttributeValues.SetFlag(this, "disabled", value);
    }
}

/// <summary>Concrete element for the <c>canvas</c> tag; 2D/WebGL scripting stays a documented disabled behavior.</summary>
public class HTMLCanvasElement : HTMLElement
{
    /// <summary>Creates the fixed <c>canvas</c> element.</summary>
    public HTMLCanvasElement() : base("canvas") { }
}

/// <summary>Concrete element for the <c>caption</c> tag.</summary>
public class HTMLTableCaptionElement : HTMLElement
{
    /// <summary>Creates the fixed <c>caption</c> element.</summary>
    public HTMLTableCaptionElement() : base("caption") { }
}

/// <summary>Concrete element for the <c>cite</c> tag.</summary>
public class HTMLCiteElement : HTMLElement
{
    /// <summary>Creates the fixed <c>cite</c> element.</summary>
    public HTMLCiteElement() : base("cite") { }
}

/// <summary>Concrete element for the <c>code</c> tag.</summary>
public class HTMLCodeElement : HTMLElement
{
    /// <summary>Creates the fixed <c>code</c> element.</summary>
    public HTMLCodeElement() : base("code") { }
}

/// <summary>Concrete element for the <c>col</c> tag.</summary>
public class HTMLColElement : HTMLTableColElement
{
    /// <summary>Creates the fixed <c>col</c> element.</summary>
    public HTMLColElement() : base("col") { }
}

/// <summary>Concrete element for the <c>colgroup</c> tag.</summary>
public class HTMLColgroupElement : HTMLTableColElement
{
    /// <summary>Creates the fixed <c>colgroup</c> element.</summary>
    public HTMLColgroupElement() : base("colgroup") { }
}

/// <summary>Concrete element for the <c>data</c> tag.</summary>
public class HTMLDataElement : HTMLElement
{
    /// <summary>Creates the fixed <c>data</c> element.</summary>
    public HTMLDataElement() : base("data") { }
}

/// <summary>Concrete element for the <c>datalist</c> tag.</summary>
public class HTMLDataListElement : HTMLElement
{
    /// <summary>Creates the fixed <c>datalist</c> element.</summary>
    public HTMLDataListElement() : base("datalist") { }
}

/// <summary>Concrete element for the <c>dd</c> tag.</summary>
public class HTMLDdElement : HTMLElement
{
    /// <summary>Creates the fixed <c>dd</c> element.</summary>
    public HTMLDdElement() : base("dd") { }
}

/// <summary>Concrete element for the <c>del</c> tag.</summary>
public class HTMLDelElement : HTMLModElement
{
    /// <summary>Creates the fixed <c>del</c> element.</summary>
    public HTMLDelElement() : base("del") { }
}

/// <summary>Concrete element for the <c>details</c> tag.</summary>
public class HTMLDetailsElement : HTMLElement
{
    /// <summary>Creates the fixed <c>details</c> element.</summary>
    public HTMLDetailsElement() : base("details") { }

    /// <summary>Whether the <c>open</c> attribute is present.</summary>
    public bool Open
    {
        get => HtmlAttributeValues.GetFlag(this, "open");
        set => HtmlAttributeValues.SetFlag(this, "open", value);
    }
}

/// <summary>Concrete element for the <c>dfn</c> tag.</summary>
public class HTMLDfnElement : HTMLElement
{
    /// <summary>Creates the fixed <c>dfn</c> element.</summary>
    public HTMLDfnElement() : base("dfn") { }
}

/// <summary>Concrete element for the <c>dialog</c> tag.</summary>
public class HTMLDialogElement : HTMLElement
{
    /// <summary>Creates the fixed <c>dialog</c> element.</summary>
    public HTMLDialogElement() : base("dialog") { }

    /// <summary>Whether the <c>open</c> attribute is present.</summary>
    public bool Open
    {
        get => HtmlAttributeValues.GetFlag(this, "open");
        set => HtmlAttributeValues.SetFlag(this, "open", value);
    }
}

/// <summary>Concrete element for the <c>div</c> tag.</summary>
public class HTMLDivElement : HTMLElement
{
    /// <summary>Creates the fixed <c>div</c> element.</summary>
    public HTMLDivElement() : base("div") { }
}

/// <summary>Concrete element for the <c>dl</c> tag.</summary>
public class HTMLDListElement : HTMLElement
{
    /// <summary>Creates the fixed <c>dl</c> element.</summary>
    public HTMLDListElement() : base("dl") { }
}

/// <summary>Concrete element for the <c>dt</c> tag.</summary>
public class HTMLDtElement : HTMLElement
{
    /// <summary>Creates the fixed <c>dt</c> element.</summary>
    public HTMLDtElement() : base("dt") { }
}

/// <summary>Concrete element for the <c>em</c> tag.</summary>
public class HTMLEmElement : HTMLElement
{
    /// <summary>Creates the fixed <c>em</c> element.</summary>
    public HTMLEmElement() : base("em") { }
}

/// <summary>Concrete element for the <c>embed</c> tag; plugin content stays a documented disabled behavior.</summary>
public class HTMLEmbedElement : HTMLElement
{
    /// <summary>Creates the fixed <c>embed</c> element.</summary>
    public HTMLEmbedElement() : base("embed") { }
}

/// <summary>Concrete element for the <c>fieldset</c> tag.</summary>
public class HTMLFieldSetElement : HTMLElement
{
    /// <summary>Creates the fixed <c>fieldset</c> element.</summary>
    public HTMLFieldSetElement() : base("fieldset") { }
}

/// <summary>Concrete element for the <c>figcaption</c> tag.</summary>
public class HTMLFigcaptionElement : HTMLElement
{
    /// <summary>Creates the fixed <c>figcaption</c> element.</summary>
    public HTMLFigcaptionElement() : base("figcaption") { }
}

/// <summary>Concrete element for the <c>figure</c> tag.</summary>
public class HTMLFigureElement : HTMLElement
{
    /// <summary>Creates the fixed <c>figure</c> element.</summary>
    public HTMLFigureElement() : base("figure") { }
}

/// <summary>Concrete element for the <c>footer</c> tag.</summary>
public class HTMLFooterElement : HTMLElement
{
    /// <summary>Creates the fixed <c>footer</c> element.</summary>
    public HTMLFooterElement() : base("footer") { }
}

/// <summary>Concrete element for the <c>form</c> tag; desktop network submission stays a documented disabled behavior.</summary>
public class HTMLFormElement : HTMLElement
{
    /// <summary>Creates the fixed <c>form</c> element.</summary>
    public HTMLFormElement() : base("form") { }
}

/// <summary>Concrete element for the <c>h1</c> tag.</summary>
public class HTMLH1Element : HTMLHeadingElement
{
    /// <summary>Creates the fixed <c>h1</c> element.</summary>
    public HTMLH1Element() : base("h1") { }
}

/// <summary>Concrete element for the <c>h2</c> tag.</summary>
public class HTMLH2Element : HTMLHeadingElement
{
    /// <summary>Creates the fixed <c>h2</c> element.</summary>
    public HTMLH2Element() : base("h2") { }
}

/// <summary>Concrete element for the <c>h3</c> tag.</summary>
public class HTMLH3Element : HTMLHeadingElement
{
    /// <summary>Creates the fixed <c>h3</c> element.</summary>
    public HTMLH3Element() : base("h3") { }
}

/// <summary>Concrete element for the <c>h4</c> tag.</summary>
public class HTMLH4Element : HTMLHeadingElement
{
    /// <summary>Creates the fixed <c>h4</c> element.</summary>
    public HTMLH4Element() : base("h4") { }
}

/// <summary>Concrete element for the <c>h5</c> tag.</summary>
public class HTMLH5Element : HTMLHeadingElement
{
    /// <summary>Creates the fixed <c>h5</c> element.</summary>
    public HTMLH5Element() : base("h5") { }
}

/// <summary>Concrete element for the <c>h6</c> tag.</summary>
public class HTMLH6Element : HTMLHeadingElement
{
    /// <summary>Creates the fixed <c>h6</c> element.</summary>
    public HTMLH6Element() : base("h6") { }
}

/// <summary>Concrete element for the <c>head</c> tag.</summary>
public class HTMLHeadElement : HTMLElement
{
    /// <summary>Creates the fixed <c>head</c> element.</summary>
    public HTMLHeadElement() : base("head") { }
}

/// <summary>Concrete element for the <c>header</c> tag.</summary>
public class HTMLHeaderElement : HTMLElement
{
    /// <summary>Creates the fixed <c>header</c> element.</summary>
    public HTMLHeaderElement() : base("header") { }
}

/// <summary>Concrete element for the <c>hgroup</c> tag.</summary>
public class HTMLHgroupElement : HTMLElement
{
    /// <summary>Creates the fixed <c>hgroup</c> element.</summary>
    public HTMLHgroupElement() : base("hgroup") { }
}

/// <summary>Concrete element for the <c>hr</c> tag.</summary>
public class HTMLHRElement : HTMLElement
{
    /// <summary>Creates the fixed <c>hr</c> element.</summary>
    public HTMLHRElement() : base("hr") { }
}

/// <summary>Concrete element for the <c>html</c> tag.</summary>
public class HTMLHtmlElement : HTMLElement
{
    /// <summary>Creates the fixed <c>html</c> element.</summary>
    public HTMLHtmlElement() : base("html") { }
}

/// <summary>Concrete element for the <c>i</c> tag.</summary>
public class HTMLIElement : HTMLElement
{
    /// <summary>Creates the fixed <c>i</c> element.</summary>
    public HTMLIElement() : base("i") { }
}

/// <summary>Concrete element for the <c>iframe</c> tag; embedded documents stay a documented disabled behavior.</summary>
public class HTMLIFrameElement : HTMLElement
{
    /// <summary>Creates the fixed <c>iframe</c> element.</summary>
    public HTMLIFrameElement() : base("iframe") { }
}

/// <summary>Concrete element for the <c>img</c> tag.</summary>
public class HTMLImageElement : HTMLElement
{
    /// <summary>Creates the fixed <c>img</c> element.</summary>
    public HTMLImageElement() : base("img") { }

    /// <summary>The <c>src</c> attribute; absent is <c>null</c>.</summary>
    public string? Src
    {
        get => GetAttribute("src");
        set => HtmlAttributeValues.Set(this, "src", value);
    }

    /// <summary>The <c>alt</c> attribute; absent is <c>null</c>.</summary>
    public string? Alt
    {
        get => GetAttribute("alt");
        set => HtmlAttributeValues.Set(this, "alt", value);
    }

    /// <summary>The <c>usemap</c> attribute; absent is <c>null</c>.</summary>
    public string? UseMap
    {
        get => GetAttribute("usemap");
        set => HtmlAttributeValues.Set(this, "usemap", value);
    }
}

/// <summary>Concrete element for the <c>input</c> tag.</summary>
public class HTMLInputElement : HTMLElement
{
    /// <summary>Creates the fixed <c>input</c> element.</summary>
    public HTMLInputElement() : base("input") { }

    /// <summary>The <c>value</c> attribute; absent is <c>null</c>.</summary>
    public string? Value
    {
        get => GetAttribute("value");
        set => HtmlAttributeValues.Set(this, "value", value);
    }

    /// <summary>Whether the <c>checked</c> attribute is present.</summary>
    public bool Checked
    {
        get => HtmlAttributeValues.GetFlag(this, "checked");
        set => HtmlAttributeValues.SetFlag(this, "checked", value);
    }

    /// <summary>The <c>type</c> attribute; absent is <c>null</c> (host defaults to <c>text</c>).</summary>
    public string? Type
    {
        get => GetAttribute("type");
        set => HtmlAttributeValues.Set(this, "type", value);
    }

    /// <summary>The <c>name</c> attribute; absent is <c>null</c>.</summary>
    public string? Name
    {
        get => GetAttribute("name");
        set => HtmlAttributeValues.Set(this, "name", value);
    }

    /// <summary>The <c>placeholder</c> attribute; absent is <c>null</c>.</summary>
    public string? Placeholder
    {
        get => GetAttribute("placeholder");
        set => HtmlAttributeValues.Set(this, "placeholder", value);
    }

    /// <summary>Whether the <c>disabled</c> attribute is present.</summary>
    public bool Disabled
    {
        get => HtmlAttributeValues.GetFlag(this, "disabled");
        set => HtmlAttributeValues.SetFlag(this, "disabled", value);
    }
}

/// <summary>Concrete element for the <c>ins</c> tag.</summary>
public class HTMLInsElement : HTMLModElement
{
    /// <summary>Creates the fixed <c>ins</c> element.</summary>
    public HTMLInsElement() : base("ins") { }
}

/// <summary>Concrete element for the <c>kbd</c> tag.</summary>
public class HTMLKbdElement : HTMLElement
{
    /// <summary>Creates the fixed <c>kbd</c> element.</summary>
    public HTMLKbdElement() : base("kbd") { }
}

/// <summary>Concrete element for the <c>label</c> tag.</summary>
public class HTMLLabelElement : HTMLElement
{
    /// <summary>Creates the fixed <c>label</c> element.</summary>
    public HTMLLabelElement() : base("label") { }

    /// <summary>The <c>for</c> attribute; absent is <c>null</c>.</summary>
    public string? HtmlFor
    {
        get => GetAttribute("for");
        set => HtmlAttributeValues.Set(this, "for", value);
    }
}

/// <summary>Concrete element for the <c>legend</c> tag.</summary>
public class HTMLLegendElement : HTMLElement
{
    /// <summary>Creates the fixed <c>legend</c> element.</summary>
    public HTMLLegendElement() : base("legend") { }
}

/// <summary>Concrete element for the <c>li</c> tag.</summary>
public class HTMLLIElement : HTMLElement
{
    /// <summary>Creates the fixed <c>li</c> element.</summary>
    public HTMLLIElement() : base("li") { }

    /// <summary>The <c>value</c> attribute; absent or non-numeric is <c>null</c>.</summary>
    public int? Value
    {
        get => HtmlAttributeValues.GetInt32(this, "value");
        set => HtmlAttributeValues.SetInt32(this, "value", value);
    }
}

/// <summary>Concrete element for the <c>link</c> tag.</summary>
public class HTMLLinkElement : HTMLElement
{
    /// <summary>Creates the fixed <c>link</c> element.</summary>
    public HTMLLinkElement() : base("link") { }
}

/// <summary>Concrete element for the <c>main</c> tag.</summary>
public class HTMLMainElement : HTMLElement
{
    /// <summary>Creates the fixed <c>main</c> element.</summary>
    public HTMLMainElement() : base("main") { }
}

/// <summary>Concrete element for the <c>map</c> tag.</summary>
public class HTMLMapElement : HTMLElement
{
    /// <summary>Creates the fixed <c>map</c> element.</summary>
    public HTMLMapElement() : base("map") { }

    /// <summary>The <c>name</c> attribute; absent is <c>null</c>.</summary>
    public string? Name
    {
        get => GetAttribute("name");
        set => HtmlAttributeValues.Set(this, "name", value);
    }
}

/// <summary>Concrete element for the <c>mark</c> tag.</summary>
public class HTMLMarkElement : HTMLElement
{
    /// <summary>Creates the fixed <c>mark</c> element.</summary>
    public HTMLMarkElement() : base("mark") { }
}

/// <summary>Concrete element for the <c>menu</c> tag.</summary>
public class HTMLMenuElement : HTMLElement
{
    /// <summary>Creates the fixed <c>menu</c> element.</summary>
    public HTMLMenuElement() : base("menu") { }
}

/// <summary>Concrete element for the <c>meta</c> tag.</summary>
public class HTMLMetaElement : HTMLElement
{
    /// <summary>Creates the fixed <c>meta</c> element.</summary>
    public HTMLMetaElement() : base("meta") { }
}

/// <summary>Concrete element for the <c>meter</c> tag.</summary>
public class HTMLMeterElement : HTMLElement
{
    /// <summary>Creates the fixed <c>meter</c> element.</summary>
    public HTMLMeterElement() : base("meter") { }

    /// <summary>The <c>value</c> attribute; absent or non-numeric is <c>null</c>.</summary>
    public double? Value
    {
        get => HtmlAttributeValues.GetDouble(this, "value");
        set => HtmlAttributeValues.SetDouble(this, "value", value);
    }

    /// <summary>The <c>min</c> attribute; absent or non-numeric is <c>null</c>.</summary>
    public double? Min
    {
        get => HtmlAttributeValues.GetDouble(this, "min");
        set => HtmlAttributeValues.SetDouble(this, "min", value);
    }

    /// <summary>The <c>max</c> attribute; absent or non-numeric is <c>null</c>.</summary>
    public double? Max
    {
        get => HtmlAttributeValues.GetDouble(this, "max");
        set => HtmlAttributeValues.SetDouble(this, "max", value);
    }

    /// <summary>The <c>low</c> attribute; absent or non-numeric is <c>null</c>.</summary>
    public double? Low
    {
        get => HtmlAttributeValues.GetDouble(this, "low");
        set => HtmlAttributeValues.SetDouble(this, "low", value);
    }

    /// <summary>The <c>high</c> attribute; absent or non-numeric is <c>null</c>.</summary>
    public double? High
    {
        get => HtmlAttributeValues.GetDouble(this, "high");
        set => HtmlAttributeValues.SetDouble(this, "high", value);
    }

    /// <summary>The <c>optimum</c> attribute; absent or non-numeric is <c>null</c>.</summary>
    public double? Optimum
    {
        get => HtmlAttributeValues.GetDouble(this, "optimum");
        set => HtmlAttributeValues.SetDouble(this, "optimum", value);
    }
}

/// <summary>Concrete element for the <c>nav</c> tag.</summary>
public class HTMLNavElement : HTMLElement
{
    /// <summary>Creates the fixed <c>nav</c> element.</summary>
    public HTMLNavElement() : base("nav") { }
}

/// <summary>Concrete element for the <c>noscript</c> tag; script execution stays a documented disabled behavior.</summary>
public class HTMLNoscriptElement : HTMLElement
{
    /// <summary>Creates the fixed <c>noscript</c> element.</summary>
    public HTMLNoscriptElement() : base("noscript") { }
}

/// <summary>Concrete element for the <c>object</c> tag; plugin content stays a documented disabled behavior.</summary>
public class HTMLObjectElement : HTMLElement
{
    /// <summary>Creates the fixed <c>object</c> element.</summary>
    public HTMLObjectElement() : base("object") { }
}

/// <summary>Concrete element for the <c>ol</c> tag.</summary>
public class HTMLOListElement : HTMLElement
{
    /// <summary>Creates the fixed <c>ol</c> element.</summary>
    public HTMLOListElement() : base("ol") { }

    /// <summary>The <c>start</c> attribute; absent or non-numeric is <c>null</c>.</summary>
    public int? Start
    {
        get => HtmlAttributeValues.GetInt32(this, "start");
        set => HtmlAttributeValues.SetInt32(this, "start", value);
    }

    /// <summary>Whether the <c>reversed</c> attribute is present.</summary>
    public bool Reversed
    {
        get => HtmlAttributeValues.GetFlag(this, "reversed");
        set => HtmlAttributeValues.SetFlag(this, "reversed", value);
    }
}

/// <summary>Concrete element for the <c>optgroup</c> tag.</summary>
public class HTMLOptGroupElement : HTMLElement
{
    /// <summary>Creates the fixed <c>optgroup</c> element.</summary>
    public HTMLOptGroupElement() : base("optgroup") { }
}

/// <summary>Concrete element for the <c>option</c> tag.</summary>
public class HTMLOptionElement : HTMLElement
{
    /// <summary>Creates the fixed <c>option</c> element.</summary>
    public HTMLOptionElement() : base("option") { }

    /// <summary>Whether the <c>selected</c> attribute is present.</summary>
    public bool Selected
    {
        get => HtmlAttributeValues.GetFlag(this, "selected");
        set => HtmlAttributeValues.SetFlag(this, "selected", value);
    }

    /// <summary>The <c>value</c> attribute; absent is <c>null</c> (host falls back to the option text).</summary>
    public string? Value
    {
        get => GetAttribute("value");
        set => HtmlAttributeValues.Set(this, "value", value);
    }
}

/// <summary>Concrete element for the <c>output</c> tag.</summary>
public class HTMLOutputElement : HTMLElement
{
    /// <summary>Creates the fixed <c>output</c> element.</summary>
    public HTMLOutputElement() : base("output") { }
}

/// <summary>Concrete element for the <c>p</c> tag.</summary>
public class HTMLParagraphElement : HTMLElement
{
    /// <summary>Creates the fixed <c>p</c> element.</summary>
    public HTMLParagraphElement() : base("p") { }
}

/// <summary>Concrete element for the <c>picture</c> tag.</summary>
public class HTMLPictureElement : HTMLElement
{
    /// <summary>Creates the fixed <c>picture</c> element.</summary>
    public HTMLPictureElement() : base("picture") { }
}

/// <summary>Concrete element for the <c>pre</c> tag.</summary>
public class HTMLPreElement : HTMLElement
{
    /// <summary>Creates the fixed <c>pre</c> element.</summary>
    public HTMLPreElement() : base("pre") { }
}

/// <summary>Concrete element for the <c>progress</c> tag.</summary>
public class HTMLProgressElement : HTMLElement
{
    /// <summary>Creates the fixed <c>progress</c> element.</summary>
    public HTMLProgressElement() : base("progress") { }

    /// <summary>The <c>value</c> attribute; absent or non-numeric is <c>null</c>.</summary>
    public double? Value
    {
        get => HtmlAttributeValues.GetDouble(this, "value");
        set => HtmlAttributeValues.SetDouble(this, "value", value);
    }

    /// <summary>The <c>max</c> attribute; absent or non-numeric is <c>null</c> (host degrades to 1).</summary>
    public double? Max
    {
        get => HtmlAttributeValues.GetDouble(this, "max");
        set => HtmlAttributeValues.SetDouble(this, "max", value);
    }
}

/// <summary>Concrete element for the <c>q</c> tag.</summary>
public class HTMLQElement : HTMLQuoteElement
{
    /// <summary>Creates the fixed <c>q</c> element.</summary>
    public HTMLQElement() : base("q") { }
}

/// <summary>Concrete element for the <c>rp</c> tag.</summary>
public class HTMLRpElement : HTMLElement
{
    /// <summary>Creates the fixed <c>rp</c> element.</summary>
    public HTMLRpElement() : base("rp") { }
}

/// <summary>Concrete element for the <c>rt</c> tag.</summary>
public class HTMLRtElement : HTMLElement
{
    /// <summary>Creates the fixed <c>rt</c> element.</summary>
    public HTMLRtElement() : base("rt") { }
}

/// <summary>Concrete element for the <c>ruby</c> tag.</summary>
public class HTMLRubyElement : HTMLElement
{
    /// <summary>Creates the fixed <c>ruby</c> element.</summary>
    public HTMLRubyElement() : base("ruby") { }
}

/// <summary>Concrete element for the <c>s</c> tag.</summary>
public class HTMLSElement : HTMLElement
{
    /// <summary>Creates the fixed <c>s</c> element.</summary>
    public HTMLSElement() : base("s") { }
}

/// <summary>Concrete element for the <c>samp</c> tag.</summary>
public class HTMLSampElement : HTMLElement
{
    /// <summary>Creates the fixed <c>samp</c> element.</summary>
    public HTMLSampElement() : base("samp") { }
}

/// <summary>Concrete element for the <c>script</c> tag; script execution stays a documented disabled behavior.</summary>
public class HTMLScriptElement : HTMLElement
{
    /// <summary>Creates the fixed <c>script</c> element.</summary>
    public HTMLScriptElement() : base("script") { }
}

/// <summary>Concrete element for the <c>search</c> tag.</summary>
public class HTMLSearchElement : HTMLElement
{
    /// <summary>Creates the fixed <c>search</c> element.</summary>
    public HTMLSearchElement() : base("search") { }
}

/// <summary>Concrete element for the <c>section</c> tag.</summary>
public class HTMLSectionElement : HTMLElement
{
    /// <summary>Creates the fixed <c>section</c> element.</summary>
    public HTMLSectionElement() : base("section") { }
}

/// <summary>Concrete element for the <c>select</c> tag.</summary>
public class HTMLSelectElement : HTMLElement
{
    /// <summary>Creates the fixed <c>select</c> element.</summary>
    public HTMLSelectElement() : base("select") { }

    /// <summary>The <c>value</c> attribute; absent is <c>null</c> (selection is mirrored by the host).</summary>
    public string? Value
    {
        get => GetAttribute("value");
        set => HtmlAttributeValues.Set(this, "value", value);
    }

    /// <summary>Whether the <c>disabled</c> attribute is present.</summary>
    public bool Disabled
    {
        get => HtmlAttributeValues.GetFlag(this, "disabled");
        set => HtmlAttributeValues.SetFlag(this, "disabled", value);
    }
}

/// <summary>Concrete element for the <c>selectedcontent</c> tag.</summary>
public class HTMLSelectedContentElement : HTMLElement
{
    /// <summary>Creates the fixed <c>selectedcontent</c> element.</summary>
    public HTMLSelectedContentElement() : base("selectedcontent") { }
}

/// <summary>Concrete element for the <c>slot</c> tag (HTML slot; the Square slot is <c>ui:Slot</c>).</summary>
public class HTMLSlotElement : HTMLElement
{
    /// <summary>Creates the fixed <c>slot</c> element.</summary>
    public HTMLSlotElement() : base("slot") { }
}

/// <summary>Concrete element for the <c>small</c> tag.</summary>
public class HTMLSmallElement : HTMLElement
{
    /// <summary>Creates the fixed <c>small</c> element.</summary>
    public HTMLSmallElement() : base("small") { }
}

/// <summary>Concrete element for the <c>source</c> tag.</summary>
public class HTMLSourceElement : HTMLElement
{
    /// <summary>Creates the fixed <c>source</c> element.</summary>
    public HTMLSourceElement() : base("source") { }
}

/// <summary>Concrete element for the <c>span</c> tag.</summary>
public class HTMLSpanElement : HTMLElement
{
    /// <summary>Creates the fixed <c>span</c> element.</summary>
    public HTMLSpanElement() : base("span") { }
}

/// <summary>Concrete element for the <c>strong</c> tag.</summary>
public class HTMLStrongElement : HTMLElement
{
    /// <summary>Creates the fixed <c>strong</c> element.</summary>
    public HTMLStrongElement() : base("strong") { }
}

/// <summary>Concrete element for the <c>style</c> tag; author style is parsed by the Square CSS engine.</summary>
public class HTMLStyleElement : HTMLElement
{
    /// <summary>Creates the fixed <c>style</c> element.</summary>
    public HTMLStyleElement() : base("style") { }
}

/// <summary>Concrete element for the <c>sub</c> tag.</summary>
public class HTMLSubElement : HTMLElement
{
    /// <summary>Creates the fixed <c>sub</c> element.</summary>
    public HTMLSubElement() : base("sub") { }
}

/// <summary>Concrete element for the <c>summary</c> tag.</summary>
public class HTMLSummaryElement : HTMLElement
{
    /// <summary>Creates the fixed <c>summary</c> element.</summary>
    public HTMLSummaryElement() : base("summary") { }
}

/// <summary>Concrete element for the <c>sup</c> tag.</summary>
public class HTMLSupElement : HTMLElement
{
    /// <summary>Creates the fixed <c>sup</c> element.</summary>
    public HTMLSupElement() : base("sup") { }
}

/// <summary>Concrete element for the <c>table</c> tag.</summary>
public class HTMLTableElement : HTMLElement
{
    /// <summary>Creates the fixed <c>table</c> element.</summary>
    public HTMLTableElement() : base("table") { }
}

/// <summary>Concrete element for the <c>tbody</c> tag.</summary>
public class HTMLTbodyElement : HTMLTableSectionElement
{
    /// <summary>Creates the fixed <c>tbody</c> element.</summary>
    public HTMLTbodyElement() : base("tbody") { }
}

/// <summary>Concrete element for the <c>td</c> tag.</summary>
public class HTMLTdElement : HTMLTableCellElement
{
    /// <summary>Creates the fixed <c>td</c> element.</summary>
    public HTMLTdElement() : base("td") { }
}

/// <summary>Concrete element for the <c>template</c> tag (nested template is a lazy inert subtree, not a fragment wrapper).</summary>
public class HTMLTemplateElement : HTMLElement
{
    /// <summary>Creates the fixed <c>template</c> element.</summary>
    public HTMLTemplateElement() : base("template") { }
}

/// <summary>Concrete element for the <c>textarea</c> tag.</summary>
public class HTMLTextAreaElement : HTMLElement
{
    /// <summary>Creates the fixed <c>textarea</c> element.</summary>
    public HTMLTextAreaElement() : base("textarea") { }

    /// <summary>The <c>value</c>-like text mirrored by the host; absent is <c>null</c>.</summary>
    public string? Value
    {
        get => GetAttribute("value");
        set => HtmlAttributeValues.Set(this, "value", value);
    }
}

/// <summary>Concrete element for the <c>tfoot</c> tag.</summary>
public class HTMLTfootElement : HTMLTableSectionElement
{
    /// <summary>Creates the fixed <c>tfoot</c> element.</summary>
    public HTMLTfootElement() : base("tfoot") { }
}

/// <summary>Concrete element for the <c>th</c> tag.</summary>
public class HTMLThElement : HTMLTableCellElement
{
    /// <summary>Creates the fixed <c>th</c> element.</summary>
    public HTMLThElement() : base("th") { }
}

/// <summary>Concrete element for the <c>thead</c> tag.</summary>
public class HTMLTheadElement : HTMLTableSectionElement
{
    /// <summary>Creates the fixed <c>thead</c> element.</summary>
    public HTMLTheadElement() : base("thead") { }
}

/// <summary>Concrete element for the <c>time</c> tag.</summary>
public class HTMLTimeElement : HTMLElement
{
    /// <summary>Creates the fixed <c>time</c> element.</summary>
    public HTMLTimeElement() : base("time") { }
}

/// <summary>Concrete element for the <c>title</c> tag.</summary>
public class HTMLTitleElement : HTMLElement
{
    /// <summary>Creates the fixed <c>title</c> element.</summary>
    public HTMLTitleElement() : base("title") { }
}

/// <summary>Concrete element for the <c>tr</c> tag.</summary>
public class HTMLTableRowElement : HTMLElement
{
    /// <summary>Creates the fixed <c>tr</c> element.</summary>
    public HTMLTableRowElement() : base("tr") { }
}

/// <summary>Concrete element for the <c>track</c> tag.</summary>
public class HTMLTrackElement : HTMLElement
{
    /// <summary>Creates the fixed <c>track</c> element.</summary>
    public HTMLTrackElement() : base("track") { }
}

/// <summary>Concrete element for the <c>u</c> tag.</summary>
public class HTMLUElement : HTMLElement
{
    /// <summary>Creates the fixed <c>u</c> element.</summary>
    public HTMLUElement() : base("u") { }
}

/// <summary>Concrete element for the <c>ul</c> tag.</summary>
public class HTMLUListElement : HTMLElement
{
    /// <summary>Creates the fixed <c>ul</c> element.</summary>
    public HTMLUListElement() : base("ul") { }
}

/// <summary>Concrete element for the <c>var</c> tag.</summary>
public class HTMLVarElement : HTMLElement
{
    /// <summary>Creates the fixed <c>var</c> element.</summary>
    public HTMLVarElement() : base("var") { }
}

/// <summary>Concrete element for the <c>video</c> tag; decoding/playback stays a documented disabled behavior.</summary>
public class HTMLVideoElement : HTMLElement
{
    /// <summary>Creates the fixed <c>video</c> element.</summary>
    public HTMLVideoElement() : base("video") { }

    /// <summary>The <c>poster</c> attribute; absent is <c>null</c>.</summary>
    public string? Poster
    {
        get => GetAttribute("poster");
        set => HtmlAttributeValues.Set(this, "poster", value);
    }
}

/// <summary>Concrete element for the <c>wbr</c> tag.</summary>
public class HTMLWbrElement : HTMLElement
{
    /// <summary>Creates the fixed <c>wbr</c> element.</summary>
    public HTMLWbrElement() : base("wbr") { }
}

/// <summary>
/// Thin attribute-backed value helpers for the typed element properties. Attribute storage on
/// <see cref="HTMLElement"/> remains the single source of truth: booleans map to presence,
/// numbers round-trip invariantly, and absent/invalid values surface as <c>null</c> so the
/// host's existing degradation logic is preserved.
/// </summary>
internal static class HtmlAttributeValues
{
    public static string? Get(HTMLElement element, string name) => element.GetAttribute(name);

    public static void Set(HTMLElement element, string name, string? value)
    {
        if (value is null) element.RemoveAttribute(name);
        else element.SetAttribute(name, value);
    }

    public static bool GetFlag(HTMLElement element, string name) => element.HasAttribute(name);

    public static void SetFlag(HTMLElement element, string name, bool value)
    {
        if (value) element.SetAttribute(name, string.Empty);
        else element.RemoveAttribute(name);
    }

    public static int? GetInt32(HTMLElement element, string name) =>
        element.GetAttribute(name) is { } raw &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    public static void SetInt32(HTMLElement element, string name, int? value)
    {
        if (value is null) element.RemoveAttribute(name);
        else element.SetAttribute(name, value.Value.ToString(CultureInfo.InvariantCulture));
    }

    public static double? GetDouble(HTMLElement element, string name) =>
        element.GetAttribute(name) is { } raw &&
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    public static void SetDouble(HTMLElement element, string name, double? value)
    {
        if (value is null) element.RemoveAttribute(name);
        else element.SetAttribute(name, value.Value.ToString(CultureInfo.InvariantCulture));
    }
}
