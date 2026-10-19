// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGComputedMarginTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGComputedMarginTest
{
    [Fact]
    public void Computed_layout_margin()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetMarginPercent(root, YGEdge.Start, 10);

        YGNodeCalculateLayout(root, 100, 100, YGDirection.LTR);

        Assert.Equal(10f, YGNodeLayoutGetMargin(root, YGEdge.Left));
        Assert.Equal(0f, YGNodeLayoutGetMargin(root, YGEdge.Right));

        YGNodeCalculateLayout(root, 100, 100, YGDirection.RTL);

        Assert.Equal(0f, YGNodeLayoutGetMargin(root, YGEdge.Left));
        Assert.Equal(10f, YGNodeLayoutGetMargin(root, YGEdge.Right));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Margin_side_overrides_horizontal_and_vertical()
    {
        YGEdge[] edges = { YGEdge.Top, YGEdge.Bottom, YGEdge.Start, YGEdge.End, YGEdge.Left, YGEdge.Right };

        for (float edgeValue = 0; edgeValue < 2; ++edgeValue)
        {
            foreach (var edge in edges)
            {
                YGEdge horizontalOrVertical = (edge == YGEdge.Top || edge == YGEdge.Bottom)
                    ? YGEdge.Vertical
                    : YGEdge.Horizontal;

                var root = YGNodeNew();
                YGNodeStyleSetWidth(root, 100);
                YGNodeStyleSetHeight(root, 100);
                YGNodeStyleSetMargin(root, horizontalOrVertical, 10);
                YGNodeStyleSetMargin(root, edge, edgeValue);

                YGNodeCalculateLayout(root, 100, 100, YGDirection.LTR);

                Assert.Equal(edgeValue, YGNodeLayoutGetMargin(root, edge));

                YGNodeFreeRecursive(root);
            }
        }
    }

    [Fact]
    public void Margin_side_overrides_all()
    {
        YGEdge[] edges = { YGEdge.Top, YGEdge.Bottom, YGEdge.Start, YGEdge.End, YGEdge.Left, YGEdge.Right };

        for (float edgeValue = 0; edgeValue < 2; ++edgeValue)
        {
            foreach (var edge in edges)
            {
                var root = YGNodeNew();
                YGNodeStyleSetWidth(root, 100);
                YGNodeStyleSetHeight(root, 100);
                YGNodeStyleSetMargin(root, YGEdge.All, 10);
                YGNodeStyleSetMargin(root, edge, edgeValue);

                YGNodeCalculateLayout(root, 100, 100, YGDirection.LTR);

                Assert.Equal(edgeValue, YGNodeLayoutGetMargin(root, edge));

                YGNodeFreeRecursive(root);
            }
        }
    }

    [Fact]
    public void Margin_horizontal_and_vertical_overrides_all()
    {
        YGEdge[] directions = { YGEdge.Horizontal, YGEdge.Vertical };

        for (float directionValue = 0; directionValue < 2; ++directionValue)
        {
            foreach (var direction in directions)
            {
                var root = YGNodeNew();
                YGNodeStyleSetWidth(root, 100);
                YGNodeStyleSetHeight(root, 100);
                YGNodeStyleSetMargin(root, YGEdge.All, 10);
                YGNodeStyleSetMargin(root, direction, directionValue);

                YGNodeCalculateLayout(root, 100, 100, YGDirection.LTR);

                if (direction == YGEdge.Vertical)
                {
                    Assert.Equal(directionValue, YGNodeLayoutGetMargin(root, YGEdge.Top));
                    Assert.Equal(directionValue, YGNodeLayoutGetMargin(root, YGEdge.Bottom));
                }
                else
                {
                    Assert.Equal(directionValue, YGNodeLayoutGetMargin(root, YGEdge.Start));
                    Assert.Equal(directionValue, YGNodeLayoutGetMargin(root, YGEdge.End));
                    Assert.Equal(directionValue, YGNodeLayoutGetMargin(root, YGEdge.Left));
                    Assert.Equal(directionValue, YGNodeLayoutGetMargin(root, YGEdge.Right));
                }

                YGNodeFreeRecursive(root);
            }
        }
    }
}
