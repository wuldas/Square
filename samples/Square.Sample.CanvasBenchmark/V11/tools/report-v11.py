#!/usr/bin/env python3
"""Report v11 raw runs without launching applications or combining unlike completion events."""
import argparse
import collections
import csv
import datetime
import html
import json
import os
from pathlib import Path
import runpy
import statistics
from urllib.parse import quote

HERE = Path(__file__).resolve().parent
COLLECTOR = runpy.run_path(str(HERE / 'run-v11.py'), run_name='v11_report_collector')
TARGETS = COLLECTOR['TARGETS']
load, digest, number = (COLLECTOR[key] for key in ('load', 'digest', 'number'))
LABELS = dict(zip(TARGETS, ('Square Software', 'Square Direct2D', 'Square Vulkan', 'Electron Canvas2D', 'Flutter Canvas', 'Avalonia')))
ALIASES = {label.lower(): target for target, label in LABELS.items()}
ALIASES.update({name.lower(): target for name, target in zip(('Software', 'Direct2D', 'Vulkan', 'Electron', 'Flutter', 'Avalonia'), TARGETS)})
ALIASES.update({target: target for target in TARGETS})
METRICS = {
    'desktopObservedChangesPerSecond': 'Desktop observed changes/s',
    'qualifiedDesktopChangesPerSecond': 'Observer flags pass subset desktop changes/s',
    'visibleCoveragePercent': 'Observed unique logical sequences %',
    'frameCpuMedianMs': '调用方帧耗时P50（墙钟，含同步等待） ms',
    'frameCpuMeanMs': '调用方帧平均耗时（墙钟，含同步等待） ms',
    'frameCpuP95Ms': '调用方帧耗时P95（墙钟，含同步等待） ms',
    'frameCpuP99Ms': '调用方帧耗时P99（墙钟，含同步等待） ms',
    'callerFrameTotalMs': '固定测量序列调用方总耗时（墙钟，含同步等待） ms',
    'treeMeasurementCpuLowerBoundMs': '实际进程树CPU时间（GetProcessTimes，observed marker phase采样边界） ms',
    'treeWholeRunCpuLowerBoundMs': '实际进程树CPU时间（GetProcessTimes，whole lifetime采样下界） ms',
    'treeWholeRunCpuPercentOneCore': '实际进程树CPU（whole lifetime；100%=one core） %',
    'nativePrepareMedianMs': 'Native prepare帧耗时P50（墙钟） ms',
    'recordSubmitMedianMs': 'Record/submit帧耗时P50（墙钟） ms',
    'presentMedianMs': 'Present API帧耗时P50（墙钟，含等待） ms',
    'assetLoadMs': 'Asset/input load ms', 'staticPrepareMs': 'Static preparation ms',
    'processColdFirstMarkerMs': 'Process.Start to first desktop marker ms',
    'treeMeasurementCpuPercentOneCore': 'Tree measurement CPU % (100%=one core)',
    'peakTreePrivateMiB': 'Whole-run peak sampled tree private MiB',
    'observerCpuPercentOneCore': 'Whole-run observer CPU % (100%=one core)',
    'observerMeasurementCpuMs': 'Observer CPU over sampled measurement bracket ms',
    'measurementGapP95Ms': 'Capture measurement gap P95 ms',
    'measurementCaptureP95Ms': 'Capture measurement cost P95 ms',
    'readbackLatencyP95Ms': 'Observer readback latency P95 ms (raw scope)',
    'nativeProcessingP95Ms': 'Observer native processing P95 ms (raw scope)',
    'accumulatedExtraDesktopFrames': 'Accumulated extra desktop frames (not proven GPU drops)',
}
PAIR_METRICS = ('callerDurationDeltaMs', 'callerDurationRatio', 'treeLifetimeCpuDeltaMs', 'treeLifetimeCpuRatio', 'privateMiBDelta', 'privateMiBRatio')
MODES = ('observed', 'control')


def stats(values):
    values = [float(value) for value in values if number(value)]
    return {'n': len(values), 'median': statistics.median(values) if values else None,
            'min': min(values) if values else None, 'max': max(values) if values else None}


def frame_percentile(values, fraction):
    ordered = sorted(float(value) for value in values if number(value))
    if not ordered: return None
    position = (len(ordered) - 1) * fraction
    lower = int(position)
    return ordered[lower] + (ordered[min(lower + 1, len(ordered) - 1)] - ordered[lower]) * (position - lower)


def target_name(value):
    return ALIASES.get(str(value).lower().replace(' canvas2d', '').replace(' canvas', ''), ALIASES.get(str(value).lower()))


def quality_for(case, target, evidence):
    """Apply fixture probes only to opcodes in this trace; never spread an image failure to fills."""
    reasons, failures, relevant, matched = [], [], 0, False
    if not case.get('qualityComparable', False):
        reasons.append(f"Declared separate native quality profile: {case.get('qualityProfile')}")
    ops = set(case['expectedMeasuredOpcodeTotals'])
    if 'gradient' in ops:
        reasons.append('Native gradient interpolation has its own quality boundary; no fixed-quality equivalence asserted')
    entries = evidence.get('entries', []) if isinstance(evidence, dict) else evidence if isinstance(evidence, list) else []
    for entry in entries:
        if target_name(entry.get('target', entry.get('framework'))) != target:
            continue
        for key, value in (('traceIds', case['id']), ('cases', case['case']), ('profiles', case['qualityProfile'])):
            scope = entry.get(key)
            if scope is not None and value not in (scope if isinstance(scope, list) else [scope]):
                break
        else:
            matched = True
            if 'probes' not in entry:
                state = entry.get('status', 'unverified')
                if state == 'pass':
                    relevant += 1
                elif state in ('failed', 'fail', 'limited', 'unsupported'):
                    relevant += 1
                    failures.extend(entry.get('reasons') or [state])
                else:
                    reasons.extend(entry.get('reasons') or ['Quality evidence has no qualification decision'])
                continue
            for probe in entry.get('probes', []):
                category, name = probe.get('category'), probe.get('name', '')
                applies = category == 'interiorProbes'
                if category == 'imageProbes':
                    applies = 'image' in ops
                elif category == 'groupProbes':
                    applies = 'layer' in ops
                elif category == 'alphaProbes':
                    applies = case['case'] in ('fill-overdraw', 'mixed-dashboard', 'clip-layer')
                elif category == 'interiorProbes':
                    if 'static-poly' in name: applies = 'poly' in ops
                    elif 'new-poly' in name: applies = 'poly-new' in ops
                    elif 'transform' in name: applies = 'transform' in ops
                    elif 'new-clip' in name: applies = 'clip-new' in ops
                    elif 'clip' in name: applies = 'clip' in ops
                    elif 'opaque-rect' in name: applies = 'rect' in ops
                if applies:
                    relevant += 1
                    if probe.get('passed') is not True:
                        failures.append(f"{name}: error={probe.get('maxChannelError')}, allowed={probe.get('allowed')}")
            if ops.intersection(('text', 'text-new')):
                text_regions = entry.get('textRegions', [])
                if not text_regions:
                    reasons.append('Text fixture coverage absent')
                for region in text_regions:
                    relevant += 1
                    if region.get('passed') is not True:
                        failures.append(f"Nonempty/content-position text fixture failed: {region.get('text')}")
    if not matched or not relevant:
        reasons.append('No applicable finalized quality evidence')
    reasons = list(dict.fromkeys(failures + reasons))
    status = 'limited' if failures or not case.get('qualityComparable', False) or 'gradient' in ops else 'semantic-pass' if matched and relevant and not reasons else 'unverified'
    if 'image' in ops and isinstance(evidence, dict):
        for framework, details in evidence.get('frameworks', {}).items():
            if target_name(framework) == target and (details.get('capabilities') or {}).get('imageIdentityAlpha') is False:
                limitation = details.get('limitation')
                if limitation and limitation not in reasons:
                    reasons.append(limitation)
    return {'status': status, 'fixedQualityQualified': status == 'semantic-pass', 'reasons': reasons,
            'note': 'Interior/alpha/group/font semantics only; AA edges and native stroke/gradient/sampling remain separate. No raw-pixel or glyph-raster equality claim.'}


