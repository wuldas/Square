using Square.Controls;
using Square.CSS.Engine;
using Square.Extensions.Markdown;
using Square.Graphics;
using Square.Graphics.Svg;
using Square.Rendering;
using Square.Runtime;
using Xunit;

namespace Square.UI.Tests;

public class MarkdownDocumentTests
{
    [Fact]
    public void ParseCreatesIndependentBlockModel()
    {
        var document = MarkdownDocument.Parse("# Heading\n\nParagraph");

        var heading = Assert.IsType<MarkdownHeading>(document.Blocks[0]);
        var paragraph = Assert.IsType<MarkdownParagraph>(document.Blocks[1]);
        Assert.Equal(1, heading.Level);
        Assert.Equal("Heading", heading.PlainText);
        Assert.Equal("Paragraph", paragraph.PlainText);
        Assert.Equal("Heading\nParagraph", document.PlainText);
    }

    [Fact]
    public void ParsePreservesInlineSemantics()
    {
        var document = MarkdownDocument.Parse(
            "Text **bold** *italic* ~~removed~~ `code` [link](https://example.test \"Example\")");

        var paragraph = Assert.IsType<MarkdownParagraph>(Assert.Single(document.Blocks));
        Assert.Contains(paragraph.Inlines, inline =>
            inline is MarkdownEmphasis { Kind: MarkdownEmphasisKind.Bold, PlainText: "bold" });
        Assert.Contains(paragraph.Inlines, inline =>
            inline is MarkdownEmphasis { Kind: MarkdownEmphasisKind.Italic, PlainText: "italic" });
        Assert.Contains(paragraph.Inlines, inline =>
            inline is MarkdownEmphasis { Kind: MarkdownEmphasisKind.Strikethrough, PlainText: "removed" });
        Assert.Contains(paragraph.Inlines, inline => inline is MarkdownCode { Code: "code" });

        var link = Assert.Single(paragraph.Inlines.OfType<MarkdownLink>());
        Assert.Equal("https://example.test", link.Destination);
        Assert.Equal("Example", link.Title);
        Assert.Equal("link", link.PlainText);
    }

    [Fact]
    public void ParsePreservesNestedListBlocks()
    {
        var document = MarkdownDocument.Parse("3. first\n4. second\n   - nested");

        var list = Assert.IsType<MarkdownList>(Assert.Single(document.Blocks));
        Assert.True(list.IsOrdered);
        Assert.Equal(3, list.Start);
        Assert.Equal(2, list.Items.Count);

        var nested = Assert.IsType<MarkdownList>(list.Items[1].Blocks[1]);
        Assert.False(nested.IsOrdered);
        Assert.Equal("nested", Assert.Single(nested.Items).PlainText);
    }

    [Fact]
    public void ParsePreservesCodeLanguageAndQuoteStructure()
    {
        var document = MarkdownDocument.Parse(
            "> quoted\n\n```csharp title=sample\nvar value = 1;\n```");

        var quote = Assert.IsType<MarkdownQuote>(document.Blocks[0]);
        Assert.IsType<MarkdownParagraph>(Assert.Single(quote.Blocks));

        var code = Assert.IsType<MarkdownCodeBlock>(document.Blocks[1]);
        Assert.Equal("csharp", code.Language);
        Assert.Equal("var value = 1;", code.Code);
    }

    [Fact]
    public void ParsePreservesTaskListState()
    {
        var document = MarkdownDocument.Parse("- [x] complete\n- [ ] pending");

        var list = Assert.IsType<MarkdownList>(Assert.Single(document.Blocks));
        Assert.All(list.Items, item => Assert.True(item.IsTask));
        Assert.True(list.Items[0].IsChecked);
        Assert.False(list.Items[1].IsChecked);
        Assert.Equal("complete", list.Items[0].PlainText);
        Assert.Equal("pending", list.Items[1].PlainText);
    }

