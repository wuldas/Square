using Square.Graphics;
using Square.UI;

namespace Square.Controls;

/// <summary>文本显示控件，类似 HTML <c>span</c>。</summary>
public class Text : UIElement, ITextSelectable
{
    private readonly DomTextContent _domText;

    /// <summary>文本内容。</summary>
    public string TextContent { get => GetProperty<string>(nameof(TextContent)) ?? ""; set => SetProperty(nameof(TextContent), value); }
    /// <summary>文字颜色。</summary>
    public Color Color { get => Properties.HasValue(nameof(Color)) ? GetProperty<Color>(nameof(Color)) : Color.Black; set => SetProperty(nameof(Color), value); }
    /// <summary>字号（像素）。</summary>
    public float FontSize { get => Properties.HasValue(nameof(FontSize)) ? GetProperty<float>(nameof(FontSize)) : 16f; set => SetProperty(nameof(FontSize), value); }

    /// <summary>初始化 <see cref="Text"/> 的新实例。</summary>
    public Text() { _domText = new DomTextContent(this); }
    /// <summary>初始化 <see cref="Text"/> 的新实例并设置文本内容。</summary>
    public Text(string text) : this() { TextContent = text; }

    /// <inheritdoc/>
    public string SelectableText => TextContent;
    /// <inheritdoc/>
    public Rect SelectableTextBounds => ControlDrawing.GetTextBounds(this, TextContent, FontSize, Geometry.Position);

    /// <inheritdoc/>
    public override Size Measure(Size availableSize)
    {
        if (string.IsNullOrEmpty(TextContent)) return Size.Zero;
        return ControlDrawing.MeasureText(this, TextContent, FontSize, availableSize);
    }

    /// <inheritdoc/>
    public override void Paint(IRenderContext ctx)
    {
        if (string.IsNullOrEmpty(TextContent)) return;
        if (string.Equals(Style.Get("display")?.Trim(), "inline", StringComparison.OrdinalIgnoreCase) &&
            ElementLayoutStore.TryGet(this, out var layout) && layout.CssTextFragments is { } fragments)
        {
            foreach (var fragment in fragments)
                ControlDrawing.DrawText(ctx, this, fragment.Text, fragment.Bounds.Position, Color, FontSize,
                    maxSize: fragment.Bounds.Size,
                    direction: fragment.Direction,
                    unicodeBidi: fragment.UnicodeBidi,
                    wrappingOptions: new TextWrappingOptions(
                        TextWhiteSpaceMode.Nowrap,
                        ControlDrawing.ResolveTextLength(this, "letter-spacing", FontSize),
                        ControlDrawing.ResolveTextLength(this, "word-spacing", FontSize),
                        TextTransformMode.None,
                        0,
                        CollapseNewlines: false,
                        TextDecorationLines: ControlDrawing.ResolveTextDecorationLines(this)));
            return;
        }
        ControlDrawing.DrawText(ctx, this, TextContent, Geometry.Position, Color, FontSize, maxSize: Geometry.Size);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(string name)
    {
        base.OnPropertyChanged(name);
        if (name == nameof(TextContent))
            _domText.Text = TextContent;
    }
}

/// <summary>使用指定字体族绘制单色字形的通用图标控件。</summary>
public class FontIcon : Text
{
    /// <summary>要绘制的字体字形。</summary>
    public string Glyph
    {
        get => TextContent;
        set => TextContent = value ?? "";
    }

    /// <summary>图标字体族。</summary>
    public string FontFamily
    {
        get => GetProperty<string>(nameof(FontFamily)) ?? "";
        set => SetProperty(nameof(FontFamily), value ?? "");
    }

    /// <summary>初始化空图标。</summary>
    public FontIcon()
    {
        Style.Set("font-weight", "400");
        Style.Set("font-style", "normal");
        Style.Set("user-select", "none");
    }

    /// <summary>使用字体族和字形初始化图标。</summary>
    public FontIcon(string fontFamily, string glyph) : this()
    {
        FontFamily = fontFamily;
        Glyph = glyph;
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(string name)
    {
        base.OnPropertyChanged(name);
        if (name != nameof(FontFamily)) return;

        if (string.IsNullOrWhiteSpace(FontFamily))
            Style.Remove("font-family");
        else
            Style.Set("font-family", $"'{FontFamily.Replace("'", "\\'")}'");
    }
}
