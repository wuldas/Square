// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// @generated from yoga/tests/generated/YGPaddingTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YGConfigAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGPaddingTest
{
    [Fact]
    public void padding_no_size()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetPadding(root, YGEdge.All, 10);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(20f, YGNodeLayoutGetWidth(root));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(20f, YGNodeLayoutGetWidth(root));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void padding_container_match_child()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetPadding(root, YGEdge.All, 10);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 10);
        YGNodeStyleSetHeight(root_child0, 10);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(30f, YGNodeLayoutGetWidth(root));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(30f, YGNodeLayoutGetWidth(root));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void padding_flex_child()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetPadding(root, YGEdge.All, 10);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 10);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(80f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void padding_stretch_child()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetPadding(root, YGEdge.All, 10);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeight(root_child0, 10);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void padding_center_child()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetPadding(root, YGEdge.Start, 10);
        YGNodeStyleSetPadding(root, YGEdge.End, 20);
        YGNodeStyleSetPadding(root, YGEdge.Bottom, 20);
        YGNodeStyleSetAlignItems(root, YGAlign.Center);
        YGNodeStyleSetJustifyContent(root, YGJustify.Center);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeight(root_child0, 10);
        YGNodeStyleSetWidth(root_child0, 10);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(40f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(35f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(35f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void child_with_padding_align_end()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);
        YGNodeStyleSetJustifyContent(root, YGJustify.FlexEnd);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexEnd);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 100);
        YGNodeStyleSetHeight(root_child0, 100);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 20);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(100f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void physical_and_relative_edge_defined()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);
        YGNodeStyleSetPadding(root, YGEdge.Left, 20);
        YGNodeStyleSetPadding(root, YGEdge.End, 50);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0, 100);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(20f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(130f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(150f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

}

