// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// @generated from yoga/tests/generated/YGIntrinsicSizeTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YGConfigAPI;

namespace Square.Rendering.Yoga.Tests;

public class YGIntrinsicSizeTest
{
    private static float LongestWordWidth(string text, float widthPerChar)
    {
        int maxLength = 0;
        int currentLength = 0;
        foreach (char c in text)
        {
            if (c == ' ')
            {
                maxLength = Math.Max(currentLength, maxLength);
                currentLength = 0;
            }
            else
            {
                currentLength++;
            }
        }
        return (float)Math.Max(currentLength, maxLength) * widthPerChar;
    }

    private static float CalculateHeight(string text, float measuredWidth, float widthPerChar, float heightPerChar)
    {
        if ((float)text.Length * widthPerChar <= measuredWidth)
        {
            return heightPerChar;
        }

        string[] words = text.Split(' ');
        float lines = 1;
        float currentLineLength = 0;
        foreach (string word in words)
        {
            float wordWidth = (float)word.Length * widthPerChar;
            if (wordWidth > measuredWidth)
            {
                if (currentLineLength > 0) lines++;
                lines++;
                currentLineLength = 0;
            }
            else if (currentLineLength + wordWidth <= measuredWidth)
            {
                currentLineLength += wordWidth + widthPerChar;
            }
            else
            {
                lines++;
                currentLineLength = wordWidth + widthPerChar;
            }
        }
        return (currentLineLength == 0 ? lines - 1 : lines) * heightPerChar;
    }

