using Square.Backends;
using Square.Controls;
using Square.Events;
using Square.Graphics;
using Square.Hosting;
using Square.Platform;
using Xunit;

using TextControl = Square.Controls.Text;
namespace Square.Platform.Tests;

public sealed class ApplicationSessionTests
{
    [Fact]
    public void ExternalSessionDoesNotPumpAndCleansHostOnce()
    {
        var window = new AppWindow("session", 64, 48);
        window.Load(new TextControl("session"));
        var host = new FakeHost();
        using var session = new ApplicationSession(window, host);

        session.Attach();
        session.Attach();
        Assert.True(session.IsAttached);
        Assert.Equal(1, host.ShowCount);
        Assert.Equal(1, host.RenderContextCount);
        Assert.Equal(0, host.PumpEventsCount);
        session.ProcessFrame();
        Assert.False(session.HasPendingFrame);

        session.Suspend();
        session.Suspend();
        Assert.True(session.IsSuspended);
        session.Tick();
        session.Resume();
        session.Resume();
        Assert.False(session.IsSuspended);
        session.ProcessFrame();

        session.Detach();
        session.Detach();
        Assert.True(session.IsDetached);
        Assert.Equal(1, host.DisposeCount);
        Assert.Equal(1, host.ShowAfterFirstFrameCount);
    }
    [Fact]
    public void FailedExternalAttachDisposesSuppliedHost()
    {
        var window = new AppWindow("session", 64, 48);
        window.Load(new TextControl("session"));
        var host = new FakeHost { ThrowOnCreateRenderContext = true };
        using var session = new ApplicationSession(window, host);

        Assert.Throws<InvalidOperationException>(session.Attach);
        Assert.True(session.IsDetached);
        Assert.Equal(1, host.DisposeCount);
        Assert.Equal(0, host.PumpEventsCount);
    }
    [Fact]
    public void DispatcherSignalsOnlyWhenQueueBecomesNonEmpty()
    {
        var dispatcher = new Square.Runtime.Dispatcher();
        var notifications = 0;
        dispatcher.WorkAvailable += () => notifications++;

        dispatcher.Invoke(() => { });
        dispatcher.Invoke(() => { });
        Assert.Equal(1, notifications);
        dispatcher.Run();

        dispatcher.Invoke(() => { });
        Assert.Equal(2, notifications);
    }

    [Fact]
    public void PointerFactoryPreservesDeviceIdentityAndCoordinates()
    {
        var input = new PointerInput(
            new Point(12.5f, 8.25f),
            PointerAction.Down,
            pointerId: 7,
            deviceKind: PointerDeviceKind.Touch,
            button: MouseButton.Left,
            isPrimary: true);

        var pointer = StandardEvents.CreatePointerDown(input);

        Assert.Equal(StandardEvents.PointerDown, pointer.Type);
        Assert.Equal(12.5f, pointer.ClientX);
        Assert.Equal(8.25f, pointer.ClientY);
        Assert.Equal(7, pointer.PointerId);
        Assert.Equal(PointerDeviceKind.Touch, pointer.PointerType);
        Assert.Equal(0, pointer.Button);
        Assert.True(pointer.IsPrimary);
        Assert.False(pointer.IsTrusted);
    }

    [Fact]
    public void CompositionUpdatesDoNotCreateIntermediateUndoEntries()
    {
        var input = new Input { Value = "a" };
        ITextInputClient client = input;
        client.SetSelection(1, 1);

        client.SetComposingText("ni");
        Assert.Equal("ani", input.Value);
        client.SetComposingText("你");
        Assert.Equal("a你", input.Value);

        client.CommitText("好");
        Assert.Equal("a好", input.Value);
        Assert.Equal(-1, client.CompositionStart);
        Assert.True(input.CanUndo);
        input.HandleKey(90, control: true);
        Assert.Equal("a", input.Value);
    }

    [Fact]
    public void DispatcherFrameNotifiesAfterUpdatedPixelsArePresented()
    {
        var root = new View();
        root.Style.Set("background", "#ff0000");
        var window = new AppWindow("frames", 64, 48);
        window.Load(root);
        var host = new FakeHost();
        using var session = new ApplicationSession(window, host);
        var presentedBlue = new List<byte>();
        session.FramePresented += () => presentedBlue.Add(host.LastFrame!.GetPixel(10, 10)[0]);
        session.Attach();

        window.Dispatcher.Invoke(() => root.Style.Set("background", "#0000ff"));
        session.Tick();

        Assert.Equal(new byte[] { 0, 255 }, presentedBlue);
        Assert.False(session.HasPendingFrame);
        session.Tick();
        Assert.Equal(2, presentedBlue.Count);
    }

