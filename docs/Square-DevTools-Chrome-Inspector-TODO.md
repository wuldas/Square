# Square.DevTools Chrome Inspector 实施 TODO

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** 让显式启用 Chrome Inspector 的 Square 桌面应用能够被 `chrome://inspect` 发现，并使用 Chrome 原生 Elements、Computed Styles、Inline Styles、Box Model 和 Overlay 完成只读元素检查与双向点选。

**Architecture:** 保留现有 `/api/v1/*` HTTP API 作为 Square DevTools 的权威协议；新增一个可选、显式开启的 Chrome DevTools Protocol（CDP）适配层。适配层通过 `/json/*` 发现接口和 `/devtools/page/{targetId}` WebSocket，把现有 `ElementInspection`、Hit Test、截图和样式数据映射为有限的 `DOM`、`CSS`、`Overlay`、`Page`、`Runtime` domain，不把 Square/CLR 伪装成完整 Chromium/V8 运行时。

**Tech Stack:** .NET 10、`HttpListener`、`System.Net.WebSockets`、显式 `System.Text.Json` 读写、Chrome DevTools Protocol 1.3 子集、xUnit、Square 当前 Inspector/Dispatcher/DisplayTree。

### 当前状态

已实现 C0～C4 的首个协议切片：显式 feature gate、CDP discovery、WebSocket session、Runtime/DOM/Page/Target 最小命令、class 属性映射、基础 Matched CSS Rules、真实四层只读 Box Model、computed/inline styles、Chrome 原生 Overlay 高亮、双向点选、真实 Sample WebSocket smoke 和 11 个 DevTools 测试。细粒度树事件、Escape 退出点选、Overlay 四层不同颜色和样式编辑仍待后续阶段。

---

## 1. 范围与成功标准

### 1.1 第一版包含

- [x] 新增 `DevToolsOptions.AllowChromeInspect`，默认 `false`。
- [x] `AllowChromeInspect=true` 时提供 `/json/version`、`/json`、`/json/list` 和 `/json/protocol`。
- [x] 提供 `/devtools/page/{targetId}` CDP WebSocket。
- [x] `chrome://inspect` 所需的 discovery target 为 `type: "page"`。
- [x] Chrome Elements 面板显示当前 Square Element Tree。
- [x] 节点 ID 在单次 CDP session 期间稳定，并由 `Element.DebugId` 建立映射。
- [x] Chrome 能按节点查询属性、文本、父子关系、class 和真实只读 Box Model；四层几何来自当前 Square 布局与 CSS 盒模型解析结果。
- [x] Chrome Elements 选中节点时，Square 窗口显示基础诊断高亮。
- [x] Chrome 点选模式能在 Square 窗口 Hit Test，并发送 `Overlay.inspectNodeRequested` 反选事件。
- [x] Styles 面板显示只读的 computed/inline styles、匹配 selector/declarations 快照。
- [x] `Page.captureScreenshot` 映射现有 renderer screenshot。
- [ ] 树变化时至少通过粗粒度 `DOM.documentUpdated` 通知 Chrome。
- [x] 保持普通 DevTools HTTP API 的 token 认证和现有行为不变。
- [x] 保持 `Square.DevTools` NativeAOT 兼容，不使用反射式 endpoint/命令发现。

### 1.2 第一版明确不包含

- [ ] 不实现 Chrome Memory/HeapProfiler 面板。
- [ ] 不实现 V8 heap snapshot、CLR GC Root 或 allocation stack。
- [ ] 不实现 Network、Sources、Debugger、Performance、Application 面板。
- [ ] 不执行 `Runtime.evaluate` 中的任意 CLR/C# 表达式。
- [ ] 不把 Component/Slot 映射为 Shadow DOM。
- [ ] 不支持通过 Styles 面板修改样式。
- [ ] 不声称完整兼容 Chrome DevTools 或所有 Chrome 版本。
- [x] 不伪造不存在的源码定位；当前只返回 selector/declarations 快照，完整 stylesheet provenance 后置。

