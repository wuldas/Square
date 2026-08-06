using Square.CSS.Engine;
using Square.CSS.Tokenizer;
using Square.Graphics;
using Square.Rendering;
using Square.UI;

namespace Square.CSS.Tests;

public sealed record Css21FixtureDocument(
    Element Root,
    IReadOnlyDictionary<string, Element> Elements)
{
    public Element Get(string name) => Elements.TryGetValue(name, out var element)
        ? element
        : throw new KeyNotFoundException($"Fixture element '{name}' was not registered.");
}

[Flags]
public enum Css21PaintFlags
{
    None = 0,
    NeedsPaint = 1,
    FullPaintDirty = 2,
    PartialPaintDirty = 4,
    HasGeometry = 8,
    Displayed = 16
}

public sealed record Css21Fixture(
    string Id,
    string FeatureId,
    string Section,
    string Css,
    Func<Css21FixtureDocument> ElementFactory,
    IReadOnlyDictionary<string, string?> ExpectedComputedStyles,
    IReadOnlyDictionary<string, Rect> ExpectedGeometry,
    IReadOnlyDictionary<string, Css21PaintFlags> ExpectedPaintFlags,
    Size Viewport,
    string MediaType = "screen",
    IReadOnlyDictionary<string, IReadOnlyList<string>>? ExpectedChildText = null,
    Css21MediaSwitch? ExpectedMediaSwitch = null,
    Css21FontFaceExpectation? ExpectedFontFaces = null,
    Css21TextLayoutExpectation? ExpectedTextLayout = null);

public sealed record Css21MediaSwitch(
    string MediaType,
    IReadOnlyDictionary<string, string?> ExpectedComputedStyles);

public sealed record Css21FontFaceExpectation(
    int DescriptorCount,
    IReadOnlyList<Css21FontFaceDescriptorExpectation> Descriptors);

public sealed record Css21FontFaceDescriptorExpectation(
    string Family,
    string Source,
    bool IsLocal);

public sealed record Css21TextLayoutExpectation(
    string Element,
    IReadOnlyList<int> VisualStartOffsets,
    IReadOnlyList<Css21TextHitTestExpectation>? HitTests = null);

public sealed record Css21TextHitTestExpectation(
    int CharacterIndex,
    bool FromLeftEdge,
    int ExpectedOffset);

public sealed record Css21MediaSwitchResult(
    Css21FixtureDocument Document,
    CssEngine Engine);

public static class Css21FixtureRunner
{
    public static Css21FixtureDocument Apply(Css21Fixture fixture)
    {
        var document = fixture.ElementFactory();
        var engine = CreateEngine(fixture);
        engine.ApplyStylesToTree(document.Root);
        new LayoutEngine().MeasureAndArrange(document.Root, fixture.Viewport);
        return document;
    }

    public static Css21MediaSwitchResult ApplyMediaSwitch(Css21Fixture fixture)
    {
        if (fixture.ExpectedMediaSwitch == null)
            throw new InvalidOperationException($"Fixture '{fixture.Id}' does not define a media switch.");

        var document = fixture.ElementFactory();
        var engine = CreateEngine(fixture);
        engine.ApplyStylesToTree(document.Root);
        engine.SetMediaType(fixture.ExpectedMediaSwitch.MediaType);
        CssStyleReconciler.Flush();
        return new Css21MediaSwitchResult(document, engine);
    }

    public static CssEngine CreateEngine(Css21Fixture fixture)
    {
        var sheet = new CssParser(new CssTokenizer(fixture.Css).Tokenize()).Parse();
        var engine = new CssEngine(fixture.MediaType);
        engine.LoadStyleSheet(sheet);
        return engine;
    }

    public static IReadOnlyList<TextFragment> CollectTextFragments(Css21FixtureDocument document)
    {
        var tree = new DisplayTree();
        tree.BuildFrom(document.Root);
        return tree.CollectTextFragments(document.Root);
    }

