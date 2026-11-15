// Tempo.Blazor pin on top (ES module) — the PUBLIC front door of the pinned-on-top registry.
//
// Keeps a host element above every Tempo top-layer surface (TmModal, TmDialog, a modal TmDrawer,
// sheets, TmOverlayPanel popovers, the command palette, …), including surfaces opened AFTER the
// element appeared. Decision TOP-LAYER-PIN, contract: docs/top-layer-pin.md.
//
//   <div id="blazor-error-ui" data-tm-pin-on-top data-tm-pin-priority="100">…</div>
//   <script type="module" src="_content/Tempo.Blazor/js/tm-pin-on-top.js"></script>
//
// or, from code: `pin(element, { priority })` / `unpin(element)` (TopLayerPinService and
// TmPinnedOnTop call these). Importing the module starts it (idempotently): one scan of
// [data-tm-pin-on-top] once the DOM is parsed, then one MutationObserver on the document.
//
// RULES (docs/top-layer-pin.md §4):
// - VISIBILITY IS THE SIGNAL. The host keeps hiding and showing its element the way it always
//   did (inline display, a class, the hidden attribute). Visible = connected, no `hidden`
//   attribute, computed display other than `none`.
// - THE MODULE OWNS `popover`. An observed pin carries popover="manual" only while it is
//   promoted: on a CLOSED popover the UA rule [popover]:not(:popover-open){display:none} would
//   force display:none and hide the very signal the module reads. Markup that already carries
//   `popover` is EXPLICIT: the host shows/hides it, the module only registers it (its signal is
//   :popover-open) and never removes the attribute.
// - NO OBSERVER LOOP. The observer does not watch `popover` or `data-tm-inert-exempt` (the only
//   attributes the module writes), showPopover/hidePopover mutate no attribute, every pass ends
//   with takeRecords(), and mutations are coalesced into ONE pass per animation frame. A record
//   that touches no pin, no pin candidate and no dialog schedules nothing.
// - NATIVE MODAL <dialog>. While `dialog:modal` matches, a pin that becomes visible is NOT
//   promoted (everything outside the dialog is inert — a pin above it would cover it while being
//   unusable); it paints in its own z-index band. A capture-phase `close` listener and the
//   dialog's `open` attribute (dialog.open = false, removal) re-run the pass afterwards, so the pin
//   never stays lost under a closed dialog.
// - INERT. A pinned element gets data-tm-inert-exempt (removed again only when the module added
//   it), so a Tempo modal trap leaves it reachable. Placement rule: pin a layout-level sibling.
// - NO POPOVER API (or no DOM): registering is a no-op and the element keeps its z-index band.

import { pinRoot, unpinRoot, promote, demote, isNativeModalOpen, syncPinnedRoots } from './tm-top-layer.js';

const SELECTOR = '[data-tm-pin-on-top]';
const OBSERVED_ATTRIBUTES = ['style', 'class', 'hidden', 'data-tm-pin-on-top', 'data-tm-pin-priority', 'open'];

// ONE state per document, even when two copies of this module load (a <script type="module"> with
// the plain URL and the IJSRuntime import that TopLayerPinService issues, which an import map can
// rewrite to the fingerprinted URL). Two copies with module-level state would run two observers
// and two pin sets that fight over the same popover attribute. The state lives on globalThis.
//   entries: element -> { api, apiPriority, attr, explicit, addedPopover, addedExempt }
const STATE_KEY = '__tmPinOnTop_v1';
const state = globalThis[STATE_KEY] ??= {
    entries: new Map(),
    env: null,
    started: false,
    observer: null,
    frameScheduled: false,
    onClose: null,
    onReady: null,
};
const entries = state.entries;

function defaultEnv() {
    const g = globalThis;
    return {
        document: g.document,
        MutationObserver: g.MutationObserver,
        getComputedStyle: typeof g.getComputedStyle === 'function' ? g.getComputedStyle.bind(g) : null,
        requestAnimationFrame: typeof g.requestAnimationFrame === 'function'
            ? g.requestAnimationFrame.bind(g)
            : (fn) => setTimeout(fn, 16),
    };
}

function isElement(el) {
    return !!el && typeof el === 'object' && typeof el.hasAttribute === 'function'
        && typeof el.setAttribute === 'function';
}

function supportsPopover(el) {
    return typeof el.showPopover === 'function' && typeof el.hidePopover === 'function';
}

function isOpen(el) {
    try { return el.matches(':popover-open'); }
    catch { return false; }
}

