'use strict';

(async () => {
  const config = await window.benchmark.config();
  const scene = config.scene;
  const canvas = document.getElementById('canvas');
  const dpr = window.devicePixelRatio;
  canvas.width = Math.round(scene.width * dpr);
  canvas.height = Math.round(scene.height * dpr);
  const ctx = canvas.getContext('2d', { alpha: false });
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.imageSmoothingEnabled = true;
  ctx.imageSmoothingQuality = 'low';
  ctx.textBaseline = 'top';
  const image = new Image();
  image.src = config.icon;
  await image.decode();
  await document.fonts.load('16px "Segoe UI"');
  const gradients = new Map();
  const prepare = op => {
    switch (op[0]) {
      case 'line': {
        const p = new Path2D(); p.moveTo(op[1], op[2]); p.lineTo(op[3], op[4]);
        return () => { ctx.strokeStyle = op[5]; ctx.lineWidth = op[6]; ctx.lineCap = 'butt'; ctx.stroke(p); };
      }
      case 'rect':
        return () => { ctx.fillStyle = op[5]; ctx.fillRect(op[1], op[2], op[3], op[4]); };
      case 'circle': {
        const p = new Path2D(); p.arc(op[1], op[2], op[3], 0, Math.PI * 2);
        return () => { ctx.strokeStyle = op[4]; ctx.lineWidth = op[5]; ctx.lineCap = 'butt'; ctx.stroke(p); };
      }
      case 'arc': {
        const p = new Path2D(); p.arc(op[1], op[2], op[3], op[4], op[4] + op[5], op[5] < 0);
        const caps = [op[4], op[4] + op[5]].map(angle => {
          const cap = new Path2D();
          cap.arc(op[1] + op[3] * Math.cos(angle), op[2] + op[3] * Math.sin(angle), op[7] / 2, 0, Math.PI * 2);
          return cap;
        });
        return () => {
          ctx.strokeStyle = op[6]; ctx.lineWidth = op[7]; ctx.lineCap = 'butt'; ctx.stroke(p);
          ctx.fillStyle = op[6]; ctx.fill(caps[0]); ctx.fill(caps[1]);
        };
      }
      case 'curve': {
        // Shared contract: sample the same cubic at t=i/32 before measurement.
        const p = new Path2D();
        for (let i = 0; i <= 32; ++i) {
          const t = i / 32, u = 1 - t;
          const x = u*u*u*op[1] + 3*u*u*t*op[3] + 3*u*t*t*op[5] + t*t*t*op[7];
          const y = u*u*u*op[2] + 3*u*u*t*op[4] + 3*u*t*t*op[6] + t*t*t*op[8];
          if (i === 0) p.moveTo(x, y); else p.lineTo(x, y);
        }
        const fill = new Path2D(p); fill.lineTo(op[7], op[9]); fill.lineTo(op[1], op[9]); fill.closePath();
        const key = op[10] + '/' + op[11];
        if (!gradients.has(key)) {
          const gradient = ctx.createLinearGradient(0, 210, 0, 390);
          gradient.addColorStop(0, op[10]); gradient.addColorStop(1, op[11]);
          gradients.set(key, gradient);
        }
        const gradient = gradients.get(key);
        return () => {
          ctx.save(); ctx.clip(fill);
          ctx.fillStyle = gradient; ctx.fillRect(op[1], 200, op[7] - op[1], 190);
          ctx.restore();
          ctx.strokeStyle = op[10]; ctx.lineWidth = op[12]; ctx.lineCap = 'butt'; ctx.stroke(p);
        };
      }
      case 'text': {
        const font = `${op[4]}px "Segoe UI"`;
        ctx.font = font; ctx.measureText(op[1]);
        return () => { ctx.font = font; ctx.fillStyle = op[5]; ctx.fillText(op[1], op[2], op[3]); };
      }
      case 'image':
        return () => ctx.drawImage(image, op[1], op[2], op[3], op[4]);
      default: throw new Error(`Unknown op: ${op[0]}`);
    }
  };
  const frames = scene.frames.map(frame => frame.ops.map(prepare));
  const intervals = [], drawTimes = [];
  let animationStart, measurementStart, previous;
  let count = 0;
  const paint = timestamp => {
    if (animationStart === undefined) animationStart = timestamp;
    const elapsed = timestamp - animationStart;
    if (measurementStart === undefined && elapsed >= config.warmup * 1000) measurementStart = timestamp;
    if (measurementStart !== undefined && timestamp >= measurementStart + config.seconds * 1000) {
      window.benchmark.complete({
        framework: 'Electron', load: config.load, frames: count, seconds: config.seconds,
        fps: count / config.seconds, frameIntervalsMs: intervals, drawTimesMs: drawTimes,
        clientWidth: innerWidth, clientHeight: innerHeight, devicePixelRatio: dpr,
        versions: config.versions,
        metric: 'requestAnimationFrame callbacks in the measured wall-clock window; drawTimesMs is synchronous Canvas2D command submission, not GPU completion; physical scanout is not observed',
      }).catch(error => window.benchmark.fail(String(error.stack || error)));
      return;
    }
    const drawStart = performance.now();
    ctx.fillStyle = scene.background;
    ctx.fillRect(0, 0, scene.width, scene.height);
    const commands = frames[Math.floor(elapsed * 60 / 1000) % frames.length];
    for (let repeat = 0; repeat < config.load; ++repeat) for (const draw of commands) draw();
    const drawTime = performance.now() - drawStart;
    if (measurementStart !== undefined) {
      if (previous !== undefined) intervals.push(timestamp - previous);
      previous = timestamp;
      ++count;
      drawTimes.push(drawTime);
    }
    requestAnimationFrame(paint);
  };
  requestAnimationFrame(paint);
})().catch(error => window.benchmark.fail(String(error.stack || error)));
