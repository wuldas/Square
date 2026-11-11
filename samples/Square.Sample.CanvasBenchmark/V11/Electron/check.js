'use strict';

// CPU-only contract check. Browser objects below are recording stubs; no Electron/GPU is launched.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const protocol = require('./protocol');

function decode(cells) {
  const value = (start, count) => cells.slice(start, start + count).reduce((sum, bit, index) => sum + bit * 2 ** index, 0);
  return { sync: value(0, 8), payload: value(8, 32), crc: value(40, 8) };
}
function polynomialCrc(payload) {
  let polynomial = 0n;
  for (let byte = 0; byte < 4; ++byte) polynomial = (polynomial << 8n) | BigInt((payload >>> (byte * 8)) & 255);
  polynomial <<= 8n;
  for (let bit = 39; bit >= 8; --bit) if (polynomial & (1n << BigInt(bit))) polynomial ^= 0x107n << BigInt(bit - 8);
  return Number(polynomial);
}
for (const tag of [1, 128, 255]) for (const phase of [1, 2, 3]) for (const seq of [0, 37, 65535]) {
  const cells = protocol.markerCells(tag, phase, seq);
  const decoded = decode(cells);
  assert.equal(cells.length, 48);
  assert.equal(decoded.sync, 0xA5);
  assert.equal(decoded.payload, tag * 262144 + phase * 65536 + seq);
  assert.equal(decoded.crc, polynomialCrc(decoded.payload));
}
assert.equal(protocol.crc8(0x00060000), 0x7E);
assert.throws(() => protocol.parseArgs(['--trace', 'x', '--output', 'y', '--tag', '0']), /1..255/);
assert.throws(() => protocol.parseArgs(['--trace', 'x', '--output', 'y', '--tag', '1', '--verify']), /Missing value/);
assert.throws(() => protocol.parseArgs(['--trace', 'x', '--output', 'y', '--tag', '1', '--tag', '2']), /Duplicate/);

const triangle = [[8, 24], [24, 24], [16, 40]];
function makeTrace() {
  const ops = frame => [
    ['poly', 0, '#E08040FF'], ['poly-new', triangle.map(([x, y]) => [x + frame, y]), '#40A0FFFF'],
    ['stroke', 0, '#FFFFFFFF', 1.5], ['stroke-new', triangle, '#FFFFFFFF', 1.5],
    ['rect', 50, 50, 16, 16, '#FF000080'], ['text', 0, 80, 24, '#FFFFFFFF'],
    ['text-new', `Canvas ${frame}`, 14, 80, 44, '#FFFFFFFF'], ['image', 120, 24, 64, 64],
    ['transform', 1, 0, 0, 1, frame, 0], ['clip', 0], ['rect', 8, 24, 16, 16, '#FFFF00FF'], ['end-clip'], ['end-transform'],
    ['clip-new', triangle], ['rect', 8, 24, 16, 16, '#00FFFFFF'], ['end-clip'],
    ['layer', 200, 24, 64, 64, 0.65], ['rect', 200, 24, 40, 40, '#FF0000FF'], ['rect', 220, 44, 40, 40, '#00FF00FF'], ['end-layer'],
    ['layer', 300, 24, 64, 64, 0.65], ['rect', 300, 24, 40, 40, '#FFFFFFFF'], ['end-layer'],
    ['gradient', 400, 24, 64, 64, 400, 24, 464, 88, '#FFFFFFFF', '#000000FF'],
  ];
  const frames = [0, 1, 2].map(i => ({ ops: ops(i) }));
  const totals = {};
  for (const frame of frames.slice(1)) for (const op of frame.ops) totals[op[0]] = (totals[op[0]] || 0) + 1;
  return { protocolVersion: 11, case: 'contract', level: 'low', width: 1280, height: 800, seed: 1, warmupFrames: 1, measureFrames: 2, paths: [triangle], texts: [{ text: 'Canvas', size: 14 }], frames, expectedMeasuredOpcodeTotals: totals };
}
const malformed = makeTrace();
malformed.frames[0].ops.unshift(['end-clip']);
assert.throws(() => protocol.validateTrace(malformed), /Unbalanced/);
const nested = makeTrace();
nested.frames[0].ops = [['layer', 0, 24, 64, 64, 0.5], ['layer', 0, 24, 64, 64, 0.5], ['end-layer'], ['end-layer']];
assert.throws(() => protocol.validateTrace(nested), /cannot nest/);
const wrongCounts = makeTrace();
wrongCounts.expectedMeasuredOpcodeTotals.poly++;
assert.throws(() => protocol.validateTrace(wrongCounts), /count mismatch/);

