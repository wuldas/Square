const { contextBridge, ipcRenderer } = require('electron');
contextBridge.exposeInMainWorld('benchmark', {
  config: () => ipcRenderer.invoke('benchmark:config'),
  complete: result => ipcRenderer.invoke('benchmark:complete', result),
  fail: message => ipcRenderer.send('benchmark:fail', message),
});
