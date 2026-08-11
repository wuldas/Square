# Square.Extensions.WebView 原生 WebView

> 状态：实施中  
> 首期平台：Windows / Win32  ︎
> 原生引擎：Microsoft Edge WebView2 Evergreen Runtime

## 1. 定位

`Square.Extensions.WebView` 是一个可选的原生 WebView 扩展程序集。它把操作系统提供的浏览器视图嵌入 Square Visual Tree，但不把浏览器引擎放入 `Square` 核心，也不参与 Square 的 SQX/SQV 模板解析、CSS 基础设施或 DisplayTree 绘制。

本扩展与后续的 `Square.Extensions.Html` 分工如下：

| 扩展 | 定位 |
|---|---|
| `Square.Extensions.WebView` | 封装操作系统原生 WebView；首期为 Win32/WebView2 |
| `Square.Extensions.Html` | 后续自研 HTML/CSS 轻量内核，提供 `HtmlView` 与自研浏览上下文 |

## 2. 首期支持矩阵

| 能力 | 状态 |
|---|---|
| `WebView` Square 控件 | 实施中 |
| Win32 / Windows 10+ | 首期支持 |
| WebView2 Evergreen Runtime | 运行前置条件 |
| URL 导航 | 计划首期实现 |
| `NavigateToString` | 计划首期实现 |
| 前进、后退、刷新、停止 | 计划首期实现 |
| 导航开始/完成/失败事件 | 计划首期实现 |
| 标题变化 | 计划首期实现 |
| Square 布局、DPI、可见性同步 | 计划首期实现 |
| macOS/WKWebView | 后续计划 |
| X11/WebKitGTK | 后续计划；当前 Xlib 宿主不能直接宣称支持 |
| JavaScript Bridge | 首期不支持 |
| ExecuteScript | 首期不支持 |
| Cookie、下载、权限、代理 | 首期不支持 |
| 自研 HTML/CSS 渲染 | 不属于本扩展 |

## 3. 公共 API 方向

```csharp
using Square.Extensions.WebView;

WebViewRegistration.RegisterDefaults();

var browser = new WebView();
await browser.NavigateAsync("https://example.com");
await browser.GoBackAsync();
await browser.ReloadAsync();
```

首期公共接口不暴露 `CoreWebView2`、`CoreWebView2Controller` 或其它 COM 类型。脚本桥接、权限和下载模型必须在安全策略确定后单独设计。

## 4. 运行时分发

首期使用 Evergreen Runtime。`Square.Extensions.WebView` 不捆绑 Fixed Version Runtime，也不在控件内部静默下载浏览器运行时。

应用发布方必须：

1. 在目标 Windows 设备安装 WebView2 Runtime；
2. 在应用启动或控件初始化失败时提供明确诊断；
3. 如果需要 Fixed Version Runtime，由应用发布层负责打包、权限和更新。

## 5. 原生视图边界

WebView 是 native island，不进入 Square `DisplayTree`。Square 核心只负责：

- 生命周期通知；
- 逻辑坐标到原生像素坐标的同步；
- bounds、DPI、visibility 和 detach；
- 通用 native-view hosting 协议。

首期只承诺矩形 WebView。祖先滚动/复杂裁剪、transform、opacity、Popup、圆角裁剪和跨层 z-index 需要单独验证，不能从普通 Square 绘制行为推断 native overlay 已正确支持。

## 6. NativeAOT 状态

Microsoft.Web.WebView2 SDK 与当前 .NET 10/NativeAOT 组合必须先经过独立 spike。若 SDK 仍存在 trim、COM wrapper 或 NativeAOT 阻塞，必须在支持矩阵中标为 partial/blocking；不能通过向 Square 核心加入反射、动态代理或运行时程序集加载来绕过。

## 7. 验证原则

- 非 Windows 测试使用 fake backend，验证公共 API、生命周期、导航状态和 bounds 同步；
- Windows smoke test 必须实际创建 WebView2 controller 并加载本地 fixture；
- renderer-only 截图与窗口级截图分开标记，因为 native overlay 未必进入 Square renderer capture；
- 当前仓库没有 canonical verifier，最终结果称为 ad-hoc verification；
- 构建前处理明确报告的旧 Sample DLL 锁定进程，不能把进程状态误报为源代码失败。
