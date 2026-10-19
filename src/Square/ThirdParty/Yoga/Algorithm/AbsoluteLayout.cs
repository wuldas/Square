// Copyright (c) Meta Platforms, Inc. and affiliates.
//
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.

using System;

namespace Square.Rendering.Yoga
{
    internal static class AbsoluteLayout
    {
        private static void SetFlexStartLayoutPosition(
            Node parent,
            Node child,
            Direction direction,
            FlexDirection axis,
            float containingBlockWidth)
        {
            float position = child.Style.ComputeFlexStartMargin(
                axis, direction, containingBlockWidth) +
                parent.Layout.Border(axis.FlexStartEdge());

            // https://www.w3.org/TR/css-grid-1/#abspos
            // absolute positioned grid items are positioned relative to the padding edge
            // of the grid container
            if (!child.HasErrata(Errata.AbsolutePositionWithoutInsetsExcludesPadding) &&
                parent.Style.Display != Display.Grid)
            {
                position += parent.Layout.Padding(axis.FlexStartEdge());
            }

            child.SetLayoutPosition(position, axis.FlexStartEdge());
        }

        private static void SetFlexEndLayoutPosition(
            Node parent,
            Node child,
            Direction direction,
            FlexDirection axis,
            float containingBlockWidth)
        {
            float flexEndPosition = parent.Layout.Border(axis.FlexEndEdge()) +
                child.Style.ComputeFlexEndMargin(
                    axis, direction, containingBlockWidth);

            // https://www.w3.org/TR/css-grid-1/#abspos
            // absolute positioned grid items are positioned relative to the padding edge
            // of the grid container
            if (!child.HasErrata(Errata.AbsolutePositionWithoutInsetsExcludesPadding) &&
                parent.Style.Display != Display.Grid)
            {
                flexEndPosition += parent.Layout.Padding(axis.FlexEndEdge());
            }

            child.SetLayoutPosition(
                TrailingPosition.GetPositionOfOppositeEdge(flexEndPosition, axis, parent, child),
                axis.FlexStartEdge());
        }

        private static void SetCenterLayoutPosition(
            Node parent,
            Node child,
            Direction direction,
            FlexDirection axis,
            float containingBlockWidth)
        {
            float parentContentBoxSize =
                parent.Layout.MeasuredDimension(axis.Dimension()) -
                parent.Layout.Border(axis.FlexStartEdge()) -
                parent.Layout.Border(axis.FlexEndEdge());

            // https://www.w3.org/TR/css-grid-1/#abspos
            // absolute positioned grid items are positioned relative to the padding edge
            // of the grid container
            if (!child.HasErrata(Errata.AbsolutePositionWithoutInsetsExcludesPadding) &&
                parent.Style.Display != Display.Grid)
            {
                parentContentBoxSize -= parent.Layout.Padding(axis.FlexStartEdge());
                parentContentBoxSize -= parent.Layout.Padding(axis.FlexEndEdge());
            }

            float childOuterSize =
                child.Layout.MeasuredDimension(axis.Dimension()) +
                child.Style.ComputeMarginForAxis(axis, containingBlockWidth);

            float position = (parentContentBoxSize - childOuterSize) / 2.0f +
                parent.Layout.Border(axis.FlexStartEdge()) +
                child.Style.ComputeFlexStartMargin(
                    axis, direction, containingBlockWidth);

            // https://www.w3.org/TR/css-grid-1/#abspos
            // absolute positioned grid items are positioned relative to the padding edge
            // of the grid container
            if (!child.HasErrata(Errata.AbsolutePositionWithoutInsetsExcludesPadding) &&
                parent.Style.Display != Display.Grid)
            {
                position += parent.Layout.Padding(axis.FlexStartEdge());
            }

            child.SetLayoutPosition(position, axis.FlexStartEdge());
        }

