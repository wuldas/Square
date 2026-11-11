# Electron Canvas benchmark

Uses Electron 44.7.2 / Canvas2D and the shared scene contract. Every frame clears once and issues all prepared commands `load` times. Paths, gradients and decoded image are prepared before warmup. Curves use 32 sampled segments and clip + gradient rectangle; arc caps are two filled circles.

On this machine the cached runtime is `.runtime/electron.exe`. Alternatively install the pinned dependency with `npm install` and use `node_modules/electron/dist/electron.exe`.

```powershell
Remove-Item Env:ELECTRON_RUN_AS_NODE -ErrorAction SilentlyContinue
Remove-Item Env:ELECTRON_FORCE_RENDERER_ACCESSIBILITY -ErrorAction SilentlyContinue
$env:TEMP = Join-Path $PWD '.tmp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force $env:TEMP | Out-Null
& .\.runtime\electron.exe --no-sandbox . --scene C:\absolute\scene.json --output C:\absolute\electron.json --load 1 --seconds 5 --warmup 2 --screenshot
```

Run from this directory. The first Electron argument may also be the absolute path to `main.js`. `--screenshot` adds `electron.json.png` after the measured window. Keep the window visible and run benchmarks sequentially.

The native title bar shows `Electron Canvas2D | <load>x`. Optional `--label <text>` appends ` | <text>`, for example `Electron Canvas2D | 32x | after`. Omit it for the default title. The client canvas remains 800x600 and the title does not enter drawing or measurement.

The DSH Windows launch environment fails Electron's native process-sandbox initialization with exception `0x80000003`; a version-only control succeeds with `--no-sandbox` and fails without it. The benchmark therefore uses that compatibility flag and records `chromiumProcessSandbox=false`. It retains default hardware acceleration and vsync. Chromium user data and temporary files stay in `.user-data` and `.tmp`.

`frames` counts requestAnimationFrame callbacks inside the configured wall-clock measurement window. `drawTimesMs` measures synchronous Canvas2D submission. Chromium may defer painting or GPU work; these callbacks are not confirmed physical scanout. The JSON records Electron/Chromium/Node versions, GPU feature status and GPU adapter data. Vsync and the native pipeline retain their defaults.
