// Tempo.Blazor.AI — composer interop.
//
// TmAiComposer owns the draft, the suggestion list and the submit contract in Blazor. This module owns the
// three things a net8 Blazor app cannot express:
//
//   1. The Enter key and the IME guard. `KeyboardEventArgs.IsComposing` exists only on .NET 9+, and Safari
//      reports the Enter that commits a composition as `keyCode === 229`, so the guard belongs on the native
//      keydown. Enter is ALSO the combobox activation while the suggestion listbox is expanded
//      (`aria-expanded="true"`): the C# handler picks the highlighted option, so the module only suppresses
//      the native newline and never submits.
//   2. The autosize of the prompt textarea. The height is measured from the content and clamped to
//      `maxRows * line-height + padding + border`, so a long prompt scrolls inside an 8-row box instead of
//      pushing the send button off the screen. The module re-measures on every input and after a programmatic
//      clear (which raises no `input` event).
//   3. The Web Speech wrapper. `speechSupported()` is the feature detect the component uses to decide whether
//      the microphone control exists at all; `startSpeech` wires one recognizer per textarea and `stopSpeech`
//      (also reached from `detach`) releases it, so no session outlives the component.
//
// The suggestion list is placed from the CARET, not from the textarea's corner: `placeSuggestions` measures
// the caret through a hidden mirror (the standard textarea technique), turns it into two CSS custom properties
// on the listbox and the stylesheet does the rest. Every entry point is null-safe and tolerates a DOM that is
// already gone — a circuit that drops mid-render must not see a throw from here.
//
// No module-level element state beyond two WeakMaps (per textarea / per root), no eval and no inline handler,
// so a host Content-Security-Policy needs no 'unsafe-inline' for this file. Node tests: ai-composer.test.mjs.

/** The default autosize cap when the caller does not pass one. */
const DEFAULT_MAX_ROWS = 8;

/** How much room above the caret the suggestion list may use, in CSS pixels. */
const DEFAULT_LIST_MAX_HEIGHT = 240;

/** The smallest list the placement will shrink to before it stops caring about the viewport. */
const MIN_LIST_HEIGHT = 48;

/** The viewport shrink below which a keyboard is assumed, matching the CSS knob's own threshold. */
const DEFAULT_MIN_INSET = 120;

/** The style properties copied into the caret mirror — everything that affects text layout. */
const MIRROR_STYLES = [
    'fontFamily', 'fontSize', 'fontWeight', 'fontStyle', 'lineHeight', 'letterSpacing',
    'textTransform', 'textIndent', 'paddingTop', 'paddingRight', 'paddingBottom', 'paddingLeft',
    'borderTopWidth', 'borderRightWidth', 'borderBottomWidth', 'borderLeftWidth', 'boxSizing',
    'tabSize', 'textRendering',
];

/** Attached composer state, keyed by the textarea element. */
const composers = new WeakMap();

/** Virtual-keyboard trackers, keyed by the composer root element. */
const viewportTrackers = new WeakMap();

function isFinitePositive(value) {
    return Number.isFinite(value) && value > 0;
}

function toPixels(value) {
    const parsed = Number.parseFloat(value);
    return Number.isFinite(parsed) ? parsed : 0;
}

function readStyle(element) {
    try {
        if (typeof globalThis.getComputedStyle === 'function') {
            return globalThis.getComputedStyle(element) ?? null;
        }
    } catch {
        // A detached element (or a test double) has no computed style — the inline style is the fallback.
    }

    return element?.style ?? null;
}

function invoke(dotNetRef, method, ...values) {
    try {
        const result = dotNetRef?.invokeMethodAsync?.(method, ...values);
        if (result && typeof result.catch === 'function') {
            result.catch(() => {
                // The circuit is gone; there is nobody left to update.
            });
        }
    } catch {
        // Same case, synchronously.
    }
}

function isExpanded(textarea) {
    return textarea?.getAttribute?.('aria-expanded') === 'true';
}

