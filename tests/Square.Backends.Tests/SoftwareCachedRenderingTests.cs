using System.Numerics;
using System.Reflection;
using Square.Backends;
using Square.Graphics;
using Xunit;

namespace Square.Backends.Tests;

public sealed class SoftwareCachedRenderingTests
{
    private static readonly FieldInfo CacheField = typeof(RenderContext).GetField("_coverageCache", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static void BeforeDraw(RenderContext context, bool forceMiss)
    {
        if (forceMiss) ((SoftwareCoverageCache)CacheField.GetValue(context)!).Clear();
    }
    private static RenderContext Context(int width = 64, int height = 56)
        => new(new Bitmap(width, height), new Size(width, height), 1);

    [Fact]
    public void WarmCoverageMatchesEveryDrawMissThroughGeometryPaintTransformAndDpiMutation()
    {
        using var warm = Context();
        using var reference = Context();
        var stroke = new PathGeometry().MoveTo(new Point(5.25f, 5.5f)).LineTo(new Point(45, 41))
            .LineTo(new Point(8, 41)).LineTo(new Point(45, 7)).Close()
            .MoveTo(new Point(5, 48)).LineTo(new Point(25, 48)).LineTo(new Point(45, 48))
            .MoveTo(new Point(52, 4)).LineTo(new Point(52, 40));
        var fill = Box(10, 8, 40, 36).MoveTo(new Point(8, 6)).LineTo(new Point(30, 48)).LineTo(new Point(54, 6)).Close();
        var strokeBrush = new SolidColorBrush(new Color(137, 193, 71, 127));
        var fillBrush = new SolidColorBrush(new Color(210, 40, 90, 91));
        var pen = new Pen(strokeBrush, 1.25f);
        var transform = Matrix3x2.Identity;
        void Draw(RenderContext context, PathGeometry line, PathGeometry area, bool miss)
        {
            context.Clear(new Color(17, 47, 113, 87));
            context.PushTransform(transform);
            for (var repeat = 0; repeat < 3; repeat++)
            {
                BeforeDraw(context, miss); context.FillPath(area, fillBrush);
                BeforeDraw(context, miss); context.DrawPath(line, pen);
            }
            context.PopTransform();
        }
        for (var phase = 0; phase < 5; phase++)
        {
            if (phase == 1)
            {
                strokeBrush.Color = new Color(20, 180, 220, 79);
                fillBrush.Color = new Color(230, 80, 10, 173);
            }
            if (phase == 2)
            {
                Commands(stroke)[1] = new LineToCmd(new Point(42, 36));
                Commands(fill)[1] = new LineToCmd(new Point(46, 15));
            }
            if (phase == 3)
            {
                stroke.MoveTo(new Point(8, 20)).ArcTo(new Rect(8, 8, 32, 28), 15, 260);
                fill.MoveTo(new Point(25, 8)).LineTo(new Point(52, 20)).LineTo(new Point(25, 32)).Close();
                pen.Width = 3.25f;
            }
            if (phase == 4)
            {
                transform = new Matrix3x2(.875f, .125f, -.25f, 1.125f, 3.25f, 1.75f);
                warm.Resize(new Size(32, 28), 2);
                reference.Resize(new Size(32, 28), 2);
                Assert.Equal(0, warm.CoverageCacheCount);
            }
            var builds = warm.CoverageBuildCount;
            var hits = warm.CoverageHitCount;
            Draw(warm, stroke, fill, false);
            Draw(reference, Copy(stroke), Copy(fill), true);
            EqualPixels(reference, warm);
            Assert.True(warm.CoverageHitCount > hits);
            if (phase == 1) Assert.Equal(builds, warm.CoverageBuildCount); // Paint is not coverage identity.
            builds = warm.CoverageBuildCount;
            Draw(warm, stroke, fill, false);
            Draw(reference, Copy(stroke), Copy(fill), true);
            EqualPixels(reference, warm);
            Assert.Equal(builds, warm.CoverageBuildCount);
        }
    }

    [Fact]
    public void SameBoundsPredicatesAndLiveEllipseChangesAreAppliedOnEveryReplay()
    {
        using var warm = Context();
        using var reference = Context();
        var paint = Box(0, 0, 64, 56);
        var a = new PathGeometry().MoveTo(new Point(4, 4)).LineTo(new Point(60, 4)).LineTo(new Point(4, 52)).Close();
        var b = new PathGeometry().MoveTo(new Point(4, 4)).LineTo(new Point(60, 52)).LineTo(new Point(60, 4)).Close();
        void DrawMask(RenderContext context, PathGeometry area, PathGeometry clip, bool miss)
        {
            context.Clear(Color.Transparent);
            context.PushClip(clip);
            BeforeDraw(context, miss); context.FillPath(area, Brush.FromColor(new Color(70, 120, 210, 128)));
            context.PopClip();
        }
        DrawMask(warm, paint, a, false);
        DrawMask(reference, Copy(paint), Copy(a), true);
        EqualPixels(reference, warm);
        Assert.Equal((byte)0, warm.GetBitmap().GetPixel(56, 44)[3]);
        // First use is probation; second use records the mask before testing live replay predicates.
        DrawMask(warm, paint, a, false);
        DrawMask(reference, Copy(paint), Copy(a), true);
        EqualPixels(reference, warm);
        var hits = warm.CoverageHitCount;
        DrawMask(warm, paint, b, false);
        DrawMask(reference, Copy(paint), Copy(b), true);
        EqualPixels(reference, warm);
        Assert.True(warm.CoverageHitCount > hits);
        Assert.True(warm.GetBitmap().GetPixel(56, 44)[3] > 0);

        void DrawLiveEllipse(RenderContext context, PathGeometry area, bool miss)
        {
            context.Clear(Color.Transparent);
            var ellipse = new EllipseGeometry(new Point(32, 28), 28, 24);
            context.PushClip(ellipse); // Bounds are fixed here; predicate remains live.
            BeforeDraw(context, miss); context.FillPath(area, Brush.FromColor(new Color(220, 60, 20, 79)));
            ellipse.Center = new Point(28, 24); ellipse.RadiusX = 10; ellipse.RadiusY = 9;
            BeforeDraw(context, miss); context.FillPath(area, Brush.FromColor(new Color(20, 160, 230, 173)));
            context.PopClip();
        }
        hits = warm.CoverageHitCount;
        DrawLiveEllipse(warm, paint, false);
        DrawLiveEllipse(reference, Copy(paint), true);
        EqualPixels(reference, warm);
        Assert.True(warm.CoverageHitCount >= hits + 2);
    }

    [Fact]
    public void LayerBoundsAndOpacityDoNotFreezeCoverageOrPerDrawCompositing()
    {
        using var warm = Context();
        using var reference = Context();
        var path = Box(6, 6, 48, 40);
        var fill = Brush.FromColor(new Color(140, 40, 220, 128));
        var pen = Pen.FromColor(new Color(30, 190, 70, 91), 2.25f);
        void Draw(RenderContext context, PathGeometry geometry, Rect bounds, float opacity, bool miss)
        {
            context.Clear(new Color(17, 47, 113, 87));
            context.PushLayer(bounds, opacity);
            for (var repeat = 0; repeat < 2; repeat++)
            {
                BeforeDraw(context, miss); context.FillPath(geometry, fill);
                BeforeDraw(context, miss); context.DrawPath(geometry, pen);
            }
            context.PopLayer();
        }
        Draw(warm, path, new Rect(4, 4, 48, 40), .3f, false);
        Draw(reference, Copy(path), new Rect(4, 4, 48, 40), .3f, true);
        EqualPixels(reference, warm);
        var builds = warm.CoverageBuildCount; var hits = warm.CoverageHitCount;
        Draw(warm, path, new Rect(12, 8, 24, 28), .8f, false);
        Draw(reference, Copy(path), new Rect(12, 8, 24, 28), .8f, true);
        EqualPixels(reference, warm);
        Assert.Equal(builds, warm.CoverageBuildCount);
        Assert.True(warm.CoverageHitCount > hits);
    }

    [Fact]
    public void TransparentDrawDoesNotPoisonCoverageAndRepeatedDrawsBlendIndependently()
    {
        using var context = Context();
        var path = Box(4, 4, 16, 16);
        context.Clear(Color.Transparent);
        context.FillPath(path, Brush.FromColor(Color.Transparent));
        context.DrawPath(path, Pen.FromColor(Color.Transparent, 2));
        Assert.Equal(0, context.CoverageCacheCount);
        var brush = new SolidColorBrush(new Color(137, 193, 71, 128));
        context.FillPath(path, brush);
        Assert.Equal((byte)128, context.GetBitmap().GetPixel(10, 10)[3]);
        var hits = context.CoverageHitCount;
        context.FillPath(path, brush);
        Assert.Equal((byte)191, context.GetBitmap().GetPixel(10, 10)[3]);
        Assert.Equal(hits, context.CoverageHitCount);
        context.FillPath(path, brush);
        Assert.Equal((byte)223, context.GetBitmap().GetPixel(10, 10)[3]);
        Assert.True(context.CoverageHitCount > hits);
    }

    [Fact]
    public void OversizedCoverageStillRendersCompletelyWithoutRetainingAPartialMask()
    {
        using var warm = Context(272, 272);
        using var reference = Context(272, 272);
        var path = Box(0, 0, 270, 270);
        var brush = Brush.FromColor(new Color(30, 170, 80, 127));
        for (var repeat = 0; repeat < 2; repeat++)
        {
            warm.Clear(Color.Transparent); reference.Clear(Color.Transparent);
            warm.FillPath(path, brush);
            BeforeDraw(reference, true); reference.FillPath(Copy(path), brush);
            EqualPixels(reference, warm);
            Assert.Equal(0, warm.CoverageCacheCount);
            Assert.Equal(0, warm.CoverageCacheBytes);
            Assert.Equal((byte)127, warm.GetBitmap().GetPixel(269, 269)[3]);
        }
    }

    [Fact]
    public void ResizeAndDisposeReleaseCoverageAndResizedPixelsMatchFreshState()
    {
        using var context = Context();
        var path = Box(4, 4, 28, 22);
        var brush = Brush.FromColor(new Color(80, 190, 40, 131));
        context.FillPath(path, brush);
        context.FillPath(path, brush); // Second use records; the first use holds only weak probation metadata.
        Assert.True(context.CoverageCacheCount > 0);
        context.Resize(new Size(40, 32), 1.5f);
        Assert.Equal(0, context.CoverageCacheCount);
        Assert.Equal(0, context.CoverageCacheBytes);
        using var reference = new RenderContext(new Bitmap(60, 48), new Size(40, 32), 1.5f);
        context.Clear(Color.Transparent); reference.Clear(Color.Transparent);
        context.FillPath(path, brush);
        BeforeDraw(reference, true); reference.FillPath(Copy(path), brush);
        EqualPixels(reference, context);
        context.Dispose();
        Assert.Equal(0, context.CoverageCacheCount);
        Assert.Equal(0, context.CoverageCacheBytes);
    }

    private static PathGeometry Box(float x, float y, float w, float h) => new PathGeometry().MoveTo(new Point(x, y))
        .LineTo(new Point(x + w, y)).LineTo(new Point(x + w, y + h)).LineTo(new Point(x, y + h)).Close();
    private static List<PathCommand> Commands(PathGeometry path) => Assert.IsType<List<PathCommand>>(path.Commands);
    private static PathGeometry Copy(PathGeometry path)
    {
        var result = new PathGeometry();
        foreach (var command in path.Commands)
            switch (command)
            {
                case MoveToCmd move: result.MoveTo(move.Point); break;
                case LineToCmd line: result.LineTo(line.Point); break;
                case ArcToCmd arc: result.ArcTo(arc.Oval, arc.StartAngle, arc.SweepAngle); break;
                case CloseCmd: result.Close(); break;
            }
        return result;
    }
    private static void EqualPixels(RenderContext expected, RenderContext actual)
    {
        var e = expected.GetBitmap(); var a = actual.GetBitmap();
        Assert.Equal(e.Width, a.Width); Assert.Equal(e.Height, a.Height);
        Assert.Equal(e.Pixels, a.Pixels);
    }
}
