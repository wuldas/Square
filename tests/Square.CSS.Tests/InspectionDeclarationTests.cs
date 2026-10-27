using Square.Controls;
using Square.UI.ElementApi;
using Xunit;

namespace Square.CSS.Tests;

public sealed class InspectionDeclarationTests
{
    [Fact]
    public void RemovingExpandedLonghandDoesNotReviveItAfterCssTextRoundTrip()
    {
        var element = new View();
        element.Style.CssText = "margin: 10px;";
        element.Style.RemoveProperty("margin-left");
        Assert.Null(element.Style.Get("margin-left"));
        var authored = element.Style.CssText;
        element.Style.CssText = authored;
        Assert.Null(element.Style.Get("margin-left"));
        Assert.Equal("10px", element.Style.Get("margin-top"));
        Assert.Equal("10px", element.Style.Get("margin-right"));
        Assert.Equal("10px", element.Style.Get("margin-bottom"));
    }


    [Fact]
    public void DisabledMarginRowClearsAllFourSides()
    {
        var entries = CssDeclarationText.Parse("/* margin: 20px; */");
        var entry = Assert.Single(entries);
        Assert.True(entry.Disabled);
        Assert.Equal("margin", entry.Property);
        Assert.Equal("20px", entry.Value);

        var element = new View();
        element.Style.CssText = "margin: 10px;";
        element.Style.SetInspectionDeclarations(entries);
        Assert.Null(element.Style.Get("margin-top"));
        Assert.Null(element.Style.Get("margin-right"));
        Assert.Null(element.Style.Get("margin-bottom"));
        Assert.Null(element.Style.Get("margin-left"));
        Assert.Equal("", element.Style.CssText);
    }

    [Fact]
    public void ExplicitLonghandSetRetiresParentShorthand()
    {
        var element = new View();
        element.Style.CssText = "margin: 10px;";
        element.Style.SetProperty("margin-left", "20px");
        Assert.Equal("20px", element.Style.Get("margin-left"));
        Assert.Equal("10px", element.Style.Get("margin-top"));
        Assert.Equal("10px", element.Style.Get("margin-right"));
        Assert.Equal("10px", element.Style.Get("margin-bottom"));
        var authored = element.Style.CssText;
        element.Style.CssText = authored;
        Assert.Equal("20px", element.Style.Get("margin-left"));
        Assert.Equal("10px", element.Style.Get("margin-top"));
        Assert.Equal("10px", element.Style.Get("margin-right"));
        Assert.Equal("10px", element.Style.Get("margin-bottom"));
    }

    [Fact]
    public void BorderSideEditKeepsOtherBorderValuesEffective()
    {
        var element = new View();
        element.Style.CssText = "border: 1px solid red;";
        element.Style.SetProperty("border-top-width", "5px");
        Assert.Equal("5px", element.Style.Get("border-top-width"));
        Assert.Equal("solid", element.Style.Get("border-top-style"));
        Assert.Equal("red", element.Style.Get("border-top-color"));
        Assert.Equal("1px", element.Style.Get("border-right-width"));
        Assert.Equal("red", element.Style.Get("border-bottom-color"));
    }

    [Fact]
    public void RemovingShorthandClearsAllExpandedSides()
    {
        var element = new View();
        element.Style.CssText = "margin: 10px;";
        element.Style.RemoveProperty("margin");
        Assert.Null(element.Style.Get("margin-top"));
        Assert.Null(element.Style.Get("margin-right"));
        Assert.Null(element.Style.Get("margin-bottom"));
        Assert.Null(element.Style.Get("margin-left"));
        Assert.Equal("", element.Style.CssText);
        Assert.Equal(0, element.Style.Length);
    }

