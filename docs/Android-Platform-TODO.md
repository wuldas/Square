# Square Android 平台支持 TODO

> Document Revision: 0.1
> Status: Draft（规划已形成，尚未实现）
> Planning Baseline: 2026-09-04
> 配套：`Architecture.md`、`Rendering-Targets.md`、`Roadmap.md`、`Rendering.md`

本文定义 Square Android 平台支持的边界、依赖顺序、验收门禁和已知不支持项。文中所有未勾选条目都只是计划；仓库当前没有 `Square.Platform.Android`、Android Sample 或 Android CI，不能据此宣称 Android 已受支持。

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

### 2.2 尚未具备

- [ ] Android target、平台项目、Activity/View host 和 Sample。
- [ ] Activity/Looper 可驱动的分步应用生命周期。
- [ ] pointer id/type/cancel 等完整触摸输入契约。
- [ ] touch slop、drag-cancel-click、fling 和 Android Back 桥接。
- [ ] Android `InputConnection` 与 composition-aware 文本输入契约。
- [ ] Android 系统字体发现和 generic family 映射。
- [ ] Android Bitmap 呈现通道及颜色、stride、alpha、DPI 证据。
- [ ] Android workload、APK/AAB、emulator 和 arm64 CI/设备门禁。
- [ ] Canvas-only View 的虚拟 accessibility tree。

### 2.3 现有阻塞证据

| 阻塞 | 当前实现 | 影响 |
|---|---|---|
| 同步消息循环 | `DesktopApplication` 固定执行 `Show -> CreateRenderContext -> PumpEvents` | Activity 主线程不能进入 Square 自有阻塞循环 |
| 一次性应用生命周期 | `Application.Run()` 只有 start/run/exit | 无法表达 resume/pause/surface 重建 |
| 鼠标中心输入 | `IPlatformHost.MouseEvent` 无 pointer id/type/cancel | touch 会误用 hover/click/drag 语义 |
| committed text only | host 只发送 `TextInput(string)` | 无法正确表达中文预编辑、选区替换和 surrounding text |
| 无 Android 字体根 | stb 字体扫描只覆盖 Windows/Linux/macOS | Software Renderer 不能保证 Android 文本真实可绘制 |
| 桌面窗口 API | `AppWindow` 含标题栏、最小化、最大化、拖窗和线程子窗口 | Android 必须明确 Unsupported 或采用应用内替代 |
| 构建拒绝 Android | `SquareTargetPlatform` 只允许 Win32/X11/macOS | Android 项目无法进入现有构建图 |
| GPU surface 缺失 | Vulkan 只支持 Win32/X11；Skia 当前为离屏 bitmap | 不能把已有 GPU 后端直接当作 Android host |

---

## 3. 已确定的首期决策

