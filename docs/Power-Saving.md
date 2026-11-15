# 按需帧调度与节能

Square 默认启用按需帧调度。静态界面没有待处理工作时停止帧时钟；输入、Dispatcher、Reconciler、绘制失效和后端重绘请求唤醒宿主；动画和延迟任务保留各自的截止时间。

```csharp
var window = new AppWindow("My App", 800, 600)
{
    PowerSavingEnabled = true // 默认值；子窗口创建时继承 owner 的设置
};
window.Load(new Main());
new DesktopApplication(window).Run();
```

需要沿用连续 Tick 的应用可以在窗口上设置 `PowerSavingEnabled = false`。关闭该选项会恢复宿主帧节拍，仍只在有视觉变化时绘制；最小化和外部宿主的 Suspend 仍会抑制绘制。此开关控制 Square 自己的调度，不调整系统电源计划、屏幕亮度或 Godot Engine 的全局节拍。

## 调度行为

| 场景 | 行为 |
|---|---|
| 静态界面、无工作 | 没有帧截止时间，桌面宿主阻塞等待事件 |
| 输入、绘制失效、Dispatcher 或 Reconciler 新工作 | 唤醒 UI 线程；合并重复通知，处理完后重新计算需求 |
| CSS 动画 | 按正常原生帧节拍继续；结束后回到空闲 |
| 图片、控件的延迟帧 | 等到最早截止时间，不为等待一个远期任务逐帧轮询 |
| Input/TextArea 光标 | 按下一次显示/隐藏边沿唤醒，有选区或失焦时不保持闪烁时钟 |
| CodeEditor 光标 | 静止期等待截止时间，渐变期按正常帧节拍推进 |
| Terminal 光标 | 按闪烁截止时间唤醒；终端隐藏光标时不保持该时钟 |
| 桌面窗口最小化 | 停止动画与绘制；Dispatcher/协调器工作仍能唤醒并执行，包括关闭窗口 |
| 桌面恢复 | 重置动画时间基线并请求重绘，避免把整个最小化时长算入动画一步 |
| Android pause / Godot 隐藏或暂停 | 撤销或停止 Square 帧工作；恢复后重新计算需求 |
| 可见窗口仅失去焦点 | 继续处理需要的动画，不把失焦等同于不可见 |

空闲后的首次 CSS Tick 同样重置时间基线。控件请求的绝对截止时间仍保留，恢复后到期任务按现有控件协议处理。

## 各宿主的等待机制

| 宿主 | 实现 |
|---|---|
| Win32 | 单次 waitable timer + `MsgWaitForMultipleObjectsEx`；空闲停 timer；后台通知通过原子 pending 与私有 `PostMessage` 唤醒 |
| X11 | `poll` 等待 X connection 与独立 eventfd；后台通知只写 eventfd，不从后台线程调用 Xlib；移除主事件循环 1ms 轮询 |
| macOS | AppKit `nextEventMatchingMask:untilDate:inMode:dequeue:` 等待事件或截止时间；`postEvent:atStart:` 唤醒；窗口通知负责关闭、最小化、恢复和尺寸变化 |
| Android | 一个 pending Choreographer 回调；支持延迟、最早截止时间、取消和生命周期暂停；帧尾移除已被当前帧消费的陈旧请求 |
| Godot | 引擎拥有循环，Square 的 `_Process` 在无截止时间或未到期时立即返回；场景、尺寸、可见性等事件更新状态；后台通知合并到主线程 |
| Web Hosting | 已有服务端事件请求与浏览器 DOM 事件驱动路径，没有 Square 周期性帧时钟或网络轮询；不修改浏览器自己的 CSS 动画节拍 |

## 自定义宿主和控件

`ApplicationSession.NextFrameDelay` 必须在会话的 UI 线程读取：

- `null`：当前没有帧需求，或会话未附加/已暂停。
- `TimeSpan.Zero`：下一个原生帧节拍需要工作；避免紧循环调用 Tick。
- 正值：等待该延时后再 Tick。