def capture_metadata(obs):
    sampling, capture, launch = obs.get('sampling') or {}, obs.get('capture') or {}, obs.get('launch') or {}
    return {'backend': sampling.get('backend') or capture.get('backend') or launch.get('captureBackend') or 'gdi-legacy',
            'eventDriven': sampling.get('eventDriven', capture.get('eventDriven')),
            'captureInitialized': False if launch.get('observeFrames') is False else True if capture else None,
            'markerTimestampSource': (obs.get('marker') or {}).get('timestampSource', 'legacy capture-completion') if obs.get('marker') is not None else None,
            'clock': obs.get('clock'), 'native': {key: value for key, value in capture.items() if key != 'acquisitions'}}


def run_metrics(app, obs, qualified, mode='observed'):
    marker, sampling = obs.get('marker') or {}, obs.get('sampling') or {}
    tree, observer = obs.get('processTree') or {}, obs.get('observer') or {}
    cpu = [row for row in app.get('frameCpu', []) if isinstance(row, dict)]
    values = {name: None for name in METRICS}
    values.update(
        desktopObservedChangesPerSecond=marker.get('desktopObservedChangesPerSecond'),
        qualifiedDesktopChangesPerSecond=marker.get('desktopObservedChangesPerSecond') if qualified else None,
        visibleCoveragePercent=100 * marker.get('uniqueMeasuredCount', 0) / app['measureFrames'] if number(app.get('measureFrames')) and app['measureFrames'] else None,
        callerFrameTotalMs=sum(row['totalMs'] for row in cpu if number(row.get('totalMs'))) if cpu else None,
        treeWholeRunCpuLowerBoundMs=tree.get('cpuLowerBoundMs'),
        treeWholeRunCpuPercentOneCore=tree['cpuLowerBoundMs'] * 100 / obs['elapsedMs'] if number(tree.get('cpuLowerBoundMs')) and number(obs.get('elapsedMs')) and obs['elapsedMs'] > 0 else None,
        treeMeasurementCpuLowerBoundMs=(tree.get('measurementCpu') or {}).get('cpuLowerBoundMs'),
        assetLoadMs=app.get('assetLoadMs'), staticPrepareMs=app.get('staticPrepareMs'),
        processColdFirstMarkerMs=marker.get('firstMarkerMs'),
        treeMeasurementCpuPercentOneCore=(tree.get('measurementCpu') or {}).get('percentOfOneLogicalCore'),
        peakTreePrivateMiB=tree['peakSampledPrivateBytes'] / 1048576 if number(tree.get('peakSampledPrivateBytes')) else None,
        observerCpuPercentOneCore=observer.get('percentOfOneLogicalCore'),
        observerMeasurementCpuMs=(observer.get('measurement') or {}).get('cpuMs'),
        measurementGapP95Ms=(sampling.get('measurementGapMs') or {}).get('p95'),
        measurementCaptureP95Ms=(sampling.get('measurementCaptureMs') or {}).get('p95'),
        readbackLatencyP95Ms=(sampling.get('readbackLatencyMs') or {}).get('p95'),
        nativeProcessingP95Ms=(sampling.get('nativeProcessingMs') or {}).get('p95'),
        accumulatedExtraDesktopFrames=sampling.get('accumulatedExtraDesktopFrames'),
    )
    for metric, field in (('frameCpuMedianMs', 'totalMs'), ('nativePrepareMedianMs', 'nativePrepareMs'), ('recordSubmitMedianMs', 'recordSubmitMs'), ('presentMedianMs', 'presentMs')):
        values[metric] = stats([row.get(field) for row in cpu])['median']
    durations = [row['totalMs'] for row in cpu if number(row.get('totalMs'))]
    values['frameCpuMeanMs'] = statistics.fmean(durations) if durations else None
    values['frameCpuP95Ms'] = frame_percentile(durations, 0.95)
    values['frameCpuP99Ms'] = frame_percentile(durations, 0.99)
    if mode == 'control':
        for field in ('desktopObservedChangesPerSecond', 'qualifiedDesktopChangesPerSecond', 'visibleCoveragePercent', 'processColdFirstMarkerMs',
                      'treeMeasurementCpuLowerBoundMs', 'treeMeasurementCpuPercentOneCore', 'observerMeasurementCpuMs', 'measurementGapP95Ms',
                      'measurementCaptureP95Ms', 'readbackLatencyP95Ms', 'nativeProcessingP95Ms', 'accumulatedExtraDesktopFrames'):
            values[field] = None
    return values


