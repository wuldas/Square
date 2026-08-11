# Square.Extensions.WebView TODO

## 阶段 A：规格与可行性

- [ ] 单独提交 `Square.Extensions.WebView` 规格文档
- [ ] 完成 WebView2 SDK / .NET 10 / NativeAOT spike
- [ ] 记录 WebView2 Runtime 缺失时的真实错误
- [ ] 确认 Microsoft.Web.WebView2 稳定包版本和中央包配置

## 阶段 B：扩展边界

- [ ] 创建 `src/Square.Extensions.WebView`
- [ ] 创建 `tests/Square.Extensions.WebView.Tests`
- [ ] 添加幂等 `WebViewRegistration.RegisterDefaults()`
- [ ] 注册 `WebView` 标签
- [ ] 增加 fake backend

## 阶段 C：Square native-view hosting

- [ ] 增加通用 native-view 元素协议
- [ ] 增加 host-ready/loaded 初始化时机
- [ ] 同步逻辑 bounds、physical bounds、DPI 和 visibility
- [ ] 处理 detach、dispose 和重复生命周期
- [ ] 验证现有控件和 DevTools 改动不回归

## 阶段 D：Win32/WebView2

- [ ] 创建共享 WebView2 environment 管理器
- [ ] 创建 CoreWebView2Controller
- [ ] 实现 Navigate/NavigateToString
- [ ] 实现 Back/Forward/Reload/Stop
- [ ] 归一化导航、标题和错误事件
- [ ] 处理 Runtime 缺失
- [ ] 验证 100%/125%/150% DPI
- [ ] 验证 resize、最小化、恢复、关闭

## 阶段 E：验证与文档

- [ ] 创建 Windows sample 和本地 fixture
- [ ] fake backend 测试在非 Windows 平台通过
- [ ] Windows 真实 smoke test
- [ ] native overlay 窗口级截图
- [ ] NativeAOT publish（前提是 SDK spike 通过）
- [ ] 更新 Architecture/Rendering-Targets 文档
- [ ] 完成 `git diff --check`
- [ ] 使用 `hermes-verify-` 临时脚本完成 ad-hoc verification

## 后续阶段

- [ ] macOS WKWebView adapter
- [ ] GTK/WebKitGTK host 与 adapter
- [ ] JavaScript bridge 安全模型
- [ ] 下载、权限、Cookie 和 DevTools 能力
- [ ] `Square.Extensions.Html` 自研 HTML/CSS 内核