    [Fact]
    public void ParsePreservesTableStructureAndAlignment()
    {
        var document = MarkdownDocument.Parse(
            "| Name | Count |\n|:-----|------:|\n| Item | 2 |");

        var table = Assert.IsType<MarkdownTable>(Assert.Single(document.Blocks));
        Assert.Equal([MarkdownTableAlignment.Left, MarkdownTableAlignment.Right], table.Alignments);
        Assert.Equal(2, table.Rows.Count);
        Assert.True(table.Rows[0].IsHeader);
        Assert.False(table.Rows[1].IsHeader);
        Assert.Equal("Name\tCount\nItem\t2", table.PlainText);
    }

    [Fact]
    public void ParsePreservesImageMetadata()
    {
        var document = MarkdownDocument.Parse("![diagram](assets/diagram.png \"Architecture\")");

        var paragraph = Assert.IsType<MarkdownParagraph>(Assert.Single(document.Blocks));
        var image = Assert.IsType<MarkdownImage>(Assert.Single(paragraph.Inlines));
        Assert.Equal("assets/diagram.png", image.Source);
        Assert.Equal("diagram", image.AltText);
        Assert.Equal("Architecture", image.Title);
    }

    [Fact]
    public void ViewerParsesContentBeforeAttachment()
    {
        var viewer = new MarkdownViewer { Content = "# Ready" };

        var heading = Assert.IsType<MarkdownHeading>(Assert.Single(viewer.Document.Blocks));
        Assert.Equal("Ready", heading.PlainText);
    }

    [Fact]
    public void ViewerRendersPreparsedDocumentWithoutReplacingIt()
    {
        var document = new MarkdownDocument([
            new MarkdownParagraph([
                new MarkdownText("Open "),
                new MarkdownLink("https://example.test", null, [new MarkdownText("link")]),
                new MarkdownText(" and "),
                new MarkdownImage("image.png", "preview")]),
            new MarkdownList(false, 1, [
                new MarkdownListItem([
                    new MarkdownParagraph([new MarkdownText("done")])
                ], isTask: true, isChecked: true)
            ]),
            new MarkdownTable(
                [MarkdownTableAlignment.Left, MarkdownTableAlignment.Right],
                [new MarkdownTableRow(true, [
                    new MarkdownTableCell([new MarkdownParagraph([new MarkdownText("Name")])]),
                    new MarkdownTableCell([new MarkdownParagraph([new MarkdownText("Count")])])
                ])])
        ]);
        var viewer = new MarkdownViewer
        {
            Content = "# ignored",
            SourceDocument = document
        };

        viewer.BuildElementTree();
        ((IComponentLifecycle)viewer).OnAttached();

        Assert.Same(document, viewer.Document);
        var link = Assert.Single(viewer.QueryAll<Link>());
        Assert.Equal("https://example.test", link.Href);
        Assert.Equal("link", link.TextContent);
        Assert.Equal("image.png", Assert.Single(viewer.QueryAll<Square.Controls.Image>()).Source);
        var task = Assert.Single(viewer.QueryAll<CheckBox>());
        Assert.True(task.IsChecked);
        Assert.False(task.IsEnabled);
        var table = Assert.Single(viewer.QueryAll<View>(), view => view.ClassList.Contains("markdown-table"));
        var row = Assert.Single(table.Children);
        Assert.True(row.ClassList.Contains("markdown-table-row"));
        Assert.Equal(2, row.Children.Count);
    }

