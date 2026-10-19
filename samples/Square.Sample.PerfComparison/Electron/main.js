'use strict';

const { app, BrowserWindow, ipcMain, screen, Menu } = require('electron');
const fs = require('fs');
const os = require('os');
const path = require('path');

// Standalone Electron benchmark app (shared cross-target contract):
//   square-electron-perf.exe --seconds 30 --warmup 2 --output C:\abs\result.json
// The app opens a visible 900x1240-client window at outer origin (100,100), runs the
// 720-box CSS animation workload, skips --warmup seconds of animated warmup, measures
// for --seconds seconds, and emits the shared result JSON (compact, unrounded) to
// stdout and, when --output is given, to that file. Exit codes: 0 measured, 1 runtime
// failure, 2 usage error. No .NET sampler and no DevTools/CDP are involved.

// Parses both "--seconds 30" and "--seconds=30" forms from the raw command line
// (Electron forwards every argument to process.argv; Chromium ignores unknown ones).
function optionValue(name) {
  const equalsForm = '--' + name + '=';
  const spaceForm = '--' + name;
  for (var i = app.isPackaged ? 1 : 2; i < process.argv.length; i++) {
    const argument = process.argv[i];
    if (argument.startsWith(equalsForm)) return argument.slice(equalsForm.length);
    if (argument === spaceForm && i + 1 < process.argv.length) return process.argv[i + 1];
  }
  return null;
}

const warmupSeconds = Number(optionValue('warmup') !== null ? optionValue('warmup') : 2);
const measuredSeconds = Number(optionValue('seconds') !== null ? optionValue('seconds') : 30);
const outputPath = optionValue('output');

const usage = 'Usage: square-electron-perf --seconds 30 --warmup 2 --output C:\\abs\\result.json';
const userDataPrefix = 'square-electron-perf-';


const ownedUserDataDir = fs.mkdtempSync(path.join(os.tmpdir(), userDataPrefix));
app.setPath('userData', ownedUserDataDir);
app.once('quit', () => {
  try { fs.rmSync(ownedUserDataDir, { recursive: true, force: true }); }
  catch (error) { console.error('Temporary profile cleanup: ' + error.message); }
});
// Pin the device scale so the 900x1240 CSS client is 900x1240 physical pixels at the
// benchmark display's 100% scale regardless of OS DPI settings.
app.commandLine.appendSwitch('force-device-scale-factor', '1');

let exiting = false;
function finish(code, message) {
  if (exiting) return;
  exiting = true;
  if (message) {
    if (code === 0) console.log(message);
    else console.error(message);
  }
  app.exit(code);
}

function fail(message) {
  finish(1, message);
}

// ---------------------------------------------------------------------------
// CPU/memory sampling via app.getAppMetrics(): cpu.cumulativeCPUUsage is the
// cumulative CPU time of a process in SECONDS since its start, so per-process
// baselines are subtracted instead of using the interval-averaged percentCPUUsage.
// Memory values are kilobytes. peakWorkingSetSize is a LIFETIME kernel peak, not a
// measurement-window observed peak, so the observed peak comes from the 1 Hz samples.
// Process identity is (pid, creationTime), so a reused PID is never merged with its
// predecessor; when a process disappears its last-seen cumulative CPU is kept (the
// sub-1 Hz gap between its last sample and its exit stays unmeasured).
// ---------------------------------------------------------------------------
const metrics = {
  cpuFirst: new Map(), // (pid:creationTime) -> cumulative CPU seconds at first sight
  cpuLast: new Map(), // last-seen cumulative CPU seconds; kept for vanished processes
  privateSumKb: 0,
  workingSumKb: 0,
  privatePeakKb: 0,
  workingPeakKb: 0,
  samples: 0,
  processCount: 0,
  incomplete: false,
  sawCumulativeCpu: false,
  sawPrivateBytes: false,
  timer: null,
  measureStartEpochMs: 0,
};

