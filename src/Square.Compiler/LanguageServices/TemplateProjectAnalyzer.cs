using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Square.Compiler.Directives;
using Square.Compiler.Diagnostics;
using Square.Compiler.Emit;
using Square.Compiler.Parser;
using Square.Compiler.ParserCore;
using Square.Compiler.Syntax;
using Square.Compiler.Template.Ir;

namespace Square.Compiler.LanguageServices;

/// <summary>
/// Shared project-level template analysis used by the generator and the language server.
/// </summary>
internal static class TemplateProjectAnalyzer
{
    internal static TemplateProjectAnalysis Analyze(
        Compilation compilation,
        IReadOnlyList<(string Path, string Content, string Namespace)> inputs,
        DirectiveCatalog directiveCatalog,
        CancellationToken cancellationToken)
    {
        var parsedInputs = TemplateCatalog.ParseInputs(inputs);
        var diagnostics = new List<SquareDiagnostic>();
        var analysisCompilation = TemplateCatalog.AddScriptDeclarations(compilation, parsedInputs);
        var catalog = TemplateCatalog.FromCompilation(analysisCompilation, parsedInputs);
        diagnostics.AddRange(catalog.Diagnostics);

        var documents = new Dictionary<string, TemplateDocumentAnalysis>(StringComparer.Ordinal);
        var emittedSources = new Dictionary<string, string>(StringComparer.Ordinal);
        var documentMappings = new Dictionary<string, IReadOnlyList<TemplateGeneratedSourceMapping>>(StringComparer.Ordinal);
        var sourceMappings = new List<TemplateGeneratedSourceMapping>();
        var eventContractDeclarations = new List<string>();

        foreach (var input in parsedInputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parseResult = input.Parse;
            diagnostics.AddRange(parseResult.Diagnostics);
            if (!parseResult.IsSuccess || parseResult.ParsedSqxDocument == null)
            {
                documents[input.Path] = new TemplateDocumentAnalysis(
                    parseResult.ParsedSqxDocument!,
                    new TemplateResolutionContext(string.Empty, Array.Empty<string>()),
                    new Dictionary<string, TemplateElementResolution>(StringComparer.Ordinal),
                    canEmit: false);
                continue;
            }

            var document = parseResult.ParsedSqxDocument;
            var namespaceName = string.IsNullOrWhiteSpace(document.Namespace) ? input.Namespace : document.Namespace;
            var resolutionContext = new TemplateResolutionContext(namespaceName, ExtractNamespaceUsings(document));
            var resolutions = new Dictionary<string, TemplateElementResolution>(StringComparer.Ordinal);
            var canEmit = true;
            foreach (var element in EnumerateElements(document.Syntax.Template.Ir.Roots))
            {
                if (element.TagName.IndexOf(':') < 0 &&
                    (directiveCatalog.IsDirective(element.TagName) ||
                     element.TagName.Equals("Fragment", StringComparison.OrdinalIgnoreCase) ||
                     element.TagName.Equals("template", StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (!resolutions.TryGetValue(element.TagName, out var resolution))
                {
                    resolution = catalog.ResolveComponent(element.TagName, resolutionContext);
                    resolutions[element.TagName] = resolution;
                }
                if (resolution.Status == TemplateElementResolutionStatus.Resolved) continue;
                canEmit = false;
                diagnostics.Add(ElementDiagnostics.ForResolution(
                    element.TagName,
                    resolution,
                    catalog,
                    element.TagNameRange.Length > 0
                        ? element.TagNameRange
                        : new SquareSourceRange(Math.Max(0, element.Origin.Offset + 1), element.TagName.Length),
                    input.Path));
            }

            var directiveDiagnostics = DirectiveValidator.CollectDiagnostics(
                input.Path, input.Content, document, directiveCatalog);
            diagnostics.AddRange(directiveDiagnostics);
            if (directiveDiagnostics.Any(item => item.Severity == SquareDiagnosticSeverity.Error))
                canEmit = false;

            if (!ValidatePropsAndSlots(catalog, document, resolutionContext, resolutions, diagnostics))
                canEmit = false;

            documents[input.Path] = new TemplateDocumentAnalysis(document, resolutionContext, resolutions, canEmit);
        }

        foreach (var pair in documents.Where(pair => pair.Value.CanEmit))
        {
            FillSlotBindingTypes(catalog, pair.Value.Document, pair.Value.Context, pair.Value.Resolutions);
        }

        foreach (var pair in documents.Where(pair => pair.Value.CanEmit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = parsedInputs.First(item => string.Equals(item.Path, pair.Key, StringComparison.Ordinal));
            var document = pair.Value.Document;
            try
            {
                var emitter = new ComponentEmitter(
                    document,
                    pair.Value.Context.CurrentNamespace,
                    directiveCatalog,
                    analysisCompilation,
                    catalog,
                    pair.Value.Context,
                    pair.Value.Resolutions);
                var source = emitter.Emit();
                var hintName = Path.GetFileNameWithoutExtension(input.Path) + "_" + StableHash(input.Path) + ".g.cs";
                emittedSources[hintName] = source;
                documentMappings[input.Path] = emitter.SourceMappings;
                sourceMappings.AddRange(emitter.SourceMappings);
                CollectEventContractDeclarations(catalog, document, eventContractDeclarations);
            }
            catch (InvalidOperationException)
            {
                diagnostics.Add(new SquareDiagnostic(
                    "SQXE001",
                    SquareDiagnosticSeverity.Error,
                    "Template '" + input.Path + "' could not be emitted because an element could not be resolved.",
                    new SquareSourceRange(0, 0),
                    input.Path));
            }
        }

        var eventMetadata = BuildEventMetadataSource(compilation, catalog, eventContractDeclarations);
        var outputCompilation = compilation.AddSyntaxTrees(
            emittedSources.Select(pair => (SyntaxTree)CSharpSyntaxTree.ParseText(
                pair.Value,
                (CSharpParseOptions)compilation.SyntaxTrees.FirstOrDefault()?.Options,
                path: pair.Key)));
        if (eventMetadata.Length > 0)
        {
            emittedSources["SquareElementEventContracts.g.cs"] = eventMetadata;
            outputCompilation = outputCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                eventMetadata,
                (CSharpParseOptions)compilation.SyntaxTrees.FirstOrDefault()?.Options,
                path: "SquareElementEventContracts.g.cs"));
        }

        var generatedComponents = new Dictionary<string, TemplateComponentDescriptor>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            if (!documents.TryGetValue(input.Path, out var documentAnalysis) || documentAnalysis.Document == null) continue;
            var document = documentAnalysis.Document;
            var namespaceName = string.IsNullOrWhiteSpace(document.Namespace) ? input.Namespace : document.Namespace;
            var metadataName = string.IsNullOrWhiteSpace(namespaceName) ? document.Name : namespaceName + "." + document.Name;
            generatedComponents[metadataName] = new TemplateComponentDescriptor(
                document.Name, metadataName, metadataName, string.Empty, string.Empty, document.Name,
                compilation.Assembly.Identity.Name, input.Path, TemplateElementKind.ClrScoped, false, true, false);
        }

        var finalCatalog = TemplateCatalog.FromOutputCompilation(outputCompilation, generatedComponents);
        CollectSemanticDiagnostics(outputCompilation, documents, documentMappings, inputs, diagnostics);
        return new TemplateProjectAnalysis(
            finalCatalog,
            outputCompilation,
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(emittedSources),
            Deduplicate(diagnostics),
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, TemplateDocumentAnalysis>(documents),
            sourceMappings);
    }

    private static void CollectSemanticDiagnostics(
        Compilation outputCompilation,
        IReadOnlyDictionary<string, TemplateDocumentAnalysis> documents,
        IReadOnlyDictionary<string, IReadOnlyList<TemplateGeneratedSourceMapping>> mappings,
        IReadOnlyList<(string Path, string Content, string Namespace)> inputs,
        ICollection<SquareDiagnostic> diagnostics)
    {
        var semanticDiagnostics = outputCompilation.GetDiagnostics()
            .Where(item => item.Severity == DiagnosticSeverity.Error && item.Location.IsInSource)
            .GroupBy(item => item.Location.SourceTree?.FilePath ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var pair in documents)
        {
            if (!pair.Value.CanEmit || pair.Value.Document == null) continue;
            var hintName = Path.GetFileNameWithoutExtension(pair.Key) + "_" + StableHash(pair.Key) + ".g.cs";
            var tree = outputCompilation.SyntaxTrees.FirstOrDefault(item =>
                string.Equals(item.FilePath, hintName, StringComparison.Ordinal));
            if (tree == null) continue;
            var isSqv = pair.Key.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase);
            var documentMappings = mappings.TryGetValue(pair.Key, out var list)
                ? list
                : Array.Empty<TemplateGeneratedSourceMapping>();
            var content = inputs.FirstOrDefault(input => string.Equals(input.Path, pair.Key, StringComparison.Ordinal)).Content ?? string.Empty;
            if (!semanticDiagnostics.TryGetValue(hintName, out var treeDiagnostics)) continue;
            foreach (var semantic in treeDiagnostics)
            {
                var mapping = FindMapping(documentMappings, semantic.Location.SourceSpan);
                var range = mapping?.SourceRange ?? MapLineSpanToRange(semantic, pair.Key, content);
                diagnostics.Add(new SquareDiagnostic(
                    ClassifySemanticDiagnostic(mapping?.Kind, semantic.Id, isSqv),
                    SquareDiagnosticSeverity.Error,
                    semantic.GetMessage(),
                    range,
                    pair.Key));
            }
        }
    }

