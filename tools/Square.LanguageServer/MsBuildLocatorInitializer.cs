using Microsoft.Build.Locator;

namespace Square.LanguageServer;

internal static class MsBuildLocatorInitializer
{
    private static readonly object Gate = new();
    private static bool _registered;
    private static string _error = string.Empty;

    internal static bool TryRegister(out string error)
    {
        lock (Gate)
        {
            if (_registered || MSBuildLocator.IsRegistered)
            {
                _registered = true;
                error = string.Empty;
                return true;
            }
            if (_error.Length > 0)
            {
                error = _error;
                return false;
            }
            try
            {
                MSBuildLocator.RegisterDefaults();
                _registered = true;
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                _error = exception.Message;
                error = _error;
                return false;
            }
        }
    }
}
