// Copyright (c) Meta Platforms, Inc. and affiliates.
//
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.

namespace Square.Rendering.Yoga
{
    internal static class Baseline
    {
        // Calculate baseline represented as an offset from the top edge of the node.
        public static float CalculateBaseline(Node node)
        {
            if (node.HasBaselineFunc())
            {
                Event.Publish(node, EventType.NodeBaselineStart);

                float baseline = node.Baseline(
                    node.Layout.MeasuredDimension(Dimension.Width),
                    node.Layout.MeasuredDimension(Dimension.Height));

                Event.Publish(node, EventType.NodeBaselineEnd);

                Debug.AssertFatal.AssertWithNode(
                    node,
                    !float.IsNaN(baseline),
                    "Expect custom baseline function to not return NaN");
                return baseline;
            }

            Node? baselineChild = null;
            foreach (var child in node.GetLayoutChildren())
            {
                if (child.GetLineIndex() > 0)
                {
                    break;
                }
                if (child.Style.PositionType == PositionType.Absolute)
                {
                    continue;
                }
                if (AlignHelper.ResolveChildAlignment(node, child) == Align.Baseline ||
                    child.IsReferenceBaseline())
                {
                    baselineChild = child;
                    break;
                }

                if (baselineChild == null)
                {
                    baselineChild = child;
                }
            }

            if (baselineChild == null)
            {
                return node.Layout.MeasuredDimension(Dimension.Height);
            }

            float childBaseline = CalculateBaseline(baselineChild);
            return childBaseline + baselineChild.Layout.Position(PhysicalEdge.Top);
        }

        // Whether any of the children of this node participate in baseline alignment
        public static bool IsBaselineLayout(Node node)
        {
            if (node.Style.FlexDirection.IsColumn())
            {
                return false;
            }
            if (node.Style.AlignItems == Align.Baseline)
            {
                return true;
            }
            foreach (var child in node.GetLayoutChildren())
            {
                if (child.Style.PositionType != PositionType.Absolute &&
                    child.Style.AlignSelf == Align.Baseline)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

