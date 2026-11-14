# 组件组合与信号通信

> 适用版本：Square 0.3 开发版  
> 相关规范：`vue-plan.md`、`Sqx-Spec.md`、`Architecture.md`、`Generator.md`

本文说明如何使用 Slot 组合组件、用 Slot 自定义 Tabs，以及如何通过 `Signal<T>` 在组件和线程之间传递状态。

---

## 1. Slot 组合

自定义组件通过 `<Slot>` 接收调用方内容。未设置 `slot` 的直接子节点进入默认 Slot，设置 `slot="name"` 的直接子节点进入同名 Slot。

```xml
<!-- Panel.sqv -->
<View class="panel">
  <View class="panel-header">
    <Slot name="header">
      <Text text="Default header" />
    </Slot>
  </View>
  <View class="panel-content">
    <Slot />
  </View>
</View>
```

```xml
<Panel>
  <Text slot="header" text="Settings" />
  <SettingsPage />
</Panel>
```

运行规则：

- Slot 内容在调用方作用域内求值，事件和绑定仍访问调用方成员。
- `<Slot>` 不产生额外的 `View`，不会改变 Flex 或 Grid 层级。
- Slot 的 children 是未传入内容时的 fallback。
- 组件的 Props 和 Slots 在 `BuildElementTree()` 之前设置。
- 每个 Slot fragment 每个组件实例只渲染一次。

---

## 2. 基于 Slot 的自定义 Tabs 示例

Sample 中的 `Tabs.sqv` 使用两个区域：

- `tabs` 命名 Slot：页签按钮。
- 默认 Slot：与按钮按索引对应的页面。

```xml
<Tabs>
  <Button slot="tabs" class="tab-button">Text</Button>
  <Button slot="tabs" class="tab-button">Signals</Button>

  <TextSamplesPage />
  <SignalsSamplesPage />
</Tabs>
```

`Tabs` 是 Sample 内的应用层组件，不属于 `Square.Controls` 标准控件。它在挂载时读取两个 Slot 的直接子节点，给按钮绑定点击事件，并维护 `SelectedIndex`。切换页签只修改页面的 `IsVisible`，不会销毁页面实例，因此输入内容、路由位置和组件内部状态都会保留。

### 2.1 数量不一致

页签和页面只对可配对的索引建立关系。额外按钮不会选中，额外页面保持隐藏；完全没有可配对项时保留 fallback 内容可见。

### 2.2 布局约束

不可见元素不参与 Block、Flex 和 Grid 的测量与排列，也不占用 `gap`。重新显示页面后，下一次布局会把活动页面放入 Tabs 内容区域。

当前 CSS 引擎不会在运行时 class 变化后重新执行选择器匹配，因此 Tabs 同时维护 `selected` class，并直接设置活动按钮的前景色和背景色。

---

## 3. Signal 与 ObservableValue 的分工

| 类型 | 用途 | 线程语义 |
|---|---|---|
| `ObservableValue<T>` | 组件内部属性绑定 | 由组件所属线程使用 |
| `Signal<T>` | 跨组件共享状态和消息 | 可从任意线程发布 |
| `SignalHub` | 按名称获取共享 Signal | 名称和类型共同构成契约 |
| `Dispatcher` | 将回调送到所属线程 | 只有所属线程可以执行 `Run()` |

不要从后台线程直接修改 Element Tree。后台任务只发布 Signal，UI 组件通过绑定了 Dispatcher 的订阅接收消息。

---

## 4. 创建和发布 Signal

可以直接创建 Signal：

```csharp
var progress = new Signal<int>(0);
progress.Publish(25);
progress.Update(value => value + 1);
```

跨组件共享时使用 `SignalHub`：

```csharp
public static Signal<string> Activity { get; } =
    SignalHub.Default.Get("sample.activity", "Ready");
```

相同名称再次调用 `Get<T>` 会返回同一个实例。若相同名称使用了不同的 `T`，运行时会抛出 `InvalidOperationException`，避免组件之间静默使用不兼容的数据契约。

### 4.1 通知规则

