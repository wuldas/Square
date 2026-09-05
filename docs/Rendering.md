# 渲染架构

> Document Revision: 0.6
> 配套：`Architecture.md`、`Graphics.md`、`Layout.md`
> `PushLayer` / `PopLayer` 在 Software 与 Skia 后端使用离屏表面、在 Direct2D 后端使用原生 layer，实现 group opacity/compositing；Software 后端会复用有界 layer buffer。其他后端可按自身能力实现同一接口语义。

---

## 1. 渲染模式

采用 **保留模式（Retained Mode）**。

不采用 Immediate Mode。

---

## 2. 管线

```
SQX
  ↓ (Source Generator, 编译期)
Component (C#)
  ↓ (组件构建)
Element Tree
  ↓ (Square.Rendering.Layout)
Layout (几何计算)
  ↓ (Square.Rendering)
Display Tree (DrawCommand 列表)
  ↓ (Square.Graphics)
IRenderContext
  ↓ (Square.Backends)
Backend (Software / Skia / Vulkan / Direct2D / ...)
```

Skia 后端位于独立的 `Square.Backends.Skia` 包，首版使用 CPU raster surface，并通过宿主已有的
BGRA 帧回调呈现。应用可调用 `window.UseSkiaBackend()` 选择它。该后端当前使用全帧渲染，
不声明 dirty-region 支持。项目会按 `SquareTargetPlatform` 选择 Win32 或 Linux 原生资产，后端包
应按目标平台构建；GPU Skia 与 NativeAOT 兼容性仍需单独验证。

注册 Skia 后端后，`TextMetrics` 会以 `SKFont.Metrics` 和 Skia glyph bounds 作为字体度量基准。
`TextLayout`、字符 fragment、选择区、光标、RichText 和 DrawCommand dirty bounds 共用该度量；
CSS `line-height` 仍控制行进距离，Skia font bounds 用于计算行盒内 baseline 与实际墨迹范围。

Direct2D 后端位于独立的 `Square.Backends.Direct2D` 包，仅支持 Windows/Win32。它使用
`ID2D1HwndRenderTarget` 直接绘制窗口，并由 DirectWrite 为支持的文本统一提供 shaping、字体回退、
line/cluster metrics、BiDi、命中测试、selection/caret 和 `DrawTextLayout`。系统字体与已加载的内存
`FontFace` 都进入同一权威 layout snapshot；Windows 8+ 的 letter/word spacing 使用
`IDWriteTextLayout1`，Windows 7 或暂不支持的 text-indent/布局边界整体回退 Square 原路径，不会只替换
绘制导致几何不一致。它支持 transform、矩形/几何 clip、渐变/描边、group opacity、bitmap 版本更新、
resize/DPI 和 `D2DERR_RECREATE_TARGET` 重建；当前只声明全帧渲染，也不提供真实 framebuffer readback。

DirectWrite 的 `TextFormat` cache 上限为 128 项，`TextLayout` cache 上限为 256 项/估算 16 MiB；
Direct2D fallback glyph cache 为 4096 项/8 MiB，image cache 为 256 项/64 MiB。动画 Bitmap 复用同一
`ID2D1Bitmap.CopyFromMemory`，预乘上传使用最大 256 KiB 分块缓冲，不再每帧分配整图 LOH 数组。
选区背景按视觉行合并连续 cluster，选择前景通过同一完整 layout 的 clip 重绘，避免字符间接缝和
substring 重排导致的 shaping 差异。

---

## 3. Element Tree

### 3.1 节点

- `Element`：基类型，持有几何、变换、可见性
- `UIElement`：带事件、输入、焦点的视觉节点
- 控件继承 `UIElement`

### 3.2 构建

- 由 Source Generator 生成的 `BuildElementTree()` 构建
- `<Show>` 条件子树支持**挂卸**
- `<For>` 列表支持**增量增删**（keyed）
- 命令式 `AppendChild`/`RemoveChild` 操作静态区域

