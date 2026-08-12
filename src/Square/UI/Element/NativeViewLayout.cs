using Square.Graphics;

namespace Square.UI;

/// <summary>布局完成后同步给原生视图的矩形、DPI 和可见性。</summary>
public readonly record struct NativeViewLayout(Rect Bounds, float DpiScale, bool IsVisible);
