# Avalonia canvas benchmark

Uses Avalonia 11.3.12 and the shared scene contract. Build from the repository root:

```powershell
dotnet build samples/Square.Sample.CanvasBenchmark/Avalonia/Avalonia.csproj -c Release
```

Run the built executable with absolute scene and output paths:

```powershell
& .\samples\Square.Sample.CanvasBenchmark\Avalonia\bin\Release\net10.0\CanvasBenchmark.Avalonia.exe `
  --scene C:\.AA\dotnet-projects\Square\samples\Square.Sample.CanvasBenchmark\scene.json `
  --output C:\.AA\dotnet-projects\Square\samples\Square.Sample.CanvasBenchmark\Avalonia\smoke.json `
  --load 1 --seconds 5 --warmup 2
```

The native title bar shows `Avalonia Canvas | <load>x`. Optional `--label <text>`
appends ` | <text>`, for example `Avalonia Canvas | 32x | after`. Omitting it
preserves the default title and benchmark options. The client canvas stays 800x600.

One custom `Control.Render` submits all instructions every time it paints. Only
individual brushes, pens, geometries, formatted text and the decoded image are
prepared and cached. The background is painted once and the scene commands are
submitted `load` times. Frames are selected from elapsed wall time at 60 Hz.
Curves use the common 32-segment polyline. Their area path clips a gradient-filled
rectangle before the open polyline stroke. Arcs use a butt-ended arc and two filled
endpoint circles. Image filtering is bilinear (`BitmapInterpolationMode.LowQuality`).

`TopLevel.RequestAnimationFrame` invalidates the canvas using Avalonia's default
native rendering pipeline. No render-loop, vsync, GPU or compositing options are
overridden. `seconds` is the configured measurement window, and `fps` divides the
Render count by that fixed duration. `stopDelaySeconds` records the extra wall time
until the exit check runs. `frames` counts measured `Control.Render` calls. `frameIntervalsMs`
contains the time between successive measured Render entries, so its length is
`frames - 1` when frames are present. `drawTimesMs` measures CPU command submission
inside Render; Avalonia's compositor and GPU rasterization occur separately.
These counters cannot observe physical presentation or scanout. Platform startup
messages and loaded renderer assembly information are included under `versions`.

The process writes JSON after the configured wall duration, then saves an offscreen
PNG beside it by changing the output extension to `.png`, and closes automatically.
The extra screenshot render happens after measurement and does not affect counters.

Avalonia API semantics were checked against the installed 11.3.12 XML documentation
and the matching upstream source for [DrawingContextImpl](https://github.com/AvaloniaUI/Avalonia/blob/11.3.12/src/Skia/Avalonia.Skia/DrawingContextImpl.cs)
and [RelativePoint](https://github.com/AvaloniaUI/Avalonia/blob/11.3.12/src/Avalonia.Base/RelativePoint.cs).
The gradient uses absolute canvas coordinates rather than relative shape bounds.
