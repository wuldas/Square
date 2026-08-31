# API 参考

> Document Revision: 0.3
> 配套：`Getting-Started.md`、`Architecture.md`、`Sqx-Spec.md`、`DevTools.md`、`Rendering-Targets.md`

本文按模块列出 Square 框架的公共 API。所有类型签名基于源码，以 `命名空间.类型名` 组织。

---

## 1. Square.Hosting — 桌面应用宿主

### DesktopApplication

```csharp
namespace Square.Hosting;

public sealed class DesktopApplication : Application
{
    public DesktopApplication(UIDocument document, PlatformHostCreateInfo hostCreateInfo);
    public DesktopApplication(Element contentRoot, PlatformHostCreateInfo hostCreateInfo); // 兼容：内容挂到新 UIDocument.Body
    public UIDocument Document { get; }
    public Color Background { get; set; }   // 默认 Color.White
    public RenderMode RenderingMode { get; set; } = RenderMode.FullFrame;
}
```

| 成员 | 说明 |
|---|---|
| 构造函数 | 接收 `UIDocument`（或兼容地接收内容 `Element` 并包进 Body）与窗口创建信息 |
| `Document` | 当前 UI 文档 |
| `Background` | 窗口背景色，默认白色 |
| `RenderingMode` | 每帧重绘策略，默认 `FullFrame`；可通过 `--render-mode` 参数或 `SQUARE_RENDER_MODE` 环境变量配置 |
| `Dispatcher`（继承自 `Application`） | UI 线程调度器，用于 Signal 跨线程投递 |
| `Run()`（继承自 `Application`） | 启动应用：注册默认后端/控件 → 构建文档 → 使用已注册的平台工厂创建窗口 → 消息循环 |
| `Shutdown()`（继承自 `Application`） | 请求关闭消息循环 |

`Run()` 内部自动处理：

1. `BackendRegistration` / `ControlRegistration.RegisterDefaults()`
2. `document.Build()` → `documentElement` 上 `OnAttached()`
3. 创建 `IPlatformHost` 并绑定所有输入事件
4. `OnLoaded()` → 首帧渲染 → `PumpEvents()` 消息循环
5. 退出时 `OnUnloaded()` → `OnDetached()`

### RenderMode

```csharp
namespace Square.Hosting;

public enum RenderMode
{
    FullFrame,      // 每帧全量重绘
    Auto,           // 自动选择（按脏区/布局状态）
    DirtyRegion     // 仅重绘脏区
}
```

| 值 | 说明 |
|---|---|
| `FullFrame` | 默认；每帧全量构建 DisplayTree 并提交 |
| `Auto` | 按布局脏标记与绘制脏标记自动决定全量或增量 |
| `DirtyRegion` | 仅重绘 `NeedsPaint` 标记的脏区域 |

`Sample.Vue` 等示例通过 `--render-mode=DirtyRegion` 或 `SQUARE_RENDER_MODE=DirtyRegion` 切换。

### AppWindow 模态与非模态窗口

```csharp
namespace Square.Hosting;

public sealed class AppWindow
{
    public IntPtr NativeWindow { get; }
    public void LoadGlobalCss(params string[] paths);
    public void LoadGlobalCssText(params string[] css);
    public void Open(Element content, Size? size = null);
    public Task<object?> OpenDialog(Element content, Size? size = null);
    public Task<T?> OpenDialog<T>(Element content, Size? size = null);
    public void CloseDialog<T>(T result);
}
```

`LoadGlobalCss` 从文件加载窗口级全局样式。相对路径以 `AppContext.BaseDirectory` 为基准；可一次传入多个路径，也可多次调用。`LoadGlobalCssText` 接收一个或多个 CSS 源文本。两种方法均保持添加顺序，同等 specificity 下后加载规则覆盖先加载规则，并且必须在应用开始运行前调用。

```csharp
var window = new AppWindow("My App");
window.LoadGlobalCss("Styles/base.css", "Styles/theme.css");
window.LoadGlobalCssText(".debug { outline-width: 1px; }");
window.Load(new Main());
```

组件 `<style>` 在全局样式之后应用，因此同等 specificity 下组件规则优先。内联样式仍保持最高优先级。

文件样式表支持位于顶部的 `@import "file.css"` 与 `@import url("file.css")`。相对地址按当前 CSS 文件解析，递归导入会保持“导入规则先于当前文件规则”的级联顺序，并检测循环引用。当前不支持网络 URL、media、`supports()` 和 `layer` 条件；`LoadGlobalCssText` 没有基础文件地址，因此不能使用相对导入。

```csharp
IReadOnlyList<DocumentStyleSheet> sheets = window.Document.StyleSheets;
IReadOnlyList<DocumentStyleSheet> imports = sheets[0].Imports;
```

`Document.StyleSheets` 只包含通过窗口加载的顶层全局样式表。导入表通过父样式表的 `Imports` 访问；组件 `<style>` 保持独立作用域，不加入该集合。

`NativeWindow` 返回当前原生窗口标识：Win32 为 `HWND`，X11 为 `Window`。原生窗口尚未创建或已经释放时返回 `IntPtr.Zero`。该属性只读，不转移句柄所有权，调用方不得释放或销毁它。

非模态窗口立即返回，没有结果值：

```csharp
AppWindow.Open(
    new MyFileDialog(properties),
    new Size(720, 480));
```

模态窗口返回一个在窗口关闭后完成的 `Task`：

```csharp
var result = await AppWindow.OpenDialog<MyFileResult>(
    new MyFileDialog(properties),
    new Size(720, 480));
```

对话框内容通过所属子窗口返回结果并关闭：

```csharp
private void Confirm()
{
    AppWindow?.CloseDialog(new MyFileResult(SelectedPath));
}

private void Cancel()
{
    AppWindow?.Close();
}
```

- `Open` 创建非模态原生子窗口并立即返回。
- `OpenDialog` 创建模态原生子窗口；Win32 会禁用 owner，X11 设置 transient/modal 窗口管理器提示。
- 用户直接关闭模态窗口时结果为 `null`/`default`。
- `CloseDialog(result)` 只能在通过 `OpenDialog` 打开的窗口中调用。
- `size` 省略时默认使用 `480 x 320`；宽高必须大于零。
- 子窗口继承 owner 的渲染后端、背景、渲染模式、标题栏样式和边框样式。
- 每个子窗口拥有独立 `UIDocument`、Dispatcher、渲染上下文和原生消息线程。

---

## 1.1. Square.DevTools — 本地调试与自动化服务

### DevToolsServer

```csharp
namespace Square.DevTools;

public sealed class DevToolsServer : IAsyncDisposable, IDisposable
{
    public const string TokenHeader = "X-Square-DevTools-Token";

    public string AccessToken { get; }
    public int Port { get; }
    public string BaseAddress { get; } // http://127.0.0.1:{Port}

    public static DevToolsServer Start(DesktopApplication application, DevToolsOptions? options = null);
    public void Dispose();
    public ValueTask DisposeAsync();
}
```

| 成员 | 说明 |
|---|---|
| `Start(application, options)` | 在 `127.0.0.1:{Port}` 启动 HTTP 服务；`Port = 0` 时由操作系统分配；所有 endpoint 要求 `TokenHeader` |
| `AccessToken` | 实际使用的 token；未配置时为自动生成值 |
| `Port` | 实际绑定端口；自动端口模式下以此值为准 |
| `BaseAddress` | 本地服务根地址 |
| `Dispose()` / `DisposeAsync()` | 停止并释放 loopback `HttpListener` 与后台请求循环 |

Endpoint 以 `/api/v1` 为前缀，提供 `/health`、`/screenshot`、`/input/pointer`、`/input/key`、`/input/text` 和 `/input/wheel`。请求/响应格式见 [`DevTools.md`](DevTools.md)。

推荐通过应用扩展启动服务：

```csharp
var devTools = app.UseDevToolsServer(options);
```

服务会在 `DesktopApplication` 退出时自动释放。直接调用 `DevToolsServer.Start()` 仍可用于需要自行控制生命周期的场景。

### DevToolsOptions

```csharp
namespace Square.DevTools;

public sealed class DevToolsOptions
{
    public int Port { get; set; } = 0;
    public string? AccessToken { get; set; }
    public bool AllowInputInjection { get; set; } = true;
    public bool AllowInspector { get; set; } = true;
    public bool IncludeSourcePaths { get; set; } = true;
    public bool IncludeTextContent { get; set; } = true;
}
```

`Port` 语义：

- `0`：由操作系统自动分配空闲端口，是默认和多实例推荐模式。
- `1..65535`：严格绑定指定端口；端口冲突时 `Start` 抛出异常。
- 启动后通过 `DevToolsServer.Port` 和 `DevToolsServer.BaseAddress` 获取实际地址。

`GET /api/v1/health` 返回：

```json
{
    "status": "ok",
    "processId": 12345,
    "port": 54321,
    "baseAddress": "http://127.0.0.1:54321",
    "inputInjection": true
}
```

### DevTools input records

```csharp
namespace Square.Hosting;

public sealed record DevToolsPointerInput(Point Position, MouseAction Action, KeyModifiers Modifiers = KeyModifiers.None);
public sealed record DevToolsKeyInput(int KeyCode, KeyAction Action, KeyModifiers Modifiers = KeyModifiers.None);
public sealed record DevToolsWheelInput(Point Position, int Delta, KeyModifiers Modifiers = KeyModifiers.None);
```

`DesktopApplication` 暴露 `InjectPointerAsync`、`InjectKeyAsync`、`InjectTextAsync`、`InjectWheelAsync` 和 `CaptureRendererBitmapAsync()` 供 DevTools 层跨线程投递输入与截图。`CaptureRendererBitmapAsync()` 优先读取活动渲染上下文的实时帧：若 RenderContext 实现 `IRenderBitmapSource` 且 `IsCaptureAvailable` 为 `true`（Vulkan 需设置 `SQUARE_VULKAN_READBACK=1`）则直接读回真实 GPU 输出；否则在 UI 线程将当前 DisplayTree 重放到离屏 Software bitmap。两种路径都不捕获平台窗口边框。

---

## 2. Square.Runtime — 应用运行时

### Application

```csharp
namespace Square.Runtime;

public abstract class Application
{
    public static Application Current { get; }
    public static bool IsStarted { get; }
    public Dispatcher Dispatcher { get; }
    public bool IsRunning { get; }

    public void Run();
    public void Shutdown();

    protected abstract void RunCore();
    protected virtual void OnStart() { }
    protected virtual void OnExit() { }
}
```

| 成员 | 说明 |
|---|---|
| `Current` | 当前应用实例，未启动时抛 `InvalidOperationException` |
| `Dispatcher` | UI 线程调度器 |
| `Run()` | 设置 `IsRunning`，调用 `OnStart` → `RunCore` → `OnExit` |
| `RunCore()` | 子类实现核心循环 |
| `OnStart/OnExit` | 应用级生命周期钩子 |

### Dispatcher

```csharp
namespace Square.Runtime;

public sealed class Dispatcher
{
    public bool CheckAccess();
    public void VerifyAccess();
    public void Invoke(Action action);
    public Task InvokeAsync(Action action);
    public void Run();
    public bool HasWork { get; }
}
```

| 成员 | 说明 |
|---|---|
| `CheckAccess()` | 当前线程是否为 Dispatcher 所属线程 |
| `Invoke(action)` | 将操作排入队列 |
| `InvokeAsync(action)` | 同线程直接执行；跨线程排队并返回 `Task` |
| `Run()` | 排空队列（仅所属线程可调用） |
| `HasWork` | 队列是否有待处理操作 |

