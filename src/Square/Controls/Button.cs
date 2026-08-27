using Square.Events;
using Square.Graphics;
using Square.UI;

namespace Square.Controls;

/// <summary>按钮控件，类似 HTML <c>button</c>。</summary>
public class Button : UIElement, ITextSelectable
{
    private readonly DomTextContent _domText;

    /// <summary>按钮文本内容。</summary>
    public string TextContent { get => GetProperty<string>(nameof(TextContent)) ?? ""; set => SetProperty(nameof(TextContent), value); }
    /// <summary>背景颜色。未设置时读取计算样式（UA 默认 <c>ButtonFace</c>）。</summary>
    public Color Background
    {
        get => Properties.HasValue(nameof(Background))
            ? GetProperty<Color>(nameof(Background))
            : ControlDrawing.GetStyledColor(this, "background", Color.FromRgb(240, 240, 240));
        set => SetProperty(nameof(Background), value);
    }
    /// <summary>前景（文字）颜色。未设置时读取计算样式（UA 默认 <c>ButtonText</c>）。</summary>
    public Color Foreground
    {
        get => Properties.HasValue(nameof(Foreground))
            ? GetProperty<Color>(nameof(Foreground))
            : ControlDrawing.GetStyledColor(this, "color", Color.Black);
        set => SetProperty(nameof(Foreground), value);
    }

    /// <summary>初始化 <see cref="Button"/> 的新实例。</summary>
    public Button()
    {
        _domText = new DomTextContent(this);
    }
    /// <summary>初始化 <see cref="Button"/> 的新实例并设置文本内容。</summary>
    public Button(string text) : this() { TextContent = text; }

    /// <inheritdoc/>
    public string SelectableText => TextContent;
    /// <inheritdoc/>
    public Rect SelectableTextBounds
    {
        get
        {
            var textSize = ControlDrawing.MeasureText(this, TextContent, 14f);
            return new Rect(
                Geometry.X + (Geometry.Width - textSize.Width) / 2f,
                Geometry.Y + (Geometry.Height - textSize.Height) / 2f,
                textSize.Width,
                textSize.Height);
        }
    }

    /// <inheritdoc/>
    public override Size Measure(Size availableSize)
    {
        var textSize = ControlDrawing.MeasureText(this, TextContent, 14f);
        return new Size(textSize.Width + 32, Math.Max(36, textSize.Height + 12));
    }

    /// <inheritdoc/>
    public override void Paint(IRenderContext ctx)
    {
        var foreground = ControlDrawing.GetStyledColor(
            this,
            "color",
            IsEnabled ? Color.Black : Color.FromRgb(235, 235, 235));
        var active = IsEnabled && HasState(ElementState.Active);
        var textSize = ControlDrawing.MeasureText(this, TextContent, 14f);
        var pressOffset = active && ControlDrawing.UsesWidgetAppearance(this) ? 1f : 0f;
        var textPosition = new Point(
            Geometry.X + (Geometry.Width - textSize.Width) / 2f,
            Geometry.Y + (Geometry.Height - textSize.Height) / 2f + pressOffset);
        ControlDrawing.DrawText(
            ctx,
            this,
            TextContent,
            textPosition,
            foreground,
            14f,
            maxSize: textSize);
    }

    /// <inheritdoc/>
    protected override bool RequiresStatePaintInvalidation(ElementState flag) =>
        flag == ElementState.Hover || base.RequiresStatePaintInvalidation(flag);

    /// <inheritdoc/>
    protected override void OnPropertyChanged(string name)
    {
        base.OnPropertyChanged(name);
        if (name == nameof(TextContent))
            _domText.Text = TextContent;
        else if (name == nameof(Background))
            Style.Set("background-color", ToCssColor(Background));
        else if (name == nameof(Foreground))
            Style.Set("color", ToCssColor(Foreground));
    }

    private static string ToCssColor(Color color) =>
        color.A == 255
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"rgba({color.R}, {color.G}, {color.B}, {(color.A / 255f).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)})";
}

/// <summary>复选框控件。</summary>
public class CheckBox : UIElement, ITextSelectable
{
    /// <summary>是否处于选中状态。</summary>
    public bool IsChecked { get => GetProperty<bool>(nameof(IsChecked)); set => SetProperty(nameof(IsChecked), value); }
    /// <summary>文本内容。</summary>
    public string TextContent { get => GetProperty<string>(nameof(TextContent)) ?? ""; set => SetProperty(nameof(TextContent), value); }

    /// <summary>初始化 <see cref="CheckBox"/> 的新实例。</summary>
    public CheckBox()
    {
        AddEventListener("click", ToggleFromInput);
    }

    /// <inheritdoc/>
    public string SelectableText => TextContent;
    /// <inheritdoc/>
    public Rect SelectableTextBounds => ControlDrawing.GetTextBounds(
        this,
        TextContent,
        14f,
        new Point(Geometry.X + 26, Geometry.Y + (Geometry.Height - 17) / 2f));

    /// <inheritdoc/>
    public override Size Measure(Size availableSize)
    {
        var text = ControlDrawing.MeasureText(this, TextContent, 14f);
        return new Size(26 + text.Width, Math.Max(24, text.Height));
    }

