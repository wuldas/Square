# Web Server 与静态 HTML 生成

> Document Revision: 0.1

Square 的 Web Server 模式由两个可选包组成：

- `Square.Native.Html`：将已经求值的 `Element Tree` 生成语义 HTML/CSS。
- `Square.Hosting.Web`：把组件工厂映射为 ASP.NET Core GET endpoint。

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

最终已应用样式通过 inline `style` 输出，并附带最小 browser baseline CSS。组件样式表无需在浏览器中重新执行 Square selector/cascade。

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
- 不生成 `onclick` 等浏览器内联事件。
- `Link` 仅允许相对 URL、fragment、HTTP、HTTPS 和 mailto。
- `Image` 仅允许相对 URL、HTTP、HTTPS 和 `data:image/*`。
- 不自动序列化任意 `PropertyStore` 值。

## 6. 运行示例

```bash
dotnet run --project samples/Square.Sample.WebServer/Square.Sample.WebServer.csproj
```

访问启动日志中的地址。示例页面由 `.sqv` Source Generator 生成，并包含表单控件、选择框、链接和路由参数页面。

## 7. 当前边界

- 生成的是请求时静态 HTML，不会把 Square C# click handler 自动变成浏览器交互。
- 浏览器原生表单控件可以输入，但数据不会自动回写服务器组件。
- 后续可以增加普通 Form POST 或局部 HTML 请求；SignalR 会话和 WASM 属于独立阶段。
- 浏览器布局是权威来源，输出不保证与 Software Renderer 像素完全一致。
