"""Validate Canvas v11 files and optional producer result/semantic PNG using CPU only."""
import argparse
from collections import Counter
import hashlib
import json
import math
from pathlib import Path
import re
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
DRAW = {"poly", "poly-new", "stroke", "stroke-new", "rect", "text", "text-new", "image", "gradient"}
LENGTHS = {"poly": 3, "poly-new": 3, "stroke": 4, "stroke-new": 4, "rect": 6,
           "text": 5, "text-new": 6, "image": 5, "transform": 7, "end-transform": 1,
           "clip": 2, "clip-new": 2, "end-clip": 1, "layer": 6, "end-layer": 1, "gradient": 11}
COLOR_AT = {"poly": [2], "poly-new": [2], "stroke": [2], "stroke-new": [2], "rect": [5], "text": [4], "text-new": [5], "gradient": [9, 10]}
COORD_AT = {"stroke": [3], "stroke-new": [3], "rect": [1, 2, 3, 4], "text": [2, 3], "text-new": [2, 3, 4],
            "image": [1, 2, 3, 4], "layer": [1, 2, 3, 4], "gradient": list(range(1, 9)), "transform": [5, 6]}
IDENTITY = (1, 0, 0, 1, 0, 0)


def canonical(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False).encode("utf-8")


def sha(data):
    return hashlib.sha256(data).hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def quantized(value, scale):
    return type(value) in (int, float) and math.isfinite(value) and value * scale == int(value * scale)


def points_ok(points, minimum=2):
    require(isinstance(points, list) and len(points) >= minimum, "invalid point count")
    require(all(isinstance(p, list) and len(p) == 2 and all(quantized(v, 16) for v in p) for p in points), "non-quantized points")
    require(len(set(tuple(p) for p in points)) == len(points), "repeated path vertices")


def convex(points):
    signs = []
    for i in range(len(points)):
        a, b, c = points[i - 2], points[i - 1], points[i]
        cross = (b[0] - a[0]) * (c[1] - b[1]) - (b[1] - a[1]) * (c[0] - b[0])
        signs.append(cross)
    require(all(v > 0 for v in signs) or all(v < 0 for v in signs), "filled polygon must be simple strictly convex in this suite")


def multiply(a, b):
    return (a[0] * b[0] + a[2] * b[1], a[1] * b[0] + a[3] * b[1],
            a[0] * b[2] + a[2] * b[3], a[1] * b[2] + a[3] * b[3],
            a[0] * b[4] + a[2] * b[5] + a[4], a[1] * b[4] + a[3] * b[5] + a[5])


def local_point(matrix, x, y):
    a, b, c, d, tx, ty = matrix
    det = a * d - b * c
    return ((d * (x - tx) - c * (y - ty)) / det, (-b * (x - tx) + a * (y - ty)) / det)


def inside_poly(points, x, y):
    # Ray casting also handles non-axis-aligned fixture triangle clipping.
    inside = False
    prev = points[-1]
    for curr in points:
        if (curr[1] > y) != (prev[1] > y):
            if x < (prev[0] - curr[0]) * (y - curr[1]) / (prev[1] - curr[1]) + curr[0]:
                inside = not inside
        prev = curr
    return inside


def rgba(color):
    return [int(color[i:i + 2], 16) / 255 for i in (1, 3, 5, 7)]


def source_over(source, dest):
    sa, da = source[3], dest[3]
    alpha = sa + da * (1 - sa)
    if alpha == 0:
        return [0, 0, 0, 0]
    return [(source[i] * sa + dest[i] * da * (1 - sa)) / alpha for i in range(3)] + [alpha]


