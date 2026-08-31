using Square.Graphics;

namespace Square.Platform.Win32;

internal static class Win32WindowScreenshot
{
    public static bool TryCaptureByProcessId(int processId, out Bitmap? bitmap)
    {
        bitmap = null;
        var hwnd = FindTopLevelWindow(processId);
        return hwnd != IntPtr.Zero && TryCaptureWindow(hwnd, out bitmap);
    }

    private static IntPtr FindTopLevelWindow(int processId)
    {
        var result = IntPtr.Zero;
        Win32Api.EnumWindows((hwnd, lParam) =>
        {
            Win32Api.GetWindowThreadProcessId(hwnd, out var windowProcessId);
            if (windowProcessId != (uint)processId || !Win32Api.IsWindowVisible(hwnd))
                return true;

            if (!Win32Api.GetWindowRect(hwnd, out var rect) || rect.Width <= 0 || rect.Height <= 0)
                return true;

            result = hwnd;
            return false;
        }, IntPtr.Zero);

        return result;
    }

    private static bool TryCaptureWindow(IntPtr hwnd, out Bitmap? bitmap)
    {
        bitmap = null;
        if (!Win32Api.GetWindowRect(hwnd, out var rect))
            return false;

        var width = rect.Width;
        var height = rect.Height;
        if (width <= 0 || height <= 0)
            return false;

        var windowDc = Win32Api.GetWindowDC(hwnd);
        if (windowDc == IntPtr.Zero)
            return false;

        var memoryDc = IntPtr.Zero;
        var hbitmap = IntPtr.Zero;
        var previousObject = IntPtr.Zero;
        try
        {
            memoryDc = Win32Api.CreateCompatibleDC(windowDc);
            if (memoryDc == IntPtr.Zero)
                return false;

            hbitmap = Win32Api.CreateCompatibleBitmap(windowDc, width, height);
            if (hbitmap == IntPtr.Zero)
                return false;

            previousObject = Win32Api.SelectObject(memoryDc, hbitmap);
            // PrintWindow can report success for a Direct2D HWND while returning an
            // uncomposited blank client area. Prefer the visible window surface and use
            // PrintWindow only when the screen copy is unavailable.
            var copied = Win32Api.BitBlt(memoryDc, 0, 0, width, height, windowDc, 0, 0, Win32Api.SRCCOPY);
            if (!copied && !Win32Api.PrintWindow(hwnd, memoryDc, Win32Api.PW_RENDERFULLCONTENT))
                return false;

            var info = new Win32Api.BITMAPINFO
            {
                bmiHeader = new Win32Api.BITMAPINFOHEADER
                {
                    biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32Api.BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = Win32Api.BI_RGB,
                    biSizeImage = (uint)(width * height * 4)
                }
            };

            var captured = new Bitmap(width, height);
            var scanLines = Win32Api.GetDIBits(
                memoryDc,
                hbitmap,
                0,
                (uint)height,
                captured.Pixels,
                ref info,
                Win32Api.DIB_RGB_COLORS);

            if (scanLines == 0)
            {
                captured.Dispose();
                return false;
            }

            bitmap = captured;
            return true;
        }
        finally
        {
            if (previousObject != IntPtr.Zero && memoryDc != IntPtr.Zero)
                Win32Api.SelectObject(memoryDc, previousObject);
            if (hbitmap != IntPtr.Zero)
                Win32Api.DeleteObject(hbitmap);
            if (memoryDc != IntPtr.Zero)
                Win32Api.DeleteDC(memoryDc);
            Win32Api.ReleaseDC(hwnd, windowDc);
        }
    }
}
