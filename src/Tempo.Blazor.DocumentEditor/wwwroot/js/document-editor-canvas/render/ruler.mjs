import { createRulerInteraction } from './ruler-interaction.mjs';

export function createCanvasRulerOverlay(options = {}) {
    const doc = options.document || globalThis.document;
    if (!doc || typeof doc.createElement !== 'function') {
        throw new Error('Canvas ruler overlay requires a DOM-like document.');
    }

    const root = doc.createElement('div');
    root.className = 'tm-document-canvas-ruler-shell';
    root.setAttribute('data-testid', 'document-canvas-ruler-shell');
    root.setAttribute('aria-hidden', 'true');

    const ruler = doc.createElement('div');
    ruler.className = 'tm-document-canvas-ruler';
    ruler.setAttribute('data-testid', 'document-canvas-ruler');
    root.appendChild(ruler);

    const controls = doc.createElement('div');
    controls.className = 'tm-document-canvas-ruler__controls';
    ruler.appendChild(controls);

    const tabPicker = doc.createElement('button');
    tabPicker.type = 'button';
    tabPicker.className = 'tm-document-canvas-ruler__picker';
    tabPicker.setAttribute('data-testid', 'document-canvas-ruler-tab-picker');
    controls.appendChild(tabPicker);

    const leaderPicker = doc.createElement('button');
    leaderPicker.type = 'button';
    leaderPicker.className = 'tm-document-canvas-ruler__picker';
    leaderPicker.setAttribute('data-testid', 'document-canvas-ruler-leader-picker');
    controls.appendChild(leaderPicker);

    const track = doc.createElement('div');
    track.className = 'tm-document-canvas-ruler__track';
    ruler.appendChild(track);

    const leftMargin = marker(doc, 'document-canvas-ruler-margin-left', 'margin-left');
    const firstLine = marker(doc, 'document-canvas-ruler-first-line-indent', 'first-line');
    const leftIndent = marker(doc, 'document-canvas-ruler-left-indent', 'left-indent');
    const rightIndent = marker(doc, 'document-canvas-ruler-right-indent', 'right-indent');
    const rightMargin = marker(doc, 'document-canvas-ruler-margin-right', 'margin-right');
    track.append(leftMargin, firstLine, leftIndent, rightIndent, rightMargin);
    const interaction = createRulerInteraction({
        root,
        executeCommand: options.executeCommand,
        getState: () => root.__tmRulerState || {},
    });

    return {
        root,
        update(layout, model, viewState = {}) {
            updateRulerOverlay(root, layout, model, viewState);
        },
        destroy() {
            interaction.destroy();
            root.remove?.();
        },
    };
}

