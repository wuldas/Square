using System.Runtime.InteropServices;

namespace Square.Platform.MacOS;

internal static partial class MacOSApi
{
    private const string ObjectiveC = "/usr/lib/libobjc.A.dylib";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";

    internal const nuint WindowStyleTitled = 1;
    internal const nuint WindowStyleClosable = 1 << 1;
    internal const nuint WindowStyleMiniaturizable = 1 << 2;
    internal const nuint WindowStyleResizable = 1 << 3;
    internal const nuint WindowStyleFullScreen = 1 << 14;
    internal const nuint BackingStoreBuffered = 2;
    internal const uint BitmapByteOrder32Little = 0x2000;
    internal const uint ImageAlphaPremultipliedFirst = 2;

    private static readonly IntPtr AppKitHandle = NativeLibrary.Load(AppKit);

    internal static IntPtr PasteboardTypeString =>
        Marshal.ReadIntPtr(NativeLibrary.GetExport(AppKitHandle, "NSPasteboardTypeString"));

    internal static IntPtr GetClass(string name)
    {
        _ = AppKitHandle;
        return ObjectiveCGetClass(name);
    }

    internal static IntPtr Selector(string name) => RegisterSelector(name);

    internal static IntPtr CreateString(string value)
    {
        var instance = SendIntPtr(GetClass("NSString"), Selector("alloc"));
        return SendUtf8(instance, Selector("initWithUTF8String:"), value);
    }

    [LibraryImport(ObjectiveC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr ObjectiveCGetClass(string name);

    [LibraryImport(ObjectiveC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr RegisterSelector(string name);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendIntPtr(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendPointer(IntPtr receiver, IntPtr selector, IntPtr value);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendByte(IntPtr receiver, IntPtr selector, byte value);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendNuint(IntPtr receiver, IntPtr selector, nuint value);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendDouble(IntPtr receiver, IntPtr selector, double value);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendRect(IntPtr receiver, IntPtr selector, NSRect value);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendSize(IntPtr receiver, IntPtr selector, NSSize value);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial byte SendPointerPointerByteResult(
        IntPtr receiver,
        IntPtr selector,
        IntPtr first,
        IntPtr second);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr SendUtf8(IntPtr receiver, IntPtr selector, string value);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendRectNuintNuintByte(
        IntPtr receiver,
        IntPtr selector,
        NSRect rect,
        nuint styleMask,
        nuint backing,
        byte defer);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendEventQuery(
        IntPtr receiver,
        IntPtr selector,
        nuint mask,
        IntPtr expiration,
        IntPtr mode,
        byte dequeue);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial double SendDoubleResult(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial byte SendByteResult(IntPtr receiver, IntPtr selector);
    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial byte RespondsToSelector(IntPtr receiver, IntPtr selector, IntPtr queriedSelector);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial nuint SendNuintResult(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial ushort SendUshortResult(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial NSPoint SendPointResult(IntPtr receiver, IntPtr selector);

    internal static NSRect SendRectResult(IntPtr receiver, IntPtr selector)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            SendRectResultX64(out var result, receiver, selector);
            return result;
        }

        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            return SendRectResultArm64(receiver, selector);

        throw new PlatformNotSupportedException("Square supports macOS on x64 and arm64.");
    }

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static partial NSRect SendRectResultArm64(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend_stret")]
    private static partial void SendRectResultX64(out NSRect result, IntPtr receiver, IntPtr selector);

    [LibraryImport(CoreFoundation, EntryPoint = "CFDataCreate")]
    internal static unsafe partial IntPtr DataCreate(IntPtr allocator, byte* bytes, nint length);

    [LibraryImport(CoreFoundation, EntryPoint = "CFRelease")]
    internal static partial void ReleaseCoreFoundation(IntPtr value);

    [LibraryImport(CoreGraphics, EntryPoint = "CGDataProviderCreateWithCFData")]
    internal static partial IntPtr DataProviderCreate(IntPtr data);

    [LibraryImport(CoreGraphics, EntryPoint = "CGColorSpaceCreateDeviceRGB")]
    internal static partial IntPtr ColorSpaceCreateDeviceRgb();

    [LibraryImport(CoreGraphics, EntryPoint = "CGImageCreate")]
    internal static partial IntPtr ImageCreate(
        nuint width,
        nuint height,
        nuint bitsPerComponent,
        nuint bitsPerPixel,
        nuint bytesPerRow,
        IntPtr colorSpace,
        uint bitmapInfo,
        IntPtr provider,
        IntPtr decode,
        byte shouldInterpolate,
        int renderingIntent);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct NSPoint(double X, double Y);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct NSSize(double Width, double Height);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct NSRect(NSPoint Origin, NSSize Size);
}
