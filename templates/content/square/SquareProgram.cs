using System.Reflection;
using Square.Hosting;

namespace SafeAppNamespace;

public static class SquareProgram
{
    public static AppWindow CreateWindow()
    {
        var title = typeof(SquareProgram).Assembly.GetCustomAttribute<AssemblyTitleAttribute>()!.Title;
        var window = new AppWindow(title, 800, 600);
        window.Load(new App());
        return window;
    }
}
