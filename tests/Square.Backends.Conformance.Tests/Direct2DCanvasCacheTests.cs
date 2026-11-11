using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DirectN;
using Square.Backends.Direct2D;
using Square.Graphics;
using Square.Hosting;
using Square.Platform;
#if PLATFORM_WIN32
using Square.Platform.Win32;
#endif
using Xunit;
using Bitmap = Square.Graphics.Bitmap;

namespace Square.Backends.Conformance.Tests;

[CollectionDefinition("Direct2DCanvasCache", DisableParallelization = true)]
public sealed class Direct2DCanvasCacheCollection;

[Collection("Direct2DCanvasCache")]
[SupportedOSPlatform("windows6.1")]
public sealed class Direct2DCanvasCacheTests
{
    private static bool Enabled => OperatingSystem.IsWindowsVersionAtLeast(6, 1) &&
        Environment.GetEnvironmentVariable("SQUARE_RUN_REAL_DIRECT2D_CONFORMANCE") == "1";

    [Fact]
    [Trait("Category", "RealDirect2D")]
    public void RepeatedResourcesAndMutableGradientsMatchFreshPixelsExactly()
    {
        if (!Enabled) return;
        using var window = new TestWindow();
        var context = window.Context;
        var path = new PathGeometry().MoveTo(new Point(10.25f, 8.5f))
            .LineTo(new Point(58, 13)).ArcTo(new Rect(24, 12, 40, 30), 0, 150)
            .LineTo(new Point(12, 42)).Close();
        var clip = Box(4, 4, 84, 44);
        var linear = new LinearGradientBrush(new Point(0, 0), new Point(88, 48),
            new GradientStop(0.8f, Color.Blue), new GradientStop(0, Color.Red),
            new GradientStop(1, new Color(0, 220, 80, 150)));
        var radial = new RadialGradientBrush(new Point(100, 70), 24,
            new GradientStop(0, Color.White), new GradientStop(1, Color.Blue));

        void Draw(PathGeometry shape, PathGeometry mask, LinearGradientBrush fill, RadialGradientBrush radialFill)
        {
            context.Clear(Color.White);
            context.PushTransform(Matrix3x2.CreateTranslation(0.25f, 0.5f));
            context.PushClip(mask);
            context.FillRect(new Rect(0, 0, 96, 52), fill);
            context.FillPath(shape, Brush.FromColor(new Color(220, 120, 30, 90)));
            context.DrawPath(shape, new Pen(Brush.FromColor(Color.Black), 1.25f));
            context.PopClip();
            context.PopTransform();
            context.FillRect(new Rect(76, 52, 48, 40), radialFill);
            context.Present();
        }

        Draw(path, clip, linear, radial);
        using var cold = window.Capture();
        var paths = context.PathGeometryCreationCount;
        var gradients = context.GradientBrushCreationCount;
        var layers = context.LayerCreationCount;
        for (var i = 0; i < 3; i++) Draw(path, clip, linear, radial);
        using var warm = window.Capture();
        AssertPixelsEqual(cold, warm);
        Assert.Equal(paths, context.PathGeometryCreationCount);
        Assert.Equal(gradients, context.GradientBrushCreationCount);
        Assert.Equal(layers, context.LayerCreationCount);

        path.MoveTo(new Point(68, 8)).LineTo(new Point(84, 8)).LineTo(new Point(78, 38)).Close();
        clip.MoveTo(new Point(88, 4)).LineTo(new Point(96, 4)).LineTo(new Point(96, 48)).Close();
        linear.Start = new Point(8, 40);
        linear.End = new Point(52, 4);
        linear.SpreadMethod = GradientSpreadMethod.Reflect;
        linear.Stops[0].Offset = 0.45f;
        linear.Stops[0].Color = new Color(220, 30, 170, 160);
        linear.Stops[1] = new GradientStop(0, Color.Green);
        radial.Center = new Point(92, 64);
        radial.Radius = 11;
        radial.SpreadMethod = GradientSpreadMethod.Repeat;
        radial.Stops = [new GradientStop(0, Color.Red), new GradientStop(0.7f, new Color(20, 210, 210, 120))];
        Draw(path, clip, linear, radial);
        using var mutated = window.Capture();
        Assert.False(cold.Pixels.AsSpan().SequenceEqual(mutated.Pixels));
        Assert.Equal(paths + 2, context.PathGeometryCreationCount);
        Assert.Equal(gradients + 2, context.GradientBrushCreationCount);
        Draw(CopyPath(path), CopyPath(clip), CopyLinear(linear), CopyRadial(radial));
        using var fresh = window.Capture();
        AssertPixelsEqual(fresh, mutated);

        // Empty gradients and restoring their mutable stop arrays must not reuse stale colors.
        linear.Stops = [];
        Draw(path, clip, linear, radial);
        using var emptyCached = window.Capture();
        Draw(CopyPath(path), CopyPath(clip), CopyLinear(linear), CopyRadial(radial));
        using var emptyFresh = window.Capture();
        AssertPixelsEqual(emptyFresh, emptyCached);
        linear.Stops = [new GradientStop(0, Color.Black), new GradientStop(1, Color.White)];
        Draw(path, clip, linear, radial);
        using var restoredCached = window.Capture();
        Draw(CopyPath(path), CopyPath(clip), CopyLinear(linear), CopyRadial(radial));
        using var restoredFresh = window.Capture();
        AssertPixelsEqual(restoredFresh, restoredCached);
    }

