using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Square.Graphics;
using Xunit;

namespace Square.Backends.Vulkan.Tests;

public sealed class VulkanLayerPoolPolicyTests
{
    private static readonly VulkanLayerSpec Spec = new(Format.B8G8R8A8Unorm, SampleCountFlags.Count4Bit, 64, 64);

    [Theory]
    [InlineData(3)]
    [InlineData(17)]
    [InlineData(48)]
    public void RetainsTheObservedWorkingSetWithinBothBudgets(int count)
    {
        var pool = new VulkanLayerResourcePool<Token>(256, 4096, token => token.Releases++);
        var tokens = Enumerable.Range(0, count).Select(_ => new Token()).ToArray();
        for (var frame = 0; frame < 3; frame++)
        {
            foreach (var token in tokens) pool.Return(Spec, 8, token);
            Assert.Equal(count, pool.Count);
            Assert.Equal((ulong)count * 8, pool.Bytes);
            var rented = new HashSet<Token>();
            for (var index = 0; index < count; index++)
            {
                Assert.True(pool.TryRent(Spec, out var token));
                Assert.True(rented.Add(token));
                Assert.Equal(0, token.Releases);
            }
            Assert.Equal(count, rented.Count);
            Assert.Equal(0, pool.Count);
            Assert.Equal(0UL, pool.Bytes);
        }
        Assert.Equal(count * 3, pool.HitCount);
    }

    [Fact]
    public void EveryResourceSpecificationFieldMustMatch()
    {
        var token = new Token();
        var pool = new VulkanLayerResourcePool<Token>(8, 1000, value => value.Releases++);
        pool.Return(Spec, 100, token);
        foreach (var other in new[] { Spec with { Format = Format.R8G8B8A8Unorm }, Spec with { Samples = SampleCountFlags.Count1Bit },
            Spec with { Width = 32 }, Spec with { Height = 32 } })
            Assert.False(pool.TryRent(other, out _));
        Assert.Equal(1, pool.Count);
        Assert.True(pool.TryRent(Spec, out var same));
        Assert.Same(token, same);
        Assert.Equal(0, token.Releases);
    }

    [Fact]
    public void ByteBudgetEvictsTheLeastRecentlyReturnedSpecification()
    {
        var pool = new VulkanLayerResourcePool<Token>(10, 100, token => token.Releases++);
        var a = new Token(); var b = new Token(); var c = new Token();
        var specB = Spec with { Width = 48 };
        var specC = Spec with { Height = 48 };
        pool.Return(Spec, 40, a);
        pool.Return(specB, 40, b);
        Assert.True(pool.TryRent(Spec, out var borrowed));
        pool.Return(Spec, 40, borrowed); // A is more recently used than B.
        pool.Return(specC, 40, c);
        Assert.Equal(1, b.Releases);
        Assert.Equal(0, a.Releases);
        Assert.Equal(0, c.Releases);
        Assert.Equal(80UL, pool.Bytes);
        pool.Clear(); pool.Clear();
        Assert.Equal(1, a.Releases);
        Assert.Equal(1, b.Releases);
        Assert.Equal(1, c.Releases);
        Assert.Equal(0UL, pool.Bytes);
    }

    [Fact]
    public void EntryCeilingBoundsTinyResourcesEvenWhenBytesFit()
    {
        var pool = new VulkanLayerResourcePool<Token>(2, 1000, token => token.Releases++);
        var a = new Token(); var b = new Token(); var c = new Token();
        pool.Return(Spec, 1, a); pool.Return(Spec, 1, b); pool.Return(Spec, 1, c);
        Assert.Equal(1, a.Releases);
        Assert.Equal(2, pool.Count);
        Assert.Equal(2UL, pool.Bytes);
        pool.Clear();
        Assert.Equal(1, b.Releases); Assert.Equal(1, c.Releases);
    }

    [Theory]
    [InlineData(0, 1024UL)]
    [InlineData(8, 1UL)]
    public void DisabledAndOversizedResourcesAreReleasedExactlyOnce(int maxEntries, ulong maxBytes)
    {
        var pool = new VulkanLayerResourcePool<Token>(maxEntries, maxBytes, token => token.Releases++);
        var token = new Token();
        pool.Return(Spec, 8, token);
        Assert.Equal(1, token.Releases);
        pool.Clear();
        Assert.Equal(1, token.Releases);
        Assert.Equal(0, pool.Count);
        Assert.Equal(0UL, pool.Bytes);
    }

