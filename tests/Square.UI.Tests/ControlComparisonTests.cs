using Square.FontComparison;
using Square.Controls;
using Square.CSS.Engine;
using Square.Graphics;
using Square.Rendering;
using Square.UI;
using Xunit;

namespace Square.UI.Tests;

public sealed class ControlComparisonTests
{
    [Theory]
    [InlineData(ControlAppearance.Auto, ControlState.Normal)]
    [InlineData(ControlAppearance.Auto, ControlState.Hover)]
    [InlineData(ControlAppearance.Auto, ControlState.Active)]
    [InlineData(ControlAppearance.Auto, ControlState.Focus)]
    [InlineData(ControlAppearance.Auto, ControlState.Disabled)]
    [InlineData(ControlAppearance.None, ControlState.Normal)]
    [InlineData(ControlAppearance.None, ControlState.Hover)]
    [InlineData(ControlAppearance.None, ControlState.Active)]
    [InlineData(ControlAppearance.None, ControlState.Focus)]
    [InlineData(ControlAppearance.None, ControlState.Disabled)]
    public void ButtonGeometryMatchesChromiumAcrossAppearancesAndStates(
        ControlAppearance appearance,
        ControlState state)
    {
        var root = new View
        {
            Style =
            {
                CssText = "display: flex; align-items: center; justify-content: center; " +
                    "box-sizing: border-box; width: 320px; height: 160px;"
            }
        };
        var button = new Button("Control");
        var item = Assert.Single(ControlComparisonManifest.CreateDefault().ExpandCases(), candidate =>
            candidate.Kind == ControlKind.Button && candidate.Appearance == appearance && candidate.State == state);
        button.Style.CssText = item.AuthorCss;
        ApplyState(button, state);
        root.Children.Add(button);

        new CssEngine().ApplyStylesToTree(root);
        new LayoutEngine().MeasureAndArrange(root, new Size(320, 160));

        var expected = appearance == ControlAppearance.Auto
            ? new Rect(130.5f, 69.5f, 58.984375f, 21)
            : new Rect(70, 62, 180, 36);
        AssertClose(expected.X, button.Geometry.X - root.Geometry.X);
        AssertClose(expected.Y, button.Geometry.Y - root.Geometry.Y);
        AssertClose(expected.Width, button.Geometry.Width);
        AssertClose(expected.Height, button.Geometry.Height);
    }

    [Theory]
    [InlineData(ControlAppearance.Auto, ControlState.Normal)]
    [InlineData(ControlAppearance.Auto, ControlState.Hover)]
    [InlineData(ControlAppearance.Auto, ControlState.Focus)]
    [InlineData(ControlAppearance.Auto, ControlState.Disabled)]
    [InlineData(ControlAppearance.Auto, ControlState.Value)]
    [InlineData(ControlAppearance.Auto, ControlState.Placeholder)]
    [InlineData(ControlAppearance.None, ControlState.Normal)]
    [InlineData(ControlAppearance.None, ControlState.Hover)]
    [InlineData(ControlAppearance.None, ControlState.Focus)]
    [InlineData(ControlAppearance.None, ControlState.Disabled)]
    [InlineData(ControlAppearance.None, ControlState.Value)]
    [InlineData(ControlAppearance.None, ControlState.Placeholder)]
    public void InputGeometryMatchesChromiumAcrossAppearancesAndStates(
        ControlAppearance appearance,
        ControlState state)
    {
        var root = new View
        {
            Style =
            {
                CssText = "display: flex; align-items: center; justify-content: center; " +
                    "box-sizing: border-box; width: 320px; height: 160px;"
            }
        };
        var item = Assert.Single(ControlComparisonManifest.CreateDefault().ExpandCases(), candidate =>
            candidate.Kind == ControlKind.Input && candidate.Appearance == appearance && candidate.State == state);
        var input = new Input
        {
            Value = state == ControlState.Placeholder ? "" : item.Value,
            Placeholder = item.Placeholder
        };
        input.Style.CssText = item.AuthorCss;
        ApplyState(input, state);
        root.Children.Add(input);

        new CssEngine().ApplyStylesToTree(root);
        new LayoutEngine().MeasureAndArrange(root, new Size(320, 160));

        var expected = appearance == ControlAppearance.Auto
            ? new Rect(71.5f, 69.5f, 177, 21)
            : new Rect(70, 62, 180, 36);
        AssertClose(expected.X, input.Geometry.X - root.Geometry.X);
        AssertClose(expected.Y, input.Geometry.Y - root.Geometry.Y);
        AssertClose(expected.Width, input.Geometry.Width);
        AssertClose(expected.Height, input.Geometry.Height);
        Assert.Equal(appearance == ControlAppearance.Auto ? "1px" : "6px", input.Style.Get("padding-top"));
        Assert.Equal(appearance == ControlAppearance.Auto ? "2px" : "10px", input.Style.Get("padding-left"));
        Assert.Equal("2px", input.Style.Get("border-left-width"));
    }

