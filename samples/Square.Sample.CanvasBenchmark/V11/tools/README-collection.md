# V11 独立收集与报告

[run-v11.py](run-v11.py) 和 [report-v11.py](report-v11.py) 只使用 Python 标准库。collector 的 `--plan-only`、reporter 的任何调用都不启动窗口；普通 collector 通过公共 Observer 串行启动真实 app，由 lead 执行。已启动的正式采样期间冻结 collector、producer、renderer、Observer 和输入；只允许继续完成报告脚本及本文。

## 计划与启动

在仓库根目录，CPU-only 校验命令：

```powershell
$pythonExe = 'C:\Users\Wuldas\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe'
& $pythonExe -B samples/Square.Sample.CanvasBenchmark/V11/tools/run-v11.py `
  --capture-backend gdi --paired-controls --plan-only
```

冻结 manifest 有29个 benchmark case/level、2个 semantic fixture 和1个 marker calibration trace。benchmark 基础矩阵为29×6×3=522个独立进程，包含 native-stroke/image-scaled 独立质量 profile。固定 seed=305441741，各轮打乱 case，轮换框架顺序。

`--paired-controls` 为每个基础项增加一个相同 trace 的独立控制进程，共1044次：observed522/control522。每对拥有相同 `pairId/baseIndex/round/traceId/target`、独立 tag、独立目录；先序按固定 seed 交替，261对 observed先、261对control先。默认不带此 flag 保留旧522 observed计划。

以下是可复现的正式启动命令，仅用于新的输出目录：

```powershell
& $pythonExe -B samples/Square.Sample.CanvasBenchmark/V11/tools/run-v11.py `
  --capture-backend gdi --paired-controls `
  --output 'C:\.AA\dotnet-projects\Square\artifacts\canvas-optimization\run-v11-gdi-paired'
