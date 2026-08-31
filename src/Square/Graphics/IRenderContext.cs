using System.Numerics;

namespace Square.Graphics;

/// <summary>
/// Present 回调：<paramref name="dirtyRects"/> 为 null 时表示整窗；
/// 非 null 时仅上传列表中的矩形（物理像素，与 Bitmap 同坐标系）。
/// </summary>
public delegate void PresentFrameHandler(Bitmap bitmap, IReadOnlyList<Rect>? dirtyRects);

/// <summary>渲染上下文创建参数。</summary>
public sealed class RenderContextCreateInfo
{
    /// <summary>逻辑画布尺寸。</summary>
    public required Size CanvasSize { get; set; }
    /// <summary>DPI 缩放比例（1 = 96dpi）。</summary>
    public float DpiScale { get; set; } = 1f;
    /// <summary>是否启用垂直同步。</summary>
    public bool VSync { get; set; } = true;
    /// <summary>帧呈现回调。</summary>
    public PresentFrameHandler? PresentFrame { get; set; }
    /// <summary>CPU 可写软件渲染表面。</summary>
    public ISoftwareRenderSurface? SoftwareSurface { get; set; }
    /// <summary>原生窗口渲染目标（用于 GPU 后端）。</summary>
    public INativeRenderTarget? NativeTarget { get; set; }
    /// <summary>后端需要宿主重新提交完整画面时调用。</summary>
    public Action? RequestRender { get; set; }
}

/// <summary>渲染上下文接口，由各后端实现。</summary>
public interface IRenderContext : IDisposable
{
    /// <summary>逻辑画布尺寸。</summary>
    Size CanvasSize { get; }
    /// <summary>DPI 缩放比例。</summary>
    float DpiScale { get; }
    /// <summary>是否支持局部脏区渲染。</summary>
    bool SupportsPartialRendering => false;

    /// <summary>压入变换矩阵。</summary>
    void PushTransform(Matrix3x2 matrix);
    /// <summary>弹出变换。</summary>
    void PopTransform();

    /// <summary>压入矩形裁剪。</summary>
    void PushClip(Rect rect);
    /// <summary>压入几何裁剪。</summary>
    void PushClip(Geometry geometry);
    /// <summary>弹出裁剪。</summary>
    void PopClip();

    /// <summary>填充矩形。</summary>
    void FillRect(Rect rect, Brush brush);
    /// <summary>描边矩形。</summary>
    void DrawRect(Rect rect, Pen pen);
    /// <summary>填充路径。</summary>
    void FillPath(PathGeometry path, Brush brush);
    /// <summary>描边路径。</summary>
    void DrawPath(PathGeometry path, Pen pen);
    /// <summary>填充几何图形。</summary>
    void FillGeometry(Geometry geometry, Brush brush);
    /// <summary>描边几何图形。</summary>
    void DrawGeometry(Geometry geometry, Pen pen);
    /// <summary>绘制文本。</summary>
    void DrawText(TextLayout text, Point origin, Brush brush);
    /// <summary>绘制图像。</summary>
    void DrawImage(Image image, Rect dest, Rect? source = null);

    /// <summary>压入透明度图层。</summary>
    void PushLayer(Rect bounds, float opacity);
    /// <summary>弹出透明度图层。</summary>
    void PopLayer();

    /// <summary>清除整个帧缓冲。</summary>
    void Clear(Color color);

    /// <summary>仅清除指定矩形（受当前 clip 约束）。</summary>
    void Clear(Color color, Rect rect);

    /// <summary>刷新后端命令队列。</summary>
    void Flush();

    /// <summary>整窗 Present。</summary>
    void Present();

    /// <summary>
    /// 局部 Present。空列表视为 no-op；null 视为整窗。
    /// </summary>
    void Present(IReadOnlyList<Rect>? dirtyRects);
}

/// <summary>可调整画布尺寸的渲染上下文。</summary>
public interface IResizableRenderContext
{
    /// <summary>调整画布尺寸。</summary>
    void Resize(Size canvasSize);
}

/// <summary>可调整画布尺寸和 DPI 的渲染上下文。</summary>
public interface IDpiResizableRenderContext : IResizableRenderContext
{
    /// <summary>更新逻辑画布尺寸和物理像素缩放。</summary>
    void Resize(Size canvasSize, float dpiScale);
}

/// <summary>可截取帧缓冲位图的渲染上下文。</summary>
public interface IRenderBitmapSource
{
    /// <summary>是否支持截取。</summary>
    bool IsCaptureAvailable => true;
    /// <summary>截取当前帧缓冲的独立副本。</summary>
    Bitmap CaptureBitmap();
}