### ObservableValue\<T\>

```csharp
namespace Square.Runtime.Binding;

public sealed class ObservableValue<T>
{
    public ObservableValue(T value);
    public ObservableValue();

    public T Value { get; set; }
    public IDisposable Subscribe(Action<T> handler);
    public void Notify();

    public static implicit operator ObservableValue<T>(T value);
    public static implicit operator T(ObservableValue<T> ov);
}
```

| 成员 | 说明 |
|---|---|
| `Value` | get 返回当前值；set 在值变化时通知订阅者（相等时不通知） |
| `Subscribe(handler)` | 订阅值变化，返回 `IDisposable` 用于取消 |
| `Notify()` | 强制通知当前值 |
| 隐式转换 | `T` ↔ `ObservableValue<T>` 双向隐式转换 |

### ObservableCollection\<T\>

```csharp
namespace Square.Runtime.Binding;

public sealed class ObservableCollection<T> : IList<T>, IReadOnlyList<T>, INotifyCollectionChanged
{
    public T this[int index] { get; set; }
    public int Count { get; }

    public void Add(T item);
    public void AddRange(IEnumerable<T> items);
    public void Insert(int index, T item);
    public bool Remove(T item);
    public void RemoveAt(int index);
    public void Move(int oldIndex, int newIndex);
    public void Clear();
    public int IndexOf(T item);
    public bool Contains(T item);

    public event NotifyCollectionChangedEventHandler? CollectionChanged;
}
```

| 成员 | 说明 |
|---|---|
| `Add/Insert` | 添加并触发 `CollectionChanged`（Add） |
| `Remove/RemoveAt` | 移除并触发 `CollectionChanged`（Remove） |
| `Move` | 移动并触发 `CollectionChanged`（Move） |
| `Clear` | 清空并触发 `CollectionChanged`（Reset） |
| `CollectionChanged` | 通知事件，`<For>` 原语自动订阅 |

### PropAttribute

```csharp
namespace Square.Runtime.Binding;

[AttributeUsage(AttributeTargets.Property)]
public sealed class PropAttribute : Attribute
{
    public bool Required { get; set; }
    public object? Default { get; set; }
}
```

| 属性 | 说明 |
|---|---|
| `Required` | 标记为必填 Prop，编译期校验 |
| `Default` | 默认值（也可用初始化器） |

### IComponentLifecycle

```csharp
namespace Square.Runtime;

public interface IComponentLifecycle
{
    void OnPropChanged(string name);
    void OnAttached();
    void OnDetached();
    void OnLoaded();
    void OnUnloaded();
}
```

`Element` 实现此接口。通过显式接口调用触发生命周期。

### Signal\<T\>

```csharp
namespace Square.Runtime.Signals;

public sealed class Signal<T>
{
    public Signal(T initialValue);
    public Signal();

    public T Value { get; set; }
    public bool Publish(T value, bool force = false);
    public T Update(Func<T, T> update, bool force = false);
    public IDisposable Subscribe(Action<T> handler, Dispatcher? dispatcher = null, bool emitCurrent = false);
}
```

| 成员 | 说明 |
|---|---|
| `Value` | get 线程安全读取；set 等同 `Publish(value)` |
| `Publish(value, force)` | 更新值并通知订阅者；同值默认不通知，`force: true` 强制通知 |
| `Update(fn, force)` | 在锁内原子计算新值，锁外通知 |
| `Subscribe(handler, dispatcher, emitCurrent)` | 订阅值变化；指定 `dispatcher` 时跨线程回调排队到该 Dispatcher；`emitCurrent: true` 订阅时立即投递当前值 |

### SignalHub

```csharp
namespace Square.Runtime.Signals;

public sealed class SignalHub
{
    public static SignalHub Default { get; }

    public Signal<T> Get<T>(string name, T initialValue = default!);
    public bool Remove<T>(string name);
}
```

| 成员 | 说明 |
|---|---|
| `Default` | 进程级共享实例 |
| `Get<T>(name, initial)` | 按名称获取或创建强类型 Signal；同名类型冲突抛 `InvalidOperationException` |
| `Remove<T>(name)` | 移除指定 Signal |

---

## 3. Square.Events — DOM 事件（Web API）

对齐 MDN：`EventTarget` / `Event` / `addEventListener` / `dispatchEvent`。
**已删除** WPF 风格 `RoutedEvent` / `RaiseEvent` / `Handled` / `RoutingStrategy`（非改名，硬切）。

### EventPhase

```csharp
namespace Square.Events;

public enum EventPhase
{
    None = 0,
    CapturingPhase = 1,
    AtTarget = 2,
    BubblingPhase = 3
}
```

### Event / EventInit

```csharp
namespace Square.Events;

public sealed class EventInit
{
    public bool Bubbles { get; init; }
    public bool Cancelable { get; init; }
    public bool Composed { get; init; }
}

public class Event
{
    public Event(string type, EventInit? init = null);

    public string Type { get; }
    public EventTarget? Target { get; }
    public EventTarget? CurrentTarget { get; }
    public EventPhase EventPhase { get; }
    public bool Bubbles { get; }
    public bool Cancelable { get; }
    public bool Composed { get; }
    public bool DefaultPrevented { get; }
    public bool IsTrusted { get; }
    public double TimeStamp { get; }

    public void PreventDefault();
    public void StopPropagation();
    public void StopImmediatePropagation();
    public IReadOnlyList<EventTarget> ComposedPath();
}
```

| 成员 | 说明 |
|---|---|
| `Target` | 派发目标（原 `OriginalSource`/`Source`） |
| `CurrentTarget` | 当前处理该事件的 `EventTarget` |
| `EventPhase` | 捕获 / 目标 / 冒泡 |
| `StopPropagation` | 停止继续传播（替代旧 `Handled`） |
| `PreventDefault` | 可取消事件上阻止默认行为 |

### EventTarget

```csharp
namespace Square.Events;

public class EventTarget
{
    public void AddEventListener(string type, Action<Event>? listener, AddEventListenerOptions? options = null);
    public void AddEventListener(string type, Action<Event>? listener, bool useCapture);
    public void AddEventListener(string type, Action handler, AddEventListenerOptions? options = null);
    public void AddEventListener<TEvent>(string type, Action<TEvent> handler, AddEventListenerOptions? options = null)
        where TEvent : Event;

    public void RemoveEventListener(string type, Action<Event>? listener, bool useCapture = false);
    public void RemoveEventListener(string type, Action handler, bool useCapture = false);

    public bool DispatchEvent(Event e);
    public bool DispatchTrusted(Event e); // IsTrusted = true（平台输入）
}
```

`AddEventListenerOptions`：`Capture`、`Once`、`Passive`、`Signal`（`CancellationToken`）。

传播：捕获 → 目标 →（若 `Bubbles`）冒泡。父链由 `GetEventParent()` 提供（`Element.Parent`，否则 `OwnerDocument`）。

### StandardEvents

```csharp
namespace Square.Events;

public static class StandardEvents
{
    public const string PointerDown = "pointerdown";
    public const string PointerUp = "pointerup";
    public const string PointerMove = "pointermove";
    public const string Wheel = "wheel";
    public const string KeyDown = "keydown";
    public const string KeyUp = "keyup";
    public const string Click = "click";
    public const string Change = "change";
    public const string Input = "input";
    public const string Focus = "focus";      // 不冒泡
    public const string Blur = "blur";        // 不冒泡
    public const string FocusIn = "focusin";  // 冒泡
    public const string FocusOut = "focusout";
    public const string RequestFrame = "requestframe"; // 框架扩展

    public static Event Create(string type);
    public static Event CreateClick();
    public static Event CreatePointerDown();
    // … 其它 Create* 工厂
    public static FrameRequestEvent CreateRequestFrame(double framesPerSecond = 60d);
}
```

### FrameRequestEvent

```csharp
namespace Square.Events;

public sealed class FrameRequestEvent : Event
{
    public double FramesPerSecond { get; }
    public double IntervalSeconds { get; }
}
```

---

## 4. Square.UI — 元素树 API

### Element

```csharp
namespace Square.UI;

public abstract class Element : EventTarget, IComponentLifecycle, ILayoutLifecycle
{
    public PropertyStore Properties { get; }
    public StyleAccessor Style { get; }
    public ClassListAccessor ClassList { get; }
    public ChildrenCollection Children { get; }
    public virtual string TagName { get; }
    public string? Id { get; set; }
    public Document? OwnerDocument { get; }
    public Element? Parent { get; }
    public Element? ParentNode { get; }      // 同 Parent
    public Element? ParentElement { get; }   // 同 Parent
    public Element? FirstElementChild { get; }
    public Element? LastElementChild { get; }
    public int ChildElementCount { get; }
    public Rect Geometry { get; set; }
    public bool IsVisible { get; set; }
    public bool IsLayoutDirty { get; }
    public bool NeedsPaint { get; }
    public virtual int ZIndex { get; set; }
    public ElementState State { get; }
    public bool IsAttached { get; }
    public bool IsLoaded { get; }

    public void SetState(ElementState flag, bool on);
    public bool HasState(ElementState flag);

    public Element AppendChild(Element child);
    public Element InsertBefore(Element newChild, Element? referenceChild);
    public Element RemoveChild(Element child);
    public void ReplaceChildren(params Element[] nodes);
    public Rect GetBoundingClientRect();

    public T? GetProperty<T>(string name);
    public void SetProperty<T>(string name, T value);
    public void BindProperty<T>(string name, Func<T> getter);
    public void BindProperty<T>(string name, ObservableValue<T> source);

    // 事件：继承 EventTarget（AddEventListener / DispatchEvent）

    public virtual Element? HitTest(Point point);\n    public bool ClipsOverflow();
    public Rect GetOverflowClipRect();

    public T? Query<T>(string? className = null) where T : Element;
    public List<T> QueryAll<T>(string? className = null) where T : Element;

    public void InvalidateLayout();
    public void InvalidatePaint();
    public void ClearLayoutDirty();
    public void ClearPaintDirty();

    public virtual Size Measure(Size availableSize);
    public virtual void Arrange(Rect finalRect);
    public virtual void Paint(IRenderContext ctx);
    public virtual void BuildElementTree();

    protected virtual void OnPropertyChanged(string name);
    protected virtual void OnPropChanged(string name);
    protected virtual void OnAttachedCore();
    protected virtual void OnDetachedCore();
}
```

### Document / UIDocument

```csharp
namespace Square.UI;

public abstract class Document : EventTarget
{
    public Element DocumentElement { get; }   // 只读
    public string Title { get; set; }
    public Element? GetElementById(string id);
    public T? GetElementById<T>(string id) where T : Element;
    public List<T> GetElementsByTagName<T>(string tagName) where T : Element;
    public List<T> GetElementsByClassName<T>(string className) where T : Element;
    public T? Query<T>(string? className = null) where T : Element;
    public List<T> QueryAll<T>(string? className = null) where T : Element;
}

public sealed class UIDocument : Document
{
    public UIRootElement Ui { get; }     // documentElement，TagName "UI"
    public UIHeadElement Head { get; }   // 元数据；本阶段不参与布局
    public UIBodyElement Body { get; }   // 窗口客户区内容宿主

    public static void RegisterElement(string tagName, Func<Element> factory);
    public Element CreateElement(string tagName);
    public T CreateElement<T>() where T : Element, new();
    public void Build();                 // 对 Body 子树 BuildElementTree
}
```

