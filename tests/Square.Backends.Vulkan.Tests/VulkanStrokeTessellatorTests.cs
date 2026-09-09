using Square.Graphics;
using Square.Rendering.Tessellation;
using Xunit;

namespace Square.Backends.Vulkan.Tests;

public class StrokeTessellatorTests
{
    [Fact]
    public void SquareCapExtendsOpenLineByHalfWidthAndFeather()
    {
        var (vertices, _) = Tessellate(
            [new Point(10, 20), new Point(30, 20)],
            new StrokeStyle { Cap = LineCap.Square });

        Assert.Equal(7.5f, vertices.Min(vertex => vertex.X), 3);
        Assert.Equal(32.5f, vertices.Max(vertex => vertex.X), 3);
    }

    [Fact]
    public void RoundCapAddsMoreGeometryThanButtCap()
    {
        var (buttVertices, buttIndices) = Tessellate(
            [new Point(0, 0), new Point(20, 0)],
            new StrokeStyle { Cap = LineCap.Butt });
        var (roundVertices, roundIndices) = Tessellate(
            [new Point(0, 0), new Point(20, 0)],
            new StrokeStyle { Cap = LineCap.Round });

        Assert.True(roundVertices.Count > buttVertices.Count);
        Assert.True(roundIndices.Count > buttIndices.Count);
        Assert.Equal(-2.5f, roundVertices.Min(vertex => vertex.X), 3);
        Assert.Equal(22.5f, roundVertices.Max(vertex => vertex.X), 3);
    }

    [Fact]
    public void MiterJoinExtendsBeyondBevelJoin()
    {
        Point[] contour = [new Point(0, 20), new Point(20, 20), new Point(20, 0)];
        var (bevelVertices, _) = Tessellate(contour, new StrokeStyle { Join = LineJoin.Bevel });
        var (miterVertices, _) = Tessellate(contour, new StrokeStyle { Join = LineJoin.Miter, MiterLimit = 10 });

        Assert.DoesNotContain(bevelVertices, vertex => vertex.X > 21.5f && vertex.Y > 21.5f);
        Assert.Contains(miterVertices, vertex => vertex.X > 21.5f && vertex.Y > 21.5f);
    }

    [Fact]
    public void MiterLimitFallsBackToBevel()
    {
        Point[] contour = [new Point(0, 20), new Point(20, 20), new Point(20, 0)];
        var (bevelVertices, bevelIndices) = Tessellate(contour, new StrokeStyle { Join = LineJoin.Bevel });
        var (limitedVertices, limitedIndices) = Tessellate(contour,
            new StrokeStyle { Join = LineJoin.Miter, MiterLimit = 1 });

        Assert.Equal(bevelVertices.Count, limitedVertices.Count);
        Assert.Equal(bevelIndices.Count, limitedIndices.Count);
        Assert.Equal(bevelVertices.Max(vertex => vertex.X), limitedVertices.Max(vertex => vertex.X), 3);
        Assert.Equal(bevelVertices.Max(vertex => vertex.Y), limitedVertices.Max(vertex => vertex.Y), 3);
    }

    [Fact]
    public void ClosedContourDoesNotGenerateEndCaps()
    {
        Point[] closed = [new Point(0, 0), new Point(20, 0), new Point(20, 20), new Point(0, 0)];
        var (buttVertices, buttIndices) = Tessellate(closed, new StrokeStyle { Cap = LineCap.Butt });
        var (roundVertices, roundIndices) = Tessellate(closed, new StrokeStyle { Cap = LineCap.Round });

        Assert.Equal(buttVertices.Count, roundVertices.Count);
        Assert.Equal(buttIndices.Count, roundIndices.Count);
    }

    [Fact]
    public void DashArraySplitsLineAtPathLengths()
    {
        var dashes = StrokeTessellator.SplitDashes(
            [new Point(0, 0), new Point(14, 0)], false, [4, 2], 0);

        Assert.Equal(3, dashes.Count);
        AssertDash(dashes[0], 0, 4);
        AssertDash(dashes[1], 6, 10);
        AssertDash(dashes[2], 12, 14);
    }

    [Fact]
    public void DashOffsetAdvancesIntoPattern()
    {
        var dashes = StrokeTessellator.SplitDashes(
            [new Point(0, 0), new Point(12, 0)], false, [4, 2], 1);

        Assert.Equal(3, dashes.Count);
        AssertDash(dashes[0], 0, 3);
        AssertDash(dashes[1], 5, 9);
        AssertDash(dashes[2], 11, 12);
    }

    [Fact]
    public void OddDashArrayRepeatsToProduceEvenPattern()
    {
        var (_, indices) = Tessellate(
            [new Point(0, 0), new Point(12, 0)],
            new StrokeStyle { DashArray = [3], Cap = LineCap.Butt });

        var singleDashIndices = Tessellate(
            [new Point(0, 0), new Point(3, 0)],
            new StrokeStyle { Cap = LineCap.Butt }).Indices.Count;
        Assert.Equal(singleDashIndices * 2, indices.Count);
    }

    [Fact]
    public void DashKeepsJoinWhenItCrossesPathVertex()
    {
        var dashes = StrokeTessellator.SplitDashes(
            [new Point(0, 0), new Point(5, 0), new Point(5, 5)], false, [8, 2], 0);

        Assert.Single(dashes);
        Assert.Equal(3, dashes[0].Count);
        Assert.Equal(new Point(5, 0), dashes[0][1]);
        Assert.Equal(new Point(5, 3), dashes[0][2]);
    }

    [Fact]
    public void ClosedContourMergesDashAcrossSeam()
    {
        var seam = new Point(0, 0);
        var dashes = StrokeTessellator.SplitDashes(
            [seam, new Point(10, 0), new Point(10, 10), new Point(0, 10)], true, [12, 4], 0);

        Assert.Equal(2, dashes.Count);
        Assert.Contains(dashes, dash => dash.IndexOf(seam) is > 0 && dash.IndexOf(seam) < dash.Count - 1);
    }

    [Fact]
    public void InvalidDashPatternFallsBackToSolidStroke()
    {
        var solid = Tessellate(
            [new Point(0, 0), new Point(12, 0)],
            new StrokeStyle { Cap = LineCap.Butt });
        var invalid = Tessellate(
            [new Point(0, 0), new Point(12, 0)],
            new StrokeStyle { DashArray = [0, 0], Cap = LineCap.Butt });

        Assert.Equal(solid.Vertices.Count, invalid.Vertices.Count);
        Assert.Equal(solid.Indices.Count, invalid.Indices.Count);
    }

    private static (List<Vertex2D> Vertices, List<uint> Indices) Tessellate(
        IReadOnlyList<Point> contour,
        StrokeStyle style)
    {
        var vertices = new List<Vertex2D>();
        var indices = new List<uint>();
        StrokeTessellator.Append(contour, 2, 1, style, 0xFFFFFFFF, 0, 0, 1, 1,
            static point => point, vertices, indices);
        return (vertices, indices);
    }

    private static void AssertDash(IReadOnlyList<Point> dash, float startX, float endX)
    {
        Assert.Equal(startX, dash[0].X, 3);
        Assert.Equal(endX, dash[^1].X, 3);
    }
}
