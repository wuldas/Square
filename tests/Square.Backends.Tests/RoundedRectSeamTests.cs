using Square.Backends;
using Square.Graphics;
using Xunit;

namespace Square.Backends.Tests;

public class RoundedRectSeamTests
{
    [Fact]
    public void TranslucentRoundedRectDoesNotDoubleBlendAtCornerJoins()
    {
        var bitmap = new Bitmap(48, 40);
        using var context = new RenderContext(bitmap, 1f);
        context.Clear(Color.Transparent);
        context.FillGeometry(
            new RoundedRectGeometry(new Rect(8, 8, 32, 24), 8, 8),
            new SolidColorBrush(Color.FromRgba(0, 0, 0, 128)));

        var bodyAlpha = AlphaAt(bitmap, 24, 12);
        Assert.Equal(128, bodyAlpha);
        Assert.Equal(bodyAlpha, AlphaAt(bitmap, 16, 10)); // top-left to horizontal edge
        Assert.Equal(bodyAlpha, AlphaAt(bitmap, 10, 16)); // top-left to vertical edge
        Assert.Equal(bodyAlpha, AlphaAt(bitmap, 37, 16)); // top-right to vertical edge
        Assert.Equal(bodyAlpha, AlphaAt(bitmap, 16, 29)); // bottom-left to horizontal edge

        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            Assert.True(AlphaAt(bitmap, x, y) <= bodyAlpha,
                $"Pixel ({x},{y}) was blended more than once: {AlphaAt(bitmap, x, y)} > {bodyAlpha}.");
    }

    [Fact]
    public void FractionalRoundedRectHasNoGapWhereVerticalEdgesMeetBottomCorners()
    {
        var bitmap = new Bitmap(48, 40);
        using var context = new RenderContext(bitmap, 1f);
        context.Clear(Color.Transparent);
        context.FillGeometry(
            new RoundedRectGeometry(new Rect(8.25f, 7.75f, 31.5f, 24.5f), 7.5f, 7.5f),
            new SolidColorBrush(Color.FromRgba(0, 0, 0, 128)));

        Assert.True(AlphaAt(bitmap, 9, 24) > 0);
        Assert.True(AlphaAt(bitmap, 38, 24) > 0);
        Assert.True(AlphaAt(bitmap, 10, 25) > 0);
        Assert.True(AlphaAt(bitmap, 37, 25) > 0);
    }

    [Theory]
    [InlineData(255)]
    [InlineData(128)]
    public void FractionalStrokeJoinsMatchStraightEdges(byte opacity)
    {
        using var bitmap = new Bitmap(90, 76);
        using var context = new RenderContext(bitmap, 1.5f);
        var ink = Color.FromRgba(53, 143, 120, opacity);
        context.Clear(Color.White);
        context.DrawGeometry(new RoundedRectGeometry(new Rect(9, 9, 42, 34), 8, 8), Pen.FromColor(ink, 2));

        var expectedRed = (byte)((ink.R * opacity + 255 * (255 - opacity) + 127) / 255);
        Assert.Equal(expectedRed, bitmap.GetPixel(40, 13)[2]);
        Assert.Equal(expectedRed, bitmap.GetPixel(13, 36)[2]);

        // The 3-device-pixel stroke has tangencies at x=25.5/64.5 and y=25.5/52.5.
        foreach (var y in new[] { 13, 64 })
        foreach (var x in new[] { 25, 64 })
            Assert.True(bitmap.GetPixel(x, y).SequenceEqual(bitmap.GetPixel(40, y)), $"Horizontal join ({x},{y})");
        foreach (var x in new[] { 13, 76 })
        foreach (var y in new[] { 25, 52 })
            Assert.True(bitmap.GetPixel(x, y).SequenceEqual(bitmap.GetPixel(x, 36)), $"Vertical join ({x},{y})");
    }

    [Fact]
    public void RoundedRectOutsideCanvasIsSafelyClipped()
    {
        var bitmap = new Bitmap(362, 40);
        using var context = new RenderContext(bitmap, 1f);
        context.Clear(Color.Transparent);

        context.FillGeometry(
            new RoundedRectGeometry(new Rect(364, 8, 36, 20), 2, 2),
            new SolidColorBrush(Color.FromRgba(0, 0, 0, 128)));

        Assert.All(bitmap.Pixels, value => Assert.Equal(0, value));
    }

    private static byte AlphaAt(Bitmap bitmap, int x, int y) => bitmap.Pixels[y * bitmap.Stride + x * 4 + 3];
}