export function updateRulerOverlay(root, layout, model, viewState = {}) {
    if (!root) {
        return;
    }

    const showRuler = viewState.showRuler !== false;
    const page = Array.isArray(layout?.pages) ? layout.pages[0] : null;
    const pageSettings = model?.pageSettings || {};
    const block = firstSelectedOrBodyBlock(model, viewState.selection);
    const props = block?.paragraphProperties || {};
    const pageWidth = Math.max(1, Number(page?.width || pageSettings.width || 794) || 794);
    const marginLeft = nonNegative(page?.body?.x ?? pageSettings.marginLeft, 96);
    const marginRight = nonNegative(pageSettings.marginRight ?? (pageWidth - ((page?.body?.x || marginLeft) + (page?.body?.width || 0))), 96);
    const leftIndentPoints = nonNegative(props.leftIndent ?? props.LeftIndent, 0);
    const rightIndentPoints = nonNegative(props.rightIndent ?? props.RightIndent, 0);
    const firstLineIndentPoints = Number(props.firstLineIndent ?? props.FirstLineIndent ?? 0) || 0;
    const leftIndent = pointsToCssPixels(leftIndentPoints);
    const rightIndent = pointsToCssPixels(rightIndentPoints);
    const firstLineIndent = pointsToCssPixels(firstLineIndentPoints);

    root.hidden = !showRuler;
    root.setAttribute('data-canvas-ruler-visible', String(showRuler));
    root.setAttribute('data-canvas-ruler-show-blocks', String(viewState.showBlocks === true));
    root.setAttribute('data-canvas-ruler-show-nonprinting', String(viewState.showNonPrintingCharacters === true));
    const ruler = root.querySelector?.('[data-testid="document-canvas-ruler"]');
    ruler?.setAttribute?.('data-canvas-ruler-visible', String(showRuler));
    ruler?.setAttribute?.('data-canvas-ruler-show-blocks', String(viewState.showBlocks === true));
    ruler?.setAttribute?.('data-canvas-ruler-show-nonprinting', String(viewState.showNonPrintingCharacters === true));

    // A page the engine fitted smaller (or a reader zoomed) than itself: the ruler follows the scale, so it is never wider than the page column.
    const scale = Math.max(0.01, Number(viewState.zoom?.scale) || 1);
    const track = root.querySelector?.('.tm-document-canvas-ruler__track');
    if (track) {
        track.style.width = `${Math.round(pageWidth * scale * 100) / 100}px`;
    }

    root.__tmRulerState = {
        scale,
        blockId: block?.id || '',
        marginLeftPx: marginLeft,
        marginRightPx: marginRight,
        leftIndentPx: leftIndent,
        rightIndentPx: rightIndent,
        leftIndentPoints,
        rightIndentPoints,
        firstLineIndentPoints,
        pageBodyWidthPoints: Math.max(1, ((page?.body?.width || pageWidth - marginLeft - marginRight) * 72 / 96)),
        track,
    };

    setMarker(root, 'document-canvas-ruler-margin-left', marginLeft, scale);
    setMarker(root, 'document-canvas-ruler-first-line-indent', marginLeft + leftIndent + firstLineIndent, scale);
    setMarker(root, 'document-canvas-ruler-left-indent', marginLeft + leftIndent, scale);
    setMarker(root, 'document-canvas-ruler-right-indent', Math.max(0, pageWidth - marginRight - rightIndent), scale);
    setMarker(root, 'document-canvas-ruler-margin-right', Math.max(0, pageWidth - marginRight), scale);
    renderTabStops(root, props, marginLeft + leftIndent, scale);
}

export function syncBlockVisualization(canvasStack, layout, model, viewState = {}) {
    if (!canvasStack?.pages) {
        return;
    }

    const showBlocks = viewState.showBlocks === true;
    const showNonPrinting = viewState.showNonPrintingCharacters === true;
    const blocks = Array.isArray(layout?.blocks) ? layout.blocks : [];
    for (const page of canvasStack.pages.values()) {
        const doc = page.pageElement.ownerDocument;
        let overlay = page.pageElement.querySelector?.('[data-testid="document-canvas-block-visualization-layer"]');
        if (!overlay) {
            overlay = doc.createElement('div');
            overlay.className = 'tm-document-canvas-block-visualization-layer';
            overlay.setAttribute('data-testid', 'document-canvas-block-visualization-layer');
            overlay.setAttribute('aria-hidden', 'true');
            page.pageElement.appendChild(overlay);
        }

        overlay.replaceChildren?.();
        overlay.hidden = !showBlocks && !showNonPrinting;
        if (overlay.hidden) {
            continue;
        }

        for (const block of blocks.filter(item => Number(item.pageIndex || 0) === Number(page.layout?.index || 0))) {
            if (showBlocks && block.rect) {
                const rect = doc.createElement('div');
                rect.className = 'tm-document-canvas-block-boundary';
                rect.setAttribute('data-testid', 'document-canvas-show-blocks-overlay');
                rect.setAttribute('data-block-id', block.blockId || '');
                rect.style.left = `${block.rect.x}px`;
                rect.style.top = `${block.rect.y}px`;
                rect.style.width = `${Math.max(1, block.rect.width)}px`;
                rect.style.height = `${Math.max(1, block.rect.height)}px`;
                overlay.appendChild(rect);
            }

            if (showNonPrinting && block.rect) {
                const mark = doc.createElement('span');
                mark.className = 'tm-document-canvas-nonprinting-mark';
                mark.setAttribute('data-testid', 'document-canvas-nonprinting-overlay');
                mark.textContent = '¶';
                mark.style.left = `${block.rect.x + Math.max(1, block.rect.width) + 6}px`;
                mark.style.top = `${block.rect.y}px`;
                overlay.appendChild(mark);
            }
        }
    }
}

function marker(doc, testId, type) {
    const item = doc.createElement('button');
    item.type = 'button';
    item.className = `tm-document-canvas-ruler__marker tm-document-canvas-ruler__marker--${type}`;
    item.setAttribute('data-testid', testId);
    item.setAttribute('data-ruler-marker', type);
    item.tabIndex = -1;
    return item;
}

