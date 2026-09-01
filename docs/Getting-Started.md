# 入门指南

> Document Revision: 0.3
> 配套：`vue-plan.md`、`Architecture.md`、`Sqx-Spec.md`、`API-Reference.md`

本文带你从零创建一个 Square 桌面应用，默认使用 `.sqv` 的 Vue 模板语法编写组件。`.sqx` 原生语法仍可用，详见 `Sqx-Spec.md`。

---

## 1. 环境要求

- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows 10+（当前主要验证平台）

```bash
dotnet --version
```

---

## 2. 创建项目

### 2.1 新建控制台项目

```bash
dotnet new console -n MyApp -o MyApp
cd MyApp
```

### 2.2 修改 csproj

将 `OutputType` 改为 `WinExe`，添加 Square 框架项目引用和 Source Generator 分析器引用：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PublishAot>true</PublishAot>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="path\to\src\Square\Square.csproj" />
    <ProjectReference Include="path\to\src\Square.Platform.Win32\Square.Platform.Win32.csproj" />
    <ProjectReference Include="path\to\src\Square.Compiler\Square.Compiler.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false"
                      SetTargetFramework="TargetFramework=netstandard2.0" />
  </ItemGroup>

  <ItemGroup>
    <AdditionalFiles Include="**\*.sqv" />
  </ItemGroup>

</Project>
```

> 如果你直接在 Square 仓库内开发，路径使用 `..\..\src\...` 相对引用。

关键配置说明：

| 配置 | 作用 |
|---|---|
| `OutputType=WinExe` | Windows 桌面应用，不弹出控制台窗口 |
| `PublishAot=true` | 启用 NativeAOT 发布 |
| `OutputItemType="Analyzer"` | Source Generator 作为分析器引用，不输出程序集 |
| `AdditionalFiles Include="**\*.sqv"` | 将 `.sqv` 文件注册为 Source Generator 输入 |

`Square` 包含桌面运行时、控件、CSS、路由、布局和软件渲染。窗口宿主由 `Square.Platform.Win32` 或 `Square.Platform.X11` 提供；Skia、Vulkan、Windows-only Direct2D、Extensions 与 DevTools 按需单独引用。

Windows 应用引用 `Square.Backends.Direct2D` 后，可在运行前显式选择 Direct2D：

```csharp
using Square.Backends.Direct2D;

window.UseDirect2DBackend();
new DesktopApplication(window).Run();
```

Direct2D 使用 `ID2D1HwndRenderTarget` 直接绘制 Win32 窗口，并以 DirectWrite 统一普通文本的 shaping、测量、换行、cluster、BiDi、命中、selection/caret 和绘制；已加载的内存 `FontFace` 也进入 DirectWrite custom collection。暂不支持的文本选项整体回退 Square 原路径。该后端只声明全帧渲染；`CaptureRendererBitmapAsync()` 暂时使用 Software DisplayTree 重放，真实 D2D/DirectWrite 验证使用窗口截图。

### 2.3 编写入口

将 `Program.cs` 替换为：

```csharp
using Square.Hosting;

var window = new AppWindow("My First App", 600, 400);
window.LoadGlobalCss("Styles/reset.css", "Styles/app.css");
window.Load(new Main());

new DesktopApplication(window).Run();
```

Windows 项目引用 `Square.Platform.Win32`，Linux 项目引用 `Square.Platform.X11`。平台包自动注册默认宿主，不需要在 `Program.cs` 中调用 `PlatformRegistry.Register(...)`。

`Main` 是由 `Main.sqv` 在编译期生成的组件类。`AppWindow` 负责窗口内容、尺寸、标题栏和渲染配置；`DesktopApplication` 负责应用生命周期和消息循环。

`LoadGlobalCss` 可一次传入多个 CSS 文件，也可多次调用。文件按加载顺序参与级联，同等 specificity 下后加载的规则覆盖先加载规则。相对路径以应用程序输出目录为基准，因此需要在项目文件中复制 CSS：

```xml
<ItemGroup>
  <Content Include="Styles\**\*.css" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

也可以直接加载内存中的 CSS 文本：

