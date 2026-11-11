// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGZeroOutLayoutRecursivelyTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGZeroOutLayoutRecursivelyTest
{
    [Fact]
    public void Zero_out_layout()
    {
        var root = YGNodeNew();
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);

        var child = YGNodeNew();
        YGNodeInsertChild(root, child, 0);
        YGNodeStyleSetWidth(child, 100);
        YGNodeStyleSetHeight(child, 100);
        YGNodeStyleSetMargin(child, YGEdge.Top, 10);
        YGNodeStyleSetPadding(child, YGEdge.Top, 10);

        YGNodeCalculateLayout(root, 100, 100, YGDirection.LTR);

        Assert.Equal(10f, YGNodeLayoutGetMargin(child, YGEdge.Top));
        Assert.Equal(10f, YGNodeLayoutGetPadding(child, YGEdge.Top));

        YGNodeStyleSetDisplay(child, YGDisplay.None);

        YGNodeCalculateLayout(root, 100, 100, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetMargin(child, YGEdge.Top));
        Assert.Equal(0f, YGNodeLayoutGetPadding(child, YGEdge.Top));

        YGNodeFreeRecursive(root);
    }
}
