# Electron Canvas2D v11 producer

This is the isolated v11 adapter. It uses the existing Electron 44.7.2 runtime in the old sample without changing that sample or installing npm dependencies. The default renderer/backend is Chromium Canvas2D/Skia; the result records actual GPU feature status and basic GPU information instead of assuming acceleration.

From the repository root, a semantic verification run is:

```powershell
& '.\samples\Square.Sample.CanvasBenchmark\Electron\.runtime\electron.exe' `
  '.\samples\Square.Sample.CanvasBenchmark\V11\Electron' `
  --trace 'C:\.AA\dotnet-projects\Square\samples\Square.Sample.CanvasBenchmark\V11\Traces\semantic-fixture-core\trace.json' `
  --output 'C:\.AA\dotnet-projects\Square\samples\Square.Sample.CanvasBenchmark\V11\Results\electron-semantic.json' `
  --tag 1 --verify 1
```

For performance, supply the selected full trace, a distinct output path and run tag, and `--verify 0`. All benchmark arguments are pairs. `--trace`, `--output`, and `--tag` are required; `--verify` defaults to 0. Paths must be absolute; tag is 1–255. Electron selects its Canvas2D backend, so the Square-specific `--backend` switch is not accepted. Runner-owned `--no-sandbox`, `--enable-logging[=...]` and `--log-file=...` are tolerated; this adapter does not add `--no-sandbox` or alter the user's environment.

The content area and backing canvas are 1280×800 with forced scale factor and checked DPR=1. The window has a normal title bar outside the content area, no menu or CSS scaling, and is shown without taking keyboard focus. The external observer owns window placement/z-order and the common desktop completion metric. No Electron window, GUI, GPU or benchmark was launched during implementation.

## Sequence and resources

The adapter reads exactly `frames[0..warmupFrames+measureFrames-1]` once, with one logical frame per rAF callback. It never chooses a phase from wall time, skips a logical input or repeats a frame pool. Every drawing/state opcode is executed and counted in measurement. The final subsequent rAF prepares and redraws the final trace body, then draws phase3/seq=`measureFrames`; this END redraw is excluded from measurement counters/timers. It holds that final scene and marker for 750ms, writes output and exits.

Trace parsing, structure/reference/stack/count checks, image decoding and explicit shared-font registration happen before sampling. `@font-face` registers the exact trace-directory `font.ttf` as `CanvasBench`; registration failures abort. Source, trace, font and image SHA256 values are recorded. Static path variants are created only for referenced fill/clip or open-stroke uses. Static text calls `measureText` once. Only the current frame creates `poly-new`, `stroke-new`, `clip-new` Path2D objects and `text-new` TextMetrics; the adapter retains no dynamic frame pool.

A layer uses an alpha canvas bounded to its extent. A resource can be reused for another group of the same extent, but every `layer` clears it and redraws every child. Child drawing uses alpha 1; opacity is applied once when the completed group is composited. Native clipping uses Path2D and native transforms use Canvas2D postmultiplication (`CTM=parent*local`). The protocol's top-level, non-nested layer restriction is checked before starting. Layer extents larger than 1280×800 are rejected.

Common image-grid/mixed traces use full-source 1:1 pixel-aligned images and `imageSmoothingEnabled=false`. Input accepts only complete-source 1×/2× extents. When `trace.samplingProfile` is `native-scaled`, contexts use `imageSmoothingEnabled=true` and quality `low`, report `actualImageSampling=linear-low`, and mark the native sampler profile separately from common fixed-quality image conclusions. Other profiles record `nearest`.

## Result schema

Successful JSON contains these fields. Millisecond values are numbers unless explicitly nullable.

