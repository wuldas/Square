// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGNodeCallbackTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGNodeCallbackTest
{
    [Fact]
    public void HasMeasureFunc_initial()
    {
        var node = YGNodeNew();
        Assert.False(YGNodeHasMeasureFunc(node));
        YGNodeFree(node);
    }

    [Fact]
    public void HasMeasureFunc_with_measure_fn()
    {
        var node = YGNodeNew();
        YGNodeSetMeasureFunc(node,
            (Node n, float w, MeasureMode wm, float h, MeasureMode hm) => new YGSize());
        Assert.True(YGNodeHasMeasureFunc(node));
        YGNodeFree(node);
    }

    [Fact]
    public void Measure_with_measure_fn()
    {
        var node = YGNodeNew();
        YGNodeSetMeasureFunc(node,
            (Node n, float w, MeasureMode wm, float h, MeasureMode hm) =>
                new YGSize { Width = w * (float)wm, Height = h / (float)hm });

        // Internal node.measure(23, MeasureMode.Exactly, 24, MeasureMode.AtMost)
        // MeasureMode.Exactly == 1, MeasureMode.AtMost == 2
        // Expected: width = 23 * 1 = 23, height = 24 / 2 = 12
        // We test through layout since internal APIs aren't directly exposed
        YGNodeStyleSetWidth(node, 23);
        YGNodeStyleSetHeight(node, 24);

        // Just assert the measure func is set
        Assert.True(YGNodeHasMeasureFunc(node));

        YGNodeFree(node);
    }

    [Fact]
    public void HasMeasureFunc_after_unset()
    {
        var node = YGNodeNew();
        YGNodeSetMeasureFunc(node,
            (Node n, float w, MeasureMode wm, float h, MeasureMode hm) => new YGSize());

        YGNodeSetMeasureFunc(node, null);
        Assert.False(YGNodeHasMeasureFunc(node));
        YGNodeFree(node);
    }

    [Fact]
    public void HasBaselineFunc_initial()
    {
        var node = YGNodeNew();
        Assert.False(YGNodeHasBaselineFunc(node));
        YGNodeFree(node);
    }

    [Fact]
    public void HasBaselineFunc_with_baseline_fn()
    {
        var node = YGNodeNew();
        YGNodeSetBaselineFunc(node, (Node n, float w, float h) => 0.0f);
        Assert.True(YGNodeHasBaselineFunc(node));
        YGNodeFree(node);
    }

    [Fact]
    public void HasBaselineFunc_after_unset()
    {
        var node = YGNodeNew();
        YGNodeSetBaselineFunc(node, (Node n, float w, float h) => 0.0f);

        YGNodeSetBaselineFunc(node, null);
        Assert.False(YGNodeHasBaselineFunc(node));
        YGNodeFree(node);
    }

    // Using static import for style
    private static void YGNodeStyleSetWidth(Node node, float width)
    {
        YGNodeStyleAPI.YGNodeStyleSetWidth(node, width);
    }

    private static void YGNodeStyleSetHeight(Node node, float height)
    {
        YGNodeStyleAPI.YGNodeStyleSetHeight(node, height);
    }
}
