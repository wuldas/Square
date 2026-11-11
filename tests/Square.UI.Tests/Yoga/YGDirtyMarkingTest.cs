// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGDirtyMarkingTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YGConfigAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGDirtyMarkingTest
{
    [Fact]
    public void Dirty_propagation()
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

        YGNodeStyleSetWidth(root_child0, 20);

        Assert.True(YGNodeIsDirty(root_child0));
        Assert.False(YGNodeIsDirty(root_child1));
        Assert.True(YGNodeIsDirty(root));

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.False(YGNodeIsDirty(root_child0));
        Assert.False(YGNodeIsDirty(root_child1));
        Assert.False(YGNodeIsDirty(root));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Dirty_propagation_only_if_prop_changed()
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

        YGNodeStyleSetWidth(root_child0, 50);

        Assert.False(YGNodeIsDirty(root_child0));
        Assert.False(YGNodeIsDirty(root_child1));
        Assert.False(YGNodeIsDirty(root));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Dirty_propagation_changing_layout_config()
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

        var root_child0_child0 = YGNodeNew();
        YGNodeStyleSetWidth(root_child0_child0, 25);
        YGNodeStyleSetHeight(root_child0_child0, 20);
        YGNodeInsertChild(root, root_child0_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.False(YGNodeIsDirty(root));
        Assert.False(YGNodeIsDirty(root_child0));
        Assert.False(YGNodeIsDirty(root_child1));
        Assert.False(YGNodeIsDirty(root_child0_child0));

        var newConfig = YGConfigNew();
        YGConfigSetErrata(newConfig, YGErrata.StretchFlexBasis);
        YGNodeSetConfig(root_child0, newConfig);

        Assert.True(YGNodeIsDirty(root));
        Assert.True(YGNodeIsDirty(root_child0));
        Assert.False(YGNodeIsDirty(root_child1));
        Assert.False(YGNodeIsDirty(root_child0_child0));

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.False(YGNodeIsDirty(root));
        Assert.False(YGNodeIsDirty(root_child0));
        Assert.False(YGNodeIsDirty(root_child1));
        Assert.False(YGNodeIsDirty(root_child0_child0));

        YGConfigFree(newConfig);
        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Dirty_propagation_changing_benign_config()
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

        var root_child0_child0 = YGNodeNew();
        YGNodeStyleSetWidth(root_child0_child0, 25);
        YGNodeStyleSetHeight(root_child0_child0, 20);
        YGNodeInsertChild(root, root_child0_child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.False(YGNodeIsDirty(root));
        Assert.False(YGNodeIsDirty(root_child0));
        Assert.False(YGNodeIsDirty(root_child1));
        Assert.False(YGNodeIsDirty(root_child0_child0));

        var newConfig = YGConfigNew();
        YGConfigSetLogger(newConfig, (config, node, level, message) => { });
        YGNodeSetConfig(root_child0, newConfig);

        Assert.False(YGNodeIsDirty(root));
        Assert.False(YGNodeIsDirty(root_child0));
        Assert.False(YGNodeIsDirty(root_child1));
        Assert.False(YGNodeIsDirty(root_child0_child0));

        YGConfigFree(newConfig);
        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Dirty_mark_all_children_as_dirty_when_display_changes()
    {
        var root = YGNodeNew();
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetHeight(root, 100);

        var child0 = YGNodeNew();
        YGNodeStyleSetFlexGrow(child0, 1);
        var child1 = YGNodeNew();
        YGNodeStyleSetFlexGrow(child1, 1);

        var child1_child0 = YGNodeNew();
        var child1_child0_child0 = YGNodeNew();
        YGNodeStyleSetWidth(child1_child0_child0, 8);
        YGNodeStyleSetHeight(child1_child0_child0, 16);

        YGNodeInsertChild(child1_child0, child1_child0_child0, 0);

        YGNodeInsertChild(child1, child1_child0, 0);
        YGNodeInsertChild(root, child0, 0);
        YGNodeInsertChild(root, child1, 0);

        YGNodeStyleSetDisplay(child0, YGDisplay.Flex);
        YGNodeStyleSetDisplay(child1, YGDisplay.None);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetWidth(child1_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(child1_child0_child0));

        YGNodeStyleSetDisplay(child0, YGDisplay.None);
        YGNodeStyleSetDisplay(child1, YGDisplay.Flex);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(8f, YGNodeLayoutGetWidth(child1_child0_child0));
        Assert.Equal(16f, YGNodeLayoutGetHeight(child1_child0_child0));

        YGNodeStyleSetDisplay(child0, YGDisplay.Flex);
        YGNodeStyleSetDisplay(child1, YGDisplay.None);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetWidth(child1_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetHeight(child1_child0_child0));

        YGNodeStyleSetDisplay(child0, YGDisplay.None);
        YGNodeStyleSetDisplay(child1, YGDisplay.Flex);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(8f, YGNodeLayoutGetWidth(child1_child0_child0));
        Assert.Equal(16f, YGNodeLayoutGetHeight(child1_child0_child0));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Dirty_node_only_if_children_are_actually_removed()
    {
        var root = YGNodeNew();
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        YGNodeStyleSetWidth(root, 50);
        YGNodeStyleSetHeight(root, 50);

        var child0 = YGNodeNew();
        YGNodeStyleSetWidth(child0, 50);
        YGNodeStyleSetHeight(child0, 25);
        YGNodeInsertChild(root, child0, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        var child1 = YGNodeNew();
        YGNodeRemoveChild(root, child1);
        Assert.False(YGNodeIsDirty(root));
        YGNodeFree(child1);

        YGNodeRemoveChild(root, child0);
        Assert.True(YGNodeIsDirty(root));
        YGNodeFree(child0);

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Dirty_node_only_if_undefined_values_gets_set_to_undefined()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 50);
        YGNodeStyleSetHeight(root, 50);
        YGNodeStyleSetMinWidth(root, float.NaN);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.False(YGNodeIsDirty(root));

        YGNodeStyleSetMinWidth(root, float.NaN);

        Assert.False(YGNodeIsDirty(root));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Dirty_removed_child_node()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var child = YGNodeNew();
        YGNodeStyleSetWidth(child, 50);
        YGNodeStyleSetHeight(child, 50);
        YGNodeInsertChild(root, child, 0);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.False(YGNodeIsDirty(child));

        YGNodeRemoveChild(root, child);

        // Child should be marked dirty after removal so layout is recalculated
        // when the child is reused (e.g., in a recycling view system)
        Assert.True(YGNodeIsDirty(child));

        YGNodeFree(child);
        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Dirty_removed_child_nodes_when_removing_all()
    {
        var root = YGNodeNew();
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetHeight(root, 100);

        var child0 = YGNodeNew();
        YGNodeStyleSetWidth(child0, 50);
        YGNodeStyleSetHeight(child0, 25);
        YGNodeInsertChild(root, child0, 0);

        var child1 = YGNodeNew();
        YGNodeStyleSetWidth(child1, 50);
        YGNodeStyleSetHeight(child1, 25);
        YGNodeInsertChild(root, child1, 1);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.False(YGNodeIsDirty(child0));
        Assert.False(YGNodeIsDirty(child1));

        YGNodeRemoveAllChildren(root);

        // All children should be marked dirty after removal
        Assert.True(YGNodeIsDirty(child0));
        Assert.True(YGNodeIsDirty(child1));

        YGNodeFree(child0);
        YGNodeFree(child1);
        YGNodeFreeRecursive(root);
    }
}