    [Fact]
    public void SameValueLonghandSetStillPromotesExpansionToAuthored()
    {
        var element = new View();
        element.Style.CssText = "margin: 10px;";
        var before = element.Style.InlineStyleRevision;
        element.Style.SetProperty("margin-left", "10px");
        Assert.Equal(before + 1, element.Style.InlineStyleRevision);
        Assert.Equal("10px", element.Style.Get("margin-left"));
        var authored = element.Style.CssText;
        element.Style.CssText = authored;
        Assert.Equal("10px", element.Style.Get("margin-left"));
        Assert.Equal("10px", element.Style.Get("margin-top"));
        Assert.Equal("10px", element.Style.Get("margin-right"));
        Assert.Equal("10px", element.Style.Get("margin-bottom"));

        // 同值且同源的重复显式写入走未变化快路径：不再递增 revision。
        var stable = element.Style.InlineStyleRevision;
        element.Style.SetProperty("margin-left", "10px");
        Assert.Equal(stable, element.Style.InlineStyleRevision);
    }

    [Fact]
    public void RevisionIncrementsOncePerOuterNotificationIncludingEqualReplacement()
    {
        var element = new View();
        var before = element.Style.InlineStyleRevision;
        element.Style.CssText = "width: 10px; height: 20px; color: red;";
        Assert.Equal(before + 1, element.Style.InlineStyleRevision);

        // application 等值整体替换：revision 递增一次，有效值不变（无多余布局变化源）。
        var afterFirst = element.Style.InlineStyleRevision;
        element.Style.CssText = "width: 10px; height: 20px; color: red;";
        Assert.Equal(afterFirst + 1, element.Style.InlineStyleRevision);
        Assert.Equal("10px", element.Style.Get("width"));
        Assert.Equal("20px", element.Style.Get("height"));
        Assert.Equal("red", element.Style.GetPropertyValue("color"));

        // inspection 提交路径同样是外层提交：整批一次。
        var afterSecond = element.Style.InlineStyleRevision;
        element.Style.SetInspectionDeclarations(CssDeclarationText.Parse("width: 30px; height: 40px;"));
        Assert.Equal(afterSecond + 1, element.Style.InlineStyleRevision);
        Assert.Equal("30px", element.Style.Get("width"));
        Assert.Equal("40px", element.Style.Get("height"));
    }

    [Fact]
    public void BlockImportantDuplicateBeatsLaterNormalDeclaration()
    {
        var element = new View();
        element.Style.CssText = "color: red !important; color: blue;";
        Assert.Equal("red", element.Style.GetPropertyValue("color"));
        Assert.Equal("important", element.Style.GetPropertyPriority("color"));

        // 检查器提交路径遵循同一折叠规则。
        var committed = new View();
        committed.Style.SetInspectionDeclarations(
            CssDeclarationText.Parse("color: red !important; color: blue;"));
        Assert.Equal("red", committed.Style.GetPropertyValue("color"));
        Assert.Equal("important", committed.Style.GetPropertyPriority("color"));

        // 禁用胜出行后露出剩余级联。
        committed.Style.SetInspectionDeclarations(
            CssDeclarationText.Parse("/* color: red !important; */ color: blue;"));
        Assert.Equal("blue", committed.Style.GetPropertyValue("color"));
        Assert.Equal("", committed.Style.GetPropertyPriority("color"));

        // 显式 CSSOM 写入是替换语义：可以覆盖 important 声明。
        element.Style.SetProperty("color", "green");
        Assert.Equal("green", element.Style.GetPropertyValue("color"));
        Assert.Equal("", element.Style.GetPropertyPriority("color"));
    }

