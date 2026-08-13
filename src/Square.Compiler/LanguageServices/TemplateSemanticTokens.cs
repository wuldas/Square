using Square.Compiler.Parser;

namespace Square.Compiler.LanguageServices;

public static class TemplateSemanticTokens
{
    public static readonly string[] TokenTypes =
    {
        "class", "type", "property", "event", "keyword", "function"
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

    public static IReadOnlyList<int> Encode(string text, string sourcePath)
    {
        var result = SquareDocumentService.ParseSyntaxTree(text, sourcePath ?? string.Empty);
        var document = result.ParsedSqxDocument;
        if (document?.Template == null) return Array.Empty<int>();

        var tokens = new List<(int Offset, int Length, int Type, int Modifiers)>();
        Collect(document.Template.Roots, tokens);
        tokens.Sort((left, right) => left.Offset.CompareTo(right.Offset));
        return Encode(text, tokens);
    }

    private static void Collect(
        IEnumerable<SqxNode> nodes,
        List<(int Offset, int Length, int Type, int Modifiers)> tokens)
    {
        foreach (var node in nodes)
        {
            if (node is SqxElement element)
            {
                var descriptor = TemplateCatalog.BuiltIn.GetComponent(element.TagName);
                var type = descriptor.IsBuiltIn ? Class : Type;
                var modifiers = descriptor.IsBuiltIn || IsControlFlow(element.TagName) ? 2 : 0;
                if (IsControlFlow(element.TagName)) type = Type;
                Add(tokens, element.Position + 1, element.TagName.Length, type, modifiers);

                foreach (var attribute in element.Attributes)
                    CollectAttribute(attribute, tokens);

                Collect(element.Children, tokens);
            }
            else if (node is TemplateForDirective forDirective)
            {
                Collect(forDirective.Children, tokens);
            }
            else if (node is TemplateIfChainDirective ifChain)
            {
                foreach (var branch in ifChain.Branches)
                    Collect(branch.Children, tokens);
            }
        }
    }

    private static void CollectAttribute(
        SqxAttribute attribute,
        List<(int Offset, int Length, int Type, int Modifiers)> tokens)
    {
        if (attribute.Position <= 0 || string.IsNullOrEmpty(attribute.Name)) return;
        if (attribute.Name.StartsWith("__", StringComparison.Ordinal)) return;

        var type = Property;
        if (attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase) && attribute.Name.Length > 2)
            type = Event;
        else if (attribute.Name.StartsWith("v-", StringComparison.OrdinalIgnoreCase) ||
                 attribute.Name.StartsWith(":", StringComparison.Ordinal) ||
                 attribute.Name.StartsWith("@", StringComparison.Ordinal) ||
                 attribute.Name.StartsWith("#", StringComparison.Ordinal))
            type = Keyword;

        Add(tokens, attribute.Position, attribute.Name.Length, type, 0);
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

    private static bool IsControlFlow(string tagName) =>
        tagName.Equals("Show", StringComparison.OrdinalIgnoreCase) ||
        tagName.Equals("For", StringComparison.OrdinalIgnoreCase) ||
        tagName.Equals("Index", StringComparison.OrdinalIgnoreCase) ||
        tagName.Equals("Switch", StringComparison.OrdinalIgnoreCase) ||
        tagName.Equals("Match", StringComparison.OrdinalIgnoreCase) ||
        tagName.Equals("Slot", StringComparison.OrdinalIgnoreCase) ||
        tagName.Equals("Outlet", StringComparison.OrdinalIgnoreCase);

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
        offset = Math.Clamp(offset, 0, text.Length);
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
