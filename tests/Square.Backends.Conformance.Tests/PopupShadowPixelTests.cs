using Square.Backends;
using Square.Backends.Skia;
using Square.Controls;
using Square.Graphics;
using Xunit;

namespace Square.Backends.Conformance.Tests;

public sealed class PopupShadowPixelTests
{
    private const int X = 12, Y = 12, Width = 64, Height = 40, OffsetX = 80, OffsetY = 6;

    [Theory]
    [InlineData(false, "0")]
    [InlineData(true, "0")]
    [InlineData(false, "24px 0 16px 5px / 12px 0 8px 9px")]
    [InlineData(true, "24px 0 16px 5px / 12px 0 8px 9px")]
    [InlineData(false, "24px / 8px")]
    [InlineData(true, "24px / 8px")]
    public void OffsetHardShadowMatchesPopupSilhouette(bool skia, string radii)
    {
        using var bitmap = RenderPopup(skia, radii, "0 0");
        Assert.Equal(255, bitmap.GetPixel(X + Width / 2, Y + Height / 2)[3]);
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var surface = bitmap.GetPixel(X + x, Y + y)[3];
            var shadow = bitmap.GetPixel(X + x + OffsetX, Y + y + OffsetY)[3];
            Assert.InRange(Math.Abs(surface - shadow), 0, 1);
        }
    }

    [Theory]
    [InlineData(false, "0 4px", 4)]
    [InlineData(true, "0 4px", 4)]
    [InlineData(false, "5px 4px", 4)]
    [InlineData(true, "5px 4px", 4)]
    [InlineData(false, "0 -3px", -3)]
    [InlineData(true, "0 -3px", -3)]
    public void SpreadAndBlurKeepRoundedAndSquarePopupCornersDistinct(bool skia, string blurSpread, int spread)
    {
        using var bitmap = RenderPopup(skia, "32px 0 0 0 / 24px 0 0 0", blurSpread);
        var left = X + OffsetX - spread;
        var right = X + OffsetX + Width + spread;
        var top = Y + OffsetY - spread;
        var rounded = bitmap.GetPixel(left + 2, top + 2)[3];
        var square = bitmap.GetPixel(right - 3, top + 2)[3];
        Assert.True(square > rounded + 50, $"rounded={rounded}, square={square}");
        Assert.Equal(0, bitmap.GetPixel(0, 0)[3]);
    }

    private static Bitmap RenderPopup(bool skia, string radii, string blurSpread)
    {
        IRenderBackendFactory factory = skia ? new SkiaBackendFactory() : new RenderBackendFactory();
        using var context = factory.CreateContext(new RenderContextCreateInfo { CanvasSize = new Size(180, 80) });
        context.Clear(Color.Transparent);
        var popup = new Popup
        {
            Anchor = new View { Geometry = new Rect(X, Y - 4, Width, 4) },
            Geometry = new Rect(0, 0, Width, Height),
            VerticalOffset = 0
        };
        popup.Style.Set("background", "#ffffff");
        popup.Style.Set("border-radius", radii);
        popup.Style.Set("box-shadow", $"{OffsetX}px {OffsetY}px {blurSpread} #000000");
        popup.Open();
        popup.PaintPopup(context);
        return ((IRenderBitmapSource)context).CaptureBitmap();
    }
}
