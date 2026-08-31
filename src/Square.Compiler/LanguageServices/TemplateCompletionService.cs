using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Square.Compiler.Syntax;

namespace Square.Compiler.LanguageServices;

public enum TemplateCompletionKind
{
    None,
    Tag,
    ClosingTag,
    Attribute,
    Event,
    EventModifier,
    ModelModifier,
    EventHandler,
    AttributeValue,
    Expression,
    Binding,
    Slot,
    Directive,
    CssClass,
    CssProperty,
    CssValue,
    CssSelector,
    CssPseudoClass,
    CssPseudoElement,
    CssAtRule,
    ScriptGeneral,
    ScriptMember,
    ScriptNamespace,
    ScriptType,
    ScriptAttribute,
    ScriptAttributeArgument
}

public sealed class TemplateCompletionContext
{
    public TemplateCompletionContext(
        TemplateCompletionKind kind,
        string prefix,
        string tagName,
        bool isSqv)
        : this(kind, prefix, tagName, isSqv, Array.Empty<string>())
    {
    }

    public TemplateCompletionContext(
        TemplateCompletionKind kind,
        string prefix,
        string tagName,
        bool isSqv,
        IReadOnlyCollection<string> existingAttributes)
        : this(
            kind,
            prefix,
            tagName,
            isSqv,
            existingAttributes,
            string.Empty,
            Array.Empty<string>())
    {
    }

    public TemplateCompletionContext(
        TemplateCompletionKind kind,
        string prefix,
        string tagName,
        bool isSqv,
        IReadOnlyCollection<string> existingAttributes,
        string attributeName,
        IReadOnlyCollection<string> localNames,
        bool hasFollowingDelimiter = false,
        int position = -1)
    {
        Kind = kind;
        Prefix = prefix ?? string.Empty;
        TagName = tagName ?? string.Empty;
        IsSqv = isSqv;
        ExistingAttributes = existingAttributes ?? throw new ArgumentNullException(nameof(existingAttributes));
        AttributeName = attributeName ?? string.Empty;
        LocalNames = localNames ?? throw new ArgumentNullException(nameof(localNames));
        HasFollowingDelimiter = hasFollowingDelimiter;
        Position = position;
    }

    public TemplateCompletionKind Kind { get; }
    public string Prefix { get; }
    public string TagName { get; }
    public bool IsSqv { get; }
    public IReadOnlyCollection<string> ExistingAttributes { get; }
    public string AttributeName { get; }
    public IReadOnlyCollection<string> LocalNames { get; }
    public bool HasFollowingDelimiter { get; }
    public int Position { get; }
}

public sealed class TemplateCompletionItem
{
    public TemplateCompletionItem(string label, int kind, string detail, string insertText)
    {
        Label = label;
        Kind = kind;
        Detail = detail;
        InsertText = insertText;
    }

    public string Label { get; }
    public int Kind { get; }
    public string Detail { get; }
    public string InsertText { get; }
}

public static class TemplateCompletionService
{
    private static readonly string[] VueDirectives =
    {
        "v-if", "v-else-if", "v-else", "v-for", "v-show", "v-text",
        "v-bind", "v-on", "v-slot"
    };

    public static TemplateCompletionContext GetContext(string text, int offset, string sourcePath)
    {
        text ??= string.Empty;
        offset = Math.Min(Math.Max(offset, 0), text.Length);
        var isSqv = sourcePath != null && sourcePath.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase);
        var result = SquareDocumentService.ParseSyntaxTree(text, sourcePath ?? string.Empty);
        var document = result.ParsedSqxDocument;
        var scriptContext = CSharpScriptCompletionService.GetContext(text, offset, sourcePath);
        if (scriptContext.Kind != CSharpScriptCompletionKind.None)
            return FromScriptContext(scriptContext, isSqv);
        var cssContext = CssCompletionService.GetContext(text, offset, sourcePath);
        if (cssContext.Kind != CssCompletionKind.None)
            return FromCssContext(cssContext, isSqv);
        var template = document?.Syntax?.Template;
        if (template != null &&
            (offset < template.ContentRange.Offset || offset > template.ContentRange.End))
            return new TemplateCompletionContext(TemplateCompletionKind.None, string.Empty, string.Empty, isSqv);
        if (template != null)
        {
            var element = template.SqxSyntax != null
                ? FindSqxElement(template.SqxSyntax.Roots, offset)
                : template.SqvSyntax != null
                    ? FindSqvElement(template.SqvSyntax.Roots, offset)
                    : null;
            if (element != null)
            {
                if (TryGetClosingTagPrefix(text, offset, out var closingPrefix))
                    return new TemplateCompletionContext(
                        TemplateCompletionKind.ClosingTag,
                        closingPrefix,
                        element.TagName,
                        isSqv,
                        Array.Empty<string>(),
                        string.Empty,
                        Array.Empty<string>(),
                        offset < text.Length && text[offset] == '>');
                if (element.ContainsEventValue(offset))
                {
                    var prefix = GetTokenPrefix(text, offset, out _);
                    return new TemplateCompletionContext(
                        TemplateCompletionKind.EventHandler,
                        prefix,
                        element.TagName,
                        isSqv,
                        element.AttributeNames,
                        element.RequiresEventParameter(offset) ? "requires-event-parameter" : string.Empty,
                        element.AttributeLocalNames);
                }
                if (element.TryGetAttributeValue(offset, out var attributeValue) &&
                    attributeValue.IsExpression)
                {
                    var prefix = GetTokenPrefix(text, offset, out _);
                    var localNames = isSqv && attributeValue.Name == "v-for"
                        ? element.AttributeLocalNames
                        : element.LocalNames;
                    return new TemplateCompletionContext(
                        TemplateCompletionKind.Expression,
                        prefix,
                        element.TagName,
                        isSqv,
                        element.AttributeNames,
                        attributeValue.Name,
                        localNames);
                }
                if (element.TryGetAttributeValue(offset, out attributeValue))
                {
                    var prefix = SafeSlice(text, attributeValue.Range.Offset, offset);
                    var property = TemplateCatalog.BuiltIn.GetProperty(attributeValue.Name);
                    if (property?.ValueKind == TemplatePropertyValueKind.CssClass)
                    {
                        prefix = GetCssClassTokenPrefix(text, attributeValue.Range.Offset, offset);
                        return new TemplateCompletionContext(
                            TemplateCompletionKind.CssClass,
                            prefix,
                            element.TagName,
                            isSqv,
                            element.AttributeNames,
                            attributeValue.Name,
                            element.LocalNames);
                    }
                    return new TemplateCompletionContext(
                        TemplateCompletionKind.AttributeValue,
                        prefix,
                        element.TagName,
                        isSqv,
                        element.AttributeNames,
                        attributeValue.Name,
                        element.LocalNames);
                }
                if (element.ContainsExpression(offset))
                {
                    var prefix = GetTokenPrefix(text, offset, out _);
                    return new TemplateCompletionContext(
                        TemplateCompletionKind.Expression,
                        prefix,
                        element.TagName,
                        isSqv,
                        element.AttributeNames,
                        string.Empty,
                        element.LocalNames);
                }
                var headerEnd = FindHeaderEnd(text, element.Start, offset);
                if (headerEnd < 0 || offset <= headerEnd)
                    return ContextInTag(text, offset, element, isSqv);
            }
        }

