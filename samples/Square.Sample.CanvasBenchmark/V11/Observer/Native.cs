using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace CanvasBenchmark.Observer;

internal static class Native
{
    internal delegate bool WindowCallback(nint window, nint parameter);
    [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct BitmapInfo
    {
        internal uint Size; internal int Width, Height; internal ushort Planes, BitCount;
        internal uint Compression, SizeImage; internal int XPelsPerMeter, YPelsPerMeter;
        internal uint ClrUsed, ClrImportant, Color;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct ProcessEntry
    {
        internal uint Size, Usage, Pid; internal nuint DefaultHeapId;
        internal uint ModuleId, Threads, ParentPid; internal int Priority; internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string ExeFile;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct MemoryCounters
    {
        internal uint Size, PageFaultCount;
        internal nuint PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
            QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] internal static extern nint GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool AreDpiAwarenessContextsEqual(nint a, nint b);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsIconic(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    internal static extern int GetWindowText(nint window, StringBuilder text, int maxCount);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ClientToScreen(nint window, ref Point point);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint GetDC(nint window);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] internal static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] internal static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] internal static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool BitBlt(nint dc, int x, int y, int width, int height, nint source, int sx, int sy, uint operation);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GdiFlush();
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern nint CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Process32First(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Process32Next(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool K32GetProcessMemoryInfo(nint process, ref MemoryCounters counters, uint size);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool TerminateProcess(nint process, uint exitCode);
    internal static void Require(bool ok, string operation) { if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), operation); }
}

internal sealed record WindowInfo(string Hwnd, string Title, int OwnerPid, double FirstFoundMs,
    int ClientWidth, int ClientHeight, int ScreenX, int ScreenY, uint Dpi, double DevicePixelRatio,
    bool TopmostWithoutActivation);

internal static class WindowLookup
{
    internal static nint Find(int rootPid)
    {
        nint found = 0;
        Native.Require(Native.EnumWindows((window, _) =>
        {
            Native.GetWindowThreadProcessId(window, out var pid);
            if (pid != rootPid || !Native.IsWindowVisible(window) || Native.IsIconic(window)) return true;
            var title = new StringBuilder(512);
            Native.GetWindowText(window, title, title.Capacity);
            if (!title.ToString().Contains("Canvas v11", StringComparison.OrdinalIgnoreCase)) return true;
            found = window;
            return true;
        }, 0), "EnumWindows");
        return found;
    }

    internal static WindowInfo Validate(nint window, int rootPid, double atMs, bool makeTopmost)
    {
        Native.GetWindowThreadProcessId(window, out var owner);
        if (owner != rootPid || !Native.IsWindowVisible(window) || Native.IsIconic(window))
            throw new InvalidOperationException("Owned benchmark window is no longer visible.");
        Native.Require(Native.GetClientRect(window, out var client), "GetClientRect");
        var origin = new Native.Point();
        Native.Require(Native.ClientToScreen(window, ref origin), "ClientToScreen");
        var width = client.Right - client.Left; var height = client.Bottom - client.Top;
        var dpi = Native.GetDpiForWindow(window);
        if (width != 1280 || height != 800 || dpi != 96)
            throw new InvalidOperationException($"Expected physical client 1280x800 / DPR1; got {width}x{height}, DPI={dpi}.");
        var left = Native.GetSystemMetrics(76); var top = Native.GetSystemMetrics(77);
        if (origin.X < left || origin.Y < top || origin.X + width > left + Native.GetSystemMetrics(78) ||
            origin.Y + height > top + Native.GetSystemMetrics(79))
            throw new InvalidOperationException("Benchmark client is not wholly inside the virtual desktop.");
        if (makeTopmost)
            Native.Require(Native.SetWindowPos(window, new nint(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010), "SetWindowPos TOPMOST NOACTIVATE");
        var title = new StringBuilder(512);
        Native.GetWindowText(window, title, title.Capacity);
        return new($"0x{window:X}", title.ToString(), rootPid, atMs, width, height, origin.X, origin.Y, dpi, dpi / 96d, true);
    }
}

internal sealed class DesktopCapture : IDisposable
{
    private nint _desktop, _memory, _bitmap, _old, _bits;
    internal DesktopCapture()
    {
        try
        {
            _desktop = Native.GetDC(0); Native.Require(_desktop != 0, "GetDC desktop");
            _memory = Native.CreateCompatibleDC(_desktop); Native.Require(_memory != 0, "CreateCompatibleDC");
            var info = new Native.BitmapInfo { Size = 40, Width = 96, Height = -8, Planes = 1, BitCount = 32 };
            _bitmap = Native.CreateDIBSection(_desktop, ref info, 0, out _bits, 0, 0);
            Native.Require(_bitmap != 0 && _bits != 0, "CreateDIBSection");
            _old = Native.SelectObject(_memory, _bitmap);
            Native.Require(_old != 0 && _old != new nint(-1), "SelectObject");
        }
        catch { Dispose(); throw; }
    }

    internal unsafe ReadOnlySpan<byte> Capture(int x, int y)
    {
        Native.Require(Native.BitBlt(_memory, 0, 0, 96, 8, _desktop, x, y, 0x00CC0020 | 0x40000000), "BitBlt desktop ROI");
        Native.Require(Native.GdiFlush(), "GdiFlush");
        return new ReadOnlySpan<byte>((void*)_bits, 96 * 8 * 4);
    }

    public void Dispose()
    {
        if (_old != 0 && _old != new nint(-1) && _memory != 0) Native.SelectObject(_memory, _old);
        if (_bitmap != 0) Native.DeleteObject(_bitmap);
        if (_memory != 0) Native.DeleteDC(_memory);
        if (_desktop != 0) Native.ReleaseDC(0, _desktop);
        _desktop = _memory = _bitmap = _old = _bits = 0;
    }
}
