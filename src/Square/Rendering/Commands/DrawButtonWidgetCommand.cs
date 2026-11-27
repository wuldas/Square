using Square.Graphics;

namespace Square.Rendering.Commands;

/// <summary>按钮底座绘制：回放时按真实 DPI 选择整像素模板或圆角内描边。</summary>
internal sealed record DrawButtonWidgetCommand(Rect Bounds, Color Fill, Color Border) : DrawCommand;
