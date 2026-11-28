import { buildDisplayList } from './display-list.mjs';
import { CANVAS_CACHE_LAYER_KINDS, CANVAS_LAYER_KINDS } from './layers.mjs';
import { paintDisplayList } from './canvas-renderer.mjs';
import { ensureStyleStore, findStyle } from '../styles/style-store.mjs';
import { resolveStyle } from '../styles/style-resolver.mjs';
import { normalizeFieldType } from '../fields/field-engine.mjs';
import { normalizeMarkType } from '../layout/canvas-text-style.mjs';
import { createCanvasViewState, viewPresentation } from '../view/view-modes.mjs';
import { createPageVirtualizer } from '../perf/page-virtualizer.mjs';
import { createTileCache } from '../perf/tile-cache.mjs';
import { createFontMetricsService } from '../../document-editor/layout/font-metrics.mjs';

export { CANVAS_LAYER_KINDS };

/**
 * The inline padding of the page column inside the engine root. A desktop keeps the view mode's own padding (24 px a side in print mode); a tablet and a
 * phone container (the editor's `data-layout`) trade it for a hairline so a page fitted to the column fills it instead of leaving a bezel of 24 px a side.
 * The fit reads the same number (`getFitPaddingInline`), so what the fit leaves around the page is what is there.
 */
export const COMPACT_ROOT_PADDING_INLINE = 4;

export function resolveRootPaddingInline(element, rootPadding) {
    try {
        const layout = element?.closest?.('[data-layout]')?.getAttribute?.('data-layout');
        return layout && layout !== 'desktop' ? Math.min(rootPadding, COMPACT_ROOT_PADDING_INLINE) : rootPadding;
    } catch {
        return rootPadding;
    }
}

