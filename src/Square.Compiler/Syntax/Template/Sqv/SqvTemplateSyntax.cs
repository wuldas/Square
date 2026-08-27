using Square.Compiler.LanguageServices;

namespace Square.Compiler.Syntax;

internal sealed class SqvTemplateSyntax
{
    public SqvTemplateSyntax(IReadOnlyList<SqvSyntaxNode> roots)
    {
        Roots = roots ?? throw new ArgumentNullException(nameof(roots));
    }

    public IReadOnlyList<SqvSyntaxNode> Roots { get; }
}

internal abstract class SqvSyntaxNode
{
    protected SqvSyntaxNode(SquareSourceRange origin)
    {
        Origin = origin;
    }

    public SquareSourceRange Origin { get; }
}

internal sealed class SqvElementSyntax : SqvSyntaxNode
{
    public SqvElementSyntax(
        string tagName,
        IReadOnlyList<SqvAttributeSyntax> attributes,
        IReadOnlyList<SqvSyntaxNode> children,
        bool isSelfClosing,
        SquareSourceRange origin)
        : base(origin)
    {
        TagName = tagName ?? string.Empty;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        Children = children ?? throw new ArgumentNullException(nameof(children));
        IsSelfClosing = isSelfClosing;
    }

    public string TagName { get; }
    public IReadOnlyList<SqvAttributeSyntax> Attributes { get; }
    public IReadOnlyList<SqvSyntaxNode> Children { get; }
    public bool IsSelfClosing { get; }
}

internal sealed class SqvTextSyntax : SqvSyntaxNode
{
    public SqvTextSyntax(string text, SquareSourceRange origin) : base(origin)
    {
        Text = text ?? string.Empty;
    }

    public string Text { get; }
}

internal sealed class SqvInterpolationSyntax : SqvSyntaxNode
{
    public SqvInterpolationSyntax(string expression, SquareSourceRange origin) : base(origin)
    {
        Expression = expression ?? string.Empty;
    }

    public string Expression { get; }
}

internal sealed class SqvAttributeSyntax
{
    public SqvAttributeSyntax(
        string name,
        string value,
        SquareSourceRange fullRange,
        SquareSourceRange nameRange,
        SquareSourceRange valueRange)
    {
        Name = name ?? string.Empty;
        Value = value;
        FullRange = fullRange;
        NameRange = nameRange;
        ValueRange = valueRange;
        var directive = ParseDirective(Name, nameRange.Offset);
        DirectiveName = directive.Name;
        Argument = directive.Argument;
        ArgumentIsDynamic = directive.ArgumentIsDynamic;
        ArgumentRange = directive.ArgumentRange;
        ModifierSyntaxes = directive.Modifiers;
        Modifiers = ModifierSyntaxes.Select(modifier => modifier.Name).ToArray();
    }

    public string Name { get; }
    public string Value { get; }
    public string DirectiveName { get; }
    public string Argument { get; }
    public bool ArgumentIsDynamic { get; }
    public IReadOnlyList<string> Modifiers { get; }
    public IReadOnlyList<SqvModifierSyntax> ModifierSyntaxes { get; }
    public SquareSourceRange FullRange { get; }
    public SquareSourceRange NameRange { get; }
    public SquareSourceRange ArgumentRange { get; }
    public SquareSourceRange ValueRange { get; }

