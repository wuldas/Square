namespace Square.UI;

/// <summary>Declares application precedence for template element namespaces.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ElementNamespaceOrderAttribute : Attribute
{
    public ElementNamespaceOrderAttribute(params string[] namespaceUris)
    {
        NamespaceUris = namespaceUris;
    }

    public string[] NamespaceUris { get; }
}
