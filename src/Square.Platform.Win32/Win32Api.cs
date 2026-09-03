using System.Runtime.InteropServices;

namespace Square.Platform.Win32;

internal delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

internal static partial class Win32Api
{
    public const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_BORDER = 0x00800000;
    public const int WS_CAPTION = 0x00C00000;
    public const int WS_SYSMENU = 0x00080000;
    public const int WS_THICKFRAME = 0x00040000;
    public const int WS_MINIMIZEBOX = 0x00020000;
    public const int WS_MAXIMIZEBOX = 0x00010000;
    public const int WS_VISIBLE = 0x10000000;
    public const int SW_SHOW = 5;
    public const int SW_MINIMIZE = 6;
    public const int SW_MAXIMIZE = 3;
    public const int SW_RESTORE = 9;
    public const int WM_CLOSE = 0x0010;
    public const int WM_DESTROY = 0x0002;
    public const int WM_PAINT = 0x000F;
    public const int WM_NCACTIVATE = 0x0086;
    public const int WM_SETCURSOR = 0x0020;
    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int WM_NCCALCSIZE = 0x0083;
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_NCPAINT = 0x0085;
    public const int WM_SIZE = 0x0005;
    public const int WM_SYSCOMMAND = 0x0112;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_MOUSEHWHEEL = 0x020E;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_CHAR = 0x0102;
    public const int WM_UNICHAR = 0x0109;
    public const int WM_TIMER = 0x0113;
    public const int WM_IME_STARTCOMPOSITION = 0x010D;
    public const int WM_QUIT = 0x0012;
    public const int CS_HREDRAW = 0x0002;
    public const int CS_VREDRAW = 0x0001;
    public const uint BI_RGB = 0;
    public const uint DIB_RGB_COLORS = 0;
    public const uint SRCCOPY = 0x00CC0020;
    public const uint PW_RENDERFULLCONTENT = 0x00000002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const int UNICODE_NOCHAR = 0xFFFF;
    public const int HTCLIENT = 1;
    public const int HTCAPTION = 2;
    public const int HTLEFT = 10;
    public const int HTRIGHT = 11;
    public const int HTTOP = 12;
    public const int HTTOPLEFT = 13;
    public const int HTTOPRIGHT = 14;
    public const int HTBOTTOM = 15;
    public const int HTBOTTOMLEFT = 16;
    public const int HTBOTTOMRIGHT = 17;
    public const int MA_ACTIVATE = 1;
    public const int SIZE_RESTORED = 0;
    public const int SIZE_MINIMIZED = 1;
    public const int SIZE_MAXIMIZED = 2;
    public const int SC_MOVE = 0xF010;
    public const int IDC_ARROW = 32512;
    public const int IDC_SIZEWE = 32644;
    public const int IDC_SIZENS = 32645;
    public const int IDC_IBEAM = 32513;
    public const int IDC_HAND = 32649;
    public const int SM_CXSIZEFRAME = 32;
    public const int SM_CXPADDEDBORDER = 92;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND = 2;
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;
    public const uint CF_UNICODETEXT = 13;
    public const uint GMEM_MOVEABLE = 0x0002;
    public const uint GMEM_ZEROINIT = 0x0040;
    public const uint CFS_POINT = 0x0002;
    public const uint CFS_EXCLUDE = 0x0080;
    public static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new(-4);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("user32.dll", EntryPoint = "SetProcessDpiAwarenessContext")]
    public static partial bool SetProcessDpiAwarenessContext(IntPtr value);

    [LibraryImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    public static partial uint GetDpiForWindow(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetSystemMetricsForDpi")]
    public static partial int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    public static partial int DwmSetWindowAttribute(
        IntPtr hWnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    [LibraryImport("user32.dll", SetLastError = true, EntryPoint = "CreateWindowExW",
        StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    public static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("user32.dll", EntryPoint = "UpdateWindow")]
    public static partial bool UpdateWindow(IntPtr hWnd);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("user32.dll", EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial bool SetWindowText(IntPtr hWnd, string lpString);

    [LibraryImport("user32.dll", EntryPoint = "SetActiveWindow")]
    public static partial IntPtr SetActiveWindow(IntPtr hWnd);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("user32.dll", EntryPoint = "EnableWindow")]
    public static partial bool EnableWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool enable);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow")]
    public static partial bool DestroyWindow(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    public static partial int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    public static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    public static partial IntPtr DispatchMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "PostQuitMessage")]
    public static partial void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX lpWndClass);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    public static partial IntPtr DefWindowProc(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetDC")]
    public static partial IntPtr GetDC(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowDC")]
    public static partial IntPtr GetWindowDC(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "ReleaseDC")]
    public static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("user32.dll", EntryPoint = "EnumWindows")]
    public static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("user32.dll", EntryPoint = "IsWindowVisible")]
    public static partial bool IsWindowVisible(IntPtr hWnd);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect")]
    public static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("user32.dll", EntryPoint = "PrintWindow")]
    public static partial bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint flags);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateCompatibleDC")]
    public static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateCompatibleBitmap")]
    public static partial IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateDIBSection")]
    public static partial IntPtr CreateDIBSection(
        IntPtr hdc,
        ref BITMAPINFO bitmapInfo,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [LibraryImport("gdi32.dll", EntryPoint = "SelectObject")]
    public static partial IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject")]
    public static partial bool DeleteObject(IntPtr ho);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("gdi32.dll", EntryPoint = "DeleteDC")]
    public static partial bool DeleteDC(IntPtr hdc);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("gdi32.dll", EntryPoint = "BitBlt")]
    public static partial bool BitBlt(
        IntPtr hdc,
        int x,
        int y,
        int cx,
        int cy,
        IntPtr hdcSrc,
        int x1,
        int y1,
        uint rop);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("gdi32.dll", EntryPoint = "StretchBlt")]
    public static partial bool StretchBlt(
        IntPtr hdcDest,
        int xDest,
        int yDest,
        int widthDest,
        int heightDest,
        IntPtr hdcSrc,
        int xSrc,
        int ySrc,
        int widthSrc,
        int heightSrc,
        uint rop);

    [LibraryImport("gdi32.dll", EntryPoint = "GetDIBits")]
    public static partial int GetDIBits(
        IntPtr hdc,
        IntPtr hbm,
        uint start,
        uint cLines,
        byte[] lpvBits,
        ref BITMAPINFO lpbmi,
        uint usage);

    [LibraryImport("gdi32.dll", EntryPoint = "StretchDIBits")]
    public static partial int StretchDIBits(
        IntPtr hdc,
        int xDest, int yDest, int destWidth, int destHeight,
        int xSrc, int ySrc, int srcWidth, int srcHeight,
        IntPtr bits,
        ref BITMAPINFO bitmapInfo,
        uint usage,
        uint rasterOperation);

    [DllImport("user32.dll", EntryPoint = "BeginPaint")]
    public static extern IntPtr BeginPaint(IntPtr hwnd, ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll", EntryPoint = "EndPaint")]
    public static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT lpPaint);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("user32.dll", EntryPoint = "GetClientRect")]
    public static partial bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("user32.dll", EntryPoint = "SetWindowPos")]
    public static partial bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    public static partial IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

    [LibraryImport("user32.dll", EntryPoint = "SetCursor")]
    public static partial IntPtr SetCursor(IntPtr cursor);

    [LibraryImport("user32.dll", EntryPoint = "SetCapture")]
    public static partial IntPtr SetCapture(IntPtr hWnd);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("user32.dll", EntryPoint = "ReleaseCapture")]
    public static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "SetTimer")]
    public static partial UIntPtr SetTimer(IntPtr hWnd, UIntPtr timerId, uint intervalMilliseconds, IntPtr callback);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("user32.dll", EntryPoint = "KillTimer")]
    public static partial bool KillTimer(IntPtr hWnd, UIntPtr timerId);

    [LibraryImport("user32.dll", EntryPoint = "GetKeyState")]
    public static partial short GetKeyState(int virtualKey);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("user32.dll", EntryPoint = "GetCursorPos")]
    public static partial bool GetCursorPos(out POINT point);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("user32.dll", EntryPoint = "ScreenToClient")]
    public static partial bool ScreenToClient(IntPtr hWnd, ref POINT point);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr GetModuleHandle(string? lpModuleName);

    [LibraryImport("imm32.dll", EntryPoint = "ImmGetContext")]
    public static partial IntPtr ImmGetContext(IntPtr hWnd);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("imm32.dll", EntryPoint = "ImmReleaseContext")]
    public static partial bool ImmReleaseContext(IntPtr hWnd, IntPtr inputContext);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("imm32.dll", EntryPoint = "ImmSetCompositionWindow")]
    public static partial bool ImmSetCompositionWindow(IntPtr inputContext, ref COMPOSITIONFORM form);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("imm32.dll", EntryPoint = "ImmSetCandidateWindow")]
    public static partial bool ImmSetCandidateWindow(IntPtr inputContext, ref CANDIDATEFORM form);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("user32.dll", EntryPoint = "OpenClipboard")]
    public static partial bool OpenClipboard(IntPtr owner);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("user32.dll", EntryPoint = "CloseClipboard")]
    public static partial bool CloseClipboard();

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("user32.dll", EntryPoint = "EmptyClipboard")]
    public static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", EntryPoint = "GetClipboardData")]
    public static partial IntPtr GetClipboardData(uint format);

    [LibraryImport("user32.dll", EntryPoint = "SetClipboardData")]
    public static partial IntPtr SetClipboardData(uint format, IntPtr memory);

    [LibraryImport("kernel32.dll", EntryPoint = "GlobalAlloc")]
    public static partial IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [LibraryImport("kernel32.dll", EntryPoint = "GlobalLock")]
    public static partial IntPtr GlobalLock(IntPtr memory);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("kernel32.dll", EntryPoint = "GlobalUnlock")]
    public static partial bool GlobalUnlock(IntPtr memory);

    [LibraryImport("kernel32.dll", EntryPoint = "GlobalFree")]
    public static partial IntPtr GlobalFree(IntPtr memory);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;
        public int reserved1;
        public int reserved2;
        public int reserved3;
        public int reserved4;
        public int reserved5;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct COMPOSITIONFORM
    {
        public uint Style;
        public POINT CurrentPosition;
        public RECT Area;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CANDIDATEFORM
    {
        public uint Index;
        public uint Style;
        public POINT CurrentPosition;
        public RECT Area;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public int cbSize;
        public int style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }
}
