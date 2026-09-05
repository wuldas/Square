# Square Android 平台支持 TODO

> Document Revision: 0.5
> Status: Experimental MVP 已实现（除 arm64 真机验证外的 emulator、像素、性能、生命周期、IME、accessibility、Canvas/Skia/Vulkan 和 profiled AOT 门禁已验证；官方 `PublishAot=true` 仍为实验能力）
> Planning Baseline: 2026-09-04
> 配套：`Architecture.md`、`Rendering-Targets.md`、`Roadmap.md`、`Rendering.md`

本文定义 Square Android 平台支持的边界、依赖顺序、验收门禁和已知不支持项。仓库已包含 Android host、Software/Canvas/Skia/Vulkan 呈现路径、Android Sample、独立 `Square.Android.slnx` 和 Android CI；除 arm64 真机验证及官方实验 NativeAOT 生产门外，本机 emulator 门禁已完成，仍不能据此宣称 Android 已达到 Beta 或 Stable。

---

## 1. 目标

首期在原生 Android Activity 中运行现有 Square UI 管线：

```text
.sqv / .sqx
  -> generated C# component
  -> Element Tree
  -> CSS / Layout
  -> DisplayTree
  -> Software RenderContext
  -> Square BGRA Bitmap
  -> Android Bitmap / View Canvas
```

必须形成以下闭环：

- 启动和显示真实生成组件；
- density、尺寸和旋转后的布局正确；
- tap/click、单指滚动、fling、Back、硬件键盘和剪贴板可用；
- Input/TextArea 可连接软键盘，Beta 前支持中文 IME composition；
- pause/resume/destroy 不泄漏 View、Bitmap、InputConnection 或帧回调；
- 可构建、安装和运行 x86_64 Debug APK 与 arm64 Release APK/AAB；
- 不破坏 Win32、X11、macOS 现有构建、输入和渲染行为。

---

## 2. 当前状态

### 2.1 已有基础

- [x] Platform、Backend、Hosting 和核心 UI 管线已经分层。
- [x] Software Renderer 可输出 BGRA32 Bitmap，并支持 dirty-region raster。
- [x] `IRenderContext` 不依赖 Android、Win32 或 X11 类型。
- [x] 已有 Mobile scrollbar profile：4 DIP overlay thumb，不占 gutter，不参与命中。
- [x] 已有 `NativeUiNode` 快照，可供长期 Android Native UI adapter 评估。
- [x] 路由、Popup/Dialog、文本编辑器、图片和动画已有平台无关实现基础。

### 2.2 尚未具备或尚未验收

- [x] Android target、平台项目、Activity/View host 和 Sample（代码已落地）。
- [x] Activity/Looper 可驱动的分步应用生命周期（ApplicationSession + Choreographer）。
- [x] pointer id/type/cancel 等统一触摸输入契约（桌面 MouseEvent 仍保留兼容）。
- [x] touch slop、drag-cancel-click、fling 和 Android Back 桥接（x86_64 emulator 基础回归已验证，完整边界与 arm64 设备回归仍待验收）。
- [x] Android `InputConnection` 与 composition-aware 文本输入契约（emulator 英文与自包含中文组合 IME 回归已验证，第三方/arm64 IME 仍待验收）。
- [x] Android 系统字体发现和 generic family 映射（emulator 系统字体与 CJK fallback 已验证，arm64 glyph fallback 待验收）。
- [x] Android Bitmap 呈现通道及颜色、stride、alpha、DPI 证据（像素探针与 emulator 首帧/旋转截图已验证；核心 Bitmap 使用紧凑 stride）。
- [x] Android workload、APK/AAB、emulator 和 arm64 CI/设备门禁（workload/APK/AAB/CI、x86_64 emulator smoke 已有，arm64 设备门禁待补）。
- [x] Canvas-only View 的虚拟 accessibility tree（uiautomator 与 TalkBack 服务绑定已验证；完整 TalkBack 人工/自动语音矩阵仍待扩展）。

### 2.3 现有阻塞证据

| 阻塞 | 当前实现 | 影响 |
|---|---|---|
| 同步消息循环 | `ApplicationSession` + Activity/Looper/Choreographer 外部驱动 | 已解除；Android 不调用 `PumpEvents()` |
| 一次性应用生命周期 | `ApplicationSession.Attach/ProcessFrame/Suspend/Resume/Detach` | 已解除；50 次 pause/resume、20 次旋转、锁屏/解锁和 finish/reopen 压力已通过 |
| 鼠标中心输入 | `PointerInput`、`PointerEvent` 和 `AndroidInputAdapter`；`MouseEvent` 保留兼容 | 已解除 Android 接入阻塞；桌面统一迁移回归待补 |
| committed text only | `ITextInputClient` + `AndroidInputConnection` 组合文本路径 | 已解除；emulator 中文组合输入已通过 |
| 无 Android 字体根 | Android 原生 Typeface 回退 + `/system/fonts` 等根目录 | 已解除；emulator CJK glyph 已通过 |
| 桌面窗口 API | Android host 对最小化、最大化、还原、拖动显式抛出 `PlatformNotSupportedException` | 已明确不支持 |
| 构建拒绝 Android | `SquareTargetPlatform=Android`、`PLATFORM_ANDROID` 和 Android RID 校验 | 已解除 |
| GPU surface 缺失 | `AndroidCanvas`、`AndroidSkia`、`Vulkan` 可选呈现路径 | 已解除代码与 emulator smoke 阻塞；设备 conformance 仍待补 |

---