壳结构：`DocumentElement = UI`（只读）→ 子节点 `Head` + `Body`。应用内容挂在 `Body` 下，**不是** documentElement。

### Range

```csharp
namespace Square.UI;

public sealed class Range
{
    public Range(Document ownerDocument);

    public Document OwnerDocument { get; }
    public Node StartContainer { get; }
    public int StartOffset { get; }
    public Node EndContainer { get; }
    public int EndOffset { get; }
    public bool Collapsed { get; }

    public void SetStart(Node node, int offset);
    public void SetEnd(Node node, int offset);
    public void SelectNodeContents(Node node);
    public void Collapse(bool toStart);
}
```

| 成员 | 说明 |
|---|---|
| `StartContainer` / `StartOffset` | 起始边界点（节点 + 偏移） |
| `EndContainer` / `EndOffset` | 结束边界点 |
| `Collapsed` | 起始与结束边界点重合 |
| `SetStart` / `SetEnd` | 设置边界点；越界或跨文档抛异常，起>终时自动折叠 |
| `SelectNodeContents` | 选中某节点的全部内容 |
| `Collapse(toStart)` | 折叠到起始或结束边界点 |
| `ToString()` | 提取范围内文本（遍历公共根下的文本节点） |

最小 DOM Range 模型，用于文本选择。边界点按文档顺序比较，支持祖先/兄弟关系判定。

### HTMLElement / XMLDocument / SVGElement

```csharp
namespace Square.UI;

public abstract class HTMLElement : Element
{
    public override string? NamespaceURI => "http://www.w3.org/1999/xhtml";
}

public abstract class XMLDocument : Document
{
    public string ContentType { get; }
    public string XmlVersion { get; set; }
    public string XmlEncoding { get; set; }
    public bool XmlStandalone { get; set; }
}

namespace Square.UI.Svg;

public abstract class SVGElement : Element
{
    public override string? NamespaceURI => "http://www.w3.org/2000/svg";
}
```

`HTMLElement` 目前仍是 HTML DOM 扩展点。`SVGElement` 已用于真实 SVG DOM；具体元素及 `SVGDocument` 见“SVGDocument 与模板 SVG”。

### UIElement

```csharp
namespace Square.UI;

public abstract class UIElement : Element
{
    public SlotCollection Slots { get; }
    public HorizontalAlignment HorizontalAlign { get; set; }
    public VerticalAlignment VerticalAlign { get; set; }

    public float Width { get; set; }       // float.NaN = auto
    public float Height { get; set; }      // float.NaN = auto
    public float MinWidth { get; set; }
    public float MinHeight { get; set; }
    public float MaxWidth { get; set; }
    public float MaxHeight { get; set; }

    public float MarginLeft { get; set; }
    public float MarginTop { get; set; }
    public float MarginRight { get; set; }
    public float MarginBottom { get; set; }

    public float PaddingLeft { get; set; }
    public float PaddingTop { get; set; }
    public float PaddingRight { get; set; }
    public float PaddingBottom { get; set; }

    public bool IsDisabled { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsFocused { get; }
    public string? Tooltip { get; set; }

    public void Focus();
    public void Unfocus();
}
```

### ElementState

```csharp
namespace Square.UI;

[Flags]
public enum ElementState : byte
{
    None = 0, Hover = 1, Focus = 2, Active = 4, Disabled = 8, Checked = 16, Empty = 32
}
```

### StyleAccessor

```csharp
namespace Square.UI.ElementApi;

public sealed class StyleAccessor
{
    public void Set(string property, string value);
    public bool SetCascaded(string property, string value, int specificity);
    public string? Get(string property);
    public void Remove(string property);
    public void Clear();
    public void ClearCascaded();
    public IReadOnlyDictionary<string, string> GetAll();
}
```

### ClassListAccessor

```csharp
namespace Square.UI.ElementApi;

public sealed class ClassListAccessor
{
    public void Add(string className);
    public void Remove(string className);
    public void Toggle(string className);
    public void Toggle(string className, bool force);
    public bool Contains(string className);
    public string ToClassString();
    public void Clear();
    public IReadOnlyCollection<string> GetAll();
}
```

### ChildrenCollection

```csharp
namespace Square.UI.ElementApi;

public sealed class ChildrenCollection : IList<Element>
{
    public Element this[int index] { get; }
    public int Count { get; }

    public void Add(Element item);
    public void AddRange(IEnumerable<Element> items);
    public void Insert(int index, Element item);
    public void InsertBefore(Element newChild, Element refChild);
    public bool Remove(Element item);
    public void RemoveAt(int index);
    public void Clear();
    public int IndexOf(Element item);
    public bool Contains(Element item);
}
```

> 添加子节点时自动触发 `OnAttached`/`OnLoaded`；移除时触发 `OnUnloaded`/`OnDetached`。

### SlotCollection

```csharp
namespace Square.UI;

public delegate void RenderFragment(Element parent);

public sealed class SlotCollection
{
    public void Set(string? name, RenderFragment fragment);
    public bool Contains(string? name);
    public bool Render(string? name, Element parent);
}
```

### PropertyStore

```csharp
namespace Square.UI.Properties;

public sealed class PropertyStore
{
    public bool TryGetValue<T>(string name, out T value);
    public void SetValue<T>(string name, T value);
    public bool HasValue(string name);
    public void MarkBound(string name);
}
```

---

## 5. Square.Controls — 控件

### 内置控件一览

| 控件 | 基类 | 关键属性 |
|---|---|---|
| `View` | `UIElement` | — |
| `ScrollViewer` | `View` | `HorizontalOffset`, `VerticalOffset`, `ExtentWidth/Height`, `ViewportWidth/Height`, `ScrollableWidth/Height`, `ScrollTo()` |
| `Popup` | `View` | `Anchor`, `Placement`, `Alignment`, `IsOpen`, `DismissOnPointerDownOutside`, `Open()`, `Close()` |
| `Dialog` | `Popup` | `IsModal`, `CloseOnBackdropClick`, `CloseOnEscape`, `BackdropColor` |
| `MenuBar` | `View` | `ActiveIndex`, `IsMenuModeActive`, `CloseMenus()` |
| `Menu` | `Popup` | `OwnerItem`, `ActiveItem`, `Items`, `OpenFor()`, `OpenAt()`, `CloseMenuTree()` |
| `ContextMenu` | `Menu` | `OpenAt(Point)` |
| `MenuItem` | `UIElement` | `TextContent`, `ShortcutText`, `Icon`, `IsCheckable`, `IsChecked`, `GroupName`, `StaysOpenOnClick`, `Command`, `Submenu` |
| `MenuSeparator` | `UIElement` | — |
| `Text` | `UIElement` | `TextContent`, `Color`, `FontSize` |
| `FontIcon` | `Text` | `Glyph`, `FontFamily` |
| `Splitter` | `UIElement` | `Value`, `Minimum`, `Maximum`, `IsVertical`, `IsReversed` |
| `SplitContainer` | `View` | `First`, `Second`, `Splitter`, `Value`, `Minimum`, `Maximum`, `SplitterThickness`, `IsVertical`, `IsSeamless` |
| `ListItem` | `UIElement` | `TextContent`, `Marker`, `Color`, `FontSize`（类似 HTML `li`） |
| `Link` | `UIElement` | `TextContent`, `Href`, `Color`, `FontSize`, `Underline`（类似 HTML `a`；应用内路由导航见 `Square.Extensions.Routing.RouterLink`） |
| `Button` | `UIElement` | `TextContent`, `Background`, `Foreground` |
| `CheckBox` | `UIElement` | `IsChecked`, `TextContent` |
| `Radio` | `UIElement` | `IsChecked`, `TextContent`, `GroupName` |
| `Select` | `UIElement` | `Value`, `Options`, `Placeholder` |
| `Image` | `UIElement` | `Source`, `ImageContent` |
| `Canvas` | `UIElement` | `DrawContent` |
| `Input` | `TextEditorBase` | `Value`, `Placeholder` |
| `TextArea` | `TextEditorBase` | `Value`, `Placeholder` |

### ScrollViewer

`ScrollViewer` 复用通用 CSS overflow 管线，默认 `overflow-x: hidden`、`overflow-y: auto`。滚轮默认动作会滚动最近仍可继续滚动的祖先容器；偏移变化派发不冒泡的 `scroll` 事件。

```csharp
public class ScrollViewer : View
{
    public float HorizontalOffset { get; }
    public float VerticalOffset { get; }
    public float ExtentWidth { get; }
    public float ExtentHeight { get; }
    public float ViewportWidth { get; }
    public float ViewportHeight { get; }
    public float ScrollableWidth { get; }
    public float ScrollableHeight { get; }

    public void ScrollTo(float horizontalOffset, float verticalOffset);
    public void ScrollToTop();
    public void ScrollToBottom();
}
```

### Popup

`Popup` 的内容保留在 Element Tree 中参与样式和布局，但由 DisplayTree 在顶层 popup layer 重放，不占用其锚点附近的普通布局空间。当前支持相对锚点的上、下、左、右定位，起始、居中、结束对齐，命令式打开关闭和可配置的点击外部关闭。

```csharp
public enum PopupPlacement { Bottom, Top, Left, Right }
public enum PopupAlignment { Start, Center, End }

public class Popup : View
{
    public Element? Anchor { get; set; }
    public PopupPlacement Placement { get; set; }
    public PopupAlignment Alignment { get; set; }
    public float HorizontalOffset { get; set; }
    public float VerticalOffset { get; set; }
    public bool DismissOnPointerDownOutside { get; set; }
    public bool IsOpen { get; set; }

    public Rect PopupBounds { get; }
    public void Open();
    public void Close();
}
```

状态变化分别派发不冒泡的 `open` / `close` 事件。当前阶段不包含视口边缘自动翻转、悬停触发和弹出动画。

Popup 后代的 `Geometry`、文本选择和光标矩形均使用内容局部坐标。`DisplayTree` 负责顶层绘制和命中，桌面宿主负责指针与 IME 矩形的窗口坐标转换；应用代码不应手动叠加 `PopupBounds`。

### Dialog

`Dialog` 基于 `Popup` 顶层渲染。默认以 `UIDocument.Body` 为视口居中，绘制 modal backdrop 并阻断下层命中。打开时保存当前焦点并聚焦对话框内第一个可交互控件；关闭时恢复原焦点。

```csharp
public class Dialog : Popup
{
    public bool IsModal { get; set; }                 // 默认 true
    public bool CloseOnBackdropClick { get; set; }    // 默认 false
    public bool CloseOnEscape { get; set; }           // 默认 true
    public Color BackdropColor { get; set; }
}
```

Escape 关闭由宿主选择最上层允许关闭的 popup；关闭遮罩的 pointerdown 使用关闭前的 popup 命中结果，因此不会点击穿透到背景控件。当前阶段不包含 Tab 焦点陷阱、拖拽和打开/关闭动画。

### Splitter

`Splitter` 是可拖动调整数值的布局分隔条：按住拖动时更新 `Value` 并派发 `input`（拖动中）与 `change`（松开）事件，值被 `Minimum`/`Maximum` 钳制。自身只负责拖动与数值，不调整任何布局——相邻面板的尺寸联动由 `SplitContainer` 或调用方监听事件完成。

```csharp
public class Splitter : UIElement
{
    public float Value { get; set; }        // 当前分隔值，通常表示相邻面板尺寸
    public float Minimum { get; set; }      // 默认 160
    public float Maximum { get; set; }      // 默认 640
    public bool IsVertical { get; set; }    // 默认 true（垂直条调宽度）
    public bool IsReversed { get; set; }    // 反转拖动方向
}
```

