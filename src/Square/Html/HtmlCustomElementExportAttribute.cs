namespace Square.Html;

/// <summary>Exports a browser-style HTML custom element definition from an assembly to templates.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class HtmlCustomElementExportAttribute(string name, Type elementType) : Attribute
{
    public string Name { get; } = name;
    public Type ElementType { get; } = elementType;
    public string? ExtendsTag { get; set; }
    public string[] ObservedAttributes { get; set; } = [];
}
