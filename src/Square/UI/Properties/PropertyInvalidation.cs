namespace Square.UI.Properties;

using Square.UI;

internal static class PropertyInvalidation
{
    /// <summary>根据强类型属性名返回所需失效标志。</summary>
    public static ElementInvalidation ForProperty(string name)
    {
        var invalidation = name switch
        {
            "TextContent" or "Marker" or "ImageContent" or "Options" or "Items" or
                "Value" or "Placeholder" => ElementInvalidation.Layout,

            "Id" => ElementInvalidation.Style | ElementInvalidation.Layout | ElementInvalidation.HitTest,
            "IsChecked" or "IsDisabled" => ElementInvalidation.Style | ElementInvalidation.Paint,
            "SelectionBackground" or "SelectionForeground" => ElementInvalidation.Paint,
            _ => ElementInvalidation.Layout
        };
        return invalidation | ElementInvalidation.Style;
    }
}
