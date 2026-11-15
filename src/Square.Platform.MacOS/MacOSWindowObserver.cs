using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Square.Platform.MacOS;

internal sealed class MacOSWindowObserver : IDisposable
{
    private static readonly object ObserverLock = new();
    private static readonly Dictionary<IntPtr, Action> Observers = new();
    private static readonly IntPtr ObserverClass = CreateObserverClass();
    private readonly IntPtr _notificationCenter;
    private IntPtr _observer;

    internal MacOSWindowObserver(IntPtr window, Action wake)
    {
        _notificationCenter = MacOSApi.SendIntPtr(MacOSApi.GetClass("NSNotificationCenter"), MacOSApi.Selector("defaultCenter"));
        _observer = MacOSApi.SendIntPtr(ObserverClass, MacOSApi.Selector("new"));
        if (_observer == IntPtr.Zero) throw new InvalidOperationException("Unable to create the macOS window observer.");
        lock (ObserverLock) Observers.Add(_observer, wake);
        try
        {
            // These can be delivered by the run loop without nextEvent returning
            // an NSEvent (for example, restoring a minimized window from the Dock).
            string[] names =
            [
                "NSWindowWillCloseNotification",
                "NSWindowDidMiniaturizeNotification",
                "NSWindowDidDeminiaturizeNotification",
                "NSWindowDidResizeNotification",
                "NSWindowDidEndLiveResizeNotification",
                "NSWindowDidChangeBackingPropertiesNotification"
            ];
            foreach (var name in names)
                MacOSApi.AddNotificationObserver(
                    _notificationCenter, MacOSApi.Selector("addObserver:selector:name:object:"),
                    _observer, MacOSApi.Selector("squareWindowChanged:"), MacOSApi.AppKitConstant(name), window);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static unsafe IntPtr CreateObserverClass()
    {
        var nativeClass = MacOSApi.AllocateClassPair(MacOSApi.GetClass("NSObject"), "SquareMacOSWindowObserver", 0);
        if (nativeClass == IntPtr.Zero) throw new InvalidOperationException("Unable to allocate the macOS window observer class.");
        var callback = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&WindowChanged;
        if (MacOSApi.AddMethod(nativeClass, MacOSApi.Selector("squareWindowChanged:"), callback, "v@:@") == 0)
            throw new InvalidOperationException("Unable to register the macOS window observer callback.");
        MacOSApi.RegisterClassPair(nativeClass);
        return nativeClass;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void WindowChanged(IntPtr observer, IntPtr selector, IntPtr notification)
    {
        Action? wake;
        lock (ObserverLock) Observers.TryGetValue(observer, out wake);
        wake?.Invoke();
    }

    public void Dispose()
    {
        if (_observer == IntPtr.Zero) return;
        MacOSApi.SendPointer(_notificationCenter, MacOSApi.Selector("removeObserver:"), _observer);
        lock (ObserverLock) Observers.Remove(_observer);
        MacOSApi.SendIntPtr(_observer, MacOSApi.Selector("release"));
        _observer = IntPtr.Zero;
    }
}
