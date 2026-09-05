using System.Diagnostics;
using Square.Extensions.CodeEditor;
using Square.Graphics;
using Square.Runtime;
using Xunit;
using Xunit.Abstractions;
namespace Square.Extensions.CodeEditor.Tests;

public sealed class LargeDocumentPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public LargeDocumentPerformanceTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Typing_100kLines_P95AtMost50ms(bool wrap)
    {
        CodeEditorRegistration.RegisterDefaults();
        var pad = new string('x', 70);
        var lines = Enumerable.Range(0, 100_000).Select(i => $"line-{i:D6} // 中文 {pad}");
        var editor = new CodeEditor
        {
            Geometry = new Rect(0, 0, 900, 600),
            WordWrap = wrap
        };
        ((IComponentLifecycle)editor).OnAttached();
        editor.Value = string.Join("\n", lines);
        editor.SelectRange(editor.Model.Length, editor.Model.Length);

        var samples = new double[120];
        for (var i = 0; i < samples.Length; i++)
        {
            var watch = Stopwatch.StartNew();
            editor.HandleTextInput(i % 7 == 0 ? "汉" : "x");
            watch.Stop();
            samples[i] = watch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        var measured = samples[20..];
        var p95 = measured[(int)(measured.Length * 0.95) - 1];
        _output.WriteLine($"wrap={wrap} p95={p95:F2}ms max={measured[^1]:F2}ms");
        Assert.True(p95 <= 50, $"Input p95 {p95:F2}ms exceeded 50ms (wrap={wrap}).");
    }
}
