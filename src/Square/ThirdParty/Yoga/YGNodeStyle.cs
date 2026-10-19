// Copyright (c) Meta Platforms, Inc. and affiliates.
//
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
//
// Original: yoga/YGNodeStyle.h, yoga/YGNodeStyle.cpp

using System;

namespace Square.Rendering.Yoga
{
    /// <summary>
    /// Public C-style API for Yoga node style properties.
    /// Delegates to the internal Node/Style classes.
    /// </summary>
    internal static class YGNodeStyleAPI
    {
        // Helper: update a style property (simple value)
    private static void UpdateStyle<TValue>(
            Node node,
        Func<Style, TValue> getter,
        Action<Style, TValue> setter,
        TValue value)
            where TValue : IEquatable<TValue>
    {
            var style = node.Style;
            if (!getter(style).Equals(value))
        {
            setter(style, value);
            node.MarkDirtyAndPropagate();
        }
    }

        // Helper: update a style property (indexed value)
        private static void UpdateStyleIndexed<TIdx, TValue>(
            Node node,
        Func<Style, TIdx, TValue> getter,
        Action<Style, TIdx, TValue> setter,
        TIdx idx,
        TValue value)
    {
            var style = node.Style;
        if (!Equals(getter(style, idx), value))
        {
            setter(style, idx, value);
            node.MarkDirtyAndPropagate();
        }
    }

        // Helper: update an enum-style property
        private static void UpdateStyleEnum<TEnum>(
            Node node,
            Func<Style, TEnum> getter,
            Action<Style, TEnum> setter,
            TEnum value)
            where TEnum : struct
        {
            var style = node.Style;
            if (!getter(style).Equals(value))
            {
                setter(style, value);
                node.MarkDirtyAndPropagate();
            }
        }

        // --- CopyStyle ---

        public static void YGNodeCopyStyle(Node dstNode, Node srcNode)
        {
            if (!dstNode.Style.Equals(srcNode.Style))
            {
                dstNode.SetStyle(srcNode.Style);
            dstNode.MarkDirtyAndPropagate();
        }
    }

        // --- Direction ---

        public static void YGNodeStyleSetDirection(Node node, YGDirection value)
        {
            UpdateStyleEnum(node,
                s => s.Direction,
                (s, v) => s.Direction = v,
                value.ToInternal());
        }

        public static YGDirection YGNodeStyleGetDirection(Node node)
        {
            return node.Style.Direction.ToYG();
        }

        // --- FlexDirection ---

        public static void YGNodeStyleSetFlexDirection(Node node, YGFlexDirection value)
        {
            UpdateStyleEnum(node,
                s => s.FlexDirection,
                (s, v) => s.FlexDirection = v,
                value.ToInternal());
        }

        public static YGFlexDirection YGNodeStyleGetFlexDirection(Node node)
        {
            return node.Style.FlexDirection.ToYG();
        }

        // --- JustifyContent ---

        public static void YGNodeStyleSetJustifyContent(Node node, YGJustify value)
        {
            UpdateStyleEnum(node,
                s => s.JustifyContent,
                (s, v) => s.JustifyContent = v,
                value.ToInternal());
        }

        public static YGJustify YGNodeStyleGetJustifyContent(Node node)
        {
            return node.Style.JustifyContent.ToYG();
        }

        // --- JustifyItems ---

        public static void YGNodeStyleSetJustifyItems(Node node, YGJustify value)
        {
            UpdateStyleEnum(node,
                s => s.JustifyItems,
                (s, v) => s.JustifyItems = v,
                value.ToInternal());
        }

        public static YGJustify YGNodeStyleGetJustifyItems(Node node)
        {
            return node.Style.JustifyItems.ToYG();
        }

        // --- JustifySelf ---

        public static void YGNodeStyleSetJustifySelf(Node node, YGJustify value)
        {
            UpdateStyleEnum(node,
                s => s.JustifySelf,
                (s, v) => s.JustifySelf = v,
                value.ToInternal());
        }

        public static YGJustify YGNodeStyleGetJustifySelf(Node node)
        {
            return node.Style.JustifySelf.ToYG();
        }

        // --- AlignContent ---

