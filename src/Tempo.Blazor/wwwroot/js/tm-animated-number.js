// tm-animated-number.js — the requestAnimationFrame ticker behind TmAnimatedNumber.
//
// The module owns exactly one thing: repainting the text of a single element from a start number to
// a target number. It never owns the accessible value — the ONE visible span already carries the final
// text the component rendered, and `Announce` is the component's only live-region opt-in (it adds a
// visually hidden polite region and hides that span) — it never reads state back into .NET, and it
// never runs two loops on the same element.
//
// Contract with the component:
//   animate(element, from, to, duration, options)   from = null → continue from the painted number
//   cancel(element)                                 stop the loop of one element
//   prefersReducedMotion()                          the machine's motion preference
//
// A second `animate` call on the same element is a RETARGET: the running loop is cancelled and a new
// one starts from the number currently on screen, so a value that changes mid-animation jumps onto
// its new target instead of finishing the old one first (the component passes from = null for it).

/** element → { frameId, painted } — the live ticker of every element this module is animating. */
const running = new WeakMap();

/**
 * element → the last number this module painted on it. It OUTLIVES the loop on purpose: a ticker
 * that already finished must still be able to continue from its own last value when the host
 * changes the target again (a component that dropped it would start every later change from the
 * new target, i.e. not animate at all). A WeakMap entry dies with its element.
 */
const paintedValues = new WeakMap();

/** formatter cache key → Intl.NumberFormat (bounded by the distinct option shapes on a page). */
const formatters = new Map();

const DEFAULT_MAX_FRACTION_DIGITS = 0;
const MAX_CACHED_FORMATTERS = 64;

/**
 * Whether the user asked for reduced motion. Missing `matchMedia` (old hosts, non-browser
 * environments) means "no preference" — and the component's own final value is still correct.
 */
export function prefersReducedMotion() {
    try {
        return globalThis.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches === true;
    } catch {
        return false;
    }
}

/** The `Intl.NumberFormat` for one option bag, cached by its shape. Invalid input never throws. */
function formatterFor(options) {
    const locale = typeof options?.locale === 'string' && options.locale ? options.locale : 'en-US';
    const style = options?.style === 'percent' ? 'percent' : 'decimal';
    const min = clampDigits(options?.minimumFractionDigits);
    const max = Math.max(min, clampDigits(options?.maximumFractionDigits));
    // useGrouping shapes the output too — a formatter cached for a grouping-free instance must
    // not be handed to one that asked for separators (and vice versa).
    const grouping = options?.useGrouping === false ? 'off' : 'on';
    const key = `${locale}|${style}|${min}|${max}|${grouping}`;

    let formatter = formatters.get(key);
    if (formatter) {
        return formatter;
    }

    try {
        formatter = new Intl.NumberFormat(locale, {
            style,
            minimumFractionDigits: min,
            maximumFractionDigits: max,
            useGrouping: options?.useGrouping !== false
        });
    } catch {
        // An unknown locale or a runtime without Intl: the plain number is still readable.
        formatter = null;
    }

    if (formatters.size >= MAX_CACHED_FORMATTERS) {
        formatters.clear();
    }

    formatters.set(key, formatter);
    return formatter;
}

function clampDigits(value) {
    const parsed = typeof value === 'number' && Number.isFinite(value) ? Math.trunc(value) : NaN;
    if (Number.isNaN(parsed)) {
        return DEFAULT_MAX_FRACTION_DIGITS;
    }

    return Math.min(15, Math.max(0, parsed));
}

/** The text of one frame: the culture's number plus the host's prefix/suffix. */
function formatValue(value, options) {
    const formatter = formatterFor(options);
    let number;
    if (formatter) {
        number = formatter.format(value);
    } else {
        number = Number.isFinite(value) ? String(Math.round(value)) : '';
    }

    const prefix = typeof options?.prefix === 'string' ? options.prefix : '';
    const suffix = typeof options?.suffix === 'string' ? options.suffix : '';
    return `${prefix}${number}${suffix}`;
}

/** Stops the loop of one element. Idempotent and safe for an element that was never animated. */
export function cancel(element) {
    const state = element ? running.get(element) : undefined;
    if (!state) {
        return;
    }

    running.delete(element);
    try {
        globalThis.cancelAnimationFrame?.(state.frameId);
    } catch {
        // A torn-down host has nothing to cancel.
    }
}

/**
 * Starts (or retargets) the ticker on `element`.
 *
 * @param {Element} element        the value span the component rendered (aria-hidden only with Announce)
 * @param {number|null} from       first-frame value, or null to continue from the painted number
 * @param {number} to              the target value
 * @param {number} duration        milliseconds
 * @param {object} options         locale / style / digits / prefix / suffix
 */
export function animate(element, from, to, duration, options) {
    if (!element || typeof to !== 'number' || !Number.isFinite(to)) {
        return;
    }

    const painted = paintedValues.get(element) ?? running.get(element)?.painted;
    const start = typeof from === 'number' && Number.isFinite(from)
        ? from
        : (typeof painted === 'number' ? painted : to);

    // Exactly one loop per element: the retarget starts where the old one stopped.
    cancel(element);

    const total = typeof duration === 'number' && Number.isFinite(duration) ? Math.max(0, duration) : 0;

    if (total === 0 || prefersReducedMotion()) {
        paintedValues.set(element, to);
        element.textContent = formatValue(to, options);
        return;
    }

    if (typeof globalThis.requestAnimationFrame !== 'function') {
        // No frame clock (a non-browser host): the target is the only honest frame.
        paintedValues.set(element, to);
        element.textContent = formatValue(to, options);
        return;
    }

    const state = { frameId: 0, painted: start };
    running.set(element, state);

    const begin = now();
    const step = timestamp => {
        if (running.get(element) !== state) {
            return;
        }

        // A removed element stops its own loop: nothing repaints a node that left the document.
        if (element.isConnected === false) {
            running.delete(element);
            return;
        }

        const elapsed = Math.max(0, timestamp - begin);
        const progress = total <= 0 ? 1 : Math.min(1, elapsed / total);
        const eased = 1 - Math.pow(1 - progress, 3);
        const current = start + (to - start) * eased;

        state.painted = current;
        paintedValues.set(element, current);

        if (progress < 1) {
            element.textContent = formatValue(current, options);
            state.frameId = requestAnimationFrame(step);
            return;
        }

        // The last frame is the exact target: a rounding difference here would leave the ticker one
        // step away from the number the component rendered for readers.
        state.painted = to;
        paintedValues.set(element, to);
        element.textContent = formatValue(to, options);
        running.delete(element);
    };

    state.frameId = requestAnimationFrame(step);
}

function now() {
    return typeof performance !== 'undefined' && typeof performance.now === 'function'
        ? performance.now()
        : Date.now();
}

/** Stops every loop this module started (a host teardown path; per-component cleanup uses cancel). */
export function cancelAll() {
    if (typeof document === 'undefined') {
        return;
    }

    for (const element of [...document.querySelectorAll('.tm-animated-number__value')]) {
        cancel(element);
    }
}
