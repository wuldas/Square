using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Square.Compiler.Syntax;

namespace Square.Compiler.LanguageServices;

public enum CSharpScriptCompletionKind
{
    None,
    General,
    Member,
    Namespace,
    Type,
    Attribute,
    AttributeArgument
}

public sealed class CSharpScriptCompletionContext
{
    public CSharpScriptCompletionContext(
        CSharpScriptCompletionKind kind,
        string prefix,
        string receiver,
        int position)
    {
        Kind = kind;
        Prefix = prefix ?? string.Empty;
        Receiver = receiver ?? string.Empty;
        Position = position;
    }

    public CSharpScriptCompletionKind Kind { get; }
    public string Prefix { get; }
    public string Receiver { get; }
    public int Position { get; }
}

public static class CSharpScriptCompletionService
{
    private static readonly string[] Namespaces =
    {
        "System", "System.Collections.Generic", "System.Linq", "System.Threading", "System.Threading.Tasks",
        "Square.Controls", "Square.Events", "Square.Graphics", "Square.Runtime.Binding", "Square.UI"
    };

    private static readonly string[] Keywords =
    {
        "private", "public", "protected", "internal", "static", "readonly", "const", "partial", "class",
        "void", "var", "new", "return", "if", "else", "switch", "case", "for", "foreach", "while",
        "break", "continue", "async", "await", "try", "catch", "finally", "throw", "true", "false", "null",
        "this", "base", "override", "virtual", "using", "namespace"
    };

    private static readonly string[] CommonTypes =
    {
        "bool", "byte", "char", "decimal", "double", "float", "int", "long", "object", "short", "string",
        "uint", "ulong", "ushort", "Action", "Func", "Task", "CancellationToken", "List", "Dictionary",
        "HashSet", "IEnumerable", "IReadOnlyList", "Event", "ObservableValue", "Color"
    };

    private static readonly string[] Attributes =
        { "Prop", "SlotContract", "SqxDirective", "SqxDirectiveAssembly", "Obsolete", "AttributeUsage" };

