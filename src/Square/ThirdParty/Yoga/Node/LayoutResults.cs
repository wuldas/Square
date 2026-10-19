using System;
using System.Runtime.CompilerServices;

namespace Square.Rendering.Yoga
{
    internal class LayoutResults
    {
        public const int MaxCachedMeasurements = 8;

        public uint ComputedFlexBasisGeneration = 0;
        public FloatOptional ComputedFlexBasis = FloatOptional.Undefined;

        public uint GenerationCount = 0;
        public uint ConfigVersion = 0;
        public Direction LastOwnerDirection = Direction.Inherit;

        public uint NextCachedMeasurementsIndex = 0;
        public CachedMeasurement[] CachedMeasurements = new CachedMeasurement[MaxCachedMeasurements];

        public CachedMeasurement CachedLayout = new CachedMeasurement();

        private Direction _direction = Direction.Inherit;
        private bool _hadOverflow = false;

        private float _dimensionWidth = YogaConstants.Undefined;
        private float _dimensionHeight = YogaConstants.Undefined;
        private float _measuredWidth = YogaConstants.Undefined;
        private float _measuredHeight = YogaConstants.Undefined;
        private float _rawWidth = YogaConstants.Undefined;
        private float _rawHeight = YogaConstants.Undefined;

        private float _positionLeft;
        private float _positionTop;
        private float _positionRight;
        private float _positionBottom;

        private float _marginLeft;
        private float _marginTop;
        private float _marginRight;
        private float _marginBottom;

        private float _borderLeft;
        private float _borderTop;
        private float _borderRight;
        private float _borderBottom;

        private float _paddingLeft;
        private float _paddingTop;
        private float _paddingRight;
        private float _paddingBottom;

