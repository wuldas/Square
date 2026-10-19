// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// @generated from yoga/tests/generated/YGBoxSizingTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YGConfigAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGBoxSizingTest
{
    [Fact]
    public void box_sizing_content_box_simple()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetPadding(root, YGEdge.All, 5);
        YGNodeStyleSetBorder(root, YGEdge.All, 10);
        YGNodeStyleSetBoxSizing(root, YGBoxSizing.ContentBox);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(130f, YGNodeLayoutGetWidth(root));
        Assert.Equal(130f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(130f, YGNodeLayoutGetWidth(root));
        Assert.Equal(130f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_simple()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetPadding(root, YGEdge.All, 5);
        YGNodeStyleSetBorder(root, YGEdge.All, 10);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_percent()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0, 50);
        YGNodeStyleSetHeightPercent(root_child0, 25);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 4);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 16);
        YGNodeStyleSetBoxSizing(root_child0, YGBoxSizing.ContentBox);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(90f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(65f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(90f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(65f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_percent()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0, 50);
        YGNodeStyleSetHeightPercent(root_child0, 25);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 4);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 16);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_absolute()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeightPercent(root_child0, 25);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 12);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 8);
        YGNodeStyleSetBoxSizing(root_child0, YGBoxSizing.ContentBox);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(65f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(60f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(65f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_absolute()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeightPercent(root_child0, 25);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 12);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 8);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Absolute);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(60f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_comtaining_block()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetPadding(root, YGEdge.All, 12);
        YGNodeStyleSetBorder(root, YGEdge.All, 8);
        YGNodeStyleSetBoxSizing(root, YGBoxSizing.ContentBox);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Static);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 50);
        YGNodeStyleSetHeightPercent(root_child0_child0, 25);
        YGNodeStyleSetPositionType(root_child0_child0, YGPositionType.Absolute);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(140f, YGNodeLayoutGetWidth(root));
        Assert.Equal(140f, YGNodeLayoutGetHeight(root));
        Assert.Equal(20f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(31f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(140f, YGNodeLayoutGetWidth(root));
        Assert.Equal(140f, YGNodeLayoutGetHeight(root));
        Assert.Equal(20f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(31f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_comtaining_block()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetPadding(root, YGEdge.All, 12);
        YGNodeStyleSetBorder(root, YGEdge.All, 8);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Static);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 50);
        YGNodeStyleSetHeightPercent(root_child0_child0, 25);
        YGNodeStyleSetPositionType(root_child0_child0, YGPositionType.Absolute);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(20f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(21f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(20f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(21f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_padding_only()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetPadding(root, YGEdge.All, 5);
        YGNodeStyleSetBoxSizing(root, YGBoxSizing.ContentBox);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(110f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(110f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_padding_only_percent()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 150);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 75);
        YGNodeStyleSetPaddingPercent(root_child0, YGEdge.All, 10);
        YGNodeStyleSetBoxSizing(root_child0, YGBoxSizing.ContentBox);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(70f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(95f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root));
        Assert.Equal(30f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(70f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(95f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_padding_only()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetPadding(root, YGEdge.All, 5);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_padding_only_percent()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 150);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 75);
        YGNodeStyleSetPaddingPercent(root_child0, YGEdge.All, 10);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_border_only()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetBorder(root, YGEdge.All, 10);
        YGNodeStyleSetBoxSizing(root, YGBoxSizing.ContentBox);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(120f, YGNodeLayoutGetWidth(root));
        Assert.Equal(120f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(120f, YGNodeLayoutGetWidth(root));
        Assert.Equal(120f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_border_only_percent()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0, 50);
        YGNodeStyleSetBoxSizing(root_child0, YGBoxSizing.ContentBox);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_border_only()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetBorder(root, YGEdge.All, 10);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_border_only_percent()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthPercent(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_no_padding_no_border()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetBoxSizing(root, YGBoxSizing.ContentBox);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_no_padding_no_border()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_children()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetPadding(root, YGEdge.All, 5);
        YGNodeStyleSetBorder(root, YGEdge.All, 10);
        YGNodeStyleSetBoxSizing(root, YGBoxSizing.ContentBox);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 25);
        YGNodeStyleSetHeight(root_child0, 25);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 25);
        YGNodeStyleSetHeight(root_child1, 25);
        YGNodeInsertChild(root, root_child1, 1);
        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child2, 25);
        YGNodeStyleSetHeight(root_child2, 25);
        YGNodeInsertChild(root, root_child2, 2);
        var root_child3 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child3, 25);
        YGNodeStyleSetHeight(root_child3, 25);
        YGNodeInsertChild(root, root_child3, 3);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(130f, YGNodeLayoutGetWidth(root));
        Assert.Equal(130f, YGNodeLayoutGetHeight(root));
        Assert.Equal(15f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(15f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(15f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(15f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(65f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child2));
        Assert.Equal(15f, YGNodeLayoutGetLeft(root_child3));
        Assert.Equal(90f, YGNodeLayoutGetTop(root_child3));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child3));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child3));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(130f, YGNodeLayoutGetWidth(root));
        Assert.Equal(130f, YGNodeLayoutGetHeight(root));
        Assert.Equal(90f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(15f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(90f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(90f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(65f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child2));
        Assert.Equal(90f, YGNodeLayoutGetLeft(root_child3));
        Assert.Equal(90f, YGNodeLayoutGetTop(root_child3));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child3));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child3));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_children()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetPadding(root, YGEdge.All, 5);
        YGNodeStyleSetBorder(root, YGEdge.All, 10);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 25);
        YGNodeStyleSetHeight(root_child0, 25);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 25);
        YGNodeStyleSetHeight(root_child1, 25);
        YGNodeInsertChild(root, root_child1, 1);
        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child2, 25);
        YGNodeStyleSetHeight(root_child2, 25);
        YGNodeInsertChild(root, root_child2, 2);
        var root_child3 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child3, 25);
        YGNodeStyleSetHeight(root_child3, 25);
        YGNodeInsertChild(root, root_child3, 3);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(15f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(15f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(15f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(15f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(65f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child2));
        Assert.Equal(15f, YGNodeLayoutGetLeft(root_child3));
        Assert.Equal(90f, YGNodeLayoutGetTop(root_child3));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child3));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child3));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(60f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(15f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(60f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(65f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child2));
        Assert.Equal(60f, YGNodeLayoutGetLeft(root_child3));
        Assert.Equal(90f, YGNodeLayoutGetTop(root_child3));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child3));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child3));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_siblings()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 25);
        YGNodeStyleSetHeight(root_child0, 25);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 25);
        YGNodeStyleSetHeight(root_child1, 25);
        YGNodeStyleSetBoxSizing(root_child1, YGBoxSizing.ContentBox);
        YGNodeStyleSetPadding(root_child1, YGEdge.All, 10);
        YGNodeStyleSetBorder(root_child1, YGEdge.All, 10);
        YGNodeInsertChild(root, root_child1, 1);
        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child2, 25);
        YGNodeStyleSetHeight(root_child2, 25);
        YGNodeInsertChild(root, root_child2, 2);
        var root_child3 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child3, 25);
        YGNodeStyleSetHeight(root_child3, 25);
        YGNodeInsertChild(root, root_child3, 3);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(65f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(65f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(90f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child2));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child3));
        Assert.Equal(115f, YGNodeLayoutGetTop(root_child3));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child3));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child3));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(35f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(65f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(65f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(90f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child2));
        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child3));
        Assert.Equal(115f, YGNodeLayoutGetTop(root_child3));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child3));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child3));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_siblings()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 25);
        YGNodeStyleSetHeight(root_child0, 25);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 25);
        YGNodeStyleSetHeight(root_child1, 25);
        YGNodeStyleSetPadding(root_child1, YGEdge.All, 10);
        YGNodeStyleSetBorder(root_child1, YGEdge.All, 10);
        YGNodeInsertChild(root, root_child1, 1);
        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child2, 25);
        YGNodeStyleSetHeight(root_child2, 25);
        YGNodeInsertChild(root, root_child2, 2);
        var root_child3 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child3, 25);
        YGNodeStyleSetHeight(root_child3, 25);
        YGNodeInsertChild(root, root_child3, 3);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(65f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child2));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child3));
        Assert.Equal(90f, YGNodeLayoutGetTop(root_child3));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child3));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child3));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(65f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child2));
        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child3));
        Assert.Equal(90f, YGNodeLayoutGetTop(root_child3));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child3));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child3));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_max_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetMaxWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 25);
        YGNodeStyleSetBoxSizing(root_child0, YGBoxSizing.ContentBox);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 5);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 15);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 25);
        YGNodeStyleSetHeight(root_child1, 25);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(90f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(65f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(65f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(90f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(65f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(65f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_max_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetMaxWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 25);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 5);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 15);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 25);
        YGNodeStyleSetHeight(root_child1, 25);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_max_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetMaxHeight(root_child0, 50);
        YGNodeStyleSetBoxSizing(root_child0, YGBoxSizing.ContentBox);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 5);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 15);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 25);
        YGNodeStyleSetHeight(root_child1, 25);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(90f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(90f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_max_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetMaxHeight(root_child0, 50);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 5);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 15);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 25);
        YGNodeStyleSetHeight(root_child1, 25);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_min_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetMinWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 25);
        YGNodeStyleSetBoxSizing(root_child0, YGBoxSizing.ContentBox);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 5);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 15);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 25);
        YGNodeStyleSetHeight(root_child1, 25);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(65f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(65f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(65f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(65f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_min_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetMinWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 25);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 5);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 15);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 25);
        YGNodeStyleSetHeight(root_child1, 25);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_min_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetMinHeight(root_child0, 50);
        YGNodeStyleSetBoxSizing(root_child0, YGBoxSizing.ContentBox);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 5);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 15);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 25);
        YGNodeStyleSetHeight(root_child1, 25);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(90f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(90f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(90f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(90f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(90f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(90f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_min_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetMinHeight(root_child0, 50);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 5);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 15);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 25);
        YGNodeStyleSetHeight(root_child1, 25);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_no_height_no_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetBoxSizing(root_child0, YGBoxSizing.ContentBox);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 2);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 7);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(18f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(18f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_no_height_no_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 2);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 7);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(18f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(18f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_nested()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetBoxSizing(root, YGBoxSizing.ContentBox);
        YGNodeStyleSetPadding(root, YGEdge.All, 15);
        YGNodeStyleSetBorder(root, YGEdge.All, 3);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 20);
        YGNodeStyleSetHeight(root_child0, 20);
        YGNodeStyleSetBoxSizing(root_child0, YGBoxSizing.ContentBox);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 2);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 7);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 10);
        YGNodeStyleSetHeight(root_child0_child0, 5);
        YGNodeStyleSetBoxSizing(root_child0_child0, YGBoxSizing.ContentBox);
        YGNodeStyleSetPadding(root_child0_child0, YGEdge.All, 1);
        YGNodeStyleSetBorder(root_child0_child0, YGEdge.All, 2);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(136f, YGNodeLayoutGetWidth(root));
        Assert.Equal(136f, YGNodeLayoutGetHeight(root));
        Assert.Equal(18f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(18f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(38f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(38f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(9f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(9f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(16f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(11f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(136f, YGNodeLayoutGetWidth(root));
        Assert.Equal(136f, YGNodeLayoutGetHeight(root));
        Assert.Equal(80f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(18f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(38f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(38f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(13f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(9f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(16f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(11f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_nested()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetPadding(root, YGEdge.All, 15);
        YGNodeStyleSetBorder(root, YGEdge.All, 3);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 20);
        YGNodeStyleSetHeight(root_child0, 20);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 2);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 7);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 10);
        YGNodeStyleSetHeight(root_child0_child0, 5);
        YGNodeStyleSetPadding(root_child0_child0, YGEdge.All, 1);
        YGNodeStyleSetBorder(root_child0_child0, YGEdge.All, 2);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(18f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(18f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(9f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(9f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(6f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(62f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(18f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(1f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(9f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(6f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_nested_alternating()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetBoxSizing(root, YGBoxSizing.ContentBox);
        YGNodeStyleSetPadding(root, YGEdge.All, 3);
        YGNodeStyleSetBorder(root, YGEdge.All, 2);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 40);
        YGNodeStyleSetHeight(root_child0, 40);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 8);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 2);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 20);
        YGNodeStyleSetHeight(root_child0_child0, 25);
        YGNodeStyleSetBoxSizing(root_child0_child0, YGBoxSizing.ContentBox);
        YGNodeStyleSetPadding(root_child0_child0, YGEdge.All, 3);
        YGNodeStyleSetBorder(root_child0_child0, YGEdge.All, 6);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0_child0, 10);
        YGNodeStyleSetHeight(root_child0_child0_child0, 5);
        YGNodeStyleSetPadding(root_child0_child0_child0, YGEdge.All, 1);
        YGNodeStyleSetBorder(root_child0_child0_child0, YGEdge.All, 1);
        YGNodeInsertChild(root_child0_child0, root_child0_child0_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(110f, YGNodeLayoutGetHeight(root));
        Assert.Equal(5f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(5f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(38f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(43f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(9f, YGNodeLayoutGetLeft(root_child0_child0_child0));
        Assert.Equal(9f, YGNodeLayoutGetTop(root_child0_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0_child0_child0));
        Assert.Equal(5f, YGNodeLayoutGetHeight(root_child0_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root));
        Assert.Equal(110f, YGNodeLayoutGetHeight(root));
        Assert.Equal(65f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(5f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(-8f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(38f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(43f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(19f, YGNodeLayoutGetLeft(root_child0_child0_child0));
        Assert.Equal(9f, YGNodeLayoutGetTop(root_child0_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetWidth(root_child0_child0_child0));
        Assert.Equal(5f, YGNodeLayoutGetHeight(root_child0_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_nested_alternating()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetPadding(root, YGEdge.All, 3);
        YGNodeStyleSetBorder(root, YGEdge.All, 2);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 40);
        YGNodeStyleSetHeight(root_child0, 40);
        YGNodeStyleSetBoxSizing(root_child0, YGBoxSizing.ContentBox);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 8);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 2);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 20);
        YGNodeStyleSetHeight(root_child0_child0, 25);
        YGNodeStyleSetPadding(root_child0_child0, YGEdge.All, 3);
        YGNodeStyleSetBorder(root_child0_child0, YGEdge.All, 6);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0_child0, 10);
        YGNodeStyleSetHeight(root_child0_child0_child0, 5);
        YGNodeStyleSetBoxSizing(root_child0_child0_child0, YGBoxSizing.ContentBox);
        YGNodeStyleSetPadding(root_child0_child0_child0, YGEdge.All, 1);
        YGNodeStyleSetBorder(root_child0_child0_child0, YGEdge.All, 1);
        YGNodeInsertChild(root_child0_child0, root_child0_child0_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(5f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(5f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(20f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(9f, YGNodeLayoutGetLeft(root_child0_child0_child0));
        Assert.Equal(9f, YGNodeLayoutGetTop(root_child0_child0_child0));
        Assert.Equal(14f, YGNodeLayoutGetWidth(root_child0_child0_child0));
        Assert.Equal(9f, YGNodeLayoutGetHeight(root_child0_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(35f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(5f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(20f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(-3f, YGNodeLayoutGetLeft(root_child0_child0_child0));
        Assert.Equal(9f, YGNodeLayoutGetTop(root_child0_child0_child0));
        Assert.Equal(14f, YGNodeLayoutGetWidth(root_child0_child0_child0));
        Assert.Equal(9f, YGNodeLayoutGetHeight(root_child0_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_flex_basis_row()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexBasis(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 25);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 5);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 10);
        YGNodeStyleSetBoxSizing(root_child0, YGBoxSizing.ContentBox);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(55f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(20f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(55f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_flex_basis_row()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexBasis(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 25);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 5);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 10);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_flex_basis_column()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexBasis(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 25);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 5);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 10);
        YGNodeStyleSetBoxSizing(root_child0, YGBoxSizing.ContentBox);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_flex_basis_column()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexBasis(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 25);
        YGNodeStyleSetPadding(root_child0, YGEdge.All, 5);
        YGNodeStyleSetBorder(root_child0, YGEdge.All, 10);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_padding_start()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetPadding(root, YGEdge.Start, 5);
        YGNodeStyleSetBoxSizing(root, YGBoxSizing.ContentBox);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(105f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(105f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_padding_start()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetPadding(root, YGEdge.Start, 5);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_padding_end()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetPadding(root, YGEdge.End, 5);
        YGNodeStyleSetBoxSizing(root, YGBoxSizing.ContentBox);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(105f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(105f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_padding_end()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetPadding(root, YGEdge.End, 5);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_border_start()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetBorder(root, YGEdge.Start, 5);
        YGNodeStyleSetBoxSizing(root, YGBoxSizing.ContentBox);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(105f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(105f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_border_start()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetBorder(root, YGEdge.Start, 5);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_content_box_border_end()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetBorder(root, YGEdge.End, 5);
        YGNodeStyleSetBoxSizing(root, YGBoxSizing.ContentBox);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(105f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(105f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void box_sizing_border_box_border_end()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetBorder(root, YGEdge.End, 5);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

}

