using System.Reflection;
using Square.Backends;
using Square.Graphics;
using Square.Platform;
using Square.Rendering;
using Square.Hosting;

namespace Square.DevTools.Tests;

internal sealed class InspectionTestHost : IDisposable
{
    private readonly AutoResetEvent _work = new(false);
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    public AppWindow Window { get; }
    public DesktopApplication Application { get; }

    public InspectionTestHost(Action<AppWindow> initialize, bool render = false)
    {
        var ready = new TaskCompletionSource<(AppWindow, DesktopApplication)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() =>
        {
            DesktopApplication? application = null;
            try
            {
                var window = new AppWindow("real CDP inspection") { RenderBackend = "Software" };
                initialize(window);
                application = new DesktopApplication(window);
                application.PrepareSession();
                application.Dispatcher.WorkAvailable += Wake;
                if (render)
                {
                    application.AttachSessionHost(new InspectionPlatformHost());
                    typeof(DesktopApplication).GetMethod("EnsureRenderer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(application, null);
                    typeof(DesktopApplication).GetMethod("RenderFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(application, null);
                }
                ready.SetResult((window, application));
                while (!_stop.IsCancellationRequested)
                {
                    application.Dispatcher.Run();
                    _work.WaitOne();
                }
                application.Dispatcher.Run();
            }
            catch (Exception error)
            {
                ready.TrySetException(error);
            }
            finally
            {
                if (application != null)
                {
                    application.Dispatcher.WorkAvailable -= Wake;
                    application.DetachSession();
                }
            }
        }) { IsBackground = true, Name = "Square inspection owner" };
        _thread.Start();
        (Window, Application) = ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }

    private void Wake() => _work.Set();

    public Task InvokeAsync(Action action) =>
        Application.Dispatcher.InvokeAsync(action).WaitAsync(TimeSpan.FromSeconds(5));

    public void Dispose()
    {
        _stop.Cancel();
        _work.Set();
        if (!_thread.Join(TimeSpan.FromSeconds(5))) throw new TimeoutException("Inspection owner did not stop.");
        _stop.Dispose();
        _work.Dispose();
    }

    private sealed class InspectionPlatformHost : IPlatformHost
    {
        public Size ClientSize => new(320, 240);
        public float DpiScale => 1;
        public bool IsRunning => true;
        public string Title { get; set; } = "real CDP inspection";
        public CursorKind Cursor { get; set; }
        public KeyModifiers Modifiers => KeyModifiers.None;
        public event Action<Size>? SizeChanged { add { } remove { } }
        public event Action<Point, MouseAction, MouseButton>? MouseEvent { add { } remove { } }
        public event Action<WheelInput>? WheelEvent { add { } remove { } }
        public event Action<int, KeyAction>? KeyEvent { add { } remove { } }
        public event Action<string>? TextInput { add { } remove { } }
        public event Action? Tick { add { } remove { } }
        public void Show() { }
        public void Close() { }
        public IRenderContext CreateRenderContext() => new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = ClientSize,
            DpiScale = DpiScale
        });
        public void PumpEvents() { }
        public void SetTextInputRect(Rect rect) { }
        public string GetClipboardText() => "";
        public void SetClipboardText(string text) { }
        public void Dispose() { }
    }
}
