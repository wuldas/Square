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

        var document = Assert.IsType<UIDocument>(new AppWindow("webview-registration-test").Document);
        var element = document.CreateComponentElement("urn:square:webview", "WebView");

        Assert.IsType<NativeWebView>(element);
        Assert.Same(document, element.OwnerDocument);
    }
}
