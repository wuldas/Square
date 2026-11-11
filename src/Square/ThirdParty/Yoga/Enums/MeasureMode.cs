using System;

namespace Square.Rendering.Yoga
{
    internal enum MeasureMode : byte
    {
        Undefined = 0,
        Exactly = 1,
        AtMost = 2,
    }

    internal static partial class YogaEnums
    {
        public static int OrdinalCount(MeasureMode mode) => 3;
    }

    internal static class MeasureModeExtensions
    {
        public static string ToStringFast(this MeasureMode mode)
        {
            return mode switch
            {
                MeasureMode.Undefined => "undefined",
                MeasureMode.Exactly => "exactly",
                MeasureMode.AtMost => "at-most",
                _ => mode.ToString()
            };
        }
    }
}

