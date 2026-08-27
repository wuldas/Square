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
        var syntax = core.Syntax;
        var sourceText = SourceText.From(syntax.SourceText);
        var templateLocation = GetLocation(sourceText, syntax.Template.ContentRange.Offset);
        var template = new SqxTemplate(
            ConvertNodes(syntax.Template.Ir.Roots, sourceText),
            templateLocation.Line,
            templateLocation.Column);
        SqxScript? script = syntax.Script == null
            ? null
            : CreateScript(syntax.Script, sourceText);
        SqxStyle? style = syntax.Style == null
            ? null
            : CreateStyle(syntax.Style, sourceText);
        return new SqxDocument(core.Name, template, script, style);
    }

    private static SqxScript CreateScript(
        Square.Compiler.Syntax.ScriptSectionSyntax script,
        SourceText sourceText)
    {
        var location = GetLocation(sourceText, script.ContentRange.Offset);
        return new SqxScript(
            script.Metadata.Language,
            script.ContentText.Trim(),
            script.Metadata.Namespace,
            script.Metadata.ComponentName,
            script.Metadata.Access,
            location.Line,
            location.Column);
    }

    private static SqxStyle CreateStyle(
        Square.Compiler.Syntax.StyleSectionSyntax style,
        SourceText sourceText)
    {
        var location = GetLocation(sourceText, style.ContentRange.Offset);
        return new SqxStyle(style.ContentText.Trim(), location.Line, location.Column);
    }

    private static List<SqxNode> ConvertNodes(
        IReadOnlyList<Square.Compiler.Template.Ir.TemplateIrNode> nodes,
        SourceText sourceText)
    {
        var result = new List<SqxNode>(nodes.Count);
        foreach (var node in nodes)
        {
            var location = GetLocation(sourceText, node.Origin.Offset);
            if (node is Square.Compiler.Template.Ir.TemplateIrText text)
            {
                result.Add(new SqxText(text.Text, location.Line, location.Column));
                continue;
            }
            if (node is Square.Compiler.Template.Ir.TemplateIrExpression expression)
            {
                result.Add(new SqxExpression(expression.Expression, location.Line, location.Column));
                continue;
            }
            if (node is Square.Compiler.Template.Ir.TemplateIrFor loop)
            {
                var attributes = new List<SqxAttribute>
                {
                    CreateAttribute("each", loop.SourceExpression, true, loop.Origin.Offset, sourceText)
                };
                if (!string.IsNullOrWhiteSpace(loop.KeyExpression))
                    attributes.Add(CreateAttribute("key", loop.KeyExpression, true, loop.Origin.Offset, sourceText));
                result.Add(new SqxElement(
                    "For",
                    attributes,
                    ConvertNodes(loop.Children, sourceText),
                    location.Line,
                    location.Column)
                {
                    Kind = SqxNodeKind.For
                });
                continue;
            }
            if (node is Square.Compiler.Template.Ir.TemplateIrIfChain chain)
            {
                var primary = chain.Branches.FirstOrDefault(branch => !branch.IsElse);
                var attributes = new List<SqxAttribute>();
                if (!string.IsNullOrWhiteSpace(primary?.Condition))
                    attributes.Add(CreateAttribute("when", primary.Condition, true, primary.Origin.Offset, sourceText));
                result.Add(new SqxElement(
                    "Show",
                    attributes,
                    primary == null ? new List<SqxNode>() : ConvertNodes(primary.Children, sourceText),
                    location.Line,
                    location.Column)
                {
                    Kind = SqxNodeKind.Show
                });
                continue;
            }
            if (node is Square.Compiler.Template.Ir.TemplateIrSlot slot)
            {
                result.Add(new SqxElement(
                    "template",
                    new List<SqxAttribute>
                    {
                        CreateAttribute("slot", slot.Name, slot.NameIsExpression, slot.Origin.Offset, sourceText)
                    },
                    ConvertNodes(slot.Children, sourceText),
                    location.Line,
                    location.Column));
                continue;
            }

            var element = (Square.Compiler.Template.Ir.TemplateIrElement)node;
            result.Add(new SqxElement(
                element.TagName,
                element.Attributes.Select(attribute => ConvertAttribute(attribute, sourceText)).ToList(),
                ConvertNodes(element.Children, sourceText),
                location.Line,
                location.Column)
            {
                Kind = GetElementKind(element.TagName)
            });
        }
        return result;
    }

    private static SqxAttribute ConvertAttribute(
        Square.Compiler.Template.Ir.TemplateIrAttribute attribute,
        SourceText sourceText) =>
        CreateAttribute(attribute.Name, attribute.Value, attribute.IsExpression, attribute.Origin.Offset, sourceText);

    private static SqxAttribute CreateAttribute(
        string name,
        string? rawValue,
        bool isExpression,
        int offset,
        SourceText sourceText)
    {
        var location = GetLocation(sourceText, offset);
        var value = rawValue == null ? null : new SqxAttributeValue(isExpression, rawValue);
        return new SqxAttribute(name, rawValue, value, location.Line, location.Column);
    }

    private static (int Line, int Column) GetLocation(SourceText sourceText, int offset)
    {
        offset = Math.Max(0, Math.Min(offset, sourceText.Length));
        var position = sourceText.Lines.GetLinePosition(offset);
        return (position.Line + 1, position.Character + 1);
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
