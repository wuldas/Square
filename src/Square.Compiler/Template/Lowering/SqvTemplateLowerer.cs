using Square.Compiler.Parser;
using Square.Compiler.LanguageServices;
using Square.Compiler.Syntax;
using Square.Compiler.Template.Ir;

namespace Square.Compiler.Template.Lowering;

internal static class SqvTemplateLowerer
{
    private static readonly HashSet<string> ConditionalAttributes = new(
        new[] { "v-if", "v-else-if", "v-else" },
        StringComparer.Ordinal);
    private static readonly HashSet<string> LoopAttributes = new(
        new[] { "v-for", ":key", "v-bind:key" },
        StringComparer.Ordinal);

    public static TemplateIrDocument Lower(SqvTemplateSyntax syntax) =>
        new(LowerNodes(syntax.Roots));

    private static IReadOnlyList<TemplateIrNode> LowerNodes(IReadOnlyList<SqvSyntaxNode> nodes)
    {
        var result = new List<TemplateIrNode>();
        for (var index = 0; index < nodes.Count; index++)
        {
            if (nodes[index] is SqvElementSyntax loopElement &&
                loopElement.Attributes.FirstOrDefault(attribute => attribute.Name == "v-for") is { } loopAttribute &&
                TryParseLoop(loopAttribute.Value, out var source, out var item, out var loopIndex))
            {
                var key = loopElement.Attributes.FirstOrDefault(attribute =>
                    attribute.Name is ":key" or "v-bind:key")?.Value;
                result.Add(new TemplateIrFor(
                    source,
                    item,
                    loopIndex,
                    key,
                    new TemplateIrNode[] { LowerElement(loopElement, LoopAttributes) },
                    loopAttribute.FullRange));
                continue;
            }
            if (nodes[index] is SqvElementSyntax element && FindConditional(element) is { } conditional &&
                conditional.Name == "v-if")
            {
                var branches = new List<TemplateIrIfBranch>();
                var chainStart = element.Origin;
                var cursor = index;
                while (cursor < nodes.Count && nodes[cursor] is SqvElementSyntax branchElement)
                {
                    var branchAttribute = FindConditional(branchElement);
                    if (branchAttribute == null ||
                        branches.Count > 0 && branchAttribute.Name is not ("v-else-if" or "v-else")) break;
                    branches.Add(new TemplateIrIfBranch(
                        branchAttribute.Name == "v-else" ? null : branchAttribute.Value ?? "false",
                        branchAttribute.Name == "v-else",
                        new TemplateIrNode[] { LowerElement(branchElement, ConditionalAttributes) },
                        branchAttribute.FullRange));
                    cursor++;
                    if (branchAttribute.Name == "v-else") break;
                }
                index = cursor - 1;
                result.Add(new TemplateIrIfChain(branches.ToArray(), chainStart));
                continue;
            }
            result.Add(LowerNode(nodes[index]));
        }
        return result.ToArray();
    }

    private static TemplateIrNode LowerNode(SqvSyntaxNode node)
    {
        if (node is SqvTextSyntax text) return new TemplateIrText(text.Text.Trim(), text.Origin);
        if (node is SqvInterpolationSyntax expression)
            return new TemplateIrExpression(expression.Expression, expression.Origin);
        var element = (SqvElementSyntax)node;
        var slot = element.Attributes.FirstOrDefault(attribute =>
            attribute.Name.StartsWith("#", StringComparison.Ordinal) ||
            attribute.Name.StartsWith("v-slot", StringComparison.Ordinal));
        if (slot != null)
        {
            var name = slot.Name.StartsWith("#", StringComparison.Ordinal)
                ? slot.Name.Substring(1)
                : slot.Name.Substring("v-slot".Length).TrimStart(':');
            if (name == "default") name = string.Empty;
            var nameIsExpression = name.StartsWith("[", StringComparison.Ordinal) &&
                                   name.EndsWith("]", StringComparison.Ordinal);
            if (nameIsExpression) name = name.Substring(1, name.Length - 2).Trim();
            return new TemplateIrSlot(
                name,
                nameIsExpression,
                slot.Value,
                element.TagName.Equals("template", StringComparison.OrdinalIgnoreCase)
                    ? LowerNodes(element.Children)
                    : new TemplateIrNode[]
                    {
                        LowerElement(element, new HashSet<string>(new[] { slot.Name }, StringComparer.Ordinal))
                    },
                element.Origin,
                LowerSlotScope(slot));
        }
        return LowerElement(element, null);
    }

