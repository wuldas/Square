using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Square.Rendering.Yoga
{
    // Structs
    internal struct YGSize
    {
        public float Width;
        public float Height;
    }

    // Delegates
    internal delegate YGSize YGMeasureFunc(Node node, float availableWidth, MeasureMode widthMode, float availableHeight, MeasureMode heightMode);
    internal delegate float YGBaselineFunc(Node node, float width, float height);
    internal delegate void YGDirtiedFunc(Node node);

    internal class Node
    {

        // Members
        private bool _hasNewLayout = true;
        private bool _isReferenceBaseline = false;
        private bool _isDirty = true;
        private bool _alwaysFormsContainingBlock = false;
        private NodeType _nodeType = NodeType.Default;
        
        private object? _context;
        
        private YGMeasureFunc? _measureFunc;
        private YGBaselineFunc? _baselineFunc;
        private YGDirtiedFunc? _dirtiedFunc;
        
        private Style _style = new Style();
        private LayoutResults _layout = new LayoutResults();
        
        private nuint _lineIndex = 0;
        private nuint _contentsChildrenCount = 0;
        private Node? _owner;
        private List<Node> _children = new List<Node>();
        
        private Config? _config;
        private StyleSizeLength[] _processedDimensions = new StyleSizeLength[] { StyleSizeLength.Undefined(), StyleSizeLength.Undefined() };

        // Public properties for external access
        public Config Config => _config!;
        public LayoutResults Layout { get => _layout; set => _layout = value; }
        public Style Style => _style;
        
        public bool HasNewLayout { get => _hasNewLayout; set => SetHasNewLayout(value); }
        public bool AlwaysFormsContainingBlock { get => _alwaysFormsContainingBlock; set => _alwaysFormsContainingBlock = value; }
        
        public LayoutableChildren<Node> LayoutChildren => GetLayoutChildren();

        // Constructors
        public Node() : this(Config.Default) { }

        public Node(Config? config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config), "Attempting to construct Node with null config");

            if (_config.UseWebDefaults())
            {
                UseWebDefaults();
            }
        }

        // Copy constructor equivalent in C# - creates a deep copy of the node's own data
        // but a shallow copy of child pointers (same as C++ default copy constructor).
        // Does NOT change children's owner - that's the key difference from C++ move constructor.
        public void MoveFrom(Node other)
        {
            _hasNewLayout = other._hasNewLayout;
            _isReferenceBaseline = other._isReferenceBaseline;
            _isDirty = other._isDirty;
            _alwaysFormsContainingBlock = other._alwaysFormsContainingBlock;
            _nodeType = other._nodeType;
            _context = other._context;
            _measureFunc = other._measureFunc;
            _baselineFunc = other._baselineFunc;
            _dirtiedFunc = other._dirtiedFunc;
            _style = other._style.Clone();
            _layout = other._layout.Clone();
            _lineIndex = other._lineIndex;
            _contentsChildrenCount = other._contentsChildrenCount;
            _owner = other._owner;
            _children = new List<Node>(other._children);
            _config = other._config;
            _processedDimensions = (StyleSizeLength[])other._processedDimensions.Clone();
        }

        // Getters
        public object? GetContext() => _context;
        public bool GetHasNewLayout() => _hasNewLayout;
        public NodeType GetNodeType() => _nodeType;
        
        public bool HasMeasureFunc() => _measureFunc != null;

        public YGSize Measure(float availableWidth, MeasureMode widthMode, float availableHeight, MeasureMode heightMode)
        {
            if (_measureFunc == null) return new YGSize { Width = 0, Height = 0 };
            
            var size = _measureFunc(this, availableWidth, widthMode, availableHeight, heightMode);

            if (float.IsNaN(size.Height) || size.Height < 0 ||
                float.IsNaN(size.Width) || size.Width < 0)
            {
                // yoga::log(...)
                size.Width = Math.Max(0.0f, size.Width);
                size.Height = Math.Max(0.0f, size.Height);
            }

            return size;
        }

        public bool HasBaselineFunc() => _baselineFunc != null;

        public float Baseline(float width, float height)
        {
            return _baselineFunc!(this, width, height);
        }

        public float DimensionWithMargin(FlexDirection axis, float widthSize)
        {
            return _layout.MeasuredDimension(axis.Dimension()) +
                _style.ComputeMarginForAxis(axis, widthSize);
        }

        public bool IsLayoutDimensionDefined(FlexDirection axis)
        {
            float value = _layout.MeasuredDimension(axis.Dimension());
            return !float.IsNaN(value) && value >= 0.0f;
        }

        public bool HasDefiniteLength(Dimension dimension, float ownerSize)
        {
            var usedValue = GetProcessedDimension(dimension).Resolve(ownerSize);
            return usedValue.IsDefined() && usedValue.Unwrap() >= 0.0f;
        }

        public bool HasErrata(Errata errata) => _config!.HasErrata(errata);

        public bool HasContentsChildren() => _contentsChildrenCount != 0;

        public YGDirtiedFunc? GetDirtiedFunc() => _dirtiedFunc;

        // Layout accessors
        public LayoutResults GetLayout() => _layout;

        public nuint GetLineIndex() => _lineIndex;
        public bool IsReferenceBaseline() => _isReferenceBaseline;
        public Node? GetOwner() => _owner;
        public IReadOnlyList<Node> GetChildren() => _children;
        
        public Node? GetChild(nuint index) => _children[(int)index];
        public nuint GetChildCount() => (nuint)_children.Count;

        // Layout Children (Iterator)
        public LayoutableChildren<Node> GetLayoutChildren()
        {
            return new LayoutableChildren<Node>(this);
        }

        public nuint GetLayoutChildCount()
        {
            if (_contentsChildrenCount == 0)
            {
                return (nuint)_children.Count;
            }
            else
            {
                nuint count = 0;
                foreach (var _ in GetLayoutChildren()) count++;
                return count;
            }
        }

        public Config? GetConfig() => _config;
        public bool IsDirty() => _isDirty;

        public StyleSizeLength GetProcessedDimension(Dimension dimension) => _processedDimensions[(int)dimension];

        public FloatOptional GetResolvedDimension(Direction direction, Dimension dimension, float referenceLength, float ownerWidth)
        {
            var value = GetProcessedDimension(dimension).Resolve(referenceLength);
            if (_style.BoxSizing == BoxSizing.BorderBox)
            {
                return value;
            }

            var dimensionPaddingAndBorder = new FloatOptional(_style.ComputePaddingAndBorderForDimension(direction, dimension, ownerWidth));

            return value + (dimensionPaddingAndBorder.IsDefined() ? dimensionPaddingAndBorder : FloatOptional.Zero);
        }

        // Setters
        public void SetContext(object? context) => _context = context;
        public void SetAlwaysFormsContainingBlock(bool value) => _alwaysFormsContainingBlock = value;
        public void SetHasNewLayout(bool value) => _hasNewLayout = value;
        public void SetNodeType(NodeType nodeType) => _nodeType = nodeType;

        public void SetMeasureFunc(YGMeasureFunc? measureFunc)
        {
            if (measureFunc == null)
            {
                SetNodeType(NodeType.Default);
            }
            else
            {
                // Assert: children_.empty()
                SetNodeType(NodeType.Text);
            }
            _measureFunc = measureFunc;
        }

        public void SetBaselineFunc(YGBaselineFunc? baseLineFunc) => _baselineFunc = baseLineFunc;
        public void SetDirtiedFunc(YGDirtiedFunc? dirtiedFunc) => _dirtiedFunc = dirtiedFunc;
        public void SetStyle(Style style) => _style = style;
        public void SetLayout(LayoutResults layout) => _layout = layout;
        public void SetLineIndex(nuint lineIndex) => _lineIndex = lineIndex;
        public void SetIsReferenceBaseline(bool isReferenceBaseline) => _isReferenceBaseline = isReferenceBaseline;
        public void SetOwner(Node? owner) => _owner = owner;

        public void SetConfig(Config? config)
        {
            // Asserts would go here
            if (config == null) throw new ArgumentNullException(nameof(config));

            if (ConfigExtensions.ConfigUpdateInvalidatesLayout(_config!, config))
            {
                MarkDirtyAndPropagate();
                _layout.ConfigVersion = 0;
            }
            else
            {
                _layout.ConfigVersion = config.GetVersion();
            }

            _config = config;
        }

        public void SetDirty(bool isDirty)
        {
            if (isDirty == _isDirty) return;
            _isDirty = isDirty;
            if (isDirty && _dirtiedFunc != null)
            {
                _dirtiedFunc(this);
            }
        }

        public void SetChildren(IReadOnlyList<Node> children)
        {
            _children = new List<Node>(children);
            _contentsChildrenCount = 0;
            foreach (var child in children)
            {
                if (child.Style.Display == Display.Contents)
                {
                    _contentsChildrenCount++;
                }
            }
        }

        public void SetLayoutLastOwnerDirection(Direction direction) => _layout.LastOwnerDirection = direction;
        public void SetLayoutComputedFlexBasis(FloatOptional computedFlexBasis) => _layout.ComputedFlexBasis = computedFlexBasis;
        public void SetLayoutComputedFlexBasisGeneration(uint computedFlexBasisGeneration) => _layout.ComputedFlexBasisGeneration = computedFlexBasisGeneration;
        public void SetLayoutMeasuredDimension(float measuredDimension, Dimension dimension) => _layout.SetMeasuredDimension(dimension, measuredDimension);
        public void SetLayoutHadOverflow(bool hadOverflow) => _layout.SetHadOverflow(hadOverflow);
        public void SetLayoutDimension(float lengthValue, Dimension dimension)
        {
            _layout.SetDimension(dimension, lengthValue);
            _layout.SetRawDimension(dimension, lengthValue);
        }

        public void SetLayoutDirection(Direction direction) => _layout.SetDirection(direction);
        
        public StyleSizeLength ProcessedDimension(Dimension dimension) => _processedDimensions[(int)dimension];
        
        public void MarkChildrenWithDisplayNone()
        {
            // Mark children that should not participate in layout
            foreach (var child in _children)
            {
                if (child.Style.Display == Display.None)
                {
                    child.SetHasNewLayout(true);
                }
            }
        }
        
        public void SetLayoutComputedMainDimension(float dimension, FlexDirection mainAxis)
        {
            _layout.SetMeasuredDimension(mainAxis.Dimension(), dimension);
        }
        
        public void SetLayoutComputedCrossDimension(float dimension, FlexDirection crossAxis)
        {
            _layout.SetMeasuredDimension(crossAxis.Dimension(), dimension);
        }
        
        public void SetLayoutMargin(float margin, PhysicalEdge edge) => _layout.SetMargin(edge, margin);
        public void SetLayoutBorder(float border, PhysicalEdge edge) => _layout.SetBorder(edge, border);
        public void SetLayoutPadding(float padding, PhysicalEdge edge) => _layout.SetPadding(edge, padding);
        public void SetLayoutPosition(float position, PhysicalEdge edge) => _layout.SetPosition(edge, position);

        public void SetPosition(Direction direction, float ownerWidth, float ownerHeight)
        {
            // Root nodes should be always layouted as LTR, so we don't return negative values.
            var directionRespectingRoot = _owner != null ? direction : Direction.LTR;
            var mainAxis = _style.FlexDirection.ResolveDirection(directionRespectingRoot);
            var crossAxis = mainAxis.ResolveCrossDirection(directionRespectingRoot);

            float relativePositionMain = RelativePosition(mainAxis, directionRespectingRoot, mainAxis.IsRow() ? ownerWidth : ownerHeight);
            float relativePositionCross = RelativePosition(crossAxis, directionRespectingRoot, mainAxis.IsRow() ? ownerHeight : ownerWidth);

            var mainAxisLeadingEdge = mainAxis.InlineStartEdge(direction);
            var mainAxisTrailingEdge = mainAxis.InlineEndEdge(direction);
            var crossAxisLeadingEdge = crossAxis.InlineStartEdge(direction);
            var crossAxisTrailingEdge = crossAxis.InlineEndEdge(direction);

            SetLayoutPosition(
                _style.ComputeInlineStartMargin(mainAxis, direction, ownerWidth) + relativePositionMain,
                mainAxisLeadingEdge);
            SetLayoutPosition(
                _style.ComputeInlineEndMargin(mainAxis, direction, ownerWidth) + relativePositionMain,
                mainAxisTrailingEdge);
            SetLayoutPosition(
                _style.ComputeInlineStartMargin(crossAxis, direction, ownerWidth) + relativePositionCross,
                crossAxisLeadingEdge);
            SetLayoutPosition(
                _style.ComputeInlineEndMargin(crossAxis, direction, ownerWidth) + relativePositionCross,
                crossAxisTrailingEdge);
        }

        private float RelativePosition(FlexDirection axis, Direction direction, float axisSize)
        {
            if (_style.PositionType == PositionType.Static) return 0;
            if (_style.IsInlineStartPositionDefined(axis, direction) &&
                !_style.IsInlineStartPositionAuto(axis, direction))
            {
                return _style.ComputeInlineStartPosition(axis, direction, axisSize);
            }
            return -1 * _style.ComputeInlineEndPosition(axis, direction, axisSize);
        }

        public StyleSizeLength ProcessFlexBasis()
        {
            var flexBasis = _style.FlexBasis;
            if (!flexBasis.IsAuto() && !flexBasis.IsUndefined())
            {
                return flexBasis;
            }
            if (_style.Flex.IsDefined() && _style.Flex.Unwrap() > 0.0f)
            {
                return _config!.UseWebDefaults() ? StyleSizeLength.OfAuto() : StyleSizeLength.Points(0);
            }
            return StyleSizeLength.OfAuto();
        }

        public FloatOptional ResolveFlexBasis(Direction direction, FlexDirection flexDirection, float referenceLength, float ownerWidth)
        {
            var value = ProcessFlexBasis().Resolve(referenceLength); // returns FloatOptional
            if (_style.BoxSizing == BoxSizing.BorderBox)
            {
                return value;
            }

            var dim = flexDirection.Dimension();
            var dimensionPaddingAndBorder = new FloatOptional(_style.ComputePaddingAndBorderForDimension(direction, dim, ownerWidth));

            return value + (dimensionPaddingAndBorder.IsDefined() ? dimensionPaddingAndBorder : FloatOptional.Zero);
        }

        public void ProcessDimensions()
        {
            ProcessSingleDimension(Dimension.Width);
            ProcessSingleDimension(Dimension.Height);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessSingleDimension(Dimension dim)
        {
            if (_style.MaxDimension(dim).IsDefined() &&
                Comparison.InexactEquals(_style.MaxDimension(dim).Resolve(float.NaN).Unwrap(), _style.MinDimension(dim).Resolve(float.NaN).Unwrap()))
            {
                _processedDimensions[(int)dim] = _style.MaxDimension(dim);
            }
            else
            {
                _processedDimensions[(int)dim] = _style.Dimension(dim);
            }
        }

        public Direction ResolveDirection(Direction ownerDirection)
        {
            if (_style.Direction == Direction.Inherit)
            {
                return ownerDirection != Direction.Inherit ? ownerDirection : Direction.LTR;
            }
            return _style.Direction;
        }

        public void ClearChildren()
        {
            _children.Clear();
            // _children.shrink_to_fit(); // Not available in standard C#
        }

        public void ReplaceChild(Node child, nuint index)
        {
            var previousChild = _children[(int)index];
            if (previousChild.Style.Display == Display.Contents &&
                child.Style.Display != Display.Contents)
            {
                _contentsChildrenCount--;
            }
            else if (previousChild.Style.Display != Display.Contents &&
                     child.Style.Display == Display.Contents)
            {
                _contentsChildrenCount++;
            }
            _children[(int)index] = child;
        }

        public void ReplaceChild(Node oldChild, Node newChild)
        {
            if (oldChild.Style.Display == Display.Contents &&
                newChild.Style.Display != Display.Contents)
            {
                _contentsChildrenCount--;
            }
            else if (oldChild.Style.Display != Display.Contents &&
                     newChild.Style.Display == Display.Contents)
            {
                _contentsChildrenCount++;
            }
            int index = _children.IndexOf(oldChild);
            if (index >= 0) _children[index] = newChild;
        }

        public void InsertChild(Node child, nuint index)
        {
            if (child.Style.Display == Display.Contents)
            {
                _contentsChildrenCount++;
            }
            _children.Insert((int)index, child);
        }

        public bool RemoveChild(Node child)
        {
            bool removed = _children.Remove(child);
            if (removed)
            {
                if (child.Style.Display == Display.Contents)
                {
                    _contentsChildrenCount--;
                }
            }
            return removed;
        }

        public void RemoveChild(nuint index)
        {
            if (_children[(int)index].Style.Display == Display.Contents)
            {
                _contentsChildrenCount--;
            }
            _children.RemoveAt((int)index);
        }

        public void CloneChildrenIfNeeded()
        {
            nuint i = 0;
            for (int j = 0; j < _children.Count; j++)
            {
                Node child = _children[j];
                if (child.GetOwner() != this)
                {
                    // var newChild = resolveRef(config_->cloneNode(child, this, i));
                    // The Config.CloneNode method needs to be implemented
                    var newChild = _config!.CloneNode(child, this, (int)i);
                    newChild.SetOwner(this);

                    if (newChild.HasContentsChildren())
                    {
                        newChild.CloneChildrenIfNeeded();
                    }
                    _children[j] = newChild;
                }
                i++;
            }
        }

        public void CloneContentsChildrenIfNeeded()
        {
             nuint i = 0;
            for (int j = 0; j < _children.Count; j++)
            {
                Node child = _children[j];
                if (child.Style.Display == Display.Contents && child.GetOwner() != this)
                {
                    var newChild = _config!.CloneNode(child, this, (int)i);
                    newChild.SetOwner(this);
                    newChild.CloneChildrenIfNeeded();
                    _children[j] = newChild;
                }
                i++;
            }
        }

        public void MarkDirtyAndPropagate()
        {
            if (!_isDirty)
            {
                SetDirty(true);
                SetLayoutComputedFlexBasis(FloatOptional.Undefined);
                _owner?.MarkDirtyAndPropagate();
            }
        }

        public float ResolveFlexGrow()
        {
            if (_owner == null) return 0.0f;
            if (_style.FlexGrow.IsDefined()) return _style.FlexGrow.Unwrap();
            if (_style.Flex.IsDefined() && _style.Flex.Unwrap() > 0.0f) return _style.Flex.Unwrap();
            return Style.DefaultFlexGrow;
        }

        public float ResolveFlexShrink()
        {
            if (_owner == null) return 0.0f;
            if (_style.FlexShrink.IsDefined()) return _style.FlexShrink.Unwrap();
            if (!_config!.UseWebDefaults() && _style.Flex.IsDefined() && _style.Flex.Unwrap() < 0.0f)
            {
                return -_style.Flex.Unwrap();
            }
            return _config.UseWebDefaults() ? Style.WebDefaultFlexShrink : Style.DefaultFlexShrink;
        }

        public bool IsNodeFlexible()
        {
            return (_style.PositionType != PositionType.Absolute) && (ResolveFlexGrow() != 0 || ResolveFlexShrink() != 0);
        }

        public void Reset()
        {
            // Asserts: children empty, owner null
            var newNode = new Node(GetConfig());
            // Copy state or replace this instance? 
            // In C# we usually return a new instance or clear fields.
            // Here we mimic the C++ assignment *this = Node{getConfig()};
            
            _hasNewLayout = true;
            _isReferenceBaseline = false;
            _isDirty = true;
            _alwaysFormsContainingBlock = false;
            _nodeType = NodeType.Default;
            _context = null;
            _measureFunc = null;
            _baselineFunc = null;
            _dirtiedFunc = null;
            // style_ = Style(); // reset to default
            // layout_ = LayoutResults(); // reset
            _lineIndex = 0;
            _contentsChildrenCount = 0;
            _owner = null;
            _children.Clear();
            // Keep config
        }

        private void UseWebDefaults()
        {
            _style.FlexDirection = FlexDirection.Row;
            _style.AlignContent = Align.Stretch;
        }
    }

}