```csharp
window.LoadGlobalCssText(
    ":root { --primary: #0078d4; }",
    "Button { background: var(--primary); }");
```

全局 CSS 应在 `DesktopApplication.Run()` 前加载。组件 `<style>` 仍作为组件作用域应用，并在同等 specificity 下覆盖全局样式。

文件样式表支持本地 `@import`：

```css
@import "reset.css";
@import url("./themes/light.css");

Button {
  color: var(--button-color);
}
```

相对地址以声明 `@import` 的 CSS 文件目录为基准，支持递归导入并检测循环引用。`@import` 必须位于普通样式规则之前；出现在普通规则之后时按 CSS 规范忽略。当前仅支持本地文件和无条件导入，HTTP/HTTPS、media 条件、`supports()` 和 `layer` 尚不支持。由于内存 CSS 没有来源文件，`LoadGlobalCssText` 中的相对 `@import` 会抛出异常。

已加载的顶层样式表可通过 `window.Document.StyleSheets` 按顺序枚举；每个 `DocumentStyleSheet.Imports` 保存其直接导入关系。

---

## 3. 编写第一个组件

### 3.1 创建 Main.sqv

在项目根目录创建 `Main.sqv`：

```vue
<template>
  <View class="container">
    <Text class="title">Hello Square</Text>
    <Input :value="Name" @input="OnNameChanged" placeholder="输入你的名字" />
    <Button @click="OnGreet" class="greet-btn">打招呼</Button>
    <Text v-if="Greeted.Value" class="greeting">你好，{{ Name.Value }}！</Text>
  </View>
</template>

<script lang="csharp">
  public ObservableValue<string> Name = new("");
  public ObservableValue<bool> Greeted = new(false);

  private void OnNameChanged(Event e)
  {
    Name.Value = ((Input)e.Target!).Value;
  }

  private void OnGreet()
  {
    Greeted.Value = true;
  }
</script>

<style>
  .container {
    display: flex;
    flex-direction: column;
    gap: 12px;
    padding: 24px;
  }

  .title {
    color: #202124;
    font-size: 20px;
  }

  .greet-btn {
    background: #0078d4;
    color: #ffffff;
  }

  .greeting {
    color: #107c10;
    font-size: 16px;
  }
</style>
```

### 3.2 文件结构

```
MyApp/
  MyApp.csproj
  Program.cs
  Main.sqv
```

### 3.3 构建和运行

```bash
dotnet build
dotnet run
```

如果 `.sqv` 有语法错误，编译时会直接报错并指向文件和行列号。运行后应看到一个窗口，包含标题、输入框、按钮和条件问候文本。

开发桌面应用时建议在 Debug 配置下使用：

```bash
dotnet watch --project MyApp.csproj
```

`Square.Compiler` 会把 `.sqv` / `.sqx` 作为 `AdditionalFiles` 加入 watch 列表。保存普通 C#、模板或组件 `<style>` 后，Roslyn 生成并应用 metadata delta；Square 再把 UI 更新投递到窗口的 Dispatcher。整个过程仍是编译期生成 C#，不会在运行时解析模板。

模板结构或组件样式变化会复用窗口的顶层生成组件实例并重建后代；窗口 `Content` / 自定义标题栏需要以生成组件作为顶层根。顶层组件字段、`ObservableValue<T>`、集合和窗口 Store 保留，但根组件与后代会重新执行 unload/detach/attach/load 生命周期，生命周期代码应支持重复挂载。后代实例、未绑定输入值、焦点、滚动位置、文本选择与运行中动画不保证保留。以下修改通常仍需重启：

- 重命名或移动模板文件、重命名组件类型。
- 删除成员、改变基类或接口。
- 删除、改名或改变生成 `ref` 的类型。

Hot Reload 仅支持非裁剪的 Debug 构建；Release、trimming、ReadyToRun 和 NativeAOT 不在支持范围内。

---

## 4. 理解 SQV 组件结构

`.sqv` 文件使用 Vue 模板语法，并保留 Square 的三个顶级 section。Source Generator 将它们编译为同一个 `partial` 组件类：