function isImeComposition(event) {
    return event?.isComposing === true || event?.keyCode === 229;
}

/**
 * Whether the document's active element sits inside `root`. The component asks this BEFORE it re-renders the
 * send/stop slot: a generation starting while the reader was on the slot's button removes that button from the
 * DOM, and the focus has to be put back on the replacement instead of dropping to `<body>` (WCAG 2.4.3).
 *
 * @param {HTMLElement} root The slot container.
 * @returns {boolean}
 */
export function isFocusWithin(root) {
    if (!root || typeof root.contains !== 'function') {
        return false;
    }

    try {
        const active = root.ownerDocument?.activeElement ?? null;
        return Boolean(active) && root.contains(active);
    } catch {
        return false;
    }
}

/**
 * Focuses the first element matching `selector` inside `root`, and reports whether anything took the focus.
 * Used for the send/stop slot: the replacement control is the successor of the control the reader just
 * activated, so the focus follows it. A disabled replacement (an empty draft after a send) matches nothing,
 * and the caller falls back to the prompt — a disabled button cannot receive the focus either.
 *
 * @param {HTMLElement} root The slot container.
 * @param {string} [selector] What to focus; defaults to the first enabled button.
 * @returns {boolean} Whether a control inside `root` now holds the focus.
 */
export function focusFirst(root, selector) {
    const query = typeof selector === 'string' && selector ? selector : 'button:not([disabled])';
    if (!root || typeof root.querySelector !== 'function') {
        return false;
    }

    let target = null;
    try {
        target = root.querySelector(query);
    } catch {
        return false;
    }

    if (!target || typeof target.focus !== 'function') {
        return false;
    }

    try {
        target.focus();
    } catch {
        return false;
    }

    return true;
}

/**
 * Whether an Enter keydown is a submit. Mirrors the keydown handler so a caller that already classified the
 * event (a host's custom composer) gets the same answer.
 *
 * @param {KeyboardEvent|{ key?: string, shiftKey?: boolean, altKey?: boolean, ctrlKey?: boolean, metaKey?: boolean, isComposing?: boolean, keyCode?: number }} event
 * @param {boolean} expanded Whether the suggestion listbox is open.
 * @returns {boolean}
 */
export function shouldSubmit(event, expanded) {
    if (event?.key !== 'Enter') {
        return false;
    }

    if (isImeComposition(event)) {
        return false;
    }

    if (event.shiftKey || event.altKey || event.ctrlKey || event.metaKey) {
        return false;
    }

    return !expanded;
}

/**
 * The autosize height for one measurement. Pure, so the clamp is testable without a DOM.
 *
 * @param {{ scrollHeight: number, lineHeight: number, paddingY: number, borderY: number, boxSizing?: string, maxRows?: number }} metrics
 * @returns {number} The height in CSS pixels.
 */
export function nextTextareaHeight(metrics) {
    const scrollHeight = Math.max(0, metrics?.scrollHeight ?? 0);
    const maxRows = metrics?.maxRows ?? 0;
    if (!isFinitePositive(maxRows)) {
        return Math.round(scrollHeight);
    }

    const lineHeight = isFinitePositive(metrics.lineHeight) ? metrics.lineHeight : 21;
    const paddingY = metrics.paddingY ?? 0;
    const borderY = metrics.boxSizing === 'border-box' ? metrics.borderY ?? 0 : 0;
    const cap = Math.round(maxRows * lineHeight + paddingY + borderY);
    return Math.min(Math.round(scrollHeight), cap);
}

/**
 * Where the suggestion listbox goes for a caret. Pure: `placeSuggestions` measures, this decides.
 *
 * @param {{ left?: number, top?: number, bottom?: number }} anchor The caret's viewport rectangle.
 * @param {{ left?: number, bottom?: number }} root The composer root's viewport rectangle.
 * @param {{ maxHeight?: number, viewport?: { top?: number, height?: number } }} [options]
 * @returns {{ inlineStart: number, blockEnd: number, maxBlockSize: number }} The two CSS offsets plus the
 *   list's own height cap.
 */
