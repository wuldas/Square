using Square.Events;
using Square.Graphics;
using Square.UI;

namespace Square.Controls;

/// <summary>支持未选、已选和半选状态的复选框控件。</summary>
public class TriStateCheckBox : UIElement, ITextSelectable
{
    private bool _spacePressed;

    /// <summary>选中状态；false 为未选，true 为已选，null 为半选。默认 false。</summary>
    public bool? IsChecked
    {
        get => Properties.HasValue(nameof(IsChecked)) ? GetProperty<bool?>(nameof(IsChecked)) : false;
        set => SetProperty(nameof(IsChecked), value);
    }

    /// <summary>文本内容。</summary>
    public string TextContent { get => GetProperty<string>(nameof(TextContent)) ?? ""; set => SetProperty(nameof(TextContent), value); }

    /// <summary>初始化 <see cref="TriStateCheckBox"/> 的新实例。</summary>
    public TriStateCheckBox()
    {
        AddEventListener<KeyboardEvent>(StandardEvents.KeyDown, e =>
        {
            if (IsSpaceActivation(e)) e.StopPropagation();
        });
        AddEventListener<KeyboardEvent>(StandardEvents.KeyUp, e =>
        {
            if (e.KeyCode != 32 || !ReferenceEquals(e.Target, this)) return;
            _spacePressed = false;
            e.StopPropagation();
        });
    }

    /// <inheritdoc/>
    public string SelectableText => TextContent;

    /// <inheritdoc/>
    public Rect SelectableTextBounds => ControlDrawing.GetTextBounds(
        this,
        TextContent,
        14f,
        new Point(Geometry.X + GetLabelOffset(), Geometry.Y + (Geometry.Height - 17) / 2f));

    /// <inheritdoc/>
    public override Size Measure(Size availableSize)
    {
        if (ControlDrawing.UsesWidgetAppearance(this) && string.IsNullOrEmpty(TextContent))
            return new Size(13, 13);
        var text = ControlDrawing.MeasureText(this, TextContent, 14f);
        return new Size(GetLabelOffset() + text.Width, Math.Max(24, text.Height));
    }

    /// <inheritdoc/>
    public override void Paint(IRenderContext ctx)
    {
        if (ControlDrawing.UsesWidgetAppearance(this))
            CheckBoxDrawing.PaintIndicator(ctx, this, IsChecked);
        ControlDrawing.DrawText(ctx, this, TextContent,
            new Point(Geometry.X + GetLabelOffset(), Geometry.Y + (Geometry.Height - 17) / 2f), Color.Black, 14f);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(string name)
    {
        base.OnPropertyChanged(name);
        if (name == nameof(IsChecked))
        {
            SetState(ElementState.Checked, IsChecked == true);
            SetState(ElementState.Indeterminate, IsChecked == null);
        }
        else if (name == nameof(IsDisabled))
            _spacePressed = false;
    }

    /// <inheritdoc/>
    protected override void OnDefaultAction(Event e)
    {
        base.OnDefaultAction(e);
        if (!IsEnabled || e.DefaultPrevented) return;
        if (e.Type.Equals(StandardEvents.Click, StringComparison.OrdinalIgnoreCase))
        {
            IsChecked = IsChecked switch { false => true, true => null, null => false };
            DispatchEvent(StandardEvents.CreateChange());
        }
        else if (e is KeyboardEvent keyboard &&
                 e.Type.Equals(StandardEvents.KeyDown, StringComparison.OrdinalIgnoreCase) && IsSpaceActivation(keyboard))
        {
            e.PreventDefault();
            if (_spacePressed) return;
            _spacePressed = true;
            DispatchEvent(StandardEvents.CreateClick());
        }
    }

    /// <inheritdoc/>
    protected override void OnBeforeUnfocus()
    {
        _spacePressed = false;
        base.OnBeforeUnfocus();
    }

    /// <inheritdoc/>
    protected override void OnDetachedCore()
    {
        _spacePressed = false;
        base.OnDetachedCore();
    }

    private bool IsSpaceActivation(KeyboardEvent e) =>
        ReferenceEquals(e.Target, this) && e.KeyCode == 32 && !e.ControlKey && !e.AltKey;

    private float GetLabelOffset() => ControlDrawing.UsesWidgetAppearance(this) ? 16f : 26f;
}
