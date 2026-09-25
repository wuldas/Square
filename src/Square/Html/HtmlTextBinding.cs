using Square.Runtime.State;
using Square.UI;

namespace Square.Html;

/// <summary>
/// Reactive bindings for DOM <see cref="Square.UI.Text"/> nodes under HTML hosts. Square text controls bind
/// through <c>Element.BindProperty</c>; DOM Text has no property store, so the bound source writes
/// <see cref="CharacterData.Data"/> directly. The owner element keeps the returned subscriptions as
/// generated resources, so discarding the generated subtree releases them.
/// </summary>
public static class HtmlTextBinding
{
    /// <summary>Subscribes to a reactive value and mirrors it into the text node's data.</summary>
    public static IDisposable Bind<T>(Element owner, Square.UI.Text node, IReactiveValue<T> source)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(source);
        node.Data = ValueText(source.Value);
        var subscription = source.Subscribe(
            value => node.Data = ValueText(value),
            new ReactiveSubscriptionOptions { Dispatcher = owner.Dispatcher });
        owner.RegisterGeneratedResource(subscription);
        return subscription;
    }

    /// <summary>Evaluates a getter now and on every source change, mirroring the result into the text node.</summary>
    public static IDisposable Bind<T>(Element owner, Square.UI.Text node, Func<T> getter, params IReactiveSource[] sources)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(getter);
        node.Data = ValueText(getter());
        var subscriptions = new List<IDisposable>();
        foreach (var source in sources.Distinct())
            subscriptions.Add(source.SubscribeChanged(
                () => node.Data = ValueText(getter()),
                new ReactiveSubscriptionOptions { Dispatcher = owner.Dispatcher }));
        var composite = new CompositeSubscription(subscriptions);
        owner.RegisterGeneratedResource(composite);
        return composite;
    }

    /// <summary>Text conversion matches the interpolated-string bindings emitted for Square controls.</summary>
    private static string ValueText<T>(T value) => value?.ToString() ?? "";

    private sealed class CompositeSubscription(IReadOnlyList<IDisposable> subscriptions) : IDisposable
    {
        public void Dispose()
        {
            foreach (var subscription in subscriptions) subscription.Dispose();
        }
    }
}
