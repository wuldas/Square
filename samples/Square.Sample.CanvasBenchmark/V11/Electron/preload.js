'use strict';
const { contextBridge, ipcRenderer } = require('electron');
contextBridge.exposeInMainWorld('benchmark', {
  config: () => ipcRenderer.invoke('canvas-v11:config'),
  complete: (result, screenshots) => ipcRenderer.invoke('canvas-v11:complete', result, screenshots),
  fail: message => ipcRenderer.send('canvas-v11:fail', message),
});
