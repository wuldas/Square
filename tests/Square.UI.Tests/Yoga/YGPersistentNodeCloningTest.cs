// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGPersistentNodeCloningTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YGConfigAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGPersistentNodeCloningTest
{
    private class NodeWrapper
    {
        public Node Node;
        public List<NodeWrapper> Children;

        public NodeWrapper(Config config, List<NodeWrapper>? children = null)
        {
            Node = YGNodeNewWithConfig(config);
            Children = children ?? new List<NodeWrapper>();
            YGNodeSetContext(Node, this);

            for (int i = 0; i < Children.Count; i++)
            {
                var child = Children[i];
                // Claim first ownership of not yet owned nodes
                if (YGNodeGetOwner(child.Node) == null)
                {
                    child.Node.SetOwner(Node);
                }
                Node.InsertChild(child.Node, Node.GetChildCount());
            }
        }

        // Clone with current children
        public NodeWrapper(NodeWrapper other)
        {
            Node = YGNodeClone(other.Node);
            Children = new List<NodeWrapper>(other.Children);
            YGNodeSetContext(Node, this);
            Node.SetOwner(null);
        }

        // Clone with new children
        public NodeWrapper(NodeWrapper other, List<NodeWrapper> children)
        {
            Node = YGNodeClone(other.Node);
            Children = children;
            YGNodeSetContext(Node, this);
            Node.SetOwner(null);
            Node.SetChildren(new List<Node>());
            Node.MarkDirtyAndPropagate();

            for (int i = 0; i < Children.Count; i++)
            {
                var child = Children[i];
                if (YGNodeGetOwner(child.Node) == null)
                {
                    child.Node.SetOwner(Node);
                }
                Node.InsertChild(child.Node, Node.GetChildCount());
            }
        }
    }

    [Fact]
    public void Changing_sibling_height_does_not_clone_neighbors()
    {
        var config = YGConfigNew();

        var nodesCloned = new List<NodeWrapper>();

        YGConfigSetCloneNodeFunc(config, (Node oldNode, Node owner, int childIndex) =>
        {
            var wrapper = (NodeWrapper)YGNodeGetContext(owner)!;
            var old = (NodeWrapper)YGNodeGetContext(oldNode)!;
            nodesCloned.Add(old);

            wrapper.Children[childIndex] = new NodeWrapper(old);
            return wrapper.Children[childIndex].Node;
        });

        var sibling = new NodeWrapper(config);
        YGNodeStyleSetHeight(sibling.Node, 1);

        var d = new NodeWrapper(config);
        var c = new NodeWrapper(config, new List<NodeWrapper> { d });
        var b = new NodeWrapper(config, new List<NodeWrapper> { c });
        var a = new NodeWrapper(config, new List<NodeWrapper> { b });
        YGNodeStyleSetHeight(a.Node, 1);

        var scrollContentView = new NodeWrapper(config, new List<NodeWrapper> { sibling, a });
        YGNodeStyleSetPositionType(scrollContentView.Node, YGPositionType.Absolute);

        var scrollView = new NodeWrapper(config, new List<NodeWrapper> { scrollContentView });
        YGNodeStyleSetWidth(scrollView.Node, 100);
        YGNodeStyleSetHeight(scrollView.Node, 100);

        YGNodeCalculateLayout(scrollView.Node, float.NaN, float.NaN, YGDirection.LTR);

        var siblingPrime = new NodeWrapper(config);
        YGNodeStyleSetHeight(siblingPrime.Node, 2);

        var scrollContentViewPrime = new NodeWrapper(scrollContentView, new List<NodeWrapper> { siblingPrime, a });
        var scrollViewPrime = new NodeWrapper(scrollView, new List<NodeWrapper> { scrollContentViewPrime });

        nodesCloned.Clear();

        YGNodeCalculateLayout(scrollViewPrime.Node, float.NaN, float.NaN, YGDirection.LTR);

        // We should only need to clone "A"
        Assert.Single(nodesCloned);
        Assert.Same(nodesCloned[0], a);

        YGConfigFree(config);
    }

    [Fact]
    public void Clone_leaf_display_contents_node()
    {
        var config = YGConfigNew();

        var nodesCloned = new List<NodeWrapper>();

        YGConfigSetCloneNodeFunc(config, (Node oldNode, Node owner, int childIndex) =>
        {
            var wrapper = (NodeWrapper)YGNodeGetContext(owner)!;
            var old = (NodeWrapper)YGNodeGetContext(oldNode)!;
            nodesCloned.Add(old);

            wrapper.Children[childIndex] = new NodeWrapper(old);
            return wrapper.Children[childIndex].Node;
        });

        var b = new NodeWrapper(config);
        var a = new NodeWrapper(config, new List<NodeWrapper> { b });
        YGNodeStyleSetDisplay(b.Node, YGDisplay.Contents);

        YGNodeCalculateLayout(a.Node, float.NaN, float.NaN, YGDirection.LTR);

        var aPrime = new NodeWrapper(config, new List<NodeWrapper> { b });

        nodesCloned.Clear();

        YGNodeCalculateLayout(aPrime.Node, 100, 100, YGDirection.LTR);

        // We should clone "B"
        Assert.Single(nodesCloned);
        Assert.Same(nodesCloned[0], b);

        YGConfigFree(config);
    }
}
