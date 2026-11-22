// Input handling for TmTimeSelector.
//
// Blazor cannot intercept keydown before Tab is swallowed, run a typeahead buffer, auto-repeat a
// native <button> hold, or measure visualViewport without a round-trip, so this module owns those
// DOM events. The component owns the model and exposes [JSInvokable] entry points; the module
// calls them through the DotNetObjectReference passed to attach():
//
//   OnKeyStepAsync(segment, delta, announce)  — arrow / PageUp / PageDown / auto-repeat
//   OnTypeaheadAsync(segment, value, advance) — digit buffer commit (and a/p, Backspace)
//   OnHomeEndAsync(segment, home)             — Home / End on a spinbutton
//   OnViewportChanged(width, height)          — measured inline-size + layout height
//   OnKeyboardChanged(open)                   — software-keyboard heuristic
//
// preventDefault runs only for handled keys; Tab is never swallowed. Auto-repeat starts after
// 400 ms, then 8/s, and after 1.5 s jumps 5× the step; reduced motion does not affect it (it is
// not an animation). pointercancel / pointerup / blur / detach stop the repeat. Fit height is
// never the root block-size: a sheet uses 85 % of visualViewport.height capped by
// --tm-sheet-viewport; inline uses innerHeight. Keyboard-open is: the focused node is editable
// AND visualViewport.height < 0.6 × the max height seen since attach.
//
// State is one session per root in a WeakMap. attach() is idempotent (re-attach detaches first).
// Node tests: __tests__/tm-time-selector.test.mjs.

const sessions = new WeakMap();

const TYPEAHEAD_MS = 1000;
const REPEAT_START_MS = 400;
const REPEAT_INTERVAL_MS = 125; // 8/s
const REPEAT_ACCEL_AFTER_MS = 1500;
const KEYBOARD_RATIO = 0.6;

function win() {
    return globalThis.window;
}

function doc() {
    return globalThis.document ?? win()?.document;
}

function invoke(dotNetRef, method, ...args) {
    try {
        const result = dotNetRef?.invokeMethodAsync?.(method, ...args);
        if (result && typeof result.catch === 'function') {
            result.catch(() => { });
        }
    } catch {
        // Dropped circuit must not surface as an unhandled error.
    }
}

function on(session, target, type, handler, options, bucket) {
    target?.addEventListener?.(type, handler, options);
    (bucket ?? session.listeners).push([target, type, handler, options]);
}

function spins(root) {
    return Array.from(root?.querySelectorAll?.('[data-tm-time-segment]') ?? []);
}

function steps(root) {
    return Array.from(root?.querySelectorAll?.('[data-tm-time-step]') ?? []);
}

function segmentOf(el) {
    return el?.getAttribute?.('data-tm-time-segment')
        ?? el?.getAttribute?.('data-tm-time-step')
        ?? '';
}

function hourCycle(root) {
    const raw = root?.getAttribute?.('data-tm-hour-cycle')
        ?? root?.dataset?.tmHourCycle
        ?? '';
    return raw.toLowerCase() === 'h12' ? 'h12' : 'h23';
}

function isSpin(el) {
    return !!el?.getAttribute?.('data-tm-time-segment');
}

function isEditable(el) {
    if (!el) {
        return false;
    }

    const name = (el.tagName ?? el.nodeName ?? '').toString().toUpperCase();
    if (name === 'INPUT' || name === 'TEXTAREA' || name === 'SELECT') {
        return !el.disabled && !el.readOnly;
    }

    return !!el.isContentEditable;
}

function nextSegment(current, root) {
    const all = spins(root).map(segmentOf);
    const i = all.indexOf(current);
    if (i < 0 || i >= all.length - 1) {
        return current;
    }

    return all[i + 1];
}

function prevSegment(current, root) {
    const all = spins(root).map(segmentOf);
    const i = all.indexOf(current);
    if (i <= 0) {
        return current;
    }

    return all[i - 1];
}

