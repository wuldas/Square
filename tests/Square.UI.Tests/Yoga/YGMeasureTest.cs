// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGMeasureTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YGConfigAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGMeasureTest
{
    private static YGSize _measure(Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        var measureCount = (int[]?)YGNodeGetContext(node);
        if (measureCount != null)
        {
            measureCount[0]++;
        }
        return new YGSize { Width = 10, Height = 10 };
    }

    private static YGSize _simulate_wrapping_text(Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        if (widthMode == MeasureMode.Undefined || width >= 68)
        {
            return new YGSize { Width = 68, Height = 16 };
        }
        return new YGSize { Width = 50, Height = 32 };
    }

    private static YGSize _measure_assert_negative(Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        Assert.True(width >= 0);
        Assert.True(height >= 0);
        return new YGSize { Width = 0, Height = 0 };
    }

    [Fact]
    public void Dont_measure_single_grow_shrink_child()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var measureCount = new int[] { 0 };

        var root_child0 = YGNodeNew();
        YGNodeSetContext(root_child0, measureCount);
        YGNodeSetMeasureFunc(root_child0, _measure);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetFlexShrink(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0, measureCount[0]);

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Measure_absolute_child_with_no_constraints()
    {
        var root = YGNodeNew();

        var root_child0 = YGNodeNew();
        YGNodeInsertChild(root, root_child0, 0);

        var measureCount = new int[] { 0 };

        var root_child0_child0 = YGNodeNew();
        YGNodeStyleSetPositionType(root_child0_child0, YGPositionType.Absolute);
        YGNodeSetContext(root_child0_child0, measureCount);
        YGNodeSetMeasureFunc(root_child0_child0, _measure);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(1, measureCount[0]);

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Dont_measure_when_min_equals_max()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var measureCount = new int[] { 0 };

        var root_child0 = YGNodeNew();
        YGNodeSetContext(root_child0, measureCount);
        YGNodeSetMeasureFunc(root_child0, _measure);
        YGNodeStyleSetMinWidth(root_child0, 10);
        YGNodeStyleSetMaxWidth(root_child0, 10);
        YGNodeStyleSetMinHeight(root_child0, 10);
        YGNodeStyleSetMaxHeight(root_child0, 10);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0, measureCount[0]);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Dont_measure_when_min_equals_max_percentages()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var measureCount = new int[] { 0 };

        var root_child0 = YGNodeNew();
        YGNodeSetContext(root_child0, measureCount);
        YGNodeSetMeasureFunc(root_child0, _measure);
        YGNodeStyleSetMinWidthPercent(root_child0, 10);
        YGNodeStyleSetMaxWidthPercent(root_child0, 10);
        YGNodeStyleSetMinHeightPercent(root_child0, 10);
        YGNodeStyleSetMaxHeightPercent(root_child0, 10);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0, measureCount[0]);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Measure_nodes_with_margin_auto_and_stretch()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 500);
        YGNodeStyleSetHeight(root, 500);

        var root_child0 = YGNodeNew();
        YGNodeSetMeasureFunc(root_child0, _measure);
        YGNodeStyleSetMarginAuto(root_child0, YGEdge.Left);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(490f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Dont_measure_when_min_equals_max_mixed_width_percent()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var measureCount = new int[] { 0 };

        var root_child0 = YGNodeNew();
        YGNodeSetContext(root_child0, measureCount);
        YGNodeSetMeasureFunc(root_child0, _measure);
        YGNodeStyleSetMinWidthPercent(root_child0, 10);
        YGNodeStyleSetMaxWidthPercent(root_child0, 10);
        YGNodeStyleSetMinHeight(root_child0, 10);
        YGNodeStyleSetMaxHeight(root_child0, 10);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0, measureCount[0]);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Dont_measure_when_min_equals_max_mixed_height_percent()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var measureCount = new int[] { 0 };

        var root_child0 = YGNodeNew();
        YGNodeSetContext(root_child0, measureCount);
        YGNodeSetMeasureFunc(root_child0, _measure);
        YGNodeStyleSetMinWidth(root_child0, 10);
        YGNodeStyleSetMaxWidth(root_child0, 10);
        YGNodeStyleSetMinHeightPercent(root_child0, 10);
        YGNodeStyleSetMaxHeightPercent(root_child0, 10);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0, measureCount[0]);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Measure_enough_size_should_be_in_single_line()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetAlignSelf(root_child0, YGAlign.FlexStart);
        YGNodeSetMeasureFunc(root_child0, _simulate_wrapping_text);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(68f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(16f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Measure_not_enough_size_should_wrap()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 55);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetAlignSelf(root_child0, YGAlign.FlexStart);
        YGNodeSetMeasureFunc(root_child0, _simulate_wrapping_text);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(32f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Measure_zero_space_should_grow()
    {
        var root = YGNodeNew();
        YGNodeStyleSetHeight(root, 200);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Column);
        YGNodeStyleSetFlexGrow(root, 0);

        var measureCount = new int[] { 0 };

        var root_child0 = YGNodeNew();
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Column);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 100);
        YGNodeSetContext(root_child0, measureCount);
        YGNodeSetMeasureFunc(root_child0, _measure);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, 282, float.NaN, YGDirection.LTR);

        Assert.Equal(282f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Measure_flex_direction_row_and_padding()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetPadding(root, YGEdge.Left, 25);
        YGNodeStyleSetPadding(root, YGEdge.Top, 25);
        YGNodeStyleSetPadding(root, YGEdge.Right, 25);
        YGNodeStyleSetPadding(root, YGEdge.Bottom, 25);
        YGNodeStyleSetWidth(root, 50);
        YGNodeStyleSetHeight(root, 50);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeSetMeasureFunc(root_child0, _simulate_wrapping_text);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 5);
        YGNodeStyleSetHeight(root_child1, 5);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));

        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(5f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(5f, YGNodeLayoutGetHeight(root_child1));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void Measure_flex_direction_column_and_padding()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetMargin(root, YGEdge.Top, 20);
        YGNodeStyleSetPadding(root, YGEdge.All, 25);
        YGNodeStyleSetWidth(root, 50);
        YGNodeStyleSetHeight(root, 50);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeSetMeasureFunc(root_child0, _simulate_wrapping_text);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 5);
        YGNodeStyleSetHeight(root_child1, 5);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(20f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));

        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(32f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(57f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(5f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(5f, YGNodeLayoutGetHeight(root_child1));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void Measure_flex_direction_row_no_padding()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetMargin(root, YGEdge.Top, 20);
        YGNodeStyleSetWidth(root, 50);
        YGNodeStyleSetHeight(root, 50);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeSetMeasureFunc(root_child0, _simulate_wrapping_text);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 5);
        YGNodeStyleSetHeight(root_child1, 5);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(20f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(5f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(5f, YGNodeLayoutGetHeight(root_child1));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void Measure_flex_direction_row_no_padding_align_items_flexstart()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetMargin(root, YGEdge.Top, 20);
        YGNodeStyleSetWidth(root, 50);
        YGNodeStyleSetHeight(root, 50);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeSetMeasureFunc(root_child0, _simulate_wrapping_text);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 5);
        YGNodeStyleSetHeight(root_child1, 5);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(20f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(32f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(5f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(5f, YGNodeLayoutGetHeight(root_child1));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void Measure_with_fixed_size()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetMargin(root, YGEdge.Top, 20);
        YGNodeStyleSetPadding(root, YGEdge.All, 25);
        YGNodeStyleSetWidth(root, 50);
        YGNodeStyleSetHeight(root, 50);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeSetMeasureFunc(root_child0, _simulate_wrapping_text);
        YGNodeStyleSetWidth(root_child0, 10);
        YGNodeStyleSetHeight(root_child0, 10);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 5);
        YGNodeStyleSetHeight(root_child1, 5);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(20f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));

        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(35f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(5f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(5f, YGNodeLayoutGetHeight(root_child1));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void Measure_with_flex_shrink()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetMargin(root, YGEdge.Top, 20);
        YGNodeStyleSetPadding(root, YGEdge.All, 25);
        YGNodeStyleSetWidth(root, 50);
        YGNodeStyleSetHeight(root, 50);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeSetMeasureFunc(root_child0, _simulate_wrapping_text);
        YGNodeStyleSetFlexShrink(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 5);
        YGNodeStyleSetHeight(root_child1, 5);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(20f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));

        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(5f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(5f, YGNodeLayoutGetHeight(root_child1));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void Measure_no_padding()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetMargin(root, YGEdge.Top, 20);
        YGNodeStyleSetWidth(root, 50);
        YGNodeStyleSetHeight(root, 50);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeSetMeasureFunc(root_child0, _simulate_wrapping_text);
        YGNodeStyleSetFlexShrink(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 5);
        YGNodeStyleSetHeight(root_child1, 5);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(20f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(32f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(32f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(5f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(5f, YGNodeLayoutGetHeight(root_child1));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void Can_nullify_measure_func_on_any_node()
    {
        var root = YGNodeNew();
        YGNodeInsertChild(root, YGNodeNew(), 0);
        YGNodeSetMeasureFunc(root, null);
        Assert.True(!YGNodeHasMeasureFunc(root));
        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Cant_call_negative_measure()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Column);
        YGNodeStyleSetWidth(root, 50);
        YGNodeStyleSetHeight(root, 10);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeSetMeasureFunc(root_child0, _measure_assert_negative);
        YGNodeStyleSetMargin(root_child0, YGEdge.Top, 20);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void Cant_call_negative_measure_horizontal()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetWidth(root, 10);
        YGNodeStyleSetHeight(root, 20);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeSetMeasureFunc(root_child0, _measure_assert_negative);
        YGNodeStyleSetMargin(root_child0, YGEdge.Start, 20);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    private static YGSize _measure_90_10(Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        return new YGSize { Width = 90, Height = 10 };
    }

    private static YGSize _measure_100_100(Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        return new YGSize { Width = 100, Height = 100 };
    }

    [Fact]
    public void Percent_with_text_node()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetJustifyContent(root, YGJustify.SpaceBetween);
        YGNodeStyleSetAlignItems(root, YGAlign.Center);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 80);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeSetMeasureFunc(root_child1, _measure_90_10);
        YGNodeStyleSetMaxWidthPercent(root_child1, 50);
        YGNodeStyleSetPaddingPercent(root_child1, YGEdge.Top, 50);
        YGNodeInsertChild(root, root_child1, 1);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(80f, YGNodeLayoutGetHeight(root));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(60f, YGNodeLayoutGetHeight(root_child1));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void Percent_margin_with_measure_func()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetWidth(root, 500);
        YGNodeStyleSetHeight(root, 500);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 100);
        YGNodeStyleSetHeight(root_child0, 100);
        YGNodeStyleSetMargin(root_child0, YGEdge.Top, 0);
        YGNodeSetMeasureFunc(root_child0, _measure_100_100);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 100);
        YGNodeStyleSetHeight(root_child1, 100);
        YGNodeStyleSetMargin(root_child1, YGEdge.Top, 100);
        YGNodeSetMeasureFunc(root_child1, _measure_100_100);
        YGNodeInsertChild(root, root_child1, 1);

        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child2, 100);
        YGNodeStyleSetHeight(root_child2, 100);
        YGNodeStyleSetMarginPercent(root_child2, YGEdge.Top, 10);
        YGNodeSetMeasureFunc(root_child2, _measure_100_100);
        YGNodeInsertChild(root, root_child2, 2);

        var root_child3 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child3, 100);
        YGNodeStyleSetHeight(root_child3, 100);
        YGNodeStyleSetMarginPercent(root_child3, YGEdge.Top, 20);
        YGNodeSetMeasureFunc(root_child3, _measure_100_100);
        YGNodeInsertChild(root, root_child3, 3);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(100f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));

        Assert.Equal(200f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child2));

        Assert.Equal(300f, YGNodeLayoutGetLeft(root_child3));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child3));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child3));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child3));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void Percent_padding_with_measure_func()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetAlignContent(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 500);
        YGNodeStyleSetHeight(root, 500);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 100);
        YGNodeStyleSetHeight(root_child0, 100);
        YGNodeStyleSetPadding(root_child0, YGEdge.Top, 0);
        YGNodeSetMeasureFunc(root_child0, _measure_100_100);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 100);
        YGNodeStyleSetHeight(root_child1, 100);
        YGNodeStyleSetPadding(root_child1, YGEdge.Top, 100);
        YGNodeSetMeasureFunc(root_child1, _measure_100_100);
        YGNodeInsertChild(root, root_child1, 1);

        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPaddingPercent(root_child2, YGEdge.Top, 10);
        YGNodeSetMeasureFunc(root_child2, _measure_100_100);
        YGNodeInsertChild(root, root_child2, 2);

        var root_child3 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPaddingPercent(root_child3, YGEdge.Top, 20);
        YGNodeSetMeasureFunc(root_child3, _measure_100_100);
        YGNodeInsertChild(root, root_child3, 3);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(100f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));

        Assert.Equal(200f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root_child2));

        Assert.Equal(300f, YGNodeLayoutGetLeft(root_child3));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child3));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child3));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child3));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void Percent_padding_and_percent_margin_with_measure_func()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetAlignContent(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 500);
        YGNodeStyleSetHeight(root, 500);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 100);
        YGNodeStyleSetHeight(root_child0, 100);
        YGNodeStyleSetPadding(root_child0, YGEdge.Top, 0);
        YGNodeSetMeasureFunc(root_child0, _measure_100_100);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 100);
        YGNodeStyleSetHeight(root_child1, 100);
        YGNodeStyleSetPadding(root_child1, YGEdge.Top, 100);
        YGNodeSetMeasureFunc(root_child1, _measure_100_100);
        YGNodeInsertChild(root, root_child1, 1);

        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPaddingPercent(root_child2, YGEdge.Top, 10);
        YGNodeStyleSetMarginPercent(root_child2, YGEdge.Top, 10);
        YGNodeSetMeasureFunc(root_child2, _measure_100_100);
        YGNodeInsertChild(root, root_child2, 2);

        var root_child3 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPaddingPercent(root_child3, YGEdge.Top, 20);
        YGNodeStyleSetMarginPercent(root_child3, YGEdge.Top, 20);
        YGNodeSetMeasureFunc(root_child3, _measure_100_100);
        YGNodeInsertChild(root, root_child3, 3);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(100f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));

        Assert.Equal(200f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root_child2));

        Assert.Equal(300f, YGNodeLayoutGetLeft(root_child3));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child3));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child3));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child3));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    private static YGSize _measure_half_width_height(Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        var measureCount = (int[]?)YGNodeGetContext(node);
        if (measureCount != null)
        {
            measureCount[0]++;
        }
        return new YGSize { Width = 0.5f * width, Height = 0.5f * height };
    }

    [Fact]
    public void Measure_content_box()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 200);
        YGNodeStyleSetBoxSizing(root, YGBoxSizing.ContentBox);
        YGNodeStyleSetPadding(root, YGEdge.All, 5);
        YGNodeStyleSetBorder(root, YGEdge.All, 10);

        var measureCount = new int[] { 0 };

        var root_child0 = YGNodeNew();
        YGNodeSetContext(root_child0, measureCount);
        YGNodeSetMeasureFunc(root_child0, _measure_half_width_height);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(1, measureCount[0]);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(130f, YGNodeLayoutGetWidth(root));
        Assert.Equal(230f, YGNodeLayoutGetHeight(root));

        Assert.Equal(15f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(15f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Measure_border_box()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 200);
        YGNodeStyleSetBoxSizing(root, YGBoxSizing.BorderBox);
        YGNodeStyleSetPadding(root, YGEdge.All, 5);
        YGNodeStyleSetBorder(root, YGEdge.All, 10);

        var measureCount = new int[] { 0 };

        var root_child0 = YGNodeNew();
        YGNodeSetContext(root_child0, measureCount);
        YGNodeSetMeasureFunc(root_child0, _measure_half_width_height);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(1, measureCount[0]);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));

        Assert.Equal(15f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(15f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(70f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(85f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Min_width_larger_than_width_propagates_to_auto_parent()
    {
        var root = YGNodeNew();

        var root_child0 = YGNodeNew();
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child0_child0 = YGNodeNew();
        YGNodeStyleSetWidth(root_child0_child0, 50);
        YGNodeStyleSetMinWidth(root_child0_child0, 100);
        YGNodeStyleSetHeight(root_child0_child0, 50);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));

        YGNodeFreeRecursive(root);
    }
}