    [Theory]
    [InlineData(ControlAppearance.Auto, ControlState.Normal)]
    [InlineData(ControlAppearance.Auto, ControlState.Hover)]
    [InlineData(ControlAppearance.Auto, ControlState.Focus)]
    [InlineData(ControlAppearance.Auto, ControlState.Disabled)]
    [InlineData(ControlAppearance.Auto, ControlState.Value)]
    [InlineData(ControlAppearance.Auto, ControlState.Placeholder)]
    [InlineData(ControlAppearance.None, ControlState.Normal)]
    [InlineData(ControlAppearance.None, ControlState.Hover)]
    [InlineData(ControlAppearance.None, ControlState.Focus)]
    [InlineData(ControlAppearance.None, ControlState.Disabled)]
    [InlineData(ControlAppearance.None, ControlState.Value)]
    [InlineData(ControlAppearance.None, ControlState.Placeholder)]
    public void TextAreaGeometryMatchesChromiumAcrossAppearancesAndStates(
        ControlAppearance appearance,
        ControlState state)
    {
        var root = new View
        {
            Style =
            {
                CssText = "display: flex; align-items: center; justify-content: center; " +
                    "box-sizing: border-box; width: 320px; height: 160px;"
            }
        };
        var item = Assert.Single(ControlComparisonManifest.CreateDefault().ExpandCases(), candidate =>
            candidate.Kind == ControlKind.TextArea && candidate.Appearance == appearance && candidate.State == state);
        var textArea = new TextArea
        {
            Value = state == ControlState.Placeholder ? "" : item.Value,
            Placeholder = item.Placeholder
        };
        textArea.Style.CssText = item.AuthorCss;
        ApplyState(textArea, state);
        root.Children.Add(textArea);

        new CssEngine().ApplyStylesToTree(root);
        new LayoutEngine().MeasureAndArrange(root, new Size(320, 160));

        var expected = appearance == ControlAppearance.Auto
            ? new Rect(79.5f, 62, 161, 36)
            : new Rect(70, 62, 180, 36);
        AssertClose(expected.X, textArea.Geometry.X - root.Geometry.X);
        AssertClose(expected.Y, textArea.Geometry.Y - root.Geometry.Y);
        AssertClose(expected.Width, textArea.Geometry.Width);
        AssertClose(expected.Height, textArea.Geometry.Height);
        Assert.Equal(appearance == ControlAppearance.Auto ? "2px" : "6px", textArea.Style.Get("padding-top"));
        Assert.Equal(appearance == ControlAppearance.Auto ? "2px" : "10px", textArea.Style.Get("padding-left"));
        Assert.Equal(appearance == ControlAppearance.Auto ? "1px" : "2px", textArea.Style.Get("border-left-width"));
    }

