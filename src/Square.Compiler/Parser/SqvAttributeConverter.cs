using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Square.Compiler.LanguageServices;

namespace Square.Compiler.Parser;

/// <summary>把原始 Vue 属性名/值转换为 emitter 可消费的 SqxAttribute 形式。</summary>
internal static class SqvAttributeConverter
{
    public static SqxAttribute Convert(string name, string value, int line, int column, int position, List<SqxAttribute> pending)
    {
        // v-for：解析为内部标记属性，后续降低为共享循环 IR。
        if (name == "v-for")
        {
            var parsed = ParseVFor(value);
            if (parsed != null)
            {
                pending.Add(new SqxAttribute { Name = "__vfor_item", RawValue = parsed.ItemName, Line = line, Position = position });
                if (parsed.IndexName != null)
                    pending.Add(new SqxAttribute { Name = "__vfor_index", RawValue = parsed.IndexName, Line = line, Position = position });
                return new SqxAttribute { Name = "__vfor_src", RawValue = parsed.Source, IsExpression = true, Line = line, Position = position };
            }
            throw new SqxParseException("Invalid v-for expression '" + (value ?? "") + "'", position, "SQV0003");
        }

        if (name == "v-if")
        {
            pending.Add(new SqxAttribute { Name = "__vif_cond", RawValue = value ?? "false", Line = line, Position = position });
            return new SqxAttribute { Name = "__vif_kind", RawValue = "if", Line = line, Position = position };
        }
        if (name == "v-else-if" || name.StartsWith("v-else-if:", StringComparison.Ordinal))
        {
            pending.Add(new SqxAttribute { Name = "__vif_cond", RawValue = value ?? "false", Line = line, Position = position });
            return new SqxAttribute { Name = "__vif_kind", RawValue = "elseif", Line = line, Position = position };
        }
        if (name == "v-else")
            return new SqxAttribute { Name = "__vif_kind", RawValue = "else", Line = line, Position = position };

        if (name == "v-text")
            return ExprAttr("text", value, line, position);
        if (name == "v-show")
            return ExprAttr("IsVisible", value, line, position);

        if (name == "v-html" || name == "v-pre" || name == "v-once" || name == "v-memo" || name == "v-cloak")
            throw new SqxParseException("Vue directive '" + name + "' is not supported", position, "SQV0002");
        if (name == "v-bind")
            return new SqxAttribute { Name = "__sqv_bind_object", RawValue = value, IsExpression = true, Line = line, Position = position };
        if (name == "v-on")
            return new SqxAttribute { Name = "__sqv_on_object", RawValue = value, IsExpression = true, Line = line, Position = position };

        if (name == "v-model" || name.StartsWith("v-model.", StringComparison.Ordinal))
            return new SqxAttribute { Name = name, RawValue = value, IsExpression = true, Line = line, Position = position };

        if (name.StartsWith(":", StringComparison.Ordinal))
        {
            var rest = name.Substring(1);
            var propName = StripModifiers(rest);
            if (propName.Length > 0 && propName[0] == '[')
                return DynamicPropertyAttr(propName, value, line, position);
            if (propName == "key")
                return new SqxAttribute { Name = "__vfor_key", RawValue = value, IsExpression = true, Line = line, Position = position };
            if (propName.Length == 0) return null;
            return ExprAttr(propName, value, line, position);
        }

        if (name.StartsWith("v-bind:", StringComparison.Ordinal))
        {
            var rest = name.Substring("v-bind:".Length);
            var propName = StripModifiers(rest);
            if (propName.Length > 0 && propName[0] == '[')
                return DynamicPropertyAttr(propName, value, line, position);
            if (propName == "key")
                return new SqxAttribute { Name = "__vfor_key", RawValue = value, IsExpression = true, Line = line, Position = position };
            if (propName.Length == 0) return null;
            return ExprAttr(propName, value, line, position);
        }

        if (name.StartsWith("@", StringComparison.Ordinal))
            return EventAttr(name.Substring(1), value, line, column, position);
        if (name.StartsWith("v-on:", StringComparison.Ordinal))
            return EventAttr(name.Substring("v-on:".Length), value, line, column, position);

        if (name.StartsWith("#", StringComparison.Ordinal))
        {
            var slotName = NormalizeSlotName(name.Substring(1));
            if (slotName.Length > 0 && slotName[0] == '[')
                return DynamicSlotAttr(slotName, value, line, position, pending);
            AddSlotScope(value, position, pending);
            return StaticAttr("slot", slotName, line, position);
        }
        if (name.StartsWith("v-slot", StringComparison.Ordinal))
        {
            var slotName = NormalizeSlotName(name.Substring("v-slot".Length));
            if (slotName.Length > 0 && slotName[0] == '[')
                return DynamicSlotAttr(slotName, value, line, position, pending);
            AddSlotScope(value, position, pending);
            return StaticAttr("slot", slotName, line, position);
        }

        if (name.StartsWith("v-", StringComparison.Ordinal))
            throw new SqxParseException("Vue directive '" + name + "' is not supported", position, "SQV0002");

        return StaticAttr(name, value, line, position);
    }

