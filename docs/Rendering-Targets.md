# 多目标渲染与宿主路线

> Document Revision: 0.1
> 配套：`Architecture.md`、`Rendering.md`、`Graphics.md`、`Roadmap.md`

本文规划 Square 从当前 Software Renderer 扩展到 WinUI XAML、HTML、Android UI、SVG、Godot 等目标时的架构边界和分阶段路线。目标不是立即实现所有目标，而是提前定义稳定的抽象，避免后续把“窗口宿主”“绘制后端”“原生 UI 输出”和“文件导出”混成一个不可维护的后端概念。

---

## 1. 目标与非目标

### 1.1 目标

1. 保留当前 `Element Tree -> Layout -> DisplayTree -> IRenderContext` 的稳定软件渲染路径。
2. 区分四类扩展目标：
   - 平台宿主：窗口、输入、消息循环、剪贴板、IME、生命周期。
   - 绘制后端：把 Square 的绘制命令画到 bitmap、GPU canvas 或平台 canvas。
   - 原生 UI 输出：把 Square 控件语义映射为 WinUI XAML、HTML DOM、Android View/Compose 等原生 UI 树。
   - 静态导出：把 DisplayTree 导出为 SVG、PDF、图片等文件格式。
3. 允许同一个控件按目标选择 native、canvas 或 fallback 实现。
4. 保持 NativeAOT 与 trim 友好：目标平台和后端通过显式注册、条件引用和构建层裁剪组合。

### 1.2 非目标

1. 不把所有目标强行塞进 `IRenderContext`。
2. 不要求 WinUI、HTML、Android 一开始完整复刻 Square 所有 CSS、布局和控件。
3. 不把 SVG/PDF 当成交互式平台宿主。
4. 不要求 Godot/Unity/Unreal 使用平台原生控件树；游戏引擎优先作为嵌入式宿主和 canvas 目标。

---

## 2. 四类目标的边界

```text
Square Component / Element Tree
        │
        ▼
Style / Layout / State / Event
        │
        ├─────────────────────────────┐
        │                             │
        ▼                             ▼
DisplayTree                     Native UI Tree
        │                             │
        │                             ├─ WinUI XAML
        │                             ├─ HTML DOM/CSS
        │                             └─ Android View/Compose
        │
        ├─ Software bitmap
        ├─ Skia / Vulkan / GPU canvas
        ├─ Godot Canvas
        ├─ SVG export
        └─ PDF export
```

| 类型 | 输入 | 输出 | 是否交互 | 示例 |
|---|---|---|---|---|
| 平台宿主 | 应用生命周期 | 窗口、输入、消息循环 | 是 | Win32、X11、WinUI host、Android host、Godot node |
| 绘制后端 | DisplayTree / DrawCommand | bitmap 或平台 canvas | 间接交互，输入由宿主处理 | Software、Skia、Godot Canvas |
| 原生 UI 输出 | 语义 UI 树 / Element Tree | 原生控件树 | 是 | WinUI XAML、HTML DOM、Android View/Compose |
| 静态导出 | DisplayTree | 文件或字符串 | 否 | SVG、PDF、PNG |

核心原则：**DisplayTree 适合绘制和导出；原生 UI 目标需要更高层的语义树。**

---

## 3. 当前管线与需要新增的抽象

当前主路径：

```text
.sqx / .sqv
  -> generated C# component
  -> Element Tree
  -> CSS cascade
  -> Layout
  -> DisplayTree
  -> IRenderContext
  -> Software Renderer
  -> Platform PresentFrame
```

这个路径适合 Software、Skia、SVG、Godot Canvas 等“绘制型目标”，但不适合直接输出 WinUI XAML 或 HTML DOM。原因是 DisplayTree 已经丢失了许多控件语义：按钮、输入框、滚动容器、可访问性角色、文本编辑模型、焦点策略等。如果从 DisplayTree 转 XAML/DOM，最终只能得到大量绝对定位的矩形和文字。

建议新增两个长期抽象：

