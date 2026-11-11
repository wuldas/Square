// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGRoundingMeasureFuncTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YGConfigAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGRoundingMeasureFuncTest
{
    private static YGSize MeasureFloor(Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        return new YGSize { Width = 10.2f, Height = 10.2f };
    }

    private static YGSize MeasureCeil(Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        return new YGSize { Width = 10.5f, Height = 10.5f };
    }

    private static YGSize MeasureFractial(Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        return new YGSize { Width = 0.5f, Height = 0.5f };
    }

    [Fact]
    public void Rounding_feature_with_custom_measure_func_floor()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeSetMeasureFunc(root_child0, MeasureFloor);
        YGNodeInsertChild(root, root_child0, 0);

        YGConfigSetPointScaleFactor(config, 0.0f);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(10.2f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10.2f, YGNodeLayoutGetHeight(root_child0));

        YGConfigSetPointScaleFactor(config, 1.0f);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(11f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(11f, YGNodeLayoutGetHeight(root_child0));

        YGConfigSetPointScaleFactor(config, 2.0f);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(10.5f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10.5f, YGNodeLayoutGetHeight(root_child0));

        YGConfigSetPointScaleFactor(config, 4.0f);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(10.25f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10.25f, YGNodeLayoutGetHeight(root_child0));

        YGConfigSetPointScaleFactor(config, 1.0f / 3.0f);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(12.0f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(12.0f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Rounding_feature_with_custom_measure_func_ceil()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeSetMeasureFunc(root_child0, MeasureCeil);
        YGNodeInsertChild(root, root_child0, 0);

        YGConfigSetPointScaleFactor(config, 1.0f);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(11f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(11f, YGNodeLayoutGetHeight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Rounding_feature_with_custom_measure_and_fractial_matching_scale()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetPosition(root_child0, YGEdge.Left, 73.625f);
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Relative);
        YGNodeSetMeasureFunc(root_child0, MeasureFractial);
        YGNodeInsertChild(root, root_child0, 0);

        YGConfigSetPointScaleFactor(config, 2.0f);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0.5f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(0.5f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(73.5f, YGNodeLayoutGetLeft(root_child0));

        YGNodeFreeRecursive(root);
    }
}