## 3. 已确定的首期决策

1. **非 MAUI**：使用 .NET 10 for Android 与原生 Activity/View，避免为一个平台 host 引入 MAUI UI 栈。
2. **Canvas-first**：首期只挂载一个 Square 自定义 View，不做 Android View/Compose 控件映射。
3. **Software-first 与可选直绘**：Software Bitmap 仍为默认兼容路径；Android Canvas、Skia surface 和 Vulkan 通过显式后端名启用。
4. **外部事件循环**：Android 由 Activity/Looper/`Choreographer` 驱动；不为 Android 伪造阻塞 `PumpEvents()`。
5. **共享运行内核**：从 `DesktopApplication` 抽出 session，桌面与 Android 共用；禁止复制桌面运行时。
6. **单 Activity / 单根 View**：首期不承诺多 Activity、多窗口或 Fragment/Compose 容器集成。
7. **最低版本默认 API 26**：Phase 0 可根据实际产品覆盖范围调整；compile/target SDK 跟随锁定的 .NET 10 workload。
8. **NativeAOT 非阻断**：正式门禁使用 .NET for Android 支持的 Release trimming/AOT；实验 `PublishAot=true` 单独记录。
9. **Beta 需要中文 IME**：只支持英文 committed text 不足以进入 Beta。
10. **Stable 需要 accessibility**：Canvas-only View 未实现虚拟语义节点前，只能标记 experimental/Beta。

---

## 4. 程序集与项目边界

### 4.1 首期新增

```text
src/Square.Platform.Android/          Android Activity/View、输入、IME、剪贴板、帧调度和 host
src/Square.Backends.AndroidCanvas/    Android Canvas 与 Skia surface 后端
src/Square.Backends.Vulkan/           Android `VK_KHR_android_surface` 目标（复用现有 Vulkan backend）
samples/Square.Sample.Android/        真实 SQV/SQX Android Sample
Square.Android.slnx                   仅包含 Android 所需项目，隔离 workload
.github/workflows/android.yml         Android build、trimming/AOT 与设备门禁

Android host/backend 项目可以依赖 `Square`；核心不得反向依赖 Android SDK。

### 4.2 首期不新增

```text
Square.Native.Android
Square.Backends.AndroidVulkan（独立程序集不创建，Android surface 位于现有 Vulkan backend）
其他原生 View/Compose adapter
```

只有 profiling 或原生语义需求证明必要时才分别立项，不能把 host、drawing backend 和 native adapter 混成一个程序集。

### 4.3 长期边界

| 项目 | 职责 | 首期状态 |
|---|---|---|
| `Square.Platform.Android` | Activity/View host、生命周期、输入、IME、剪贴板、frame scheduling、bitmap present | Experimental MVP 已实现；arm64 设备门禁待验收 |
| `Square.Backends.AndroidCanvas` | Android Canvas `Picture` 与 Skia `SKCanvasView` 直绘 | emulator smoke 已实现 |
| `Square.Native.Android` | `NativeUiNode` 映射为 Android View/Compose 语义控件树 | 不创建 |
| `Square.Backends.Vulkan` Android surface | `ANativeWindow`、`VK_KHR_android_surface`、swapchain、present/readback | emulator smoke 已实现；arm64 conformance 待验收 |

---

## 5. 运行模型

### 5.1 Surface host 与 window host 分离

计划从当前 `IPlatformHost` 中提取平台 surface 能力，最终名称暂定 `IPlatformSurfaceHost`：

- 客户区逻辑尺寸与 DPI；
- pointer、wheel、key、text-input 事件；
- 剪贴板与输入法候选区；
- render context 创建与 resize；
- frame tick/render request；
- attach/suspend/resume/detach 所需资源。

桌面 `IPlatformHost` 在此基础上保留：

- title、window state、native handle；
- Show/Close/Minimize/Maximize/Restore/BeginMove；
- 阻塞 `PumpEvents()`。

Android host 只实现 surface/lifecycle 语义，不提供假的桌面窗口能力。

### 5.2 共享 ApplicationSession

`ApplicationSession` 复用 `DesktopApplication` 的运行内核，由外部宿主驱动：

| 操作 | 责任 |
|---|---|
| Attach | 构建文档、注册 CSS scope、attach/load、创建 render context |
| ProcessFrame | Dispatcher/Reconciler 更新、布局、DisplayTree、render/present |
| Tick | 推进 CSS 动画、控件帧请求与 caret；需要时提交画面 |
| Suspend | 暂停动画、caret 和 frame callback；保留文档与控件待处理帧请求 |
| Resume | 重置时间基线并请求重绘，继续暂停前的控件帧请求 |
| ReleaseRenderContext | 仅在已附加且暂停时释放 renderer；不卸载文档，恢复后重新创建 |
| FramePresented | 成功提交给后端后通知宿主刷新 View，包括 Tick 内部直接提交的帧；不是系统显示完成回调 |
| Detach | unload/detach、取消输入、释放 renderer 和回调 |

`DesktopApplication` 继续负责 PlatformRegistry、窗口创建和 `PumpEvents()`；Android Activity/View 直接驱动 session。抽取后现有桌面公开 API 和生命周期顺序必须保持兼容。

### 5.3 按需帧调度

Android 使用 `Choreographer`，但空闲时不得永久 60Hz Tick：

- 所有 element invalidation、Dispatcher 工作、CSS/图片动画、caret 和 backend replay 都汇聚为 frame demand；
- `HasPendingFrame` 包含当前树仍在运行的 CSS 动画，不能只检查一次性 dirty 标记；
- host 同时最多保留一个 frame callback；
- 延迟帧保留最早 deadline；
- pause/detach 撤销 callback；
- resume 只在有待处理工作时重新调度；
- Dispatcher 队列由空变非空时唤醒 UI thread，回调不得在队列锁内执行。

---

## 6. 渲染与 DPI
### 6.1 首期路径
```text
DisplayTree
  -> Software RenderContext
  -> reusable Square BGRA Bitmap
  -> reusable Android Bitmap
  -> SquareView.OnDraw(Canvas)