    [Fact]
    public void DynamicMarkdownChildrenReceiveComponentStylesBeforeLayout()
    {
        var window = new Square.Hosting.AppWindow("Markdown styles");
        var viewer = new MarkdownViewer { Content = "# Heading\n\n- Item" };
        window.Load(viewer);
        viewer.BuildElementTree();
        ((IComponentLifecycle)viewer).OnAttached();

        Assert.True(CssStyleReconciler.HasWork);
        CssStyleReconciler.Flush();

        var heading = Assert.Single(
            viewer.QueryAll<View>(),
            view => view.ClassList.Contains("markdown-heading-1"));
        var list = Assert.Single(
            viewer.QueryAll<View>(),
            view => view.ClassList.Contains("markdown-list"));
        Assert.Equal("28px", heading.Style.Get("font-size"));
        Assert.Equal("6px", list.Style.Get("gap"));
        CssStyleReconciler.UnregisterScopesForTree(window.WindowDocument.DocumentElement);
    }

    [Fact]
    public void TableOccupiesLayoutSpaceBeforeFollowingContent()
    {
        var viewer = new MarkdownViewer
        {
            Content = "| Name | Count |\n|:-----|------:|\n| Item | 2 |\n\nAfter table"
        };
        viewer.BuildElementTree();
        ((IComponentLifecycle)viewer).OnAttached();
        var layout = new LayoutEngine();

        layout.Measure(viewer, new Size(600, float.PositiveInfinity));
        layout.Arrange(viewer, new Rect(0, 0, 600, 400));

        var table = Assert.Single(viewer.QueryAll<View>(), view => view.ClassList.Contains("markdown-table"));
        var after = Assert.Single(
            viewer.QueryAll<Square.Controls.Text>(),
            text => text.TextContent == "After table");
        Assert.True(table.Geometry.Height > 0);
        Assert.True(after.Geometry.Top >= table.Geometry.Bottom);
    }

    [Fact]
    public void TableHeaderAndBodyUseTheSameColumnGeometry()
    {
        var viewer = new MarkdownViewer
        {
            Content = "| Short | A much longer heading |\n|:------|----------------------:|\n| A very long body value | 2 |"
        };
        viewer.BuildElementTree();
        ((IComponentLifecycle)viewer).OnAttached();
        var layout = new LayoutEngine();

        layout.Measure(viewer, new Size(600, float.PositiveInfinity));
        layout.Arrange(viewer, new Rect(0, 0, 600, 400));

        var table = Assert.Single(viewer.QueryAll<View>(), view => view.ClassList.Contains("markdown-table"));
        Assert.Equal(2, table.Children.Count);
        var header = table.Children[0];
        var body = table.Children[1];
        Assert.Equal(2, header.Children.Count);
        Assert.Equal(2, body.Children.Count);
        Assert.Equal(header.Children[0].Geometry.X, body.Children[0].Geometry.X);
        Assert.Equal(header.Children[0].Geometry.Width, body.Children[0].Geometry.Width);
        Assert.Equal(header.Children[1].Geometry.X, body.Children[1].Geometry.X);
        Assert.Equal(header.Children[1].Geometry.Width, body.Children[1].Geometry.Width);
        var headerFirstText = Assert.Single(header.Children[0].QueryAll<Square.Controls.Text>());
        var bodyFirstText = Assert.Single(body.Children[0].QueryAll<Square.Controls.Text>());
        var headerSecondText = Assert.Single(header.Children[1].QueryAll<Square.Controls.Text>());
        var bodySecondText = Assert.Single(body.Children[1].QueryAll<Square.Controls.Text>());
        Assert.Equal(headerFirstText.Geometry.X, bodyFirstText.Geometry.X);
        Assert.Equal(headerSecondText.Geometry.X, bodySecondText.Geometry.X);
        Assert.True(header.Children[0].ClassList.Contains("markdown-table-cell"));
        Assert.True(header.Children[0].ClassList.Contains("markdown-table-header"));
    }

    [Fact]
    public void ViewerDecodesInlineSvgDataImage()
    {
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='20' height='10'><rect width='20' height='10' fill='red'/></svg>";
        var source = "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));
        var viewer = new MarkdownViewer { Content = $"![preview]({source})" };
        viewer.BuildElementTree();
        ((IComponentLifecycle)viewer).OnAttached();

        var image = Assert.Single(viewer.QueryAll<Square.Controls.Image>());
        Assert.IsType<SvgImage>(image.ImageContent);
        Assert.Empty(image.Source);
    }

