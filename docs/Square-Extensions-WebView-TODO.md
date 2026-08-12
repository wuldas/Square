# Square.Extensions.WebView TODO

## 已完成

- [x] 单独提交 `Square.Extensions.WebView` 规格文档
- [x] 完成 WebView2Aot / .NET 10 / NativeAOT publish spike
- [x] 创建 `src/Square.Extensions.WebView`
- [x] 创建 `tests/Square.Extensions.WebView.Tests`
- [x] 添加幂等 `WebViewRegistration.RegisterDefaults()`
- [x] 注册 `WebView` 标签
- [x] 增加 fake backend
- [x] 增加通用 native-view 元素协议
- [x] 增加 host-ready/loaded 初始化时机
- [x] 同步逻辑 bounds、physical bounds、DPI 和 visibility
- [x] 处理 detach、dispose 和重复生命周期
- [x] 创建共享 WebView2 environment 管理器
- [x] 创建 CoreWebView2Controller
- [x] 实现 `Navigate` / `NavigateAsync` / HTML 字符串加载
- [x] 实现 Back / Forward / Reload / Stop
- [x] 归一化导航、标题、历史和错误事件
- [x] 实现 `Init` / `Eval` / `Dispatch`
- [x] 实现 `Bind` / `Unbind` / `Return` 双向 JSON RPC
- [x] 提供按架构嵌入的 `WebView2Loader.dll`
- [x] 创建 Windows Sample
- [x] 扩展 Release 测试 12/12 通过
- [x] NativeAOT publish 生成原生 x64 PE 产物
- [x] 最终修复后的普通 Win32 Sample 导航 smoke test
- [x] 最终修复后的 NativeAOT Win32 Sample 导航 smoke test

## 当前待完成

- [x] 定位并修复普通/NativeAOT Sample 运行后退出码 127 的原因
- [x] 收集最终修复后的 `NavigationStarting` / `NavigationCompleted` 日志
- [ ] 实际验证 100% / 125% / 150% DPI
- [ ] 实际验证 resize、最小化、恢复、关闭
- [ ] native overlay 窗口级截图
- [x] 使用 `hermes-verify-` 临时脚本完成最终 ad-hoc verification
- [ ] 更新 Architecture / Rendering-Targets 文档

## 后续阶段

- [ ] macOS WKWebView adapter
- [ ] GTK/WebKitGTK host 与 adapter
- [ ] 下载、权限、Cookie 和 DevTools 能力
- [ ] `Square.Extensions.Html` 自研 HTML/CSS 内核