### 3.3 脏标记

- 属性变化 → 标记节点脏
- 脏节点 → 触发 Layout → 触发 Display Tree 更新
- 增量更新，不全量重建

---

## 4. Layout 阶段

- 调用 `Square.Rendering` 程序集中的 `LayoutEngine` 计算几何
- 测量（Measure）→ 排列（Arrange）
- 高 DPI 物理像素对齐
- 详见 `Layout.md`

---

## 5. Display Tree

### 5.1 DrawCommand

| 命令 | 说明 |
|---|---|
| `FillRect` | 填充矩形 |
| `FillPath` | 填充路径 |
| `DrawText` | 绘制文本 |
| `DrawPath` | 描边路径 |
| `FillGeometry` | 填充矩形、圆角矩形、椭圆或 `PathGeometry` |
| `DrawGeometry` | 描边矩形、圆角矩形、椭圆或 `PathGeometry` |
| `DrawImage` | 绘制图片 |
| `PushClip` | 推入裁剪 |
| `PopClip` | 弹出裁剪 |
| `PushTransform` | 推入变换 |
| `PopTransform` | 弹出变换 |

### 5.2 构建

- Element Tree → Layout → 遍历生成 DrawCommand 列表
- 保留模式：脏区驱动增量重绘

### 5.3 提交

- 调用 `IRenderContext` 提交 DrawCommand
- Backend 负责实际绘制
- 模板内联 SVG 会先转换为 Geometry/Path 命令；Software 与 Vulkan 后端均通过
  `FillGeometry` / `DrawGeometry` 分派 `PathGeometry`，因此 `<path>`、`<polygon>`
  和 `<polyline>` 的填充与描边不需要后端理解 SVG DOM

---

## 6. 脏区与增量

### 6.1 脏区管理

- 节点几何变化 → 标记脏区
- 合并脏区减少重绘次数
- 仅重绘脏区范围内的 DrawCommand
- `VisualBounds` 使用 DrawCommand 的实际视觉范围，而不是只使用元素 `Geometry`
- Path、clip、transform、popup 等都会参与脏区计算，避免局部重绘漏绘或过度扩大
- Popup 内容使用 popup 局部坐标生成 DrawCommand；任一后代需要重绘时，脏区会提升到整个 Popup 视觉范围，包含全部 `box-shadow` 外阴影
- 宿主将窗口指针坐标映射到 Popup 内容坐标，并将文本光标矩形映射回窗口坐标后交给平台 IME

### 6.2 渲染模式

宿主支持三种渲染模式：

| 模式 | 说明 |
|---|---|
| `FullFrame` | 每帧全窗口清屏并重绘，默认模式，优先保证正确性 |
| `DirtyRegion` | 强制使用脏区局部重绘，用于压测和诊断脏区路径 |
| `Auto` | 根据 dirty rect 数量和面积比例自动选择脏区或全帧 |

`Auto` 会在以下情况回退全帧：

- layout dirty，需要重新布局
- 没有 dirty rect，但仍请求了渲染
- dirty rect 数量超过 `MaxDirtyRectCount`
- dirty area 比例超过 `MaxDirtyAreaRatio`

当前默认仍为 `FullFrame`，因为它是最稳定的正确性基线。DirtyRegion 和 Auto 用于逐步验证和优化局部重绘路径。

### 6.3 渲染诊断 Overlay

`DesktopApplication` 提供渲染诊断开关：

| 属性 | 说明 |
|---|---|
| `ShowRenderDiagnosticsOverlay` | 在窗口左上角绘制文字诊断信息 |
| `ShowDirtyUnionOverlay` | 在画面上绘制 dirty union 外框 |
| `LastRenderDiagnostics` | 最近一帧的渲染模式、决策原因、dirty 数量、面积比例和 union |

文字诊断 overlay 会显示：