```xml
<template>
  <!-- Vue 风格结构、绑定、事件和流程控制 -->
</template>

<script lang="csharp">
  // C# 逻辑 + Props 声明
</script>

<style>
  /* CSS 样式 */
</style>
```

| Section | 必需 | 数量 | 职责 |
|---|---|---|---|
| `<template>` | 是 | 1 | UI 结构、绑定、事件、流程控制 |
| `<script>` | 否 | 0-1 | C# 逻辑、Props 声明、文件级元数据 |
| `<style>` | 否 | 0-1 | CSS 样式 |

### 4.1 组件名与命名空间

组件名默认取文件名。可在 `<script>` 标签属性上覆盖：

```xml
<script lang="csharp" namespace="MyApp.Components" name="HomePage" access="internal">
```

### 4.2 编译产物

Source Generator 生成类似以下的 C# 代码（在 `obj/Generated` 下可查看）：

```csharp
public partial class Main : UIElement
{
    public ObservableValue<string> Name = new("");
    public ObservableValue<bool> Greeted = new(false);

    protected override void BuildElementTree()
    {
        var view = new View();
        var title = new Text("Hello Square");
        var input = new Input();
        input.BindProperty("value", () => Name.Value);
        input.AddEventListener("input", OnNameChanged);
        // ...
    }
}
```

运行时零解析，不引入 Vue 运行时或 JavaScript 引擎。所有 UI 在编译期已生成普通 C# 类型。

---

## 5. 数据绑定

### 5.1 ObservableValue

`ObservableValue<T>` 是绑定的基础原语：

```csharp
public ObservableValue<string> Name = new("");
public ObservableValue<int> Count = new(0);
public ObservableValue<bool> Visible = new(true);
```

### 5.2 文本插值

```vue
<Text>你好，{{ Name.Value }}</Text>
<Text>{{ Count.Value }} 次点击</Text>
```

`{{ expr }}` 在编译期解析为 C# 表达式，并由生成器建立订阅。

### 5.3 属性绑定

```vue
<Text :text="Title" />
<View :class="ActiveClass" />
```

### 5.4 事件处理

```vue
<Button @click="OnClick">Click</Button>
<Input @input="OnInput" />
```

SQV 使用 Vue 风格事件绑定：`@click="OnClick"`。Handler 支持三种签名：

```csharp
private void OnClick() { }
private void OnClick(Event e) { }

```

### 5.5 双向绑定（显式）

Square 不提供隐式双向绑定。单向属性绑定 + 事件回写：

```vue
<Input :value="Name" @input="OnNameChanged" />
```

```csharp
private void OnNameChanged(Event e)
{
    Name.Value = ((Input)e.Target!).Value;
}
```

---

## 6. Props：组件输入

### 6.1 声明 Props

在 `<script>` 中用 `[Prop]` 特性声明：

```csharp
[Prop] public ObservableValue<string> Title { get; set; } = new("");
[Prop(Required = true)] public ObservableValue<int> Count { get; set; } = new(0);
```

### 6.2 传值

调用方在模板中以属性形式传入：

```xml
<UserCard Title={PageTitle} Count={ItemCount} />
<UserCard Title="Hello" Count={5} />
```

### 6.3 数据流

- 单向：父 → 子
- 子组件不可改写 Props 值
- 父组件源变化时子组件自动更新
- 子组件可订阅 prop 或重写 `OnPropChanged` 响应

```csharp
protected override void OnPropChanged(string name)
{
    if (name == nameof(Title))
    {
        // 响应 Title 变化
    }
}
```

### 6.4 编译期校验

必填 Prop 缺失时，编译期报诊断（带 `.sqv` / `.sqx` 行列号）。

---

## 7. 流程控制

### 7.1 条件渲染

```vue
<Text v-if="IsLoggedIn.Value">欢迎回来</Text>
```

`v-if` 绑定 C# 布尔表达式。条件变化时增删 Element 子树。

### 7.2 列表渲染

```vue
<Text v-for="item in Items" :key="item.Id">
  {{ item.Name }}
</Text>
```

`v-for` 绑定 `ObservableCollection<T>`。引用键增量更新，项移动时节点不重建。

声明集合：

```csharp
public ObservableCollection<TodoItem> Items = new();
```

