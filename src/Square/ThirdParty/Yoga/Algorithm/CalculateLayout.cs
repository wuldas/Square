// Copyright (c) Meta Platforms, Inc. and affiliates.
//
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Square.Rendering.Yoga
{
    internal static partial class LayoutAlgorithm
    {
        private static uint gCurrentGenerationCount = 0;

        public static void ConstrainMaxSizeForMode(
            Node node,
            Direction direction,
            FlexDirection axis,
            float ownerAxisSize,
            float ownerWidth,
            ref SizingMode mode,
            ref float size)
        {
            var maxSize = node.Style.ResolvedMaxDimension(
                    direction,
                    axis.Dimension(),
                    ownerAxisSize,
                    ownerWidth) +
                new FloatOptional(node.Style.ComputeMarginForAxis(axis, ownerWidth));

            switch (mode)
            {
                case SizingMode.StretchFit:
                case SizingMode.FitContent:
                    size = (!maxSize.IsDefined() || size < maxSize.Unwrap())
                        ? size
                        : maxSize.Unwrap();
                    break;
                case SizingMode.MaxContent:
                    if (maxSize.IsDefined())
                    {
                        mode = SizingMode.FitContent;
                        size = maxSize.Unwrap();
                    }
                    break;
            }
        }

        private static void ComputeFlexBasisForChild(
            Node node,
            Node child,
            float width,
            SizingMode widthMode,
            float height,
            float ownerWidth,
            float ownerHeight,
            SizingMode heightMode,
            Direction direction,
            ref LayoutData layoutMarkerData,
            uint depth,
            uint generationCount)
        {
            var mainAxis = node.Style.FlexDirection.ResolveDirection(direction);
            bool isMainAxisRow = mainAxis.IsRow();
            float mainAxisSize = isMainAxisRow ? width : height;
            float mainAxisOwnerSize = isMainAxisRow ? ownerWidth : ownerHeight;

            float childWidth = YogaConstants.Undefined;
            float childHeight = YogaConstants.Undefined;
            SizingMode childWidthSizingMode;
            SizingMode childHeightSizingMode;

            var resolvedFlexBasis = child.ResolveFlexBasis(
                direction, mainAxis, mainAxisOwnerSize, ownerWidth);
            bool isRowStyleDimDefined =
                child.HasDefiniteLength(Dimension.Width, ownerWidth);
            bool isColumnStyleDimDefined =
                child.HasDefiniteLength(Dimension.Height, ownerHeight);

            bool fixFlexBasisFitContent =
                node.Config.IsExperimentalFeatureEnabled(
                    ExperimentalFeature.FixFlexBasisFitContent);

            bool useResolvedFlexBasis =
                resolvedFlexBasis.IsDefined() && Comparison.IsDefined(mainAxisSize);

            if (fixFlexBasisFitContent && resolvedFlexBasis.IsDefined() &&
                resolvedFlexBasis.Unwrap() > 0)
            {
                useResolvedFlexBasis = true;
            }

            if (useResolvedFlexBasis)
            {
                if (child.Layout.ComputedFlexBasis.IsUndefined() ||
                    (child.Config.IsExperimentalFeatureEnabled(
                         ExperimentalFeature.WebFlexBasis) &&
                     child.Layout.ComputedFlexBasisGeneration != generationCount))
                {
                    var paddingAndBorder = new FloatOptional(
                        BoundAxis.PaddingAndBorderForAxis(child, mainAxis, direction, ownerWidth));
                    child.SetLayoutComputedFlexBasis(
                        FloatOptional.MaxOrDefined(resolvedFlexBasis, paddingAndBorder));
                }
            }
            else if (isMainAxisRow && isRowStyleDimDefined)
            {
                var paddingAndBorder = new FloatOptional(
                    BoundAxis.PaddingAndBorderForAxis(
                        child, FlexDirection.Row, direction, ownerWidth));
                child.SetLayoutComputedFlexBasis(
                    FloatOptional.MaxOrDefined(
                        child.GetResolvedDimension(
                            direction, Dimension.Width, ownerWidth, ownerWidth),
                        paddingAndBorder));
            }
            else if (!isMainAxisRow && isColumnStyleDimDefined)
            {
                var paddingAndBorder = new FloatOptional(
                    BoundAxis.PaddingAndBorderForAxis(
                        child, FlexDirection.Column, direction, ownerWidth));
                child.SetLayoutComputedFlexBasis(
                    FloatOptional.MaxOrDefined(
                        child.GetResolvedDimension(
                            direction, Dimension.Height, ownerHeight, ownerWidth),
                        paddingAndBorder));
            }
            else
            {
                childWidthSizingMode = SizingMode.MaxContent;
                childHeightSizingMode = SizingMode.MaxContent;

                float marginRow =
                    child.Style.ComputeMarginForAxis(FlexDirection.Row, ownerWidth);
                float marginColumn =
                    child.Style.ComputeMarginForAxis(FlexDirection.Column, ownerWidth);

                if (isRowStyleDimDefined)
                {
                    childWidth = child
                        .GetResolvedDimension(
                            direction, Dimension.Width, ownerWidth, ownerWidth)
                        .Unwrap() + marginRow;
                    childWidthSizingMode = SizingMode.StretchFit;
                }
                if (isColumnStyleDimDefined)
                {
                    childHeight = child
                            .GetResolvedDimension(
                                direction, Dimension.Height, ownerHeight, ownerWidth)
                        .Unwrap() + marginColumn;
                    childHeightSizingMode = SizingMode.StretchFit;
                }

                if ((!isMainAxisRow && node.Style.Overflow == Overflow.Scroll) ||
                    node.Style.Overflow != Overflow.Scroll)
                {
                    if (Comparison.IsUndefined(childWidth) && Comparison.IsDefined(width))
                    {
                        childWidth = width;
                        childWidthSizingMode = SizingMode.FitContent;
                    }
                }

                bool applyHeightFitContent =
                    isMainAxisRow || node.Style.Overflow != Overflow.Scroll;
                if (fixFlexBasisFitContent)
                {
                    applyHeightFitContent = isMainAxisRow ||
                        (child.HasMeasureFunc() &&
                         node.Style.Overflow != Overflow.Scroll);
                }
                if (applyHeightFitContent && Comparison.IsUndefined(childHeight) &&
                    Comparison.IsDefined(height))
                {
                    childHeight = height;
                    childHeightSizingMode = SizingMode.FitContent;
                }

                var childStyle = child.Style;
                if (childStyle.AspectRatio.IsDefined())
                {
                    if (!isMainAxisRow && childWidthSizingMode == SizingMode.StretchFit)
                    {
                        childHeight = marginColumn +
                            (childWidth - marginRow) / childStyle.AspectRatio.Unwrap();
                        childHeightSizingMode = SizingMode.StretchFit;
                    }
                    else if (isMainAxisRow && childHeightSizingMode == SizingMode.StretchFit)
                    {
                        childWidth = marginRow +
                            (childHeight - marginColumn) * childStyle.AspectRatio.Unwrap();
                        childWidthSizingMode = SizingMode.StretchFit;
                    }
                }

                bool hasExactWidth =
                    Comparison.IsDefined(width) && widthMode == SizingMode.StretchFit;
                bool childWidthStretch =
                    AlignHelper.ResolveChildAlignment(node, child) == Align.Stretch &&
                    childWidthSizingMode != SizingMode.StretchFit;
                if (!isMainAxisRow && !isRowStyleDimDefined && hasExactWidth &&
                    childWidthStretch)
                {
                    childWidth = width;
                    childWidthSizingMode = SizingMode.StretchFit;
                    if (childStyle.AspectRatio.IsDefined())
                    {
                        childHeight =
                            (childWidth - marginRow) / childStyle.AspectRatio.Unwrap();
                        childHeightSizingMode = SizingMode.StretchFit;
                    }
                }

                bool hasExactHeight =
                    Comparison.IsDefined(height) && heightMode == SizingMode.StretchFit;
                bool childHeightStretch =
                    AlignHelper.ResolveChildAlignment(node, child) == Align.Stretch &&
                    childHeightSizingMode != SizingMode.StretchFit;
                if (isMainAxisRow && !isColumnStyleDimDefined && hasExactHeight &&
                    childHeightStretch)
                {
                    childHeight = height;
                    childHeightSizingMode = SizingMode.StretchFit;
                    if (childStyle.AspectRatio.IsDefined())
                    {
                        childWidth =
                            (childHeight - marginColumn) * childStyle.AspectRatio.Unwrap();
                        childWidthSizingMode = SizingMode.StretchFit;
                    }
                }

                ConstrainMaxSizeForMode(
                    child, direction, FlexDirection.Row, ownerWidth, ownerWidth,
                    ref childWidthSizingMode, ref childWidth);
                ConstrainMaxSizeForMode(
                    child, direction, FlexDirection.Column, ownerHeight, ownerWidth,
                    ref childHeightSizingMode, ref childHeight);

                CalculateLayoutInternal(
                    child, childWidth, childHeight, direction,
                    childWidthSizingMode, childHeightSizingMode,
                    ownerWidth, ownerHeight, false,
                    LayoutPassReason.MeasureChild,
                    ref layoutMarkerData, depth, generationCount);

                child.SetLayoutComputedFlexBasis(new FloatOptional(
                    Comparison.MaxOrDefined(
                        child.Layout.MeasuredDimension(mainAxis.Dimension()),
                        BoundAxis.PaddingAndBorderForAxis(child, mainAxis, direction, ownerWidth))));
            }
            child.SetLayoutComputedFlexBasisGeneration(generationCount);
        }

        private static void MeasureNodeWithMeasureFunc(
            Node node, Direction direction,
            float availableWidth, float availableHeight,
            SizingMode widthSizingMode, SizingMode heightSizingMode,
            float ownerWidth, float ownerHeight,
            ref LayoutData layoutMarkerData, LayoutPassReason reason)
        {
            Debug.AssertFatal.AssertWithNode(
                node, node.HasMeasureFunc(),
                "Expected node to have custom measure function");

            if (widthSizingMode == SizingMode.MaxContent)
                availableWidth = YogaConstants.Undefined;
            if (heightSizingMode == SizingMode.MaxContent)
                availableHeight = YogaConstants.Undefined;

            var layout = node.Layout;
            float paddingAndBorderAxisRow = layout.Padding(PhysicalEdge.Left) +
                layout.Padding(PhysicalEdge.Right) + layout.Border(PhysicalEdge.Left) +
                layout.Border(PhysicalEdge.Right);
            float paddingAndBorderAxisColumn = layout.Padding(PhysicalEdge.Top) +
                layout.Padding(PhysicalEdge.Bottom) + layout.Border(PhysicalEdge.Top) +
                layout.Border(PhysicalEdge.Bottom);

            float innerWidth = Comparison.IsUndefined(availableWidth)
                ? availableWidth
                : Comparison.MaxOrDefined(0.0f, availableWidth - paddingAndBorderAxisRow);
            float innerHeight = Comparison.IsUndefined(availableHeight)
                ? availableHeight
                : Comparison.MaxOrDefined(0.0f, availableHeight - paddingAndBorderAxisColumn);

            if (widthSizingMode == SizingMode.StretchFit &&
                heightSizingMode == SizingMode.StretchFit)
            {
                node.SetLayoutMeasuredDimension(
                    BoundAxis.ComputeBoundAxis(node, FlexDirection.Row, direction,
                        availableWidth, ownerWidth, ownerWidth), Dimension.Width);
                node.SetLayoutMeasuredDimension(
                    BoundAxis.ComputeBoundAxis(node, FlexDirection.Column, direction,
                        availableHeight, ownerHeight, ownerWidth), Dimension.Height);
            }
            else
            {
                Event.Publish(node, EventType.MeasureCallbackStart);

                var measuredSize = node.Measure(
                    innerWidth, widthSizingMode.ToMeasureMode(),
                    innerHeight, heightSizingMode.ToMeasureMode());

                if (Event.HasSubscribers)
                {
                    Event.Publish(node, EventType.MeasureCallbackEnd,
                        new Event.MeasureCallbackEndData
                        {
                            Width = innerWidth,
                            WidthMeasureMode = widthSizingMode.ToMeasureMode(),
                            Height = innerHeight,
                            HeightMeasureMode = heightSizingMode.ToMeasureMode(),
                            MeasuredWidth = measuredSize.Width,
                            MeasuredHeight = measuredSize.Height,
                            Reason = reason,
                        });
                }

                layoutMarkerData.MeasureCallbacks += 1;
                layoutMarkerData.MeasureCallbackReasonsCount[(int)reason] += 1;

                node.SetLayoutMeasuredDimension(
                    BoundAxis.ComputeBoundAxis(node, FlexDirection.Row, direction,
                        (widthSizingMode == SizingMode.MaxContent ||
                         widthSizingMode == SizingMode.FitContent)
                            ? measuredSize.Width + paddingAndBorderAxisRow
                            : availableWidth,
                        ownerWidth, ownerWidth), Dimension.Width);

                node.SetLayoutMeasuredDimension(
                    BoundAxis.ComputeBoundAxis(node, FlexDirection.Column, direction,
                        (heightSizingMode == SizingMode.MaxContent ||
                         heightSizingMode == SizingMode.FitContent)
                            ? measuredSize.Height + paddingAndBorderAxisColumn
                            : availableHeight,
                        ownerHeight, ownerWidth), Dimension.Height);
            }
        }

        private static void MeasureNodeWithoutChildren(
            Node node, Direction direction,
            float availableWidth, float availableHeight,
            SizingMode widthSizingMode, SizingMode heightSizingMode,
            float ownerWidth, float ownerHeight)
        {
            var layout = node.Layout;
            float width = availableWidth;
            if (widthSizingMode == SizingMode.MaxContent ||
                widthSizingMode == SizingMode.FitContent)
            {
                width = layout.Padding(PhysicalEdge.Left) +
                    layout.Padding(PhysicalEdge.Right) +
                    layout.Border(PhysicalEdge.Left) + layout.Border(PhysicalEdge.Right);
            }
            node.SetLayoutMeasuredDimension(
                BoundAxis.ComputeBoundAxis(
                    node, FlexDirection.Row, direction, width, ownerWidth, ownerWidth),
                Dimension.Width);

            float height = availableHeight;
            if (heightSizingMode == SizingMode.MaxContent ||
                heightSizingMode == SizingMode.FitContent)
            {
                height = layout.Padding(PhysicalEdge.Top) +
                    layout.Padding(PhysicalEdge.Bottom) +
                    layout.Border(PhysicalEdge.Top) + layout.Border(PhysicalEdge.Bottom);
            }
            node.SetLayoutMeasuredDimension(
                BoundAxis.ComputeBoundAxis(
                    node, FlexDirection.Column, direction, height, ownerHeight, ownerWidth),
                Dimension.Height);
        }

        internal static bool IsFixedSize(float dim, SizingMode sizingMode)
        {
            return sizingMode == SizingMode.StretchFit ||
                (Comparison.IsDefined(dim) && sizingMode == SizingMode.FitContent && dim <= 0.0);
        }

        private static bool MeasureNodeWithFixedSize(
            Node node, Direction direction,
            float availableWidth, float availableHeight,
            SizingMode widthSizingMode, SizingMode heightSizingMode,
            float ownerWidth, float ownerHeight)
        {
            if (IsFixedSize(availableWidth, widthSizingMode) &&
                IsFixedSize(availableHeight, heightSizingMode))
            {
                node.SetLayoutMeasuredDimension(
                    BoundAxis.ComputeBoundAxis(node, FlexDirection.Row, direction,
                        Comparison.IsUndefined(availableWidth) ||
                                (widthSizingMode == SizingMode.FitContent && availableWidth < 0.0f)
                            ? 0.0f : availableWidth,
                        ownerWidth, ownerWidth), Dimension.Width);

                node.SetLayoutMeasuredDimension(
                    BoundAxis.ComputeBoundAxis(node, FlexDirection.Column, direction,
                        Comparison.IsUndefined(availableHeight) ||
                                (heightSizingMode == SizingMode.FitContent && availableHeight < 0.0f)
                            ? 0.0f : availableHeight,
                        ownerHeight, ownerWidth), Dimension.Height);
                return true;
            }
            return false;
        }

        public static void ZeroOutLayoutRecursively(Node node)
        {
            node.Layout = new LayoutResults();
            node.SetLayoutDimension(0, Dimension.Width);
            node.SetLayoutDimension(0, Dimension.Height);
            node.SetHasNewLayout(true);
            node.CloneChildrenIfNeeded();
            foreach (var child in node.GetChildren())
                ZeroOutLayoutRecursively(child);
        }

        public static void CleanupContentsNodesRecursively(Node node)
        {
            if (node.HasContentsChildren())
            {
                node.CloneContentsChildrenIfNeeded();
                foreach (var child in node.GetChildren())
                {
                    if (child.Style.Display == Display.Contents)
                    {
                        child.Layout = new LayoutResults();
                        child.SetLayoutDimension(0, Dimension.Width);
                        child.SetLayoutDimension(0, Dimension.Height);
                        child.SetHasNewLayout(true);
                        child.SetDirty(false);
                        child.CloneChildrenIfNeeded();
                        CleanupContentsNodesRecursively(child);
                    }
                }
            }
        }

        public static float CalculateAvailableInnerDimension(
            Node node, Direction direction, Dimension dimension,
            float availableDim, float paddingAndBorder,
            float ownerDim, float ownerWidth)
        {
            float availableInnerDim = availableDim - paddingAndBorder;
            if (Comparison.IsDefined(availableInnerDim))
            {
                var minDimensionOptional =
                    node.Style.ResolvedMinDimension(direction, dimension, ownerDim, ownerWidth);
                float minInnerDim = minDimensionOptional.IsUndefined()
                    ? 0.0f : minDimensionOptional.Unwrap() - paddingAndBorder;

                var maxDimensionOptional =
                    node.Style.ResolvedMaxDimension(direction, dimension, ownerDim, ownerWidth);
                float maxInnerDim = maxDimensionOptional.IsUndefined()
                    ? float.MaxValue : maxDimensionOptional.Unwrap() - paddingAndBorder;
                availableInnerDim = Comparison.MaxOrDefined(
                    Comparison.MinOrDefined(availableInnerDim, maxInnerDim), minInnerDim);
            }
            return availableInnerDim;
        }

        private static float ComputeFlexBasisForChildren(
            Node node, float availableInnerWidth, float availableInnerHeight,
            float ownerWidth, float ownerHeight,
            SizingMode widthSizingMode, SizingMode heightSizingMode,
            Direction direction, FlexDirection mainAxis, bool performLayout,
            ref LayoutData layoutMarkerData, uint depth, uint generationCount)
        {
            float totalOuterFlexBasis = 0.0f;
            Node? singleFlexChild = null;
            var children = node.GetLayoutChildren();
            SizingMode sizingModeMainDim =
                mainAxis.IsRow() ? widthSizingMode : heightSizingMode;
            if (sizingModeMainDim == SizingMode.StretchFit)
            {
                foreach (var child in children)
                {
                    if (child.IsNodeFlexible())
                    {
                        if (singleFlexChild != null ||
                            Comparison.InexactEquals(child.ResolveFlexGrow(), 0.0f) ||
                            Comparison.InexactEquals(child.ResolveFlexShrink(), 0.0f))
                        {
                            singleFlexChild = null;
                            break;
                        }
                        else
                        {
                            singleFlexChild = child;
                        }
                    }
                }
            }

            foreach (var child in children)
            {
                child.ProcessDimensions();
                if (child.Style.Display == Display.None)
                {
                    ZeroOutLayoutRecursively(child);
                    child.SetHasNewLayout(true);
                    child.SetDirty(false);
                    continue;
                }
                if (performLayout)
                {
                    Direction childDirection = child.ResolveDirection(direction);
                    child.SetPosition(childDirection, availableInnerWidth, availableInnerHeight);
                }
                if (child.Style.PositionType == PositionType.Absolute)
                    continue;
                if (child == singleFlexChild)
                {
                    child.SetLayoutComputedFlexBasisGeneration(generationCount);
                    child.SetLayoutComputedFlexBasis(new FloatOptional(0));
                }
                else
                {
                    ComputeFlexBasisForChild(
                        node, child, availableInnerWidth, widthSizingMode,
                        availableInnerHeight, ownerWidth, ownerHeight,
                        heightSizingMode, direction,
                        ref layoutMarkerData, depth, generationCount);
                }
                totalOuterFlexBasis +=
                    (child.Layout.ComputedFlexBasis.Unwrap() +
                     child.Style.ComputeMarginForAxis(mainAxis, availableInnerWidth));
            }
            return totalOuterFlexBasis;
        }

        private static float DistributeFreeSpaceSecondPass(
            FlexLine flexLine, Node node,
            FlexDirection mainAxis, FlexDirection crossAxis, Direction direction,
            float ownerWidth, float mainAxisOwnerSize,
            float availableInnerMainDim, float availableInnerCrossDim,
            float availableInnerWidth, float availableInnerHeight,
            bool mainAxisOverflows, SizingMode sizingModeCrossDim,
            bool performLayout, ref LayoutData layoutMarkerData,
            uint depth, uint generationCount)
        {
            float childFlexBasis = 0;
            float flexShrinkScaledFactor = 0;
            float flexGrowFactor = 0;
            float deltaFreeSpace = 0;
            bool isMainAxisRow = mainAxis.IsRow();
            bool isNodeFlexWrap = node.Style.FlexWrap != Wrap.NoWrap;

            foreach (var currentLineChild in flexLine.ItemsInFlow)
            {
                childFlexBasis = BoundAxis.BoundAxisWithinMinAndMax(
                    currentLineChild, direction, mainAxis,
                         currentLineChild.Layout.ComputedFlexBasis,
                    mainAxisOwnerSize, ownerWidth).Unwrap();
                float updatedMainSize = childFlexBasis;

                if (Comparison.IsDefined(flexLine.Layout.RemainingFreeSpace) &&
                    flexLine.Layout.RemainingFreeSpace < 0)
                {
                    flexShrinkScaledFactor =
                        -currentLineChild.ResolveFlexShrink() * childFlexBasis;
                    if (flexShrinkScaledFactor != 0)
                    {
                        float childSize = YogaConstants.Undefined;
                        if (Comparison.IsDefined(flexLine.Layout.TotalFlexShrinkScaledFactors) &&
                            flexLine.Layout.TotalFlexShrinkScaledFactors == 0)
                        {
                            childSize = childFlexBasis + flexShrinkScaledFactor;
                        }
                        else
                        {
                            childSize = childFlexBasis +
                                (flexLine.Layout.RemainingFreeSpace /
                                 flexLine.Layout.TotalFlexShrinkScaledFactors) *
                                    flexShrinkScaledFactor;
                        }
                        updatedMainSize = BoundAxis.ComputeBoundAxis(
                            currentLineChild, mainAxis, direction,
                            childSize, availableInnerMainDim, availableInnerWidth);
                    }
                }
                else if (Comparison.IsDefined(flexLine.Layout.RemainingFreeSpace) &&
                    flexLine.Layout.RemainingFreeSpace > 0)
                {
                    flexGrowFactor = currentLineChild.ResolveFlexGrow();
                    if (!float.IsNaN(flexGrowFactor) && flexGrowFactor != 0)
                    {
                        updatedMainSize = BoundAxis.ComputeBoundAxis(
                            currentLineChild, mainAxis, direction,
                            childFlexBasis + flexLine.Layout.RemainingFreeSpace /
                                    flexLine.Layout.TotalFlexGrowFactors * flexGrowFactor,
                            availableInnerMainDim, availableInnerWidth);
                    }
                }

                deltaFreeSpace += updatedMainSize - childFlexBasis;

                float marginMain = currentLineChild.Style.ComputeMarginForAxis(mainAxis, availableInnerWidth);
                float marginCross = currentLineChild.Style.ComputeMarginForAxis(crossAxis, availableInnerWidth);

                float childCrossSize = YogaConstants.Undefined;
                float childMainSize = updatedMainSize + marginMain;
                SizingMode childCrossSizingMode;
                SizingMode childMainSizingMode = SizingMode.StretchFit;

                var childStyle = currentLineChild.Style;
                if (childStyle.AspectRatio.IsDefined())
                {
                    childCrossSize = isMainAxisRow
                        ? (childMainSize - marginMain) / childStyle.AspectRatio.Unwrap()
                        : (childMainSize - marginMain) * childStyle.AspectRatio.Unwrap();
                    childCrossSizingMode = SizingMode.StretchFit;
                    childCrossSize += marginCross;
                }
                else if (!float.IsNaN(availableInnerCrossDim) &&
                    !currentLineChild.HasDefiniteLength(crossAxis.Dimension(), availableInnerCrossDim) &&
                    sizingModeCrossDim == SizingMode.StretchFit &&
                    !(isNodeFlexWrap && mainAxisOverflows) &&
                    AlignHelper.ResolveChildAlignment(node, currentLineChild) == Align.Stretch &&
                    !currentLineChild.Style.FlexStartMarginIsAuto(crossAxis, direction) &&
                    !currentLineChild.Style.FlexEndMarginIsAuto(crossAxis, direction))
                {
                    childCrossSize = availableInnerCrossDim;
                    childCrossSizingMode = SizingMode.StretchFit;
                }
                else if (!currentLineChild.HasDefiniteLength(crossAxis.Dimension(), availableInnerCrossDim))
                {
                    childCrossSize = availableInnerCrossDim;
                    childCrossSizingMode = Comparison.IsUndefined(childCrossSize)
                        ? SizingMode.MaxContent : SizingMode.FitContent;
                }
                else
                {
                    childCrossSize = currentLineChild
                        .GetResolvedDimension(direction, crossAxis.Dimension(),
                            availableInnerCrossDim, availableInnerWidth)
                        .Unwrap() + marginCross;
                    bool isLoosePercentageMeasurement =
                        currentLineChild.ProcessedDimension(crossAxis.Dimension()).IsPercent() &&
                        sizingModeCrossDim != SizingMode.StretchFit;
                    childCrossSizingMode =
                        Comparison.IsUndefined(childCrossSize) || isLoosePercentageMeasurement
                        ? SizingMode.MaxContent : SizingMode.StretchFit;
                }

                ConstrainMaxSizeForMode(currentLineChild, direction, mainAxis,
                    availableInnerMainDim, availableInnerWidth,
                    ref childMainSizingMode, ref childMainSize);
                ConstrainMaxSizeForMode(currentLineChild, direction, crossAxis,
                    availableInnerCrossDim, availableInnerWidth,
                    ref childCrossSizingMode, ref childCrossSize);

                bool requiresStretchLayout =
                    !currentLineChild.HasDefiniteLength(crossAxis.Dimension(), availableInnerCrossDim) &&
                    AlignHelper.ResolveChildAlignment(node, currentLineChild) == Align.Stretch &&
                    !currentLineChild.Style.FlexStartMarginIsAuto(crossAxis, direction) &&
                    !currentLineChild.Style.FlexEndMarginIsAuto(crossAxis, direction);

                float childWidth = isMainAxisRow ? childMainSize : childCrossSize;
                float childHeight = !isMainAxisRow ? childMainSize : childCrossSize;
                SizingMode childWidthSizingMode =
                    isMainAxisRow ? childMainSizingMode : childCrossSizingMode;
                SizingMode childHeightSizingMode =
                    !isMainAxisRow ? childMainSizingMode : childCrossSizingMode;

                bool isLayoutPass = performLayout && !requiresStretchLayout;
                CalculateLayoutInternal(
                    currentLineChild, childWidth, childHeight,
                    node.Layout.GetDirection(),
                    childWidthSizingMode, childHeightSizingMode,
                    availableInnerWidth, availableInnerHeight,
                    isLayoutPass,
                    isLayoutPass ? LayoutPassReason.FlexLayout : LayoutPassReason.FlexMeasure,
                    ref layoutMarkerData, depth, generationCount);
                node.SetLayoutHadOverflow(
                    node.Layout.HadOverflow() || currentLineChild.Layout.HadOverflow());
            }
            return deltaFreeSpace;
        }

        private static void DistributeFreeSpaceFirstPass(
            FlexLine flexLine, Direction direction, FlexDirection mainAxis,
            float ownerWidth, float mainAxisOwnerSize,
            float availableInnerMainDim, float availableInnerWidth)
        {
            float flexShrinkScaledFactor = 0;
            float flexGrowFactor = 0;
            float baseMainSize = 0;
            float boundMainSize = 0;
            float deltaFreeSpace = 0;

            foreach (var currentLineChild in flexLine.ItemsInFlow)
            {
                float childFlexBasis = BoundAxis.BoundAxisWithinMinAndMax(
                    currentLineChild, direction, mainAxis,
                               currentLineChild.Layout.ComputedFlexBasis,
                    mainAxisOwnerSize, ownerWidth).Unwrap();

                if (flexLine.Layout.RemainingFreeSpace < 0)
                {
                    flexShrinkScaledFactor =
                        -currentLineChild.ResolveFlexShrink() * childFlexBasis;
                    if (Comparison.IsDefined(flexShrinkScaledFactor) && flexShrinkScaledFactor != 0)
                    {
                        baseMainSize = childFlexBasis +
                            flexLine.Layout.RemainingFreeSpace /
                                flexLine.Layout.TotalFlexShrinkScaledFactors * flexShrinkScaledFactor;
                        boundMainSize = BoundAxis.ComputeBoundAxis(
                            currentLineChild, mainAxis, direction,
                            baseMainSize, availableInnerMainDim, availableInnerWidth);
                        if (Comparison.IsDefined(baseMainSize) && Comparison.IsDefined(boundMainSize) &&
                            baseMainSize != boundMainSize)
                        {
                            deltaFreeSpace += boundMainSize - childFlexBasis;
                            flexLine.Layout.TotalFlexShrinkScaledFactors -=
                                (-currentLineChild.ResolveFlexShrink() *
                                 currentLineChild.Layout.ComputedFlexBasis.Unwrap());
                        }
                    }
                }
                else if (Comparison.IsDefined(flexLine.Layout.RemainingFreeSpace) &&
                    flexLine.Layout.RemainingFreeSpace > 0)
                {
                    flexGrowFactor = currentLineChild.ResolveFlexGrow();
                    if (Comparison.IsDefined(flexGrowFactor) && flexGrowFactor != 0)
                    {
                        baseMainSize = childFlexBasis +
                            flexLine.Layout.RemainingFreeSpace /
                                flexLine.Layout.TotalFlexGrowFactors * flexGrowFactor;
                        boundMainSize = BoundAxis.ComputeBoundAxis(
                            currentLineChild, mainAxis, direction,
                            baseMainSize, availableInnerMainDim, availableInnerWidth);
                        if (Comparison.IsDefined(baseMainSize) && Comparison.IsDefined(boundMainSize) &&
                            baseMainSize != boundMainSize)
                        {
                            deltaFreeSpace += boundMainSize - childFlexBasis;
                            flexLine.Layout.TotalFlexGrowFactors -= flexGrowFactor;
                        }
                    }
                }
            }
            flexLine.Layout.RemainingFreeSpace -= deltaFreeSpace;
        }

        private static void ResolveFlexibleLength(
            Node node, FlexLine flexLine,
            FlexDirection mainAxis, FlexDirection crossAxis, Direction direction,
            float ownerWidth, float mainAxisOwnerSize,
            float availableInnerMainDim, float availableInnerCrossDim,
            float availableInnerWidth, float availableInnerHeight,
            bool mainAxisOverflows, SizingMode sizingModeCrossDim,
            bool performLayout, ref LayoutData layoutMarkerData,
            uint depth, uint generationCount)
        {
            float originalFreeSpace = flexLine.Layout.RemainingFreeSpace;
            DistributeFreeSpaceFirstPass(
                flexLine, direction, mainAxis, ownerWidth,
                mainAxisOwnerSize, availableInnerMainDim, availableInnerWidth);

            float distributedFreeSpace = DistributeFreeSpaceSecondPass(
                flexLine, node, mainAxis, crossAxis, direction,
                ownerWidth, mainAxisOwnerSize,
                availableInnerMainDim, availableInnerCrossDim,
                availableInnerWidth, availableInnerHeight,
                mainAxisOverflows, sizingModeCrossDim, performLayout,
                ref layoutMarkerData, depth, generationCount);

            flexLine.Layout.RemainingFreeSpace = originalFreeSpace - distributedFreeSpace;
        }

        private static void JustifyMainAxis(
            Node node, FlexLine flexLine,
            FlexDirection mainAxis, FlexDirection crossAxis, Direction direction,
            SizingMode sizingModeMainDim, SizingMode sizingModeCrossDim,
            float mainAxisOwnerSize, float ownerWidth,
            float availableInnerMainDim, float availableInnerCrossDim,
            float availableInnerWidth, bool performLayout)
        {
            var style = node.Style;
            float leadingPaddingAndBorderMain =
                node.Style.ComputeFlexStartPaddingAndBorder(mainAxis, direction, ownerWidth);
            float trailingPaddingAndBorderMain =
                node.Style.ComputeFlexEndPaddingAndBorder(mainAxis, direction, ownerWidth);
            float gap = node.Style.ComputeGapForAxis(mainAxis, availableInnerMainDim);

            if (sizingModeMainDim == SizingMode.FitContent &&
                flexLine.Layout.RemainingFreeSpace > 0)
            {
                if (style.MinDimension(mainAxis.Dimension()).IsDefined() &&
                    style.ResolvedMinDimension(
                        direction, mainAxis.Dimension(), mainAxisOwnerSize, ownerWidth).IsDefined())
                {
                    float minAvailableMainDim =
                        style.ResolvedMinDimension(
                                direction, mainAxis.Dimension(), mainAxisOwnerSize, ownerWidth)
                            .Unwrap() - leadingPaddingAndBorderMain - trailingPaddingAndBorderMain;
                    float occupiedSpaceByChildNodes =
                        availableInnerMainDim - flexLine.Layout.RemainingFreeSpace;
                    flexLine.Layout.RemainingFreeSpace = Comparison.MaxOrDefined(
                        0.0f, minAvailableMainDim - occupiedSpaceByChildNodes);
                }
                else
                {
                    flexLine.Layout.RemainingFreeSpace = 0;
                }
            }

            float leadingMainDim = 0;
            float betweenMainDim = gap;
            Justify justifyContent = flexLine.Layout.RemainingFreeSpace >= 0
                ? node.Style.JustifyContent
                : AlignHelper.FallbackAlignment(node.Style.JustifyContent);

            if (flexLine.NumberOfAutoMargins == 0)
            {
                switch (justifyContent)
                {
                    case Justify.Start:
                    case Justify.End:
                    case Justify.Auto:
                    case Justify.Stretch:
                    case Justify.FlexStart:
                        break;
                    case Justify.Center:
                        leadingMainDim = flexLine.Layout.RemainingFreeSpace / 2;
                        break;
                    case Justify.FlexEnd:
                        leadingMainDim = flexLine.Layout.RemainingFreeSpace;
                        break;
                    case Justify.SpaceBetween:
                        if (flexLine.ItemsInFlow.Count > 1)
                            betweenMainDim += flexLine.Layout.RemainingFreeSpace /
                                (float)(flexLine.ItemsInFlow.Count - 1);
                        break;
                    case Justify.SpaceEvenly:
                        leadingMainDim = flexLine.Layout.RemainingFreeSpace /
                            (float)(flexLine.ItemsInFlow.Count + 1);
                        betweenMainDim += leadingMainDim;
                        break;
                    case Justify.SpaceAround:
                        leadingMainDim = 0.5f * flexLine.Layout.RemainingFreeSpace /
                            (float)(flexLine.ItemsInFlow.Count);
                        betweenMainDim += leadingMainDim * 2;
                        break;
                }
            }

            flexLine.Layout.MainDim = leadingPaddingAndBorderMain + leadingMainDim;
            flexLine.Layout.CrossDim = 0;

            float maxAscentForCurrentLine = 0;
            float maxDescentForCurrentLine = 0;
            bool isNodeBaselineLayout = Baseline.IsBaselineLayout(node);
            for (int i = 0; i < flexLine.ItemsInFlow.Count; i++)
            {
                var child = flexLine.ItemsInFlow[i];
                var childLayout = child.Layout;
                if (child.Style.FlexStartMarginIsAuto(mainAxis, direction) &&
                    flexLine.Layout.RemainingFreeSpace > 0.0f)
                {
                    flexLine.Layout.MainDim += flexLine.Layout.RemainingFreeSpace /
                        (float)(flexLine.NumberOfAutoMargins);
                }

                if (performLayout)
                {
                    child.SetLayoutPosition(
                        childLayout.Position(mainAxis.FlexStartEdge()) + flexLine.Layout.MainDim,
                        mainAxis.FlexStartEdge());
                }

                if (i != flexLine.ItemsInFlow.Count - 1)
                    flexLine.Layout.MainDim += betweenMainDim;

                if (child.Style.FlexEndMarginIsAuto(mainAxis, direction) &&
                    flexLine.Layout.RemainingFreeSpace > 0.0f)
                {
                    flexLine.Layout.MainDim += flexLine.Layout.RemainingFreeSpace /
                        (float)(flexLine.NumberOfAutoMargins);
                }
                bool canSkipFlex = !performLayout && sizingModeCrossDim == SizingMode.StretchFit;
                if (canSkipFlex)
                {
                    flexLine.Layout.MainDim +=
                        child.Style.ComputeMarginForAxis(mainAxis, availableInnerWidth) +
                        BoundAxis.BoundAxisWithinMinAndMax(child, direction, mainAxis,
                            childLayout.ComputedFlexBasis, mainAxisOwnerSize, ownerWidth).Unwrap();
                    flexLine.Layout.CrossDim = availableInnerCrossDim;
                }
                else
                {
                    flexLine.Layout.MainDim +=
                        child.DimensionWithMargin(mainAxis, availableInnerWidth);

                    if (isNodeBaselineLayout)
                    {
                        float ascent = Baseline.CalculateBaseline(child) +
                            child.Style.ComputeFlexStartMargin(
                                FlexDirection.Column, direction, availableInnerWidth);
                        float descent =
                            child.Layout.MeasuredDimension(Dimension.Height) +
                            child.Style.ComputeMarginForAxis(FlexDirection.Column, availableInnerWidth) -
                            ascent;
                        maxAscentForCurrentLine = Comparison.MaxOrDefined(maxAscentForCurrentLine, ascent);
                        maxDescentForCurrentLine = Comparison.MaxOrDefined(maxDescentForCurrentLine, descent);
                    }
                    else
                    {
                        flexLine.Layout.CrossDim = Comparison.MaxOrDefined(
                            flexLine.Layout.CrossDim,
                            child.DimensionWithMargin(crossAxis, availableInnerWidth));
                    }
                }
            }
            flexLine.Layout.MainDim += trailingPaddingAndBorderMain;
            if (isNodeBaselineLayout)
                flexLine.Layout.CrossDim = maxAscentForCurrentLine + maxDescentForCurrentLine;
        }

        private static void CalculateLayoutImpl(
            Node node, float availableWidth, float availableHeight,
            Direction ownerDirection,
            SizingMode widthSizingMode, SizingMode heightSizingMode,
            float ownerWidth, float ownerHeight,
            bool performLayout, LayoutPassReason reason,
            ref LayoutData layoutMarkerData, uint depth, uint generationCount)
        {
            Debug.AssertFatal.AssertWithNode(node,
                Comparison.IsUndefined(availableWidth) ? widthSizingMode == SizingMode.MaxContent : true,
                "availableWidth is indefinite so widthSizingMode must be MaxContent");
            Debug.AssertFatal.AssertWithNode(node,
                Comparison.IsUndefined(availableHeight) ? heightSizingMode == SizingMode.MaxContent : true,
                "availableHeight is indefinite so heightSizingMode must be MaxContent");

            if (performLayout) layoutMarkerData.Layouts += 1;
            else layoutMarkerData.Measures += 1;

            Direction direction = node.ResolveDirection(ownerDirection);
            node.SetLayoutDirection(direction);

            FlexDirection flexRowDirection = FlexDirection.Row.ResolveDirection(direction);
            FlexDirection flexColumnDirection = FlexDirection.Column.ResolveDirection(direction);

            PhysicalEdge startEdge = direction == Direction.LTR ? PhysicalEdge.Left : PhysicalEdge.Right;
            PhysicalEdge endEdge = direction == Direction.LTR ? PhysicalEdge.Right : PhysicalEdge.Left;

            float marginRowLeading = node.Style.ComputeInlineStartMargin(flexRowDirection, direction, ownerWidth);
            node.SetLayoutMargin(marginRowLeading, startEdge);
            float marginRowTrailing = node.Style.ComputeInlineEndMargin(flexRowDirection, direction, ownerWidth);
            node.SetLayoutMargin(marginRowTrailing, endEdge);
            float marginColumnLeading = node.Style.ComputeInlineStartMargin(flexColumnDirection, direction, ownerWidth);
            node.SetLayoutMargin(marginColumnLeading, PhysicalEdge.Top);
            float marginColumnTrailing = node.Style.ComputeInlineEndMargin(flexColumnDirection, direction, ownerWidth);
            node.SetLayoutMargin(marginColumnTrailing, PhysicalEdge.Bottom);

            float marginAxisRow = marginRowLeading + marginRowTrailing;
            float marginAxisColumn = marginColumnLeading + marginColumnTrailing;

            node.SetLayoutBorder(node.Style.ComputeInlineStartBorder(flexRowDirection, direction), startEdge);
            node.SetLayoutBorder(node.Style.ComputeInlineEndBorder(flexRowDirection, direction), endEdge);
            node.SetLayoutBorder(node.Style.ComputeInlineStartBorder(flexColumnDirection, direction), PhysicalEdge.Top);
            node.SetLayoutBorder(node.Style.ComputeInlineEndBorder(flexColumnDirection, direction), PhysicalEdge.Bottom);

            node.SetLayoutPadding(node.Style.ComputeInlineStartPadding(flexRowDirection, direction, ownerWidth), startEdge);
            node.SetLayoutPadding(node.Style.ComputeInlineEndPadding(flexRowDirection, direction, ownerWidth), endEdge);
            node.SetLayoutPadding(node.Style.ComputeInlineStartPadding(flexColumnDirection, direction, ownerWidth), PhysicalEdge.Top);
            node.SetLayoutPadding(node.Style.ComputeInlineEndPadding(flexColumnDirection, direction, ownerWidth), PhysicalEdge.Bottom);

            if (node.HasMeasureFunc())
            {
                MeasureNodeWithMeasureFunc(node, direction,
                    availableWidth - marginAxisRow, availableHeight - marginAxisColumn,
                    widthSizingMode, heightSizingMode, ownerWidth, ownerHeight,
                    ref layoutMarkerData, reason);
                CleanupContentsNodesRecursively(node);
                return;
            }

            var childCount = node.GetLayoutChildCount();
            if (childCount == 0)
            {
                MeasureNodeWithoutChildren(node, direction,
                    availableWidth - marginAxisRow, availableHeight - marginAxisColumn,
                    widthSizingMode, heightSizingMode, ownerWidth, ownerHeight);
                CleanupContentsNodesRecursively(node);
                return;
            }

            if (!performLayout && MeasureNodeWithFixedSize(node, direction,
                    availableWidth - marginAxisRow, availableHeight - marginAxisColumn,
                    widthSizingMode, heightSizingMode, ownerWidth, ownerHeight))
            {
                CleanupContentsNodesRecursively(node);
                return;
            }

            node.CloneChildrenIfNeeded();
            node.SetLayoutHadOverflow(false);
            CleanupContentsNodesRecursively(node);

            // STEP 1
            FlexDirection mainAxis = node.Style.FlexDirection.ResolveDirection(direction);
            FlexDirection crossAxis = mainAxis.ResolveCrossDirection(direction);
            bool isMainAxisRow = mainAxis.IsRow();
            bool isNodeFlexWrap = node.Style.FlexWrap != Wrap.NoWrap;

            float mainAxisOwnerSize = isMainAxisRow ? ownerWidth : ownerHeight;
            float crossAxisOwnerSize = isMainAxisRow ? ownerHeight : ownerWidth;

            float paddingAndBorderAxisMain = BoundAxis.PaddingAndBorderForAxis(node, mainAxis, direction, ownerWidth);
            float paddingAndBorderAxisCross = BoundAxis.PaddingAndBorderForAxis(node, crossAxis, direction, ownerWidth);
            float leadingPaddingAndBorderCross =
                node.Style.ComputeFlexStartPaddingAndBorder(crossAxis, direction, ownerWidth);

            SizingMode sizingModeMainDim = isMainAxisRow ? widthSizingMode : heightSizingMode;
            SizingMode sizingModeCrossDim = isMainAxisRow ? heightSizingMode : widthSizingMode;

            float paddingAndBorderAxisRow = isMainAxisRow ? paddingAndBorderAxisMain : paddingAndBorderAxisCross;
            float paddingAndBorderAxisColumn = isMainAxisRow ? paddingAndBorderAxisCross : paddingAndBorderAxisMain;

            // STEP 2
            float availableInnerWidth = CalculateAvailableInnerDimension(
                node, direction, Dimension.Width, availableWidth - marginAxisRow,
                paddingAndBorderAxisRow, ownerWidth, ownerWidth);
            float availableInnerHeight = CalculateAvailableInnerDimension(
                node, direction, Dimension.Height, availableHeight - marginAxisColumn,
                paddingAndBorderAxisColumn, ownerHeight, ownerWidth);

            float availableInnerMainDim = isMainAxisRow ? availableInnerWidth : availableInnerHeight;
            float availableInnerCrossDim = isMainAxisRow ? availableInnerHeight : availableInnerWidth;

            // STEP 3
            float ownerWidthForChildren = availableInnerWidth;
            float ownerHeightForChildren = availableInnerHeight;

            if (node.Config.IsExperimentalFeatureEnabled(ExperimentalFeature.FixFlexBasisFitContent))
            {
                var owner = node.GetOwner();
                bool isChildOfScrollContainer = owner != null && owner.Style.Overflow == Overflow.Scroll;
                if (!isChildOfScrollContainer)
                {
                    if (Comparison.IsUndefined(ownerWidthForChildren) && Comparison.IsDefined(ownerWidth))
                    {
                        ownerWidthForChildren = CalculateAvailableInnerDimension(
                            node, direction, Dimension.Width, ownerWidth - marginAxisRow,
                            paddingAndBorderAxisRow, ownerWidth, ownerWidth);
                    }
                    if (Comparison.IsUndefined(ownerHeightForChildren) && Comparison.IsDefined(ownerHeight))
                    {
                        ownerHeightForChildren = CalculateAvailableInnerDimension(
                            node, direction, Dimension.Height, ownerHeight - marginAxisColumn,
                            paddingAndBorderAxisColumn, ownerHeight, ownerWidth);
                    }
                }
            }

            float totalMainDim = 0;
            totalMainDim += ComputeFlexBasisForChildren(
                node, availableInnerWidth, availableInnerHeight,
                ownerWidthForChildren, ownerHeightForChildren,
                widthSizingMode, heightSizingMode, direction, mainAxis,
                performLayout, ref layoutMarkerData, depth, generationCount);

            if (childCount > 1)
            {
                totalMainDim +=
                    node.Style.ComputeGapForAxis(mainAxis, availableInnerMainDim) *
                    (float)(childCount - 1);
            }

            bool mainAxisOverflows =
                (sizingModeMainDim != SizingMode.MaxContent) && totalMainDim > availableInnerMainDim;

            if (isNodeFlexWrap && mainAxisOverflows && sizingModeMainDim == SizingMode.FitContent)
                sizingModeMainDim = SizingMode.StretchFit;

            // STEP 4: COLLECT FLEX ITEMS INTO FLEX LINES
            var layoutChildren = new List<Node>(node.GetLayoutChildren());
            int startOfLineIndex = 0;
            int lineCount = 0;
            float totalLineCrossDim = 0;
            float crossAxisGap = node.Style.ComputeGapForAxis(crossAxis, availableInnerCrossDim);
            float maxLineMainDim = 0;

            while (startOfLineIndex < layoutChildren.Count)
            {
                var lineIterator = new ListSegmentEnumerator(layoutChildren, startOfLineIndex);
                var flexLine = YogaAlgorithm.CalculateFlexLine(
                    node, ownerDirection, ownerWidth, mainAxisOwnerSize,
                    availableInnerWidth, availableInnerMainDim, lineIterator, lineCount);
                startOfLineIndex = lineIterator.CurrentIndex;

                bool canSkipFlex = !performLayout && sizingModeCrossDim == SizingMode.StretchFit;

                // STEP 5
                bool sizeBasedOnContent = false;
                if (sizingModeMainDim != SizingMode.StretchFit)
                {
                    var style = node.Style;
                    float minInnerWidth = style.ResolvedMinDimension(
                        direction, Dimension.Width, ownerWidth, ownerWidth).Unwrap() - paddingAndBorderAxisRow;
                    float maxInnerWidth = style.ResolvedMaxDimension(
                        direction, Dimension.Width, ownerWidth, ownerWidth).Unwrap() - paddingAndBorderAxisRow;
                    float minInnerHeight = style.ResolvedMinDimension(
                        direction, Dimension.Height, ownerHeight, ownerWidth).Unwrap() - paddingAndBorderAxisColumn;
                    float maxInnerHeight = style.ResolvedMaxDimension(
                        direction, Dimension.Height, ownerHeight, ownerWidth).Unwrap() - paddingAndBorderAxisColumn;

                    float minInnerMainDim = isMainAxisRow ? minInnerWidth : minInnerHeight;
                    float maxInnerMainDim = isMainAxisRow ? maxInnerWidth : maxInnerHeight;

                    if (Comparison.IsDefined(minInnerMainDim) && flexLine.SizeConsumed < minInnerMainDim)
                    {
                        availableInnerMainDim = minInnerMainDim;
                    }
                    else if (Comparison.IsDefined(maxInnerMainDim) && flexLine.SizeConsumed > maxInnerMainDim)
                    {
                        availableInnerMainDim = maxInnerMainDim;
                    }
                    else
                    {
                        bool useLegacyStretchBehaviour = node.HasErrata(Errata.StretchFlexBasis);
                        if (!useLegacyStretchBehaviour &&
                            ((Comparison.IsDefined(flexLine.Layout.TotalFlexGrowFactors) &&
                              flexLine.Layout.TotalFlexGrowFactors == 0) ||
                             (Comparison.IsDefined(node.ResolveFlexGrow()) &&
                              node.ResolveFlexGrow() == 0)))
                        {
                            availableInnerMainDim = flexLine.SizeConsumed;
                        }
                        sizeBasedOnContent = !useLegacyStretchBehaviour;
                    }
                }

                if (!sizeBasedOnContent && Comparison.IsDefined(availableInnerMainDim))
                    flexLine.Layout.RemainingFreeSpace = availableInnerMainDim - flexLine.SizeConsumed;
                else if (flexLine.SizeConsumed < 0)
                    flexLine.Layout.RemainingFreeSpace = -flexLine.SizeConsumed;

                if (!canSkipFlex)
                {
                    ResolveFlexibleLength(
                        node, flexLine, mainAxis, crossAxis, direction,
                        ownerWidth, mainAxisOwnerSize,
                        availableInnerMainDim, availableInnerCrossDim,
                        availableInnerWidth, availableInnerHeight,
                        mainAxisOverflows, sizingModeCrossDim, performLayout,
                        ref layoutMarkerData, depth, generationCount);
                }

                node.SetLayoutHadOverflow(
                    node.Layout.HadOverflow() || (flexLine.Layout.RemainingFreeSpace < 0));

                // STEP 6
                JustifyMainAxis(node, flexLine, mainAxis, crossAxis, direction,
                    sizingModeMainDim, sizingModeCrossDim, mainAxisOwnerSize, ownerWidth,
                    availableInnerMainDim, availableInnerCrossDim, availableInnerWidth, performLayout);

                float containerCrossAxis = availableInnerCrossDim;
                if (sizingModeCrossDim == SizingMode.MaxContent || sizingModeCrossDim == SizingMode.FitContent)
                {
                    containerCrossAxis = BoundAxis.ComputeBoundAxis(
                        node, crossAxis, direction,
                        flexLine.Layout.CrossDim + paddingAndBorderAxisCross,
                        crossAxisOwnerSize, ownerWidth) - paddingAndBorderAxisCross;
                }

                if (!isNodeFlexWrap && sizingModeCrossDim == SizingMode.StretchFit)
                    flexLine.Layout.CrossDim = availableInnerCrossDim;

                if (!isNodeFlexWrap)
                {
                    flexLine.Layout.CrossDim = BoundAxis.ComputeBoundAxis(
                        node, crossAxis, direction,
                        flexLine.Layout.CrossDim + paddingAndBorderAxisCross,
                        crossAxisOwnerSize, ownerWidth) - paddingAndBorderAxisCross;
                }

                // STEP 7: CROSS-AXIS ALIGNMENT
                if (performLayout)
                {
                    for (int i = 0; i < flexLine.ItemsInFlow.Count; i++)
                    {
                        var child = flexLine.ItemsInFlow[i];
                        float leadingCrossDim = leadingPaddingAndBorderCross;
                        Align alignItem = AlignHelper.ResolveChildAlignment(node, child);

                        if (alignItem == Align.Stretch &&
                            !child.Style.FlexStartMarginIsAuto(crossAxis, direction) &&
                            !child.Style.FlexEndMarginIsAuto(crossAxis, direction))
                        {
                            if (!child.HasDefiniteLength(crossAxis.Dimension(), availableInnerCrossDim))
                            {
                                float childMainSize = child.Layout.MeasuredDimension(mainAxis.Dimension());
                                var childStyle = child.Style;
                                float childCrossSize = childStyle.AspectRatio.IsDefined()
                                    ? child.Style.ComputeMarginForAxis(crossAxis, availableInnerWidth) +
                                        (isMainAxisRow
                                             ? childMainSize / childStyle.AspectRatio.Unwrap()
                                             : childMainSize * childStyle.AspectRatio.Unwrap())
                                    : flexLine.Layout.CrossDim;

                                childMainSize += child.Style.ComputeMarginForAxis(mainAxis, availableInnerWidth);

                                SizingMode childMainSizingMode = SizingMode.StretchFit;
                                SizingMode childCrossSizingMode = SizingMode.StretchFit;
                                ConstrainMaxSizeForMode(child, direction, mainAxis,
                                    availableInnerMainDim, availableInnerWidth,
                                    ref childMainSizingMode, ref childMainSize);
                                ConstrainMaxSizeForMode(child, direction, crossAxis,
                                    availableInnerCrossDim, availableInnerWidth,
                                    ref childCrossSizingMode, ref childCrossSize);

                                float childWidthS = isMainAxisRow ? childMainSize : childCrossSize;
                                float childHeightS = !isMainAxisRow ? childMainSize : childCrossSize;

                                var alignContent = node.Style.AlignContent;
                                var crossAxisDoesNotGrow = alignContent != Align.Stretch && isNodeFlexWrap;
                                SizingMode childWidthSizingMode =
                                    Comparison.IsUndefined(childWidthS) || (!isMainAxisRow && crossAxisDoesNotGrow)
                                    ? SizingMode.MaxContent : SizingMode.StretchFit;
                                SizingMode childHeightSizingMode =
                                    Comparison.IsUndefined(childHeightS) || (isMainAxisRow && crossAxisDoesNotGrow)
                                    ? SizingMode.MaxContent : SizingMode.StretchFit;

                                CalculateLayoutInternal(child, childWidthS, childHeightS, direction,
                                    childWidthSizingMode, childHeightSizingMode,
                                    availableInnerWidth, availableInnerHeight,
                                    true, LayoutPassReason.Stretch,
                                    ref layoutMarkerData, depth, generationCount);
                            }
                }
                else
                {
                            float remainingCrossDim = containerCrossAxis -
                                child.DimensionWithMargin(crossAxis, availableInnerWidth);

                            if (child.Style.FlexStartMarginIsAuto(crossAxis, direction) &&
                                child.Style.FlexEndMarginIsAuto(crossAxis, direction))
                            {
                                leadingCrossDim += Comparison.MaxOrDefined(0.0f, remainingCrossDim / 2);
                            }
                            else if (child.Style.FlexEndMarginIsAuto(crossAxis, direction))
                            {
                                // No-Op
                            }
                            else if (child.Style.FlexStartMarginIsAuto(crossAxis, direction))
                            {
                                leadingCrossDim += Comparison.MaxOrDefined(0.0f, remainingCrossDim);
                            }
                            else if (alignItem == Align.FlexStart)
                            {
                                // No-Op
                            }
                            else if (alignItem == Align.Center)
                            {
                                leadingCrossDim += remainingCrossDim / 2;
                            }
                            else
                            {
                                leadingCrossDim += remainingCrossDim;
                            }
                        }
                        child.SetLayoutPosition(
                            child.Layout.Position(crossAxis.FlexStartEdge()) +
                                totalLineCrossDim + leadingCrossDim,
                            crossAxis.FlexStartEdge());
                    }
                }

                float appliedCrossGap = lineCount != 0 ? crossAxisGap : 0.0f;
                totalLineCrossDim += flexLine.Layout.CrossDim + appliedCrossGap;
                maxLineMainDim = Comparison.MaxOrDefined(maxLineMainDim, flexLine.Layout.MainDim);
                lineCount++;
            }

            // STEP 8: MULTI-LINE CONTENT ALIGNMENT
            if (performLayout && (isNodeFlexWrap || Baseline.IsBaselineLayout(node)))
            {
                float leadPerLine = 0;
                float currentLead = leadingPaddingAndBorderCross;
                float extraSpacePerLine = 0;

                float unclampedCrossDim = sizingModeCrossDim == SizingMode.StretchFit
                    ? availableInnerCrossDim + paddingAndBorderAxisCross
                    : node.HasDefiniteLength(crossAxis.Dimension(), crossAxisOwnerSize)
                    ? node.GetResolvedDimension(direction, crossAxis.Dimension(), crossAxisOwnerSize, ownerWidth).Unwrap()
                    : totalLineCrossDim + paddingAndBorderAxisCross;

                float innerCrossDim = BoundAxis.ComputeBoundAxis(
                    node, crossAxis, direction, unclampedCrossDim,
                    crossAxisOwnerSize, ownerWidth) - paddingAndBorderAxisCross;

                float remainingAlignContentDim = innerCrossDim - totalLineCrossDim;
                Align alignContent = remainingAlignContentDim >= 0
                    ? node.Style.AlignContent
                    : AlignHelper.FallbackAlignment(node.Style.AlignContent);

                switch (alignContent)
                {
                    case Align.Start: case Align.End: break;
                    case Align.FlexEnd: currentLead += remainingAlignContentDim; break;
                    case Align.Center: currentLead += remainingAlignContentDim / 2; break;
                    case Align.Stretch:
                        extraSpacePerLine = remainingAlignContentDim / (float)(lineCount);
                        break;
                    case Align.SpaceAround:
                        currentLead += remainingAlignContentDim / (2 * (float)(lineCount));
                        leadPerLine = remainingAlignContentDim / (float)(lineCount);
                        break;
                    case Align.SpaceEvenly:
                        currentLead += remainingAlignContentDim / (float)(lineCount + 1);
                        leadPerLine = remainingAlignContentDim / (float)(lineCount + 1);
                        break;
                    case Align.SpaceBetween:
                        if (lineCount > 1) leadPerLine = remainingAlignContentDim / (float)(lineCount - 1);
                        break;
                    case Align.Auto: case Align.FlexStart: case Align.Baseline: break;
                }

                int endIndex = 0;
                for (int i = 0; i < lineCount; i++)
                {
                    int startIndex = endIndex;
                    float lineHeight = 0;
                    float maxAscentForCurrentLine = 0;
                    float maxDescentForCurrentLine = 0;
                    for (int j = startIndex; j < layoutChildren.Count; j++)
                    {
                        var child = layoutChildren[j];
                        if (child.Style.Display == Display.None) continue;
                        if (child.Style.PositionType != PositionType.Absolute)
                        {
                            if (child.GetLineIndex() != (nuint)i) break;
                            if (child.IsLayoutDimensionDefined(crossAxis))
                            {
                                lineHeight = Comparison.MaxOrDefined(lineHeight,
                                    child.Layout.MeasuredDimension(crossAxis.Dimension()) +
                                        child.Style.ComputeMarginForAxis(crossAxis, availableInnerWidth));
                            }
                            if (AlignHelper.ResolveChildAlignment(node, child) == Align.Baseline)
                            {
                                float ascent = Baseline.CalculateBaseline(child) +
                                    child.Style.ComputeFlexStartMargin(FlexDirection.Column, direction, availableInnerWidth);
                                float descent = child.Layout.MeasuredDimension(Dimension.Height) +
                                    child.Style.ComputeMarginForAxis(FlexDirection.Column, availableInnerWidth) - ascent;
                                maxAscentForCurrentLine = Comparison.MaxOrDefined(maxAscentForCurrentLine, ascent);
                                maxDescentForCurrentLine = Comparison.MaxOrDefined(maxDescentForCurrentLine, descent);
                                lineHeight = Comparison.MaxOrDefined(lineHeight, maxAscentForCurrentLine + maxDescentForCurrentLine);
                            }
                        }
                        endIndex = j + 1;
                    }
                    currentLead += i != 0 ? crossAxisGap : 0;
                    lineHeight += extraSpacePerLine;

                    for (int j = startIndex; j < endIndex; j++)
                    {
                        var child = layoutChildren[j];
                        if (child.Style.Display == Display.None) continue;
                        if (child.Style.PositionType != PositionType.Absolute)
                        {
                            switch (AlignHelper.ResolveChildAlignment(node, child))
                            {
                                case Align.Start: case Align.End: break;
                                case Align.FlexStart:
                                    child.SetLayoutPosition(
                                        currentLead + child.Style.ComputeFlexStartPosition(crossAxis, direction, availableInnerWidth),
                                        crossAxis.FlexStartEdge());
                                    break;
                                case Align.FlexEnd:
                                    child.SetLayoutPosition(
                                        currentLead + lineHeight -
                                            child.Style.ComputeFlexEndMargin(crossAxis, direction, availableInnerWidth) -
                                            child.Layout.MeasuredDimension(crossAxis.Dimension()),
                                        crossAxis.FlexStartEdge());
                                    break;
                                case Align.Center:
                                {
                                    float childHeightCross = child.Layout.MeasuredDimension(crossAxis.Dimension());
                                    child.SetLayoutPosition(currentLead + (lineHeight - childHeightCross) / 2, crossAxis.FlexStartEdge());
                                    break;
                                }
                                case Align.Stretch:
                                {
                                    child.SetLayoutPosition(
                                        currentLead + child.Style.ComputeFlexStartMargin(crossAxis, direction, availableInnerWidth),
                                        crossAxis.FlexStartEdge());
                                    if (!child.HasDefiniteLength(crossAxis.Dimension(), availableInnerCrossDim))
                                    {
                                        float childWidthML = isMainAxisRow
                                            ? (child.Layout.MeasuredDimension(Dimension.Width) +
                                               child.Style.ComputeMarginForAxis(mainAxis, availableInnerWidth))
                                            : leadPerLine + lineHeight;
                                        float childHeightML = !isMainAxisRow
                                            ? (child.Layout.MeasuredDimension(Dimension.Height) +
                                               child.Style.ComputeMarginForAxis(crossAxis, availableInnerWidth))
                                            : leadPerLine + lineHeight;
                                        if (!(Comparison.InexactEquals(childWidthML, child.Layout.MeasuredDimension(Dimension.Width)) &&
                                              Comparison.InexactEquals(childHeightML, child.Layout.MeasuredDimension(Dimension.Height))))
                                        {
                                            CalculateLayoutInternal(child, childWidthML, childHeightML, direction,
                                                SizingMode.StretchFit, SizingMode.StretchFit,
                                                availableInnerWidth, availableInnerHeight,
                                                true, LayoutPassReason.MultilineStretch,
                                                ref layoutMarkerData, depth, generationCount);
                                        }
                                    }
                                    break;
                                }
                                case Align.Baseline:
                                    child.SetLayoutPosition(
                                        currentLead + maxAscentForCurrentLine - Baseline.CalculateBaseline(child) +
                                            child.Style.ComputeFlexStartPosition(FlexDirection.Column, direction, availableInnerCrossDim),
                                        PhysicalEdge.Top);
                                    break;
                                case Align.Auto: case Align.SpaceBetween: case Align.SpaceAround: case Align.SpaceEvenly: break;
                            }
                        }
                    }
                    currentLead = currentLead + leadPerLine + lineHeight;
                }
            }

            // STEP 9: COMPUTING FINAL DIMENSIONS
            node.SetLayoutMeasuredDimension(
                BoundAxis.ComputeBoundAxis(node, FlexDirection.Row, direction,
                    availableWidth - marginAxisRow, ownerWidth, ownerWidth), Dimension.Width);
            node.SetLayoutMeasuredDimension(
                BoundAxis.ComputeBoundAxis(node, FlexDirection.Column, direction,
                    availableHeight - marginAxisColumn, ownerHeight, ownerWidth), Dimension.Height);

            if (sizingModeMainDim == SizingMode.MaxContent ||
                (node.Style.Overflow != Overflow.Scroll && sizingModeMainDim == SizingMode.FitContent))
            {
                node.SetLayoutMeasuredDimension(
                    BoundAxis.ComputeBoundAxis(node, mainAxis, direction,
                        maxLineMainDim, mainAxisOwnerSize, ownerWidth), mainAxis.Dimension());
            }
            else if (sizingModeMainDim == SizingMode.FitContent && node.Style.Overflow == Overflow.Scroll)
            {
                node.SetLayoutMeasuredDimension(
                    Comparison.MaxOrDefined(
                        Comparison.MinOrDefined(
                            availableInnerMainDim + paddingAndBorderAxisMain,
                            BoundAxis.BoundAxisWithinMinAndMax(node, direction, mainAxis,
                                new FloatOptional(maxLineMainDim), mainAxisOwnerSize, ownerWidth).Unwrap()),
                        paddingAndBorderAxisMain), mainAxis.Dimension());
            }

            if (sizingModeCrossDim == SizingMode.MaxContent ||
                (node.Style.Overflow != Overflow.Scroll && sizingModeCrossDim == SizingMode.FitContent))
            {
                node.SetLayoutMeasuredDimension(
                    BoundAxis.ComputeBoundAxis(node, crossAxis, direction,
                        totalLineCrossDim + paddingAndBorderAxisCross, crossAxisOwnerSize, ownerWidth),
                    crossAxis.Dimension());
            }
            else if (sizingModeCrossDim == SizingMode.FitContent && node.Style.Overflow == Overflow.Scroll)
            {
                node.SetLayoutMeasuredDimension(
                    Comparison.MaxOrDefined(
                        Comparison.MinOrDefined(
                            availableInnerCrossDim + paddingAndBorderAxisCross,
                            BoundAxis.BoundAxisWithinMinAndMax(node, direction, crossAxis,
                                new FloatOptional(totalLineCrossDim + paddingAndBorderAxisCross),
                                crossAxisOwnerSize, ownerWidth).Unwrap()),
                        paddingAndBorderAxisCross), crossAxis.Dimension());
            }

            // wrap-reverse
            if (performLayout && node.Style.FlexWrap == Wrap.WrapReverse)
            {
                foreach (var child in node.GetLayoutChildren())
                {
                    if (child.Style.PositionType != PositionType.Absolute)
                    {
                        child.SetLayoutPosition(
                            node.Layout.MeasuredDimension(crossAxis.Dimension()) -
                                child.Layout.Position(crossAxis.FlexStartEdge()) -
                                child.Layout.MeasuredDimension(crossAxis.Dimension()),
                            crossAxis.FlexStartEdge());
                    }
                }
            }

            if (performLayout)
            {
                // STEP 10: SETTING TRAILING POSITIONS FOR CHILDREN
                bool needsMainTrailingPos = TrailingPosition.NeedsTrailingPosition(mainAxis);
                bool needsCrossTrailingPos = TrailingPosition.NeedsTrailingPosition(crossAxis);
                if (needsMainTrailingPos || needsCrossTrailingPos)
                {
                    foreach (var child in node.GetLayoutChildren())
                    {
                        if (child.Style.Display == Display.None || child.Style.PositionType == PositionType.Absolute)
                            continue;
                        if (needsMainTrailingPos)
                            TrailingPosition.SetChildTrailingPosition(node, child, mainAxis);
                        if (needsCrossTrailingPos)
                            TrailingPosition.SetChildTrailingPosition(node, child, crossAxis);
                    }
                }

                // STEP 11: SIZING AND POSITIONING ABSOLUTE CHILDREN
                if (node.Style.PositionType != PositionType.Static ||
                    node.AlwaysFormsContainingBlock || depth == 1)
                {
                    AbsoluteLayout.LayoutAbsoluteDescendants(
                        node, node,
                        isMainAxisRow ? sizingModeMainDim : sizingModeCrossDim,
                        direction, ref layoutMarkerData, depth, generationCount,
                        0.0f, 0.0f, availableInnerWidth, availableInnerHeight);
                }
            }
        }

        public static bool CalculateLayoutInternal(
            Node node, float availableWidth, float availableHeight,
        Direction ownerDirection,
            SizingMode widthSizingMode, SizingMode heightSizingMode,
            float ownerWidth, float ownerHeight,
            bool performLayout, LayoutPassReason reason,
            ref LayoutData layoutMarkerData, uint depth, uint generationCount)
        {
            LayoutResults layout = node.Layout;
        depth++;

        bool needToVisitNode =
            (node.IsDirty() && layout.GenerationCount != generationCount) ||
                layout.ConfigVersion != node.Config.GetVersion() ||
            layout.LastOwnerDirection != ownerDirection;

        if (needToVisitNode)
        {
            layout.NextCachedMeasurementsIndex = 0;
            layout.CachedLayout.AvailableWidth = -1;
            layout.CachedLayout.AvailableHeight = -1;
            layout.CachedLayout.WidthSizingMode = SizingMode.MaxContent;
            layout.CachedLayout.HeightSizingMode = SizingMode.MaxContent;
            layout.CachedLayout.ComputedWidth = -1;
            layout.CachedLayout.ComputedHeight = -1;
        }

            CachedMeasurement? cachedResults = null;

        if (node.HasMeasureFunc())
        {
                float marginAxisRow = node.Style.ComputeMarginForAxis(FlexDirection.Row, ownerWidth);
                float marginAxisColumn = node.Style.ComputeMarginForAxis(FlexDirection.Column, ownerWidth);

            if (Cache.CanUseCachedMeasurement(
                        widthSizingMode, availableWidth, heightSizingMode, availableHeight,
                        layout.CachedLayout.WidthSizingMode, layout.CachedLayout.AvailableWidth,
                        layout.CachedLayout.HeightSizingMode, layout.CachedLayout.AvailableHeight,
                        layout.CachedLayout.ComputedWidth, layout.CachedLayout.ComputedHeight,
                        marginAxisRow, marginAxisColumn, node.Config))
            {
                cachedResults = layout.CachedLayout;
            }
            else
            {
                for (int i = 0; i < layout.NextCachedMeasurementsIndex; i++)
                {
                    if (Cache.CanUseCachedMeasurement(
                                widthSizingMode, availableWidth, heightSizingMode, availableHeight,
                            layout.CachedMeasurements[i].WidthSizingMode,
                            layout.CachedMeasurements[i].AvailableWidth,
                            layout.CachedMeasurements[i].HeightSizingMode,
                            layout.CachedMeasurements[i].AvailableHeight,
                            layout.CachedMeasurements[i].ComputedWidth,
                            layout.CachedMeasurements[i].ComputedHeight,
                                marginAxisRow, marginAxisColumn, node.Config))
                    {
                        cachedResults = layout.CachedMeasurements[i];
                        break;
                    }
                }
            }
        }
        else if (performLayout)
        {
            if (Comparison.InexactEquals(layout.CachedLayout.AvailableWidth, availableWidth) &&
                Comparison.InexactEquals(layout.CachedLayout.AvailableHeight, availableHeight) &&
                layout.CachedLayout.WidthSizingMode == widthSizingMode &&
                layout.CachedLayout.HeightSizingMode == heightSizingMode)
            {
                cachedResults = layout.CachedLayout;
            }
        }
        else
        {
            for (uint i = 0; i < layout.NextCachedMeasurementsIndex; i++)
            {
                if (Comparison.InexactEquals(layout.CachedMeasurements[i].AvailableWidth, availableWidth) &&
                    Comparison.InexactEquals(layout.CachedMeasurements[i].AvailableHeight, availableHeight) &&
                    layout.CachedMeasurements[i].WidthSizingMode == widthSizingMode &&
                    layout.CachedMeasurements[i].HeightSizingMode == heightSizingMode)
                {
                    cachedResults = layout.CachedMeasurements[i];
                    break;
                }
            }
        }

        if (!needToVisitNode && cachedResults != null)
        {
                layout.SetMeasuredDimension(Dimension.Width, cachedResults.Value.ComputedWidth);
                layout.SetMeasuredDimension(Dimension.Height, cachedResults.Value.ComputedHeight);
                if (performLayout) layoutMarkerData.CachedLayouts += 1;
                else layoutMarkerData.CachedMeasures += 1;
        }
        else
        {
                CalculateLayoutImpl(node, availableWidth, availableHeight,
                    ownerDirection, widthSizingMode, heightSizingMode,
                    ownerWidth, ownerHeight, performLayout, reason,
                    ref layoutMarkerData, depth, generationCount);

            layout.LastOwnerDirection = ownerDirection;
                layout.ConfigVersion = node.Config.GetVersion();

            if (cachedResults == null)
            {
                layoutMarkerData.MaxMeasureCache = Math.Max(
                    layoutMarkerData.MaxMeasureCache,
                    layout.NextCachedMeasurementsIndex + 1u);

                if (layout.NextCachedMeasurementsIndex == LayoutResults.MaxCachedMeasurements)
                    layout.NextCachedMeasurementsIndex = 0;

                var newCacheEntry = new CachedMeasurement
                {
                    AvailableWidth = availableWidth,
                    AvailableHeight = availableHeight,
                    WidthSizingMode = widthSizingMode,
                    HeightSizingMode = heightSizingMode,
                    ComputedWidth = layout.MeasuredDimension(Dimension.Width),
                    ComputedHeight = layout.MeasuredDimension(Dimension.Height),
                };

                if (performLayout)
                {
                    layout.CachedLayout = newCacheEntry;
                }
                else
                {
                    layout.CachedMeasurements[layout.NextCachedMeasurementsIndex] = newCacheEntry;
                    layout.NextCachedMeasurementsIndex++;
                }
            }
        }

        if (performLayout)
        {
                node.SetLayoutDimension(node.Layout.MeasuredDimension(Dimension.Width), Dimension.Width);
                node.SetLayoutDimension(node.Layout.MeasuredDimension(Dimension.Height), Dimension.Height);
            node.SetHasNewLayout(true);
            node.SetDirty(false);
        }

        layout.GenerationCount = generationCount;

        LayoutType layoutType;
        if (performLayout)
        {
            layoutType = cachedResults != null ? LayoutType.CachedLayout : LayoutType.Layout;
        }
        else
        {
            layoutType = cachedResults != null ? LayoutType.CachedMeasure : LayoutType.Measure;
        }
        if (Event.HasSubscribers)
        {
            Event.Publish(node, EventType.NodeLayout,
                new Event.NodeLayoutData { LayoutType = layoutType });
        }

        return (needToVisitNode || cachedResults == null);
    }

        public static void CalculateLayout(
            Node node, float ownerWidth, float ownerHeight, Direction ownerDirection)
        {
        Event.Publish(node, EventType.LayoutPassStart);
        LayoutData markerData = new LayoutData();
            gCurrentGenerationCount++;
            node.ProcessDimensions();
            Direction direction = node.ResolveDirection(ownerDirection);
            float width = YogaConstants.Undefined;
        SizingMode widthSizingMode = SizingMode.MaxContent;
            var style = node.Style;

            if (node.HasDefiniteLength(Dimension.Width, ownerWidth))
            {
                width = node.GetResolvedDimension(direction, FlexDirection.Row.Dimension(), ownerWidth, ownerWidth)
                    .Unwrap() + node.Style.ComputeMarginForAxis(FlexDirection.Row, ownerWidth);
            widthSizingMode = SizingMode.StretchFit;
        }
            else if (style.ResolvedMaxDimension(direction, Dimension.Width, ownerWidth, ownerWidth).IsDefined())
        {
                width = style.ResolvedMaxDimension(direction, Dimension.Width, ownerWidth, ownerWidth).Unwrap();
            widthSizingMode = SizingMode.FitContent;
        }
        else
        {
            width = ownerWidth;
            widthSizingMode = Comparison.IsUndefined(width) ? SizingMode.MaxContent : SizingMode.StretchFit;
        }

            float height = YogaConstants.Undefined;
        SizingMode heightSizingMode = SizingMode.MaxContent;
            if (node.HasDefiniteLength(Dimension.Height, ownerHeight))
            {
                height = node.GetResolvedDimension(direction, FlexDirection.Column.Dimension(), ownerHeight, ownerWidth)
                    .Unwrap() + node.Style.ComputeMarginForAxis(FlexDirection.Column, ownerWidth);
            heightSizingMode = SizingMode.StretchFit;
        }
            else if (style.ResolvedMaxDimension(direction, Dimension.Height, ownerHeight, ownerWidth).IsDefined())
        {
                height = style.ResolvedMaxDimension(direction, Dimension.Height, ownerHeight, ownerWidth).Unwrap();
            heightSizingMode = SizingMode.FitContent;
        }
        else
        {
            height = ownerHeight;
            heightSizingMode = Comparison.IsUndefined(height) ? SizingMode.MaxContent : SizingMode.StretchFit;
        }

            if (CalculateLayoutInternal(node, width, height, ownerDirection,
                    widthSizingMode, heightSizingMode, ownerWidth, ownerHeight,
                    true, LayoutPassReason.Initial, ref markerData, 0, gCurrentGenerationCount))
            {
                node.SetPosition(node.Layout.GetDirection(), ownerWidth, ownerHeight);
                PixelGrid.RoundLayoutResultsToPixelGrid(node, 0.0f, 0.0f);
            }

        if (Event.HasSubscribers)
        {
            Event.Publish(node, EventType.LayoutPassEnd,
                new Event.LayoutPassEndData { LayoutData = markerData });
        }
        }
    }

    internal class ListSegmentEnumerator : IEnumerator<Node>
    {
        private readonly List<Node> _list;
        private int _index;

        public ListSegmentEnumerator(List<Node> list, int startIndex)
        {
            _list = list;
            _index = startIndex - 1;
        }

        public Node Current => _list[_index];
        object System.Collections.IEnumerator.Current => Current;
        public int CurrentIndex => _index < _list.Count ? _index : _list.Count;

        public bool MoveNext()
        {
            _index++;
            return _index < _list.Count;
        }

        public void Reset() { throw new NotSupportedException(); }
        public void Dispose() { }
    }
}