export function computeCaretPlacement(anchor, root, options) {
    const opts = options ?? {};
    const viewport = opts.viewport ?? { top: 0, height: 600 };
    const maxHeight = opts.maxHeight ?? DEFAULT_LIST_MAX_HEIGHT;
    const gap = 8;

    const inlineStart = Math.max(0, Math.round((anchor?.left ?? 0) - (root?.left ?? 0)));
    const blockEnd = Math.max(0, Math.round((root?.bottom ?? anchor?.bottom ?? 0) - (anchor?.top ?? 0)));
    const available = (anchor?.top ?? 0) - (viewport.top ?? 0) - gap;
    const maxBlockSize = Math.max(MIN_LIST_HEIGHT, Math.min(maxHeight, Math.round(available)));

    return { inlineStart, blockEnd, maxBlockSize };
}

/**
 * The caret's viewport rectangle, measured with a hidden mirror because a textarea exposes no caret geometry.
 * Falls back to the textarea's own rectangle when the DOM is unavailable or the measurement fails.
 */
function caretAnchorRect(textarea) {
    const fallback = () => {
        try {
            const rect = textarea.getBoundingClientRect();
            return { left: rect.left, top: rect.bottom, bottom: rect.bottom };
        } catch {
            return null;
        }
    };

    if (typeof document === 'undefined' || typeof textarea.getBoundingClientRect !== 'function') {
        return fallback();
    }

    try {
        const rect = textarea.getBoundingClientRect();
        const style = readStyle(textarea);
        const mirror = document.createElement('div');
        for (const property of MIRROR_STYLES) {
            const value = style?.[property];
            if (value) {
                mirror.style[property] = value;
            }
        }

        mirror.style.position = 'absolute';
        mirror.style.visibility = 'hidden';
        mirror.style.insetBlockStart = '0';
        mirror.style.insetInlineStart = '0';
        mirror.style.blockSize = 'auto';
        mirror.style.inlineSize = `${rect.width}px`;
        mirror.style.whiteSpace = 'pre-wrap';
        mirror.style.overflowWrap = 'break-word';
        mirror.style.overflow = 'hidden';
        mirror.textContent = (textarea.value ?? '').slice(0, textarea.selectionStart ?? 0);

        const marker = document.createElement('span');
        marker.textContent = '\u200b';
        mirror.appendChild(marker);
        document.body.appendChild(mirror);

        const markerRect = marker.getBoundingClientRect();
        const mirrorRect = mirror.getBoundingClientRect();
        document.body.removeChild(mirror);

        const left = rect.left + (markerRect.left - mirrorRect.left);
        const top = rect.top + (markerRect.top - mirrorRect.top);
        return { left, top, bottom: top + (markerRect.height || 0) };
    } catch {
        return fallback();
    }
}

/**
 * Positions the suggestion listbox above the caret through two CSS custom properties. The stylesheet owns the
 * actual placement (and its fallback when no caret measurement ever arrived).
 *
 * @param {HTMLTextAreaElement} textarea The prompt input.
 * @param {HTMLElement} list The rendered `role="listbox"` element.
 */
export function placeSuggestions(textarea, list) {
    if (!textarea || !list?.style || typeof list.style.setProperty !== 'function') {
        return;
    }

    const anchor = caretAnchorRect(textarea);
    if (!anchor) {
        return;
    }

    let rootRect = null;
    try {
        const root = textarea.closest?.('.tm-ai-composer') ?? textarea.parentElement;
        rootRect = root?.getBoundingClientRect?.() ?? null;
    } catch {
        rootRect = null;
    }

    const viewport = {
        top: 0,
        height: typeof globalThis.innerHeight === 'number' ? globalThis.innerHeight : 600,
    };

    const placement = computeCaretPlacement(anchor, rootRect ?? anchor, { viewport });
    list.style.setProperty('--tm-ai-composer-caret-inline-start', `${placement.inlineStart}px`);
    list.style.setProperty('--tm-ai-composer-caret-block-end', `${placement.blockEnd}px`);
    list.style.setProperty('--tm-ai-composer-list-max-block-size', `${placement.maxBlockSize}px`);
}

