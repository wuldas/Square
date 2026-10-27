#nullable enable

namespace Square.Html;

/// <summary>
/// Frozen WHATWG conforming HTML elements (22 September 2026).
/// Pure string data: compiled into the netstandard2.0 analyzer, so it must not reference
/// <c>typeof</c>, delegates, or any other Square runtime type. The tag → CLR name table
/// (<see cref="TypeNames"/>) is the single source consumed by the compiler descriptors and,
/// at runtime, cross-checked against <see cref="HtmlElementFactory"/>.
/// </summary>
public static class HtmlTagCatalog
{
    private const string Names =
        "a abbr address area article aside audio b base bdi bdo blockquote body br button canvas caption cite code col colgroup " +
        "data datalist dd del details dfn dialog div dl dt em embed fieldset figcaption figure footer form " +
        "h1 h2 h3 h4 h5 h6 head header hgroup hr html i iframe img input ins kbd label legend li link main " +
        "map mark menu meta meter nav noscript object ol optgroup option output p picture pre progress q rp rt ruby s samp " +
        "script search section select selectedcontent slot small source span strong style sub summary sup table tbody td " +
        "template textarea tfoot th thead time title tr track u ul var video wbr";

    public static readonly IReadOnlyList<string> Tags = Names.Split(' ');
    private static readonly HashSet<string> Known = new(Tags, StringComparer.Ordinal);
    private static readonly HashSet<string> Void = Set("area base br col embed hr img input link meta source track wbr");
    private static readonly HashSet<string> Metadata = Set("base head link meta script style template title");
    private static readonly HashSet<string> Blocks = Set("address article aside blockquote body dd details dialog div dl dt fieldset figcaption figure footer form h1 h2 h3 h4 h5 h6 header hgroup hr html legend li main menu nav ol p pre search section summary ul");
    private static readonly HashSet<string> Table = Set("table caption colgroup col thead tbody tfoot tr th td");
    private static readonly HashSet<string> Replaced = Set("area audio canvas embed iframe img input object progress meter select textarea video");
    private static readonly HashSet<string> Disabled = Set("base link script style iframe object embed");

    public static readonly IReadOnlyCollection<string> BooleanAttributes = Set(
        "allowfullscreen alpha async autofocus autoplay checked controls default defer disabled disableremoteplayback " +
        "formnovalidate inert ismap itemscope loop multiple muted nomodule novalidate open playsinline readonly required reversed " +
        "selected shadowrootclonable shadowrootdelegatesfocus shadowrootserializable truespeed typemustmatch");

