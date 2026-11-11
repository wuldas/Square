# 布局动画（720 方块）：历史六方对照与 Vulkan 低帧率修复

历史六方对照让 Square 三种后端、Electron、Flutter、Avalonia 跑同一份 CSS 动画负载：三组各 240 个方块，6 秒 linear 循环，客户区 **900×1240、DPR 1**。下方六方表保留低 FPS 修复前的历史 30 秒单轮原数；本轮另做 Vulkan before/after 各三轮交替 30 秒，不能把新 Vulkan 数据拼成同批六方排名。

上一轮已处理 Vulkan 呈现异常；本轮只补接纯色单叶 opacity 快路径，原尺寸 FPS 中位数从 **7.980772 提升到 59.972378**。完整当前证据见[修复记录](../../artifacts/vulkan-layout-fps-fix/README.md)。

## 环境

| 项 | 值 |
|---|---|
| CPU | 12th Gen Intel Core i5-12400F（6 核 12 线程） |
| GPU / 驱动 | NVIDIA GeForce GTX 1660 SUPER / 32.0.16.1088 |
| 系统 | Microsoft Windows 11 专业版，build 26300 |
| .NET | SDK 10.0.303（net10.0 运行时） |
| 显示 | 2560×1440，单屏，DPR 1 |
| 仓库背景 | commit `cc22599`；实际源码来源为工作树快照及下文 SHA-256，commit 不能完整标识本次版本 |

## 负载

| 组 | 数量 | 属性 | 颜色 | 说明 |
|---|---:|---|---|---|
| transform | 240 | translate 0→36→0、rotate 0→180→360°、scale 0.6→1.4→0.6 | `#EF4444` | 三层嵌套 30×30，核心 18×18 居中，不触发布局 |
| opacity | 240 | opacity 0→1→0 | `#3B82F6` | 只影响合成与重绘 |
| width | 240 | width 8→30→8 | `#F59E0B` | 每帧重新排列换行，触发布局 |

方块 30×30，右间距 5、下间距 4，`flex-wrap` 换行网格。三个实现分别由 [CssAnimationPerfPage.sqv](<Square/CssAnimationPerfPage.sqv>)、[index.html](<Chrome/index.html>)、[Avalonia/Program.cs](<Avalonia/Program.cs>)、[FlutterLayout/lib/main.dart](<FlutterLayout/lib/main.dart>) 描述，方块数、时长、关键帧停点一致。

## 计量口径

- **FPS**：各实现的计数器并不相同。Square 统计 `FramePresented`（渲染提交次数）；Avalonia 与 Electron 统计动画帧回调（Electron 是页面内 `requestAnimationFrame`）；Flutter 统计引擎帧时序回调。它们都表示"每秒推进的帧"，但不是同一种扫描/呈现完成信号。
- **CPU**：进程 CPU 时间 ÷ 墙钟时间，单位是"一个逻辑核 = 100%"。Square 与 Avalonia 取自身进程；Electron 与 Flutter 由外部采样器按进程树统计，Electron 因此包含 browser/GPU/renderer/utility 四个进程。
- **内存**：私有提交字节与工作集的均值/峰值，约 1 Hz 采样；不含独显显存。
- Vulkan 与 Direct2D 的窗口为固定边框系统标题栏，内容区仍为 900×1240。

## 低 FPS 修复前：历史六方单轮

此表保留旧主报告批次，和[旧摘要中的另一批次](../../artifacts/layout-animation-20261030/summary.md)分别注明，均不混入当前三对复测。

| 实现 | 秒 | 帧数 | FPS | CPU（1 核=100%） | 私有均值 MiB | 私有峰值 MiB | 工作集峰值 MiB | 进程数 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Square Software | 30.297 | 1805 | 59.58 | 54.51% | 59.7 | 66.6 | 118.8 | 1 |
| Square Direct2D | 30.348 | 1821 | 60.00 | 35.42% | 100.6 | 108.7 | 132.4 | 1 |
| Square Vulkan | 30.065 | 258 | **8.58** | 45.63% | 266.0 | 278.4 | 213.3 | 1 |
| Avalonia 11 | 30.016 | 1799 | 59.93 | 34.31% | 118.3 | 125.2 | 148.9 | 1 |
| Electron 44.7.2（Chromium 152） | 30.016 | 1800 | 59.97 | **95.54%** | 267.9 | 276.5 | 427.5 | 4 |
| Flutter（Impeller / GLES） | 30.010 | 1934 | **64.4** | 38.27% | 165.8 | 168.9 | 111.1 | 1 |

