using System.Diagnostics;
using System.Text;
using Square.Extensions.CodeEditor;
using Square.Graphics;
using Xunit;
using Xunit.Abstractions;
namespace Square.Extensions.CodeEditor.Tests;

public sealed class LargeDocumentPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public LargeDocumentPerformanceTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Typing_100kLines_NoWrap_P95AtMost50ms() => Measure(wrap: false, lineCount: 100_000);

    [Fact]
    public void Typing_2kLines_Wrap_P95AtMost50ms() => Measure(wrap: true, lineCount: 2_000);

    private void Measure(bool wrap, int lineCount)
    {
        CodeEditorRegistration.RegisterDefaults();
        var pad = new string('x', 70);
        var builder = new StringBuilder(lineCount * 100);
        for (var i = 0; i < lineCount; i++)
        {
            builder.Append("line-");
            builder.Append(i.ToString("D6"));
            builder.Append(" // 中文 ");
            builder.Append(pad);
            builder.Append('\n');
        }

        var editor = new CodeEditor
        {
            Geometry = new Rect(0, 0, 900, 600),
            WordWrap = wrap,
            ShowFolding = false
        };
        editor.Value = builder.ToString();
        editor.SelectRange(editor.Model.Length, editor.Model.Length);
        _output.WriteLine($"lines={lineCount} bytes={editor.Model.Length} wrap={wrap}");

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
        _output.WriteLine($"p95={p95:F2}ms max={measured[^1]:F2}ms");
        Assert.True(p95 <= 50, $"Input p95 {p95:F2}ms exceeded 50ms (wrap={wrap}, lines={lineCount}).");
    }
}
