using System.Runtime.Versioning;
using DirectN;
using WebView2.Utilities;

namespace Square.Extensions.WebView;

[SupportedOSPlatform("windows5.1.2600")]
internal static class WebView2EnvironmentManager
{
    public static void CreateEnvironment(
        Action<HRESULT, global::WebView2.ICoreWebView2Environment?> completed)
    {
        ArgumentNullException.ThrowIfNull(completed);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("WebView2 is supported only on Windows.");


        WebView2Utilities.Initialize(typeof(WebView2EnvironmentManager).Assembly);
        var browserVersion = WebView2Utilities.GetAvailableCoreWebView2BrowserVersionString();

        if (string.IsNullOrWhiteSpace(browserVersion))
            throw new InvalidOperationException(
                "Microsoft Edge WebView2 Runtime was not found. Install the Evergreen Runtime first.");

        var result = global::WebView2.Functions.CreateCoreWebView2EnvironmentWithOptions(
            PWSTR.Null,
            PWSTR.Null,
            null!,
            new global::WebView2.Utilities.CoreWebView2CreateCoreWebView2EnvironmentCompletedHandler(
                (hr, environment) =>
                {

                    completed(hr, environment);
                }));
        result.ThrowOnError();
    }
}