- 当前 `RenderMode`
- 当前帧使用 full frame 还是 dirty region
- 决策原因，例如 `DirtyRegion`、`LayoutDirty`、`TooManyDirtyRects`、`DirtyAreaTooLarge`、`NoDirtyRects`
- dirty rect 数量
- dirty area 比例
- dirty union 矩形

Sample 支持命令行和环境变量配置：

```powershell
dotnet run --project "samples/Square.Sample/Square.Sample.csproj" -- --render-mode Auto --render-overlay true --dirty-overlay true
```

可用参数：

```text
--render-mode FullFrame|Auto|DirtyRegion
--render-overlay true|false
--dirty-overlay true|false
--max-dirty-area 0.35
--max-dirty-rects 16
```

对应环境变量：

```text
SQUARE_RENDER_MODE
SQUARE_RENDER_OVERLAY
SQUARE_DIRTY_OVERLAY
SQUARE_MAX_DIRTY_AREA
SQUARE_MAX_DIRTY_RECTS
```

Debug 构建的 `Square.Sample` 支持按 `F12` 切换 `ShowRenderDiagnosticsOverlay`。标题栏会显示当前状态：

```text
Square Framework - Overlay: On
Square Framework - Overlay: Off
```

### 6.4 子树挂卸

- `<Show>` 条件变化 → 子树挂载/卸载
- 挂载：构建 Element 子树 → Layout → 加入 Display Tree
- 卸载：从 Display Tree 移除 → 释放资源

### 6.5 列表增量

- `<For>` 列表变化 → keyed 增量增删
- 项移动时节点不重建，仅调整位置
- 项新增 → 创建节点；项删除 → 卸载节点

---

## 7. 后端切换

```
IRenderContext (抽象)
  ├── SoftwareBackend   (纯 C# CPU 渲染)
  ├── SkiaBackend       (SkiaSharp CPU 渲染)
  ├── VulkanBackend     (Silk.NET 原生 Vulkan)
  └── Direct2DBackend   (DirectNAot HWND RenderTarget)
```

- 同一 `IRenderContext` 接口
- 构建层裁剪决定装配哪个后端
- 切换后端不影响 Display Tree 逻辑

### 7.1 原生 Vulkan 后端

`Square.Backends.Vulkan` 直接基于 Silk.NET Vulkan API，实现 swapchain、render pass、pipeline、批处理、纹理 atlas、MSAA resolve 和可选 GPU readback。Win32 与 X11 宿主通过 `NativeTarget` 提供平台 surface 信息。

在主示例中启用：

```bash
dotnet run --project samples/Square.Sample/Square.Sample.csproj -- --backend Vulkan
```

应用自行注册时调用：

```csharp
app.UseVulkanBackend();
```

`DesktopApplication` 默认使用 Software 后端；只有调用该扩展且引用 `Square.Backends.Vulkan` 时才启用 Vulkan。

Vulkan 后端支持 NativeAOT。主示例的 Windows x64 AOT 发布命令：

```powershell
dotnet publish samples/Square.Sample/Square.Sample.csproj `
  -c Release `
  -r win-x64 `
  -p:SquareSamplePublishAot=true `
  -p:SquareSampleUseVulkan=true `
  -o artifacts/aot-vulkan-win-x64

