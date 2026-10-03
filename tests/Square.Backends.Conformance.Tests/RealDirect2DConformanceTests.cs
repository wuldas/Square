using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Square.Backends.Direct2D;
using Square.Graphics;
using Square.Hosting;
using Square.Platform;
using Square.Rendering;
#if PLATFORM_WIN32
using Square.Platform.Win32;
#endif
using Xunit;

namespace Square.Backends.Conformance.Tests;

public sealed class RealDirect2DConformanceTests
{
    [Fact]
    [Trait("Category", "RealDirect2D")]
    [SupportedOSPlatform("windows6.1")]
    public void WindowedContextPresentsActualDirect2DPixels()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("SQUARE_RUN_REAL_DIRECT2D_CONFORMANCE"),
                "1",
                StringComparison.Ordinal))
            return;

#if PLATFORM_WIN32
        var platformFactory = new Win32PlatformFactory();
        var backendFactory = new Direct2DBackendFactory();
        RenderBackendRegistry.Register(backendFactory);
        using var textLayoutScope = TextLayoutProviderContext.Push(backendFactory.TextLayoutProvider);
        using var host = platformFactory.CreateHost(new PlatformHostCreateInfo
        {
            Title = "Square Direct2D conformance",
            Width = 128,
            Height = 96,
            RenderBackend = "Direct2D",
            TitleStyle = TitleStyle.Hidden,
            BorderStyle = BorderStyle.None
        });
        host.Show();
        MakeWindowVisible(host);

        using var context = host.CreateRenderContext();
        var direct2D = Assert.IsType<Direct2DRenderContext>(context);
        Assert.True(context.SupportsPartialRendering);
        Assert.True(context.NeedsFullRedraw);
        Assert.IsNotAssignableFrom<IRenderBitmapSource>(context);
        using var image = new Bitmap(1, 1);
        image.SetPixels([255, 0, 0, 255]);

        DrawFrame();
        context.Present();
        Assert.False(context.NeedsFullRedraw);

        Assert.Equal(1, direct2D.ImageBitmapCreationCount);
        Assert.Equal(1, direct2D.ImageCacheCount);
        Assert.Equal(4, direct2D.ImageCacheBytes);
        Assert.InRange(direct2D.BitmapUploadBufferLength, 4, 256 * 1024);
        host.ShowAfterFirstFrame();
        ((IDpiResizableRenderContext)context).Resize(context.CanvasSize, context.DpiScale);
        Assert.True(context.NeedsFullRedraw);
        image.SetPixels([255, 0, 255, 255]);
        DrawFrame();
        context.Present();
        Assert.Equal(1, direct2D.ImageBitmapCreationCount);
        Assert.False(context.NeedsFullRedraw);

        AllowComposition();
        using var bitmap = Capture(host);
        var redPixels = CountPixels(bitmap, static pixel =>
            pixel[2] > 220 && pixel[1] < 50 && pixel[0] < 50);
        var bluePixels = CountPixels(bitmap, static pixel =>
            pixel[0] > 180 && pixel[1] < 100 && pixel[2] < 100);
        var greenPixels = CountPixels(bitmap, static pixel =>
            pixel[1] > 100 && pixel[0] < 120 && pixel[2] < 120);
        var blackPixels = CountPixels(bitmap, static pixel =>
            pixel[0] < 25 && pixel[1] < 25 && pixel[2] < 25);
        var magentaPixels = CountPixels(bitmap, static pixel =>
            pixel[0] > 180 && pixel[1] < 80 && pixel[2] > 180);
        Assert.True(redPixels > 200,
            "The actual Win32 window capture did not contain the Direct2D red rectangle. " +
            DescribeBitmap(bitmap));
        Assert.True(bluePixels > 100, DescribeBitmap(bitmap));
        Assert.True(greenPixels > 100, DescribeBitmap(bitmap));
        Assert.True(blackPixels > 20, DescribeBitmap(bitmap));
        Assert.True(magentaPixels > 50, DescribeBitmap(bitmap));
        AssertPixelNear(bitmap, host.DpiScale, 90, 30, 255, 0, 0, 10);
        AssertPixelNear(bitmap, host.DpiScale, 72, 24, 255, 0, 255, 10);
        AssertPixelNear(bitmap, host.DpiScale, 22, 34, 0, 0, 255, 10);
        AssertPixelNear(bitmap, host.DpiScale, 46, 24, 128, 255, 128, 12);
        AssertPixelNear(bitmap, host.DpiScale, 12, 76, 255, 128, 0, 12);
        AssertPixelNear(bitmap, host.DpiScale, 8, 76, 255, 255, 255, 8);
        AssertPixelNear(bitmap, host.DpiScale, 55, 65, 0, 255, 255, 12);
        AssertPixelNear(bitmap, host.DpiScale, 85, 65, 255, 255, 127, 16);
        Assert.True(CountDarkPixels(bitmap, host.DpiScale, new Rect(66, 36, 20, 18)) > 5,
            "Direct2D text coverage did not produce ink in the expected text region.");
        Assert.True(CountDarkPixels(bitmap, host.DpiScale, new Rect(96, 50, 20, 20)) > 2,
            "Direct2D supplementary-rune fallback did not produce visible ink.");
        Assert.True(CountDarkPixels(bitmap, host.DpiScale, new Rect(34, 70, 60, 20)) > 8,
            "DirectWrite descender ink did not reach the expected lower text region.");

        context.Clear(Color.White);
        for (var index = 0; index < 17; index++)
        {
            using var stressImage = new Bitmap(1024, 1024);
            stressImage.GetPixel(0, 0).Fill(255);
            stressImage.MarkDirty();
            context.DrawImage(stressImage, new Rect(index, 0, 1, 1));
        }
        context.Present();
        Assert.Equal(0, direct2D.ImageCacheBytes);
        Assert.Equal(0, direct2D.ImageCacheCount);
        Assert.InRange(direct2D.BitmapUploadBufferLength, 4, 256 * 1024);

        using (var oversizedImage = new Bitmap(4097, 4097))
        {
            oversizedImage.MarkDirty();
            context.Clear(Color.White);
            context.DrawImage(oversizedImage, new Rect(0, 0, 1, 1));
            Assert.Equal(0, direct2D.ImageCacheCount);
            Assert.Equal(0, direct2D.ImageCacheBytes);
            Assert.InRange(direct2D.BitmapUploadBufferLength, 4, 256 * 1024);
            context.Present();
        }

        void DrawFrame()
        {
            context.Clear(Color.White);
            context.PushTransform(Matrix3x2.CreateTranslation(2, 2));
            context.PushClip(new Rect(0, 0, context.CanvasSize.Width - 4, context.CanvasSize.Height - 4));
            context.PushClip(new RoundedRectGeometry(
                new Rect(0, 0, context.CanvasSize.Width - 4, context.CanvasSize.Height - 4),
                4,
                4));
            Assert.Throws<InvalidOperationException>(() => context.PopLayer());
            Assert.Throws<InvalidOperationException>(() => context.Clear(Color.Black));
            context.Flush();
            context.FillRect(
                new Rect(0, 0, context.CanvasSize.Width - 4, 12),
                new LinearGradientBrush(
                    new Point(0, 0),
                    new Point(context.CanvasSize.Width - 4, 0),
                    new GradientStop(0, Color.Blue),
                    new GradientStop(1, Color.Green)));
            context.FillGeometry(
                new EllipseGeometry(new Point(20, 32), 10, 8),
                Brush.FromColor(Color.Blue));
            context.DrawPath(
                PathGeometry.Create()
                    .MoveTo(new Point(4, 55))
                    .ArcTo(new Rect(4, 45, 28, 20), 180, 180),
                new Pen(Brush.FromColor(Color.Black), 2, new StrokeStyle
                {
                    Cap = LineCap.Round,
                    Join = LineJoin.Round,
                    DashArray = [3, 2]
                }));
            context.PushLayer(new Rect(34, 18, 26, 20), 0.5f);
            Assert.Throws<InvalidOperationException>(() => context.PopClip());
            context.FillRect(new Rect(34, 18, 20, 20), Brush.FromColor(Color.Green));
            context.FillRect(new Rect(40, 18, 20, 20), Brush.FromColor(Color.Green));
            context.Flush();
            context.PopLayer();
            context.DrawImage(image, new Rect(66, 18, 12, 12));
            context.DrawText(
                new TextLayout("D2D", new Font("Segoe UI", 12)),
                new Point(66, 36),
                Brush.FromColor(Color.Black));
            context.DrawText(
                new TextLayout("😀", new Font("Segoe UI", 16)),
                new Point(96, 50),
                Brush.FromColor(Color.Black));
            context.FillRect(
                new Rect(84, 22, 28, 20),
                Brush.FromColor(Color.Red));
            context.PopClip();
            context.PopClip();
            context.PopTransform();
            context.PushTransform(Matrix3x2.CreateTranslation(-180, 0));
            context.PushLayer(new Rect(260, 60, 20, 16), 0.5f);
            context.FillRect(new Rect(260, 60, 20, 16), Brush.FromColor(Color.FromRgb(255, 255, 0)));
            context.PopLayer();
            context.PopTransform();
            context.PushTransform(Matrix3x2.CreateTranslation(10, 66));
            context.PushClip(new RectGeometry(new Rect(0, 0, 20, 20)));
            context.FillRect(new Rect(-10, -10, 40, 40), Brush.FromColor(Color.FromRgb(255, 128, 0)));
            context.PopClip();
            context.PopTransform();
            context.DrawText(
                new TextLayout("gypq", new Font("Segoe UI", 24)),
                new Point(34, 55),
                Brush.FromColor(Color.Black));
            context.PushTransform(Matrix3x2.CreateTranslation(-180, 0));
            context.PushClip(new RoundedRectGeometry(new Rect(230, 60, 20, 16), 4, 4));
            context.FillRect(new Rect(230, 60, 20, 16), Brush.FromColor(Color.FromRgb(0, 255, 255)));
            context.PopClip();
            context.PopTransform();
        }