1. **非 MAUI**：使用 .NET 10 for Android 与原生 Activity/View，避免为一个平台 host 引入 MAUI UI 栈。
2. **Canvas-first**：首期只挂载一个 Square 自定义 View，不做 Android View/Compose 控件映射。
3. **Software-first**：先复用现有 Software Renderer；Android Canvas、直接 Skia surface 和 Vulkan 后置。
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
src/Square.Platform.Android/          Android Activity/View、输入、IME、剪贴板、帧调度和呈现
samples/Square.Sample.Android/        真实 SQV/SQX Android Sample
Square.Android.slnx                   仅包含 Android 所需项目，隔离 workload
.github/workflows/android.yml         Android build/emulator 门禁
```

`Square.Platform.Android` 是首期唯一新增运行时程序集。平台项目可以依赖 `Square`，核心不得反向依赖 Android SDK。

### 4.2 首期不新增

```text
Square.Backends.AndroidCanvas
Square.Native.Android
Square.Backends.AndroidVulkan
```

只有 profiling 或原生语义需求证明必要时才分别立项，不能把 host、drawing backend 和 native adapter 混成一个程序集。

### 4.3 长期边界

| 项目 | 职责 | 首期状态 |
|---|---|---|
| `Square.Platform.Android` | Activity/View host、生命周期、输入、IME、剪贴板、frame scheduling、bitmap present | 计划实施 |
| `Square.Backends.AndroidCanvas` | `DisplayTree` 绘制命令映射到 Android Canvas | 不创建 |
| `Square.Native.Android` | `NativeUiNode` 映射为 Android View/Compose 语义控件树 | 不创建 |
| `Square.Backends.Vulkan` Android surface | Android native window、swapchain、present/readback | 不创建 |

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

计划把 `DesktopApplication` 中的平台无关部分抽为 `ApplicationSession`：

| 操作 | 责任 |
|---|---|
| Attach | 构建文档、注册 CSS scope、attach/load、创建 render context |
| ProcessFrame | Dispatcher/Reconciler/CSS animation、布局、DisplayTree、render/present |
| Suspend | 暂停动画、caret 和 frame callback，不卸载文档 |
| Resume | 重置时间基线，按实际 demand 请求帧 |
| Detach | unload/detach、取消输入、释放 renderer 和回调 |

`DesktopApplication` 继续负责 PlatformRegistry、窗口创建和 `PumpEvents()`；Android Activity/View 直接驱动 session。抽取后现有桌面公开 API 和生命周期顺序必须保持兼容。

### 5.3 按需帧调度

Android 使用 `Choreographer`，但空闲时不得永久 60Hz Tick：

- 所有 element invalidation、Dispatcher 工作、CSS/图片动画、caret 和 backend replay 都汇聚为 frame demand；
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
```

### 6.2 必须验证

- [ ] BGRA 与 Android ARGB_8888 的通道顺序。
- [ ] premultiplied alpha 语义。
- [ ] stride 与非紧凑行宽。
- [ ] dirty rect 的逻辑像素/物理像素转换。
- [ ] `1 Square DIP = 1 Android dp`，bitmap 尺寸为 `logical size * density`。
- [ ] rotation/density resize 后旧 bitmap/render context 正确释放。
- [ ] 每帧不创建 Bitmap、ByteBuffer 或大数组。
- [ ] 记录局部 raster 是否仍需要全量 Android bitmap upload。

首版允许一次受控 CPU copy。是否使用区域上传或 `AndroidBitmap_lockPixels` 必须由 Phase 0 数据决定，不能先引入 NDK 复杂度。

### 6.3 后端升级触发条件

只有满足以下任一条件才评估新后端：

- 位图上传已成为滚动 frame time 的主要瓶颈；
- Software Renderer 无法达到确定的设备性能门；
- Android Canvas/Skia 能在不改变 Square 绘制语义的前提下降低内存或复制；
- Vulkan 有明确产品场景和真实设备 conformance 资源。

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

- [ ] 增加 Android 可读系统字体根。
- [ ] generic `sans-serif`/`system-ui` 映射到 Roboto 或设备系统 sans。
- [ ] generic `serif`/`monospace` 映射到真实可用字体。
- [ ] 中文/日文/韩文和 emoji fallback 使用设备真实字体或明确 fallback。
- [ ] 确定性像素测试使用仓库嵌入字体。
- [ ] 真机 smoke 同时验证系统字体，不能只靠测试字体通过。
- [ ] layout、selection、caret 和 raster 使用同一字体度量来源。

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
- [ ] 单独提交本 TODO、`Rendering-Targets.md` 和 `Roadmap.md` 的文档改动。

**退出标准：** 文档 commit 不包含任何代码或无关文件。

### Phase 0：工具链与像素 spike

- 临时 spike 放在系统临时目录，不提交到仓库；只有测试资产和结论进入正式项目。
- [ ] 安装并记录固定 .NET 10 Android workload、JDK、SDK 和 emulator image。
- [ ] 建立最小非 MAUI `net10.0-android` Activity spike。
- [ ] 在 x86_64 emulator 安装并启动 Debug APK。
- [ ] 验证 BGRA/ARGB、alpha、stride、DPI 和 dirty rect。
- [ ] 比较全量 copy、区域 copy、可选 lockPixels。
- [ ] 记录首帧上传时间、滚动上传时间和每帧分配。
- [ ] 构建 arm64 Release APK/AAB。
- [ ] 验证平台支持的 trimming/AOT。
- [ ] 单独记录实验 NativeAOT 结果和 XA1040，不作为通过门。

