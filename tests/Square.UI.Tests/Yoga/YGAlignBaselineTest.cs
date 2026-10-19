// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGAlignBaselineTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YGConfigAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGAlignBaselineTest
{
    private static float BaselineFunc(Node node, float width, float height)
    {
        return height / 2;
    }

    private static YGSize Measure1(Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        return new YGSize { Width = 42, Height = 50 };
    }

    private static YGSize Measure2(Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        return new YGSize { Width = 279, Height = 126 };
    }

    private static Node CreateYGNode(Config config, YGFlexDirection direction, int width, int height, bool alignBaseline)
    {
        var node = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(node, direction);
        if (alignBaseline)
        {
            YGNodeStyleSetAlignItems(node, YGAlign.Baseline);
        }
        YGNodeStyleSetWidth(node, width);
        YGNodeStyleSetHeight(node, height);
        return node;
    }

    [Fact]
    public void Align_baseline_parent_ht_not_specified()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetAlignContent(root, YGAlign.Stretch);
        YGNodeStyleSetAlignItems(root, YGAlign.Baseline);
        YGNodeStyleSetWidth(root, 340);
        YGNodeStyleSetMaxHeight(root, 170);
        YGNodeStyleSetMinHeight(root, 0);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child0, 0);
        YGNodeStyleSetFlexShrink(root_child0, 1);
        YGNodeSetMeasureFunc(root_child0, Measure1);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexGrow(root_child1, 0);
        YGNodeStyleSetFlexShrink(root_child1, 1);
        YGNodeSetMeasureFunc(root_child1, Measure2);
        YGNodeInsertChild(root, root_child1, 1);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(340f, YGNodeLayoutGetWidth(root));
        Assert.Equal(126f, YGNodeLayoutGetHeight(root));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(42f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(76f, YGNodeLayoutGetTop(root_child0));

        Assert.Equal(42f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(279f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(126f, YGNodeLayoutGetHeight(root_child1));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void Align_baseline_with_no_parent_ht()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetAlignItems(root, YGAlign.Baseline);
        YGNodeStyleSetWidth(root, 150);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 50);
        YGNodeStyleSetHeight(root_child1, 40);
        YGNodeSetBaselineFunc(root_child1, BaselineFunc);
        YGNodeInsertChild(root, root_child1, 1);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(150f, YGNodeLayoutGetWidth(root));
        Assert.Equal(70f, YGNodeLayoutGetHeight(root));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(30f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(40f, YGNodeLayoutGetHeight(root_child1));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void Align_baseline_with_no_baseline_func_and_no_parent_ht()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetAlignItems(root, YGAlign.Baseline);
        YGNodeStyleSetWidth(root, 150);

        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 80);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 50);
        YGNodeStyleSetHeight(root_child1, 50);
        YGNodeInsertChild(root, root_child1, 1);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(150f, YGNodeLayoutGetWidth(root));
        Assert.Equal(80f, YGNodeLayoutGetHeight(root));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(80f, YGNodeLayoutGetHeight(root_child0));

        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(30f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void Align_baseline_parent_using_child_in_column_as_reference()
    {
        var config = YGConfigNew();

        var root = CreateYGNode(config, YGFlexDirection.Row, 1000, 1000, true);

        var root_child0 = CreateYGNode(config, YGFlexDirection.Column, 500, 600, false);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = CreateYGNode(config, YGFlexDirection.Column, 500, 800, false);
        YGNodeInsertChild(root, root_child1, 1);

        var root_child1_child0 = CreateYGNode(config, YGFlexDirection.Column, 500, 300, false);
        YGNodeInsertChild(root_child1, root_child1_child0, 0);

        var root_child1_child1 = CreateYGNode(config, YGFlexDirection.Column, 500, 400, false);
        YGNodeSetBaselineFunc(root_child1_child1, BaselineFunc);
        YGNodeSetIsReferenceBaseline(root_child1_child1, true);
        YGNodeInsertChild(root_child1, root_child1_child1, 1);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));

        Assert.Equal(500f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child1));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1_child0));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1_child1));
        Assert.Equal(300f, YGNodeLayoutGetTop(root_child1_child1));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void Align_baseline_parent_using_child_with_padding_in_column_as_reference()
    {
        var config = YGConfigNew();

        var root = CreateYGNode(config, YGFlexDirection.Row, 1000, 1000, true);

        var root_child0 = CreateYGNode(config, YGFlexDirection.Column, 500, 600, false);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = CreateYGNode(config, YGFlexDirection.Column, 500, 800, false);
        YGNodeInsertChild(root, root_child1, 1);

        var root_child1_child0 = CreateYGNode(config, YGFlexDirection.Column, 500, 300, false);
        YGNodeInsertChild(root_child1, root_child1_child0, 0);

        var root_child1_child1 = CreateYGNode(config, YGFlexDirection.Column, 500, 400, false);
        YGNodeSetBaselineFunc(root_child1_child1, BaselineFunc);
        YGNodeSetIsReferenceBaseline(root_child1_child1, true);
        YGNodeStyleSetPadding(root_child1_child1, YGEdge.Left, 100);
        YGNodeStyleSetPadding(root_child1_child1, YGEdge.Right, 100);
        YGNodeStyleSetPadding(root_child1_child1, YGEdge.Top, 100);
        YGNodeStyleSetPadding(root_child1_child1, YGEdge.Bottom, 100);
        YGNodeInsertChild(root_child1, root_child1_child1, 1);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));

        Assert.Equal(500f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child1));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1_child0));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1_child1));
        Assert.Equal(300f, YGNodeLayoutGetTop(root_child1_child1));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void Align_baseline_parent_using_child_in_column_as_reference_with_no_baseline_func()
    {
        var config = YGConfigNew();

        var root = CreateYGNode(config, YGFlexDirection.Row, 1000, 1000, true);

        var root_child0 = CreateYGNode(config, YGFlexDirection.Column, 500, 600, false);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = CreateYGNode(config, YGFlexDirection.Column, 500, 800, false);
        YGNodeInsertChild(root, root_child1, 1);

        var root_child1_child0 = CreateYGNode(config, YGFlexDirection.Column, 500, 300, false);
        YGNodeInsertChild(root_child1, root_child1_child0, 0);

        var root_child1_child1 = CreateYGNode(config, YGFlexDirection.Column, 500, 400, false);
        YGNodeSetIsReferenceBaseline(root_child1_child1, true);
        YGNodeInsertChild(root_child1, root_child1_child1, 1);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child0));

        Assert.Equal(500f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1_child0));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1_child1));
        Assert.Equal(300f, YGNodeLayoutGetTop(root_child1_child1));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void Align_baseline_parent_using_child_in_row_as_reference()
    {
        var config = YGConfigNew();

        var root = CreateYGNode(config, YGFlexDirection.Row, 1000, 1000, true);

        var root_child0 = CreateYGNode(config, YGFlexDirection.Column, 500, 600, false);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = CreateYGNode(config, YGFlexDirection.Row, 500, 800, true);
        YGNodeInsertChild(root, root_child1, 1);

        var root_child1_child0 = CreateYGNode(config, YGFlexDirection.Column, 500, 500, false);
        YGNodeInsertChild(root_child1, root_child1_child0, 0);

        var root_child1_child1 = CreateYGNode(config, YGFlexDirection.Column, 500, 400, false);
        YGNodeSetBaselineFunc(root_child1_child1, BaselineFunc);
        YGNodeSetIsReferenceBaseline(root_child1_child1, true);
        YGNodeInsertChild(root_child1, root_child1_child1, 1);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));

        Assert.Equal(500f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child1));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1_child0));

        Assert.Equal(500f, YGNodeLayoutGetLeft(root_child1_child1));
        Assert.Equal(300f, YGNodeLayoutGetTop(root_child1_child1));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void Align_baseline_parent_using_child_in_column_as_reference_with_height_not_specified()
    {
        var config = YGConfigNew();

        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetAlignItems(root, YGAlign.Baseline);
        YGNodeStyleSetWidth(root, 1000);

        var root_child0 = CreateYGNode(config, YGFlexDirection.Column, 500, 600, false);
        YGNodeInsertChild(root, root_child0, 0);

        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child1, YGFlexDirection.Column);
        YGNodeStyleSetWidth(root_child1, 500);
        YGNodeInsertChild(root, root_child1, 1);

        var root_child1_child0 = CreateYGNode(config, YGFlexDirection.Column, 500, 300, false);
        YGNodeInsertChild(root_child1, root_child1_child0, 0);

        var root_child1_child1 = CreateYGNode(config, YGFlexDirection.Column, 500, 400, false);
        YGNodeSetBaselineFunc(root_child1_child1, BaselineFunc);
        YGNodeSetIsReferenceBaseline(root_child1_child1, true);
        YGNodeInsertChild(root_child1, root_child1_child1, 1);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Equal(800f, YGNodeLayoutGetHeight(root));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));

        Assert.Equal(500f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(700f, YGNodeLayoutGetHeight(root_child1));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1_child0));

        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1_child1));
        Assert.Equal(300f, YGNodeLayoutGetTop(root_child1_child1));

        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }
}
