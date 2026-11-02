// Pointer adapter for the PDF template designer. A finger, a pen and a mouse move or resize a field
// through the shared pointer-drag helper (docs/touch-interaction.md: never a second drag implementation).
// The session is attached to the workspace element, never the document, and commits ONCE, on pointerup,
// with client pixels divided by the measured page surface. A cancel commits nothing and restores the
// live preview. A press outside the workspace, or one with Ctrl/⌘ held, is declined.
//
// Deployed the module lives at _content/Tempo.Blazor.Signing/js/, so the helper is the sibling
// _content/Tempo.Blazor/js/pointer-drag.js. Node tests inject the helper; a static specifier would
// resolve inside this package and 404 in the browser.

const VIRTUAL_PAGE = 1000;
const HELPER_URL = new URL('../../Tempo.Blazor/js/pointer-drag.js', import.meta.url);

/**
 * Attaches one move/resize session to a designer workspace.
 * @param {Element} element The workspace the gesture is delegated from.
 * @param {{ onCommit?: (session: object) => void, onContextMenu?: (info: object) => void, pageSize?: number, selector?: string, attach?: Function }} [options]
 * @returns {{ detach: () => void, lastCommit: () => object|null, probe: () => string }}
 */
export function attachDesignerDrag(element, options = {}) {
    const pageSize = options.pageSize > 0 ? options.pageSize : VIRTUAL_PAGE;
    const attach = typeof options.attach === 'function' ? options.attach : null;
    let committed = null;
    let session = null;

    const readField = (target) => {
        const field = target?.closest?.('[data-field-uuid]');
        return {
            fieldUuid: field?.getAttribute('data-field-uuid') ?? null,
            handle: target?.closest?.('[data-handle]')?.getAttribute('data-handle') ?? null,
            areaUuid: target?.closest?.('[data-area-uuid]')?.getAttribute('data-area-uuid') ?? null,
            surface: target?.closest?.('.tm-pdf-template-designer__page-surface') ?? null,
            node: target?.closest?.('[data-field-uuid]') ?? field,
        };
    };

    const clearPreview = (node) => {
        if (!node?.style) return;
        node.style.transform = '';
        node.style.width = '';
        node.style.height = '';
    };

    const previewNodes = (pressedNode) => {
        // The selected class is the source of truth. element.selectedFieldUuids is not written by
        // Blazor, so a multi-select move used to preview only the pressed field.
        const root = typeof element.closest === 'function'
            ? (element.closest('.tm-pdf-template-designer') ?? element)
            : element;
        if (typeof root.querySelectorAll !== 'function') {
            return pressedNode ? [pressedNode] : [];
        }
        const selected = [...root.querySelectorAll('.tm-signing-field--selected')];
        if (selected.length < 2) {
            return pressedNode ? [pressedNode] : [];
        }
        return selected;
    };

    const start = (attachImpl) => {
        let pressed = { fieldUuid: null, handle: null, areaUuid: null, surface: null, node: null };
        const drag = attachImpl(element, {
            // Capture starts only once the gesture crosses the threshold, so a tap still selects.
            threshold: 1,
            captureOn: 'start',
            selector: options.selector,
            listenerCapture: true,
            touchAction: null,
            onPress(info) {
                const target = info.event?.target;
                if (!element.contains?.(target)) {
                    pressed = { fieldUuid: null, handle: null, areaUuid: null, surface: null, node: null };
                    return false;
                }
                if (info.event?.ctrlKey || info.event?.metaKey) return false;
                // Draw mode owns the page. A field under the pointer must not start a move.
                const root = typeof element.closest === 'function' ? element.closest('.tm-pdf-template-designer') : null;
                if (root?.classList?.contains?.('tm-pdf-template-designer--drawing')) return false;
                if (!target?.closest?.('[data-field-uuid], [data-handle]')) return false;
                pressed = readField(target);
            },
            onStart() {
                clearPreview(pressed.node);
            },
            onMove(info) {
                const node = pressed.node;
                if (!node?.style || (!info.started && info.dx === 0 && info.dy === 0)) return;
                if (pressed.handle) {
                    const handle = pressed.handle ?? '';
                    const west = handle === 'West' || handle === 'NorthWest' || handle === 'SouthWest' || handle === 'W';
                    const north = handle === 'North' || handle === 'NorthWest' || handle === 'NorthEast' || handle === 'N';
                    const east = handle.includes('East') || handle === 'E' || handle === 'SouthEast' || handle === 'NorthEast';
                    const south = handle.includes('South') || handle === 'S' || handle === 'SouthEast' || handle === 'SouthWest';
                    const dx = west ? -info.dx : info.dx;
                    const dy = north ? -info.dy : info.dy;
                    const width = Math.max(0, (node.offsetWidth || 0) + (east || west ? dx : 0));
                    const height = Math.max(0, (node.offsetHeight || 0) + (south || north ? dy : 0));
                    node.style.width = `${width}px`;
                    node.style.height = `${height}px`;
                    if (west || north) {
                        node.style.transform = `translate(${west ? info.dx : 0}px, ${north ? info.dy : 0}px)`;
                    }
                }
                else {
                    for (const peer of previewNodes(pressed.node)) {
                        if (peer?.style) peer.style.transform = `translate(${info.dx}px, ${info.dy}px)`;
                    }
                }
            },
            onEnd(info) {
                const surface = pressed.surface
                    ?? element.querySelector?.('.tm-pdf-template-designer__page-surface')
                    ?? null;
                const rect = surface?.getBoundingClientRect?.();
                const width = rect?.width > 0 ? rect.width : pageSize;
                const height = rect?.height > 0 ? rect.height : pageSize;
                committed = {
                    dx: info.dx,
                    dy: info.dy,
                    normalizedDx: info.dx / width,
                    normalizedDy: info.dy / height,
                    fieldUuid: pressed.fieldUuid,
                    handle: pressed.handle,
                    areaUuid: pressed.areaUuid,
                };
                options.onCommit?.(committed);
            },
            onRelease(info) {
                // A committed drag keeps its preview until Blazor rewrites the positioned style.
                // Clearing it here leaves a content-sized box. A cancel never gets that render.
                if (info?.outcome === 'cancel') {
                    for (const peer of previewNodes(pressed.node)) clearPreview(peer);
                    clearPreview(pressed.node);
                }
            },
        });
        const longPress = attachImpl(element, {
            selector: options.selector,
            pointerTypes: ['touch'],
            longPressMs: 500,
            touchAction: null,
            onStart(info) {
                if (info.origin !== 'long-press') return;
                const target = readField(info.event?.target ?? info.target);
                options.onContextMenu?.({
                    fieldUuid: target.fieldUuid,
                    clientX: info.startX,
                    clientY: info.startY,
                });
                return false;
            },
        });
        session = {
            detach() {
                drag?.detach?.();
                longPress?.detach?.();
            },
        };
    };

    if (attach) start(attach);
    else {
        import(HELPER_URL.href).then((helper) => {
            if (session !== false) start(helper.attach);
        }).catch(() => { /* no pointer helper: the designer keeps its keyboard path */ });
    }

    return {
        probe: () => 'pdf-template-designer-pointer',
        detach() {
            if (session && session !== false) session.detach();
            session = false;
        },
        lastCommit: () => committed,
    };
}

