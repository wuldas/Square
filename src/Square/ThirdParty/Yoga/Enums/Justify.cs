using System;

namespace Square.Rendering.Yoga
{
    internal enum Justify : byte
    {
        Auto = 0,
        FlexStart = 1,
        Center = 2,
        FlexEnd = 3,
        SpaceBetween = 4,
        SpaceAround = 5,
        SpaceEvenly = 6,
        Stretch = 7,
        Start = 8,
        End = 9,
    }

    internal static partial class YogaEnums
    {
        public static int OrdinalCount(Justify _) => 10;

        public static string ToString(Justify e)
        {
            return e switch
            {
                Justify.Auto => "auto",
                Justify.FlexStart => "flex-start",
                Justify.Center => "center",
                Justify.FlexEnd => "flex-end",
                Justify.SpaceBetween => "space-between",
                Justify.SpaceAround => "space-around",
                Justify.SpaceEvenly => "space-evenly",
                Justify.Stretch => "stretch",
                Justify.Start => "start",
                Justify.End => "end",
                _ => throw new ArgumentOutOfRangeException(nameof(e), e, "Invalid Justify enum value"),
            };
        }
    }
}

