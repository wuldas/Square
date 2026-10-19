// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGBaselineFuncTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGBaselineFuncTest
{
    private static float BaselineFunc(Node node, float width, float height)
    {
        var baselineValue = (float)YGNodeGetContext(node)!;
        return baselineValue;
    }

    [Fact]
    public void Align_baseline_customer_func()
    {
        var root = YGNodeNew();
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetAlignItems(root, YGAlign.Baseline);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNew();
        YGNodeStyleSetWidth(root_child1, 50);
        YGNodeStyleSetHeight(root_child1, 20);
        YGNodeInsertChild(root, root_child1, 1);

        float baselineValue = 10;
        var root_child1_child0 = YGNodeNew();
        YGNodeSetContext(root_child1_child0, baselineValue);
        YGNodeStyleSetWidth(root_child1_child0, 50);
        YGNodeSetBaselineFunc(root_child1_child0, BaselineFunc);
        YGNodeStyleSetHeight(root_child1_child0, 20);
        YGNodeInsertChild(root_child1, root_child1_child0, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child1));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child1_child0));

        YGNodeFreeRecursive(root);
    }
}
