using System.Globalization;
using Square.Events;
using Square.Graphics;
using Square.UI;

namespace Square.Controls;

/// <summary>可拖动调整数值的布局分隔条。</summary>
public class Splitter : UIElement
{
    private Point _dragStart;
    private float _dragStartValue;

    /// <summary>当前分隔值，通常表示相邻面板的逻辑像素尺寸。</summary>
    public float Value { get => GetNumericProperty(nameof(Value), 0); set => SetProperty(nameof(Value), Clamp(value)); }
    /// <summary>最小值。</summary>
    public float Minimum { get => GetNumericProperty(nameof(Minimum), 160f); set => SetProperty(nameof(Minimum), value); }
    /// <summary>最大值。</summary>
    public float Maximum { get => GetNumericProperty(nameof(Maximum), 640f); set => SetProperty(nameof(Maximum), value); }
    /// <summary>垂直分隔条调整宽度；水平分隔条调整高度。</summary>
    public bool IsVertical { get => !Properties.HasValue(nameof(IsVertical)) || GetProperty<bool>(nameof(IsVertical)); set => SetProperty(nameof(IsVertical), value); }
    /// <summary>反转拖动方向，用于位于面板左侧的分隔条。</summary>
    public bool IsReversed { get => GetProperty<bool>(nameof(IsReversed)); set => SetProperty(nameof(IsReversed), value); }

    /// <inheritdoc/>
    public override string TagName => "Splitter";

    /// <inheritdoc/>
    protected override bool RequiresStatePaintInvalidation(ElementState flag) =>
        flag == ElementState.Hover || base.RequiresStatePaintInvalidation(flag);

    public override void Paint(IRenderContext context)
    {
        var background = ControlDrawing.GetStyledColor(this, "background", Color.Transparent);
        if (background.A > 0)
            context.FillRect(Geometry, new SolidColorBrush(background));
    }

    /// <inheritdoc/>
    public override Size Measure(Size availableSize) => IsVertical
        ? new Size(float.IsNaN(Width) ? 6 : Width, availableSize.Height)
        : new Size(availableSize.Width, float.IsNaN(Height) ? 6 : Height);

    internal void HandlePointerDown(Point point)
    {
        _dragStart = point;
        _dragStartValue = Value;
        SetState(ElementState.Active, true);
    }

    internal void HandlePointerMove(Point point)
    {
        var delta = IsVertical ? point.X - _dragStart.X : point.Y - _dragStart.Y;
        if (IsReversed) delta = -delta;
        var value = Clamp(_dragStartValue + delta);
        if (value.Equals(Value)) return;
        Value = value;
        DispatchTrusted(StandardEvents.CreateInput());
    }

    internal void HandlePointerUp(Point point)
    {
        HandlePointerMove(point);
        SetState(ElementState.Active, false);
        DispatchTrusted(StandardEvents.CreateChange());
    }

    private float Clamp(float value) => Math.Clamp(value, Math.Min(Minimum, Maximum), Math.Max(Minimum, Maximum));

    private float GetNumericProperty(string name, float fallback)
    {
        if (!Properties.HasValue(name)) return fallback;
        if (Properties.TryGetValue<float>(name, out var value)) return value;
        if (Properties.TryGetValue<int>(name, out var integer)) return integer;
        return fallback;
    }
}

/// <summary>
/// 带内置分隔条的两栏容器：拖动分隔条自动调整两侧面板尺寸，无需手动同步布局。
/// 垂直模式（默认）左右分栏；水平模式上下分栏。
/// </summary>
public class SplitContainer : View
{
    private readonly Splitter _splitter;
    private float _thickness = 8f;

    /// <summary>初始化 <see cref="SplitContainer"/> 的新实例。</summary>
    public SplitContainer()
    {
        Style.Set("display", "flex");
        Style.Set("flex-direction", "row");
        Style.Set("gap", "0");
        Style.Set("width", "100%");
        Style.Set("height", "100%");
        Style.Set("flex", "1");

        First = new View();
        Second = new View();
        First.Style.Set("flex-shrink", "0");
        Second.Style.Set("flex", "1");

        _splitter = new Splitter
        {
            IsVertical = true,
            Minimum = 160,
            Maximum = 640,
            Value = 320
        };
        _splitter.ClassList.Add("splitter");
        // 不设置 inline background：默认透明（Splitter.Paint 的 fallback），
        // 并允许样式表中的 .splitter:hover 规则覆盖。
        _splitter.Style.Set("flex-shrink", "0");
        _splitter.Style.Set("cursor", "col-resize");
        _splitter.AddEventListener(StandardEvents.Input, () =>
        {
            // 拖动后把分隔值同步回 Properties，触发 OnPropertyChanged → ApplyLayout。
            SetProperty(nameof(Value), _splitter.Value);
            DispatchTrusted(StandardEvents.CreateInput());
        });
        _splitter.AddEventListener(StandardEvents.Change, () =>
            DispatchTrusted(StandardEvents.CreateChange()));

        Children.Add(First);
        Children.Add(_splitter);
        Children.Add(Second);
        ApplyLayout();
    }

    /// <inheritdoc/>
    public override void BuildElementTree()
    {
        base.BuildElementTree();
        // sqx 模板中 slot="first"/slot="second" 的内容渲染到对应面板。
        Slots.Render("first", First);
        Slots.Render("second", Second);
    }

