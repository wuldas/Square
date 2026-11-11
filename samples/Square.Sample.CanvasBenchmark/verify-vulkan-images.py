"""Check shared Canvas Vulkan curves against math, including a required v3 negative control.

Default: validate six existing GPU captures. --prepare-only never starts a process.
Only --capture launches the six sequential fixed-phase visual runs; these are not FPS data.
"""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

SOURCE = Path(__file__).resolve().parent
REPOSITORY = SOURCE.parents[1]
PHASES = (0, 17, 37)
LOADS = (1, 32)
TOLERANCE = {1: 3, 32: 5}
EDGE_MARGIN = 4.0


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def save_new(path, content):
    data = content if isinstance(content, bytes) else content.encode('utf-8')
    if path.exists():
        if path.read_bytes() != data:
            raise RuntimeError(f'Refusing to overwrite existing evidence: {path}')
        return
    with path.open('xb') as stream:
        stream.write(data)


def save_json(path, value):
    save_new(path, json.dumps(value, ensure_ascii=False, indent=2) + '\n')


def color(value):
    raw = value.removeprefix('#')
    if len(raw) not in (6, 8):
        raise ValueError(f'Unsupported color: {value}')
    result = [int(raw[index:index+2], 16) for index in range(0, len(raw), 2)]
    return np.array(result + ([255] if len(result) == 3 else []), dtype=np.float64)


def curve_points(operation, segments):
    # The shared C# scene uses float32 arithmetic and includes both endpoints.
    t = np.arange(segments + 1, dtype=np.float32) / np.float32(segments)
    u = np.float32(1) - t
    coordinates = []
    for axis in (1, 2):
        values = [np.float32(operation[axis + 2 * index]) for index in range(4)]
        coordinates.append(u*u*u*values[0] + 3*u*u*t*values[1] +
                           3*u*t*t*values[2] + t*t*t*values[3])
    return np.column_stack(coordinates).astype(np.float64)


def distance_squared(x, y, points):
    minimum = np.full(x.shape, np.inf)
    for start, end in zip(points[:-1], points[1:]):
        delta = end - start
        length = float(np.dot(delta, delta))
        if length == 0:
            candidate = (x-start[0])**2 + (y-start[1])**2
        else:
            fraction = np.clip(((x-start[0])*delta[0] + (y-start[1])*delta[1]) / length, 0, 1)
            candidate = (x-start[0]-fraction*delta[0])**2 + (y-start[1]-fraction*delta[1])**2
        minimum = np.minimum(minimum, candidate)
    return minimum


def region_statistics(error, mask, actual, expected, x, y, tolerance):
    mismatch = mask & (error > tolerance)
    positions = np.argwhere(mismatch)
    examples = []
    for row, column in positions[:8]:
        examples.append({'x': int(x[row, column]-.5), 'y': int(y[row, column]-.5),
                         'actual': actual[row, column].tolist(),
                         'expected': expected[row, column].tolist(),
                         'error': int(error[row, column])})
    return {'checkedPixels': int(np.count_nonzero(mask)),
            'maxChannelError': int(error[mask].max(initial=0)),
            'mismatchedPixels': int(len(positions)), 'examples': examples}


