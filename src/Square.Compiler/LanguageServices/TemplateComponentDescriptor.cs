namespace Square.Compiler.LanguageServices;

/// <summary>
/// Metadata describing how a template tag is materialized by the Square emitter.
/// </summary>
public sealed class TemplateComponentDescriptor
{
    public TemplateComponentDescriptor(
        string tagName,
        string typeName,
        bool isBuiltIn,
        bool requiresBuildAfterAttach,
        bool isTextContentElement)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            throw new ArgumentException("A component tag name is required.", nameof(tagName));
        if (string.IsNullOrWhiteSpace(typeName))
            throw new ArgumentException("A component type name is required.", nameof(typeName));

        TagName = tagName;
        TypeName = typeName;
        IsBuiltIn = isBuiltIn;
        RequiresBuildAfterAttach = requiresBuildAfterAttach;
        IsTextContentElement = isTextContentElement;
    }

    public string TagName { get; }

    public string TypeName { get; }

    public bool IsBuiltIn { get; }

    public bool RequiresBuildAfterAttach { get; }

    public bool IsTextContentElement { get; }
}

public sealed class TemplateEventDescriptor
{
    public TemplateEventDescriptor(string name, string canonicalName)
    {
        Name = name;
        CanonicalName = canonicalName;
    }

    public string Name { get; }

    public string CanonicalName { get; }
}

public sealed class TemplatePropertyDescriptor
{
    public TemplatePropertyDescriptor(
        string name,
        string canonicalName,
        TemplatePropertyValueKind valueKind = TemplatePropertyValueKind.String)
    {
        Name = name;
        CanonicalName = canonicalName;
        ValueKind = valueKind;
    }

    public string Name { get; }

    public string CanonicalName { get; }

    public TemplatePropertyValueKind ValueKind { get; }
}

public enum TemplatePropertyValueKind
{
    String,
    Boolean,
    CssClass
}
