namespace Square.DevTools;

public sealed class DevToolsOptions
{
    public int Port { get; set; }
    public string? AccessToken { get; set; }
    public bool AllowInputInjection { get; set; }
    public bool AllowInspector { get; set; }
    public bool AllowMemoryDiagnostics { get; set; }
    public bool AllowChromeInspect { get; set; }
    public bool IncludeSourcePaths { get; set; }
    public bool IncludeTextContent { get; set; }
}
