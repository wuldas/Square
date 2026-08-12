namespace Square.LanguageServer;

public static class Program
{
    public static async Task<int> Main()
    {
        var host = new LanguageServerHost(Console.OpenStandardInput(), Console.OpenStandardOutput());
        return await host.RunAsync();
    }
}
