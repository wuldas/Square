"""Control-only six-column summary. Producer cadence is never inverse caller wall time."""
from collections import defaultdict
import csv
import json
import math
from pathlib import Path
import statistics

FIELDS = ('CPU', 'FPS', 'avgPrivate', 'avgWS', 'PublishMiB')
def load(p): return json.loads(Path(p).read_text(encoding='utf-8-sig'))
def finite(x): return isinstance(x, (int, float)) and not isinstance(x, bool) and math.isfinite(x)

def cadence(app, base):
    count = app['measureFrames']
    if base.startswith('square-'):
        times = app.get('producerFrameTimestampsMs', [])
        rows = app.get('frameCpu', [])
        if len(rows) != count or any(r.get('sequence', r.get('seq')) != i for i, r in enumerate(rows)): return None
    else:
        key, field, sequence = {
            'electron': ('producerFrameTimes', 'rafTimestampMs', 'seq'),
            'flutter': ('producerFrames', 'paintStartSinceEntryMs', 'seq'),
            'avalonia': ('producerFrameTimes', 'renderStartMs', 'sequence')}[base]
        rows = [r for r in app.get(key, []) if r.get('phase') == 2]
        if any(r.get(sequence) != i for i, r in enumerate(rows)): return None
        times = [r.get(field) for r in rows]
    if len(times) != count or count < 2 or not all(finite(t) for t in times): return None
    if any(b <= a for a, b in zip(times, times[1:])): return None
    return (count - 1) * 1000 / (times[-1] - times[0])

def memory(obs):
    rows = obs.get('processTree', {}).get('samples', [])
    if len(rows) < 2: return None, None, 0
    duration = rows[-1]['atMs'] - rows[0]['atMs']
    covered = 0; private = 0; working = 0
    for a, b in zip(rows, rows[1:]):
        dt = b['atMs'] - a['atMs']
        if dt <= 0: return None, None, 0
        if a['memoryComplete'] and b['memoryComplete']:
            covered += dt
            private += dt * (a['privateBytes'] + b['privateBytes']) / 2
            working += dt * (a['workingSetBytes'] + b['workingSetBytes']) / 2
    coverage = covered / duration if duration > 0 else 0
    if coverage < 1 - 1e-9: return None, None, coverage
    return private / duration / 1048576, working / duration / 1048576, coverage

def package_bytes(command, base):
    root = Path(command['fileName']).parent
    if base.startswith('square-') or base == 'avalonia':
        configs = [load(p) for p in root.glob('*.runtimeconfig.json')]
        if not any(c.get('runtimeOptions', {}).get('includedFrameworks') for c in configs):
            return None  # An FDD bin directory is not the full self-contained distribution.
    app = Path(command['args'][-1]) if base == 'electron' else None
    profile = [app / name for name in ('.tmp', '.user-data')] if app else []
    def delivered(p): return p.is_file() and not any(p.is_relative_to(x) for x in profile)
    paths = {p for p in root.rglob('*') if delivered(p)}
    if base == 'electron':
        app = Path(command['args'][-1])
        paths.update(p for p in app.rglob('*') if delivered(p))
    return sum(p.stat().st_size for p in paths if p.suffix.lower() not in {'.pdb', '.dbg'})

def report(output, commands):
    output = Path(output)
    # Snapshot artifact bytes before any subsequent run can generate a browser profile.
    environment = load(output / 'environment.json')
    sizes = environment.get('publishedBytesBeforeRuns', {})
    samples = []
    for record in load(output / 'manifest.json'):
        if record['observationMode'] != 'control': continue
        directory = output / record['directory']; target = record['target']
        base = target.removeprefix('remote-').removeprefix('local-')
        metric = {k: None for k in FIELDS}
        if record['valid']:
            app = load(directory / 'app.json'); obs = load(directory / 'observer.json')
            elapsed = obs.get('elapsedMs'); cpu = obs.get('processTree', {}).get('cpuLowerBoundMs')
            private, working, coverage = memory(obs)
            metric.update(CPU=cpu / elapsed * 100 if finite(cpu) and finite(elapsed) and elapsed > 0 else None,
                          FPS=cadence(app, base), avgPrivate=private, avgWS=working)
        size = sizes.get(target)
        metric['PublishMiB'] = size / 1048576 if size is not None else None
        samples.append({'target': target, 'traceId': record['traceId'], 'round': record['round'],
                        'valid': record['valid'], 'directory': str(directory), **metric})
    (output / 'per-run.json').write_text(json.dumps(samples, indent=2, allow_nan=False) + '\n', encoding='utf-8')
    grouped = defaultdict(list)
    for row in samples: grouped[row['traceId'], row['target']].append(row)
    columns = ['target', *FIELDS, 'traceId', 'quality'] + [f'{k}_{s}' for k in FIELDS for s in ('n', 'min', 'max')]
    result = []
    for (case, target), runs in grouped.items():
        row = {'target': target, 'traceId': case, 'quality': 'Current control timing only; fresh semantic/capture qualification required; native AA/stroke/gradient remain separate'}
        for key in FIELDS:
            values = [r[key] for r in runs if r[key] is not None]
            row.update({key: statistics.median(values) if values else None, key+'_n': len(values),
                        key+'_min': min(values) if values else None, key+'_max': max(values) if values else None})
        result.append(row)
    with (output / 'summary.csv').open('w', encoding='utf-8-sig', newline='') as f:
        writer = csv.DictWriter(f, columns); writer.writeheader(); writer.writerows(result)
    (output / 'summary.json').write_text(json.dumps(result, indent=2, allow_nan=False) + '\n', encoding='utf-8')
    notes = ['CPU=whole discovered-tree monitored-lifetime lower bound; 100%=one logical core.',
             'FPS=120measurement producer events/119intervals, phase2 only; not scanout or inverse caller wall.',
             'Private/WS=actual-dt adjacent complete-sample trapezoid means, no gap filling/extrapolation; incomplete=N/A. WS can double-count shared pages, not VRAM.',
             'PublishMiB=actual full distribution snapshot before runs; only pdb/dbg excluded, XML retained. FDD bin without runtime=N/A. Same artifact repeated per run, not independent publishes.',
             'One composite load, N3 default. No total score; native AA/stroke/gradient and clip/group/image semantic limits are retained.']
    lines = ['# Canvas comprehensive control summary', '', *['- '+n for n in notes], '', '|target|CPU|FPS|avgPrivate MiB|avgWS MiB|PublishMiB|', '|---|---|---|---|---|---|']
    for row in result:
        values = ['N/A (N0)' if row[k] is None else f'{row[k]:.2f} [{row[k+"_min"]:.2f}, {row[k+"_max"]:.2f}] N={row[k+"_n"]}' for k in FIELDS]
        lines.append('|'+row['target']+'|'+'|'.join(values)+'|')
    (output / 'summary.md').write_text('\n'.join(lines)+'\n', encoding='utf-8')
    return result
