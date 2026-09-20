// TmColorPicker's floating panel is a TmOverlayPanel now — dismissal (Escape / outside
// pointerdown), placement, flipping, shifting and scroll/resize tracking all live in overlay.js.
// What remains here is the palette keyboard-navigation helper TmColorPalette still calls.
window.tmColorPicker = window.tmColorPicker || (function () {
    function focusPaletteSwatch(root, index) {
        if (!root) {
            return;
        }

        const swatch = root.querySelector(`.tm-color-palette-swatch[data-palette-index="${index}"]`);
        if (swatch && typeof swatch.focus === "function") {
            swatch.focus({ preventScroll: true });
        }
    }

    return {
        focusPaletteSwatch: focusPaletteSwatch
    };
})();
