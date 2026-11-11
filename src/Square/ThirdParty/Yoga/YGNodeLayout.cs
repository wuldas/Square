// Copyright (c) Meta Platforms, Inc. and affiliates.
//
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
//
// Original: yoga/YGNodeLayout.h, yoga/YGNodeLayout.cpp

namespace Square.Rendering.Yoga
{
    /// <summary>
    /// Public C-style API for reading layout results.
    /// Delegates to the internal Node.GetLayout() / LayoutResults.
    /// </summary>
    internal static class YGNodeLayoutAPI
    {
        public static float YGNodeLayoutGetLeft(Node node)
        {
            return node.GetLayout().Position(PhysicalEdge.Left);
        }

        public static float YGNodeLayoutGetTop(Node node)
        {
            return node.GetLayout().Position(PhysicalEdge.Top);
        }

        public static float YGNodeLayoutGetRight(Node node)
        {
            return node.GetLayout().Position(PhysicalEdge.Right);
        }

        public static float YGNodeLayoutGetBottom(Node node)
        {
            return node.GetLayout().Position(PhysicalEdge.Bottom);
        }

        public static float YGNodeLayoutGetWidth(Node node)
        {
            return node.GetLayout().Dimension(Dimension.Width);
        }

        public static float YGNodeLayoutGetHeight(Node node)
        {
            return node.GetLayout().Dimension(Dimension.Height);
        }

        public static YGDirection YGNodeLayoutGetDirection(Node node)
        {
            return node.GetLayout().GetDirection().ToYG();
        }

        public static bool YGNodeLayoutGetHadOverflow(Node node)
        {
            return node.GetLayout().HadOverflow();
        }

        public static float YGNodeLayoutGetMargin(Node node, YGEdge edge)
        {
            return GetResolvedLayoutProperty(
                (n, e) => n.GetLayout().Margin(e),
                node,
                edge.ToInternal());
        }

        public static float YGNodeLayoutGetBorder(Node node, YGEdge edge)
        {
            return GetResolvedLayoutProperty(
                (n, e) => n.GetLayout().Border(e),
                node,
                edge.ToInternal());
        }

        public static float YGNodeLayoutGetPadding(Node node, YGEdge edge)
        {
            return GetResolvedLayoutProperty(
                (n, e) => n.GetLayout().Padding(e),
                node,
                edge.ToInternal());
        }

        public static float YGNodeLayoutGetRawHeight(Node node)
        {
            return node.GetLayout().RawDimension(Dimension.Height);
        }

        public static float YGNodeLayoutGetRawWidth(Node node)
        {
            return node.GetLayout().RawDimension(Dimension.Width);
        }

        private delegate float LayoutPropertyGetter(Node node, PhysicalEdge edge);

        private static float GetResolvedLayoutProperty(
            LayoutPropertyGetter getter,
            Node node,
            Edge edge)
        {
            Debug.AssertFatal.AssertWithNode(
                node,
                edge <= Edge.End,
                "Cannot get layout properties of multi-edge shorthands");

            if (edge == Edge.Start)
            {
                if (node.GetLayout().GetDirection() == Direction.RTL)
                {
                    return getter(node, PhysicalEdge.Right);
                }
                else
                {
                    return getter(node, PhysicalEdge.Left);
                }
            }

            if (edge == Edge.End)
            {
                if (node.GetLayout().GetDirection() == Direction.RTL)
                {
                    return getter(node, PhysicalEdge.Left);
                }
                else
                {
                    return getter(node, PhysicalEdge.Right);
                }
            }

            return getter(node, (PhysicalEdge)(byte)edge);
        }
    }
}
