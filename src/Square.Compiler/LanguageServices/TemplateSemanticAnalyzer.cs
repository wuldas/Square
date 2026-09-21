using System.Text;
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
    private static readonly SymbolDisplayFormat FullyQualifiedTypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions &
            ~SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    internal TemplateProjectAnalysis AnalyzeProject(
        Compilation compilation,
        IEnumerable<(string Path, string Content, string Namespace)> inputs,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<SquareDiagnostic>();
        Directives.DirectiveCatalog directiveCatalog;
        try
        {
            directiveCatalog = Directives.DirectiveCatalog.FromCompilation(compilation);
        }
        catch (Exception exception)
        {
            diagnostics.Add(new SquareDiagnostic(
                "SQXD001", SquareDiagnosticSeverity.Error, exception.Message, new SquareSourceRange(0, 0), string.Empty));
            directiveCatalog = Directives.DirectiveCatalog.BuiltIn;
        }
        var analysis = TemplateProjectAnalyzer.Analyze(
            compilation,
            (inputs ?? Array.Empty<(string, string, string)>()).ToArray(),
            directiveCatalog,
            cancellationToken);
        if (diagnostics.Count == 0) return analysis;
        return new TemplateProjectAnalysis(
            analysis.Catalog,
            analysis.OutputCompilation,
            analysis.GeneratedSources,
            analysis.Diagnostics.Concat(diagnostics).ToArray(),
            analysis.Documents,
            analysis.SourceMappings);
    }

    internal TemplateComponentContracts BuildComponentContracts(
        Compilation compilation,
        INamedTypeSymbol type,
        bool exported)
    {
        if (compilation == null || type == null) return TemplateComponentContracts.Empty;
        var diagnostics = new List<SquareDiagnostic>();
        var props = BuildProps(compilation, type, exported);
        var slots = BuildSlots(compilation, type, exported);
        var events = BuildEvents(compilation, type, exported, diagnostics, out var declarations);
        return new TemplateComponentContracts(props, events, slots, diagnostics, declarations);
    }

    private static IReadOnlyList<TemplatePropDescriptor> BuildProps(
        Compilation compilation,
        INamedTypeSymbol type,
        bool exported)
    {
        var propAttribute = compilation.GetTypeByMetadataName("Square.Runtime.Binding.PropAttribute");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<TemplatePropDescriptor>();
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (!seen.Add(property.Name) || property.IsStatic || property.IsIndexer || property.GetMethod == null) continue;
                if (!IsAccessible(compilation, property, property.GetMethod, exported)) continue;
                var attribute = property.GetAttributes().FirstOrDefault(item =>
                    propAttribute != null && SymbolEqualityComparer.Default.Equals(item.AttributeClass, propAttribute));
                if (attribute == null) continue;
                var required = attribute.NamedArguments.Any(argument =>
                    argument.Key == "Required" && argument.Value.Value is true);
                result.Add(new TemplatePropDescriptor(property.Name,
                    property.Type.ToDisplayString(FullyQualifiedTypeFormat), required));
            }
        }
        return Array.AsReadOnly(result.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray());
    }

    private static IReadOnlyDictionary<string, TemplateSlotDescriptor> BuildSlots(
        Compilation compilation,
        INamedTypeSymbol type,
        bool exported)
    {
        var slotAttribute = compilation.GetTypeByMetadataName("Square.UI.SlotContractAttribute");
        var slots = new Dictionary<string, TemplateSlotDescriptor>(StringComparer.Ordinal);
        foreach (var current in BaseTypesFirst(type))
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (slotAttribute == null || !SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, slotAttribute) ||
                    attribute.ConstructorArguments.Length != 2 ||
                    attribute.ConstructorArguments[0].Value is not string name ||
                    attribute.ConstructorArguments[1].Value is not INamedTypeSymbol propsType) continue;
                var properties = new Dictionary<string, string>(StringComparer.Ordinal);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var propsCurrent = propsType; propsCurrent != null; propsCurrent = propsCurrent.BaseType)
                {
                    foreach (var property in propsCurrent.GetMembers().OfType<IPropertySymbol>())
                    {
                        if (!seen.Add(property.Name) || property.IsStatic || property.IsIndexer || property.GetMethod == null) continue;
                        if (!IsAccessible(compilation, property, property.GetMethod, exported)) continue;
                        var propertyName = char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1);
                        properties[propertyName] = property.Type.ToDisplayString(FullyQualifiedTypeFormat);
                    }
                }
                slots[name] = new TemplateSlotDescriptor(name,
                    new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(properties));
            }
        }
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, TemplateSlotDescriptor>(slots);
    }

    private static IReadOnlyList<TemplateComponentEventDescriptor> BuildEvents(
        Compilation compilation,
        INamedTypeSymbol type,
        bool exported,
        ICollection<SquareDiagnostic> diagnostics,
        out IReadOnlyList<string> declarations)
    {
        var metadataDiagnostics = new List<SquareDiagnostic>();
        var metadata = ReadEventMetadata(compilation, metadataDiagnostics);
        foreach (var diagnostic in metadataDiagnostics) diagnostics.Add(diagnostic);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<TemplateComponentEventDescriptor>();
        var generated = new List<string>();
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var field in current.GetMembers().OfType<IFieldSymbol>())
            {
                if (!seen.Add(field.Name) || !field.IsStatic || !field.IsReadOnly ||
                    !IsFieldAccessible(compilation, field, exported) ||
                    !TryGetComponentEventDetailType(compilation, field.Type, out var detailTypeName)) continue;
                var sourceName = SymbolEqualityComparer.Default.Equals(field.ContainingAssembly, compilation.Assembly)
                    ? GetSourceEventName(field)
                    : null;
                var componentKey = EventMetadataKey(type, field.Name);
                var declaringKey = EventMetadataKey(field.ContainingType, field.Name);
                metadata.TryGetValue(componentKey, out var metadataName);
                if (metadataName == null) metadata.TryGetValue(declaringKey, out metadataName);
                if (sourceName != null && !IsValidEventName(sourceName))
                {
                    diagnostics.Add(EventDiagnostic("Component event '" + field.Name + "' must use a lowercase kebab-case name.", field));
                    continue;
                }
                if (sourceName != null && metadataName != null && !sourceName.Equals(metadataName, StringComparison.Ordinal))
                {
                    diagnostics.Add(EventDiagnostic("ElementEventContract for '" + type.ToDisplayString() + "." + field.Name +
                        "' conflicts with the source event name.", field));
                    continue;
                }
                var eventName = sourceName ?? metadataName;
                if (eventName == null)
                {
                    if (exported)
                        diagnostics.Add(EventDiagnostic("Exported element '" + type.ToDisplayString() + "' event member '" + field.Name +
                            "' has no literal name or ElementEventContract metadata.", field));
                    continue;
                }
                candidates.Add(new TemplateComponentEventDescriptor(field.Name, eventName, detailTypeName));
                if (exported && sourceName != null && metadataName == null &&
                    SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly))
                    generated.Add("[assembly: global::Square.UI.ElementEventContractAttribute(typeof(" +
                        type.ToDisplayString(FullyQualifiedTypeFormat) + "), \"" + EscapeString(field.Name) + "\", \"" +
                        EscapeString(sourceName) + "\")]");
            }
        }
        var conflicts = candidates.GroupBy(item => item.NormalizedName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).ToArray();
        foreach (var conflict in conflicts)
            diagnostics.Add(EventDiagnostic("Component event alias '" + conflict.Key + "' is ambiguous on '" +
                type.ToDisplayString() + "'.", type));
        var conflictNames = new HashSet<string>(conflicts.Select(group => group.Key), StringComparer.OrdinalIgnoreCase);
        declarations = Array.AsReadOnly(generated.Distinct(StringComparer.Ordinal).ToArray());
        return Array.AsReadOnly(candidates.Where(item => !conflictNames.Contains(item.NormalizedName))
            .OrderBy(item => item.Name, StringComparer.Ordinal).ToArray());
    }

    private static bool IsAccessible(
        Compilation compilation,
        IPropertySymbol property,
        IMethodSymbol getter,
        bool exported) =>
        exported
            ? property.DeclaredAccessibility == Accessibility.Public && getter.DeclaredAccessibility == Accessibility.Public
            : compilation.IsSymbolAccessibleWithin(property, compilation.Assembly) &&
              compilation.IsSymbolAccessibleWithin(getter, compilation.Assembly);

    private static bool IsFieldAccessible(Compilation compilation, IFieldSymbol field, bool exported) =>
        exported
            ? field.DeclaredAccessibility == Accessibility.Public
            : compilation.IsSymbolAccessibleWithin(field, compilation.Assembly);

    public IReadOnlyDictionary<string, TemplateComponentDescriptor> BuildGeneratedComponents(
        IEnumerable<(string Path, string Content, string Namespace)> inputs) =>
        BuildGeneratedComponents(TemplateCatalog.ParseInputs(inputs));

    internal IReadOnlyDictionary<string, TemplateComponentDescriptor> BuildGeneratedComponents(
        IReadOnlyList<(string Path, string Content, string Namespace, SquareParseResult Parse)> inputs)
    {
        var result = new Dictionary<string, TemplateComponentDescriptor>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            var parsed = input.Parse;
            if (!parsed.IsSuccess || parsed.ParsedSqxDocument == null) continue;
            var document = parsed.ParsedSqxDocument;

            var namespaceName = string.IsNullOrWhiteSpace(document.Namespace)
                ? input.Namespace
                : document.Namespace;
            var metadataName = string.IsNullOrWhiteSpace(namespaceName)
                ? document.Name
                : namespaceName + "." + document.Name;
            result[metadataName] = new TemplateComponentDescriptor(
                document.Name,
                metadataName,
                metadataName,
                string.Empty,
                string.Empty,
                document.Name,
                string.Empty,
                input.Path,
                TemplateElementKind.ClrScoped,
                false,
                true,
                false);
        }

        return result;
    }

    public IReadOnlyDictionary<string, TemplatePropDescriptor[]> BuildEmbeddedPropContracts(
        IEnumerable<(string Path, string Content, string Namespace)> inputs)
    {
        var contracts = new Dictionary<string, TemplatePropDescriptor[]>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            if (!TryParse(input.Content, input.Path, out var document)) continue;
            var namespaceName = string.IsNullOrWhiteSpace(document.Namespace)
                ? input.Namespace
                : document.Namespace;
            var metadataName = string.IsNullOrWhiteSpace(namespaceName)
                ? document.Name
                : namespaceName + "." + document.Name;
            contracts[metadataName] = ExtractEmbeddedProps(document).Values.ToArray();
        }
        return contracts;
    }

    public TemplateEventMetadataResult BuildEventMetadata(Compilation compilation, TemplateCatalog catalog)
    {
        if (compilation == null) throw new ArgumentNullException(nameof(compilation));
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));
        var generated = catalog.Components
            .Where(component => component.Kind == TemplateElementKind.Extension &&
                                component.AssemblyName.Equals(compilation.Assembly.Identity.Name, StringComparison.Ordinal))
            .GroupBy(component => component.AssemblyName + "\0" + component.TypeName, StringComparer.Ordinal)
            .Select(group => group.First())
            .SelectMany(catalog.GetEventContractDeclarations)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var source = generated.Length == 0
            ? string.Empty
            : "// <auto-generated/>\n" + string.Join("\n", generated) + "\n";
        return new TemplateEventMetadataResult(source, Array.Empty<SquareDiagnostic>());
    }

    public IReadOnlyDictionary<string, TemplateComponentEventDescriptor[]> BuildEmbeddedEventContracts(
        IEnumerable<(string Path, string Content, string Namespace)> inputs)
    {
        var contracts = new Dictionary<string, TemplateComponentEventDescriptor[]>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            if (!TryParse(input.Content, input.Path, out var document)) continue;
            var namespaceName = string.IsNullOrWhiteSpace(document.Namespace)
                ? input.Namespace
                : document.Namespace;
            var metadataName = string.IsNullOrWhiteSpace(namespaceName)
                ? document.Name
                : namespaceName + "." + document.Name;
            contracts[metadataName] = NormalizeEventContracts(ExtractEmbeddedEvents(document)).ToArray();
        }
        return contracts;
    }

    public IReadOnlyDictionary<string, TemplateComponentEventDescriptor[]> BuildCodeBehindEventContracts(
        string content)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(content ?? string.Empty);
        if (syntaxTree.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            return new Dictionary<string, TemplateComponentEventDescriptor[]>(StringComparer.Ordinal);

        var contracts = new Dictionary<string, TemplateComponentEventDescriptor[]>(StringComparer.Ordinal);
        foreach (var componentClass in syntaxTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var events = NormalizeEventContracts(
                ExtractEventFields(componentClass.Members.OfType<FieldDeclarationSyntax>())).ToArray();
            if (events.Length == 0) continue;
            var namespaceName = string.Join(".", componentClass.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .Select(declaration => declaration.Name.ToString()));
            var metadataName = string.IsNullOrWhiteSpace(namespaceName)
                ? componentClass.Identifier.ValueText
                : namespaceName + "." + componentClass.Identifier.ValueText;
            contracts[metadataName] = events;
        }
        return contracts;
    }

    private static string GetSourceEventName(IFieldSymbol field) =>
        field.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<VariableDeclaratorSyntax>()
            .Select(variable => GetEventName(variable.Initializer?.Value))
            .FirstOrDefault(name => name != null);

    private static Dictionary<string, string> ReadEventMetadata(
        Compilation compilation,
        ICollection<SquareDiagnostic> diagnostics)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var declarations = new List<(string Key, string Name, INamedTypeSymbol Type, string Member, AttributeData Attribute)>();
        var eventAttribute = compilation.GetTypeByMetadataName("Square.UI.ElementEventContractAttribute");
        if (eventAttribute == null) return result;
        foreach (var assembly in ReferencedAssemblies(compilation).Concat(new[] { compilation.Assembly }))
        {
            foreach (var attribute in assembly.GetAttributes().Where(item =>
                         SymbolEqualityComparer.Default.Equals(item.AttributeClass, eventAttribute)))
            {
                if (attribute.ConstructorArguments.Length != 3 ||
                    attribute.ConstructorArguments[0].Value is not INamedTypeSymbol elementType ||
                    attribute.ConstructorArguments[1].Value is not string memberName ||
                    attribute.ConstructorArguments[2].Value is not string eventName ||
                    !IsValidEventName(eventName))
                {
                    diagnostics?.Add(EventDiagnostic("ElementEventContract has invalid arguments or event name.", attribute));
                    continue;
                }
                IFieldSymbol field = null;
                for (var current = elementType; current != null && field == null; current = current.BaseType)
                    field = current.GetMembers(memberName).OfType<IFieldSymbol>().FirstOrDefault();
                if (field == null || !field.IsStatic || !field.IsReadOnly ||
                    field.DeclaredAccessibility != Accessibility.Public ||
                    !TryGetComponentEventDetailType(compilation, field.Type, out _))
                {
                    diagnostics?.Add(EventDiagnostic("ElementEventContract member '" + memberName +
                        "' must be a public static readonly ComponentEvent field.", attribute));
                    continue;
                }
                declarations.Add((
                    EventMetadataKey(elementType, memberName),
                    eventName,
                    elementType,
                    memberName,
                    attribute));
            }
        }
        foreach (var group in declarations.GroupBy(item => item.Key, StringComparer.Ordinal))
        {
            var names = group.Select(item => item.Name).Distinct(StringComparer.Ordinal).ToArray();
            if (names.Length == 1)
            {
                result[group.Key] = names[0];
                continue;
            }
            foreach (var declaration in group)
                diagnostics?.Add(EventDiagnostic("Conflicting ElementEventContract declarations for '" +
                    declaration.Type.ToDisplayString() + "." + declaration.Member + "'.", declaration.Attribute));
        }
        return result;
    }

    private static IEnumerable<IAssemblySymbol> ReferencedAssemblies(Compilation compilation)
    {
        foreach (var reference in compilation.References)
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
                yield return assembly;
    }

    private static string EventMetadataKey(INamedTypeSymbol type, string memberName) =>
        type.ContainingAssembly.Identity.Name + "\0" + type.ToDisplayString(FullyQualifiedTypeFormat) + "\0" + memberName;

    private static string GetMetadataName(INamedTypeSymbol type)
    {
        var names = new Stack<string>();
        for (var current = type; current != null; current = current.ContainingType) names.Push(current.MetadataName);
        var namespaceName = type.ContainingNamespace?.ToDisplayString();
        var nestedName = string.Join("+", names);
        return string.IsNullOrEmpty(namespaceName) ? nestedName : namespaceName + "." + nestedName;
    }

    private static bool IsValidEventName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var segmentStart = true;
        foreach (var character in name)
        {
            if (character == '-')
            {
                if (segmentStart) return false;
                segmentStart = true;
            }
            else if (segmentStart)
            {
                if (character is < 'a' or > 'z') return false;
                segmentStart = false;
            }
            else if (character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9')) return false;
        }
        return !segmentStart;
    }

    private static string EscapeString(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static SquareDiagnostic EventDiagnostic(string message, ISymbol symbol)
    {
        var location = symbol?.Locations.FirstOrDefault(item => item.IsInSource);
        if (location == null)
            return new SquareDiagnostic("SQXE006", SquareDiagnosticSeverity.Error, message, new SquareSourceRange(0, 0), string.Empty);
        var mapped = location.GetMappedLineSpan();
        var path = !string.IsNullOrWhiteSpace(mapped.Path) ? mapped.Path : location.SourceTree?.FilePath ?? string.Empty;
        if (IsTemplatePath(path))
            return new SquareDiagnostic("SQXE006", SquareDiagnosticSeverity.Error, message, new SquareSourceRange(0, 0), path);
        return new SquareDiagnostic("SQXE006", SquareDiagnosticSeverity.Error, message,
            new SquareSourceRange(location.SourceSpan.Start, location.SourceSpan.Length), path);
    }

    private static bool IsTemplatePath(string path) =>
        path.EndsWith(".sqx", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase);

    private static SquareDiagnostic EventDiagnostic(string message, AttributeData attribute)
    {
        var syntax = attribute.ApplicationSyntaxReference?.GetSyntax();
        return syntax == null
            ? new SquareDiagnostic("SQXE006", SquareDiagnosticSeverity.Error, message, new SquareSourceRange(0, 0), string.Empty)
            : new SquareDiagnostic("SQXE006", SquareDiagnosticSeverity.Error, message,
                new SquareSourceRange(syntax.Span.Start, syntax.Span.Length), syntax.SyntaxTree.FilePath ?? string.Empty);
    }

    private static IEnumerable<INamedTypeSymbol> BaseTypesFirst(INamedTypeSymbol type)
    {
        var stack = new Stack<INamedTypeSymbol>();
        for (var current = type; current != null; current = current.BaseType) stack.Push(current);
        while (stack.Count > 0) yield return stack.Pop();
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

    private static Dictionary<string, TemplatePropDescriptor> ExtractEmbeddedProps(SqxDocument document)
    {
        var props = new Dictionary<string, TemplatePropDescriptor>(StringComparer.OrdinalIgnoreCase);
        var script = document.Syntax?.Script;
        if (script == null) return props;
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
        return props;
    }

    private static IEnumerable<TemplateComponentEventDescriptor> ExtractEmbeddedEvents(SqxDocument document)
    {
        var script = document.Syntax?.Script;
        if (script == null) yield break;
        foreach (var componentEvent in ExtractEventFields(script.CSharp.Members.OfType<FieldDeclarationSyntax>()))
            yield return componentEvent;
    }

    private static IEnumerable<TemplateComponentEventDescriptor> ExtractEventFields(
        IEnumerable<FieldDeclarationSyntax> fields)
    {
        foreach (var field in fields)
        {
            if (!IsExportedStaticReadonly(field.Modifiers) ||
                !TryGetComponentEventDetailType(field.Declaration.Type, out var detailTypeName))
                continue;

            foreach (var variable in field.Declaration.Variables)
            {
                var eventName = GetEventName(variable.Initializer?.Value);
                if (eventName == null) continue;
                yield return new TemplateComponentEventDescriptor(
                    variable.Identifier.ValueText,
                    eventName,
                    detailTypeName);
            }
        }
    }

    private static bool IsExportedStaticReadonly(SyntaxTokenList modifiers) =>
        (modifiers.Any(SyntaxKind.PublicKeyword) || modifiers.Any(SyntaxKind.InternalKeyword)) &&
        modifiers.Any(SyntaxKind.StaticKeyword) &&
        modifiers.Any(SyntaxKind.ReadOnlyKeyword);

    private static IEnumerable<TemplateComponentEventDescriptor> NormalizeEventContracts(
        IEnumerable<TemplateComponentEventDescriptor> events)
    {
        var byMember = new Dictionary<string, TemplateComponentEventDescriptor>(StringComparer.Ordinal);
        foreach (var componentEvent in events)
            if (!byMember.ContainsKey(componentEvent.MemberName))
                byMember.Add(componentEvent.MemberName, componentEvent);
        return byMember.Values
            .GroupBy(componentEvent => componentEvent.NormalizedName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .Select(group => group.First());
    }

    private static bool TryGetComponentEventDetailType(TypeSyntax type, out string detailTypeName)
    {
        var typeName = type.ToString().Replace("global::", string.Empty);
        var genericStart = typeName.IndexOf('<');
        var baseName = genericStart < 0 ? typeName : typeName.Substring(0, genericStart);
        if (baseName is not ("ComponentEvent" or "Square.Events.ComponentEvent"))
        {
            detailTypeName = string.Empty;
            return false;
        }

        detailTypeName = genericStart < 0
            ? string.Empty
            : typeName.Substring(genericStart + 1, typeName.Length - genericStart - 2);
        return true;
    }

    private static bool TryGetComponentEventDetailType(
        Compilation compilation,
        ITypeSymbol type,
        out string detailTypeName)
    {
        if (type is not INamedTypeSymbol named)
        {
            detailTypeName = string.Empty;
            return false;
        }
        var definitionName = named.Arity == 0
            ? "Square.Events.ComponentEvent"
            : "Square.Events.ComponentEvent`1";
        var definition = compilation.GetTypeByMetadataName(definitionName);
        if (definition == null || !SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, definition))
        {
            detailTypeName = string.Empty;
            return false;
        }
        detailTypeName = named.Arity == 0
            ? string.Empty
            : named.TypeArguments[0].ToDisplayString(FullyQualifiedTypeFormat);
        return true;
    }

    private static string GetEventName(ExpressionSyntax expression)
    {
        ArgumentListSyntax arguments = null;
        if (expression is ImplicitObjectCreationExpressionSyntax implicitCreation)
            arguments = implicitCreation.ArgumentList;
        else if (expression is ObjectCreationExpressionSyntax creation)
            arguments = creation.ArgumentList;
        if (arguments?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression))
            return literal.Token.ValueText;
        return null;
    }
}