        public LayoutResults()
        {
            for (int i = 0; i < MaxCachedMeasurements; i++)
            {
                CachedMeasurements[i] = new CachedMeasurement();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Direction GetDirection() => _direction;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetDirection(Direction direction) => _direction = direction;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HadOverflow() => _hadOverflow;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetHadOverflow(bool hadOverflow) => _hadOverflow = hadOverflow;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Dimension(Dimension axis) => axis == Yoga.Dimension.Width ? _dimensionWidth : _dimensionHeight;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetDimension(Dimension axis, float dimension)
        {
            if (axis == Yoga.Dimension.Width) _dimensionWidth = dimension;
            else _dimensionHeight = dimension;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float MeasuredDimension(Dimension axis) => axis == Yoga.Dimension.Width ? _measuredWidth : _measuredHeight;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float RawDimension(Dimension axis) => axis == Yoga.Dimension.Width ? _rawWidth : _rawHeight;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetMeasuredDimension(Dimension axis, float dimension)
        {
            if (axis == Yoga.Dimension.Width) _measuredWidth = dimension;
            else _measuredHeight = dimension;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetRawDimension(Dimension axis, float dimension)
        {
            if (axis == Yoga.Dimension.Width) _rawWidth = dimension;
            else _rawHeight = dimension;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Position(PhysicalEdge physicalEdge) => physicalEdge switch
        {
            PhysicalEdge.Left => _positionLeft,
            PhysicalEdge.Top => _positionTop,
            PhysicalEdge.Right => _positionRight,
            _ => _positionBottom,
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetPosition(PhysicalEdge physicalEdge, float value)
        {
            switch (physicalEdge)
            {
                case PhysicalEdge.Left: _positionLeft = value; break;
                case PhysicalEdge.Top: _positionTop = value; break;
                case PhysicalEdge.Right: _positionRight = value; break;
                default: _positionBottom = value; break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Margin(PhysicalEdge physicalEdge) => physicalEdge switch
        {
            PhysicalEdge.Left => _marginLeft,
            PhysicalEdge.Top => _marginTop,
            PhysicalEdge.Right => _marginRight,
            _ => _marginBottom,
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetMargin(PhysicalEdge physicalEdge, float value)
        {
            switch (physicalEdge)
            {
                case PhysicalEdge.Left: _marginLeft = value; break;
                case PhysicalEdge.Top: _marginTop = value; break;
                case PhysicalEdge.Right: _marginRight = value; break;
                default: _marginBottom = value; break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Border(PhysicalEdge physicalEdge) => physicalEdge switch
        {
            PhysicalEdge.Left => _borderLeft,
            PhysicalEdge.Top => _borderTop,
            PhysicalEdge.Right => _borderRight,
            _ => _borderBottom,
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBorder(PhysicalEdge physicalEdge, float value)
        {
            switch (physicalEdge)
            {
                case PhysicalEdge.Left: _borderLeft = value; break;
                case PhysicalEdge.Top: _borderTop = value; break;
                case PhysicalEdge.Right: _borderRight = value; break;
                default: _borderBottom = value; break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Padding(PhysicalEdge physicalEdge) => physicalEdge switch
        {
            PhysicalEdge.Left => _paddingLeft,
            PhysicalEdge.Top => _paddingTop,
            PhysicalEdge.Right => _paddingRight,
            _ => _paddingBottom,
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetPadding(PhysicalEdge physicalEdge, float value)
        {
            switch (physicalEdge)
            {
                case PhysicalEdge.Left: _paddingLeft = value; break;
                case PhysicalEdge.Top: _paddingTop = value; break;
                case PhysicalEdge.Right: _paddingRight = value; break;
                default: _paddingBottom = value; break;
            }
        }

        public LayoutResults Clone()
        {
            var clone = new LayoutResults
            {
                ComputedFlexBasisGeneration = ComputedFlexBasisGeneration,
                ComputedFlexBasis = ComputedFlexBasis,
                GenerationCount = GenerationCount,
                ConfigVersion = ConfigVersion,
                LastOwnerDirection = LastOwnerDirection,
                NextCachedMeasurementsIndex = NextCachedMeasurementsIndex,
                CachedLayout = CachedLayout,
                _direction = _direction,
                _hadOverflow = _hadOverflow,
                _dimensionWidth = _dimensionWidth,
                _dimensionHeight = _dimensionHeight,
                _measuredWidth = _measuredWidth,
                _measuredHeight = _measuredHeight,
                _rawWidth = _rawWidth,
                _rawHeight = _rawHeight,
                _positionLeft = _positionLeft,
                _positionTop = _positionTop,
                _positionRight = _positionRight,
                _positionBottom = _positionBottom,
                _marginLeft = _marginLeft,
                _marginTop = _marginTop,
                _marginRight = _marginRight,
                _marginBottom = _marginBottom,
                _borderLeft = _borderLeft,
                _borderTop = _borderTop,
                _borderRight = _borderRight,
                _borderBottom = _borderBottom,
                _paddingLeft = _paddingLeft,
                _paddingTop = _paddingTop,
                _paddingRight = _paddingRight,
                _paddingBottom = _paddingBottom,
            };

            Array.Copy(CachedMeasurements, clone.CachedMeasurements, MaxCachedMeasurements);

            return clone;
        }

        public bool Equals(LayoutResults? layout)
        {
            if (layout is null) return false;

            bool isEqual =
                _positionLeft == layout._positionLeft &&
                _positionTop == layout._positionTop &&
                _positionRight == layout._positionRight &&
                _positionBottom == layout._positionBottom &&
                _dimensionWidth == layout._dimensionWidth &&
                _dimensionHeight == layout._dimensionHeight &&
                _marginLeft == layout._marginLeft &&
                _marginTop == layout._marginTop &&
                _marginRight == layout._marginRight &&
                _marginBottom == layout._marginBottom &&
                _borderLeft == layout._borderLeft &&
                _borderTop == layout._borderTop &&
                _borderRight == layout._borderRight &&
                _borderBottom == layout._borderBottom &&
                _paddingLeft == layout._paddingLeft &&
                _paddingTop == layout._paddingTop &&
                _paddingRight == layout._paddingRight &&
                _paddingBottom == layout._paddingBottom &&
                _direction == layout._direction &&
                _hadOverflow == layout._hadOverflow &&
                LastOwnerDirection == layout.LastOwnerDirection &&
                ConfigVersion == layout.ConfigVersion &&
                NextCachedMeasurementsIndex == layout.NextCachedMeasurementsIndex &&
                CachedLayout.Equals(layout.CachedLayout) &&
                ComputedFlexBasis.Equals(layout.ComputedFlexBasis);

            for (int i = 0; i < MaxCachedMeasurements && isEqual; ++i)
            {
                isEqual = CachedMeasurements[i].Equals(layout.CachedMeasurements[i]);
            }

            if (!float.IsNaN(_measuredWidth) || !float.IsNaN(layout._measuredWidth))
            {
                isEqual = isEqual && (_measuredWidth == layout._measuredWidth);
            }

            if (!float.IsNaN(_measuredHeight) || !float.IsNaN(layout._measuredHeight))
            {
                isEqual = isEqual && (_measuredHeight == layout._measuredHeight);
            }

            return isEqual;
        }
    }
}
