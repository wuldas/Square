using System.ComponentModel;

namespace Square.Hosting;

/// <summary>Dispatches CLR metadata updates to active Square desktop applications.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SquareHotReloadHandler
{
    private static readonly object Gate = new();
    private static readonly List<WeakReference<DesktopApplication>> Applications = [];

    internal static void Register(DesktopApplication application)
    {
        lock (Gate)
        {
            RemoveDeadApplications();
            if (Applications.Any(reference =>
                    reference.TryGetTarget(out var existing) && ReferenceEquals(existing, application)))
                return;
            Applications.Add(new WeakReference<DesktopApplication>(application));
        }
    }

    internal static void Unregister(DesktopApplication application)
    {
        lock (Gate)
            Applications.RemoveAll(reference =>
                !reference.TryGetTarget(out var existing) || ReferenceEquals(existing, application));
    }

    /// <summary>Called by the CLR before updated metadata is applied.</summary>
    public static void ClearCache(Type[]? updatedTypes)
    {
    }

    /// <summary>Called by the CLR after updated metadata is applied.</summary>
    public static void UpdateApplication(Type[]? updatedTypes)
    {
        DesktopApplication[] applications;
        lock (Gate)
        {
            RemoveDeadApplications();
            applications = Applications
                .Select(reference => reference.TryGetTarget(out var application) ? application : null)
                .Where(static application => application != null)
                .Cast<DesktopApplication>()
                .ToArray();
        }

        foreach (var application in applications)
            application.QueueHotReloadUpdate();
    }

    private static void RemoveDeadApplications() =>
        Applications.RemoveAll(static reference => !reference.TryGetTarget(out _));
}