export function createCanvasStack(options = {}) {
    const doc = options.document;
    if (!doc || typeof doc.createElement !== 'function') {
        throw new Error('CanvasDocumentEngine requires a DOM-like document with createElement.');
    }

    const pixelRatioProvider = typeof options.pixelRatioProvider === 'function'
        ? options.pixelRatioProvider
        : () => globalThis.devicePixelRatio || 1;
    const theme = options.theme || {};
    const root = doc.createElement('div');
    root.className = 'tm-document-canvas-engine';
    root.setAttribute('data-testid', 'document-canvas-engine-root');
    root.setAttribute('data-page-surface-strategy', 'canvas-per-visible-page');
    root.style.position = 'relative';
    root.style.display = 'grid';
    root.style.justifyContent = 'center';
    root.style.gap = '24px';
    root.style.padding = '24px';
    // The stage is transparent: the workspace token paints the whole document column (the host and the surface), so the page column never shows as a lighter band.
    root.style.background = readThemeValue(theme, 'workspaceBackground', 'transparent');
    const topSpacer = createSpacer(doc, 'document-canvas-virtual-top-spacer');
    const bottomSpacer = createSpacer(doc, 'document-canvas-virtual-bottom-spacer');
    root.appendChild(topSpacer);
    root.appendChild(bottomSpacer);

    const pages = new Map();
    const virtualizer = createPageVirtualizer({
        bufferPages: Number(options.virtualization?.bufferPages ?? 1) || 1,
        enabled: options.virtualization?.enabled !== false,
        pageGap: 24,
        paddingBlock: 24,
    });
    const tileCache = createTileCache({
        maxEntries: Number(options.tileCache?.maxEntries ?? 160) || 160,
    });
    // The last computed render plan (layout + display list + view state). `repaint` reuses it so a
    // scroll/zoom re-paints visible pages WITHOUT re-running the document layout (buildDisplayList).
    let lastPlan = null;
    // Incremental layout cache: persists per-block paragraph layout across recalcs so an edit only
    // re-lays-out the changed block and everything after it (Phase 3).
    const layoutBlockCache = new Map();
    // Per-fragment display-command cache (Phase 4): keyed by the (cached) fragment object, so an edit
    // re-assembles display commands only for the changed block and reuses the rest.
    const commandDisplayCache = new WeakMap();
    // Persistent font-metrics service (Phase N1): measureText results only depend on the style
    // fingerprint (which includes zoom), so the LRU cache must survive across layout passes —
    // a keystroke then re-measures only genuinely new runs instead of the whole edited paragraph
    // plus headers/footers/notes. Cap raised from the 4096 default: one cache now serves the whole
    // document lifetime. Cleared defensively on a devicePixelRatio change (the key has no DPR part).
    const fontMetricsService = createFontMetricsService({ cacheLimit: 8192 });
    let fontMetricsPixelRatio = null;

    // Page offset/scale snapshot shared by the DOM overlays (comments/revisions/presence). Reading
    // offsetLeft/offsetTop forces a synchronous reflow, and each overlay used to read them PER MARKER while
    // also writing to the DOM — layout thrashing that dominated per-keystroke profiles. The cache survives
    // across paints: page offsets only move when the vertical geometry changes (pages mount/unmount/resize,
    // spacer height, zoom — tracked via a signature computed from plan data, no DOM reads) or when the root
    // itself is resized (tracked by a ResizeObserver), so steady typing performs NO forced reflow at all.
    let pagePlacementsCache = null;
    let pagePlacementsSignature = null;
    let placementResizeObserver = null;

    function getPagePlacements() {
        if (!pagePlacementsCache) {
            pagePlacementsCache = new Map();
            for (const [key, page] of pages) {
                const pageElement = page?.pageElement || null;
                pagePlacementsCache.set(String(key), {
                    offsetX: Number(pageElement?.offsetLeft || 0) || 0,
                    offsetY: Number(pageElement?.offsetTop || 0) || 0,
                    scale: Math.max(0.01, Number(pageElement?.getAttribute?.('data-canvas-page-zoom-scale') || 1) || 1),
                });
            }
        }

        return pagePlacementsCache;
    }

    function ensurePage(pageLayout) {
        const key = String(pageLayout.index);
        if (pages.has(key)) {
            return pages.get(key);
        }

        const pageElement = doc.createElement('section');
        pageElement.className = 'tm-document-canvas-page';
        pageElement.setAttribute('data-testid', 'document-canvas-page');
        pageElement.setAttribute('data-page-index', key);
        pageElement.style.position = 'relative';
        pageElement.style.width = `${pageLayout.width}px`;
        pageElement.style.height = `${pageLayout.height}px`;
        pageElement.style.background = readThemeValue(theme, 'pageBackground', 'var(--tm-color-surface, #ffffff)');
        pageElement.style.boxShadow = readThemeValue(theme, 'pageShadow', 'var(--tm-shadow-lg, 0 14px 34px rgba(15, 23, 42, 0.18))');
        pageElement.style.border = readThemeValue(theme, 'pageBorder', '1px solid var(--tm-border-color, rgba(148, 163, 184, 0.45))');

        const layers = new Map();
        for (const kind of CANVAS_LAYER_KINDS) {
            const canvas = doc.createElement('canvas');
            canvas.className = `tm-document-canvas-layer tm-document-canvas-layer--${kind}`;
            canvas.setAttribute('data-testid', `document-canvas-layer-${kind}`);
            canvas.setAttribute('data-canvas-layer', kind);
            canvas.setAttribute('aria-hidden', 'true');
            canvas.style.position = 'absolute';
            canvas.style.inset = '0';
            canvas.style.width = `${pageLayout.width}px`;
            canvas.style.height = `${pageLayout.height}px`;
            canvas.style.pointerEvents = kind === 'selection-caret' ? 'none' : 'auto';
            pageElement.appendChild(canvas);
            layers.set(kind, canvas);
        }

        insertPageInDomOrder(root, pageElement, Number(pageLayout.index || 0) || 0, pages, bottomSpacer);
        const page = { pageElement, layers, layout: pageLayout, needsFirstPaint: true };
        pages.set(key, page);
        return page;
    }

    // Builds the (expensive) render plan: runs the document layout and the display list. Called only
    // on a real recalc (mount/edit/command), never on scroll.
    function buildRenderPlan(layout, model = {}, options = {}) {
        const pixelRatio = Math.max(1, Number(pixelRatioProvider()) || 1);
        if (fontMetricsPixelRatio !== null && pixelRatio !== fontMetricsPixelRatio) {
            fontMetricsService.clearCache();
        }
        fontMetricsPixelRatio = pixelRatio;
        const viewState = createCanvasViewState(options.viewState || {});
        const presentation = viewPresentation(viewState);
        const zoomScale = Math.max(0.01, Number(viewState.zoom?.scale || 1) || 1);
        const contentControlRenderMode = options.contentControlRenderMode || theme.contentControlRenderMode;
        const displayList = buildDisplayList(model, layout, {
            theme,
            debug: theme.debug === true,
            contentControlRenderMode,
            signingRoles: Array.isArray(options.signingRoles) ? options.signingRoles : [],
            layoutCache: layoutBlockCache,
            commandCache: commandDisplayCache,
            fontMetrics: fontMetricsService,
            // Perf plan N11.2: progressive first layout budget + the resume token continuing it.
            layoutBudget: options.layoutBudget || null,
            layoutResume: options.layoutResume || null,
        });
        const allRenderPages = Array.isArray(displayList.pages) && displayList.pages.length > 0
            ? displayList.pages
            : (Array.isArray(layout?.pages) ? layout.pages : []);
        return {
            layout,
            model,
            viewState,
            presentation,
            zoomScale,
            contentControlRenderMode,
            displayList,
            allRenderPages,
            layoutComplete: displayList.layoutComplete !== false,
            layoutResume: displayList.layoutResume || null,
            layoutProgress: displayList.layoutProgress || null,
        };
    }

    // Full recalc + paint. Use for mount/edit/command (anything that changes the model or layout).
    function render(layout, model = {}, options = {}) {
        lastPlan = buildRenderPlan(layout, model, options);
        return paintRenderPlan(lastPlan, options);
    }

    // Paint-only re-render for scroll/zoom. Reuses the cached plan (no layout / display list rebuild);
    // re-plans virtualization for the new viewport and repaints the newly visible pages.
    function repaint(options = {}) {
        if (!lastPlan) {
            return null;
        }

        if (options.viewState) {
            lastPlan.viewState = createCanvasViewState(options.viewState);
            lastPlan.presentation = viewPresentation(lastPlan.viewState);
            lastPlan.zoomScale = Math.max(0.01, Number(lastPlan.viewState.zoom?.scale || 1) || 1);
        }

        return paintRenderPlan(lastPlan, { ...options, dirtyBlockIds: [], structural: false });
    }

    // Paints a render plan to the visible pages. Pure with respect to the document model: it only
    // depends on the cached plan and the current viewport, so it is safe to call repeatedly on scroll.
    function paintRenderPlan(plan, options = {}) {
        const layout = plan.layout;
        const model = plan.model || {};
        const viewState = plan.viewState;
        const presentation = plan.presentation;
        const zoomScale = plan.zoomScale;
        const contentControlRenderMode = plan.contentControlRenderMode;
        const displayList = plan.displayList;
        const allRenderPages = plan.allRenderPages;
        const pixelRatio = Math.max(1, Number(pixelRatioProvider()) || 1);
        const virtualization = virtualizer.plan(allRenderPages, options.viewport || {}, { scale: zoomScale });
        const renderPages = virtualization.pages;

        // Invalidate the shared overlay placement snapshot ONLY when the page geometry can actually move
        // (different mounted pages, page dimensions, spacer height or zoom). Typing inside a page keeps the
        // signature stable, so overlays keep reading cached offsets with zero forced reflows.
        const placementSignature = `${zoomScale}|${Math.round(Number(virtualization.topSpacerHeight) || 0)}|`
            + renderPages.map(page => `${page.index}:${Math.round(Number(page.width) || 0)}x${Math.round(Number(page.height) || 0)}`).join(',');
        if (placementSignature !== pagePlacementsSignature) {
            pagePlacementsSignature = placementSignature;
            pagePlacementsCache = null;
        }
        const activePageKeys = new Set(renderPages.map(page => String(page.index)));

        for (const [key, page] of pages) {
            if (!activePageKeys.has(key)) {
                removeElement(page.pageElement);
                pages.delete(key);
            }
        }

        // Perf plan N6.1: the command-filter diagnostics (each an O(all commands) scan, testing
        // contract only) are skipped on the per-keystroke input render; the debounced idle
        // reconciliation render publishes the settled values, so E2E reads after a pause see them.
        const deferPaintDiagnostics = options.deferPaintDiagnostics === true;
        root.setAttribute('data-canvas-page-count', String(allRenderPages.length));
        root.setAttribute('data-canvas-layout-has-landscape-page', String(allRenderPages.some(page => (Number(page?.width || 0) || 0) > (Number(page?.height || 0) || 0))));
        root.setAttribute('data-canvas-layout-section-ids', allRenderPages.map(page => String(page?.sectionId || '')).filter(Boolean).join(','));
        if (!deferPaintDiagnostics) {
            root.setAttribute('data-canvas-column-separator-count', String(displayList.commands.filter(command => command.type === 'columnSeparator').length));
            root.setAttribute('data-canvas-line-number-count', String(displayList.commands.filter(command => command.type === 'lineNumber').length));
        }
        root.setAttribute('data-canvas-mounted-page-count', String(renderPages.length));
        root.setAttribute('data-canvas-virtualization-enabled', String(virtualization.enabled === true));
        root.setAttribute('data-canvas-virtualization-progressive', String(virtualization.progressive === true));
        root.setAttribute('data-canvas-visible-page-indexes', virtualization.visiblePageIndexes.join(','));
        root.setAttribute('data-canvas-layout-cache-hit-count', String(displayList.layoutCacheStats?.hits || 0));
        root.setAttribute('data-canvas-layout-cache-miss-count', String(displayList.layoutCacheStats?.misses || 0));
        root.setAttribute('data-canvas-command-cache-hit-count', String(displayList.commandCacheStats?.hits || 0));
        root.setAttribute('data-canvas-command-cache-miss-count', String(displayList.commandCacheStats?.misses || 0));
        if (!deferPaintDiagnostics) {
            root.setAttribute('data-canvas-tab-leader-count', String(displayList.commands.filter(command => command.type === 'tabLeader').length));
            root.setAttribute('data-canvas-dotted-tab-leader-count', String(displayList.commands.filter(command => command.type === 'tabLeader' && command.leader === 'dots').length));
        }
        root.setAttribute('data-canvas-view-mode', presentation.mode);
        root.setAttribute('data-canvas-view-toolbar-hidden', String(presentation.toolbarHidden === true));
        root.setAttribute('data-canvas-zoom-percent', String(viewState.zoom.percent));
        root.setAttribute('data-canvas-zoom-preset', viewState.zoom.preset);
        root.setAttribute('data-canvas-zoom-auto-fit', viewState.zoom.autoFit || 'off');
        root.setAttribute('data-canvas-print-preview-active', String(viewState.printPreview.active === true));
        root.style.gap = `${presentation.rootGap}px`;
        root.style.padding = `${presentation.rootPadding}px ${resolveRootPaddingInline(root, presentation.rootPadding)}px`;
        // Perf plan N11.3: while the progressive first layout is incomplete, the bottom spacer adds
        // an ESTIMATE of the unlaid tail (remaining blocks ÷ laid blocks-per-page × average page
        // stride) so the scrollbar length approximates the final document immediately; each idle
        // chunk recomputes the estimate until real pages replace it.
        const layoutComplete = plan.layoutComplete !== false;
        let estimatedRemainingHeight = 0;
        let estimatedTotalPages = allRenderPages.length;
        if (!layoutComplete && plan.layoutProgress) {
            const laidBlocks = Math.max(1, Number(plan.layoutProgress.laidBlockCount) || 1);
            const totalBlocks = Math.max(laidBlocks, Number(plan.layoutProgress.totalBlockCount) || laidBlocks);
            const laidPages = Math.max(1, allRenderPages.length);
            const blocksPerPage = laidBlocks / laidPages;
            const remainingPages = Math.ceil((totalBlocks - laidBlocks) / Math.max(0.001, blocksPerPage));
            const averagePageHeight = allRenderPages.reduce((sum, page) => sum + (Number(page.height) || 0), 0) / laidPages;
            estimatedRemainingHeight = Math.max(0, remainingPages * ((averagePageHeight * zoomScale) + presentation.rootGap));
            estimatedTotalPages = allRenderPages.length + remainingPages;
        }
        root.setAttribute('data-canvas-layout-complete', String(layoutComplete));
        root.setAttribute('data-canvas-estimated-page-count', String(estimatedTotalPages));
        topSpacer.style.height = `${Math.max(0, virtualization.topSpacerHeight)}px`;
        bottomSpacer.style.height = `${Math.max(0, virtualization.bottomSpacerHeight + estimatedRemainingHeight)}px`;
        topSpacer.setAttribute('data-canvas-spacer-height', String(Math.round(Math.max(0, virtualization.topSpacerHeight))));
        bottomSpacer.setAttribute('data-canvas-spacer-height', String(Math.round(Math.max(0, virtualization.bottomSpacerHeight + estimatedRemainingHeight))));
        const incremental = createIncrementalPlan(displayList, options);
        // Model-wide diagnostic counters are identical for every page and each one scans the whole
        // model, so compute them ONCE per paint instead of O(visible pages) times (Phase 5.3).
        // N6.1: skipped entirely on the deferred input render — the idle render publishes them.
        const modelDiagnostics = deferPaintDiagnostics ? null : computeModelDiagnostics(model);

        for (const pageLayout of renderPages) {
            const page = ensurePage(pageLayout);
            const pageIndex = Number(pageLayout.index || 0) || 0;
            const pageNeedsFirstPaint = page.needsFirstPaint === true;
            const cssWidth = Math.max(1, (Number(pageLayout.width) || 1) * zoomScale);
            const cssHeight = Math.max(1, (Number(pageLayout.height) || 1) * zoomScale);
            const backingStoreChanged = hasPageBackingStoreChanged(page, cssWidth, cssHeight, pixelRatio);
            const cacheDecision = tileCache.shouldRepaint(pageIndex, displayList, {
                dirtyPageIndexes: incremental.repaintPageIndexes,
                force: options.forceRepaint === true || pageNeedsFirstPaint || backingStoreChanged,
            });
            const repaintPage = incremental.repaintPageIndexes === null
                ? cacheDecision.repaint
                : pageNeedsFirstPaint || backingStoreChanged || incremental.repaintPageIndexes.has(pageIndex) || cacheDecision.repaint;
            page.layout = pageLayout;
            page.pageElement.style.width = `${cssWidth}px`;
            page.pageElement.style.height = `${cssHeight}px`;
            page.pageElement.style.boxShadow = readThemeValue(theme, 'pageShadow', presentation.pageShadow);
            page.pageElement.style.border = readThemeValue(theme, 'pageBorder', presentation.pageBorder);
            page.pageElement.setAttribute('data-canvas-page-zoom-scale', String(zoomScale));
            page.pageElement.setAttribute('data-canvas-page-logical-width', String(Math.round(Number(pageLayout.width) || 0)));
            page.pageElement.setAttribute('data-canvas-page-logical-height', String(Math.round(Number(pageLayout.height) || 0)));
            page.pageElement.setAttribute('data-canvas-page-section-id', String(pageLayout.sectionId || ''));
            page.pageElement.setAttribute('data-canvas-page-css-width', String(Math.round(cssWidth)));
            page.pageElement.setAttribute('data-canvas-page-css-height', String(Math.round(cssHeight)));
            for (const kind of CANVAS_LAYER_KINDS) {
                if (repaintPage || kind === 'selection-caret') {
                    configureCanvas(page.layers.get(kind), pageLayout.width, pageLayout.height, pixelRatio, zoomScale);
                    clearLayer(page.layers.get(kind), pageLayout);
                } else {
                    configureCanvasWithoutClearing(page.layers.get(kind), pageLayout.width, pageLayout.height, pixelRatio, zoomScale);
                }
                // Repaint hook for async image loads (image.onload): reuse the already-computed
                // display list instead of re-laying-out the whole document (Phase 5.4).
                page.layers.get(kind).__tmCanvasRepaint = () => repaintPageFromDisplayList(page, pageLayout, displayList, contentControlRenderMode);
            }

            const pageCommands = repaintPage
                ? displayList.commands.filter(command => command.pageIndex === pageLayout.index)
                : [];
            const pageDisplayList = {
                ...displayList,
                commands: pageCommands,
            };
            // Two-pass paint: margin/decoration/object content is painted first without a clip
            // (it legitimately lives in the margins or is page-clipped already), then body text
            // flow is painted clipped to the page body so a mislaid-out run cannot bleed out.
            const { bodyCommands, marginCommands } = partitionPageCommands(pageCommands);
            const marginSummary = paintDisplayList(
                page.layers,
                { ...displayList, commands: marginCommands },
                { contentControlRenderMode });
            const bodySummary = paintDisplayList(
                page.layers,
                { ...displayList, commands: bodyCommands },
                { contentControlRenderMode, clipRect: pageBodyClipRect(pageLayout) });
            const paintSummary = {
                paintedCommandCount: marginSummary.paintedCommandCount + bodySummary.paintedCommandCount,
                textRunCount: marginSummary.textRunCount + bodySummary.textRunCount,
                mathEquationCount: marginSummary.mathEquationCount + bodySummary.mathEquationCount,
                contentControlCount: marginSummary.contentControlCount + bodySummary.contentControlCount,
                diagnosticCount: marginSummary.diagnosticCount + bodySummary.diagnosticCount,
            };
            if (repaintPage) {
                tileCache.commitPage(pageIndex, cacheDecision.signature, {
                    paintedCommandCount: paintSummary.paintedCommandCount,
                });
                page.needsFirstPaint = false;
            }
            // Perf plan N6.1: ~35 diagnostic attributes, most an O(page commands) filter — testing
            // contract only. Deferred on the input render; the first non-deferred render (idle
            // reconciliation) catches up with the page's REAL commands even when the tile cache
            // skips the repaint, so post-pause reads see the typed content.
            if (deferPaintDiagnostics) {
                page.paintDiagnosticsStale = true;
            } else if (repaintPage || page.paintDiagnosticsStale === true) {
            // Skipped-and-not-stale pages keep their previous attribute values: the page content did
            // not change, and recomputing from the (empty) skipped-paint command list zeroed every
            // per-page count — surfaced by the N11 progressive continuations, which re-run the paint
            // plan several times right after load while the first pages are unchanged.
            const diagnosticsDisplayList = repaintPage
                ? pageDisplayList
                : { ...displayList, commands: displayList.commands.filter(command => Number(command.pageIndex || 0) === Number(pageLayout.index || 0)) };
            page.paintDiagnosticsStale = false;
            page.pageElement.setAttribute('data-canvas-model-document-id', modelDiagnostics.documentId);
            page.pageElement.setAttribute('data-canvas-model-block-count', modelDiagnostics.blockCount);
            page.pageElement.setAttribute('data-canvas-model-section-count', modelDiagnostics.sectionCount);
            page.pageElement.setAttribute('data-canvas-model-section-ids', modelDiagnostics.sectionIds);
            page.pageElement.setAttribute('data-canvas-model-section-block-counts', modelDiagnostics.sectionBlockCounts);
            page.pageElement.setAttribute('data-canvas-model-hyphenation-enabled', modelDiagnostics.hyphenationEnabled);
            page.pageElement.setAttribute('data-canvas-model-page-background-color', modelDiagnostics.pageBackgroundColor);
            page.pageElement.setAttribute('data-canvas-model-table-block-count', modelDiagnostics.tableBlockCount);
            page.pageElement.setAttribute('data-canvas-model-image-block-count', modelDiagnostics.imageBlockCount);
            page.pageElement.setAttribute('data-canvas-model-field-count', modelDiagnostics.fieldCount);
            page.pageElement.setAttribute('data-canvas-model-math-count', modelDiagnostics.mathCount);
            page.pageElement.setAttribute('data-canvas-model-content-control-count', modelDiagnostics.contentControlCount);
            page.pageElement.setAttribute('data-canvas-model-advanced-char-mark-count', modelDiagnostics.advancedCharMarkCount);
            page.pageElement.setAttribute('data-canvas-model-caption-count', modelDiagnostics.captionCount);
            page.pageElement.setAttribute('data-canvas-model-toc-entry-count', modelDiagnostics.tocEntryCount);
            page.pageElement.setAttribute('data-canvas-cross-reference-count', modelDiagnostics.crossReferenceCount);
            page.pageElement.setAttribute('data-canvas-table-of-figures-text', modelDiagnostics.tableOfFiguresText);
            page.pageElement.setAttribute('data-canvas-bibliography-text', modelDiagnostics.bibliographyText);
            page.pageElement.setAttribute('data-canvas-cross-reference-text', modelDiagnostics.crossReferenceText);
            page.pageElement.setAttribute('data-canvas-style-count', modelDiagnostics.styleCount);
            page.pageElement.setAttribute('data-canvas-style-heading1-font-size', modelDiagnostics.heading1FontSize);
            page.pageElement.setAttribute('data-canvas-render-command-count', String(diagnosticsDisplayList.commands.length));
            // Only a REAL repaint updates the painted-command diagnostic: a tile-cache-skipped
            // paint painted 0 commands by design, and overwriting the attribute with that 0 made an
            // already-painted page look blank to the E2E probes (surfaced by the N11 progressive
            // continuations, which re-run paintRenderPlan while earlier pages are unchanged).
            if (repaintPage || !page.pageElement.hasAttribute?.('data-canvas-painted-command-count')) {
                page.pageElement.setAttribute('data-canvas-painted-command-count', String(paintSummary.paintedCommandCount));
            }
            page.pageElement.setAttribute('data-canvas-text-run-count', String(diagnosticsDisplayList.commands.filter(command => command.type === 'textRun' || command.type === 'listLabel').length));
            page.pageElement.setAttribute('data-canvas-tab-leader-count', String(diagnosticsDisplayList.commands.filter(command => command.type === 'tabLeader').length));
            page.pageElement.setAttribute('data-canvas-dotted-tab-leader-count', String(diagnosticsDisplayList.commands.filter(command => command.type === 'tabLeader' && command.leader === 'dots').length));
            page.pageElement.setAttribute('data-canvas-hyphenated-text-run-count', String(diagnosticsDisplayList.commands.filter(command => command.type === 'textRun' && command.hyphenated === true).length));
            page.pageElement.setAttribute('data-canvas-watermark-count', String(diagnosticsDisplayList.commands.filter(command => command.type === 'watermarkText' || command.type === 'watermarkImage').length));
            page.pageElement.setAttribute('data-canvas-page-fill', String(diagnosticsDisplayList.commands.find(command => command.type === 'pageFill')?.fill || ''));
            page.pageElement.setAttribute('data-canvas-page-border-count', String(diagnosticsDisplayList.commands.filter(command => command.type === 'pageBorder').length));
            const balancedColumnLineCounts = pageBalancedColumnLineCounts(pageLayout, displayList);
            page.pageElement.setAttribute('data-canvas-column-count', String(Array.isArray(pageLayout.columns) ? pageLayout.columns.length : 1));
            page.pageElement.setAttribute('data-canvas-column-balanced', String(pageLayout.columnBalanced === true));
            page.pageElement.setAttribute('data-canvas-balanced-column-line-counts', balancedColumnLineCounts.join(','));
            page.pageElement.setAttribute('data-canvas-balanced-column-line-spread', String(columnLineSpread(balancedColumnLineCounts)));
            page.pageElement.setAttribute('data-canvas-column-separator-count', String(diagnosticsDisplayList.commands.filter(command => command.type === 'columnSeparator').length));
            page.pageElement.setAttribute('data-canvas-line-number-count', String(diagnosticsDisplayList.commands.filter(command => command.type === 'lineNumber').length));
            page.pageElement.setAttribute('data-canvas-header-footer-count', String(diagnosticsDisplayList.commands.filter(command => command.type === 'headerFooterFrame').length));
            page.pageElement.setAttribute('data-canvas-field-count', String(diagnosticsDisplayList.commands.filter(command => command.type === 'field').length));
            page.pageElement.setAttribute('data-canvas-math-count', String(diagnosticsDisplayList.commands.filter(command => command.type === 'mathEquation').length));
            page.pageElement.setAttribute('data-canvas-content-control-count', String(diagnosticsDisplayList.commands.filter(command => command.type === 'formControl').length));
            page.pageElement.setAttribute('data-canvas-note-count', String(diagnosticsDisplayList.commands.filter(command => command.type === 'noteMarker').length));
            page.pageElement.setAttribute('data-canvas-table-count', String(diagnosticsDisplayList.commands.filter(command => command.type === 'tableBox').length));
            page.pageElement.setAttribute('data-canvas-table-cell-count', String(diagnosticsDisplayList.commands.filter(command => command.type === 'tableCell').length));
            page.pageElement.setAttribute('data-canvas-object-count', String(diagnosticsDisplayList.commands.filter(isCanvasObjectCommand).length));
            page.pageElement.setAttribute('data-canvas-image-count', String(diagnosticsDisplayList.commands.filter(command => command.type === 'imageObject' || command.type === 'imageBox' || command.type === 'drawingRun').length));
            page.pageElement.setAttribute('data-canvas-drawing-count', String(diagnosticsDisplayList.commands.filter(command => command.type === 'drawingShape' || command.type === 'drawingLine' || command.type === 'drawingChart').length));
            page.pageElement.setAttribute('data-canvas-comment-anchor-count', String(diagnosticsDisplayList.commands.filter(command => command.type === 'commentAnchor').length));
            page.pageElement.setAttribute('data-canvas-revision-anchor-count', String(diagnosticsDisplayList.commands.filter(command => command.type === 'revisionAnchor').length));
            page.pageElement.setAttribute('data-canvas-diagnostic-count', String(diagnosticsDisplayList.commands.filter(command => command.layer === 'diagnostics').length));
            }
            if (repaintPage) {
                // The text-rect layer is test/diagnostic metadata (hundreds of DOM nodes on a full
                // page) with no runtime consumer. The per-keystroke input render defers it; the
                // debounced reconciliation render (or the next non-deferred paint) catches up.
                if (options.deferTextRectMetadata === true) {
                    page.textRectMetadataStale = true;
                } else {
                    syncTextRectMetadata(page, pageDisplayList, zoomScale);
                    page.textRectMetadataStale = false;
                }
                syncTableOfContentsHitMetadata(page, pageDisplayList, model, zoomScale);
                syncTableCellMetadata(page, pageDisplayList, zoomScale);
                syncObjectMetadata(page, pageDisplayList, zoomScale);
                syncHeaderFooterMetadata(page, pageDisplayList, zoomScale);
                syncNoteMetadata(page, pageDisplayList, zoomScale);
                syncContentControlMetadata(page, pageDisplayList, zoomScale);
            } else if (page.textRectMetadataStale === true && options.deferTextRectMetadata !== true) {
                syncTextRectMetadata(page, {
                    ...displayList,
                    commands: displayList.commands.filter(command => command.pageIndex === pageLayout.index),
                }, zoomScale);
                page.textRectMetadataStale = false;
            }
        }

        const tileCacheSnapshot = tileCache.snapshot();
        return {
            pixelRatio,
            pageCount: allRenderPages.length,
            mountedPageCount: renderPages.length,
            layerKinds: CANVAS_LAYER_KINDS.slice(),
            modelDocumentId: String(model?.documentId || ''),
            modelBlockCount: Array.isArray(model?.body?.blocks) ? model.body.blocks.length : 0,
            displayListCommandCount: displayList.commands.length,
            textRunCount: displayList.textRunCount,
            contentControlCount: displayList.contentControlCount,
            diagnosticCount: displayList.diagnosticCount,
            view: {
                mode: viewState.viewMode,
                zoomPercent: viewState.zoom.percent,
                zoomScale,
                zoomPreset: viewState.zoom.preset,
                zoomAutoFit: viewState.zoom.autoFit || 'off',
                printPreviewActive: viewState.printPreview.active === true,
            },
            displayList,
            selectionLayout: createSelectionLayout(displayList),
            measurementStats: displayList.measurementStats,
            incremental: {
                enabled: incremental.repaintPageIndexes !== null,
                repaintPageIndexes: incremental.repaintPageIndexes ? Array.from(incremental.repaintPageIndexes) : null,
                dirtyBlockIds: Array.from(incremental.dirtyBlockIds),
            },
            virtualization,
            tileCache: tileCacheSnapshot,
            // Perf plan N11: progressive first-layout state (resume token intentionally NOT included
            // here — it holds live loop state and is read via getLayoutState()).
            layoutComplete,
            layoutProgress: plan.layoutProgress || null,
        };
    }

    function mount(host) {
        host.appendChild(root);
        // Horizontal page offsets (centering) move when the root resizes without any paint — the geometry
        // signature above cannot see that, so a ResizeObserver drops the placement snapshot instead.
        const ResizeObserverCtor = globalThis.ResizeObserver;
        if (!placementResizeObserver && typeof ResizeObserverCtor === 'function') {
            placementResizeObserver = new ResizeObserverCtor(() => {
                pagePlacementsCache = null;
            });
            placementResizeObserver.observe(root);
        }

        return root;
    }

    function destroy() {
        if (root.parentNode) {
            root.parentNode.removeChild(root);
        }
        pages.clear();
        tileCache.invalidate();
        layoutBlockCache.clear();
        fontMetricsService.clearCache();
        fontMetricsPixelRatio = null;
        lastPlan = null;
        pagePlacementsCache = null;
        pagePlacementsSignature = null;
        placementResizeObserver?.disconnect?.();
        placementResizeObserver = null;
    }

    // Perf plan N11: progressive first-layout state of the last built plan. The resume token holds
    // live layout loop state (model + accumulator references) and is only ever consumed by the next
    // layout continuation — never serialized.
    function getLayoutState() {
        return {
            complete: lastPlan ? lastPlan.layoutComplete !== false : true,
            resume: lastPlan?.layoutResume || null,
            progress: lastPlan?.layoutProgress || null,
            laidPages: Array.isArray(lastPlan?.allRenderPages) ? lastPlan.allRenderPages.length : 0,
        };
    }

    return {
        root,
        mount,
        render,
        repaint,
        destroy,
        getLayoutState,
        pages,
        getPagePlacements,
    };
}

