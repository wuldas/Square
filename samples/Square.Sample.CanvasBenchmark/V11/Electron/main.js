'use strict';

const { app, BrowserWindow, ipcMain } = require('electron');
const fs = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const { createHash } = require('node:crypto');
const { performance } = require('node:perf_hooks');
const { parseArgs, validateTrace, equalTotals } = require('./protocol');

const started = performance.now();
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const localTemp = path.join(__dirname, '.tmp');
fs.mkdirSync(localTemp, { recursive: true });
fs.mkdirSync(path.join(__dirname, '.user-data'), { recursive: true });
app.setPath('temp', localTemp);
app.setPath('userData', path.join(__dirname, '.user-data'));
app.commandLine.appendSwitch('force-device-scale-factor', '1');

let config, window, input, completed = false;
function writeResult(result) {
  fs.mkdirSync(path.dirname(config.output), { recursive: true });
  fs.writeFileSync(config.output, JSON.stringify(result, null, 2));
}
function fail(error) {
  const message = String(error && (error.stack || error));
  console.error(message);
  if (config && !completed) {
    try {
      writeResult({ protocolVersion: 11, framework: 'Electron', backend: 'Canvas2D', status: 'failed', timingEligible: false,
        trace: config.trace, traceSha256: input && input.traceSha256, tag: config.tag, error: message, versions: { ...process.versions } });
    } catch (writeError) { console.error(writeError); }
  }
  app.exit(1);
}

try {
  // These Chromium switches belong to the external runner, not the benchmark CLI.
  const firstBenchmarkArg = process.argv.findIndex(arg => ['--trace', '--output', '--tag', '--verify'].includes(arg));
  const args = process.argv.slice(firstBenchmarkArg < 0 ? process.argv.length : firstBenchmarkArg).filter(arg => arg !== '--no-sandbox' && arg !== '--enable-logging' && !arg.startsWith('--enable-logging=') && !arg.startsWith('--log-file='));
  config = parseArgs(args);
  if (!path.isAbsolute(config.trace) || !path.isAbsolute(config.output)) throw new Error('--trace and --output must be absolute paths');
  if (path.resolve(config.trace).toLowerCase() === path.resolve(config.output).toLowerCase()) throw new Error('--output cannot overwrite the trace');
} catch (error) {
  console.error(error.message);
  app.exit(2);
}

if (config) app.whenReady().then(async () => {
  const loadStart = performance.now();
  const bytes = fs.readFileSync(config.trace);
  const traceSha256 = sha256(bytes);
  input = { traceSha256 };
  const trace = JSON.parse(bytes.toString('utf8'));
  const preparation = validateTrace(trace);
  const directory = path.dirname(config.trace);
  const font = fs.readFileSync(path.join(directory, 'font.ttf'));
  const image = fs.readFileSync(path.join(directory, 'image.png'));
  const fontSha256 = sha256(font), imageSha256 = sha256(image);
  for (const [name, actual, expected] of [
    ['font', fontSha256, trace.assets && trace.assets.font && trace.assets.font.sha256],
    ['image', imageSha256, trace.assets && trace.assets.image && trace.assets.image.sha256],
  ]) if (expected && expected !== actual) throw new Error(`${name} SHA256 does not match trace asset declaration`);
  const sourceSha256 = {};
  for (const file of ['main.js', 'preload.js', 'protocol.js', 'renderer.js', 'index.html', 'package.json']) {
    sourceSha256[file] = sha256(fs.readFileSync(path.join(__dirname, file)));
  }
  input = {
    ...config, trace, preparation, traceSha256, sourceSha256,
    fontUrl: pathToFileURL(path.join(directory, 'font.ttf')).href,
    imageUrl: pathToFileURL(path.join(directory, 'image.png')).href,
    assets: { fontSha256, imageSha256, fontFamily: 'CanvasBench' },
    mainAssetLoadMs: performance.now() - loadStart,
    versions: { ...process.versions, chromiumProcessSandbox: !app.commandLine.hasSwitch('no-sandbox') },
  };

  let signalShown;
  const shown = new Promise(resolve => { signalShown = resolve; });
  ipcMain.handle('canvas-v11:config', async () => {
    await shown;
    return { ...input, mainReadyMs: performance.now() - started, contentSize: window.getContentSize() };
  });
  ipcMain.on('canvas-v11:fail', (_event, message) => fail(message));
  ipcMain.handle('canvas-v11:complete', async (_event, result, screenshots) => {
    try {
      if (completed) throw new Error('Duplicate completion');
      equalTotals(result.actualMeasuredOpcodeTotals, trace.expectedMeasuredOpcodeTotals);
      if (result.frameCpu.length !== trace.measureFrames) throw new Error('Measurement frame count mismatch');
      result.hardware = {
        gpuFeatureStatus: app.getGPUFeatureStatus(),
        basicInfo: await app.getGPUInfo('basic'),
        completeInfo: await app.getGPUInfo('complete'),
      };
      result.diagnostics.mainProcessElapsedMs = performance.now() - started;
      result.diagnostics.contentBounds = window.getContentBounds();
      result.diagnostics.electronExecutable = process.execPath;
      result.verificationScreenshots = [];
      for (const screenshot of screenshots || []) {
        if (config.verify !== 1 || ![0, 17, 37].includes(screenshot.seq)) throw new Error('Unexpected verification screenshot');
        const prefix = 'data:image/png;base64,';
        if (!screenshot.png.startsWith(prefix)) throw new Error('Invalid PNG data URL');
        const filename = `${config.output}.frame${String(screenshot.seq).padStart(4, '0')}.png`;
        fs.writeFileSync(filename, Buffer.from(screenshot.png.slice(prefix.length), 'base64'));
        result.verificationScreenshots.push({ seq: screenshot.seq, path: filename });
      }
      writeResult(result);
      completed = true;
      app.quit();
    } catch (error) { fail(error); }
  });

  window = new BrowserWindow({
    title: `Canvas v11 Electron | ${trace.case}/${trace.level} | tag ${config.tag}`,
    width: 1280, height: 800, useContentSize: true, x: 100, y: 100,
    resizable: false, maximizable: false, fullscreenable: false, autoHideMenuBar: true,
    backgroundColor: '#101827', show: false,
    webPreferences: { preload: path.join(__dirname, 'preload.js'), contextIsolation: true, nodeIntegration: false, zoomFactor: 1, backgroundThrottling: false },
  });
  window.removeMenu();
  window.on('page-title-updated', event => event.preventDefault());
  window.once('ready-to-show', () => { window.showInactive(); signalShown(); });
  window.on('closed', () => { if (!completed) fail('Window closed before completion'); });
  window.webContents.on('render-process-gone', (_event, details) => fail(JSON.stringify(details)));
  window.webContents.on('did-fail-load', (_event, code, description) => fail(`Page load failed ${code}: ${description}`));
  await window.loadFile(path.join(__dirname, 'index.html'));
}).catch(fail);
app.on('window-all-closed', () => app.quit());
