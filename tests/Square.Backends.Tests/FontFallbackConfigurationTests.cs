using System;
using Square.Graphics;
using Square.Text;
using Square.Text.Glyph;
using Xunit;

namespace Square.Backends.Tests;

[CollectionDefinition("Process font fallback", DisableParallelization = true)]
public sealed class ProcessFontFallbackCollection;

[Collection("Process font fallback")]
public sealed class FontFallbackConfigurationTests
{
    [Fact]
    public void Win32CssPreservesAvailableFamiliesInAuthoredOrder()
    {
        if (!OperatingSystem.IsWindows()) return;
        var manager = FontManager.Instance;
        var font = manager.FromCss("'Square Missing Face', Georgia, 'Courier New', serif, 'Segoe UI Symbol'", "24px");
        Assert.Equal("Georgia", font.Family);
        Assert.Equal(["Courier New", "Times New Roman", "Segoe UI Symbol"], font.FallbackFamilies);
        Assert.Same(font.FallbackFamilies, font.WithSize(36).FallbackFamilies);
        Assert.Same(font.FallbackFamilies, font.WithWeight(FontWeight.Bold).FallbackFamilies);
        Assert.Same(font.FallbackFamilies, font.WithStyle(FontStyle.Italic).FallbackFamilies);
        Assert.Equal("Consolas", manager.FromCss("Courier New, sans-serif", null).Family);
        Assert.Equal("Courier New", manager.FromCss("'Courier New', sans-serif", null).Family);
        Assert.Equal("Georgia", manager.FromCss("'serif', Georgia, sans-serif", null).Family);
    }

    [Fact]
    public void Win32LocalFallbackPrecedesProcessFallbackAndPreservesPrimaryGlyphs()
    {
        if (!OperatingSystem.IsWindows()) return;
        const string missing = "Square Missing Face";
        var manager = FontManager.Instance;
        var rasterizer = new SystemGlyphRasterizer();
        var oldFamilies = manager.DefaultFallbackFamilies;
        var configured = new[] { "Times New Roman" };
        var times = rasterizer.Rasterize(new Font("Times New Roman", 24), 'W');
        var courier = rasterizer.Rasterize(new Font("Courier New", 24), 'W');
        Assert.NotNull(times);
        Assert.NotNull(courier);
        Assert.False(times.Coverage.AsSpan().SequenceEqual(courier.Coverage));

        try
        {
            manager.DefaultFallbackFamilies = configured;
            configured[0] = "Courier New";
            Assert.Equal(times.Coverage, rasterizer.Rasterize(new Font(missing, 24), 'W')?.Coverage);

            var local = new Font(missing, 24) { FallbackFamilies = ["Square Also Missing", "Courier New"] };
            Assert.Equal(courier.Coverage, rasterizer.Rasterize(local, 'W')?.Coverage);

            var primary = new Font("Times New Roman", 24) { FallbackFamilies = ["Courier New"] };
            Assert.Equal(times.Coverage, rasterizer.Rasterize(primary, 'W')?.Coverage);
        }
        finally
        {
            manager.DefaultFallbackFamilies = oldFamilies;
        }
    }
}
