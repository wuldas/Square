using System;
using Square.Markup.Ast;
using Square.Markup.Parser;
using Square.Markup;
using Xunit;

namespace Square.Markup.Tests;

public class SqxParserTests
{
    [Fact]
    public void ParsesSlotsWhileLegacyRouterTagsRemainOrdinaryElements()
    {
        const string source = "<template><Router initialPath=\"/\"><Route path=\"/\" component={Shell}><Route path=\":id\" component={Page} /></Route></Router><Slot name=\"header\" /><Outlet /></template>";

        var document = new SqxParser().Parse(source, "Composition.sqx");

        var router = Assert.IsType<SqxElement>(document.Template.Roots[0]);
        Assert.Equal(SqxNodeKind.Element, router.Kind);
        var route = Assert.IsType<SqxElement>(Assert.Single(router.Children));
        Assert.Equal(SqxNodeKind.Element, route.Kind);
        Assert.Equal(SqxNodeKind.Element, Assert.IsType<SqxElement>(Assert.Single(route.Children)).Kind);
        Assert.Equal(SqxNodeKind.Slot, Assert.IsType<SqxElement>(document.Template.Roots[1]).Kind);
        Assert.Equal(SqxNodeKind.Slot, Assert.IsType<SqxElement>(document.Template.Roots[2]).Kind);
    }

    [Fact]
    public void ParsesForLambdaTemplateAsChildElement()
    {
        const string source = "<template><For each={Items}>{(it)=><Text text={it} />}</For></template>";

        var document = new SqxParser().Parse(source, "List.sqx");
        var loop = Assert.IsType<SqxElement>(Assert.Single(document.Template.Roots));

        Assert.Equal(SqxNodeKind.For, loop.Kind);
        Assert.Contains(loop.Children, child => child is SqxElement element && element.TagName == "Text");
    }
    [Fact]
    public void ParseSimpleElement()
    {
        var parser = new SqxParser();
        var doc = parser.Parse("<template><View></View></template>", "Test.sqx");
        Assert.Single(doc.Template.Roots);
        var el = Assert.IsType<SqxElement>(doc.Template.Roots[0]);
        Assert.Equal("View", el.TagName);
    }

    [Fact]
    public void ParseNestedElements()
    {
        var parser = new SqxParser();
        var doc = parser.Parse("<template><View><Text>Hi</Text></View></template>", "Test.sqx");
        Assert.Single(doc.Template.Roots);
        var view = Assert.IsType<SqxElement>(doc.Template.Roots[0]);
        Assert.Single(view.Children);
        var text = Assert.IsType<SqxElement>(view.Children[0]);
        Assert.Equal("Text", text.TagName);
    }

    [Fact]
    public void ParseAttributes()
    {
        var parser = new SqxParser();
        var doc = parser.Parse("<template><Button ref={MyBtn} onClick={OnClick}>Click</Button></template>", "Test.sqx");
        var btn = Assert.IsType<SqxElement>(doc.Template.Roots[0]);
        Assert.Equal(2, btn.Attributes.Count);
        Assert.Equal("ref", btn.Attributes[0].Name);
        Assert.Equal("MyBtn", btn.Attributes[0].Value?.Content);
        Assert.True(btn.Attributes[0].Value?.IsExpression);
    }

    [Fact]
    public void ParseStringAttribute()
    {
        var parser = new SqxParser();
        var doc = parser.Parse("<template><Text text=\"Hello\">Hi</Text></template>", "Test.sqx");
        var text = Assert.IsType<SqxElement>(doc.Template.Roots[0]);
        Assert.Equal("Hello", text.Attributes[0].Value?.Content);
        Assert.False(text.Attributes[0].Value?.IsExpression);
    }

    [Fact]
    public void ParseScript()
    {
        var parser = new SqxParser();
        var doc = parser.Parse("<template><View/></template><script lang=\"csharp\">int x = 1;</script>", "Test.sqx");
        Assert.NotNull(doc.Script);
        Assert.Equal("csharp", doc.Script.Language);
        Assert.Contains("int x = 1;", doc.Script.Code);
    }

    [Fact]
    public void ParsesScriptComponentMetadata()
    {
        const string source = "<template><View /></template><script lang=\"csharp\" namespace=\"App.Pages\" name=\"HomePage\" access=\"internal\"></script>";

        var script = Assert.IsType<SqxScript>(new SqxParser().Parse(source, "View.sqx").Script);

        Assert.Equal("App.Pages", script.Namespace);
        Assert.Equal("HomePage", script.ComponentName);
        Assert.Equal("internal", script.Access);
    }

