// Copyright (c) Meta Platforms, Inc. and affiliates.
//
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.

using System.Runtime.CompilerServices;

namespace Square.Rendering.Yoga;

internal readonly struct FloatOptional : IEquatable<FloatOptional>
{
    private readonly float _value;

    public static FloatOptional Undefined => new(float.NaN);

    public static FloatOptional Zero => new(0f);

    public FloatOptional(float value)
    {
        _value = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Unwrap() => _value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float UnwrapOrDefault(float defaultValue)
    {
        return IsUndefined() ? defaultValue : _value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsUndefined()
    {
        return float.IsNaN(_value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsDefined()
    {
        return !float.IsNaN(_value);
    }

    public static bool operator ==(FloatOptional lhs, FloatOptional rhs)
    {
        return lhs._value == rhs._value ||
            (float.IsNaN(lhs._value) && float.IsNaN(rhs._value));
    }

    public static bool operator !=(FloatOptional lhs, FloatOptional rhs)
    {
        return !(lhs == rhs);
    }

    public static bool operator ==(FloatOptional lhs, float rhs)
    {
        return lhs == new FloatOptional(rhs);
    }

    public static bool operator !=(FloatOptional lhs, float rhs)
    {
        return !(lhs == rhs);
    }

    public static bool operator ==(float lhs, FloatOptional rhs)
    {
        return rhs == lhs;
    }

    public static bool operator !=(float lhs, FloatOptional rhs)
    {
        return !(rhs == lhs);
    }

    public static FloatOptional operator +(FloatOptional lhs, FloatOptional rhs)
    {
        return new FloatOptional(lhs._value + rhs._value);
    }

    public static bool operator >(FloatOptional lhs, FloatOptional rhs)
    {
        return lhs._value > rhs._value;
    }

    public static bool operator <(FloatOptional lhs, FloatOptional rhs)
    {
        return lhs._value < rhs._value;
    }

    public static bool operator >=(FloatOptional lhs, FloatOptional rhs)
    {
        return lhs > rhs || lhs == rhs;
    }

    public static bool operator <=(FloatOptional lhs, FloatOptional rhs)
    {
        return lhs < rhs || lhs == rhs;
    }

    public static FloatOptional MaxOrDefined(FloatOptional lhs, FloatOptional rhs)
    {
        if (lhs.IsUndefined()) return rhs;
        if (rhs.IsUndefined()) return lhs;
        return lhs._value > rhs._value ? lhs : rhs;
    }

    public static bool InexactEquals(FloatOptional lhs, FloatOptional rhs)
    {
        return Comparison.InexactEquals(lhs._value, rhs._value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(FloatOptional other)
    {
        return this == other;
    }

    public override bool Equals(object? obj)
    {
        return obj is FloatOptional other && this == other;
    }

    public override int GetHashCode()
    {
        return _value.GetHashCode();
    }

    public override string ToString()
    {
        return IsUndefined() ? "undefined" : _value.ToString();
    }
}

