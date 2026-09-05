using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Square.Hosting;
using Square.Platform.Android;

namespace Square.Sample.Android;

[Activity(
    Label = "Square Android Sample",
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
        return window;
    }
}
