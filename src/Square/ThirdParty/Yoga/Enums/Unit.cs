using System;

namespace Square.Rendering.Yoga
{
    internal enum Unit : byte
    {
        Undefined = 0,
        Point = 1,
        Percent = 2,
        Auto = 3,
        MaxContent = 4,
        FitContent = 5,
        Stretch = 6
    }

    internal static partial class YogaEnums
    {
        public static int OrdinalCount(Unit unitType)
        {
            return 7;
        }

        public static string ToString(Unit unit)
        {
            return unit switch
            {
                Unit.Undefined => "undefined",
                Unit.Point => "point",
                Unit.Percent => "percent",
                Unit.Auto => "auto",
                Unit.MaxContent => "max-content",
                Unit.FitContent => "fit-content",
                Unit.Stretch => "stretch",
                _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Invalid Unit enum value")
            };
        }
    }
}

