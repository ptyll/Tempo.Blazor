// Tempo.Blazor color gradient (ES module) — the TmColorGradient gesture.
//
// The pointer gesture is the shared pointer-drag helper (pointer capture, Escape / pointercancel
// cancel, touch-action…); this module only turns pointer positions into HSV/alpha FRACTIONS of the
// element's real bounding rect — never the event offset (it jumps on the thumb child) and never a
// fixed pixel size (the host may size the gradient however it wants). Fractions stream to .NET
// through rAF-throttled OnGradientDrag calls (C# owns the colour maths and the preview), the
// release commits ONCE through OnGradientCommit, and Escape / pointercancel / a mid-drag detach
// report OnGradientCancel — nothing is committed on a cancelled gesture. A tap (press + release
// without moving) sets the colour through the same commit.
//
// The keyboard path (arrows / Home / End on the three role="slider" elements) lives in Blazor and
// commits through the same ValueChanged; this module only prevents the axis keys and Home/End from
// scrolling the page. The horizontal slider mapping is deliberately NOT RTL-inverted: the
// saturation/hue/alpha gradients paint left-to-right in every writing direction, so the pointer
// and the arrow keys both follow the painted gradient, not the text flow.

import { attach } from './pointer-drag.js';

const registry = new Map();

// Keys the sliders answer (Blazor performs the step); the browser's default for them on a focused
// non-input element is scrolling the page, which a value change must not also do.
const SLIDER_KEYS = ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End'];

const clamp01 = (v) => (Number.isFinite(v) ? Math.min(1, Math.max(0, v)) : 0);

/**
 * Attaches the pointer-drag gestures to one gradient root: the saturation/value area, the hue
 * track and (when present) the alpha track. The keyboard alternative stays in Blazor; only the
 * scrolling keys are prevented here.
 * @param {HTMLElement} root the .tm-color-gradient element
 * @param {{ invokeMethodAsync: Function }|null} dotnet the component, which receives
 *        OnGradientDrag(kind, fx, fy) / OnGradientCommit(kind, fx, fy) / OnGradientCancel()
 * @param {string} id registration key; attaching again with the same id replaces the previous one
 */
export function attachGradient(root, dotnet, id) {
    if (!root || !root.querySelector) return;
    detachGradient(id);

    const parts = [
        ['area', '.tm-color-gradient-area'],
        ['hue', '.tm-color-gradient-hue-track'],
        ['alpha', '.tm-color-gradient-alpha-track'],
    ];
    const drags = [];
    const onKeys = [];

    for (const [kind, selector] of parts) {
        const el = root.querySelector(selector);
        if (!el) continue;

        let frame = 0;
        let last = null;

        // Fractions of THIS element's box, recomputed live (a resize between events re-bases).
        const frac = ({ x, y }) => {
            const r = el.getBoundingClientRect();
            return {
                fx: clamp01((x - r.left) / r.width),
                fy: clamp01((y - r.top) / r.height),
            };
        };

        const send = (method, p) => {
            try {
                dotnet?.invokeMethodAsync(method, kind, p.fx, p.fy)?.catch?.(() => {});
            } catch {
                // A disposed circuit must not turn a preview into an unhandled rejection.
            }
        };

        drags.push(attach(el, {
            threshold: 0,
            // CSS owns touch-action: none on the part (.tm-color-gradient-area / tracks).
            touchAction: null,
            // The part owns no click; the RGBA number inputs and the preview must keep theirs.
            suppressClick: false,
            onPress(info) {
                last = frac(info);
                send('OnGradientDrag', last);
            },
            onMove(info) {
                last = frac(info);
                if (frame) return;
                frame = requestAnimationFrame(() => {
                    frame = 0;
                    send('OnGradientDrag', last);
                });
            },
            onEnd(info) {
                if (frame) {
                    cancelAnimationFrame(frame);
                    frame = 0;
                }
                send('OnGradientCommit', frac(info));
            },
            onCancel() {
                if (frame) {
                    cancelAnimationFrame(frame);
                    frame = 0;
                }
                try {
                    dotnet?.invokeMethodAsync('OnGradientCancel')?.catch?.(() => {});
                } catch {
                    // ditto
                }
            },
            onRelease({ outcome }) {
                // A press that never became a drag still sets the colour — through the commit.
                if (outcome === 'tap' && last) send('OnGradientCommit', last);
            },
        }));

        const onKey = (event) => {
            if (SLIDER_KEYS.indexOf(event.key) !== -1) event.preventDefault();
        };
        el.addEventListener('keydown', onKey);
        onKeys.push({ el, onKey });
    }

    registry.set(id, { drags, onKeys });
}

/** Removes the gradient registered under the id. Safe to call twice or for an unknown id. */
export function detachGradient(id) {
    const entry = registry.get(id);
    if (!entry) return;
    registry.delete(id);
    for (const drag of entry.drags) drag.detach();
    for (const { el, onKey } of entry.onKeys) el.removeEventListener('keydown', onKey);
}

/** Drop every registration. Test-only: a module imported by Node is shared across tests. */
export function __resetForTests() {
    for (const id of [...registry.keys()]) detachGradient(id);
}
