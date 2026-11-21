using Square.Runtime.State;

namespace Square.Runtime.Binding;

/// <summary>Resolves the value type of a generated CLR setter binding.</summary>
public static class PropBinding
{
    /// <summary>Use a single value.</summary>
    public static PropBinding<T> From<T>(T value) => new(value, null, null);

    /// <summary>Evaluate a getter once.</summary>
    public static PropBinding<T> From<T>(Func<T> getter) => new(default!, getter, null);

    /// <summary>Observe a value and its changes.</summary>
    public static PropBinding<T> From<T>(ObservableValue<T> source) => From((IReactiveValue<T>)source);

    /// <summary>Observe a value and its changes.</summary>
    public static PropBinding<T> From<T>(IReactiveValue<T> source) => new(default!, null, source);
}

/// <summary>A value, getter or reactive source selected for a compile-time CLR setter.</summary>
public readonly struct PropBinding<T>
{
    private readonly T _value;
    private readonly Func<T>? _getter;

    internal PropBinding(T value, Func<T>? getter, IReactiveValue<T>? source)
    {
        _value = value;
        _getter = getter;
        Source = source;
    }

    internal IReactiveValue<T>? Source { get; }
    internal bool IsBound => Source != null || _getter != null;
    internal T GetValue() => Source != null ? Source.Value : _getter != null ? _getter() : _value;
}