function sampleMetrics() {
  let processList;
  try {
    processList = app.getAppMetrics();
  } catch {
    metrics.incomplete = true;
    return;
  }
  let privateKb = 0;
  let workingKb = 0;
  for (const process of processList) {
    const key = process.pid + ':' + process.creationTime;
    const memory = process.memory || {};
    const cpu = process.cpu || {};
    if (typeof memory.privateBytes !== 'number') metrics.incomplete = true;
    else {
      privateKb += memory.privateBytes;
      metrics.sawPrivateBytes = true;
    }
    if (typeof memory.workingSetSize !== 'number') metrics.incomplete = true;
    else workingKb += memory.workingSetSize;
    if (typeof cpu.cumulativeCPUUsage !== 'number') metrics.incomplete = true;
    else {
      metrics.sawCumulativeCpu = true;
      if (!metrics.cpuFirst.has(key)) {
        metrics.cpuFirst.set(key, process.creationTime >= metrics.measureStartEpochMs ? 0 : cpu.cumulativeCPUUsage);
      }
      metrics.cpuLast.set(key, cpu.cumulativeCPUUsage);
    }
  }
  metrics.processCount = Math.max(metrics.processCount, processList.length);
  metrics.privateSumKb += privateKb;
  metrics.workingSumKb += workingKb;
  metrics.privatePeakKb = Math.max(metrics.privatePeakKb, privateKb);
  metrics.workingPeakKb = Math.max(metrics.workingPeakKb, workingKb);
  metrics.samples++;
}

function startMetricsSampling() {
  metrics.measureStartEpochMs = Date.now();
  sampleMetrics(); // baseline read + first 1 Hz memory sample
  metrics.timer = setInterval(sampleMetrics, 1000);
}

function stopMetricsSampling() {
  if (metrics.timer !== null) {
    clearInterval(metrics.timer);
    metrics.timer = null;
    sampleMetrics(); // final cumulative-CPU read
  }
}

let readyGeometry = null;
let watchdog = null;

function armWatchdog() {
  clearTimeout(watchdog);
  watchdog = setTimeout(function () {
    fail('benchmark timed out: the renderer never reported a result within warmup+seconds+90s');
  }, (warmupSeconds + measuredSeconds + 90) * 1000);
}

function writeResult(report) {
  if (!readyGeometry) {
    fail('renderer reported a result before reporting the client geometry');
    return;
  }
  if (!(report.s > 0) || !(report.f > 0)) {
    fail('renderer reported an empty measurement window (seconds=' + report.s + ', frames=' + report.f + ')');
    return;
  }
  stopMetricsSampling();
  // Require real counters: never emit silent zeros when app.getAppMetrics() did not
  // provide cumulative CPU or private bytes on this platform.
  if (metrics.incomplete || !metrics.sawCumulativeCpu || !metrics.sawPrivateBytes) {
    fail('app.getAppMetrics did not provide a complete CPU/private-memory sample');
    return;
  }

  let cpuSeconds = 0;
  for (const [key, first] of metrics.cpuFirst) {
    const last = metrics.cpuLast.get(key);
    if (typeof last === 'number') cpuSeconds += Math.max(0, last - first);
  }
  const wallSeconds = (Date.now() - metrics.measureStartEpochMs) / 1000;
  const result = {
    target: 'electron',
    seconds: report.s,
    frames: Math.round(report.f),
    fps: report.f / report.s,
    fpsDefinition: 'Electron renderer requestAnimationFrame callbacks per measured second; not presented-frame or scanout telemetry',
    cpuSeconds,
    cpuOneCorePercent: wallSeconds > 0 ? (cpuSeconds / wallSeconds) * 100 : 0,
    cpuMeasurementSeconds: wallSeconds,
    privateAvgMiB: metrics.samples > 0 ? metrics.privateSumKb / metrics.samples / 1024 : 0,
    privatePeakMiB: metrics.privatePeakKb / 1024,
    workingAvgMiB: metrics.samples > 0 ? metrics.workingSumKb / metrics.samples / 1024 : 0,
    workingPeakMiB: metrics.workingPeakKb / 1024,
    samples: metrics.samples,
    memoryDefinition: 'app.getAppMetrics() per-process privateBytes (private commit) + workingSetSize (working set) in KiB summed over the Electron process tree and sampled at 1 Hz; observed peak = maximum 1 Hz sample (not the kernel lifetime peak); GPU VRAM is not included; processes that exit mid-run keep their last-seen cumulative CPU (sub-1 Hz exited-CPU gap)',
    clientWidth: readyGeometry.w,
    clientHeight: readyGeometry.h,
    warmupSeconds,
    targetFps: 60,
    startedUtc: new Date(metrics.measureStartEpochMs).toISOString(),
    processCount: metrics.processCount,
    incomplete: metrics.incomplete,
    electronVersion: process.versions.electron,
    chromiumVersion: process.versions.chrome,
    userDataDirectory: ownedUserDataDir,
  };
  const json = JSON.stringify(result);
  if (typeof outputPath === 'string' && outputPath.length > 0) {
    const fullPath = path.resolve(outputPath);
    const directory = path.dirname(fullPath);
    if (directory) fs.mkdirSync(directory, { recursive: true });
    fs.writeFileSync(fullPath, json);
  }
  clearTimeout(watchdog);
  finish(0, json);
}