def matrix_pairs(runs):
    grouped = collections.defaultdict(dict)
    for run in runs:
        identity, mode = run['pairId'], run['observationMode']
        if mode in grouped[identity]:
            raise ValueError(f'Duplicate {mode} run in pair {identity}')
        grouped[identity][mode] = run
    pairs = []
    for identity, modes in grouped.items():
        observed, control = modes.get('observed'), modes.get('control')
        example = observed or control
        valid = bool(observed and control and observed['valid'] and control['valid'])
        failures = [f'{mode}: ' + '; '.join(run['failures']) for mode, run in modes.items() if not run['valid']]
        for mode in MODES:
            if mode not in modes: failures.append(f'{mode} not planned; no substitution from the other mode')
        if valid and any(observed[key] != control[key] for key in ('traceId', 'target', 'round')):
            valid = False; failures.append('Pair workload identity differs')
        metrics = {key: None for key in PAIR_METRICS}
        if valid:
            for source, delta, ratio in (('callerFrameTotalMs', 'callerDurationDeltaMs', 'callerDurationRatio'),
                    ('treeWholeRunCpuLowerBoundMs', 'treeLifetimeCpuDeltaMs', 'treeLifetimeCpuRatio'),
                    ('peakTreePrivateMiB', 'privateMiBDelta', 'privateMiBRatio')):
                on, off = observed['metrics'][source], control['metrics'][source]
                if number(on) and number(off):
                    metrics[delta] = on - off
                    if off > 0: metrics[ratio] = on / off
        pairs.append({'pairId': identity, 'traceId': example['traceId'], 'target': example['target'], 'round': example['round'],
            'observedDirectory': observed['directory'] if observed else None, 'controlDirectory': control['directory'] if control else None,
            'observedPlanned': observed is not None, 'controlPlanned': control is not None, 'validPair': valid, 'failures': failures,
            'pairOrder': [run['observationMode'] for run in sorted(modes.values(), key=lambda row: row['index'])], 'metrics': metrics})
    return pairs


def diagnostics(roots):
    records = []
    for root in roots:
        root = Path(root).resolve()
        paths = [root] if root.is_file() else sorted(path for path in root.rglob('*.json') if path.name == 'observer.json' or path.name.endswith('.observer.json'))
        for path in paths:
            try:
                obs = load(path)
                if obs.get('protocolVersion') != 11 or 'sampling' not in obs:
                    continue
                app_path = path.with_name('app.json') if path.name == 'observer.json' else path.with_name(path.name.removesuffix('.observer.json') + '.json')
                app = load(app_path) if app_path.is_file() else {}
                target = target_name(app.get('framework', path.name.removesuffix('.observer.json')))
                control = obs.get('status') == 'control-complete' and (obs.get('sampling') or {}).get('enabled') is False and (obs.get('launch') or {}).get('observeFrames') is False
                captured = obs.get('status') == 'ok' and (obs.get('sampling') or {}).get('enabled') is True and (obs.get('launch') or {}).get('observeFrames') is True
                records.append({'path': str(path), 'sha256': digest(path), 'target': target, 'traceSha256': obs.get('traceSha256'),
                    'kind': 'no-capture-control' if control else 'captured-diagnostic' if captured else 'failed-diagnostic',
                    'observationMode': 'control' if (obs.get('launch') or {}).get('observeFrames') is False else 'observed',
                    'capture': capture_metadata(obs),
                    'status': obs.get('status'), 'failures': obs.get('failures', []), 'elapsedMs': obs.get('elapsedMs'),
                    'observerCpuMs': (obs.get('observer') or {}).get('cpuMs'),
                    'treeCpuLowerBoundMs': (obs.get('processTree') or {}).get('cpuLowerBoundMs'),
                    'peakTreePrivateBytes': (obs.get('processTree') or {}).get('peakSampledPrivateBytes'),
                    'frameCpuMedianMs': stats([row.get('totalMs') for row in app.get('frameCpu', [])])['median'],
                    'sampling': {key: value for key, value in (obs.get('sampling') or {}).items() if key != 'samples'},
                    'marker': {key: value for key, value in (obs.get('marker') or {}).items() if key not in ('transitionRecords', 'uniqueMeasuredFrames')},
                    'metricBoundary': obs.get('metricBoundary')})
            except (OSError, ValueError, TypeError, AttributeError) as error:
                records.append({'path': str(path), 'kind': 'unreadable-diagnostic', 'error': str(error)})
    pairs = []
    grouped = collections.defaultdict(list)
    for row in records:
        if row.get('target') and row.get('traceSha256'):
            grouped[(row['target'], row['traceSha256'], row['capture']['backend'])].append(row)
    for (target, trace_hash, backend), rows in sorted(grouped.items()):
        captures = [row for row in rows if row['kind'] == 'captured-diagnostic']
        controls = [row for row in rows if row['kind'] == 'no-capture-control']
        if captures and controls:
            pair = {'target': target, 'traceSha256': trace_hash, 'captureBackend': backend, 'captureN': len(captures), 'controlN': len(controls),
                'captureAttemptsN': sum(row['observationMode'] == 'observed' for row in rows),
                'controlAttemptsN': sum(row['observationMode'] == 'control' for row in rows),
                'failedEvidence': [row['path'] for row in rows if row['kind'] == 'failed-diagnostic'], 'differences': {}}
            for field in ('elapsedMs', 'observerCpuMs', 'treeCpuLowerBoundMs', 'peakTreePrivateBytes', 'frameCpuMedianMs'):
                on, off = stats([row.get(field) for row in captures]), stats([row.get(field) for row in controls])
                pair['differences'][field] = {'capture': on, 'control': off, 'medianCaptureMinusControl': on['median'] - off['median'] if on['n'] and off['n'] else None}
            pairs.append(pair)
    return {'records': records, 'pairs': pairs,
            'boundary': 'Controls have no desktop FPS. Pair by framework, exact trace SHA and declared capture backend. Capture-minus-control differences retain launch/window/tree monitoring overhead and are diagnostic perturbation estimates; not pure capture cost or GPU/scanout time.'}


