// Demo-only ES module for the /chat render-window section (FZE-14): counts the mounted
// .tm-chat__item rows inside one demo section. Lives in the demo's own static assets — the
// page counts DOM rows through this exported function instead of window.eval, so the counter
// works under a strict CSP and the selector stays testable.
export function countRows(sectionSelector) {
    const section = document.querySelector(sectionSelector);
    if (!section) {
        return 0;
    }

    return section.querySelectorAll('.tm-chat__item').length;
}
