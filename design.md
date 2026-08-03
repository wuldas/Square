# Square Framework 设计文档

> 配套计划：`plan.md`（分阶段路线图、排期、风险、交付）  
> 需求来源：`docs/Requirements.md`（v0.3 Draft）  
> 架构重建记录：`docs/rebuild-plan.md`（已完成并合并至 main）  
> 状态：M0-M2 已完成。当前主线是 DOM 化 UI 模型、编译期模板生成、保留模式渲染、可插拔平台/后端，以及面向 NativeAOT 的纯 C# 运行时。

---

## 1. 项目定位与核心约束

Square 是 **纯 C# 实现、编译优先（Compile First）、NativeAOT 优先、渲染后端可插拔** 的跨平台 UI 框架。

核心原则：

1. **Compile First**：`.sqx` / `.sqv` 在编译期生成 C#，运行时零模板解析。
2. **Pure C# Core**：Parser、Generator、CSS、Layout、Runtime、Display Tree、Animation、Text 均以 C# 实现。
3. **NativeAOT First**：禁用 `Reflection.Emit`、运行时代码生成、`dynamic` 和运行时程序集加载；注册、绑定、平台分发均采用 AOT 友好机制。
4. **Backend Independent**：核心不依赖具体图形库；Software、Skia、Vulkan 等后端通过 `IRenderContext`/`IRenderBackendFactory` 接入。
5. **Retained Rendering**：使用 Document/Element Tree + Display Tree；布局与绘制由脏标记驱动，而非 Immediate Mode。
6. **Low Coupling / IDE Friendly**：模块间通过抽象接口通信；Source Generator 将 `.sqx`/`.sqv` 诊断映射回源文件行列。

设计边界：

- 不引入 JS 引擎、WebView 或 JSBridge 作为运行时 UI 基础。
- 不采用虚拟 DOM 或运行时模板 diff；结构化流程控制由生成器编译为命令式节点挂卸逻辑。
- 不采用反射式 / Proxy 响应式绑定；状态使用 `ObservableValue<T>`、`ObservableCollection<T>`、`Signal<T>` 和委托订阅。
- 平台与后端选择优先在构建层裁剪，运行时仅负责已注册平台/后端的实例化。

---

## 2. 当前架构总览

当前运行时主路径：

```text
.sqx / .sqv (template + script + style)
      |
      v
[Square.Compiler] -> C# partial component (compile time)
      |
      v
UIDocument
  documentElement = <UI>
    <Head>   metadata / title-bar extension point
    <Body>   application content host
      |
      v
  Component / UIElement tree
      |
      v
LayoutEngine (Square.Rendering)
      |
      v
DisplayTree + DrawCommand list
      |
      v
IRenderContext (Square.Graphics)
      |
      v
Backend + Platform host (Software/Win32/X11/...)
```

关键变化：旧的 `Visual` 类型与术语已经废弃；Square 当前按接近 Web API 的 `EventTarget -> Node -> Document | Element` 模型组织 UI。`Document` 不是 `Element` 子类，`UIDocument.DocumentElement` 是只读 `<UI>` 根，应用内容挂在 `document.Body` 下。

`DesktopApplication` 负责将 `UIDocument` 连接到平台宿主：注册默认控件、平台与后端，构建 Body 下组件树，执行生命周期，处理输入事件，运行 Dispatcher/Reconciler/CSS 更新队列，并根据渲染策略提交全帧或脏区绘制。

---

## 3. 程序集与模块职责