function focusSegment(root, slug) {
    const el = spins(root).find((s) => segmentOf(s) === slug);
    if (!el) {
        return;
    }

    try {
        el.focus?.();
    } catch {
        // Detached node.
    }

    try {
        el.scrollIntoView?.({ block: 'nearest' });
    } catch {
        // No scroller.
    }
}

function clearTypeahead(session) {
    if (session.typeaheadTimer) {
        clearTimeout(session.typeaheadTimer);
        session.typeaheadTimer = 0;
    }

    session.buffer = '';
    session.bufferSegment = null;
}

function commitTypeahead(session, advance) {
    const segment = session.bufferSegment;
    const raw = session.buffer;
    clearTypeahead(session);
    if (!segment || raw === '') {
        return;
    }

    invoke(session.dotNetRef, 'OnTypeaheadAsync', segment, Number.parseInt(raw, 10), !!advance);
    if (advance) {
        focusSegment(session.root, nextSegment(segment, session.root));
    }
}

function autoAdvanceDigit(session, segment, digit) {
    const cycle = hourCycle(session.root);
    if (segment === 'hours') {
        if (cycle === 'h12') {
            return digit >= 2 && digit <= 9;
        }

        return digit >= 3 && digit <= 9;
    }

    if (segment === 'minutes' || segment === 'seconds') {
        return digit >= 6 && digit <= 9;
    }

    return false;
}

function onDigit(session, segment, digit) {
    if (session.bufferSegment && session.bufferSegment !== segment) {
        commitTypeahead(session, false);
    }

    session.bufferSegment = segment;
    session.buffer = `${session.buffer ?? ''}${digit}`;

    if (session.buffer.length >= 2) {
        commitTypeahead(session, true);
        return;
    }

    if (autoAdvanceDigit(session, segment, digit)) {
        commitTypeahead(session, true);
        return;
    }

    session.typeaheadTimer = setTimeout(() => commitTypeahead(session, false), TYPEAHEAD_MS);
}

function pageDelta(segment, sign) {
    if (segment === 'hours') {
        return sign * 3;
    }

    return sign * 15;
}

function isGridCell(el) {
    return !!el?.closest?.('[data-tm-time-grid-cell]');
}

function handleKey(session, event) {
    const target = event.target;
    if (isGridCell(target)) {
        const gridKey = event.key;
        if (gridKey === 'ArrowUp' || gridKey === 'ArrowDown'
            || gridKey === 'ArrowLeft' || gridKey === 'ArrowRight'
            || gridKey === 'Home' || gridKey === 'End') {
            event.preventDefault?.();
        }

        return;
    }

    if (!isSpin(target)) {
        return;
    }

    const key = event.key;
    if (key === 'Tab') {
        return;
    }

    const segment = segmentOf(target);
    const ctrl = event.ctrlKey || event.metaKey || event.altKey;

    if (key === 'ArrowUp' || key === 'ArrowDown') {
        event.preventDefault?.();
        const delta = key === 'ArrowUp' ? 1 : -1;
        invoke(session.dotNetRef, 'OnKeyStepAsync', segment, delta, false);
        return;
    }

    if (key === 'PageUp' || key === 'PageDown') {
        event.preventDefault?.();
        const sign = key === 'PageUp' ? 1 : -1;
        invoke(session.dotNetRef, 'OnKeyStepAsync', segment, pageDelta(segment, sign), false);
        return;
    }

    if (key === 'Home' || key === 'End') {
        event.preventDefault?.();
        invoke(session.dotNetRef, 'OnHomeEndAsync', segment, key === 'Home');
        return;
    }

    if (key === 'ArrowLeft' || key === 'ArrowRight') {
        event.preventDefault?.();
        const next = key === 'ArrowRight'
            ? nextSegment(segment, session.root)
            : prevSegment(segment, session.root);
        focusSegment(session.root, next);
        return;
    }

    if (key === ':' || key === '.' || key === 'h' || key === 'H') {
        event.preventDefault?.();
        if (session.buffer) {
            commitTypeahead(session, true);
        } else {
            focusSegment(session.root, nextSegment(segment, session.root));
        }

        return;
    }

    if ((key === 'a' || key === 'A' || key === 'p' || key === 'P') && !ctrl) {
        event.preventDefault?.();
        invoke(session.dotNetRef, 'OnTypeaheadAsync', 'period', key.toLowerCase() === 'p' ? 1 : 0, false);
        return;
    }

    if (key === 'Backspace') {
        event.preventDefault?.();
        clearTypeahead(session);
        invoke(session.dotNetRef, 'OnTypeaheadAsync', segment, 0, false);
        return;
    }

    if (key.length === 1 && key >= '0' && key <= '9' && !ctrl) {
        event.preventDefault?.();
        onDigit(session, segment, Number.parseInt(key, 10));
    }
}

