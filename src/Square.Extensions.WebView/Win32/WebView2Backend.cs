using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DirectN;
using DirectN.Extensions;
using DirectN.Extensions.Com;
using WebView2;
using WebView2.Utilities;
using Square.Runtime;
using Square.UI;

namespace Square.Extensions.WebView;

[SupportedOSPlatform("windows5.1.2600")]
internal sealed class WebView2Backend : IWebViewBackend
{
    private ComObject<ICoreWebView2Controller>? _controller;
    private ICoreWebView2? _webView;
    private Dispatcher? _dispatcher;
    private NativeViewLayout _lastLayout;
    private bool _disposed;

    private EventRegistrationToken _navigationStartingToken;
    private EventRegistrationToken _navigationCompletedToken;
    private EventRegistrationToken _documentTitleChangedToken;
    private EventRegistrationToken _historyChangedToken;
    private EventRegistrationToken _webMessageReceivedToken;

    private CoreWebView2NavigationStartingEventHandler? _navigationStartingHandler;
    private CoreWebView2NavigationCompletedEventHandler? _navigationCompletedHandler;
    private CoreWebView2DocumentTitleChangedEventHandler? _documentTitleChangedHandler;
    private CoreWebView2HistoryChangedEventHandler? _historyChangedHandler;
    private CoreWebView2WebMessageReceivedEventHandler? _webMessageReceivedHandler;
    private readonly List<CoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler> _scriptHandlers = [];
    private readonly List<CoreWebView2ExecuteScriptCompletedHandler> _executeScriptHandlers = [];
    private readonly List<PWSTR> _ownedInputValues = [];

    public bool IsInitialized => !_disposed && _webView != null && _controller != null;

    public event Action<string>? NavigationStarting;
    public event Action<Uri?, bool, string?>? NavigationCompleted;
    public event Action<string?>? TitleChanged;
    public event Action<bool, bool>? HistoryChanged;
    public event Action<string>? WebMessageReceived;

