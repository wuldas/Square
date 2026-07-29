using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Square.Compiler.Directives;
using Square.Compiler.Emit;
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

            var contracts = BuildPropContracts(compilation, files);
            foreach (var file in files)
                Generate(productionContext, file, contracts, catalog);
        });
    }

    private static void Generate(
        SourceProductionContext context,
        SqxInput input,
        IReadOnlyDictionary<string, PropContract[]> contracts,
        DirectiveCatalog catalog)
    {
        string code;
        try
        {
            var document = ParseDocument(input);
            ValidateRequiredProps(context, input, document, contracts);
            ValidateRefNames(context, input, document);
            code = DirectiveValidator.Validate(context, input.Path, input.Content, document, catalog)
                ? new ComponentEmitter(document, input.Namespace, catalog).Emit()
                : "// Generator error: unsupported directive shape\n// Path: " + input.Path;
        }
        catch (SqxParseException exception)
        {
            code = $"// Generator error: {exception.Message}\n// Path: {input.Path}";
            var source = SourceText.From(input.Content, Encoding.UTF8);
            var position = Math.Max(0, Math.Min(exception.Position, source.Length));
            var span = new TextSpan(position, 0);
            var descriptor = input.Path.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase)
                ? Diagnostics.SqvDiagnostics.Get(exception.DiagnosticId)
                : Diagnostics.SqxDiagnostics.SQX0001_SyntaxError;
            context.ReportDiagnostic(Diagnostic.Create(
                descriptor,
                Location.Create(input.Path, span, source.Lines.GetLinePositionSpan(span)),
                exception.Message));
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

    private static IReadOnlyDictionary<string, PropContract[]> BuildPropContracts(
        Compilation compilation,
        ImmutableArray<SqxInput> inputs)
    {
        var contracts = new Dictionary<string, PropContract[]>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            SqxDocument document;
            try
            {
                document = ParseDocument(input);
            }
            catch (SqxParseException)
            {
                continue;
            }

            var props = new Dictionary<string, PropContract>(StringComparer.OrdinalIgnoreCase);
            var script = ExtractScript(input.Content);
            if (script != null)
            {
                var matches = Regex.Matches(
                    script,
                    @"\[Prop(?:Attribute)?\s*(?:\((?<options>[^)]*)\))?\]\s*(?:public|internal|protected|private)?\s*(?<type>[A-Za-z_][A-Za-z0-9_<>?., ]*)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\{");
                foreach (Match match in matches)
                {
                    var options = match.Groups["options"].Value;
                    var prop = new PropContract(
                        match.Groups["name"].Value,
                        match.Groups["type"].Value.Trim(),
                        options.Contains("Required", StringComparison.OrdinalIgnoreCase) &&
                        options.Contains("true", StringComparison.OrdinalIgnoreCase));
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
                    var prop = new PropContract(
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

    private static bool IsPropAttribute(AttributeData attribute)
    {
        var type = attribute.AttributeClass;
        if (type == null) return false;
        var metadataName = type.ToDisplayString();
        return metadataName == "Square.Runtime.Binding.PropAttribute" ||
            type.Name is "PropAttribute" or "Prop";
    }

    private static SqxDocument ParseDocument(SqxInput input) =>
        input.Path.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase)
            ? SqvParser.Parse(input.Content, input.Path)
            : SqxParser.Parse(input.Content, input.Path);

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

    private static string ExtractScript(string source)
    {
        var start = source.IndexOf("<script", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        var openEnd = source.IndexOf('>', start);
        if (openEnd < 0) return null;
        var close = source.IndexOf("</script", openEnd, StringComparison.OrdinalIgnoreCase);
        return close < 0 ? null : source.Substring(openEnd + 1, close - openEnd - 1);
    }

    private static void ValidateRequiredProps(
        SourceProductionContext context,
        SqxInput input,
        SqxDocument document,
        IReadOnlyDictionary<string, PropContract[]> contracts)
    {
        var currentNamespace = string.IsNullOrWhiteSpace(document.Namespace)
            ? input.Namespace
            : document.Namespace;
        var scriptUsings = ExtractNamespaceUsings(document.ScriptCode);
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

    private static Location CreateLocation(SqxInput input, int line, int column)
    {
        var source = SourceText.From(input.Content, Encoding.UTF8);
        var lineIndex = Math.Max(0, Math.Min(line - 1, source.Lines.Count - 1));
        var textLine = source.Lines[lineIndex];
        var position = Math.Min(textLine.End, textLine.Start + Math.Max(0, column - 1));
        var span = new TextSpan(position, 0);
        return Location.Create(input.Path, span, source.Lines.GetLinePositionSpan(span));
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

    private sealed class PropContract
    {
        public string Name { get; }
        public string TypeName { get; }
        public bool Required { get; }

        public PropContract(string name, string typeName, bool required)
        {
            Name = name;
            TypeName = typeName;
            Required = required;
        }
    }
}