### 1.3 第一版验收场景

1. 运行 `Square.Sample --devtools --devtools-chrome-inspect`。
2. 在 `chrome://inspect/#devices` 配置应用输出的 loopback 地址。
3. Square target 出现在 Remote Target 列表中。
4. 点击 Inspect 后 Elements 面板可展开 Element Tree。
5. 选择一个 Button/Text/View 后：
   - Elements 显示正确 TagName、id、文本和层级；
   - Computed 显示 Square 的最终样式值；
   - Styles 显示内联声明；
   - Box Model 与 Square 最终布局一致；
   - Square 窗口高亮同一个元素。
6. 开启 Chrome 点选模式并点击 Square 窗口，Chrome 反选命中的 Element。
7. 普通 `/api/v1/health` 未带 token 仍返回 `401`。
8. `AllowChromeInspect=false` 时 `/json/list` 和 CDP WebSocket 不可用。

---

## 2. 当前代码基线

### 已有能力

- `src/Square/Hosting/Inspection/ElementInspection.cs`
  - 已有树快照、`DebugId`、TagName、ElementId、ComponentName、Bounds、交互状态、源码位置和文本。
- `src/Square/Hosting/DesktopApplication.cs:312-330`
  - 已有树快照、按 DebugId 查询和按坐标 Hit Test，且操作投递到 UI Dispatcher。
- `src/Square/Hosting/DesktopApplication.cs:434-455`
  - 已有 Element → `ElementInspectionNode` 映射。
- `src/Square/UI/ElementApi/StyleAccessor.cs:25-59`
  - 已有 `CssText` 和内联样式枚举入口。
- `src/Square/UI/ElementApi/StyleAccessor.cs:192-243`
  - 已有最终应用值读取。
- `src/Square/UI/ElementApi/StyleAccessor.cs:276-290`
  - 已有最终样式快照 `GetAll()`。
- `src/Square.DevTools/DevToolsServer.cs`
  - 已有 loopback `HttpListener`、token、安全默认值、截图和 Inspector HTTP 路由。
- `tests/Square.DevTools.Tests/DevToolsServerTests.cs`
  - 已有真实 loopback HTTP 集成测试结构。

### 当前缺口

- 没有 `/json/*` CDP target discovery。
- 没有 WebSocket server/session。
- 没有 CDP 消息 ID、响应、错误和事件模型。
- `ElementInspectionNode.Bounds` 只有一个矩形，没有 content/padding/border/margin 四层盒模型。
- 没有 Inspector 选中元素高亮状态及渲染入口。
- 没有点选模式输入拦截和 `Overlay.inspectNodeRequested` 事件。
- 没有样式规则来源、selector、stylesheet ID、声明覆盖关系，因此暂时不能诚实实现 matched rules。
- 现有 token header 无法直接用于 `chrome://inspect` 的 `/json/list` 和 WebSocket 连接，需要独立安全边界。

---

## 3. 文件规划

### 预计新增

- `src/Square.DevTools/Cdp/CdpTargetDiscovery.cs`
  - 生成 `/json/version`、`/json/list`、`/json/protocol` 响应。
- `src/Square.DevTools/Cdp/CdpSession.cs`
  - 管理单个 WebSocket、请求读取、响应序列化、事件发送和写入串行化。
- `src/Square.DevTools/Cdp/CdpCommandDispatcher.cs`
  - 使用显式 `switch` 分发 CDP method，避免反射并保持 NativeAOT 兼容。
- `src/Square.DevTools/Cdp/CdpDomDomain.cs`
  - DOM tree、attributes、text、box model、location hit test 映射。
- `src/Square.DevTools/Cdp/CdpCssDomain.cs`
  - computed/inline style 的只读映射。
- `src/Square.DevTools/Cdp/CdpOverlayDomain.cs`
  - highlight、hide、inspect mode 和反选事件。
- `src/Square.DevTools/Cdp/CdpPageDomain.cs`
  - layout metrics、bring-to-front、screenshot。