    [Theory]
    [InlineData(ControlAppearance.Auto, ControlState.Normal)]
    [InlineData(ControlAppearance.Auto, ControlState.Hover)]
    [InlineData(ControlAppearance.Auto, ControlState.Focus)]
    [InlineData(ControlAppearance.Auto, ControlState.Disabled)]
    [InlineData(ControlAppearance.None, ControlState.Normal)]
    [InlineData(ControlAppearance.None, ControlState.Hover)]
    [InlineData(ControlAppearance.None, ControlState.Focus)]
    [InlineData(ControlAppearance.None, ControlState.Disabled)]
    public void SelectGeometryMatchesChromiumAcrossAppearancesAndStates(
        ControlAppearance appearance,
        ControlState state)
    {
        var root = new View
        {
            Style =
            {
                CssText = "display: flex; align-items: center; justify-content: center; " +
                    "box-sizing: border-box; width: 320px; height: 160px;"
            }
        };
        var item = Assert.Single(ControlComparisonManifest.CreateDefault().ExpandCases(), candidate =>
            candidate.Kind == ControlKind.Select && candidate.Appearance == appearance && candidate.State == state);
        var select = new Select { Options = [item.Value], Value = item.Value };
        select.Style.CssText = item.AuthorCss;
        ApplyState(select, state);
        root.Children.Add(select);

        new CssEngine().ApplyStylesToTree(root);
        new LayoutEngine().MeasureAndArrange(root, new Size(320, 160));

        var expected = appearance == ControlAppearance.Auto
            ? new Rect(132, 70.5f, 56, 19)
            : new Rect(70, 62, 180, 36);
        AssertClose(expected.X, select.Geometry.X - root.Geometry.X);
        AssertClose(expected.Y, select.Geometry.Y - root.Geometry.Y);
        AssertClose(expected.Width, select.Geometry.Width);
        AssertClose(expected.Height, select.Geometry.Height);
        Assert.Equal(appearance == ControlAppearance.Auto ? "0" : "6px", select.Style.Get("padding-top") ?? "0");
        Assert.Equal(appearance == ControlAppearance.Auto ? "0" : "10px", select.Style.Get("padding-left") ?? "0");
        Assert.Equal(appearance == ControlAppearance.Auto ? "1px" : "2px", select.Style.Get("border-left-width"));
    }

    [Theory]
    [InlineData(ControlAppearance.Auto, ControlState.Unchecked)]
    [InlineData(ControlAppearance.Auto, ControlState.Checked)]
    [InlineData(ControlAppearance.Auto, ControlState.Hover)]
    [InlineData(ControlAppearance.Auto, ControlState.Active)]
    [InlineData(ControlAppearance.Auto, ControlState.Focus)]
    [InlineData(ControlAppearance.Auto, ControlState.Disabled)]
    [InlineData(ControlAppearance.None, ControlState.Unchecked)]
    [InlineData(ControlAppearance.None, ControlState.Checked)]
    [InlineData(ControlAppearance.None, ControlState.Hover)]
    [InlineData(ControlAppearance.None, ControlState.Active)]
    [InlineData(ControlAppearance.None, ControlState.Focus)]
    [InlineData(ControlAppearance.None, ControlState.Disabled)]
    public void CheckBoxGeometryMatchesChromiumAcrossAppearancesAndStates(
        ControlAppearance appearance,
        ControlState state)
    {
        var root = new View
        {
            Style =
            {
                CssText = "display: flex; align-items: center; justify-content: center; " +
                    "box-sizing: border-box; width: 320px; height: 160px;"
            }
        };
        var item = Assert.Single(ControlComparisonManifest.CreateDefault().ExpandCases(), candidate =>
            candidate.Kind == ControlKind.CheckBox && candidate.Appearance == appearance && candidate.State == state);
        var checkBox = new CheckBox { TextContent = "", IsChecked = state == ControlState.Checked };
        checkBox.Style.CssText = item.AuthorCss;
        ApplyState(checkBox, state);
        root.Children.Add(checkBox);

        new CssEngine().ApplyStylesToTree(root);
        new LayoutEngine().MeasureAndArrange(root, new Size(320, 160));

        var expected = appearance == ControlAppearance.Auto
            ? new Rect(154, 73.5f, 13, 13)
            : new Rect(70, 62, 180, 36);
        AssertClose(expected.X, checkBox.Geometry.X - root.Geometry.X);
        AssertClose(expected.Y, checkBox.Geometry.Y - root.Geometry.Y);
        AssertClose(expected.Width, checkBox.Geometry.Width);
        AssertClose(expected.Height, checkBox.Geometry.Height);
        Assert.Equal(appearance == ControlAppearance.Auto ? "0" : "6px", checkBox.Style.Get("padding-top") ?? "0");
        Assert.Equal(appearance == ControlAppearance.Auto ? "0" : "10px", checkBox.Style.Get("padding-left") ?? "0");
        Assert.Equal(appearance == ControlAppearance.Auto ? "0" : "2px", checkBox.Style.Get("border-left-width") ?? "0");
    }

