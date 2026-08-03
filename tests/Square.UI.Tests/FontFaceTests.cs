using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Square.Text.Fonts;
using Square.Graphics;
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