def check_image(scene, phase, load, path):
    if load not in TOLERANCE:
        raise ValueError('Only contracted visual loads 1 and 32 are supported')
    if (scene['width'], scene['height'], scene['curveSegments']) != (800, 600, 32):
        raise ValueError('Expected unchanged 800x600 / 32-segment shared scene')
    with Image.open(path) as opened:
        if opened.size != (800, 600):
            raise ValueError(f'Unexpected image size: {path}: {opened.size}')
        image = np.asarray(opened.convert('RGB'), dtype=np.int16)
    background = color(scene['background'])
    if background[3] != 255:
        raise ValueError('The reference requires the contracted opaque background')
    operations = [operation for operation in scene['frames'][phase]['ops'] if operation[0] == 'curve']
    if len(operations) != 4:
        raise ValueError('Expected the four original shared curve panels')
    panels = []
    for index, operation in enumerate(operations):
        points = curve_points(operation, 32)
        if not np.all(np.diff(points[:, 0]) > 0) or np.max(points[:, 1]) >= operation[9]:
            raise ValueError('Expected the original x-monotone curves above their baseline')
        left, right = float(operation[1]), float(operation[7])
        top, bottom = 200.0, float(operation[9])
        y_indices, x_indices = np.mgrid[int(top):int(bottom), int(left):int(right)]
        x, y = x_indices + .5, y_indices + .5
        curve_y = np.interp(x, points[:, 0], points[:, 1])
        eligible = ((x-left >= EDGE_MARGIN) & (right-x >= EDGE_MARGIN) &
                    (y-top >= EDGE_MARGIN) & (bottom-y >= EDGE_MARGIN) &
                    (distance_squared(x, y, points) >= EDGE_MARGIN**2))
        inside = y > curve_y
        first, last = color(operation[10]), color(operation[11])
        fraction = np.clip((y-210.0) / (390.0-210.0), 0, 1)
        rgba = first[None, None, :] + (last-first)[None, None, :] * fraction[:, :, None]
        alpha = np.where(inside, rgba[:, :, 3] / 255.0, 0)[:, :, None]
        # One clear, then load paints; emulate RGBA8 attachment quantization after EVERY blend.
        expected_float = np.broadcast_to(background[:3], rgba.shape[:2]+(3,)).copy()
        for _ in range(load):
            expected_float = np.rint(rgba[:, :, :3] * alpha + expected_float * (1-alpha))
        expected = expected_float.astype(np.int16)
        actual = image[y_indices, x_indices]
        error = np.max(np.abs(actual-expected), axis=2)
        statistics = {name: region_statistics(error, mask, actual, expected, x, y, TOLERANCE[load])
                      for name, mask in (('inside', eligible & inside), ('background', eligible & ~inside))}
        if any(value['checkedPixels'] < 100 for value in statistics.values()):
            raise ValueError('Reference ROI unexpectedly empty; do not silently skip a panel')
        panels.append({'index': index, 'bounds': [left, top, right, bottom],
                       'excludedEdgePixels': int(np.size(eligible)-np.count_nonzero(eligible)), **statistics})
    checked = sum(panel[region]['checkedPixels'] for panel in panels for region in ('inside', 'background'))
    mismatched = sum(panel[region]['mismatchedPixels'] for panel in panels for region in ('inside', 'background'))
    maximum = max(panel[region]['maxChannelError'] for panel in panels for region in ('inside', 'background'))
    return {'phase': phase, 'load': load, 'image': str(path), 'sha256': sha256(path),
            'tolerance': TOLERANCE[load], 'checkedPixels': checked, 'mismatchedPixels': mismatched,
            'maxChannelError': maximum, 'passed': mismatched == 0, 'panels': panels}


def prepare(scene, output):
    for phase in PHASES:
        folder = output / f'input-{phase}'
        folder.mkdir(exist_ok=True)
        fixed = {**scene, 'frames': [scene['frames'][phase]] * len(scene['frames'])}
        save_new(folder/'scene.json', json.dumps(fixed, separators=(',', ':')))
        save_new(folder/'icon.png', (SOURCE/'icon.png').read_bytes())


def capture(executable, output, phase, load):
    stem = f'vulkan-phase{phase}-load{load}'
    result = output/f'{stem}.json'
    bmp, gpu = result.with_suffix('.bmp'), output/f'{stem}.gpu.bmp'
    if any(path.exists() for path in (result, bmp, gpu, result.with_suffix('.png'))):
        raise RuntimeError(f'Refusing to overwrite a visual run: {stem}')
    arguments = [str(executable.resolve()), '--scene', str(output/f'input-{phase}'/'scene.json'),
                 '--output', str(result), '--backend', 'Vulkan', '--readback', '1', '--validation', '1',
                 '--load', str(load), '--seconds', '1', '--warmup', '.5', '--label', f'v4 phase={phase} load={load}']
    with (output/f'{stem}.stdout.log').open('x', encoding='utf-8') as stdout, \
         (output/f'{stem}.stderr.log').open('x', encoding='utf-8') as stderr:
        subprocess.run(arguments, stdout=stdout, stderr=stderr, cwd=REPOSITORY, check=True, timeout=60)
    if not gpu.is_file():
        raise RuntimeError(f'Dedicated GPU readback was not saved: {gpu}')
    with Image.open(gpu) as opened:
        opened.convert('RGB').save(result.with_suffix('.png'))
    save_json(output/f'{stem}.provenance.json', {'source': 'Initial IRenderBitmapSource readback in the dedicated .gpu.bmp; screen capture never writes this file',
              'gpuBmpSha256': sha256(gpu), 'pngSha256': sha256(result.with_suffix('.png')),
              'sceneSha256': sha256(output/f'input-{phase}'/'scene.json')})