    private static DirectiveParts ParseDirective(string name, int offset)
    {
        string directiveName;
        var argumentStart = -1;
        if (name.StartsWith(":", StringComparison.Ordinal))
        {
            directiveName = "bind";
            argumentStart = 1;
        }
        else if (name.StartsWith("@", StringComparison.Ordinal))
        {
            directiveName = "on";
            argumentStart = 1;
        }
        else if (name.StartsWith("#", StringComparison.Ordinal))
        {
            directiveName = "slot";
            argumentStart = 1;
        }
        else if (name.StartsWith("v-bind:", StringComparison.Ordinal))
        {
            directiveName = "bind";
            argumentStart = "v-bind:".Length;
        }
        else if (name.StartsWith("v-on:", StringComparison.Ordinal))
        {
            directiveName = "on";
            argumentStart = "v-on:".Length;
        }
        else if (name.StartsWith("v-slot", StringComparison.Ordinal))
        {
            directiveName = "slot";
            argumentStart = "v-slot".Length;
            if (argumentStart < name.Length && name[argumentStart] == ':') argumentStart++;
        }
        else if (name.StartsWith("v-", StringComparison.Ordinal))
        {
            var directiveEnd = name.IndexOfAny(new[] { ':', '.' }, 2);
            if (directiveEnd < 0) directiveEnd = name.Length;
            directiveName = name.Substring(2, directiveEnd - 2);
            argumentStart = directiveEnd < name.Length && name[directiveEnd] == ':' ? directiveEnd + 1 : -1;
        }
        else
        {
            return new DirectiveParts(null, null, false,
                new SquareSourceRange(offset + name.Length, 0), Array.Empty<SqvModifierSyntax>());
        }

        var modifiers = new List<SqvModifierSyntax>();
        var argument = (string)null;
        var argumentIsDynamic = false;
        var argumentRange = new SquareSourceRange(offset + name.Length, 0);
        var modifierStart = argumentStart;
        if (argumentStart >= 0 && argumentStart < name.Length)
        {
            if (name[argumentStart] == '[')
            {
                var close = name.IndexOf(']', argumentStart + 1);
                if (close >= 0)
                {
                    argumentIsDynamic = true;
                    argument = name.Substring(argumentStart + 1, close - argumentStart - 1);
                    argumentRange = new SquareSourceRange(offset + argumentStart + 1, argument.Length);
                    modifierStart = close + 1;
                }
            }
            else
            {
                var dot = name.IndexOf('.', argumentStart);
                var end = dot < 0 ? name.Length : dot;
                argument = name.Substring(argumentStart, end - argumentStart);
                argumentRange = new SquareSourceRange(offset + argumentStart, argument.Length);
                modifierStart = end;
            }
        }
        else
        {
            var firstDot = name.IndexOf('.');
            modifierStart = firstDot < 0 ? name.Length : firstDot;
        }

        while (modifierStart < name.Length)
        {
            if (name[modifierStart] == '.') modifierStart++;
            var end = name.IndexOf('.', modifierStart);
            if (end < 0) end = name.Length;
            if (end > modifierStart)
            {
                var modifier = name.Substring(modifierStart, end - modifierStart);
                modifiers.Add(new SqvModifierSyntax(
                    modifier,
                    new SquareSourceRange(offset + modifierStart, modifier.Length)));
            }
            modifierStart = end;
        }
        return new DirectiveParts(directiveName, argument, argumentIsDynamic, argumentRange, modifiers.ToArray());
    }

    private sealed class DirectiveParts
    {
        public DirectiveParts(
            string name,
            string argument,
            bool argumentIsDynamic,
            SquareSourceRange argumentRange,
            IReadOnlyList<SqvModifierSyntax> modifiers)
        {
            Name = name;
            Argument = argument;
            ArgumentIsDynamic = argumentIsDynamic;
            ArgumentRange = argumentRange;
            Modifiers = modifiers;
        }

        public string Name { get; }
        public string Argument { get; }
        public bool ArgumentIsDynamic { get; }
        public SquareSourceRange ArgumentRange { get; }
        public IReadOnlyList<SqvModifierSyntax> Modifiers { get; }
    }
}

internal sealed class SqvModifierSyntax
{
    public SqvModifierSyntax(string name, SquareSourceRange range)
    {
        Name = name ?? string.Empty;
        Range = range;
    }

    public string Name { get; }
    public SquareSourceRange Range { get; }
}
