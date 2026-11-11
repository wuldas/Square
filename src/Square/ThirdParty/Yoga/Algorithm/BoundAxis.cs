// Copyright (c) Meta Platforms, Inc. and affiliates.
//
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.

using System;
namespace Square.Rendering.Yoga
{
    internal static class BoundAxis
    {
        public static float PaddingAndBorderForAxis(
            Node node,
            FlexDirection axis,
            Direction direction,
            float widthSize)
        {
            return node.Style.ComputeInlineStartPaddingAndBorder(axis, direction, widthSize) +
                   node.Style.ComputeInlineEndPaddingAndBorder(axis, direction, widthSize);
        }

        public static FloatOptional BoundAxisWithinMinAndMax(
            Node node,
            Direction direction,
            FlexDirection axis,
            FloatOptional value,
            float axisSize,
            float widthSize)
        {
            FloatOptional min;
            FloatOptional max;

            if (axis.IsColumn())
            {
                min = node.Style.ResolvedMinDimension(direction, Dimension.Height, axisSize, widthSize);
                max = node.Style.ResolvedMaxDimension(direction, Dimension.Height, axisSize, widthSize);
            }
            else if (axis.IsRow())
            {
                min = node.Style.ResolvedMinDimension(direction, Dimension.Width, axisSize, widthSize);
                max = node.Style.ResolvedMaxDimension(direction, Dimension.Width, axisSize, widthSize);
            }
            else
            {
                min = FloatOptional.Undefined;
                max = FloatOptional.Undefined;
            }

            if (max >= FloatOptional.Zero && value > max)
            {
                return max;
            }

            if (min >= FloatOptional.Zero && value < min)
            {
                return min;
            }

            return value;
        }

        public static float ComputeBoundAxis(
            Node node,
            FlexDirection axis,
            Direction direction,
            float value,
            float axisSize,
            float widthSize)
        {
            return Comparison.MaxOrDefined(
                BoundAxisWithinMinAndMax(node, direction, axis, new FloatOptional(value), axisSize, widthSize).Unwrap(),
                PaddingAndBorderForAxis(node, axis, direction, widthSize));
        }
    }
}
