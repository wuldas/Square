# Web Server、静态 HTML 与交互页面

> Document Revision: 0.2

Square 的 Web Server 模式由两个可选包组成：

- `Square.Native.Html`：将已经求值的 `Element Tree` 生成语义 HTML/CSS。
- `Square.Hosting.Web`：把组件工厂映射为 ASP.NET Core 静态或交互 endpoint。

它不是 WASM，也不会引入 JavaScript 引擎。`.sqx` / `.sqv` 仍由现有 Source Generator 编译为 C# 组件。

## 1. 基本用法

```csharp
using Square.Hosting.Web;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapSquarePage<Main>("/", options =>
{
    options.Html.Title = "Square Web";
    options.Html.Language = "zh-CN";
});

app.MapSquarePage("/users/{id}", context => new UserPage
{
    UserId = context.Request.RouteValues["id"]?.ToString() ?? ""
});

app.Run();
```

每个请求都会创建新的组件实例。Element Tree、绑定、响应式状态和 CSS scope 不会跨请求共享，请勿返回全局单例组件。

### 1.1 交互页面

需要让浏览器事件回到现有 C# handler 时，使用显式的交互页面 API：

```csharp
app.MapSquareInteractivePage<Main>("/app", options =>
{
    options.Html.Title = "Square Interactive";
    options.SessionIdleTimeout = TimeSpan.FromMinutes(20);
});
```

交互页面首次 GET 创建独立的服务端组件会话。浏览器通过同一路由的 JSON POST 回传 `click`、`input` 和 `change`；服务端同步原生表单值、派发 Square 事件、刷新 Dispatcher/Reconciler/CSS，再返回新的根节点 HTML 与 CSS。客户端替换 `.square-root`，并恢复焦点、文本 selection 和滚动位置。

模板无需使用另一套事件 API：`@click`、`@input`、`@change` 和 `v-model` 继续编译为现有 C# 监听器与绑定。例如：

```vue
<template>
  <View>
    <Input v-model="Name" />
    <Button @click="Save">Save</Button>
    <Text v-if="Saved" text="Saved" />
  </View>
</template>
```

不同页面加载使用随机 capability token 隔离状态。空闲会话默认保留 20 分钟，可通过 `SessionIdleTimeout` 调整；`MaxSessions` 控制单个 endpoint 同时保留的会话上限。应用停止时会释放生命周期、响应式绑定、Store 与 CSS scope。

## 2. 与桌面平台共存

`Square.Hosting.Web` 不实现或注册 `IPlatformFactory`，不读取或修改 `PlatformRegistry`，也不定义任何 `PLATFORM_*` 编译常量。应用可以同时引用 Win32、X11、macOS 平台包和 Web 包。

同一进程同时提供桌面窗口和 HTTP 服务：

```csharp
using Square.Hosting;
using Square.Hosting.Web;

var webBuilder = WebApplication.CreateBuilder(args);
var web = webBuilder.Build();
web.MapSquarePage<WebMain>("/");
await web.StartAsync();

try
{
    var window = new AppWindow("Square Desktop");
    window.Load(new DesktopMain());
    new DesktopApplication(window).Run();
}
finally
{
    await web.StopAsync();
    await web.DisposeAsync();
}
```

桌面和 Web 必须分别创建组件实例。不要把已经挂载到 `AppWindow` 的元素树同时交给 Web endpoint。

## 3. 输出模型

首版使用浏览器语义布局，不将 `Element.Geometry` 输出为绝对定位：

| Square | HTML |
|---|---|
| `View` / `ScrollViewer` | `div` |
| `Text` | `span` |
| `Button` | `button` |
| `Input` | `input` |
| `TextArea` | `textarea` |
| `CheckBox` / `Radio` | `label` + `input` |
| `Select` | `select` + `option` |
| `Link` | `a` |
| `Image.Source` / `Bitmap` | `img` / PNG data URI |
| `List` / `ListItem` | `ul` / `li` |
| 内联 SVG DOM | 原生 SVG 标签 |

最终已应用样式默认去重为 head 中的 CSS class，也可通过 `UseInlineStyles` 输出 inline `style`。组件样式表无需在浏览器中重新执行 Square selector/cascade。交互页面在每次事件后同时返回当前页面 CSS，不使用共享 stylesheet endpoint 表达会话动态状态。

表单控件默认 `appearance: auto`：基线 CSS 把它写在 `button,input,select,textarea` 上，让浏览器使用原生控件外观。`CheckBox` / `Radio` 的 `appearance` 写在内部 `input` 上，而不是外层 `label`。`appearance: none` 覆盖该基线，关闭原生 chrome。

## 4. 不支持控件

`Canvas`、Popup/Dialog/Menu overlay、富文本编辑器和代码编辑器等复杂控件首版输出占位：

```html
<div data-square-kind="Canvas" data-square-unsupported="true">
  Canvas is not supported by the static HTML target.
</div>
```

`HtmlExportResult.Diagnostics` 提供对应诊断。Web endpoint 默认不把诊断写入响应头；可显式启用：

```csharp
app.MapSquarePage<Main>("/", options =>
{
    options.IncludeDiagnosticHeaders = true;
});
```

## 5. 安全边界

- 文本、属性、class、id 和 style 均使用 HTML 编码。
- 不支持原始 HTML 注入。
- 不生成 `onclick` 等浏览器内联事件；交互页面使用单个委托式脚本监听已声明事件。
- `Link` 仅允许相对 URL、fragment、HTTP、HTTPS 和 mailto。
- `Image` 仅允许相对 URL、HTTP、HTTPS 和 `data:image/*`。
- 不自动序列化任意 `PropertyStore` 值。

## 6. 运行示例

```bash
dotnet run --project samples/Square.Sample.WebServer/Square.Sample.WebServer.csproj
```

访问启动日志中的地址。示例页面由 `.sqv` Source Generator 生成，并包含表单控件、选择框、链接和路由参数页面。

## 7. 当前边界

- `MapSquarePage` 仍生成请求时静态 HTML，不会保留状态或输出交互 runtime。
- `MapSquareInteractivePage` 当前桥接 `click`、`input` 和 `change`，并通过替换页面根节点更新 DOM；尚不提供细粒度 DOM diff。
- 交互会话保存在当前服务进程内。多实例部署需要粘性路由，进程重启会丢失页面状态。
- 页面 token 是同源事件能力凭据；如果组件 handler 执行业务写操作，应用仍须按 ASP.NET Core 常规方式实施认证、授权与输入验证。
- SignalR 主动推送、离线恢复、WASM 和通用 pointer/keyboard 事件属于独立阶段。
- 浏览器布局是权威来源，输出不保证与 Software Renderer 像素完全一致。
