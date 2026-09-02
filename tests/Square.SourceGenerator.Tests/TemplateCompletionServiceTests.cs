using Square.Compiler.LanguageServices;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class TemplateCompletionServiceTests
{
    [Fact]
    public void CssDeclarationCompletesSupportedProperties()
    {
        const string source = "<template><View /></template><style>.page { flex-di }</style>";
        var offset = source.IndexOf("flex-di", StringComparison.Ordinal) + "flex-di".Length;

        var context = TemplateCompletionService.GetContext(source, offset, "Editing.sqx");
        var items = TemplateCompletionService.GetItems(context, source);

        Assert.Equal(TemplateCompletionKind.CssProperty, context.Kind);
        Assert.Equal("flex-di", context.Prefix);
        Assert.Contains(items, item => item.Label == "flex-direction" && item.Kind == 10);
        Assert.DoesNotContain(items, item => item.Label == "display");
    }

    [Fact]
    public void CssDeclarationValueCompletesValuesForCurrentProperty()
    {
        const string source = "<template><View /></template><style>.page { display: fl }</style>";
        var offset = source.IndexOf("fl }", StringComparison.Ordinal) + "fl".Length;

        var context = TemplateCompletionService.GetContext(source, offset, "Editing.sqx");
        var items = TemplateCompletionService.GetItems(context, source);

        Assert.Equal(TemplateCompletionKind.CssValue, context.Kind);
        Assert.Equal("display", context.AttributeName);
        Assert.Contains(items, item => item.Label == "flex");
        Assert.DoesNotContain(items, item => item.Label == "column");
    }

    [Fact]
    public void CssSelectorCompletesTemplateClassAndBuiltInControl()
    {
        const string source = "<template><View class=\"panel\" /></template><style>.pa</style>";
        var offset = source.IndexOf(".pa", StringComparison.Ordinal) + ".pa".Length;

        var items = TemplateCompletionService.GetItems(source, offset, "Editing.sqx");

        Assert.Contains(items, item => item.Label == ".panel");
        Assert.DoesNotContain(items, item => item.Label == "Button");
    }

    [Fact]
    public void CssTypeSelectorCompletesBuiltInControls()
    {
        const string source = "<template><View /></template><style>But</style>";
        var offset = source.IndexOf("</style>", StringComparison.Ordinal);

        var items = TemplateCompletionService.GetItems(source, offset, "Editing.sqx");

        Assert.Contains(items, item => item.Label == "Button");
        Assert.DoesNotContain(items, item => item.Label == "View");
    }

    [Fact]
    public void CssSelectorInsideMediaRuleIsNotTreatedAsAProperty()
    {
        const string source = "<template><View class=\"panel\" /></template><style>@media screen { .pa }</style>";
        var offset = source.IndexOf(".pa", StringComparison.Ordinal) + ".pa".Length;

        var context = TemplateCompletionService.GetContext(source, offset, "Editing.sqx");
        var items = TemplateCompletionService.GetItems(context, source);

        Assert.Equal(TemplateCompletionKind.CssSelector, context.Kind);
        Assert.Contains(items, item => item.Label == ".panel");
    }

    [Fact]
    public void CssAtRuleCompletionIncludesSupportedRuntimeRules()
    {
        const string source = "<template><View /></template><style>@key</style>";
        var offset = source.IndexOf("</style>", StringComparison.Ordinal);

        var context = TemplateCompletionService.GetContext(source, offset, "Editing.sqx");
        var items = TemplateCompletionService.GetItems(context, source);

        Assert.Equal(TemplateCompletionKind.CssAtRule, context.Kind);
        Assert.Contains(items, item => item.Label == "@keyframes");
        Assert.DoesNotContain(items, item => item.Label == "@supports");
    }

    [Theory]
    [InlineData("<template><View /></template><style>Button:hov</style>", "hover", TemplateCompletionKind.CssPseudoClass)]
    [InlineData("<template><View /></template><style>Text::be</style>", "before", TemplateCompletionKind.CssPseudoElement)]
    public void CssSelectorCompletesSupportedPseudoSelectors(
        string source,
        string expected,
        TemplateCompletionKind expectedKind)
    {
        var offset = source.IndexOf("</style>", StringComparison.Ordinal);

        var context = TemplateCompletionService.GetContext(source, offset, "Editing.sqx");
        var items = TemplateCompletionService.GetItems(context, source);

        Assert.Equal(expectedKind, context.Kind);
        Assert.Contains(items, item => item.Label == expected);
    }

    [Fact]
    public void CssValueCompletesCustomPropertiesThroughVar()
    {
        const string source = "<template><View /></template><style>:root { --accent: #fff; } .page { color: var(--ac }</style>";
        var offset = source.IndexOf("--ac }", StringComparison.Ordinal) + "--ac".Length;

        var items = TemplateCompletionService.GetItems(source, offset, "Editing.sqx");

        Assert.Contains(items, item => item.Label == "var(--accent)");
    }

    [Fact]
    public void IncompleteTagUsesSyntaxTreeForControlFlowCompletion()
    {
        const string source = "<template><Sh";

        var items = TemplateCompletionService.GetItems(source, source.Length, "Editing.sqx");

        Assert.Contains(items, item => item.Label == "Show" && item.Detail == "Show");
        Assert.DoesNotContain(items, item => item.Label == "Button");
    }

    [Fact]
    public void IncompleteAttributeUsesOwningElementFromSyntaxTree()
    {
        const string source = "<template><Button te";

        var context = TemplateCompletionService.GetContext(source, source.Length, "Editing.sqx");

        Assert.Equal(TemplateCompletionKind.Attribute, context.Kind);
        Assert.Equal("Button", context.TagName);
        Assert.Equal("te", context.Prefix);
        Assert.Contains(
            TemplateCompletionService.GetItems(context, source),
            item => item.Label == "text");
    }

    [Fact]
    public void VueDirectivePrefixCompletesSupportedDirectives()
    {
        const string source = "<template><Text v-";

        var items = TemplateCompletionService.GetItems(source, source.Length, "Editing.sqv");

        Assert.Contains(items, item => item.Label == "v-if" && item.Detail == "Vue directive");
        Assert.Contains(items, item => item.Label == "v-for");
        Assert.DoesNotContain(items, item => item.Label == "v-html");
    }

    [Fact]
    public void SqxEventPrefixCompletesCanonicalEventName()
    {
        const string source = "<template><Button on";

        var items = TemplateCompletionService.GetItems(source, source.Length, "Editing.sqx");

        Assert.Contains(items, item => item.Label == "onClick");
        Assert.DoesNotContain(items, item => item.Label == "click");
    }

    [Fact]
    public void SqvAtPrefixCompletesVueEventName()
    {
        const string source = "<template><Button @";

        var items = TemplateCompletionService.GetItems(source, source.Length, "Editing.sqv");

        Assert.Contains(items, item => item.Label == "click");
        Assert.DoesNotContain(items, item => item.Label == "onClick");
    }

    [Theory]
    [InlineData("<template><View /></template><script>private const string X = \"<Bu\";</script>")]
    [InlineData("<template><View /></template><style>.x { content: \"<Bu\"; }</style>")]
    public void CompletionDoesNotUseTemplateFallbackOutsideTemplateSection(string source)
    {
        var offset = source.IndexOf("<Bu", source.IndexOf("</template>", StringComparison.Ordinal), StringComparison.Ordinal) + 3;

        var context = TemplateCompletionService.GetContext(source, offset, "Editing.sqx");

        Assert.Equal(TemplateCompletionKind.None, context.Kind);
    }

    [Fact]
    public void SqxEventValueCompletesMethodsFromCurrentScript()
    {
        const string source = """
            <template><Button onClick={OnS} /></template>
            <script>
            private Event? OnStatus;
            private Event? OnState { get; set; }
            private void OnSave(Event e) { }
            private void OnSearch(Event e) { }
            private void Reset(Event e) { }
            </script>
            """;
        var offset = source.IndexOf("OnS}", StringComparison.Ordinal) + "OnS".Length;

        var context = TemplateCompletionService.GetContext(source, offset, "Editing.sqx");
        var items = TemplateCompletionService.GetItems(source, offset, "Editing.sqx");

        Assert.Equal(TemplateCompletionKind.EventHandler, context.Kind);
        Assert.Equal("OnS", context.Prefix);
        Assert.Collection(
            items.OrderBy(item => item.Label),
            item =>
            {
                Assert.Equal("OnSave", item.Label);
                Assert.Equal(3, item.Kind);
                Assert.Equal("OnSave", item.InsertText);
                Assert.Contains("Event e", item.Detail, StringComparison.Ordinal);
            },
            item => Assert.Equal("OnSearch", item.Label));
        Assert.DoesNotContain(items, item => item.Label is "OnStatus" or "OnState" or "Reset");
    }

    [Fact]
    public void ComponentEventValueCompletesHandlersMatchingDetailType()
    {
        const string source = """
            <template><Card onItemSelected={On} /></template>
            <script>
            private void OnTyped(CustomEvent<int> e) { }
            private void OnBase(Event e) { }
            private void OnEmpty() { }
            private void OnWrong(CustomEvent<string> e) { }
            </script>
            """;
        var offset = source.IndexOf("{On}", StringComparison.Ordinal) + "{On".Length;
        var context = TemplateCompletionService.GetContext(source, offset, "Editing.sqx");
        var componentEvent = new TemplateComponentEventDescriptor(
            "ItemSelectedEvent",
            "item-selected",
            "int");

        var items = TemplateCompletionService.GetItems(context, source, componentEvent);

        Assert.Contains(items, item => item.Label == "OnTyped");
        Assert.Contains(items, item => item.Label == "OnBase");
        Assert.Contains(items, item => item.Label == "OnEmpty");
        Assert.DoesNotContain(items, item => item.Label == "OnWrong");
        Assert.Equal("onItemSelected", context.AttributeName);
    }

    [Fact]
    public void ClosingTagCompletesNearestOpenElement()
    {
        const string source = "<template><View><Button></";

        var context = TemplateCompletionService.GetContext(source, source.Length, "Editing.sqx");
        var item = Assert.Single(TemplateCompletionService.GetItems(context, source));

        Assert.Equal(TemplateCompletionKind.ClosingTag, context.Kind);
        Assert.Equal("Button", context.TagName);
        Assert.Equal("Button", item.Label);
        Assert.Equal("Button>", item.InsertText);
    }

    [Fact]
    public void ClosingTagBeforeExistingDelimiterDoesNotInsertAnotherDelimiter()
    {
        const string source = "<template><View><Button></Bu>";
        var offset = source.Length - 1;

        var context = TemplateCompletionService.GetContext(source, offset, "Editing.sqx");
        var item = Assert.Single(TemplateCompletionService.GetItems(context, source));

        Assert.Equal(TemplateCompletionKind.ClosingTag, context.Kind);
        Assert.Equal("Button", item.Label);
        Assert.Equal("Button", item.InsertText);
    }

    [Fact]
    public void AttributeCompletionIsTagAwareAndExcludesExistingAttributes()
    {
        const string source = "<template><Button text=\"Save\"  /></template>";
        var offset = source.IndexOf("  />", StringComparison.Ordinal) + 1;

        var context = TemplateCompletionService.GetContext(source, offset, "Editing.sqx");
        var items = TemplateCompletionService.GetItems(context, source);

        Assert.Equal(TemplateCompletionKind.Attribute, context.Kind);
        Assert.Contains(items, item => item.Label == "class");
        Assert.Contains(items, item => item.Label == "disabled");
        Assert.Contains(items, item => item.Label == "onClick" && item.Kind == 23);
        Assert.DoesNotContain(items, item => item.Label == "text");
        Assert.DoesNotContain(items, item => item.Label == "d");
    }

    [Fact]
    public void BuiltInAttributeCompletionMatchesRuntimeWritableProperties()
    {
        const string button = "<template><Button  /></template>";
        const string listItem = "<template><ListItem  /></template>";
        const string menuItem = "<template><MenuItem gr";
        var buttonItems = TemplateCompletionService.GetItems(
            button,
            button.IndexOf("  />", StringComparison.Ordinal) + 1,
            "Editing.sqx");
        var listItemItems = TemplateCompletionService.GetItems(
            listItem,
            listItem.IndexOf("  />", StringComparison.Ordinal) + 1,
            "Editing.sqx");
        var menuItemItems = TemplateCompletionService.GetItems(
            menuItem,
            menuItem.Length,
            "Editing.sqx");

        Assert.Contains(buttonItems, item => item.Label == "disabled");
        Assert.Contains(buttonItems, item => item.Label == "width");
        Assert.DoesNotContain(buttonItems, item => item.Label == "icon");
        Assert.Contains(listItemItems, item => item.Label == "marker");
        Assert.DoesNotContain(listItemItems, item => item.Label == "selected-index");
        Assert.Contains(menuItemItems, item => item.Label == "group");
    }

    [Fact]
    public void UnknownComponentOnlyUsesCommonPropertiesBeforeProjectMetadataIsAdded()
    {
        const string source = "<template><Card  /></template>";
        var offset = source.IndexOf("  />", StringComparison.Ordinal) + 1;

        var items = TemplateCompletionService.GetItems(source, offset, "Editing.sqx");

        Assert.Contains(items, item => item.Label == "class");
        Assert.DoesNotContain(items, item => item.Label is "text" or "value" or "items" or "d");
    }

    [Fact]
    public void BooleanAttributeValueCompletesTrueAndFalse()
    {
        const string source = "<template><Button disabled=\"\" /></template>";
        var offset = source.IndexOf("\"\"", StringComparison.Ordinal) + 1;

        var context = TemplateCompletionService.GetContext(source, offset, "Editing.sqx");
        var items = TemplateCompletionService.GetItems(context, source);

        Assert.Equal(TemplateCompletionKind.AttributeValue, context.Kind);
        Assert.Equal("disabled", context.AttributeName);
        Assert.Collection(
            items.OrderBy(item => item.Label),
            item => Assert.Equal("false", item.Label),
            item => Assert.Equal("true", item.Label));
        Assert.All(items, item => Assert.Equal(12, item.Kind));
    }

    [Fact]
    public void SqxExpressionCompletesCurrentScriptMembers()
    {
        const string source = """
            <template><Text text={} /></template>
            <script>
            private string Title = "Title";
            private string Subtitle { get; set; } = "Sub";
            private string Format() => Title;
            </script>
            """;
        var offset = source.IndexOf("{}", StringComparison.Ordinal) + 1;

        var context = TemplateCompletionService.GetContext(source, offset, "Editing.sqx");
        var items = TemplateCompletionService.GetItems(source, offset, "Editing.sqx");
        var contextItems = TemplateCompletionService.GetItems(context, source);

        Assert.Equal(TemplateCompletionKind.Expression, context.Kind);
        Assert.Contains(items, item => item.Label == "Title" && item.Kind == 5 && item.InsertText == "Title");
        Assert.Contains(items, item => item.Label == "Subtitle" && item.Kind == 10);
        Assert.Contains(items, item => item.Label == "Format" && item.Kind == 2 && item.InsertText == "Format()");
        Assert.Equal(
            items.Select(item => item.Label).OrderBy(label => label),
            contextItems.Select(item => item.Label).OrderBy(label => label));
    }

    [Fact]
    public void ForLambdaItemIsAvailableOnlyInsideItsTemplateSubtree()
    {
        const string source = """
            <template>
              <For each={Items}>{(item)=><Text text={i} />}</For>
              <Text text={i} />
            </template>
            """;
        var firstOffset = source.IndexOf("{i}", StringComparison.Ordinal) + 2;
        var secondOffset = source.LastIndexOf("{i}", StringComparison.Ordinal) + 2;

        var insideItems = TemplateCompletionService.GetItems(source, firstOffset, "Editing.sqx");
        var outsideItems = TemplateCompletionService.GetItems(source, secondOffset, "Editing.sqx");

        Assert.Contains(insideItems, item =>
            item.Label == "item" && item.Kind == 6 && item.Detail == "Template local");
        Assert.DoesNotContain(outsideItems, item => item.Label == "item");
    }

    [Fact]
    public void SqvEventValueCompletesCompatibleScriptMethods()
    {
        const string source = """
            <template><Button @click="OnS" /></template>
            <script>
            private Event? OnState { get; set; }
            private void OnSave(Event e) { }
            </script>
            """;
        var offset = source.IndexOf("OnS\"", StringComparison.Ordinal) + "OnS".Length;

        var context = TemplateCompletionService.GetContext(source, offset, "Editing.sqv");
        var items = TemplateCompletionService.GetItems(source, offset, "Editing.sqv");

        Assert.Equal(TemplateCompletionKind.EventHandler, context.Kind);
        Assert.Contains(items, item => item.Label == "OnSave" && item.Kind == 3 && item.InsertText == "OnSave");
        Assert.DoesNotContain(items, item => item.Label == "OnState");
    }

    [Fact]
    public void SqvModifiedEventCompletionRequiresAnEventParameter()
    {
        const string source = """
            <template><Button @click.stop="On" /></template>
            <script>
            private void OnZero() { }
            private void OnEvent(Event e) { }
            </script>
            """;
        var offset = source.IndexOf("On\"", StringComparison.Ordinal) + "On".Length;

        var items = TemplateCompletionService.GetItems(source, offset, "Editing.sqv");

        Assert.Contains(items, item => item.Label == "OnEvent");
        Assert.DoesNotContain(items, item => item.Label == "OnZero");
    }

    [Fact]
    public void SqvForLocalsAreAvailableOnlyInsideTheLoopSubtree()
    {
        const string source = """
            <template>
              <View>
                <Text v-for="(item, index) in Items">{{ i }}</Text>
                <Text>{{ i }}</Text>
              </View>
            </template>
            """;
        var firstOffset = source.IndexOf("i }}", StringComparison.Ordinal) + 1;
        var secondOffset = source.LastIndexOf("i }}", StringComparison.Ordinal) + 1;

        var insideItems = TemplateCompletionService.GetItems(source, firstOffset, "Editing.sqv");
        var outsideItems = TemplateCompletionService.GetItems(source, secondOffset, "Editing.sqv");

        Assert.Contains(insideItems, item => item.Label == "item" && item.Kind == 6);
        Assert.Contains(insideItems, item => item.Label == "index" && item.Kind == 6);
        Assert.DoesNotContain(outsideItems, item => item.Label is "item" or "index");
    }

    [Fact]
    public void SqvForLocalsAreNotAvailableInTheCollectionExpression()
    {
        const string source = """
            <template><Text v-for="item in I" /></template>
            <script>private string[] Items = [];</script>
            """;
        var offset = source.IndexOf("I\"", StringComparison.Ordinal) + 1;

        var items = TemplateCompletionService.GetItems(source, offset, "Editing.sqv");

        Assert.Contains(items, item => item.Label == "Items");
        Assert.DoesNotContain(items, item => item.Label == "item");
    }

    [Fact]
    public void SqvForLocalsAreAvailableInOtherBindingsOnTheLoopElement()
    {
        const string source = "<template><Text v-for=\"item in Items\" :text=\"it\" /></template>";
        var offset = source.IndexOf("it\"", StringComparison.Ordinal) + 2;

        var items = TemplateCompletionService.GetItems(source, offset, "Editing.sqv");

        Assert.Contains(items, item => item.Label == "item" && item.Kind == 6);
    }

    [Theory]
    [InlineData(":te")]
    [InlineData("v-bind:te")]
    public void SqvBindingPrefixCompletesTagAwareProperties(string binding)
    {
        var source = "<template><Button " + binding;

        var context = TemplateCompletionService.GetContext(source, source.Length, "Editing.sqv");
        var items = TemplateCompletionService.GetItems(context, source);

        Assert.Equal(TemplateCompletionKind.Binding, context.Kind);
        Assert.Equal("te", context.Prefix);
        Assert.Contains(items, item => item.Label == "text" && item.InsertText == "text");
        Assert.DoesNotContain(items, item => item.Label == "d");
    }

    [Theory]
    [InlineData("@cl")]
    [InlineData("v-on:cl")]
    public void SqvEventPrefixesCompleteSupportedEvents(string eventAttribute)
    {
        var source = "<template><Button " + eventAttribute;

        var context = TemplateCompletionService.GetContext(source, source.Length, "Editing.sqv");
        var items = TemplateCompletionService.GetItems(context, source);

        Assert.Equal(TemplateCompletionKind.Event, context.Kind);
        Assert.Equal("cl", context.Prefix);
        Assert.Contains(items, item => item.Label == "click" && item.InsertText == "click");
    }

    [Theory]
    [InlineData("@click.st", "st", "stop")]
    [InlineData("v-on:click.stop.pr", "pr", "prevent")]
    public void SqvEventModifierCompletionOnlyOffersSupportedModifiers(
        string eventAttribute,
        string prefix,
        string expected)
    {
        var source = "<template><Button " + eventAttribute;

        var context = TemplateCompletionService.GetContext(source, source.Length, "Editing.sqv");
        var items = TemplateCompletionService.GetItems(context, source);

        Assert.Equal(TemplateCompletionKind.EventModifier, context.Kind);
        Assert.Equal(prefix, context.Prefix);
        Assert.Contains(items, item => item.Label == expected);
        Assert.DoesNotContain(items, item => item.Label == "once");
    }

    [Fact]
    public void SqvDirectiveCompletionOnlyOffersModelOnSupportedControls()
    {
        const string textSource = "<template><Text v-";
        const string inputSource = "<template><Input v-m";

        var textItems = TemplateCompletionService.GetItems(textSource, textSource.Length, "Editing.sqv");
        var inputItems = TemplateCompletionService.GetItems(inputSource, inputSource.Length, "Editing.sqv");

        Assert.Contains(textItems, item => item.Label == "v-if");
        Assert.Contains(textItems, item => item.Label == "v-bind");
        Assert.DoesNotContain(textItems, item => item.Label == "v-model");
        Assert.Contains(inputItems, item => item.Label == "v-model");
    }

    [Fact]
    public void SqvDirectiveCompletionTreatsModifiedDirectiveAsExisting()
    {
        const string source = "<template><Input v-model.trim  /></template>";
        var offset = source.IndexOf("  />", StringComparison.Ordinal) + 1;

        var items = TemplateCompletionService.GetItems(source, offset, "Editing.sqv");

        Assert.DoesNotContain(items, item => item.Label == "v-model");
    }

    [Theory]
    [InlineData("#de")]
    [InlineData("v-slot:de")]
    public void SqvSlotPrefixCompletesDefaultSlot(string slotAttribute)
    {
        var source = "<template><Card><template " + slotAttribute;

        var context = TemplateCompletionService.GetContext(source, source.Length, "Editing.sqv");
        var item = Assert.Single(TemplateCompletionService.GetItems(context, source));

        Assert.Equal(TemplateCompletionKind.Slot, context.Kind);
        Assert.Equal("de", context.Prefix);
        Assert.Equal("default", item.Label);
        Assert.Equal("default", item.InsertText);
    }

    [Fact]
    public void SqvBlankAttributeCompletionOffersStaticDynamicEventAndDirectiveForms()
    {
        const string source = "<template><Button  /></template>";
        var offset = source.IndexOf("  />", StringComparison.Ordinal) + 1;

        var items = TemplateCompletionService.GetItems(source, offset, "Editing.sqv");

        Assert.Contains(items, item => item.Label == "text");
        Assert.Contains(items, item => item.Label == ":text");
        Assert.Contains(items, item => item.Label == "@click");
        Assert.Contains(items, item => item.Label == "v-if");
        Assert.DoesNotContain(items, item => item.Label == "v-model");
    }

    [Theory]
    [InlineData("v-model.tr", "trim")]
    [InlineData("v-model.trim.nu", "number")]
    public void SqvModelModifierCompletionUsesTheSupportedModifierSet(
        string modelAttribute,
        string expected)
    {
        var source = "<template><Input " + modelAttribute;

        var context = TemplateCompletionService.GetContext(source, source.Length, "Editing.sqv");
        var items = TemplateCompletionService.GetItems(context, source);

        Assert.Equal(TemplateCompletionKind.ModelModifier, context.Kind);
        Assert.Contains(items, item => item.Label == expected);
        Assert.DoesNotContain(items, item => item.Label is "stop" or "prevent");
    }
}
