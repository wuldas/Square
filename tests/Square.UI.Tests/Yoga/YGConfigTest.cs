// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGConfigTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGConfigAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGConfigTest
{
    private static Node clonedNode = YGNodeNew();

    private static Node? CloneNode(Node oldNode, Node owner, int childIndex)
    {
        return clonedNode;
    }

    private static Node? DoNotClone(Node oldNode, Node owner, int childIndex)
    {
        return null;
    }

    [Fact]
    public void Uses_values_provided_by_cloning_callback()
    {
        var config = YGConfigNew();
        YGConfigSetCloneNodeFunc(config, CloneNode);

        var node = YGNodeNewWithConfig(config);
        var owner = YGNodeNewWithConfig(config);
        YGNodeInsertChild(owner, node, 0);

        var clone = config.CloneNode(node, owner, 0);

        Assert.Same(clonedNode, clone);

        YGNodeFreeRecursive(owner);
    }

    [Fact]
    public void Falls_back_to_regular_cloning_if_callback_returns_null()
    {
        var config = YGConfigNew();
        YGConfigSetCloneNodeFunc(config, DoNotClone);

        var node = YGNodeNewWithConfig(config);
        var owner = YGNodeNewWithConfig(config);
        YGNodeInsertChild(owner, node, 0);

        var clone = config.CloneNode(node, owner, 0);

        Assert.NotNull(clone);
        YGNodeFree(clone!);

        YGNodeFreeRecursive(owner);
    }
}