        private static void JustifyAbsoluteChild(
            Node parent,
            Node child,
            Direction direction,
            FlexDirection mainAxis,
            float containingBlockWidth)
        {
            Justify justify = parent.Style.Display == Display.Grid
                ? AlignHelper.ResolveChildJustification(parent, child)
                : parent.Style.JustifyContent;

            switch (justify)
            {
                case Justify.Start:
                case Justify.Auto:
                case Justify.Stretch:
                case Justify.FlexStart:
                case Justify.SpaceBetween:
                    SetFlexStartLayoutPosition(
                        parent, child, direction, mainAxis, containingBlockWidth);
                    break;
                case Justify.End:
                case Justify.FlexEnd:
                    SetFlexEndLayoutPosition(
                        parent, child, direction, mainAxis, containingBlockWidth);
                    break;
                case Justify.Center:
                case Justify.SpaceAround:
                case Justify.SpaceEvenly:
                    SetCenterLayoutPosition(
                        parent, child, direction, mainAxis, containingBlockWidth);
                    break;
            }
        }

        private static void AlignAbsoluteChild(
            Node parent,
            Node child,
            Direction direction,
            FlexDirection crossAxis,
            float containingBlockWidth)
        {
            Align itemAlign = AlignHelper.ResolveChildAlignment(parent, child);
            Wrap parentWrap = parent.Style.FlexWrap;
            if (parentWrap == Wrap.WrapReverse)
            {
                if (itemAlign == Align.FlexEnd)
                {
                    itemAlign = Align.FlexStart;
                }
                else if (itemAlign != Align.Center)
                {
                    itemAlign = Align.FlexEnd;
                }
            }

            switch (itemAlign)
            {
                case Align.Start:
                case Align.Auto:
                case Align.FlexStart:
                case Align.Baseline:
                case Align.SpaceAround:
                case Align.SpaceBetween:
                case Align.Stretch:
                case Align.SpaceEvenly:
                    SetFlexStartLayoutPosition(
                        parent, child, direction, crossAxis, containingBlockWidth);
                    break;
                case Align.End:
                case Align.FlexEnd:
                    SetFlexEndLayoutPosition(
                        parent, child, direction, crossAxis, containingBlockWidth);
                    break;
                case Align.Center:
                    SetCenterLayoutPosition(
                        parent, child, direction, crossAxis, containingBlockWidth);
                    break;
            }
        }

        /*
         * Absolutely positioned nodes do not participate in flex layout and thus their
         * positions can be determined independently from the rest of their siblings.
         * For each axis there are essentially two cases:
         *
         * 1) The node has insets defined. In this case we can just use these to
         *    determine the position of the node.
         * 2) The node does not have insets defined. In this case we look at the style
         *    of the parent to position the node. Things like justify content and
         *    align content will move absolute children around. If none of these
         *    special properties are defined, the child is positioned at the start
         *    (defined by flex direction) of the leading flex line.
         *
         * This function does that positioning for the given axis. The spec has more
         * information on this topic: https://www.w3.org/TR/css-flexbox-1/#abspos-items
         */
        private static void PositionAbsoluteChild(
            Node containingNode,
            Node parent,
            Node child,
            Direction direction,
            FlexDirection axis,
            bool isMainAxis,
            float containingBlockWidth,
            float containingBlockHeight)
        {
            bool isAxisRow = axis.IsRow();
            float containingBlockSize =
                isAxisRow ? containingBlockWidth : containingBlockHeight;

            if (child.Style.IsInlineStartPositionDefined(axis, direction) &&
                !child.Style.IsInlineStartPositionAuto(axis, direction))
            {
                float positionRelativeToInlineStart =
                    child.Style.ComputeInlineStartPosition(
                        axis, direction, containingBlockSize) +
                    containingNode.Style.ComputeInlineStartBorder(axis, direction) +
                    child.Style.ComputeInlineStartMargin(
                        axis, direction, containingBlockSize);
                float positionRelativeToFlexStart =
                    axis.InlineStartEdge(direction) != axis.FlexStartEdge()
                    ? TrailingPosition.GetPositionOfOppositeEdge(
                          positionRelativeToInlineStart, axis, containingNode, child)
                    : positionRelativeToInlineStart;

                child.SetLayoutPosition(positionRelativeToFlexStart, axis.FlexStartEdge());
            }
            else if (
                child.Style.IsInlineEndPositionDefined(axis, direction) &&
                !child.Style.IsInlineEndPositionAuto(axis, direction))
            {
                float positionRelativeToInlineStart =
                    containingNode.Layout.MeasuredDimension(axis.Dimension()) -
                    child.Layout.MeasuredDimension(axis.Dimension()) -
                    containingNode.Style.ComputeInlineEndBorder(axis, direction) -
                    child.Style.ComputeInlineEndMargin(
                        axis, direction, containingBlockSize) -
                    child.Style.ComputeInlineEndPosition(
                        axis, direction, containingBlockSize);
                float positionRelativeToFlexStart =
                    axis.InlineStartEdge(direction) != axis.FlexStartEdge()
                    ? TrailingPosition.GetPositionOfOppositeEdge(
                          positionRelativeToInlineStart, axis, containingNode, child)
                    : positionRelativeToInlineStart;

                child.SetLayoutPosition(positionRelativeToFlexStart, axis.FlexStartEdge());
            }
            else
            {
                if (isMainAxis)
                {
                    JustifyAbsoluteChild(
                        parent, child, direction, axis, containingBlockWidth);
                }
                else
                {
                    AlignAbsoluteChild(
                        parent, child, direction, axis, containingBlockWidth);
                }
            }
        }

