using Square.UI;
using Square.UI.ElementApi;

namespace Square.CSS.Engine;

public static class CssStyleReconciler
{
    private static readonly object Gate = new();
    private static readonly object ApplyGate = new();
    private static readonly List<StyleScope> Scopes = [];
    private static readonly HashSet<Element> DirtyElements = [];
    [ThreadStatic]
    private static int _applying;

    static CssStyleReconciler()
    {
        Element.StyleInvalidated += MarkDirty;
    }

    public static bool HasWork
    {
        get { lock (Gate) return DirtyElements.Count > 0; }
    }

    internal static bool HasWorkForTree(Element root)
    {
        ArgumentNullException.ThrowIfNull(root);
        lock (Gate)
            return DirtyElements.Any(element => ReferenceEquals(FindTreeRoot(element), root));
    }

    internal static void RegisterScope(CssEngine engine, Element root)
    {
        _ = HasWork; // Ensures the static constructor subscribed to Element.StyleInvalidated.
        lock (Gate)
        {
            foreach (var scope in Scopes)
            {
                if (ReferenceEquals(scope.Engine, engine) && ReferenceEquals(scope.Root, root))
                    return;
            }
            Scopes.Add(new StyleScope(engine, root));
        }
    }

    internal static void ApplyScope(CssEngine engine, Element root)
    {
        lock (ApplyGate)
        {
            RegisterScope(engine, root);
            engine.ApplyStylesToTreeCore(root);
            foreach (var changed in CssEngine.FinalizePseudoElements(root))
                changed.Invalidate(ElementInvalidation.Layout | ElementInvalidation.DisplayTree | ElementInvalidation.HitTest);
            RefreshAnimations(engine, root);
        }
    }

    /// <summary>刷新所有脏元素的样式，重新应用级联样式并推进动画。</summary>
    public static void Flush()
    {
        FlushCore(root: null);
    }

    internal static void Flush(Element root)
    {
        ArgumentNullException.ThrowIfNull(root);
        FlushCore(root);
    }

    private static void FlushCore(Element? root)
    {
        lock (ApplyGate)
        {
            StyleScope[] scopes;
            Element[] dirtyElements;
            lock (Gate)
            {
                if (DirtyElements.Count == 0) return;
                if (root == null)
                {
                    dirtyElements = DirtyElements.ToArray();
                    DirtyElements.Clear();
                }
                else
                {
                    dirtyElements = DirtyElements
                        .Where(element => ReferenceEquals(FindTreeRoot(element), root))
                        .ToArray();
                    if (dirtyElements.Length == 0) return;
                    foreach (var element in dirtyElements)
                        DirtyElements.Remove(element);
                }
                var candidateScopes = Scopes
                    .Where(scope => dirtyElements.Any(element => AreInSameStyleBranch(scope.Root, element)))
                    .ToArray();
                var expandToParent = candidateScopes.Any(scope => scope.Engine.HasSiblingCombinators);
                var styleRoots = MinimizeRoots(dirtyElements
                    .Select(element => expandToParent && element.Parent != null ? element.Parent : element));
                scopes = Scopes
                    .Where(scope => styleRoots.Any(root => AreInSameStyleBranch(scope.Root, root)))
                    .ToArray();
            }
            if (scopes.Length == 0) return;

            _applying++;
            try
            {
                var expandToParent = scopes.Any(scope => scope.Engine.HasSiblingCombinators);
                var styleRoots = MinimizeRoots(dirtyElements
                    .Select(element => expandToParent && element.Parent != null ? element.Parent : element));
                var styleSnapshots = styleRoots.Select(CaptureStyleSnapshot).ToArray();
                var pseudoElementChanges = new HashSet<Element>();

                using (Element.SuppressInvalidation())
                {
                    foreach (var styleRoot in styleRoots)
                    {
                        styleRoot.Style.ClearComputedStylesRecursive();
                        ClearCascadedSubtree(styleRoot);
                    }

                    foreach (var styleRoot in styleRoots)
                    {
                        foreach (var scope in scopes)
                        {
                            var target = IsAncestorOrSelf(scope.Root, styleRoot) ? styleRoot : scope.Root;
                            if (AreInSameStyleBranch(target, styleRoot))
                                scope.Engine.ApplyStylesToTreeCore(target);
                        }
                    }
                    foreach (var scopeRoot in MinimizeRoots(scopes.Select(scope => scope.Root)))
                        pseudoElementChanges.UnionWith(CssEngine.FinalizePseudoElements(scopeRoot));
                    foreach (var scope in scopes)
                        scope.Animations.Attach(scope.Root);
                }

                foreach (var snapshot in styleSnapshots)
                    ApplyStyleDifferences(snapshot);
                foreach (var changed in pseudoElementChanges)
                    changed.Invalidate(ElementInvalidation.Layout | ElementInvalidation.DisplayTree | ElementInvalidation.HitTest);
            }
            finally
            {
                _applying--;
            }
        }
    }

    internal static void RefreshAnimations(CssEngine engine, Element root)
    {
        lock (Gate)
        {
            foreach (var scope in Scopes)
            {
                if (!ReferenceEquals(scope.Engine, engine) || !ReferenceEquals(scope.Root, root)) continue;
                scope.Animations.Attach(root);
                return;
            }
        }
    }