function stopRepeat(session) {
    if (session.repeatTimer) {
        clearTimeout(session.repeatTimer);
        session.repeatTimer = 0;
    }

    if (session.repeatInterval) {
        clearInterval(session.repeatInterval);
        session.repeatInterval = 0;
    }
}

function startRepeat(session, segment, delta) {
    const started = session.now();
    const tick = () => {
        const elapsed = session.now() - started;
        const step = elapsed >= REPEAT_ACCEL_AFTER_MS ? delta * 5 : delta;
        invoke(session.dotNetRef, 'OnKeyStepAsync', segment, step, false);
    };

    session.repeatInterval = setInterval(tick, REPEAT_INTERVAL_MS);
    tick();
}

function bindSteps(session) {
    for (const btn of steps(session.root)) {
        const segment = segmentOf(btn);
        const delta = Number.parseInt(btn.getAttribute?.('data-tm-time-delta') ?? '1', 10) || 1;

        const onDown = (event) => {
            if (event.button != null && event.button !== 0) {
                return;
            }

            session.repeatPointer = true;
            session.repeating = false;
            session.suppressClick = false;
            session.repeatOrigin = session.now();
            stopRepeat(session);
            session.repeatTimer = setTimeout(() => {
                session.suppressClick = true;
                session.repeating = true;
                startRepeat(session, segment, delta);
            }, REPEAT_START_MS);
        };

        const onUp = () => {
            const held = session.repeating;
            if (held) {
                session.suppressClick = true;
                invoke(session.dotNetRef, 'OnKeyStepAsync', segment, 0, true);
            }

            session.repeatPointer = false;
            session.repeating = false;
            stopRepeat(session);
        };

        const onClick = (event) => {
            if (!session.suppressClick) {
                return;
            }

            event.preventDefault?.();
            event.stopPropagation?.();
            session.suppressClick = false;
        };

        const bucket = session.stepListeners;
        on(session, btn, 'pointerdown', onDown, undefined, bucket);
        on(session, btn, 'pointerup', onUp, undefined, bucket);
        on(session, btn, 'pointercancel', onUp, undefined, bucket);
        on(session, btn, 'blur', onUp, undefined, bucket);
        on(session, btn, 'click', onClick, true, bucket);
    }
}

function parsePx(raw) {
    const t = (raw ?? '').toString().trim();
    if (!t.toLowerCase().endsWith('px')) {
        return 0;
    }

    const n = Number.parseFloat(t);
    return Number.isFinite(n) && n > 0 ? n : 0;
}

function layoutHeight(root) {
    const w = win();
    const vv = w?.visualViewport?.height;
    const inner = w?.innerHeight ?? 0;
    const sheet = root?.closest?.('.tm-sheet');
    if (sheet) {
        let cap = 0;
        try {
            const raw = w?.getComputedStyle?.(sheet)?.getPropertyValue?.('--tm-sheet-viewport') ?? '';
            cap = parsePx(raw);
        } catch {
            cap = 0;
        }

        const vvH = Number.isFinite(vv) ? vv : inner;
        const eightyFive = 0.85 * vvH;
        return cap > 0 ? Math.min(eightyFive, cap) : eightyFive;
    }

    return inner;
}

function inlineSize(root) {
    const rect = root?.getBoundingClientRect?.();
    if (rect && Number.isFinite(rect.width) && rect.width > 0) {
        return rect.width;
    }

    return root?.clientWidth ?? 0;
}

