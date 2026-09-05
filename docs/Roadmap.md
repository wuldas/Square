# 开发路线

> Document Revision: 0.4
> 配套：`Architecture.md`、`Rendering-Targets.md`、`plan.md`、`rebuild-plan.md`

---

## 1. 分阶段路线图

| 里程碑 | 目标 | 退出标准 | 状态 |
|---|---|---|---|
| **M0 脚手架** | 解决方案与全部 `Square.*` 空项目、目录规范、AOT/Trim 发布配置 | 空项目可编译 | ✅ 完成 |
| **M1 Phase 1 MVP** | 编译优先可运行 Demo：`.sqx`→C#、Props、ref、基础 CSS、flex 布局、纯 C# 软件渲染、基础控件、事件、Win32 宿主、构建层裁剪、生命周期、NativeAOT 验证 | `.sqx` 示例经 Source Generator 编译为 AOT 可执行，窗口渲染并响应交互；Props 传值校验、ref 操作、`<Show>`/`<For>` 可用 | ✅ 完成 |
| **M2 CSS 完整化 + 组件组合 + 动画 + 主题** | 默认/具名 Slot、fallback、嵌套组件；`Signal<T>` 跨组件/跨线程通信；完整 Selector/Cascade/Pseudo/Animation；Grid；Theme；元素查询 API | 插槽保持调用方作用域且不产生隐式布局容器；后台信号经 Dispatcher 安全送达 UI；CSS 测试套件通过 | ✅ 完成 |
| **M3 扩展控件 + 路由** | `Square.Extensions.Routing` 窗口路由、参数、通配符、嵌套 RouterView、守卫、KeepAlive；扩展控件 | 路由可前进/后退、守卫重定向并正确切换生命周期；各控件可交互 | ✅ 完成 |
| **M4 图形后端扩展** | Vulkan / Skia 后端完善（`IRenderContext` 不变） | 同一 Demo 切换后端渲染一致 | 🔄 Vulkan、Skia 已落地；后端合规测试起步 |
| **M5 跨平台桌面** | Linux(X11)、macOS 平台宿主；高 DPI/高刷新率打磨 | 三桌面平台 AOT 可执行均运行 | 🔄 X11 DPI/刷新率调度与 macOS Software MVP 已落地；macOS AOT/原生验收待完成 |
| **M6 移动端与 WebAssembly** | Android / iOS / WASM 平台层（最小实现） | 目标平台可启动并渲染基础 UI | 🔄 Android Experimental MVP 已实现，x86_64 emulator 的 IME/像素/性能/生命周期/无障碍与 Canvas/Skia/Vulkan smoke 已完成；arm64 真机待补；iOS/WASM 仍为计划 |
| **M7 文本与 Canvas 完整** | BiDi、Font Fallback、Caret/Selection/HitTest 完整、标准 RichTextBox/WYSIWYG 富文本模型与渲染、Canvas `CanvasRenderingContext2D` 兼容层→DrawCommand | 复杂文本/富文本编辑与 Canvas 绘图可运行 | ⏳ 计划 |
| **M8 工具链** | 完整 Source Generator 诊断、IDE 智能提示/补全、编译期检查、Debug Hot Reload | IDE 内 `.sqx` 报错可定位、可补全；桌面模板和组件样式可增量更新 | 🔄 桌面 Debug Hot Reload 已落地；补全与更多诊断继续推进 |
| **M9 多目标输出** | WinUI 宿主、HTML、SVG 导出、Native UI adapter、Godot 嵌入等多目标路线 | Software、Native UI、Export、Embedded Host 四类目标边界清晰，至少两个目标形成闭环 | 🔄 Static HTML/Web Server 已形成首个 Native UI 输出闭环 |

---

## 2. 排期建议（相对）

| 里程碑 | 预估 |
|---|---|
| M0 | 约 1 周 |
| M1 | 约 6–8 周（可并行：Generator/Markup 线、Graphics/Backend 线、Controls/Layout 线） |
| M2–M8 | 每个约 2–4 周，M1 验收后细化 |

---

## 3. M1 任务清单

