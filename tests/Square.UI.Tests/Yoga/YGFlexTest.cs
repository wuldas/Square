// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// @generated from yoga/tests/generated/YGFlexTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YGConfigAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGFlexTest
{
    [Fact]
    public void flex_basis_flex_grow_column()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexBasis(root_child0, 50);
        YGNodeStyleSetFlexGrow(root_child0, 1);
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
        Assert.Equal(75f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(75f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(75f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void flex_shrink_flex_grow_row()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 500);
        YGNodeStyleSetHeight(root, 500);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 500);
        YGNodeStyleSetHeight(root_child0, 100);
        YGNodeStyleSetFlexShrink(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 500);
        YGNodeStyleSetHeight(root_child1, 100);
        YGNodeStyleSetFlexShrink(root_child1, 1);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(250f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(250f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(250f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));
        Assert.Equal(250f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(250f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(250f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void flex_shrink_flex_grow_child_flex_shrink_other_child()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 500);
        YGNodeStyleSetHeight(root, 500);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 500);
        YGNodeStyleSetHeight(root_child0, 100);
        YGNodeStyleSetFlexShrink(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 500);
        YGNodeStyleSetHeight(root_child1, 100);
        YGNodeStyleSetFlexGrow(root_child1, 1);
        YGNodeStyleSetFlexShrink(root_child1, 1);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(250f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(250f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(250f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));
        Assert.Equal(250f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(250f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(250f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void flex_basis_flex_grow_row()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexBasis(root_child0, 50);
        YGNodeStyleSetFlexGrow(root_child0, 1);
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
        Assert.Equal(75f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void flex_basis_flex_shrink_column()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexBasis(root_child0, 100);
        YGNodeStyleSetFlexShrink(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexBasis(root_child1, 50);
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
    public void flex_basis_flex_shrink_row()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexBasis(root_child0, 100);
        YGNodeStyleSetFlexShrink(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexBasis(root_child1, 50);
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
    public void flex_shrink_to_zero()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 75);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 50);
        YGNodeStyleSetHeight(root_child1, 50);
        YGNodeStyleSetFlexShrink(root_child1, 1);
        YGNodeInsertChild(root, root_child1, 1);
        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child2, 50);
        YGNodeStyleSetHeight(root_child2, 50);
        YGNodeInsertChild(root, root_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(75f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(75f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void flex_basis_overrides_main_size()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetWidth(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeight(root_child0, 20);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetFlexBasis(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeight(root_child1, 10);
        YGNodeStyleSetFlexGrow(root_child1, 1);
        YGNodeInsertChild(root, root_child1, 1);
        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeight(root_child2, 10);
        YGNodeStyleSetFlexGrow(root_child2, 1);
        YGNodeInsertChild(root, root_child2, 2);
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
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(80f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child2));
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
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(80f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void flex_grow_shrink_at_most()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetWidth(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0_child0, 1);
        YGNodeStyleSetFlexShrink(root_child0_child0, 1);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void flex_grow_less_than_factor_one()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 500);
        YGNodeStyleSetWidth(root, 200);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 0.2f);
        YGNodeStyleSetFlexBasis(root_child0, 40);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child1, 0.2f);
        YGNodeInsertChild(root, root_child1, 1);
        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child2, 0.4f);
        YGNodeInsertChild(root, root_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(132f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(132f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(92f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(224f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(184f, YGNodeLayoutGetHeight(root_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(132f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(132f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(92f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(224f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(184f, YGNodeLayoutGetHeight(root_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

}