    [Fact]
    [Trait("Category", "RealDirect2D")]
    public void ActiveClipSurvivesAppendEvictionAndNestedLayerReuse()
    {
        if (!Enabled) return;
        using var window = new TestWindow();
        var context = window.Context;
        var original = Box(8, 8, 104, 72);
        var clip = CopyPath(original);
        var inner = Box(24, 16, 64, 48);
        var green = Brush.FromColor(Color.Green);
        var red = Brush.FromColor(Color.Red);
        void Contents()
        {
            context.FillRect(new Rect(0, 0, 128, 96), red);
            context.PushLayer(new Rect(16, 16, 88, 56), 0.5f);
            context.PushClip(inner);
            context.FillRect(new Rect(16, 16, 88, 56), green);
            context.PopClip();
            context.PopLayer();
        }
        context.Clear(Color.White);
        context.PushClip(clip);
        // Replacing the cached version while an earlier version is an active mask needs two leases.
        clip.MoveTo(new Point(0, 0)).LineTo(new Point(128, 0)).LineTo(new Point(128, 96)).Close();
        context.PushClip(clip);
        context.PopClip();
        for (var i = 0; i <= Direct2DRenderContext.MaxPathCacheEntries; i++)
            context.DrawGeometry(new PathGeometry().MoveTo(new Point(-10, -10)).LineTo(new Point(-9, -9)),
                new Pen(red, 1));
        Assert.InRange(context.PathCacheCount, 1, Direct2DRenderContext.MaxPathCacheEntries);
        Contents();
        context.PopClip();
        context.Present();
        using var retained = window.Capture();
        var layers = context.LayerCreationCount;
        context.Clear(Color.White);
        context.PushClip(original);
        Contents();
        context.PopClip();
        context.Present();
        using var fresh = window.Capture();
        AssertPixelsEqual(fresh, retained);
        Assert.Equal(layers, context.LayerCreationCount);
        Assert.InRange(context.LayerPoolCount, 3, Direct2DRenderContext.MaxPooledLayers);

        // A small previous backing store must grow, and previous layer contents must be cleared.
        ((IDpiResizableRenderContext)context).Resize(context.CanvasSize, context.DpiScale);
        context.Clear(Color.White);
        context.PushClip(Box(0, 0, 2, 2));
        context.FillRect(new Rect(0, 0, 2, 2), red);
        context.PopClip();
        context.Present();
        context.Clear(Color.White);
        context.PushClip(original);
        Contents();
        context.PopClip();
        context.Present();
        using var grown = window.Capture();
        AssertPixelsEqual(fresh, grown);
    }