    [Theory]
    [InlineData(ControlAppearance.Auto, ControlState.Unchecked)]
    [InlineData(ControlAppearance.Auto, ControlState.Checked)]
    [InlineData(ControlAppearance.Auto, ControlState.Hover)]
    [InlineData(ControlAppearance.Auto, ControlState.Active)]
    [InlineData(ControlAppearance.Auto, ControlState.Focus)]
    [InlineData(ControlAppearance.Auto, ControlState.Disabled)]
    [InlineData(ControlAppearance.None, ControlState.Unchecked)]
    [InlineData(ControlAppearance.None, ControlState.Checked)]
    [InlineData(ControlAppearance.None, ControlState.Hover)]
    [InlineData(ControlAppearance.None, ControlState.Active)]
    [InlineData(ControlAppearance.None, ControlState.Focus)]
    [InlineData(ControlAppearance.None, ControlState.Disabled)]
    public void RadioGeometryMatchesChromiumAcrossAppearancesAndStates(
        ControlAppearance appearance,
        ControlState state)
    {
        var root = new View
        {
            Style =
            {
                CssText = "display: flex; align-items: center; justify-content: center; " +
                    "box-sizing: border-box; width: 320px; height: 160px;"
            }
        };
        var item = Assert.Single(ControlComparisonManifest.CreateDefault().ExpandCases(), candidate =>
            candidate.Kind == ControlKind.Radio && candidate.Appearance == appearance && candidate.State == state);
        var radio = new Radio { TextContent = "", IsChecked = state == ControlState.Checked };
        radio.Style.CssText = item.AuthorCss;
        ApplyState(radio, state);
        root.Children.Add(radio);

        new CssEngine().ApplyStylesToTree(root);
        new LayoutEngine().MeasureAndArrange(root, new Size(320, 160));

        var expected = appearance == ControlAppearance.Auto
            ? new Rect(154.5f, 75, 13, 13)
            : new Rect(70, 62, 180, 36);
        AssertClose(expected.X, radio.Geometry.X - root.Geometry.X);
        AssertClose(expected.Y, radio.Geometry.Y - root.Geometry.Y);
        AssertClose(expected.Width, radio.Geometry.Width);
        AssertClose(expected.Height, radio.Geometry.Height);
        Assert.Equal(appearance == ControlAppearance.Auto ? "0" : "6px", radio.Style.Get("padding-top") ?? "0");
        Assert.Equal(appearance == ControlAppearance.Auto ? "0" : "10px", radio.Style.Get("padding-left") ?? "0");
        Assert.Equal(appearance == ControlAppearance.Auto ? "0" : "2px", radio.Style.Get("border-left-width") ?? "0");
    }

    [Fact]
    public void ManifestExpandsSemanticControlsAppearancesAndRelevantStates()
    {
        var manifest = ControlComparisonManifest.CreateDefault();
        var cases = manifest.ExpandCases();

        Assert.Equal(
            [ControlKind.Button, ControlKind.Input, ControlKind.TextArea, ControlKind.Select, ControlKind.CheckBox, ControlKind.Radio],
            manifest.Controls.Select(control => control.Kind));
        Assert.All(manifest.Controls, control =>
            Assert.Equal([ControlAppearance.Auto, ControlAppearance.None], control.Appearances));
        Assert.Contains(cases, item => item.Kind == ControlKind.Button && item.State == ControlState.Active);
        Assert.Contains(cases, item => item.Kind == ControlKind.Input && item.State == ControlState.Placeholder);
        Assert.Contains(cases, item => item.Kind == ControlKind.CheckBox && item.State == ControlState.Checked);
        Assert.DoesNotContain(cases, item => item.Kind == ControlKind.Select && item.State == ControlState.Open);
    }

    [Fact]
    public void AppearanceNoneUsesOneAuthorCssPayloadForBothRenderers()
    {
        var manifest = ControlComparisonManifest.CreateSmoke();
        var item = Assert.Single(manifest.ExpandCases(), item =>
            item.Kind == ControlKind.Input && item.Appearance == ControlAppearance.None);

        Assert.NotEmpty(item.AuthorCss);
        Assert.Contains("margin: 0", item.AuthorCss);
        Assert.Equal(manifest.AppearanceNoneAuthorCss, item.AuthorCss);
        Assert.Equal(item.ChromiumAuthorCss, item.SquareAuthorCss);
        Assert.Equal(item.AuthorCss, item.ChromiumAuthorCss);
    }

    [Fact]
    public void ArtifactPathsAreDeterministicAndRendererNamesAreCanonical()
    {
        var first = ControlArtifactPaths.For("artifacts/control-comparison", "Software", "input-none-normal");
        var second = ControlArtifactPaths.For("artifacts/control-comparison", "software", "input-none-normal");

        Assert.Equal(first, second);
        Assert.EndsWith("software/cases/input-none-normal.png", first.Screenshot.Replace('\\', '/'));
        Assert.EndsWith("software/geometry.json", first.Metrics.Replace('\\', '/'));
    }

