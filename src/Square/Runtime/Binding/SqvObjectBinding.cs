using Square.Events;
using Square.Runtime.State;
using Square.UI;

namespace Square.Runtime.Binding;

/// <summary>AOT-safe runtime protocol for Vue object-form property and event bindings.</summary>
public static class SqvObjectBinding
{
    /// <summary>合并模板静态 class 与动态值。</summary>
    public static IDisposable BindClass(Element target, string staticClasses, string? value) =>
        new ClassBinding<string?>(target, staticClasses, value);

    /// <summary>合并模板静态 class 与表达式求值结果。</summary>
    public static IDisposable BindClass<T>(Element target, string staticClasses, Func<T> getter) =>
        new ClassBinding<T>(target, staticClasses, getter());

    /// <summary>合并模板静态 class，并响应动态值更新。</summary>
    public static IDisposable BindClass<T>(Element target, string staticClasses, ObservableValue<T> source) =>
        BindClass(target, staticClasses, (IReactiveValue<T>)source);

    /// <summary>合并模板静态 class，并在元素 Dispatcher 上响应动态值更新。</summary>
    public static IDisposable BindClass<T>(Element target, string staticClasses, IReactiveValue<T> source)
    {
        var binding = new ClassBinding<T>(target, staticClasses, source.Value);
        binding.SetSubscription(source.Subscribe(binding.Apply,
            new ReactiveSubscriptionOptions { Dispatcher = target.Dispatcher }));
        return binding;
    }

    /// <summary>绑定单个动态名称属性。</summary>
    public static IDisposable BindProperty<TName, T>(Element target, TName name, T value) =>
        name is IReactiveValue<string> reactiveName
            ? new DynamicPropertyBinding<T>(target, reactiveName, value)
            : new DynamicPropertyBinding<T>(target, GetStaticName(name), value);

    /// <summary>绑定单个动态名称属性，并响应属性值变化。</summary>
    public static IDisposable BindProperty<TName, T>(Element target, TName name, ObservableValue<T> value) =>
        name is IReactiveValue<string> reactiveName
            ? new DynamicPropertyBinding<T>(target, reactiveName, (IReactiveValue<T>)value)
            : new DynamicPropertyBinding<T>(target, GetStaticName(name), (IReactiveValue<T>)value);

    /// <summary>绑定单个动态名称属性，并响应通用响应式属性值变化。</summary>
    public static IDisposable BindProperty<TName, T>(Element target, TName name, IReactiveValue<T> value) =>
        name is IReactiveValue<string> reactiveName
            ? new DynamicPropertyBinding<T>(target, reactiveName, value)
            : new DynamicPropertyBinding<T>(target, GetStaticName(name), value);

    /// <summary>绑定动态名称事件。</summary>
    public static IDisposable BindEvent<TName>(EventTarget target, TName name, Action<Event> listener) =>
        name is IReactiveValue<string> reactiveName
            ? new DynamicEventBinding(target, reactiveName, listener)
            : new DynamicEventBinding(target, GetStaticName(name), listener);

    /// <summary>绑定动态名称无参数事件。</summary>
    public static IDisposable BindEvent<TName>(EventTarget target, TName name, Action listener) =>
        BindEvent(target, name, _ => listener());

    private static string GetStaticName<TName>(TName name) =>
        name as string ?? throw new ArgumentException("Dynamic SQV argument names must be string or IReactiveValue<string>.", nameof(name));

    /// <summary>用静态字典一次性绑定属性。</summary>
    public static IDisposable BindProperties(
        Element target,
        IReadOnlyDictionary<string, object?> values) =>
        new PropertyMapBinding(target, values);

    public static IDisposable BindProperties<TMap>(Element target, ObservableValue<TMap> source)
        where TMap : IReadOnlyDictionary<string, object?>
    {
        var binding = new PropertyMapBinding(target, source.Value);
        binding.SetSubscription(source.Subscribe(value => binding.Apply(value)));
        return binding;
    }

    public static IDisposable BindProperties<TMap>(Element target, IReactiveValue<TMap> source)
        where TMap : IReadOnlyDictionary<string, object?>
    {
        var binding = new PropertyMapBinding(target, source.Value);
        binding.SetSubscription(source.Subscribe(
            value => binding.Apply(value),
            new ReactiveSubscriptionOptions { Dispatcher = target.Dispatcher }));
        return binding;
    }

    /// <summary>用静态字典绑定事件监听器。</summary>
    public static IDisposable BindEvents(
        EventTarget target,
        IReadOnlyDictionary<string, Action<Event>> listeners) =>
        new EventMapBinding(target, listeners);