    [Fact]
    [Trait("Category", "RealDirect2D")]
    public void CacheBudgetsResizeTargetRecreationAndDisposeReleaseResources()
    {
        if (!Enabled) return;
        using var window = new TestWindow();
        var context = window.Context;
        var solid = Brush.FromColor(Color.Blue);
        context.Clear(Color.White);
        for (var i = 0; i < 3; i++)
        {
            var path = new PathGeometry().MoveTo(new Point(-10, -10));
            for (var j = 0; j < Direct2DRenderContext.MaxPathCacheCommands / 2; j++)
                path.LineTo(new Point(-9, -9));
            context.DrawPath(path, new Pen(solid, 1));
            Assert.InRange(context.PathCacheCommands, 0, Direct2DRenderContext.MaxPathCacheCommands);
        }
        var cacheCount = context.PathCacheCount;
        var oversized = new PathGeometry().MoveTo(new Point(-10, -10));
        for (var i = 0; i < Direct2DRenderContext.MaxPathCacheCommands; i++) oversized.LineTo(new Point(-9, -9));
        context.DrawPath(oversized, new Pen(solid, 1));
        Assert.Equal(cacheCount, context.PathCacheCount);
        for (var i = 0; i <= Direct2DRenderContext.MaxGradientCacheEntries; i++)
        {
            context.FillRect(new Rect(-10, -10, 1, 1), new LinearGradientBrush(Point.Zero, new Point(1, 1),
                new GradientStop(0, Color.White), new GradientStop(1, Color.Blue)));
        }
        Assert.InRange(context.GradientCacheCount, 1, Direct2DRenderContext.MaxGradientCacheEntries);
        for (var i = 0; i < 3; i++)
        {
            var stops = Enumerable.Range(0, Direct2DRenderContext.MaxGradientCacheStops / 2 + 1)
                .Select(j => new GradientStop(j / (float)(Direct2DRenderContext.MaxGradientCacheStops / 2), Color.Blue)).ToArray();
            context.FillRect(new Rect(-10, -10, 1, 1), new LinearGradientBrush(Point.Zero, new Point(1, 1), stops));
            Assert.InRange(context.GradientCacheStops, 0, Direct2DRenderContext.MaxGradientCacheStops);
        }
        var gradientCount = context.GradientCacheCount;
        var oversizedStops = Enumerable.Range(0, Direct2DRenderContext.MaxGradientCacheStops + 1)
            .Select(i => new GradientStop(i / (float)Direct2DRenderContext.MaxGradientCacheStops, Color.Red)).ToArray();
        context.FillRect(new Rect(-10, -10, 1, 1), new LinearGradientBrush(Point.Zero, new Point(1, 1), oversizedStops));
        Assert.Equal(gradientCount, context.GradientCacheCount);
        for (var i = 0; i < Direct2DRenderContext.MaxPooledLayers + 3; i++) context.PushLayer(new Rect(0, 0, 2, 2), 0.5f);
        for (var i = 0; i < Direct2DRenderContext.MaxPooledLayers + 3; i++) context.PopLayer();
        Assert.Equal(Direct2DRenderContext.MaxPooledLayers, context.LayerPoolCount);
        context.Present();

        var clip = Box(4, 4, 112, 80);
        var gradient = new LinearGradientBrush(Point.Zero, new Point(100, 70),
            new GradientStop(0, Color.Red), new GradientStop(1, Color.Blue));
        void Draw()
        {
            context.Clear(Color.White);
            context.PushClip(clip);
            context.FillRect(new Rect(0, 0, 128, 96), gradient);
            context.PopClip();
            context.Present();
        }
        Draw();
        ((IDpiResizableRenderContext)context).Resize(context.CanvasSize, context.DpiScale * 1.25f);
        Assert.Equal(0, context.LayerPoolCount);
        Draw();
        using var resizedCached = window.Capture();
        typeof(Direct2DRenderContext).GetMethod("HandleDrawResult", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(context, [new HRESULT(unchecked((int)0x8899000C)), "Simulated target loss"]);
        Assert.Equal(1, window.RecreationRequests);
        Assert.Equal(0, context.PathCacheCount);
        Assert.Equal(0, context.PathCacheCommands);
        Assert.Equal(0, context.GradientCacheCount);
        Assert.Equal(0, context.GradientCacheStops);
        Assert.Equal(0, context.LayerPoolCount);
        Assert.True(context.NeedsFullRedraw);
        Draw();
        using var recreated = window.Capture();
        AssertPixelsEqual(resizedCached, recreated);
        context.Dispose();
        Assert.Equal(0, context.PathCacheCount);
        Assert.Equal(0, context.GradientCacheCount);
        Assert.Equal(0, context.LayerPoolCount);
        Assert.Throws<ObjectDisposedException>(() => context.Clear(Color.White));
    }

    private static PathGeometry Box(float x, float y, float width, float height) => new PathGeometry()
        .MoveTo(new Point(x, y)).LineTo(new Point(x + width, y)).LineTo(new Point(x + width, y + height))
        .LineTo(new Point(x, y + height)).Close();

    private static PathGeometry CopyPath(PathGeometry source)
    {
        var result = new PathGeometry();
        foreach (var command in source.Commands)
        {
            switch (command)
            {
                case MoveToCmd move: result.MoveTo(move.Point); break;
                case LineToCmd line: result.LineTo(line.Point); break;
                case ArcToCmd arc: result.ArcTo(arc.Oval, arc.StartAngle, arc.SweepAngle); break;
                case CloseCmd: result.Close(); break;
            }
        }
        return result;
    }
    private static GradientStop[] CopyStops(GradientStop[] stops) => stops.Select(s => new GradientStop(s.Offset, s.Color)).ToArray();
    private static LinearGradientBrush CopyLinear(LinearGradientBrush source) => new(source.Start, source.End, CopyStops(source.Stops)) { SpreadMethod = source.SpreadMethod };
    private static RadialGradientBrush CopyRadial(RadialGradientBrush source) => new(source.Center, source.Radius, CopyStops(source.Stops)) { SpreadMethod = source.SpreadMethod };
    private static void AssertPixelsEqual(Bitmap expected, Bitmap actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.True(expected.Pixels.AsSpan().SequenceEqual(actual.Pixels), "Cached and newly created native resources produced different pixels.");
    }

    private sealed class TestWindow : IDisposable
    {
        public IPlatformHost Host { get; }
        public Direct2DRenderContext Context { get; }
        public int RecreationRequests { get; private set; }
        public TestWindow()
        {
#if PLATFORM_WIN32
            Host = new Win32PlatformFactory().CreateHost(new PlatformHostCreateInfo
            {
                Title = "Direct2D Canvas cache conformance", Width = 128, Height = 96,
                RenderBackend = "Direct2D", TitleStyle = TitleStyle.Hidden, BorderStyle = BorderStyle.None
            });
            Host.Show();
            var handle = (IntPtr)Host.GetType().GetProperty("Handle")!.GetValue(Host)!;
            Context = new Direct2DRenderContext(new RenderContextCreateInfo
            {
                CanvasSize = Host.ClientSize, DpiScale = Host.DpiScale, VSync = false,
                RequestRender = () => RecreationRequests++
            }, new Win32RenderTarget(handle, IntPtr.Zero));
            Context.Clear(Color.White);
            Context.Present();
            Host.ShowAfterFirstFrame();
            Assert.True(SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0043));
#else
            throw new PlatformNotSupportedException();
#endif
        }
        public Bitmap Capture()
        {
            Thread.Sleep(160);
            return VisibleHwndCapture.Capture(Host);
        }
        public void Dispose() { Context.Dispose(); Host.Dispose(); }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
}
