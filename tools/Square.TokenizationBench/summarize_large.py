"""Summarize the million-line cache benchmark without mixing it with smaller runs."""
from pathlib import Path
import argparse
import json
from summarize import VARIANTS, LABELS

NAMES = {
    "cached-line": "同一行命中",
    "cached-viewport": "64 行视口命中",
    "scroll": "顺序滚动",
    "local-jumps": "4096 行范围内跳转",
    "invalidate-tail": "末尾附近缓存失效＋重读",
    "invalidate-retained-middle": "保留检查点窗口中段失效＋重读",
    "rewind-8192": "回跳到索引 8191，重扫 8192 行",
    "rewind-document-middle": "回跳文档中部，重扫 50 万行",
    "invalidate-document-middle": "文档中部失效＋重读",
    "cold-eof": "空缓存首次跳至 EOF",
}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--cpu", default="See metadata")
    args = parser.parse_args()
    data = json.loads(args.input.read_text(encoding="utf-8"))
    assert data["metadata"].get("Comparison", "legacy") == "legacy", "Use summarize_fix.py for production comparisons"
    for result in data["results"]:
        if result["Variant"] == "BaselineTokenizationCache":
            result["Variant"] = "TokenizationCache"
    metadata, results, seeds = data["metadata"], data["results"], data["seeds"]
    assert {row["Profile"] for row in results} == {"large-synthetic", "large-textmate"}
    assert all(row["DocumentLines"] == 1_000_000 and row["TextCodeUnits"] == 80_999_999 for row in results)
    assert all(seed["TokenizerCalls"] == 995904 and seed["FirstCheckpoint"] == 930432 and seed["LastCheckpoint"] == 995904 for seed in seeds)
    index = {(row["Profile"], row["Scenario"], row["Variant"]): row for row in results}
    def row(profile, scenario, variant):
        return index[(profile, scenario, variant)]
    def time(profile, scenario, variant):
        result = row(profile, scenario, variant)
        divisor = 1_000 if result["Unit"] == "request" else 1_000_000
        return result["MedianNs"] / divisor
    text = ["# TokenizationCache 百万行文本测试", "", "日期：2026-10-27。生产实现未修改。", "",
            f"环境：{args.cpu}，{metadata['Runtime']}，{metadata['OS']}，{metadata['Architecture']}，AVX2={metadata['Avx2']}，workstation GC，关闭分层编译，BBTree 0.1.1。", "",
            "## 文本与初态", "", "本次实际创建生产 CodeEditorTextModel / PieceTable：1,000,000 行，每行 80 个 UTF-16 码元，行间使用 LF，无末尾换行。总长 **80,999,999 个 UTF-16 码元，字符净荷约 154.50 MiB**。这不是文件磁盘大小、进程峰值或全部托管内存。", "",
            "语料版本 mixed-csharp-v1，包含普通 C# 代码、单行注释、跨检查点块注释和跨检查点 verbatim 字符串。两个层次都使用这份真实大文本模型；large-synthetic 只替换为轻量合成分词器，large-textmate 使用生产 TextMate C# 分词器。", "",
            "原版缓存从空状态单次请求索引 995903（第 995904 行），实际先处理 0..995903 共 995904 行。初态随后只保留非零检查点 **930432..995904，间隔 64，共 1024 个**，以及 0 哨兵；行 token 缓存为 995775..995903，共 129 行。这些均有精确断言。", "",
            "## 原版首次接近全文的扫描", "", "以下是每层构建初态时的单次实测，仅测原版，不用于比较各容器。已完成模型创建、加载 grammar 和小文本语义校验；模型初始化垃圾先 GC。计时包含缓存取行、分词及缓存维护，排除模型创建、snapshot 和恢复；有稀疏进度日志。", "",
            "| 分词器 | 初次扫描时间 | 当前线程托管分配 | 处理行数 | 处理 UTF-16 码元 |", "|---|---:|---:|---:|---:|"]
    for seed in seeds:
        name = "TextMate C#" if seed["Profile"] == "large-textmate" else "合成分词器"
        text.append(f"| {name} | {seed['ElapsedMs'] / 1000:.3f} s | {seed['AllocatedBytes'] / 1048576:.2f} MiB | {seed['TokenizerCalls']:,} | {seed['TokenizedCharacters']:,} |")
    text += ["", "## 各实现对比", "",
             f"每组每场景先进行独立的计时外语义检查，再进行 {metadata['WarmupRounds']} 轮预热、{metadata['Rounds']} 轮计量。每轮新建缓存并在计时外恢复同一层原版初态；冷缓存 EOF 场景不恢复。下表为样本中位数。单次事件的 min/max 与逐轮数据保存在 JSON。", ""]
    for profile, title in [("large-textmate", "真实 TextMate C#"), ("large-synthetic", "合成分词器＋生产大文本模型")]:
        scenarios = list(dict.fromkeys(result["Scenario"] for result in results if result["Profile"] == profile))
        text += [f"### {title}", "", "| 场景 | 单位 | " + " | ".join(LABELS) + " |", "|---|---|" + "---:|" * len(VARIANTS)]
        for scenario in scenarios:
            first = row(profile, scenario, VARIANTS[0])
            unit = "微秒／请求" if first["Unit"] == "request" else "毫秒／事件"
            text.append(f"| {NAMES[scenario]} | {unit} | " + " | ".join(f"{time(profile, scenario, variant):.3f}" for variant in VARIANTS) + " |")
        text += ["", "当前线程托管分配：", "", "| 场景 | 单位 | " + " | ".join(LABELS) + " |", "|---|---|" + "---:|" * len(VARIANTS)]
        for scenario in scenarios:
            first = row(profile, scenario, VARIANTS[0])
            factor = 1 if first["Unit"] == "request" else 1048576
            unit = "B／请求" if factor == 1 else "MiB／事件"
            text.append(f"| {NAMES[scenario]} | {unit} | " + " | ".join(f"{row(profile, scenario, variant)['BytesPerUnit'] / factor:,.3f}" for variant in VARIANTS) + " |")
        text += ["", "具体轨迹和分词工作量（行号为 0-based；各实现逐项断言工作量相同）：", "",
                 "| 场景 | 请求数 | 请求索引范围 | 失效起点 | 分词调用数 | 分词 UTF-16 码元 |", "|---|---:|---|---:|---:|---:|"]
        for scenario in scenarios:
            first = row(profile, scenario, VARIANTS[0])
            peers = [row(profile, scenario, variant) for variant in VARIANTS]
            assert len({(peer["TokenizerCalls"], peer["TokenizedCharacters"]) for peer in peers}) == 1
            text.append(f"| {NAMES[scenario]} | {first['Operations']} | {first['MinRequestedLine']}..{first['MaxRequestedLine']} | {first['InvalidateLine']} | {first['TokenizerCalls']:,} | {first['TokenizedCharacters']:,} |")
        text.append("")
    rewind_seconds = [time("large-textmate", "rewind-8192", variant) / 1000 for variant in VARIANTS]
    cold_before = time("large-synthetic", "cold-eof", VARIANTS[0])
    cold_after = time("large-synthetic", "cold-eof", VARIANTS[-1])
    text += ["## 观察结论", "",
             "1. 百万行没有扩大检查点 map 的容量：它始终最多保留 1024 个非零检查点。热命中中的主要可消除开销仍是每次请求的全量剪枝扫描；按需剪枝的内置集合方案和 BBTree 方案都能避免该扫描。", "",
             f"2. 合成分词器的首次 EOF 冷跳中，BBTree 优化版由原版 {cold_before:.3f} 毫秒降至 {cold_after:.3f} 毫秒，观察到约 {(1 - cold_after / cold_before) * 100:.1f}% 的耗时减少；这说明其缓存维护路径在部分大重放场景有收益，但不是 TextMate 整体提速。", "",
             f"3. 真实 TextMate 回跳至索引 8191，各实现都必须从 0 重算 8192 行，耗时中位数范围为 {min(rewind_seconds):.3f}–{max(rewind_seconds):.3f} 秒。更换 map 没有减少重新分词的行数。真实首次接近全文的原版扫描为 {next(seed['ElapsedMs'] for seed in seeds if seed['Profile'] == 'large-textmate') / 1000:.3f} 秒，也是单次观察，不用于容器排名。", "",
             "4. 当前按最小行号淘汰检查点，使接近文件尾部时仅保留最近约 6.55 万行范围和 0 哨兵。若目标是减少大文件长距离回跳卡顿，后续应优先评估能保留旧区域恢复位置的检查点策略，以及同步重新分词的工作量；BBTree 只改变容器维护成本。", "",
             "## 正确性与解释范围", "",
             "- 所有新增百万行轨迹，在计时外逐请求比较完整 TokenSpan 序列，并比较逻辑检查点、行 token 缓存、行末状态键集合和缓存 token 内容；各变体均与原版对照。",
             "- 明确断言回跳文档中部 500000 次分词、文档中部失效 500065 次、EOF 冷跳 1000000 次、真实回跳索引 8191 时 8192 次，以及两种局部失效的 127/128 次。",
             "- 1000000 恰好是 64 的倍数。EOF 冷跳最后生成 checkpoint==LineCount；计时外再次请求 EOF，验证该检查点被清除、token 不变且不新增分词。",
             "- TextMate 回跳索引 8191 的跳转距离约 98.8 万行，但实际重新分词只有 8192 行。真实文档中部 50 万行重扫未逐容器重复测量；百万行模型上接近全文的 995904 行 TextMate 扫描只测原版建立初态那一次。",
             "- 缓存失效场景只计 InvalidateFromLine＋重读，不包含模型 Replace，不是完整输入或渲染延迟。",
             "- 合成分词器本身复用状态与 token，但生产取行和缓存维护仍会分配。表中只计当前线程托管分配，排除原生内存、其它线程、构造／恢复和进程峰值。",
             f"- 每场景 {metadata['Rounds']} 轮观察值只支持明显趋势；频率、后台负载、长期碎片化、其它语言、NativeAOT 和真实编辑访问分布仍可能改变结果。", "",
             "## 复现", "", "```powershell", "./tools/Square.TokenizationBench/Run.ps1 -Python python -Profile large -Rounds 3 -OutputDirectory artifacts/tokenization-bench/large-1", "python tools/Square.TokenizationBench/summarize_large.py artifacts/tokenization-bench/large-1/results.json --cpu \"Intel Core i5-12400F\" --output tools/Square.TokenizationBench/LARGE-RESULTS.md", "```", "",
             f"原始数据：`{args.input.as_posix()}`，以及同目录 results.csv。", ""]
    args.output.write_text("\n".join(text), encoding="utf-8")
    print(args.output)


if __name__ == "__main__":
    main()