        public static void LayoutAbsoluteChild(
            Node containingNode,
            Node node,
            Node child,
            float containingBlockWidth,
            float containingBlockHeight,
            SizingMode widthMode,
            Direction direction,
            ref LayoutData layoutMarkerData,
            uint depth,
            uint generationCount)
        {
            FlexDirection mainAxis = node.Style.Display == Display.Grid
                ? FlexDirection.Row.ResolveDirection(direction)
                : node.Style.FlexDirection.ResolveDirection(direction);
            FlexDirection crossAxis = node.Style.Display == Display.Grid
                ? FlexDirection.Column
                : mainAxis.ResolveCrossDirection(direction);
            bool isMainAxisRow = mainAxis.IsRow();

            float childWidth = YogaConstants.Undefined;
            float childHeight = YogaConstants.Undefined;
            SizingMode childWidthSizingMode = SizingMode.MaxContent;
            SizingMode childHeightSizingMode = SizingMode.MaxContent;

            float marginRow = child.Style.ComputeMarginForAxis(
                FlexDirection.Row, containingBlockWidth);
            float marginColumn = child.Style.ComputeMarginForAxis(
                FlexDirection.Column, containingBlockWidth);

            if (child.HasDefiniteLength(Dimension.Width, containingBlockWidth))
            {
                childWidth = child
                    .GetResolvedDimension(
                        direction,
                        Dimension.Width,
                        containingBlockWidth,
                        containingBlockWidth)
                    .Unwrap() +
                    marginRow;
            }
            else
            {
                if (child.Style.IsFlexStartPositionDefined(
                        FlexDirection.Row, direction) &&
                    child.Style.IsFlexEndPositionDefined(
                        FlexDirection.Row, direction) &&
                    !child.Style.IsFlexStartPositionAuto(
                        FlexDirection.Row, direction) &&
                    !child.Style.IsFlexEndPositionAuto(FlexDirection.Row, direction))
                {
                    childWidth =
                        containingNode.Layout.MeasuredDimension(Dimension.Width) -
                        (containingNode.Style.ComputeFlexStartBorder(
                             FlexDirection.Row, direction) +
                         containingNode.Style.ComputeFlexEndBorder(
                             FlexDirection.Row, direction)) -
                        (child.Style.ComputeFlexStartPosition(
                             FlexDirection.Row, direction, containingBlockWidth) +
                         child.Style.ComputeFlexEndPosition(
                             FlexDirection.Row, direction, containingBlockWidth));
                    childWidth = BoundAxis.ComputeBoundAxis(
                        child,
                        FlexDirection.Row,
                        direction,
                        childWidth,
                        containingBlockWidth,
                        containingBlockWidth);
                }
            }

            if (child.HasDefiniteLength(Dimension.Height, containingBlockHeight))
            {
                childHeight = child
                    .GetResolvedDimension(
                        direction,
                        Dimension.Height,
                        containingBlockHeight,
                        containingBlockWidth)
                    .Unwrap() +
                    marginColumn;
            }
            else
            {
                if (child.Style.IsFlexStartPositionDefined(
                        FlexDirection.Column, direction) &&
                    child.Style.IsFlexEndPositionDefined(
                        FlexDirection.Column, direction) &&
                    !child.Style.IsFlexStartPositionAuto(
                        FlexDirection.Column, direction) &&
                    !child.Style.IsFlexEndPositionAuto(
                        FlexDirection.Column, direction))
                {
                    childHeight =
                        containingNode.Layout.MeasuredDimension(Dimension.Height) -
                        (containingNode.Style.ComputeFlexStartBorder(
                             FlexDirection.Column, direction) +
                         containingNode.Style.ComputeFlexEndBorder(
                             FlexDirection.Column, direction)) -
                        (child.Style.ComputeFlexStartPosition(
                             FlexDirection.Column, direction, containingBlockHeight) +
                         child.Style.ComputeFlexEndPosition(
                             FlexDirection.Column, direction, containingBlockHeight));
                    childHeight = BoundAxis.ComputeBoundAxis(
                        child,
                        FlexDirection.Column,
                        direction,
                        childHeight,
                        containingBlockHeight,
                        containingBlockWidth);
                }
            }

            Style childStyle = child.Style;
            if (Comparison.IsUndefined(childWidth) ^ Comparison.IsUndefined(childHeight))
            {
                if (childStyle.AspectRatio.IsDefined())
                {
                    if (Comparison.IsUndefined(childWidth))
                    {
                        childWidth = marginRow +
                            (childHeight - marginColumn) * childStyle.AspectRatio.Unwrap();
                    }
                    else if (Comparison.IsUndefined(childHeight))
                    {
                        childHeight = marginColumn +
                            (childWidth - marginRow) / childStyle.AspectRatio.Unwrap();
                    }
                }
            }

            if (Comparison.IsUndefined(childWidth) || Comparison.IsUndefined(childHeight))
            {
                childWidthSizingMode = Comparison.IsUndefined(childWidth)
                    ? SizingMode.MaxContent
                    : SizingMode.StretchFit;
                childHeightSizingMode = Comparison.IsUndefined(childHeight)
                    ? SizingMode.MaxContent
                    : SizingMode.StretchFit;

                if (!isMainAxisRow && Comparison.IsUndefined(childWidth) &&
                    widthMode != SizingMode.MaxContent &&
                    Comparison.IsDefined(containingBlockWidth) && containingBlockWidth > 0)
                {
                    childWidth = containingBlockWidth;
                    childWidthSizingMode = SizingMode.FitContent;
                }

                LayoutAlgorithm.CalculateLayoutInternal(
                    child,
                    childWidth,
                    childHeight,
                    direction,
                    childWidthSizingMode,
                    childHeightSizingMode,
                    containingBlockWidth,
                    containingBlockHeight,
                    false,
                    LayoutPassReason.AbsMeasureChild,
                    ref layoutMarkerData,
                    depth,
                    generationCount);
                childWidth = child.Layout.MeasuredDimension(Dimension.Width) +
                    child.Style.ComputeMarginForAxis(
                        FlexDirection.Row, containingBlockWidth);
                childHeight = child.Layout.MeasuredDimension(Dimension.Height) +
                    child.Style.ComputeMarginForAxis(
                        FlexDirection.Column, containingBlockWidth);
            }

            LayoutAlgorithm.CalculateLayoutInternal(
                child,
                childWidth,
                childHeight,
                direction,
                SizingMode.StretchFit,
                SizingMode.StretchFit,
                containingBlockWidth,
                containingBlockHeight,
                true,
                LayoutPassReason.AbsLayout,
                ref layoutMarkerData,
                depth,
                generationCount);

            PositionAbsoluteChild(
                containingNode,
                node,
                child,
                direction,
                mainAxis,
                true /*isMainAxis*/,
                containingBlockWidth,
                containingBlockHeight);
            PositionAbsoluteChild(
                containingNode,
                node,
                child,
                direction,
                crossAxis,
                false /*isMainAxis*/,
                containingBlockWidth,
                containingBlockHeight);
        }

