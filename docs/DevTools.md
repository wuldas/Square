# DevTools

> Document Revision: 0.8
> 配套：`Getting-Started.md`、`API-Reference.md`、`Rendering.md`

`Square.DevTools` 提供一个只监听 `127.0.0.1` 的 HTTP 调试服务，用于在运行中的 Square 桌面应用上做截图采集、输入自动化、元素检查和低开销内存诊断。它面向本地开发、示例演示、端到端测试和外部调试工具，不参与应用的正常 UI 渲染管线。

---

## 1. 启动服务

应用需要引用 `Square.DevTools`，然后在 `DesktopApplication.Run()` 前调用 `UseDevToolsServer()`。服务会随应用退出自动释放。

```csharp
using Square.Hosting;
using Square.Platform;
using Square.Platform.Win32;
using Square.DevTools;

PlatformRegistry.Register(new Win32PlatformFactory());

var app = new DesktopApplication(new Main(), new PlatformHostCreateInfo
{
    Title = "My App",
    Width = 800,
    Height = 600
});

var devTools = app.UseDevToolsServer(new DevToolsOptions
{
    Port = 0,
    AccessToken = "dev-token",
    AllowInputInjection = true,
    AllowMemoryDiagnostics = true
});

Console.WriteLine($"{devTools.BaseAddress}/api/v1/health");
Console.WriteLine($"{DevToolsServer.TokenHeader}: {devTools.AccessToken}");

app.Run();
```

`DevToolsOptions`：

| 属性 | 默认值 | 说明 |
|---|---:|---|
| `Port` | `0` | `0` 表示由操作系统自动分配空闲端口；`1..65535` 表示严格绑定指定端口 |
| `AccessToken` | `null` | 访问令牌；为空时自动生成 24 字节随机 token 的十六进制字符串 |
| `AllowInputInjection` | `false` | 是否允许 `/input/*` 输入注入接口；关闭后输入接口返回 `403` |
| `AllowInspector` | `false` | 是否允许 `/inspect/*` 运行时检查接口；关闭后返回 `403` |
| `AllowMemoryDiagnostics` | `false` | 是否允许 `/memory` 只读运行时内存快照；关闭后返回 `403` |
| `AllowChromeInspect` | `false` | 是否启用 Chrome DevTools Protocol discovery/WebSocket；关闭后 `/json/*` 返回 `404` |
| `AllowChromeStyleEditing` | `false` | 独立开启运行时样式编辑；要求同时开启 `AllowChromeInspect`，不写回源文件 |
| `AllowChromeConsole` | `false` | 独立开启 framework diagnostics 和进程 managed Console 输出采集；要求同时开启 `AllowChromeInspect` |
| `IncludeSourcePaths` | `false` | Inspector 响应是否包含模板源码路径 |
| `IncludeTextContent` | `false` | Inspector 响应是否包含元素文本内容 |

Chrome 能力取 `DevToolsServer.Start` 时的值，运行中修改 options 不改变权限。`/api/v1/health` 的 `chromeInspect`、`chromeStyleEditing`、`chromeConsole` 返回实际开关；仅开启 Inspector 不授予编辑或日志捕获权限。

RichText 示例已经集成 DevTools：

```bash
dotnet run --project samples/Square.Sample.RichText/Square.Sample.RichText.csproj
```

