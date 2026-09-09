using System.Runtime.InteropServices;

namespace Square.Rendering.Tessellation;

/// <summary>共享二维 GPU 顶点布局：位置、UV 与 RGBA8 颜色，共 20 字节。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Vertex2D
{
    public float X, Y;
    public float U, V;
    public uint Color;

    public Vertex2D(float x, float y, float u, float v, uint color)
    {
        X = x;
        Y = y;
        U = u;
        V = v;
        Color = color;
    }
}
