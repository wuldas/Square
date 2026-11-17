namespace Square.DevTools;

public sealed class DevToolsOptions
{
    public int Port { get; set; }
    public string? AccessToken { get; set; }
    /// <summary>
    /// true 时 <see cref="DevToolsServer.TryStart"/> 的使用方必须自行处理启动失败；
    /// 默认 false，<see cref="DevToolsApplicationExtensions.UseDevToolsServer"/> 在监听
    /// 无法绑定时记录警告并返回 null，不阻断应用启动。
    /// </summary>
    public bool ThrowOnStartFailure { get; set; }
    public bool AllowInputInjection { get; set; }
    public bool AllowInspector { get; set; }
    public bool AllowMemoryDiagnostics { get; set; }
    public bool AllowChromeInspect { get; set; }
    public bool AllowChromeStyleEditing { get; set; }
    public bool AllowChromeConsole { get; set; }
    public bool IncludeSourcePaths { get; set; }
    public bool IncludeTextContent { get; set; }
}
