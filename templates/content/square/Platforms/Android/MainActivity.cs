using Android.App;
using Android.Content.PM;
using Android.Views;
using Square.Hosting;
using Square.Platform.Android;

namespace SafeAppNamespace;

[Activity(
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize |
                           ConfigChanges.ScreenLayout | ConfigChanges.Density,
    WindowSoftInputMode = SoftInput.AdjustResize,
    Theme = "@style/SquareTheme")]
public sealed class MainActivity : SquareActivity
{
    protected override AppWindow CreateSquareWindow() => SquareProgram.CreateWindow();
}