        public static bool LayoutAbsoluteDescendants(
            Node containingNode,
            Node currentNode,
            SizingMode widthSizingMode,
            Direction currentNodeDirection,
            ref LayoutData layoutMarkerData,
            uint currentDepth,
            uint generationCount,
            float currentNodeLeftOffsetFromContainingBlock,
            float currentNodeTopOffsetFromContainingBlock,
            float containingNodeAvailableInnerWidth,
            float containingNodeAvailableInnerHeight)
        {
            bool hasNewLayout = false;
            foreach (Node child in currentNode.GetLayoutChildren())
            {
                if (child.Style.Display == Display.None)
                {
                    continue;
                }
                else if (child.Style.PositionType == PositionType.Absolute)
                {
                    bool absoluteErrata =
                        currentNode.HasErrata(Errata.AbsolutePercentAgainstInnerSize);
                    float containingBlockWidth = absoluteErrata
                        ? containingNodeAvailableInnerWidth
                        : containingNode.Layout.MeasuredDimension(Dimension.Width) -
                            containingNode.Style.ComputeBorderForAxis(FlexDirection.Row);
                    float containingBlockHeight = absoluteErrata
                        ? containingNodeAvailableInnerHeight
                        : containingNode.Layout.MeasuredDimension(Dimension.Height) -
                            containingNode.Style.ComputeBorderForAxis(
                                FlexDirection.Column);

                    LayoutAbsoluteChild(
                        containingNode,
                        currentNode,
                        child,
                        containingBlockWidth,
                        containingBlockHeight,
                        widthSizingMode,
                        currentNodeDirection,
                        ref layoutMarkerData,
                        currentDepth,
                        generationCount);

                    hasNewLayout = hasNewLayout || child.HasNewLayout;

                    FlexDirection parentMainAxis = currentNode.Style.FlexDirection.ResolveDirection(
                        currentNodeDirection);
                    FlexDirection parentCrossAxis =
                        parentMainAxis.ResolveCrossDirection(currentNodeDirection);

                    if (TrailingPosition.NeedsTrailingPosition(parentMainAxis))
                    {
                        bool mainInsetsDefined = parentMainAxis.IsRow()
                            ? child.Style.HorizontalInsetsDefined()
                            : child.Style.VerticalInsetsDefined();
                        TrailingPosition.SetChildTrailingPosition(
                            mainInsetsDefined ? containingNode : currentNode,
                            child,
                            parentMainAxis);
                    }
                    if (TrailingPosition.NeedsTrailingPosition(parentCrossAxis))
                    {
                        bool crossInsetsDefined = parentCrossAxis.IsRow()
                            ? child.Style.HorizontalInsetsDefined()
                            : child.Style.VerticalInsetsDefined();
                        TrailingPosition.SetChildTrailingPosition(
                            crossInsetsDefined ? containingNode : currentNode,
                            child,
                            parentCrossAxis);
                    }

                    float childLeftPosition =
                        child.Layout.Position(PhysicalEdge.Left);
                    float childTopPosition =
                        child.Layout.Position(PhysicalEdge.Top);

                    float childLeftOffsetFromParent =
                        child.Style.HorizontalInsetsDefined()
                        ? (childLeftPosition - currentNodeLeftOffsetFromContainingBlock)
                        : childLeftPosition;
                    float childTopOffsetFromParent =
                        child.Style.VerticalInsetsDefined()
                        ? (childTopPosition - currentNodeTopOffsetFromContainingBlock)
                        : childTopPosition;

                    child.SetLayoutPosition(childLeftOffsetFromParent, PhysicalEdge.Left);
                    child.SetLayoutPosition(childTopOffsetFromParent, PhysicalEdge.Top);
                }
                else if (
                    child.Style.PositionType == PositionType.Static &&
                    !child.AlwaysFormsContainingBlock)
                {
                    child.CloneChildrenIfNeeded();
                    Direction childDirection =
                        child.ResolveDirection(currentNodeDirection);
                    float childLeftOffsetFromContainingBlock =
                        currentNodeLeftOffsetFromContainingBlock +
                        child.Layout.Position(PhysicalEdge.Left);
                    float childTopOffsetFromContainingBlock =
                        currentNodeTopOffsetFromContainingBlock +
                        child.Layout.Position(PhysicalEdge.Top);

                    hasNewLayout = LayoutAbsoluteDescendants(
                                       containingNode,
                                       child,
                                       widthSizingMode,
                                       childDirection,
                                       ref layoutMarkerData,
                                       currentDepth + 1,
                                       generationCount,
                                       childLeftOffsetFromContainingBlock,
                                       childTopOffsetFromContainingBlock,
                                       containingNodeAvailableInnerWidth,
                                       containingNodeAvailableInnerHeight) ||
                        hasNewLayout;

                    if (hasNewLayout)
                    {
                        child.HasNewLayout = hasNewLayout;
                    }
                }
            }
            return hasNewLayout;
        }
    }
}