    internal static void ReapplyScopesToTree(Element root)
    {
        lock (ApplyGate)
        {
            StyleScope[] scopes;
            lock (Gate)
                scopes = Scopes.Where(scope => ReferenceEquals(FindTreeRoot(scope.Root), root)).ToArray();
            if (scopes.Length == 0) return;

            IReadOnlyCollection<Element> pseudoElementChanges;
            using (Element.SuppressInvalidation())
            {
                root.Style.ClearComputedStylesRecursive();
                ClearCascadedSubtree(root);
                foreach (var scope in scopes)
                {
                    scope.Engine.ApplyStylesToTreeCore(scope.Root);
                    scope.Animations.Attach(scope.Root);
                }
                pseudoElementChanges = CssEngine.FinalizePseudoElements(root);
            }
            foreach (var changed in pseudoElementChanges)
                changed.Invalidate(ElementInvalidation.Layout | ElementInvalidation.DisplayTree | ElementInvalidation.HitTest);
        }
    }

    internal static void InvalidateScopes(CssEngine engine)
    {
        Element[] roots;
        lock (Gate)
            roots = Scopes.Where(scope => ReferenceEquals(scope.Engine, engine))
                .Select(scope => scope.Root)
                .Distinct()
                .ToArray();
        foreach (var root in roots)
            root.Invalidate(ElementInvalidation.Style);
    }

    internal static IReadOnlyList<CssInspectionRule> GetMatchedRules(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        StyleScope[] scopes;
        lock (Gate)
            scopes = Scopes.Where(scope => IsAncestorOrSelf(scope.Root, element)).ToArray();

        return scopes
            .SelectMany(scope => scope.Engine.GetMatchedRules(element))
            .ToArray();
    }

    /// <summary>释放与指定元素树关联的 CSS scope 和待处理样式失效。</summary>
    public static void UnregisterScopesForTree(Element root)
    {
        ArgumentNullException.ThrowIfNull(root);
        lock (Gate)
        {
            Scopes.RemoveAll(scope => ReferenceEquals(FindTreeRoot(scope.Root), root));
            DirtyElements.RemoveWhere(element => ReferenceEquals(FindTreeRoot(element), root));
        }
    }

    /// <summary>Advances animations owned by CSS scopes in the supplied element tree.</summary>
    public static bool TickAnimations(Element root, float deltaSeconds)
    {
        StyleScope[] scopes;
        lock (Gate)
            scopes = Scopes.Where(scope => ReferenceEquals(FindTreeRoot(scope.Root), root)).ToArray();

        var running = false;
        foreach (var scope in scopes)
        {
            if (!scope.Animations.HasRunningAnimations) continue;
            running = true; // The final tick still needs a frame to present its terminal value.
            scope.Animations.Tick(deltaSeconds);
        }
        return running;
    }

    private static void MarkDirty(Element element)
    {
        if (_applying > 0) return;
        lock (Gate)
            DirtyElements.Add(element);
    }

    private static void ClearCascadedSubtree(Element element)
    {
        element.Style.ClearCascaded();
        foreach (var child in element.Children.ToArray())
            if (ReferenceEquals(child.Parent, element)) ClearCascadedSubtree(child);
    }

    private static Element FindTreeRoot(Element element)
    {
        while (element.Parent != null)
            element = element.Parent;
        return element;
    }

    private static bool AreInSameStyleBranch(Element scopeRoot, Element dirtyElement) =>
        IsAncestorOrSelf(scopeRoot, dirtyElement) || IsAncestorOrSelf(dirtyElement, scopeRoot);

    private static bool IsAncestorOrSelf(Element ancestor, Element element)
    {
        for (var current = element; current != null; current = current.Parent)
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }

    private static Element[] MinimizeRoots(IEnumerable<Element> elements)
    {
        var roots = elements.Distinct().ToList();
        roots.RemoveAll(candidate => roots.Any(other =>
            !ReferenceEquals(candidate, other) && IsAncestorOrSelf(other, candidate)));
        return roots.ToArray();
    }

    private static StyleSnapshot CaptureStyleSnapshot(Element element)
    {
        var properties = element.Style.GetAll();
        var children = element.Children.Select(CaptureStyleSnapshot).ToArray();
        return new StyleSnapshot(element, properties, children);
    }

    private static void ApplyStyleDifferences(StyleSnapshot snapshot)
    {
        foreach (var child in snapshot.Children)
            ApplyStyleDifferences(child);

        var current = snapshot.Element.Style.GetAll()
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var invalidation = ElementInvalidation.None;
        foreach (var property in snapshot.Properties.Keys.Concat(current.Keys).Distinct(StringComparer.Ordinal))
        {
            snapshot.Properties.TryGetValue(property, out var previousValue);
            current.TryGetValue(property, out var currentValue);
            if (!string.Equals(previousValue, currentValue, StringComparison.Ordinal))
                invalidation |= StyleInvalidation.ForProperty(property);
        }

        if (invalidation != ElementInvalidation.None)
            snapshot.Element.Invalidate(invalidation);
    }

    private sealed record StyleSnapshot(
        Element Element,
        IReadOnlyDictionary<string, string> Properties,
        IReadOnlyList<StyleSnapshot> Children);

    private sealed class StyleScope
    {
        public StyleScope(CssEngine engine, Element root)
        {
            Engine = engine;
            Root = root;
            Animations = new CssAnimationManager(engine);
        }

        public CssEngine Engine { get; }
        public Element Root { get; }
        public CssAnimationManager Animations { get; }
    }
}
