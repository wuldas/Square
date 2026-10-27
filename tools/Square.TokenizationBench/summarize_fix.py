"""Report frozen-original versus current-production performance and acceptance gates."""
from pathlib import Path
import argparse
import json
import statistics

BASE = "BaselineTokenizationCache"
CURRENT = "TokenizationCache"
NAMES = {"cached-line": "同一行命中", "cached-viewport": "64行视口命中", "scroll": "顺序滚动",
         "jumps": "固定种子跳转", "local-jumps": "4096行范围内跳转", "invalidate-tail": "尾部失效＋重读",
         "invalidate-middle": "中部失效＋重读", "invalidate-retained-middle": "保留窗口中部失效＋重读",
         "cold-jump": "空缓存冷跳", "cold-eof": "空缓存至EOF", "rewind-8192": "回跳索引8191，重扫8192行",
         "rewind-document-middle": "回跳文档中部，重扫50万行", "invalidate-document-middle": "文档中部失效＋重读"}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("inputs", nargs="+", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--cpu", default="Intel Core i5-12400F")
    args = parser.parse_args()
    runs = [json.loads(path.read_text(encoding="utf-8")) for path in args.inputs]
    for run in runs:
        assert run["metadata"]["Comparison"] == "production"
        assert {row["Variant"] for row in run["results"]} == {BASE, CURRENT}
    assert len({run["metadata"]["Preparation"]["productionSourceSha256"] for run in runs}) == 1
    assert len({run["metadata"]["Preparation"]["baselineSourceSha256"] for run in runs}) == 1
    groups = {}
    for run in runs:
        for row in run["results"]:
            key = (row["Profile"], row["Checkpoints"], row["Scenario"])
            groups.setdefault(key, {}).setdefault(row["Variant"], []).append(row)
    def time(rows):
        return statistics.median(sample for row in rows for sample in row["SamplesNs"])
    def bytes_per_unit(rows):
        return statistics.median(row["BytesPerUnit"] for row in rows)
    flags = []
    for key, pair in groups.items():
        before, after = pair[BASE], pair[CURRENT]
        base_work = {(row["TokenizerCalls"], row["TokenizedCharacters"]) for row in before}
        current_work = {(row["TokenizerCalls"], row["TokenizedCharacters"]) for row in after}
        assert len(base_work) == len(current_work) == 1
        assert next(iter(current_work))[0] <= next(iter(base_work))[0]
        assert next(iter(current_work))[1] <= next(iter(base_work))[1]
        if time(after) > time(before) * 1.1:
            flags.append(f"{key}: 耗时中位数回退 {(time(after) / time(before) - 1) * 100:.1f}%")
        if key[2] in {"cached-line", "cached-viewport"}:
            assert bytes_per_unit(after) == 0, key
            assert time(after) < 1000, key
    meta = runs[0]["metadata"]
    text = ["# TokenizationCache 修复前后性能验收", "",
            "本次生产修复：SortedList 二分前驱、最大键判断是否需剪枝、过渡行不写 token 缓存、复用旧窗口状态、每批结束一次修剪、复用删除键缓冲。公共接口和检查点保留策略保持。", "",
            f"环境：{args.cpu}，{meta['Runtime']}，{meta['OS']}，{meta['Architecture']}，AVX2={meta['Avx2']}，workstation GC，关闭分层编译。", "",
            "## 来源与判定", "",
            f"冻结原版 SHA-256：`{meta['Preparation']['baselineSourceSha256']}`。", "",
            f"生产版 SHA-256：`{meta['Preparation']['productionSourceSha256']}`。", "",
            "基准直接编译当前生产源码，原版来自独立冻结 fixture。各实现记录自身调用次数；可减少重复分词，不能增加工作量。跨行状态、完整 token、失效、缩短增长、EOF、缓存容量及保留范围由计时外语义校验验证。", "",
            "已缓存请求的 0 B 托管分配、批次平均耗时小于 1 微秒、各轨迹分词调用数和字符数不增加：全部通过。", "",
            "超过 10% 耗时回退的场景：" + ("无。" if not flags else "需定向复核。"), ""]
    text += [f"- {flag}" for flag in flags]
    profiles = list(dict.fromkeys(key[0] for key in groups))
    for profile in profiles:
        relevant_runs = [run for run in runs if any(row["Profile"] == profile for row in run["results"])]
        dimensions = {row["DocumentLines"] for run in relevant_runs for row in run["results"] if row["Profile"] == profile}
        text += [f"## {profile}", "",
                 f"文档行数：{', '.join(str(value) for value in sorted(dimensions))}。预热 {relevant_runs[0]['metadata']['WarmupRounds']} 轮；各输入进程计量轮数：{', '.join(str(run['metadata']['Rounds']) for run in relevant_runs)}。表格合并同场景原始样本后取中位数；请求为批次平均，事件为完整操作。负变化表示更快。", "",
                 "| 非零CP初始数 | 场景 | 单位 | 原版 | 修复版 | 耗时变化 | 分词次数／整轨迹 |", "|---:|---|---|---:|---:|---:|---:|"]
        keys = [key for key in groups if key[0] == profile]
        for key in keys:
            pair = groups[key]
            before, after = pair[BASE], pair[CURRENT]
            factor = 1000 if before[0]["Unit"] == "request" else 1_000_000
            unit = "μs/请求" if factor == 1000 else "ms/事件"
            change = (time(after) / time(before) - 1) * 100
            text.append(f"| {key[1]} | {NAMES[key[2]]} | {unit} | {time(before) / factor:.3f} | {time(after) / factor:.3f} | {change:+.1f}% | {before[0]['TokenizerCalls']} → {after[0]['TokenizerCalls']} |")
        text += ["", "当前线程托管分配：", "", "| 非零CP初始数 | 场景 | 单位 | 原版 | 修复版 |", "|---:|---|---|---:|---:|"]
        for key in keys:
            pair = groups[key]
            factor = 1 if pair[BASE][0]["Unit"] == "request" else 1048576
            unit = "B/请求" if factor == 1 else "MiB/事件"
            text.append(f"| {key[1]} | {NAMES[key[2]]} | {unit} | {bytes_per_unit(pair[BASE]) / factor:,.3f} | {bytes_per_unit(pair[CURRENT]) / factor:,.3f} |")
        text.append("")
    text += ["## 测量边界", "",
             "- 模型创建、grammar 加载、包装器构造、snapshot/恢复和显式 GC 在稳态计时外。初态由冻结原版建立，各实现使用相同初态。",
             "- 百万行的真实 TextMate 接近全文初态建立只运行冻结原版一次，其耗时单独留在 JSON seeds 中，不能据此声称当前版首次全文扫描获得相同倍数提升。",
             "- 真实回跳索引8191只重扫8192行；当前检查点保留策略不变，仍不能消除旧区域的全量状态追赶。",
             "- 过渡行仍调用分词器并获取 token；本次减少的是字典写入、遍历、临时数组和重复分词，没有实现 TextMate 状态专用接口、后台分词或全局稀疏检查点。",
             "- 失效轨迹仅 InvalidateFromLine＋重读，不是模型 Replace 或UI端到端延迟。",
             "- 分配仅当前线程托管内存，包含取行、分词和缓存维护，排除原生内存、其它线程和进程峰值。",
             "- 冷跳与回跳工作量相同的场景仍可能因 JIT、CPU频率和后台负载而波动；min/max与每轮样本保存在JSON，微小差异不作强排名。", "",
             "## 复现与数据", "", "运行方法见 [README](README.md)。", ""]
    text += [f"- `{path.as_posix()}`" for path in args.inputs]
    text.append("")
    args.output.write_text("\n".join(text), encoding="utf-8")
    print(args.output)
    for flag in flags:
        print("RECHECK:", flag)


if __name__ == "__main__":
    main()