- `Publish(value)` 更新状态并通知订阅者。
- 新值与当前值相等时默认不通知，并返回 `false`。
- `Publish(value, force: true)` 可强制通知。重复的按钮通知或相同方法组委托值可使用此参数，无需仅为触发通知而改用递增版本号。
- `Update(update)` 在锁内原子计算新值，在锁外通知订阅者。
- 发布使用订阅快照，回调可以安全取消自己的订阅。

---

## 5. 订阅与组件生命周期

组件应在挂载时订阅，在卸载时释放：

```csharp
private IDisposable? _subscription;

protected override void OnAttachedCore()
{
    _subscription = AppSignals.Activity.Subscribe(
        value => Status.Value = value,
        AppDispatchers.UI,
        emitCurrent: true);
}

protected override void OnDetachedCore()
{
    _subscription?.Dispose();
    _subscription = null;
}
```

`emitCurrent: true` 会在订阅后投递 Signal 当前值。释放订阅后，即使 Dispatcher 队列中还有尚未执行的回调，也不会再次调用组件。

---

## 6. 前后台线程通信

应用使用 `DesktopApplication` 时，Dispatcher 队列由宿主驱动排空。应用将自己的 Signal 订阅指向窗口 Dispatcher：

```csharp
var window = new AppWindow("Square Framework", 900, 980);
window.Load(new Main());
SampleSignals.Initialize(window.Dispatcher);
new DesktopApplication(window).Run();
```

Square 使用自有 `Dispatcher`，宿主不安装 UI `SynchronizationContext`。直接从后台回写 UI 时，可用 `await window.Dispatcher.InvokeAsync(() => Status.Value = "完成");`；发布 Signal 时则在 `Subscribe` 传入该 Dispatcher。

`DesktopApplication` 在每个 Tick 内执行 `Dispatcher.Run()` 排空队列并按需触发渲染。后台任务只负责发布：

```csharp
_ = Task.Run(async () =>
{
    for (var i = 1; i <= 5; i++)
    {
        await Task.Delay(350, cancellationToken);
        AppSignals.Activity.Publish($"Background message {i}");
    }
}, cancellationToken);
```

订阅指定 Dispatcher 后：

- UI 线程发布时，回调可在当前线程立即执行。
- 后台线程发布时，回调进入 Dispatcher 队列。
- `Dispatcher.Run()` 只能由创建 Dispatcher 的线程调用。
- `InvokeAsync` 可让后台代码等待 UI 操作完成或接收异常。

---

## 7. Sample 导航

主示例按能力拆分为多个页签：

| 页签 | 内容 |
|---|---|
| Text | 单行/多行输入、字体大小、行高、选区和输入法 |
| Controls | Button、CheckBox、Radio、Select、Show、For |
| Media | Image 和 Canvas |
| Router | Link、参数路由、查询参数、嵌套路由和 Slot |
| Signals | 跨组件发布/订阅和后台线程到 UI Dispatcher |
| Overflow | overflow 裁剪、轴向裁剪和 ScrollViewer |
| Main 顶部 | MenuBar、多级菜单、Check 多选和 Radio 分组 |
| Overlays | Popup、Dialog 和命令式 ContextMenu |

关键文件：

- `samples/Square.Sample.Vue/Components/Tabs.sqv`
- `samples/Square.Sample.Vue/Components/Main.sqv`
- `samples/Square.Sample.Vue/Components/SignalPublisher.sqv`
- `samples/Square.Sample.Vue/Components/SignalSubscriber.sqv`
- `samples/Square.Sample/SampleSignals.cs`

---

## 8. 当前边界

- Signal 是进程内通信，不提供跨进程传输或持久化。
- `SignalHub.Default` 是进程级共享实例；测试或多应用宿主可创建独立 `SignalHub`。
- Dispatcher 不自行创建线程，也不自行驱动消息循环。
- Tabs 的键盘导航、可访问性语义、关闭按钮和位置策略由应用按产品需求自行实现；框架不提供统一标准 Tab 控件。
- 动态 class 的 CSS 重新匹配尚未实现，状态组件需要同步更新必要的内联样式。
