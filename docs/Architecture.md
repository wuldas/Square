# Square Framework 总体架构

> Document Revision: 0.3
> 配套：`Requirements.md`（需求）、`vue-plan.md`（SQV / Vue 模板语法）、`Sqx-Spec.md`（SQX 原生语法）、`Rendering-Targets.md`（多目标渲染与宿主路线）、`plan.md`（分阶段计划）、`rebuild-plan.md`（架构重建）

---

## 1. 定位与核心约束

Square 是 **纯 C#、编译优先（Compile First）、NativeAOT 优先、渲染后端可插拔** 的跨平台 UI 框架。

六大核心原则：

1. **Compile First** —— 所有 UI 在编译期生成 C#，运行时零解析。
2. **Pure C# Core** —— 框架核心全部 C# 实现。
3. **NativeAOT First** —— 禁用 `Reflection.Emit`、运行时代码生成、`Dynamic`、运行时加载程序集。
4. **Backend Independent** —— 核心不依赖具体图形库；图形库均为可插拔 Backend。
5. **Retained Rendering** —— Element Tree + Display Tree，非 Immediate Mode。
6. **Low Coupling / IDE Friendly** —— 模块间通过抽象接口通信；`.sqv` / `.sqx` 提供类型检查、智能补全、编译错误定位。

---

## 2. 总体管线（保留模式）

```
.sqv / .sqx (template + style + script)
      │
      ▼
[Square.Compiler] ──► C# 组件类型 (编译期)
      │
      ▼
  Component (C#)
      │
      ▼
 Element Tree   (Square.UI / Runtime)
      │
      ▼
Layout Engine  (Square.Rendering, CSS 盒/flex/grid)
      │
      ▼
 Display Tree   (Square.Rendering, DrawCommand 列表)
      │
      ▼
 IRenderContext  (Square.Graphics 抽象)
      │
      ▼
 Backend  (Square.Backends: Software / Skia / Vulkan / Direct2D)
```

- **非 Immediate Mode**：保留 Element Tree + Display Tree，支持脏区增量重绘。
- **低耦合**：具体 Backend 与 Platform 实现仅依赖 `Square` 中的抽象接口，核心不反向依赖实现程序集。
- **NativeAOT 合规**：组件类型在编译期生成，运行时无反射解析；属性系统使用生成代码与强类型委托。

Debug 桌面开发另有一条增量分支：`dotnet watch` 监视 `.sqv` / `.sqx` AdditionalFiles，Roslyn 重新生成 C# 并应用 metadata delta，`SquareHotReloadHandler` 再把更新投递到活动窗口 Dispatcher。模板或组件 `<style>` 变化时复用顶层生成组件实例并重建后代；普通 C# 方法体变化只请求重绘。该分支不引入运行时模板解析，也不进入 Release / NativeAOT 发布路径。

---

## 3. 模块划分与职责

当前物理程序集包括 `Square`、`Square.Compiler`、平台与渲染后端、`Square.Extensions`、`Square.Extensions.Markdown`、`Square.Extensions.CodeEditor`、`Square.DevTools`、`Square.Native.Html` 和 `Square.Hosting.Web`。下表中的 Runtime/UI/Controls 等名称是 `Square` 聚合程序集内部保持稳定的逻辑模块与命名空间。

