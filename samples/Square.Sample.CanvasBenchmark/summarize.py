"""Validate preserved Canvas runs and publish versioned reports with evidence-gated Vulkan status."""
import argparse
import csv
import html
import hashlib
import io
import json
import math
import os
import re
import statistics
from collections import defaultdict
from pathlib import Path
from urllib.parse import quote

NAMES = {"square-software": "Square Software", "square-direct2d": "Square Direct2D", "square-vulkan": "Square Vulkan", "electron": "Electron", "flutter": "Flutter", "avalonia": "Avalonia"}
COLORS = {"square-software": "#e05a47", "square-direct2d": "#2284c7", "square-vulkan": "#64748b", "electron": "#9e6bd1", "flutter": "#1d9d80", "avalonia": "#c28a1e"}
REPOSITORY = Path(__file__).resolve().parents[2]
GPU_PREFIX = "src/square.backends.vulkan/"
GPU_REQUIRED = {GPU_PREFIX + name for name in ("vulkanrendercontext.cs", "vulkanbatchrenderer.cs", "vulkanbrushmaterial.cs", "vulkanpipeline.cs", "vulkantextureatlas.cs", "vulkangeometryclipper.cs", "vulkanoffscreenlayer.cs", "vulkanswapchain.cs", "vulkandevice.cs", "embeddedshaders.cs", "shaders/vert2d.glsl", "shaders/frag2d.glsl")}


def load_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def next_version(directory):
    versions = []
    for folder in (REPOSITORY / "artifacts/canvas-benchmark", REPOSITORY / "artifacts/canvas-optimization", directory):
        if folder.is_dir():
            for path in folder.rglob("report-v*.md"):
                match = re.fullmatch(r"report-v(\d+)\.md", path.name)
                if match:
                    versions.append(int(match[1]))
    return max(5, max(versions, default=0) + 1)


