using System;

namespace Square.Rendering.Yoga
{
    internal static class PixelGrid
    {
        public const float YGUndefined = float.NaN;

        public static float RoundValueToPixelGrid(
            double value,
            double pointScaleFactor,
            bool forceCeil,
            bool forceFloor)
        {
            double scaledValue = value * pointScaleFactor;
            double fractial = scaledValue % 1.0;

            if (fractial < 0)
            {
                fractial += 1.0;
            }

            if (Comparison.InexactEquals(fractial, 0.0))
            {
                scaledValue = scaledValue - fractial;
            }
            else if (Comparison.InexactEquals(fractial, 1.0))
            {
                scaledValue = scaledValue - fractial + 1.0;
            }
            else if (forceCeil)
            {
                scaledValue = scaledValue - fractial + 1.0;
            }
            else if (forceFloor)
            {
                scaledValue = scaledValue - fractial;
            }
            else
            {
                scaledValue = scaledValue - fractial +
                    (!double.IsNaN(fractial) &&
                         (fractial > 0.5 || Comparison.InexactEquals(fractial, 0.5))
                     ? 1.0
                     : 0.0);
            }

            return (double.IsNaN(scaledValue) || double.IsNaN(pointScaleFactor))
                ? YGUndefined
                : (float)(scaledValue / pointScaleFactor);
        }

        public static void RoundLayoutResultsToPixelGrid(
            Node node,
            double absoluteLeft,
            double absoluteTop)
        {
            var pointScaleFactor = (double)node.GetConfig()!.GetPointScaleFactor();

            double nodeLeft = node.GetLayout().Position(PhysicalEdge.Left);
            double nodeTop = node.GetLayout().Position(PhysicalEdge.Top);

            double nodeWidth = node.GetLayout().Dimension(Dimension.Width);
            double nodeHeight = node.GetLayout().Dimension(Dimension.Height);

            double absoluteNodeLeft = absoluteLeft + nodeLeft;
            double absoluteNodeTop = absoluteTop + nodeTop;

            double absoluteNodeRight = absoluteNodeLeft + nodeWidth;
            double absoluteNodeBottom = absoluteNodeTop + nodeHeight;

            if (pointScaleFactor != 0.0)
            {
                bool textRounding = node.GetNodeType() == NodeType.Text;

                node.SetLayoutPosition(
                    RoundValueToPixelGrid(nodeLeft, pointScaleFactor, false, textRounding),
                    PhysicalEdge.Left);

                node.SetLayoutPosition(
                    RoundValueToPixelGrid(nodeTop, pointScaleFactor, false, textRounding),
                    PhysicalEdge.Top);

                double scaledNodeWith = nodeWidth * pointScaleFactor;
                bool hasFractionalWidth =
                    !Comparison.InexactEquals(Math.Round(scaledNodeWith), scaledNodeWith);

                double scaledNodeHeight = nodeHeight * pointScaleFactor;
                bool hasFractionalHeight =
                    !Comparison.InexactEquals(Math.Round(scaledNodeHeight), scaledNodeHeight);

                node.GetLayout().SetDimension(
                    Dimension.Width,
                    RoundValueToPixelGrid(
                        absoluteNodeRight,
                        pointScaleFactor,
                        (textRounding && hasFractionalWidth),
                        (textRounding && !hasFractionalWidth)) -
                    RoundValueToPixelGrid(
                        absoluteNodeLeft, pointScaleFactor, false, textRounding));

                node.GetLayout().SetDimension(
                    Dimension.Height,
                    RoundValueToPixelGrid(
                        absoluteNodeBottom,
                        pointScaleFactor,
                        (textRounding && hasFractionalHeight),
                        (textRounding && !hasFractionalHeight)) -
                    RoundValueToPixelGrid(
                        absoluteNodeTop, pointScaleFactor, false, textRounding));
            }

            foreach (Node child in node.GetChildren())
            {
                RoundLayoutResultsToPixelGrid(child, absoluteNodeLeft, absoluteNodeTop);
            }
        }
    }
}