    private static TemplateIrElement LowerElement(
        SqvElementSyntax element,
        HashSet<string> excludedAttributes)
    {
        var converted = new SqxElement { TagName = element.TagName };
        foreach (var attribute in element.Attributes)
        {
            if (excludedAttributes != null && excludedAttributes.Contains(attribute.Name)) continue;
            var pending = new List<SqxAttribute>();
            var value = SqvAttributeConverter.Convert(
                attribute.Name,
                attribute.Value,
                1,
                1,
                attribute.NameRange.Offset,
                pending);
            if (value != null) converted.Attributes.Add(value);
            converted.Attributes.AddRange(pending);
        }
        SqvAttributeConverter.ApplyVModel(converted);
        var origins = element.Attributes.ToDictionary(
            attribute => attribute.NameRange.Offset,
            attribute => attribute.FullRange);
        return new TemplateIrElement(
            TemplateCatalog.BuiltIn.GetComponent(element.TagName).TagName,
            converted.Attributes.Select(attribute => LowerAttribute(
                attribute,
                origins.TryGetValue(attribute.Position, out var origin) ? origin : element.Origin)).ToArray(),
            LowerNodes(element.Children),
            element.Origin);
    }

    private static TemplateIrAttribute LowerAttribute(SqxAttribute attribute, Square.Compiler.LanguageServices.SquareSourceRange origin)
    {
        var kind = attribute.IsDynamicProperty
            ? TemplateIrAttributeKind.DynamicProperty
            : attribute.IsDynamicEvent
                ? TemplateIrAttributeKind.DynamicEvent
                : attribute.Name == "__sqv_bind_object"
                    ? TemplateIrAttributeKind.ObjectProperties
                    : attribute.Name == "__sqv_on_object"
                        ? TemplateIrAttributeKind.ObjectEvents
                        : attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                            ? TemplateIrAttributeKind.Event
                            : TemplateIrAttributeKind.Property;
        var name = kind is TemplateIrAttributeKind.DynamicProperty or
            TemplateIrAttributeKind.DynamicEvent or
            TemplateIrAttributeKind.ObjectProperties or
            TemplateIrAttributeKind.ObjectEvents
            ? string.Empty
            : attribute.Name;
        return new TemplateIrAttribute(
            name,
            attribute.RawValue,
            attribute.IsExpression,
            origin,
            kind,
            attribute.ArgumentExpression,
            attribute.IsModelEvent);
    }

    private static SqvAttributeSyntax FindConditional(SqvElementSyntax element) =>
        element.Attributes.FirstOrDefault(attribute => ConditionalAttributes.Contains(attribute.Name));

    private static TemplateIrSlotScope LowerSlotScope(SqvAttributeSyntax slot)
    {
        if (string.IsNullOrWhiteSpace(slot.Value)) return null;
        var scope = SqvAttributeConverter.ParseSlotScope(slot.Value, slot.FullRange.Offset);
        return new TemplateIrSlotScope(
            scope.WholePropsName,
            scope.Properties.Select(binding => new TemplateIrSlotBinding(
                binding.PropertyName,
                binding.LocalName,
                slot.FullRange)).ToArray(),
            slot.FullRange);
    }

    private static bool TryParseLoop(
        string expression,
        out string source,
        out string item,
        out string index)
    {
        source = string.Empty;
        item = "item";
        index = null;
        if (string.IsNullOrWhiteSpace(expression)) return false;
        var marker = expression.IndexOf(" in ", StringComparison.Ordinal);
        if (marker < 0) marker = expression.IndexOf(" of ", StringComparison.Ordinal);
        if (marker < 0) return false;
        var binding = expression.Substring(0, marker).Trim().Trim('(', ')');
        source = expression.Substring(marker + 4).Trim();
        var names = binding.Split(',').Select(name => name.Trim()).Where(name => name.Length > 0).ToArray();
        if (names.Length == 0 || source.Length == 0) return false;
        item = names[0];
        if (names.Length > 1) index = names[1];
        return true;
    }
}
