"""Validate paired optimization runs and write report-vN with no external packages."""
import argparse
import csv
import html
import json
import math
import re
import statistics
from collections import defaultdict
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('directory', type=Path)
args = parser.parse_args()
root = args.directory.resolve()
env = json.loads((root / 'environment.json').read_text(encoding='utf-8-sig'))
manifest = json.loads((root / 'manifest.json').read_text(encoding='utf-8-sig'))
version = int(env['optimizationVersion'])
suffix = f'-v{version}'
report_path = root / f'report{suffix}.md'
if report_path.exists():
    raise SystemExit(f'Report version already exists; preserve it: {report_path}')
test_results = []
for suite in ('Square.Backends.Tests', 'Square.Backends.Direct2D.Tests', 'Square.Backends.Conformance.Tests'):
    path = root.parent / f'{suite}{suffix}.log'
    match = re.search(r'RESULT passed=(\d+) failed=(\d+) skipped=(\d+) errors=(\d+)', path.read_text(encoding='utf-8-sig'))
    assert match, f'Missing test result: {path}'
    passed, failed, skipped, errors = map(int, match.groups())
    assert failed == errors == 0, f'Test failure: {path}'
    test_results.append({'suite': suite, 'passed': passed, 'skipped': skipped, 'file': path.name})
pixel_path = root.parent / f'visual{suffix}' / 'pixel-comparison.json'
pixels = json.loads(pixel_path.read_text(encoding='utf-8-sig'))
assert pixels['allExact'] and all(row['exactMatch'] and row['changedPixels'] == 0 for row in pixels['comparisons'])
integrity_path = root.parent / f'benchmark-integrity{suffix}.json'
integrity = json.loads(integrity_path.read_text(encoding='utf-8-sig'))
assert all(integrity[key] for key in ('benchmarkSourceUnchanged', 'sceneUnchanged', 'iconUnchanged'))
def hotspot(filename, method):
    path = root.parent / f'diagnostics{suffix}' / filename
    data = json.loads(path.read_text(encoding='utf-8-sig'))
    return next((row['percent'] for row in data['inclusive'] if method in row['name']), 0.0)

hotspots = {
    'pathBefore': hotspot('baseline-direct2d.speedscope-hotspots.json', '.Direct2DRenderContext.CreatePath('),
    'pathAfter': hotspot('after-direct2d.speedscope-hotspots.json', '.Direct2DRenderContext.CreateUncachedPath('),
    'brushBefore': hotspot('baseline-direct2d.speedscope-hotspots.json', '.Direct2DRenderContext.CreateBrush('),
    'brushAfter': hotspot('after-direct2d.speedscope-hotspots.json', '.Direct2DRenderContext.CreateBrush('),
    'textAfter': hotspot('after-direct2d.speedscope-hotspots.json', '.Direct2DRenderContext.DrawText('),
    'softwareGradientBefore': hotspot('baseline-software.speedscope-hotspots.json', '.RenderContext.BlendBrush('),
    'softwareGradientAfter': hotspot('after-software.speedscope-hotspots.json', '.RenderContext.BlendBrush('),
    'softwareSamplerBefore': hotspot('baseline-software.speedscope-hotspots.json', '.RenderContext.SampleBrush('),
    'softwareSamplerAfter': hotspot('after-software.speedscope-hotspots.json', '.RenderContext.SampleBrush('),
    'softwareLineAfter': hotspot('after-software.speedscope-hotspots.json', '.RenderContext.DrawLine('),
}
verification = {'hotspots': hotspots, 'tests': test_results, 'totalPassed': sum(row['passed'] for row in test_results),
    'pixelPairs': len(pixels['comparisons']), 'allPixelsExact': pixels['allExact'], 'inputIntegrity': integrity}
(root / f'verification{suffix}.json').write_text(json.dumps(verification, indent=2), encoding='utf-8')
names = {'before-software': 'Software before', 'after-software': 'Software after',
    'before-direct2d': 'Direct2D before', 'after-direct2d': 'Direct2D after', 'avalonia': 'Avalonia'}
