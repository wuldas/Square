using System.Reflection;
using Square.Controls;
using Square.CSS.Ast;
using Square.CSS.Engine;
using Square.CSS.Tokenizer;
using Square.Graphics;
using Square.Hosting;
using Square.Platform;
using Square.UI;
using Xunit;

namespace Square.UI.Tests;

/// <summary>
/// 真实 DesktopApplication + CssEngine 上的运行时样式编辑行为：独立 inline 归属、
/// author 规则组同步与共用 AST 的跨 engine 隔离、important/删除/清空恢复级联、
/// 等值写回丢弃禁用草稿、整批原子性、CRLF/引号 URL/无效行/范围与失效资源错误。
/// 一律观察真实 computed 值与布局几何，不使用回显替身。
/// </summary>
public sealed class InspectorStyleEditingTests
{
    [Fact]
    public async Task InlineEditsStayIndependentPerOwnerAndChangeRealGeometry()
    {
        var first = new View();
        var second = new View();
        first.Style.Set("width", "100px");
        second.Style.Set("width", "200px");
        var (window, application, _) = CreateApp(content: new[] { first, second });
        try
        {
            InvokeRenderFrame(application);
            var firstSnapshot = await DrainAsync(application,
                window.InspectElementStylesAsync(first.DebugId, includeStyleText: true));
            var secondSnapshot = await DrainAsync(application,
                window.InspectElementStylesAsync(second.DebugId, includeStyleText: true));
            var firstInline = firstSnapshot!.InlineStyle;
            Assert.NotNull(firstInline);
            var secondInline = secondSnapshot!.InlineStyle;
            Assert.NotNull(secondInline);

            Assert.Equal($"square-inline-{first.DebugId}", firstInline!.StyleSheetId);
            Assert.Equal($"square-inline-{second.DebugId}", secondInline!.StyleSheetId);
            Assert.True(firstInline.IsInline);
            Assert.Equal(first.DebugId, firstInline.OwnerDebugId);
            Assert.Equal("100px", first.Style.Get("width"));

            var results = await DrainAsync(application, window.ApplyInspectionStyleEditsAsync(
            [
                new ElementInspectionStyleTextEdit(firstInline.StyleSheetId, firstInline.Range, "width: 240px;"),
            ]));
            var edited = Assert.Single(results);
            Assert.Equal(firstInline.StyleSheetId, edited.StyleSheetId);
            Assert.Equal("width: 240px;", edited.Text);

            // 真实值与真实布局几何改变。
            Assert.Equal("240px", first.Style.Get("width"));
            Assert.Equal("200px", second.Style.Get("width"));
            InvokeRenderFrame(application);
            Assert.Equal(240f, first.Geometry.Width);
            Assert.Equal(200f, second.Geometry.Width);

            // 未编辑 owner 的虚拟资源与真实值完全不受影响。
            var secondAfter = await DrainAsync(application,
                window.InspectElementStylesAsync(second.DebugId, includeStyleText: true));
            var secondInlineAfter = secondAfter!.InlineStyle;
            Assert.NotNull(secondInlineAfter);
            Assert.Equal("width: 200px;", secondInlineAfter!.Text);
        }
        finally
        {
            application.DetachSession();
        }
    }

