# Canvas：单一综合性能测试

默认只有一个综合场景、一个固定档位、control-only、每目标N3：Square Software/Direct2D/Vulkan、Electron、Flutter、Avalonia六目标，共18个独立进程。显式加入原main同harness的三个Square目标时为九目标、27个进程。不会默认采集29档、pressure循环或capture/control双份。

准备Windows/.NET10 SDK、已缓存的Electron44.7.2，以及V11 Flutter Release程序后，在仓库根目录运行一个命令：

```powershell
pwsh -File samples/Square.Sample.CanvasBenchmark/Run-Benchmark.ps1 -OutputDirectory artifacts/canvas-comprehensive/run-001
```

目录必须全新。默认脚本发布Square/Avalonia完整win-x64 self-contained runtime10.0.14、构建Observer；不安装或替换Electron/Flutter。外部runtime路径可用 `-ElectronExe` / `-FlutterExe` 指定；已有本轮正确产物时用 `-SkipBuild`。直接Python入口为 [V11/tools/run-v11.py](V11/tools/run-v11.py)，默认完全相同。

## 固定输入与时长

- 1280×800物理画布、DPR1、原Segoe UI/font.ttf、32×32 RGBA image.png、seed305441741。
- 复用原mixed-dashboard-high四panel的52条ops/frame，只补每panel transform/poly/end-transform及stroke，并令原poly paint按frame变化，合计68条ops/frame、44次绘制。真实覆盖fill、alpha、vector reuse/recreate、stroke、static/dynamic text、image、transform、clip、group layer、gradient。
- 固定60逻辑帧预热、120测量帧，另原750ms END保持；60Hz时逻辑生产约3秒，每进程含启动/准备/END通常约4–8秒，18进程预计约1.5–3分钟（不含构建，实际elapsed始终保留）。不为慢框架追60而改负载/采样数。
- [独立生成器](V11/tools/generate-comprehensive.py) 与 [独立manifest](V11/Comprehensive/manifest.json) 记录新trace/input/source哈希。旧 [Traces/manifest](V11/Traces/manifest.json)、旧32条benchmark/fixture输入、[旧生成器](V11/tools/generate-traces.py) 不改字节、不重生成。

## 显式扩展与对照

```powershell
# 仅构造计划，零build/GUI/producer；默认18
pwsh -File samples/Square.Sample.CanvasBenchmark/Run-Benchmark.ps1 -PlanOnly
# 明确选择旧29档，六目标N3=522 control进程
pwsh -File samples/Square.Sample.CanvasBenchmark/Run-Benchmark.ps1 -Suite extended -PlanOnly
# 同一新harness、不同engine的原main Square，不接受v4旧binary或历史成绩
pwsh -File samples/Square.Sample.CanvasBenchmark/Compare-Optimization.ps1 -BaselineSquare C:\absolute\baseline-publish\CanvasBench.SquareV11.exe -BaselineRoot C:\absolute\main-source -PlanOnly
```

原main exe须由同一个 [Square项目](V11/Square/CanvasBench.SquareV11.csproj) 使用 `-p:EngineRoot=C:\absolute\main-source` 单独构建/发布。新 [VulkanMetadata](V11/Square/VulkanMetadata.cs) 只读两版实际device/readback与存在的fence counters；原版不存在的counter为null，不复制任何renderer功能。普通未传baseline仍是六目标18。

capture仅由Python显式 `--observe` 选择；`--paired-controls` 只允许显式 `--suite extended`，不会成为默认。`--rounds 1 --targets square-software` 是明确的单目标smoke，不能称正式N3。旧108-load脚本只保留在明确的 [Run-Benchmark-Legacy.ps1](Run-Benchmark-Legacy.ps1) / [Compare-Optimization-Legacy.ps1](Compare-Optimization-Legacy.ps1)，不是canonical入口。

## 六列与有效性边界

`target, CPU, FPS, avgPrivate, avgWS, PublishMiB` 顺序不变。[control汇总器](V11/tools/report-control.py) 输出per-run、summary JSON/CSV/Markdown，逐run后汇总median[min,max]/实际N，空值不填0、慢样本不删，不算总分。

- CPU：已发现进程树GetProcessTimes生命周期下界/实际observer.elapsedMs，100%=一逻辑核；不是布局UI原measurement-window CPU。
- private/WS：同一首末sample窗口，实际atMs相邻完整样本梯形积分；不外推、不补缺口、保留真实退出0。coverage不完整主值N/A；WS会重复共享页，不是VRAM。
- FPS：120测量点的119个间隔；Square Present-return、Electron phase2 rAF timestamp、Flutter phase2 paintStart、Avalonia phase2 renderStart（排END）。原序严格递增；不是inverse caller wall、桌面变化或scanout。
- PublishMiB：运行前完整交付目录与Electron app资源快照，XML计入，仅pdb/dbg不计；FDD bin无runtime不是完整包，记N/A。相同包三轮重复引用不是三次独立发布。
- 新freeze绑定本次实际source、binaries、chosen trace/assets和build配置；control不要求DDCapture.dll，旧v8/v9/v10验证仅历史，不冒充当前资格。真实guard仍在采集前后逐SHA生效。
- 原Electron main仍在自身目录创建 `.user-data`/`.tmp`；仅这两个明确路径子树作为运行状态排除，不能排任意code/DLL/资源。新input、源文件与bin均被冻结。
- 综合含native stroke/gradient/AA、clip/group/image语义，保留不同profile限制。timing有效不代表逐像素同图或GPU吞吐排名；质量需本轮独立证据。

## 布局窗口标题

本轮 [PerfComparison入口](../Square.Sample.PerfComparison/Square/Program.cs) 的标题为 `Square CSS animation benchmark-{actual backend lower}-{jit/aot}`，例如 `...-direct2d-aot`。模式由 `RuntimeFeature.IsDynamicCodeSupported` 判断，不靠目录或build label猜。

普通SW/D2D包不加Vulkan依赖。独立VkCLR产品用 `-p:IncludeVulkan=true` 编译条件引用/分支；计时、CPUdelta、frames、memory原语句不变。新验证harness复制本轮stage入口，只加只读时间边界/DPR/runtime导出；不编辑旧Frozen common。
