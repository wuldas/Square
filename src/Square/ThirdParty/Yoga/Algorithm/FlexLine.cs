using System;
using System.Collections.Generic;

namespace Square.Rendering.Yoga
{

    internal struct FlexLineRunningLayout
    {
        public float TotalFlexGrowFactors;
        public float TotalFlexShrinkScaledFactors;
        public float RemainingFreeSpace;
        public float MainDim;
        public float CrossDim;
        public float Gap;
        public float MaxBaseline;

        public FlexLineRunningLayout(float totalFlexGrowFactors, float totalFlexShrinkScaledFactors)
        {
            TotalFlexGrowFactors = totalFlexGrowFactors;
            TotalFlexShrinkScaledFactors = totalFlexShrinkScaledFactors;
            RemainingFreeSpace = 0.0f;
            MainDim = 0.0f;
            CrossDim = 0.0f;
            Gap = 0.0f;
            MaxBaseline = 0.0f;
        }
    }

    internal class FlexLine
    {
        public readonly IReadOnlyList<Node> ItemsInFlow;
        public readonly float SizeConsumed;
        public readonly int NumberOfAutoMargins;
        public FlexLineRunningLayout Layout;

        public FlexLine(IReadOnlyList<Node> itemsInFlow, float sizeConsumed, int numberOfAutoMargins, FlexLineRunningLayout layout)
        {
            ItemsInFlow = itemsInFlow;
            SizeConsumed = sizeConsumed;
            NumberOfAutoMargins = numberOfAutoMargins;
            Layout = layout;
        }
    }

    internal static partial class YogaAlgorithm
    {
        public static FlexLine CalculateFlexLine(
            Node node,
            Direction ownerDirection,
            float ownerWidth,
            float mainAxisOwnerSize,
            float availableInnerWidth,
            float availableInnerMainDim,
            IEnumerator<Node> iterator,
            int lineCount)
        {
            var itemsInFlow = new List<Node>((int)node.GetChildCount());

            float sizeConsumed = 0.0f;
            float totalFlexGrowFactors = 0.0f;
            float totalFlexShrinkScaledFactors = 0.0f;
            int numberOfAutoMargins = 0;
            Node? firstElementInLine = null;

            float sizeConsumedIncludingMinConstraint = 0;
            Direction direction = node.ResolveDirection(ownerDirection);
            FlexDirection mainAxis = node.Style.FlexDirection.ResolveDirection(direction);
            bool isNodeFlexWrap = node.Style.FlexWrap != Wrap.NoWrap;
            float gap = node.Style.ComputeGapForAxis(mainAxis, availableInnerMainDim);

            while (iterator.MoveNext())
            {
                var child = iterator.Current;
                if (child.Style.Display == Display.None ||
                    child.Style.PositionType == PositionType.Absolute)
                {
                    continue;
                }

                if (firstElementInLine == null)
                {
                    firstElementInLine = child;
                }

                if (child.Style.FlexStartMarginIsAuto(mainAxis, ownerDirection))
                {
                    numberOfAutoMargins++;
                }
                if (child.Style.FlexEndMarginIsAuto(mainAxis, ownerDirection))
                {
                    numberOfAutoMargins++;
                }

                child.SetLineIndex((nuint)lineCount);
                float childMarginMainAxis = child.Style.ComputeMarginForAxis(mainAxis, availableInnerWidth);
                float childLeadingGapMainAxis = child == firstElementInLine ? 0.0f : gap;
                
                float flexBasisWithMinAndMaxConstraints = BoundAxis.BoundAxisWithinMinAndMax(
                    child,
                    direction,
                    mainAxis,
                    child.Layout.ComputedFlexBasis,
                    mainAxisOwnerSize,
                    ownerWidth).Unwrap();

                if (sizeConsumedIncludingMinConstraint + flexBasisWithMinAndMaxConstraints +
                            childMarginMainAxis + childLeadingGapMainAxis >
                        availableInnerMainDim &&
                    isNodeFlexWrap && itemsInFlow.Count > 0)
                {
                    break;
                }

                sizeConsumedIncludingMinConstraint += flexBasisWithMinAndMaxConstraints +
                    childMarginMainAxis + childLeadingGapMainAxis;
                sizeConsumed += flexBasisWithMinAndMaxConstraints + childMarginMainAxis +
                    childLeadingGapMainAxis;

                if (child.IsNodeFlexible())
                {
                    totalFlexGrowFactors += child.ResolveFlexGrow();
                    totalFlexShrinkScaledFactors += -child.ResolveFlexShrink() *
                        child.Layout.ComputedFlexBasis.Unwrap();
                }

                itemsInFlow.Add(child);
            }

            if (totalFlexGrowFactors > 0 && totalFlexGrowFactors < 1)
            {
                totalFlexGrowFactors = 1;
            }

            if (totalFlexShrinkScaledFactors > 0 && totalFlexShrinkScaledFactors < 1)
            {
                totalFlexShrinkScaledFactors = 1;
            }

            return new FlexLine(
                itemsInFlow,
                sizeConsumed,
                numberOfAutoMargins,
                new FlexLineRunningLayout(totalFlexGrowFactors, totalFlexShrinkScaledFactors)
            );
        }
    }
}