### 7.3 多分支

```vue
<Text v-if="Status.Value == &quot;loading&quot;">加载中</Text>
<Text v-else-if="Status.Value == &quot;done&quot;">完成</Text>
<Text v-else>未知状态</Text>
```

---

## 8. 插槽与组合

### 8.1 默认插槽

```xml
<!-- Card.sqv -->
<View class="card">
  <View class="card-body">
    <Slot />
  </View>
</View>
```

```xml
<Card>
  <Text>这是卡片内容</Text>
</Card>
```

### 8.2 具名插槽

```xml
<!-- Panel.sqv -->
<View class="panel">
  <View class="panel-header"><Slot name="header"><Text>默认标题</Text></Slot></View>
  <View class="panel-content"><Slot /></View>
</View>
```

```xml
<Panel>
  <Text slot="header">设置</Text>
  <SettingsPage />
</Panel>
```

插槽内容保持调用方作用域——事件和绑定仍访问调用方成员。`<Slot>` 不产生额外布局容器。

---

## 9. 样式

### 9.1 组件级 `<style>`

```xml
<style>
  .container {
    display: flex;
    flex-direction: column;
    gap: 12px;
    padding: 16px;
  }

  Button {
    background: #0078d4;
    color: #ffffff;
  }
</style>
```

### 9.2 内联样式与类

```xml
<Button style="color: red; padding: 8px;">Click</Button>
<Button class="primary large">Click</Button>
```

### 9.3 CSS 变量

```css
:root {
  --primary: #0078d4;
  --spacing: 16px;
}

Button {
  background: var(--primary);
  padding: var(--spacing);
}
```

### 9.4 支持的选择器

| 选择器 | 示例 | 状态 |
|---|---|---|
| 类型 | `Button` | ✅ |
| 类 | `.active` | ✅ |
| ID | `#main` | ✅ |
| 后代 | `View Text` | ✅ |
| 子代 | `View > Text` | ✅ |
| 相邻兄弟 | `Text + Text` | ✅ |
| 通用 | `*` | ✅ |
| 属性 | `[IsDisabled]` `[variant=primary]` `[tags~=primary]` `[lang|=en]` `[code^=pre]` `[code$=suffix]` `[code*=middle]` | ✅ |
| 伪类 | `:hover` `:focus` `:active` `:disabled` `:checked` | ✅ |

`Button`、`Input`、`TextArea`、`Select`、`CheckBox` 和 `Radio` 由 UA 样式默认 `appearance: auto`，对齐 Chrome `html.css` 浅色表单控件。Software/Skia/Vulkan 盒绘制消费计算样式；控件 `Paint` 只画内容。`appearance: none` 不自动清掉 UA 边框/背景。可以继续使用作者 `Button:hover` / `Button:active` 覆盖。

详见 [`CSS-Spec.md`](CSS-Spec.md)。

---

## 10. 生命周期

### 10.1 组件级钩子

| 钩子 | 触发时机 |
|---|---|
| `OnPropChanged(name)` | Props 值变化 |
| `OnAttached` | 挂载到视觉树 |
| `OnDetached` | 从视觉树卸载 |
| `OnLoaded` | 加载完成 |
| `OnUnloaded` | 卸载完成 |

### 10.2 使用示例

```csharp
protected override void OnAttachedCore()
{
    // 订阅信号、初始化资源
}

protected override void OnDetachedCore()
{
    // 释放订阅、清理资源
}
```

---

## 11. 跨组件信号

`Signal<T>` 用于不相关组件之间的状态共享，`SignalHub` 按名称共享强类型信号。

### 11.1 定义信号

```csharp
public static class AppSignals
{
    public static Signal<string> Status { get; } =
        SignalHub.Default.Get("app.status", "Ready");
}
```

### 11.2 发布

```csharp
AppSignals.Status.Publish("Processing");
```

### 11.3 订阅（带 Dispatcher 切换）

```csharp
private IDisposable? _subscription;

protected override void OnAttachedCore()
{
    _subscription = AppSignals.Status.Subscribe(
        value => StatusText.Value = value,
        Application.Current.Dispatcher,
        emitCurrent: true);
}

protected override void OnDetachedCore()
{
    _subscription?.Dispose();
    _subscription = null;
}
```

