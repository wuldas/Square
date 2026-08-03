# SQX 语言规范

> Document Revision: 0.3
> 配套：`Architecture.md`、`Requirements.md`

---

## 1. 文件格式

`.sqx` 是无文件级根标签的单文件组件格式。文件由三个顶级 section 组成：

```xml
<template>
  <!-- 结构 + 绑定 + 流程控制 -->
</template>

<script lang="csharp">
  // C# 逻辑 + Props 声明
</script>

<style>
  /* CSS 样式 */
</style>
```

- `<template>`：必须且只能有一个；包含结构、绑定和流程控制；允许多个视觉根节点，不自动插入包装 `View`。
- `<script>`：可选，但最多一个；包含 C# 逻辑、Props 声明和文件级组件元数据。
- `<style>`：可选，但最多一个；CSS 由样式引擎消费。
- 顶级 section 推荐按 `<template>` → `<script>` → `<style>` 排列。
- 不再使用 `<sqx>` 文件级根标签。

Source Generator 将三段编译为同一个 `partial` 组件类。

### 1.1 代码后置

模板可以配套一个同名 C# 代码后置文件：

```text
UserCard.sqx
UserCard.sqx.cs

Settings.sqv
Settings.sqv.cs
```

代码后置声明与模板生成类型相同的 `partial` 类，可承载 Props、事件方法、生命周期方法和普通 C# 逻辑，从而替代大部分 `<script>` 内容：

```csharp
namespace MyApp.Components;

public partial class UserCard
{
    [Prop(Required = true)]
    public ObservableValue<string> Title { get; } = new("");

    private void OnSave(Event e)
    {
        SaveButton.TextContent = Title.Value;
    }
}
```

- 代码后置是普通 SDK `Compile` 项，不在运行时加载或解析。
- 命名空间必须匹配 `<script namespace="...">`，未指定时匹配项目 `RootNamespace`。
- 类名必须匹配 `<script name="...">`，未指定时匹配模板文件名。
- 声明必须使用 `partial`，通常不重复声明基类。
- 模板 `<script>` 与代码后置可以共存。
- 代码后置中的 `[Prop]` 同样参与必填与字面量类型检查。

### 1.2 `<script>` 元数据

文件级组件元数据统一声明在唯一的 `<script>` 标签上：

```xml
<script
  lang="csharp"
  namespace="MyApp.Components"
  name="UserCard"
  access="internal">
  // C# component code
</script>
```

| 属性 | 默认值 | 说明 |
|---|---|---|
| `lang` | `csharp` | 脚本语言；当前只支持 `csharp` |
| `namespace` | MSBuild `RootNamespace` | 覆盖生成组件的命名空间 |
| `name` | `.sqx` 文件名 | 覆盖生成组件的类型名 |
| `access` | `public` | 生成类型的可见性；支持 `public`、`internal` |

通常应沿用文件名和项目命名空间，只在确有需要时覆盖。样式级元数据属于 `<style>`，例如未来的 `<style scoped>`，不放在 `<script>` 上。

### 1.3 section 约束与诊断

| 规则 | 建议诊断 |
|---|---|
| 缺少 `<template>` | `SQX0001` |
| 重复 `<template>` | `SQX0002` |
| 重复 `<script>` | `SQX0003` |
| 重复 `<style>` | `SQX0004` |
| section 未闭合 | `SQX0005` |
| 不支持的脚本语言 | `SQX0006` |
| 未知顶级 section | `SQX0007` |
| section 外存在非空内容 | `SQX0008` |
| 组件元数据无效 | `SQX0009` |

---

## 2. 元素

### 2.1 内置元素（M1）

| 标签 | 说明 |
|---|---|
| `View` | 通用容器 |
| `Text` | 文本 |
| `Button` | 按钮 |
| `Input` | 输入框 |
| `TextArea` | 多行输入 |
| `CheckBox` | 复选 |
| `Radio` | 单选 |
| `Select` | 下拉选择 |
| `Image` | 图片 |
| `Canvas` | 画布 |
| `svg` | 内联 SVG 根元素；创建独立 `SVGDocument` |
| `g` | SVG 分组、样式继承和变换 |
| `path` / `rect` / `circle` / `ellipse` | SVG 路径与基础图形 |
| `line` / `polyline` / `polygon` | SVG 线段和点集图形 |
| `MenuBar` / `Menu` / `ContextMenu` | 顶级菜单栏、弹出菜单与命令式上下文菜单 |
| `MenuItem` / `MenuSeparator` | 菜单命令、Check/Radio 项、子菜单入口与分隔线 |

