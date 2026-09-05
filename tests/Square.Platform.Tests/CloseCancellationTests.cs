using System.ComponentModel;
using System.Reflection;
using Square.Graphics;
using Square.Hosting;
using Square.Platform;
using Square.Rendering;
using Xunit;

namespace Square.Platform.Tests;

public sealed class CloseCancellationTests
{
    [Fact]
    public void AppWindow_ForwardsClosingAndHonorsCancel()
    {
        var window = new AppWindow("close", 64, 48);
        var host = new ClosingHost();
        typeof(AppWindow)
            .GetMethod("Attach", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [host]);

        Assert.True(window.SupportsCloseCancellation);
        var seen = 0;
        window.Closing += (_, e) =>
        {
            seen++;
            e.Cancel = true;
        };

        window.Close();
        Assert.Equal(1, seen);
        Assert.False(host.Destroyed);

        typeof(AppWindow)
            .GetMethod("Detach", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [host]);
        Assert.False(window.SupportsCloseCancellation);
    }

    private sealed class ClosingHost : IPlatformHost, IPlatformCloseRequestSource
    {
        public bool Destroyed { get; private set; }
        public Size ClientSize => new(64, 48);
        public float DpiScale => 1f;
        public bool IsRunning { get; private set; } = true;
        public string Title { get; set; } = "close";
        public CursorKind Cursor { get; set; }
        public KeyModifiers Modifiers => KeyModifiers.None;

        public event Action<Size>? SizeChanged { add { } remove { } }
        public event Action<Point, MouseAction, MouseButton>? MouseEvent { add { } remove { } }
        public event Action<WheelInput>? WheelEvent { add { } remove { } }
        public event Action<int, KeyAction>? KeyEvent { add { } remove { } }
        public event Action<string>? TextInput { add { } remove { } }
        public event Action? Tick { add { } remove { } }
        public event EventHandler<CancelEventArgs>? Closing;

        public void Show() => IsRunning = true;

        public void Close()
        {
            var args = new CancelEventArgs();
            Closing?.Invoke(this, args);
            if (args.Cancel)
                return;
            Destroyed = true;
            IsRunning = false;
        }

        public IRenderContext CreateRenderContext() => throw new NotSupportedException();
        public void PumpEvents() { }
        public void SetTextInputRect(Rect rect) { }
        public string GetClipboardText() => "";
        public void SetClipboardText(string text) { }
        public void Dispose() { }
    }
}
