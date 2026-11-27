// Shared text-rect extraction for the DOM overlays that map a model range (a diagnostic, a search match) to
// page rectangles. The layout's own textRects carry NO start/end offsets and nest the geometry under `.rect`
// ({ id, blockId, runId, pageIndex, rect: { x, y, width, height } }); the display list's textRun / field /
// listLabel commands carry the flat x / y / width / height AND the start / end offsets. Prefer the commands and
// fall back to the layout rects, normalising both to one flat shape.

export function extractTextRects(render) {
    const commands = Array.isArray(render?.displayList?.commands)
        ? render.displayList.commands
        : Array.isArray(render?.commands)
            ? render.commands
            : [];
    const commandRects = commands
        .filter(command => command?.type === 'textRun' || command?.type === 'field' || command?.type === 'listLabel')
        .map(normalizeTextRect);
    if (commandRects.some(rect => rect.end > rect.start)) {
        return commandRects;
    }

    const layoutRects = Array.isArray(render?.displayList?.textRects)
        ? render.displayList.textRects
        : Array.isArray(render?.selectionLayout?.textRects)
            ? render.selectionLayout.textRects
            : [];
    return layoutRects.map(normalizeTextRect);
}

export function normalizeTextRect(item) {
    const rect = item?.rect || item || {};
    return {
        blockId: String(item?.blockId || ''),
        start: Number(item?.start ?? item?.startOffset ?? 0) || 0,
        end: Number(item?.end ?? item?.endOffset ?? 0) || 0,
        pageIndex: Number(item?.pageIndex || 0) || 0,
        x: Number(rect?.x || 0) || 0,
        y: Number(rect?.y || 0) || 0,
        width: Math.max(0, Number(rect?.width || 0) || 0),
        height: Math.max(1, Number(rect?.height || 0) || 16),
        baseline: Number(item?.baseline || 0) || 0,
    };
}