def finite(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def quantile(values, fraction):
    if not values:
        return None
    values = sorted(values)
    position = (len(values) - 1) * fraction
    index = int(position)
    return values[index] + (values[min(index + 1, len(values) - 1)] - values[index]) * (position - index)


def hardware_field(environment, kind, field):
    items = environment.get(kind, {})
    return " / ".join(str(item.get(field, "未记录")) for item in (items if isinstance(items, list) else [items]))


def hash_map(records):
    if isinstance(records, dict):
        records = [{"Path": path, "Hash": value} for path, value in records.items()]
    if not isinstance(records, list):
        raise ValueError("sourceHashes must be a path/hash mapping or [{Path,Hash}] list")
    output = {}
    for item in records:
        path = str(item.get("Path", item.get("path", ""))).replace("\\", "/").casefold()
        marker = path.find(GPU_PREFIX)
        if marker >= 0:
            path = path[marker:]
        digest = str(item.get("Hash", item.get("hash", ""))).casefold()
        if not path or not re.fullmatch(r"[0-9a-f]{64}", digest):
            raise ValueError("invalid source hash entry")
        if path in output and output[path] != digest:
            raise ValueError(f"conflicting source snapshots: {path}")
        output[path] = digest
    return output


def existing_path(value, base):
    if not value:
        return None
    path = Path(value)
    candidates = [path] if path.is_absolute() else [base / path, REPOSITORY / path]
    return next((candidate.resolve() for candidate in candidates if candidate.is_file()), None)


def feature_verification(root, environment, collection_version, explicit=None, explicit_log=None):
    candidates = [explicit.resolve()] if explicit else [root / f"feature-verification-v{collection_version}.json", root.parent / f"preflight-v{collection_version}/feature-verification-v{collection_version}.json", REPOSITORY / f"artifacts/canvas-optimization/preflight-v{collection_version}/feature-verification-v{collection_version}.json"]
    path = next((candidate for candidate in candidates if candidate.is_file()), None)
    result = {"passed": False, "path": path, "logs": [], "canvasPath": None, "canvasImage": None, "negativeImage": None, "reasons": [], "sourceFilesMatched": 0, "nativePassed": 0, "resourcesPassed": 0, "unitPassed": None, "pipelinePassed": None, "sameImageCases": 0, "canvasComparisons": 0, "checkedPixels": 0, "maxChannelError": None, "edgeExclusion": None, "negativeMismatches": 0}
    if not path:
        result["reasons"].append("缺少真实GPU feature-verification JSON，不能从版本号或截图推断通过。")
        return result
    try:
        data = load_json(path)
        if str(data.get("status", "")).casefold() != "passed":
            result["reasons"].append("GPU验证总状态不是 passed。")
        test_records = [("nativeTests", "nativePassed"), ("resourcesTests", "resourcesPassed")]
        if "unitTests" in data:
            test_records.append(("unitTests", "unitPassed"))
        if collection_version is not None and collection_version >= 5:
            test_records.append(("pipelineTests", "pipelinePassed"))
            equal_cases = data.get("sameImageAgainstV4", [])
            keys = {(item.get("phase"), item.get("load")) for item in equal_cases}
            expected = {(phase, load) for phase in (0, 17, 37) for load in (1, 32)}
            if len(equal_cases) != 6 or keys != expected or any(item.get("differentPixels") != 0 or item.get("maxChannelError") != 0 for item in equal_cases):
                result["reasons"].append("v5与v4六张完整RGBA GPU图尚未逐像素相同。")
            else:
                result["sameImageCases"] = 6
        for name, passed_field in test_records:
            tests = data.get(name, {})
            counts = tuple(tests.get(key) for key in ("passed", "failed", "skipped", "errors"))
            valid_counts = all(isinstance(value, int) and not isinstance(value, bool) for value in counts)
            if not valid_counts or counts[0] <= 0 or any(counts[1:]):
                result["reasons"].append(f"{name} 必须实跑 passed>0，failed/skipped/errors=0。")
            else:
                result[passed_field] = counts[0]
            log_value = explicit_log if name == "nativeTests" and explicit_log else tests.get("log")
            log = existing_path(log_value, path.parent)
            if not log:
                result["reasons"].append(f"{name} 缺少实际通过日志。")
            else:
                result["logs"].append(log)
                log_text = log.read_text(encoding="utf-8-sig")
                matches = re.findall(r"RESULT passed=(\d+) failed=(\d+) skipped=(\d+) errors=(\d+)", log_text)
                if name == "nativeTests" and "Real Vulkan GPU gate=1" not in log_text:
                    result["reasons"].append("nativeTests 日志未确认真实GPU gate=1。")
                if not matches or not valid_counts or tuple(map(int, matches[-1])) != counts:
                    result["reasons"].append(f"{name} 日志末次RESULT与声明计数不匹配。")
        canvas = data.get("canvasVisual", {})
        comparisons = canvas.get("comparisons", [])
        count = len(comparisons) if isinstance(comparisons, list) else comparisons if isinstance(comparisons, int) else 0
        result["canvasComparisons"] = count
        result["canvasPath"] = existing_path(canvas.get("path"), path.parent)
        if canvas.get("verified") is not True or count < 6 or not result["canvasPath"]:
            result["reasons"].append("Canvas六组画面矩阵尚未 verified=true、comparisons>=6 或缺少真实证据文件。")
        if result["canvasPath"]:
            visual = load_json(result["canvasPath"])
            actual = visual.get("comparisons", [])
            expected_cases = {(phase, load) for phase in (0, 17, 37) for load in (1, 32)}
            actual_cases = {(item.get("phase"), item.get("load")) for item in actual}
            if visual.get("allPassed") is not True or len(actual) != count or not expected_cases.issubset(actual_cases) or any(item.get("passed") is not True or item.get("mismatchedPixels") != 0 or item.get("checkedPixels", 0) <= 0 for item in actual):
                result["reasons"].append("Canvas实际JSON未完整通过phase0/17/37 × load1/32六组曲线ROI。")
            if str(visual.get("sceneSha256", "")).casefold() != str(environment.get("sceneSha256", "")).casefold():
                result["reasons"].append("Canvas验证与实测scene哈希不匹配。")
            result["checkedPixels"] = sum(item.get("checkedPixels", 0) for item in actual)
            result["maxChannelError"] = max((item.get("maxChannelError", 0) for item in actual), default=None)
            result["edgeExclusion"] = visual.get("edgeExclusionDistancePixels")
            negative = visual.get("negativeControl", {})
            result["negativeMismatches"] = negative.get("result", {}).get("mismatchedPixels", 0)
            if negative.get("rejectedAsExpected") is not True or result["negativeMismatches"] <= 0:
                result["reasons"].append("已知错误v3负对照未被实际拒绝。")
            captures = actual + [negative.get("result", {})]
            for capture_index, capture in enumerate(captures):
                image = existing_path(capture.get("image"), result["canvasPath"].parent)
                if not image or hashlib.sha256(image.read_bytes()).hexdigest() != capture.get("sha256"):
                    result["reasons"].append("Canvas GPU图像不存在或已改变：" + str(capture.get("image")))
                elif capture_index == 0:
                    result["canvasImage"] = image
                elif capture_index == len(actual):
                    result["negativeImage"] = image
        recorded = {key: value for key, value in hash_map(environment.get("implementationSha256", [])).items() if key.startswith(GPU_PREFIX)}
        tested = hash_map(data.get("sourceHashes", []))
        if not GPU_REQUIRED.issubset(recorded):
            result["reasons"].append("采集环境未保存完整GPU实现/GLSL快照。")
        mismatches = [key for key, value in recorded.items() if tested.get(key) != value]
        result["sourceFilesMatched"] = len(recorded) - len(mismatches)
        if mismatches:
            result["reasons"].append("测试与采集GPU源码快照不匹配/缺失：" + ", ".join(mismatches))
    except (OSError, ValueError, TypeError, AttributeError) as error:
        result["reasons"].append(f"验证文件无效：{error}")
    result["passed"] = not result["reasons"]
    return result


def vulkan_observation(data, required=False):
    g = data.get("vulkanFrameDiagnostics")
    if g is None and not required and data.get("measurementVersion", 0) < 5:
        return None
    if data.get("measurementVersion") != 5 or not isinstance(g, dict):
        raise SystemExit("Vulkan v5 requires explicit measurementVersion=5 and fence diagnostics")
    snapshots = [g.get(name) for name in ("before", "atDeadline", "afterDrain")]
    if any(not isinstance(snapshot, dict) or any(not isinstance(snapshot.get(key), int) or isinstance(snapshot.get(key), bool) or snapshot[key] < 0 for key in ("SubmittedFrames", "CompletedFrames", "PendingFrames", "FramesInFlight")) for snapshot in snapshots):
        raise SystemExit("Invalid Vulkan cumulative fence snapshots")
    b, a, d = snapshots
    for key in ("submittedFrames", "completedFrames", "apiReturnsObserved", "pendingAtDeadline", "framesInFlight"):
        if not isinstance(g.get(key), int) or isinstance(g.get(key), bool) or g[key] < 0:
            raise SystemExit(f"Invalid Vulkan observation {key}")
    for key in ("configuredSeconds", "observationSeconds", "completedFps", "warmupDrainMilliseconds", "endDrainMilliseconds"):
        if not finite(g.get(key)) or g[key] < 0:
            raise SystemExit(f"Invalid Vulkan observation {key}")
    if not (g["observationSeconds"] >= data["seconds"] > 0 and abs(g["configuredSeconds"] - data["seconds"]) < 1e-6 and
            b["PendingFrames"] == 0 and b["SubmittedFrames"] == b["CompletedFrames"] and
            all(snapshot["FramesInFlight"] == 2 for snapshot in snapshots) and g["framesInFlight"] == 2 and
            a["SubmittedFrames"] >= b["SubmittedFrames"] and a["CompletedFrames"] >= b["CompletedFrames"] and
            a["PendingFrames"] == a["SubmittedFrames"] - a["CompletedFrames"] == g["pendingAtDeadline"] and a["PendingFrames"] <= 2 and
            g["submittedFrames"] == a["SubmittedFrames"] - b["SubmittedFrames"] > 0 and
            g["completedFrames"] == a["CompletedFrames"] - b["CompletedFrames"] <= g["submittedFrames"] and
            data["frames"] <= g["apiReturnsObserved"] <= data["frames"] + 1 and g["submittedFrames"] <= g["apiReturnsObserved"] and
            abs(g["completedFps"] - g["completedFrames"] / g["observationSeconds"]) < 1e-6 and
            d["PendingFrames"] == 0 and d["SubmittedFrames"] == a["SubmittedFrames"] and d["CompletedFrames"] == d["SubmittedFrames"]):
        raise SystemExit("Inconsistent Vulkan deadline/delta/duration/drain accounting; do not treat queued or drained work as observed completions")
    return g


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--version", type=int)
    parser.add_argument("--visual-directory", type=Path)
    parser.add_argument("--feature-verification", type=Path)
    parser.add_argument("--feature-log", type=Path)
    args = parser.parse_args()
    root = args.directory.resolve()
    environment = load_json(root / "environment.json")
    manifest = load_json(root / "manifest.json")
    version = args.version if args.version is not None else environment.get("reportVersion", next_version(root))
    if not isinstance(version, int) or isinstance(version, bool) or version < 1:
        parser.error("Report version must be a positive integer")
    collection_version = environment.get("reportVersion")
    if not isinstance(collection_version, int) or isinstance(collection_version, bool) or collection_version < 1:
        collection_version = None
    suffix = f"-v{version}"
    files = {key: f"{stem}{suffix}.{extension}" for key, stem, extension in (("md", "report", "md"), ("html", "report", "html"), ("csv", "summary", "csv"), ("json", "summary", "json"), ("svg", "fps", "svg"))}
    for name in files.values():
        if (root / name).exists():
            raise SystemExit(f"Preserve existing report output; choose a new --version: {root / name}")
    declared = [target["name"] for target in environment["targets"]]
    if not declared or len(set(declared)) != len(declared) or set(declared) - NAMES.keys():
        raise SystemExit("Unsupported/duplicate standard-suite targets")
    targets = [target for target in NAMES if target in declared]
    loads = sorted(environment["loads"])
    repetitions = environment["repetitions"]
    if not loads or len(set(loads)) != len(loads) or any(not isinstance(load, int) or load < 1 for load in loads) or not isinstance(repetitions, int) or repetitions < 1:
        raise SystemExit("Invalid loads/repetitions")
    if not finite(environment["seconds"]) or environment["seconds"] <= 0:
        raise SystemExit("Invalid measurement duration")
    groups = defaultdict(list)
    seen = set()
    for entry in manifest:
        key = (entry["target"], entry["load"], entry["repeat"])
        if not entry["valid"] or entry.get("exitCode", 0) != 0 or entry.get("timedOut", False) or key in seen or entry["target"] not in targets or entry["load"] not in loads or entry["repeat"] not in range(1, repetitions + 1):
            raise SystemExit(f"Invalid/duplicated sample: {entry}")
        seen.add(key)
        if "raised" in entry and (not entry["raised"] or not entry.get("observedTitle")):
            raise SystemExit(f"Unconfirmed visible window: {entry['result']}")
        data = load_json(root / entry["result"])
        for field in ("fps", "seconds", "frames", "clientWidth", "clientHeight", "devicePixelRatio"):
            if not finite(data.get(field)):
                raise SystemExit(f"Invalid {field}: {entry['result']}")
        if data["clientWidth"] != 800 or data["clientHeight"] != 600 or data["devicePixelRatio"] != 1:
            raise SystemExit(f"Unexpected viewport/DPI: {entry['result']}")
        if not isinstance(data["frames"], int) or data["frames"] <= 0 or data["seconds"] <= 0 or data.get("load") != entry["load"] or abs(data["fps"] - data["frames"] / data["seconds"]) > 1e-6:
            raise SystemExit(f"Inconsistent frame/load/FPS: {entry['result']}")
        if len(data["frameIntervalsMs"]) != data["frames"] - 1 or len(data["drawTimesMs"]) != data["frames"]:
            raise SystemExit(f"Invalid timing array lengths: {entry['result']}")
        timing_keys = ("frameIntervalsMs", "drawTimesMs", "uiDrawTimesMs") if "uiDrawTimesMs" in data else ("frameIntervalsMs", "drawTimesMs")
        if any(not finite(value) or value < 0 for field in timing_keys for value in data[field]):
            raise SystemExit(f"Invalid timing values: {entry['result']}")
        if abs(data["seconds"] - environment["seconds"]) > 0.0001:
            raise SystemExit(f"Duration differs from contract: {entry['result']}")
        limitations = data.get("renderingLimitations", [])
        if not isinstance(limitations, list) or not all(isinstance(item, str) for item in limitations):
            raise SystemExit(f"Invalid root renderingLimitations: {entry['result']}")
        if entry["target"] == "square-vulkan":
            vk = data.get("versions", {}).get("vulkan", {})
            if vk.get("device", {}).get("DeviceType") not in ("DiscreteGpu", "IntegratedGpu"):
                raise SystemExit(f"Vulkan is not a hardware GPU: {entry['result']}")
            if vk.get("readbackEnabled") is not False or vk.get("validationEnabled") is not False:
                raise SystemExit(f"Vulkan performance requires readbackEnabled=false, validationEnabled=false: {entry['result']}")
            vulkan_observation(data, required=collection_version is not None and collection_version >= 5)
        data["source"] = entry["result"]
        groups[entry["target"], entry["load"]].append(data)
    if len(manifest) != len(targets) * len(loads) * repetitions:
        raise SystemExit("Incomplete run")
    visual_roots = [args.visual_directory.resolve()] if args.visual_directory else [root / f"ui-v{collection_version}", root.parent / f"ui-v{collection_version}", REPOSITORY / f"artifacts/canvas-optimization/ui-v{collection_version}"]
    visual_path = next((folder / f"window-titles-v{collection_version}.json" for folder in visual_roots if (folder / f"window-titles-v{collection_version}.json").is_file()), None)
    evidence = {}
    if visual_path:
        visual = load_json(visual_path)
        visual_rows = visual if isinstance(visual, list) else visual["targets"]
        for item in visual_rows:
            if item["target"] in evidence:
                raise SystemExit("Duplicate window evidence")
            evidence[item["target"]] = item
        for target in targets:
            item = evidence.get(target, {})
            if not item.get("hasCaption") or item.get("client") != [800, 600] or not (visual_path.parent / item.get("screenshot", "")).is_file():
                raise SystemExit(f"Incomplete actual window evidence: {target}")
    elif args.visual_directory or "square-vulkan" in targets:
        raise SystemExit(f"Real window evidence required: window-titles-v{collection_version}.json; supply --visual-directory")
    verification = feature_verification(root, environment, collection_version, args.feature_verification, args.feature_log) if "square-vulkan" in targets else {"passed": False, "reasons": [], "path": None, "logs": [], "canvasPath": None, "sourceFilesMatched": 0, "nativePassed": 0, "resourcesPassed": 0, "unitPassed": None, "pipelinePassed": None, "sameImageCases": 0, "canvasComparisons": 0, "checkedPixels": 0, "maxChannelError": None, "edgeExclusion": None, "negativeMismatches": 0}
    declarations = {}
    comparable = {}
    target_limits = {}
    for target in targets:
        runs = [data for load in loads for data in groups[target, load]]
        declarations[target] = all((data.get("renderingEquivalent") is True) if target == "square-vulkan" else (data.get("renderingEquivalent", True) is True) for data in runs)
        comparable[target] = declarations[target] and (verification["passed"] if target == "square-vulkan" else True)
        target_limits[target] = list(dict.fromkeys(item for data in runs for item in data.get("renderingLimitations", [])))
    rows = []
    for target in targets:
        for load in loads:
            runs = groups[target, load]
            if len(runs) != repetitions:
                raise SystemExit(f"Missing repeats: {target}/{load}")
            intervals = [value for data in runs for value in data["frameIntervalsMs"]]
            draws = [statistics.mean(data.get("uiDrawTimesMs", data["drawTimesMs"])) for data in runs if data.get("uiDrawTimesMs", data["drawTimesMs"])]
            rasters = [statistics.mean(data["drawTimesMs"]) for data in runs if target == "flutter" and data["drawTimesMs"]]
            observations = [data["vulkanFrameDiagnostics"] for data in runs if target == "square-vulkan" and data.get("vulkanFrameDiagnostics") is not None]
            if observations and len(observations) != len(runs):
                raise SystemExit("Mixed legacy and fence-observed Vulkan measurements in one group")
            reasons = []
            if target == "square-vulkan":
                if not declarations[target]:
                    reasons.append("本轮至少一个原始root renderingEquivalent不是显式true，整组保持受限。")
                reasons += verification["reasons"]
            rows.append({"reportVersion": version, "collectionVersion": collection_version, "target": target, "framework": NAMES[target], "load": load,
                         "repetitions": len(runs), "fpsMedian": statistics.median(data["fps"] for data in runs), "fpsMin": min(data["fps"] for data in runs), "fpsMax": max(data["fps"] for data in runs),
                         "fpsMetric": "fixed-window API return / queued rendering" if observations else "raw event definition; see metric",
                         "gpuCompletedFpsMedian": statistics.median(item["completedFps"] for item in observations) if observations else None,
                         "gpuCompletedFpsMin": min((item["completedFps"] for item in observations), default=None),
                         "gpuCompletedFpsMax": max((item["completedFps"] for item in observations), default=None),
                         "gpuObservationSeconds": [item["observationSeconds"] for item in observations],
                         "gpuCompletedFrames": [item["completedFrames"] for item in observations],
                         "gpuSubmittedFrames": [item["submittedFrames"] for item in observations],
                         "gpuPendingAtDeadline": [item["pendingAtDeadline"] for item in observations],
                         "gpuEndDrainMilliseconds": [item["endDrainMilliseconds"] for item in observations],
                         "gpuWarmupDrainMilliseconds": [item["warmupDrainMilliseconds"] for item in observations],
                         "gpuApiReturnsObserved": [item["apiReturnsObserved"] for item in observations],
                         "framesInFlight": 2 if observations else None,
                         "intervalP50Ms": quantile(intervals, .5), "intervalP95Ms": quantile(intervals, .95), "intervalP99Ms": quantile(intervals, .99),
                         "drawMeanMsMedian": statistics.median(draws) if draws else None, "rasterMeanMsMedian": statistics.median(rasters) if rasters else None,
                         "renderingEquivalent": declarations[target], "rawRenderingEquivalent": [data.get("renderingEquivalent") for data in runs],
                         "featureVerificationPassed": verification["passed"] if target == "square-vulkan" else None,
                         "qualityComparable": comparable[target], "renderingLimitations": target_limits[target], "verificationReasons": reasons,
                         "qualityNote": ("共同场景功能可比；并非逐像素或统一GPU计数" if comparable[target] else "功能受限/未验证，不参与同画质比较"),
                         "metric": runs[0]["metric"], "sources": [data["source"] for data in runs]})
    lookup = {(row["target"], row["load"]): row for row in rows}
    labels = {target: NAMES[target] + ("（受限/未验证）" if not comparable[target] else "") for target in targets}
    has_vulkan = "square-vulkan" in targets
    has_fence_observation = any(row["framesInFlight"] == 2 for row in rows)
    if has_fence_observation:
        labels["square-vulkan"] += " API queued"
    if has_vulkan and comparable["square-vulkan"]:
        warning = "Vulkan所有原始样本显式声明true，真实GPU功能/资源/六组Canvas验证通过且源码快照匹配，本轮可按共同场景功能比较；字体、AA、采样与计数阶段仍有差异，不是严格逐像素或GPU吞吐排名。"
    elif has_vulkan:
        warning = "Vulkan仍标为受限/未验证：只有全部原始root显式true，加真实GPU验证passed和匹配源码快照才可移除此标签；false或缺省的旧v3数据绝不自动升级。原始限制见下文。"
    else:
        warning = "历史五方数据重新汇总；未新增Vulkan样本，跨框架画质与计数阶段差异仍保留。"
    fps_table = "| 框架 | " + " | ".join(f"{load}×" for load in loads) + " | 资格说明 |\n|---|" + "---:|" * len(loads) + "---|\n"
    for target in targets:
        fps_table += "| " + labels[target] + " | " + " | ".join(f"{lookup[target, load]['fpsMedian']:.2f}" for load in loads) + " | " + lookup[target, loads[0]]["qualityNote"] + " |\n"
    gpu_table = gpu_html = ''
    if has_fence_observation:
        gpu_table = '| 负载 | API返回FPS（固定窗口） | fence完成FPS中位数 | 实际观测秒（三轮） | 完成/提交（三轮） | 截止pending | 计时外drain ms |\n|---:|---:|---:|---|---|---|---|\n'
        gpu_html = '<table><tr><th>负载</th><th>API返回FPS</th><th>fence完成FPS</th><th>实际观测秒</th><th>完成/提交</th><th>截止pending</th><th>计时外drain ms</th></tr>'
        for load in loads:
            row = lookup['square-vulkan', load]
            values = [f'{load}×', f"{row['fpsMedian']:.2f}", f"{row['gpuCompletedFpsMedian']:.2f}",
                      ' / '.join(f'{value:.6f}' for value in row['gpuObservationSeconds']),
                      ' / '.join(f'{done}/{queued}' for done, queued in zip(row['gpuCompletedFrames'], row['gpuSubmittedFrames'])),
                      ' / '.join(map(str, row['gpuPendingAtDeadline'])), ' / '.join(f'{value:.3f}' for value in row['gpuEndDrainMilliseconds'])]
            gpu_table += '| ' + ' | '.join(values) + ' |\n'
            gpu_html += '<tr>' + ''.join('<td>' + html.escape(value) + '</td>' for value in values) + '</tr>'
        gpu_html += '</table>'
    width, height, left, right, top, bottom = 1060, 550, 78, 300, 58, 78
    plot_width, plot_height = width - left - right, height - top - bottom
    maximum = max(65, max(row["fpsMax"] for row in rows) * 1.05)
    def x(load):
        return left + math.log2(load / loads[0]) / math.log2(loads[-1] / loads[0]) * plot_width if len(loads) > 1 else left
    def y(fps):
        return top + (1 - fps / maximum) * plot_height
    chart_note = "Vulkan: shared features verified; counting stages differ" if has_vulkan and comparable["square-vulkan"] else "Vulkan: limited/unverified, no equal-quality ranking" if has_vulkan else "Observed event rates; counting stages differ"
    if has_fence_observation:
        chart_note = "Vulkan curve = queued/API returns; GPU fence-observed rates shown separately"
    svg = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}" role="img" aria-label="{chart_note}"><rect width="100%" height="100%" fill="white"/><g font-family="Segoe UI,Arial,sans-serif" font-size="13" fill="#334155">', f'<text x="78" y="25">{chart_note}</text>']
    for tick in range(0, math.ceil(maximum), 10):
        svg.append(f'<path d="M{left},{y(tick):.1f}H{width-right}" stroke="#e2e8f0"/><text x="{left-12}" y="{y(tick)+4:.1f}" text-anchor="end">{tick}</text>')
    for load in loads:
        svg.append(f'<text x="{x(load):.1f}" y="{height-bottom+24}" text-anchor="middle">{load}x</text>')
    svg.append(f'<path d="M{left},{y(60):.1f}H{width-right}" stroke="#64748b" stroke-dasharray="5 5"/><text x="{left+8}" y="{y(60)-9:.1f}">60 Hz reference</text>')
    for index, target in enumerate(targets):
        points = [row for row in rows if row["target"] == target]
        coordinates = " ".join(f'{x(row["load"]):.1f},{y(row["fpsMedian"]):.1f}' for row in points)
        dash = ' stroke-dasharray="7 5"' if not comparable[target] else ""
        svg.append(f'<polyline points="{coordinates}" fill="none" stroke="{COLORS[target]}" stroke-width="2.5"{dash}/>')
        for row in points:
            tooltip = f'{labels[target]} {row["load"]}x: {row["fpsMedian"]:.2f} API/event FPS; {row["qualityNote"]}'
            svg.append(f'<circle cx="{x(row["load"]):.1f}" cy="{y(row["fpsMedian"]):.1f}" r="4" fill="{COLORS[target]}"><title>{html.escape(tooltip)}</title></circle>')
        svg.append(f'<path d="M{width-right+20},{top+index*29}h20" stroke="{COLORS[target]}" stroke-width="3"{dash}/><text x="{width-right+48}" y="{top+index*29+5}">{html.escape(labels[target])}</text>')
    svg += [f'<text x="{left+plot_width/2}" y="{height-15}" text-anchor="middle">Repeated full scene draws per frame (log2)</text>', f'<text x="18" y="{top+plot_height/2}" transform="rotate(-90 18 {top+plot_height/2})" text-anchor="middle">Observed FPS, median of {repetitions} runs</text>', '</g></svg>']
    svg_text = "\n".join(svg)
    def link(path):
        return quote(os.path.relpath(path, root).replace(os.sep, "/"), safe="/.-_")
    baseline_table = baseline_html = ''
    if has_fence_observation and comparable['square-vulkan'] and verification['sameImageCases'] == 6:
        baseline_path = next((path for path in (root.parent / 'run-v4/summary-v4.json', REPOSITORY / 'artifacts/canvas-optimization/run-v4/summary-v4.json') if path.is_file()), None)
        if baseline_path:
            old_environment = load_json(baseline_path.parent / 'environment.json')
            old_rows = {row['load']: row for row in load_json(baseline_path) if row['target'] == 'square-vulkan' and row.get('collectionVersion') == 4 and row.get('qualityComparable') is True}
            same_inputs = all(str(old_environment.get(key, '')).casefold() == str(environment.get(key, '')).casefold() for key in ('sceneSha256', 'iconSha256'))
            if same_inputs and set(loads).issubset(old_rows):
                baseline_table = f"旧数据来源：[v4 summary](<{link(baseline_path)}>)；输入哈希一致。三列边界/时长保留；完成率变化只用v5 fence / v4 queue-idle后renderdone参考，不用queued列计算收益。\n\n| 负载 | v4 API完成含queue-idle | v5 API queued | v5 fence观测完成 | 完成观察率变化 |\n|---:|---:|---:|---:|---:|\n"
                baseline_html = '<table><tr><th>负载</th><th>v4 API完成含queue-idle</th><th>v5 API queued</th><th>v5 fence观测完成</th><th>完成观察率变化</th></tr>'
                for load in loads:
                    row = lookup['square-vulkan', load]
                    values = [f'{load}×', f"{old_rows[load]['fpsMedian']:.2f}", f"{row['fpsMedian']:.2f}", f"{row['gpuCompletedFpsMedian']:.2f}", f"{(row['gpuCompletedFpsMedian'] / old_rows[load]['fpsMedian'] - 1) * 100:+.1f}%"]
                    baseline_table += '| ' + ' | '.join(values) + ' |\n'
                    baseline_html += '<tr>' + ''.join('<td>' + value + '</td>' for value in values) + '</tr>'
                baseline_html += '</table><p>来源：<a href="' + link(baseline_path) + '">v4 summary</a>。输入哈希与六张图像一致；变化率只用fence完成列对v4 queue-idle后renderdone参考，保留fixed/poll窗口差异，不代表GPU执行时间缩短或scanout提速。</p>'
    versions_md = []
    for target in targets:
        versions = groups[target, loads[0]][0].get("versions", {})
        if target.startswith("square-"):
            detail = f"Square {versions.get('square')}; .NET {versions.get('dotnet')}; {versions.get('context')}"
            if target == "square-vulkan":
                vk = versions["vulkan"]; device = vk["device"]; api = device.get("ApiVersion")
                supported = f"{(api >> 22) & 127}.{(api >> 12) & 1023}.{api & 4095} (raw {api})" if isinstance(api, int) else str(api)
                detail += f"; {device.get('Name')}/{device.get('DeviceType')}; 设备支持API={supported}; 应用请求API=1.1; driverVersion(raw/vendor-specific)={device.get('DriverVersion')}; MSAA={vk.get('colorSampleCount')}; readback={vk.get('readbackEnabled')}; validation={vk.get('validationEnabled')}"
        elif target == "electron":
            aux = versions.get("gpuInfo", {}).get("auxAttributes", {})
            detail = f"Electron {versions.get('electron')}/Chromium {versions.get('chrome')}; {aux.get('glRenderer')}; Skia {aux.get('skiaBackendType')}; Canvas2D {versions.get('gpuFeatureStatus', {}).get('2d_canvas')}; sandbox={versions.get('chromiumProcessSandbox')}"
        elif target == "flutter":
            logs = set()
            for run in groups[target, loads[0]]:
                path = root / Path(run["source"]).with_suffix(".stderr.log")
                if path.is_file():
                    logs.update(line.strip() for line in path.read_text(encoding="utf-8-sig").splitlines() if "Using the " in line and "rendering backend" in line)
            detail = f"Flutter {versions.get('flutter')}/Dart {versions.get('dart', '').split(' ')[0]}; Release; 启动日志 {' / '.join(sorted(logs)) or '未取得'}"
        else:
            detail = f"Avalonia {versions.get('avalonia', '').split('+')[0]}; .NET {versions.get('dotnet')}; 具体GPU后端见原始启动信息"
        versions_md.append(f"- **{labels[target]}**：{detail}")
    evidence_md = "历史数据未附窗口证明，不补写验证通过。\n"
    evidence_html = "<p>历史数据未附本轮窗口证明。</p>"
    if evidence:
        evidence_md = f"核对清单：[window-titles-v{collection_version}.json](<{link(visual_path)}>)。\n\n| 目标 | 实际标题 | 窗口证据 |\n|---|---|---|\n"
        evidence_html = '<ul>'
        for target in targets:
            item = evidence[target]; image = link(visual_path.parent / item["screenshot"])
            evidence_md += f"| {labels[target]} | {item['title'].replace('|', chr(92)+'|')} | [截图](<{image}>) |\n"
            evidence_html += f'<li><a href="{image}">{html.escape(item["title"])}</a> — {html.escape(labels[target])}</li>'
        evidence_html += '</ul>'
    verification_md = verification_html = ''
    if has_vulkan:
        proof_state = 'passed' if verification['passed'] else '未完成/不匹配'
        proof_links = []
        if verification['path']:
            proof_links.append((verification['path'], 'GPU功能验证JSON'))
        proof_links += [(path, '测试通过日志') for path in verification['logs']]
        if verification['canvasPath']:
            proof_links.append((verification['canvasPath'], 'Canvas六组核对JSON'))
        verification_md = f"验证状态：**{proof_state}**；native passed={verification['nativePassed']}，resources passed={verification['resourcesPassed']}，unit passed={verification['unitPassed']}，pipeline passed={verification['pipelinePassed']}，Canvas comparisons={verification['canvasComparisons']}，匹配GPU源码文件={verification['sourceFilesMatched']}。\n\n"
        roi_note = f"Canvas检查范围是曲线面板ROI，排除边缘{verification['edgeExclusion']}px；合计检查{verification['checkedPixels']}像素，最大通道误差{verification['maxChannelError']}，已知错误v3负对照有{verification['negativeMismatches']}个不匹配像素并被拒绝。该检查不声称全帧/边缘逐像素一致。"
        if verification['sameImageCases'] == 6:
            roi_note += ' 另有v5/v4六张完整800×600 RGBA图逐像素相同检查，合计288万像素0差异；这不代表跨框架像素相同，也不改变FPS计数边界。'
        verification_md += roi_note + "\n\n"
        verification_md += "\n".join(f"- [{label}](<{link(path)}>)" for path, label in proof_links) + "\n"
        verification_md += "\n".join("- " + reason for reason in lookup['square-vulkan', loads[0]]['verificationReasons']) + "\n"
        verification_html = '<p>' + html.escape(verification_md.split('\n')[0].replace('**', '')) + '</p><ul>'
        verification_html += ''.join(f'<li><a href="{link(path)}">{label}</a></li>' for path, label in proof_links)
        verification_html += ''.join('<li>' + html.escape(reason) + '</li>' for reason in lookup['square-vulkan', loads[0]]['verificationReasons']) + '</ul><p>' + html.escape(roi_note) + '</p>'
    image_md = image_html = ''
    if has_vulkan:
        current_image = verification.get('canvasImage') or root / 'vulkan-readback.png'
        images = [(current_image, f'本轮v{collection_version}独立GPU读回诊断，不进入FPS统计')]
        if collection_version >= 4 and verification.get('negativeImage'):
            images.append((verification['negativeImage'], '重新采集的v3实际GPU负对照：平顶单色矩形，原始文件不被窗口截图覆盖'))
        comparison_caption = '历史v3负对照与本轮真实曲线渐变；v5/v4全帧一致由独立JSON核验' if has_fence_observation else '独立画面对照：功能正确性改变，不是同图纯优化'
        images += [(root.parent / f'visual-v{collection_version}/curve-comparison-v{collection_version}.png', comparison_caption)]
        for path, caption in images:
            if path.is_file():
                image_md += f"![{caption}]({link(path)})\n\n{caption}\n\n"
                image_html += f'<figure><img src="{link(path)}" alt="{html.escape(caption, quote=True)}" style="max-width:100%"><figcaption>{caption}</figcaption></figure>'
    limitations_md = "\n".join(f"- **{NAMES[target]}**：{value}" for target in targets for value in target_limits[target]) or "原始root未列出限制；空列表不代替显式true和独立验证。"
    baseline = environment.get('implementationNotes', '旧数据未记录本轮实现基线；版本以原环境/源码快照为准，不混入新轮次。')
    metrics_md = "\n".join(f"- **{NAMES[target]}**：{groups[target, loads[0]][0]['metric']}；drawMetric：{groups[target, loads[0]][0].get('drawMetric', '见原始结果')}。" for target in targets)
    completion_section = ''
    if has_fence_observation:
        completion_section = '## Vulkan队列返回与fence完成分开观察\n\nRoot/API FPS只统计固定窗口中的CPU返回；下表GPU rate取deadline非阻塞poll的累计完成差值/实际观测时长，先poll再end drain。实际窗口可能略长于配置秒数，最后一次跨界API调用可出现在submitted/observed计数中；drain完成数绝不追加到rate。两者都不是显示器scanout。仅因移除queue-idle或API返回变快，不能宣称GPU提速。\n\n' + gpu_table + '\n\n' + baseline_table + '\n\n'
    suite = '六方' if len(targets) == 6 else f'{len(targets)}方'
    md = f'''# Canvas {suite}性能观察报告 v{version}

> **{warning}**

reportVersion={version}；collectionVersion={collection_version}；开始 {environment['started']}。保留 {len(manifest)} 个原始样本，每档 {repetitions} 轮中位数，表格按固定目标顺序展示而非性能排名。

## 方法与实现基线

- {baseline}
- 800×600客户区，DPR=1；默认六方/六档/三轮，预热{environment['warmup']}秒、固定测量{environment['seconds']}秒，旋转顺序、串行执行、不剔除慢样本。新采集保留默认GPU/vsync，按子PID置顶测试窗口且NOACTIVATE，manifest记录observedTitle/raised。
- CPU {hardware_field(environment, 'cpu', 'Name')}；GPU {hardware_field(environment, 'gpu', 'Name')}；Win32驱动 {hardware_field(environment, 'gpu', 'DriverVersion')}；Windows {hardware_field(environment, 'os', 'Version')}；{hardware_field(environment, 'gpu', 'CurrentRefreshRate')}Hz。
- 场景仍为60帧、每帧284条逻辑命令，load重复绘制，清屏一次；资源可缓存，整个画面不可缓存。Bezier统一32段；clip+渐变矩形+开放折线；butt弧+两个端点圆。原始scene/icon与实现哈希保存在environment.json。
- v4连续渐变、真实几何裁剪及分组图层是图像正确性改变；相对旧v3不能称同画质纯性能优化。Qt原文关闭vsync，本套保留桌面默认调度，不能照搬其吞吐倍数。

## 等价声明、真实GPU验证与原始限制

{verification_md}

{limitations_md}

资格仅取每个原始root显式true，并核对实际native/resources日志、六组Canvas和被测源码快照。false/缺省旧数据永久保持受限，不因报告重新编号而升级；root声明和qualityComparable分别进入JSON/CSV。共同功能可比仍不是跨框架逐像素一致或严格GPU吞吐排名。

## 真实窗口与画面对照

{evidence_md}

{image_md}

caption/客户区/背景自动检查不代替人工内容核对。独立readback/validation诊断仅作正确性证据，性能组强制两者关闭。初始.gpu.bmp/.bmp读回不可被后续窗口图覆盖，窗口合成图另存.window.bmp。

## API/事件返回率（Vulkan v5为queued，非GPU完成）

{fps_table}

![负载与API/事件率]({files['svg']})

{completion_section}

## 指标边界

{metrics_md}

以上都不是物理scanout：Square为API返回，Avalonia进入Render，Electron rAF，Flutter引擎raster timing。Vulkan v4返回含queue-idle；v5返回为queued，CPU wall可含复用slot fence/acquire等待，GPU fence观测rate独立列出，不是GPU耗时。低负载接近60FPS是调度上限，不能代表无上限绘制吞吐相同。Flutter CPU用uiDrawTimesMs、rasterDuration另列；字体、AA和图标采样仍有实现差异。不跨框架计算GPU时间倍数，未测能耗/显存/启动或业务UI。

## 实际后端与可追溯输出

''' + '\n'.join(versions_md) + f"\n\n原始manifest/environment/日志不修改；{files['csv']}、{files['json']}提供min/max、P50/P95/P99及提交/raster统计；{files['html']}为离线交互表，{files['svg']}为曲线。旧报告不覆盖。\n"
    page = f'''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>Canvas {suite}观察 v{version}</title><style>body{{font:16px/1.7 system-ui;margin:32px auto;max-width:1180px;padding:0 20px;color:#1e293b}}table{{border-collapse:collapse;width:100%}}td,th{{border-bottom:1px solid #ddd;padding:8px;text-align:right}}td:first-child,th:first-child,td:last-child,th:last-child{{text-align:left}}svg{{width:100%}}select{{font:inherit;padding:5px}}.note{{background:#f1f5f9;padding:16px}}.limited{{background:#fff7ed;color:#9a3412}}a{{color:#1671bd}}</style><h1>Canvas {suite}观察 v{version}</h1><p class="note"><strong>{html.escape(warning)}</strong></p><p>collectionVersion={collection_version}，{len(manifest)}样本，每档{repetitions}轮；固定顺序展示，不按FPS排序。</p><p>{html.escape(baseline)}</p>'''
    page += svg_text
    if has_fence_observation:
        page += '<h2>Vulkan API queued与fence观测完成</h2><p>API列为固定配置窗口；GPU列以实际start到非阻塞deadline poll时长为分母。随后end drain计时外，不追加其完成数。两者都不是scanout；API返回增长本身不是GPU提速证据。</p>' + gpu_html + baseline_html
    page += '<h2>真实GPU功能资格与原始限制</h2>' + verification_html
    page += '<ul>' + ''.join('<li>' + html.escape(NAMES[target] + ': ' + value) + '</li>' for target in targets for value in target_limits[target]) + '</ul>'
    page += '<h2>真实窗口与正确性图像</h2>' + evidence_html + image_html + '<p>v4图像正确性改变不是v3同图纯优化。字体、AA、采样和计数边界仍不同，均非物理scanout。GPU诊断图不进性能统计。</p>'
    page += '<details><summary>实际后端与计数定义</summary><ul>' + ''.join('<li>' + html.escape(item.removeprefix('- ').replace('**', '')) + '</li>' for item in versions_md) + '</ul><p>Square API返回、Avalonia Render、Electron rAF、Flutter raster timing不是相同阶段。Vulkan旧v4含queue-idle，v5 API queued可含slot fence/acquire CPU等待；fence完成率按实际观测时长另列，drain不计。readback/validation性能采样时关闭。</p></details>'
    page += '<label>负载 <select id="load">' + ''.join(f'<option>{load}</option>' for load in loads) + '</select> ×</label><table><thead><tr><th>框架</th><th>API/事件FPS</th><th>fence完成FPS</th><th>API/事件范围</th><th>间隔P95 ms</th><th>间隔P99 ms</th><th>资格说明</th></tr></thead><tbody id="rows"></tbody></table>'
    page += f'<p><a href="{files["csv"]}">CSV</a> · <a href="{files["json"]}">JSON</a> · <a href="{files["md"]}">完整报告</a> · <a href="manifest.json">原始索引</a></p><script>'
    page += 'const data=' + json.dumps(rows, ensure_ascii=False).replace('<', '\\u003c') + ';'
    page += '''function render(){document.querySelector('#rows').innerHTML=data.filter(r=>r.load==document.querySelector('#load').value).map(r=>`<tr class="${r.qualityComparable?'':'limited'}"><td>${r.framework}</td><td>${r.fpsMedian.toFixed(2)}</td><td>${r.gpuCompletedFpsMedian?.toFixed(2)??'n/a'}</td><td>${r.fpsMin.toFixed(2)}–${r.fpsMax.toFixed(2)}</td><td>${r.intervalP95Ms?.toFixed(2)??'n/a'}</td><td>${r.intervalP99Ms?.toFixed(2)??'n/a'}</td><td>${r.qualityNote}</td></tr>`).join('')}document.querySelector('#load').onchange=render;render();</script></html>'''
    buffer = io.StringIO(newline='')
    writer = csv.DictWriter(buffer, fieldnames=list(rows[0])); writer.writeheader()
    writer.writerows({key: json.dumps(value, ensure_ascii=False) if isinstance(value, list) else value for key, value in row.items()} for row in rows)
    outputs = {files['md']: md, files['html']: page, files['svg']: svg_text, files['json']: json.dumps(rows, ensure_ascii=False, indent=2), files['csv']: buffer.getvalue()}
    for name, content in outputs.items():
        with (root / name).open('x', encoding='utf-8-sig' if name.endswith('.csv') else 'utf-8', newline='') as file:
            file.write(content)
    print(fps_table)
    print(f"Validated {len(manifest)} samples; collectionVersion={collection_version}, reportVersion={version}; {root / files['html']}")


if __name__ == '__main__':
    main()
