"""Compare the first-stage cache with bounded global checkpoint retention."""
from pathlib import Path
import argparse
import json
import statistics
from summarize_fix import NAMES

BEFORE = "CheckpointBaselineTokenizationCache"
AFTER = "TokenizationCache"
NAMES = dict(NAMES, **{"rewind-8192": "回跳至索引8191", "rewind-document-middle": "回跳文档中部"})


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("inputs", nargs="+", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    runs = [json.loads(path.read_text(encoding="utf-8")) for path in args.inputs]
    assert all(run["metadata"]["Comparison"] == "checkpoints" for run in runs)
    assert len({run["metadata"]["Preparation"]["productionSourceSha256"] for run in runs}) == 1
    groups = {}
    for run in runs:
        for row in run["results"]:
            key = (row["Profile"], row["Checkpoints"], row["Scenario"])
            groups.setdefault(key, {}).setdefault(row["Variant"], []).append(row)
    def elapsed(rows):
        return statistics.median(value for row in rows for value in row["SamplesNs"]) / 1_000_000
    def allocated(rows):
        return statistics.median(row["BytesPerUnit"] for row in rows)
    flags = []
    for key, pair in groups.items():
        before, after = pair[BEFORE], pair[AFTER]
        assert all(row["TokenizerCalls"] <= before[0]["TokenizerCalls"] for row in after)
        if key[2] in {"cached-line", "cached-viewport"}:
            assert allocated(after) == 0 and elapsed(after) < 0.001
        elif elapsed(after) > elapsed(before) * 1.1 and elapsed(after) - elapsed(before) > 0.1:
            flags.append(f"{key}: +{(elapsed(after) / elapsed(before) - 1) * 100:.1f}%, +{elapsed(after) - elapsed(before):.3f} ms")
    meta = runs[0]["metadata"]
    text = ["# 全局检查点保留策略：修复前后对比", "",
            "对照组是上一轮已完成缓存热路径优化的生产实现，不是最早的 SortedDictionary 原版。时间表统一使用 **毫秒**；同一行左右两组单位完全相同。计量列区分每请求与整次事件。", "",
            "## 策略与来源", "",
            "非零检查点总预算仍为 1024，另有永久 0 哨兵。最多 512 个名额保护覆盖已成功扫描区域的全局网格，其余点按距本次目标的远近保留。全局间隔从 64 行开始，按文档长度单调倍增；文件缩短不缩小间隔，Reset 恢复 64。失效与缩短删除后缀后，不对尚未重扫的区域声明覆盖。", "",
            f"前阶段源码 SHA-256：`{meta['Preparation']['checkpointBaselineSha256']}`。", "",
            f"当前源码 SHA-256：`{meta['Preparation']['productionSourceSha256']}`。", "",
            f"环境：Intel Core i5-12400F，{meta['Runtime']}，{meta['OS']}，x64，AVX2={meta['Avx2']}，关闭分层编译、workstation GC。", "",
            "只运行一次前阶段 grammar 扫描，额外收集每 64 行的完整状态。新策略按同样顺序和目标行调用真实 StoreCheckpoint 重建自然初态，各组分别恢复自己的检查点图。两组并未强行使用相同的尾部检查点。采集、重建、恢复和完整 token 比较均在计时外。", "",
            "## 验收", "",
            "已缓存请求保持 0 B 托管分配、低于 0.001 ms；本次指定轨迹的新策略分词次数不增加。完整请求 token、各自恢复状态及有效全局网格覆盖检查通过。", "",
            "显著回退检查：" + ("未发现同时超过 10% 且绝对增加超过 0.1 ms 的场景。" if not flags else "以下场景需关注："), ""]
    text += [f"- {flag}" for flag in flags]
    profiles = list(dict.fromkeys(key[0] for key in groups))
    text += ["## 百万行初态覆盖", "", "| 分词器 | 策略 | 最早非零恢复点 | 最大检查点间隔/行 | 全局间隔/行 |", "|---|---|---:|---:|---:|"]
    for profile in [value for value in profiles if value.startswith("large")]:
        pair = groups[(profile, 1024, "cached-line")]
        for variant, label in [(BEFORE, "前阶段"), (AFTER, "全局保留")]:
            row = pair[variant][0]
            text.append(f"| {profile} | {label} | {row['FirstSeedCheckpoint']} | {row['MaxSeedCheckpointGap']} | {row['GlobalCheckpointInterval']} |")
    for path, run in zip(args.inputs, runs):
        if "recheck" not in path.as_posix(): continue
        rows = {row["Variant"]: row for row in run["results"] if row["Profile"] == "synthetic" and row["Checkpoints"] == 1024 and row["Scenario"] == "cold-jump"}
        if not rows: continue
        before_ms, after_ms = rows[BEFORE]["MedianNs"] / 1_000_000, rows[AFTER]["MedianNs"] / 1_000_000
        text += ["", "## 小型冷跳定向复测", "",
                 f"初轮约 1 ms 的合成冷跳曾出现约 14% 差异，因此另开进程增加 {run['metadata']['Rounds']} 轮。该次中位数为 {before_ms:.6f} → {after_ms:.6f} ms（{(after_ms / before_ms - 1) * 100:+.1f}%）。下方合成表格合并初轮与复测的全部样本，并未只选择较好的那轮。", ""]
    for profile in profiles:
        rounds = [str(run["metadata"]["Rounds"]) for run in runs if any(row["Profile"] == profile for row in run["results"])]
        text += ["", f"## {profile}", "", f"计量轮数：{', '.join(rounds)}。取原始批次平均耗时的样本中位数；不是单次请求 p95，也不是 UI 帧时间。", "",
                 "| 非零CP初始数 | 场景 | 计量 | 前阶段/ms | 全局保留/ms | 变化 | 分词行数/整轨迹 |", "|---:|---|---|---:|---:|---:|---:|"]
        keys = [key for key in groups if key[0] == profile]
        for key in keys:
            before, after = groups[key][BEFORE], groups[key][AFTER]
            unit = "每请求" if before[0]["Unit"] == "request" else "整次事件"
            delta = (elapsed(after) / elapsed(before) - 1) * 100
            text.append(f"| {key[1]} | {NAMES[key[2]]} | {unit} | {elapsed(before):.6f} | {elapsed(after):.6f} | {delta:+.1f}% | {before[0]['TokenizerCalls']} → {after[0]['TokenizerCalls']} |")
        text += ["", "托管分配（每请求用 B，整次事件用 MiB，同一行两组单位一致）：", "",
                 "| 非零CP初始数 | 场景 | 单位 | 前阶段 | 全局保留 |", "|---:|---|---|---:|---:|"]
        for key in keys:
            before, after = groups[key][BEFORE], groups[key][AFTER]
            factor = 1 if before[0]["Unit"] == "request" else 1048576
            unit = "B/请求" if factor == 1 else "MiB/事件"
            text.append(f"| {key[1]} | {NAMES[key[2]]} | {unit} | {allocated(before) / factor:,.3f} | {allocated(after) / factor:,.3f} |")
    text += ["", "## 边界与复现", "",
             "- 百万行文本是生产 CodeEditorTextModel，80,999,999 个 UTF-16 码元，约 154.50 MiB 字符净荷。",
             "- 全局覆盖仅针对已成功扫描、未被失效裁掉的前缀；冷启动仍必须建立跨行状态。本次没有实现后台或分批高亮。",
             "- 局部请求只多一个 O(1) 间隔检查，纳秒级差异单独以零分配和 1 微秒门槛判断，不用很小基数的百分比作强排名。其他小操作的绝对变化也保留在表中。",
             "- 各策略的检查点集合和最终缓存窗口可以不同，返回 token 必须相同。指定轨迹回放量不增加不代表任意访问序列都不会回退。",
             "- 失效只测缓存失效与重读，不包含模型编辑。分配是当前线程托管累计值，不含原生内存、构造/恢复、其它线程或进程峰值。",
             "- 历史 FIX-RESULTS.md 保留，当前报告单独记录本轮全局策略。完整测试记录位于 artifacts/tokenization-bench/checkpoint-tests/inprocess-final.log。", "",
             "```powershell", "./tools/Square.TokenizationBench/Run.ps1 -Python python -Comparison checkpoints -Rounds 9 -OutputDirectory artifacts/tokenization-bench/checkpoints-normal-2", "./tools/Square.TokenizationBench/Run.ps1 -Python python -Comparison checkpoints -Profile large -Rounds 5 -OutputDirectory artifacts/tokenization-bench/checkpoints-large-2", "./tools/Square.TokenizationBench/Run.ps1 -Python python -Comparison checkpoints -Profile synthetic -Rounds 21 -OutputDirectory artifacts/tokenization-bench/checkpoints-synthetic-recheck", "python tools/Square.TokenizationBench/summarize_checkpoints.py artifacts/tokenization-bench/checkpoints-normal-2/results.json artifacts/tokenization-bench/checkpoints-large-2/results.json artifacts/tokenization-bench/checkpoints-synthetic-recheck/results.json --output tools/Square.TokenizationBench/CHECKPOINT-RESULTS.md", "```", ""]
    args.output.write_text("\n".join(text), encoding="utf-8")
    print(args.output)
    for flag in flags: print("RECHECK:", flag)


if __name__ == "__main__":
    main()
