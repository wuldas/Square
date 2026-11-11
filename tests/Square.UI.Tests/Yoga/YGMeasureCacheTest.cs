// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGMeasureCacheTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGMeasureCacheTest
{
    private static YGSize MeasureMax(Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        var measureCount = (int[])YGNodeGetContext(node)!;
        measureCount[0]++;
        return new YGSize
        {
            Width = widthMode == MeasureMode.Undefined ? 10 : width,
            Height = heightMode == MeasureMode.Undefined ? 10 : height,
        };
    }

    private static YGSize MeasureMin(Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        var measureCount = (int[])YGNodeGetContext(node)!;
        measureCount[0]++;
        return new YGSize
        {
            Width = widthMode == MeasureMode.Undefined || (widthMode == MeasureMode.AtMost && width > 10)
                ? 10
                : width,
            Height = heightMode == MeasureMode.Undefined || (heightMode == MeasureMode.AtMost && height > 10)
                ? 10
                : height,
        };
    }

    private static YGSize Measure_84_49(Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        var measureCount = YGNodeGetContext(node) as int[];
        if (measureCount != null)
        {
            measureCount[0]++;
        }
        return new YGSize { Width = 84f, Height = 49f };
    }

    [Fact]
    public void Measure_once_single_flexible_child()
    {
        var root = YGNodeNew();
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        var measureCount = new int[] { 0 };
        YGNodeSetContext(root_child0, measureCount);
        YGNodeSetMeasureFunc(root_child0, MeasureMax);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(1, measureCount[0]);

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Remeasure_with_same_exact_width_larger_than_needed_height()
    {
        var root = YGNodeNew();

        var root_child0 = YGNodeNew();
        var measureCount = new int[] { 0 };
        YGNodeSetContext(root_child0, measureCount);
        YGNodeSetMeasureFunc(root_child0, MeasureMin);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, 100, 100, YGDirection.LTR);
        YGNodeCalculateLayout(root, 100, 50, YGDirection.LTR);

        Assert.Equal(1, measureCount[0]);

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Remeasure_with_same_atmost_width_larger_than_needed_height()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);

        var root_child0 = YGNodeNew();
        var measureCount = new int[] { 0 };
        YGNodeSetContext(root_child0, measureCount);
        YGNodeSetMeasureFunc(root_child0, MeasureMin);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, 100, 100, YGDirection.LTR);
        YGNodeCalculateLayout(root, 100, 50, YGDirection.LTR);

        Assert.Equal(1, measureCount[0]);

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Remeasure_with_computed_width_larger_than_needed_height()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);

        var root_child0 = YGNodeNew();
        var measureCount = new int[] { 0 };
        YGNodeSetContext(root_child0, measureCount);
        YGNodeSetMeasureFunc(root_child0, MeasureMin);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, 100, 100, YGDirection.LTR);
        YGNodeStyleSetAlignItems(root, YGAlign.Stretch);
        YGNodeCalculateLayout(root, 10, 50, YGDirection.LTR);

        Assert.Equal(1, measureCount[0]);

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Remeasure_with_atmost_computed_width_undefined_height()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);

        var root_child0 = YGNodeNew();
        var measureCount = new int[] { 0 };
        YGNodeSetContext(root_child0, measureCount);
        YGNodeSetMeasureFunc(root_child0, MeasureMin);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, 100, float.NaN, YGDirection.LTR);
        YGNodeCalculateLayout(root, 10, float.NaN, YGDirection.LTR);

        Assert.Equal(1, measureCount[0]);

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Remeasure_with_already_measured_value_smaller_but_still_float_equal()
    {
        var measureCount = new int[] { 0 };

        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 288f);
        YGNodeStyleSetHeight(root, 288f);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 2.88f);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child0_child0 = YGNodeNew();
        YGNodeSetContext(root_child0_child0, measureCount);
        YGNodeSetMeasureFunc(root_child0_child0, Measure_84_49);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        YGNodeFreeRecursive(root);

        Assert.Equal(1, measureCount[0]);
    }
}
