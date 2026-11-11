# Canvas v11 公平测试协议

状态：实现规范；任何变更须在正式采样前定稿。旧v1–v10保留，不混入本轮成绩。

## 统一边界

- 参与方：Square Software、Square Direct2D、Square Vulkan、Electron Canvas2D、Flutter Canvas、Avalonia DrawingContext。
- 客户区与画布1280×800物理像素，DPR=1；系统标题栏在画布之外。底色#101827FF，前16像素高保留外部帧标记。画面不做CSS或窗口缩放。
- 不修改渲染引擎以迎合新输入。允许默认的通用资源缓存；每帧仍执行trace全部逻辑绘制，禁止在适配器中缓存整帧/整个场景display list或省略重复source-over。
- 主试验按固定逻辑帧序列执行，不按墙钟挑相位、不跳帧。常规warmup=60帧、measure=120帧。新几何/变换/颜色/文字使用不重复的预生成输入，warmup与measurement输入不循环。
- trace JSON只存逻辑输入，解析在计时前；静态资源可预创建，poly-new/stroke-new/text-new必须逐帧创建原生对象，创建包含在每帧CPU记录中。资产加载、静态准备、首帧、每帧native prepare与record/submit分别记录。静态、同值重建、连续变更三者分开。
- 每个case/level/框架3轮；每轮分别使用独立进程执行observed与无捕获control，并轮换两者先后。29档×6方×3轮×2模式=1044进程；不把control缺失时的captured CPU冒充无扰动成本。框架与case顺序按固定seed轮换，串行运行。保留所有有效慢样本及失败，不择优、不计算一个混合总分。

## trace结构（所有适配器读同一文件）

```json
{"protocolVersion":11,"case":"vector-reuse","level":"low","width":1280,"height":800,"seed":305441741,"warmupFrames":60,"measureFrames":120,"paths":[[[0,0],[8,0],[8,8],[0,8]]],"texts":[{"text":"Canvas 0123","size":14}],"frames":[{"ops":[["poly",0,"#E08040FF"]]}],"expectedMeasuredOpcodeTotals":{}}
```

frames长度=warmupFrames+measureFrames，禁止模数回放。资产image.png和font.ttf在trace同目录；字体使用本机Segoe UI字体文件的相同字节，显式注册或报告不支持，禁止默默换字体。所有数字按1/16像素或1/4096矩阵量化，输入文件SHA256写入每份结果。

操作数组：

- `["poly",pathId,rgba]`：闭合简单多边形，静态原生path资产。
- `["poly-new",points,rgba]`：同样填充，但每帧新建原生path。点为`[[x,y],...]`，最后close；不使用洞或自交来混淆winding规则。
- `["stroke",pathId,rgba,width]` / `["stroke-new",points,rgba,width]`：开放折线，native默认端点/接头；该profile单列画质差异，不伪称与fill profile同固定质量。
- `["rect",x,y,w,h,rgba]`。
- `["text",textId,x,y,rgba]`：同一字体、14px默认、top-left原点；可预备静态布局，仍每帧绘制。
- `["text-new",text,size,x,y,rgba]`：每帧创建布局/paragraph，禁止按有限帧池回放。
- `["image",x,y,w,h]`：主image-grid/mixed使用image.png全部源区域32×32→32×32整数1:1，避免Square Software固定nearest与D2D/Vulkan固定linear而公共API无法选择的差异。独立image-scaled 2×保留各原生采样，samplingProfile=native-scaled、qualityComparable=false，按nearest/linear披露；不将其作为六方固定质量排名。
- `["transform",m11,m12,m21,m22,dx,dy]` / `["end-transform"]`：push当前矩阵后按相同乘法应用/恢复。
- `["clip",pathId]` / `["clip-new",points]` / `["end-clip"]`：真实几何clip；不得用bounding rectangle代替。
- `["layer",x,y,w,h,opacity]` / `["end-layer"]`：分组source-over透明度。主trace中layer在顶层且不嵌套，内部允许clip/transform。Electron使用有界离屏canvas且每次clear+重绘该group，不能用globalAlpha逐child冒充分组。
- `["gradient",x,y,w,h,x0,y0,x1,y1,rgba0,rgba1]`：线性pad，native插值差异单列并由图像门槛检查。

