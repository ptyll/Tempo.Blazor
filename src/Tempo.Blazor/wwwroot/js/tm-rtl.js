// The writing direction of an element, read live from its computed style. The shared helper for
// Blazor components that must mirror arrow semantics in RTL (TmLightbox swaps ArrowLeft/Right and
// mirrors its chevrons through a [dir="rtl"] CSS rule): "rtl" only when the element's computed
// direction says so, "ltr" everywhere else (Node without DOM globals, SSR, a throwing probe —
// the same fallback shape as data-table.js and tm-splitter.js).
export function directionOf(element) {
    try {
        return globalThis.getComputedStyle?.(element)?.direction === 'rtl' ? 'rtl' : 'ltr';
    } catch {
        return 'ltr';
    }
}
