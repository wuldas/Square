namespace Square.UI;

/// <summary>Declares a template element exported by an assembly.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ElementExportAttribute : Attribute
{
    public ElementExportAttribute(string namespaceUri, string localName, Type elementType)
    {
        NamespaceUri = namespaceUri;
        LocalName = localName;
        ElementType = elementType;
    }

    public string NamespaceUri { get; }
    public string LocalName { get; }
    public Type ElementType { get; }
}