let window = null;

function createWindow() {
  // No menu anywhere: setApplicationMenu(null) before creation keeps Windows from
  // reserving menu-bar height; calling window.removeMenu() after creation would
  // release that reservation and grow the client area past the requested size.
  Menu.setApplicationMenu(null);
  window = new BrowserWindow({
    // useContentSize makes width/height the web page (client) size: exactly
    // 900x1240 CSS pixels; the outer frame is placed at origin (100,100).
    x: 100,
    y: 100,
    width: 900,
    height: 1240,
    useContentSize: true,
    show: true,
    resizable: false,
    maximizable: false,
    fullscreenable: false,
    backgroundColor: '#f3f6fb',
    title: 'square-electron-perf',
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  });
  window.webContents.on('render-process-gone', (_event, details) => fail('renderer gone: ' + details.reason));
  window.webContents.on('did-finish-load', () => {
    // The whole window must stay on its screen during the measured run.
    const bounds = window.getBounds();
    const area = screen.getDisplayMatching(bounds).bounds;
    const fits = bounds.x >= area.x && bounds.y >= area.y &&
      bounds.x + bounds.width <= area.x + area.width &&
      bounds.y + bounds.height <= area.y + area.height;
    if (!fits) {
      fail('window ' + JSON.stringify(bounds) + ' does not fit on screen ' + JSON.stringify(area));
      return;
    }
    // Invalid warmup/seconds already exit with code 2 before any window exists.
    armWatchdog();
    window.webContents.send('perf-bench:command', { warmupSeconds, seconds: measuredSeconds });
  });
  window.loadFile(path.join(__dirname, 'index.html'), {
    query: { bench: '1', warmup: String(warmupSeconds), seconds: String(measuredSeconds) },
  });
}

ipcMain.on('perf-bench:report', (_event, report) => {
  if (!report || typeof report !== 'object' || typeof report.state !== 'string') return;
  if (report.state === 'ready') {
    const dpr = report.dpr;
    if (report.w !== 900 || report.h !== 1240 || dpr !== 1) {
      fail('client viewport is ' + report.w + 'x' + report.h + ' at devicePixelRatio ' + dpr + ', expected exactly 900x1240 at 1');
      return;
    }
    readyGeometry = { w: report.w, h: report.h };
  } else if (report.state === 'measure') {
    startMetricsSampling();
  } else if (report.state === 'done') {
    try { writeResult(report); }
    catch (error) { fail('result reporting failed: ' + error.message); }
  } else if (report.state === 'error') {
    fail('renderer: ' + String(report.e || 'unspecified error'));
  }
});

// Usage errors that are detectable before any window exists exit with code 2.
if (!Number.isFinite(warmupSeconds) || warmupSeconds < 0 ||
    !Number.isFinite(measuredSeconds) || measuredSeconds <= 0) {
  console.error(usage + ' (warmup must be >= 0, seconds must be > 0)');
  process.exit(2);
}

app.whenReady().then(createWindow);
app.on('window-all-closed', () => {
  if (!exiting) fail('all windows closed before the benchmark finished');
});
