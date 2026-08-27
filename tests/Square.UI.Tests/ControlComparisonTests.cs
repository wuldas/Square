using Square.FontComparison;
using Square.Controls;
using Square.CSS.Engine;
using Square.Graphics;
using Square.Rendering;
using Square.UI;
using SkiaSharp;
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

    [Fact]
    public void TextAreaAutoUsesChromiumControlFontSize()
    {
        var textArea = new TextArea();

        new CssEngine().ApplyStyles(textArea);

        Assert.Equal("13.3333px", textArea.Style.Get("font-size"));
        Assert.Equal("monospace", textArea.Style.Get("font-family"));
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

    [Fact]
    public void SelectAutoUsesChromiumControlFont()
    {
        var select = new Select();

        new CssEngine().ApplyStyles(select);

        Assert.Equal("13.3333px", select.Style.Get("font-size"));
        Assert.Equal("Arial", select.Style.Get("font-family"));
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
        Assert.Contains(cases, item => item.Kind == ControlKind.TextArea && item.Value.Contains('\n'));
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
    public void VisualPhaseRejectsMissingScreenshots()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-control-gate-" + Guid.NewGuid());
        var report = Geometry("Software", 10, 20, 100, 36, "cases/button-auto-normal.png");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ControlGeometryGate.EnsureVisualAllowed(
                [report],
                ["button-auto-normal"],
                "manifest-a",
                artifactRoot));

        Assert.Contains("screenshot", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("button-auto-normal", exception.Message);
    }

    [Fact]
    public void VisualPhaseRejectsDuplicateManifestCaseIds()
    {
        var report = Geometry("Software", 10, 20, 100, 36, "square.png");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ControlGeometryGate.EnsureVisualAllowed(
                [report],
                ["button-auto-normal", "button-auto-normal"],
                "manifest-a"));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("button-auto-normal", exception.Message);
    }

    [Fact]
    public void FullVisualPhaseRejectsMissingBlockingRenderer()
    {
        var report = Geometry("Software", 10, 20, 100, 36, "square.png");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ControlGeometryGate.EnsureVisualAllowed(
                [report],
                ["button-auto-normal"],
                "manifest-a",
                requiredRenderers: ["Chromium", "Software", "Skia"]));

        Assert.Contains("Chromium", exception.Message);
        Assert.Contains("Skia", exception.Message);
    }

    [Fact]
    public void GeometryMatrixSummarizesAllControlAppearancesAndRenderers()
    {
        var cases = ControlComparisonManifest.CreateDefault().ExpandCases();
        var reports = new[] { "Chromium", "Software", "Skia" }.Select(renderer =>
            new ControlGeometryReport
            {
                Renderer = renderer,
                Cases = cases.Select(item => new ControlGeometryCaseResult
                {
                    Id = item.Id,
                    Kind = item.Kind,
                    Appearance = item.Appearance,
                    State = item.State,
                    Passed = true
                }).ToList()
            });

        var markdown = ControlGeometryMatrix.CreateMarkdown(cases, reports);

        Assert.Contains("| Button | Auto | 5 | 5/5 | 5/5 | 5/5 |", markdown);
        Assert.Contains("| Radio | None | 6 | 6/6 | 6/6 | 6/6 |", markdown);
        Assert.Equal(12, markdown.Split('\n').Count(line => line.StartsWith("| ") && !line.Contains("Renderer")));
    }

    [Fact]
    public void FullVisualPhaseAcceptsCanonicalArtifactDirectories()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-control-gate-" + Guid.NewGuid());
        try
        {
            var reports = new[]
            {
                Geometry("Chromium", 10, 20, 100, 36, "cases/button-auto-normal.png"),
                Geometry("Software", 10, 20, 100, 36, "cases/button-auto-normal.png"),
                Geometry("Skia", 10, 20, 100, 36, "cases/button-auto-normal.png")
            };
            foreach (var directory in new[] { "chrome", "software", "skia" })
            {
                var screenshot = Path.Combine(artifactRoot, directory, "cases", "button-auto-normal.png");
                Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
                File.WriteAllBytes(screenshot, [1]);
            }

            ControlGeometryGate.EnsureVisualAllowed(
                reports,
                ["button-auto-normal"],
                "manifest-a",
                artifactRoot,
                ["Chromium", "Software", "Skia"]);
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Fact]
    public void ButtonVisualComparisonReportsAChangedCenteredTextRegion()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-button-visual-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(artifactRoot);
            var chromium = Path.Combine(artifactRoot, "chromium.png");
            var square = Path.Combine(artifactRoot, "square.png");
            var diff = Path.Combine(artifactRoot, "diff.png");
            WriteButtonFixture(chromium, SKColors.Black);
            WriteButtonFixture(square, new SKColor(232, 238, 244));

            var result = ControlVisualComparer.CompareButton(
                chromium,
                square,
                diff,
                new ControlRect(10, 10, 40, 20),
                ControlVisualThresholds.Button);

            Assert.False(result.Passed);
            Assert.Contains(result.Regions, region => region.Name == "text" && !region.Passed);
            Assert.All(result.Regions.Where(region => region.Name != "text"), region => Assert.True(region.Passed));
            Assert.True(File.Exists(diff));
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Fact]
    public void ButtonVisualComparisonIgnoresPixelsOutsideTheBorderBox()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-button-visual-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(artifactRoot);
            var chromium = Path.Combine(artifactRoot, "chromium.png");
            var square = Path.Combine(artifactRoot, "square.png");
            WriteButtonFixture(chromium, SKColors.Black, SKColors.Black);
            WriteButtonFixture(square, SKColors.Black, SKColors.White);

            var result = ControlVisualComparer.CompareButton(
                chromium,
                square,
                Path.Combine(artifactRoot, "diff.png"),
                new ControlRect(10, 10, 40, 20),
                ControlVisualThresholds.Button);

            Assert.True(result.Passed, string.Join(" | ", result.Regions.SelectMany(region => region.Failures)));
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Fact]
    public void ButtonVisualComparisonDetectsMissingSparseTextInk()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-button-visual-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(artifactRoot);
            var chromium = Path.Combine(artifactRoot, "chromium.png");
            var square = Path.Combine(artifactRoot, "square.png");
            WriteButtonFixture(chromium, SKColors.Black, sparseText: true);
            WriteButtonFixture(square, new SKColor(232, 238, 244), sparseText: true);

            var result = ControlVisualComparer.CompareButton(
                chromium,
                square,
                Path.Combine(artifactRoot, "diff.png"),
                new ControlRect(10, 10, 40, 20),
                ControlVisualThresholds.Button);

            var text = Assert.Single(result.Regions, region => region.Name == "text");
            Assert.False(text.Passed);
            Assert.Equal(0, text.MaskIoU);
            Assert.All(result.Regions.Where(region => region.Name != "text"), region => Assert.True(region.Passed));
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Fact]
    public void InputVisualComparisonReportsBackgroundBorderTextAndCaretRegions()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-input-visual-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(artifactRoot);
            var chromium = Path.Combine(artifactRoot, "chromium.png");
            var square = Path.Combine(artifactRoot, "square.png");
            WriteInputFixture(chromium, drawCaret: true);
            WriteInputFixture(square, drawCaret: false);

            var result = ControlVisualComparer.CompareInput(
                chromium,
                square,
                Path.Combine(artifactRoot, "diff.png"),
                new ControlRect(10, 10, 40, 20),
                new ControlRect(10, 10, 40, 20),
                ControlState.Focus,
                ControlVisualThresholds.Input);

            Assert.Equal(["corner", "border", "text", "caret", "background"],
                result.Regions.Select(region => region.Name));
            Assert.False(result.Passed);
            Assert.False(Assert.Single(result.Regions, region => region.Name == "caret").Passed);
            Assert.All(result.Regions.Where(region => region.Name != "caret"), region => Assert.True(region.Passed));
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Fact]
    public void InputVisualComparisonAlignsEachRendererBorderBox()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-input-align-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(artifactRoot);
            var chromium = Path.Combine(artifactRoot, "chromium.png");
            var square = Path.Combine(artifactRoot, "square.png");
            WriteInputFixture(chromium, drawCaret: false, left: 10);
            WriteInputFixture(square, drawCaret: false, left: 11);

            var result = ControlVisualComparer.CompareInput(
                chromium,
                square,
                Path.Combine(artifactRoot, "diff.png"),
                new ControlRect(10, 10, 40, 20),
                new ControlRect(11, 10, 40, 20),
                ControlState.Normal,
                ControlVisualThresholds.Input);

            Assert.True(result.Passed, string.Join(" | ", result.Regions.SelectMany(region => region.Failures)));
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Fact]
    public void TextAreaVisualComparisonReportsMultilineTextCaretBorderAndBackgroundRegions()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-textarea-visual-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(artifactRoot);
            var chromium = Path.Combine(artifactRoot, "chromium.png");
            var square = Path.Combine(artifactRoot, "square.png");
            WriteTextAreaFixture(chromium, drawSecondLine: true, drawCaret: true);
            WriteTextAreaFixture(square, drawSecondLine: false, drawCaret: true);

            var result = ControlVisualComparer.CompareTextArea(
                chromium,
                square,
                Path.Combine(artifactRoot, "diff.png"),
                new ControlRect(10, 8, 40, 24),
                new ControlRect(10, 8, 40, 24),
                ControlState.Focus,
                ControlVisualThresholds.TextArea);

            Assert.Equal(["corner", "border", "text-line-1", "text-line-2", "caret", "background"],
                result.Regions.Select(region => region.Name));
            Assert.False(result.Passed);
            Assert.False(Assert.Single(result.Regions, region => region.Name == "text-line-2").Passed);
            Assert.All(result.Regions.Where(region => region.Name != "text-line-2"), region => Assert.True(region.Passed));
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Fact]
    public void TextAreaVisualThresholdAllowsObservedFocusAntialiasingButStillBlocksMissingInk()
    {
        Assert.Equal(0.60f, ControlVisualThresholds.TextArea.MinimumMaskIoU);
        Assert.True(ControlVisualThresholds.TextArea.MinimumMaskIoU > 0.5f);
    }

    [Fact]
    public void SelectVisualComparisonReportsMissingArrowSeparatelyFromTextAndBox()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-select-visual-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(artifactRoot);
            var chromium = Path.Combine(artifactRoot, "chromium.png");
            var square = Path.Combine(artifactRoot, "square.png");
            WriteSelectFixture(chromium, drawArrow: true);
            WriteSelectFixture(square, drawArrow: false);

            var result = ControlVisualComparer.CompareSelect(
                chromium,
                square,
                Path.Combine(artifactRoot, "diff.png"),
                new ControlRect(10, 10, 40, 20),
                new ControlRect(10, 10, 40, 20),
                ControlVisualThresholds.Select);

            Assert.Equal(["corner", "border", "text", "arrow", "background"],
                result.Regions.Select(region => region.Name));
            Assert.False(result.Passed);
            Assert.False(Assert.Single(result.Regions, region => region.Name == "arrow").Passed);
            Assert.All(result.Regions.Where(region => region.Name != "arrow"), region => Assert.True(region.Passed));
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Fact]
    public void SelectVisualThresholdsCoverObservedArrowAndFocusCornerRasterization()
    {
        Assert.Equal(0.60f, ControlVisualThresholds.Select.MinimumMaskIoU);
        Assert.Equal(26f, ControlVisualThresholds.Select.MaximumMeanColorDelta);
        Assert.Equal(0.15f, ControlVisualThresholds.Select.MaximumHighDeltaRatio);
        Assert.Equal(100f, ControlVisualThresholds.Select.MaximumCornerMeanDelta);
        Assert.True(ControlVisualThresholds.Select.MinimumMaskIoU > 0.5f);
    }


    [Fact]
    public async Task SoftwareButtonCaptureIncludesCenteredTextInk()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-button-capture-" + Guid.NewGuid());
        try
        {
            var manifest = new ControlComparisonManifest
            {
                Controls =
                [
                    new ControlDefinition
                    {
                        Kind = ControlKind.Button,
                        Element = "button",
                        Appearances = [ControlAppearance.Auto],
                        States = [ControlState.Normal],
                        AutoAuthorCss = ControlComparisonManifest.ButtonAppearanceAutoCss,
                        Text = "Control"
                    }
                ]
            };

            var report = await ControlSquareCapture.CaptureAsync("Software", manifest, artifactRoot);
            var item = Assert.Single(report.Cases);
            using var bitmap = SKBitmap.Decode(Path.Combine(artifactRoot, item.Screenshot));
            var box = item.BorderBox;
            var darkPixels = 0;
            for (var y = (int)(box.Y + 4); y < (int)(box.Y + box.Height - 4); y++)
                for (var x = (int)(box.X + 8); x < (int)(box.X + box.Width - 8); x++)
                    if (bitmap.GetPixel(x, y).Red < 80) darkPixels++;

            Assert.True(darkPixels >= 10,
                $"Expected centered Button text ink, found {darkPixels} dark pixels; " +
                $"fragments={item.ComputedStyles["textFragments"]}, bounds={item.ComputedStyles["textBounds"]}, family={item.ComputedStyles["textFamily"]}.");
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(ControlState.Normal, 239, 118)]
    [InlineData(ControlState.Hover, 229, 79)]
    [InlineData(ControlState.Active, 245, 141)]
    [InlineData(ControlState.Focus, 229, 79)]
    [InlineData(ControlState.Disabled, 238, 208)]
    public async Task SoftwareButtonAutoStatesUseChromiumWidgetColors(
        ControlState state,
        byte expectedFill,
        byte expectedBorder)
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-button-state-" + Guid.NewGuid());
        try
        {
            var manifest = new ControlComparisonManifest
            {
                Controls =
                [
                    new ControlDefinition
                    {
                        Kind = ControlKind.Button,
                        Element = "button",
                        Appearances = [ControlAppearance.Auto],
                        States = [state],
                        AutoAuthorCss = ControlComparisonManifest.ButtonAppearanceAutoCss,
                        Text = "Control"
                    }
                ]
            };
            var item = Assert.Single((await ControlSquareCapture.CaptureAsync("Software", manifest, artifactRoot)).Cases);
            using var bitmap = SKBitmap.Decode(Path.Combine(artifactRoot, item.Screenshot));
            var colors = new Dictionary<byte, int>();
            for (var y = (int)MathF.Floor(item.BorderBox.Y); y < (int)MathF.Ceiling(item.BorderBox.Y + item.BorderBox.Height); y++)
            {
                for (var x = (int)MathF.Floor(item.BorderBox.X); x < (int)MathF.Ceiling(item.BorderBox.X + item.BorderBox.Width); x++)
                {
                    var color = bitmap.GetPixel(x, y);
                    if (color.Red != color.Green || color.Green != color.Blue || color.Red == 255) continue;
                    colors[color.Red] = colors.TryGetValue(color.Red, out var count) ? count + 1 : 1;
                }
            }
            var dominant = colors.MaxBy(pair => pair.Value).Key;

            Assert.Equal(expectedFill, dominant);
            Assert.Contains(colors.Keys, value => Math.Abs(value - expectedBorder) <= 10);
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(ControlAppearance.Auto, ControlState.Normal, 79)]
    [InlineData(ControlAppearance.Auto, ControlState.Hover, 79)]
    [InlineData(ControlAppearance.Auto, ControlState.Focus, 16)]
    [InlineData(ControlAppearance.Auto, ControlState.Disabled, 212)]
    [InlineData(ControlAppearance.Auto, ControlState.Value, 79)]
    [InlineData(ControlAppearance.Auto, ControlState.Placeholder, 79)]
    [InlineData(ControlAppearance.None, ControlState.Focus, 16)]
    public async Task SoftwareInputStatesUseChromiumOuterBorder(
        ControlAppearance appearance,
        ControlState state,
        byte expectedBorder)
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-input-state-" + Guid.NewGuid());
        try
        {
            var manifest = new ControlComparisonManifest
            {
                Controls =
                [
                    new ControlDefinition
                    {
                        Kind = ControlKind.Input,
                        Element = "input",
                        Appearances = [appearance],
                        States = [state],
                        Value = "Value",
                        Placeholder = "Placeholder"
                    }
                ]
            };
            var item = Assert.Single((await ControlSquareCapture.CaptureAsync("Software", manifest, artifactRoot)).Cases);
            using var bitmap = SKBitmap.Decode(Path.Combine(artifactRoot, item.Screenshot));
            var left = (int)MathF.Round(item.BorderBox.X, MidpointRounding.AwayFromZero);
            var top = (int)MathF.Round(item.BorderBox.Y, MidpointRounding.AwayFromZero);
            var right = (int)MathF.Round(item.BorderBox.X + item.BorderBox.Width, MidpointRounding.AwayFromZero) - 1;
            var bottom = (int)MathF.Round(item.BorderBox.Y + item.BorderBox.Height, MidpointRounding.AwayFromZero) - 1;
            var edge = new List<byte>();
            for (var x = left; x <= right; x++)
            {
                edge.Add(bitmap.GetPixel(x, top).Red);
                edge.Add(bitmap.GetPixel(x, bottom).Red);
            }
            for (var y = top + 1; y < bottom; y++)
            {
                edge.Add(bitmap.GetPixel(left, y).Red);
                edge.Add(bitmap.GetPixel(right, y).Red);
            }

            Assert.True(edge.Count(value => Math.Abs(value - expectedBorder) <= 4) >= edge.Count * 9 / 10,
                $"Expected Chromium border {expectedBorder}, got {string.Join(", ", edge.GroupBy(value => value).OrderByDescending(group => group.Count()).Take(4).Select(group => $"{group.Key}:{group.Count()}"))}.");
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(ControlAppearance.Auto, 4)]
    [InlineData(ControlAppearance.None, 12)]
    public async Task SoftwareInputTextUsesComputedBorderAndPadding(
        ControlAppearance appearance,
        float expectedOffset)
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-input-padding-" + Guid.NewGuid());
        try
        {
            var manifest = new ControlComparisonManifest
            {
                Controls =
                [
                    new ControlDefinition
                    {
                        Kind = ControlKind.Input,
                        Element = "input",
                        Appearances = [appearance],
                        States = [ControlState.Focus],
                        Value = "Value"
                    }
                ]
            };
            var item = Assert.Single((await ControlSquareCapture.CaptureAsync("Software", manifest, artifactRoot)).Cases);
            var textBounds = item.ComputedStyles["textBounds"];
            var textX = float.Parse(textBounds[1..textBounds.IndexOf(',')]);

            AssertClose(item.BorderBox.X + expectedOffset, textX);
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(ControlAppearance.Auto, 3, 3)]
    [InlineData(ControlAppearance.None, 12, 8)]
    public async Task SoftwareTextAreaTextUsesComputedBorderAndPadding(
        ControlAppearance appearance,
        float expectedOffsetX,
        float expectedOffsetY)
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-textarea-padding-" + Guid.NewGuid());
        try
        {
            var manifest = new ControlComparisonManifest
            {
                Controls =
                [
                    new ControlDefinition
                    {
                        Kind = ControlKind.TextArea,
                        Element = "textarea",
                        Appearances = [appearance],
                        States = [ControlState.Value],
                        Value = "Line one\nLine two"
                    }
                ]
            };
            var item = Assert.Single((await ControlSquareCapture.CaptureAsync("Software", manifest, artifactRoot)).Cases);
            var bounds = item.ComputedStyles["textBounds"].Trim('[', ']').Split(',');
            var textX = float.Parse(bounds[0]);
            var textY = float.Parse(bounds[1].Split(' ')[0]);

            AssertClose(item.BorderBox.X + expectedOffsetX, textX);
            AssertClose(item.BorderBox.Y + expectedOffsetY, textY);
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(ControlAppearance.Auto, 4, 3)]
    [InlineData(ControlAppearance.None, 8, 8)]
    public async Task SoftwareSelectTextUsesChromiumContentOffset(
        ControlAppearance appearance,
        float expectedOffsetX,
        float expectedOffsetY)
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-select-padding-" + Guid.NewGuid());
        try
        {
            var manifest = new ControlComparisonManifest
            {
                Controls =
                [
                    new ControlDefinition
                    {
                        Kind = ControlKind.Select,
                        Element = "select",
                        Appearances = [appearance],
                        States = [ControlState.Normal],
                        Value = "Value"
                    }
                ]
            };
            var item = Assert.Single((await ControlSquareCapture.CaptureAsync("Software", manifest, artifactRoot)).Cases);
            var bounds = item.ComputedStyles["textBounds"].Trim('[', ']').Split(',');
            var textX = float.Parse(bounds[0]);
            var textY = float.Parse(bounds[1].Split(' ')[0]);

            AssertClose(item.BorderBox.X + expectedOffsetX, textX);
            AssertClose(item.BorderBox.Y + expectedOffsetY, textY);
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(ControlAppearance.Auto, ControlState.Normal, 79)]
    [InlineData(ControlAppearance.Auto, ControlState.Hover, 79)]
    [InlineData(ControlAppearance.Auto, ControlState.Focus, 16)]
    [InlineData(ControlAppearance.Auto, ControlState.Disabled, 222)]
    [InlineData(ControlAppearance.None, ControlState.Focus, 16)]
    public async Task SoftwareSelectStatesUseChromiumOuterBorder(
        ControlAppearance appearance,
        ControlState state,
        byte expectedBorder)
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-select-state-" + Guid.NewGuid());
        try
        {
            var manifest = new ControlComparisonManifest
            {
                Controls =
                [
                    new ControlDefinition
                    {
                        Kind = ControlKind.Select,
                        Element = "select",
                        Appearances = [appearance],
                        States = [state],
                        Value = "Value"
                    }
                ]
            };
            var item = Assert.Single((await ControlSquareCapture.CaptureAsync("Software", manifest, artifactRoot)).Cases);
            using var bitmap = SKBitmap.Decode(Path.Combine(artifactRoot, item.Screenshot));
            var left = (int)MathF.Round(item.BorderBox.X, MidpointRounding.AwayFromZero);
            var top = (int)MathF.Round(item.BorderBox.Y, MidpointRounding.AwayFromZero);
            var right = (int)MathF.Round(item.BorderBox.X + item.BorderBox.Width, MidpointRounding.AwayFromZero) - 1;
            var bottom = (int)MathF.Round(item.BorderBox.Y + item.BorderBox.Height, MidpointRounding.AwayFromZero) - 1;
            var edge = new List<byte>();
            for (var x = left; x <= right; x++)
            {
                edge.Add(bitmap.GetPixel(x, top).Red);
                edge.Add(bitmap.GetPixel(x, bottom).Red);
            }
            for (var y = top + 1; y < bottom; y++)
            {
                edge.Add(bitmap.GetPixel(left, y).Red);
                edge.Add(bitmap.GetPixel(right, y).Red);
            }

            Assert.True(edge.Count(value => Math.Abs(value - expectedBorder) <= 4) >= edge.Count * 9 / 10,
                $"Expected Chromium Select border {expectedBorder}, got {string.Join(", ", edge.GroupBy(value => value).OrderByDescending(group => group.Count()).Take(4).Select(group => $"{group.Key}:{group.Count()}"))}.");
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Fact]
    public async Task SoftwareSelectAutoArrowUsesChromiumIndicatorPosition()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-select-arrow-" + Guid.NewGuid());
        try
        {
            var manifest = new ControlComparisonManifest
            {
                Controls =
                [
                    new ControlDefinition
                    {
                        Kind = ControlKind.Select,
                        Element = "select",
                        Appearances = [ControlAppearance.Auto],
                        States = [ControlState.Normal],
                        Value = "Value"
                    }
                ]
            };
            var item = Assert.Single((await ControlSquareCapture.CaptureAsync("Software", manifest, artifactRoot)).Cases);
            using var bitmap = SKBitmap.Decode(Path.Combine(artifactRoot, item.Screenshot));
            var right = (int)MathF.Round(item.BorderBox.X + item.BorderBox.Width, MidpointRounding.AwayFromZero) - 1;
            var top = (int)MathF.Round(item.BorderBox.Y, MidpointRounding.AwayFromZero);
            var bottom = (int)MathF.Round(item.BorderBox.Y + item.BorderBox.Height, MidpointRounding.AwayFromZero) - 1;
            var ink = new List<Point>();
            for (var y = top + 2; y < bottom - 1; y++)
                for (var x = right - 18; x < right; x++)
                    if (bitmap.GetPixel(x, y).Red < 160) ink.Add(new Point(x, y));

            Assert.NotEmpty(ink);
            Assert.InRange(ink.Min(point => point.X), right - 12, right - 9);
            Assert.True(ink.Max(point => point.X) >= right - 5,
                $"Expected arrow to reach x={right - 5}, got {ink.Max(point => point.X)}.");
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(ControlAppearance.Auto, ControlState.Normal, 79)]
    [InlineData(ControlAppearance.Auto, ControlState.Hover, 79)]
    [InlineData(ControlAppearance.Auto, ControlState.Focus, 16)]
    [InlineData(ControlAppearance.Auto, ControlState.Disabled, 212)]
    [InlineData(ControlAppearance.Auto, ControlState.Value, 79)]
    [InlineData(ControlAppearance.Auto, ControlState.Placeholder, 79)]
    [InlineData(ControlAppearance.None, ControlState.Focus, 16)]
    public async Task SoftwareTextAreaStatesUseChromiumOuterBorder(
        ControlAppearance appearance,
        ControlState state,
        byte expectedBorder)
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-textarea-state-" + Guid.NewGuid());
        try
        {
            var manifest = new ControlComparisonManifest
            {
                Controls =
                [
                    new ControlDefinition
                    {
                        Kind = ControlKind.TextArea,
                        Element = "textarea",
                        Appearances = [appearance],
                        States = [state],
                        Value = "Line one\nLine two",
                        Placeholder = "Placeholder"
                    }
                ]
            };
            var item = Assert.Single((await ControlSquareCapture.CaptureAsync("Software", manifest, artifactRoot)).Cases);
            using var bitmap = SKBitmap.Decode(Path.Combine(artifactRoot, item.Screenshot));
            var left = (int)MathF.Round(item.BorderBox.X, MidpointRounding.AwayFromZero);
            var top = (int)MathF.Round(item.BorderBox.Y, MidpointRounding.AwayFromZero);
            var right = (int)MathF.Round(item.BorderBox.X + item.BorderBox.Width, MidpointRounding.AwayFromZero) - 1;
            var bottom = (int)MathF.Round(item.BorderBox.Y + item.BorderBox.Height, MidpointRounding.AwayFromZero) - 1;
            var edge = new List<byte>();
            for (var x = left; x <= right; x++)
            {
                edge.Add(bitmap.GetPixel(x, top).Red);
                edge.Add(bitmap.GetPixel(x, bottom).Red);
            }
            for (var y = top + 1; y < bottom; y++)
            {
                edge.Add(bitmap.GetPixel(left, y).Red);
                edge.Add(bitmap.GetPixel(right, y).Red);
            }

            Assert.True(edge.Count(value => Math.Abs(value - expectedBorder) <= 4) >= edge.Count * 9 / 10,
                $"Expected Chromium TextArea border {expectedBorder}, got {string.Join(", ", edge.GroupBy(value => value).OrderByDescending(group => group.Count()).Take(4).Select(group => $"{group.Key}:{group.Count()}"))}.");
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Fact]
    public async Task SoftwareTextAreaFocusPlacesCaretAtBrowserEndPosition()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-textarea-caret-" + Guid.NewGuid());
        try
        {
            var manifest = new ControlComparisonManifest
            {
                Controls =
                [
                    new ControlDefinition
                    {
                        Kind = ControlKind.TextArea,
                        Element = "textarea",
                        Appearances = [ControlAppearance.None],
                        States = [ControlState.Focus],
                        Value = "Line one\nLine two"
                    }
                ]
            };
            var item = Assert.Single((await ControlSquareCapture.CaptureAsync("Software", manifest, artifactRoot)).Cases);
            var caret = item.ComputedStyles["caretBounds"].Trim('[', ']').Split(',');
            var caretX = float.Parse(caret[0]);
            var caretY = float.Parse(caret[1].Split(' ')[0]);

            Assert.True(caretX > item.ContentBox.X + 30, $"Expected end-of-line caret, got {caretX}.");
            Assert.True(caretY > item.ContentBox.Y + 10, $"Expected second-line caret, got {caretY}.");
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
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

    private static void WriteButtonFixture(
        string path,
        SKColor textColor,
        SKColor? outsideColor = null,
        bool sparseText = false)
    {
        using var bitmap = new SKBitmap(60, 40, SKColorType.Bgra8888, SKAlphaType.Opaque);
        bitmap.Erase(SKColors.White);
        using var canvas = new SKCanvas(bitmap);
        using var fill = new SKPaint { Color = new SKColor(232, 238, 244) };
        using var border = new SKPaint { Color = new SKColor(52, 86, 120), Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        using var text = new SKPaint { Color = textColor };
        canvas.DrawRoundRect(new SKRect(10, 10, 50, 30), 4, 4, fill);
        canvas.DrawRoundRect(new SKRect(11, 11, 49, 29), 3, 3, border);
        canvas.DrawRect(sparseText ? new SKRect(29, 19, 31, 21) : new SKRect(26, 17, 34, 23), text);
        if (outsideColor is SKColor color)
        {
            using var outside = new SKPaint { Color = color };
            canvas.DrawRect(new SKRect(0, 0, 8, 8), outside);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    private static void WriteInputFixture(string path, bool drawCaret, int left = 10)
    {
        using var bitmap = new SKBitmap(60, 40, SKColorType.Bgra8888, SKAlphaType.Opaque);
        bitmap.Erase(SKColors.White);
        using var canvas = new SKCanvas(bitmap);
        using var fill = new SKPaint { Color = new SKColor(250, 250, 250) };
        using var border = new SKPaint { Color = new SKColor(118, 118, 118), Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        using var ink = new SKPaint { Color = new SKColor(32, 32, 32) };
        canvas.DrawRect(new SKRect(left, 10, left + 40, 30), fill);
        canvas.DrawRect(new SKRect(left + 1, 11, left + 39, 29), border);
        canvas.DrawRect(new SKRect(left + 6, 17, left + 14, 23), ink);
        if (drawCaret) canvas.DrawRect(new SKRect(left + 37, 16, left + 38, 24), ink);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    private static void WriteTextAreaFixture(string path, bool drawSecondLine, bool drawCaret)
    {
        using var bitmap = new SKBitmap(60, 40, SKColorType.Bgra8888, SKAlphaType.Opaque);
        bitmap.Erase(SKColors.White);
        using var canvas = new SKCanvas(bitmap);
        using var fill = new SKPaint { Color = new SKColor(250, 250, 250) };
        using var border = new SKPaint { Color = new SKColor(118, 118, 118), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var ink = new SKPaint { Color = new SKColor(32, 32, 32) };
        canvas.DrawRect(new SKRect(10, 8, 50, 32), fill);
        canvas.DrawRect(new SKRect(10.5f, 8.5f, 49.5f, 31.5f), border);
        canvas.DrawRect(new SKRect(14, 13, 22, 16), ink);
        if (drawSecondLine) canvas.DrawRect(new SKRect(14, 21, 22, 24), ink);
        if (drawCaret) canvas.DrawRect(new SKRect(32, 12, 33, 25), ink);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    private static void WriteSelectFixture(string path, bool drawArrow)
    {
        using var bitmap = new SKBitmap(60, 40, SKColorType.Bgra8888, SKAlphaType.Opaque);
        bitmap.Erase(SKColors.White);
        using var canvas = new SKCanvas(bitmap);
        using var fill = new SKPaint { Color = new SKColor(250, 250, 250) };
        using var border = new SKPaint { Color = new SKColor(118, 118, 118), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var ink = new SKPaint { Color = new SKColor(32, 32, 32) };
        canvas.DrawRect(new SKRect(10, 10, 50, 30), fill);
        canvas.DrawRect(new SKRect(10.5f, 10.5f, 49.5f, 29.5f), border);
        canvas.DrawRect(new SKRect(14, 17, 22, 23), ink);
        if (drawArrow)
        {
            canvas.DrawRect(new SKRect(42, 17, 44, 19), ink);
            canvas.DrawRect(new SKRect(44, 19, 46, 21), ink);
            canvas.DrawRect(new SKRect(46, 17, 48, 19), ink);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }


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