| 模块 | 职责 | 关键设计 |
|---|---|---|
| `Square.Markup` | `.sqv` / `.sqx` 解析 → 共享 AST | SQV 使用 Vue 模板语法前端；SQX 是 Square 原生语法；错误带行列号 |
| `Square.Compiler` | Roslyn Incremental Generator，`.sqv` / `.sqx` → C# | Props/ref/绑定/事件；结构指令经 `[SqxDirective]` Catalog 发射；诊断映射 |
| `Square.Runtime` | `Application`、生命周期、调度、信号、DOM 事件 | UI Dispatcher；`EventTarget`/`Event`；`[SqxDirective]` 特性 |
| `Square.UI` | `Node`/`Element`/`UIElement`/`Document`/`UIDocument`/`XMLDocument`/`SVGDocument`、属性 | UI Element Tree；嵌入 SVG DOM；Style/ClassList/Children；Reconciler 批量更新 |
| `Square.Controls` | 控件 + 结构原语运行时 + 动画 | 控件 = 元素 + 行为 + 默认样式；指令 marker；`CreateElement` 注册 |
| `Square.Extensions.Routing` | 可选窗口路由、守卫、KeepAlive 与嵌套 RouterView | `AppWindow.UseRouter` 静态页面工厂、参数/通配符、RouterLink、按路径缓存 |
| `Square.CSS` | CSS 引擎 | Selector/Cascade/Specificity/Var/Inheritance；Animation；Theme；M1 子集 |
| `Square.Graphics` | `IRenderContext` 抽象 + 绘图原语 | 工厂 `IRenderBackendFactory`；原语 Geometry/Brush/Pen/Font/Path/Transform/Clip |
| `Square.Rendering` | Element→Layout→Display Tree→DrawCommand | Flex/Block 经 Yoga.Net（Meta Yoga C# 移植）；Grid 内置；保留模式、脏区/增量 |
| `Square.Text` | 文本引擎 | Unicode/Glyph/Font/Layout/Caret/Selection/HitTest/BiDi；FontFace/FontFaceSet |
| `Square.Platform` | 平台宿主抽象 | `IPlatformHost`、`IPlatformFactory`、`PlatformRegistry` 与跨平台截图入口 |
| `Square.Platform.Win32` | Windows 平台实现 | Win32 窗口、消息循环、输入、IME、剪贴板与窗口截图 |
| `Square.Platform.X11` | Linux 平台实现 | X11 窗口、事件循环、输入、IME、剪贴板与窗口截图 |
| `Square.Extensions` | 可选扩展 | RichText、Routing 与文件弹窗；由应用显式注册，不被核心反向依赖 |
| `Square.Extensions.Markdown` | Markdown 扩展 | Markdig 文档模型与 TextMate 代码块高亮；通过 `MarkdownRegistration` 注册 |
| `Square.Extensions.CodeEditor` | 代码编辑扩展 | PieceTable、视口绘制、多光标、折叠与 TextMate 高亮；通过 `CodeEditorRegistration` 注册 |
| `Square.Backends` | 渲染后端 | 纯 C# Software Renderer、Skia、Vulkan、Windows Direct2D |
| `Square.Hosting` | 桌面应用宿主 | `DesktopApplication(UIDocument)`：窗口、输入、焦点、帧调度、布局与 DisplayTree 提交 |
| `Square.Native.Html` | 静态语义 HTML | Element/NativeUiNode → browser semantic HTML + inline final CSS；不依赖桌面平台 |
| `Square.Hosting.Web` | ASP.NET Core Web Server 宿主 | 每请求组件工厂、HTML response 和请求级资源释放；可与桌面平台注册共存 |

**依赖方向**：`Square.Compiler` 在编译期生成组件；`Events` 保持平台与 UI 无关；`UI` → `Events`；`Controls/UI/Rendering/CSS/Text` → `Runtime` + `Graphics`（逻辑依赖）；具体 Platform/Backend 项目依赖核心抽象。核心层禁止反向依赖具体 Backend/Platform 实现。`Square.Hosting` 是聚合层，应用在启动前通过 `PlatformRegistry` 注册所引用的平台工厂。

内联 SVG 使用嵌入文档边界：宿主 `UIDocument` 只布局 `SVGSVGElement` 根盒；根元素持有 `SVGDocument : XMLDocument`，SVG 内部元素的 `OwnerDocument` 指向该 SVGDocument。根元素将 SVG 子树递归编译为现有 Geometry/Path/Transform 绘制命令；`PathGeometry` 统一通过 `FillGeometry` / `DrawGeometry` 进入后端，因此 `<path>`、`<polygon>`、`<polyline>` 的填充和描边在 Software 与 Vulkan 中共享相同的 SVG 上层实现，后端不需要单独理解 SVG DOM。

---

## 4. 组件模型

### 4.1 组件 = 模板 + 逻辑 + 样式

推荐使用 `.sqv` 作为默认组件格式。`.sqv` 使用 Vue 风格模板语法，`.sqx` 是 Square 原生模板语法；两者最终都会编译到同一个组件模型。

两种模板格式都使用无文件级根标签的顶级 section：

