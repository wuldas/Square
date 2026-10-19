// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGRelayoutTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YGConfigAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGRelayoutTest
{
    [Fact]
    public void Dont_cache_computed_flex_basis_between_layouts()
    {
        var config = YGConfigNew();
        YGConfigSetExperimentalFeatureEnabled(config, YGExperimentalFeature.WebFlexBasis, true);

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeightPercent(root, 100);
        YGNodeStyleSetWidthPercent(root, 100);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexBasisPercent(root_child0, 100);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, 100, float.NaN, YGDirection.LTR);
        YGNodeCalculateLayout(root, 100, 100, YGDirection.LTR);

        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Recalculate_resolvedDimonsion_onchange()
    {
        var root = YGNodeNew();

        var root_child0 = YGNodeNew();
        YGNodeStyleSetMinHeight(root_child0, 10);
        YGNodeStyleSetMaxHeight(root_child0, 10);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));

        YGNodeStyleSetMinHeight(root_child0, float.NaN);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Relayout_containing_block_size_changes()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Relative);
        YGNodeStyleSetMargin(root_child0, YGEdge.Left, 4);
        YGNodeStyleSetMargin(root_child0, YGEdge.Top, 5);
        YGNodeStyleSetMargin(root_child0, YGEdge.Right, 9);
        YGNodeStyleSetMargin(root_child0, YGEdge.Bottom, 1);
        YGNodeStyleSetPadding(root_child0, YGEdge.Left, 2);
        YGNodeStyleSetPadding(root_child0, YGEdge.Top, 9);
        YGNodeStyleSetPadding(root_child0, YGEdge.Right, 11);
        YGNodeStyleSetPadding(root_child0, YGEdge.Bottom, 13);
        YGNodeStyleSetBorder(root_child0, YGEdge.Left, 5);
        YGNodeStyleSetBorder(root_child0, YGEdge.Top, 6);
        YGNodeStyleSetBorder(root_child0, YGEdge.Right, 7);
        YGNodeStyleSetBorder(root_child0, YGEdge.Bottom, 8);
        YGNodeStyleSetWidth(root_child0, 500);
        YGNodeStyleSetHeight(root_child0, 500);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0_child0, YGPositionType.Static);
        YGNodeStyleSetMargin(root_child0_child0, YGEdge.Left, 8);
        YGNodeStyleSetMargin(root_child0_child0, YGEdge.Top, 6);
        YGNodeStyleSetMargin(root_child0_child0, YGEdge.Right, 3);
        YGNodeStyleSetMargin(root_child0_child0, YGEdge.Bottom, 9);
        YGNodeStyleSetPadding(root_child0_child0, YGEdge.Left, 1);
        YGNodeStyleSetPadding(root_child0_child0, YGEdge.Top, 7);
        YGNodeStyleSetPadding(root_child0_child0, YGEdge.Right, 9);
        YGNodeStyleSetPadding(root_child0_child0, YGEdge.Bottom, 4);
        YGNodeStyleSetBorder(root_child0_child0, YGEdge.Left, 8);
        YGNodeStyleSetBorder(root_child0_child0, YGEdge.Top, 10);
        YGNodeStyleSetBorder(root_child0_child0, YGEdge.Right, 2);
        YGNodeStyleSetBorder(root_child0_child0, YGEdge.Bottom, 1);
        YGNodeStyleSetWidth(root_child0_child0, 200);
        YGNodeStyleSetHeight(root_child0_child0, 200);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);

        var root_child0_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root_child0_child0_child0, YGPositionType.Absolute);
        YGNodeStyleSetPosition(root_child0_child0_child0, YGEdge.Left, 2);
        YGNodeStyleSetPosition(root_child0_child0_child0, YGEdge.Right, 12);
        YGNodeStyleSetMargin(root_child0_child0_child0, YGEdge.Left, 9);
        YGNodeStyleSetMargin(root_child0_child0_child0, YGEdge.Top, 12);
        YGNodeStyleSetMargin(root_child0_child0_child0, YGEdge.Right, 4);
        YGNodeStyleSetMargin(root_child0_child0_child0, YGEdge.Bottom, 7);
        YGNodeStyleSetPadding(root_child0_child0_child0, YGEdge.Left, 5);
        YGNodeStyleSetPadding(root_child0_child0_child0, YGEdge.Top, 3);
        YGNodeStyleSetPadding(root_child0_child0_child0, YGEdge.Right, 8);
        YGNodeStyleSetPadding(root_child0_child0_child0, YGEdge.Bottom, 10);
        YGNodeStyleSetBorder(root_child0_child0_child0, YGEdge.Left, 2);
        YGNodeStyleSetBorder(root_child0_child0_child0, YGEdge.Top, 1);
        YGNodeStyleSetBorder(root_child0_child0_child0, YGEdge.Right, 5);
        YGNodeStyleSetBorder(root_child0_child0_child0, YGEdge.Bottom, 9);
        YGNodeStyleSetWidthPercent(root_child0_child0_child0, 41);
        YGNodeStyleSetHeightPercent(root_child0_child0_child0, 63);
        YGNodeInsertChild(root_child0_child0, root_child0_child0_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(513f, YGNodeLayoutGetWidth(root));
        Assert.Equal(506f, YGNodeLayoutGetHeight(root));

        Assert.Equal(4f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(5f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(15f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(21f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0_child0));

        Assert.Equal(1f, YGNodeLayoutGetLeft(root_child0_child0_child0));
        Assert.Equal(29f, YGNodeLayoutGetTop(root_child0_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0_child0_child0));
        Assert.Equal(306f, YGNodeLayoutGetHeight(root_child0_child0_child0));

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(513f, YGNodeLayoutGetWidth(root));
        Assert.Equal(506f, YGNodeLayoutGetHeight(root));

        Assert.Equal(4f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(5f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(279f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(21f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0_child0));

        Assert.Equal(-2f, YGNodeLayoutGetLeft(root_child0_child0_child0));
        Assert.Equal(29f, YGNodeLayoutGetTop(root_child0_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0_child0_child0));
        Assert.Equal(306f, YGNodeLayoutGetHeight(root_child0_child0_child0));

        // Relayout starts here
        YGNodeStyleSetWidth(root_child0, 456);
        YGNodeStyleSetHeight(root_child0, 432);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(469f, YGNodeLayoutGetWidth(root));
        Assert.Equal(438f, YGNodeLayoutGetHeight(root));

        Assert.Equal(4f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(5f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(456f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(432f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(15f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(21f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0_child0));

        Assert.Equal(1f, YGNodeLayoutGetLeft(root_child0_child0_child0));
        Assert.Equal(29f, YGNodeLayoutGetTop(root_child0_child0_child0));
        Assert.Equal(182f, YGNodeLayoutGetWidth(root_child0_child0_child0));
        Assert.Equal(263f, YGNodeLayoutGetHeight(root_child0_child0_child0));

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(469f, YGNodeLayoutGetWidth(root));
        Assert.Equal(438f, YGNodeLayoutGetHeight(root));

        Assert.Equal(4f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(5f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(456f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(432f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(235f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(21f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root_child0_child0));

        Assert.Equal(16f, YGNodeLayoutGetLeft(root_child0_child0_child0));
        Assert.Equal(29f, YGNodeLayoutGetTop(root_child0_child0_child0));
        Assert.Equal(182f, YGNodeLayoutGetWidth(root_child0_child0_child0));
        Assert.Equal(263f, YGNodeLayoutGetHeight(root_child0_child0_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Has_new_layout_flag_set_static()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Static);
        YGNodeStyleSetWidth(root_child0, 10);
        YGNodeStyleSetHeight(root_child0, 10);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child0_child1 = YGNodeNew();
        YGNodeStyleSetPositionType(root_child0_child1, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root_child0_child1, 5);
        YGNodeStyleSetHeight(root_child0_child1, 5);
        YGNodeInsertChild(root_child0, root_child0_child1, 0);

        var root_child0_child0 = YGNodeNew();
        YGNodeStyleSetPositionType(root_child0_child0, YGPositionType.Static);
        YGNodeStyleSetWidth(root_child0_child0, 5);
        YGNodeStyleSetHeight(root_child0_child0, 5);
        YGNodeInsertChild(root_child0, root_child0_child0, 1);

        var root_child0_child0_child0 = YGNodeNew();
        YGNodeStyleSetPositionType(root_child0_child0_child0, YGPositionType.Absolute);
        YGNodeStyleSetWidthPercent(root_child0_child0_child0, 1);
        YGNodeStyleSetHeight(root_child0_child0_child0, 1);
        YGNodeInsertChild(root_child0_child0, root_child0_child0_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        YGNodeSetHasNewLayout(root, false);
        YGNodeSetHasNewLayout(root_child0, false);
        YGNodeSetHasNewLayout(root_child0_child0, false);
        YGNodeSetHasNewLayout(root_child0_child0_child0, false);

        YGNodeStyleSetWidth(root, 110);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.True(YGNodeGetHasNewLayout(root));
        Assert.True(YGNodeGetHasNewLayout(root_child0));
        Assert.True(YGNodeGetHasNewLayout(root_child0_child0));
        Assert.True(YGNodeGetHasNewLayout(root_child0_child0_child0));

        YGNodeFreeRecursive(root);
    }
}