    /// <summary>对元素上的 v-model 属性批量产出绑定+事件回写属性（需 tagName 决定目标属性/事件）。</summary>
    public static void ApplyVModel(SqxElement element)
    {
        for (var i = 0; i < element.Attributes.Count; i++)
        {
            var attr = element.Attributes[i];
            var name = attr.Name;
            if (name != "v-model" && !name.StartsWith("v-model.", StringComparison.Ordinal)) continue;

            var value = attr.RawValue;
            if (string.IsNullOrWhiteSpace(value)) { element.Attributes.RemoveAt(i); i--; continue; }

            var modifiers = GetModifiers(name);
            var property = GetModelProperty(element);
            element.Attributes.RemoveAt(i);
            i--;
            if (property == null)
                throw new SqxParseException(
                    "v-model is not supported on component <" + element.TagName + ">",
                    attr.Position,
                    "SQV0002");
            foreach (var modifier in modifiers)
            {
                if (modifier is not ("trim" or "number" or "lazy"))
                    throw new SqxParseException(
                        "v-model modifier '." + modifier + "' is not supported",
                        attr.Position,
                        "SQV0002");
            }

            var eventName = GetModelEvent(element, modifiers.Contains("lazy"));
            var writeModifiers = new List<string>();
            if (modifiers.Contains("trim")) writeModifiers.Add("trim");
            if (modifiers.Contains("number")) writeModifiers.Add("number");

            element.Attributes.Add(ExprAttr(property.Value.AttributeName, value, attr.Line, attr.Position));
            var eventAttribute = ExprAttr(ToEventAttribute(eventName), value, attr.Line, attr.Position);
            eventAttribute.IsModelEvent = true;
            eventAttribute.ModelMemberName = GetModelMember(element);
            eventAttribute.ModelModifiers = writeModifiers;
            element.Attributes.Add(eventAttribute);
        }
    }

    private static SqxAttribute ExprAttr(string name, string value, int line, int position = 0) =>
        new() { Name = name, RawValue = value ?? "null", IsExpression = true, Line = line, Position = position };

    private static SqxAttribute StaticAttr(string name, string value, int line, int position) =>
        new() { Name = name, RawValue = value, Line = line, Position = position };

    private static SqxAttribute EventAttr(string eventNameWithModifiers, string value, int line, int column, int position)
    {
        var dot = eventNameWithModifiers.IndexOf('.');
        var eventName = dot >= 0 ? eventNameWithModifiers.Substring(0, dot) : eventNameWithModifiers;
        if (eventName.Length > 0 && eventName[0] == '[')
        {
            var argument = ExtractDynamicArgument(eventName, position);
            var modifiers = dot >= 0 ? eventNameWithModifiers.Substring(dot + 1) : "";
            ValidateEventModifiers(modifiers, position);
            return new SqxAttribute
            {
                Name = "__sqv_dynamic_event",
                ArgumentExpression = argument,
                RawValue = WrapEventHandler(value, modifiers),
                IsExpression = true,
                IsDynamicEvent = true,
                Line = line,
                Position = position
            };
        }
        if (eventName.Length == 0) return null;
        if (dot >= 0)
        {
            ValidateEventModifiers(eventNameWithModifiers.Substring(dot + 1), position);
        }
        var attrName = ToEventAttribute(eventName);
        if (string.IsNullOrWhiteSpace(value))
            return ExprAttr(attrName, value, line, position);
        var wrapper = WrapEventHandler(value, dot >= 0 ? eventNameWithModifiers.Substring(dot + 1) : "");
        return ExprAttr(attrName, wrapper, line, position);
    }

    private static SqxAttribute DynamicPropertyAttr(string name, string value, int line, int position) =>
        new()
        {
            Name = "__sqv_dynamic_property",
            ArgumentExpression = ExtractDynamicArgument(name, position),
            RawValue = value ?? "null",
            IsExpression = true,
            IsDynamicProperty = true,
            Line = line,
            Position = position
        };