function createSpacer(doc, testId) {
    const spacer = doc.createElement('div');
    spacer.setAttribute('data-testid', testId);
    spacer.setAttribute('aria-hidden', 'true');
    spacer.style.width = '1px';
    spacer.style.height = '0px';
    spacer.style.pointerEvents = 'none';
    return spacer;
}

// Mount a page so the DOM keeps ascending page-index order regardless of scroll direction (B5). Inserting
// before the next-higher mounted page (rather than always before the bottom spacer) keeps page 0 above page
// 1 when an upward scroll mounts the lower page while the higher one is still mounted.
function insertPageInDomOrder(root, pageElement, pageIndex, pages, bottomSpacer) {
    if (typeof root.insertBefore !== 'function') {
        root.appendChild(pageElement);
        return;
    }

    let nextElement = null;
    let nextIndex = Infinity;
    for (const [key, page] of pages) {
        const mountedIndex = Number(key);
        if (mountedIndex > pageIndex && mountedIndex < nextIndex && page?.pageElement?.parentNode === root) {
            nextIndex = mountedIndex;
            nextElement = page.pageElement;
        }
    }

    root.insertBefore(pageElement, nextElement || bottomSpacer);
}

function createIncrementalPlan(displayList, options = {}) {
    const dirtyBlockIds = new Set((options.dirtyBlockIds || []).map(String).filter(Boolean));
    if (dirtyBlockIds.size === 0) {
        return { dirtyBlockIds, repaintPageIndexes: null };
    }

    const dirtyPageIndexes = new Set();
    for (const command of displayList.commands || []) {
        if (dirtyBlockIds.has(String(command.blockId || ''))) {
            dirtyPageIndexes.add(Number(command.pageIndex || 0) || 0);
        }
    }

    if (dirtyPageIndexes.size === 0) {
        return { dirtyBlockIds, repaintPageIndexes: null };
    }

    if (options.structural === true) {
        const firstDirtyPage = Math.min(...dirtyPageIndexes);
        return {
            dirtyBlockIds,
            repaintPageIndexes: new Set((displayList.pages || [])
                .map(page => Number(page.index || 0) || 0)
                .filter(pageIndex => pageIndex >= firstDirtyPage)),
        };
    }

    return { dirtyBlockIds, repaintPageIndexes: dirtyPageIndexes };
}

