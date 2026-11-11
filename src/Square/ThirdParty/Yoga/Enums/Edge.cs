using System;

namespace Square.Rendering.Yoga
{
    internal enum Edge : byte
    {
        Left = 0,
        Top = 1,
        Right = 2,
        Bottom = 3,
        Start = 4,
        End = 5,
        Horizontal = 6,
        Vertical = 7,
        All = 8,
    }

    internal static class EdgeExtensions
    {
        public static int OrdinalCount(this Edge _)
        {
            return 9;
        }

        public static string ToString(this Edge e)
        {
            return e switch
            {
                Edge.Left => "Left",
                Edge.Top => "Top",
                Edge.Right => "Right",
                Edge.Bottom => "Bottom",
                Edge.Start => "Start",
                Edge.End => "End",
                Edge.Horizontal => "Horizontal",
                Edge.Vertical => "Vertical",
                Edge.All => "All",
                _ => $"Unknown Edge ({(int)e})"
            };
        }
    }
}

