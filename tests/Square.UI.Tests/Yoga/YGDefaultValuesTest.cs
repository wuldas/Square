// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGDefaultValuesTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YGConfigAPI;
using static Square.Rendering.Yoga.YogaValue;

namespace Square.Rendering.Yoga.Tests;

public class YGDefaultValuesTest
{
    [Fact]
    public void Assert_default_values()
    {
        var root = YGNodeNew();

        Assert.Equal(0u, (uint)YGNodeGetChildCount(root));
        Assert.Null(YGNodeGetChild(root, 1));

        Assert.Equal(YGDirection.Inherit, YGNodeStyleGetDirection(root));
        Assert.Equal(YGFlexDirection.Column, YGNodeStyleGetFlexDirection(root));
        Assert.Equal(YGJustify.FlexStart, YGNodeStyleGetJustifyContent(root));
        Assert.Equal(YGAlign.FlexStart, YGNodeStyleGetAlignContent(root));
        Assert.Equal(YGAlign.Stretch, YGNodeStyleGetAlignItems(root));
        Assert.Equal(YGAlign.Auto, YGNodeStyleGetAlignSelf(root));
        Assert.Equal(YGPositionType.Relative, YGNodeStyleGetPositionType(root));
        Assert.Equal(YGWrap.NoWrap, YGNodeStyleGetFlexWrap(root));
        Assert.Equal(YGOverflow.Visible, YGNodeStyleGetOverflow(root));
        Assert.Equal(0f, YGNodeStyleGetFlexGrow(root));
        Assert.Equal(0f, YGNodeStyleGetFlexShrink(root));
        Assert.Equal(Unit.Auto, YGNodeStyleGetFlexBasis(root).Unit);

        Assert.Equal(Unit.Undefined, YGNodeStyleGetPosition(root, YGEdge.Left).Unit);
        Assert.Equal(Unit.Undefined, YGNodeStyleGetPosition(root, YGEdge.Top).Unit);
        Assert.Equal(Unit.Undefined, YGNodeStyleGetPosition(root, YGEdge.Right).Unit);
        Assert.Equal(Unit.Undefined, YGNodeStyleGetPosition(root, YGEdge.Bottom).Unit);
        Assert.Equal(Unit.Undefined, YGNodeStyleGetPosition(root, YGEdge.Start).Unit);
        Assert.Equal(Unit.Undefined, YGNodeStyleGetPosition(root, YGEdge.End).Unit);

        Assert.Equal(Unit.Undefined, YGNodeStyleGetMargin(root, YGEdge.Left).Unit);
        Assert.Equal(Unit.Undefined, YGNodeStyleGetMargin(root, YGEdge.Top).Unit);
        Assert.Equal(Unit.Undefined, YGNodeStyleGetMargin(root, YGEdge.Right).Unit);
        Assert.Equal(Unit.Undefined, YGNodeStyleGetMargin(root, YGEdge.Bottom).Unit);
        Assert.Equal(Unit.Undefined, YGNodeStyleGetMargin(root, YGEdge.Start).Unit);
        Assert.Equal(Unit.Undefined, YGNodeStyleGetMargin(root, YGEdge.End).Unit);

        Assert.Equal(Unit.Undefined, YGNodeStyleGetPadding(root, YGEdge.Left).Unit);
        Assert.Equal(Unit.Undefined, YGNodeStyleGetPadding(root, YGEdge.Top).Unit);
        Assert.Equal(Unit.Undefined, YGNodeStyleGetPadding(root, YGEdge.Right).Unit);
        Assert.Equal(Unit.Undefined, YGNodeStyleGetPadding(root, YGEdge.Bottom).Unit);
        Assert.Equal(Unit.Undefined, YGNodeStyleGetPadding(root, YGEdge.Start).Unit);
        Assert.Equal(Unit.Undefined, YGNodeStyleGetPadding(root, YGEdge.End).Unit);

        Assert.True(YGFloatIsUndefined(YGNodeStyleGetBorder(root, YGEdge.Left)));
        Assert.True(YGFloatIsUndefined(YGNodeStyleGetBorder(root, YGEdge.Top)));
        Assert.True(YGFloatIsUndefined(YGNodeStyleGetBorder(root, YGEdge.Right)));
        Assert.True(YGFloatIsUndefined(YGNodeStyleGetBorder(root, YGEdge.Bottom)));
        Assert.True(YGFloatIsUndefined(YGNodeStyleGetBorder(root, YGEdge.Start)));
        Assert.True(YGFloatIsUndefined(YGNodeStyleGetBorder(root, YGEdge.End)));

        Assert.Equal(Unit.Auto, YGNodeStyleGetWidth(root).Unit);
        Assert.Equal(Unit.Auto, YGNodeStyleGetHeight(root).Unit);
        Assert.Equal(Unit.Undefined, YGNodeStyleGetMinWidth(root).Unit);
        Assert.Equal(Unit.Undefined, YGNodeStyleGetMinHeight(root).Unit);
        Assert.Equal(Unit.Undefined, YGNodeStyleGetMaxWidth(root).Unit);
        Assert.Equal(Unit.Undefined, YGNodeStyleGetMaxHeight(root).Unit);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(0f, YGNodeLayoutGetRight(root));
        Assert.Equal(0f, YGNodeLayoutGetBottom(root));

        Assert.Equal(0f, YGNodeLayoutGetMargin(root, YGEdge.Left));
        Assert.Equal(0f, YGNodeLayoutGetMargin(root, YGEdge.Top));
        Assert.Equal(0f, YGNodeLayoutGetMargin(root, YGEdge.Right));
        Assert.Equal(0f, YGNodeLayoutGetMargin(root, YGEdge.Bottom));

        Assert.Equal(0f, YGNodeLayoutGetPadding(root, YGEdge.Left));
        Assert.Equal(0f, YGNodeLayoutGetPadding(root, YGEdge.Top));
        Assert.Equal(0f, YGNodeLayoutGetPadding(root, YGEdge.Right));
        Assert.Equal(0f, YGNodeLayoutGetPadding(root, YGEdge.Bottom));

        Assert.Equal(0f, YGNodeLayoutGetBorder(root, YGEdge.Left));
        Assert.Equal(0f, YGNodeLayoutGetBorder(root, YGEdge.Top));
        Assert.Equal(0f, YGNodeLayoutGetBorder(root, YGEdge.Right));
        Assert.Equal(0f, YGNodeLayoutGetBorder(root, YGEdge.Bottom));

        Assert.True(YGFloatIsUndefined(YGNodeLayoutGetWidth(root)));
        Assert.True(YGFloatIsUndefined(YGNodeLayoutGetHeight(root)));
        Assert.Equal(YGDirection.Inherit, YGNodeLayoutGetDirection(root));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Assert_webdefault_values()
    {
        var config = YGConfigNew();
        YGConfigSetUseWebDefaults(config, true);
        var root = YGNodeNewWithConfig(config);

        Assert.Equal(YGFlexDirection.Row, YGNodeStyleGetFlexDirection(root));
        Assert.Equal(YGAlign.Stretch, YGNodeStyleGetAlignContent(root));
        Assert.Equal(1.0f, YGNodeStyleGetFlexShrink(root));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Assert_webdefault_values_reset()
    {
        var config = YGConfigNew();
        YGConfigSetUseWebDefaults(config, true);
        var root = YGNodeNewWithConfig(config);
        YGNodeReset(root);

        Assert.Equal(YGFlexDirection.Row, YGNodeStyleGetFlexDirection(root));
        Assert.Equal(YGAlign.Stretch, YGNodeStyleGetAlignContent(root));
        Assert.Equal(1.0f, YGNodeStyleGetFlexShrink(root));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Assert_legacy_stretch_behaviour()
    {
        var config = YGConfigNew();
        YGConfigSetErrata(config, YGErrata.StretchFlexBasis);
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root, 500);
        YGNodeStyleSetHeight(root, 500);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetAlignItems(root_child0, YGAlign.FlexStart);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0_child0, 1);
        YGNodeStyleSetFlexShrink(root_child0_child0, 1);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);

        var root_child0_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0_child0_child0, 1);
        YGNodeStyleSetFlexShrink(root_child0_child0_child0, 1);
        YGNodeInsertChild(root_child0_child0, root_child0_child0_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root_child0_child0));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0_child0_child0));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root_child0_child0_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Assert_box_sizing_border_box()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);

        Assert.Equal(YGBoxSizing.BorderBox, YGNodeStyleGetBoxSizing(root));

        YGNodeFreeRecursive(root);
    }
}
