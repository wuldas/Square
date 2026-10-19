// Copyright (c) Meta Platforms, Inc. and affiliates.
//
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Square.Rendering.Yoga
{
    internal static partial class YogaEnums
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToUnderlying<TEnum>(TEnum e) where TEnum : struct, Enum
        {
            // All Yoga enums are byte-backed. Read only 1 byte to avoid
            // reading garbage from adjacent bytes under NativeAOT.
            return Unsafe.As<TEnum, byte>(ref e);
        }

        public static IEnumerable<TEnum> Ordinals<TEnum>() where TEnum : struct, Enum
        {
            int ordinalCount = OrdinalCount<TEnum>();
            for (int i = 0; i < ordinalCount; i++)
            {
                yield return Unsafe.As<byte, TEnum>(ref Unsafe.As<int, byte>(ref i));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int OrdinalCount<TEnum>() where TEnum : struct, Enum
        {
            if (typeof(TEnum) == typeof(Align))
            {
                return 11;
            }
            if (typeof(TEnum) == typeof(BoxSizing))
            {
                return 2;
            }
            if (typeof(TEnum) == typeof(Dimension))
            {
                return 2;
            }
            if (typeof(TEnum) == typeof(Direction))
            {
                return 3;
            }
            if (typeof(TEnum) == typeof(Display))
            {
                return 4;
            }
            if (typeof(TEnum) == typeof(Edge))
            {
                return 9;
            }
            if (typeof(TEnum) == typeof(Errata))
            {
                return 4;
            }
            if (typeof(TEnum) == typeof(ExperimentalFeature))
            {
                return 2;
            }
            if (typeof(TEnum) == typeof(FlexDirection))
            {
                return 4;
            }
            if (typeof(TEnum) == typeof(Gutter))
            {
                return 3;
            }
            if (typeof(TEnum) == typeof(GridTrackType))
            {
                return 5;
            }
            if (typeof(TEnum) == typeof(Justify))
            {
                return 10;
            }
            if (typeof(TEnum) == typeof(LogLevel))
            {
                return 6;
            }
            if (typeof(TEnum) == typeof(MeasureMode))
            {
                return 3;
            }
            if (typeof(TEnum) == typeof(NodeType))
            {
                return 2;
            }
            if (typeof(TEnum) == typeof(Overflow))
            {
                return 3;
            }
            if (typeof(TEnum) == typeof(PositionType))
            {
                return 3;
            }
            if (typeof(TEnum) == typeof(Unit))
            {
                return 7;
            }
            if (typeof(TEnum) == typeof(Wrap))
            {
                return 3;
            }
            throw new NotSupportedException("Unsupported enum type does not have ordinality defined.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BitCount<TEnum>() where TEnum : struct, Enum
        {
            int count = OrdinalCount<TEnum>() - 1;
            return 32 - BitOperations.LeadingZeroCount((uint)count);
        }
    }
}

