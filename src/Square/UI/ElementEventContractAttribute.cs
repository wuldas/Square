namespace Square.UI;

/// <summary>Persists a component event name in assembly metadata.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ElementEventContractAttribute : Attribute
{
    public ElementEventContractAttribute(Type elementType, string memberName, string eventName)
    {
        ElementType = elementType;
        MemberName = memberName;
        EventName = eventName;
    }

    public Type ElementType { get; }
    public string MemberName { get; }
    public string EventName { get; }
}
