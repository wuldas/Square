using System.Numerics;
using System.Reflection;
using Square.Backends;
using Square.Graphics;
using Xunit;

namespace Square.Backends.Tests;

public sealed class SoftwareBlendSimdTests
{
    private delegate void BlendRow(Span<byte> row, byte blue, byte green, byte red, byte alpha);
    private static readonly BlendRow Blend = typeof(RenderContext)
        .GetMethod("BlendSolidRow", BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<BlendRow>();

    [Fact]
    public void VectorBlocksMatchFrozenScalarRoundingForEveryAlphaAlignmentAndTail()
    {
        var vectorPixels = Vector<byte>.Count / 4;
        int[] lengths = [0, 1, vectorPixels - 1, vectorPixels, vectorPixels + 1, 2 * vectorPixels + 3, 4 * vectorPixels + 1];
        foreach (var pixels in lengths)
        foreach (var alignment in new[] { 0, 1, 3, 7, 15 })
        foreach (var mixed in new[] { false, true })
        for (var alpha = 0; alpha <= 255; alpha++)
        {
            var actual = Enumerable.Repeat((byte)0xA7, alignment + pixels * 4 + 19).ToArray();
            for (var pixel = 0; pixel < pixels; pixel++)
            {
                var offset = alignment + pixel * 4;
                actual[offset] = (byte)(pixel * 37 + alpha);
                actual[offset + 1] = (byte)(pixel * 71 + 29);
                actual[offset + 2] = (byte)(pixel * 13 + 211);
                actual[offset + 3] = mixed && (pixel % (vectorPixels * 2) == 0 || pixel == pixels - 1)
                    ? new byte[] { 0, 1, 63, 128, 254 }[pixel % 5] : (byte)255;
            }
            var expected = actual.ToArray();
            var blue = (byte)(alpha * 13 + 7);
            var green = (byte)(alpha * 29 + 71);
            var red = (byte)(alpha * 37 + 137);
            Reference(expected.AsSpan(alignment, pixels * 4), blue, green, red, (byte)alpha);
            Blend(actual.AsSpan(alignment, pixels * 4), blue, green, red, (byte)alpha);
            Assert.Equal(expected, actual); // Includes unaligned prefix and row/tail canaries.
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SolidRowsInsideLayerMatchOnePixelScalarDrawsAndPreserveOrder(bool opaqueLayerBase)
    {
        using var actual = new RenderContext(new Bitmap(65, 9), 1f);
        using var expected = new RenderContext(new Bitmap(65, 9), 1f);
        foreach (var context in new[] { actual, expected })
        {
            context.Clear(new Color(17, 47, 113, 255));
            context.PushLayer(new Rect(2, 1, 61, 7), .47f);
        }
        (Rect Rect, Color Color)[] draws =
        [
            (new Rect(2, 1, 61, 7), new Color(197, 73, 29, opaqueLayerBase ? (byte)255 : (byte)63)),
            (new Rect(2, 1, 61, 7), new Color(31, 139, 227, 128)),
            (new Rect(3, 2, 29, 4), new Color(223, 151, 17, 1)),
            (new Rect(4, 1, 50, 7), new Color(113, 43, 191, 254))
        ];
        foreach (var draw in draws)
        {
            var brush = new SolidColorBrush(draw.Color);
            actual.FillRect(draw.Rect, brush);
            for (var y = (int)draw.Rect.Top; y < draw.Rect.Bottom; y++)
            for (var x = (int)draw.Rect.Left; x < draw.Rect.Right; x++)
                expected.FillRect(new Rect(x, y, 1, 1), brush); // Rows shorter than one vector stay scalar.
            Assert.Equal(expected.GetBitmap().Pixels, actual.GetBitmap().Pixels);
        }
        actual.PopLayer(); expected.PopLayer();
        Assert.Equal(expected.GetBitmap().Pixels, actual.GetBitmap().Pixels);
    }

    private static void Reference(Span<byte> row, byte blue, byte green, byte red, byte alpha)
    {
        // Frozen pre-SIMD BlendSolidRow arithmetic: opaque destinations round once;
        // nonopaque destinations retain the historical scalar truncation unchanged.
        var inverse = 255 - alpha;
        for (var pixel = 0; pixel < row.Length; pixel += 4)
        {
            var destinationAlpha = row[pixel + 3];
            if (destinationAlpha == 255)
            {
                row[pixel] = (byte)((blue * alpha + row[pixel] * inverse + 127) / 255);
                row[pixel + 1] = (byte)((green * alpha + row[pixel + 1] * inverse + 127) / 255);
                row[pixel + 2] = (byte)((red * alpha + row[pixel + 2] * inverse + 127) / 255);
                continue;
            }
            var outputAlpha = alpha + destinationAlpha * inverse / 255;
            if (outputAlpha == 0) continue;
            row[pixel] = (byte)((blue * alpha + row[pixel] * destinationAlpha * inverse / 255) / outputAlpha);
            row[pixel + 1] = (byte)((green * alpha + row[pixel + 1] * destinationAlpha * inverse / 255) / outputAlpha);
            row[pixel + 2] = (byte)((red * alpha + row[pixel + 2] * destinationAlpha * inverse / 255) / outputAlpha);
            row[pixel + 3] = (byte)outputAlpha;
        }
    }
}
