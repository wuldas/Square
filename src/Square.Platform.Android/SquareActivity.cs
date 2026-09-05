using Android.Content;
using Android.Content.Res;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using Android.Views.InputMethods;
using Square.Hosting;
using Square.Platform;
using Square.UI.Scrolling;
using AndroidActivity = Android.App.Activity;

namespace Square.Platform.Android;

/// <summary>Square Android 应用便利基类；应用仍可直接组合宿主和会话。</summary>
public abstract class SquareActivity : AndroidActivity
{
    private AndroidPlatformHost? _host;
    private SquareView? _view;
    private AndroidVulkanSurfaceView? _vulkanSurface;
    private ApplicationSession? _session;
    private AndroidFrameScheduler? _scheduler;
    private AppWindow? _window;
    private bool _destroyed;
    private bool _resumed;

    /// <summary>创建应用窗口和真实 Square 内容。</summary>
    protected abstract AppWindow CreateSquareWindow();
    /// <summary>应用路由消费 Back 时覆写；返回 true 表示已处理。</summary>
    protected virtual bool OnSquareBackRequested() => false;

    /// <summary>创建 Android Activity 内容。</summary>
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ActionBar?.Hide();
        ConfigureSystemBars();
        Window?.SetSoftInputMode(SoftInput.AdjustResize | SoftInput.StateAlwaysHidden);
        AndroidFontPolicy.LogDiagnostics();
        AndroidPlatformRegistration.Register(this);

        var window = CreateSquareWindow();
        ArgumentNullException.ThrowIfNull(window);
        window.ScrollbarProfile = ScrollbarDeviceProfile.Mobile;
        _window = window;

        var hostInfo = new PlatformHostCreateInfo
        {
            Title = window.Title,
            Width = Math.Max(1, (int)MathF.Ceiling(window.ClientSize.Width)),
            Height = Math.Max(1, (int)MathF.Ceiling(window.ClientSize.Height)),
            RenderBackend = window.RenderBackend,
            SoftwareSurface = window.SoftwareSurface,
            TitleStyle = window.TitleStyle,
            BorderStyle = window.BorderStyle
        };
        var host = new AndroidPlatformHost(this, hostInfo);
        host.AccessibilityRootQuery = () => window.Content;
        var view = new SquareView(this, host);
        host.AttachView(view);
        _host = host;
        _view = view;
        var requiresVulkanSurface = string.Equals(window.RenderBackend, "Vulkan", StringComparison.OrdinalIgnoreCase);
        if (requiresVulkanSurface)
        {
            var surfaceView = new AndroidVulkanSurfaceView(this, host);
            surfaceView.SurfaceReady += AttachSession;
            surfaceView.SurfaceUnavailable += SuspendForSurfaceLoss;
            _vulkanSurface = surfaceView;
            var root = new FrameLayout(this);
            var layoutParams = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
            root.AddView(surfaceView, layoutParams);
            root.AddView(view, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            SetContentView(root);
        }
        else
        {
            SetContentView(view);
        }
        view.Post(ApplyRootWindowInsets);

        var session = new ApplicationSession(window, host);
        _session = session;
        host.TextEditorFocusQuery = () => session.HasTextEditorFocus;
        host.TextInputClientQuery = () => session.FocusedTextInputClient;
        session.BackRequested = OnSquareBackRequested;
        session.FramePresented += host.InvalidateView;
        var scheduler = new AndroidFrameScheduler(ProcessFrame);
        _scheduler = scheduler;
        host.RenderRequested += RequestFrame;
        window.Dispatcher.WorkAvailable += RequestFrame;
        if (!requiresVulkanSurface) AttachSession();
        else if (_vulkanSurface?.HasSurface == true) AttachSession();
    }

    private void AttachSession()
    {
        if (_destroyed || _session == null || _scheduler == null || _host == null || _window == null)
            return;
        try
        {
            if (!_session.IsAttached) _session.Attach();
            UpdateSessionState();
        }
        catch
        {
            _window.Dispatcher.WorkAvailable -= RequestFrame;
            _host.RenderRequested -= RequestFrame;
            _scheduler.Dispose();
            _scheduler = null;
            _session.Dispose();
            _session = null;
            _host = null;
            _view = null;
            _vulkanSurface = null;
            _window = null;
            throw;
        }
    }

    private void SuspendForSurfaceLoss()
    {
        _scheduler?.Pause();
        _session?.Suspend();
        _session?.ReleaseRenderContext();
    }

    private void UpdateSessionState()
    {
        if (_session == null || _scheduler == null) return;
        if (!_resumed || (_vulkanSurface != null && !_vulkanSurface.HasSurface))
        {
            _session.Suspend();
            _scheduler.Pause();
            return;
        }
        _session.Resume();
        _scheduler.Resume();
        if (_session.HasPendingFrame) RequestFrame();
    }