| 模块 | 当前职责 | 关键设计 |
|---|---|---|
| `Square.Markup` | `.sqx` 词法/语法解析与 AST | 独立解析器，错误含行列信息 |
| `Square.Compiler` | Roslyn Incremental Generator，`.sqx`/`.sqv` -> C# | `AdditionalText` 输入；Props 校验；诊断映射；扫描 `SqxDirective` 元数据构建指令目录 |
| `Square.Runtime` | 应用基类、Dispatcher、状态与绑定原语、指令元数据 | `ObservableValue<T>`、`ObservableCollection<T>`、`Signal<T>`、`SqxDirectiveAttribute` |
| `Square.Events` | DOM 风格事件模型 | `EventTarget`、`Event`、`addEventListener`、`dispatchEvent`、捕获/冒泡路径 |
| `Square.UI` | DOM 化文档树与 UI 元素基础 | `Node`、`Document`、`UIDocument`、`XMLDocument`、`SVGDocument`、`Element`、`UIElement`、SVG DOM、Shell、Range/Selection、Reconciler |
| `Square.Controls` | 内置控件、指令声明、基础动画 | View/Text/Button/Input/TextArea/CheckBox/Radio/Select/Image/Canvas 等；`Show`/`For`/`Switch`/`Match`/`Slot` 指令标记 |
| `Square.CSS` | CSS 解析、选择器、级联、主题、动画协调 | Selector/Cascade/Specificity/Var/Inheritance/Pseudo；`ThemeProvider`；`CssAnimationManager`；`CssStyleReconciler` |
| `Square.Rendering` | 布局、DisplayTree、DrawCommand、文本片段 | Box/Flex/Grid 布局；脏节点更新；命中测试；`TextFragment` 字符级命中 |
| `Square.Graphics` | 绘图抽象与图像编解码 | `IRenderContext`、`IRenderBackendFactory`、Color/Rect/Font/Image/Path；`SvgImage`；PNG 编码与 BMP 转换 |
| `Square.Backends` | 软件渲染后端与后端注册 | 纯托管 BGRA 软件渲染；脏区 Present；像素/裁剪缓存与批量填充优化 |
| `Square.Platform` | 平台宿主抽象与实现 | `IPlatformHost`、Win32、X11、截图；P/Invoke 使用 `LibraryImport` |
| `Square.Hosting` | 桌面应用组合层 | `DesktopApplication`、`RenderMode`、`RenderDecision`、`RenderDiagnostics` |
| `Square.Extensions.Routing` | 可选窗口路由 | Route matcher、nested RouterView、history、guards、KeepAlive、RouterLink |
| `Square.Extensions` | 可选扩展模块 | RichText、Routing 等可选控件 |
| `Square.Extensions.Markdown` | Markdown 扩展模块 | 基于 Markdig 的文档模型与 TextMate 代码块高亮 |
| `Square.Extensions.CodeEditor` | 代码编辑扩展模块 | PieceTable、视口虚拟化、TextMate 高亮、折叠、多光标与查找替换 |
| `Square.DevTools` | 本地 HTTP 调试与自动化 | localhost + token；renderer PNG 截图；指针、键盘、文本、滚轮输入注入 |
| `Square.Text` | 文本、字体、选择与测量基础 | Font manager、FontFaceSet、文本测量、caret/selection/hit test 基础 |

依赖方向遵循：核心抽象向下游开放，平台/后端/扩展在边缘注册；核心层不得反向依赖具体平台和具体图形库。

代码编辑器以 TextMate 作为低成本、错误容忍的实时高亮层，以轻量括号、XML 标签和 Python 缩进规则提供基础折叠。ANTLR4 不进入核心依赖；若后续需要诊断、符号、大纲或精确语法折叠，应作为可选语言服务扩展接入。

---

## 4. DOM 化 UI 模型

### 4.1 类型树

当前 UI 树以 DOM 子集为基础：

```text
EventTarget
  -> Node
       -> Document
            -> UIDocument
            -> XMLDocument
                 -> SVGDocument
       -> Element
            -> UIElement
                 -> UIRootElement   TagName "UI"
                 -> UIHeadElement   TagName "Head"
                 -> UIBodyElement   TagName "Body"
                 -> View, Text, Button, Input, ...
             -> HTMLElement          reserved abstract placeholder
             -> SVGElement
                  -> SVGSVGElement, SVGGElement, SVGPathElement, ...
```

