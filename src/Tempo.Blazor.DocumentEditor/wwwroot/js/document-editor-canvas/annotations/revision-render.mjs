export const REVIEW_DISPLAY_MODES = Object.freeze({
    allMarkup: 'allMarkup',
    simpleMarkup: 'simpleMarkup',
    noMarkup: 'noMarkup',
    original: 'original',
});

export function normalizeReviewDisplayMode(value) {
    const normalized = String(value || '').replace(/[\s_-]/g, '').toLowerCase();
    if (normalized === 'nomarkup' || normalized === 'final') {
        return REVIEW_DISPLAY_MODES.noMarkup;
    }

    if (normalized === 'original') {
        return REVIEW_DISPLAY_MODES.original;
    }

    if (normalized === 'simplemarkup' || normalized === 'simple') {
        return REVIEW_DISPLAY_MODES.simpleMarkup;
    }

    return REVIEW_DISPLAY_MODES.allMarkup;
}

export function createCanvasRevisionOverlay(options = {}) {
    const doc = options.document || globalThis.document;
    if (!doc || typeof doc.createElement !== 'function') {
        throw new Error('Canvas revision overlay requires a DOM-like document.');
    }

    const root = doc.createElement('div');
    root.className = 'tm-document-canvas-revision-overlay';
    root.setAttribute('data-testid', 'document-canvas-revision-overlay');
    root.setAttribute('aria-hidden', 'true');
    Object.assign(root.style, {
        position: 'absolute',
        inset: '0',
        pointerEvents: 'none',
        zIndex: '33',
    });

    let reviewMode = REVIEW_DISPLAY_MODES.allMarkup;
    let selectedRevisionId = '';
    let markers = [];
    let canvasStack = null;

    function mount(stack) {
        canvasStack = stack || null;
        const parent = canvasStack?.root || stack;
        if (parent?.appendChild && root.parentNode !== parent) {
            parent.appendChild(root);
        }

        return api;
    }

    function update(model, render, options = {}) {
        reviewMode = normalizeReviewDisplayMode(options.reviewMode || reviewMode);
        selectedRevisionId = String(options.selectedRevisionId || selectedRevisionId || '');
        markers = buildRevisionMarkers(model, render, { reviewMode, selectedRevisionId })
            .map(marker => withPagePlacement(marker, canvasStack));
        reconcileRevisionElements(doc, root, markers);
        root.setAttribute('data-canvas-revision-marker-count', String(markers.length));
        root.setAttribute('data-canvas-review-display-mode', reviewMode);
        root.setAttribute('data-canvas-revision-selected-id', selectedRevisionId);
        return snapshot();
    }

    function select(revisionId) {
        selectedRevisionId = String(revisionId || '');
        for (const child of Array.from(root.children || [])) {
            const active = child.getAttribute?.('data-revision-id') === selectedRevisionId;
            const type = child.getAttribute?.('data-canvas-revision-type') || 'insertion';
            child.className = classNameForRevision(type, active);
            child.setAttribute?.('data-canvas-revision-selected', String(active));
        }

        root.setAttribute('data-canvas-revision-selected-id', selectedRevisionId);
        return markerForRevision(selectedRevisionId);
    }

    function markerForRevision(revisionId) {
        const id = String(revisionId || '');
        return markers.find(marker => marker.revisionId === id) || null;
    }

    function destroy() {
        if (root.parentNode) {
            root.parentNode.removeChild(root);
        }

        markers = [];
        canvasStack = null;
    }

    function snapshot() {
        return {
            markerCount: markers.length,
            reviewMode,
            selectedRevisionId,
            markers: markers.map(marker => ({ ...marker })),
        };
    }

    const api = {
        root,
        mount,
        update,
        select,
        markerForRevision,
        snapshot,
        destroy,
    };

    return api;
}

