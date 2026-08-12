using Microsoft.CodeAnalysis.Text;
using Square.Compiler.LanguageServices;
using Square.Markup.Ast;

namespace Square.Markup.Parser;

public sealed class SqxParser
{
    public SqxDocument Parse(string source, string fileName = "")
    {
        ArgumentNullException.ThrowIfNull(source);

        var sharedResult = SquareDocumentService.Parse(source, fileName);
        if (!sharedResult.IsSuccess)
            throw ToParseException(sharedResult.Diagnostics[0], sharedResult.SourceText);

        return ConvertDocument(sharedResult.ParsedSqxDocument);
    }

    private static SqxParseException ToParseException(
        SquareDiagnostic diagnostic,
        SourceText sourceText)
    {
        var span = diagnostic.GetLinePositionSpan(sourceText);
        return new SqxParseException(
            diagnostic.Message,
            diagnostic.Id,
            diagnostic.Range.Offset,
            span.Start.Line + 1,
            span.Start.Character + 1);
    }

    private static SqxDocument ConvertDocument(Square.Compiler.Parser.SqxDocument core)
    {
        var template = new SqxTemplate(
            ConvertNodes(core.Template.Roots),
            core.Template.Roots.FirstOrDefault()?.Line ?? 1,
            core.Template.Roots.FirstOrDefault()?.Column ?? 1);
        SqxScript? script = core.ScriptCode == null
            ? null
            : new SqxScript(
                core.ScriptLang ?? "csharp",
                core.ScriptCode,
                core.Namespace,
                core.Name,
                core.Access,
                1,
                1);
        SqxStyle? style = core.StyleCode == null
            ? null
            : new SqxStyle(core.StyleCode, 1, 1);
        return new SqxDocument(core.Name, template, script, style);
    }

    private static List<SqxNode> ConvertNodes(List<Square.Compiler.Parser.SqxNode> nodes)
    {
        var result = new List<SqxNode>(nodes.Count);
        foreach (var node in nodes)
        {
            if (node is Square.Compiler.Parser.SqxText text)
            {
                result.Add(new SqxText(text.Text, text.Line, text.Column));
                continue;
            }
            if (node is Square.Compiler.Parser.SqxExpression expression)
            {
                result.Add(new SqxExpression(
                    expression.Expression,
                    expression.Line,
                    expression.Column));
                continue;
            }

            var element = (Square.Compiler.Parser.SqxElement)node;
            var converted = new SqxElement(
                element.TagName,
                ConvertAttributes(element.Attributes),
                ConvertNodes(element.Children),
                element.Line,
                element.Column)
            {
                Kind = GetElementKind(element.TagName)
            };
            result.Add(converted);
        }
        return result;
    }

    private static List<SqxAttribute> ConvertAttributes(List<Square.Compiler.Parser.SqxAttribute> attributes)
    {
        var result = new List<SqxAttribute>(attributes.Count);
        foreach (var attribute in attributes)
        {
            var value = attribute.RawValue == null
                ? null
                : new SqxAttributeValue(attribute.IsExpression, attribute.RawValue);
            result.Add(new SqxAttribute(
                attribute.Name,
                attribute.RawValue,
                value,
                attribute.Line,
                1));
        }
        return result;
    }

    private static SqxNodeKind GetElementKind(string tagName) => tagName switch
    {
        "Show" => SqxNodeKind.Show,
        "For" => SqxNodeKind.For,
        "Index" => SqxNodeKind.Index,
        "Switch" => SqxNodeKind.Switch,
        "Match" => SqxNodeKind.Match,
        "Slot" or "Outlet" => SqxNodeKind.Slot,
        _ => SqxNodeKind.Element
    };
}