| Field | Shape / boundary |
| --- | --- |
| `protocolVersion`, `framework`, `backend`, `status` | `11`, `Electron`, `Canvas2D`, `ok` |
| `case`, `level`, `seed`, `tag`, `verify`, `timingEligible` | Trace/run identity; verification always has `timingEligible=false` |
| `traceSha256`, `sourceSha256`, `assets` | Raw input hash; per-source hashes; font/image hashes and explicit font registration |
| `clientWidth`, `clientHeight`, `devicePixelRatio` | Checked actual content/backing conditions: 1280, 800, 1 |
| `samplingProfile`, `actualImageSampling`, `qualityQualification` | Input sampling profile, actual settings, qualification boundary |
| `warmupFrames`, `measureFrames` | Exact trace counts |
| `assetLoadMs`, `staticPrepareMs` | Input/hash/decode/font setup and static native preparation, before sampling |
| `frameCpu[]` | One measurement item: `{seq,nativePrepareMs,recordSubmitMs,presentMs:null,totalMs}` |
| `producerFrameTimes[]` | Every logical frame: `{logicalIndex,phase,seq,rafTimestampMs,callbackStartMs,recordEndMs}` |
| `actualMeasuredOpcodeTotals`, `expectedMeasuredOpcodeTotals` | Includes drawing and state operations; excludes clear/marker; exact equality required |
| `nativeAssets` | Closed/open path, static text, offscreen resource counts and bounded RGBA byte estimate |
| `versions` | Electron/Chromium/V8/Node runtime versions and Chromium process sandbox state |
| `hardware` | Electron `gpuFeatureStatus`, `basicInfo=app.getGPUInfo('basic')`, and `completeInfo=app.getGPUInfo('complete')` for available adapter/Skia/ANGLE details; queries run after frame timing |
| `verificationScreenshots[]` | Measurement seq and absolute output PNG path, verification only |
| `diagnostics` | Warmup CPU frames; renderer asset/first-draw timestamps; END timestamp; content bounds; CPU/API and quality limitations |

`nativePrepareMs` includes all current-frame dynamic path/TextMetrics construction. `recordSubmitMs` includes all synchronous Canvas2D drawing/state operations, actual opcode increments and marker calls. `totalMs` ends before diagnostic array bookkeeping, PNG readback/encoding and output. The renderer clock is `performance.now()`; rAF timestamps use its time origin. `firstDrawMs` and `assetsReadyMs` are relative to renderer startup, while producer frame timestamps/END are absolute within that renderer time origin. Startup diagnostic values do not claim process-cold completeness.

Canvas2D exposes no synchronous present or GPU-completion callback. `presentMs` is null. rAF times describe producer scheduling only, and must not be compared to Vulkan fence or other frameworks' GPU/raster completion as a shared FPS metric. The external composited-desktop marker observer supplies the shared metric. It also measures process-tree CPU/private bytes, including Electron's main/renderer/GPU processes; this adapter's `offscreenBytesRgba` is only a resource-size estimate, not process memory.

A failure after configuration attempts to preserve a JSON with `status=failed`, `timingEligible=false`, trace/tag/runtime identity and error. CLI failures exit 2; runtime failures exit 1. Valid slow samples are retained without filtering.

## Verification and limitations

`--verify 1` executes the complete supplied trace; use the short semantic fixture for a short verification. When present, measurement seq0, seq17 and seq37 are read back from the canvas after that frame's CPU timer, before END. PNGs are written beside the JSON as `<output>.frame0000.png` etc. `--verify 0` performs no readback. Verification scheduling/timing is diagnostic and cannot enter performance samples.

All protocol opcodes are implemented. Native strokes retain Canvas2D defaults (butt caps, miter joins, miter limit 10), and linear gradients use native pad/interpolation. These profiles require the protocol's image-quality qualification, as do antialiasing and text positioning. Explicit registration proves the font file used for `CanvasBench`; missing-glyph fallback and complex shaping still require semantic/glyph coverage checks.

Canvas2D does not expose a Paragraph/TextLayout creation API. For each `text-new`, this adapter creates fresh native TextMetrics through `measureText` in native preparation, then calls `fillText` during recording. Chromium's normal font/glyph/shaping caches remain enabled. This boundary is recorded in results; it must not be described as the same native paragraph-construction API cost as Flutter/Avalonia/Square.

CPU-only checks (Node, no Electron launch):

```powershell
$nodeExe = 'C:\Users\Wuldas\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\node\bin\node.exe'
& $nodeExe --check '.\samples\Square.Sample.CanvasBenchmark\V11\Electron\main.js'
& $nodeExe --check '.\samples\Square.Sample.CanvasBenchmark\V11\Electron\preload.js'
& $nodeExe --check '.\samples\Square.Sample.CanvasBenchmark\V11\Electron\protocol.js'
& $nodeExe --check '.\samples\Square.Sample.CanvasBenchmark\V11\Electron\renderer.js'
& $nodeExe --check '.\samples\Square.Sample.CanvasBenchmark\V11\Electron\check.js'
& $nodeExe '.\samples\Square.Sample.CanvasBenchmark\V11\Electron\check.js' '.\samples\Square.Sample.CanvasBenchmark\V11\Traces'
```

The CPU check uses recording browser stubs to verify the producer control contract, native-prepare timing placement, group alpha placement, exact measurement counters, verify isolation and all marker phases. CRC is checked with independent polynomial division. It does not validate native rendering or visual quality; lead runs those checks serially.