internal sealed class TemplateComponentContracts
{
    internal static TemplateComponentContracts Empty { get; } = new(
        Array.Empty<TemplatePropDescriptor>(),
        Array.Empty<TemplateComponentEventDescriptor>(),
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, TemplateSlotDescriptor>(
            new Dictionary<string, TemplateSlotDescriptor>(StringComparer.Ordinal)),
        Array.Empty<SquareDiagnostic>(),
        Array.Empty<string>());

    internal TemplateComponentContracts(
        IReadOnlyList<TemplatePropDescriptor> props,
        IReadOnlyList<TemplateComponentEventDescriptor> events,
        IReadOnlyDictionary<string, TemplateSlotDescriptor> slots,
        IReadOnlyList<SquareDiagnostic> diagnostics,
        IReadOnlyList<string> eventContractDeclarations)
    {
        Props = Array.AsReadOnly((props ?? Array.Empty<TemplatePropDescriptor>()).ToArray());
        Events = Array.AsReadOnly((events ?? Array.Empty<TemplateComponentEventDescriptor>()).ToArray());
        Slots = slots ?? Empty.Slots;
        Diagnostics = Array.AsReadOnly((diagnostics ?? Array.Empty<SquareDiagnostic>()).ToArray());
        EventContractDeclarations = Array.AsReadOnly((eventContractDeclarations ?? Array.Empty<string>()).ToArray());
    }

    internal IReadOnlyList<TemplatePropDescriptor> Props { get; }
    internal IReadOnlyList<TemplateComponentEventDescriptor> Events { get; }
    internal IReadOnlyDictionary<string, TemplateSlotDescriptor> Slots { get; }
    internal IReadOnlyList<SquareDiagnostic> Diagnostics { get; }
    internal IReadOnlyList<string> EventContractDeclarations { get; }
}

public sealed class TemplateEventMetadataResult
{
    public TemplateEventMetadataResult(string source, IReadOnlyList<SquareDiagnostic> diagnostics)
    {
        Source = source ?? string.Empty;
        Diagnostics = diagnostics ?? Array.Empty<SquareDiagnostic>();
    }

    public string Source { get; }
    public IReadOnlyList<SquareDiagnostic> Diagnostics { get; }
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

internal static class TemplateScriptDefaults
{
    internal static readonly string[] UsingDirectives =
    {
        "Square.UI",
        "Square.Events",
        "Square.Runtime",
        "Square.Runtime.Binding",
        "Square.Graphics",
        "Square.Controls",
        "Square.Controls.Primitives"
    };
}