function contentInlineSize(root) {
    const width = root?.clientWidth ?? 0;
    if (!(width > 0)) {
        return inlineSize(root);
    }

    let pad = 0;
    try {
        const style = win()?.getComputedStyle?.(root);
        pad = (Number.parseFloat(style?.paddingLeft) || 0)
            + (Number.parseFloat(style?.paddingRight) || 0);
    } catch {
        pad = 0;
    }

    return Math.max(0, width - pad);
}

function reportViewport(session) {
    const width = inlineSize(session.root);
    const inner = contentInlineSize(session.root);
    const height = layoutHeight(session.root);
    if (width <= 0 || height <= 0) {
        return;
    }

    if (session.lastWidth === width && session.lastHeight === height && session.lastInner === inner) {
        return;
    }

    session.lastWidth = width;
    session.lastHeight = height;
    session.lastInner = inner;
    invoke(session.dotNetRef, 'OnViewportChanged', width, height, inner);
}

function reportKeyboard(session) {
    const w = win();
    const vvH = w?.visualViewport?.height;
    if (Number.isFinite(vvH) && vvH > 0) {
        session.maxVv = Math.max(session.maxVv ?? 0, vvH);
    }

    const open = isEditable(doc()?.activeElement)
        && Number.isFinite(vvH)
        && session.maxVv > 0
        && vvH < KEYBOARD_RATIO * session.maxVv;

    if (session.keyboardOpen === open) {
        return;
    }

    session.keyboardOpen = open;
    invoke(session.dotNetRef, 'OnKeyboardChanged', open);
}

function watchViewport(session) {
    const w = win();
    if (!w) {
        return;
    }

    const onResize = () => {
        reportKeyboard(session);
        if (!session.keyboardOpen) {
            reportViewport(session);
        }
    };

    on(session, w, 'resize', onResize);
    if (w.visualViewport) {
        on(session, w.visualViewport, 'resize', onResize);
        on(session, w.visualViewport, 'scroll', onResize);
    }

    const d = doc();
    if (d) {
        on(session, d, 'focusin', () => reportKeyboard(session));
        on(session, d, 'focusout', () => reportKeyboard(session));
    }

    session.maxVv = w.visualViewport?.height ?? w.innerHeight ?? 0;
    reportViewport(session);
    reportKeyboard(session);
}

function bind(session) {
    on(session, session.root, 'keydown', (event) => handleKey(session, event));
    bindSteps(session);
}

/**
 * Attaches listeners to `root`. Idempotent: a second call detaches the previous session first.
 * @param {Element} root
 * @param {{ invokeMethodAsync: Function }} dotNetRef
 */
export function attach(root, dotNetRef) {
    if (!root || !dotNetRef) {
        return;
    }

    detach(root);
    const session = {
        root,
        dotNetRef,
        listeners: [],
        stepListeners: [],
        buffer: '',
        bufferSegment: null,
        typeaheadTimer: 0,
        repeatTimer: 0,
        repeatInterval: 0,
        repeatOrigin: 0,
        suppressClick: false,
        keyboardOpen: false,
        maxVv: 0,
        lastWidth: undefined,
        lastHeight: undefined,
        lastInner: undefined,
        repeating: false,
        now: () => Date.now(),
    };
    sessions.set(root, session);
    bind(session);
    watchViewport(session);
}

/** Removes every listener the attach() call registered on this root. */
export function detach(root) {
    const session = sessions.get(root);
    if (!session) {
        return;
    }

    sessions.delete(root);
    clearTypeahead(session);
    stopRepeat(session);
    for (const entry of [...session.listeners, ...session.stepListeners]) {
        const [target, type, handler, options] = entry;
        target?.removeEventListener?.(type, handler, options);
    }
}

/** Re-binds stepper listeners after a Blazor re-render. Viewport / keyboard watchers stay. */
export function update(root) {
    const session = sessions.get(root);
    if (!session) {
        return;
    }

    for (const [target, type, handler, options] of session.stepListeners) {
        target?.removeEventListener?.(type, handler, options);
    }

    session.stepListeners = [];
    bindSteps(session);
}
