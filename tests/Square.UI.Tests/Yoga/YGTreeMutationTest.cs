// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGTreeMutationTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGTreeMutationTest
{
    private static Node[] GetChildren(Node node)
    {
        var count = YGNodeGetChildCount(node);
        var children = new Node[count];
        for (nuint i = 0; i < count; i++)
        {
            children[i] = YGNodeGetChild(node, i)!;
        }
        return children;
    }

    [Fact]
    public void Set_children_adds_children_to_parent()
    {
        var root = YGNodeNew();
        var root_child0 = YGNodeNew();
        var root_child1 = YGNodeNew();

        YGNodeSetChildren(root, new[] { root_child0, root_child1 });

        var children = GetChildren(root);
        Assert.Equal(new[] { root_child0, root_child1 }, children);
        Assert.Equal(root, YGNodeGetOwner(root_child0));
        Assert.Equal(root, YGNodeGetOwner(root_child1));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Set_children_to_empty_removes_old_children()
    {
        var root = YGNodeNew();
        var root_child0 = YGNodeNew();
        var root_child1 = YGNodeNew();

        YGNodeSetChildren(root, new[] { root_child0, root_child1 });
        YGNodeSetChildren(root, Array.Empty<Node>());

        var children = GetChildren(root);
        Assert.Empty(children);
        Assert.Null(YGNodeGetOwner(root_child0));
        Assert.Null(YGNodeGetOwner(root_child1));

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Set_children_replaces_non_common_children()
    {
        var root = YGNodeNew();
        var root_child0 = YGNodeNew();
        var root_child1 = YGNodeNew();

        YGNodeSetChildren(root, new[] { root_child0, root_child1 });

        var root_child2 = YGNodeNew();
        var root_child3 = YGNodeNew();

        YGNodeSetChildren(root, new[] { root_child2, root_child3 });

        var children = GetChildren(root);
        Assert.Equal(new[] { root_child2, root_child3 }, children);
        Assert.Null(YGNodeGetOwner(root_child0));
        Assert.Null(YGNodeGetOwner(root_child1));

        YGNodeFreeRecursive(root);
        YGNodeFree(root_child0);
        YGNodeFree(root_child1);
    }

    [Fact]
    public void Set_children_keeps_and_reorders_common_children()
    {
        var root = YGNodeNew();
        var root_child0 = YGNodeNew();
        var root_child1 = YGNodeNew();
        var root_child2 = YGNodeNew();

        YGNodeSetChildren(root, new[] { root_child0, root_child1, root_child2 });

        var root_child3 = YGNodeNew();

        YGNodeSetChildren(root, new[] { root_child2, root_child1, root_child3 });

        var children = GetChildren(root);
        Assert.Equal(new[] { root_child2, root_child1, root_child3 }, children);

        Assert.Null(YGNodeGetOwner(root_child0));
        Assert.Equal(root, YGNodeGetOwner(root_child1));
        Assert.Equal(root, YGNodeGetOwner(root_child2));
        Assert.Equal(root, YGNodeGetOwner(root_child3));

        YGNodeFreeRecursive(root);
        YGNodeFree(root_child0);
    }
}