    [Fact]
    public void SeparateFrameOwnersNeverBorrowEachOthersIdleResources()
    {
        var first = new VulkanLayerResourcePool<Token>(8, 1000, token => token.Releases++);
        var second = new VulkanLayerResourcePool<Token>(8, 1000, token => token.Releases++);
        var token = new Token(); first.Return(Spec, 8, token);
        Assert.False(second.TryRent(Spec, out _));
        Assert.True(first.TryRent(Spec, out var borrowed));
        Assert.Same(token, borrowed);
        first.Return(Spec, 8, borrowed);
        first.Clear(); second.Clear();
        Assert.Equal(1, token.Releases);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [Trait("Category", "RealVulkanGpu")]
    public void WarmNativeWorkingSetReusesResourcesAndResizeReleasesThem(int samples)
    {
        if (!VulkanOptimizationGpuScope.Enabled) return;
        using var environment = new VulkanOptimizationGpuScope(samples);
        using var fixture = new VulkanOptimizationGpuFixture();
        var context = fixture.Context;
        Assert.Equal((uint)samples, context.ColorSampleCount);
        Assert.True(context.ReadbackEnabled);
        var brush = Brush.FromColor(new Color(50, 120, 220, 160));
        void DrawLayers()
        {
            for (var index = 0; index < 24; index++)
            {
                var bounds = new Rect(4 + index % 6 * 24, 4 + index / 6 * 24, 20, 20);
                context.PushLayer(bounds, .6f);
                context.FillRect(bounds, brush); context.FillRect(bounds, brush);
                context.PopLayer();
            }
        }
        for (var frame = 0; frame < 4; frame++) using (fixture.Draw(DrawLayers)) { }
        var batches = fixture.Batches();
        var created = batches.Sum(batch => batch.LayerCreateCount);
        Assert.True(created >= 24);
        for (var frame = 0; frame < 4; frame++) using (fixture.Draw(DrawLayers)) { }
        Assert.Equal(created, batches.Sum(batch => batch.LayerCreateCount));
        Assert.Equal(0, batches.Sum(batch => batch.LayerDestroyCount));
        Assert.True(batches.Sum(batch => batch.LayerPoolHitCount) > 0);
        fixture.Resize(128, 128);
        Assert.Equal(created, batches.Sum(batch => batch.LayerDestroyCount));
        foreach (var batch in batches)
        {
            Assert.Equal(0, batch.LayerPoolCount);
            Assert.Equal(0UL, batch.LayerPoolBytes);
        }
        using (fixture.Draw(DrawLayers)) { }
        fixture.Dispose();
        Assert.Equal(batches.Sum(batch => batch.LayerCreateCount), batches.Sum(batch => batch.LayerDestroyCount));
    }

    private sealed class Token { public int Releases; }
}

internal sealed class VulkanOptimizationGpuScope : IDisposable
{
    public static bool Enabled => OperatingSystem.IsWindows() &&
        Environment.GetEnvironmentVariable("SQUARE_RUN_REAL_VULKAN_GPU_CONFORMANCE") == "1";
    private readonly string? _readback = Environment.GetEnvironmentVariable("SQUARE_VULKAN_READBACK");
    private readonly string? _samples = Environment.GetEnvironmentVariable("SQUARE_VULKAN_MSAA");
    private readonly string? _validation = Environment.GetEnvironmentVariable("SQUARE_VULKAN_VALIDATION");
    public VulkanOptimizationGpuScope(int samples)
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

internal sealed class VulkanOptimizationGpuFixture : IDisposable
{
    private IntPtr _window;
    public VulkanRenderContext Context { get; }
    public VulkanOptimizationGpuFixture()
    {
        var instance = GetModuleHandle(null);
        _window = CreateWindowEx(0, "STATIC", "Vulkan optimization test", 0x80000000u,
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
            Assert.False(string.Equals(Context.DeviceInfo.DeviceType, "Cpu", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            try { created?.Dispose(); }
            finally { DestroyWindow(_window); _window = IntPtr.Zero; }
            throw;
        }
    }
    public Bitmap Draw(Action draw)
    {
        Context.Clear(Color.Black); draw(); Context.Present();
        return Context.CaptureSubmittedFrame(((IVulkanFrameDiagnostics)Context).GetFrameStatistics().SubmittedFrames);
    }
    public VulkanBatchRenderer[] Batches() => ((Array)typeof(VulkanRenderContext)
        .GetField("_frames", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Context)!).Cast<object>()
        .Select(frame => (VulkanBatchRenderer)frame.GetType().GetField("Batch")!.GetValue(frame)!).ToArray();
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
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandle(string? module);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string name, string title, uint style, int x, int y,
        int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);
}
