// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGLayoutableChildrenTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGLayoutableChildrenTest
{
    [Fact]
    public void Layoutable_children_single_contents_node()
    {
        var root = YGNodeNew();
        var root_child0 = YGNodeNew();
        var root_child1 = YGNodeNew();
        var root_child2 = YGNodeNew();
        var root_grandchild0 = YGNodeNew();
        var root_grandchild1 = YGNodeNew();

        YGNodeInsertChild(root, root_child0, 0);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeInsertChild(root, root_child2, 2);

        YGNodeInsertChild(root_child1, root_grandchild0, 0);
        YGNodeInsertChild(root_child1, root_grandchild1, 1);

        YGNodeStyleSetDisplay(root_child1, YGDisplay.Contents);

        var expected = new[] { root_child0, root_grandchild0, root_grandchild1, root_child2 };
        var actual = root.GetLayoutChildren().ToList();

        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Same(expected[i], actual[i]);
        }

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Layoutable_children_multiple_contents_nodes()
    {
        var root = YGNodeNew();
        var root_child0 = YGNodeNew();
        var root_child1 = YGNodeNew();
        var root_child2 = YGNodeNew();
        var root_grandchild0 = YGNodeNew();
        var root_grandchild1 = YGNodeNew();
        var root_grandchild2 = YGNodeNew();
        var root_grandchild3 = YGNodeNew();
        var root_grandchild4 = YGNodeNew();
        var root_grandchild5 = YGNodeNew();

        YGNodeInsertChild(root, root_child0, 0);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeInsertChild(root, root_child2, 2);

        YGNodeInsertChild(root_child0, root_grandchild0, 0);
        YGNodeInsertChild(root_child0, root_grandchild1, 1);
        YGNodeInsertChild(root_child1, root_grandchild2, 0);
        YGNodeInsertChild(root_child1, root_grandchild3, 1);
        YGNodeInsertChild(root_child2, root_grandchild4, 0);
        YGNodeInsertChild(root_child2, root_grandchild5, 1);

        YGNodeStyleSetDisplay(root_child0, YGDisplay.Contents);
        YGNodeStyleSetDisplay(root_child1, YGDisplay.Contents);
        YGNodeStyleSetDisplay(root_child2, YGDisplay.Contents);

        var expected = new[]
        {
            root_grandchild0, root_grandchild1,
            root_grandchild2, root_grandchild3,
            root_grandchild4, root_grandchild5
        };
        var actual = root.GetLayoutChildren().ToList();

        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Same(expected[i], actual[i]);
        }

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Layoutable_children_nested_contents_nodes()
    {
        var root = YGNodeNew();
        var root_child0 = YGNodeNew();
        var root_child1 = YGNodeNew();
        var root_child2 = YGNodeNew();
        var root_grandchild0 = YGNodeNew();
        var root_grandchild1 = YGNodeNew();
        var root_great_grandchild0 = YGNodeNew();
        var root_great_grandchild1 = YGNodeNew();

        YGNodeInsertChild(root, root_child0, 0);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeInsertChild(root, root_child2, 2);

        YGNodeInsertChild(root_child1, root_grandchild0, 0);
        YGNodeInsertChild(root_child1, root_grandchild1, 1);

        YGNodeInsertChild(root_grandchild1, root_great_grandchild0, 0);
        YGNodeInsertChild(root_grandchild1, root_great_grandchild1, 1);

        YGNodeStyleSetDisplay(root_child1, YGDisplay.Contents);
        YGNodeStyleSetDisplay(root_grandchild1, YGDisplay.Contents);

        var expected = new[] { root_child0, root_grandchild0, root_great_grandchild0, root_great_grandchild1, root_child2 };
        var actual = root.GetLayoutChildren().ToList();

        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Same(expected[i], actual[i]);
        }

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Layoutable_children_contents_leaf_node()
    {
        var root = YGNodeNew();
        var root_child0 = YGNodeNew();
        var root_child1 = YGNodeNew();
        var root_child2 = YGNodeNew();

        YGNodeInsertChild(root, root_child0, 0);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeInsertChild(root, root_child2, 2);

        YGNodeStyleSetDisplay(root_child1, YGDisplay.Contents);

        var expected = new[] { root_child0, root_child2 };
        var actual = root.GetLayoutChildren().ToList();

        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Same(expected[i], actual[i]);
        }

        YGNodeFreeRecursive(root);
    }

    [Fact]
    public void Layoutable_children_contents_root_node()
    {
        var root = YGNodeNew();
        var root_child0 = YGNodeNew();
        var root_child1 = YGNodeNew();
        var root_child2 = YGNodeNew();

        YGNodeInsertChild(root, root_child0, 0);
        YGNodeInsertChild(root, root_child1, 1);
        YGNodeInsertChild(root, root_child2, 2);

        YGNodeStyleSetDisplay(root, YGDisplay.Contents);

        var expected = new[] { root_child0, root_child1, root_child2 };
        var actual = root.GetLayoutChildren().ToList();

        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Same(expected[i], actual[i]);
        }

        YGNodeFreeRecursive(root);
    }
}