    [Fact]
    public void RejectsUnsupportedScriptLanguage()
    {
        var error = Assert.Throws<SqxParseException>(() =>
            new SqxParser().Parse("<template><View /></template><script lang=\"javascript\"></script>", "Test.sqx"));

        Assert.Contains("language", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsUnknownScriptMetadata()
    {
        var error = Assert.Throws<SqxParseException>(() =>
            new SqxParser().Parse("<template><View /></template><script scoped=\"true\"></script>", "Test.sqx"));

        Assert.Contains("scoped", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseStyle()
    {
        var parser = new SqxParser();
        var doc = parser.Parse("<template><View/></template><style>View { color: red; }</style>", "Test.sqx");
        Assert.NotNull(doc.Style);
        Assert.Contains("color: red", doc.Style.Css);
    }

    [Fact]
    public void ScriptAndStyleLocationsComeFromSectionRanges()
    {
        const string source = """
            <template>
              <View />
            </template>
            <script lang="csharp">
            int x;
            </script>
            <style>
            View { color: red; }
            </style>
            """;

        var document = new SqxParser().Parse(source, "Locations.sqx");

        Assert.Equal(2, Assert.IsType<SqxElement>(Assert.Single(document.Template.Roots)).Line);
        Assert.Equal(4, Assert.IsType<SqxScript>(document.Script).Line);
        Assert.True(document.Script.Column > 1);
        Assert.Equal(7, Assert.IsType<SqxStyle>(document.Style).Line);
        Assert.True(document.Style.Column > 1);
    }

    [Fact]
    public void ParseSelfClosing()
    {
        var parser = new SqxParser();
        var doc = parser.Parse("<template><View /><Text /></template>", "Test.sqx");
        Assert.Equal(2, doc.Template.Roots.Count);
    }

    [Fact]
    public void ParseShowPrimitive()
    {
        var parser = new SqxParser();
        var doc = parser.Parse("<template><Show when={Visible}><Text>Hi</Text></Show></template>", "Test.sqx");
        var show = Assert.IsType<SqxElement>(doc.Template.Roots[0]);
        Assert.Equal(SqxNodeKind.Show, show.Kind);
    }

    [Fact]
    public void ParseForPrimitive()
    {
        var parser = new SqxParser();
        var doc = parser.Parse("<template><For each={Items}><Text>Item</Text></For></template>", "Test.sqx");
        var forNode = Assert.IsType<SqxElement>(doc.Template.Roots[0]);
        Assert.Equal(SqxNodeKind.For, forNode.Kind);
    }

    [Fact]
    public void ParseExpressionInterpolation()
    {
        var parser = new SqxParser();
        var doc = parser.Parse("<template><Text>{Name}</Text></template>", "Test.sqx");
        var text = Assert.IsType<SqxElement>(doc.Template.Roots[0]);
        Assert.Single(text.Children);
        var expr = Assert.IsType<SqxExpression>(text.Children[0]);
        Assert.Equal("Name", expr.Expression);
    }

    [Fact]
    public void RejectsMissingTemplateSection()
    {
        var error = Assert.Throws<SqxParseException>(() =>
            new SqxParser().Parse("<script lang=\"csharp\">int x;</script>", "Missing.sqx"));

        Assert.Contains("template", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsDuplicateScriptSectionAtSecondTag()
    {
        const string source = "<template><View /></template>\n<script lang=\"csharp\"></script>\n<script lang=\"csharp\"></script>";

        var error = Assert.Throws<SqxParseException>(() => new SqxParser().Parse(source, "Duplicate.sqx"));

        Assert.Equal(3, error.Line);
        Assert.Equal(1, error.Column);
    }

    [Fact]
    public void RejectsLegacySqxDocumentRoot()
    {
        var error = Assert.Throws<SqxParseException>(() =>
            new SqxParser().Parse("<sqx><template><View /></template></sqx>", "Legacy.sqx"));

        Assert.Equal(1, error.Line);
        Assert.Equal(1, error.Column);
    }

    [Fact]
    public void RejectsNonWhitespaceOutsideSections()
    {
        var error = Assert.Throws<SqxParseException>(() =>
            new SqxParser().Parse("hello\n<template><View /></template>", "Outside.sqx"));

        Assert.Equal(1, error.Line);
        Assert.Equal(1, error.Column);
    }

    [Fact]
    public void RejectsInvalidDirectiveUsingCompilerDiagnosticContract()
    {
        var error = Assert.Throws<SqxParseException>(() =>
            new SqxParser().Parse("<template><Show><Text /></Show></template>", "InvalidShow.sqx"));

        Assert.Equal("SQXD002", error.DiagnosticId);
        Assert.Contains("when", error.DiagnosticMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(error.Offset >= 0);
    }

    [Fact]
    public void ParsesTemplateCommentsWithoutAstNodes()
    {
        const string source = "<template><!-- before --><View><!-- child --><Text>ok</Text></View></template>";

        var document = new SqxParser().Parse(source, "Comments.sqx");

        var view = Assert.IsType<SqxElement>(Assert.Single(document.Template.Roots));
        Assert.Equal("View", view.TagName);
        Assert.IsType<SqxElement>(Assert.Single(view.Children));
    }

    [Fact]
    public void PreservesLineNumbersAfterMultilineTemplateComment()
    {
        const string source = "<template>\n<!-- first\nsecond -->\n<View />\n</template>";

        var view = Assert.IsType<SqxElement>(Assert.Single(new SqxParser().Parse(source, "Comments.sqx").Template.Roots));

        Assert.Equal(4, view.Line);
    }

    [Theory]
    [InlineData("<template><!-- broken<View /></template>")]
    [InlineData("<template><Text text=\"broken /></template>")]
    [InlineData("<template><Text text={Name /></template>")]
    [InlineData("<template><View></></View></template>")]
    [InlineData("<template><View></ View></template>")]
    [InlineData("<template><View></View extra></template>")]
    [InlineData("<template><View></View</template>")]
    [InlineData("<template><View></View/></template>")]
    [InlineData("<template></View></template>")]
    [InlineData("<template><View></Text></template>")]
    public void RejectsMalformedTemplateSyntax(string source)
    {
        Assert.Throws<SqxParseException>(() => new SqxParser().Parse(source, "Malformed.sqx"));
    }
}