export function buildRevisionMarkers(model, render, options = {}) {
    const reviewMode = normalizeReviewDisplayMode(options.reviewMode);
    if (reviewMode === REVIEW_DISPLAY_MODES.noMarkup) {
        return [];
    }

    const selectedRevisionId = String(options.selectedRevisionId || '');
    const revisions = revisionsOf(model)
        .filter(revision => revision.action === 'pending');
    const commands = commandsOf(render);
    const markers = [];
    const used = new Set();

    for (const command of commands.filter(command => command.type === 'revisionAnchor')) {
        const revisionId = String(command.revisionId || '');
        if (!revisionId) {
            continue;
        }

        const revision = revisions.find(item => item.id === revisionId) || null;
        // A marker must only ever represent a PENDING revision. A revisionAnchor command can still linger for a
        // revision that was just accepted/rejected (e.g. accept-all / reject-all) until the next relayout —
        // rendering a marker for it would leave a stale marker that never clears (Phase17 reject-all bug).
        if (!revision) {
            continue;
        }

        if (reviewMode === REVIEW_DISPLAY_MODES.original && revision.type === 'insertion') {
            continue;
        }

        used.add(revisionId);
        markers.push(createRevisionMarker({
            id: `${revisionId}-${markers.length}`,
            revisionId,
            blockId: String(command.blockId || revision?.range?.blockId || ''),
            pageIndex: Number(command.pageIndex || 0) || 0,
            rect: rectFromCommand(command),
            type: revision?.type || revisionTypeFromCommand(command),
            selected: revisionId === selectedRevisionId,
            simple: reviewMode === REVIEW_DISPLAY_MODES.simpleMarkup,
            source: 'inline-mark',
        }));
    }

    for (const revision of revisions) {
        if (used.has(revision.id)) {
            continue;
        }

        if (reviewMode === REVIEW_DISPLAY_MODES.original && revision.type === 'insertion') {
            continue;
        }

        const range = rectForRevisionRange(revision, commands);
        if (!range) {
            continue;
        }

        markers.push(createRevisionMarker({
            id: `${revision.id}-${markers.length}`,
            revisionId: revision.id,
            blockId: revision.range.blockId,
            pageIndex: range.pageIndex,
            rect: range.rect,
            type: revision.type,
            selected: revision.id === selectedRevisionId,
            simple: reviewMode === REVIEW_DISPLAY_MODES.simpleMarkup,
            source: 'revision-range',
        }));
    }

    const order = new Map(revisions.map((revision, index) => [revision.id, index]));
    return markers.sort((left, right) =>
        (order.get(left.revisionId) ?? 0) - (order.get(right.revisionId) ?? 0)
        || left.revisionId.localeCompare(right.revisionId));
}

export function applyReviewDecision(model, revisionId, action) {
    const normalizedAction = String(action || '').toLowerCase() === 'rejected' ? 'rejected' : 'accepted';
    const working = clone(model || {});
    const revisions = revisionsOf(working);
    const revision = revisions.find(item => item.id === String(revisionId || ''));
    if (!revision || revision.action !== 'pending') {
        return { changed: false, model: working, revision: null };
    }

    if ((revision.type === 'insertion' && normalizedAction === 'rejected')
        || (revision.type === 'deletion' && normalizedAction === 'accepted')) {
        removeRevisionContent(working, revision.id);
    } else {
        removeRevisionMarks(working, revision.id);
    }

    const stored = rawRevisions(working).find(item => String(item?.id || item?.Id || '') === revision.id);
    if (stored) {
        if ('Action' in stored) {
            stored.Action = titleCase(normalizedAction);
        } else {
            stored.action = normalizedAction;
        }
    }

    working.version = Math.max(0, Number(working.version || working.Version || 0) || 0) + 1;
    return { changed: true, model: working, revision: { ...revision, action: normalizedAction } };
}

export function applyReviewDecisionAll(model, action, filter = {}) {
    let working = clone(model || {});
    const targetIds = revisionsOf(working)
        .filter(revision => revision.action === 'pending')
        .filter(revision => matchesFilter(revision, filter))
        .map(revision => revision.id);
    let changed = false;
    for (const revisionId of targetIds) {
        const result = applyReviewDecision(working, revisionId, action);
        working = result.model;
        changed = changed || result.changed;
    }

    return { changed, model: working, revisionIds: targetIds };
}

function reconcileRevisionElements(doc, root, markers) {
    const existing = new Map();
    for (const child of Array.from(root.children || [])) {
        const id = child.getAttribute?.('data-revision-id');
        if (id) existing.set(id, child);
    }

    const next = [];
    const seen = new Set();
    for (const marker of markers) {
        if (seen.has(marker.revisionId)) continue;
        seen.add(marker.revisionId);
        let element = existing.get(marker.revisionId);
        if (!element) {
            element = createRevisionElement(doc, marker);
        } else {
            applyRevisionElement(element, marker);
        }
        next.push(element);
    }

    for (const [id, element] of existing) {
        if (!seen.has(id) && element.parentNode === root) {
            root.removeChild(element);
        }
    }

    let reference = root.firstChild || null;
    for (const element of next) {
        if (element === reference) {
            reference = element.nextSibling || null;
            continue;
        }
        if (typeof root.insertBefore === 'function') {
            root.insertBefore(element, reference);
        } else if (element.parentNode !== root) {
            root.appendChild(element);
        }
    }
}

