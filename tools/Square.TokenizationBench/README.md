# TokenizationCache 性能对比

独立程序直接编译当前生产缓存、分词器和文本模型，支持最早原版、第一阶段优化版及当前全局检查点策略的对比。BBTree 只在历史候选基准中使用，生产代码没有新增包依赖。

## 当前全局检查点对比

需要 .NET 10 SDK、PowerShell 7 和 Python 3（标准库）。从仓库根目录运行：

```powershell
./tools/Square.TokenizationBench/Run.ps1 -Python python -Comparison checkpoints -Rounds 9 -OutputDirectory artifacts/tokenization-bench/checkpoints-normal-2
./tools/Square.TokenizationBench/Run.ps1 -Python python -Comparison checkpoints -Profile large -Rounds 5 -OutputDirectory artifacts/tokenization-bench/checkpoints-large-2
./tools/Square.TokenizationBench/Run.ps1 -Python python -Comparison checkpoints -Profile synthetic -Rounds 21 -OutputDirectory artifacts/tokenization-bench/checkpoints-synthetic-recheck
python tools/Square.TokenizationBench/summarize_checkpoints.py artifacts/tokenization-bench/checkpoints-normal-2/results.json artifacts/tokenization-bench/checkpoints-large-2/results.json artifacts/tokenization-bench/checkpoints-synthetic-recheck/results.json --output tools/Square.TokenizationBench/CHECKPOINT-RESULTS.md
```

仅验证语义：

```powershell
./tools/Square.TokenizationBench/Run.ps1 -Python python -Comparison checkpoints -ValidateOnly
```

`-Profile synthetic|textmate|all|large` 选择负载，默认 `all`。`-Comparison checkpoints` 比较前阶段与当前策略；`production` 比较最早原版与当前代码；`legacy` 复现历史五组容器候选。

## 当前保留策略

每 64 行生成候选检查点，非零检查点总预算仍为 1024，另有永久 `(0, null)` 哨兵。最多 512 个名额保护全局网格，其余名额按距本次目标的远近保留，兼顾当前区域的密集恢复点。

全局间隔从 64 开始，单调翻倍到 `interval * 512 >= LineCount`。新网格是旧网格子集，能保留已建立的全局覆盖。失效及文档缩短仍删除后缀，未重扫的失效区域没有覆盖保证；缩短不降低间隔，Reset 恢复 64。

超过预算时从 local 首尾挑最远点淘汰，global 和 0 哨兵不参与。利用完整全局前后缀二分定位 local 两端；普通向前扫描只需定位前端。行 token 缓存仍是目标中心 ±128 的闭区间，请求结束最多 257 行，重放过程中只会同时保留两个有界窗口。

## 正确性测试

常规环境：

```powershell
dotnet test tests/Square.Extensions.CodeEditor.Tests/Square.Extensions.CodeEditor.Tests.csproj -c Release
```

当前环境的 VSTest testhost 无法监视父进程。通过工作区已有的进程内 xUnit runner 完成全套 **190 项通过、0 失败、0 跳过**，日志在 `artifacts/tokenization-bench/checkpoint-tests/inprocess-final.log`。其中包括前阶段 19 项缓存回归和本轮 7 项全局保留回归，覆盖独立线性淘汰 oracle、20 万行状态回跳、倍增边界、网格附近编辑及缓存命中时增长。

现有 runner 源码位于被忽略的 `artifacts/inprocess-tests/`；普通环境直接运行标准测试命令即可。

## 冻结参考源码

| 文件 | 参考实现 | 归一化 UTF-8/LF SHA-256 |
|---|---|---|
| `Baseline/TokenizationCache.cs.txt` | 最早 SortedDictionary 原版，提交 `1912e3319080441a94a1a336a676e5cf5977c2f4` | `55b34cb46eddb80ed2f38f78d9f275ab77abdf7920d5147355e74dfdf127ed3c` |
| `Baseline/CheckpointBaseline.cs.txt` | 第一阶段已优化版，仍只保留最高行号的检查点 | `91b34b30ba033cef4e4f8ae31bdd50c1bf8a930f8364f80803f70439d24d800d` |