- `src/Square.DevTools/Cdp/CdpRuntimeDomain.cs`
  - Chrome 前端初始化所需的最小 Runtime 响应；不执行任意表达式。
- `tests/Square.DevTools.Tests/CdpDiscoveryTests.cs`
- `tests/Square.DevTools.Tests/CdpSessionTests.cs`
- `tests/Square.DevTools.Tests/CdpDomTests.cs`
- `tests/Square.DevTools.Tests/CdpCssTests.cs`
- `tests/Square.DevTools.Tests/CdpOverlayTests.cs`

### 预计修改

- `src/Square.DevTools/DevToolsOptions.cs`
  - 添加显式 CDP 开关。
- `src/Square.DevTools/DevToolsServer.cs`
  - 在普通 token API 外单独接入 discovery/WebSocket 路由；不把 domain 逻辑继续堆进该文件。
- `src/Square/Hosting/Inspection/ElementInspection.cs`
  - 增加 Box Model、属性和样式检查数据结构，或增加对应独立 record。
- `src/Square/Hosting/IAppWindowRuntime.cs`
  - 增加只读样式/盒模型查询及 Inspector Overlay 控制入口。
- `src/Square/Hosting/AppWindow.cs`
  - 转发新增 runtime 检查能力。
- `src/Square/Hosting/DesktopApplication.cs`
  - 在 UI 线程读取样式/盒模型、维护高亮状态、处理点选模式并请求重绘。
- `src/Square/Rendering/Layout/LayoutEngine.cs`
  - 仅在现有数据无法准确取得四层盒模型时增加可复用的盒模型快照入口；禁止在 DevTools 中重新实现布局算法。
- `samples/Square.Sample/Program.cs`
  - 添加 `--devtools-chrome-inspect` 显式开关，不随普通 `--devtools` 自动开启。
- `tests/Square.DevTools.Tests/DevToolsServerTests.cs`
  - 验证开关、安全边界和现有 API 无回归。
- `docs/DevTools.md`
  - 增加 Chrome Inspector 启用方式、支持矩阵、安全说明和排障步骤。

### 延后到 matched rules 阶段再评估

- `src/Square/CSS/Engine/CssStyleReconciler.cs`
- `src/Square/CSS/Engine/CssParser.cs`
- CSS rule/stylesheet/selector 相关模型文件
- `tests/Square.CSS.Tests/CssTests.cs`

这些文件只有在决定保留 selector、stylesheet、source span 和 cascade provenance 后才修改；第一版 computed/inline styles 不要求改动 CSS 级联内部结构。

---

## 4. 分阶段 TODO

## Phase C0：冻结协议边界与安全模型

### Task C0.1：记录 CDP 支持矩阵

**Objective:** 在实现前明确“支持、部分支持、不支持”，避免把有限适配描述为完整浏览器实现。

**Files:**
- Modify: `docs/DevTools.md`

**Steps:**

- [x] 写出 domain 支持矩阵：DOM、CSS、Overlay、Page、Runtime。
- [x] 标记 Network、Debugger、HeapProfiler、Tracing 等为 unsupported。
- [x] 明确第一版只读，样式编辑计划后置。
- [x] 明确只验证指定 Chrome stable 版本，不承诺任意版本。
- [x] 明确 CDP 模式与 token HTTP API 是不同安全边界。

**Verification:**

- [ ] 文档中没有“完整支持 Chrome DevTools”的表述。
- [ ] 每个可见 Chrome 面板都能对应到 implemented/partial/unsupported。

### Task C0.2：定义 CDP 安全规则

**Objective:** 决定无法携带自定义 header 时，哪些 discovery/WebSocket 请求可以访问。

**Decisions required:**

- [x] `AllowChromeInspect` 默认必须为 `false`。
- [x] 仅绑定 `127.0.0.1`；不得扩展到 `0.0.0.0` 或局域网地址。
- [x] 使用进程内随机 target ID。
- [x] 验证实际 Chrome DevTools WebSocket `Origin`，形成精确 allowlist；无 Origin 的本地协议测试也允许连接。
- [x] 拒绝普通 `http://`、`https://` 网页 Origin。
- [x] CDP discovery 不得削弱 `/api/v1/*` 的 token 检查。
- [x] 明确 `/json/list` 可被本机进程读取，因此随机 target ID 不是强认证。