        public static void YGNodeStyleSetAlignContent(Node node, YGAlign value)
        {
            UpdateStyleEnum(node,
                s => s.AlignContent,
                (s, v) => s.AlignContent = v,
                value.ToInternal());
        }

        public static YGAlign YGNodeStyleGetAlignContent(Node node)
        {
            return node.Style.AlignContent.ToYG();
        }

        // --- AlignItems ---

        public static void YGNodeStyleSetAlignItems(Node node, YGAlign value)
        {
            UpdateStyleEnum(node,
                s => s.AlignItems,
                (s, v) => s.AlignItems = v,
                value.ToInternal());
        }

        public static YGAlign YGNodeStyleGetAlignItems(Node node)
        {
            return node.Style.AlignItems.ToYG();
        }

        // --- AlignSelf ---

        public static void YGNodeStyleSetAlignSelf(Node node, YGAlign value)
        {
            UpdateStyleEnum(node,
                s => s.AlignSelf,
                (s, v) => s.AlignSelf = v,
                value.ToInternal());
        }

        public static YGAlign YGNodeStyleGetAlignSelf(Node node)
        {
            return node.Style.AlignSelf.ToYG();
        }

        // --- PositionType ---

        public static void YGNodeStyleSetPositionType(Node node, YGPositionType value)
        {
            UpdateStyleEnum(node,
                s => s.PositionType,
                (s, v) => s.PositionType = v,
                value.ToInternal());
        }

        public static YGPositionType YGNodeStyleGetPositionType(Node node)
        {
            return node.Style.PositionType.ToYG();
        }

        // --- FlexWrap ---

        public static void YGNodeStyleSetFlexWrap(Node node, YGWrap value)
        {
            UpdateStyleEnum(node,
                s => s.FlexWrap,
                (s, v) => s.FlexWrap = v,
                value.ToInternal());
        }

        public static YGWrap YGNodeStyleGetFlexWrap(Node node)
        {
            return node.Style.FlexWrap.ToYG();
        }

        // --- Overflow ---

        public static void YGNodeStyleSetOverflow(Node node, YGOverflow value)
        {
            UpdateStyleEnum(node,
                s => s.Overflow,
                (s, v) => s.Overflow = v,
                value.ToInternal());
        }

        public static YGOverflow YGNodeStyleGetOverflow(Node node)
        {
            return node.Style.Overflow.ToYG();
        }

        // --- Display ---

        public static void YGNodeStyleSetDisplay(Node node, YGDisplay value)
        {
            UpdateStyleEnum(node,
                s => s.Display,
                (s, v) => s.Display = v,
                value.ToInternal());
        }

        public static YGDisplay YGNodeStyleGetDisplay(Node node)
        {
            return node.Style.Display.ToYG();
        }

        // --- Flex ---