DisplayTree -> AndroidCanvas backend -> Android Picture -> View Canvas
DisplayTree -> AndroidSkia backend -> SKPicture -> SKCanvasView surface
DisplayTree -> Vulkan backend -> ANativeWindow -> Android swapchain
```

### 6.2 必须验证
- [x] BGRA 与 Android ARGB_8888 的通道顺序（像素探针：`FF010203`）。
- [x] premultiplied alpha 语义（像素探针：`800A141E` 保持通道和 alpha）。
- [~] stride 与非紧凑行宽（探针覆盖核心 Bitmap 的紧凑 stride `12` 和跨行复制；核心 Bitmap API 当前固定 `width * 4`，不存在非紧凑输入构造路径）。
- [x] dirty rect 的逻辑像素/物理像素转换（探针验证局部更新且非脏像素保持 `FF415263`）。
- [x] `1 Square DIP = 1 Android dp`，backing bitmap 尺寸为逻辑尺寸乘 density（emulator 420 dpi，presenter `1080x2274`）。
- [x] Software/Vulkan 的文本断行与测量统一使用逻辑坐标，字距和词间距不再与物理像素混用；density 2.625 下单行按钮及正常多行文本均已验证。
- [x] Button 按内容区宽度测量和绘制；自动高度随行数增加，显式高度保持不变。Sample 新增宽度 180 DIP、行高 22 DIP、未指定高度的长文本按钮，Software/Vulkan 均显示 4 行，高度 112 DIP。
- [x] rotation/density resize 后旧 bitmap/render context 正确释放（20 次旋转压力无 crash）。
- [x] 每帧不创建 Bitmap、ByteBuffer 或大数组（presenter 只在尺寸变化时重建，性能日志验证复用）。
- [x] 已记录局部 raster 仍需全量 Android bitmap upload；性能日志记录 `uploadBytes` 与耗时。
首期 Software 路径保留一次受控 CPU copy；AndroidCanvas、AndroidSkia 和 Vulkan 直绘路径已实现，是否继续引入区域 upload/`AndroidBitmap_lockPixels` 由后续设备 profiling 决定。

### 6.3 后端选择与升级
当前后端选择已冻结为显式能力：Software 兼容路径、AndroidCanvas/AndroidSkia 直绘路径和 Vulkan `ANativeWindow` 路径均可用。后续 profiling 只决定默认路径与更细粒度 upload 优化，不再以未实现 backend 阻塞 Android host。

---

## 7. 输入、滚动与 Back

### 7.1 PointerInput

新增平台无关 pointer 输入，至少包含：

- position；
- action：down、move、up、cancel；
- pointer id；
- device kind：mouse、touch、pen；
- button；
- isPrimary。

旧 `MouseEvent` 在迁移期保留兼容，不直接删除公开 API。Win32、X11、macOS 也迁移到统一处理路径，确保 Android 不产生第二套控件交互实现。

### 7.2 Tap 与 scroll

- tap 仅在 down/up 目标一致且未超过 touch slop 时合成 click；
- 超过 slop 后取消 click/active，并进入 scroll gesture；
- touch 不产生持久 `:hover`；
- `GestureDetector`/`VelocityTracker`/`OverScroller` 产出 precise/inertial `WheelInput`，复用现有滚动链；
- fling 在 pause、detach 或新手势开始时立即取消；
- 首期只跟踪 primary pointer，额外 pointer 明确忽略。

### 7.3 Android Back 顺序

1. 关闭最上层 Popup/Menu/Select/Dialog；
2. 已配置 Router 时执行后退；
3. 无可消费状态时交给 Activity finish。

不能把 Back 永久转换为 Escape，也不能让已关闭的 Activity 继续收到异步回调。

---

## 8. 文本、IME 与字体

### 8.1 Text input client

现有 `ITextEditor.HandleTextInput(string)` 继续保留，但 Android IME 需要新增 composition-aware 协议，至少表达：

- 当前文本和 selection；
- composition range；
- replace/commit composing text；
- finish/cancel composition；
- delete surrounding text；
- set selection；
- caret rect；
- input type、multiline 和 IME action。

Android View 通过 `OnCheckIsTextEditor` / `OnCreateInputConnection` 接入。composition 更新不能反复写入最终 undo history。

### 8.2 字体

- [x] 增加 Android 可读系统字体根；Android 原生 Typeface 回退同时覆盖 `/system/fonts` 等目录。
- [x] generic `sans-serif`/`system-ui` 映射到 Android sans-serif/Roboto。
- [x] generic `serif`/`monospace` 映射到设备系统字体。
- [~] 中文/日文/韩文 fallback 使用 Android Typeface；emulator 中文 glyph 已验证，emoji 非 BMP raster 未做独立像素断言。
- [x] 确定性像素测试继续使用仓库嵌入字体或像素探针。
- [ ] 真机 smoke 同时验证系统字体（arm64 真机按本次范围排除）。
- [x] layout、selection、caret 和 raster 使用同一字体度量来源；Android Paint metrics 已注册到核心。

---

## 9. 生命周期与窗口能力

### 9.1 Activity / View 生命周期

| Android 生命周期 | Square 行为 |
|---|---|
| OnCreate | 创建 AppWindow、View、host 和 session；未有非零 surface 尺寸前不建 renderer |
| View attached/size known | attach session，创建 renderer，首帧布局与绘制 |
| OnPause | 暂停 frame、fling、caret 和输入，不 unload 文档 |
| OnResume | 重置 frame time，仅在有 demand 时恢复 |
| size/density change | resize renderer、失效 layout/paint |
| Vulkan Surface destroyed/replaced | 先暂停会话并释放旧 render context，再释放 ANativeWindow；新 Surface 和 resumed 状态同时满足后恢复并重建 renderer |
| OnDestroy | detach 一次，取消 Choreographer/IME/fling，释放 Bitmap/renderer |

首期对 orientation/screenSize/density 做原位 resize。进程死亡后业务状态恢复不在首期范围，但重建不得崩溃或引用旧 Activity。

### 9.2 Insets

首期默认不做 edge-to-edge：

- 内容区由系统 bars 和 `adjustResize` 提供；
- keyboard resize 后重新布局；
- safe-area/window-insets 后置为显式能力；
- 任何 inset 都先从物理像素转换为 Square DIP。

### 9.3 AppWindow 能力矩阵

| API | Android 首期 |
|---|---|
| `Close` | 映射 Activity finish |
| `Title` | 可映射 Activity title，不保证显示系统标题栏 |
| `Minimize/Maximize/Restore` | Unsupported |
| `BeginMove` | Unsupported |
| Custom title bar | Unsupported；应用内容自行绘制 header 不等于系统 title bar |
| `Open` OS 子窗口 | Unsupported |
| `OpenDialog` | Unsupported；应用改用 Square 应用内 `Dialog`，不创建新线程 Activity |
| `NativeWindow` | `IntPtr.Zero`，不把 Java object handle 冒充 HWND |

Unsupported 路径应明确抛出 `PlatformNotSupportedException` 或在能力查询中提前拒绝，不能静默成功。

---

## 10. 分阶段任务

### Gate 0：文档与决策

- [x] 明确非 MAUI、Canvas-first、Software-first。
- [x] 明确 Android 使用外部事件循环，不复用阻塞 `PumpEvents()`。
- [x] 明确 NativeAOT 为实验、非阻断。
- [x] 明确 Beta/Stable 的 IME 与 accessibility 门槛。
- [x] 本轮 TODO、`Rendering-Targets.md` 和 `Roadmap.md` 的文档改动已完成并与实现同步。

**退出标准：** 文档已和实现同步，未将未验证能力写成 Beta 或 Stable。

### Phase 0：工具链与像素 spike

- 临时 spike 放在系统临时目录，不提交到仓库；只有测试资产和结论进入正式项目。
- [x] 已安装并记录 .NET 10 Android workload 36.1.69（SDK 10.0.303.1）、JDK 17.0.19、Android platform 36、Build Tools 36.0.0、platform-tools 37.0.1；x86_64 emulator 已可用。
- [x] 已建立正式非 MAUI `net10.0-android` Activity Sample。
- [x] 在 x86_64 emulator 安装并启动 Debug APK（API 37、x86_64、1080x2400、420 dpi）。
- [x] 已通过 x86_64 emulator 与像素探针验证首帧颜色/alpha、DPI、旋转布局、BGRA→ARGB 和 dirty rect。
- [~] stride 已验证核心 Bitmap 的紧凑 stride 与跨行复制；非紧凑 stride 无公共构造路径，lockPixels 未引入。
- [x] 已记录 Software 全量上传、AndroidCanvas/Skia/Vulkan 直绘的首帧与滚动耗时；性能日志包含 frame/upload 计数、耗时和字节数。
- [x] 已构建 arm64 Release APK/AAB。
- [x] 已验证 Release trimming + profiled AOT 构建并在 x86_64 emulator 启动；Android NativeAOT `PublishAot=true` 单独执行。
- [x] 已记录实验 NativeAOT 结果：XA1040 与 IL3053（Mono.Android 无效 IL/CLR metadata、SkiaSharp.Views.Android 资源设计程序集无法加载），因此按官方限制不作为生产门。

**退出标准：** emulator 的 presenter 像素、旋转、性能和 AOT/trimming 证据已完成；arm64 真机和官方实验 NativeAOT 生产门禁不在本次可通过范围。
### Phase 1：外部事件循环 ApplicationSession

**预计修改：**

- `src/Square/Runtime/Application/Application.cs`
- `src/Square/Runtime/Application/Dispatcher.cs`
- `src/Square/Hosting/DesktopApplication.cs`
- `src/Square/Hosting/AppWindow.cs`
- `src/Square/Hosting/IAppWindowRuntime.cs`
- `src/Square/Platform/IPlatformHost.cs`
- 新增 `src/Square/Hosting/ApplicationSession.cs`

**任务：**

- [x] 已写 start/resume/frame/pause/detach 顺序测试（当前覆盖外部会话、幂等清理和不调用 PumpEvents）。
- [x] pause/resume 已重置动画基线；emulator 生命周期压力已完成。
- [x] Dispatcher 已增加并测试空→非空 wakeup。
- [x] 已抽取 session，并让 DesktopApplication 复用共享准备、帧和清理路径。
- [x] 已完成桌面解决方案 Debug 构建回归；完整三桌面运行行为仍需 CI 回归。

**退出标准：** fake external-loop host 已能不调用 `PumpEvents()` 完成首帧和清理；三桌面 Debug build 与定向回归已通过，完整三桌面运行行为仍由各自 CI 门禁负责。

### Phase 2：统一 pointer/touch 语义

**预计修改：**

- 新增 `src/Square/Platform/PointerInput.cs`
- `src/Square/Runtime/Events/Event.cs`
- `src/Square/Runtime/Events/StandardEvents.cs`
- 抽取后的 session input path
- Win32/X11/macOS host

**任务：**

- [x] pointerdown/move/up/cancel 已路由。
- [x] PointerEvent 已暴露坐标、id、type、primary 和 button。
- [x] touch 路径不设置 hover；mouse 保持现有 hover，桌面宿主仍使用兼容 MouseEvent。
- [x] Android touch slop 超限后取消 click/active。
- [x] scroll/fling 已复用 WheelInput 滚动链。
- [~] 已实现 pause/new gesture 取消路径；drag selection、splitter、popup、scrollbar、context menu 的完整回归待补。

**退出标准：** Android 只翻译 `MotionEvent` 并复用统一 session 输入路由；x86_64 emulator 已完成 touch/scroll/back smoke，复杂控件矩阵仍按对应扩展测试维护。


### Phase 3：Android host 与首帧

**新增项目/文件：**

- `src/Square.Platform.Android/Square.Platform.Android.csproj`
- `src/Square.Platform.Android/AndroidPlatformHost.cs`
- `src/Square.Platform.Android/SquareView.cs`
- `src/Square.Platform.Android/AndroidBitmapPresenter.cs`
- `src/Square.Platform.Android/AndroidFrameScheduler.cs`
- `src/Square.Platform.Android/SquareActivity.cs`
- `src/Square.Platform.Android/AndroidPlatformRegistration.cs`
- `src/Square.Platform.Android/AndroidInputAdapter.cs`
- `src/Square.Platform.Android/AndroidClipboard.cs`
- `src/Square.Platform.Android/AndroidInputConnection.cs`
- `src/Square.Platform.Android/AndroidFontPolicy.cs`
- `samples/Square.Sample.Android/Square.Sample.Android.csproj`
- `samples/Square.Sample.Android/MainActivity.cs`
- `samples/Square.Sample.Android/MainPage.sqv`
- `Square.Android.slnx`

**任务：**

- [x] 增加 `SquareTargetPlatform=Android`、`PLATFORM_ANDROID` 和 Android RID 校验。
- [x] 实现 Activity/View host、frame scheduler 和 bitmap presenter。
- [x] 使用真实 SQV Sample，不用手写假树绕过 generator。
- [x] `SquareActivity` 只作为便利入口；底层 host/session 保持可组合。
- [x] Android `Auto` scrollbar profile 在便利 Activity 中解析为 Mobile。
- [x] resize/density change 调整 render context 并触发重绘。
- [~] 空闲时不持续请求帧（代码为单 pending demand，`gfxinfo` 空闲窗口观察到 1 个 frame；真实功耗仍待设备测量）。
- [x] 每帧不创建 Bitmap；呈现转换数组复用，性能日志已记录上传与直绘帧耗时。

**退出标准：** emulator 已显示真实 SQV 文本、形状、滚动内容并验证旋转后的几何与像素尺寸；图片绘制已在 Canvas/Skia 后端实现，arm64 设备验证按本次范围排除。

### Phase 4：触摸、滚动、Back 与剪贴板

**预计新增：**

- `src/Square.Platform.Android/AndroidInputAdapter.cs`（包含 touch slop、scroll 和 fling）
- `src/Square.Platform.Android/AndroidClipboard.cs`

- [x] tap Button 只 click 一次（emulator smoke 通过）。
- [x] 在 Button 上开始 scroll 不误 click（emulator swipe smoke 通过）。
- [x] 垂直/水平 scroll 和 fling 已接入 WheelInput，边界行为由统一 ScrollBy 链处理并有回归测试。
- [x] 内层滚动到边界后外层可继续滚动（Wheel 默认动作遍历祖先，边界回归通过）。
- [x] pause/detach/new gesture 取消 fling 路径已实现。
- [~] Back 按 Popup/Dialog/Router/Activity 顺序：Popup 和 Activity 已接入，Router 由 Activity 回调提供，Dialog 专项设备回归待补。
- [x] Android ClipboardManager 已桥接 Unicode 文本。
- [~] 外接鼠标 hover/wheel 不污染 touch 状态，代码路径已区分设备；emulator 未连接真实外接鼠标。

**退出标准：** 代码已不依赖桌面滚轮；x86_64 emulator 已完成基础 Sample 的启动、tap、swipe、text、back 和截图验收，复杂控件专项保持后续回归。

### Phase 5：字体、软键盘和 IME

**预计修改/新增：**

- `src/Square/Text/Glyph/StbGlyphRasterizer.cs`
- `src/Square/Text/Glyph/SystemGlyphRasterizer.cs`
- `src/Square/Controls/TextEditors.cs`（`ITextInputClient`）
- `src/Square.Platform.Android/AndroidInputConnection.cs`
- `src/Square.Platform.Android/AndroidFontPolicy.cs`

- [x] Android 系统字体和 generic family 已加入 `/system/fonts` 等根目录与 Roboto/Noto 映射，emulator 读取与 CJK fallback 已验证。
- [~] 英文与中文真实 glyph 和一致 metrics（emulator 中文组合 IME 截图通过）；emoji 非 BMP raster 未做独立像素断言。
- [x] Input/TextArea 获焦后显示软键盘（emulator 通过）。
- [x] commit/composition/delete/selection/editor action 已接入组合客户端；自包含中文 IME service 在 emulator 通过。
- [x] 中文拼音预编辑不重复写值或 undo history（`n`→`ni`→`你`→commit 后显示单个 `你`）。
- [x] caret rect 已转为物理像素并请求候选区滚动（emulator 输入连接通过）。
- [x] 切换输入框、旋转、销毁后的旧 InputConnection 通过会话/宿主释放路径失效；压力回归无 crash。
- [~] copy/cut/paste 与选区已有统一核心路径；Android emulator 剪贴板自动化专项仍待扩展。

**退出标准：** emulator 的英文/中文组合输入、文本、caret、selection 已通过；arm64 真机 IME 验收按本次范围排除。

### Phase 6：生命周期、资源和压力测试

- [x] pause/resume 50 次无 crash。
- [x] 旋转 20 次无重复 session、renderer 或 callback。
- [x] 锁屏/解锁后按需恢复。
- [x] Activity finish/reopen 后无旧引用。
- [x] renderer、Bitmap、InputConnection、Choreographer callback 各释放一次；压力回归无 crash。
- [~] locale/fontScale/density change 的压力路径已执行 fontScale/density smoke；locale 长期回归待设备矩阵。
- [x] 应用异常退出后可安全重建；业务状态恢复限制保持文档声明。

**退出标准：** x86_64 emulator 压力回归无 crash；内存 PSS 基线 105,221 KB、回归后 100,609 KB，未观察到增长；arm64 设备压力按本次范围排除。

### Phase 7：打包、CI、文档与 Beta

**预计新增/修改：**

- `.github/workflows/android.yml`
- `README.md`
- `docs/Architecture.md`
- `docs/Rendering-Targets.md`
- `docs/Roadmap.md`

- [x] 新增独立 Android workflow，不要求三桌面 runner 安装 Android workload。
- [x] 普通 runner 已运行 session、pointer、组合输入回归；emulator font policy 与 CJK fallback smoke 已通过。
- [x] x86_64 emulator 安装、启动、tap/swipe/text/back、截图和 logcat smoke（API 37、x86_64；arm64 设备仍待验收）。
- [ ] arm64 设备执行中文 IME 与生命周期门禁（按本次范围排除）。
- [x] 已记录 arm64 APK/AAB、ABI、包体积和 Release trimming 配置；x86_64 profiled AOT gate 已构建并运行。
- [x] 已记录首帧、空闲帧需求、滚动 frame time、内存和 bitmap upload；Software/Canvas/Skia/Vulkan 路径均有性能日志或 gfxinfo 证据。
- [x] 已更新 Android TODO、Architecture 路线、Rendering 路线、Roadmap 和 README 状态；API support matrix 仍不宣称 Stable。
- [x] Android 继续在文档中保持 experimental，直到 Stable 条件满足。

**退出标准：** 除 arm64 真机门禁外，本轮 emulator 的 IME、像素、性能、生命周期、无障碍和图形后端证据均已完成；Beta/Stable 仍需 arm64 真机验证。


### Phase 8：Stable 与后续优化

- [x] 基于 profiling 决定当前保留 Software 全量 bitmap upload，并提供 AndroidCanvas/AndroidSkia/Vulkan 直绘路径；未引入未经测量的 lockPixels。
- [x] AndroidCanvas backend 已实现并在 emulator 显示、滚动和空闲帧路径通过。
- [x] Skia Android direct surface 已通过 `SKCanvasView` 与 emulator 画面验证。
- [x] Android Vulkan surface 已通过 `ANativeWindow`、`VK_KHR_android_surface`、RGBA swapchain 和 emulator 旋转 smoke。
- [~] 已用 `AccessibilityNodeProvider` 暴露虚拟节点、名称、状态、bounds 和 action；uiautomator 已读取节点树，TalkBack 服务当前未启用（`enabled_accessibility_services=null`）。完整多设备语音矩阵后置。
- [ ] 另立 `Square.Native.Android` 计划，优先 View adapter，再评估 Compose。
- [ ] 另行规划 AppCompat、Fragment/Compose container、多 Activity 和状态恢复。

**退出标准：** accessibility、IME、多设备 CI 和性能阈值全部通过后，才能讨论 Stable。

---

## 11. 验收矩阵

| 能力 | 启动 MVP | 交互 MVP | Beta | Stable 候选 |
|---|---:|---:|---:|---:|
| SQV/SQX + CSS/Layout | 必须 | 必须 | 必须 | 必须 |
| Software bitmap 呈现 | 必须 | 必须 | 必须 | 必须 |
| density/resize/rotation | 基础 | 必须 | 压力测试 | 多设备 CI |
| tap/click | 基础 | 必须 | 回归矩阵 | 多输入设备 |
| touch scroll/fling | 不要求 | 必须 | 嵌套/边界 | 性能阈值 |
| hardware keyboard | 不要求 | 基础 | 必须 | 多布局 |
| soft keyboard | 不要求 | 基础 | 必须 | 多 IME |
| 中文 composition | 不要求 | 不要求 | 必须 | 多厂商设备 |
| clipboard | 不要求 | 必须 | 必须 | 必须 |
| pause/resume/destroy | 不崩溃 | 必须 | 压力测试 | 长期回归 |
| Release APK/AAB arm64 | 不要求 | build | install/run | 发布门禁 |
| NativeAOT | 实验记录 | 非阻断 | 非阻断 | 等待官方成熟 |
| Accessibility | 不支持 | 不支持 | 设计完成 | 必须 |
| Native View/Compose | 不支持 | 不支持 | 不支持 | 后续独立路线 |

---

## 12. 验证命令与证据要求

具体 SDK/API 参数须在 Phase 0 按锁定 workload 校正；计划中的基础命令为：

```bash
dotnet workload install android
dotnet restore Square.Android.slnx -p:SquareTargetPlatform=Android
dotnet build Square.Android.slnx -c Debug -p:SquareTargetPlatform=Android
dotnet build samples/Square.Sample.Android/Square.Sample.Android.csproj -t:Install -f net10.0-android
adb shell am start -n <package>/<activity>
adb exec-out screencap -p > artifacts/android/first-frame.png
```

本次实现的本机证据：

- `dotnet workload install android`：退出码 0；Android workload 36.1.69 / SDK 10.0.303.1。
- `dotnet build Square.Android.slnx -c Debug -p:SquareTargetPlatform=Android`：退出码 0；包含 AndroidCanvas、AndroidSkia 和 Vulkan Android target。
- `dotnet build Square.slnx -c Debug`：退出码 0；桌面解决方案未被 Android 多目标改动破坏。
- `dotnet test tests/Square.Platform.Tests/Square.Platform.Tests.csproj -c Debug -p:SquareTargetPlatform=Win32 --filter "FullyQualifiedName~ApplicationSessionTests"`：5/5 通过。
- `dotnet test tests/Square.UI.Tests/Square.UI.Tests.csproj -c Debug -p:SquareTargetPlatform=Win32 --filter "FullyQualifiedName~DocumentTests|FullyQualifiedName~ScrollbarHostInteractionTests"`：190/190 通过。
- `dotnet test tests/Square.Extensions.CodeEditor.Tests/Square.Extensions.CodeEditor.Tests.csproj -c Debug -p:SquareTargetPlatform=Win32 --filter "FullyQualifiedName~SharedScrollbarTests"`：20/20 通过。
- arm64 Release trimming APK：`artifacts/android-arm64/com.wuldas.square.sample-Signed.apk` 9,286,356 bytes；AAB：`artifacts/android-arm64-aab/com.wuldas.square.sample-Signed.aab` 9,276,716 bytes。
- x86_64 Debug APK：`samples/Square.Sample.Android/bin/Debug/net10.0-android/android-x64/com.wuldas.square.sample-Signed.apk` 28,540,505 bytes；`EmbedAssembliesIntoApk=true` 防止 Fast Deployment 覆盖目录崩溃。
- `aapt2 dump badging`：Debug `native-code='x86_64'`、arm64 `native-code='arm64-v8a'`，均为 `minSdkVersion=26`、`targetSdkVersion=36`。
- `emulator-5554`（`sdk_gphone16k_x86_64`，API 37，1080x2400，420 dpi）：Debug APK 安装和启动成功，Software/Canvas/Skia/Vulkan 四种模式均保持进程运行；logcat 为 `Android fonts: root=True, stb=True, sans=Roboto`。
- 中文 IME：临时 `InputMethodService` 在 emulator 执行 `n`→`ni`→`你`→commit，输入框最终显示单个 `你`；`artifacts/android/ime-cjk-native.png`。
- 像素探针输出：`initial=FF010203;alpha=800A141E;row1=FF0B1C2D;untouched=FF415263;dirty=FF00FF00/FF415263;stride=12`，确认 BGRA→ARGB、alpha、跨行 stride 和 dirty rect。
- 性能日志：Software `presents=22/uploadAvgMs=49.577/uploadBytes=216120960/bitmap=1080x2274`；AndroidCanvas `frames=31/frameAvgMs=29.733/presents=0`；AndroidSkia `frames=51/frameAvgMs=27.453`；Vulkan `frames=55/frameAvgMs=29.222`。Software 全量 upload 瓶颈已记录，直绘路径不再上传 Square bitmap。
- `dumpsys gfxinfo`：Canvas 空闲等待 3 秒只记录 1 个 frame；滚动后 AndroidCanvas 18 frames、17 janky，Vulkan 14 frames、无 bitmap upload；性能数据用于后续阈值优化，不冒充 Stable 门禁。
- 生命周期：x86_64 emulator 完成 pause/resume 50 次、旋转 20 次、锁屏/解锁、finish/reopen 5 次；Software 基线 PSS 105,221 KB、回归后 100,609 KB；无 fatal crash。Vulkan 旋转 20 次同样无 fatal crash。
- 重建：`am crash` 后再次 `am start` 成功，进程重新启动；fontScale/density 改变并恢复后进程保持运行。
- `uiautomator` 读取 26 个 Sample 节点，包含 1 Button、1 EditText、1 ScrollView；TalkBack 服务未启用，`artifacts/android/square-talkback-final.xml` 保存虚拟树输出。
- 图形截图：`artifacts/android/software-final.png`、`android-canvas.png`、`android-skia.png`、`android-vulkan-fixed.png`；四条路径均显示真实生成 SQV 页面。
- 2026-09-05 按钮断行补充回归：`Square.UI.Tests` 766/766、`Square.Backends.Tests` 180/180 通过，包含负字距不误折行、正词间距正常折行、按钮随宽度增高/恢复和显式高度不被撑大。更新后的 x86_64 Debug APK 已安装，Software/Vulkan 实际画面确认原按钮单行和新增按钮四行。
- `PublishAot=false` + `RunAOTCompilation=true` + `AndroidEnableProfiledAot=true` + `TrimMode=full`：x86_64 APK 构建并在 emulator 启动成功。`PublishAot=true` 实验命令在 LLVM 可用后仍以 XA1040/IL3053 失败，按官方限制不作为生产门。
- arm64 真机未安装、未启动、未做 IME 或压力验证；这是本次明确排除项。

### 2026-09-05 审查修复回归

- 13 项修复已整合：密码无障碍泄露、Vulkan Surface 重建、硬件字符/控制键、IME 原生查询、Canvas 提交刷新、抬手前惯性滚动、空提交删除选区、非统一圆角、透明/局部清除、Android 通用字体、CSS 动画调度、暂停期间控件帧请求、自定义字体优先级与替换缓存。
- `dotnet test`：`Square.Platform.Tests` 25、`Square.UI.Tests` 774、`Square.CSS.Tests` 207、`Square.Graphics.Tests` 55，合计 1,061 项通过，0 失败、0 跳过。
- Android API 37 x86_64 emulator：25 项原生行为探针通过，覆盖密码节点/祖先搜索、IME formatted 虚调用和大长度查询、空提交与撤销、触摸 release/cancel、硬件字符与控制键、Canvas 像素清除/圆角、通用字体和自定义字体度量。
- Vulkan：真实后台/前台切换后画面恢复，按钮仍更新为 `Tapped`，输入和 Home/Delete/End/Backspace 可继续编辑；没有复现旧 Surface 崩溃。
- AndroidCanvas：Dispatcher 将背景从红改蓝后，屏幕自动刷新为蓝色；记录画面像素为 `FF0000FF`，无需触摸或强制 redraw。
- arm64 Release：`dotnet publish samples/Square.Sample.Android/Square.Sample.Android.csproj -c Release -f net10.0-android -r android-arm64 --self-contained false -p:SquareTargetPlatform=Android -p:PublishAot=false -p:TrimMode=full -o artifacts/android-review-arm64` 成功，包含 Android AOT 编译；签名 APK/AAB 位于该目录，未做 arm64 真机运行验证。
- 证据：`artifacts/android/review-fixes.log`、`review-vulkan-resumed.png`、`review-vulkan-input.png` / `.xml`、`review-canvas-refresh.png`。上述证据不替代 arm64 真机和不同 IME/厂商驱动的验证。

### 后续阶段证据要求

每个阶段的完成声明至少包含：

- 精确命令与退出码；
- test 数量和过滤条件；
- APK/AAB 绝对路径、ABI 和文件大小；
- emulator/API 或真机型号、Android 版本、density 和刷新率；
- 截图、logcat 或 instrumentation 输出；
- `git diff --check`；
- 三桌面 build/test 回归；
- NativeAOT 若失败或警告，按实验结果原样报告。

---

## 13. 风险与处理

| 风险 | 处理 |
|---|---|
| 复制 DesktopApplication 造成运行时分叉 | 先抽共享 `ApplicationSession` |
| Activity 主线程被阻塞 | 完全由 Activity/Looper/Choreographer 外部驱动 |
| 空闲持续 60Hz 消耗电量 | 统一 frame demand 和单 pending callback |
| Software bitmap upload 过慢 | 先测量；按证据选择区域上传、lockPixels、Canvas 或 Skia |
| touch drag 误 click | pointer cancel + touch slop + down/up target 规则 |
| 中文输入仅提交最终字符串 | composition-aware text client + InputConnection |
| Android 系统字体无法解析 | Android font roots/generic mapping + 系统字体真机 smoke |
| rotation/destroy 泄漏 Activity | session 明确 ownership，取消全部 native callback 和输入连接 |
| Android workload 影响三桌面 CI | 独立 `Square.Android.slnx` 与 workflow |
| NativeAOT 实验状态与框架目标冲突 | 支持的 Release AOT/trimming 为正式门；NativeAOT 独立报告 |
| Canvas-only 无障碍差 | experimental/Beta 诚实标记；Stable 前实现虚拟 accessibility tree |
| Native adapter 出现双布局 | 不纳入首期；后续单独冻结 Square/native 布局权威关系 |

---

## 14. 明确不支持项

在对应后续阶段完成前，Android 文档和包元数据必须明确标注：

- 不支持 Android View/Compose 原生控件树；
- 不支持多窗口、多 Activity 会话与桌面式窗口操作；
- 不支持多指缩放、旋转、手写笔压力/倾斜；
- 不支持 Android 原生 WebView 扩展；
- 不保证进程死亡后的业务状态恢复；
- Beta 前不保证完整中文 IME；
- Stable 前不保证 TalkBack/完整 accessibility；
- Android NativeAOT 在官方仍标记实验时不作为生产支持声明。

---

## 15. 外部基线资料

- [.NET for Android Build Properties](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties)
- [.NET for Android warning XA1040](https://learn.microsoft.com/en-us/dotnet/android/messages/xa1040)
- [Android Activity lifecycle](https://developer.android.com/guide/components/activities/activity-lifecycle)
- [Android Choreographer](https://developer.android.com/reference/android/view/Choreographer)
- [Android InputConnection](https://developer.android.com/reference/android/view/inputmethod/InputConnection)

外部文档会随 Android 和 .NET workload 更新。Phase 0 必须记录实际使用的 SDK/workload 版本，并以该版本的正式文档和真实构建输出为准。
