'use strict';

(function buildBoxes() {
  var BOXES_PER_GROUP = 240;
  var grids = document.querySelectorAll('.perf-grid');
  if (grids.length !== 3) { throw new Error('expected 3 .perf-grid containers, found ' + grids.length); }
  grids.forEach(function (grid) {
    var kind = grid.getAttribute('data-kind');
    if (kind !== 'transform' && kind !== 'opacity' && kind !== 'width') {
      throw new Error('unknown perf-grid data-kind: ' + kind);
    }
    var fragment = document.createDocumentFragment();
    for (var i = 0; i < BOXES_PER_GROUP; i++) {
      var box = document.createElement('div');
      box.className = 'box';
      if (kind === 'transform') {
        box.classList.add('perf-shift');
        var spin = document.createElement('div');
        spin.className = 'perf-spin';
        var scale = document.createElement('div');
        scale.className = 'perf-scale';
        spin.appendChild(scale);
        box.appendChild(spin);
      } else if (kind === 'opacity') {
        box.classList.add('perf-fade');
      } else {
        box.classList.add('perf-wobble');
      }
      fragment.appendChild(box);
    }
    grid.appendChild(fragment);
  });
})();

/**
 * Counts requestAnimationFrame callbacks over a measured wall window after skipping a
 * warmup window. This is measured callback FPS, not physical presentation:
 * the display is 60 Hz, but callbacks do not prove compositor scanout.
 * Nothing is simulated or extrapolated.
 *
 * The workload must already be live (720 boxes, running CSS animations) and the page
 * visible; a visibility loss mid-run rejects instead of reporting a partial window.
 *
 * @param {number} warmupSeconds animated warmup before the measured window.
 * @param {number} seconds measured window duration in seconds.
 * @param {(startedAtEpochMs: number) => void} [onMeasureStart] invoked once when the
 *   measured window starts (the main process aligns CPU/memory sampling to it).
 * @returns {Promise<{seconds: number, frames: number, startedAtEpochMs: number}>}
 *   resolved with the actual elapsed window, the callback count inside it, and the
 *   measure-start time in epoch milliseconds.
 */
window.runBenchmark = function runBenchmark(warmupSeconds, seconds, onMeasureStart) {
  return new Promise(function (resolve, reject) {
    if (typeof warmupSeconds !== 'number' || !(warmupSeconds >= 0) || !isFinite(warmupSeconds)) {
      reject(new TypeError('runBenchmark: warmupSeconds must be a non-negative finite number'));
      return;
    }
    if (typeof seconds !== 'number' || !(seconds > 0) || !isFinite(seconds)) {
      reject(new TypeError('runBenchmark: seconds must be a positive finite number'));
      return;
    }
    if (document.visibilityState !== 'visible') {
      reject(new Error('runBenchmark: page is not visible (visibilityState=' +
        document.visibilityState + '); frames are not presented while hidden'));
      return;
    }
    var boxes = document.querySelectorAll('.box');
    if (boxes.length !== 720) {
      reject(new Error('runBenchmark: expected 720 boxes, found ' + boxes.length));
      return;
    }
    if (typeof document.getAnimations === 'function') {
      var running = 0;
      document.getAnimations().forEach(function (animation) {
        if (animation.playState === 'running') { running++; }
      });
      if (running === 0) {
        reject(new Error('runBenchmark: no running CSS animations found'));
        return;
      }
    }

    var warmupMs = warmupSeconds * 1000;
    var durationMs = seconds * 1000;
    var phase = 'warmup'; // warmup -> measure
    var phaseStart = null; // rAF timestamp the current phase started at
    var frames = 0;
    var startedAtEpochMs = 0;
    var finished = false;

    function onVisibilityChange() {
      if (!finished && document.visibilityState !== 'visible') {
        finished = true;
        document.removeEventListener('visibilitychange', onVisibilityChange);
        reject(new Error('runBenchmark: page became hidden mid-run (visibilityState=' +
          document.visibilityState + '); the measured window would not be presented'));
      }
    }
    document.addEventListener('visibilitychange', onVisibilityChange);

    function tick(now) {
      if (finished) { return; }
      if (phaseStart === null) { phaseStart = now; }
      if (phase === 'warmup') {
        if (now - phaseStart >= warmupMs) {
          phase = 'measure';
          phaseStart = now;
          startedAtEpochMs = Date.now();
          if (typeof onMeasureStart === 'function') { onMeasureStart(startedAtEpochMs); }
        }
      } else {
        var elapsedMs = now - phaseStart;
        if (elapsedMs >= durationMs) {
          finished = true;
          document.removeEventListener('visibilitychange', onVisibilityChange);
          resolve({ seconds: elapsedMs / 1000, frames: frames, startedAtEpochMs: startedAtEpochMs });
          return;
        }
        frames += 1;
      }
      window.requestAnimationFrame(tick);
    }
    window.requestAnimationFrame(tick);
  });
};

// Bench mode: the main process sends the warmup/seconds command after the window and
// page are verified; the renderer drives the rAF measurement and reports transitions.
(function autoBench() {
  if (!window.perfBench) { return; } // opened outside the Electron app
  function report(report) { window.perfBench.report(report); }
  window.perfBench.onCommand(function (command) {
    window.runBenchmark(command.warmupSeconds, command.seconds, function (startedAtEpochMs) {
      report({ state: 'measure', t: startedAtEpochMs });
    }).then(function (result) {
      report({ state: 'done', s: result.seconds, f: result.frames, t: result.startedAtEpochMs });
    }, function (error) {
      report({ state: 'error', e: String((error && error.message) || error) });
    });
  });
  // Report the client geometry once layout has settled (two frames).
  window.requestAnimationFrame(function () {
    window.requestAnimationFrame(function () {
      report({
        state: 'ready',
        w: window.innerWidth,
        h: window.innerHeight,
        dpr: window.devicePixelRatio,
      });
    });
  });
})();