function setMarker(root, testId, x, scale = 1) {
    const item = root.querySelector?.(`[data-testid="${testId}"]`);
    if (!item) {
        return;
    }

    const value = Math.round((Number(x) || 0) * 100) / 100;
    item.style.left = `${Math.round(value * scale * 100) / 100}px`;
    item.setAttribute('data-ruler-position', String(value));
}

function renderTabStops(root, props, baseX, scale = 1) {
    const doc = root.ownerDocument;
    const track = root.querySelector?.('.tm-document-canvas-ruler__track');
    if (!doc || !track) {
        return;
    }

    for (const existing of track.querySelectorAll?.('[data-ruler-tab-stop]') || []) {
        existing.remove();
    }

    const stops = normalizeTabStops(props);
    for (const stop of stops) {
        const item = doc.createElement('button');
        item.type = 'button';
        item.className = `tm-document-canvas-ruler__tab-stop tm-document-canvas-ruler__tab-stop--${stop.alignment}`;
        item.setAttribute('data-testid', 'document-canvas-ruler-tab-stop');
        item.setAttribute('data-ruler-tab-stop', stop.alignment);
        item.setAttribute('data-tab-position', String(stop.position));
        item.setAttribute('data-tab-alignment', stop.alignment);
        item.setAttribute('data-tab-leader', stop.leader);
        item.style.left = `${Math.round((baseX + pointsToCssPixels(stop.position)) * scale * 100) / 100}px`;
        item.style.width = stop.alignment === 'bar' ? '4px' : '13px';
        item.style.height = '13px';
        item.style.position = 'absolute';
        item.style.top = '50%';
        item.tabIndex = -1;
        track.appendChild(item);
    }
}

function normalizeTabStops(props = {}) {
    const stops = Array.isArray(props.tabStops)
        ? props.tabStops
        : Array.isArray(props.TabStops) ? props.TabStops : [];
    return stops
        .map((stop, index) => {
            const position = Number(stop?.position ?? stop?.Position);
            if (!Number.isFinite(position) || position < 0) {
                return null;
            }

            return {
                id: String(stop?.id ?? stop?.Id ?? `tab-${index}`),
                position,
                alignment: normalizeTabAlignment(stop?.alignment ?? stop?.Alignment),
                leader: normalizeTabLeader(stop?.leader ?? stop?.Leader),
            };
        })
        .filter(Boolean)
        .sort((left, right) => left.position - right.position);
}

function normalizeTabAlignment(value) {
    if (typeof value === 'number') {
        return ['left', 'center', 'right', 'decimal', 'bar'][Math.max(0, Math.min(4, Math.trunc(value)))] || 'left';
    }

    const normalized = String(value || '').replace(/[\s_-]/g, '').toLowerCase();
    if (normalized === 'center' || normalized === 'centre' || normalized === 'middle') return 'center';
    if (normalized === 'right' || normalized === 'end') return 'right';
    if (normalized === 'decimal') return 'decimal';
    if (normalized === 'bar') return 'bar';
    return 'left';
}

function normalizeTabLeader(value) {
    if (typeof value === 'number') {
        return ['none', 'dots', 'dash', 'underline'][Math.max(0, Math.min(3, Math.trunc(value)))] || 'none';
    }

    const normalized = String(value || '').replace(/[\s_-]/g, '').toLowerCase();
    if (normalized === 'dot' || normalized === 'dots' || normalized === 'dotted') return 'dots';
    if (normalized === 'dash' || normalized === 'dashes' || normalized === 'dashed') return 'dash';
    if (normalized === 'underline' || normalized === 'line') return 'underline';
    return 'none';
}

function firstSelectedOrBodyBlock(model, selection) {
    const blocks = Array.isArray(model?.body?.blocks) ? model.body.blocks : [];
    if (selection?.focus?.blockId) {
        const selected = blocks.find(block => String(block.id || '') === String(selection.focus.blockId));
        if (selected) {
            return selected;
        }
    }

    return blocks[0] || null;
}

function nonNegative(value, fallback) {
    const parsed = Number(value);
    return Number.isFinite(parsed) && parsed >= 0 ? parsed : fallback;
}

function pointsToCssPixels(value) {
    return (Number(value) || 0) * 96 / 72;
}
