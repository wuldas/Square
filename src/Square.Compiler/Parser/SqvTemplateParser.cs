using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Square.Compiler.Parser;

/// <summary>
/// Vue 模板解析器：消费 <see cref="SqvLexer"/> 产出的 token，直接构造 <see cref="SqxNode"/> 树，
/// 并把 <c>v-for</c> / <c>v-if</c> 链降低为共享模板 IR。
/// 不依赖 <c>SqxCoreParser</c> / <c>SqxParser</c>。
/// </summary>
internal sealed class SqvTemplateParser
{
    private readonly List<SqvToken> _tokens;
    private readonly int _baseOffset;
    private int _index;

    private SqvTemplateParser(List<SqvToken> tokens, int baseOffset)
    {
        _tokens = tokens;
        _baseOffset = baseOffset;
    }

    public static List<SqxNode> Parse(string templateSource, int baseOffset = 0)
    {
        var tokens = new SqvLexer(templateSource, baseOffset).Tokenize();
        return new SqvTemplateParser(tokens, baseOffset).ParseRoots();
    }

    private List<SqxNode> ParseRoots()
    {
        var raw = new List<SqxNode>();
        while (Peek().Type != SqvTokenType.Eof)
        {
            var node = ParseNode();
            if (node != null) raw.Add(node);
        }
        return RewriteSiblings(raw);
    }

    private SqxNode ParseNode()
    {
        var token = Peek();
        switch (token.Type)
        {
            case SqvTokenType.OpenTag:
                return ParseElement();
            case SqvTokenType.Text:
                _index++;
                return new SqxText { Text = token.Text.Trim(), Kind = SqxNodeKind.Text, Line = token.Line, Column = token.Column, Position = Absolute(token.Offset) };
            case SqvTokenType.Interpolation:
                _index++;
                return new SqxExpression { Expression = token.Text, Kind = SqxNodeKind.Expression, Line = token.Line, Column = token.Column, Position = Absolute(token.Offset) };
            case SqvTokenType.EndTag:
                throw Error("Unexpected closing tag </" + token.Text + ">", token.Offset);
            default:
                _index++;
                return null;
        }
    }

    private SqxElement ParseElement()
    {
        var open = Expect(SqvTokenType.OpenTag);
        var nameToken = Expect(SqvTokenType.Identifier);
        var tagName = nameToken.Text;
        var element = new SqxElement
        {
            TagName = tagName,
            Kind = SqxNodeKind.Element,
            Line = open.Line,
            Column = open.Column + 1,
            Position = Absolute(open.Offset)
        };

        while (Peek().Type is not (SqvTokenType.CloseTag or SqvTokenType.CloseSelfTag or SqvTokenType.Eof))
        {
            var attr = ParseAttribute();
            if (attr != null) element.Attributes.Add(attr);
            foreach (var pending in _pendingAttrs) element.Attributes.Add(pending);
            _pendingAttrs.Clear();
        }

        SqvAttributeConverter.ApplyVModel(element);
        var slotScopeAttribute = element.Attributes.FirstOrDefault(attribute => attribute.Name == "__sqv_slot_scope");
        if (slotScopeAttribute != null)
        {
            element.SlotScope = SqvAttributeConverter.ParseSlotScope(slotScopeAttribute.RawValue, slotScopeAttribute.Position);
            element.Attributes.Remove(slotScopeAttribute);
        }

        if (Peek().Type == SqvTokenType.CloseSelfTag)
        {
            _index++;
            return element;
        }

        Expect(SqvTokenType.CloseTag);
        while (true)
        {
            var t = Peek();
            if (t.Type == SqvTokenType.Eof) break;
            if (t.Type == SqvTokenType.EndTag)
            {
                if (!string.Equals(t.Text, tagName, StringComparison.OrdinalIgnoreCase))
                    throw Error("Closing tag </" + t.Text + "> does not match <" + tagName + ">", t.Offset);
                _index++;
                return element;
            }
            var child = ParseNode();
            if (child != null) element.Children.Add(child);
        }
        throw Error("Unclosed element <" + tagName + ">", open.Offset);
    }

