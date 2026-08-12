using Square.UI;

namespace Square.Hosting;

internal static class NativeViewSynchronizer
{
    public static void Synchronize(Element root, float dpiScale)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!float.IsFinite(dpiScale) || dpiScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(dpiScale));

        SynchronizeElement(root, dpiScale);
    }

    private static void SynchronizeElement(Element element, float dpiScale)
    {
        if (element is INativeViewElement native)
        {
            var visible = element.IsVisible && element.IsEffectivelyVisible && element.IsCssDisplayed();
            native.SynchronizeNativeView(new NativeViewLayout(element.Geometry, dpiScale, visible));
        }

        foreach (var child in element.Children)
            SynchronizeElement(child, dpiScale);
    }
}
