import { attachDesignerDrag, attachPaletteDrag } from './pdf-template-designer-pointer.js';

/**
 * Attaches the designer's move/resize session and the palette drag to the workspace element.
 * Both sessions own their listeners: detach removes every one of them.
 * @param {Element} workspace
 * @param {DotNetObject} dotNetRef
 * @returns {{ detach: () => void, probe: () => string }}
 */
export function attachFieldDrag(workspace, dotNetRef) {
    // Palette items live in the shell's left region, outside the workspace. The drop still commits
    // only when the pointer ends on this workspace's page surface.
    const paletteRoot = workspace.closest?.('.tm-pdf-template-designer') ?? workspace;
    // Blazor re-enters OnAfterRenderAsync while the first attach is still in flight, and each entry
    // passes the same workspace. One workspace owns one session; a repeat returns the live one.
    if (workspace.__tmFieldDrag) {
        return workspace.__tmFieldDrag;
    }
    const fieldDrag = attachDesignerDrag(workspace, {
        selector: '.tm-signing-field, .tm-signing-field__resize-handle',
        onCommit(commit) {
            dotNetRef.invokeMethodAsync(
                'CommitFieldDrag',
                commit.fieldUuid,
                commit.handle,
                commit.areaUuid,
                commit.normalizedDx,
                commit.normalizedDy)
                .catch(() => { /* the designer keeps the last committed geometry */ });
        },
        onContextMenu(info) {
            dotNetRef.invokeMethodAsync('OpenFieldContextMenu', info.fieldUuid, info.clientX, info.clientY);
        },
    });
    const paletteDrag = attachPaletteDrag(paletteRoot, {
        onDrop(commit) {
            dotNetRef.invokeMethodAsync('CommitPaletteDrop', commit.pageKey, commit.x, commit.y, commit.type);
        },
        onCancel() {
            dotNetRef.invokeMethodAsync('PaletteDragCancelled');
        },
    });
    const onKeyDown = (event) => handleDesignerKeyDown(workspace, event);
    // Capture: a form control (Detect, the culture select, an inspector input) keeps its own keys.
    // stopPropagation here means Blazor's bubble handler on the workspace never sees that keydown,
    // so Delete with Detect focused cannot delete the selection. The listener does not preventDefault
    // for those targets — Enter still activates the button.
    workspace.addEventListener?.('keydown', onKeyDown, true);
    const session = {
        probe: () => fieldDrag.probe(),
        detach() {
            if (workspace.__tmFieldDrag === session) workspace.__tmFieldDrag = null;
            workspace.removeEventListener?.('keydown', onKeyDown, true);
            fieldDrag.detach();
            paletteDrag.detach();
        },
    };
    workspace.__tmFieldDrag = session;
    return session;
}

/**
 * A key the workspace owns. Form controls keep their own keys: Enter must activate Detect, and Tab
 * must leave a focused field. preventDefault runs only for a key this listener handles.
 * @param {Element} workspace
 * @param {KeyboardEvent} event
 */
export function handleDesignerKeyDown(workspace, event) {
    // KeyboardEventArgs.Type is the key event type, never the target tag, so a C# guard on it
    // cannot tell a form control from the workspace. This listener is the guard: it stops the
    // bubble before Blazor's handler runs.
    if (isFormControl(event.target)) {
        event.stopPropagation?.();
        return;
    }
    const key = event.key;
    const modifier = event.ctrlKey || event.metaKey;
    if ((key === 'ContextMenu' || (key === 'F10' && event.shiftKey)) && event.target?.closest?.('[data-field-uuid]')) {
        event.preventDefault?.();
        return;
    }
    const handled = key === 'Delete'
        || (modifier && (key === 'c' || key === 'C' || key === 'v' || key === 'V'))
        || key === 'ArrowLeft' || key === 'ArrowRight' || key === 'ArrowUp' || key === 'ArrowDown';
    if (!handled) return;
    event.preventDefault?.();
}

/** @param {EventTarget|null} target */
export function isFormControl(target) {
    const name = target?.tagName?.toLowerCase?.();
    return name === 'input' || name === 'button' || name === 'select' || name === 'textarea'
        || target?.isContentEditable === true;
}

/**
 * The page surface a gesture belongs to. A page key measures that page's own surface — the first box
 * that merely contains the point can belong to a neighbouring page the pointer is also over.
 * @param {number} clientX
 * @param {number} clientY
 * @param {string} [pageKey]
 */
export function getSurfaceRect(clientX, clientY, pageKey) {
    if (pageKey) {
        const escaped = typeof CSS !== 'undefined' && typeof CSS.escape === 'function'
            ? CSS.escape(pageKey)
            : pageKey.replace(/["\\]/g, '\\$&');
        const owned = document.querySelector?.(`.tm-pdf-template-designer__page-surface[data-page-key="${escaped}"]`);
        const ownedRect = getElementRect(owned);
        if (ownedRect.width > 0 && ownedRect.height > 0) {
            return ownedRect;
        }
    }

    return getSurfaceRectAtPoint(clientX, clientY);
}

function getSurfaceRectAtPoint(clientX, clientY) {
    const hit = document.elementFromPoint?.(clientX, clientY);
    const surfaces = document.querySelectorAll?.('.tm-pdf-template-designer__page-surface') ?? [];
    for (const candidate of surfaces) {
        const rect = candidate.getBoundingClientRect?.();
        if (rect && clientX >= rect.left && clientX <= rect.right && clientY >= rect.top && clientY <= rect.bottom) {
            return getElementRect(candidate);
        }
    }

    const surface = hit?.closest?.('.tm-pdf-template-designer__page-surface') ?? hit;
    return getElementRect(surface);
}

/**
 * The origin a `position: fixed` menu is resolved against. The shell root is the size container, so a
 * fixed descendant is positioned against that root, not the viewport and not the workspace.
 * @param {Element} element
 */
export function getFixedOrigin(element) {
    const root = element?.closest?.('.tm-pdf-template-designer') ?? element;
    if (!root || typeof root.getBoundingClientRect !== 'function') {
        return { left: 0, top: 0 };
    }

    const probe = root.ownerDocument.createElement('div');
    probe.style.position = 'fixed';
    probe.style.left = '0';
    probe.style.top = '0';
    root.appendChild(probe);
    const rect = probe.getBoundingClientRect();
    probe.remove();
    return { left: rect.left, top: rect.top };
}

/**
 * @param {Element|null} element
 */
export function getElementRect(element) {
    if (!element || typeof element.getBoundingClientRect !== 'function') {
        return { left: 0, top: 0, width: 0, height: 0 };
    }

    const rect = element.getBoundingClientRect();
    return {
        left: rect.left,
        top: rect.top,
        width: rect.width,
        height: rect.height,
    };
}
