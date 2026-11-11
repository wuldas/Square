// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGAspectRatioTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YGConfigAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGAspectRatioTest
{
    private static YGSize Measure(Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        return new YGSize
        {
            Width = widthMode == MeasureMode.Exactly ? width : 50,
            Height = heightMode == MeasureMode.Exactly ? height : 50,
        };
    }

    [Fact]
    public void Aspect_ratio_cross_defined()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_main_defined()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_both_dimensions_defined_row()
    {
        var root = YGNodeNew();
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetWidth(root_child0, 100);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_both_dimensions_defined_column()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetWidth(root_child0, 100);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_align_stretch()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_flex_grow()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_flex_shrink()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetHeight(root_child0, 150);
        YGNodeStyleSetFlexShrink(root_child0, 1);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_flex_shrink_2()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetHeightPercent(root_child0, 100);
        YGNodeStyleSetFlexShrink(root_child0, 1);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNew();
        YGNodeStyleSetHeightPercent(root_child1, 100);
        YGNodeStyleSetFlexShrink(root_child1, 1);
        YGNodeStyleSetAspectRatio(root_child1, 1);
        YGNodeInsertChild(root, root_child1, 1);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_basis()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetFlexBasis(root_child0, 50);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_absolute_layout_width_defined()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetPosition(root_child0, YGEdge.Left, 0);
        YGNodeStyleSetPosition(root_child0, YGEdge.Top, 0);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_absolute_layout_height_defined()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetPosition(root_child0, YGEdge.Left, 0);
        YGNodeStyleSetPosition(root_child0, YGEdge.Top, 0);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_with_max_cross_defined()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeStyleSetMaxWidth(root_child0, 40);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_with_max_main_defined()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetMaxHeight(root_child0, 40);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_with_min_cross_defined()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetHeight(root_child0, 30);
        YGNodeStyleSetMinWidth(root_child0, 40);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_with_min_main_defined()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetWidth(root_child0, 30);
        YGNodeStyleSetMinHeight(root_child0, 40);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_double_cross()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeStyleSetAspectRatio(root_child0, 2);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_half_cross()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetHeight(root_child0, 100);
        YGNodeStyleSetAspectRatio(root_child0, 0.5f);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_double_main()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetAspectRatio(root_child0, 0.5f);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_half_main()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetWidth(root_child0, 100);
        YGNodeStyleSetAspectRatio(root_child0, 2);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_with_measure_func()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeSetMeasureFunc(root_child0, Measure);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_width_height_flex_grow_row()
    {
        var root = YGNodeNew();
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 200);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_width_height_flex_grow_column()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_height_as_flex_basis()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNew();
        YGNodeStyleSetHeight(root_child1, 100);
        YGNodeStyleSetFlexGrow(root_child1, 1);
        YGNodeStyleSetAspectRatio(root_child1, 1);
        YGNodeInsertChild(root, root_child1, 1);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(125f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(125f, YGNodeLayoutGetHeight(root_child1));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_width_as_flex_basis()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNew();
        YGNodeStyleSetWidth(root_child1, 100);
        YGNodeStyleSetFlexGrow(root_child1, 1);
        YGNodeStyleSetAspectRatio(root_child1, 1);
        YGNodeInsertChild(root, root_child1, 1);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(75f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(125f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(125f, YGNodeLayoutGetHeight(root_child1));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_overrides_flex_grow_row()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetAspectRatio(root_child0, 0.5f);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_overrides_flex_grow_column()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetAspectRatio(root_child0, 2);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_left_right_absolute()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetPosition(root_child0, YGEdge.Left, 10);
        YGNodeStyleSetPosition(root_child0, YGEdge.Top, 10);
        YGNodeStyleSetPosition(root_child0, YGEdge.Right, 10);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_top_bottom_absolute()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetPosition(root_child0, YGEdge.Left, 10);
        YGNodeStyleSetPosition(root_child0, YGEdge.Top, 10);
        YGNodeStyleSetPosition(root_child0, YGEdge.Bottom, 10);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_width_overrides_align_stretch_row()
    {
        var root = YGNodeNew();
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_height_overrides_align_stretch_column()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_allow_child_overflow_parent_size()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeStyleSetAspectRatio(root_child0, 4);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));

        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_defined_main_with_margin()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.Center);
        YGNodeStyleSetJustifyContent(root, YGJustify.Center);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeStyleSetMargin(root_child0, YGEdge.Left, 10);
        YGNodeStyleSetMargin(root_child0, YGEdge.Right, 10);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));

        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_defined_cross_with_margin()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.Center);
        YGNodeStyleSetJustifyContent(root, YGJustify.Center);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeStyleSetMargin(root_child0, YGEdge.Left, 10);
        YGNodeStyleSetMargin(root_child0, YGEdge.Right, 10);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));

        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_defined_cross_with_main_margin()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.Center);
        YGNodeStyleSetJustifyContent(root, YGJustify.Center);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetAspectRatio(root_child0, 1);
        YGNodeStyleSetMargin(root_child0, YGEdge.Top, 10);
        YGNodeStyleSetMargin(root_child0, YGEdge.Bottom, 10);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));

        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Aspect_ratio_should_prefer_explicit_height()
    {
        var config = YGConfigNew();
        YGConfigSetUseWebDefaults(config, true);

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Column);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Column);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0_child0, YGFlexDirection.Column);
        YGNodeStyleSetHeight(root_child0_child0, 100);
        YGNodeStyleSetAspectRatio(root_child0_child0, 2);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);

        YGNodeCalculateLayout(root, 100, 200, YGDirection.LTR);

        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));

        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child0));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void Aspect_ratio_should_prefer_explicit_width()
    {
        var config = YGConfigNew();
        YGConfigSetUseWebDefaults(config, true);

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0_child0, YGFlexDirection.Row);
        YGNodeStyleSetWidth(root_child0_child0, 100);
        YGNodeStyleSetAspectRatio(root_child0_child0, 0.5f);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);

        YGNodeCalculateLayout(root, 200, 100, YGDirection.LTR);

        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));

        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0_child0));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void Aspect_ratio_should_prefer_flexed_dimension()
    {
        var config = YGConfigNew();
        YGConfigSetUseWebDefaults(config, true);

        var root = YGNodeNewWithConfig(config);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Column);
        YGNodeStyleSetAspectRatio(root_child0, 2);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetAspectRatio(root_child0_child0, 4);
        YGNodeStyleSetFlexGrow(root_child0_child0, 1);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);

        YGNodeCalculateLayout(root, 100, 100, YGDirection.LTR);

        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));

        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    // @generated from yoga/tests/generated/YGAspectRatioTest.cpp

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void aspect_ratio_does_not_stretch_cross_axis_dim()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 300);
        YGNodeStyleSetHeight(root, 300);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetFlexShrink(root_child0, 1);
        YGNodeStyleSetFlexBasisPercent(root_child0, 0);
        YGNodeStyleSetOverflow(root_child0, YGOverflow.Scroll);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0_child0, YGFlexDirection.Row);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0_child0_child0, 2);
        YGNodeStyleSetFlexShrink(root_child0_child0_child0, 1);
        YGNodeStyleSetFlexBasisPercent(root_child0_child0_child0, 0);
        YGNodeStyleSetAspectRatio(root_child0_child0_child0, 1);
        YGNodeInsertChild(root_child0_child0, root_child0_child0_child0, 0);
        var root_child0_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0_child1, 5);
        YGNodeInsertChild(root_child0_child0, root_child0_child0_child1, 1);
        var root_child0_child0_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0_child0_child2, 1);
        YGNodeStyleSetFlexShrink(root_child0_child0_child2, 1);
        YGNodeStyleSetFlexBasisPercent(root_child0_child0_child2, 0);
        YGNodeInsertChild(root_child0_child0, root_child0_child0_child2, 2);
        var root_child0_child0_child2_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0_child0_child2_child0, 1);
        YGNodeStyleSetFlexShrink(root_child0_child0_child2_child0, 1);
        YGNodeStyleSetFlexBasisPercent(root_child0_child0_child2_child0, 0);
        YGNodeStyleSetAspectRatio(root_child0_child0_child2_child0, 1);
        YGNodeInsertChild(root_child0_child0_child2, root_child0_child0_child2_child0, 0);
        var root_child0_child0_child2_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0_child2_child0_child0, 5);
        YGNodeInsertChild(root_child0_child0_child2_child0, root_child0_child0_child2_child0_child0, 0);
        var root_child0_child0_child2_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0_child0_child2_child0_child1, 1);
        YGNodeStyleSetFlexShrink(root_child0_child0_child2_child0_child1, 1);
        YGNodeStyleSetFlexBasisPercent(root_child0_child0_child2_child0_child1, 0);
        YGNodeStyleSetAspectRatio(root_child0_child0_child2_child0_child1, 1);
        YGNodeInsertChild(root_child0_child0_child2_child0, root_child0_child0_child2_child0_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(300f, YGNodeLayoutGetWidth(root));
        Assert.Equal(300f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(300f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(300f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(300f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(197f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0_child0));
        Assert.Equal(197f, YGNodeLayoutGetWidth(root_child0_child0_child0));
        Assert.Equal(197f, YGNodeLayoutGetHeight(root_child0_child0_child0));
        Assert.Equal(197f, YGNodeLayoutGetLeft(root_child0_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0_child1));
        Assert.Equal(5f, YGNodeLayoutGetWidth(root_child0_child0_child1));
        Assert.Equal(197f, YGNodeLayoutGetHeight(root_child0_child0_child1));
        Assert.Equal(202f, YGNodeLayoutGetLeft(root_child0_child0_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0_child2));
        Assert.Equal(98f, YGNodeLayoutGetWidth(root_child0_child0_child2));
        Assert.Equal(197f, YGNodeLayoutGetHeight(root_child0_child0_child2));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0_child2_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0_child2_child0));
        Assert.Equal(98f, YGNodeLayoutGetWidth(root_child0_child0_child2_child0));
        Assert.Equal(197f, YGNodeLayoutGetHeight(root_child0_child0_child2_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0_child2_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0_child2_child0_child0));
        Assert.Equal(5f, YGNodeLayoutGetWidth(root_child0_child0_child2_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0_child0_child2_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0_child2_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0_child2_child0_child1));
        Assert.Equal(98f, YGNodeLayoutGetWidth(root_child0_child0_child2_child0_child1));
        Assert.Equal(197f, YGNodeLayoutGetHeight(root_child0_child0_child2_child0_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(300f, YGNodeLayoutGetWidth(root));
        Assert.Equal(300f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(300f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(300f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(300f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(197f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(103f, YGNodeLayoutGetLeft(root_child0_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0_child0));
        Assert.Equal(197f, YGNodeLayoutGetWidth(root_child0_child0_child0));
        Assert.Equal(197f, YGNodeLayoutGetHeight(root_child0_child0_child0));
        Assert.Equal(98f, YGNodeLayoutGetLeft(root_child0_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0_child1));
        Assert.Equal(5f, YGNodeLayoutGetWidth(root_child0_child0_child1));
        Assert.Equal(197f, YGNodeLayoutGetHeight(root_child0_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0_child2));
        Assert.Equal(98f, YGNodeLayoutGetWidth(root_child0_child0_child2));
        Assert.Equal(197f, YGNodeLayoutGetHeight(root_child0_child0_child2));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0_child2_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0_child2_child0));
        Assert.Equal(98f, YGNodeLayoutGetWidth(root_child0_child0_child2_child0));
        Assert.Equal(197f, YGNodeLayoutGetHeight(root_child0_child0_child2_child0));
        Assert.Equal(93f, YGNodeLayoutGetLeft(root_child0_child0_child2_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0_child2_child0_child0));
        Assert.Equal(5f, YGNodeLayoutGetWidth(root_child0_child0_child2_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0_child0_child2_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0_child2_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0_child2_child0_child1));
        Assert.Equal(98f, YGNodeLayoutGetWidth(root_child0_child0_child2_child0_child1));
        Assert.Equal(197f, YGNodeLayoutGetHeight(root_child0_child0_child2_child0_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void zero_aspect_ratio_behaves_like_auto()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 300);
        YGNodeStyleSetHeight(root, 300);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetAspectRatio(root_child0, 0);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(300f, YGNodeLayoutGetWidth(root));
        Assert.Equal(300f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(300f, YGNodeLayoutGetWidth(root));
        Assert.Equal(300f, YGNodeLayoutGetHeight(root));
        Assert.Equal(250f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }
}
