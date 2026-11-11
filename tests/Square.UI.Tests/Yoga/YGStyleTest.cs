// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGStyleTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YogaValue;

namespace Square.Rendering.Yoga.Tests;

public class YGStyleTest
{
    [Fact]
    public void Copy_style_same()
    {
        var node0 = YGNodeNew();
        var node1 = YGNodeNew();

        YGNodeCopyStyle(node0, node1);

        YGNodeFree(node0);
        YGNodeFree(node1);
    }

    [Fact]
    public void Copy_style_modified()
    {
        var node0 = YGNodeNew();
        Assert.Equal(YGFlexDirection.Column, YGNodeStyleGetFlexDirection(node0));
        Assert.False(YGNodeStyleGetMaxHeight(node0).Unit != Unit.Undefined);

        var node1 = YGNodeNew();
        YGNodeStyleSetFlexDirection(node1, YGFlexDirection.Row);
        YGNodeStyleSetMaxHeight(node1, 10);

        YGNodeCopyStyle(node0, node1);
        Assert.Equal(YGFlexDirection.Row, YGNodeStyleGetFlexDirection(node0));
        Assert.Equal(10f, YGNodeStyleGetMaxHeight(node0).Value);

        YGNodeFree(node0);
        YGNodeFree(node1);
    }

    [Fact]
    public void Copy_style_modified_same()
    {
        var node0 = YGNodeNew();
        YGNodeStyleSetFlexDirection(node0, YGFlexDirection.Row);
        YGNodeStyleSetMaxHeight(node0, 10);
        YGNodeCalculateLayout(node0, float.NaN, float.NaN, YGDirection.LTR);

        var node1 = YGNodeNew();
        YGNodeStyleSetFlexDirection(node1, YGFlexDirection.Row);
        YGNodeStyleSetMaxHeight(node1, 10);

        YGNodeCopyStyle(node0, node1);

        YGNodeFree(node0);
        YGNodeFree(node1);
    }

    [Fact]
    public void Initialise_flexShrink_flexGrow()
    {
        var node0 = YGNodeNew();
        YGNodeStyleSetFlexShrink(node0, 1);
        Assert.Equal(1f, YGNodeStyleGetFlexShrink(node0));

        YGNodeStyleSetFlexShrink(node0, float.NaN);
        YGNodeStyleSetFlexGrow(node0, 3);
        Assert.Equal(0f, YGNodeStyleGetFlexShrink(node0)); // Default value is Zero, if flex shrink is not defined
        Assert.Equal(3f, YGNodeStyleGetFlexGrow(node0));

        YGNodeStyleSetFlexGrow(node0, float.NaN);
        YGNodeStyleSetFlexShrink(node0, 3);
        Assert.Equal(0f, YGNodeStyleGetFlexGrow(node0)); // Default value is Zero, if flex grow is not defined
        Assert.Equal(3f, YGNodeStyleGetFlexShrink(node0));

        YGNodeFree(node0);
    }
}
