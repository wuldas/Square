// Copyright (c) Meta Platforms, Inc. and affiliates.
//
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
//
// Original: yoga/YGValue.h, yoga/YGValue.cpp

using System.Runtime.CompilerServices;

namespace Square.Rendering.Yoga
{
    /// <summary>
    /// Public API constants and helper for YGValue.
    /// The YGValue struct itself is defined in StyleLength.cs.
    /// </summary>
    internal static class YogaValue
    {
        public static readonly YGValue YGValueZero = new YGValue(0, Unit.Point);
        public static readonly YGValue YGValueUndefined = new YGValue(YogaConstants.Undefined, Unit.Undefined);
        public static readonly YGValue YGValueAuto = new YGValue(YogaConstants.Undefined, Unit.Auto);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool YGFloatIsUndefined(float value)
        {
            return float.IsNaN(value);
        }
    }
}
