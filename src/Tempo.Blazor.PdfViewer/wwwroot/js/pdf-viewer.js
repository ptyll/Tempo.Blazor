/**
 * Tempo Blazor — PDF Viewer JS Interop
 *
 * Uses PDF.js v5 (ES modules) via dynamic import.
 * Requires pdf.min.mjs + pdf.worker.min.mjs in the same directory as this file.
 *
 * Document state is keyed by the viewer ROOT element (not the single-page
 * canvas), so a viewer that starts directly in continuous mode — where the
 * single-page canvas is never rendered — still loads its document.
 */
window.tmPdfViewer = (function () {
    'use strict';

    // Capture script directory while document.currentScript is still set.
    const _scriptDir = (() => {
        const src = document.currentScript?.src ?? '';
        return src ? src.substring(0, src.lastIndexOf('/') + 1) : '_content/Tempo.Blazor.PdfViewer/js/';
    })();

    // rootEl → per-instance state. Shape:
    //   pending load: { rootEl, token, pending: true, dotNetRef, renderTasks, overlayObservers, abort, buildGen, paintGen, thumbGen }
    //   loaded:       { rootEl, pdfDoc, token, currentPage, scale, rotation, dotNetRef, renderTasks,
    //                   overlayObservers, abort, buildGen, paintGen, thumbGen, _selectionHandler,
    //                   fitMode, fitObserver, fitObserverEl }
    // `token` is a unique marker per init() call: a download that finishes after a
    // newer init()/destroy() detects the stale token and discards itself.
    const _docs = new WeakMap();
    let _lib = null;
    let _sizeModulePromise = null;
    let _renderModulePromise = null;
    let _renderModule = null;

    async function _ensureLib() {
        if (_lib) return _lib;
        const mod = await import(_scriptDir + 'pdf.min.mjs');
        mod.GlobalWorkerOptions.workerSrc = _scriptDir + 'pdf.worker.min.mjs';
        _lib = mod;
        return _lib;
    }

    // Pure DPR-aware canvas sizing lives in pdf-viewer-size.mjs (node-tested
    // via npm run test:pdf-viewer); it ships in the same folder as this script.
    function _ensureSizeModule() {
        if (!_sizeModulePromise) {
            _sizeModulePromise = import(_scriptDir + 'pdf-viewer-size.mjs')
                .catch(e => { _sizeModulePromise = null; throw e; });
        }
        return _sizeModulePromise;
    }

    // Render-task bookkeeping lives in pdf-viewer-render.mjs (node-tested the
    // same way); PDF.js v5 RenderTasks are not thenable and must be cancelled.
    function _ensureRenderModule() {
        if (_renderModule) return Promise.resolve(_renderModule);
        if (!_renderModulePromise) {
            _renderModulePromise = import(_scriptDir + 'pdf-viewer-render.mjs')
                .then(mod => { _renderModule = mod; return mod; })
                .catch(e => { _renderModulePromise = null; throw e; });
        }
        return _renderModulePromise;
    }

    // Pure fit-mode logic lives in pdf-viewer-fit.mjs (node-tested the same way);
    // the DOM reads around it stay in this script.
    let _fitModule = null;
    let _fitModulePromise = null;
    function _ensureFitModule() {
        if (_fitModule) return Promise.resolve(_fitModule);
        if (!_fitModulePromise) {
            _fitModulePromise = import(_scriptDir + 'pdf-viewer-fit.mjs')
                .then(mod => { _fitModule = mod; return mod; })
                .catch(e => { _fitModulePromise = null; throw e; });
        }
        return _fitModulePromise;
    }

    // Pure continuous-mode logic (visible page, lazy queue order, canvas release
    // limit, zoom scroll anchors) lives in pdf-viewer-continuous.mjs, node-tested
    // the same way; pdf-viewer.js only collects the DOM boxes around it.
    let _continuousModule = null;
    let _continuousModulePromise = null;
    function _ensureContinuousModule() {
        if (_continuousModule) return Promise.resolve(_continuousModule);
        if (!_continuousModulePromise) {
            _continuousModulePromise = import(_scriptDir + 'pdf-viewer-continuous.mjs')
                .then(mod => { _continuousModule = mod; return mod; })
                .catch(e => { _continuousModulePromise = null; throw e; });
        }
        return _continuousModulePromise;
    }

    // Pure download-decision logic lives in pdf-viewer-download.mjs, node-tested the
    // same way; pdf-viewer.js only executes the decision it returns.
    let _downloadModule = null;
    let _downloadModulePromise = null;
    function _ensureDownloadModule() {
        if (_downloadModule) return Promise.resolve(_downloadModule);
        if (!_downloadModulePromise) {
            _downloadModulePromise = import(_scriptDir + 'pdf-viewer-download.mjs')
                .then(mod => { _downloadModule = mod; return mod; })
                .catch(e => { _downloadModulePromise = null; throw e; });
        }
        return _downloadModulePromise;
    }

    // The document-length lookup lives in pdf-viewer-filesize.mjs, node-tested the
    // same way; a null answer (unknown length, rejected getDownloadInfo) reports nothing.
    let _filesizeModule = null;
    let _filesizeModulePromise = null;
    function _ensureFilesizeModule() {
        if (_filesizeModule) return Promise.resolve(_filesizeModule);
        if (!_filesizeModulePromise) {
            _filesizeModulePromise = import(_scriptDir + 'pdf-viewer-filesize.mjs')
                .then(mod => { _filesizeModule = mod; return mod; })
                .catch(e => { _filesizeModulePromise = null; throw e; });
        }
        return _filesizeModulePromise;
    }

    // ── FZE-9: the pinch lives in the CORE sibling module ───────────────────
    // Multi-pointer gestures are never copied into a package (docs/touch-interaction.md,
    // "Multi-pointer gestures"). Referenced packages' static web assets are served as
    // siblings, so the import walks up from this script's own directory — a relative
    // URL that keeps <base href> and the MapStaticAssets fingerprint intact.
    const _pinchModuleUrl = new URL('../../Tempo.Blazor/js/pointer-gesture.js', _scriptDir).href;
    let _pinchModulePromise = null;
    function _ensurePinchModule() {
        if (!_pinchModulePromise) {
            _pinchModulePromise = import(_pinchModuleUrl)
                .catch(e => { _pinchModulePromise = null; throw e; });
        }
        return _pinchModulePromise;
    }

    function _isCancelled(err) {
        return _renderModule ? _renderModule.isRenderingCancelled(err) : false;
    }

    function _newBaseState(dotNetRef, token) {
        return {
            token,
            pending: false,
            dotNetRef,
            // canvas → in-flight RenderTask; a new render cancels the previous one
            renderTasks: new Map(),
            // overlayEl → ResizeObserver; disconnected in destroy()
            overlayObservers: new Map(),
            abort: new AbortController(),
            // Generation counters guarding the continuous loops. buildGen invalidates
            // the DOM build (renderAllPages, its IntersectionObserver callback and
            // _resolvePageSizes); paintGen invalidates the paint loop (the pump and
            // _renderContinuousPage). A rebuild bumps both; a scale-only update bumps
            // only paintGen so a pending build still finishes instead of leaving an
            // empty DOM.
            buildGen: 0,
            paintGen: 0,
            thumbGen: 0,
            // Per-page boxes (page → {width, height} at scale 1) resolved lazily so
            // continuous placeholders carry the correct aspect ratio.
            pageSizes: new Map()
        };
    }

    function _fallbackCanvasSize(viewport) {
        // pdf-viewer-size.mjs could not be loaded — fall back to the same DPR
        // cap inline so HiDPI rendering still works.
        const dpr = Math.min(window.devicePixelRatio || 1, 2);
        return {
            width: Math.max(1, Math.round(viewport.width * dpr)),
            height: Math.max(1, Math.round(viewport.height * dpr)),
            cssWidth: viewport.width,
            cssHeight: viewport.height,
            dpr
        };
    }

    function _getCanvas(rootEl) {
        return rootEl?.querySelector?.(':scope > .tm-pdf-viewer__body canvas.tm-pdf-viewer__canvas') ?? null;
    }

    /**
     * Resolves with the instance state once its document is loaded. Callers that
     * can race the initial download (e.g. search typed while the spinner is up)
     * must wait instead of silently dropping the request. Returns null when the
     * root has no load in flight (destroyed or never inited) or on timeout.
     */
    async function _waitForDoc(rootEl, timeoutMs = 30000) {
        const deadline = Date.now() + timeoutMs;
        for (;;) {
            const state = _docs.get(rootEl);
            if (!state) return null;
            if (state.pdfDoc) return state;
            if (!state.pending || Date.now() >= deadline) return null;
            await new Promise(resolve => setTimeout(resolve, 100));
        }
    }

    function isAvailable() {
        return true;
    }

    async function init(rootEl, url, dotNetRef) {
        if (!rootEl || !url) return;
        destroy(rootEl);
        const token = Symbol('tmPdfViewer.init');
        // Register the pending load BEFORE awaiting getDocument: a destroy() or a
        // newer init() during the download flips the token below instead of
        // letting a stale document stomp the newer state.
        _docs.set(rootEl, { ..._newBaseState(dotNetRef, token), pending: true });
        try {
            const pdfjs = await _ensureLib();
            const pdfDoc = await pdfjs.getDocument(url).promise;
            // A newer init (or destroy) superseded this load while it downloaded —
            // discard the freshly opened document instead of adopting it.
            const state = _docs.get(rootEl);
            if (!state || state.token !== token) {
                try { await pdfDoc.destroy(); } catch { /* already closed */ }
                return;
            }
            // Flip the EXISTING pending entry to loaded instead of replacing it: anything
            // that captured the entry object during the download (a waiter in _waitForDoc,
            // a caller that attached observers/handlers before the pending guards existed)
            // keeps working, and the abort signal, observers and render-task bookkeeping
            // registered so far survive into the loaded phase.
            state.pending = false;
            Object.assign(state, {
                pdfDoc,
                currentPage: 1,
                scale: 1.0,
                rotation: 0
            });
            // Render page 1 into the single-page canvas when one is present; in
            // continuous mode the host renders all pages via renderAllPages instead.
            if (_getCanvas(rootEl)) {
                await renderPage(rootEl, 1, 1.0, 0);
            }
            dotNetRef.invokeMethodAsync('OnPdfLoaded', pdfDoc.numPages).catch(console.error);
            // FZE-11: report the byte length for the header's file-size subtitle. The
            // lookup runs in the BACKGROUND — init must resolve as soon as page 1 is
            // painted and never wait on getDownloadInfo (a still-streaming download
            // would hold the viewer hostage). It normalizes a missing/rejected
            // getDownloadInfo to null and reports nothing then — the header keeps its
            // pages-only subtitle. Superseded loads (destroyed or re-initialized root)
            // stay quiet, exactly like OnPdfLoaded.
            _ensureFilesizeModule()
                .then(m => m.documentLength(pdfDoc))
                .then(len => {
                    if (len !== null && _docs.get(rootEl)?.token === token) {
                        dotNetRef.invokeMethodAsync('OnDocumentLengthKnown', len).catch(() => { });
                    }
                })
                .catch(() => { /* unknown length — never report */ });
        } catch (err) {
            // Only the owner of the root reports failures; a superseded load stays quiet.
            if (_docs.get(rootEl)?.token === token) {
                dotNetRef.invokeMethodAsync('OnPdfLoadError', String(err?.message ?? err))
                         .catch(console.error);
            }
        }
    }

    // HiDPI render: the backing store is the viewport size × device pixel ratio
    // (capped at 2 by pdf-viewer-size.mjs) while the CSS size stays the viewport
    // size. Overlay alignment, search highlights and annotations all work with
    // normalized 0..1 coordinates over the CSS box, so they stay aligned.
    async function _renderToCanvas(canvasEl, page, viewport, options) {
        const { renderPageToCanvas } = await _ensureRenderModule();
        let size;
        try {
            const { canvasSize } = await _ensureSizeModule();
            size = canvasSize(viewport.width, viewport.height, window.devicePixelRatio);
        } catch (err) {
            console.warn('tmPdfViewer: pdf-viewer-size.mjs unavailable, using fallback DPR', err);
            size = _fallbackCanvasSize(viewport);
        }
        await renderPageToCanvas({
            canvas: canvasEl,
            page,
            viewport,
            size,
            setCssSize: options?.setCssSize !== false,
            tasks: options?.tasks instanceof Map ? options.tasks : new Map()
        });
    }

    async function renderPage(rootEl, pageNum, scale, rotation) {
        const state = _docs.get(rootEl);
        if (!state?.pdfDoc) return;
        state.scale = scale;
        if (rotation !== undefined) state.rotation = rotation;
        const canvasEl = _getCanvas(rootEl);
        if (!canvasEl) return; // continuous mode renders all pages instead
        // Only the single-page canvas owns state.currentPage: in continuous mode the
        // scroll tracker keeps it in sync and a stale render call must not fight it.
        state.currentPage = pageNum;
        try {
            const page = await state.pdfDoc.getPage(pageNum);
            const viewport = page.getViewport({ scale, rotation: state.rotation });
            await _renderToCanvas(canvasEl, page, viewport, { tasks: state.renderTasks });
        } catch (err) {
            if (_isCancelled(err)) return; // superseded by a newer render for this canvas
            console.error('tmPdfViewer.renderPage', err);
        }
        // M5: a pinch commit re-rendered the page — restore the content point that sat
        // under the fingers and clear the anchor.
        _applyPinchAnchor(state, state._pinchEl);
    }

    function getTotalPages(rootEl) {
        return _docs.get(rootEl)?.pdfDoc?.numPages ?? 0;
    }

    async function setScale(rootEl, scale) {
        const state = _docs.get(rootEl);
        if (!state) return;
        await renderPage(rootEl, state.currentPage, scale, state.rotation);
    }

    async function setRotation(rootEl, rotation) {
        const state = _docs.get(rootEl);
        if (!state) return;
        await renderPage(rootEl, state.currentPage, state.scale, rotation);
    }

    // Shared text-layer builder: fills `el` with the selectable text spans for one
    // page. Used by the single-page layer and by the per-page layers of the
    // continuous renderer.
    //
    // Phase 10 review: the layer is built by PDF.js's own `TextLayer` class (the
    // bundled pdf.min.mjs exports it). The previous hand-rolled divs sat on the text
    // BASELINE (top = tx[5]) and never broke lines, so a multi-line selection came
    // out as one run-together string with misaligned boxes. `TextLayer` inserts
    // <br> for hasEOL items, places each span at top = baseline − fontHeight and
    // scales it against the actually measured glyph width. The spans keep painting
    // nothing (the scoped `color: transparent` rule) while the native ::selection
    // highlight renders at full strength.
    async function _renderTextLayerInto(state, el, page, viewport) {
        if (!state?.pdfDoc || !el) return;
        try {
            const pdfjs = await _ensureLib();

            el.innerHTML = '';
            el.style.width = viewport.width + 'px';
            el.style.height = viewport.height + 'px';
            // PDF.js v5 sizes the layer box and its spans through the
            // --total-scale-factor custom property (raw page units × scale, applied
            // purely in CSS so a zoom re-flow needs no re-render) — the variables
            // must resolve on the container or the layer collapses to zero.
            el.style.setProperty('--scale-factor', String(viewport.scale));
            el.style.setProperty('--user-unit', String(page.userUnit ?? 1));
            el.style.setProperty('--total-scale-factor', 'calc(var(--scale-factor) * var(--user-unit))');
            el.style.setProperty('--scale-round-x', '0.01px');
            el.style.setProperty('--scale-round-y', '0.01px');

            const textContent = await page.getTextContent();
            const layer = new pdfjs.TextLayer({
                textContentSource: textContent,
                container: el,
                viewport,
            });
            await layer.render();
        } catch (err) {
            console.error('tmPdfViewer.renderTextLayer', err);
        }
    }

    async function renderTextLayer(rootEl, textLayerEl, pageNum, scale, rotation) {
        const state = _docs.get(rootEl);
        if (!state?.pdfDoc || !textLayerEl) return;
        const page = await state.pdfDoc.getPage(pageNum);
        const viewport = page.getViewport({ scale, rotation: rotation ?? state.rotation });
        await _renderTextLayerInto(state, textLayerEl, page, viewport);
    }

    async function renderThumbnails(rootEl, containerEl, scale, dotNetRef) {
        const state = _docs.get(rootEl);
        if (!state?.pdfDoc || !containerEl) return;
        // Reuse the already-open document — a second getDocument(url) would
        // download and parse the file again.
        const pdfDoc = state.pdfDoc;
        // Generation guard: a newer renderThumbnails (or destroy) invalidates this loop.
        const gen = ++state.thumbGen;
        containerEl.innerHTML = '';
        for (let i = 1; i <= pdfDoc.numPages; i++) {
            // Paint (colors, shadows, radii, hover/active) lives in TmPdfViewer.razor.css under
            // ::deep — inline styles would beat every stylesheet rule and ignore the themes.
            const thumbWrap = document.createElement('div');
            thumbWrap.className = 'tm-pdf-thumbnail';
            thumbWrap.dataset.page = String(i);

            const thumbCanvas = document.createElement('canvas');

            const pageNumLabel = document.createElement('div');
            pageNumLabel.className = 'tm-pdf-thumbnail__label';
            pageNumLabel.textContent = String(i);

            thumbWrap.appendChild(thumbCanvas);
            thumbWrap.appendChild(pageNumLabel);
            containerEl.appendChild(thumbWrap);

            try {
                const page = await pdfDoc.getPage(i);
                if (state.thumbGen !== gen) return; // superseded meanwhile
                const thumbScale = scale ?? 0.3;
                const viewport = page.getViewport({ scale: thumbScale });
                // Thumbnails keep their fluid CSS width (100% of the sidebar); only the
                // backing store is scaled by the device pixel ratio.
                await _renderToCanvas(thumbCanvas, page, viewport, { setCssSize: false, tasks: state.renderTasks });
                if (state.thumbGen !== gen) return; // superseded meanwhile
            } catch (err) {
                if (_isCancelled(err)) return;
                console.error('tmPdfViewer.renderThumbnails page', i, err);
            }

            thumbWrap.addEventListener('click', () => {
                dotNetRef.invokeMethodAsync('OnThumbnailClicked', i).catch(console.error);
            }, { signal: state.abort?.signal });
        }
    }

    function highlightThumbnail(rootEl, pageNum) {
        if (!rootEl) return;
        rootEl.querySelectorAll('.tm-pdf-thumbnail').forEach(el => {
            const isActive = parseInt(el.dataset.page, 10) === pageNum;
            // The active fill is the .tm-pdf-thumbnail--active class in TmPdfViewer.razor.css —
            // an inline background here would override it and pin the palette.
            el.classList.toggle('tm-pdf-thumbnail--active', isActive);
        });
    }

    // ── Text selection ──────────────────────────────────────────────────────

    // FZE-9 review (M3): `selectionchange` fires for every DOM mutation of the live
    // selection; reporting each would spam the Blazor dispatcher. The report is
    // debounced; `pointerup` always flushes immediately.
    const SELECTION_DEBOUNCE_MS = 120;

    /**
     * The identity of a selection report — text | page | rects. A report whose key equals
     * the previous one is skipped: the handlers fire for many DOM reasons, and only a
     * changed selection is worth a dispatcher round-trip. Pure (node-tested).
     */
    function _selectionKey(text, page, flat) {
        return `${text ?? ''}|${page}|${(flat ?? []).join(',')}`;
    }

    /** Clears the DOM selection and resets the report dedupe key (the CTA dismiss path). */
    function clearSelection(rootEl) {
        const state = _docs.get(rootEl);
        if (state) state._lastSelectionKey = null;
        try {
            window.getSelection()?.removeAllRanges();
        } catch { /* no selection API (test harness) */ }
    }

    function enableSelection(rootEl, textLayerEl, dotNetRef) {
        if (!rootEl || !textLayerEl) return;
        const state = _docs.get(rootEl);
        // While the document is still downloading the text layer is empty — attaching the
        // handler now would race the load. The component re-runs its one-shot setup once
        // OnPdfLoaded fires, so skipping here only defers the work.
        if (!state || state.pending) return;
        state.dotNetRef = dotNetRef;
        state.abort ??= new AbortController();
        state.overlayObservers ??= new Map();

        // Per-instance document listeners: re-bind if called again, and always
        // unregister in destroy() so multiple viewers on one page (Notion
        // blocks) don't leak document-level listeners.
        if (state._selectionHandler) {
            document.removeEventListener('selectionchange', state._selectionHandler);
            document.removeEventListener('pointerup', state._pointerUpHandler ?? state._selectionHandler);
        }
        clearTimeout(state._selectionDebounce);

        const report = () => {
            // Read the live state so a later document (Url) change — which
            // re-inits the root — is honoured.
            const doc = _docs.get(rootEl);
            if (!doc || !doc.dotNetRef) return;

            const sel = window.getSelection();
            if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return;
            const anchor = sel.anchorNode;
            if (!anchor || !textLayerEl.contains(anchor)) return;

            const canvasEl = _getCanvas(rootEl);
            if (!canvasEl) return;
            const canvasRect = canvasEl.getBoundingClientRect();
            if (canvasRect.width < 1 || canvasRect.height < 1) return;

            const flat = [];
            const range = sel.getRangeAt(0);
            for (const r of range.getClientRects()) {
                if (r.width < 1 || r.height < 1) continue;
                const x = _clamp01((r.left - canvasRect.left) / canvasRect.width);
                const y = _clamp01((r.top - canvasRect.top) / canvasRect.height);
                const w = _clamp01(r.width / canvasRect.width);
                const h = _clamp01(r.height / canvasRect.height);
                flat.push(x, y, w, h);
            }
            if (flat.length === 0) return;

            // M3: an unchanged selection (same text, page and rects) reports nothing —
            // the handlers also fire after clearSelection, on unrelated reflows.
            const key = _selectionKey(sel.toString(), doc.currentPage, flat);
            if (key === doc._lastSelectionKey) return;
            doc._lastSelectionKey = key;

            doc.dotNetRef.invokeMethodAsync('OnTextSelectionChanged', sel.toString(), doc.currentPage, flat).catch(() => { });
        };
        state._selectionHandler = () => {
            // M3: the live selection streams — debounce the report; the final state
            // comes through the pointerup flush.
            clearTimeout(state._selectionDebounce);
            state._selectionDebounce = setTimeout(report, SELECTION_DEBOUNCE_MS);
        };
        state._pointerUpHandler = () => {
            // M3: the gesture ended — flush immediately (no debounce on the commit).
            clearTimeout(state._selectionDebounce);
            report();
        };
        // FZE-9 touch selection: `selectionchange` streams the selection while the finger
        // drags or the native handles move it (there is no `mouseup` on touch), and
        // `pointerup` reports the final state once the gesture ends — both pointer types.
        document.addEventListener('selectionchange', state._selectionHandler, { signal: state.abort?.signal });
        document.addEventListener('pointerup', state._pointerUpHandler, { signal: state.abort?.signal });
    }

    // ── Pinch zoom (FZE-9) ───────────────────────────────────────────────────

    // The absolute scale bounds, shared with TmPdfViewer.MinScale/MaxScale: the JS side
    // clamps the committed scale before it reaches .NET, which guards it again.
    const PINCH_MIN_SCALE = 0.1;
    const PINCH_MAX_SCALE = 5.0;

    function _clearPinchTransform(el) {
        el.style.transform = '';
        el.style.transformOrigin = '';
    }

    /**
     * FZE-9 review (M5): the pinch commit re-renders the page(s) at the new scale, which
     * replaces the CSS transform with real layout — the point that sat under the fingers
     * would drift. The commit records a scroll anchor (`state._pinchAnchor`: the transform
     * origin in wrap coordinates + the scale ratio); the next renderPage /
     * updateContinuousScale completion applies it — the content point under the fingers
     * keeps its position (scroll grows/shrinks around the origin) — and clears the anchor.
     */
    function _applyPinchAnchor(state, scrollEl) {
        const anchor = state?._pinchAnchor;
        if (!anchor || !scrollEl || !scrollEl.isConnected) return;
        state._pinchAnchor = null;
        if (!Number.isFinite(anchor.ratio) || anchor.ratio <= 0) return;
        if (typeof scrollEl.scrollLeft === 'number') {
            scrollEl.scrollLeft = (scrollEl.scrollLeft + anchor.originX) * anchor.ratio - anchor.originX;
        }
        if (typeof scrollEl.scrollTop === 'number') {
            scrollEl.scrollTop = (scrollEl.scrollTop + anchor.originY) * anchor.ratio - anchor.originY;
        }
    }

    /**
     * Wires the two-finger pinch/pan zoom onto `gestureEl` — the scroll wrap that owns the
     * page(s) (canvas wrap or continuous wrap). The live gesture is a CSS transform
     * (translate + scale around the fingers' start centre); the commit clears it, turns the
     * two-finger pan into scroll and reports the final ABSOLUTE scale through
     * `OnPinchZoomEnded`, which re-renders at the new scale and flips the zoom mode to
     * Custom. `touchAction` stays null on purpose: CSS owns `touch-action` (pan-x pan-y on
     * the wrap keeps the one-finger scroll and blocks the native page pinch), and the
     * annotator's draw surface claims `touch-action: none` for the ink stroke.
     * A call with a fresh element (a view-mode swap) detaches the previous pinch.
     */
    function enablePinchZoom(rootEl, gestureEl, dotNetRef) {
        const state = _docs.get(rootEl);
        // The wrap exists only after the layout rendered and the document loaded; the
        // component re-runs its one-shot setup once OnPdfLoaded fires.
        if (!state || state.pending || !gestureEl || gestureEl === state._pinchEl) return;
        state.dotNetRef = dotNetRef ?? state.dotNetRef;
        state.abort ??= new AbortController();
        state._pinchEl = gestureEl;
        state._pinch?.detach();

        _ensurePinchModule().then(({ attachPinch }) => {
            // A destroy() or a newer re-bind may have happened while the module loaded.
            if (_docs.get(rootEl) !== state || state._pinchEl !== gestureEl) return;
            // M6: iOS Safari still runs the native two-finger page zoom on the wrap even
            // with `touch-action` set. A non-passive listener bound to the instance abort
            // signal blocks the default while two fingers are down (the pinch owns the
            // gesture) and never interferes with the one-finger scroll.
            gestureEl.addEventListener('touchmove', (e) => {
                if (e.touches.length >= 2) e.preventDefault();
            }, { passive: false, signal: state.abort.signal });
            state._pinch = attachPinch(gestureEl, {
                pointerTypes: ['touch'],   // mouse/pen cannot pinch; ctrl+wheel covers trackpads
                wheel: true,               // ctrl+wheel (trackpad pinch) zooms around the cursor
                touchAction: null,         // CSS owns touch-action; the helper must not touch it
                // FZE-9: the annotator's ink surface is a DESCENDANT listener inside this wrap —
                // capturing a lone finger here would retarget its move/up away from the surface
                // and the stroke would die. The capture happens once the pinch actually starts.
                captureSinglePointer: false,
                minScale: 0,
                maxScale: Infinity,        // the ABSOLUTE clamp below owns the bounds
                onStart({ centerX, centerY }) {
                    const rect = gestureEl.getBoundingClientRect();
                    state._pinchLive = { originX: centerX - rect.left, originY: centerY - rect.top };
                    // M2: the pinch owns the surface from here on — the annotator discards
                    // its live ink stroke (never commits it) through this notification.
                    state.dotNetRef?.invokeMethodAsync('OnPinchStarted').catch(() => { });
                },
                onChange({ scale, dx, dy }) {
                    if (!state._pinchLive) return;
                    gestureEl.style.transformOrigin = `${state._pinchLive.originX}px ${state._pinchLive.originY}px`;
                    gestureEl.style.transform = `translate(${dx}px, ${dy}px) scale(${scale})`;
                },
                onEnd({ scale, dx, dy }) {
                    const origin = state._pinchLive;
                    state._pinchLive = null;
                    _clearPinchTransform(gestureEl);
                    // The two-finger pan survives the transform being replaced by the
                    // re-render at the committed scale: the applied pan becomes scroll.
                    if (dx || dy) {
                        gestureEl.scrollLeft -= dx;
                        gestureEl.scrollTop -= dy;
                    }
                    const doc = _docs.get(rootEl);
                    if (!doc?.dotNetRef) return;
                    const next = Math.min(PINCH_MAX_SCALE, Math.max(PINCH_MIN_SCALE, (doc.scale ?? 1) * scale));
                    // M5: remember where the fingers were; the render completion restores
                    // the point under them and clears the anchor.
                    if (origin) {
                        state._pinchAnchor = {
                            originX: origin.originX,
                            originY: origin.originY,
                            ratio: next / (doc.scale ?? 1)
                        };
                    }
                    doc.dotNetRef.invokeMethodAsync('OnPinchZoomEnded', next).catch(() => { });
                },
                onCancel() {
                    state._pinchLive = null;
                    _clearPinchTransform(gestureEl);
                },
            });
        }).catch(err => {
            // The core gesture module is unavailable (offline bundle, CSP): the zoom
            // dropdown and the ± buttons remain the WCAG 2.5.7 alternative.
            console.warn('tmPdfViewer: pointer-gesture.js unavailable, pinch zoom disabled', err);
        });
    }

    // ── Overlay positioning ──────────────────────────────────────────────────

    // Overlay elements are tracked per instance (state.overlayObservers), so two
    // viewers on one page never share — or leak — ResizeObservers.
    function syncOverlay(rootEl, overlayEl) {
        const state = _docs.get(rootEl);
        // No canvas exists before the document loads, so alignment would only register a
        // ResizeObserver against a stale canvas. The component re-runs its one-shot sync
        // once OnPdfLoaded fires.
        if (!state || state.pending) return;
        state.overlayObservers ??= new Map();
        const canvasEl = _getCanvas(rootEl);
        if (!canvasEl || !overlayEl) return;
        const apply = () => {
            overlayEl.style.left = canvasEl.offsetLeft + 'px';
            overlayEl.style.top = canvasEl.offsetTop + 'px';
            overlayEl.style.width = canvasEl.offsetWidth + 'px';
            overlayEl.style.height = canvasEl.offsetHeight + 'px';
        };
        apply();
        // The canvas is resized asynchronously when a page renders (initial load, zoom,
        // rotation). Track it so overlays never go stale between explicit sync calls.
        if (typeof ResizeObserver !== 'undefined' && !state.overlayObservers.has(overlayEl)) {
            const observer = new ResizeObserver(apply);
            observer.observe(canvasEl);
            state.overlayObservers.set(overlayEl, observer);
        }
    }

    // ── Search ────────────────────────────────────────────────────────────────

    async function search(rootEl, searchLayerEl, query, dotNetRef) {
        // The user can type before the document finishes downloading — wait for it
        // instead of dropping the query (the results would never arrive otherwise).
        const state = await _waitForDoc(rootEl);
        if (!state) return;
        state.abort ??= new AbortController();
        state.searchLayerEl = searchLayerEl;
        state.dotNetRef = dotNetRef ?? state.dotNetRef;

        if (!query) {
            clearSearch(rootEl, searchLayerEl);
            dotNetRef.invokeMethodAsync('OnSearchResults', 0, []).catch(() => { });
            return;
        }

        state.searchQuery = query;
        const q = query.toLowerCase();
        const matches = [];
        const perPage = [];
        try {
            const pdfjs = await _ensureLib();
            for (let p = 1; p <= state.pdfDoc.numPages; p++) {
                const page = await state.pdfDoc.getPage(p);
                const viewport = page.getViewport({ scale: 1 });
                const pw = viewport.width;
                const ph = viewport.height;
                const textContent = await page.getTextContent();
                let count = 0;

                for (const item of textContent.items) {
                    const str = item.str;
                    if (!str) continue;
                    const lower = str.toLowerCase();
                    if (lower.indexOf(q) === -1) continue;

                    const tx = pdfjs.Util.transform(viewport.transform, item.transform);
                    const fontHeight = Math.hypot(tx[2], tx[3]) || item.height || 0;
                    const left = tx[4];
                    const top = tx[5] - fontHeight;
                    const widthPx = item.width || (Math.hypot(tx[0], tx[1]) * str.length);
                    const charWidth = widthPx / Math.max(str.length, 1);

                    let from = 0;
                    let idx;
                    while ((idx = lower.indexOf(q, from)) !== -1) {
                        matches.push({
                            page: p,
                            x: _clamp01((left + idx * charWidth) / pw),
                            y: _clamp01(top / ph),
                            w: _clamp01((q.length * charWidth) / pw),
                            h: _clamp01(fontHeight / ph)
                        });
                        count++;
                        from = idx + q.length;
                    }
                }
                perPage.push(count);
            }
        } catch (err) {
            console.error('tmPdfViewer.search', err);
        }

        state.searchMatches = matches;
        state.searchActive = matches.length ? 0 : -1;
        dotNetRef.invokeMethodAsync('OnSearchResults', matches.length, perPage).catch(() => { });

        // Continuous mode keeps the highlights inside the page wraps — refresh all
        // of them once the matches are known (the active one is drawn right after).
        if (_isContinuous(state)) {
            _drawAllSearchHighlights(state);
        }

        if (matches.length) {
            await _activateMatch(rootEl, searchLayerEl);
        } else {
            _drawSearchHighlights(rootEl, searchLayerEl, state.currentPage);
        }
    }

    // ── Search highlighting per surface ─────────────────────────────────────

    // The single-page viewer draws highlights into one overlay sized to the canvas;
    // the continuous renderer keeps a search layer inside every page wrap instead.
    function _isContinuous(state) {
        return !!state?._continuousEl?.isConnected;
    }

    function _drawSearchHighlightsIntoPage(state, wrap, pageNum) {
        const layer = wrap?.querySelector?.(':scope > .tm-pdf-viewer__page-inner > .tm-pdf-viewer__search-layer');
        if (!layer) return;
        layer.innerHTML = '';
        (state.searchMatches ?? []).forEach((match, i) => {
            if (match.page !== pageNum) return;
            const div = document.createElement('div');
            const isActive = i === state.searchActive;
            div.className = 'tm-pdf-search-highlight' + (isActive ? ' tm-pdf-search-highlight--active' : '');
            div.style.left = (match.x * 100) + '%';
            div.style.top = (match.y * 100) + '%';
            div.style.width = (match.w * 100) + '%';
            div.style.height = (match.h * 100) + '%';
            if (isActive) div.dataset.active = '1';
            layer.appendChild(div);
        });
    }

    function _drawAllSearchHighlights(state) {
        state?._continuousEl?.querySelectorAll(':scope > .tm-pdf-viewer__page').forEach(wrap => {
            _drawSearchHighlightsIntoPage(state, wrap, parseInt(wrap.dataset.page, 10));
        });
    }

    async function searchNext(rootEl, searchLayerEl) {
        const state = _docs.get(rootEl);
        if (!state || !state.searchMatches || !state.searchMatches.length) return;
        state.searchActive = (state.searchActive + 1) % state.searchMatches.length;
        await _activateMatch(rootEl, searchLayerEl);
    }

    async function searchPrev(rootEl, searchLayerEl) {
        const state = _docs.get(rootEl);
        if (!state || !state.searchMatches || !state.searchMatches.length) return;
        const n = state.searchMatches.length;
        state.searchActive = (state.searchActive - 1 + n) % n;
        await _activateMatch(rootEl, searchLayerEl);
    }

    function redrawSearch(rootEl, searchLayerEl, pageNum) {
        _drawSearchHighlights(rootEl, searchLayerEl, pageNum);
    }

    function clearSearch(rootEl, searchLayerEl) {
        const state = _docs.get(rootEl);
        if (state) {
            state.searchMatches = [];
            state.searchActive = -1;
            state.searchQuery = null;
        }
        if (searchLayerEl) searchLayerEl.innerHTML = '';
        // Continuous mode keeps a search layer inside every page wrap.
        if (state?._continuousEl) {
            state._continuousEl.querySelectorAll('.tm-pdf-viewer__search-layer').forEach(layer => {
                layer.innerHTML = '';
            });
        }
    }

    async function _activateMatch(rootEl, searchLayerEl) {
        const state = _docs.get(rootEl);
        if (!state || !state.searchMatches || state.searchActive < 0) return;
        const match = state.searchMatches[state.searchActive];
        if (_isContinuous(state)) {
            // The match's page is scrolled into view; the lazy renderer paints it when
            // it becomes visible. Rendering into the single-page canvas would target a
            // node that does not exist in this mode.
            scrollToPage(rootEl, match.page);
            const wrap = state._continuousEl.querySelector(`:scope > [data-page="${match.page}"]`);
            _drawSearchHighlightsIntoPage(state, wrap, match.page);
            _scrollToActive(wrap?.querySelector('.tm-pdf-viewer__search-layer'));
        } else {
            if (match.page !== state.currentPage) {
                await renderPage(rootEl, match.page, state.scale, state.rotation);
            }
            _drawSearchHighlights(rootEl, searchLayerEl, match.page);
            _scrollToActive(searchLayerEl);
        }
        if (state.dotNetRef) {
            state.dotNetRef.invokeMethodAsync('OnSearchActiveChanged', state.searchActive + 1, match.page).catch(() => { });
        }
    }

    function _drawSearchHighlights(rootEl, searchLayerEl, pageNum) {
        if (!searchLayerEl) return;
        syncOverlay(rootEl, searchLayerEl);
        searchLayerEl.innerHTML = '';
        const state = _docs.get(rootEl);
        if (!state || !state.searchMatches) return;

        state.searchMatches.forEach((match, i) => {
            if (match.page !== pageNum) return;
            const div = document.createElement('div');
            const isActive = i === state.searchActive;
            div.className = 'tm-pdf-search-highlight' + (isActive ? ' tm-pdf-search-highlight--active' : '');
            div.style.left = (match.x * 100) + '%';
            div.style.top = (match.y * 100) + '%';
            div.style.width = (match.w * 100) + '%';
            div.style.height = (match.h * 100) + '%';
            if (isActive) div.dataset.active = '1';
            searchLayerEl.appendChild(div);
        });
    }

    // The floating search card overlays the body's top strip: a highlight that is inside
    // the scroller box may still sit UNDER the card. The gap keeps the parked highlight
    // clear of the card's edge (the card's --tm-space-2 inner rhythm).
    const SEARCH_CARD_GAP = 8;

    // The card of the VIEWER that owns the search layer (the demo page renders several
    // viewers at once — a document-wide lookup could steal another viewer's card box).
    function _searchCardRect(searchLayerEl) {
        const root = searchLayerEl?.closest?.('.tm-pdf-viewer');
        return root?.querySelector?.('.tm-pdf-viewer__search-card')?.getBoundingClientRect?.() ?? null;
    }

    // How far below the container's top edge the card's bottom (+gap) sits — the inset the
    // wraps' scroll-padding reserves. 0 without a card. The visible-page tracker reads the
    // same effective top, so scrollIntoView(block:'start') + scroll-padding and the page
    // indicator always agree on which page the user is actually looking at.
    function _effectiveTopInset(containerEl) {
        if (typeof document === 'undefined') return 0;
        const root = containerEl?.closest?.('.tm-pdf-viewer');
        const card = root?.querySelector?.('.tm-pdf-viewer__search-card');
        if (!card) return 0;
        const cardRect = card.getBoundingClientRect?.();
        const containerRect = containerEl.getBoundingClientRect?.();
        if (!cardRect || !containerRect) return 0;
        const inset = (cardRect.bottom + SEARCH_CARD_GAP) - containerRect.top;
        return inset > 0 ? Math.min(inset, containerRect.height) : 0;
    }

    // Scrolls the active highlight into view — but only when it is not already
    // fully visible inside its scroll container. `block: 'center'` here used to
    // drag the wrap so far that the viewport's top edge landed on page N−1 and
    // the page indicator regressed; 'nearest' scrolls the minimum distance, and
    // an already-visible highlight scrolls nothing at all. When the floating
    // search card is open, its bottom edge (+gap) counts as the scroller's top:
    // a highlight hidden behind the card counts as NOT visible and is pushed
    // below it with an explicit scrollBy (scrollIntoView cannot see the card —
    // the card is not the scroller and not in the scroll path).
    function _scrollToActive(searchLayerEl) {
        if (!searchLayerEl) return;
        const active = searchLayerEl.querySelector('[data-active="1"]');
        if (!active || typeof active.scrollIntoView !== 'function') return;
        const scroller = _closestScrollContainer(active);
        const cardRect = _searchCardRect(searchLayerEl);
        if (scroller) {
            const activeRect = active.getBoundingClientRect();
            const scrollerRect = scroller.getBoundingClientRect();
            const topLimit = cardRect
                ? Math.max(scrollerRect.top, cardRect.bottom + SEARCH_CARD_GAP)
                : scrollerRect.top;
            if (activeRect.top >= topLimit && activeRect.bottom <= scrollerRect.bottom) {
                return; // already visible (and clear of the card) — keep scroll stable
            }
        }
        active.scrollIntoView({ block: 'nearest', inline: 'nearest' });
        if (cardRect && scroller && typeof scroller.scrollBy === 'function') {
            const nowRect = active.getBoundingClientRect();
            const hiddenByCard = nowRect.top < cardRect.bottom + SEARCH_CARD_GAP
                && nowRect.bottom > cardRect.top;
            if (hiddenByCard) {
                scroller.scrollBy({ top: (cardRect.bottom + SEARCH_CARD_GAP) - nowRect.top });
            }
        }
    }

    // Nearest ancestor with a scrollable overflow-y (the continuous wrap or the
    // single-page canvas wrap). Degrades to null when getComputedStyle is not
    // available (node tests).
    function _closestScrollContainer(el) {
        if (typeof document === 'undefined' || typeof window?.getComputedStyle !== 'function') return null;
        let node = el?.parentElement;
        while (node && node !== document.body) {
            const style = window.getComputedStyle(node);
            if (style && /(auto|scroll)/.test(style.overflowY || '') && node.clientHeight > 0) return node;
            node = node.parentElement;
        }
        return null;
    }

    function _clamp01(value) {
        if (!Number.isFinite(value)) return 0;
        return Math.min(Math.max(value, 0), 1);
    }

    // ── Continuous mode (lazy render) ─────────────────────────────────────────

    // Painted-page limit (pdf-viewer-continuous.mjs DEFAULT_MAX_RENDERED_PAGES): a
    // painted A4 page at DPR 2 costs tens of MB of backing store, so canvases far
    // outside the viewport are released and re-rendered on demand.
    const MAX_RENDERED_PAGES = 12;

    // Page metas for the pure continuous math: offset of each wrap inside the
    // scrolled content. getBoundingClientRect is used because the wrap's
    // offsetParent is not necessarily the scroll container.
    function _collectPageMetas(containerEl) {
        if (!containerEl?.querySelectorAll) return [];
        const containerTop = containerEl.getBoundingClientRect().top;
        const scrollTop = containerEl.scrollTop;
        return Array.from(containerEl.querySelectorAll(':scope > .tm-pdf-viewer__page'), wrap => ({
            page: parseInt(wrap.dataset.page, 10),
            top: wrap.getBoundingClientRect().top - containerTop + scrollTop,
            height: wrap.getBoundingClientRect().height
        }));
    }

    // An odd quarter turn swaps a page box's axes.
    function _swapsAxes(rotation) {
        const r = ((rotation % 360) + 360) % 360;
        return r === 90 || r === 270;
    }

    // Best-known page box at scale 1: the page's own resolved size, else page 1's
    // from the current rotation (most documents are uniform), else page 1 from any
    // cached rotation — re-oriented for a 90/270 turn — else the PDF default Letter
    // box, also re-oriented. Used until _resolvePageSizes corrects the placeholder.
    function _pageSizeOrFallback(state, pageNum) {
        const own = state.pageSizes.get(pageNum);
        if (own) return own;
        const first = state.pageSizes.get(1);
        if (first) return first;
        for (const [cachedRotation, map] of state.pageSizesByRotation ?? []) {
            const base = map?.get?.(1);
            if (!base) continue;
            const delta = (((state.rotation - cachedRotation) % 360) + 360) % 360;
            return _swapsAxes(delta) ? { width: base.height, height: base.width } : base;
        }
        return _swapsAxes(state.rotation) ? { width: 792, height: 612 } : { width: 612, height: 792 };
    }

    // Applies the placeholder box to a not-yet-painted canvas. The painted render
    // produces exactly the same CSS size, so there is no layout shift.
    function _applyPlaceholderSize(state, wrap, scale) {
        const canvas = wrap.querySelector(':scope > .tm-pdf-viewer__page-inner > canvas');
        if (!canvas || canvas.dataset.rendered) return;
        const pageNum = parseInt(wrap.dataset.page, 10);
        const size = _pageSizeOrFallback(state, pageNum);
        const box = _continuousModule
            ? _continuousModule.placeholderSize(size.width, size.height, scale)
            : { width: Math.max(1, Math.round(size.width * scale)), height: Math.max(1, Math.round(size.height * scale)) };
        canvas.style.width = box.width + 'px';
        canvas.style.height = box.height + 'px';
    }

    // Applies a scroll anchor recorded before a rebuild against the current boxes.
    // Shared by the build-time restore and the re-applies after _resolvePageSizes
    // corrects the placeholders (the corrections shift the layout).
    function _applyScrollAnchor(state, containerEl, anchor) {
        if (!anchor || !containerEl?.isConnected || !_continuousModule) return;
        const restored = _continuousModule.applyScrollAnchor(
            _collectPageMetas(containerEl), anchor, containerEl.clientHeight);
        if (restored !== null) containerEl.scrollTop = restored;
    }

    // Resolves the real per-page boxes (scale 1 viewport) chunk-wise and corrects
    // the placeholders — a wrong guessed ratio would shift the layout on paint.
    // The resolved map is cached per rotation (renderAllPages); the __resolved flag
    // marks a complete pass so a superseded (partial) resolution re-runs later.
    // `gen` is the build generation and `anchor` the scroll anchor recorded before
    // the rebuild: each correction shifts the layout, so the anchor is re-applied
    // after the first chunk and again when the pass completes.
    async function _resolvePageSizes(state, containerEl, gen, anchor) {
        const numPages = state.pdfDoc.numPages;
        const chunk = 8;
        let firstChunk = true;
        for (let start = 1; start <= numPages; start += chunk) {
            const end = Math.min(start + chunk - 1, numPages);
            const resolved = await Promise.all(Array.from({ length: end - start + 1 }, async (_, i) => {
                const pageNum = start + i;
                const page = await state.pdfDoc.getPage(pageNum);
                const viewport = page.getViewport({ scale: 1, rotation: state.rotation });
                return [pageNum, { width: viewport.width, height: viewport.height }];
            }));
            if (state.buildGen !== gen || !containerEl.isConnected) return;
            for (const [pageNum, size] of resolved) {
                state.pageSizes.set(pageNum, size);
                const wrap = containerEl.querySelector(`:scope > [data-page="${pageNum}"]`);
                if (wrap) _applyPlaceholderSize(state, wrap, state.scale);
            }
            if (firstChunk || start + chunk > numPages) {
                _applyScrollAnchor(state, containerEl, anchor);
            }
            firstChunk = false;
        }
        state.pageSizes.__resolved = true;
    }

    function _cancelRenderTasksInside(state, containerEl) {
        if (!(state.renderTasks instanceof Map)) return;
        for (const [canvas, task] of state.renderTasks) {
            if (containerEl.contains(canvas)) {
                try { task?.cancel?.(); } catch { /* task already settled */ }
                state.renderTasks.delete(canvas);
            }
        }
    }

    // Clears the per-page layers inside a wrap: released pages hold stale-scale
    // text/search geometry that would float over the placeholder box.
    function _clearPageLayers(wrap) {
        const inner = wrap?.querySelector?.(':scope > .tm-pdf-viewer__page-inner');
        const textLayer = inner?.querySelector?.(':scope > .tm-pdf-viewer__text-layer');
        if (textLayer) textLayer.innerHTML = '';
        const searchLayer = inner?.querySelector?.(':scope > .tm-pdf-viewer__search-layer');
        if (searchLayer) searchLayer.innerHTML = '';
    }

    function _releasePageCanvas(state, containerEl, pageNum) {
        const wrap = containerEl.querySelector(`:scope > [data-page="${pageNum}"]`);
        const canvas = wrap?.querySelector(':scope > .tm-pdf-viewer__page-inner > canvas');
        if (!canvas || !canvas.dataset.rendered) return;
        // Cancel an in-flight task first (release wins over the queued render).
        const pending = state.renderTasks.get(canvas);
        if (pending) {
            try { pending?.cancel?.(); } catch { /* task already settled */ }
            state.renderTasks.delete(canvas);
        }
        // Zeroing the backing store is the actual memory release; the placeholder
        // box keeps the page geometry stable for the next paint.
        canvas.width = 0;
        canvas.height = 0;
        delete canvas.dataset.rendered;
        _clearPageLayers(wrap);
        _applyPlaceholderSize(state, wrap, state.scale);
    }

    // Release sweep with an inline fallback (mirrors selectCanvasesToRelease) for a
    // failed pdf-viewer-continuous.mjs import: farthest rendered page first, visible
    // pages never released.
    function _enforceRenderedLimit(state) {
        const containerEl = state._continuousEl;
        if (!containerEl?.isConnected) return;
        if (_continuousModule) {
            const metas = _collectPageMetas(containerEl).map(meta => {
                const canvas = containerEl.querySelector(`:scope > [data-page="${meta.page}"] canvas`);
                return { ...meta, rendered: !!canvas?.dataset.rendered };
            });
            for (const pageNum of _continuousModule.selectCanvasesToRelease(
                metas, containerEl.scrollTop, containerEl.clientHeight, MAX_RENDERED_PAGES)) {
                _releasePageCanvas(state, containerEl, pageNum);
            }
            return;
        }
        let rendered = [];
        for (const wrap of containerEl.querySelectorAll(':scope > .tm-pdf-viewer__page')) {
            if (wrap.querySelector('canvas')?.dataset.rendered) {
                const rect = wrap.getBoundingClientRect();
                rendered.push({ page: parseInt(wrap.dataset.page, 10), top: rect.top, height: rect.height });
            }
        }
        const viewBottom = containerEl.getBoundingClientRect().bottom;
        while (rendered.length > MAX_RENDERED_PAGES) {
            let candidate = null;
            let distance = -1;
            for (const meta of rendered) {
                const d = meta.top >= viewBottom ? meta.top - viewBottom : 0; // visible → 0
                if (d > distance) { candidate = meta; distance = d; }
            }
            if (!candidate) return;
            _releasePageCanvas(state, containerEl, candidate.page);
            rendered = rendered.filter(meta => meta !== candidate);
        }
    }

    // Visible-page report with an inline fallback (mirrors computeVisiblePage: the
    // page containing the viewport's top edge) for a failed module import. The top
    // edge sits below the floating search card when it is open (the same effective
    // top the scroll-padding and _scrollToActive use), so page navigation that
    // aligns with the scroll padding does not report the page one above.
    function _reportVisiblePage(state, containerEl) {
        const topInset = _effectiveTopInset(containerEl);
        let visible;
        if (_continuousModule) {
            const metas = _collectPageMetas(containerEl);
            visible = _continuousModule.computeVisiblePage(metas, containerEl.scrollTop + topInset, containerEl.clientHeight);
        } else {
            const containerTop = containerEl.getBoundingClientRect().top;
            const scrollTop = containerEl.scrollTop + topInset;
            visible = 0;
            for (const wrap of containerEl.querySelectorAll(':scope > .tm-pdf-viewer__page')) {
                const top = wrap.getBoundingClientRect().top - containerTop + scrollTop;
                if (top + wrap.getBoundingClientRect().height > scrollTop) {
                    visible = parseInt(wrap.dataset.page, 10);
                    break;
                }
            }
        }
        if (!visible || visible === state.currentPage) return;
        state.currentPage = visible;
        state.dotNetRef?.invokeMethodAsync('OnVisiblePageChanged', visible).catch(() => { });
    }

    // Re-queues unpainted pages that intersect the actual viewport. The
    // IntersectionObserver is transition-driven: a page released by a sweep that
    // raced a scroll (or re-observed mid-transition) may sit inside the viewport
    // with no pending entry and no fresh isIntersecting event — the rAF tick is
    // the safety net that repaints it.
    function _requeueVisibleUnpainted(state, containerEl) {
        if (!state._renderQueue || !_continuousModule) return;
        const metas = _collectPageMetas(containerEl);
        for (const meta of metas) {
            const canvas = containerEl.querySelector(`:scope > [data-page="${meta.page}"] canvas`);
            if (canvas?.dataset.rendered) continue;
            if (state._renderQueue.has(meta.page)) continue;
            if (_continuousModule.pageIntersectsViewport(meta, containerEl.scrollTop, containerEl.clientHeight)) {
                state._renderQueue.add(meta.page);
            }
        }
    }

    // Keeps aria-current="page" on the wrap the user is viewing.
    function _syncAriaCurrent(state, containerEl) {
        if (!containerEl?.querySelectorAll) return;
        for (const wrap of containerEl.querySelectorAll(':scope > .tm-pdf-viewer__page')) {
            if (parseInt(wrap.dataset.page, 10) === state.currentPage) {
                wrap.setAttribute('aria-current', 'page');
            } else if (wrap.getAttribute('aria-current')) {
                wrap.removeAttribute('aria-current');
            }
        }
    }

    // Scroll tracker: rAF-throttled visible-page reporting plus the release sweep.
    // Rebinding per rebuild aborts the previous controller, so repeated view-mode
    // switches never accumulate listeners.
    function _bindContinuousScroll(state, containerEl) {
        state._continuousScrollController?.abort();
        if (typeof containerEl.addEventListener !== 'function') return;
        const controller = new AbortController();
        state._continuousScrollController = controller;
        let frame = 0;
        const onScroll = () => {
            if (frame) return;
            frame = requestAnimationFrame(() => {
                frame = 0;
                if (!containerEl.isConnected) return;
                _reportVisiblePage(state, containerEl);
                _enforceRenderedLimit(state);
                _requeueVisibleUnpainted(state, containerEl);
                _pumpContinuousQueue(state);
                _syncAriaCurrent(state, containerEl);
            });
        };
        containerEl.addEventListener('scroll', onScroll, { signal: controller.signal, passive: true });
    }

    function _nextQueuedPage(state, containerEl) {
        if (!_continuousModule) {
            return state._renderQueue.size ? Math.min(...state._renderQueue) : undefined;
        }
        const metas = _collectPageMetas(containerEl);
        const ordered = _continuousModule.orderPagesForRender(
            [...state._renderQueue], metas, containerEl.scrollTop, containerEl.clientHeight);
        return ordered[0];
    }

    // Serial render pump: one page at a time so a 100+ page document never blocks
    // the UI with a burst of renders. A rebuild or a scale update bumps paintGen;
    // the pump stops when it notices a new generation.
    async function _pumpContinuousQueue(state) {
        if (state._pumping) return;
        state._pumping = true;
        try {
            for (;;) {
                const containerEl = state._continuousEl;
                if (!containerEl?.isConnected || !state._renderQueue?.size) return;
                const gen = state.paintGen;
                const next = _nextQueuedPage(state, containerEl);
                if (next === undefined) return;
                state._renderQueue.delete(next);
                await _renderContinuousPage(state, next, containerEl);
                if (state.paintGen !== gen) return; // superseded by a rebuild/scale update
            }
        } finally {
            state._pumping = false;
            // Pages queued while pumping (scrolled into view mid-render) still get painted.
            if (state._renderQueue?.size && state._continuousEl?.isConnected) {
                _pumpContinuousQueue(state);
            }
        }
    }

    async function _renderContinuousPage(state, pageNum, containerEl) {
        const wrap = containerEl.querySelector(`:scope > [data-page="${pageNum}"]`);
        const canvas = wrap?.querySelector(':scope > .tm-pdf-viewer__page-inner > canvas');
        if (!canvas) return;
        const gen = state.paintGen;
        try {
            const page = await state.pdfDoc.getPage(pageNum);
            if (state.paintGen !== gen || !wrap.isConnected) return;
            const viewport = page.getViewport({ scale: state.scale, rotation: state.rotation });
            await _renderToCanvas(canvas, page, viewport, { tasks: state.renderTasks });
            if (state.paintGen !== gen || !wrap.isConnected) return;
            canvas.dataset.rendered = '1';
            if (state._continuousFlags?.textLayer) {
                const textLayer = wrap.querySelector('.tm-pdf-viewer__text-layer');
                if (textLayer) await _renderTextLayerInto(state, textLayer, page, viewport);
            }
            if (state.paintGen !== gen) return;
            _enforceRenderedLimit(state);
        } catch (err) {
            if (_isCancelled(err)) return; // superseded by a newer render for this canvas
            console.error('tmPdfViewer.renderAllPages page', pageNum, err);
        }
    }

    async function renderAllPages(rootEl, containerEl, scale, rotation, options) {
        const state = _docs.get(rootEl);
        if (!state?.pdfDoc || !containerEl) return;
        // Keep the state in sync with what this loop renders: a fit recompute after a
        // rotate reads state.rotation to size the page viewport.
        state.scale = scale ?? state.scale;
        if (rotation !== undefined) state.rotation = rotation;
        const build = ++state.buildGen;
        const paint = ++state.paintGen;
        // Warm the pure continuous math (node-tested); a failed load keeps the
        // inline fallbacks working.
        await _ensureContinuousModule().catch(() => { });
        if (state.buildGen !== build) return;

        // Preserve the content point across the rebuild (zoom/rotate/switch keep the
        // current page and its offset instead of jumping back to the top).
        const metasBefore = _collectPageMetas(containerEl);
        const anchor = _continuousModule
            ? _continuousModule.computeScrollAnchor(metasBefore, containerEl.scrollTop, containerEl.clientHeight)
            : null;

        // The rebuild replaces every page wrap: the previous observer tracked
        // detached nodes and the queued renders targeted the old canvases.
        state._pageObserver?.disconnect();
        state._pageObserver = null;
        state._renderQueue = new Set();
        state._continuousEl = containerEl;
        state._continuousFlags = {
            textLayer: options?.textLayer === true,
            searchLayer: options?.searchLayer === true,
            pageLabelTemplate: typeof options?.pageLabelTemplate === 'string' ? options.pageLabelTemplate : undefined
        };
        _cancelRenderTasksInside(state, containerEl);

        containerEl.innerHTML = '';

        const total = state.pdfDoc.numPages;
        for (let i = 1; i <= total; i++) {
            // Layout and paint come from .tm-pdf-viewer__page in TmPdfViewer.razor.css (::deep) —
            // the page shadow and radius must follow the theme, not an inline literal.
            const wrap = document.createElement('div');
            wrap.className = 'tm-pdf-viewer__page';
            // Anchor for scrollToPage: the thumbnail sidebar also carries
            // data-page, so continuous-page wraps must be found through their
            // parent container.
            wrap.dataset.page = String(i);
            // A11y: each page is a named group; the localized "Page {0} of {1}"
            // template comes from the host (falls back to English in the pure helper).
            wrap.setAttribute('role', 'group');
            wrap.setAttribute('aria-label',
                _continuousModule ? _continuousModule.formatPageLabel(state._continuousFlags.pageLabelTemplate, i, total)
                    : `Page ${i} of ${total}`);

            const inner = document.createElement('div');
            inner.className = 'tm-pdf-viewer__page-inner';

            const pageCanvas = document.createElement('canvas');
            pageCanvas.dataset.page = String(i);
            // A11y: the canvas is an image of the page — unless the (selectable) text
            // layer carries the page's text, then it is presentational.
            if (state._continuousFlags.textLayer) {
                pageCanvas.setAttribute('aria-hidden', 'true');
            } else {
                pageCanvas.setAttribute('role', 'img');
                pageCanvas.setAttribute('aria-label', wrap.getAttribute('aria-label'));
            }
            inner.appendChild(pageCanvas);

            if (state._continuousFlags.textLayer) {
                const textLayer = document.createElement('div');
                textLayer.className = 'tm-pdf-viewer__text-layer';
                inner.appendChild(textLayer);
            }
            if (state._continuousFlags.searchLayer) {
                const searchLayer = document.createElement('div');
                searchLayer.className = 'tm-pdf-viewer__search-layer';
                searchLayer.setAttribute('aria-hidden', 'true');
                inner.appendChild(searchLayer);
            }

            wrap.appendChild(inner);
            containerEl.appendChild(wrap);
            _applyPlaceholderSize(state, wrap, state.scale);

            // Existing matches are drawn into the fresh layer immediately — the
            // layer overlays the canvas box, so percent coordinates stay aligned.
            if (state._continuousFlags.searchLayer && state.searchMatches?.length) {
                _drawSearchHighlightsIntoPage(state, wrap, i);
            }
        }

        // Lazy render: an IntersectionObserver queues the wraps near the viewport;
        // the serial pump paints them one at a time.
        if (typeof IntersectionObserver !== 'undefined') {
            const observer = new IntersectionObserver(entries => {
                if (state.buildGen !== build) return;
                for (const entry of entries) {
                    const pageNum = parseInt(entry.target.dataset?.page, 10);
                    if (!entry.isIntersecting) {
                        // A page that left the band is dropped from the queue —
                        // otherwise the pump would paint pages the user already
                        // scrolled past.
                        state._renderQueue?.delete(pageNum);
                        continue;
                    }
                    const canvas = entry.target.querySelector?.(':scope > .tm-pdf-viewer__page-inner > canvas');
                    if (canvas?.dataset.rendered) continue;
                    state._renderQueue.add(pageNum);
                }
                _pumpContinuousQueue(state);
                _enforceRenderedLimit(state);
            }, { root: containerEl, rootMargin: '50% 0px' });
            containerEl.querySelectorAll(':scope > .tm-pdf-viewer__page').forEach(wrap => observer.observe(wrap));
            state._pageObserver = observer;
        } else {
            // No observer support: queue everything and let the pump chew through it.
            for (let i = 1; i <= total; i++) state._renderQueue.add(i);
            _pumpContinuousQueue(state);
        }

        _bindContinuousScroll(state, containerEl);

        // Correct the guessed placeholder ratios once the real boxes are known. The
        // resolved sizes are cached per rotation (a page's scale-1 box only depends
        // on the document and the rotation), so a scale-only change skips the work.
        // The cache is only used when the previous pass COMPLETED (__resolved): a
        // resolve interrupted by a newer build left a partial map that must resolve
        // again — its entries stay as hints until the fresh pass overwrites them.
        const cached = state.pageSizesByRotation?.get(state.rotation);
        if (cached?.__resolved) {
            state.pageSizes = cached;
        } else {
            if (!cached) {
                state.pageSizes = new Map();
                (state.pageSizesByRotation ??= new Map()).set(state.rotation, state.pageSizes);
            } else {
                state.pageSizes = cached;
            }
            _resolvePageSizes(state, containerEl, build, anchor).catch(() => { /* doc closed meanwhile */ });
        }

        // Restore the scroll anchor recorded before the rebuild. With no anchor
        // (a first build into an empty container — e.g. a mode switch while page N
        // was open) land on the current page's top instead of jumping to page 1.
        const restorePage = Number.isFinite(options?.currentPage) && options.currentPage >= 1
            ? Math.floor(options.currentPage)
            : state.currentPage;
        if (anchor) {
            _applyScrollAnchor(state, containerEl, anchor);
        } else if (restorePage > 1) {
            state.currentPage = restorePage; // optimistic, like scrollToPage; the tracker confirms
            _applyScrollAnchor(state, containerEl, { page: restorePage, offsetRatio: 0 });
        }

        // M5: a pinch commit may have gone through this full rebuild (updateContinuousScale's
        // fallback path) — restore the content point that sat under the fingers.
        _applyPinchAnchor(state, state._pinchEl?.isConnected ? state._pinchEl : containerEl);

        // Sync the visible page (and aria-current) after the rebuild.
        _syncAriaCurrent(state, containerEl);
    }

    /**
     * Scale-only continuous update: resizes the placeholders, drops the painted
     * canvases and re-queues the visible band WITHOUT rebuilding the DOM (the
     * per-page wraps, their layers and the observer/scroll bindings survive).
     * The content point is restored through a scroll anchor, exactly like a full
     * rebuild. A rotation or document change still goes through renderAllPages.
     */
    async function updateContinuousScale(rootEl, containerEl, scale) {
        const state = _docs.get(rootEl);
        if (!state?.pdfDoc || !containerEl || !containerEl.isConnected) return;
        if (!Number.isFinite(scale) || !(scale > 0)) return;
        state.scale = scale;
        // The in-place resize is only valid when the page wraps are actually there
        // and this container is the one the last build rendered into. Otherwise a
        // build is still pending (or a fresh container arrived) — a full rebuild
        // must run or the viewer would be left with an empty DOM. The rebuild also
        // bumps paintGen, so this older call's work is superseded either way.
        const wraps = containerEl.querySelectorAll?.(':scope > .tm-pdf-viewer__page');
        if (!wraps?.length || state._continuousEl !== containerEl) {
            await renderAllPages(rootEl, containerEl, scale, state.rotation, {
                ...(state._continuousFlags ?? {}),
                currentPage: state.currentPage
            });
            return;
        }
        const paint = ++state.paintGen;
        await _ensureContinuousModule().catch(() => { });
        if (state.paintGen !== paint) return;

        // Anchor the content point against the pre-resize boxes.
        const metasBefore = _collectPageMetas(containerEl);
        const anchor = _continuousModule
            ? _continuousModule.computeScrollAnchor(metasBefore, containerEl.scrollTop, containerEl.clientHeight)
            : null;

        state._renderQueue?.clear();
        for (const wrap of containerEl.querySelectorAll(':scope > .tm-pdf-viewer__page')) {
            const canvas = wrap.querySelector(':scope > .tm-pdf-viewer__page-inner > canvas');
            if (!canvas) continue;
            if (canvas.dataset.rendered) {
                // Cancel an in-flight render and release the stale-scale backing store.
                const pending = state.renderTasks.get(canvas);
                if (pending) {
                    try { pending?.cancel?.(); } catch { /* task already settled */ }
                    state.renderTasks.delete(canvas);
                }
                canvas.width = 0;
                canvas.height = 0;
                delete canvas.dataset.rendered;
                _clearPageLayers(wrap);
            }
            _applyPlaceholderSize(state, wrap, scale);
        }

        // Restore the content point against the resized boxes.
        if (anchor) {
            const restored = _continuousModule.applyScrollAnchor(
                _collectPageMetas(containerEl), anchor, containerEl.clientHeight);
            if (restored !== null) containerEl.scrollTop = restored;
        }

        // M5: a pinch commit re-rendered at the new scale — restore the content point
        // that sat under the fingers and clear the anchor.
        _applyPinchAnchor(state, state._pinchEl?.isConnected ? state._pinchEl : containerEl);

        // Re-queue the pages the resized (and re-anchored) layout puts in the
        // viewport — measured AFTER the resize, the pre-resize boxes are stale —
        // then let the pump paint them visibility-ordered.
        _requeueVisibleUnpainted(state, containerEl);
        _pumpContinuousQueue(state);

        // Search highlights are percent-positioned — redraw them into the cleared layers.
        if (state._continuousFlags?.searchLayer && state.searchMatches?.length) {
            _drawAllSearchHighlights(state);
        }
        _syncAriaCurrent(state, containerEl);
    }

    function scrollToPage(rootEl, page) {
        if (!rootEl) return;
        const pageNum = parseInt(page, 10);
        if (!Number.isFinite(pageNum) || pageNum < 1) return;
        // The wrap is a direct child of the continuous container; the thumbnails
        // sidebar carries data-page too and must not win the lookup.
        const wrap = rootEl.querySelector(`:scope .tm-pdf-viewer__continuous-wrap > [data-page="${pageNum}"]`)
            ?? rootEl.querySelector(`:scope .tm-pdf-viewer__continuous-wrap [data-page="${pageNum}"]`);
        if (wrap && typeof wrap.scrollIntoView === 'function') {
            wrap.scrollIntoView({ block: 'start', behavior: 'auto' });
            // Optimistic current-page update: the scroll tracker confirms (or refines)
            // it on the next scroll event, and the fit math reads a fresh page meanwhile.
            const state = _docs.get(rootEl);
            if (state?._continuousEl?.isConnected) {
                state.currentPage = pageNum;
            }
        }
    }

    // ── Fit modes (PD2) ──────────────────────────────────────────────────────

    // The component's absolute scale bounds (TmPdfViewer.MinScale/MaxScale), kept for
    // the inline fallback below; the single source of truth is pdf-viewer-fit.mjs. A
    // fit scale is ALLOWED to fall outside the component's zoom steps (the exact
    // container ratio beats the steps) — it is clamped only to these absolute bounds
    // so the canvas can never collapse or explode. Documented on
    // TmPdfViewer.ApplyZoomModeAsync / OnFitScaleComputed.
    const FIT_MIN_SCALE = 0.1;
    const FIT_MAX_SCALE = 5;

    function _wrapPadding(el) {
        const style = window.getComputedStyle(el);
        return {
            x: (parseFloat(style.paddingLeft) || 0) + (parseFloat(style.paddingRight) || 0),
            y: (parseFloat(style.paddingTop) || 0) + (parseFloat(style.paddingBottom) || 0)
        };
    }

    // DOM adapter around the pure pdf-viewer-fit.mjs math: reads the container box,
    // then delegates. The inline branch is the fallback for a failed module load and
    // must stay in sync with computeFitScale.
    function _computeFitScale(containerEl, viewport, mode) {
        if (!containerEl) return 0;
        const pad = _wrapPadding(containerEl);
        const availableWidth = containerEl.clientWidth - pad.x;
        const availableHeight = mode === 'fit-page' ? containerEl.clientHeight - pad.y : null;
        if (_fitModule) {
            return _fitModule.computeFitScale(viewport, availableWidth, availableHeight, mode);
        }
        if (!(availableWidth >= 1)) return 0;
        let scale;
        if (mode === 'fit-page') {
            if (!(availableHeight >= 1)) return 0;
            scale = Math.min(availableWidth / viewport.width, availableHeight / viewport.height);
        } else {
            scale = availableWidth / viewport.width;
        }
        return Math.min(FIT_MAX_SCALE, Math.max(FIT_MIN_SCALE, scale));
    }

    // Call-time mode resolution for the ResizeObserver recompute (see _recomputeFit).
    function _resolveRecomputeFitMode(state) {
        if (_fitModule) return _fitModule.resolveRecomputeFitMode(state);
        return state?.pdfDoc && state.fitMode ? state.fitMode : null;
    }

    function _isRecomputeValid(state, mode) {
        if (_fitModule) return _fitModule.isRecomputeValid(state, mode);
        return !!state && state.fitMode === mode;
    }

    async function fitScale(rootEl, containerEl, mode, dotNetRef) {
        // Warm the pure fit math (node-tested); a failed load keeps the inline
        // fallback working.
        await _ensureFitModule().catch(() => { });
        const state = _docs.get(rootEl);
        if (!state?.pdfDoc) return 0;
        const page = await state.pdfDoc.getPage(state.currentPage);
        const viewport = page.getViewport({ scale: 1, rotation: state.rotation });
        const scale = _computeFitScale(containerEl, viewport, mode);
        if (scale <= 0) return 0;
        state.fitMode = mode;
        // Keep the scale following container resizes (fit-width must survive a panel
        // resize): the observer re-reports through OnFitScaleComputed, and the component
        // decides whether the changed scale is worth a re-render. Registered once per
        // container — a repeated fitScale call for the same container must not stack
        // observers. The callback takes NO captured mode: a fit-width → fit-page switch
        // reuses the same container, so the recompute must read state.fitMode at call
        // time (_recomputeFit does exactly that).
        if (typeof ResizeObserver !== 'undefined' && containerEl && state.fitObserverEl !== containerEl) {
            state.fitObserver?.disconnect();
            const observer = new ResizeObserver(() => _recomputeFit(rootEl, containerEl, dotNetRef));
            observer.observe(containerEl);
            state.fitObserver = observer;
            state.fitObserverEl = containerEl;
        }
        dotNetRef?.invokeMethodAsync('OnFitScaleComputed', scale).catch(() => { });
        return scale;
    }

    const FIT_RECOMPUTE_DEBOUNCE_MS = 100;

    function _recomputeFit(rootEl, containerEl, dotNetRef) {
        const state = _docs.get(rootEl);
        // Read the mode at CALL time, from the live state: the observer outlives
        // fit-mode switches on the same container, and a mode captured at registration
        // would silently pin the old mode after fit-width → fit-page. Null when the
        // document is gone or clearFit ran — nothing to recompute.
        const mode = _resolveRecomputeFitMode(state);
        if (!mode) return;
        const compute = () => {
            // The mode may have switched (or cleared) while the debounce waited or
            // during the async page lookup.
            if (!_isRecomputeValid(state, mode)) return;
            state.pdfDoc.getPage(state.currentPage).then(page => {
                if (!_isRecomputeValid(state, mode)) return;
                const viewport = page.getViewport({ scale: 1, rotation: state.rotation });
                const scale = _computeFitScale(containerEl, viewport, mode);
                if (scale > 0) {
                    dotNetRef?.invokeMethodAsync('OnFitScaleComputed', scale).catch(() => { });
                }
            }).catch(() => { });
        };
        // Continuous mode: the wrap's box shifts while the lazy renderer paints and
        // releases pages — debounce the recompute so the fit settles instead of
        // chasing every intermediate layout.
        if (state._continuousEl?.isConnected) {
            clearTimeout(state._fitDebounceTimer);
            state._fitDebounceTimer = setTimeout(compute, FIT_RECOMPUTE_DEBOUNCE_MS);
            return;
        }
        compute();
    }

    function clearFit(rootEl) {
        const state = _docs.get(rootEl);
        if (!state) return;
        state.fitMode = null;
        if (state._fitDebounceTimer) {
            clearTimeout(state._fitDebounceTimer);
            state._fitDebounceTimer = null;
        }
        if (state.fitObserver) {
            state.fitObserver.disconnect();
            state.fitObserver = null;
            state.fitObserverEl = null;
        }
    }

    function destroy(rootEl) {
        const state = _docs.get(rootEl);
        if (!state) return;
        // Invalidate every queued/looping render (the serial pump, a debounce fit
        // recompute, a pending _resolvePageSizes) at the generation guards.
        state.buildGen++;
        state.paintGen++;
        // Tear down everything the entry accumulated. For a loaded state this releases the
        // document and its renders; for a pending state (download still in flight — the
        // init() notices its stale token and discards the result) it makes sure an abort
        // signal, overlay observers or a selection handler attached before the delete are
        // released instead of leaking on a keyed-by-root re-init.
        if (state._selectionHandler) {
            document.removeEventListener('selectionchange', state._selectionHandler);
            document.removeEventListener('pointerup', state._pointerUpHandler ?? state._selectionHandler);
            clearTimeout(state._selectionDebounce);
            state._selectionHandler = null;
            state._pointerUpHandler = null;
        }
        // The pinch handle (and its touch-action restore) detaches with the instance.
        state._pinch?.detach();
        state._pinch = null;
        state._pinchEl = null;
        state._pinchLive = null;
        // Removes every document listener bound with the instance signal and
        // stops overlay alignment observers.
        state.abort?.abort();
        // Continuous-mode bindings: the per-wrap IntersectionObserver and the
        // scroll tracker (rAF + its own AbortController).
        state._pageObserver?.disconnect();
        state._pageObserver = null;
        state._continuousScrollController?.abort();
        state._continuousScrollController = null;
        if (state._fitDebounceTimer) {
            clearTimeout(state._fitDebounceTimer);
            state._fitDebounceTimer = null;
        }
        state._renderQueue = new Set();
        state._continuousEl = null;
        if (state.fitObserver) {
            state.fitObserver.disconnect();
            state.fitObserver = null;
            state.fitObserverEl = null;
        }
        if (state.overlayObservers instanceof Map) {
            for (const observer of state.overlayObservers.values()) {
                try { observer.disconnect(); } catch { /* already disconnected */ }
            }
            state.overlayObservers.clear();
        }
        if (state.renderTasks instanceof Map) {
            for (const task of state.renderTasks.values()) {
                try { task?.cancel?.(); } catch { /* task already settled */ }
            }
            state.renderTasks.clear();
        }
        try { state.pdfDoc?.destroy(); } catch { /* already destroyed */ }
        _docs.delete(rootEl);
    }

    // The mobile "More" menu offers Download as a plain menu item: the click cannot carry an
    // anchor, so the download is triggered programmatically. The decision (protocol
    // allow-list, same-origin vs. cross-origin) comes from pdf-viewer-download.mjs:
    // same-origin and blob: URLs take the anchor + download path; any other origin is
    // opened in a new tab (a cross-origin `download` attribute is ignored by browsers and
    // must never turn into a navigation). A module that fails to load downloads nothing.
    async function downloadUrl(url) {
        if (!url) return;
        let decision = null;
        try {
            const mod = await _ensureDownloadModule();
            decision = mod.resolveDownload(url, document.baseURI);
        } catch {
            decision = null;
        }
        if (!decision || decision.method === 'none') return;

        if (decision.method === 'window') {
            window.open(decision.href, '_blank', 'noopener,noreferrer');
            return;
        }

        const a = document.createElement('a');
        a.href = decision.href;
        a.download = '';
        a.rel = 'noopener';
        document.body.appendChild(a);
        a.click();
        a.remove();
    }

    // Scrolls the thread CARD owning `element` (a comment item's edit input, say) into view —
    // used by the shared comment item when the inline edit opens in a scrolled sheet: focus
    // alone can leave the card's author row clipped under the sheet header (phase 10 backlog).
    function scrollCardIntoView(element) {
        const card = element?.closest?.('.tm-pdf-thread-card');
        if (!card) return false;
        card.scrollIntoView({ block: 'nearest' });
        return true;
    }

    // Restores focus to the toolbar's search toggle — the Escape path of the floating
    // search card (FZE-11): the card closes and the keyboard user keeps their place on
    // the toolbar. The toggle is marked with data-tm-search-toggle (prefix-independent),
    // not data-testid, whose value can carry a host TestIdPrefix. When the toggle has
    // overflowed into the "More" menu it renders collapsed (inert) — focusing it would
    // be a no-op that drops the user on a dead end, so the toolbar's More trigger takes
    // the focus instead; with no toolbar at all, the viewer root (tabindex=-1) does.
    function focusSearchToggle(rootEl) {
        const toggle = rootEl?.querySelector?.('[data-tm-search-toggle="true"]');
        if (toggle && !toggle.hasAttribute('inert')) {
            toggle.focus();
            return true;
        }
        const moreTrigger = rootEl?.querySelector?.('.tm-toolbar button.tm-toolbar-more');
        if (moreTrigger) {
            moreTrigger.focus();
            return true;
        }
        if (rootEl) {
            rootEl.tabIndex = -1;
            rootEl.focus();
            return true;
        }
        return false;
    }

    return {
        isAvailable,
        init,
        renderPage,
        getTotalPages,
        setScale,
        setRotation,
        renderTextLayer,
        renderThumbnails,
        highlightThumbnail,
        enableSelection,
        enablePinchZoom,
        syncOverlay,
        search,
        searchNext,
        searchPrev,
        redrawSearch,
        clearSearch,
        renderAllPages,
        updateContinuousScale,
        scrollToPage,
        fitScale,
        clearFit,
        clearSelection,
        scrollCardIntoView,
        focusSearchToggle,
        // Internal (test surface): the pure selection-report identity (M3).
        _selectionKey,
        downloadUrl,
        destroy
    };
})();