`Node` 提供 `OwnerDocument`、`ParentNode`、`ParentElement`、`NodeTypeValue`、`NodeName` 与事件路径父级。`Element` 承载 DOM 风格身份和 Square 的保留模式扩展：`TagName`、`Id`、`ClassList`、`Style`、`ChildNodes`、`Children`、`Geometry`、`Measure`、`Arrange`、`Paint`、`HitTest`、脏标记与生命周期。

内联 `<svg>` 是嵌入文档根：`SVGSVGElement.SvgDocument` 管理 SVG 子树，`ContentType` 为 `image/svg+xml`。模板编译器直接生成浏览器式 SVG 元素类型；SVGDocument 负责内部查询、样式继承、变换、viewBox 与绘制，宿主 UIDocument 仅负责根 SVG 的布局。

### 4.2 UIDocument 壳

`UIDocument` 固定创建：

```text
UIDocument
  documentElement = <UI>
    <Head>
    <Body>
```

规则：

- `DocumentElement` 只读，UI 文档中始终是 `<UI>`。
- `Head` 当前作为元数据 / 标题栏扩展点，默认高度为 0。
- `Body` 是窗口客户区内容宿主，应用页面和根组件挂在 Body 下。
- 更换页面内容应操作 `Body.Children`，而不是替换 `documentElement`。
- `document.Title` 与平台窗口标题同步。

### 4.3 查询、Range 与选择

`Document` 已提供：

- `GetElementById`
- `GetElementsByTagName`
- `GetElementsByClassName`
- `QuerySelector` / `QuerySelectorAll`，支持 tag、`.class`、`#id`、后代与 `>` 子集
- `CreateRange()`
- `GetSelection()`
- `Fonts`，对齐 `document.fonts` / CSS Font Loading 的简化模型

`Range`、`Selection` 与 `TextFragment` 用于文本选择和字符级命中测试，是后续富文本与编辑器能力的基础。

### 4.4 事件系统

事件模型对齐 Web API 子集：

- `EventTarget` 负责监听、移除监听与派发。
- `Node.GetEventParent()` 形成冒泡路径：子元素 -> 父元素 -> `Body` -> `UI` -> `Document`。
- 平台输入在 `DesktopApplication` 中转换为鼠标、键盘、滚轮、文本输入、焦点和合成 Click 等事件。
- 元素状态位用于 CSS 伪类：hover、active、focus、disabled 等。

---

## 5. 组件、模板与 Source Generator

### 5.1 模板文件格式

Square 支持两类编译期模板输入：

- `.sqx`：Square 原生单文件模板。
- `.sqv`：Vue 风格模板前端，采用规范化路径，详见 `docs/vue-plan.md`。

`.sqx` 文件不使用文件级根标签，顶级 section 为：

- `<template>`：结构，含元素、文本、绑定表达式、事件、指令。
- `<script lang="csharp">`：可选且最多一个；包含 C# 逻辑、Props 声明和文件级元数据。
- `<style>`：可选且最多一个；样式交由 CSS 引擎消费。

示例：

```xml
<template>
  <View class="panel">
    <Text>{Title}</Text>
    <Show when={IsReady}>
      <Button onClick={Save}>Save</Button>
    </Show>
  </View>
</template>

<script lang="csharp" namespace="MyApp.Components" access="public">
  public ObservableValue<string> Title { get; } = new("Draft");
  public ObservableValue<bool> IsReady { get; } = new(false);
  void Save() { }
</script>

<style>
  .panel { padding: 12px; }
</style>
```

### 5.2 生成器职责

`Square.Compiler` 是 Roslyn `IIncrementalGenerator`：

1. 读取 `.sqx` / `.sqv` AdditionalText。
2. 解析模板、脚本和样式。
3. 从当前 Compilation 扫描带 `SqxDirectiveAttribute` 的类型，构建 `DirectiveCatalog`。
4. 校验 Props、ref 名称、指令嵌套和必需属性。
5. 生成 partial 组件类与 `BuildElementTree()`。
6. 将语法、Props、控件和指令错误映射回源模板行列。

