using Square.Compiler.Directives;
using Square.Compiler.Parser;
using Square.Compiler.Syntax;
using Square.Compiler.Template.Ir;

namespace Square.Compiler.Template.Compatibility;

internal static class TemplateIrCompatibilityAdapter
{
    public static List<SqxNode> ToSqxNodes(
        TemplateIrDocument document,
        string sourceText,
        ComponentDialect? dialect = null,
        int templateContentOffset = 0) =>
        new ConversionContext(sourceText ?? string.Empty, dialect, templateContentOffset)
            .ConvertNodes(document?.Roots ?? Array.Empty<TemplateIrNode>());

    private sealed class ConversionContext
    {
        private readonly string _source;
        private readonly ComponentDialect? _dialect;
        private readonly int _templateContentOffset;
        private readonly int _sqxLineBase;

        public ConversionContext(
            string source,
            ComponentDialect? dialect,
            int templateContentOffset,
            int? sqxLineBase = null)
        {
            _source = source;
            _dialect = dialect;
            _templateContentOffset = Math.Max(0, Math.Min(templateContentOffset, source.Length));
            _sqxLineBase = sqxLineBase ?? GetAbsoluteLine(source, _templateContentOffset);
        }

        public List<SqxNode> ConvertNodes(IReadOnlyList<TemplateIrNode> nodes)
        {
            var result = new List<SqxNode>(nodes.Count);
            foreach (var node in nodes) result.Add(ConvertNode(node));
            return result;
        }

        private SqxNode ConvertNode(TemplateIrNode node)
        {
            var location = GetLocation(node.Origin.Offset);
            if (node is TemplateIrText text)
            {
                return new SqxText
                {
                    Text = text.Text,
                    Kind = SqxNodeKind.Text,
                    Line = location.Line,
                    Column = location.Column,
                    Position = node.Origin.Offset
                };
            }
            if (node is TemplateIrExpression expression)
            {
                return new SqxExpression
                {
                    Expression = expression.Expression,
                    Kind = SqxNodeKind.Expression,
                    Line = location.Line,
                    Column = location.Column,
                    Position = node.Origin.Offset
                };
            }
            if (node is TemplateIrFor loop)
                return _dialect == ComponentDialect.Sqx
                    ? ConvertSqxFor(loop, location)
                    : ConvertSqvFor(loop, location);
            if (node is TemplateIrIfChain chain)
                return _dialect == ComponentDialect.Sqx
                    ? ConvertSqxConditional(chain, location)
                    : ConvertSqvConditional(chain, location);
            if (node is TemplateIrSlot slot)
                return ConvertSlot(slot, location);
            return ConvertElement((TemplateIrElement)node, location);
        }

        private SqxNode ConvertSqvFor(TemplateIrFor loop, SourceLocation location) =>
            new TemplateForDirective
            {
                Kind = SqxNodeKind.Directive,
                SourceExpression = loop.SourceExpression,
                ItemName = loop.ItemName,
                IndexName = loop.IndexName,
                KeyExpression = loop.KeyExpression,
                KeyPosition = loop.Origin.Offset,
                Children = ConvertNodes(loop.Children),
                Line = location.Line,
                Column = location.Column,
                Position = loop.Origin.Offset
            };

