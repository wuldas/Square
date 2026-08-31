# IRenderContext API

> Document Revision: 0.3
> 配套：`Architecture.md`、`Rendering.md`

---

## 1. 定位

`Square.Graphics` 提供统一绘图抽象接口，不依赖 CSS / Controls / Component / Runtime。

所有 Backend 实现此接口。

---

## 2. 核心接口

### 2.1 IRenderContext

```csharp
public interface IRenderContext : IDisposable
{
    // 状态
    Size CanvasSize { get; }
    float DpiScale { get; }

    // 变换
    void PushTransform(Matrix3x2 matrix);
    void PopTransform();

    // 裁剪
    void PushClip(Rect rect);
    void PushClip(Geometry geometry);
    void PopClip();

    // 绘制
    void FillRect(Rect rect, Brush brush);
    void DrawRect(Rect rect, Pen pen);
    void FillPath(Path path, Brush brush);
    void DrawPath(Path path, Pen pen);
    void DrawText(TextLayout text, Point origin, Brush brush);
    void DrawImage(Image image, Rect dest, Rect? source = null);

    // 图层
    void PushLayer(Rect bounds, float opacity);
    void PopLayer();

    // 提交
    void Flush();
    void Present();
}
```

### 2.2 IRenderBackendFactory

```csharp
public interface IRenderBackendFactory
{
    string Name { get; }
    IRenderContext CreateContext(RenderContextCreateInfo info);
}
```

`RenderContextCreateInfo.NativeTarget` 将平台 HWND/X11 handle 交给原生后端；`RequestRender`
用于 target 丢失或平台曝光后请求宿主重新提交完整 DisplayTree。Win32 host 会把它转发为
`IPlatformHost.RenderRequested`，`DesktopApplication` 再进入现有全帧请求路径。

### 2.3 IRenderBitmapSource

```csharp
public interface IRenderBitmapSource
{
    bool IsCaptureAvailable => true;
    Bitmap CaptureBitmap();
}
```

渲染上下文可通过该接口提供活动帧截图。GPU 后端可以根据运行配置返回 `IsCaptureAvailable == false`；此时 `DesktopApplication.CaptureRendererBitmapAsync()` 使用 Software RenderContext 重放 Display Tree，而不是强制分配 GPU readback buffer。Direct2D HWND 首版不实现该接口，因此同样使用 Software 重放；需要验证真实 D2D 输出时使用平台窗口截图。

---

## 3. 绘图原语类型

### 3.1 Color

```csharp
public readonly struct Color
{
    public byte R, G, B, A;
    // 构造 / 转换 / 解析
}
```

### 3.2 Rect / Size / Point

```csharp
public readonly struct Rect { public float X, Y, Width, Height; }
public readonly struct Size { public float Width, Height; }
public readonly struct Point { public float X, Y; }
```

### 3.3 Matrix3x2

使用 `System.Numerics.Matrix3x2`。

### 3.4 Brush

```csharp
public abstract class Brush { }
public sealed class SolidColorBrush : Brush { public Color Color; }
public sealed class LinearGradientBrush : Brush { /* stops, direction */ }
public sealed class RadialGradientBrush : Brush { /* stops, center, radius */ }
```

### 3.5 Pen

```csharp
public sealed class Pen
{
    public Brush Brush;
    public float Width;
    public StrokeStyle StrokeStyle;
}

public sealed class StrokeStyle
{
    public float[] DashArray;
    public float DashOffset;
    public LineCap Cap;
    public LineJoin Join;
    public float MiterLimit;
}
```

### 3.6 Geometry / Path

```csharp
public abstract class Geometry { }
public sealed class RectGeometry : Geometry { public Rect Rect; }
public sealed class RoundedRectGeometry : Geometry { /* rect, radius */ }
public sealed class PathGeometry : Geometry
{
    public void MoveTo(Point p);
    public void LineTo(Point p);
    public void ArcTo(Rect oval, float startAngle, float sweepAngle);
    public void Close();
}
```

### 3.7 Image / Bitmap

```csharp
public abstract class Image : IDisposable { public int Width; public int Height; }
public sealed class Bitmap : Image { /* 像素数据 */ }
```

### 3.8 Font

```csharp
public sealed class Font
{
    public string Family;
    public float Size;
    public FontWeight Weight;
    public FontStyle Style;
}
```

### 3.9 TextLayout

```csharp
public sealed class TextLayout
{
    public string Text;
    public Font Font;
    public Size MaxSize;
    public TextAlignment Alignment;
    public Size Measure();
}
```

---

## 4. Backend 注册

### 4.1 编译期注册

通过构建层 `BACKEND_*` 宏控制装配：

```csharp
#if BACKEND_SOFTWARE
    RenderBackendRegistry.Register(new SoftwareBackendFactory());
#endif
#if BACKEND_SKIA
    RenderBackendRegistry.Register(new SkiaBackendFactory());
#endif

app.UseVulkanBackend();
app.UseDirect2DBackend();
```

### 4.2 RenderBackendRegistry

```csharp
public static class RenderBackendRegistry
{
    public static void Register(IRenderBackendFactory factory);
    public static IRenderBackendFactory Get(string name);
    public static IRenderBackendFactory Default { get; }
}
```

---

## 5. 不依赖

`Square.Graphics` 不依赖：

- CSS
- Controls
- Component
- Runtime

仅依赖基础类型（`System.Numerics` 等）。

---

## 6. M1 范围

- 接口定义 + 基础类型
- `SolidColorBrush` / `Pen` / `RectGeometry` / `PathGeometry`
- `Font` / `TextLayout` 基础
- Software Backend 实现
- `RenderBackendRegistry` 注册机制

---

## 7. 后续扩展

- 渐变 Brush（M2）
- 图层/混合模式（M2）
- 滤镜（M3+）
- 离屏渲染（M3+）
