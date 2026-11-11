using System.Runtime.InteropServices;
using System.Text;

namespace CanvasBenchmark.Observer;

// Read-only desktop/window identity probes. No desktop/station/affinity setters exist here.
internal static class DesktopIdentity
{
    private sealed record ObjectName(string? Name, int Win32Error, uint RequiredBytes);
    private static ObjectName Name(nint handle)
    {
        if (handle == 0) return new(null, Marshal.GetLastWin32Error(), 0);
        var buffer = new StringBuilder(512);
        var ok = GetUserObjectInformation(handle, 2, buffer, 1024, out var required);
        return new(ok ? buffer.ToString() : null, ok ? 0 : Marshal.GetLastWin32Error(), required);
    }

    internal static object Read(nint window)
    {
        var currentThreadId = GetCurrentThreadId();
        var targetThreadId = Native.GetWindowThreadProcessId(window, out var ownerPid);
        var currentDesktop = Name(GetThreadDesktop(currentThreadId));
        var windowStation = Name(GetProcessWindowStation());
        var targetDesktop = Name(GetThreadDesktop(targetThreadId));
        var input = OpenInputDesktop(0, false, 0x0001); // DESKTOP_READOBJECTS only.
        ObjectName inputDesktop;
        try { inputDesktop = Name(input); }
        finally { if (input != 0) CloseDesktop(input); }
        var cloakResult = DwmGetWindowAttribute(window, 14, out var cloaked, 4); // DWMWA_CLOAKED
        var affinityOk = GetWindowDisplayAffinity(window, out var affinity);
        var affinityError = affinityOk ? 0 : Marshal.GetLastWin32Error();
        var monitor = MonitorFromWindow(window, 2);
        var monitorInfo = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>(), DeviceName = "" };
        var monitorOk = GetMonitorInfo(monitor, ref monitorInfo);
        var monitorError = monitorOk ? 0 : Marshal.GetLastWin32Error();
        return new
        {
            currentThreadId, targetThreadId, ownerPid, currentDesktop, windowStation, targetDesktop, inputDesktop,
            cloaking = new { hResult = $"0x{cloakResult:X8}", value = cloakResult >= 0 ? (uint?)cloaked : null },
            displayAffinity = new { success = affinityOk, win32Error = affinityError,
                value = affinityOk ? (uint?)affinity : null,
                name = affinityOk ? affinity switch { 0 => "WDA_NONE", 1 => "WDA_MONITOR", 0x11 => "WDA_EXCLUDEFROMCAPTURE", _ => "unknown" } : "unavailable" },
            targetMonitor = new { handle = $"0x{monitor:X}", success = monitorOk, win32Error = monitorError,
                deviceName = monitorOk ? monitorInfo.DeviceName : null,
                bounds = new { monitorInfo.Monitor.Left, monitorInfo.Monitor.Top, monitorInfo.Monitor.Right, monitorInfo.Monitor.Bottom } },
            policy = "Read-only names, cloaking, affinity and monitor queries; no desktop switching or affinity changes."
        };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        internal uint Size;
        internal Native.Rect Monitor, Work;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string DeviceName;
    }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] private static extern nint GetThreadDesktop(uint threadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint GetProcessWindowStation();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetUserObjectInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetUserObjectInformation(nint handle, int index, StringBuilder value, uint bytes, out uint required);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseDesktop(nint desktop);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, uint attribute, out uint value, uint size);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