```

输出根必须不存在；拒绝覆盖原始样本，当前不实现 resume 或以重试替换失败。tag 1..255 在串行独立 root PID 间循环，观测记录不跨进程复用。

`--capture-backend desktop-duplication|gdi` 默认 DD，任何失败均不自动切换。launch 显式写所选后端；observed 输出必须匹配该 backend/eventDriven（DD=true、GDI=false）。GDI 的解释固定为：**受限的采样可见变化率/可能漏见（显示同步、时间边界量化）**。count/duration 可能因端点量化略高于显示刷新率，例如60.05；它不构成严格数学下界、精确FPS、GPU吞吐或单一总排名。原GDI limited/badCoverage门槛不修改、不放宽，合格子集为0时显示0/N/A。

默认程序：Square `V11/Square/bin/Release/net10.0/CanvasBench.SquareV11.exe --backend Software|Direct2D|Vulkan`；Electron 复用旧 `Electron/.runtime/electron.exe --no-sandbox V11/Electron`；Flutter `V11/Flutter/build/windows/x64/runner/Release/square_canvas_v11.exe`；Avalonia `V11/Avalonia/bin/Release/net10.0/CanvasBenchmark.V11.Avalonia.exe`；公共 Observer `V11/Observer/bin/Release/net10.0/Observer.exe`。可显式覆盖各程序路径、seed和timeout；不安装或升级任何依赖。

## 两模式与原始数据

每个子app使用同一 trace 绝对路径、独立 app.json、匹配tag和`--verify 0`。observed 的 `observeFrames=true`；control 为false，app仍正常执行完整trace/原生绘制与GPU后端，只有 Observer 不初始化捕获GPU/GDI、不readback、不decode marker。control保留同样的root窗口/TOPMOST和进程树监控开销。

共同校验：正常退出、v11/tag/trace SHA、1280×800/DPR1、固定warm/measure数量、timingEligible、无verify、共享字体hash、全部实际opcode totals、measurement帧数组/顺序、版本/硬件或默认管线metadata、root-owned标题窗口/DPI96。Square Vulkan另验证真实GPU类型、timed readback关闭。Flutter成功状态按实际schema接受`completed`。

observed还检查成功marker phase2→3、唯一序号/missing accounting、count/duration公式、capture启用和backend/eventDriven匹配。control必须`status=control-complete`、`marker:null`、`sampling.enabled:false`、`capture:null`，跳过marker测量条件；不把control当桌面观测样本。控制进程自身的frameCpu数组和trace操作计数仍须完整通过共同校验。

`valid`与观测资格分开。observed的limited/badCoverage不删除有效慢样本，只使`observationQualified=false`并记录原始原因。control永不进入该子集。Missing IDs表示漏见/合并，不能叫GPU掉帧。

根目录：

- `environment.json`：seed、完整case/target计划、pairedControls/baseRuns、显式captureBackend/eventDrivenRequired、captureObservationMode/observationModeLimited/observationInterpretation、命令和冻结哈希。
- `plan.json`：固定1044项或兼容的522项；每项有index/baseIndex/round/target/traceId/tag/pairId/observationMode/directory。`observationMode`只为observed或control，捕获解释另用`captureObservationMode`。
- `manifest.json`：逐项更新的完整记录，保留valid/失败/覆盖原因/原始文件hash。
- `completion.json`：总数、每模式planned/finished/valid/observationQualified、冻结前后hash及变化路径。

paired目录为`r<round>-<traceId>-<target>/observed|control/`，各自保存launch.json、app.json、observer.json、stdout.log、stderr.log、run.json。旧522布局不变。每个run明确记录模式及capture contract。stdout/stderr用直接文件句柄继承，不用PIPE；异常停止可疑重叠，不删除失败证据。

collector清理其进程环境中的ELECTRON_RUN_AS_NODE/FORCE_RENDERER_ACCESSIBILITY/ELECTRON_FORCE_RENDERER_ACCESSIBILITY，Electron TEMP/TMP指向该run。Square要求D2D硬件、禁Vulkan timed readback/validation。未修改全局环境。

输入manifest/schema/generator、32个trace及资产、新producer/Observer源码与全部现有runtime/bin、v8/v9/v10旧renderer源码引用在采样前后核对。源码冻结排除Electron .tmp/.user-data和构建中间目录；DD要求native capture DLL，已存在的Observer bin文件包括该DLL全部冻结。tools只冻结collector/generator/validator，**不冻结report-v11.py和本文**；报告生成器在最终summary里单独记录SHA256。

## 报告命令

数据齐后由 lead 通知生成，示例：

```powershell
& $pythonExe -B samples/Square.Sample.CanvasBenchmark/V11/tools/report-v11.py `
  --input 'C:\.AA\dotnet-projects\Square\artifacts\canvas-optimization\run-v11-gdi-paired' `
  --quality 'C:\.AA\dotnet-projects\Square\artifacts\canvas-optimization\preflight-v11\quality-verification-v11.json' `
  --diagnostic-root 'artifacts/canvas-optimization/preflight-v11/calibration-gdi-final-six' `
  --diagnostic-root 'artifacts/canvas-optimization/preflight-v11/control-gdi-final-six' `
  --diagnostic-root 'artifacts/canvas-optimization/preflight-v11/calibration-gdi-stress-six' `
  --diagnostic-root 'artifacts/canvas-optimization/preflight-v11/control-gdi-stress-six' `
  --diagnostic-root 'artifacts/canvas-optimization/preflight-v11/control-gdi-stress-ava-fixed'
```

默认产物写raw run根，可指定`--output`。reporter只读验证原始JSON/输出hash/冻结文件，不运行benchmark。未完成矩阵可产生明确标记incomplete的报告，所有missing/invalid仍占计划N。正式执行只在数据齐后进行。

| 输出 | 内容 |
| --- | --- |
| report-v11.md / report-v11.html | 每case/level先control主耗时及实际CPU，再observed采样/配对扰动，两套N、原始链接和资格限制 |
| summary-v11.json | 按case/target/mode分开的groups、逐run、pairs/pairGroups、模式总数、runtime metadata、报告生成器hash |
| summary-v11.csv | 每case/target/mode一行，正常矩阵348组，每metric的n/median/min/max |
| runs-v11.csv | 每个计划进程一行，1044或兼容522条，含模式与全部失败/缺失 |
| pairs-v11.csv | 每个基础项一行，配对先序、是否两边计划/有效、逐对差值与比值；旧observed-only计划控制侧明确未计划 |
| charts/chart-<traceId>.svg | 仅control的frameCpuMeanMs：每run帧平均墙钟耗时，再取三轮median[min,max]；含同步等待，非实际CPU/GPU吞吐；质量受限琥珀/未验证灰，缺control为N/A。observed受限变化率保留在表格中 |

