using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Square.Rendering.Yoga
{
    internal class Style
    {
        public const float DefaultFlexGrow = 0.0f;
        public const float DefaultFlexShrink = 0.0f;
        public const float WebDefaultFlexShrink = 1.0f;

        private Direction _direction = Direction.Inherit;
        private FlexDirection _flexDirection = FlexDirection.Column;
        private Justify _justifyContent = Justify.FlexStart;
        private Justify _justifyItems = Justify.Stretch;
        private Justify _justifySelf = Justify.Auto;
        private Align _alignContent = Align.FlexStart;
        private Align _alignItems = Align.Stretch;
        private Align _alignSelf = Align.Auto;
        private PositionType _positionType = PositionType.Relative;
        private Wrap _flexWrap = Wrap.NoWrap;
        private Overflow _overflow = Overflow.Visible;
        private Display _display = Display.Flex;
        private BoxSizing _boxSizing = BoxSizing.BorderBox;

        private StyleValueHandle _flex;
        private StyleValueHandle _flexGrow;
        private StyleValueHandle _flexShrink;
        private StyleValueHandle _flexBasis = StyleValueHandle.OfAuto();
        private readonly StyleValueHandle[] _margin = new StyleValueHandle[9];
        private readonly StyleValueHandle[] _position = new StyleValueHandle[9];
        private readonly StyleValueHandle[] _padding = new StyleValueHandle[9];
        private readonly StyleValueHandle[] _border = new StyleValueHandle[9];
        private readonly StyleValueHandle[] _gap = new StyleValueHandle[3];
        private static readonly StyleValueHandle[] DefaultDimensions = new StyleValueHandle[2] { StyleValueHandle.OfAuto(), StyleValueHandle.OfAuto() };
        private readonly StyleValueHandle[] _dimensions = new StyleValueHandle[2];
        private readonly StyleValueHandle[] _minDimensions = new StyleValueHandle[2];
        private readonly StyleValueHandle[] _maxDimensions = new StyleValueHandle[2];
        private StyleValueHandle _aspectRatio;

        // Grid properties
        private GridTrackList _gridTemplateColumns = new GridTrackList();
        private GridTrackList _gridTemplateRows = new GridTrackList();
        private GridTrackList _gridAutoColumns = new GridTrackList();
        private GridTrackList _gridAutoRows = new GridTrackList();
        private GridLine _gridColumnStart = new GridLine();
        private GridLine _gridColumnEnd = new GridLine();
        private GridLine _gridRowStart = new GridLine();
        private GridLine _gridRowEnd = new GridLine();

        private StyleValuePool _pool = new StyleValuePool();

        public Style()
        {
            Array.Copy(DefaultDimensions, _dimensions, 2);
        }

        public Direction Direction
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _direction;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _direction = value;
        }

        public FlexDirection FlexDirection
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _flexDirection;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _flexDirection = value;
        }

        public Justify JustifyContent
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _justifyContent;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _justifyContent = value;
        }

        public Justify JustifyItems
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _justifyItems;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _justifyItems = value;
        }

        public Justify JustifySelf
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _justifySelf;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _justifySelf = value;
        }

        public Align AlignContent
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _alignContent;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _alignContent = value;
        }

        public Align AlignItems
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _alignItems;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _alignItems = value;
        }

        public Align AlignSelf
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _alignSelf;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _alignSelf = value;
        }

        public PositionType PositionType
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _positionType;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _positionType = value;
        }

        public Wrap FlexWrap
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _flexWrap;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _flexWrap = value;
        }

        public Overflow Overflow
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _overflow;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _overflow = value;
        }

        public Display Display
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _display;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _display = value;
        }

        public FloatOptional Flex
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _pool.GetNumber(_flex);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _pool.Store(ref _flex, value);
        }

        public FloatOptional FlexGrow
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _pool.GetNumber(_flexGrow);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _pool.Store(ref _flexGrow, value);
        }

        public FloatOptional FlexShrink
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _pool.GetNumber(_flexShrink);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _pool.Store(ref _flexShrink, value);
        }

        public StyleSizeLength FlexBasis
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _pool.GetSize(_flexBasis);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _pool.Store(ref _flexBasis, value);
        }

        public StyleLength Margin(Edge edge)
        {
            return _pool.GetLength(_margin[YogaEnums.ToUnderlying(edge)]);
        }

        public void SetMargin(Edge edge, StyleLength value)
        {
            _pool.Store(ref _margin[YogaEnums.ToUnderlying(edge)], value);
        }

        public StyleLength Position(Edge edge)
        {
            return _pool.GetLength(_position[YogaEnums.ToUnderlying(edge)]);
        }

        public void SetPosition(Edge edge, StyleLength value)
        {
            _pool.Store(ref _position[YogaEnums.ToUnderlying(edge)], value);
        }

        public StyleLength Padding(Edge edge)
        {
            return _pool.GetLength(_padding[YogaEnums.ToUnderlying(edge)]);
        }

        public void SetPadding(Edge edge, StyleLength value)
        {
            _pool.Store(ref _padding[YogaEnums.ToUnderlying(edge)], value);
        }

        public StyleLength Border(Edge edge)
        {
            return _pool.GetLength(_border[YogaEnums.ToUnderlying(edge)]);
        }

        public void SetBorder(Edge edge, StyleLength value)
        {
            _pool.Store(ref _border[YogaEnums.ToUnderlying(edge)], value);
        }

        public StyleLength Gap(Gutter gutter)
        {
            return _pool.GetLength(_gap[YogaEnums.ToUnderlying(gutter)]);
        }

        public void SetGap(Gutter gutter, StyleLength value)
        {
            _pool.Store(ref _gap[YogaEnums.ToUnderlying(gutter)], value);
        }

        public StyleSizeLength Dimension(Dimension axis)
        {
            return _pool.GetSize(_dimensions[YogaEnums.ToUnderlying(axis)]);
        }

        public void SetDimension(Dimension axis, StyleSizeLength value)
        {
            _pool.Store(ref _dimensions[YogaEnums.ToUnderlying(axis)], value);
        }

        public StyleSizeLength MinDimension(Dimension axis)
        {
            return _pool.GetSize(_minDimensions[YogaEnums.ToUnderlying(axis)]);
        }

        public void SetMinDimension(Dimension axis, StyleSizeLength value)
        {
            _pool.Store(ref _minDimensions[YogaEnums.ToUnderlying(axis)], value);
        }

        // Grid Container Properties
        public GridTrackList GridTemplateColumns
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _gridTemplateColumns;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _gridTemplateColumns = value;
        }

        public void ResizeGridTemplateColumns(int count)
        {
            _gridTemplateColumns.Resize(count);
        }

        public void SetGridTemplateColumnAt(int index, GridTrackSize value)
        {
            _gridTemplateColumns[index] = value;
        }

        public GridTrackList GridTemplateRows
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _gridTemplateRows;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _gridTemplateRows = value;
        }

        public void ResizeGridTemplateRows(int count)
        {
            _gridTemplateRows.Resize(count);
        }

        public void SetGridTemplateRowAt(int index, GridTrackSize value)
        {
            _gridTemplateRows[index] = value;
        }

        public GridTrackList GridAutoColumns
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _gridAutoColumns;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _gridAutoColumns = value;
        }

        public void ResizeGridAutoColumns(int count)
        {
            _gridAutoColumns.Resize(count);
        }

        public void SetGridAutoColumnAt(int index, GridTrackSize value)
        {
            _gridAutoColumns[index] = value;
        }

        public GridTrackList GridAutoRows
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _gridAutoRows;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _gridAutoRows = value;
        }

        public void ResizeGridAutoRows(int count)
        {
            _gridAutoRows.Resize(count);
        }

        public void SetGridAutoRowAt(int index, GridTrackSize value)
        {
            _gridAutoRows[index] = value;
        }

        // Grid Item Properties
        public GridLine GridColumnStart
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _gridColumnStart;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _gridColumnStart = value;
        }

        public GridLine GridColumnEnd
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _gridColumnEnd;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _gridColumnEnd = value;
        }

        public GridLine GridRowStart
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _gridRowStart;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _gridRowStart = value;
        }

        public GridLine GridRowEnd
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _gridRowEnd;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _gridRowEnd = value;
        }

        public FloatOptional ResolvedMinDimension(
            Direction direction,
            Dimension axis,
            float referenceLength,
            float ownerWidth)
        {
            FloatOptional value = MinDimension(axis).Resolve(referenceLength);
            if (this.BoxSizing == BoxSizing.BorderBox)
            {
                return value;
            }

            FloatOptional dimensionPaddingAndBorder = new FloatOptional(
                ComputePaddingAndBorderForDimension(direction, axis, ownerWidth));

            return value + (dimensionPaddingAndBorder.IsDefined() ? dimensionPaddingAndBorder : FloatOptional.Zero);
        }

        public StyleSizeLength MaxDimension(Dimension axis)
        {
            return _pool.GetSize(_maxDimensions[YogaEnums.ToUnderlying(axis)]);
        }

        public void SetMaxDimension(Dimension axis, StyleSizeLength value)
        {
            _pool.Store(ref _maxDimensions[YogaEnums.ToUnderlying(axis)], value);
        }

        public FloatOptional ResolvedMaxDimension(
            Direction direction,
            Dimension axis,
            float referenceLength,
            float ownerWidth)
        {
            FloatOptional value = MaxDimension(axis).Resolve(referenceLength);
            if (this.BoxSizing == BoxSizing.BorderBox)
            {
                return value;
            }

            FloatOptional dimensionPaddingAndBorder = new FloatOptional(
                ComputePaddingAndBorderForDimension(direction, axis, ownerWidth));

            return value + (dimensionPaddingAndBorder.IsDefined() ? dimensionPaddingAndBorder : FloatOptional.Zero);
        }

        public FloatOptional AspectRatio
        {
            get => _pool.GetNumber(_aspectRatio);
            set
            {
                // degenerate aspect ratios act as auto.
                // see https://drafts.csswg.org/css-sizing-4/#valdef-aspect-ratio-ratio
                _pool.Store(ref _aspectRatio,
                    value == 0.0f || float.IsInfinity(value.Unwrap()) ? FloatOptional.Undefined : value);
            }
        }

        public BoxSizing BoxSizing
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _boxSizing;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _boxSizing = value;
        }

        public bool HorizontalInsetsDefined()
        {
            return _position[YogaEnums.ToUnderlying(Edge.Left)].IsDefined ||
                   _position[YogaEnums.ToUnderlying(Edge.Right)].IsDefined ||
                   _position[YogaEnums.ToUnderlying(Edge.All)].IsDefined ||
                   _position[YogaEnums.ToUnderlying(Edge.Horizontal)].IsDefined ||
                   _position[YogaEnums.ToUnderlying(Edge.Start)].IsDefined ||
                   _position[YogaEnums.ToUnderlying(Edge.End)].IsDefined;
        }

        public bool VerticalInsetsDefined()
        {
            return _position[YogaEnums.ToUnderlying(Edge.Top)].IsDefined ||
                   _position[YogaEnums.ToUnderlying(Edge.Bottom)].IsDefined ||
                   _position[YogaEnums.ToUnderlying(Edge.All)].IsDefined ||
                   _position[YogaEnums.ToUnderlying(Edge.Vertical)].IsDefined;
        }

        public bool IsFlexStartPositionDefined(FlexDirection axis, Direction direction)
        {
            return ComputePosition(axis.FlexStartEdge(), direction).IsDefined();
        }

        public bool IsFlexStartPositionAuto(FlexDirection axis, Direction direction)
        {
            return ComputePosition(axis.FlexStartEdge(), direction).IsAuto();
        }

        public bool IsInlineStartPositionDefined(FlexDirection axis, Direction direction)
        {
            return ComputePosition(axis.InlineStartEdge(direction), direction).IsDefined();
        }

        public bool IsInlineStartPositionAuto(FlexDirection axis, Direction direction)
        {
            return ComputePosition(axis.InlineStartEdge(direction), direction).IsAuto();
        }

        public bool IsFlexEndPositionDefined(FlexDirection axis, Direction direction)
        {
            return ComputePosition(axis.FlexEndEdge(), direction).IsDefined();
        }

        public bool IsFlexEndPositionAuto(FlexDirection axis, Direction direction)
        {
            return ComputePosition(axis.FlexEndEdge(), direction).IsAuto();
        }

        public bool IsInlineEndPositionDefined(FlexDirection axis, Direction direction)
        {
            return ComputePosition(axis.InlineEndEdge(direction), direction).IsDefined();
        }

        public bool IsInlineEndPositionAuto(FlexDirection axis, Direction direction)
        {
            return ComputePosition(axis.InlineEndEdge(direction), direction).IsAuto();
        }

        public float ComputeFlexStartPosition(FlexDirection axis, Direction direction, float axisSize)
        {
            return ComputePosition(axis.FlexStartEdge(), direction)
                .Resolve(axisSize)
                .UnwrapOrDefault(0.0f);
        }

        public float ComputeInlineStartPosition(FlexDirection axis, Direction direction, float axisSize)
        {
            return ComputePosition(axis.InlineStartEdge(direction), direction)
                .Resolve(axisSize)
                .UnwrapOrDefault(0.0f);
        }

        public float ComputeFlexEndPosition(FlexDirection axis, Direction direction, float axisSize)
        {
            return ComputePosition(axis.FlexEndEdge(), direction)
                .Resolve(axisSize)
                .UnwrapOrDefault(0.0f);
        }

        public float ComputeInlineEndPosition(FlexDirection axis, Direction direction, float axisSize)
        {
            return ComputePosition(axis.InlineEndEdge(direction), direction)
                .Resolve(axisSize)
                .UnwrapOrDefault(0.0f);
        }

        public float ComputeFlexStartMargin(FlexDirection axis, Direction direction, float widthSize)
        {
            return ComputeMargin(axis.FlexStartEdge(), direction)
                .Resolve(widthSize)
                .UnwrapOrDefault(0.0f);
        }

        public float ComputeInlineStartMargin(FlexDirection axis, Direction direction, float widthSize)
        {
            return ComputeMargin(axis.InlineStartEdge(direction), direction)
                .Resolve(widthSize)
                .UnwrapOrDefault(0.0f);
        }

        public float ComputeFlexEndMargin(FlexDirection axis, Direction direction, float widthSize)
        {
            return ComputeMargin(axis.FlexEndEdge(), direction)
                .Resolve(widthSize)
                .UnwrapOrDefault(0.0f);
        }

        public float ComputeInlineEndMargin(FlexDirection axis, Direction direction, float widthSize)
        {
            return ComputeMargin(axis.InlineEndEdge(direction), direction)
                .Resolve(widthSize)
                .UnwrapOrDefault(0.0f);
        }

        public float ComputeFlexStartBorder(FlexDirection axis, Direction direction)
        {
            return Comparison.MaxOrDefined(
                ComputeBorder(axis.FlexStartEdge(), direction).Resolve(0.0f).UnwrapOrDefault(0.0f),
                0.0f);
        }

        public float ComputeInlineStartBorder(FlexDirection axis, Direction direction)
        {
            return Comparison.MaxOrDefined(
                ComputeBorder(axis.InlineStartEdge(direction), direction)
                    .Resolve(0.0f)
                    .UnwrapOrDefault(0.0f),
                0.0f);
        }

        public float ComputeFlexEndBorder(FlexDirection axis, Direction direction)
        {
            return Comparison.MaxOrDefined(
                ComputeBorder(axis.FlexEndEdge(), direction).Resolve(0.0f).UnwrapOrDefault(0.0f),
                0.0f);
        }

        public float ComputeInlineEndBorder(FlexDirection axis, Direction direction)
        {
            return Comparison.MaxOrDefined(
                ComputeBorder(axis.InlineEndEdge(direction), direction)
                    .Resolve(0.0f)
                    .UnwrapOrDefault(0.0f),
                0.0f);
        }

        public float ComputeFlexStartPadding(FlexDirection axis, Direction direction, float widthSize)
        {
            return Comparison.MaxOrDefined(
                ComputePadding(axis.FlexStartEdge(), direction)
                    .Resolve(widthSize)
                    .UnwrapOrDefault(0.0f),
                0.0f);
        }

        public float ComputeInlineStartPadding(FlexDirection axis, Direction direction, float widthSize)
        {
            return Comparison.MaxOrDefined(
                ComputePadding(axis.InlineStartEdge(direction), direction)
                    .Resolve(widthSize)
                    .UnwrapOrDefault(0.0f),
                0.0f);
        }

        public float ComputeFlexEndPadding(FlexDirection axis, Direction direction, float widthSize)
        {
            return Comparison.MaxOrDefined(
                ComputePadding(axis.FlexEndEdge(), direction)
                    .Resolve(widthSize)
                    .UnwrapOrDefault(0.0f),
                0.0f);
        }

        public float ComputeInlineEndPadding(FlexDirection axis, Direction direction, float widthSize)
        {
            return Comparison.MaxOrDefined(
                ComputePadding(axis.InlineEndEdge(direction), direction)
                    .Resolve(widthSize)
                    .UnwrapOrDefault(0.0f),
                0.0f);
        }

        public float ComputeInlineStartPaddingAndBorder(FlexDirection axis, Direction direction, float widthSize)
        {
            return ComputeInlineStartPadding(axis, direction, widthSize) +
                   ComputeInlineStartBorder(axis, direction);
        }

        public float ComputeFlexStartPaddingAndBorder(FlexDirection axis, Direction direction, float widthSize)
        {
            return ComputeFlexStartPadding(axis, direction, widthSize) +
                   ComputeFlexStartBorder(axis, direction);
        }

        public float ComputeInlineEndPaddingAndBorder(FlexDirection axis, Direction direction, float widthSize)
        {
            return ComputeInlineEndPadding(axis, direction, widthSize) +
                   ComputeInlineEndBorder(axis, direction);
        }

        public float ComputeFlexEndPaddingAndBorder(FlexDirection axis, Direction direction, float widthSize)
        {
            return ComputeFlexEndPadding(axis, direction, widthSize) +
                   ComputeFlexEndBorder(axis, direction);
        }

        public float ComputePaddingAndBorderForDimension(Direction direction, Dimension dimension, float widthSize)
        {
            FlexDirection flexDirectionForDimension = dimension == Square.Rendering.Yoga.Dimension.Width
                ? FlexDirection.Row
                : FlexDirection.Column;

            return ComputeFlexStartPaddingAndBorder(flexDirectionForDimension, direction, widthSize) +
                   ComputeFlexEndPaddingAndBorder(flexDirectionForDimension, direction, widthSize);
        }

        public float ComputeBorderForAxis(FlexDirection axis)
        {
            return ComputeInlineStartBorder(axis, Direction.LTR) +
                   ComputeInlineEndBorder(axis, Direction.LTR);
        }

        public float ComputeMarginForAxis(FlexDirection axis, float widthSize)
        {
            // The total margin for a given axis does not depend on the direction
            // so hardcoding LTR here to avoid piping direction to this function
            return ComputeInlineStartMargin(axis, Direction.LTR, widthSize) +
                   ComputeInlineEndMargin(axis, Direction.LTR, widthSize);
        }

        public float ComputeGapForAxis(FlexDirection axis, float ownerSize)
        {
            var gap = axis.IsRow() ? ComputeColumnGap() : ComputeRowGap();
            return Comparison.MaxOrDefined(gap.Resolve(ownerSize).UnwrapOrDefault(0.0f), 0.0f);
        }

        public float ComputeGapForDimension(Dimension dimension, float ownerSize)
        {
            var gap = dimension == Square.Rendering.Yoga.Dimension.Width ? ComputeColumnGap() : ComputeRowGap();
            return Comparison.MaxOrDefined(gap.Resolve(ownerSize).UnwrapOrDefault(0.0f), 0.0f);
        }

        public bool FlexStartMarginIsAuto(FlexDirection axis, Direction direction)
        {
            return ComputeMargin(axis.FlexStartEdge(), direction).IsAuto();
        }

        public bool FlexEndMarginIsAuto(FlexDirection axis, Direction direction)
        {
            return ComputeMargin(axis.FlexEndEdge(), direction).IsAuto();
        }

        public bool InlineStartMarginIsAuto(FlexDirection axis, Direction direction)
        {
            return ComputeMargin(axis.InlineStartEdge(direction), direction).IsAuto();
        }

        public bool InlineEndMarginIsAuto(FlexDirection axis, Direction direction)
        {
            return ComputeMargin(axis.InlineEndEdge(direction), direction).IsAuto();
        }

        public Style Clone()
        {
            var clone = new Style();
            clone._direction = _direction;
            clone._flexDirection = _flexDirection;
            clone._justifyContent = _justifyContent;
            clone._justifyItems = _justifyItems;
            clone._justifySelf = _justifySelf;
            clone._alignContent = _alignContent;
            clone._alignItems = _alignItems;
            clone._alignSelf = _alignSelf;
            clone._positionType = _positionType;
            clone._flexWrap = _flexWrap;
            clone._overflow = _overflow;
            clone._display = _display;
            clone._boxSizing = _boxSizing;

            clone._flex = _flex;
            clone._flexGrow = _flexGrow;
            clone._flexShrink = _flexShrink;
            clone._flexBasis = _flexBasis;
            clone._aspectRatio = _aspectRatio;

            Array.Copy(_margin, clone._margin, _margin.Length);
            Array.Copy(_position, clone._position, _position.Length);
            Array.Copy(_padding, clone._padding, _padding.Length);
            Array.Copy(_border, clone._border, _border.Length);
            Array.Copy(_gap, clone._gap, _gap.Length);
            Array.Copy(_dimensions, clone._dimensions, _dimensions.Length);
            Array.Copy(_minDimensions, clone._minDimensions, _minDimensions.Length);
            Array.Copy(_maxDimensions, clone._maxDimensions, _maxDimensions.Length);

            clone._gridTemplateColumns = new GridTrackList(_gridTemplateColumns);
            clone._gridTemplateRows = new GridTrackList(_gridTemplateRows);
            clone._gridAutoColumns = new GridTrackList(_gridAutoColumns);
            clone._gridAutoRows = new GridTrackList(_gridAutoRows);
            clone._gridColumnStart = _gridColumnStart;
            clone._gridColumnEnd = _gridColumnEnd;
            clone._gridRowStart = _gridRowStart;
            clone._gridRowEnd = _gridRowEnd;

            // Deep clone the pool - handles reference indices into the pool
            clone._pool = _pool.Clone();

            return clone;
        }

        public override bool Equals(object? obj)
        {
            if (obj is not Style other)
                return false;

            return _direction == other._direction &&
                   _flexDirection == other._flexDirection &&
                   _justifyContent == other._justifyContent &&
                   _justifyItems == other._justifyItems &&
                   _justifySelf == other._justifySelf &&
                   _alignContent == other._alignContent &&
                   _alignItems == other._alignItems &&
                   _alignSelf == other._alignSelf &&
                   _positionType == other._positionType &&
                   _flexWrap == other._flexWrap &&
                   _overflow == other._overflow &&
                   _display == other._display &&
                   NumbersEqual(_flex, _pool, other._flex, other._pool) &&
                   NumbersEqual(_flexGrow, _pool, other._flexGrow, other._pool) &&
                   NumbersEqual(_flexShrink, _pool, other._flexShrink, other._pool) &&
                   LengthsEqual(_flexBasis, _pool, other._flexBasis, other._pool) &&
                   LengthsEqual(_margin, _pool, other._margin, other._pool) &&
                   LengthsEqual(_position, _pool, other._position, other._pool) &&
                   LengthsEqual(_padding, _pool, other._padding, other._pool) &&
                   LengthsEqual(_border, _pool, other._border, other._pool) &&
                   LengthsEqual(_gap, _pool, other._gap, other._pool) &&
                   LengthsEqual(_dimensions, _pool, other._dimensions, other._pool) &&
                   LengthsEqual(_minDimensions, _pool, other._minDimensions, other._pool) &&
                   LengthsEqual(_maxDimensions, _pool, other._maxDimensions, other._pool) &&
                   NumbersEqual(_aspectRatio, _pool, other._aspectRatio, other._pool) &&
                   _gridTemplateColumns.Equals(other._gridTemplateColumns) &&
                   _gridTemplateRows.Equals(other._gridTemplateRows) &&
                   _gridAutoColumns.Equals(other._gridAutoColumns) &&
                   _gridAutoRows.Equals(other._gridAutoRows) &&
                   _gridColumnStart.Equals(other._gridColumnStart) &&
                   _gridColumnEnd.Equals(other._gridColumnEnd) &&
                   _gridRowStart.Equals(other._gridRowStart) &&
                   _gridRowEnd.Equals(other._gridRowEnd);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(_direction);
            hash.Add(_flexDirection);
            hash.Add(_justifyContent);
            hash.Add(_justifyItems);
            hash.Add(_justifySelf);
            hash.Add(_alignContent);
            hash.Add(_alignItems);
            hash.Add(_alignSelf);
            hash.Add(_positionType);
            hash.Add(_flexWrap);
            hash.Add(_overflow);
            hash.Add(_display);
            hash.Add(_boxSizing);
            return hash.ToHashCode();
        }

        private static bool NumbersEqual(StyleValueHandle lhsHandle, StyleValuePool lhsPool, StyleValueHandle rhsHandle, StyleValuePool rhsPool)
        {
            return (lhsHandle.IsDefined && rhsHandle.IsDefined) ||
                   (lhsPool.GetNumber(lhsHandle) == rhsPool.GetNumber(rhsHandle));
        }

        private static bool LengthsEqual(StyleValueHandle lhsHandle, StyleValuePool lhsPool, StyleValueHandle rhsHandle, StyleValuePool rhsPool)
        {
            return (lhsHandle.IsDefined && rhsHandle.IsDefined) ||
                   (lhsPool.GetLength(lhsHandle) == rhsPool.GetLength(rhsHandle));
        }

        private static bool LengthsEqual(StyleValueHandle[] lhs, StyleValuePool lhsPool, StyleValueHandle[] rhs, StyleValuePool rhsPool)
        {
            if (lhs.Length != rhs.Length)
                return false;

            for (int i = 0; i < lhs.Length; i++)
            {
                if (!LengthsEqual(lhs[i], lhsPool, rhs[i], rhsPool))
                    return false;
            }

            return true;
        }

        private StyleLength ComputeColumnGap()
        {
            if (_gap[YogaEnums.ToUnderlying(Gutter.Column)].IsDefined)
            {
                return _pool.GetLength(_gap[YogaEnums.ToUnderlying(Gutter.Column)]);
            }
            else
            {
                return _pool.GetLength(_gap[YogaEnums.ToUnderlying(Gutter.All)]);
            }
        }

        private StyleLength ComputeRowGap()
        {
            if (_gap[YogaEnums.ToUnderlying(Gutter.Row)].IsDefined)
            {
                return _pool.GetLength(_gap[YogaEnums.ToUnderlying(Gutter.Row)]);
            }
            else
            {
                return _pool.GetLength(_gap[YogaEnums.ToUnderlying(Gutter.All)]);
            }
        }

        private StyleLength ComputeLeftEdge(StyleValueHandle[] edges, Direction layoutDirection)
        {
            if (layoutDirection == Direction.LTR &&
                edges[YogaEnums.ToUnderlying(Edge.Start)].IsDefined)
            {
                return _pool.GetLength(edges[YogaEnums.ToUnderlying(Edge.Start)]);
            }
            else if (layoutDirection == Direction.RTL &&
                     edges[YogaEnums.ToUnderlying(Edge.End)].IsDefined)
            {
                return _pool.GetLength(edges[YogaEnums.ToUnderlying(Edge.End)]);
            }
            else if (edges[YogaEnums.ToUnderlying(Edge.Left)].IsDefined)
            {
                return _pool.GetLength(edges[YogaEnums.ToUnderlying(Edge.Left)]);
            }
            else if (edges[YogaEnums.ToUnderlying(Edge.Horizontal)].IsDefined)
            {
                return _pool.GetLength(edges[YogaEnums.ToUnderlying(Edge.Horizontal)]);
            }
            else
            {
                return _pool.GetLength(edges[YogaEnums.ToUnderlying(Edge.All)]);
            }
        }

        private StyleLength ComputeTopEdge(StyleValueHandle[] edges)
        {
            if (edges[YogaEnums.ToUnderlying(Edge.Top)].IsDefined)
            {
                return _pool.GetLength(edges[YogaEnums.ToUnderlying(Edge.Top)]);
            }
            else if (edges[YogaEnums.ToUnderlying(Edge.Vertical)].IsDefined)
            {
                return _pool.GetLength(edges[YogaEnums.ToUnderlying(Edge.Vertical)]);
            }
            else
            {
                return _pool.GetLength(edges[YogaEnums.ToUnderlying(Edge.All)]);
            }
        }

        private StyleLength ComputeRightEdge(StyleValueHandle[] edges, Direction layoutDirection)
        {
            if (layoutDirection == Direction.LTR &&
                edges[YogaEnums.ToUnderlying(Edge.End)].IsDefined)
            {
                return _pool.GetLength(edges[YogaEnums.ToUnderlying(Edge.End)]);
            }
            else if (layoutDirection == Direction.RTL &&
                     edges[YogaEnums.ToUnderlying(Edge.Start)].IsDefined)
            {
                return _pool.GetLength(edges[YogaEnums.ToUnderlying(Edge.Start)]);
            }
            else if (edges[YogaEnums.ToUnderlying(Edge.Right)].IsDefined)
            {
                return _pool.GetLength(edges[YogaEnums.ToUnderlying(Edge.Right)]);
            }
            else if (edges[YogaEnums.ToUnderlying(Edge.Horizontal)].IsDefined)
            {
                return _pool.GetLength(edges[YogaEnums.ToUnderlying(Edge.Horizontal)]);
            }
            else
            {
                return _pool.GetLength(edges[YogaEnums.ToUnderlying(Edge.All)]);
            }
        }

        private StyleLength ComputeBottomEdge(StyleValueHandle[] edges)
        {
            if (edges[YogaEnums.ToUnderlying(Edge.Bottom)].IsDefined)
            {
                return _pool.GetLength(edges[YogaEnums.ToUnderlying(Edge.Bottom)]);
            }
            else if (edges[YogaEnums.ToUnderlying(Edge.Vertical)].IsDefined)
            {
                return _pool.GetLength(edges[YogaEnums.ToUnderlying(Edge.Vertical)]);
            }
            else
            {
                return _pool.GetLength(edges[YogaEnums.ToUnderlying(Edge.All)]);
            }
        }

        private StyleLength ComputePosition(PhysicalEdge edge, Direction direction)
        {
            switch (edge)
            {
                case PhysicalEdge.Left:
                    return ComputeLeftEdge(_position, direction);
                case PhysicalEdge.Top:
                    return ComputeTopEdge(_position);
                case PhysicalEdge.Right:
                    return ComputeRightEdge(_position, direction);
                case PhysicalEdge.Bottom:
                    return ComputeBottomEdge(_position);
                default:
                    throw new InvalidOperationException("Invalid physical edge");
            }
        }

        private StyleLength ComputeMargin(PhysicalEdge edge, Direction direction)
        {
            switch (edge)
            {
                case PhysicalEdge.Left:
                    return ComputeLeftEdge(_margin, direction);
                case PhysicalEdge.Top:
                    return ComputeTopEdge(_margin);
                case PhysicalEdge.Right:
                    return ComputeRightEdge(_margin, direction);
                case PhysicalEdge.Bottom:
                    return ComputeBottomEdge(_margin);
                default:
                    throw new InvalidOperationException("Invalid physical edge");
            }
        }

        private StyleLength ComputePadding(PhysicalEdge edge, Direction direction)
        {
            switch (edge)
            {
                case PhysicalEdge.Left:
                    return ComputeLeftEdge(_padding, direction);
                case PhysicalEdge.Top:
                    return ComputeTopEdge(_padding);
                case PhysicalEdge.Right:
                    return ComputeRightEdge(_padding, direction);
                case PhysicalEdge.Bottom:
                    return ComputeBottomEdge(_padding);
                default:
                    throw new InvalidOperationException("Invalid physical edge");
            }
        }

        private StyleLength ComputeBorder(PhysicalEdge edge, Direction direction)
        {
            switch (edge)
            {
                case PhysicalEdge.Left:
                    return ComputeLeftEdge(_border, direction);
                case PhysicalEdge.Top:
                    return ComputeTopEdge(_border);
                case PhysicalEdge.Right:
                    return ComputeRightEdge(_border, direction);
                case PhysicalEdge.Bottom:
                    return ComputeBottomEdge(_border);
                default:
                    throw new InvalidOperationException("Invalid physical edge");
            }
        }
    }
}

