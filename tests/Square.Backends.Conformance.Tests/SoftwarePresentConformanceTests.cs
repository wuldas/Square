using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using Square.Graphics;
using Square.Hosting;
using Square.Platform;
#if PLATFORM_WIN32
using Square.Platform.Win32;
#endif
using Xunit;

namespace Square.Backends.Conformance.Tests;

[CollectionDefinition("SoftwarePresent", DisableParallelization = true)]
public sealed class SoftwarePresentCollection;

[Collection("SoftwarePresent")]
public sealed class SoftwarePresentConformanceTests
{
    [Theory]
    [InlineData(SoftwareRenderSurfaceKind.Auto)]
    [InlineData(SoftwareRenderSurfaceKind.Bitmap)]
    [Trait("Category", "RealSoftwarePresent")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields | DynamicallyAccessedMemberTypes.PublicProperties,
        "Square.Platform.Win32.Win32Host", "Square.Platform.Win32")]
    public void SoftwareFramesFlushOnceWhileEmptyAndPaintDoNotWait(SoftwareRenderSurfaceKind surface)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("SQUARE_RUN_REAL_SOFTWARE_PRESENT") != "1") return;
#if PLATFORM_WIN32
        RenderBackendRegistry.Register(new Square.Backends.RenderBackendFactory());
        using var host = new Win32PlatformFactory().CreateHost(new PlatformHostCreateInfo
        {
            Title = "Software present handoff", Width = 128, Height = 96, RenderBackend = "Software",
            SoftwareSurface = surface, TitleStyle = TitleStyle.Hidden, BorderStyle = BorderStyle.None
        });
        // Show creates the HWND and client metrics; visibility is deferred until ShowAfterFirstFrame.
        host.Show();
        var hwnd = (IntPtr)host.GetType().GetProperty("Handle")!.GetValue(host)!;
        Assert.NotEqual(IntPtr.Zero, hwnd);
        using var context = host.CreateRenderContext();
        // Hidden presents must hand off GDI memory without waiting on a compositor.
        var beforeShow = Count(host);
        context.Clear(Color.Black); context.Present();
        Assert.Equal(beforeShow, Count(host));
        host.ShowAfterFirstFrame();
        Position(hwnd, (int)MathF.Round(128 * host.DpiScale), (int)MathF.Round(96 * host.DpiScale));
        var beforeInitialPaint = Count(host);
        Assert.True(UpdateWindow(hwnd)); // Settle the initial Position paint before drawing the measured blue frame.
        Assert.Equal(beforeInitialPaint, Count(host));
        context.Clear(Color.Blue);
        var before = Count(host);
        context.Present();
        Assert.Equal(before + 1, Count(host));
        AssertSuccess(host, surface, "full");
        RecordPixelOrigins(host, context, hwnd);
        CaptureColor(host, 8, 8, Color.Blue);

        context.FillRect(new Rect(4, 4, 20, 16), new SolidColorBrush(Color.Red));
        context.FillRect(new Rect(76, 48, 20, 16), new SolidColorBrush(Color.Green));
        before = Count(host);
        context.Present(new[] { new Rect(4, 4, 20, 16), new Rect(76, 48, 20, 16) });
        Assert.Equal(before + 1, Count(host));
        AssertSuccess(host, surface, "dirty union");
        CaptureColor(host, 8, 8, Color.Red);
        CaptureColor(host, 80, 52, Color.Green);
        CaptureColor(host, 52, 32, Color.Blue);

        before = Count(host);
        context.Present(Array.Empty<Rect>());
        Assert.Equal(before, Count(host));
        Assert.True(InvalidateRect(hwnd, IntPtr.Zero, false));
        Assert.True(UpdateWindow(hwnd));
        Assert.Equal(before, Count(host)); // Real WM_PAINT may flush GDI, never DwmFlush again.
        CaptureColor(host, 8, 8, Color.Red);

        Position(hwnd, (int)MathF.Round(160 * host.DpiScale), (int)MathF.Round(112 * host.DpiScale));
        ((IDpiResizableRenderContext)context).Resize(host.ClientSize, host.DpiScale);
        context.Clear(Color.Red);
        before = Count(host);
        context.Present();
        Assert.Equal(before + 1, Count(host));
        AssertSuccess(host, surface, "resize");
        CaptureColor(host, 32, 32, Color.Red);
        context.Dispose(); context.Dispose();
        host.Dispose(); host.Dispose();