    public static IDisposable BindEvents<TMap>(EventTarget target, ObservableValue<TMap> source)
        where TMap : IReadOnlyDictionary<string, Action<Event>>
    {
        var binding = new EventMapBinding(target, source.Value);
        binding.SetSubscription(source.Subscribe(value => binding.Apply(value)));
        return binding;
    }

    public static IDisposable BindEvents<TMap>(EventTarget target, IReactiveValue<TMap> source)
        where TMap : IReadOnlyDictionary<string, Action<Event>>
    {
        var binding = new EventMapBinding(target, source.Value);
        var dispatcher = target is Element element ? element.Dispatcher : null;
        binding.SetSubscription(source.Subscribe(
            value => binding.Apply(value),
            new ReactiveSubscriptionOptions { Dispatcher = dispatcher }));
        return binding;
    }

    private sealed class ClassBinding<T> : IDisposable
    {
        private readonly Element _target;
        private readonly string _staticClasses;
        private IDisposable? _subscription;
        private bool _disposed;

        public ClassBinding(Element target, string staticClasses, T value)
        {
            _target = target;
            _staticClasses = staticClasses;
            Apply(value);
        }

        public void SetSubscription(IDisposable subscription) => _subscription = subscription;

        public void Apply(T value)
        {
            if (_disposed) return;
            var dispatcher = _target.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => Apply(value));
                return;
            }
            var dynamicClasses = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            _target.SetProperty("class", _staticClasses + " " + dynamicClasses);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subscription?.Dispose();
            _subscription = null;
        }
    }

    private sealed class PropertyMapBinding : IDisposable
    {
        private readonly Element _target;
        private readonly HashSet<string> _owned = new(StringComparer.OrdinalIgnoreCase);
        private IDisposable? _subscription;
        private bool _disposed;

        public PropertyMapBinding(Element target, IReadOnlyDictionary<string, object?> values)
        {
            _target = target;
            Apply(values);
        }

        public void SetSubscription(IDisposable subscription) => _subscription = subscription;

        public void Apply(IReadOnlyDictionary<string, object?> values)
        {
            if (_disposed) return;
            ArgumentNullException.ThrowIfNull(values);

            var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in values)
            {
                var name = SqvPropertyNames.Map(pair.Key);
                if (!normalized.TryAdd(name, pair.Value))
                    throw new InvalidOperationException("Duplicate mapped property '" + name + "' in SQV object binding.");
            }

            foreach (var pair in normalized)
            {
                if (pair.Value == null)
                    _target.RemoveProperty(pair.Key);
                else
                    _target.SetProperty<object?>(pair.Key, pair.Value);
            }

            foreach (var name in _owned)
                if (!normalized.ContainsKey(name)) _target.RemoveProperty(name);
            _owned.Clear();
            _owned.UnionWith(normalized.Keys);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subscription?.Dispose();
            _subscription = null;
            foreach (var name in _owned) _target.RemoveProperty(name);
            _owned.Clear();
        }
    }

    private sealed class EventMapBinding : IDisposable
    {
        private readonly EventTarget _target;
        private readonly List<IDisposable> _listeners = [];
        private IDisposable? _subscription;
        private bool _disposed;

        public EventMapBinding(EventTarget target, IReadOnlyDictionary<string, Action<Event>> listeners)
        {
            _target = target;
            Apply(listeners);
        }

        public void SetSubscription(IDisposable subscription) => _subscription = subscription;

        public void Apply(IReadOnlyDictionary<string, Action<Event>> listeners)
        {
            if (_disposed) return;
            ArgumentNullException.ThrowIfNull(listeners);

            var normalized = new Dictionary<string, Action<Event>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in listeners)
            {
                if (pair.Value == null) continue;
                var name = pair.Key.Trim().ToLowerInvariant();
                if (name.Length == 0)
                    throw new InvalidOperationException("SQV event binding names cannot be empty.");
                if (!normalized.TryAdd(name, pair.Value))
                    throw new InvalidOperationException("Duplicate event '" + name + "' in SQV object binding.");
            }

            ClearListeners();
            foreach (var pair in normalized)
                _listeners.Add(_target.Listen(pair.Key, pair.Value));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subscription?.Dispose();
            _subscription = null;
            ClearListeners();
        }

        private void ClearListeners()
        {
            foreach (var listener in _listeners) listener.Dispose();
            _listeners.Clear();
        }
    }

    private sealed class DynamicPropertyBinding<T> : IDisposable
    {
        private readonly Element _target;
        private string? _name;
        private T _value;
        private IDisposable? _nameSubscription;
        private IDisposable? _valueSubscription;
        private bool _disposed;

        public DynamicPropertyBinding(Element target, string name, T value)
        {
            _target = target;
            _value = value;
            SetName(name);
        }

        public DynamicPropertyBinding(Element target, string name, IReactiveValue<T> value) : this(target, name, value.Value)
        {
            _valueSubscription = value.Subscribe(SetValue,
                new ReactiveSubscriptionOptions { Dispatcher = target.Dispatcher });
        }

        public DynamicPropertyBinding(Element target, IReactiveValue<string> name, T value) : this(target, name.Value, value)
        {
            _nameSubscription = name.Subscribe(SetName,
                new ReactiveSubscriptionOptions { Dispatcher = target.Dispatcher });
        }

        public DynamicPropertyBinding(Element target, IReactiveValue<string> name, IReactiveValue<T> value) : this(target, name.Value, value.Value)
        {
            _nameSubscription = name.Subscribe(SetName,
                new ReactiveSubscriptionOptions { Dispatcher = target.Dispatcher });
            _valueSubscription = value.Subscribe(SetValue,
                new ReactiveSubscriptionOptions { Dispatcher = target.Dispatcher });
        }

        private void SetName(string? value)
        {
            if (_disposed) return;
            var mapped = string.IsNullOrWhiteSpace(value) ? null : SqvPropertyNames.Map(value);
            if (string.Equals(_name, mapped, StringComparison.OrdinalIgnoreCase)) return;
            if (_name != null) _target.RemoveProperty(_name);
            _name = mapped;
            Apply();
        }

        private void SetValue(T value)
        {
            if (_disposed) return;
            _value = value;
            Apply();
        }

        private void Apply()
        {
            if (_name == null) return;
            if (_value is null) _target.RemoveProperty(_name);
            else _target.SetProperty(_name, _value);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _nameSubscription?.Dispose();
            _valueSubscription?.Dispose();
            if (_name != null) _target.RemoveProperty(_name);
            _name = null;
        }
    }

    private sealed class DynamicEventBinding : IDisposable
    {
        private readonly EventTarget _target;
        private readonly Action<Event> _listener;
        private IDisposable? _nameSubscription;
        private IDisposable? _registration;
        private string? _name;
        private bool _disposed;

        public DynamicEventBinding(EventTarget target, string name, Action<Event> listener)
        {
            _target = target;
            _listener = listener;
            SetName(name);
        }

        public DynamicEventBinding(EventTarget target, IReactiveValue<string> name, Action<Event> listener) : this(target, name.Value, listener)
        {
            var dispatcher = target is Element element ? element.Dispatcher : null;
            _nameSubscription = name.Subscribe(SetName,
                new ReactiveSubscriptionOptions { Dispatcher = dispatcher });
        }

        private void SetName(string? value)
        {
            if (_disposed) return;
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
            if (string.Equals(_name, normalized, StringComparison.Ordinal)) return;
            _registration?.Dispose();
            _registration = null;
            _name = normalized;
            if (_name != null) _registration = _target.Listen(_name, _listener);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _nameSubscription?.Dispose();
            _registration?.Dispose();
            _nameSubscription = null;
            _registration = null;
            _name = null;
        }
    }
}

