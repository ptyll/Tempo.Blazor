// Measures the demo hosts so the heading can show the container width the grid actually follows.
// The page owns the text; this script only reads widths.

export function measure(ids) {
    const result = {};
    for (const id of ids) {
        const host = document.querySelector(`[data-testid="${id}"] .tm-dashboard-grid-container`);
        result[id] = host ? Math.round(host.clientWidth) : 0;
    }
    return result;
}
