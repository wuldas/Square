using Square.CSS.Engine;
using Square.CSS.Tokenizer;
using Square.Text.Fonts;
using Xunit;

namespace Square.CSS.Tests;

public sealed class FontFaceCssTests
{
    [Fact]
    public void ParserPreservesFontFaceDeclarationsAndNormalRules()
    {
        var sheet = Parse("@font-face { font-family: 'Parser Face'; src: url(fonts/parser.ttf); font-weight: 700; font-style: italic; } Button { color: blue; }");

        var atRule = Assert.Single(sheet.AtRules);
        Assert.Equal("font-face", atRule.Name);
        Assert.Collection(atRule.Declarations,
            declaration => Assert.Equal(("font-family", "\"Parser Face\""), (declaration.Property, declaration.Value)),
            declaration => Assert.Equal(("src", "url(fonts/parser.ttf)"), (declaration.Property, declaration.Value)),
            declaration => Assert.Equal(("font-weight", "700"), (declaration.Property, declaration.Value)),
            declaration => Assert.Equal(("font-style", "italic"), (declaration.Property, declaration.Value)));
        Assert.Single(sheet.Rules);
        Assert.Equal("blue", sheet.Rules[0].Declarations[0].Value);
    }

    [Fact]
    public void EngineTracksDescriptorsAndDoesNotLoadRemoteSources()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(Parse("@font-face { font-family: RemoteFace; src: url(https://example.test/font.ttf); font-weight: bold; font-style: oblique; }"));

        var descriptor = Assert.Single(engine.FontFaceDescriptors);
        Assert.Equal("RemoteFace", descriptor.Family);
        Assert.Equal("https://example.test/font.ttf", descriptor.Source);
        Assert.Equal(Square.Graphics.FontWeight.Bold, descriptor.Weight);
        Assert.Equal(Square.Graphics.FontStyle.Oblique, descriptor.Style);
        Assert.False(descriptor.IsLocal);
        Assert.Empty(engine.Fonts);
    }

    [Fact]
    public async Task EngineLoadsRelativeLocalFontWhenBaseDirectoryIsSupplied()
    {
        var sourcePath = FindSystemFontPath();
        if (sourcePath is null) return;

        var directory = Path.Combine(Path.GetTempPath(), "square-font-face-tests");
        Directory.CreateDirectory(directory);
        var fontPath = Path.Combine(directory, "test-font.ttf");
        File.Copy(sourcePath, fontPath, overwrite: true);
        try
        {
            var engine = new CssEngine();
            engine.LoadStyleSheet(Parse("@font-face { font-family: EngineLocalFace; src: url(test-font.ttf); font-weight: 600; font-style: italic; }"));

            await engine.LoadFontsAsync(directory);

            var face = Assert.Single(engine.Fonts);
            Assert.Equal("EngineLocalFace", face.Family);
            Assert.Equal(Square.Graphics.FontWeight.SemiBold, face.Weight);
            Assert.Equal(Square.Graphics.FontStyle.Italic, face.Style);
            Assert.Equal(FontFaceLoadStatus.Loaded, face.Status);
            Assert.True(Square.Text.FontManager.Instance.IsFamilyKnown("EngineLocalFace"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task EngineRetainsMultipleFacesAndMatchesRequestedWeightAndStyle()
    {
        var sourcePath = FindSystemFontPath();
        if (sourcePath is null) return;

        var directory = Path.Combine(Path.GetTempPath(), "square-font-face-multiple-tests");
        Directory.CreateDirectory(directory);
        var fontPath = Path.Combine(directory, "test-font" + Path.GetExtension(sourcePath));
        File.Copy(sourcePath, fontPath, overwrite: true);
        try
        {
            const string family = "EngineMultipleFace";
            var engine = new CssEngine();
            engine.LoadStyleSheet(Parse(
                $"@font-face {{ font-family: {family}; src: url({Path.GetFileName(fontPath)}); }} " +
                $"@font-face {{ font-family: {family}; src: url({Path.GetFileName(fontPath)}); font-weight: bold; font-style: italic; }}"));

            await engine.LoadFontsAsync(directory);

            Assert.Equal(2, engine.Fonts.Count);
            var normal = engine.Fonts.Match(family, Square.Graphics.FontWeight.Normal, Square.Graphics.FontStyle.Normal);
            var boldItalic = engine.Fonts.Match(family, Square.Graphics.FontWeight.Bold, Square.Graphics.FontStyle.Italic);
            Assert.NotNull(normal);
            Assert.NotNull(boldItalic);
            Assert.Equal(Square.Graphics.FontWeight.Normal, normal!.Weight);
            Assert.Equal(Square.Graphics.FontStyle.Normal, normal.Style);
            Assert.Equal(Square.Graphics.FontWeight.Bold, boldItalic!.Weight);
            Assert.Equal(Square.Graphics.FontStyle.Italic, boldItalic.Style);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FontFaceDescriptorsDefaultToNormalAndParseCssKeywords()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(Parse("""
            @font-face { font-family: DefaultFace; src: url(default.ttf); }
            @font-face { font-family: BoldFace; src: url(bold.ttf); font-weight: bold; }
            @font-face { font-family: ItalicFace; src: url(italic.ttf); font-style: italic; }
            @font-face { font-family: ObliqueFace; src: url(oblique.ttf); font-style: oblique; }
            """));

        Assert.Collection(engine.FontFaceDescriptors,
            descriptor =>
            {
                Assert.Equal(Square.Graphics.FontWeight.Normal, descriptor.Weight);
                Assert.Equal(Square.Graphics.FontStyle.Normal, descriptor.Style);
            },
            descriptor =>
            {
                Assert.Equal(Square.Graphics.FontWeight.Bold, descriptor.Weight);
                Assert.Equal(Square.Graphics.FontStyle.Normal, descriptor.Style);
            },
            descriptor =>
            {
                Assert.Equal(Square.Graphics.FontWeight.Normal, descriptor.Weight);
                Assert.Equal(Square.Graphics.FontStyle.Italic, descriptor.Style);
            },
            descriptor =>
            {
                Assert.Equal(Square.Graphics.FontWeight.Normal, descriptor.Weight);
                Assert.Equal(Square.Graphics.FontStyle.Oblique, descriptor.Style);
            });
    }

    [Fact]
    public void MalformedFontFaceIsIgnoredWithoutDroppingNormalRules()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(Parse("@font-face { src: url(missing-family.ttf); font-weight: nope; } Button { color: green; }"));

        Assert.Empty(engine.FontFaceDescriptors);
        Assert.Empty(engine.Fonts);
        Assert.Single(Parse("@font-face { src: url(missing-family.ttf); font-weight: nope; } Button { color: green; }").Rules);
    }

    private static Square.CSS.Ast.CssStyleSheet Parse(string css) =>
        new CssParser(new CssTokenizer(css).Tokenize()).Parse();

    private static string? FindSystemFontPath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "arial.ttf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "segoeui.ttf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "calibri.ttf"),
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/TTF/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf",
            "/System/Library/Fonts/Supplemental/Arial.ttf",
            "/System/Library/Fonts/Helvetica.ttc",
            "/Library/Fonts/Arial.ttf"
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
