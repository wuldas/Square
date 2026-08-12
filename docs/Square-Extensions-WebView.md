# Square.Extensions.WebView 原生 WebView

> 状态：Win32 首期实现中
> 首期平台：Windows / Win32
> 原生引擎：Microsoft Edge WebView2 Evergreen Runtime

## 1. 定位

`Square.Extensions.WebView` 是一个可选的原生 WebView 扩展程序集。它把操作系统提供的浏览器视图嵌入 Square Visual Tree，但不把浏览器引擎放入 `Square` 核心，也不参与 Square 的 SQX/SQV 模板解析、CSS 基础设施或 DisplayTree 绘制。

本扩展与后续的 `Square.Extensions.Html` 分工如下：

| 扩展 | 定位 |
|---|---|
| `Square.Extensions.WebView` | 封装操作系统原生 WebView；首期为 Win32/WebView2 |
| `Square.Extensions.Html` | 后续自研 HTML/CSS 轻量内核，提供 `HtmlView` 与自研浏览上下文 |

## 2. 当前支持矩阵

| 能力 | 状态 |
|---|---|
| `WebView` Square 控件 | 已实现 |
| Win32 / Windows 10+ | 已实现首期后端 |
| WebView2 Evergreen Runtime | 运行前置条件 |
| URL / data URI 导航 | 已实现 |
| HTML 字符串加载 | 已实现 |
| 前进、后退、刷新、停止 | 已实现 |
| 导航开始/完成/失败事件 | 已实现 |
| 标题变化和历史状态 | 已实现 |
| Square 布局、DPI、可见性同步 | 已实现 |
| `Init` 文档启动脚本 | 已实现 |
| `Eval` JavaScript 执行 | 已实现 |
| `Dispatch` UI 线程调度 | 已实现 |
| `Bind` / `Unbind` / `Return` 双向 JSON RPC | 已实现 |
| WebView2Aot NativeAOT 编译发布 | 已验证发布产物 |
| 最终修复后的 GUI 导航 smoke test | 已验证 |
| macOS / WKWebView | 后续计划 |
| X11 / WebKitGTK | 后续计划；当前 Xlib 宿主不能直接宣称支持 |
| Cookie、下载、权限、代理 | 尚未实现 |
| 自研 HTML/CSS 渲染 | 不属于本扩展 |

## 3. 公共 API

```csharp
using Square.Extensions.WebView;

WebViewRegistration.RegisterDefaults();

var browser = new WebView();
browser.Init("window.__squareReady = true;");
await browser.Navigate("https://example.com");
await browser.SetHtml("<h1>Hello</h1>");
await browser.Eval("document.title = 'Square';");
await browser.GoBackAsync();
await browser.ReloadAsync();
```

### JavaScript Bridge

绑定使用 JSON 请求和 JSON 结果，不暴露 `CoreWebView2`、`CoreWebView2Controller` 或其它 COM 类型：

```csharp
await browser.Bind("add", async request =>
{
    // request.ArgumentsJson 是 JavaScript 参数数组
    await request.ReturnAsync(0, "5");
});

await browser.Unbind("add");

// 同步返回（仅适合已经位于 WebView UI 调度上下文的简单 handler）
browser.Return("request-id", 0, "5");
```

`Bind` 的 JavaScript 函数返回 Promise。请求消息包含 `id`、`method` 和 `params`；返回消息包含 `id`、`status` 和 `result`。

## 4. 运行时分发

首期使用 Evergreen Runtime。`Square.Extensions.WebView` 不捆绑 Fixed Version Runtime，也不在控件内部静默下载浏览器运行时。

应用发布方必须：

1. 在目标 Windows 设备安装 WebView2 Runtime；
2. 使用 STA UI 线程启动窗口；
3. 在应用启动或控件初始化失败时提供明确诊断；
4. 如果需要 Fixed Version Runtime，由应用发布层负责打包、权限和更新。

`WebView2Loader.dll` 以架构相关的嵌入资源提供给 `WebView2Aot`，运行时会按进程架构提取并加载，不要求它出现在 publish 根目录。

## 5. 原生视图边界

WebView 是 native island，不进入 Square `DisplayTree`。Square 核心只负责：

- 生命周期通知；
- 逻辑坐标到原生像素坐标的同步；
- bounds、DPI、visibility 和 detach；
- 通用 native-view hosting 协议。

首期只承诺矩形 WebView。祖先滚动/复杂裁剪、transform、opacity、Popup、圆角裁剪和跨层 z-index 需要单独验证，不能从普通 Square 绘制行为推断 native overlay 已正确支持。

## 6. NativeAOT

WebView2Aot 绑定已在 `net10.0/win-x64` 下完成 NativeAOT publish，产物检查结果为原生 `PE32+` x64 GUI executable。当前已在安装 WebView2 Runtime 的 Windows 环境中完成最终 GUI smoke test，并观察到 `NavigationStarting` 与 `NavigationCompleted(success=True)`；这不代表每台设备都具备 WebView2 Runtime。

实现中避免引用 WPF/WinForms WebView2 控件包，以免把不必要的 `WindowsBase` 冲突带入扩展程序集。

## 7. 验证原则

- fake backend 测试公共 API、生命周期、导航状态、bounds 同步和 JSON RPC；
- Windows smoke test 必须实际创建 WebView2 controller 并加载本地 fixture；
- renderer-only 截图与窗口级截图分开标记，因为 native overlay 未必进入 Square renderer capture；
- 当前仓库没有 canonical verifier，最终结果称为 ad-hoc verification；
- 构建前处理明确报告的旧 Sample DLL 锁定进程，不能把进程状态误报为源代码失败。
