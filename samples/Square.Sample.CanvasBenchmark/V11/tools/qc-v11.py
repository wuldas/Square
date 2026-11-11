#!/usr/bin/env python3
"""Independent, read-only v11 QC; run only after collection and reporting finish."""
import argparse, hashlib, json, math, statistics, sys
from collections import Counter, defaultdict
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote, urlsplit
import xml.etree.ElementTree as ET

V11 = Path(__file__).resolve().parents[1]
TARGETS = ('square-software', 'square-direct2d', 'square-vulkan', 'electron', 'flutter', 'avalonia')
FRAMEWORKS = dict(zip(TARGETS, ('Square Software', 'Square Direct2D', 'Square Vulkan', 'Electron', 'Flutter', 'Avalonia')))
RAW = ('launch.json', 'app.json', 'observer.json', 'stdout.log', 'stderr.log')
FIELDS = ('callerFrameTotalMs', 'frameCpuMeanMs', 'frameCpuMedianMs', 'frameCpuP95Ms', 'frameCpuP99Ms')

def load(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))

def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

def durations(values):
    if not values or any(type(x) not in (int, float) or not math.isfinite(x) or x < 0 for x in values):
        raise ValueError('frameCpu.totalMs must be nonempty, finite, nonnegative numbers')
    ordered = sorted(values)
    def percentile(p):
        lo, hi = math.floor(p * (len(ordered) - 1)), math.ceil(p * (len(ordered) - 1))
        return ordered[lo] if lo == hi else math.fsum((ordered[lo] * (hi - p * (len(ordered) - 1)), ordered[hi] * (p * (len(ordered) - 1) - lo)))
    return dict(zip(FIELDS, (math.fsum(values), statistics.fmean(values), statistics.median(values), percentile(.95), percentile(.99))))

def interval(values):
    return dict(zip(('n', 'median', 'min', 'max'), (len(values), statistics.median(values), min(values), max(values)))) if values else dict(n=0, median=None, min=None, max=None)

def same(a, b):
    return type(a) in (int, float) and type(b) in (int, float) and math.isfinite(a) and math.isfinite(b) and math.isclose(a, b, rel_tol=1e-10, abs_tol=1e-8) if a is not None and b is not None else a is b

def quality_levels(cases, evidence):
    entries = {}
    for entry in evidence['entries']:
        for case in entry['cases']:
            key = (entry['target'], case)
            if key in entries or entry['target'] not in TARGETS or entry['status'] not in ('pass', 'limited'):
                raise ValueError(f'Duplicate/unsupported quality entry: {key}')
            entries[key] = entry['status']
    if set(entries) != {(target, c['case']) for c in cases for target in TARGETS}:
        raise ValueError('Quality entries do not exactly cover benchmark case-targets')
    return {(c['id'], t): 'semantic-pass' if entries[t, c['case']] == 'pass' and c['qualityComparable'] and 'gradient' not in c['expectedMeasuredOpcodeTotals'] else 'limited' for c in cases for t in TARGETS}

class Links(HTMLParser):
    def __init__(self, root, check):
        super().__init__(); self.root, self.check, self.count = root, check, 0
    def handle_starttag(self, tag, attrs):
        for name, value in attrs:
            if name in ('href', 'src') and value:
                url = urlsplit(value)
                if not url.scheme and not url.netloc and url.path:
                    self.count += 1; self.check((self.root / unquote(url.path)).is_file(), f'HTML missing {name}: {value}')

