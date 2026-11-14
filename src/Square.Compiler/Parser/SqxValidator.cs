using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Square.Compiler.Parser;

internal static class SqxValidator
{
    public static void Validate(IEnumerable<SqxNode> nodes)
    {
        foreach (var node in nodes) ValidateNode(node);
    }

    private static void ValidateNode(SqxNode node)
    {
        switch (node)
        {
            case SqxExpression expression:
                if (!IsWrapperExpression(expression.Expression))
                    ValidateExpression(expression.Expression, expression.Position);
                break;
            case SqxElement element:
                ValidateElement(element);
                Validate(element.Children);
                foreach (var fragment in element.Attributes.Where(attribute => attribute.FragmentNodes != null))
                    Validate(fragment.FragmentNodes);
                break;
        }
    }

    private static void ValidateElement(SqxElement element)
    {
        var attributes = new HashSet<(string Name, bool IsStaticClass)>();
        foreach (var attribute in element.Attributes)
        {
            // Match SQV: allow one static class plus one expression without relaxing other duplicates.
            var key = (attribute.Name.ToLowerInvariant(), attribute.Name.Equals("class", StringComparison.OrdinalIgnoreCase) && !attribute.IsExpression);
            if (!attributes.Add(key))
                throw new SqxParseException(
                    "Duplicate attribute '" + attribute.Name + "' on <" + element.TagName + ">",
                    attribute.Position);
            if (attribute.IsExpression && attribute.FragmentNodes == null)
                ValidateExpression(attribute.RawValue, attribute.Position);
        }

        if (string.Equals(element.TagName, "Switch", StringComparison.OrdinalIgnoreCase) &&
            element.Attributes.Any(attribute => string.Equals(attribute.Name, "fallback", StringComparison.OrdinalIgnoreCase)) &&
            element.Children.OfType<SqxElement>().Any(match =>
                string.Equals(match.TagName, "Match", StringComparison.OrdinalIgnoreCase) &&
                !match.Attributes.Any(attribute => string.Equals(attribute.Name, "when", StringComparison.OrdinalIgnoreCase))))
        {
            throw new SqxParseException("<Switch> cannot contain both fallback and a default <Match>", element.Position);
        }
    }

    private static void ValidateExpression(string expression, int position)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new SqxParseException("Template expression is not valid C#", position);
        var syntax = SyntaxFactory.ParseExpression(expression);
        if (syntax.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            throw new SqxParseException("Template expression '" + expression + "' is not valid C#", position);
    }

    private static bool IsWrapperExpression(string expression)
    {
        var value = expression?.Trim() ?? "";
        return value.EndsWith("=>", StringComparison.Ordinal) || value == "}";
    }
}
