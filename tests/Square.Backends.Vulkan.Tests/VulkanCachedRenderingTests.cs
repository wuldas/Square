using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Square.Graphics;
using Xunit;

namespace Square.Backends.Vulkan.Tests;

[CollectionDefinition("VulkanCachedRendering", DisableParallelization = true)]
public sealed class VulkanCachedRenderingCollection { }

[Collection("VulkanCachedRendering")]
public sealed class VulkanCachedRenderingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [Trait("Category", "RealVulkanGpu")]
    public void CachedMutableScenesMatchColdContextsAcrossTwoSlotsAndResize(int samples)
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("SQUARE_RUN_REAL_VULKAN_GPU_CONFORMANCE") != "1") return;
        using var environment = new GpuEnvironment(samples);
        // Each reference has a completely new context, not a cache cleared through
        // private fields or the same warm cache queried with a second key.
        using var coldA = RenderCold(samples, new SceneState(false));
        using var coldB = RenderCold(samples, new SceneState(true));
        var appended = new SceneState(false);
        AppendHole(appended.FillPath);
        using var coldC = RenderCold(samples, appended);
        AssertBright(coldA, 14, 88);
        AssertBlack(coldA, 58, 88);
        AssertBlack(coldB, 14, 88);
        AssertBright(coldB, 58, 88);
        Assert.True(coldA.GetPixel(108, 28)[2] > 120, "The reference fill probe must cover the later hole.");
        AssertBlack(coldC, 108, 28);

        using var fixture = new HiddenContext(samples);
        var state = new SceneState(false);
        var warmSerial = fixture.Submit(() => Draw(fixture.Context, state));
        using (var warm = fixture.Context.CaptureSubmittedFrame(warmSerial)) AssertFrame(coldA, warm);

        void VerifyPair(ulong a, ulong b, Bitmap referenceA, Bitmap referenceB, bool reverse)
        {
            using var first = fixture.Context.CaptureSubmittedFrame(reverse ? b : a);
            using var second = fixture.Context.CaptureSubmittedFrame(reverse ? a : b);
            AssertFrame(referenceA, reverse ? second : first);
            AssertFrame(referenceB, reverse ? first : second);
            fixture.Drain();
        }

        for (var round = 0; round < 2; round++)
        {
            state.Apply(false);
            var a = fixture.Submit(() => Draw(fixture.Context, state));
            // Same path/list/pen/style/dash/brush instances change before the first
            // submitted slot is captured. Earlier immutable CPU/GPU data must survive.
            state.Apply(true);
            var b = fixture.Submit(() => Draw(fixture.Context, state));
            Assert.Equal(a + 1, b);
            VerifyPair(a, b, coldA, coldB, round != 0);
        }

        // Resize drains two uncaptured submissions. The same cached geometry must
        // subsequently render at the old coordinates and in newly exposed pixels.
        state.Apply(false);
        fixture.Submit(() => Draw(fixture.Context, state));
        state.Apply(true);
        fixture.Submit(() => Draw(fixture.Context, state));
        var submittedBeforeResize = fixture.Statistics().SubmittedFrames;
        fixture.Resize(192, 176);
        Assert.Equal(submittedBeforeResize, fixture.Drain().SubmittedFrames);
        using var resizedColdA = RenderCold(samples, new SceneState(false), 192, 176);
        using var resizedColdB = RenderCold(samples, new SceneState(true), 192, 176);
        using var resizedColdC = RenderCold(samples, appended, 192, 176);
        state.Apply(false);
        var resizedA = fixture.Submit(() => Draw(fixture.Context, state));
        state.Apply(true);
        var resizedB = fixture.Submit(() => Draw(fixture.Context, state));
        VerifyPair(resizedA, resizedB, resizedColdA, resizedColdB, true);

        // Appending a new contour preserves the path identity but introduces an
        // even-odd hole. Queue both before capture to catch mutable payload aliases.
        state.Apply(false);
        var beforeAppend = fixture.Submit(() => Draw(fixture.Context, state));
        AppendHole(state.FillPath);
        var afterAppend = fixture.Submit(() => Draw(fixture.Context, state));
        VerifyPair(beforeAppend, afterAppend, resizedColdA, resizedColdC, false);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [Trait("Category", "RealVulkanGpu")]
    public void SolidLeafOpacityMatchesOffscreenComposition(int samples)
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("SQUARE_RUN_REAL_VULKAN_GPU_CONFORMANCE") != "1") return;
        using var environment = new GpuEnvironment(samples);
        using var fixture = new HiddenContext(samples);
        var context = fixture.Context;
        var opacities = new[] { float.NegativeInfinity, 0f, .17f, .5f, .83f, 1f, float.PositiveInfinity, float.NaN };
        var alphas = new byte[] { 0, 128, 255 };
        foreach (var background in new[] { Color.Black, Color.Transparent })
        {
            Bitmap Draw(bool optimized)
            {
                context.Clear(background);
                // An integer scissor remains safe: no partially covered edge is folded.
                context.PushClip(new Rect(4, 4, 132, 140));
                for (var row = 0; row < opacities.Length; row++)
                for (var column = 0; column < alphas.Length; column++)
                {
                    var bounds = new Rect(8 + column * 40, 8 + row * 16, 24, 12);
                    var color = new Color(59, 130, 246, alphas[column]);
                    if (optimized)
                        Assert.True(context.TryFillSolidLeafOpacity(bounds, color, opacities[row]));
                    else
                    {
                        context.PushLayer(bounds, opacities[row]);
                        context.FillRect(bounds, new SolidColorBrush(color));
                        context.PopLayer();
                    }
                }
                context.PopClip();
                context.Present();
                return context.CaptureSubmittedFrame(fixture.Statistics().SubmittedFrames);
            }
            using var reference = Draw(false);
            using var optimized = Draw(true);
            Assert.Equal(reference.Width, optimized.Width);
            Assert.Equal(reference.Height, optimized.Height);
            // The old path quantizes an intermediate RGBA8 layer as well as the final
            // target. Folding removes that intermediate rounding; allow two byte steps.
            for (var i = 0; i < reference.Pixels.Length; i++)
                Assert.True(Math.Abs(reference.Pixels[i] - optimized.Pixels[i]) <= 2,
                    $"GPU opacity mismatch at byte {i}: {reference.Pixels[i]} vs {optimized.Pixels[i]}");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [Trait("Category", "RealVulkanGpu")]
    public void SolidLeafOpacityDeclinesPartialCoverageAndNestedLayers(int samples)
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("SQUARE_RUN_REAL_VULKAN_GPU_CONFORMANCE") != "1") return;
        using var environment = new GpuEnvironment(samples);
        using var fixture = new HiddenContext(samples);
        var context = fixture.Context;
        var rect = new Rect(16, 16, 32, 24);
        context.Clear(Color.Black);
        foreach (var transform in new[] { Matrix3x2.CreateTranslation(.5f, 0), Matrix3x2.CreateRotation(.2f) })
        {
            context.PushTransform(transform);
            Assert.False(context.TryFillSolidLeafOpacity(rect, Color.Blue, .5f));
            context.PopTransform();
        }
        context.PushClip(new EllipseGeometry(new Point(40, 40), 24, 24));
        Assert.False(context.TryFillSolidLeafOpacity(rect, Color.Blue, .5f));
        context.PopClip();
        context.PushLayer(new Rect(8, 8, 80, 80), .5f);
        Assert.False(context.TryFillSolidLeafOpacity(rect, Color.Blue, .5f));
        context.FillRect(rect, new SolidColorBrush(Color.Blue));
        context.FillRect(new Rect(32, 16, 32, 24), new SolidColorBrush(Color.Blue));
        context.PopLayer();
        context.Present();
        using var bitmap = context.CaptureSubmittedFrame(fixture.Statistics().SubmittedFrames);
        // The overlap must keep group opacity, not blend two separately faded shapes.
        Assert.InRange(bitmap.GetPixel(36, 24)[0], (byte)126, (byte)129);
        Assert.InRange(bitmap.GetPixel(20, 24)[0], (byte)126, (byte)129);
    }

    private static Bitmap RenderCold(int samples, SceneState scene, int width = 160, int height = 160)
    {
        using var fresh = new HiddenContext(samples, width, height);
        var serial = fresh.Submit(() => Draw(fresh.Context, scene));
        return fresh.Context.CaptureSubmittedFrame(serial);
    }

    private static void Draw(VulkanRenderContext context, SceneState scene)
    {
        context.PushTransform(scene.Transform);
        for (var i = 0; i < 3; i++)
        {
            // Repeated translucent instructions must remain repeated draws on hits.
            context.FillPath(scene.FillPath, scene.FillBrush);
            context.DrawPath(scene.StrokePath, scene.Pen);
        }
        context.PopTransform();
        context.PushClip(scene.ParentClip);
        context.PushClip(scene.ChildClip);
        for (var i = 0; i < 3; i++) context.FillRect(new Rect(8, 64, 56, 40), scene.ClipBrush);
        context.PopClip();
        context.PopClip();
        context.PushLayer(new Rect(80, 64, 64, 40), .5f);
        context.FillPath(scene.FooterPath, scene.FooterBrush);
        context.FillPath(scene.FooterPath, scene.FooterBrush);
        context.PopLayer();
        // Outside the original 160px canvas; visible after Resize without changing
        // this path or transform, so an old empty clipped result cannot be retained.
        context.FillPath(scene.ExtendedPath, scene.MarkerBrush);
    }

    private static void AssertFrame(Bitmap expected, Bitmap actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        for (var y = 0; y < expected.Height; y++)
        for (var x = 0; x < expected.Width; x++)
        {
            var a = expected.GetPixel(x, y);
            var b = actual.GetPixel(x, y);
            for (var channel = 0; channel < 4; channel++)
                Assert.True(a[channel] == b[channel],
                    $"Cached/cold pixel ({x},{y}) channel {channel}: expected={a[channel]}, actual={b[channel]}");
        }
    }

    private static void AssertBright(Bitmap bitmap, int x, int y)
    {
        var p = bitmap.GetPixel(x, y);
        Assert.True(p[0] > 180 && p[1] > 180 && p[2] > 180, $"Reference probe ({x},{y}) is not inside the intended parent clip.");
    }
    private static void AssertBlack(Bitmap bitmap, int x, int y)
    {
        var p = bitmap.GetPixel(x, y);
        Assert.True(p[0] == 0 && p[1] == 0 && p[2] == 0, $"Reference probe ({x},{y}) unexpectedly contains geometry.");
    }

    private sealed class SceneState
    {
        public PathGeometry StrokePath { get; } = Stroke(false);
        public PathGeometry FillPath { get; } = Fill(false);
        public PathGeometry ParentClip { get; } = Parent(false);
        public EllipseGeometry ChildClip { get; } = new(new Point(36, 84), 28, 20);
        public PathGeometry FooterPath { get; } = Rectangle(new Rect(80, 64, 64, 40));
        public PathGeometry ExtendedPath { get; } = Rectangle(new Rect(168, 144, 16, 16));
        public SolidColorBrush FillBrush { get; } = new(Color.Red);
        public SolidColorBrush StrokeBrush { get; } = new(Color.White);
        public SolidColorBrush FooterBrush { get; } = new(Color.Blue);
        public SolidColorBrush MarkerBrush { get; } = new(Color.Red);
        public SolidColorBrush ClipBrush { get; } = new(new Color(255, 255, 255, 160));
        public StrokeStyle Style { get; } = new() { DashArray = [3, 2] };
        public Pen Pen { get; }
        public Matrix3x2 Transform { get; private set; }

        public SceneState(bool second)
        {
            Pen = new Pen(StrokeBrush, 2, Style);
            Apply(second);
        }
        public void Apply(bool second)
        {
            ReplaceCommands(StrokePath, Stroke(second));
            ReplaceCommands(FillPath, Fill(second));
            ReplaceCommands(ParentClip, Parent(second));
            Pen.Width = second ? 5 : 2;
            Style.Cap = second ? LineCap.Round : LineCap.Butt;
            Style.Join = second ? LineJoin.Round : LineJoin.Miter;
            Style.MiterLimit = second ? 2 : 10;
            Style.DashOffset = second ? .75f : 0;
            Style.DashArray![0] = second ? 1.5f : 3;
            Style.DashArray[1] = second ? 1 : 2;
            FillBrush.Color = second ? new Color(20, 100, 240, 192) : new Color(240, 40, 20, 160);
            StrokeBrush.Color = second ? new Color(255, 200, 20, 192) : new Color(255, 255, 255, 128);
            FooterBrush.Color = second ? Color.FromRgb(255, 255, 0) : Color.Blue;
            MarkerBrush.Color = second ? Color.FromRgb(0, 255, 0) : Color.Red;
            Transform = second ? new Matrix3x2(1, 0, .125f, 1, 2, 1) : Matrix3x2.Identity;
        }
    }

    private static void ReplaceCommands(PathGeometry target, PathGeometry source)
    {
        var list = Assert.IsType<List<PathCommand>>(target.Commands);
        Assert.Equal(list.Count, source.Commands.Count);
        for (var i = 0; i < list.Count; i++) list[i] = source.Commands[i];
    }
    private static PathGeometry Stroke(bool second) => new PathGeometry().MoveTo(new Point(12, 12))
        .LineTo(new Point(64, second ? 28 : 12)).LineTo(new Point(44, 44));
    private static PathGeometry Fill(bool second) => second
        ? Polygon([new(80, 12), new(140, 20), new(132, 48), new(88, 40)])
        : Rectangle(new Rect(80, 12, 60, 36));
    private static PathGeometry Parent(bool second) => second
        ? Polygon([new(8, 64), new(64, 64), new(64, 104), new(52, 104), new(52, 76), new(8, 76)])
        : Polygon([new(8, 64), new(64, 64), new(64, 76), new(20, 76), new(20, 104), new(8, 104)]);
    private static PathGeometry Polygon(Point[] points)
    {
        var path = new PathGeometry().MoveTo(points[0]);
        foreach (var point in points.Skip(1)) path.LineTo(point);
        return path.Close();
    }
    private static PathGeometry Rectangle(Rect r) => Polygon([new(r.Left, r.Top), new(r.Right, r.Top), new(r.Right, r.Bottom), new(r.Left, r.Bottom)]);
    private static void AppendHole(PathGeometry path)
        => path.MoveTo(new Point(100, 20)).LineTo(new Point(116, 20)).LineTo(new Point(116, 36)).LineTo(new Point(100, 36)).Close();

    private sealed class HiddenContext : IDisposable
    {
        private IntPtr _window;
        public VulkanRenderContext Context { get; }
        public HiddenContext(int samples, int width = 160, int height = 160)
        {
            var instance = GetModuleHandle(null);
            _window = CreateWindowEx(0, "STATIC", "Vulkan cache rendering test", 0x80000000u,
                0, 0, width, height, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            Assert.NotEqual(IntPtr.Zero, _window);
            VulkanRenderContext? created = null;
            try
            {
                Context = created = new VulkanRenderContext(new RenderContextCreateInfo
                {
                    CanvasSize = new Size(width, height), DpiScale = 1, VSync = false,
                    NativeTarget = new Win32RenderTarget(_window, instance)
                });
                Assert.False(string.Equals(Context.DeviceInfo.DeviceType, "Cpu", StringComparison.OrdinalIgnoreCase));
                Assert.Equal((uint)samples, Context.ColorSampleCount);
                Assert.True(Context.ReadbackEnabled);
            }
            catch
            {
                try { created?.Dispose(); }
                finally { DestroyWindow(_window); _window = IntPtr.Zero; }
                throw;
            }
        }
        public ulong Submit(Action draw)
        {
            Context.Clear(Color.Black);
            draw();
            Context.Present();
            return Statistics().SubmittedFrames;
        }
        public VulkanFrameStatistics Statistics() => ((IVulkanFrameDiagnostics)Context).GetFrameStatistics();
        public VulkanFrameStatistics Drain()
        {
            ((IVulkanFrameDiagnostics)Context).WaitForSubmittedFrames();
            var stats = Statistics();
            Assert.Equal(stats.SubmittedFrames, stats.CompletedFrames);
            Assert.Equal(0, stats.PendingFrames);
            return stats;
        }
        public void Resize(int width, int height)
        {
            Assert.True(SetWindowPos(_window, IntPtr.Zero, 0, 0, width, height, 0x0016));
            Context.Resize(new Size(width, height), 1);
        }
        public void Dispose()
        {
            if (_window == IntPtr.Zero) return;
            try { Context.Dispose(); }
            finally { DestroyWindow(_window); _window = IntPtr.Zero; }
        }
    }

    private sealed class GpuEnvironment : IDisposable
    {
        private readonly string? _readback = Environment.GetEnvironmentVariable("SQUARE_VULKAN_READBACK");
        private readonly string? _samples = Environment.GetEnvironmentVariable("SQUARE_VULKAN_MSAA");
        private readonly string? _validation = Environment.GetEnvironmentVariable("SQUARE_VULKAN_VALIDATION");
        public GpuEnvironment(int samples)
        {
            Environment.SetEnvironmentVariable("SQUARE_VULKAN_READBACK", "1");
            Environment.SetEnvironmentVariable("SQUARE_VULKAN_MSAA", samples.ToString(CultureInfo.InvariantCulture));
            Environment.SetEnvironmentVariable("SQUARE_VULKAN_VALIDATION", "1");
        }
        public void Dispose()
        {
            Environment.SetEnvironmentVariable("SQUARE_VULKAN_READBACK", _readback);
            Environment.SetEnvironmentVariable("SQUARE_VULKAN_MSAA", _samples);
            Environment.SetEnvironmentVariable("SQUARE_VULKAN_VALIDATION", _validation);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandle(string? module);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);
}
