using System.Runtime.InteropServices;

namespace Square.Platform.X11;

internal static partial class X11Api
{
    public const int Success = 0;
    public const int True = 1;
    public const int False = 0;
    public const int QueuedAfterFlush = 2;

    public const long CopyFromParent = 0L;
    public const long ParentRelative = 1L;

    public const uint InputOutput = 1;
    public const uint InputOnly = 2;

    public const uint CWBackPixel = 0x00000002;
    public const uint CWBorderPixel = 0x00000008;
    public const uint CWBitGravity = 0x00000010;
    public const uint CWEventMask = 0x00000800;
    public const uint CWDontPropagate = 0x00001000;
    public const uint CWColormap = 0x00002000;
    public const uint CWCursor = 0x00004000;

    public const long NoEventMask = 0L;
    public const long KeyPressMask = 0x00000001L;
    public const long KeyReleaseMask = 0x00000002L;
    public const long ButtonPressMask = 0x00000004L;
    public const long ButtonReleaseMask = 0x00000008L;
    public const long EnterWindowMask = 0x00000010L;
    public const long LeaveWindowMask = 0x00000020L;
    public const long PointerMotionMask = 0x00000040L;
    public const long PointerMotionHintMask = 0x00000080L;
    public const long Button1MotionMask = 0x00000100L;
    public const long Button2MotionMask = 0x00000200L;
    public const long Button3MotionMask = 0x00000400L;
    public const long Button4MotionMask = 0x00000800L;
    public const long Button5MotionMask = 0x00001000L;
    public const long ButtonMotionMask = 0x00002000L;
    public const long KeymapStateMask = 0x00004000L;
    public const long ExposureMask = 0x00008000L;
    public const long VisibilityChangeMask = 0x00010000L;
    public const long StructureNotifyMask = 0x00020000L;
    public const long ResizeRedirectMask = 0x00040000L;
    public const long SubstructureNotifyMask = 0x00080000L;
    public const long SubstructureRedirectMask = 0x00100000L;
    public const long FocusChangeMask = 0x00200000L;
    public const long PropertyChangeMask = 0x00400000L;
    public const long ColormapChangeMask = 0x00800000L;
    public const long OwnerGrabButtonMask = 0x01000000L;

    public const int KeyPress = 2;
    public const int KeyRelease = 3;
    public const int ButtonPress = 4;
    public const int ButtonRelease = 5;
    public const int MotionNotify = 6;
    public const int EnterNotify = 7;
    public const int LeaveNotify = 8;
    public const int FocusIn = 9;
    public const int FocusOut = 10;
    public const int KeymapNotify = 11;
    public const int Expose = 12;
    public const int GraphicsExpose = 13;
    public const int NoExpose = 14;
    public const int VisibilityNotify = 15;
    public const int CreateNotify = 16;
    public const int DestroyNotify = 17;
    public const int UnmapNotify = 18;
    public const int MapNotify = 19;
    public const int MapRequest = 20;
    public const int ReparentNotify = 21;
    public const int ConfigureNotify = 22;
    public const int ConfigureRequest = 23;
    public const int GravityNotify = 24;
    public const int ResizeRequest = 25;
    public const int CirculateNotify = 26;
    public const int CirculateRequest = 27;
    public const int PropertyNotify = 28;
    public const int SelectionClear = 29;
    public const int SelectionRequest = 30;
    public const int SelectionNotify = 31;
    public const int ColormapNotify = 32;
    public const int ClientMessage = 33;
    public const int MappingNotify = 34;

    public const int Button1 = 1;
    public const int Button2 = 2;
    public const int Button3 = 3;
    public const int Button4 = 4;
    public const int Button5 = 5;

    public const int ShiftMapIndex = 0;
    public const int LockMapIndex = 1;
    public const int ControlMapIndex = 2;
    public const int Mod1MapIndex = 3;
    public const int Mod2MapIndex = 4;
    public const int Mod3MapIndex = 5;
    public const int Mod4MapIndex = 6;
    public const int Mod5MapIndex = 7;