function parsePriority(value) {
    const n = Number.parseInt(value ?? '', 10);
    return Number.isFinite(n) ? n : 0;
}

function priorityOf(el, entry) {
    if (el.hasAttribute('data-tm-pin-priority')) return parsePriority(el.getAttribute('data-tm-pin-priority'));
    return entry.apiPriority;
}

function isVisible(el) {
    if (!el.isConnected || el.hasAttribute('hidden')) return false;
    let display = null;
    try {
        display = state.env?.getComputedStyle ? state.env.getComputedStyle(el)?.display : el.style?.display;
    }
    catch {
        display = null;
    }
    return display !== 'none';
}

function ensureEntry(el) {
    let entry = entries.get(el);
    if (entry) return entry;
    if (!supportsPopover(el)) return null;   // no Popover API: a no-op, the element keeps its band
    entry = {
        api: false,
        apiPriority: 0,
        attr: false,
        explicit: el.hasAttribute('popover'),
        addedPopover: false,
        addedExempt: false,
    };
    if (!el.hasAttribute('data-tm-inert-exempt')) {
        el.setAttribute('data-tm-inert-exempt', '');
        entry.addedExempt = true;
    }
    entries.set(el, entry);
    return entry;
}

function release(el, entry) {
    entries.delete(el);
    unpinRoot(el);
    if (!el.isConnected) return;   // detached: the browser already hid it, nothing to hand back
    if (entry.addedPopover) {
        demote(el);
        el.removeAttribute('popover');
    }
    if (entry.addedExempt) el.removeAttribute('data-tm-inert-exempt');
}

function syncObserved(el, entry, nativeModal) {
    // A promoted pin that something else closed is a plain element again before visibility is read.
    if (entry.addedPopover && !isOpen(el)) {
        el.removeAttribute('popover');
        entry.addedPopover = false;
    }
    const visible = isVisible(el);
    if (!visible) {
        if (entry.addedPopover) {
            demote(el);
            el.removeAttribute('popover');
            entry.addedPopover = false;
        }
        return;
    }
    if (entry.addedPopover) return;          // already promoted; re-raises come from the registry
    if (nativeModal) return;                 // deferred: the close re-runs the pass
    el.setAttribute('popover', 'manual');
    entry.addedPopover = true;
    promote(el);
}

/** One synchronous pass over the attribute scan, the API pins and every tracked element. */
function pass() {
    const doc = state.env?.document;
    const tagged = new Set();
    if (doc && typeof doc.querySelectorAll === 'function') {
        try {
            for (const el of doc.querySelectorAll(SELECTOR)) tagged.add(el);
        }
        catch {
            // A document without querySelectorAll support for the selector: API pins still work.
        }
    }
    for (const el of tagged) {
        if (!isElement(el)) continue;
        const entry = ensureEntry(el);
        if (entry) entry.attr = true;
    }

    const nativeModal = isNativeModalOpen();
    const live = [];
    let reordered = false;
    for (const [el, entry] of [...entries]) {
        entry.attr = tagged.has(el) && el.hasAttribute('data-tm-pin-on-top');
        if (!el.isConnected || (!entry.api && !entry.attr)) {
            release(el, entry);
            continue;
        }
        const priority = priorityOf(el, entry);
        // A priority change of a pin that is already ON SCREEN must re-rank now, not at the next
        // unrelated promotion.
        if (entry.lastPriority !== undefined && entry.lastPriority !== priority && isOpen(el)) reordered = true;
        entry.lastPriority = priority;
        pinRoot(el, { priority });
        live.push([el, entry]);
    }

    // Ascending priority: a pin promoted in this pass re-raises the higher bands above itself, so
    // the order is right whatever order the pins became visible in.
    live.sort(([a, ea], [b, eb]) => priorityOf(a, ea) - priorityOf(b, eb));
    for (const [el, entry] of live) {
        if (!entry.explicit) syncObserved(el, entry, nativeModal);
    }
    if (reordered) syncPinnedRoots();   // a no-op while a native modal dialog is open

    // Drop the records this pass's own writes queued: a pass never schedules the next one.
    try { state.observer?.takeRecords?.(); }
    catch { /* a disconnected observer */ }
}

function schedule() {
    if (state.frameScheduled || !state.env) return;
    state.frameScheduled = true;
    state.env.requestAnimationFrame(() => {
        state.frameScheduled = false;
        pass();
    });
}