    [Fact]
    public async Task AuthorGroupEditUpdatesWholeSelectorGroupWithoutTouchingSharedAstEngine()
    {
        // 两个真实窗口的引擎共用同一份解析后 AST：编辑第一个引擎的规则组，
        // 第二个引擎的同源规则完全不变。
        var sharedSheet = new CssParser(new CssTokenizer(".a, .b { color: red; }").Tokenize()).Parse();

        var firstTarget = new View();
        firstTarget.ClassList.Add("a");
        var (firstWindow, firstApp, _) = CreateApp(content: new[] { firstTarget }, styleSheet: sharedSheet);

        var secondTarget = new View();
        secondTarget.ClassList.Add("b");
        var (secondWindow, secondApp, _) = CreateApp(content: new[] { secondTarget }, styleSheet: sharedSheet);

        try
        {
            InvokeRenderFrame(firstApp);
            InvokeRenderFrame(secondApp);
            Assert.Equal("red", firstTarget.Style.Get("color"));
            Assert.Equal("red", secondTarget.Style.Get("color"));

            var sheets = await DrainAsync(firstApp, firstWindow.CaptureInspectionStyleSheetsAsync());
            var group = Assert.Single(sheets, sheet => sheet.Text.StartsWith(".a, .b {", StringComparison.Ordinal));
            Assert.Equal(".a, .b {\ncolor: red;\n}", group.Text);
            Assert.False(group.IsInline);
            Assert.Null(group.OwnerDebugId);
            Assert.Equal("regular", group.Origin);

            var results = await DrainAsync(firstApp, firstWindow.ApplyInspectionStyleEditsAsync(
            [
                new ElementInspectionStyleTextEdit(group.StyleSheetId, group.Range, "color: blue;"),
            ]));
            Assert.Equal(".a, .b {\ncolor: blue;\n}", Assert.Single(results).Text);

            // 第一个引擎的整个选择器组一起生效。
            Assert.Equal("blue", firstTarget.Style.Get("color"));
            // 共用 AST 的第二个 engine 完全不变。
            Assert.Equal("red", secondTarget.Style.Get("color"));
            var secondSheets = await DrainAsync(secondApp, secondWindow.CaptureInspectionStyleSheetsAsync());
            var secondGroup = Assert.Single(secondSheets,
                sheet => sheet.Text.StartsWith(".a, .b {", StringComparison.Ordinal));
            Assert.Equal(".a, .b {\ncolor: red;\n}", secondGroup.Text);

            // matched 快照携带编辑后的完整虚拟文本与真实有效声明。
            var snapshot = await DrainAsync(firstApp,
                firstWindow.InspectElementStylesAsync(firstTarget.DebugId, includeStyleText: true));
            var matched = Assert.Single(snapshot!.MatchedRules!, rule => rule.Selector == ".a");
            Assert.Equal("regular", matched.Origin);
            var matchedStyle = matched.Style;
            Assert.NotNull(matchedStyle);
            Assert.Equal(".a, .b {\ncolor: blue;\n}", matchedStyle!.Text);
            var declaration = Assert.Single(matchedStyle.Declarations);
            Assert.Equal("color", declaration.Property);
            Assert.Equal("blue", declaration.Value);
        }
        finally
        {
            firstApp.DetachSession();
            secondApp.DetachSession();
        }
    }

    [Fact]
    public async Task ImportantDeletionAndEmptyBlockRestoreCascade()
    {
        var target = new View();
        target.ClassList.Add("t");
        target.Style.Set("width", "240px");
        target.Style.Set("color", "red");
        var (window, application, _) = CreateApp(css: ".t { width: 100px; color: blue !important; }", content: new[] { target });
        try
        {
            InvokeRenderFrame(application);
            Assert.Equal("240px", target.Style.Get("width"));
            Assert.Equal("blue", target.Style.Get("color"));

            var snapshot = await DrainAsync(application,
                window.InspectElementStylesAsync(target.DebugId, includeStyleText: true));
            var inline = snapshot!.InlineStyle;
            Assert.NotNull(inline);

            // inline !important 胜过 author !important。
            var important = Assert.Single(await DrainAsync(application, window.ApplyInspectionStyleEditsAsync(
            [
                new ElementInspectionStyleTextEdit(inline!.StyleSheetId, inline.Range,
                    "width: 240px; color: red !important;"),
            ])));
            Assert.Equal("red", target.Style.Get("color"));
            Assert.True(important.Declarations[1].Important);

            // 清空声明块：width/color 回到 author 规则的级联值。
            var emptied = Assert.Single(await DrainAsync(application, window.ApplyInspectionStyleEditsAsync(
            [
                new ElementInspectionStyleTextEdit(inline.StyleSheetId, important.Range, ""),
            ])));
            Assert.Equal("", emptied.Text);
            Assert.Equal(new ElementInspectionStyleRange(0, 0, 0, 0), emptied.Range);
            Assert.Equal("", target.Style.CssText);
            Assert.Equal("100px", target.Style.Get("width"));
            Assert.Equal("blue", target.Style.Get("color"));

            // matched 规则声明仍可编辑并真实生效。
            var ruleSnapshot = await DrainAsync(application,
                window.InspectElementStylesAsync(target.DebugId, includeStyleText: true));
            var matched = Assert.Single(ruleSnapshot!.MatchedRules!, rule => rule.Selector == ".t");
            var ruleStyle = matched.Style;
            Assert.NotNull(ruleStyle);
            Assert.Equal(".t {\nwidth: 100px;\ncolor: blue !important;\n}", ruleStyle!.Text);
            await DrainAsync(application, window.ApplyInspectionStyleEditsAsync(
            [
                new ElementInspectionStyleTextEdit(ruleStyle.StyleSheetId, ruleStyle.Range,
                    "width: 320px;\ncolor: blue !important;"),
            ]));
            Assert.Equal("320px", target.Style.Get("width"));
            Assert.Equal("blue", target.Style.Get("color"));
        }
        finally
        {
            application.DetachSession();
        }
    }

