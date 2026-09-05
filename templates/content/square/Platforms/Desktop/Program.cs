using Square.Hosting;

namespace SafeAppNamespace;

public static class Program
{
    [STAThread]
    public static void Main() => new DesktopApplication(SquareProgram.CreateWindow()).Run();
}
