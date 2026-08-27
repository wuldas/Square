using Square.Compiler.Directives;
using Square.Compiler.ParserCore;

namespace Square.Compiler.Parser
{
    internal static class SqxParser
    {
        public static SqxDocument Parse(string source, string fileName)
        {
            try
            {
                var core = SqxCoreParser.Parse(source, fileName, new SqxCoreParserOptions
                {
                    StrictTemplate = true,
                    CaseSensitiveSectionNames = true
                });
                var document = ConvertDocument(core);
                SqxValidator.Validate(document.Template.Roots);
                return document;
            }
            catch (CoreParseException exception)
            {
                throw new SqxParseException(exception.Message, exception.Position, length: exception.Length);
            }
        }

        private static SqxDocument ConvertDocument(CoreDocument core)
        {
            var document = new SqxDocument
            {
                Syntax = core.Syntax,
                SourcePath = core.SourcePath,
                Name = core.Script != null && core.Script.ComponentName != null
                    ? core.Script.ComponentName
                    : core.FileName,
                Template = new SqxTemplate { Roots = ConvertNodes(core.Template.Roots, core.Template.Line - 1) }
            };
            if (core.Script != null)
            {
                document.ScriptCode = core.Script.Code;
                document.ScriptLang = core.Script.Language;
                document.Namespace = core.Script.Namespace;
                document.Access = core.Script.Access;
            }
            if (core.Style != null) document.StyleCode = core.Style.Css;
            return document;
        }

        private static List<SqxNode> ConvertNodes(List<CoreNode> nodes, int lineOffset)
        {
            var result = new List<SqxNode>(nodes.Count);
            foreach (var node in nodes)
            {
                var text = node as CoreText;
                if (text != null)
                {
                    result.Add(new SqxText
                    {
                        Text = text.Text,
                        Kind = SqxNodeKind.Text,
                        Line = text.Line + lineOffset,
                        Column = text.Column,
                        Position = text.Position
                    });
                    continue;
                }

                var expression = node as CoreExpression;
                if (expression != null)
                {
                    result.Add(new SqxExpression
                    {
                        Expression = expression.Expression,
                        Kind = SqxNodeKind.Expression,
                        Line = expression.Line + lineOffset,
                        Column = expression.Column,
                        Position = expression.Position
                    });
                    continue;
                }

                var element = (CoreElement)node;
                string directiveId = null;
                var kind = SqxNodeKind.Element;
                DirectiveDescriptor descriptor;
                if (DirectiveCatalog.BuiltIn.TryGet(element.TagName, out descriptor))
                {
                    kind = SqxNodeKind.Directive;
                    directiveId = descriptor.TagName;
                }
                result.Add(new SqxElement
                {
                    TagName = element.TagName,
                    DirectiveId = directiveId,
                    Attributes = ConvertAttributes(element.Attributes, lineOffset),
                    Children = ConvertNodes(element.Children, lineOffset),
                    Kind = kind,
                    Line = element.Line + lineOffset,
                    Column = element.Column + 1,
                    Position = element.Position
                });
            }
            return result;
        }

        private static List<SqxAttribute> ConvertAttributes(List<CoreAttribute> attributes, int lineOffset)
        {
            var result = new List<SqxAttribute>(attributes.Count);
            foreach (var attribute in attributes)
            {
                result.Add(new SqxAttribute
                {
                    Name = attribute.Name,
                    RawValue = attribute.RawValue,
                    IsExpression = attribute.IsExpression,
                    FragmentNodes = attribute.FragmentNodes == null ? null : ConvertNodes(attribute.FragmentNodes, lineOffset),
                    Line = attribute.Line + lineOffset,
                    Position = attribute.Position
                });
            }
            return result;
        }
    }

    internal sealed class SqxParseException : Exception
    {
        public int Position { get; }
        public int Length { get; }
        public string DiagnosticId { get; }

        public SqxParseException(
            string message,
            int position,
            string diagnosticId = null,
            int length = 0) : base(message)
        {
            Position = position;
            Length = length;
            DiagnosticId = diagnosticId;
        }
    }
}