    private SqxAttribute ParseAttribute()
    {
        var nameToken = Peek();
        if (nameToken.Type != SqvTokenType.Identifier)
        {
            _index++;
            return null;
        }
        _index++;

        if (Peek().Type != SqvTokenType.Equals)
        {
            // 无值属性（如 v-else、disabled）也要经过 Vue 属性转换。
            var noValue = SqvAttributeConverter.Convert(nameToken.Text, null, nameToken.Line, nameToken.Column, Absolute(nameToken.Offset), _pendingAttrs);
            return noValue ?? new SqxAttribute { Name = nameToken.Text, Line = nameToken.Line, Position = Absolute(nameToken.Offset) };
        }

        _index++;
        var valueToken = Peek();
        string rawValue = null;
        if (valueToken.Type == SqvTokenType.StringLiteral)
        {
            _index++;
            rawValue = valueToken.Text;
        }
        else if (valueToken.Type == SqvTokenType.Identifier)
        {
            _index++;
            rawValue = valueToken.Text;
        }

        // v-if / v-else-if 需要同时产出 kind 与 cond 两个标记属性，通过 _pendingAttrs 追加。
        var primary = SqvAttributeConverter.Convert(nameToken.Text, rawValue, nameToken.Line, nameToken.Column, Absolute(nameToken.Offset), _pendingAttrs);
        return primary;
    }

    private readonly List<SqxAttribute> _pendingAttrs = new();

    /// <summary>把兄弟节点中的 v-for / v-if 链重组为 Vue 专属指令节点（与旧 SqvParser.RewriteVueDirectives 等价）。</summary>
    private static List<SqxNode> RewriteSiblings(List<SqxNode> nodes)
    {
        var rewritten = new List<SqxNode>(nodes.Count);
        TemplateIfChainDirective currentChain = null;

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node is not SqxElement element)
            {
                currentChain = null;
                rewritten.Add(node);
                continue;
            }

            element.Children = RewriteSiblings(element.Children);

            var vfor = FindAttr(element, "__vfor_src");
            if (vfor != null)
            {
                if (element.Attributes.Count(attribute => attribute.Name == "__vfor_key") > 1)
                {
                    var duplicateKey = element.Attributes.Last(attribute => attribute.Name == "__vfor_key");
                    throw new SqxParseException(
                        "Duplicate key binding on <" + element.TagName + ">",
                        duplicateKey.Position,
                        "SQV0005");
                }
                var key = FindAttr(element, "__vfor_key");
                var directive = new TemplateForDirective
                {
                    SourceExpression = vfor.RawValue ?? "",
                    ItemName = FindAttr(element, "__vfor_item")?.RawValue ?? "item",
                    IndexName = FindAttr(element, "__vfor_index")?.RawValue,
                    KeyExpression = key?.RawValue,
                    KeyPosition = key?.Position ?? 0,
                    Position = vfor.Position,
                    Children = new List<SqxNode> { element }
                };
                StripVueMarkerAttributes(element);
                currentChain = null;
                rewritten.Add(directive);
                continue;
            }

            var orphanedKey = FindAttr(element, "__vfor_key");
            if (orphanedKey != null)
                throw new SqxParseException(
                    "Vue key bindings are only supported on elements with v-for",
                    orphanedKey.Position,
                    "SQV0002");

            var vifKind = FindAttr(element, "__vif_kind")?.RawValue;
            if (vifKind != null)
            {
                var cond = FindAttr(element, "__vif_cond")?.RawValue;
                var position = FindAttr(element, "__vif_kind")?.Position ?? 0;
                StripVueMarkerAttributes(element);

                if (vifKind == "if")
                {
                    currentChain = new TemplateIfChainDirective { Position = position };
                    currentChain.Branches.Add(new TemplateIfBranch { Condition = cond ?? "false", Position = position, Children = new List<SqxNode> { element } });
                    rewritten.Add(currentChain);
                }
                else if (vifKind == "elseif" && currentChain != null)
                {
                    currentChain.Branches.Add(new TemplateIfBranch { Condition = cond ?? "false", Position = position, Children = new List<SqxNode> { element } });
                }
                else if (vifKind == "else" && currentChain != null)
                {
                    currentChain.Branches.Add(new TemplateIfBranch { IsElse = true, Position = position, Children = new List<SqxNode> { element } });
                    currentChain = null;
                }
                else
                {
                    throw new SqxParseException(
                        "v-" + (vifKind == "elseif" ? "else-if" : "else") + " must immediately follow a v-if or v-else-if branch",
                        position,
                        "SQV0004");
                }
                continue;
            }

