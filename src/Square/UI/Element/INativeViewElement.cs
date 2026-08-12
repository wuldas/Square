namespace Square.UI;

/// <summary>Marks an element whose platform-native view is synchronized after layout.</summary>
public interface INativeViewElement
{
    /// <summary>Receives the latest layout and visibility state for the native view.</summary>
    void SynchronizeNativeView(NativeViewLayout layout);
}