订阅 `ApplicationSession.FrameRequested` 以获知新需求。事件可能来自其他线程，应先合并、投递到 UI 线程，再查询延迟。Tick 和直接输入绘制结束后也会通知宿主重新计算截止时间；不要在事件内同步递归调用 Tick。`Tick()` 本身负责必要的绘制，不需要每次再无条件 `ProcessFrame()`。

独立桌面宿主可实现 `IPlatformHost.FrameDelayQuery` 与线程安全的 `Wake()`。核心附加时设置查询、分离时清除；Wake 只请求一次 UI Tick，不能转发成 `RenderRequested` 再递归唤醒。队列在后台入队和 UI 等待之间使用原子 pending／原生唤醒信号防止丢工作。接口有默认实现，已有第三方宿主仍可维持原有循环，需接入上述契约才能停止空闲轮询。

自定义 `ITextEditor` 可实现 `TimeSpan? CaretBlinkDelay` 上报光标期限，返回 `null` 表示无需闪烁。默认接口实现返回零，保持旧编辑器每帧检查光标的兼容行为。内置 Input/TextArea、CodeEditor、Terminal 已实现实际截止时间。

`Reconciler.WorkAvailable` 与 Dispatcher 的通知约定一致：队列由空变为非空时在锁外触发。附加/分离时的订阅和资源清理配对，避免关闭后的延迟通知操作已释放的宿主。

## 验证

```powershell
# 主解决方案包含原生 Win32 调度、共享会话和外部宿主边界替身测试。
dotnet test Square.slnx -c Release -p:SquareTargetPlatform=Win32 --filter "Category!=ChromiumIntegration&Category!=Performance"

# 在 Windows 可执行纯托管策略测试；不替代对应 OS 的原生事件泵验证。
dotnet test tests/Square.Platform.Tests/Square.Platform.Tests.csproj -c Release -p:SquareTargetPlatform=MacOS
dotnet test tests/Square.Platform.Tests/Square.Platform.Tests.csproj -c Release -p:SquareTargetPlatform=X11

# 额外工具链的解决方案独立构建。
dotnet build Square.Godot.slnx -c Debug
dotnet build Square.Android.slnx -c Debug -p:SquareTargetPlatform=Android
```

外部宿主测试直接编译生产 Android scheduler 与 Godot control，并用可控原生边界替身验证单 pending、取消、延迟、空闲早退、后台通知合并和生命周期；核心会话另有真实控件回归。替身测试不等于 Android 设备或真实 Godot 引擎运行。

2026-11-15 本机验证使用 Windows，最终结果：

- 主解决方案 Release 功能测试：5,039 通过、35 项既有 Yoga 用例跳过、0 失败；筛选条件排除 ChromiumIntegration 与 Performance。TRX 位于忽略的 `artifacts/power-saving-tests-final`。
- 平台测试按配置串行执行：Win32 70/70、X11 94/94、macOS 67/67。其中 Win32 有 6 项真实窗口消息循环回归，macOS 有 16 项宿主策略测试；X11/macOS 的此处结果为 Windows 上的编译与可运行测试。
- 外部宿主边界替身测试 15/15，通过主解决方案持续执行；覆盖空闲一千次引擎回调不执行 Square 工作、最早截止时间、后台合并、隐藏/最小化/恢复和重入。
- 桌面 Release、Android Debug（含四种后端和示例）、Godot Debug 解决方案构建均退出码 0，0 警告、0 错误。Android 使用本机 `artifacts/android-sdk` 与 `artifacts/android-jdk`，通过命令行 `AndroidSdkDirectory` / `JavaSdkDirectory` 传入。
- `git diff --check` 通过。

X11/macOS 的实际阻塞等待与恢复、Android 设备、Godot 引擎运行仍需各自运行环境验收。本轮没有采集电池续航或硬件功耗，不以减少 Tick 数量推导具体节电百分比。