    /// <inheritdoc />
    protected override void OnPause()
    {
        _resumed = false;
        _host?.CancelInput();
        _session?.Suspend();
        _scheduler?.Pause();
        HideKeyboard();
        _host?.LogPerformanceDiagnostics(_scheduler);
        base.OnPause();
    }

    /// <inheritdoc />
    protected override void OnResume()
    {
        base.OnResume();
        _resumed = true;
        UpdateSessionState();
    }
    /// <inheritdoc />
    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        if (_view != null) _host?.UpdateSurfaceSize(_view.Width, _view.Height);
        RequestFrame();
    }
    /// <inheritdoc />
    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus) _view?.Post(ApplyRootWindowInsets);
    }

    /// <inheritdoc />
    public override void OnBackPressed()
    {
        if (_session?.HandleBack() == true) return;
#pragma warning disable CA1422
        base.OnBackPressed();
#pragma warning restore CA1422
    }

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        _destroyed = true;
        if (_window != null) _window.Dispatcher.WorkAvailable -= RequestFrame;
        if (_host != null) _host.RenderRequested -= RequestFrame;
        var vulkanSurface = _vulkanSurface;
        if (vulkanSurface != null)
        {
            vulkanSurface.SurfaceReady -= AttachSession;
            vulkanSurface.SurfaceUnavailable -= SuspendForSurfaceLoss;
        }
        _host?.LogPerformanceDiagnostics(_scheduler);
        _scheduler?.Dispose();
        _scheduler = null;
        _session?.Dispose();
        _session = null;
        vulkanSurface?.ReleaseSurface();
        _vulkanSurface = null;
        _host = null;
        _view = null;
        _window = null;
        base.OnDestroy();
    }

    private void ApplyRootWindowInsets()
    {
        if (_view == null || _host == null) return;
        var insets = _view.RootWindowInsets;
        if (insets == null) return;
        var resources = Resources;
        if (resources == null) return;

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
#pragma warning disable CA1416, CA1422
            var systemBars = insets.GetInsets(WindowInsets.Type.SystemBars());
#pragma warning restore CA1416, CA1422
            if (systemBars.Left != 0 || systemBars.Top != 0 || systemBars.Right != 0 || systemBars.Bottom != 0)
            {
                _host.UpdateInsets(systemBars.Left, systemBars.Top, systemBars.Right, systemBars.Bottom);
                return;
            }
        }

#pragma warning disable CA1422
        var top = resources.GetIdentifier("status_bar_height", "dimen", "android") is var topId && topId > 0
            ? resources.GetDimensionPixelSize(topId)
            : 0;
        var bottom = resources.GetIdentifier("navigation_bar_height", "dimen", "android") is var bottomId && bottomId > 0
            ? resources.GetDimensionPixelSize(bottomId)
            : 0;
#pragma warning restore CA1422
        if (_view.Height >= _view.Width)
            _host.UpdateInsets(0, top, 0, bottom);
        else
            _host.UpdateInsets(0, top, bottom, 0);
    }

    private bool ProcessFrame()
    {
        if (_destroyed || _session == null || _host == null) return false;
        _host.StepFling();
        _session.Tick();
        if (_session.HasPendingFrame)
            _session.ProcessFrame();
        return !_destroyed && (_host.HasFling || _session.HasPendingFrame);
    }

    private void RequestFrame()
    {
        if (_destroyed || _scheduler == null) return;
        if (Looper.MyLooper() == Looper.MainLooper)
            _scheduler.RequestFrame();
        else
            RunOnUiThread(_scheduler.RequestFrame);
    }

    private void ConfigureSystemBars()
    {
        var decorView = Window?.DecorView;
        if (decorView == null) return;

#pragma warning disable CA1416, CA1422
        if (OperatingSystem.IsAndroidVersionAtLeast(30) && !OperatingSystem.IsAndroidVersionAtLeast(35))
            Window?.SetDecorFitsSystemWindows(true);
#pragma warning restore CA1416, CA1422

#pragma warning disable CA1422
        var flags = decorView.SystemUiFlags;
        if (OperatingSystem.IsAndroidVersionAtLeast(23)) flags |= SystemUiFlags.LightStatusBar;
        if (OperatingSystem.IsAndroidVersionAtLeast(26)) flags |= SystemUiFlags.LightNavigationBar;
        decorView.SystemUiFlags = flags;
#pragma warning restore CA1422
    }

    private void HideKeyboard()
    {
        if (_view == null) return;
        var manager = GetSystemService(InputMethodService) as InputMethodManager;
        manager?.HideSoftInputFromWindow(_view.WindowToken, HideSoftInputFlags.None);
    }
}