async function checkRenderer(verify, nativeScaled = false) {
  const trace = makeTrace();
  if (nativeScaled) trace.samplingProfile = 'native-scaled';
  const preparation = protocol.validateTrace(trace);
  const raf = [], timers = [], contexts = [], pathEvents = [], textEvents = [], markers = [], readbacks = [];
  let stage = 'static', result, failure, clock = 0;
  class MockContext {
    constructor(isRoot) { this.isRoot = isRoot; this.globalAlpha = 1; this.stack = []; this.draws = []; contexts.push(this); }
    save() { this.stack.push({ globalAlpha: this.globalAlpha, font: this.font }); }
    restore() { assert.ok(this.stack.length, 'Unbalanced Canvas restore'); Object.assign(this, this.stack.pop()); }
    setTransform() {} transform() {} beginPath() {} rect() {} clip() {}
    fill() {} stroke() {} fillText() {}
    measureText(text) { textEvents.push({ stage, text }); return {}; }
    clearRect() { this.clears = (this.clears || 0) + 1; }
    fillRect(x, y, width, height) {
      if (this.isRoot && x === 0 && y === 0 && width === 96 && height === 8 && this.fillStyle === '#000000') markers.push(Array(48).fill(0));
      if (this.isRoot && y === 0 && width === 2 && height === 8 && this.fillStyle === '#ffffff') markers.at(-1)[x / 2] = 1;
      if (!this.isRoot) this.draws.push(this.globalAlpha);
    }
    drawImage(...args) { this.draws.push({ alpha: this.globalAlpha, args }); }
    createLinearGradient() { return { addColorStop() {} }; }
  }
  class MockCanvas {
    constructor(isRoot = false) { this.ctx = new MockContext(isRoot); }
    getContext() { return this.ctx; }
    toDataURL() { readbacks.push(stage); return 'data:image/png;base64,cG5n'; }
  }
  const root = new MockCanvas(true);
  const sandbox = {
    CanvasV11: protocol, console, innerWidth: 1280, innerHeight: 800, devicePixelRatio: 1,
    performance: { now: () => ++clock },
    requestAnimationFrame: callback => raf.push(callback),
    setTimeout: (callback, delay) => timers.push({ callback, delay }),
    Path2D: class { constructor() { pathEvents.push(stage); } moveTo() {} lineTo() {} closePath() {} },
    Image: class { constructor() { this.naturalWidth = this.naturalHeight = 32; } async decode() {} },
    document: {
      getElementById: () => root, head: { appendChild() {} },
      createElement: name => name === 'canvas' ? new MockCanvas() : {},
      fonts: { load: async () => [{}], ready: Promise.resolve(), check: () => true },
    },
    window: { benchmark: {
      config: async () => ({ trace, preparation, contentSize: [1280, 800], tag: 7, verify, mainAssetLoadMs: 1, mainReadyMs: 1, assets: {}, versions: {}, fontUrl: 'file:///font.ttf', imageUrl: 'file:///image.png', traceSha256: 'fixture', sourceSha256: {} }),
      complete: async (value, images) => { result = value; result.images = images; },
      fail: error => { failure = error; },
    } },
  };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, 'renderer.js'), 'utf8'), sandbox, { filename: 'renderer.js' });
  await new Promise(setImmediate);
  assert.equal(failure, undefined);
  assert.equal(raf.length, 1);
  assert.deepEqual(pathEvents, ['static', 'static'], 'Only closed/open static assets can be prepared before rAF');
  assert.equal(textEvents.length, 1, 'Only static text can be measured before rAF');
  for (let frame = 0; frame < 4; ++frame) {
    stage = frame === 3 ? 'end' : `frame${frame}`;
    const callback = raf.shift();
    assert.equal(typeof callback, 'function');
    callback(frame * 16.6);
    assert.equal(failure, undefined);
    assert.equal(pathEvents.filter(value => value === stage).length, 3, 'Current-frame and END redraw paths must be created');
    assert.equal(textEvents.filter(value => value.stage === stage).length, 1, 'Current-frame and END redraw text must call native measureText');
    if (frame === 3) assert.equal(textEvents.at(-1).text, 'Canvas 2', 'END redraw must use the final trace body');
  }
  assert.equal(raf.length, 0);
  assert.equal(timers.length, 1);
  assert.equal(timers[0].delay, 750);
  timers[0].callback();
  await new Promise(setImmediate);
  assert.equal(failure, undefined);
  assert.equal(result.status, 'ok');
  assert.equal(result.timingEligible, verify === 0);
  assert.equal(result.actualImageSampling, nativeScaled ? 'linear-low' : 'nearest');
  assert.ok(contexts.every(ctx => ctx.imageSmoothingEnabled === nativeScaled && ctx.imageSmoothingQuality === 'low'));
  assert.equal(result.frameCpu.length, 2);
  assert.equal(result.producerFrameTimes.length, 3);
  protocol.equalTotals(result.actualMeasuredOpcodeTotals, trace.expectedMeasuredOpcodeTotals);
  assert.equal(contexts.length, 2, 'Same-size bounded layer resources can be reused');
  assert.equal(contexts[1].clears, 8, 'Every layer is cleared on every frame and END redraw');
  assert.ok(contexts[1].draws.every(alpha => alpha === 1), 'Group opacity must not be applied per child');
  assert.equal(root.ctx.draws.filter(draw => draw.alpha === 0.65).length, 8, 'Group opacity is applied once per completed offscreen group');
  assert.ok(contexts.every(ctx => ctx.stack.length === 0));
  assert.equal(readbacks.length, verify ? 1 : 0);
  assert.equal(result.images.length, verify ? 1 : 0);
  const payloads = markers.map(cells => decode(cells).payload);
  assert.deepEqual(payloads, [7 * 262144 + 65536, 7 * 262144 + 131072, 7 * 262144 + 131072 + 1, 7 * 262144 + 196608 + 2]);
}

(async () => {
  await checkRenderer(0);
  await checkRenderer(1);
  await checkRenderer(0, true);
  const root = process.argv[2];
  if (root) {
    let count = 0;
    function scan(directory) {
      for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
        const filename = path.join(directory, entry.name);
        if (entry.isDirectory()) scan(filename);
        else if (entry.name === 'trace.json') { protocol.validateTrace(JSON.parse(fs.readFileSync(filename, 'utf8'))); ++count; }
      }
    }
    scan(path.resolve(root));
    assert.ok(count > 0, 'No generated trace.json found');
    console.log(`Validated ${count} generated traces`);
  }
  console.log('CPU-only contract checks passed: CRC, CLI, counts, exact frame sequence, per-frame native prepare, offscreen group alpha, verification isolation');
})().catch(error => { console.error(error); process.exitCode = 1; });