def build_report(root, quality_path=None, diagnostic_roots=()):
    root = Path(root).resolve()
    environment, plan = load(root / 'environment.json'), load(root / 'plan.json')
    plan = [{**row, 'observationMode': row.get('observationMode', 'observed'), 'pairId': row.get('pairId', row['directory'])} for row in plan]
    if environment.get('protocolVersion') != 11:
        raise ValueError('Only protocol v11 inputs can enter this report')
    capture_contract = COLLECTOR['capture_contract'](environment.get('captureBackend', 'desktop-duplication'))
    cases = {case['id']: case for case in environment['cases']}
    completion = load(root / 'completion.json') if (root / 'completion.json').is_file() else {}
    quality = load(quality_path) if quality_path else None
    if isinstance(quality, dict) and quality.get('protocolVersion', 11) != 11:
        raise ValueError('Quality evidence must use protocol v11')
    issues, runs, versions = [], [], {target: [] for target in TARGETS}
    if capture_contract['observationModeLimited']:
        issues.append(capture_contract['observationInterpretation'])
    changed = [row['path'] for row in environment.get('hashes', []) if not Path(row['path']).is_file() or digest(row['path']) != row['sha256']]
    if changed: issues.append(f'{len(changed)} frozen source/input/binary files differ at reporting time')
    if completion.get('hashesUnchanged') is not True: issues.append('Collection has no successful unchanged-hash completion record')
    assets = load(COLLECTOR['V11'] / 'Traces/manifest.json')['assetsSha256']
    for planned in plan:
        directory = root / planned['directory']
        case = cases[planned['traceId']]
        if not (directory / 'run.json').is_file():
            runs.append({**planned, **capture_contract, 'valid': False, 'missing': True, 'observationQualified': False, 'failures': ['Run not collected'], 'metrics': {name: None for name in METRICS}})
            continue
        record = load(directory / 'run.json')
        errors = list(record.get('failures', []))
        app, obs = {}, {}
        try:
            app, obs = load(directory / 'app.json'), load(directory / 'observer.json')
            errors.extend(COLLECTOR['validate_run'](app, obs, case, planned, record.get('observerExitCode'), assets, capture_contract['captureBackend']))
        except (OSError, ValueError, TypeError, AttributeError, KeyError) as error:
            errors.append(f'Report input validation: {error}')
        for name, expected in record.get('outputHashes', {}).items():
            path = directory / name
            if not path.is_file() or digest(path) != expected: errors.append(f'Raw artifact hash changed: {name}')
        if record.get('valid') is not True: errors.append('Collector marked run invalid')
        valid = not errors
        sampling = obs.get('sampling') or {}
        qualified = planned['observationMode'] == 'observed' and valid and sampling.get('badCoverage') is False and sampling.get('observerLimited') is False
        metrics = run_metrics(app, obs, qualified, planned['observationMode']) if valid else {name: None for name in METRICS}
        runs.append({**planned, **capture_contract, 'valid': valid, 'missing': False, 'failures': list(dict.fromkeys(errors)), 'observationQualified': qualified,
            'observerLimited': sampling.get('observerLimited'), 'badCoverage': sampling.get('badCoverage'), 'observationReasons': sampling.get('reasons', []),
            'samplingTelemetry': {key: value for key, value in sampling.items() if key != 'samples'},
            'capture': capture_metadata(obs),
            'metrics': metrics, 'rawDirectory': str(directory), 'unobservedSequences': (obs.get('marker') or {}).get('missingMeasuredSequences'),
            'cpuBoundary': 'Stopwatch wall-clock prepare + record/Flush + Present-return；含同步等待，非GetProcessTimes/GPU completion' if planned['target'].startswith('square-') else (app.get('diagnostics') or {}).get('cpuBoundary'),
            'rawProducerMetric': app.get('metric'),
            'memorySnapshots': app.get('memory'), 'nativeAssets': app.get('nativeAssets'),
            'frameworkDiagnostics': {'fenceBefore': (app.get('diagnostics') or {}).get('fenceBefore'), 'fenceAfter': (app.get('diagnostics') or {}).get('fenceAfter'),
                'flutterFrameTimingEvents': len((app.get('diagnostics') or {}).get('flutterFrameTiming', [])), 'rawApp': str(directory / 'app.json')},
            'processSamplingBoundary': (obs.get('processTree') or {}).get('measurementCpu') if planned['observationMode'] == 'observed' else None,
            'processLifetimeBoundary': {'elapsedMs': obs.get('elapsedMs'), 'cpuLowerBoundMs': (obs.get('processTree') or {}).get('cpuLowerBoundMs'),
                'note': 'Whole monitored app lifetime including startup, warmup, measurement, END hold and exit grace; not an exact marker-phase CPU window'}})
        metadata = {'versions': app.get('versions'), 'hardware': app.get('hardware', app.get('hardwareStatus', {'context': (app.get('versions') or {}).get('context'), 'hardwareRequired': app.get('hardwareRequired')})),
            'font': app.get('font', app.get('assets')), 'imageSampling': app.get('actualImageSampling', app.get('image', (app.get('diagnostics') or {}).get('bitmapInterpolation'))),
            'rawApp': str(directory / 'app.json')}
        identity = json.dumps({key: value for key, value in metadata.items() if key != 'rawApp'}, sort_keys=True)
        if app and identity not in [row['identity'] for row in versions[planned['target']]]:
            versions[planned['target']].append({'identity': identity, **metadata})
    pairs = matrix_pairs(runs)
    groups, pair_groups = [], []
    for case in environment['cases']:
        for target in TARGETS:
            target_pairs = [pair for pair in pairs if pair['traceId'] == case['id'] and pair['target'] == target]
            pair_groups.append({'traceId': case['id'], 'target': target,
                'plannedN': sum(pair['observedPlanned'] and pair['controlPlanned'] for pair in target_pairs),
                'validN': sum(pair['validPair'] for pair in target_pairs),
                'pairOrderCounts': dict(collections.Counter(pair['pairOrder'][0] for pair in target_pairs if pair['observedPlanned'] and pair['controlPlanned'])),
                'metrics': {name: stats([pair['metrics'][name] for pair in target_pairs]) for name in PAIR_METRICS}})
            for mode in MODES:
                subset = [run for run in runs if run['traceId'] == case['id'] and run['target'] == target and run['observationMode'] == mode]
                valid = [run for run in subset if run['valid']]
                group = {'traceId': case['id'], 'case': case['case'], 'level': case['level'], 'target': target, 'observationMode': mode,
                    'captureBackendSelected': capture_contract['captureBackend'], 'captureObservationMode': capture_contract['captureObservationMode'],
                    'qualityProfile': case['qualityProfile'], 'samplingProfile': case.get('samplingProfile'),
                    'plannedN': len(subset), 'validN': len(valid), 'observationQualifiedN': sum(run['observationQualified'] for run in valid),
                    'missingN': sum(run.get('missing', False) for run in subset), 'failedN': sum(not run['valid'] and not run.get('missing', False) for run in subset),
                    'quality': quality_for(case, target, quality),
                    'metrics': {name: stats([run['metrics'][name] for run in valid]) for name in METRICS},
                    'runDirectories': [run['directory'] for run in subset],
                    'observationReasons': sorted({reason for run in valid for reason in run.get('observationReasons', [])})}
                identities = sorted({(run['capture']['backend'], run['capture']['markerTimestampSource']) for run in valid}, key=str)
                group['captureIdentities'] = [{'backend': backend, 'markerTimestampSource': source} for backend, source in identities]
                group['desktopAggregationQualified'] = mode == 'observed' and len(identities) == 1
                if mode == 'observed' and len(identities) > 1:
                    group['observationReasons'].append('Mixed capture backends/timestamp sources; aggregated desktop rates omitted, per-run data retained')
                    for metric in ('desktopObservedChangesPerSecond', 'qualifiedDesktopChangesPerSecond'):
                        group['metrics'][metric] = stats([])
                if mode == 'control' and not valid:
                    group['observationReasons'].append('No valid control: primary caller/process CPU remains N/A; no captured substitution')
                groups.append(group)
    mode_counts = {mode: {'planned': sum(run['observationMode'] == mode for run in runs),
        'finished': sum(run['observationMode'] == mode and not run.get('missing', False) for run in runs),
        'valid': sum(run['observationMode'] == mode and run['valid'] for run in runs),
        'failed': sum(run['observationMode'] == mode and not run['valid'] and not run.get('missing', False) for run in runs),
        'missing': sum(run['observationMode'] == mode and run.get('missing', False) for run in runs),
        'observationQualified': sum(run['observationMode'] == mode and run['observationQualified'] for run in runs)} for mode in MODES}
    finished = sum(not run.get('missing', False) for run in runs)
    if finished != len(plan): issues.append(f'Incomplete collection: {finished}/{len(plan)} run records exist')
    return {'protocolVersion': 11, **capture_contract, 'generatedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'root': str(root),
        'seed': environment['seed'], 'plannedRuns': len(plan), 'finishedRuns': finished, 'validRuns': sum(run['valid'] for run in runs),
        'missingRuns': len(plan) - finished, 'failedRuns': sum(not run['valid'] and not run.get('missing', False) for run in runs),
        'observationQualifiedRuns': sum(run['observationQualified'] for run in runs), 'byObservationMode': mode_counts,
        'primaryCallerTimingMode': 'control', 'primaryProcessCpuMode': 'control', 'metricDefinitions': METRICS,
        'framePercentileMethod': 'Caller frame wall-clock statistics, per run: P50=median, mean=arithmetic mean; P95/P99 use linear interpolation at p*(n-1) in sorted totalMs (p=0.95/0.99). Each run statistic is then summarized as median[min,max] across independent processes.',
        'observerPercentileMethod': 'Observer distributions retain nearest rank: sorted[max(0,ceil(p*n)-1)] for P50/P95/P99, per Observer/Marker.cs Distribution.Of. Observer percentiles are not recomputed with the caller frame interpolation method.',
        'pairedControls': environment.get('pairedControls', any(row['observationMode'] == 'control' for row in plan)),
        'reportGenerator': {'path': str(Path(__file__).resolve()), 'sha256': digest(Path(__file__))},
        'issues': issues, 'changedFrozenFiles': changed, 'completion': completion,
        'sourceProvenance': environment.get('rendererProvenance', []), 'sourceHashFileCount': len(environment.get('hashes', [])),
        'qualityEvidence': {'path': str(Path(quality_path).resolve()), 'sha256': digest(quality_path)} if quality_path else None,
        'metricBoundary': 'Composited-desktop unique matching-tag marker changes / phase2-to-phase3 duration. Desktop Duplication uses LastPresentTime QPC desktop updates; legacy GDI uses capture-completion timestamps. Backend/timestamp boundaries are recorded and never pooled together. Not physical scanout, GPU time or theoretical unthrottled throughput. Missing IDs are unobserved/merged, not proven GPU drops.',
        'aggregation': 'Per trace/level/target/mode only: median[min,max] of up to three independent processes. Primary caller wall-clock durations use control samples only; missing/invalid controls remain N/A and never use observed substitutes. Captured caller wall-clock durations are retained only as paired perturbation observations. Per-run frame mean/P50/P95 are summarized across runs without pooling all frames. Pair ratios are computed within each round before median/range and are not causal confidence estimates. No mixed total score or historical FPS.',
        'qualityBoundary': 'Native stroke, scaled sampler and gradient profiles are explicitly separate. Failed probes apply only to traces using the affected operation. Unknown quality evidence is not a pass.',
        'cpuMemoryBoundary': '调用方每帧/固定序列耗时为墙钟（含同步等待）；实际进程树CPU时间取 GetProcessTimes。Control retains window/TOPMOST/process-tree monitoring overhead. Control actual process CPU and peak private bytes cover the whole monitored lifetime (startup/warmup/measurement/END/exit grace); marker-phase actual CPU is unavailable for controls. Process CPU is a sampled lower bound, with 100%=one logical core. Observed marker-window samples and framework fence/raster/API diagnostics stay separate. Memory peaks between samples may be missed; no physical scanout or GPU throughput is inferred.',
        'groups': groups, 'pairGroups': pair_groups, 'pairs': pairs, 'runs': runs, 'runtimeMetadata': versions, 'diagnostics': diagnostics(diagnostic_roots)}


