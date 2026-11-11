'use strict';

(function (root, factory) {
  if (typeof module === 'object' && module.exports) module.exports = factory();
  else root.CanvasV11 = factory();
})(globalThis, () => {
  const WIDTH = 1280, HEIGHT = 800;
  const OPCODES = new Set(['poly', 'poly-new', 'stroke', 'stroke-new', 'rect', 'text', 'text-new', 'image', 'transform', 'end-transform', 'clip', 'clip-new', 'end-clip', 'layer', 'end-layer', 'gradient']);

  function assert(condition, message) {
    if (!condition) throw new Error(message);
  }

  function parseArgs(args) {
    const config = { verify: 0 };
    const seen = new Set();
    for (let i = 0; i < args.length; i += 2) {
      const name = args[i];
      assert(['--trace', '--output', '--tag', '--verify'].includes(name), `Unknown argument: ${name}`);
      assert(i + 1 < args.length && !args[i + 1].startsWith('--'), `Missing value for ${name}`);
      assert(!seen.has(name), `Duplicate argument: ${name}`);
      seen.add(name);
      config[name.slice(2)] = ['--tag', '--verify'].includes(name) ? Number(args[i + 1]) : args[i + 1];
    }
    assert(config.trace && config.output, '--trace and --output are required');
    assert(Number.isInteger(config.tag) && config.tag >= 1 && config.tag <= 255, '--tag must be an integer in 1..255');
    assert(config.verify === 0 || config.verify === 1, '--verify must be 0 or 1');
    return config;
  }

  function increment(totals, opcode) {
    totals[opcode] = (totals[opcode] || 0) + 1;
  }

  function equalTotals(actual, expected) {
    const names = new Set([...Object.keys(actual), ...Object.keys(expected)]);
    for (const name of names) {
      assert(OPCODES.has(name), `Unknown opcode in totals: ${name}`);
      assert(Number.isSafeInteger(expected[name] || 0) && (expected[name] || 0) >= 0, `Invalid expected count: ${name}`);
      assert((actual[name] || 0) === (expected[name] || 0), `Opcode count mismatch for ${name}: actual=${actual[name] || 0}, expected=${expected[name] || 0}`);
    }
  }

  function validateTrace(trace) {
    assert(trace.protocolVersion === 11, 'protocolVersion must be 11');
    assert(trace.width === WIDTH && trace.height === HEIGHT, 'Trace must be 1280x800');
    assert(typeof trace.case === 'string' && typeof trace.level === 'string', 'Trace case and level are required');
    assert(Number.isInteger(trace.seed), 'Trace seed must be an integer');
    assert(Number.isInteger(trace.warmupFrames) && trace.warmupFrames >= 0 && trace.warmupFrames <= 65535, 'Invalid warmupFrames');
    assert(Number.isInteger(trace.measureFrames) && trace.measureFrames > 0 && trace.measureFrames <= 65535, 'Invalid measureFrames');
    assert(Array.isArray(trace.frames) && trace.frames.length === trace.warmupFrames + trace.measureFrames, 'frames length must equal warmupFrames + measureFrames');
    assert(Array.isArray(trace.paths) && Array.isArray(trace.texts), 'paths and texts arrays are required');
    assert(trace.expectedMeasuredOpcodeTotals && typeof trace.expectedMeasuredOpcodeTotals === 'object' && !Array.isArray(trace.expectedMeasuredOpcodeTotals), 'expectedMeasuredOpcodeTotals is required');
    const finite = values => assert(values.every(Number.isFinite), 'Non-finite numeric input');
    const color = value => assert(typeof value === 'string' && /^#[0-9a-fA-F]{8}$/.test(value), `Invalid RGBA: ${value}`);
    const points = (value, minimum) => {
      assert(Array.isArray(value) && value.length >= minimum, `Path requires at least ${minimum} points`);
      for (const point of value) {
        assert(Array.isArray(point) && point.length === 2, 'Point must be [x,y]');
        finite(point);
      }
    };
    for (const value of trace.paths) points(value, 2);
    for (const value of trace.texts) {
      assert(value && typeof value.text === 'string' && Number.isFinite(value.size) && value.size > 0, 'Invalid static text');
    }
    const closedPaths = new Set(), openPaths = new Set(), layerSizes = new Map();
    const expected = Object.create(null);
    function pathId(id, minimum, destination) {
      assert(Number.isInteger(id) && id >= 0 && id < trace.paths.length, `Invalid pathId: ${id}`);
      assert(trace.paths[id].length >= minimum, `pathId ${id} needs ${minimum} points`);
      destination.add(id);
    }
    const arity = { poly: 3, 'poly-new': 3, stroke: 4, 'stroke-new': 4, rect: 6, text: 5, 'text-new': 6, image: 5, transform: 7, 'end-transform': 1, clip: 2, 'clip-new': 2, 'end-clip': 1, layer: 6, 'end-layer': 1, gradient: 11 };
    for (let frameIndex = 0; frameIndex < trace.frames.length; ++frameIndex) {
      const frame = trace.frames[frameIndex];
      assert(frame && Array.isArray(frame.ops), `Missing ops in frame ${frameIndex}`);
      const stack = [];
      for (const op of frame.ops) {
        assert(Array.isArray(op) && OPCODES.has(op[0]), `Unknown opcode in frame ${frameIndex}: ${op && op[0]}`);
        const name = op[0];
        assert(op.length === arity[name], `Invalid arity for ${name} in frame ${frameIndex}`);
        switch (name) {
          case 'poly': pathId(op[1], 3, closedPaths); color(op[2]); break;
          case 'poly-new': points(op[1], 3); color(op[2]); break;
          case 'stroke':
            pathId(op[1], 2, openPaths); color(op[2]); finite([op[3]]); assert(op[3] > 0, 'Stroke width must be positive'); break;
          case 'stroke-new':
            points(op[1], 2); color(op[2]); finite([op[3]]); assert(op[3] > 0, 'Stroke width must be positive'); break;
          case 'rect': finite(op.slice(1, 5)); color(op[5]); break;
          case 'text':
            assert(Number.isInteger(op[1]) && op[1] >= 0 && op[1] < trace.texts.length, 'Invalid textId'); finite(op.slice(2, 4)); color(op[4]); break;
          case 'text-new':
            assert(typeof op[1] === 'string' && Number.isFinite(op[2]) && op[2] > 0, 'Invalid dynamic text'); finite(op.slice(3, 5)); color(op[5]); break;
          case 'image': finite(op.slice(1, 5)); assert(op[3] > 0 && op[4] > 0, 'Image extent must be positive'); break;
          case 'transform': finite(op.slice(1)); stack.push('transform'); break;
          case 'clip': pathId(op[1], 3, closedPaths); stack.push('clip'); break;
          case 'clip-new': points(op[1], 3); stack.push('clip'); break;
          case 'layer':
            finite(op.slice(1));
            assert(stack.length === 0, 'Layers must be top-level and cannot nest');
            assert(op[3] > 0 && op[4] > 0 && op[3] <= WIDTH && op[4] <= HEIGHT, 'Layer must have bounded positive extent <=1280x800');
            assert(op[5] >= 0 && op[5] <= 1, 'Layer opacity must be in 0..1');
            layerSizes.set(`${op[3]}/${op[4]}`, [op[3], op[4]]);
            stack.push('layer'); break;
          case 'end-transform': case 'end-clip': case 'end-layer':
            assert(stack.pop() === name.slice(4), `Unbalanced ${name} in frame ${frameIndex}`); break;
          case 'gradient': finite(op.slice(1, 9)); color(op[9]); color(op[10]); break;
        }
        if (frameIndex >= trace.warmupFrames) increment(expected, name);
      }
      assert(stack.length === 0, `Unclosed state in frame ${frameIndex}`);
    }
    equalTotals(expected, trace.expectedMeasuredOpcodeTotals);
    return { closedPaths: [...closedPaths], openPaths: [...openPaths], layerSizes: [...layerSizes.values()] };
  }

  function crc8(payload) {
    let crc = 0;
    for (let byte = 0; byte < 4; ++byte) {
      crc ^= (payload >>> (byte * 8)) & 255;
      for (let bit = 0; bit < 8; ++bit) crc = (crc & 128) ? ((crc << 1) ^ 7) & 255 : (crc << 1) & 255;
    }
    return crc;
  }

  function markerCells(tag, phase, seq) {
    const payload = ((tag & 255) << 18) | ((phase & 3) << 16) | (seq & 65535);
    const checksum = crc8(payload);
    const cells = [];
    for (let bit = 0; bit < 8; ++bit) cells.push((0xA5 >>> bit) & 1);
    for (let bit = 0; bit < 32; ++bit) cells.push((payload >>> bit) & 1);
    for (let bit = 0; bit < 8; ++bit) cells.push((checksum >>> bit) & 1);
    return cells;
  }

  return { WIDTH, HEIGHT, parseArgs, validateTrace, increment, equalTotals, crc8, markerCells };
});
