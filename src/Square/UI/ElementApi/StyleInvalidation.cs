namespace Square.UI.ElementApi;

using Square.UI;

internal static class StyleInvalidation
{
    /// <summary>根据 CSS 属性名返回所需失效标志。</summary>
    public static ElementInvalidation ForProperty(string property)
    {
        property = StyleAccessor.NormalizePropertyName(property);
        if (property.StartsWith("--", StringComparison.Ordinal))
            return ElementInvalidation.Paint;

        return property switch
        {
            "background" or "background-color" or "box-shadow" or "color" or "border-color" or "border-top-color" or
                "border-right-color" or "border-bottom-color" or "border-left-color" or "border-style" or
                "border-top-style" or "border-right-style" or "border-bottom-style" or "border-left-style" or
                "border-radius" or "border-top-left-radius" or "border-top-right-radius" or
                "border-bottom-right-radius" or "border-bottom-left-radius" or "appearance" or "caret-color" or "outline" or
                "outline-color" or "outline-style" or "outline-width" or "outline-offset" or
                "text-decoration" or "text-decoration-color" or "text-decoration-line" or "text-decoration-style" or
                "opacity" or "selection-background" or "selection-color" => ElementInvalidation.Paint,

            "z-index" or "visibility" or "overflow" or "overflow-x" or "overflow-y" or "user-select" or "cursor" =>
                ElementInvalidation.Paint | ElementInvalidation.DisplayTree | ElementInvalidation.HitTest,

            _ when IsLayoutProperty(property) => ElementInvalidation.Layout,
            _ => ElementInvalidation.Layout
        };
    }

    private static bool IsLayoutProperty(string property) => property is
        "display" or "width" or "height" or "min-width" or "min-height" or "max-width" or "max-height" or
        "margin" or "margin-left" or "margin-top" or "margin-right" or "margin-bottom" or
        "padding" or "padding-left" or "padding-top" or "padding-right" or "padding-bottom" or
        "border" or "border-width" or "border-left-width" or "border-top-width" or "border-right-width" or "border-bottom-width" or
        "font" or "font-size" or "font-family" or "font-weight" or "font-style" or "line-height" or
        "letter-spacing" or "word-spacing" or "text-indent" or "text-transform" or "white-space" or
        "list-style" or "list-style-type" or "list-style-position" or "list-style-image" or
        "flex" or "flex-direction" or "flex-wrap" or "flex-grow" or "flex-shrink" or "flex-basis" or
        "justify-content" or "align-items" or "align-self" or
        "grid" or "grid-template-columns" or "grid-template-rows" or "grid-template-areas" or
        "grid-column" or "grid-row" or "grid-area" or "grid-column-span" or "grid-row-span" or
        "gap" or "row-gap" or "column-gap" or
        "position" or "left" or "top" or "right" or "bottom";
}