**退出标准：** 真正的 Activity 显示颜色正确的 Square 位图，并形成 presenter 选择记录。

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

- [ ] 先写 start/resume/frame/pause/detach 顺序测试。
- [ ] 覆盖重复调用、异常清理和只释放一次。
- [ ] 覆盖 pause 后动画停止、resume 不出现巨大 delta。
- [ ] 为 Dispatcher 增加空→非空 wakeup 测试。
- [ ] 抽取 session，再让 DesktopApplication 组合它。
- [ ] 保持桌面公开 API、生命周期和输入行为不变。

**退出标准：** fake external-loop host 不调用 `PumpEvents()` 也能完成首帧、第二帧和清理；三桌面构建与测试通过。

### Phase 2：统一 pointer/touch 语义

**预计修改：**

- 新增 `src/Square/Platform/PointerInput.cs`
- `src/Square/Runtime/Events/Event.cs`
- `src/Square/Runtime/Events/StandardEvents.cs`
- 抽取后的 session input path
- Win32/X11/macOS host

**任务：**

- [ ] pointerdown/move/up/cancel 全部路由。
- [ ] PointerEvent 暴露坐标、id、type、primary 和 button。
- [ ] touch 不设置 hover，mouse 保持现有 hover。
- [ ] drag 超过 slop 后不 click。
- [ ] scroll/fling 复用 WheelInput 滚动链。
- [ ] 回归 drag selection、splitter、popup、scrollbar、context menu。

**退出标准：** Android 只需翻译 MotionEvent，无需复制控件交互代码；桌面输入无回归。

### Phase 3：Android host 与首帧

**新增项目/文件：**

- `src/Square.Platform.Android/Square.Platform.Android.csproj`
- `src/Square.Platform.Android/AndroidPlatformHost.cs`
- `src/Square.Platform.Android/SquareView.cs`
- `src/Square.Platform.Android/AndroidBitmapPresenter.cs`
- `src/Square.Platform.Android/AndroidFrameScheduler.cs`
- `src/Square.Platform.Android/SquareActivity.cs`
- `src/Square.Platform.Android/AndroidPlatformRegistration.cs`
- `samples/Square.Sample.Android/Square.Sample.Android.csproj`
- `samples/Square.Sample.Android/MainActivity.cs`
- `samples/Square.Sample.Android/MainPage.sqv`
- `Square.Android.slnx`

**任务：**

- [ ] 增加 `SquareTargetPlatform=Android`、`PLATFORM_ANDROID` 和 Android RID 校验。
- [ ] 实现 Activity/View host、frame scheduler 和 bitmap presenter。
- [ ] 使用真实 SQV/SQX Sample，不用手写假树绕过 generator。
- [ ] `SquareActivity` 只作为便利入口；底层 host/session 保持可组合，避免把消费方锁死在 Activity 继承模型。
- [ ] Android `Auto` scrollbar profile 解析为 Mobile。
- [ ] resize/density change 正确调整 render context。
- [ ] 空闲时不持续请求帧。
- [ ] 每帧无大对象/Bitmap 分配。

**退出标准：** emulator 显示文本、形状、图片和滚动内容；旋转后几何与像素尺寸正确。

### Phase 4：触摸、滚动、Back 与剪贴板

**预计新增：**

- `src/Square.Platform.Android/AndroidInputAdapter.cs`
- `src/Square.Platform.Android/AndroidScrollGesture.cs`
- `src/Square.Platform.Android/AndroidClipboard.cs`

- [ ] tap Button 只 click 一次。
- [ ] 在 Button 上开始 scroll 不误 click。
- [ ] 垂直/水平 scroll 和 fling 可用。
- [ ] 内层滚动到边界后外层可继续滚动。
- [ ] pause/detach/new gesture 取消 fling。
- [ ] Back 按 Popup/Dialog/Router/Activity 顺序处理。
- [ ] Android ClipboardManager 桥接 Unicode 文本。
- [ ] 外接鼠标 hover/wheel 不污染 touch 状态。