```
<template>   结构 + 绑定 + 流程控制（SQV 使用 Vue 风格语法）
<script lang="csharp">  C# 逻辑 + Props 声明 + 文件级元数据
<style>  CSS 样式
```

`<template>` 必须且只能有一个；`<script>`、`<style>` 可选且各自最多一个。Source Generator 将三个 section 编译为同一个 `partial` 组件类。组件名默认取文件名，文件级元数据声明在 `<script>` 标签属性上。

SQV 示例：

```vue
<template>
  <Button :disabled="Saving" @click="Save">Save</Button>
  <Text v-if="Saved.Value">Saved</Text>
</template>
```

### 4.2 Props（组件输入契约）

- 声明：`<script lang="csharp">` 中 `[Prop]` 特性
- 类型：`ObservableValue<T>`（生成器辅助包装）
- 数据流：父→子单向，子不可改写
- 响应：子组件订阅 prop 或重写 `OnPropChanged`
- 校验：编译期 Generator 检查必填 prop
- 内置元素属性与自定义组件 Props 共用机制

详见 `Sqx-Spec.md` §Props。

#### 4.2.1 自定义事件（组件输出契约）

- 组件以 `public/internal static readonly ComponentEvent` 或 `ComponentEvent<TDetail>` 声明事件契约。
- 组件内部通过 `Emit(ComponentEvent)` / `Emit(ComponentEvent<TDetail>, detail)` 同步派发。
- 调用方在 SQX 使用 `onSelected={Handler}`，在 SQV 使用 `@selected="Handler"`。
- 有载荷 handler 接收 `CustomEvent<TDetail>`，从 `Detail` 读取强类型值。
- 组件事件默认不冒泡、不可取消，只通知直接监听该组件实例的调用方；需要 DOM 冒泡或取消语义时显式使用 `DispatchEvent`。
- 未声明事件继续兼容现有字符串监听；跨线程或无直接父子关系的通信仍使用 `Signal<T>`。

### 4.3 绑定模型

- `ObservableValue<T>`：强类型、委托订阅、零反射
- `ObservableCollection<T>`：列表原语，支撑 `<For>`
- 绑定语法：`{expr}`（文本/属性/事件/流程控制同源）
- 双向：`value={expr} onInput={Method}` 显式表达

详见 `Sqx-Spec.md` §绑定。

### 4.4 结构化流程控制

| 原语 | 用途 |
|---|---|
| `<Show when={expr}>` | 条件子树 |
| `<For each={expr}>` | 列表 |
| `<Switch>` + `<Match when={expr}>` | 多分支 |
| `<Index each={expr}>` | 索引列表（可选） |

编译为 `ObservableValue`/`ObservableCollection` 驱动的细粒度控制流，无虚拟 DOM。

详见 `Sqx-Spec.md` §流程控制。

### 4.5 生命周期钩子

| 钩子 | 触发时机 |
|---|---|
| `OnPropChanged(name)` | Props 值变化 |
| `OnAttached` | 挂载到视觉树 |
| `OnDetached` | 从视觉树卸载 |
| `OnLoaded` | 加载完成 |
| `OnUnloaded` | 卸载完成 |
| `OnMeasure` | 布局测量 |
| `OnArrange` | 布局排列 |
| `OnStart` / `OnExit` | 应用级 |

### 4.6 组件内容与插槽

- 调用处 children 编译为调用方作用域内的 `RenderFragment`。
- `<Slot>` 是生成器结构节点，不产生额外布局容器。
- 默认、具名与 fallback 内容由 `SlotOutlet` 管理为连续子节点区域。
- 嵌套路由布局复用默认 Slot；`Outlet` 只是路由语义别名。

### 4.7 路由

路由位于可选的 `Square.Extensions.Routing` 中，通过 `AppWindow.UseRouter` 注册窗口级 Router。页面使用静态构造委托，满足 NativeAOT 的零反射约束；布局组件通过嵌套 `<RouterView>` 显示下一层匹配页面。

路由切换通过 `ChildrenCollection` 替换当前层页面，因此沿用视觉树生命周期和布局失效机制。`KeepAlive` 页面按路由定义和实际匹配路径缓存，查询变化复用页面实例；非缓存页面离开时释放生成子树资源。

