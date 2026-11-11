using System.Reflection;
using Square.Backends;
using Square.Graphics;
using Xunit;

namespace Square.Backends.Tests;

public sealed class SoftwareBitmapAlphaTests
{
    private static readonly FieldInfo OpacityField = typeof(RenderContext).GetField("_currentOpacity", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static IEnumerable<object[]> AlphaCases()
    {
        foreach (byte sourceAlpha in new byte[] { 0, 1, 128, 254, 255 })
        foreach (byte destinationAlpha in new byte[] { 0, 1, 2, 63, 128, 254, 255 })
        foreach (float opacity in new[] { 1f, .5f, .125f })
        for (var mode = 0; mode < 4; mode++)
            yield return [sourceAlpha, destinationAlpha, opacity, mode];
    }

    [Theory]
    [MemberData(nameof(AlphaCases))]
    public void StraightBitmapSourceOverMatchesIndependentReferenceAcrossAlphaOpacityScaleAndClip(
        byte sourceAlpha, byte destinationAlpha, float opacity, int mode)
    {
        var sourceColor = new Color(32, 128, 224, sourceAlpha);
        var destinationColor = new Color(61, 103, 149, destinationAlpha);
        using var source = new Bitmap(4, 4);
        Seed(source, sourceColor);
        var originalSource = source.Pixels.ToArray();
        using var destination = new Bitmap(10, 10);
        Seed(destination, destinationColor); // Explicit straight bytes, independent of Clear's packing.
        using var context = new RenderContext(destination, 1f);
        OpacityField.SetValue(context, opacity);
        var extent = mode is 1 or 3 ? 8 : 4;
        var clipped = mode >= 2;
        if (clipped)
            context.PushClip(new PathGeometry().MoveTo(new Point(1, 1))
                .LineTo(new Point(1 + extent, 1)).LineTo(new Point(1, 1 + extent)).Close());
        context.DrawImage(source, new Rect(1, 1, extent, extent));
        if (clipped) context.PopClip();

        for (var y = 0; y < destination.Height; y++)
        for (var x = 0; x < destination.Width; x++)
        {
            var visible = x >= 1 && x < 1 + extent && y >= 1 && y < 1 + extent
                && (!clipped || x + y < extent + 1);
            var actual = destination.GetPixel(x, y);
            if (!visible || MathF.Round(sourceAlpha * opacity) == 0)
                Assert.Equal(Bgra(destinationColor), actual.ToArray());
            else
                AssertNear(Reference(sourceColor, destinationColor, opacity), actual);
        }
        Assert.Equal(originalSource, source.Pixels); // Public Bitmap bytes stay straight and unmodified.
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void HalfTransparentBlueOverV11BackgroundMatchesDeclaredProbe(int mode)
    {
        using var source = new Bitmap(4, 4);
        Seed(source, new Color(32, 128, 224, 128));
        using var context = new RenderContext(new Bitmap(10, 10), 1f);
        context.Clear(new Color(16, 24, 39, 255));
        var extent = mode is 1 or 3 ? 8 : 4;
        if (mode >= 2)
            context.PushClip(new PathGeometry().MoveTo(new Point(1, 1))
                .LineTo(new Point(1 + extent, 1)).LineTo(new Point(1, 1 + extent)).Close());
        context.DrawImage(source, new Rect(1, 1, extent, extent));
        // Predeclared V11 alpha probe: RGBA approximately (24,76,132,255), tolerance 2.
        AssertNear([132, 76, 24, 255], context.GetBitmap().GetPixel(2, 2));
    }

    [Fact]
    public void RepeatedBitmapDrawsApplySourceOverInOrderInsideOpacityLayer()
    {
        using var source = new Bitmap(4, 4);
        var sourceColor = new Color(32, 128, 224, 128);
        Seed(source, sourceColor);
        using var context = new RenderContext(new Bitmap(6, 6), 1f);
        context.Clear(new Color(16, 24, 39, 255));
        context.PushLayer(new Rect(1, 1, 4, 4), .5f);
        context.DrawImage(source, new Rect(1, 1, 4, 4));
        context.DrawImage(source, new Rect(1, 1, 4, 4));
        context.PopLayer();
        var groupAlpha = (byte)(128 + 128 * 127 / 255);
        AssertNear(Reference(new Color(32, 128, 224, groupAlpha), new Color(16, 24, 39, 255), .5f),
            context.GetBitmap().GetPixel(2, 2));
        Assert.Equal(new byte[] { 39, 24, 16, 255 }, context.GetBitmap().GetPixel(0, 0).ToArray());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(2, 63)]
    [InlineData(128, 2)]
    public void LowAlphaLayerAndSolidLeafNormalizeBeforeQuantizingOutputAlpha(byte sourceAlpha, byte destinationAlpha)
    {
        var sourceColor = new Color(32, 128, 224, sourceAlpha);
        var destinationColor = new Color(61, 103, 149, destinationAlpha);
        using var source = new Bitmap(1, 1);
        Seed(source, sourceColor);
        using var layerBitmap = new Bitmap(1, 1);
        using var leafBitmap = new Bitmap(1, 1);
        Seed(layerBitmap, destinationColor);
        Seed(leafBitmap, destinationColor);
        using var layer = new RenderContext(layerBitmap, 1f);
        using var leaf = new RenderContext(leafBitmap, 1f);
        layer.PushLayer(new Rect(0, 0, 1, 1), 1);
        layer.DrawImage(source, new Rect(0, 0, 1, 1));
        layer.PopLayer();
        Assert.True(leaf.TryFillSolidLeafOpacity(new Rect(0, 0, 1, 1), sourceColor, 1));
        AssertNear(Reference(sourceColor, destinationColor, 1), layerBitmap.GetPixel(0, 0));
        Assert.Equal(layerBitmap.Pixels, leafBitmap.Pixels);
    }

    [Fact]
    public void RepeatedWhiteAlphaOneDrawsAccumulateWithoutWrappingStraightChannels()
    {
        using var source = new Bitmap(1, 1);
        Seed(source, new Color(255, 255, 255, 1));
        using var destination = new Bitmap(1, 1);
        Seed(destination, new Color(255, 255, 255, 1));
        using var context = new RenderContext(destination, 1f);
        for (byte draw = 1; draw <= 8; draw++)
        {
            context.DrawImage(source, new Rect(0, 0, 1, 1));
            Assert.Equal(new byte[] { 255, 255, 255, (byte)(draw + 1) }, destination.GetPixel(0, 0).ToArray());
        }
    }

    private static byte[] Reference(Color source, Color destination, float opacity)
    {
        // Independent normalized Porter-Duff straight-alpha reference, rather than the integer production formula.
        var sa = Math.Round(source.A * opacity, MidpointRounding.ToEven) / 255d;
        var da = destination.A / 255d;
        var outputAlpha = sa + da * (1 - sa);
        byte Channel(byte s, byte d) => (byte)Math.Clamp((int)Math.Round((s * sa + d * da * (1 - sa)) / outputAlpha), 0, 255);
        return [Channel(source.B, destination.B), Channel(source.G, destination.G), Channel(source.R, destination.R), (byte)Math.Round(outputAlpha * 255)];
    }

    private static void AssertNear(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        Assert.Equal(expected[3], actual[3]);
        for (var channel = 0; channel < 3; channel++)
            Assert.InRange((int)actual[channel], expected[channel] - 2, expected[channel] + 2);
    }

    private static byte[] Bgra(Color color) => [color.B, color.G, color.R, color.A];
    private static void Seed(Bitmap bitmap, Color color)
    {
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            Bgra(color).CopyTo(bitmap.GetPixel(x, y));
    }
}
