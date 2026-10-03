using System;
using System.Runtime.InteropServices;
using Square.Graphics;
using Square.Platform;

namespace Square.Backends.Conformance.Tests;

/// <summary>
/// 截取真实可见 HWND 的屏幕合成结果(PrintWindow 对 D2D 窗口可能返回未合成的空白/错误像素)。
/// 按 client 区域截取,坐标与 RenderContext 的 client 逻辑坐标一致。
/// </summary>
internal static class VisibleHwndCapture
{
    public static Bitmap Capture(IPlatformHost host)
    {
        var hwnd = (IntPtr)host.GetType().GetProperty("Handle")!.GetValue(host)!;
        if (!GetClientRect(hwnd, out var client) || client.Right <= 0 || client.Bottom <= 0)
            throw new InvalidOperationException("GetClientRect failed");
        var origin = new Point { X = 0, Y = 0 };
        if (!ClientToScreen(hwnd, ref origin))
            throw new InvalidOperationException("ClientToScreen failed");
        var width = client.Right;
        var height = client.Bottom;
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var header = new Header { Size = 40, Width = width, Height = -height, Planes = 1, Bits = 32 };
        var dib = CreateDIBSection(screen, ref header, 0, out var bits, IntPtr.Zero, 0);
        var previous = SelectObject(memory, dib);
        try
        {
            if (!BitBlt(memory, 0, 0, width, height, screen, origin.X, origin.Y, 0x40CC0020))
                throw new InvalidOperationException("Visible HWND screen copy failed");
            var result = new Bitmap(width, height);
            Marshal.Copy(bits, result.Pixels, 0, result.Pixels.Length);
            for (var i = 3; i < result.Pixels.Length; i += 4) result.Pixels[i] = 255;
            result.MarkDirty();
            return result;
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(dib);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Header
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, Bits;
        public uint Compression, ImageSize;
        public int XResolution, YResolution;
        public uint Colors, ImportantColors;
    }
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref Header header, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dc, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
}