function createSelectionLayout(displayList) {
    const baseLayout = displayList.layout || {};
    const baseBlocks = Array.isArray(baseLayout.blocks) ? baseLayout.blocks : [];
    const objectMetadata = canvasObjectCommandMetadata(displayList.commands || []);
    let enrichedBaseChanged = false;
    const enrichedBaseBlocks = baseBlocks.map(block => {
        const enriched = enrichCanvasObjectBlock(block, objectMetadata);
        if (enriched !== block) {
            enrichedBaseChanged = true;
        }

        return enriched;
    });
    const existingBlockIds = new Set(enrichedBaseBlocks.map(block => String(block?.blockId || '')).filter(Boolean));
    const existingObjectIds = new Set(enrichedBaseBlocks
        .map(block => String(block?.objectId || block?.object?.objectId || ''))
        .filter(Boolean));
    const objectBlocks = objectSelectionBlocks(displayList.commands || [], existingObjectIds);
    const extraBlocks = extendedEditableBlocks(displayList.commands || [], existingBlockIds);
    const mathEquations = mathEquationSelectionBlocks(displayList.commands || []);
    const drawingText = drawingTextSelectionLines(displayList.commands || []);
    if (!enrichedBaseChanged && objectBlocks.length === 0 && extraBlocks.length === 0 && mathEquations.length === 0 && drawingText.length === 0) {
        return baseLayout;
    }

    if (objectBlocks.length === 0 && extraBlocks.length === 0) {
        return { ...baseLayout, blocks: enrichedBaseBlocks, mathEquations, drawingText };
    }

    return {
        ...baseLayout,
        blocks: [...enrichedBaseBlocks, ...objectBlocks, ...extraBlocks],
        mathEquations,
        drawingText,
    };
}