样式：`Splitter` 自身不绘制内容，仅通过 CSS `background`（含 `rgba` 半透明与 `:hover`/`:active` 伪类）和 `::before`/`::after` 伪元素（支持 `content: ""` 装饰盒子）定制外观；光标随 `IsVertical` 自动切换 `col-resize` / `row-resize`。

### SplitContainer

`SplitContainer` 是带内置分隔条的两栏容器：拖动分隔条自动调整两侧面板尺寸，无需手动同步布局。

```xml
<SplitContainer value="340" minimum="200" maximum="470" splitter-thickness="8">
  <View slot="first" ...>…</View>
  <View slot="second" ...>…</View>
</SplitContainer>
```

```csharp
public class SplitContainer : View
{
    public View First { get; }              // 左/上面板（slot="first"）
    public View Second { get; }             // 右/下面板（slot="second"）
    public Splitter Splitter { get; }       // 中间分隔条
    public bool IsVertical { get; set; }    // 默认 true（左右分栏）
    public float Value { get; set; }        // 第一面板尺寸
    public float Minimum { get; set; }
    public float Maximum { get; set; }
    public float SplitterThickness { get; set; }  // 分隔条厚度，默认 8px
    public bool IsSeamless { get; set; }    // 默认 true
}
```

- **无缝模式（`IsSeamless="true"`，默认）**：两侧面板各向分隔条延伸一半厚度，覆盖接缝实现无缝相接；分隔条置顶（`z-index: 2`），透明时面板直接相连，`hover` 高亮等样式覆盖在接缝上。拖动时两面板同步伸缩并始终填满容器。
- **有缝模式（`IsSeamless="false"`）**：分隔条独立显示在面板之间，可见间隙等于 `SplitterThickness`，可直接用 `splitter-thickness` 控制缝隙大小。
- 水平分栏用 `vertical="false"`，面板高度由 `Value` 控制。
- 面板内容通过 sqx 插槽 `slot="first"` / `slot="second"` 注入；内部 `Splitter` 带有 `splitter` class，可用类型选择器 `Splitter`、伪类 `Splitter:hover` 与伪元素 `Splitter::after` 设置样式。

### MenuBar / Menu / ContextMenu

菜单使用显式嵌套结构。顶级 `MenuBar` 项的 `Menu` 向下展开，普通 `MenuItem` 的嵌套 `Menu` 向右展开；空间不足时自动翻转。

```xml
<MenuBar>
  <MenuItem text="File">
    <Menu>
      <MenuItem text="New" shortcut="Ctrl+N" onClick={CreateDocument} />
      <MenuSeparator />
      <MenuItem text="Export">
        <Menu>
          <MenuItem text="PNG" onClick={ExportPng} />
          <MenuItem text="SVG" onClick={ExportSvg} />
        </Menu>
      </MenuItem>
    </Menu>
  </MenuItem>
</MenuBar>
```

MenuItem 角色由属性推导：

| 属性 | 角色 |
|---|---|
| 无 `checkable` / `group` | 普通命令 |
| `checkable="true"` | 独立 Check，可与其他项同时选中 |
| `group="name"` | Radio，同一根菜单树内同 GroupName 互斥 |
| 内含 `<Menu>` | 子菜单入口 |

叶子项先派发可取消的 `click`；未取消时更新 Check/Radio 状态、派发 `change`、执行可选 `Command`，并默认关闭整条菜单链。`StaysOpenOnClick` 可保持菜单展开。`ShortcutText` 当前只负责显示，不注册全局快捷键。

键盘支持 Up/Down/Home/End、Left/Right、Enter/Space、Escape 和 Tab。`ContextMenu.OpenAt(point)` 已可命令式使用；自动右键触发将在平台输入增加鼠标按键字段后接入。

### Text

```csharp
namespace Square.Controls;

public class Text : UIElement
{
    public string TextContent { get; set; }
    public Color Color { get; set; }        // 默认 Black
    public float FontSize { get; set; }     // 默认 16f

    public Text() { }
    public Text(string text);

    public override Size Measure(Size availableSize);
    public override void Render(IRenderContext ctx);
}
```

### Button

```csharp
namespace Square.Controls;

public class Button : UIElement
{
    public string TextContent { get; set; }
    public Color Background { get; set; }   // 默认 #0078d4
    public Color Foreground { get; set; }   // 默认 White

    public Button() { }
    public Button(string text);

    public override Size Measure(Size availableSize);
    public override void Render(IRenderContext ctx);
}
```

默认 UA `appearance: auto`，对齐 Chrome `html.css` 浅色表单控件：`ButtonFace` 背景、`2px outset ButtonBorder`；`:active` 把边框改成 `inset`；`:disabled` 用半透明灰。Chrome UA 没有 `button:hover` 颜色规则。`appearance: none` 不自动清掉 UA 边框/背景。组件 CSS 仍可覆盖这些规则。

### CheckBox

```csharp
namespace Square.Controls;

public class CheckBox : UIElement
{
    public bool IsChecked { get; set; }
    public string TextContent { get; set; }
}
```

点击时自动切换 `IsChecked` 并触发 `change` 事件。

### Radio

```csharp
namespace Square.Controls;

public class Radio : UIElement
{
    public bool IsChecked { get; set; }
    public string TextContent { get; set; }
    public string GroupName { get; set; }
}
```

同组 `GroupName` 内互斥选中。

### Select

```csharp
namespace Square.Controls;

public class Select : UIElement
{
    public string Value { get; set; }
    public string[] Options { get; set; }
    public string Placeholder { get; set; }  // 默认 "Select"
    public bool IsOpen { get; }

    public override int ZIndex { get; set; }  // 打开时自动提升到 1000

    public void HandlePointerDown(Point point);
    public bool HandlePointerMove(Point point);
    public void CloseDropDown();
}
```

### Image

```csharp
namespace Square.Controls;

public class Image : UIElement
{
    public string Source { get; set; }
    public Square.Graphics.Image? ImageContent { get; set; }
    public Exception? Error { get; }
}
```

`Source` 通过 `ImageSourceLoaderRegistry` 异步解析。核心 `Square` 只定义 `IImageFrameSource`、`IImageSourceLoader` 和注册表；引用 `Square.Images` 后会注册本地文件加载器，原有相对路径解析行为保持不变。HTTP(S) 和嵌入资源加载器须显式注册：

```csharp
ImageSourceRegistration.RegisterHttp(httpClient, decoderOptions);
ImageSourceRegistration.RegisterEmbeddedResources(typeof(App).Assembly, decoderOptions);

image.Source = "https://example.com/image.png";
image.Source = "embedded://My.App/My.App.Assets.image.png";
```

HTTP 加载器使用但不释放调用方注入的 `HttpClient`。嵌入资源 URI 的主机名必须匹配显式注册程序集的简单名称，路径是完整 manifest resource name；实现不会按名称动态加载程序集，兼容 NativeAOT。两种加载器都会在读取时执行 `ImageDecoderOptions.MaxEncodedBytes` 限制并传播取消。`data:` URI 仍不支持。

通过 `Source` 加载的图片资源由控件拥有，改变 `Source` 或卸载控件会取消旧请求并释放旧文档。动画在控件挂载且可见时自动播放，隐藏时暂停并从剩余帧时长恢复。成功派发冒泡的 `load`，失败设置只读 `Error` 并派发冒泡的 `loaderror`；失败不会在 UI Dispatcher 上抛出。

`ImageContent` 仍可直接接收调用方管理的 `Bitmap` 或 `VectorImage`，不会转移所有权。手动内容优先显示并禁用 `Source` 自动加载，此时 `Source` 仅作为替代文本；清空 `ImageContent` 后会重新加载非空 `Source`。

### Canvas

```csharp
namespace Square.Controls;

public class Canvas : UIElement
{
    public Action<IRenderContext, Rect>? DrawContent { get; set; }

    public void RequestFrame(double fps = 60d);
    public void RequestAnimationFrame(Action<IRenderContext, Rect> callback);
    public void RequestAnimationFrame(Action<IRenderContext, Rect> callback, double fps);
}
```

`RequestFrame()` 通过 `StandardEvents.RequestFrame` 冒泡并合并，`DesktopApplication` 在 Tick 中检查调度帧并触发重绘。

`FrameRequestEvent` 同时支持 FPS 构造和精确 `TimeSpan` 延迟。图片动画使用精确延迟，以保留不规则或长帧时长。

### ITextEditor / TextEditorBase

```csharp
namespace Square.Controls;

public interface ITextEditor
{
    int CaretIndex { get; }
    int SelectionStart { get; }
    int SelectionLength { get; }
    string SelectedText { get; }
    Rect CaretRect { get; }

    void HandleTextInput(string text);
    void HandleKey(int keyCode, bool shift = false, bool control = false);
    void HandlePointerDown(Point point, bool extendSelection = false);
    void HandlePointerMove(Point point);
    void HandlePointerUp(Point point);
    void SelectAll();
    bool DeleteSelection();
    bool ToggleCaretBlink();
    void ResetCaretBlink();
}

public abstract class TextEditorBase : UIElement, ITextEditor
{
    public string Value { get; set; }
    public string Placeholder { get; set; }
    public Color SelectionBackground { get; set; }
    public Color SelectionForeground { get; set; }
}

public class Input : TextEditorBase { protected override bool IsMultiline => false; }
public class TextArea : TextEditorBase { protected override bool IsMultiline => true; }
```

光标与选择高亮共享同一视觉行盒，其高度为 CSS `line-height` 与字体自然行高的较大值，因此单行和多行编辑器中的光标、高亮及 IME 定位保持一致。

### 结构原语

```csharp
namespace Square.Controls.Primitives;

public sealed class ShowNode : IDisposable
{
    public ShowNode(ObservableValue<bool> source, Func<Element?> build);
    public ShowNode(Func<bool> condition, Func<Element?> build);
    public void AttachTo(Element parent);
    public void Update();
    public void Dispose();
}

public static class ForNode
{
    public static IForNode Create<T>(ObservableCollection<T> source, Func<T, Element?> build);
    public static IForNode Create<T>(IEnumerable<T> source, Func<T, Element?> build);
}

public sealed class SwitchNode : IDisposable
{
    public SwitchNode(Func<int> selector);
    public void AddBranch(Func<bool> condition, Func<Element?> build);
    public void AddDefault(Func<Element?> build);
    public void AttachTo(Element parent);
    public void Update();
    public void Dispose();
}
```

---

## 6. Square.Graphics — 绘图抽象

### IRenderContext

```csharp
namespace Square.Graphics;

public interface IRenderContext : IDisposable
{
    Size CanvasSize { get; }
    float DpiScale { get; }

    void PushTransform(Matrix3x2 matrix);
    void PopTransform();

    void PushClip(Rect rect);
    void PushClip(Geometry geometry);
    void PopClip();

    void FillRect(Rect rect, Brush brush);
    void DrawRect(Rect rect, Pen pen);
    void FillPath(PathGeometry path, Brush brush);
    void DrawPath(PathGeometry path, Pen pen);
    void FillGeometry(Geometry geometry, Brush brush);
    void DrawGeometry(Geometry geometry, Pen pen);
    void DrawText(TextLayout text, Point origin, Brush brush);
    void DrawImage(Image image, Rect dest, Rect? source = null);

    void PushLayer(Rect bounds, float opacity);
    void PopLayer();

    void Clear(Color color);
    void Flush();
    void Present();
}
```

### Color