/**
 * One palette drag, attached to the workspace, for every pointer type. A press that does not move
 * stays a tap. Native click is the only activation: a tap must not also call back into .NET, or one
 * press arms draw mode twice and the second press places two fields. A press that moves commits
 * once, at the page under the pointer. An off-page release or a cancel places nothing.
 * @param {Element} workspace
 * @param {{ onDrop?: (commit: object) => void, onCancel?: () => void, attach?: Function }} [options]
 * @returns {{ detach: () => void }}
 */
export function attachPaletteDrag(workspace, options = {}) {
    const attach = typeof options.attach === 'function' ? options.attach : null;
    let session = null;
    let placed = false;
    const drops = [];

    const commit = (info) => {
        const hit = workspace.ownerDocument?.elementFromPoint?.(info.x, info.y)
            ?? (typeof document !== 'undefined' ? document.elementFromPoint?.(info.x, info.y) : null);
        const surface = hit?.closest?.('.tm-pdf-template-designer__page-surface');
        if (!surface || !workspace.contains?.(surface)) return;
        const rect = surface.getBoundingClientRect?.();
        if (!(rect?.width > 0) || !(rect?.height > 0)) return;
        const item = info.origin?.closest?.('.tm-pdf-template-designer__palette-item')
            ?? info.target?.closest?.('.tm-pdf-template-designer__palette-item');
        const payload = {
            pageKey: surface.getAttribute?.('data-page-key') ?? null,
            x: (info.x - rect.left) / rect.width,
            y: (info.y - rect.top) / rect.height,
            type: item?.getAttribute?.('data-field-type') ?? null,
        };
        drops.push(payload);
        if (placed) return;
        placed = true;
        options.onDrop?.(payload);
    };

    const markDragging = (on) => {
        const root = workspace.closest?.('.tm-pdf-template-designer') ?? workspace;
        root.classList?.toggle?.('tm-pdf-template-designer--dragging', on);
    };

    // Registered BEFORE the shared session. A capture listener added earlier runs later, so this
    // one runs after the session has recorded the press and then stops the bubble. The page
    // surface listens in the bubble phase; without this stop a palette drag also draws or selects.
    const stopPagePress = (event) => {
        const target = event.target;
        if (!target?.closest?.('.tm-pdf-template-designer__palette-item')) return;
        if (!workspace.contains?.(target)) return;
        event.stopPropagation?.();
    };
    workspace.addEventListener?.('pointerdown', stopPagePress, true);

    const start = (attachImpl) => {
        const drag = attachImpl(workspace, {
            selector: '.tm-pdf-template-designer__palette-item',
            threshold: 4,
            listenerCapture: true,
            captureOn: 'start',
            touchAction: null,
            pointerTypes: ['mouse', 'pen', 'touch'],
            onPress(info) {
                placed = false;
                const target = info.event?.target;
                if (!workspace.contains?.(target) && !target?.closest?.('.tm-pdf-template-designer__palette-item')) {
                    return false;
                }
                markDragging(true);
            },
            onRelease(info) {
                markDragging(false);
                if (info.outcome === 'end') {
                    const before = drops.length;
                    commit(info);
                    // Off the page: the pointer never landed on a surface, so nothing is placed.
                    if (drops.length === before) options.onCancel?.();
                    return;
                }
                // A tap is the button's own click. Calling back here double-activates the palette.
                if (info.outcome === 'tap') return;
                options.onCancel?.();
            },
        });
        session = { detach() { drag?.detach?.(); }, drops };
    };

    if (attach) start(attach);
    else {
        import(HELPER_URL.href).then((helper) => {
            if (session !== false) start(helper.attach);
        }).catch(() => { /* the Blazor fallback places the field when the module never attached */ });
    }

    return {
        detach() {
            workspace.removeEventListener?.('pointerdown', stopPagePress, true);
            if (session && session !== false) session.detach();
            session = false;
        },
        drops: () => drops,
    };
}
