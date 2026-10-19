// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGHadOverflowTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YGConfigAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGHadOverflowTest : IDisposable
{
    private readonly Config _config;
    private readonly Node _root;

    public YGHadOverflowTest()
    {
        _config = YGConfigNew();
        _root = YGNodeNewWithConfig(_config);
        YGNodeStyleSetWidth(_root, 200);
        YGNodeStyleSetHeight(_root, 100);
        YGNodeStyleSetFlexDirection(_root, YGFlexDirection.Column);
        YGNodeStyleSetFlexWrap(_root, YGWrap.NoWrap);
    }

    void IDisposable.Dispose()
    {
        YGNodeFreeRecursive(_root);
    }

    [Fact]
    public void Children_overflow_no_wrap_and_no_flex_children()
    {
        var child0 = YGNodeNewWithConfig(_config);
        YGNodeStyleSetWidth(child0, 80);
        YGNodeStyleSetHeight(child0, 40);
        YGNodeStyleSetMargin(child0, YGEdge.Top, 10);
        YGNodeStyleSetMargin(child0, YGEdge.Bottom, 15);
        YGNodeInsertChild(_root, child0, 0);

        var child1 = YGNodeNewWithConfig(_config);
        YGNodeStyleSetWidth(child1, 80);
        YGNodeStyleSetHeight(child1, 40);
        YGNodeStyleSetMargin(child1, YGEdge.Bottom, 5);
        YGNodeInsertChild(_root, child1, 1);

        YGNodeCalculateLayout(_root, 200, 100, YGDirection.LTR);

        Assert.True(YGNodeLayoutGetHadOverflow(_root));
    }

    [Fact]
    public void Spacing_overflow_no_wrap_and_no_flex_children()
    {
        var child0 = YGNodeNewWithConfig(_config);
        YGNodeStyleSetWidth(child0, 80);
        YGNodeStyleSetHeight(child0, 40);
        YGNodeStyleSetMargin(child0, YGEdge.Top, 10);
        YGNodeStyleSetMargin(child0, YGEdge.Bottom, 10);
        YGNodeInsertChild(_root, child0, 0);

        var child1 = YGNodeNewWithConfig(_config);
        YGNodeStyleSetWidth(child1, 80);
        YGNodeStyleSetHeight(child1, 40);
        YGNodeStyleSetMargin(child1, YGEdge.Bottom, 5);
        YGNodeInsertChild(_root, child1, 1);

        YGNodeCalculateLayout(_root, 200, 100, YGDirection.LTR);

        Assert.True(YGNodeLayoutGetHadOverflow(_root));
    }

    [Fact]
    public void No_overflow_no_wrap_and_flex_children()
    {
        var child0 = YGNodeNewWithConfig(_config);
        YGNodeStyleSetWidth(child0, 80);
        YGNodeStyleSetHeight(child0, 40);
        YGNodeStyleSetMargin(child0, YGEdge.Top, 10);
        YGNodeStyleSetMargin(child0, YGEdge.Bottom, 10);
        YGNodeInsertChild(_root, child0, 0);

        var child1 = YGNodeNewWithConfig(_config);
        YGNodeStyleSetWidth(child1, 80);
        YGNodeStyleSetHeight(child1, 40);
        YGNodeStyleSetMargin(child1, YGEdge.Bottom, 5);
        YGNodeStyleSetFlexShrink(child1, 1);
        YGNodeInsertChild(_root, child1, 1);

        YGNodeCalculateLayout(_root, 200, 100, YGDirection.LTR);

        Assert.False(YGNodeLayoutGetHadOverflow(_root));
    }

    [Fact]
    public void HadOverflow_gets_reset_if_not_longer_valid()
    {
        var child0 = YGNodeNewWithConfig(_config);
        YGNodeStyleSetWidth(child0, 80);
        YGNodeStyleSetHeight(child0, 40);
        YGNodeStyleSetMargin(child0, YGEdge.Top, 10);
        YGNodeStyleSetMargin(child0, YGEdge.Bottom, 10);
        YGNodeInsertChild(_root, child0, 0);

        var child1 = YGNodeNewWithConfig(_config);
        YGNodeStyleSetWidth(child1, 80);
        YGNodeStyleSetHeight(child1, 40);
        YGNodeStyleSetMargin(child1, YGEdge.Bottom, 5);
        YGNodeInsertChild(_root, child1, 1);

        YGNodeCalculateLayout(_root, 200, 100, YGDirection.LTR);

        Assert.True(YGNodeLayoutGetHadOverflow(_root));

        YGNodeStyleSetFlexShrink(child1, 1);

        YGNodeCalculateLayout(_root, 200, 100, YGDirection.LTR);

        Assert.False(YGNodeLayoutGetHadOverflow(_root));
    }

    [Fact]
    public void Spacing_overflow_in_nested_nodes()
    {
        var child0 = YGNodeNewWithConfig(_config);
        YGNodeStyleSetWidth(child0, 80);
        YGNodeStyleSetHeight(child0, 40);
        YGNodeStyleSetMargin(child0, YGEdge.Top, 10);
        YGNodeStyleSetMargin(child0, YGEdge.Bottom, 10);
        YGNodeInsertChild(_root, child0, 0);

        var child1 = YGNodeNewWithConfig(_config);
        YGNodeStyleSetWidth(child1, 80);
        YGNodeStyleSetHeight(child1, 40);
        YGNodeInsertChild(_root, child1, 1);

        var child1_1 = YGNodeNewWithConfig(_config);
        YGNodeStyleSetWidth(child1_1, 80);
        YGNodeStyleSetHeight(child1_1, 40);
        YGNodeStyleSetMargin(child1_1, YGEdge.Bottom, 5);
        YGNodeInsertChild(child1, child1_1, 0);

        YGNodeCalculateLayout(_root, 200, 100, YGDirection.LTR);

        Assert.True(YGNodeLayoutGetHadOverflow(_root));
    }
}
