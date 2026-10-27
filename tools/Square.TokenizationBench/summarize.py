"""Summarize two or more independent runs of the same benchmark corpus."""
from pathlib import Path
import argparse
import json
import statistics

VARIANTS = ["TokenizationCache", "BBTreeDropInCache", "GuardedDictionaryCache", "SortedListCache", "BBTreeOptimizedCache"]
LABELS = ["原版", "BBTree 直接替换", "SortedDictionary + guard", "SortedList 优化", "BBTree 优化"]
SCENARIOS = {"cached-line": "同一行命中", "cached-viewport": "64 行视口命中", "scroll": "顺序滚动",
             "jumps": "固定种子跳转", "invalidate-tail": "尾部附近失效＋重读", "invalidate-middle": "中部失效＋重读", "cold-jump": "空缓存首次跳转"}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("inputs", nargs="+", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--cpu", default="See raw metadata")
    args = parser.parse_args()
    runs = [json.loads(path.read_text(encoding="utf-8")) for path in args.inputs]
    for run in runs:
        assert run["metadata"].get("Comparison", "legacy") == "legacy", "Use summarize_fix.py for production comparisons"
        for row in run["results"]:
            if row["Variant"] == "BaselineTokenizationCache":
                row["Variant"] = "TokenizationCache"
    assert len({run["metadata"]["CorpusVersion"] for run in runs}) == 1
    assert runs[0]["metadata"]["CorpusVersion"] == "mixed-csharp-v1"
    indexes = [{(row["Profile"], row["Checkpoints"], row["Scenario"], row["Variant"]): row for row in run["results"]} for run in runs]
    assert all(index.keys() == indexes[0].keys() for index in indexes)
    def rows(profile, count, scenario, variant):
        return [index[(profile, count, scenario, variant)] for index in indexes]
    def time(profile, count, scenario, variant):
        return statistics.median(sample for row in rows(profile, count, scenario, variant) for sample in row["SamplesNs"]) / 1000
    def allocation(profile, count, scenario, variant):
        return statistics.median(row["BytesPerUnit"] for row in rows(profile, count, scenario, variant))
    for profile, count, scenario, _ in indexes[0]:
        calls = {(row["TokenizerCalls"], row["TokenizedCharacters"], row["Operations"]) for variant in VARIANTS for row in rows(profile, count, scenario, variant)}
        assert len(calls) == 1, (profile, count, scenario, calls)
    metadata = runs[0]["metadata"]
    text = ["# TokenizationCache 本机性能对比", "", "日期：2026-10-27。生产缓存未修改。", "",
            f"环境：{args.cpu}，{metadata['OS']}，{metadata['Architecture']}，{metadata['Runtime']}，AVX2={metadata['Avx2']}，workstation GC，关闭分层编译；BBTree 0.1.1。", "",
            f"正式数据来自 {len(runs)} 个独立进程，每组每场景各 2 轮预热、{metadata['Rounds']} 轮测量。表格为合并全部样本后的中位数；时间单位均为微秒，越小越好。请求场景是每次 GetLineTokens 的批次平均耗时，失效和冷跳是完整事件耗时。", "",
            "混合 C# 语料版本：`mixed-csharp-v1`。此前 `run-1` / `run-2` 的注释占比过高，仅作探索，未纳入本报告。", "",
            "## 真实 TextMate C#＋生产文本模型", "", "10 万行文档，初始 1024 个非零检查点＋逻辑 0 哨兵。失效不包含模型编辑成本。", "",
            "| 场景／单位 | " + " | ".join(LABELS) + " |", "|---|" + "---:|" * len(VARIANTS)]
    real_scenarios = [scenario for scenario in SCENARIOS if scenario != "cold-jump"]
    for scenario in real_scenarios:
        unit = "次请求" if scenario in ["cached-line", "cached-viewport", "scroll", "jumps"] else "次事件"
        text.append(f"| {SCENARIOS[scenario]}／{unit} | " + " | ".join(f"{time('textmate', 1024, scenario, variant):.3f}" for variant in VARIANTS) + " |")
    text += ["", "分配量（当前线程托管字节／请求或事件，包含整个缓存调用，排除原生内存和初始化／恢复）：", "",
             "| 场景 | " + " | ".join(LABELS) + " |", "|---|" + "---:|" * len(VARIANTS)]
    for scenario in real_scenarios:
        text.append(f"| {SCENARIOS[scenario]} | " + " | ".join(f"{allocation('textmate', 1024, scenario, variant):,.1f}" for variant in VARIANTS) + " |")
    text += ["", "独立进程复测范围（各进程中位数，微秒）：", "",
             "| 场景 | " + " | ".join(LABELS) + " |", "|---|" + "---:|" * len(VARIANTS)]
    for scenario in real_scenarios:
        ranges = []
        for variant in VARIANTS:
            values = [row["MedianNs"] / 1000 for row in rows("textmate", 1024, scenario, variant)]
            ranges.append(f"{min(values):.3f}–{max(values):.3f}")
        text.append(f"| {SCENARIOS[scenario]} | " + " | ".join(ranges) + " |")
    text += ["", "## 合成分词器：隔离缓存维护成本", "", "预生成字符串数组、无分配对象状态分词器、复用 token 数组。K 是非零检查点初始数量。", "",
             "| K | 场景 | " + " | ".join(LABELS) + " |", "|---:|---|" + "---:|" * len(VARIANTS)]
    for count in [64, 512, 1024]:
        for scenario in SCENARIOS:
            text.append(f"| {count} | {SCENARIOS[scenario]} | " + " | ".join(f"{time('synthetic', count, scenario, variant):.3f}" for variant in VARIANTS) + " |")
    text += ["", "## 工作量与正确性", "", "各实现每个场景的重新分词调用次数和 UTF-16 字符数均相同。以下为真实层每个完整计时轨迹的工作量：", "",
             "| 场景 | 请求数 | 分词调用数 | 分词 UTF-16 码元 |", "|---|---:|---:|---:|"]
    for scenario in real_scenarios:
        row = rows("textmate", 1024, scenario, VARIANTS[0])[0]
        text.append(f"| {SCENARIOS[scenario]} | {row['Operations']} | {row['TokenizerCalls']} | {row['TokenizedCharacters']} |")
    text += ["", "构建成功，0 警告、0 错误。合成字符串状态、对象状态以及真实 TextMate 的 token 对照与跨行编辑检查通过；逻辑检查点集合、淘汰后保留范围、行缓存集合和容量检查通过。", "",
             "## 结论与建议", "",
             "1. 最大的可消除成本是热命中前的全量剪枝扫描。只换 BBTree 能减少部分时间和分配；保留 SortedDictionary 并增加安全上界判断，热命中也能接近 0.02 微秒、0 托管字节。这个收益主要来自避免扫描。", "",
             "2. 真实混合 C# 的顺序滚动中，SortedList 与 BBTree 优化版均比原版少约 10% 耗时，两者不足 1% 的中位数差距没有形成稳定优势；两次进程中的先后顺序也不同。随机跳转和失效后重建主要受分词影响，不能据这些小差距推荐新增 BBTree 依赖。", "",
             "3. 热命中收益不能外推到全部路径。合成 K=1024 冷跳中，单独 guard 组由约 7.590 毫秒增至 8.343 毫秒（约 10% 回退）；SortedList 优化组约 7.530 毫秒，BBTree 优化组约 8.040 毫秒。若后续落地修改，必须保留冷跳回归检查。", "",
             "建议优先将 SortedList＋二分前驱＋按需剪枝作为后续实现候选，或者先做范围更小的剪枝修正并验证冷跳；本轮保持生产实现不变。当前数据没有显示引入 BBTree 比内置集合方案更值得。", "",
             "## 解释边界", "", "原版到 BBTree 直接替换版体现当前访问方式下的容器差异。guard 组说明仅减少无意义剪枝扫描也能取得收益。SortedList／BBTree 优化版还改变了前驱定位和失效删除方式，不能把相对原版的全部收益归给容器。", "",
             "热命中仅衡量缓存入口，不能将其提速倍数外推为整个编辑器或整帧提速。真实未命中还包含取行、分词、token 缓存维护；冷跳必须重新分词的工作不会被有序容器消除。", "",
             "单次失效事件只测一次后取多轮中位数，小幅差异容易受调度、CPU 频率和后台负载影响。两次进程复测不是统计置信区间。初始化后的树形状、固定语料和固定轨迹也不能代表长期随机编辑；本次未测 NativeAOT。", "",
             "## 复现与原始数据", "", "运行方法、候选实现说明及初始化方式见 [README](README.md)。`SamplesNs` 按测量轮次顺序保存。", ""]
    for path in args.inputs:
        text.append(f"- `{path.as_posix()}`")
    text += ["", "生产缓存源码及包文件 SHA-256 记录见 `artifacts/tokenization-bench/preparation.json`。", ""]
    args.output.write_text("\n".join(text), encoding="utf-8")
    print(args.output)


if __name__ == "__main__":
    main()