function canvasObjectCommandMetadata(commands) {
    const metadata = new Map();
    for (const command of commands) {
        if (!isCanvasObjectCommand(command)) {
            continue;
        }

        const objectId = String(command.objectId || '');
        if (!objectId || metadata.has(objectId)) {
            continue;
        }

        metadata.set(objectId, command);
    }

    return metadata;
}

function enrichCanvasObjectBlock(block, objectMetadata) {
    const objectId = String(block?.objectId || block?.object?.objectId || '');
    const command = objectId ? objectMetadata.get(objectId) : null;
    if (!command) {
        return block;
    }

    const rect = {
        x: Number(command.x ?? block?.rect?.x ?? 0) || 0,
        y: Number(command.y ?? block?.rect?.y ?? 0) || 0,
        width: Math.max(1, Number(command.width ?? block?.rect?.width ?? 0) || 1),
        height: Math.max(1, Number(command.height ?? block?.rect?.height ?? 0) || 1),
    };
    const connector = command.connector ? cloneConnector(command.connector) : null;
    return {
        ...block,
        objectId,
        type: block?.type || 'image',
        role: String(command.role || block?.role || block?.object?.role || (command.type === 'imageObject' ? 'imageBlock' : 'drawingRun')),
        pageIndex: Number(command.pageIndex ?? block?.pageIndex ?? 0) || 0,
        sequence: Number(command.sequence ?? block?.sequence ?? 0) || 0,
        rect,
        object: {
            ...(block?.object || {}),
            objectId,
            role: String(command.role || block?.object?.role || block?.role || (command.type === 'imageObject' ? 'imageBlock' : 'drawingRun')),
            wrapMode: String(command.wrapMode || block?.object?.wrapMode || 'Inline'),
            altText: String(command.altText ?? block?.object?.altText ?? ''),
            caption: String(command.caption ?? block?.object?.caption ?? ''),
            zIndex: Number(command.zIndex ?? block?.object?.zIndex ?? 0) || 0,
            kind: String(command.kind || block?.object?.kind || (command.type === 'imageObject' ? 'image' : 'drawing')),
            connector,
        },
        connector,
    };
}

function objectSelectionBlocks(commands, existingObjectIds) {
    const blocks = [];
    for (const command of commands) {
        if (!isCanvasObjectCommand(command)) {
            continue;
        }

        const objectId = String(command.objectId || '');
        if (!objectId || existingObjectIds.has(objectId)) {
            continue;
        }

        existingObjectIds.add(objectId);
        blocks.push({
            blockId: String(command.blockId || ''),
            runId: String(command.runId || ''),
            objectId,
            type: 'image',
            role: String(command.role || (command.type === 'imageObject' ? 'imageBlock' : 'drawingRun')),
            pageIndex: Number(command.pageIndex || 0) || 0,
            sequence: Number(command.sequence || 0) || 0,
            rect: {
                x: Number(command.x || 0) || 0,
                y: Number(command.y || 0) || 0,
                width: Math.max(1, Number(command.width || 0) || 1),
                height: Math.max(1, Number(command.height || 0) || 1),
            },
            object: {
                objectId,
                role: String(command.role || (command.type === 'imageObject' ? 'imageBlock' : 'drawingRun')),
                wrapMode: String(command.wrapMode || 'Inline'),
                altText: String(command.altText || ''),
                caption: String(command.caption || ''),
                zIndex: Number(command.zIndex || 0) || 0,
                kind: String(command.kind || (command.type === 'imageObject' ? 'image' : 'drawing')),
                connector: command.connector ? cloneConnector(command.connector) : null,
            },
            connector: command.connector ? cloneConnector(command.connector) : null,
        });
    }

    return blocks;
}

function cloneConnector(connector) {
    return {
        routing: String(connector?.routing || ''),
        start: clonePoint(connector?.start),
        end: clonePoint(connector?.end),
        points: Array.isArray(connector?.points) ? connector.points.map(clonePoint).filter(Boolean) : [],
        startConnection: connector?.startConnection ? { ...connector.startConnection } : null,
        endConnection: connector?.endConnection ? { ...connector.endConnection } : null,
    };
}

function clonePoint(point) {
    const x = Number(point?.x ?? point?.X);
    const y = Number(point?.y ?? point?.Y);
    if (!Number.isFinite(x) || !Number.isFinite(y)) {
        return null;
    }

    return { x, y };
}

function drawingTextSelectionLines(commands) {
    return commands
        .filter(command => command?.type === 'drawingText' && String(command.objectId || ''))
        .slice()
        .sort((left, right) =>
            (Number(left.pageIndex || 0) - Number(right.pageIndex || 0))
            || String(left.objectId || '').localeCompare(String(right.objectId || ''))
            || (Number(left.textStart ?? left.sequence ?? 0) - Number(right.textStart ?? right.sequence ?? 0))
            || (Number(left.sequence || 0) - Number(right.sequence || 0)))
        .map(command => {
            const text = String(command.text || '');
            const width = Math.max(1, Number(command.width || 0) || 1);
            const textStart = Math.max(0, Number(command.textStart ?? 0) || 0);
            const textEnd = Math.max(textStart, Number(command.textEnd ?? (textStart + text.length)) || textStart + text.length);
            return {
                objectId: String(command.objectId || ''),
                blockId: String(command.blockId || ''),
                runId: String(command.runId || ''),
                pageIndex: Number(command.pageIndex || 0) || 0,
                sequence: Number(command.sequence || 0) || 0,
                paragraphIndex: Number(command.paragraphIndex || 0) || 0,
                lineIndex: Number(command.lineIndex || 0) || 0,
                text,
                start: textStart,
                end: textEnd,
                x: Number(command.x || 0) || 0,
                y: Number(command.y || 0) || 0,
                baseline: Number(command.baseline || 0) || 0,
                width,
                height: Math.max(1, Number(command.height || 0) || Number(command.style?.fontSize || 14) * 1.25),
                align: String(command.align || 'left'),
                charWidth: width / Math.max(1, text.length),
                style: command.style ? { ...command.style } : {},
            };
        });
}

function extendedEditableBlocks(commands, existingBlockIds) {
    const groups = new Map();
    const editableCommands = commands
        .filter(command => (command.type === 'textRun' || command.type === 'field' || command.type === 'formControl')
            && String(command.blockId || '')
            && (command.headerFooterId || command.noteId)
            && !existingBlockIds.has(String(command.blockId || '')))
        .sort((left, right) =>
            (Number(left.pageIndex || 0) - Number(right.pageIndex || 0))
            || (Number(left.y || 0) - Number(right.y || 0))
            || (Number(left.x || 0) - Number(right.x || 0))
            || String(left.id || '').localeCompare(String(right.id || '')));

    for (const command of editableCommands) {
        const blockId = String(command.blockId || '');
        if (!groups.has(blockId)) {
            groups.set(blockId, {
                blockId,
                pageIndex: Number(command.pageIndex || 0) || 0,
                caretStops: [],
                _offset: 0,
                _orderY: Number(command.y || 0) || 0,
            });
        }

        appendCommandCaretStops(groups.get(blockId), command);
    }

    return Array.from(groups.values())
        .sort((left, right) =>
            (Number(left.pageIndex || 0) - Number(right.pageIndex || 0))
            || (Number(left._orderY || 0) - Number(right._orderY || 0))
            || String(left.blockId || '').localeCompare(String(right.blockId || '')))
        .map(({ _offset, _orderY, ...block }) => block);
}

