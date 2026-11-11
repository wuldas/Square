using System.Runtime.InteropServices;
using Square.Graphics;

internal static class WindowFrame
{
    internal static IntPtr SizeForClient(int processId, int width, int height)
    {
        var handle = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != (uint)processId) return true;
            var title = new System.Text.StringBuilder(512);
            GetWindowText(window, title, title.Capacity);
            if (!title.ToString().Contains("Canvas v11", StringComparison.OrdinalIgnoreCase)) return true;
            handle = window; return false;
        }, IntPtr.Zero);
        if (handle == IntPtr.Zero) throw new InvalidOperationException("Window not found");
        var dpi = GetDpiForWindow(handle);
        if (dpi != 96) throw new InvalidOperationException("DPR1 desktop required");
        var rect = new NativeRect { Right = width, Bottom = height };
        var style = unchecked((uint)GetWindowLong(handle, -16));
        if (!AdjustWindowRectExForDpi(ref rect, style, false, 0, dpi) || !SetWindowPos(handle, new IntPtr(-1), 64, 64, rect.Right - rect.Left, rect.Bottom - rect.Top, 0x0010 | 0x0020))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        return handle;
    }
    internal static Bitmap CropClient(IntPtr handle, Bitmap window)
    {
        var origin = new NativePoint();
        if (!GetClientRect(handle, out var client) || !GetWindowRect(handle, out var outer) || !ClientToScreen(handle, ref origin)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        var x = origin.X - outer.Left; var y = origin.Y - outer.Top;
        var result = new Bitmap(client.Right - client.Left, client.Bottom - client.Top);
        for (var row = 0; row < result.Height; row++) window.Pixels.AsSpan((y + row) * window.Stride + x * 4, result.Width * 4).CopyTo(result.GetRow(row));
        return result;
    }
    // Desktop DIB contract: V11/Observer/Native.cs:116; diagnostics only.
    internal static Bitmap CaptureDesktopClient(IntPtr handle)
    {
        var origin = new NativePoint();
        if (!ClientToScreen(handle, ref origin) || !GetClientRect(handle, out var rect)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        var desktop = GetDC(IntPtr.Zero);
        if (desktop == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "GetDC desktop");
        var memory = IntPtr.Zero; var bitmap = IntPtr.Zero; var previous = IntPtr.Zero;
        try
        {
            memory = CreateCompatibleDC(desktop);
            if (memory == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "CreateCompatibleDC");
            var info = new BitmapInfo { Size = 40, Width = rect.Right, Height = -rect.Bottom, Planes = 1, BitCount = 32 };
            bitmap = CreateDIBSection(desktop, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "CreateDIBSection");
            previous = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, rect.Right, rect.Bottom, desktop, origin.X, origin.Y, 0x00CC0020 | 0x40000000)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "BitBlt desktop client");
            GdiFlush();
            var result = new Bitmap(rect.Right, rect.Bottom);
            Marshal.Copy(bits, result.Pixels, 0, result.Pixels.Length);
            return result;
        }
        finally
        {
            if (previous != IntPtr.Zero && memory != IntPtr.Zero) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, desktop);
        }
    }
    internal static void SaveBmp(string path, Bitmap bitmap)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((ushort)0x4D42); writer.Write(54 + bitmap.Pixels.Length); writer.Write(0); writer.Write(54);
        writer.Write(40); writer.Write(bitmap.Width); writer.Write(-bitmap.Height);
        writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0); writer.Write(bitmap.Pixels.Length);
        writer.Write(2835); writer.Write(2835); writer.Write(0); writer.Write(0); writer.Write(bitmap.Pixels);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    private delegate bool EnumCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")] private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool AdjustWindowRectExForDpi(ref NativeRect rect, uint style, bool menu, uint exStyle, uint dpi);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, SizeImage; public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant, Color;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool BitBlt(IntPtr dc, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint operation);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("dwmapi.dll")] internal static extern int DwmFlush();
}
