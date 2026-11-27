export function createHiddenInputBridge(options = {}) {
    const doc = options.document;
    if (!doc || typeof doc.createElement !== 'function') {
        throw new Error('CanvasDocumentEngine input bridge requires a DOM-like document.');
    }

    const input = doc.createElement('textarea');
    input.className = 'tm-document-canvas-hidden-input';
    if (options.id) {
        // A stable id lets a host name the input as the restore target of a dialog (TmModal RestoreFocusTargetId).
        input.id = options.id;
    }
    input.setAttribute('data-testid', 'document-canvas-hidden-input');
    input.setAttribute('role', 'textbox');
    input.setAttribute('aria-multiline', 'true');
    input.setAttribute('aria-label', options.ariaLabel || '');
    if (options.controlsId) {
        input.setAttribute('aria-controls', options.controlsId);
    }

    if (options.describedById) {
        input.setAttribute('aria-describedby', options.describedById);
    }

    input.setAttribute('autocomplete', 'off');
    input.setAttribute('autocorrect', 'off');
    input.setAttribute('autocapitalize', 'off');
    input.setAttribute('spellcheck', 'false');
    input.style.position = 'fixed';
    input.style.left = '-10000px';
    input.style.top = '0';
    input.style.width = '1px';
    input.style.height = '1px';
    input.style.opacity = '0';
    input.style.pointerEvents = 'none';

    const listeners = new Set();

    function onBeforeInput(event) {
        const payload = {
            inputType: event.inputType || '',
            data: event.data || '',
            isComposing: !!event.isComposing,
        };
        for (const listener of listeners) {
            listener(payload, event);
        }
    }

    input.addEventListener?.('beforeinput', onBeforeInput);

    function subscribe(listener) {
        if (typeof listener !== 'function') {
            throw new Error('Hidden input bridge listener must be a function.');
        }

        listeners.add(listener);
        return () => listeners.delete(listener);
    }

    function focus() {
        input.focus?.({ preventScroll: true });
    }

    function destroy() {
        input.removeEventListener?.('beforeinput', onBeforeInput);
        listeners.clear();
        if (input.parentNode) {
            input.parentNode.removeChild(input);
        }
    }

    return {
        input,
        subscribe,
        focus,
        destroy,
    };
}
