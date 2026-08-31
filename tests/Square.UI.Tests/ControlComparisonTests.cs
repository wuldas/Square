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
    [Fact]
    public async Task CompleteControlComparisonRunsGeometryBeforeVisual()
    {
        var phases = new List<string>();

        var exitCode = await ControlComparisonRunner.RunCompleteAsync(async phase =>
        {
            phases.Add(phase);
            await Task.CompletedTask;
            return 0;
        });

        Assert.Equal(0, exitCode);
        Assert.Equal(["geometry", "visual"], phases);
    }

    [Fact]
    public async Task ControlComparisonRejectsBackendTraversalBeforeCreatingOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "square-backend-root-" + Guid.NewGuid());
        var outside = Path.Combine(Path.GetTempPath(), "square-backend-outside-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(root);
            var manifestPath = Path.Combine(root, "manifest.json");
            await File.WriteAllTextAsync(
                manifestPath,
                System.Text.Json.JsonSerializer.Serialize(
                    ControlComparisonManifest.CreateSmoke(), ControlReportIO.JsonOptions));
            var backend = Path.GetRelativePath(root, outside);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ControlComparisonRunner.RunAsync("geometry", manifestPath, root, [backend]));

            Assert.Contains("renderer", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(outside));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
        }
    }

    [Theory]
    [Trait("Category", "WindowsRenderingMetrics")]
    [InlineData(ControlState.Normal)]
    [InlineData(ControlState.Hover)]
    [InlineData(ControlState.Active)]
    [InlineData(ControlState.Focus)]
    [InlineData(ControlState.Disabled)]
    public void ButtonAutoGeometryMatchesChromiumAcrossStates(ControlState state) =>
        AssertButtonGeometry(ControlAppearance.Auto, state);

    [Theory]
    [InlineData(ControlState.Normal)]
    [InlineData(ControlState.Hover)]
    [InlineData(ControlState.Active)]
    [InlineData(ControlState.Focus)]
    [InlineData(ControlState.Disabled)]
    public void ButtonNoneGeometryMatchesFixedAuthorBoxAcrossStates(ControlState state) =>
        AssertButtonGeometry(ControlAppearance.None, state);

    private static void AssertButtonGeometry(ControlAppearance appearance, ControlState state)
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

    [Fact]
    [Trait("Category", "WindowsRenderingMetrics")]
    public void ButtonAutoGeometryIncludesUserAgentBoxInExplicitRowFlex()
    {
        var root = new View
        {
            Style =
            {
                CssText = "display: flex; flex-direction: row; align-items: flex-start; " +
                    "box-sizing: border-box; width: 320px; height: 160px;"
            }
        };
        var button = new Button("Control");
        button.Style.CssText = ControlComparisonManifest.ButtonAppearanceAutoCss;
        root.Children.Add(button);

        new CssEngine().ApplyStylesToTree(root);
        new LayoutEngine().MeasureAndArrange(root, new Size(320, 160));

        AssertClose(58.984375f, button.Geometry.Width);
        AssertClose(21, button.Geometry.Height);
        Assert.Equal("6px", button.Style.Get("padding-left"));
        Assert.Equal("6px", button.Style.Get("padding-right"));
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
            ? new Rect(76, 62, 168, 36)
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
        Assert.Equal(new Size(155, 30), textArea.Measure(new Size(float.MaxValue, float.MaxValue)));
    }

    [Fact]
    public async Task ControlManifestPinsTextAreaAutoWidthAcrossBrowserFontEnvironments()
    {
        var manifestPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "tools", "Square.FontComparison", "Cases", "ControlComparisonCases.json"));
        var manifest = await ControlReportIO.LoadManifestAsync(manifestPath);
        var item = Assert.Single(manifest.ExpandCases(), candidate =>
            candidate.Kind == ControlKind.TextArea &&
            candidate.Appearance == ControlAppearance.Auto &&
            candidate.State == ControlState.Normal);

        Assert.Contains("box-sizing: border-box", item.AuthorCss, StringComparison.Ordinal);
        Assert.Contains("width: 168px", item.AuthorCss, StringComparison.Ordinal);
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
    public void AppearanceNoneUsesSharedAuthorCssWithFocusOnlyOutline()
    {
        var manifest = ControlComparisonManifest.CreateDefault();
        var normal = Assert.Single(manifest.ExpandCases(), item =>
            item.Kind == ControlKind.Input && item.Appearance == ControlAppearance.None &&
            item.State == ControlState.Normal);
        var focus = Assert.Single(manifest.ExpandCases(), item =>
            item.Kind == ControlKind.Input && item.Appearance == ControlAppearance.None &&
            item.State == ControlState.Focus);

        Assert.NotEmpty(normal.AuthorCss);
        Assert.Contains("margin: 0", normal.AuthorCss);
        Assert.Equal(manifest.AppearanceNoneAuthorCss, normal.AuthorCss);
        Assert.DoesNotContain("outline:", normal.AuthorCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outline: 1px solid Highlight", focus.AuthorCss, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(focus.ChromiumAuthorCss, focus.SquareAuthorCss);
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
    public void VisualPhaseRejectsScreenshotPathTraversal()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-control-path-" + Guid.NewGuid());
        var outsidePath = Path.Combine(Path.GetTempPath(), "square-control-outside-" + Guid.NewGuid() + ".png");
        try
        {
            Directory.CreateDirectory(Path.Combine(artifactRoot, "software"));
            File.WriteAllBytes(outsidePath, [1]);
            var report = new ControlGeometryReport
            {
                Renderer = "Software",
                ManifestFingerprint = "manifest-a",
                BuildFingerprint = ControlArtifactIdentity.ComputeBuildFingerprint(),
                CaptureSession = "session-a",
                CapturedAt = DateTimeOffset.UtcNow,
                Cases =
                [
                    new ControlGeometryCaseResult
                    {
                        Id = "button-auto-normal",
                        Passed = true,
                        Screenshot = Path.GetRelativePath(
                            Path.Combine(artifactRoot, "software"), outsidePath),
                        ScreenshotSha256 = ControlArtifactIdentity.ComputeFileSha256(outsidePath)
                    }
                ]
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ControlGeometryGate.EnsureVisualAllowed(
                    [report],
                    ["button-auto-normal"],
                    "manifest-a",
                    artifactRoot));

            Assert.Contains("path", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
            if (File.Exists(outsidePath)) File.Delete(outsidePath);
        }
    }

    [Fact]
    public void VisualPhaseRejectsRendererPathTraversal()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-control-renderer-root-" + Guid.NewGuid());
        var outsideRoot = Path.Combine(Path.GetTempPath(), "square-control-renderer-outside-" + Guid.NewGuid());
        try
        {
            var screenshot = Path.Combine(outsideRoot, "cases", "button-auto-normal.png");
            Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
            File.WriteAllBytes(screenshot, [1]);
            var report = new ControlGeometryReport
            {
                Renderer = Path.GetRelativePath(artifactRoot, outsideRoot),
                ManifestFingerprint = "manifest-a",
                BuildFingerprint = ControlArtifactIdentity.ComputeBuildFingerprint(),
                CaptureSession = "session-a",
                CapturedAt = DateTimeOffset.UtcNow,
                Cases =
                [
                    new ControlGeometryCaseResult
                    {
                        Id = "button-auto-normal",
                        Passed = true,
                        Screenshot = "cases/button-auto-normal.png",
                        ScreenshotSha256 = ControlArtifactIdentity.ComputeFileSha256(screenshot)
                    }
                ]
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ControlGeometryGate.EnsureVisualAllowed(
                    [report],
                    ["button-auto-normal"],
                    "manifest-a",
                    artifactRoot));

            Assert.Contains("renderer", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
            if (Directory.Exists(outsideRoot)) Directory.Delete(outsideRoot, recursive: true);
        }
    }

    [Fact]
    public void VisualPhaseRejectsReparsePointInCasesDirectory()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-control-reparse-root-" + Guid.NewGuid());
        var outsideRoot = Path.Combine(Path.GetTempPath(), "square-control-reparse-outside-" + Guid.NewGuid());
        var casesLink = Path.Combine(artifactRoot, "software", "cases");
        try
        {
            Directory.CreateDirectory(Path.Combine(artifactRoot, "software"));
            Directory.CreateDirectory(outsideRoot);
            CreateDirectoryLink(casesLink, outsideRoot);
            var screenshot = Path.Combine(outsideRoot, "button-auto-normal.png");
            File.WriteAllBytes(screenshot, [1]);
            var report = new ControlGeometryReport
            {
                Renderer = "Software",
                ManifestFingerprint = "manifest-a",
                BuildFingerprint = ControlArtifactIdentity.ComputeBuildFingerprint(),
                CaptureSession = "session-a",
                CapturedAt = DateTimeOffset.UtcNow,
                Cases =
                [
                    new ControlGeometryCaseResult
                    {
                        Id = "button-auto-normal",
                        Passed = true,
                        Screenshot = "cases/button-auto-normal.png",
                        ScreenshotSha256 = ControlArtifactIdentity.ComputeFileSha256(screenshot)
                    }
                ]
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ControlGeometryGate.EnsureVisualAllowed(
                    [report],
                    ["button-auto-normal"],
                    "manifest-a",
                    artifactRoot));

            Assert.Contains("reparse", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(casesLink)) Directory.Delete(casesLink);
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
            if (Directory.Exists(outsideRoot)) Directory.Delete(outsideRoot, recursive: true);
        }
    }

    [Fact]
    public async Task VisualPhaseRejectsTamperedScreenshotHash()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-control-hash-" + Guid.NewGuid());
        try
        {
            var screenshot = Path.Combine(artifactRoot, "software", "cases", "button-auto-normal.png");
            Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
            var expectedBytes = new byte[] { 1 };
            File.WriteAllBytes(screenshot, [2]);
            var expectedHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(expectedBytes)).ToLowerInvariant();
            var reportPath = Path.Combine(artifactRoot, "software", "geometry.json");
            await File.WriteAllTextAsync(reportPath, $$"""
                {
                  "renderer": "Software",
                  "manifestFingerprint": "manifest-a",
                  "buildFingerprint": "{{ControlArtifactIdentity.ComputeBuildFingerprint()}}",
                  "captureSession": "session-a",
                  "capturedAt": "{{DateTimeOffset.UtcNow:O}}",
                  "cases": [
                    {
                      "id": "button-auto-normal",
                      "passed": true,
                      "screenshot": "cases/button-auto-normal.png",
                      "screenshotSha256": "{{expectedHash}}"
                    }
                  ]
                }
                """);
            var report = await ControlReportIO.ReadAsync(reportPath);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ControlGeometryGate.EnsureVisualAllowed(
                    [report],
                    ["button-auto-normal"],
                    "manifest-a",
                    artifactRoot));

            Assert.Contains("screenshot", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("hash", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Fact]
    public async Task VisualPhaseRejectsStaleBuildFingerprint()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-control-build-" + Guid.NewGuid());
        try
        {
            var screenshot = Path.Combine(artifactRoot, "software", "cases", "button-auto-normal.png");
            Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
            File.WriteAllBytes(screenshot, [1]);
            var screenshotHash = ControlArtifactIdentity.ComputeFileSha256(screenshot);
            var reportPath = Path.Combine(artifactRoot, "software", "geometry.json");
            await File.WriteAllTextAsync(reportPath, $$"""
                {
                  "renderer": "Software",
                  "manifestFingerprint": "manifest-a",
                  "buildFingerprint": "stale-build",
                  "captureSession": "session-a",
                  "capturedAt": "{{DateTimeOffset.UtcNow:O}}",
                  "cases": [
                    {
                      "id": "button-auto-normal",
                      "passed": true,
                      "screenshot": "cases/button-auto-normal.png",
                      "screenshotSha256": "{{screenshotHash}}"
                    }
                  ]
                }
                """);
            var report = await ControlReportIO.ReadAsync(reportPath);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ControlGeometryGate.EnsureVisualAllowed(
                    [report],
                    ["button-auto-normal"],
                    "manifest-a",
                    artifactRoot));

            Assert.Contains("build fingerprint", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Fact]
    public async Task VisualPhaseRejectsMixedCaptureSessions()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-control-session-" + Guid.NewGuid());
        try
        {
            async Task<ControlGeometryReport> WriteReport(string renderer, string directory, string session)
            {
                var screenshot = Path.Combine(artifactRoot, directory, "cases", "button-auto-normal.png");
                Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
                File.WriteAllBytes(screenshot, [1]);
                var reportPath = Path.Combine(artifactRoot, directory, "geometry.json");
                await File.WriteAllTextAsync(reportPath, $$"""
                    {
                      "renderer": "{{renderer}}",
                      "manifestFingerprint": "manifest-a",
                      "buildFingerprint": "{{ControlArtifactIdentity.ComputeBuildFingerprint()}}",
                      "captureSession": "{{session}}",
                      "capturedAt": "{{DateTimeOffset.UtcNow:O}}",
                      "cases": [
                        {
                          "id": "button-auto-normal",
                          "passed": true,
                          "screenshot": "cases/button-auto-normal.png",
                          "screenshotSha256": "{{ControlArtifactIdentity.ComputeFileSha256(screenshot)}}"
                        }
                      ]
                    }
                    """);
                return await ControlReportIO.ReadAsync(reportPath);
            }

            var reports = new[]
            {
                await WriteReport("Software", "software", "session-a"),
                await WriteReport("Skia", "skia", "session-b")
            };
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ControlGeometryGate.EnsureVisualAllowed(
                    reports,
                    ["button-auto-normal"],
                    "manifest-a",
                    artifactRoot,
                    ["Software", "Skia"]));

            Assert.Contains("capture session", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Fact]
    public async Task VisualPhaseRejectsCaptureSessionWithInconsistentTimestamps()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-control-time-" + Guid.NewGuid());
        try
        {
            async Task<ControlGeometryReport> WriteReport(string renderer, string directory, DateTimeOffset capturedAt)
            {
                var screenshot = Path.Combine(artifactRoot, directory, "cases", "button-auto-normal.png");
                Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
                File.WriteAllBytes(screenshot, [1]);
                var reportPath = Path.Combine(artifactRoot, directory, "geometry.json");
                await File.WriteAllTextAsync(reportPath, $$"""
                    {
                      "renderer": "{{renderer}}",
                      "manifestFingerprint": "manifest-a",
                      "buildFingerprint": "{{ControlArtifactIdentity.ComputeBuildFingerprint()}}",
                      "captureSession": "session-a",
                      "capturedAt": "{{capturedAt:O}}",
                      "cases": [
                        {
                          "id": "button-auto-normal",
                          "passed": true,
                          "screenshot": "cases/button-auto-normal.png",
                          "screenshotSha256": "{{ControlArtifactIdentity.ComputeFileSha256(screenshot)}}"
                        }
                      ]
                    }
                    """);
                return await ControlReportIO.ReadAsync(reportPath);
            }

            var now = DateTimeOffset.UtcNow;
            var reports = new[]
            {
                await WriteReport("Software", "software", now),
                await WriteReport("Skia", "skia", now.AddMinutes(-15))
            };
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ControlGeometryGate.EnsureVisualAllowed(
                    reports,
                    ["button-auto-normal"],
                    "manifest-a",
                    artifactRoot,
                    ["Software", "Skia"]));

            Assert.Contains("capture timestamp", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("stale")]
    public async Task VisualPhaseRejectsMissingOrStaleCaptureTimestamp(string mode)
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-control-freshness-" + Guid.NewGuid());
        try
        {
            var screenshot = Path.Combine(artifactRoot, "software", "cases", "button-auto-normal.png");
            Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
            File.WriteAllBytes(screenshot, [1]);
            var capturedAtProperty = mode == "stale"
                ? $"\"capturedAt\": \"{DateTimeOffset.UtcNow.AddMinutes(-30):O}\","
                : "";
            var reportPath = Path.Combine(artifactRoot, "software", "geometry.json");
            await File.WriteAllTextAsync(reportPath, $$"""
                {
                  "renderer": "Software",
                  "manifestFingerprint": "manifest-a",
                  "buildFingerprint": "{{ControlArtifactIdentity.ComputeBuildFingerprint()}}",
                  "captureSession": "session-a",
                  {{capturedAtProperty}}
                  "cases": [
                    {
                      "id": "button-auto-normal",
                      "passed": true,
                      "screenshot": "cases/button-auto-normal.png",
                      "screenshotSha256": "{{ControlArtifactIdentity.ComputeFileSha256(screenshot)}}"
                    }
                  ]
                }
                """);
            var report = await ControlReportIO.ReadAsync(reportPath);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ControlGeometryGate.EnsureVisualAllowed(
                    [report],
                    ["button-auto-normal"],
                    "manifest-a",
                    artifactRoot));

            Assert.Contains("capture timestamp", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
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
            foreach (var directory in new[] { "chrome", "software", "skia" })
            {
                var screenshot = Path.Combine(artifactRoot, directory, "cases", "button-auto-normal.png");
                Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
                File.WriteAllBytes(screenshot, [1]);
            }
            var screenshotHash = ControlArtifactIdentity.ComputeFileSha256(
                Path.Combine(artifactRoot, "chrome", "cases", "button-auto-normal.png"));
            var reports = new[]
            {
                Geometry("Chromium", 10, 20, 100, 36, "cases/button-auto-normal.png", screenshotHash),
                Geometry("Software", 10, 20, 100, 36, "cases/button-auto-normal.png", screenshotHash),
                Geometry("Skia", 10, 20, 100, 36, "cases/button-auto-normal.png", screenshotHash)
            };

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
    public void ButtonVisualComparisonRejectsEqualLuminanceDifferentHue()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-button-hue-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(artifactRoot);
            var chromium = Path.Combine(artifactRoot, "chromium.png");
            var square = Path.Combine(artifactRoot, "square.png");
            WriteButtonFixture(chromium, SKColors.Black, fillColor: new SKColor(255, 0, 0));
            WriteButtonFixture(square, SKColors.Black, fillColor: new SKColor(0, 130, 0));

            var result = ControlVisualComparer.CompareButton(
                chromium,
                square,
                Path.Combine(artifactRoot, "diff.png"),
                new ControlRect(10, 10, 40, 20),
                ControlVisualThresholds.Button);

            var background = Assert.Single(result.Regions, region => region.Name == "background");
            Assert.False(background.Passed);
            Assert.True(background.MeanColorDelta > 0);
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
    public void TextAreaVisualComparisonUsesContentBoxOffsetForContentRegions()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-textarea-offset-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(artifactRoot);
            var chromium = Path.Combine(artifactRoot, "chromium.png");
            var square = Path.Combine(artifactRoot, "square.png");
            var diff = Path.Combine(artifactRoot, "diff.png");
            WriteOffsetTextAreaFixture(chromium, borderLeft: 10, textLeft: 17);
            WriteOffsetTextAreaFixture(square, borderLeft: 11, textLeft: 16);

            var result = ControlVisualComparer.CompareTextArea(
                chromium,
                square,
                diff,
                new ControlRect(10.49f, 8, 40, 24),
                new ControlRect(10.51f, 8, 40, 24),
                new ControlRect(14.51f, 10, 30, 20),
                new ControlRect(14.49f, 10, 30, 20),
                ControlState.Normal,
                ControlVisualThresholds.TextArea);

            Assert.True(result.Passed,
                string.Join(" | ", result.Regions.SelectMany(region => region.Failures)));
            using var diffBitmap = SKBitmap.Decode(diff);
            Assert.Equal(0, diffBitmap.GetPixel(17, 13).Red);
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
    public void CheckBoxVisualComparisonReportsMissingCheckGlyphSeparatelyFromBox()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-checkbox-visual-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(artifactRoot);
            var chromium = Path.Combine(artifactRoot, "chromium.png");
            var square = Path.Combine(artifactRoot, "square.png");
            WriteCheckBoxFixture(chromium, drawCheck: true);
            WriteCheckBoxFixture(square, drawCheck: false);

            var result = ControlVisualComparer.CompareCheckBox(
                chromium,
                square,
                Path.Combine(artifactRoot, "diff.png"),
                new ControlRect(10, 10, 20, 20),
                new ControlRect(10, 10, 20, 20),
                ControlVisualThresholds.CheckBox);

            Assert.Equal(["corner", "border", "check", "background"],
                result.Regions.Select(region => region.Name));
            Assert.False(result.Passed);
            Assert.False(Assert.Single(result.Regions, region => region.Name == "check").Passed);
            Assert.All(result.Regions.Where(region => region.Name != "check"), region => Assert.True(region.Passed));
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(ControlState.Unchecked)]
    [InlineData(ControlState.Checked)]
    [InlineData(ControlState.Hover)]
    [InlineData(ControlState.Active)]
    [InlineData(ControlState.Focus)]
    [InlineData(ControlState.Disabled)]
    public async Task SoftwareCheckBoxAutoChromeStaysInsideChromiumBorderBox(ControlState state)
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-checkbox-bounds-" + Guid.NewGuid());
        try
        {
            var manifest = new ControlComparisonManifest
            {
                Controls =
                [
                    new ControlDefinition
                    {
                        Kind = ControlKind.CheckBox,
                        Element = "input",
                        Appearances = [ControlAppearance.Auto],
                        States = [state]
                    }
                ]
            };
            var item = Assert.Single((await ControlSquareCapture.CaptureAsync("Software", manifest, artifactRoot)).Cases);
            using var bitmap = SKBitmap.Decode(Path.Combine(artifactRoot, item.Screenshot));
            var left = (int)MathF.Round(item.BorderBox.X, MidpointRounding.AwayFromZero);
            var top = (int)MathF.Round(item.BorderBox.Y, MidpointRounding.AwayFromZero);
            var right = (int)MathF.Round(item.BorderBox.X + item.BorderBox.Width, MidpointRounding.AwayFromZero) - 1;
            var bottom = (int)MathF.Round(item.BorderBox.Y + item.BorderBox.Height, MidpointRounding.AwayFromZero) - 1;
            var ink = new List<Point>();
            for (var y = 0; y < bitmap.Height; y++)
                for (var x = 0; x < bitmap.Width; x++)
                    if (bitmap.GetPixel(x, y) != SKColors.White) ink.Add(new Point(x, y));

            Assert.NotEmpty(ink);
            Assert.InRange(ink.Min(point => point.X), left, right);
            Assert.InRange(ink.Max(point => point.X), left, right);
            Assert.InRange(ink.Min(point => point.Y), top, bottom);
            Assert.InRange(ink.Max(point => point.Y), top, bottom);
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Fact]
    public void CheckBoxVisualThresholdsCoverObservedSoftwareAndSkiaCheckAntialiasing()
    {
        Assert.Equal(0.65f, ControlVisualThresholds.CheckBox.MinimumMaskIoU);
        Assert.Equal(27f, ControlVisualThresholds.CheckBox.MaximumMeanColorDelta);
        Assert.Equal(0.19f, ControlVisualThresholds.CheckBox.MaximumHighDeltaRatio);
        Assert.True(ControlVisualThresholds.CheckBox.MinimumMaskIoU > 0.5f);
    }

    [Fact]
    public void RadioVisualComparisonReportsMissingInnerDotSeparatelyFromCircle()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-radio-visual-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(artifactRoot);
            var chromium = Path.Combine(artifactRoot, "chromium.png");
            var square = Path.Combine(artifactRoot, "square.png");
            WriteRadioFixture(chromium, drawDot: true);
            WriteRadioFixture(square, drawDot: false);

            var result = ControlVisualComparer.CompareRadio(
                chromium,
                square,
                Path.Combine(artifactRoot, "diff.png"),
                new ControlRect(10, 10, 20, 20),
                new ControlRect(10, 10, 20, 20),
                ControlVisualThresholds.Radio);

            Assert.Equal(["corner", "border", "dot", "background"],
                result.Regions.Select(region => region.Name));
            Assert.False(result.Passed);
            Assert.False(Assert.Single(result.Regions, region => region.Name == "dot").Passed);
            Assert.All(result.Regions.Where(region => region.Name != "dot"), region => Assert.True(region.Passed));
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(ControlState.Unchecked)]
    [InlineData(ControlState.Checked)]
    [InlineData(ControlState.Hover)]
    [InlineData(ControlState.Active)]
    [InlineData(ControlState.Focus)]
    [InlineData(ControlState.Disabled)]
    public async Task SoftwareRadioAutoChromeStaysInsideChromiumBorderBox(ControlState state)
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-radio-bounds-" + Guid.NewGuid());
        try
        {
            var manifest = new ControlComparisonManifest
            {
                Controls =
                [
                    new ControlDefinition
                    {
                        Kind = ControlKind.Radio,
                        Element = "input",
                        Appearances = [ControlAppearance.Auto],
                        States = [state]
                    }
                ]
            };
            var item = Assert.Single((await ControlSquareCapture.CaptureAsync("Software", manifest, artifactRoot)).Cases);
            using var bitmap = SKBitmap.Decode(Path.Combine(artifactRoot, item.Screenshot));
            var left = (int)MathF.Round(item.BorderBox.X, MidpointRounding.AwayFromZero);
            var top = (int)MathF.Round(item.BorderBox.Y, MidpointRounding.AwayFromZero);
            var right = (int)MathF.Round(item.BorderBox.X + item.BorderBox.Width, MidpointRounding.AwayFromZero) - 1;
            var bottom = (int)MathF.Round(item.BorderBox.Y + item.BorderBox.Height, MidpointRounding.AwayFromZero) - 1;
            var ink = new List<Point>();
            for (var y = 0; y < bitmap.Height; y++)
                for (var x = 0; x < bitmap.Width; x++)
                    if (bitmap.GetPixel(x, y) != SKColors.White) ink.Add(new Point(x, y));

            Assert.NotEmpty(ink);
            Assert.InRange(ink.Min(point => point.X), left, right);
            Assert.InRange(ink.Max(point => point.X), left, right);
            Assert.InRange(ink.Min(point => point.Y), top, bottom);
            Assert.InRange(ink.Max(point => point.Y), top, bottom);
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }


    [Fact]
    [Trait("Category", "WindowsRenderingMetrics")]
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

    [Fact]
    [Trait("Category", "WindowsRenderingMetrics")]
    public void DefaultButtonTextUsesChromiumWidgetAdvance()
    {
        var root = new View
        {
            Style =
            {
                CssText = "display: flex; flex-direction: row; align-items: flex-start; " +
                    "box-sizing: border-box; width: 320px; height: 160px;"
            }
        };
        var button = new Button("Clear Cache");
        button.Style.CssText = ControlComparisonManifest.ButtonAppearanceAutoCss;
        root.Children.Add(button);

        new CssEngine().ApplyStylesToTree(root);
        new LayoutEngine().MeasureAndArrange(root, new Size(320, 160));
        var tree = new DisplayTree();
        tree.BuildFrom(root);

        var fragment = Assert.Single(tree.CollectTextFragments(button));
        AssertClose(button.Geometry.Width - 16, fragment.Bounds.Width);
    }

    [Fact]
    public async Task SoftwareButtonAutoPaintHonorsAuthorBackground()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-button-author-background-" + Guid.NewGuid());
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
                        AutoAuthorCss = "background: #0d6efd; color: #ffffff; border-radius: 6px;",
                        Text = "Sign in"
                    }
                ]
            };

            var item = Assert.Single((await ControlSquareCapture.CaptureAsync("Software", manifest, artifactRoot)).Cases);
            Assert.Equal("#0d6efd", item.ComputedStyles["backgroundColor"]);
            Assert.Equal("6px", item.ComputedStyles["borderRadius"]);
            using var bitmap = SKBitmap.Decode(Path.Combine(artifactRoot, item.Screenshot));
            var sample = bitmap.GetPixel(
                (int)MathF.Round(item.BorderBox.X, MidpointRounding.AwayFromZero) + 6,
                (int)MathF.Round(item.BorderBox.Y + item.BorderBox.Height / 2f, MidpointRounding.AwayFromZero));

            Assert.Equal(new SKColor(0x0d, 0x6e, 0xfd), sample);
            var top = (int)MathF.Round(item.BorderBox.Y, MidpointRounding.AwayFromZero);
            var bottom = (int)MathF.Round(item.BorderBox.Y + item.BorderBox.Height, MidpointRounding.AwayFromZero) - 1;
            var left = (int)MathF.Round(item.BorderBox.X, MidpointRounding.AwayFromZero) + 8;
            var right = (int)MathF.Round(item.BorderBox.X + item.BorderBox.Width, MidpointRounding.AwayFromZero) - 9;
            var textRows = new List<int>();
            for (var y = top + 3; y <= bottom - 3; y++)
                for (var x = left; x <= right; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.Red > 180 && pixel.Green > 180 && pixel.Blue > 180)
                        textRows.Add(y);
                }

            Assert.NotEmpty(textRows);
            var inkCenter = (textRows.Min() + textRows.Max()) / 2f;
            var buttonCenter = item.BorderBox.Y + item.BorderBox.Height / 2f;
            Assert.InRange(inkCenter - buttonCenter, 0, 0.5f);
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "WindowsRenderingMetrics")]
    public async Task SoftwareMultilineAuthorButtonCentersAllRenderedTextInk()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-button-multiline-center-" + Guid.NewGuid());
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
                        AutoAuthorCss =
                            "width: 120px; height: 60px; background: #0d6efd; color: #ffffff; " +
                            "border: 0; border-radius: 6px; text-decoration: underline;",
                        Text = "Top\nBottom"
                    }
                ]
            };

            var item = Assert.Single((await ControlSquareCapture.CaptureAsync("Software", manifest, artifactRoot)).Cases);
            using var bitmap = SKBitmap.Decode(Path.Combine(artifactRoot, item.Screenshot));
            var top = (int)MathF.Round(item.BorderBox.Y, MidpointRounding.AwayFromZero);
            var bottom = (int)MathF.Round(item.BorderBox.Y + item.BorderBox.Height, MidpointRounding.AwayFromZero) - 1;
            var left = (int)MathF.Round(item.BorderBox.X, MidpointRounding.AwayFromZero) + 8;
            var right = (int)MathF.Round(item.BorderBox.X + item.BorderBox.Width, MidpointRounding.AwayFromZero) - 9;
            var textRows = new List<int>();
            for (var y = top + 3; y <= bottom - 3; y++)
                for (var x = left; x <= right; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.Red > 180 && pixel.Green > 180 && pixel.Blue > 180)
                        textRows.Add(y);
                }

            Assert.NotEmpty(textRows);
            var inkCenter = (textRows.Min() + textRows.Max()) / 2f;
            var buttonCenter = item.BorderBox.Y + item.BorderBox.Height / 2f;
            Assert.InRange(inkCenter - buttonCenter, 0, 0.5f);
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(ControlKind.Input)]
    [InlineData(ControlKind.TextArea)]
    [InlineData(ControlKind.Select)]
    public async Task SoftwareAutoFormControlPaintHonorsAuthorBorderNone(ControlKind kind)
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-author-border-none-" + Guid.NewGuid());
        try
        {
            var manifest = new ControlComparisonManifest
            {
                Controls =
                [
                    new ControlDefinition
                    {
                        Kind = kind,
                        Element = kind.ToString(),
                        Appearances = [ControlAppearance.Auto],
                        States = [ControlState.Normal],
                        AutoAuthorCss =
                            "width: 180px; height: 36px; background: #123456; " +
                            "border: 0; border-radius: 0; color: #ffffff;",
                        Value = "Value"
                    }
                ]
            };

            var item = Assert.Single((await ControlSquareCapture.CaptureAsync("Software", manifest, artifactRoot)).Cases);
            using var bitmap = SKBitmap.Decode(Path.Combine(artifactRoot, item.Screenshot));
            var sample = bitmap.GetPixel(
                (int)MathF.Round(item.BorderBox.X, MidpointRounding.AwayFromZero) + 6,
                (int)MathF.Round(item.BorderBox.Y, MidpointRounding.AwayFromZero));

            Assert.Equal(new SKColor(0x12, 0x34, 0x56), sample);
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
    [InlineData(ControlState.Focus, 239, 118)]
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
    [InlineData(ControlAppearance.Auto, ControlState.Normal, 118)]
    [InlineData(ControlAppearance.Auto, ControlState.Hover, 79)]
    [InlineData(ControlAppearance.Auto, ControlState.Focus, 16)]
    [InlineData(ControlAppearance.Auto, ControlState.Disabled, 212)]
    [InlineData(ControlAppearance.Auto, ControlState.Value, 118)]
    [InlineData(ControlAppearance.Auto, ControlState.Placeholder, 118)]
    [InlineData(ControlAppearance.None, ControlState.Focus, 52)]
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

    [Fact]
    [Trait("Category", "WindowsRenderingMetrics")]
    public async Task SoftwareAuthorStyledSelectUsesBootstrapContentBox()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-select-bootstrap-content-" + Guid.NewGuid());
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
                        AutoAuthorCss =
                            "height: 38px; line-height: 24px; padding: 6px 36px 6px 12px; border-radius: 6px;",
                        Value = "Plan"
                    }
                ]
            };
            var item = Assert.Single((await ControlSquareCapture.CaptureAsync("Software", manifest, artifactRoot)).Cases);
            using var bitmap = SKBitmap.Decode(Path.Combine(artifactRoot, item.Screenshot));
            var left = (int)MathF.Round(item.BorderBox.X, MidpointRounding.AwayFromZero);
            var top = (int)MathF.Round(item.BorderBox.Y, MidpointRounding.AwayFromZero);
            var right = (int)MathF.Round(item.BorderBox.X + item.BorderBox.Width, MidpointRounding.AwayFromZero) - 24;
            var bottom = (int)MathF.Round(item.BorderBox.Y + item.BorderBox.Height, MidpointRounding.AwayFromZero) - 1;
            var ink = new List<(int X, int Y)>();
            for (var y = top + 2; y < bottom - 1; y++)
                for (var x = left + 2; x < right; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.Red < 80 && pixel.Green < 80 && pixel.Blue < 80)
                        ink.Add((x, y));
                }

            Assert.NotEmpty(ink);
            Assert.InRange(ink.Min(point => point.X) - left, 12, 15);
            var inkCenter = (ink.Min(point => point.Y) + ink.Max(point => point.Y)) / 2f;
            var selectCenter = item.BorderBox.Y + item.BorderBox.Height / 2f;
            Assert.InRange(inkCenter - selectCenter, 0, 0.5f);
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
    [InlineData(ControlAppearance.Auto, ControlState.Normal, 118)]
    [InlineData(ControlAppearance.Auto, ControlState.Hover, 79)]
    [InlineData(ControlAppearance.Auto, ControlState.Focus, 16)]
    [InlineData(ControlAppearance.Auto, ControlState.Disabled, 222)]
    [InlineData(ControlAppearance.None, ControlState.Focus, 52)]
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
    [Trait("Category", "WindowsRenderingMetrics")]
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
    [InlineData(ControlAppearance.Auto, ControlState.Normal, 118)]
    [InlineData(ControlAppearance.Auto, ControlState.Hover, 79)]
    [InlineData(ControlAppearance.Auto, ControlState.Focus, 16)]
    [InlineData(ControlAppearance.Auto, ControlState.Disabled, 212)]
    [InlineData(ControlAppearance.Auto, ControlState.Value, 118)]
    [InlineData(ControlAppearance.Auto, ControlState.Placeholder, 118)]
    [InlineData(ControlAppearance.None, ControlState.Focus, 52)]
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
    [Trait("Category", "WindowsRenderingMetrics")]
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
            Assert.InRange(
                caretY,
                item.ContentBox.Y + item.ContentBox.Height / 2f,
                item.ContentBox.Y + item.ContentBox.Height - 1);
        }
        finally
        {
            if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Fact]
    public void TextAreaCaretDoesNotJumpWhenMovingToAnAlreadyVisiblePreviousLine()
    {
        var area = new TextArea
        {
            Geometry = new Rect(0, 0, 200, 60),
            Value = "0\n1\n2\n3\n4\n5\n6\n7\n8\n9"
        };
        area.Style.CssText =
            "appearance: none; border: 1px solid #767676; padding: 2px; " +
            "font: 14px Arial; line-height: 17px;";
        area.Focus();
        area.HandleKey(35, control: true);
        var lastLineCaret = area.CaretRect;

        area.HandleKey(38);
        var previousLineCaret = area.CaretRect;

        Assert.True(previousLineCaret.Y < lastLineCaret.Y,
            $"Expected previous visible line above y={lastLineCaret.Y}, got y={previousLineCaret.Y}.");
        Assert.True(previousLineCaret.Bottom <= area.Geometry.Bottom);
    }

    [Fact]
    public void TextAreaOversizedLineCaretVisibilityIsIdempotent()
    {
        var area = new TextArea
        {
            Geometry = new Rect(0, 0, 200, 60),
            Value = "Oversized"
        };
        area.Style.CssText =
            "appearance: none; border: 1px solid #767676; padding: 2px; " +
            "font: 14px Arial; line-height: 100px;";
        area.Focus();
        area.HandleKey(35, control: true);

        var first = area.CaretRect;
        var second = area.CaretRect;
        var third = area.CaretRect;

        Assert.Equal(first.Y, second.Y);
        Assert.Equal(second.Y, third.Y);
    }

    [Fact]
    [Trait("Category", "ChromiumIntegration")]
    public async Task ChromiumFocusCaptureIsIndependentOfPriorHoverCase()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "square-browser-state-" + Guid.NewGuid());
        try
        {
            static ControlComparisonManifest Create(params ControlState[] states) => new()
            {
                Controls =
                [
                    new ControlDefinition
                    {
                        Kind = ControlKind.Button,
                        Element = "button",
                        Appearances = [ControlAppearance.Auto],
                        States = states.ToList(),
                        AutoAuthorCss = ControlComparisonManifest.ButtonAppearanceAutoCss,
                        Text = "Control"
                    }
                ]
            };

            var focusDirectory = Path.Combine(artifactRoot, "focus", "chrome");
            var orderedDirectory = Path.Combine(artifactRoot, "ordered", "chrome");
            var focusOnly = await ControlBrowserCapture.CaptureAsync(Create(ControlState.Focus), focusDirectory);
            var ordered = await ControlBrowserCapture.CaptureAsync(
                Create(ControlState.Hover, ControlState.Focus),
                orderedDirectory);

            var focusOnlyPath = Path.Combine(focusDirectory,
                Assert.Single(focusOnly.Cases).Screenshot.Replace('/', Path.DirectorySeparatorChar));
            var orderedFocusPath = Path.Combine(orderedDirectory,
                Assert.Single(ordered.Cases, item => item.State == ControlState.Focus).Screenshot.Replace('/', Path.DirectorySeparatorChar));
            var orderedHoverPath = Path.Combine(orderedDirectory,
                Assert.Single(ordered.Cases, item => item.State == ControlState.Hover).Screenshot.Replace('/', Path.DirectorySeparatorChar));

            Assert.Equal(File.ReadAllBytes(focusOnlyPath), File.ReadAllBytes(orderedFocusPath));
            Assert.NotEqual(File.ReadAllBytes(orderedHoverPath), File.ReadAllBytes(orderedFocusPath));
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

    private static void CreateDirectoryLink(string linkPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(targetPath);
        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start mklink.");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"mklink failed: {process.StandardError.ReadToEnd()} {process.StandardOutput.ReadToEnd()}");
    }

    private static ControlGeometryReport Geometry(
        string renderer, float x, float y, float width, float height, string screenshot,
        string screenshotSha256 = "") => new()
        {
            Renderer = renderer,
            ManifestFingerprint = "manifest-a",
            BuildFingerprint = ControlArtifactIdentity.ComputeBuildFingerprint(),
            CaptureSession = "session-a",
            CapturedAt = DateTimeOffset.UtcNow,
            Cases =
            [
                new ControlGeometryCaseResult
                {
                    Id = "button-auto-normal",
                    Passed = true,
                    BorderBox = new ControlRect(x, y, width, height),
                    ContentBox = new ControlRect(x, y, width, height),
                    Screenshot = screenshot,
                    ScreenshotSha256 = screenshotSha256
                }
            ]
        };

    private static void WriteButtonFixture(
        string path,
        SKColor textColor,
        SKColor? outsideColor = null,
        bool sparseText = false,
        SKColor? fillColor = null)
    {
        using var bitmap = new SKBitmap(60, 40, SKColorType.Bgra8888, SKAlphaType.Opaque);
        bitmap.Erase(SKColors.White);
        using var canvas = new SKCanvas(bitmap);
        using var fill = new SKPaint { Color = fillColor ?? new SKColor(232, 238, 244) };
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

    private static void WriteOffsetTextAreaFixture(string path, int borderLeft, int textLeft)
    {
        using var bitmap = new SKBitmap(80, 40, SKColorType.Bgra8888, SKAlphaType.Opaque);
        bitmap.Erase(SKColors.White);
        using var canvas = new SKCanvas(bitmap);
        using var fill = new SKPaint { Color = new SKColor(250, 250, 250) };
        using var border = new SKPaint { Color = new SKColor(118, 118, 118), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var ink = new SKPaint { Color = new SKColor(32, 32, 32) };
        canvas.DrawRect(new SKRect(borderLeft, 8, borderLeft + 40, 32), fill);
        canvas.DrawRect(new SKRect(borderLeft + 0.5f, 8.5f, borderLeft + 39.5f, 31.5f), border);
        canvas.DrawRect(new SKRect(textLeft, 12, textLeft + 1, 16), ink);
        canvas.DrawRect(new SKRect(textLeft, 22, textLeft + 1, 26), ink);
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

    private static void WriteCheckBoxFixture(string path, bool drawCheck)
    {
        using var bitmap = new SKBitmap(40, 40, SKColorType.Bgra8888, SKAlphaType.Opaque);
        bitmap.Erase(SKColors.White);
        using var canvas = new SKCanvas(bitmap);
        using var fill = new SKPaint { Color = new SKColor(0, 117, 255) };
        using var border = new SKPaint { Color = new SKColor(0, 90, 210), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var check = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        canvas.DrawRoundRect(new SKRect(10, 10, 30, 30), 3, 3, fill);
        canvas.DrawRoundRect(new SKRect(10.5f, 10.5f, 29.5f, 29.5f), 3, 3, border);
        if (drawCheck)
        {
            canvas.DrawLine(14, 20, 18, 24, check);
            canvas.DrawLine(18, 24, 26, 15, check);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    private static void WriteRadioFixture(string path, bool drawDot)
    {
        using var bitmap = new SKBitmap(40, 40, SKColorType.Bgra8888, SKAlphaType.Opaque);
        bitmap.Erase(SKColors.White);
        using var canvas = new SKCanvas(bitmap);
        using var fill = new SKPaint { Color = new SKColor(0, 92, 200) };
        using var dot = new SKPaint { Color = SKColors.White };
        canvas.DrawCircle(20, 20, 10, fill);
        if (drawDot) canvas.DrawCircle(20, 20, 4, dot);
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
