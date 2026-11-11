using System.Globalization;
using System.Numerics;
using Square.Backends.Vulkan;
using Square.Graphics;
using Square.Hosting;
using Square.Platform;
#if PLATFORM_WIN32
using Square.Platform.Win32;
#elif PLATFORM_X11
using Square.Platform.X11;
#endif
using Xunit;

namespace Square.Backends.Conformance.Tests;

[CollectionDefinition("RealVulkanCanvasFeatures", DisableParallelization = true)]
public sealed class RealVulkanCanvasFeatureCollection { }

[Collection("RealVulkanCanvasFeatures")]
public sealed class RealVulkanCanvasFeatureTests
{
    private static readonly Color Green = Color.FromRgb(0, 255, 0);
    private static readonly Color Yellow = Color.FromRgb(255, 255, 0);
    private static bool Enabled => Environment.GetEnvironmentVariable("SQUARE_RUN_REAL_VULKAN_GPU_CONFORMANCE") == "1";

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [Trait("Category", "RealVulkanGpu")]
    public void GradientsAndMutableBrushSnapshotsMatchActualGpuPixels(int samples)
    {
        if (!Enabled) return;
        using var environment = new GpuEnvironment(samples);
        using var canvas = new GpuCanvas(samples);
        var context = canvas.Context;
        var linear = Linear(new Point(8, 8), new Point(72, 8));
        using (var bitmap = canvas.Frame(() =>
        {
            context.FillRect(new Rect(8, 8, 64, 24), linear);
            for (var i = 0; i < 3; i++)
            {
                var left = 8 + i * 50;
                context.FillRect(new Rect(left, 48, 40, 40),
                    new RadialGradientBrush(new Point(left + 10, 68), 8,
                        new GradientStop(0, Color.Red), new GradientStop(1, Color.Blue))
                    { SpreadMethod = (GradientSpreadMethod)i });
            }
            context.FillRect(new Rect(8, 112, 32, 24), new LinearGradientBrush(new Point(8, 112), new Point(40, 112)));
            context.FillRect(new Rect(48, 112, 32, 24), new LinearGradientBrush(new Point(48, 112), new Point(80, 112),
                new GradientStop(.7f, new Color(20, 180, 80, 255))));
            context.FillRect(new Rect(88, 112, 64, 24), new LinearGradientBrush(new Point(88, 112), new Point(152, 112),
                new GradientStop(1, Color.Blue), new GradientStop(.5f, Green), new GradientStop(0, Color.Red), new GradientStop(.5f, Yellow)));
        }))
        {
            foreach (var x in new[] { 12, 32, 60 })
                canvas.AssertRedBlue(bitmap, x, 18, (canvas.Center(x, 18).X - 8) / 64);
            for (var i = 0; i < 3; i++)
            foreach (var delta in new[] { 4, 12, 22 })
            {
                var left = 8 + i * 50;
                var point = canvas.Center(left + 10 + delta, 68);
                var offset = Math.Sqrt(Math.Pow(point.X - left - 10, 2) + Math.Pow(point.Y - 68, 2)) / 8;
                canvas.AssertRedBlue(bitmap, left + 10 + delta, 68, Spread(offset, (GradientSpreadMethod)i));
            }
            canvas.AssertRgb(bitmap, 24, 124, 0, 0, 0);
            canvas.AssertRgb(bitmap, 64, 124, 20, 180, 80);
            foreach (var x in new[] { 104, 112, 136, 144 })
            {
                var t = (canvas.Center(x, 124).X - 88) / 64;
                if (t < .5) canvas.AssertRgb(bitmap, x, 124, 255 * (1 - 2 * t), 255 * 2 * t, 0);
                else canvas.AssertRgb(bitmap, x, 124, 255 * (2 - 2 * t), 255 * (2 - 2 * t), 255 * (2 * t - 1));
            }
        }

        var mutable = Linear(new Point(8, 8), new Point(72, 8));
        var transform = Matrix3x2.CreateScale(1.5f, 1.25f) * Matrix3x2.CreateTranslation(80, 16);
        var rotated = Matrix3x2.CreateScale(1.25f, 1) * Matrix3x2.CreateRotation(.25f) * Matrix3x2.CreateTranslation(88, 48);
        using (var bitmap = canvas.Frame(() =>
        {
            context.FillRect(new Rect(8, 8, 64, 24), mutable);
            mutable.Stops[0].Color = Green;
            mutable.Start = new Point(8, 48);
            mutable.End = new Point(56, 48);
            context.FillRect(new Rect(8, 48, 64, 24), mutable);
            mutable.Stops = [new GradientStop(0, Yellow)];
            context.FillRect(new Rect(8, 88, 64, 24), mutable);
            context.PushTransform(transform);
            context.FillRect(new Rect(0, 0, 32, 16), Linear(new Point(0, 0), new Point(32, 0)));
            context.PopTransform();
            context.PushTransform(rotated);
            context.FillRect(new Rect(0, 0, 32, 16), Linear(new Point(0, 0), new Point(32, 0)));
            context.PopTransform();
            context.FillRect(new Rect(88, 88, 64, 24), new LinearGradientBrush(new Point(10, 10), new Point(10, 10),
                new GradientStop(1, Color.Blue), new GradientStop(0, Color.Red), new GradientStop(0, Yellow)));
        }))
        {
            canvas.AssertRedBlue(bitmap, 32, 18, (canvas.Center(32, 18).X - 8) / 64);
            var t = Math.Clamp((canvas.Center(32, 58).X - 8) / 48, 0, 1);
            canvas.AssertRgb(bitmap, 32, 58, 0, 255 * (1 - t), 255 * t);
            canvas.AssertRgb(bitmap, 32, 98, 255, 255, 0);
            foreach (var x in new[] { 92, 116 })
                canvas.AssertRedBlue(bitmap, x, 24, (canvas.Center(x, 24).X - 80) / 48);
            Assert.True(Matrix3x2.Invert(rotated, out var inverse));
            foreach (var localX in new[] { 8, 24 })
            {
                var logical = Vector2.Transform(new Vector2(localX, 8), rotated);
                var center = canvas.Center(logical.X, logical.Y);
                var local = Vector2.Transform(center, inverse);
                canvas.AssertRedBlue(bitmap, logical.X, logical.Y, local.X / 32);
            }
            canvas.AssertRgb(bitmap, 120, 100, 255, 0, 0);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [Trait("Category", "RealVulkanGpu")]
    public void GeometryClipsAndGroupOpacityMatchActualGpuPixels(int samples)
    {
        if (!Enabled) return;
        using var environment = new GpuEnvironment(samples);
        using var canvas = new GpuCanvas(samples);
        var context = canvas.Context;
        var concave = new PathGeometry().MoveTo(new Point(8, 8)).LineTo(new Point(48, 8))
            .LineTo(new Point(48, 20)).LineTo(new Point(20, 20)).LineTo(new Point(20, 48)).LineTo(new Point(8, 48)).Close();
        var hole = RectanglePath(new Rect(60, 8, 40, 40));
        AddRectangle(hole, new Rect(72, 20, 16, 16));
        var ellipse = new EllipseGeometry(new Point(132, 28), 16, 20);
        var rotation = Matrix3x2.CreateRotation(MathF.PI / 4, new Vector2(90, 82));
        using (var bitmap = canvas.Frame(() =>
        {
            for (var i = 0; i < 3; i++) context.PushClip(concave);
            AddRectangle(concave, new Rect(24, 24, 20, 20)); // Already-pushed clips keep their old shape.
            context.FillRect(new Rect(0, 0, 56, 56), Brush.FromColor(new Color(255, 255, 255, 128)));
            for (var i = 0; i < 3; i++) context.PopClip();
            context.FillRect(new Rect(32, 32, 6, 6), Brush.FromColor(Color.Blue));
            context.PushClip(hole);
            context.PushClip(new Rect(60, 8, 40, 30));
            context.FillRect(new Rect(56, 0, 48, 56), Brush.FromColor(Color.Red));
            context.PopClip();
            context.PopClip();
            context.PushClip(ellipse);
            ellipse.RadiusX = 1;
            context.FillRect(new Rect(112, 0, 44, 56), Brush.FromColor(Green));
            context.PopClip();
            context.PushClip(new RoundedRectGeometry(new Rect(8, 64, 40, 40),
                new CornerRadius(10), new CornerRadius(0), new CornerRadius(12), new CornerRadius(3)));
            context.FillRect(new Rect(0, 56, 56, 56), Brush.FromColor(Color.White));
            context.PopClip();
            context.PushTransform(rotation);
            context.PushClip(new Rect(78, 70, 24, 24));
            context.PopTransform();
            context.FillRect(new Rect(64, 56, 56, 56), Brush.FromColor(Color.White));
            context.PopClip();
            context.FillRect(new Rect(112, 64, 8, 8), Brush.FromColor(Color.Blue));
            context.PushClip(new Rect(128.25f, 64.25f, 20.5f, 32.5f));
            context.FillRect(new Rect(122, 60, 32, 42), Brush.FromColor(Color.Red));
            context.PopClip();
        }))
        {
            canvas.AssertRgb(bitmap, 12, 36, 128, 128, 128);
            canvas.AssertRgb(bitmap, 42, 42, 0, 0, 0);
            canvas.AssertRgb(bitmap, 35, 35, 0, 0, 255);
            canvas.AssertRgb(bitmap, 64, 12, 255, 0, 0);
            canvas.AssertRgb(bitmap, 80, 28, 0, 0, 0);
            canvas.AssertRgb(bitmap, 64, 44, 0, 0, 0);
            canvas.AssertRgb(bitmap, 132, 28, 0, 255, 0);
            canvas.AssertRgb(bitmap, 142, 28, 0, 255, 0);
            canvas.AssertRgb(bitmap, 117, 9, 0, 0, 0);
            canvas.AssertRgb(bitmap, 9, 65, 0, 0, 0);
            canvas.AssertRgb(bitmap, 46, 66, 255, 255, 255);
            canvas.AssertRgb(bitmap, 46, 102, 0, 0, 0);
            canvas.AssertRgb(bitmap, 90, 82, 255, 255, 255);
            canvas.AssertRgb(bitmap, 74, 66, 0, 0, 0);
            canvas.AssertRgb(bitmap, 115, 67, 0, 0, 255);
            canvas.AssertRgb(bitmap, 132, 84, 255, 0, 0);
            canvas.AssertRgb(bitmap, 124, 84, 0, 0, 0);
        }

        using (var bitmap = canvas.Frame(() =>
        {
            context.PushLayer(new Rect(8, 8, 64, 40), .5f);
            context.FillRect(new Rect(0, 0, 48, 40), Brush.FromColor(Color.White));
            context.FillRect(new Rect(32, 8, 48, 32), Brush.FromColor(Color.White));
            context.PopLayer();
            context.PushLayer(new Rect(88, 8, 56, 40), .5f);
            context.PushLayer(new Rect(96, 12, 32, 24), .5f);
            context.FillRect(new Rect(88, 8, 56, 40), Brush.FromColor(Color.White));
            context.PopLayer();
            context.PopLayer();
            context.PushLayer(new Rect(16, 72, 96, 32), .75f);
            context.FillRect(new Rect(16, 72, 96, 32), Linear(new Point(16, 72), new Point(112, 72)));
            context.PopLayer();
        }))
        {
            foreach (var x in new[] { 16, 40, 64 }) canvas.AssertRgb(bitmap, x, 20, 128, 128, 128);
            foreach (var x in new[] { 4, 76 }) canvas.AssertRgb(bitmap, x, 20, 0, 0, 0);
            canvas.AssertRgb(bitmap, 108, 20, 64, 64, 64);
            canvas.AssertRgb(bitmap, 90, 20, 0, 0, 0);
            foreach (var x in new[] { 24, 64, 104 })
                canvas.AssertRedBlue(bitmap, x, 84, (canvas.Center(x, 84).X - 16) / 96, .75);
        }

        using var icon = new Bitmap(12, 12);
        for (var y = 0; y < icon.Height; y++)
        for (var x = 0; x < icon.Width; x++)
        {
            var pixel = icon.GetPixel(x, y);
            pixel[0] = 50; pixel[1] = 100; pixel[2] = 200; pixel[3] = 255;
        }
        icon.MarkDirty();
        var text = new TextLayout("GPU", new Font("Segoe UI", 18));
        text.Measure();
        void DrawTextAndIcon()
        {
            context.DrawImage(icon, new Rect(12, 12, 24, 24));
            context.DrawText(text, new Point(48, 12), Brush.FromColor(Color.White));
        }
        using var baseline = canvas.Frame(DrawTextAndIcon);
        using var translucent = canvas.Frame(() =>
        {
            context.PushLayer(new Rect(8, 8, 120, 40), .5f);
            DrawTextAndIcon();
            context.PopLayer();
        });
        var textInk = 0;
        for (var y = (int)(8 * canvas.Dpi); y < (int)(48 * canvas.Dpi); y++)
        for (var x = (int)(8 * canvas.Dpi); x < (int)(128 * canvas.Dpi); x++)
        {
            var before = baseline.GetPixel(x, y);
            var after = translucent.GetPixel(x, y);
            for (var channel = 0; channel < 3; channel++)
                Assert.True(Math.Abs(after[channel] - before[channel] * .5) <= 3,
                    $"Layered text/image pixel ({x},{y}) channel {channel}: baseline={before[channel]}, actual={after[channel]}");
            if (x >= 48 * canvas.Dpi && before[2] > 20) textInk++;
        }
        Assert.True(textInk > 20, "GPU baseline did not contain text ink in the text region.");
        canvas.AssertRgb(translucent, 20, 20, 100, 50, 25);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [Trait("Category", "RealVulkanGpu")]
    public void LargeGradientsRemainValidAcrossPooledLayerReuse(int samples)
    {
        if (!Enabled) return;
        using var environment = new GpuEnvironment(samples);
        using var canvas = new GpuCanvas(samples);
        var context = canvas.Context;
        var a = new Rect(8, 8, 64, 48);
        var b = new Rect(88, 8, 64, 32);
        using (var bitmap = canvas.Frame(() =>
        {
            context.PushLayer(a, .5f);
            context.FillRect(a, Linear(new Point(a.Left, a.Top), new Point(a.Right, a.Top)));
            context.PopLayer();
            context.PushLayer(b, .5f);
            context.FillRect(b, Linear(new Point(b.Left, b.Top), new Point(b.Right, b.Top)));
            context.PopLayer();
        }))
            canvas.AssertRedBlue(bitmap, 40, 24, (canvas.Center(40, 24).X - 8) / 64, .5);

        // Both layer sizes are now pooled. Grow stop data while neither layer is used.
        var large = ManyStops(new Point(8, 72), new Point(152, 72));
        using (var bitmap = canvas.Frame(() => context.FillRect(new Rect(8, 72, 144, 32), large)))
            foreach (var x in new[] { 24, 80, 136 })
                canvas.AssertRedBlue(bitmap, x, 84, (canvas.Center(x, 84).X - 8) / 144);

        // Reuse both targets with different gradient coordinates; this also exercises
        // another growth step after multiple pooled sampled descriptors already exist.
        using (var bitmap = canvas.Frame(() =>
        {
            context.PushLayer(a, .5f);
            context.FillRect(a, ManyStops(new Point(a.Left, a.Top), new Point(a.Right, a.Top)));
            context.PopLayer();
            context.PushLayer(b, .5f);
            context.FillRect(b, ManyStops(new Point(b.Left, b.Top), new Point(b.Right, b.Top)));
            context.PopLayer();
            context.FillRect(new Rect(8, 88, 64, 32), ManyStops(new Point(8, 88), new Point(72, 88)));
            context.FillRect(new Rect(88, 88, 64, 32), ManyStops(new Point(88, 88), new Point(152, 88)));
        }))
        {
            foreach (var left in new[] { 8, 88 })
            foreach (var dx in new[] { 8, 32, 56 })
            {
                var x = left + dx;
                var t = (canvas.Center(x, 24).X - left) / 64;
                canvas.AssertRedBlue(bitmap, x, 24, t, .5);
                canvas.AssertRedBlue(bitmap, x, 100, t);
            }
        }
        using (var bitmap = canvas.Frame(() =>
        {
            var green = new LinearGradientBrush(new Point(0, 0), new Point(0, 0), new GradientStop(0, Green));
            context.PushLayer(a, 1);
            context.FillRect(a, green);
            context.PopLayer();
            context.PushLayer(b, 1);
            context.FillRect(b, green);
            context.PopLayer();
        }))
        {
            canvas.AssertRgb(bitmap, 40, 24, 0, 255, 0);
            canvas.AssertRgb(bitmap, 120, 24, 0, 255, 0);
        }
    }

    private static LinearGradientBrush Linear(Point start, Point end) => new(start, end,
        new GradientStop(0, Color.Red), new GradientStop(1, Color.Blue));
    private static LinearGradientBrush ManyStops(Point start, Point end) => new(start, end,
        Enumerable.Range(0, 65).Select(i => new GradientStop(i / 64f,
            new Color((byte)Math.Round(255 * (1 - i / 64.0)), 0, (byte)Math.Round(255 * i / 64.0), 255))).ToArray());
    private static double Spread(double offset, GradientSpreadMethod spread)
    {
        if (spread == GradientSpreadMethod.Repeat) return offset - Math.Floor(offset);
        if (spread == GradientSpreadMethod.Reflect)
        {
            var value = offset - Math.Floor(offset / 2) * 2;
            return value <= 1 ? value : 2 - value;
        }
        return Math.Clamp(offset, 0, 1);
    }
    private static PathGeometry RectanglePath(Rect rect) { var path = new PathGeometry(); AddRectangle(path, rect); return path; }
    private static void AddRectangle(PathGeometry path, Rect rect)
        => path.MoveTo(new Point(rect.Left, rect.Top)).LineTo(new Point(rect.Right, rect.Top))
            .LineTo(new Point(rect.Right, rect.Bottom)).LineTo(new Point(rect.Left, rect.Bottom)).Close();

    private sealed class GpuEnvironment : IDisposable
    {
        private readonly string? _readback = Environment.GetEnvironmentVariable("SQUARE_VULKAN_READBACK");
        private readonly string? _msaa = Environment.GetEnvironmentVariable("SQUARE_VULKAN_MSAA");
        public GpuEnvironment(int samples)
        {
            Environment.SetEnvironmentVariable("SQUARE_VULKAN_READBACK", "1");
            Environment.SetEnvironmentVariable("SQUARE_VULKAN_MSAA", samples.ToString(CultureInfo.InvariantCulture));
        }
        public void Dispose()
        {
            Environment.SetEnvironmentVariable("SQUARE_VULKAN_READBACK", _readback);
            Environment.SetEnvironmentVariable("SQUARE_VULKAN_MSAA", _msaa);
        }
    }

    private sealed class GpuCanvas : IDisposable
    {
        private readonly IPlatformHost _host;
        private readonly IRenderBitmapSource _source;
        private bool _shown;
        public IRenderContext Context { get; }
        public float Dpi => Context.DpiScale;
        public GpuCanvas(int samples)
        {
            RenderBackendRegistry.Register(new VulkanBackendFactory());
            _host = PlatformFactory().CreateHost(new PlatformHostCreateInfo
            {
                Title = "Square Vulkan canvas features",
                Width = 160, Height = 160, RenderBackend = "Vulkan",
                TitleStyle = TitleStyle.Hidden, BorderStyle = BorderStyle.None
            });
            IRenderContext? createdContext = null;
            try
            {
                _host.Show();
                Context = createdContext = _host.CreateRenderContext();
                var info = Assert.IsAssignableFrom<IVulkanRenderContextInfo>(Context);
                Assert.False(string.Equals(info.DeviceInfo.DeviceType, "Cpu", StringComparison.OrdinalIgnoreCase), "GPU conformance requires a hardware Vulkan device.");
                Assert.Equal((uint)samples, info.ColorSampleCount);
                Assert.True(info.ReadbackEnabled);
                _source = Assert.IsAssignableFrom<IRenderBitmapSource>(Context);
                Assert.True(Context.CanvasSize.Width >= 159 && Context.CanvasSize.Height >= 159, "Expected a 160x160 borderless client canvas.");
            }
            catch
            {
                try { createdContext?.Dispose(); }
                finally { _host.Dispose(); }
                throw;
            }
        }
        public Bitmap Frame(Action draw)
        {
            Context.Clear(Color.Black);
            draw();
            Context.Present();
            if (!_shown) { _host.ShowAfterFirstFrame(); _shown = true; }
            return _source.CaptureBitmap();
        }
        public Vector2 Center(float x, float y) => new(((int)MathF.Floor(x * Dpi) + .5f) / Dpi, ((int)MathF.Floor(y * Dpi) + .5f) / Dpi);
        public void AssertRedBlue(Bitmap bitmap, float x, float y, double offset, double opacity = 1)
        {
            offset = Math.Clamp(offset, 0, 1);
            AssertRgb(bitmap, x, y, 255 * (1 - offset) * opacity, 0, 255 * offset * opacity);
        }
        public void AssertRgb(Bitmap bitmap, float x, float y, double r, double g, double b, int tolerance = 3)
        {
            var px = (int)MathF.Floor(x * Dpi);
            var py = (int)MathF.Floor(y * Dpi);
            var pixel = bitmap.GetPixel(px, py);
            Assert.True(Math.Abs(pixel[2] - r) <= tolerance && Math.Abs(pixel[1] - g) <= tolerance && Math.Abs(pixel[0] - b) <= tolerance,
                $"GPU pixel logical ({x},{y}), physical ({px},{py}): expected RGB=({r:F2},{g:F2},{b:F2}), actual=({pixel[2]},{pixel[1]},{pixel[0]})");
            Assert.Equal((byte)255, pixel[3]);
        }
        public void Dispose() { try { Context.Dispose(); } finally { _host.Dispose(); } }
        private static IPlatformFactory PlatformFactory()
        {
#if PLATFORM_WIN32
            return new Win32PlatformFactory();
#elif PLATFORM_X11
            return new X11PlatformFactory();
#else
            throw new PlatformNotSupportedException("Real Vulkan canvas tests require Win32 or X11.");
#endif
        }
    }
}