`Canvas.RequestFrame()` 请求宿主在下一次平台 Tick 渲染新帧。请求通过 Element Tree 冒泡并合并，同一 Tick 内的多个请求只需触发一次窗口渲染。它用于 Canvas 时钟、游戏循环和后续动画系统；该方法只请求下一帧，不会自动形成持续循环。持续绘制应在每帧完成后再次调用 `RequestFrame()`。

命名：PascalCase 控件类型（C# 习惯），`.sqx` 内标签同名。

SVG 标签使用与浏览器一致的小写名称。模板编译器将它们直接映射到 `Square.UI.Svg` 下的 `SVGSVGElement`、`SVGGElement`、`SVGPathElement` 等类型。SVG 子树不是自定义组件，不使用 Slot，也不调用运行时模板解析。

### 2.2 内联 SVG

```xml
<svg viewBox="0 0 120 80" width="120" height="80">
  <g transform="translate(8 8)" fill="#2b78ee">
    <rect x="0" y="0" width="104" height="64" rx="10" />
    <circle cx="52" cy="32" r="16" fill="#ffffff" />
    <path d="M 44 32 L 50 38 L 62 24"
          fill="none" stroke="#152241" stroke-width="4" />
  </g>
</svg>
```

支持的元素：

- `svg`、`g`
- `path`、`rect`、`circle`、`ellipse`
- `line`、`polyline`、`polygon`

支持的主要属性：

- 视口：`viewBox`、`width`、`height`
- 几何：`x`、`y`、`rx`、`ry`、`cx`、`cy`、`r`、`x1`、`y1`、`x2`、`y2`、`points`、`d`
- 绘制：`fill`、`stroke`、`stroke-width`、`opacity`、`fill-opacity`、`stroke-opacity`
- 变换：`translate`、`scale`、`rotate`、`matrix`
- `style` 中的对应 SVG presentation 属性

这些属性同样支持 SQX 表达式绑定；SQV 中可使用 `:fill="Color"`、`:r="Radius"` 等 Vue 风格绑定。每个 `SVGSVGElement` 持有 `SvgDocument`，其类型为 `SVGDocument : XMLDocument`，`ContentType` 为 `image/svg+xml`。

当前限制：不支持 SVG 脚本、动画、滤镜、mask、gradient、text、外部资源、`use`、`defs`、嵌入 raster image 和路径 `A/a` 圆弧命令。

### 2.3 自定义组件

任何 `.sqx` 文件即一个组件。组件类型名默认取文件名，也可由唯一 `<script name="MyComponent">` 覆盖。

调用：

```xml
<MyComponent Title={PageTitle} Count={ItemCount} />
```

### 2.4 结构原语（编译期处理，非运行时组件）

| 原语 | 用途 |
|---|---|
| `<Show when={expr}>` | 条件子树 |
| `<For each={expr}>{(it)=>…}</For>` | 列表 |
| `<Switch>` + `<Match when={expr}>` | 多分支 |
| `<Index each={expr}>` | 索引列表（可选） |

详见 §6。

### 2.5 插槽与组件内容

自定义组件使用 `<Slot>` 接收调用处的子节点。未指定 `slot` 的直接子节点进入默认插槽：

```xml
<!-- AppShell.sqx -->
<View class="shell">
  <Slot name="header"><Text text="Default header" /></Slot>
  <Slot />
</View>
```

```xml
<AppShell>
  <Text slot="header" text="Dashboard" />
  <HomePage />
</AppShell>
```

- `<Slot />` 表示默认插槽；`<Slot name="..." />` 表示具名插槽。
- `<Slot>` 的 children 是插槽未传入时的 fallback。
- `slot="..."` 只在自定义组件的直接子节点上参与内容分组，不作为控件属性发射。
- 插槽内容保持调用方的表达式、事件与绑定作用域。
- 插槽通过延迟 `RenderFragment` 构建，每个组件实例至多解析一次。
- 多个插槽根节点直接插入目标父节点，不额外包裹 `View`，避免改变 Flex/布局语义。
- 组件 Props 与 Slots 必须在子组件 `BuildElementTree()` 前完成设置。

第一阶段支持默认插槽、具名插槽和 fallback；运行时替换 Slot factory、`::slotted` 样式与跨组件内容搬运后置。

开发者可直接基于 Slot 自定义 Tabs 等组合组件，不增加专用模板语法：

```xml
<Tabs>
  <Button slot="tabs" class="tab-button">Controls</Button>
  <Button slot="tabs" class="tab-button">Signals</Button>
  <ControlsPage />
  <SignalsPage />
</Tabs>
```

