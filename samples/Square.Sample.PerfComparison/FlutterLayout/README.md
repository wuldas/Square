# flutter_layout

Flutter Windows target of the 720-box layout-animation comparison in
`samples/Square.Sample.PerfComparison` (next to `Chrome/index.html`,
`Square/CssAnimationPerfPage.sqv` and `Avalonia/Program.cs`).

`lib/main.dart` reproduces the same workload: three groups of 240 boxes, each a
6s linear infinite triangle wave:

| group | color | animation | cost |
|---|---|---|---|
| transform | `#EF4444` | three nested 30x30 layers: translate 0→36→0, rotate 0→180→360deg, scale 0.6→1.4→0.6 around an 18x18 core (6px padding) | paint only |
| opacity | `#3B82F6` | 30x30 boxes, opacity 0→1→0 | paint only |
| width | `#F59E0B` | 30px tall boxes, width 8→30→8 | re-wraps the Wrap rows every frame (layout) |

The window is a fixed 900x1240 client at DPR 1: `windows/runner/win32_window.cpp`
treats the requested size as physical client pixels, so on a 96 DPI display the
logical client is exactly 900x1240 and the engine reports `devicePixelRatio == 1`.

## Build and run

```powershell
$root = 'C:\.AA\dotnet-projects\Square\samples\Square.Sample.PerfComparison\FlutterLayout'
$env:TEMP = "$root\.tmp"; $env:TMP = $env:TEMP
$env:PUB_CACHE = 'C:\.AA\dotnet-projects\Square\samples\Square.Sample.CanvasBenchmark\Flutter\.pub-cache'
$flutter = 'C:\.AA\dotnet-projects\Square\samples\Square.Sample.CanvasBenchmark\Flutter\.sdk\flutter\bin\flutter.bat'

& $flutter pub get --offline
& $flutter build windows --release --no-pub
& "$root\build\windows\x64\runner\Release\flutter_layout.exe" --seconds 5
```

After a 1s warmup the app counts presented frames with
`SchedulerBinding.addTimingsCallback` for `--seconds` (default 30), prints one
JSON line to stdout and exits:

```json
{"target":"Flutter","seconds":30.01,"frames":1800,"fps":60.0,"client":"900x1240","dpr":1,"boxes":720,"groups":[240,240,240]}
```

CPU and memory are sampled by the external runner, not by the app.