        private SqxElement ConvertSqxFor(TemplateIrFor loop, SourceLocation location)
        {
            var children = new List<SqxNode>
            {
                new SqxExpression
                {
                    Expression = "(" + loop.ItemName +
                                 (string.IsNullOrWhiteSpace(loop.IndexName) ? "" : ", " + loop.IndexName) + ")=>",
                    Kind = SqxNodeKind.Expression,
                    Line = location.Line,
                    Column = location.Column,
                    Position = loop.Origin.Offset
                }
            };
            children.AddRange(ConvertNodes(loop.Children));
            children.Add(new SqxExpression
            {
                Expression = "}",
                Kind = SqxNodeKind.Expression,
                Line = location.Line,
                Column = location.Column,
                Position = loop.Origin.End - 1
            });
            var element = new SqxElement
            {
                TagName = "For",
                DirectiveId = "For",
                Kind = SqxNodeKind.Directive,
                Children = children,
                Line = location.Line,
                Column = location.Column + 1,
                Position = loop.Origin.Offset
            };
            element.Attributes.Add(new SqxAttribute
            {
                Name = "each",
                RawValue = loop.SourceExpression,
                IsExpression = true,
                Line = location.Line,
                Position = loop.Origin.Offset
            });
            if (!string.IsNullOrWhiteSpace(loop.KeyExpression))
                element.Attributes.Add(new SqxAttribute
                {
                    Name = "key",
                    RawValue = loop.KeyExpression,
                    IsExpression = true,
                    Line = location.Line,
                    Position = loop.Origin.Offset
                });
            if (loop.Fallback.Count > 0)
                element.Attributes.Add(new SqxAttribute
                {
                    Name = "fallback",
                    IsExpression = true,
                    FragmentNodes = ConvertFragmentNodes(loop.Fallback),
                    Line = location.Line,
                    Position = loop.Origin.Offset
                });
            return element;
        }

        private SqxNode ConvertSqvConditional(TemplateIrIfChain chain, SourceLocation location)
        {
            var legacy = new TemplateIfChainDirective
            {
                Kind = SqxNodeKind.Directive,
                Line = location.Line,
                Column = location.Column,
                Position = chain.Origin.Offset
            };
            foreach (var branch in chain.Branches)
            {
                legacy.Branches.Add(new TemplateIfBranch
                {
                    Condition = branch.Condition,
                    IsElse = branch.IsElse,
                    Position = branch.Origin.Offset,
                    Children = ConvertNodes(branch.Children)
                });
            }
            return legacy;
        }

        private SqxElement ConvertSqxConditional(TemplateIrIfChain chain, SourceLocation location)
        {
            var primary = chain.Branches.FirstOrDefault(branch => !branch.IsElse);
            var fallback = chain.Branches.FirstOrDefault(branch => branch.IsElse);
            var element = new SqxElement
            {
                TagName = "Show",
                DirectiveId = "Show",
                Kind = SqxNodeKind.Directive,
                Children = primary == null ? new List<SqxNode>() : ConvertNodes(primary.Children),
                Line = location.Line,
                Column = location.Column + 1,
                Position = chain.Origin.Offset
            };
            if (!string.IsNullOrWhiteSpace(primary?.Condition))
                element.Attributes.Add(new SqxAttribute
                {
                    Name = "when",
                    RawValue = primary.Condition,
                    IsExpression = true,
                    Line = location.Line,
                    Position = primary.Origin.Offset
                });
            if (fallback != null)
                element.Attributes.Add(new SqxAttribute
                {
                    Name = "fallback",
                    IsExpression = true,
                    FragmentNodes = ConvertFragmentNodes(fallback.Children),
                    Line = GetLocation(fallback.Origin.Offset).Line,
                    Position = fallback.Origin.Offset
                });
            return element;
        }

        private SqxElement ConvertSlot(TemplateIrSlot slot, SourceLocation location)
        {
            var slotElement = new SqxElement
            {
                TagName = "template",
                Kind = SqxNodeKind.Element,
                Line = location.Line,
                Column = location.Column,
                Position = slot.Origin.Offset,
                Children = ConvertNodes(slot.Children)
            };
            slotElement.Attributes.Add(new SqxAttribute
            {
                Name = "slot",
                RawValue = slot.Name,
                IsExpression = slot.NameIsExpression,
                Line = location.Line,
                Position = slot.Origin.Offset
            });
            if (slot.Scope != null)
            {
                slotElement.SlotScope = new TemplateSlotScope
                {
                    WholePropsName = slot.Scope.WholePropertiesName,
                    Position = slot.Scope.Origin.Offset
                };
                foreach (var binding in slot.Scope.Properties)
                    slotElement.SlotScope.Properties.Add(new TemplateSlotPropertyBinding
                    {
                        PropertyName = binding.PropertyName,
                        LocalName = binding.LocalName,
                        TypeName = binding.TypeName,
                        Position = binding.Origin.Offset
                    });
            }
            else if (!string.IsNullOrWhiteSpace(slot.ScopeExpression))
                slotElement.SlotScope = SqvAttributeConverter.ParseSlotScope(
                    slot.ScopeExpression,
                    slot.Origin.Offset);
            return slotElement;
        }