def fixture_pixel(trace, image, x, y):
    """Analytic interior-only reference; it makes no font, stroke or AA equivalence claim."""
    matrix, clipped, surface = IDENTITY, False, rgba(trace["background"])
    stack = []
    for op in trace["frames"][0]["ops"]:
        name = op[0]
        lx, ly = local_point(matrix, x, y)
        if name == "transform":
            stack.append((name, matrix, clipped, None))
            matrix = multiply(matrix, op[1:])
        elif name in {"clip", "clip-new"}:
            stack.append(("clip", matrix, clipped, None))
            points = trace["paths"][op[1]] if name == "clip" else op[1]
            clipped = clipped or not inside_poly(points, lx, ly)
        elif name == "layer":
            stack.append((name, matrix, clipped, (surface, op[5])))
            clipped = clipped or not (op[1] <= lx < op[1] + op[3] and op[2] <= ly < op[2] + op[4])
            surface = [0, 0, 0, 0]
        elif name.startswith("end-"):
            kind, matrix, clipped, layer = stack.pop()
            if kind == "layer":
                parent, opacity = layer
                surface[3] *= opacity
                surface = source_over(surface, parent)
        elif not clipped:
            source = None
            if name in {"poly", "poly-new"}:
                points = trace["paths"][op[1]] if name == "poly" else op[1]
                if inside_poly(points, lx, ly):
                    source = rgba(op[2])
            elif name == "rect" and op[1] <= lx < op[1] + op[3] and op[2] <= ly < op[2] + op[4]:
                source = rgba(op[5])
            elif name == "image" and op[1] <= lx < op[1] + op[3] and op[2] <= ly < op[2] + op[4]:
                ix, iy = int((lx - op[1]) * image.width / op[3]), int((ly - op[2]) * image.height / op[4])
                source = [v / 255 for v in image.getpixel((ix, iy))]
            if source is not None:
                surface = source_over(source, surface)
    return [round(v * 255) for v in surface]


def probes(trace):
    checks = trace.get("semanticChecks", {})
    for key in ("interiorProbes", "alphaProbes", "groupProbes", "imageProbes"):
        for probe in checks.get(key, []):
            yield key, probe


def validate_fixture(trace, image):
    count = 0
    for _, probe in probes(trace):
        x, y, w, h = probe["rect"]
        require(x >= 0 and y >= 16 and x + w <= 1280 and y + h <= 800, "probe outside content")
        for py in range(y, y + h):
            for px in range(x, x + w):
                actual = fixture_pixel(trace, image, px + .5, py + .5)
                require(max(abs(a - e) for a, e in zip(actual, probe["expectedRgba"])) <= probe["maxChannelError"], f'analytic fixture mismatch: {probe["name"]} at {px},{py}: {actual}')
        count += 1
    return count