artifacts/aot-vulkan-win-x64/Square.Sample.exe --backend Vulkan
```

Vulkan loader 由 AOT 安全的显式系统库加载器解析；shader 在构建前已编译为内嵌 SPIR-V，不依赖运行时代码生成或 shader 编译器。

`Square.Sample` 的 AOT 发布默认不引用 Vulkan；只有 `SquareSampleUseVulkan=true` 时才添加 Vulkan 项目引用和对应代码路径。不需要 Vulkan 的应用只需不引用 `Square.Backends.Vulkan`，NativeAOT 发布中不会包含 Vulkan 或 Silk.NET Vulkan 代码。

Vulkan 配置均在创建 RenderContext 前通过环境变量读取：

| 环境变量 | 值 | 默认行为 |
|---|---|---|
| `SQUARE_VULKAN_VALIDATION` | `1` / `true` | 关闭；开启时需要可用的 Vulkan validation layer |
| `SQUARE_VULKAN_READBACK` | `1` / `true` | 关闭；开启后 `CaptureRendererBitmapAsync()` 可读取真实 GPU 帧 |
| `SQUARE_VULKAN_MSAA` | `1` / `2` / `4` | 小于等于约 300 万物理像素时 4x，更大窗口 2x，并受设备能力限制 |
| `SQUARE_VULKAN_ATLAS_SIZE` | `512` / `1024` / `2048` | `1024` |
| `SQUARE_VULKAN_EXTRA_SWAPCHAIN_IMAGE` | `1` / `true` | 关闭；默认请求 surface 最小图像数 |

GPU readback 默认关闭，因为它需要额外的 host-visible buffer 和 GPU 到 CPU 拷贝。关闭时截图 API自动回退为 Software RenderContext 重放；开启时截图反映真实 Vulkan framebuffer。

Vulkan 描边回归场景可通过主示例的确定性页面运行。该页面覆盖锐角 miter、bevel/round join、三种 cap、跨折点 dash、闭合 dash、弧线和变换后的亚像素细线；`--verify-stroke-regression` 会验证关键颜色、dash 连通分量和抗锯齿混合像素：

```powershell
$env:SQUARE_VULKAN_READBACK = "1"
dotnet run --project samples/Square.Sample/Square.Sample.csproj -- \
  --stroke-regression \
  --verify-stroke-regression \
  --backend Vulkan \
  --screenshot artifacts/vulkan-stroke-regression.png
```

Shader 源码位于 `src/Square.Backends.Vulkan/Shaders/`，修改后运行以下命令重新生成内嵌 SPIR-V：

```bash
dotnet run --project tools/ShaderGen
```

### 7.2 原生 Direct2D 后端

Windows 应用引用 `Square.Backends.Direct2D` 后调用：

```csharp
window.UseDirect2DBackend();
```

主示例启用：

```powershell
dotnet run --project samples/Square.Sample/Square.Sample.csproj `
  -p:SquareTargetPlatform=Win32 `
  -p:SquareSampleUseDirect2D=true `
  -- --backend Direct2D
```

Win32 创建窗口时，`CreateWindowEx` 会在返回前同步派发消息；`WndProc` 在关联正在创建的宿主时先绑定 HWND，再处理消息，确保 `SizeChanged` 回调创建 Direct2D render target 时已能取得有效窗口句柄。

在 Windows 上从仓库根目录运行以下 PowerShell 命令，可复现 `.github/workflows/ci.yml` 的 Direct2D NativeAOT 发布、启动与截图 smoke（需安装 .NET SDK 及 Windows NativeAOT 构建工具链）：

```powershell
dotnet publish samples/Square.Sample/Square.Sample.csproj `
  --configuration Release --runtime win-x64 --self-contained true `
  -p:SquareTargetPlatform=Win32 `
  -p:SquareSamplePublishAot=true `
  -p:SquareSampleUseDirect2D=true `
  -o artifacts/nativeaot-direct2d

