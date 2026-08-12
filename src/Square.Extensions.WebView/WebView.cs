using System.Text.Json;
using Square.Runtime;
using Square.UI;

namespace Square.Extensions.WebView;

/// <summary>Hosts an operating-system WebView inside the Square visual tree.</summary>
public sealed class WebView : UIElement, INativeViewElement
{
    private readonly IWebViewBackend _backend;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly List<string> _initializationScripts = [];
    private readonly Dictionary<string, WebViewBindingHandler> _bindings = [];
    private NativeViewLayout _lastLayout;
    private Task? _initializationTask;
    private bool _suppressSourceNavigation;
    private bool _disposed;

    public WebView() : this(WebViewBackendRegistry.CreateDefault())
    {
    }

    internal WebView(IWebViewBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _backend.NavigationStarting += HandleNavigationStarting;
        _backend.NavigationCompleted += HandleNavigationCompleted;
        _backend.TitleChanged += HandleTitleChanged;
        _backend.HistoryChanged += HandleHistoryChanged;
        _backend.WebMessageReceived += HandleWebMessageReceived;
        if (_backend.IsInitialized)
            _initializationTask = Task.CompletedTask;
    }

    public string? Source
    {
        get => GetProperty<string>(nameof(Source));
        set => SetProperty(nameof(Source), value);
    }

    public Uri? CurrentUri { get; private set; }
    public string? DocumentTitle { get; private set; }
    public bool IsLoading { get; private set; }
    public bool CanGoBack { get; private set; }
    public bool CanGoForward { get; private set; }
    public Exception? LastError { get; private set; }

    public event EventHandler<WebViewNavigationStartingEventArgs>? NavigationStarting;
    public event EventHandler<WebViewNavigationCompletedEventArgs>? NavigationCompleted;
    public event EventHandler<WebViewTitleChangedEventArgs>? TitleChanged;
    public event EventHandler<WebViewLoadErrorEventArgs>? LoadError;

    public void Init(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        _initializationScripts.Add(script);
        if (_backend.IsInitialized)
            _backend.Init(script);
    }

    public async Task Eval(string script, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(script);
        await EnsureBackendReadyAsync(cancellationToken).ConfigureAwait(true);
        await _backend.EvalAsync(script, cancellationToken).ConfigureAwait(true);
    }

    public async Task Dispatch(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await EnsureBackendReadyAsync(cancellationToken).ConfigureAwait(true);
        await _backend.DispatchAsync(action, cancellationToken).ConfigureAwait(true);
    }