    public static Css21PaintFlags GetPaintFlags(Element element)
    {
        var flags = Css21PaintFlags.None;
        if (element.NeedsPaint) flags |= Css21PaintFlags.NeedsPaint;
        if (element.IsPaintFullDirty) flags |= Css21PaintFlags.FullPaintDirty;
        if (element.PaintDirtyRects.Count > 0) flags |= Css21PaintFlags.PartialPaintDirty;
        if (!element.Geometry.IsEmpty) flags |= Css21PaintFlags.HasGeometry;
        if (IsDisplayed(element)) flags |= Css21PaintFlags.Displayed;
        return flags;
    }

    private static bool IsDisplayed(Element element)
    {
        for (var current = element; current != null; current = current.Parent)
            if (string.Equals(current.Style.Get("display")?.Trim(), "none", StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }
}

public enum Css21FeatureStatus
{
    Supported,
    Deferred
}

public sealed record Css21FeatureManifestEntry(
    string Id,
    string Section,
    Css21FeatureStatus Status,
    string Limitation);

public static class Css21FeatureManifest
{
    // Keep this list independent from Entries so the report test catches both typos and duplicates.
    public static IReadOnlySet<string> KnownFeatureIds { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "CSS21-SYNTAX",
        "CSS21-SELECTORS",
        "CSS21-CASCADE",
        "CSS21-VALUES",
        "CSS21-BOX",
        "CSS21-VISUAL-FORMATTING",
        "CSS21-GENERATED-CONTENT",
        "CSS21-TABLES",
        "CSS21-FONTS",
        "CSS21-MEDIA",
        "CSS21-PAINT-FLAGS",
        "CSS21-AT-FONT-FACE",
        "CSS21-BIDI",
        "CSS21-PAGED-MEDIA",
        "CSS21-ANONYMOUS-BOXES",
        "CSS21-FULL-COLOR-PAINT",
        "CSS22-PARSER-NUMBERS-ESCAPES",
        "CSS22-FONT-FAMILY-KEYWORDS",
        "CSS22-MARGIN-COLLAPSE",
        "CSS22-FORMATTING-CONTEXTS-CONTAINING-BLOCKS",
        "CSS22-OVERFLOW-TABLE-BEHAVIOR",
        "CSS22-TABLE-WRAPPER",
        "CSS22-FIXED-STACKING",
        "CSS22-CANVAS-BACKGROUND-VISIBILITY",
        "CSS22-HEIGHT-PERCENTAGE-COMPUTED",
        "CSS22-MALFORMED-DECLARATION-RECOVERY",
        "CSS22-W3C-CONFORMANCE",
        "CSS22-ANONYMOUS-BOXES",
        "CSS22-FULL-BFC",
        "CSS22-FULL-TABLE-MODEL",
        "CSS22-FULL-UNICODE-BIDI",
        "CSS22-PAGED-MEDIA"
    };

    public static IReadOnlyList<Css21FeatureManifestEntry> Entries { get; } =
    [
        new("CSS21-SYNTAX", "2.1 Syntax", Css21FeatureStatus.Supported,
            "Local tokenizer/parser fixtures cover accepted declarations and recovery cases."),
        new("CSS21-SELECTORS", "5 Selectors", Css21FeatureStatus.Supported,
            "Local fixtures cover type, class, ID, descendant, child, and structural matching."),
        new("CSS21-CASCADE", "6 Assigning property values", Css21FeatureStatus.Supported,
            "Local fixtures cover inheritance, specificity, source order, and !important."),
        new("CSS21-VALUES", "4 Values", Css21FeatureStatus.Supported,
            "Local fixtures compare serialized computed values for the exercised value subset."),
        new("CSS21-BOX", "8 Box model", Css21FeatureStatus.Supported,
            "Local fixtures compare logical box geometry, padding, margins, and dimensions."),
        new("CSS21-VISUAL-FORMATTING", "9 Visual formatting", Css21FeatureStatus.Supported,
            "Local fixtures exercise block flow and explicit positioned boxes."),
        new("CSS21-GENERATED-CONTENT", "12 Generated content", Css21FeatureStatus.Supported,
            "Local fixtures exercise before/after text generation and child ordering."),
        new("CSS21-TABLES", "17 Tables", Css21FeatureStatus.Supported,
            "Local fixtures exercise the implemented table sizing and cell geometry subset."),
        new("CSS21-FONTS", "15 Fonts", Css21FeatureStatus.Supported,
            "Local fixtures compare inherited and direct font declarations; shaping remains platform-specific."),
        new("CSS21-MEDIA", "7 Media types", Css21FeatureStatus.Supported,
            "Local fixtures exercise screen and print media type selection only."),
        new("CSS21-PAINT-FLAGS", "11 Harness paint contract", Css21FeatureStatus.Supported,
            "Fixtures assert invalidation and geometry flags, not raster output."),
        new("CSS21-AT-FONT-FACE", "15 Fonts", Css21FeatureStatus.Supported,
            "Local fixtures cover descriptor parsing and portable local-file LoadFontsAsync loading; remote/data sources, CSS source selection, and full web-font format support are not claimed."),
        new("CSS21-BIDI", "9 Visual formatting", Css21FeatureStatus.Supported,
            "Local fixtures cover direction, unicode-bidi, and basic run-level bidi mapping; Arabic shaping, glyph mirroring, isolates, and full UAX #9 conformance are not claimed."),
        new("CSS21-PAGED-MEDIA", "13 Paged media", Css21FeatureStatus.Deferred,
            "Pagination, page boxes, and print layout beyond media type selection are deferred."),
        new("CSS21-ANONYMOUS-BOXES", "9 Visual formatting", Css21FeatureStatus.Deferred,
            "Complete anonymous block/inline/table box construction is deferred."),
        new("CSS21-FULL-COLOR-PAINT", "14 Colors and backgrounds", Css21FeatureStatus.Deferred,
            "Full CSS color syntax and browser-equivalent background/border painting are deferred."),
        new("CSS22-PARSER-NUMBERS-ESCAPES", "CSS2.2 revision/errata: parser numbers and escapes", Css21FeatureStatus.Supported,
            "Portable tokenizer fixtures cover leading-dot numbers, exponents, escaped identifiers, and hexadecimal escapes."),
        new("CSS22-FONT-FAMILY-KEYWORDS", "CSS2.2 revision/errata: font-family keywords", Css21FeatureStatus.Supported,
            "Local fixtures preserve family lists; portable tests cover the generic-family fallback mappings exposed by FontManager."),
        new("CSS22-MARGIN-COLLAPSE", "CSS2.2 revision/errata: margin collapse clarifications", Css21FeatureStatus.Supported,
            "Portable layout fixtures cover adjacent positive block margins; parent, empty-block, clearance, and root-edge cases remain limited."),
        new("CSS22-FORMATTING-CONTEXTS-CONTAINING-BLOCKS", "CSS2.2 revision/errata: formatting contexts and containing blocks", Css21FeatureStatus.Supported,
            "Portable fixtures cover the implemented block path and direct absolute containing-block geometry; full BFC behavior is deferred."),
        new("CSS22-OVERFLOW-TABLE-BEHAVIOR", "CSS2.2 revision/errata: overflow table behavior", Css21FeatureStatus.Supported,
            "Portable tests cover the public table overflow clipping and scroll-container contract; complete table overflow interoperability is not claimed."),
        new("CSS22-TABLE-WRAPPER", "CSS2.2 revision/errata: table wrapper", Css21FeatureStatus.Supported,
            "Portable fixtures cover the implemented table and inline-table roots; anonymous wrapper construction is deferred."),
        new("CSS22-FIXED-STACKING", "CSS2.2 revision/errata: fixed stacking context", Css21FeatureStatus.Supported,
            "Portable tests cover the viewport fixed layer and simple z-index ordering; full browser stacking-context isolation is deferred."),
        new("CSS22-CANVAS-BACKGROUND-VISIBILITY", "CSS2.2 revision/errata: canvas background and display/visibility", Css21FeatureStatus.Supported,
            "Portable display-tree tests distinguish display:none removal from visibility:hidden paint suppression on Canvas."),
        new("CSS22-HEIGHT-PERCENTAGE-COMPUTED", "CSS2.2 revision/errata: height percentage computed value", Css21FeatureStatus.Supported,
            "Portable style fixtures verify that a percentage height remains the computed Style value; used-value resolution is not generalized."),
        new("CSS22-MALFORMED-DECLARATION-RECOVERY", "CSS2.2 revision/errata: malformed declaration recovery", Css21FeatureStatus.Supported,
            "Portable parser fixtures verify recovery at the next semicolon and preservation of following valid declarations."),
        new("CSS22-W3C-CONFORMANCE", "CSS2.2 full W3C conformance", Css21FeatureStatus.Deferred,
            "The full CSS2.2 W3C test suite is not imported or claimed."),
        new("CSS22-ANONYMOUS-BOXES", "CSS2.2 anonymous boxes", Css21FeatureStatus.Deferred,
            "Complete anonymous block, inline, and table box construction is deferred."),
        new("CSS22-FULL-BFC", "CSS2.2 full block formatting context", Css21FeatureStatus.Deferred,
            "Complete BFC establishment, float interaction, clearance, and containment rules are deferred."),
        new("CSS22-FULL-TABLE-MODEL", "CSS2.2 full table model", Css21FeatureStatus.Deferred,
            "The complete table wrapper, anonymous table boxes, border conflict, and table layout model are deferred."),
        new("CSS22-FULL-UNICODE-BIDI", "CSS2.2 full Unicode bidi", Css21FeatureStatus.Deferred,
            "Full Unicode bidirectional text layout, shaping, mirroring, and UAX #9 behavior are deferred."),
        new("CSS22-PAGED-MEDIA", "CSS2.2 paged media", Css21FeatureStatus.Deferred,
            "Page boxes, pagination, fragmentation, and paged-media layout are deferred.")
    ];

    public static IReadOnlyList<Css21FeatureManifestEntry> Css21Entries =>
        Entries.Where(entry => entry.Id.StartsWith("CSS21-", StringComparison.Ordinal)).ToArray();

    public static IReadOnlyList<Css21FeatureManifestEntry> Css22Entries =>
        Entries.Where(entry => entry.Id.StartsWith("CSS22-", StringComparison.Ordinal)).ToArray();

    public static IReadOnlyList<Css21FeatureManifestEntry> Css21Supported =>
        Css21Entries.Where(entry => entry.Status == Css21FeatureStatus.Supported).ToArray();

    public static IReadOnlyList<Css21FeatureManifestEntry> Css21Deferred =>
        Css21Entries.Where(entry => entry.Status == Css21FeatureStatus.Deferred).ToArray();

    public static IReadOnlyList<Css21FeatureManifestEntry> Css22Supported =>
        Css22Entries.Where(entry => entry.Status == Css21FeatureStatus.Supported).ToArray();

    public static IReadOnlyList<Css21FeatureManifestEntry> Css22Deferred =>
        Css22Entries.Where(entry => entry.Status == Css21FeatureStatus.Deferred).ToArray();

    public static IReadOnlyList<Css21FeatureManifestEntry> Supported =>
        Entries.Where(entry => entry.Status == Css21FeatureStatus.Supported).ToArray();

    public static IReadOnlyList<Css21FeatureManifestEntry> Deferred =>
        Entries.Where(entry => entry.Status == Css21FeatureStatus.Deferred).ToArray();

    public static IReadOnlyList<string> Validate()
    {
        var issues = new List<string>();
        var ids = Entries.Select(entry => entry.Id).ToArray();
        foreach (var duplicate in ids.GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() > 1))
            issues.Add($"duplicate feature ID: {duplicate.Key}");
        foreach (var unknown in ids.Where(id => !KnownFeatureIds.Contains(id)).Distinct(StringComparer.Ordinal))
            issues.Add($"unknown feature ID: {unknown}");
        foreach (var missing in KnownFeatureIds.Where(id => !ids.Contains(id, StringComparer.Ordinal)))
            issues.Add($"missing feature ID: {missing}");
        return issues;
    }

    public static string BuildReport()
    {
        static string Format(IEnumerable<Css21FeatureManifestEntry> entries) =>
            string.Join(", ", entries.Select(entry => entry.Id));

        return string.Join(Environment.NewLine,
            "CSS2.1/CSS2.2 local fixture manifest",
            $"Supported ({Supported.Count}): {Format(Supported)}",
            $"Deferred ({Deferred.Count}): {Format(Deferred)}",
            $"CSS2.2 revision/errata supported ({Css22Supported.Count}): {Format(Css22Supported)}",
            $"CSS2.2 revision/errata deferred ({Css22Deferred.Count}): {Format(Css22Deferred)}");
    }
}