[x] M0：创建 `Square.slnx` 与发布/AOT 配置；运行时逻辑模块现聚合到 `Square`
[x] `Square.Markup`：`.sqx` 解析器 + AST + 单测（严格顶级 section + script 元数据）
[x] `Square.Compiler`：Incremental Generator + Props 校验 + ref 生成 + 绑定编译 + 诊断映射
[x] `Square.CSS`：Tokenizer/Selector/Cascade/Variables/Inheritance（含子代/兄弟/通用/属性选择器、`!important`、基础伪类）
[x] `Square.Graphics`：`IRenderContext`/`IRenderBackendFactory` + 基础类型
[x] `Square.Backends`：纯 C# Software Renderer（BGRA、脏区提交、真正的 group opacity/offscreen compositing、SIMD 不透明 BGRA 行扫描与每上下文有界临时 layer surface 池化）
[x] `Square.Rendering`：Box/Flex/Grid 布局 + Element→DisplayTree→DrawCommand→提交；DisplayTree 已按 Element 标识增量同步插入、移除、显隐与顺序并复用未变化命令
[x] `Square.Runtime` + `Square.UI`：Application/Element/UIDocument 基类/属性/元素操作 API（Style/ClassList/Children/Event）
[x] `Square.Hosting`：`DesktopApplication` 聚合层——提取窗口、输入路由、焦点管理、文本编辑、剪贴板、帧调度和布局渲染循环
[x] `Square.Controls`：10 个第一阶段控件 + 结构原语（Show/For/Switch/Match）+ 默认样式 + 基础动画时钟/缓动
[x] `Square.Text`：FontManager/测量/绘制（基础）
[x] `Square.Platform.Win32` / `Square.Platform.X11`：独立平台宿主、输入泵、IME、剪贴板与截图
[x] 事件系统：Mouse/Keyboard/Focus/Wheel + `.sqx` 绑定 + Click 合成
[x] 绑定：`ObservableValue<T>` + `ObservableCollection<T>` + 生成期绑定
[x] Props：`[Prop]` 特性 + `ObservableValue<T>` 包装 + 编译期校验（必填 + 类型）+ `OnPropChanged`
[x] ref：模板标记 + 强类型字段生成 + 挂载/卸载赋值 + 重复名称诊断
[x] 示例 + NativeAOT 发布验证 + 基线指标（2.53 MiB EXE，512ms 启动，32 MB 内存）
[~] 构建层裁剪：C# `#if` + MSBuild `DefineConstants` + 条件 `ProjectReference` ✓ / 未声明 AOT 兼容的运行时包已启用 trim analyzer，第三方依赖与平台回调验证后再逐包声明兼容性
[x] 流程控制结构原语：`<Show>`/`<For>`/`<Switch>`/`<Match>` + `ObservableCollection<T>`
[x] 组件/应用生命周期钩子（OnAttached/OnDetached/OnLoaded/OnUnloaded + Application.OnStart/OnExit）

---

## 4. M2 任务清单

[x] 组件组合：默认/具名 Slot、fallback、嵌套组件，且 Slot 不产生隐式布局容器
[x] `Signal<T>` + `SignalHub`：跨组件通信、跨线程发布、Dispatcher 回到 UI 线程投递
[x] 元素查询 API：`Query<T>()` / `QueryAll<T>()` 基础类型 + 类查询
[x] CSS Selector/Cascade：组合选择器、属性选择器、伪类、`!important`、变量、继承
[x] CSS Animation 起步：`@keyframes` 解析 + `animation` 简写展开为 computed animation 属性
[x] Theme 起步：注册/切换主题变量，重应用样式时覆盖样式表变量
[x] Grid 起步：`display: grid`、`grid-template-*`、`fr`、`gap`、`grid-column`/`grid-row` span
[x] 尺寸单位扩展：`rem` / `em`，Grid 内 `min-content` / `max-content` / `fit-content` 基础测量
[x] Animation 运行时联动：`@keyframes` 可创建 timeline，支持 delay/iteration/direction 基础语义，`CssAnimationManager` 可自动扫描 Element Tree 并按 tick 更新样式属性
[x] Theme 完整体系起步：`ThemeProvider` 可切换主题、清理级联样式并自动重算整棵 Element Tree
[x] Grid 完整化起步：支持 `minmax()`、基础 auto-placement、`grid-template-areas` / `grid-area` 命名区域

