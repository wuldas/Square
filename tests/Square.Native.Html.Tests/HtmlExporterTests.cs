using Square.Controls;
using Square.Graphics;
using Square.Native.Html;
using Square.UI;
using Square.UI.Svg;
using Xunit;
using SquareImage = Square.Controls.Image;
using SquareText = Square.Controls.Text;

namespace Square.Native.Html.Tests;

public sealed class HtmlExporterTests
{
    [Fact]
    public void ExportMapsCoreControlsStylesAndFormState()
    {
        var root = new View { Id = "root" };
        root.ClassList.Add("page");
        root.Style.Set("display", "flex");
        root.Style.Set("gap", "12px");
        root.Children.Add(new SquareText("Hello <Square>"));
        root.Children.Add(new Button("Save") { IsDisabled = true });
        root.Children.Add(new Input { Type = "password", Value = "a&b", Placeholder = "Password" });
        root.Children.Add(new TextArea { Value = "Line <one>", Placeholder = "Notes" });
        root.Children.Add(new CheckBox { TextContent = "Remember", IsChecked = true });
        root.Children.Add(new Radio { TextContent = "Pro", GroupName = "plan", IsChecked = true });
        root.Children.Add(new Select { Value = "Pro", Options = ["Free", "Pro"] });

        var result = HtmlExporter.Export(root, new HtmlExportOptions { Title = "Export <test>" });

        Assert.Contains("<!doctype html>", result.Html);
        Assert.Contains("<title>Export &lt;test&gt;</title>", result.Html);
        Assert.Contains("id=\"root\"", result.Html);
        Assert.Contains("class=\"page square-root sq-style-", result.Html);
        Assert.Contains("display:flex;gap:12px;", result.Css);
        Assert.Contains("<style data-square-css=\"true\">", result.Html);
        Assert.DoesNotContain("style=\"", result.Html);
        Assert.Contains("Hello &lt;Square&gt;", result.Html);
        Assert.Contains("<button disabled>Save</button>", result.Html);
        Assert.Contains("type=\"password\"", result.Html);
        Assert.Contains("value=\"a&amp;b\"", result.Html);
        Assert.Contains("<textarea placeholder=\"Notes\">Line &lt;one&gt;</textarea>", result.Html);
        Assert.Contains("type=\"checkbox\" checked", result.Html);
        Assert.Contains("type=\"radio\" name=\"plan\" checked", result.Html);
        Assert.Contains("<option value=\"Pro\" selected>Pro</option>", result.Html);
        Assert.DoesNotContain("<script", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ExportDeduplicatesComputedStylesIntoHeadStylesheet()
    {
        var root = new View();
        var first = new View();
        var second = new View();
        first.Style.Set("padding", "8px");
        second.Style.Set("padding", "8px");
        root.Children.Add(first);
        root.Children.Add(second);

        var result = HtmlExporter.Export(root, new HtmlExportOptions { IncludeDocument = false });

        Assert.DoesNotContain("style=\"", result.Html);
        var classes = result.Html.Split("class=\"", StringSplitOptions.None)
            .Skip(1)
            .Select(value => value.Split('"')[0])
            .Where(value => value.Contains("sq-style-", StringComparison.Ordinal))
            .ToArray();
        Assert.True(classes.Length >= 2);
        Assert.Equal(classes[0], classes[1]);
        Assert.Equal(1, CountOccurrences(result.Css, "padding:8px;"));
    }

    [Fact]
    public void ExportCanOptIntoLegacyInlineStyles()
    {
        var root = new View();
        root.Style.Set("display", "grid");

        var result = HtmlExporter.Export(root, new HtmlExportOptions
        {
            IncludeDocument = false,
            UseInlineStyles = true,
            IncludeBaselineCss = false
        });

        Assert.Contains("style=\"display:grid;\"", result.Html);
        Assert.DoesNotContain("style data-square-css", result.Html);
        Assert.DoesNotContain("display:grid;", result.Css);
    }

    [Fact]
    public void ExportCanReferenceGeneratedExternalStylesheet()
    {
        var root = new View();
        root.Style.Set("display", "flex");

        var result = HtmlExporter.Export(root, new HtmlExportOptions
        {
            StylesheetHref = "/assets/square.generated.css"
        });

        Assert.Contains("<link rel=\"stylesheet\" href=\"/assets/square.generated.css\">", result.Html);
        Assert.DoesNotContain("<style data-square-css=\"true\">", result.Html);
        Assert.Contains("display:flex;", result.Css);
    }

    [Fact]
    public void ExportRejectsUnsafeExternalStylesheetAndKeepsInlineFallback()
    {
        var root = new View();
        root.Style.Set("display", "grid");

        var result = HtmlExporter.Export(root, new HtmlExportOptions
        {
            StylesheetHref = "javascript:alert(1)"
        });

        Assert.Contains("<style data-square-css=\"true\">", result.Html);
        Assert.DoesNotContain("javascript:", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("display:grid;", result.Css);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("stylesheet URL", StringComparison.Ordinal));
    }

    [Fact]
    public void ExportRejectsUnsafeLinkAndMarksUnsupportedControls()
    {
        var root = new View();
        root.Children.Add(new Link("Bad", "javascript:alert(1)"));
        root.Children.Add(new Canvas());

        var result = HtmlExporter.Export(root, new HtmlExportOptions { IncludeDocument = false });

        Assert.Contains("<a>Bad</a>", result.Html);
        Assert.DoesNotContain("javascript:", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-square-kind=\"Canvas\" data-square-unsupported=\"true\"", result.Html);
        Assert.Equal(2, result.Diagnostics.Count);
    }

    [Fact]
    public void ExportSerializesInlineSvgAndBitmapImage()
    {
        var root = new View();
        var svg = new SVGSVGElement { ViewBox = "0 0 24 24" };
        var circle = new SVGCircleElement();
        circle.SetProperty("CenterX", 12);
        circle.SetProperty("CenterY", 12);
        circle.SetProperty("Radius", 10);
        circle.SetProperty("Fill", "#ff0000");
        svg.Children.Add(circle);
        root.Children.Add(svg);

        var bitmap = new Bitmap(1, 1);
        bitmap.SetPixels([0, 0, 255, 255]);
        root.Children.Add(new SquareImage { ImageContent = bitmap });

        var result = HtmlExporter.Export(root, new HtmlExportOptions { IncludeDocument = false });

        Assert.Contains("<svg viewBox=\"0 0 24 24\">", result.Html);
        Assert.Contains("<circle cx=\"12\" cy=\"12\" r=\"10\" fill=\"#ff0000\"></circle>", result.Html);
        Assert.Contains("src=\"data:image/png;base64,", result.Html);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ExportBuildsGeneratedStyleLikeComponentsOnlyOnce()
    {
        var component = new TestComponent();

        var first = HtmlExporter.Export(component, new HtmlExportOptions { IncludeDocument = false });
        var second = HtmlExporter.Export(component, new HtmlExportOptions { IncludeDocument = false });

        Assert.Equal(1, component.BuildCount);
        Assert.Equal(first.Html, second.Html);
        Assert.Contains("data-square-component=", first.Html);
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0; index += needle.Length)
            count++;
        return count;
    }

    private sealed class TestComponent : UIElement
    {
        private bool _built;
        public int BuildCount { get; private set; }

        public override void BuildElementTree()
        {
            if (_built) return;
            _built = true;
            BuildCount++;
            Children.Add(new SquareText("Generated"));
            Style.Set("display", "flex");
        }
    }
}
