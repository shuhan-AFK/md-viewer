(() => {
  const MIN_SIZE = 10;
  const MAX_SIZE = 28;
  const DEFAULT_SIZE = 14;
  let currentSize = DEFAULT_SIZE;
  let mountQueued = false;

  const clamp = value => Math.min(MAX_SIZE, Math.max(MIN_SIZE, Math.round(Number(value) || DEFAULT_SIZE)));

  const ensureStyles = () => {
    if (document.getElementById('md-font-size-styles')) return;
    const style = document.createElement('style');
    style.id = 'md-font-size-styles';
    style.textContent = `
      .markdown-body { font-size: var(--md-reader-size, 14px) !important; }
      .markdown-editor { font-size: var(--md-reader-size, 14px) !important; }
      .markdown-body h1 { font-size: var(--md-h1-size, 22px) !important; }
      .markdown-body h2 { font-size: var(--md-h2-size, 18px) !important; }
      .markdown-body h3 { font-size: var(--md-h3-size, 16px) !important; }
      .markdown-body h4 { font-size: var(--md-h4-size, 14px) !important; }
      .markdown-body h5, .markdown-body h6 { font-size: var(--md-h5-size, 13px) !important; }
      .markdown-body table { font-size: var(--md-table-size, 13px) !important; }
      .markdown-body pre code { font-size: var(--md-code-size, 12.5px) !important; }
      .font-size-controls {
        flex: none;
        align-items: center;
        gap: 1px;
        display: flex;
        height: 30px;
        padding: 0 2px;
        border-left: 1px solid var(--border);
        border-right: 1px solid var(--border);
      }
      .font-size-controls .font-size-button {
        width: 27px;
        height: 28px;
        font-size: 12px;
        font-weight: 650;
      }
      .font-size-controls .font-size-value {
        width: 30px;
        color: var(--text);
        font-size: 11px;
        font-variant-numeric: tabular-nums;
      }
      @media (max-width: 820px) {
        .font-size-controls .font-size-value { display: none; }
      }
    `;
    document.head.appendChild(style);
  };

  const applySize = (value, persist = false) => {
    currentSize = clamp(value);
    const root = document.documentElement;
    const scaled = base => `${(currentSize * base / DEFAULT_SIZE).toFixed(2)}px`;
    root.style.setProperty('--md-reader-size', `${currentSize}px`);
    root.style.setProperty('--md-h1-size', scaled(22));
    root.style.setProperty('--md-h2-size', scaled(18));
    root.style.setProperty('--md-h3-size', scaled(16));
    root.style.setProperty('--md-h4-size', scaled(14));
    root.style.setProperty('--md-h5-size', scaled(13));
    root.style.setProperty('--md-table-size', scaled(13));
    root.style.setProperty('--md-code-size', scaled(12.5));
    const valueButton = document.querySelector('.font-size-value');
    if (valueButton) {
      if (valueButton.textContent !== String(currentSize)) {
        valueButton.textContent = String(currentSize);
      }
      valueButton.title = `当前字号：${currentSize}px；点击恢复默认字号`;
    }
    if (persist && window.mdViewer?.setFontSize) {
      window.mdViewer.setFontSize(currentSize).catch(() => {});
    }
  };

  const makeButton = (text, title, className, onClick) => {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = `icon-button font-size-button ${className}`;
    button.textContent = text;
    button.title = title;
    button.setAttribute('aria-label', title);
    button.addEventListener('click', onClick);
    return button;
  };

  const mountControls = () => {
    mountQueued = false;
    ensureStyles();
    const toolbar = document.querySelector('.toolbar-actions');
    if (!toolbar) return;
    let controls = document.getElementById('font-size-controls');
    if (!controls) {
      controls = document.createElement('div');
      controls.id = 'font-size-controls';
      controls.className = 'font-size-controls';
      controls.setAttribute('role', 'group');
      controls.setAttribute('aria-label', '字体大小');
      controls.append(
        makeButton('A−', '缩小字体 (Ctrl+-)', 'font-size-decrease', () => applySize(currentSize - 1, true)),
        makeButton(String(currentSize), '恢复默认字号 (Ctrl+0)', 'font-size-value', () => applySize(DEFAULT_SIZE, true)),
        makeButton('A+', '放大字体 (Ctrl++)', 'font-size-increase', () => applySize(currentSize + 1, true)),
      );
    }
    if (controls.parentElement !== toolbar) {
      const themeButton = [...toolbar.querySelectorAll('button')]
        .find(button => button.title?.startsWith('主题：'));
      toolbar.insertBefore(controls, themeButton || null);
    }
    applySize(currentSize);
  };

  const queueMount = () => {
    if (mountQueued) return;
    mountQueued = true;
    window.requestAnimationFrame(mountControls);
  };

  window.addEventListener('keydown', event => {
    if (!event.ctrlKey || event.altKey || event.metaKey) return;
    if (event.key === '-' || event.key === '_') {
      event.preventDefault();
      applySize(currentSize - 1, true);
    } else if (event.key === '+' || event.key === '=') {
      event.preventDefault();
      applySize(currentSize + 1, true);
    } else if (event.key === '0') {
      event.preventDefault();
      applySize(DEFAULT_SIZE, true);
    }
  }, true);

  const initialize = async () => {
    ensureStyles();
    try {
      applySize(await window.mdViewer.getFontSize());
    } catch {
      applySize(DEFAULT_SIZE);
    }
    queueMount();
    new MutationObserver(queueMount).observe(document.body, { childList: true, subtree: true });
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initialize, { once: true });
  } else {
    initialize();
  }
})();