    [Fact]
    public void ParsedRowsKeepTruthfulRangesTextAndFlags()
    {
        const string text = "color: red;\r\n width: bogus;\n content: \"a; b\" ;/* margin: 20px; */ trailing";
        var entries = CssDeclarationText.Parse(text);
        Assert.Equal(5, entries.Count);

        var color = entries[0];
        Assert.Equal((0, 10), (color.StartOffset, color.EndOffset));
        Assert.Equal("color: red", color.Text);
        Assert.Equal("color", color.Property);
        Assert.Equal("red", color.Value);
        Assert.False(color.Important);
        Assert.False(color.Disabled);
        Assert.True(color.ParsedOk);

        var width = entries[1];
        Assert.Equal((14, 26), (width.StartOffset, width.EndOffset));
        Assert.Equal("width: bogus", width.Text);
        Assert.Equal("width", width.Property);
        Assert.Equal("bogus", width.Value);
        Assert.False(width.ParsedOk);

        var content = entries[2];
        Assert.Equal((29, 44), (content.StartOffset, content.EndOffset));
        Assert.Equal("content: \"a; b\"", content.Text);
        Assert.Equal("content", content.Property);
        Assert.Equal("\"a; b\"", content.Value);
        Assert.True(content.ParsedOk);

        var margin = entries[3];
        Assert.Equal((46, 65), (margin.StartOffset, margin.EndOffset));
        Assert.Equal(text[margin.StartOffset..margin.EndOffset], margin.Text);
        Assert.Equal("margin", margin.Property);
        Assert.Equal("20px", margin.Value);
        Assert.True(margin.Disabled);
        Assert.True(margin.ParsedOk);

        var trailing = entries[4];
        Assert.Equal((66, 74), (trailing.StartOffset, trailing.EndOffset));
        Assert.Equal("trailing", trailing.Text);
        Assert.Equal("", trailing.Property);
        Assert.False(trailing.ParsedOk);

        // disabled/invalid 行保留但不应用；有效行正常生效。
        var element = new View();
        element.Style.SetInspectionDeclarations(entries);
        Assert.Equal("red", element.Style.GetPropertyValue("color"));
        Assert.Equal("\"a; b\"", element.Style.GetPropertyValue("content"));
        Assert.Null(element.Style.Get("width"));
        Assert.Null(element.Style.Get("margin-top"));
        Assert.Null(element.Style.Get("margin-left"));
    }

    [Fact]
    public void OrdinaryCommentsAreNotPropertiesAndValueCommentsKeepPriority()
    {
        const string text = "/* ordinary */ color: /* value */ red ! /* priority */ important; color: blue;";
        var rows = CssDeclarationText.Parse(text);
        Assert.Equal(2, rows.Count);
        var element = new View();
        element.Style.SetInspectionDeclarations(rows);
        Assert.Equal("red", element.Style.Get("color"));
        Assert.Equal("important", element.Style.GetPropertyPriority("color"));
    }

    [Fact]
    public void RowsKeepOffsetsAcrossCrlfAndUnicode()
    {
        var entries = CssDeclarationText.Parse("width:\r\n10px");
        var wrapped = Assert.Single(entries);
        Assert.Equal((0, 12), (wrapped.StartOffset, wrapped.EndOffset));
        Assert.Equal("width:\r\n10px", wrapped.Text);
        Assert.True(wrapped.ParsedOk);
        Assert.Equal("10px", wrapped.Value);

        var unicode = CssDeclarationText.Parse("width: 12px; content: \"日本語\"");
        Assert.Equal(2, unicode.Count);
        Assert.Equal((0, 11), (unicode[0].StartOffset, unicode[0].EndOffset));
        Assert.Equal((13, 27), (unicode[1].StartOffset, unicode[1].EndOffset));
        Assert.Equal("content: \"日本語\"", unicode[1].Text);
        Assert.True(unicode[1].ParsedOk);

        var element = new View();
        element.Style.SetInspectionDeclarations(unicode);
        Assert.Equal("12px", element.Style.Get("width"));
        Assert.Equal("\"日本語\"", element.Style.GetPropertyValue("content"));
    }

    [Fact]
    public void UnterminatedQuoteRemainderIsParsedOkFalseAndRepairable()
    {
        var entries = CssDeclarationText.Parse("color: red; content: \"open");
        Assert.Equal(2, entries.Count);
        Assert.True(entries[0].ParsedOk);
        var remainder = entries[1];
        Assert.False(remainder.ParsedOk);
        Assert.Empty(remainder.Assignments);
        Assert.Equal("content", remainder.Property);
        Assert.Equal((12, 26), (remainder.StartOffset, remainder.EndOffset));
        Assert.EndsWith("open", remainder.Text, StringComparison.Ordinal);

        // 普通 CssText setter 不抛出；剩余段不应用，随后整块替换可修复。
        var element = new View();
        element.Style.CssText = "color: red; content: \"open";
        Assert.Equal("red", element.Style.GetPropertyValue("color"));
        element.Style.CssText = "color: blue;";
        Assert.Equal("blue", element.Style.GetPropertyValue("color"));
        Assert.Equal("", element.Style.GetPropertyValue("content"));
    }

