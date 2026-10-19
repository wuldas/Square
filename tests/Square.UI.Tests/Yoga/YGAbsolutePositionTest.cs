// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// @generated from yoga/tests/generated/YGAbsolutePositionTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YGConfigAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGAbsolutePositionTest
{
    [Fact]
    public void absolute_layout_width_height_start_top()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 10);
        YGNodeStyleSetHeight(root_child0, 10);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetPosition(root_child0, YGEdge.Start, 10);
        YGNodeStyleSetPosition(root_child0, YGEdge.Top, 10);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(80f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_width_height_left_auto_right()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 10);
        YGNodeStyleSetHeight(root_child0, 10);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetPositionAuto(root_child0, YGEdge.Left);
        YGNodeStyleSetPosition(root_child0, YGEdge.Right, 10);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(80f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(80f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_width_height_left_right_auto()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 10);
        YGNodeStyleSetHeight(root_child0, 10);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetPosition(root_child0, YGEdge.Left, 10);
        YGNodeStyleSetPositionAuto(root_child0, YGEdge.Right);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_width_height_left_auto_right_auto()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 10);
        YGNodeStyleSetHeight(root_child0, 10);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetPositionAuto(root_child0, YGEdge.Left);
        YGNodeStyleSetPositionAuto(root_child0, YGEdge.Right);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(90f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_width_height_end_bottom()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 10);
        YGNodeStyleSetHeight(root_child0, 10);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetPosition(root_child0, YGEdge.End, 10);
        YGNodeStyleSetPosition(root_child0, YGEdge.Bottom, 10);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(80f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_start_top_end_bottom()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetPosition(root_child0, YGEdge.Start, 10);
        YGNodeStyleSetPosition(root_child0, YGEdge.Top, 10);
        YGNodeStyleSetPosition(root_child0, YGEdge.End, 10);
        YGNodeStyleSetPosition(root_child0, YGEdge.Bottom, 10);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_width_height_start_top_end_bottom()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 10);
        YGNodeStyleSetHeight(root_child0, 10);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetPosition(root_child0, YGEdge.Start, 10);
        YGNodeStyleSetPosition(root_child0, YGEdge.Top, 10);
        YGNodeStyleSetPosition(root_child0, YGEdge.End, 10);
        YGNodeStyleSetPosition(root_child0, YGEdge.Bottom, 10);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(80f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void do_not_clamp_height_of_absolute_node_to_height_of_its_overflow_hidden_parent()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 50);
        YGNodeStyleSetWidth(root, 50);
        YGNodeStyleSetOverflow(root, YGOverflow.Hidden);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetPosition(root_child0, YGEdge.Start, 0);
        YGNodeStyleSetPosition(root_child0, YGEdge.Top, 0);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 100);
        YGNodeStyleSetHeight(root_child0_child0, 100);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(-50f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_within_border()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetBorder(root, YGEdge.All, 10);
        YGNodeStyleSetMargin(root, YGEdge.All, 10);
        YGNodeStyleSetPadding(root, YGEdge.All, 10);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeStyleSetPosition(root_child0, YGEdge.Left, 0);
        YGNodeStyleSetPosition(root_child0, YGEdge.Top, 0);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child1, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child1, 50);
        YGNodeStyleSetHeight(root_child1, 50);
        YGNodeStyleSetPosition(root_child1, YGEdge.Right, 0);
        YGNodeStyleSetPosition(root_child1, YGEdge.Bottom, 0);
        YGNodeInsertChild(root, root_child1, 1);
        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child2, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child2, 50);
        YGNodeStyleSetHeight(root_child2, 50);
        YGNodeStyleSetPosition(root_child2, YGEdge.Left, 0);
        YGNodeStyleSetPosition(root_child2, YGEdge.Top, 0);
        YGNodeStyleSetMargin(root_child2, YGEdge.All, 10);
        YGNodeInsertChild(root, root_child2, 2);
        var root_child3 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child3, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child3, 50);
        YGNodeStyleSetHeight(root_child3, 50);
        YGNodeStyleSetPosition(root_child3, YGEdge.Right, 0);
        YGNodeStyleSetPosition(root_child3, YGEdge.Bottom, 0);
        YGNodeStyleSetMargin(root_child3, YGEdge.All, 10);
        YGNodeInsertChild(root, root_child3, 3);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(10f, YGNodeLayoutGetLeft(root));
        Assert.Equal(10f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(20f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(20f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child2));
        Assert.Equal(30f, YGNodeLayoutGetLeft(root_child3));
        Assert.Equal(30f, YGNodeLayoutGetTop(root_child3));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child3));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child3));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(10f, YGNodeLayoutGetLeft(root));
        Assert.Equal(10f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(20f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(20f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child2));
        Assert.Equal(30f, YGNodeLayoutGetLeft(root_child3));
        Assert.Equal(30f, YGNodeLayoutGetTop(root_child3));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child3));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child3));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_align_items_and_justify_content_center()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetWidth(root, 110);
        YGNodeStyleSetFlexGrow(root, 1);
        YGNodeStyleSetAlignItems(root, YGAlign.Center);
        YGNodeStyleSetJustifyContent(root, YGJustify.Center);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child0, 60);
        YGNodeStyleSetHeight(root_child0, 40);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_align_items_and_justify_content_flex_end()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetWidth(root, 110);
        YGNodeStyleSetFlexGrow(root, 1);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexEnd);
        YGNodeStyleSetJustifyContent(root, YGJustify.FlexEnd);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child0, 60);
        YGNodeStyleSetHeight(root_child0, 40);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_justify_content_center()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetWidth(root, 110);
        YGNodeStyleSetFlexGrow(root, 1);
        YGNodeStyleSetJustifyContent(root, YGJustify.Center);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child0, 60);
        YGNodeStyleSetHeight(root_child0, 40);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_align_items_center()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetWidth(root, 110);
        YGNodeStyleSetFlexGrow(root, 1);
        YGNodeStyleSetAlignItems(root, YGAlign.Center);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child0, 60);
        YGNodeStyleSetHeight(root_child0, 40);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_align_items_center_on_child_only()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetWidth(root, 110);
        YGNodeStyleSetFlexGrow(root, 1);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child0, 60);
        YGNodeStyleSetHeight(root_child0, 40);
        YGNodeStyleSetAlignSelf(root_child0, YGAlign.Center);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_align_items_and_justify_content_center_and_top_position()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetWidth(root, 110);
        YGNodeStyleSetFlexGrow(root, 1);
        YGNodeStyleSetAlignItems(root, YGAlign.Center);
        YGNodeStyleSetJustifyContent(root, YGJustify.Center);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child0, 60);
        YGNodeStyleSetHeight(root_child0, 40);
        YGNodeStyleSetPosition(root_child0, YGEdge.Top, 10);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_align_items_and_justify_content_center_and_bottom_position()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetWidth(root, 110);
        YGNodeStyleSetFlexGrow(root, 1);
        YGNodeStyleSetAlignItems(root, YGAlign.Center);
        YGNodeStyleSetJustifyContent(root, YGJustify.Center);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child0, 60);
        YGNodeStyleSetHeight(root_child0, 40);
        YGNodeStyleSetPosition(root_child0, YGEdge.Bottom, 10);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_align_items_and_justify_content_center_and_left_position()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetWidth(root, 110);
        YGNodeStyleSetFlexGrow(root, 1);
        YGNodeStyleSetAlignItems(root, YGAlign.Center);
        YGNodeStyleSetJustifyContent(root, YGJustify.Center);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child0, 60);
        YGNodeStyleSetHeight(root_child0, 40);
        YGNodeStyleSetPosition(root_child0, YGEdge.Left, 5);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(5f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(5f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_align_items_and_justify_content_center_and_right_position()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetWidth(root, 110);
        YGNodeStyleSetFlexGrow(root, 1);
        YGNodeStyleSetAlignItems(root, YGAlign.Center);
        YGNodeStyleSetJustifyContent(root, YGJustify.Center);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child0, 60);
        YGNodeStyleSetHeight(root_child0, 40);
        YGNodeStyleSetPosition(root_child0, YGEdge.Right, 5);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(45f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(45f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void position_root_with_rtl_should_position_withoutdirection()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 52);
        YGNodeStyleSetWidth(root, 52);
        YGNodeStyleSetPosition(root, YGEdge.Left, 72);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(72f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(52f, YGNodeLayoutGetWidth(root));
        Assert.Equal(52f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(72f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(52f, YGNodeLayoutGetWidth(root));
        Assert.Equal(52f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_percentage_bottom_based_on_parent_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 200);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetPositionPercent(root_child0, YGEdge.Top, 50);
        YGNodeStyleSetWidth(root_child0, 10);
        YGNodeStyleSetHeight(root_child0, 10);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child1, YGPositionType.Absolute);
        YGNodeStyleSetPositionPercent(root_child1, YGEdge.Bottom, 50);
        YGNodeStyleSetWidth(root_child1, 10);
        YGNodeStyleSetHeight(root_child1, 10);
        YGNodeInsertChild(root, root_child1, 1);
        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child2, YGPositionType.Absolute);
        YGNodeStyleSetPositionPercent(root_child2, YGEdge.Top, 10);
        YGNodeStyleSetWidth(root_child2, 10);
        YGNodeStyleSetPositionPercent(root_child2, YGEdge.Bottom, 10);
        YGNodeInsertChild(root, root_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(90f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(20f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(160f, YGNodeLayoutGetHeight(root_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(90f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(90f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(90f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(90f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(20f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(160f, YGNodeLayoutGetHeight(root_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_in_wrap_reverse_column_container()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetFlexWrap(root, YGWrap.WrapReverse);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 20);
        YGNodeStyleSetHeight(root_child0, 20);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(80f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_in_wrap_reverse_row_container()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetFlexWrap(root, YGWrap.WrapReverse);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 20);
        YGNodeStyleSetHeight(root_child0, 20);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(80f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_in_wrap_reverse_column_container_flex_end()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetFlexWrap(root, YGWrap.WrapReverse);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 20);
        YGNodeStyleSetHeight(root_child0, 20);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetAlignSelf(root_child0, YGAlign.FlexEnd);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(80f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_in_wrap_reverse_row_container_flex_end()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetFlexWrap(root, YGWrap.WrapReverse);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 20);
        YGNodeStyleSetHeight(root_child0, 20);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetAlignSelf(root_child0, YGAlign.FlexEnd);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(80f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percent_absolute_position_infinite_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 300);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 300);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child1, 20);
        YGNodeStyleSetHeightPercent(root_child1, 20);
        YGNodeStyleSetPositionPercent(root_child1, YGEdge.Left, 20);
        YGNodeStyleSetPositionPercent(root_child1, YGEdge.Top, 20);
        YGNodeStyleSetPositionType(root_child1, YGPositionType.Absolute);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(300f, YGNodeLayoutGetWidth(root));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(300f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(300f, YGNodeLayoutGetWidth(root));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(300f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_percentage_height_based_on_padded_parent()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetPadding(root, YGEdge.Top, 10);
        YGNodeStyleSetBorder(root, YGEdge.Top, 10);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 100);
        YGNodeStyleSetHeightPercent(root_child0, 50);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(45f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(45f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_percentage_height_based_on_padded_parent_and_align_items_center()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetAlignItems(root, YGAlign.Center);
        YGNodeStyleSetJustifyContent(root, YGJustify.Center);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetPadding(root, YGEdge.Top, 20);
        YGNodeStyleSetPadding(root, YGEdge.Bottom, 20);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child0, 100);
        YGNodeStyleSetHeightPercent(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_padding_left()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);
        YGNodeStyleSetPadding(root, YGEdge.Left, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(100f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(150f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_padding_right()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);
        YGNodeStyleSetPadding(root, YGEdge.Right, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_padding_top()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);
        YGNodeStyleSetPadding(root, YGEdge.Top, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(150f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_padding_bottom()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);
        YGNodeStyleSetPadding(root, YGEdge.Bottom, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(150f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_padding()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 200);
        YGNodeStyleSetHeight(root_child0, 200);
        YGNodeStyleSetMargin(root_child0, YGEdge.All, 10);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0_child0, YGPositionType.Static);
        YGNodeStyleSetWidth(root_child0_child0, 200);
        YGNodeStyleSetHeight(root_child0_child0, 200);
        YGNodeStyleSetPadding(root_child0_child0, YGEdge.All, 50);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0_child0_child0, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child0_child0_child0, 50);
        YGNodeStyleSetHeight(root_child0_child0_child0, 50);
        YGNodeInsertChild(root_child0_child0, root_child0_child0_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(220f, YGNodeLayoutGetWidth(root));
        Assert.Equal(220f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(220f, YGNodeLayoutGetWidth(root));
        Assert.Equal(220f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(100f, YGNodeLayoutGetLeft(root_child0_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_border()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 200);
        YGNodeStyleSetHeight(root_child0, 200);
        YGNodeStyleSetMargin(root_child0, YGEdge.All, 10);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0_child0, YGPositionType.Static);
        YGNodeStyleSetWidth(root_child0_child0, 200);
        YGNodeStyleSetHeight(root_child0_child0, 200);
        YGNodeStyleSetBorder(root_child0_child0, YGEdge.All, 10);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0_child0_child0, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child0_child0_child0, 50);
        YGNodeStyleSetHeight(root_child0_child0_child0, 50);
        YGNodeInsertChild(root_child0_child0, root_child0_child0_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(220f, YGNodeLayoutGetWidth(root));
        Assert.Equal(220f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(220f, YGNodeLayoutGetWidth(root));
        Assert.Equal(220f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(140f, YGNodeLayoutGetLeft(root_child0_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void absolute_layout_column_reverse_margin_border()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.ColumnReverse);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeStyleSetPosition(root_child0, YGEdge.Left, 5);
        YGNodeStyleSetPosition(root_child0, YGEdge.Right, 3);
        YGNodeStyleSetMargin(root_child0, YGEdge.Right, 4);
        YGNodeStyleSetMargin(root_child0, YGEdge.Left, 3);
        YGNodeStyleSetBorder(root_child0, YGEdge.Right, 7);
        YGNodeStyleSetBorder(root_child0, YGEdge.Left, 1);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(8f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(150f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(143f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(150f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

}

