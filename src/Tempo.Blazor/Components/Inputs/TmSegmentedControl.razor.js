// Sliding highlight for the core TmSegmentedControl.
//
// The highlight is a decorative strip inside the tray, positioned by MEASURING the selected segment. It
// cannot be done in CSS: a segmented tray is a scrolling flex row (the segments are content-sized, and a
// long set scrolls), so there is no static relationship between a segment's box and the tray's edge that
// a selector could express. The strip is `transform`ed rather than moved with `left`, so the travel is
// compositable and `prefers-reduced-motion` can drop it to a jump.
//
// Two invariants this module keeps:
//
// 1. It never CLAIMS the sliding mode when it cannot measure. `data-tm-indicator="sliding"` on the tray
//    is what hides the selected segment's own background (see _segmented-control.css); setting it while
//    the measurement is meaningless (a hidden tray, a zero-width segment) would leave the control with
//    no visible selection at all. The attribute is therefore set only after a successful measurement and
//    removed when the segment goes away.
//
// 2. A module is shared by every instance on the page, so all per-instance state lives in `trayState`
//    keyed by the element. `release()` disconnects the observer; the component calls it from
//    `DisposeAsync`, so a disposed tray cannot keep the observer (and the component) alive.

const trayState = new WeakMap();

function options(tray) {
    return Array.from(tray.querySelectorAll(".tm-segmented__option"));
}

function selectedOption(tray) {
    return options(tray).find(
        (option) =>
            option.getAttribute("aria-checked") === "true" ||
            option.getAttribute("aria-selected") === "true",
    ) ?? null;
}

function indicatorOf(tray) {
    return tray.querySelector(".tm-segmented__indicator");
}

/**
 * Positions the highlight over the selected segment, or gives the tray back to the CSS fallback when
 * there is nothing measurable.
 * @param {HTMLElement | null} tray the element carrying the role (the scrolling track)
 */
export function place(tray) {
    if (!tray || !(tray instanceof HTMLElement)) return;

    const indicator = indicatorOf(tray);
    if (!indicator) return;

    const selected = selectedOption(tray);
    // offsetWidth is 0 for a display:none tray, and offsetLeft is meaningless before layout.
    if (!selected || selected.offsetWidth === 0) {
        tray.removeAttribute("data-tm-indicator");
        indicator.style.removeProperty("width");
        indicator.style.removeProperty("height");
        indicator.style.removeProperty("transform");
        return;
    }

    // offsetLeft/offsetTop are measured inside the tray's padding box, which is also the containing
    // block of the absolutely positioned strip — so no scroll correction is needed: both live in the
    // scrolled coordinate space and travel together. The strip is sized AND positioned from the
    // measurement, so it follows a segment whose height differs from its neighbours.
    indicator.style.width = `${selected.offsetWidth}px`;
    indicator.style.height = `${selected.offsetHeight}px`;
    indicator.style.transform = `translate(${selected.offsetLeft}px, ${selected.offsetTop}px)`;
    tray.setAttribute("data-tm-indicator", "sliding");

    observe(tray);
}

/**
 * Moves DOM focus to one segment (the roving tabindex path). The browser scrolls the focused segment
 * into view, which is exactly the required behaviour in a tray that overflows: the focused option must
 * be visible.
 * @param {HTMLElement | null} tray the element carrying the role
 * @param {number} index zero-based segment index
 */
export function focusOption(tray, index) {
    if (!tray || !(tray instanceof HTMLElement)) return;

    const target = options(tray)[index];
    if (!target) return;

    target.focus();
    if (target.offsetWidth > 0) {
        target.scrollIntoView({ block: "nearest", inline: "nearest" });
    }
}

/**
 * Re-measures when the tray or any segment changes size — a container resize, a font swap, a longer
 * label, a device rotation. Observing the SEGMENTS too matters: the tray's own width can stay identical
 * while the selected segment's box changes.
 * @param {HTMLElement} tray the element carrying the role
 */
function observe(tray) {
    if (trayState.has(tray) || typeof ResizeObserver === "undefined") return;

    const observer = new ResizeObserver(() => place(tray));
    observer.observe(tray);
    for (const option of options(tray)) observer.observe(option);

    trayState.set(tray, observer);
}

/** Disconnects the observer of a tray that is going away. */
export function release(tray) {
    if (!tray || !(tray instanceof HTMLElement)) return;

    const observer = trayState.get(tray);
    if (observer) {
        observer.disconnect();
        trayState.delete(tray);
    }
}
