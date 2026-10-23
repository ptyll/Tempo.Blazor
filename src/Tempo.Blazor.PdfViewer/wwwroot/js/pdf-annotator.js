/**
 * Tempo Blazor — PDF Annotator JS Interop
 *
 * Companion to pdf-viewer.js used by TmPdfAnnotator:
 *  - measure:                   size of the annotation surface for pointer normalization
 *  - rememberFocus:             capture document.activeElement as the mobile-sheet opener
 *  - focusOpener:               restore focus to the opener (fallback: first action-bar tile)
 *  - focusDescendant:           focus the first element matching a selector inside a scope
 *  - scrollSelectedCardIntoView: scrollIntoView({block:'nearest'}) the selected thread card
 *  - focusFirstAction:          focus the first mobile action-bar tile
 *  - isEditableFocused:        whether the focus sits in an editable inside the root
 *  - attachHistoryShortcuts:   ONE root keydown listener for the undo/redo shortcuts (FZE-15);
 *                              filters Ctrl xor ⌘ / z|y / !repeat / no editable focus, then
 *                              invokes OnHistoryShortcut(redo) on the instance reference
 *  - detachHistoryShortcuts:   unregisters the per-instance (AbortController) listener
 *  - exportPdf:                 embed annotations into the PDF (pdf-lib), optionally flattened
 *  - printWithAnnotations:      render pages + annotation overlays into a print frame
 *
 * Requires pdf.min.mjs (PDF.js v5) and pdf-lib.min.js in the same directory.
 */
