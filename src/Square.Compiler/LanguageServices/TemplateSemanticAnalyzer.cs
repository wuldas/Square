using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Square.Compiler.Parser;

namespace Square.Compiler.LanguageServices;

/// <summary>
/// Shared semantic metadata extraction for SQX/SQV generator and language tooling.
/// </summary>
public sealed class TemplateSemanticAnalyzer
{
    public IReadOnlyCollection<string> BuildGeneratedTypeNames(
        IEnumerable<(string Path, string Content, string Namespace)> inputs)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            if (!TryParse(input.Content, input.Path, out var document)) continue;

            var namespaceName = string.IsNullOrWhiteSpace(document.Namespace)
                ? input.Namespace
                : document.Namespace;
            names.Add(document.Name);
            if (!string.IsNullOrWhiteSpace(namespaceName))
                names.Add(namespaceName + "." + document.Name);
        }

        return names;
    }

    public IReadOnlyDictionary<string, TemplateComponentDescriptor> BuildGeneratedComponents(
        IEnumerable<(string Path, string Content, string Namespace)> inputs)
    {
        var result = new Dictionary<string, TemplateComponentDescriptor>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            if (!TryParse(input.Content, input.Path, out var document)) continue;

            var namespaceName = string.IsNullOrWhiteSpace(document.Namespace)
                ? input.Namespace
                : document.Namespace;
            var metadataName = string.IsNullOrWhiteSpace(namespaceName)
                ? document.Name
                : namespaceName + "." + document.Name;
            result[metadataName] = new TemplateComponentDescriptor(
                document.Name,
                metadataName,
                false,
                true,
                false);
        }

        return result;
    }

    public IReadOnlyDictionary<string, TemplatePropDescriptor[]> BuildPropContracts(
        Compilation compilation,
        IEnumerable<(string Path, string Content, string Namespace)> inputs)
    {
        var contracts = new Dictionary<string, TemplatePropDescriptor[]>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            if (!TryParse(input.Content, input.Path, out var document)) continue;

            var props = new Dictionary<string, TemplatePropDescriptor>(StringComparer.OrdinalIgnoreCase);
            var script = document.Syntax?.Script;
            if (script != null)
            {
                foreach (var property in script.CSharp.Members.OfType<PropertyDeclarationSyntax>())
                {
                    var attribute = property.AttributeLists
                        .SelectMany(list => list.Attributes)
                        .FirstOrDefault(IsPropAttribute);
                    if (attribute == null) continue;
                    var prop = new TemplatePropDescriptor(
                        property.Identifier.ValueText,
                        property.Type.ToString(),
                        IsRequired(attribute));
                    props[prop.Name] = prop;
                }
            }

            var namespaceName = string.IsNullOrWhiteSpace(document.Namespace)
                ? input.Namespace
                : document.Namespace;
            var metadataName = string.IsNullOrWhiteSpace(namespaceName)
                ? document.Name
                : namespaceName + "." + document.Name;
            var codeBehindType = compilation.GetTypeByMetadataName(metadataName);
            if (codeBehindType != null)
            {
                foreach (var property in codeBehindType.GetMembers().OfType<IPropertySymbol>())
                {
                    var attribute = property.GetAttributes().FirstOrDefault(IsPropAttribute);
                    if (attribute == null) continue;
                    var required = attribute.NamedArguments.Any(argument =>
                        argument.Key == "Required" && argument.Value.Value is true);
                    var prop = new TemplatePropDescriptor(
                        property.Name,
                        property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        required);
                    props[prop.Name] = prop;
                }
            }

            contracts[metadataName] = props.Values.ToArray();
        }

        return contracts;
    }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, TemplateSlotDescriptor>> BuildSlotContracts(
        Compilation compilation)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, TemplateSlotDescriptor>>(StringComparer.Ordinal);
        VisitNamespace(compilation.GlobalNamespace);
        return result;

        void VisitNamespace(INamespaceSymbol namespaceSymbol)
        {
            foreach (var nested in namespaceSymbol.GetNamespaceMembers()) VisitNamespace(nested);
            foreach (var type in namespaceSymbol.GetTypeMembers()) VisitType(type);
        }

        void VisitType(INamedTypeSymbol type)
        {
            var slots = new Dictionary<string, TemplateSlotDescriptor>(StringComparer.Ordinal);
            foreach (var attribute in type.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != "Square.UI.SlotContractAttribute" ||
                    attribute.ConstructorArguments.Length != 2 ||
                    attribute.ConstructorArguments[0].Value is not string name ||
                    attribute.ConstructorArguments[1].Value is not INamedTypeSymbol propsType)
                    continue;

                var properties = propsType.GetMembers().OfType<IPropertySymbol>()
                    .Where(property => !property.IsStatic && property.GetMethod != null)
                    .ToDictionary(
                        property => char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1),
                        property => property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        StringComparer.Ordinal);
                slots[name] = new TemplateSlotDescriptor(name, properties);
            }

            if (slots.Count > 0)
                result[type.ToDisplayString()] = slots;
            foreach (var nested in type.GetTypeMembers()) VisitType(nested);
        }
    }

    private static bool TryParse(string content, string path, out SqxDocument document)
    {
        var result = SquareDocumentService.ParseSyntax(content, path);
        document = result.ParsedSqxDocument;
        return result.IsSuccess && document != null;
    }

    private static bool IsPropAttribute(AttributeData attribute)
    {
        var type = attribute.AttributeClass;
        if (type == null) return false;
        var metadataName = type.ToDisplayString();
        return metadataName == "Square.Runtime.Binding.PropAttribute" ||
            type.Name is "PropAttribute" or "Prop";
    }

    private static bool IsPropAttribute(AttributeSyntax attribute)
    {
        var name = attribute.Name.ToString();
        var separator = name.LastIndexOf('.');
        if (separator >= 0) name = name.Substring(separator + 1);
        if (name.StartsWith("global::", StringComparison.Ordinal))
            name = name.Substring("global::".Length);
        return name is "Prop" or "PropAttribute";
    }

    private static bool IsRequired(AttributeSyntax attribute)
    {
        if (attribute.ArgumentList == null) return false;
        foreach (var argument in attribute.ArgumentList.Arguments)
        {
            if (argument.NameEquals?.Name.Identifier.ValueText == "Required" &&
                argument.Expression.IsKind(SyntaxKind.TrueLiteralExpression))
                return true;
        }
        return false;
    }
}

public sealed class TemplatePropDescriptor
{
    public TemplatePropDescriptor(string name, string typeName, bool required)
    {
        Name = name;
        TypeName = typeName;
        Required = required;
    }

    public string Name { get; }
    public string TypeName { get; }
    public bool Required { get; }
}

public sealed class TemplateSlotDescriptor
{
    public TemplateSlotDescriptor(string name, IReadOnlyDictionary<string, string> properties)
    {
        Name = name;
        Properties = properties;
    }

    public string Name { get; }
    public IReadOnlyDictionary<string, string> Properties { get; }
}