```csharp
public interface IDisplayTreeExporter
{
    void Export(DisplayTree displayTree, DisplayTreeExportContext context);
}

public interface INativeUiAdapter<TNativeNode>
{
    TNativeNode Mount(NativeUiNode node, NativeUiMountContext context);
    void Update(TNativeNode nativeNode, NativeUiNode node, NativeUiUpdateContext context);
    void Unmount(TNativeNode nativeNode);
}
```

其中 `NativeUiNode` 不是 DrawCommand，而是保留控件语义的中间树：

```csharp
public sealed class NativeUiNode
{
    public string Kind { get; init; } = "";
    public Rect Bounds { get; init; }
    public StyleSnapshot Style { get; init; }
    public IReadOnlyList<NativeUiNode> Children { get; init; } = [];
    public Element? SourceElement { get; init; }
}
```

`SourceElement` 允许 adapter 读取控件状态、事件、可访问性和平台能力，但 adapter 不应直接修改 Element Tree；状态变更仍通过 Square 事件与绑定管线回写。

---

## 4. 推荐项目划分

```text
Square.Platform.WinUI       // WinUI 窗口、输入、剪贴板、消息循环
Square.Platform.Android     // Android Activity/View 宿主
Square.Platform.Godot       // Godot Node/Control 嵌入宿主

Square.Backends.Software    // 当前软件渲染，可保留在 Square.Backends
Square.Backends.Skia        // DisplayTree -> Skia canvas
Square.Backends.Godot       // DisplayTree -> Godot CanvasItem/RenderingServer

Square.Native.WinUI         // NativeUiNode -> XAML 控件树
Square.Native.Html          // NativeUiNode -> DOM/CSS
Square.Native.Android       // NativeUiNode -> Android View/Compose

Square.Export.Svg           // DisplayTree -> SVG
Square.Export.Pdf           // DisplayTree -> PDF
```

命名原则：

- `Platform.*` 负责宿主和输入，不负责控件语义输出。
- `Backends.*` 负责绘制，不负责窗口生命周期。
- `Native.*` 负责原生控件树，不负责静态导出。
- `Export.*` 负责文件/字符串输出，不处理交互输入。

---

## 5. WinUI 路线

WinUI 有两条路径，应分阶段推进。

### 5.1 阶段 A：WinUI 平台宿主 + Software Renderer

```text
Square UI
  -> DisplayTree
  -> Software Renderer
  -> bitmap
  -> WinUI Image / WriteableBitmap / SwapChainPanel
```

职责：

- `Square.Platform.WinUI` 创建 WinUI `Window`。
- 桥接 pointer、keyboard、text input、wheel、clipboard、DPI、resize。
- 继续使用现有 Software Renderer。
- `PresentFrame` 把 bitmap 刷新到 WinUI 视觉元素。

优点：

- 最小化风险。
- 不要求重写控件。
- 富文本、选择、高亮、Canvas 等现有行为保持一致。

风险：

- WinUI 3 的 `Application.Start` 和 DispatcherQueue 模型与当前 `DesktopApplication.RunCore()` 的同步 `Show()` / `CreateRenderContext()` / `PumpEvents()` 顺序不同。
- 可能需要为 WinUI 增加专用启动器或异步宿主管线，而不是强行塞进当前 Win32/X11 模型。

退出标准：

- 最小 SQV 示例可在 WinUI 窗口中显示。
- 鼠标、键盘、文本输入、窗口 resize 可用。
- 现有 Win32/X11 构建不受影响。

### 5.2 阶段 B：WinUI Native UI Adapter

```text
Element Tree / NativeUiNode
  -> WinUI Grid / StackPanel / TextBlock / Button / TextBox
```

优先映射：

| Square | WinUI |
|---|---|
| `View` | `Grid` / `StackPanel` / `Canvas` |
| `Text` | `TextBlock` |
| `Button` | `Button` |
| `Input` | `TextBox` |
| `Image` | `Image` |
| `ScrollViewer` | `ScrollViewer` |

复杂控件如 `RichTextEditor`、`Canvas` 和自定义绘图控件可以先 fallback 到 Software bitmap island，后续再做原生实现。

---

## 6. HTML / Web 路线

