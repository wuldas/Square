using System.Runtime.InteropServices;
using AndroidFormat = global::Android.Graphics.Format;
using Android.Content;
using Android.Text;
using Android.Views.InputMethods;
using AndroidKeyEvent = global::Android.Views.KeyEvent;
using Android.Views;
using AndroidView = global::Android.Views.SurfaceView;

namespace Square.Platform.Android;

/// <summary>为 Android Vulkan 提供可交换的 SurfaceView；Square 输入仍由覆盖的 SquareView 接收。</summary>
public sealed class AndroidVulkanSurfaceView : AndroidView, ISurfaceHolderCallback
{
    private readonly AndroidPlatformHost _host;
    private global::Android.Views.Surface? _surface;
    private bool _disposed;

    /// <summary>创建 Vulkan SurfaceView。</summary>
    public AndroidVulkanSurfaceView(Context context, AndroidPlatformHost host)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        SetZOrderOnTop(false);
        Holder!.AddCallback(this);
        Focusable = true;
        FocusableInTouchMode = true;
        Clickable = true;
        _host.AttachInputView(this);
    }

    /// <summary>Surface 首次可用或重新创建后触发。</summary>
    public event Action? SurfaceReady;
    /// <summary>释放旧 Surface 之前触发；宿主应暂停会话并释放关联的渲染上下文。</summary>
    public event Action? SurfaceUnavailable;
    internal bool HasSurface => _surface?.IsValid == true;

    /// <inheritdoc />
    public void SurfaceCreated(ISurfaceHolder holder)
    {
        if (_disposed) return;
        SetSurface(holder.Surface!);
    }

    /// <inheritdoc />
    public void SurfaceChanged(ISurfaceHolder holder, AndroidFormat format, int width, int height)
    {
        if (_disposed || holder.Surface is not { IsValid: true } surface) return;
        if (_surface?.Handle == surface.Handle) return;
        SetSurface(surface);
    }

    /// <inheritdoc />
    public void SurfaceDestroyed(ISurfaceHolder holder)
    {
        if (!_disposed) ReleaseCurrentSurface();
    }
    private void SetSurface(global::Android.Views.Surface surface)
    {
        if (_surface != null) SurfaceUnavailable?.Invoke();
        _host.SetNativeSurface(surface);
        _surface = surface;
        SurfaceReady?.Invoke();
    }

    private void ReleaseCurrentSurface()
    {
        if (_surface != null) SurfaceUnavailable?.Invoke();
        _surface = null;
        _host.ClearNativeSurface();
    }

    public override bool OnTouchEvent(MotionEvent? e) => e != null && _host.HandleTouchEvent(e);

    public override bool OnGenericMotionEvent(MotionEvent? e) =>
        e != null && _host.HandleGenericMotionEvent(e) || base.OnGenericMotionEvent(e);

    public override bool OnKeyDown(Keycode keyCode, AndroidKeyEvent? e) =>
        _host.HandleKeyEvent(keyCode, e, Square.Platform.KeyAction.Down) || base.OnKeyDown(keyCode, e);

    public override bool OnKeyUp(Keycode keyCode, AndroidKeyEvent? e) =>
        _host.HandleKeyEvent(keyCode, e, Square.Platform.KeyAction.Up) || base.OnKeyUp(keyCode, e);

    public override bool OnCheckIsTextEditor() => true;

    public override IInputConnection? OnCreateInputConnection(EditorInfo? outAttrs)
    {
        if (outAttrs != null)
        {
            outAttrs.InputType = InputTypes.ClassText;
            if (_host.TextInputClientQuery?.Invoke()?.IsMultiline == true)
                outAttrs.InputType |= InputTypes.TextFlagMultiLine;
            outAttrs.ImeOptions = ImeFlags.NoExtractUi;
        }
        return new AndroidInputConnection(this, _host);
    }

    internal void ReleaseSurface()
    {
        if (_disposed) return;
        _disposed = true;
        Holder?.RemoveCallback(this);
        ReleaseCurrentSurface();
        SurfaceReady = null;
        SurfaceUnavailable = null;
    }
}

internal static class AndroidNativeWindow
{
    [DllImport("libandroid.so", EntryPoint = "ANativeWindow_fromSurface")]
    private static extern IntPtr FromSurfaceNative(IntPtr environment, IntPtr surface);

    [DllImport("libandroid.so", EntryPoint = "ANativeWindow_release")]
    private static extern void ReleaseNative(IntPtr window);

    public static IntPtr FromSurface(global::Android.Views.Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return FromSurfaceNative(global::Android.Runtime.JNIEnv.Handle, surface.Handle);
    }

    public static void Release(IntPtr window)
    {
        if (window != IntPtr.Zero) ReleaseNative(window);
    }
}
