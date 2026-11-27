import { extractTextRects } from '../render/text-rects.mjs';
import { mapDiagnosticRects } from '../diagnostics/proofing-service.mjs';

export function createCanvasSearchOverlay(options = {}) {
    const doc = options.document || globalThis.document;
    const canvasStack = options.canvasStack;

    function update(searchState = {}, render = null) {
        clear();
        const matches = Array.isArray(searchState?.matches) ? searchState.matches : [];
        if (matches.length === 0 || !canvasStack?.pages) {
            return snapshot();
        }

        const activeIndex = Math.max(0, Number(searchState.activeIndex || 0) || 0);
        const textRects = extractTextRects(render);
        let rectCount = 0;
        for (const match of matches) {
            const fragments = rectsForMatch(textRects, match);
            for (const fragment of fragments) {
                appendHighlight(fragment, match.index === activeIndex);
                rectCount += 1;
            }
        }

        // The active match IS the selection while the user steps through results: the engine host carries a flag so the
        // stylesheet can hide the blue selection rectangle (selection state itself stays untouched) and the amber match is
        // not muddied by the blue wash stacked beneath it. Only while a highlight is really painted - hiding the selection
        // with nothing to replace it would make the match invisible.
        if (rectCount > 0) {
            engineHost()?.setAttribute?.('data-canvas-search-active', 'true');
        }

        canvasStack.root?.setAttribute?.('data-canvas-search-match-count', String(matches.length));
        canvasStack.root?.setAttribute?.('data-canvas-search-highlight-count', String(rectCount));
        canvasStack.root?.setAttribute?.('data-canvas-search-active-index', String(activeIndex));
        return snapshot();
    }

    function clear() {
        if (!canvasStack?.pages) {
            return;
        }

        for (const page of canvasStack.pages.values()) {
            const layer = findLayer(page.pageElement);
            if (layer) {
                layer.replaceChildren?.();
                while (layer.children?.length) {
                    layer.children[0].remove?.();
                }
            }
        }

        canvasStack.root?.setAttribute?.('data-canvas-search-match-count', '0');
        canvasStack.root?.setAttribute?.('data-canvas-search-highlight-count', '0');
        engineHost()?.removeAttribute?.('data-canvas-search-active');
    }

    /** The element carrying .tm-document-canvas-engine-host (the root's ancestor); the root itself when it is detached. */
    function engineHost() {
        const root = canvasStack?.root;
        return root?.closest?.('.tm-document-canvas-engine-host') || root || null;
    }

    function appendHighlight(fragment, active) {
        const page = canvasStack.pages.get(String(fragment.pageIndex));
        if (!page?.pageElement) {
            return;
        }

        const layer = ensureLayer(page.pageElement);
        const element = doc.createElement('div');
        element.className = active
            ? 'tm-document-canvas-search-highlight tm-document-canvas-search-highlight--active'
            : 'tm-document-canvas-search-highlight';
        element.setAttribute('data-testid', active ? 'document-canvas-search-active' : 'document-canvas-search-match');
        element.setAttribute('data-canvas-search-highlight', active ? 'active' : 'match');
        element.style.position = 'absolute';
        // Layout units -> screen pixels: the page element carries the zoom factor (same as the selection layer).
        const scale = Math.max(0.01, Number(page.pageElement.getAttribute?.('data-canvas-page-zoom-scale') || 1) || 1);
        element.style.left = `${fragment.x * scale}px`;
        element.style.top = `${fragment.y * scale}px`;
        element.style.width = `${Math.max(1, fragment.width * scale)}px`;
        element.style.height = `${Math.max(1, fragment.height * scale)}px`;
        layer.appendChild(element);
    }

    function ensureLayer(pageElement) {
        let layer = findLayer(pageElement);
        if (!layer) {
            layer = doc.createElement('div');
            layer.setAttribute('data-testid', 'document-canvas-search-layer');
            layer.style.position = 'absolute';
            layer.style.inset = '0';
            layer.style.pointerEvents = 'none';
            // Above the selection layer (z-index 12): the active match IS the selection while the user steps through
            // results, and the amber highlight must stay visible instead of reading as the blue selection tint.
            // Colours and the radius live in _document-editor-find.css (--tm-document-highlight-match[-active]).
            layer.style.zIndex = '13';
            pageElement.appendChild(layer);
        }

        return layer;
    }

    function snapshot() {
        return {
            highlightCount: Number(canvasStack?.root?.getAttribute?.('data-canvas-search-highlight-count') || 0) || 0,
            matchCount: Number(canvasStack?.root?.getAttribute?.('data-canvas-search-match-count') || 0) || 0,
        };
    }

    return { update, clear, destroy: clear, snapshot };
}

function rectsForMatch(textRects, match) {
    // The same range -> rectangles mapping the proofing squiggles use (one implementation, one set of fixtures).
    return mapDiagnosticRects(
        { blockId: match?.blockId, start: match?.start, end: match?.end },
        textRects);
}

function findLayer(pageElement) {
    return pageElement?.querySelector?.('[data-testid="document-canvas-search-layer"]') || null;
}