    public async Task InitializeAsync(IntPtr parentWindow, Dispatcher dispatcher, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("WebView2 is supported only on Windows.");
        if (parentWindow == IntPtr.Zero)
            throw new InvalidOperationException("A native parent window is required to initialize WebView2.");
        ArgumentNullException.ThrowIfNull(dispatcher);
        if (IsInitialized) return;

        _dispatcher = dispatcher;
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void CreateOnDispatcher()
        {
            try
            {
                WebView2EnvironmentManager.CreateEnvironment((environmentHr, environment) =>
                {
                    try
                    {
                        environmentHr.ThrowOnError();
                        if (environment == null)
                            throw new InvalidOperationException("WebView2 returned a null environment.");


                        var result = environment.CreateCoreWebView2Controller(
                            new HWND(parentWindow),
                            new CoreWebView2CreateCoreWebView2ControllerCompletedHandler((hr, controller) =>
                            {
                                try
                                {
                                    hr.ThrowOnError();

                                    InitializeController(controller);
                                    completion.TrySetResult();
                                }
                                catch (Exception exception)
                                {
                                    completion.TrySetException(exception);
                                }
                            }));
                        result.ThrowOnError();
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                });
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        if (dispatcher.CheckAccess())
            CreateOnDispatcher();
        else
            dispatcher.Invoke(CreateOnDispatcher);

        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Synchronize(NativeViewLayout layout)
    {
        _lastLayout = layout;
        if (_dispatcher == null || _dispatcher.CheckAccess())
        {
            SynchronizeCore(layout);
            return;
        }

        _dispatcher.Invoke(() => SynchronizeCore(layout));
    }

    public Task NavigateAsync(string source, bool asHtml, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_dispatcher == null || _dispatcher.CheckAccess())
        {
            NavigateCore(source, asHtml);
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(() => NavigateCore(source, asHtml));
    }

    public void Init(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        EnsureInitialized();
        if (_dispatcher!.CheckAccess())
        {
            InitCore(script);
            return;
        }

        _dispatcher.Invoke(() => InitCore(script));
    }

    public Task EvalAsync(string script, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(script);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        return InvokeEvalAsync(script, cancellationToken);
    }

    public Task DispatchAsync(Action action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        if (_dispatcher!.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(action);
    }

    public Task ReturnAsync(string id, int status, string result, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();
        return InvokeAsync(() =>
        {
            var value = PWSTR.From($"{{\"id\":{WebViewJson.Quote(id)},\"status\":{status},\"result\":{result}}}");
            try
            {
                _webView!.PostWebMessageAsJson(value).ThrowOnError();
            }
            finally
            {
                Free(value);
            }
        });
    }

    private void InitCore(string script)
    {
        var value = PWSTR.From(script);
        _ownedInputValues.Add(value);
        CoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler? handler = null;
        handler = new CoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler((hr, result) =>
        {
            if (result.Value != IntPtr.Zero)
                _ = result.ToString();
            try { hr.ThrowOnError(); }
            catch { }
            finally
            {
                _scriptHandlers.Remove(handler!);
            }
        });
        _scriptHandlers.Add(handler);
        try
        {
            _webView!.AddScriptToExecuteOnDocumentCreated(value, handler).ThrowOnError();
        }
        catch
        {
            _scriptHandlers.Remove(handler);
            throw;
        }
    }

    private Task InvokeEvalAsync(string script, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Execute()
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = PWSTR.From(script);
            _ownedInputValues.Add(value);
            CoreWebView2ExecuteScriptCompletedHandler? handler = null;
            try
            {
                handler = new CoreWebView2ExecuteScriptCompletedHandler((hr, result) =>
                {
                    if (result.Value != IntPtr.Zero)
                        Free(result);
                    try
                    {
                        hr.ThrowOnError();
                        completion.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                    finally
                    {
                        _executeScriptHandlers.Remove(handler!);
                    }
                });
                _executeScriptHandlers.Add(handler);
                _webView!.ExecuteScript(
                    value,
                    handler).ThrowOnError();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
                if (handler != null)
                    _executeScriptHandlers.Remove(handler);
            }
        }

        if (_dispatcher!.CheckAccess())
            Execute();
        else
            _dispatcher.Invoke(Execute);

        return completion.Task.WaitAsync(cancellationToken);
    }

    public Task ReloadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return InvokeAsync(() => _webView!.Reload().ThrowOnError());
    }

    public Task GoBackAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return InvokeAsync(() => _webView!.GoBack().ThrowOnError());
    }

    public Task GoForwardAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return InvokeAsync(() => _webView!.GoForward().ThrowOnError());
    }

    public void Stop()
    {
        if (_dispatcher == null || _dispatcher.CheckAccess())
        {
            if (IsInitialized) _webView!.Stop().ThrowOnError();
            return;
        }

        _dispatcher.Invoke(() =>
        {
            if (IsInitialized) _webView!.Stop().ThrowOnError();
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_dispatcher != null && !_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(Dispose);
            return;
        }

        _disposed = true;
        var webView = _webView;
        if (webView != null)
        {
            TryRemoveEvents(webView);
            _webView = null;
        }

        try { _controller?.Object.Close(); }
        catch { }
        _controller?.Dispose();
        _controller = null;
        foreach (var value in _ownedInputValues)
            Free(value);
        _ownedInputValues.Clear();
    }

    private void InitializeController(ICoreWebView2Controller nativeController)
    {
        _dispatcher!.VerifyAccess();

        _controller = new ComObject<ICoreWebView2Controller>(nativeController);

        nativeController.get_CoreWebView2(out var webView).ThrowOnError();

        _webView = webView;
        RegisterEvents();

        SynchronizeCore(_lastLayout);

    }

    private void SynchronizeCore(NativeViewLayout layout)
    {
        if (!IsInitialized) return;

        var bounds = layout.Bounds;
        var dpi = layout.DpiScale <= 0 || !float.IsFinite(layout.DpiScale) ? 1f : layout.DpiScale;
        var left = ToPhysical(bounds.X, dpi);
        var top = ToPhysical(bounds.Y, dpi);
        var width = Math.Max(0, ToPhysical(bounds.Width, dpi));
        var height = Math.Max(0, ToPhysical(bounds.Height, dpi));
        _controller!.Object.put_Bounds(RECT.Sized(left, top, width, height)).ThrowOnError();
        _controller.Object.put_IsVisible(new BOOL(layout.IsVisible)).ThrowOnError();
    }

    private void NavigateCore(string source, bool asHtml)
    {
        EnsureInitialized();
        var value = PWSTR.From(source);
        _ownedInputValues.Add(value);
        if (asHtml)
            _webView!.NavigateToString(value).ThrowOnError();
        else
            _webView!.Navigate(value).ThrowOnError();
    }

    private Task InvokeAsync(Action action)
    {
        EnsureInitialized();
        if (_dispatcher!.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(action);
    }

    private void RegisterEvents()
    {

        var webView = _webView!;
        _navigationStartingHandler = new CoreWebView2NavigationStartingEventHandler((_, args) =>
        {
            args.get_Uri(out var uri).ThrowOnError();
            NavigationStarting?.Invoke(ReadAndFree(uri) ?? "");
        });
        _navigationCompletedHandler = new CoreWebView2NavigationCompletedEventHandler((_, args) =>
        {
            var success = BOOL.FALSE;
            var status = default(COREWEBVIEW2_WEB_ERROR_STATUS);
            args.get_IsSuccess(ref success).ThrowOnError();
            args.get_WebErrorStatus(ref status).ThrowOnError();
            var uri = GetCurrentUri();
            NavigationCompleted?.Invoke(uri, success.Value != 0, success.Value != 0 ? null : status.ToString());
            RaiseHistoryChanged();
        });
        _documentTitleChangedHandler = new CoreWebView2DocumentTitleChangedEventHandler((_, _) =>
        {
            webView.get_DocumentTitle(out var title).ThrowOnError();
            TitleChanged?.Invoke(ReadAndFree(title));
        });
        _historyChangedHandler = new CoreWebView2HistoryChangedEventHandler((_, _) => RaiseHistoryChanged());

        webView.add_NavigationStarting(_navigationStartingHandler, ref _navigationStartingToken).ThrowOnError();

        webView.add_NavigationCompleted(_navigationCompletedHandler, ref _navigationCompletedToken).ThrowOnError();

        webView.add_DocumentTitleChanged(_documentTitleChangedHandler, ref _documentTitleChangedToken).ThrowOnError();

        webView.add_HistoryChanged(_historyChangedHandler, ref _historyChangedToken).ThrowOnError();

        _webMessageReceivedHandler = new CoreWebView2WebMessageReceivedEventHandler((_, args) =>
        {
            args.get_WebMessageAsJson(out var message).ThrowOnError();
            WebMessageReceived?.Invoke(ReadAndFree(message) ?? "{}");
        });
        webView.add_WebMessageReceived(_webMessageReceivedHandler, ref _webMessageReceivedToken).ThrowOnError();

        RaiseHistoryChanged();
    }

    private void TryRemoveEvents(ICoreWebView2 webView)
    {
        try { webView.remove_NavigationStarting(_navigationStartingToken); } catch { }
        try { webView.remove_NavigationCompleted(_navigationCompletedToken); } catch { }
        try { webView.remove_DocumentTitleChanged(_documentTitleChangedToken); } catch { }
        try { webView.remove_HistoryChanged(_historyChangedToken); } catch { }
        try { webView.remove_WebMessageReceived(_webMessageReceivedToken); } catch { }
    }


    private void RaiseHistoryChanged()
    {
        if (!IsInitialized) return;
        var canGoBack = BOOL.FALSE;
        var canGoForward = BOOL.FALSE;
        _webView!.get_CanGoBack(ref canGoBack).ThrowOnError();
        _webView.get_CanGoForward(ref canGoForward).ThrowOnError();
        HistoryChanged?.Invoke(canGoBack.Value != 0, canGoForward.Value != 0);
    }

    private Uri? GetCurrentUri()
    {
        _webView!.get_Source(out var source).ThrowOnError();
        var text = ReadAndFree(source);
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) ? uri : null;
    }

    private void EnsureInitialized()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsInitialized)
            throw new InvalidOperationException("The WebView has not been initialized by a running Square window.");
    }

    private static int ToPhysical(float value, float dpi) =>
        (int)MathF.Round(value * dpi, MidpointRounding.AwayFromZero);

    private static string? ReadAndFree(PWSTR value)
    {
        if (value.Value == IntPtr.Zero) return null;
        var text = value.ToString();
        Free(value);
        return text;
    }

    private static void Free(PWSTR value)
    {
        if (value.Value != IntPtr.Zero)
            Marshal.FreeCoTaskMem(value.Value);
    }
}
