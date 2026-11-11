using System.Globalization;
using System.Runtime.InteropServices;
using Square.Graphics;
using Xunit;

namespace Square.Backends.Vulkan.Tests;

[CollectionDefinition("VulkanFramePipelining", DisableParallelization = true)]
public sealed class VulkanFramePipeliningCollection { }

[Collection("VulkanFramePipelining")]
public sealed class VulkanFramePipeliningTests
{
    private static readonly Color Green = Color.FromRgb(0, 255, 0);
    private static readonly Color Yellow = Color.FromRgb(255, 255, 0);
    private static readonly Color Cyan = Color.FromRgb(0, 255, 255);
    private static readonly Color OriginalIcon = new(200, 100, 50, 255);
    private static readonly Color MutatedIcon = new(10, 210, 190, 255);

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [Trait("Category", "RealVulkanGpu")]
    public void TwoSubmittedFramesRetainDistinctPixelsAcrossSlotReuseResizeAndDispose(int samples)
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("SQUARE_RUN_REAL_VULKAN_GPU_CONFORMANCE") != "1") return;

        using var environment = new GpuEnvironment(samples);
        using var fixture = new HiddenContext(samples);
        var context = fixture.Context;
        using var icon = new Bitmap(12, 12);
        SetIcon(icon, OriginalIcon);
        var font = new Font("Segoe UI", 16);
        var textA = new TextLayout("PIPE A", font);
        var textB = new TextLayout("PIPE B", font);
        var extraGlyph = new TextLayout("Ω", new Font("Segoe UI", 18));
        textA.Measure();
        textB.Measure();
        extraGlyph.Measure();
        var brush = new LinearGradientBrush(new Point(8, 8), new Point(120, 8));

        ulong Submit(bool second, int stops, bool addNewGlyph = false)
        {
            brush.Start = new Point(8, 8);
            brush.End = new Point(second ? 88 : 120, 8);
            brush.Stops = MakeStops(stops, second ? Green : Color.Red, second ? Color.Red : Color.Blue);
            return fixture.Submit(() => DrawScene(context, brush, icon, second ? textB : textA, second, addNewGlyph ? extraGlyph : null));
        }

        // Warm all steady-state image/glyph atlas entries before the overlap checks.
        // Only these setup frames are captured one at a time.
        var warmA = Submit(false, 2);
        using var baselineA = context.CaptureSubmittedFrame(warmA);
        var warmB = Submit(true, 2);
        using var baselineB = context.CaptureSubmittedFrame(warmB);
        AssertGlyphInk(baselineA);
        AssertGlyphInk(baselineB);
        fixture.DrainAndAssertComplete();

        void VerifyPair(ulong firstSerial, ulong secondSerial, bool reverseCaptureOrder, Color secondIcon, bool newGlyph)
        {
            // Both Present calls have returned before either capture or explicit drain.
            using var capturedFirst = context.CaptureSubmittedFrame(reverseCaptureOrder ? secondSerial : firstSerial);
            using var capturedSecond = context.CaptureSubmittedFrame(reverseCaptureOrder ? firstSerial : secondSerial);
            var frameA = reverseCaptureOrder ? capturedSecond : capturedFirst;
            var frameB = reverseCaptureOrder ? capturedFirst : capturedSecond;
            Assert.Equal((int)context.CanvasSize.Width, frameA.Width);
            Assert.Equal((int)context.CanvasSize.Height, frameA.Height);
            Assert.Equal(frameA.Width, frameB.Width);
            Assert.Equal(frameA.Height, frameB.Height);
            AssertScene(frameA, baselineA, false, OriginalIcon, false);
            AssertScene(frameB, baselineB, true, secondIcon, newGlyph);
            using var latest = context.CaptureBitmap();
            AssertScene(latest, baselineB, true, secondIcon, newGlyph);
            fixture.AssertComplete();
        }

        for (var batch = 0; batch < 4; batch++)
        {
            SetIcon(icon, OriginalIcon);
            var a = Submit(false, batch == 0 ? 2 : 65);
            // Mutate the just-submitted brush in place before building the other slot.
            brush.Stops[0].Color = Yellow;
            var uploadNewAtlasData = batch == 2;
            if (uploadNewAtlasData) SetIcon(icon, MutatedIcon);
            var b = Submit(true, batch < 2 ? 65 : 257, uploadNewAtlasData);
            Assert.Equal(a + 1, b);
            VerifyPair(a, b, batch % 2 != 0, uploadNewAtlasData ? MutatedIcon : OriginalIcon, uploadNewAtlasData);
        }

        fixture.DrainAndAssertComplete();
        var completedBeforeResize = fixture.Statistics();
        // Queue another pair without captures, then resize with submitted work still
        // eligible to be pending. Fast GPUs may already have signalled either fence.
        SetIcon(icon, OriginalIcon);
        Submit(false, 65);
        Submit(true, 257);
        var beforeResize = fixture.Statistics();
        Assert.Equal(completedBeforeResize.SubmittedFrames + 2, beforeResize.SubmittedFrames);
        fixture.Resize(192, 176);
        var afterResize = fixture.AssertComplete();
        Assert.Equal(beforeResize.SubmittedFrames, afterResize.SubmittedFrames);
        Assert.True(afterResize.CompletedFrames >= completedBeforeResize.CompletedFrames);
        Assert.Equal((uint)samples, context.ColorSampleCount);

        // Old serial history is intentionally invalidated by Resize. Only read the two
        // new submissions; counters remain cumulative and fresh bundles must be usable.
        SetIcon(icon, OriginalIcon);
        var resizedA = Submit(false, 65);
        var resizedB = Submit(true, 257);
        VerifyPair(resizedA, resizedB, true, OriginalIcon, false);
        Assert.Equal(afterResize.SubmittedFrames + 2, fixture.Statistics().SubmittedFrames);

        // Dispose is a drain path too: no capture or explicit wait is allowed between
        // this final pair and disposal. Repeated Dispose must remain harmless.
        Submit(false, 65);
        Submit(true, 257);
        fixture.Statistics();
        context.Dispose();
        context.Dispose();
        Assert.Throws<ObjectDisposedException>(() => context.Clear(Color.Black));
    }

    private static void DrawScene(VulkanRenderContext context, LinearGradientBrush brush, Bitmap icon,
        TextLayout text, bool second, TextLayout? extraGlyph)
    {
        context.FillRect(new Rect(8, 8, 112, 28), brush);
        context.FillRect(new Rect(136, 8, 16, 16), Brush.FromColor(second ? Cyan : Yellow));
        var groupColor = second ? Yellow : Color.White;
        context.PushLayer(new Rect(8, 48, 112, 40), .5f);
        context.FillRect(new Rect(8, 48, 72, 40), Brush.FromColor(groupColor));
        context.FillRect(new Rect(48, 48, 72, 40), Brush.FromColor(groupColor));
        context.PopLayer();
        context.PushLayer(new Rect(128, 48, 24, 40), .5f);
        context.PushLayer(new Rect(128, 48, 24, 40), .5f);
        context.FillRect(new Rect(128, 48, 24, 40), Brush.FromColor(second ? Green : Color.Blue));
        context.PopLayer();
        context.PopLayer();
        context.DrawImage(icon, new Rect(8, 108, 24, 24));
        context.DrawText(text, new Point(48, 108), Brush.FromColor(second ? Cyan : Color.White));
        if (extraGlyph is not null)
            context.DrawText(extraGlyph, new Point(136, 108), Brush.FromColor(Color.White));
    }

    private static void AssertScene(Bitmap bitmap, Bitmap glyphBaseline, bool second, Color icon, bool extraGlyph)
    {
        var name = second ? "second submitted frame" : "first submitted frame";
        foreach (var x in new[] { 16, 56, 104 })
        {
            var t = Math.Clamp((x + .5 - 8) / (second ? 80 : 112), 0, 1);
            AssertRgb(bitmap, x, 20, second ? 255 * t : 255 * (1 - t), second ? 255 * (1 - t) : 0,
                second ? 0 : 255 * t, name);
        }
        AssertRgb(bitmap, 144, 16, second ? 0 : 255, 255, second ? 255 : 0, name);
        foreach (var x in new[] { 24, 64, 104 })
            AssertRgb(bitmap, x, 64, 128, 128, second ? 0 : 128, name);
        AssertRgb(bitmap, 140, 64, 0, second ? 64 : 0, second ? 0 : 64, name);
        AssertRgb(bitmap, 20, 120, icon.R, icon.G, icon.B, name);
        AssertRgb(bitmap, 150, 150, 0, 0, 0, name);
        for (var y = 104; y < 136; y++)
        for (var x = 44; x < 128; x++)
        {
            var expected = glyphBaseline.GetPixel(x, y);
            var actual = bitmap.GetPixel(x, y);
            for (var channel = 0; channel < 4; channel++)
                Assert.True(Math.Abs(expected[channel] - actual[channel]) <= 3,
                    $"{name}: shared-atlas glyph mismatch at ({x},{y}) channel {channel}, expected={expected[channel]}, actual={actual[channel]}");
        }
        if (extraGlyph)
        {
            var ink = 0;
            for (var y = 104; y < 140; y++)
            for (var x = 132; x < 160; x++)
                if (bitmap.GetPixel(x, y)[2] > 20) ink++;
            Assert.True(ink > 10, "The new glyph uploaded while the earlier frame was submitted did not render.");
        }
    }

    private static void AssertGlyphInk(Bitmap bitmap)
    {
        var ink = 0;
        for (var y = 104; y < 136; y++)
        for (var x = 44; x < 128; x++)
            if (bitmap.GetPixel(x, y)[1] > 20) ink++;
        Assert.True(ink > 30, "GPU baseline did not contain the shared-atlas glyph pattern.");
    }

    private static void AssertRgb(Bitmap bitmap, int x, int y, double r, double g, double b, string label)
    {
        var actual = bitmap.GetPixel(x, y);
        Assert.True(Math.Abs(actual[2] - r) <= 3 && Math.Abs(actual[1] - g) <= 3 && Math.Abs(actual[0] - b) <= 3,
            $"{label} pixel ({x},{y}): expected RGB ({r:F2},{g:F2},{b:F2}), actual ({actual[2]},{actual[1]},{actual[0]})");
        Assert.Equal((byte)255, actual[3]);
    }

    private static GradientStop[] MakeStops(int count, Color start, Color end)
        => Enumerable.Range(0, count).Select(i =>
        {
            var t = i / (double)(count - 1);
            return new GradientStop((float)t, new Color(
                (byte)Math.Round(start.R + (end.R - start.R) * t),
                (byte)Math.Round(start.G + (end.G - start.G) * t),
                (byte)Math.Round(start.B + (end.B - start.B) * t), 255));
        }).ToArray();

    private static void SetIcon(Bitmap icon, Color color)
    {
        for (var y = 0; y < icon.Height; y++)
        for (var x = 0; x < icon.Width; x++)
        {
            var pixel = icon.GetPixel(x, y);
            pixel[0] = color.B; pixel[1] = color.G; pixel[2] = color.R; pixel[3] = color.A;
        }
        icon.MarkDirty();
    }

    private sealed class HiddenContext : IDisposable
    {
        private IntPtr _window;
        private ulong _expectedSubmitted;
        private ulong _lastCompleted;
        public VulkanRenderContext Context { get; }
        public IVulkanFrameDiagnostics Diagnostics { get; }

        public HiddenContext(int samples)
        {
            var instance = GetModuleHandle(null);
            _window = CreateWindowEx(0, "STATIC", "Vulkan frame pipelining test", 0x80000000u,
                0, 0, 160, 160, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            Assert.NotEqual(IntPtr.Zero, _window);
            VulkanRenderContext? created = null;
            try
            {
                Context = created = new VulkanRenderContext(new RenderContextCreateInfo
                {
                    CanvasSize = new Size(160, 160), DpiScale = 1, VSync = false,
                    NativeTarget = new Win32RenderTarget(_window, instance)
                });
                Diagnostics = Assert.IsAssignableFrom<IVulkanFrameDiagnostics>(Context);
                Assert.False(string.Equals(Context.DeviceInfo.DeviceType, "Cpu", StringComparison.OrdinalIgnoreCase), "Native pipelining tests require a hardware Vulkan device.");
                Assert.Equal((uint)samples, Context.ColorSampleCount);
                Assert.True(Context.ReadbackEnabled);
                Assert.True(Context.IsCaptureAvailable);
                var initial = Statistics();
                Assert.Equal(0ul, initial.SubmittedFrames);
                Assert.Equal(0ul, initial.CompletedFrames);
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
            _expectedSubmitted++;
            // This is only a nonblocking fence poll, never a capture or an explicit wait.
            return Statistics().SubmittedFrames;
        }

        public VulkanFrameStatistics Statistics()
        {
            var result = Diagnostics.GetFrameStatistics();
            Assert.Equal(2, result.FramesInFlight);
            Assert.Equal(_expectedSubmitted, result.SubmittedFrames);
            Assert.True(result.CompletedFrames <= result.SubmittedFrames);
            Assert.True(result.CompletedFrames >= _lastCompleted, "Completed frame counts must be monotonic.");
            Assert.InRange(result.PendingFrames, 0, 2);
            Assert.Equal(result.SubmittedFrames - result.CompletedFrames, (ulong)result.PendingFrames);
            _lastCompleted = result.CompletedFrames;
            return result;
        }

        public VulkanFrameStatistics AssertComplete()
        {
            var result = Statistics();
            Assert.Equal(result.SubmittedFrames, result.CompletedFrames);
            Assert.Equal(0, result.PendingFrames);
            return result;
        }

        public void DrainAndAssertComplete()
        {
            Diagnostics.WaitForSubmittedFrames();
            var drained = AssertComplete();
            // Repeated polling must not account for a completed fence more than once.
            Assert.Equal(drained, AssertComplete());
        }

        public void Resize(int width, int height)
        {
            Assert.True(SetWindowPos(_window, IntPtr.Zero, 0, 0, width, height, 0x0016),
                $"SetWindowPos failed: {Marshal.GetLastPInvokeError()}");
            Context.Resize(new Size(width, height), 1);
            Assert.Equal(new Size(width, height), Context.CanvasSize);
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