#else
        throw new PlatformNotSupportedException($"Software-present conformance for {surface} requires a Win32 build.");
#endif
    }

    private static long Count(IPlatformHost host) => Field<long>(host, "_softwareDwmFlushCount");
    private static T Field<T>(IPlatformHost host, string name) => (T)(host.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host)
        ?? throw new InvalidOperationException("Missing rooted software-present diagnostic: " + name));
    private static void AssertSuccess(IPlatformHost host, SoftwareRenderSurfaceKind surface, string phase)
    {
        var hr = Field<int>(host, "_softwareDwmFlushHResult");
        var elapsed = Field<long>(host, "_softwareDwmFlushLastTicks");
        Console.WriteLine($"SoftwarePresent surface={surface} phase={phase} calls={Count(host)} HRESULT=0x{unchecked((uint)hr):X8} lastWaitMs={elapsed * 1000d / Stopwatch.Frequency:R} cumulativeTicks={Field<long>(host, "_softwareDwmFlushTicks")}");
        Assert.True(hr == 0, $"DwmFlush failed: 0x{unchecked((uint)hr):X8}");
        Assert.True(elapsed >= 0);
    }
    private static void RecordPixelOrigins(IPlatformHost host, IRenderContext context, IntPtr hwnd)
    {
        var x = (int)MathF.Round(8 * host.DpiScale);
        var y = (int)MathF.Round(8 * host.DpiScale);
        using var cpu = ((IRenderBitmapSource)context).CaptureBitmap();
        var cpuPixel = cpu.GetPixel(x, y).ToArray();
        var windowDc = GetDC(hwnd);
        uint windowPixel;
        try { windowPixel = windowDc == IntPtr.Zero ? uint.MaxValue : GetPixel(windowDc, x, y); }
        finally { if (windowDc != IntPtr.Zero) ReleaseDC(hwnd, windowDc); }
        var haveWindow = GetWindowRect(hwnd, out var window);
        var haveClient = GetClientRect(hwnd, out var client);
        Console.WriteLine($"SoftwarePixelOrigins hwnd=0x{hwnd.ToInt64():X} at=({x},{y}) CPU={cpu.Width}x{cpu.Height} BGRA=[{string.Join(",", cpuPixel)}] WindowDC_COLORREF=0x{windowPixel:X8} windowRect={haveWindow}:({window.Left},{window.Top},{window.Right},{window.Bottom}) clientRect={haveClient}:({client.Left},{client.Top},{client.Right},{client.Bottom}) hostLogical={host.ClientSize} dpi={host.DpiScale}");
    }

    private static void CaptureColor(IPlatformHost host, float x, float y, Color expected)
    {
        Thread.Sleep(100);
        using var capture = VisibleHwndCapture.Capture(host);
        var pixel = capture.GetPixel((int)MathF.Round(x * host.DpiScale), (int)MathF.Round(y * host.DpiScale));
        Assert.Equal(new byte[] { expected.B, expected.G, expected.R, 255 }, pixel.ToArray());
    }
    private static void Position(IntPtr hwnd, int width, int height)
    {
        var succeeded = SetWindowPos(hwnd, new IntPtr(-1), 32, 32, width, height, 0x0050);
        var error = succeeded ? 0 : Marshal.GetLastPInvokeError();
        Assert.True(succeeded, $"SetWindowPos hwnd=0x{hwnd.ToInt64():X} size={width}x{height} Win32Error={error}");
    }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr window, IntPtr rect, bool erase);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(IntPtr window);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
}