Props 校验通过 `<script>` 中的 `[Prop]` 成员提取组件契约；模板中缺少 required prop 或字面值类型不匹配时生成诊断。

### 5.3 指令模型

结构原语不再只是生成器硬编码分支，而是由运行时/控件程序集声明元数据，再由生成器编译期扫描。

内置指令：

| 指令 | 语义 | 生成模式 |
|---|---|---|
| `Show` | 条件挂卸子树 | `ControlFlowAttach`，主属性 `when`，运行时节点 `ShowNode` |
| `For` | 列表挂卸/重建 | `ControlFlowAttach`，主属性 `each`，运行时节点 `ForNode` |
| `Switch` | 多分支容器 | 只允许 `Match` 子节点 |
| `Match` | `Switch` 分支 | 不能独立发射，主属性 `when` |
| `Slot` / `Outlet` | 插槽出口 | `SlotOutlet`，主属性 `name` |

这种设计允许后续 Router、Controls 或第三方扩展通过 `SqxDirectiveAttribute` 暴露编译期可识别的结构能力，同时保持 NativeAOT 友好。

### 5.4 绑定与状态

模板表达式统一使用 `{expr}`：

- 文本插值：`<Text>{Name}</Text>`
- 属性绑定：`text={Title}`、`value={UserName}`
- 事件绑定：`onClick={Save}`
- 显式双向：`value={UserName} onInput={OnUserNameChanged}`

运行时状态原语：

- `ObservableValue<T>`：组件内状态和模板属性绑定的基础。
- `ObservableCollection<T>`：列表和 `ForNode` 数据源。
- `Signal<T>`：跨组件或跨线程发布订阅；可通过 `Dispatcher` 投递到 UI 线程。
- `Reconciler` 和 `CssStyleReconciler`：批处理树更新与样式更新，避免每个状态变化立即完整重绘。

---

## 6. CSS、主题与动画

`Square.CSS` 当前覆盖：

- Tokenizer / Parser / AST
- 类型、类、id、后代、子代、兄弟、通用和完整基础操作符属性选择器
- Specificity、Cascade、`!important`
- CSS Variables、Inheritance
- 基础伪类和元素状态匹配
- 常用样式属性：颜色、背景、边框、padding、margin、字体、尺寸、布局相关属性
- `ThemeProvider` 主题变量
- `CssAnimationManager` / `CssAnimationTimeline`
- `CssStyleReconciler` 样式更新队列

CSS 只负责样式计算与属性应用；具体布局由 `Square.Rendering.LayoutEngine` 执行，绘制由元素 `Paint()` 产生命令并进入 DisplayTree。

仍需后续完善的方向：伪元素、完整 animation/keyframes 语义、更多 CSSOM API，以及命名空间属性和大小写修饰符等高级选择器语义。

---

## 7. 布局、DisplayTree 与渲染

### 7.1 布局

`LayoutEngine` 支持 Box/Flex/Grid 基础布局，输入为 `Element` 树和可用尺寸，输出写入元素 `Geometry`。

关键规则：

- `Body` 按平台客户区尺寸布局。
- `Head` 当前高度为 0，后续可扩展为自定义标题栏。
- 元素通过 `InvalidateLayout()` 和 `InvalidatePaint()` 标记更新。
- 高 DPI 方向要求布局与光栅阶段进行物理像素对齐，避免模糊。

### 7.2 DisplayTree

`DisplayTree` 将布局后的元素树转换为保留模式绘制树：

- `BuildFrom(root)` 用于布局失效或首次构建。
- `UpdateDirty()` 用于绘制失效节点更新。
- `CollectDirtyRects()` 收集局部重绘区域。
- `HitTestPopups()` 与元素 `HitTest()` 一起支撑输入命中。
- `Render(IRenderContext)` 或 `Render(IRenderContext, dirtyUnion)` 提交绘制命令。

