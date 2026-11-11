'use strict';

(async () => {
  const rendererStarted = performance.now();
  const config = await window.benchmark.config();
  const { trace, preparation } = config;
  const { increment, equalTotals, markerCells } = CanvasV11;
  const canvas = document.getElementById('canvas');
  if (innerWidth !== 1280 || innerHeight !== 800 || devicePixelRatio !== 1 || config.contentSize[0] !== 1280 || config.contentSize[1] !== 800) {
    throw new Error(`Required physical client 1280x800 DPR1, got ${innerWidth}x${innerHeight} DPR${devicePixelRatio}`);
  }
  canvas.width = 1280;
  canvas.height = 800;
  const root = canvas.getContext('2d', { alpha: false });
  if (!root) throw new Error('Canvas2D unavailable');
  const nativeScaled = trace.samplingProfile === 'native-scaled';
  function configure(ctx) {
    ctx.imageSmoothingEnabled = nativeScaled;
    ctx.imageSmoothingQuality = 'low';
    ctx.textBaseline = 'top';
    ctx.textAlign = 'left';
    ctx.direction = 'ltr';
  }
  configure(root);

  const assetStart = performance.now();
  // Explicit @font-face registration prevents falling back to an installed font with another version.
  const fontStyle = document.createElement('style');
  fontStyle.textContent = `@font-face{font-family:CanvasBench;src:url(${JSON.stringify(config.fontUrl)}) format('truetype');font-weight:400;font-style:normal;font-display:block}`;
  document.head.appendChild(fontStyle);
  const loadedFonts = await document.fonts.load('14px "CanvasBench"', 'Canvas 0123');
  await document.fonts.ready;
  if (!loadedFonts.length || !document.fonts.check('14px "CanvasBench"', 'Canvas 0123')) throw new Error('Shared font.ttf could not be registered');
  const image = new Image();
  image.src = config.imageUrl;
  await image.decode();
  for (const frame of trace.frames) for (const op of frame.ops) {
    if (op[0] === 'image' && !((op[3] === image.naturalWidth && op[4] === image.naturalHeight) || (op[3] === image.naturalWidth * 2 && op[4] === image.naturalHeight * 2))) {
      throw new Error('Image trace must use complete source at integer 1x or 2x scale');
    }
  }
  const assetLoadMs = config.mainAssetLoadMs + performance.now() - assetStart;
  const assetsReadyMs = performance.now() - rendererStarted;

  function makePath(points, close) {
    const path = new Path2D();
    path.moveTo(points[0][0], points[0][1]);
    for (let i = 1; i < points.length; ++i) path.lineTo(points[i][0], points[i][1]);
    if (close) path.closePath();
    return path;
  }
  function prepareText(text, size) {
    const font = `${size}px "CanvasBench"`;
    root.font = font;
    // Canvas2D exposes TextMetrics, not a reusable Paragraph/TextLayout object.
    return { text, font, metrics: root.measureText(text) };
  }
  const staticStart = performance.now();
  const closedPaths = new Map(preparation.closedPaths.map(id => [id, makePath(trace.paths[id], true)]));
  const openPaths = new Map(preparation.openPaths.map(id => [id, makePath(trace.paths[id], false)]));
  const texts = trace.texts.map(value => prepareText(value.text, value.size));
  const layers = new Map();
  let offscreenPixelCount = 0;
  for (const [width, height] of preparation.layerSizes) {
    const buffer = document.createElement('canvas');
    buffer.width = Math.ceil(width);
    buffer.height = Math.ceil(height);
    const ctx = buffer.getContext('2d', { alpha: true });
    if (!ctx) throw new Error('Layer Canvas2D unavailable');
    configure(ctx);
    layers.set(`${width}/${height}`, { canvas: buffer, ctx });
    offscreenPixelCount += buffer.width * buffer.height;
  }
  const staticPrepareMs = performance.now() - staticStart;
  const actualMeasuredOpcodeTotals = Object.create(null);
  const frameCpu = [], warmupFrameCpu = [], producerFrameTimes = [], screenshots = [];
  let logicalIndex = 0, firstDrawMs = null, endMarkerTimeMs = null;

  function prepareFrame(ops) {
    // Only the current logical frame receives native objects. No frame pool is prepared here.
    const dynamic = new Map();
    for (let i = 0; i < ops.length; ++i) {
      const op = ops[i];
      switch (op[0]) {
        case 'poly-new': case 'clip-new': dynamic.set(i, makePath(op[1], true)); break;
        case 'stroke-new': dynamic.set(i, makePath(op[1], false)); break;
        case 'text-new': dynamic.set(i, prepareText(op[1], op[2])); break;
      }
    }
    return dynamic;
  }

  function marker(phase, seq) {
    const cells = markerCells(config.tag, phase, seq);
    root.fillStyle = '#000000';
    root.fillRect(0, 0, 96, 8);
    root.fillStyle = '#ffffff';
    for (let cell = 0; cell < cells.length; ++cell) if (cells[cell]) root.fillRect(cell * 2, 0, 2, 8);
  }

  function recordFrame(ops, dynamic, measured, phase, seq) {
    let ctx = root, group = null;
    root.fillStyle = '#101827ff';
    root.fillRect(0, 0, 1280, 800);
    for (let i = 0; i < ops.length; ++i) {
      const op = ops[i], name = op[0];
      switch (name) {
        case 'poly': case 'poly-new':
          ctx.fillStyle = op[2]; ctx.fill(name === 'poly' ? closedPaths.get(op[1]) : dynamic.get(i)); break;
        case 'stroke': case 'stroke-new':
          ctx.strokeStyle = op[2]; ctx.lineWidth = op[3]; ctx.stroke(name === 'stroke' ? openPaths.get(op[1]) : dynamic.get(i)); break;
        case 'rect': ctx.fillStyle = op[5]; ctx.fillRect(op[1], op[2], op[3], op[4]); break;
        case 'text': {
          const text = texts[op[1]];
          ctx.font = text.font; ctx.fillStyle = op[4]; ctx.fillText(text.text, op[2], op[3]); break;
        }
        case 'text-new': {
          const text = dynamic.get(i);
          ctx.font = text.font; ctx.fillStyle = op[5]; ctx.fillText(text.text, op[3], op[4]); break;
        }
        case 'image': ctx.drawImage(image, op[1], op[2], op[3], op[4]); break;
        case 'transform': ctx.save(); ctx.transform(op[1], op[2], op[3], op[4], op[5], op[6]); break;
        case 'end-transform': case 'end-clip': ctx.restore(); break;
        case 'clip': case 'clip-new':
          ctx.save(); ctx.clip(name === 'clip' ? closedPaths.get(op[1]) : dynamic.get(i)); break;
        case 'layer': {
          const buffer = layers.get(`${op[3]}/${op[4]}`);
          group = { buffer, x: op[1], y: op[2], width: op[3], height: op[4], opacity: op[5] };
          ctx = buffer.ctx;
          ctx.clearRect(0, 0, buffer.canvas.width, buffer.canvas.height);
          ctx.save();
          ctx.setTransform(1, 0, 0, 1, -group.x, -group.y);
          ctx.beginPath(); ctx.rect(group.x, group.y, group.width, group.height); ctx.clip();
          break;
        }
        case 'end-layer':
          ctx.restore();
          ctx = root;
          ctx.save(); ctx.globalAlpha = group.opacity;
          ctx.drawImage(group.buffer.canvas, 0, 0, group.width, group.height, group.x, group.y, group.width, group.height);
          ctx.restore(); group = null; break;
        case 'gradient': {
          const gradient = ctx.createLinearGradient(op[5], op[6], op[7], op[8]);
          gradient.addColorStop(0, op[9]); gradient.addColorStop(1, op[10]);
          ctx.fillStyle = gradient; ctx.fillRect(op[1], op[2], op[3], op[4]); break;
        }
        default: throw new Error(`Unsupported opcode: ${name}`);
      }
      if (measured) increment(actualMeasuredOpcodeTotals, name);
    }
    // Every state is balanced by trace validation. The marker is drawn in root/device coordinates.
    marker(phase, seq);
  }

  async function finish() {
    equalTotals(actualMeasuredOpcodeTotals, trace.expectedMeasuredOpcodeTotals);
    const result = {
      protocolVersion: 11, framework: 'Electron', backend: 'Canvas2D', status: 'ok',
      case: trace.case, level: trace.level, seed: trace.seed, traceSha256: config.traceSha256,
      sourceSha256: config.sourceSha256, assets: { ...config.assets, fontRegistered: true },
      clientWidth: innerWidth, clientHeight: innerHeight, devicePixelRatio,
      tag: config.tag, verify: config.verify, timingEligible: config.verify === 0,
      samplingProfile: trace.samplingProfile || null, actualImageSampling: nativeScaled ? 'linear-low' : 'nearest',
      qualityQualification: nativeScaled ? 'native-sampler-profile; excluded from common fixed-quality image conclusions' : 'pending cross-engine image qualification',
      warmupFrames: trace.warmupFrames, measureFrames: trace.measureFrames,
      assetLoadMs, staticPrepareMs, frameCpu, producerFrameTimes, actualMeasuredOpcodeTotals,
      expectedMeasuredOpcodeTotals: trace.expectedMeasuredOpcodeTotals, versions: config.versions,
      nativeAssets: { closedPaths: closedPaths.size, openPaths: openPaths.size, staticTexts: texts.length, offscreenCanvases: layers.size, offscreenPixelCount, offscreenBytesRgba: offscreenPixelCount * 4 },
      diagnostics: {
        mainReadyMs: config.mainReadyMs, assetsReadyMs, firstDrawMs, endMarkerTimeMs, warmupFrameCpu,
        producerClock: 'performance.now() milliseconds since renderer time origin; rAF timestamps share that origin',
        cpuBoundary: 'nativePrepare: current-frame Path2D/measureText; recordSubmit: all Canvas2D calls, opcode counters and marker; total: synchronous callback work before diagnostics/readback',
        presentBoundary: 'No synchronous Canvas2D present/GPU-complete event; presentMs is null. rAF scheduling is diagnostic only; external composited-desktop observation is the main completion metric.',
        textNewBoundary: 'Each text-new calls measureText to create current-frame TextMetrics and then fillText; Canvas2D exposes no native layout/paragraph object. Chromium may apply its normal font/glyph caches.',
        layerStrategy: 'Bounded alpha canvas per distinct extent, cleared and completely redrawn for every layer; opacity applied once on compositing the completed group.',
        imageSampling: nativeScaled ? 'Native-scaled profile: imageSmoothingEnabled=true, imageSmoothingQuality=low; native linear sampling, separate quality profile' : 'imageSmoothingEnabled=false; full source at integer 1x/2x scale (common image-grid/mixed traces use 1x)',
        nativeStroke: 'Canvas2D default butt caps, miter joins and miterLimit=10; separate default-quality profile',
        gradient: 'Native Canvas2D linear pad gradient; cross-engine interpolation requires image qualification',
        verification: 'verify=1 executes the entire supplied trace, captures measurement seq0/17/37 when present after frameCPU timing, and is excluded from performance samples',
      },
    };
    await window.benchmark.complete(result, screenshots);
  }

  function paint(timestamp) {
    try {
      if (logicalIndex === trace.frames.length) {
        const lastOps = trace.frames[trace.frames.length - 1].ops;
        const dynamic = prepareFrame(lastOps);
        recordFrame(lastOps, dynamic, false, 3, trace.measureFrames);
        endMarkerTimeMs = performance.now();
        setTimeout(() => finish().catch(error => window.benchmark.fail(String(error.stack || error))), 750);
        return;
      }
      const measured = logicalIndex >= trace.warmupFrames;
      const phase = measured ? 2 : 1;
      const seq = measured ? logicalIndex - trace.warmupFrames : logicalIndex;
      const ops = trace.frames[logicalIndex].ops;
      const start = performance.now();
      const dynamic = prepareFrame(ops);
      const prepared = performance.now();
      recordFrame(ops, dynamic, measured, phase, seq);
      const recorded = performance.now();
      const timing = { seq, nativePrepareMs: prepared - start, recordSubmitMs: recorded - prepared, presentMs: null, totalMs: recorded - start };
      (measured ? frameCpu : warmupFrameCpu).push(timing);
      producerFrameTimes.push({ logicalIndex, phase, seq, rafTimestampMs: timestamp, callbackStartMs: start, recordEndMs: recorded });
      if (firstDrawMs === null) firstDrawMs = recorded - rendererStarted;
      if (config.verify === 1 && measured && [0, 17, 37].includes(seq)) screenshots.push({ seq, png: canvas.toDataURL('image/png') });
      ++logicalIndex;
      requestAnimationFrame(paint);
    } catch (error) { window.benchmark.fail(String(error.stack || error)); }
  }
  requestAnimationFrame(paint);
})().catch(error => window.benchmark.fail(String(error.stack || error)));