function touchesPins(node) {
    if (!node || node.nodeType !== 1) return false;
    if (entries.has(node)) return true;
    try {
        if (typeof node.matches === 'function' && node.matches(SELECTOR)) return true;
        if (node.tagName === 'DIALOG') return true;
        if (typeof node.querySelector === 'function' && node.querySelector(`${SELECTOR}, dialog`)) return true;
        for (const el of entries.keys()) {
            if (typeof node.contains === 'function' && node.contains(el)) return true;
        }
    }
    catch {
        return true;   // when in doubt, re-check: a pass is cheap, a missed pin is not
    }
    return false;
}

function relevant(records) {
    for (const record of records) {
        if (record.type === 'attributes') {
            if (record.attributeName === 'data-tm-pin-on-top' || record.attributeName === 'open') return true;
            if (entries.has(record.target)) return true;
            continue;
        }
        for (const node of record.addedNodes ?? []) if (touchesPins(node)) return true;
        for (const node of record.removedNodes ?? []) if (touchesPins(node)) return true;
    }
    return false;
}

/**
 * Starts the module: one scan of [data-tm-pin-on-top] (now, or on DOMContentLoaded while the
 * document is still loading), one document-wide MutationObserver and one capture-phase `close`
 * listener. Idempotent — across module copies too; importing the module calls it.
 * @param {object} [environment] test seam: { document, MutationObserver, getComputedStyle, requestAnimationFrame }
 */
export function start(environment) {
    if (state.started) return;
    const env = environment ?? defaultEnv();
    const doc = env.document;
    if (!doc) return;                        // no DOM (a worker, prerender tooling): stay idle
    state.env = env;
    state.started = true;

    if (typeof env.MutationObserver === 'function' && doc.documentElement) {
        state.observer = new env.MutationObserver((records) => {
            if (relevant(records)) schedule();
        });
        state.observer.observe(doc.documentElement, {
            subtree: true,
            childList: true,
            attributes: true,
            attributeFilter: OBSERVED_ATTRIBUTES,
        });
    }

    if (typeof doc.addEventListener === 'function') {
        state.onClose = () => schedule();
        doc.addEventListener('close', state.onClose, true);
    }

    if (doc.readyState === 'loading' && typeof doc.addEventListener === 'function') {
        state.onReady = () => pass();
        doc.addEventListener('DOMContentLoaded', state.onReady, { once: true });
    }
    else {
        pass();
    }
}

/**
 * Pins an element: it follows its own visibility (shown → promoted above every Tempo top-layer
 * surface, hidden → handed back as a plain element). Calling it again updates the priority.
 * No-op for null, a non-element or a browser without the Popover API.
 * @param {HTMLElement} element the element to keep on top
 * @param {{priority?: number}} [options] order between pins; higher paints higher (default 0).
 *   A data-tm-pin-priority attribute on the element wins over this value.
 */
export function pin(element, options) {
    if (!isElement(element)) return;
    start();
    if (!state.started) return;
    const entry = ensureEntry(element);
    if (!entry) return;
    entry.api = true;
    const priority = Number(options?.priority);
    entry.apiPriority = Number.isFinite(priority) ? Math.trunc(priority) : 0;
    pass();
}

/**
 * Releases an element pinned with {@link pin}: it is demoted and every attribute the module added
 * (popover, data-tm-inert-exempt) is removed. An element that still carries data-tm-pin-on-top
 * stays pinned through the attribute. No-op for an element that is not pinned.
 * @param {HTMLElement} element the previously pinned element
 */
export function unpin(element) {
    const entry = element ? entries.get(element) : undefined;
    if (!entry) return;
    entry.api = false;
    pass();
}

/**
 * Forces one synchronous pass. Rarely needed: the observer sees the element's own style, class
 * and hidden changes; call this after hiding the element through an ANCESTOR.
 */
export function refresh() {
    if (state.started) pass();
}

/** Number of tracked pins. Test seam. */
export function __pinnedCountForTests() {
    return entries.size;
}

/** Stops the module and forgets every pin without touching the DOM. The node tests call it. */
export function __resetForTests() {
    try { state.observer?.disconnect(); }
    catch { /* already gone */ }
    const doc = state.env?.document;
    if (doc && typeof doc.removeEventListener === 'function') {
        if (state.onClose) doc.removeEventListener('close', state.onClose, true);
        if (state.onReady) doc.removeEventListener('DOMContentLoaded', state.onReady);
    }
    entries.clear();
    state.observer = null;
    state.env = null;
    state.started = false;
    state.frameScheduled = false;
    state.onClose = null;
    state.onReady = null;
}

// Importing the module starts it. Under node (the tests) there is no document and this is a no-op.
start();