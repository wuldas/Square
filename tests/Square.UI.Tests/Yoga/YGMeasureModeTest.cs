// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGMeasureModeTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGMeasureModeTest
{
    private struct MeasureConstraint
    {
        public float Width;
        public MeasureMode WidthMode;
        public float Height;
        public MeasureMode HeightMode;
    }

    private static YGSize Measure(Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        var constraints = (List<MeasureConstraint>)YGNodeGetContext(node)!;
        constraints.Add(new MeasureConstraint
        {
            Width = width,
            WidthMode = widthMode,
            Height = height,
            HeightMode = heightMode,
        });
        return new YGSize
        {
            Width = widthMode == MeasureMode.Undefined ? 10 : width,
            Height = heightMode == MeasureMode.Undefined ? 10 : width,
        };
    }

    [Fact]
    public void Exactly_measure_stretched_child_column()
    {
        var constraints = new List<MeasureConstraint>();

        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeSetContext(root_child0, constraints);
        YGNodeSetMeasureFunc(root_child0, Measure);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Single(constraints);
        Assert.Equal(100f, constraints[0].Width);
        Assert.Equal(MeasureMode.Exactly, constraints[0].WidthMode);

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Exactly_measure_stretched_child_row()
    {
        var constraints = new List<MeasureConstraint>();

        var root = YGNodeNew();
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeSetContext(root_child0, constraints);
        YGNodeSetMeasureFunc(root_child0, Measure);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Single(constraints);
        Assert.Equal(100f, constraints[0].Height);
        Assert.Equal(MeasureMode.Exactly, constraints[0].HeightMode);

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void At_most_main_axis_column()
    {
        var constraints = new List<MeasureConstraint>();

        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeSetContext(root_child0, constraints);
        YGNodeSetMeasureFunc(root_child0, Measure);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Single(constraints);
        Assert.Equal(100f, constraints[0].Height);
        Assert.Equal(MeasureMode.AtMost, constraints[0].HeightMode);

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void At_most_cross_axis_column()
    {
        var constraints = new List<MeasureConstraint>();

        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeSetContext(root_child0, constraints);
        YGNodeSetMeasureFunc(root_child0, Measure);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Single(constraints);
        Assert.Equal(100f, constraints[0].Width);
        Assert.Equal(MeasureMode.AtMost, constraints[0].WidthMode);

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void At_most_main_axis_row()
    {
        var constraints = new List<MeasureConstraint>();

        var root = YGNodeNew();
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeSetContext(root_child0, constraints);
        YGNodeSetMeasureFunc(root_child0, Measure);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Single(constraints);
        Assert.Equal(100f, constraints[0].Width);
        Assert.Equal(MeasureMode.AtMost, constraints[0].WidthMode);

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void At_most_cross_axis_row()
    {
        var constraints = new List<MeasureConstraint>();

        var root = YGNodeNew();
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeSetContext(root_child0, constraints);
        YGNodeSetMeasureFunc(root_child0, Measure);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Single(constraints);
        Assert.Equal(100f, constraints[0].Height);
        Assert.Equal(MeasureMode.AtMost, constraints[0].HeightMode);

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Flex_child()
    {
        var constraints = new List<MeasureConstraint>();

        var root = YGNodeNew();
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeSetContext(root_child0, constraints);
        YGNodeSetMeasureFunc(root_child0, Measure);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(2, constraints.Count);
        Assert.Equal(100f, constraints[0].Height);
        Assert.Equal(MeasureMode.AtMost, constraints[0].HeightMode);
        Assert.Equal(100f, constraints[1].Height);
        Assert.Equal(MeasureMode.Exactly, constraints[1].HeightMode);

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Flex_child_with_flex_basis()
    {
        var constraints = new List<MeasureConstraint>();

        var root = YGNodeNew();
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetFlexBasis(root_child0, 0);
        YGNodeSetContext(root_child0, constraints);
        YGNodeSetMeasureFunc(root_child0, Measure);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Single(constraints);
        Assert.Equal(100f, constraints[0].Height);
        Assert.Equal(MeasureMode.Exactly, constraints[0].HeightMode);

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Overflow_scroll_column()
    {
        var constraints = new List<MeasureConstraint>();

        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetOverflow(root, YGOverflow.Scroll);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetWidth(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeSetContext(root_child0, constraints);
        YGNodeSetMeasureFunc(root_child0, Measure);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Single(constraints);
        Assert.Equal(100f, constraints[0].Width);
        Assert.Equal(MeasureMode.AtMost, constraints[0].WidthMode);
        Assert.True(float.IsNaN(constraints[0].Height));
        Assert.Equal(MeasureMode.Undefined, constraints[0].HeightMode);

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Overflow_scroll_row()
    {
        var constraints = new List<MeasureConstraint>();

        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetOverflow(root, YGOverflow.Scroll);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetWidth(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeSetContext(root_child0, constraints);
        YGNodeSetMeasureFunc(root_child0, Measure);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Single(constraints);
        Assert.True(float.IsNaN(constraints[0].Width));
        Assert.Equal(MeasureMode.Undefined, constraints[0].WidthMode);
        Assert.Equal(100f, constraints[0].Height);
        Assert.Equal(MeasureMode.AtMost, constraints[0].HeightMode);

        YGNodeFreeRecursive(root);
    }
}
