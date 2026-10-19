// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// @generated from yoga/tests/generated/YGPercentageTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YGConfigAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGPercentageTest
{
    [Fact]
    public void percentage_width_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0, 30);
        YGNodeStyleSetHeightPercent(root_child0, 30);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(140f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percentage_position_left_top()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 400);
        YGNodeStyleSetHeight(root, 400);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0, 45);
        YGNodeStyleSetHeightPercent(root_child0, 55);
        YGNodeStyleSetPositionPercent(root_child0, YGEdge.Left, 10);
        YGNodeStyleSetPositionPercent(root_child0, YGEdge.Top, 20);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(400f, YGNodeLayoutGetWidth(root));
        Assert.Equal(400f, YGNodeLayoutGetHeight(root));
        Assert.Equal(40f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(180f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(220f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(400f, YGNodeLayoutGetWidth(root));
        Assert.Equal(400f, YGNodeLayoutGetHeight(root));
        Assert.Equal(260f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(180f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(220f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percentage_position_bottom_right()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 500);
        YGNodeStyleSetHeight(root, 500);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0, 55);
        YGNodeStyleSetHeightPercent(root_child0, 15);
        YGNodeStyleSetPositionPercent(root_child0, YGEdge.Bottom, 10);
        YGNodeStyleSetPositionPercent(root_child0, YGEdge.Right, 20);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));
        Assert.Equal(-100f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(-50f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(275f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));
        Assert.Equal(125f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(-50f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(275f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percentage_flex_basis()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetFlexBasisPercent(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child1, 1);
        YGNodeStyleSetFlexBasisPercent(root_child1, 25);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(125f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(125f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(75f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(125f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(75f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percentage_flex_basis_cross()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetFlexBasisPercent(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child1, 1);
        YGNodeStyleSetFlexBasisPercent(root_child1, 25);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(125f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(125f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(75f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(125f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(125f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(75f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void percentage_flex_basis_cross_min_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetMinHeightPercent(root_child0, 60);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child1, 2);
        YGNodeStyleSetMinHeightPercent(root_child1, 10);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(120f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(120f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(80f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(120f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(120f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(80f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percentage_flex_basis_main_max_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetFlexBasisPercent(root_child0, 10);
        YGNodeStyleSetMaxHeightPercent(root_child0, 60);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child1, 4);
        YGNodeStyleSetFlexBasisPercent(root_child1, 10);
        YGNodeStyleSetMaxHeightPercent(root_child1, 20);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(52f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(120f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(52f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(148f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(148f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(52f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(120f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(148f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percentage_flex_basis_cross_max_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetFlexBasisPercent(root_child0, 10);
        YGNodeStyleSetMaxHeightPercent(root_child0, 60);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child1, 4);
        YGNodeStyleSetFlexBasisPercent(root_child1, 10);
        YGNodeStyleSetMaxHeightPercent(root_child1, 20);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(120f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(120f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(120f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(120f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percentage_flex_basis_main_max_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetFlexBasisPercent(root_child0, 15);
        YGNodeStyleSetMaxWidthPercent(root_child0, 60);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child1, 4);
        YGNodeStyleSetFlexBasisPercent(root_child1, 10);
        YGNodeStyleSetMaxWidthPercent(root_child1, 20);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(120f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(120f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(80f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(120f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percentage_flex_basis_cross_max_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetFlexBasisPercent(root_child0, 10);
        YGNodeStyleSetMaxWidthPercent(root_child0, 60);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child1, 4);
        YGNodeStyleSetFlexBasisPercent(root_child1, 15);
        YGNodeStyleSetMaxWidthPercent(root_child1, 20);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(120f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(80f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(120f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(160f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percentage_flex_basis_main_min_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetFlexBasisPercent(root_child0, 15);
        YGNodeStyleSetMinWidthPercent(root_child0, 60);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child1, 4);
        YGNodeStyleSetFlexBasisPercent(root_child1, 10);
        YGNodeStyleSetMinWidthPercent(root_child1, 20);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(120f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(120f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(80f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(80f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(120f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(80f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percentage_flex_basis_cross_min_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetFlexBasisPercent(root_child0, 10);
        YGNodeStyleSetMinWidthPercent(root_child0, 60);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child1, 4);
        YGNodeStyleSetFlexBasisPercent(root_child1, 15);
        YGNodeStyleSetMinWidthPercent(root_child1, 20);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percentage_multiple_nested_with_padding_margin_and_percentage_values()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetFlexBasisPercent(root_child0, 10);
        YGNodeStyleSetMinWidthPercent(root_child0, 60);
        YGNodeStyleSetMargin(root_child0, YGEdge.All, 5);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 3);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0_child0, 50);
        YGNodeStyleSetMargin(root_child0_child0, YGEdge.All, 5);
        YGNodeStyleSetPaddingPercent(root_child0_child0, YGEdge.All, 3);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0_child0_child0, 45);
        YGNodeStyleSetMarginPercent(root_child0_child0_child0, YGEdge.All, 5);
        YGNodeStyleSetPadding(root_child0_child0_child0, YGEdge.All, 3);
        YGNodeInsertChild(root_child0_child0, root_child0_child0_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child1, 4);
        YGNodeStyleSetFlexBasisPercent(root_child1, 15);
        YGNodeStyleSetMinWidthPercent(root_child1, 20);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(5f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(5f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(190f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(48f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(8f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(8f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(92f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0_child0_child0));
        Assert.Equal(36f, YGNodeLayoutGetWidth(root_child0_child0_child0));
        Assert.Equal(6f, YGNodeLayoutGetHeight(root_child0_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(58f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(142f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(5f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(5f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(190f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(48f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(90f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(8f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(92f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(46f, YGNodeLayoutGetLeft(root_child0_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0_child0_child0));
        Assert.Equal(36f, YGNodeLayoutGetWidth(root_child0_child0_child0));
        Assert.Equal(6f, YGNodeLayoutGetHeight(root_child0_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(58f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(142f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percentage_margin_should_calculate_based_only_on_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetMarginPercent(root_child0, YGEdge.All, 10);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 10);
        YGNodeStyleSetHeight(root_child0_child0, 10);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(20f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(160f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(20f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(160f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(150f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percentage_padding_should_calculate_based_only_on_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetPaddingPercent(root_child0, YGEdge.All, 10);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 10);
        YGNodeStyleSetHeight(root_child0_child0, 10);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(20f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(170f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(20f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percentage_absolute_position()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetPositionPercent(root_child0, YGEdge.Top, 10);
        YGNodeStyleSetPositionPercent(root_child0, YGEdge.Left, 30);
        YGNodeStyleSetWidth(root_child0, 10);
        YGNodeStyleSetHeight(root_child0, 10);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(60f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(60f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percentage_width_height_undefined_parent_size()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0, 50);
        YGNodeStyleSetHeightPercent(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percent_within_flex_grow()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetWidth(root, 350);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 100);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child1, 1);
        YGNodeInsertChild(root, root_child1, 1);
        var root_child1_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child1_child0, 100);
        YGNodeInsertChild(root_child1, root_child1_child0, 0);
        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child2, 100);
        YGNodeInsertChild(root, root_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(350f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(150f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1_child0));
        Assert.Equal(150f, YGNodeLayoutGetWidth(root_child1_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child1_child0));
        Assert.Equal(250f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(350f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(250f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(150f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1_child0));
        Assert.Equal(150f, YGNodeLayoutGetWidth(root_child1_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child1_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percentage_container_in_wrapping_container()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetAlignItems(root, YGAlign.Center);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);
        YGNodeStyleSetJustifyContent(root, YGJustify.Center);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0_child0, YGFlexDirection.Row);
        YGNodeStyleSetJustifyContent(root_child0_child0, YGJustify.Center);
        YGNodeStyleSetWidthPercent(root_child0_child0, 100);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0_child0, 50);
        YGNodeStyleSetHeight(root_child0_child0_child0, 50);
        YGNodeInsertChild(root_child0_child0, root_child0_child0_child0, 0);
        var root_child0_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0_child1, 50);
        YGNodeStyleSetHeight(root_child0_child0_child1, 50);
        YGNodeInsertChild(root_child0_child0, root_child0_child0_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percent_absolute_position()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 60);
        YGNodeStyleSetHeight(root, 50);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeStyleSetWidthPercent(root_child0, 100);
        YGNodeStyleSetPositionPercent(root_child0, YGEdge.Left, 50);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0_child0, 100);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0_child1, 100);
        YGNodeInsertChild(root_child0, root_child0_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(30f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(60f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(30f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(-60f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percent_of_minmax_main()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetMinWidth(root, 60);
        YGNodeStyleSetMaxWidth(root, 60);
        YGNodeStyleSetHeight(root, 50);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 20);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(30f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void percent_of_min_main()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetMinWidth(root, 60);
        YGNodeStyleSetHeight(root, 50);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 20);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(30f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void percent_of_min_main_multiple()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetMinWidth(root, 60);
        YGNodeStyleSetHeight(root, 50);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 20);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child1, 50);
        YGNodeStyleSetHeight(root_child1, 20);
        YGNodeInsertChild(root, root_child1, 1);
        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child2, 50);
        YGNodeStyleSetHeight(root_child2, 20);
        YGNodeInsertChild(root, root_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(30f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(60f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(30f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(30f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(30f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(-30f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(30f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void percent_of_max_main()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetMaxWidth(root, 60);
        YGNodeStyleSetHeight(root, 50);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 20);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percent_of_minmax_cross_stretched()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetMinWidth(root, 60);
        YGNodeStyleSetMaxWidth(root, 60);
        YGNodeStyleSetHeight(root, 50);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 20);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(30f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percent_absolute_of_minmax_cross_stretched()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetMinWidth(root, 60);
        YGNodeStyleSetMaxWidth(root, 60);
        YGNodeStyleSetHeight(root, 50);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 20);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(30f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percent_of_minmax_cross_unstretched()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetMinWidth(root, 60);
        YGNodeStyleSetMaxWidth(root, 60);
        YGNodeStyleSetHeight(root, 50);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 20);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(30f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void percent_of_min_cross_unstretched()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetMinWidth(root, 60);
        YGNodeStyleSetHeight(root, 50);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 20);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(30f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void percent_of_max_cross_unstretched()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetMaxWidth(root, 60);
        YGNodeStyleSetHeight(root, 50);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 20);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

}

