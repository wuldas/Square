"""Serial v11 integration/observation preflight; never mixed with performance samples."""
from pathlib import Path
import argparse,hashlib,json,os,subprocess

BASE=Path(__file__).resolve().parents[1]; REPO=BASE.parents[2]
PYROOT=REPO/'artifacts/canvas-optimization/preflight-v11'
FRAMEWORKS=('Software','Direct2D','Vulkan','Electron','Flutter','Avalonia')

def command(framework,trace,output,tag,verify):
 args=['--trace',str(trace),'--output',str(output),'--tag',str(tag),'--verify',str(int(verify))]
 if framework in FRAMEWORKS[:3]: return [str(BASE/'Square/bin/Release/net10.0/CanvasBench.SquareV11.exe'),'--backend',framework,*args]
 if framework=='Electron': return [str(BASE.parent/'Electron/.runtime/electron.exe'),'--no-sandbox',str(BASE/'Electron'),*args]
 if framework=='Flutter': return [str(BASE/'Flutter/build/windows/x64/runner/Release/square_canvas_v11.exe'),*args]
 return [str(BASE/'Avalonia/bin/Release/net10.0/CanvasBenchmark.V11.Avalonia.exe'),*args]

def main():
 p=argparse.ArgumentParser(); p.add_argument('--mode',choices=('semantic','calibration','control'),required=True); p.add_argument('--frameworks',nargs='+',choices=FRAMEWORKS,default=list(FRAMEWORKS)); p.add_argument('--suffix',required=True); p.add_argument('--trace',type=Path); p.add_argument('--capture-backend',choices=('desktop-duplication','gdi'),default='desktop-duplication'); opt=p.parse_args()
 run=PYROOT/(opt.mode+'-'+opt.suffix)
 if run.exists(): raise RuntimeError('Refusing to overwrite integration evidence')
 run.mkdir(parents=True)
 trace=opt.trace or BASE/'Traces'/('semantic-fixture-core' if opt.mode=='semantic' else 'calibration-marker-only')/'trace.json'
 env=os.environ.copy()
 for key in ('ELECTRON_RUN_AS_NODE','ELECTRON_FORCE_RENDERER_ACCESSIBILITY'): env.pop(key,None)
 results=[]
 for index,framework in enumerate(opt.frameworks):
  tag=41+index; app=run/(framework+'.json'); cmd=command(framework,trace.resolve(),app.resolve(),tag,opt.mode=='semantic')
  if not Path(cmd[0]).is_file(): raise RuntimeError(f'Missing executable {cmd[0]}')
  observed=None
  if opt.mode in ('calibration','control'):
   launch=run/(framework+'.launch.json'); observed=run/(framework+'.observer.json')
   launch.write_text(json.dumps({'fileName':cmd[0],'args':cmd[1:],'workingDirectory':str(REPO),'environment':{},'expectedClientWidth':1280,'expectedClientHeight':800,'observeFrames':opt.mode=='calibration','captureBackend':opt.capture_backend}),encoding='utf-8')
   cmd=[str(BASE/'Observer/bin/Release/net10.0/Observer.exe'),'--launch',str(launch.resolve()),'--output',str(observed.resolve()),'--tag',str(tag),'--timeout-seconds','180']
  with (run/(framework+'.stdout.log')).open('wb') as stdout,(run/(framework+'.stderr.log')).open('wb') as stderr:
   proc=subprocess.Popen(cmd,cwd=REPO,env=env,stdout=stdout,stderr=stderr)
   try: code=proc.wait(timeout=210)
   except subprocess.TimeoutExpired:
    proc.kill(); proc.wait(); raise RuntimeError(f'{framework} integration timeout')
  row={'framework':framework,'exitCode':code,'app':str(app),'observer':str(observed) if observed else None}
  if app.exists():
   data=json.loads(app.read_text(encoding='utf-8-sig')); row.update({'width':data.get('clientWidth'),'height':data.get('clientHeight'),'dpr':data.get('devicePixelRatio'),'measureFrames':data.get('measureFrames'),'actualCounts':data.get('actualMeasuredOpcodeTotals')})
  if observed and observed.exists():
   info=json.loads(observed.read_text(encoding='utf-8-sig')); row['observerStatus']=info.get('status')
   marker=info.get('marker') or {}; sampling=info.get('sampling') or {}
   row['marker']={k:marker.get(k) for k in ('desktopObservedChangesPerSecond','uniqueMeasuredCount','measurementDurationMs','missingMeasuredSequences')}
   row['sampling']={k:sampling.get(k) for k in ('enabled','observerLimited','badCoverage','measurementGapMs')}
  results.append(row); print(json.dumps(row,ensure_ascii=False),flush=True)
  (run/'manifest.json').write_text(json.dumps(results,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
  if code: raise RuntimeError(f'{framework} integration exit {code}; retained logs')
 print('Completed serial integration: '+str(run))

if __name__=='__main__': main()