    private static readonly IReadOnlyDictionary<string, string[]> AttributeArguments =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Prop"] = ["Required", "Default"],
            ["SqxDirective"] = ["Aliases", "ParentTag", "AllowedChildTags", "SkipStandaloneEmit", "Pattern", "RuntimeTypeName", "FieldPrefix", "PrimaryAttribute"],
            ["AttributeUsage"] = ["AllowMultiple", "Inherited"]
        };

    public static CSharpScriptCompletionContext GetContext(string text, int offset, string sourcePath)
    {
        text ??= string.Empty;
        offset = Math.Min(Math.Max(offset, 0), text.Length);
        var script = SquareDocumentService.ParseSyntaxTree(text, sourcePath ?? string.Empty)
            .ParsedSqxDocument?.Syntax?.Script;
        if (script?.CSharp == null || offset < script.ContentRange.Offset || offset > script.ContentRange.End)
            return new CSharpScriptCompletionContext(CSharpScriptCompletionKind.None, string.Empty, string.Empty, offset);
        if (IsInsideCommentOrString(script.CSharp, offset))
            return new CSharpScriptCompletionContext(CSharpScriptCompletionKind.None, string.Empty, string.Empty, offset);

        var lineStart = text.LastIndexOf('\n', Math.Max(0, offset - 1));
        lineStart = lineStart < script.ContentRange.Offset ? script.ContentRange.Offset : lineStart + 1;
        var linePrefix = text.Substring(lineStart, offset - lineStart);
        var trimmed = linePrefix.TrimStart();
        if (trimmed.StartsWith("using ", StringComparison.Ordinal) && trimmed.IndexOf(';') < 0)
        {
            var usingStart = lineStart + linePrefix.IndexOf("using ", StringComparison.Ordinal) + "using ".Length;
            while (usingStart < offset && char.IsWhiteSpace(text[usingStart])) usingStart++;
            return new CSharpScriptCompletionContext(
                CSharpScriptCompletionKind.Namespace,
                text.Substring(usingStart, offset - usingStart),
                string.Empty,
                offset);
        }

        var prefix = GetIdentifierPrefix(text, script.ContentRange.Offset, offset, out var prefixStart);
        if (TryGetAttributeContext(text, script.ContentRange.Offset, offset, prefixStart, out var attributeName))
            return new CSharpScriptCompletionContext(
                attributeName.Length == 0 ? CSharpScriptCompletionKind.Attribute : CSharpScriptCompletionKind.AttributeArgument,
                prefix,
                attributeName,
                offset);
        if (prefixStart > script.ContentRange.Offset && text[prefixStart - 1] == '.')
        {
            var receiver = GetReceiver(text, script.ContentRange.Offset, prefixStart - 1);
            return new CSharpScriptCompletionContext(CSharpScriptCompletionKind.Member, prefix, receiver, offset);
        }
        if (IsTypeContext(text, script.ContentRange.Offset, prefixStart))
            return new CSharpScriptCompletionContext(CSharpScriptCompletionKind.Type, prefix, string.Empty, offset);
        return new CSharpScriptCompletionContext(CSharpScriptCompletionKind.General, prefix, string.Empty, offset);
    }

    public static IReadOnlyList<TemplateCompletionItem> GetItems(
        CSharpScriptCompletionContext context,
        string text,
        string sourcePath)
    {
        if (context == null || context.Kind == CSharpScriptCompletionKind.None)
            return Array.Empty<TemplateCompletionItem>();
        var document = SquareDocumentService.ParseSyntaxTree(text, sourcePath ?? string.Empty).ParsedSqxDocument;
        var script = document?.Syntax?.Script?.CSharp;
        if (script == null) return Array.Empty<TemplateCompletionItem>();

        IEnumerable<TemplateCompletionItem> items = context.Kind switch
        {
            CSharpScriptCompletionKind.Namespace => Namespaces.Select(name => Item(name, 9, "C# namespace")),
            CSharpScriptCompletionKind.Attribute => Attributes.Select(name => Item(name, 7, "C# attribute")),
            CSharpScriptCompletionKind.AttributeArgument => GetAttributeArgumentItems(context.Receiver),
            CSharpScriptCompletionKind.Type => GetTypeItems(),
            CSharpScriptCompletionKind.Member => GetMemberItems(document.Syntax, script, context),
            _ => GetGeneralItems(script, context.Position)
        };
        return items
            .Where(item => item.Label.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Label)
            .ToArray();
    }

    public static string GetHoverDetail(string text, int offset, string sourcePath)
    {
        text ??= string.Empty;
        offset = Math.Min(Math.Max(offset, 0), text.Length);
        var script = SquareDocumentService.ParseSyntaxTree(text, sourcePath ?? string.Empty)
            .ParsedSqxDocument?.Syntax?.Script;
        if (script?.CSharp == null || offset < script.ContentRange.Offset || offset > script.ContentRange.End)
            return null;
        var tokenStart = offset;
        if (tokenStart == text.Length || tokenStart > 0 && !IsIdentifierPart(text[tokenStart])) tokenStart--;
        while (tokenStart >= script.ContentRange.Offset && IsIdentifierPart(text[tokenStart])) tokenStart--;
        tokenStart++;
        var tokenEnd = Math.Max(tokenStart, offset);
        while (tokenEnd < script.ContentRange.End && IsIdentifierPart(text[tokenEnd])) tokenEnd++;
        if (tokenEnd <= tokenStart) return null;
        var token = text.Substring(tokenStart, tokenEnd - tokenStart);
        var context = GetContext(text, tokenEnd, sourcePath);
        return GetItems(context, text, sourcePath)
            .FirstOrDefault(item => item.Label.Equals(token, StringComparison.Ordinal))?.Detail;
    }

    private static IEnumerable<TemplateCompletionItem> GetGeneralItems(CSharpScriptSyntax script, int position)
    {
        foreach (var keyword in Keywords) yield return Item(keyword, 14, "C# keyword");
        foreach (var type in GetTypeItems()) yield return type;
        foreach (var member in GetScriptMemberItems(script)) yield return member;
        foreach (var local in GetVisibleLocals(script, position)) yield return local;
        if (!script.Members.OfType<MethodDeclarationSyntax>()
                .Any(method => Contains(script.SourceMap.ToDocumentRange(method.Span), position)))
        {
            foreach (var lifecycle in LifecycleItems()) yield return lifecycle;
        }
    }

    private static IEnumerable<TemplateCompletionItem> GetAttributeArgumentItems(string attributeName)
    {
        var normalized = attributeName.EndsWith("Attribute", StringComparison.Ordinal)
            ? attributeName.Substring(0, attributeName.Length - "Attribute".Length)
            : attributeName;
        return AttributeArguments.TryGetValue(normalized, out var arguments)
            ? arguments.Select(name => Item(name, 10, normalized + " named argument"))
            : Array.Empty<TemplateCompletionItem>();
    }

    private static IEnumerable<TemplateCompletionItem> GetTypeItems()
    {
        foreach (var type in CommonTypes) yield return Item(type, 7, "C# type");
        foreach (var component in TemplateCatalog.BuiltIn.Components)
            yield return Item(component.TagName, 7, component.TypeName);
    }

    private static IEnumerable<TemplateCompletionItem> GetScriptMemberItems(CSharpScriptSyntax script)
    {
        foreach (var field in script.Members.OfType<FieldDeclarationSyntax>())
            foreach (var variable in field.Declaration.Variables)
                yield return Item(variable.Identifier.ValueText, 5,
                    field.Declaration.Type + " " + variable.Identifier.ValueText);
        foreach (var property in script.Members.OfType<PropertyDeclarationSyntax>())
            yield return Item(property.Identifier.ValueText, 10, property.Type + " " + property.Identifier.ValueText);
        foreach (var method in script.Members.OfType<MethodDeclarationSyntax>())
            yield return new TemplateCompletionItem(
                method.Identifier.ValueText,
                2,
                method.ReturnType + " " + method.Identifier.ValueText + method.ParameterList,
                method.Identifier.ValueText + (method.ParameterList.Parameters.Count == 0 ? "()" : string.Empty));
    }

    private static IEnumerable<TemplateCompletionItem> GetVisibleLocals(CSharpScriptSyntax script, int position)
    {
        var method = script.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(candidate => Contains(script.SourceMap.ToDocumentRange(candidate.Span), position));
        if (method == null) yield break;
        foreach (var parameter in method.ParameterList.Parameters)
            yield return Item(parameter.Identifier.ValueText, 6,
                (parameter.Type?.ToString() ?? "var") + " " + parameter.Identifier.ValueText);
        foreach (var variable in method.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            var range = script.SourceMap.ToDocumentRange(variable.Span);
            if (range.Offset >= position) continue;
            var declaration = variable.Parent as VariableDeclarationSyntax;
            yield return Item(variable.Identifier.ValueText, 6,
                (declaration?.Type.ToString() ?? "var") + " " + variable.Identifier.ValueText);
        }
        foreach (var statement in method.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            var range = script.SourceMap.ToDocumentRange(statement.Identifier.Span);
            if (range.Offset < position)
                yield return Item(statement.Identifier.ValueText, 6,
                    statement.Type + " " + statement.Identifier.ValueText);
        }
    }

    private static IEnumerable<TemplateCompletionItem> GetMemberItems(
        ComponentDocumentSyntax document,
        CSharpScriptSyntax script,
        CSharpScriptCompletionContext context)
    {
        if (context.Receiver.Equals("this", StringComparison.Ordinal))
            return GetScriptMemberItems(script).Concat(GetMembersForType("__Component"));
        var typeName = ResolveExpressionType(document, script, context.Receiver, context.Position);
        return GetMembersForType(typeName);
    }

    private static string ResolveExpressionType(
        ComponentDocumentSyntax document,
        CSharpScriptSyntax script,
        string expression,
        int position)
    {
        var parts = expression.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return string.Empty;
        var type = ResolveIdentifierType(document, script, parts[0], position);
        for (var index = 1; index < parts.Length && type.Length > 0; index++)
            type = ResolveMemberType(type, parts[index]);
        return type;
    }

    private static string ResolveIdentifierType(
        ComponentDocumentSyntax document,
        CSharpScriptSyntax script,
        string identifier,
        int position)
    {
        if (identifier == "this") return "__Component";
        var field = script.Members.OfType<FieldDeclarationSyntax>()
            .FirstOrDefault(item => item.Declaration.Variables.Any(variable => variable.Identifier.ValueText == identifier));
        if (field != null) return InferType(field.Declaration.Type, field.Declaration.Variables
            .First(variable => variable.Identifier.ValueText == identifier).Initializer?.Value);
        var property = script.Members.OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault(item => item.Identifier.ValueText == identifier);
        if (property != null) return property.Type.ToString();

        var method = script.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(candidate => Contains(script.SourceMap.ToDocumentRange(candidate.Span), position));
        var parameter = method?.ParameterList.Parameters.FirstOrDefault(item => item.Identifier.ValueText == identifier);
        if (parameter?.Type != null) return parameter.Type.ToString();
        var variable = method?.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Where(item => item.Identifier.ValueText == identifier &&
                           script.SourceMap.ToDocumentRange(item.Span).Offset < position)
            .LastOrDefault();
        if (variable?.Parent is VariableDeclarationSyntax declaration)
            return InferType(declaration.Type, variable.Initializer?.Value);

        var refType = FindTemplateRefType(document.Template, identifier);
        if (refType.Length > 0) return refType;
        return CommonTypes.Concat(TemplateCatalog.BuiltIn.Components.Select(item => item.TagName))
            .FirstOrDefault(item => item.Equals(identifier, StringComparison.Ordinal)) ?? string.Empty;
    }

    private static string InferType(TypeSyntax syntax, ExpressionSyntax initializer)
    {
        if (!syntax.ToString().Equals("var", StringComparison.Ordinal)) return syntax.ToString();
        if (initializer is ObjectCreationExpressionSyntax creation) return creation.Type.ToString();
        if (initializer is ImplicitObjectCreationExpressionSyntax) return string.Empty;
        if (initializer is LiteralExpressionSyntax literal)
        {
            if (literal.IsKind(SyntaxKind.StringLiteralExpression)) return "string";
            if (literal.IsKind(SyntaxKind.TrueLiteralExpression) || literal.IsKind(SyntaxKind.FalseLiteralExpression)) return "bool";
            if (literal.IsKind(SyntaxKind.NumericLiteralExpression)) return "int";
        }
        return string.Empty;
    }

    private static string FindTemplateRefType(TemplateSectionSyntax template, string identifier)
    {
        if (template?.SqxSyntax != null)
            return FindSqxRef(template.SqxSyntax.Roots, identifier);
        if (template?.SqvSyntax != null)
            return FindSqvRef(template.SqvSyntax.Roots, identifier);
        return string.Empty;
    }

    private static string FindSqxRef(IEnumerable<SqxSyntaxNode> nodes, string identifier)
    {
        foreach (var element in nodes.OfType<SqxElementSyntax>())
        {
            var reference = element.Attributes.FirstOrDefault(attribute =>
                attribute.Name.Equals("ref", StringComparison.OrdinalIgnoreCase));
            if (reference != null && reference.Value != null &&
                reference.Value.Trim().Equals(identifier, StringComparison.Ordinal)) return element.TagName;
            var nested = FindSqxRef(element.Children, identifier);
            if (nested.Length > 0) return nested;
        }
        return string.Empty;
    }

    private static string FindSqvRef(IEnumerable<SqvSyntaxNode> nodes, string identifier)
    {
        foreach (var element in nodes.OfType<SqvElementSyntax>())
        {
            var reference = element.Attributes.FirstOrDefault(attribute =>
                attribute.Name.Equals("ref", StringComparison.OrdinalIgnoreCase));
            if (reference != null && reference.Value != null &&
                reference.Value.Trim().Equals(identifier, StringComparison.Ordinal)) return element.TagName;
            var nested = FindSqvRef(element.Children, identifier);
            if (nested.Length > 0) return nested;
        }
        return string.Empty;
    }

    private static IEnumerable<TemplateCompletionItem> GetMembersForType(string typeName)
    {
        var normalized = NormalizeType(typeName);
        foreach (var member in CommonObjectMembers()) yield return member;
        if (normalized == "string")
        {
            foreach (var member in Members(
                         Property("Length", "int"),
                         Method("Contains", "bool Contains(string value)"),
                         Method("StartsWith", "bool StartsWith(string value)"),
                         Method("EndsWith", "bool EndsWith(string value)"),
                         Method("Trim", "string Trim()"),
                         Method("ToUpperInvariant", "string ToUpperInvariant()"),
                         Method("ToLowerInvariant", "string ToLowerInvariant()"))) yield return member;
        }
        if (normalized.StartsWith("ObservableValue<", StringComparison.Ordinal))
        {
            foreach (var member in Members(
                         Property("Value", GenericArgument(normalized)),
                         Method("Subscribe", "IDisposable Subscribe(Action<T> handler)"),
                         Method("Notify", "void Notify()"))) yield return member;
        }
        if (IsCollectionType(normalized))
        {
            foreach (var member in Members(
                         Property("Count", "int"),
                         Method("Add", "void Add(T item)"),
                         Method("Remove", "bool Remove(T item)"),
                         Method("Clear", "void Clear()"),
                         Method("Contains", "bool Contains(T item)"))) yield return member;
        }
        if (normalized is "Event" or "Square.Events.Event")
        {
            foreach (var member in Members(
                         Property("Target", "Element"),
                         Property("CurrentTarget", "Element"),
                         Property("EventPhase", "EventPhase"),
                         Method("PreventDefault", "void PreventDefault()"),
                         Method("StopPropagation", "void StopPropagation()"))) yield return member;
        }
        if (normalized == "StyleAccessor")
        {
            foreach (var member in Members(
                         Property("CssText", "string"),
                         Method("Get", "string? Get(string property)"),
                         Method("Set", "void Set(string property, string value)"),
                         Method("SetProperty", "void SetProperty(string property, string value)"),
                         Method("RemoveProperty", "void RemoveProperty(string property)"))) yield return member;
        }
        if (normalized == "ClassListAccessor")
        {
            foreach (var member in Members(
                         Method("Add", "void Add(string name)"),
                         Method("Remove", "bool Remove(string name)"),
                         Method("Contains", "bool Contains(string name)"),
                         Method("Toggle", "bool Toggle(string name)"))) yield return member;
        }
        if (normalized == "ChildrenCollection")
        {
            foreach (var member in Members(
                         Property("Count", "int"),
                         Method("Add", "void Add(Element child)"),
                         Method("Remove", "bool Remove(Element child)"),
                         Method("Clear", "void Clear()"))) yield return member;
        }
        if (normalized == "Color")
        {
            foreach (var member in Members(
                         Property("Transparent", "Color"),
                         Property("Black", "Color"),
                         Property("White", "Color"),
                         Property("Red", "Color"),
                         Property("Green", "Color"),
                         Property("Blue", "Color"),
                         Method("FromRgb", "Color FromRgb(byte r, byte g, byte b)"),
                         Method("FromRgba", "Color FromRgba(byte r, byte g, byte b, byte a)"),
                         Method("Parse", "Color Parse(string value)"),
                         Method("TryParse", "bool TryParse(string value, out Color color)"))) yield return member;
        }
        if (IsElementType(normalized))
        {
            foreach (var member in ElementMembers()) yield return member;
            foreach (var member in ControlMembers(normalized)) yield return member;
        }
    }

    private static string ResolveMemberType(string typeName, string memberName)
    {
        var normalized = NormalizeType(typeName);
        if (IsElementType(normalized))
        {
            if (memberName == "Style") return "StyleAccessor";
            if (memberName == "ClassList") return "ClassListAccessor";
            if (memberName == "Children") return "ChildrenCollection";
            if (memberName == "Parent") return "Element";
        }
        if (normalized.StartsWith("ObservableValue<", StringComparison.Ordinal) && memberName == "Value")
            return GenericArgument(normalized);
        return string.Empty;
    }

    private static IEnumerable<TemplateCompletionItem> ElementMembers() => Members(
        Property("Style", "StyleAccessor"),
        Property("ClassList", "ClassListAccessor"),
        Property("Children", "ChildrenCollection"),
        Property("Id", "string?"),
        Property("Parent", "Element?"),
        Property("TagName", "string"),
        Method("AddEventListener", "void AddEventListener(string type, Action<Event> handler)"),
        Method("SetProperty", "void SetProperty(string name, object value)"),
        Method("BindProperty", "void BindProperty(string name, object value)"),
        Method("QuerySelector", "Element? QuerySelector(string selector)"));

    private static IEnumerable<TemplateCompletionItem> ControlMembers(string typeName)
    {
        return typeName switch
        {
            "Button" => Members(Property("TextContent", "string"), Property("IsDisabled", "bool")),
            "Input" or "TextArea" => Members(Property("Value", "string"), Property("Placeholder", "string"), Property("IsDisabled", "bool")),
            "CheckBox" or "Radio" => Members(Property("IsChecked", "bool"), Property("TextContent", "string"), Property("IsDisabled", "bool")),
            "Select" => Members(Property("Value", "string"), Property("Placeholder", "string"), Property("IsDisabled", "bool")),
            "Text" or "ListItem" or "TreeItem" => Members(Property("TextContent", "string"), Property("Color", "Color"), Property("FontSize", "float")),
            "Image" => Members(Property("Source", "string")),
            "List" or "VirtualList" => Members(Property("SelectedIndex", "int")),
            "Splitter" or "SplitContainer" => Members(Property("Value", "float")),
            _ => Array.Empty<TemplateCompletionItem>()
        };
    }

    private static IEnumerable<TemplateCompletionItem> CommonObjectMembers() => Members(
        Method("ToString", "string ToString()"),
        Method("Equals", "bool Equals(object value)"),
        Method("GetHashCode", "int GetHashCode()"));

    private static TemplateCompletionItem Property(string name, string type) => Item(name, 10, type + " " + name);
    private static IEnumerable<TemplateCompletionItem> LifecycleItems() => Members(
        new TemplateCompletionItem(
            "OnPropChanged",
            2,
            "protected override void OnPropChanged(string name)",
            "protected override void OnPropChanged(string name)\n{\n    base.OnPropChanged(name);\n}"),
        new TemplateCompletionItem(
            "OnAttachedCore",
            2,
            "protected override void OnAttachedCore()",
            "protected override void OnAttachedCore()\n{\n    base.OnAttachedCore();\n}"),
        new TemplateCompletionItem(
            "OnDetachedCore",
            2,
            "protected override void OnDetachedCore()",
            "protected override void OnDetachedCore()\n{\n    base.OnDetachedCore();\n}"),
        new TemplateCompletionItem(
            "OnLoadedCore",
            2,
            "protected override void OnLoadedCore()",
            "protected override void OnLoadedCore()\n{\n    base.OnLoadedCore();\n}"),
        new TemplateCompletionItem(
            "OnUnloadedCore",
            2,
            "protected override void OnUnloadedCore()",
            "protected override void OnUnloadedCore()\n{\n    base.OnUnloadedCore();\n}"));

    private static TemplateCompletionItem Method(string name, string detail) =>
        new(name, 2, detail, name + (detail.Contains("()", StringComparison.Ordinal) ? "()" : string.Empty));
    private static IEnumerable<TemplateCompletionItem> Members(params TemplateCompletionItem[] members) => members;
    private static TemplateCompletionItem Item(string label, int kind, string detail) => new(label, kind, detail, label);

    private static string NormalizeType(string typeName)
    {
        var value = (typeName ?? string.Empty).Replace("global::", string.Empty).Trim();
        if (value.Length == 0) return string.Empty;
        var nullable = value.EndsWith("?", StringComparison.Ordinal) ? value.Length - 1 : value.Length;
        value = value.Substring(0, nullable);
        if (value.Length == 0) return string.Empty;
        var generic = value.IndexOf('<');
        var typeEnd = generic < 0 ? value.Length : generic;
        var dot = value.LastIndexOf('.', typeEnd - 1, typeEnd);
        return dot < 0 ? value : value.Substring(dot + 1);
    }

    private static string GenericArgument(string typeName)
    {
        var open = typeName.IndexOf('<');
        var close = typeName.LastIndexOf('>');
        return open >= 0 && close > open ? typeName.Substring(open + 1, close - open - 1) : "T";
    }

    private static bool IsCollectionType(string typeName) =>
        typeName.StartsWith("List<", StringComparison.Ordinal) ||
        typeName.StartsWith("IList<", StringComparison.Ordinal) ||
        typeName.StartsWith("ICollection<", StringComparison.Ordinal) ||
        typeName.StartsWith("ObservableCollection<", StringComparison.Ordinal);

    private static bool IsElementType(string typeName) =>
        typeName is "Element" or "UIElement" or "__Component" ||
        TemplateCatalog.BuiltIn.Components.Any(item => item.TagName.Equals(typeName, StringComparison.OrdinalIgnoreCase));

    private static bool Contains(SquareSourceRange range, int position) =>
        position >= range.Offset && position <= range.End;

    private static bool IsInsideCommentOrString(CSharpScriptSyntax script, int documentOffset)
    {
        var syntheticOffset = script.SourceMap.ToSyntheticOffset(documentOffset);
        var root = script.Root;
        if (root.FullSpan.Length == 0) return false;
        var position = Math.Min(Math.Max(syntheticOffset - 1, 0), root.FullSpan.End - 1);
        var trivia = root.FindTrivia(position, findInsideTrivia: true);
        if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
            trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
            trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
            trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)) return true;
        var token = root.FindToken(position, findInsideTrivia: true);
        return token.Parent?.AncestorsAndSelf().Any(node =>
            node is LiteralExpressionSyntax literal &&
                (literal.IsKind(SyntaxKind.StringLiteralExpression) || literal.IsKind(SyntaxKind.CharacterLiteralExpression)) ||
            node is InterpolatedStringExpressionSyntax) == true;
    }

    private static string GetIdentifierPrefix(string text, int contentStart, int offset, out int start)
    {
        start = offset;
        while (start > contentStart && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) start--;
        return text.Substring(start, offset - start);
    }

    private static string GetReceiver(string text, int contentStart, int dot)
    {
        var start = dot;
        while (start > contentStart &&
               (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] is '_' or '.')) start--;
        return text.Substring(start, dot - start).Trim('.');
    }

    private static char PreviousNonWhitespace(string text, int contentStart, int offset)
    {
        for (var index = offset - 1; index >= contentStart; index--)
            if (!char.IsWhiteSpace(text[index])) return text[index];
        return '\0';
    }

    private static bool TryGetAttributeContext(
        string text,
        int contentStart,
        int offset,
        int prefixStart,
        out string attributeName)
    {
        attributeName = string.Empty;
        var open = text.LastIndexOf('[', Math.Max(contentStart, prefixStart - 1));
        if (open < contentStart) return false;
        var close = text.LastIndexOf(']', Math.Max(contentStart, prefixStart - 1));
        if (close > open) return false;
        var previous = PreviousNonWhitespace(text, contentStart, open);
        if (previous != '\0' && previous is not (';' or '{' or '}' or ']' or '(' or ',')) return false;
        var position = open + 1;
        while (position < offset && char.IsWhiteSpace(text[position])) position++;
        var nameStart = position;
        while (position < offset && IsIdentifierPart(text[position])) position++;
        if (position == nameStart) return prefixStart >= nameStart;
        var name = text.Substring(nameStart, position - nameStart);
        while (position < offset && char.IsWhiteSpace(text[position])) position++;
        if (position >= offset || text[position] != '(') return prefixStart >= nameStart && prefixStart <= position;
        attributeName = name;
        return true;
    }

    private static bool IsTypeContext(string text, int contentStart, int prefixStart)
    {
        var start = Math.Max(contentStart, prefixStart - 32);
        var before = text.Substring(start, prefixStart - start).TrimEnd();
        return before.EndsWith("new", StringComparison.Ordinal) ||
               before.EndsWith("typeof(", StringComparison.Ordinal) ||
               before.EndsWith(" is", StringComparison.Ordinal) ||
               before.EndsWith(" as", StringComparison.Ordinal);
    }

    private static bool IsIdentifierPart(char value) => char.IsLetterOrDigit(value) || value == '_';
}
