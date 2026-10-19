using System;
using System.Collections.Generic;
using Square.Backends;
using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Square.Rendering.Tree;
using Square.UI;
using Square.UI.ElementApi;
using System.Numerics;
using Xunit;

namespace Square.Backends.Tests;

public class DirtyPartialPresentTests
{
    // ---------------------------------------------------------------------------
    // Step4 consumer-pixel contract: every animated/mutated frame is presented and
    // committed; partial frames clear only the reported damage and must match a
    // fresh full rendering of the same scene pixel for pixel, with old locations
    // erased down to the underlying pixels.
    // ---------------------------------------------------------------------------

    [Fact]
    public void WidthRewrapAcrossRowsMovesFollowingSectionAndErasesOldRows()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "column");
        var row = new View();
        row.Style.Set("display", "flex");
        row.Style.Set("flex-wrap", "wrap");
        row.Style.Set("width", "200px");
        var first = NewFlexItem(Color.Red, "90px", "40px");
        var second = NewFlexItem(Color.Blue, "90px", "40px");
        var third = NewFlexItem(Color.Green, "90px", "40px");
        var section = NewFlexItem(Color.FromRgb(255, 165, 0), "auto", "60px");
        row.Children.Add(first);
        row.Children.Add(second);
        row.Children.Add(third);
        root.Children.Add(row);
        root.Children.Add(section);

        var layout = new LayoutEngine();
        Relayout(layout, root, 320, 320);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(320, 320);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);
        AssertPixel(bitmap, 10, 10, Color.Red);
        AssertPixel(bitmap, 150, 10, Color.Blue);
        AssertPixel(bitmap, 10, 50, Color.Green);
        AssertPixel(bitmap, 10, 100, Color.FromRgb(255, 165, 0));

        // Widen: all items share row 1, the section shifts up, old rows are erased.
        row.Style.Set("width", "300px");
        Relayout(layout, root, 320, 320);
        PresentFrame(tree, context, root, Color.White);
        AssertFrameMatchesReference(tree, bitmap, 320, 320, Color.White);
        AssertPixel(bitmap, 10, 50, Color.FromRgb(255, 165, 0));   // old row-2 area now the moved-up section
        AssertPixel(bitmap, 10, 120, Color.White);                  // old section area is empty now
        AssertPixel(bitmap, 190, 10, Color.Green);                  // third item joined row 1

        // Shrink past the wrap point: one item per row, the section shifts down.
        row.Style.Set("width", "140px");
        Relayout(layout, root, 320, 320);
        PresentFrame(tree, context, root, Color.White);
        AssertFrameMatchesReference(tree, bitmap, 320, 320, Color.White);
        AssertPixel(bitmap, 150, 10, Color.White);                  // old row-1 right half erased
        AssertPixel(bitmap, 190, 10, Color.White);
        AssertPixel(bitmap, 10, 90, Color.Green);                   // third item wrapped into its own row
        AssertPixel(bitmap, 10, 130, Color.FromRgb(255, 165, 0));   // section moved down with the reflow
    }

    [Fact]
    public void NestedAnimatedTransformAndOpacityEraseOldLocationsAndMatchFullFrame()
    {
        var root = new View { Geometry = new Rect(0, 0, 300, 220) };
        var underlay = new ColorPaintElement(Color.Green) { Geometry = new Rect(60, 40, 80, 60) };
        var parent = new ColorPaintElement(Color.Blue) { Geometry = new Rect(60, 40, 80, 60) };
        parent.Style.Set("transform", "translate(20px, 10px)");
        var child = new ColorPaintElement(Color.Red) { Geometry = new Rect(10, 10, 40, 30) };
        child.Style.Set("opacity", "0.6");
        child.Style.Set("transform", "rotate(0deg)");
        parent.Children.Add(child);
        root.Children.Add(underlay);
        root.Children.Add(parent);

        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(300, 220);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);
        AssertPixel(bitmap, 85, 55, Color.Blue);

        // Two CSS animated states change at once: the parent translation carries the
        // rotated/scaled child; old parent and child pixels must reveal the underlay.
        parent.Style.SetAnimated("transform", "translate(120px, 80px)");
        child.Style.SetAnimated("transform", "rotate(25deg) scale(1.2)");
        child.Style.SetAnimated("opacity", "0.9");
        PresentFrame(tree, context, root, Color.White);
        AssertFrameMatchesReference(tree, bitmap, 300, 220, Color.White);
        AssertPixel(bitmap, 85, 55, Color.Green);   // old parent area reveals the underlay
        AssertPixel(bitmap, 95, 65, Color.Green);   // old child area reveals the underlay
        AssertPixel(bitmap, 145, 105, Color.FromRgb(255, 25, 25)); // transformed child now covers this old parent area
        AssertPixel(bitmap, 200, 140, Color.Blue);  // translated parent painted at the new location

        // Animating back must restore the first frame exactly.
        parent.Style.SetAnimated("transform", "translate(20px, 10px)");
        child.Style.SetAnimated("transform", "rotate(0deg)");
        child.Style.SetAnimated("opacity", "0.6");
        PresentFrame(tree, context, root, Color.White);
        AssertFrameMatchesReference(tree, bitmap, 300, 220, Color.White);
        AssertPixel(bitmap, 85, 55, Color.Blue);
    }

    [Fact]
    public void AnimatedGroupOpacityOverlappingSiblingsMatchesFullFrame()
    {
        var root = new View { Geometry = new Rect(0, 0, 220, 180) };
        var groupA = new View { Geometry = new Rect(20, 20, 80, 80) };
        groupA.Style.Set("opacity", "0.5");
        groupA.Children.Add(new ColorPaintElement(Color.Red) { Geometry = new Rect(20, 20, 60, 60) });
        groupA.Children.Add(new ColorPaintElement(Color.Blue) { Geometry = new Rect(50, 50, 60, 60) });
        var groupB = new View { Geometry = new Rect(70, 50, 80, 80) };
        groupB.Style.Set("opacity", "0.7");
        groupB.Children.Add(new ColorPaintElement(Color.Green) { Geometry = new Rect(70, 50, 60, 60) });
        root.Children.Add(groupA);
        root.Children.Add(groupB);

        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(220, 180);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);

        // One animated opacity plus one animated transform: the moved group must erase
        // its old area down to what group A (at its new opacity) paints underneath.
        groupA.Style.SetAnimated("opacity", "0.85");
        groupB.Style.SetAnimated("transform", "translate(30px, 20px)");
        PresentFrame(tree, context, root, Color.White);
        AssertFrameMatchesReference(tree, bitmap, 220, 180, Color.White);
        AssertPixel(bitmap, 125, 55, Color.White);                  // old group-B-only area reveals background
        Assert.NotEqual(Color.White, PixelAt(bitmap, 140, 120));    // translated group painted at the new location

        groupA.Style.SetAnimated("opacity", "0.5");
        groupB.Style.SetAnimated("transform", "translate(0px, 0px)");
        PresentFrame(tree, context, root, Color.White);
        AssertFrameMatchesReference(tree, bitmap, 220, 180, Color.White);
        Assert.NotEqual(Color.White, PixelAt(bitmap, 125, 55));     // group B covers the point again
    }

    [Fact]
    public void ScrolledClippedFixedAndPopupMutationsMatchFullFramePixels()
    {
        var root = new View { Geometry = new Rect(0, 0, 260, 200) };
        var scroll = new View { Geometry = new Rect(0, 0, 120, 150) };
        scroll.Style.Set("overflow-y", "auto");
        scroll.Style.Set("scrollbar-width", "none");
        scroll.SetScrollContentSize(new Size(120, 300));
        scroll.Children.Add(new ColorPaintElement(Color.Red) { Geometry = new Rect(0, 0, 120, 60) });
        scroll.Children.Add(new ColorPaintElement(Color.Blue) { Geometry = new Rect(0, 60, 120, 60) });
        scroll.Children.Add(new ColorPaintElement(Color.Green) { Geometry = new Rect(0, 120, 120, 60) });
        scroll.Children.Add(new ColorPaintElement(Color.FromRgb(255, 200, 0)) { Geometry = new Rect(0, 180, 120, 60) });
        scroll.Children.Add(new ColorPaintElement(Color.FromRgb(0, 220, 220)) { Geometry = new Rect(0, 240, 120, 60) });

        var clipWrapper = new View { Geometry = new Rect(140, 10, 100, 60) };
        clipWrapper.Style.Set("overflow", "hidden");
        var purple = new ColorPaintElement(Color.FromRgb(160, 32, 240)) { Geometry = new Rect(160, 40, 60, 60) };
        clipWrapper.Children.Add(purple);

        var fixedElement = new ColorPaintElement(Color.Blue)
        {
            Geometry = new Rect(10, 160, 80, 30)
        };
        fixedElement.Style.Set("position", "fixed");
        fixedElement.Style.Set("top", "160px");
        fixedElement.Style.Set("left", "10px");
        fixedElement.Style.Set("width", "80px");
        fixedElement.Style.Set("height", "30px");

        var menu = new Menu { Geometry = new Rect(0, 0, 90, 50) };
        menu.Style.Set("box-shadow", "none");
        menu.Children.Add(new MenuItem { TextContent = "First", Geometry = new Rect(0, 0, 90, 25) });
        menu.Children.Add(new MenuItem { TextContent = "Second", Geometry = new Rect(0, 25, 90, 25) });

        root.Children.Add(scroll);
        root.Children.Add(clipWrapper);
        root.Children.Add(fixedElement);
        root.Children.Add(menu);
        menu.OpenAt(new Point(150, 120));

        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(260, 200);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);

        scroll.ScrollTop = 100;
        PresentFrame(tree, context, root, Color.White);
        AssertFrameMatchesReference(tree, bitmap, 260, 200, Color.White);
        AssertPixel(bitmap, 10, 10, Color.Blue);                        // content shifted under the viewport top
        AssertPixel(bitmap, 10, 70, Color.Green);
        AssertPixel(bitmap, 10, 130, Color.FromRgb(255, 200, 0));
        AssertPixel(bitmap, 50, 170, Color.Blue);                       // fixed element ignores ancestor scroll

        purple.Arrange(new Rect(160, 60, 60, 60));
        PresentFrame(tree, context, root, Color.White);
        AssertFrameMatchesReference(tree, bitmap, 260, 200, Color.White);
        AssertPixel(bitmap, 200, 50, Color.White);                       // old clipped area erased
        AssertPixel(bitmap, 200, 65, Color.FromRgb(160, 32, 240));       // moved content stays clipped by the wrapper

        fixedElement.Arrange(new Rect(10, 40, 80, 30));
        PresentFrame(tree, context, root, Color.White);
        AssertFrameMatchesReference(tree, bitmap, 260, 200, Color.White);
        AssertPixel(bitmap, 50, 50, Color.Blue);                         // fixed element painted at its new spot
        AssertPixel(bitmap, 50, 170, Color.White);                      // old fixed area lies below the clipped scroll viewport

        menu.Close();
        PresentFrame(tree, context, root, Color.White, expectFullFrame: null);
        AssertFrameMatchesReference(tree, bitmap, 260, 200, Color.White);
        AssertPixel(bitmap, 200, 140, Color.White);                      // popup surface erased
    }

    [Fact]
    public void StructuralChangesDisplayToggleAndResizeFallBackToFullFrame()
    {
        var root = new View { Geometry = new Rect(0, 0, 200, 150) };
        root.Style.Set("overflow", "hidden");
        var underlay = new ColorPaintElement(Color.Green) { Geometry = new Rect(10, 10, 180, 130) };
        var itemA = new ColorPaintElement(Color.Red) { Geometry = new Rect(20, 20, 50, 50) };
        root.Children.Add(underlay);
        root.Children.Add(itemA);

        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(200, 150);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);

        var itemB = new ColorPaintElement(Color.Blue) { Geometry = new Rect(90, 20, 50, 50) };
        root.Children.Add(itemB);
        Assert.True(tree.NeedsSynchronization(root));
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);
        AssertFrameMatchesReference(tree, bitmap, 200, 150, Color.White);
        AssertPixel(bitmap, 95, 25, Color.Blue);

        itemA.Style.Set("display", "none");
        Assert.True(tree.NeedsSynchronization(root));
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);
        AssertFrameMatchesReference(tree, bitmap, 200, 150, Color.White);
        AssertPixel(bitmap, 25, 25, Color.Green);   // hidden element reveals the underlay

        itemA.Style.Set("display", "block");
        Assert.True(tree.NeedsSynchronization(root));
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);
        AssertFrameMatchesReference(tree, bitmap, 200, 150, Color.White);
        AssertPixel(bitmap, 25, 25, Color.Red);

        root.Children.Remove(itemB);
        Assert.True(tree.NeedsSynchronization(root));
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);
        AssertFrameMatchesReference(tree, bitmap, 200, 150, Color.White);
        AssertPixel(bitmap, 95, 25, Color.Green);

        // Resize forces a full frame and the shrunken clip erases the bottom strip.
        root.Arrange(new Rect(0, 0, 260, 110));
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);
        AssertFrameMatchesReference(tree, bitmap, 200, 150, Color.White);
        AssertPixel(bitmap, 100, 120, Color.White);
        AssertPixel(bitmap, 100, 100, Color.Green);

        root.Arrange(new Rect(0, 0, 200, 150));
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);
        AssertFrameMatchesReference(tree, bitmap, 200, 150, Color.White);
        AssertPixel(bitmap, 100, 120, Color.Green);
    }

    [Fact]
    public void DeepStructuralChangesFallBackToFullFrameAndEraseRemovedContent()
    {
        var root = new View { Geometry = new Rect(0, 0, 200, 150) };
        root.Style.Set("overflow", "hidden");
        var underlay = new ColorPaintElement(Color.Green) { Geometry = new Rect(10, 10, 180, 130) };
        var branch = new View { Geometry = new Rect(0, 0, 200, 150) };
        var deep = new ColorPaintElement(Color.Red) { Geometry = new Rect(70, 60, 50, 50) };
        branch.Children.Add(deep);
        root.Children.Add(underlay);
        root.Children.Add(branch);

        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(200, 150);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);
        AssertPixel(bitmap, 95, 85, Color.Red);

        // display:none two levels below the root: the change must still demand a full
        // frame, otherwise no node emits the removed area and it is never erased.
        deep.Style.Set("display", "none");
        Assert.True(tree.NeedsSynchronization(root));
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);
        AssertFrameMatchesReference(tree, bitmap, 200, 150, Color.White);
        AssertPixel(bitmap, 95, 85, Color.Green);   // removed element's area reveals the underlay

        deep.Style.Set("display", "block");
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);
        AssertFrameMatchesReference(tree, bitmap, 200, 150, Color.White);
        AssertPixel(bitmap, 95, 85, Color.Red);

        // Detaching a grandchild node must behave the same as hiding it.
        branch.Children.Remove(deep);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);
        AssertFrameMatchesReference(tree, bitmap, 200, 150, Color.White);
        AssertPixel(bitmap, 95, 85, Color.Green);
    }

    [Fact]
    public void RotatedOpacityNodePaintsCornersBeyondAxisAlignedBounds()
    {
        var root = new View { Geometry = new Rect(0, 0, 260, 260) };
        var diamond = new ColorPaintElement(Color.Red) { Geometry = new Rect(100, 100, 60, 60) };
        diamond.Style.Set("transform", "rotate(45deg)");
        diamond.Style.Set("opacity", "0.5");
        root.Children.Add(diamond);

        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(260, 260);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);

        // The rotated square's tips extend ~12px past the untransformed AABB; the
        // PushLayer bounds must cover the transformed extent (pixel (130,92) lies
        // 3px inside the rotated top tip but above the AABB top edge) or these
        // corner pixels are clipped to the background.
        var corner = PixelAt(bitmap, 130, 92);
        Assert.NotEqual(Color.White, corner);
        Assert.True(corner.R > 200 && corner.G < 200 && corner.B < 200,
            $"expected blended red at the rotated tip, got {corner}");
        var center = PixelAt(bitmap, 130, 130);
        Assert.NotEqual(Color.White, center);
    }

    [Fact]
    public void TranslatedAncestorOpacityGroupMapsLayerBoundsExactlyOnce()
    {
        var root = new View { Geometry = new Rect(0, 0, 260, 140) };
        var parent = new View { Geometry = new Rect(20, 20, 40, 40) };
        parent.Style.Set("transform", "translate(100px, 0px)");
        var group = new View { Geometry = new Rect(20, 20, 40, 40) };
        group.Style.Set("opacity", "0.5");
        group.Children.Add(new ColorPaintElement(Color.Red) { Geometry = group.Geometry });
        parent.Children.Add(group);
        root.Children.Add(parent);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(260, 140);
        using var context = new RenderContext(bitmap, 1f);

        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);
        var blendedRed = Color.FromRgb(255, 127, 127);
        AssertPixel(bitmap, 140, 40, blendedRed);
        AssertPixel(bitmap, 40, 40, Color.White);

        parent.Style.SetAnimated("transform", "translate(60px, 0px)");
        PresentFrame(tree, context, root, Color.White);
        AssertFrameMatchesReference(tree, bitmap, 260, 140, Color.White);
        AssertPixel(bitmap, 100, 40, blendedRed);
        AssertPixel(bitmap, 140, 40, Color.White);
    }

    [Fact]
    public void NonFiniteAnimatedTransformFallsBackToUntransformedPaint()
    {
        var root = new View { Geometry = new Rect(0, 0, 200, 120) };
        var panel = new ColorPaintElement(Color.Red) { Geometry = new Rect(60, 30, 60, 40) };
        root.Children.Add(panel);

        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(200, 120);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);
        AssertPixel(bitmap, 90, 50, Color.Red);

        // Interpolation overflow can yield non-finite animated values; the numeric
        // fast path must reject them exactly like the string parser (which cannot
        // parse "NaN") instead of pushing a NaN transform into the pipeline.
        panel.Style.SetAnimatedNumeric("transform", new AnimatedNumericValue(float.NaN, "rotate(", "deg)"));
        PresentFrame(tree, context, root, Color.White);
        AssertFrameMatchesReference(tree, bitmap, 200, 120, Color.White);
        AssertPixel(bitmap, 90, 50, Color.Red);     // painted untransformed at its layout spot
    }

    // ---------------------------------------------------------------------------
    // Damage coverage conversions: dirty rendering must cover command visual bounds
    // beyond element geometry and erase the previous state.
    // ---------------------------------------------------------------------------

    [Fact]
    public void DirtyRenderRepaintsTextVisualBoundsBeyondGeometry()
    {
        var root = new View { Geometry = new Rect(0, 0, 260, 80) };
        var text = new Square.Controls.Text("Shy hippos wander") { Geometry = new Rect(10, 10, 20, 24) };
        root.Children.Add(text);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(260, 80);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);

        text.Arrange(new Rect(150, 30, 20, 24));
        PresentFrame(tree, context, root, Color.White);
        AssertFrameMatchesReference(tree, bitmap, 260, 80, Color.White);
    }

    [Fact]
    public void DirtyRenderRepaintsPathVisualBoundsBeyondGeometry()
    {
        var root = new View { Geometry = new Rect(0, 0, 160, 80) };
        var element = new PathPaintElement { Geometry = new Rect(0, 0, 10, 10) };
        root.Children.Add(element);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(160, 80);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);

        element.Offset = new Point(0, 30);
        element.InvalidatePaint();
        PresentFrame(tree, context, root, Color.White);
        AssertFrameMatchesReference(tree, bitmap, 160, 80, Color.White);
        AssertPixel(bitmap, 70, 25, Color.White);   // old stroke location erased
        AssertPixel(bitmap, 70, 55, Color.Red);     // stroke painted at the new location
    }

    [Fact]
    public void DirtyRenderRepaintsPushClipVisualBounds()
    {
        var root = new View { Geometry = new Rect(0, 0, 220, 80) };
        var element = new ClippedPaintElement { Geometry = new Rect(0, 0, 10, 10) };
        root.Children.Add(element);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(220, 80);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);
        AssertPixel(bitmap, 30, 30, Color.Blue);

        element.ClipRect = new Rect(60, 20, 20, 20);
        element.InvalidatePaint();
        PresentFrame(tree, context, root, Color.White);
        AssertFrameMatchesReference(tree, bitmap, 220, 80, Color.White);
        AssertPixel(bitmap, 30, 30, Color.White);   // previously clipped-in area erased
        AssertPixel(bitmap, 70, 30, Color.Blue);    // new clip window shows content
    }

    [Fact]
    public void DirtyRenderRepaintsPushTransformVisualBounds()
    {
        var root = new View { Geometry = new Rect(0, 0, 200, 100) };
        var element = new TransformedPaintElement { Geometry = new Rect(0, 0, 10, 10) };
        root.Children.Add(element);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(200, 100);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);
        AssertPixel(bitmap, 80, 25, Color.Green);

        element.Offset = new Point(30, 50);
        element.InvalidatePaint();
        PresentFrame(tree, context, root, Color.White);
        AssertFrameMatchesReference(tree, bitmap, 200, 100, Color.White);
        AssertPixel(bitmap, 80, 25, Color.White);   // old transformed area erased
        AssertPixel(bitmap, 40, 55, Color.Green);
    }

    [Fact]
    public void DirtyRenderRepaintsBoxShadowVisualBoundsOnStyleChange()
    {
        var root = new View { Geometry = new Rect(0, 0, 160, 100) };
        var view = new View { Geometry = new Rect(40, 30, 30, 20) };
        view.Style.Set("background", "#ffffff");
        view.Style.Set("box-shadow", "-8px -6px 0 2px #000000");
        root.Children.Add(view);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(160, 100);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);
        AssertPixel(bitmap, 34, 26, Color.Black);

        view.Style.Set("box-shadow", "6px 8px 0 2px #000000");
        PresentFrame(tree, context, root, Color.White);
        AssertFrameMatchesReference(tree, bitmap, 160, 100, Color.White);
        AssertPixel(bitmap, 34, 26, Color.White);   // old shadow ring erased
        AssertPixel(bitmap, 74, 55, Color.Black);   // new shadow ring painted
    }

    [Fact]
    public void DirtyRenderErasesRemovedOutline()
    {
        var root = new View { Geometry = new Rect(0, 0, 100, 80) };
        var view = new View { Geometry = new Rect(30, 20, 20, 20) };
        view.Style.Set("background", "#ffffff");
        view.Style.Set("outline", "4px solid #ff0000");
        root.Children.Add(view);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(100, 80);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);
        AssertPixel(bitmap, 28, 25, Color.Red);

        view.Style.Set("outline", "none");
        PresentFrame(tree, context, root, Color.White);
        AssertFrameMatchesReference(tree, bitmap, 100, 80, Color.White);
        AssertPixel(bitmap, 28, 25, Color.White);   // outline ring erased down to background
    }

    [Fact]
    public void ClosingShadowedMenuErasesSurfaceAndShadow()
    {
        var (root, menu, _, _) = CreatePopupHoverTree(shadowed: true);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(320, 180);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);

        menu.Close();
        PresentFrame(tree, context, root, Color.White, expectFullFrame: null);
        AssertFrameMatchesReference(tree, bitmap, 320, 180, Color.White);
        AssertPixel(bitmap, 100, 40, Color.White);  // menu surface erased
    }

    // ---------------------------------------------------------------------------
    // Scroll / fixed / geometry conversions.
    // ---------------------------------------------------------------------------

    [Fact]
    public void ScrollWithVisibleOverflowXRepaintsOverflowStrip()
    {
        var root = new View { Geometry = new Rect(20, 30, 100, 60) };
        root.Style.Set("overflow-x", "visible");
        root.Style.Set("overflow-y", "auto");
        root.Style.Set("scrollbar-width", "none");
        root.SetScrollContentSize(new Size(85, 160));
        root.Children.Add(new ColorPaintElement(Color.Red) { Geometry = new Rect(20, 30, 130, 60) });
        root.Children.Add(new ColorPaintElement(Color.Blue) { Geometry = new Rect(20, 130, 85, 20) });
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(140, 100);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);
        AssertPixel(bitmap, 125, 80, Color.Red);    // horizontally overflowing pixels render

        root.ScrollTop = 20;
        PresentFrame(tree, context, root, Color.White);
        AssertFrameMatchesReference(tree, bitmap, 140, 100, Color.White);
        AssertPixel(bitmap, 125, 80, Color.White);  // old overflow strip erased
        AssertPixel(bitmap, 125, 50, Color.Red);    // overflow strip follows the scrolled content
    }

    [Fact]
    public void FixedElementStaysPutAndRepaintsWhileContentScrolls()
    {
        var root = new View { Geometry = new Rect(0, 0, 100, 60) };
        root.Style.Set("overflow-y", "auto");
        root.Style.Set("scrollbar-width", "none");
        root.SetScrollContentSize(new Size(100, 200));
        root.Children.Add(new ColorPaintElement(Color.Green) { Geometry = new Rect(0, 0, 100, 60) });
        root.Children.Add(new ColorPaintElement(Color.FromRgb(255, 200, 0)) { Geometry = new Rect(0, 60, 100, 60) });
        root.Children.Add(new ColorPaintElement(Color.FromRgb(128, 128, 128)) { Geometry = new Rect(0, 120, 100, 80) });
        var fixedElement = new ColorPaintElement(Color.Blue)
        {
            Geometry = new Rect(10, 10, 30, 20)
        };
        fixedElement.Style.Set("position", "fixed");
        fixedElement.Style.Set("top", "10px");
        fixedElement.Style.Set("left", "10px");
        fixedElement.Style.Set("width", "30px");
        fixedElement.Style.Set("height", "20px");
        root.Children.Add(fixedElement);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(100, 60);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);

        root.ScrollTop = 40;
        PresentFrame(tree, context, root, Color.White);
        AssertFrameMatchesReference(tree, bitmap, 100, 60, Color.White);
        AssertPixel(bitmap, 15, 15, Color.Blue);                        // fixed element unmoved by scroll
        AssertPixel(bitmap, 60, 40, Color.FromRgb(255, 200, 0));        // content scrolled under it
    }

    [Fact]
    public void GeometryMoveWithConstantCssRotationUsesPreviouslyPresentedBounds()
    {
        var root = new View { Geometry = new Rect(0, 0, 400, 260) };
        var child = new ColorPaintElement(Color.Red) { Geometry = new Rect(40, 40, 100, 50) };
        child.Style.Set("transform", "rotate(90deg)");
        root.Children.Add(child);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(400, 260);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);
        AssertPixel(bitmap, 90, 60, Color.Red);
        child.Arrange(new Rect(180, 140, 100, 50));
        PresentFrame(tree, context, root, Color.White, expectFullFrame: false);
        AssertFrameMatchesReference(tree, bitmap, 400, 260, Color.White);
        AssertPixel(bitmap, 90, 60, Color.White);
        AssertPixel(bitmap, 230, 160, Color.Red);
    }

    [Fact]
    public void GeometryMovePartialRenderErasesOldLocation()
    {
        var root = new View { Geometry = new Rect(0, 0, 200, 100) };
        var child = new ColorPaintElement(Color.Blue) { Geometry = new Rect(10, 20, 30, 40) };
        root.Children.Add(child);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(200, 100);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);

        child.Arrange(new Rect(100, 20, 30, 40));
        PresentFrame(tree, context, root, Color.White);
        AssertFrameMatchesReference(tree, bitmap, 200, 100, Color.White);
        AssertPixel(bitmap, 20, 40, Color.White);       // old location erased
        AssertPixel(bitmap, 110, 40, Color.Blue);       // new location painted
    }

    // ---------------------------------------------------------------------------
    // Converted multi-frame consumers: every present is followed by a commit.
    // ---------------------------------------------------------------------------

    [Fact]
    public void PopupHoverDirtyRenderMatchesFullFramePixels()
    {
        var (root, menu, first, second) = CreatePopupHoverTree();
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(320, 180);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);

        first.SetState(ElementState.Hover, true);
        PresentFrame(tree, context, root, Color.White);

        first.SetState(ElementState.Hover, false);
        second.SetState(ElementState.Hover, true);
        PresentFrame(tree, context, root, Color.White);

        AssertFrameMatchesReference(tree, bitmap, 320, 180, Color.White);
    }

    [Fact]
    public void ScrolledDirtyRenderMatchesFullFramePixels()
    {
        var root = CreateScrolledPixelTree();
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(100, 60);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);

        root.ScrollTop = 80;
        PresentFrame(tree, context, root, Color.White);

        AssertFrameMatchesReference(tree, bitmap, 100, 60, Color.White);
    }

    [Fact]
    public void ScrolledDirtyRenderWithScrollbarMatchesFullFramePixels()
    {
        var root = CreateScrolledPixelTreeWithScrollbar();
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(100, 60);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);

        root.ScrollTop = 80;
        PresentFrame(tree, context, root, Color.White);

        AssertFrameMatchesReference(tree, bitmap, 100, 60, Color.White);
    }

    [Fact]
    public void ScrollingBackToZeroDirtyRenderMatchesFullFramePixels()
    {
        var root = CreateScrolledPixelTreeWithScrollbar();
        root.ScrollTop = 80;
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(100, 60);
        using var context = new RenderContext(bitmap, 1f);
        PresentFrame(tree, context, root, Color.White, expectFullFrame: true);

        root.ScrollTop = 0;
        PresentFrame(tree, context, root, Color.White);

        AssertFrameMatchesReference(tree, bitmap, 100, 60, Color.White);
    }

    // ---------------------------------------------------------------------------
    // Retained behavior that stays: command-collection invalidation, present
    // delegate contract, culling, synchronization, hit testing.
    // ---------------------------------------------------------------------------

    [Fact]
    public void PaintInvalidationRaisedDuringCommandCollectionIsPreserved()
    {
        var element = new ReinvalidatingElement();
        element.ClearPaintDirty();
        element.InvalidatePaint();
        var node = new DisplayNode { Element = element };

        node.RebuildCommands();

        Assert.True(element.NeedsPaint);
    }

    [Fact]
    public void ClearRectOnlyTouchesPixelsInsideRect()
    {
        var bmp = new Bitmap(10, 10);
        var ctx = new RenderContext(bmp, 1f);
        ctx.Clear(Color.Black);
        ctx.Clear(Color.White, new Rect(2, 2, 3, 3));

        // Outside remains black (BGRA: B=0,G=0,R=0,A=255)
        Assert.Equal(0, bmp.Pixels[0]);
        Assert.Equal(255, bmp.Pixels[3]);

        // Inside (2,2) is white premultiplied
        var idx = 2 * bmp.Stride + 2 * 4;
        Assert.Equal(255, bmp.Pixels[idx]);     // B
        Assert.Equal(255, bmp.Pixels[idx + 1]); // G
        Assert.Equal(255, bmp.Pixels[idx + 2]); // R
        Assert.Equal(255, bmp.Pixels[idx + 3]); // A
    }

    [Fact]
    public void ClearRectAtFractionalDpiDoesNotEscapeMatchingClip()
    {
        var bmp = new Bitmap(12, 12);
        var ctx = new RenderContext(bmp, new Size(8, 8), 1.5f);
        ctx.Clear(Color.Black);

        var dirty = new Rect(1, 1, 4, 4);
        ctx.Clear(Color.White, dirty);
        ctx.PushClip(dirty);
        ctx.FillRect(new Rect(0, 0, 8, 8), new SolidColorBrush(Color.Red));
        ctx.PopClip();

        var outside = 3 * bmp.Stride + 7 * 4;
        Assert.Equal(0, bmp.Pixels[outside]);
        Assert.Equal(0, bmp.Pixels[outside + 1]);
        Assert.Equal(0, bmp.Pixels[outside + 2]);
        Assert.Equal(255, bmp.Pixels[outside + 3]);
    }

    [Fact]
    public void PresentEmptyDirtyListIsNoOp()
    {
        var calls = 0;
        var bmp = new Bitmap(4, 4);
        var ctx = new RenderContext(bmp, 1f, (_, _) => calls++);
        ctx.Present(Array.Empty<Rect>());
        Assert.Equal(0, calls);
    }

    [Fact]
    public void PresentNullDirtyMeansFullWindow()
    {
        var calls = 0;
        IReadOnlyList<Rect>? received = new List<Rect>(); // sentinel non-null
        var bmp = new Bitmap(4, 4);
        var ctx = new RenderContext(bmp, 1f, (_, dirty) =>
        {
            calls++;
            received = dirty;
        });
        ctx.Present();
        Assert.Equal(1, calls);
        Assert.Null(received);
    }

    [Fact]
    public void DisplayTreeSynchronizationReusesUnchangedNodesAndBuildsNewNodes()
    {
        var root = new View { Geometry = new Rect(0, 0, 100, 100) };
        var existing = new CountingPaintElement { Geometry = new Rect(0, 0, 20, 20) };
        root.Children.Add(existing);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        var existingPaintCount = existing.PaintCount;

        var added = new CountingPaintElement { Geometry = new Rect(20, 0, 20, 20) };
        root.Children.Add(added);
        tree.Synchronize(root);

        Assert.Equal(existingPaintCount, existing.PaintCount);
        Assert.Equal(1, added.PaintCount);
    }

    [Fact]
    public void DisplayTreeSynchronizationPreservesDomOrderForEqualZIndex()
    {
        var root = new View { Geometry = new Rect(0, 0, 20, 20) };
        var red = new ColorPaintElement(Color.Red) { Geometry = root.Geometry };
        var blue = new ColorPaintElement(Color.Blue) { Geometry = root.Geometry };
        root.Children.Add(red);
        root.Children.Add(blue);
        var tree = new DisplayTree();
        tree.BuildFrom(root);

        root.Children.Move(1, 0);
        tree.Synchronize(root);
        var bitmap = new Bitmap(20, 20);
        tree.Render(new RenderContext(bitmap, 1f));

        var pixel = 10 * bitmap.Stride + 10 * 4;
        Assert.Equal(0, bitmap.Pixels[pixel]);
        Assert.Equal(0, bitmap.Pixels[pixel + 1]);
        Assert.Equal(255, bitmap.Pixels[pixel + 2]);
        Assert.Equal(255, bitmap.Pixels[pixel + 3]);
    }

    [Fact]
    public void DisplayTreeRebuildsCommandsWhenArrangeMovesCleanElement()
    {
        var root = new View { Geometry = new Rect(0, 0, 100, 100) };
        var child = new ColorPaintElement(Color.Blue) { Geometry = new Rect(0, 0, 20, 20) };
        root.Children.Add(child);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        tree.Render(new RenderContext(new Bitmap(100, 100), 1f));

        child.Arrange(new Rect(0, 50, 20, 20));
        tree.UpdateDirty();
        var bitmap = new Bitmap(100, 100);
        tree.Render(new RenderContext(bitmap, 1f));

        AssertPixel(bitmap, 10, 10, Color.Transparent);
        AssertPixel(bitmap, 10, 60, Color.Blue);
    }

    [Fact]
    public void RenderContextAppliesPushTransformToFillRect()
    {
        var bmp = new Bitmap(40, 30);
        var ctx = new RenderContext(bmp, 1f);
        ctx.Clear(Color.Transparent);

        ctx.PushTransform(Matrix3x2.CreateTranslation(10, 5));
        ctx.FillRect(new Rect(0, 0, 4, 4), Brush.FromColor(Color.Red));
        ctx.PopTransform();
        ctx.FillRect(new Rect(0, 0, 2, 2), Brush.FromColor(Color.Blue));

        AssertPixel(bmp, 11, 6, Color.Red);
        AssertPixel(bmp, 1, 1, Color.Blue);
        AssertPixel(bmp, 5, 5, Color.Transparent);
    }

    [Fact]
    public void DirtyRenderSkipsNodesOutsideDirtyClip()
    {
        var root = new View { Geometry = new Rect(0, 0, 200, 100) };
        var inside = new CountingPaintElement { Geometry = new Rect(10, 10, 20, 20) };
        var outside = new CountingPaintElement { Geometry = new Rect(150, 10, 20, 20) };
        root.Children.Add(inside);
        root.Children.Add(outside);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var context = new CountingRenderContext();

        tree.Render(context, new Rect(8, 8, 24, 24));

        Assert.Equal(1, context.FillCount);
    }

    [Fact]
    public void DisjointDirtyRenderDoesNotDrawNodesBetweenRegions()
    {
        var root = new View { Geometry = new Rect(0, 0, 300, 100) };
        root.Children.Add(new CountingPaintElement { Geometry = new Rect(10, 10, 20, 20) });
        root.Children.Add(new CountingPaintElement { Geometry = new Rect(140, 10, 20, 20) });
        root.Children.Add(new CountingPaintElement { Geometry = new Rect(270, 10, 20, 20) });
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var context = new CountingRenderContext();

        tree.Render(context, [new Rect(8, 8, 24, 24), new Rect(268, 8, 24, 24)]);

        Assert.Equal(2, context.FillCount);
    }

    [Fact]
    public void DirtyRenderUsesViewportCoordinatesForScrolledChildren()
    {
        var root = new View { Geometry = new Rect(0, 0, 100, 60) };
        root.Style.Set("overflow-y", "auto");
        root.Style.Set("scrollbar-width", "none");
        root.SetScrollContentSize(new Size(100, 160));
        var child = new CountingPaintElement { Geometry = new Rect(0, 100, 100, 20) };
        root.Children.Add(child);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var context = new CountingRenderContext();

        root.ScrollTop = 80;
        tree.UpdateDirty();
        tree.Render(context, new Rect(0, 20, 100, 20));

        Assert.Equal(1, context.FillCount);
    }

    [Fact]
    public void FixedHitTestRunsOutsideAncestorScrollMappingAndNormalLayerSkipsIt()
    {
        var (root, fixedElement) = CreateFixedScrollTree();
        root.ScrollTop = 40;
        var tree = new DisplayTree();
        tree.BuildFrom(root);

        Assert.Same(fixedElement, tree.HitTestFixed(new Point(10, 15)));
        Assert.NotSame(fixedElement, tree.HitTestRoot(new Point(10, 15)));
    }

    // ---------------------------------------------------------------------------
    // Frame harness: the step4 presented-frame contract.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Presents one frame under the step4 contract: synchronize structure when the
    /// topology changed, update dirty, then either render full (RequiresFullFrame) or
    /// clear only the reported damage and replay it, always calling
    /// <see cref="DisplayTree.CommitPresentedFrame"/> after the actual Present.
    /// </summary>
    /// <param name="expectFullFrame">
    /// true asserts the tree demanded a full frame, false asserts a partial frame was
    /// possible, null skips the assertion (e.g. popup close frames are not pinned).
    /// </param>
    private static void PresentFrame(
        DisplayTree tree,
        RenderContext context,
        Element root,
        Color background,
        bool? expectFullFrame = false)
    {
        if (tree.NeedsSynchronization(root)) tree.Synchronize(root);
        tree.UpdateDirty();
        if (expectFullFrame.HasValue)
            Assert.Equal(expectFullFrame.Value, tree.RequiresFullFrame);
        if (tree.RequiresFullFrame)
        {
            context.Clear(background);
            tree.Render(context);
            context.Present();
            tree.CommitPresentedFrame();
        }
        else
        {
            var dirty = tree.CollectDirtyRects(context.CanvasSize);
            foreach (var rect in dirty) context.Clear(background, rect);
            tree.Render(context, dirty);
            context.Present(dirty);
            tree.CommitPresentedFrame();
        }
    }

    private static Bitmap RenderReferenceFrame(DisplayTree tree, int width, int height, Color background)
    {
        var bitmap = new Bitmap(width, height);
        var context = new RenderContext(bitmap, 1f);
        context.Clear(background);
        tree.Render(context);
        return bitmap;
    }

    private static void AssertFrameMatchesReference(
        DisplayTree tree,
        Bitmap actual,
        int width,
        int height,
        Color background)
    {
        using var expected = RenderReferenceFrame(tree, width, height, background);
        AssertBitmapEqual(expected, actual);
    }

    private static void AssertBitmapEqual(Bitmap expected, Bitmap actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        for (var i = 0; i < expected.Pixels.Length; i++)
        {
            if (expected.Pixels[i] == actual.Pixels[i]) continue;
            var pixel = i / 4;
            var x = pixel % expected.Width;
            var y = pixel / expected.Width;
            var exp = expected.Pixels.AsSpan(i - i % 4, 4).ToArray();
            var act = actual.Pixels.AsSpan(i - i % 4, 4).ToArray();
            throw new Xunit.Sdk.XunitException(
                $"Bitmap mismatch at byte {i} (pixel ({x},{y})): expected BGRA [{string.Join(",", exp)}] actual BGRA [{string.Join(",", act)}]");
        }
    }

    private static void Relayout(LayoutEngine layout, Element root, float width, float height)
    {
        layout.Measure(root, new Size(width, height));
        layout.Arrange(root, new Rect(0, 0, width, height));
    }

    private static UIElement NewFlexItem(Color color, string width, string height)
    {
        var item = new ColorPaintElement(color);
        item.Style.Set("flex", "none");
        item.Style.Set("width", width);
        item.Style.Set("height", height);
        return item;
    }

    private static View CreateScrolledPixelTree()
    {
        var root = new View { Geometry = new Rect(0, 0, 100, 60) };
        root.Style.Set("overflow-y", "auto");
        root.Style.Set("scrollbar-width", "none");
        root.SetScrollContentSize(new Size(100, 160));
        root.Children.Add(new ColorPaintElement(Color.Red) { Geometry = new Rect(0, 0, 100, 60) });
        root.Children.Add(new ColorPaintElement(Color.Blue) { Geometry = new Rect(0, 100, 100, 20) });
        return root;
    }

    private static View CreateScrolledPixelTreeWithScrollbar()
    {
        var root = new View { Geometry = new Rect(0, 0, 100, 60) };
        root.Style.Set("overflow-y", "auto");
        root.SetScrollContentSize(new Size(85, 160));
        root.Children.Add(new ColorPaintElement(Color.Red) { Geometry = new Rect(0, 0, 85, 60) });
        root.Children.Add(new ColorPaintElement(Color.Blue) { Geometry = new Rect(0, 100, 85, 20) });
        return root;
    }

    private static (View Root, Menu Menu, MenuItem First, MenuItem Second) CreatePopupHoverTree(bool shadowed = false)
    {
        var root = new View { Geometry = new Rect(0, 0, 320, 180) };
        var menu = new Menu { Geometry = new Rect(0, 0, 160, 64) };
        menu.Style.Set("box-shadow", shadowed
            ? "-8px -4px 0 2px rgba(0,0,0,0.5), 0 6px 12px rgba(0,0,0,0.5)"
            : "none");
        var first = new MenuItem { TextContent = "First", Geometry = new Rect(0, 0, 160, 32) };
        var second = new MenuItem { TextContent = "Second", Geometry = new Rect(0, 32, 160, 32) };
        menu.Children.Add(first);
        menu.Children.Add(second);
        root.Children.Add(menu);
        menu.OpenAt(new Point(90, 30));
        return (root, menu, first, second);
    }

    private static (View Root, View FixedElement) CreateFixedScrollTree()
    {
        var root = new View();
        root.Style.Set("display", "block");
        root.Style.Set("overflow-y", "auto");
        root.Style.Set("scrollbar-width", "none");
        var normal = new View();
        normal.Style.Set("display", "block");
        normal.Style.Set("height", "100px");
        var fixedElement = new View();
        fixedElement.Style.Set("display", "block");
        fixedElement.Style.Set("position", "fixed");
        fixedElement.Style.Set("top", "10px");
        fixedElement.Style.Set("width", "30px");
        fixedElement.Style.Set("height", "20px");
        fixedElement.Style.Set("background", "#ff0000");
        root.Children.Add(normal);
        root.Children.Add(fixedElement);
        var layout = new LayoutEngine();
        layout.Measure(root, new Size(100, 50));
        layout.Arrange(root, new Rect(0, 0, 100, 50));
        return (root, fixedElement);
    }

    private sealed class PathPaintElement : UIElement
    {
        public Point Offset { get; set; }

        public override void Paint(IRenderContext ctx)
        {
            ctx.DrawPath(
                PathGeometry.Create()
                    .MoveTo(new Point(50 + Offset.X, 20 + Offset.Y))
                    .LineTo(new Point(90 + Offset.X, 30 + Offset.Y)),
                Pen.FromColor(Color.Red, 2));
        }
    }

    private sealed class CountingPaintElement : UIElement
    {
        public int PaintCount { get; private set; }

        public override void Paint(IRenderContext ctx)
        {
            PaintCount++;
            ctx.FillRect(Geometry, Brush.FromColor(Color.Red));
        }
    }

    private sealed class ColorPaintElement(Color color) : UIElement
    {
        public override void Paint(IRenderContext ctx) => ctx.FillRect(Geometry, Brush.FromColor(color));
    }

    private sealed class ReinvalidatingElement : UIElement
    {
        public override void Paint(IRenderContext ctx) => InvalidatePaint();
    }

    private sealed class CountingRenderContext : IRenderContext
    {
        public int FillCount { get; private set; }
        public Size CanvasSize => new(200, 100);
        public float DpiScale => 1;
        public void FillRect(Rect rect, Brush brush) => FillCount++;
        public void PushTransform(Matrix3x2 matrix) { }
        public void PopTransform() { }
        public void PushClip(Rect rect) { }
        public void PushClip(Geometry geometry) { }
        public void PopClip() { }
        public void DrawRect(Rect rect, Pen pen) { }
        public void FillPath(PathGeometry path, Brush brush) { }
        public void DrawPath(PathGeometry path, Pen pen) { }
        public void FillGeometry(Geometry geometry, Brush brush) { }
        public void DrawGeometry(Geometry geometry, Pen pen) { }
        public void DrawText(TextLayout text, Point origin, Brush brush) { }
        public void DrawImage(Square.Graphics.Image image, Rect dest, Rect? source = null) { }
        public void PushLayer(Rect bounds, float opacity) { }
        public void PopLayer() { }
        public void Clear(Color color) { }
        public void Clear(Color color, Rect rect) { }
        public void Flush() { }
        public void Present() { }
        public void Present(IReadOnlyList<Rect>? dirtyRects) { }
        public void Dispose() { }
    }

    private static void AssertPixel(Bitmap bmp, int x, int y, Color color)
    {
        var idx = y * bmp.Stride + x * 4;
        Assert.Equal(color.B, bmp.Pixels[idx]);
        Assert.Equal(color.G, bmp.Pixels[idx + 1]);
        Assert.Equal(color.R, bmp.Pixels[idx + 2]);
        Assert.Equal(color.A, bmp.Pixels[idx + 3]);
    }

    private static Color PixelAt(Bitmap bmp, int x, int y)
    {
        var idx = y * bmp.Stride + x * 4;
        return Color.FromRgba(bmp.Pixels[idx + 2], bmp.Pixels[idx + 1], bmp.Pixels[idx], bmp.Pixels[idx + 3]);
    }

    private sealed class ClippedPaintElement : UIElement
    {
        public Rect ClipRect { get; set; } = new(20, 20, 20, 20);

        public override void Paint(IRenderContext ctx)
        {
            ctx.PushClip(ClipRect);
            ctx.FillRect(new Rect(20, 20, 160, 40), Brush.FromColor(Color.Blue));
            ctx.PopClip();
        }
    }

    private sealed class TransformedPaintElement : UIElement
    {
        public Point Offset { get; set; } = new(70, 20);

        public override void Paint(IRenderContext ctx)
        {
            ctx.PushTransform(Matrix3x2.CreateTranslation(Offset.X, Offset.Y));
            ctx.FillRect(new Rect(0, 0, 20, 10), Brush.FromColor(Color.Green));
            ctx.PopTransform();
        }
    }
}