function mathEquationSelectionBlocks(commands) {
    return commands
        .filter(command => command.type === 'mathEquation' && command.mathLayout)
        .map(command => ({
            pageIndex: Number(command.pageIndex || 0) || 0,
            blockId: String(command.blockId || ''),
            runId: String(command.runId || ''),
            mathId: String(command.mathId || command.mathLayout?.mathId || ''),
            displayMode: String(command.displayMode || 'inline'),
            start: Number(command.start || 0) || 0,
            end: Number(command.end || command.start || 0) || 0,
            x: Number(command.x || 0) || 0,
            y: Number(command.y || 0) || 0,
            width: Math.max(1, Number(command.width || command.mathLayout?.width || 0) || 1),
            height: Math.max(1, Number(command.height || command.mathLayout?.height || 0) || 1),
            baseline: Number(command.baseline || 0) || 0,
            rect: {
                x: Number(command.x || 0) || 0,
                y: Number(command.y || 0) || 0,
                width: Math.max(1, Number(command.width || command.mathLayout?.width || 0) || 1),
                height: Math.max(1, Number(command.height || command.mathLayout?.height || 0) || 1),
            },
            text: String(command.text || ''),
            mathLayout: command.mathLayout,
        }));
}

function appendCommandCaretStops(block, command) {
    const text = String(command.text || '');
    if (!text) {
        return;
    }

    const length = text.length;
    const width = Math.max(1, Number(command.width || 0) || 1);
    const charWidth = width / Math.max(1, length);
    const y = Number(command.y || 0) || 0;
    const height = Math.max(1, Number(command.height || 0) || Number(command.style?.fontSize || 14) * 1.25);
    const pageIndex = Number(command.pageIndex || 0) || 0;
    const lineId = `${block.blockId}-line-${pageIndex}-${Math.round(y * 10)}`;
    for (let index = 0; index <= length; index += 1) {
        block.caretStops.push({
            blockId: block.blockId,
            offset: block._offset + index,
            lineId,
            pageIndex,
            affinity: index === 0 ? 'before' : 'after',
            rect: {
                x: (Number(command.x || 0) || 0) + charWidth * index,
                y,
                width: 1,
                height,
            },
        });
    }

    block._offset += length;
}

export function configureCanvas(canvas, width, height, pixelRatio, scale = 1) {
    const ratio = Math.max(1, Number(pixelRatio) || 1);
    const zoomScale = Math.max(0.01, Number(scale) || 1);
    const logicalWidth = Math.max(1, Number(width) || 1);
    const logicalHeight = Math.max(1, Number(height) || 1);
    const cssWidth = logicalWidth * zoomScale;
    const cssHeight = logicalHeight * zoomScale;
    canvas.width = Math.round(cssWidth * ratio);
    canvas.height = Math.round(cssHeight * ratio);
    canvas.style.width = `${cssWidth}px`;
    canvas.style.height = `${cssHeight}px`;
    const context = canvas.getContext('2d');
    if (context && typeof context.setTransform === 'function') {
        context.setTransform(ratio * zoomScale, 0, 0, ratio * zoomScale, 0, 0);
    }

    return { context, pixelRatio: ratio, scale: zoomScale, cssWidth, cssHeight, logicalWidth, logicalHeight };
}

function configureCanvasWithoutClearing(canvas, width, height, pixelRatio, scale = 1) {
    const ratio = Math.max(1, Number(pixelRatio) || 1);
    const zoomScale = Math.max(0.01, Number(scale) || 1);
    const cssWidth = Math.max(1, Number(width) || 1) * zoomScale;
    const cssHeight = Math.max(1, Number(height) || 1) * zoomScale;
    canvas.style.width = `${cssWidth}px`;
    canvas.style.height = `${cssHeight}px`;
    if (canvas.width !== Math.round(cssWidth * ratio) || canvas.height !== Math.round(cssHeight * ratio)) {
        configureCanvas(canvas, width, height, pixelRatio, zoomScale);
    }
}

function hasPageBackingStoreChanged(page, cssWidth, cssHeight, pixelRatio) {
    const ratio = Math.max(1, Number(pixelRatio) || 1);
    const targetWidth = Math.round(Math.max(1, Number(cssWidth) || 1) * ratio);
    const targetHeight = Math.round(Math.max(1, Number(cssHeight) || 1) * ratio);
    for (const canvas of page.layers?.values?.() || []) {
        if (!canvas || canvas.width !== targetWidth || canvas.height !== targetHeight) {
            return true;
        }
    }

    return false;
}

function clearLayer(canvas, pageLayout) {
    const context = canvas.getContext('2d');
    if (context) {
        context.clearRect(0, 0, pageLayout.width, pageLayout.height);
    }
}

// Computes the model-wide diagnostic counters once per paint. Each value scans the whole model, so
// hoisting this out of the per-page loop turns O(visible pages x model) into O(model) per paint.
function computeModelDiagnostics(model) {
    const sections = Array.isArray(model?.sections) ? model.sections : [];
    const hyphenation = model?.hyphenation || model?.Hyphenation || {};
    const background = model?.pageBackground || model?.PageBackground || {};
    return {
        documentId: String(model?.documentId || ''),
        blockCount: String(Array.isArray(model?.body?.blocks) ? model.body.blocks.length : 0),
        sectionCount: String(sections.length),
        sectionIds: sections.map(section => String(section?.id || '')).filter(Boolean).join(','),
        sectionBlockCounts: sections.map(section => String((section?.blocks || []).length)).join(','),
        hyphenationEnabled: String(hyphenation.enabled === true || hyphenation.Enabled === true),
        pageBackgroundColor: String(background.color || background.Color || ''),
        tableBlockCount: String(countModelBlocks(model, 'table')),
        imageBlockCount: String(countModelBlocks(model, 'image')),
        fieldCount: String(countModelFields(model).length),
        mathCount: String(countModelMathRuns(model).length),
        contentControlCount: String(countModelContentControls(model).length),
        advancedCharMarkCount: String(countModelMarks(model, [
            'superscript',
            'subscript',
            'smallcaps',
            'allcaps',
            'doublestrikethrough',
            'characterspacing',
            'characterscale',
            'kerning',
        ])),
        captionCount: String(countModelCaptions(model)),
        tocEntryCount: String(countModelTocEntries(model)),
        crossReferenceCount: String(countModelFields(model, 13).length),
        tableOfFiguresText: truncateAttribute(firstFieldText(model, 15)),
        bibliographyText: truncateAttribute(firstFieldText(model, 16)),
        crossReferenceText: truncateAttribute(firstFieldText(model, 13)),
        styleCount: String(ensureStyleStore(model).length),
        heading1FontSize: String(readStyleFontSize(model, 'heading-1')),
    };
}

function countModelBlocks(model, type) {
    return topLevelModelBlocks(model)
        .filter(block => String(block?.type || '').toLowerCase() === type)
        .length;
}

function countModelCaptions(model) {
    return allModelBlocks(model).filter(block => block?.content?.caption?.id).length;
}

function countModelFields(model, fieldType = null) {
    return allModelBlocks(model)
        .flatMap(block => Array.isArray(block?.content?.runs) ? block.content.runs : [])
        .filter(run => String(run?.type || '') === 'field'
            && (fieldType == null || normalizeFieldType(run?.field?.fieldType ?? run?.field?.FieldType) === fieldType));
}

function countModelMathRuns(model) {
    return allModelBlocks(model)
        .flatMap(block => Array.isArray(block?.content?.runs) ? block.content.runs : [])
        .filter(run => String(run?.type || '').toLowerCase() === 'math' || !!run?.math || !!run?.Math);
}

function countModelContentControls(model) {
    const controls = [];
    for (const block of allModelBlocks(model)) {
        if (block?.content?.contentControl?.control) {
            controls.push(block.content.contentControl.control);
        }

        for (const run of Array.isArray(block?.content?.runs) ? block.content.runs : []) {
            if (run?.contentControl?.control || run?.contentControl) {
                controls.push(run.contentControl?.control || run.contentControl);
            }
        }
    }

    return controls;
}

function countModelMarks(model, markTypes) {
    const targets = new Set((markTypes || []).map(normalizeMarkType));
    return allModelBlocks(model)
        .flatMap(block => Array.isArray(block?.content?.runs) ? block.content.runs : [])
        .flatMap(run => Array.isArray(run?.marks) ? run.marks : [])
        .filter(mark => targets.has(normalizeMarkType(mark?.type))).length;
}

function countModelTocEntries(model) {
    return allModelBlocks(model).filter(block => block?.content?.tableOfContents?.isEntry === true).length;
}

function firstFieldText(model, fieldType) {
    const run = countModelFields(model, fieldType)[0];
    return String(run?.field?.displayText ?? run?.field?.cachedResult ?? run?.text ?? '');
}

function allModelBlocks(model) {
    const stack = topLevelModelBlocks(model).slice().reverse();
    const result = [];
    while (stack.length > 0) {
        const block = stack.pop();
        if (!block) {
            continue;
        }

        result.push(block);
        const rows = block?.content?.table?.rows;
        if (Array.isArray(rows)) {
            for (let rowIndex = rows.length - 1; rowIndex >= 0; rowIndex -= 1) {
                for (const cell of [...(rows[rowIndex]?.cells || [])].reverse()) {
                    for (const nested of [...(cell?.blocks || [])].reverse()) {
                        stack.push(nested);
                    }
                }
            }
        }

        const nestedControls = block?.content?.contentControl?.blocks;
        if (Array.isArray(nestedControls)) {
            for (const nested of [...nestedControls].reverse()) {
                stack.push(nested);
            }
        }
    }

    return result;
}

function topLevelModelBlocks(model) {
    if (Array.isArray(model?.body?.blocks) && model.body.blocks.length > 0) {
        return model.body.blocks;
    }

    if (Array.isArray(model?.sections)) {
        return model.sections.flatMap(section => Array.isArray(section?.blocks) ? section.blocks : []);
    }

    return [];
}

