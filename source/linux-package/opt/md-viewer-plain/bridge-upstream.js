(() => {
  const pending = new Map();
  const listeners = new Map();
  const queued = new Map();
  let nextId = 1;

  const invoke = (method, args = []) => new Promise((resolve, reject) => {
    const id = nextId++;
    pending.set(id, { resolve, reject });
    window.chrome.webview.postMessage({ kind: 'invoke', id, method, args });
  });

  const subscribe = (type, callback) => {
    if (!listeners.has(type)) listeners.set(type, new Set());
    listeners.get(type).add(callback);
    const events = queued.get(type) || [];
    queued.delete(type);
    for (const payload of events) callback(payload);
    return () => listeners.get(type)?.delete(callback);
  };

  const emit = (type, payload) => {
    const callbacks = listeners.get(type);
    if (!callbacks || callbacks.size === 0) {
      if (!queued.has(type)) queued.set(type, []);
      queued.get(type).push(payload);
      return;
    }
    for (const callback of callbacks) callback(payload);
  };

  window.chrome.webview.addEventListener('message', (event) => {
    const message = event.data;
    if (message?.type === 'response') {
      const request = pending.get(message.id);
      if (!request) return;
      pending.delete(message.id);
      if (message.error) request.reject(new Error(message.error));
      else request.resolve(message.result);
      return;
    }
    if (message?.type) emit(message.type, message.payload);
  });

  window.mdViewer = {
    runtime: 'webview2',
    canExportDocx: true,
    pickFiles: () => invoke('pickFiles'),
    openDroppedFiles: () => Promise.resolve([]),
    saveDocument: (id, content) => invoke('saveDocument', [id, content]),
    exportDocument: (id, content) => invoke('exportDocument', [id, content]),
    closeDocument: (id) => invoke('closeDocument', [id]),
    getTheme: () => invoke('getTheme'),
    setTheme: (preference) => invoke('setTheme', [preference]),
    getFontSize: () => invoke('getFontSize'),
    setFontSize: (size) => invoke('setFontSize', [size]),
    openExternal: (url) => invoke('openExternal', [url]),
    windowAction: (action) => invoke('windowAction', [action]),
    startWindowDrag: () => window.chrome.webview.postMessage({ kind: 'event', method: 'startWindowDrag' }),
    startWindowResize: (direction) => window.chrome.webview.postMessage({ kind: 'event', method: 'startWindowResize', args: [direction] }),
    onDocumentsOpened: (callback) => subscribe('documents:opened', callback),
    onDocumentChanged: (callback) => subscribe('documents:changed', callback),
    onError: (callback) => subscribe('app:error', callback),
  };
})();