    [Fact]
    public void VisualPhaseRefusesAnyFailedGeometryCase()
    {
        var reports = new[]
        {
            new ControlGeometryReport
            {
                Renderer = "Software",
                ManifestFingerprint = "manifest-a",
                Cases =
                [
                    new ControlGeometryCaseResult { Id = "button-auto-normal", Passed = true },
                    new ControlGeometryCaseResult { Id = "input-auto-normal", Passed = false }
                ]
            }
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ControlGeometryGate.EnsureVisualAllowed(
                reports,
                ["button-auto-normal", "input-auto-normal"],
                "manifest-a"));

        Assert.Contains("input-auto-normal", exception.Message);
    }

    [Fact]
    public void GeometryComparisonUsesHalfPixelToleranceWithoutReadingScreenshots()
    {
        var chromium = Geometry("Chromium", 10, 20, 100, 36, "missing-chrome.png");
        var square = Geometry("Software", 10.5f, 19.51f, 100.49f, 36.5f, "missing-square.png");

        var report = ControlGeometryComparer.Compare(chromium, square, 0.5f);

        Assert.True(Assert.Single(report.Cases).Passed);
    }

    [Fact]
    public void GeometryComparisonRejectsMissingSquareCases()
    {
        var chromium = Geometry("Chromium", 10, 20, 100, 36, "chrome.png");
        var square = new ControlGeometryReport
        {
            Renderer = "Software",
            ManifestFingerprint = "manifest-a",
            Cases = []
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ControlGeometryComparer.Compare(chromium, square));

        Assert.Contains("button-auto-normal", exception.Message);
    }

    [Fact]
    public void VisualPhaseRejectsAnEmptyGeometryReport()
    {
        var reports = new[] { new ControlGeometryReport { Renderer = "Software", Cases = [] } };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ControlGeometryGate.EnsureVisualAllowed(reports, ["button-auto-normal"], "manifest-a"));

        Assert.Contains("Software", exception.Message);
    }

    [Fact]
    public void VisualPhaseRejectsPartialPassingGeometrySet()
    {
        var report = Geometry("Software", 10, 20, 100, 36, "square.png");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ControlGeometryGate.EnsureVisualAllowed(
                [report],
                ["button-auto-normal", "input-auto-normal"],
                "manifest-a"));

        Assert.Contains("input-auto-normal", exception.Message);
    }

    [Fact]
    public void VisualPhaseRejectsStaleManifestFingerprint()
    {
        var report = Geometry("Software", 10, 20, 100, 36, "square.png");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ControlGeometryGate.EnsureVisualAllowed(
                [report],
                ["button-auto-normal"],
                "manifest-b"));

        Assert.Contains("fingerprint", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrowserPayloadUsesJavascriptPropertyNamesAndPreservesAuthorCss()
    {
        var item = Assert.Single(ControlComparisonManifest.CreateSmoke().ExpandCases(), item =>
            item.Kind == ControlKind.Button && item.Appearance == ControlAppearance.None);

        var payload = ControlBrowserCaseConfig.Create(item);

        Assert.Equal("button", payload.element);
        Assert.Equal("Button", payload.kind);
        Assert.Equal("Control", payload.text);
        Assert.Equal(item.AuthorCss, payload.authorCss);
    }

    private static ControlGeometryReport Geometry(
        string renderer, float x, float y, float width, float height, string screenshot) => new()
        {
            Renderer = renderer,
            ManifestFingerprint = "manifest-a",
            Cases =
            [
                new ControlGeometryCaseResult
                {
                    Id = "button-auto-normal",
                    Passed = true,
                    BorderBox = new ControlRect(x, y, width, height),
                    ContentBox = new ControlRect(x, y, width, height),
                    Screenshot = screenshot
                }
            ]
        };

    private static void ApplyState(UIElement control, ControlState state)
    {
        if (state == ControlState.Disabled) control.IsDisabled = true;
        if (state == ControlState.Hover) control.SetState(ElementState.Hover, true);
        if (state == ControlState.Active) control.SetState(ElementState.Active, true);
        if (state == ControlState.Focus) control.SetState(ElementState.Focus, true);
    }

    private static void AssertClose(float expected, float actual) =>
        Assert.InRange(Math.Abs(actual - expected), 0, 0.5f);
}