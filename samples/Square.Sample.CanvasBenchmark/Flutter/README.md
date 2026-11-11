# Flutter Windows Canvas benchmark

Flutter 3.47.7 / Dart 3.13.5, no third-party packages. A CustomPainter issues all prepared Canvas commands on every engine vsync. Text layout, paints, image and paths are cached before warmup; full scenes/pictures are not cached. Curves use the common 32-segment polyline, clip + gradient rectangle, and explicit filled circle caps for arcs.

Build with Visual Studio C++ desktop tools and Flutter Windows support:

```powershell
.\Build.ps1 -FlutterSdk C:\absolute\flutter
```

The default SDK path is `.sdk/flutter`. The build script isolates Pub and temporary build files locally and embeds Flutter/engine versions. On this machine a cached Flutter 3.47.7 SDK zip was extracted to that directory.

```powershell
& .\build\windows\x64\runner\Release\square_canvas_flutter.exe --scene C:\absolute\scene.json --output C:\absolute\flutter.json --load 1 --seconds 5 --warmup 2 --screenshot
```

`--screenshot` writes `flutter.json.png` after timing. Errors write `flutter.json.error.txt` and exit with status 1. Keep the window visible and run all benchmarks sequentially.

The native title bar shows `Flutter CustomPainter | <load>x`. Optional `--label <text>` appends ` | <text>`, for example `Flutter CustomPainter | 32x | after`. The Windows runner consumes the label before forwarding benchmark arguments to Dart. Omitting it preserves the default title and options. The client canvas remains 800x600.

`frames` counts rasterized FrameTiming entries whose rasterFinishWallTime falls inside the configured wall-clock window. Timings are drained after measurement to include delayed release-mode batches. `frameIntervalsMs` uses monotonic raster-finish timestamps; `drawTimesMs` is engine raster duration. Additional `uiPaintCount` and `uiDrawTimesMs` describe CustomPainter command submissions. These stages differ from Electron's rAF and Square's presentation event, and physical scanout is not observable. Flutter uses its default Windows rendering pipeline/vsync; its exact runtime renderer has no public query here and is reported as unqueried.
