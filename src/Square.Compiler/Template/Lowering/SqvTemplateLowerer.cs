using Square.Compiler.Parser;
using Square.Compiler.LanguageServices;
using Square.Compiler.Syntax;
using Square.Compiler.Template.Ir;
using Square.UI.Html;

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
        new(LowerNodes(syntax.Roots, htmlContext: false, preserveAll: false));

    private static IReadOnlyList<TemplateIrNode> LowerNodes(
        IReadOnlyList<SqvSyntaxNode> nodes,
        bool htmlContext,
        bool preserveAll)
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
                    new TemplateIrNode[] { LowerElement(loopElement, LoopAttributes, htmlContext, preserveAll) },
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
                        new TemplateIrNode[] { LowerElement(branchElement, ConditionalAttributes, htmlContext, preserveAll) },
                        branchAttribute.FullRange));
                    cursor++;
                    if (branchAttribute.Name == "v-else") break;
                }
                index = cursor - 1;
                result.Add(new TemplateIrIfChain(branches.ToArray(), chainStart));
                continue;
            }
            var lowered = LowerNode(nodes[index], htmlContext, preserveAll);
            if (lowered != null) result.Add(lowered);
        }
        return result.ToArray();
    }

    private static TemplateIrNode LowerNode(SqvSyntaxNode node, bool htmlContext, bool preserveAll)
    {
        if (node is SqvTextSyntax text)
        {
            if (htmlContext)
            {
                // HTML 上下文保留原文；pre/textarea 之外的纯缩进空白丢弃。
                if (!preserveAll && string.IsNullOrWhiteSpace(text.Text)) return null;
                return new TemplateIrText(text.Text, text.Origin);
            }
            return string.IsNullOrWhiteSpace(text.Text) ? null : new TemplateIrText(text.Text.Trim(), text.Origin);
        }
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
                    ? LowerNodes(element.Children, htmlContext, preserveAll)
                    : new TemplateIrNode[]
                    {
                        LowerElement(
                            element,
                            new HashSet<string>(new[] { slot.Name }, StringComparer.Ordinal),
                            htmlContext,
                            preserveAll)
                    },
                element.Origin,
                LowerSlotScope(slot));
        }
        return LowerElement(element, null, htmlContext, preserveAll);
    }

    private static TemplateIrElement LowerElement(
        SqvElementSyntax element,
        HashSet<string> excludedAttributes,
        bool htmlContext,
        bool preserveAll)
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
            if (value != null)
            {
                if (attribute.ValueRange.Length > 0)
                {
                    value.ValuePosition = attribute.ValueRange.Offset;
                    value.ValueLength = attribute.ValueRange.Length;
                }
                converted.Attributes.Add(value);
            }
            converted.Attributes.AddRange(pending);
        }
        SqvAttributeConverter.ApplyVModel(converted);
        var origins = element.Attributes.ToDictionary(
            attribute => attribute.NameRange.Offset,
            attribute => attribute.FullRange);
        return new TemplateIrElement(
            ResolveTagName(element.TagName),
            converted.Attributes.Select(attribute => LowerAttribute(
                attribute,
                origins.TryGetValue(attribute.Position, out var origin) ? origin : element.Origin)).ToArray(),
            LowerNodes(
                element.Children,
                htmlContext || IsHtmlElement(element.TagName),
                preserveAll || IsSpacePreservingHtmlElement(element.TagName)),
            element.Origin,
            element.TagNameRange,
            element.CloseTagNameRange,
            element.TagName);
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
            attribute.IsModelEvent,
            valueRange: attribute.ValueLength > 0 && attribute.ValuePosition >= 0
                ? new SquareSourceRange(attribute.ValuePosition, attribute.ValueLength)
                : default,
            modelMemberName: attribute.ModelMemberName,
            modelModifiers: attribute.ModelModifiers);
    }

    /// <summary>
    /// HTML 标签身份解析：html: 前缀（大小写不敏感）与精确小写 HTML 目录名保留 HTML 规范名，
    /// 不经大小写不敏感的 Square 内建归一化；其余标签维持既有解析。
    /// </summary>
    private static string ResolveTagName(string tagName)
    {
        if (!string.IsNullOrEmpty(tagName))
        {
            if (tagName.StartsWith("html:", StringComparison.OrdinalIgnoreCase))
            {
                var local = tagName.Substring("html:".Length).ToLowerInvariant();
                if (HtmlTagCatalog.IsTag(local)) return local;
            }
            else if (HtmlTagCatalog.IsTag(tagName))
            {
                return tagName;
            }
        }
        return TemplateCatalog.BuiltIn.TryGetBuiltInComponent(tagName, out var descriptor)
            ? descriptor.TagName
            : tagName;
    }

    /// <summary>精确小写命中 HTML 目录或带 html: 前缀即 HTML 元素；不带前缀的 template 是既有片段包装，不算 HTML。</summary>
    private static bool IsHtmlElement(string tagName)
    {
        if (string.IsNullOrEmpty(tagName)) return false;
        if (tagName.StartsWith("html:", StringComparison.OrdinalIgnoreCase))
            return HtmlTagCatalog.IsTag(tagName.Substring("html:".Length).ToLowerInvariant());
        if (tagName.Equals("template", StringComparison.Ordinal)) return false;
        return HtmlTagCatalog.IsTag(tagName);
    }

    /// <summary>HTML 上下文中 pre/textarea 保留换行与缩进。</summary>
    private static bool IsSpacePreservingHtmlElement(string tagName)
    {
        if (tagName.StartsWith("html:", StringComparison.OrdinalIgnoreCase))
            tagName = tagName.Substring("html:".Length).ToLowerInvariant();
        return tagName is "pre" or "textarea";
    }

    private static SqvAttributeSyntax FindConditional(SqvElementSyntax element) =>
        element.Attributes.FirstOrDefault(attribute => ConditionalAttributes.Contains(attribute.Name));

    private static TemplateIrSlotScope LowerSlotScope(SqvAttributeSyntax slot)
    {
        if (string.IsNullOrWhiteSpace(slot.Value)) return null;
        var scope = SqvAttributeConverter.ParseSlotScope(slot.Value, slot.ValueRange.Offset);
        return new TemplateIrSlotScope(
            scope.WholePropsName,
            scope.Properties.Select(binding => new TemplateIrSlotBinding(
                binding.PropertyName,
                binding.LocalName,
                binding.Length > 0
                    ? new SquareSourceRange(binding.Position, binding.Length)
                    : slot.FullRange)).ToArray(),
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