- `tabs` Slot 中的按钮与默认 Slot 中的页面按顺序一一对应。
- 示例 Tabs 只切换页面的可见性，不重建页面，因此页内输入状态会保留；它不是框架标准控件。
- 页签按钮数量与页面数量不一致时，只对可配对部分建立选择关系。

---

## 3. Props

### 3.1 声明

在 `<script lang="csharp">` 中使用 `[Prop]` 特性：

```csharp
[Prop] public ObservableValue<string> Title { get; set; } = new("");
[Prop(Required = true)] public ObservableValue<int> Count { get; set; } = new(0);
[Prop] public ObservableValue<bool> Visible { get; set; } = new(true);
```

- 类型为 `ObservableValue<T>`
- 默认值用 C# 初始化器
- `[Prop(Required = true)]` 标记必填
- 标量 Prop 与 `ObservableValue<T>` Prop 均可声明；响应式传播要求源值实现 `ObservableValue<T>` 或 `IReactiveValue<T>`

### 3.2 传值

调用方在模板中以属性形式传入：

```xml
<MyComponent Title={PageTitle} Count={ItemCount} />
<!-- 常量 -->
<MyComponent Title="Hello" Count={5} />
```

- `{expr}` 绑定到 `ObservableValue<T>`
- 常量字面量自动包装

### 3.3 数据流

- **单向**：父 → 子
- 子组件**不可直接赋值改写** Props 的 `ObservableValue<T>` 内部值
- 父组件源变化 → 子组件 prop 自动更新
- 子组件响应变化的方式：
  - 订阅 prop 的 `ObservableValue`：`Title.Subscribe(v => ...)`
  - 重写钩子：`protected override void OnPropChanged(string name)`

### 3.4 校验

- 编译期：Generator 检查调用方是否传齐必填 Prop，缺失则报诊断（带 `.sqx` 行列）
- 运行时不做反射校验

### 3.5 内置元素属性

内置元素的属性（如 `<Button disabled>`、`<Input type="text">`）与自定义组件 Props **共用同一套机制**：

- 属性可绑定（`disabled={IsDisabled}`）或常量（`disabled`）
- 绑定属性编译为 `ObservableValue` 订阅
- 编译期类型检查

### 3.6 Prop 特性参考

| 特性 | 属性 | 说明 |
|---|---|---|
| `[Prop]` | — | 标记为组件 Prop |
| `[Prop]` | `Required` | 是否必填（默认 false） |
| `[Prop]` | `Default` | 默认值（也可用初始化器） |

---

## 4. ref 引用

### 4.1 语法

```xml
<Button ref={MyBtn}>Click</Button>
<Text ref={TitleEl}>Hello</Text>
```

### 4.2 生成

- 生成器在 `partial` 组件类中产出强类型字段：`internal Button MyBtn;`
- 元素挂载时自动赋值
- 元素卸载时置 null

### 4.3 使用

```csharp
MyBtn.Style.Set("color", "red");
MyBtn.ClassList.Add("active");
```

---

## 5. 绑定语法

### 5.1 统一语法

所有绑定使用 `{expr}` 表达式，与流程控制 `when=`/`each=` 同源。

### 5.2 文本插值

```xml
<Text>{Name}</Text>
<Text>Hello {FirstName} {LastName}</Text>
```

`{expr}` 内编译为 `ObservableValue` 读取并订阅。

### 5.3 单向属性

```xml
<Text text={Title} />
<View class={ActiveClass} />
```

编译为属性绑定并订阅源变化。

### 5.4 事件

```xml
<Button onClick={OnClick}>Click</Button>
<Input onInput={OnInput} />
```

- 事件名首字母大写：click → onClick、textChanged → onTextChanged
- 映射到 `<script lang="csharp">` 中的 C# 方法
- handler 支持无参和强类型路由事件签名：

```csharp
private void OnClick() { }
private void OnClick(Event e) { }
```

- DOM 事件提供 `Target`、`CurrentTarget`、`EventPhase`、`StopPropagation()` 与 `PreventDefault()`。
- `TunnelAndBubble` 事件按根→目标隧道、目标处理、目标父级→根冒泡；`Handled` 抑制后续普通 handler，仅 `handledEventsToo` 观察者仍可收到事件。

### 5.5 双向（显式）

```xml
<Input value={UserName} onInput={OnUserNameChanged} />
```

- `value={expr}` 单向属性绑定
- `onInput={Method}` 事件处理，Method 在 C# 中写回 `ObservableValue.Value`
- 不提供隐式双向绑定，保持显式可控

