using System.Runtime.CompilerServices;
using Square.Controls;
using Square.UI;

namespace Square.Extensions.Routing;

public sealed class RouterView : View
{
    private readonly Dictionary<string, UIElement> _cache = new(StringComparer.Ordinal);
    private Router? _router;
    private UIElement? _active;
    private RouteMatchEntry? _activeEntry;
    private RouteLocation? _activeLocation;
    private string? _activeCacheKey;
    private int _depth;

    public Router Router => ResolveRouter()
        ?? throw new InvalidOperationException("RouterView is not attached to a configured window router.");
    public RouteLocation? Current => ResolveRouter()?.Current;

    public bool TryGetRouter(out Router router)
    {
        router = ResolveRouter()!;
        return router != null;
    }

    public void ConfigureOnce(Action<RouteCollectionBuilder> configure, string initialPath = "/")
    {
        ArgumentNullException.ThrowIfNull(configure);
        var window = AppWindow ?? throw new InvalidOperationException("RouterView must belong to an AppWindow before configuration.");
        _router = RouterRegistry.Get(window) ?? window.UseRouter(configure, initialPath);
    }

    public bool Navigate(string location, bool replace = false) => Router.Navigate(location, replace);
    public bool Replace(string location) => Router.Replace(location);
    public bool Back() => Router.Back();
    public bool Forward() => Router.Forward();

    protected override void OnAttachedCore()
    {
        base.OnAttachedCore();
        var window = AppWindow;
        if (window == null) return;
        _router ??= RouterRegistry.Get(window)
            ?? throw new InvalidOperationException("No router is configured. Call AppWindow.UseRouter(...) or RouterView.ConfigureOnce(...).");
        _depth = GetDepth();
        RouterRegistry.RegisterView(window, this);
        _router.RouteChanged += OnRouteChanged;
        _router.Start();
        if (_router.Current != null) Render(_router.Current, null);
    }

    protected override void OnDetachedCore()
    {
        if (_router != null) _router.RouteChanged -= OnRouteChanged;
        base.OnDetachedCore();
    }

    private int GetDepth()
    {
        var depth = 0;
        for (Element? current = Parent; current != null; current = current.Parent)
            if (current is RouterView) depth++;
        return depth;
    }

    private Router? ResolveRouter()
    {
        if (_router != null) return _router;
        var window = AppWindow;
        return window == null ? null : _router = RouterRegistry.Get(window);
    }

    private void OnRouteChanged(RouteLocation to, RouteLocation? from) => Render(to, from);

    private void Render(RouteLocation location, RouteLocation? previous)
    {
        if (_depth >= location.Matched.Count)
        {
            DeactivateCurrent();
            return;
        }

        var entry = location.Matched[_depth];
        var cacheKey = GetCacheKey(entry, location);
        if (_active != null && ReferenceEquals(_activeEntry?.Definition, entry.Definition) &&
            string.Equals(GetCacheKey(_activeEntry!, _activeLocation!), cacheKey, StringComparison.Ordinal))
        {
            _active.SetProperty(RouteLocation.PropertyName, location);
            if (_active is IRouteAware aware && previous != null) aware.OnRouteUpdated(location, previous);
            _activeLocation = location;
            return;
        }

        DeactivateCurrent();
        UIElement page;
        if (entry.Definition.KeepAlive && _cache.TryGetValue(cacheKey, out var cached))
        {
            page = cached;
            if (HasHotReloadChanges(page) && page is Square.Hosting.ISquareHotReloadComponent hotReloadComponent)
                hotReloadComponent.RebuildAfterHotReload();
        }
        else
        {
            page = entry.Definition.PageFactory();
            page.SetProperty(RouteLocation.PropertyName, location);
            page.BuildElementTree();
            if (entry.Definition.KeepAlive) _cache[cacheKey] = page;
        }

        page.SetProperty(RouteLocation.PropertyName, location);
        Children.Add(page);
        _active = page;
        _activeEntry = entry;
        _activeLocation = location;
        _activeCacheKey = entry.Definition.KeepAlive ? cacheKey : null;
        if (page is IRouteAware routeAware) routeAware.OnRouteActivated(location);
    }

    private static bool HasHotReloadChanges(Element element)
    {
        if (element is Square.Hosting.ISquareHotReloadComponent { HasHotReloadChanges: true }) return true;
        foreach (var child in element.Children)
            if (HasHotReloadChanges(child)) return true;
        return false;
    }

    private void DeactivateCurrent()
    {
        if (_active == null || _activeEntry == null || _activeLocation == null) return;
        if (_active is IRouteAware aware) aware.OnRouteDeactivated(_activeLocation);
        if (_active.Parent == this) Children.Remove(_active);
        if (_activeCacheKey == null || !_cache.TryGetValue(_activeCacheKey, out var cached) || !ReferenceEquals(cached, _active))
            _active.DiscardGeneratedSubtree();
        _active = null;
        _activeEntry = null;
        _activeLocation = null;
        _activeCacheKey = null;
    }

    private static string GetCacheKey(RouteMatchEntry entry, RouteLocation location)
    {
        var custom = entry.Definition.CacheKeySelector?.Invoke(location);
        return RuntimeHelpers.GetHashCode(entry.Definition) + ":" + (custom ?? entry.MatchedPath);
    }

    public void RemoveCache(string matchedPath)
    {
        foreach (var pair in _cache.Where(pair => pair.Key.EndsWith(":" + matchedPath, StringComparison.Ordinal)).ToArray())
        {
            if (!ReferenceEquals(pair.Value, _active)) pair.Value.DiscardGeneratedSubtree();
            _cache.Remove(pair.Key);
        }
    }

    public void ClearCache()
    {
        foreach (var page in _cache.Values.Distinct())
            if (!ReferenceEquals(page, _active)) page.DiscardGeneratedSubtree();
        _cache.Clear();
    }

    internal void Shutdown()
    {
        var active = _active;
        if (active?.Parent == this) Children.Remove(active);
        foreach (var page in _cache.Values.Append(active).Where(page => page != null).Distinct())
            page!.DiscardGeneratedSubtree();
        _cache.Clear();
        _active = null;
        _activeEntry = null;
        _activeLocation = null;
        _activeCacheKey = null;
    }
}

public sealed class RouterLink : Square.Controls.Link
{
    public string To
    {
        get => GetProperty<string>(nameof(To)) ?? Href;
        set { SetProperty(nameof(To), value); Href = value; }
    }

    public bool Replace
    {
        get => GetProperty<bool>(nameof(Replace));
        set => SetProperty(nameof(Replace), value);
    }

    protected override void Activate()
    {
        var window = AppWindow;
        var router = window == null ? null : RouterRegistry.Get(window);
        if (router != null && !string.IsNullOrWhiteSpace(To)) router.Navigate(To, Replace);
    }
}
