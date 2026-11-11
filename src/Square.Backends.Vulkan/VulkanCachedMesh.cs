using System.Runtime.InteropServices;
using Square.Rendering.Tessellation;

namespace Square.Backends.Vulkan;

/// <summary>Immutable value snapshot of device-space vertices and triangle indices.</summary>
internal sealed class VulkanCachedMesh
{
    private readonly Vertex2D[] _vertices;
    private readonly uint[] _indices;

    public static VulkanCachedMesh Empty { get; } = new([], []);
    public ReadOnlySpan<Vertex2D> Vertices => _vertices;
    public ReadOnlySpan<uint> Indices => _indices;
    /// <summary>Conservative payload and managed-object/array overhead estimate.</summary>
    public long ByteSize => 96L + (long)_vertices.Length * 20 + (long)_indices.Length * sizeof(uint);

    public VulkanCachedMesh(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices)
    {
        _vertices = vertices.ToArray();
        _indices = indices.ToArray();
    }

    public bool Matches(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices)
        => MemoryMarshal.AsBytes(Vertices).SequenceEqual(MemoryMarshal.AsBytes(vertices))
            && Indices.SequenceEqual(indices);
}