---

## Phase C1：Target discovery 和 WebSocket 传输

### Task C1.1：先写 discovery feature gate 失败测试

**Objective:** 证明 CDP discovery 默认关闭，显式开启后才可发现 target。

**Files:**
- Test: `tests/Square.DevTools.Tests/CdpDiscoveryTests.cs`
- Modify: `src/Square.DevTools/DevToolsOptions.cs`
- Create: `src/Square.DevTools/Cdp/CdpTargetDiscovery.cs`
- Modify: `src/Square.DevTools/DevToolsServer.cs`

**TDD:**

- [x] RED：默认 options 请求 `/json/list` 返回 `404`。
- [x] RED：`AllowChromeInspect=true` 时 `/json/list` 返回一个 target。
- [x] RED：target 包含非空 `id`、`title`、`type: page`、`webSocketDebuggerUrl`。
- [x] GREEN：添加最小 option 和 discovery 路由。
- [x] REFACTOR：把 discovery JSON 构造移出 `DevToolsServer.cs`。

**Focused command:**

```bash
dotnet test tests/Square.DevTools.Tests/Square.DevTools.Tests.csproj --filter FullyQualifiedName~CdpDiscoveryTests
```

### Task C1.2：实现 `/json/version` 和 `/json/protocol`

**Objective:** 让 Chrome 能识别协议版本和 Square 实际暴露的 domain 子集。

**TDD:**

- [x] RED：验证 `Protocol-Version`、`Browser` 和 WebSocket URL。
- [x] RED：验证 `/json/protocol` 只声明真正支持的方法和事件。
- [x] GREEN：显式序列化 discovery/protocol JSON。
- [x] 禁止复制完整 Chromium protocol 后再大量返回“不支持”。

### Task C1.3：实现最小 WebSocket request/response

**Objective:** 接受 CDP JSON 请求，并保持 request `id` 关联。

**Files:**
- Create: `src/Square.DevTools/Cdp/CdpSession.cs`
- Create: `src/Square.DevTools/Cdp/CdpCommandDispatcher.cs`
- Test: `tests/Square.DevTools.Tests/CdpSessionTests.cs`

**TDD:**

- [x] RED：`Runtime.enable` 返回相同 `id` 和空 `result`。
- [x] RED：未知 method 返回标准 CDP error，不关闭 session。
- [x] RED：无效 JSON 返回错误或关闭违规连接，但不得终止 server。
- [x] RED：多个并发响应/事件不会交错破坏 WebSocket message。
- [x] GREEN：使用单一异步读取循环和串行写锁。
- [x] GREEN：显式 method switch；禁止反射扫描 handler。
- [x] 验证 server dispose 会关闭活动 session。

---

## Phase C2：只读 Elements Tree

### Task C2.1：建立 Synthetic Document 映射

**Objective:** 将 Square Element Tree 映射成 Chrome 可接受的 DOM Document。

**Files:**
- Create: `src/Square.DevTools/Cdp/CdpDomDomain.cs`
- Test: `tests/Square.DevTools.Tests/CdpDomTests.cs`

**Mapping:**

- [ ] Synthetic Document 使用独立保留 node ID，不与 `Element.DebugId` 冲突。
- [ ] Square Element 使用 `nodeType = 1`。
- [ ] `nodeId` 和 `backendNodeId` 在单次运行内稳定映射到 `DebugId`。
- [ ] `nodeName` 使用大写 TagName，`localName` 使用原始/规范化小写名称。
- [ ] Element 文本映射为 `nodeType = 3` 的合成 Text Node。
- [ ] 第一版展示真实 Element Tree，不插入伪 Shadow Root。
- [ ] ComponentName 使用 `square-component` 诊断属性展示，不改变真实匹配语义。

