using NativeWebView = Square.Extensions.WebView.WebView;
using Square.Graphics;
using Square.Runtime;
using Square.UI;
using Xunit;

#pragma warning disable CS0067

namespace Square.Extensions.WebView.Tests;

public sealed class WebViewGeometryTests
{
    [Fact]
    public void SynchronizeNativeViewForwardsBoundsDpiAndVisibility()
    {
        var backend = new RecordingBackend();
        var view = new NativeWebView(backend);
        var layout = new NativeViewLayout(new Rect(12, 24, 320, 180), 1.25f, true);

        view.SynchronizeNativeView(layout);

        Assert.Equal(layout, backend.LastLayout);
    }

    private sealed class RecordingBackend : IWebViewBackend
    {
        public NativeViewLayout LastLayout { get; private set; }
        public bool IsInitialized => true;
        public event Action<string>? NavigationStarting;
        public event Action<Uri?, bool, string?>? NavigationCompleted;
        public event Action<string?>? TitleChanged;
        public event Action<bool, bool>? HistoryChanged;
        public event Action<string>? WebMessageReceived;

        public Task InitializeAsync(IntPtr parentWindow, Dispatcher dispatcher, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Synchronize(NativeViewLayout layout) => LastLayout = layout;
        public void Init(string script)
        {
        }
        public Task EvalAsync(string script, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DispatchAsync(Action action, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReturnAsync(string id, int status, string result, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NavigateAsync(string source, bool asHtml, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task GoBackAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task GoForwardAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }
}
