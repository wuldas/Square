"""Attribute Speedscope evented stack time inside the benchmark Draw call."""
import argparse
import json
from collections import Counter
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('trace', type=Path)
args = parser.parse_args()
data = json.loads(args.trace.read_text(encoding='utf-8-sig'))
frames = [entry['name'] for entry in data['shared']['frames']]
inclusive, exclusive = Counter(), Counter()
total = 0.0
for profile in data['profiles']:
    if profile['type'] != 'evented':
        raise SystemExit('Expected dotnet-trace evented Speedscope format')
    stack = []
    previous = profile['startValue']
    for event in profile['events']:
        delta = event['at'] - previous
        names = [frames[index] for index in stack]
        if delta > 0 and any('g__Draw|' in name for name in names):
            total += delta
            for name in set(names):
                inclusive[name] += delta
            if names:
                exclusive[names[-1]] += delta
        if event['type'] == 'O':
            stack.append(event['frame'])
        else:
            if not stack or stack.pop() != event['frame']:
                raise SystemExit('Unbalanced trace stack')
        previous = event['at']
result = {
    'trace': str(args.trace),
    'metric': 'sampled thread stack time inside Draw, includes native/wait boundaries; not hardware CPU cycles',
    'totalDrawStackTime': total,
    'inclusive': [{'name': name, 'percent': value / total * 100} for name, value in inclusive.most_common()] if total else [],
    'exclusive': [{'name': name, 'percent': value / total * 100} for name, value in exclusive.most_common()] if total else [],
}
output = args.trace.with_name(args.trace.stem + '-hotspots.json')
output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print('Draw stack sampled time:', round(total, 3))
for entry in result['inclusive']:
    if 'Square!' in entry['name'] or 'Square.Backends.Direct2D!' in entry['name']:
        print(f"{entry['percent']:6.2f}% {entry['name']}")
print('Saved', output)