## 主指标与统计边界

主调用方结果只用control三轮。**调用方每帧/固定序列耗时（墙钟，含同步等待）**来自原始frameCpu.totalMs，不是GetProcessTimes实际CPU，也不是GPU渲染时间。`callerFrameTotalMs`为measurement逻辑帧的totalMs之和；P50为每run帧中位数，mean为算术平均，P95/P99按排序位置p×(n-1)线性插值（p=0.95/0.99）。Observer的P50/P95/P99保留nearest-rank：sorted[max(0,ceil(p×n)-1)]。两种方法在HTML、MD和JSON同时公开，互不重算。再对三个独立run的各统计量取median[min,max]；不把360帧池化后冒充独立样本。warmup/END、JSON、截图不进该数组。

captured调用方墙钟耗时只作配对扰动诊断。每round内算observed/control比值与差值，再给三对的N/median[min,max]；实际进程CPU和private bytes配对差值/比值独立保留。小N配对不是因果置信结论。缺失或失败control时主耗时/实际CPU为N/A，绝不拿captured补位，外部preflight control也不会移植进正式矩阵。

实际进程树CPU取Observer的GetProcessTimes，100%等于一个逻辑核心，受进程发现/句柄可用性影响保留采样下界说明。**control只有whole monitored lifetime CPU及private bytes峰值**，包括启动/warmup/measurement/END/退出grace；没有marker phase，marker-window CPU字段强制N/A。observed marker-phase实际CPU保留实际采样边界，和whole-lifetime字段分开。private bytes为全树采样峰值，单位MiB，采样间峰值可能漏掉；不把shared working set重复页当private bytes。

Square边界在报告中明确为Stopwatch wall-clock prepare + record/Flush + Present-return，含同步等待；冻结producer旧metric只留raw链接/metadata，不把旧主指标宣言输出正文。

rAF、Flutter FrameTiming、Avalonia Render、Square Present API和Vulkan fence只作各自边界诊断，不混成共同FPS。GDI采样按实际flags受限，qualifiedN=0也保留全部有效观测。无混合总分、无历史FPS、无择优保留；process-cold不声称清空OS/字体/驱动缓存。

最终quality JSON只按entries场景scope套用：174个case/level×target决定中136 semantic-pass、38 limited；两模式共享语义资格但统计N独立。Software透明image失败只限制有关场景，native-stroke/image-scaled/mixed gradient单列。AA/font raster/full-frame raw-pixel相同从未被声明。

## 捕获诊断与失败证据

每run保留samplingTelemetry（除逐样本数组）、capture backend/eventDriven/marker timestampSource/QPC clock/native metadata；完整samples/acquisitions仍在raw Observer。DD用LastPresentTime，GDI用capture completion，二者不合并统计；QPC tick不是捕获吞吐分辨率。readback latency、native processing、累积desktop updates独立保留，不叫GPU掉帧。

外部calibration/control仅作附加扰动证据，按框架+精确trace SHA+capture backend分组。失败记录单列；例如旧Avalonia stress control失败和fixed成功分别保存，并显示有效/尝试次数与失败路径，不覆盖或静默挑样。DD失败三次可以通过额外diagnostic-root附作限制证据，永不混入formal groups或替代GDI模式。

## CPU 验证

collector已验证旧522兼容、1044配对计划、522/522模式数、261/261先序、目录/tag独立、seed可重现，并直接对24份真实GDI校准/控制/stress JSON共同schema校验通过（Ava成功修正版用于成功合同检查，旧失败证据仍保留）。仅report/README在正式运行期间可修改；没有启动GUI/GPU/build或读取lead的后台job。报告CPU合成测试覆盖缺失/无效control、零合格observed子集、wall-clock与实际CPU分开、配对比值、模式分列及JSON/CSV/MD/HTML/SVG导出。