window.tmPdfAnnotator = (function () {
    'use strict';

    const _scriptDir = (() => {
        const src = document.currentScript?.src ?? '';
        return src ? src.substring(0, src.lastIndexOf('/') + 1) : '_content/Tempo.Blazor.PdfViewer/js/';
    })();

    let _pdfjs = null;
    let _pdfLibPromise = null;

    function isAvailable() {
        return true;
    }

    function measure(el) {
        if (!el) return [0, 0];
        return [el.clientWidth, el.clientHeight];
    }

    /**
     * Focuses the first tile of the annotator's mobile action bar (the one reopen path after
     * the bottom sheet unmounted). Same contract as the diagram editor's helper; a missing bar
     * (tests, a host without export) is a safe no-op.
     */
    function focusFirstAction(root) {
        if (!root) return false;
        const el = root.querySelector('.tm-mobile-action-bar__action');
        if (!el) return false;
        el.focus({ preventScroll: true });
        return true;
    }

    // The element that opened the mobile sheet (thread marker, Comments tile or the palette
    // tile arming a draft) — captured on open, restored on close (phase 8 review 1).
    let _sheetOpener = null;

    /**
     * Captures document.activeElement inside the annotator root as the sheet opener. A body
     * focus (a tapped non-focusable marker) records nothing, so the close restore falls back
     * to the first action-bar tile.
     */
    function rememberFocus(root) {
        const active = typeof document !== 'undefined' ? document.activeElement : null;
        _sheetOpener = active && active !== document.body && root && root.contains(active)
            ? active
            : null;
        return _sheetOpener !== null;
    }

    /**
     * Returns focus to the remembered sheet opener; only when it is gone (detached, or nothing
     * was recorded) does focus fall back to the first action-bar tile — the one reopen path on
     * a phone.
     */
    function focusOpener(root) {
        const opener = _sheetOpener;
        _sheetOpener = null;
        if (opener && opener.isConnected) {
            opener.focus({ preventScroll: true });
            return true;
        }
        return focusFirstAction(root);
    }

    /**
     * Focuses the first element matching `selector` inside `root` (the annotator root or a
     * comment item). Missing scopes and missing elements are safe no-ops; used for the sheet
     * composer focus and the edit save/cancel restore to the comment's "⋯" trigger.
     */
    /**
     * FZE-15: reports whether the focus sits in an editable element (textarea, text-like
     * input, select or contenteditable) inside the annotator root — the annotator's Ctrl+Z /
     * Ctrl+Shift+Z / Ctrl+Y shortcuts must not fire there, the native text undo wins.
     * Only called on an actual shortcut press.
     */
    function isEditableFocused(root) {
        const active = typeof document !== 'undefined' ? document.activeElement : null;
        if (!active || !root || !root.contains(active)) return false;
        if (active.isContentEditable) return true;
        const tag = active.tagName;
        if (tag === 'TEXTAREA' || tag === 'SELECT') return true;
        if (tag === 'INPUT') {
            const type = (active.getAttribute('type') || '').toLowerCase();
            return !['button', 'checkbox', 'radio', 'file', 'submit', 'reset', 'image'].includes(type);
        }
        return false;
    }

    // FZE-15 review: the undo/redo shortcut filter runs on ONE keydown listener per annotator
    // root instead of a Blazor event handler — a keystroke storm (a held key) must not hit
    // the render tree. The listener is keyed off the root element (per instance) through an
    // AbortController, so a re-attach replaces it and dispose/destroy unregisters it.
    const _shortcutControllers = new WeakMap();

    /**
     * Attaches the annotator's history shortcut listener to the root element. The listener
     * filters locally (exactly one of Ctrl / ⌘, the z/y keys, no held-key repeat, no editable
     * focus — the native text undo wins there), calls preventDefault and invokes
     * `OnHistoryShortcut(redo)` on the instance's DotNetObjectReference; Blazor is reached
     * only for an actual shortcut press. Returns true when attached.
     */
    function attachHistoryShortcuts(root, dotNetRef) {
        if (!root || !dotNetRef || typeof root.addEventListener !== 'function') return false;
        detachHistoryShortcuts(root); // per instance: a re-attach replaces the old listener
        const controller = new AbortController();
        _shortcutControllers.set(root, controller);
        root.addEventListener('keydown', (e) => {
            if (e.repeat) return;
            if (e.altKey || e.ctrlKey === e.metaKey) return; // exactly one of Ctrl / ⌘
            const key = (e.key || '').toLowerCase();
            if (key !== 'z' && key !== 'y') return;
            if (isEditableFocused(root)) return;
            e.preventDefault();
            const redo = key === 'y' || e.shiftKey;
            dotNetRef.invokeMethodAsync('OnHistoryShortcut', redo).catch(() => { });
        }, { signal: controller.signal });
        return true;
    }

    /**
     * Unregisters the root's history shortcut listener (aborts its AbortController).
     * Returns true when a listener was removed.
     */
    function detachHistoryShortcuts(root) {
        if (!root) return false;
        const controller = _shortcutControllers.get(root);
        if (!controller) return false;
        controller.abort();
        _shortcutControllers.delete(root);
        return true;
    }

    /**
     * Focuses the first element matching `selector` inside `root` (the annotator root or a
     * comment item). Missing scopes and missing elements are safe no-ops; used for the sheet
     * composer focus and the edit save/cancel restore to the comment's "⋯" trigger.
     */
    function focusDescendant(root, selector) {
        if (!root || !selector) return false;
        const el = root.querySelector(selector);
        if (!el) return false;
        el.focus({ preventScroll: true });
        return true;
    }

    /**
     * Scrolls the selected thread card into view — used when the mobile sheet opens on a tapped
     * thread, so the peek snap still shows the target card. The card is aligned by its BOTTOM
     * edge: the sticky reply composer rides at the card's bottom, so an `end` alignment lands
     * the composer on the sheet's visible band (the pinned position) instead of leaving it
     * below the band the way a top-aligned oversized card would.
     */
    function scrollSelectedCardIntoView(root) {
        if (!root) return false;
        const card = root.querySelector('.tm-pdf-thread-card--selected');
        if (!card) return false;
        card.scrollIntoView({ block: 'end' });
        return true;
    }

    async function _ensurePdfJs() {
        if (_pdfjs) return _pdfjs;
        const mod = await import(_scriptDir + 'pdf.min.mjs');
        mod.GlobalWorkerOptions.workerSrc = _scriptDir + 'pdf.worker.min.mjs';
        _pdfjs = mod;
        return _pdfjs;
    }

    function _ensurePdfLib() {
        if (window.PDFLib) return Promise.resolve(window.PDFLib);
        if (_pdfLibPromise) return _pdfLibPromise;
        _pdfLibPromise = new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = _scriptDir + 'pdf-lib.min.js';
            script.onload = () => window.PDFLib ? resolve(window.PDFLib) : reject(new Error('pdf-lib failed to load'));
            script.onerror = () => reject(new Error('pdf-lib failed to load'));
            document.head.appendChild(script);
        });
        return _pdfLibPromise;
    }

    function _hexToRgb(color) {
        const fallback = { r: 0.15, g: 0.39, b: 0.92 };
        if (typeof color !== 'string') return fallback;
        const m = color.trim().match(/^#([0-9a-f]{3}|[0-9a-f]{6})$/i);
        if (!m) return fallback;
        let hex = m[1];
        if (hex.length === 3) hex = hex.split('').map(c => c + c).join('');
        return {
            r: parseInt(hex.substring(0, 2), 16) / 255,
            g: parseInt(hex.substring(2, 4), 16) / 255,
            b: parseInt(hex.substring(4, 6), 16) / 255
        };
    }

    // pdf-lib standard fonts only encode WinAnsi; strip diacritics and replace anything
    // still outside Latin-1 so drawText/widthOfTextAtSize never throw (e.g. "Bedřich").
    function _winAnsiSafe(text) {
        return String(text ?? '')
            .normalize('NFKD')
            .replace(/[̀-ͯ]/g, '')
            .replace(/[‘’]/g, "'")
            .replace(/[“”]/g, '"')
            .replace(/[–—]/g, '-')
            .replace(/…/g, '...')
            .replace(/[^\x20-\x7e\xa0-\xff]/g, '?');
    }

    function _escapeHtml(text) {
        return String(text ?? '')
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    async function _fetchBytes(url) {
        const response = await fetch(url, { credentials: 'same-origin' });
        if (!response.ok) throw new Error('Failed to fetch PDF: ' + response.status);
        return await response.arrayBuffer();
    }

    function _download(bytes, fileName) {
        const blob = new Blob([bytes], { type: 'application/pdf' });
        const link = document.createElement('a');
        link.href = URL.createObjectURL(blob);
        link.download = fileName || 'annotated.pdf';
        document.body.appendChild(link);
        link.click();
        link.remove();
        // Keep the blob URL alive long enough for slow consumers (e.g. automated
        // download capture) — revoking too early cancels an in-flight download.
        setTimeout(() => URL.revokeObjectURL(link.href), 60000);
    }

    // ── Export ───────────────────────────────────────────────────────────────

    async function exportPdf(url, payloadJson, optionsJson) {
        const annotations = JSON.parse(payloadJson || '[]');
        const options = JSON.parse(optionsJson || '{}');
        const PDFLib = await _ensurePdfLib();
        const bytes = await _fetchBytes(url);
        const doc = await PDFLib.PDFDocument.load(bytes, { ignoreEncryption: true });
        const font = await doc.embedFont(PDFLib.StandardFonts.HelveticaBold);

        for (const a of annotations) {
            const pageIndex = (a.page | 0) - 1;
            if (pageIndex < 0 || pageIndex >= doc.getPageCount()) continue;
            const page = doc.getPage(pageIndex);
            if (options.flatten) {
                _flattenAnnotation(PDFLib, page, a, font);
            } else {
                _attachAnnotation(PDFLib, doc, page, a);
            }
        }

        if (options.flatten) {
            _appendSummaryPages(PDFLib, doc, font, annotations);
        }

        const outBytes = await doc.save({ useObjectStreams: false });
        _download(outBytes, options.fileName);
    }

    function _flattenAnnotation(PDFLib, page, a, font) {
        const { width: W, height: H } = page.getSize();
        const color = _hexToRgb(a.color);
        const rgb = PDFLib.rgb(color.r, color.g, color.b);

        if (a.kind === 'highlight' && Array.isArray(a.rects) && a.rects.length) {
            for (const r of a.rects) {
                page.drawRectangle({
                    x: r.x * W,
                    y: H - (r.y + r.height) * H,
                    width: r.width * W,
                    height: r.height * H,
                    color: rgb,
                    opacity: 0.35
                });
            }
            return;
        }

        if (a.kind === 'drawing' && Array.isArray(a.strokes)) {
            for (const stroke of a.strokes) {
                const pts = stroke.points || [];
                const thickness = Math.max(0.75, (stroke.thickness || 0.004) * W);
                for (let i = 1; i < pts.length; i++) {
                    page.drawLine({
                        start: { x: pts[i - 1].x * W, y: H - pts[i - 1].y * H },
                        end: { x: pts[i].x * W, y: H - pts[i].y * H },
                        thickness,
                        color: rgb,
                        lineCap: PDFLib.LineCapStyle.Round,
                        opacity: 0.9
                    });
                }
            }
            return;
        }

        if (a.kind === 'stamp') {
            const x = a.x * W;
            const h = Math.max(a.height * H, 14);
            const w = Math.max(a.width * W, 40);
            const y = H - a.y * H - h;
            page.drawRectangle({
                x, y, width: w, height: h,
                borderColor: rgb, borderWidth: 1.5,
                color: PDFLib.rgb(1, 1, 1), opacity: 0.6, borderOpacity: 1
            });
            const text = _winAnsiSafe(a.stampText || '');
            if (text) {
                let size = h * 0.5;
                while (size > 5 && font.widthOfTextAtSize(text, size) > w - 8) size -= 0.5;
                page.drawText(text, {
                    x: x + (w - font.widthOfTextAtSize(text, size)) / 2,
                    y: y + (h - size) / 2 + size * 0.12,
                    size,
                    font,
                    color: rgb
                });
            }
            return;
        }

        // Comment marker: numbered disc at the anchor point.
        const cx = a.x * W;
        const cy = H - a.y * H;
        page.drawCircle({ x: cx, y: cy, size: 9, color: rgb, opacity: 0.95 });
        const label = String(a.number || '');
        if (label) {
            const size = 9;
            page.drawText(label, {
                x: cx - font.widthOfTextAtSize(label, size) / 2,
                y: cy - size * 0.36,
                size,
                font,
                color: PDFLib.rgb(1, 1, 1)
            });
        }
    }

    function _appendSummaryPages(PDFLib, doc, font, annotations) {
        const withComments = annotations.filter(a =>
            (a.comments || []).some(c => (c.body || '').trim().length) || a.quote || a.stampText);
        if (!withComments.length) return;

        const pageSize = [595.28, 841.89]; // A4
        let page = doc.addPage(pageSize);
        let y = 800;
        const left = 50;
        const lineHeight = 14;

        const writeLine = (text, size, color) => {
            if (y < 60) {
                page = doc.addPage(pageSize);
                y = 800;
            }
            page.drawText(_winAnsiSafe(text), { x: left, y, size, font, color });
            y -= lineHeight;
        };

        const wrap = (text, size, maxWidth) => {
            const words = _winAnsiSafe(text).split(/\s+/);
            const lines = [];
            let line = '';
            for (const word of words) {
                const candidate = line ? line + ' ' + word : word;
                if (font.widthOfTextAtSize(candidate, size) > maxWidth && line) {
                    lines.push(line);
                    line = word;
                } else {
                    line = candidate;
                }
            }
            if (line) lines.push(line);
            return lines;
        };

        writeLine('Annotations', 16, PDFLib.rgb(0, 0, 0));
        y -= lineHeight / 2;

        for (const a of withComments) {
            const color = _hexToRgb(a.color);
            const rgb = PDFLib.rgb(color.r, color.g, color.b);
            writeLine('#' + a.number + '  [' + a.kind + ']  page ' + a.page
                + (a.status === 'resolved' ? '  (resolved)' : ''), 11, rgb);
            if (a.quote) {
                for (const line of wrap('"' + a.quote + '"', 9, 470)) {
                    writeLine(line, 9, PDFLib.rgb(0.35, 0.35, 0.35));
                }
            }
            if (a.stampText) {
                writeLine(a.stampText, 9, rgb);
            }
            for (const c of (a.comments || [])) {
                if (!(c.body || '').trim()) continue;
                for (const line of wrap((c.author || '') + ': ' + c.body, 9, 470)) {
                    writeLine(line, 9, PDFLib.rgb(0.1, 0.1, 0.1));
                }
            }
            y -= lineHeight / 2;
        }
    }

    function _attachAnnotation(PDFLib, doc, page, a) {
        const { width: W, height: H } = page.getSize();
        const color = _hexToRgb(a.color);
        const colorArray = [color.r, color.g, color.b];
        const contents = (a.comments || [])
            .filter(c => (c.body || '').trim().length)
            .map(c => (c.author ? c.author + ': ' : '') + c.body)
            .join('\n');
        const author = (a.comments && a.comments[0] && a.comments[0].author) || '';
        const context = doc.context;
        const dicts = [];

        if (a.kind === 'highlight' && Array.isArray(a.rects) && a.rects.length) {
            const quadPoints = [];
            let minX = 1, minY = 1, maxX = 0, maxY = 0;
            for (const r of a.rects) {
                const x1 = r.x * W;
                const y1 = H - r.y * H;             // top
                const x2 = (r.x + r.width) * W;
                const y2 = H - (r.y + r.height) * H; // bottom
                quadPoints.push(x1, y1, x2, y1, x1, y2, x2, y2);
                minX = Math.min(minX, r.x); minY = Math.min(minY, r.y);
                maxX = Math.max(maxX, r.x + r.width); maxY = Math.max(maxY, r.y + r.height);
            }
            dicts.push({
                Type: 'Annot', Subtype: 'Highlight',
                Rect: [minX * W, H - maxY * H, maxX * W, H - minY * H],
                QuadPoints: quadPoints,
                C: colorArray, CA: 0.5,
                T: PDFLib.PDFHexString.fromText(author),
                Contents: PDFLib.PDFHexString.fromText(contents || (a.quote || '')),
                F: 4
            });
        } else if (a.kind === 'drawing' && Array.isArray(a.strokes) && a.strokes.length) {
            const inkList = [];
            let minX = 1, minY = 1, maxX = 0, maxY = 0;
            for (const stroke of a.strokes) {
                const flat = [];
                for (const p of (stroke.points || [])) {
                    flat.push(p.x * W, H - p.y * H);
                    minX = Math.min(minX, p.x); minY = Math.min(minY, p.y);
                    maxX = Math.max(maxX, p.x); maxY = Math.max(maxY, p.y);
                }
                if (flat.length >= 4) inkList.push(flat);
            }
            if (inkList.length) {
                dicts.push({
                    Type: 'Annot', Subtype: 'Ink',
                    Rect: [minX * W - 4, H - maxY * H - 4, maxX * W + 4, H - minY * H + 4],
                    InkList: inkList,
                    C: colorArray,
                    BS: { W: Math.max(1, ((a.strokes[0].thickness || 0.004) * W)) },
                    T: PDFLib.PDFHexString.fromText(author),
                    Contents: PDFLib.PDFHexString.fromText(contents),
                    F: 4
                });
            }
        } else if (a.kind === 'stamp') {
            dicts.push({
                Type: 'Annot', Subtype: 'Square',
                Rect: [a.x * W, H - (a.y + a.height) * H, (a.x + a.width) * W, H - a.y * H],
                C: colorArray,
                T: PDFLib.PDFHexString.fromText(author),
                Contents: PDFLib.PDFHexString.fromText(a.stampText || contents),
                F: 4
            });
        } else {
            dicts.push({
                Type: 'Annot', Subtype: 'Text',
                Rect: [a.x * W, H - a.y * H - 20, a.x * W + 20, H - a.y * H],
                Name: 'Comment',
                C: colorArray,
                T: PDFLib.PDFHexString.fromText(author),
                Contents: PDFLib.PDFHexString.fromText(contents),
                Open: false,
                F: 4
            });
        }

        for (const dict of dicts) {
            const ref = context.register(context.obj(dict));
            const annots = page.node.lookup(PDFLib.PDFName.of('Annots'));
            if (annots) {
                annots.push(ref);
            } else {
                page.node.set(PDFLib.PDFName.of('Annots'), context.obj([ref]));
            }
        }
    }

    // ── Print ────────────────────────────────────────────────────────────────

    async function printWithAnnotations(url, payloadJson) {
        const annotations = JSON.parse(payloadJson || '[]');
        const pdfjs = await _ensurePdfJs();
        const pdfDoc = await pdfjs.getDocument(url).promise;

        const byPage = new Map();
        for (const a of annotations) {
            if (!byPage.has(a.page)) byPage.set(a.page, []);
            byPage.get(a.page).push(a);
        }

        const sections = [];
        for (let pageNum = 1; pageNum <= pdfDoc.numPages; pageNum++) {
            const page = await pdfDoc.getPage(pageNum);
            const viewport = page.getViewport({ scale: 1.5 });
            const canvas = document.createElement('canvas');
            canvas.width = viewport.width;
            canvas.height = viewport.height;
            await page.render({ canvasContext: canvas.getContext('2d'), viewport }).promise;
            const overlays = (byPage.get(pageNum) || []).map(a => _overlayHtml(a)).join('');
            sections.push(
                '<div class="page"><div class="page-inner">' +
                '<img src="' + canvas.toDataURL('image/png') + '" alt="page ' + pageNum + '" />' +
                overlays +
                '</div></div>');
        }

        const summary = _summaryHtml(annotations);
        const html =
            '<!DOCTYPE html><html><head><meta charset="utf-8"><title>Print</title><style>' +
            'body{margin:0;font-family:Helvetica,Arial,sans-serif;}' +
            '.page{page-break-after:always;display:flex;justify-content:center;}' +
            '.page-inner{position:relative;}' +
            '.page-inner img{display:block;max-width:100%;height:auto;}' +
            '.hl{position:absolute;opacity:0.35;border-radius:2px;}' +
            '.stamp{position:absolute;display:flex;align-items:center;justify-content:center;' +
            'border:2px solid;font-weight:bold;font-size:12px;letter-spacing:0.08em;' +
            'text-transform:uppercase;transform:rotate(-6deg);background:rgba(255,255,255,0.7);}' +
            '.marker{position:absolute;width:20px;height:20px;border-radius:50% 50% 50% 2px;' +
            'transform:translate(-50%,-100%);color:#fff;font-size:11px;font-weight:bold;' +
            'display:flex;align-items:center;justify-content:center;}' +
            '.ink{position:absolute;inset:0;width:100%;height:100%;}' +
            '.summary{padding:24px;}' +
            '.summary h2{font-size:16px;}' +
            '.summary .item{margin-bottom:12px;font-size:11px;}' +
            '.summary .head{font-weight:bold;}' +
            '.summary .quote{color:#555;font-style:italic;}' +
            '</style></head><body>' + sections.join('') + summary + '</body></html>';

        const frame = document.createElement('iframe');
        frame.style.position = 'fixed';
        frame.style.right = '0';
        frame.style.bottom = '0';
        frame.style.width = '0';
        frame.style.height = '0';
        frame.style.border = '0';
        document.body.appendChild(frame);
        frame.contentDocument.open();
        frame.contentDocument.write(html);
        frame.contentDocument.close();
        await new Promise(resolve => setTimeout(resolve, 250));
        frame.contentWindow.focus();
        frame.contentWindow.print();
        setTimeout(() => frame.remove(), 60000);
    }

    function _pct(value) {
        return (value * 100).toFixed(3) + '%';
    }

    function _overlayHtml(a) {
        const color = _escapeHtml(a.color || '#2563eb');
        if (a.kind === 'highlight' && Array.isArray(a.rects)) {
            return a.rects.map(r =>
                '<div class="hl" style="left:' + _pct(r.x) + ';top:' + _pct(r.y) +
                ';width:' + _pct(r.width) + ';height:' + _pct(r.height) +
                ';background:' + color + '"></div>').join('');
        }
        if (a.kind === 'stamp') {
            return '<div class="stamp" style="left:' + _pct(a.x) + ';top:' + _pct(a.y) +
                ';width:' + _pct(a.width) + ';height:' + _pct(a.height) +
                ';color:' + color + ';border-color:' + color + '">' +
                _escapeHtml(a.stampText || '') + '</div>';
        }
        if (a.kind === 'drawing' && Array.isArray(a.strokes)) {
            const polylines = a.strokes.map(stroke =>
                '<polyline fill="none" stroke="' + color + '" stroke-width="2.5" ' +
                'vector-effect="non-scaling-stroke" stroke-linecap="round" points="' +
                (stroke.points || []).map(p => (p.x * 100).toFixed(3) + ',' + (p.y * 100).toFixed(3)).join(' ') +
                '"/>').join('');
            return '<svg class="ink" viewBox="0 0 100 100" preserveAspectRatio="none">' + polylines + '</svg>';
        }
        return '<div class="marker" style="left:' + _pct(a.x) + ';top:' + _pct(a.y) +
            ';background:' + color + '">' + _escapeHtml(a.number) + '</div>';
    }

    function _summaryHtml(annotations) {
        const items = annotations.filter(a =>
            (a.comments || []).some(c => (c.body || '').trim().length) || a.quote || a.stampText);
        if (!items.length) return '';
        const rows = items.map(a => {
            const color = _escapeHtml(a.color || '#2563eb');
            const comments = (a.comments || [])
                .filter(c => (c.body || '').trim().length)
                .map(c => '<div>' + _escapeHtml((c.author ? c.author + ': ' : '') + c.body) + '</div>')
                .join('');
            return '<div class="item">' +
                '<div class="head" style="color:' + color + '">#' + _escapeHtml(a.number) +
                ' [' + _escapeHtml(a.kind) + '] page ' + _escapeHtml(a.page) +
                (a.status === 'resolved' ? ' (resolved)' : '') + '</div>' +
                (a.quote ? '<div class="quote">&quot;' + _escapeHtml(a.quote) + '&quot;</div>' : '') +
                (a.stampText ? '<div>' + _escapeHtml(a.stampText) + '</div>' : '') +
                comments +
                '</div>';
        }).join('');
        return '<div class="summary"><h2>Annotations</h2>' + rows + '</div>';
    }

    return {
        isAvailable,
        measure,
        rememberFocus,
        focusOpener,
        isEditableFocused,
        attachHistoryShortcuts,
        detachHistoryShortcuts,
        focusDescendant,
        scrollSelectedCardIntoView,
        focusFirstAction,
        exportPdf,
        printWithAnnotations
    };
})();
