using System.Collections.ObjectModel;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Square.Compiler.Parser;

namespace Square.Compiler.LanguageServices;

/// <summary>Shared compile-time metadata used by the generator and language services.</summary>
public sealed class TemplateCatalog
{
    public const string HtmlNamespaceUri = "http://www.w3.org/1999/xhtml";
    public const string SquareNamespaceUri = "urn:square:ui";
    public const string SvgNamespaceUri = "http://www.w3.org/2000/svg";
    public const string LocalNamespaceUri = "urn:square:local";

    private const string ExportAttributeName = "Square.UI.ElementExportAttribute";
    private const string OrderAttributeName = "Square.UI.ElementNamespaceOrderAttribute";
    private const string AliasAttributeName = "Square.UI.ElementNamespaceAliasAttribute";

    private static readonly IReadOnlyDictionary<string, string> BuiltInTypeNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["View"] = "Square.Controls.View",
            ["ScrollViewer"] = "Square.Controls.ScrollViewer",
            ["Popup"] = "Square.Controls.Popup",
            ["Dialog"] = "Square.Controls.Dialog",
            ["MenuBar"] = "Square.Controls.MenuBar",
            ["Menu"] = "Square.Controls.Menu",
            ["ContextMenu"] = "Square.Controls.ContextMenu",
            ["MenuItem"] = "Square.Controls.MenuItem",
            ["MenuSeparator"] = "Square.Controls.MenuSeparator",
            ["Text"] = "Square.Controls.Text",
            ["FontIcon"] = "Square.Controls.FontIcon",
            ["Splitter"] = "Square.Controls.Splitter",
            ["SplitContainer"] = "Square.Controls.SplitContainer",
            ["List"] = "Square.Controls.List",
            ["VirtualList"] = "Square.Controls.VirtualList",
            ["ListItem"] = "Square.Controls.ListItem",
            ["Tree"] = "Square.Controls.Tree",
            ["VirtualTree"] = "Square.Controls.VirtualTree",
            ["TreeItem"] = "Square.Controls.TreeItem",
            ["Swiper"] = "Square.Controls.Swiper",
            ["Button"] = "Square.Controls.Button",
            ["Input"] = "Square.Controls.Input",
            ["TextArea"] = "Square.Controls.TextArea",
            ["CheckBox"] = "Square.Controls.CheckBox",
            ["Radio"] = "Square.Controls.Radio",
            ["Select"] = "Square.Controls.Select",
            ["Image"] = "Square.Controls.Image",
            ["Canvas"] = "Square.Controls.Canvas",
            ["TitleBar"] = "Square.Controls.TitleBar",
            ["Link"] = "Square.Controls.Link",
            ["Table"] = "Square.Controls.Table",
            ["InlineTable"] = "Square.Controls.InlineTable",
            ["TableRowGroup"] = "Square.Controls.TableRowGroup",
            ["TableHeaderGroup"] = "Square.Controls.TableHeaderGroup",
            ["TableFooterGroup"] = "Square.Controls.TableFooterGroup",
            ["TableRow"] = "Square.Controls.TableRow",
            ["TableCell"] = "Square.Controls.TableCell",
            ["TableCaption"] = "Square.Controls.TableCaption",
            ["UI"] = "Square.UI.UIRootElement",
            ["Head"] = "Square.UI.UIHeadElement",
            ["Body"] = "Square.UI.UIBodyElement",
            ["svg"] = "Square.UI.Svg.SVGSVGElement",
            ["g"] = "Square.UI.Svg.SVGGElement",
            ["path"] = "Square.UI.Svg.SVGPathElement",
            ["rect"] = "Square.UI.Svg.SVGRectElement",
            ["circle"] = "Square.UI.Svg.SVGCircleElement",
            ["ellipse"] = "Square.UI.Svg.SVGEllipseElement",
            ["line"] = "Square.UI.Svg.SVGLineElement",
            ["polyline"] = "Square.UI.Svg.SVGPolylineElement",
            ["polygon"] = "Square.UI.Svg.SVGPolygonElement"
        };

    private static readonly IReadOnlyDictionary<string, string> PropertyAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "Id", ["class"] = "class", ["style"] = "style", ["ref"] = "ref", ["slot"] = "slot",
            ["key"] = "key", ["when"] = "when", ["each"] = "each", ["fallback"] = "fallback", ["name"] = "name",
            ["text"] = "TextContent", ["glyph"] = "Glyph", ["icon"] = "Icon", ["font-family"] = "FontFamily",
            ["minimum"] = "Minimum", ["maximum"] = "Maximum", ["splitter-thickness"] = "SplitterThickness",
            ["seamless"] = "IsSeamless", ["vertical"] = "IsVertical", ["reversed"] = "IsReversed",
            ["value"] = "Value", ["checked"] = "IsChecked", ["disabled"] = "IsDisabled",
            ["placeholder"] = "Placeholder", ["source"] = "Source", ["image"] = "ImageContent", ["group"] = "GroupName",
            ["shortcut"] = "ShortcutText", ["checkable"] = "IsCheckable", ["stays-open-on-click"] = "StaysOpenOnClick",
            ["options"] = "Options", ["items"] = "Items", ["selected-index"] = "SelectedIndex", ["item-height"] = "ItemHeight",
            ["overscan-count"] = "OverscanCount", ["indent-size"] = "IndentSize", ["expanded"] = "IsExpanded",
            ["loop"] = "Loop", ["to"] = "To", ["href"] = "Href", ["marker"] = "Marker", ["replace"] = "Replace",
            ["color"] = "Color", ["background"] = "Background", ["underline"] = "Underline", ["type"] = "Type",
            ["viewbox"] = "ViewBox", ["x"] = "X", ["y"] = "Y", ["width"] = "Width", ["height"] = "Height",
            ["rx"] = "RadiusX", ["ry"] = "RadiusY", ["cx"] = "CenterX", ["cy"] = "CenterY", ["r"] = "Radius",
            ["x1"] = "X1", ["y1"] = "Y1", ["x2"] = "X2", ["y2"] = "Y2", ["points"] = "Points", ["d"] = "Data",
            ["transform"] = "Transform", ["fill"] = "Fill", ["stroke"] = "Stroke", ["stroke-width"] = "StrokeWidth",
            ["opacity"] = "Opacity", ["fill-opacity"] = "FillOpacity", ["stroke-opacity"] = "StrokeOpacity"
        };

    private static readonly string[] CommonPropertyNames = { "id", "class", "style", "ref", "slot" };
    private static readonly string[] UiElementPropertyNames = { "width", "height", "disabled" };
    private static readonly HashSet<string> NonUiElementTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "Show", "For", "Index", "Switch", "Match", "Slot", "Outlet",
        "svg", "g", "path", "rect", "circle", "ellipse", "line", "polyline", "polygon"
    };

    private static readonly IReadOnlyDictionary<string, string[]> TagPropertyNames =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Text"] = new[] { "text", "color" }, ["FontIcon"] = new[] { "glyph", "color", "font-family" },
            ["Button"] = new[] { "text", "background" }, ["Input"] = new[] { "value", "placeholder", "type" },
            ["TextArea"] = new[] { "value", "placeholder" }, ["CheckBox"] = new[] { "text", "checked" },
            ["Radio"] = new[] { "text", "checked", "group" }, ["Select"] = new[] { "options", "value", "placeholder" },
            ["Image"] = new[] { "source", "image" },
            ["Splitter"] = new[] { "minimum", "maximum", "value", "vertical", "reversed" },
            ["SplitContainer"] = new[] { "minimum", "maximum", "value", "vertical", "splitter-thickness", "seamless" },
            ["List"] = new[] { "items", "selected-index" },
            ["VirtualList"] = new[] { "selected-index", "item-height", "overscan-count" },
            ["VirtualTree"] = new[] { "item-height", "overscan-count", "indent-size" },
            ["TreeItem"] = new[] { "text", "expanded", "color" }, ["ListItem"] = new[] { "text", "marker", "color" },
            ["Swiper"] = new[] { "selected-index", "loop" },
            ["MenuItem"] = new[] { "text", "icon", "group", "shortcut", "checkable", "checked", "stays-open-on-click", "disabled" },
            ["Link"] = new[] { "text", "href", "underline", "color" }, ["Show"] = new[] { "when", "fallback" },
            ["For"] = new[] { "each", "key", "fallback" }, ["Index"] = new[] { "each", "fallback" },
            ["Switch"] = new[] { "fallback" }, ["Match"] = new[] { "when" },
            ["Slot"] = new[] { "name", "fallback" }, ["Outlet"] = new[] { "name", "fallback" },
            ["svg"] = new[] { "viewbox", "width", "height", "fill", "stroke", "opacity" },
            ["g"] = new[] { "transform", "fill", "stroke", "opacity" },
            ["path"] = new[] { "d", "transform", "fill", "stroke", "stroke-width", "opacity" },
            ["rect"] = new[] { "x", "y", "width", "height", "rx", "ry", "fill", "stroke", "opacity" },
            ["circle"] = new[] { "cx", "cy", "r", "fill", "stroke", "opacity" },
            ["ellipse"] = new[] { "cx", "cy", "rx", "ry", "fill", "stroke", "opacity" },
            ["line"] = new[] { "x1", "y1", "x2", "y2", "stroke", "stroke-width", "opacity" },
            ["polyline"] = new[] { "points", "fill", "stroke", "stroke-width", "opacity" },
            ["polygon"] = new[] { "points", "fill", "stroke", "stroke-width", "opacity" }
        };

    private static readonly HashSet<string> TextContentTags = new(StringComparer.OrdinalIgnoreCase)
        { "Text", "Button", "Link", "ListItem", "TreeItem" };
    private static readonly HashSet<string> BooleanPropertyNames = new(StringComparer.OrdinalIgnoreCase)
        { "seamless", "vertical", "reversed", "checked", "disabled", "checkable", "stays-open-on-click", "expanded", "loop", "replace", "underline" };
    private static readonly (string Name, string CanonicalName)[] StandardEvents =
    {
        ("pointerdown", "onPointerDown"), ("pointerup", "onPointerUp"), ("pointermove", "onPointerMove"),
        ("wheel", "onWheel"), ("scroll", "onScroll"), ("keydown", "onKeyDown"), ("keyup", "onKeyUp"),
        ("textinput", "onTextInput"), ("focusin", "onFocusIn"), ("focusout", "onFocusOut"),
        ("focus", "onFocus"), ("blur", "onBlur"), ("click", "onClick"), ("contextmenu", "onContextMenu"),
        ("change", "onChange"), ("selectionchange", "onSelectionChange"), ("input", "onInput"), ("requestframe", "onRequestFrame")
    };

    private readonly IReadOnlyDictionary<string, TemplateComponentDescriptor> _builtIns;
    private readonly IReadOnlyList<TemplateComponentDescriptor> _exports;
    private readonly IReadOnlyList<TemplateComponentDescriptor> _localComponents;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _prefixes;
    private readonly IReadOnlyList<string> _namespaceOrder;
    private readonly Compilation _compilation;
    private readonly IReadOnlyDictionary<string, INamedTypeSymbol> _exportedSymbols;
    private readonly Dictionary<INamedTypeSymbol, TemplateComponentContracts> _contracts;
    private readonly object _contractGate = new();

    private TemplateCatalog(
        IReadOnlyDictionary<string, TemplateComponentDescriptor> builtIns,
        IReadOnlyList<TemplateComponentDescriptor> exports,
        IReadOnlyList<TemplateComponentDescriptor> localComponents,
        IReadOnlyDictionary<string, IReadOnlyList<string>> prefixes,
        IReadOnlyList<string> namespaceOrder,
        IReadOnlyList<SquareDiagnostic> diagnostics,
        Compilation compilation,
        Dictionary<INamedTypeSymbol, TemplateComponentContracts> contracts,
        IReadOnlyDictionary<string, INamedTypeSymbol> exportedSymbols)
    {
        _builtIns = builtIns;
        _exports = exports;
        _localComponents = localComponents;
        _prefixes = prefixes;
        _namespaceOrder = namespaceOrder;
        Diagnostics = diagnostics;
        _compilation = compilation;
        _contracts = contracts ?? new Dictionary<INamedTypeSymbol, TemplateComponentContracts>(SymbolEqualityComparer.Default);
        _exportedSymbols = exportedSymbols ?? new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
    }

    public static TemplateCatalog BuiltIn { get; } = CreateBuiltIn();
    public IReadOnlyList<SquareDiagnostic> Diagnostics { get; }
    internal Compilation AnalysisCompilation => _compilation;
    public IReadOnlyCollection<TemplateEventDescriptor> Events { get; } =
        new ReadOnlyCollection<TemplateEventDescriptor>(StandardEvents.Select(item => new TemplateEventDescriptor(item.Name, item.CanonicalName)).ToArray());
    public IReadOnlyCollection<TemplatePropertyDescriptor> Properties { get; } =
        new ReadOnlyCollection<TemplatePropertyDescriptor>(PropertyAliases.Select(pair =>
            new TemplatePropertyDescriptor(pair.Key, pair.Value, GetPropertyValueKind(pair.Key))).ToArray());
    public IReadOnlyCollection<TemplateComponentDescriptor> Components =>
        new ReadOnlyCollection<TemplateComponentDescriptor>(_builtIns.Values.Concat(_exports).Concat(_localComponents)
            .GroupBy(ComponentIdentity, StringComparer.Ordinal).Select(group => group.First()).ToArray());

    public static TemplateCatalog FromCompilation(
        Compilation compilation,
        IEnumerable<(string Path, string Content, string Namespace)> inputs) =>
        FromCompilation(compilation, ParseInputs(inputs));

    internal static TemplateCatalog FromCompilation(
        Compilation compilation,
        IReadOnlyList<(string Path, string Content, string Namespace, SquareParseResult Parse)> inputs)
    {
        if (compilation == null) throw new ArgumentNullException(nameof(compilation));
        var inputArray = inputs ?? Array.Empty<(string Path, string Content, string Namespace, SquareParseResult Parse)>();
        return CreateFromCompilation(
            AddGeneratedComponentDeclarations(compilation, inputArray),
            new TemplateSemanticAnalyzer().BuildGeneratedComponents(inputArray));
    }

    /// <summary>把每个输入解析一次，供声明注入、组件发现与发射共用。</summary>
    internal static IReadOnlyList<(string Path, string Content, string Namespace, SquareParseResult Parse)> ParseInputs(
        IEnumerable<(string Path, string Content, string Namespace)> inputs) =>
        (inputs ?? Array.Empty<(string Path, string Content, string Namespace)>())
            .Select(input => (input.Path, input.Content, input.Namespace,
                SquareDocumentService.ParseSyntax(input.Content, input.Path)))
            .ToArray();

    internal static TemplateCatalog FromOutputCompilation(
        Compilation outputCompilation,
        IReadOnlyDictionary<string, TemplateComponentDescriptor> generatedComponents)
    {
        if (outputCompilation == null) throw new ArgumentNullException(nameof(outputCompilation));
        return CreateFromCompilation(
            outputCompilation,
            generatedComponents ?? new Dictionary<string, TemplateComponentDescriptor>(StringComparer.Ordinal));
    }

    private static TemplateCatalog CreateFromCompilation(
        Compilation analysisCompilation,
        IReadOnlyDictionary<string, TemplateComponentDescriptor> generatedComponents)
    {
        var builtIns = CreateBuiltInDescriptorsWithSourcePaths(analysisCompilation);
        var diagnostics = new List<SquareDiagnostic>();
        var exports = new List<TemplateComponentDescriptor>();
        var elementSymbol = analysisCompilation.GetTypeByMetadataName("Square.UI.Element");
        var frameworkAssembly = elementSymbol?.ContainingAssembly;
        var exportAttribute = frameworkAssembly?.GetTypeByMetadataName(ExportAttributeName);
        var orderAttribute = frameworkAssembly?.GetTypeByMetadataName(OrderAttributeName);
        var aliasAttribute = frameworkAssembly?.GetTypeByMetadataName(AliasAttributeName);

        var exportedSymbols = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        var declarations = new Dictionary<string, AttributeData>(StringComparer.Ordinal);
        if (elementSymbol != null && exportAttribute != null)
        {
            foreach (var reference in analysisCompilation.References)
            {
                if (analysisCompilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
                    ReadExports(assembly, exportAttribute, elementSymbol, exports, diagnostics, exportedSymbols, declarations);
            }
            ReadExports(analysisCompilation.Assembly, exportAttribute, elementSymbol, exports, diagnostics, exportedSymbols, declarations);
        }

        var aliases = ReadAliases(analysisCompilation.Assembly, aliasAttribute, exports, diagnostics);
        var order = ReadOrder(analysisCompilation.Assembly, orderAttribute, exports, diagnostics);
        ValidateExportIdentities(exports, declarations, diagnostics);
        ValidateCrossAssemblyTypeNames(exports, declarations, diagnostics);
        var prefixes = BuildPrefixes(exports, aliases, declarations, diagnostics);
        var distinctExports = exports.GroupBy(ComponentIdentity, StringComparer.Ordinal)
            .Select(group => group.OrderBy(item => item.Prefix, StringComparer.OrdinalIgnoreCase).First())
            .ToArray();
        exports.Clear();
        exports.AddRange(distinctExports);

        var exportedTypes = new HashSet<string>(exports.Select(ComponentAssemblyTypeIdentity), StringComparer.Ordinal);
        var locals = BuildLocalComponents(analysisCompilation, exportedTypes).ToList();
        for (var index = 0; index < exports.Count; index++)
        {
            var component = exports[index];
            if (!component.AssemblyName.Equals(analysisCompilation.Assembly.Identity.Name, StringComparison.Ordinal) ||
                !generatedComponents.TryGetValue(component.TypeMetadataName, out var generated)) continue;
            exports[index] = CopyWithSourcePath(component, generated.SourcePath);
        }
        for (var index = 0; index < locals.Count; index++)
        {
            var local = locals[index];
            if (!generatedComponents.TryGetValue(local.TypeMetadataName, out var generated)) continue;
            locals[index] = CopyWithSourcePath(local, generated.SourcePath);
        }
        var contractCache = new Dictionary<INamedTypeSymbol, TemplateComponentContracts>(SymbolEqualityComparer.Default);
        var semanticAnalyzer = new TemplateSemanticAnalyzer();
        foreach (var component in exports.Concat(locals)
                     .GroupBy(ComponentAssemblyTypeIdentity, StringComparer.Ordinal).Select(group => group.First()))
        {
            var symbol = FindComponentSymbol(analysisCompilation, component, exportAttribute, exportedSymbols);
            if (symbol == null || contractCache.ContainsKey(symbol)) continue;
            var contracts = semanticAnalyzer.BuildComponentContracts(
                analysisCompilation, symbol, component.Kind != TemplateElementKind.ClrScoped);
            contractCache.Add(symbol, contracts);
            diagnostics.AddRange(contracts.Diagnostics);
        }
        var frozenDiagnostics = diagnostics.GroupBy(diagnostic =>
                diagnostic.Id + "\0" + diagnostic.SourcePath + "\0" + diagnostic.Range.Offset + "\0" +
                diagnostic.Range.Length + "\0" + diagnostic.Message, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(diagnostic => diagnostic.SourcePath, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Range.Offset)
            .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ToArray();

        return new TemplateCatalog(
            builtIns,
            new ReadOnlyCollection<TemplateComponentDescriptor>(exports.OrderBy(ComponentSortKey, StringComparer.Ordinal).ToArray()),
            new ReadOnlyCollection<TemplateComponentDescriptor>(locals.OrderBy(ComponentSortKey, StringComparer.Ordinal).ToArray()),
            prefixes,
            order,
            new ReadOnlyCollection<SquareDiagnostic>(frozenDiagnostics),
            analysisCompilation,
            contractCache,
            exportedSymbols);
    }

    private static TemplateComponentDescriptor CopyWithSourcePath(TemplateComponentDescriptor component, string sourcePath) =>
        new(component.TagName, component.TypeName, component.TypeMetadataName, component.NamespaceUri, component.Prefix,
            component.LocalName, component.AssemblyName, sourcePath, component.Kind, component.IsBuiltIn,
            component.RequiresBuildAfterAttach, component.IsTextContentElement, component.IsSlotHost);

    public bool TryGetBuiltInComponent(string tagName, out TemplateComponentDescriptor descriptor)
    {
        descriptor = null;
        return !string.IsNullOrWhiteSpace(tagName) && tagName.IndexOf(':') < 0 &&
               _builtIns.TryGetValue(tagName, out descriptor);
    }

    public IReadOnlyList<TemplatePropDescriptor> GetProps(TemplateComponentDescriptor component) =>
        GetContracts(component).Props;

    public IReadOnlyList<TemplateComponentEventDescriptor> GetEvents(TemplateComponentDescriptor component) =>
        GetContracts(component).Events;

    public IReadOnlyDictionary<string, TemplateSlotDescriptor> GetSlots(TemplateComponentDescriptor component) =>
        GetContracts(component).Slots;

    internal IReadOnlyList<SquareDiagnostic> GetContractDiagnostics(TemplateComponentDescriptor component) =>
        GetContracts(component).Diagnostics;

    internal IReadOnlyList<string> GetEventContractDeclarations(TemplateComponentDescriptor component) =>
        GetContracts(component).EventContractDeclarations;

    internal INamedTypeSymbol GetComponentSymbol(TemplateComponentDescriptor component)
    {
        if (component == null || _compilation == null) return null;
        var frameworkAssembly = _compilation.GetTypeByMetadataName("Square.UI.Element")?.ContainingAssembly;
        return FindComponentSymbol(_compilation, component, frameworkAssembly?.GetTypeByMetadataName(ExportAttributeName), _exportedSymbols);
    }

    private TemplateComponentContracts GetContracts(TemplateComponentDescriptor component)
    {
        var symbol = GetComponentSymbol(component);
        if (symbol == null) return TemplateComponentContracts.Empty;
        lock (_contractGate)
        {
            if (_contracts.TryGetValue(symbol, out var contracts)) return contracts;
            var exported = _exports.Any(item =>
                ComponentAssemblyTypeIdentity(item) == ComponentAssemblyTypeIdentity(component) && item.TypeName == component.TypeName);
            contracts = new TemplateSemanticAnalyzer().BuildComponentContracts(_compilation, symbol, exported);
            _contracts.Add(symbol, contracts);
            return contracts;
        }
    }

    public IReadOnlyList<string> GetQualifiedNames(TemplateComponentDescriptor component)
    {
        if (component == null) return Array.Empty<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (component.Kind == TemplateElementKind.ClrScoped) names.Add("local:" + component.TypeName);
        else
        {
            foreach (var pair in _prefixes.Where(pair => pair.Value.Count == 1 && pair.Value.Contains(component.NamespaceUri, StringComparer.Ordinal)))
                names.Add(pair.Key + ":" + component.LocalName);
        }
        var exact = ResolveExactClr(component.TypeName);
        if (exact.Status == TemplateElementResolutionStatus.Resolved &&
            ComponentAssemblyTypeIdentity(exact.Component) == ComponentAssemblyTypeIdentity(component))
            names.Add("global::" + component.TypeName);
        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public TemplateElementResolution ResolveComponent(string tagName, TemplateResolutionContext context)
    {
        context ??= new TemplateResolutionContext(string.Empty, Array.Empty<string>());
        var parsedName = TemplateElementName.Parse(tagName, false);
        if (parsedName.Status != TemplateElementNameStatus.Valid)
            return TemplateElementResolution.Failed(TemplateElementResolutionStatus.InvalidName);
        if (parsedName.Kind == TemplateElementNameKind.ClrExact)
            return ResolveExactClr(parsedName.ClrTypeName);
        if (parsedName.Kind == TemplateElementNameKind.Local)
            return SelectLocal(ResolveLocalCandidates(parsedName.ClrTypeName, context));
        if (parsedName.Kind == TemplateElementNameKind.Qualified)
        {
            var prefix = parsedName.Prefix;
            var localName = parsedName.LocalName;
            if (!_prefixes.TryGetValue(prefix, out var namespaceUris))
                return TemplateElementResolution.Failed(TemplateElementResolutionStatus.UnknownPrefix);
            var candidates = _exports.Where(component => namespaceUris.Contains(component.NamespaceUri, StringComparer.Ordinal) &&
                    component.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
                .Concat(_builtIns.Values.Where(component => namespaceUris.Contains(component.NamespaceUri, StringComparer.Ordinal) &&
                    component.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(ComponentSortKey, StringComparer.Ordinal).ToArray();
            var orderedUris = namespaceUris.OrderBy(uri => uri, StringComparer.Ordinal).ToArray();
            if (candidates.Length == 1 && namespaceUris.Count == 1)
                return TemplateElementResolution.Resolved(candidates[0]);
            if (candidates.Length == 0)
                return TemplateElementResolution.Failed(TemplateElementResolutionStatus.UnknownElement, candidates, orderedUris);
            return TemplateElementResolution.Failed(TemplateElementResolutionStatus.Ambiguous, candidates, orderedUris);
        }

        var unqualifiedName = parsedName.LocalName;
        if (_builtIns.TryGetValue(unqualifiedName, out var builtIn)) return TemplateElementResolution.Resolved(builtIn);

        var extensionCandidates = _exports.Where(component => component.LocalName.Equals(unqualifiedName, StringComparison.OrdinalIgnoreCase)).ToList();
        extensionCandidates.AddRange(ResolveLocalCandidates(unqualifiedName, context));
        if (extensionCandidates.Count == 0) return TemplateElementResolution.Failed(TemplateElementResolutionStatus.UnknownElement);
        if (extensionCandidates.Count == 1) return TemplateElementResolution.Resolved(extensionCandidates[0]);

        var candidateUris = extensionCandidates
            .Select(candidate => candidate.Kind == TemplateElementKind.ClrScoped ? LocalNamespaceUri : candidate.NamespaceUri)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(uri => uri, StringComparer.Ordinal)
            .ToArray();
        var selectedNamespace = _namespaceOrder.FirstOrDefault(uri => candidateUris.Contains(uri, StringComparer.Ordinal));
        if (selectedNamespace == null)
            return TemplateElementResolution.Failed(
                TemplateElementResolutionStatus.Ambiguous,
                extensionCandidates.OrderBy(ComponentSortKey, StringComparer.Ordinal),
                candidateUris);
        var selected = extensionCandidates.Where(candidate =>
            (candidate.Kind == TemplateElementKind.ClrScoped ? LocalNamespaceUri : candidate.NamespaceUri).Equals(selectedNamespace, StringComparison.Ordinal)).ToArray();
        return selected.Length == 1
            ? TemplateElementResolution.Resolved(selected[0])
            : TemplateElementResolution.Failed(
                TemplateElementResolutionStatus.Ambiguous,
                selected.OrderBy(ComponentSortKey, StringComparer.Ordinal),
                candidateUris);
    }

    public string MapPropertyName(string markupName) =>
        !string.IsNullOrWhiteSpace(markupName) && PropertyAliases.TryGetValue(markupName, out var propertyName) ? propertyName : markupName;

    public IReadOnlyCollection<TemplatePropertyDescriptor> GetPropertiesForTag(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName)) return Properties;
        var names = new HashSet<string>(CommonPropertyNames, StringComparer.OrdinalIgnoreCase);
        if (!NonUiElementTags.Contains(tagName)) names.UnionWith(UiElementPropertyNames);
        if (TagPropertyNames.TryGetValue(tagName, out var tagProperties)) names.UnionWith(tagProperties);
        return Properties.Where(property => names.Contains(property.Name)).ToArray();
    }

    public TemplatePropertyDescriptor GetProperty(string name) =>
        Properties.FirstOrDefault(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private TemplateElementResolution ResolveExactClr(string typeName)
    {
        var builtIn = _builtIns.Values.FirstOrDefault(component => component.TypeName.Equals(typeName, StringComparison.Ordinal));
        if (builtIn != null) return TemplateElementResolution.Resolved(builtIn);
        var known = DistinctComponents(_exports.Concat(_localComponents)
            .Where(component => component.TypeName.Equals(typeName, StringComparison.Ordinal)));
        if (known.Count == 1) return TemplateElementResolution.Resolved(known[0]);
        if (known.Count > 1) return TemplateElementResolution.Failed(TemplateElementResolutionStatus.Ambiguous, known);
        var symbol = GetTypeByCSharpName(_compilation, typeName);
        var descriptor = symbol == null ? null : CreateClrDescriptor(_compilation, symbol);
        return descriptor == null
            ? TemplateElementResolution.Failed(TemplateElementResolutionStatus.UnknownElement)
            : TemplateElementResolution.Resolved(descriptor);
    }

    private IReadOnlyList<TemplateComponentDescriptor> ResolveLocalCandidates(string name, TemplateResolutionContext context)
    {
        var result = new List<TemplateComponentDescriptor>();
        if (name.IndexOf('.') >= 0)
        {
            AddClr(name);
            return DistinctComponents(result);
        }

        AddClr(context.CurrentNamespace.Length == 0 ? name : context.CurrentNamespace + "." + name);
        if (result.Count > 0) return DistinctComponents(result);
        foreach (var usingNamespace in context.UsingNamespaces) AddClr(usingNamespace + "." + name);
        if (result.Count > 0) return DistinctComponents(result);
        result.AddRange(_localComponents.Where(component => component.LocalName.Equals(name, StringComparison.Ordinal)));
        return DistinctComponents(result);

        void AddClr(string fullName)
        {
            var symbol = GetTypeByCSharpName(_compilation, fullName);
            var descriptor = symbol == null ? null : CreateClrDescriptor(_compilation, symbol);
            if (descriptor != null && !_exports.Any(exported =>
                    exported.AssemblyName == descriptor.AssemblyName && exported.TypeName == descriptor.TypeName))
                result.Add(_localComponents.FirstOrDefault(local =>
                    ComponentAssemblyTypeIdentity(local) == ComponentAssemblyTypeIdentity(descriptor) &&
                    local.TypeName == descriptor.TypeName) ?? descriptor);
        }
    }

    private static TemplateElementResolution SelectLocal(IReadOnlyList<TemplateComponentDescriptor> candidates) =>
        candidates.Count == 1
            ? TemplateElementResolution.Resolved(candidates[0])
            : TemplateElementResolution.Failed(candidates.Count == 0
                ? TemplateElementResolutionStatus.UnknownElement
                : TemplateElementResolutionStatus.Ambiguous, candidates);

    private static IReadOnlyList<TemplateComponentDescriptor> DistinctComponents(IEnumerable<TemplateComponentDescriptor> components) =>
        components.GroupBy(component => ComponentAssemblyTypeIdentity(component) + "\0" + component.TypeName, StringComparer.Ordinal)
            .Select(group => group.First()).OrderBy(ComponentSortKey, StringComparer.Ordinal).ToArray();

    private static TemplateCatalog CreateBuiltIn()
    {
        var prefixes = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["html"] = new[] { HtmlNamespaceUri }, ["ui"] = new[] { SquareNamespaceUri },
            ["svg"] = new[] { SvgNamespaceUri }, ["local"] = new[] { LocalNamespaceUri }
        };
        return new TemplateCatalog(CreateBuiltInDescriptors(), Array.Empty<TemplateComponentDescriptor>(),
            Array.Empty<TemplateComponentDescriptor>(), prefixes, Array.Empty<string>(), Array.Empty<SquareDiagnostic>(), null,
            new Dictionary<INamedTypeSymbol, TemplateComponentContracts>(SymbolEqualityComparer.Default),
            new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal));
    }

    private static IReadOnlyDictionary<string, TemplateComponentDescriptor> CreateBuiltInDescriptors()
    {
        var components = new Dictionary<string, TemplateComponentDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in BuiltInTypeNames)
        {
            var svg = pair.Value.StartsWith("Square.UI.Svg.", StringComparison.Ordinal);
            var slotHost = pair.Key.Equals("TitleBar", StringComparison.OrdinalIgnoreCase) ||
                           pair.Key.Equals("SplitContainer", StringComparison.OrdinalIgnoreCase);
            components[pair.Key] = new TemplateComponentDescriptor(
                pair.Key, pair.Value, pair.Value, svg ? SvgNamespaceUri : SquareNamespaceUri, svg ? "svg" : "ui", pair.Key,
                "Square", string.Empty, svg ? TemplateElementKind.Svg : TemplateElementKind.Square, true,
                slotHost, TextContentTags.Contains(pair.Key), slotHost);
        }
        return new ReadOnlyDictionary<string, TemplateComponentDescriptor>(components);
    }

    /// <summary>
    /// 内置控件描述符默认没有源路径；当分析编译里能解析到类型符号且源码可用（工程引用）时补上，
    /// 使 <c>textDocument/definition</c> 能跳到 <c>View</c>/<c>Button</c> 这类框架控件。
    /// </summary>
    private static IReadOnlyDictionary<string, TemplateComponentDescriptor> CreateBuiltInDescriptorsWithSourcePaths(
        Compilation compilation)
    {
        var descriptors = CreateBuiltInDescriptors();
        if (compilation == null) return descriptors;
        var withPaths = new Dictionary<string, TemplateComponentDescriptor>(descriptors.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in descriptors)
        {
            var sourcePath = compilation.GetTypeByMetadataName(pair.Value.TypeMetadataName)
                ?.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree.FilePath;
            withPaths[pair.Key] = string.IsNullOrWhiteSpace(sourcePath)
                ? pair.Value
                : CopyWithSourcePath(pair.Value, sourcePath);
        }
        return new ReadOnlyDictionary<string, TemplateComponentDescriptor>(withPaths);
    }

    internal static Compilation AddScriptDeclarations(
        Compilation compilation,
        IReadOnlyList<(string Path, string Content, string Namespace, SquareParseResult Parse)> inputs) =>
        AddGeneratedComponentDeclarations(compilation, inputs);

    private static Compilation AddGeneratedComponentDeclarations(
        Compilation compilation,
        IEnumerable<(string Path, string Content, string Namespace, SquareParseResult Parse)> inputs)
    {
        var trees = new List<SyntaxTree>();
        foreach (var input in inputs)
        {
            var parsed = input.Parse;
            var document = parsed.ParsedSqxDocument;
            if (!parsed.IsSuccess || document == null) continue;
            var namespaceName = string.IsNullOrWhiteSpace(document.Namespace) ? input.Namespace : document.Namespace;
            var metadataName = string.IsNullOrWhiteSpace(namespaceName) ? document.Name : namespaceName + "." + document.Name;
            if (HasMaterializedGeneratedType(compilation, metadataName)) continue;

            var sources = new StringBuilder();
            foreach (var usingDirective in TemplateScriptDefaults.UsingDirectives)
                sources.Append("using ").Append(usingDirective).AppendLine(";");
            var script = document.Syntax?.Script?.CSharp;
            if (script != null)
                foreach (var usingDirective in script.Usings)
                    sources.AppendLine(usingDirective.WithoutTrivia().NormalizeWhitespace().ToFullString());
            if (!string.IsNullOrWhiteSpace(namespaceName)) sources.Append("namespace ").Append(namespaceName).AppendLine(" {");
            sources.Append(string.IsNullOrWhiteSpace(document.Access) ? "public" : document.Access)
                .Append(" partial class ").Append(document.Name).AppendLine(" : global::Square.UI.UIElement {");
            if (script != null)
            {
                var escapedPath = input.Path.Replace("\\", "\\\\");
                var baseLine = 1;
                var scriptOffset = document.Syntax?.Script?.ContentRange.Offset ?? 0;
                for (var index = 0; index < scriptOffset && index < input.Content.Length; index++)
                    if (input.Content[index] == '\n') baseLine++;
                foreach (var member in script.Members)
                {
                    var memberLine = baseLine + member.GetLocation().GetLineSpan().StartLinePosition.Line;
                    sources.Append("#line ").Append(memberLine).Append(" \"").Append(escapedPath).AppendLine("\"");
                    sources.AppendLine(member.ToFullString());
                }
                sources.AppendLine("#line default");
            }
            sources.AppendLine("}");
            if (!string.IsNullOrWhiteSpace(namespaceName)) sources.AppendLine("}");
            var options = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;
            trees.Add(CSharpSyntaxTree.ParseText(sources.ToString(), options,
                "Square.TemplateCatalog.analysis." + trees.Count + ".g.cs"));
        }
        return trees.Count == 0 ? compilation : compilation.AddSyntaxTrees(trees);
    }

    private static bool HasMaterializedGeneratedType(Compilation compilation, string metadataName)
    {
        var existing = compilation.GetTypeByMetadataName(metadataName);
        if (existing == null) return false;
        foreach (var reference in existing.DeclaringSyntaxReferences)
        {
            var path = reference.SyntaxTree.FilePath ?? string.Empty;
            if (path.StartsWith("Square.TemplateCatalog.analysis.", StringComparison.Ordinal) &&
                path.EndsWith(".g.cs", StringComparison.Ordinal)) return true;
            if (path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) &&
                path.IndexOf("SqxGenerator", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static void ReadExports(
        IAssemblySymbol assembly,
        INamedTypeSymbol exportAttribute,
        INamedTypeSymbol elementSymbol,
        ICollection<TemplateComponentDescriptor> exports,
        ICollection<SquareDiagnostic> diagnostics,
        IDictionary<string, INamedTypeSymbol> exportedSymbols,
        IDictionary<string, AttributeData> declarations)
    {
        foreach (var attribute in assembly.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, exportAttribute)) continue;
            if (attribute.ConstructorArguments.Length != 4 ||
                attribute.ConstructorArguments[0].Value is not string namespaceUri ||
                attribute.ConstructorArguments[1].Value is not string prefix ||
                attribute.ConstructorArguments[2].Value is not string localName ||
                attribute.ConstructorArguments[3].Value is not INamedTypeSymbol elementType)
            {
                AddDiagnostic(diagnostics, "SQXE001", "ElementExport has invalid constructor arguments.", attribute);
                continue;
            }
            if (string.IsNullOrWhiteSpace(namespaceUri) || IsReservedNamespace(namespaceUri) ||
                IsReservedPrefix(prefix) || !IsMarkupName(prefix) || !IsMarkupName(localName))
            {
                AddDiagnostic(diagnostics, "SQXE001", "Invalid or reserved element export '" + prefix + ":" + localName + "' for URI '" + namespaceUri + "'.", attribute);
                continue;
            }
            if (!IsValidExportType(elementType, elementSymbol))
            {
                AddDiagnostic(diagnostics, "SQXE001", "Exported type '" + elementType.ToDisplayString() + "' must be public, non-abstract, closed, derive from Square.UI.Element, and have a public parameterless constructor.", attribute);
                continue;
            }
            var sourcePath = elementType.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree.FilePath ?? string.Empty;
            var uiElementSymbol = elementSymbol.ContainingAssembly.GetTypeByMetadataName("Square.UI.UIElement");
            var descriptor = new TemplateComponentDescriptor(
                localName, ToCSharpTypeName(elementType), GetMetadataName(elementType), namespaceUri, prefix, localName,
                elementType.ContainingAssembly.Identity.Name, sourcePath, TemplateElementKind.Extension, false, true, false,
                DerivesFrom(elementType, uiElementSymbol));
            exports.Add(descriptor);
            exportedSymbols[ComponentAssemblyTypeIdentity(descriptor)] = elementType;
            declarations[ComponentAssemblyTypeIdentity(descriptor)] = attribute;
        }
    }

    private static IReadOnlyDictionary<string, string> ReadAliases(
        IAssemblySymbol assembly,
        INamedTypeSymbol aliasAttribute,
        IReadOnlyCollection<TemplateComponentDescriptor> exports,
        ICollection<SquareDiagnostic> diagnostics)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        if (aliasAttribute == null) return new ReadOnlyDictionary<string, string>(aliases);
        var aliasDeclarations = new Dictionary<string, AttributeData>(StringComparer.Ordinal);
        var knownUris = new HashSet<string>(exports.Select(item => item.NamespaceUri), StringComparer.Ordinal);
        foreach (var attribute in assembly.GetAttributes().Where(item =>
                     SymbolEqualityComparer.Default.Equals(item.AttributeClass, aliasAttribute)))
        {
            if (attribute.ConstructorArguments.Length != 2 || attribute.ConstructorArguments[0].Value is not string uri ||
                attribute.ConstructorArguments[1].Value is not string prefix || string.IsNullOrWhiteSpace(uri) ||
                !knownUris.Contains(uri) || IsReservedPrefix(prefix) || !IsMarkupName(prefix) || aliases.ContainsKey(uri))
            {
                AddDiagnostic(diagnostics, "SQXE001", "Invalid, duplicate, or unknown element namespace alias.", attribute);
                continue;
            }
            aliases.Add(uri, prefix);
            aliasDeclarations[uri] = attribute;
        }
        foreach (var conflict in aliases.GroupBy(item => item.Value, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            foreach (var uri in conflict.Select(item => item.Key).OrderBy(item => item, StringComparer.Ordinal))
            {
                aliasDeclarations.TryGetValue(uri, out var declaration);
                AddDiagnostic(diagnostics, "SQXE001",
                    "Element namespace alias prefix '" + conflict.Key + "' is assigned to multiple URIs.",
                    declaration);
            }
        return new ReadOnlyDictionary<string, string>(aliases);
    }

    private static IReadOnlyList<string> ReadOrder(
        IAssemblySymbol assembly,
        INamedTypeSymbol orderAttribute,
        IReadOnlyCollection<TemplateComponentDescriptor> exports,
        ICollection<SquareDiagnostic> diagnostics)
    {
        if (orderAttribute == null) return Array.Empty<string>();
        var attributes = assembly.GetAttributes().Where(item =>
            SymbolEqualityComparer.Default.Equals(item.AttributeClass, orderAttribute)).ToArray();
        if (attributes.Length == 0) return Array.Empty<string>();
        if (attributes.Length > 1)
            foreach (var attribute in attributes)
                AddDiagnostic(diagnostics, "SQXE001", "ElementNamespaceOrder may only be declared once.", attribute);
        var knownUris = new HashSet<string>(exports.Select(item => item.NamespaceUri), StringComparer.Ordinal) { LocalNamespaceUri };
        var result = new List<string>();
        var argument = attributes[0].ConstructorArguments.FirstOrDefault();
        if (argument.Kind != TypedConstantKind.Array || argument.IsNull)
        {
            AddDiagnostic(diagnostics, "SQXE001", "ElementNamespaceOrder must contain a non-null URI array.", attributes[0]);
            return Array.Empty<string>();
        }
        foreach (var value in argument.Values)
        {
            if (value.Value is not string uri || !knownUris.Contains(uri) || IsFrameworkNamespace(uri) || result.Contains(uri, StringComparer.Ordinal))
            {
                AddDiagnostic(diagnostics, "SQXE001", "ElementNamespaceOrder contains a duplicate, unknown, or fixed framework URI.", attributes[0]);
                continue;
            }
            result.Add(uri);
        }
        return new ReadOnlyCollection<string>(result);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildPrefixes(
        IReadOnlyCollection<TemplateComponentDescriptor> exports,
        IReadOnlyDictionary<string, string> aliases,
        IReadOnlyDictionary<string, AttributeData> declarations,
        ICollection<SquareDiagnostic> diagnostics)
    {
        var uriPrefixes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var uriGroup in exports.GroupBy(item => item.NamespaceUri, StringComparer.Ordinal))
        {
            if (aliases.TryGetValue(uriGroup.Key, out var alias))
            {
                uriPrefixes[uriGroup.Key] = alias;
                continue;
            }
            var defaults = uriGroup.Select(item => item.Prefix).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (defaults.Length == 1)
            {
                uriPrefixes[uriGroup.Key] = defaults[0];
                continue;
            }
            foreach (var prefix in defaults.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
                AddDiagnostic(diagnostics, "SQXE001",
                    "Namespace URI '" + uriGroup.Key + "' declares inconsistent default prefixes; configure ElementNamespaceAlias.",
                    FindDeclaration(declarations, uriGroup.First(item =>
                        item.Prefix.Equals(prefix, StringComparison.OrdinalIgnoreCase))));
        }
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["html"] = new[] { HtmlNamespaceUri }, ["ui"] = new[] { SquareNamespaceUri },
            ["svg"] = new[] { SvgNamespaceUri }, ["local"] = new[] { LocalNamespaceUri }
        };
        foreach (var prefixGroup in uriPrefixes.GroupBy(item => item.Value, StringComparer.OrdinalIgnoreCase))
        {
            var uris = prefixGroup.Select(item => item.Key).OrderBy(item => item, StringComparer.Ordinal).ToArray();
            if (uris.Length > 1 && uris.Any(aliases.ContainsKey))
                foreach (var uri in uris)
                    foreach (var component in exports.Where(item =>
                                 item.NamespaceUri.Equals(uri, StringComparison.Ordinal) &&
                                 item.Prefix.Equals(prefixGroup.Key, StringComparison.OrdinalIgnoreCase)))
                        AddDiagnostic(diagnostics, "SQXE001",
                            "Element namespace alias prefix '" + prefixGroup.Key + "' conflicts with another namespace prefix.",
                            FindDeclaration(declarations, component));
            result[prefixGroup.Key] = uris;
        }
        return new ReadOnlyDictionary<string, IReadOnlyList<string>>(result);
    }

    private static void ValidateExportIdentities(
        IReadOnlyCollection<TemplateComponentDescriptor> exports,
        IReadOnlyDictionary<string, AttributeData> declarations,
        ICollection<SquareDiagnostic> diagnostics)
    {
        foreach (var group in exports.GroupBy(item => item.NamespaceUri + "\0" + item.LocalName.ToUpperInvariant(), StringComparer.Ordinal))
        {
            var types = group.GroupBy(ComponentAssemblyTypeIdentity, StringComparer.Ordinal).Select(item => item.First()).ToArray();
            if (types.Length <= 1) continue;
            var message = "Duplicate element identity '" + types[0].NamespaceUri + "#" + types[0].LocalName + "': " +
                string.Join(", ", types.Select(item => item.AssemblyName + ":" + item.TypeName));
            foreach (var type in types)
                AddDiagnostic(diagnostics, "SQXE005", message, FindDeclaration(declarations, type));
        }
    }

    private static void ValidateCrossAssemblyTypeNames(
        IReadOnlyCollection<TemplateComponentDescriptor> exports,
        IReadOnlyDictionary<string, AttributeData> declarations,
        ICollection<SquareDiagnostic> diagnostics)
    {
        foreach (var group in exports.GroupBy(item => item.TypeMetadataName, StringComparer.Ordinal))
        {
            var assemblies = group.Select(item => item.AssemblyName).Distinct(StringComparer.Ordinal).ToArray();
            if (assemblies.Length <= 1) continue;
            var message = "CLR type '" + group.Key + "' is exported by multiple assemblies: " +
                string.Join(", ", assemblies) + ".";
            foreach (var assembly in assemblies)
                AddDiagnostic(diagnostics, "SQXE001", message, FindDeclaration(declarations,
                    group.First(item => item.AssemblyName.Equals(assembly, StringComparison.Ordinal))));
        }
    }

    private static AttributeData FindDeclaration(
        IReadOnlyDictionary<string, AttributeData> declarations,
        TemplateComponentDescriptor component) =>
        declarations.TryGetValue(ComponentAssemblyTypeIdentity(component), out var declaration) ? declaration : null;

    private static IReadOnlyList<TemplateComponentDescriptor> BuildLocalComponents(Compilation compilation, ISet<string> exportedTypes)
    {
        var result = new List<TemplateComponentDescriptor>();
        VisitNamespace(compilation.Assembly.GlobalNamespace);
        return DistinctComponents(result);

        void VisitNamespace(INamespaceSymbol namespaceSymbol)
        {
            foreach (var child in namespaceSymbol.GetNamespaceMembers()) VisitNamespace(child);
            foreach (var type in namespaceSymbol.GetTypeMembers()) VisitType(type);
        }
        void VisitType(INamedTypeSymbol type)
        {
            var descriptor = CreateClrDescriptor(compilation, type);
            if (descriptor != null && !exportedTypes.Contains(ComponentAssemblyTypeIdentity(descriptor))) result.Add(descriptor);
            foreach (var nested in type.GetTypeMembers()) VisitType(nested);
        }
    }

    private static TemplateComponentDescriptor CreateClrDescriptor(Compilation compilation, INamedTypeSymbol type)
    {
        if (!IsUsableClrType(compilation, type)) return null;
        var typeName = ToCSharpTypeName(type);
        var uiElementSymbol = compilation.GetTypeByMetadataName("Square.UI.UIElement");
        return new TemplateComponentDescriptor(type.Name, typeName, GetMetadataName(type), string.Empty, string.Empty, type.Name,
            type.ContainingAssembly?.Identity.Name ?? string.Empty, type.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree.FilePath ?? string.Empty,
            TemplateElementKind.ClrScoped, false, true, false, DerivesFrom(type, uiElementSymbol));
    }

    private static bool IsValidExportType(INamedTypeSymbol type, INamedTypeSymbol elementSymbol) =>
        IsClosedConcreteElement(type, elementSymbol) && IsPublic(type) &&
        type.InstanceConstructors.Any(ctor => ctor.Parameters.Length == 0 && ctor.DeclaredAccessibility == Accessibility.Public);

    private static bool IsUsableClrType(Compilation compilation, INamedTypeSymbol type)
    {
        var elementSymbol = compilation.GetTypeByMetadataName("Square.UI.Element");
        if (!IsClosedConcreteElement(type, elementSymbol) || !compilation.IsSymbolAccessibleWithin(type, compilation.Assembly)) return false;
        return type.InstanceConstructors.Any(ctor => ctor.Parameters.Length == 0 &&
            compilation.IsSymbolAccessibleWithin(ctor, compilation.Assembly));
    }

    private static bool IsClosedConcreteElement(INamedTypeSymbol type, INamedTypeSymbol elementSymbol) =>
        elementSymbol != null && !type.IsAbstract && !type.IsUnboundGenericType && !ContainsTypeParameter(type) &&
        DerivesFromElement(type, elementSymbol);

    private static bool ContainsTypeParameter(ITypeSymbol type) =>
        type.TypeKind == TypeKind.TypeParameter || type is INamedTypeSymbol named && named.TypeArguments.Any(ContainsTypeParameter);

    private static bool IsPublic(INamedTypeSymbol type)
    {
        for (var current = type; current != null; current = current.ContainingType)
            if (current.DeclaredAccessibility != Accessibility.Public) return false;
        return true;
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        if (baseType == null) return false;
        for (var current = type; current != null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType)) return true;
        return false;
    }

    private static bool DerivesFromElement(INamedTypeSymbol type, INamedTypeSymbol elementSymbol)
    {
        for (var current = type; current != null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, elementSymbol)) return true;
        return false;
    }

    private static INamedTypeSymbol FindComponentSymbol(
        Compilation compilation,
        TemplateComponentDescriptor component,
        INamedTypeSymbol exportAttribute,
        IReadOnlyDictionary<string, INamedTypeSymbol> exportedSymbols = null)
    {
        if (compilation == null || component == null) return null;
        if (exportedSymbols != null && exportedSymbols.TryGetValue(ComponentAssemblyTypeIdentity(component), out var declared))
            return declared;
        foreach (var assembly in EnumerateAssemblies(compilation))
        {
            if (!assembly.Identity.Name.Equals(component.AssemblyName, StringComparison.Ordinal)) continue;
            if (exportAttribute != null)
            {
                foreach (var attribute in assembly.GetAttributes().Where(attribute =>
                             SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, exportAttribute)))
                {
                    if (attribute.ConstructorArguments.Length != 4 ||
                        attribute.ConstructorArguments[3].Value is not INamedTypeSymbol exportedType) continue;
                    if (exportedType.ContainingAssembly.Identity.Name.Equals(component.AssemblyName, StringComparison.Ordinal) &&
                        GetMetadataName(exportedType).Equals(component.TypeMetadataName, StringComparison.Ordinal) &&
                        ToCSharpTypeName(exportedType).Equals(component.TypeName, StringComparison.Ordinal))
                        return exportedType;
                }
            }
            var direct = assembly.GetTypeByMetadataName(component.TypeMetadataName);
            if (direct != null) return direct;
        }
        return null;
    }

    private static IEnumerable<IAssemblySymbol> EnumerateAssemblies(Compilation compilation)
    {
        yield return compilation.Assembly;
        foreach (var reference in compilation.References)
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
                yield return assembly;
    }

    private static INamedTypeSymbol GetTypeByCSharpName(Compilation compilation, string typeName)
    {
        if (compilation == null || string.IsNullOrWhiteSpace(typeName)) return null;
        var normalized = StripIdentifierEscapes(typeName);
        var direct = compilation.GetTypeByMetadataName(normalized);
        if (direct != null) return direct;
        var separators = normalized.Select((character, index) => (character, index)).Where(item => item.character == '.').Select(item => item.index).Reverse();
        foreach (var separator in separators)
        {
            var metadataName = normalized.Substring(0, separator) + "+" + normalized.Substring(separator + 1).Replace('.', '+');
            var nested = compilation.GetTypeByMetadataName(metadataName);
            if (nested != null) return nested;
        }
        return null;
    }

    private static string StripIdentifierEscapes(string typeName) =>
        string.Join(".", typeName.Split('.').Select(part =>
            part.StartsWith("@", StringComparison.Ordinal) ? part.Substring(1) : part));

    private static string GetMetadataName(INamedTypeSymbol type)
    {
        var names = new Stack<string>();
        for (var current = type; current != null; current = current.ContainingType) names.Push(current.MetadataName);
        var nested = string.Join("+", names);
        var namespaceName = type.ContainingNamespace?.ToDisplayString();
        return string.IsNullOrEmpty(namespaceName) ? nested : namespaceName + "." + nested;
    }
    private static string ToCSharpTypeName(ITypeSymbol type)
    {
        var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return name.StartsWith("global::", StringComparison.Ordinal) ? name.Substring("global::".Length) : name;
    }

    private static bool IsMarkupName(string value)
    {
        if (string.IsNullOrEmpty(value) || !(value[0] == '_' || char.IsLetter(value[0]))) return false;
        return value.Skip(1).All(character => character == '_' || character == '-' || char.IsLetterOrDigit(character));
    }
    private static bool IsClrName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Split('.').All(part => part.Length > 0 && (part[0] == '_' || char.IsLetter(part[0])) &&
            part.Skip(1).All(character => character == '_' || char.IsLetterOrDigit(character)));
    }
    private static bool IsReservedPrefix(string prefix) => prefix != null &&
        (prefix.Equals("html", StringComparison.OrdinalIgnoreCase) || prefix.Equals("ui", StringComparison.OrdinalIgnoreCase) ||
         prefix.Equals("svg", StringComparison.OrdinalIgnoreCase) || prefix.Equals("local", StringComparison.OrdinalIgnoreCase));
    private static bool IsFrameworkNamespace(string uri) => uri == HtmlNamespaceUri || uri == SquareNamespaceUri || uri == SvgNamespaceUri;
    private static bool IsReservedNamespace(string uri) => IsFrameworkNamespace(uri) || uri == LocalNamespaceUri;
    private static TemplatePropertyValueKind GetPropertyValueKind(string name) =>
        name.Equals("class", StringComparison.OrdinalIgnoreCase) ? TemplatePropertyValueKind.CssClass :
        BooleanPropertyNames.Contains(name) ? TemplatePropertyValueKind.Boolean : TemplatePropertyValueKind.String;
    private static string ComponentIdentity(TemplateComponentDescriptor item) => item.NamespaceUri + "\0" + item.LocalName.ToUpperInvariant() + "\0" + ComponentAssemblyTypeIdentity(item);
    private static string ComponentAssemblyTypeIdentity(TemplateComponentDescriptor item) =>
        item.AssemblyName + "\0" + item.TypeMetadataName + "\0" + item.TypeName;
    private static string ComponentSortKey(TemplateComponentDescriptor item) => item.NamespaceUri + "\0" + item.LocalName.ToUpperInvariant() + "\0" + ComponentAssemblyTypeIdentity(item);

    private static void AddDiagnostic(ICollection<SquareDiagnostic> diagnostics, string id, string message, AttributeData attribute)
    {
        var syntax = attribute == null ? null : attribute.ApplicationSyntaxReference?.GetSyntax();
        diagnostics.Add(syntax == null
            ? NewDiagnostic(id, message)
            : new SquareDiagnostic(id, SquareDiagnosticSeverity.Error, message,
                new SquareSourceRange(syntax.Span.Start, syntax.Span.Length), syntax.SyntaxTree.FilePath ?? string.Empty));
    }
    private static SquareDiagnostic NewDiagnostic(string id, string message) =>
        new(id, SquareDiagnosticSeverity.Error, message, new SquareSourceRange(0, 0), string.Empty);
}