传入 `Dispatcher` 后，后台线程发布的回调会自动排队到 UI 线程执行。

详见 [`Composition-and-Signals.md`](Composition-and-Signals.md)。

---

## 12. 命令式元素操作

### 12.1 ref 引用

```vue
<Button ref="MyBtn">Click</Button>
```

生成器产出强类型字段 `internal Button MyBtn;`，挂载时赋值，卸载时置 null。

```csharp
MyBtn.Style.Set("color", "red");
MyBtn.ClassList.Add("active");
```

### 12.2 操作 API

```csharp
el.SetProperty("disabled", true);
el.GetProperty<bool>("disabled");
el.Style.Set("color", "red");
el.Style.Get("color");
el.ClassList.Add("active");
el.ClassList.Toggle("active");
el.AppendChild(new Text("hello"));
el.RemoveChild(child);
el.InsertBefore(newChild, refChild);
el.ClearChildren();
el.AddEventListener("click", handler);
el.RemoveEventListener("click", handler);
```

### 12.3 仲裁规则

- 命令式写入已绑定属性：允许，但下一次源变更会覆盖
- 命令式操作 `<Show>`/`<For>` 子树：不允许（会被控制流冲掉）
- 命令式操作静态声明区域：允许
- 命令式创建并挂载元素：允许，接生命周期钩子

---

## 13. 路由

路由位于可选的 `Square.Extensions.Routing` 命名空间。

### 13.1 注册路由

```csharp
using Square.Extensions.Routing;

var router = window.UseRouter(routes =>
{
    routes.Map("/", static () => new HomePage());
    routes.Map("/users", static () => new UsersLayout(), route =>
    {
        route.KeepAlive = true;
        route.Map("", static () => new UserList());
        route.Map(":id", static () => new UserDetail(), child => child.KeepAlive = true);
    });
    routes.Map("*", static () => new NotFound());
});
```

匹配优先级：静态段 > `:parameter` > `*wildcard`。

模板出口：

```vue
<template>
  <RouterView ref="router" />
</template>

<script lang="csharp">
  using Square.Extensions.Routing;
</script>
```

布局组件内再放一个 `<RouterView>` 即可显示下一层子路由。

### 13.2 导航

```vue
<RouterLink to="/users/42">用户 42</RouterLink>
```

命令式导航：`Navigate`、`Replace`、`Back`、`Forward`。

路由守卫：

```csharp
router.BeforeEach((to, from) =>
    to.Path == "/admin" && !IsSignedIn
        ? RouteGuardResult.Redirect("/login")
        : RouteGuardResult.Allow);
```

### 13.3 运行仓库路由 Sample

SQX 和 SQV 主示例都包含完整路由页面：

```bash
dotnet run --project samples/Square.Sample/Square.Sample.csproj
dotnet run --project samples/Square.Sample.Vue/Square.Sample.Vue.csproj
```

在主界面选择 `Router` 页签，可以验证：

- `Home`、`User 42`、`User 7` 参数路由和查询参数。
- `Protected` 经过 `BeforeEach` 守卫重定向到登录页，并显示 `returnUrl`。
- 在用户页输入 KeepAlive note，切换到其他路由再返回，输入状态仍保留。
- `Back` / `Forward` 执行历史导航。
- `Go User 7` 演示通过 `RouterView` ref 命令式导航。
- `Clear Cache` 清理各层 RouterView 的 KeepAlive 页面缓存。

详见 [`Sqx-Spec.md`](Sqx-Spec.md) §10。

---

## 14. NativeAOT 发布

```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

输出位于：

```
bin/Release/net10.0/win-x64/publish/
```

发布主示例时需显式传入 Sample 的 AOT 开关：

```bash
dotnet publish samples/Square.Sample/Square.Sample.csproj \
  -c Release \
  -r win-x64 \
  -p:SquareSamplePublishAot=true \
  --self-contained true