function truncateAttribute(value) {
    return String(value || '').replace(/\s+/g, ' ').trim().slice(0, 240);
}

function readStyleFontSize(model, idOrName) {
    const style = resolveStyle(model, idOrName, 'paragraph') || findStyle(model, idOrName);
    const value = style?.characterFormat?.fontSize ?? style?.characterFormat?.FontSize ?? '';
    return value == null ? '' : String(value);
}

function syncTextRectMetadata(page, pageDisplayList, scale = 1) {
    const doc = page.pageElement.ownerDocument;
    let layer = typeof page.pageElement.querySelector === 'function'
        ? page.pageElement.querySelector('[data-testid="document-canvas-text-rect-layer"]')
        : findDescendant(page.pageElement, node => node.getAttribute?.('data-testid') === 'document-canvas-text-rect-layer');
    if (!layer) {
        layer = doc.createElement('div');
        layer.setAttribute('data-testid', 'document-canvas-text-rect-layer');
        layer.setAttribute('aria-hidden', 'true');
        layer.style.position = 'absolute';
        layer.style.inset = '0';
        layer.style.pointerEvents = 'none';
        layer.style.opacity = '0';
        page.pageElement.appendChild(layer);
    }

    if (typeof layer.replaceChildren === 'function') {
        layer.replaceChildren();
    } else {
        while (layer.children?.length) {
            removeElement(layer.children[0]);
        }
    }
    const textCommands = pageDisplayList.commands.filter(command =>
        (command.type === 'textRun' || command.type === 'field' || command.type === 'formControl' || command.type === 'listLabel' || command.type === 'lineNumber' || command.type === 'tableCell')
        && String(command.text || '').length > 0);
    for (const command of textCommands) {
        const rect = doc.createElement('div');
        rect.setAttribute('data-canvas-text-rect', '');
        rect.setAttribute('data-block-id', command.blockId || '');
        rect.setAttribute('data-run-id', command.runId || '');
        rect.setAttribute('data-command-id', command.id || '');
        rect.setAttribute('data-canvas-text', command.text || '');
        rect.setAttribute('data-canvas-start-offset', String(Number(command.start || 0) || 0));
        rect.setAttribute('data-canvas-end-offset', String(Number(command.end || 0) || 0));
        rect.style.position = 'absolute';
        assignScaledRectStyle(rect, command, scale);
        layer.appendChild(rect);
    }
}

function syncContentControlMetadata(page, pageDisplayList, scale = 1) {
    const doc = page.pageElement.ownerDocument;
    let layer = typeof page.pageElement.querySelector === 'function'
        ? page.pageElement.querySelector('[data-testid="document-canvas-content-control-layer"]')
        : findDescendant(page.pageElement, node => node.getAttribute?.('data-testid') === 'document-canvas-content-control-layer');
    if (!layer) {
        layer = doc.createElement('div');
        layer.setAttribute('data-testid', 'document-canvas-content-control-layer');
        layer.setAttribute('aria-hidden', 'true');
        layer.style.position = 'absolute';
        layer.style.inset = '0';
        layer.style.pointerEvents = 'none';
        layer.style.opacity = '0';
        page.pageElement.appendChild(layer);
    }

    if (typeof layer.replaceChildren === 'function') {
        layer.replaceChildren();
    } else {
        while (layer.children?.length) {
            removeElement(layer.children[0]);
        }
    }

    for (const command of pageDisplayList.commands.filter(item => item.type === 'formControl')) {
        const rect = doc.createElement('div');
        rect.setAttribute('data-canvas-content-control', '');
        rect.setAttribute('data-control-id', command.controlId || '');
        rect.setAttribute('data-control-kind', command.controlKind || '');
        rect.setAttribute('data-control-text', command.text || '');
        rect.setAttribute('data-control-required', String(command.isRequired === true));
        rect.setAttribute('data-control-locked', String(command.isLocked === true));
        rect.setAttribute('data-control-valid', String(!command.validation || command.validation.valid !== false));
        rect.setAttribute('data-control-render-mode', command.renderMode || command.renderState?.mode || 'form');
        rect.setAttribute('data-control-design-tag', command.designTag || command.renderState?.tagLabel || '');
        rect.style.position = 'absolute';
        assignScaledRectStyle(rect, command, scale);
        layer.appendChild(rect);
    }
}

function syncTableOfContentsHitMetadata(page, pageDisplayList, model, scale = 1) {
    const doc = page.pageElement.ownerDocument;
    let layer = typeof page.pageElement.querySelector === 'function'
        ? page.pageElement.querySelector('[data-testid="document-canvas-toc-hit-layer"]')
        : findDescendant(page.pageElement, node => node.getAttribute?.('data-testid') === 'document-canvas-toc-hit-layer');
    if (!layer) {
        layer = doc.createElement('div');
        layer.setAttribute('data-testid', 'document-canvas-toc-hit-layer');
        layer.setAttribute('aria-hidden', 'true');
        layer.style.position = 'absolute';
        layer.style.inset = '0';
        layer.style.pointerEvents = 'none';
        layer.style.opacity = '0';
        page.pageElement.appendChild(layer);
    }

    if (typeof layer.replaceChildren === 'function') {
        layer.replaceChildren();
    } else {
        while (layer.children?.length) {
            removeElement(layer.children[0]);
        }
    }

    const tocByBlockId = new Map(allModelBlocks(model)
        .filter(block => block?.content?.tableOfContents?.isEntry === true)
        .map(block => [String(block.id || ''), block.content.tableOfContents]));
    if (tocByBlockId.size === 0) {
        page.pageElement.setAttribute('data-canvas-toc-hit-count', '0');
        return;
    }

    let count = 0;
    const textCommands = pageDisplayList.commands.filter(command =>
        (command.type === 'textRun' || command.type === 'field')
        && tocByBlockId.has(String(command.blockId || ''))
        && String(command.text || '').trim().length > 0);
    for (const command of textCommands) {
        const toc = tocByBlockId.get(String(command.blockId || ''));
        const targetBlockId = String(toc?.targetBlockId || '');
        if (!targetBlockId) {
            continue;
        }

        const rect = doc.createElement('button');
        rect.type = 'button';
        rect.setAttribute('data-testid', 'document-canvas-toc-entry');
        rect.setAttribute('data-canvas-toc-entry', '');
        rect.setAttribute('data-canvas-toc-block-id', command.blockId || '');
        rect.setAttribute('data-canvas-toc-target-block-id', targetBlockId);
        rect.setAttribute('data-canvas-toc-level', String(Number(toc?.level || 1) || 1));
        rect.setAttribute('data-canvas-toc-page-number', String(Number(toc?.pageNumber || 1) || 1));
        rect.setAttribute('tabindex', '-1');
        rect.style.position = 'absolute';
        assignScaledRectStyle(rect, command, scale, 1);
        rect.style.border = '0';
        rect.style.padding = '0';
        rect.style.background = 'transparent';
        rect.style.pointerEvents = 'auto';
        rect.style.cursor = 'pointer';
        layer.appendChild(rect);
        count++;
    }

    page.pageElement.setAttribute('data-canvas-toc-hit-count', String(count));
}

function syncTableCellMetadata(page, pageDisplayList, scale = 1) {
    const doc = page.pageElement.ownerDocument;
    let layer = typeof page.pageElement.querySelector === 'function'
        ? page.pageElement.querySelector('[data-testid="document-canvas-table-cell-layer"]')
        : findDescendant(page.pageElement, node => node.getAttribute?.('data-testid') === 'document-canvas-table-cell-layer');
    if (!layer) {
        layer = doc.createElement('div');
        layer.setAttribute('data-testid', 'document-canvas-table-cell-layer');
        layer.setAttribute('aria-hidden', 'true');
        layer.style.position = 'absolute';
        layer.style.inset = '0';
        layer.style.pointerEvents = 'none';
        layer.style.opacity = '0';
        page.pageElement.appendChild(layer);
    }

    if (typeof layer.replaceChildren === 'function') {
        layer.replaceChildren();
    } else {
        while (layer.children?.length) {
            removeElement(layer.children[0]);
        }
    }

    for (const command of pageDisplayList.commands.filter(item => item.type === 'tableCell')) {
        const rect = doc.createElement('div');
        rect.setAttribute('data-canvas-table-cell', '');
        rect.setAttribute('data-table-id', command.tableId || '');
        rect.setAttribute('data-cell-id', command.cellId || '');
        rect.setAttribute('data-row-index', String(Number(command.rowIndex || 0) || 0));
        rect.setAttribute('data-column-index', String(Number(command.columnIndex || 0) || 0));
        rect.setAttribute('data-repeated-header', command.isRepeatedHeader === true ? 'true' : 'false');
        rect.setAttribute('data-banded-row', command.bandedRow === true ? 'true' : 'false');
        rect.setAttribute('data-total-row', command.isTotal === true ? 'true' : 'false');
        rect.style.position = 'absolute';
        assignScaledRectStyle(rect, command, scale);
        layer.appendChild(rect);
    }
}

