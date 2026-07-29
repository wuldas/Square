using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Square.Compiler.Directives;

internal sealed class DirectiveDescriptor
{
    public string TagName { get; set; } = "";
    public ImmutableArray<string> Aliases { get; set; } = ImmutableArray<string>.Empty;
    public string ParentTag { get; set; }
    public ImmutableArray<string> AllowedChildTags { get; set; } = ImmutableArray<string>.Empty;
    public bool SkipStandaloneEmit { get; set; }
    public string Pattern { get; set; } = "ControlFlowAttach";
    public string RuntimeTypeName { get; set; }
    public string FieldPrefix { get; set; } = "_dir";
    public string PrimaryAttribute { get; set; }
}

internal sealed class DirectiveCatalog
{
    private readonly Dictionary<string, DirectiveDescriptor> _byTag =
        new Dictionary<string, DirectiveDescriptor>(StringComparer.OrdinalIgnoreCase);

    public static DirectiveCatalog BuiltIn { get; } = CreateBuiltIn();

    public IEnumerable<DirectiveDescriptor> Descriptors => _byTag.Values.Distinct();

    public bool IsDirective(string tagName) =>
        !string.IsNullOrEmpty(tagName) && _byTag.ContainsKey(tagName);

    public bool TryGet(string tagName, out DirectiveDescriptor descriptor) =>
        _byTag.TryGetValue(tagName, out descriptor);

    public string ResolveTag(string tagName) =>
        _byTag.TryGetValue(tagName, out var d) ? d.TagName : tagName;

    public void Add(DirectiveDescriptor descriptor)
    {
        if (descriptor == null || string.IsNullOrEmpty(descriptor.TagName)) return;
        Register(descriptor.TagName, descriptor);
        foreach (var alias in descriptor.Aliases)
            if (!string.IsNullOrEmpty(alias))
                Register(alias, descriptor);
    }

    private void Register(string tag, DirectiveDescriptor descriptor)
    {
        if (_byTag.TryGetValue(tag, out var existing) &&
            !string.Equals(existing.TagName, descriptor.TagName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(tag);
        }
        _byTag[tag] = descriptor;
    }

    public static DirectiveCatalog FromCompilation(Compilation compilation)
    {
        var catalog = CreateBuiltIn();
        var attrName = "Square.Directives.SqxDirectiveAttribute";
        foreach (var reference in compilation.References)
        {
            var symbol = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
            if (symbol == null) continue;
            ScanNamespace(symbol.GlobalNamespace, attrName, catalog);
        }
        ScanNamespace(compilation.Assembly.GlobalNamespace, attrName, catalog);
        return catalog;
    }

    private static void ScanNamespace(INamespaceSymbol ns, string attrName, DirectiveCatalog catalog)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamespaceSymbol childNs)
                ScanNamespace(childNs, attrName, catalog);
            else if (member is INamedTypeSymbol type)
                TryAddFromType(type, attrName, catalog);
        }
    }

    private static void TryAddFromType(INamedTypeSymbol type, string attrName, DirectiveCatalog catalog)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass == null) continue;
            var full = attr.AttributeClass.ToDisplayString();
            if (full != attrName && attr.AttributeClass.Name != "SqxDirectiveAttribute") continue;

            var tag = attr.ConstructorArguments.Length > 0
                ? attr.ConstructorArguments[0].Value as string
                : null;
            if (string.IsNullOrEmpty(tag)) continue;

            var descriptor = new DirectiveDescriptor { TagName = tag };
            foreach (var named in attr.NamedArguments)
            {
                switch (named.Key)
                {
                    case "Aliases":
                        descriptor.Aliases = ToStringArray(named.Value);
                        break;
                    case "ParentTag":
                        descriptor.ParentTag = named.Value.Value as string;
                        break;
                    case "AllowedChildTags":
                        descriptor.AllowedChildTags = ToStringArray(named.Value);
                        break;
                    case "SkipStandaloneEmit":
                        descriptor.SkipStandaloneEmit = named.Value.Value is true;
                        break;
                    case "Pattern":
                        descriptor.Pattern = named.Value.Value as string ?? descriptor.Pattern;
                        break;
                    case "RuntimeTypeName":
                        descriptor.RuntimeTypeName = named.Value.Value as string;
                        break;
                    case "FieldPrefix":
                        descriptor.FieldPrefix = named.Value.Value as string ?? descriptor.FieldPrefix;
                        break;
                    case "PrimaryAttribute":
                        descriptor.PrimaryAttribute = named.Value.Value as string;
                        break;
                }
            }
            catalog.Add(descriptor);
        }
    }

    private static ImmutableArray<string> ToStringArray(TypedConstant constant)
    {
        if (constant.Kind != TypedConstantKind.Array || constant.Values.IsDefaultOrEmpty)
            return ImmutableArray<string>.Empty;
        var builder = ImmutableArray.CreateBuilder<string>(constant.Values.Length);
        foreach (var v in constant.Values)
            if (v.Value is string s && !string.IsNullOrEmpty(s))
                builder.Add(s);
        return builder.ToImmutable();
    }

    private static DirectiveCatalog CreateBuiltIn()
    {
        var catalog = new DirectiveCatalog();
        catalog.Add(new DirectiveDescriptor
        {
            TagName = "Show",
            Pattern = "ControlFlowAttach",
            RuntimeTypeName = "ShowNode",
            FieldPrefix = "_show",
            PrimaryAttribute = "when"
        });
        catalog.Add(new DirectiveDescriptor
        {
            TagName = "For",
            Pattern = "ControlFlowAttach",
            RuntimeTypeName = "ForNode",
            FieldPrefix = "_for",
            PrimaryAttribute = "each"
        });
        catalog.Add(new DirectiveDescriptor
        {
            TagName = "Index",
            Pattern = "ControlFlowAttach",
            RuntimeTypeName = "IForNode",
            FieldPrefix = "_index",
            PrimaryAttribute = "each"
        });
        catalog.Add(new DirectiveDescriptor
        {
            TagName = "Switch",
            Pattern = "ControlFlowAttach",
            RuntimeTypeName = "SwitchNode",
            FieldPrefix = "_switch",
            AllowedChildTags = ImmutableArray.Create("Match")
        });
        catalog.Add(new DirectiveDescriptor
        {
            TagName = "Match",
            ParentTag = "Switch",
            SkipStandaloneEmit = true,
            PrimaryAttribute = "when"
        });
        catalog.Add(new DirectiveDescriptor
        {
            TagName = "Slot",
            Aliases = ImmutableArray.Create("Outlet"),
            Pattern = "SlotOutlet",
            PrimaryAttribute = "name"
        });
        return catalog;
    }
}
