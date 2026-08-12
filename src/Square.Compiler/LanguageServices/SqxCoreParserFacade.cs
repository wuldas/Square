using Square.Compiler.Directives;
using Square.Compiler.Parser;
using Square.Compiler.ParserCore;

namespace Square.Compiler.LanguageServices;

internal static class SqxCoreParserFacade
{
    public static Square.Compiler.Parser.SqxDocument Parse(
        string source,
        string sourcePath,
        bool strictTemplate)
    {
        var core = SqxCoreParser.Parse(source, sourcePath, new SqxCoreParserOptions
        {
            StrictTemplate = strictTemplate,
            CaseSensitiveSectionNames = true
        });

        return ConvertDocument(core);
    }

    private static Square.Compiler.Parser.SqxDocument ConvertDocument(CoreDocument core)
    {
        var document = new Square.Compiler.Parser.SqxDocument
        {
            SourcePath = core.SourcePath,
            Name = core.Script != null && core.Script.ComponentName != null
                ? core.Script.ComponentName
                : core.FileName,
            Template = new Square.Compiler.Parser.SqxTemplate
            {
                Roots = ConvertNodes(core.Template.Roots, core.Template.Line - 1)
            }
        };

        if (core.Script != null)
        {
            document.ScriptCode = core.Script.Code;
            document.ScriptLang = core.Script.Language;
            document.Namespace = core.Script.Namespace;
            document.Access = core.Script.Access;
        }

        if (core.Style != null)
            document.StyleCode = core.Style.Css;

        return document;
    }

    private static List<Square.Compiler.Parser.SqxNode> ConvertNodes(
        List<CoreNode> nodes,
        int lineOffset)
    {
        var result = new List<Square.Compiler.Parser.SqxNode>(nodes.Count);
        foreach (var node in nodes)
        {
            if (node is CoreText text)
            {
                result.Add(new Square.Compiler.Parser.SqxText
                {
                    Text = text.Text,
                    Kind = Square.Compiler.Parser.SqxNodeKind.Text,
                    Line = text.Line + lineOffset,
                    Column = text.Column,
                    Position = text.Position
                });
                continue;
            }

            if (node is CoreExpression expression)
            {
                result.Add(new Square.Compiler.Parser.SqxExpression
                {
                    Expression = expression.Expression,
                    Kind = Square.Compiler.Parser.SqxNodeKind.Expression,
                    Line = expression.Line + lineOffset,
                    Column = expression.Column,
                    Position = expression.Position
                });
                continue;
            }

            var element = (CoreElement)node;
            string directiveId = null;
            var kind = Square.Compiler.Parser.SqxNodeKind.Element;
            if (DirectiveCatalog.BuiltIn.TryGet(element.TagName, out var descriptor))
            {
                kind = Square.Compiler.Parser.SqxNodeKind.Directive;
                directiveId = descriptor.TagName;
            }

            result.Add(new Square.Compiler.Parser.SqxElement
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

    private static List<Square.Compiler.Parser.SqxAttribute> ConvertAttributes(
        List<CoreAttribute> attributes,
        int lineOffset)
    {
        var result = new List<Square.Compiler.Parser.SqxAttribute>(attributes.Count);
        foreach (var attribute in attributes)
        {
            result.Add(new Square.Compiler.Parser.SqxAttribute
            {
                Name = attribute.Name,
                RawValue = attribute.RawValue,
                IsExpression = attribute.IsExpression,
                FragmentNodes = attribute.FragmentNodes == null
                    ? null
                    : ConvertNodes(attribute.FragmentNodes, lineOffset),
                Line = attribute.Line + lineOffset,
                Position = attribute.Position
            });
        }

        return result;
    }
}