function createRevisionElement(doc, marker) {
    const element = doc.createElement('button');
    element.type = 'button';
    applyRevisionElement(element, marker);
    return element;
}

function applyRevisionElement(element, marker) {
    element.className = classNameForRevision(marker.type, marker.selected, marker.simple);
    element.setAttribute('data-testid', 'document-canvas-revision-marker');
    element.setAttribute('data-revision-id', marker.revisionId);
    element.setAttribute('data-block-id', marker.blockId);
    element.setAttribute('data-page-index', String(marker.pageIndex));
    element.setAttribute('data-canvas-revision-type', marker.type);
    element.setAttribute('data-canvas-revision-selected', String(marker.selected));
    Object.assign(element.style, {
        position: 'absolute',
        left: `${marker.rect.x * marker.scale + marker.pageOffsetX}px`,
        top: `${marker.rect.y * marker.scale + marker.pageOffsetY}px`,
        width: `${marker.rect.width * marker.scale}px`,
        height: `${marker.rect.height * marker.scale}px`,
        pointerEvents: 'auto',
    });
}

function withPagePlacement(marker, canvasStack) {
    // Prefer the canvas-stack's shared placement snapshot — see comment-overlay.mjs: per-marker
    // offsetLeft/offsetTop reads force reflows on the per-keystroke render path.
    const placement = canvasStack?.getPagePlacements?.()?.get?.(String(marker.pageIndex));
    if (placement) {
        return {
            ...marker,
            pageOffsetX: placement.offsetX,
            pageOffsetY: placement.offsetY,
            scale: placement.scale,
        };
    }

    const page = canvasStack?.pages?.get?.(String(marker.pageIndex));
    const pageElement = page?.pageElement || null;
    const scale = Math.max(0.01, Number(pageElement?.getAttribute?.('data-canvas-page-zoom-scale') || 1) || 1);
    return {
        ...marker,
        pageOffsetX: Number(pageElement?.offsetLeft || 0) || 0,
        pageOffsetY: Number(pageElement?.offsetTop || 0) || 0,
        scale,
    };
}

function classNameForRevision(type, selected = false, simple = false) {
    const normalized = String(type || 'insertion').toLowerCase();
    return [
        'tm-document-canvas-revision-overlay__marker',
        `tm-document-canvas-revision-overlay__marker--${normalized}`,
        selected ? 'tm-document-canvas-revision-overlay__marker--selected' : '',
        simple ? 'tm-document-canvas-revision-overlay__marker--simple' : '',
    ].filter(Boolean).join(' ');
}

function revisionsOf(model) {
    return rawRevisions(model).map(revision => ({
        raw: revision,
        id: String(revision?.id || revision?.Id || ''),
        type: normalizeRevisionType(revision?.type || revision?.Type),
        action: normalizeRevisionAction(revision?.action || revision?.Action),
        authorId: String(revision?.author?.id || revision?.Author?.Id || ''),
        range: normalizeRange(revision?.range || revision?.Range),
    })).filter(revision => revision.id);
}

function rawRevisions(model) {
    return Array.isArray(model?.revisions) ? model.revisions : Array.isArray(model?.Revisions) ? model.Revisions : [];
}

function normalizeRange(range = {}) {
    return {
        blockId: String(range.blockId || range.BlockId || ''),
        startOffset: nullableNumber(range.startOffset ?? range.StartOffset) ?? 0,
        endOffset: nullableNumber(range.endOffset ?? range.EndOffset) ?? nullableNumber(range.startOffset ?? range.StartOffset) ?? 0,
    };
}

export function normalizeRevisionType(value) {
    const normalized = String(value || 'insertion').toLowerCase();
    if (normalized === 'deletion' || normalized === '1') {
        return 'deletion';
    }

    if (normalized === 'formatting' || normalized === '2') {
        return 'formatting';
    }

    return 'insertion';
}

function normalizeRevisionAction(value) {
    const normalized = String(value || 'pending').toLowerCase();
    if (normalized === 'accepted' || normalized === '1') {
        return 'accepted';
    }

    if (normalized === 'rejected' || normalized === '2') {
        return 'rejected';
    }

    return 'pending';
}

