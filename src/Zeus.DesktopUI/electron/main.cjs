const { app, BrowserWindow, ipcMain, dialog, session } = require('electron');
const { spawn } = require('node:child_process');
const { randomBytes } = require('node:crypto');
const { join } = require('node:path');
const { pathToFileURL } = require('node:url');
const { createServer } = require('node:net');
let child, window, port, quitting = false;
const mutations = new Set();
let exitRequested = false;
const token = randomBytes(32).toString('hex');
const entry = pathToFileURL(join(__dirname, '..', 'dist', 'index.html')).href;
const routes = {
  diagnostics: ['GET', 'diagnostics'], performance: ['GET', 'performance'], history: ['GET', 'history'],
  preview: ['POST', 'personalization/preview', 'layoutId'],
  apply: ['POST', 'personalization/apply', 'previewId'],
  revert: ['POST', 'personalization/revert', 'transactionId'],
};
async function request(route, method = 'GET', body) {
  const response = await fetch(`http://127.0.0.1:${port}/api/${route}`, {
    method, headers: { 'X-Zeus-Token': token, 'Content-Type': 'application/json' },
    body: body ? JSON.stringify(body) : undefined,
    signal: route.endsWith('/apply') || route.endsWith('/revert') ? undefined : AbortSignal.timeout(60000),
  });
  const value = await response.json();
  if (!response.ok) throw new Error(value.message || 'O Windows não concluiu esta operação. Tente novamente.');
  return value;
}
async function freePort() {
  return await new Promise((resolve, reject) => {
    const server = createServer(); server.on('error', reject);
    server.listen(0, '127.0.0.1', () => { const value = server.address().port; server.close(() => resolve(value)); });
  });
}
async function startNative() {
  port = await freePort();
  const base = app.isPackaged ? join(process.resourcesPath, 'native') : join(__dirname, '..', 'native-bin');
  child = spawn(join(base, 'Zeus.LocalApi.exe'), [], {
    cwd: base, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'],
    env: { ...process.env, ZEUS_TOKEN: token, ZEUS_PORT: String(port) },
  });
  child.on('error', () => { if (!quitting) dialog.showErrorBox('Zeus PC', 'O serviço local não iniciou. Abra o pacote completo do aplicativo.'); });
  let exited = false;
  child.on('exit', () => { exited = true; if (!quitting && window) { dialog.showErrorBox('Zeus PC', 'O serviço local foi encerrado. Reabra o aplicativo para continuar.'); app.quit(); } });
  child.stdout.resume(); child.stderr.resume();
  for (let i = 0; i < 120; i++) {
    if (exited) throw new Error('O serviço local foi encerrado durante a inicialização.');
    try { await request('health'); return; } catch { await new Promise(r => setTimeout(r, 250)); }
  }
  throw new Error('O serviço local excedeu o tempo de inicialização.');
}
if (!app.requestSingleInstanceLock()) app.quit();
else {
  app.on('second-instance', () => { if (window) { if (window.isMinimized()) window.restore(); window.focus(); } });
  app.whenReady().then(async () => {
    session.defaultSession.setPermissionRequestHandler((_wc, _permission, callback) => callback(false));
    session.defaultSession.webRequest.onHeadersReceived((details, callback) => callback({ responseHeaders: {
      ...details.responseHeaders,
      'Content-Security-Policy': ["default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'none'; object-src 'none'; base-uri 'none'; frame-src 'none'"],
    } }));
    await startNative();
    for (const [name, [method, route, field]] of Object.entries(routes)) {
      ipcMain.handle(`zeus:${name}`, async (event, value) => {
        if (event.sender !== window?.webContents || event.senderFrame !== window.webContents.mainFrame || event.senderFrame.url !== entry)
          throw new Error('Origem não autorizada.');
        if (field && (typeof value !== 'string' || value.length > 80 || !/^[a-zA-Z0-9-]+$/.test(value))) throw new Error('Seleção inválida.');
        const operation = request(route, method, field ? { [field]: value } : undefined);
        if (name === 'apply' || name === 'revert') {
          mutations.add(operation);
          try { return await operation; } finally { mutations.delete(operation); }
        }
        return operation;
      });
    }
    window = new BrowserWindow({ title: 'Zeus PC', width: 1220, height: 830, minWidth: 800, minHeight: 620,
      backgroundColor: '#0f1117', autoHideMenuBar: true, show: false,
      webPreferences: { preload: join(__dirname, 'preload.cjs'), contextIsolation: true, sandbox: true, nodeIntegration: false },
    });
    window.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
    window.webContents.on('will-navigate', (event, url) => { if (url !== entry) event.preventDefault(); });
    window.on('close', event => {
      if (mutations.size > 0) {
        event.preventDefault();
        dialog.showMessageBox(window, { type: 'info', title: 'Concluindo a alteração', message: 'Aguarde a aplicação ou restauração terminar antes de fechar o Zeus PC.', buttons: ['Entendi'] });
      }
    });
    window.once('ready-to-show', () => window.show());
    await window.loadURL(entry);
  }).catch(error => { dialog.showErrorBox('Zeus PC', error.message); app.quit(); });
}
app.on('window-all-closed', () => app.quit());
app.on('before-quit', event => {
  if (mutations.size > 0) {
    event.preventDefault();
    if (!exitRequested) {
      exitRequested = true;
      Promise.allSettled([...mutations]).then(() => app.quit());
    }
    return;
  }
  quitting = true;
  child?.kill();
});
