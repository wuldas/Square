"""Generate canonical Canvas v11 traces, without constructing native objects."""
import argparse
from collections import Counter
import hashlib
import json
import math
from pathlib import Path
import shutil
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
TRACES = ROOT / "Traces"
ASSETS = ROOT / "Assets"
SEED = 0x1234ABCD
DRAW_OPS = {"poly", "poly-new", "stroke", "stroke-new", "rect", "text", "text-new", "image", "gradient"}
CASES = ["vector-reuse", "vector-recreate", "vector-dynamic", "paint-dynamic", "transform-dynamic", "text-reuse", "text-dynamic", "image-grid", "image-scaled", "clip-layer", "fill-overdraw", "mixed-dashboard", "stroke-native"]


def canonical(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False).encode("utf-8")


def digest(data):
    return hashlib.sha256(data).hexdigest()


def mix32(value):
    value &= 0xFFFFFFFF
    value ^= value >> 16
    value = (value * 0x7FEB352D) & 0xFFFFFFFF
    value ^= value >> 15
    value = (value * 0x846CA68B) & 0xFFFFFFFF
    return (value ^ (value >> 16)) & 0xFFFFFFFF


def color(index, frame=0, alpha=255):
    # Addition is bijective within RGB24; dynamic colors do not repeat per object.
    rgb = ((mix32(SEED ^ index) & 0xFFFFFF) + frame * 0x010307) & 0xFFFFFF
    return f"#{rgb:06X}{alpha:02X}"


def octagon(x, y, size=16, inset=4):
    return [[x + inset, y], [x + size - inset, y], [x + size, y + inset],
            [x + size, y + size - inset], [x + size - inset, y + size],
            [x + inset, y + size], [x, y + size - inset], [x, y + inset]]