**TDD:**

- [x] RED：`DOM.getDocument` 返回 Document → Root Element。
- [x] RED：多层 children、childNodeCount 和 parentId 正确。
- [x] RED：同一个 Element 在重复查询中 node ID 稳定。
- [x] RED：文本和特殊字符正确 JSON 转义。
- [x] GREEN：复用现有 `CaptureInspectionSnapshotAsync`，不跨线程直接遍历 Element。

### Task C2.2：实现节点详情和属性查询

**Commands:**

- [x] `DOM.enable`
- [x] `DOM.disable`
- [x] `DOM.getDocument`
- [x] `DOM.requestChildNodes`
- [x] `DOM.describeNode`
- [x] `DOM.getAttributes`
- [x] `DOM.getNodeForLocation`

**Acceptance:**

- [x] `DOM.getNodeForLocation` 复用 Square Hit Test。
- [x] Chrome 示例显式启用文本内容；不改变普通 `/inspect/*` 的 `IncludeTextContent` 默认值。
- [x] 不伪造不存在的 HTML 属性。

### Task C2.3：树变化通知

**Objective:** 第一版用可验证的粗粒度刷新保证树不会永久陈旧。

- [ ] 定义当前窗口 Inspector tree revision。
- [ ] Element Tree 发生结构变化时发送 `DOM.documentUpdated`。
- [ ] 不在每一帧无条件发送更新。
- [ ] 后续再用 `childNodeInserted/Removed` 替换粗粒度刷新。

---

## Phase C3：Box Model 和双向点选

### Task C3.1：定义准确 Box Model 数据

**Objective:** 为 `DOM.getBoxModel` 返回真实 content/padding/border/margin 四边形。

**Files:**
- Modify: `src/Square/Hosting/Inspection/ElementInspection.cs`
- Modify: `src/Square/Hosting/IAppWindowRuntime.cs`
- Modify: `src/Square/Hosting/AppWindow.cs`
- Modify: `src/Square/Hosting/DesktopApplication.cs`
- Potential modify: `src/Square/Rendering/Layout/LayoutEngine.cs`

**Rules:**

- [x] 盒模型快照由 Square 权威布局和 CSS 四向边缘解析产生。
- [x] 禁止把同一个 `Geometry` 复制成 content/padding/border/margin。
- [ ] 对无法证明的数据明确不返回或标记 unsupported。
- [ ] 坐标使用客户区逻辑像素，与现有 Hit Test 一致。

**TDD:**

- [x] RED：有 padding/border/margin 的元素返回不同四层 box。
- [ ] RED：绝对定位和缩放/DPI 下坐标保持一致。
- [x] GREEN：实现真实四层只读 Box Model snapshot。

### Task C3.2：Chrome → Square 元素高亮

**Files:**
- Create: `src/Square.DevTools/Cdp/CdpOverlayDomain.cs`
- Modify: `src/Square/Hosting/DesktopApplication.cs`
- Test: `tests/Square.DevTools.Tests/CdpOverlayTests.cs`

**Commands:**

- [x] `Overlay.enable`
- [x] `Overlay.disable`
- [x] `Overlay.highlightNode`
- [x] `Overlay.hideHighlight`

**Behavior:**

- [x] 按 nodeId 找到当前 Element。
- [x] 在 UI Dispatcher 上更新高亮状态。
- [x] 请求一帧重绘。
- [ ] 使用不同半透明颜色绘制 content/padding/border/margin。
- [ ] 元素删除后自动清除高亮，不保留强引用造成泄漏。

### Task C3.3：Square → Chrome 点选模式

**Behavior:**

- [x] `Overlay.setInspectMode` 开启/关闭点选。
- [x] 点选模式下 pointer move 更新临时高亮。
- [x] pointer down/up 不触发应用普通 Click、拖动或文本选择。
- [x] 点击后执行 Square Hit Test。
- [x] 发送 `Overlay.inspectNodeRequested { backendNodeId }`。
- [ ] Escape 或 disable 退出点选模式。