主示例 `Square.Sample` 通过命令行选项按需启用 DevTools，可与任意后端组合。Vulkan 默认关闭 GPU readback；设置 `SQUARE_VULKAN_READBACK=1` 后，截图才会读取真实 GPU 帧（见 [3. API 概览](#3-api-概览) 的 screenshot 说明）：

```bash
# Software 后端（默认）
dotnet run --project samples/Square.Sample/Square.Sample.csproj -- --devtools

# Vulkan 后端 + DevTools + GPU readback (PowerShell)
$env:SQUARE_VULKAN_READBACK = "1"
dotnet run --project samples/Square.Sample/Square.Sample.csproj -- --backend=Vulkan --devtools
```

`Square.Sample` 支持的 DevTools 相关选项：

| 选项 | 说明 |
|---|---|
| `--devtools` | 启动 DevToolsServer（缺省不启动） |
| `--devtools-port=<port>` | 指定端口；省略时使用自动端口（`Port = 0`） |
| `--devtools-token=<token>` | 指定访问令牌；省略时自动生成随机 token |
| `--devtools-memory` | 显式启用 `/memory` 只读内存诊断；必须与 `--devtools` 一起使用 |
| `--devtools-chrome-inspect` | 显式启用 Chrome Inspector discovery 和 CDP WebSocket；必须与 `--devtools` 一起使用 |
| `--devtools-chrome-edit` | 允许运行时 `element.style` / author-rule 声明编辑；必须同时指定 `--devtools` 和 `--devtools-chrome-inspect` |
| `--devtools-chrome-console` | 采集进程 managed `Console.Out/Error` 和该窗口诊断；必须同时指定 `--devtools` 和 `--devtools-chrome-inspect` |

任一 edit/console flag 缺少依赖时，样例在创建窗口前报错。启动输出列出实际能力；bootstrap 地址、token 和权限警告通过 Start 前保存的 writer 输出，不进入日志历史。Controls 页的普通 activity 按钮点击会输出真实 `Square.Sample activity N`。

启动后控制台会输出实际 base address 和 token header，例如：

```text
http://127.0.0.1:54321
X-Square-DevTools-Token: <启动时生成的随机令牌>
```

### 端口分配规则

1. 库和示例默认使用 `Port = 0`，自动探测 loopback 空闲端口并在绑定冲突时有限重试，允许多个 Square 程序并行启动。
2. 自动端口模式下，调用方必须读取 `DevToolsServer.Port` 或 `DevToolsServer.BaseAddress`，不得假设端口为 `5128`。
3. 只有需要固定外部配置时才使用 `1..65535`。指定端口被占用时启动失败，不自动换到其他端口，避免客户端连接到错误实例。
4. 每个进程默认生成独立随机 token。固定 token 仅用于受控的本地示例或测试。
5. 服务始终只监听 `127.0.0.1`，禁止通过自动端口规则扩大监听范围。

### 自动端口模式

自动端口是常规开发、并行测试和多实例运行的默认选择：

```csharp
var devTools = app.UseDevToolsServer();

Console.WriteLine($"DevTools endpoint: {devTools.BaseAddress}/api/v1");
Console.WriteLine($"{DevToolsServer.TokenHeader}: {devTools.AccessToken}");
```

`UseDevToolsServer()` 和直接 `DevToolsServer.Start()` 均随窗口 `Closed` 停止服务；已关闭窗口不能启动服务。`Dispose()` 只发起取消并停止监听，不阻塞 UI。需要确认所有请求、CDP session 和事件泵已退出时，使用 `await server.DisposeAsync()`。

`DevToolsServer.Start` 返回前，`HttpListener` 已完成监听。此时：

- `devTools.Port` 是实际绑定端口，不会是 `0`。
- `devTools.BaseAddress` 是当前实例的实际根地址。
- 自动端口使用短暂的探测再绑定流程，存在极小的竞争窗口；实现会有限重试，持续冲突时启动失败。

### 固定端口模式

只有外部工具无法接收动态地址，或防火墙/容器映射要求固定端口时才使用固定端口：

```csharp
var devTools = app.UseDevToolsServer(new DevToolsOptions
{
    Port = 5128
});
```

固定端口被占用时，`Start` 会抛出启动异常。调用方不应捕获异常后自动递增端口，否则外部客户端可能继续访问旧端口并连接到错误实例。

### 多实例发现

Square 不提供全局注册表或固定发现端口。应用负责把实例元数据传给需要连接的工具，推荐优先级如下：

1. 父进程直接读取 `DevToolsServer.BaseAddress` 和 `AccessToken`。
2. 应用将地址和 token 输出到结构化日志、测试结果或进程间通信通道。
3. 本地手工调试时输出到控制台。
4. 不推荐通过扫描全部监听端口发现实例。

一个实例至少应暴露以下连接信息：

```json
{
  "processId": 12345,
  "baseAddress": "http://127.0.0.1:54321",
  "tokenHeader": "X-Square-DevTools-Token",
  "accessToken": "<token>"
}
```

不要把 token 写入公共日志、版本库或遥测系统。上面的结构仅表示本地受控进程间传递格式。

---

## 2. 认证与安全边界

所有 endpoint 都必须携带 header：

```text
X-Square-DevTools-Token: <access-token>
```

缺少或错误 token 时返回：

```json
{"error":"unauthorized"}
```

状态码为 `401`。Token 比较使用固定时间比较，降低本地调试场景中的时序侧信道风险。

服务只绑定 `http://127.0.0.1:{Port}`，不监听局域网地址。不要把固定 token 用在生产构建或提交到公开仓库；示例中的固定 token 仅用于本地 demo。

---

## 3. API 概览

所有路径都以 `/api/v1` 为前缀。

| 方法 | 路径 | 说明 |
|---|---|---|
| `GET` | `/health` | 返回服务状态、进程 ID、实际端口、BaseAddress 和功能开关 |
| `GET` | `/memory` | 返回当前进程和 CLR GC 的只读内存快照 |
| `GET` | `/screenshot` | 返回当前 renderer bitmap 的 PNG |
| `POST` | `/input/pointer` | 注入鼠标移动/按下/抬起 |
| `POST` | `/input/key` | 注入键盘按下/抬起 |
| `POST` | `/input/text` | 注入文本输入 |
| `POST` | `/input/wheel` | 注入滚轮 |
| `GET` | `/inspect/tree` | 返回当前元素树快照 |
| `GET` | `/inspect/hit-test?x=&y=` | 返回指定坐标命中的元素 |
| `GET` | `/inspect/elements/{id}` | 返回指定运行时元素详情 |

Inspector endpoint 见 [7. 元素调试与 Inspector](#7-元素调试与-inspector)。

### Chrome Inspector（CDP 子集）

主示例可以显式启用 Chrome Inspector：

```bash
dotnet run --project samples/Square.Sample/Square.Sample.csproj -c Release \
  -p:SquareSampleUseDevTools=true -- --backend Software \
  --devtools --devtools-chrome-inspect
```

启动后控制台会输出 `/json/list` 地址。打开 Chrome：

1. 访问 `chrome://inspect/#devices`。
2. 在 **Configure...** 中添加 `127.0.0.1:<DevToolsServer.Port>`。
3. 在 Remote Target 中点击 **Inspect**。

端口可以使用 `Port = 0` 自动分配；多个 Square 进程应分别使用各自输出的端口配置。`--devtools` 不会自动开启 CDP，必须额外指定 `--devtools-chrome-inspect`。

按需在上述命令末尾加 `--devtools-chrome-edit --devtools-chrome-console`，两个权限独立 opt-in。样式编辑只修改运行时；Console 捕获是进程级 managed 输出，可能包含应用敏感信息。

本轮在 Chrome Stable `155.0.8059.39` 上验证了 CDP 1.3 discovery、WebSocket、原生 Elements 点选/Escape、Styles 运行时 inline/author 编辑和 Console 清空/重连；Computed 与实际 renderer/盒模型一并检查。Chrome frontend 会探测额外的 Debugger/Network/Accessibility 能力，这些能力仍返回 unsupported 或兼容空操作，不代表 Square 已实现对应面板。NativeAOT 使用显式 JSON 和无反射命令分发设计，最终支持状态以实际 AOT publish + CDP smoke 为准。

NativeAOT 默认不包含 DevTools，发布时必须显式保留：

```powershell
dotnet publish samples/Square.Sample/Square.Sample.csproj -c Release -f net10.0 `
  -r win-x64 --self-contained true -p:SquareSamplePublishAot=true `
  -p:SquareSampleUseDevTools=true -o artifacts/aot-devtools-win-x64
& ./artifacts/aot-devtools-win-x64/Square.Sample.exe --backend Software `
  --devtools --devtools-chrome-inspect --devtools-chrome-edit --devtools-chrome-console
```

本轮 `win-x64` NativeAOT 发布 exe 已从原输出目录完成 discovery、DOM 点选/Escape、inline/author 样式编辑、computed tracking、managed Console 清空/重连及活动连接下关闭验收。Windows 的 `HttpListener` 依赖 HTTP.sys；发布/链接成功不代表执行环境已授予访问权限。本机 exe 初始继承 Low 完整性标签，HTTP.sys 被拒绝；经用户授权仅将本次 exe 设为 Medium 后通过，未更改工作区目录或全局安全策略。受保护执行环境应使用授权机制，不通过换目录绕过安全策略。

如果 target 不出现，确认已配置 `127.0.0.1:<port>` 而不是 `/api/v1/health` URL，并检查 `AllowChromeInspect` 和 `--devtools-chrome-inspect`。如果 WebSocket 被拒绝，检查浏览器 Origin 是否为 Chrome DevTools origin；普通网页 Origin 会被拒绝。面板显示 unsupported 方法属于当前 CDP 子集限制。

当前已实现的 CDP 子集：

| Domain | 当前支持 |
|---|---|
| Discovery | `/json/version`、`/json`、`/json/list`、`/json/protocol` |
| `Runtime` | `enable`、`disable`、`getProperties`、`callFunctionOn`、`releaseObject`，以及默认 execution context 事件 |
| `DOM` | `enable`、`disable`、`getDocument`、`getFlattenedDocument`、`requestChildNodes`、`pushNodesByBackendIdsToFrontend`、`describeNode`、`getAttributes`、`getNodeForLocation`、`getBoxModel`、`getOuterHTML`、`resolveNode`；增量 child/attribute/text 事件 |
| `CSS` | computed/matched/inline 读取，`getStyleSheetText`、`setStyleTexts`、ForNode/batch computed tracking 与 stylesheet/computed-style 事件 |
| `Page` | `enable`、`disable`、`getFrameTree`、`getResourceTree`、`getNavigationHistory`、`getLayoutMetrics`、`captureScreenshot` |
| `Target` | `getTargetInfo`、`setDiscoverTargets`、`setAutoAttach` |
| `Overlay` | `enable`、`disable`、`hideHighlight`、`setInspectMode`、`highlightNode`；真实盒模型高亮、`inspectNodeRequested` 和 `inspectModeCanceled` |
| `Log` | `enable`、`disable`、`clear` 与真实 `entryAdded`；独立 opt-in 的 managed/window 日志 |
| 兼容空操作 | `Inspector`、`Console`、`Network` 的部分 enable/disable 和前端探测命令 |
| `Schema` | `getDomains` 返回实际固定的 11-domain 列表，与 `/json/protocol` 共用；metadata 显式描述本子集的 commands/params/returns/events/types |

Square Element Tree 映射为 Document、Element 和合成 Text，保留真实属性及 component 元数据。frontend/backend ID 按 `(DebugId, node kind)` 分别分配，不能把 native DebugId 当作 backend ID；同批次移动保留身份，删除后旧 node/object ID 失效且不复用。`getDocument` 和 `getFlattenedDocument` 隐式开启 DOM，depth 默认 `1`，`-1` 返回全部，其余必须为正整数；`describeNode` 默认 depth `0`。backend 反选先公布祖先，再返回对应 frontend ID。

元素结构、属性和文本通过所属窗口的源级通知合并同步；已展开父节点接收 child inserted/removed，未展开父节点只接收 count，已公布节点接收 attribute/characterData 变化。普通变更不发送 `documentUpdated`，不需要手工 reload；DOM disable 停止这些事件。Styles-only 通知不重建 DOM 快照。computed/inline styles、匹配规则和 content/padding/border/margin 来自真实 Square CSS/布局，不声称实现 V8、Debugger、Network 或 Memory 面板。

点选在应用 pointer/focus/click 分发前消费一次 Down/Up；选中后工具退出，下一次普通点击恢复。原生 Escape 优先取消点选，不进入全局快捷键、popup 或 focused editor。每条 CDP 连接持有独立非零 owner，最后发起 highlight/inspect 的连接接管窗口 overlay；旧 owner 的 hide/disable/断开不会清掉新 owner，接管旧点选会通知其 `inspectModeCanceled`。

高亮用真实 content 和三层非重叠矩形环，沿用 scroll/popup 客户区坐标。无法证明四层盒模型时只描画真实 border bounds。`highlightConfig` 缺失使用默认四色；存在时缺失 layer 为透明，RGB 要求 0–255 整数，alpha 要求 0–1（缺失默认 1）。`searchForNode` 和 `searchForUAShadowDOM` 进入真实点选，`none` 停止；`captureAreaScreenshot` 明确 unsupported。

`AllowChromeStyleEditing=true` 允许编辑运行中的 `element.style` 和已加载 author-rule 的声明：新增、修改、删除、comment 禁用/恢复与 `!important`。仅开启 Inspector 时这些样式仍可读，但写请求返回 `-32000 / "Chrome style editing is disabled."`，不提供可编辑 ID/range。UA 只读；selector/new-rule 和 `setEffectivePropertyValueForNode` 未实现，不自动添加 inline override 代替 winning-rule 语义。

虚拟资源位于 `square://runtime-style/{styleSheetId}`：inline 是声明块本身，rule 是真实 selector group 加声明块，不声称原 `.sqx` 文件位置。`setStyleTexts` 按输入顺序在 staging text 上应用范围，整批先验证 live resource/range 并准备赋值，再提交；任一 unknown/stale/read-only/bad-range 都不部分修改。响应按 edit 顺序保留每步的 metadata。range 使用 UTF-16 column、CRLF 单次换行和 end-exclusive；invalid/disabled 行保留原文及 parsedOk/disabled 状态，不应用。完整声明块替换可修复未闭合 quote/comment。

运行时样式断开后保留、不写回磁盘；应用显式 `CssText` 写回（即使有效值相同）接管 inline 草稿，engine/component 重建接管 rule。共享声明组只影响该 engine，同 AST 的另一个 engine 不变。ForNode 比较真实 computed 值并合并单次 50ms 通知；batch 只监视已公布 live Element，old/new 不同且任一属于配置 value 集合时返回 nodeId。空 `takeComputedStyleUpdates` 登记 pending request，不阻塞后续 DOM/编辑命令；CSS disable/空配置完成等待并停止追踪。

batch computed tracking 在公布新 live Element 的响应或 DOM 事件发送前建立基线；客户端收到新节点后立即触发的样式变化会正常进入待取结果，不会被误当成初始化基线。

编辑沿用 Square 的 CSS registry、布局和绘制语义。已知既有边界：绘制器的命名色支持少于 registry（例如 purple/orange 会回退）；不要据 computed 字符串推断该颜色已绘制，使用已支持的颜色语法并检查实际 renderer。

`AllowChromeConsole=true` 在 server Start 时接入进程 managed `Console.Out/Error` 与该窗口的 framework diagnostics；它可能包含应用敏感输出，默认不捕获。stdout 映射 info，stderr 和实际 custom-element/Hot Reload failure 映射 error，Warning/Verbose 保留相应等级。Chrome 只收到 `Log.entryAdded` 的 source=`other`、level、真实 text 和 Unix 毫秒 timestamp，不伪造 JS args/context/stack，也不重复发送 Runtime/Console 事件。

每个 target 保存最近 256 条历史；`Log.enable` ack 后按序重放并与 live stream 去重，重复 enable 不重复显示。disable ack 后停止该连接的日志，重新 enable 只补仍在 ring 中的未见记录。`Log.clear` 清该 target 并过滤所有连接尚未发送的旧项，不清另一个 server 的历史或原 Console 输出。未开 Console 能力时 Log 域可初始化为空，不接管 writer。

多个 server 共享一个 tee，分别持有 collector；最后一个关闭才恢复仍由 tee 持有的 Out/Error，应用后来自行 `SetOut/SetError` 的替换不被覆盖。原输出/异常保留，CR/LF/CRLF 和 partial Flush 合并，长行按最多 8192 UTF-16 units 无损分段。Stop 立即退订；`DisposeAsync` 在请求/session/event pump 收尾后释放 collector。bootstrap 地址/token/权限警告应由 Start 前保存的 writer 输出，绕过采集，不猜测过滤密码内容。

Chrome Inspector 是显式 opt-in 的本地调试能力。由于 Chrome discovery/WebSocket 连接不能附加 Square 的 `X-Square-DevTools-Token` header，CDP discovery 在开启后不要求该 header；服务仍只监听 `127.0.0.1`，并限制 WebSocket Origin。不要在不受信任的机器或生产构建中开启 `AllowChromeInspect`。

### GET /api/v1/health

返回 JSON：

```json
{
  "status": "ok",
  "processId": 12345,
  "port": 54321,
  "baseAddress": "http://127.0.0.1:54321",
  "inputInjection": true,
  "memoryDiagnostics": true,
  "chromeInspect": true,
  "chromeStyleEditing": false,
  "chromeConsole": false
}
```

示例：

```bash
curl -H "X-Square-DevTools-Token: $TOKEN" \
  http://127.0.0.1:<port>/api/v1/health
```

### GET /api/v1/memory

当 `AllowMemoryDiagnostics=true` 时返回当前进程和 CLR GC 的一次性只读快照。采集不调用 `GC.Collect()`，不生成 dump，不保存服务端历史，也不启动后台采样线程。响应字段按顺序采集，并不是同一原子时刻的进程快照；`sampledAtUnixMilliseconds` 表示这次采集的大致时间。

```bash
curl -H "X-Square-DevTools-Token: $TOKEN" \
  http://127.0.0.1:<port>/api/v1/memory
```

响应示例：

```json
{
  "processId": 12345,
  "sampledAtUnixMilliseconds": 1786464000000,
  "process": {
    "workingSetBytes": 183500800,
    "privateMemoryBytes": 146800640,
    "virtualMemoryBytes": 2147483648
  },
  "managed": {
    "currentBytes": 35651584,
    "approximateTotalAllocatedBytes": 982347112,
    "heapSizeAfterLastGcBytes": 33554432,
    "fragmentedAfterLastGcBytes": 1048576,
    "totalCommittedBytes": 50331648,
    "totalAvailableMemoryBytes": 8589934592,
    "memoryLoadBytes": 4294967296,
    "highMemoryLoadThresholdBytes": 7730941132,
    "pendingFinalizers": 0,
    "pinnedObjects": 12,
    "pauseTimePercentage": 1.25
  },
  "collections": {
    "gen0": 245,
    "gen1": 37,
    "gen2": 5
  }
}
```

字段语义：

| 字段 | 说明 |
|---|---|
| `process.workingSetBytes` | 操作系统当前分配给进程的物理内存工作集 |
| `process.privateMemoryBytes` | 进程提交的私有内存；不等同于 Native/GPU 内存；部分平台可能返回 `0` 表示该值不可用 |
| `process.virtualMemoryBytes` | 进程虚拟地址空间大小；不能当作实际 RAM 占用，具体统计口径遵循 .NET 和当前操作系统 |
| `managed.currentBytes` | `GC.GetTotalMemory(false)` 的当前托管内存估算，不触发回收 |
| `managed.approximateTotalAllocatedBytes` | 进程启动以来的快速近似累计托管分配量，可用于观察趋势，但可能滞后，不能替代 profiler 的精确 allocation rate |
| `managed.heapSizeAfterLastGcBytes` | 最近一次 GC 完成后的托管堆大小，不是请求瞬间的精确堆大小 |
| `managed.fragmentedAfterLastGcBytes` | 最近一次 GC 后的托管堆碎片 |
| `managed.totalCommittedBytes` | GC 已提交的虚拟内存 |
| `managed.totalAvailableMemoryBytes` | GC 认为当前进程可使用的内存上限 |
| `managed.memoryLoadBytes` | 最近一次 GC 时观察到的机器内存负载 |
| `managed.highMemoryLoadThresholdBytes` | GC 使用的高内存负载阈值 |
| `managed.pendingFinalizers` | 最近一次 GC 信息中的待终结对象数 |
| `managed.pinnedObjects` | 最近一次 GC 信息中的 pinned object 数 |
| `managed.pauseTimePercentage` | GC 暂停占运行时间的百分比 |
| `collections.gen0/gen1/gen2` | 当前进程各代累计 GC 次数 |

客户端可以定时轮询，并通过两次响应的 `approximateTotalAllocatedBytes` 与 `sampledAtUnixMilliseconds` 差值观察近似分配速率趋势。该值可能滞后，且轮询和 JSON 响应本身也会产生少量分配，因此不能用于精确基准，也不应使用过高采样频率；需要精确 allocation rate 或 allocation stack 时应改用专用 profiler。服务端不维护时间序列，避免 DevTools 自身产生持续内存占用。

不能使用下面的差值推断 Native 或 GPU 内存：

```text
process.privateMemoryBytes - managed.currentBytes
```

该差值还包含 CLR、线程栈、模块映像、JIT/AOT、内存池、映射文件以及其他进程私有内存。需要对象引用链、GC Root、分配调用栈或 Native 内存来源时，应继续使用 dotMemory、PerfView、`dotnet-gcdump`、`dotnet-dump` 或 VMMap。

仓库提供 `tools/Memory/Invoke-SquareMemoryProfile.ps1` 包装 PerfView。脚本按顺序查找
`-PerfViewPath`、`PERFVIEW_PATH`、`C:\quick\PerfView\PerfView.exe` 和 `PATH`，产物默认写入
`artifacts/perfview/`：

```powershell
# 运行真实 DirectWrite/Direct2D 内存压力用例并采集 sampled allocation stacks
.\tools\Memory\Invoke-SquareMemoryProfile.ps1 -Mode Allocation

# 对已运行进程抓托管堆或 native OS heap
.\tools\Memory\Invoke-SquareMemoryProfile.ps1 -Mode HeapSnapshot -ProcessId 1234
.\tools\Memory\Invoke-SquareMemoryProfile.ps1 -Mode NativeHeap -ProcessId 1234 -DurationSeconds 30
```

`HeapSnapshot` 用于托管保留路径，`Allocation` 用于分配调用栈，`NativeHeap` 用于 DirectWrite、
Direct2D 和 COM/native heap；它们不能互相替代。采集 ETW/native heap 通常需要管理员权限。

`process` 下的指标直接来自 `System.Diagnostics.Process`。不同操作系统的统计口径并不完全一致；特别是值为 `0` 时，调用方需要将其视为“可能不可用”，不能直接得出实际内存为零的结论。

### GET /api/v1/screenshot

将当前保留的 DisplayTree 在进程内离屏重放为位图并返回 PNG，文件名为 `square-screenshot.png`。

```bash
curl -H "X-Square-DevTools-Token: $TOKEN" \
  -o screenshot.png \
  http://127.0.0.1:<port>/api/v1/screenshot
```

截图不是平台窗口截图：它不按 PID 枚举窗口、不依赖桌面合成器，也不包含标题栏或窗口边框。该路径适合 UI 回归、自动化和采集控件状态。

截图来源取决于活动渲染后端的能力：

- **GPU 实时帧回读（优先）**：当活动 RenderContext 实现 `IRenderBitmapSource` 且 `IsCaptureAvailable` 为 `true` 时，`CaptureRendererBitmapAsync()` 直接读回最近一帧真实呈现的 GPU 图像。这使截图反映真实 GPU 输出，GPU 侧的渲染 bug（例如 render pass 被丢弃导致的白屏）会直接暴露在截图中，而不会被软件重渲染掩盖。Vulkan 需设置 `SQUARE_VULKAN_READBACK=1`；启用后在帧内把 swapchain 颜色附件 copy 到 host-visible buffer，swapchain 格式 B8G8R8A8 与 `Bitmap` 的 BGRA 布局一致，无需通道交换。
- **软件重渲染（回退）**：当活动后端不提供实时帧时，在 UI 线程创建离屏 Software RenderContext，重放与活动后端相同的 DisplayTree 命令、文本选择和诊断覆盖层。它支持形状、Path、Bitmap、文本、渐变、透明层和 Geometry clip；不同渲染路径的抗锯齿仍可能产生像素差异。

调试 DPI、文字选择或后端差异时，应优先采用同一组输入注入分别采集 Software 与 Vulkan GPU readback 帧。这样可以区分布局/选择区逻辑问题和具体后端的 glyph 定位、coverage 或混合问题。

### POST /api/v1/input/pointer

请求体：

```json
{
  "x": 40,
  "y": 32,
  "action": "Down",
  "modifiers": ["Shift"]
}
```

字段：

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `x` | number | 是 | 客户区 X 坐标 |
| `y` | number | 是 | 客户区 Y 坐标 |
| `action` | string | 是 | `Down`、`Up`、`Move` |
| `modifiers` | string[] | 否 | `Shift`、`Control`、`Alt`，省略表示 `None` |

成功返回 `204 No Content`。

```bash
curl -X POST -H "X-Square-DevTools-Token: $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"x\":40,\"y\":32,\"action\":\"Down\"}" \
  http://127.0.0.1:<port>/api/v1/input/pointer
```

### POST /api/v1/input/key

请求体：

```json
{
  "keyCode": 65,
  "action": "Down",
  "modifiers": ["Control"]
}
```

字段：

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `keyCode` | integer | 是 | 平台键码；字母键常用 ASCII/虚拟键码，例如 `65` 表示 A |
| `action` | string | 是 | `Down` 或 `Up` |
| `modifiers` | string[] | 否 | `Shift`、`Control`、`Alt` |

成功返回 `204 No Content`。

### POST /api/v1/input/text

请求体：

```json
{
  "text": "hello 中文"
}
```

字段：

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `text` | string | 是 | 注入到当前焦点文本编辑器的文本 |

成功返回 `204 No Content`。该接口走文本输入路径，适合输入 Unicode 文本；快捷键请使用 `/input/key`。

### POST /api/v1/input/wheel

请求体：

```json
{
  "x": 120,
  "y": 180,
  "delta": -120,
  "modifiers": []
}
```

字段：

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `x` | number | 是 | 客户区 X 坐标 |
| `y` | number | 是 | 客户区 Y 坐标 |
| `delta` | integer | 是 | 滚轮增量；正负方向沿用平台输入语义 |
| `modifiers` | string[] | 否 | `Shift`、`Control`、`Alt` |

成功返回 `204 No Content`。

---

## 4. 错误响应

| 状态码 | 场景 |
|---:|---|
| `400` | JSON 无效、字段缺失、字段类型错误或枚举值不支持 |
| `401` | 缺少或错误 `X-Square-DevTools-Token` |
| `403` | `AllowInputInjection=false` 时调用 `/input/*` |
| `403` | `AllowInspector=false` 时调用 `/inspect/*` |
| `403` | `AllowMemoryDiagnostics=false` 时调用 `/memory` |
| `500` | 截图、输入注入、Inspector 或内存快照期间出现未处理异常 |

DevTools 启动阶段的端口冲突不会转换为 HTTP 状态码，因为此时服务尚未启动；`DevToolsServer.Start` 会直接抛出异常。

输入 JSON 使用 camelCase 字段名。枚举值解析不区分大小写。

---

## 5. 运行模型

DevTools HTTP 请求由仅绑定 loopback 的 `HttpListener` 处理，不依赖 ASP.NET Core。输入注入不会直接跨线程操作 UI；`DevToolsServer` 会调用 `DesktopApplication.InjectPointerAsync`、`InjectKeyAsync`、`InjectTextAsync` 和 `InjectWheelAsync`，再通过 `Dispatcher.InvokeAsync` 投递到 UI 线程。

`Square.DevTools` 支持 NativeAOT。服务使用显式路由和 JSON 读写，包括内存快照响应在内都不依赖运行时 endpoint 发现、反射序列化元数据或动态代码生成。主示例 AOT 发布后仍可使用 `--devtools`：

```powershell
dotnet publish samples/Square.Sample/Square.Sample.csproj `
  -c Release `
  -r win-x64 `
  -p:SquareSamplePublishAot=true `
  -p:SquareSampleUseVulkan=true `
  -p:SquareSampleUseDevTools=true `
  -o artifacts/aot-vulkan-win-x64

artifacts/aot-vulkan-win-x64/Square.Sample.exe --backend Vulkan --devtools
```

`Square.Sample` 的 AOT 发布默认不引用 `Square.DevTools`。只有 `SquareSampleUseDevTools=true` 时才添加项目引用和 `app.UseDevToolsServer()` 调用路径；普通应用不引用 `Square.DevTools` 即可从发布产物中完全移除 DevTools 服务。

截图通过 `DesktopApplication.CaptureRendererBitmapAsync()` 获取，优先读取活动渲染上下文的实时帧：若 `_renderContext` 实现 `IRenderBitmapSource` 且 `IsCaptureAvailable` 为 `true`，直接 `CaptureBitmap()` 读回真实 GPU 输出；否则在 UI 线程创建离屏 Software RenderContext，重放当前 DisplayTree、文本选择和诊断覆盖层。因此 Software 与 Vulkan 都能使用同一截图 API；Vulkan 只有在设置 `SQUARE_VULKAN_READBACK=1` 后才捕获真实 GPU 帧，默认使用软件重放以降低内存和拷贝成本。

输入注入后的行为与平台输入路径一致：鼠标命中测试、焦点、文本编辑器、键盘快捷键、滚轮路由和必要的重绘都会由 `DesktopApplication` 统一处理。

---

## 6. 自动化示例

下面示例点击 RichText 编辑器左上角并输入文本：

```bash
TOKEN=square-richtext-demo
BASE=http://127.0.0.1:<port>/api/v1

curl -X POST -H "X-Square-DevTools-Token: $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"x":45,"y":230,"action":"Down"}' \
  "$BASE/input/pointer"

curl -X POST -H "X-Square-DevTools-Token: $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"x":45,"y":230,"action":"Up"}' \
  "$BASE/input/pointer"

curl -X POST -H "X-Square-DevTools-Token: $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"text":"Hello from DevTools"}' \
  "$BASE/input/text"

curl -H "X-Square-DevTools-Token: $TOKEN" \
  -o after-input.png \
  "$BASE/screenshot"
```

在 Windows PowerShell 中可使用等价变量：

```powershell
$token = "square-richtext-demo"
$base = "http://127.0.0.1:<port>/api/v1"
$headers = @{ "X-Square-DevTools-Token" = $token }

Invoke-RestMethod -Method Post -Headers $headers -ContentType "application/json" `
  -Body '{"x":45,"y":230,"action":"Down"}' "$base/input/pointer"
Invoke-RestMethod -Method Post -Headers $headers -ContentType "application/json" `
  -Body '{"x":45,"y":230,"action":"Up"}' "$base/input/pointer"
Invoke-RestMethod -Method Post -Headers $headers -ContentType "application/json" `
  -Body '{"text":"Hello from DevTools"}' "$base/input/text"
Invoke-WebRequest -Headers $headers -OutFile "after-input.png" "$base/screenshot"
```

---

## 7. 元素调试与 Inspector

DevTools 提供运行时 Inspector：通过坐标、元素 ID 或树查询定位 Square 元素，并返回模板源码位置、布局盒、样式、状态和绘制信息。该能力用于 IDE 跳转、可视化检查、端到端测试失败诊断和外部调试工具。

### 7.1 总体目标

Inspector 不应只暴露截图，也不应只暴露 DisplayTree。它需要把运行时对象和模板源文件连起来：

```text
.sqx / .sqv source
  -> Parser AST SourceSpan
  -> Source Generator emits ElementDebugInfo
  -> Element.DebugInfo
  -> LayoutBox / DisplayNode keeps Element reference or debug id
  -> DevTools hit test / query
  -> source location + runtime state
```

核心原则：

1. **源码位置由 Source Generator 注入**：不要依赖 C# caller info，因为 caller info 会指向 `.g.cs`，不是 `.sqx` / `.sqv`。
2. **权威调试信息挂在 Element 上**：Rendering 只传递引用或 debug id，不作为源码信息的唯一来源。
3. **DevTools 只读为主**：Inspector 默认不修改 Element Tree；未来需要样式热调试时再单独设计写入权限。
4. **Debug 信息可裁剪**：Release / NativeAOT 发布可以通过构建属性关闭详细 source path 或完全关闭 Inspector metadata。

### 7.2 编译期数据：SourceSpan 与 ElementDebugInfo

`Square.Markup` 的 AST 节点应保留模板位置：

```csharp
public readonly record struct SourceSpan(
    string FilePath,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);
```

`Square.Compiler` 在生成元素创建代码时注入调试信息：

```csharp
var element = new Button();
element.SetDebugInfo(ElementDebugInfo.Create(
    sourceId: 3,
    startLine: 12,
    startColumn: 5,
    endLine: 18,
    endColumn: 12,
    tagName: "Button",
    componentName: "Main",
    kind: ElementGeneratedKind.TemplateNode));
```

`sourceId` 推荐指向组件级 source table，避免每个元素重复存完整路径字符串：

```csharp
private static readonly DebugSourceFile[] __SquareDebugSources =
[
    new(3, "Components/Main.sqv")
];
```

### 7.3 运行时数据：Element 上的 DebugInfo

`Square.UI` 负责定义轻量 metadata：

```csharp
public sealed class ElementDebugInfo
{
    public int SourceId { get; init; }
    public int StartLine { get; init; }
    public int StartColumn { get; init; }
    public int EndLine { get; init; }
    public int EndColumn { get; init; }
    public string? TagName { get; init; }
    public string? ComponentName { get; init; }
    public ElementGeneratedKind Kind { get; init; }
}

public enum ElementGeneratedKind
{
    TemplateNode,
    ComponentRoot,
    SlotContent,
    ForItem,
    ConditionalBranch,
    GeneratedWrapper
}
```

Element 暴露只读调试入口：

```csharp
public ElementDebugInfo? DebugInfo { get; }
```

设置入口应限制在框架/生成代码可用范围，例如 `internal set`、`SetDebugInfo(...)` 或 source-generator-only helper，避免普通应用逻辑随意篡改源码位置。

### 7.4 Layout / DisplayTree 反查

DevTools 的坐标点选需要从屏幕坐标回到 Element：

```text
client point
  -> latest layout root / display tree
  -> deepest hit LayoutBox or DisplayNode
  -> source Element
  -> Element.DebugInfo
```

建议 Rendering 层保留：

| 数据 | 用途 |
|---|---|
| `LayoutBox.Element` | 布局命中、盒模型检查、尺寸定位 |
| `DisplayNode.Element` 或 `DebugElementId` | 绘制命中、截图叠加、高亮 |
| `Element.DebugId` | DevTools 返回稳定引用，后续查询详情 |

`DebugId` 只要求在单次运行期间稳定，不要求跨进程或跨构建稳定。跨构建跳转应依赖 `SourceSpan`。

### 7.5 计划 endpoint

后续 Inspector endpoint 建议挂在 `/api/v1/inspect/*` 下：

| 方法 | 路径 | 说明 |
|---|---|---|
| `GET` | `/inspect/tree` | 返回当前 Element / Layout 调试树摘要 |
| `GET` | `/inspect/hit-test?x=120&y=80` | 返回指定客户区坐标命中的最深元素 |
| `GET` | `/inspect/elements/{id}` | 返回元素详情 |
| `GET` | `/inspect/elements/{id}/styles` | 返回 computed style、matched rules 和 inline style |
| `GET` | `/inspect/elements/{id}/layout` | 返回 content/padding/border/margin box 与 flex/grid 信息 |
| `GET` | `/inspect/elements/{id}/source` | 返回模板源码位置 |
| `GET` | `/inspect/snapshot` | 返回 tree + layout + selected display info 的一次性快照 |

`/inspect/hit-test` 示例响应：

```json
{
  "elementId": 42,
  "tagName": "Button",
  "componentName": "Main",
  "bounds": { "x": 24, "y": 96, "width": 128, "height": 36 },
  "source": {
    "file": "Components/Main.sqv",
    "startLine": 12,
    "startColumn": 5,
    "endLine": 18,
    "endColumn": 12
  },
  "state": {
    "hover": true,
    "focus": false,
    "active": false,
    "disabled": false
  }
}
```

`/inspect/tree` 应默认返回摘要，避免大型 UI 一次性输出过多数据：

```json
{
  "root": {
    "id": 1,
    "tagName": "View",
    "componentName": "Main",
    "bounds": { "x": 0, "y": 0, "width": 800, "height": 600 },
    "children": [
      { "id": 2, "tagName": "Text", "text": "Hello", "childCount": 0 }
    ]
  }
}
```

### 7.6 IDE 跳转协议

DevTools 只返回源码位置，不直接假设 IDE。外部工具可以按响应中的 source location 调用 IDE：

```text
file: Components/Main.sqv
line: 12
column: 5
```

后续可以补充可选 endpoint：

| 方法 | 路径 | 说明 |
|---|---|---|
| `POST` | `/inspect/open-source` | 由本地开发工具注册 handler 后打开源码 |

该 endpoint 不应默认启用，避免 DevTools 服务直接执行外部命令。默认安全模型应保持“返回数据，由调用方决定如何打开 IDE”。

### 7.7 安全与隐私

Inspector 会暴露源码路径、组件名、文本内容和样式信息，因此需要比截图/输入更明确的开关：

```csharp
public sealed class DevToolsOptions
{
    public bool AllowInspector { get; set; }
    public bool IncludeSourcePaths { get; set; }
    public bool IncludeTextContent { get; set; }
}
```

建议默认策略：

| 构建 | `AllowInspector` | `IncludeSourcePaths` | 说明 |
|---|---:|---:|---|
| Debug | true | true | 本地开发默认可用 |
| Release | false | false | 除非显式打开 |
| NativeAOT publish | false | false | 避免泄露路径并减少 metadata |

即使 Inspector 启用，服务仍只监听 `127.0.0.1`，并继续要求 `X-Square-DevTools-Token`。

### 7.8 分阶段实现

| 阶段 | 内容 | 退出标准 |
|---|---|---|
| D0 | 文档计划与命名稳定 | DevTools 文档明确 Inspector 数据流和 endpoint 草案 |
| D1 | `SourceSpan` 贯通 Parser / AST | `.sqx` / `.sqv` AST 节点保留准确行列 |
| D2 | Generator 注入 `ElementDebugInfo` | 生成的元素可回溯到模板源位置 |
| D3 | `Element.DebugId` 与 runtime registry | DevTools 可通过 ID 查询当前运行时元素摘要 |
| D4 | Layout / DisplayTree hit test | `/inspect/hit-test` 可从坐标返回元素与源码位置 |
| D5 | Tree / element detail endpoint | `/inspect/tree`、`/inspect/elements/{id}` 可用 |
| D6 | Style / layout diagnostics | 可查看 computed style、matched rules、box model |
| D7 | IDE 集成 | 外部工具可基于 DevTools 响应跳转源码 |

CDP 的元素增量同步、真实盒模型、运行时声明编辑和 managed Console 转发已接通，详见 [Chrome Inspector（CDP 子集）](#chrome-inspectorcdp-子集)。本节 HTTP / IDE endpoint 表仍是路线图；IDE 打开和磁盘源码热编辑不在当前实现范围。
