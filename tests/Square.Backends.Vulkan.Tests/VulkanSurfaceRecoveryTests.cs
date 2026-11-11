using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Square.Graphics;
using Xunit;

namespace Square.Backends.Vulkan.Tests;

[CollectionDefinition("VulkanSurfaceRecovery", DisableParallelization = true)]
public sealed class VulkanSurfaceRecoveryCollection { }

[Collection("VulkanSurfaceRecovery")]
public sealed class VulkanSurfaceRecoveryTests
{
    private static bool Enabled => OperatingSystem.IsWindows() &&
        Environment.GetEnvironmentVariable("SQUARE_RUN_REAL_VULKAN_GPU_CONFORMANCE") == "1";

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [Trait("Category", "RealVulkanGpu")]
    public void RepeatedSurfaceRecoveryRetiresImagesAndResumesRendering(int samples)
    {
        if (!Enabled) return;
        using var environment = new GpuEnvironment(samples);
        using var fixture = new HiddenContext(samples);
        for (var i = 0; i < 4; i++)
        {
            fixture.DrawAndAssert(Color.Red);
            var generation = fixture.Swapchain.Generation;
            var oldSurface = fixture.Device.Surface;
            fixture.Swapchain.RecoverSurface();
            AssertRetired(fixture.Swapchain);
            Assert.NotEqual(0ul, fixture.Device.Surface.Handle);
            // The replacement is created while the old surface is still owned, so
            // these two simultaneously live surface handles cannot be the same.
            Assert.NotEqual(oldSurface.Handle, fixture.Device.Surface.Handle);
            fixture.DrawAndAssert(Color.Blue);
            Assert.True(fixture.Swapchain.Generation > generation);
            Assert.True(fixture.Swapchain.IsAvailable);
        }
        fixture.Context.Dispose();
        Assert.Throws<ObjectDisposedException>(() => fixture.Swapchain.RecoverSurface());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [Trait("Category", "RealVulkanGpu")]
    public void FailedSurfaceReplacementRetainsOwnershipAndCanRetry(int samples)
    {
        if (!Enabled) return;
        using var environment = new GpuEnvironment(samples);
        using var fixture = new HiddenContext(samples);
        fixture.DrawAndAssert(Color.Red);
        var oldSurface = fixture.Device.Surface;
        var nativeTarget = typeof(VulkanDevice).GetField("_nativeTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var originalTarget = nativeTarget.GetValue(fixture.Device);
        try
        {
            // Fail deterministically before any native CreateSurface call. This tests
            // the real teardown path without passing an invalid HWND to the driver.
            nativeTarget.SetValue(fixture.Device, new UnsupportedTarget());
            Assert.Throws<VulkanException>(() => fixture.Swapchain.RecoverSurface());
            AssertRetired(fixture.Swapchain);
            Assert.Equal(oldSurface.Handle, fixture.Device.Surface.Handle);
        }
        finally
        {
            nativeTarget.SetValue(fixture.Device, originalTarget);
        }
        fixture.Swapchain.RecoverSurface();
        fixture.DrawAndAssert(Color.Green);
        Assert.True(fixture.Swapchain.IsAvailable);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [Trait("Category", "RealVulkanGpu")]
    public void SubmittedFrameClosesWhenPresentationRecreationThrows(int samples)
    {
        if (!Enabled) return;
        using var environment = new GpuEnvironment(samples);
        using var fixture = new HiddenContext(samples);
        fixture.DrawAndAssert(Color.Red);
        fixture.Context.Clear(Color.Black);
        fixture.Context.FillRect(new Rect(12, 12, 48, 32), new SolidColorBrush(Color.Blue));
        var submitted = fixture.Context.GetFrameStatistics().SubmittedFrames;
        var disposed = typeof(VulkanSwapchain).GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var recreate = typeof(VulkanSwapchain).GetField("_recreateAfterPresent", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            // QueueSubmit and QueuePresent still use valid native objects. Force only
            // the subsequent managed Recreate guard to throw, exercising a genuine
            // post-submit failure without passing invalid native handles to a driver.
            recreate.SetValue(fixture.Swapchain, true);
            disposed.SetValue(fixture.Swapchain, true);
            Assert.Throws<ObjectDisposedException>(() => fixture.Context.Present());
        }
        finally
        {
            disposed.SetValue(fixture.Swapchain, false);
        }
        Assert.Equal(submitted + 1, fixture.Context.GetFrameStatistics().SubmittedFrames);
        var recording = typeof(VulkanRenderContext).GetField("_frameStarted", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.False((bool)recording.GetValue(fixture.Context)!);
        fixture.Swapchain.RecoverSurface();
        fixture.DrawAndAssert(Color.Green);
    }

    private static void AssertRetired(VulkanSwapchain swapchain)
    {
        Assert.False(swapchain.IsAvailable);
        Assert.Equal(0ul, swapchain.Swapchain.Handle);
        Assert.Equal(0u, swapchain.ImageCount);
        Assert.Empty(swapchain.Images);
        Assert.Empty(swapchain.ImageViews);
        Assert.Empty(swapchain.Framebuffers);
    }

    private sealed class UnsupportedTarget : INativeRenderTarget
    {
        public NativeRenderTargetKind Kind => NativeRenderTargetKind.Win32;
        public IntPtr WindowHandle => IntPtr.Zero;
        public IntPtr DisplayHandle => IntPtr.Zero;
        public int Screen => 0;
    }

    private sealed class HiddenContext : IDisposable
    {
        private IntPtr _window;
        public VulkanRenderContext Context { get; }
        public VulkanDevice Device { get; }
        public VulkanSwapchain Swapchain { get; }
        public HiddenContext(int samples)
        {
            var instance = GetModuleHandle(null);
            _window = CreateWindowEx(0, "STATIC", "Vulkan surface recovery test", 0x80000000u,
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
                Device = ReadField<VulkanDevice>(Context, "_device");
                Swapchain = ReadField<VulkanSwapchain>(Context, "_swapchain");
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
        public void DrawAndAssert(Color color)
        {
            Context.Clear(Color.Black);
            Context.FillRect(new Rect(12, 12, 48, 32), new SolidColorBrush(color));
            Context.Present();
            var serial = Context.GetFrameStatistics().SubmittedFrames;
            using var pixels = Context.CaptureSubmittedFrame(serial);
            var pixel = pixels.GetPixel(24, 24);
            Assert.Equal(color.B, pixel[0]);
            Assert.Equal(color.G, pixel[1]);
            Assert.Equal(color.R, pixel[2]);
            Assert.Equal((byte)255, pixel[3]);
            Context.WaitForSubmittedFrames();
        }
        public void Dispose()
        {
            if (_window == IntPtr.Zero) return;
            try { Context.Dispose(); }
            finally { DestroyWindow(_window); _window = IntPtr.Zero; }
        }
        private static T ReadField<T>(object owner, string name) =>
            (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
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
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);
}
