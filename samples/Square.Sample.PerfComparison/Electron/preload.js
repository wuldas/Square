'use strict';

const { contextBridge, ipcRenderer } = require('electron');

// The page stays sandboxed from Node: it can only receive the benchmark command and
// report state transitions over IPC (no remote content, no Node in the renderer).
contextBridge.exposeInMainWorld('perfBench', {
  onCommand: (handler) => {
    ipcRenderer.on('perf-bench:command', (_event, command) => handler(command));
  },
  report: (report) => {
    ipcRenderer.send('perf-bench:report', report);
  },
});
