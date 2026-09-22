namespace Square.LanguageServer;

public static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            var host = new LanguageServerHost(Console.OpenStandardInput(), Console.OpenStandardOutput());
            return await host.RunAsync();
        }
        catch (Exception exception)
        {
            // LSP 服务端不得以未处理异常终止（编辑器会静默丢失全部语言功能），记录后以退出码 1 结束。
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
