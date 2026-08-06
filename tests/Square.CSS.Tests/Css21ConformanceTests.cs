using Square.CSS.Engine;
using Square.CSS.Tokenizer;
using Square.Graphics;
using Square.UI;
using Xunit;

namespace Square.CSS.Tests;

public sealed class Css21ConformanceTests
{
    [Fact]
    public void LocalFixtureCatalogCoversRequestedCss21Sections()
    {
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "Syntax",
            "Selectors",
            "Cascade",
            "Values",
            "Box model",
            "Visual formatting",
            "Generated content",
            "Tables",
            "Fonts",
            "Media"
        };

        Assert.True(expected.IsSubsetOf(Css21ConformanceFixtureCatalog.All.Select(fixture => fixture.Section)),
            "The local fixture catalog must retain coverage for every requested CSS2.1 section.");
    }

    [Fact]
    public void LocalCss21FixturesMatchComputedStylesGeometryAndPaintFlags()
    {
        foreach (var fixture in Css21ConformanceFixtureCatalog.All)
        {
            var document = Css21FixtureRunner.Apply(fixture);

            foreach (var expected in fixture.ExpectedComputedStyles)
            {
                var separator = expected.Key.IndexOf('.');
                Assert.True(separator > 0, $"{fixture.Id}: style key '{expected.Key}' must be element.property.");
                var actual = document.Get(expected.Key[..separator]).Style.Get(expected.Key[(separator + 1)..]);
                Assert.Equal(expected.Value, actual);
            }

            foreach (var expected in fixture.ExpectedGeometry)
            {
                var actual = document.Get(expected.Key).Geometry;
                AssertRectClose(expected.Value, actual, $"{fixture.Id}: {expected.Key}");
            }

            foreach (var expected in fixture.ExpectedPaintFlags)
                Assert.Equal(expected.Value, Css21FixtureRunner.GetPaintFlags(document.Get(expected.Key)));

            if (fixture.ExpectedChildText != null)
            {
                foreach (var expected in fixture.ExpectedChildText)
                {
                    var actual = document.Get(expected.Key).Children
                        .OfType<Square.Controls.Text>()
                        .Select(child => child.TextContent)
                        .ToArray();
                    Assert.Equal(expected.Value, actual);
                }
            }

            if (fixture.ExpectedMediaSwitch != null)
            {
                var switched = Css21FixtureRunner.ApplyMediaSwitch(fixture);
                Assert.Equal(fixture.ExpectedMediaSwitch.MediaType, switched.Engine.MediaType);
                foreach (var expected in fixture.ExpectedMediaSwitch.ExpectedComputedStyles)
                {
                    var separator = expected.Key.IndexOf('.');
                    Assert.True(separator > 0, $"{fixture.Id}: switched style key '{expected.Key}' must be element.property.");
                    var actual = switched.Document.Get(expected.Key[..separator]).Style.Get(expected.Key[(separator + 1)..]);
                    Assert.Equal(expected.Value, actual);
                }
            }

            if (fixture.ExpectedFontFaces != null)
            {
                var engine = Css21FixtureRunner.CreateEngine(fixture);
                Assert.Equal(fixture.ExpectedFontFaces.DescriptorCount, engine.FontFaceDescriptors.Count);
                Assert.Equal(fixture.ExpectedFontFaces.Descriptors.Count, engine.FontFaceDescriptors.Count);
                for (var index = 0; index < engine.FontFaceDescriptors.Count; index++)
                {
                    var expected = fixture.ExpectedFontFaces.Descriptors[index];
                    var actual = engine.FontFaceDescriptors[index];
                    Assert.Equal(expected.Family, actual.Family);
                    Assert.Equal(expected.Source, actual.Source);
                    Assert.Equal(expected.IsLocal, actual.IsLocal);
                }
            }

            if (fixture.ExpectedTextLayout != null)
            {
                var expected = fixture.ExpectedTextLayout;
                var text = document.Get(expected.Element);
                var fragment = Assert.Single(
                    Css21FixtureRunner.CollectTextFragments(document),
                    fragment => ReferenceEquals(fragment.Element, text));
                Assert.Equal(expected.VisualStartOffsets, fragment.Characters
                    .Select(character => character.StartOffset));

                if (expected.HitTests != null)
                {
                    foreach (var hitTest in expected.HitTests)
                    {
                        var character = fragment.Characters[hitTest.CharacterIndex];
                        var x = hitTest.FromLeftEdge ? character.Bounds.Left + 0.1f : character.Bounds.Right - 0.1f;
                        var actual = fragment.HitTestOffset(new Point(x, character.Bounds.Y + 1));
                        Assert.Equal(hitTest.ExpectedOffset, actual);
                    }
                }
            }
        }
    }

    [Fact]
    public void SyntaxRecoveryDoesNotLeakMalformedSelectorRules()
    {
        var sheet = Parse("View, . { color: red; } Button { color: blue; }");

        var rule = Assert.Single(sheet.Rules);
        Assert.Equal("Button", rule.Selector.Steps[0].Selector.Parts[0].Name);
        Assert.Equal("blue", rule.Declarations[0].Value);
    }

    [Fact]
    public void PaintFlagsRepresentPartialInvalidationSeparatelyFromFullInvalidation()
    {
        var element = new Square.Controls.View();
        element.ClearPaintDirty();
        element.InvalidatePaint(new Rect(1, 2, 3, 4));

        var flags = Css21FixtureRunner.GetPaintFlags(element);

        Assert.Equal(Css21PaintFlags.NeedsPaint | Css21PaintFlags.PartialPaintDirty | Css21PaintFlags.Displayed, flags);
    }

    [Fact]
    public void FeatureManifestReportsSupportedAndDeferredIdsWithoutUnknownOrDuplicateEntries()
    {
        Assert.Empty(Css21FeatureManifest.Validate());
        Assert.NotEmpty(Css21FeatureManifest.Supported);
        Assert.NotEmpty(Css21FeatureManifest.Deferred);

        var report = Css21FeatureManifest.BuildReport();
        Assert.Contains("Supported", report);
        Assert.Contains("Deferred", report);
        Assert.Empty(Css21FeatureManifest.Supported.Select(entry => entry.Id)
            .Intersect(Css21FeatureManifest.Deferred.Select(entry => entry.Id), StringComparer.Ordinal));
        Assert.All(Css21FeatureManifest.Entries,
            entry => Assert.Contains(entry.Id, report));
        Assert.All(Css21ConformanceFixtureCatalog.All,
            fixture => Assert.Contains(fixture.FeatureId,
                Css21FeatureManifest.Supported.Select(entry => entry.Id)));
    }

    [Fact]
    public void FeatureManifestMatchesDocumentationSupportedAndDeferredIds()
    {
        var documentation = File.ReadAllLines(FindRepositoryRootFile("docs", "CSS-Support.md"));
        var supported = ReadManifestIds(documentation, "CSS21-MANIFEST-SUPPORTED");
        var deferred = ReadManifestIds(documentation, "CSS21-MANIFEST-DEFERRED");
        var css22Supported = ReadManifestIds(documentation, "CSS22-MANIFEST-SUPPORTED");
        var css22Deferred = ReadManifestIds(documentation, "CSS22-MANIFEST-DEFERRED");

        Assert.Equal(Css21FeatureManifest.Css21Supported.Select(entry => entry.Id), supported);
        Assert.Equal(Css21FeatureManifest.Css21Deferred.Select(entry => entry.Id), deferred);
        Assert.Equal(Css21FeatureManifest.Css22Supported.Select(entry => entry.Id), css22Supported);
        Assert.Equal(Css21FeatureManifest.Css22Deferred.Select(entry => entry.Id), css22Deferred);
    }

    [Fact]
    public void FontFaceFixtureCoversDescriptorParsingAndPortableLocalLoadApi()
    {
        var fixture = Assert.Single(Css21ConformanceFixtureCatalog.All,
            fixture => fixture.Id == "CSS21-AT-FONT-FACE-001");
        var sheet = Parse(fixture.Css);
        var engine = new CssEngine();

        engine.LoadStyleSheet(sheet);

        var descriptor = Assert.Single(engine.FontFaceDescriptors);
        Assert.Equal("FixtureLocal", descriptor.Family);
        Assert.Equal("fixture.ttf", descriptor.Source);
        Assert.Equal(Square.Graphics.FontWeight.SemiBold, descriptor.Weight);
        Assert.Equal(Square.Graphics.FontStyle.Italic, descriptor.Style);
        Assert.True(descriptor.IsLocal);
        Assert.Empty(engine.Fonts);
    }

    [Fact]
    public void BidiFixtureMapsCssDirectionAndUnicodeBidiToBasicRunLayout()
    {
        var fixture = Assert.Single(Css21ConformanceFixtureCatalog.All,
            fixture => fixture.Id == "CSS21-BIDI-001");
        var document = Css21FixtureRunner.Apply(fixture);
        var target = Assert.IsType<Square.Controls.Text>(document.Get("target"));
        var direction = target.Style.Get("direction");
        var unicodeBidi = target.Style.Get("unicode-bidi");
        var layout = BidiText.Layout(
            target.TextContent,
            new BidiTextOptions(
                direction == "rtl" ? BidiDirection.Rtl : BidiDirection.Ltr,
                unicodeBidi switch
                {
                    "embed" => BidiTextMode.Embed,
                    "bidi-override" => BidiTextMode.BidiOverride,
                    _ => BidiTextMode.Normal
                }));

        Assert.Equal(BidiDirection.Ltr, layout.BaseDirection);
        Assert.Single(layout.VisualRuns);
        Assert.Equal(BidiDirection.Ltr, layout.VisualRuns[0].Direction);
    }

    private static string[] ReadManifestIds(IEnumerable<string> lines, string marker)
    {
        var line = Assert.Single(lines, line => line.StartsWith($"<!-- {marker}:", StringComparison.Ordinal));
        var value = line[(line.IndexOf(':') + 1)..].Trim();
        Assert.EndsWith("-->", value);
        return value[..^3].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string FindRepositoryRootFile(params string[] path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine([directory.FullName, ..path]);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository documentation from the test output directory.");
    }

    private static Square.CSS.Ast.CssStyleSheet Parse(string css) =>
        new CssParser(new CssTokenizer(css).Tokenize()).Parse();

    private static void AssertRectClose(Rect expected, Rect actual, string message)
    {
        Assert.True(MathF.Abs(expected.X - actual.X) <= 0.01f, $"{message}: expected {expected}, actual {actual}");
        Assert.True(MathF.Abs(expected.Y - actual.Y) <= 0.01f, $"{message}: expected {expected}, actual {actual}");
        Assert.True(MathF.Abs(expected.Width - actual.Width) <= 0.01f, $"{message}: expected {expected}, actual {actual}");
        Assert.True(MathF.Abs(expected.Height - actual.Height) <= 0.01f, $"{message}: expected {expected}, actual {actual}");
    }
}