    [Fact]
    public void CodeBackgroundIsRenderedByAContainerInsteadOfText()
    {
        var viewer = new MarkdownViewer
        {
            Content = "Paragraph with `inline` code.\n\n```csharp\nvar value = 1;\n```"
        };
        viewer.BuildElementTree();
        ((IComponentLifecycle)viewer).OnAttached();

        var block = Assert.Single(
            viewer.QueryAll<View>(),
            view => view.ClassList.Contains("markdown-code"));
        var blockText = Assert.Single(block.Children);
        Assert.True(blockText.ClassList.Contains("markdown-code-text"));
        Assert.True(blockText.ClassList.Contains("language-csharp"));
        Assert.Equal("var value = 1;", Assert.IsAssignableFrom<ITextSelectable>(blockText).SelectableText);

        var inline = Assert.Single(
            viewer.QueryAll<View>(),
            view => view.ClassList.Contains("markdown-inline-code"));
        var inlineText = Assert.Single(inline.QueryAll<Square.Controls.Text>());
        Assert.True(inlineText.ClassList.Contains("markdown-inline-code-text"));
        Assert.Equal("inline", inlineText.TextContent);
    }

    [Fact]
    public void CodeAndTableRemainInTheSameSelectableTextTree()
    {
        var viewer = new MarkdownViewer
        {
            Content = "```csharp\nvar value = 1;\n```\n\n| Layer | Responsibility |\n|:------|:---------------|\n| Parser | Markdown to document |"
        };
        viewer.BuildElementTree();
        ((IComponentLifecycle)viewer).OnAttached();
        var layout = new LayoutEngine();
        layout.Measure(viewer, new Size(600, float.PositiveInfinity));
        layout.Arrange(viewer, new Rect(0, 0, 600, 400));
        var tree = new DisplayTree();
        tree.BuildFrom(viewer);

        var fragments = tree.CollectTextFragments(viewer);

        var codeFragments = fragments
            .Where(fragment => fragment.Element.ClassList.Contains("markdown-code-text"))
            .ToArray();
        Assert.Equal("var value = 1;", string.Concat(codeFragments.Select(fragment => fragment.Text)));
        Assert.Contains(fragments, fragment => fragment.Text == "Layer");
        Assert.Contains(fragments, fragment => fragment.Text == "Responsibility");
        Assert.Contains(fragments, fragment => fragment.Text == "Parser");
        Assert.All(fragments, fragment => Assert.True(fragment.Element.IsUserSelectText()));
    }

    [Fact]
    public void HighlightedCodeFragmentsMergeIntoContinuousSelectableText()
    {
        var viewer = new MarkdownViewer
        {
            Content = "```csharp\nvar viewer = new MarkdownViewer { Content = \"# Hello\" };\n```",
        };
        viewer.BuildElementTree();
        ((IComponentLifecycle)viewer).OnAttached();
        var layout = new LayoutEngine();
        layout.Measure(viewer, new Size(700, float.PositiveInfinity));
        layout.Arrange(viewer, new Rect(0, 0, 700, 200));
        var tree = new DisplayTree();
        tree.BuildFrom(viewer);

        var codeElement = Assert.Single(
            viewer.QueryAll<UIElement>(),
            element => element.ClassList.Contains("markdown-code-text"));
        var selectable = Assert.IsAssignableFrom<ITextSelectable>(codeElement);
        var fragments = tree.CollectTextFragments(viewer)
            .Where(fragment => ReferenceEquals(fragment.Element, codeElement))
            .ToArray();
        var merged = Square.Hosting.DesktopApplication.MergeSelectableTextFragments(
            codeElement,
            selectable.SelectableText,
            fragments);

        Assert.Equal(selectable.SelectableText, merged.Text);
        Assert.Equal(0, merged.Characters[0].StartOffset);
        Assert.Equal(3, merged.Characters[2].EndOffset);
        Assert.Equal(0, merged.HitTestOffset(new Point(merged.Bounds.Left, merged.Bounds.Top + 2)));
    }