**退出标准：** 无桌面滚轮也能完整操作基础 Sample 页面。

### Phase 5：字体、软键盘和 IME

**预计修改/新增：**

- `src/Square/Text/Glyph/StbGlyphRasterizer.cs`
- `src/Square/Text/Glyph/SystemGlyphRasterizer.cs`
- `src/Square/Controls/TextEditors.cs`
- `src/Square/Platform/TextInputClient.cs`（名称在实现前最终冻结）
- `src/Square.Platform.Android/AndroidInputConnection.cs`
- `src/Square.Platform.Android/AndroidFontPolicy.cs`

- [ ] Android 系统字体和 generic family 可解析。
- [ ] 英文、中文、emoji 有真实 glyph 和一致 metrics。
- [ ] Input/TextArea 获焦后显示软键盘。
- [ ] commit/composition/delete/selection/editor action 完成闭环。
- [ ] 中文拼音预编辑不重复写值或 undo history。
- [ ] caret rect 能驱动候选窗位置。
- [ ] 切换输入框、旋转、销毁后旧 InputConnection 失效。
- [ ] copy/cut/paste 与选区一致。

**退出标准：** 英文和中文 IME 在 arm64 真机通过；文本、caret、selection 的布局、命中和绘制一致。

### Phase 6：生命周期、资源和压力测试

- [ ] pause/resume 50 次无 crash。
- [ ] 旋转 20 次无重复 session、renderer 或 callback。
- [ ] 锁屏/解锁后按需恢复。
- [ ] Activity finish/reopen 后无旧引用。
- [ ] renderer、Bitmap、InputConnection、Choreographer callback 各释放一次。
- [ ] locale/fontScale/density change 触发正确失效。
- [ ] 进程被系统杀死后可安全重建，状态恢复限制有文档。

**退出标准：** 压力测试无持续增长的 Bitmap、callback 或 Activity 引用。

### Phase 7：打包、CI、文档与 Beta

**预计新增/修改：**

- `.github/workflows/android.yml`
- `README.md`
- `docs/Architecture.md`
- `docs/Rendering-Targets.md`
- `docs/Roadmap.md`
- `docs/Getting-Started.md`
- `docs/API-Reference.md`
- 新增稳定后使用说明 `docs/Android.md`

- [ ] 新增独立 Android workflow，不要求三桌面 runner 安装 Android workload。
- [ ] 普通 runner 运行 session/pointer/font policy 单测。
- [ ] Android job 构建 Debug APK 和 arm64 Release APK/AAB。
- [ ] emulator 安装、启动、tap/swipe/text/back、截图和 logcat smoke。
- [ ] arm64 设备执行中文 IME 与生命周期门禁。
- [ ] 记录 ABI、包体积、runtime、AOT/trimming 配置。
- [ ] 记录首帧、空闲 CPU、滚动 frame time、内存和 bitmap upload。
- [ ] 更新 README、Architecture、Getting Started 和 API support matrix。
- [ ] Android 在 README 中保持 experimental，直到 Stable 条件满足。

**退出标准：** Beta 矩阵有真实 emulator 与 arm64 证据；不以 build-only 代替运行成功。

### Phase 8：Stable 与后续优化

- [ ] 基于 profiling 决定区域 upload 或 lockPixels。
- [ ] 按数据决定是否建立 AndroidCanvas backend。
- [ ] 评估 Skia Android direct surface，禁止离屏后重复复制冒充 GPU 优化。
- [ ] 评估 Android Vulkan surface 与真实设备 conformance。
- [ ] 用 `AccessibilityNodeProvider` 暴露虚拟节点、名称、状态、bounds 和 action。
- [ ] 增加 TalkBack 自动/人工验收。
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
- 不支持 Android Vulkan 或直接 Skia GPU surface；
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