        return ContextFromPrefix(text, offset, isSqv);
    }

    public static IReadOnlyList<TemplateCompletionItem> GetItems(
        string text,
        int offset,
        string sourcePath)
    {
        var context = GetContext(text, offset, sourcePath);
        return GetItems(context, text);
    }

    public static IReadOnlyList<TemplateCompletionItem> GetItems(
        TemplateCompletionContext context,
        string text)
    {
        if (context == null) return Array.Empty<TemplateCompletionItem>();
        var inferredSourcePath = context.IsSqv ? "Completion.sqv" : "Completion.sqx";
        if (context.Kind is TemplateCompletionKind.CssProperty or
            TemplateCompletionKind.CssValue or
            TemplateCompletionKind.CssSelector or
            TemplateCompletionKind.CssPseudoClass or
            TemplateCompletionKind.CssPseudoElement or
            TemplateCompletionKind.CssAtRule)
        {
            return CssCompletionService.GetItems(ToCssContext(context), text, inferredSourcePath);
        }
        if (context.Kind is TemplateCompletionKind.ScriptGeneral or
            TemplateCompletionKind.ScriptMember or
            TemplateCompletionKind.ScriptNamespace or
            TemplateCompletionKind.ScriptType or
            TemplateCompletionKind.ScriptAttribute or
            TemplateCompletionKind.ScriptAttributeArgument)
        {
            return CSharpScriptCompletionService.GetItems(ToScriptContext(context), text, inferredSourcePath);
        }
        if (context.Kind == TemplateCompletionKind.EventHandler)
            return GetEventHandlerItems(
                text,
                inferredSourcePath,
                context.Prefix,
                context.AttributeName == "requires-event-parameter");
        if (context.Kind == TemplateCompletionKind.Expression)
            return GetExpressionItems(text, inferredSourcePath, context);

        switch (context.Kind)
        {
            case TemplateCompletionKind.Event:
                return Filter(
                    TemplateCatalog.BuiltIn.Events,
                    context.Prefix,
                    item => context.IsSqv ? item.Name : item.CanonicalName,
                    item =>
                    {
                        var name = context.IsSqv ? item.Name : item.CanonicalName;
                        return new TemplateCompletionItem(name, 23, "Square event", name);
                    });
            case TemplateCompletionKind.EventModifier:
                return GetEventModifierItems(context);
            case TemplateCompletionKind.ModelModifier:
                return GetModelModifierItems(context);
            case TemplateCompletionKind.Directive:
                return GetDirectiveItems(context);
            case TemplateCompletionKind.Slot:
                return new[] { "default" }
                    .Where(name => name.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
                    .Select(name => new TemplateCompletionItem(name, 14, "Vue slot", name))
                    .ToArray();
            case TemplateCompletionKind.CssClass:
                return ExtractCssClassNames(text)
                    .Where(name => name.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
                    .Select(name => new TemplateCompletionItem(name, 12, "CSS class", name))
                    .ToArray();
            case TemplateCompletionKind.Attribute:
                return GetAttributeItems(context);
            case TemplateCompletionKind.AttributeValue:
                return GetAttributeValueItems(context);
            case TemplateCompletionKind.Binding:
                return GetBindingItems(context);
            case TemplateCompletionKind.Tag:
                return Filter(
                    TemplateCatalog.BuiltIn.Components,
                    context.Prefix,
                    item => item.TagName,
                    item => new TemplateCompletionItem(
                        item.TagName,
                        item.IsBuiltIn ? 7 : 14,
                        item.TypeName,
                        item.TagName));
            case TemplateCompletionKind.ClosingTag:
                if (context.TagName.Length == 0 ||
                    !context.TagName.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
                    return Array.Empty<TemplateCompletionItem>();
                return new[]
                {
                    new TemplateCompletionItem(
                        context.TagName,
                        7,
                        "Closing tag",
                        context.TagName + (context.HasFollowingDelimiter ? string.Empty : ">"))
                };
            default:
                return Array.Empty<TemplateCompletionItem>();
        }
    }

    private static IReadOnlyList<TemplateCompletionItem> Filter<T>(
        IEnumerable<T> source,
        string prefix,
        Func<T, string> name,
        Func<T, TemplateCompletionItem> map)
    {
        return source
            .Where(item => name(item).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(map)
            .ToArray();
    }

    private static TemplateCompletionContext FromCssContext(CssCompletionContext context, bool isSqv) =>
        new(
            context.Kind switch
            {
                CssCompletionKind.Property => TemplateCompletionKind.CssProperty,
                CssCompletionKind.Value => TemplateCompletionKind.CssValue,
                CssCompletionKind.Selector => TemplateCompletionKind.CssSelector,
                CssCompletionKind.PseudoClass => TemplateCompletionKind.CssPseudoClass,
                CssCompletionKind.PseudoElement => TemplateCompletionKind.CssPseudoElement,
                CssCompletionKind.AtRule => TemplateCompletionKind.CssAtRule,
                _ => TemplateCompletionKind.None
            },
            context.Prefix,
            string.Empty,
            isSqv,
            Array.Empty<string>(),
            context.PropertyName,
            Array.Empty<string>());

    private static CssCompletionContext ToCssContext(TemplateCompletionContext context) =>
        new(
            context.Kind switch
            {
                TemplateCompletionKind.CssProperty => CssCompletionKind.Property,
                TemplateCompletionKind.CssValue => CssCompletionKind.Value,
                TemplateCompletionKind.CssSelector => CssCompletionKind.Selector,
                TemplateCompletionKind.CssPseudoClass => CssCompletionKind.PseudoClass,
                TemplateCompletionKind.CssPseudoElement => CssCompletionKind.PseudoElement,
                TemplateCompletionKind.CssAtRule => CssCompletionKind.AtRule,
                _ => CssCompletionKind.None
            },
            context.Prefix,
            context.AttributeName);

    private static TemplateCompletionContext FromScriptContext(
        CSharpScriptCompletionContext context,
        bool isSqv) =>
        new(
            context.Kind switch
            {
                CSharpScriptCompletionKind.General => TemplateCompletionKind.ScriptGeneral,
                CSharpScriptCompletionKind.Member => TemplateCompletionKind.ScriptMember,
                CSharpScriptCompletionKind.Namespace => TemplateCompletionKind.ScriptNamespace,
                CSharpScriptCompletionKind.Type => TemplateCompletionKind.ScriptType,
                CSharpScriptCompletionKind.Attribute => TemplateCompletionKind.ScriptAttribute,
                CSharpScriptCompletionKind.AttributeArgument => TemplateCompletionKind.ScriptAttributeArgument,
                _ => TemplateCompletionKind.None
            },
            context.Prefix,
            string.Empty,
            isSqv,
            Array.Empty<string>(),
            context.Receiver,
            Array.Empty<string>(),
            position: context.Position);

    private static CSharpScriptCompletionContext ToScriptContext(TemplateCompletionContext context) =>
        new(
            context.Kind switch
            {
                TemplateCompletionKind.ScriptGeneral => CSharpScriptCompletionKind.General,
                TemplateCompletionKind.ScriptMember => CSharpScriptCompletionKind.Member,
                TemplateCompletionKind.ScriptNamespace => CSharpScriptCompletionKind.Namespace,
                TemplateCompletionKind.ScriptType => CSharpScriptCompletionKind.Type,
                TemplateCompletionKind.ScriptAttribute => CSharpScriptCompletionKind.Attribute,
                TemplateCompletionKind.ScriptAttributeArgument => CSharpScriptCompletionKind.AttributeArgument,
                _ => CSharpScriptCompletionKind.None
            },
            context.Prefix,
            context.AttributeName,
            context.Position);

    private static IReadOnlyList<TemplateCompletionItem> GetEventHandlerItems(
        string text,
        string sourcePath,
        string prefix,
        bool requiresEventParameter)
    {
        var script = SquareDocumentService.ParseSyntaxTree(text, sourcePath ?? string.Empty)
            .ParsedSqxDocument?.Syntax?.Script?.CSharp;
        if (script == null) return Array.Empty<TemplateCompletionItem>();
        return script.Members
            .OfType<MethodDeclarationSyntax>()
            .Where(method => IsCompatibleEventHandler(method, requiresEventParameter))
            .Where(method => method.Identifier.ValueText.StartsWith(
                prefix ?? string.Empty,
                StringComparison.OrdinalIgnoreCase))
            .Select(method => new TemplateCompletionItem(
                method.Identifier.ValueText,
                3,
                method.ReturnType + " " + method.Identifier.ValueText + method.ParameterList,
                method.Identifier.ValueText))
            .ToArray();
    }

    private static IReadOnlyList<TemplateCompletionItem> GetEventModifierItems(
        TemplateCompletionContext context)
    {
        var firstDot = context.AttributeName.IndexOf('.');
        var lastDot = context.AttributeName.LastIndexOf('.');
        var used = firstDot < 0 || lastDot <= firstDot
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(
                context.AttributeName.Substring(firstDot + 1, lastDot - firstDot - 1)
                    .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);
        return new[] { "stop", "prevent" }
            .Where(modifier => !used.Contains(modifier))
            .Where(modifier => modifier.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
            .Select(modifier => new TemplateCompletionItem(
                modifier,
                14,
                "Vue event modifier",
                modifier))
            .ToArray();
    }

    private static IReadOnlyList<TemplateCompletionItem> GetModelModifierItems(
        TemplateCompletionContext context)
    {
        var firstDot = context.AttributeName.IndexOf('.');
        var lastDot = context.AttributeName.LastIndexOf('.');
        var used = firstDot < 0 || lastDot <= firstDot
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(
                context.AttributeName.Substring(firstDot + 1, lastDot - firstDot - 1)
                    .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);
        return new[] { "trim", "number", "lazy" }
            .Where(modifier => !used.Contains(modifier))
            .Where(modifier => modifier.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
            .Select(modifier => new TemplateCompletionItem(
                modifier,
                14,
                "Vue model modifier",
                modifier))
            .ToArray();
    }

    private static bool IsCompatibleEventHandler(
        MethodDeclarationSyntax method,
        bool requiresEventParameter)
    {
        if (method.ReturnType is not PredefinedTypeSyntax predefined ||
            predefined.Keyword.RawKind != (int)SyntaxKind.VoidKeyword ||
            method.TypeParameterList != null)
            return false;
        if (method.ParameterList.Parameters.Count == 0) return !requiresEventParameter;
        if (method.ParameterList.Parameters.Count != 1) return false;
        var parameter = method.ParameterList.Parameters[0];
        if (parameter.Modifiers.Count != 0 || parameter.Type == null) return false;
        var typeName = parameter.Type.ToString();
        return typeName == "Event" ||
               typeName == "Square.Events.Event" ||
               typeName == "global::Square.Events.Event";
    }

    private static IReadOnlyList<TemplateCompletionItem> GetDirectiveItems(
        TemplateCompletionContext context)
    {
        var existing = new HashSet<string>(
            context.ExistingAttributes.Select(NormalizeSqvDirectiveName),
            StringComparer.OrdinalIgnoreCase);
        var directives = SupportsSqvModel(context.TagName)
            ? VueDirectives.Concat(new[] { "v-model" })
            : VueDirectives;
        return directives
            .Where(name => !existing.Contains(name))
            .Where(name => name.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
            .Select(name => new TemplateCompletionItem(name, 14, "Vue directive", name))
            .ToArray();
    }

    private static string NormalizeSqvDirectiveName(string name)
    {
        var modifier = name.IndexOf('.');
        return modifier < 0 ? name : name.Substring(0, modifier);
    }

    private static bool SupportsSqvModel(string tagName) =>
        tagName.Equals("Input", StringComparison.OrdinalIgnoreCase) ||
        tagName.Equals("TextArea", StringComparison.OrdinalIgnoreCase) ||
        tagName.Equals("Select", StringComparison.OrdinalIgnoreCase) ||
        tagName.Equals("CheckBox", StringComparison.OrdinalIgnoreCase) ||
        tagName.Equals("Radio", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<TemplateCompletionItem> GetAttributeItems(
        TemplateCompletionContext context)
    {
        var propertyDescriptors = TemplateCatalog.BuiltIn.GetPropertiesForTag(context.TagName);
        var existingProperties = new HashSet<string>(
            context.IsSqv
                ? context.ExistingAttributes.Select(NormalizeSqvPropertyName)
                : context.ExistingAttributes,
            StringComparer.OrdinalIgnoreCase);
        var properties = propertyDescriptors
            .Where(property => !existingProperties.Contains(property.Name))
            .Where(property => property.Name.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
            .Select(property => new TemplateCompletionItem(
                property.Name,
                10,
                property.CanonicalName,
                property.Name));

        if (!context.IsSqv)
        {
            var existing = new HashSet<string>(context.ExistingAttributes, StringComparer.OrdinalIgnoreCase);
            var events = TemplateCatalog.BuiltIn.Events
                .Select(eventItem => eventItem.CanonicalName)
                .Where(name => !existing.Contains(name))
                .Where(name => name.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
                .Select(name => new TemplateCompletionItem(name, 23, "Square event", name));
            return properties.Concat(events).ToArray();
        }

        var dynamicProperties = propertyDescriptors
            .Where(property => !existingProperties.Contains(property.Name))
            .Select(property => ":" + property.Name)
            .Where(name => name.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
            .Select(name => new TemplateCompletionItem(name, 10, "Dynamic Square property", name));
        var existingEvents = new HashSet<string>(
            context.ExistingAttributes.Select(NormalizeSqvEventName),
            StringComparer.OrdinalIgnoreCase);
        var vueEvents = TemplateCatalog.BuiltIn.Events
            .Where(eventItem => !existingEvents.Contains(eventItem.Name))
            .Select(eventItem => "@" + eventItem.Name)
            .Where(name => name.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
            .Select(name => new TemplateCompletionItem(name, 23, "Square event", name));
        return properties
            .Concat(dynamicProperties)
            .Concat(vueEvents)
            .Concat(GetDirectiveItems(context))
            .ToArray();
    }

    private static IReadOnlyList<TemplateCompletionItem> GetAttributeValueItems(
        TemplateCompletionContext context)
    {
        var property = TemplateCatalog.BuiltIn.GetProperty(context.AttributeName);
        if (property?.ValueKind != TemplatePropertyValueKind.Boolean)
            return Array.Empty<TemplateCompletionItem>();
        return new[] { "true", "false" }
            .Where(value => value.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
            .Select(value => new TemplateCompletionItem(value, 12, "Boolean", value))
            .ToArray();
    }

    private static IReadOnlyList<TemplateCompletionItem> GetBindingItems(
        TemplateCompletionContext context)
    {
        var existing = new HashSet<string>(
            context.ExistingAttributes.Select(NormalizeSqvPropertyName),
            StringComparer.OrdinalIgnoreCase);
        return TemplateCatalog.BuiltIn.GetPropertiesForTag(context.TagName)
            .Where(property => !existing.Contains(property.Name))
            .Where(property => property.Name.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
            .Select(property => new TemplateCompletionItem(
                property.Name,
                10,
                "Dynamic " + property.CanonicalName,
                property.Name))
            .ToArray();
    }

    private static IReadOnlyList<TemplateCompletionItem> GetExpressionItems(
        string text,
        string sourcePath,
        TemplateCompletionContext context)
    {
        var items = context.LocalNames
            .Select(name => new TemplateCompletionItem(name, 6, "Template local", name))
            .ToList();
        var script = SquareDocumentService.ParseSyntaxTree(text, sourcePath ?? string.Empty)
            .ParsedSqxDocument?.Syntax?.Script?.CSharp;
        if (script != null)
        {
            foreach (var field in script.Members.OfType<FieldDeclarationSyntax>())
                foreach (var variable in field.Declaration.Variables)
                    items.Add(new TemplateCompletionItem(
                        variable.Identifier.ValueText,
                        5,
                        field.Declaration.Type + " " + variable.Identifier.ValueText,
                        variable.Identifier.ValueText));
            foreach (var property in script.Members.OfType<PropertyDeclarationSyntax>())
                items.Add(new TemplateCompletionItem(
                    property.Identifier.ValueText,
                    10,
                    property.Type + " " + property.Identifier.ValueText,
                    property.Identifier.ValueText));
            foreach (var method in script.Members.OfType<MethodDeclarationSyntax>())
                items.Add(new TemplateCompletionItem(
                    method.Identifier.ValueText,
                    2,
                    method.ReturnType + " " + method.Identifier.ValueText + method.ParameterList,
                    method.Identifier.ValueText +
                    (method.ParameterList.Parameters.Count == 0 ? "()" : string.Empty)));
        }
        return items
            .Where(item => item.Label.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static TemplateCompletionContext ContextInTag(
        string text,
        int offset,
        TemplateElementContext element,
        bool isSqv)
    {
        var nameStart = element.Start + 1;
        var nameEnd = nameStart + element.TagName.Length;
        if (offset <= nameEnd)
        {
            var prefix = SafeSlice(text, nameStart, offset);
            return new TemplateCompletionContext(TemplateCompletionKind.Tag, prefix, element.TagName, isSqv);
        }

        if (isSqv && TryGetSqvEventModifierPrefix(
                text,
                offset,
                element.Start,
                out var modifierPrefix,
                out var eventAttribute))
            return new TemplateCompletionContext(
                TemplateCompletionKind.EventModifier,
                modifierPrefix,
                element.TagName,
                true,
                element.AttributeNames,
                eventAttribute,
                element.LocalNames);

        if (isSqv && SupportsSqvModel(element.TagName) && TryGetSqvModelModifierPrefix(
                text,
                offset,
                element.Start,
                out var modelModifierPrefix,
                out var modelAttribute))
            return new TemplateCompletionContext(
                TemplateCompletionKind.ModelModifier,
                modelModifierPrefix,
                element.TagName,
                true,
                element.AttributeNames,
                modelAttribute,
                element.LocalNames);

        if (TryGetClassPrefix(text, offset, out var classPrefix))
            return new TemplateCompletionContext(
                TemplateCompletionKind.CssClass,
                classPrefix,
                element.TagName,
                isSqv,
                element.AttributeNames);

        var token = GetTokenPrefix(text, offset, out var tokenStart);
        if (isSqv && TryGetSqvEventPrefix(token, out var eventPrefix))
            return new TemplateCompletionContext(
                TemplateCompletionKind.Event,
                eventPrefix,
                element.TagName,
                true,
                element.AttributeNames);
        if (isSqv && TryGetSqvSlotPrefix(token, out var slotPrefix))
            return new TemplateCompletionContext(
                TemplateCompletionKind.Slot,
                slotPrefix,
                element.TagName,
                true,
                element.AttributeNames);
        if (isSqv && TryGetSqvBindingPrefix(token, out var bindingPrefix))
            return new TemplateCompletionContext(
                TemplateCompletionKind.Binding,
                bindingPrefix,
                element.TagName,
                true,
                element.AttributeNames);
        if (isSqv && tokenStart > 0 && text[tokenStart - 1] == '@')
            return new TemplateCompletionContext(
                TemplateCompletionKind.Event,
                token,
                element.TagName,
                isSqv,
                element.AttributeNames);
        if (!isSqv && token.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            return new TemplateCompletionContext(
                TemplateCompletionKind.Event,
                token,
                element.TagName,
                false,
                element.AttributeNames);
        if (isSqv && (token.StartsWith("v-", StringComparison.OrdinalIgnoreCase) ||
                      token.StartsWith("#", StringComparison.Ordinal)))
            return new TemplateCompletionContext(
                TemplateCompletionKind.Directive,
                token,
                element.TagName,
                isSqv,
                element.AttributeNames);

        return new TemplateCompletionContext(
            TemplateCompletionKind.Attribute,
            token,
            element.TagName,
            isSqv,
            element.AttributeNames);
    }

    private static TemplateCompletionContext ContextFromPrefix(string text, int offset, bool isSqv)
    {
        var tagName = GetOpenTagName(text, offset);
        if (isSqv)
        {
            var elementStart = offset > 0 ? text.LastIndexOf('<', offset - 1) : -1;
            if (elementStart >= 0 && TryGetSqvEventModifierPrefix(
                    text,
                    offset,
                    elementStart,
                    out var modifierPrefix,
                    out var eventAttribute))
                return new TemplateCompletionContext(
                    TemplateCompletionKind.EventModifier,
                    modifierPrefix,
                    tagName,
                    true,
                    Array.Empty<string>(),
                    eventAttribute,
                    Array.Empty<string>());
            if (elementStart >= 0 && SupportsSqvModel(tagName) && TryGetSqvModelModifierPrefix(
                    text,
                    offset,
                    elementStart,
                    out var modelModifierPrefix,
                    out var modelAttribute))
                return new TemplateCompletionContext(
                    TemplateCompletionKind.ModelModifier,
                    modelModifierPrefix,
                    tagName,
                    true,
                    Array.Empty<string>(),
                    modelAttribute,
                    Array.Empty<string>());
        }
        var token = GetTokenPrefix(text, offset, out var tokenStart);
        if (isSqv && TryGetSqvEventPrefix(token, out var eventPrefix))
            return new TemplateCompletionContext(TemplateCompletionKind.Event, eventPrefix, tagName, true);
        if (isSqv && TryGetSqvSlotPrefix(token, out var slotPrefix))
            return new TemplateCompletionContext(TemplateCompletionKind.Slot, slotPrefix, tagName, true);
        if (isSqv && TryGetSqvBindingPrefix(token, out var bindingPrefix))
            return new TemplateCompletionContext(TemplateCompletionKind.Binding, bindingPrefix, tagName, true);
        if (isSqv && tokenStart > 0 && text[tokenStart - 1] == '@')
            return new TemplateCompletionContext(TemplateCompletionKind.Event, token, tagName, isSqv);
        if (!isSqv && token.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            return new TemplateCompletionContext(TemplateCompletionKind.Event, token, string.Empty, false);
        if (tokenStart > 0 && text[tokenStart - 1] == '<')
            return new TemplateCompletionContext(TemplateCompletionKind.Tag, token, string.Empty, isSqv);
        if (TryGetClassPrefix(text, offset, out var classPrefix))
            return new TemplateCompletionContext(TemplateCompletionKind.CssClass, classPrefix, string.Empty, isSqv);
        if (isSqv && token.StartsWith("v-", StringComparison.OrdinalIgnoreCase))
            return new TemplateCompletionContext(TemplateCompletionKind.Directive, token, tagName, isSqv);
        return new TemplateCompletionContext(TemplateCompletionKind.None, token, string.Empty, isSqv);
    }

    private static string GetOpenTagName(string text, int offset)
    {
        var open = offset > 0 ? text.LastIndexOf('<', offset - 1) : -1;
        if (open < 0 || open + 1 >= offset || text[open + 1] == '/') return string.Empty;
        var start = open + 1;
        var end = start;
        while (end < offset && (char.IsLetterOrDigit(text[end]) || text[end] is '_' or '-')) end++;
        return SafeSlice(text, start, end);
    }

    private static bool TryGetSqvSlotPrefix(string token, out string prefix)
    {
        if (token.StartsWith("#", StringComparison.Ordinal))
        {
            prefix = token.Substring(1);
            return true;
        }
        const string longhand = "v-slot:";
        if (token.StartsWith(longhand, StringComparison.OrdinalIgnoreCase))
        {
            prefix = token.Substring(longhand.Length);
            return true;
        }
        prefix = string.Empty;
        return false;
    }

    private static bool TryGetSqvEventPrefix(string token, out string prefix)
    {
        const string longhand = "v-on:";
        if (token.StartsWith(longhand, StringComparison.OrdinalIgnoreCase))
        {
            prefix = token.Substring(longhand.Length);
            return true;
        }
        prefix = string.Empty;
        return false;
    }

    private static bool TryGetSqvEventModifierPrefix(
        string text,
        int offset,
        int elementStart,
        out string prefix,
        out string attribute)
    {
        var start = offset;
        while (start > elementStart)
        {
            var previous = text[start - 1];
            if (char.IsWhiteSpace(previous) || previous is '<' or '>') break;
            start--;
        }
        attribute = SafeSlice(text, start, offset);
        var eventNameStart = attribute.StartsWith("@", StringComparison.Ordinal)
            ? 1
            : attribute.StartsWith("v-on:", StringComparison.OrdinalIgnoreCase)
                ? "v-on:".Length
                : -1;
        var lastDot = attribute.LastIndexOf('.');
        if (eventNameStart < 0 || lastDot <= eventNameStart || attribute.IndexOf('=') >= 0)
        {
            prefix = string.Empty;
            attribute = string.Empty;
            return false;
        }
        prefix = attribute.Substring(lastDot + 1);
        return prefix.All(IsTokenCharacter);
    }

    private static bool TryGetSqvModelModifierPrefix(
        string text,
        int offset,
        int elementStart,
        out string prefix,
        out string attribute)
    {
        var start = offset;
        while (start > elementStart)
        {
            var previous = text[start - 1];
            if (char.IsWhiteSpace(previous) || previous is '<' or '>') break;
            start--;
        }
        attribute = SafeSlice(text, start, offset);
        var lastDot = attribute.LastIndexOf('.');
        if (!attribute.StartsWith("v-model.", StringComparison.OrdinalIgnoreCase) ||
            lastDot < "v-model".Length ||
            attribute.IndexOf('=') >= 0)
        {
            prefix = string.Empty;
            attribute = string.Empty;
            return false;
        }
        prefix = attribute.Substring(lastDot + 1);
        return prefix.All(IsTokenCharacter);
    }

    private static bool TryGetSqvBindingPrefix(string token, out string prefix)
    {
        if (token.StartsWith(":", StringComparison.Ordinal))
        {
            prefix = token.Substring(1);
            return true;
        }
        const string longhand = "v-bind:";
        if (token.StartsWith(longhand, StringComparison.OrdinalIgnoreCase))
        {
            prefix = token.Substring(longhand.Length);
            return true;
        }
        prefix = string.Empty;
        return false;
    }

    private static string NormalizeSqvPropertyName(string name)
    {
        if (name.StartsWith(":", StringComparison.Ordinal)) name = name.Substring(1);
        else if (name.StartsWith("v-bind:", StringComparison.OrdinalIgnoreCase))
            name = name.Substring("v-bind:".Length);
        var modifier = name.IndexOf('.');
        return modifier < 0 ? name : name.Substring(0, modifier);
    }

    private static string NormalizeSqvEventName(string name)
    {
        if (name.StartsWith("@", StringComparison.Ordinal)) name = name.Substring(1);
        else if (name.StartsWith("v-on:", StringComparison.OrdinalIgnoreCase))
            name = name.Substring("v-on:".Length);
        var modifier = name.IndexOf('.');
        return modifier < 0 ? name : name.Substring(0, modifier);
    }

    private static TemplateElementContext FindSqxElement(
        IEnumerable<SqxSyntaxNode> nodes,
        int offset,
        IReadOnlyCollection<string> inheritedLocalNames = null)
    {
        inheritedLocalNames ??= Array.Empty<string>();
        foreach (var element in nodes.OfType<SqxElementSyntax>())
        {
            if (offset < element.Origin.Offset || offset > element.Origin.End) continue;
            var childLocalNames = inheritedLocalNames;
            var forLocalName = GetForLocalName(element);
            if (forLocalName != null)
                childLocalNames = inheritedLocalNames.Concat(new[] { forLocalName }).ToArray();
            return FindSqxElement(element.Children, offset, childLocalNames) ??
                   new TemplateElementContext(
                       element.TagName,
                       element.Origin.Offset,
                       element.Attributes
                           .Where(attribute =>
                               attribute.IsExpression &&
                               attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase) &&
                               attribute.Name.Length > 2)
                           .Select(attribute => attribute.ValueRange)
                           .ToArray(),
                       Array.Empty<SquareSourceRange>(),
                       element.Attributes.Select(attribute => attribute.Name).ToArray(),
                       element.Attributes
                           .Where(attribute => attribute.Value != null)
                           .Select(attribute => new TemplateAttributeValueContext(
                               attribute.Name,
                               attribute.ValueRange,
                               attribute.IsExpression))
                           .ToArray(),
                       element.Children.OfType<SqxExpressionSyntax>().Select(expression => expression.Origin).ToArray(),
                       inheritedLocalNames,
                       inheritedLocalNames);
        }
        return null;
    }

    private static string GetForLocalName(SqxElementSyntax element)
    {
        if (!element.TagName.Equals("For", StringComparison.Ordinal)) return null;
        var wrapper = element.Children.OfType<SqxExpressionSyntax>()
            .FirstOrDefault(expression => expression.Expression.TrimEnd().EndsWith("=>", StringComparison.Ordinal));
        if (wrapper == null) return null;
        var value = wrapper.Expression.Trim();
        value = value.Substring(0, value.Length - 2).Trim();
        if (value.StartsWith("(", StringComparison.Ordinal) && value.EndsWith(")", StringComparison.Ordinal))
            value = value.Substring(1, value.Length - 2).Trim();
        if (value.Length == 0 || !IsIdentifier(value)) return null;
        return value;
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || !(char.IsLetter(value[0]) || value[0] == '_')) return false;
        return value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');
    }

    private static TemplateElementContext FindSqvElement(
        IEnumerable<SqvSyntaxNode> nodes,
        int offset,
        IReadOnlyCollection<string> inheritedLocalNames = null)
    {
        inheritedLocalNames ??= Array.Empty<string>();
        foreach (var element in nodes.OfType<SqvElementSyntax>())
        {
            if (offset < element.Origin.Offset || offset > element.Origin.End) continue;
            var localNames = inheritedLocalNames
                .Concat(GetSqvForLocalNames(element))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return FindSqvElement(element.Children, offset, localNames) ??
                   new TemplateElementContext(
                       element.TagName,
                       element.Origin.Offset,
                       element.Attributes
                           .Where(attribute => attribute.DirectiveName == "on" && attribute.Value != null)
                           .Select(attribute => attribute.ValueRange)
                           .ToArray(),
                       element.Attributes
                           .Where(attribute =>
                               attribute.DirectiveName == "on" &&
                               attribute.Value != null &&
                               attribute.Modifiers.Any(modifier => modifier is "stop" or "prevent"))
                           .Select(attribute => attribute.ValueRange)
                           .ToArray(),
                       element.Attributes.Select(attribute => attribute.Name).ToArray(),
                       element.Attributes
                           .Where(attribute => attribute.Value != null)
                           .Select(attribute => new TemplateAttributeValueContext(
                               attribute.Name,
                               attribute.ValueRange,
                               attribute.DirectiveName != null))
                           .ToArray(),
                       element.Children.OfType<SqvInterpolationSyntax>().Select(expression => expression.Origin).ToArray(),
                       inheritedLocalNames,
                       localNames);
        }
        return null;
    }

    private static IReadOnlyCollection<string> GetSqvForLocalNames(SqvElementSyntax element)
    {
        var loop = element.Attributes.FirstOrDefault(attribute => attribute.DirectiveName == "for");
        if (loop == null || string.IsNullOrWhiteSpace(loop.Value)) return Array.Empty<string>();
        var marker = loop.Value.IndexOf(" in ", StringComparison.Ordinal);
        if (marker < 0) marker = loop.Value.IndexOf(" of ", StringComparison.Ordinal);
        if (marker < 0) return Array.Empty<string>();
        var binding = loop.Value.Substring(0, marker).Trim().Trim('(', ')');
        return binding.Split(',')
            .Select(name => name.Trim())
            .Where(IsIdentifier)
            .ToArray();
    }

    private static int FindHeaderEnd(string text, int elementStart, int offset)
    {
        var start = Math.Min(Math.Max(elementStart, 0), text.Length);
        var limit = Math.Min(text.Length, Math.Max(offset, start));
        var quote = '\0';
        for (var index = start; index < limit; index++)
        {
            var value = text[index];
            if (quote != '\0')
            {
                if (value == quote) quote = '\0';
                continue;
            }
            if (value is '"' or '\'') quote = value;
            else if (value == '>') return index;
        }
        return -1;
    }

    private static bool TryGetClosingTagPrefix(string text, int offset, out string prefix)
    {
        var open = offset > 0 ? text.LastIndexOf('<', offset - 1) : -1;
        if (open < 0 || open + 1 >= text.Length || text[open + 1] != '/' ||
            text.IndexOf('>', open, offset - open) >= 0)
        {
            prefix = string.Empty;
            return false;
        }
        prefix = SafeSlice(text, open + 2, offset);
        return prefix.All(IsTokenCharacter);
    }

    private sealed class TemplateElementContext
    {
        public TemplateElementContext(
            string tagName,
            int start,
            IReadOnlyList<SquareSourceRange> eventValueRanges,
            IReadOnlyList<SquareSourceRange> eventParameterRanges,
            IReadOnlyCollection<string> attributeNames,
            IReadOnlyList<TemplateAttributeValueContext> attributeValues,
            IReadOnlyList<SquareSourceRange> expressionRanges,
            IReadOnlyCollection<string> attributeLocalNames,
            IReadOnlyCollection<string> localNames)
        {
            TagName = tagName ?? string.Empty;
            Start = start;
            EventValueRanges = eventValueRanges ?? throw new ArgumentNullException(nameof(eventValueRanges));
            EventParameterRanges = eventParameterRanges ?? throw new ArgumentNullException(nameof(eventParameterRanges));
            AttributeNames = attributeNames ?? throw new ArgumentNullException(nameof(attributeNames));
            AttributeValues = attributeValues ?? throw new ArgumentNullException(nameof(attributeValues));
            ExpressionRanges = expressionRanges ?? throw new ArgumentNullException(nameof(expressionRanges));
            AttributeLocalNames = attributeLocalNames ?? throw new ArgumentNullException(nameof(attributeLocalNames));
            LocalNames = localNames ?? throw new ArgumentNullException(nameof(localNames));
        }

        public string TagName { get; }
        public int Start { get; }
        public IReadOnlyList<SquareSourceRange> EventValueRanges { get; }
        public IReadOnlyList<SquareSourceRange> EventParameterRanges { get; }
        public IReadOnlyCollection<string> AttributeNames { get; }
        public IReadOnlyList<TemplateAttributeValueContext> AttributeValues { get; }
        public IReadOnlyList<SquareSourceRange> ExpressionRanges { get; }
        public IReadOnlyCollection<string> AttributeLocalNames { get; }
        public IReadOnlyCollection<string> LocalNames { get; }

        public bool ContainsEventValue(int offset) =>
            EventValueRanges.Any(range => offset >= range.Offset && offset <= range.End);

        public bool RequiresEventParameter(int offset) =>
            EventParameterRanges.Any(range => offset >= range.Offset && offset <= range.End);

        public bool ContainsExpression(int offset) =>
            ExpressionRanges.Any(range => offset >= range.Offset && offset <= range.End);

        public bool TryGetAttributeValue(int offset, out TemplateAttributeValueContext value)
        {
            value = AttributeValues.FirstOrDefault(item => offset >= item.Range.Offset && offset <= item.Range.End);
            return value != null;
        }
    }

    private sealed class TemplateAttributeValueContext
    {
        public TemplateAttributeValueContext(string name, SquareSourceRange range, bool isExpression)
        {
            Name = name ?? string.Empty;
            Range = range;
            IsExpression = isExpression;
        }

        public string Name { get; }
        public SquareSourceRange Range { get; }
        public bool IsExpression { get; }
    }

    private static string GetTokenPrefix(string text, int offset, out int start)
    {
        start = offset;
        while (start > 0 && IsTokenCharacter(text[start - 1])) start--;
        return SafeSlice(text, start, offset);
    }

    private static bool TryGetClassPrefix(string text, int offset, out string prefix)
    {
        var start = offset;
        while (start > 0 && IsTokenCharacter(text[start - 1])) start--;

        var quote = start - 1;
        while (quote >= 0 && text[quote] is not ('"' or '\'')) quote--;
        if (quote < 0)
        {
            prefix = string.Empty;
            return false;
        }

        var tagStart = text.LastIndexOf('<', quote);
        var tagEnd = text.LastIndexOf('>', quote);
        if (tagStart <= tagEnd)
        {
            prefix = string.Empty;
            return false;
        }

        var beforeQuote = text.Substring(tagStart + 1, quote - tagStart - 1);
        if (!Regex.IsMatch(beforeQuote, @"\bclass\s*=\s*$", RegexOptions.IgnoreCase) &&
            !Regex.IsMatch(beforeQuote, @"(?::class|v-bind:class)\s*=\s*$", RegexOptions.IgnoreCase))
        {
            prefix = string.Empty;
            return false;
        }

        prefix = SafeSlice(text, start, offset);
        return true;
    }

    private static IReadOnlyCollection<string> ExtractCssClassNames(string text)
    {
        var document = SquareDocumentService.ParseSyntaxTree(text, "Styles.sqx").ParsedSqxDocument;
        var style = document?.Syntax?.Style?.Css;
        if (style == null) return Array.Empty<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selector in style.Rules.SelectMany(rule => rule.Selectors))
        {
            var value = selector.Text;
            for (var index = 0; index + 1 < value.Length; index++)
            {
                if (value[index] != '.' || !IsCssIdentifierStart(value[index + 1])) continue;
                var start = ++index;
                while (index + 1 < value.Length && IsCssIdentifierPart(value[index + 1])) index++;
                names.Add(value.Substring(start, index - start + 1));
            }
        }
        return names;
    }

    private static bool IsCssIdentifierStart(char value) =>
        char.IsLetter(value) || value is '_' or '-';

    private static bool IsCssIdentifierPart(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '-';

    private static string GetCssClassTokenPrefix(string text, int valueStart, int offset)
    {
        var start = Math.Min(Math.Max(offset, valueStart), text.Length);
        while (start > valueStart && IsCssIdentifierPart(text[start - 1])) start--;
        return SafeSlice(text, start, offset);
    }

    private static bool IsTokenCharacter(char value) =>
        char.IsLetterOrDigit(value) || value is '-' or '_' or ':' or '#';

    private static string SafeSlice(string text, int start, int end)
    {
        start = Math.Min(Math.Max(start, 0), text.Length);
        end = Math.Min(Math.Max(end, start), text.Length);
        return text.Substring(start, end - start);
    }
}
