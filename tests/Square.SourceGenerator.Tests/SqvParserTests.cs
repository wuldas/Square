using Square.Compiler.Parser;
using Xunit;

namespace Square.Compiler.Tests;

public class SqvParserTests
{
    [Fact]
    public void LexerPreservesDynamicArgumentTokenAndAbsoluteOffset()
    {
        var tokens = new SqvLexer("<Text :[name]=\"Value\" />", 40).Tokenize();
        var attribute = Assert.Single(tokens, token => token.Text == ":[name]");

        Assert.Equal(SqvTokenType.Identifier, attribute.Type);
        Assert.Equal(6, attribute.Offset);
    }

    [Fact]
    public void TemplateParserPreservesExpressionAndAttributePositions()
    {
        var roots = SqvTemplateParser.Parse("<Text :text=\"Title\">{{ Title }}</Text>", 100);
        var element = Assert.IsType<SqxElement>(Assert.Single(roots));
        var attribute = Assert.Single(element.Attributes);
        var expression = Assert.IsType<SqxExpression>(Assert.Single(element.Children));

        Assert.Equal(100, element.Position);
        Assert.Equal(106, attribute.Position);
        Assert.Equal(120, expression.Position);
    }

    [Fact]
    public void TemplateParserRewritesForAndIfWithSourcePositions()
    {
        const string source = "<View><Text v-for=\"item in Items\">{{ item }}</Text><Text v-if=\"Ready\">yes</Text></View>";

        var root = Assert.IsType<SqxElement>(Assert.Single(SqvTemplateParser.Parse(source, 20)));
        var forDirective = Assert.IsType<TemplateForDirective>(root.Children[0]);
        var ifDirective = Assert.IsType<TemplateIfChainDirective>(root.Children[1]);

        Assert.Equal(32, forDirective.Position);
        Assert.Equal(77, ifDirective.Position);
        Assert.Equal(77, Assert.Single(ifDirective.Branches).Position);
    }

    [Fact]
    public void ValidatorFindsDuplicateBindingInsideForDirective()
    {
        var roots = SqvTemplateParser.Parse(
            "<Input v-for=\"item in Items\" value=\"a\" :value=\"item\" />",
            0);

        var exception = Assert.Throws<SqxParseException>(() => SqvValidator.Validate(roots));

        Assert.Equal("SQV0005", exception.DiagnosticId);
    }

    [Fact]
    public void TemplateParserPromotesKeyToForDirective()
    {
        var roots = SqvTemplateParser.Parse(
            "<Text :key=\"item.Id\" v-for=\"item in Items\">{{ item.Name }}</Text>",
            10);

        var directive = Assert.IsType<TemplateForDirective>(Assert.Single(roots));
        var element = Assert.IsType<SqxElement>(Assert.Single(directive.Children));

        Assert.Equal("item.Id", directive.KeyExpression);
        Assert.Equal(16, directive.KeyPosition);
        Assert.DoesNotContain(element.Attributes, attribute => attribute.Name == "__vfor_key");
    }

    [Fact]
    public void ValidatorRejectsInvalidKeyExpression()
    {
        var roots = SqvTemplateParser.Parse(
            "<Text v-for=\"item in Items\" :key=\"item.\" />",
            0);

        var exception = Assert.Throws<SqxParseException>(() => SqvValidator.Validate(roots));

        Assert.Equal("SQV0009", exception.DiagnosticId);
    }

    [Fact]
    public void TemplateParserRepresentsDynamicArgumentsAndScopedSlots()
    {
        var root = Assert.IsType<SqxElement>(Assert.Single(SqvTemplateParser.Parse(
            "<Card><template #[slotName]=\"slotProps\"><Button :[propertyName]=\"Value\" @[eventName]=\"Handle\" /></template></Card>")));
        var template = Assert.IsType<SqxElement>(Assert.Single(root.Children));
        var slot = Assert.Single(template.Attributes, attribute => attribute.Name == "slot");
        var button = Assert.IsType<SqxElement>(Assert.Single(template.Children));

        Assert.True(slot.IsExpression);
        Assert.Equal("slotName", slot.RawValue);
        Assert.Equal("slotProps", template.SlotScope!.WholePropsName);
        Assert.Equal("propertyName", Assert.Single(button.Attributes, attribute => attribute.IsDynamicProperty).ArgumentExpression);
        Assert.Equal("eventName", Assert.Single(button.Attributes, attribute => attribute.IsDynamicEvent).ArgumentExpression);
    }

    [Fact]
    public void ParserRepresentsScopedSlotDestructuring()
    {
        var card = Assert.IsType<SqxElement>(Assert.Single(
            SqvTemplateParser.Parse("<Card><template #default=\"{ item: row, label }\" /></Card>")));
        var template = Assert.IsType<SqxElement>(Assert.Single(card.Children));

        Assert.Collection(template.SlotScope!.Properties,
            item => { Assert.Equal("item", item.PropertyName); Assert.Equal("row", item.LocalName); },
            label => { Assert.Equal("label", label.PropertyName); Assert.Equal("label", label.LocalName); });
    }
}
