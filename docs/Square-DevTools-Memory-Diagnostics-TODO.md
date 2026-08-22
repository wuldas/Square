# Square.DevTools 运行时内存诊断实施 TODO

> Document Revision: 0.1

## 1. 目标

为 `Square.DevTools` 增加低开销、只读、显式启用的运行时内存诊断接口，让外部工具能够轮询当前进程和 CLR GC 指标，判断问题属于托管堆增长、高分配率、碎片、频繁 GC，还是托管堆之外的进程内存增长。

首个实现切片只提供瞬时快照，不启动后台采样线程，不保存历史，不触发强制 GC，不生成 dump，也不尝试替代 dotMemory、PerfView、`dotnet-gcdump` 或 `dotnet-dump`。

### 当前状态

第一阶段已实现：`AllowMemoryDiagnostics`、`GET /api/v1/memory`、health capability、主示例显式 CLI 开关、文档、测试以及 Release/NativeAOT 验证均已完成。M4 及以后阶段仍保持计划状态。

## 2. 第一阶段成功标准

- [x] 新增 `DevToolsOptions.AllowMemoryDiagnostics`，默认 `false`。
- [x] 新增 `GET /api/v1/memory`。
- [x] 所有内存接口继续要求 `X-Square-DevTools-Token`。
- [x] 功能关闭时，已认证请求返回 `403 Forbidden`。
- [x] `/api/v1/health` 报告 `memoryDiagnostics` 开关状态。
- [x] 快照包含进程 Working Set、Private Memory 和 Virtual Memory。
- [x] 快照包含当前托管内存估算、累计分配量和最近一次 GC 后的堆/碎片/提交指标。
- [x] 快照包含 Gen 0、Gen 1、Gen 2 累计回收次数。
- [x] 快照包含 finalizer、pinned object 和 GC pause 百分比指标。
- [x] 响应使用显式 JSON 写入，不依赖反射序列化元数据，保持 NativeAOT 兼容。
- [x] 采集过程不调用 `GC.Collect()`。
- [x] 服务端不维护时间序列；调用方根据累计分配量和采样时间计算速率。

## 3. API 草案

### 3.1 配置

```csharp
var devTools = window.UseDevToolsServer(new DevToolsOptions
{
    AllowMemoryDiagnostics = true
});
```

默认关闭：

```csharp
public bool AllowMemoryDiagnostics { get; set; }
```

### 3.2 Endpoint

```text
GET /api/v1/memory
X-Square-DevTools-Token: <access-token>
```

成功响应：

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

### 3.3 字段语义

| 字段 | 语义 |
|---|---|
| `workingSetBytes` | 操作系统当前分配给进程的物理内存工作集 |
| `privateMemoryBytes` | 进程提交的私有内存；不等同于 Native/GPU 内存；部分平台可能返回 `0` 表示不可用 |
| `virtualMemoryBytes` | 进程虚拟地址空间大小；不能当作实际 RAM 占用，统计口径遵循当前操作系统 |
| `currentBytes` | `GC.GetTotalMemory(false)` 返回的当前托管内存估算，不触发回收 |
| `approximateTotalAllocatedBytes` | 进程启动以来的快速近似累计托管分配量，可用于观察分配趋势，但可能滞后 |
| `heapSizeAfterLastGcBytes` | 最近一次 GC 完成后的托管堆大小，不是请求瞬间的精确堆大小 |
| `fragmentedAfterLastGcBytes` | 最近一次 GC 后的托管堆碎片 |
| `totalCommittedBytes` | GC 已提交的虚拟内存 |
| `totalAvailableMemoryBytes` | GC 认为可用于进程的内存上限 |
| `memoryLoadBytes` | 最近一次 GC 时的机器内存负载 |
| `highMemoryLoadThresholdBytes` | GC 使用的高内存负载阈值 |
| `pendingFinalizers` | 最近一次 GC 信息中的待终结对象数量 |
| `pinnedObjects` | 最近一次 GC 信息中的 pinned object 数量 |
| `pauseTimePercentage` | GC 暂停占运行时间的百分比 |
| `collections.gen*` | 当前进程各代累计 GC 次数 |

响应中的进程和 GC 字段按顺序读取，并非同一原子时刻的快照；`sampledAtUnixMilliseconds` 只表示该次采集的大致时间。

必须在文档中强调：

```text
privateMemoryBytes - currentBytes != Native/GPU memory
```

该差值还包含 CLR、线程栈、模块映像、JIT/AOT、内存池、映射文件和其他进程私有内存。

## 4. 第一阶段明确不做

- [ ] 不调用 `GC.Collect()`。
- [ ] 不提供 `/memory/collect`。
- [ ] 不生成 gcdump/core dump。
- [ ] 不枚举所有 CLR 对象或类型实例数。
- [ ] 不分析 GC Root、对象引用链或 allocation stack。
- [ ] 不引入 `Microsoft.Diagnostics.NETCore.Client`。
- [ ] 不引入 `ObjectLayoutInspector`。
- [ ] 不在服务端启动定时器或后台采样循环。
- [ ] 不用“进程内存减托管堆”估算 Native/GPU 内存。
- [ ] 不在本阶段修改 Square Runtime/UI/Rendering 公共 API。

## 5. 文件范围

### 新增

- `src/Square.DevTools/Memory/MemorySnapshot.cs`
  - 内部只读快照模型和采集逻辑。
### 修改

