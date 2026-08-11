using System.Diagnostics;
using Square.Graphics;
using Square.UI;

namespace Square.Controls;

/// <summary>List item, similar to HTML <c>li</c>. Optional marker and text; may also host children.</summary>
public class ListItem : UIElement, ITextSelectable
{
    /// <summary>文本内容。</summary>
    public string TextContent { get => GetProperty<string>(nameof(TextContent)) ?? ""; set => SetProperty(nameof(TextContent), value); }
    /// <summary>文字颜色。</summary>
    public Color Color { get => Properties.HasValue(nameof(Color)) ? GetProperty<Color>(nameof(Color)) : Color.Black; set => SetProperty(nameof(Color), value); }
    /// <summary>字号（像素）。</summary>
    public float FontSize { get => Properties.HasValue(nameof(FontSize)) ? GetProperty<float>(nameof(FontSize)) : 16f; set => SetProperty(nameof(FontSize), value); }
    /// <summary>是否处于选中状态。</summary>
    public bool IsSelected { get => GetProperty<bool>(nameof(IsSelected)); set => SetProperty(nameof(IsSelected), value); }
    /// <summary>Bullet or number prefix, e.g. "• ", "1. ". Empty draws no marker.</summary>
    public string Marker { get => GetProperty<string>(nameof(Marker)) ?? "• "; set => SetProperty(nameof(Marker), value); }

    /// <summary>初始化 <see cref="ListItem"/> 的新实例。</summary>
    public ListItem() { }
    /// <summary>初始化 <see cref="ListItem"/> 的新实例并设置文本内容。</summary>
    public ListItem(string text) { TextContent = text; }

    /// <inheritdoc/>
    public string SelectableText => Marker + TextContent;
    /// <inheritdoc/>
    public Rect SelectableTextBounds => ControlDrawing.GetTextBounds(this, Marker + TextContent, FontSize, Geometry.Position);

    /// <inheritdoc/>
    public override Size Measure(Size availableSize)
    {
        var label = Marker + TextContent;
        if (string.IsNullOrEmpty(label.Trim()))
        {
            var childWidth = 0f;
            var childHeight = 0f;
            foreach (var child in Children)
            {
                var size = child.Measure(availableSize);
                childWidth = Math.Max(childWidth, size.Width);
                childHeight += size.Height;
            }
            return new Size(childWidth, childHeight);
        }

        return ControlDrawing.MeasureText(this, label, FontSize);
    }

    /// <inheritdoc/>
    public override void Paint(IRenderContext ctx)
    {
        var background = ControlDrawing.GetStyledColor(this, "background",
            IsSelected ? Color.FromRgb(0, 120, 212) : Color.Transparent);
        ControlDrawing.DrawStyledBackground(ctx, this, background);

        var label = Marker + TextContent;
        if (!string.IsNullOrEmpty(label.Trim()))
        {
            var foreground = IsSelected
                ? ControlDrawing.GetStyledColor(this, "color", Color.White)
                : ControlDrawing.GetStyledColor(this, "color", Color);
            ControlDrawing.DrawText(ctx, this, label, Geometry.Position, foreground, FontSize);
        }
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(string name)
    {
        base.OnPropertyChanged(name);
        if (name == nameof(IsSelected)) SetState(ElementState.Checked, IsSelected);
    }
}

/// <summary>Hyperlink-style control, similar to HTML <c>a</c>. Use <see cref="Href"/> for the target URL/path.</summary>
public class Link : UIElement, ITextSelectable
{
    private readonly DomTextContent _domText;

    /// <summary>文本内容。</summary>
    public string TextContent { get => GetProperty<string>(nameof(TextContent)) ?? ""; set => SetProperty(nameof(TextContent), value); }
    /// <summary>跳转目标 URL 或路径。</summary>
    public string Href { get => GetProperty<string>(nameof(Href)) ?? ""; set => SetProperty(nameof(Href), value); }
    /// <summary>文字颜色。</summary>
    public Color Color { get => Properties.HasValue(nameof(Color)) ? GetProperty<Color>(nameof(Color)) : Color.FromRgb(0, 102, 204); set => SetProperty(nameof(Color), value); }
    /// <summary>字号（像素）。</summary>
    public float FontSize { get => Properties.HasValue(nameof(FontSize)) ? GetProperty<float>(nameof(FontSize)) : 16f; set => SetProperty(nameof(FontSize), value); }
    /// <summary>是否显示下划线。</summary>
    public bool Underline { get => !Properties.HasValue(nameof(Underline)) || GetProperty<bool>(nameof(Underline)); set => SetProperty(nameof(Underline), value); }

    /// <summary>初始化 <see cref="Link"/> 的新实例。</summary>
    public Link()
    {
        _domText = new DomTextContent(this);
        AddEventListener("click", Activate);
    }
    /// <summary>初始化 <see cref="Link"/> 的新实例并设置文本内容。</summary>
    public Link(string text) : this() { TextContent = text; }
    /// <summary>初始化 <see cref="Link"/> 的新实例并设置文本和链接地址。</summary>
    public Link(string text, string href) : this() { TextContent = text; Href = href; }

    /// <inheritdoc/>
    public string SelectableText => TextContent;
    /// <inheritdoc/>
    public Rect SelectableTextBounds => ControlDrawing.GetTextBounds(this, TextContent, FontSize, Geometry.Position);

    /// <inheritdoc/>
    public override Size Measure(Size availableSize)
    {
        if (string.IsNullOrEmpty(TextContent)) return Size.Zero;
        var font = ControlDrawing.ResolveFont(this, FontSize);
        var measured = ControlDrawing.MeasureText(this, TextContent, FontSize);
        return new Size(ControlDrawing.MeasureRenderedTextWidth(TextContent, font), measured.Height);
    }

    /// <inheritdoc/>
    public override void Paint(IRenderContext ctx)
    {
        if (string.IsNullOrEmpty(TextContent)) return;
        var color = IsEnabled
            ? ControlDrawing.GetStyledColor(this, "color", Color)
            : Color.FromRgb(125, 130, 136);
        var origin = Geometry.Position;
        ControlDrawing.DrawText(ctx, this, TextContent, origin, color, FontSize);
        if (Underline)
        {
            var font = ControlDrawing.ResolveFont(this, FontSize);
            var underlineWidth = ControlDrawing.MeasureRenderedTextWidth(TextContent, font);
            var y = origin.Y + ControlDrawing.MeasureText(this, TextContent, FontSize).Height - 1f;
            ctx.FillRect(new Rect(origin.X, y, underlineWidth, 1f), new SolidColorBrush(color));
        }
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(string name)
    {
        base.OnPropertyChanged(name);
        if (name == nameof(TextContent))
            _domText.Text = TextContent;
    }

    /// <summary>激活链接，打开目标地址。</summary>
    protected virtual void Activate()
    {
        if (!IsEnabled || !TryGetExternalUri(Href, out var uri)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    private static bool TryGetExternalUri(string href, out Uri uri)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out var candidate) &&
            candidate.Scheme is "http" or "https" or "mailto")
        {
            uri = candidate;
            return true;
        }

        uri = null!;
        return false;
    }
}
