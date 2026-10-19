using System;

namespace Square.Rendering.Yoga
{
    internal enum Align : byte
    {
        Auto = 0,
        FlexStart = 1,
        Center = 2,
        FlexEnd = 3,
        Stretch = 4,
        Baseline = 5,
        SpaceBetween = 6,
        SpaceAround = 7,
        SpaceEvenly = 8,
        Start = 9,
        End = 10,
    }

    internal static partial class YogaEnums
    {
        public static int OrdinalCount(Align _) => 11;

        public static string ToString(this Align align)
        {
            return align switch
            {
                Align.Auto => "auto",
                Align.FlexStart => "flex-start",
                Align.Center => "center",
                Align.FlexEnd => "flex-end",
                Align.Stretch => "stretch",
                Align.Baseline => "baseline",
                Align.SpaceBetween => "space-between",
                Align.SpaceAround => "space-around",
                Align.SpaceEvenly => "space-evenly",
                Align.Start => "start",
                Align.End => "end",
                _ => throw new ArgumentOutOfRangeException(nameof(align), align, "Invalid Align enum value")
            };
        }
    }
}

