using Square.Compiler.LanguageServices;
using Square.Compiler.Syntax;
using Square.Compiler.Template.Ir;

namespace Square.Compiler.Template.Lowering;

internal static class SqxTemplateLowerer
{
    public static TemplateIrDocument Lower(SqxTemplateSyntax syntax) =>
        new(syntax.Roots.Select(LowerNode).ToArray());

    private static TemplateIrNode LowerNode(SqxSyntaxNode node)
    {
        if (node is SqxTextSyntax text) return new TemplateIrText(text.Text, text.Origin);
        if (node is SqxExpressionSyntax expression)
            return new TemplateIrExpression(expression.Expression, expression.Origin);
        var element = (SqxElementSyntax)node;
        var slot = element.Attributes.FirstOrDefault(attribute => attribute.Name == "slot");
        if (slot != null)
        {
            return new TemplateIrSlot(
                slot.Value ?? string.Empty,
                slot.IsExpression,
                null,
                new TemplateIrNode[] { LowerElement(element, "slot") },
                slot.FullRange);
        }
        if (element.TagName == "Show")
        {
            var when = element.Attributes.FirstOrDefault(attribute => attribute.Name == "when");
            var fallback = element.Attributes.FirstOrDefault(attribute => attribute.Name == "fallback");
            var branches = new List<TemplateIrIfBranch>
            {
                new(
                    when?.Value ?? "false",
                    false,
                    element.Children.Select(LowerNode).ToArray(),
                    when?.FullRange ?? element.Origin)
            };
            if (fallback?.FragmentNodes != null)
                branches.Add(new TemplateIrIfBranch(
                    null,
                    true,
                    fallback.FragmentNodes.Select(LowerNode).ToArray(),
                    fallback.FullRange));
            return new TemplateIrIfChain(
                branches.ToArray(),
                element.Origin);
        }
        if (element.TagName == "For")
        {
            var each = element.Attributes.FirstOrDefault(attribute => attribute.Name == "each");
            var fallback = element.Attributes.FirstOrDefault(attribute => attribute.Name == "fallback");
            var wrapper = element.Children.OfType<SqxExpressionSyntax>()
                .FirstOrDefault(expression => expression.Expression.TrimEnd().EndsWith("=>", StringComparison.Ordinal));
            var itemName = ParseLambdaItem(wrapper?.Expression) ?? "item";
            var children = element.Children
                .Where(child => child is not SqxExpressionSyntax expression ||
                                !expression.Expression.Trim().EndsWith("=>", StringComparison.Ordinal) &&
                                expression.Expression.Trim() != "}")
                .Select(LowerNode)
                .ToArray();
            return new TemplateIrFor(
                each?.Value ?? string.Empty,
                itemName,
                null,
                null,
                children,
                element.Origin,
                fallback?.FragmentNodes?.Select(LowerNode).ToArray());
        }
        return LowerElement(element, null);
    }

    private static TemplateIrElement LowerElement(SqxElementSyntax element, string excludedAttribute)
    {
        return new TemplateIrElement(
            TemplateCatalog.BuiltIn.GetComponent(element.TagName).TagName,
            element.Attributes
                .Where(attribute => attribute.Name != excludedAttribute)
                .Select(attribute => new TemplateIrAttribute(
                attribute.Name,
                attribute.Value,
                attribute.IsExpression,
                attribute.FullRange,
                attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                    ? TemplateIrAttributeKind.Event
                    : TemplateIrAttributeKind.Property)).ToArray(),
            element.Children.Select(LowerNode).ToArray(),
            element.Origin);
    }

    private static string ParseLambdaItem(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return null;
        var value = expression.Trim();
        var arrow = value.LastIndexOf("=>", StringComparison.Ordinal);
        if (arrow >= 0) value = value.Substring(0, arrow).Trim();
        if (value.StartsWith("(", StringComparison.Ordinal) && value.EndsWith(")", StringComparison.Ordinal))
            value = value.Substring(1, value.Length - 2).Trim();
        return value.Length == 0 ? null : value;
    }
}
