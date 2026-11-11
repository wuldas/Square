"""Render fixed animation phases with frozen and optimized binaries, compare RGB exactly."""
import argparse
import ctypes
from ctypes import wintypes
import json
import subprocess
import time
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw

user32 = ctypes.WinDLL('user32', use_last_error=True)
callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
user32.EnumWindows.argtypes = (callback_type, wintypes.LPARAM)
user32.GetWindowThreadProcessId.argtypes = (wintypes.HWND, ctypes.POINTER(wintypes.DWORD))
user32.IsWindowVisible.argtypes = (wintypes.HWND,)
user32.SetWindowPos.argtypes = (wintypes.HWND, wintypes.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_uint)

def raise_validation_window(process):
    deadline = time.monotonic() + 20
    while process.poll() is None and time.monotonic() < deadline:
        found = []
        @callback_type
        def visit(handle, parameter):
            owner = wintypes.DWORD()
            user32.GetWindowThreadProcessId(handle, ctypes.byref(owner))
            if owner.value == process.pid and user32.IsWindowVisible(handle):
                found.append(handle)
                return False
            return True
        user32.EnumWindows(visit, 0)
        if found:
            if not user32.SetWindowPos(found[0], ctypes.c_void_p(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040):
                raise ctypes.WinError(ctypes.get_last_error())
            return
        time.sleep(.05)
    raise RuntimeError('Visual validation window did not become visible')

def changed_pixels(image):
    data = image.tobytes()
    return sum(bool(data[index] or data[index+1] or data[index+2]) for index in range(0, len(data), 3))

parser = argparse.ArgumentParser()
parser.add_argument('--baseline', type=Path, default=Path('artifacts/canvas-optimization/baseline-square/CanvasBench.Square.exe'))
parser.add_argument('--optimized', type=Path, default=Path('samples/Square.Sample.CanvasBenchmark/Square/bin/Release/net10.0/CanvasBench.Square.exe'))
parser.add_argument('--output', type=Path, default=Path('artifacts/canvas-optimization/visual'))
parser.add_argument('--prepare-only', action='store_true')
parser.add_argument('--stroke-fix', action='store_true', help='Allow Software changes only within arc/curve panels')
args = parser.parse_args()
source = Path(__file__).resolve().parent
output = args.output.resolve()
output.mkdir(parents=True, exist_ok=True)
scene = json.loads((source / 'scene.json').read_text(encoding='utf-8'))
phases, loads = (0, 17, 37), (1, 32)
for phase in phases:
    folder = output / f'input-{phase}'
    folder.mkdir(exist_ok=True)
    fixed = {**scene, 'frames': [scene['frames'][phase]] * len(scene['frames'])}
    (folder / 'scene.json').write_text(json.dumps(fixed, separators=(',', ':')), encoding='utf-8')
    (folder / 'icon.png').write_bytes((source / 'icon.png').read_bytes())
if args.prepare_only:
    print('Prepared fixed-phase visual inputs:', output)
    raise SystemExit(0)
for phase in phases:
    for load in loads:
        for backend in ('Software', 'Direct2D'):
            for label, executable in (('before', args.baseline), ('after', args.optimized)):
                stem = f'{label}-{backend.lower()}-phase{phase}-load{load}'
                result = output / f'{stem}.json'
                print('Rendering', stem, flush=True)
                with (output / f'{stem}.stdout.log').open('w', encoding='utf-8') as stdout, (output / f'{stem}.stderr.log').open('w', encoding='utf-8') as stderr:
                    process = subprocess.Popen([str(executable.resolve()), '--scene', str(output / f'input-{phase}' / 'scene.json'),
                        '--output', str(result), '--load', str(load), '--seconds', '1', '--warmup', '.5', '--backend', backend, '--label', label],
                        stdout=stdout, stderr=stderr)
                    try:
                        raise_validation_window(process)
                        code = process.wait(timeout=60)
                        if code:
                            raise subprocess.CalledProcessError(code, process.args)
                    finally:
                        if process.poll() is None:
                            process.kill()
                            process.wait()
                with Image.open(result.with_suffix('.bmp')) as capture:
                    capture.convert('RGB').save(result.with_suffix('.png'))
comparisons = []
for phase in phases:
    for load in loads:
        for backend in ('software', 'direct2d'):
            stem = f'{backend}-phase{phase}-load{load}'
            with Image.open(output / f'before-{stem}.png') as before, Image.open(output / f'after-{stem}.png') as after:
                assert before.size == after.size == (800, 600)
                delta = ImageChops.difference(before, after)
                changed = changed_pixels(delta)
                maximum = max(high for low, high in delta.getextrema())
                outside = delta.copy()
                if args.stroke_fix and backend == 'software':
                    draw = ImageDraw.Draw(outside)
                    draw.rectangle((0, 95, 799, 184), fill=(0, 0, 0))
                    draw.rectangle((0, 195, 799, 399), fill=(0, 0, 0))
                outside_changed = changed_pixels(outside)
                expected = outside_changed == 0 if args.stroke_fix else changed == 0
                comparisons.append({'backend': backend, 'phase': phase, 'load': load,
                    'changedPixels': changed, 'outsideExpectedRegions': outside_changed,
                    'maximumChannelDifference': maximum, 'exactMatch': changed == 0, 'expected': expected})
                if changed:
                    delta.save(output / f'diff-{stem}.png')
report = {'metric': 'Fixed-phase RGB comparison; stroke-fix mode permits Software changes only in arc/curve panels; no visual-fixture timing used for performance',
    'strokeFix': args.stroke_fix, 'comparisons': comparisons,
    'allExact': all(row['exactMatch'] for row in comparisons), 'allExpected': all(row['expected'] for row in comparisons)}
if args.stroke_fix:
    report['fixChangesVisible'] = any(row['backend'] == 'software' and row['phase'] == 0 and row['load'] == 1 and row['changedPixels'] > 0 for row in comparisons)
    report['allExpected'] = report['allExpected'] and report['fixChangesVisible']
(output / 'pixel-comparison.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report, indent=2))
raise SystemExit(0 if report['allExpected'] else 1)