    private static SqxAttribute DynamicSlotAttr(
        string name,
        string value,
        int line,
        int position,
        List<SqxAttribute> pending)
    {
        AddSlotScope(value, position, pending);
        return new SqxAttribute
        {
            Name = "slot",
            ArgumentExpression = ExtractDynamicArgument(name, position),
            RawValue = ExtractDynamicArgument(name, position),
            IsExpression = true,
            Line = line,
            Position = position
        };
    }

    private static void AddSlotScope(string value, int position, List<SqxAttribute> pending)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        pending.Add(new SqxAttribute { Name = "__sqv_slot_scope", RawValue = value.Trim(), Position = position });
    }

    private static string ExtractDynamicArgument(string value, int position)
    {
        if (value.Length < 3 || value[0] != '[' || value[value.Length - 1] != ']')
            throw new SqxParseException("Invalid dynamic argument '" + value + "'", position, "SQV0006");
        var expression = value.Substring(1, value.Length - 2).Trim();
        if (expression.Length == 0)
            throw new SqxParseException("Dynamic argument cannot be empty", position, "SQV0006");
        return expression;
    }

    private static void ValidateEventModifiers(string modifiers, int position)
    {
        if (string.IsNullOrWhiteSpace(modifiers)) return;
        foreach (var modifier in modifiers.Split('.'))
        {
            if (modifier is not ("stop" or "prevent"))
                throw new SqxParseException(
                    "Event modifier '." + modifier + "' is not supported",
                    position,
                    "SQV0002");
        }
    }

    private static string WrapEventHandler(string handler, string modifiers)
    {
        if (string.IsNullOrWhiteSpace(modifiers)) return handler;
        var stop = ContainsModifier(modifiers, "stop");
        var prevent = ContainsModifier(modifiers, "prevent");
        if (!stop && !prevent) return handler;
        var sb = new StringBuilder("e => { ");
        if (stop) sb.Append("e.StopPropagation(); ");
        if (prevent) sb.Append("e.PreventDefault(); ");
        sb.Append(handler).Append("(e); }");
        return sb.ToString();
    }

    private static bool ContainsModifier(string modifiers, string name)
    {
        foreach (var m in modifiers.Split('.'))
            if (string.Equals(m, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static VForParsed ParseVFor(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var inIndex = value.IndexOf(" in ", StringComparison.OrdinalIgnoreCase);
        if (inIndex < 0) inIndex = value.IndexOf(" of ", StringComparison.OrdinalIgnoreCase);
        if (inIndex < 0) return null;
        var left = value.Substring(0, inIndex).Trim();
        var source = value.Substring(inIndex + 4).Trim();
        var trimmed = left.TrimStart('(').TrimEnd(')').Trim();
        var parts = trimmed.Split(',');
        for (var i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
        if (parts.Length == 0 || !IsValidIdentifier(parts[0])) return null;
        if (parts.Length == 1) return new VForParsed { Source = source, ItemName = parts[0] };
        if (parts.Length == 2 && IsValidIdentifier(parts[1]))
            return new VForParsed { Source = source, ItemName = parts[0], IndexName = parts[1] };
        return null;
    }

    private static bool IsValidIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value) || !SyntaxFacts.IsValidIdentifier(value)) return false;
        return SyntaxFacts.GetKeywordKind(value) == SyntaxKind.None;
    }

    /// <param name="value">作用域绑定原文（标识符或对象解构模式）。</param>
    /// <param name="position">该原文在文档中的绝对起始偏移。</param>
    internal static TemplateSlotScope ParseSlotScope(string value, int position)
    {
        var raw = value ?? "";
        var leading = raw.Length - raw.TrimStart().Length;
        value = raw.Trim();
        if (IsValidIdentifier(value))
            return new TemplateSlotScope { WholePropsName = value, Position = position + leading };
        if (value.Length < 2 || value[0] != '{' || value[value.Length - 1] != '}')
            throw new SqxParseException("Scoped slot binding must be an identifier or an object destructuring pattern", position, "SQV0008");

        var innerStart = position + leading + 1;
        var scope = new TemplateSlotScope { Position = innerStart };
        var locals = new HashSet<string>(StringComparer.Ordinal);
        var inner = value.Substring(1, value.Length - 2);
        var segmentStart = 0;
        while (segmentStart <= inner.Length)
        {
            var comma = inner.IndexOf(',', segmentStart);
            var segmentEnd = comma < 0 ? inner.Length : comma;
            var rawPart = inner.Substring(segmentStart, segmentEnd - segmentStart);
            var part = rawPart.Trim();
            if (part.Length > 0)
            {
                var separator = part.IndexOf(':');
                var propertyName = separator < 0 ? part : part.Substring(0, separator).Trim();
                var localName = separator < 0 ? propertyName : part.Substring(separator + 1).Trim();
                if (!IsValidIdentifier(propertyName) || !IsValidIdentifier(localName))
                    throw new SqxParseException("Scoped slot destructuring names must be valid C# identifiers", position, "SQV0008");
                if (!locals.Add(localName))
                    throw new SqxParseException("Scoped slot local '" + localName + "' is declared more than once", position, "SQV0008");
                scope.Properties.Add(new TemplateSlotPropertyBinding
                {
                    PropertyName = propertyName,
                    LocalName = localName,
                    Position = innerStart + segmentStart + (rawPart.Length - rawPart.TrimStart().Length),
                    Length = part.Length
                });
            }
            if (comma < 0) break;
            segmentStart = comma + 1;
        }
        if (scope.Properties.Count == 0)
            throw new SqxParseException("Scoped slot destructuring pattern cannot be empty", position, "SQV0008");
        return scope;
    }

    private static string NormalizeSlotName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (value.StartsWith(":", StringComparison.Ordinal)) value = value.Substring(1);
        return value == "default" ? "" : value;
    }

    private static string StripModifiers(string value)
    {
        var dot = value.IndexOf('.');
        return dot >= 0 ? value.Substring(0, dot) : value;
    }

    private static string ToEventAttribute(string eventName)
    {
        eventName = StripModifiers(eventName);
        if (eventName.Length == 0) return "on";
        return "on" + char.ToUpperInvariant(eventName[0]) + eventName.Substring(1);
    }

    private static ModelProperty? GetModelProperty(SqxElement element)
    {
        var component = element.Resolution?.Component;
        if (component == null) return null;
        if (component.Kind is TemplateElementKind.Html or TemplateElementKind.HtmlCustom)
        {
            if (IsHtmlCheckableInput(element)) return new ModelProperty("checked");
            return component.LocalName is "input" or "textarea" or "select"
                ? new ModelProperty("value") : null;
        }
        if (component.NamespaceUri == TemplateCatalog.SquareNamespaceUri)
        {
            if (component.LocalName is "CheckBox" or "Radio") return new ModelProperty("checked");
            if (component.LocalName is "Input" or "TextArea" or "Select") return new ModelProperty("value");
            return null;
        }
        return new ModelProperty("Value");
    }

    private static string GetModelEvent(SqxElement element, bool lazy)
    {
        if (IsHtmlCheckableInput(element)) return "change";
        var component = element.Resolution?.Component;
        if (component?.LocalName is "input" or "textarea" &&
            component.Kind is TemplateElementKind.Html or TemplateElementKind.HtmlCustom ||
            component?.LocalName is "Input" or "TextArea" && component.NamespaceUri == TemplateCatalog.SquareNamespaceUri)
            return lazy ? "change" : "input";
        return "change";
    }

    private static string GetModelMember(SqxElement element)
    {
        if (IsHtmlCheckableInput(element)) return "checked";
        return element.Resolution?.Component?.NamespaceUri == TemplateCatalog.SquareNamespaceUri &&
            element.Resolution.Component.LocalName is "CheckBox" or "Radio" ? "IsChecked" : "Value";
    }

    private static bool IsHtmlCheckableInput(SqxElement element)
    {
        if (element.Resolution?.Component?.Kind is not (TemplateElementKind.Html or TemplateElementKind.HtmlCustom) ||
            element.Resolution.Component.LocalName != "input") return false;
        var type = element.Attributes.FirstOrDefault(attribute =>
            attribute.Name.Equals("type", StringComparison.OrdinalIgnoreCase))?.RawValue;
        return type is not null && (type.Equals("checkbox", StringComparison.OrdinalIgnoreCase) ||
                                    type.Equals("radio", StringComparison.OrdinalIgnoreCase));
    }


    private static HashSet<string> GetModifiers(string name)
    {
        var modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var firstDot = name.IndexOf('.');
        if (firstDot < 0) return modifiers;
        foreach (var modifier in name.Substring(firstDot + 1).Split('.'))
            if (!string.IsNullOrWhiteSpace(modifier)) modifiers.Add(modifier);
        return modifiers;
    }

    private readonly struct ModelProperty
    {
        public string AttributeName { get; }
        public ModelProperty(string attributeName) => AttributeName = attributeName;
    }

    private sealed class VForParsed
    {
        public string Source;
        public string ItemName;
        public string IndexName;
    }
}