            currentChain = null;
            rewritten.Add(element);
        }
        return rewritten;
    }

    private static void StripVueMarkerAttributes(SqxElement element) =>
        element.Attributes.RemoveAll(a =>
            a.Name == "__vfor_src" || a.Name == "__vfor_item" || a.Name == "__vfor_index" ||
            a.Name == "__vfor_key" ||
            a.Name == "__vif_kind" || a.Name == "__vif_cond");

    private static SqxAttribute FindAttr(SqxElement element, string name) =>
        element.Attributes.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));

    private SqvToken Peek() => _index < _tokens.Count ? _tokens[_index] : _tokens[_tokens.Count - 1];

    private SqvToken Expect(SqvTokenType type)
    {
        var token = Peek();
        if (token.Type != type)
            throw Error("Expected " + type + " but got " + token.Type, token.Offset);
        _index++;
        return token;
    }

    private int Absolute(int position) => _baseOffset + position;

    private SqxParseException Error(string message, int position) =>
        new(message, Absolute(position), "SQV0001");
}

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
            var property = GetModelProperty(element.TagName);
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

            var eventName = GetModelEvent(element.TagName, modifiers.Contains("lazy"));
            var targetValue = GetModelTargetValue(element.TagName);
            var writeValue = ApplyModelModifiers(targetValue, modifiers);

            element.Attributes.Add(ExprAttr(property.Value.AttributeName, value, attr.Line, attr.Position));
            var eventAttribute = ExprAttr(ToEventAttribute(eventName),
                "e => " + value + ".Value = " + writeValue, attr.Line, attr.Position);
            eventAttribute.IsModelEvent = true;
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

    internal static TemplateSlotScope ParseSlotScope(string value, int position)
    {
        value = value?.Trim() ?? "";
        if (IsValidIdentifier(value))
            return new TemplateSlotScope { WholePropsName = value, Position = position };
        if (value.Length < 2 || value[0] != '{' || value[value.Length - 1] != '}')
            throw new SqxParseException("Scoped slot binding must be an identifier or an object destructuring pattern", position, "SQV0008");

        var scope = new TemplateSlotScope { Position = position };
        var locals = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawPart in value.Substring(1, value.Length - 2).Split(','))
        {
            var part = rawPart.Trim();
            if (part.Length == 0) continue;
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
                Position = position
            });
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

    private static ModelProperty? GetModelProperty(string tagName)
    {
        if (string.Equals(tagName, "CheckBox", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tagName, "Radio", StringComparison.OrdinalIgnoreCase))
            return new ModelProperty("checked");
        if (string.Equals(tagName, "Input", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tagName, "TextArea", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tagName, "Select", StringComparison.OrdinalIgnoreCase))
            return new ModelProperty("value");
        return IsBuiltInTag(tagName) ? null : new ModelProperty("Value");
    }

    private static string GetModelEvent(string tagName, bool lazy)
    {
        if (string.Equals(tagName, "Input", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tagName, "TextArea", StringComparison.OrdinalIgnoreCase))
            return lazy ? "change" : "input";
        return "change";
    }

    private static string GetModelTargetValue(string tagName)
    {
        if (string.Equals(tagName, "CheckBox", StringComparison.OrdinalIgnoreCase))
            return "((Square.Controls.CheckBox)e.Target!).IsChecked";
        if (string.Equals(tagName, "Radio", StringComparison.OrdinalIgnoreCase))
            return "((Square.Controls.Radio)e.Target!).IsChecked";
        if (string.Equals(tagName, "TextArea", StringComparison.OrdinalIgnoreCase))
            return "((Square.Controls.TextArea)e.Target!).Value";
        if (string.Equals(tagName, "Select", StringComparison.OrdinalIgnoreCase))
            return "((Square.Controls.Select)e.Target!).Value";
        if (string.Equals(tagName, "Input", StringComparison.OrdinalIgnoreCase))
            return "((Square.Controls.Input)e.Target!).Value";
        return "((" + tagName + ")e.Target!).Value";
    }

    private static bool IsBuiltInTag(string tagName) => tagName.ToLowerInvariant() is
        "view" or "scrollviewer" or "popup" or "dialog" or "menubar" or "menu" or
        "contextmenu" or "menuitem" or "menuseparator" or "text" or "list" or "virtuallist" or "listitem" or
        "tree" or "virtualtree" or "treeitem" or "swiper" or "button" or "input" or "textarea" or "checkbox" or
        "radio" or "select" or "image" or "canvas" or "titlebar" or "link" or "svg" or "g" or
        "path" or "rect" or "circle" or "ellipse" or "line" or "polyline" or "polygon";

    private static string ApplyModelModifiers(string valueExpression, HashSet<string> modifiers)
    {
        if (modifiers.Contains("trim")) valueExpression += ".Trim()";
        if (modifiers.Contains("number"))
            valueExpression = "double.Parse(" + valueExpression + ", System.Globalization.CultureInfo.InvariantCulture)";
        return valueExpression;
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
