using Square.Backends;
using Square.CSS.Engine;
using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Xunit;

namespace Square.Backends.Tests;

public sealed class FormButtonEdgeTests
{
    private const float Dpi = 1.5f;
    private const float Face = 239f;
    private const float Border = 118f;
    private static readonly Rect Bounds = new(5, 4, 71, 22);

    [Fact]
    public void DefaultButtonBorderIsOneAndAHalfPixelsOnTopAndBottomAt150Dpi()
    {
        var button = new Button("Emit") { Geometry = Bounds };
        new CssEngine().ApplyStyles(button);

        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(120, 64),
            DpiScale = Dpi
        });
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(button);
        tree.Render(context);
        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();

        var centreX = (int)MathF.Round((Bounds.X + Bounds.Width / 2) * Dpi);
        var top = Coverage(bitmap, centreX, (int)MathF.Round(Bounds.Y * Dpi));
        var bottom = Coverage(bitmap, centreX, (int)MathF.Round(Bounds.Bottom * Dpi));

        Assert.InRange(top, 1.25f, 1.75f);
        Assert.InRange(bottom, 1.25f, 1.75f);
        Assert.True(Math.Abs(top - bottom) <= 0.25f, $"top={top:0.###}, bottom={bottom:0.###}");
    }

    /// <summary>在边缘行两侧各 2 行内累积边框覆盖率，结果即该边的 DIP 厚度。</summary>
    private static float Coverage(Bitmap bitmap, int x, int edgeRow)
    {
        var sum = 0f;
        for (var offset = -2; offset <= 2; offset++)
        {
            var y = Math.Clamp(edgeRow + offset, 0, bitmap.Height - 1);
            var pixel = bitmap.GetPixel(x, y);
            var luminance = pixel[2] * 0.299f + pixel[1] * 0.587f + pixel[0] * 0.114f;
            sum += Math.Clamp((Face - luminance) / (Face - Border), 0f, 1f);
        }

        return sum;
    }
}
