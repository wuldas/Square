"""Build a self-contained Chinese reading guide from the sealed v11 summary."""
from pathlib import Path
from collections import Counter
import hashlib, html, json, math
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[4]
OUT = ROOT / 'artifacts/canvas-optimization/run-v11-gdi-paired'
SOURCE = OUT / 'summary-v11.json'
summary = json.loads(SOURCE.read_bytes())
environment = json.loads((OUT / 'environment.json').read_bytes())
TARGETS = ['square-software','square-direct2d','square-vulkan','electron','flutter','avalonia']
NAMES = ['Square 软件渲染','Square Direct2D','Square Vulkan','Electron Canvas2D','Flutter Canvas','Avalonia']
COLORS = ['#cf5c42','#3975a7','#825bc1','#23877b','#2c9cbb','#aa7c26']
CASES = {
 'vector-reuse':('复用同一批图形','每帧画相同图形，沿用已创建的绘图对象。适合看稳定界面能从缓存中获得多少收益。'),
 'vector-recreate':('重建相同图形','画面内容保持相同，但每帧重新创建几何对象。可以看创建对象与准备绘制带来的额外开销。'),
 'vector-dynamic':('图形持续变化','每帧改变图形本身。可以看缓存较难直接复用时的调用路径成本。'),
 'paint-dynamic':('颜色与透明度变化','图形本身可复用，每帧改变颜色或透明度。'),
 'transform-dynamic':('位置与旋转变化','每帧改变变换矩阵，检查变换后的几何处理成本。'),
 'text-reuse':('复用相同文字','反复绘制相同文字，允许正常复用文字资源。字体文件字节相同。'),
 'text-dynamic':('文字持续变化','每帧改变固定长度的文字内容，包含当帧文字对象的准备成本。'),
 'image-grid':('大量图片：原尺寸','以整数位置按 1:1 绘制图片，包含透明像素。Square 软件渲染的图片透明混合未通过语义检查。'),
 'image-scaled':('放大图片：画质受限','图片放大两倍。各实现的默认滤波不同，因此不按同等画质排名；软件渲染还存在图片透明混合错误。'),
 'clip-layer':('不规则裁剪与透明图层','检查真实的几何裁剪，以及一组图形画完后整体应用透明度的成本。'),
 'fill-overdraw':('大面积反复覆盖','多次覆盖同一屏幕区域，包含透明混合，主要增加像素处理工作。'),
 'mixed-dashboard':('混合界面：画质受限','混合图形、文字、图片与渐变。渐变和抗锯齿存在原生质量差异；软件渲染还存在图片透明混合错误。'),
 'cache-pressure':('缓存容量压力','每帧固定画 128 条路径，资源池分别为 256、4096、65536 条。资源池越大，同一资源再次被使用的间隔越长。'),
 'stroke-native':('原生描边：画质受限','开放折线使用各实现默认描边。端点、连接和抗锯齿不完全一致，只作受限比较。'),
}
METRICS = {
 'frameCpuMeanMs':{'name':'平均每帧调用耗时','unit':'毫秒 / 帧','help':'画一帧时，调用线程准备对象、发出绘图指令并返回所花的墙钟时间，包含同步等待。越低表示此调用边界耗时越少。不同框架在后台继续绘制的工作量不同，因此不能据此宣布 GPU 谁最快，也不能用 1000 ÷ 此数值得到真实帧率。'},
 'frameCpuMedianMs':{'name':'常见帧耗时（P50）','unit':'毫秒 / 帧','help':'每轮中约一半的帧不超过这个调用耗时。这里先在每轮取中位数，再取三轮结果的中位数。包含同步等待，不是整条渲染流水线完成时间。'},
 'frameCpuP95Ms':{'name':'偏慢帧耗时（P95）','unit':'毫秒 / 帧','help':'每轮约 95% 的帧不超过这个调用耗时。它帮助观察偶发慢帧；先逐轮计算，再取三轮结果的中位数。'},
 'frameCpuP99Ms':{'name':'很慢帧耗时（P99）','unit':'毫秒 / 帧','help':'每轮约 99% 的帧不超过这个调用耗时。样本每轮 120 帧，P99 会更接近最慢的少数帧，不能把它当作长期稳定的上限。'},
 'treeWholeRunCpuLowerBoundMs':{'name':'实际进程 CPU 总时间','unit':'毫秒（完整生命周期）','help':'来自操作系统的进程树 CPU 核算，累加各线程真正消耗的 CPU 时间。包含启动、预热、测量、结束等待和退出；是采样下界，不能当作仅绘图的 CPU 时间。'},
 'treeWholeRunCpuPercentOneCore':{'name':'实际 CPU 使用强度','unit':'%（一个逻辑核 = 100%）','help':'完整进程生命周期的 CPU 时间除以运行时间。100% 约等于持续用满一个逻辑核，200% 约等于两个；它不是整台电脑 CPU 百分比。'},
 'peakTreePrivateMiB':{'name':'私有内存峰值','unit':'MiB（越低占用越少）','help':'完整进程树在运行期间采样到的私有内存峰值，包含资源池、运行时与框架开销。1 MiB 约等于 1.05 MB；这不是总显存，也可能漏掉采样之间的瞬时峰值。'},
}
controls = {(g['traceId'],g['target']):g for g in summary['groups'] if g['observationMode']=='control'}
observed = {(g['traceId'],g['target']):g for g in summary['groups'] if g['observationMode']=='observed'}
assert len(controls)==174 and len(observed)==174 and summary['validRuns']==1044
cases=[]
for c in environment['cases']:
 rows=[]
 for t,n,color in zip(TARGETS,NAMES,COLORS):
  g=controls[c['id'],t]; o=observed[c['id'],t]
  assert g['validN']==3 and g['plannedN']==3
  rows.append({'target':t,'name':n,'color':color,'quality':g['quality']['status'],'metrics':{m:g['metrics'][m] for m in METRICS},'sampledRate':o['metrics']['desktopObservedChangesPerSecond'],'observedCoverage':o['metrics']['visibleCoveragePercent'],'raw':f"r1-{c['id']}-{t}/control/run.json"})
 level={'low':'少量','high':'大量'}.get(str(c['level']),str(c['level'])+' 条资源')
 title,description=CASES[c['case']]
 cases.append({'id':c['id'],'family':c['case'],'name':title+' · '+level,'description':description,'level':level,'rows':rows,'warmup':c['warmupFrames'],'measured':c['measureFrames'],'operations':c['expectedMeasuredOpcodeTotals']})
