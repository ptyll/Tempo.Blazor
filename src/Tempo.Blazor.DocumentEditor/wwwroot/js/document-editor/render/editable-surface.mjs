// Phase D — render/editable-surface.mjs
// Pointer-target predicates that decide whether a click/point lands on editable
// document text (as opposed to an image/object overlay or editor chrome).
//
// `createEditableSurfacePredicates({ELEMENT_NODE, nativeCaretRangeFromPoint})` →
//   `{ targetIsEditableDocumentSurface, nativePointTargetsEditableText }`.
//
// • targetIsEditableDocumentSurface(inst, target) — true when `target` is inside the
//   instance root, NOT within an image/object overlay, and IS within a
//   contenteditable page region or a `[data-block-id]` block.
// • nativePointTargetsEditableText(inst, x, y) — resolves the native caret range at
//   the point and returns true only when it lands on a text node inside a
//   `[data-block-id]`/`[data-render-block-id]` block and clear of object overlays
//   and toolbars.
//
// `ELEMENT_NODE` defaults to 1; `nativeCaretRangeFromPoint` is required.

export function createEditableSurfacePredicates(options) {
    const opts = options || {};
    if (typeof opts.nativeCaretRangeFromPoint !== 'function') {
        throw new TypeError(
            'createEditableSurfacePredicates requires options.nativeCaretRangeFromPoint (function)');
    }
    const ELEMENT_NODE = typeof opts.ELEMENT_NODE === 'number' ? opts.ELEMENT_NODE : 1;
    const { nativeCaretRangeFromPoint } = opts;

    function targetIsEditableDocumentSurface(inst, target) {
        if (!inst || !inst.root || !target) return false;
        const element = target.nodeType === ELEMENT_NODE ? target : target.parentElement;
        if (!element || !inst.root.contains(element)) return false;
        if (element.closest && element.closest('figure.tm-wysiwyg-image, .tm-render-image-widget, .tm-wysiwyg-inline-drawing[data-object-id], .tm-wysiwyg-object-layer-item[data-object-id], .tm-wysiwyg-object-selection-overlay[data-object-id], .tm-wysiwyg-object-guides-overlay[data-object-id]')) {
            return false;
        }
        return !!(element.closest && element.closest('.tm-wysiwyg-page__body[contenteditable], .tm-wysiwyg-page__header[contenteditable], .tm-wysiwyg-page__footer[contenteditable], .tm-wysiwyg-block[data-block-id]'));
    }

    function nativePointTargetsEditableText(inst, x, y) {
        const range = nativeCaretRangeFromPoint(x, y);
        if (!range || !range.startContainer || !inst || !inst.root) return false;
        const node = range.startContainer;
        const element = node.nodeType === ELEMENT_NODE ? node : node.parentElement;
        if (!element || !inst.root.contains(element)) return false;
        if (element.closest && element.closest('figure, .tm-wysiwyg-inline-drawing, .tm-wysiwyg-object-layer-item, .tm-wysiwyg-object-selection-overlay, .tm-wysiwyg-object-guides-overlay, .tm-wysiwyg-drawing-anchor, .tm-document-editor__ribbon, .tm-wysiwyg-layout-bubble')) {
            return false;
        }
        return node.nodeType === 3 && !!element.closest('[data-block-id], [data-render-block-id]');
    }

    return Object.freeze({
        targetIsEditableDocumentSurface,
        nativePointTargetsEditableText,
    });
}