    public const int XK_BackSpace = 0xFF08;
    public const int XK_Tab = 0xFF09;
    public const int XK_Return = 0xFF0D;
    public const int XK_Escape = 0xFF1B;
    public const int XK_Home = 0xFF50;
    public const int XK_Left = 0xFF51;
    public const int XK_Up = 0xFF52;
    public const int XK_Right = 0xFF53;
    public const int XK_Down = 0xFF54;
    public const int XK_End = 0xFF57;
    public const int XK_Insert = 0xFF63;
    public const int XK_Delete = 0xFFFF;
    public const int XK_KP_0 = 0xFFB0;
    public const int XK_KP_9 = 0xFFB9;
    public const int XK_KP_Enter = 0xFF8D;
    public const int XK_KP_Home = 0xFF95;
    public const int XK_KP_Left = 0xFF96;
    public const int XK_KP_Up = 0xFF97;
    public const int XK_KP_Right = 0xFF98;
    public const int XK_KP_Down = 0xFF99;
    public const int XK_KP_End = 0xFF9C;
    public const int XK_KP_Insert = 0xFF9E;
    public const int XK_KP_Delete = 0xFF9F;
    public const int XK_Shift_L = 0xFFE1;
    public const int XK_Shift_R = 0xFFE2;
    public const int XK_Control_L = 0xFFE3;
    public const int XK_Control_R = 0xFFE4;
    public const int XK_Alt_L = 0xFFE9;
    public const int XK_Alt_R = 0xFFEA;
    public const int XK_Num_Lock = 0xFF7F;
    public const int XK_KP_Multiply = 0xFFAA;
    public const int XK_KP_Add = 0xFFAB;
    public const int XK_KP_Separator = 0xFFAC;
    public const int XK_KP_Subtract = 0xFFAD;
    public const int XK_KP_Decimal = 0xFFAE;
    public const int XK_KP_Divide = 0xFFAF;

    // XIM / input method
    public const long XIMPreeditPosition = 0x0004L;
    public const long XIMPreeditNothing = 0x0008L;
    public const long XIMStatusNothing = 0x0400L;
    public const int XBufferOverflow = -1;
    public const int XLookupNone = 1;
    public const int XLookupChars = 2;
    public const int XLookupKeySym = 3;
    public const int XLookupBoth = 4;

    public const int ZPixmap = 2;
    public const int PropModeReplace = 0;
    public const uint MotifWmHintsDecorations = 1u << 1;
    public const uint MotifWmDecorBorder = 1u << 1;
    public const uint MotifWmDecorResizeH = 1u << 2;
    public const int IconicState = 3;
    public const int NetWmStateRemove = 0;
    public const int NetWmStateAdd = 1;
    public const int NetWmMoveresizeMove = 8;
    public const int NetWmSourceApplication = 1;
    public const uint AllPlanes = uint.MaxValue;
    public const int BitmapPad = 32;
    public const int LSBFirst = 0;
    public const int MSBFirst = 1;

    public const IntPtr None = 0;

