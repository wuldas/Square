// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGScaleChangeTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YGConfigAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGScaleChangeTest
{
    [Fact]
    public void Scale_change_invalidates_layout()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGConfigSetPointScaleFactor(config, 1.0f);

        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetWidth(root, 50);
        YGNodeStyleSetHeight(root, 50);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child1, 1);
        YGNodeInsertChild(root, root_child1, 1);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child1));

        YGConfigSetPointScaleFactor(config, 1.5f);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(25.333334f, YGNodeLayoutGetLeft(root_child1));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Errata_config_change_relayout()
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

        YGConfigSetErrata(config, YGErrata.None);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0_child0));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetWidth(root_child0_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(root_child0_child0_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Setting_compatible_config_maintains_layout_cache()
    {
        int measureCallCount = 0;
        YGMeasureFunc measureCustom = (Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode) =>
        {
            measureCallCount++;
            return new YGSize { Width = 25.0f, Height = 25.0f };
        };

        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGConfigSetPointScaleFactor(config, 1.0f);

        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetWidth(root, 50);
        YGNodeStyleSetHeight(root, 50);

        var root_child0 = YGNodeNewWithConfig(config);
        Assert.Equal(0, measureCallCount);

        YGNodeSetMeasureFunc(root_child0, measureCustom);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child1, 1);
        YGNodeInsertChild(root, root_child1, 1);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(1, measureCallCount);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child1));

        var config2 = YGConfigNew();
        YGConfigSetPointScaleFactor(config2, 1.0f);
        YGConfigSetPointScaleFactor(config2, 1.5f);
        YGConfigSetPointScaleFactor(config2, 1.0f);

        YGNodeSetConfig(root, config2);
        YGNodeSetConfig(root_child0, config2);
        YGNodeSetConfig(root_child1, config2);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(1, measureCallCount);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child1));

        YGNodeFreeRecursive(root);
    }
}
