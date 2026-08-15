// Phase D — render/image-layout-bubble.mjs
// `createRenderImageLayoutBubbleButton({escapeHtml})` →
//   `renderImageLayoutBubbleButton(testId, command, label, active, attributes?)`
//   — single button inside the floating layout bubble. `attributes['aria-label']`
//   (or `attributes.ariaLabel`) wins; remaining keys are emitted as escaped
//   attributes. Active state toggles `--active` class + `aria-pressed="true"`.
// `createRenderImageLayoutBubbleHtml({normalizeWrapModeName, renderImageLayoutBubbleButton})` →
//   `renderImageLayoutBubbleHtml(object)` — emits the toolbar of wrap-mode +
//   anchor-mode buttons that hover next to an image. Active button is determined
//   by `object.wrapMode` (Inline/Square→Wrap/Tight/TopBottom→Break/BehindText/InFrontOfText)
//   and `object.fixedOnPage` / `moveWithText` for the anchor-mode pair.

export function createRenderImageLayoutBubbleButton(options) {
    const opts = options || {};
    if (typeof opts.escapeHtml !== 'function') {
        throw new TypeError(
            'createRenderImageLayoutBubbleButton requires options.escapeHtml (function)');
    }
    const { escapeHtml } = opts;
    return function renderImageLayoutBubbleButton(testId, command, label, active, attributes) {
        const extra = Object.assign({}, attributes || {});
        const ariaLabel = extra['aria-label'] || extra.ariaLabel || label;
        delete extra['aria-label'];
        delete extra.ariaLabel;
        let html = '<button type="button" class="tm-wysiwyg-layout-bubble__button'
            + (active ? ' tm-wysiwyg-layout-bubble__button--active' : '')
            + '" data-testid="' + escapeHtml(testId) + '"'
            + ' data-command="' + escapeHtml(command) + '"'
            + ' aria-label="' + escapeHtml(ariaLabel) + '"'
            + ' aria-pressed="' + (active ? 'true' : 'false') + '"';
        Object.keys(extra).forEach(function (key) {
            html += ' ' + escapeHtml(key) + '="' + escapeHtml(extra[key]) + '"';
        });
        html += '>' + escapeHtml(label) + '</button>';
        return html;
    };
}

export function createRenderImageLayoutBubbleHtml(options) {
    const opts = options || {};
    if (typeof opts.normalizeWrapModeName !== 'function') {
        throw new TypeError(
            'createRenderImageLayoutBubbleHtml requires options.normalizeWrapModeName (function)');
    }
    if (typeof opts.renderImageLayoutBubbleButton !== 'function') {
        throw new TypeError(
            'createRenderImageLayoutBubbleHtml requires options.renderImageLayoutBubbleButton (function)');
    }
    const { normalizeWrapModeName, renderImageLayoutBubbleButton } = opts;

    return function renderImageLayoutBubbleHtml(object) {
        const mode = normalizeWrapModeName(object && object.wrapMode);
        const fixedOnPage = !!(object && object.fixedOnPage === true);
        const moveWithText = !!(object && object.moveWithText !== false && !fixedOnPage);
        const buttons = [
            renderImageLayoutBubbleButton('document-wysiwyg-layout-bubble-inline',
                'setImageWrapMode', 'Inline', mode === 'Inline',
                { 'data-wrap-mode': 'Inline', 'aria-label': 'Place image inline with text' }),
            renderImageLayoutBubbleButton('document-wysiwyg-layout-bubble-wrap',
                'setImageWrapMode', 'Wrap', mode === 'Square',
                { 'data-wrap-mode': 'Square', 'aria-label': 'Wrap text around image' }),
            renderImageLayoutBubbleButton('document-wysiwyg-layout-bubble-tight',
                'setImageWrapMode', 'Tight', mode === 'Tight',
                { 'data-wrap-mode': 'Tight', 'aria-label': 'Use tight image text wrapping' }),
            renderImageLayoutBubbleButton('document-wysiwyg-layout-bubble-break',
                'setImageWrapMode', 'Break', mode === 'TopBottom',
                { 'data-wrap-mode': 'TopBottom', 'aria-label': 'Place image between text lines' }),
            renderImageLayoutBubbleButton('document-wysiwyg-layout-bubble-behind',
                'setImageWrapMode', 'Behind', mode === 'BehindText',
                { 'data-wrap-mode': 'BehindText', 'aria-label': 'Place image behind text' }),
            renderImageLayoutBubbleButton('document-wysiwyg-layout-bubble-front',
                'setImageWrapMode', 'Front', mode === 'InFrontOfText',
                { 'data-wrap-mode': 'InFrontOfText', 'aria-label': 'Place image in front of text' }),
            renderImageLayoutBubbleButton('document-wysiwyg-layout-bubble-move-with-text',
                'setImageAnchorMode', 'Move', moveWithText,
                {
                    'data-move-with-text': 'true',
                    'data-fixed-on-page': 'false',
                    'aria-label': 'Move image with text',
                }),
            renderImageLayoutBubbleButton('document-wysiwyg-layout-bubble-fix-position',
                'setImageAnchorMode', 'Fix', fixedOnPage,
                {
                    'data-move-with-text': 'false',
                    'data-fixed-on-page': 'true',
                    'aria-label': 'Fix image position on page',
                }),
            renderImageLayoutBubbleButton('document-wysiwyg-layout-bubble-more',
                'focusImageOptions', 'More', false,
                { 'aria-label': 'Open image options' }),
        ];
        return '<span class="tm-wysiwyg-layout-bubble"'
            + ' data-testid="document-wysiwyg-object-layout-bubble"'
            + ' role="group"'
            + ' aria-label="Image layout options"'
            + ' aria-expanded="false">'
            + buttons.join('') + '</span>';
    };
}