    [LibraryImport("libX11.so.6", EntryPoint = "XOpenDisplay", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr OpenDisplay(string? name);

    [LibraryImport("libX11.so.6", EntryPoint = "XCloseDisplay")]
    public static partial int CloseDisplay(IntPtr display);

    [LibraryImport("libX11.so.6", EntryPoint = "XDefaultRootWindow")]
    public static partial IntPtr DefaultRootWindow(IntPtr display);

    [LibraryImport("libX11.so.6", EntryPoint = "XDefaultScreen")]
    public static partial int DefaultScreen(IntPtr display);

    [LibraryImport("libX11.so.6", EntryPoint = "XResourceManagerString")]
    public static partial IntPtr XResourceManagerString(IntPtr display);

    [LibraryImport("libX11.so.6", EntryPoint = "XBlackPixel")]
    public static partial nuint BlackPixel(IntPtr display, int screen);

    [LibraryImport("libX11.so.6", EntryPoint = "XWhitePixel")]
    public static partial nuint WhitePixel(IntPtr display, int screen);

    [LibraryImport("libX11.so.6", EntryPoint = "XDefaultVisual")]
    public static partial IntPtr DefaultVisual(IntPtr display, int screen);

    [LibraryImport("libX11.so.6", EntryPoint = "XDefaultDepth")]
    public static partial int DefaultDepth(IntPtr display, int screen);

    [LibraryImport("libX11.so.6", EntryPoint = "XDefaultColormap")]
    public static partial IntPtr DefaultColormap(IntPtr display, int screen);

    [DllImport("libX11.so.6", EntryPoint = "XCreateWindow",
        CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindow(
        IntPtr display, IntPtr parent,
        int x, int y, uint width, uint height,
        uint borderWidth, int depth, uint windowClass,
        IntPtr visual, nuint valueMask, ref XSetWindowAttributes attributes);

    [LibraryImport("libX11.so.6", EntryPoint = "XMapWindow")]
    public static partial int MapWindow(IntPtr display, IntPtr window);

    [LibraryImport("libX11.so.6", EntryPoint = "XUnmapWindow")]
    public static partial int UnmapWindow(IntPtr display, IntPtr window);

    [LibraryImport("libX11.so.6", EntryPoint = "XIconifyWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IconifyWindow(IntPtr display, IntPtr window, int screen);

    [LibraryImport("libX11.so.6", EntryPoint = "XDestroyWindow")]
    public static partial int DestroyWindow(IntPtr display, IntPtr window);

    [LibraryImport("libX11.so.6", EntryPoint = "XSetInputFocus")]
    public static partial int SetInputFocus(IntPtr display, IntPtr focus, int revertTo, IntPtr time);

    public const int RevertToParent = 2;
    public static readonly IntPtr CurrentTime = 0;

    [LibraryImport("libX11.so.6", EntryPoint = "XMapRaised")]
    public static partial int MapRaised(IntPtr display, IntPtr window);

    [LibraryImport("libX11.so.6", EntryPoint = "XGetInputFocus")]
    public static partial void GetInputFocus(IntPtr display, out IntPtr focus, out int revertTo);

    [LibraryImport("libX11.so.6", EntryPoint = "XGrabButton")]
    public static partial int GrabButton(
        IntPtr display, nuint button, uint modifiers,
        IntPtr grab_window, uint event_mask,
        int pointer_mode, int keyboard_mode,
        IntPtr confine_to, IntPtr cursor);

    public const int GrabModeAsync = 1;

    [LibraryImport("libX11.so.6", EntryPoint = "XSelectInput")]
    public static partial int SelectInput(IntPtr display, IntPtr window, long eventMask);

    [LibraryImport("libc.so.6", EntryPoint = "malloc")]
    public static partial IntPtr Malloc(nuint size);

    [LibraryImport("libc.so.6", EntryPoint = "free")]
    public static partial void CFree(IntPtr ptr);

    public delegate int XErrorHandler(IntPtr display, ref XErrorEvent e);

    [StructLayout(LayoutKind.Sequential)]
    public struct XErrorEvent
    {
        public int type;
        public IntPtr display;
        public IntPtr resourceid;
        public IntPtr serial;
        public byte error_code;
        public byte request_code;
        public byte minor_code;
        public int resource_type;
    }

    [LibraryImport("libX11.so.6", EntryPoint = "XSetErrorHandler")]
    public static partial IntPtr SetErrorHandler(XErrorHandler handler);

    [LibraryImport("libX11.so.6", EntryPoint = "XGetErrorText", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int GetErrorText(IntPtr display, byte code, byte[] buffer, int length);

    [LibraryImport("libX11.so.6", EntryPoint = "XFlush")]
    public static partial int Flush(IntPtr display);

    [LibraryImport("libX11.so.6", EntryPoint = "XSync")]
    public static partial int Sync(IntPtr display, [MarshalAs(UnmanagedType.Bool)] bool discard);

    [LibraryImport("libX11.so.6", EntryPoint = "XPending")]
    public static partial int Pending(IntPtr display);

    [LibraryImport("libX11.so.6", EntryPoint = "XEventsQueued")]
    public static partial int EventsQueued(IntPtr display, int mode);

    [DllImport("libX11.so.6", EntryPoint = "XNextEvent")]
    public static extern int NextEvent(IntPtr display, out XEvent e);

    [DllImport("libX11.so.6", EntryPoint = "XCheckIfEvent")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CheckIfEvent(IntPtr display, out XEvent e, EventPredicate predicate, IntPtr arg);

    [DllImport("libX11.so.6", EntryPoint = "XPeekEvent")]
    public static extern int PeekEvent(IntPtr display, out XEvent e);

    [DllImport("libX11.so.6", EntryPoint = "XSendEvent")]
    public static extern int SendEvent(IntPtr display, IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool propagate, long eventMask, ref XEvent e);

    [DllImport("libX11.so.6", EntryPoint = "XPutBackEvent")]
    public static extern int PutBackEvent(IntPtr display, ref XEvent e);

    [DllImport("libX11.so.6", EntryPoint = "XFilterEvent")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FilterEvent(ref XEvent e, IntPtr window);

    [DllImport("libX11.so.6", EntryPoint = "XLookupString")]
    public static extern int LookupString(ref XKeyEvent e, byte[] buffer, int bytes, ref IntPtr keysym, IntPtr status);

    [DllImport("libX11.so.6", EntryPoint = "XSetLocaleModifiers", CharSet = CharSet.Ansi)]
    public static extern IntPtr SetLocaleModifiers(string modifierList);

    [DllImport("libX11.so.6", EntryPoint = "XOpenIM")]
    public static extern IntPtr OpenIM(IntPtr display, IntPtr rdb, IntPtr resName, IntPtr resClass);

    [DllImport("libX11.so.6", EntryPoint = "XCloseIM")]
    public static extern int CloseIM(IntPtr im);

    [DllImport("libX11.so.6", EntryPoint = "XCreateIC", CharSet = CharSet.Ansi)]
    public static extern IntPtr CreateIC(
        IntPtr im,
        string n1, long v1,
        string n2, IntPtr v2,
        string n3, IntPtr v3,
        IntPtr end);

    [DllImport("libX11.so.6", EntryPoint = "XDestroyIC")]
    public static extern void DestroyIC(IntPtr ic);

    [DllImport("libX11.so.6", EntryPoint = "XSetICFocus")]
    public static extern void SetICFocus(IntPtr ic);

    [DllImport("libX11.so.6", EntryPoint = "XUnsetICFocus")]
    public static extern void UnsetICFocus(IntPtr ic);

    [DllImport("libX11.so.6", EntryPoint = "Xutf8LookupString")]
    public static extern int Utf8LookupString(
        IntPtr ic,
        ref XKeyEvent keyEvent,
        byte[] bufferReturn,
        int bytesBuffer,
        out IntPtr keysymReturn,
        out int statusReturn);

    [DllImport("libc.so.6", EntryPoint = "setlocale", CharSet = CharSet.Ansi)]
    public static extern IntPtr SetLocale(int category, string? locale);

    public const int LcCtype = 0;

    [LibraryImport("libX11.so.6", EntryPoint = "XKeysymToKeycode")]
    public static partial nuint KeysymToKeycode(IntPtr display, IntPtr keysym);

    [LibraryImport("libX11.so.6", EntryPoint = "XKeycodeToKeysym")]
    public static partial IntPtr KeycodeToKeysym(IntPtr display, nuint keycode, int index);

    [LibraryImport("libX11.so.6", EntryPoint = "XGetModifierMapping")]
    public static partial IntPtr GetModifierMapping(IntPtr display);

    [LibraryImport("libX11.so.6", EntryPoint = "XFreeModifiermap")]
    public static partial int FreeModifiermap(IntPtr map);

    [LibraryImport("libX11.so.6", EntryPoint = "XDisplayWidth")]
    public static partial int DisplayWidth(IntPtr display, int screen);

    [LibraryImport("libX11.so.6", EntryPoint = "XDisplayHeight")]
    public static partial int DisplayHeight(IntPtr display, int screen);

    [LibraryImport("libX11.so.6", EntryPoint = "XWidthMMOfScreen")]
    public static partial int WidthMMOfScreen(IntPtr screen);

    [LibraryImport("libX11.so.6", EntryPoint = "XHeightMMOfScreen")]
    public static partial int HeightMMOfScreen(IntPtr screen);

    [LibraryImport("libX11.so.6", EntryPoint = "XScreenOfDisplay")]
    public static partial IntPtr ScreenOfDisplay(IntPtr display, int screen);

    [LibraryImport("libXrandr.so.2", EntryPoint = "XRRGetScreenInfo")]
    public static partial IntPtr XRRGetScreenInfo(IntPtr display, IntPtr drawable);

    [LibraryImport("libXrandr.so.2", EntryPoint = "XRRConfigCurrentRate")]
    public static partial short XRRConfigCurrentRate(IntPtr config);

    [LibraryImport("libXrandr.so.2", EntryPoint = "XRRFreeScreenConfigInfo")]
    public static partial void XRRFreeScreenConfigInfo(IntPtr config);

    [LibraryImport("libX11.so.6", EntryPoint = "XCreatePixmap")]
    public static partial IntPtr CreatePixmap(IntPtr display, IntPtr drawable, uint width, uint height, int depth);

    [LibraryImport("libX11.so.6", EntryPoint = "XFreePixmap")]
    public static partial int FreePixmap(IntPtr display, IntPtr pixmap);

    [LibraryImport("libX11.so.6", EntryPoint = "XCreateGC")]
    public static partial IntPtr CreateGC(IntPtr display, IntPtr drawable, nuint mask, IntPtr values);

    [LibraryImport("libX11.so.6", EntryPoint = "XFreeGC")]
    public static partial int FreeGC(IntPtr display, IntPtr gc);

    [LibraryImport("libX11.so.6", EntryPoint = "XSetForeground")]
    public static partial int SetForeground(IntPtr display, IntPtr gc, nuint color);

    [LibraryImport("libX11.so.6", EntryPoint = "XCreateImage", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr CreateImage(
        IntPtr display, IntPtr visual, int depth, int format,
        int offset, IntPtr data, uint width, uint height,
        int bitmapPad, int bytesPerLine);

    [LibraryImport("libX11.so.6", EntryPoint = "XDestroyImage")]
    public static partial int DestroyImage(IntPtr image);

    [LibraryImport("libX11.so.6", EntryPoint = "XPutImage")]
    public static partial int PutImage(
        IntPtr display, IntPtr drawable, IntPtr gc,
        IntPtr image, int srcX, int srcY, int destX, int destY,
        uint width, uint height);

    [LibraryImport("libX11.so.6", EntryPoint = "XInternAtom", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr InternAtom(IntPtr display, string name, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

    [LibraryImport("libX11.so.6", EntryPoint = "XGetAtomName", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr GetAtomName(IntPtr display, IntPtr atom);

    [LibraryImport("libX11.so.6", EntryPoint = "XFree")]
    public static partial int Free(IntPtr data);

    [LibraryImport("libX11.so.6", EntryPoint = "XGetSelectionOwner")]
    public static partial IntPtr GetSelectionOwner(IntPtr display, IntPtr selection);

    [LibraryImport("libX11.so.6", EntryPoint = "XSetSelectionOwner")]
    public static partial int SetSelectionOwner(IntPtr display, IntPtr selection, IntPtr owner, IntPtr time);

    [LibraryImport("libX11.so.6", EntryPoint = "XConvertSelection")]
    public static partial int ConvertSelection(IntPtr display, IntPtr selection, IntPtr target, IntPtr property, IntPtr requestor, IntPtr time);

    [LibraryImport("libX11.so.6", EntryPoint = "XGetWindowProperty")]
    public static partial int GetWindowProperty(
        IntPtr display, IntPtr window, IntPtr property,
        long offset, long length, [MarshalAs(UnmanagedType.Bool)] bool delete,
        IntPtr reqType, out IntPtr actualType, out int actualFormat,
        out nuint nItems, out nuint bytesAfter, out IntPtr prop);

    [LibraryImport("libX11.so.6", EntryPoint = "XChangeProperty")]
    public static partial int ChangeProperty(
        IntPtr display, IntPtr window, IntPtr property, IntPtr type,
        int format, int mode, byte[] data, int nElements);

    [LibraryImport("libX11.so.6", EntryPoint = "XDeleteProperty")]
    public static partial int DeleteProperty(IntPtr display, IntPtr window, IntPtr property);

    [LibraryImport("libX11.so.6", EntryPoint = "XStoreName", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int StoreName(IntPtr display, IntPtr window, string name);

    [LibraryImport("libX11.so.6", EntryPoint = "XSetWMProtocols")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWMProtocols(IntPtr display, IntPtr window, IntPtr[] protocols, int count);

    [LibraryImport("libX11.so.6", EntryPoint = "XSetTransientForHint")]
    public static partial int SetTransientForHint(IntPtr display, IntPtr window, IntPtr owner);

    [LibraryImport("libX11.so.6", EntryPoint = "XGrabPointer")]
    public static partial int GrabPointer(IntPtr display, IntPtr window,
        [MarshalAs(UnmanagedType.Bool)] bool ownerEvents, uint eventMask,
        int pointerMode, int keyboardMode, IntPtr confineTo, IntPtr cursor, IntPtr time);

    [LibraryImport("libX11.so.6", EntryPoint = "XUngrabPointer")]
    public static partial int UngrabPointer(IntPtr display, IntPtr time);

    [LibraryImport("libX11.so.6", EntryPoint = "XQueryPointer")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryPointer(
        IntPtr display, IntPtr window,
        out IntPtr rootReturn, out IntPtr childReturn,
        out int rootXReturn, out int rootYReturn,
        out int winXReturn, out int winYReturn, out uint maskReturn);

    [LibraryImport("libX11.so.6", EntryPoint = "XDefineCursor")]
    public static partial int DefineCursor(IntPtr display, IntPtr window, IntPtr cursor);

    [LibraryImport("libX11.so.6", EntryPoint = "XUndefineCursor")]
    public static partial int UndefineCursor(IntPtr display, IntPtr window);

    [LibraryImport("libX11.so.6", EntryPoint = "XCreateFontCursor")]
    public static partial IntPtr CreateFontCursor(IntPtr display, uint shape);

    public const uint XC_Xterm = 152;
    public const uint XC_left_ptr = 68;
    public const uint XC_hand2 = 60;

    [LibraryImport("libX11.so.6", EntryPoint = "XFreeCursor")]
    public static partial int FreeCursor(IntPtr display, IntPtr cursor);

    [LibraryImport("libX11.so.6", EntryPoint = "XQueryTree")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryTree(IntPtr display, IntPtr window,
        out IntPtr root, out IntPtr parent, out IntPtr children, out int nChildren);

    [LibraryImport("libX11.so.6", EntryPoint = "XGetGeometry")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetGeometry(IntPtr display, IntPtr drawable,
        out IntPtr root, out int x, out int y, out uint width, out uint height,
        out uint borderWidth, out uint depth);

    [LibraryImport("libX11.so.6", EntryPoint = "XGetImage")]
    public static partial IntPtr GetImage(
        IntPtr display, IntPtr drawable, int x, int y, uint width, uint height,
        uint planeMask, int format);

    [LibraryImport("libX11.so.6", EntryPoint = "XGetPixel")]
    public static partial nuint GetPixel(IntPtr image, int x, int y);

    [LibraryImport("libX11.so.6", EntryPoint = "XResizeWindow")]
    public static partial int ResizeWindow(IntPtr display, IntPtr window, uint width, uint height);

    [LibraryImport("libX11.so.6", EntryPoint = "XMoveResizeWindow")]
    public static partial int MoveResizeWindow(IntPtr display, IntPtr window, int x, int y, uint width, uint height);

    [LibraryImport("libX11.so.6", EntryPoint = "XCreateColormap")]
    public static partial IntPtr CreateColormap(IntPtr display, IntPtr window, IntPtr visual, int alloc);

    [LibraryImport("libX11.so.6", EntryPoint = "XFreeColormap")]
    public static partial int FreeColormap(IntPtr display, IntPtr colormap);

    public const int AllocNone = 0;

    [LibraryImport("libX11.so.6", EntryPoint = "XVisualIDFromVisual")]
    public static partial IntPtr VisualIDFromVisual(IntPtr visual);

    [LibraryImport("libX11.so.6", EntryPoint = "XGetVisualInfo")]
    public static partial IntPtr GetVisualInfo(IntPtr display, nuint mask, ref XVisualInfo template, out int nItems);

    [LibraryImport("libX11.so.6", EntryPoint = "XMatchVisualInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MatchVisualInfo(IntPtr display, int screen, int depth, int visualClass, out XVisualInfo info);

    public const int TrueColor = 4;
    public const int VisualIDMask = 0x1;
    public const int VisualScreenMask = 0x2;
    public const int VisualDepthMask = 0x4;
    public const int VisualClassMask = 0x8;
    public const int VisualRedMaskMask = 0x10;
    public const int VisualGreenMaskMask = 0x20;
    public const int VisualBlueMaskMask = 0x40;
    public const int VisualColormapSizeMask = 0x80;
    public const int VisualBitsPerRGBMask = 0x100;
    public const int VisualAllMask = 0x1FF;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate bool EventPredicate(IntPtr display, ref XEvent e, IntPtr arg);

    [StructLayout(LayoutKind.Sequential)]
    public struct XSetWindowAttributes
    {
        public IntPtr backgroundPixmap;
        public nuint backgroundPixel;
        public IntPtr borderPixmap;
        public nuint borderPixel;
        public int bitGravity;
        public int winGravity;
        public int backingStore;
        public nuint backingPlanes;
        public nuint backingPixel;
        public int saveUnder;
        public long eventMask;
        public long doNotPropagateMask;
        public int overrideRedirect;
        public IntPtr colormap;
        public IntPtr cursor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XVisualInfo
    {
        public IntPtr visual;
        public IntPtr visualid;
        public int screen;
        public uint depth;
        public int @class;
        public nuint redMask;
        public nuint greenMask;
        public nuint blueMask;
        public int colormapSize;
        public int bitsPerRGB;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XAnyEvent
    {
        public int type;
        public IntPtr serial;
        public int sendEvent;
        public IntPtr display;
        public IntPtr window;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XKeyEvent
    {
        public int type;
        public IntPtr serial;
        public int sendEvent;
        public IntPtr display;
        public IntPtr window;
        public IntPtr subwindow;
        public IntPtr time;
        public int x;
        public int y;
        public int xRoot;
        public int yRoot;
        public uint state;
        public uint keycode;
        public int sameScreen;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XButtonEvent
    {
        public int type;
        public IntPtr serial;
        public int sendEvent;
        public IntPtr display;
        public IntPtr window;
        public IntPtr subwindow;
        public IntPtr time;
        public int x;
        public int y;
        public int xRoot;
        public int yRoot;
        public uint state;
        public uint button;
        public int sameScreen;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XMotionEvent
    {
        public int type;
        public IntPtr serial;
        public int sendEvent;
        public IntPtr display;
        public IntPtr window;
        public IntPtr subwindow;
        public IntPtr time;
        public int x;
        public int y;
        public int xRoot;
        public int yRoot;
        public uint state;
        public byte isHint;
        public int sameScreen;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XCrossingEvent
    {
        public int type;
        public IntPtr serial;
        public int sendEvent;
        public IntPtr display;
        public IntPtr window;
        public IntPtr subwindow;
        public IntPtr time;
        public int x;
        public int y;
        public int xRoot;
        public int yRoot;
        public int mode;
        public int detail;
        public int sameScreen;
        public int focus;
        public uint state;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XConfigureEvent
    {
        public int type;
        public IntPtr serial;
        public int sendEvent;
        public IntPtr display;
        public IntPtr eventWindow;
        public IntPtr window;
        public int x;
        public int y;
        public int width;
        public int height;
        public int borderWidth;
        public IntPtr above;
        public int overrideRedirect;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XExposeEvent
    {
        public int type;
        public IntPtr serial;
        public int sendEvent;
        public IntPtr display;
        public IntPtr window;
        public int x;
        public int y;
        public int width;
        public int height;
        public int count;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XPropertyEvent
    {
        public int type;
        public IntPtr serial;
        public int sendEvent;
        public IntPtr display;
        public IntPtr window;
        public IntPtr atom;
        public IntPtr time;
        public int state;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XClientMessageEvent
    {
        public int type;
        public IntPtr serial;
        public int sendEvent;
        public IntPtr display;
        public IntPtr window;
        public IntPtr messageType;
        public int format;
        public ClientMessageData data;
    }

    [StructLayout(LayoutKind.Sequential, Size = 40)]
    public unsafe struct ClientMessageData
    {
        public fixed long l[5];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XSelectionRequestEvent
    {
        public int type;
        public IntPtr serial;
        public int sendEvent;
        public IntPtr display;
        public IntPtr owner;
        public IntPtr requestor;
        public IntPtr selection;
        public IntPtr target;
        public IntPtr property;
        public IntPtr time;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XSelectionEvent
    {
        public int type;
        public IntPtr serial;
        public int sendEvent;
        public IntPtr display;
        public IntPtr requestor;
        public IntPtr selection;
        public IntPtr target;
        public IntPtr property;
        public IntPtr time;
    }

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    public struct XEvent
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(0)] public XAnyEvent any;
        [FieldOffset(0)] public XKeyEvent key;
        [FieldOffset(0)] public XButtonEvent button;
        [FieldOffset(0)] public XMotionEvent motion;
        [FieldOffset(0)] public XCrossingEvent crossing;
        [FieldOffset(0)] public XConfigureEvent configure;
        [FieldOffset(0)] public XExposeEvent expose;
        [FieldOffset(0)] public XPropertyEvent property;
        [FieldOffset(0)] public XClientMessageEvent clientMessage;
        [FieldOffset(0)] public XSelectionRequestEvent selectionRequest;
        [FieldOffset(0)] public XSelectionEvent selection;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XModifierKeymap
    {
        public int maxKeyperMod;
        public IntPtr modifiermap;
    }
}