        public static void YGNodeStyleSetFlex(Node node, float flex)
        {
            var newValue = new FloatOptional(flex);
            if (node.Style.Flex != newValue)
            {
                node.Style.Flex = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static float YGNodeStyleGetFlex(Node node)
        {
            return node.Style.Flex.IsUndefined()
                ? YogaConstants.Undefined
                : node.Style.Flex.Unwrap();
        }

        // --- FlexGrow ---

        public static void YGNodeStyleSetFlexGrow(Node node, float flexGrow)
        {
            var newValue = new FloatOptional(flexGrow);
            if (node.Style.FlexGrow != newValue)
            {
                node.Style.FlexGrow = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static float YGNodeStyleGetFlexGrow(Node node)
        {
            return node.Style.FlexGrow.IsUndefined()
                ? Style.DefaultFlexGrow
                : node.Style.FlexGrow.Unwrap();
        }

        // --- FlexShrink ---

        public static void YGNodeStyleSetFlexShrink(Node node, float flexShrink)
        {
            var newValue = new FloatOptional(flexShrink);
            if (node.Style.FlexShrink != newValue)
            {
                node.Style.FlexShrink = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static float YGNodeStyleGetFlexShrink(Node node)
        {
            return node.Style.FlexShrink.IsUndefined()
                ? (node.GetConfig()!.UseWebDefaults()
                    ? Style.WebDefaultFlexShrink
                    : Style.DefaultFlexShrink)
                : node.Style.FlexShrink.Unwrap();
        }

        // --- FlexBasis ---

        public static void YGNodeStyleSetFlexBasis(Node node, float flexBasis)
        {
            var newValue = StyleSizeLength.Points(flexBasis);
            if (node.Style.FlexBasis != newValue)
            {
                node.Style.FlexBasis = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static void YGNodeStyleSetFlexBasisPercent(Node node, float flexBasisPercent)
        {
            var newValue = StyleSizeLength.Percent(flexBasisPercent);
            if (node.Style.FlexBasis != newValue)
            {
                node.Style.FlexBasis = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static void YGNodeStyleSetFlexBasisAuto(Node node)
        {
            var newValue = StyleSizeLength.OfAuto();
            if (node.Style.FlexBasis != newValue)
            {
                node.Style.FlexBasis = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static void YGNodeStyleSetFlexBasisMaxContent(Node node)
        {
            var newValue = StyleSizeLength.OfMaxContent();
            if (node.Style.FlexBasis != newValue)
            {
                node.Style.FlexBasis = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static void YGNodeStyleSetFlexBasisFitContent(Node node)
        {
            var newValue = StyleSizeLength.OfFitContent();
            if (node.Style.FlexBasis != newValue)
            {
                node.Style.FlexBasis = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static void YGNodeStyleSetFlexBasisStretch(Node node)
        {
            var newValue = StyleSizeLength.OfStretch();
            if (node.Style.FlexBasis != newValue)
            {
                node.Style.FlexBasis = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static YGValue YGNodeStyleGetFlexBasis(Node node)
        {
            return node.Style.FlexBasis.ToYGValue();
        }

        // --- Position ---

        public static void YGNodeStyleSetPosition(Node node, YGEdge edge, float points)
        {
            var internalEdge = edge.ToInternal();
            var newValue = StyleLength.Points(points);
            if (node.Style.Position(internalEdge) != newValue)
            {
                node.Style.SetPosition(internalEdge, newValue);
                node.MarkDirtyAndPropagate();
            }
        }

        public static void YGNodeStyleSetPositionPercent(Node node, YGEdge edge, float percent)
        {
            var internalEdge = edge.ToInternal();
            var newValue = StyleLength.Percent(percent);
            if (node.Style.Position(internalEdge) != newValue)
            {
                node.Style.SetPosition(internalEdge, newValue);
                node.MarkDirtyAndPropagate();
            }
        }

        public static void YGNodeStyleSetPositionAuto(Node node, YGEdge edge)
        {
            var internalEdge = edge.ToInternal();
            var newValue = StyleLength.OfAuto();
            if (node.Style.Position(internalEdge) != newValue)
            {
                node.Style.SetPosition(internalEdge, newValue);
                node.MarkDirtyAndPropagate();
            }
        }

        public static YGValue YGNodeStyleGetPosition(Node node, YGEdge edge)
        {
            return (YGValue)node.Style.Position(edge.ToInternal());
        }

        // --- Margin ---

        public static void YGNodeStyleSetMargin(Node node, YGEdge edge, float points)
        {
            var internalEdge = edge.ToInternal();
            var newValue = StyleLength.Points(points);
            if (node.Style.Margin(internalEdge) != newValue)
            {
                node.Style.SetMargin(internalEdge, newValue);
                node.MarkDirtyAndPropagate();
            }
        }

        public static void YGNodeStyleSetMarginPercent(Node node, YGEdge edge, float percent)
        {
            var internalEdge = edge.ToInternal();
            var newValue = StyleLength.Percent(percent);
            if (node.Style.Margin(internalEdge) != newValue)
            {
                node.Style.SetMargin(internalEdge, newValue);
                node.MarkDirtyAndPropagate();
            }
        }

        public static void YGNodeStyleSetMarginAuto(Node node, YGEdge edge)
        {
            var internalEdge = edge.ToInternal();
            var newValue = StyleLength.OfAuto();
            if (node.Style.Margin(internalEdge) != newValue)
            {
                node.Style.SetMargin(internalEdge, newValue);
                node.MarkDirtyAndPropagate();
            }
        }

        public static YGValue YGNodeStyleGetMargin(Node node, YGEdge edge)
        {
            return (YGValue)node.Style.Margin(edge.ToInternal());
        }

        // --- Padding ---

        public static void YGNodeStyleSetPadding(Node node, YGEdge edge, float points)
        {
            var internalEdge = edge.ToInternal();
            var newValue = StyleLength.Points(points);
            if (node.Style.Padding(internalEdge) != newValue)
            {
                node.Style.SetPadding(internalEdge, newValue);
                node.MarkDirtyAndPropagate();
            }
        }

        public static void YGNodeStyleSetPaddingPercent(Node node, YGEdge edge, float percent)
        {
            var internalEdge = edge.ToInternal();
            var newValue = StyleLength.Percent(percent);
            if (node.Style.Padding(internalEdge) != newValue)
            {
                node.Style.SetPadding(internalEdge, newValue);
                node.MarkDirtyAndPropagate();
            }
        }

        public static YGValue YGNodeStyleGetPadding(Node node, YGEdge edge)
        {
            return (YGValue)node.Style.Padding(edge.ToInternal());
        }

        // --- Border ---

        public static void YGNodeStyleSetBorder(Node node, YGEdge edge, float border)
        {
            var internalEdge = edge.ToInternal();
            var newValue = StyleLength.Points(border);
            if (node.Style.Border(internalEdge) != newValue)
            {
                node.Style.SetBorder(internalEdge, newValue);
                node.MarkDirtyAndPropagate();
            }
        }

        public static float YGNodeStyleGetBorder(Node node, YGEdge edge)
        {
            var border = node.Style.Border(edge.ToInternal());
            if (border.IsUndefined() || border.IsAuto())
            {
                return YogaConstants.Undefined;
            }

        return ((YGValue)border).Value;
    }

        // --- Gap ---

        public static void YGNodeStyleSetGap(Node node, YGGutter gutter, float gapLength)
        {
            var internalGutter = gutter.ToInternal();
            var newValue = StyleLength.Points(gapLength);
            if (node.Style.Gap(internalGutter) != newValue)
            {
                node.Style.SetGap(internalGutter, newValue);
                node.MarkDirtyAndPropagate();
            }
        }

        public static void YGNodeStyleSetGapPercent(Node node, YGGutter gutter, float percent)
        {
            var internalGutter = gutter.ToInternal();
            var newValue = StyleLength.Percent(percent);
            if (node.Style.Gap(internalGutter) != newValue)
            {
                node.Style.SetGap(internalGutter, newValue);
                node.MarkDirtyAndPropagate();
            }
        }

        public static YGValue YGNodeStyleGetGap(Node node, YGGutter gutter)
        {
            return (YGValue)node.Style.Gap(gutter.ToInternal());
        }

        // --- AspectRatio ---

        public static void YGNodeStyleSetAspectRatio(Node node, float aspectRatio)
        {
            var newValue = new FloatOptional(aspectRatio);
            if (node.Style.AspectRatio != newValue)
            {
                node.Style.AspectRatio = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static float YGNodeStyleGetAspectRatio(Node node)
        {
            var op = node.Style.AspectRatio;
            return op.IsUndefined() ? YogaConstants.Undefined : op.Unwrap();
        }

        // --- BoxSizing ---

        public static void YGNodeStyleSetBoxSizing(Node node, YGBoxSizing boxSizing)
        {
            UpdateStyleEnum(node,
                s => s.BoxSizing,
                (s, v) => s.BoxSizing = v,
                boxSizing.ToInternal());
        }

        public static YGBoxSizing YGNodeStyleGetBoxSizing(Node node)
        {
            return node.Style.BoxSizing.ToYG();
        }

        // --- Width ---

        public static void YGNodeStyleSetWidth(Node node, float points)
        {
            SetDimension(node, Dimension.Width, StyleSizeLength.Points(points));
        }

        public static void YGNodeStyleSetWidthPercent(Node node, float percent)
        {
            SetDimension(node, Dimension.Width, StyleSizeLength.Percent(percent));
        }

        public static void YGNodeStyleSetWidthAuto(Node node)
        {
            SetDimension(node, Dimension.Width, StyleSizeLength.OfAuto());
        }

        public static void YGNodeStyleSetWidthMaxContent(Node node)
        {
            SetDimension(node, Dimension.Width, StyleSizeLength.OfMaxContent());
        }

        public static void YGNodeStyleSetWidthFitContent(Node node)
        {
            SetDimension(node, Dimension.Width, StyleSizeLength.OfFitContent());
        }

        public static void YGNodeStyleSetWidthStretch(Node node)
        {
            SetDimension(node, Dimension.Width, StyleSizeLength.OfStretch());
        }

        public static YGValue YGNodeStyleGetWidth(Node node)
        {
            return node.Style.Dimension(Dimension.Width).ToYGValue();
        }

        // --- Height ---

        public static void YGNodeStyleSetHeight(Node node, float points)
        {
            SetDimension(node, Dimension.Height, StyleSizeLength.Points(points));
        }

        public static void YGNodeStyleSetHeightPercent(Node node, float percent)
        {
            SetDimension(node, Dimension.Height, StyleSizeLength.Percent(percent));
        }

        public static void YGNodeStyleSetHeightAuto(Node node)
        {
            SetDimension(node, Dimension.Height, StyleSizeLength.OfAuto());
        }

        public static void YGNodeStyleSetHeightMaxContent(Node node)
        {
            SetDimension(node, Dimension.Height, StyleSizeLength.OfMaxContent());
        }

        public static void YGNodeStyleSetHeightFitContent(Node node)
        {
            SetDimension(node, Dimension.Height, StyleSizeLength.OfFitContent());
        }

        public static void YGNodeStyleSetHeightStretch(Node node)
        {
            SetDimension(node, Dimension.Height, StyleSizeLength.OfStretch());
        }

        public static YGValue YGNodeStyleGetHeight(Node node)
        {
            return node.Style.Dimension(Dimension.Height).ToYGValue();
        }

        // --- MinWidth ---

        public static void YGNodeStyleSetMinWidth(Node node, float minWidth)
        {
            SetMinDimension(node, Dimension.Width, StyleSizeLength.Points(minWidth));
        }

        public static void YGNodeStyleSetMinWidthPercent(Node node, float minWidth)
        {
            SetMinDimension(node, Dimension.Width, StyleSizeLength.Percent(minWidth));
        }

        public static void YGNodeStyleSetMinWidthMaxContent(Node node)
        {
            SetMinDimension(node, Dimension.Width, StyleSizeLength.OfMaxContent());
        }

        public static void YGNodeStyleSetMinWidthFitContent(Node node)
        {
            SetMinDimension(node, Dimension.Width, StyleSizeLength.OfFitContent());
        }

        public static void YGNodeStyleSetMinWidthStretch(Node node)
        {
            SetMinDimension(node, Dimension.Width, StyleSizeLength.OfStretch());
        }

        public static YGValue YGNodeStyleGetMinWidth(Node node)
        {
            return node.Style.MinDimension(Dimension.Width).ToYGValue();
        }

        // --- MinHeight ---

        public static void YGNodeStyleSetMinHeight(Node node, float minHeight)
        {
            SetMinDimension(node, Dimension.Height, StyleSizeLength.Points(minHeight));
        }

        public static void YGNodeStyleSetMinHeightPercent(Node node, float minHeight)
        {
            SetMinDimension(node, Dimension.Height, StyleSizeLength.Percent(minHeight));
        }

        public static void YGNodeStyleSetMinHeightMaxContent(Node node)
        {
            SetMinDimension(node, Dimension.Height, StyleSizeLength.OfMaxContent());
        }

        public static void YGNodeStyleSetMinHeightFitContent(Node node)
        {
            SetMinDimension(node, Dimension.Height, StyleSizeLength.OfFitContent());
        }

        public static void YGNodeStyleSetMinHeightStretch(Node node)
        {
            SetMinDimension(node, Dimension.Height, StyleSizeLength.OfStretch());
        }

        public static YGValue YGNodeStyleGetMinHeight(Node node)
        {
            return node.Style.MinDimension(Dimension.Height).ToYGValue();
        }

        // --- MaxWidth ---

        public static void YGNodeStyleSetMaxWidth(Node node, float maxWidth)
        {
            SetMaxDimension(node, Dimension.Width, StyleSizeLength.Points(maxWidth));
        }

        public static void YGNodeStyleSetMaxWidthPercent(Node node, float maxWidth)
        {
            SetMaxDimension(node, Dimension.Width, StyleSizeLength.Percent(maxWidth));
        }

        public static void YGNodeStyleSetMaxWidthMaxContent(Node node)
        {
            SetMaxDimension(node, Dimension.Width, StyleSizeLength.OfMaxContent());
        }

        public static void YGNodeStyleSetMaxWidthFitContent(Node node)
        {
            SetMaxDimension(node, Dimension.Width, StyleSizeLength.OfFitContent());
        }

        public static void YGNodeStyleSetMaxWidthStretch(Node node)
        {
            SetMaxDimension(node, Dimension.Width, StyleSizeLength.OfStretch());
        }

        public static YGValue YGNodeStyleGetMaxWidth(Node node)
        {
            return node.Style.MaxDimension(Dimension.Width).ToYGValue();
        }

        // --- MaxHeight ---

        public static void YGNodeStyleSetMaxHeight(Node node, float maxHeight)
        {
            SetMaxDimension(node, Dimension.Height, StyleSizeLength.Points(maxHeight));
        }

        public static void YGNodeStyleSetMaxHeightPercent(Node node, float maxHeight)
        {
            SetMaxDimension(node, Dimension.Height, StyleSizeLength.Percent(maxHeight));
        }

        public static void YGNodeStyleSetMaxHeightMaxContent(Node node)
        {
            SetMaxDimension(node, Dimension.Height, StyleSizeLength.OfMaxContent());
        }

        public static void YGNodeStyleSetMaxHeightFitContent(Node node)
        {
            SetMaxDimension(node, Dimension.Height, StyleSizeLength.OfFitContent());
        }

        public static void YGNodeStyleSetMaxHeightStretch(Node node)
        {
            SetMaxDimension(node, Dimension.Height, StyleSizeLength.OfStretch());
        }

        public static YGValue YGNodeStyleGetMaxHeight(Node node)
        {
            return node.Style.MaxDimension(Dimension.Height).ToYGValue();
        }

        // --- Grid Item Properties ---

        // GridColumnStart
        public static void YGNodeStyleSetGridColumnStart(Node node, int gridColumnStart)
        {
            var newValue = GridLine.FromInteger(gridColumnStart);
            if (node.Style.GridColumnStart != newValue)
            {
                node.Style.GridColumnStart = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static void YGNodeStyleSetGridColumnStartAuto(Node node)
        {
            var newValue = GridLine.Auto();
            if (node.Style.GridColumnStart != newValue)
            {
                node.Style.GridColumnStart = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static void YGNodeStyleSetGridColumnStartSpan(Node node, int span)
        {
            var newValue = GridLine.Span(span);
            if (node.Style.GridColumnStart != newValue)
            {
                node.Style.GridColumnStart = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static int YGNodeStyleGetGridColumnStart(Node node)
        {
            var gridLine = node.Style.GridColumnStart;
            return gridLine.IsInteger() ? gridLine.Integer : 0;
        }

        // GridColumnEnd
        public static void YGNodeStyleSetGridColumnEnd(Node node, int gridColumnEnd)
        {
            var newValue = GridLine.FromInteger(gridColumnEnd);
            if (node.Style.GridColumnEnd != newValue)
            {
                node.Style.GridColumnEnd = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static void YGNodeStyleSetGridColumnEndAuto(Node node)
        {
            var newValue = GridLine.Auto();
            if (node.Style.GridColumnEnd != newValue)
            {
                node.Style.GridColumnEnd = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static void YGNodeStyleSetGridColumnEndSpan(Node node, int span)
        {
            var newValue = GridLine.Span(span);
            if (node.Style.GridColumnEnd != newValue)
            {
                node.Style.GridColumnEnd = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static int YGNodeStyleGetGridColumnEnd(Node node)
        {
            var gridLine = node.Style.GridColumnEnd;
            return gridLine.IsInteger() ? gridLine.Integer : 0;
        }

        // GridRowStart
        public static void YGNodeStyleSetGridRowStart(Node node, int gridRowStart)
        {
            var newValue = GridLine.FromInteger(gridRowStart);
            if (node.Style.GridRowStart != newValue)
            {
                node.Style.GridRowStart = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static void YGNodeStyleSetGridRowStartAuto(Node node)
        {
            var newValue = GridLine.Auto();
            if (node.Style.GridRowStart != newValue)
            {
                node.Style.GridRowStart = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static void YGNodeStyleSetGridRowStartSpan(Node node, int span)
        {
            var newValue = GridLine.Span(span);
            if (node.Style.GridRowStart != newValue)
            {
                node.Style.GridRowStart = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static int YGNodeStyleGetGridRowStart(Node node)
        {
            var gridLine = node.Style.GridRowStart;
            return gridLine.IsInteger() ? gridLine.Integer : 0;
        }

        // GridRowEnd
        public static void YGNodeStyleSetGridRowEnd(Node node, int gridRowEnd)
        {
            var newValue = GridLine.FromInteger(gridRowEnd);
            if (node.Style.GridRowEnd != newValue)
            {
                node.Style.GridRowEnd = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static void YGNodeStyleSetGridRowEndAuto(Node node)
        {
            var newValue = GridLine.Auto();
            if (node.Style.GridRowEnd != newValue)
            {
                node.Style.GridRowEnd = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static void YGNodeStyleSetGridRowEndSpan(Node node, int span)
        {
            var newValue = GridLine.Span(span);
            if (node.Style.GridRowEnd != newValue)
            {
                node.Style.GridRowEnd = newValue;
                node.MarkDirtyAndPropagate();
            }
        }

        public static int YGNodeStyleGetGridRowEnd(Node node)
        {
            var gridLine = node.Style.GridRowEnd;
            return gridLine.IsInteger() ? gridLine.Integer : 0;
        }

        // --- Grid Container Properties ---

        // Helper: convert YGGridTrackType to GridTrackSize
        private static GridTrackSize GridTrackSizeFromTypeAndValue(YGGridTrackType type, float value)
        {
            return type switch
            {
                YGGridTrackType.Points => GridTrackSize.Length(value),
                YGGridTrackType.Percent => GridTrackSize.Percent(value),
                YGGridTrackType.Fr => GridTrackSize.Fr(value),
                YGGridTrackType.Auto => GridTrackSize.Auto(),
                YGGridTrackType.Minmax => GridTrackSize.Auto(),
                _ => throw new InvalidOperationException("Unknown YGGridTrackType"),
            };
        }

        // Helper: convert YGGridTrackType to StyleSizeLength (for minmax)
        private static StyleSizeLength StyleSizeLengthFromTypeAndValue(YGGridTrackType type, float value)
        {
            return type switch
            {
                YGGridTrackType.Points => StyleSizeLength.Points(value),
                YGGridTrackType.Percent => StyleSizeLength.Percent(value),
                YGGridTrackType.Fr => StyleSizeLength.Stretch(value),
                YGGridTrackType.Auto => StyleSizeLength.OfAuto(),
                YGGridTrackType.Minmax => StyleSizeLength.OfAuto(),
                _ => throw new InvalidOperationException("Unknown YGGridTrackType"),
            };
        }

        // GridTemplateColumns
        public static void YGNodeStyleSetGridTemplateColumnsCount(Node node, int count)
        {
            node.Style.ResizeGridTemplateColumns(count);
            node.MarkDirtyAndPropagate();
        }

        public static void YGNodeStyleSetGridTemplateColumn(Node node, int index, YGGridTrackType type, float value)
        {
            node.Style.SetGridTemplateColumnAt(index, GridTrackSizeFromTypeAndValue(type, value));
            node.MarkDirtyAndPropagate();
        }

        public static void YGNodeStyleSetGridTemplateColumnMinMax(
            Node node,
            int index,
            YGGridTrackType minType,
            float minValue,
            YGGridTrackType maxType,
            float maxValue)
        {
            node.Style.SetGridTemplateColumnAt(
                index,
                GridTrackSize.MinMax(
                    StyleSizeLengthFromTypeAndValue(minType, minValue),
                    StyleSizeLengthFromTypeAndValue(maxType, maxValue)));
            node.MarkDirtyAndPropagate();
        }

        // GridTemplateRows
        public static void YGNodeStyleSetGridTemplateRowsCount(Node node, int count)
        {
            node.Style.ResizeGridTemplateRows(count);
            node.MarkDirtyAndPropagate();
        }

        public static void YGNodeStyleSetGridTemplateRow(Node node, int index, YGGridTrackType type, float value)
        {
            node.Style.SetGridTemplateRowAt(index, GridTrackSizeFromTypeAndValue(type, value));
            node.MarkDirtyAndPropagate();
        }

        public static void YGNodeStyleSetGridTemplateRowMinMax(
            Node node,
            int index,
            YGGridTrackType minType,
            float minValue,
            YGGridTrackType maxType,
            float maxValue)
        {
            node.Style.SetGridTemplateRowAt(
                index,
                GridTrackSize.MinMax(
                    StyleSizeLengthFromTypeAndValue(minType, minValue),
                    StyleSizeLengthFromTypeAndValue(maxType, maxValue)));
            node.MarkDirtyAndPropagate();
        }

        // GridAutoColumns
        public static void YGNodeStyleSetGridAutoColumnsCount(Node node, int count)
        {
            node.Style.ResizeGridAutoColumns(count);
            node.MarkDirtyAndPropagate();
        }

        public static void YGNodeStyleSetGridAutoColumn(Node node, int index, YGGridTrackType type, float value)
        {
            node.Style.SetGridAutoColumnAt(index, GridTrackSizeFromTypeAndValue(type, value));
            node.MarkDirtyAndPropagate();
        }

        public static void YGNodeStyleSetGridAutoColumnMinMax(
            Node node,
            int index,
            YGGridTrackType minType,
            float minValue,
            YGGridTrackType maxType,
            float maxValue)
        {
            node.Style.SetGridAutoColumnAt(
                index,
                GridTrackSize.MinMax(
                    StyleSizeLengthFromTypeAndValue(minType, minValue),
                    StyleSizeLengthFromTypeAndValue(maxType, maxValue)));
            node.MarkDirtyAndPropagate();
        }

        // GridAutoRows
        public static void YGNodeStyleSetGridAutoRowsCount(Node node, int count)
        {
            node.Style.ResizeGridAutoRows(count);
            node.MarkDirtyAndPropagate();
        }

        public static void YGNodeStyleSetGridAutoRow(Node node, int index, YGGridTrackType type, float value)
        {
            node.Style.SetGridAutoRowAt(index, GridTrackSizeFromTypeAndValue(type, value));
            node.MarkDirtyAndPropagate();
        }

        public static void YGNodeStyleSetGridAutoRowMinMax(
            Node node,
            int index,
            YGGridTrackType minType,
            float minValue,
            YGGridTrackType maxType,
            float maxValue)
        {
            node.Style.SetGridAutoRowAt(
                index,
                GridTrackSize.MinMax(
                    StyleSizeLengthFromTypeAndValue(minType, minValue),
                    StyleSizeLengthFromTypeAndValue(maxType, maxValue)));
            node.MarkDirtyAndPropagate();
        }

        // --- Internal Helpers for Dimension/MinDimension/MaxDimension ---

        private static void SetDimension(Node node, Dimension dim, StyleSizeLength value)
        {
            if (node.Style.Dimension(dim) != value)
            {
                node.Style.SetDimension(dim, value);
                node.MarkDirtyAndPropagate();
                node.ProcessDimensions();
            }
        }

        private static void SetMinDimension(Node node, Dimension dim, StyleSizeLength value)
        {
            if (node.Style.MinDimension(dim) != value)
            {
                node.Style.SetMinDimension(dim, value);
                node.MarkDirtyAndPropagate();
                node.ProcessDimensions();
            }
        }

        private static void SetMaxDimension(Node node, Dimension dim, StyleSizeLength value)
        {
            if (node.Style.MaxDimension(dim) != value)
            {
                node.Style.SetMaxDimension(dim, value);
                node.MarkDirtyAndPropagate();
                node.ProcessDimensions();
            }
        }
    }
}