---

## 5. 架构重建（Rebuild）✅ 已完成

重建分支已合并至 `main`。P0–P4 已完成；内置指令目录与发射管线已完成，第三方自定义指令的通用发射仍标记为实验性：

- [x] P0：文档规格
- [x] P1：DOM 事件系统（EventTarget / Event / addEventListener / dispatchEvent + 捕获/冒泡）
- [~] P1.5/D0–D4：内置指令目录与发射管线、第三方声明式条件指令及当前/引用程序集端到端测试 ✓ / 更复杂的第三方循环与分支模式仍待扩展
- [x] P2：去掉 Visual，Element 替代（EventTarget → Node → Element → UIElement）
- [x] P3：Document / UIDocument 壳（UI/Head/Body，documentElement 只读）
- [x] P4：DisplayTree / DisplayNode + HTMLElement 扩展点；SVGElement 后续已扩展为可绘制 SVG DOM

详见 `docs/rebuild-plan.md`。

---

## 6. 扩展能力（M2 之后增量）

M2 与架构重建完成后，以下能力作为增量落地，未归入既有 M0–M8 阶段编号：

- **`.sqv` Vue 模板前端**：在保留 `.sqx` 原生语法的前提下，新增 Vue 3 模板语法兼容前端。`SqvParser` 将 `{{ }}` 插值、`:prop` / `v-bind`、`@event` / `v-on`、`v-if` / `v-else-if` / `v-else`、`v-for` / `:key`、`ref` 及事件修饰符（`.stop` / `.prevent`）规范化为与 `.sqx` 相同的中间表示，运行时仍是纯 C#。配套 `samples/Square.Sample.Vue` 提供控件、表单、媒体、Markdown、路由、信号、溢出等示例页面。完整设计与后续里程碑见 `docs/vue-plan.md`。
- **`Square.Extensions.Markdown` 扩展模块**：从聚合扩展中独立，使用 Markdig 构建 Markdown 文档模型，围栏代码块通过 TextMate grammar 高亮；通过 `MarkdownRegistration.RegisterDefaults()` 注册。
- **`Square.Extensions.CodeEditor` 扩展模块**：提供 PieceTable、视口虚拟化、多光标、查找替换、代码折叠和 TextMate grammar 高亮；支持导入 VS Code 扩展 grammar，通过 `CodeEditorRegistration.RegisterDefaults()` 注册。
- **平台截图**：`PlatformScreenshot.CaptureByProcessId` / `TryCaptureByProcessId` 按进程 ID 捕获窗口位图，Win32 与 X11 各有实现，按构建层 `PLATFORM_*` 裁剪。
- **进程内 renderer 截图**：`DesktopApplication.CaptureRendererBitmapAsync()` 在 UI 线程将 DisplayTree 重放到离屏 Software bitmap，不依赖 PID、窗口枚举或桌面合成器；DevTools 与示例 `--screenshot` 默认使用该路径。
- **原生 Vulkan 后端**：基于 Silk.NET 实现 Windows/Win32 与 Linux/X11 surface、swapchain、批处理、纹理 atlas、MSAA、字体渲染和可选 GPU framebuffer readback；已支持 NativeAOT 系统 loader、内嵌 SPIR-V 与无动态代码的 validation callback。
- **Skia CPU 后端与后端合规测试**：`Square.Backends.Skia` 支持基础几何、渐变、描边、位图与文本绘制，并按 Win32/X11 条件携带对应 native assets；公共 conformance suite 统一验证 Software、Skia、Vulkan 工厂契约，并对可 headless 创建的 Software/Skia 验证 DPI、capture 与基础像素语义。Vulkan 已增加显式环境门控的真实窗口、present、readback 像素测试，由独立 self-hosted GPU 工作流执行。
- **Windows Direct2D / DirectWrite 后端**：`Square.Backends.Direct2D` 使用 DirectNAot 和 `ID2D1HwndRenderTarget` 直接绘制 Win32 HWND；DirectWrite 已统一支持文本的 shaping、系统/内存字体、line/cluster metrics、BiDi、命中测试、selection/caret 和绘制，并使用有界 format/layout cache。图片缓存有 64 MiB 预算，动画帧原位更新且分块预乘上传；几何 layer 使用实际 bounds。后端使用全帧渲染，设备目标丢失后由宿主请求 DisplayTree 重放，renderer 截图暂时回退 Software。D3D11/DXGI readback 与 dirty present 属于后续阶段。
- **DevTools NativeAOT**：移除 ASP.NET Core/Kestrel 依赖，改为 loopback `HttpListener`、显式路由与手写 JSON 序列化，主示例 AOT 发布可继续启用截图、输入注入和 Inspector。
- **PNG 编码与 BMP 解码**：`Square.Graphics.Codecs` 命名空间下，`BitmapPngEncoder` 将 `Bitmap` 编码为 8 位 RGBA PNG（zlib 压缩），`BmpPngConverter` 提供非压缩 24/32 位 BMP 加载与 BMP→PNG 转换，纯 C# 无外部依赖。
- **SVG 资源与模板 SVG DOM**：`Square.Graphics.Svg.SvgImage` 可从文件、流或字符串加载静态 SVG；SQX/SQV 可直接声明 `svg/g/path/rect/circle/ellipse/line/polyline/polygon`。每个根 `SVGSVGElement` 持有 `SVGDocument : XMLDocument`，内部 SVG 节点由该文档管理并通过现有矢量绘制命令渲染，支持 NativeAOT。
- **`Square.Images` 图片文档、控件加载与动画模块**：独立 packable 项目依赖核心 `Square.Graphics.Bitmap`，通过统一 `ImageDecoder.Decode(...) -> ImageDocument` 自动探测格式。已支持纯 C# PNG/APNG、基线 JPEG、BMP、GIF 多帧合成、ICO/CUR 全变体、Classic TIFF 多页面，以及 VP8L/VP8/ALPH 静态与动画 WebP；TIFF 支持未压缩/LZW/Deflate/Adobe Deflate/PackBits Strip 和 8 位 Predictor 2；WebP 支持 VP8X EXIF Orientation、ICCP/ALPH/EXIF/XMP flag/chunk 一致性、阶段顺序与累计元数据限制；VP8 关键帧支持分割、多 token partition、全部帧内预测、残差、量化与 loop filter；ALPH 支持 raw/VP8L 压缩、四种 filter 与 straight-alpha RGB 保留；动画支持 VP8L、VP8、ALPH+VP8 混合帧、局部矩形、alpha-over/no-blend、dispose-to-background 和单帧 loop metadata；GIF、APNG 和 WebP 覆盖帧时长、循环、透明、帧矩形、blend 与 disposal，ICO/CUR 暴露主变体、源位深与热点，JPEG/TIFF/WebP 支持 Exif/IFD Orientation。`<Image source="...">` 已通过核心加载器注册表异步加载本地文件、显式程序集嵌入资源与 HTTP/HTTPS，支持有界读取、取消、错误和动画可见性暂停；动画复用稳定 `Bitmap` 表面，Software Renderer 直接读取新像素，Vulkan 依据 `ContentVersion` 覆盖既有 atlas 区域。测试包含提交到仓库的 GIF/APNG、VP8L/VP8/ALPH 动画 WebP、静态 VP8 lossy 和透明 VP8+ALPH 文件、SHA-256 清单与 raw BGRA golden，并覆盖 Source 快速切换、取消、卸载、迟到结果释放以及 HTTP/嵌入资源加载。`Square.Images` 已通过本地 NuGet 包消费和 Windows x64 NativeAOT 原生发布/执行验证。后续增量包括 TIFF Tile 与更多颜色空间，以及完整 Exif/GPS/缩略图。
- **DOM `Range` 与 `TextFragment`**：`Square.UI.Range` 提供最小 DOM Range 文本选择模型（`SetStart` / `SetEnd` / `SelectNodeContents` / `Collapse` / 边界点比较）；`Square.Rendering.TextFragment` 提供字符级命中测试（`HitTestOffset`），为富文本编辑与选择奠定基础。
- **Software Renderer 性能优化**：`RenderContext` 缓存位图像素指针与尺寸、裁剪区域缓存（避免栈查找）、批量 BGRA 填充；`LayoutEngine` 与 `StyleAccessor` 同步优化。
- **`DesktopApplication.RenderingMode`**：新增 `RenderMode` 枚举（`FullFrame` / `Auto` / `DirtyRegion`），控制每帧重绘策略，可通过 `--render-mode` 参数或 `SQUARE_RENDER_MODE` 环境变量配置。
- **多目标渲染与宿主路线**：将后续 WinUI XAML、HTML DOM、Android UI、SVG、PDF、Godot 等目标拆分为 `Platform Host`、`Drawing Backend`、`Native UI Adapter`、`Exporter` 四类能力。完整计划见 `docs/Rendering-Targets.md`。
- **Static HTML、Web Server 与交互 DOM**：`Square.Native.Html` 从已求值 Element Tree 生成浏览器语义 HTML，并将最终样式默认去重为 CSS class；`Square.Hosting.Web` 提供无状态 `MapSquarePage` 和保留独立页面会话的 `MapSquareInteractivePage`。交互模式已打通 `click`/`input`/`change`、原生表单值同步、C# 事件派发、Reconciler/CSS 刷新和根节点替换；不注册或替换桌面 `PlatformRegistry`，可与 Win32/X11/macOS 宿主共存。复杂绘制控件仍输出带诊断的占位节点，SignalR/WASM 和细粒度 DOM diff 属于后续阶段。