读法：多数计数接近 60，但这些异源计数不能单独证明都被 vsync 封顶；Flutter 的 64.4 也不能证明它解除了 vsync。Electron 的 CPU/内存覆盖四进程，不能由此把额外开销全部归因于架构。历史各项只有一轮，没有跨框架统计排名或无限帧吞吐结论。

## 上一轮：Square Vulkan 呈现异常处理

上一轮观察到 `vkQueuePresentKHR` 返回 `VK_ERROR_OUT_OF_DATE_KHR` 后，surface capabilities 查询返回 `VK_ERROR_SURFACE_LOST_KHR`，重建路径抛异常退出。现有证据不能认定它稳定在一秒触发，也未证明原始 surface 失效的原因。

上一轮改动（本次没有再修改）：

1. `VulkanSwapchain.Recreate`：caps 查询遇到 `SURFACE_LOST` 时，先用同一原生窗口调用 `VulkanDevice.RecreateSurface()` 重建 surface 并重试一次；仍失败则把交换链标记为不可用并**跳过该帧**，不再抛异常。重建过 surface 的旧交换链不作为 `oldSwapchain` 链入新表。
2. `VulkanDevice`：保留创建时传入的 `INativeRenderTarget`，新增 `RecreateSurface()`（等待设备空闲后重建 surface，并释放旧 surface）。
3. `VulkanRenderContext`：新增 `TryResumePresentation()`，暂停状态按 250 ms 节奏重试，恢复后自动继续渲染；`Resize` 在交换链不可用时同样转为暂停而不是继续使用失效表。

上一轮处理后的工作树基线 SHA-256（低 FPS 快路径修复之前；当前 Context 哈希见下一节）：

| 文件 | SHA-256 |
|---|---|
| [VulkanSwapchain.cs](<../../src/Square.Backends.Vulkan/VulkanSwapchain.cs>) | `8dd72356c6d2a4a4e4081c84a94ca5534265968a9c29e583feb0d35d89321b52` |
| [VulkanDevice.cs](<../../src/Square.Backends.Vulkan/VulkanDevice.cs>) | `3f65ab163a58e9c5b059fb12b6ca7c7eeb67158585e636ce7e5a2b03f5dac469` |
| [VulkanRenderContext.cs 基线快照](<../../artifacts/vulkan-layout-fps-fix/before/VulkanRenderContext.cs>) | `caf6602a0a865b4d08bfa330d35538dfe9d30befb2d9403767e6be384f09c367` |

上一轮可运行的 Vulkan 30 秒记录保留在历史表中。本轮没有重做 surface 修复，也没有修改冻结 V11 二进制。

## 当前：原尺寸 Vulkan 低帧率修复

原先用 400×300 与 900×1240 的差异断言“瓶颈已确定为按像素成本”不成立：窗口变化还会改变布局、clip 和可见图层。真正定位来自**固定 900×1240** 的[修复前阶段日志](../../artifacts/vulkan-layout-fps-fix/before-phases.log)：热帧有240个离屏层，池只复用16个，每帧重建224个；回收约60ms、创建约30–40ms。图层资源周转是已定位的主要成本。

[生产改动](../../src/Square.Backends.Vulkan/VulkanRenderContext.cs)仅新增已有 `ISolidLeafOpacityRenderer` 接口及25行方法/helper：root target、无mask、轴对齐且实际物理边界为整数的**单个纯色叶矩形**直接合成 alpha，去掉不必要的离屏层。nested、旋转/斜切、分数边界、复杂 clip 仍走原路径，图层池上限未改。[修复后阶段日志](../../artifacts/vulkan-layout-fps-fix/after-phases.log)显示此负载为0层/0重建。阶段插桩已移除，下面样本来自无profiling的隔离二进制。

两版本按 before→after 交替，各三次独立进程；每次先暖机2秒再请求测量30秒。原动画、900×1240、720方块和默认MSAA策略未改。全部六轮 exit=0、stderr为空；精确值及所有均值/峰值见[原始CSV](../../artifacts/vulkan-layout-fps-fix/measurement-final/runs.csv)。内存列为均值/峰值 MiB。