/**
 * Re-measures the textarea height, clamped to `maxRows`.
 *
 * @param {HTMLTextAreaElement} textarea The prompt input.
 * @param {number} [maxRows] The row cap; 0 or a negative value leaves the height uncapped.
 */
export function resize(textarea, maxRows) {
    if (!textarea?.style) {
        return;
    }

    const style = readStyle(textarea);
    const lineHeight = toPixels(style?.lineHeight) || toPixels(style?.fontSize) * 1.5 || 21;
    const paddingY = toPixels(style?.paddingTop) + toPixels(style?.paddingBottom);
    const borderY = toPixels(style?.borderTopWidth) + toPixels(style?.borderBottomWidth);
    const cap = isFinitePositive(maxRows) ? maxRows : 0;

    // Resetting first lets scrollHeight report the natural height of the CURRENT content — without it the
    // textarea can only ever grow.
    textarea.style.height = 'auto';
    const scrollHeight = textarea.scrollHeight ?? 0;
    const height = nextTextareaHeight({
        scrollHeight,
        lineHeight,
        paddingY,
        borderY,
        boxSizing: style?.boxSizing,
        maxRows: cap,
    });

    textarea.style.height = `${height}px`;
    textarea.style.overflowY = cap > 0 && scrollHeight > height ? 'auto' : 'hidden';
}

/**
 * Attaches the composer behaviour to one textarea: the Enter/IME guard of the keydown, the autosize and the
 * caret reports that drive the suggestion list. A second attach on the same element replaces the first.
 *
 * @param {HTMLTextAreaElement} textarea The prompt input.
 * @param {{ invokeMethodAsync: Function }} dotNetRef A `DotNetObjectReference<TmAiComposer>`.
 * @param {{ maxRows?: number, submitMethod?: string }} [options] The row cap and the `[JSInvokable]` submit
 *   method (default `OnComposerSubmit`).
 */
export function attach(textarea, dotNetRef, options) {
    if (!textarea || typeof textarea.addEventListener !== 'function') {
        throw new TypeError('ai-composer.js: a composer textarea is required.');
    }

    if (!dotNetRef || typeof dotNetRef.invokeMethodAsync !== 'function') {
        throw new TypeError('ai-composer.js: a DotNetObjectReference is required.');
    }

    detach(textarea);

    const opts = options ?? {};
    const maxRows = isFinitePositive(opts.maxRows) ? opts.maxRows : DEFAULT_MAX_ROWS;
    const submitMethod = typeof opts.submitMethod === 'string' && opts.submitMethod
        ? opts.submitMethod
        : 'OnComposerSubmit';

    const entry = { dotNetRef, lastCaret: -1, resizeFrame: null };

    function reportCaret() {
        const caret = textarea.selectionStart;
        if (typeof caret !== 'number' || caret === entry.lastCaret) {
            return;
        }

        entry.lastCaret = caret;
        invoke(dotNetRef, 'OnCaretChanged', caret);
    }

    function scheduleResize() {
        if (entry.resizeFrame !== null) {
            return;
        }

        if (typeof requestAnimationFrame === 'function') {
            entry.resizeFrame = requestAnimationFrame(() => {
                entry.resizeFrame = null;
                resize(textarea, maxRows);
            });
        } else {
            resize(textarea, maxRows);
        }
    }

    function onKeyDown(event) {
        const expanded = isExpanded(textarea);

        if (event.key === 'Enter') {
            if (expanded) {
                // The C# handler picks the highlighted option. Suppress only the plain Enter's newline;
                // Shift+Enter stays the documented new line even with the list open.
                if (!event.isComposing && event.keyCode !== 229
                    && !event.shiftKey && !event.altKey && !event.ctrlKey && !event.metaKey) {
                    event.preventDefault?.();
                }

                return;
            }

            if (!shouldSubmit(event, false)) {
                return;
            }

            event.preventDefault?.();
            invoke(dotNetRef, submitMethod);
            scheduleResize();
            return;
        }

        if (expanded && (event.key === 'ArrowDown' || event.key === 'ArrowUp')) {
            event.preventDefault?.();
        }
    }

    function onInput() {
        scheduleResize();
        reportCaret();
    }

    entry.onKeyDown = onKeyDown;
    entry.onInput = onInput;
    entry.onCaretEvent = reportCaret;

    textarea.addEventListener('keydown', onKeyDown);
    textarea.addEventListener('input', onInput);
    textarea.addEventListener('click', reportCaret);
    textarea.addEventListener('keyup', reportCaret);
    textarea.addEventListener('select', reportCaret);

    composers.set(textarea, entry);

    // Initial measurement: a pre-filled draft has to render at its content height immediately.
    resize(textarea, maxRows);
}

