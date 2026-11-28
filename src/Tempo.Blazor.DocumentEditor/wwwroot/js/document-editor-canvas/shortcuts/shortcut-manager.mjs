const SHORTCUTS = [
    { key: 'b', ctrl: true, command: 'bold' },
    { key: 'i', ctrl: true, command: 'italic' },
    { key: 'u', ctrl: true, command: 'underline' },
    { key: 'z', ctrl: true, command: 'undo' },
    { key: 'y', ctrl: true, command: 'redo' },
    { key: 'k', ctrl: true, command: 'link', argument: 'https://example.com' },
    // Reset zoom: back to the configured automatic fit (the browser's own Ctrl+0 would reset the page zoom of the whole host).
    { key: '0', code: 'Digit0', ctrl: true, command: 'resetZoom' },
    { key: 'p', ctrl: true, shift: true, palette: true },
];

export function createCanvasShortcutManager(options = {}) {
    const root = options.root || null;
    const input = options.input || null;
    const execCommand = typeof options.execCommand === 'function' ? options.execCommand : null;
    const openCommandPalette = typeof options.openCommandPalette === 'function' ? options.openCommandPalette : null;
    const focusRibbon = typeof options.focusRibbon === 'function' ? options.focusRibbon : null;
    const openVersionsPanel = typeof options.openVersionsPanel === 'function' ? options.openVersionsPanel : null;
    let revision = 0;

    function mount() {
        root?.addEventListener?.('keydown', handleKeyDown);
        input?.addEventListener?.('keydown', handleKeyDown);
        root?.setAttribute?.('data-canvas-shortcut-manager', 'enabled');
        return api;
    }

    function destroy() {
        root?.removeEventListener?.('keydown', handleKeyDown);
        input?.removeEventListener?.('keydown', handleKeyDown);
        root?.removeAttribute?.('data-canvas-shortcut-manager');
    }

    function handleKeyDown(event) {
        if (isRibbonActivation(event)) {
            event.preventDefault?.();
            event.stopPropagation?.();
            revision += 1;
            root?.setAttribute?.('data-canvas-shortcut-last', 'ribbon');
            root?.setAttribute?.('data-canvas-shortcut-revision', String(revision));
            focusRibbon?.();
            return;
        }

        if (isVersionsPanelShortcut(event)) {
            event.preventDefault?.();
            event.stopPropagation?.();
            revision += 1;
            root?.setAttribute?.('data-canvas-shortcut-last', 'versions-panel');
            root?.setAttribute?.('data-canvas-shortcut-revision', String(revision));
            openVersionsPanel?.();
            return;
        }

        const shortcut = matchShortcut(event);
        if (!shortcut) {
            return;
        }

        event.preventDefault?.();
        event.stopPropagation?.();
        revision += 1;
        if (shortcut.palette) {
            root?.setAttribute?.('data-canvas-shortcut-last', 'command-palette');
            root?.setAttribute?.('data-canvas-shortcut-revision', String(revision));
            openCommandPalette?.();
            return;
        }

        const result = execCommand?.(shortcut.command, shortcut.argument ?? null);
        root?.setAttribute?.('data-canvas-shortcut-last', shortcut.command);
        root?.setAttribute?.('data-canvas-shortcut-handled', String(result?.handled === true || result?.changed === true));
        root?.setAttribute?.('data-canvas-shortcut-revision', String(revision));
    }

    function snapshot() {
        return {
            enabled: true,
            revision,
            shortcutCount: SHORTCUTS.length,
        };
    }

    const api = {
        mount,
        destroy,
        snapshot,
    };

    return api;
}

function isRibbonActivation(event) {
    const key = String(event?.key || '').toLowerCase();
    const onlyAlt = key === 'alt'
        && event?.altKey === true
        && event?.ctrlKey !== true
        && event?.metaKey !== true
        && event?.shiftKey !== true;

    return key === 'f10' || onlyAlt;
}

function isVersionsPanelShortcut(event) {
    const key = String(event?.key || '').toLowerCase();
    const ctrl = event?.ctrlKey === true || event?.metaKey === true;
    return key === 'v'
        && ctrl
        && event?.altKey === true
        && event?.shiftKey !== true;
}

function matchShortcut(event) {
    const key = String(event?.key || '').toLowerCase();
    const ctrl = event?.ctrlKey === true || event?.metaKey === true;
    const shift = event?.shiftKey === true;
    const alt = event?.altKey === true;
    if (alt) {
        return null;
    }

    return SHORTCUTS.find(shortcut =>
        (shortcut.key === key || (shortcut.code !== undefined && event?.code === shortcut.code))
        && shortcut.ctrl === ctrl
        && (shortcut.shift === true) === shift) || null;
}