---

## 7. 风险与缓解

| 风险 | 缓解 |
|---|---|
| Source Generator 增量缓存导致 IDE 诊断滞后 | 严格设计缓存键；单测覆盖 |
| 纯 C# 软件渲染性能不足 | 预乘 Alpha + SIMD + 脏区；M4 接 Skia |
| 完整 CSS/布局工作量巨大 | M1 仅子集，M2 扩展 |
| NativeAOT 裁剪误删后端/平台代码 | 构建层裁剪 + 显式注册 + trim 注解 |
| 文本引擎复杂 | M1 仅基础，M7 引入完整 BiDi/Fallback |
| Props/ref 生成器复杂度 | 先做基础形态，查询/高级操作后置 M2 |

---

## 8. 下一步

M2 与架构重建已完成，`.sqv` 前端、扩展模块、截图、PNG、文本命中测试与渲染优化等增量已落地。当前重点：

- M3 扩展控件（ScrollViewer、List、Tree、Swiper、Popup、Dialog、MenuBar/Menu/ContextMenu 已落地；基础范围完成）
- M4 Vulkan 描边收尾：`LineCap` / `LineJoin` / `MiterLimit`、任意 Path dash 与复杂路径抗锯齿场景已落地；真实 GPU readback 自动测试已门控，需配置带 Vulkan GPU 的 self-hosted runner
- Vulkan NativeAOT：Windows x64 原生发布、启动、GPU readback 与截图回归已有本地验证；持续 GPU 验收由独立工作流承接
- M5 跨平台完善（Win32/X11/macOS Software NativeAOT 发布、启动和 renderer 截图回归已加入 CI；macOS 真实原生交互仍需持续验收；X11 已支持 Xft/物理 DPI fallback 与 XRandR 刷新率驱动调度，后续继续完善多显示器动态 DPI）
- M6 Android：已落地非 MAUI、Canvas-first、Software-first、外部事件循环 `ApplicationSession`、统一 `PointerInput`、Activity/View host、触摸滚动、剪贴板、组合 IME、Android 原生字体回退、虚拟 accessibility tree、AndroidCanvas/AndroidSkia/Vulkan surface、独立 `Square.Android.slnx` 与 CI；x86_64 emulator 的全部非 arm64 门禁已完成，arm64 真机验证待补，实施与验收见 [Android-Platform-TODO.md](Android-Platform-TODO.md)
- M9 多目标输出：WinUI host + Software bitmap、SVG exporter、NativeUiNode 原型、Godot 嵌入宿主（见 `docs/Rendering-Targets.md`）
- M7 标准 RichTextBox/WYSIWYG：富文本 document model、per-range style、selection/run 映射；折叠选区已支持活动样式、相邻 run 样式继承及基础加粗/下划线/斜体操作，RichText 选区已映射到文档 DOM `Range`；BiDi 与复杂文本 shaping 仍待完成
- `.sqv` 前端继续推进：语言无关 `TemplateDocument` IR 入口、基于 `[SlotContract]` 的类型化 scoped slot 解构、动态/缺失 slot contract 诊断及 Roslyn 模板语义诊断已落地；下一步继续把剩余 `SqxNode` 兼容节点迁移为完整 Template IR 节点，并扩展跨模板联合语义绑定（见 `docs/vue-plan.md` 里程碑 F–G）
- 继续扩展 CSS Grid / Animation 到更完整规范

