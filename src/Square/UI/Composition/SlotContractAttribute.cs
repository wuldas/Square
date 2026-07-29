namespace Square.UI;

/// <summary>声明组件具名插槽及其强类型属性契约。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class SlotContractAttribute : Attribute
{
    public SlotContractAttribute(string name, Type propsType)
    {
        Name = name ?? "";
        PropsType = propsType ?? throw new ArgumentNullException(nameof(propsType));
    }

    public string Name { get; }
    public Type PropsType { get; }
}