HTML 也不应从 DisplayTree 直接生成。更推荐：

```text
NativeUiNode
  -> DOM element
  -> CSS style
  -> browser layout / input / accessibility
```

可选输出模式：

| 模式 | 说明 | 适用场景 |
|---|---|---|
| Static HTML | 生成静态 DOM/CSS，不含运行时状态 | 文档、预览、服务端导出 |
| Interactive DOM | DOM 事件回写 Square runtime | WASM 或嵌入 WebView |
| Canvas fallback | DisplayTree -> `<canvas>` | 高一致性渲染、复杂控件 fallback |

重要设计点：

1. Square CSS 子集要映射到标准 CSS，而不是复制浏览器完整布局。
2. 若使用 browser layout，需要确认 Square layout 与 browser layout 的权威来源，避免双布局冲突。
3. 输入框、文本选择、IME、可访问性应优先使用原生 DOM 控件。
4. RichText 可以长期评估 `contenteditable`、自定义 DOM、Canvas fallback 三种实现。

---

## 7. Android 路线

Android 可以分为 View 方案和 Compose 方案：

```text
NativeUiNode -> Android View hierarchy
NativeUiNode -> Compose tree
DisplayTree  -> custom View canvas
```

建议阶段：

1. `Square.Platform.Android`：Activity + 自定义 View 宿主，复用 Software 或 Android Canvas 绘制。
2. `Square.Backends.AndroidCanvas`：DisplayTree 映射到 `Canvas` 绘制。
3. `Square.Native.Android`：基础控件映射到 View 或 Compose。

优先策略：

- 游戏、嵌入、截图一致性：DisplayTree -> Canvas。
- 应用 UI、输入、可访问性：NativeUiNode -> View/Compose。

风险：

- IME、软键盘、焦点、生命周期和 Activity 重建复杂。
- Android 的测量/布局协议与 Square layout 需要明确谁是权威。
- NativeAOT、Mono/Android 和 trim 规则需要单独验证。

---

## 8. SVG / PDF / 静态导出路线

SVG 属于 DisplayTree 导出，不属于平台宿主或原生 UI adapter。

这里的“SVG”特指将任意 Square `DisplayTree` 导出为 `.svg` 文件的 exporter，该能力仍在规划中。它与已经实现的两项 SVG 能力不同：

- `Square.UI.Svg`：SQX/SQV 模板中的内联 SVG DOM，由 `SVGDocument : XMLDocument` 管理并在应用内绘制。
- `Square.Graphics.Svg.SvgImage`：从 SVG 文件、流或字符串加载的静态矢量图片资源。

内联 SVG 和 `SvgImage` 都是输入/显示能力；`Square.Export.Svg` 则是把任意 Square 绘制结果转换为 SVG 的输出能力。

```text
DisplayTree
  -> SvgDisplayTreeExporter
  -> .svg
```

映射：

| Display command | SVG |
|---|---|
| 填充矩形 | `<rect>` |
| 圆角矩形 | `<rect rx="..." ry="...">` |
| 边框 | `<rect stroke="...">` 或 `<path>` |
| 文本 | `<text>` / `<tspan>` |
| 图片 | `<image href="data:...">` |
| 裁剪 | `<clipPath>` |
| 透明度 | `opacity` / group opacity |
| transform | `transform` |

文本策略：

| 模式 | 输出 | 优点 | 缺点 |
|---|---|---|---|
| Text mode | `<text>` / `<tspan>` | 可选择、可搜索、体积小 | 不同查看器字体排版可能略有差异 |
| Path mode | glyph path | 与截图更一致 | 体积大、不可编辑、需要 glyph outline |

建议默认使用 Text mode，提供精确导出选项：

```csharp
public enum SvgTextExportMode
{
    Text,
    GlyphPath
}
```

PDF 可复用类似的 DisplayTree exporter，但需要分页、字体嵌入、图片压缩和文本选择策略，建议在 SVG 之后推进。

---

## 9. Godot 路线

Godot 最适合作为嵌入式宿主和 canvas 绘制目标，而不是一开始映射成 Godot `Control` 树。