def audit(root, quality_path, result):
    errors = result['errors']
    def check(ok, message):
        if not ok: errors.append(message)
    env, plan, manifest, completion, summary, quality = [load(p) for p in (root / 'environment.json', root / 'plan.json', root / 'manifest.json', root / 'completion.json', root / 'summary-v11.json', quality_path)]
    traces = load(V11 / 'Traces/manifest.json'); cases = {c['id']: c for c in env['cases']}
    check(env['protocolVersion'] == summary['protocolVersion'] == quality['protocolVersion'] == traces['protocolVersion'] == 11 and traces['complete'] is True, 'Protocol/trace completion')
    check(env['cases'] == traces['benchmarkTraces'] and len(cases) == 29 and tuple(env['targets']) == TARGETS and env['pairedControls'] is True, 'Frozen case/target matrix')
    check(env['plannedRuns'] == summary['plannedRuns'] == completion['plannedRuns'] == 1044 and env['baseRuns'] == 522, 'Planned counts')
    check(completion['finishedRuns'] == completion['validRuns'] == summary['finishedRuns'] == summary['validRuns'] == 1044 and summary['failedRuns'] == summary['missingRuns'] == 0, 'Incomplete/invalid collection')
    check(completion['hashesUnchanged'] is True and not completion['changedInputs'] and not summary['changedFrozenFiles'], 'Frozen hash completion')
    check(summary['primaryCallerTimingMode'] == summary['primaryProcessCpuMode'] == 'control', 'Primary mode must remain control')
    expected = {(c, t, r, m) for c in cases for t in TARGETS for r in (1, 2, 3) for m in ('observed', 'control')}
    check(len(plan) == len(manifest) == len(summary['runs']) == 1044 and {(p['traceId'], p['target'], p['round'], p['observationMode']) for p in plan} == expected, '1044 unique planned workloads')
    check([p['index'] for p in plan] == list(range(1, 1045)) and len({p['directory'] for p in plan}) == 1044, 'Plan indices/directories')
    for mode in ('observed', 'control'):
        check(sum(p['observationMode'] == mode for p in plan) == 522 and all(completion['byObservationMode'][mode][k] == summary['byObservationMode'][mode][k] == 522 for k in ('planned', 'finished', 'valid')), f'{mode} must have 522 valid runs')
    records, reported = {r['directory']: r for r in manifest}, {r['directory']: r for r in summary['runs']}
    check(set(records) == set(reported) == {p['directory'] for p in plan}, 'Manifest/report directories differ')
    check(bool(env['hashes']) and len({h['path'] for h in env['hashes']}) == len(env['hashes']), 'Frozen hash list absent/duplicated')
    for h in env['hashes']:
        check(sha(h['path']) == h['sha256'] and Path(h['path']).stat().st_size == h['bytes'], f'Frozen SHA/size changed: {h["path"]}')
    pairs, computed, grouped = defaultdict(dict), {}, defaultdict(list)
    for p in plan:
        directory = root / p['directory']; label = p['directory']; mode = p['observationMode']; c = cases[p['traceId']]
        check(mode not in pairs[p['pairId']], f'Duplicate pair mode: {label}'); pairs[p['pairId']][mode] = p
        try:
            run, launch, app, obs = [load(directory / name) for name in ('run.json', 'launch.json', 'app.json', 'observer.json')]; rr = reported[label]
            check(run == records[label] and all(run[k] == rr[k] == v for k, v in p.items()), f'Plan/manifest/run/report identity: {label}')
            good = run['valid'] is True and not run['failures'] and rr['valid'] is True and not rr['failures'] and rr['missing'] is False
            check(good and run['observerExitCode'] == obs['childExitCode'] == 0 and not obs['failures'], f'Run/child validity: {label}')
            check(set(run['outputHashes']) == set(RAW) and all(sha(directory / n) == run['outputHashes'][n] for n in RAW), f'Five raw SHA: {label}')
            check(all(d['protocolVersion'] == 11 and d['tag'] == p['tag'] and d['traceSha256'].lower() == c['traceSha256'] for d in (app, obs)), f'App/observer identity: {label}')
            check(app.get('status', 'ok') in (('ok', 'completed') if p['target'] == 'flutter' else ('ok',)) and app['framework'] == FRAMEWORKS[p['target']], f'App status/framework: {label}')
            check((app['clientWidth'], app['clientHeight'], app['devicePixelRatio'], app['warmupFrames'], app['measureFrames']) == (1280, 800, 1, c['warmupFrames'], c['measureFrames']) and app['actualMeasuredOpcodeTotals'] == c['expectedMeasuredOpcodeTotals'] and app['timingEligible'] is True and not app.get('verificationOnly', False) and not app.get('verify', False), f'App dimensions/frames/opcodes/timing: {label}')
            check((app.get('assets') or {}).get('fontSha256', (app.get('font') or {}).get('sha256')) == traces['assetsSha256']['font.ttf'], f'App font SHA: {label}')
            check(obs['status'] == ('ok' if mode == 'observed' else 'control-complete') and obs['expectedMeasuredFrames'] == c['measureFrames'], f'Observer status/frames: {label}')
            window = obs['window']; check(window['ownerPid'] == obs['rootPid'] > 0 and window['topmostWithoutActivation'] is True and (window['clientWidth'], window['clientHeight'], window['devicePixelRatio'], window['dpi']) == (1280, 800, 1, 96), f'Observer window: {label}')
            check(all(d['observeFrames'] is (mode == 'observed') and d['captureBackend'] == env['captureBackend'] for d in (launch, obs['launch'])) and obs['sampling']['enabled'] is (mode == 'observed'), f'Launch/capture mode: {label}')
            argv = launch['args']; command = env['adapters'][p['target']]
            check(launch['fileName'] == command['fileName'] and launch['workingDirectory'] == command['workingDirectory'] and argv[:len(command['args'])] == command['args'] and argv[argv.index('--tag') + 1] == str(p['tag']) and argv[argv.index('--verify') + 1] == '0' and Path(argv[argv.index('--trace') + 1]).resolve() == (V11 / 'Traces' / c['trace']).resolve() and Path(argv[argv.index('--output') + 1]).resolve() == directory / 'app.json', f'Launch arguments: {label}')
            if mode == 'control':
                check(obs['marker'] is None and obs['capture'] is None, f'Control initialized capture/marker: {label}')
            else:
                marker = obs['marker']; seqs = [x['seq'] for x in marker['uniqueMeasuredFrames']]; count = marker['uniqueMeasuredCount']; duration = marker['measurementDurationMs']
                check(len(seqs) == len(set(seqs)) == count and 0 < count <= c['measureFrames'] and set(seqs) <= set(range(c['measureFrames'])) and marker['missingMeasuredSequences'] == sorted(set(range(c['measureFrames'])) - set(seqs)) and duration > 0 and same(duration, marker['firstEndMs'] - marker['firstMeasurementMs']) and same(marker['desktopObservedChangesPerSecond'], count * 1000 / duration), f'Observed marker accounting: {label}')
            frames = app['frameCpu']; check(len(frames) == c['measureFrames'] and [x.get('seq', x.get('sequence')) for x in frames] == list(range(c['measureFrames'])), f'CPU frame sequence/count: {label}')
            values = durations([x['totalMs'] for x in frames])
            check(all(same(value, rr['metrics'][key]) for key, value in values.items()), f'Independent per-run duration metrics: {label}')
            if good: computed[label] = values; grouped[p['traceId'], p['target'], mode].append(values)
        except (OSError, ValueError, KeyError, TypeError, AttributeError, IndexError) as error:
            errors.append(f'{label}: {type(error).__name__}: {error}')
    check(len(pairs) == 522 and len({tuple(p[k] for k in ('traceId', 'target', 'round')) for p in plan}) == 522, '522 unique pairs')
    report_pairs = {p['pairId']: p for p in summary['pairs']}
    check(len(report_pairs) == len(summary['pairs']) == 522 and set(report_pairs) == set(pairs), 'Report pair identities')
    for identity, modes in pairs.items():
        check(set(modes) == {'observed', 'control'} and len({tuple(p[k] for k in ('traceId', 'target', 'round')) for p in modes.values()}) == 1, f'Pair workload mismatch/missing control: {identity}')
        rp = report_pairs[identity]
        check(rp['validPair'] is True and not rp['failures'] and all(rp[m + 'Planned'] is True and rp[m + 'Directory'] == p['directory'] and all(rp[k] == p[k] for k in ('traceId', 'target', 'round')) for m, p in modes.items()), f'Report pair workload: {identity}')
    qmap = quality_levels(list(cases.values()), quality); counts = Counter(qmap.values())
    check(quality['allSourcesValid'] is True and len(qmap) == 174 and counts == {'semantic-pass': 136, 'limited': 38}, 'Quality must be 136 pass/38 limited across 174 unique trace-targets')
    check(summary['qualityEvidence']['sha256'] == sha(quality_path) and Path(summary['qualityEvidence']['path']).resolve() == quality_path, 'Quality evidence path/SHA')
    groups = {(g['traceId'], g['target'], g['observationMode']): g for g in summary['groups']}
    check(len(groups) == len(summary['groups']) == 348 and set(groups) == {(c, t, m) for c in cases for t in TARGETS for m in ('observed', 'control')}, 'Report group matrix')
    result.update(perRun=computed, groups=[], qualityCounts=dict(counts), uniqueQualityCaseTargets=len(qmap), frozenFilesChecked=len(env['hashes']), pairsChecked=len(pairs))
    for key, g in groups.items():
        check(g['case'] == cases[key[0]]['case'] and g['level'] == cases[key[0]]['level'] and g['quality']['status'] == qmap[key[:2]] and g['quality']['fixedQualityQualified'] is (qmap[key[:2]] == 'semantic-pass'), f'Case-level quality mapping: {key}')
        subset = [p['directory'] for p in plan if (p['traceId'], p['target'], p['observationMode']) == key]
        check(g['runDirectories'] == subset and g['plannedN'] == g['validN'] == len(grouped[key]) == 3 and g['missingN'] == g['failedN'] == 0, f'Three independent rounds/no mode substitution: {key}')
        metrics = {f: interval([v[f] for v in grouped[key]]) for f in FIELDS}
        check(all(same(v, g['metrics'][f][s]) for f, stat in metrics.items() for s, v in stat.items()), f'Independent median[min,max] metrics: {key}')
        result['groups'].append(dict(traceId=key[0], target=key[1], observationMode=key[2], metrics=metrics, quality=qmap[key[:2]]))
    for trace_id in cases:
        check(ET.parse(root / 'charts' / f'chart-{trace_id}.svg').getroot().tag == '{http://www.w3.org/2000/svg}svg', f'SVG root: {trace_id}')
    links = Links(root, check); links.feed((root / 'report-v11.html').read_text(encoding='utf-8')); links.close()
    check(links.count > 0, 'HTML has no local references'); result.update(svgChecked=len(cases), localLinksChecked=links.count)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for flag in ('input', 'quality', 'output'): parser.add_argument('--' + flag, type=Path, required=True)
    args = parser.parse_args(); result = dict(protocolVersion=11, status='failed', errors=[])
    try:
        audit(args.input.resolve(), args.quality.resolve(), result)
    except (OSError, ValueError, KeyError, TypeError, AttributeError, IndexError, ET.ParseError) as error:
        result['errors'].append(f'{type(error).__name__}: {error}')
    result['status'] = 'failed' if result['errors'] else 'pass'
    try:
        with args.output.open('x', encoding='utf-8') as stream: json.dump(result, stream, ensure_ascii=False, indent=2, allow_nan=False); stream.write('\n')
    except (OSError, ValueError) as error:
        result['errors'].append(f'QC output (must be a new file): {error}'); result['status'] = 'failed'
    print(json.dumps(dict(status=result['status'], errors=result['errors']), ensure_ascii=False), file=sys.stderr if result['errors'] else sys.stdout)
    return int(bool(result['errors']))

if __name__ == '__main__':
    sys.exit(main())
