using Square.Hosting;

namespace Square.DevTools;

public static class DevToolsApplicationExtensions
{
    /// <summary>
    /// 启动 DevTools 服务。loopback 监听不可用时记录警告并返回 null，让应用继续运行；
    /// 设置 <see cref="DevToolsOptions.ThrowOnStartFailure"/> 可改为抛出异常。
    /// 服务所有权跟随窗口 <c>Closed</c>，不在这里注册第二次释放。
    /// </summary>
    public static DevToolsServer? UseDevToolsServer(this AppWindow window, DevToolsOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        options ??= new DevToolsOptions();
        if (DevToolsServer.TryStart(window, options, out var server, out var error))
            return server;
        if (options.ThrowOnStartFailure)
            throw new InvalidOperationException(error);
        Console.Error.WriteLine($"Square DevTools: {error}. The application continues without the DevTools server.");
        return null;
    }
}