```csharp
namespace Square.Graphics;

public readonly struct Color : IEquatable<Color>
{
    public readonly byte R, G, B, A;

    public Color(byte r, byte g, byte b, byte a = 255);

    public static Color FromRgb(byte r, byte g, byte b);
    public static Color FromRgba(byte r, byte g, byte b, byte a);
    public static Color Parse(string hex);

    public static readonly Color Transparent, Black, White, Red, Green, Blue;

    public uint ToPackedBgra();
}
```

`Parse` 支持 `#RGB`、`#RRGGBB`、`#AARRGGBB` 格式。

### Rect / Size / Point

```csharp
namespace Square.Graphics;

public readonly struct Rect : IEquatable<Rect>
{
    public readonly float X, Y, Width, Height;

    public Rect(float x, float y, float width, float height);
    public Rect(Point pos, Size size);

    public float Left { get; }
    public float Top { get; }
    public float Right { get; }
    public float Bottom { get; }
    public Point Position { get; }
    public Size Size { get; }
    public Point Center { get; }
    public bool IsEmpty { get; }

    public bool Contains(Point p);
    public bool Contains(float px, float py);
    public bool IntersectsWith(Rect other);

    public static Rect Union(Rect a, Rect b);
    public static Rect Intersect(Rect a, Rect b);

    public Rect Offset(float dx, float dy);
    public Rect Inflate(float dx, float dy);

    public static readonly Rect Empty;
}

public readonly struct Size
{
    public readonly float Width, Height;
    public Size(float width, float height);
    public static readonly Size Zero;
}

public readonly struct Point
{
    public readonly float X, Y;
    public Point(float x, float y);
}
```

### Brush

```csharp
namespace Square.Graphics;

public abstract class Brush
{
    public static SolidColorBrush FromColor(Color color);
}

public sealed class SolidColorBrush : Brush
{
    public Color Color { get; set; }
    public SolidColorBrush(Color color);
    public SolidColorBrush(byte r, byte g, byte b, byte a = 255);
}

public sealed class LinearGradientBrush : Brush
{
    public Point Start { get; set; }
    public Point End { get; set; }
    public GradientStop[] Stops { get; set; }
    public GradientSpreadMethod SpreadMethod { get; set; }
}

public sealed class RadialGradientBrush : Brush
{
    public Point Center { get; set; }
    public float Radius { get; set; }
    public GradientStop[] Stops { get; set; }
    public GradientSpreadMethod SpreadMethod { get; set; }
}

public sealed class GradientStop
{
    public float Offset { get; set; }
    public Color Color { get; set; }
}
```

### Pen

```csharp
namespace Square.Graphics;

public sealed class Pen
{
    public Brush Brush { get; set; }
    public float Width { get; set; }
    public StrokeStyle? StrokeStyle { get; set; }

    public Pen(Brush brush, float width = 1f, StrokeStyle? style = null);
    public static Pen FromColor(Color color, float width = 1f);
}
```

### Font

```csharp
namespace Square.Graphics;

public sealed class Font
{
    public string Family { get; set; }
    public float Size { get; set; }
    public FontWeight Weight { get; set; }
    public FontStyle Style { get; set; }

    public Font();
    public Font(string family, float size);
    public Font(string family, float size, FontWeight weight, FontStyle style = FontStyle.Normal);

    public Font WithSize(float size);
    public Font WithWeight(FontWeight weight);
}

public enum FontWeight : ushort { Thin=100, ExtraLight=200, Light=300, Normal=400, Medium=500, SemiBold=600, Bold=700, ExtraBold=800, Black=900 }
public enum FontStyle : byte { Normal, Italic, Oblique }
public enum TextAlignment : byte { Left, Center, Right, Justify }
```

### Geometry / PathGeometry

```csharp
namespace Square.Graphics;

public abstract class Geometry { }

public sealed class RectGeometry : Geometry { public Rect Rect { get; set; } }
public sealed class RoundedRectGeometry : Geometry { public Rect Rect; public float RadiusX; public float RadiusY; }
public sealed class EllipseGeometry : Geometry { public Point Center; public float RadiusX; public float RadiusY; }

public sealed class PathGeometry : Geometry
{
    public IReadOnlyList<PathCommand> Commands { get; }

    public PathGeometry MoveTo(Point p);
    public PathGeometry LineTo(Point p);
    public PathGeometry ArcTo(Rect oval, float startAngle, float sweepAngle);
    public PathGeometry Close();

    public static PathGeometry Create();
}
```

### TextLayout

```csharp
namespace Square.Graphics;

public sealed class TextLayout
{
    public string Text { get; set; }
    public Font Font { get; set; }
    public Size MaxSize { get; set; }
    public TextAlignment Alignment { get; set; }
    public float LineHeight { get; set; }   // 倍数，默认 DefaultLineHeight

    public Size Measure();
    public static float DefaultLineHeight { get; }
    public static float MeasureRuneAdvance(Rune rune, float fontSize);
}
```

### IRenderBackendFactory / RenderBackendRegistry

```csharp
namespace Square.Graphics;

public interface IRenderBackendFactory
{
    string Name { get; }
    IRenderContext CreateContext(RenderContextCreateInfo info);
}

public static class RenderBackendRegistry
{
    public static void Register(IRenderBackendFactory factory);
    public static IRenderBackendFactory Get(string name);
    public static IRenderBackendFactory Default { get; }
    public static bool TryGet(string name, out IRenderBackendFactory? factory);
    public static IReadOnlyCollection<string> AvailableNames { get; }
}
```

### BitmapPngEncoder

```csharp
namespace Square.Graphics.Codecs;

public static class BitmapPngEncoder
{
    public static void Save(Bitmap bitmap, string path);
    public static void Save(Bitmap bitmap, Stream stream);
}
```

| 成员 | 说明 |
|---|---|
| `Save(bitmap, path)` | 将 `Bitmap` 编码为 PNG 写入文件 |
| `Save(bitmap, stream)` | 将 `Bitmap` 编码为 PNG 写入流 |

输出 8 位 RGBA PNG（IHDR + zlib 压缩 IDAT + IEND），纯 C# 实现，含 CRC32 校验。

### SvgImage

```csharp
namespace Square.Graphics.Svg;

public sealed class SvgImage : VectorImage
{
    public static SvgImage Load(string path);
    public static SvgImage Load(Stream stream);
    public static SvgImage Parse(string svg);
}
```

`SvgImage` 是核心 `Square.Graphics` 中的纯 C# 静态 SVG 实现，可直接赋给 `Square.Controls.Image.ImageContent`。当前支持 `svg`、`g`、`rect`、`circle`、`ellipse`、`line`、`polyline`、`polygon`、`path`，以及 `viewBox`、基础变换、填充、描边、透明度和内联 `style`。路径支持直线、三次贝塞尔和二次贝塞尔命令；脚本、外部资源、滤镜、文本、渐变和动画暂不支持。

```csharp
using var icon = SvgImage.Load("icon.svg");
image.ImageContent = icon;
```

SVG 通过现有矢量绘制命令渲染，不依赖反射、动态代码或原生库，支持 trimming 和 NativeAOT。PNG、JPEG、GIF、WebP 等其他图片格式归属独立的 `Square.Images` 模块。

### SVGDocument 与模板 SVG

```csharp
namespace Square.UI;

public abstract class XMLDocument : Document
{
    public string ContentType { get; }
    public string XmlVersion { get; set; }
    public string XmlEncoding { get; set; }
    public bool XmlStandalone { get; set; }
}

namespace Square.UI.Svg;

public sealed class SVGDocument : XMLDocument;
public abstract class SVGElement : Element;
public sealed class SVGSVGElement : SVGElement;
public sealed class SVGGElement : SVGElement;
public sealed class SVGPathElement : SVGElement;
public sealed class SVGRectElement : SVGElement;
public sealed class SVGCircleElement : SVGElement;
public sealed class SVGEllipseElement : SVGElement;
public sealed class SVGLineElement : SVGElement;
public sealed class SVGPolylineElement : SVGElement;
public sealed class SVGPolygonElement : SVGElement;
```

SQX 与 SQV 模板可以直接声明 SVG DOM：

```xml
<svg viewBox="0 0 100 100" width="100" height="100">
  <g transform="translate(10 10)" fill="#2b78ee">
    <rect x="0" y="0" width="80" height="80" rx="8" />
    <circle cx="40" cy="40" r="18" fill="#ffffff" />
    <path d="M 25 40 L 36 51 L 58 29" fill="none" stroke="#152241" stroke-width="5" />
  </g>
</svg>
```

模板编译器将标签直接生成为 `Square.UI.Svg` 下对应的浏览器式 SVG 元素类型。每个根 `SVGSVGElement` 持有独立的 `SvgDocument`，该文档继承 `XMLDocument`，`ContentType` 为 `image/svg+xml`，并管理 SVG 内部节点、查询、样式继承、变换、viewBox 与渲染。宿主 UI 文档仅负责根 `<svg>` 的盒布局。

### BmpPngConverter

```csharp
namespace Square.Graphics.Codecs;

public static class BmpPngConverter
{
    public static void Convert(string bmpPath, string pngPath);
    public static Bitmap LoadBmp(string path);
}
```

| 成员 | 说明 |
|---|---|
| `Convert(bmpPath, pngPath)` | 读取 BMP 并写出为 PNG |
| `LoadBmp(path)` | 加载非压缩 24/32 位 BMP 为 `Bitmap`（自上而下/自下而上均支持） |

### Square.Images.ImageDecoder

`Square.Images` 是独立可选包，不由核心 `Square` 反向依赖。包加载时会向核心 `ImageSourceLoaderRegistry` 注册本地文件加载器，使 `<Image source="...">` 能直接显示并播放支持的格式。

```csharp
namespace Square.Images;

public static class ImageDecoder
{
    public static ImageDocument Decode(string path, ImageDecoderOptions? options = null);
    public static ImageDocument Decode(Stream stream, ImageDecoderOptions? options = null);
    public static ImageDocument Decode(ReadOnlySpan<byte> data, ImageDecoderOptions? options = null);
}

public sealed class ImageDocument : IDisposable
{
    public ImageFormat Format { get; }
    public ImageDocumentKind Kind { get; }
    public IReadOnlyList<ImageItem> Items { get; }
    public int PrimaryIndex { get; }
    public ImageItem PrimaryItem { get; }
    public Bitmap PrimaryBitmap { get; }
    public ImageAnimationInfo? Animation { get; }
    public ImageMetadata Metadata { get; }
    public bool IsDisposed { get; }

    public Bitmap GetBitmap(int index);
}

public sealed class ImageItem
{
    public int Index { get; }
    public int Width { get; }
    public int Height { get; }
    public int BitDepth { get; }
    public int SourceBitDepth { get; }
    public TimeSpan Duration { get; }
    public Point? Hotspot { get; }
}

public sealed class ImageAnimationInfo
{
    public bool LoopsForever { get; }
    public int PlayCount { get; }
    public TimeSpan TotalDuration { get; }
}

public sealed class ImageMetadata
{
    public ImageOrientation OriginalOrientation { get; }
    public bool OrientationApplied { get; }
}

public sealed class ImageDecoderOptions
{
    public int MaxWidth { get; init; }
    public int MaxHeight { get; init; }
    public long MaxPixelCount { get; init; }
    public long MaxDecodedBytes { get; init; }
    public long MaxEncodedBytes { get; init; }
    public int MaxChunkBytes { get; init; }
    public int MaxItemCount { get; init; }
    public long MaxTotalDecodedBytes { get; init; }
    public long MaxMetadataBytes { get; init; }
    public int MaxExifTagCount { get; init; }
    public int MaxIfdDepth { get; init; }
    public PngCrcPolicy PngCrcPolicy { get; init; }
    public ExifOrientationPolicy ExifOrientationPolicy { get; init; }
}

public enum PngCrcPolicy
{
    AllChunks,
    CriticalChunksOnly,
    Ignore
}

public enum ExifOrientationPolicy
{
    Apply,
    Ignore
}

public enum ImageDocumentKind
{
    Still,
    Animation,
    Pages,
    Variants
}
```