层/clip/transform按LIFO嵌套。rgba为#RRGGBBAA。每个adapter记录measurement的实际opcode次数，和trace期望完全一致；marker不计入这些次数。

## 场景矩阵

vector-reuse、vector-recreate、vector-dynamic、paint-dynamic、transform-dynamic、text-reuse、text-dynamic、image-grid、clip-layer、fill-overdraw、cache-pressure、mixed-dashboard；另stroke-native默认质量profile单列。通常low/high两档，压力使用256/4096/65536路径三档（每帧固定128条，warmup覆盖全部pool两轮，测量120帧）。负载用更多独立对象/像素覆盖而不是把整幅相同场景重复N次。

## 通用外部观测（主指标）

在canvas左上角每次绘制后画96×8不透明黑白标记，每cell宽2高8，中心采样，LSB在前：8bit同步字0xA5 + payload32bit + CRC8。payload=`(tag&255)<<18 | (phase&3)<<16 | (seq&65535)`；phase=1 warmup、2 measurement、3 end；tag由runner传入，每个run不同。CRC对payload的4个little-endian字节，初值0，多项式0x07，逐byte xor再8次左移。每个框架全部绘制后画marker，最后才submit/present。

外部runner对自有PID窗口置顶且不抢键盘焦点，捕获相同桌面marker区域，记录checksum通过的唯一帧ID和时间、失败率和采样耗时。phase2进入到phase3首次可见为同一外部阶段边界，记录桌面合成中可见的唯一帧、漏见/合并的序号、间隔分布。它是composited-desktop observation，**不是物理scanout或GPU kernel time**；采样的分辨率与观察开销明确报告。主报告不混用rAF、Control.Render、Flutter rasterFinish与Vulkan fence作同一FPS。

额外记录各框架自己的CPU/API/recording/raster/fence事件，并明确各自边界，仅作诊断。进程树CPU和private bytes由外部统一采样，Electron须计main/renderer/GPU子进程。observer自身CPU/捕获耗时单列；marker-only校准与无observer对照用于识别扰动。接近60Hz只能说明在本显示调度上达到上限，不能推断未限帧极限吞吐。

## 每个程序CLI与输出

`--trace <absolute json> --output <absolute json> --tag <1..255> [--backend Software|Direct2D|Vulkan] [--verify 0|1]`，全部成对参数。结束measurement后画phase3/seq=measureFrames，保留最后画面750ms，再写JSON并正常退出。verify=1只执行指定trace的短验证帧，允许截图/readback，禁止把这次时间混入性能数据。可以保留启动到assetsReady/first draw的独立时间；首个可见marker由外部观察。

输出至少：protocolVersion=11/framework/backend/traceSha256/clientWidth/clientHeight/devicePixelRatio/tag/warmupFrames/measureFrames、assetLoadMs/staticPrepareMs、measurement frameCpu数组（每项nativePrepareMs、recordSubmitMs、presentMs、totalMs）、producer帧时间、actualMeasuredOpcodeTotals、versions及硬件状态；Vulkan fence/Flutter FrameTiming等放单独diagnostics字段。不能把程序写JSON/捕获/编码时间算进帧CPU时间。

## 正确性与资格

先验证opcode计数与trace哈希，再做独立低密度语义fixtures和代表frame0/17/37截图。跨引擎不要求AA边缘/字体逐像素同值：预先定义不透明内部ROI、背景与alpha/layer交集探针，边缘带单列；文本核对同字体字节、内容、位置和非空笔画，不拿不同AA的raw pixels当同质量证明。native-stroke、gradient、复杂字体fallback若不满足同一语义/画质门槛，标明受限或N/A，不偷偷减少功能。受限结果仍保留，不参与该profile的固定质量结论。

冷启动称为process-cold：包含进程/运行时/输入加载，不声称清空OS字体、文件或GPU驱动磁盘缓存。缓存压力记录预备内存、运行内存及准备成本；不得无限加大cache却不计内存。所有原始日志、trace、截图、source/binary哈希、外部marker记录和失败原因随报告保留。
