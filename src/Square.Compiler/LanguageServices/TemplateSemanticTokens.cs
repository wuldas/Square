using Microsoft.CodeAnalysis.CSharp.Syntax;
using Square.Compiler.Syntax;

namespace Square.Compiler.LanguageServices;

public static class TemplateSemanticTokens
{
    public static readonly string[] TokenTypes =
    {
        "class", "type", "property", "event", "keyword", "function", "variable", "parameter", "decorator"
    };

    public static readonly string[] TokenModifiers =
    {
        "declaration", "defaultLibrary"
    };

    private const int Class = 0;
    private const int Type = 1;
    private const int Property = 2;
    private const int Event = 3;
    private const int Keyword = 4;
    private const int Function = 5;
    private const int Variable = 6;
    private const int Parameter = 7;
    private const int Decorator = 8;

    public static IReadOnlyList<int> Encode(
        string text,
        string sourcePath,
        TemplateCatalog catalog = null,
        TemplateResolutionContext resolutionContext = null)
    {
        catalog ??= TemplateCatalog.BuiltIn;
        resolutionContext ??= TemplateResolutionContext.FromTemplateText(text, sourcePath ?? string.Empty);
        var dialect = sourcePath?.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase) == true
            ? ComponentDialect.Sqv : ComponentDialect.Sqx;
        var document = ComponentSectionScanner.Scan(text ?? string.Empty, sourcePath ?? string.Empty,
            dialect, tolerant: true, catalog: catalog, context: resolutionContext).Document;

