using Square.FontComparison;
using Xunit;

namespace Square.UI.Tests;

public sealed class ControlComparisonTests
{
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
}