    private static TemplateGeneratedSourceMapping FindMapping(
        IReadOnlyList<TemplateGeneratedSourceMapping> mappings,
        Microsoft.CodeAnalysis.Text.TextSpan span)
    {
        TemplateGeneratedSourceMapping best = null;
        foreach (var mapping in mappings)
        {
            if (!mapping.GeneratedSpan.Contains(span)) continue;
            if (best == null || mapping.GeneratedSpan.Length < best.GeneratedSpan.Length) best = mapping;
        }
        return best;
    }

    private static SquareSourceRange MapLineSpanToRange(
        Diagnostic semantic,
        string sourcePath,
        string content)
    {
        var mapped = semantic.Location.GetMappedLineSpan();
        if (!string.Equals(mapped.Path, sourcePath, StringComparison.OrdinalIgnoreCase))
            return new SquareSourceRange(0, 0);
        var offset = 0;
        var line = 0;
        while (line < mapped.StartLinePosition.Line && offset < content.Length)
        {
            if (content[offset++] == '\n') line++;
        }
        var position = Math.Min(content.Length, offset + Math.Max(0, mapped.StartLinePosition.Character));
        return new SquareSourceRange(position, 0);
    }

    private static string ClassifySemanticDiagnostic(
        TemplateGeneratedSourceKind? kind,
        string csharpId,
        bool isSqv)
    {
        if (isSqv) return "SQV0013";
        if (kind == null) return csharpId;
        if (IsMissingMemberDiagnostic(csharpId)) return "SQX0004";
        return kind switch
        {
            TemplateGeneratedSourceKind.EventHandler => "SQX0005",
            TemplateGeneratedSourceKind.PropertyBinding => "SQX0007",
            TemplateGeneratedSourceKind.SlotBinding => "SQX0007",
            _ => csharpId
        };
    }

