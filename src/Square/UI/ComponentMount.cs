using Square.UI.ElementApi;

namespace Square.UI;

/// <summary>挂载编译后的组件，替换容器内容并拥有组件的永久生命周期。</summary>
public static class ComponentMountExtensions
{
    /// <summary>在所属文档线程挂载组件，保留容器本身及其样式。</summary>
    public static ComponentMount Mount(this Element container, UIElement component)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(component);
        if (container.OwnerDocument is not UIDocument document)
            throw new InvalidOperationException("The mount target is not owned by a UIDocument.");
        document.Context.Dispatcher.VerifyAccess();
        if (document.AppWindow?.IsClosed == true)
            throw new InvalidOperationException("The window is closed.");
        if (container.IsGeneratedSubtreeDiscarded)
            throw new InvalidOperationException("The mount target has been discarded.");
        if (component.IsGeneratedSubtreeDiscarded)
            throw new InvalidOperationException("The component has been discarded.");
        for (Element? ancestor = container; ancestor != null; ancestor = ancestor.Parent)
            if (ReferenceEquals(ancestor, component))
                throw new InvalidOperationException("Cannot mount a component into itself or its subtree.");
        if (component.ParentNode != null || component.ComponentMountOwner != null)
            throw new InvalidOperationException("The component is already mounted.");
        if (component.OwnerDocument != null && !ReferenceEquals(component.OwnerDocument, document))
            throw new InvalidOperationException("The component belongs to a different document.");

        document.AssignOwnerDocument(component);
        try
        {
            component.BuildElementTree();
            var previous = container.Children.ToArray();
            container.ChildNodes.Clear();
            foreach (var child in previous)
                child.DiscardGeneratedSubtree();
            var mount = new ComponentMount(container, component);
            component.ComponentMountOwner = mount;
            container.ChildNodes.Add(component);
            return mount;
        }
        catch
        {
            try
            {
                if (ReferenceEquals(component.ParentNode, container))
                    container.ChildNodes.Remove(component);
            }
            finally
            {
                component.DiscardGeneratedSubtree();
            }
            throw;
        }
    }

    /// <summary>按现有 CSS selector 选择容器，未找到时不改动原树。</summary>
    public static ComponentMount Mount(this Element root, string selector, UIElement component)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(component);
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        (root.OwnerDocument as UIDocument)?.Context.Dispatcher.VerifyAccess();
        var container = CssSelector.QuerySelector(root, selector, includeRoot: true)
            ?? throw new InvalidOperationException($"Mount target was not found: '{selector}'.");
        return container.Mount(component);
    }
}

/// <summary>一个组件的挂载句柄；卸载后须使用新组件实例重新挂载。</summary>
public sealed class ComponentMount : IDisposable
{
    private readonly UIDocument _document;
    private Element? _container;

    internal ComponentMount(Element container, UIElement component)
    {
        _document = (UIDocument)container.OwnerDocument!;
        _container = container;
        Component = component;
    }

    /// <summary>此句柄拥有的组件实例。</summary>
    public UIElement Component { get; }

    /// <summary>组件尚未销毁且仍为原容器的直接孩子。</summary>
    public bool IsMounted => _container != null && ReferenceEquals(Component.ParentNode, _container);

    /// <summary>移除并永久释放组件的生成资源与绑定；重复调用无效果。</summary>
    public void Unmount()
    {
        if (_container == null) return;
        _document.Context.Dispatcher.VerifyAccess();
        if (Component.ParentNode != null && !ReferenceEquals(Component.ParentNode, _container))
            throw new InvalidOperationException("The mounted component was moved to another container.");
        if (ReferenceEquals(Component.ParentNode, _container))
            _container.ChildNodes.Remove(Component);
        Component.DiscardGeneratedSubtree();
    }

    /// <inheritdoc />
    public void Dispose() => Unmount();

    internal void MarkDiscarded()
    {
        _container = null;
        Component.ComponentMountOwner = null;
    }
}