解码器按文件签名自动识别格式，不依赖扩展名。`ImageDocument` 统一表示单张图片、动画、页面集合和尺寸变体；所有已解码像素均为 top-down、紧密排列、straight-alpha BGRA `Bitmap`。

`ImageDocument` 拥有其全部位图。调用方不应单独释放 `PrimaryBitmap` 或 `GetBitmap(index)` 的返回值；释放文档会统一释放所有项目位图。

自动控件加载：

```xml
<Image source="Assets/animation.webp" />
```

手动解码：

```csharp
using var document = ImageDecoder.Decode("photo.jpg");
image.ImageContent = document.PrimaryBitmap;
```

当前格式支持：

| 格式 | 文档类型 | 支持范围 |
|---|---|---|
| PNG/APNG | `Still` / `Animation` | PNG 灰度、RGB、indexed、Alpha、Adam7；APNG `acTL`/`fcTL`/`fdAT`、帧矩形、时长、循环、source/over blend 和 dispose none/background/previous |
| JPEG | `Still` | 8 位基线顺序 JPEG、灰度和 YCbCr、Huffman、采样与 Exif Orientation |
| BMP | `Still` | Windows BMP，未压缩 24 位 BGR 和 32 位 BGRA，top-down / bottom-up |
| GIF | `Still` / `Animation` | GIF87a/GIF89a 全部帧；完整逻辑画布合成、时长、循环、透明索引、全局/局部调色板、交错、12 位 LZW、disposal 0–3 |
| ICO | `Variants` | 全部目录变体；1/4/8/24/32 位 BMP 嵌入和 PNG 嵌入；主变体选择 |
| CUR | `Variants` | ICO 同类变体解码，并公开热点 |
| WebP | `Still` / `Animation` | simple/VP8X 静态 VP8L、VP8 lossy 关键帧、`ALPH+VP8`，以及 `ANIM`/`ANMF` 内嵌 VP8L、VP8 或 `ALPH+VP8` 动画；EXIF Orientation、ICCP/XMP、帧矩形、时长、循环、blend 和 dispose-to-background |
| TIFF | `Pages` | Classic TIFF 42，大小端，多 IFD 页面，未压缩/LZW/Deflate/Adobe Deflate/PackBits Strip，8 位 Predictor 2，1/8 位灰度、Palette、8 位 RGB/RGBA、ExtraSamples Alpha 与页面 Orientation |

NativeAOT 应用可以在构建时移除不需要的解码器。所有开关默认继承 `SquareImagesEnableAllFormats=true`，因此现有项目行为不变：

| MSBuild 属性 | 控制格式 |
|---|---|
| `SquareImagesEnableAllFormats` | 所有格式的默认值 |
| `SquareImagesEnablePng` | PNG 与 APNG |
| `SquareImagesEnableJpeg` | JPEG |
| `SquareImagesEnableBmp` | BMP |
| `SquareImagesEnableGif` | GIF |
| `SquareImagesEnableIcon` | ICO 与 CUR |
| `SquareImagesEnableTiff` | TIFF |
| `SquareImagesEnableWebp` | WebP |

例如，只保留 PNG/APNG：

```bash
dotnet publish -c Release -r win-x64 \
  -p:PublishAot=true \
  -p:SquareImagesEnableAllFormats=false \
  -p:SquareImagesEnablePng=true
```

格式开关在编译 `Square.Images` 时生效。关闭的格式由 `ImageDecoder` 报告为不支持；关闭 PNG 时，ICO/CUR 仍可解码内嵌 BMP 的变体，但不能解码内嵌 PNG 的变体。

GIF、APNG 和动画 WebP 的每个 `ImageItem` 都是完整画布的合成快照。`Duration` 是该帧原始时长；`ImageAnimationInfo.PlayCount` 表示总播放次数，`LoopsForever` 表示无限循环。

```csharp
using var animation = ImageDecoder.Decode("animation.gif");

foreach (var frame in animation.Items)
{
    Bitmap bitmap = animation.GetBitmap(frame.Index);
    TimeSpan duration = frame.Duration;
}
```

ICO/CUR 中的项目是尺寸/位深变体，不是动画帧。`PrimaryIndex` 按像素面积优先、源位深次优选择；CUR 的 `Hotspot` 非空。

```csharp
using var icon = ImageDecoder.Decode("app.ico");
Bitmap preferred = icon.PrimaryBitmap;

foreach (var variant in icon.Items)
    Console.WriteLine($"{variant.Width}x{variant.Height}, {variant.SourceBitDepth} bit");
```

JPEG、TIFF 页面与 WebP EXIF Orientation 默认应用到像素。`Metadata.OriginalOrientation` 保留文件原始值，`Metadata.OrientationApplied` 表示是否发生方向变换。设置 `ExifOrientationPolicy.Ignore` 可保留原始像素方向。

TIFF 每个 IFD 页面对应一个 `ImageItem`，页面可具有不同尺寸、源位深和 Orientation。页面元数据位于 `ImageItem.Metadata`，文档级 `Metadata` 对应主页面。当前 TIFF 支持 Chunky 的未压缩、LZW、Deflate、Adobe Deflate 与 PackBits Strip；8 位样本支持水平差分 `Predictor=2`。Tile、Planar Separate、CMYK、YCbCr、Lab 和 BigTIFF 尚未支持。

WebP 当前支持静态 `VP8L`、静态 VP8 lossy 关键帧，以及 `ANIM`/`ANMF` 内嵌 VP8L、VP8 或 `ALPH+VP8` 动画帧，并保留完全透明像素的 RGB 分量。纯 C# VP8 解码器支持分割、1/2/4/8 token partition、Y2/WHT、16x16/8x8 与全部 4x4 帧内预测模式、系数概率更新、量化、simple/normal loop filter，并将规范 YUV420 平面通过确定性的 libwebp 标量公式转换为 BGRA。VP8 支持独立 `ALPH` chunk：method 0 原始平面、method 1 VP8L 压缩平面、None/Horizontal/Vertical/Gradient filter，以及 preprocessing 0/1；alpha 与颜色按 straight-alpha 合并。动画合成支持局部 frame rectangle、alpha-over/no-blend、dispose-to-background、ANIM BGRA 背景和单帧动画 loop metadata。扩展 WebP 支持 `EXIF` Orientation（可由 `ExifOrientationPolicy` 控制），并校验 `ICCP`、`ALPH`、`EXIF`、`XMP ` chunk 与 VP8X flag 的一致性、重复项、规范阶段顺序和累计元数据大小限制；VP8X 必须是扩展文件首块，ICCP 位于图像数据前，ALPH 紧邻并位于 VP8 前，EXIF/XMP 位于图像或动画帧后。

解码限制分别覆盖编码输入、单项目尺寸与内存、文档项目数、全部项目累计解码内存，以及 Exif 元数据大小、标签数和 IFD 深度。

实现只使用 BCL `ZLibStream` 和显式格式分派，无反射、动态代码、native library 或运行时 codec discovery，支持 trimming 与 NativeAOT。

---

## 7. Square.Platform — 平台宿主

### IPlatformHost

```csharp
namespace Square.Platform;

public interface IPlatformHost
{
    Size ClientSize { get; }
    float DpiScale { get; }
    bool IsRunning { get; }
    CursorKind Cursor { get; set; }
    KeyModifiers Modifiers { get; }

    event Action<Size>? SizeChanged;
    event Action<Point, MouseAction>? MouseEvent;
    event Action<Point, int>? WheelEvent;
    event Action<int, KeyAction>? KeyEvent;
    event Action<string>? TextInput;
    event Action? Tick;
    event Action? RenderRequested;

    void Show();
    void Close();
    IRenderContext CreateRenderContext();
    void PumpEvents();
    void SetTextInputRect(Rect rect);
    string GetClipboardText();
    void SetClipboardText(string text);
}
```

| 成员 | 说明 |
|---|---|
| `ClientSize` | 窗口客户区逻辑像素尺寸 |
| `DpiScale` | 当前 DPI 缩放 |
| `Modifiers` | 当前键盘修饰键状态 |
| `MouseEvent` | `(point, action)` 鼠标事件 |
| `KeyEvent` | `(keyCode, action)` 键盘事件 |
| `TextInput` | `(text)` 文本输入（IME） |
| `Tick` | 平台消息循环空闲回调 |
| `RenderRequested` | 平台曝光或原生后端 target 重建后请求应用重新提交完整画面 |
| `PumpEvents()` | 阻塞消息循环，直到窗口关闭 |
| `SetTextInputRect` | 设置 IME 候选框位置 |

### PlatformHostCreateInfo

```csharp
namespace Square.Platform;

public sealed class PlatformHostCreateInfo
{
    public required string Title { get; set; }
    public int Width { get; set; } = 800;
    public int Height { get; set; } = 600;
}
```

### 枚举

```csharp
public enum MouseAction { Down, Up, Move, Wheel }
public enum KeyAction { Down, Up }
public enum CursorKind { Arrow, Text }

[Flags]
public enum KeyModifiers { None = 0, Shift = 1, Control = 2, Alt = 4 }
```

### IPlatformFactory / PlatformRegistry

```csharp
namespace Square.Platform;

public interface IPlatformFactory
{
    string Name { get; }
    IPlatformHost CreateHost(PlatformHostCreateInfo info);
}

public static class PlatformRegistry
{
    public static void Register(IPlatformFactory factory);
    public static bool TryGet(out IPlatformFactory? factory);
    public static IPlatformFactory Get();
}
```

### 平台实现注册

```csharp
using Square.Platform;
using Square.Platform.Win32;

PlatformRegistry.Register(new Win32PlatformFactory());
```

Windows 引用 `Square.Platform.Win32` 并注册 `Win32PlatformFactory`；Linux/X11 引用 `Square.Platform.X11` 并注册 `X11PlatformFactory`。`DesktopApplication` 不依赖具体平台实现，因此必须在 `Run()` 前完成注册。

### PlatformScreenshot

```csharp
namespace Square.Platform;

public static class PlatformScreenshot
{
    public static Bitmap CaptureByProcessId(int processId);
    public static bool TryCaptureByProcessId(int processId, out Bitmap? bitmap);
}
```

| 成员 | 说明 |
|---|---|
| `CaptureByProcessId(pid)` | 按进程 ID 捕获其顶层窗口位图；找不到可捕获窗口时抛 `InvalidOperationException` |
| `TryCaptureByProcessId(pid, out bitmap)` | 尝试捕获，返回是否成功 |

截图由当前注册的平台工厂提供。配合 `BitmapPngEncoder` 可将截图保存为 PNG。

`PlatformScreenshot` 用于捕获真实平台窗口，与 `DesktopApplication.CaptureRendererBitmapAsync()` 的进程内 renderer 截图区分。自动化和 DevTools 默认使用后者，以避免 PID 查找、遮挡、窗口边框和桌面合成器差异。

---

## 8. Square.Rendering — 布局引擎

### LayoutEngine

