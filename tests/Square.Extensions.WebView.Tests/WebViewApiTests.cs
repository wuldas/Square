using NativeWebView = Square.Extensions.WebView.WebView;
using Square.Graphics;
using Square.Runtime;
using Square.UI;
using Xunit;

#pragma warning disable CS0067

namespace Square.Extensions.WebView.Tests;

public sealed class WebViewApiTests
{
    [Fact]
    public async Task NavigateMatchesWebviewStyleUrlOperation()
    {
        var backend = new RecordingBackend();
        var view = new NativeWebView(backend);

        await view.Navigate("https://example.com");

        Assert.Equal(("https://example.com", false), backend.LastNavigation);
        Assert.Equal("https://example.com", view.Source);
    }

    [Fact]
    public async Task SetHtmlMatchesWebviewStyleHtmlOperation()
    {
        var backend = new RecordingBackend();
        var view = new NativeWebView(backend);

        await view.SetHtml("<h1>Hello</h1>");

        Assert.Equal(("<h1>Hello</h1>", true), backend.LastNavigation);
    }

    [Fact]
    public void InitStoresDocumentStartupScript()
    {
        var backend = new RecordingBackend();
        var view = new NativeWebView(backend);

        view.Init("window.__squareReady = true;");

        Assert.Equal("window.__squareReady = true;", backend.LastInitScript);
    }

    [Fact]
    public async Task EvalForwardsJavascriptToBackend()
    {
        var backend = new RecordingBackend();
        var view = new NativeWebView(backend);

        await view.Eval("document.title = 'Square';");

        Assert.Equal("document.title = 'Square';", backend.LastScript);
    }

    [Fact]
    public async Task DispatchForwardsWorkToBackendDispatcher()
    {
        var backend = new RecordingBackend();
        var view = new NativeWebView(backend);
        var called = false;

        await view.Dispatch(() => called = true);

        Assert.True(called);
        Assert.True(backend.DispatchCalled);
    }

    private sealed class RecordingBackend : IWebViewBackend
    {
        public (string Source, bool IsHtml) LastNavigation { get; private set; }
        public string? LastInitScript { get; private set; }
        public string? LastScript { get; private set; }
        public bool DispatchCalled { get; private set; }
        public bool IsInitialized => true;
        public event Action<string>? NavigationStarting;
        public event Action<Uri?, bool, string?>? NavigationCompleted;
        public event Action<string?>? TitleChanged;
        public event Action<bool, bool>? HistoryChanged;
        public event Action<string>? WebMessageReceived;

        public Task InitializeAsync(IntPtr parentWindow, Dispatcher dispatcher, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Synchronize(NativeViewLayout layout)
        {
        }
        public Task NavigateAsync(string source, bool asHtml, CancellationToken cancellationToken)
        {
            LastNavigation = (source, asHtml);
            return Task.CompletedTask;
        }

        public void Init(string script) => LastInitScript = script;

        public Task EvalAsync(string script, CancellationToken cancellationToken)
        {
            LastScript = script;
            return Task.CompletedTask;
        }

        public Task DispatchAsync(Action action, CancellationToken cancellationToken)
        {
            DispatchCalled = true;
            action();
            return Task.CompletedTask;
        }
        public Task ReturnAsync(string id, int status, string result, CancellationToken cancellationToken) => Task.CompletedTask;
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
