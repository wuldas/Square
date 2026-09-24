namespace Square.UI.Html;

/// <summary>Frozen WHATWG conforming HTML elements (22 September 2026).</summary>
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

    public static bool IsTag(string name) => Known.Contains(name);
    public static bool IsVoid(string name) => Void.Contains(name);
    public static bool IsMetadata(string name) => Metadata.Contains(name);
    public static bool IsBlock(string name) => Blocks.Contains(name);
    public static bool IsTable(string name) => Table.Contains(name);
    public static bool IsReplaced(string name) => Replaced.Contains(name);
    public static bool IsDisabledActiveContent(string name) => Disabled.Contains(name);

    private static HashSet<string> Set(string names) => new(names.Split(' '), StringComparer.Ordinal);
}