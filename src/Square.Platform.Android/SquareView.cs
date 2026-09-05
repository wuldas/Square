using Android.Content;
using Android.Graphics;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using SkiaSharp;
using SkiaSharp.Views.Android;
using Android.Views.Accessibility;
using AndroidKeyEvent = Android.Views.KeyEvent;
using AndroidView = global::Android.Views.View;

namespace Square.Platform.Android;

/// <summary>承载 Square 软件帧并转发 Android 输入的单根 View。</summary>
public sealed class SquareView : SKCanvasView
{
    private readonly AndroidPlatformHost _host;
    private readonly AndroidAccessibilityNodeProvider _accessibilityProvider;
    private bool _insetsInitialized;
    /// <summary>创建 Square View。</summary>
    public SquareView(Context context, AndroidPlatformHost host)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        Focusable = true;
        FocusableInTouchMode = true;
        Clickable = true;
        SetWillNotDraw(false);
        ImportantForAccessibility = ImportantForAccessibility.Yes;
        _accessibilityProvider = new AndroidAccessibilityNodeProvider(this, host);
    }
    public override AccessibilityNodeProvider? AccessibilityNodeProvider => _accessibilityProvider;

    internal void NotifyAccessibilityChanged()
    {
        _accessibilityProvider.Refresh();
        var manager = Context?.GetSystemService(Context.AccessibilityService) as AccessibilityManager;
        if (manager?.IsEnabled == true)
            SendAccessibilityEvent(EventTypes.WindowContentChanged);
    }

    /// <inheritdoc />
    protected override void OnDraw(Canvas canvas)
    {
        if (_host.IsUsingAndroidSkia)
        {
            base.OnDraw(canvas);
            return;
        }
        if (!_insetsInitialized && RootWindowInsets is { } rootInsets)
            ApplySystemInsets(rootInsets);
        _host.Draw(canvas);
    }

    /// <inheritdoc />
    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        if (_host.IsUsingAndroidSkia)
            _host.DrawSkia(e.Surface.Canvas);
    }
    /// <inheritdoc />
    public override WindowInsets? OnApplyWindowInsets(WindowInsets? insets)
    {
        if (insets != null) ApplySystemInsets(insets);
        return base.OnApplyWindowInsets(insets);
    }

    private void ApplySystemInsets(WindowInsets insets)
    {
        var left = 0;
        var top = 0;
        var right = 0;
        var bottom = 0;
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
#pragma warning disable CA1416, CA1422
            var systemBars = insets.GetInsets(WindowInsets.Type.SystemBars());
#pragma warning restore CA1416, CA1422
            left = systemBars.Left;
            top = systemBars.Top;
            right = systemBars.Right;
            bottom = systemBars.Bottom;
        }
        else
        {
#pragma warning disable CA1422
            left = insets.SystemWindowInsetLeft;
            top = insets.SystemWindowInsetTop;
            right = insets.SystemWindowInsetRight;
            bottom = insets.SystemWindowInsetBottom;
#pragma warning restore CA1422
        }

        if (left == 0 && top == 0 && right == 0 && bottom == 0)
        {
            var resources = Resources;
            if (resources != null)
            {
#pragma warning disable CA1422
                var statusId = resources.GetIdentifier("status_bar_height", "dimen", "android");
                var navigationId = resources.GetIdentifier("navigation_bar_height", "dimen", "android");
                top = statusId > 0 ? resources.GetDimensionPixelSize(statusId) : 0;
                bottom = navigationId > 0 ? resources.GetDimensionPixelSize(navigationId) : 0;
#pragma warning restore CA1422
                if (Height < Width) (right, bottom) = (bottom, 0);
            }
        }

        if (left == 0 && top == 0 && right == 0 && bottom == 0) return;
        _host.UpdateInsets(left, top, right, bottom);
        _insetsInitialized = true;
    }

    /// <inheritdoc />
    protected override void OnSizeChanged(int width, int height, int oldWidth, int oldHeight)
    {
        base.OnSizeChanged(width, height, oldWidth, oldHeight);
        _host.UpdateSurfaceSize(width, height);
    }

    /// <inheritdoc />
    public override bool OnTouchEvent(MotionEvent? e) => e != null && _host.HandleTouchEvent(e);

    /// <inheritdoc />
    public override bool OnGenericMotionEvent(MotionEvent? e) =>
        e != null && _host.HandleGenericMotionEvent(e) || base.OnGenericMotionEvent(e);

    /// <inheritdoc />
    public override bool OnKeyDown(Keycode keyCode, AndroidKeyEvent? e) =>
        _host.HandleKeyEvent(keyCode, e, Square.Platform.KeyAction.Down) || base.OnKeyDown(keyCode, e);

    /// <inheritdoc />
    public override bool OnKeyUp(Keycode keyCode, AndroidKeyEvent? e) =>
        _host.HandleKeyEvent(keyCode, e, Square.Platform.KeyAction.Up) || base.OnKeyUp(keyCode, e);

    /// <inheritdoc />
    public override bool OnCheckIsTextEditor() => true;

    /// <inheritdoc />
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

    /// <summary>请求 Android 将输入法候选区滚动到指定位置。</summary>
    internal void RequestInputRectangle(global::Android.Graphics.Rect rectangle) =>
        RequestRectangleOnScreen(rectangle, false);
}