| 顺序 | 实测秒 | 帧数 | FPS | CPU%（单核） | CPU ms/帧 | 私有 MiB | 工作集 MiB |
|---|---:|---:|---:|---:|---:|---:|---:|
| before-1 | 30.072277 | 240 | 7.980772 | 42.4498 | 53.1901 | 272.8/286.9 | 222.2/234.9 |
| after-1 | 30.046413 | 1803 | 60.007164 | 51.1708 | 8.5275 | 217.5/261.9 | 226.5/270.8 |
| before-2 | 30.088415 | 237 | 7.876786 | 43.7253 | 55.5116 | 273.3/285.2 | 222.5/234.6 |
| after-2 | 30.030492 | 1801 | 59.972378 | 35.3287 | 5.8908 | 210.8/259.4 | 203.9/252.4 |
| before-3 | 30.054463 | 249 | 8.284959 | 45.3344 | 54.7189 | 265.0/277.5 | 200.7/213.1 |
| after-3 | 30.213554 | 1803 | 59.675204 | 44.6819 | 7.4875 | 208.6/253.6 | 203.2/248.1 |

FPS中位数/范围：**7.980772 [7.876786,8.284959] → 59.972378 [59.675204,60.007164]**。CPU/帧按每轮 `cpuSeconds×1000/frames` 再取中位数，为 **54.718876→7.487521ms**；墙钟CPU%中位数为43.7253→44.6819，并未同步大幅下降。私有均值的中位数272.8→210.8MiB，但三轮最大工作集峰值234.9→270.8MiB上升，不能概括为所有内存指标都下降。

[独立GPU runner日志](../../artifacts/vulkan-layout-fps-fix/checks/gpu-checks-final.log)为12/12、exit0，覆盖1x/4x MSAA及8种opacity（含±∞/NaN）、缓存、裁剪、图层和渐变。常规testhost在本机仍有 `Win32Exception(5)`，因此直接调用既有xUnit断言方法。叶子快路移除了RGBA8中间层量化，对照允许**每byte≤2**，不声称0diff。[Software](../../artifacts/vulkan-layout-fps-fix/measurement-final/software-smoke.stdout.log)/[Direct2D](../../artifacts/vulkan-layout-fps-fix/measurement-final/direct2d-smoke.stdout.log)另各5秒回归为60.033056/59.989888 FPS，均exit0；不并入历史六方排名。

源码基线来自[工作树快照](../../artifacts/vulkan-layout-fps-fix/before/VulkanRenderContext.cs)，不是HEAD；当前Context SHA-256为 `A7C913866D421BB958A3B53CBFC8B45290A64C53EFBA8B30568111896F68A214`。[最终核验](../../artifacts/vulkan-layout-fps-fix/measurement-final/final-verification.json)确认源码已恢复并重建，DesktopApplication、Batch、Device、Swapchain、样例Program与基线快照一致，原sqv和已核对V11二进制未变；[精确差异](../../artifacts/vulkan-layout-fps-fix/measurement-final/production-vs-snapshot.diff)仅为上述接口接入与25行方法/helper。

## 边界

历史六方各一轮；当前仅Vulkan有三对样本。FPS计数边界不同，接近60或Flutter历史64.4均不能证明扫描输出或未限帧吞吐。CPU、约1Hz采样的内存不含独显显存，也不能从Electron进程树总量推出架构因果。此报告为UI布局动画；冻结V11画布测试是另一组输入、二进制和观察边界。

## 复现

```powershell
# Square 三种后端（30 秒）
samples/Square.Sample.PerfComparison/Square/bin/Release/net10.0/Square.PerfComparison.exe --backend Software --seconds 30
samples/Square.Sample.PerfComparison/Square/bin/Release/net10.0/Square.PerfComparison.exe --backend Direct2D --seconds 30
samples/Square.Sample.PerfComparison/Square/bin/Release/net10.0/Square.PerfComparison.exe --backend Vulkan  --seconds 30

# Avalonia
samples/Square.Sample.PerfComparison/Avalonia/bin/Release/net10.0/Avalonia.exe --seconds 30

# Electron（需先清空 ELECTRON_RUN_AS_NODE，否则 electron.exe 会按 node 模式启动）
$env:ELECTRON_RUN_AS_NODE=$null
samples/Square.Sample.CanvasBenchmark/Electron/.runtime/electron.exe --no-sandbox `
  samples/Square.Sample.PerfComparison/Electron --output out.json --seconds 30

# Flutter（Release 构建）
samples/Square.Sample.PerfComparison/FlutterLayout/build/windows/x64/runner/Release/flutter_layout.exe --seconds 30
```

一次性跑完六条车道并落盘原始输出的脚本：[run-all-lanes.ps1](../../artifacts/layout-animation-20261030/run-all-lanes.ps1)。
本批原始输出与汇总：[artifacts/layout-animation-20261030](../../artifacts/layout-animation-20261030/summary.md)。