    [Fact]
    public async Task EqualApplicationCssTextReplacementDiscardsDisabledDraft()
    {
        var target = new View();
        target.Style.CssText = "color: red;";
        var (window, application, _) = CreateApp(content: new[] { target });
        try
        {
            InvokeRenderFrame(application);
            var snapshot = await DrainAsync(application,
                window.InspectElementStylesAsync(target.DebugId, includeStyleText: true));
            var inline = snapshot!.InlineStyle;
            Assert.NotNull(inline);
            Assert.Equal("color: red;", inline!.Text);

            // 通过检查器禁用声明：真实值立即消失，草稿保留注释行。
            var disabled = Assert.Single(await DrainAsync(application, window.ApplyInspectionStyleEditsAsync(
            [
                new ElementInspectionStyleTextEdit(inline.StyleSheetId, inline.Range, "/* color: red; */"),
            ])));
            var disabledRow = Assert.Single(disabled.Declarations);
            Assert.True(disabledRow.Disabled);
            Assert.True(disabledRow.ParsedOk);
            // 禁用行的 Text 是完整原始注释文本（含 /* */），Range 对应完整资源切片。
            Assert.Equal("/* color: red; */", disabledRow.Text);
            Assert.StartsWith("/*", disabledRow.Text, StringComparison.Ordinal);
            Assert.Equal("", target.Style.CssText);
            Assert.NotEqual("red", target.Style.Get("color"));

            // 等值的应用显式 CssText 替换也必须丢弃禁用草稿并按 authored 重建。
            target.Style.CssText = "color: red;";
            Assert.Equal("red", target.Style.Get("color"));
            var restored = await DrainAsync(application,
                window.InspectElementStylesAsync(target.DebugId, includeStyleText: true));
            var restoredInline = restored!.InlineStyle;
            Assert.NotNull(restoredInline);
            Assert.Equal("color: red;", restoredInline!.Text);
            var restoredRow = Assert.Single(restoredInline.Declarations);
            Assert.False(restoredRow.Disabled);
            Assert.Equal("red", restoredRow.Value);
        }
        finally
        {
            application.DetachSession();
        }
    }