groups = defaultdict(list)
for entry in manifest:
    data = json.loads((root / entry['result']).read_text(encoding='utf-8-sig'))
    assert entry['valid'] and entry['exitCode'] == 0 and not entry['timedOut'], entry
    assert data['clientWidth'] == 800 and data['clientHeight'] == 600 and data['devicePixelRatio'] == 1, entry
    assert data['frames'] > 0 and data['load'] == entry['load'], entry
    assert abs(data['seconds'] - env['seconds']) < .0001, entry
    assert abs(data['fps'] - data['frames'] / data['seconds']) < .000001, entry
    assert len(data['drawTimesMs']) == data['frames'] and len(data['frameIntervalsMs']) == data['frames'] - 1, entry
    assert all(math.isfinite(v) and v >= 0 for key in ('frameIntervalsMs', 'drawTimesMs') for v in data[key]), entry
    groups[entry['target'], entry['load']].append((entry, data))
assert len(manifest) == len(names) * len(env['loads']) * env['repetitions'], 'Incomplete run'

def quantile(values, q):
    values = sorted(values)
    if not values:
        return None
    position = (len(values) - 1) * q
    index = int(position)
    return values[index] + (values[min(index + 1, len(values) - 1)] - values[index]) * (position - index)

rows = []
loads = sorted(env['loads'])
for target, name in names.items():
    for load in loads:
        runs = groups[target, load]
        assert len(runs) == env['repetitions'] and len({entry['repeat'] for entry, data in runs}) == env['repetitions']
        intervals = [value for entry, data in runs for value in data['frameIntervalsMs']]
        fps = [data['fps'] for entry, data in runs]
        rows.append({'target': target, 'name': name, 'load': load, 'fpsMedian': statistics.median(fps),
            'fpsMin': min(fps), 'fpsMax': max(fps), 'intervalP50Ms': quantile(intervals, .5),
            'intervalP95Ms': quantile(intervals, .95), 'intervalP99Ms': quantile(intervals, .99),
            'drawMeanMsMedian': statistics.median(statistics.mean(data['drawTimesMs']) for entry, data in runs),
            'metric': runs[0][1]['metric'], 'sources': [entry['result'] for entry, data in runs]})
lookup = {(row['target'], row['load']): row for row in rows}
(root / f'summary{suffix}.json').write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding='utf-8')
with (root / f'summary{suffix}.csv').open('w', encoding='utf-8-sig', newline='') as file:
    writer = csv.DictWriter(file, fieldnames=list(rows[0]))
    writer.writeheader()
    writer.writerows({key: json.dumps(value) if isinstance(value, list) else value for key, value in row.items()} for row in rows)
labels = {'before-software': 'Square Software 优化前', 'after-software': 'Square Software 优化后',
    'before-direct2d': 'Square Direct2D 优化前', 'after-direct2d': 'Square Direct2D 优化后', 'avalonia': 'Avalonia'}
table = '| 版本 | ' + ' | '.join(f'{load}×' for load in loads) + ' |\n|---|' + '---:|' * len(loads) + '\n'
for target in names:
    table += '| ' + labels[target] + ' | ' + ' | '.join(f"{lookup[target,load]['fpsMedian']:.2f}" for load in loads) + ' |\n'
colors = {'before-software': '#d95d4e', 'after-software': '#d95d4e', 'before-direct2d': '#237eb4', 'after-direct2d': '#237eb4', 'avalonia': '#b7831c'}
width, height, left, top, plotw, ploth = 1000, 520, 75, 40, 660, 390
ceiling = max(65, max(row['fpsMax'] for row in rows) * 1.05)
def x(load):
    return left + math.log2(load / loads[0]) / math.log2(loads[-1] / loads[0]) * plotw if len(loads) > 1 else left

def y(fps):
    return top + (1 - fps / ceiling) * ploth
svg = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}"><rect width="100%" height="100%" fill="white"/><g font-family="Segoe UI,Arial,sans-serif" font-size="14" fill="#334155">']
for tick in range(0, math.ceil(ceiling), 10):
    svg.append(f'<path d="M{left},{y(tick):.1f}h{plotw}" stroke="#e2e8f0"/><text x="{left-12}" y="{y(tick)+5:.1f}" text-anchor="end">{tick}</text>')
for load in loads:
    svg.append(f'<text x="{x(load):.1f}" y="{top+ploth+28}" text-anchor="middle">{load}x</text>')