### 9.1 阶段 A：Godot 宿主 + Software bitmap

```text
Square UI
  -> Software Renderer
  -> Bitmap
  -> Godot ImageTexture / TextureRect
```

`SquareControl` 可以作为 Godot `Control` 节点：

```csharp
public partial class SquareControl : Control
{
    public override void _Ready()
    {
        // 初始化 Square document/application facade
    }

    public override void _Draw()
    {
        // 绘制 Square bitmap 或提交 Godot canvas draw calls
    }

    public override void _Input(InputEvent e)
    {
        // 转换 pointer/key/text/wheel 输入
    }

    public override void _Process(double delta)
    {
        // 驱动 dispatcher、timer、animation、QueueRedraw
    }
}
```

优点：

- 最快在游戏中嵌入 Square UI。
- 现有布局、文本、RichText、选择和高亮保持一致。

缺点：

- Godot 看到的是贴图，不是原生 Godot Control。
- 可访问性、主题、编辑器集成较弱。

### 9.2 阶段 B：DisplayTree -> Godot Canvas

```text
DisplayTree
  -> CanvasItem.DrawRect / DrawString / DrawTextureRect
```

映射：

| Square | Godot |
|---|---|
| Rect | `CanvasItem.DrawRect` |
| Border | `DrawLine` / `DrawPolyline` / StyleBox-like helper |
| Text | `Font.DrawString` / TextServer |
| Image | `DrawTextureRect` |
| Clip | clipping helper / SubViewport |
| Transform | Canvas transform |

### 9.3 阶段 C：Godot Control 树（长期）

Godot Control 映射应后置，因为会遇到双框架冲突：

- Square layout 与 Godot Container 谁负责布局？
- Square 事件冒泡与 Godot signal 如何桥接？
- hover、focus、pressed、disabled 状态谁维护？
- RichText、输入法、选择是否使用 Godot 原生能力？

只有当目标是深度 Godot 编辑器/主题集成时，才值得推进这一阶段。

---

## 10. 控件能力矩阵

每个控件应声明不同目标的支持方式：

| 控件 | Software | SVG | WinUI Native | HTML Native | Android Native | Godot |
|---|---|---|---|---|---|---|
| `View` | draw | group/rect | native layout | `div` | layout/view | canvas/control |
| `Text` | draw | `<text>` | `TextBlock` | text/span | `TextView` | draw text |
| `Button` | draw + events | static shape/text | `Button` | `button` | `Button` | canvas first |
| `Input` | custom text edit | static only | `TextBox` | `input` | `EditText` | software fallback |
| `Image` | draw bitmap | `<image>` | `Image` | `img` | `ImageView` | texture |
| `Canvas` | native Square draw | SVG subset | bitmap island | `<canvas>` | custom View | canvas |
| `RichTextEditor` | supported | static export | fallback/custom | contenteditable/custom | custom | software fallback |

支持等级建议：

```csharp
public enum TargetSupportLevel
{
    Unsupported,
    StaticOnly,
    FallbackBitmap,
    CanvasNative,
    PlatformNative
}
```

---

## 11. 分阶段实施计划

### P0：文档与命名稳定

- 明确 `Platform`、`Backend`、`Native`、`Export` 四类扩展的边界。
- 在 Roadmap 中加入多目标渲染路线。
- 避免新增“万能后端”接口。

退出标准：

- 文档约定稳定。
- 新项目命名不冲突。

### P1：WinUI 宿主实验

- 新增 `Square.Platform.WinUI` 实验项目。
- 使用 Software Renderer 输出 bitmap。
- 桥接窗口、resize、pointer、keyboard、text input、wheel。
- 处理 WinUI `Application.Start` 与当前 `DesktopApplication` 的运行模型差异。

退出标准：

- WinUI 示例可显示并响应基础输入。
- 不影响 Win32/X11 默认路径。

### P2：DisplayTree exporter 基础

- 新增 `IDisplayTreeExporter` 抽象。
- 新增 `Square.Export.Svg`。
- 支持 rect、border、text、image、clip、opacity、transform。
- 提供 text mode 和后续 glyph path mode 的选项。