```

Square 以 NativeAOT 兼容为设计约束：不使用 `Reflection.Emit`、`dynamic` 或运行时程序集发现。P/Invoke 使用源生成或显式静态入口。CI 在 Win32、X11 和 macOS runner 上发布 Software AOT 示例，启动原生可执行文件并执行像素截图回归；Vulkan、Direct2D 与 DevTools 也提供 AOT 路径，但组合场景仍属于实验性验证范围。Direct2D 使用 AOT-safe DirectNAot COM wrapper；Vulkan 后端通过显式系统库加载器加载 `vulkan-1.dll` 或 `libvulkan.so`，shader 使用构建期生成的内嵌 SPIR-V；DevTools 使用 `HttpListener`、显式路由和手写 JSON 输出。

主示例的 AOT 发布默认不引用 Vulkan、Direct2D 和 DevTools。需要 Vulkan AOT 时增加 `-p:SquareSampleUseVulkan=true`，Windows Direct2D 增加 `-p:SquareSampleUseDirect2D=true`，需要 DevTools 时增加 `-p:SquareSampleUseDevTools=true`；不传对应属性时，相关项目及其依赖不会进入发布产物。

---

## 15. 调试技巧

### 15.1 查看生成代码

Source Generator 默认只将源码交给编译器，不写入磁盘。需要检查 `BuildElementTree()` 等生成结果时临时执行：

```powershell
dotnet build -p:SquareEmitCompilerGeneratedFiles=true
```

不要在日常 Windows/Linux 多目标构建中长期启用磁盘输出，否则 IDE 可能同时索引多个 RuntimeIdentifier 下的同名 partial 类型。

### 15.2 诊断代码

| 诊断 | 说明 |
|---|---|
| `SQX0001` | 语法错误 |
| `SQX0002` | 未定义的控件 |
| `SQX0003` | 必填 Prop 缺失 |
| `SQX0004` | 绑定表达式成员未找到 |
| `SQX0005` | 事件方法签名不匹配 |
| `SQX0006` | ref 名称冲突 |
| `SQX0007` | Prop 类型不匹配 |

### 15.3 构建层裁剪

平台项目引用和后端通过 MSBuild 属性及 `DefineConstants` 在编译期选择：

| 常量 | 启用 |
|---|---|
| `PLATFORM_WIN32` | Win32 窗口宿主 |
| `PLATFORM_X11` | X11 窗口宿主 |
| `BACKEND_SOFTWARE` | 纯 C# 软件渲染器 |

`DesktopApplication` 在运行时注册默认软件后端和控件。具体平台位于独立程序集，引用 `Square.Platform.Win32` 或 `Square.Platform.X11` 后会自动成为默认平台；高级宿主仍可通过 `PlatformRegistry.Register(...)` 显式覆盖。

### 15.4 启用 DevTools

需要截图、输入自动化或运行时 Inspector 时，引用 `Square.DevTools`，并在 `app.Run()` 前启动服务：

```csharp
using Square.DevTools;

var devTools = app.UseDevToolsServer(new DevToolsOptions
{
  Port = 0
});

Console.WriteLine($"DevTools: {devTools.BaseAddress}/api/v1");
Console.WriteLine($"{DevToolsServer.TokenHeader}: {devTools.AccessToken}");

app.Run();
```

`Port = 0` 是推荐默认值，由操作系统为每个进程分配独立端口。多个应用或测试实例可以同时运行。连接方必须使用 `devTools.BaseAddress`，不能假设固定端口。

固定端口只用于外部系统要求稳定地址的场景；端口被占用时启动会失败，不会自动递增到其他端口。完整规则、认证和 HTTP API 见 [`DevTools.md`](DevTools.md)。

---

## 16. 下一步

- [API 参考](API-Reference.md) — 完整类型与方法签名
- [SQV / Vue 模板语法](vue-plan.md) — 默认模板语法
- [SQX 语言规范](Sqx-Spec.md) — Square 原生语法
- [CSS 规范](CSS-Spec.md) — 样式引擎支持范围
- [组件组合与信号](Composition-and-Signals.md) — Slot、自定义 Tabs 示例、Signal
- [总体架构](Architecture.md) — 模块划分与设计决策
- [Vue 示例代码](../samples/Square.Sample.Vue/) — 默认示例应用