- `src/Square.DevTools/DevToolsOptions.cs`
  - 添加 `AllowMemoryDiagnostics`。
- `src/Square.DevTools/DevToolsServer.cs`
  - 添加 `/api/v1/memory` 路由和显式 JSON 序列化。
  - health 响应增加 `memoryDiagnostics`。
- `tests/Square.DevTools.Tests/DevToolsServerTests.cs`
  - 更新 health 默认值、feature gate 矩阵和内存快照协议测试。
- `samples/Square.Sample/Program.cs`
  - 添加显式 `--devtools-memory` 开关，便于真实 Release/NativeAOT smoke test。
- `docs/DevTools.md`
  - 更新配置、API、字段语义、安全边界和限制。

## 6. TDD 实施步骤

### M1：Feature gate 和 health capability

- [x] RED：health 响应预期 `memoryDiagnostics=false`。
- [x] RED：已认证请求 `/api/v1/memory` 在默认配置下返回 `403`。
- [x] GREEN：添加 option、health 字段和路由 gate。
- [x] 验证现有 `/input/*`、`/inspect/*` gate 行为不变。

### M2：内存快照协议

- [x] RED：启用开关后 `/api/v1/memory` 返回 `200` 和完整 JSON 结构。
- [x] RED：`processId` 等于当前进程 ID。
- [x] RED：字节数、GC 次数和计数值不为负数。
- [x] RED：`sampledAtUnixMilliseconds` 为有效 Unix 毫秒值。
- [x] GREEN：使用 `Process.GetCurrentProcess()`、`GC.GetTotalMemory(false)`、`GC.GetTotalAllocatedBytes(false)`、`GC.GetGCMemoryInfo()` 和 `GC.CollectionCount()` 采集。
- [x] GREEN：使用显式 `StringBuilder` JSON 写入。
- [x] REFACTOR：采集逻辑与 HTTP 路由分离，避免继续膨胀 `DevToolsServer`。

### M3：文档与示例

- [x] `DevToolsOptions` 表增加开关。
- [x] API 概览增加 `/memory`。
- [x] health 示例增加 `memoryDiagnostics`。
- [x] 增加完整响应示例和字段说明。
- [x] 明确调用方轮询和计算 allocation rate 的方式。
- [x] 明确指标不等同于堆快照或泄漏结论。

## 7. 后续阶段

### M4：Square 框架专项指标

在不建立强引用全局对象表的前提下，评估：

- 当前挂载 Element 数量及最大深度。
- Layout/Display Tree 节点数量。
- 当前绘制命令数量。
- TextLayout、字体、Glyph 和 Bitmap 缓存条目数。
- 能可靠计算的 Bitmap/缓存估算字节数。
- 后端明确维护的 Native 资源字节数。

约束：

- 从当前窗口的权威树结构遍历，不能为了统计建立 `static List<Element>`。
- 精确值与估算值必须在协议中区分。
- 无法可靠统计的 GPU 驱动内存不得伪造。

### M5：客户端时间线

- 客户端按需轮询 `/api/v1/memory`。
- 根据 `sampledAtUnixMilliseconds` 和累计指标计算速率。
- 服务端保持无状态，不保存无限历史。
- 如未来需要服务端短期历史，必须有固定容量环形缓冲区和显式启用开关。

### M6：有副作用的诊断动作

只有在独立设计后才考虑：

```csharp
public bool AllowMemoryActions { get; set; }
```

该开关必须默认关闭，并与只读 `AllowMemoryDiagnostics` 分离。强制 GC 或 dump 生成不得混入第一阶段。

## 8. 验证

Square 的 canonical test command 是 `dotnet test Square.slnx`。下列内存诊断构建、NativeAOT 和真实 HTTP smoke test 是该完整测试之外的 focused/ad-hoc verification；如使用系统 TEMP 下的 `hermes-verify-` 前缀临时脚本，脚本结束后应删除。

最低验证：

```bash
dotnet test Square.slnx
dotnet test tests/Square.DevTools.Tests/Square.DevTools.Tests.csproj
dotnet build src/Square.DevTools/Square.DevTools.csproj -c Release
dotnet build samples/Square.Sample/Square.Sample.csproj -c Release -p:SquareSampleUseDevTools=true
git diff --check
```

NativeAOT 验证：

```bash
dotnet publish samples/Square.Sample/Square.Sample.csproj \
  -c Release \
  -r win-x64 \
  -p:SquareSamplePublishAot=true \
  -p:SquareSampleUseDevTools=true \
  -o "$TEMP/hermes-verify-square-memory-aot"

file "$TEMP/hermes-verify-square-memory-aot/Square.Sample.exe"
```

还需对普通 Release 和 NativeAOT 产物分别执行真实 HTTP smoke test：

- 未带 token → `401`。
- 带 token、功能关闭 → `403`。
- 带 token、功能开启 → `200`，字段完整且值满足基本约束。

验证结果必须描述为 focused/ad-hoc evidence，不能笼统声称完整测试套件全部通过。

## 9. 开放问题

- [x] `Square.Sample --devtools` 不默认开启内存诊断；使用 `--devtools-memory` 显式开启。
- [ ] 第二阶段 Square 专项指标按窗口返回，还是在进程级响应中按 window/target 分组。
- [ ] 未来时间线由独立 Web UI、Chrome 自定义面板还是其他客户端展示。
- [ ] 是否需要稳定的 schema/version 字段；第一版沿用 `/api/v1` 版本边界即可。