### 7.3 渲染决策

`Square.Hosting` 提供渲染策略：

- `RenderMode.FullFrame`：每帧全窗口重绘。
- 脏区模式：收集 dirty rect，依据 `MaxDirtyRectCount` 和 `MaxDirtyAreaRatio` 判断局部 Present 还是退回全帧。
- `RenderDiagnostics` 记录本帧模式、原因、脏区数量、面积比例和 union。
- 可开启诊断 overlay 与 dirty union overlay 辅助性能调试。

Software Renderer 当前已经包含 BGRA 软件缓冲、预乘 Alpha、位图像素/裁剪区域缓存、批量 BGRA 填充和局部 Present 能力。SIMD、更多路径/文本高级光栅能力仍可继续扩展。

---

## 8. 平台宿主、截图与图形抽象

### 8.1 图形抽象

`Square.Graphics` 定义：

- `IRenderContext`
- `IRenderBackendFactory`
- `RenderBackendRegistry`
- 基础原语：`Color`、`Point`、`Size`、`Rect`、`Brush`、`Pen`、`Font`、`Image`、`PathGeometry`、`Transform`、`Clip`
- 图像编解码：`BitmapPngEncoder`、`BmpPngConverter`

图形抽象必须保持后端无关；后端不得泄漏平台或第三方库类型到核心 API。

### 8.2 平台抽象

`Square.Platform` 定义 `IPlatformHost`，负责：

- 窗口创建、显示与消息循环
- 客户区尺寸与 DPI 信息
- 鼠标、键盘、滚轮、文本输入、焦点事件
- 创建 `IRenderContext`
- 文本输入区域同步
- 平台截图

当前已有独立的 `Square.Platform.Win32` 与 `Square.Platform.X11` 实现程序集。应用引用目标平台后，在 `DesktopApplication.Run()` 前通过 `PlatformRegistry.Register(...)` 显式注册对应工厂；平台截图由已注册工厂实现的 `IPlatformScreenshotProvider` 提供。

### 8.3 Hosting 组合层

`DesktopApplication` 是桌面端组合入口：

```csharp
var document = new UIDocument { Title = "Square" };
document.Body.Children.Add(new Main());
new DesktopApplication(document, hostInfo).Run();
```

兼容构造函数仍支持传入单个 `Element` 内容根，内部会包装进新的 `UIDocument.Body`。新代码应优先使用 `UIDocument` 入口。

---

## 9. 路由、组合与扩展

### 9.1 路由

`Square.Extensions.Routing` 当前提供窗口级内存路由：

- `Router` 控件继承自 `View`。
- `RouteDefinition` 描述路径和组件工厂。
- `RouteMatcher` 支持参数、嵌套 branch 和匹配结果。
- `INavigationHistory` 默认使用 `MemoryNavigationHistory`。
- `Navigate`、`Replace`、`Back`、`Forward` 驱动当前页面。
- 路由上下文通过元素属性向页面分发。
- 嵌套路由通过默认 Slot 注入子页面。

### 9.2 组件组合

组件组合以 Slot 为基础：

- 调用方内容保持调用方作用域。
- `Slot` / `Outlet` 作为编译期指令发射。
- Router 嵌套分支复用默认 Slot 挂载子页面。

### 9.3 扩展模块

`Square.Extensions` 用于承载非核心能力。当前包含：

- Markdown 渲染扩展，基于 Markdig。
- RichText 前置能力：`RichTextDocument`、block/inline schema、`RichTextMarks`、block/offset selection、基础编辑状态、快照式 undo/redo，以及第一版 `RichTextEditor` 控件壳；已接入跨 run 行内布局、软换行、selection rect、精确 caret/hit test、视觉行键盘导航、Unicode grapheme 删除/移动、Ctrl+单词导航、Bold/Italic/Underline mark 命令，以及保留 block/run/marks 的 JSON 富文本片段复制粘贴。
- `samples/Square.Sample.RichText` 提供独立编辑器工作台，覆盖格式工具栏、颜色、清格式、撤销/重做、全选、样例加载和纯文本检查视图。

