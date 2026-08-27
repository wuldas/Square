using Square.Compiler.LanguageServices;
using Square.Compiler.Parser;
using Square.Compiler.Template;
using Square.Compiler.Template.Ir;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class TemplateIrTests
{
    [Fact]
    public void TemplateIrUsesLanguageNeutralNodesAndPreservesOrigins()
    {
        var expression = new TemplateIrExpression("Title", new SquareSourceRange(20, 7));
        var button = new TemplateIrElement(
            "Button",
            new[]
            {
                new TemplateIrAttribute("text", "Save", false, new SquareSourceRange(8, 11)),
                new TemplateIrAttribute("click", "OnSave", true, new SquareSourceRange(30, 16))
            },
            new TemplateIrNode[] { expression },
            new SquareSourceRange(0, 54));
        var document = new TemplateIrDocument(new TemplateIrNode[] { button });

        var root = Assert.IsType<TemplateIrElement>(Assert.Single(document.Roots));
        Assert.Equal("Button", root.TagName);
        Assert.Equal(new SquareSourceRange(0, 54), root.Origin);
        Assert.Equal(new SquareSourceRange(30, 16), root.Attributes[1].Origin);
        Assert.Equal(new SquareSourceRange(20, 7), Assert.IsType<TemplateIrExpression>(root.Children[0]).Origin);
        Assert.All(
            new[] { document.GetType(), root.GetType(), expression.GetType() },
            type =>
            {
                Assert.DoesNotContain("Sqx", type.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Sqv", type.Name, StringComparison.OrdinalIgnoreCase);
            });
    }

    [Fact]
    public void CompatibilityTemplateDocumentReferencesSectionIr()
    {
        const string source = "<template><Button /></template>";
        var parsed = SqxParser.Parse(source, "Button.sqx");

        var document = TemplateDocument.From(parsed);

        Assert.Same(parsed.Syntax.Template.Ir, document.Ir);
    }
}