/// <summary>Maps Vue/SQV attribute names to Square property names without reflection.</summary>
public static class SqvPropertyNames
{
    /// <summary>将 Vue/SQV 属性名映射为 Square 属性名；未知名称原样返回。</summary>
    public static string Map(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        name = name.Trim();
        return name.ToLowerInvariant() switch
        {
            "text" => "TextContent",
            "value" => "Value",
            "checked" => "IsChecked",
            "disabled" => "IsDisabled",
            "placeholder" => "Placeholder",
            "source" => "Source",
            "image" => "ImageContent",
            "group" => "GroupName",
            "shortcut" => "ShortcutText",
            "checkable" => "IsCheckable",
            "stays-open-on-click" => "StaysOpenOnClick",
            "options" => "Options",
            "items" => "Items",
            "selected-index" => "SelectedIndex",
            "expanded" => "IsExpanded",
            "header" => "Header",
            "loop" => "Loop",
            "to" => "To",
            "href" => "Href",
            "marker" => "Marker",
            "replace" => "Replace",
            "color" => "Color",
            "background" => "Background",
            "underline" => "Underline",
            "type" => "Type",
            "viewbox" => "ViewBox",
            "x" => "X",
            "y" => "Y",
            "width" => "Width",
            "height" => "Height",
            "rx" => "RadiusX",
            "ry" => "RadiusY",
            "cx" => "CenterX",
            "cy" => "CenterY",
            "r" => "Radius",
            "x1" => "X1",
            "y1" => "Y1",
            "x2" => "X2",
            "y2" => "Y2",
            "points" => "Points",
            "d" => "Data",
            "transform" => "Transform",
            "fill" => "Fill",
            "stroke" => "Stroke",
            "stroke-width" => "StrokeWidth",
            "opacity" => "Opacity",
            "fill-opacity" => "FillOpacity",
            "stroke-opacity" => "StrokeOpacity",
            _ => name
        };
    }
}
