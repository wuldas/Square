# Flutter Canvas v11

Fixed logical trace producer. Source and build output are isolated from the legacy Flutter benchmark; the existing SDK and Pub cache are reused.

Build only (PowerShell, no window or benchmark launch):

```powershell
& .\build.ps1
```

This runs offline dependency resolution, Dart format validation, Flutter analyze, and a Windows release build. The script reuses `../../Flutter/.sdk/flutter` and `../../Flutter/.pub-cache`, sets only its process TEMP/TMP to this project's `.tmp`, and restores the previous environment afterwards. `-SkipPub` uses an already resolved project.

```powershell
& .\build.ps1 -SkipPub
```

The release build was also validated by resuming the already generated CMake project serially after the Flutter CLI hit a host Dart worker-thread resource error. From this project directory, the exact successful build-only command was:

```powershell
$env:TEMP = Join-Path $PWD '.tmp'
$env:TMP = $env:TEMP
$env:PUB_CACHE = (Resolve-Path '..\..\Flutter\.pub-cache').Path
& 'C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe' `
  --build build/windows/x64 --config Release --target INSTALL --parallel 1
```

Run those commands in a dedicated PowerShell process if using the manual CMake path. Build orchestration should remain serial across adapters on this host.

The executable and adjacent runtime files are in `build/windows/x64/runner/Release/`. Run the executable from that directory or provide its absolute path; its working directory does not determine trace assets.

```powershell
& .\build\windows\x64\runner\Release\square_canvas_v11.exe `
  --trace C:\absolute\traces\vector-reuse-low.json `
  --output C:\absolute\results\flutter-vector-reuse-low.json `
  --tag 11 --verify 0
```

The titled Windows client is exactly 1280×800 physical pixels. Use a monitor at 100% scale: Dart verifies physical size, logical size and DPR=1 and reports an error rather than changing resolution. No software-rendering engine switch is added. All paired arguments are forwarded unchanged to Dart.

Assets `font.ttf` and `image.png` are read from the trace directory. `FontLoader('CanvasBench').load()` explicitly registers the font bytes used by every text layout; trace, image and font SHA256 values are reported. Static paths and text layouts are prepared once. Each actual paint creates all dynamic paths and text layouts anew inside `nativePrepareMs`, then records every operation and the final marker. `Canvas.saveLayer` supplies group opacity, `clipPath` supplies geometry clipping, and image sampling is `FilterQuality.none` except the separate `samplingProfile` containing `native-scaled`, where it is `FilterQuality.low` as agreed for the native linear profile. Native strokes use Flutter default cap/join. Input frames never wrap or follow wall-clock phase selection.

`--verify 1` executes the same full trace and validates the same measurement opcode totals, with `timingEligible=false` and `verificationOnly=true`. Only measurement sequence 0, 17 and 37, when present, are read back and saved outside the frame CPU timer as `<output>.frame0.png`, `.frame17.png` and `.frame37.png`. Use the shared one-frame semantic trace for a short qualification run. `--verify 0` performs no readback.

After all measurement frames, an additional end-marker paint redraws the last scene with phase 3 / sequence `measureFrames`, holds it for at least 750ms, writes JSON and exits. Its CPU sample is separate from measurement. Main completion results come from the lead's external desktop marker observer. `frameCpu` measures native preparation plus CustomPainter Canvas recording, not GPU completion; `presentMs` is null because Flutter does not expose that boundary here. Engine `FrameTiming` events remain under `diagnostics`, include whatever startup/warmup/measurement/end batches are reported before exit, and are never used as a substitute FPS metric.