function rectForRevisionRange(revision, commands) {
    const blockId = String(revision.range.blockId || '');
    if (!blockId) {
        return null;
    }

    const start = revision.range.startOffset;
    const end = Math.max(start + 1, revision.range.endOffset || start + 1);
    const textCommands = commands.filter(command =>
        (command.type === 'textRun' || command.type === 'field' || command.type === 'formControl')
        && String(command.blockId || '') === blockId
        && Number(command.end ?? 0) > start
        && Number(command.start ?? 0) < end);
    if (textCommands.length === 0) {
        const paragraph = commands.find(command => command.type === 'paragraphBox' && String(command.blockId || '') === blockId);
        return paragraph ? { pageIndex: Number(paragraph.pageIndex || 0) || 0, rect: rectFromCommand(paragraph) } : null;
    }

    return unionCommandRects(textCommands);
}

function commandsOf(render) {
    return Array.isArray(render?.displayList?.commands)
        ? render.displayList.commands
        : Array.isArray(render?.commands)
            ? render.commands
            : [];
}

function unionCommandRects(commands) {
    const first = commands[0];
    const pageIndex = Number(first?.pageIndex || 0) || 0;
    const samePage = commands.filter(command => (Number(command.pageIndex || 0) || 0) === pageIndex);
    const minX = Math.min(...samePage.map(command => Number(command.x || 0) || 0));
    const minY = Math.min(...samePage.map(command => Number(command.y || 0) || 0));
    const maxX = Math.max(...samePage.map(command => (Number(command.x || 0) || 0) + Math.max(1, Number(command.width || 0) || 1)));
    const maxY = Math.max(...samePage.map(command => (Number(command.y || 0) || 0) + Math.max(1, Number(command.height || 0) || 1)));
    return {
        pageIndex,
        rect: { x: minX, y: minY, width: maxX - minX, height: maxY - minY },
    };
}

function rectFromCommand(command) {
    return {
        x: Number(command.x || 0) || 0,
        y: Number(command.y || 0) || 0,
        width: Math.max(2, Number(command.width || 0) || 2),
        height: Math.max(12, Number(command.height || 0) || 12),
    };
}

function createRevisionMarker(marker) {
    return {
        ...marker,
        rect: {
            x: marker.rect.x,
            y: marker.rect.y,
            width: marker.simple ? 4 : Math.max(8, marker.rect.width),
            height: Math.max(14, marker.rect.height),
        },
    };
}

function revisionTypeFromCommand(command) {
    return normalizeRevisionType(command.revisionType || command.type);
}

function removeRevisionContent(model, revisionId) {
    for (const block of blocksOf(model)) {
        const runs = Array.isArray(block?.content?.runs) ? block.content.runs : [];
        block.content.runs = runs.filter(run => !hasRevisionMark(run, revisionId));
        if (block.content.runs.length === 0) {
            block.content.runs.push({ id: stableId('empty'), type: 'text', text: '', marks: [] });
        }
    }
}

function removeRevisionMarks(model, revisionId) {
    for (const block of blocksOf(model)) {
        for (const run of Array.isArray(block?.content?.runs) ? block.content.runs : []) {
            if (Array.isArray(run.marks)) {
                run.marks = run.marks.filter(mark => !(normalizeMarkType(mark?.type) === 'revision' && String(mark?.revisionId || '') === revisionId));
            }
        }
    }
}

function hasRevisionMark(run, revisionId) {
    return Array.isArray(run?.marks) && run.marks.some(mark =>
        normalizeMarkType(mark?.type) === 'revision' && String(mark?.revisionId || '') === revisionId);
}

function blocksOf(model) {
    return Array.isArray(model?.body?.blocks) ? model.body.blocks : [];
}

function matchesFilter(revision, filter = {}) {
    const authorId = String(filter.authorId || filter.AuthorId || '');
    const type = filter.type ?? filter.Type;
    if (authorId && revision.authorId !== authorId) {
        return false;
    }

    return type == null || normalizeRevisionType(type) === revision.type;
}

function normalizeMarkType(type) {
    return String(type || '').replace(/[\s_-]/g, '').toLowerCase();
}

function nullableNumber(value) {
    return value == null || Number.isNaN(Number(value)) ? null : Number(value);
}

function titleCase(value) {
    return value.charAt(0).toUpperCase() + value.slice(1);
}

function stableId(prefix) {
    return `${prefix}-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
}

function clone(value) {
    if (typeof structuredClone === 'function') {
        return structuredClone(value);
    }

    return JSON.parse(JSON.stringify(value ?? null));
}
