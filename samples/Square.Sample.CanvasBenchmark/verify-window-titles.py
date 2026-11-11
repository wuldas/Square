"""Check real Win32 title bars and 800x600 clients for all six benchmark targets."""
import argparse
import ctypes
from ctypes import wintypes
import json
import os
from pathlib import Path
import subprocess
import time
from PIL import ImageGrab

root = Path(__file__).resolve().parents[2]
bench = Path(__file__).resolve().parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--version', type=int, default=5)
parser.add_argument('--output', type=Path)
args = parser.parse_args()
if args.version < 1:
    parser.error('--version must be positive')
output = (args.output or root / f'artifacts/canvas-optimization/ui-v{args.version}').resolve()
output.mkdir(parents=True, exist_ok=True)
user32 = ctypes.WinDLL('user32', use_last_error=True)
callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
user32.EnumWindows.argtypes = (callback_type, wintypes.LPARAM)
user32.GetWindowThreadProcessId.argtypes = (wintypes.HWND, ctypes.POINTER(wintypes.DWORD))
user32.IsWindowVisible.argtypes = (wintypes.HWND,)
user32.GetWindowTextW.argtypes = (wintypes.HWND, wintypes.LPWSTR, ctypes.c_int)
user32.GetClientRect.argtypes = (wintypes.HWND, ctypes.POINTER(wintypes.RECT))
user32.GetWindowRect.argtypes = (wintypes.HWND, ctypes.POINTER(wintypes.RECT))
user32.GetWindowLongW.argtypes = (wintypes.HWND, ctypes.c_int)
user32.SetWindowPos.argtypes = (wintypes.HWND, wintypes.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_uint)
user32.ClientToScreen.argtypes = (wintypes.HWND, ctypes.POINTER(wintypes.POINT))
dwmapi = ctypes.WinDLL('dwmapi')
dwmapi.DwmGetWindowAttribute.argtypes = (wintypes.HWND, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD)
user32.SetProcessDpiAwarenessContext.argtypes = (wintypes.HANDLE,)
user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))

def visible_window(pid):
    found = []
    @callback_type
    def visit(handle, parameter):
        owner = wintypes.DWORD()
        user32.GetWindowThreadProcessId(handle, ctypes.byref(owner))
        if owner.value == pid and user32.IsWindowVisible(handle):
            text = ctypes.create_unicode_buffer(512)
            user32.GetWindowTextW(handle, text, len(text))
            if text.value:
                found.append((handle, text.value))
                return False
        return True
    user32.EnumWindows(visit, 0)
    return found[0] if found else None

targets = [
    ('square-software', bench/'Square/bin/Release/net10.0/CanvasBench.Square.exe', [], ['--backend', 'Software'], 'Square Software'),
    ('square-direct2d', bench/'Square/bin/Release/net10.0/CanvasBench.Square.exe', [], ['--backend', 'Direct2D'], 'Square Direct2D'),
    ('square-vulkan', bench/'Square/bin/Release/net10.0/CanvasBench.Square.exe', [], ['--backend', 'Vulkan'], 'Square Vulkan'),
    ('electron', bench/'Electron/.runtime/electron.exe', ['--no-sandbox', str(bench/'Electron/main.js')], [], 'Electron Canvas2D'),
    ('flutter', bench/'Flutter/build/windows/x64/runner/Release/square_canvas_flutter.exe', [], [], 'Flutter CustomPainter'),
    ('avalonia', bench/'Avalonia/bin/Release/net10.0/CanvasBenchmark.Avalonia.exe', [], [], 'Avalonia Canvas'),
]
manifest_path = output / f'window-titles-v{args.version}.json'
planned_files = [manifest_path] + [output / f'{name}{suffix}'
    for name, *_ in targets for suffix in ('.json', '.stdout.log', '.stderr.log', '-window.png')]
existing = next((path for path in planned_files if path.exists()), None)
if existing:
    raise SystemExit(f'Preserve previous window evidence; use a fresh --output directory: {existing}')
results = []
for name, exe, prefix, extra, expected in targets:
    result_path = output / f'{name}.json'
    env = os.environ.copy()
    for key in ('ELECTRON_RUN_AS_NODE', 'ELECTRON_FORCE_RENDERER_ACCESSIBILITY'):
        env.pop(key, None)
    if name == 'electron':
        env['TEMP'] = env['TMP'] = str(bench/'Electron/.tmp')
    command = [str(exe), *prefix, '--scene', str(bench/'scene.json'), '--output', str(result_path),
        '--load', '1', '--seconds', '2', '--warmup', '1', *extra]
    print('Checking', name, flush=True)
    with (output/f'{name}.stdout.log').open('w', encoding='utf-8') as stdout, (output/f'{name}.stderr.log').open('w', encoding='utf-8') as stderr:
        process = subprocess.Popen(command, cwd=root, env=env, stdout=stdout, stderr=stderr)
        try:
            deadline = time.monotonic() + 20
            window = None
            while process.poll() is None and time.monotonic() < deadline:
                window = visible_window(process.pid)
                if window:
                    break
                time.sleep(.05)
            if window is None:
                raise RuntimeError(f'{name}: no visible benchmark window')
            handle, title = window
            client, outer = wintypes.RECT(), wintypes.RECT()
            assert user32.GetClientRect(handle, ctypes.byref(client))
            assert user32.GetWindowRect(handle, ctypes.byref(outer))
            style = user32.GetWindowLongW(handle, -16) & 0xffffffff
            assert expected in title and '1x' in title and '标题验证' not in title, title
            assert style & 0x00c00000 == 0x00c00000, (name, hex(style))
            assert (client.right-client.left, client.bottom-client.top) == (800, 600), (name, client.right, client.bottom)
            # Only this short-lived validation window is raised; it disappears on exit.
            assert user32.SetWindowPos(handle, ctypes.c_void_p(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040)
            time.sleep(.2)
            visible_frame = wintypes.RECT()
            if dwmapi.DwmGetWindowAttribute(handle, 9, ctypes.byref(visible_frame), ctypes.sizeof(visible_frame)) == 0:
                outer = visible_frame
            screenshot = output/f'{name}-window.png'
            capture = ImageGrab.grab(bbox=(outer.left, outer.top, outer.right, outer.bottom))
            anchor = wintypes.POINT(5, 5)
            assert user32.ClientToScreen(handle, ctypes.byref(anchor))
            color = capture.getpixel((anchor.x-outer.left, anchor.y-outer.top))[:3]
            assert max(abs(a-b) for a,b in zip(color, (16,24,39))) <= 2, (name, 'window is occluded', color)
            capture.save(screenshot)
            code = process.wait(timeout=45)
            assert code == 0, (name, code)
            data = json.loads(result_path.read_text(encoding='utf-8-sig'))
            assert data['clientWidth'] == 800 and data['clientHeight'] == 600 and data['frames'] > 0, name
            equivalent = data.get('renderingEquivalent') is True if name == 'square-vulkan' else data.get('renderingEquivalent', True) is True
            limitations = data.get('renderingLimitations', [])
            assert isinstance(limitations, list) and all(isinstance(value, str) for value in limitations), name
            results.append({'target': name, 'reportVersion': args.version, 'title': title, 'hasCaption': True,
                'client': [800, 600], 'screenshot': screenshot.name, 'renderingEquivalent': equivalent,
                'renderingLimitations': limitations, 'visualReview': 'Manual content review required; title/client/background checks do not prove equal image quality.'})
        finally:
            if process.poll() is None:
                process.kill()
                process.wait()
manifest_path.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
print('Verified visible captions, labels and client dimensions for', len(results), 'targets;', manifest_path)
