using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Square.Compiler.Directives;
using Square.Compiler.Emit;
using Square.Compiler.LanguageServices;
using Square.Compiler.Parser;

namespace Square.Compiler;

[Generator]
public sealed class SqxGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var inputs = context.AdditionalTextsProvider
            .Where(file => IsTemplateFile(file.Path))
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select((pair, cancellationToken) =>
            {
                var file = pair.Left;
                var options = pair.Right;
                options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var rootNamespace);
                options.GlobalOptions.TryGetValue("build_property.MSBuildProjectDirectory", out var projectDirectory);
                options.GetOptions(file).TryGetValue("build_metadata.AdditionalFiles.Link", out var logicalPath);
                return new SqxInput(
                    file.Path,
                    file.GetText(cancellationToken)?.ToString() ?? "",
                    GetDefaultNamespace(
                        rootNamespace ?? "Square.Sample",
                        file.Path,
                        projectDirectory,
                        logicalPath));
            })
            .Collect();

        // Compilation drives directive catalog refresh (metadata scan of referenced assemblies).
        var compilationAndInputs = context.CompilationProvider.Combine(inputs);

        context.RegisterSourceOutput(compilationAndInputs, static (productionContext, pair) =>
        {
            var compilation = pair.Left;
            var files = pair.Right;
            DirectiveCatalog catalog;
            try
            {
                catalog = DirectiveCatalog.FromCompilation(compilation);
            }
            catch (Exception ex)
            {
                productionContext.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.SqxDiagnostics.SQXD001_DuplicateDirective,
                    Location.None,
                    ex.Message));
                catalog = DirectiveCatalog.BuiltIn;
            }

            var semanticAnalyzer = new TemplateSemanticAnalyzer();
            var semanticInputs = files.Select(file => (file.Path, file.Content, file.Namespace)).ToArray();
            var contracts = semanticAnalyzer.BuildPropContracts(compilation, semanticInputs);
            var generatedTypes = semanticAnalyzer.BuildGeneratedTypeNames(semanticInputs);
            var slotContracts = semanticAnalyzer.BuildSlotContracts(compilation);
            foreach (var file in files)
                Generate(productionContext, compilation, file, contracts, generatedTypes, slotContracts, catalog);
        });
    }

    private static void Generate(
        SourceProductionContext context,
        Compilation compilation,
        SqxInput input,
        IReadOnlyDictionary<string, TemplatePropDescriptor[]> contracts,
        IReadOnlyCollection<string> generatedTypes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, TemplateSlotDescriptor>> slotContracts,
        DirectiveCatalog catalog)
    {
        string code;
        try
        {
            var document = ParseDocument(input);
            ValidateRequiredProps(context, input, document, contracts);
            ValidateRefNames(context, input, document);
            ValidateSlotScopes(context, input, document, slotContracts);
            code = DirectiveValidator.Validate(context, input.Path, input.Content, document, catalog)
                ? new ComponentEmitter(document, input.Namespace, catalog).Emit()
                : "// Generator error: unsupported directive shape\n// Path: " + input.Path;
            if (input.Path.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase))
                ReportSemanticDiagnostics(context, compilation, input, code, generatedTypes);
        }
        catch (SqxParseException exception)
        {
            code = $"// Generator error: {exception.Message}\n// Path: {input.Path}";
            var parseResult = SquareDocumentService.Parse(input.Content, input.Path);
            var diagnostic = parseResult.Diagnostics.Length > 0
                ? parseResult.Diagnostics[0]
                : new SquareDiagnostic(
                    input.Path.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase)
                        ? "SQV0001"
                        : "SQX0001",
                    SquareDiagnosticSeverity.Error,
                    exception.Message,
                    new SquareSourceRange(exception.Position < 0 ? 0 : exception.Position, 0),
                    input.Path);
            var span = diagnostic.Range.ToTextSpan(parseResult.SourceText);
            var descriptor = input.Path.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase)
                ? Diagnostics.SqvDiagnostics.Get(diagnostic.Id)
                : Diagnostics.SqxDiagnostics.SQX0001_SyntaxError;
            context.ReportDiagnostic(Diagnostic.Create(
                descriptor,
                Location.Create(input.Path, span, diagnostic.GetLinePositionSpan(parseResult.SourceText)),
                diagnostic.Message));
        }
        catch (Exception exception)
        {
            code = $"// Generator error: {exception.Message}\n// Path: {input.Path}";
            var descriptor = input.Path.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase)
                ? Diagnostics.SqvDiagnostics.SQV0001_SyntaxError
                : Diagnostics.SqxDiagnostics.SQX0001_SyntaxError;
            context.ReportDiagnostic(Diagnostic.Create(
                descriptor,
                Location.None,
                exception.Message));
        }

        var hintName = Path.GetFileNameWithoutExtension(input.Path) + "_" + StableHash(input.Path) + ".g.cs";
        context.AddSource(hintName, SourceText.From(code, Encoding.UTF8));
    }


    private static SqxDocument ParseDocument(SqxInput input)
    {
        var result = SquareDocumentService.ParseSyntax(input.Content, input.Path);
        if (!result.IsSuccess)
        {
            var diagnostic = result.Diagnostics[0];
            throw new SqxParseException(
                diagnostic.Message,
                diagnostic.Range.Offset,
                diagnostic.Id);
        }

        return result.ParsedSqxDocument;
    }

    private static bool IsTemplateFile(string path) =>
        (path.EndsWith(".sqx", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase)) &&
        !IsResourceDirectoryPath(path);

    private static bool IsResourceDirectoryPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith("Public/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
               normalized.IndexOf("/Public/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalized.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string GetDefaultNamespace(
        string rootNamespace,
        string filePath,
        string projectDirectory,
        string logicalPath)
    {
        var relativePath = !string.IsNullOrWhiteSpace(logicalPath)
            ? logicalPath
            : GetProjectRelativePath(filePath, projectDirectory);
        var directory = Path.GetDirectoryName(relativePath);
        if (string.IsNullOrWhiteSpace(directory)) return rootNamespace;

        var segments = directory
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeNamespaceSegment)
            .Where(segment => segment.Length > 0);
        var suffix = string.Join(".", segments);
        return suffix.Length == 0 ? rootNamespace : rootNamespace + "." + suffix;
    }

    private static string GetProjectRelativePath(string filePath, string projectDirectory)
    {
        if (!Path.IsPathRooted(filePath)) return filePath;
        if (string.IsNullOrWhiteSpace(projectDirectory)) return Path.GetFileName(filePath);

        var projectPath = Path.GetFullPath(projectDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(filePath);
        return fullPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase)
            ? fullPath.Substring(projectPath.Length)
            : Path.GetFileName(filePath);
    }

    private static string SanitizeNamespaceSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return "";
        var builder = new StringBuilder(segment.Length + 1);
        for (var i = 0; i < segment.Length; i++)
        {
            var character = segment[i];
            var valid = i == 0
                ? character == '_' || char.IsLetter(character)
                : character == '_' || char.IsLetterOrDigit(character);
            builder.Append(valid ? character : '_');
        }

        var value = builder.ToString();
        if (value.Length == 0 || !(value[0] == '_' || char.IsLetter(value[0]))) value = "_" + value;
        return Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(value) !=
               Microsoft.CodeAnalysis.CSharp.SyntaxKind.None
            ? "_" + value
            : value;
    }

    private static void ValidateRequiredProps(
        SourceProductionContext context,
        SqxInput input,
        SqxDocument document,
        IReadOnlyDictionary<string, TemplatePropDescriptor[]> contracts)
    {
        var currentNamespace = string.IsNullOrWhiteSpace(document.Namespace)
            ? input.Namespace
            : document.Namespace;
        var scriptUsings = ExtractNamespaceUsings(document);
        foreach (var element in EnumerateElements(document.Template.Roots))
        {
            var contractName = ResolveContractName(
                element.TagName,
                currentNamespace,
                scriptUsings,
                contracts.Keys);
            if (contractName == null || !contracts.TryGetValue(contractName, out var props)) continue;
            foreach (var prop in props)
            {
                var attr = element.Attributes.FirstOrDefault(a =>
                    string.Equals(a.Name, prop.Name, StringComparison.OrdinalIgnoreCase));
                if (attr == null)
                {
                    if (prop.Required)
                        context.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.SqxDiagnostics.SQX0003_RequiredPropMissing,
                            CreateLocation(input, element.Line, element.Column),
                            element.TagName,
                            prop.Name));
                    continue;
                }
                if (!attr.IsExpression && !string.IsNullOrEmpty(attr.RawValue))
                {
                    var innerType = ExtractInnerType(prop.TypeName);
                    if (!IsAssignableTo(innerType, attr.RawValue))
                        context.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.SqxDiagnostics.SQX0007_PropTypeMismatch,
                            CreateLocation(input, element.Line, element.Column),
                            prop.Name));
                }
            }
        }
    }

    private static string ResolveContractName(
        string tagName,
        string currentNamespace,
        IReadOnlyList<string> scriptUsings,
        IEnumerable<string> contractNames)
    {
        var names = contractNames as ICollection<string> ?? contractNames.ToArray();
        var normalizedTag = tagName.StartsWith("global::", StringComparison.Ordinal)
            ? tagName.Substring("global::".Length)
            : tagName;
        if (normalizedTag.Contains('.') && names.Contains(normalizedTag)) return normalizedTag;

        var currentName = string.IsNullOrWhiteSpace(currentNamespace)
            ? normalizedTag
            : currentNamespace + "." + normalizedTag;
        if (names.Contains(currentName)) return currentName;

        foreach (var namespaceName in scriptUsings)
        {
            var importedName = namespaceName + "." + normalizedTag;
            if (names.Contains(importedName)) return importedName;
        }

        var suffix = "." + normalizedTag;
        var matches = names.Where(name => name == normalizedTag || name.EndsWith(suffix, StringComparison.Ordinal)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static IReadOnlyList<string> ExtractNamespaceUsings(string scriptCode)
    {
        if (string.IsNullOrWhiteSpace(scriptCode)) return Array.Empty<string>();

        var result = new List<string>();
        foreach (Match match in Regex.Matches(
                     scriptCode,
                     @"(?m)^\s*using\s+(?!static\b)(?<namespace>[A-Za-z_][A-Za-z0-9_.]*)\s*;"))
            result.Add(match.Groups["namespace"].Value);
        return result;
    }

    private static IReadOnlyList<string> ExtractNamespaceUsings(SqxDocument document)
    {
        var script = document.Syntax?.Script;
        if (script == null) return ExtractNamespaceUsings(document.ScriptCode);

        return script.CSharp.Usings
            .Where(directive => directive.Alias == null && directive.StaticKeyword.RawKind == 0)
            .Select(directive => directive.Name?.ToString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();
    }

    private static string ExtractInnerType(string typeName)
    {
        var open = typeName.IndexOf('<');
        var close = typeName.LastIndexOf('>');
        return open >= 0 && close > open ? typeName.Substring(open + 1, close - open - 1).Trim() : typeName;
    }

    private static bool IsAssignableTo(string innerType, string value)
    {
        if (string.IsNullOrEmpty(innerType)) return true;
        if (innerType == "string") return true;
        if (innerType == "int" || innerType == "Int32")
            return int.TryParse(value, out _);
        if (innerType == "float" || innerType == "Single")
            return float.TryParse(value, out _);
        if (innerType == "double" || innerType == "Double")
            return double.TryParse(value, out _);
        if (innerType == "bool" || innerType == "Boolean")
            return bool.TryParse(value, out _);
        return true;
    }

    private static IEnumerable<SqxElement> EnumerateElements(IEnumerable<SqxNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is SqxElement element)
            {
                yield return element;
                foreach (var child in EnumerateElements(element.Children))
                    yield return child;
            }
            else if (node is TemplateForDirective forDirective)
            {
                foreach (var child in EnumerateElements(forDirective.Children))
                    yield return child;
            }
            else if (node is TemplateIfChainDirective ifChain)
            {
                foreach (var branch in ifChain.Branches)
                foreach (var child in EnumerateElements(branch.Children))
                    yield return child;
            }
        }
    }

    private static void ValidateRefNames(
        SourceProductionContext context,
        SqxInput input,
        SqxDocument document)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in EnumerateElements(document.Template.Roots))
        {
            var refAttr = element.Attributes.FirstOrDefault(
                a => string.Equals(a.Name, "ref", StringComparison.OrdinalIgnoreCase));
            if (refAttr == null || string.IsNullOrWhiteSpace(refAttr.RawValue)) continue;
            if (!seen.Add(refAttr.RawValue))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.SqxDiagnostics.SQX0006_RefNameConflict,
                    CreateLocation(input, element.Line, element.Column),
                    refAttr.RawValue));
            }
        }
    }

    private static void ValidateSlotScopes(
        SourceProductionContext context,
        SqxInput input,
        SqxDocument document,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, TemplateSlotDescriptor>> contracts)
    {
        var currentNamespace = string.IsNullOrWhiteSpace(document.Namespace) ? input.Namespace : document.Namespace;
        var scriptUsings = ExtractNamespaceUsings(document);
        Visit(document.Template.Roots);

        void Visit(IEnumerable<SqxNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node is SqxElement element)
                {
                    ValidateComponentSlots(element);
                    Visit(element.Children);
                }
                else if (node is TemplateForDirective loop) Visit(loop.Children);
                else if (node is TemplateIfChainDirective chain)
                    foreach (var branch in chain.Branches) Visit(branch.Children);
            }
        }

        void ValidateComponentSlots(SqxElement component)
        {
            foreach (var child in component.Children.OfType<SqxElement>())
            {
                var scope = child.SlotScope;
                if (scope == null || scope.Properties.Count == 0) continue;
                var slotAttribute = child.Attributes.FirstOrDefault(attribute =>
                    string.Equals(attribute.Name, "slot", StringComparison.OrdinalIgnoreCase));
                if (slotAttribute?.IsExpression == true)
                {
                    Report(Diagnostics.SqvDiagnostics.SQV0012_DynamicSlotDestructuring, scope.Position,
                        "Dynamic slot names cannot use typed destructuring.");
                    continue;
                }

                var componentName = ResolveContractName(component.TagName, currentNamespace, scriptUsings, contracts.Keys);
                var slotName = slotAttribute?.RawValue ?? "";
                if (componentName == null || !contracts.TryGetValue(componentName, out var componentSlots) ||
                    !componentSlots.TryGetValue(slotName, out var contract))
                {
                    foreach (var binding in scope.Properties) binding.TypeName = "object";
                    Report(Diagnostics.SqvDiagnostics.SQV0010_SlotContractMissing, scope.Position,
                        "Component <" + component.TagName + "> does not declare a contract for slot '" +
                        (slotName.Length == 0 ? "default" : slotName) + "'.");
                    continue;
                }

                foreach (var binding in scope.Properties)
                {
                    if (!contract.Properties.TryGetValue(binding.PropertyName, out var typeName))
                    {
                        binding.TypeName = "object";
                        Report(Diagnostics.SqvDiagnostics.SQV0011_SlotPropertyMissing, binding.Position,
                            "Slot '" + (slotName.Length == 0 ? "default" : slotName) +
                            "' does not provide property '" + binding.PropertyName + "'.");
                        continue;
                    }
                    binding.TypeName = typeName;
                }
            }
        }

        void Report(DiagnosticDescriptor descriptor, int position, string message)
        {
            var source = SourceText.From(input.Content, Encoding.UTF8);
            position = Math.Max(0, Math.Min(position, source.Length));
            var span = new TextSpan(position, 0);
            context.ReportDiagnostic(Diagnostic.Create(
                descriptor,
                Location.Create(input.Path, span, source.Lines.GetLinePositionSpan(span)),
                message));
        }
    }

    private static Location CreateLocation(SqxInput input, int line, int column)
    {
        var source = SourceText.From(input.Content, Encoding.UTF8);
        var lineIndex = Math.Max(0, Math.Min(line - 1, source.Lines.Count - 1));
        var textLine = source.Lines[lineIndex];
        var position = Math.Min(textLine.End, textLine.Start + Math.Max(0, column - 1));
        var span = new TextSpan(position, 0);
        return Location.Create(input.Path, span, source.Lines.GetLinePositionSpan(span));
    }

    private static void ReportSemanticDiagnostics(
        SourceProductionContext context,
        Compilation compilation,
        SqxInput input,
        string generatedCode,
        IReadOnlyCollection<string> generatedTypes)
    {
        var parseOptions = compilation.SyntaxTrees.FirstOrDefault()?.Options as Microsoft.CodeAnalysis.CSharp.CSharpParseOptions;
        var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
            generatedCode,
            parseOptions,
            input.Path + ".semantic.g.cs");
        var semanticCompilation = compilation.AddSyntaxTrees(syntaxTree);
        var source = SourceText.From(input.Content, Encoding.UTF8);
        foreach (var diagnostic in semanticCompilation.GetDiagnostics()
                      .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Location.IsInSource))
        {
            if (IsMissingGeneratedTypeDiagnostic(diagnostic, generatedTypes)) continue;

            var mapped = diagnostic.Location.GetMappedLineSpan();
            if (!string.Equals(mapped.Path, input.Path, StringComparison.OrdinalIgnoreCase)) continue;
            var line = Math.Max(0, Math.Min(mapped.StartLinePosition.Line, source.Lines.Count - 1));
            var textLine = source.Lines[line];
            var column = Math.Max(0, mapped.StartLinePosition.Character);
            var position = Math.Min(textLine.End, textLine.Start + column);
            var span = new TextSpan(position, 0);
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.SqvDiagnostics.SQV0013_SemanticError,
                Location.Create(input.Path, span, source.Lines.GetLinePositionSpan(span)),
                diagnostic.GetMessage()));
        }
    }

    private static bool IsMissingGeneratedTypeDiagnostic(Diagnostic diagnostic, IReadOnlyCollection<string> generatedTypes)
    {
        if (diagnostic.Id != "CS0246") return false;
        var message = diagnostic.GetMessage();
        var firstQuote = message.IndexOfAny(new[] { '\'', '“' });
        if (firstQuote < 0) return false;
        var closingQuote = message[firstQuote] == '“' ? '”' : '\'';
        var secondQuote = message.IndexOf(closingQuote, firstQuote + 1);
        if (secondQuote <= firstQuote + 1) return false;
        var typeName = message.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
        return generatedTypes.Contains(typeName) || generatedTypes.Any(name => name.EndsWith("." + typeName, StringComparison.Ordinal));
    }

    private static uint StableHash(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in value)
                hash = (hash ^ character) * 16777619u;
            return hash;
        }
    }

    private sealed class SqxInput
    {
        public string Path { get; }
        public string Content { get; }
        public string Namespace { get; }

        public SqxInput(string path, string content, string namespaceName)
        {
            Path = path;
            Content = content;
            Namespace = namespaceName;
        }
    }

}
