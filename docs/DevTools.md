# DevTools

> Document Revision: 0.5
> 配套：`Getting-Started.md`、`API-Reference.md`、`Rendering.md`

`Square.DevTools` 提供一个只监听 `127.0.0.1` 的 HTTP 调试服务，用于在运行中的 Square 桌面应用上做截图采集和输入自动化。它面向本地开发、示例演示、端到端测试和外部调试工具，不参与应用的正常 UI 渲染管线。

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
    AllowInputInjection = true
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
| `IncludeSourcePaths` | `false` | Inspector 响应是否包含模板源码路径 |
| `IncludeTextContent` | `false` | Inspector 响应是否包含元素文本内容 |

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

`UseDevToolsServer()` 会在应用退出时自动释放服务。需要自行控制生命周期时仍可直接调用 `DevToolsServer.Start()` 并手工释放。

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
| `GET` | `/health` | 返回服务状态、进程 ID、实际端口、BaseAddress 和输入注入开关 |
| `GET` | `/screenshot` | 返回当前 renderer bitmap 的 PNG |
| `POST` | `/input/pointer` | 注入鼠标移动/按下/抬起 |
| `POST` | `/input/key` | 注入键盘按下/抬起 |
| `POST` | `/input/text` | 注入文本输入 |
| `POST` | `/input/wheel` | 注入滚轮 |
| `GET` | `/inspect/tree` | 返回当前元素树快照 |
| `GET` | `/inspect/hit-test?x=&y=` | 返回指定坐标命中的元素 |
| `GET` | `/inspect/elements/{id}` | 返回指定运行时元素详情 |

Inspector endpoint 见 [7. 元素调试与 Inspector](#7-元素调试与-inspector)。

### GET /api/v1/health

返回 JSON：

```json
{
  "status": "ok",
  "processId": 12345,
  "port": 54321,
  "baseAddress": "http://127.0.0.1:54321",
  "inputInjection": true
}
```

示例：

```bash
curl -H "X-Square-DevTools-Token: $TOKEN" \
  http://127.0.0.1:<port>/api/v1/health
```

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
| `500` | 截图或输入注入期间出现未处理异常 |

DevTools 启动阶段的端口冲突不会转换为 HTTP 状态码，因为此时服务尚未启动；`DevToolsServer.Start` 会直接抛出异常。

输入 JSON 使用 camelCase 字段名。枚举值解析不区分大小写。

---

## 5. 运行模型

DevTools HTTP 请求由仅绑定 loopback 的 `HttpListener` 处理，不依赖 ASP.NET Core。输入注入不会直接跨线程操作 UI；`DevToolsServer` 会调用 `DesktopApplication.InjectPointerAsync`、`InjectKeyAsync`、`InjectTextAsync` 和 `InjectWheelAsync`，再通过 `Dispatcher.InvokeAsync` 投递到 UI 线程。

`Square.DevTools` 支持 NativeAOT。服务使用显式路由和 JSON 读写，不依赖运行时 endpoint 发现、反射序列化元数据或动态代码生成。主示例 AOT 发布后仍可使用 `--devtools`：

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

优先级建议：先完成 D1-D4，形成“点选 UI -> 定位 `.sqx/.sqv` 源码”的闭环；样式规则解释、IDE 打开、热编辑可以后置。
