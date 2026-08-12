using NativeWebView = Square.Extensions.WebView.WebView;
using Square.Extensions.WebView;
using Square.Hosting;

namespace Square.Sample.WebView;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            Console.WriteLine("Square WebView sample started.");
            WebViewRegistration.RegisterDefaults();

            const string html = "<!doctype html><html><head><meta charset='utf-8'><title>Square WebView</title><style>body{font-family:Segoe UI,sans-serif;margin:32px;background:#f5f7fb;color:#172033}h1{color:#155eef}a{color:#0b63ce}</style></head><body><h1>Square.Extensions.WebView</h1><p>This page is rendered by the native WebView2 backend.</p><p><a href='https://example.com'>Open example.com</a></p></body></html>";
            var source = "data:text/html;charset=utf-8," + Uri.EscapeDataString(html);

            var window = new AppWindow("Square Native WebView", 960, 640);
            var browser = new NativeWebView { Source = source };
            browser.NavigationStarting += (_, args) => Console.WriteLine($"Navigation starting: {args.Source}");
            browser.NavigationCompleted += (_, args) =>
                Console.WriteLine($"Navigation completed: {args.Uri} success={args.IsSuccess} error={args.Error}");
            browser.LoadError += (_, args) => Console.Error.WriteLine($"WebView error: {args.Source} {args.Message}");
            window.Load(browser);

            new DesktopApplication(window).Run();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
