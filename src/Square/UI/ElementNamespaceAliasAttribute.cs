namespace Square.UI;

/// <summary>Overrides the template prefix assigned to an exported namespace.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ElementNamespaceAliasAttribute : Attribute
{
    public ElementNamespaceAliasAttribute(string namespaceUri, string prefix)
    {
        NamespaceUri = namespaceUri;
        Prefix = prefix;
    }

    public string NamespaceUri { get; }
    public string Prefix { get; }
}
