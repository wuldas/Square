namespace Square.Extensions.WebView;

/// <summary>Raised when a navigation request is submitted to the native browser.</summary>
public sealed class WebViewNavigationStartingEventArgs(string source) : EventArgs
{
    public string Source { get; } = source;
}

/// <summary>Raised when a native navigation completes.</summary>
public sealed class WebViewNavigationCompletedEventArgs(Uri? uri, bool isSuccess, string? error)
    : EventArgs
{
    public Uri? Uri { get; } = uri;
    public bool IsSuccess { get; } = isSuccess;
    public string? Error { get; } = error;
}

/// <summary>Raised when the native document title changes.</summary>
public sealed class WebViewTitleChangedEventArgs(string? title) : EventArgs
{
    public string? Title { get; } = title;
}

/// <summary>Raised when the native browser reports a navigation failure.</summary>
public sealed class WebViewLoadErrorEventArgs(string source, string message) : EventArgs
{
    public string Source { get; } = source;
    public string Message { get; } = message;
}
