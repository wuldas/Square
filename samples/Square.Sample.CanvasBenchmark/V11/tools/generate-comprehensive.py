"""One comprehensive input; the legacy generator and 32 inputs stay byte-identical."""
from collections import Counter
import hashlib
import json
from pathlib import Path
import shutil

V11 = Path(__file__).resolve().parents[1]
ROOT = V11 / 'Comprehensive'
DRAW = {'poly', 'poly-new', 'stroke', 'stroke-new', 'rect', 'text', 'text-new', 'image', 'gradient'}

def canonical(value): return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False).encode('utf-8')
def digest(data): return hashlib.sha256(data).hexdigest()
def load(path): return json.loads(path.read_text(encoding='utf-8-sig'))
def color(index, frame):
    value = (0x1234ABCD ^ index) & 0xffffffff
    value ^= value >> 16; value = value * 0x7FEB352D & 0xffffffff
    value ^= value >> 15; value = value * 0x846CA68B & 0xffffffff; value ^= value >> 16
    return f'#{((value & 0xffffff) + frame * 0x010307) & 0xffffff:06X}FF'

def generate():
    old = load(V11 / 'Traces/manifest.json')
    row = next(x for x in old['benchmarkTraces'] if x['id'] == 'mixed-dashboard-high')
    source = V11 / 'Traces' / row['trace']
    assert digest(source.read_bytes()) == row['traceSha256']
    trace = load(source)
    trace.update(case='comprehensive', level='default', qualityComparable=False,
                 qualityProfile='comprehensive-native-gradient-stroke')
    for frame_index, frame in enumerate(trace['frames']):
        for op in frame['ops']:
            if op[0] == 'poly': op[2] = color(op[1], frame_index)
        for panel in range(4):
            frame['ops'].extend([
                ['transform', 1, frame_index / 4096, -frame_index / 4096, 1, frame_index / 16, 0],
                ['poly', panel * 2, color(panel + 8, frame_index)], ['end-transform'],
                ['stroke', panel * 2, color(panel + 16, frame_index), 1.5]])
    measured = trace['frames'][60:]
    totals = dict(sorted(Counter(op[0] for f in measured for op in f['ops']).items()))
    hashes = [digest(canonical(f['ops'])) for f in trace['frames']]
    assert len(hashes) == len(set(hashes)) == 180
    trace['expectedMeasuredOpcodeTotals'] = totals
    trace['drawsPerFrame'] = sum(op[0] in DRAW for op in trace['frames'][0]['ops'])
    trace['logicalInputsSha256'] = digest(canonical({k: trace[k] for k in ('paths', 'texts', 'frames')}))
    trace['metadata']['frameLogicalInputsSha256'] = hashes
    trace['workload'].update(dynamicFields=['geometry', 'text', 'paint', 'transform'],
                             description='Four original dashboard panels plus missing transform/stroke; one fixed load, no pressure loop')
    trace['inputAssertions'].update(frameCount=180, uniqueFrameInputs=180,
                                   dynamicFrameInputsMustBeUnique=True, warmupMeasurementDynamicInputsDisjoint=True)
    trace['qualityLimitations'] = ['Native stroke cap/join and gradient interpolation/AA are not pixel-equal across backends',
                                   'Clip/group alpha requires fresh semantic evidence; timing does not establish equivalence']
    folder = ROOT / 'comprehensive-default'; folder.mkdir(parents=True, exist_ok=True)
    for asset, expected in old['assetsSha256'].items():
        assert digest((source.parent / asset).read_bytes()) == expected
        shutil.copyfile(source.parent / asset, folder / asset)
    payload = canonical(trace); (folder / 'trace.json').write_bytes(payload)
    item = {**row, 'id': 'comprehensive-default', 'case': 'comprehensive', 'level': 'default',
            'trace': 'comprehensive-default/trace.json', 'traceSha256': digest(payload), 'bytes': len(payload),
            'drawsPerFrame': trace['drawsPerFrame'], 'expectedMeasuredOpcodeTotals': totals,
            'logicalInputsSha256': trace['logicalInputsSha256'], 'inputAssertions': trace['inputAssertions'],
            'qualityComparable': False, 'qualityProfile': trace['qualityProfile']}
    manifest = {'protocolVersion': 11, 'complete': True, 'width': 1280, 'height': 800, 'devicePixelRatio': 1,
                'traceCount': 1, 'schema': 'schema.json', 'semanticFixtures': [], 'calibrationTraces': [], 'benchmarkTraces': [item], 'assetsSha256': old['assetsSha256'],
                'schemaSha256': digest((V11 / 'Traces/schema.json').read_bytes()),
                'generatorSha256': digest(Path(__file__).read_bytes()),
                'sourceTrace': str(source.relative_to(V11)), 'sourceTraceSha256': row['traceSha256'],
                'sourceGeneratorSha256': old['generatorSha256'],
                'qualityLimitations': trace['qualityLimitations']}
    shutil.copyfile(V11 / 'Traces/schema.json', ROOT / 'schema.json')
    (ROOT / 'manifest.json').write_bytes(canonical(manifest))
    return manifest

if __name__ == '__main__':
    print(json.dumps(generate(), ensure_ascii=False, indent=2))
