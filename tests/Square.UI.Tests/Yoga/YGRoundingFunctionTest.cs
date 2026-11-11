// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGRoundingFunctionTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YGConfigAPI;
using static Square.Rendering.Yoga.YGPixelGridAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGRoundingFunctionTest
{
    [Fact]
    public void Rounding_value()
    {
        // Test that whole numbers are rounded to whole despite ceil/floor flags
        Assert.Equal(6.0f, YGRoundValueToPixelGrid(6.000001, 2.0, false, false));
        Assert.Equal(6.0f, YGRoundValueToPixelGrid(6.000001, 2.0, true, false));
        Assert.Equal(6.0f, YGRoundValueToPixelGrid(6.000001, 2.0, false, true));
        Assert.Equal(6.0f, YGRoundValueToPixelGrid(5.999999, 2.0, false, false));
        Assert.Equal(6.0f, YGRoundValueToPixelGrid(5.999999, 2.0, true, false));
        Assert.Equal(6.0f, YGRoundValueToPixelGrid(5.999999, 2.0, false, true));
        // Same tests for negative numbers
        Assert.Equal(-6.0f, YGRoundValueToPixelGrid(-6.000001, 2.0, false, false));
        Assert.Equal(-6.0f, YGRoundValueToPixelGrid(-6.000001, 2.0, true, false));
        Assert.Equal(-6.0f, YGRoundValueToPixelGrid(-6.000001, 2.0, false, true));
        Assert.Equal(-6.0f, YGRoundValueToPixelGrid(-5.999999, 2.0, false, false));
        Assert.Equal(-6.0f, YGRoundValueToPixelGrid(-5.999999, 2.0, true, false));
        Assert.Equal(-6.0f, YGRoundValueToPixelGrid(-5.999999, 2.0, false, true));

        // Test that numbers with fraction are rounded correctly accounting for ceil/floor flags
        Assert.Equal(6.0f, YGRoundValueToPixelGrid(6.01, 2.0, false, false));
        Assert.Equal(6.5f, YGRoundValueToPixelGrid(6.01, 2.0, true, false));
        Assert.Equal(6.0f, YGRoundValueToPixelGrid(6.01, 2.0, false, true));
        Assert.Equal(6.0f, YGRoundValueToPixelGrid(5.99, 2.0, false, false));
        Assert.Equal(6.0f, YGRoundValueToPixelGrid(5.99, 2.0, true, false));
        Assert.Equal(5.5f, YGRoundValueToPixelGrid(5.99, 2.0, false, true));
        // Same tests for negative numbers
        Assert.Equal(-6.0f, YGRoundValueToPixelGrid(-6.01, 2.0, false, false));
        Assert.Equal(-6.0f, YGRoundValueToPixelGrid(-6.01, 2.0, true, false));
        Assert.Equal(-6.5f, YGRoundValueToPixelGrid(-6.01, 2.0, false, true));
        Assert.Equal(-6.0f, YGRoundValueToPixelGrid(-5.99, 2.0, false, false));
        Assert.Equal(-5.5f, YGRoundValueToPixelGrid(-5.99, 2.0, true, false));
        Assert.Equal(-6.0f, YGRoundValueToPixelGrid(-5.99, 2.0, false, true));

        // Rounding up/down halfway values
        Assert.Equal(-3f, YGRoundValueToPixelGrid(-3.5, 1.0, false, false));
        Assert.Equal(-3f, YGRoundValueToPixelGrid(-3.4, 1.0, false, false));
        Assert.Equal(-4f, YGRoundValueToPixelGrid(-3.6, 1.0, false, false));
        Assert.Equal(-3f, YGRoundValueToPixelGrid(-3.499999, 1.0, false, false));
        Assert.Equal(-3f, YGRoundValueToPixelGrid(-3.500001, 1.0, false, false));
        Assert.Equal(-4f, YGRoundValueToPixelGrid(-3.5001, 1.0, false, false));

        Assert.Equal(-3f, YGRoundValueToPixelGrid(-3.5, 1.0, true, false));
        Assert.Equal(-3f, YGRoundValueToPixelGrid(-3.4, 1.0, true, false));
        Assert.Equal(-3f, YGRoundValueToPixelGrid(-3.6, 1.0, true, false));
        Assert.Equal(-3f, YGRoundValueToPixelGrid(-3.499999, 1.0, true, false));
        Assert.Equal(-3f, YGRoundValueToPixelGrid(-3.500001, 1.0, true, false));
        Assert.Equal(-3f, YGRoundValueToPixelGrid(-3.5001, 1.0, true, false));
        Assert.Equal(-3f, YGRoundValueToPixelGrid(-3.00001, 1.0, true, false));
        Assert.Equal(-3f, YGRoundValueToPixelGrid(-3, 1.0, true, false));

        Assert.Equal(-4f, YGRoundValueToPixelGrid(-3.5, 1.0, false, true));
        Assert.Equal(-4f, YGRoundValueToPixelGrid(-3.4, 1.0, false, true));
        Assert.Equal(-4f, YGRoundValueToPixelGrid(-3.6, 1.0, false, true));
        Assert.Equal(-4f, YGRoundValueToPixelGrid(-3.499999, 1.0, false, true));
        Assert.Equal(-4f, YGRoundValueToPixelGrid(-3.500001, 1.0, false, true));
        Assert.Equal(-4f, YGRoundValueToPixelGrid(-3.5001, 1.0, false, true));
        Assert.Equal(-3f, YGRoundValueToPixelGrid(-3.00001, 1.0, false, true));
        Assert.Equal(-3f, YGRoundValueToPixelGrid(-3, 1.0, false, true));

        // NAN is treated as expected
        Assert.True(float.IsNaN(YGRoundValueToPixelGrid(float.NaN, 1.5f, false, false)));
        Assert.True(float.IsNaN(YGRoundValueToPixelGrid(1.5f, float.NaN, false, false)));
        Assert.True(float.IsNaN(YGRoundValueToPixelGrid(float.NaN, float.NaN, false, false)));
    }

    private static YGSize MeasureText(Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        return new YGSize { Width = 10, Height = 10 };
    }

    [Fact]
    public void Consistent_rounding_during_repeated_layouts()
    {
        var config = YGConfigNew();
        YGConfigSetPointScaleFactor(config, 2);

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetMargin(root, YGEdge.Top, -1.49f);
        YGNodeStyleSetWidth(root, 500);
        YGNodeStyleSetHeight(root, 500);

        var node0 = YGNodeNewWithConfig(config);
        YGNodeInsertChild(root, node0, 0);

        var node1 = YGNodeNewWithConfig(config);
        YGNodeSetMeasureFunc(node1, MeasureText);
        YGNodeInsertChild(node0, node1, 0);

        for (int i = 0; i < 5; i++)
        {
            YGNodeStyleSetMargin(root, YGEdge.Left, (float)(i + 1));
            YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
            Assert.Equal(10f, YGNodeLayoutGetHeight(node1));
        }

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Per_node_point_scale_factor()
    {
        var config1 = YGConfigNew();
        YGConfigSetPointScaleFactor(config1, 2);

        var config2 = YGConfigNew();
        YGConfigSetPointScaleFactor(config2, 1);

        var config3 = YGConfigNew();
        YGConfigSetPointScaleFactor(config3, 0.5f);

        var root = YGNodeNewWithConfig(config1);
        YGNodeStyleSetWidth(root, 11.5f);
        YGNodeStyleSetHeight(root, 11.5f);

        var node0 = YGNodeNewWithConfig(config2);
        YGNodeStyleSetWidth(node0, 9.5f);
        YGNodeStyleSetHeight(node0, 9.5f);
        YGNodeInsertChild(root, node0, 0);

        var node1 = YGNodeNewWithConfig(config3);
        YGNodeStyleSetWidth(node1, 7);
        YGNodeStyleSetHeight(node1, 7);
        YGNodeInsertChild(node0, node1, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(11.5f, YGNodeLayoutGetWidth(root));
        Assert.Equal(11.5f, YGNodeLayoutGetHeight(root));

        Assert.Equal(10f, YGNodeLayoutGetWidth(node0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(node0));

        Assert.Equal(8f, YGNodeLayoutGetWidth(node1));
        Assert.Equal(8f, YGNodeLayoutGetHeight(node1));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Raw_layout_dimensions()
    {
        var config = YGConfigNew();
        YGConfigSetPointScaleFactor(config, 0.5f);

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root, 11.5f);
        YGNodeStyleSetHeight(root, 9.5f);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(12.0f, YGNodeLayoutGetWidth(root));
        Assert.Equal(10.0f, YGNodeLayoutGetHeight(root));
        Assert.Equal(11.5f, YGNodeLayoutGetRawWidth(root));
        Assert.Equal(9.5f, YGNodeLayoutGetRawHeight(root));

        YGNodeFreeRecursive(root);
    }
}
