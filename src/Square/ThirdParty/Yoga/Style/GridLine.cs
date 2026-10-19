using System;

namespace Square.Rendering.Yoga
{
    internal enum GridLineType : byte
    {
        Auto,
        Integer,
        Span,
    }

    internal readonly struct GridLine : IEquatable<GridLine>
    {
        public GridLineType Type { get; }
        public int Integer { get; }

        private GridLine(GridLineType type, int integer)
        {
            Type = type;
            Integer = integer;
        }

        public static GridLine Auto()
        {
            return new GridLine(GridLineType.Auto, 0);
        }

        public static GridLine FromInteger(int value)
        {
            return new GridLine(GridLineType.Integer, value);
        }

        public static GridLine Span(int value)
        {
            return new GridLine(GridLineType.Span, value);
        }

        public bool IsAuto()
        {
            return Type == GridLineType.Auto;
        }

        public bool IsInteger()
        {
            return Type == GridLineType.Integer;
        }

        public bool IsSpan()
        {
            return Type == GridLineType.Span;
        }

        public bool Equals(GridLine other)
        {
            return Type == other.Type && Integer == other.Integer;
        }

        public override bool Equals(object? obj)
        {
            return obj is GridLine other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Type, Integer);
        }

        public static bool operator ==(GridLine left, GridLine right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GridLine left, GridLine right)
        {
            return !left.Equals(right);
        }
    }
}