New-Item -ItemType Directory -Force "artifacts/screenshots" | Out-Null
$process = Start-Process -FilePath ".\artifacts\nativeaot-direct2d\Square.Sample.exe" -ArgumentList "--backend", "Direct2D", "--screenshot", "artifacts/screenshots/direct2d-aot.png" -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Direct2D NativeAOT sample exited with code $($process.ExitCode)." }
if (-not (Test-Path -LiteralPath "artifacts/screenshots/direct2d-aot.png") -or (Get-Item -LiteralPath "artifacts/screenshots/direct2d-aot.png").Length -eq 0) { throw "Direct2D NativeAOT screenshot was not created." }
```

该 smoke 验证 NativeAOT 程序可启动、创建 Direct2D HWND target 并完成截图流程。Direct2D HWND target 不实现
`IRenderBitmapSource`；`CaptureRendererBitmapAsync()` 因此使用 Software 重放，生成的 PNG 不能作为 Direct2D
原生像素正确性的证据。真实 HWND 后端一致性测试另用 `SQUARE_RUN_REAL_DIRECT2D_CONFORMANCE=1` 开启 Win32 窗口抓图（CI 筛选 `Category=RealDirect2D`）。截图完成时机及外部宿主的帧驱动要求见 [API Reference](API-Reference.md#devtools-input-records)。

---

## 8. 高 DPI

- 布局按逻辑像素，光栅按物理像素
- 物理像素对齐避免模糊
- 支持多显示器不同 DPI
- Win32 在创建窗口后读取所在显示器 DPI，并将配置的逻辑窗口尺寸转换为物理尺寸；布局继续使用逻辑客户区尺寸
- Software 和 Vulkan 文本按逻辑 advance 累计位置，只在 glyph 落点映射到物理像素时取整，避免 125% / 150% / 200% DPI 下的累计宽度漂移
- Vulkan 在普通轴对齐 DPI 变换下，将文本原点和 glyph offset 对齐到整数物理像素，避免已经抗锯齿的 coverage atlas 再被线性过滤一次
- 旋转、斜切或额外缩放的文本保留浮点几何和过滤路径
- Vulkan 曲线按变换后的物理半径自适应细分，避免大圆和圆角使用固定段数产生折角
- Vulkan 填充/描边椭圆和 path stroke 在边缘生成约 1 个物理像素的 alpha feather，细斜线不只依赖有限的 MSAA coverage level
- Vulkan path stroke 已支持 `Butt` / `Round` / `Square` LineCap、`Miter` / `Round` / `Bevel` LineJoin、`MiterLimit` 回退，以及带 `DashOffset` 的任意 Path dash；dash 可跨折点保留 join，闭合路径会合并跨接缝的首尾 dash，并有真实 GPU readback 描边回归场景验证复杂路径和 alpha feather

---

## 9. 性能目标

- 脏区增量重绘
- DrawCommand 列表复用
- 减少全量 Layout
- 高刷新率支持（60/120/144Hz）

### 9.1 内存生命周期

- Software framebuffer 使用托管 BGRA 数组；`Bitmap.Dispose()` 会立即断开像素数组引用，使 LOH framebuffer 在无其他引用时可回收
- Software RenderContext 在 DPI 变化时清理旧物理字号的 glyph coverage cache，并复用 dirty rect 与 polygon scanline 临时缓冲
- Win32 host 关闭时解除 RenderContext、最后一帧、事件委托和静态当前宿主引用，避免窗口关闭后继续根引用 framebuffer 与 glyph cache
- Vulkan atlas 仅保留 GPU 图像和紧凑 staging uploads，不保留完整 CPU atlas 镜像
- Vulkan readback、额外 swapchain image 和更高 MSAA 均为显式或受控配置，避免默认资源占用过高

### 9.2 Software 圆角与阴影

- 半透明圆角填充按完整圆角矩形一次光栅化，不再拆分为直边和四个圆角分别混合，避免阴影接缝处重复 alpha 或透明缺口。
- 完全位于圆角矩形主体横带或竖带内的像素走全覆盖快速路径。
- 只有外边缘和四个圆角执行 4x4 子像素采样；圆角边界、半径平方倒数和内侧切线在循环外预计算。
- 边缘热路径直接计算椭圆方程，不构造 `Point`，也不调用通用 `ContainsRoundedRect`、`Rect.Contains` 或重复 `Math.Clamp`。

### 9.3 Win32 首帧显示

Win32 窗口先以隐藏状态创建并完成 DPI 调整。`DesktopApplication` 创建 RenderContext、执行首次布局和 `Present()` 后，再调用平台宿主显示窗口。Software 和 Vulkan 因此都在窗口可见前准备好首帧，避免 DWM 短暂显示未初始化的黑色客户区。