    [Fact]
    public void HighlightedCodeExposesDomTextForCrossBlockCopy()
    {
        var viewer = new MarkdownViewer
        {
            Content = "Before\n\n```csharp\nvar value = 1;\n```\n\nAfter",
        };
        var document = new UIDocument();
        document.Body.AppendChild(viewer);
        viewer.BuildElementTree();
        ((IComponentLifecycle)viewer).OnAttached();

        var codeElement = Assert.Single(
            viewer.QueryAll<UIElement>(),
            element => element.ClassList.Contains("markdown-code-text"));
        var codeNode = Assert.Single(codeElement.ChildNodes.OfType<Square.UI.Text>());
        Assert.Equal("var value = 1;", codeNode.Data);

        var textNodes = EnumerateTextNodes(viewer).ToArray();
        var before = Assert.Single(textNodes, node => node.Data == "Before");
        var after = Assert.Single(textNodes, node => node.Data == "After");
        var range = document.CreateRange();
        range.SetStart(before, 0);
        range.SetEnd(after, after.Length);

        Assert.Equal("Before\nvar value = 1;\nAfter", range.ToString());

        static IEnumerable<Square.UI.Text> EnumerateTextNodes(Node node)
        {
            if (node is not Element element) yield break;
            foreach (var child in element.ChildNodes)
            {
                if (child is Square.UI.Text text) yield return text;
                foreach (var descendant in EnumerateTextNodes(child)) yield return descendant;
            }
        }
    }

    [Fact]
    public void ScrolledCodeSelectionUsesTheVisibleOverflowClip()
    {
        var scroll = new ScrollViewer { Geometry = new Rect(0, 100, 600, 300) };
        scroll.SetScrollContentSize(new Size(600, 900));
        scroll.ScrollTo(0, 240);
        var code = new View { Geometry = new Rect(20, 360, 560, 60) };
        code.Style.Set("overflow", "hidden");
        var text = new Square.Controls.Text("var value = 1;")
        {
            Geometry = new Rect(36, 376, 160, 20)
        };
        scroll.Children.Add(code);
        code.Children.Add(text);

        var clipMethod = typeof(Square.Hosting.DesktopApplication).GetMethod(
            "GetTextSelectionClip",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var clip = Assert.IsType<Rect>(clipMethod.Invoke(null, [text]));

        Assert.Equal(new Rect(20, 120, 560, 60), clip);
        Assert.True(clip.Contains(new Point(36, 136)));
    }

    [Fact]
    public void ViewerFallsBackToContentWhenSourceDocumentIsCleared()
    {
        var viewer = new MarkdownViewer
        {
            Content = "# Content",
            SourceDocument = new MarkdownDocument([
                new MarkdownParagraph([new MarkdownText("Document")])
            ])
        };

        Assert.Equal("Document", viewer.Document.PlainText);

        viewer.SourceDocument = null;

        Assert.Equal("Content", viewer.Document.PlainText);
    }

    [Fact]
    public void EmptyInputReturnsEmptyDocument()
    {
        Assert.Empty(MarkdownDocument.Parse(null).Blocks);
        Assert.Empty(MarkdownDocument.Parse("  ").Blocks);
    }

    [Fact]
    public void MarkdownRegistrationRegistersViewerIndependently()
    {
        MarkdownRegistration.RegisterDefaults();

        var create = typeof(ElementRegistry).GetMethod(
            "Create",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Assert.IsType<MarkdownViewer>(create.Invoke(null, ["MarkdownViewer"]));
    }
}