#else
        throw new PlatformNotSupportedException("Real Direct2D conformance requires Win32.");
#endif
    }

    [Fact]
    [Trait("Category", "RealDirect2D")]
    [SupportedOSPlatform("windows6.1")]
    public void WindowedContextRetainsPixelsAcrossPartialPresentsOcclusionAndResize()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("SQUARE_RUN_REAL_DIRECT2D_CONFORMANCE"),
                "1",
                StringComparison.Ordinal))
            return;

#if PLATFORM_WIN32
        var platformFactory = new Win32PlatformFactory();
        var backendFactory = new Direct2DBackendFactory();
        RenderBackendRegistry.Register(backendFactory);
        using var textLayoutScope = TextLayoutProviderContext.Push(backendFactory.TextLayoutProvider);
        using var host = platformFactory.CreateHost(new PlatformHostCreateInfo
        {
            Title = "Square Direct2D retention conformance",
            Width = 128,
            Height = 96,
            RenderBackend = "Direct2D",
            TitleStyle = TitleStyle.Hidden,
            BorderStyle = BorderStyle.None
        });
        host.Show();
        MakeWindowVisible(host);

        using var context = host.CreateRenderContext();
        var direct2D = Assert.IsType<Direct2DRenderContext>(context);
        var dpi = host.DpiScale;
        Assert.True(context.SupportsPartialRendering);
        Assert.True(context.NeedsFullRedraw);

        var blueBrush = Brush.FromColor(Color.FromRgb(0, 0, 255));
        var leafGreen = Color.FromRgb(0, 255, 0);
        var restoreBrush = Brush.FromColor(leafGreen);
        var rotatedBrush = Brush.FromColor(Color.FromRgb(255, 128, 0));

        // 场景：静态蓝/叶透明绿/组透明绿/还原绿 + 平移叶子（快路径）+ 旋转矩形（回退路径）。
        // 坐标均取 4 的倍数逻辑像素，保证在 100%/125%/150%/175%/200% DPI 下物理对齐。
        void FillLeaf(Rect rect, Color color, float opacity)
        {
            if (direct2D.TryFillSolidLeafOpacity(rect, color, opacity)) return;
            context.PushLayer(rect, opacity);
            context.FillRect(rect, Brush.FromColor(color));
            context.PopLayer();
        }

        void DrawContent(int step)
        {
            context.FillRect(new Rect(8, 64, 24, 24), blueBrush);
            FillLeaf(new Rect(40, 64, 16, 16), leafGreen, 0.5f);
            context.PushLayer(new Rect(64, 64, 16, 16), 0.5f);
            context.FillRect(new Rect(64, 64, 16, 16), restoreBrush);
            context.PopLayer();
            context.FillRect(new Rect(88, 64, 16, 16), restoreBrush);
            context.PushTransform(Matrix3x2.CreateTranslation(8 * step, 32));
            FillLeaf(new Rect(0, 0, 16, 16), Color.FromRgb(255, 0, 255), 0.75f);
            context.PopTransform();
            context.PushTransform(
                Matrix3x2.CreateRotation(step * MathF.PI / 9f) * Matrix3x2.CreateTranslation(64, 16));
            context.FillRect(new Rect(-8, -8, 16, 16), rotatedBrush);
            context.PopTransform();
        }

        void DrawScene(int step)
        {
            context.Clear(Color.White);
            DrawContent(step);
        }

        void DrawPartialScene(int step, params Rect[] logicalDamage)
        {
            foreach (var dirty in logicalDamage)
            {
                context.PushClip(dirty);
                context.Clear(Color.White, dirty);
                DrawContent(step);
                context.PopClip();
            }
        }

        static Rect Physical(Rect logical, float scale) => new(
            MathF.Floor(logical.Left * scale) - 2f,
            MathF.Floor(logical.Top * scale) - 2f,
            MathF.Ceiling(logical.Right * scale) - MathF.Floor(logical.Left * scale) + 4f,
            MathF.Ceiling(logical.Bottom * scale) - MathF.Floor(logical.Top * scale) + 4f);

        // 旋转矩形围绕 (64,16) 转动，包围盒恒为 (52,4,24,24)；平移叶子逐帧右移 8 逻辑像素。
        var rotatedDirty = Physical(new Rect(52, 4, 24, 24), dpi);
        var leafDirty12 = Physical(new Rect(8, 32, 24, 16), dpi);
        var leafDirty23 = Physical(new Rect(16, 32, 24, 16), dpi);

        // 第一帧：整窗提交清掉 NeedsFullRedraw。
        DrawScene(1);
        context.Present();
        Assert.False(context.NeedsFullRedraw);
        host.ShowAfterFirstFrame();
        AllowComposition();
        using var frame1 = Capture(host);

        // 第二帧：只重放移动区域。保留内容必须让未动区域与第一帧逐位一致，
        // 且与整帧重绘的基准位图一致（旧位置被擦除、无残影）。
        DrawPartialScene(2, new Rect(48, 0, 32, 32), new Rect(8, 32, 24, 16));
        context.Present([rotatedDirty, leafDirty12]);
        Assert.False(context.NeedsFullRedraw);
        AllowComposition();
        using var frame2Partial = Capture(host);

        DrawScene(2);
        context.Present();
        AllowComposition();
        using var frame2Full = Capture(host);

        AssertBitmapsMatch(frame2Full, frame2Partial, 2,
            "Partial present diverged from the full-frame reference; retention or dirty replay is broken.");
        AssertRegionMatches(frame1, frame2Partial, Physical(new Rect(8, 64, 96, 16), dpi),
            "Partial present must not disturb unchanged static regions.");
        AssertPixelNear(frame2Partial, dpi, 12, 40, 255, 255, 255, 6);
        AssertPixelNear(frame2Partial, dpi, 20, 76, 0, 0, 255, 10);
        AssertPixelNear(frame2Partial, dpi, 48, 72, 128, 255, 128, 6);
        AssertPixelNear(frame2Partial, dpi, 72, 72, 128, 255, 128, 6);
        AssertPixelNear(frame2Partial, dpi, 96, 72, 0, 255, 0, 6);
        AssertPixelNear(frame2Partial, dpi, 24, 40, 255, 64, 255, 10);
        AssertPixelNear(frame2Partial, dpi, 64, 16, 255, 128, 0, 10);

        // 遮挡再重显：保留像素必须存活，且其后的局部提交仍与整帧基准一致。
        using (var occluder = platformFactory.CreateHost(new PlatformHostCreateInfo
               {
                   Title = "Square Direct2D occluder",
                   Width = 128,
                   Height = 96,
                   RenderBackend = "Direct2D",
                   TitleStyle = TitleStyle.Hidden,
                   BorderStyle = BorderStyle.None
               }))
        {
            occluder.Show();
            occluder.ShowAfterFirstFrame();
            // host 已被 MakeWindowVisible 置为 HWND_TOPMOST：遮挡窗口必须同为 TOPMOST 且
            // 精确覆盖 host 窗口矩形，否则默认级联位置根本不会产生遮挡，
            // 后面的“遮挡后像素一致”断言会空洞通过。
            var hostHandle = GetHandle(host);
            var occluderHandle = GetHandle(occluder);
            Assert.True(GetWindowRect(hostHandle, out var hostWindowRect));
            Assert.True(SetWindowPos(
                occluderHandle,
                new IntPtr(-1),
                hostWindowRect.Left,
                hostWindowRect.Top,
                hostWindowRect.Right - hostWindowRect.Left,
                hostWindowRect.Bottom - hostWindowRect.Top,
                0x0040)); // SWP_SHOWWINDOW
            using var occluderContext = occluder.CreateRenderContext();
            occluderContext.Clear(Color.Black);
            occluderContext.Present();
            AllowComposition();
            // 命中遮挡窗且屏幕中心变黑，证明其不透明内容实际盖住了 host。
            Assert.True(GetClientRect(hostHandle, out var hostClientRect));
            var clientOrigin = new NativePoint { X = 0, Y = 0 };
            Assert.True(ClientToScreen(hostHandle, ref clientOrigin));
            var probe = new NativePoint
            {
                X = clientOrigin.X + (hostClientRect.Right - hostClientRect.Left) / 2,
                Y = clientOrigin.Y + (hostClientRect.Bottom - hostClientRect.Top) / 2
            };
            Assert.Equal(occluderHandle, GetAncestor(WindowFromPoint(probe), 2)); // GA_ROOT
            using var coveredFrame = Capture(host);
            AssertPixelNear(coveredFrame, dpi, context.CanvasSize.Width / 2,
                context.CanvasSize.Height / 2, 0, 0, 0, 2);
        }
        AllowComposition();
        using var frameAfterOcclusion = Capture(host);
        AssertBitmapsMatch(frame2Full, frameAfterOcclusion, 2,
            "Retained pixels must survive window occlusion and re-exposure.");

        DrawPartialScene(3, new Rect(48, 0, 32, 32), new Rect(16, 32, 24, 16));
        context.Present([rotatedDirty, leafDirty23]);
        AllowComposition();
        using var frame3Partial = Capture(host);
        DrawScene(3);
        context.Present();
        AllowComposition();
        using var frame3Full = Capture(host);
        AssertBitmapsMatch(frame3Full, frame3Partial, 2,
            "Partial present after occlusion diverged from the full-frame reference.");

        // Resize 必须要求整帧重绘：局部提交不能清标志，只有整窗提交才能清除。
        Assert.False(context.NeedsFullRedraw);
        ((IDpiResizableRenderContext)context).Resize(context.CanvasSize, context.DpiScale);
        Assert.True(context.NeedsFullRedraw);
        DrawPartialScene(3, new Rect(48, 0, 32, 32), new Rect(16, 32, 24, 16));
        context.Present([rotatedDirty, leafDirty23]);
        Assert.True(context.NeedsFullRedraw);
        DrawScene(3);
        context.Present();
        Assert.False(context.NeedsFullRedraw);
        AllowComposition();
        using var frameAfterResize = Capture(host);
        AssertBitmapsMatch(frame3Full, frameAfterResize, 2,
            "Resize must require and receive a full redraw matching the reference.");

