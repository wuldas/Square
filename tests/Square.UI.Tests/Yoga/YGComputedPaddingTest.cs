// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGComputedPaddingTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGComputedPaddingTest
{
    [Fact]
    public void Computed_layout_padding()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetPaddingPercent(root, YGEdge.Start, 10);

        YGNodeCalculateLayout(root, 100, 100, YGDirection.LTR);

        Assert.Equal(10f, YGNodeLayoutGetPadding(root, YGEdge.Left));
        Assert.Equal(0f, YGNodeLayoutGetPadding(root, YGEdge.Right));

        YGNodeCalculateLayout(root, 100, 100, YGDirection.RTL);

        Assert.Equal(0f, YGNodeLayoutGetPadding(root, YGEdge.Left));
        Assert.Equal(10f, YGNodeLayoutGetPadding(root, YGEdge.Right));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Padding_side_overrides_horizontal_and_vertical()
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
                YGNodeStyleSetPadding(root, horizontalOrVertical, 10);
                YGNodeStyleSetPadding(root, edge, edgeValue);

                YGNodeCalculateLayout(root, 100, 100, YGDirection.LTR);

                Assert.Equal(edgeValue, YGNodeLayoutGetPadding(root, edge));

                YGNodeFreeRecursive(root);
            }
        }
    }

    [Fact]
    public void Padding_side_overrides_all()
    {
        YGEdge[] edges = { YGEdge.Top, YGEdge.Bottom, YGEdge.Start, YGEdge.End, YGEdge.Left, YGEdge.Right };

        for (float edgeValue = 0; edgeValue < 2; ++edgeValue)
        {
            foreach (var edge in edges)
            {
                var root = YGNodeNew();
                YGNodeStyleSetWidth(root, 100);
                YGNodeStyleSetHeight(root, 100);
                YGNodeStyleSetPadding(root, YGEdge.All, 10);
                YGNodeStyleSetPadding(root, edge, edgeValue);

                YGNodeCalculateLayout(root, 100, 100, YGDirection.LTR);

                Assert.Equal(edgeValue, YGNodeLayoutGetPadding(root, edge));

                YGNodeFreeRecursive(root);
            }
        }
    }

    [Fact]
    public void Padding_horizontal_and_vertical_overrides_all()
    {
        YGEdge[] directions = { YGEdge.Horizontal, YGEdge.Vertical };

        for (float directionValue = 0; directionValue < 2; ++directionValue)
        {
            foreach (var direction in directions)
            {
                var root = YGNodeNew();
                YGNodeStyleSetWidth(root, 100);
                YGNodeStyleSetHeight(root, 100);
                YGNodeStyleSetPadding(root, YGEdge.All, 10);
                YGNodeStyleSetPadding(root, direction, directionValue);

                YGNodeCalculateLayout(root, 100, 100, YGDirection.LTR);

                if (direction == YGEdge.Vertical)
                {
                    Assert.Equal(directionValue, YGNodeLayoutGetPadding(root, YGEdge.Top));
                    Assert.Equal(directionValue, YGNodeLayoutGetPadding(root, YGEdge.Bottom));
                }
                else
                {
                    Assert.Equal(directionValue, YGNodeLayoutGetPadding(root, YGEdge.Start));
                    Assert.Equal(directionValue, YGNodeLayoutGetPadding(root, YGEdge.End));
                    Assert.Equal(directionValue, YGNodeLayoutGetPadding(root, YGEdge.Left));
                    Assert.Equal(directionValue, YGNodeLayoutGetPadding(root, YGEdge.Right));
                }

                YGNodeFreeRecursive(root);
            }
        }
    }
}
