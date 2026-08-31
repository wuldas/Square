using Square.Backends;
using Square.CSS.Engine;
using Square.CSS.Tokenizer;
using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Square.UI;
using Xunit;

namespace Square.CSS.Tests;

public sealed class AppearanceCssTests
{
    [Fact]
    public void UserAgentStylesGiveFormControlsAppearanceAuto()
    {
        var engine = new CssEngine();
        var button = new Button("Save");
        var input = new Input();
        var view = new View();

        engine.ApplyStyles(button);
        engine.ApplyStyles(input);
        engine.ApplyStyles(view);

        Assert.Equal("auto", button.Style.Get("appearance"));
        Assert.Equal("auto", input.Style.Get("appearance"));
        Assert.Equal("ButtonFace", button.Style.Get("background-color"));
        Assert.Null(button.Style.Get("border-radius"));
        Assert.Equal("Field", input.Style.Get("background-color"));
        Assert.Equal("2px", input.Style.Get("border-top-width"));
        Assert.Equal("inset", input.Style.Get("border-top-style"));
        Assert.Null(view.Style.Get("appearance"));
        Assert.Null(view.Style.Get("background"));
    }

    [Fact]
    public void AuthorAppearanceNoneOverridesUserAgentAuto()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(
            "Button { appearance: none; background: #112233; border-radius: 0; border: none; }").Tokenize()).Parse());
        var button = new Button("Save");

        engine.ApplyStyles(button);

        Assert.Equal("none", button.Style.Get("appearance"));
        Assert.Equal("#112233", button.Style.Get("background"));
        Assert.Equal("0", button.Style.Get("border-radius"));
        Assert.Equal("none", button.Style.Get("border-top-style"));
    }

    [Fact]
    public void InheritedAuthorValueDoesNotOverrideDirectUserAgentDeclaration()
    {
        var engine = new CssEngine();
        var parent = new View();
        parent.Style.CssText = "color: red; text-align: left;";
        var button = new Button("Save");
        parent.Children.Add(button);

        engine.ApplyStylesToTree(parent);

        Assert.Equal("ButtonText", button.Style.Get("color"));
        Assert.Equal("center", button.Style.Get("text-align"));
        Assert.False(button.Style.IsAuthorSpecified("color"));
        Assert.False(button.Style.IsAuthorSpecified("text-align"));
    }

    [Fact]
    public void InheritedAuthorFontKeepsDefaultSelectHorizontalInset()
    {
        var engine = new CssEngine();
        var parent = new View();
        parent.Style.Set("font-weight", "700");
        var select = new Select
        {
            Geometry = new Rect(10, 20, 180, 38),
            Options = ["Plan"],
            Value = "Plan"
        };
        parent.Children.Add(select);

        engine.ApplyStylesToTree(parent);

        Assert.True(select.Style.IsAuthorSpecified("font-weight"));
        Assert.Equal(14, select.SelectableTextBounds.X);
        Assert.True(select.SelectableTextBounds.Y > 23);
    }

    [Fact]
    public void SameValuedInlineAuthorPresenceInvalidatesSelectPaint()
    {
        var engine = new CssEngine();
        var select = new Select
        {
            Geometry = new Rect(10, 20, 180, 38),
            Options = ["Plan"],
            Value = "Plan"
        };
        engine.ApplyStyles(select);
        var untouched = select.SelectableTextBounds;
        select.ClearPaintDirty();

        select.Style.Set("border-radius", "0");

        Assert.True(select.Style.IsAuthorSpecified("border-top-left-radius"));
        Assert.True(select.IsPaintFullDirty);
        Assert.Equal(untouched, select.SelectableTextBounds);

        select.ClearPaintDirty();
        select.Style.Remove("border-radius");

        Assert.False(select.Style.IsAuthorSpecified("border-top-left-radius"));
        Assert.True(select.IsPaintFullDirty);
        Assert.Equal(untouched, select.SelectableTextBounds);
    }

    [Fact]
    public void SameValuedRuleAuthorPresenceInvalidatesAfterStyleReplay()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(
            ".author { border-top-left-radius: 0; }").Tokenize()).Parse());
        var select = new Select
        {
            Geometry = new Rect(10, 20, 180, 38),
            Options = ["Plan"],
            Value = "Plan"
        };
        try
        {
            engine.ApplyStylesToTree(select);
            select.ClassList.Add("author");
            select.ClearPaintDirty();

            CssStyleReconciler.Flush();

            Assert.True(select.Style.IsAuthorSpecified("border-top-left-radius"));
            Assert.True(select.IsPaintFullDirty);

            select.ClassList.Remove("author");
            select.ClearPaintDirty();
            CssStyleReconciler.Flush();

            Assert.False(select.Style.IsAuthorSpecified("border-top-left-radius"));
            Assert.True(select.IsPaintFullDirty);
        }
        finally
        {
            CssStyleReconciler.UnregisterScopesForTree(select);
        }
    }

    [Fact]
    public void SameValuedRuleAuthorPresenceInvalidatesDuringFullReapply()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(
            ".author { border-top-left-radius: 0; }").Tokenize()).Parse());
        var select = new Select
        {
            Geometry = new Rect(10, 20, 180, 38),
            Options = ["Plan"],
            Value = "Plan"
        };
        try
        {
            engine.ApplyStylesToTree(select);
            select.ClassList.Add("author");
            select.ClearLayoutDirty();
            select.ClearPaintDirty();

            CssStyleReconciler.ReapplyScopesToTree(select);

            Assert.True(select.Style.IsAuthorSpecified("border-top-left-radius"));
            Assert.True(select.IsPaintFullDirty);
        }
        finally
        {
            CssStyleReconciler.UnregisterScopesForTree(select);
        }
    }

    [Fact]
    public void ImplicitInheritedAuthorProvenanceInvalidatesDescendantDuringFullReapply()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(
            ".author { direction: ltr; }").Tokenize()).Parse());
        var root = new View();
        var select = new Select
        {
            Geometry = new Rect(10, 20, 180, 38),
            Options = ["Plan"],
            Value = "Plan"
        };
        root.Children.Add(select);
        try
        {
            engine.ApplyStylesToTree(root);
            root.ClassList.Add("author");
            root.ClearLayoutDirty();
            root.ClearPaintDirty();
            select.ClearLayoutDirty();
            select.ClearPaintDirty();

            CssStyleReconciler.ReapplyScopesToTree(root);

            Assert.True(select.Style.IsAuthorSpecified("direction"));
            Assert.True(select.IsLayoutDirty);
        }
        finally
        {
            CssStyleReconciler.UnregisterScopesForTree(root);
        }
    }

    [Fact]
    public void AuthorOriginOverridesMoreSpecificDisabledUserAgentRule()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(
            ".author { background: #0d6efd; border-color: #0d6efd; color: #ffffff; }").Tokenize()).Parse());
        var button = new Button("Save") { IsDisabled = true };
        button.ClassList.Add("author");

        engine.ApplyStyles(button);

        Assert.Equal("#0d6efd", button.Style.Get("background-color"));
        Assert.Equal("#0d6efd", button.Style.Get("border-top-color"));
        Assert.Equal("#ffffff", button.Style.Get("color"));
    }

    [Fact]
    public void ButtonInkBoundsIncludeRenderedTextDecorations()
    {
        var button = new Button("ABC");
        button.Style.CssText = "font: 14px Arial; line-height: 17px;";
        var textSize = ControlDrawing.MeasureText(button, button.TextContent, 14f);
        var plain = ControlDrawing.MeasureTextInkBounds(button, button.TextContent, 14f, textSize);

        button.Style.Set("text-decoration", "underline");
        var decorated = ControlDrawing.MeasureTextInkBounds(button, button.TextContent, 14f, textSize);

        Assert.True(decorated.Bottom > plain.Bottom,
            $"Expected underline below glyph ink {plain}, got {decorated}.");
    }

    [Fact]
    public void ButtonInkBoundsUsePaintMaxSizeForNegativeIndentWrapping()
    {
        var button = new Button("ABCDEFGHIJKLMN");
        button.Style.CssText = "font: 14px Arial; line-height: 17px; text-indent: -20px;";
        var textSize = ControlDrawing.MeasureText(button, button.TextContent, 14f);

        var bounded = ControlDrawing.MeasureTextInkBounds(button, button.TextContent, 14f, textSize);
        var unbounded = ControlDrawing.MeasureTextInkBounds(
            button, button.TextContent, 14f, new Size(float.MaxValue, float.MaxValue));

        Assert.True(bounded.Height > unbounded.Height,
            $"Expected bounded paint layout to wrap: bounded={bounded}, unbounded={unbounded}, size={textSize}.");
    }

    [Fact]
    public void ButtonInkBoundsUseRenderedTextAlignmentOrigin()
    {
        var button = new Button("ABC\nA");
        button.Style.CssText = "font: 14px Arial; line-height: 17px; text-align: right;";

        var ink = ControlDrawing.MeasureTextInkBounds(button, button.TextContent, 14f, new Size(100, 40));

        Assert.True(ink.Left > 50, $"Expected right-aligned ink in a 100px layout, got {ink}.");
    }

    [Fact]
    public void ButtonSelectableBoundsMatchPaintRelayout()
    {
        var button = new Button("ABCDEFGHIJKLMN") { Geometry = new Rect(0, 0, 120, 60) };
        button.Style.CssText =
            "appearance: none; font: 14px Arial; line-height: 17px; text-indent: -20px;";
        var maxSize = ControlDrawing.MeasureText(button, button.TextContent, 14f);
        var paintedSize = ControlDrawing.MeasureText(button, button.TextContent, 14f, maxSize);

        Assert.Equal(paintedSize.Height, button.SelectableTextBounds.Height);
    }

    [Fact]
    public void PaintOnlyAuthorPresenceDoesNotChangeButtonIntrinsicMeasure()
    {
        var engine = new CssEngine();
        var button = new Button("Save");
        engine.ApplyStyles(button);
        var untouched = button.Measure(new Size(200, 100));
        button.ClearLayoutDirty();
        button.ClearPaintDirty();

        button.Style.Set("background-color", "ButtonFace");

        Assert.Equal(untouched, button.Measure(new Size(200, 100)));
        Assert.False(button.IsLayoutDirty);
        Assert.True(button.IsPaintFullDirty);

        button.ClearLayoutDirty();
        button.ClearPaintDirty();
        button.Style.Remove("background-color");

        Assert.Equal(untouched, button.Measure(new Size(200, 100)));
        Assert.False(button.IsLayoutDirty);
        Assert.True(button.IsPaintFullDirty);
    }

    [Fact]
    public void NonDefaultTextMetricChangesButtonMeasureWithLayoutInvalidation()
    {
        var engine = new CssEngine();
        var button = new Button("Save");
        engine.ApplyStyles(button);
        var untouched = button.Measure(new Size(200, 100));
        button.ClearLayoutDirty();
        button.ClearPaintDirty();

        button.Style.Set("line-height", "24px");

        Assert.NotEqual(untouched, button.Measure(new Size(200, 100)));
        Assert.True(button.IsLayoutDirty);

        button.ClearLayoutDirty();
        button.ClearPaintDirty();
        button.Style.Remove("line-height");

        Assert.Equal(untouched, button.Measure(new Size(200, 100)));
        Assert.True(button.IsLayoutDirty);
    }

    [Theory]
    [InlineData("font: 13.3333px Arial; font-weight: 400;")]
    [InlineData("font: 13.333300px Arial;")]
    [InlineData("font: 13.3333px \"Arial\";")]
    [InlineData("font: 13.3333px Arial; text-indent: 0px;")]
    [InlineData("font: 13.3333px Arial; letter-spacing: 0px;")]
    [InlineData("font: 13.3333px Arial; word-spacing: 0px;")]
    [InlineData("font: 13.333300px \"Arial\";")]
    public void EquivalentChromiumMetricSpellingsPreserveButtonMeasure(string css)
    {
        var engine = new CssEngine();
        var button = new Button("Save");
        engine.ApplyStyles(button);
        button.Style.CssText = "font: 13.3333px Arial;";
        var baseline = button.Measure(new Size(200, 100));

        button.Style.CssText = css;

        Assert.Equal(baseline, button.Measure(new Size(200, 100)));
    }

    [Fact]
    public void AppearanceMutationInvalidatesButtonLayoutWhenMeasureChanges()
    {
        var engine = new CssEngine();
        var button = new Button("Save");
        engine.ApplyStyles(button);
        var auto = button.Measure(new Size(200, 100));
        button.ClearLayoutDirty();
        button.ClearPaintDirty();

        button.Style.Set("appearance", "none");

        Assert.NotEqual(auto, button.Measure(new Size(200, 100)));
        Assert.True(button.IsLayoutDirty);
    }

    [Fact]
    public void AppearanceNoneButtonDoesNotUseAutoOpticalOffset()
    {
        var button = new Button("Save") { Geometry = new Rect(0, 0.5f, 120, 38) };
        button.Style.CssText =
            "appearance:none; font-size:16px; line-height:24px; padding:6px 12px; border:1px solid transparent;";
        var paintMaxSize = ControlDrawing.MeasureText(button, button.TextContent, 14f);
        var ink = ControlDrawing.MeasureTextInkBounds(button, button.TextContent, 14f, paintMaxSize);
        var top = button.Geometry.Y + 1 + 6;
        var bottom = button.Geometry.Bottom - 1 - 6;
        var expectedY = (top + bottom) / 2f - (ink.Top + ink.Bottom) / 2f;

        Assert.Equal(expectedY, button.SelectableTextBounds.Y);
    }

    [Theory]
    [InlineData("ltr", "start", TextAlignment.Left)]
    [InlineData("ltr", "end", TextAlignment.Right)]
    [InlineData("rtl", "start", TextAlignment.Right)]
    [InlineData("rtl", "end", TextAlignment.Left)]
    public void ButtonLogicalTextAlignmentResolvesAgainstDirection(
        string direction,
        string alignment,
        TextAlignment expected)
    {
        var button = new Button("Long line\nShort");
        button.Style.CssText = $"direction: {direction}; text-align: {alignment};";

        Assert.Equal(expected, ControlDrawing.ResolveTextAlignment(button));
    }

    [Fact]
    public void AppearanceAutoButtonHonorsAuthorTextAlignment()
    {
        var engine = new CssEngine();
        var button = new Button("Save") { Geometry = new Rect(0, 0, 180, 36) };
        button.Style.CssText =
            "width: 180px; height: 36px; padding: 0 10px; border: 0; text-align: left;";

        engine.ApplyStyles(button);

        Assert.InRange(button.SelectableTextBounds.X, 9, 12);
    }

    [Fact]
    public void AppearanceAutoPaintsChromiumRoundedButtonCornersOnSoftwareRenderer()
    {
        var engine = new CssEngine();
        var button = new Button("Save") { Geometry = new Rect(2, 2, 80, 28) };
        engine.ApplyStyles(button);

        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(90, 36)
        });
        context.Clear(Color.FromRgb(255, 0, 0));
        var tree = new DisplayTree();
        tree.BuildFrom(button);
        tree.Render(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var corner = bitmap.GetPixel(2, 2);
        var shoulder = bitmap.GetPixel(3, 2);
        var interior = bitmap.GetPixel(20, 16);
        var topBorder = bitmap.GetPixel(20, 2);
        var topInner = bitmap.GetPixel(20, 3);
        var bottomInner = bitmap.GetPixel(20, 28);
        var bottomBorder = bitmap.GetPixel(20, 29);
        var leftBorder = bitmap.GetPixel(2, 16);
        var leftInner = bitmap.GetPixel(3, 16);
        var rightInner = bitmap.GetPixel(80, 16);
        var rightBorder = bitmap.GetPixel(81, 16);

        Assert.True(interior[2] > 200 && interior[1] > 200 && interior[0] > 200,
            "appearance:auto should fill the button interior from Chrome ButtonFace");
        Assert.True(shoulder[2] < 200 && shoulder[1] < 200 && shoulder[0] < 200,
            "appearance:auto should use Chromium's 1px button corner shoulder");
        Assert.True(topBorder[2] < 180 && topBorder[1] < 180 && topBorder[0] < 180,
            "appearance:auto should align the top border to the outer pixel row");
        Assert.True(topInner[2] > 200 && topInner[1] > 200 && topInner[0] > 200,
            "appearance:auto should keep the inner top row at ButtonFace");
        Assert.True(bottomInner[2] > 200 && bottomInner[1] > 200 && bottomInner[0] > 200,
            "appearance:auto should keep the inner bottom row at ButtonFace");
        Assert.True(bottomBorder[2] < 180 && bottomBorder[1] < 180 && bottomBorder[0] < 180,
            "appearance:auto should align the bottom border to the outer pixel row");
        Assert.True(leftBorder[2] < 180 && leftBorder[1] < 180 && leftBorder[0] < 180,
            "appearance:auto should align the left border to the outer pixel column");
        Assert.True(leftInner[2] > 200 && leftInner[1] > 200 && leftInner[0] > 200,
            "appearance:auto should keep the inner left column at ButtonFace");
        Assert.True(rightInner[2] > 200 && rightInner[1] > 200 && rightInner[0] > 200,
            "appearance:auto should keep the inner right column at ButtonFace");
        Assert.True(rightBorder[2] < 180 && rightBorder[1] < 180 && rightBorder[0] < 180,
            "appearance:auto should align the right border to the outer pixel column");
        for (var y = 0; y < 5; y++)
        for (var x = 0; x < 5; x++)
        {
            var topLeft = bitmap.GetPixel(2 + x, 2 + y);
            Assert.True(topLeft.SequenceEqual(bitmap.GetPixel(81 - x, 2 + y)),
                $"appearance:auto top corners differ at ({x}, {y})");
            Assert.True(topLeft.SequenceEqual(bitmap.GetPixel(2 + x, 29 - y)),
                $"appearance:auto left corners differ at ({x}, {y})");
            Assert.True(topLeft.SequenceEqual(bitmap.GetPixel(81 - x, 29 - y)),
                $"appearance:auto diagonal corners differ at ({x}, {y})");
        }
        Assert.InRange(corner[2], 200, 254);
        Assert.InRange(corner[1], 1, 80);
        Assert.InRange(corner[0], 1, 80);
    }

    [Fact]
    public void AppearanceAutoPaintsButtonText()
    {
        var engine = new CssEngine();
        var button = new Button("Save") { Geometry = new Rect(2, 2, 80, 28) };
        engine.ApplyStyles(button);

        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(90, 36)
        });
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(button);
        tree.Render(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var darkTextPixels = 0;
        var left = int.MaxValue;
        var right = int.MinValue;
        for (var y = 6; y < 28; y++)
        for (var x = 10; x < 74; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel[2] < 160 && pixel[1] < 160 && pixel[0] < 160)
            {
                darkTextPixels++;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
            }
        }

        Assert.True(darkTextPixels > 8,
            $"Expected visible button glyphs, found {darkTextPixels} dark interior pixels.");
        var glyphCenter = (left + right) / 2f;
        var buttonCenter = button.Geometry.X + button.Geometry.Width / 2f;
        Assert.InRange(Math.Abs(glyphCenter - buttonCenter), 0, 2);
    }

    [Fact]
    public void AppearanceAutoAlignsButtonTextLikeChromiumAtIntegerCoordinates()
    {
        var engine = new CssEngine();
        var button = new Button("Clear Cache") { Geometry = new Rect(2, 2, 90, 21) };
        button.Style.CssText = "font: 13.3333px Arial;";
        engine.ApplyStyles(button);

        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(96, 28)
        });
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(button);
        tree.Render(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var rows = new List<int>();
        for (var y = 3; y < 22; y++)
        for (var x = 10; x < 84; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel[2] < 80 && pixel[1] < 80 && pixel[0] < 80)
                rows.Add(y);
        }

        Assert.NotEmpty(rows);
        var inkCenter = (rows.Min() + rows.Max()) / 2f;
        Assert.InRange(inkCenter - button.Geometry.Center.Y, -1.5f, -0.5f);
    }

    [Fact]
    public void AppearanceNoneKeepsAuthorBoxWithoutWidgetRadius()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(
            "Button { appearance: none; background: #112233; border-radius: 0; border: none; }").Tokenize()).Parse());
        var button = new Button("Save") { Geometry = new Rect(2, 2, 80, 28) };
        engine.ApplyStyles(button);

        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(90, 36)
        });
        context.Clear(Color.FromRgb(255, 0, 0));
        var tree = new DisplayTree();
        tree.BuildFrom(button);
        tree.Render(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var corner = bitmap.GetPixel(2, 2);
        Assert.Equal(0x11, corner[2]);
        Assert.Equal(0x22, corner[1]);
        Assert.Equal(0x33, corner[0]);
    }

    [Fact]
    public void ButtonBackgroundPropertyOverridesUserAgentFill()
    {
        var engine = new CssEngine();
        var unset = new Button("Save");
        engine.ApplyStyles(unset);
        Assert.NotEqual(Color.FromRgb(0, 120, 212), unset.Background);

        var button = new Button("Save") { Geometry = new Rect(2, 2, 80, 28) };
        button.Background = Color.FromRgb(0x11, 0x22, 0x33);
        button.Foreground = Color.FromRgb(0x44, 0x55, 0x66);
        engine.ApplyStyles(button);

        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(90, 36)
        });
        context.Clear(Color.FromRgb(255, 0, 0));
        var tree = new DisplayTree();
        tree.BuildFrom(button);
        tree.Render(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var interior = bitmap.GetPixel(20, 16);
        Assert.Equal(0x11, interior[2]);
        Assert.Equal(0x22, interior[1]);
        Assert.Equal(0x33, interior[0]);
        Assert.Equal("#112233", button.Style.Get("background-color"));
        Assert.Equal("#445566", button.Style.Get("color"));
    }

    [Fact]
    public void AppearanceAutoPaintsSelectFieldChromeThroughCssBox()
    {
        var engine = new CssEngine();
        var select = new Select
        {
            Geometry = new Rect(2, 2, 80, 28),
            Placeholder = "",
            Value = ""
        };
        engine.ApplyStyles(select);

        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(90, 36)
        });
        context.Clear(Color.FromRgb(255, 0, 0));
        var tree = new DisplayTree();
        tree.BuildFrom(select);
        tree.Render(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var interior = bitmap.GetPixel(12, 16);
        Assert.True(interior[2] > 200 && interior[1] > 200 && interior[0] > 200,
            "Select appearance:auto should paint Chrome Field chrome through CssBoxPainter");
    }

    [Fact]
    public void InputPlaceholderPaintsGrayInsteadOfFieldText()
    {
        var engine = new CssEngine();
        var input = new Input
        {
            Geometry = new Rect(4, 4, 160, 28),
            Placeholder = "MMMM",
            Value = ""
        };
        engine.ApplyStyles(input);

        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(180, 40)
        });
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(input);
        tree.Render(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var foundGray = false;
        var foundBlack = false;
        for (var y = 12; y < 24; y++)
        {
            for (var x = 16; x < 70; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                var red = pixel[2];
                var green = pixel[1];
                var blue = pixel[0];
                if (red < 40 && green < 40 && blue < 40)
                    foundBlack = true;
                if (red is > 80 and < 180 && green is > 80 and < 180 && blue is > 80 and < 180)
                    foundGray = true;
            }
        }

        Assert.True(foundGray, "empty Input should paint placeholder in gray, not Field chrome");
        Assert.False(foundBlack, "placeholder must not consume UA FieldText black");
    }

    [Fact]
    public void AppearanceNoneDisablesCheckBoxNativeIndicator()
    {
        var engine = new CssEngine();
        var check = new CheckBox
        {
            Geometry = new Rect(4, 4, 120, 24),
            TextContent = "Remember",
            IsChecked = true
        };
        check.Style.Set("appearance", "none");
        check.Style.Set("background", "transparent");
        check.Style.Set("border", "none");
        engine.ApplyStyles(check);

        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(140, 36)
        });
        context.Clear(Color.FromRgb(255, 0, 0));
        var tree = new DisplayTree();
        tree.BuildFrom(check);
        tree.Render(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var indicator = bitmap.GetPixel(12, 16);
        Assert.Equal(255, indicator[2]);
        Assert.Equal(0, indicator[1]);
        Assert.Equal(0, indicator[0]);
    }

    [Theory]
    [InlineData("CheckBox")]
    [InlineData("Radio")]
    public void AppearanceAutoChoiceIndicatorIsVerticallyCenteredWithText(string kind)
    {
        UIElement control = kind switch
        {
            "CheckBox" => new CheckBox { TextContent = "Choice", IsChecked = true },
            "Radio" => new Radio { TextContent = "Choice", IsChecked = true },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        control.Geometry = new Rect(4, 4, 140, 24);
        control.Style.Set("appearance", "auto");
        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(160, 36)
        });
        context.Clear(Color.White);

        control.Paint(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var indicatorRows = new List<int>();
        for (var y = 0; y < 32; y++)
        for (var x = 4; x < 18; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel[2] == 0 && pixel[1] == 117 && pixel[0] == 255)
                indicatorRows.Add(y);
        }
        Assert.NotEmpty(indicatorRows);
        var indicatorCenter = (indicatorRows.Min() + indicatorRows.Max() + 1) / 2f;
        var textBounds = kind == "CheckBox"
            ? ((CheckBox)control).SelectableTextBounds
            : ((Radio)control).SelectableTextBounds;
        var textCenter = textBounds.Y + textBounds.Height / 2f;
        Assert.InRange(Math.Abs(indicatorCenter - textCenter), 0, 1);
    }

    [Fact]
    public void AppearanceAutoCheckBoxUsesCapturedChromiumCheckedFill()
    {
        var engine = new CssEngine();
        var check = new CheckBox
        {
            Geometry = new Rect(4, 4, 120, 24),
            TextContent = "",
            IsChecked = true
        };
        engine.ApplyStyles(check);

        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(140, 36)
        });
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(check);
        tree.Render(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var foundFluentBlue = false;
        var foundChromiumBlue = false;
        for (var y = 6; y < 22; y++)
        {
            for (var x = 4; x < 22; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel[2] == 0 && pixel[1] == 120 && pixel[0] == 212)
                    foundFluentBlue = true;
                if (pixel[2] == 0 && pixel[1] == 117 && pixel[0] == 255)
                    foundChromiumBlue = true;
            }
        }

        Assert.False(foundFluentBlue, "checked CheckBox must not paint Fluent #0078d4");
        Assert.True(foundChromiumBlue, "checked CheckBox should use the captured Chromium #0075ff fill");
    }

    [Fact]
    public void AppearanceAutoCheckBoxFocusUsesOutlineInsteadOfFluentBorder()
    {
        var engine = new CssEngine();
        var check = new CheckBox
        {
            Geometry = new Rect(8, 8, 120, 24),
            TextContent = "",
            IsChecked = false
        };
        check.Focus();
        engine.ApplyStyles(check);

        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(140, 40)
        });
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(check);
        tree.Render(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var foundFluentFocus = false;
        for (var y = 8; y < 26; y++)
        {
            var pixel = bitmap.GetPixel(8, y);
            if (pixel[2] == 0 && pixel[1] == 95 && pixel[0] == 184)
                foundFluentFocus = true;
        }

        Assert.False(foundFluentFocus, "focused CheckBox must not paint Fluent #005fb8 indicator border");
        Assert.Equal("solid", check.Style.Get("outline-style"));
        Assert.Equal("Highlight", check.Style.Get("outline-color"));
    }

    [Fact]
    public void DisabledCheckBoxDoesNotPaintFullHighlightFill()
    {
        var engine = new CssEngine();
        var check = new CheckBox
        {
            Geometry = new Rect(4, 4, 160, 24),
            TextContent = "Remember",
            IsChecked = true,
            IsDisabled = true
        };
        engine.ApplyStyles(check);

        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(180, 36)
        });
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(check);
        tree.Render(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        Assert.True(Color.TryParse("Highlight", out var highlight));
        var foundFullHighlight = false;
        for (var y = 6; y < 22; y++)
        {
            for (var x = 4; x < 22; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel[2] == highlight.R && pixel[1] == highlight.G && pixel[0] == highlight.B && pixel[3] == 255)
                    foundFullHighlight = true;
            }
        }

        Assert.False(foundFullHighlight, "disabled checked CheckBox must not keep opaque Highlight fill");
        Assert.Equal("GrayText", check.Style.Get("color"));
    }

    [Fact]
    [Trait("Category", "WindowsRenderingMetrics")]
    public void SelectPlaceholderPaintsGrayInsteadOfFieldText()
    {
        var engine = new CssEngine();
        var select = new Select
        {
            Geometry = new Rect(4, 4, 160, 28),
            Placeholder = "MMMM",
            Value = ""
        };
        engine.ApplyStyles(select);

        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(180, 40)
        });
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(select);
        tree.Render(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var foundGray = false;
        var foundBlack = false;
        for (var y = 12; y < 24; y++)
        {
            for (var x = 16; x < 70; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                var red = pixel[2];
                var green = pixel[1];
                var blue = pixel[0];
                if (red < 40 && green < 40 && blue < 40)
                    foundBlack = true;
                if (red is > 80 and < 180 && green is > 80 and < 180 && blue is > 80 and < 180)
                    foundGray = true;
            }
        }

        Assert.True(foundGray, "empty Select should paint placeholder in gray, not Field chrome");
        Assert.False(foundBlack, "Select placeholder must not consume UA FieldText black");
    }

    [Fact]
    public void AppearanceNoneHidesSelectArrow()
    {
        var engine = new CssEngine();
        var select = new Select
        {
            Geometry = new Rect(4, 4, 80, 28),
            Placeholder = "",
            Value = ""
        };
        select.Style.Set("appearance", "none");
        select.Style.Set("background", "transparent");
        select.Style.Set("border", "none");
        engine.ApplyStyles(select);

        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(90, 36)
        });
        context.Clear(Color.FromRgb(255, 0, 0));
        var tree = new DisplayTree();
        tree.BuildFrom(select);
        tree.Render(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var foundArrowInk = false;
        for (var y = 10; y < 26; y++)
        {
            for (var x = 60; x < 80; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel[2] != 255 || pixel[1] != 0 || pixel[0] != 0)
                    foundArrowInk = true;
            }
        }

        Assert.False(foundArrowInk, "appearance:none should hide the native Select arrow");
    }

    [Fact]
    public void AppearanceNoneDoesNotPaintFallbackInputFrame()
    {
        var input = new Input
        {
            Geometry = new Rect(4, 4, 80, 24),
            Value = ""
        };
        input.Style.Set("appearance", "none");
        new CssEngine().ApplyStyles(input);

        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(90, 32)
        });
        context.Clear(Color.FromRgb(255, 0, 0));
        input.Paint(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var pixel = bitmap.GetPixel(8, 8);
        Assert.Equal(255, pixel[2]);
        Assert.Equal(0, pixel[1]);
        Assert.Equal(0, pixel[0]);
    }

    [Fact]
    public void DisabledButtonUsesUserAgentTextColor()
    {
        var engine = new CssEngine();
        var button = new Button("Save") { IsDisabled = true, Geometry = new Rect(2, 2, 80, 28) };
        engine.ApplyStyles(button);

        Assert.Equal("rgba(16, 16, 16, 0.3)", button.Style.Get("color"));
        Assert.True(Color.TryParse("rgba(16, 16, 16, 0.3)", out var expected));
        Assert.Equal(expected, ControlDrawing.GetStyledColor(button, "color", Color.White));
    }

    [Fact]
    public void SelectPopupHoverDoesNotUseFluentBlue()
    {
        var engine = new CssEngine();
        var select = new Select
        {
            Geometry = new Rect(10, 10, 160, 28),
            Options = ["Alpha", "Beta"],
            Value = "Alpha"
        };
        engine.ApplyStyles(select);
        select.HandlePointerDown(new Point(20, 20));

        using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(180, 90)
        });
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(select);
        tree.Render(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var foundFluentHover = false;
        var foundSelectedGray = false;
        for (var y = 42; y < 70; y++)
        {
            for (var x = 20; x < 150; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel[2] == 230 && pixel[1] == 242 && pixel[0] == 252)
                    foundFluentHover = true;
                if (pixel[2] == 0xce && pixel[1] == 0xce && pixel[0] == 0xce)
                    foundSelectedGray = true;
            }
        }

        Assert.False(foundFluentHover, "Select popup hover must not paint Fluent #e6f2fc");
        Assert.True(foundSelectedGray, "selected option should use Chrome list-box #cecece");
    }
}