def validate_trace(path, entry, manifest):
    data = path.read_bytes()
    trace = json.loads(data)
    require(data == canonical(trace), "trace is not canonical JSON")
    require(sha(data) == entry["traceSha256"] and len(data) == entry["bytes"], "manifest trace hash/length mismatch")
    require((trace["protocolVersion"], trace["width"], trace["height"], trace["devicePixelRatio"], trace["seed"]) == (11, 1280, 800, 1, 0x1234ABCD), "wrong protocol/dimensions/seed")
    warm, measure = trace["warmupFrames"], trace["measureFrames"]
    require(type(warm) is int and warm >= 0 and type(measure) is int and measure > 0, "invalid frame counts")
    require(len(trace["frames"]) == warm + measure, "frame length mismatch; no cyclic playback permitted")
    require(trace["inputAssertions"]["frameCount"] == warm + measure, "declared frameCount mismatch")
    case, high = trace["case"], trace["level"] == "high"
    if case == "calibration":
        require(trace.get("kind") == "calibration" and (warm, measure) == (60, 120), "calibration configuration")
        require(not trace["paths"] and not trace["texts"] and all(not frame["ops"] for frame in trace["frames"]), "calibration must contain no scene inputs")
        require(trace["drawsPerFrame"] == 0 and trace["expectedMeasuredOpcodeTotals"] == {}, "calibration marker must be excluded from scene totals")
    elif case == "comprehensive":
        require(trace["level"] == "default" and (warm, measure) == (60, 120), "comprehensive fixed profile")
        require(trace["drawsPerFrame"] == 44 and all(len(f["ops"]) == 68 for f in trace["frames"]), "comprehensive68ops/44draws")
    elif case not in {"semantic-fixture", "cache-pressure"}:
        require((warm, measure) == (60, 120), "regular profile must use warmup60/measure120")
        expected_draws = {"vector-reuse": (128, 512), "vector-recreate": (128, 512), "vector-dynamic": (128, 512),
                          "paint-dynamic": (128, 512), "transform-dynamic": (128, 512), "stroke-native": (128, 512),
                          "text-reuse": (24, 96), "text-dynamic": (24, 96), "image-grid": (64, 256), "image-scaled": (32, 128),
                          "clip-layer": (16, 64), "fill-overdraw": (4, 16), "mixed-dashboard": (9, 36)}
        require(trace["level"] in {"low", "high"} and trace["drawsPerFrame"] == expected_draws[case][high], "scene matrix load mismatch")
    else:
        require(case != "semantic-fixture" or (warm, measure) == (0, 1), "semantic fixture must be independent single frame")
    for name, key in (("font.ttf", "font"), ("image.png", "image")):
        actual_hash = sha((path.parent / name).read_bytes())
        require(actual_hash == manifest["assetsSha256"][name] == trace["assets"][key]["sha256"] == trace["metadata"][f"{key}Sha256"], f"asset hash mismatch: {name}")
    image = Image.open(path.parent / "image.png").convert("RGBA")
    require(image.size == (32, 32), "image dimensions")
    for points in trace["paths"]:
        points_ok(points)
    for text in trace["texts"]:
        require(isinstance(text["text"], str) and quantized(text["size"], 16) and text["size"] > 0, "invalid static text")
    totals, draw_counts, frame_hashes, dynamic_values = Counter(), [], [], []
    filled_path_ids = set()
    for frame_index, frame in enumerate(trace["frames"]):
        stack = []
        frame_draws = 0
        values = []
        for op in frame["ops"]:
            require(isinstance(op, list) and op and op[0] in LENGTHS and len(op) == LENGTHS[op[0]], "invalid opcode/arity")
            name = op[0]
            for index in COLOR_AT.get(name, []):
                require(isinstance(op[index], str) and re.fullmatch(r"#[0-9A-F]{8}", op[index]), "invalid RGBA color")
            for index in COORD_AT.get(name, []):
                require(quantized(op[index], 16), f"non-quantized coordinate: {name}")
            if name == "transform":
                require(all(quantized(v, 4096) for v in op[1:5]), "non-quantized matrix")
                require(op[1] * op[4] - op[2] * op[3] != 0, "singular matrix")
                stack.append("transform")
            elif name in {"clip", "clip-new"}:
                stack.append("clip")
            elif name == "layer":
                require(not stack, "layer must be top-level, not nested")
                require(0 <= op[5] <= 1, "invalid opacity")
                stack.append("layer")
            elif name.startswith("end-"):
                require(stack and stack.pop() == name[4:], "invalid LIFO state nesting")
            if name in {"poly", "stroke", "clip", "text"}:
                pool = trace["texts"] if name == "text" else trace["paths"]
                require(type(op[1]) is int and 0 <= op[1] < len(pool), "asset ID out of range")
                if name in {"poly", "clip"}:
                    filled_path_ids.add(op[1])
            if name in {"poly-new", "stroke-new", "clip-new"}:
                points_ok(op[1], 2 if name == "stroke-new" else 3)
                if name != "stroke-new":
                    convex(op[1])
            if name in {"rect", "image", "layer", "gradient"}:
                require(op[3] > 0 and op[4] > 0, "non-positive width/height")
            if name in {"stroke", "stroke-new"}:
                require(op[3] > 0, "non-positive stroke width")
            if name == "text-new":
                require(isinstance(op[1], str) and op[2] > 0, "invalid dynamic text")
            if name == "image":
                require(op[3:5] in ([32, 32], [64, 64]), "unsupported image scaling")
                if trace["case"] in {"image-grid", "mixed-dashboard"}:
                    require(op[3:5] == [32, 32] and all(type(v) is int for v in op[1:5]), "main image profile must be integer 1:1")
            frame_draws += name in DRAW
            if frame_index >= warm:
                totals[name] += 1
            # Assert individual dynamic inputs, independently from whole-frame hash.
            case = trace["case"]
            if case == "comprehensive" and name in {"poly-new", "poly", "stroke", "transform", "text-new"}:
                values.append(op[1:])
            if (case == "vector-dynamic" and name == "poly-new") or (case == "mixed-dashboard" and name in {"poly-new", "text-new"}):
                values.append(op[1])
            elif case == "paint-dynamic" and name == "poly":
                values.append(op[2])
            elif case == "transform-dynamic" and name == "transform":
                values.append(op[1:])
            elif case == "text-dynamic" and name == "text-new":
                values.append(op[1])
        require(not stack, "unclosed frame state")
        draw_counts.append(frame_draws)
        frame_hashes.append(sha(canonical(frame["ops"])))
        if values:
            dynamic_values.append(values)
    for path_id in filled_path_ids:
        convex(trace["paths"][path_id])
    require(dict(totals) == trace["expectedMeasuredOpcodeTotals"] == entry["expectedMeasuredOpcodeTotals"], "measurement opcode totals mismatch")
    require(all(count == trace["drawsPerFrame"] for count in draw_counts), "draws/frame mismatch")
    logical = sha(canonical({key: trace[key] for key in ("paths", "texts", "frames")}))
    require(logical == trace["logicalInputsSha256"] == entry["logicalInputsSha256"], "logical input hash mismatch")
    require(len(set(frame_hashes)) == trace["inputAssertions"]["uniqueFrameInputs"], "unique frame assertion mismatch")
    require(frame_hashes == trace["metadata"]["frameLogicalInputsSha256"], "per-frame input SHA mismatch")
    if trace["case"] in {"text-reuse", "text-dynamic"}:
        contents = [[trace["texts"][op[1]]["text"] if op[0] == "text" else op[1] for op in frame["ops"]] for frame in trace["frames"]]
        require(all(len(value) == 21 and value.isascii() for frame in contents for value in frame), "text profiles must use 21 ASCII characters per item")
        require({c for frame in contents for value in frame for c in value} == set("Canvas frame0123456789"), "static/dynamic text glyph alphabet differs")
        require([sha(canonical(frame)) for frame in contents] == trace["metadata"]["frameTextContentSha256"], "per-frame text content hash mismatch")
    if trace["inputAssertions"]["dynamicFrameInputsMustBeUnique"]:
        require(len(set(frame_hashes)) == len(frame_hashes), "dynamic frames repeated")
        require(len(dynamic_values) == warm + measure, "dynamic input absent")
        for object_values in zip(*dynamic_values):
            require(len({sha(canonical(value)) for value in object_values}) == warm + measure, "individual dynamic input repeated")
    if trace["case"] == "vector-recreate":
        require(len(set(frame_hashes)) == 1 and not trace["paths"], "same-value rebuild must be logical poly-new each frame")
    if trace["case"] == "cache-pressure":
        pool = len(trace["paths"])
        require(pool in (256, 4096, 65536) and warm == max(60, math.ceil(pool / 128) * 2) and measure == 120, "cache-pressure configuration")
        require(all(count == 128 for count in draw_counts), "pressure must draw 128 paths/frame")
        usage = Counter(op[1] for frame in trace["frames"][:warm] for op in frame["ops"])
        cycles = warm * 128 // pool
        require(set(usage) == set(range(pool)) and cycles >= 2 and set(usage.values()) == {cycles}, "warmup must cover the pool uniformly at least twice")
        require(trace["workload"]["warmupPoolCycles"] == cycles, "declared warmup pool cycles mismatch")
        require(len({sha(canonical(points)) for points in trace["paths"]}) == pool, "pressure paths are not unique")
    if trace["samplingProfile"] == "native-scaled":
        require(trace["qualityComparable"] is False, "scaled native profile cannot claim fixed-quality comparability")
    count = validate_fixture(trace, image) if trace["case"] == "semantic-fixture" else 0
    return {"id": entry["id"], "status": "passed", "traceSha256": sha(data), "frames": warm + measure,
            "drawsPerFrame": trace["drawsPerFrame"], "semanticProbeCount": count}