    private static YGSize IntrinsicSizeMeasure(Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        string innerText = (string)node.GetContext()!;
        float heightPerChar = 10;
        float widthPerChar = 10;
        float measuredWidth;
        float measuredHeight;

        if (widthMode == MeasureMode.Exactly)
        {
            measuredWidth = width;
        }
        else if (widthMode == MeasureMode.AtMost)
        {
            measuredWidth = Math.Min((float)innerText.Length * widthPerChar, width);
        }
        else
        {
            measuredWidth = (float)innerText.Length * widthPerChar;
        }

        if (heightMode == MeasureMode.Exactly)
        {
            measuredHeight = height;
        }
        else if (heightMode == MeasureMode.AtMost)
        {
            measuredHeight = Math.Min(
                CalculateHeight(
                    innerText,
                    YGNodeStyleGetFlexDirection(node) == YGFlexDirection.Column
                        ? measuredWidth
                        : Math.Max(LongestWordWidth(innerText, widthPerChar), measuredWidth),
                    widthPerChar,
                    heightPerChar),
                height);
        }
        else
        {
            measuredHeight = CalculateHeight(
                innerText,
                YGNodeStyleGetFlexDirection(node) == YGFlexDirection.Column
                    ? measuredWidth
                    : Math.Max(LongestWordWidth(innerText, widthPerChar), measuredWidth),
                widthPerChar,
                heightPerChar);
        }

        return new YGSize { Width = measuredWidth, Height = measuredHeight };
    }
    [Fact]
    public void contains_inner_text_long_word()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 2000);
        YGNodeStyleSetHeight(root, 2000);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeSetContext(root_child0, "LoremipsumdolorsitametconsecteturadipiscingelitSedeleifasdfettortoracauctorFuscerhoncusipsumtemporerosaliquamconsequatPraesentsoda");
        YGNodeSetMeasureFunc(root_child0, IntrinsicSizeMeasure);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(1300f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(700f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(1300f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void contains_inner_text_no_width_no_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 2000);
        YGNodeStyleSetHeight(root, 2000);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeSetContext(root_child0, "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed eleifasd et tortor ac auctor. Integer at volutpat libero, sed elementum dui interdum id. Aliquam consectetur massa vel neque aliquet, quis consequat risus fringilla. Fusce rhoncus ipsum tempor eros aliquam, vel tempus metus ullamcorper. Nam at nulla sed tellus vestibulum fringilla vel sit amet ligula. Proin velit lectus, euismod sit amet quam vel ultricies dolor, vitae finibus lorem ipsum. Pellentesque molestie at mi sit amet dictum. Donec vehicula lacinia felis sit amet consectetur. Praesent sodales enim sapien, sed varius ipsum pellentesque vel. Aenean eu mi eu justo tincidunt finibus vel sit amet ipsum. Sed bibasdum purus vel ipsum sagittis, quis fermentum dolor lobortis. Etiam vulputate eleifasd lectus vel varius. Phasellus imperdiet lectus sit amet ipsum egestas, ut bibasdum ipsum malesuada. Vestibulum ante ipsum primis in faucibus orci luctus et ultrices posuere cubilia Curae; Sed mollis eros sit amet elit porttitor, vel venenatis turpis venenatis. Nulla tempus tortor at eros efficitur, sit amet dapibus ipsum malesuada. Ut at mauris sed nunc malesuada convallis. Duis id sem vel magna varius eleifasd vel at est. Donec eget orci a ipsum tempor lobortis. Sed at consectetur ipsum.");
        YGNodeSetMeasureFunc(root_child0, IntrinsicSizeMeasure);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(70f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(70f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void contains_inner_text_no_width_no_height_long_word_in_paragraph()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 2000);
        YGNodeStyleSetHeight(root, 2000);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeSetContext(root_child0, "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed eleifasd et tortor ac auctor. Integer at volutpat libero, sed elementum dui interdum id. Aliquam consectetur massa vel neque aliquet, quis consequat risus fringilla. Fusce rhoncus ipsum tempor eros aliquam, vel tempus metus ullamcorper. Nam at nulla sed tellus vestibulum fringilla vel sit amet ligula. Proin velit lectus, euismod sit amet quam vel ultricies dolor, vitae finibus loremipsumloremipsumloremipsumloremipsumloremipsumloremipsumloremipsumloremipsumloremipsumloremipsumloremipsumloremipsumloremipsumlorem Pellentesque molestie at mi sit amet dictum. Donec vehicula lacinia felis sit amet consectetur. Praesent sodales enim sapien, sed varius ipsum pellentesque vel. Aenean eu mi eu justo tincidunt finibus vel sit amet ipsum. Sed bibasdum purus vel ipsum sagittis, quis fermentum dolor lobortis. Etiam vulputate eleifasd lectus vel varius. Phasellus imperdiet lectus sit amet ipsum egestas, ut bibasdum ipsum malesuada. Vestibulum ante ipsum primis in faucibus orci luctus et ultrices posuere cubilia Curae; Sed mollis eros sit amet elit porttitor, vel venenatis turpis venenatis. Nulla tempus tortor at eros efficitur, sit amet dapibus ipsum malesuada. Ut at mauris sed nunc malesuada convallis. Duis id sem vel magna varius eleifasd vel at est. Donec eget orci a ipsum tempor lobortis. Sed at consectetur ipsum.");
        YGNodeSetMeasureFunc(root_child0, IntrinsicSizeMeasure);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(70f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(70f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void contains_inner_text_fixed_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 2000);
        YGNodeStyleSetHeight(root, 2000);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeStyleSetWidth(root_child0, 100);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeSetContext(root_child0, "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed eleifasd et tortor ac auctor. Integer at volutpat libero, sed elementum dui interdum id. Aliquam consectetur massa vel neque aliquet, quis consequat risus fringilla. Fusce rhoncus ipsum tempor eros aliquam, vel tempus metus ullamcorper. Nam at nulla sed tellus vestibulum fringilla vel sit amet ligula. Proin velit lectus, euismod sit amet quam vel ultricies dolor, vitae finibus lorem ipsum. Pellentesque molestie at mi sit amet dictum. Donec vehicula lacinia felis sit amet consectetur. Praesent sodales enim sapien, sed varius ipsum pellentesque vel. Aenean eu mi eu justo tincidunt finibus vel sit amet ipsum. Sed bibasdum purus vel ipsum sagittis, quis fermentum dolor lobortis. Etiam vulputate eleifasd lectus vel varius. Phasellus imperdiet lectus sit amet ipsum egestas, ut bibasdum ipsum malesuada. Vestibulum ante ipsum primis in faucibus orci luctus et ultrices posuere cubilia Curae; Sed mollis eros sit amet elit porttitor, vel venenatis turpis venenatis. Nulla tempus tortor at eros efficitur, sit amet dapibus ipsum malesuada. Ut at mauris sed nunc malesuada convallis. Duis id sem vel magna varius eleifasd vel at est. Donec eget orci a ipsum tempor lobortis. Sed at consectetur ipsum.");
        YGNodeSetMeasureFunc(root_child0, IntrinsicSizeMeasure);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(1290f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(1900f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(1290f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void contains_inner_text_no_width_fixed_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 2000);
        YGNodeStyleSetHeight(root, 2000);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeStyleSetHeight(root_child0, 20);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeSetContext(root_child0, "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed eleifasd et tortor ac auctor. Integer at volutpat libero, sed elementum dui interdum id. Aliquam consectetur massa vel neque aliquet, quis consequat risus fringilla. Fusce rhoncus ipsum tempor eros aliquam, vel tempus metus ullamcorper. Nam at nulla sed tellus vestibulum fringilla vel sit amet ligula. Proin velit lectus, euismod sit amet quam vel ultricies dolor, vitae finibus lorem ipsum. Pellentesque molestie at mi sit amet dictum. Donec vehicula lacinia felis sit amet consectetur. Praesent sodales enim sapien, sed varius ipsum pellentesque vel. Aenean eu mi eu justo tincidunt finibus vel sit amet ipsum. Sed bibasdum purus vel ipsum sagittis, quis fermentum dolor lobortis. Etiam vulputate eleifasd lectus vel varius. Phasellus imperdiet lectus sit amet ipsum egestas, ut bibasdum ipsum malesuada. Vestibulum ante ipsum primis in faucibus orci luctus et ultrices posuere cubilia Curae; Sed mollis eros sit amet elit porttitor, vel venenatis turpis venenatis. Nulla tempus tortor at eros efficitur, sit amet dapibus ipsum malesuada. Ut at mauris sed nunc malesuada convallis. Duis id sem vel magna varius eleifasd vel at est. Donec eget orci a ipsum tempor lobortis. Sed at consectetur ipsum.");
        YGNodeSetMeasureFunc(root_child0, IntrinsicSizeMeasure);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void contains_inner_text_fixed_width_fixed_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 2000);
        YGNodeStyleSetHeight(root, 2000);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 20);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeSetContext(root_child0, "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed eleifasd et tortor ac auctor. Integer at volutpat libero, sed elementum dui interdum id. Aliquam consectetur massa vel neque aliquet, quis consequat risus fringilla. Fusce rhoncus ipsum tempor eros aliquam, vel tempus metus ullamcorper. Nam at nulla sed tellus vestibulum fringilla vel sit amet ligula. Proin velit lectus, euismod sit amet quam vel ultricies dolor, vitae finibus lorem ipsum. Pellentesque molestie at mi sit amet dictum. Donec vehicula lacinia felis sit amet consectetur. Praesent sodales enim sapien, sed varius ipsum pellentesque vel. Aenean eu mi eu justo tincidunt finibus vel sit amet ipsum. Sed bibasdum purus vel ipsum sagittis, quis fermentum dolor lobortis. Etiam vulputate eleifasd lectus vel varius. Phasellus imperdiet lectus sit amet ipsum egestas, ut bibasdum ipsum malesuada. Vestibulum ante ipsum primis in faucibus orci luctus et ultrices posuere cubilia Curae; Sed mollis eros sit amet elit porttitor, vel venenatis turpis venenatis. Nulla tempus tortor at eros efficitur, sit amet dapibus ipsum malesuada. Ut at mauris sed nunc malesuada convallis. Duis id sem vel magna varius eleifasd vel at est. Donec eget orci a ipsum tempor lobortis. Sed at consectetur ipsum.");
        YGNodeSetMeasureFunc(root_child0, IntrinsicSizeMeasure);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(1950f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void contains_inner_text_max_width_max_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 2000);
        YGNodeStyleSetHeight(root, 2000);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeStyleSetMaxWidth(root_child0, 50);
        YGNodeStyleSetMaxHeight(root_child0, 20);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeSetContext(root_child0, "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed eleifasd et tortor ac auctor. Integer at volutpat libero, sed elementum dui interdum id. Aliquam consectetur massa vel neque aliquet, quis consequat risus fringilla. Fusce rhoncus ipsum tempor eros aliquam, vel tempus metus ullamcorper. Nam at nulla sed tellus vestibulum fringilla vel sit amet ligula. Proin velit lectus, euismod sit amet quam vel ultricies dolor, vitae finibus lorem ipsum. Pellentesque molestie at mi sit amet dictum. Donec vehicula lacinia felis sit amet consectetur. Praesent sodales enim sapien, sed varius ipsum pellentesque vel. Aenean eu mi eu justo tincidunt finibus vel sit amet ipsum. Sed bibasdum purus vel ipsum sagittis, quis fermentum dolor lobortis. Etiam vulputate eleifasd lectus vel varius. Phasellus imperdiet lectus sit amet ipsum egestas, ut bibasdum ipsum malesuada. Vestibulum ante ipsum primis in faucibus orci luctus et ultrices posuere cubilia Curae; Sed mollis eros sit amet elit porttitor, vel venenatis turpis venenatis. Nulla tempus tortor at eros efficitur, sit amet dapibus ipsum malesuada. Ut at mauris sed nunc malesuada convallis. Duis id sem vel magna varius eleifasd vel at est. Donec eget orci a ipsum tempor lobortis. Sed at consectetur ipsum.");
        YGNodeSetMeasureFunc(root_child0, IntrinsicSizeMeasure);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(1950f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void contains_inner_text_max_width_max_height_column()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 2000);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetMaxWidth(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeSetContext(root_child0, "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed eleifasd et tortor ac auctor. Integer at volutpat libero, sed elementum dui interdum id. Aliquam consectetur massa vel neque aliquet, quis consequat risus fringilla. Fusce rhoncus ipsum tempor eros aliquam, vel tempus metus ullamcorper. Nam at nulla sed tellus vestibulum fringilla vel sit amet ligula. Proin velit lectus, euismod sit amet quam vel ultricies dolor, vitae finibus lorem ipsum. Pellentesque molestie at mi sit amet dictum. Donec vehicula lacinia felis sit amet consectetur. Praesent sodales enim sapien, sed varius ipsum pellentesque vel. Aenean eu mi eu justo tincidunt finibus vel sit amet ipsum. Sed bibasdum purus vel ipsum sagittis, quis fermentum dolor lobortis. Etiam vulputate eleifasd lectus vel varius. Phasellus imperdiet lectus sit amet ipsum egestas, ut bibasdum ipsum malesuada. Vestibulum ante ipsum primis in faucibus orci luctus et ultrices posuere cubilia Curae; Sed mollis eros sit amet elit porttitor, vel venenatis turpis venenatis. Nulla tempus tortor at eros efficitur, sit amet dapibus ipsum malesuada. Ut at mauris sed nunc malesuada convallis. Duis id sem vel magna varius eleifasd vel at est. Donec eget orci a ipsum tempor lobortis. Sed at consectetur ipsum.");
        YGNodeSetMeasureFunc(root_child0, IntrinsicSizeMeasure);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(1890f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(1890f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(1890f, YGNodeLayoutGetHeight(root));
        Assert.Equal(1950f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(1890f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void contains_inner_text_max_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 2000);
        YGNodeStyleSetHeight(root, 2000);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeStyleSetMaxWidth(root_child0, 100);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeSetContext(root_child0, "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed eleifasd et tortor ac auctor. Integer at volutpat libero, sed elementum dui interdum id. Aliquam consectetur massa vel neque aliquet, quis consequat risus fringilla. Fusce rhoncus ipsum tempor eros aliquam, vel tempus metus ullamcorper. Nam at nulla sed tellus vestibulum fringilla vel sit amet ligula. Proin velit lectus, euismod sit amet quam vel ultricies dolor, vitae finibus lorem ipsum. Pellentesque molestie at mi sit amet dictum. Donec vehicula lacinia felis sit amet consectetur. Praesent sodales enim sapien, sed varius ipsum pellentesque vel. Aenean eu mi eu justo tincidunt finibus vel sit amet ipsum. Sed bibasdum purus vel ipsum sagittis, quis fermentum dolor lobortis. Etiam vulputate eleifasd lectus vel varius. Phasellus imperdiet lectus sit amet ipsum egestas, ut bibasdum ipsum malesuada. Vestibulum ante ipsum primis in faucibus orci luctus et ultrices posuere cubilia Curae; Sed mollis eros sit amet elit porttitor, vel venenatis turpis venenatis. Nulla tempus tortor at eros efficitur, sit amet dapibus ipsum malesuada. Ut at mauris sed nunc malesuada convallis. Duis id sem vel magna varius eleifasd vel at est. Donec eget orci a ipsum tempor lobortis. Sed at consectetur ipsum.");
        YGNodeSetMeasureFunc(root_child0, IntrinsicSizeMeasure);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(1290f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(1900f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(1290f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void contains_inner_text_fixed_width_shorter_text()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 2000);
        YGNodeStyleSetHeight(root, 2000);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeStyleSetWidth(root_child0, 100);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeSetContext(root_child0, "Lorem ipsum");
        YGNodeSetMeasureFunc(root_child0, IntrinsicSizeMeasure);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(1900f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void contains_inner_text_fixed_height_shorter_text()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 2000);
        YGNodeStyleSetHeight(root, 2000);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeStyleSetHeight(root_child0, 100);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeSetContext(root_child0, "Lorem ipsum");
        YGNodeSetMeasureFunc(root_child0, IntrinsicSizeMeasure);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(1890f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(110f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void contains_inner_text_max_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 2000);
        YGNodeStyleSetHeight(root, 2000);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeStyleSetMaxHeight(root_child0, 20);
        YGNodeInsertChild(root, root_child0, 0);
        YGNodeSetContext(root_child0, "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed eleifasd et tortor ac auctor. Integer at volutpat libero, sed elementum dui interdum id. Aliquam consectetur massa vel neque aliquet, quis consequat risus fringilla. Fusce rhoncus ipsum tempor eros aliquam, vel tempus metus ullamcorper. Nam at nulla sed tellus vestibulum fringilla vel sit amet ligula. Proin velit lectus, euismod sit amet quam vel ultricies dolor, vitae finibus lorem ipsum. Pellentesque molestie at mi sit amet dictum. Donec vehicula lacinia felis sit amet consectetur. Praesent sodales enim sapien, sed varius ipsum pellentesque vel. Aenean eu mi eu justo tincidunt finibus vel sit amet ipsum. Sed bibasdum purus vel ipsum sagittis, quis fermentum dolor lobortis. Etiam vulputate eleifasd lectus vel varius. Phasellus imperdiet lectus sit amet ipsum egestas, ut bibasdum ipsum malesuada. Vestibulum ante ipsum primis in faucibus orci luctus et ultrices posuere cubilia Curae; Sed mollis eros sit amet elit porttitor, vel venenatis turpis venenatis. Nulla tempus tortor at eros efficitur, sit amet dapibus ipsum malesuada. Ut at mauris sed nunc malesuada convallis. Duis id sem vel magna varius eleifasd vel at est. Donec eget orci a ipsum tempor lobortis. Sed at consectetur ipsum.");
        YGNodeSetMeasureFunc(root_child0, IntrinsicSizeMeasure);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root));
        Assert.Equal(2000f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(2000f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void max_content_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetWidthMaxContent(root);
        YGNodeStyleSetFlexWrap(root, YGWrap.Wrap);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 100);
        YGNodeStyleSetHeight(root_child1, 50);
        YGNodeInsertChild(root, root_child1, 1);
        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child2, 25);
        YGNodeStyleSetHeight(root_child2, 50);
        YGNodeInsertChild(root, root_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(175f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(150f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(175f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(125f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void fit_content_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 90);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeStyleSetWidthFitContent(root_child0);
        YGNodeStyleSetFlexWrap(root_child0, YGWrap.Wrap);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 50);
        YGNodeStyleSetHeight(root_child0_child0, 50);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child1, 100);
        YGNodeStyleSetHeight(root_child0_child1, 50);
        YGNodeInsertChild(root_child0, root_child0_child1, 1);
        var root_child0_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child2, 25);
        YGNodeStyleSetHeight(root_child0_child2, 50);
        YGNodeInsertChild(root_child0, root_child0_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(90f, YGNodeLayoutGetWidth(root));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(90f, YGNodeLayoutGetWidth(root));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root));
        Assert.Equal(-10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void stretch_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 500);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeStyleSetWidthStretch(root_child0);
        YGNodeStyleSetFlexWrap(root_child0, YGWrap.Wrap);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 50);
        YGNodeStyleSetHeight(root_child0_child0, 50);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child1, 100);
        YGNodeStyleSetHeight(root_child0_child1, 50);
        YGNodeInsertChild(root_child0, root_child0_child1, 1);
        var root_child0_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child2, 25);
        YGNodeStyleSetHeight(root_child0_child2, 50);
        YGNodeInsertChild(root_child0, root_child0_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(150f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(450f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(350f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(325f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void max_content_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeightMaxContent(root);
        YGNodeStyleSetFlexWrap(root, YGWrap.Wrap);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 50);
        YGNodeStyleSetHeight(root_child1, 100);
        YGNodeInsertChild(root, root_child1, 1);
        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child2, 50);
        YGNodeStyleSetHeight(root_child2, 25);
        YGNodeInsertChild(root, root_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(175f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(150f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(175f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(150f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void fit_content_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 90);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeightFitContent(root_child0);
        YGNodeStyleSetFlexWrap(root_child0, YGWrap.Wrap);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 50);
        YGNodeStyleSetHeight(root_child0_child0, 50);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child1, 50);
        YGNodeStyleSetHeight(root_child0_child1, 100);
        YGNodeInsertChild(root_child0, root_child0_child1, 1);
        var root_child0_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child2, 50);
        YGNodeStyleSetHeight(root_child0_child2, 25);
        YGNodeInsertChild(root_child0, root_child0_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(90f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(175f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(150f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(90f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(175f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(150f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void stretch_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 500);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetHeightStretch(root_child0);
        YGNodeStyleSetFlexWrap(root_child0, YGWrap.Wrap);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 50);
        YGNodeStyleSetHeight(root_child0_child0, 50);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child1, 50);
        YGNodeStyleSetHeight(root_child0_child1, 100);
        YGNodeInsertChild(root_child0, root_child0_child1, 1);
        var root_child0_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child2, 50);
        YGNodeStyleSetHeight(root_child0_child2, 25);
        YGNodeInsertChild(root_child0, root_child0_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(150f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(150f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void max_content_flex_basis_column()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetFlexBasisMaxContent(root);
        YGNodeStyleSetFlexWrap(root, YGWrap.Wrap);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 50);
        YGNodeStyleSetHeight(root_child1, 100);
        YGNodeInsertChild(root, root_child1, 1);
        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child2, 50);
        YGNodeStyleSetHeight(root_child2, 25);
        YGNodeInsertChild(root, root_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(175f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(150f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(175f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(150f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void fit_content_flex_basis_column()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 90);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexBasisFitContent(root_child0);
        YGNodeStyleSetFlexWrap(root_child0, YGWrap.Wrap);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 50);
        YGNodeStyleSetHeight(root_child0_child0, 50);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child1, 50);
        YGNodeStyleSetHeight(root_child0_child1, 100);
        YGNodeInsertChild(root_child0, root_child0_child1, 1);
        var root_child0_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child2, 50);
        YGNodeStyleSetHeight(root_child0_child2, 25);
        YGNodeInsertChild(root_child0, root_child0_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(90f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(175f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(150f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(90f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(175f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(150f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void stretch_flex_basis_column()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 500);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexBasisStretch(root_child0);
        YGNodeStyleSetFlexWrap(root_child0, YGWrap.Wrap);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 50);
        YGNodeStyleSetHeight(root_child0_child0, 50);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child1, 50);
        YGNodeStyleSetHeight(root_child0_child1, 100);
        YGNodeInsertChild(root_child0, root_child0_child1, 1);
        var root_child0_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child2, 50);
        YGNodeStyleSetHeight(root_child0_child2, 25);
        YGNodeInsertChild(root_child0, root_child0_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(175f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(150f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(175f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(150f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void max_content_flex_basis_row()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetFlexBasisMaxContent(root);
        YGNodeStyleSetFlexWrap(root, YGWrap.Wrap);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 100);
        YGNodeStyleSetHeight(root_child1, 500);
        YGNodeInsertChild(root, root_child1, 1);
        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child2, 25);
        YGNodeStyleSetHeight(root_child2, 50);
        YGNodeInsertChild(root, root_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(600f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(550f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root));
        Assert.Equal(600f, YGNodeLayoutGetHeight(root));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(550f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void fit_content_flex_basis_row()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 90);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeStyleSetFlexBasisFitContent(root_child0);
        YGNodeStyleSetFlexWrap(root_child0, YGWrap.Wrap);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 50);
        YGNodeStyleSetHeight(root_child0_child0, 50);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child1, 100);
        YGNodeStyleSetHeight(root_child0_child1, 50);
        YGNodeInsertChild(root_child0, root_child0_child1, 1);
        var root_child0_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child2, 25);
        YGNodeStyleSetHeight(root_child0_child2, 50);
        YGNodeInsertChild(root_child0, root_child0_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(90f, YGNodeLayoutGetWidth(root));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(90f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(90f, YGNodeLayoutGetWidth(root));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(90f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(40f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(-10f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(65f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void stretch_flex_basis_row()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 500);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeStyleSetFlexBasisStretch(root_child0);
        YGNodeStyleSetFlexWrap(root_child0, YGWrap.Wrap);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 50);
        YGNodeStyleSetHeight(root_child0_child0, 50);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child1, 100);
        YGNodeStyleSetHeight(root_child0_child1, 50);
        YGNodeInsertChild(root_child0, root_child0_child1, 1);
        var root_child0_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child2, 25);
        YGNodeStyleSetHeight(root_child0_child2, 50);
        YGNodeInsertChild(root_child0, root_child0_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(150f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(450f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(350f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(325f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void max_content_max_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetMaxWidthMaxContent(root);
        YGNodeStyleSetWidth(root, 200);
        YGNodeStyleSetFlexWrap(root, YGWrap.Wrap);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 100);
        YGNodeStyleSetHeight(root_child1, 50);
        YGNodeInsertChild(root, root_child1, 1);
        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child2, 25);
        YGNodeStyleSetHeight(root_child2, 50);
        YGNodeInsertChild(root, root_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(175f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(150f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(175f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(125f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void fit_content_max_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 90);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeStyleSetMaxWidthFitContent(root_child0);
        YGNodeStyleSetWidth(root_child0, 110);
        YGNodeStyleSetFlexWrap(root_child0, YGWrap.Wrap);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 50);
        YGNodeStyleSetHeight(root_child0_child0, 50);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child1, 100);
        YGNodeStyleSetHeight(root_child0_child1, 50);
        YGNodeInsertChild(root_child0, root_child0_child1, 1);
        var root_child0_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child2, 25);
        YGNodeStyleSetHeight(root_child0_child2, 50);
        YGNodeInsertChild(root_child0, root_child0_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(90f, YGNodeLayoutGetWidth(root));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(90f, YGNodeLayoutGetWidth(root));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root));
        Assert.Equal(-10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void stretch_max_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 500);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeStyleSetMaxWidthStretch(root_child0);
        YGNodeStyleSetWidth(root_child0, 600);
        YGNodeStyleSetFlexWrap(root_child0, YGWrap.Wrap);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 50);
        YGNodeStyleSetHeight(root_child0_child0, 50);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child1, 100);
        YGNodeStyleSetHeight(root_child0_child1, 50);
        YGNodeInsertChild(root_child0, root_child0_child1, 1);
        var root_child0_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child2, 25);
        YGNodeStyleSetHeight(root_child0_child2, 50);
        YGNodeInsertChild(root_child0, root_child0_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(150f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(450f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(350f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(325f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void max_content_min_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetMinWidthMaxContent(root);
        YGNodeStyleSetWidth(root, 100);
        YGNodeStyleSetFlexWrap(root, YGWrap.Wrap);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 100);
        YGNodeStyleSetHeight(root_child1, 50);
        YGNodeInsertChild(root, root_child1, 1);
        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child2, 25);
        YGNodeStyleSetHeight(root_child2, 50);
        YGNodeInsertChild(root, root_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(175f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(150f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(175f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(125f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(25f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void fit_content_min_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 90);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeStyleSetMinWidthFitContent(root_child0);
        YGNodeStyleSetWidth(root_child0, 90);
        YGNodeStyleSetFlexWrap(root_child0, YGWrap.Wrap);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 50);
        YGNodeStyleSetHeight(root_child0_child0, 50);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child1, 100);
        YGNodeStyleSetHeight(root_child0_child1, 50);
        YGNodeInsertChild(root_child0, root_child0_child1, 1);
        var root_child0_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child2, 25);
        YGNodeStyleSetHeight(root_child0_child2, 50);
        YGNodeInsertChild(root_child0, root_child0_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(90f, YGNodeLayoutGetWidth(root));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(90f, YGNodeLayoutGetWidth(root));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root));
        Assert.Equal(-10f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(150f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(75f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(100f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void stretch_min_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 500);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0, YGFlexDirection.Row);
        YGNodeStyleSetMinWidthStretch(root_child0);
        YGNodeStyleSetWidth(root_child0, 400);
        YGNodeStyleSetFlexWrap(root_child0, YGWrap.Wrap);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 50);
        YGNodeStyleSetHeight(root_child0_child0, 50);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child1, 100);
        YGNodeStyleSetHeight(root_child0_child1, 50);
        YGNodeInsertChild(root_child0, root_child0_child1, 1);
        var root_child0_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child2, 25);
        YGNodeStyleSetHeight(root_child0_child2, 50);
        YGNodeInsertChild(root_child0, root_child0_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(150f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(450f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(350f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(325f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void max_content_max_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetMaxHeightMaxContent(root);
        YGNodeStyleSetHeight(root, 200);
        YGNodeStyleSetFlexWrap(root, YGWrap.Wrap);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 50);
        YGNodeStyleSetHeight(root_child1, 100);
        YGNodeInsertChild(root, root_child1, 1);
        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child2, 50);
        YGNodeStyleSetHeight(root_child2, 25);
        YGNodeInsertChild(root, root_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(175f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(150f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(175f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(150f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void fit_content_max_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 90);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetMaxHeightFitContent(root_child0);
        YGNodeStyleSetHeight(root_child0, 110);
        YGNodeStyleSetFlexWrap(root_child0, YGWrap.Wrap);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 50);
        YGNodeStyleSetHeight(root_child0_child0, 50);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child1, 50);
        YGNodeStyleSetHeight(root_child0_child1, 100);
        YGNodeInsertChild(root_child0, root_child0_child1, 1);
        var root_child0_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child2, 50);
        YGNodeStyleSetHeight(root_child0_child2, 25);
        YGNodeInsertChild(root_child0, root_child0_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(90f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(90f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(-50f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(-100f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void stretch_max_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 500);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetMaxHeightStretch(root_child0);
        YGNodeStyleSetFlexWrap(root_child0, YGWrap.Wrap);
        YGNodeStyleSetHeight(root_child0, 600);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 50);
        YGNodeStyleSetHeight(root_child0_child0, 50);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child1, 50);
        YGNodeStyleSetHeight(root_child0_child1, 100);
        YGNodeInsertChild(root_child0, root_child0_child1, 1);
        var root_child0_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child2, 50);
        YGNodeStyleSetHeight(root_child0_child2, 25);
        YGNodeInsertChild(root_child0, root_child0_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(150f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(150f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void max_content_min_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetMinHeightMaxContent(root);
        YGNodeStyleSetHeight(root, 100);
        YGNodeStyleSetFlexWrap(root, YGWrap.Wrap);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0, 50);
        YGNodeStyleSetHeight(root_child0, 50);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child1, 50);
        YGNodeStyleSetHeight(root_child1, 100);
        YGNodeInsertChild(root, root_child1, 1);
        var root_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child2, 50);
        YGNodeStyleSetHeight(root_child2, 25);
        YGNodeInsertChild(root, root_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(-50f, YGNodeLayoutGetLeft(root_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child1));
        Assert.Equal(-100f, YGNodeLayoutGetLeft(root_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void fit_content_min_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 90);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetMinHeightFitContent(root_child0);
        YGNodeStyleSetHeight(root_child0, 90);
        YGNodeStyleSetFlexWrap(root_child0, YGWrap.Wrap);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 50);
        YGNodeStyleSetHeight(root_child0_child0, 50);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child1, 50);
        YGNodeStyleSetHeight(root_child0_child1, 100);
        YGNodeInsertChild(root_child0, root_child0_child1, 1);
        var root_child0_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child2, 50);
        YGNodeStyleSetHeight(root_child0_child2, 25);
        YGNodeInsertChild(root_child0, root_child0_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(90f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(90f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(-50f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(-100f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void stretch_min_height()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetHeight(root, 500);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetMinHeightStretch(root_child0);
        YGNodeStyleSetFlexWrap(root_child0, YGWrap.Wrap);
        YGNodeStyleSetHeight(root_child0, 400);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child0, 50);
        YGNodeStyleSetHeight(root_child0_child0, 50);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        var root_child0_child1 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child1, 50);
        YGNodeStyleSetHeight(root_child0_child1, 100);
        YGNodeInsertChild(root_child0, root_child0_child1, 1);
        var root_child0_child2 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidth(root_child0_child2, 50);
        YGNodeStyleSetHeight(root_child0_child2, 25);
        YGNodeInsertChild(root_child0, root_child0_child2, 2);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(150f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(500f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(50f, YGNodeLayoutGetHeight(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetTop(root_child0_child1));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child1));
        Assert.Equal(100f, YGNodeLayoutGetHeight(root_child0_child1));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child2));
        Assert.Equal(150f, YGNodeLayoutGetTop(root_child0_child2));
        Assert.Equal(50f, YGNodeLayoutGetWidth(root_child0_child2));
        Assert.Equal(25f, YGNodeLayoutGetHeight(root_child0_child2));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void text_max_content_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthMaxContent(root_child0);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0_child0, YGFlexDirection.Row);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        YGNodeSetContext(root_child0_child0, "Lorem ipsum sdafhasdfkjlasdhlkajsfhasldkfhasdlkahsdflkjasdhflaksdfasdlkjhasdlfjahsdfljkasdhalsdfhas dolor sit amet");
        YGNodeSetMeasureFunc(root_child0_child0, IntrinsicSizeMeasure);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(1140f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(1140f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root));
        Assert.Equal(-940f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(1140f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(1140f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void text_stretch_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthStretch(root_child0);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0_child0, YGFlexDirection.Row);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        YGNodeSetContext(root_child0_child0, "Lorem ipsum dolor sit amet");
        YGNodeSetMeasureFunc(root_child0_child0, IntrinsicSizeMeasure);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void text_fit_content_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetWidthFitContent(root_child0);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0_child0, YGFlexDirection.Row);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        YGNodeSetContext(root_child0_child0, "Lorem ipsum sdafhasdfkjlasdhlkajsfhasldkfhasdlkahsdflkjasdhflaksdfasdlkjhasdlfjahsdfljkasdhalsdfhas dolor sit amet");
        YGNodeSetMeasureFunc(root_child0_child0, IntrinsicSizeMeasure);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(870f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(870f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root));
        Assert.Equal(-670f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(870f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(870f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void text_max_content_min_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetMinWidthMaxContent(root_child0);
        YGNodeStyleSetWidth(root_child0, 200);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0_child0, YGFlexDirection.Row);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        YGNodeSetContext(root_child0_child0, "Lorem ipsum sdafhasdfkjlasdhlkajsfhasldkfhasdlkahsdflkjasdhflaksdfasdlkjhasdlfjahsdfljkasdhalsdfhas dolor sit amet");
        YGNodeSetMeasureFunc(root_child0_child0, IntrinsicSizeMeasure);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(1140f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(1140f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root));
        Assert.Equal(-940f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(1140f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(1140f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void text_stretch_min_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetMinWidthStretch(root_child0);
        YGNodeStyleSetWidth(root_child0, 100);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0_child0, YGFlexDirection.Row);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        YGNodeSetContext(root_child0_child0, "Lorem ipsum dolor sit amet");
        YGNodeSetMeasureFunc(root_child0_child0, IntrinsicSizeMeasure);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void text_fit_content_min_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetMinWidthFitContent(root_child0);
        YGNodeStyleSetWidth(root_child0, 300);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0_child0, YGFlexDirection.Row);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        YGNodeSetContext(root_child0_child0, "Lorem ipsum sdafhasdfkjlasdhlkajsfhasldkfhasdlkahsdflkjasdhflaksdfasdlkjhasdlfjahsdfljkasdhalsdfhas dolor sit amet");
        YGNodeSetMeasureFunc(root_child0_child0, IntrinsicSizeMeasure);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(870f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(870f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root));
        Assert.Equal(-670f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(870f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(870f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void text_max_content_max_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetMaxWidthMaxContent(root_child0);
        YGNodeStyleSetWidth(root_child0, 2000);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0_child0, YGFlexDirection.Row);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        YGNodeSetContext(root_child0_child0, "Lorem ipsum sdafhasdfkjlasdhlkajsfhasldkfhasdlkahsdflkjasdhflaksdfasdlkjhasdlfjahsdfljkasdhalsdfhas dolor sit amet");
        YGNodeSetMeasureFunc(root_child0_child0, IntrinsicSizeMeasure);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(1140f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(1140f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root));
        Assert.Equal(-940f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(1140f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(1140f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(10f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact]
    public void text_stretch_max_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetMaxWidthStretch(root_child0);
        YGNodeStyleSetWidth(root_child0, 300);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0_child0, YGFlexDirection.Row);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        YGNodeSetContext(root_child0_child0, "Lorem ipsum dolor sit amet");
        YGNodeSetMeasureFunc(root_child0_child0, IntrinsicSizeMeasure);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(20f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

    [Fact(Skip = "Skipped: matches upstream C++ GTEST_SKIP()")]
    public void text_fit_content_max_width()
    {
        var config = YGConfigNew();
        var root = YGNodeNewWithConfig(config);
        YGNodeStyleSetPositionType(root, YGPositionType.Absolute);
        YGNodeStyleSetWidth(root, 200);
        var root_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetMaxWidthFitContent(root_child0);
        YGNodeStyleSetWidth(root_child0, 1000);
        YGNodeInsertChild(root, root_child0, 0);
        var root_child0_child0 = YGNodeNewWithConfig(config);
        YGNodeStyleSetFlexDirection(root_child0_child0, YGFlexDirection.Row);
        YGNodeInsertChild(root_child0, root_child0_child0, 0);
        YGNodeSetContext(root_child0_child0, "Lorem ipsum sdafhasdfkjlasdhlkajsfhasldkfhasdlkahsdflkjasdhflaksdfasdlkjhasdlfjahsdfljkasdhalsdfhas dolor sit amet");
        YGNodeSetMeasureFunc(root_child0_child0, IntrinsicSizeMeasure);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(870f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(870f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.RTL);
        Assert.Equal(0f, YGNodeLayoutGetLeft(root));
        Assert.Equal(0f, YGNodeLayoutGetTop(root));
        Assert.Equal(200f, YGNodeLayoutGetWidth(root));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root));
        Assert.Equal(-670f, YGNodeLayoutGetLeft(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0));
        Assert.Equal(870f, YGNodeLayoutGetWidth(root_child0));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root_child0));
        Assert.Equal(0f, YGNodeLayoutGetLeft(root_child0_child0));
        Assert.Equal(0f, YGNodeLayoutGetTop(root_child0_child0));
        Assert.Equal(870f, YGNodeLayoutGetWidth(root_child0_child0));
        Assert.Equal(30f, YGNodeLayoutGetHeight(root_child0_child0));
        YGNodeFreeRecursive(root);
        YGConfigFree(config);
    }

}

