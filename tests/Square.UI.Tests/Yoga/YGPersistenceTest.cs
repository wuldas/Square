// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGPersistenceTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YGConfigAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGPersistenceTest
{
    [Fact]
    public void Cloning_shared_root()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetFlexBasis(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child1, 1);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(75f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));

        var root2 = YGNodeClone(root);
        YGNodeStyleSetWidth(root2, 100);

        Assert.Equal(2u, (uint)YGNodeGetChildCount(root2));
        // The children should have referential equality at this point.
        Assert.Same(root_child0, YGNodeGetChild(root2, 0));
        Assert.Same(root_child1, YGNodeGetChild(root2, 1));

        YGNodeCalculateLayout(root2, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(2u, (uint)YGNodeGetChildCount(root2));
        // Relayout with no changed input should result in referential equality.
        Assert.Same(root_child0, YGNodeGetChild(root2, 0));
        Assert.Same(root_child1, YGNodeGetChild(root2, 1));

        YGNodeStyleSetWidth(root2, 150);
        YGNodeStyleSetHeight(root2, 200);
        YGNodeCalculateLayout(root2, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(2u, (uint)YGNodeGetChildCount(root2));
        // Relayout with changed input should result in cloned children.
        var root2_child0 = YGNodeGetChild(root2, 0);
        var root2_child1 = YGNodeGetChild(root2, 1);
        Assert.NotSame(root_child0, root2_child0);
        Assert.NotSame(root_child1, root2_child1);

        // Everything in the root should remain unchanged.
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(75f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(75f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1));

        // The new root now has new layout.
        Assert.Equal(0f, YGNodeLayoutGetLeft(root2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root2));
        Assert.Equal(150f, YGNodeLayoutGetWidth(root2));
        Assert.Equal(200f, YGNodeLayoutGetHeight(root2));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root2_child0!));
        Assert.Equal(0f, YGNodeLayoutGetTop(root2_child0!));
        Assert.Equal(150f, YGNodeLayoutGetWidth(root2_child0!));
        Assert.Equal(125f, YGNodeLayoutGetHeight(root2_child0!));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root2_child1!));
        Assert.Equal(125f, YGNodeLayoutGetTop(root2_child1!));
        Assert.Equal(150f, YGNodeLayoutGetWidth(root2_child1!));
        Assert.Equal(75f, YGNodeLayoutGetHeight(root2_child1!));

        YGNodeFreeRecursive(root2);
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void Mutating_children_of_a_clone_clones_only_after_layout()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        Assert.Equal(0u, (uint)YGNodeGetChildCount(root));

        var root2 = YGNodeClone(root);
        Assert.Equal(0u, (uint)YGNodeGetChildCount(root2));

        var root2_child0 = YGNodeNewWithConfig(config);
        YGNodeInsertChild(root2, root2_child0, 0);

        Assert.Equal(0u, (uint)YGNodeGetChildCount(root));
        Assert.Equal(1u, (uint)YGNodeGetChildCount(root2));

        var root3 = YGNodeClone(root2);
        Assert.Equal(1u, (uint)YGNodeGetChildCount(root2));
        Assert.Equal(1u, (uint)YGNodeGetChildCount(root3));
        Assert.Same(YGNodeGetChild(root2, 0), YGNodeGetChild(root3, 0));

        var root3_child1 = YGNodeNewWithConfig(config);
        YGNodeInsertChild(root3, root3_child1, 1);
        Assert.Equal(1u, (uint)YGNodeGetChildCount(root2));
        Assert.Equal(2u, (uint)YGNodeGetChildCount(root3));
        Assert.Same(root3_child1, YGNodeGetChild(root3, 1));
        Assert.Same(YGNodeGetChild(root2, 0), YGNodeGetChild(root3, 0));

        var root4 = YGNodeClone(root3);
        Assert.Same(root3_child1, YGNodeGetChild(root4, 1));

        YGNodeRemoveChild(root4, root3_child1);
        Assert.Equal(2u, (uint)YGNodeGetChildCount(root3));
        Assert.Equal(1u, (uint)YGNodeGetChildCount(root4));
        Assert.Same(YGNodeGetChild(root3, 0), YGNodeGetChild(root4, 0));

        YGNodeCalculateLayout(root4, float.NaN, float.NaN, YGDirection.LTR);
        Assert.NotSame(YGNodeGetChild(root3, 0), YGNodeGetChild(root4, 0));
        YGNodeCalculateLayout(root3, float.NaN, float.NaN, YGDirection.LTR);
        Assert.NotSame(YGNodeGetChild(root2, 0), YGNodeGetChild(root3, 0));

        YGNodeFreeRecursive(root4);
        YGNodeFreeRecursive(root3);
        YGNodeFreeRecursive(root2);
        YGNodeFreeRecursive(root);

        YGConfigFree(config);
    }

    [Fact]
    public void Cloning_two_levels()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 1);
        YGNodeStyleSetFlexBasis(root_child0, 15);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child1, 1);
        YGNodeInsertChild(root, root_child1, 1);

        var root_child1_0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexBasis(root_child1_0, 10);
        YGNodeStyleSetFlexGrow(root_child1_0, 1);
        YGNodeInsertChild(root_child1, root_child1_0, 0);

        var root_child1_1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexBasis(root_child1_1, 25);
        YGNodeInsertChild(root_child1, root_child1_1, 1);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(35f, YGNodeLayoutGetHeight(root_child1_0));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1_1));

        var root2_child0 = YGNodeClone(root_child0);
        var root2_child1 = YGNodeClone(root_child1);
        var root2 = YGNodeClone(root);

        YGNodeStyleSetFlexGrow(root2_child0, 0);
        YGNodeStyleSetFlexBasis(root2_child0, 40);

        YGNodeRemoveAllChildren(root2);
        YGNodeInsertChild(root2, root2_child0, 0);
        YGNodeInsertChild(root2, root2_child1, 1);
        Assert.Equal(2u, (uint)YGNodeGetChildCount(root2));

        YGNodeCalculateLayout(root2, float.NaN, float.NaN, YGDirection.LTR);

        // Original root is unchanged
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(60f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(35f, YGNodeLayoutGetHeight(root_child1_0));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child1_1));

        // New root has new layout at the top
        Assert.Equal(40f, YGNodeLayoutGetHeight(root2_child0));
        Assert.Equal(60f, YGNodeLayoutGetHeight(root2_child1));

        // The deeper children are untouched.
        Assert.Same(YGNodeGetChild(root2_child1, 0), root_child1_0);
        Assert.Same(YGNodeGetChild(root2_child1, 1), root_child1_1);

        YGNodeFreeRecursive(root2);
        YGNodeFreeRecursive(root);

        YGConfigFree(config);
    }
}