def grid_path(index, frame=0, pool=False):
    slot = index % (48 * 30)
    x, y = 8 + slot % 48 * 24, 24 + slot // 48 * 24
    x += (index // (48 * 30)) / 16 if pool else frame / 16
    return octagon(x, y)


def create_assets():
    ASSETS.mkdir(parents=True, exist_ok=True)
    source = Path("C:/Windows/Fonts/segoeui.ttf")
    if not source.is_file():
        raise FileNotFoundError(f"Required Segoe UI Regular font missing: {source}")
    shutil.copyfile(source, ASSETS / "font.ttf")
    image = Image.new("RGBA", (32, 32))
    for y in range(32):
        for x in range(32):
            image.putpixel((x, y), (224, 64, 32, 255) if ((x // 8 + y // 8) & 1) == 0 else (32, 128, 224, 128))
    image.save(ASSETS / "image.png", optimize=False)
    return {name: digest((ASSETS / name).read_bytes()) for name in ("font.ttf", "image.png")}


def base_trace(case, level, warmup=60, measure=120):
    return {"protocolVersion": 11, "case": case, "level": level, "width": 1280, "height": 800,
            "devicePixelRatio": 1, "background": "#101827FF", "seed": SEED,
            "warmupFrames": warmup, "measureFrames": measure, "paths": [], "texts": [], "frames": [],
            "matrixConvention": "x'=m11*x+m21*y+dx; y'=m12*x+m22*y+dy; CTM=parent*local",
            "verificationMeasurementSequences": [0, 17, 37],
            "samplingProfile": "native-scaled" if case == "image-scaled" else "identity-1to1",
            "qualityComparable": case not in {"image-scaled", "stroke-native"},
            "qualityProfile": "native-image-sampling" if case == "image-scaled" else ("native-stroke-default-cap-join" if case == "stroke-native" else "fill-source-over")}


def regular_trace(case, level):
    high = level == "high"
    count = 512 if high else 128
    trace = base_trace(case, level)
    if case in {"vector-reuse", "vector-recreate", "vector-dynamic", "paint-dynamic", "transform-dynamic", "stroke-native"}:
        trace["paths"] = [grid_path(i) for i in range(count)] if case not in {"vector-recreate", "vector-dynamic"} else []
    elif case in {"text-reuse", "text-dynamic"}:
        count = 96 if high else 24
        if case == "text-reuse":
            trace["texts"] = [{"text": f"Canvas {i:03d} frame 0000", "size": 14} for i in range(count)]
    elif case in {"image-grid", "image-scaled"}:
        count = (128 if high else 32) if case == "image-scaled" else (256 if high else 64)
    elif case == "clip-layer":
        count = 32 if high else 8
        trace["paths"] = [octagon(16 + i % 16 * 76, 32 + i // 16 * 88, 64, 16) for i in range(count)]
    elif case == "fill-overdraw":
        count = 16 if high else 4
    elif case == "mixed-dashboard":
        count = 4 if high else 1
        trace["texts"] = [{"text": f"Canvas panel {i:02d}", "size": 14} for i in range(count)]
        for i in range(count):
            x, y = 16 + i % 2 * 624, 32 + i // 2 * 376
            trace["paths"].extend([octagon(x + 24, y + 64), octagon(x + 448, y + 64, 64, 16)])
    for frame in range(180):
        ops = []
        for i in range(count):
            if case == "vector-reuse":
                ops.append(["poly", i, color(i)])
            elif case in {"vector-recreate", "vector-dynamic"}:
                ops.append(["poly-new", grid_path(i, frame if case == "vector-dynamic" else 0), color(i)])
            elif case == "paint-dynamic":
                ops.append(["poly", i, color(i, frame)])
            elif case == "transform-dynamic":
                ops.extend([["transform", 1 + frame / 4096, frame / 4096, -frame / 4096, 1, frame / 16, 0], ["poly", i, color(i)], ["end-transform"]])
            elif case == "stroke-native":
                ops.append(["stroke", i, color(i), 1.5])
            elif case in {"text-reuse", "text-dynamic"}:
                x, y = 16 + i % 8 * 152, 32 + i // 8 * 56
                ops.append(["text", i, x, y, color(i)] if case == "text-reuse" else ["text-new", f"Canvas {i:03d} frame {frame:04d}", 14, x, y, color(i)])
            elif case in {"image-grid", "image-scaled"}:
                size, columns, stride = (64, 16, 76) if case == "image-scaled" else (32, 32, 38)
                ops.append(["image", 16 + i % columns * stride, 32 + i // columns * (size + 16), size, size])
            elif case == "clip-layer":
                x, y = 16 + i % 16 * 76, 32 + i // 16 * 88
                ops.extend([["layer", x, y, 64, 64, .65], ["clip", i], ["rect", x, y, 48, 64, color(i * 2)], ["rect", x + 16, y, 48, 64, color(i * 2 + 1)], ["end-clip"], ["end-layer"]])
            elif case == "fill-overdraw":
                ops.append(["rect", 0, 16, 1280, 784, color(i, alpha=128)])
            elif case == "mixed-dashboard":
                x, y = 16 + i % 2 * 624, 32 + i // 2 * 376
                ops.extend([["rect", x, y, 608, 352, "#1E293BFF"], ["text", i, x + 16, y + 16, "#E2E8F0FF"], ["poly", i * 2, color(i)],
                            ["poly-new", octagon(x + 64 + frame / 16, y + 64), color(i + 1)],
                            ["gradient", x + 24, y + 112, 352, 144, x + 24, y + 112, x + 376, y + 256, "#22C55EFF", "#1E40AFFF"],
                            ["image", x + 448, y + 176, 32, 32], ["layer", x + 448, y + 64, 64, 64, .65], ["clip", i * 2 + 1],
                            ["rect", x + 448, y + 64, 48, 64, "#EF4444FF"], ["rect", x + 464, y + 64, 48, 64, "#3B82F6FF"], ["end-clip"], ["end-layer"],
                            ["text-new", f"Frame {frame:04d} panel {i}", 14, x + 24, y + 288, "#E2E8F0FF"]])
        trace["frames"].append({"ops": ops})
    trace["workload"] = {"independentObjectsPerFrame": count, "dynamicFields": {
        "vector-recreate": ["native-path-rebuilt-same-values"], "vector-dynamic": ["geometry"], "paint-dynamic": ["paint"],
        "transform-dynamic": ["transform"], "text-dynamic": ["text"], "mixed-dashboard": ["geometry", "text"]}.get(case, [])}
    return trace


def pressure_trace(pool_size):
    warmup = max(60, math.ceil(pool_size / 128) * 2)
    trace = base_trace("cache-pressure", str(pool_size), warmup)
    trace["paths"] = [grid_path(i, pool=True) for i in range(pool_size)]
    for frame in range(warmup + 120):
        start = frame * 128
        trace["frames"].append({"ops": [["poly", (start + i) % pool_size, color(i)] for i in range(128)]})
    trace["workload"] = {"pathPoolSize": pool_size, "independentObjectsPerFrame": 128,
                         "warmupPoolCycles": warmup * 128 // pool_size, "dynamicFields": [],
                         "warmupRationale": "At least 60 logical frames for runtime/CPU warmup, and at least two complete path-pool passes; no wall-clock phase selection.",
                         "poolSchedule": "consecutive-128-wrap-pool-only"}
    return trace


def over(top, bottom):
    rgba = [int(top[i:i + 2], 16) for i in (1, 3, 5, 7)]
    alpha = rgba[3] / 255
    return [round(rgba[i] * alpha + bottom[i] * (1 - alpha)) for i in range(3)] + [255]


def probe(name, xy, expected, tolerance=2):
    return {"name": name, "rect": [xy[0] - 2, xy[1] - 2, 5, 5], "expectedRgba": expected, "maxChannelError": tolerance}


def semantic_trace():
    trace = base_trace("semantic-fixture", "core", 0, 1)
    trace["verificationMeasurementSequences"] = [0]
    trace["paths"] = [octagon(120, 40, 64, 16), [[328, 48], [352, 72], [376, 48]], [[600, 40], [664, 40], [600, 104]]]
    trace["texts"] = [{"text": "Canvas 0123", "size": 14}]
    trace["frames"] = [{"ops": [
        ["rect", 24, 40, 64, 64, "#E04020FF"], ["poly", 0, "#E0A020FF"], ["poly-new", octagon(216, 40, 64, 16), "#20C0C0FF"],
        ["stroke", 1, "#FFFFFFFF", 1.5], ["stroke-new", [[408, 48], [432, 72], [456, 48]], "#FFFFFFFF", 1.5],
        ["text", 0, 24, 160, "#E2E8F0FF"], ["text-new", "New 4567", 14, 24, 200, "#E2E8F0FF"], ["image", 320, 160, 32, 32],
        ["transform", 1, 0, 0, 1, 480, 160], ["transform", 2, 0, 0, 2, 0, 0], ["rect", 0, 0, 24, 24, "#A060E0FF"], ["end-transform"], ["end-transform"],
        ["clip", 2], ["rect", 600, 40, 64, 64, "#20C060FF"], ["end-clip"],
        ["clip-new", [[704, 40], [768, 40], [704, 104]]], ["rect", 704, 40, 64, 64, "#E0C020FF"], ["end-clip"],
        ["layer", 600, 160, 96, 96, .65], ["rect", 600, 160, 64, 96, "#E04020FF"], ["rect", 632, 160, 64, 96, "#2040E0FF"], ["end-layer"],
        ["rect", 816, 40, 64, 64, "#E0402080"], ["rect", 848, 40, 64, 64, "#2040E080"],
        ["gradient", 816, 160, 96, 64, 816, 160, 912, 160, "#E04020FF", "#2040E0FF"]]}]
    bg = [16, 24, 39, 255]
    group_red = [round(v * .65 + bg[i] * .35) for i, v in enumerate([224, 64, 32])] + [255]
    group_blue = [round(v * .65 + bg[i] * .35) for i, v in enumerate([32, 64, 224])] + [255]
    trace["semanticChecks"] = {
        "interiorProbes": [probe("background", (1000, 400), bg, 0), probe("opaque-rect", (48, 64), [224, 64, 32, 255], 0),
                           probe("static-poly", (152, 72), [224, 160, 32, 255], 0), probe("new-poly", (248, 72), [32, 192, 192, 255], 0),
                           probe("parent-times-local-transform", (500, 180), [160, 96, 224, 255], 0),
                           probe("true-clip-interior", (612, 52), [32, 192, 96, 255], 0), probe("true-clip-outside-inside-bounds", (652, 92), bg, 0),
                           probe("new-clip-interior", (716, 52), [224, 192, 32, 255], 0), probe("new-clip-outside-inside-bounds", (756, 92), bg, 0)],
        "alphaProbes": [probe("alpha-single", (828, 64), over("#E0402080", bg)), probe("alpha-source-over-intersection", (864, 64), over("#2040E080", over("#E0402080", bg)))],
        "groupProbes": [probe("group-red-only", (612, 200), group_red), probe("group-overlap-is-blue-on-transparent-then-group-opacity", (648, 200), group_blue),
                        probe("group-blue-only", (680, 200), group_blue), probe("group-outside-bounds", (704, 200), bg, 0)],
        "imageProbes": [probe("identity-opaque-cell", (324, 164), [224, 64, 32, 255], 0), probe("identity-alpha-cell", (332, 164), over("#2080E080", bg))],
        "textRegions": [{"rect": [24, 160, 120, 24], "text": "Canvas 0123", "size": 14, "origin": [24, 160]}, {"rect": [24, 200, 120, 24], "text": "New 4567", "size": 14, "origin": [24, 200]}],
        "textRule": "Check identical font bytes, exact content/top-left origin and nonempty strokes; report AA separately.",
        "separateQualityRegions": [{"profile": "native-stroke-default-cap-join", "rect": [320, 40, 144, 40]}, {"profile": "native-gradient-interpolation", "rect": [816, 160, 96, 64]},
                                   {"profile": "AA-polygon-edge", "rect": [120, 40, 20, 20]}, {"profile": "AA-clip-diagonal", "rect": [624, 64, 16, 16]}],
        "roiRule": "Exact probes are 5x5 interior/background regions away from edges; source-over uses encoded sRGB bytes, tolerance covers final quantization."}
    return trace


def sampling_trace():
    trace = base_trace("semantic-fixture", "sampling", 0, 1)
    trace.update({"verificationMeasurementSequences": [0], "samplingProfile": "native-scaled",
                  "qualityComparable": False, "qualityProfile": "native-image-sampling"})
    trace["frames"] = [{"ops": [["image", 320, 160, 32, 32], ["image", 400, 160, 64, 64]]}]
    bg = [16, 24, 39, 255]
    trace["semanticChecks"] = {
        "imageProbes": [probe("identity-opaque-cell", (324, 164), [224, 64, 32, 255], 0),
                        probe("identity-alpha-cell", (332, 164), over("#2080E080", bg)),
                        probe("scaled-opaque-interior", (406, 166), [224, 64, 32, 255], 0),
                        probe("scaled-alpha-interior", (422, 166), over("#2080E080", bg))],
        "separateQualityRegions": [{"profile": "native-scaled-sampler-boundary", "rect": [412, 160, 8, 16],
                                    "rule": "Report nearest versus linear boundary pixels; not fixed-quality eligible."}],
        "qualityRule": "Identity probes qualify the common 1:1 path; 2:1 interior probes only verify texture content. Boundary interpolation remains backend native/default and separate."}
    return trace


def calibration_trace():
    trace = base_trace("calibration", "marker-only")
    trace.update({"kind": "calibration", "qualityProfile": "marker-only", "samplingProfile": "identity-1to1"})
    trace["frames"] = [{"ops": []} for _ in range(180)]
    trace["workload"] = {"independentObjectsPerFrame": 0, "dynamicFields": [],
                         "purpose": "Uniform observer overhead/visible-frame calibration; marker excluded from opcode counts; not part of benchmark matrix."}
    return trace


def finish_trace(trace, asset_hashes):
    measured = trace["frames"][trace["warmupFrames"]:]
    totals = Counter(op[0] for frame in measured for op in frame["ops"])
    frame_counts = [sum(op[0] in DRAW_OPS for op in frame["ops"]) for frame in trace["frames"]]
    frame_hashes = [digest(canonical(frame["ops"])) for frame in trace["frames"]]
    dynamic = trace.get("workload", {}).get("dynamicFields", [])
    changing = bool(set(dynamic) - {"native-path-rebuilt-same-values"})
    if changing:
        assert len(set(frame_hashes)) == len(frame_hashes), trace["case"]
        assert not set(frame_hashes[:trace["warmupFrames"]]) & set(frame_hashes[trace["warmupFrames"]:])
    trace["expectedMeasuredOpcodeTotals"] = dict(sorted(totals.items()))
    trace["drawsPerFrame"] = frame_counts[0] if len(set(frame_counts)) == 1 else frame_counts
    trace["logicalInputsSha256"] = digest(canonical({key: trace[key] for key in ("paths", "texts", "frames")}))
    trace["assets"] = {"font": {"file": "font.ttf", "family": "Segoe UI", "style": "Regular", "sha256": asset_hashes["font.ttf"]},
                       "image": {"file": "image.png", "width": 32, "height": 32, "samplingProfile": trace["samplingProfile"], "sha256": asset_hashes["image.png"]}}
    trace["metadata"] = {"fontSha256": asset_hashes["font.ttf"], "imageSha256": asset_hashes["image.png"],
                         "frameLogicalInputsSha256": frame_hashes,
                         "nativeDynamicObjects": "poly-new/stroke-new/text-new are logical inputs only; construct native objects in that frame's nativePrepareMs"}
    if trace["case"] in {"text-reuse", "text-dynamic"}:
        trace["metadata"]["frameTextContentSha256"] = [digest(canonical([
            trace["texts"][op[1]]["text"] if op[0] == "text" else op[1]
            for op in frame["ops"] if op[0] in {"text", "text-new"}])) for frame in trace["frames"]]
        trace["metadata"]["textContentRule"] = "Matching ASCII content length and font glyph set; dynamic changes only fixed-width four-digit frame content."
    trace["inputAssertions"] = {"frameCount": len(trace["frames"]), "uniqueFrameInputs": len(set(frame_hashes)), "dynamicFrameInputsMustBeUnique": changing,
                                "warmupMeasurementDynamicInputsDisjoint": changing, "coordinatesQuantum": "1/16", "matrixQuantum": "1/4096"}
    return trace


def write_trace(trace, asset_hashes):
    finish_trace(trace, asset_hashes)
    name = f'{trace["case"]}-{trace["level"]}'
    folder = TRACES / name
    folder.mkdir(parents=True, exist_ok=True)
    for asset in ("font.ttf", "image.png"):
        shutil.copyfile(ASSETS / asset, folder / asset)
    data = canonical(trace)
    (folder / "trace.json").write_bytes(data)
    return {"id": name, "kind": trace.get("kind", "semantic-fixture" if trace["case"] == "semantic-fixture" else "benchmark"),
            "trace": f"{name}/trace.json", "traceSha256": digest(data), "bytes": len(data), "case": trace["case"], "level": trace["level"],
            "warmupFrames": trace["warmupFrames"], "measureFrames": trace["measureFrames"], "drawsPerFrame": trace["drawsPerFrame"],
            "expectedMeasuredOpcodeTotals": trace["expectedMeasuredOpcodeTotals"], "logicalInputsSha256": trace["logicalInputsSha256"],
            "inputAssertions": trace["inputAssertions"], "qualityProfile": trace["qualityProfile"],
            "qualityComparable": trace["qualityComparable"], "samplingProfile": trace["samplingProfile"],
            "verificationMeasurementSequences": trace["verificationMeasurementSequences"]}


def schema():
    coord = {"type": "number", "multipleOf": 1 / 16}
    matrix = {"type": "number", "multipleOf": 1 / 4096}
    rgba = {"type": "string", "pattern": "^#[0-9A-F]{8}$"}
    index = {"type": "integer", "minimum": 0}
    points = {"type": "array", "minItems": 2, "items": {"type": "array", "prefixItems": [coord, coord], "minItems": 2, "maxItems": 2}}
    polygon = {**points, "minItems": 3}
    ops = []
    def op(name, fields):
        values = [{"const": name}] + fields
        ops.append({"type": "array", "prefixItems": values, "minItems": len(values), "maxItems": len(values)})
    op("poly", [index, rgba]); op("poly-new", [polygon, rgba]); op("stroke", [index, rgba, coord]); op("stroke-new", [points, rgba, coord])
    op("rect", [coord] * 4 + [rgba]); op("text", [index, coord, coord, rgba]); op("text-new", [{"type": "string"}, coord, coord, coord, rgba]); op("image", [coord] * 4)
    op("transform", [matrix] * 4 + [coord] * 2); op("end-transform", []); op("clip", [index]); op("clip-new", [polygon]); op("end-clip", [])
    op("layer", [coord] * 4 + [{"type": "number", "minimum": 0, "maximum": 1}]); op("end-layer", []); op("gradient", [coord] * 8 + [rgba, rgba])
    return {"$schema": "https://json-schema.org/draft/2020-12/schema", "title": "Canvas benchmark v11 logical trace", "type": "object",
            "required": ["protocolVersion", "case", "level", "width", "height", "seed", "warmupFrames", "measureFrames", "paths", "texts", "frames", "expectedMeasuredOpcodeTotals"],
            "properties": {"protocolVersion": {"const": 11}, "case": {"type": "string"}, "level": {"type": "string"}, "width": {"const": 1280}, "height": {"const": 800}, "seed": {"const": SEED},
                           "warmupFrames": {"type": "integer", "minimum": 0}, "measureFrames": {"type": "integer", "minimum": 1}, "paths": {"type": "array", "items": points},
                           "texts": {"type": "array", "items": {"type": "object", "required": ["text", "size"], "properties": {"text": {"type": "string"}, "size": coord}}},
                           "frames": {"type": "array", "items": {"type": "object", "required": ["ops"], "properties": {"ops": {"type": "array", "items": {"oneOf": ops}}}}},
                           "devicePixelRatio": {"const": 1}, "background": {"const": "#101827FF"},
                           "samplingProfile": {"enum": ["identity-1to1", "native-scaled"]}, "qualityComparable": {"type": "boolean"},
                           "verificationMeasurementSequences": {"type": "array", "items": {"type": "integer", "minimum": 0}},
                           "logicalInputsSha256": {"type": "string", "pattern": "^[0-9a-f]{64}$"},
                           "metadata": {"type": "object", "required": ["fontSha256", "imageSha256"], "properties": {
                               "fontSha256": {"type": "string", "pattern": "^[0-9a-f]{64}$"}, "imageSha256": {"type": "string", "pattern": "^[0-9a-f]{64}$"}}},
                           "expectedMeasuredOpcodeTotals": {"type": "object", "additionalProperties": {"type": "integer", "minimum": 0}}},
            "description": "Paths are bare point arrays. poly closes, stroke remains open. Parse logical traces before timing; construct dynamic native objects per frame. Additional metadata is advisory. validate-traces.py checks stacks, lengths, references, quantization and totals."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--representative", action="store_true", help="Generate schema, calibration, two fixtures and mixed-dashboard-low; manifest.complete=false")
    args = parser.parse_args()
    asset_hashes = create_assets()
    TRACES.mkdir(parents=True, exist_ok=True)
    (TRACES / "schema.json").write_bytes(canonical(schema()))
    entries = [write_trace(calibration_trace(), asset_hashes), write_trace(semantic_trace(), asset_hashes),
               write_trace(sampling_trace(), asset_hashes), write_trace(regular_trace("mixed-dashboard", "low"), asset_hashes)]
    if not args.representative:
        for case in CASES:
            for level in ("low", "high"):
                if (case, level) != ("mixed-dashboard", "low"):
                    entries.append(write_trace(regular_trace(case, level), asset_hashes))
        for pool_size in (256, 4096, 65536):
            entries.append(write_trace(pressure_trace(pool_size), asset_hashes))
    entries.sort(key=lambda item: item["id"])
    manifest = {"protocolVersion": 11, "seed": SEED, "width": 1280, "height": 800, "devicePixelRatio": 1, "complete": not args.representative,
                "traceCount": len(entries), "schema": "schema.json", "schemaSha256": digest((TRACES / "schema.json").read_bytes()),
                "assetsSha256": asset_hashes, "assetProvenance": {"fontSource": "C:/Windows/Fonts/segoeui.ttf", "fontFamily": "Segoe UI", "fontStyle": "Regular", "image": "generated 32x32 RGBA 8px checker, opaque/alpha128 cells"},
                "generatorSha256": digest(Path(__file__).read_bytes()),
                "canonicalJson": "UTF-8, sorted object keys, compact separators, no BOM or trailing newline",
                "benchmarkTraces": [entry for entry in entries if entry["kind"] == "benchmark"],
                "semanticFixtures": [entry for entry in entries if entry["kind"] == "semantic-fixture"],
                "calibrationTraces": [entry for entry in entries if entry["kind"] == "calibration"],
                "verification": {"regularMeasurementSequences": [0, 17, 37], "fixtureMeasurementSequences": [0], "indicesAre": "measurement sequence; absolute frame index = warmupFrames + sequence", "fixtureTimingEligible": False},
                "capabilityProfiles": {"fill-source-over": "simple polygons, rect, same-font text, integer 1:1 images, true clip, group opacity",
                                       "native-image-sampling": "2:1 scaling uses backend-native/default sampler; qualityComparable=false; retain nearest/linear differences",
                                       "native-stroke-default-cap-join": "separate default native stroke quality; no cap/join/AA identity claim",
                                       "native-gradient-interpolation": "separate interpolation quality; inspect semantic fixture", "curves-holes-self-intersections": "not exercised; no equivalence claim"}}
    (TRACES / "manifest.json").write_bytes(canonical(manifest))
    for entry in entries:
        print(f'{entry["id"]}: {entry["bytes"]:,} bytes, warmup={entry["warmupFrames"]}, measure={entry["measureFrames"]}, draws={entry["drawsPerFrame"]}')
    print(f'Generated {len(entries)} traces, {sum(entry["bytes"] for entry in entries):,} JSON bytes; complete={manifest["complete"]}')


if __name__ == "__main__":
    main()
