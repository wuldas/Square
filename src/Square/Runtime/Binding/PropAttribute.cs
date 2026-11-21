namespace Square.Runtime.Binding;

/// <summary>标记 SQV 对象式绑定中可设置的属性。</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class PropAttribute : Attribute
{
    /// <summary>是否必填。</summary>
    public bool Required { get; set; }
    /// <summary>默认值。</summary>
    public object? Default { get; set; }
    /// <summary>编译静态名称的模板值绑定时，使用 CLR setter 接收初始值和后续更新。</summary>
    public bool UseSetter { get; set; }
}