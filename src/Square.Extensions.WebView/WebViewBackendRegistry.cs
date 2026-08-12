namespace Square.Extensions.WebView;

internal static class WebViewBackendRegistry
{
    public static IWebViewBackend CreateDefault() =>
        OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600) ? new WebView2Backend() : new UnsupportedWebViewBackend();

    private sealed class UnsupportedWebViewBackend : IWebViewBackend
    {
        public bool IsInitialized => false;

        public event Action<string>? NavigationStarting
        {
            add { }
            remove { }
        }

        public event Action<Uri?, bool, string?>? NavigationCompleted
        {
            add { }
            remove { }
        }

        public event Action<string?>? TitleChanged
        {
            add { }
            remove { }
        }

        public event Action<bool, bool>? HistoryChanged
        {
            add { }
            remove { }
        }

        public event Action<string>? WebMessageReceived
        {
            add { }
            remove { }
        }

        public Task InitializeAsync(IntPtr parentWindow, Square.Runtime.Dispatcher dispatcher, CancellationToken cancellationToken) =>
            Task.FromException(new PlatformNotSupportedException(
                "The native Square WebView backend is not available on this platform."));

        public void Synchronize(Square.UI.NativeViewLayout layout)
        {
        }

        public Task NavigateAsync(string source, bool asHtml, CancellationToken cancellationToken) =>
            Task.FromException(new PlatformNotSupportedException(
                "The native Square WebView backend is not available on this platform."));

        public void Init(string script)
        {
            throw new PlatformNotSupportedException(
                "The native Square WebView backend is not available on this platform.");
        }

        public Task EvalAsync(string script, CancellationToken cancellationToken) =>
            Task.FromException(new PlatformNotSupportedException(
                "The native Square WebView backend is not available on this platform."));

        public Task DispatchAsync(Action action, CancellationToken cancellationToken) =>
            Task.FromException(new PlatformNotSupportedException(
                "The native Square WebView backend is not available on this platform."));

        public Task ReturnAsync(string id, int status, string result, CancellationToken cancellationToken) =>
            Task.FromException(new PlatformNotSupportedException(
                "The native Square WebView backend is not available on this platform."));

        public Task ReloadAsync(CancellationToken cancellationToken) =>
            Task.FromException(new PlatformNotSupportedException(
                "The native Square WebView backend is not available on this platform."));

        public Task GoBackAsync(CancellationToken cancellationToken) =>
            Task.FromException(new PlatformNotSupportedException(
                "The native Square WebView backend is not available on this platform."));

        public Task GoForwardAsync(CancellationToken cancellationToken) =>
            Task.FromException(new PlatformNotSupportedException(
                "The native Square WebView backend is not available on this platform."));

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }
}
