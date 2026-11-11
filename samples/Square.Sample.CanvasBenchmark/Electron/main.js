const { app, BrowserWindow, ipcMain } = require('electron');
const fs = require('node:fs');
const path = require('node:path');

// Keep persistent Chromium state and temporary files with this isolated benchmark.
const localTemp = path.join(__dirname, '.tmp');
fs.mkdirSync(localTemp, { recursive: true });
fs.mkdirSync(path.join(__dirname, '.user-data'), { recursive: true });
app.setPath('temp', localTemp);
app.setPath('userData', path.join(__dirname, '.user-data'));

function parseArgs(args) {
  const config = { load: 1, seconds: 10, warmup: 2, screenshot: false, label: '' };
  for (let i = 0; i < args.length; ++i) {
    const name = args[i];
    if (name === '--screenshot') { config.screenshot = true; continue; }
    if (!['--scene', '--output', '--load', '--seconds', '--warmup', '--label'].includes(name)) {
      throw new Error(`Unknown argument: ${name}`);
    }
    if (++i >= args.length) throw new Error(`Missing value for ${name}`);
    config[name.slice(2)] = ['--scene', '--output', '--label'].includes(name) ? args[i] : Number(args[i]);
  }
  if (!config.scene || !config.output) throw new Error('--scene and --output are required');
  if (!Number.isInteger(config.load) || config.load < 1) throw new Error('--load must be a positive integer');
  if (!Number.isFinite(config.seconds) || config.seconds <= 0) throw new Error('--seconds must be positive');
  if (!Number.isFinite(config.warmup) || config.warmup < 0) throw new Error('--warmup must be nonnegative');
  return config;
}

let window;
let config;
try {
  const firstBenchmarkArg = process.argv.findIndex(arg => ['--scene', '--output', '--load', '--seconds', '--warmup', '--screenshot', '--label'].includes(arg));
  const argv = process.argv.slice(firstBenchmarkArg < 0 ? process.argv.length : firstBenchmarkArg).filter(arg => arg !== '--no-sandbox' && !arg.startsWith('--enable-logging') && !arg.startsWith('--log-file='));
  config = parseArgs(argv);
} catch (error) {
  console.error(error.message);
  app.exit(2);
}
if (config) app.whenReady().then(async () => {
  const scene = JSON.parse(fs.readFileSync(config.scene, 'utf8'));
  const icon = fs.readFileSync(path.join(path.dirname(config.scene), 'icon.png')).toString('base64');
  ipcMain.handle('benchmark:config', () => ({
    ...config, scene, icon: `data:image/png;base64,${icon}`,
    versions: { ...process.versions, chromiumProcessSandbox: !app.commandLine.hasSwitch('no-sandbox') },
  }));
  ipcMain.handle('benchmark:complete', async (_event, result) => {
    const gpuInfo = await app.getGPUInfo('complete');
    result.versions.gpuFeatureStatus = app.getGPUFeatureStatus();
    result.versions.gpuInfo = gpuInfo;
    fs.mkdirSync(path.dirname(config.output), { recursive: true });
    fs.writeFileSync(config.output, JSON.stringify(result, null, 2));
    if (config.screenshot) {
      const image = await window.webContents.capturePage();
      fs.writeFileSync(config.output + '.png', image.toPNG());
    }
    app.quit();
  });
  ipcMain.on('benchmark:fail', (_event, message) => {
    console.error(message);
    app.exit(1);
  });
  window = new BrowserWindow({
    title: `Electron Canvas2D | ${config.load}x${config.label ? ` | ${config.label}` : ''}`, width: scene.width, height: scene.height,
    useContentSize: true, x: 100, y: 100, resizable: false, autoHideMenuBar: true,
    backgroundColor: scene.background, show: false,
    webPreferences: { preload: path.join(__dirname, 'preload.js'), contextIsolation: true, nodeIntegration: false },
  });
  window.removeMenu();
  window.on('page-title-updated', event => event.preventDefault());
  window.once('ready-to-show', () => { window.show(); window.focus(); });
  window.webContents.on('render-process-gone', (_event, details) => {
    console.error(JSON.stringify(details));
    app.exit(1);
  });
  await window.loadFile('index.html');
}).catch(error => { console.error(error); app.exit(1); });
app.on('window-all-closed', () => app.quit());