for index, target in enumerate(names):
    points = [lookup[target, load] for load in loads]
    coords = ' '.join(f"{x(row['load']):.1f},{y(row['fpsMedian']):.1f}" for row in points)
    dash = ' stroke-dasharray="7 5"' if target.startswith('before') else ''
    svg.append(f'<polyline points="{coords}" stroke="{colors[target]}" fill="none" stroke-width="2.5"{dash}/>')
    for row in points:
        svg.append(f'<circle cx="{x(row["load"]):.1f}" cy="{y(row["fpsMedian"]):.1f}" r="4" fill="{colors[target]}"><title>{names[target]} {row["load"]}x: {row["fpsMedian"]:.2f} FPS</title></circle>')
    svg.append(f'<path d="M770,{top+index*32}h23" stroke="{colors[target]}" stroke-width="3"{dash}/><text x="805" y="{top+index*32+5}">{names[target]}</text>')
svg += [f'<text x="{left+plotw/2}" y="490" text-anchor="middle">Scene repetitions per frame (log2 scale)</text>', '<text x="22" y="230" transform="rotate(-90 22 230)" text-anchor="middle">Observed FPS, median of runs</text>', '</g></svg>']
(root / f'fps{suffix}.svg').write_text('\n'.join(svg), encoding='utf-8')
high = loads[-1]
before = lookup['before-direct2d', high]
after = lookup['after-direct2d', high]
software_before, software_after = lookup['before-software', high], lookup['after-software', high]
ava = lookup['avalonia', high]
conclusion = f"{high}× 负载下，Direct2D 从 {before['fpsMedian']:.2f} 提升至 {after['fpsMedian']:.2f} FPS（{after['fpsMedian']/before['fpsMedian']:.2f} 倍），本轮 Avalonia 为 {ava['fpsMedian']:.2f} FPS。Software 从 {software_before['fpsMedian']:.2f} 提升至 {software_after['fpsMedian']:.2f} FPS（{software_after['fpsMedian']/software_before['fpsMedian']:.2f} 倍）。"
method = f"本轮 {len(manifest)} 次同机串行测量，{env['repetitions']} 轮、负载 {loads}，每次预热 {env['warmup']} 秒、采样 {env['seconds']} 秒；800×600、DPR=1、显示刷新率 {env['gpu']['CurrentRefreshRate']} Hz。优化前冻结的二进制与优化后版本、Avalonia轮换运行；共同场景和基准绘制/计时程序保持不变。"
limits = 'Square 计数为固定窗口内完成的 Present，Avalonia 为 Render 调用；它们不是物理 scanout 或统一 GPU 完成计数。低负载接近60时受默认帧调度限制。CPU draw耗时仅可在相同后端优化前后比较；Avalonia记录边界不含compositor/GPU。画质、透明混合、裁剪和绘制次数保持原语义；没有缓存整帧，也没有修改Avalonia。Square与Avalonia各自原有的抗锯齿、文字栅格化和图像插值仍有差异；像素相等验证针对同一后端的优化前后。线程栈采样比例包含native/wait边界，不等同硬件CPU周期。'
md = f'''# Canvas 优化报告 v{version}

{conclusion}

## 实测结果

{table}

![优化前后帧率](fps{suffix}.svg)

## 方法与环境

{method}

- 开始时间：{env['started']}。
- CPU：{env['cpu']['Name']}；GPU：{env['gpu']['Name']}，驱动 {env['gpu']['DriverVersion']}。
- 系统：{env['os']['Caption']} {env['os']['Version']}。
- 原始输入与实现、二进制SHA-256见 `environment.json`，执行顺序及原始JSON索引见 `manifest.json`。

## 热点与改动

- 线程栈采样中，Direct2D原生路径创建占比 {hotspots['pathBefore']:.1f}% → {hotspots['pathAfter']:.1f}%，画刷创建 {hotspots['brushBefore']:.1f}% → {hotspots['brushAfter']:.1f}%；Software渐变填充链路 {hotspots['softwareGradientBefore']:.1f}% → {hotspots['softwareGradientAfter']:.1f}%，渐变逐点取样 {hotspots['softwareSamplerBefore']:.1f}% → {hotspots['softwareSamplerAfter']:.1f}%。这些是各次诊断中Draw内部含子调用的采样线程栈比例，分母不同，不能相加或等同硬件CPU周期；原始trace和归因JSON保存在上级 `diagnostics-v{version}`。
- 剩余热点：Direct2D的fallback文字绘制约 {hotspots['textAfter']:.1f}%，Software线段光栅化约 {hotspots['softwareLineAfter']:.1f}%。本版尚未缓存文字布局或降低线段抗锯齿采样；Software结果应独立阅读，不能将硬件后端达标推广为CPU后端也达到Avalonia。
- Direct2D：原生路径和渐变画刷复用，entry及command/stop双预算约束；路径追加、画刷/stop修改会失效；活跃clip持有独立引用；仅复用已Pop的layer，resize/device-loss/dispose清理相应资源。
- Software：路径clip按行计算交点，避免逐像素扫描全部边；垂直线性渐变按行采样；受限的单path轴向clip走扫描行span。复杂/嵌套/非有限条件保留通用路径，透明目标保持原混合舍入。
- 成功的Direct2D Flush/Present直接返回，避免无必要触发DirectN.Constants的巨大静态初始化；错误及target recreation分支保留。

## 验证与限制

{limits}

本轮三个测试项目共{verification['totalPassed']}项通过（{', '.join(row['suite'] + ': ' + str(row['passed']) for row in test_results)}）；显式启用实际Direct2D硬件窗口测试。`dotnet test` 的testhost在此环境无法监视父进程，因此使用已有进程内xUnit运行器执行同一测试程序集，日志保存在上级目录的 `Square.Backends.Tests-v{version}.log`、`Square.Backends.Direct2D.Tests-v{version}.log`、`Square.Backends.Conformance.Tests-v{version}.log`。

三个固定动画相位×两档负载×两个后端，共{verification['pixelPairs']}组800×600整图对照，全部RGB差异像素为0；证据见 [像素比较](../visual-v{version}/pixel-comparison.json)。固定画面的运行不进入性能统计。[基准完整性](../benchmark-integrity-v{version}.json) 已确认基准源码、scene及icon SHA-256与初次五方测试完全一致。

完整原始帧间隔、绘制耗时、各轮波动范围保存在原始JSON及 `summary{suffix}.csv` / `summary{suffix}.json`。

## 复现

先保存本轮优化前可运行目录，再完成Release构建和回归测试，使用 `Compare-Optimization.ps1` 运行冻结旧版本/当前新版本/Avalonia，最后执行 `python samples/Square.Sample.CanvasBenchmark/report-optimization.py <结果目录>`。runner的 `-Version 0` 默认从已有report-vN.md计算下一版；本轮固定v{version}。报告和汇总/图表均带 `-v{version}` 后缀，已存在报告不覆盖。
'''
report_path.write_text(md, encoding='utf-8')
page = f'''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>Canvas 优化报告 v{version}</title><style>body{{font:16px/1.7 system-ui;color:#1e293b;max-width:1150px;margin:30px auto;padding:0 20px}}svg{{width:100%}}table{{width:100%;border-collapse:collapse}}th,td{{text-align:right;border-bottom:1px solid #ddd;padding:8px}}th:first-child,td:first-child{{text-align:left}}select{{font:inherit;padding:6px}}.note{{background:#f1f5f9;padding:16px}}a{{color:#1672ac}}</style><h1>Canvas 优化报告 v{version}</h1><p>{html.escape(conclusion)}</p><p>{html.escape(method)}</p>'''
page += '\n'.join(svg)
page += f'<p class="note">{html.escape(limits)}</p><label>负载 <select id="load">' + ''.join(f'<option{" selected" if load == high else ""}>{load}</option>' for load in loads) + '</select> ×</label><table><thead><tr><th>版本</th><th>FPS中位数</th><th>FPS范围</th><th>间隔P95 ms</th><th>CPU draw均值 ms</th></tr></thead><tbody id="rows"></tbody></table>'
page += f'<p><a href="report{suffix}.md">完整方法与改动</a> · <a href="summary{suffix}.csv">CSV汇总</a> · <a href="manifest.json">原始样本索引</a></p><script>const rows=' + json.dumps(rows).replace('<', '\\u003c') + ';const labels=' + json.dumps(labels, ensure_ascii=False) + ';'
page += '''function render(){document.querySelector('#rows').innerHTML=rows.filter(r=>r.load==document.querySelector('#load').value).map(r=>`<tr><td>${labels[r.target]}</td><td>${r.fpsMedian.toFixed(2)}</td><td>${r.fpsMin.toFixed(2)}–${r.fpsMax.toFixed(2)}</td><td>${r.intervalP95Ms?.toFixed(2)??'n/a'}</td><td>${r.drawMeanMsMedian.toFixed(2)}</td></tr>`).join('')}document.querySelector('#load').onchange=render;render();</script></html>'''
(root / f'report{suffix}.html').write_text(page, encoding='utf-8')
print(table)
print(conclusion)
print('Validated samples:', len(manifest), '; report:', report_path)
