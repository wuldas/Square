using System.Reflection;
using System.Text;
using Square.Backends;
using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Square.Text;
using Xunit;

namespace Square.Backends.Tests;

public class TextSelectionMeasurementTests
{
    [Fact]
    public void TextEditorSelectionWidthUsesRenderedGlyphAdvances()
    {
        var input = new Input { Geometry = new Rect(0, 0, 220, 44), Value = "WWW" };
        input.Style.Set("font-size", "14px");
        input.Focus();
        input.SelectAll();

        var getSelectionRects = typeof(TextEditorBase).GetMethod(
            "GetSelectionRects",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var selection = Assert.Single((List<Rect>)getSelectionRects.Invoke(input, [input.Value])!);
        var font = FontManager.Instance.FromCss("sans-serif", "14px", null, null, 14);
        var glyph = TextMetrics.GetGlyphMetrics(font, new Rune('W'));
        var logicalWidth = glyph.AdvanceX * 3;
        var leftOverhang = Math.Max(0, -glyph.InkBounds.Left);
        var rightOverhang = Math.Max(0, glyph.InkBounds.Right - glyph.AdvanceX);
        var logicalCaretX = selection.Left + leftOverhang + logicalWidth;

        Assert.InRange(
            Math.Abs(selection.Width - (logicalWidth + leftOverhang + rightOverhang)),
            0,
            0.0001f);
        Assert.InRange(Math.Abs((selection.Right - logicalCaretX) - rightOverhang), 0, 0.0001f);
        Assert.InRange(Math.Abs(input.CaretRect.X - logicalCaretX), 0, 0.5001f);
    }

    [Fact]
    public void TextEditorSelectionCoversLastGlyphInkPastCaret()
    {
        var input = new TextArea
        {
            Geometry = new Rect(0, 0, 400, 132),
            Value = "Different color and line-height"
        };
        input.Style.Set("font-size", "14px");
        input.Focus();
        input.SelectAll();

        var getSelectionRects = typeof(TextEditorBase).GetMethod(
            "GetSelectionRects",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var selection = Assert.Single((List<Rect>)getSelectionRects.Invoke(input, [input.Value])!);
        var font = new Font("Segoe UI", 14);
        var glyph = TextMetrics.GetGlyphMetrics(font, new Rune('t'));
        var overhang = Math.Max(0, glyph.InkBounds.Right - glyph.AdvanceX);
        var actualOverhang = selection.Right - input.CaretRect.X;

        Assert.InRange(actualOverhang, overhang - 0.5001f, overhang + 0.5001f);
        Assert.True(selection.Right >= input.CaretRect.X - 0.5001f);
    }

    [Fact]
    public void HighDpiTextRenderingStaysInsideSelectionBackground()
    {
        var input = new TextArea
        {
            Geometry = new Rect(0, 0, 400, 132),
            Value = "Different color and line-height"
        };
        input.Style.Set("font-size", "14px");
        input.Style.Set("line-height", "30px");
        input.Style.Set("selection-background", "#b692f6");
        input.Style.Set("selection-color", "#1f1235");
        input.Focus();
        input.SelectAll();

        var bitmap = new Bitmap(600, 198);
        using var context = new RenderContext(bitmap, new Size(400, 132), 1.5f);
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(input);
        tree.Render(context);

        var rightmostBackground = -1;
        var rightmostForeground = -1;
        var chromeInset = (int)MathF.Ceiling(4 * 1.5f);
        for (var y = chromeInset; y < bitmap.Height - chromeInset; y++)
        {
            // Exclude the control's focus border; this assertion concerns text ink.
            for (var x = chromeInset; x < bitmap.Width - chromeInset; x++)
            {
                var index = y * bitmap.Stride + x * 4;
                var b = bitmap.Pixels[index];
                var g = bitmap.Pixels[index + 1];
                var r = bitmap.Pixels[index + 2];
                if (b == 0xf6 && g == 0x92 && r == 0xb6) rightmostBackground = Math.Max(rightmostBackground, x);
                if (r < 80 && g < 80 && b < 100) rightmostForeground = Math.Max(rightmostForeground, x);
            }
        }

        Assert.True(rightmostBackground >= 0);
        Assert.True(rightmostForeground <= rightmostBackground,
            $"Foreground reached x={rightmostForeground}, selection background reached x={rightmostBackground}.");
    }

    [Fact]
    public void DocumentSelectionBoundsCoverGlyphInkPastAdvance()
    {
        var root = new View { Geometry = new Rect(0, 0, 300, 60) };
        var text = new Square.Controls.Text("Different color and line-height")
        {
            FontSize = 20,
            Geometry = new Rect(0, 0, 300, 30)
        };
        root.Children.Add(text);
        var tree = new DisplayTree();
        tree.BuildFrom(root);

        var fragment = Assert.Single(tree.CollectTextFragments(root));
        var lastCharacter = fragment.Characters[^1];
        var glyph = TextMetrics.GetGlyphMetrics(fragment.Font, new Rune('t'));
        var expectedRight = Math.Max(
            lastCharacter.Bounds.Right,
            lastCharacter.Bounds.X + glyph.InkBounds.Right);

        Assert.InRange(Math.Abs(lastCharacter.SelectionBounds.Right - expectedRight), 0, 0.0001f);
        Assert.True(lastCharacter.SelectionBounds.Right >= lastCharacter.Bounds.Right);
    }

    [Fact]
    public void DisplayTextFragmentsUseVisualOrderAndPreserveLogicalOffsets()
    {
        var root = new View { Geometry = new Rect(0, 0, 300, 60) };
        var text = new Square.Controls.Text("A אבג 123")
        {
            FontSize = 20,
            Geometry = new Rect(0, 0, 300, 30)
        };
        root.Children.Add(text);
        var tree = new DisplayTree();
        tree.BuildFrom(root);

        var fragment = Assert.Single(tree.CollectTextFragments(root));
        Assert.Equal(new[] { 0, 1, 4, 3, 2, 5, 6, 7, 8 },
            fragment.Characters.Select(character => character.StartOffset));

        var rtlCharacter = fragment.Characters[2];
        Assert.Equal(5, fragment.HitTestOffset(new Point(rtlCharacter.Bounds.Left + 0.1f, rtlCharacter.Bounds.Y + 1)));
        Assert.Equal(4, fragment.HitTestOffset(new Point(rtlCharacter.Bounds.Right - 0.1f, rtlCharacter.Bounds.Y + 1)));
    }
}