### 4.8 Tabs 自定义组合示例

`Tabs` 不属于标准控件，也不引入新的结构原语。Sample 展示了开发者如何将页签按钮投影到 `tabs` 命名 Slot、将对应页面投影到默认 Slot，并按索引维护按钮选中状态和页面可见性。页签与页面一一对应，Slot 不产生额外布局节点。

### 4.9 跨组件信号与线程切换

- `ObservableValue<T>` 继续承担组件局部属性绑定，不保证跨线程访问。
- `Signal<T>` 是线程安全的状态信号；发布时对订阅者使用快照，允许订阅者在回调中取消订阅。
- `SignalHub` 按名称共享强类型信号。相同名称只能绑定一种 `T`，类型冲突立即抛错。
- 未指定 `Dispatcher` 的订阅在发布线程同步执行；绑定 `Dispatcher` 后，后台发布会排队到该 Dispatcher 的所属线程。
- 组件在 `OnAttached` 订阅，在 `OnDetached` 释放订阅，避免卸载组件继续接收消息。
- Dispatcher 队列由平台消息循环在 UI 线程排空；后台线程不得直接修改 Element Tree。

完整用法与生命周期示例见 `Composition-and-Signals.md`。

---

## 5. 元素操作管线

### 5.1 引用获取（ref）

```
模板：<Button ref={MyBtn}>Click</Button>
生成：partial 类中产出 Button MyBtn; 字段
运行：元素挂载时赋值，卸载时置 null
```

### 5.2 命令式 API

```
el.Style.Set("color", "red")
el.ClassList.Add("active")
el.AppendChild(new Text("hello"))
el.Children
el.AddEventListener("click", handler)
```

### 5.3 仲裁规则

```
声明式绑定属性  ──┐
                  ├── 同一属性：声明式优先，命令式写入会被下一次源变更覆盖
命令式写入      ──┘

<Show>/<For> 子树 ── 声明式控制流管理，命令式不侵入
静态声明区域     ── 命令式可自由增删
```

### 5.4 元素创建

`new Button()` 命令式构造 → `AppendChild` 挂载 → 接生命周期钩子。

---

## 6. 构建层裁剪

平台/后端选择由构建层在编译期完成：

- C# 逻辑内 `#if`/`#endif`
- MSBuild `DefineConstants`：`PLATFORM_*`/`BACKEND_*`
- 条件 `ProjectReference` 控制后端/宿主装配

```
PLATFORM_WIN32 / X11 / MACOS / ANDROID / IOS / WASM
BACKEND_SOFTWARE / SKIA / BLEND2D / CAIRO
```

价值：避免运行时平台判断，减小体积；被条件包含的路径不会被 trim 误删。

---

## 7. 关键技术决策

| 决策 | 选择 | 理由 |
|---|---|---|
| 绑定后端 | `ObservableValue<T>` 委托订阅 | AOT 安全、零反射、体积小 |
| 跨组件通信 | `Signal<T>` + `SignalHub` + `Dispatcher` | 强类型、线程安全、显式 UI 线程切换 |
| 流程控制 | 编译期命令式控制流，无 VDOM | 与 Retained Rendering 同构 |
| Props | `[Prop]` 特性 + `ObservableValue<T>` | C# 习惯、类型安全、编译期校验 |
| 元素操作 | ref + 强类型 API + 仲裁规则 | 声明式为主、命令式兜底 |
| 平台裁剪 | 构建层 `#if` + MSBuild | AOT 友好、编译期消除 |
| P/Invoke | `LibraryImport` 源生成 | AOT 合规 |
| 渲染后端 M1 | 纯 C# Software Renderer | 无 C++ 依赖，验证管线 |

---

## 8. 设计边界（Non-Goals）

- 不内置 JS 引擎 / WebView / JSBridge 的运行时渲染与响应式
- 不采用反射式 / Proxy 数据绑定
- 不采用运行时平台 `if/else` 判断
- 不采用虚拟 DOM 与运行时 diff
- 不提供隐式双向绑定
- 不采用运行时动态组件 / 运行时 DOM 搬运
- 命令式操作不覆盖声明式绑定（不静默回滚）