    /// <summary>第一面板（垂直模式为左/上面板）。</summary>
    public View First { get; }

    /// <summary>第二面板（垂直模式为右/下面板）。</summary>
    public View Second { get; }

    /// <summary>中间分隔条。</summary>
    public Splitter Splitter => _splitter;

    /// <summary>垂直分隔条调整宽度；水平分隔条调整高度。默认 <c>true</c>（左右分栏）。</summary>
    public bool IsVertical
    {
        get => _splitter.IsVertical;
        set
        {
            if (_splitter.IsVertical == value) return;
            _splitter.IsVertical = value;
            Style.Set("flex-direction", value ? "row" : "column");
            _splitter.Style.Set("cursor", value ? "col-resize" : "row-resize");
            ApplyLayout();
        }
    }

    /// <summary>第一面板的尺寸（垂直模式为宽度，水平模式为高度）。</summary>
    public float Value
    {
        get => GetNumericProperty(nameof(Value), _splitter.Value);
        set => SetProperty(nameof(Value), value);
    }

    /// <summary>第一面板的最小尺寸。</summary>
    public float Minimum
    {
        get => GetNumericProperty(nameof(Minimum), _splitter.Minimum);
        set => SetProperty(nameof(Minimum), value);
    }

    /// <summary>第一面板的最大尺寸。</summary>
    public float Maximum
    {
        get => GetNumericProperty(nameof(Maximum), _splitter.Maximum);
        set => SetProperty(nameof(Maximum), value);
    }

    /// <summary>分隔条厚度（垂直模式为宽度，水平模式为高度）。默认 8px。</summary>
    public float SplitterThickness
    {
        get => GetNumericProperty(nameof(SplitterThickness), _thickness);
        set => SetProperty(nameof(SplitterThickness), value);
    }

    /// <summary>
    /// 无缝衔接（默认 <c>true</c>）：两侧面板各向分隔条延伸一半厚度，覆盖接缝实现无缝相接，
    /// 分隔条置顶，透明时面板直接相连，hover/背景样式时覆盖在接缝上。
    /// 设为 <c>false</c> 时分隔条独立显示在面板之间，保留可见间隙。
    /// </summary>
    public bool IsSeamless
    {
        get => GetBoolProperty(nameof(IsSeamless), _isSeamless);
        set => SetProperty(nameof(IsSeamless), value);
    }

    private readonly bool _isSeamless = true;

    /// <inheritdoc/>
    protected override void OnPropertyChanged(string name)
    {
        base.OnPropertyChanged(name);
        switch (name)
        {
            case nameof(Value):
                _splitter.Value = Value;
                ApplyLayout();
                break;
            case nameof(Minimum):
                _splitter.Minimum = Minimum;
                break;
            case nameof(Maximum):
                _splitter.Maximum = Maximum;
                break;
            case nameof(SplitterThickness):
                _thickness = Math.Max(1, SplitterThickness);
                ApplyLayout();
                break;
            case nameof(IsSeamless):
                ApplyLayout();
                break;
        }
    }

    private float GetNumericProperty(string name, float fallback)
    {
        if (Properties.TryGetValue<float>(name, out var value)) return value;
        if (Properties.TryGetValue<int>(name, out var integer)) return integer;
        return fallback;
    }

    private bool GetBoolProperty(string name, bool fallback)
    {
        if (Properties.TryGetValue<bool>(name, out var value)) return value;
        if (Properties.TryGetValue<int>(name, out var integer)) return integer != 0;
        return fallback;
    }

    private void ApplyLayout()
    {
        var primary = Value.ToString("0", CultureInfo.InvariantCulture) + "px";
        var thickness = _thickness.ToString("0", CultureInfo.InvariantCulture) + "px";
        var overlap = (_thickness / 2f).ToString("0.#", CultureInfo.InvariantCulture) + "px";
        // 无缝模式：面板各向分隔条延伸一半厚度，覆盖接缝实现无缝衔接；
        // 分隔条置顶，透明时面板无缝相接，hover/背景样式时覆盖在接缝上。
        if (IsVertical)
        {
            First.Style.Set("width", primary);
            First.Style.Set("height", "100%");
            Second.Style.Set("height", "100%");
            _splitter.Style.Set("width", thickness);
            _splitter.Style.Set("height", "100%");
            if (IsSeamless)
            {
                First.Style.Set("margin-right", "-" + overlap);
                Second.Style.Set("margin-left", "-" + overlap);
                _splitter.Style.Set("z-index", "2");
            }
            else
            {
                First.Style.Remove("margin-right");
                Second.Style.Remove("margin-left");
                _splitter.Style.Remove("z-index");
            }
        }
        else
        {
            First.Style.Set("height", primary);
            First.Style.Set("width", "100%");
            Second.Style.Set("width", "100%");
            _splitter.Style.Set("height", thickness);
            _splitter.Style.Set("width", "100%");
            if (IsSeamless)
            {
                First.Style.Set("margin-bottom", "-" + overlap);
                Second.Style.Set("margin-top", "-" + overlap);
                _splitter.Style.Set("z-index", "2");
            }
            else
            {
                First.Style.Remove("margin-bottom");
                Second.Style.Remove("margin-top");
                _splitter.Style.Remove("z-index");
            }
        }
        InvalidateLayout();
    }
}
