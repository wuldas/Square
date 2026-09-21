namespace Square.Compiler.LanguageServices;

/// <summary>
/// Metadata describing how a template tag is materialized by the Square emitter.
/// </summary>
public sealed class TemplateComponentDescriptor
{
    public TemplateComponentDescriptor(
        string tagName,
        string typeName,
        string typeMetadataName,
        string namespaceUri,
        string prefix,
        string localName,
        string assemblyName,
        string sourcePath,
        TemplateElementKind kind,
        bool isBuiltIn,
        bool requiresBuildAfterAttach,
        bool isTextContentElement,
        bool isSlotHost = false)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            throw new ArgumentException("A component tag name is required.", nameof(tagName));
        if (string.IsNullOrWhiteSpace(typeName))
            throw new ArgumentException("A component type name is required.", nameof(typeName));
        if (string.IsNullOrWhiteSpace(typeMetadataName))
            throw new ArgumentException("A component metadata name is required.", nameof(typeMetadataName));

        TagName = tagName;
        TypeName = typeName;
        TypeMetadataName = typeMetadataName;
        NamespaceUri = namespaceUri ?? string.Empty;
        Prefix = prefix ?? string.Empty;
        LocalName = localName ?? tagName;
        AssemblyName = assemblyName ?? string.Empty;
        SourcePath = sourcePath ?? string.Empty;
        Kind = kind;
        IsBuiltIn = isBuiltIn;
        RequiresBuildAfterAttach = requiresBuildAfterAttach;
        IsTextContentElement = isTextContentElement;
        IsSlotHost = isSlotHost;
    }

    public string TagName { get; }
    public string TypeName { get; }
    public string TypeMetadataName { get; }
    public string NamespaceUri { get; }
    public string Prefix { get; }
    public string LocalName { get; }
    public string AssemblyName { get; }
    public string SourcePath { get; }
    public TemplateElementKind Kind { get; }
    public bool IsBuiltIn { get; }
    public bool RequiresBuildAfterAttach { get; }
    public bool IsTextContentElement { get; }
    internal bool IsSlotHost { get; }
}

public enum TemplateElementKind
{
    Html,
    Square,
    Svg,
    Extension,
    ClrScoped
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

public sealed class TemplateComponentEventDescriptor
{
    public TemplateComponentEventDescriptor(string memberName, string name, string detailTypeName)
    {
        MemberName = memberName;
        Name = name;
        DetailTypeName = detailTypeName;
        NormalizedName = NormalizeName(name);
        SqxName = ToSqxName(name);
        SqvName = "@" + name;
    }

    public string MemberName { get; }

    public string Name { get; }

    public string DetailTypeName { get; }

    public bool HasDetail => !string.IsNullOrEmpty(DetailTypeName);

    public string NormalizedName { get; }

    public string SqxName { get; }

    public string SqvName { get; }

    private static string ToSqxName(string name)
    {
        var parts = name.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
        return "on" + string.Concat(parts.Select(part =>
            char.ToUpperInvariant(part[0]) + part.Substring(1)));
    }

    private static string NormalizeName(string name) =>
        new string((name ?? string.Empty)
            .Where(character => character != '-')
            .Select(char.ToLowerInvariant)
            .ToArray());
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