    public async Task Bind(string name, WebViewBindingHandler handler, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handler);
        if (!_bindings.TryAdd(name, handler))
            throw new InvalidOperationException($"A WebView binding named '{name}' already exists.");
        try
        {
            await EnsureBackendReadyAsync(cancellationToken).ConfigureAwait(true);
            await _backend.EvalAsync(CreateBindScript(name), cancellationToken).ConfigureAwait(true);
        }
        catch
        {
            _bindings.Remove(name);
            throw;
        }
    }

    public async Task Unbind(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_bindings.Remove(name))
            throw new KeyNotFoundException($"No WebView binding named '{name}' exists.");
        await EnsureBackendReadyAsync(cancellationToken).ConfigureAwait(true);
        await _backend.EvalAsync(CreateUnbindScript(name), cancellationToken).ConfigureAwait(true);
    }

    public async Task ReturnAsync(string id, int status, string result, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(result);
        await EnsureBackendReadyAsync(cancellationToken).ConfigureAwait(true);
        await _backend.ReturnAsync(id, status, result, cancellationToken).ConfigureAwait(true);
    }

    public void Return(string id, int status, string result)
    {
        ReturnAsync(id, status, result).GetAwaiter().GetResult();
    }

    public Task Navigate(string url, CancellationToken cancellationToken = default) => NavigateAsync(url, cancellationToken);

    public Task SetHtml(string html, CancellationToken cancellationToken = default) =>
        NavigateToStringAsync(html, cancellationToken: cancellationToken);

    public Task NavigateAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        _suppressSourceNavigation = true;
        try { Source = source; }
        finally { _suppressSourceNavigation = false; }
        return NavigateCoreAsync(source, false, cancellationToken);
    }

    public Task NavigateToStringAsync(string html, Uri? baseUri = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        _ = baseUri;
        return NavigateCoreAsync(html, true, cancellationToken);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureBackendReadyAsync(cancellationToken).ConfigureAwait(true);
        await _backend.ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task GoBackAsync(CancellationToken cancellationToken = default)
    {
        await EnsureBackendReadyAsync(cancellationToken).ConfigureAwait(true);
        await _backend.GoBackAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task GoForwardAsync(CancellationToken cancellationToken = default)
    {
        await EnsureBackendReadyAsync(cancellationToken).ConfigureAwait(true);
        await _backend.GoForwardAsync(cancellationToken).ConfigureAwait(true);
    }

    public void Stop() => _backend.Stop();

    public void SynchronizeNativeView(NativeViewLayout layout)
    {
        _lastLayout = layout;
        _backend.Synchronize(layout);
    }

    protected override void OnLoadedCore()
    {
        base.OnLoadedCore();
        var window = AppWindow;
        if (_initializationTask != null || window?.NativeWindow == IntPtr.Zero) return;
        _initializationTask = InitializeBackendAsync(window!.NativeWindow, window.Dispatcher);
    }

    protected override void OnUnloadedCore()
    {
        if (!_disposed)
            _backend.Synchronize(new NativeViewLayout(_lastLayout.Bounds, _lastLayout.DpiScale, false));
        base.OnUnloadedCore();
    }

    protected override void OnDetachedCore()
    {
        if (!_disposed)
        {
            _disposed = true;
            _lifetimeCancellation.Cancel();
            _backend.Dispose();
            _lifetimeCancellation.Dispose();
        }
        base.OnDetachedCore();
    }

    protected override void OnPropertyChanged(string name)
    {
        base.OnPropertyChanged(name);
        if (name == nameof(Source) && !_suppressSourceNavigation && IsLoaded && !string.IsNullOrWhiteSpace(Source))
            _ = NavigateCoreAsync(Source, false, CancellationToken.None);
    }

    private async Task InitializeBackendAsync(IntPtr parentWindow, Dispatcher dispatcher)
    {
        try
        {
            await _backend.InitializeAsync(parentWindow, dispatcher, _lifetimeCancellation.Token).ConfigureAwait(true);
            await dispatcher.InvokeAsync(() =>
            {
                _backend.Synchronize(_lastLayout);
                _backend.Init(CreateBridgeScript());
                foreach (var script in _initializationScripts)
                    _backend.Init(script);
                if (!string.IsNullOrWhiteSpace(Source))
                {
                    LastError = null;
                    IsLoading = true;
                    _backend.NavigateAsync(Source, false, _lifetimeCancellation.Token).GetAwaiter().GetResult();
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LastError = exception;
            IsLoading = false;
            if (!string.IsNullOrWhiteSpace(Source))
                LoadError?.Invoke(this, new WebViewLoadErrorEventArgs(Source, exception.Message));
        }
    }

    private async Task NavigateCoreAsync(string source, bool asHtml, CancellationToken cancellationToken)
    {
        await EnsureBackendReadyAsync(cancellationToken).ConfigureAwait(true);
        LastError = null;
        IsLoading = true;
        await _backend.NavigateAsync(source, asHtml, cancellationToken).ConfigureAwait(true);
    }

    private async Task EnsureBackendReadyAsync(CancellationToken cancellationToken)
    {
        if (_initializationTask == null)
            throw new InvalidOperationException("The WebView must be loaded into a running Square window first.");
        await _initializationTask.WaitAsync(cancellationToken).ConfigureAwait(true);
        if (!_backend.IsInitialized)
            throw new InvalidOperationException("The native WebView backend is not initialized.");
    }

    private void HandleNavigationStarting(string source)
    {
        IsLoading = true;
        NavigationStarting?.Invoke(this, new WebViewNavigationStartingEventArgs(source));
    }

    private void HandleNavigationCompleted(Uri? uri, bool success, string? error)
    {
        IsLoading = false;
        CurrentUri = uri;
        LastError = success ? null : new InvalidOperationException(error ?? "Native WebView navigation failed.");
        if (!success && !string.IsNullOrWhiteSpace(Source) && LastError is { } lastError)
            LoadError?.Invoke(this, new WebViewLoadErrorEventArgs(Source!, lastError.Message));
        NavigationCompleted?.Invoke(this, new WebViewNavigationCompletedEventArgs(uri, success, error));
    }

    private void HandleTitleChanged(string? title)
    {
        DocumentTitle = title;
        TitleChanged?.Invoke(this, new WebViewTitleChangedEventArgs(title));
    }

    private void HandleHistoryChanged(bool canGoBack, bool canGoForward)
    {
        CanGoBack = canGoBack;
        CanGoForward = canGoForward;
    }

    private void HandleWebMessageReceived(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var idElement) ||
                !root.TryGetProperty("method", out var methodElement) ||
                !root.TryGetProperty("params", out var paramsElement)) return;
            var id = idElement.GetString();
            var name = methodElement.GetString();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || !_bindings.TryGetValue(name, out var handler)) return;
            _ = InvokeBindingAsync(handler, new WebViewBindingRequest(this, id, name, paramsElement.GetRawText()));
        }
        catch (JsonException)
        {
        }
    }

    private static async Task InvokeBindingAsync(WebViewBindingHandler handler, WebViewBindingRequest request)
    {
        try { await handler(request).ConfigureAwait(true); }
        catch (Exception exception) { await request.ReturnAsync(1, WebViewJson.Quote(exception.Message)).ConfigureAwait(true); }
    }

    private static string CreateBridgeScript() => """
        (() => {
          const pending = new Map();
          const api = {
            bindings: new Set(),
            bind(name) {
              if (this.bindings.has(name)) return;
              this.bindings.add(name);
              window[name] = (...params) => new Promise((resolve, reject) => {
                const id = crypto.randomUUID();
                pending.set(id, { resolve, reject });
                chrome.webview.postMessage({ id, method: name, params });
              });
            },
            unbind(name) { this.bindings.delete(name); delete window[name]; }
          };
          window.chrome.webview.addEventListener('message', event => {
            const message = event.data;
            const request = pending.get(message?.id);
            if (!request) return;
            pending.delete(message.id);
            let value = message.result;
            try { value = JSON.parse(message.result); } catch { }
            (message.status === 0 ? request.resolve : request.reject)(value);
          });
          window.__squareWebView = api;
        })();
        """;

    private static string CreateBindScript(string name) => $"window.__squareWebView.bind({WebViewJson.Quote(name)});";
    private static string CreateUnbindScript(string name) => $"window.__squareWebView.unbind({WebViewJson.Quote(name)});";
}