**Risk:** 输入拦截属于平台输入和 UI 路由交界，必须检查 Win32/X11/macOS 共用路径，禁止只在单个平台 Host 里实现。

---

## Phase C4：Computed 和 Inline Styles

### Task C4.1：computed style 只读快照

**Objective:** 支持 `CSS.getComputedStyleForNode`。

**Files:**
- Create: `src/Square.DevTools/Cdp/CdpCssDomain.cs`
- Modify: runtime inspection records/methods as needed
- Test: `tests/Square.DevTools.Tests/CdpCssTests.cs`

**Rules:**

- [x] 在 UI Dispatcher 上读取 `element.Style.GetAll()`。
- [x] 第一版只返回真实参与计算并可读取的属性。
- [x] 不为缺失属性伪造 CSS initial value。
- [x] 属性名和值保持 Square 的规范格式。
- [ ] CSS 变量解析后的 computed value 与实际布局/绘制读取一致。

### Task C4.2：inline style 只读快照

**Objective:** 支持 `CSS.getInlineStylesForNode`。

- [x] 从 `StyleAccessor.CssText` 和内联声明枚举生成 CDP style。
- [x] 保留 `!important`。
- [x] 生成稳定的诊断 styleSheetId/styleId，但不假装存在外部样式表。
- [ ] `CSS.setStyleTexts`、`DOM.setAttributeValue` 等写命令明确返回 unsupported。

### Task C4.3：Chrome 前端初始化兼容

**Minimal commands/stubs:**

- [x] `CSS.enable/disable`
- [x] `Page.enable/disable`
- [x] `Page.getLayoutMetrics`
- [x] `Page.captureScreenshot`
- [ ] `Page.bringToFront`
- [x] `Runtime.enable/disable`
- [x] `Runtime.releaseObject`
- [x] `Runtime.releaseObjectGroup`

**Rules:**

- [x] `Page.captureScreenshot` 复用 `CaptureRendererBitmapAsync()`。
- [ ] `Runtime.evaluate` 不执行 CLR 代码；根据 Chrome 实际启动调用决定返回 unsupported 或安全常量。
- [ ] 未支持 domain 返回标准 error，不能导致 session 终止。

---

## Phase C5：Sample、文档与真实 Chrome 验证

### Task C5.1：Sample 显式启用

**Files:**
- Modify: `samples/Square.Sample/Program.cs`

**Behavior:**

- [x] `--devtools` 继续只启动原 DevTools。
- [x] `--devtools-chrome-inspect` 只有与 `--devtools` 一起使用时才开启 CDP。
- [x] 控制台输出实际 discovery 地址，例如 `http://127.0.0.1:{port}/json/list`。
- [x] 控制台安全提示该模式允许本机 Chrome 调试连接。

### Task C5.2：文档支持矩阵

**Files:**
- Modify: `docs/DevTools.md`

**Required sections:**

- [x] 启用命令。
- [x] `chrome://inspect/#devices` 配置步骤。
- [x] 已验证 Chrome Stable `151.0.7922.76`。
- [x] Elements/Styles/Overlay/Page/Runtime 支持矩阵。
- [x] unsupported 面板列表。
- [x] token 与 CDP 安全边界差异。
- [x] 动态端口和多实例说明。
- [ ] NativeAOT 支持状态（待本轮 AOT publish + CDP smoke）。
- [x] 常见故障：target 不出现、WebSocket Origin 被拒绝、面板方法不支持。

### Task C5.3：真实 Chrome 互操作验证

- [ ] 启动 Square.Sample 固定测试端口。
- [ ] Chrome `chrome://inspect` 能发现 target。
- [ ] 打开 DevTools 后检查 DevTools 自身 Console，不存在导致 Elements/Styles 不可用的未处理错误。
- [ ] 展开至少三层 Element Tree。
- [ ] 验证 Text、Button、带 padding/border/margin 的 View。
- [x] 验证 Chrome → Square 高亮。
- [x] 验证 Square → Chrome 点选协议事件。
- [ ] 修改 UI 状态后验证 `DOM.documentUpdated`。
- [ ] 截取 Chrome Elements 面板和 Square 高亮画面作为人工验收证据；不把截图替代协议测试。