---

## 9. M5 跨平台桌面进度

[~] Linux / X11 平台宿主：窗口、消息循环、鼠标/键盘/滚轮、剪贴板（CLIPBOARD + PRIMARY 中键粘贴）、XIM/XIC 文本输入、Software Renderer 通过 `XPutImage` 上屏、原生 Vulkan surface/swapchain/present、构建层 `PLATFORM_X11` 裁剪与交叉发布、Xft/物理 DPI fallback、可选 XRandR 刷新率调度 ✓ / 完整 IME preedit 与候选窗定位、多显示器动态 DPI 待完善
[~] 跨平台字形栅格化：Windows 走 GDI `GetGlyphOutline` ✓ / Linux 与 macOS 走 StbTrueTypeSharp（纯 C#，无 native 依赖）✓ / 字体回退按脚本（CJK/日/韩）映射到 Noto/Source Han ✓ / Fontconfig 集成待实现
[~] macOS 平台宿主：AppKit 窗口、主线程事件泵、鼠标/键盘/滚轮、Software Renderer 上屏、动态尺寸/Retina scale、窗口状态、Unicode 剪贴板与基于 `NSTextView` 的 IME composition/candidate rect 已落地 ✓ / 真实 macOS 交互回归和多显示器动态 DPI 持续验收待完成
[~] 高 DPI / 高刷新率打磨：X11 逻辑/物理坐标和 fractional DPI、刷新率 deadline 调度 ✓ / per-monitor DPI、呈现反馈与热插拔验收待完善

