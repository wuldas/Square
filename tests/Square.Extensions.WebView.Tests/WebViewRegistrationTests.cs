using NativeWebView = Square.Extensions.WebView.WebView;
using Square.Extensions.WebView;
using Square.Hosting;
using Square.UI;
using Xunit;

namespace Square.Extensions.WebView.Tests;

public sealed class WebViewRegistrationTests
{
    [Fact]
    public void RegisterDefaultsIsIdempotentAndCreatesWebViewTag()
    {
        WebViewRegistration.RegisterDefaults();
        WebViewRegistration.RegisterDefaults();

        var window = new AppWindow("webview-registration-test");
        var createElement = window.Document.GetType().GetMethod("CreateElement", [typeof(string)]);
        Assert.NotNull(createElement);

        var element = createElement!.Invoke(window.Document, ["WebView"]);

        Assert.IsType<NativeWebView>(element);
    }
}
