"""Export the verified v2 title-bar and polyline-correctness record."""
import json
import re
from pathlib import Path
from PIL import Image, ImageFont, ImageDraw

root = Path(__file__).resolve().parents[2]
base = root/'artifacts/canvas-optimization'
output = base/'run-v2'
output.mkdir(exist_ok=True)
report_path = output/'report-v2.md'
if report_path.exists():
    raise SystemExit('Preserve the existing v2 report; use a new version for new work.')
visual = base/'visual-v2'
pixels = json.loads((visual/'pixel-comparison.json').read_text(encoding='utf-8'))
titles = json.loads((base/'ui-v2/window-titles-v2.json').read_text(encoding='utf-8'))
assert pixels['allExpected'] and pixels['fixChangesVisible']
assert len(titles) == 5 and all(item['hasCaption'] and item['client'] == [800,600] for item in titles)
assert all(row['changedPixels'] == 0 for row in pixels['comparisons'] if row['backend'] == 'direct2d')
assert all(row['outsideExpectedRegions'] == 0 for row in pixels['comparisons'])
font = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 20)
comparison = Image.new('RGB', (1600, 464), '#101827')
for index, label in enumerate(('before','after')):
    image = Image.open(visual/f'{label}-software-phase0-load1.png').convert('RGB')
    curve = image.crop((0,230,800,330)).resize((1600,200), Image.Resampling.NEAREST)
    comparison.paste(curve, (0,index*232+32))
    ImageDraw.Draw(comparison).text((12,index*232+3), f'Software {"v1 (before)" if label=="before" else "v2 (after)"} - 2x pixel zoom', font=font, fill='#e2e8f0')
comparison.save(output/'curve-comparison-v2.png')
headers = Image.new('RGB', (820, 5*48), 'white')
for index, item in enumerate(titles):
    with Image.open(base/'ui-v2'/item['screenshot']) as window:
        headers.paste(window.convert('RGB').crop((0,0,min(window.width,820),40)), (0,index*48))
headers.save(output/'window-titles-v2.png')
test_results = []
for suite in ('Square.Backends.Tests','Square.Backends.Direct2D.Tests','Square.Backends.Conformance.Tests'):
    text = (base/f'{suite}-v2.log').read_text(encoding='utf-8-sig')
    match = re.search(r'RESULT passed=(\d+) failed=(\d+) skipped=(\d+) errors=(\d+)', text)
    assert match
    passed, failed, skipped, errors = map(int, match.groups())
    assert failed == errors == 0
    test_results.append({'suite':suite, 'passed':passed, 'skipped':skipped})
short_runs = []
for phase in (0,17,37):
    values = {label: json.loads((visual/f'{label}-software-phase{phase}-load32.json').read_text(encoding='utf-8'))['fps'] for label in ('before','after')}
    short_runs.append({'phase':phase, **values})
verification = {'kind':'title-bar and polyline correctness fix; pixels intentionally change',
    'titles':titles, 'pixelComparison':pixels, 'tests':test_results, 'shortFixedPhaseRuns':short_runs}
(output/'verification-v2.json').write_text(json.dumps(verification,ensure_ascii=False,indent=2),encoding='utf-8')
title_lines = '\n'.join('- `' + item['title'] + '`' for item in titles)
short_lines = '\n'.join(f"| {row['phase']} | {row['before']:.0f} | {row['after']:.0f} |" for row in short_runs)
md = f'''# Canvas 标题栏与连续描边修复报告 v2

## 结果

五方测试窗口均显示系统标题栏，标明框架、Square后端、负载及可选标签。实际Win32句柄/样式与客户区检查通过，五方客户区均为800×600；标题栏没有占用绘图区。采集脚本自动传入目标名，优化对照可显示before/after标识。

{title_lines}

![五方实际标题栏](window-titles-v2.png)

## 曲线亮点原因与修复

问题定位于Square Software。测试将Bezier曲线预采样为32段；原DrawPolyline逐段DrawLine并执行source-over，半透明线段接头会重复混合，同色抗锯齿边缘也会重复加深，形成连续亮点。

现在先把一次路径描边中各段的4×4采样bit求并集，再按总覆盖数对每像素混合一次。没有降低颜色opacity，没有用最大覆盖数代替采样并集，也没有减少抗锯齿采样。单段保留既有快路；临时掩码分块，逻辑数据最多128KiB，使用ArrayPool并在finally归还。另修正描边中MoveTo断开及Close闭合当前子路径，精确共线同向点可合并，避免细分触发旧短线阈值。

上图为v1，下图为v2；均来自固定相位0、1×负载的实际800×600客户区截图，仅作2倍最近邻像素放大：

![接头修复前后](curve-comparison-v2.png)

## 验证

- Release构建通过。三个相关测试项目共{sum(item['passed'] for item in test_results)}项通过，其中新增连续描边测试26项；已有测试无需修改。Direct2D硬件窗口用例显式启用。
- 12组整图对照：三个相位×两档负载×两个后端。Software变化全部限定在弧线/曲线面板；其余区域差异为0。Direct2D六组客户区图像全部逐像素相等。
- 额外核对带标题栏的Direct2D客户区截图与v1原图，六组完全相同，标题栏没有导致客户区偏移。
- 标题检查覆盖中文label，Electron阻止HTML标题覆盖原生窗口标题；Flutter在native runner消费label，其余测试参数继续传Dart。
- 客户区BMP保持800×600；Square另存.window.bmp作为完整窗口证据。图像验证期间临时置前自有测试窗口，避免遮挡污染截图，窗口退出后无残留置顶。

[像素明细](../visual-v2/pixel-comparison.json) · [标题明细](../ui-v2/window-titles-v2.json) · [验证汇总](verification-v2.json)

## 简短异常检查

以下为固定相位32×负载、每次预热0.5秒/测量1秒的Software短跑数据，仅用于检查明显回退；不替代动态场景的正式三轮性能比较。

| 相位 | v1 FPS | v2 FPS |
|---|---:|---:|
{short_lines}

本轮是标题栏和描边正确性修复，预期会改变曲线边缘像素，不能包装为保持像素不变的纯性能优化。v1报告及原始结果保留。本轮记录使用-v2后缀，下一轮递增v3。

## 复现

```powershell
python samples/Square.Sample.CanvasBenchmark/verify-window-titles.py
python samples/Square.Sample.CanvasBenchmark/verify-optimization-images.py --baseline artifacts/canvas-optimization/baseline-v2/CanvasBench.Square.exe --output artifacts/canvas-optimization/visual-v2 --stroke-fix
```

v2对照使用冻结的v1渲染器DLL，并给修复前后相同的测试壳添加标题栏、客户区校准和截图裁剪，避免将边框差异计入画面比较。场景JSON没有改动。
'''
report_path.write_text(md,encoding='utf-8')
print('Saved', report_path)
print('Tests:',sum(item['passed'] for item in test_results), '; verified window targets:',len(titles))