`prepare.py` 检查参考哈希后生成不同类名；当前类直接链接生产源码。参考、当前源码和包文件的哈希写入 `preparation.json` 并嵌入 JSON 结果。

依赖固定为 TextMateSharp / Grammars 2.0.4、Onigwrap 1.0.11，历史比较另使用 BBTree 0.1.1。包、生成代码和隔离 NuGet 缓存都在被忽略的 `artifacts/tokenization-bench/`。

历史 `RESULTS.md`、`LARGE-RESULTS.md`、`FIX-RESULTS.md` 保留为当时测量，不用新策略数据覆盖。历史五组使用 `-Comparison legacy`，配套 `summarize.py` / `summarize_large.py`。`summarize_fix.py` 只用于其对应第一阶段数据；本轮使用 `summarize_checkpoints.py`。

## 工作负载与公平性

常规合成层使用预生成的 80 UTF-16 码元行、无分配状态分词器，初始非零检查点为 64、512、1024。真实层使用 10 万行生产文本模型与预加载的 TextMate C# grammar。

百万行层实际创建 1,000,000 行生产 CodeEditorTextModel / PieceTable，每行 80 个 UTF-16 码元，含 LF 共 80,999,999 个码元，字符净荷约 154.50 MiB。合成与真实 TextMate 层使用相同文本模型。语料含普通 C#、单行注释、跨检查点块注释和 verbatim 字符串。

参考实现从空缓存单次扫描至索引 995903，共 995904 行。在扫描期间额外收集每 64 行的完整状态；新策略用同样顺序、目标和实际 StoreCheckpoint 重建其自然初态。每个样本分别恢复自身策略的快照，包括全局间隔；不把旧尾部1024点直接当成全局策略的初态。两组使用相同视口 tokens 和行末状态。

| 场景 | 单个计时样本 |
|---|---|
| cached-line | 同一缓存行读取 32768 次 |
| cached-viewport | 64 行视口内循环读取 8192 次 |
| scroll | 常规/合成读取512行；百万行真实层读取128行 |
| jumps / local-jumps | 4096 行范围内固定种子跳转；常规/合成128次，百万行真实层16次 |
| invalidate-* | 失效后重读，不含模型 Replace |
| cold-jump / cold-eof | 空缓存首次跳转；百万 EOF 必须处理100万行 |
| rewind-* | 从接近末尾的初态回跳旧区域，新旧实际分词量分别记录 |

前阶段百万行快照仅保留 930432..995904（另有0）；新策略保留全局覆盖和局部点。结果逐行记录最早恢复点、最大恢复间隔、全局间隔和实际分词次数。

## 计量边界

常规预热 2 轮，大文本预热 1 轮，计量轮数由 `-Rounds` 指定。每轮轮换实现顺序。构造、模型初始化、grammar 加载、状态采集、重建、恢复、集合惰性视图初始化、显式 GC 与完整 token 比较在稳态计时外；空缓存场景仍包含自身初始化。

逐请求对照完整 tokens，分别验证缓存有界、token/state 键同步、全局网格无洞及 EOF 边界。新旧检查点集合和缓存窗口允许不同；指定轨迹要求新策略的分词量不增加，不对任意访问序列作无回退承诺。

时间是批次平均的样本中位数，事件是完整操作，不是 UI 帧时间或单次请求 p95。当前报告统一用毫秒展示时间，同一行的单位一致。原始 JSON 保留逐轮时间、min/max、工作量、源哈希和恢复点间隔；CSV 用纳秒原始单位。

分配只统计当前线程托管累计字节，包含取行、分词与缓存维护，排除原生内存、其它线程、构造/恢复与进程峰值。全局策略仍同步推进跨行状态，本轮未实现后台或分批高亮。
