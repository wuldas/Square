"""Validate actual v11 semantic screenshots without changing predefined probes or tolerances."""
from pathlib import Path
import hashlib,importlib.util,json
from PIL import Image
BASE=Path(__file__).resolve().parents[1]; REPO=BASE.parents[2]; ART=REPO/'artifacts/canvas-optimization/preflight-v11'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
module_spec=importlib.util.spec_from_file_location('v11inputs',BASE/'tools/validate-traces.py'); validator=importlib.util.module_from_spec(module_spec); module_spec.loader.exec_module(validator)
trace_file=BASE/'Traces/semantic-fixture-core/trace.json'; trace=json.loads(trace_file.read_bytes()); folder=ART/'semantic-final-six4'; dest=ART/'semantic-qualified-images-final'
if dest.exists(): raise RuntimeError('Preserve existing quality evidence')
dest.mkdir()
rows={}
for framework in ('Software','Direct2D','Vulkan','Electron','Flutter','Avalonia'):
 app=folder/(framework+'.json'); result=json.loads(app.read_bytes())
 candidates=sorted(p for p in folder.glob(framework+'.*') if p.suffix.lower() in ('.bmp','.png') and ('frame0' in p.name or 'frame0000' in p.name))
 if len(candidates)!=1: raise RuntimeError(f'{framework}: expected one seq0 image, got {candidates}')
 source=candidates[0]; png=dest/(framework+'.png'); Image.open(source).convert('RGB').save(png)
 row=validator.validate_png(trace,png)
 row['producer']={'path':str(app.relative_to(REPO)),'sha256':sha(app),'traceHashMatches':result.get('traceSha256','').lower()==sha(trace_file),'opcodeCountsMatch':result.get('actualMeasuredOpcodeTotals')==trace['expectedMeasuredOpcodeTotals'],'dimensionsMatch':(result.get('clientWidth'),result.get('clientHeight'),result.get('devicePixelRatio'))==(1280,800,1),'framesMatch':(result.get('warmupFrames'),result.get('measureFrames'))==(trace['warmupFrames'],trace['measureFrames']),'timingEligible':result.get('timingEligible')}
 image=Image.open(png).convert('RGB'); bits=[]
 for i in range(48):
  rgb=image.getpixel((2*i+1,4)); bits.append(1 if min(rgb)>=200 else 0 if max(rgb)<=40 else -1)
 sync=sum(bit<<i for i,bit in enumerate(bits[:8])) if -1 not in bits else -1
 payload=sum(bits[8+i]<<i for i in range(32)) if -1 not in bits else 0
 crc=0
 for b in payload.to_bytes(4,'little'):
  crc^=b
  for _ in range(8): crc=((crc<<1)^7)&255 if crc&128 else (crc<<1)&255
 actual_crc=sum(bits[40+i]<<i for i in range(8)) if -1 not in bits else -1
 row['markerSource']={'sync':sync,'payload':payload,'crcMatches':crc==actual_crc,'tag':(payload>>18)&255,'phase':(payload>>16)&3,'sequence':payload&65535,'matchesRun':sync==165 and crc==actual_crc and ((payload>>18)&255)==result['tag'] and ((payload>>16)&3)==2 and (payload&65535)==0}
 groups={name:all(p['passed'] for p in row['probes'] if p['category']==name) for name in ('interiorProbes','alphaProbes','groupProbes','imageProbes')}
 font_hash=trace['assets']['font']['sha256']; row['fontBytesHashDeclared']=font_hash.lower() in json.dumps(result).lower()
 row['capabilities']={'geometryAndClip':groups['interiorProbes'],'sourceOverAlpha':groups['alphaProbes'],'isolatedGroupOpacity':groups['groupProbes'],'imageIdentityAlpha':groups['imageProbes'],'textNonemptyAndFontBytes':all(t['passed'] for t in row['textRegions']) and row['fontBytesHashDeclared']}
 common=all(row['producer'][k] for k in ('traceHashMatches','opcodeCountsMatch','dimensionsMatch','framesMatch')) and row['markerSource']['matchesRun']
 row['inputAndSourceValid']=common
 cap=row['capabilities']; all_core=all(cap.values())
 row['profileQualification']={
  'vector-reuse':common and cap['geometryAndClip'], 'vector-recreate':common and cap['geometryAndClip'], 'vector-dynamic':common and cap['geometryAndClip'],
  'paint-dynamic':common and cap['geometryAndClip'] and cap['sourceOverAlpha'], 'transform-dynamic':common and cap['geometryAndClip'],
  'text-reuse':common and cap['textNonemptyAndFontBytes'], 'text-dynamic':common and cap['textNonemptyAndFontBytes'],
  'image-grid':common and cap['imageIdentityAlpha'], 'clip-layer':common and cap['geometryAndClip'] and cap['isolatedGroupOpacity'],
  'fill-overdraw':common and cap['sourceOverAlpha'], 'cache-pressure':common and cap['geometryAndClip'],
  'mixed-dashboard':False, 'stroke-native':False, 'image-scaled':False}
 row['nativeQualityProfiles']={'mixed-dashboard':{'semanticCoreSupported':common and all_core,'fixedQualityComparable':False,'reason':'Native gradient interpolation and AA are separate quality regions; no identical-gradient qualification claimed.'},'stroke-native':{'fixedQualityComparable':False,'reason':'Default caps/joins/AA differ and Square Software stroke style support is limited.'},'image-scaled':{'fixedQualityComparable':False,'reason':'Square public API fixes nearest on Software and linear on Direct2D/Vulkan; scaled native sampling is explicitly separate.'}}
 row['limitation']='Actual image-alpha semantic probe failed; image-grid and mixed/scaled image output must be reported restricted.' if not cap['imageIdentityAlpha'] else ''
 rows[framework]=row
 print(framework,'sourceValid',common,'capabilities',cap,'failedProbes',[p['name'] for p in row['probes'] if not p['passed']],flush=True)
entries=[]
targets={'Software':'square-software','Direct2D':'square-direct2d','Vulkan':'square-vulkan','Electron':'electron','Flutter':'flutter','Avalonia':'avalonia'}
for framework,row in rows.items():
 for case,passed in row['profileQualification'].items():
  reason=row['nativeQualityProfiles'].get(case,{}).get('reason') or (row['limitation'] if not passed else '')
  entries.append({'target':targets[framework],'cases':[case],'status':'pass' if passed else 'limited','reasons':[reason] if reason else []})
report={'protocolVersion':11,'entries':entries,'traceSha256':sha(trace_file),'predefinedProbePolicy':'Frozen analytical 5x5 interior/background, source-over and group probes; no tolerance adjusted after renderer observations. All semantic checks retained, including failures.','allSourcesValid':all(r['inputAndSourceValid'] for r in rows.values()),'frameworks':rows,'qualityScope':'Interior semantic fixtures qualify opcode families; AA edges and exact text rasterization are not proven identical. Native gradient/stroke/scaled-sampler profiles remain separate.','sourceUnchanged':'Renderer sources are checked separately against v8/v9/v10 snapshots; only benchmark tools are changed.'}
(ART/'quality-verification-v11.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
if not report['allSourcesValid']: raise RuntimeError('At least one screenshot/input source does not match the intended run')
