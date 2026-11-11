using System.Numerics;
using System.Runtime.InteropServices;
using Square.Graphics;
using Square.Rendering.Tessellation;
using Xunit;

namespace Square.Backends.Vulkan.Tests;

public sealed class VulkanBrushMaterialTests
{
    [Fact]
    public void NativeMaterialLayoutFitsTheVulkanMinimumPushBudget()
    {
        Assert.Equal(20, Marshal.SizeOf<Vertex2D>());
        Assert.Equal(8, Marshal.SizeOf<VulkanGradientStop>());
        Assert.Equal(48, Marshal.SizeOf<VulkanMaterialPushConstants>());
        Assert.Equal(0, Marshal.OffsetOf<VulkanMaterialPushConstants>(nameof(VulkanMaterialPushConstants.RowX)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<VulkanMaterialPushConstants>(nameof(VulkanMaterialPushConstants.RowY)).ToInt32());
        Assert.Equal(32, Marshal.OffsetOf<VulkanMaterialPushConstants>(nameof(VulkanMaterialPushConstants.Mode)).ToInt32());
        Assert.Equal(44, Marshal.OffsetOf<VulkanMaterialPushConstants>(nameof(VulkanMaterialPushConstants.StopCount)).ToInt32());
        Assert.InRange(64 + Marshal.SizeOf<VulkanMaterialPushConstants>(), 0, 128);
    }

    [Fact]
    public void LinearCoordinatesInvertDpiRotationTranslationAndNonuniformScale()
    {
        var transform = Matrix3x2.CreateScale(1.25f, -2f) * Matrix3x2.CreateRotation(0.35f) * Matrix3x2.CreateTranslation(35, 61);
        var brush = new LinearGradientBrush(new Point(10, 20), new Point(50, 20),
            new GradientStop(0, Color.Red), new GradientStop(1, Color.Blue));
        var material = VulkanBrushMaterial.Create(brush, transform);
        Assert.Equal(1u, material.Mode);
        foreach (var t in new[] { -0.5f, 0f, 0.25f, 1f, 1.5f })
        {
            var physical = Vector2.Transform(new Vector2(10 + 40 * t, 32), transform);
            AssertNear(t, Evaluate(material.RowX, physical));
        }
    }

    [Fact]
    public void RadialCoordinatesAreLogicalCirclesEvenUnderAnisotropicDeviceTransform()
    {
        var transform = Matrix3x2.CreateScale(2, 3) * Matrix3x2.CreateRotation(0.6f) * Matrix3x2.CreateTranslation(80, 22);
        var material = VulkanBrushMaterial.Create(new RadialGradientBrush(new Point(10, 8), 4,
            new GradientStop(0, Color.White), new GradientStop(1, Color.Black)), transform);
        Assert.Equal(2u, material.Mode);
        var physical = Vector2.Transform(new Vector2(12.4f, 11.2f), transform);
        var normalized = new Vector2(Evaluate(material.RowX, physical), Evaluate(material.RowY, physical));
        AssertNear(0.6f, normalized.X);
        AssertNear(0.8f, normalized.Y);
        AssertNear(1, normalized.Length());
    }

    [Fact]
    public void OffscreenOriginBiasPreservesRootGradientCoordinates()
    {
        var material = VulkanBrushMaterial.Create(new RadialGradientBrush(new Point(10, 8), 20,
            new GradientStop(0, Color.Red), new GradientStop(1, Color.Blue)), Matrix3x2.CreateScale(1.5f));
        var origin = new Vector2(48, 72);
        var rootPixel = new Vector2(63.5f, 90.5f);
        var push = material.ToPushConstants(17, origin);
        AssertNear(Evaluate(material.RowX, rootPixel), Evaluate(push.RowX, rootPixel - origin));
        AssertNear(Evaluate(material.RowY, rootPixel), Evaluate(push.RowY, rootPixel - origin));
        Assert.Equal(17u, push.StopOffset);
        Assert.Equal(2u, push.StopCount);
    }

    [Fact]
    public void DrawSnapshotKeepsOriginalStopOrderAndSurvivesAllBrushMutations()
    {
        var first = new GradientStop(0.7f, new Color(17, 34, 51, 68));
        var second = new GradientStop(0.4f, Color.Red);
        var third = new GradientStop(0.4f, Color.Green);
        var brush = new LinearGradientBrush(new Point(1, 2), new Point(11, 2), first, second, third)
        {
            SpreadMethod = GradientSpreadMethod.Reflect
        };
        var snapshot = VulkanBrushMaterial.Create(brush, Matrix3x2.Identity);
        var same = VulkanBrushMaterial.Create(brush, Matrix3x2.Identity);
        Assert.Equal(snapshot, same);
        Assert.Equal(snapshot.GetHashCode(), same.GetHashCode());
        Assert.Equal(0.7f, snapshot.Stops[0].Offset);
        Assert.Equal(0x44332211u, snapshot.Stops[0].Color);
        Assert.Equal(0.4f, snapshot.Stops[1].Offset);
        Assert.Equal(0.4f, snapshot.Stops[2].Offset);
        Assert.NotEqual(snapshot.Stops[1].Color, snapshot.Stops[2].Color);
        Assert.Equal((uint)GradientSpreadMethod.Reflect, snapshot.Spread);
        first.Offset = 0.2f;
        first.Color = Color.Blue;
        brush.Start = new Point(8, 9);
        brush.End = new Point(20, 24);
        brush.SpreadMethod = GradientSpreadMethod.Repeat;
        brush.Stops = [third, second, first];
        Assert.NotEqual(snapshot, VulkanBrushMaterial.Create(brush, Matrix3x2.Identity));
        Assert.Equal(0.7f, snapshot.Stops[0].Offset);
        Assert.Equal(0x44332211u, snapshot.Stops[0].Color);
        Assert.Equal((uint)GradientSpreadMethod.Reflect, snapshot.Spread);
    }

    [Theory]
    [InlineData(GradientSpreadMethod.Pad)]
    [InlineData(GradientSpreadMethod.Reflect)]
    [InlineData(GradientSpreadMethod.Repeat)]
    public void SpreadModeAndEmptyStopsReachTheShaderWithoutCpuColorFallback(GradientSpreadMethod spread)
    {
        var brush = new LinearGradientBrush(Point.Zero, new Point(0, 10)) { SpreadMethod = spread };
        var material = VulkanBrushMaterial.Create(brush, Matrix3x2.Identity);
        var push = material.ToPushConstants(0, Vector2.Zero);
        Assert.Equal(1u, push.Mode);
        Assert.Equal((uint)spread, push.Spread);
        Assert.Equal(0u, push.StopCount);
    }

    [Fact]
    public void DegenerateGeometryUsesTheFirstGradientCoordinateWithoutDivisionByZero()
    {
        var linear = VulkanBrushMaterial.Create(new LinearGradientBrush(new Point(5, 5), new Point(5, 5),
            new GradientStop(0, Color.Red)), Matrix3x2.Identity);
        var radial = VulkanBrushMaterial.Create(new RadialGradientBrush(new Point(5, 5), 0,
            new GradientStop(0, Color.Red)), Matrix3x2.Identity);
        var singular = VulkanBrushMaterial.Create(new LinearGradientBrush(Point.Zero, new Point(10, 10)), Matrix3x2.CreateScale(0));
        Assert.Equal(Vector4.Zero, linear.RowX);
        Assert.Equal(Vector4.Zero, radial.RowX);
        Assert.Equal(Vector4.Zero, radial.RowY);
        Assert.Equal(Vector4.Zero, singular.RowX);
    }

    [Fact]
    public void LayerOpacityKeepsExactFloatPrecisionAndIsNotBakedIntoAnEightBitTint()
    {
        const float opacity = 0.001234567f;
        var push = VulkanBrushMaterial.Layer(opacity).ToPushConstants(0, new Vector2(77, 91));
        Assert.Equal(3u, push.Mode);
        Assert.Equal(opacity, push.RowX.W);
        Assert.NotEqual(0, push.RowX.W);
        Assert.Equal(0u, push.StopCount);
        Assert.Equal(Vector4.Zero, push.RowY);
        Assert.Equal(1f, VulkanBrushMaterial.Layer(float.NaN).RowX.W);
        Assert.Equal(0f, VulkanBrushMaterial.Layer(-1).RowX.W);
        Assert.Equal(1f, VulkanBrushMaterial.Layer(2).RowX.W);
    }

    [Fact]
    public void LayerBackingBoundsAreIntegerClampedWhileTheContextKeepsTheExactMask()
    {
        var actual = VulkanBatchRenderer.RoundLayerBounds(new Rect(2.25f, 3.5f, 10.25f, 7.75f), new Rect(0, 0, 128, 96));
        Assert.Equal(new Rect(2, 3, 11, 9), actual);
        Assert.Equal(new Rect(2, 2, 5, 5), VulkanBatchRenderer.RoundLayerBounds(new Rect(0, 0, 100, 100), new Rect(2, 2, 5, 5)));
        Assert.True(VulkanBatchRenderer.RoundLayerBounds(new Rect(200, 200, 10, 10), new Rect(0, 0, 128, 96)).IsEmpty);
        Assert.True(VulkanBatchRenderer.RoundLayerBounds(new Rect(float.NaN, 0, 10, 10), new Rect(0, 0, 128, 96)).IsEmpty);
    }

    [Fact]
    public void GeneratedFragmentSpirvMatchesThePushOffsetsAndStopStride()
    {
        Assert.Equal(0x07230203u, BitConverter.ToUInt32(EmbeddedShaders.Vertex2D));
        Assert.Equal(0x07230203u, BitConverter.ToUInt32(EmbeddedShaders.Fragment2D));
        var words = MemoryMarshal.Cast<byte, uint>(EmbeddedShaders.Fragment2D);
        var offsets = new List<uint>();
        var strides = new List<uint>();
        for (var i = 5; i < words.Length;)
        {
            var length = (int)(words[i] >> 16);
            var opcode = words[i] & 0xffff;
            Assert.True(length > 0 && i + length <= words.Length);
            if (opcode == 72 && length >= 5 && words[i + 3] == 35) offsets.Add(words[i + 4]); // OpMemberDecorate Offset
            if (opcode == 71 && length >= 4 && words[i + 2] == 6) strides.Add(words[i + 3]); // OpDecorate ArrayStride
            i += length;
        }
        Assert.Contains(64u, offsets);
        Assert.Contains(80u, offsets);
        Assert.Contains(96u, offsets);
        Assert.Contains(8u, strides);
    }

    private static float Evaluate(Vector4 row, Vector2 point) => row.X * point.X + row.Y * point.Y + row.Z;
    private static void AssertNear(float expected, float actual) => Assert.InRange(MathF.Abs(expected - actual), 0, 0.00002f);
}