        private SqxElement ConvertElement(TemplateIrElement element, SourceLocation location)
        {
            var kind = SqxNodeKind.Element;
            string directiveId = null;
            if (DirectiveCatalog.BuiltIn.TryGet(element.TagName, out var descriptor))
            {
                kind = SqxNodeKind.Directive;
                directiveId = descriptor.TagName;
            }
            return new SqxElement
            {
                TagName = element.TagName,
                DirectiveId = directiveId,
                Kind = kind,
                Attributes = element.Attributes.Select(ConvertAttribute).ToList(),
                Children = ConvertNodes(element.Children),
                Line = location.Line,
                Column = location.Column + 1,
                Position = element.Origin.Offset
            };
        }

        private SqxAttribute ConvertAttribute(TemplateIrAttribute attribute)
        {
            var location = GetLocation(attribute.Origin.Offset);
            var name = attribute.Name;
            if (attribute.Kind == TemplateIrAttributeKind.DynamicProperty) name = "__sqv_dynamic_property";
            else if (attribute.Kind == TemplateIrAttributeKind.DynamicEvent) name = "__sqv_dynamic_event";
            else if (attribute.Kind == TemplateIrAttributeKind.ObjectProperties) name = "__sqv_bind_object";
            else if (attribute.Kind == TemplateIrAttributeKind.ObjectEvents) name = "__sqv_on_object";
            return new SqxAttribute
            {
                Name = name,
                RawValue = attribute.Value,
                IsExpression = attribute.IsExpression,
                IsDynamicProperty = attribute.Kind == TemplateIrAttributeKind.DynamicProperty,
                IsDynamicEvent = attribute.Kind == TemplateIrAttributeKind.DynamicEvent,
                IsModelEvent = attribute.IsModelEvent,
                ArgumentExpression = attribute.ArgumentExpression,
                FragmentNodes = attribute.FragmentNodes == null
                    ? null
                    : ConvertFragmentNodes(attribute.FragmentNodes),
                Line = location.Line,
                Position = attribute.Origin.Offset
            };
        }

        private List<SqxNode> ConvertFragmentNodes(IReadOnlyList<TemplateIrNode> nodes)
        {
            if (nodes.Count == 0) return new List<SqxNode>();
            return new ConversionContext(
                _source,
                _dialect,
                nodes.Min(node => node.Origin.Offset),
                _sqxLineBase).ConvertNodes(nodes);
        }

        private SourceLocation GetLocation(int offset)
        {
            offset = Math.Max(_templateContentOffset, Math.Min(offset, _source.Length));
            var line = 1;
            var column = 1;
            for (var index = _templateContentOffset; index < offset; index++)
            {
                if (_source[index] == '\n')
                {
                    line++;
                    column = 1;
                }
                else
                {
                    column++;
                }
            }
            if (_dialect == ComponentDialect.Sqx)
                line += _sqxLineBase - 1;
            return new SourceLocation(line, column);
        }

        private static int GetAbsoluteLine(string source, int offset)
        {
            var line = 1;
            for (var index = 0; index < offset && index < source.Length; index++)
                if (source[index] == '\n') line++;
            return line;
        }
    }

    private readonly struct SourceLocation
    {
        public SourceLocation(int line, int column)
        {
            Line = line;
            Column = column;
        }

        public int Line { get; }
        public int Column { get; }
    }
}