### 5.6 实现约束

- 绑定后端**必须**用 `ObservableValue<T>`（强类型、委托驱动、零反射、AOT 安全）
- `{expr}` 在编译期校验为合法 C# 表达式。直接响应值和混合文本中的直接响应成员会生成订阅；任意复合表达式的完整依赖分析仍由后续语义绑定阶段扩展
- 运行时零解析

### 5.7 跨组件信号

`ObservableValue<T>` 用于组件内部绑定；不相关组件之间使用应用共享的强类型信号：

```csharp
var activity = SignalHub.Default.Get("sample.activity", "Ready");
activity.Publish("Saved");

_subscription = activity.Subscribe(
    value => Status.Value = value,
    uiDispatcher,
    emitCurrent: true);
```

- `Signal<T>` 可从任意线程发布。
- 未传 `Dispatcher` 时，回调在发布线程同步执行。
- 传入 UI `Dispatcher` 时，后台发布的回调进入 UI 队列；UI 线程发布可同步回调。
- 同值默认不重复通知；需要强制通知时使用 `Publish(value, force: true)`。
- `Update` 在信号锁内原子计算新值，在锁外通知订阅者。
- 订阅返回 `IDisposable`，组件必须在卸载时释放。
- Signal 只传递状态和消息，不隐式操作 Element Tree。

Tabs 组合模式、SignalHub 和前后台线程投递的完整示例见 `Composition-and-Signals.md`。

---

## 6. 流程控制

### 6.1 `<Show>`

```xml
<Show when={LoggedIn}>
  <Text>欢迎</Text>
</Show>
```

- `when` 绑定 `ObservableValue<bool>`
- 条件变化时增删 Element 子树（记忆化复用）
- 可选 `fallback` 属性指定条件假时的替代子树：

```xml
<Show when={LoggedIn} fallback={<>未登录</>}>
  <Text>欢迎</Text>
</Show>
```

### 6.2 `<For>`

```xml
<For each={Items}>{(it)=>
  <Text>{it.Name}</Text>
}</For>
```

- `each` 绑定 `ObservableCollection<T>`
- `it` 为列表项
- 引用键增量更新（项移动时节点不重建）
- 可选 `fallback` 属性指定空列表时的替代子树：

```xml
<For each={Items} fallback={<>无数据</>}>{(it)=>
  <Text>{it.Name}</Text>
}</For>
```

- `fallback` 在集合为空（`Count == 0`）时渲染，有项时移除
- 与 `<Show>`/`<Switch>` 的 `fallback` 语义一致：均为"无内容时的替代"，作为属性传入，不占用 children 位置（children 专属于迭代模板）

### 6.3 `<Switch>` + `<Match>`

```xml
<Switch fallback={<>未知</>}>
  <Match when={Status == "loading"}><Text>Loading</Text></Match>
  <Match when={Status == "done"}><Text>Done</Text></Match>
</Switch>
```

- 互斥，首项真即渲染
- `<Switch>` 可带 `fallback`：无 `<Match>` 命中时渲染
- children **只能是 `<Match>`**（编译器校验，非 Match 子节点报错）
- `fallback` 是属性，不混入分支层级，与"匹配分支"视觉上分离

### 6.4 `<Index>`（可选，M2）

索引键列表。

### 6.5 编译模型

`<Show>`/`<For>`/`<Switch>`/`<Match>` 为 **Source Generator 已知的结构原语**（非运行时组件实例），由生成器特判编译为 Element Tree 的挂卸/迭代。

### 6.6 阶段

- M1：`<Show>`/`<For>` 基础形态
- M2：`<Switch>`/`<Match>`/`<Index>` + keyed 复用 ✅ 已实现。原生 keyed `For` 使用 `key={(it) => it.Id}`；`Index` 按位置保持槽位，集合项替换或移动时重建受影响位置的内容

---

## 7. 元素操作 API

### 7.1 引用

通过 `ref` 获取强类型引用（见 §4）。

### 7.2 属性

```csharp
el.SetProperty("disabled", true);
var v = el.GetProperty<bool>("disabled");
```

- 命令式**不覆盖已绑定属性**：若该属性已被声明式绑定，命令式写入会被下一次源变更覆盖

### 7.3 样式

```csharp
el.Style.Set("color", "red");
el.Style.Get("color");
el.Style.Remove("color");
```

### 7.4 类

```csharp
el.ClassList.Add("active");
el.ClassList.Remove("active");
el.ClassList.Toggle("active");
el.ClassList.Contains("active");
```