    [Fact]
    public void PendingControlFrameSurvivesSuspendAndResume()
    {
        var root = new ScheduledView();
        var window = new AppWindow("scheduled", 64, 48);
        window.Load(root);
        var host = new FakeHost();
        using var session = new ApplicationSession(window, host);
        session.Attach();
        root.DispatchEvent(StandardEvents.CreateRequestFrame(TimeSpan.Zero));

        session.Suspend();
        session.Tick();
        Assert.Equal(0, root.FrameCount);
        session.Resume();
        session.Tick();

        Assert.Equal(1, root.FrameCount);
        Assert.Equal(255, host.LastFrame!.GetPixel(10, 10)[0]);
        Assert.False(session.HasPendingFrame);
    }

    [Fact]
    public void CssAnimationKeepsExternalSessionPendingUntilStopped()
    {
        var root = new View();
        root.ClassList.Add("animated");
        var window = new AppWindow("animation", 64, 48);
        window.Load(root);
        window.LoadGlobalCssText("@keyframes fade { from { opacity: 0; } to { opacity: 1; } } " +
            ".animated { animation: fade 60s linear; }");
        using var session = new ApplicationSession(window, new FakeHost());
        session.Attach();
        session.Tick();

        Assert.True(session.HasPendingFrame);
        root.ClassList.Remove("animated");
        session.Tick();
        Assert.False(session.HasPendingFrame);
    }

    [Fact]
    public void ReplacingNativeRenderContextPreservesDocumentAndPendingFrames()
    {
        var root = new ScheduledView();
        root.Style.Set("background", "#ff0000");
        var window = new AppWindow("surface", 64, 48);
        window.Load(root);
        var host = new FakeHost();
        using var session = new ApplicationSession(window, host);
        session.Attach();
        Assert.Throws<InvalidOperationException>(session.ReleaseRenderContext);
        root.DispatchEvent(StandardEvents.CreateRequestFrame(TimeSpan.Zero));

        session.Suspend();
        session.ReleaseRenderContext();
        session.Resume();
        session.ProcessFrame();
        session.Tick();

        Assert.Equal(1, root.FrameCount);
        Assert.Equal(255, host.LastFrame!.GetPixel(10, 10)[0]);
        Assert.True(root.IsAttached);
        Assert.True(root.IsLoaded);
        Assert.Equal(0, host.DisposeCount);
    }

    private sealed class ScheduledView : View
    {
        public int FrameCount { get; private set; }

        protected override void OnFrameDueCore()
        {
            base.OnFrameDueCore();
            FrameCount++;
            Style.Set("background", "#0000ff");
        }
    }

    private sealed class FakeHost : IPlatformHost
    {
        private readonly RenderBackendFactory _factory = new();
        private bool _disposed;
        private string _title = "session";

        public Size ClientSize { get; } = new(64, 48);
        public float DpiScale => 1f;
        public bool IsRunning { get; private set; }
        public AppWindowState State => AppWindowState.Normal;
        public string Title { get => _title; set => _title = value; }
        public CursorKind Cursor { get; set; }
        public KeyModifiers Modifiers => KeyModifiers.None;
        public int ShowCount { get; private set; }
        public int ShowAfterFirstFrameCount { get; private set; }
        public int PumpEventsCount { get; private set; }
        public int RenderContextCount { get; private set; }
        public int DisposeCount { get; private set; }
        public bool ThrowOnCreateRenderContext { get; init; }
        public Bitmap? LastFrame { get; private set; }

        public event Action<Size>? SizeChanged
        {
            add { }
            remove { }
        }
        public event Action<Point, MouseAction, MouseButton>? MouseEvent
        {
            add { }
            remove { }
        }
        public event Action<WheelInput>? WheelEvent
        {
            add { }
            remove { }
        }
        public event Action<int, KeyAction>? KeyEvent
        {
            add { }
            remove { }
        }
        public event Action<string>? TextInput
        {
            add { }
            remove { }
        }
        public event Action? Tick
        {
            add { }
            remove { }
        }

        public void Show()
        {
            ShowCount++;
            IsRunning = true;
        }

        public void ShowAfterFirstFrame() => ShowAfterFirstFrameCount++;
        public void Close() => IsRunning = false;

        public IRenderContext CreateRenderContext()
        {
            RenderContextCount++;
            if (ThrowOnCreateRenderContext)
                throw new InvalidOperationException("renderer failure");
            return _factory.CreateContext(new RenderContextCreateInfo
            {
                CanvasSize = ClientSize,
                DpiScale = DpiScale,
                PresentFrame = (bitmap, _) => LastFrame = bitmap
            });
        }

        public void PumpEvents() => PumpEventsCount++;
        public void SetTextInputRect(Rect rect) { }
        public string GetClipboardText() => "";
        public void SetClipboardText(string text) { }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DisposeCount++;
            IsRunning = false;
        }
    }
}