def validate_png(trace, png_path):
    image = Image.open(png_path).convert("RGBA")
    require(image.size == (1280, 800), "semantic PNG must be exactly 1280x800 canvas pixels")
    result = []
    for category, probe in probes(trace):
        x, y, w, h = probe["rect"]
        pixels = list(image.crop((x, y, x + w, y + h)).getdata())
        maximum = max(abs(pixel[i] - probe["expectedRgba"][i]) for pixel in pixels for i in range(4))
        result.append({"name": probe["name"], "category": category, "maxChannelError": maximum,
                       "allowed": probe["maxChannelError"], "passed": maximum <= probe["maxChannelError"]})
    text_result = []
    for region in trace.get("semanticChecks", {}).get("textRegions", []):
        x, y, w, h = region["rect"]
        pixels = list(image.crop((x, y, x + w, y + h)).getdata())
        background = tuple(round(v * 255) for v in rgba(trace["background"]))
        changed = sum(any(abs(pixel[i] - background[i]) > 10 for i in range(3)) for pixel in pixels)
        text_result.append({"text": region["text"], "nonBackgroundPixels": changed, "passed": changed > 8,
                            "limitation": "nonempty strokes only; exact font hash/content/origin must also match producer declarations"})
    return {"image": str(png_path), "sha256": sha(png_path.read_bytes()), "probes": result, "textRegions": text_result,
            "passed": all(item["passed"] for item in result + text_result),
            "qualityRegions": trace.get("semanticChecks", {}).get("separateQualityRegions", []),
            "qualityNote": "AA edges, native stroke, gradient and scaled sampler boundaries remain separate; no raw-pixel equality claim."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=ROOT / "Comprehensive" / "manifest.json")
    parser.add_argument("--output", type=Path, default=ROOT / "Comprehensive" / "validation.json")
    parser.add_argument("--trace", type=Path, help="Specific trace used for --png or --result")
    parser.add_argument("--png", type=Path, help="Existing semantic canvas PNG; does not launch GUI")
    parser.add_argument("--result", type=Path, help="Existing producer JSON; validate hash/count/dimensions")
    args = parser.parse_args()
    manifest = json.loads(args.manifest.read_bytes())
    entries = manifest["benchmarkTraces"] + manifest["semanticFixtures"] + manifest.get("calibrationTraces", [])
    require(len(entries) == manifest["traceCount"], "manifest count mismatch")
    require(sha((args.manifest.parent / manifest["schema"]).read_bytes()) == manifest["schemaSha256"], "schema SHA mismatch")
    require(sha((ROOT / "tools" / "generate-comprehensive.py").read_bytes()) == manifest["generatorSha256"], "generator SHA mismatch; regenerate after source changes")
    results, errors = [], []
    for entry in entries:
        try:
            results.append(validate_trace(args.manifest.parent / entry["trace"], entry, manifest))
        except (ValueError, KeyError, IndexError, TypeError, OSError) as error:
            errors.append({"id": entry["id"], "error": str(error)})
    report = {"protocolVersion": 11, "status": "failed" if errors else "passed", "manifestSha256": sha(args.manifest.read_bytes()),
              "manifestComplete": manifest["complete"], "traceCount": len(entries), "passed": len(results), "failed": len(errors),
              "traces": results, "errors": errors, "validatorSha256": sha(Path(__file__).read_bytes()), "execution": "CPU input/analytic-probe validation only; no GUI or GPU benchmark"}
    if args.png or args.result:
        require(args.trace is not None, "--trace required with --png/--result")
        trace = json.loads(args.trace.read_bytes())
        if args.png:
            report["semanticImage"] = validate_png(trace, args.png)
            if not report["semanticImage"]["passed"]:
                report["status"] = "failed"
        if args.result:
            produced = json.loads(args.result.read_bytes())
            checks = {"hash": produced.get("traceSha256", "").lower() == sha(args.trace.read_bytes()),
                      "opcodes": produced.get("actualMeasuredOpcodeTotals") == trace["expectedMeasuredOpcodeTotals"],
                      "dimensions": (produced.get("clientWidth"), produced.get("clientHeight"), produced.get("devicePixelRatio")) == (1280, 800, 1),
                      "frames": (produced.get("warmupFrames"), produced.get("measureFrames")) == (trace["warmupFrames"], trace["measureFrames"])}
            report["producerResult"] = {"path": str(args.result), "sha256": sha(args.result.read_bytes()), "checks": checks, "passed": all(checks.values())}
            if not all(checks.values()):
                report["status"] = "failed"
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(canonical(report))
    print(f'{report["status"]}: {len(results)}/{len(entries)} traces, {sum(item["semanticProbeCount"] for item in results)} analytic probes; output={args.output}')
    for error in errors:
        print(f'{error["id"]}: {error["error"]}')
    raise SystemExit(0 if report["status"] == "passed" else 1)


if __name__ == "__main__":
    main()
