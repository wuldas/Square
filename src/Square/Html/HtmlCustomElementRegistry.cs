using System.Collections.ObjectModel;
using System.ComponentModel;
using Square.UI;

namespace Square.Html;

/// <summary>A callback failure on one custom element; later elements still connect and update.</summary>
public sealed record HtmlCustomElementDiagnostic(string Message, ElementDebugInfo? DebugInfo, Exception Exception);

/// <summary>Per-document definitions for autonomous and customized built-in HTML elements.</summary>
public sealed class HtmlCustomElementRegistry
{
    private readonly Dictionary<string, Definition> _definitions = new(StringComparer.Ordinal);
    private readonly List<HtmlCustomElementDiagnostic> _diagnostics = [];
    private readonly ReadOnlyCollection<HtmlCustomElementDiagnostic> _diagnosticView;

    internal HtmlCustomElementRegistry()
    {
        _diagnosticView = _diagnostics.AsReadOnly();
    }

    public IReadOnlyList<HtmlCustomElementDiagnostic> Diagnostics => _diagnosticView;

    public void DefineAutonomous<T>(string name, Func<T> factory, params string[] observedAttributes)
        where T : HTMLElement, new() => Define(name, null, factory, observedAttributes);

    public void DefineCustomizedBuiltIn<T>(string name, string extendsTag, Func<T> factory,
        params string[] observedAttributes) where T : HTMLElement, new() =>
        Define(name, extendsTag, factory, observedAttributes);

    private void Define<T>(string name, string? extendsTag, Func<T> factory, string[] observedAttributes)
        where T : HTMLElement, new()
    {
        ArgumentNullException.ThrowIfNull(factory);
        var observed = Validate(typeof(T), name, extendsTag, observedAttributes);
        if (_definitions.ContainsKey(name)) throw new InvalidOperationException($"Custom element '{name}' is already defined in this document.");
        var prototype = new T();
        if (prototype.TagName != (extendsTag ?? name))
            throw new ArgumentException($"Custom element '{name}' must construct the {(extendsTag == null ? "autonomous tag" : "extended built-in tag")} '{extendsTag ?? name}'.", nameof(factory));
        var definition = new Definition(name, extendsTag, typeof(T), observed, () => factory());
        definition.Create = () =>
        {
            var element = definition.Factory() ?? throw new InvalidOperationException($"Factory for '{name}' returned null.");
            if (!definition.ElementType.IsInstanceOfType(element) || element.TagName != (definition.ExtendsTag ?? definition.Name))
                throw new InvalidOperationException($"Factory for '{name}' returned an incompatible HTML element.");
            element.InitializeCustomDefinition(definition.Name, definition.ExtendsTag, definition.ObservedAttributes);
            return element;
        };
        _definitions.Add(name, definition);
    }

    internal Func<Element>? FindAutonomousFactory(string name) =>
        _definitions.TryGetValue(ElementRegistry.Normalize(ElementRegistry.HtmlNamespaceUri, name), out var definition) &&
        definition.ExtendsTag == null
            ? definition.Create : null;

    internal HTMLElement CreateCustomizedBuiltIn(string name, string extendsTag)
    {
        if (!_definitions.TryGetValue(name, out var definition) || definition.ExtendsTag != extendsTag)
            throw new InvalidOperationException($"Customized built-in '{name}' is not defined for '{extendsTag}' in this document.");
        var element = (HTMLElement)definition.Create();
        element.SetAttribute("is", name);
        return element;
    }

    /// <summary>Generator-only definition initialization before any generated attribute binding.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void InitializeGenerated(HTMLElement element, string name, string? extendsTag,
        string[] observedAttributes)
    {
        ArgumentNullException.ThrowIfNull(element);
        var observed = Validate(element.GetType(), name, extendsTag, observedAttributes);
        if (element.TagName != (extendsTag ?? name))
            throw new ArgumentException($"Custom element '{name}' has an incompatible tag '{element.TagName}'.", nameof(element));
        element.InitializeCustomDefinition(name, extendsTag, observed);
    }

    private static string[] Validate(Type type, string name, string? extendsTag, string[] observedAttributes)
    {
        if (!IsValidCustomElementName(name))
            throw new ArgumentException($"'{name}' is not a valid custom element name.", nameof(name));
        if (!type.IsVisible || type.IsAbstract || type.ContainsGenericParameters ||
            !typeof(HTMLElement).IsAssignableFrom(type))
            throw new ArgumentException($"{type.FullName} must be a public concrete HTML element.", nameof(type));
        if (extendsTag != null)
        {
            if (!HtmlTagCatalog.IsTag(extendsTag) ||
                !HtmlElementFactory.Create(extendsTag).GetType().IsAssignableFrom(type))
                throw new ArgumentException($"{type.FullName} must inherit the concrete built-in HTML type for '{extendsTag}'.", nameof(extendsTag));
        }
        observedAttributes ??= [];
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attribute in observedAttributes)
        {
            if (string.IsNullOrWhiteSpace(attribute) || attribute.Any(character => char.IsWhiteSpace(character) || character is '<' or '>' or '/' or '=' or '\'' or '"'))
                throw new ArgumentException("Observed attribute names must be valid non-empty HTML attribute names.", nameof(observedAttributes));
            var normalized = attribute.ToLowerInvariant();
            if (!result.Add(normalized)) throw new ArgumentException($"Observed attribute '{normalized}' is duplicated.", nameof(observedAttributes));
        }
        return result.ToArray();
    }

    public static bool IsValidCustomElementName(string? name) => HtmlCustomElementNames.IsValid(name);

    internal void Report(HTMLElement element, Exception exception)
    {
        var diagnostic = new HtmlCustomElementDiagnostic(
            $"Custom element '{element.CustomElementName}' callback failed: {exception.Message}",
            element.DebugInfo, exception);
        _diagnostics.Add(diagnostic);
    }

    private sealed class Definition(string name, string? extendsTag, Type elementType,
        string[] observedAttributes, Func<HTMLElement> factory)
    {
        internal string Name { get; } = name;
        internal string? ExtendsTag { get; } = extendsTag;
        internal Type ElementType { get; } = elementType;
        internal string[] ObservedAttributes { get; } = observedAttributes;
        internal Func<HTMLElement> Factory { get; } = factory;
        internal Func<Element> Create { get; set; } = null!;
    }
}
