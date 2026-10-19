// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGCloneNodeTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGCloneNodeTest
{
    private static void RecursivelyAssertProperNodeOwnership(Node node)
    {
        for (nuint i = 0; i < YGNodeGetChildCount(node); ++i)
        {
            var child = YGNodeGetChild(node, i);
            Assert.NotNull(child);
            Assert.Equal(node, YGNodeGetOwner(child!));
            RecursivelyAssertProperNodeOwnership(child!);
        }
    }

    [Fact]
    public void Absolute_node_cloned_with_static_parent()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Static);
        YGNodeStyleSetWidth(root_child0, 10);
        YGNodeStyleSetHeight(root_child0, 10);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child0_child0 = YGNodeNew();
        YGNodeStyleSetPositionType(root_child0_child0, YGPositionType.Absolute);
        YGNodeStyleSetWidthPercent(root_child0_child0, 1);
        YGNodeStyleSetHeight(root_child0_child0, 1);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        var clonedRoot = YGNodeClone(root);
        YGNodeStyleSetWidth(clonedRoot, 110);
        YGNodeCalculateLayout(clonedRoot, float.NaN, float.NaN, YGDirection.LTR);

        RecursivelyAssertProperNodeOwnership(clonedRoot);

        YGNodeFreeRecursive(root);
        YGNodeFreeRecursive(clonedRoot);
    }

    [Fact]
    public void Absolute_node_cloned_with_static_ancestors()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNew();
        YGNodeStyleSetPositionType(root_child0, YGPositionType.Static);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child0_child0 = YGNodeNew();
        YGNodeStyleSetPositionType(root_child0_child0, YGPositionType.Static);
        YGNodeStyleSetWidth(root_child0_child0, 40);
        YGNodeStyleSetHeight(root_child0_child0, 40);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);

        var root_child0_child0_child0 = YGNodeNew();
        YGNodeStyleSetPositionType(root_child0_child0_child0, YGPositionType.Static);
        YGNodeStyleSetWidth(root_child0_child0_child0, 30);
        YGNodeStyleSetHeight(root_child0_child0_child0, 30);
        YGNodeInsertChild(root_child0_child0, root_child0_child0_child0, 0);

        var root_child0_child0_child0_child0 = YGNodeNew();
        YGNodeStyleSetPositionType(root_child0_child0_child0_child0, YGPositionType.Absolute);
        YGNodeStyleSetWidthPercent(root_child0_child0_child0_child0, 1);
        YGNodeStyleSetHeight(root_child0_child0_child0_child0, 1);
        YGNodeInsertChild(root_child0_child0_child0, root_child0_child0_child0_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        var clonedRoot = YGNodeClone(root);
        YGNodeStyleSetWidth(clonedRoot, 110);
        YGNodeCalculateLayout(clonedRoot, float.NaN, float.NaN, YGDirection.LTR);

        RecursivelyAssertProperNodeOwnership(clonedRoot);

        YGNodeFreeRecursive(root);
        YGNodeFreeRecursive(clonedRoot);
    }
}