/**
 * Releases everything `attach` registered on the textarea, including an in-flight speech session.
 *
 * @param {HTMLTextAreaElement} textarea The prompt input.
 */
export function detach(textarea) {
    const entry = composers.get(textarea);
    if (entry) {
        textarea.removeEventListener?.('keydown', entry.onKeyDown);
        textarea.removeEventListener?.('input', entry.onInput);
        textarea.removeEventListener?.('click', entry.onCaretEvent);
        textarea.removeEventListener?.('keyup', entry.onCaretEvent);
        textarea.removeEventListener?.('select', entry.onCaretEvent);

        if (entry.resizeFrame !== null && typeof cancelAnimationFrame === 'function') {
            cancelAnimationFrame(entry.resizeFrame);
        }

        composers.delete(textarea);
    }

    stopSpeech(textarea);
}

/**
 * Whether the browser exposes the Web Speech API (the controlled value of the opt-in voice input).
 *
 * @returns {boolean}
 */
export function speechSupported() {
    try {
        return Boolean(globalThis.SpeechRecognition || globalThis.webkitSpeechRecognition);
    } catch {
        return false;
    }
}

/** In-flight recognizers, keyed by the textarea they were started from. */
const speechSessions = new WeakMap();

/**
 * Starts one speech-recognition session for the textarea. The final transcript, the error and the end are
 * reported back to .NET (`OnSpeechResult`, `OnSpeechError`, `OnSpeechEnded`), so the component owns the draft.
 *
 * @param {HTMLTextAreaElement} textarea The prompt input.
 * @param {{ invokeMethodAsync: Function }} dotNetRef A `DotNetObjectReference<TmAiComposer>`.
 * @param {{ language?: string }} [options] BCP-47 language; defaults to the document language, then en-US.
 * @returns {boolean} Whether the API existed and the session started.
 */
export function startSpeech(textarea, dotNetRef, options) {
    if (!textarea || !dotNetRef) {
        return false;
    }

    stopSpeech(textarea);

    let Recognition = null;
    try {
        Recognition = globalThis.SpeechRecognition || globalThis.webkitSpeechRecognition || null;
    } catch {
        Recognition = null;
    }

    if (!Recognition) {
        return false;
    }

    let recognition;
    try {
        recognition = new Recognition();
    } catch {
        return false;
    }

    const language = options?.language
        || (typeof document !== 'undefined' ? document.documentElement?.lang : null)
        || 'en-US';

    recognition.lang = language;
    recognition.continuous = false;
    recognition.interimResults = false;
    recognition.maxAlternatives = 1;

    const session = { recognition };
    speechSessions.set(textarea, session);

    recognition.addEventListener?.('result', (event) => {
        const text = event?.results?.[0]?.[0]?.transcript ?? '';
        if (text) {
            invoke(dotNetRef, 'OnSpeechResult', text, true);
        }
    });

    recognition.addEventListener?.('error', (event) => {
        invoke(dotNetRef, 'OnSpeechError', event?.error ?? 'unknown');
    });

    recognition.addEventListener?.('end', () => {
        if (speechSessions.get(textarea) === session) {
            speechSessions.delete(textarea);
        }

        invoke(dotNetRef, 'OnSpeechEnded');
    });

    try {
        recognition.start();
    } catch {
        speechSessions.delete(textarea);
        return false;
    }

    return true;
}

