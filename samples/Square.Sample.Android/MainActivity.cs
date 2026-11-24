using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using Square.DevTools;
using Square.Hosting;
using Square.Platform.Android;

namespace Square.Sample.Android;

[Activity(
    Label = "Square Android Sample",
    Name = "com.wuldas.square.sample.MainActivity",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize |
                           ConfigChanges.ScreenLayout | ConfigChanges.Density,
    WindowSoftInputMode = SoftInput.AdjustResize,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
public sealed class MainActivity : SquareActivity
{
    protected override AppWindow CreateSquareWindow()
    {
        var window = new AppWindow("Square Android Sample", 480, 800);
        var requestedBackend = Intent?.GetStringExtra("backend");
        if (!string.IsNullOrWhiteSpace(requestedBackend))
            window.RenderBackend = requestedBackend;
        window.Load(new MainPage());
        StartDevTools(window);
        return window;
    }

    // adb shell am start -n com.wuldas.square.sample/.MainActivity --ez devtools true --ei devtools-port 5128
    private void StartDevTools(AppWindow window)
    {
        if (Intent?.GetBooleanExtra("devtools", false) != true) return;
        var devTools = window.UseDevToolsServer(new DevToolsOptions
        {
            Port = Intent.GetIntExtra("devtools-port", 0),
            AllowInputInjection = true,
            AllowInspector = true,
            IncludeTextContent = true
        });
        if (devTools == null) return;
        Log.Info("Square", $"DevTools {devTools.BaseAddress}/api/v1");
        Log.Info("Square", $"{DevToolsServer.TokenHeader}: {devTools.AccessToken}");
    }
}
