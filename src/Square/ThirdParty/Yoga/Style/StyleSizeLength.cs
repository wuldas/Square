using System;

namespace Square.Rendering.Yoga
{
    internal readonly struct StyleSizeLength : IEquatable<StyleSizeLength>
    {
        private readonly FloatOptional _value;
        private readonly Unit _unit;

        private StyleSizeLength(FloatOptional value, Unit unit)
        {
            _value = value;
            _unit = unit;
        }

        public static StyleSizeLength Points(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return Undefined();
            }

            return new StyleSizeLength(new FloatOptional(value), Unit.Point);
        }

        public static StyleSizeLength Percent(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return Undefined();
            }

            return new StyleSizeLength(new FloatOptional(value), Unit.Percent);
        }

        public static StyleSizeLength Stretch(float fraction)
        {
            if (float.IsNaN(fraction) || float.IsInfinity(fraction))
            {
                return Undefined();
            }

            return new StyleSizeLength(new FloatOptional(fraction), Unit.Stretch);
        }

        public static StyleSizeLength OfAuto()
        {
            return new StyleSizeLength(default, Unit.Auto);
        }

        public static StyleSizeLength OfMaxContent()
        {
            return new StyleSizeLength(default, Unit.MaxContent);
        }

        public static StyleSizeLength OfFitContent()
        {
            return new StyleSizeLength(default, Unit.FitContent);
        }

        public static StyleSizeLength OfStretch()
        {
            return new StyleSizeLength(default, Unit.Stretch);
        }

        public static StyleSizeLength Undefined()
        {
            return new StyleSizeLength(default, Unit.Undefined);
        }

        public bool IsAuto()
        {
            return _unit == Unit.Auto;
        }

        public bool IsMaxContent()
        {
            return _unit == Unit.MaxContent;
        }

        public bool IsFitContent()
        {
            return _unit == Unit.FitContent;
        }

        public bool IsStretch()
        {
            return _unit == Unit.Stretch;
        }

        public bool IsUndefined()
        {
            return _unit == Unit.Undefined;
        }

        public bool IsDefined()
        {
            return !IsUndefined();
        }

        public bool IsPoints()
        {
            return _unit == Unit.Point;
        }

        public bool IsPercent()
        {
            return _unit == Unit.Percent;
        }

        public FloatOptional Value()
        {
            return _value;
        }

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

        public YGValue ToYGValue()
        {
            return new YGValue(_value.Unwrap(), _unit);
        }

        public bool Equals(StyleSizeLength other)
        {
            return _value == other._value && _unit == other._unit;
        }

        public override bool Equals(object? obj)
        {
            return obj is StyleSizeLength other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_value, _unit);
        }

        public static bool operator ==(StyleSizeLength left, StyleSizeLength right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(StyleSizeLength left, StyleSizeLength right)
        {
            return !left.Equals(right);
        }

        public bool InexactEquals(StyleSizeLength other)
        {
            return _unit == other._unit &&
                   FloatOptional.InexactEquals(_value, other._value);
        }
    }
}