---

## 10. M6 Android 规划状态

[x] 首期边界：.NET 10 for Android、非 MAUI、单 Activity / 单 Square View、Canvas/Software 绘制
[x] 运行模型：先从 `DesktopApplication` 抽出外部事件循环可驱动的共享 `ApplicationSession`，不为 Android 伪造阻塞 `PumpEvents()`
[x] 稳定等级：启动 MVP、交互 MVP、Beta、Stable 候选采用不同退出门，不以“能显示首帧”宣称完整支持
[x] 发布约束：Android NativeAOT 在官方仍标记实验时只做非阻断记录，正式门禁使用平台支持的 Release trimming/AOT
[~] Phase 0：Android workload、SDK platform/build-tools、JDK17、像素探针与 arm64 APK/AAB 已验证；x86_64 emulator smoke 与性能证据已完成，非紧凑 stride/lockPixels 仍不在核心 Bitmap API
[~] Phase 1–2：ApplicationSession、按需帧调度、PointerInput 和 touch 路由已实现；桌面输入专项回归待补
[~] Phase 3–4：Android host、Activity/View、触摸滚动、fling、Back 与剪贴板已实现；Canvas/Skia/Vulkan 后端和 x86_64 emulator smoke 已完成，arm64 conformance 待补
[~] Phase 5–6：Android 字体、软键盘、中文组合输入、性能采样和资源清理路径已实现；生命周期压力已完成，arm64 IME/压力待补
[~] Phase 7：APK/AAB、Android CI、trimming + profiled AOT 已实现并验证；`PublishAot=true` 仍受 XA1040/IL3053 官方实验限制
[~] Phase 8：虚拟 accessibility tree、AndroidCanvas/Skia/Vulkan 与 profiling 已实现；NativeUiNode 原生 adapter、TalkBack 多设备矩阵和更深度性能优化后置

完整任务、文件边界、风险和验收矩阵见 [Android-Platform-TODO.md](Android-Platform-TODO.md)。