    /// <summary>Short CLR type names keyed by exact lowercase tag; entry order mirrors <see cref="Tags"/>.</summary>
    private static readonly Dictionary<string, string> ClrShortNames = new(StringComparer.Ordinal)
    {
        ["a"] = "HTMLAnchorElement",
        ["abbr"] = "HTMLAbbrElement",
        ["address"] = "HTMLAddressElement",
        ["area"] = "HTMLAreaElement",
        ["article"] = "HTMLArticleElement",
        ["aside"] = "HTMLAsideElement",
        ["audio"] = "HTMLAudioElement",
        ["b"] = "HTMLBElement",
        ["base"] = "HTMLBaseElement",
        ["bdi"] = "HTMLBdiElement",
        ["bdo"] = "HTMLBdoElement",
        ["blockquote"] = "HTMLBlockquoteElement",
        ["body"] = "HTMLBodyElement",
        ["br"] = "HTMLBRElement",
        ["button"] = "HTMLButtonElement",
        ["canvas"] = "HTMLCanvasElement",
        ["caption"] = "HTMLTableCaptionElement",
        ["cite"] = "HTMLCiteElement",
        ["code"] = "HTMLCodeElement",
        ["col"] = "HTMLColElement",
        ["colgroup"] = "HTMLColgroupElement",
        ["data"] = "HTMLDataElement",
        ["datalist"] = "HTMLDataListElement",
        ["dd"] = "HTMLDdElement",
        ["del"] = "HTMLDelElement",
        ["details"] = "HTMLDetailsElement",
        ["dfn"] = "HTMLDfnElement",
        ["dialog"] = "HTMLDialogElement",
        ["div"] = "HTMLDivElement",
        ["dl"] = "HTMLDListElement",
        ["dt"] = "HTMLDtElement",
        ["em"] = "HTMLEmElement",
        ["embed"] = "HTMLEmbedElement",
        ["fieldset"] = "HTMLFieldSetElement",
        ["figcaption"] = "HTMLFigcaptionElement",
        ["figure"] = "HTMLFigureElement",
        ["footer"] = "HTMLFooterElement",
        ["form"] = "HTMLFormElement",
        ["h1"] = "HTMLH1Element",
        ["h2"] = "HTMLH2Element",
        ["h3"] = "HTMLH3Element",
        ["h4"] = "HTMLH4Element",
        ["h5"] = "HTMLH5Element",
        ["h6"] = "HTMLH6Element",
        ["head"] = "HTMLHeadElement",
        ["header"] = "HTMLHeaderElement",
        ["hgroup"] = "HTMLHgroupElement",
        ["hr"] = "HTMLHRElement",
        ["html"] = "HTMLHtmlElement",
        ["i"] = "HTMLIElement",
        ["iframe"] = "HTMLIFrameElement",
        ["img"] = "HTMLImageElement",
        ["input"] = "HTMLInputElement",
        ["ins"] = "HTMLInsElement",
        ["kbd"] = "HTMLKbdElement",
        ["label"] = "HTMLLabelElement",
        ["legend"] = "HTMLLegendElement",
        ["li"] = "HTMLLIElement",
        ["link"] = "HTMLLinkElement",
        ["main"] = "HTMLMainElement",
        ["map"] = "HTMLMapElement",
        ["mark"] = "HTMLMarkElement",
        ["menu"] = "HTMLMenuElement",
        ["meta"] = "HTMLMetaElement",
        ["meter"] = "HTMLMeterElement",
        ["nav"] = "HTMLNavElement",
        ["noscript"] = "HTMLNoscriptElement",
        ["object"] = "HTMLObjectElement",
        ["ol"] = "HTMLOListElement",
        ["optgroup"] = "HTMLOptGroupElement",
        ["option"] = "HTMLOptionElement",
        ["output"] = "HTMLOutputElement",
        ["p"] = "HTMLParagraphElement",
        ["picture"] = "HTMLPictureElement",
        ["pre"] = "HTMLPreElement",
        ["progress"] = "HTMLProgressElement",
        ["q"] = "HTMLQElement",
        ["rp"] = "HTMLRpElement",
        ["rt"] = "HTMLRtElement",
        ["ruby"] = "HTMLRubyElement",
        ["s"] = "HTMLSElement",
        ["samp"] = "HTMLSampElement",
        ["script"] = "HTMLScriptElement",
        ["search"] = "HTMLSearchElement",
        ["section"] = "HTMLSectionElement",
        ["select"] = "HTMLSelectElement",
        ["selectedcontent"] = "HTMLSelectedContentElement",
        ["slot"] = "HTMLSlotElement",
        ["small"] = "HTMLSmallElement",
        ["source"] = "HTMLSourceElement",
        ["span"] = "HTMLSpanElement",
        ["strong"] = "HTMLStrongElement",
        ["style"] = "HTMLStyleElement",
        ["sub"] = "HTMLSubElement",
        ["summary"] = "HTMLSummaryElement",
        ["sup"] = "HTMLSupElement",
        ["table"] = "HTMLTableElement",
        ["tbody"] = "HTMLTbodyElement",
        ["td"] = "HTMLTdElement",
        ["template"] = "HTMLTemplateElement",
        ["textarea"] = "HTMLTextAreaElement",
        ["tfoot"] = "HTMLTfootElement",
        ["th"] = "HTMLThElement",
        ["thead"] = "HTMLTheadElement",
        ["time"] = "HTMLTimeElement",
        ["title"] = "HTMLTitleElement",
        ["tr"] = "HTMLTableRowElement",
        ["track"] = "HTMLTrackElement",
        ["u"] = "HTMLUElement",
        ["ul"] = "HTMLUListElement",
        ["var"] = "HTMLVarElement",
        ["video"] = "HTMLVideoElement",
        ["wbr"] = "HTMLWbrElement",
    };

    /// <summary>Fixed tag → concrete CLR full name (Square.Html.*) mapping; one unique type per tag.</summary>
    public static IReadOnlyDictionary<string, string> TypeNames { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            ClrShortNames.ToDictionary(pair => pair.Key, pair => "Square.Html." + pair.Value, StringComparer.Ordinal));

    /// <summary>Returns the fixed concrete CLR full name for an exact lowercase tag.</summary>
    public static bool TryGetTypeName(string tag, out string? typeName)
    {
        if (tag != null && ClrShortNames.TryGetValue(tag, out var shortName))
        {
            typeName = "Square.Html." + shortName;
            return true;
        }
        typeName = null;
        return false;
    }

    public static bool IsTag(string name) => Known.Contains(name);
    public static bool IsVoid(string name) => Void.Contains(name);
    public static bool IsMetadata(string name) => Metadata.Contains(name);
    public static bool IsBlock(string name) => Blocks.Contains(name);
    public static bool IsTable(string name) => Table.Contains(name);
    public static bool IsReplaced(string name) => Replaced.Contains(name);
    public static bool IsDisabledActiveContent(string name) => Disabled.Contains(name);

    private static HashSet<string> Set(string names) => new(names.Split(' '), StringComparer.Ordinal);
}