quality=Counter(r['quality'] for c in cases for r in c['rows'])
assert quality=={'semantic-pass':136,'limited':38}
DATA={'cases':cases,'metrics':METRICS,'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'counts':{'processes':1044,'pairs':522,'caseLevels':29,'implementations':6,'rounds':3,'semanticPass':136,'limited':38,'qualifiedObserved':0}}
(OUT/'reading-guide-v11-zh-data.json').write_text(json.dumps(DATA,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

# A standalone shareable picture. Each panel has its own plainly labelled scale.
W,H=2100,1120
image=Image.new('RGB',(W,H),'#f1f5f9'); draw=ImageDraw.Draw(image)
font=lambda size,bold=False:ImageFont.truetype('C:/Windows/Fonts/msyhbd.ttc' if bold else 'C:/Windows/Fonts/msyh.ttc',size)
f44,f28,f24,f21,f18=font(44,True),font(28,True),font(24),font(21),font(18)
draw.text((54,32),'同样画一帧：复用、重建、变化的差别',font=f44,fill='#14263d')
draw.text((56,99),'单位：毫秒 / 帧；无捕获控制组；每轮帧平均耗时 → 三轮中位数。条越短，此调用边界耗时越少。',font=f24,fill='#41566f')
for panel,(cid,title) in enumerate([('vector-reuse-high','复用同一批图形'),('vector-recreate-high','每帧重建相同图形'),('vector-dynamic-high','每帧图形持续变化')]):
 c=next(c for c in cases if c['id']==cid); left=40+panel*690
 draw.rounded_rectangle((left,165,left+670,841),radius=22,fill='white')
 draw.text((left+25,188),title,font=f28,fill='#14263d')
 maximum=max(r['metrics']['frameCpuMeanMs']['max'] for r in c['rows'])*1.12
 px,pw=left+194,337
 draw.text((left+25,234),'大量图形 · 512 次绘制 / 帧',font=f21,fill='#52647a')
 for k in range(5):
  x=px+pw*k/4; v=maximum*k/4
  draw.line((x,285,x,755),fill='#e1e8f0',width=2)
  draw.text((x-10,768),f'{v:.1f}',font=f18,fill='#6a7d91')
 for i,r in enumerate(c['rows']):
  y=309+i*76; s=r['metrics']['frameCpuMeanMs']; end=px+pw*s['median']/maximum
  draw.text((left+18,y-5),r['name'].replace('Square ','').replace(' Canvas2D','').replace(' Canvas',''),font=f21,fill='#243b55')
  draw.rounded_rectangle((px,y,end if end>px+4 else px+4,y+25),radius=4,fill=r['color'])
  a,b=px+pw*s['min']/maximum,px+pw*s['max']/maximum
  draw.line((a,y+12,b,y+12),fill='#21364d',width=3); draw.line((a,y+6,a,y+18),fill='#21364d',width=2); draw.line((b,y+6,b,y+18),fill='#21364d',width=2)
  draw.text((left+551,y-5),f"{s['median']:.2f}",font=f24,fill='#14263d')
 draw.text((left+25,804),'各图横轴独立；细横线是三轮结果范围',font=f18,fill='#52647a')
draw.rounded_rectangle((40,870,2060,1080),radius=20,fill='#e6eef8')
draw.text((68,892),'怎样读这张图',font=f28,fill='#163e65')
for y,text in [(941,'软件渲染：复用约 1.87 ms，重建约 52.55 ms，持续变化约 54.91 ms。复用率会明显影响结果。'),(985,'这里统计调用路径的墙钟时间，包含等待；后台 GPU、合成器等工作可能尚未完成。'),(1028,'不能把这些数值换算成真实 FPS，也不能据此给 GPU 总排名。图片透明混合与原生画质限制另列。')]:
 draw.text((68,y),text,font=f24,fill='#264761')
image.save(OUT/'overview-v11-zh.png')

page=r'''<!doctype html>
<html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Canvas v11 中文图表解读</title>
<style>
:root{--ink:#15283e;--muted:#52677f;--line:#dce5ef;--brand:#225fa3;--amber:#a96514}*{box-sizing:border-box}body{margin:0;background:#f2f6fa;color:var(--ink);font:16px/1.75 "Microsoft YaHei","Segoe UI",sans-serif}main{max-width:1260px;margin:auto;padding:30px 26px 65px}h1{font-size:clamp(29px,4vw,43px);line-height:1.3;margin:12px 0 15px}h2{font-size:25px;margin:0 0 15px}h3{font-size:19px;margin:5px 0 10px}p{margin:10px 0}a{color:var(--brand)}nav{display:flex;gap:19px;flex-wrap:wrap;border-bottom:1px solid var(--line);padding:15px 0;margin-bottom:28px}nav a{text-decoration:none;font-weight:bold}.eyebrow{color:var(--brand);font-weight:bold}.lead{color:var(--muted);max-width:960px;font-size:18px}.grid{display:grid;grid-template-columns:repeat(3,1fr);gap:16px}.card,section{background:#fff;border:1px solid var(--line);border-radius:17px;padding:23px;margin-bottom:20px}.card{margin:0}.big{font-size:32px;font-weight:bold;line-height:1.3}.small{font-size:14px;color:var(--muted)}.note{background:#edf4fc;border-left:4px solid #3b79b8;padding:14px 18px;border-radius:7px}.warn{background:#fff6e8;border-left:4px solid #d39a37;padding:14px 18px;border-radius:7px}.muted{color:var(--muted)}.stats{margin:25px 0}.controls{display:grid;grid-template-columns:1.2fr 1fr;gap:18px}label{font-weight:bold;display:block}select{width:100%;font:inherit;color:var(--ink);padding:11px;border:1px solid #b7c9db;background:white;border-radius:9px}button{font:inherit;border:1px solid #b7c9db;background:#fff;color:var(--ink);border-radius:8px;padding:6px 12px;cursor:pointer}button[aria-pressed="true"]{background:var(--brand);color:#fff;border-color:var(--brand)}.buttons{display:flex;gap:8px;flex-wrap:wrap;margin:14px 0}.chart{width:100%;height:auto;display:block;min-width:660px}.scroll{overflow-x:auto}table{border-collapse:collapse;width:100%;font-size:14px}th,td{border-bottom:1px solid var(--line);padding:10px;text-align:left;white-space:nowrap}th{background:#f3f7fb}td.num{text-align:right;font-variant-numeric:tabular-nums}.badge{font-size:12px;padding:3px 9px;border-radius:99px;background:#e7f3ec;color:#276246;white-space:nowrap}.badge.limit{background:#fff0d8;color:#8a5912}.two{display:grid;grid-template-columns:1fr 1fr;gap:20px}dt{font-weight:bold;color:#234a73;margin-top:13px}dd{margin:3px 0 12px;color:var(--muted)}details{border-top:1px solid var(--line);margin-top:15px;padding-top:13px}summary{cursor:pointer;font-weight:bold}.overview{width:100%;height:auto;border-radius:10px}.formula{font-family:inherit;background:#eff4fa;padding:2px 7px;border-radius:4px}.test-info{color:var(--muted);font-size:14px}.rowhead{display:flex;align-items:center;justify-content:space-between;gap:15px}.table-title{font-size:16px;margin-top:18px}footer{font-size:13px;color:var(--muted)}.scope-grid{margin:15px 0}.scope-grid .card{padding:17px}.data-links{display:flex;gap:15px;flex-wrap:wrap}.key{color:#a13f29;font-weight:bold}.mono{font-variant-numeric:tabular-nums}@media(max-width:780px){main{padding:20px 15px}.grid,.two,.controls{grid-template-columns:1fr}section{padding:18px}.lead{font-size:16px}.stats .card{padding:15px}.big{font-size:27px}}
</style></head><body><main>
<div class="eyebrow">1280 × 800 · 6 个实现 · 3 轮独立进程 · 已完成实测</div>
<h1>Canvas 性能报告，怎样看才有用？</h1>
<p class="lead">先看每个场景下的调用耗时，再看 CPU、内存和画质。此页把原始报告的字段改成中文，保留原始数值；你可以切换场景与指标，并查看三次测试的波动范围。</p>
<nav aria-label="页面导航"><a href="#read-first">先读结论</a><a href="#all-cases">交互图表</a><a href="#dictionary">指标是什么意思</a><a href="#reliability">哪些结论可靠</a><a href="#evidence">原始数据</a></nav>
<div class="grid stats"><div class="card"><div class="small">测试进程全部完成</div><div class="big">1044 / 1044</div><div class="small">522 组配对；0 失败、0 缺失</div></div><div class="card"><div class="small">共同语义资格</div><div class="big">136 / 174 组</div><div class="small">另外 38 组受原生画质或图片错误限制</div></div><div class="card"><div class="small">精确帧率观测资格</div><div class="big">0 / 522 份</div><div class="small">本轮不依据外部采样给精确 FPS / GPU 排名</div></div></div>
<section id="read-first"><h2>先读这三点</h2><div class="grid"><div><h3>① 复用与变化差别很大</h3><p>Square 软件渲染在大量图形下，复用对象约 <b>1.87 ms</b>，重建对象约 <b>52.55 ms</b>，持续变化约 <b>54.91 ms</b>。判断性能时要结合你的界面会怎样变化。</p></div><div><h3>② 数字小，要看测量边界</h3><p>下图统计“发出这一帧绘图调用并返回”花了多久。有些框架把更多工作放到后台，因此数值小不等于 GPU 更快，也不等于整帧已显示。</p></div><div><h3>③ 画错不能算赢</h3><p>Square 软件渲染的半透明图片混合未通过检查。涉及图片的相关结果会带“画质受限”标记；原生描边、缩放滤波和渐变也单独标注。</p></div></div>
<div class="scroll"><img class="overview" src="overview-v11-zh.png" alt="复用、重建、持续变化三种场景的每帧调用耗时对比图，所有数值来自无捕获控制组"></div>
<p class="small">图中每个小图的横轴刻度独立；同一小图内可按条形长短比较。横线表示三个独立进程的结果范围，不是单帧最小值与最大值。</p></section>
<section id="all-cases"><h2>切换场景，看你真正关心的负载</h2><div class="controls"><div><label for="scenario">场景</label><select id="scenario"></select></div><div><label for="metric">查看指标</label><select id="metric"></select></div></div>
<div class="buttons" aria-label="常用场景"><button data-case="vector-reuse-high">复用对象</button><button data-case="vector-recreate-high">重建对象</button><button data-case="vector-dynamic-high">持续变化</button><button data-case="text-dynamic-high">动态文字</button><button data-case="image-grid-high">大量图片</button><button data-case="cache-pressure-65536">大资源池</button></div>
<h3 id="case-title"></h3><p id="case-description" class="muted"></p><p id="case-detail" class="test-info"></p>
<div id="metric-help" class="note"></div><div id="quality-warning" class="warn" style="margin-top:13px"></div>
<p class="small">默认使用无屏幕捕获的控制组。条形为三轮结果的中位数，细横线为三轮范围；本页没有综合总分。</p>
<div class="scroll"><svg id="comparison" class="chart" viewBox="0 0 1120 525" role="img" aria-labelledby="svg-title svg-description"></svg></div>
<h3 class="table-title">精确数字：中间值与三轮波动</h3><div class="scroll"><table><thead><tr><th>实现</th><th>三轮中间值</th><th>三轮最小—最大</th><th>画质资格</th><th>样本数</th></tr></thead><tbody id="values"></tbody></table></div>
<details><summary>这组的外部窗口采样为什么不能直接叫 FPS？</summary><p>桌面捕获约每 16.7 ms 才返回一次，可能漏见实际画过的序号，也有开始/结束边界误差。以下数字只是“被采样到的窗口变化率”，全部未达到本轮精确覆盖门槛。未观测序号也不能直接判为 GPU 掉帧。</p><div class="scroll"><table><thead><tr><th>实现</th><th>采样窗口变化 / 秒</th><th>帧序号观测覆盖</th><th>精确资格</th></tr></thead><tbody id="sampled"></tbody></table></div></details>
</section>
<section id="dictionary"><h2>指标是什么意思？</h2><div class="two"><dl><dt>平均每帧调用耗时：越小，调用路径越省时</dt><dd>单位毫秒（ms），1 ms = 0.001 秒，50 ms = 0.05 秒。包含准备对象、记录/提交绘制以及接口内部等待。它不是 GPU 完成时间。</dd><dt>常见帧 P50：看日常水平</dt><dd>每轮约一半的帧比它快、一半比它慢。它比单个最快帧更有代表性。</dd><dt>偏慢帧 P95 / 很慢帧 P99：看尾部波动</dt><dd>P95 代表每轮约 95% 的帧不超过该调用耗时，P99 对少数慢帧更敏感。每轮只有 120 个测量帧，别把尾部分位看作长期最坏上限。</dd><dt>三轮中间值与范围：看重现稳定程度</dt><dd>例如 3.00 [2.80, 4.20] 表示三轮结果的中位数为 3.00，三轮最低 2.80、最高 4.20。不是“误差 ± 某值”，也不是全部单帧的极值。</dd></dl><dl><dt>实际 CPU 时间：CPU 真正干了多少活</dt><dd>由操作系统 GetProcessTimes 统计，累加进程树所有线程。包括启动、预热、测量、结束及退出，和上面的调用路径墙钟时间含义不同；这里仍是进程采样下界。</dd><dt>CPU 使用强度：一个逻辑核等于 100%</dt><dd>100% 约等于持续用满一个逻辑核，200% 约等于两个。不能直接按“整台电脑用了多少百分比”来读。</dd><dt>私有内存峰值：观察资源占用</dt><dd>单位 MiB（1 MiB 约 1.05 MB）。包括进程、运行时和资源池；不含全部显存。采样之间的瞬时峰值可能漏掉。</dd><dt>为什么不能把 1000 ÷ 耗时当 FPS？</dt><dd>因为后台 GPU、合成器与窗口调度可能还在工作。60 Hz 屏幕每次刷新约 16.7 ms，但这些调用耗时没有覆盖完整显示流水线，因此不能直接判定真实帧率。</dd></dl></div></section>
<section id="reliability"><h2>哪些结论可靠，哪些需要保留？</h2><div class="two"><div><h3>这轮已经核验的内容</h3><p>同一份固定输入、1280×800 客户区、DPR=1；每个条件三轮独立进程，带捕获与无捕获各一组。总共 1044 份记录完整有效，源码与输入冻结哈希未变。</p><p>用预定义内部像素、透明混合、真实裁剪、矩阵与图层探针检查语义；使用同一字体文件字节。独立脚本又从原始数据复算统计、配对和文件哈希。</p><p>本轮列出的“语义通过”覆盖这些检查点，不能扩大为整个画面、所有抗锯齿边缘或文字光栅结果逐像素相同。</p></div><div><h3>必须带着看的限制</h3><p><span class="badge limit">软件图片透明混合错误</span> 软件渲染的图片探针最大通道误差 111（通道范围 0–255）；含图片的相关场景受限。</p><p><span class="badge limit">原生画质不同</span> 描边端点/连接、图像放大滤波、渐变与抗锯齿不完全一致；相关组不按等质量排名。</p><p><span class="badge limit">帧率观测未达标</span> GDI 与桌面复制的实际校准限制已保留；这轮不能给精确 GPU 吞吐、物理显示帧率或“全场景冠军”。</p></div></div></section>
<section id="evidence"><h2>想核对时，再打开原始资料</h2><p>此页只重新组织和翻译已完成报告的数据，没有重跑测试或修改原始样本。每个条件都显示三轮范围，避免只挑最快结果。</p><div class="data-links"><a href="report-v11.html">原始详细报告</a><a href="summary-v11.csv">分组数据 CSV</a><a href="pairs-v11.csv">配对数据 CSV</a><a href="qc-v11-final.json">独立复核记录</a><a href="summary-v11.json">完整机器可读数据</a><a href="reading-guide-v11-zh-data.json">本页图表数据</a><a href="overview-v11-zh.png">下载关键图（PNG）</a></div><details><summary>统计方法与来源校验</summary><p>调用方耗时在每轮独立计算平均值、P50、P95、P99；P95/P99 在排序后的 p×(n−1) 位置线性插值。然后将三轮统计汇总为中位数、最小与最大，不把三轮帧混成一组。Observer 的采样分位采用其原始最近秩方法，二者不混用。</p><p class="small">图表数据源 SHA-256：<span id="source-sha"></span></p></details></section>
<footer>Canvas v11 中文解读版 · 2026 年 10 月 30 日 · 同一 Windows 测试机器的结果，不外推为所有设备、场景和 GPU 完成时间的排名。</footer>
<script id="chart-data" type="application/json">__DATA__</script>
<script>
'use strict';
const DATA=JSON.parse(document.querySelector('#chart-data').textContent), $=id=>document.getElementById(id);
const fmt=v=>v==null?'暂无数据':Number(v).toLocaleString('zh-CN',{minimumFractionDigits:2,maximumFractionDigits:2});
const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const priority=['vector-reuse','vector-recreate','vector-dynamic','paint-dynamic','transform-dynamic','text-reuse','text-dynamic','image-grid','image-scaled','clip-layer','fill-overdraw','mixed-dashboard','cache-pressure','stroke-native'];
const ordered=[...DATA.cases].sort((a,b)=>priority.indexOf(a.family)-priority.indexOf(b.family)||a.id.localeCompare(b.id));
$('scenario').innerHTML=ordered.map(c=>`<option value="${esc(c.id)}">${esc(c.name)}</option>`).join('');
$('metric').innerHTML=Object.entries(DATA.metrics).map(([key,m])=>`<option value="${key}">${esc(m.name)}</option>`).join('');
$('scenario').value='vector-dynamic-high'; $('metric').value='frameCpuMeanMs';
$('source-sha').textContent=DATA.sourceSha256;
function render(){
 const c=DATA.cases.find(c=>c.id===$('scenario').value), key=$('metric').value, metric=DATA.metrics[key];
 $('case-title').textContent=c.name; $('case-description').textContent=c.description;
 const draws=Object.entries(c.operations).filter(([k])=>!['transform','end-transform','clip','clip-new','end-clip','layer','end-layer'].includes(k)).reduce((s,[,v])=>s+v,0)/c.measured;
 $('case-detail').textContent=`每轮：预热 ${c.warmup} 帧，测量 ${c.measured} 帧；每帧 ${Math.round(draws)} 次内容绘制；图中取无捕获控制组，共 3 轮。`;
 $('metric-help').textContent=metric.help;
 const limited=c.rows.filter(r=>r.quality!=='semantic-pass');
 $('quality-warning').hidden=!limited.length;
 $('quality-warning').textContent=limited.length?`画质受限：${limited.map(r=>r.name).join('、')}。保留数字供诊断，不与已通过场景作等质量性能结论。${c.rows.some(r=>r.target==='square-software'&&r.quality==='limited')&&['image-grid','image-scaled','mixed-dashboard'].includes(c.family)?'Square 软件渲染还存在已证实的图片透明混合错误。':''}`:'';
 const x0=228,plot=630, max=Math.max(...c.rows.map(r=>r.metrics[key].max||0))*1.13||1, sx=v=>x0+v/max*plot;
 let svg=`<title id="svg-title">${esc(c.name)}：${esc(metric.name)}</title><desc id="svg-description">${esc(metric.help)}。各条中间值和三轮范围也见下方表格。</desc><text x="18" y="28" font-size="17" font-family="Microsoft YaHei, sans-serif" fill="#243d59">${esc(metric.unit)} · 数值越低越少 · 细线为三轮范围</text>`;
 for(let i=0;i<=5;i++){const value=max*i/5,x=sx(value);svg+=`<path d="M${x},52V474" stroke="#e0e7f0"/><text x="${x}" y="501" text-anchor="middle" font-family="Microsoft YaHei,sans-serif" font-size="13" fill="#52677f">${fmt(value)}</text>`;}
 c.rows.forEach((r,i)=>{const y=86+i*67,s=r.metrics[key],color=r.quality==='semantic-pass'?r.color:'#b88439';svg+=`<text x="18" y="${y+6}" font-family="Microsoft YaHei,sans-serif" font-size="17" fill="#243d59">${esc(r.name)}</text>`;if(s.n){const a=sx(s.min),b=sx(s.max),m=sx(s.median);svg+=`<rect x="${x0}" y="${y-12}" width="${Math.max(4,m-x0)}" height="25" rx="4" fill="${color}"/><path d="M${a},${y}H${b}M${a},${y-7}V${y+7}M${b},${y-7}V${y+7}" stroke="#20364e" stroke-width="2"/><circle cx="${m}" cy="${y}" r="4" fill="#20364e"/><text x="886" y="${y+6}" font-family="Microsoft YaHei,sans-serif" font-size="19" fill="#20364e">${fmt(s.median)}</text>`;}else svg+=`<text x="${x0}" y="${y+5}" fill="#64758a">暂无数据</text>`;svg+=`<text x="${x0}" y="${y+32}" font-family="Microsoft YaHei,sans-serif" font-size="12" fill="#64758a">三轮范围：${fmt(s.min)}—${fmt(s.max)}${r.quality==='semantic-pass'?'':' · 画质受限'}</text>`;});
 $('comparison').innerHTML=svg;
 $('values').innerHTML=c.rows.map(r=>{const s=r.metrics[key],ok=r.quality==='semantic-pass';return `<tr><td>${esc(r.name)}</td><td class="num">${fmt(s.median)}</td><td class="num">${fmt(s.min)}—${fmt(s.max)}</td><td><span class="badge ${ok?'':'limit'}">${ok?'语义检查通过':'画质受限'}</span></td><td>${s.n} / 3</td></tr>`;}).join('');
 $('sampled').innerHTML=c.rows.map(r=>`<tr><td>${esc(r.name)}</td><td class="num">${fmt(r.sampledRate.median)}</td><td class="num">${fmt(r.observedCoverage.median)}%</td><td><span class="badge limit">未达到门槛</span></td></tr>`).join('');
 document.querySelectorAll('[data-case]').forEach(b=>b.setAttribute('aria-pressed',String(b.dataset.case===c.id)));
}
$('scenario').addEventListener('change',render);$('metric').addEventListener('change',render);
document.querySelectorAll('[data-case]').forEach(b=>b.addEventListener('click',()=>{$('scenario').value=b.dataset.case;render();}));
render();
</script></main></body></html>'''
page=page.replace('__DATA__',json.dumps(DATA,ensure_ascii=False,separators=(',',':')).replace('<','\\u003c'))
(OUT/'report-v11-zh.html').write_text(page,encoding='utf-8')
print(json.dumps({'html':str(OUT/'report-v11-zh.html'),'image':str(OUT/'overview-v11-zh.png'),'caseLevels':len(cases),'rows':sum(len(c['rows']) for c in cases),'quality':dict(quality),'sourceSha256':DATA['sourceSha256']},ensure_ascii=False,indent=2))
