using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Square.Text.Fonts;
using Square.Graphics;
using Square.Resources;
using Square.Text.Glyph;
using Square.UI;
using Xunit;

namespace Square.UI.Tests;

public class FontFaceTests
{
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
            "/Library/Fonts/Arial.ttf",
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    [Fact]
    public async Task FontFaceLoadsFromLocalPathAndRegistersFamily()
    {
        var path = FindSystemFontPath();
        if (path is null)
            return; // CI 无系统字体时跳过

        var face = new FontFace("SquareTestFace", path);
        Assert.Equal(FontFaceLoadStatus.Unloaded, face.Status);

        await face.LoadAsync();

        Assert.Equal(FontFaceLoadStatus.Loaded, face.Status);
        Assert.NotNull(face.Data);
        Assert.True(face.Data!.Count > 0);

        var set = new FontFaceSet();
        set.Add(face);
        Assert.True(set.Check("16px SquareTestFace"));
        Assert.True(Square.Text.FontManager.Instance.IsFamilyKnown("SquareTestFace"));
    }

    [Fact]
    public async Task FontFaceSetLoadAsyncLoadsMatchingFaces()
    {
        var path = FindSystemFontPath();
        if (path is null)
            return;

        var set = new FontFaceSet();
        var face = new FontFace("SquareSetFace", path);
        set.Add(face);

        await set.LoadAsync("16px SquareSetFace");

        Assert.Equal(FontFaceLoadStatus.Loaded, face.Status);
        Assert.True(set.Check("SquareSetFace"));
    }

    [Fact]
    public async Task FontFaceSetRetainsAndMatchesMultipleFacesFromOneFile()
    {
        var path = FindSystemFontPath();
        if (path is null)
            return;

        const string family = "SquareMultipleFace";
        var normal = new FontFace(family, path, FontWeight.Normal, FontStyle.Normal);
        var boldOblique = new FontFace(family, path, FontWeight.Bold, FontStyle.Oblique);
        var set = new FontFaceSet();
        set.Add(normal);
        set.Add(boldOblique);

        await normal.LoadAsync();
        await boldOblique.LoadAsync();

        Assert.Equal(2, set.Count);
        Assert.True(set.Check("bold oblique 16px SquareMultipleFace"));
        Assert.Same(normal, set.Match(family, FontWeight.Normal, FontStyle.Normal));
        Assert.Same(boldOblique, set.Match(family, FontWeight.Bold, FontStyle.Oblique));

        var requested = Square.Text.FontManager.Instance.FromCss(
            family,
            "16px",
            "bold",
            "oblique");
        Assert.Equal(family, requested.Family);
        Assert.Equal(FontWeight.Bold, requested.Weight);
        Assert.Equal(FontStyle.Oblique, requested.Style);
    }

    [Fact]
    public async Task FontFaceLoadFromBytes()
    {
        var path = FindSystemFontPath();
        if (path is null)
            return;

        var bytes = await File.ReadAllBytesAsync(path);
        var face = new FontFace("SquareBytesFace", bytes);
        await face.LoadAsync();
        Assert.Equal(FontFaceLoadStatus.Loaded, face.Status);
    }

    [Fact]
    public async Task FontFacePreservesWeightAndStyleDescriptors()
    {
        var path = FindSystemFontPath();
        if (path is null)
            return;

        var face = new FontFace("SquareDescriptorFace", path, FontWeight.Bold, FontStyle.Italic);
        await face.LoadAsync();

        Assert.Equal(FontWeight.Bold, face.Weight);
        Assert.Equal(FontStyle.Italic, face.Style);
        Assert.True(Square.Text.FontManager.Instance.IsFamilyKnown("SquareDescriptorFace"));
    }

    [Fact]
    public async Task CustomFontUsesLoadedGlyphsAndMetricsAfterReplacingFace()
    {
        const string family = "SquareCustomGlyphMetrics";
        var bytes = ApplicationResource.ReadAllBytes("iconfont/iconfont.ttf", typeof(UIDocument).Assembly);
        await new FontFace(family, bytes).LoadAsync();
        var font = new Font(family, 32);
        var rasterizer = new SystemGlyphRasterizer();
        var provider = new Square.Text.Glyph.SystemTextMetricsProvider(rasterizer);
        var glyph = Assert.IsType<RasterizedGlyph>(rasterizer.Rasterize(font, '\ue669'));

        // The embedded icon face uses a 1024-unit em, 1024-unit advances,
        // 896-unit ascent and -128-unit descent, and has no Latin glyphs.
        Assert.Equal(32f, glyph.AdvanceX);
        Assert.Null(rasterizer.Rasterize(font, 'A'));
        Assert.True(provider.TryGetFontMetrics(font, out var metrics));
        Assert.Equal(-28f, metrics.Ascent);
        Assert.Equal(4f, metrics.Descent);
        var reference = Assert.IsType<RasterizedGlyph>(new StbGlyphRasterizer().Rasterize(font, '\ue669'));
        Assert.Equal(reference.Coverage, glyph.Coverage);

        // Replacing the same face must replace cached glyphs as well as metrics.
        var replacement = (byte[])bytes.Clone();
        var tableCount = BinaryPrimitives.ReadUInt16BigEndian(replacement.AsSpan(4));
        for (var i = 0; i < tableCount; i++)
        {
            var record = 12 + i * 16;
            if (BinaryPrimitives.ReadUInt32BigEndian(replacement.AsSpan(record)) != 0x68656164) continue; // head
            var offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(replacement.AsSpan(record + 8)));
            BinaryPrimitives.WriteUInt16BigEndian(replacement.AsSpan(offset + 18), 2048);
            break;
        }
        await new FontFace(family, replacement).LoadAsync();

        var replacedGlyph = Assert.IsType<RasterizedGlyph>(rasterizer.Rasterize(font, '\ue669'));
        Assert.Equal(16f, replacedGlyph.AdvanceX);
        Assert.True(provider.TryGetFontMetrics(font, out metrics));
        Assert.Equal(-14f, metrics.Ascent);
        Assert.Equal(2f, metrics.Descent);
    }

    [Fact]
    public void DocumentFontsIsSharedDefaultSet()
    {
        var doc = new UIDocument();
        Assert.Same(FontFaceSet.Default, doc.Fonts);

        var face = new FontFace("DocFontMeta", "about:blank");
        // 不 load，仅验证 add 到 document.fonts
        doc.Fonts.Add(face);
        Assert.True(doc.Fonts.Contains(face));
        doc.Fonts.Delete(face);
    }

    [Fact]
    public void ParseFamilyFromFontShorthand()
    {
        Assert.Equal("MyFont", FontFaceSet.ParseFamilyFromFont("16px MyFont"));
        Assert.Equal("MyFont", FontFaceSet.ParseFamilyFromFont("MyFont"));
        Assert.Equal("Segoe UI", FontFaceSet.ParseFamilyFromFont("14px \"Segoe UI\""));
    }

    [Fact]
    public async Task FontFaceMissingFileSetsErrorStatus()
    {
        var face = new FontFace("Missing", Path.Combine(Path.GetTempPath(), "no-such-font-square-xyz.ttf"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => face.LoadAsync());
        Assert.Equal(FontFaceLoadStatus.Error, face.Status);
        Assert.False(string.IsNullOrEmpty(face.ErrorMessage));
    }
}