def fmt(value, digits=2):
    return f'{value:.{digits}f}' if number(value) else 'N/A'


def interval(value):
    return f"{fmt(value['median'])} [{fmt(value['min'])}, {fmt(value['max'])}]" if value['n'] else 'N/A'


def chart_svg(trace_id, groups):
    if any(row['observationMode'] != 'control' for row in groups):
        raise ValueError('Main chart requires control groups; observed timings cannot substitute')
    width, height, left, plot_width = 1040, 112 + len(groups) * 64, 210, 560
    has_data = any(row['metrics']['frameCpuMeanMs']['n'] for row in groups)
    maximum = max((row['metrics']['frameCpuMeanMs']['max'] or 0 for row in groups), default=0) or 1
    scale = plot_width / (maximum * 1.12)
    subtitle = 'Control: 每run帧平均墙钟ms → 三轮 median[min,max]；含同步等待，非CPU核算/GPU吞吐；越低耗时越少'
    svg = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" data-observation-mode="control" data-metric="frameCpuMeanMs">',
        f'<rect width="{width}" height="{height}" fill="#f7f9fc"/>',
        f'<text x="24" y="30" font-family="Segoe UI,sans-serif" font-size="20" fill="#13233c">{html.escape(trace_id)} · control 每帧平均墙钟耗时</text>',
        f'<text x="24" y="54" font-family="Segoe UI,sans-serif" font-size="12" fill="#45566c">{html.escape(subtitle)}</text>']
    for tick in range(6) if has_data else ():
        value = maximum * 1.12 * tick / 5
        x = left + value * scale
        svg.append(f'<path d="M{x:.2f},76 V{height-35}" stroke="#dbe2ed"/><text x="{x:.2f}" y="{height-15}" font-size="11" font-family="Segoe UI,sans-serif" fill="#45566c">{value:.2f} ms</text>')
    for index, row in enumerate(groups):
        y = 100 + index * 64
        stat = row['metrics']['frameCpuMeanMs']
        quality = row['quality']['status']
        color = '#1976b5' if stat['n'] and quality == 'semantic-pass' else '#b47c1b' if stat['n'] and quality == 'limited' else '#8997a8'
        svg.append(f'<text x="24" y="{y+4}" font-family="Segoe UI,sans-serif" font-size="14" fill="#13233c">{html.escape(LABELS[row["target"]])}</text>')
        if stat['n']:
            a, b, median = (left + stat[name] * scale for name in ('min', 'max', 'median'))
            svg.append(f'<rect x="{left}" y="{y-11}" width="{median-left:.2f}" height="20" fill="{color}"/><path d="M{a:.2f},{y-2} H{b:.2f} M{a:.2f},{y-9} V{y+5} M{b:.2f},{y-9} V{y+5}" stroke="#13233c" stroke-width="2"/><circle cx="{median:.2f}" cy="{y-2}" r="4" fill="#13233c"/>')
        value_label = interval(stat) + ' ms' if stat['n'] else 'N/A'
        svg.append(f'<text x="790" y="{y+3}" font-family="Segoe UI,sans-serif" font-size="12" fill="#13233c">{html.escape(value_label)}</text>')
        note = f"control valid {row['validN']}/{row['plannedN']}; independent run means N={stat['n']}; quality {quality}"
        svg.append(f'<text x="{left}" y="{y+28}" font-family="Segoe UI,sans-serif" font-size="11" fill="#45566c">{html.escape(note)}</text>')
    svg.append('</svg>')
    return '\n'.join(svg)


