// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGNodeChildTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YogaValue;

namespace Square.Rendering.Yoga.Tests;

public class YGNodeChildTest
{
    [Fact]
    public void Reset_layout_when_child_removed()
    {
        var root = YGNodeNew();

        var root_child0 = YGNodeNew();
        YGNodeStyleSetWidth(root_child0, 100);
        YGNodeStyleSetHeight(root_child0, 100);
        YGNodeInsertChild(root, root_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));

        YGNodeRemoveChild(root, root_child0);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.True(YGFloatIsUndefined(YGNodeLayoutGetWidth(root_child0)));
        Assert.True(YGFloatIsUndefined(YGNodeLayoutGetHeight(root_child0)));

        YGNodeFreeRecursive(root);
        YGNodeFreeRecursive(root_child0);
    }

    [Fact]
    public void Removed_child_can_be_reused_with_valid_layout()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetHeight(root, 200);

        var child = YGNodeNew();
        YGNodeStyleSetWidth(child, 100);
        YGNodeStyleSetHeight(child, 100);
        YGNodeInsertChild(root, child, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(100f, YGNodeLayoutGetWidth(child));
        Assert.Equal(100f, YGNodeLayoutGetHeight(child));

        // Remove child - layout should be cleared and child marked dirty
        YGNodeRemoveChild(root, child);

        Assert.True(YGFloatIsUndefined(YGNodeLayoutGetWidth(child)));
        Assert.True(YGFloatIsUndefined(YGNodeLayoutGetHeight(child)));
        Assert.True(YGNodeIsDirty(child));

        // Reinsert the child and recalculate - layout should be valid again
        YGNodeInsertChild(root, child, 0);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(100f, YGNodeLayoutGetWidth(child));
        Assert.Equal(100f, YGNodeLayoutGetHeight(child));
        Assert.False(YGNodeIsDirty(child));

        YGNodeFreeRecursive(root);
    }
}
