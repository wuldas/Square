// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// @generated from yoga/tests/generated/YGMinMaxDimensionTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YGConfigAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGMinMaxDimensionTest
{
    [Fact]
    public void max_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeight(root_child0, 10);
        YGNodeStyleSetMaxWidth(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void max_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 10);
        YGNodeStyleSetMaxHeight(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(90f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void min_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetMinHeight(root_child0, 60);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child1, 1);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(60f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(60f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void min_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetMinWidth(root_child0, 60);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child1, 1);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(40f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void justify_content_min_max()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetMaxHeight(root, 200);
        YGNodeStyleSetMinHeight(root, 100);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetJustifyContent(root, YGJustify.Center);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 60);
        YGNodeStyleSetHeight(root_child0, 60);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(40f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void align_items_min_max()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetMaxWidth(root, 200);
        YGNodeStyleSetMinWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetAlignItems(root, YGAlign.Center);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 60);
        YGNodeStyleSetHeight(root_child0, 60);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(20f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(20f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void justify_content_overflow_min_max()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetMinHeight(root, 100);
        YGNodeStyleSetMaxHeight(root, 110);
        YGNodeStyleSetJustifyContent(root, YGJustify.Center);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 50);
        YGNodeStyleSetHeight(root_child1, 50);
        YGNodeInsertChild(root, root_child1, 1);
        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child2, 50);
        YGNodeStyleSetHeight(root_child2, 50);
        YGNodeInsertChild(root, root_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(110f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(-20f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(30f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(80f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(110f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(-20f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(30f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(80f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void flex_grow_to_min()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetMinHeight(root, 100);
        YGNodeStyleSetMaxHeight(root, 500);
        YGNodeStyleSetWidth(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetFlexShrink(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeight(root_child1, 50);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void flex_grow_in_at_most_container()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0_child0, 1);
        YGNodeStyleSetFlexBasis(root_child0_child0, 0);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(100f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void flex_grow_child()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeight(root_child0, 100);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetFlexBasis(root_child0, 0);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void flex_grow_within_constrained_min_max_column()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetMinHeight(root, 100);
        YGNodeStyleSetMaxHeight(root, 200);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeight(root_child1, 50);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void flex_grow_within_max_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeStyleSetMaxWidth(root_child0, 100);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeight(root_child0_child0, 20);
        YGNodeStyleSetFlexGrow(root_child0_child0, 1);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(100f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void flex_grow_within_constrained_max_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeStyleSetMaxWidth(root_child0, 300);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeight(root_child0_child0, 20);
        YGNodeStyleSetFlexGrow(root_child0_child0, 1);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void flex_root_ignored()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetMinHeight(root, 100);
        YGNodeStyleSetMaxHeight(root, 500);
        YGNodeStyleSetFlexGrow(root, 1);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexBasis(root_child0, 200);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeight(root_child1, 100);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(300f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(200f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(300f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(200f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void flex_grow_root_minimized()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetMinHeight(root, 100);
        YGNodeStyleSetMaxHeight(root, 500);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetMinHeight(root_child0, 100);
        YGNodeStyleSetMaxHeight(root_child0, 500);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexBasis(root_child0_child0, 200);
        YGNodeStyleSetFlexGrow(root_child0_child0, 1);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeight(root_child0_child1, 100);
        YGNodeInsertChild(root_child0, root_child0_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(300f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(300f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(200f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(300f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(300f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(200f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void flex_grow_height_maximized()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 500);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetMinHeight(root_child0, 100);
        YGNodeStyleSetMaxHeight(root_child0, 500);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexBasis(root_child0_child0, 200);
        YGNodeStyleSetFlexGrow(root_child0_child0, 1);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeight(root_child0_child1, 100);
        YGNodeInsertChild(root_child0, root_child0_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(400f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(400f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(400f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(400f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void flex_grow_within_constrained_min_row()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetMinWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 50);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void flex_grow_within_constrained_min_column()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetMinHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeight(root_child1, 50);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void flex_grow_within_constrained_max_row()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeight(root_child0, 100);
        YGNodeStyleSetMaxWidth(root_child0, 100);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexShrink(root_child0_child0, 1);
        YGNodeStyleSetFlexBasis(root_child0_child0, 100);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child1, 50);
        YGNodeInsertChild(root_child0, root_child0_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(100f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void flex_grow_within_constrained_max_column()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetMaxHeight(root, 100);
        YGNodeStyleSetWidth(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexShrink(root_child0, 1);
        YGNodeStyleSetFlexBasis(root_child0, 100);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeight(root_child1, 50);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void child_min_max_width_flexing()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 120);
        YGNodeStyleSetHeight(root, 50);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetMinWidth(root_child0, 60);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetFlexBasis(root_child0, 0);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetMaxWidth(root_child1, 20);
        YGNodeStyleSetFlexGrow(root_child1, 1);
        YGNodeStyleSetFlexBasisPercent(root_child1, 50);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(120f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(20f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(120f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(20f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(20f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void min_width_overrides_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetMinWidth(root, 100);
        YGNodeStyleSetWidth(root, 50);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void max_width_overrides_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetMaxWidth(root, 100);
        YGNodeStyleSetWidth(root, 200);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void min_height_overrides_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetMinHeight(root, 100);
        YGNodeStyleSetHeight(root, 50);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void max_height_overrides_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetMaxHeight(root, 100);
        YGNodeStyleSetHeight(root, 200);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void min_max_percent_no_width_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetMinWidthPercent(root_child0, 10);
        YGNodeStyleSetMaxWidthPercent(root_child0, 10);
        YGNodeStyleSetMinHeightPercent(root_child0, 10);
        YGNodeStyleSetMaxHeightPercent(root_child0, 10);
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

}

