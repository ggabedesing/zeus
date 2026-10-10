const { contextBridge, ipcRenderer } = require('electron');
contextBridge.exposeInMainWorld('zeus', Object.freeze({
  diagnostics: () => ipcRenderer.invoke('zeus:diagnostics'),
  performance: () => ipcRenderer.invoke('zeus:performance'),
  history: () => ipcRenderer.invoke('zeus:history'),
  preview: layoutId => ipcRenderer.invoke('zeus:preview', layoutId),
  apply: previewId => ipcRenderer.invoke('zeus:apply', previewId),
  revert: transactionId => ipcRenderer.invoke('zeus:revert', transactionId),
}));
