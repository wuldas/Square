using System.Reflection;
using System.Text;
using Square.Backends;
using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Square.Text.Glyph;
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
        var font = new Font("Segoe UI", 14);
        var glyph = new SystemGlyphRasterizer().Rasterize(font, 'W');
        var expectedWidth = glyph == null
            ? TextLayout.MeasureRuneAdvance(new Rune('W'), font) * 3
            : glyph.AdvanceX * 3;

        Assert.True(selection.Width >= expectedWidth);
        Assert.True(selection.Left <= input.CaretRect.X - expectedWidth);
        Assert.True(selection.Right >= input.CaretRect.X);
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
        var glyph = new SystemGlyphRasterizer().Rasterize(font, 't');
        var expectedRight = glyph == null
            ? input.CaretRect.X
            : Math.Max(input.CaretRect.X, input.CaretRect.X - glyph.AdvanceX + glyph.OffsetX + glyph.Width);

        Assert.Equal(expectedRight, selection.Right);
        Assert.True(selection.Right >= input.CaretRect.X);
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
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
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
        Assert.True(rightmostForeground <= rightmostBackground);
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
        var glyph = new SystemGlyphRasterizer().Rasterize(fragment.Font, 't');
        var expectedRight = glyph == null
            ? lastCharacter.Bounds.Right
            : Math.Max(lastCharacter.Bounds.Right, lastCharacter.Bounds.X + glyph.OffsetX + glyph.Width);

        Assert.Equal(expectedRight, lastCharacter.SelectionBounds.Right);
        Assert.True(lastCharacter.SelectionBounds.Right >= lastCharacter.Bounds.Right);
    }
}
