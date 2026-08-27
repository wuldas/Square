using Square.Compiler.Syntax;
using Square.Compiler.Template.Ir;
using Square.Compiler.Template.Lowering;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class TemplateLoweringParityTests
{
    [Fact]
    public void SqxAndSqvEventsLowerToTheSameTemplateIr()
    {
        const string sqx = "<Button text=\"Save\" onClick={OnSave} />";
        const string sqv = "<Button text=\"Save\" @click=\"OnSave\" />";

        var sqxIr = SqxTemplateLowerer.Lower(SqxTemplateSyntaxParser.Parse(sqx, 100, tolerant: false));
        var sqvIr = SqvTemplateLowerer.Lower(SqvTemplateSyntaxParser.Parse(sqv, 100, tolerant: false));
        var sqxButton = Assert.IsType<TemplateIrElement>(Assert.Single(sqxIr.Roots));
        var sqvButton = Assert.IsType<TemplateIrElement>(Assert.Single(sqvIr.Roots));

        Assert.Equal(sqxButton.TagName, sqvButton.TagName);
        Assert.Equal(sqxButton.Attributes.Count, sqvButton.Attributes.Count);
        for (var index = 0; index < sqxButton.Attributes.Count; index++)
        {
            Assert.Equal(sqxButton.Attributes[index].Name, sqvButton.Attributes[index].Name);
            Assert.Equal(sqxButton.Attributes[index].Value, sqvButton.Attributes[index].Value);
            Assert.Equal(sqxButton.Attributes[index].IsExpression, sqvButton.Attributes[index].IsExpression);
        }
        Assert.Equal(new[] { "text", "onClick" }, sqxButton.Attributes.Select(attribute => attribute.Name));
    }

    [Fact]
    public void SqxShowAndSqvIfLowerToTheSameConditionalIr()
    {
        const string sqx = "<Show when={Ready}><Text text=\"yes\" /></Show>";
        const string sqv = "<Text v-if=\"Ready\" text=\"yes\" />";

        var sqxChain = Assert.IsType<TemplateIrIfChain>(Assert.Single(
            SqxTemplateLowerer.Lower(SqxTemplateSyntaxParser.Parse(sqx)).Roots));
        var sqvChain = Assert.IsType<TemplateIrIfChain>(Assert.Single(
            SqvTemplateLowerer.Lower(SqvTemplateSyntaxParser.Parse(sqv)).Roots));

        var sqxBranch = Assert.Single(sqxChain.Branches);
        var sqvBranch = Assert.Single(sqvChain.Branches);
        Assert.Equal("Ready", sqxBranch.Condition);
        Assert.Equal(sqxBranch.Condition, sqvBranch.Condition);
        var sqxText = Assert.IsType<TemplateIrElement>(Assert.Single(sqxBranch.Children));
        var sqvText = Assert.IsType<TemplateIrElement>(Assert.Single(sqvBranch.Children));
        Assert.Equal(sqxText.TagName, sqvText.TagName);
        Assert.DoesNotContain(sqvText.Attributes, attribute => attribute.Name.StartsWith("__vif", StringComparison.Ordinal));
    }

    [Fact]
    public void SqxForAndSqvForLowerToTheSameLoopIr()
    {
        const string sqx = "<For each={Items}>{(item)=><Text text={item} />}</For>";
        const string sqv = "<Text v-for=\"item in Items\" :text=\"item\" />";

        var sqxLoop = Assert.IsType<TemplateIrFor>(Assert.Single(
            SqxTemplateLowerer.Lower(SqxTemplateSyntaxParser.Parse(sqx)).Roots));
        var sqvLoop = Assert.IsType<TemplateIrFor>(Assert.Single(
            SqvTemplateLowerer.Lower(SqvTemplateSyntaxParser.Parse(sqv)).Roots));

        Assert.Equal("Items", sqxLoop.SourceExpression);
        Assert.Equal(sqxLoop.SourceExpression, sqvLoop.SourceExpression);
        Assert.Equal("item", sqxLoop.ItemName);
        Assert.Equal(sqxLoop.ItemName, sqvLoop.ItemName);
        var sqxText = Assert.IsType<TemplateIrElement>(Assert.Single(sqxLoop.Children));
        var sqvText = Assert.IsType<TemplateIrElement>(Assert.Single(sqvLoop.Children));
        Assert.Equal("item", Assert.Single(sqxText.Attributes).Value);
        Assert.Equal(Assert.Single(sqxText.Attributes).Value, Assert.Single(sqvText.Attributes).Value);
    }

    [Fact]
    public void SqxSlotAttributeAndSqvSlotTemplateLowerToTheSameSlotIr()
    {
        const string sqx = "<Card><Text slot=\"header\" text=\"Title\" /></Card>";
        const string sqv = "<Card><template #header><Text text=\"Title\" /></template></Card>";

        var sqxCard = Assert.IsType<TemplateIrElement>(Assert.Single(
            SqxTemplateLowerer.Lower(SqxTemplateSyntaxParser.Parse(sqx)).Roots));
        var sqvCard = Assert.IsType<TemplateIrElement>(Assert.Single(
            SqvTemplateLowerer.Lower(SqvTemplateSyntaxParser.Parse(sqv)).Roots));
        var sqxSlot = Assert.IsType<TemplateIrSlot>(Assert.Single(sqxCard.Children));
        var sqvSlot = Assert.IsType<TemplateIrSlot>(Assert.Single(sqvCard.Children));

        Assert.Equal("header", sqxSlot.Name);
        Assert.Equal(sqxSlot.Name, sqvSlot.Name);
        Assert.False(sqxSlot.NameIsExpression);
        Assert.False(sqvSlot.NameIsExpression);
        Assert.Equal("Text", Assert.IsType<TemplateIrElement>(Assert.Single(sqxSlot.Children)).TagName);
        Assert.Equal("Text", Assert.IsType<TemplateIrElement>(Assert.Single(sqvSlot.Children)).TagName);
    }

    [Fact]
    public void SqvDynamicBindingsLowerWithoutSourceSyntaxMarkers()
    {
        const string sqv = "<Button :[propertyName]=\"Value\" @[eventName].stop=\"Handle\" />";

        var button = Assert.IsType<TemplateIrElement>(Assert.Single(
            SqvTemplateLowerer.Lower(SqvTemplateSyntaxParser.Parse(sqv)).Roots));
        var property = button.Attributes[0];
        var @event = button.Attributes[1];

        Assert.Equal(TemplateIrAttributeKind.DynamicProperty, property.Kind);
        Assert.Equal("propertyName", property.ArgumentExpression);
        Assert.Equal(TemplateIrAttributeKind.DynamicEvent, @event.Kind);
        Assert.Equal("eventName", @event.ArgumentExpression);
        Assert.DoesNotContain(button.Attributes, attribute =>
            attribute.Name.Contains("__sqv", StringComparison.Ordinal) ||
            attribute.Name.Contains('@') || attribute.Name.Contains(':') ||
            attribute.Name.StartsWith("v-", StringComparison.Ordinal));
    }

    [Fact]
    public void SqvModelLowersToPropertyAndModelEventOnly()
    {
        var input = Assert.IsType<TemplateIrElement>(Assert.Single(
            SqvTemplateLowerer.Lower(SqvTemplateSyntaxParser.Parse(
                "<Input v-model.trim=\"Name\" />")).Roots));

        Assert.Collection(
            input.Attributes,
            value =>
            {
                Assert.Equal("value", value.Name);
                Assert.Equal("Name", value.Value);
                Assert.Equal(TemplateIrAttributeKind.Property, value.Kind);
            },
            change =>
            {
                Assert.Equal("onInput", change.Name);
                Assert.Equal(TemplateIrAttributeKind.Event, change.Kind);
                Assert.True(change.IsModelEvent);
                Assert.Contains(".Trim()", change.Value, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void SqxAndSqvClassStyleAndSvgLowerToEquivalentIr()
    {
        const string sqx = "<Svg class=\"icon active\" style=\"width: 16px\"><Path d={Data} /></Svg>";
        const string sqv = "<svg class=\"icon active\" style=\"width: 16px\"><path :d=\"Data\" /></svg>";

        var sqxSvg = Assert.IsType<TemplateIrElement>(Assert.Single(
            SqxTemplateLowerer.Lower(SqxTemplateSyntaxParser.Parse(sqx)).Roots));
        var sqvSvg = Assert.IsType<TemplateIrElement>(Assert.Single(
            SqvTemplateLowerer.Lower(SqvTemplateSyntaxParser.Parse(sqv)).Roots));

        Assert.Equal(sqxSvg.TagName, sqvSvg.TagName);
        Assert.Equal(
            sqxSvg.Attributes.Select(attribute => (attribute.Name, attribute.Value, attribute.IsExpression)),
            sqvSvg.Attributes.Select(attribute => (attribute.Name, attribute.Value, attribute.IsExpression)));
        var sqxPath = Assert.IsType<TemplateIrElement>(Assert.Single(sqxSvg.Children));
        var sqvPath = Assert.IsType<TemplateIrElement>(Assert.Single(sqvSvg.Children));
        Assert.Equal(sqxPath.TagName, sqvPath.TagName);
        Assert.Equal("d", Assert.Single(sqvPath.Attributes).Name);
        Assert.True(Assert.Single(sqvPath.Attributes).IsExpression);
    }

    [Fact]
    public void SqxShowFallbackLowersToElseBranch()
    {
        const string sqx = "<Show when={Ready} fallback={<Text text=\"no\" />}><Text text=\"yes\" /></Show>";

        var chain = Assert.IsType<TemplateIrIfChain>(Assert.Single(
            SqxTemplateLowerer.Lower(SqxTemplateSyntaxParser.Parse(sqx)).Roots));

        Assert.Collection(
            chain.Branches,
            branch =>
            {
                Assert.Equal("Ready", branch.Condition);
                Assert.False(branch.IsElse);
                Assert.Equal("yes", Assert.Single(
                    Assert.IsType<TemplateIrElement>(Assert.Single(branch.Children)).Attributes).Value);
            },
            branch =>
            {
                Assert.True(branch.IsElse);
                Assert.Equal("no", Assert.Single(
                    Assert.IsType<TemplateIrElement>(Assert.Single(branch.Children)).Attributes).Value);
            });
    }

    [Fact]
    public void SqxForFallbackIsPreservedInLoopIr()
    {
        const string sqx = "<For each={Items} fallback={<Text text=\"empty\" />}>{(item)=><Text text={item} />}</For>";

        var loop = Assert.IsType<TemplateIrFor>(Assert.Single(
            SqxTemplateLowerer.Lower(SqxTemplateSyntaxParser.Parse(sqx)).Roots));

        Assert.Equal("Items", loop.SourceExpression);
        Assert.Equal("item", loop.ItemName);
        Assert.Equal("empty", Assert.Single(
            Assert.IsType<TemplateIrElement>(Assert.Single(loop.Fallback)).Attributes).Value);
    }

    [Fact]
    public void SqvScopedSlotOnElementLowersWithoutCompatibilityMarkers()
    {
        const string sqv = "<Card #header=\"{ item: row }\" text=\"Title\" />";

        var slot = Assert.IsType<TemplateIrSlot>(Assert.Single(
            SqvTemplateLowerer.Lower(SqvTemplateSyntaxParser.Parse(sqv)).Roots));
        var card = Assert.IsType<TemplateIrElement>(Assert.Single(slot.Children));

        Assert.Equal("header", slot.Name);
        Assert.Equal("{ item: row }", slot.ScopeExpression);
        Assert.Equal("text", Assert.Single(card.Attributes).Name);
        Assert.DoesNotContain(card.Attributes, attribute => attribute.Name.Contains("sqv", StringComparison.OrdinalIgnoreCase));
    }
}