    [Fact]
    public void UnterminatedCommentRemainderIsParsedOkFalse()
    {
        var entries = CssDeclarationText.Parse("width: 8px; /* color: red");
        Assert.Equal(2, entries.Count);
        Assert.True(entries[0].ParsedOk);
        Assert.False(entries[1].ParsedOk);
        Assert.Equal("/* color: red", entries[1].Text);
        Assert.Equal((12, 25), (entries[1].StartOffset, entries[1].EndOffset));

        var element = new View();
        element.Style.CssText = "width: 8px; /* color: red";
        Assert.Equal("8px", element.Style.Get("width"));
        Assert.Equal("", element.Style.GetPropertyValue("color"));
    }

    [Fact]
    public void ImportantAndPropertyTrimmingAreParsedTruthfully()
    {
        var entries = CssDeclarationText.Parse("color: red !IMPORTANT; width : 10px !important");
        Assert.Equal(2, entries.Count);
        Assert.True(entries[0].Important);
        Assert.Equal("red", entries[0].Value);
        Assert.True(entries[0].ParsedOk);
        Assert.Equal("width", entries[1].Property);
        Assert.Equal("10px", entries[1].Value);
        Assert.True(entries[1].Important);

        var element = new View();
        element.Style.SetInspectionDeclarations(entries);
        Assert.Equal("important", element.Style.GetPropertyPriority("color"));
        Assert.Equal("important", element.Style.GetPropertyPriority("width"));

        var empty = CssDeclarationText.Parse("color: !important");
        var entry = Assert.Single(empty);
        Assert.Equal("color", entry.Property);
        Assert.Equal("", entry.Value);
        Assert.False(entry.Important);
        Assert.False(entry.ParsedOk);
    }

    [Fact]
    public void IsEditBoundaryMarksStringAndCommentInteriors()
    {
        const string text = "color: /* c; omment */ red; content: \"a;b\"";
        Assert.True(CssDeclarationText.IsEditBoundary(text, 7));   // 注释起点
        Assert.False(CssDeclarationText.IsEditBoundary(text, 8)); // /* 之间
        Assert.False(CssDeclarationText.IsEditBoundary(text, 11)); // 注释内分号
        Assert.False(CssDeclarationText.IsEditBoundary(text, 21));
        Assert.True(CssDeclarationText.IsEditBoundary(text, 22));  // */ 之后
        Assert.True(CssDeclarationText.IsEditBoundary(text, 37));  // 开引号边界
        Assert.False(CssDeclarationText.IsEditBoundary(text, 38));
        Assert.False(CssDeclarationText.IsEditBoundary(text, 39)); // 引号内分号
        Assert.False(CssDeclarationText.IsEditBoundary(text, 40));
        Assert.False(CssDeclarationText.IsEditBoundary(text, 41)); // 闭引号
        Assert.True(CssDeclarationText.IsEditBoundary(text, 42));
        Assert.False(CssDeclarationText.IsEditBoundary(text, -1));
        Assert.True(CssDeclarationText.IsEditBoundary(text, text.Length));

        // 转义引号不终止字符串。
        const string escaped = "content: \"a\\\"b\"";
        Assert.True(CssDeclarationText.IsEditBoundary(escaped, 9));
        Assert.False(CssDeclarationText.IsEditBoundary(escaped, 10));
        Assert.False(CssDeclarationText.IsEditBoundary(escaped, 12)); // \" 位置
        Assert.False(CssDeclarationText.IsEditBoundary(escaped, 14)); // 闭引号
        Assert.True(CssDeclarationText.IsEditBoundary(escaped, 15));

        // 未闭合 token 延伸到文本末尾。
        Assert.False(CssDeclarationText.IsEditBoundary("content: \"open", 12));
        Assert.True(CssDeclarationText.IsEditBoundary("content: \"open", 9));
        Assert.False(CssDeclarationText.IsEditBoundary("width: 8px /* o", 14));
    }
}