### 7.5 子节点

```csharp
el.AppendChild(new Text("hello"));
el.RemoveChild(child);
el.InsertBefore(newChild, refChild);
el.ClearChildren();
el.Children  // 子节点集合
```

- 命令式**不侵入 `<Show>`/`<For>` 管理的子树**

### 7.6 事件

```csharp
el.AddEventListener("click", handler);
el.RemoveEventListener("click", handler);
```

### 7.7 元素创建

```csharp
var btn = new Button();
btn.Text = "Click";
container.AppendChild(btn);
```

- 命令式构造的元素接生命周期钩子（OnAttached/OnDetached/...）

### 7.8 查询（M2）

```csharp
var btn = el.Query<Tag.Button>(".cls");
```

- 编译期生成匹配器，避免运行时反射

---

## 8. 生命周期钩子

### 8.1 组件级

| 钩子 | 触发时机 |
|---|---|
| `OnPropChanged(string name)` | Props 值变化 |
| `OnAttached` | 挂载到视觉树 |
| `OnDetached` | 从视觉树卸载 |
| `OnLoaded` | 加载完成 |
| `OnUnloaded` | 卸载完成 |
| `OnMeasure` | 布局测量 |
| `OnArrange` | 布局排列 |

### 8.2 应用级

| 钩子 | 触发时机 |
|---|---|
| `OnStart` | 应用启动 |
| `OnExit` | 应用退出 |

### 8.3 落地

编译期生成的 `partial` 组件类提供可重写虚方法，供 C# 业务逻辑订阅。

---

## 9. 仲裁规则总表

| 场景 | 规则 |
|---|---|
| 命令式写已绑定属性 | 允许写入，但下一次源变更会覆盖，不静默回滚 |
| 命令式操作 `<Show>` 子树 | 不允许（会被条件更新冲掉） |
| 命令式操作 `<For>` 子树 | 不允许（会被列表更新冲掉） |
| 命令式操作 `<Slot>` 区域 | 不允许（区域由组件调用方与生成代码管理） |

---

## 10. 路由

Square 桌面应用默认采用内存历史。路由声明由 Source Generator 编译为静态组件工厂，不使用反射：

```xml
var router = window.UseRouter(routes =>
{
    routes.Map("/", static () => new HomePage());
    routes.Map("/users", static () => new UsersLayout(), route =>
    {
        route.KeepAlive = true;
        route.Map("", static () => new UsersPage());
        route.Map(":id", static () => new UserPage(), child => child.KeepAlive = true);
    });
    routes.Map("*", static () => new NotFoundPage());
});
```

- 匹配优先级：静态段 > `:parameter` > `*wildcard`。
- 父 Route 有组件时作为布局组件，子路由内容投影到其默认 Slot；`<Outlet />` 是该 Slot 的路由语义别名。
- `<RouterLink to="...">` 执行声明式导航；`Navigate`、`Replace`、`Back`、`Forward` 提供命令式导航。
- 页面出口使用 `<RouterView>`；父布局中嵌套 RouterView 显示下一层子路由。
- `<Router>`/`<Route>` 已不再是 SQX 结构语法。
- `RouteContext` 提供当前 Path、Params 和 Query。
- 导航替换视觉子树时，沿用 Attached/Loaded/Unloaded/Detached 生命周期顺序。
- MVP 不包含运行时程序集懒加载、异步守卫、页面缓存和平台 URL 协议；仅预留 preload/历史适配边界。
| 命令式操作静态声明区域 | 允许 |
| 命令式创建并挂载元素 | 允许，接生命周期钩子 |
| Props 子组件改写 | 不允许（单向数据流） |

---

## 10. 按标签即用

控件按标签名即可用、免手动注册。Source Generator 按标签解析控件，无需显式 `using`/注册清单。

---

## 11. 示例

```xml
<template>
  <View>
    <Show when={LoggedIn}>
      <Text>Hello {UserName}</Text>
    </Show>
    <Button ref={MyBtn} onClick={OnClick}>Click</Button>
    <For each={Items}>{(it)=>
      <Text>{it.Name}</Text>
    }</For>
  </View>
</template>

<script lang="csharp">
  [Prop] public ObservableValue<bool> LoggedIn { get; set; } = new(false);
  [Prop] public ObservableValue<string> UserName { get; set; } = new("");

  public ObservableCollection<Item> Items = new();

  private void OnClick()
  {
      MyBtn.ClassList.Add("clicked");
  }
</script>

<style>
  View { padding: 16px; }
  Button.clicked { color: red; }
</style>
```
