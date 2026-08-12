using NativeWebView = Square.Extensions.WebView.WebView;
using Square.Graphics;
using Square.Runtime;
using Square.UI;
using Xunit;

#pragma warning disable CS0067

namespace Square.Extensions.WebView.Tests;

public sealed class WebViewBindingTests
{
    [Fact]
    public async Task BindingHandlerReceivesJsonRequestAndCanReturnJsonResult()
    {
        var backend = new RecordingBackend();
        var view = new NativeWebView(backend);
        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await view.Bind("add", async request =>
        {
            Assert.Equal("request-1", request.Id);
            Assert.Equal("add", request.Name);
            Assert.Equal("[2,3]", request.ArgumentsJson);
            await request.ReturnAsync(0, "5");
            handled.SetResult();
        });

        backend.EmitMessage("{\"id\":\"request-1\",\"method\":\"add\",\"params\":[2,3]}");
        await handled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Contains("window.__squareWebView.bind(\"add\");", backend.Scripts);
        Assert.Equal(("request-1", 0, "5"), backend.LastReturn);
    }

    [Fact]
    public async Task DuplicateBindingIsRejectedAndUnbindRemovesIt()
    {
        var backend = new RecordingBackend();
        var view = new NativeWebView(backend);

        await view.Bind("ping", _ => ValueTask.CompletedTask);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            view.Bind("ping", _ => ValueTask.CompletedTask));

        await view.Unbind("ping");

        Assert.Contains("window.__squareWebView.unbind(\"ping\");", backend.Scripts);
    }

    [Fact]
    public void ReturnForwardsResponseSynchronously()
    {
        var backend = new RecordingBackend();
        var view = new NativeWebView(backend);

        view.Return("request-2", 0, "{\"value\":7}");

        Assert.Equal(("request-2", 0, "{\"value\":7}"), backend.LastReturn);
    }

    private sealed class RecordingBackend : IWebViewBackend
    {
        public List<string> Scripts { get; } = [];
        public (string Id, int Status, string Result)? LastReturn { get; private set; }
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
        public Task NavigateAsync(string source, bool asHtml, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Init(string script) => Scripts.Add(script);
        public Task EvalAsync(string script, CancellationToken cancellationToken)
        {
            Scripts.Add(script);
            return Task.CompletedTask;
        }
        public Task DispatchAsync(Action action, CancellationToken cancellationToken)
        {
            action();
            return Task.CompletedTask;
        }
        public Task ReturnAsync(string id, int status, string result, CancellationToken cancellationToken)
        {
            RecordReturn(id, status, result);
            return Task.CompletedTask;
        }
        public Task ReloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task GoBackAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task GoForwardAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Stop()
        {
        }
        public void EmitMessage(string message) => WebMessageReceived?.Invoke(message);
        public void RecordReturn(string id, int status, string result) => LastReturn = (id, status, result);
        public void Dispose()
        {
        }
    }
}