function syncObjectMetadata(page, pageDisplayList, scale = 1) {
    const doc = page.pageElement.ownerDocument;
    let layer = typeof page.pageElement.querySelector === 'function'
        ? page.pageElement.querySelector('[data-testid="document-canvas-object-layer"]')
        : findDescendant(page.pageElement, node => node.getAttribute?.('data-testid') === 'document-canvas-object-layer');
    if (!layer) {
        layer = doc.createElement('div');
        layer.setAttribute('data-testid', 'document-canvas-object-layer');
        layer.setAttribute('aria-hidden', 'true');
        layer.style.position = 'absolute';
        layer.style.inset = '0';
        layer.style.pointerEvents = 'none';
        layer.style.opacity = '0';
        page.pageElement.appendChild(layer);
    }

    if (typeof layer.replaceChildren === 'function') {
        layer.replaceChildren();
    } else {
        while (layer.children?.length) {
            removeElement(layer.children[0]);
        }
    }

    for (const command of pageDisplayList.commands.filter(isCanvasObjectCommand)) {
        const rect = doc.createElement('div');
        rect.setAttribute('data-canvas-object', '');
        rect.setAttribute('data-object-id', command.objectId || '');
        rect.setAttribute('data-block-id', command.blockId || '');
        rect.setAttribute('data-run-id', command.runId || '');
        rect.setAttribute('data-object-role', command.role || '');
        rect.setAttribute('data-object-kind', command.kind || (command.type === 'imageObject' ? 'image' : 'drawing'));
        rect.setAttribute('data-wrap-mode', command.wrapMode || '');
        rect.setAttribute('data-has-alt-warning', String(command.type === 'imageObject' && !command.altText && command.isDecorative !== true));
        rect.style.position = 'absolute';
        assignScaledRectStyle(rect, command, scale);
        layer.appendChild(rect);
    }
}

function isCanvasObjectCommand(command) {
    return command?.type === 'imageObject'
        || command?.type === 'drawingShape'
        || command?.type === 'drawingLine'
        || command?.type === 'drawingChart';
}

function pageBalancedColumnLineCounts(pageLayout, displayList) {
    const columns = Array.isArray(pageLayout?.columns) ? pageLayout.columns : [];
    if (pageLayout?.columnBalanced !== true || columns.length < 2) {
        return [];
    }

    const pageIndex = Number(pageLayout?.index || 0) || 0;
    const blocks = Array.isArray(displayList?.layout?.blocks) ? displayList.layout.blocks : [];
    let bestCounts = [];
    let bestTotal = 0;
    for (const block of blocks) {
        if (Number(block?.pageIndex || 0) !== pageIndex || !Array.isArray(block?.lines)) {
            continue;
        }

        const counts = Array.from({ length: columns.length }, () => 0);
        for (const line of block.lines) {
            const columnIndex = Math.max(0, Math.min(columns.length - 1, Number(line?.columnIndex || 0) || 0));
            counts[columnIndex] += 1;
        }

        const usedColumns = counts.filter(count => count > 0).length;
        const total = counts.reduce((sum, count) => sum + count, 0);
        if (usedColumns > 1 && total > bestTotal) {
            bestCounts = counts;
            bestTotal = total;
        }
    }

    return bestCounts;
}

function columnLineSpread(counts) {
    if (!Array.isArray(counts) || counts.length < 2) {
        return 0;
    }

    const used = counts.filter(count => Number(count || 0) > 0);
    if (used.length < 2) {
        return 0;
    }

    return Math.max(...used) - Math.min(...used);
}

function syncHeaderFooterMetadata(page, pageDisplayList, scale = 1) {
    syncRegionMetadata(page, pageDisplayList, {
        testId: 'document-canvas-header-footer-layer',
        itemAttribute: 'data-canvas-header-footer-region',
        commands: pageDisplayList.commands.filter(item => item.type === 'headerFooterFrame'),
        attributes(rect, command) {
            rect.setAttribute('data-header-footer-id', command.headerFooterId || '');
            rect.setAttribute('data-region', command.region || '');
            rect.setAttribute('data-scope', command.scope || '');
        },
    }, scale);
}

function syncNoteMetadata(page, pageDisplayList, scale = 1) {
    syncRegionMetadata(page, pageDisplayList, {
        testId: 'document-canvas-note-layer',
        itemAttribute: 'data-canvas-note-region',
        commands: pageDisplayList.commands.filter(item => item.type === 'noteSeparator'),
        attributes(rect, command) {
            rect.setAttribute('data-note-type', command.noteType || '');
        },
    }, scale);
}

function syncRegionMetadata(page, pageDisplayList, options, scale = 1) {
    const doc = page.pageElement.ownerDocument;
    let layer = typeof page.pageElement.querySelector === 'function'
        ? page.pageElement.querySelector(`[data-testid="${options.testId}"]`)
        : findDescendant(page.pageElement, node => node.getAttribute?.('data-testid') === options.testId);
    if (!layer) {
        layer = doc.createElement('div');
        layer.setAttribute('data-testid', options.testId);
        layer.setAttribute('aria-hidden', 'true');
        layer.style.position = 'absolute';
        layer.style.inset = '0';
        layer.style.pointerEvents = 'none';
        layer.style.opacity = '0';
        page.pageElement.appendChild(layer);
    }

    if (typeof layer.replaceChildren === 'function') {
        layer.replaceChildren();
    } else {
        while (layer.children?.length) {
            removeElement(layer.children[0]);
        }
    }

    for (const command of options.commands || pageDisplayList.commands || []) {
        const rect = doc.createElement('div');
        rect.setAttribute(options.itemAttribute, '');
        rect.style.position = 'absolute';
        assignScaledRectStyle(rect, command, scale);
        options.attributes?.(rect, command);
        layer.appendChild(rect);
    }
}

function assignScaledRectStyle(element, command, scale = 1, minSize = 0) {
    const zoomScale = Math.max(0.01, Number(scale) || 1);
    element.style.left = `${(Number(command.x) || 0) * zoomScale}px`;
    element.style.top = `${(Number(command.y) || 0) * zoomScale}px`;
    element.style.width = `${Math.max(minSize, Number(command.width) || 0) * zoomScale}px`;
    element.style.height = `${Math.max(minSize, Number(command.height) || 0) * zoomScale}px`;
}

function findDescendant(rootElement, predicate) {
    const children = Array.isArray(rootElement?.children) ? rootElement.children : Array.from(rootElement?.children || []);
    for (const child of children) {
        if (predicate(child)) {
            return child;
        }

        const nested = findDescendant(child, predicate);
        if (nested) {
            return nested;
        }
    }

    return null;
}

function removeElement(element) {
    if (!element) {
        return;
    }

    if (typeof element.remove === 'function') {
        element.remove();
        return;
    }

    element.parentNode?.removeChild?.(element);
}

function readThemeValue(theme, key, fallback) {
    return theme && theme[key] ? theme[key] : fallback;
}

// Command types that must NOT be clipped to the page body: page chrome, watermarks, header/footer
// frames, line numbers (left margin), notes, column separators, and all positioned objects /
// drawings (which may sit in the margin and are already confined to the page canvas).
const UNCLIPPED_COMMAND_TYPES = new Set([
    'pageFill', 'pageBorder', 'marginGuide', 'bodyArea',
    'watermarkText', 'watermarkImage',
    'headerFooterFrame', 'lineNumber', 'noteMarker', 'noteSeparator', 'columnSeparator',
    'imageObject', 'imageCaption', 'imageBox',
    'drawingShape', 'drawingShapeEffect', 'drawingShapeFill', 'drawingShapeStroke',
    'drawingLine', 'drawingChart', 'drawingRun', 'drawingText',
    'diagnosticOverlay', 'debugBounds', 'pageBreak',
]);

// Repaints a single page from an already-computed display list (used by the async image-load hook).
// Clears the cached content layers and re-runs the same two-pass (margin unclipped, body clipped)
// paint as the main render so a late-loading image appears without re-laying-out the document.
function repaintPageFromDisplayList(page, pageLayout, displayList, contentControlRenderMode) {
    if (!page?.layers) {
        return;
    }

    for (const kind of CANVAS_CACHE_LAYER_KINDS) {
        clearLayer(page.layers.get(kind), pageLayout);
    }

    const pageCommands = (displayList.commands || []).filter(command => command.pageIndex === pageLayout.index);
    const { bodyCommands, marginCommands } = partitionPageCommands(pageCommands);
    paintDisplayList(page.layers, { ...displayList, commands: marginCommands }, { contentControlRenderMode });
    paintDisplayList(page.layers, { ...displayList, commands: bodyCommands }, { contentControlRenderMode, clipRect: pageBodyClipRect(pageLayout) });
}

function partitionPageCommands(commands) {
    const bodyCommands = [];
    const marginCommands = [];
    for (const command of commands || []) {
        if (UNCLIPPED_COMMAND_TYPES.has(command.type) || command.headerFooterId || command.noteId || command.region) {
            marginCommands.push(command);
        } else {
            bodyCommands.push(command);
        }
    }

    return { bodyCommands, marginCommands };
}

function pageBodyClipRect(pageLayout) {
    const body = pageLayout?.body;
    if (!body) {
        return null;
    }

    // A small inset of slack keeps legitimate ascenders/descenders at the body edges intact while
    // still catching gross horizontal/vertical bleed into the margins.
    const pad = 8;
    return {
        x: Number(body.x || 0) - pad,
        y: Number(body.y || 0) - pad,
        width: Number(body.width || 0) + pad * 2,
        height: Number(body.height || 0) + pad * 2,
    };
}
