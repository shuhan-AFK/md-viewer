(() => {
  let theme = 'light';
  window.mdViewer = {
    runtime: 'webview2',
    canExportDocx: true,
    pickFiles: async () => [],
    openDroppedFiles: async () => [],
    saveDocument: async () => true,
    exportDocument: async () => true,
    closeDocument: async () => true,
    getTheme: async () => theme,
    setTheme: async value => { theme = value; return value; },
    getFontSize: async () => 14,
    setFontSize: async value => value,
    openExternal: async () => true,
    windowAction: async () => true,
    startWindowDrag: () => {},
    startWindowResize: () => {},
    onDocumentsOpened: () => () => {},
    onDocumentChanged: () => () => {},
    onError: () => () => {},
  };
})();
