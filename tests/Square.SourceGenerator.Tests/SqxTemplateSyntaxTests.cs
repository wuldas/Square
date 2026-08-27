using Square.Compiler.Parser;
using Square.Compiler.Syntax;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class SqxTemplateSyntaxTests
{
    [Fact]
    public void SqxSourceSyntaxPreservesUnicodeAttributeAndExpressionRanges()
    {
        const string source = "<template>\r\n<View title=\"世界\" count={Value}>{  Text  }</View>\r\n</template>";
        var section = ComponentSectionScanner.Scan(source, "Card.sqx", ComponentDialect.Sqx, tolerant: false)
            .Document.Template;

        var syntax = SqxTemplateSyntaxParser.Parse(section.ContentText, section.ContentRange.Offset, tolerant: false);
        var view = Assert.IsType<SqxElementSyntax>(Assert.Single(syntax.Roots));
        var title = view.Attributes[0];
        var count = view.Attributes[1];
        var expression = Assert.IsType<SqxExpressionSyntax>(Assert.Single(view.Children));

        Assert.Equal("<View title=\"世界\" count={Value}>{  Text  }</View>",
            source.Substring(view.Origin.Offset, view.Origin.Length));
        Assert.Equal("title", source.Substring(title.NameRange.Offset, title.NameRange.Length));
        Assert.Equal("世界", source.Substring(title.ValueRange.Offset, title.ValueRange.Length));
        Assert.Equal("Value", source.Substring(count.ValueRange.Offset, count.ValueRange.Length));
        Assert.Equal("{  Text  }", source.Substring(expression.Origin.Offset, expression.Origin.Length));
    }

    [Fact]
    public void SqxTolerantParserUsesTheSameTokensForPartialElements()
    {
        const string source = "<Button text=";

        Assert.Throws<SqxParseException>(() => SqxTemplateSyntaxParser.Parse(source, 0, tolerant: false));
        var syntax = SqxTemplateSyntaxParser.Parse(source, 20, tolerant: true);
        var button = Assert.IsType<SqxElementSyntax>(Assert.Single(syntax.Roots));

        Assert.Equal("Button", button.TagName);
        Assert.Equal(20, button.Origin.Offset);
        Assert.Equal("text", Assert.Single(button.Attributes).Name);
    }
}