扩展模块应通过显式注册、指令元数据或控件工厂接入，避免核心层反向依赖。

---

## 10. NativeAOT 与裁剪策略

NativeAOT 约束贯穿设计：

- 组件由 Source Generator 生成，不做运行时模板解析。
- 控件、指令、平台和后端使用显式注册或编译期元数据扫描。
- P/Invoke 使用 `LibraryImport` 源生成。
- 绑定和事件使用强类型委托，不依赖反射属性访问。
- 构建层可通过 MSBuild `DefineConstants` 和条件 `ProjectReference` 裁剪平台/后端。

预留构建符号语义：

- 平台：`PLATFORM_WIN32`、`PLATFORM_X11`、`PLATFORM_MACOS`、`PLATFORM_ANDROID`、`PLATFORM_IOS`、`PLATFORM_WASM`
- 后端：`BACKEND_SOFTWARE`、`BACKEND_SKIA`、`BACKEND_BLEND2D`、`BACKEND_CAIRO`

当前默认注册路径适合开发与测试；发布裁剪应在样例和模板项目中继续细化。

---

## 11. 已完成里程碑摘要

已完成：

- M0：解决方案、项目结构、统一构建配置、AOT/Trim 基础配置。
- M1：`.sqx` 编译生成、基础控件、事件、绑定、Win32 宿主、Software Renderer、NativeAOT 样例验证。
- M2：CSS 能力扩展、Slot/组件组合、Signal/Dispatcher、主题与动画基础。
- 架构重建：去除 `Visual` 术语，迁移到 `Document`/`Node`/`Element`/`EventTarget` 模型。
- `.sqv` Vue 模板前端初步实现。
- 独立的 `Square.Extensions.Markdown` 与 `Square.Extensions.CodeEditor` 扩展。
- Win32/X11 平台截图、PNG 编码、BMP 转 PNG。
- DOM `Range`/`Selection` 文本选择模型与 `TextFragment` 字符级命中测试。
- Software Renderer 性能优化：位图像素/裁剪区域缓存、批量 BGRA 填充、脏区 Present。

历史 Phase 1 的详细任务清单已不再作为本文主体；路线图和排期以 `plan.md` 为准，重建细节以 `docs/rebuild-plan.md` 为参考记录。

---

## 12. 当前限制与后续方向

仍需推进：

- 条件 `ProjectReference` 和发布模板中的平台/后端裁剪继续细化。
- Skia / Vulkan 后端持续完善，并保持 `IRenderContext` API 稳定。
- macOS、移动端、WASM 平台宿主。
- 富文本编辑器深化：显式 caret affinity、系统剪贴板多 MIME、列表/链接/图片节点、Markdown/HTML import/export。
- 完整文本能力：BiDi、Font Fallback、复杂 selection/caret 行为。
- 完整 CSS Grid、伪元素、更多 CSSOM 与 animation/keyframes 语义。
- PointerEvent、KeyboardEvent 等事件字段继续对齐 Web API。
- 自定义标题栏：让 `Head` 从元数据区扩展为可布局、可点击/拖拽的标题栏区域。
- IDE 智能提示、补全和更完整的 Source Generator 诊断。

---

## 13. 关联文档

- `plan.md`：M0-M8 路线图、风险与交付说明。
- `docs/rebuild-plan.md`：DOM 化架构重建的目标、命名迁移和实现规格。
- `docs/Architecture.md`：架构说明。
- `docs/Rendering.md`：渲染管线说明。
- `docs/API-Reference.md`：API 参考。
- `docs/vue-plan.md`：`.sqv` Vue 风格模板前端计划。
- `docs/Requirements.md`：原始需求。
