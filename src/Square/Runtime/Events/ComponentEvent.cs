namespace Square.Events;

/// <summary>无载荷的组件自定义事件契约。</summary>
public sealed class ComponentEvent
{
    /// <summary>使用规范事件名创建组件事件契约。</summary>
    public ComponentEvent(string name)
    {
        Name = ComponentEventName.Normalize(name);
    }

    /// <summary>运行时事件名。</summary>
    public string Name { get; }

    internal Event CreateEvent()
    {
        var componentEvent = new Event(Name);
        componentEvent.SetTargetOnly();
        return componentEvent;
    }
}

/// <summary>带强类型载荷的组件自定义事件契约。</summary>
public sealed class ComponentEvent<TDetail>
{
    /// <summary>使用规范事件名创建组件事件契约。</summary>
    public ComponentEvent(string name)
    {
        Name = ComponentEventName.Normalize(name);
    }

    /// <summary>运行时事件名。</summary>
    public string Name { get; }

    internal CustomEvent<TDetail> CreateEvent(TDetail detail)
    {
        var componentEvent = new CustomEvent<TDetail>(Name, detail);
        componentEvent.SetTargetOnly();
        return componentEvent;
    }
}

/// <summary>携带组件自定义事件载荷的事件对象。</summary>
public sealed class CustomEvent<TDetail> : Event
{
    /// <summary>创建自定义事件。</summary>
    public CustomEvent(string type, TDetail detail, EventInit? init = null)
        : base(type, init)
    {
        Detail = detail;
    }

    /// <summary>组件派发的强类型载荷。</summary>
    public TDetail Detail { get; }
}

internal static class ComponentEventName
{
    public static string Normalize(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        name = name.Trim();
        if (!IsValid(name))
            throw new ArgumentException(
                "Component event names must use lowercase kebab-case.", nameof(name));
        return name;
    }

    private static bool IsValid(string name)
    {
        var segmentStart = true;
        foreach (var character in name)
        {
            if (character == '-')
            {
                if (segmentStart) return false;
                segmentStart = true;
                continue;
            }

            if (segmentStart)
            {
                if (character is < 'a' or > 'z') return false;
                segmentStart = false;
                continue;
            }

            if (character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9'))
                return false;
        }

        return !segmentStart;
    }
}
