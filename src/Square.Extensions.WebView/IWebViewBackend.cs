using Square.Runtime;
using Square.UI;

namespace Square.Extensions.WebView;

internal interface IWebViewBackend : IDisposable
{
    bool IsInitialized { get; }

    event Action<string>? NavigationStarting;
    event Action<Uri?, bool, string?>? NavigationCompleted;
    event Action<string?>? TitleChanged;
    event Action<bool, bool>? HistoryChanged;
    event Action<string>? WebMessageReceived;

    Task InitializeAsync(IntPtr parentWindow, Dispatcher dispatcher, CancellationToken cancellationToken);

    void Synchronize(NativeViewLayout layout);

    Task NavigateAsync(string source, bool asHtml, CancellationToken cancellationToken);
    void Init(string script);
    Task EvalAsync(string script, CancellationToken cancellationToken);
    Task DispatchAsync(Action action, CancellationToken cancellationToken);
    Task ReturnAsync(string id, int status, string result, CancellationToken cancellationToken);
    Task ReloadAsync(CancellationToken cancellationToken);
    Task GoBackAsync(CancellationToken cancellationToken);
    Task GoForwardAsync(CancellationToken cancellationToken);
    void Stop();
}