```csharp
namespace Square.Rendering;

public sealed class LayoutEngine
{
    public void Measure(Element element, Size availableSize);
    public void Arrange(Element element, Rect finalRect);
}
```

布局流程：`Measure`（计算期望尺寸） → `Arrange`（确定最终位置与尺寸） → 写入 `Element.Geometry`。

### ComputedStyle

```csharp
namespace Square.Rendering;

public sealed class ComputedStyle
{
    public DisplayMode Display { get; set; }
    public FlexDirection FlexDirection { get; set; }
    public JustifyContent JustifyContent { get; set; }
    public AlignItems AlignItems { get; set; }
    public float FlexGrow { get; set; }
    public float FlexShrink { get; set; }
    public float FlexBasis { get; set; }
    public float Gap { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float Padding { get; set; }
    public float Margin { get; set; }
    public string GridTemplateColumns { get; set; }
    public string GridTemplateRows { get; set; }
    public int GridColumn { get; set; }
    public int GridRow { get; set; }
    public int GridColumnSpan { get; set; }
    public int GridRowSpan { get; set; }
    public string GridArea { get; set; }
}

public enum DisplayMode { Block, Flex, Grid, None }
public enum FlexDirection { Row, Column, RowReverse, ColumnReverse }
public enum JustifyContent { FlexStart, Center, FlexEnd, SpaceBetween, SpaceAround }
public enum AlignItems { Stretch, FlexStart, Center, FlexEnd }
```

支持的 CSS 属性读取自 `Element.Style.Get(...)`：`display`、`flex-direction`、`justify-content`、`align-items`、`gap`、`padding`、`margin`、`width`、`height`、`flex-grow`、`flex-shrink`、`flex-basis`、`grid-template-columns`、`grid-template-rows`、`grid-column`、`grid-row`、`grid-area`、`font-size`。

长度单位：`px`、`%`、`rem`、`em`、`vw`、`vh`、`rp`、`auto`、`min-content`、`max-content`、`fit-content`。

---

## 9. Square.Rendering — Display Tree

### DisplayTree

```csharp
namespace Square.Rendering;

public sealed class DisplayTree
{
    public void BuildFrom(Element element);
    public void Invalidate(Rect rect);
    public void UpdateDirty();
    public void Render(IRenderContext ctx);
}
```

| 成员 | 说明 |
|---|---|
| `BuildFrom(element)` | 从 Element Tree 全量构建 Display Tree |
| `UpdateDirty()` | 按 `NeedsPaint` 增量标记脏节点 |
| `Render(ctx)` | 遍历 DisplayNode，收集并提交 DrawCommand |

`DesktopApplication` 在 `RenderFrame()` 中按需调用 `BuildFrom` 或 `UpdateDirty`，随后 `Render`。`DisplayNode.Source` / `Element` 指向对应文档元素。

### TextFragment

```csharp
namespace Square.Rendering;

public sealed record TextFragment(
    Element Element, string Text, Font Font, Rect Bounds,
    IReadOnlyList<TextCharacterFragment> Characters)
{
    public int HitTestOffset(Point point);
}

public readonly record struct TextCharacterFragment(int StartOffset, int EndOffset, Rect Bounds);
```

| 成员 | 说明 |
|---|---|
| `Element` | 该文本片段所属的元素 |
| `Text` | 片段文本内容 |
| `Font` | 渲染字体 |
| `Bounds` | 片段整体边界矩形 |
| `Characters` | 逐字符边界信息，用于精确命中 |
| `HitTestOffset(point)` | 返回点对应的文本偏移量；命中字符内时按中点判定左右，否则取最近字符的起/止偏移 |

为富文本编辑、光标定位与选择提供字符级几何信息。配合 `Square.UI.Range` 构建文本选择模型。


---

## 10. Square.Extensions.Routing — 路由

### Router

```csharp
namespace Square.Extensions.Routing;

public sealed class Router : IDisposable
{
    public AppWindow Window { get; }
    public IReadOnlyList<RouteDefinition> Routes { get; }
    public INavigationHistory History { get; set; }
    public RouteLocation? Current { get; }
    public event Action<RouteLocation>? Navigated;

    public bool Navigate(string location, bool replace = false);
    public bool Replace(string location);
    public bool Back();
    public bool Forward();
    public IDisposable BeforeEach(Func<RouteLocation, RouteLocation?, RouteGuardResult> guard);
    public void ClearCache();
    public void RemoveCache(string matchedPath);
}
```

| 成员 | 说明 |
|---|---|
| `Routes` | 路由声明列表 |
| `History` | 导航历史；默认 `MemoryNavigationHistory` |
| `Current` | 当前 `RouteLocation` |
| `Navigate(location, replace)` | 导航到指定路径；路径不匹配返回 false |
| `Back/Forward` | 历史导航 |

### AppWindow.UseRouter

```csharp
var router = window.UseRouter(routes =>
{
    routes.Map("/", static () => new HomePage());
    routes.Map("users", static () => new UsersLayout(), route =>
    {
        route.KeepAlive = true;
        route.Map(":id", static () => new UserPage(), child => child.KeepAlive = true);
    });
});
```

### RouterView / RouterLink

```csharp
namespace Square.Extensions.Routing;

public sealed class RouterView : View
{
    public Router Router { get; }
    public RouteLocation? Current { get; }
    public void ConfigureOnce(Action<RouteCollectionBuilder> configure, string initialPath = "/");
}

public sealed class RouterLink : Square.Controls.Link
{
    public string To { get; set; }
    public bool Replace { get; set; }
}
```

### RouteLocation

```csharp
namespace Square.Extensions.Routing;

public sealed class RouteLocation
{
    public string FullPath { get; }
    public string Path { get; }
    public IReadOnlyDictionary<string, string> Parameters { get; }
    public IReadOnlyDictionary<string, string> Query { get; }
    public IReadOnlyList<RouteMatchEntry> Matched { get; }
    public T? GetMeta<T>(int depth = -1);
    public static RouteLocation? Find(Element element);
}
```

### RouteDefinition

```csharp
namespace Square.Extensions.Routing;

public sealed class RouteDefinition
{
    public string Path { get; }
    public string? Name { get; set; }
    public bool KeepAlive { get; set; }
    public object? Meta { get; set; }
    public Func<RouteLocation, string>? CacheKeySelector { get; set; }
    public List<RouteDefinition> Children { get; }
}
```

### INavigationHistory

```csharp
namespace Square.Extensions.Routing;

public interface INavigationHistory
{
    string Current { get; }
    event Action<string> Changed;

    void Push(string location);
    void Replace(string location);
    bool Back();
    bool Forward();
}
```

### Link（控件，类似 `a`）

```csharp
namespace Square.Controls;

public class Link : UIElement
{
    public string TextContent { get; set; }
    public string Href { get; set; }
    public Color Color { get; set; }      // 默认 #0066CC
    public float FontSize { get; set; }   // 默认 16
    public bool Underline { get; set; }   // 默认 true
}
```

### ListItem（类似 `li`）

```csharp
namespace Square.Controls;

public class ListItem : UIElement
{
    public string TextContent { get; set; }
    public string Marker { get; set; }    // 默认 "• "
    public Color Color { get; set; }
    public float FontSize { get; set; }
    public bool IsSelected { get; set; }
}
```

### List

```csharp
namespace Square.Controls;

public enum SelectionMode { None, Single, Multiple }

public class List : ScrollViewer
{
    public string[] Items { get; set; }
    public SelectionMode SelectionMode { get; set; }
    public int SelectedIndex { get; set; }
    public ListItem? SelectedItem { get; }
    public IReadOnlyList<int> SelectedIndices { get; }
    public IReadOnlyList<ListItem> SelectedItems { get; }

    public void SetItemsSource(ObservableCollection<string>? source);
    public bool SelectIndex(int index, bool control = false, bool shift = false);
    public void ClearSelection();
    public bool HandleKey(int keyCode, bool shift = false, bool control = false);
}
```

`List` 默认单选并复用 `ScrollViewer` 的纵向滚动。选择变化派发冒泡的 `selectionchange` 和 `change` 事件；多选模式支持 Control 切换与 Shift 范围选择。既可声明 `<ListItem>` 子项，也可通过 `Items` 或 `SetItemsSource(ObservableCollection<string>)` 生成文本项。

### VirtualList

```csharp
public sealed class VirtualList : ScrollViewer
{
    public float ItemHeight { get; set; }       // 默认 28
    public int OverscanCount { get; set; }      // 默认 3
    public SelectionMode SelectionMode { get; set; }
    public int ItemCount { get; }
    public int RealizedItemCount { get; }
    public int SelectedIndex { get; set; }
    public IReadOnlyList<int> SelectedIndices { get; }
    public object? SelectedValue { get; }

    public void SetItemsSource<T>(
        IReadOnlyList<T>? source,
        Func<T, int, ListItem>? itemTemplate = null);
    public void ScrollIntoView(int index);
}
```

`VirtualList` 使用固定行高，只创建视口和 overscan 范围内的 `ListItem`。选择以逻辑索引保存，行控件滚出视口后不会丢失选择；`SelectedItem` / `SelectedItems` 只返回当前已实现的容器，完整数据选择应读取 `SelectedValue` / `SelectedValues`。实现使用上下 spacer 保持完整滚动范围，数据源实现 `INotifyCollectionChanged` 时会响应增删、移动、替换与重置。

### Tree / TreeItem

```csharp
namespace Square.Controls;

public class Tree : ScrollViewer
{
    public TreeItem? SelectedItem { get; }
    public bool SelectItem(TreeItem? item);
    public void ClearSelection();
    public bool HandleKey(int keyCode);
}

public class TreeItem : UIElement
{
    public string TextContent { get; set; }
    public bool IsExpanded { get; set; }
    public bool IsSelected { get; set; }
    public IReadOnlyList<TreeItem> Items { get; }
    public bool HasItems { get; }

    public bool Expand();
    public bool Collapse();
    public bool Toggle();
}
```

`Tree` 使用嵌套 `<TreeItem>` 表达层级。上下键在可见节点间移动，右键展开或进入第一个子项，左键折叠或返回父项，Enter/Space 切换展开状态。选择变化派发 `selectionchange` 和 `change`，节点展开/折叠分别派发 `expand` 和 `collapse`。

### VirtualTree

```csharp
public sealed class VirtualTree : ScrollViewer
{
    public float ItemHeight { get; set; }       // 默认 28
    public int OverscanCount { get; set; }      // 默认 3
    public float IndentSize { get; set; }       // 默认 18
    public int VisibleItemCount { get; }
    public int RealizedItemCount { get; }
    public object? SelectedValue { get; }

    public void SetItemsSource<T, TKey>(
        IReadOnlyList<T>? roots,
        Func<T, IReadOnlyList<T>?> childSelector,
        Func<T, TKey> keySelector,
        Func<T, int, TreeItem>? itemTemplate = null);
    public bool ExpandIndex(int index);
    public bool CollapseIndex(int index);
    public void ScrollIntoView(int index);
}
```

`VirtualTree` 将已展开的逻辑层级扁平化，只实现视口附近的 `TreeItem`，并按深度应用缩进。展开和选择状态按 key 保存，不依赖对应行是否已实现；键盘方向键保持普通 `Tree` 的父子导航语义。当前版本采用固定行高，根集合可通过 `INotifyCollectionChanged` 通知更新，子集合变化后可重新设置数据源刷新。

### Swiper

