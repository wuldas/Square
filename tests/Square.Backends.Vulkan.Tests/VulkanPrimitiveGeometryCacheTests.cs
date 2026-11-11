using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Square.Graphics;
using Square.Rendering.Tessellation;
using Xunit;

namespace Square.Backends.Vulkan.Tests;

public sealed class VulkanPrimitiveGeometryCacheTests
{
    private const uint Tint = 0xA37F391B;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StablePrimitiveValuesReuseExactLegacyMeshWithoutMoreBuilds(bool rounded)
    {
        var cache = new VulkanGeometryCache();
        var rect = new Rect(4, 6, 54, 38);
        var transform = new Matrix3x2(1.25f, .125f, -.25f, 1.5f, 7, -3);
        var pen = Pen.FromColor(Color.White, 3);
        var first = Get(cache, rect, 9, 7, rounded, transform, pen, Tint);
        AssertFresh(first, rect, 9, 7, rounded, transform, pen, Tint);
        for (var index = 0; index < 100; index++)
            Assert.Same(first, Get(cache, rect, 9, 7, rounded, transform, pen, Tint));
        Assert.Equal(1, cache.BuildCount);
        Assert.Equal(100, cache.HitCount);
        Assert.Equal(1, cache.Count);
        Assert.True(cache.Bytes > first.ByteSize);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryShapeTransformPaintAndDashValueInvalidates(bool rounded)
    {
        var cache = new VulkanGeometryCache();
        var rect = new Rect(4, 6, 54, 38);
        var rx = 9f; var ry = 7f;
        var transform = Matrix3x2.Identity;
        var style = new StrokeStyle { DashArray = [3, 2] };
        var pen = new Pen(Brush.FromColor(Color.White), 3, style);
        var color = Tint;
        var previous = Get(cache, rect, rx, ry, rounded, transform, pen, color);
        void Change(Action mutate)
        {
            var saved = new VulkanCachedMesh(previous.Vertices, previous.Indices);
            var builds = cache.BuildCount;
            mutate();
            var next = Get(cache, rect, rx, ry, rounded, transform, pen, color);
            Assert.Equal(builds + 1, cache.BuildCount);
            AssertFresh(next, rect, rx, ry, rounded, transform, pen, color);
            Assert.True(saved.Matches(previous.Vertices, previous.Indices));
            previous = next;
        }
        Change(() => rect = new Rect(5, rect.Y, rect.Width, rect.Height));
        Change(() => rect = new Rect(rect.X, 7, rect.Width, rect.Height));
        Change(() => rect = new Rect(rect.X, rect.Y, 56, rect.Height));
        Change(() => rect = new Rect(rect.X, rect.Y, rect.Width, 40));
        Change(() => rx = 10);
        Change(() => ry = 8);
        Change(() => transform.M11 = 1.25f);
        Change(() => transform.M12 = .125f);
        Change(() => transform.M21 = -.125f);
        Change(() => transform.M22 = 1.5f);
        Change(() => transform.M31 = .25f);
        Change(() => transform.M32 = -.5f);
        Change(() => pen.Width = 4);
        Change(() => style.Cap = LineCap.Round);
        Change(() => style.Join = LineJoin.Bevel);
        Change(() => style.MiterLimit = 2);
        Change(() => style.DashOffset = .75f);
        Change(() => style.DashArray![0] = 1.25f);
        Change(() => style.DashArray = [2, 1, 3]);
        Change(() => style.DashArray![1] = 1.5f);
        Change(() => color = 0x7F239B51);
        var beforeCopy = cache.BuildCount;
        style.DashArray = style.DashArray!.ToArray();
        Assert.Same(previous, Get(cache, rect, rx, ry, rounded, transform, pen, color));
        Assert.Equal(beforeCopy, cache.BuildCount);
        Change(() => pen.StrokeStyle = null);
    }

    [Fact]
    public void PrimitiveAndPublicPathEntriesShareLruBudgetAndKeepPathValidation()
    {
        var cache = new VulkanGeometryCache(maxEntries: 2);
        var rect = new Rect(4, 6, 54, 38);
        var pen = Pen.FromColor(Color.White, 3);
        var path = MakePath(rect, 0, 0, false, Matrix3x2.Identity);
        cache.GetStroke(path, Matrix3x2.Identity, pen, Tint);
        var primitive = Get(cache, rect, 0, 0, false, Matrix3x2.Identity, pen, Tint);
        Assert.Same(primitive, Get(cache, rect, 0, 0, false, Matrix3x2.Identity, pen, Tint));
        Get(cache, rect, 9, 7, true, Matrix3x2.Identity, pen, Tint);
        Assert.Equal(2, cache.Count);
        var builds = cache.BuildCount;
        cache.GetStroke(path, Matrix3x2.Identity, pen, Tint);
        Assert.Equal(builds + 1, cache.BuildCount); // Public path was the LRU victim.
        ((List<PathCommand>)path.Commands)[1] = new LineToCmd(new Point(42, 10));
        cache.GetStroke(path, Matrix3x2.Identity, pen, Tint);
        Assert.Equal(builds + 2, cache.BuildCount); // Same-count replacement still invalidates.
        var old = new VulkanCachedMesh(primitive.Vertices, primitive.Indices);
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Bytes);
        Assert.True(old.Matches(primitive.Vertices, primitive.Indices));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ByteBudgetAndOversizedBypassIncludePrimitiveBookkeeping(bool rounded)
    {
        var rect = new Rect(4, 6, 54, 38);
        var pen = new Pen(Brush.FromColor(Color.White), 3, new StrokeStyle { DashArray = [3, 2] });
        var probe = new VulkanGeometryCache();
        var expected = Get(probe, rect, 9, 7, rounded, Matrix3x2.Identity, pen, Tint);
        var budget = probe.Bytes + 32;
        var bounded = new VulkanGeometryCache(maxEntries: 100, maxBytes: budget);
        for (var index = 0; index < 12; index++)
        {
            Get(bounded, new Rect(index * 60, rect.Y, rect.Width, rect.Height), 9, 7, rounded, Matrix3x2.Identity, pen, Tint);
            Assert.InRange(bounded.Bytes, 0, budget);
            Assert.InRange(bounded.Count, 0, 1);
        }
        foreach (var disabled in new[] { new VulkanGeometryCache(maxEntries: 0), new VulkanGeometryCache(maxBytes: 1) })
        {
            for (var index = 0; index < 2; index++)
            {
                var actual = Get(disabled, rect, 9, 7, rounded, Matrix3x2.Identity, pen, Tint);
                Assert.True(expected.Matches(actual.Vertices, actual.Indices));
            }
            Assert.Equal(0, disabled.Count);
            Assert.Equal(0, disabled.Bytes);
            Assert.Equal(2, disabled.BuildCount);
        }
    }

    [Fact]
    public void ShapeKindsAndRawCoordinateBitsCannotAlias()
    {
        var cache = new VulkanGeometryCache();
        var pen = Pen.FromColor(Color.White, 3);
        var rect = new Rect(0, 6, 54, 38);
        Get(cache, rect, 0, 0, false, Matrix3x2.Identity, pen, Tint);
        Get(cache, rect, 0, 0, true, Matrix3x2.Identity, pen, Tint);
        Get(cache, new Rect(BitConverter.Int32BitsToSingle(int.MinValue), rect.Y, rect.Width, rect.Height), 0, 0, false, Matrix3x2.Identity, pen, Tint);
        Assert.Equal(3, cache.BuildCount);
        Assert.Equal(3, cache.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [Trait("Category", "RealVulkanGpu")]
    public void PrimitiveWarmDrawsMatchLegacyPathsWithDpiClipsLayersAndGradientPens(int samples)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("SQUARE_RUN_REAL_VULKAN_GPU_CONFORMANCE") != "1") return;
        var oldReadback = Environment.GetEnvironmentVariable("SQUARE_VULKAN_READBACK");
        var oldSamples = Environment.GetEnvironmentVariable("SQUARE_VULKAN_MSAA");
        var oldValidation = Environment.GetEnvironmentVariable("SQUARE_VULKAN_VALIDATION");
        try
        {
            Environment.SetEnvironmentVariable("SQUARE_VULKAN_READBACK", "1");
            Environment.SetEnvironmentVariable("SQUARE_VULKAN_MSAA", samples.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Environment.SetEnvironmentVariable("SQUARE_VULKAN_VALIDATION", "1");
            foreach (var dpi in new[] { 1f, 1.25f })
            {
                using var fixture = new HiddenContext(dpi, samples);
                var context = fixture.Context;
                var transform = new Matrix3x2(1, .1f, -.05f, 1, 2, 3);
                var physicalTransform = transform * Matrix3x2.CreateScale(dpi);
                var solid = new Pen(Brush.FromColor(new Color(59, 130, 246, 128)), 2.75f,
                    new StrokeStyle { Cap = LineCap.Round, Join = LineJoin.Bevel, DashArray = [3, 2], DashOffset = .5f });
                var gradient = new LinearGradientBrush(new Point(72, 8), new Point(136, 52),
                    new GradientStop(0, Color.Red), new GradientStop(1, Color.Blue));
                var gradientPen = new Pen(gradient, 3.25f, new StrokeStyle { Join = LineJoin.Round, DashArray = [2, 1] });
                Bitmap Draw(bool primitive)
                {
                    context.Clear(Color.Black);
                    context.PushTransform(transform);
                    void Stroke(Rect rect, float rx, float ry, bool rounded, Pen pen)
                    {
                        if (primitive)
                        {
                            if (rounded) context.DrawGeometry(new RoundedRectGeometry(rect, rx, ry), pen);
                            else context.DrawRect(rect, pen);
                        }
                        else context.DrawPath(MakePath(rect, rx, ry, rounded, physicalTransform), pen);
                    }
                    for (var repeat = 0; repeat < 3; repeat++)
                    {
                        Stroke(new Rect(6, 8, 54, 38), 0, 0, false, solid);
                        Stroke(new Rect(72, 8, 54, 40), 10, 8, true, gradientPen);
                    }
                    context.PushClip(new EllipseGeometry(new Point(36, 86), 30, 20));
                    Stroke(new Rect(8, 66, 60, 40), 0, 0, false, solid);
                    context.PopClip();
                    context.PushLayer(new Rect(74, 66, 56, 48), .6f);
                    for (var repeat = 0; repeat < 3; repeat++) Stroke(new Rect(78, 70, 48, 38), 8, 7, true, solid);
                    context.PopLayer();
                    context.PopTransform();
                    context.Present();
                    return context.CaptureSubmittedFrame(((IVulkanFrameDiagnostics)context).GetFrameStatistics().SubmittedFrames);
                }
                using var reference = Draw(false);
                using var cold = Draw(true);
                var cache = Assert.IsType<VulkanGeometryCache>(typeof(VulkanRenderContext)
                    .GetField("_geometryCache", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(context));
                var builds = cache.BuildCount;
                using var warm = Draw(true);
                Assert.Equal(builds, cache.BuildCount);
                Assert.Equal(reference.Pixels, cold.Pixels);
                Assert.Equal(reference.Pixels, warm.Pixels);
                // Same mesh values with a changed gradient must still resolve today's material.
                gradient.Stops[0] = new GradientStop(0, Color.Green);
                using var changedReference = Draw(false);
                using var changedWarm = Draw(true);
                Assert.Equal(changedReference.Pixels, changedWarm.Pixels);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SQUARE_VULKAN_READBACK", oldReadback);
            Environment.SetEnvironmentVariable("SQUARE_VULKAN_MSAA", oldSamples);
            Environment.SetEnvironmentVariable("SQUARE_VULKAN_VALIDATION", oldValidation);
        }
    }

    private static VulkanCachedMesh Get(VulkanGeometryCache cache, Rect rect, float rx, float ry, bool rounded,
        Matrix3x2 transform, Pen pen, uint color)
    {
        if (cache.TryGetPrimitiveStroke(rect, rx, ry, rounded, transform, pen, color, out var mesh)) return mesh;
        return cache.BuildPrimitiveStroke(MakePath(rect, rx, ry, rounded, transform), rect, rx, ry, rounded, transform, pen, color);
    }

    private static void AssertFresh(VulkanCachedMesh actual, Rect rect, float rx, float ry, bool rounded,
        Matrix3x2 transform, Pen pen, uint color)
    {
        var fresh = new VulkanGeometryCache().GetStroke(MakePath(rect, rx, ry, rounded, transform), transform, pen, color);
        Assert.True(fresh.Matches(actual.Vertices, actual.Indices));
    }

    // Original renderer path construction, including its clamp, segment choice and float operation order.
    private static PathGeometry MakePath(Rect rect, float rx, float ry, bool rounded, Matrix3x2 transform)
    {
        var path = PathGeometry.Create();
        if (!rounded || rx <= 0 || ry <= 0)
            return path.MoveTo(new Point(rect.X, rect.Y)).LineTo(new Point(rect.Right, rect.Y))
                .LineTo(new Point(rect.Right, rect.Bottom)).LineTo(new Point(rect.X, rect.Bottom)).Close();
        rx = Math.Min(rx, rect.Width / 2);
        ry = Math.Min(ry, rect.Height / 2);
        var segments = PathTessellator.GetCurveSegmentCount(rx, ry, MathF.PI / 2, transform);
        var l = rect.X; var t = rect.Y; var r = rect.Right; var b = rect.Bottom;
        void Arc(float cx, float cy, float start)
        {
            for (var index = 1; index <= segments; index++)
            {
                var angle = start + MathF.PI / 2 * index / segments;
                path.LineTo(new Point(cx + rx * MathF.Cos(angle), cy + ry * MathF.Sin(angle)));
            }
        }
        path.MoveTo(new Point(l + rx, t));
        path.LineTo(new Point(r - rx, t)); Arc(r - rx, t + ry, -MathF.PI / 2);
        path.LineTo(new Point(r, b - ry)); Arc(r - rx, b - ry, 0);
        path.LineTo(new Point(l + rx, b)); Arc(l + rx, b - ry, MathF.PI / 2);
        path.LineTo(new Point(l, t + ry)); Arc(l + rx, t + ry, MathF.PI);
        return path.Close();
    }

    private sealed class HiddenContext : IDisposable
    {
        private readonly IntPtr _window;
        public VulkanRenderContext Context { get; }
        public HiddenContext(float dpi, int samples)
        {
            var instance = GetModuleHandle(null);
            _window = CreateWindowEx(0, "STATIC", "Vulkan primitive cache test", 0x80000000u,
                0, 0, 160, 160, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            Assert.NotEqual(IntPtr.Zero, _window);
            VulkanRenderContext? created = null;
            try
            {
                Context = created = new VulkanRenderContext(new RenderContextCreateInfo
                {
                    CanvasSize = new Size(160 / dpi, 160 / dpi), DpiScale = dpi, VSync = false,
                    NativeTarget = new Win32RenderTarget(_window, instance)
                });
                Assert.NotEqual("Cpu", Context.DeviceInfo.DeviceType);
                Assert.Equal((uint)samples, Context.ColorSampleCount);
                Assert.True(Context.ReadbackEnabled);
            }
            catch
            {
                try { created?.Dispose(); }
                finally { DestroyWindow(_window); }
                throw;
            }
        }
        public void Dispose()
        {
            try { Context.Dispose(); }
            finally { DestroyWindow(_window); }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandle(string? module);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);
}
