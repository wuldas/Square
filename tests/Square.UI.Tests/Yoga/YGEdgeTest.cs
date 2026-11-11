// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGEdgeTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGEdgeTest
{
    [Fact]
    public void Start_overrides()
    {
        var root = YGNodeNew();
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetMargin(root_child0, YGEdge.Start, 10);
        YGNodeStyleSetMargin(root_child0, YGEdge.Left, 20);
        YGNodeStyleSetMargin(root_child0, YGEdge.Right, 20);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetRight(root_child0));

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(20f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetRight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void End_overrides()
    {
        var root = YGNodeNew();
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetMargin(root_child0, YGEdge.End, 10);
        YGNodeStyleSetMargin(root_child0, YGEdge.Left, 20);
        YGNodeStyleSetMargin(root_child0, YGEdge.Right, 20);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(20f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetRight(root_child0));

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetRight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Horizontal_overridden()
    {
        var root = YGNodeNew();
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetMargin(root_child0, YGEdge.Horizontal, 10);
        YGNodeStyleSetMargin(root_child0, YGEdge.Left, 20);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(20f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetRight(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Vertical_overridden()
    {
        var root = YGNodeNew();
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Column);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetMargin(root_child0, YGEdge.Vertical, 10);
        YGNodeStyleSetMargin(root_child0, YGEdge.Top, 20);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(20f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetBottom(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Horizontal_overrides_all()
    {
        var root = YGNodeNew();
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Column);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetMargin(root_child0, YGEdge.Horizontal, 10);
        YGNodeStyleSetMargin(root_child0, YGEdge.All, 20);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetRight(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetBottom(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Vertical_overrides_all()
    {
        var root = YGNodeNew();
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Column);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetMargin(root_child0, YGEdge.Vertical, 10);
        YGNodeStyleSetMargin(root_child0, YGEdge.All, 20);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(20f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetRight(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetBottom(root_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void All_overridden()
    {
        var root = YGNodeNew();
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Column);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetMargin(root_child0, YGEdge.Left, 10);
        YGNodeStyleSetMargin(root_child0, YGEdge.Top, 10);
        YGNodeStyleSetMargin(root_child0, YGEdge.Right, 10);
        YGNodeStyleSetMargin(root_child0, YGEdge.Bottom, 10);
        YGNodeStyleSetMargin(root_child0, YGEdge.All, 20);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetRight(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetBottom(root_child0));

        YGNodeFreeRecursive(root);
    }
}
