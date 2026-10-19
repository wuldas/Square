using System;

namespace Square.Rendering.Yoga
{
    internal struct YGValue : IEquatable<YGValue>
    {
        public float Value;
        public Unit Unit;

        public YGValue(float value, Unit unit)
        {
            Value = value;
            Unit = unit;
        }

        public bool Equals(YGValue other)
        {
            if (Unit != other.Unit)
                return false;

            return Unit switch
            {
                Unit.Undefined or Unit.Auto or Unit.FitContent or Unit.MaxContent or Unit.Stretch => true,
                Unit.Point or Unit.Percent => Value == other.Value,
                _ => false,
            };
        }

        public override bool Equals(object? obj) => obj is YGValue other && Equals(other);

        public override int GetHashCode()
        {
            return Unit switch
            {
                Unit.Undefined or Unit.Auto or Unit.FitContent or Unit.MaxContent or Unit.Stretch
                    => HashCode.Combine(Unit),
                _ => HashCode.Combine(Value, Unit),
            };
        }

        public static bool operator ==(YGValue lhs, YGValue rhs) => lhs.Equals(rhs);
        public static bool operator !=(YGValue lhs, YGValue rhs) => !lhs.Equals(rhs);

        public static YGValue operator -(YGValue value) => new(-value.Value, value.Unit);
    }

    // default(StyleLength) must be indistinguishable from Undefined(): Unit.Undefined is
    // the zero value of Unit and ValueOrUndefined normalizes the value of every
    // non-Point / non-Percent length to FloatOptional.Undefined.
    internal readonly struct StyleLength : IEquatable<StyleLength>
    {
        public static StyleLength Undefined() => new StyleLength(FloatOptional.Undefined, Unit.Undefined);

        public static StyleLength OfAuto() => new StyleLength(FloatOptional.Undefined, Unit.Auto);

        public static StyleLength Points(float value)
        {
            return IsUndefined(value) || float.IsInfinity(value)
                ? Undefined()
                : new StyleLength(new FloatOptional(value), Unit.Point);
        }

        public static StyleLength Percent(float value)
        {
            return IsUndefined(value) || float.IsInfinity(value)
                ? Undefined()
                : new StyleLength(new FloatOptional(value), Unit.Percent);
        }

        public bool IsAuto() => _unit == Unit.Auto;

        public bool IsUndefined() => _unit == Unit.Undefined;

        public bool IsPoints() => _unit == Unit.Point;

        public bool IsPercent() => _unit == Unit.Percent;

        public bool IsDefined() => !IsUndefined();

        public FloatOptional Value() => ValueOrUndefined;

        public FloatOptional Resolve(float referenceLength)
        {
            switch (_unit)
            {
                case Unit.Point:
                    return _value;
                case Unit.Percent:
                    return new FloatOptional(_value.Unwrap() * referenceLength * 0.01f);
                default:
                    return FloatOptional.Undefined;
            }
        }

        public static explicit operator YGValue(StyleLength length)
        {
            return new YGValue(length.ValueOrUndefined.Unwrap(), length._unit);
        }

        public override bool Equals(object? obj)
        {
            return obj is StyleLength other && Equals(other);
        }

        public bool Equals(StyleLength other)
        {
            return ValueOrUndefined.Equals(other.ValueOrUndefined) && _unit == other._unit;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ValueOrUndefined, _unit);
        }

        public static bool operator ==(StyleLength left, StyleLength right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(StyleLength left, StyleLength right)
        {
            return !left.Equals(right);
        }

        public bool InexactEquals(StyleLength other)
        {
            return _unit == other._unit &&
                FloatOptional.InexactEquals(ValueOrUndefined, other.ValueOrUndefined);
        }

        private StyleLength(FloatOptional value, Unit unit)
        {
            _value = value;
            _unit = unit;
        }

        private readonly FloatOptional _value;
        private readonly Unit _unit;

        private FloatOptional ValueOrUndefined =>
            _unit == Unit.Point || _unit == Unit.Percent ? _value : FloatOptional.Undefined;

        private static bool IsUndefined(float value)
        {
            return float.IsNaN(value);
        }
    }

    internal static class StyleLengthExtensions
    {
        public static bool InexactEquals(this StyleLength a, StyleLength b)
        {
            return a.InexactEquals(b);
        }
    }
}