def validate_capture(output, phase, load):
    stem = f'vulkan-phase{phase}-load{load}'
    metadata = json.loads((output/f'{stem}.json').read_text(encoding='utf-8-sig'))
    info = metadata['versions']['vulkan']
    if (metadata['framework'] != 'Square Vulkan' or metadata['load'] != load or
        metadata['clientWidth'] != 800 or metadata['clientHeight'] != 600 or metadata['devicePixelRatio'] != 1 or
        not info['readbackEnabled'] or not info['validationEnabled'] or
        info['device']['DeviceType'] not in ('DiscreteGpu', 'IntegratedGpu')):
        raise ValueError(f'Unexpected GPU/readback settings: {stem}')
    evidence = json.loads((output/f'{stem}.provenance.json').read_text(encoding='utf-8'))
    if (evidence['gpuBmpSha256'] != sha256(output/f'{stem}.gpu.bmp') or
        evidence['pngSha256'] != sha256(output/f'{stem}.png') or
        evidence['sceneSha256'] != sha256(output/f'input-{phase}'/'scene.json')):
        raise ValueError(f'GPU capture evidence changed: {stem}')
    logs = (output/f'{stem}.stdout.log').read_text(encoding='utf-8', errors='replace') + \
           (output/f'{stem}.stderr.log').read_text(encoding='utf-8', errors='replace')
    if '[VulkanValidation]' in logs:
        raise ValueError(f'Validation-layer output requires investigation: {stem}')


def montage(before, after, path, result, version=4):
    with Image.open(before) as first, Image.open(after) as second:
        crops = [image.convert('RGB').crop((0, 195, 800, 400)) for image in (first, second)]
    image = Image.new('RGB', (800, 470), '#101827')
    draw = ImageDraw.Draw(image)
    for index, crop in enumerate(crops):
        image.paste(crop, (0, index*235+30))
        label = 'v3: negative control, phase 0 / load 1' if index == 0 else \
                f'v{version}: phase 0 / load 1, ROI max error {result["maxChannelError"]}, tolerance 3'
        draw.text((10, index*235+8), label, fill='white')
    if path.exists():
        raise RuntimeError(f'Refusing to overwrite comparison: {path}')
    image.save(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', type=int, default=4)
    parser.add_argument('--output', type=Path, default=REPOSITORY/'artifacts/canvas-optimization/visual-v4')
    parser.add_argument('--executable', type=Path, default=SOURCE/'Square/bin/Release/net10.0/CanvasBench.Square.exe')
    parser.add_argument('--negative', type=Path, default=REPOSITORY/'artifacts/canvas-optimization/preflight-v4/vulkan-v3-true-readback.gpu.bmp')
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--prepare-only', action='store_true', help='Prepare inputs and run the numerical negative control; never launch UI')
    mode.add_argument('--capture', action='store_true', help='Launch all six GPU visual runs sequentially, then check their pixels')
    args = parser.parse_args()
    output = args.output.resolve()
    if any(part in ('visual-v1', 'visual-v2', 'visual-v3', 'run-v1', 'run-v2', 'run-v3') for part in output.parts):
        raise ValueError('Historical artifact directories must not be reused')
    output.mkdir(parents=True, exist_ok=True)
    scene = json.loads((SOURCE/'scene.json').read_text(encoding='utf-8'))
    prepare(scene, output)
    negative = check_image(scene, 0, 1, args.negative.resolve())
    control = {'kind': 'Required known-bad v3 phase0/load1 negative control',
               'rejectedAsExpected': not negative['passed'], 'result': negative}
    save_json(output/f'negative-control-v{args.version}-{negative["sha256"][:12]}.json', control)
    print(f'Negative control rejected={control["rejectedAsExpected"]}, mismatch={negative["mismatchedPixels"]}, max={negative["maxChannelError"]}', flush=True)
    if not control['rejectedAsExpected']:
        raise RuntimeError('The validator failed to reject the known-bad v3 rendering')
    if args.prepare_only:
        print('Prepared fixed inputs; no rendering process was started.')
        return 0
    results = []
    for phase in PHASES:
        for load in LOADS:
            if args.capture:
                print(f'Capturing fixed phase={phase}, load={load}', flush=True)
                capture(args.executable, output, phase, load)
            validate_capture(output, phase, load)
            result = check_image(scene, phase, load, output/f'vulkan-phase{phase}-load{load}.png')
            results.append(result)
            print(f'phase={phase} load={load}: pass={result["passed"]} checked={result["checkedPixels"]} mismatch={result["mismatchedPixels"]} max={result["maxChannelError"]}', flush=True)
    report = {'reference': '32-segment float32 polyline, pixel centers, np.interp below-curve test; linear RGBA gradient from y210 to390; source-over with np.rint after every paint',
              'sceneSha256': sha256(SOURCE/'scene.json'), 'scriptSha256': sha256(Path(__file__)),
              'edgeExclusionDistancePixels': EDGE_MARGIN, 'toleranceByLoad': TOLERANCE,
              'usesPerformanceData': False, 'negativeControl': control, 'comparisons': results,
              'allPassed': all(result['passed'] for result in results)}
    save_json(output/f'curve-verification-v{args.version}.json', report)
    montage(args.negative, output/'vulkan-phase0-load1.png', output/f'curve-comparison-v{args.version}.png', results[0], args.version)
    return 0 if report['allPassed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