/**
 * Stops the textarea's speech session, if one is running.
 *
 * @param {HTMLTextAreaElement} textarea The prompt input.
 * @returns {boolean} Whether a session was stopped.
 */
export function stopSpeech(textarea) {
    const session = textarea ? speechSessions.get(textarea) : null;
    if (!session) {
        return false;
    }

    speechSessions.delete(textarea);
    try {
        session.recognition.stop();
    } catch {
        try {
            session.recognition.abort();
        } catch {
            // Nothing left to release.
        }
    }

    return true;
}

/**
 * Tracks the on-screen keyboard for a sticky composer: `visualViewport` shrinks while the layout viewport
 * (and every sticky containing block) stays put, so the module measures the covered gap and writes it as the
 * RUNTIME knob `--tm-ai-composer-keyboard-inset` plus a `data-tm-keyboard-open` marker on the root. The knob is
 * never declared by the stylesheet — its default lives in the `var()` fallback, so a host override cascades.
 *
 * @param {HTMLElement} root The composer root.
 * @param {{ minInset?: number }} [options] The shrink below which browser chrome is assumed.
 */
export function attachViewportTracker(root, options) {
    if (!root?.style || typeof root.style.setProperty !== 'function') {
        throw new TypeError('ai-composer.js: a composer root element is required.');
    }

    detachViewportTracker(root);

    const minInset = isFinitePositive(options?.minInset) ? options.minInset : DEFAULT_MIN_INSET;
    const viewport = typeof globalThis !== 'undefined' ? globalThis.visualViewport : null;

    let written = null;
    let open = false;

    function currentInset() {
        if (!viewport) {
            return 0;
        }

        if (typeof viewport.scale === 'number' && Math.abs(viewport.scale - 1) > 0.01) {
            return written ?? 0;
        }

        if ((viewport.height || 0) < 2 || (viewport.width || 0) < 2) {
            return 0;
        }

        const raw = Math.max(0, (globalThis.innerHeight || 0) - ((viewport.height || 0) + (viewport.offsetTop || 0)));
        return raw >= minInset ? Math.round(raw) : 0;
    }

    function apply() {
        const inset = currentInset();
        const nextOpen = inset > 0;
        if (inset === written && nextOpen === open) {
            return;
        }

        written = inset;
        open = nextOpen;

        if (open) {
            root.style.setProperty('--tm-ai-composer-keyboard-inset', `${inset}px`);
            root.setAttribute?.('data-tm-keyboard-open', '');
        } else {
            root.style.removeProperty('--tm-ai-composer-keyboard-inset');
            root.removeAttribute?.('data-tm-keyboard-open');
        }
    }

    const tracker = { apply };
    viewportTrackers.set(root, tracker);

    if (viewport?.addEventListener) {
        viewport.addEventListener('resize', apply, { passive: true });
        viewport.addEventListener('scroll', apply, { passive: true });
    }

    globalThis.addEventListener?.('resize', apply, { passive: true });

    tracker.remove = () => {
        viewport?.removeEventListener?.('resize', apply);
        viewport?.removeEventListener?.('scroll', apply);
        globalThis.removeEventListener?.('resize', apply);
    };

    apply();
}

/**
 * Releases the virtual-keyboard tracker of a composer root and removes the runtime knob it wrote.
 *
 * @param {HTMLElement} root The composer root.
 */
export function detachViewportTracker(root) {
    const tracker = root ? viewportTrackers.get(root) : null;
    if (!tracker) {
        return;
    }

    tracker.remove?.();
    viewportTrackers.delete(root);
    root.style?.removeProperty?.('--tm-ai-composer-keyboard-inset');
    root.removeAttribute?.('data-tm-keyboard-open');
}
