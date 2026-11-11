using System.Numerics;
using System.Runtime.InteropServices;
using Square.Graphics;

namespace Square.Backends.Vulkan;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal readonly record struct VulkanGradientStop(float Offset, uint Color);

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct VulkanMaterialPushConstants
{
    public Vector4 RowX;
    public Vector4 RowY;
    public uint Mode;
    public uint Spread;
    public uint StopOffset;
    public uint StopCount;
}

/// <summary>Immutable draw-time brush snapshot. Rows map root physical pixels to gradient coordinates.</summary>
internal sealed class VulkanBrushMaterial : IEquatable<VulkanBrushMaterial>
{
    private readonly VulkanGradientStop[] _stops;
    private readonly int _hash;
    public uint Mode { get; }
    public uint Spread { get; }
    public Vector4 RowX { get; }
    public Vector4 RowY { get; }
    public ReadOnlySpan<VulkanGradientStop> Stops => _stops;

    private VulkanBrushMaterial(uint mode, uint spread, Vector4 rowX, Vector4 rowY, VulkanGradientStop[] stops)
    {
        Mode = mode;
        Spread = spread;
        RowX = rowX;
        RowY = rowY;
        _stops = stops;
        var hash = new HashCode();
        hash.Add(mode); hash.Add(spread); hash.Add(rowX); hash.Add(rowY);
        foreach (var stop in stops) hash.Add(stop);
        _hash = hash.ToHashCode();
    }

    internal readonly record struct Descriptor(uint Mode, uint Spread, Vector4 RowX, Vector4 RowY, GradientStop[] Stops);

    public static VulkanBrushMaterial Create(Brush brush, Matrix3x2 logicalToDevice)
        => Snapshot(Describe(brush, logicalToDevice));

    internal static Descriptor Describe(Brush brush, Matrix3x2 logicalToDevice)
    {
        var invertible = Matrix3x2.Invert(logicalToDevice, out var inverse);
        var rowX = Vector4.Zero;
        var rowY = Vector4.Zero;
        GradientStop[] values;
        uint mode;
        GradientSpreadMethod spread;
        switch (brush)
        {
            case SolidColorBrush:
                return new Descriptor(0, 0, default, default, []);
            case LinearGradientBrush linear:
                mode = 1; spread = linear.SpreadMethod; values = linear.Stops;
                var delta = new Vector2(linear.End.X - linear.Start.X, linear.End.Y - linear.Start.Y);
                var lengthSquared = delta.X * delta.X + delta.Y * delta.Y;
                if (invertible && float.IsFinite(lengthSquared) && lengthSquared > float.Epsilon)
                {
                    rowX = new Vector4(
                        (inverse.M11 * delta.X + inverse.M12 * delta.Y) / lengthSquared,
                        (inverse.M21 * delta.X + inverse.M22 * delta.Y) / lengthSquared,
                        ((inverse.M31 - linear.Start.X) * delta.X + (inverse.M32 - linear.Start.Y) * delta.Y) / lengthSquared, 0);
                }
                break;
            case RadialGradientBrush radial:
                mode = 2; spread = radial.SpreadMethod; values = radial.Stops;
                if (invertible && float.IsFinite(radial.Radius) && radial.Radius > 0)
                {
                    rowX = new Vector4(inverse.M11, inverse.M21, inverse.M31 - radial.Center.X, 0) / radial.Radius;
                    rowY = new Vector4(inverse.M12, inverse.M22, inverse.M32 - radial.Center.Y, 0) / radial.Radius;
                }
                break;
            default:
                throw new NotSupportedException($"Vulkan does not support brush type '{brush.GetType().Name}'.");
        }
        return new Descriptor(mode, (uint)spread, rowX, rowY, values);
    }

    internal static VulkanBrushMaterial Snapshot(in Descriptor descriptor)
    {
        var values = descriptor.Stops;
        var stops = values.Length == 0 ? Array.Empty<VulkanGradientStop>() : new VulkanGradientStop[values.Length];
        for (var i = 0; i < values.Length; i++)
            stops[i] = new VulkanGradientStop(values[i].Offset, Pack(values[i].Color));
        // Keep original order: duplicate offsets choose first upper/last lower, matching the primitive contract.
        return new VulkanBrushMaterial(descriptor.Mode, descriptor.Spread, descriptor.RowX, descriptor.RowY, stops);
    }

    internal static Descriptor DescribeLayer(float opacity) => new(3, 0,
        new Vector4(0, 0, 0, float.IsNaN(opacity) ? 1 : Math.Clamp(opacity, 0, 1)), default, []);

    public static VulkanBrushMaterial Layer(float opacity) => Snapshot(DescribeLayer(opacity));

    internal static int ContentHash(in Descriptor descriptor)
    {
        var hash = new HashCode();
        hash.Add(descriptor.Mode); hash.Add(descriptor.Spread); hash.Add(descriptor.RowX); hash.Add(descriptor.RowY);
        foreach (var stop in descriptor.Stops) hash.Add(new VulkanGradientStop(stop.Offset, Pack(stop.Color)));
        return hash.ToHashCode();
    }

    internal bool Matches(in Descriptor descriptor)
    {
        if (Mode != descriptor.Mode || Spread != descriptor.Spread || !SameBits(RowX, descriptor.RowX) ||
            !SameBits(RowY, descriptor.RowY) || _stops.Length != descriptor.Stops.Length) return false;
        for (var i = 0; i < _stops.Length; i++)
            if (BitConverter.SingleToInt32Bits(_stops[i].Offset) != BitConverter.SingleToInt32Bits(descriptor.Stops[i].Offset) ||
                _stops[i].Color != Pack(descriptor.Stops[i].Color)) return false;
        return true;
    }

    private static uint Pack(Color color) => (uint)(color.R | color.G << 8 | color.B << 16 | color.A << 24);
    private static bool SameBits(Vector4 a, Vector4 b) =>
        BitConverter.SingleToInt32Bits(a.X) == BitConverter.SingleToInt32Bits(b.X) &&
        BitConverter.SingleToInt32Bits(a.Y) == BitConverter.SingleToInt32Bits(b.Y) &&
        BitConverter.SingleToInt32Bits(a.Z) == BitConverter.SingleToInt32Bits(b.Z) &&
        BitConverter.SingleToInt32Bits(a.W) == BitConverter.SingleToInt32Bits(b.W);

    public VulkanMaterialPushConstants ToPushConstants(uint stopOffset, Vector2 targetOrigin)
    {
        var x = RowX;
        var y = RowY;
        if (Mode is 1 or 2)
        {
            x.Z += x.X * targetOrigin.X + x.Y * targetOrigin.Y;
            y.Z += y.X * targetOrigin.X + y.Y * targetOrigin.Y;
        }
        return new VulkanMaterialPushConstants
        {
            RowX = x, RowY = y, Mode = Mode, Spread = Spread,
            StopOffset = stopOffset, StopCount = (uint)_stops.Length
        };
    }

    public bool Equals(VulkanBrushMaterial? other) => other != null && Mode == other.Mode && Spread == other.Spread &&
        RowX == other.RowX && RowY == other.RowY && _stops.AsSpan().SequenceEqual(other._stops);
    public override bool Equals(object? obj) => obj is VulkanBrushMaterial material && Equals(material);
    public override int GetHashCode() => _hash;
}