    /// <inheritdoc/>
    public override void Paint(IRenderContext ctx)
    {
        if (ControlDrawing.UsesWidgetAppearance(this))
        {
            var box = new Rect(Geometry.X, Geometry.Y + (Geometry.Height - 18) / 2f, 18, 18);
            ctx.FillRect(box, new SolidColorBrush(IsEnabled ? Color.White : Color.FromRgb(235, 235, 235)));
            ctx.DrawRect(box, Pen.FromColor(IsEnabled ? Color.FromRgb(118, 118, 118) : Color.FromRgba(118, 118, 118, 153)));
            if (IsChecked)
            {
                var fill = Color.TryParse("Highlight", out var highlight) ? highlight : Color.FromRgb(0, 120, 215);
                if (!IsEnabled)
                    fill = Color.FromRgba(fill.R, fill.G, fill.B, 153);
                ctx.FillRect(box.Inflate(-2, -2), new SolidColorBrush(fill));
                ctx.DrawPath(PathGeometry.Create()
                    .MoveTo(new Point(box.X + 4, box.Y + 9))
                    .LineTo(new Point(box.X + 8, box.Y + 13))
                    .LineTo(new Point(box.X + 15, box.Y + 5)),
                    Pen.FromColor(Color.White, 2));
            }
        }
        ControlDrawing.DrawText(ctx, this, TextContent,
            new Point(Geometry.X + 26, Geometry.Y + (Geometry.Height - 17) / 2f), Color.Black, 14f);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(string name)
    {
        base.OnPropertyChanged(name);
        if (name == nameof(IsChecked)) SetState(ElementState.Checked, IsChecked);
    }

    private void ToggleFromInput()
    {
        if (!IsEnabled) return;
        IsChecked = !IsChecked;
        DispatchEvent(StandardEvents.CreateChange());
    }
}

/// <summary>单选按钮控件，按 <see cref="GroupName"/> 分组。</summary>
public class Radio : UIElement, ITextSelectable
{
    /// <summary>是否处于选中状态。</summary>
    public bool IsChecked { get => GetProperty<bool>(nameof(IsChecked)); set => SetProperty(nameof(IsChecked), value); }
    /// <summary>文本内容。</summary>
    public string TextContent { get => GetProperty<string>(nameof(TextContent)) ?? ""; set => SetProperty(nameof(TextContent), value); }
    /// <summary>所属分组名称；同组单选按钮互斥。</summary>
    public string GroupName { get => GetProperty<string>(nameof(GroupName)) ?? ""; set => SetProperty(nameof(GroupName), value); }

    /// <summary>初始化 <see cref="Radio"/> 的新实例。</summary>
    public Radio()
    {
        AddEventListener("click", SelectFromInput);
    }

    /// <inheritdoc/>
    public string SelectableText => TextContent;
    /// <inheritdoc/>
    public Rect SelectableTextBounds => ControlDrawing.GetTextBounds(
        this,
        TextContent,
        14f,
        new Point(Geometry.X + 26, Geometry.Y + (Geometry.Height - 17) / 2f));

    /// <inheritdoc/>
    public override Size Measure(Size availableSize)
    {
        var text = ControlDrawing.MeasureText(this, TextContent, 14f);
        return new Size(26 + text.Width, Math.Max(24, text.Height));
    }

    /// <inheritdoc/>
    public override void Paint(IRenderContext ctx)
    {
        if (ControlDrawing.UsesWidgetAppearance(this))
        {
            var center = new Point(Geometry.X + 9, Geometry.Y + Geometry.Height / 2f);
            ctx.FillGeometry(new EllipseGeometry(center, 9, 9), new SolidColorBrush(IsEnabled ? Color.White : Color.FromRgb(235, 235, 235)));
            ctx.DrawGeometry(new EllipseGeometry(center, 9, 9), Pen.FromColor(IsEnabled ? Color.FromRgb(118, 118, 118) : Color.FromRgba(118, 118, 118, 153)));
            if (IsChecked)
            {
                var fill = Color.TryParse("Highlight", out var highlight) ? highlight : Color.FromRgb(0, 120, 215);
                if (!IsEnabled)
                    fill = Color.FromRgba(fill.R, fill.G, fill.B, 153);
                ctx.FillGeometry(new EllipseGeometry(center, 5, 5), new SolidColorBrush(fill));
            }
        }
        ControlDrawing.DrawText(ctx, this, TextContent,
            new Point(Geometry.X + 26, Geometry.Y + (Geometry.Height - 17) / 2f), Color.Black, 14f);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(string name)
    {
        base.OnPropertyChanged(name);
        if (name == nameof(IsChecked)) SetState(ElementState.Checked, IsChecked);
    }

    private void SelectFromInput()
    {
        if (!IsEnabled || IsChecked) return;
        if (Parent != null && !string.IsNullOrEmpty(GroupName))
        {
            foreach (var radio in Parent.QueryAll<Radio>())
                if (radio != this && radio.GroupName == GroupName) radio.IsChecked = false;
        }
        IsChecked = true;
        DispatchEvent(StandardEvents.CreateChange());
    }
}