```csharp
namespace Square.Controls;

public class Swiper : View
{
    public int SelectedIndex { get; set; }
    public bool Loop { get; set; }
    public int Count { get; }
    public Element? SelectedItem { get; }
    public bool CanGoPrevious { get; }
    public bool CanGoNext { get; }

    public bool GoTo(int index);
    public bool Previous();
    public bool Next();
    public bool HandleKey(int keyCode);
}
```

`Swiper` 将直接子元素作为页面，同时只显示当前页。左右键导航，Home/End 跳到首尾页；`Loop` 开启时首尾循环。当前基础版本提供离散分页与 `change` 事件，不包含拖拽手势或过渡动画。

模板中的 `<Link>` 始终映射为普通 `Square.Controls.Link`；应用内导航使用可选扩展的 `<RouterLink>`。

---

## 11. Square.Controls.Animation — 动画

### Animation\<T\>

```csharp
namespace Square.Controls.Animation;

public sealed class Animation<T>
{
    public Animation(
        Func<T, T, float, T> interpolate,
        T from, T to, float duration,
        Func<float, float> easing,
        Action<T> onUpdate);

    public bool IsComplete { get; }
    public void Start();
    public void Stop();
    public void Update(float deltaSeconds);
}
```

### Easing

```csharp
namespace Square.Controls.Animation;

public static class Easing
{
    public static float Linear(float t);
    public static float EaseIn(float t);
    public static float EaseOut(float t);
    public static float EaseInOut(float t);
    public static float EaseInQuad(float t);
    public static float EaseOutQuad(float t);
    public static float EaseInOutQuad(float t);
}
```

### Clock

```csharp
namespace Square.Controls.Animation;

public sealed class Clock
{
    public double ElapsedSeconds { get; }
    public double DeltaSeconds { get; }
    public void Start();
    public void Stop();
}
```

---

## 12. Square.Extensions — 扩展模块

可选扩展组件与第三方集成。引用 `Square.Extensions` 后调用 `ExtensionRegistration.RegisterDefaults()` 注册扩展控件标签。

### ExtensionRegistration

```csharp
namespace Square.Extensions;

public static class ExtensionRegistration
{
    public static void RegisterDefaults();
}
```

| 成员 | 说明 |
|---|---|
| `RegisterDefaults()` | 注册 `RichTextEditor`、`RouterView` 等扩展标签。幂等，重复调用安全 |

应用启动时调用一次即可让扩展控件在 `.sqx` / `.sqv` 中按标签使用：

```csharp
ExtensionRegistration.RegisterDefaults();
var app = new DesktopApplication(document, createInfo);
app.Run();
```

### MarkdownViewer

`MarkdownViewer` 位于独立包 `Square.Extensions.Markdown`。应用启动时先调用：

```csharp
MarkdownRegistration.RegisterDefaults();
```

```csharp
namespace Square.Extensions.Markdown;

public sealed class MarkdownViewer : UIElement
{
    [Prop] public string Content { get; set; }   // Markdown 文本
    [Prop] public string Markdown { get; set; }  // Content 的别名
    [Prop] public MarkdownDocument? SourceDocument { get; set; }
    public MarkdownDocument Document { get; }   // 当前解析后的文档模型
}
```

| 成员 | 说明 |
|---|---|
| `Content` | Markdown 源文本；设置后自动重建子元素 |
| `Markdown` | `Content` 的语义别名，二者同步 |
| `SourceDocument` | 可选的预解析文档；非空时优先于 `Content`，不会再次经过 Markdig |
| `Document` | 当前解析后的只读 Markdown 文档模型；不暴露 Markdig 类型 |

`MarkdownDocument.Parse(string?)` 将源文本解析为 `Square.Extensions.Markdown` 自有的块和行内模型。Markdig 仅作为 `Square.Extensions.Markdown` 内部解析器，不会出现在公开模型或核心 `Square` 项目的依赖中。围栏代码块通过 TextMate grammar 数据库进行语法高亮。

`MarkdownViewer` 将该模型渲染为 Square 元素树（标题 H1–H6、段落、嵌套有序/无序列表、任务列表、引用、代码块、分隔线、表格、图片、链接、强调与行内代码）。容器默认 `display:flex; flex-direction:column; gap:8px`，各块带 `markdown-*` class，可在 `<style>` 中定制。挂载（`OnAttached`）或 Prop 变化时自动重新渲染。

需要复用解析结果时可直接传入文档：

```csharp
var document = MarkdownDocument.Parse(source);
var viewer = new MarkdownViewer { SourceDocument = document };
```

### CodeEditor

`CodeEditor` 位于独立包 `Square.Extensions.CodeEditor`。应用启动时先注册：

```csharp
using Square.Extensions.CodeEditor;

CodeEditorRegistration.RegisterDefaults();
```

随后可在代码或模板中使用：

```csharp
var editor = new CodeEditor
{
    Language = "csharp",
    ThemeId = "default-dark",
    ShowLineNumbers = true,
    ShowFolding = true,
    WordWrap = false,
};
editor.Value = "public class App { }";
```

```xml
<CodeEditor Language="json" ThemeId="default-dark" ShowLineNumbers="true" />
```

核心能力包括 PieceTable 编辑模型、增量撤销/重做、视口虚拟化、多光标、查找替换、行装饰、overview ruler 与代码折叠。语法高亮使用 `TextMateSharp` / `TextMateSharp.Grammars`；未知 languageId 回退为纯文本。

`TextMateLanguageProvider.RegisterExtension(path)` 可加载包含 `package.json` 和 `syntaxes/*.tmLanguage.json` 的 VS Code 扩展目录。`LanguageRegistry.GuessLanguage(path)` 可按文件扩展名推断 languageId。

折叠占位规则：花括号显示 `{...}`，多行数组或列表显示 `[...]`，XML/HTML 保留开始标签首行并显示 ` ...>`，Python 冒号缩进块显示 `...`。

---

## 13. 枚举与对齐辅助

### HorizontalAlignment / VerticalAlignment

```csharp
namespace Square.UI;

public enum HorizontalAlignment { Left, Center, Right, Stretch }
public enum VerticalAlignment { Top, Center, Bottom, Stretch }
```

---

## 14. 注册与初始化

应用启动时的注册行为：

| 注册器 | 方法 | 条件 |
|---|---|---|
| `BackendRegistration` | `RegisterDefaults()` | `BACKEND_SOFTWARE` 等编译常量 |
| `Direct2DRegistration` | `Register()` / `UseDirect2DBackend()` | Windows 应用引用 `Square.Backends.Direct2D` 后显式调用 |
| `PlatformRegistry` | `Register(new Win32PlatformFactory())` 或 `Register(new X11PlatformFactory())` | 应用在 `Run()` 前显式调用 |
| `ExtensionRegistration` | `RegisterDefaults()` | 手动调用（引用 `Square.Extensions` 后） |
| `MarkdownRegistration` | `RegisterDefaults()` | 手动调用（引用 `Square.Extensions.Markdown` 后） |

应用代码通常不需要手动调用 `BackendRegistration`，但必须显式注册所引用的平台工厂。扩展注册器不由 `DesktopApplication` 自动调用：`Square.Extensions` 使用 `ExtensionRegistration`，Markdown 使用独立的 `MarkdownRegistration`。

---

## 15. 事件签名约定

SQX 中的事件处理方法支持（由 Source Generator 适配）：

```csharp
private void OnClick() { }
private void OnClick(Event e) { }
```

事件名映射规则：DOM 风格小写 → SQX `on` 前缀 + PascalCase。例如 `click` → `onClick`、`textinput` → `onTextInput`、`requestframe` → `onRequestFrame`。

---

## 16. 命名空间速查

| 命名空间 | 主要类型 |
|---|---|
| `Square.Hosting` | `DesktopApplication`, `RenderMode` |
| `Square.Runtime` | `Application`, `Dispatcher`, `IComponentLifecycle` |
| `Square.Runtime.Binding` | `ObservableValue<T>`, `ObservableCollection<T>`, `PropAttribute` |
| `Square.Runtime.Signals` | `Signal<T>`, `SignalHub` |
| `Square.Events` | `EventTarget`, `Event`, `EventInit`, `EventPhase`, `StandardEvents`, `FrameRequestEvent` |
| `Square.Directives` | `SqxDirectiveAttribute`（编译期指令发现） |
| `Square.UI` | `Node`, `Element`, `UIElement`, `ElementState`, `Document`, `UIDocument`, `XMLDocument`, `Range`, `UIRootElement`, `UIHeadElement`, `UIBodyElement`, `HTMLElement`, `SlotCollection`, `RenderFragment` |
| `Square.UI.Svg` | `SVGDocument`, `SVGElement`, `SVGSVGElement`, `SVGGElement`, `SVGPathElement`, `SVGRectElement`, `SVGCircleElement`, `SVGEllipseElement`, `SVGLineElement`, `SVGPolylineElement`, `SVGPolygonElement` |
| `Square.UI.ElementApi` | `StyleAccessor`, `ClassListAccessor`, `ChildrenCollection` |
| `Square.UI.Properties` | `PropertyStore` |
| `Square.Controls` | `View`, `ScrollViewer`, `List`, `VirtualList`, `ListItem`, `Tree`, `VirtualTree`, `TreeItem`, `Swiper`, `Popup`, `Dialog`, `MenuBar`, `Menu`, `ContextMenu`, `MenuItem`, `MenuSeparator`, `Text`, `Link`, `Button`, `Input`, `TextArea`, `CheckBox`, `Radio`, `Select`, `Image`, `Canvas` |
| `Square.Controls.Primitives` | `ShowNode`, `ForNode`, `SwitchNode` |
| `Square.Graphics` | `IRenderContext`, `Color`, `Rect`, `Size`, `Point`, `Brush`, `Pen`, `Font`, `PathGeometry`, `TextLayout`, `Bitmap`, `RenderBackendRegistry` |
| `Square.Graphics.Svg` | `SvgImage` |
| `Square.Images` | `ImageDecoder`, `ImageDocument`, `ImageItem`, `ImageAnimationInfo`, `ImageMetadata`, `ImageDecoderOptions`, `ImageFormat`, `ImageDocumentKind`, `ImageOrientation`, `ExifOrientationPolicy`, `PngCrcPolicy` |
| `Square.Graphics.Codecs` | `BitmapPngEncoder`, `BmpPngConverter`, `Crc32` |
| `Square.Rendering` | `LayoutEngine`, `ComputedStyle`, `DisplayMode`, `FlexDirection`, `DisplayTree`, `DisplayNode`, `TextFragment`, `TextCharacterFragment` |
| `Square.Platform` | `IPlatformHost`, `IPlatformFactory`, `IPlatformScreenshotProvider`, `PlatformHostCreateInfo`, `PlatformRegistry`, `PlatformScreenshot` |
| `Square.Platform.Win32` | `Win32PlatformFactory` |
| `Square.Platform.X11` | `X11PlatformFactory` |
| `Square.Backends.Direct2D` | `Direct2DBackendFactory`, `Direct2DRegistration`, `UseDirect2DBackend` |
| `Square.Extensions.Routing` | `Router`, `RouterView`, `RouterLink`, `RouteLocation`, `RouteDefinition`, `INavigationHistory` |
| `Square.Controls.Animation` | `Animation<T>`, `Clock`, `Easing` |
| `Square.Extensions.Markdown` | `MarkdownViewer` |
| `Square.Extensions.CodeEditor` | `CodeEditor`, `CodeEditorRegistration`, `LanguageRegistry`, `TextMateLanguageProvider`, `CodeEditorThemeRegistry` |
| `Square.Extensions` | `ExtensionRegistration` |
