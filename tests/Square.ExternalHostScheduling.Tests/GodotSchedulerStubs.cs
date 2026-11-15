using System.Collections.Concurrent;
using System.Reflection;

#pragma warning disable CS0067 // Test-only native boundary signals.

namespace Godot
{
    public class InputEvent { }
    public class InputEventMouseButton : InputEvent
    {
        public object ButtonIndex => "Left";
        public bool Pressed { get; set; }
    }
    public readonly record struct Vector2(float X, float Y)
    {
        public float Length() => MathF.Sqrt(X * X + Y * Y);
    }
    public readonly record struct Transform2D(Vector2 X, Vector2 Y);
    public class Viewport
    {
        public event Action? SizeChanged;
        public void Resize() => SizeChanged?.Invoke();
    }
    public class Window : Viewport
    {
        public enum ModeEnum { Windowed, Minimized }
        public bool Visible { get; set; } = true;
        public ModeEnum Mode { get; set; }
        public event Action? FocusEntered;
        public event Action? FocusExited;
        public event Action? DpiChanged;
        public event Action? VisibilityChanged;
        public void ChangeVisibility(bool visible) { Visible = visible; VisibilityChanged?.Invoke(); }
        public void EnterFocus() => FocusEntered?.Invoke();
        public void ExitFocus() => FocusExited?.Invoke();
    }
    public class Control
    {
        public enum FocusModeEnum { All }
        public enum MouseFilterEnum { Stop }
        public const long NotificationPaused = 14, NotificationUnpaused = 15;
        public const long NotificationEnabled = 29, NotificationDisabled = 28, NotificationTransformChanged = 2000;
        private readonly ConcurrentQueue<string> _deferred = new();
        private readonly Viewport _viewport = new();
        private readonly Window _window = new();
        public FocusModeEnum FocusMode { get; set; }
        public MouseFilterEnum MouseFilter { get; set; }
        public bool ClipContents { get; set; }
        public Vector2 Size { get; set; } = new(640, 480);
        public bool Visible { get; set; } = true;
        public bool Processable { get; set; } = true;
        public bool Focused { get; set; }
        public int SceneQueries { get; private set; }
        public int DeferredCount => _deferred.Count;
        public event Action? Resized;
        public event Action? VisibilityChanged;
        public event Action? MouseEntered;
        public event Action? MouseExited;
        public event Action? FocusEntered;
        public event Action? FocusExited;
        public virtual void _GuiInput(InputEvent input) { }
        public virtual void _Process(double delta) { }
        public virtual void _Notification(int what) { }
        public virtual void _ExitTree() { }
        public bool IsInsideTree() => true;
        public bool IsVisibleInTree() { SceneQueries++; return Visible; }
        public bool CanProcess() { SceneQueries++; return Processable; }
        public bool HasFocus() { SceneQueries++; return Focused; }
        public Transform2D GetScreenTransform() { SceneQueries++; return new(new(1, 0), new(0, 1)); }
        public Viewport GetViewport() => _viewport;
        public Window GetWindow() => _window;
        public void SetNotifyTransform(bool enabled) { }
        public void AcceptEvent() { }
        public void CallDeferred(string method) => _deferred.Enqueue(method);
        public void DrainDeferred()
        {
            var calls = 0;
            while (_deferred.TryDequeue(out var method))
            {
                if (++calls > 100) throw new InvalidOperationException("Deferred wakeup loop.");
                GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(this, null);
            }
        }
        public void ChangeVisibility(bool visible) { Visible = visible; VisibilityChanged?.Invoke(); }
        public void Resize(Vector2 size) { Size = size; Resized?.Invoke(); }
    }
}

namespace Square.Graphics
{
    public readonly record struct Size(float Width, float Height);
}

namespace Square.Runtime
{
    public static class Application
    {
        public static bool IsStarted => false;
        public static State Current { get; } = new();
        public sealed class State { public bool IsRunning => false; }
    }
}

namespace Square.Hosting
{
    public sealed class AppWindow
    {
        public string Title => "Test";
        public string RenderBackend { get; set; } = "";
        public string SoftwareSurface => "";
        public string TitleStyle => "";
        public string BorderStyle => "";
    }
    public sealed class ApplicationSession : IDisposable
    {
        public ApplicationSession(AppWindow window, object host) => Window = window;
        public AppWindow Window { get; }
        public TimeSpan? Delay { get; set; } = TimeSpan.Zero;
        public TimeSpan? DelayAfterTick { get; set; }
        public int Ticks { get; private set; }
        public int DelayQueries { get; private set; }
        public int LastQueryThread { get; private set; }
        public bool HasTextEditorFocus => false;
        public bool IsSuspended { get; private set; }
        public Action? DuringTick { get; set; }
        public Action? DuringQuery { get; set; }
        public event Action? FrameRequested;
        public TimeSpan? NextFrameDelay
        {
            get
            {
                DelayQueries++;
                LastQueryThread = System.Environment.CurrentManagedThreadId;
                DuringQuery?.Invoke();
                return IsSuspended ? null : Delay;
            }
        }
        public void RequestFrame(TimeSpan? delay) { Delay = delay; FrameRequested?.Invoke(); }
        public void Attach() { }
        public void Tick() { Ticks++; DuringTick?.Invoke(); Delay = DelayAfterTick; }
        public void Suspend() => IsSuspended = true;
        public void Resume() { IsSuspended = false; RequestFrame(TimeSpan.Zero); }
        public void NotifyFocusLost() { }
        public void NotifyPointerExited() { }
        public void Dispose() { }
    }
}

namespace Square.Platform.Godot
{
    internal sealed class PlatformHostCreateInfo
    {
        public string Title { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public string RenderBackend { get; set; } = "";
        public string SoftwareSurface { get; set; } = "";
        public string TitleStyle { get; set; } = "";
        public string BorderStyle { get; set; } = "";
    }
    internal enum KeyModifiers { None }
    internal sealed class GodotPlatformHost
    {
        internal static GodotPlatformHost? LastCreated { get; private set; }
        internal GodotPlatformHost(global::Godot.Control control, PlatformHostCreateInfo info) => LastCreated = this;
        internal KeyModifiers Modifiers { get; set; }
        internal int MetricUpdates { get; private set; }
        internal event Action? CloseRequested;
        internal void UpdateMetrics(Graphics.Size size, float dpi, bool notify = true) => MetricUpdates++;
        internal void SetImeActive(bool active) { }
    }
    internal static class GodotInput
    {
        internal static bool TryHandle(global::Godot.Control control, GodotPlatformHost host,
            Hosting.ApplicationSession session, global::Godot.InputEvent input) => false;
    }
}