退出标准：

- 同一示例可导出 SVG。
- SVG 可被浏览器打开。
- 基础视觉与软件渲染接近。

### P3：NativeUiNode 原型 ✅

- 从 Element Tree + layout 结果生成 `NativeUiNode`。
- 保留控件 kind、bounds、style snapshot、source element。
- 不要求立即实现完整 native adapter。

退出标准：

- 基础控件可生成稳定语义树。
- 控件状态和事件桥接点清晰。

### P4：WinUI Native adapter 原型

- 映射 `View`、`Text`、`Button`、`Input`、`Image`。
- 复杂控件使用 bitmap island fallback。
- 建立 native 控件事件回写 Square runtime 的规则。

退出标准：

- 基础表单示例可用原生 WinUI 控件运行。
- fallback 控件可与 native 控件混排。

### P5：HTML adapter 原型 🔄

- `Square.Native.Html` 已生成 static semantic HTML，并默认将最终样式去重为 head 中的 CSS class；可通过 `HtmlExportOptions.UseInlineStyles` 兼容旧的内联样式输出。
- `HtmlExportOptions.StylesheetHref` 可让页面引用外部 CSS；`HtmlExportResult.Css` 提供写入静态资源的完整 stylesheet 内容。
- `Square.Hosting.Web` 提供 `MapSquareStylesheet`，可与 `MapSquarePage` 配套暴露同一页面工厂生成的 CSS。
- `Square.Hosting.Web` 已支持 ASP.NET Core 每请求组件工厂，并可与桌面平台宿主共存。
- 可选支持 interactive DOM/WASM。
- Canvas/复杂绘制控件当前输出带诊断的占位节点，bitmap/canvas fallback 待实现。

退出标准：

- 基础页面可导出静态 HTML。✅
- ASP.NET Core Web Server 可按路由返回语义 HTML。✅
- 事件桥接方案形成最小闭环。待实现

### P6：Android / Godot 嵌入路线

- Android：先 custom View + Canvas/Software，再评估 View/Compose native adapter。
- Godot：先 `SquareControl` + Software texture，再实现 DisplayTree -> Godot Canvas。

退出标准：

- Android 最小 Activity 可显示 Square UI。
- Godot 示例可在场景中嵌入 Square UI。

---

## 12. 风险与缓解

| 风险 | 缓解 |
|---|---|
| 抽象过早复杂化 | 先做 WinUI host 和 SVG exporter 两个最小闭环，再固化接口 |
| DisplayTree 与 Native UI 语义边界不清 | DisplayTree 只承载绘制；NativeUiNode 保留控件语义 |
| 双布局冲突 | 每个 native adapter 明确 Square layout 或平台 layout 谁是权威 |
| 文本渲染不一致 | SVG/HTML 默认保留文本，提供 glyph path/canvas fallback 精确模式 |
| 输入法和文本编辑复杂 | Native UI 目标优先使用平台文本控件；绘制目标继续使用 Square 文本编辑 |
| NativeAOT/trim 破坏目标注册 | 使用显式注册、条件项目引用和构建常量，不做运行时程序集扫描 |
| Godot/Android 生命周期差异 | 为嵌入式宿主增加独立 facade，不强行复用桌面同步消息循环 |

---

## 13. 结论

Square 后续扩展不应只有一个“渲染后端”概念，而应分成：

```text
Platform Host    : Win32 / X11 / WinUI / Android / Godot
Drawing Backend  : Software / Skia / Godot Canvas
Native UI Adapter: WinUI XAML / HTML DOM / Android View or Compose
Exporter         : SVG / PDF / PNG
```

短期优先级建议：

1. WinUI 平台宿主，继续显示 Software bitmap。
2. SVG DisplayTree exporter，验证静态矢量导出。
3. NativeUiNode 原型，给 WinUI/HTML/Android native adapter 留出正确入口。
4. Godot 嵌入宿主，优先 Software texture，再演进到 Godot Canvas。

这样可以同时保留当前软件渲染的一致性，又为原生 UI、静态导出和游戏引擎嵌入留下清晰的演进空间。
