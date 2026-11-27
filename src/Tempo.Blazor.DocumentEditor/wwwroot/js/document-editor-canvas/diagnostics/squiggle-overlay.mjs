import { mapDiagnosticRects } from './proofing-service.mjs';
import { extractTextRects } from '../render/text-rects.mjs';

const SQUIGGLE_COLOR = '#dc2626';

export function createCanvasProofingSquiggleOverlay(options = {}) {
    const doc = options.document || globalThis.document;
    const canvasStack = options.canvasStack;
    let painted = [];

    function update(proofingSnapshot, render) {
        clearCanvases();
        clearMetadataLayers();
        const diagnostics = proofingSnapshot?.diagnostics || [];
        const textRects = extractTextRects(render);
        painted = [];

        for (const diagnostic of diagnostics) {
            const rects = mapDiagnosticRects(diagnostic, textRects);
            for (const rect of rects) {
                paintSquiggle(rect);
                appendMetadata(diagnostic, rect);
                painted.push({ diagnostic, rect });
            }
        }

        canvasStack?.root?.setAttribute?.('data-canvas-proofing-count', String(diagnostics.length));
        canvasStack?.root?.setAttribute?.('data-canvas-proofing-squiggle-count', String(painted.length));
        canvasStack?.root?.setAttribute?.('data-canvas-proofing-revision', String(proofingSnapshot?.revision || 0));
        return snapshot();
    }

    function diagnosticAtPoint(pageIndex, x, y) {
        const px = Number(x || 0) || 0;
        const py = Number(y || 0) || 0;
        return painted.find(item => {
            const rect = item.rect || {};
            return Number(rect.pageIndex || 0) === Number(pageIndex || 0)
                && px >= Number(rect.x || 0) - 2
                && px <= Number(rect.x || 0) + Math.max(1, Number(rect.width || 0)) + 2
                && py >= Number(rect.y || 0)
                && py <= Number(rect.y || 0) + Math.max(1, Number(rect.height || 0)) + 6;
        })?.diagnostic || null;
    }

    function snapshot() {
        return {
            squiggleCount: painted.length,
            diagnostics: painted.map(item => ({
                id: item.diagnostic?.id || '',
                blockId: item.diagnostic?.blockId || '',
                start: item.diagnostic?.start || 0,
                end: item.diagnostic?.end || 0,
                pageIndex: item.rect?.pageIndex || 0,
            })),
        };
    }

    function destroy() {
        clearCanvases();
        clearMetadataLayers();
        painted = [];
    }

    function paintSquiggle(rect) {
        const context = diagnosticsContext(rect.pageIndex);
        if (!context) {
            return;
        }

        const x = Number(rect.x || 0) || 0;
        const width = Math.max(1, Number(rect.width || 0) || 0);
        const y = (Number(rect.y || 0) || 0) + Math.max(1, Number(rect.height || 0) || 16) - 2;
        context.save?.();
        context.strokeStyle = SQUIGGLE_COLOR;
        context.lineWidth = 1.35;
        context.beginPath?.();
        let cursor = x;
        let up = true;
        context.moveTo?.(cursor, y);
        while (cursor <= x + width) {
            cursor += 3;
            context.lineTo?.(Math.min(cursor, x + width), y + (up ? 2 : -1));
            up = !up;
        }

        context.stroke?.();
        context.restore?.();
    }

    function appendMetadata(diagnostic, rect) {
        const page = canvasStack?.pages?.get?.(String(rect.pageIndex));
        if (!page?.pageElement || !doc?.createElement) {
            return;
        }

        const layer = ensureMetadataLayer(page.pageElement);
        const element = doc.createElement('div');
        element.className = 'tm-document-canvas-proofing-squiggle-hit';
        element.setAttribute('data-testid', 'document-canvas-spell-squiggle');
        element.setAttribute('data-canvas-proofing-diagnostic-id', diagnostic.id || '');
        element.setAttribute('data-block-id', diagnostic.blockId || '');
        element.setAttribute('data-canvas-start-offset', String(diagnostic.start || 0));
        element.setAttribute('data-canvas-end-offset', String(diagnostic.end || 0));
        element.setAttribute('data-canvas-word', diagnostic.word || '');
        element.setAttribute('data-canvas-suggestions', (diagnostic.suggestions || []).join('|'));
        element.style.position = 'absolute';
        element.style.left = `${Number(rect.x || 0) || 0}px`;
        element.style.top = `${(Number(rect.y || 0) || 0) + Math.max(1, Number(rect.height || 0) || 16) - 4}px`;
        element.style.width = `${Math.max(1, Number(rect.width || 0) || 0)}px`;
        element.style.height = '8px';
        element.style.pointerEvents = 'none';
        element.style.background = 'transparent';
        layer.appendChild(element);
    }

    function ensureMetadataLayer(pageElement) {
        let layer = typeof pageElement.querySelector === 'function'
            ? pageElement.querySelector('[data-testid="document-canvas-spell-overlay"]')
            : null;
        if (!layer) {
            layer = doc.createElement('div');
            layer.className = 'tm-document-canvas-proofing-overlay';
            layer.setAttribute('data-testid', 'document-canvas-spell-overlay');
            layer.setAttribute('aria-hidden', 'true');
            layer.style.position = 'absolute';
            layer.style.inset = '0';
            layer.style.pointerEvents = 'none';
            pageElement.appendChild(layer);
        }

        return layer;
    }

    function clearCanvases() {
        for (const page of canvasStack?.pages?.values?.() || []) {
            const canvas = page.layers?.get?.('diagnostics');
            const context = canvas?.getContext?.('2d');
            if (context) {
                context.clearRect(0, 0, page.layout?.width || canvas.width || 0, page.layout?.height || canvas.height || 0);
            }
        }
    }

    function clearMetadataLayers() {
        for (const page of canvasStack?.pages?.values?.() || []) {
            const layer = page.pageElement?.querySelector?.('[data-testid="document-canvas-spell-overlay"]');
            layer?.replaceChildren?.();
        }
    }

    function diagnosticsContext(pageIndex) {
        const page = canvasStack?.pages?.get?.(String(pageIndex));
        return page?.layers?.get?.('diagnostics')?.getContext?.('2d') || null;
    }

    return {
        update,
        diagnosticAtPoint,
        snapshot,
        destroy,
    };
}
