// Copyright (c) Meta Platforms, Inc. and affiliates.
//
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.

namespace Square.Rendering.Yoga
{
    internal static class TrailingPosition
    {
        // Given an offset to an edge, returns the offset to the opposite edge on the
        // same axis. This assumes that the width/height of both nodes is determined at
        // this point.
        public static float GetPositionOfOppositeEdge(
            float position,
            FlexDirection axis,
            Node containingNode,
            Node node)
        {
            return containingNode.Layout.MeasuredDimension(axis.Dimension()) -
                node.Layout.MeasuredDimension(axis.Dimension()) - position;
        }

        public static void SetChildTrailingPosition(
            Node node,
            Node child,
            FlexDirection axis)
        {
            child.SetLayoutPosition(
                GetPositionOfOppositeEdge(
                    child.Layout.Position(axis.FlexStartEdge()),
                    axis,
                    node,
                    child),
                axis.FlexEndEdge());
        }

        public static bool NeedsTrailingPosition(FlexDirection axis)
        {
            return axis == FlexDirection.RowReverse ||
                axis == FlexDirection.ColumnReverse;
        }
    }
}