        var tokens = new List<(int Offset, int Length, int Type, int Modifiers)>();
        if (document.Template?.SqvSyntax != null)
            CollectSqv(document.Template.SqvSyntax.Roots, tokens, catalog, resolutionContext);
        else if (document.Template?.SqxSyntax != null)
            CollectSqx(document.Template.SqxSyntax.Roots, tokens, catalog, resolutionContext);
        if (document.Script?.CSharp != null) CollectScript(document.Script.CSharp, tokens);
        tokens.Sort((left, right) => left.Offset.CompareTo(right.Offset));
        return Encode(text, tokens);
    }

    private static void CollectSqx(
        IEnumerable<SqxSyntaxNode> nodes,
        List<(int Offset, int Length, int Type, int Modifiers)> tokens,
        TemplateCatalog catalog,
        TemplateResolutionContext context)
    {
        foreach (var node in nodes)
        {
            if (node is not SqxElementSyntax element) continue;
            CollectElement(element.TagName, element.TagNameRange, element.CloseTagNameRange,
                element.Origin, element.Resolution, tokens);
            foreach (var attribute in element.Attributes)
            {
                var type = attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase) && attribute.Name.Length > 2
                    ? Event : Property;
                Add(tokens, attribute.NameRange.Offset, attribute.NameRange.Length, type, 0);
            }
            CollectSqx(element.Children, tokens, catalog, context);
        }
    }

    private static void CollectSqv(
        IEnumerable<SqvSyntaxNode> nodes,
        List<(int Offset, int Length, int Type, int Modifiers)> tokens,
        TemplateCatalog catalog,
        TemplateResolutionContext context)
    {
        foreach (var node in nodes)
        {
            if (node is not SqvElementSyntax element) continue;
            CollectElement(element.TagName, element.TagNameRange, element.CloseTagNameRange,
                element.Origin, element.Resolution, tokens);
            foreach (var attribute in element.Attributes)
                Add(tokens, attribute.NameRange.Offset, attribute.NameRange.Length,
                    attribute.DirectiveName == null ? Property : Keyword, 0);
            CollectSqv(element.Children, tokens, catalog, context);
        }
    }

    private static void CollectElement(
        string tagName, SquareSourceRange tagNameRange, SquareSourceRange closeTagNameRange,
        SquareSourceRange origin, TemplateElementResolution resolution,
        List<(int Offset, int Length, int Type, int Modifiers)> tokens)
    {
        var component = resolution?.Status == TemplateElementResolutionStatus.Resolved ? resolution.Component : null;
        var isBuiltIn = component?.IsBuiltIn == true;
        var controlFlow = component?.NamespaceUri == TemplateCatalog.SquareNamespaceUri &&
            component.LocalName is "Show" or "For" or "Index" or "Switch" or "Match" or "Slot" or "Outlet" or "Fragment";
        var type = controlFlow ? Type : isBuiltIn ? Class : Type;
        var modifiers = isBuiltIn || controlFlow ? 2 : 0;
        var offset = tagNameRange.Length > 0 ? tagNameRange.Offset : origin.Offset + 1;
        Add(tokens, offset, tagName.Length, type, modifiers);
        if (closeTagNameRange.Length > 0)
            Add(tokens, closeTagNameRange.Offset, closeTagNameRange.Length, type, modifiers);
    }

    private static void CollectScript(
        CSharpScriptSyntax script,
        List<(int Offset, int Length, int Type, int Modifiers)> tokens)
    {
        foreach (var field in script.Members.OfType<FieldDeclarationSyntax>())
            foreach (var variable in field.Declaration.Variables)
                AddMapped(variable.Identifier.Span, Variable, 1);
        foreach (var property in script.Members.OfType<PropertyDeclarationSyntax>())
            AddMapped(property.Identifier.Span, Property, 1);
        foreach (var method in script.Members.OfType<MethodDeclarationSyntax>())
        {
            AddMapped(method.Identifier.Span, Function, 1);
            foreach (var parameter in method.ParameterList.Parameters)
                AddMapped(parameter.Identifier.Span, Parameter, 1);
            foreach (var variable in method.DescendantNodes().OfType<VariableDeclaratorSyntax>())
                AddMapped(variable.Identifier.Span, Variable, 1);
            foreach (var statement in method.DescendantNodes().OfType<ForEachStatementSyntax>())
                AddMapped(statement.Identifier.Span, Variable, 1);
        }
        foreach (var attribute in script.Root.DescendantNodes().OfType<AttributeSyntax>())
        {
            AddMapped(attribute.Name.Span, Decorator, 0);
            foreach (var argument in attribute.ArgumentList?.Arguments ?? default)
                if (argument.NameEquals != null) AddMapped(argument.NameEquals.Name.Span, Property, 0);
        }

        void AddMapped(Microsoft.CodeAnalysis.Text.TextSpan span, int type, int modifiers)
        {
            var range = script.SourceMap.ToDocumentRange(span);
            Add(tokens, range.Offset, range.Length, type, modifiers);
        }
    }

    private static void Add(
        List<(int Offset, int Length, int Type, int Modifiers)> tokens,
        int offset,
        int length,
        int type,
        int modifiers)
    {
        if (offset < 0 || length <= 0) return;
        tokens.Add((offset, length, type, modifiers));
    }


    private static IReadOnlyList<int> Encode(
        string text,
        List<(int Offset, int Length, int Type, int Modifiers)> tokens)
    {
        var data = new List<int>(tokens.Count * 5);
        var previousLine = 0;
        var previousCharacter = 0;
        foreach (var token in tokens)
        {
            var position = ToPosition(text, token.Offset);
            var deltaLine = position.Line - previousLine;
            var deltaStart = deltaLine == 0
                ? position.Character - previousCharacter
                : position.Character;
            data.Add(deltaLine);
            data.Add(deltaStart);
            data.Add(token.Length);
            data.Add(token.Type);
            data.Add(token.Modifiers);
            previousLine = position.Line;
            previousCharacter = position.Character;
        }
        return data;
    }

    private static (int Line, int Character) ToPosition(string text, int offset)
    {
        var line = 0;
        var lineStart = 0;
        offset = Math.Min(Math.Max(offset, 0), text.Length);
        for (var index = 0; index < offset; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                lineStart = index + 1;
            }
        }
        return (line, offset - lineStart);
    }
}
