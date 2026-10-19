// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// @generated from yoga/tests/generated/YGDisplayContentsTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YGConfigAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGDisplayContentsTest
{
    [Fact]
    public void test1()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetDisplay(root_child0, YGDisplay.Contents);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0_child0, 1);
        YGNodeStyleSetFlexShrink(root_child0_child0, 1);
        YGNodeStyleSetFlexBasisPercent(root_child0_child0, 0);
        YGNodeStyleSetHeight(root_child0_child0, 10);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0_child1, 1);
        YGNodeStyleSetFlexShrink(root_child0_child1, 1);
        YGNodeStyleSetFlexBasisPercent(root_child0_child1, 0);
        YGNodeStyleSetHeight(root_child0_child1, 20);
        YGNodeInsertChild(root_child0, root_child0_child1, 1);
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
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

}