---

## 5. 延后阶段

## Phase C6：Matched Rules 和源码定位

只有在 CSS 引擎能保留完整 provenance 后进行。

- [ ] 为匹配规则保留 stylesheet ID、selector text、specificity、source span。
- [ ] 为每条声明记录 active/overridden 及获胜原因。
- [ ] 诊断数据仅在明确启用时保留，避免普通运行时内存膨胀。
- [ ] 实现 `CSS.getMatchedStylesForNode`。
- [ ] 实现 `CSS.getStyleSheetText`。
- [ ] 将 `.sqx/.sqv` 和 CSS source span 映射到 Chrome location。
- [ ] 不先实现“看起来像 matched rules”但缺少来源和覆盖关系的假数据。

## Phase C7：细粒度 DOM 事件

- [ ] `DOM.childNodeInserted`
- [ ] `DOM.childNodeRemoved`
- [ ] `DOM.attributeModified`
- [ ] `DOM.characterDataModified`
- [ ] 保持 Chrome 当前选择和展开状态。

## Phase C8：可选样式编辑

- [ ] 单独设计写权限，不自动复用只读 `AllowChromeInspect`。
- [ ] 实现 inline style 编辑，不直接修改 stylesheet 源文件。
- [ ] 编辑必须走 `StyleAccessor`，触发正确 Style/Layout/Paint invalidation。
- [ ] 支持撤销或明确“仅运行时、刷新丢失”。
- [ ] 不实现任意 CLR 属性反射写入。

## Phase C9：内存分析自定义界面

- [ ] 继续使用 Square 自己的内存指标协议。
- [ ] 不实现假的 CDP `HeapProfiler`。
- [ ] 如需在浏览器中显示，使用独立 Square Memory 页面或自定义 DevTools frontend。
- [ ] Chrome Elements CDP 适配层与内存数据模型保持解耦。

---

## 6. 测试策略

### 6.1 单元/协议测试

- discovery JSON schema 和 feature gate。
- WebSocket request ID、response、event 和 error。
- DOM node ID 稳定性。
- Synthetic Document 和 Text Node 映射。
- attributes、特殊字符和空树边界。
- Box Model 数值。
- computed/inline style。
- unsupported method 不会终止连接。
- server dispose 关闭 session。

### 6.2 运行时集成测试

- 使用真实 `AppWindow + FakeRuntime` 验证 server 路由。
- 使用真实 Element Tree 验证 DOM/CSS 映射；避免只测试手写 JSON。
- 使用 `ClientWebSocket` 连接真实 loopback server。
- 验证所有 Element/Style 读取都通过 Dispatcher。
- 验证 Overlay 不对被检查 Element 建立长期强引用。

### 6.3 安全回归

- `AllowChromeInspect=false` 时 `/json/*` 返回 `404`。
- `/api/v1/*` 无 token 始终 `401`。
- 普通网页 Origin 无法建立 CDP WebSocket。
- 非 loopback 监听不被引入。
- malformed/oversized message 不导致进程退出；大小上限根据实际 Chrome 消息测量后确定。

### 6.4 NativeAOT

CDP 使用 WebSocket 和显式 JSON 后必须真实发布和运行，不以 `<IsAotCompatible>true</IsAotCompatible>` 代替验证。

建议验证：

```bash
dotnet publish samples/Square.Sample/Square.Sample.csproj \
  -c Release \
  -r win-x64 \
  -p:SquareSamplePublishAot=true \
  -p:SquareSampleUseDevTools=true \
  -o "$TEMP/hermes-verify-square-cdp-aot"

file "$TEMP/hermes-verify-square-cdp-aot/Square.Sample.exe"
```

运行 AOT 产物后再次执行 `/json/list` 和 WebSocket smoke test。

---

## 7. 最终 ad-hoc verification

