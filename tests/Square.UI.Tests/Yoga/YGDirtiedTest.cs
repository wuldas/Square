// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGDirtiedTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGDirtiedTest
{
    [Fact]
    public void Dirtied()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        int dirtiedCount = 0;
        YGNodeSetContext(root, null);
        YGNodeSetDirtiedFunc(root, (node) => { dirtiedCount++; });

        Assert.Equal(0, dirtiedCount);

        // _dirtied MUST be called in case of explicit dirtying.
        root.SetDirty(true);
        Assert.Equal(1, dirtiedCount);

        // _dirtied MUST be called ONCE (already dirty).
        root.SetDirty(true);
        Assert.Equal(1, dirtiedCount);
    }

    [Fact]
    public void Dirtied_propagation()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 20);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNew();
        YGNodeStyleSetWidth(root_child1, 50);
        YGNodeStyleSetHeight(root_child1, 20);
        YGNodeInsertChild(root, root_child1, 1);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        int dirtiedCount = 0;
        YGNodeSetDirtiedFunc(root, (node) => { dirtiedCount++; });

        Assert.Equal(0, dirtiedCount);

        // _dirtied MUST be called for the first time.
        root_child0.MarkDirtyAndPropagate();
        Assert.Equal(1, dirtiedCount);

        // _dirtied must NOT be called for the second time (already dirty).
        root_child0.MarkDirtyAndPropagate();
        Assert.Equal(1, dirtiedCount);
    }

    [Fact]
    public void Dirtied_hierarchy()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 20);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNew();
        YGNodeStyleSetWidth(root_child1, 50);
        YGNodeStyleSetHeight(root_child1, 20);
        YGNodeInsertChild(root, root_child1, 1);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        int dirtiedCount = 0;
        YGNodeSetDirtiedFunc(root_child0, (node) => { dirtiedCount++; });

        Assert.Equal(0, dirtiedCount);

        // _dirtied must NOT be called for descendants.
        root.MarkDirtyAndPropagate();
        Assert.Equal(0, dirtiedCount);

        // _dirtied must NOT be called for the sibling node.
        root_child1.MarkDirtyAndPropagate();
        Assert.Equal(0, dirtiedCount);

        // _dirtied MUST be called in case of explicit dirtying.
        root_child0.MarkDirtyAndPropagate();
        Assert.Equal(1, dirtiedCount);
    }
}