    private static bool IsMissingMemberDiagnostic(string csharpId) =>
        csharpId is "CS0103" or "CS0117" or "CS0119" or "CS1061" or "CS0122";

    /// <summary>把 <c>onXxx</c> 属性名归一化为事件别名（去连字符并小写）。</summary>
    private static string NormalizeEventName(string attributeName) =>
        attributeName.Length > 2 && attributeName.StartsWith("on", StringComparison.OrdinalIgnoreCase)
            ? new string(attributeName.Substring(2)
                .Where(character => character != '-')
                .Select(char.ToLowerInvariant)
                .ToArray())
            : string.Empty;

    private static bool ValidatePropsAndSlots(
        TemplateCatalog catalog,
        SqxDocument document,
        TemplateResolutionContext resolutionContext,
        IReadOnlyDictionary<string, TemplateElementResolution> resolutions,
        ICollection<SquareDiagnostic> diagnostics)
    {
        var hasErrors = false;
        var seenRefs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in EnumerateElements(document.Syntax.Template.Ir.Roots))
        {
            var resolution = resolutions.TryGetValue(element.TagName, out var cached)
                ? cached
                : catalog.ResolveComponent(element.TagName, resolutionContext);
            if (resolution.Status == TemplateElementResolutionStatus.Resolved)
            {
                var props = catalog.GetProps(resolution.Component);
                foreach (var prop in props)
                {
                    var attribute = element.Attributes.FirstOrDefault(candidate =>
                        candidate.Name.Equals(prop.Name, StringComparison.OrdinalIgnoreCase));
                    if (attribute == null)
                    {
                        if (!prop.Required) continue;
                        hasErrors = true;
                        diagnostics.Add(new SquareDiagnostic(
                            "SQX0003",
                            SquareDiagnosticSeverity.Error,
                            "Component '" + element.TagName + "' requires Prop '" + prop.Name + "'.",
                            new SquareSourceRange(element.Origin.Offset + 1, element.TagName.Length),
                            document.SourcePath));
                        continue;
                    }
                    if (attribute.IsExpression || string.IsNullOrEmpty(attribute.Value)) continue;
                    var innerType = ExtractInnerType(prop.TypeName);
                    if (IsAssignableTo(innerType, attribute.Value)) continue;
                    hasErrors = true;
                    diagnostics.Add(new SquareDiagnostic(
                        "SQX0007",
                        SquareDiagnosticSeverity.Error,
                        "Prop '" + prop.Name + "' type mismatch.",
                        attribute.Origin,
                        document.SourcePath));
                }
                if (props.Count > 0)
                {
                    foreach (var attribute in element.Attributes)
                    {
                        if (IsStructuralAttribute(attribute.Name) || attribute.Name.StartsWith("__", StringComparison.Ordinal)) continue;
                        if (attribute.IsExpression) continue;
                        if (attribute.Kind is TemplateIrAttributeKind.DynamicProperty or TemplateIrAttributeKind.DynamicEvent or
                            TemplateIrAttributeKind.ObjectProperties or TemplateIrAttributeKind.ObjectEvents) continue;
                        if (props.Any(prop => prop.Name.Equals(attribute.Name, StringComparison.OrdinalIgnoreCase))) continue;
                        if (TemplateCatalog.BuiltIn.GetProperty(attribute.Name) != null) continue;
                        hasErrors = true;
                        diagnostics.Add(new SquareDiagnostic(
                            "SQX0004",
                            SquareDiagnosticSeverity.Error,
                            "Member '" + attribute.Name + "' was not found on component '" + element.TagName + "'.",
                            attribute.Origin,
                            document.SourcePath));
                    }
                }
                if (!resolution.Component.IsBuiltIn)
                {
                    var events = catalog.GetEvents(resolution.Component);
                    var standardEvents = new HashSet<string>(
                        catalog.Events.Select(item => item.Name), StringComparer.OrdinalIgnoreCase);
                    foreach (var attribute in element.Attributes)
                    {
                        if (attribute.Kind != TemplateIrAttributeKind.Event) continue;
                        var eventName = NormalizeEventName(attribute.Name);
                        if (eventName.Length == 0) continue;
                        if (events.Any(componentEvent =>
                                componentEvent.NormalizedName.Equals(eventName, StringComparison.OrdinalIgnoreCase))) continue;
                        if (standardEvents.Contains(eventName)) continue;
                        hasErrors = true;
                        diagnostics.Add(new SquareDiagnostic(
                            "SQX0005",
                            SquareDiagnosticSeverity.Error,
                            "Component '" + element.TagName + "' declares no event '" + eventName + "'.",
                            attribute.Origin,
                            document.SourcePath));
                    }
                }
            }
            var refAttribute = element.Attributes.FirstOrDefault(attribute =>
                attribute.Name.Equals("ref", StringComparison.OrdinalIgnoreCase));
            if (refAttribute != null && !string.IsNullOrWhiteSpace(refAttribute.Value) && !seenRefs.Add(refAttribute.Value))
            {
                hasErrors = true;
                diagnostics.Add(new SquareDiagnostic(
                    "SQX0006",
                    SquareDiagnosticSeverity.Error,
                    "Duplicate ref name '" + refAttribute.Value + "'.",
                    refAttribute.Origin,
                    document.SourcePath));
            }

            foreach (var slot in element.Children.OfType<TemplateIrSlot>())
            {
                var scope = slot.Scope;
                if (scope == null || scope.Properties.Count == 0) continue;
                if (slot.NameIsExpression)
                {
                    hasErrors = true;
                    diagnostics.Add(new SquareDiagnostic(
                        "SQV0012", SquareDiagnosticSeverity.Error,
                        "Dynamic slot names cannot use typed destructuring.",
                        scope.Origin, document.SourcePath));
                    continue;
                }
                var slots = resolution.Status == TemplateElementResolutionStatus.Resolved
                    ? catalog.GetSlots(resolution.Component)
                    : null;
                if (slots == null || !slots.TryGetValue(slot.Name, out var contract))
                {
                    hasErrors = true;
                    foreach (var binding in scope.Properties) binding.TypeName = "object";
                    diagnostics.Add(new SquareDiagnostic(
                        "SQV0010", SquareDiagnosticSeverity.Error,
                        "Component <" + element.TagName + "> does not declare a contract for slot '" +
                        (slot.Name.Length == 0 ? "default" : slot.Name) + "'.",
                        scope.Origin, document.SourcePath));
                    continue;
                }
                foreach (var binding in scope.Properties)
                {
                    if (contract.Properties.TryGetValue(binding.PropertyName, out var typeName))
                        binding.TypeName = typeName;
                    else
                    {
                        hasErrors = true;
                        binding.TypeName = "object";
                        diagnostics.Add(new SquareDiagnostic(
                            "SQV0011", SquareDiagnosticSeverity.Error,
                            "Slot '" + (slot.Name.Length == 0 ? "default" : slot.Name) +
                            "' does not provide property '" + binding.PropertyName + "'.",
                            binding.Origin, document.SourcePath));
                    }
                }
            }
        }
        return !hasErrors;
    }

    private static bool IsStructuralAttribute(string name) =>
        name.Equals("id", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("class", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("style", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("ref", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("slot", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("key", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("name", StringComparison.OrdinalIgnoreCase);

    private static string ExtractInnerType(string typeName)
    {
        var open = typeName.IndexOf('<');
        var close = typeName.LastIndexOf('>');
        return open >= 0 && close > open ? typeName.Substring(open + 1, close - open - 1).Trim() : typeName;
    }

    private static bool IsAssignableTo(string innerType, string value)
    {
        if (string.IsNullOrEmpty(innerType)) return true;
        innerType = innerType.Replace("global::System.", string.Empty).Replace("System.", string.Empty);
        if (innerType is "string" or "String") return true;
        if (innerType is "int" or "Int32") return int.TryParse(value, out _);
        if (innerType is "float" or "Single") return float.TryParse(value, out _);
        if (innerType is "double" or "Double") return double.TryParse(value, out _);
        if (innerType is "bool" or "Boolean") return bool.TryParse(value, out _);
        return true;
    }

    private static void CollectEventContractDeclarations(
        TemplateCatalog catalog,
        SqxDocument document,
        ICollection<string> declarations)
    {
        var namespaceName = string.IsNullOrWhiteSpace(document.Namespace) ? string.Empty : document.Namespace;
        var typeName = namespaceName.Length == 0 ? document.Name : namespaceName + "." + document.Name;
        foreach (var component in catalog.Components.Where(item =>
                     item.Kind == TemplateElementKind.Extension &&
                     (item.TypeName.Equals(typeName, StringComparison.Ordinal))))
        {
            foreach (var declaration in catalog.GetEventContractDeclarations(component))
                declarations.Add(declaration);
        }
    }

    private static string BuildEventMetadataSource(
        Compilation compilation,
        TemplateCatalog catalog,
        IReadOnlyList<string> extraDeclarations)
    {
        var generated = catalog.Components
            .Where(component => component.Kind == TemplateElementKind.Extension &&
                                component.AssemblyName.Equals(compilation.Assembly.Identity.Name, StringComparison.Ordinal))
            .GroupBy(component => component.AssemblyName + "\0" + component.TypeName, StringComparer.Ordinal)
            .Select(group => group.First())
            .SelectMany(catalog.GetEventContractDeclarations)
            .Concat(extraDeclarations)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return generated.Length == 0
            ? string.Empty
            : "// <auto-generated/>\n" + string.Join("\n", generated) + "\n";
    }

    private static IReadOnlyList<string> ExtractNamespaceUsings(SqxDocument document)
    {
        var script = document.Syntax?.Script;
        if (script == null) return Array.Empty<string>();
        return script.CSharp.Usings
            .Where(directive => directive.Alias == null && directive.StaticKeyword.RawKind == 0)
            .Select(directive => directive.Name?.ToString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();
    }

    internal static void FillSlotBindingTypes(
        TemplateCatalog catalog,
        SqxDocument document,
        TemplateResolutionContext resolutionContext,
        IReadOnlyDictionary<string, TemplateElementResolution> resolutions)
    {
        foreach (var element in EnumerateElements(document.Syntax.Template.Ir.Roots))
        {
            foreach (var slot in element.Children.OfType<TemplateIrSlot>())
            {
                var scope = slot.Scope;
                if (scope == null || scope.Properties.Count == 0) continue;
                if (slot.NameIsExpression) continue;
                if (!resolutions.TryGetValue(element.TagName, out var resolution) ||
                    resolution.Status != TemplateElementResolutionStatus.Resolved) continue;
                var slots = catalog.GetSlots(resolution.Component);
                if (slots == null || !slots.TryGetValue(slot.Name, out var contract)) continue;
                foreach (var binding in scope.Properties)
                    if (contract.Properties.TryGetValue(binding.PropertyName, out var typeName))
                        binding.TypeName = typeName;
            }
        }
    }

    private static IEnumerable<TemplateIrElement> EnumerateElements(IEnumerable<TemplateIrNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is TemplateIrElement element)
            {
                yield return element;
                foreach (var child in EnumerateElements(element.Children)) yield return child;
                foreach (var attribute in element.Attributes)
                    foreach (var child in EnumerateElements(attribute.FragmentNodes ?? Array.Empty<TemplateIrNode>()))
                        yield return child;
            }
            else if (node is TemplateIrFor loop)
            {
                foreach (var child in EnumerateElements(loop.Children)) yield return child;
                foreach (var child in EnumerateElements(loop.Fallback)) yield return child;
            }
            else if (node is TemplateIrIfChain chain)
            {
                foreach (var branch in chain.Branches)
                    foreach (var child in EnumerateElements(branch.Children)) yield return child;
            }
            else if (node is TemplateIrSlot slot)
            {
                foreach (var child in EnumerateElements(slot.Children)) yield return child;
            }
        }
    }

    private static IReadOnlyList<SquareDiagnostic> Deduplicate(IEnumerable<SquareDiagnostic> diagnostics) =>
        diagnostics.GroupBy(diagnostic =>
                diagnostic.Id + "\0" + diagnostic.SourcePath + "\0" + diagnostic.Range.Offset + "\0" +
                diagnostic.Range.Length + "\0" + diagnostic.Message, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(diagnostic => diagnostic.SourcePath, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Range.Offset)
            .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ToArray();

    private static string StableHash(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in value) hash = (hash ^ character) * 16777619u;
            return hash.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
