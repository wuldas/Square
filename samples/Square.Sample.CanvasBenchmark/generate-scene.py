"""Generate deterministic shared commands; requires Pillow only for the icon."""
import json
import math
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent
frames = []
for frame in range(60):
    phase = frame / 60 * math.tau
    ops = []
    # Ruler: independent short strokes and labels that appear/disappear.
    for i in range(120):
        x = 20 + i * 6.3
        h = 25 if i % 10 == 0 else 14 if i % 5 == 0 else 7
        y = 50 + math.sin(phase) * 3
        ops.append(["line", x, y, x, y + h, "#94A3B8", 1])
        if i % 10 == 0 and (i // 10 + frame // 15) % 3 != 0:
            ops.append(["text", str(i * 10), x, 22, 11, "#E2E8F0"])
    # Circles: full rings and animated arcs with round caps.
    for i in range(12):
        cx, cy, radius = 43 + i * 65, 141, 21 + 3 * math.sin(phase + i)
        ops.append(["circle", cx, cy, radius, "#334155", 3])
        ops.append(["arc", cx, cy, radius, phase + i * .3, 1.2 + .7 * (1 + math.sin(phase + i)), "#38BDF8", 4])
    # Bezier-derived line charts: all renderers use the same 32-segment sampling.
    for i in range(4):
        x = 20 + i * 195
        y = 280 + 24 * math.sin(phase + i)
        ops.append(["curve", x, y, x + 50, 200 + 25 * math.cos(phase + i), x + 120, 370 - 25 * math.sin(phase + i), x + 175, 260 + 20 * math.cos(phase + i), 390, "#22C55EBB", "#22C55E10", 2])
    # Bars: semi-transparent so repeated workloads cannot be eliminated as opaque overdraw.
    for i in range(96):
        h = 18 + 85 * (.5 + .5 * math.sin(phase + i * .31))
        ops.append(["rect", 20 + i * 7.95, 515 - h, 5.5, h, "#F59E0BBB"])
    # Icons and short static labels.
    for i in range(16):
        x, y = 20 + (i % 8) * 97, 535 + (i // 8) * 30
        ops.append(["image", x, y, 22, 22])
        ops.append(["text", f"Item {i:02}", x + 27, y + 3, 12, "#E2E8F0"])
    frames.append({"ops": ops})
scene = {"width": 800, "height": 600, "background": "#101827", "animationHz": 60, "curveSegments": 32, "frames": frames}
(root / "scene.json").write_text(json.dumps(scene, separators=(",", ":")), encoding="utf-8")
icon = Image.new("RGBA", (24, 24), (0, 0, 0, 0))
draw = ImageDraw.Draw(icon)
draw.rounded_rectangle((1, 1, 22, 22), radius=5, fill=(37, 99, 235, 255))
draw.line([(6, 15), (10, 8), (14, 15), (18, 8)], fill=(255, 255, 255, 255), width=2)
icon.save(root / "icon.png")
print(f"Generated {len(frames)} frames; {min(len(f['ops']) for f in frames)}–{max(len(f['ops']) for f in frames)} commands/frame")
