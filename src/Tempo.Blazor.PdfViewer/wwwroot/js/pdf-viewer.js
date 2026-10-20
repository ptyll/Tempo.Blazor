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
    //   pending load: { rootEl, token, pending: true, dotNetRef, renderTasks, overlayObservers, abort, renderGen, thumbGen }
    //   loaded:       { rootEl, pdfDoc, token, currentPage, scale, rotation, dotNetRef, renderTasks,
    //                   overlayObservers, abort, renderGen, thumbGen, _selectionHandler }
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
            // generation counters guarding renderAllPages/renderThumbnails loops
            renderGen: 0,
            thumbGen: 0
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
        state.currentPage = pageNum;
        state.scale = scale;
        if (rotation !== undefined) state.rotation = rotation;
        const canvasEl = _getCanvas(rootEl);
        if (!canvasEl) return; // continuous mode renders all pages instead
        try {
            const page = await state.pdfDoc.getPage(pageNum);
            const viewport = page.getViewport({ scale, rotation: state.rotation });
            await _renderToCanvas(canvasEl, page, viewport, { tasks: state.renderTasks });
        } catch (err) {
            if (_isCancelled(err)) return; // superseded by a newer render for this canvas
            console.error('tmPdfViewer.renderPage', err);
        }
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

    async function renderTextLayer(rootEl, textLayerEl, pageNum, scale, rotation) {
        const state = _docs.get(rootEl);
        if (!state?.pdfDoc || !textLayerEl) return;
        try {
            const pdfjs = await _ensureLib();
            const page = await state.pdfDoc.getPage(pageNum);
            const viewport = page.getViewport({ scale, rotation: rotation ?? state.rotation });
            const textContent = await page.getTextContent();

            textLayerEl.innerHTML = '';
            textLayerEl.style.width = viewport.width + 'px';
            textLayerEl.style.height = viewport.height + 'px';

            for (const item of textContent.items) {
                const tx = pdfjs.Util.transform(viewport.transform, item.transform);
                const fontHeight = Math.hypot(tx[0], tx[1]);
                const fontWidth = Math.hypot(tx[2], tx[3]);
                const div = document.createElement('div');
                div.textContent = item.str;
                div.style.position = 'absolute';
                div.style.left = tx[4] + 'px';
                div.style.top = tx[5] + 'px';
                div.style.fontSize = fontHeight + 'px';
                div.style.fontFamily = item.fontName;
                div.style.transform = `scaleX(${fontWidth > 0 ? fontHeight / fontWidth : 1})`;
                div.style.transformOrigin = '0 0';
                div.style.whiteSpace = 'pre';
                div.style.userSelect = 'text';
                div.style.cursor = 'text';
                div.style.opacity = '0.2';
                textLayerEl.appendChild(div);
            }
        } catch (err) {
            console.error('tmPdfViewer.renderTextLayer', err);
        }
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
            const thumbWrap = document.createElement('div');
            thumbWrap.className = 'tm-pdf-thumbnail';
            thumbWrap.dataset.page = String(i);
            thumbWrap.style.cursor = 'pointer';
            thumbWrap.style.marginBottom = '8px';
            thumbWrap.style.padding = '4px';
            thumbWrap.style.borderRadius = '4px';
            thumbWrap.style.transition = 'background .15s';

            const thumbCanvas = document.createElement('canvas');
            thumbCanvas.style.display = 'block';
            thumbCanvas.style.width = '100%';
            thumbCanvas.style.borderRadius = '2px';

            const pageNumLabel = document.createElement('div');
            pageNumLabel.textContent = String(i);
            pageNumLabel.style.textAlign = 'center';
            pageNumLabel.style.fontSize = '11px';
            pageNumLabel.style.marginTop = '2px';
            pageNumLabel.style.color = 'var(--tm-text-muted, #888)';

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
            el.classList.toggle('tm-pdf-thumbnail--active', isActive);
            el.style.background = isActive ? 'var(--tm-primary-100, #e0f2fe)' : 'transparent';
        });
    }

    // ── Text selection ──────────────────────────────────────────────────────

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

        // Per-instance document listener: re-bind if called again, and always
        // unregister in destroy() so multiple viewers on one page (Notion
        // blocks) don't leak document-level listeners.
        if (state._selectionHandler) {
            document.removeEventListener('mouseup', state._selectionHandler);
        }
        const handler = () => {
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

            doc.dotNetRef.invokeMethodAsync('OnTextSelectionChanged', sel.toString(), doc.currentPage, flat).catch(() => { });
        };
        state._selectionHandler = handler;
        document.addEventListener('mouseup', handler, { signal: state.abort?.signal });
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

        if (matches.length) {
            await _activateMatch(rootEl, searchLayerEl);
        } else {
            _drawSearchHighlights(rootEl, searchLayerEl, state.currentPage);
        }
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
    }

    async function _activateMatch(rootEl, searchLayerEl) {
        const state = _docs.get(rootEl);
        if (!state || !state.searchMatches || state.searchActive < 0) return;
        const match = state.searchMatches[state.searchActive];
        if (match.page !== state.currentPage) {
            await renderPage(rootEl, match.page, state.scale, state.rotation);
        }
        _drawSearchHighlights(rootEl, searchLayerEl, match.page);
        _scrollToActive(searchLayerEl);
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

    function _scrollToActive(searchLayerEl) {
        if (!searchLayerEl) return;
        const active = searchLayerEl.querySelector('[data-active="1"]');
        if (active && typeof active.scrollIntoView === 'function') {
            active.scrollIntoView({ block: 'center', inline: 'center' });
        }
    }

    function _clamp01(value) {
        if (!Number.isFinite(value)) return 0;
        return Math.min(Math.max(value, 0), 1);
    }

    async function renderAllPages(rootEl, containerEl, scale, rotation) {
        const state = _docs.get(rootEl);
        if (!state?.pdfDoc || !containerEl) return;
        // Generation guard: zoom/rotate/re-init bump the counter, so an older
        // loop stops appending canvases after each awaited page.
        const gen = ++state.renderGen;
        containerEl.innerHTML = '';
        for (let i = 1; i <= state.pdfDoc.numPages; i++) {
            const wrap = document.createElement('div');
            wrap.style.marginBottom = '16px';
            wrap.style.display = 'flex';
            wrap.style.justifyContent = 'center';
            // Anchor for scrollToPage: the thumbnail sidebar also carries
            // data-page, so continuous-page wraps must be found through their
            // parent container.
            wrap.dataset.page = String(i);

            const pageCanvas = document.createElement('canvas');
            pageCanvas.style.display = 'block';
            pageCanvas.style.boxShadow = '0 1px 3px rgba(0,0,0,0.08)';
            pageCanvas.style.borderRadius = '4px';
            pageCanvas.dataset.page = String(i);

            wrap.appendChild(pageCanvas);
            containerEl.appendChild(wrap);

            try {
                const page = await state.pdfDoc.getPage(i);
                if (state.renderGen !== gen) return; // superseded meanwhile
                const viewport = page.getViewport({ scale: scale ?? 1.0, rotation: rotation ?? 0 });
                await _renderToCanvas(pageCanvas, page, viewport, { tasks: state.renderTasks });
                if (state.renderGen !== gen) return; // superseded meanwhile
            } catch (err) {
                if (_isCancelled(err)) return;
                console.error('tmPdfViewer.renderAllPages page', i, err);
            }
        }
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
        }
    }

    function destroy(rootEl) {
        const state = _docs.get(rootEl);
        if (!state) return;
        // Tear down everything the entry accumulated. For a loaded state this releases the
        // document and its renders; for a pending state (download still in flight — the
        // init() notices its stale token and discards the result) it makes sure an abort
        // signal, overlay observers or a selection handler attached before the delete are
        // released instead of leaking on a keyed-by-root re-init.
        if (state._selectionHandler) {
            document.removeEventListener('mouseup', state._selectionHandler);
            state._selectionHandler = null;
        }
        // Removes every document listener bound with the instance signal and
        // stops overlay alignment observers.
        state.abort?.abort();
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
        syncOverlay,
        search,
        searchNext,
        searchPrev,
        redrawSearch,
        clearSearch,
        renderAllPages,
        scrollToPage,
        destroy
    };
})();
