using NativeWebView = Square.Extensions.WebView.WebView;
using Xunit;

namespace Square.Extensions.WebView.Tests;

public sealed class WebViewStateTests
{
    [Fact]
    public void SourceReadsAndWritesThroughSquarePropertyStore()
    {
        var view = new NativeWebView();

        view.SetProperty(nameof(NativeWebView.Source), "https://example.com/");

        Assert.Equal("https://example.com/", view.Source);
    }

    [Fact]
    public void NewWebViewStartsWithoutNavigationState()
    {
        var view = new NativeWebView();

        Assert.Null(view.Source);
        Assert.Null(view.CurrentUri);
        Assert.False(view.IsLoading);
        Assert.False(view.CanGoBack);
        Assert.False(view.CanGoForward);
        Assert.Null(view.LastError);
    }
}