    [Fact]
    public async Task BatchWithInvalidEditLeavesEveryResourceUntouched()
    {
        var target = new View();
        target.ClassList.Add("t");
        target.Style.Set("width", "10px");
        var (window, application, _) = CreateApp(css: ".t { color: blue; }", content: new[] { target });
        try
        {
            InvokeRenderFrame(application);
            var sheets = await DrainAsync(application, window.CaptureInspectionStyleSheetsAsync());
            var inline = Assert.Single(sheets, sheet => sheet.OwnerDebugId == target.DebugId);
            var rule = Assert.Single(sheets, sheet => sheet.Text.StartsWith(".t {", StringComparison.Ordinal));

            // 第二个 edit 的范围越过声明块：整批都不提交。
            var rangeError = await Assert.ThrowsAsync<ArgumentException>(() =>
                DrainAsync(application, window.ApplyInspectionStyleEditsAsync(
                [
                    new ElementInspectionStyleTextEdit(inline.StyleSheetId, inline.Range, "width: 20px;"),
                    new ElementInspectionStyleTextEdit(rule.StyleSheetId,
                        new ElementInspectionStyleRange(0, 0, 99, 0), "color: red;"),
                ])));
            Assert.Equal("Invalid style edit range.", rangeError.Message);
            Assert.Equal("10px", target.Style.Get("width"));
            Assert.Equal("blue", target.Style.Get("color"));

            // 第二个 edit 指向未知资源：同样整批拒绝。
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                DrainAsync(application, window.ApplyInspectionStyleEditsAsync(
                [
                    new ElementInspectionStyleTextEdit(inline.StyleSheetId, inline.Range, "width: 20px;"),
                    new ElementInspectionStyleTextEdit("square-rule-999",
                        new ElementInspectionStyleRange(0, 0, 0, 0), "color: red;"),
                ])));
            Assert.Equal("10px", target.Style.Get("width"));

            // 随后同一批的有效编辑正常生效，证明连接与资源未被破坏。
            await DrainAsync(application, window.ApplyInspectionStyleEditsAsync(
            [
                new ElementInspectionStyleTextEdit(inline.StyleSheetId, inline.Range, "width: 20px;"),
            ]));
            Assert.Equal("20px", target.Style.Get("width"));
            Assert.Equal("blue", target.Style.Get("color"));
        }
        finally
        {
            application.DetachSession();
        }
    }

    [Fact]
    public async Task CrlfQuotedUrlsInvalidRowsAndRangesPreserveRawText()
    {
        var target = new View();
        target.Style.CssText = "color: red;";
        var (window, application, _) = CreateApp(content: new[] { target });
        try
        {
            InvokeRenderFrame(application);
            var snapshot = await DrainAsync(application,
                window.InspectElementStylesAsync(target.DebugId, includeStyleText: true));
            var inline = snapshot!.InlineStyle;
            Assert.NotNull(inline);

            var edited = Assert.Single(await DrainAsync(application, window.ApplyInspectionStyleEditsAsync(
            [
                new ElementInspectionStyleTextEdit(inline!.StyleSheetId, inline.Range,
                    "background-image: url(\"data:image/png;base64,AA==\");\r\ncolor:\r\n  blue;\r\nwidth: not-a-width;"),
            ])));

            // 引号/分号内的 data URL 不被拆散；CRLF 计一次换行；无效行不应用但保留原文。
            Assert.Equal("blue", target.Style.Get("color"));
            Assert.Null(target.Style.Get("width"));
            Assert.Equal("url(\"data:image/png;base64,AA==\")", target.Style.Get("background-image"));

            Assert.Equal(3, edited.Declarations.Count);
            var background = edited.Declarations[0];
            Assert.Equal("background-image", background.Property);
            Assert.False(background.Disabled);
            Assert.True(background.ParsedOk);
            Assert.Equal(0, background.Range!.Value.StartLine);
            var color = edited.Declarations[1];
            Assert.Equal("color", color.Property);
            Assert.Equal(1, color.Range!.Value.StartLine);
            Assert.Equal(2, color.Range.Value.EndLine);
            // 行 Text 是去除分隔分号的原始切片。
            Assert.Equal("color:\r\n  blue", color.Text);
            var width = edited.Declarations[2];
            Assert.Equal("width", width.Property);
            Assert.False(width.ParsedOk);
            Assert.Equal(3, width.Range!.Value.StartLine);

            var refreshed = await DrainAsync(application,
                window.InspectElementStylesAsync(target.DebugId, includeStyleText: true));
            var refreshedInline = refreshed!.InlineStyle;
            Assert.NotNull(refreshedInline);
            Assert.Equal(edited.Text, refreshedInline!.Text);

            // 切开引号 token 的范围无效。
            var quoteIndex = edited.Text.IndexOf('"');
            Assert.True(quoteIndex >= 0);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                DrainAsync(application, window.ApplyInspectionStyleEditsAsync(
                [
                    new ElementInspectionStyleTextEdit(edited.StyleSheetId,
                        new ElementInspectionStyleRange(0, quoteIndex + 3, 0, quoteIndex + 8), "x"),
                ])));
            Assert.Equal("blue", target.Style.Get("color"));

            // 切开注释 token 的范围同样无效。
            var comment = Assert.Single(await DrainAsync(application, window.ApplyInspectionStyleEditsAsync(
            [
                new ElementInspectionStyleTextEdit(edited.StyleSheetId, refreshedInline.Range,
                    "/* note */ color: blue;"),
            ])));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                DrainAsync(application, window.ApplyInspectionStyleEditsAsync(
                [
                    new ElementInspectionStyleTextEdit(comment.StyleSheetId,
                        new ElementInspectionStyleRange(0, 3, 0, 6), "x"),
                ])));
            Assert.Equal("blue", target.Style.Get("color"));
        }
        finally
        {
            application.DetachSession();
        }
    }

    [Fact]
    public async Task RemovedAndStaleResourcesAreRejectedWithoutResurrection()
    {
        var target = new View();
        target.ClassList.Add("t");
        target.Style.Set("width", "10px");
        var (window, application, root) = CreateApp(css: ".t { color: blue; }", content: new[] { target });
        try
        {
            InvokeRenderFrame(application);
            var sheets = await DrainAsync(application, window.CaptureInspectionStyleSheetsAsync());
            var rule = Assert.Single(sheets, sheet => sheet.Text.StartsWith(".t {", StringComparison.Ordinal));

            // 未知 ID 一律不存在。
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                DrainAsync(application, window.InspectStyleSheetAsync("square-rule-999")));
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                DrainAsync(application, window.InspectStyleSheetAsync($"square-inline-{target.DebugId + 100000}")));
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                DrainAsync(application, window.ApplyInspectionStyleEditsAsync(
                [
                    new ElementInspectionStyleTextEdit("square-inline-424242",
                        new ElementInspectionStyleRange(0, 0, 0, 0), "width: 5px;"),
                ])));
            Assert.Equal("10px", target.Style.Get("width"));

            // 有效编辑仍可用。
            await DrainAsync(application, window.ApplyInspectionStyleEditsAsync(
            [
                new ElementInspectionStyleTextEdit(rule.StyleSheetId, rule.Range, "color: green;"),
            ]));
            Assert.Equal("green", target.Style.Get("color"));

            // scope 注销（engine 不再提供该规则）后旧 ID 立即失效。
            CssStyleReconciler.UnregisterScopesForTree(window.WindowDocument.DocumentElement);
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                DrainAsync(application, window.InspectStyleSheetAsync(rule.StyleSheetId)));
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                DrainAsync(application, window.ApplyInspectionStyleEditsAsync(
                [
                    new ElementInspectionStyleTextEdit(rule.StyleSheetId, rule.Range, "color: red;"),
                ])));
            Assert.Equal("green", target.Style.Get("color"));

            // inline 资源跟随 live 元素；元素移除后旧 ID 失效。
            var inlineSheets = await DrainAsync(application, window.CaptureInspectionStyleSheetsAsync());
            var inline = Assert.Single(inlineSheets, sheet => sheet.OwnerDebugId == target.DebugId);
            root.Children.Remove(target);
            ((UIDocument)window.Document).FlushPendingUpdates();
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                DrainAsync(application, window.InspectStyleSheetAsync(inline.StyleSheetId)));
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                DrainAsync(application, window.ApplyInspectionStyleEditsAsync(
                [
                    new ElementInspectionStyleTextEdit(inline.StyleSheetId, inline.Range, "width: 5px;"),
                ])));
        }
        finally
        {
            application.DetachSession();
        }
    }

    [Fact]
    public async Task PlainReadCarriesNoEditableMetadataAndLazilyCreatesEditor()
    {
        var target = new Button("Inspect me");
        target.ClassList.Add("t");
        target.Style.Set("width", "80px");
        var (window, application, _) = CreateApp(css: ".t { color: blue; }", content: new[] { target });
        try
        {
            InvokeRenderFrame(application);
            var plain = await DrainAsync(application, window.InspectElementStylesAsync(target.DebugId));
            Assert.NotNull(plain);
            Assert.Null(plain!.InlineStyle);
            Assert.Equal("width: 80px;", plain.InlineCssText);

            // UA 规则如实标注为只读来源，author 规则不携带编辑元数据。
            var userAgentRules = plain.MatchedRules!.Where(rule => rule.Origin == "user-agent").ToArray();
            Assert.NotEmpty(userAgentRules);
            Assert.All(userAgentRules, rule => Assert.Null(rule.Style));
            var author = Assert.Single(plain.MatchedRules!, rule => rule.Origin == "regular");
            var authorDeclaration = Assert.Single(author.Declarations);
            Assert.Equal("color", authorDeclaration.Property);
            Assert.Equal("blue", authorDeclaration.Value);
            Assert.Null(author.Style);
            Assert.Null(GetStyleEditorField(application));

            // includeStyleText=true 才惰性创建编辑器元数据。
            var editable = await DrainAsync(application,
                window.InspectElementStylesAsync(target.DebugId, includeStyleText: true));
            Assert.NotNull(editable!.InlineStyle);
            var editableAuthor = Assert.Single(editable.MatchedRules!, rule => rule.Origin == "regular");
            Assert.NotNull(editableAuthor.Style);
            var editableUaRules = editable.MatchedRules!.Where(rule => rule.Origin == "user-agent").ToArray();
            Assert.NotEmpty(editableUaRules);
            Assert.All(editableUaRules, rule => Assert.Null(rule.Style));
            Assert.NotNull(GetStyleEditorField(application));
        }
        finally
        {
            application.DetachSession();
        }
    }

    [Fact]
    public async Task ComputedApiReadsActualValuesWithRequestedPropertiesOnly()
    {
        var first = new View();
        first.ClassList.Add("t");
        first.Style.Set("width", "240px");
        var second = new View();
        second.ClassList.Add("t");
        var (window, application, _) = CreateApp(
            css: ".t { color: blue; background: green; }", content: new[] { first, second });
        try
        {
            InvokeRenderFrame(application);

            // 只读取所给属性；未知/未发布节点省略；返回顺序与输入一致。
            var filtered = await DrainAsync(application,
                window.InspectComputedStylesAsync([first.DebugId, second.DebugId, 987654], ["color"]));
            Assert.Equal(2, filtered.Count);
            Assert.Equal(first.DebugId, filtered[0].Id);
            Assert.Single(filtered[0].Properties);
            Assert.Equal("blue", filtered[0].Properties["color"]);
            Assert.Equal(second.DebugId, filtered[1].Id);
            Assert.Equal("blue", filtered[1].Properties["color"]);

            var full = await DrainAsync(application, window.InspectComputedStylesAsync([first.DebugId]));
            var fullSnapshot = Assert.Single(full);
            Assert.Equal("240px", fullSnapshot.Properties["width"]);
            Assert.Equal("blue", fullSnapshot.Properties["color"]);
            Assert.Equal("green", fullSnapshot.Properties["background"]);

            Assert.Empty(await DrainAsync(application, window.InspectComputedStylesAsync([987654])));
        }
        finally
        {
            application.DetachSession();
        }
    }

    [Fact]
    public async Task SameResourceBatchReturnsEachStageAndCommitsFinalValue()
    {
        var target = new View();
        target.Style.CssText = "width: 10px;";
        var (window, application, _) = CreateApp(content: [target]);
        try
        {
            var snapshot = await DrainAsync(application, window.InspectElementStylesAsync(target.DebugId, true));
            var inline = snapshot!.InlineStyle!;
            const string first = "width: 20px;", final = "width: 30px;";
            var results = await DrainAsync(application, window.ApplyInspectionStyleEditsAsync(
            [
                new(inline.StyleSheetId, inline.Range, first),
                new(inline.StyleSheetId, new(0, 0, 0, first.Length), final)
            ]));
            Assert.Equal([first, final], results.Select(result => result.Text));
            Assert.Equal("30px", target.Style.Get("width"));
        }
        finally { application.DetachSession(); }
    }

    [Fact]
    public async Task WholeRuleBlockCanRepairAnUnterminatedQuotedRemainder()
    {
        var target = new View();
        target.ClassList.Add("t");
        var (window, application, _) = CreateApp(css: ".t { color: red; }", content: [target]);
        try
        {
            var snapshot = await DrainAsync(application, window.InspectElementStylesAsync(target.DebugId, true));
            var rule = snapshot!.MatchedRules!.Single(rule => rule.Selector == ".t").Style!;
            var invalid = Assert.Single(await DrainAsync(application, window.ApplyInspectionStyleEditsAsync(
                [new(rule.StyleSheetId, rule.Range, "color: \"unterminated")])));
            Assert.False(Assert.Single(invalid.Declarations).ParsedOk);
            var repaired = Assert.Single(await DrainAsync(application, window.ApplyInspectionStyleEditsAsync(
                [new(rule.StyleSheetId, invalid.Range, "color: blue;")])));
            Assert.True(Assert.Single(repaired.Declarations).ParsedOk);
            Assert.Equal("blue", target.Style.Get("color"));
        }
        finally { application.DetachSession(); }
    }

    private static object? GetStyleEditorField(DesktopApplication application) =>
        typeof(DesktopApplication).GetField("_inspectionStyleEditor", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(application);

    private static (AppWindow Window, DesktopApplication Application, View Root) CreateApp(
        string? css = null, IReadOnlyList<Element>? content = null, CssStyleSheet? styleSheet = null)
    {
        var window = new AppWindow("Inspector styles");
        var root = new View();
        if (content != null)
            foreach (var child in content)
                root.Children.Add(child);
        window.Load(root);
        if (css != null) ((UIDocument)window.Document).LoadGlobalCssText(css);
        if (styleSheet != null) ((UIDocument)window.Document).GlobalCssEngine.LoadStyleSheet(styleSheet);
        var application = new DesktopApplication(window);
        application.PrepareSession();
        SetPrivateField(application, "_host", new TestHost());
        SetPrivateField(application, "_renderContext", new RecordingRenderContext());
        return (window, application, root);
    }

    private static async Task<T> DrainAsync<T>(DesktopApplication application, Task<T> task)
    {
        application.Dispatcher.Run();
        return await task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static void InvokeRenderFrame(DesktopApplication application) =>
        InvokePrivate(application, "RenderFrame");

    private static void InvokePrivate(DesktopApplication application, string name, params object?[] args)
    {
        var method = typeof(DesktopApplication).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(application, args);
    }

    private static void SetPrivateField<T>(DesktopApplication application, string name, T value)
    {
        var field = typeof(DesktopApplication).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(application, value);
    }

    private sealed class RecordingRenderContext : IRenderContext
    {
        public Size CanvasSize => new(320, 240);
        public float DpiScale => 1;

        public void PushTransform(System.Numerics.Matrix3x2 matrix) { }
        public void PopTransform() { }
        public void PushClip(Rect rect) { }
        public void PushClip(Geometry geometry) { }
        public void PopClip() { }
        public void FillRect(Rect rect, Brush brush) { }
        public void DrawRect(Rect rect, Pen pen) { }
        public void FillPath(PathGeometry path, Brush brush) { }
        public void DrawPath(PathGeometry path, Pen pen) { }
        public void FillGeometry(Geometry geometry, Brush brush) { }
        public void DrawGeometry(Geometry geometry, Pen pen) { }
        public void DrawText(TextLayout text, Point origin, Brush brush) { }
        public void DrawImage(Square.Graphics.Image image, Rect dest, Rect? source = null) { }
        public void PushLayer(Rect bounds, float opacity) { }
        public void PopLayer() { }
        public void Clear(Color color) { }
        public void Clear(Color color, Rect rect) { }
        public void Flush() { }
        public void Present() { }
        public void Present(IReadOnlyList<Rect>? dirtyRects) { }
        public void Dispose() { }
    }

    private sealed class TestHost : IPlatformHost
    {
        public Size ClientSize => new(320, 240);
        public float DpiScale => 1;
        public bool IsRunning => true;
        public string Title { get; set; } = "inspector-style-editing";
        public CursorKind Cursor { get; set; }
        public KeyModifiers Modifiers => KeyModifiers.None;
        public event Action<Size>? SizeChanged { add { } remove { } }
        public event Action<Point, MouseAction, MouseButton>? MouseEvent { add { } remove { } }
        public event Action<WheelInput>? WheelEvent { add { } remove { } }
        public event Action<int, KeyAction>? KeyEvent { add { } remove { } }
        public event Action<string>? TextInput { add { } remove { } }
        public event Action? Tick { add { } remove { } }
        public void Show() { }
        public void Close() { }
        public IRenderContext CreateRenderContext() => throw new NotSupportedException();
        public void PumpEvents() { }
        public void SetTextInputRect(Rect rect) { }
        public string GetClipboardText() => "";
        public void SetClipboardText(string text) { }
        public void Dispose() { }
    }
}
