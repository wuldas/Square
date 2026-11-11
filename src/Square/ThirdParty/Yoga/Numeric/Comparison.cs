using System;
using System.Runtime.CompilerServices;

namespace Square.Rendering.Yoga
{
    internal static class Comparison
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsUndefined(float value)
        {
            return float.IsNaN(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsUndefined(double value)
        {
            return double.IsNaN(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDefined(float value)
        {
            return !float.IsNaN(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDefined(double value)
        {
            return !double.IsNaN(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MaxOrDefined(float a, float b)
        {
            if (IsDefined(a) && IsDefined(b))
            {
                return Math.Max(a, b);
            }
            return IsUndefined(a) ? b : a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double MaxOrDefined(double a, double b)
        {
            if (IsDefined(a) && IsDefined(b))
            {
                return Math.Max(a, b);
            }
            return IsUndefined(a) ? b : a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MinOrDefined(float a, float b)
        {
            if (IsDefined(a) && IsDefined(b))
            {
                return Math.Min(a, b);
            }
            return IsUndefined(a) ? b : a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double MinOrDefined(double a, double b)
        {
            if (IsDefined(a) && IsDefined(b))
            {
                return Math.Min(a, b);
            }
            return IsUndefined(a) ? b : a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool InexactEquals(float a, float b)
        {
            if (IsDefined(a) && IsDefined(b))
            {
                return Math.Abs(a - b) < 0.0001f;
            }
            return IsUndefined(a) && IsUndefined(b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool InexactEquals(double a, double b)
        {
            if (IsDefined(a) && IsDefined(b))
            {
                return Math.Abs(a - b) < 0.0001;
            }
            return IsUndefined(a) && IsUndefined(b);
        }

        public static bool InexactEquals(ReadOnlySpan<float> val1, ReadOnlySpan<float> val2)
        {
            if (val1.Length != val2.Length)
            {
                return false;
            }

            for (int i = 0; i < val1.Length; i++)
            {
                if (!InexactEquals(val1[i], val2[i]))
                {
                    return false;
                }
            }
            return true;
        }

        public static bool InexactEquals(ReadOnlySpan<double> val1, ReadOnlySpan<double> val2)
        {
            if (val1.Length != val2.Length)
            {
                return false;
            }

            for (int i = 0; i < val1.Length; i++)
            {
                if (!InexactEquals(val1[i], val2[i]))
                {
                    return false;
                }
            }
            return true;
        }
    }
}