Square 当前没有 canonical test command。实施完成后应在系统 TEMP 下创建 `hermes-verify-` 前缀临时脚本，执行后删除，并将结果表述为 focused/ad-hoc verification。

最低验证内容：

```bash
dotnet test tests/Square.DevTools.Tests/Square.DevTools.Tests.csproj
dotnet build src/Square.DevTools/Square.DevTools.csproj -c Release
dotnet build samples/Square.Sample/Square.Sample.csproj -c Release -p:SquareSampleUseDevTools=true
git diff --check
```

此外必须：

- [ ] NativeAOT publish。
- [ ] `file` 检查最终 exe 格式。
- [ ] 运行普通 Release Sample 的真实 CDP smoke test。
- [ ] 运行 AOT Sample 的真实 CDP smoke test。
- [ ] 使用真实 Chrome 完成 Elements/Styles/Overlay 人工闭环。
- [ ] 构建前如遇 DLL 锁，只有在 MSBuild 明确指出锁定 Sample 进程后才终止该进程并重试。
- [ ] 不把 focused/ad-hoc verification 描述为完整 suite green。

---

## 8. 风险与权衡

### Chrome 前端版本漂移

CDP 1.3 名称相对稳定，但 Chrome 自带 frontend 会增加初始化调用。必须记录已验证版本，并对未知 method 返回标准错误；不要通过复制完整 protocol schema 假装支持。

### 安全边界弱于 token API

`chrome://inspect` 不能方便地附加 Square 自定义 token header。CDP 必须显式 opt-in、loopback-only，并诚实说明本机进程可发现 target。

### Inspector 数据开销

computed style 和树快照可按请求生成；matched rules provenance 可能显著增加常驻内存，因此延后并要求显式诊断开关。

### UI 线程阻塞

Chrome 可能高频查询 DOM/CSS。所有运行时读取必须在 Dispatcher 上执行，但应避免一次请求多次重复遍历整棵树。先测量再决定是否引入按 revision 缓存，不能预先增加复杂缓存。

### CDP 与 Square 语义差异

Square Element 不是 HTML DOM。协议适配应只映射可证明的共同语义；Component、Slot、Pseudo Element、CSS rule provenance 和 Shadow DOM 后续单独设计。

### NativeAOT

保持显式命令分发和 JSON 写入；不引入运行时反射序列化、动态代理或依赖 Chromium/V8 的包。

---

## 9. 开放问题

实施前需确认：

- [ ] 第一版验证的 Chrome stable 版本范围。
- [ ] CDP discovery 是否与现有 HTTP API 共用同一端口；当前建议共用，减少生命周期和端口管理复杂度。
- [ ] Chrome DevTools WebSocket 实际发送的 Origin 值及不同 Chrome 版本差异。
- [ ] Box Model 的权威数据应从 LayoutEngine 直接快照，还是从 Element Geometry + computed style 安全推导。
- [ ] Inspector 高亮是否复用现有 diagnostics overlay 绘制入口，还是建立独立 overlay state；当前建议独立状态、共用最终绘制阶段。
- [ ] 点选模式是否只覆盖主窗口；多窗口 target 建议一窗口一 target，后续再设计。
- [ ] `IncludeSourcePaths` 和 `IncludeTextContent` 是否继续约束 CDP 输出；当前建议继续遵守。

---

## 10. 建议交付顺序

1. C0：范围、安全和支持矩阵。
2. C1：discovery + WebSocket。
3. C2：只读 Elements Tree。
4. C3：Box Model + 双向 Overlay。
5. C4：computed/inline Styles + screenshot。
6. C5：Sample、文档、Chrome/NativeAOT 实测。
7. 用户验收后再决定 C6 matched rules、C7 细粒度事件和 C8 样式编辑。

每个代码行为严格按 RED → GREEN → REFACTOR 实施。提交只在用户明确要求时执行；如需提交，使用中文 commit message，并按阶段拆分，避免把协议传输、DOM、Overlay 和 CSS provenance 混成一个大提交。
