#!/usr/bin/env python3
"""Serial v11 collection through the shared desktop Observer. --plan-only is CPU-only."""
import argparse
import collections
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import random
import subprocess
import sys
import time

HERE = Path(__file__).resolve().parent
V11 = HERE.parent
REPO = V11.parents[2]
TARGETS = ('square-software', 'square-direct2d', 'square-vulkan', 'electron', 'flutter', 'avalonia')


def load(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def save(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + '\n', encoding='utf-8')


def digest(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def require(value, message):
    if not value:
        raise ValueError(message)


def number(value):
    return isinstance(value, (float, int)) and not isinstance(value, bool) and math.isfinite(value)


def trace_matrix(suite='comprehensive'):
    root = V11 / ('Comprehensive' if suite == 'comprehensive' else 'Traces')
    if suite == 'comprehensive' and not (root / 'manifest.json').is_file():
        spec = importlib.util.spec_from_file_location('comprehensive_inputs', HERE / 'generate-comprehensive.py')
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        module.generate()
    manifest = load(root / 'manifest.json')
    require(manifest['protocolVersion'] == 11 and manifest['complete'] is True, 'Trace manifest is not complete v11')
    require((manifest['width'], manifest['height'], manifest['devicePixelRatio']) == (1280, 800, 1), 'Wrong trace viewport')
    rows = manifest['benchmarkTraces']
    require(len({row['id'] for row in rows}) == len(rows), 'Duplicate benchmark trace IDs')
    require(all(row.get('kind') == 'benchmark' and 'semantic' not in row['case'] and 'calibration' not in row['case'] for row in rows), 'Non-benchmark input entered timing matrix')
    require(digest(root / 'schema.json') == manifest['schemaSha256'], 'Trace schema hash mismatch')
    require(digest(HERE / ('generate-comprehensive.py' if suite == 'comprehensive' else 'generate-traces.py')) == manifest['generatorSha256'], 'Trace generator hash mismatch')
    files = {root / 'manifest.json', root / 'schema.json'}
    all_rows = rows + manifest.get('semanticFixtures', []) + manifest.get('calibrationTraces', [])
    require(len(all_rows) == manifest['traceCount'], 'Manifest trace count mismatch')
    for row in all_rows:
        path = root / row['trace']
        require(digest(path) == row['traceSha256'], f'Trace hash mismatch: {path}')
        trace = load(path)
        require((trace['protocolVersion'], trace['width'], trace['height'], trace['devicePixelRatio']) == (11, 1280, 800, 1), f'Trace viewport/protocol: {row["id"]}')
        require(len(trace['frames']) == row['warmupFrames'] + row['measureFrames'], f'Frame count: {row["id"]}')
        require(trace['expectedMeasuredOpcodeTotals'] == row['expectedMeasuredOpcodeTotals'], f'Opcode metadata: {row["id"]}')
        actual = collections.Counter(op[0] for frame in trace['frames'][row['warmupFrames']:] for op in frame['ops'])
        require(dict(actual) == row['expectedMeasuredOpcodeTotals'], f'Actual input opcodes: {row["id"]}')
        files.add(path)
        for name, expected in manifest['assetsSha256'].items():
            asset = path.parent / name
            require(digest(asset) == expected, f'Asset hash mismatch: {asset}')
            files.add(asset)
    for row in rows: row['traceRoot'] = str(root)
    return manifest, rows, files


def schedule(cases, seed, paired_controls=False, rounds=3, observed=False):
    output = []
    for round_number in range(1, rounds + 1):
        ordered = list(cases)
        random.Random(seed + round_number).shuffle(ordered)
        for position, case in enumerate(ordered):
            offset = (position + round_number - 1) % len(TARGETS)
            for target in TARGETS[offset:] + TARGETS[:offset]:
                index = len(output)
                directory = f'r{round_number}-{case["id"]}-{target}'
                output.append({'index': index + 1, 'baseIndex': index + 1, 'round': round_number, 'target': target, 'traceId': case['id'],
                               'tag': index % 255 + 1, 'directory': directory, 'pairId': directory, 'observationMode': 'observed' if observed else 'control'})
    if not paired_controls:
        return output
    paired = []
    first_mode_offset = random.Random(seed).randrange(2)
    for base in output:
        modes = ('observed', 'control') if (base['baseIndex'] - 1 + first_mode_offset) % 2 == 0 else ('control', 'observed')
        for mode in modes:
            index = len(paired)
            paired.append({**base, 'index': index + 1, 'tag': index % 255 + 1, 'observationMode': mode,
                           'pairOrder': list(modes), 'directory': base['directory'] + '/' + mode})
    return paired


def adapters(args):
    square = args.square.resolve()
    commands = {
        **{f'square-{name.lower()}': {'fileName': str(square), 'args': ['--backend', name], 'workingDirectory': str(square.parent)}
           for name in ('Software', 'Direct2D', 'Vulkan')},
        'electron': {'fileName': str(args.electron.resolve()), 'args': ['--no-sandbox', str(V11 / 'Electron')], 'workingDirectory': str(V11 / 'Electron')},
        'flutter': {'fileName': str(args.flutter.resolve()), 'args': [], 'workingDirectory': str(args.flutter.resolve().parent)},
        'avalonia': {'fileName': str(args.avalonia.resolve()), 'args': [], 'workingDirectory': str(args.avalonia.resolve().parent)}}
    if args.baseline_square:
        baseline = args.baseline_square.resolve()
        result = {('local-' + key if key.startswith('square-') else key): value for key, value in commands.items()}
        result.update({'remote-' + key: {**value, 'fileName': str(baseline), 'workingDirectory': str(baseline.parent)}
                       for key, value in commands.items() if key.startswith('square-')})
        return {key: result[key] for key in TARGETS}
    return {key: commands[key] for key in TARGETS}



def capture_contract(backend, enabled=True):
    if not enabled:
        return {'captureBackend': backend, 'captureEnabled': False, 'eventDrivenRequired': False,
                'captureObservationMode': 'disabled-control', 'observationModeLimited': False,
                'observationInterpretation': 'No capture initialized; CPU/tree-memory and producer cadence only, not desktop/scanout.'}
    require(backend in ('desktop-duplication', 'gdi'), 'Unknown capture backend')
    return {'captureBackend': backend, 'eventDrivenRequired': backend == 'desktop-duplication',
            'captureObservationMode': 'desktop-update-events' if backend == 'desktop-duplication' else 'display-synchronized-coarse-sampling',
            'observationModeLimited': backend == 'gdi',
            'observationInterpretation': 'Desktop update events with LastPresentTime QPC timestamps; qualification follows recorded native metadata and Observer flags.' if backend == 'desktop-duplication' else '受限的采样可见变化率/可能漏见（显示同步、时间边界量化） (limited sampled visible-change rate; possible unseen sequences). Boundary quantization can place count/duration slightly above display refresh. Interpret alongside fixed-trace caller durations, process-tree CPU and private bytes; capture/control perturbation stays separate.'}


def frozen_files(inputs, commands, observer, capture_backend='desktop-duplication', observed=False, baseline_root=None):
    # Current source/binary/trace guard, not historical renderer qualifications.
    if observed and capture_backend == 'desktop-duplication':
        require((observer.parent / 'CanvasObserverCapture.dll').is_file(), 'Missing explicitly requested capture DLL')
    files = set(inputs) | {V11 / 'PROTOCOL-v11.md'}
    runtime_profiles = [(V11 / 'Electron' / name).resolve() for name in ('.user-data', '.tmp')]
    def runtime_file(path): return any(path.resolve().is_relative_to(root) for root in runtime_profiles)
    excluded = {'bin', 'obj', 'obj-native', 'build', '.dart_tool', '.git', '.runtime', 'ephemeral', '.pub-cache', '.sdk', 'node_modules', '__pycache__'}
    suffixes = {'.cs', '.csproj', '.js', '.dart', '.cpp', '.h', '.yaml', '.lock', '.py', '.html', '.ps1', '.props', '.targets', '.glsl', '.spv'}
    folders = [V11, REPO / 'src']
    if baseline_root:
        require(baseline_root.is_dir(), 'Provide the actual baseline source root, not an old results directory')
        folders.append(baseline_root / 'src')
    for folder in folders:
        for path in folder.rglob('*'):
            if path.is_file() and not runtime_file(path) and not excluded.intersection(path.relative_to(folder).parts) and (folder.name == 'src' or path.suffix in suffixes or (path.suffix == '.json' and path.relative_to(folder).parts[0] not in ('Traces', 'Comprehensive'))):
                files.add(path)
    for executable in {Path(command['fileName']) for command in commands.values()} | {observer}:
        require(executable.is_file(), f'Missing built executable: {executable}')
        for path in executable.parent.rglob('*'):
            if path.is_file() and not runtime_file(path):
                files.add(path)
    for root in [REPO] + ([baseline_root] if baseline_root else []):
        for name in ('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'global.json', 'NuGet.Config', 'NuGet.config'):
            if (root / name).is_file(): files.add(root / name)
    rows = [{'path': str(path.resolve()), 'sha256': digest(path), 'bytes': path.stat().st_size} for path in sorted(files)]
    return rows, {'policy': 'Actual current source+binary+chosen input frozen; exact runtime profile/cache excluded; old v8/v9/v10 receipts are history, not current qualification', 'sourceRoot': str(REPO), 'runtimeProfileExclusions': [str(x) for x in runtime_profiles], 'baselineSourceRoot': str(baseline_root) if baseline_root else None}


def validate_run(app, obs, case, run, observer_exit, expected_assets=None, capture_backend='desktop-duplication'):
    errors = []
    def check(value, message):
        if not value:
            errors.append(message)
    mode = run.get('observationMode', 'observed')
    check(mode in ('observed', 'control'), 'unknown observation mode')
    control = mode == 'control'
    check(observer_exit == 0 and obs.get('status') == ('control-complete' if control else 'ok') and obs.get('childExitCode') == 0 and not obs.get('failures'), 'observer/child unsuccessful')
    allowed_status = ('ok', 'completed') if run['target'] == 'flutter' else ('ok',)
    check(app.get('status', 'ok') in allowed_status, 'application unsuccessful')
    expected_framework = {'square-software': 'Square Software', 'square-direct2d': 'Square Direct2D', 'square-vulkan': 'Square Vulkan', 'electron': 'Electron', 'flutter': 'Flutter', 'avalonia': 'Avalonia'}
    check(app.get('framework') == expected_framework[run['target']], 'application framework identity')
    for data, label in ((app, 'app'), (obs, 'observer')):
        check(data.get('protocolVersion') == 11 and data.get('tag') == run['tag'], f'{label} protocol/tag')
        check(str(data.get('traceSha256', '')).lower() == case['traceSha256'], f'{label} trace hash')
    check(app.get('clientWidth') == 1280 and app.get('clientHeight') == 800 and app.get('devicePixelRatio') == 1, 'application viewport/DPR')
    check(app.get('warmupFrames') == case['warmupFrames'] and app.get('measureFrames') == case['measureFrames'], 'application fixed frame counts')
    check(app.get('timingEligible') is True and app.get('verificationOnly', False) is False and app.get('verify', False) in (False, 0), 'verification entered timing data')
    check(app.get('actualMeasuredOpcodeTotals') == case['expectedMeasuredOpcodeTotals'], 'application opcode totals')
    check(isinstance(app.get('versions'), dict) and bool(app['versions']), 'application versions absent')
    for field in ('assetLoadMs', 'staticPrepareMs'):
        check(number(app.get(field)) and app[field] >= 0, f'application {field}')
    font_hash = (app.get('assets') or {}).get('fontSha256') or (app.get('font') or {}).get('sha256')
    if expected_assets:
        check(font_hash == expected_assets['font.ttf'], 'shared font hash absent/mismatched')
        image_hash = (app.get('assets') or {}).get('imageSha256') or (app.get('image') or {}).get('sha256')
        if image_hash is not None:
            check(image_hash == expected_assets['image.png'], 'shared image hash mismatched')
    cpu = app.get('frameCpu')
    check(isinstance(cpu, list) and len(cpu) == case['measureFrames'], 'measurement CPU total array')
    if not isinstance(cpu, list):
        cpu = []
    for index, row in enumerate(cpu):
        if not isinstance(row, dict):
            check(False, 'measurement CPU row is not an object')
            continue
        sequence = row.get('seq', row.get('sequence'))
        check(sequence == index, 'measurement CPU sequence order')
        for field in ('nativePrepareMs', 'recordSubmitMs', 'totalMs'):
            check(number(row.get(field)) and row[field] >= 0, f'CPU {field}')
        check('presentMs' in row and (row['presentMs'] is None or (number(row['presentMs']) and row['presentMs'] >= 0)), 'CPU presentMs')
    producer = next((app[key] for key in ('producerFrameTimes', 'producerFrameTimestampsMs', 'producerFrames') if key in app), None)
    check(isinstance(producer, list) and len(producer) >= case['measureFrames'], 'producer diagnostic frame array')
    if run['target'].startswith('square-'):
        check(bool((app.get('versions') or {}).get('context')), 'Square context metadata absent')
    else:
        check(bool(app.get('hardware') or app.get('hardwareStatus')), 'application hardware/default-pipeline metadata absent')
    if run['target'] in ('square-direct2d', 'square-vulkan'):
        check(app.get('hardwareRequired') is True, 'Square hardware requirement absent')
    if run['target'] == 'square-vulkan':
        vk = app.get('versions', {}).get('vulkan', {}) or {}
        check(vk.get('device', {}).get('DeviceType') in ('DiscreteGpu', 'IntegratedGpu'), 'Vulkan hardware device absent')
        check(vk.get('readbackEnabled') is False, 'Vulkan timed readback enabled')
    window = obs.get('window') or {}
    check(isinstance(obs.get('rootPid'), int) and obs['rootPid'] > 0 and window.get('ownerPid') == obs['rootPid'] and window.get('topmostWithoutActivation') is True, 'observer root-owned/topmost window')
    check('canvas v11' in str(window.get('title', '')).lower(), 'observer window title')
    check((window.get('clientWidth'), window.get('clientHeight'), window.get('devicePixelRatio'), window.get('dpi')) == (1280, 800, 1, 96), 'observer viewport/DPR')
    check((obs.get('launch') or {}).get('observeFrames') is (not control), 'launch observeFrames does not match observation mode')
    check((obs.get('launch') or {}).get('captureBackend') == capture_backend, 'formal launch capture backend mismatch')
    check(obs.get('expectedMeasuredFrames') == case['measureFrames'], 'observer expected frame count')
    if control:
        check('marker' in obs and obs['marker'] is None, 'control must have marker:null')
        check((obs.get('sampling') or {}).get('enabled') is False, 'control sampling must be disabled')
        check(obs.get('capture') is None, 'control initialized capture')
        return errors
    marker = obs.get('marker') or {}
    count, duration = marker.get('uniqueMeasuredCount'), marker.get('measurementDurationMs')
    check(isinstance(count, int) and 0 < count <= case['measureFrames'] and number(duration) and duration > 0, 'observer marker count/duration')
    if number(duration) and duration > 0 and isinstance(count, int):
        rate = marker.get('desktopObservedChangesPerSecond')
        check(number(rate) and math.isclose(rate, count * 1000 / duration, rel_tol=0, abs_tol=1e-6), 'observer count/duration formula')
        start, end = marker.get('firstMeasurementMs'), marker.get('firstEndMs')
        check(number(start) and number(end) and math.isclose(end - start, duration, abs_tol=1e-6), 'observer phase boundary duration')
    unique = marker.get('uniqueMeasuredFrames', [])
    sequences = [row.get('seq') for row in unique if isinstance(row, dict)] if isinstance(unique, list) else []
    check(len(sequences) == count and len(set(sequences)) == len(sequences) and all(isinstance(seq, int) and 0 <= seq < case['measureFrames'] for seq in sequences), 'observer unique sequences')
    check(marker.get('missingMeasuredSequences') == sorted(set(range(case['measureFrames'])) - set(sequences)), 'observer unobserved sequence accounting')
    transitions = marker.get('transitionRecords', [])
    matching = [row for row in transitions if isinstance(row, dict) and row.get('tag') == run['tag']]
    check(any(row.get('phase') == 2 for row in matching) and any(row.get('phase') == 3 and row.get('seq') == case['measureFrames'] for row in matching), 'observer measurement/end boundaries absent')
    sampling = obs.get('sampling') or {}
    check(sampling.get('enabled') is True, 'formal desktop capture is disabled')
    check(sampling.get('backend') == capture_backend and sampling.get('eventDriven') is (capture_backend == 'desktop-duplication'), 'capture backend/eventDriven does not match explicitly selected mode')
    check(isinstance(sampling.get('badCoverage'), bool) and isinstance(sampling.get('observerLimited'), bool), 'observer coverage flags absent')
    return errors


def main():
    global TARGETS
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--suite', choices=('comprehensive', 'extended'), default='comprehensive')
    parser.add_argument('--observe', action='store_true', help='Explicit diagnostic capture; default is control only')
    parser.add_argument('--baseline-square', type=Path, help='Prebuilt SAME v11 harness bound to baseline engine; adds three before targets')
    parser.add_argument('--baseline-root', type=Path, help='Actual baseline engine source root for current provenance')
    parser.add_argument('--rounds', type=int, default=3, help='Default N3; use 1 only for explicit smoke diagnostics')
    parser.add_argument('--targets', nargs='+', help='Explicit diagnostic subset; ordinary default remains six targets')
    parser.add_argument('--output', type=Path, default=REPO / ('artifacts/canvas-comprehensive/' + time.strftime('%Y%m%d-%H%M%S')))
    parser.add_argument('--seed', type=int, default=305441741)
    parser.add_argument('--timeout-seconds', type=int, default=180)
    parser.add_argument('--capture-backend', choices=('desktop-duplication', 'gdi'), default='desktop-duplication', help='Explicit capture mode; GDI has display-synchronized coarse sampling and possible unseen sequences. No automatic fallback.')
    parser.add_argument('--observer', type=Path, default=V11 / 'Observer/bin/Release/net10.0/Observer.exe')
    parser.add_argument('--square', type=Path, default=V11 / 'Square/bin/Release/net10.0/win-x64/publish/CanvasBench.SquareV11.exe')
    parser.add_argument('--electron', type=Path, default=V11.parent / 'Electron/.runtime/electron.exe')
    parser.add_argument('--flutter', type=Path, default=V11 / 'Flutter/build/windows/x64/runner/Release/square_canvas_v11.exe')
    parser.add_argument('--avalonia', type=Path, default=V11 / 'Avalonia/bin/Release/net10.0/win-x64/publish/CanvasBenchmark.V11.Avalonia.exe')
    parser.add_argument('--paired-controls', action='store_true', help='Pair each base run with an independent no-capture control; alternate pair order deterministically (1044 processes).')
    parser.add_argument('--plan-only', action='store_true', help='CPU-only trace/opcode/hash validation and matrix summary; launches nothing')
    args = parser.parse_args()
    require(args.rounds > 0, 'rounds must be positive')
    require(not args.paired_controls or args.suite == 'extended', 'Paired capture/control is explicit extended diagnostics only')
    if args.baseline_square:
        TARGETS = tuple('remote-' + name for name in TARGETS[:3]) + tuple('local-' + name for name in TARGETS[:3]) + TARGETS[3:]
        require(args.plan_only or args.baseline_root is not None, 'Baseline runs require --baseline-root provenance')
    if args.targets:
        require(set(args.targets) <= set(TARGETS), 'Unknown diagnostic target')
        TARGETS = tuple(args.targets)
    manifest, cases, inputs = trace_matrix(args.suite)
    plan = schedule(cases, args.seed, args.paired_controls, args.rounds, args.observe)
    if args.plan_only:
        print(json.dumps({'suite': args.suite, 'observationMode': 'observed' if args.observe else 'control', 'warmupFrames': [c['warmupFrames'] for c in cases], 'measureFrames': [c['measureFrames'] for c in cases], 'regularTraces': len(cases), 'baseRuns': len(cases) * len(TARGETS) * args.rounds, 'pairedControls': args.paired_controls,
                          'processesByMode': dict(collections.Counter(run['observationMode'] for run in plan)), 'independentProcesses': len(plan), 'targets': TARGETS,
                          'seed': args.seed, 'profiles': sorted({row['qualityProfile'] for row in cases}),
                          'tagReuse': '1..255 wraps in serial distinct root processes', **capture_contract(args.capture_backend, args.observe or args.paired_controls), 'launches': 0}, indent=2))
        return
    output = args.output.resolve()
    require(not output.exists(), f'Use a fresh directory; existing raw/invalid records are preserved: {output}')
    commands = adapters(args)
    report_spec = importlib.util.spec_from_file_location('control_summary', HERE / 'report-control.py')
    reporter = importlib.util.module_from_spec(report_spec); report_spec.loader.exec_module(reporter)
    sizes = {name: reporter.package_bytes(command, name.removeprefix('remote-').removeprefix('local-')) for name, command in commands.items()}
    hashes, references = frozen_files(inputs, commands, args.observer.resolve(), args.capture_backend, args.observe or args.paired_controls, args.baseline_root)
    output.mkdir(parents=True)
    save(output / 'environment.json', {'protocolVersion': 11, **capture_contract(args.capture_backend, args.observe or args.paired_controls), 'seed': args.seed, 'startedUtc': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
        'publishedBytesBeforeRuns': sizes, 'cases': cases, 'targets': TARGETS, 'plannedRuns': len(plan), 'baseRuns': len(cases) * len(TARGETS) * args.rounds, 'pairedControls': args.paired_controls,
        'observationModes': ['observed', 'control'] if args.paired_controls else ['observed' if args.observe else 'control'], 'hashes': hashes, 'rendererProvenance': references,
        'observer': str(args.observer.resolve()), 'adapters': commands, 'metric': 'CPU whole discovered-tree lifetime; actual-time trapezoid memory; 120 producer points/119 intervals, not scanout; capture only diagnostic',
        'prepareBoundary': 'Square engine resources can be created lazily during Draw/Present; prepare buckets are not comparable native resource costs across frameworks.'})
    save(output / 'plan.json', plan)
    results = []
    by_id = {case['id']: case for case in cases}
    clean_env = dict(os.environ)
    for key in list(clean_env):
        if key.upper() in ('ELECTRON_RUN_AS_NODE', 'FORCE_RENDERER_ACCESSIBILITY', 'ELECTRON_FORCE_RENDERER_ACCESSIBILITY'):
            clean_env.pop(key)
    for run in plan:
        directory = output / run['directory']; directory.mkdir(parents=True)
        case = by_id[run['traceId']]
        launch = dict(commands[run['target']])
        launch['args'] = launch['args'] + ['--trace', str((Path(case['traceRoot']) / case['trace']).resolve()), '--output', str(directory / 'app.json'), '--tag', str(run['tag']), '--verify', '0']
        launch['environment'] = {}
        base_target = run['target'].removeprefix('remote-').removeprefix('local-')
        if base_target.startswith('square-'):
            launch['environment'].update(SQUARE_DIRECT2D_REQUIRE_HARDWARE='1', SQUARE_VULKAN_READBACK='0', SQUARE_VULKAN_VALIDATION='0')
        if run['target'] == 'electron':
            temp = directory / 'temp'; temp.mkdir()
            launch['environment'].update(TEMP=str(temp), TMP=str(temp))
        launch.update(expectedClientWidth=1280, expectedClientHeight=800, observeFrames=run['observationMode'] == 'observed', captureBackend=args.capture_backend)
        save(directory / 'launch.json', launch)
        command = [str(args.observer.resolve()), '--launch', str(directory / 'launch.json'), '--output', str(directory / 'observer.json'), '--tag', str(run['tag']), '--timeout-seconds', str(args.timeout_seconds)]
        print(f'[{run["index"]}/{len(plan)}] {run["directory"]}', flush=True)
        started = time.time(); errors = []; exit_code = None
        with (directory / 'stdout.log').open('wb') as stdout, (directory / 'stderr.log').open('wb') as stderr:
            try:
                # Direct files, never PIPE: the Observer and its owned app share these inherited handles.
                process = subprocess.run(command, cwd=REPO, env=clean_env, stdout=stdout, stderr=stderr, timeout=args.timeout_seconds + 45, check=False)
                exit_code = process.returncode
            except Exception as error:
                errors.append(f'outerRunner:{type(error).__name__}:{error}')
        try:
            app = load(directory / 'app.json'); obs = load(directory / 'observer.json')
            errors.extend(validate_run(app, obs, case, {**run, 'target': base_target}, exit_code, manifest['assetsSha256'], args.capture_backend))
        except Exception as error:
            errors.append(f'outputValidation:{type(error).__name__}:{error}'); app = {}; obs = {}
        sampling = obs.get('sampling') or {}
        record = {**run, **capture_contract(args.capture_backend, run['observationMode'] == 'observed'), 'observerExitCode': exit_code, 'elapsedSeconds': time.time() - started,
                  'valid': not errors, 'failures': errors, 'observationQualified': run['observationMode'] == 'observed' and not errors and sampling.get('badCoverage') is False and sampling.get('observerLimited') is False,
                  'observerLimited': sampling.get('observerLimited'), 'badCoverage': sampling.get('badCoverage'),
                  'observationReasons': sampling.get('reasons', []), 'qualityComparableDeclared': case['qualityComparable'],
                  'outputHashes': {name: digest(directory / name) for name in ('launch.json', 'app.json', 'observer.json', 'stdout.log', 'stderr.log') if (directory / name).is_file()}}
        save(directory / 'run.json', record); results.append(record); save(output / 'manifest.json', results)
        print(f'  valid={record["valid"]} observationQualified={record["observationQualified"]} {errors}', flush=True)
        if any(error.startswith('outerRunner:') for error in errors):
            # Observer normally performs owned-tree cleanup. If it itself times out/crashes, stop to avoid overlap.
            break
    changed = [row['path'] for row in hashes if not Path(row['path']).is_file() or digest(row['path']) != row['sha256']]
    save(output / 'completion.json', {'plannedRuns': len(plan), 'finishedRuns': len(results), 'validRuns': sum(row['valid'] for row in results),
        'observationQualifiedRuns': sum(row['observationQualified'] for row in results),
        'byObservationMode': {mode: {'planned': sum(row['observationMode'] == mode for row in plan),
            'finished': sum(row['observationMode'] == mode for row in results),
            'valid': sum(row['observationMode'] == mode and row['valid'] for row in results),
            'observationQualified': sum(row['observationMode'] == mode and row['observationQualified'] for row in results)} for mode in ('observed', 'control')},
        'hashesUnchanged': not changed, 'changedInputs': changed})
    metric_checks = []
    if not args.observe and not args.paired_controls:
        rows = reporter.report(output, commands)  # Preserve available partial results before failure.
        grouped = {(row['traceId'], row['target']): row for row in rows}
        for case in cases:
            for target in TARGETS:
                row = grouped.get((case['id'], target), {})
                counts = {key: row.get(key + '_n', 0) for key in ('CPU', 'FPS', 'avgPrivate', 'avgWS')}
                metric_checks.append({'traceId': case['id'], 'target': target, 'requestedN': args.rounds,
                                      'metricN': counts, 'complete': all(n == args.rounds for n in counts.values())})
    all_valid = len(results) == len(plan) and all(row['valid'] for row in results)
    metrics_complete = all(row['complete'] for row in metric_checks)
    completion = load(output / 'completion.json')
    completion.update(allMeasurementsValid=all_valid, requestedRounds=args.rounds,
                      metricCompleteness=metric_checks, coreMetricsComplete=metrics_complete,
                      complete=not changed and all_valid and metrics_complete,
                      publishNote='FDD/no full runtime may have PublishMiB=N/A; not a core measurement completeness failure')
    save(output / 'completion.json', completion)
    require(completion['complete'], 'Collection/metrics incomplete, invalid run or changed input; raw, completion and available partial summary retained')


if __name__ == '__main__':
    main()
