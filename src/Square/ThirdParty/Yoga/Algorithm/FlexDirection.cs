using System;

namespace Square.Rendering.Yoga
{
    internal static class FlexDirectionAlgorithms
    {
        public static bool IsRow(this FlexDirection flexDirection)
        {
            return flexDirection == FlexDirection.Row ||
                   flexDirection == FlexDirection.RowReverse;
        }

        public static bool IsColumn(this FlexDirection flexDirection)
        {
            return flexDirection == FlexDirection.Column ||
                   flexDirection == FlexDirection.ColumnReverse;
        }

        public static FlexDirection ResolveDirection(
            this FlexDirection flexDirection,
            Direction direction)
        {
            if (direction == Direction.RTL)
            {
                if (flexDirection == FlexDirection.Row)
                {
                    return FlexDirection.RowReverse;
                }
                else if (flexDirection == FlexDirection.RowReverse)
                {
                    return FlexDirection.Row;
                }
            }

            return flexDirection;
        }

        public static FlexDirection ResolveCrossDirection(
            this FlexDirection flexDirection,
            Direction direction)
        {
            return flexDirection.IsColumn()
                ? FlexDirection.Row.ResolveDirection(direction)
                : FlexDirection.Column;
        }

        public static PhysicalEdge FlexStartEdge(this FlexDirection flexDirection)
        {
            switch (flexDirection)
            {
                case FlexDirection.Column:
                    return PhysicalEdge.Top;
                case FlexDirection.ColumnReverse:
                    return PhysicalEdge.Bottom;
                case FlexDirection.Row:
                    return PhysicalEdge.Left;
                case FlexDirection.RowReverse:
                    return PhysicalEdge.Right;
                default:
                    throw new InvalidOperationException("Invalid FlexDirection");
            }
        }

        public static PhysicalEdge FlexEndEdge(this FlexDirection flexDirection)
        {
            switch (flexDirection)
            {
                case FlexDirection.Column:
                    return PhysicalEdge.Bottom;
                case FlexDirection.ColumnReverse:
                    return PhysicalEdge.Top;
                case FlexDirection.Row:
                    return PhysicalEdge.Right;
                case FlexDirection.RowReverse:
                    return PhysicalEdge.Left;
                default:
                    throw new InvalidOperationException("Invalid FlexDirection");
            }
        }

        public static PhysicalEdge InlineStartEdge(
            this FlexDirection flexDirection,
            Direction direction)
        {
            if (flexDirection.IsRow())
            {
                return direction == Direction.RTL ? PhysicalEdge.Right : PhysicalEdge.Left;
            }

            return PhysicalEdge.Top;
        }

        public static PhysicalEdge InlineEndEdge(
            this FlexDirection flexDirection,
            Direction direction)
        {
            if (flexDirection.IsRow())
            {
                return direction == Direction.RTL ? PhysicalEdge.Left : PhysicalEdge.Right;
            }

            return PhysicalEdge.Bottom;
        }

        public static Dimension Dimension(this FlexDirection flexDirection)
        {
            switch (flexDirection)
            {
                case FlexDirection.Column:
                case FlexDirection.ColumnReverse:
                    return Yoga.Dimension.Height;
                case FlexDirection.Row:
                case FlexDirection.RowReverse:
                    return Yoga.Dimension.Width;
                default:
                    throw new InvalidOperationException("Invalid FlexDirection");
            }
        }
    }
}