def write_report(summary, output):
    output = Path(output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    charts = output / 'charts'
    charts.mkdir(exist_ok=True)
    def href(path):
        return quote(os.path.relpath(path, output).replace('\\', '/'), safe='/:')
    def table(headers, rows):
        return '<table><thead><tr>' + ''.join(f'<th>{html.escape(str(cell))}</th>' for cell in headers) + '</tr></thead><tbody>' + ''.join('<tr>' + ''.join(f'<td>{html.escape(str(cell))}</td>' for cell in row) + '</tr>' for row in rows) + '</tbody></table>'
    COLLECTOR['save'](output / 'summary-v11.json', summary)
    fields = ['traceId', 'case', 'level', 'target', 'qualityProfile', 'samplingProfile', 'plannedN', 'validN', 'observationQualifiedN', 'failedN', 'missingN', 'qualityStatus', 'qualityReasons', 'captureBackends', 'observationMode', 'markerTimestampSources', 'observationReasons']
    fields += [f'{metric}_{stat}' for metric in METRICS for stat in ('n', 'median', 'min', 'max')]
    with (output / 'summary-v11.csv').open('w', newline='', encoding='utf-8-sig') as stream:
        writer = csv.DictWriter(stream, fieldnames=fields); writer.writeheader()
        for group in summary['groups']:
            row = {key: group.get(key) for key in fields[:10]}
            row.update(failedN=group['failedN'], missingN=group['missingN'], qualityStatus=group['quality']['status'], qualityReasons='; '.join(group['quality']['reasons']),
                       observationMode=group.get('observationMode'), captureBackends='; '.join(sorted({value['backend'] for value in group.get('captureIdentities', [])})),
                       markerTimestampSources='; '.join(sorted({str(value['markerTimestampSource']) for value in group.get('captureIdentities', [])})),
                       observationReasons='; '.join(group['observationReasons']))
            row.update({f'{metric}_{stat}': group['metrics'][metric][stat] for metric in METRICS for stat in ('n', 'median', 'min', 'max')})
            writer.writerow(row)
    with (output / 'runs-v11.csv').open('w', newline='', encoding='utf-8-sig') as stream:
        fields = ['index', 'round', 'traceId', 'target', 'observationMode', 'pairId', 'directory', 'valid', 'missing', 'observationQualified', 'observerLimited', 'badCoverage', 'failures', 'observationReasons', 'captureBackend', 'markerTimestampSource', 'eventDriven'] + list(METRICS)
        writer = csv.DictWriter(stream, fieldnames=fields); writer.writeheader()
        for run in summary['runs']:
            row = {key: run.get(key) for key in fields if key not in METRICS}
            row.update(failures='; '.join(run['failures']), observationReasons='; '.join(run.get('observationReasons', [])), **run['metrics'])
            capture = run.get('capture') or {}
            row.update(captureBackend=capture.get('backend'), markerTimestampSource=capture.get('markerTimestampSource'), eventDriven=capture.get('eventDriven'))
            writer.writerow(row)
    with (output / 'pairs-v11.csv').open('w', newline='', encoding='utf-8-sig') as stream:
        fields = ['pairId', 'traceId', 'target', 'round', 'observedDirectory', 'controlDirectory', 'observedPlanned', 'controlPlanned', 'validPair', 'pairOrder', 'failures'] + list(PAIR_METRICS)
        writer = csv.DictWriter(stream, fieldnames=fields); writer.writeheader()
        for pair in summary['pairs']:
            row = {key: pair[key] for key in fields if key not in PAIR_METRICS}
            row.update(pairOrder=' then '.join(pair['pairOrder']), failures='; '.join(pair['failures']), **pair['metrics'])
            writer.writerow(row)
    count = f"Planned {summary['plannedRuns']}; collected {summary['finishedRuns']}; valid {summary['validRuns']}; failed {summary['failedRuns']}; missing {summary['missingRuns']}; observed flags pass {summary['observationQualifiedRuns']}."
    mode_counts = '; '.join(f"{mode}: valid {values['valid']}/{values['planned']}, failed {values['failed']}, missing {values['missing']}" for mode, values in summary['byObservationMode'].items())
    count += ' ' + mode_counts
    mode_text = f"Capture backend: {summary.get('captureBackend')}; capture interpretation: {summary.get('captureObservationMode')}. {summary.get('observationInterpretation', '')}"
    md = ['# Canvas v11 fixed-trace report', '', count, '', mode_text, '', summary['metricBoundary'], '', summary['aggregation'], '', summary['framePercentileMethod'], '', summary['observerPercentileMethod'], '', summary['cpuMemoryBoundary'], '', summary['qualityBoundary'], '']
    page = ['<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Canvas v11 report</title><style>body{font:15px/1.55 Segoe UI,sans-serif;color:#13233c;background:#f7f9fc;margin:0}main{max-width:1320px;margin:auto;padding:32px}h1,h2{line-height:1.2}section{background:white;padding:24px;margin:24px 0;border:1px solid #dbe2ed;border-radius:10px}table{border-collapse:collapse;width:100%;font-size:13px;display:block;overflow-x:auto}th,td{text-align:left;padding:9px;border-bottom:1px solid #dbe2ed}th{background:#edf2f8}img{max-width:100%;height:auto}.issues{background:#fff0d9;padding:16px}a{color:#11619c}code{overflow-wrap:anywhere}small{color:#45566c}</style><main><h1>Canvas v11 fixed-trace report</h1>', f'<p>{html.escape(count)}</p>']
    page.append('<p>' + html.escape(mode_text) + '</p>')
    for text in ('metricBoundary', 'aggregation', 'framePercentileMethod', 'observerPercentileMethod', 'cpuMemoryBoundary', 'qualityBoundary'):
        page.append(f'<p>{html.escape(summary[text])}</p>')
    links = ' · '.join(f'<a href="{name}">{name}</a>' for name in ('summary-v11.json', 'summary-v11.csv', 'runs-v11.csv', 'pairs-v11.csv', 'report-v11.md'))
    page.append(f'<p>{links}</p>')
    if summary['qualityEvidence']:
        evidence = summary['qualityEvidence']
        page.append(f'<p>Finalized scoped semantic evidence: <a href="{href(evidence["path"])}">quality JSON</a>; SHA256 <code>{html.escape(evidence["sha256"])}</code>. This does not claim identical full-frame, AA or font-raster pixels.</p>')
    if summary['issues']:
        page.append('<div class="issues">' + '<br>'.join(html.escape(value) for value in summary['issues']) + '</div>')
        md.extend(['## Collection limitations', ''] + [f'- {issue}' for issue in summary['issues']] + [''])
    for trace_id in dict.fromkeys(group['traceId'] for group in summary['groups']):
        case_groups = [row for row in summary['groups'] if row['traceId'] == trace_id]
        groups = [row for row in case_groups if row['observationMode'] == 'observed']
        controls = {row['target']: row for row in case_groups if row['observationMode'] == 'control'}
        paired = {row['target']: row for row in summary['pairGroups'] if row['traceId'] == trace_id}
        filename = f'chart-{trace_id}.svg'
        (charts / filename).write_text(chart_svg(trace_id, [controls[row['target']] for row in groups]), encoding='utf-8')
        chart_caption = 'Control 每run的帧平均墙钟耗时（ms），再取独立三轮 median[min,max]；含 API 同步等待，非实际CPU核算/GPU吞吐。越低表示该调用边界耗时越少。质量受限为琥珀色，未验证为灰色；缺失control为N/A。'
        primary_headers = ['Framework', 'Control valid/planned (failed/missing)', '固定序列调用方总耗时 ms（墙钟，含同步等待）', '帧耗时P50 ms', '帧平均耗时 ms', '帧耗时P95 ms', '帧耗时P99 ms', '实际进程CPU时间 ms（whole lifetime）', '实际进程CPU %（whole lifetime）', 'Control peak private MiB', 'Control load / static prep ms']
        observed_headers = ['Framework', 'Observed valid/planned (failed/missing)', 'Obs flags pass N', 'Sampled changes/s [min,max]', 'Flags subset changes/s', 'Captured调用方总耗时 ms（墙钟，扰动）', 'Valid pairs/planned', 'Paired调用方墙钟耗时比 captured/control', 'Paired调用方墙钟耗时差 ms', 'Semantic quality']
        primary_rows, observed_rows = [], []
        for row in groups:
            control, pair = controls[row['target']], paired[row['target']]
            cm, om = control['metrics'], row['metrics']
            primary_rows.append([LABELS[row['target']], f"{control['validN']}/{control['plannedN']} ({control['failedN']}/{control['missingN']})",
                interval(cm['callerFrameTotalMs']), interval(cm['frameCpuMedianMs']), interval(cm['frameCpuMeanMs']), interval(cm['frameCpuP95Ms']), interval(cm['frameCpuP99Ms']), interval(cm['treeWholeRunCpuLowerBoundMs']), interval(cm['treeWholeRunCpuPercentOneCore']), interval(cm['peakTreePrivateMiB']), f"{interval(cm['assetLoadMs'])} / {interval(cm['staticPrepareMs'])}"])
            ratio = pair['metrics']['callerDurationRatio']
            observed_rows.append([LABELS[row['target']], f"{row['validN']}/{row['plannedN']} ({row['failedN']}/{row['missingN']})", row['observationQualifiedN'],
                interval(om['desktopObservedChangesPerSecond']), interval(om['qualifiedDesktopChangesPerSecond']), interval(om['callerFrameTotalMs']),
                f"{pair['validN']}/{pair['plannedN']}", f"{interval(ratio)}; N={ratio['n']}", interval(pair['metrics']['callerDurationDeltaMs']), row['quality']['status']])
        page.extend([f'<section><h2>{html.escape(trace_id)}</h2><p>Quality profile: <code>{html.escape(groups[0]["qualityProfile"])}</code>; sampling: {html.escape(str(groups[0]["samplingProfile"]))}.</p>',
            '<h3>主调用方耗时（control；墙钟，含同步等待）与实际进程CPU（whole lifetime）</h3>',
            f'<img src="charts/{quote(filename)}" alt="{html.escape(trace_id)} control 每帧平均墙钟耗时 ms：三轮 median[min,max]">',
            '<p>' + html.escape(chart_caption) + '</p>', table(primary_headers, primary_rows), '<h3>Observed sampling and paired perturbation</h3>', table(observed_headers, observed_rows)])
        md.extend([f'## {trace_id}', '', f"Quality profile: {groups[0]['qualityProfile']}; sampling: {groups[0]['samplingProfile']}.", '', '### 主调用方耗时（control；墙钟，含同步等待）与实际进程CPU（whole lifetime）', '',
            f'![{trace_id} control 每帧平均墙钟耗时 ms](charts/{filename})', '', chart_caption, '',
            '| ' + ' | '.join(primary_headers) + ' |', '| ' + ' | '.join('---' for _ in primary_headers) + ' |'] + ['| ' + ' | '.join(map(str, row)) + ' |' for row in primary_rows] + ['', '### Observed sampling and paired perturbation', '',
            '| ' + ' | '.join(observed_headers) + ' |', '| ' + ' | '.join('---' for _ in observed_headers) + ' |'] + ['| ' + ' | '.join(map(str, row)) + ' |' for row in observed_rows] + [''])
        observer_headers = ['Framework', 'Observed sequences %', 'Capture P95 ms', 'Event/sample gap P95 ms', 'Observer whole-run CPU %', 'Observer measurement CPU ms', 'Readback latency P95 ms', 'Native processing P95 ms', 'Extra desktop frames', 'First observed marker ms', 'Control observer lifetime CPU %']
        observer_metrics = ('visibleCoveragePercent', 'measurementCaptureP95Ms', 'measurementGapP95Ms', 'observerCpuPercentOneCore', 'observerMeasurementCpuMs', 'readbackLatencyP95Ms', 'nativeProcessingP95Ms', 'accumulatedExtraDesktopFrames', 'processColdFirstMarkerMs')
        observer_rows = [[LABELS[row['target']]] + [interval(row['metrics'][name]) for name in observer_metrics] + [interval(controls[row['target']]['metrics']['observerCpuPercentOneCore'])] for row in groups]
        page.append('<h3>Observer sampling and overhead</h3>' + table(observer_headers, observer_rows))
        telemetry_note = 'Readback/native/extra-frame counters retain their raw Observer scope. Desktop Duplication gaps follow desktop events; QPC tick precision is not capture throughput. Extra accumulated frames are not proven GPU drops.'
        page.append('<p><small>' + html.escape(telemetry_note) + '</small></p>')
        md.extend(['### Observer sampling and overhead', '', telemetry_note, '', '| ' + ' | '.join(observer_headers) + ' |', '| ' + ' | '.join('---' for _ in observer_headers) + ' |'] + ['| ' + ' | '.join(map(str, row)) + ' |' for row in observer_rows] + [''])
        for row in case_groups:
            raw_links, markdown_links = [], []
            for directory in row['runDirectories']:
                record = Path(summary['root']) / directory / 'run.json'
                label = directory.split('-', 1)[0] + '/' + row['observationMode']
                if record.is_file():
                    raw_links.append(f'<a href="{href(record)}">{html.escape(label)}</a>')
                    markdown_links.append(f'[{label}]({href(record)})')
                else:
                    raw_links.append(f'{html.escape(label)} missing')
                    markdown_links.append(f'{label} missing')
            page.append(f'<p><small>{html.escape(LABELS[row["target"]])} raw runs: ' + ', '.join(raw_links) + '</small></p>')
            md.extend([f"{LABELS[row['target']]} raw runs: " + ', '.join(markdown_links), ''])
            notes = row['quality']['reasons'] + row['observationReasons']
            if notes:
                text = f"{LABELS[row['target']]}: " + '; '.join(notes)
                page.append(f'<p><small>{html.escape(text)}</small></p>'); md.extend([text, ''])
        page.append('</section>')
    page.append('<section><h2>Diagnostic boundaries and raw evidence</h2>')
    md.extend(['## Diagnostic boundaries and raw evidence', ''])
    for target in TARGETS:
        example = next((run for run in summary['runs'] if run['target'] == target and run['valid'] and run['observationMode'] == 'observed'), None) or next((run for run in summary['runs'] if run['target'] == target and run['valid']), None)
        if example:
            text = f"{LABELS[target]} raw frameCpu elapsed-timer boundary: {example['cpuBoundary'] or 'see raw producer metadata'}."
            page.append(f'<p>{html.escape(text)} <a href="{href(Path(example["rawDirectory"]) / "app.json")}">Raw app / versions / hardware / native diagnostics</a></p>')
            md.extend([text + f' [Raw app]({href(Path(example["rawDirectory"]) / "app.json")})', ''])
            capture = example.get('capture') or {}
            capture_text = f"{LABELS[target]} observer: backend={capture.get('backend')}; eventDriven={capture.get('eventDriven')}; marker timestamp={capture.get('markerTimestampSource')}."
            page.append('<p>' + html.escape(capture_text) + '</p><details><summary>Observer native/clock telemetry</summary><pre>' + html.escape(json.dumps(capture, ensure_ascii=False, indent=2)) + '</pre></details>')
            md.extend([capture_text + f' [Raw observer]({href(Path(example["rawDirectory"]) / "observer.json")})', ''])
    diagnostic = summary['diagnostics']
    page.append('<h3>Marker-only / no-capture control</h3><p>' + html.escape(diagnostic['boundary']) + '</p>')
    md.extend(['### Marker-only / no-capture control', '', diagnostic['boundary'], ''])
    if not diagnostic['records']:
        page.append('<p>No additional external calibration/control evidence supplied; matrix paired results, when present, remain listed per case.</p>')
        md.extend(['No additional external calibration/control evidence supplied; matrix paired results, when present, remain listed per case.', ''])
    for pair in diagnostic['pairs']:
        diagnostic_labels = {'elapsedMs': '全生命周期墙钟耗时 ms', 'observerCpuMs': 'Observer实际CPU时间 ms', 'treeCpuLowerBoundMs': 'GetProcessTimes实际进程树CPU时间 ms（lifetime）', 'peakTreePrivateBytes': '采样private bytes峰值'}
        rows = [[METRICS.get(field, diagnostic_labels.get(field, field)), interval(value['capture']), interval(value['control']), fmt(value['medianCaptureMinusControl'])] for field, value in pair['differences'].items()]
        attempts = f"Valid capture {pair['captureN']}/{pair['captureAttemptsN']} attempts; valid control {pair['controlN']}/{pair['controlAttemptsN']} attempts; failed evidence records {len(pair['failedEvidence'])}. Failed originals and successful corrections remain separately listed below."
        page.append(f'<h3>{html.escape(LABELS[pair["target"]])}: same-trace diagnostic pair ({html.escape(pair["captureBackend"])})</h3><p>{html.escape(attempts)}</p>' + table(['Field', 'Capture', 'Control', 'Capture minus control'], rows))
        md.extend([attempts, ''])
        md.extend([f"{LABELS[pair['target']]} paired trace SHA256 `{pair['traceSha256']}`; capture N={pair['captureN']}, control N={pair['controlN']}.", '', '| Field | Capture | Control | Capture minus control |', '| --- | --- | --- | --- |'] + ['| ' + ' | '.join(map(str, row)) + ' |' for row in rows] + [''])
    for record in diagnostic['records']:
        text = f"{record.get('target', 'unknown')} {record.get('observationMode', 'unknown')} {record['kind']}: {record.get('status', record.get('error', 'unknown'))}; {(record.get('sampling') or {}).get('reasons', [])}"
        page.append(f'<p>{html.escape(text)} <a href="{href(record["path"])}">Raw diagnostic</a></p>')
        md.extend([text + f' [Raw diagnostic]({href(record["path"])})', ''])
    provenance = f"Frozen files: {summary['sourceHashFileCount']}; old renderer reference groups: " + ', '.join(f"v{row['version']}={row['sourceFiles']} source files" for row in summary['sourceProvenance']) + '. Source history contributes provenance only; no historical performance samples are mixed.'
    provenance += f" Report generator SHA256: {summary['reportGenerator']['sha256']}. The report generator is recorded separately from benchmark input/source/binary freezing."
    page.append('<p>' + html.escape(provenance) + '</p></section></main></html>')
    md.extend(['## Provenance', '', provenance, '', f"Quality evidence: {summary['qualityEvidence']}", '', 'Per-run failures, missing IDs, CPU brackets and raw directories are preserved in summary-v11.json and runs-v11.csv. Unqualified/slow samples remain visible; no best-run selection.'])
    (output / 'report-v11.md').write_text('\n'.join(md) + '\n', encoding='utf-8')
    (output / 'report-v11.html').write_text('\n'.join(page), encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, default=COLLECTOR['REPO'] / 'artifacts/canvas-optimization/run-v11')
    parser.add_argument('--output', type=Path, help='Report directory; defaults to the raw run directory')
    parser.add_argument('--quality', type=Path, help='Final independent semantic/quality JSON evidence')
    parser.add_argument('--diagnostic-root', type=Path, action='append', default=[], help='Existing calibration/control folder or observer JSON, repeatable')
    args = parser.parse_args()
    summary = build_report(args.input, args.quality, args.diagnostic_root)
    write_report(summary, args.output or args.input)
    print(json.dumps({key: summary[key] for key in ('plannedRuns', 'finishedRuns', 'validRuns', 'failedRuns', 'missingRuns', 'observationQualifiedRuns', 'issues')}, indent=2))


if __name__ == '__main__':
    main()