#else
        throw new PlatformNotSupportedException("Real Direct2D conformance requires Win32.");
#endif
    }

    [Fact]
    [Trait("Category", "RealDirect2D")]
    [SupportedOSPlatform("windows6.1")]
    public void WindowedDisplayTreeOpacityHonorsAncestorTransforms()
    {
        if (Environment.GetEnvironmentVariable("SQUARE_RUN_REAL_DIRECT2D_CONFORMANCE") != "1") return;
#if PLATFORM_WIN32
        var factory = new Win32PlatformFactory();
        var backend = new Direct2DBackendFactory();
        RenderBackendRegistry.Register(backend);
        using var textLayoutScope = TextLayoutProviderContext.Push(backend.TextLayoutProvider);
        using var host = factory.CreateHost(new PlatformHostCreateInfo
        {
            Title = "Square Direct2D nested opacity",
            Width = 260,
            Height = 140,
            RenderBackend = "Direct2D",
            TitleStyle = TitleStyle.Hidden,
            BorderStyle = BorderStyle.None
        });
        host.Show();
        MakeWindowVisible(host);
        using var context = host.CreateRenderContext();
        var root = new Square.Controls.View { Geometry = new Rect(0, 0, 260, 140) };
        var parent = new Square.Controls.View { Geometry = new Rect(20, 20, 40, 40) };
        parent.Style.Set("transform", "translate(100px, 0px)");
        var group = new Square.Controls.View { Geometry = parent.Geometry };
        group.Style.Set("opacity", "0.5");
        group.Style.Set("transform", "rotate(45deg)");
        group.Children.Add(new SolidPaintElement { Geometry = group.Geometry });
        parent.Children.Add(group);
        root.Children.Add(parent);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        tree.UpdateDirty();
        context.Clear(Color.White);
        tree.Render(context);
        context.Present();
        tree.CommitPresentedFrame();
        AllowComposition();
        using var initial = Capture(host);
        AssertPixelNear(initial, context.DpiScale, 140, 40, 255, 128, 128, 3);
        AssertPixelNear(initial, context.DpiScale, 140, 14, 255, 128, 128, 3);

        parent.Style.SetAnimated("transform", "translate(60px, 0px)");
        tree.UpdateDirty();
        var dirty = tree.CollectDirtyRects(context.CanvasSize);
        foreach (var rect in dirty) context.Clear(Color.White, rect);
        tree.Render(context, dirty);
        context.Present(dirty);
        tree.CommitPresentedFrame();
        AllowComposition();
        using var partial = Capture(host);
        AssertPixelNear(partial, context.DpiScale, 100, 40, 255, 128, 128, 3);
        AssertPixelNear(partial, context.DpiScale, 100, 14, 255, 128, 128, 3);
        AssertPixelNear(partial, context.DpiScale, 140, 40, 255, 255, 255, 3);

        context.Clear(Color.White);
        tree.Render(context);
        context.Present();
        AllowComposition();
        using var full = Capture(host);
        AssertBitmapsMatch(full, partial, 2, "Nested-opacity dirty HWND pixels differ from full redraw.");
#else
        throw new PlatformNotSupportedException("Real Direct2D conformance requires Win32.");
#endif
    }

    private sealed class SolidPaintElement : Square.Controls.View
    {
        public override void Paint(IRenderContext context) => context.FillRect(Geometry, Brush.FromColor(Color.Red));
    }

    private static void MakeWindowVisible(IPlatformHost host)
    {
        var handle = GetHandle(host);
        host.ShowAfterFirstFrame();
        Assert.True(SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0043));
        SetForegroundWindow(handle);
        AllowComposition();
    }

    private static IntPtr GetHandle(IPlatformHost host)
        => (IntPtr)host.GetType().GetProperty("Handle")!.GetValue(host)!;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr handle, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr handle, uint flags);

    private static Bitmap Capture(IPlatformHost host) => VisibleHwndCapture.Capture(host);

    private static void AllowComposition() => Thread.Sleep(160);

    private static void AssertBitmapsMatch(Bitmap expected, Bitmap actual, byte tolerance, string message)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        var differing = 0;
        var maxDelta = 0;
        for (var y = 0; y < expected.Height; y++)
        for (var x = 0; x < expected.Width; x++)
        {
            var expectedPixel = expected.GetPixel(x, y);
            var actualPixel = actual.GetPixel(x, y);
            var delta = 0;
            for (var channel = 0; channel < 4; channel++)
                delta = Math.Max(delta, Math.Abs(expectedPixel[channel] - actualPixel[channel]));
            if (delta > tolerance)
            {
                differing++;
                maxDelta = Math.Max(maxDelta, delta);
            }
        }
        Assert.True(differing == 0,
            $"{message} Differing pixels: {differing} of {expected.Width * expected.Height} (maxDelta={maxDelta}).");
    }

    private static void AssertRegionMatches(Bitmap expected, Bitmap actual, Rect physicalRect, string message)
    {
        var left = Math.Clamp((int)physicalRect.Left, 0, expected.Width);
        var top = Math.Clamp((int)physicalRect.Top, 0, expected.Height);
        var right = Math.Clamp((int)MathF.Ceiling(physicalRect.Right), left, expected.Width);
        var bottom = Math.Clamp((int)MathF.Ceiling(physicalRect.Bottom), top, expected.Height);
        var differing = 0;
        var maxDelta = 0;
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
        {
            var expectedPixel = expected.GetPixel(x, y);
            var actualPixel = actual.GetPixel(x, y);
            var delta = 0;
            for (var channel = 0; channel < 4; channel++)
                delta = Math.Max(delta, Math.Abs(expectedPixel[channel] - actualPixel[channel]));
            if (delta > 2)
            {
                differing++;
                maxDelta = Math.Max(maxDelta, delta);
            }
        }
        Assert.True(differing == 0,
            $"{message} Region ({left},{top})-({right},{bottom}) differing pixels: {differing} (maxDelta={maxDelta}).");
    }

    private static int CountPixels(Bitmap bitmap, Func<ReadOnlySpan<byte>, bool> predicate)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            if (predicate(bitmap.GetPixel(x, y))) count++;
        return count;
    }

    private static string DescribeBitmap(Bitmap bitmap)
    {
        var white = CountPixels(bitmap, static pixel => pixel[0] > 240 && pixel[1] > 240 && pixel[2] > 240);
        var black = CountPixels(bitmap, static pixel => pixel[0] < 15 && pixel[1] < 15 && pixel[2] < 15);
        var blue = CountPixels(bitmap, static pixel => pixel[0] > 220 && pixel[1] < 50 && pixel[2] < 50);
        var green = CountPixels(bitmap, static pixel => pixel[1] > 100 && pixel[0] < 100 && pixel[2] < 100);
        return $"Capture={bitmap.Width}x{bitmap.Height}, white={white}, black={black}, blue={blue}, green={green}.";
    }

    private static void AssertPixelNear(
        Bitmap bitmap,
        float dpiScale,
        float logicalX,
        float logicalY,
        byte red,
        byte green,
        byte blue,
        byte tolerance)
    {
        var x = Math.Clamp((int)MathF.Round(logicalX * dpiScale), 0, bitmap.Width - 1);
        var y = Math.Clamp((int)MathF.Round(logicalY * dpiScale), 0, bitmap.Height - 1);
        var pixel = bitmap.GetPixel(x, y);
        Assert.InRange(pixel[2], (byte)Math.Max(0, red - tolerance), (byte)Math.Min(255, red + tolerance));
        Assert.InRange(pixel[1], (byte)Math.Max(0, green - tolerance), (byte)Math.Min(255, green + tolerance));
        Assert.InRange(pixel[0], (byte)Math.Max(0, blue - tolerance), (byte)Math.Min(255, blue + tolerance));
    }

    private static int CountDarkPixels(Bitmap bitmap, float dpiScale, Rect logicalRect)
    {
        var left = Math.Clamp((int)MathF.Floor(logicalRect.Left * dpiScale), 0, bitmap.Width);
        var top = Math.Clamp((int)MathF.Floor(logicalRect.Top * dpiScale), 0, bitmap.Height);
        var right = Math.Clamp((int)MathF.Ceiling(logicalRect.Right * dpiScale), left, bitmap.Width);
        var bottom = Math.Clamp((int)MathF.Ceiling(logicalRect.Bottom * dpiScale), top, bitmap.Height);
        var count = 0;
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel[0] < 80 && pixel[1] < 80 && pixel[2] < 80) count++;
        }
        return count;
    }

}
