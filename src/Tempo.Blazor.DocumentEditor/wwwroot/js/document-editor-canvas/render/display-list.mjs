import { layoutCanvasDocument } from '../layout/pagination.mjs';
import { normalizeMarkType } from '../layout/canvas-text-style.mjs';
import { buildPageFrameCommands } from './page-frame.mjs';
import { CANVAS_RENDER_LAYERS, layerForDisplayCommand } from './layers.mjs';
import { imageDisplayCommands } from '../objects/image-render.mjs';
import { buildHeaderFooterLayout } from '../layout/header-footer-layout.mjs';
import { buildNotesLayout } from '../layout/notes-layout.mjs';
import { normalizeMathRun, mathToAccessibleText } from '../math/math-model.mjs';
import { layoutMathRun } from '../math/math-layout.mjs';
import { contentControlDisplayText, normalizeContentControl, validateContentControl } from '../controls/sdt-model.mjs';
import { buildContentControlRenderState, normalizeContentControlRenderMode } from '../controls/sdt-render.mjs';
import { resolveSigningRoleColor } from '../controls/signing-field-model.mjs';

export function buildDisplayList(model, layout, options = {}) {
    const documentModel = model || {};
    const textLayout = layoutCanvasDocument(documentModel, {
        pageSettings: layout?.pageSettings,
        fontMetrics: options.fontMetrics,
        fontMetricsOptions: options.fontMetricsOptions,
        layoutCache: options.layoutCache,
        // Perf plan N11.2: progressive first layout — an optional page/time budget plus the resume
        // token from the previous partial pass. The display list is rebuilt per chunk over ALL pages
        // laid out so far (the command cache keeps unchanged fragments cheap).
        budget: options.layoutBudget || undefined,
        resume: options.layoutResume || undefined,
    });
    const pages = textLayout.pages.length > 0 ? textLayout.pages : (Array.isArray(layout?.pages) ? layout.pages : []);
    const commands = [];
    let sequence = 0;

    for (const page of pages) {
        for (const command of buildPageFrameCommands(page, options.theme || {}, documentModel)) {
            commands.push(withSequence(command, sequence++));
        }
    }

    const commandCacheStats = { hits: 0, misses: 0 };
    for (const command of buildBodyCommands(textLayout, options, commandCacheStats)) {
        commands.push(withSequence(command, sequence++));
    }

    for (const command of buildLineNumberCommands(textLayout)) {
        commands.push(withSequence(command, sequence++));
    }

    const headerFooterLayout = buildHeaderFooterLayout(documentModel, textLayout, {
        fontMetrics: options.fontMetrics,
    });
    for (const command of headerFooterLayout.commands) {
        commands.push(withSequence(command, sequence++));
    }

    const notesLayout = buildNotesLayout(documentModel, textLayout, {
        fontMetrics: options.fontMetrics,
    });
    for (const command of notesLayout.commands) {
        commands.push(withSequence(command, sequence++));
    }

    if (options.debug === true) {
        for (const page of pages) {
            commands.push(withSequence({
                id: `page-${page.index || 0}-diagnostic`,
                type: 'diagnosticOverlay',
                layer: CANVAS_RENDER_LAYERS.diagnostics,
                pageIndex: Number(page.index) || 0,
                x: page.body.x,
                y: page.body.y,
                width: page.body.width,
                height: page.body.height,
                stroke: '#0ea5e9',
                lineWidth: 1,
                dash: [3, 3],
            }, sequence++));
        }
    }

    commands.sort(compareDisplayCommands);
    // Perf plan N6.4: one pass computes the summary counters AND resolves signing-field role colours
    // (body + header/footer) — this used to be a dedicated signing loop plus four full filters.
    // Colour resolution intentionally runs even with no configured roles: the hash-palette fallback
    // colours fields per submitter, which the signing E2E relies on.
    const signingRoles = Array.isArray(options.signingRoles) ? options.signingRoles : [];
    let textRunCount = 0;
    let mathEquationCount = 0;
    let contentControlCount = 0;
    let diagnosticCount = 0;
    for (const command of commands) {
        if (command.type === 'signingField') {
            command.roleColor = resolveSigningRoleColor(command.submitterUuid, signingRoles, command.fieldUuid);
        } else if (command.type === 'textRun') {
            textRunCount += 1;
        } else if (command.type === 'mathEquation') {
            mathEquationCount += 1;
        } else if (command.type === 'formControl') {
            contentControlCount += 1;
        }

        if (command.layer === CANVAS_RENDER_LAYERS.diagnostics) {
            diagnosticCount += 1;
        }
    }

    return {
        schemaVersion: 1,
        layout: textLayout,
        pages,
        commands,
        pageCount: pages.length,
        textRunCount,
        mathEquationCount,
        contentControlCount,
        diagnosticCount,
        textRects: textLayout.textRects,
        lineNumbers: textLayout.lineNumbers,
        headerFooterRegions: headerFooterLayout.regions,
        noteRegions: notesLayout.regions,
        measurementStats: textLayout.measurementStats,
        layoutCacheStats: textLayout.cacheStats || null,
        commandCacheStats,
        // Perf plan N11.2: progressive layout state for the canvas stack / engine continuation loop.
        layoutComplete: textLayout.layoutComplete !== false,
        layoutResume: textLayout.resume || null,
        layoutProgress: textLayout.progress || null,
    };
}

function buildLineNumberCommands(textLayout) {
    return (textLayout.lineNumbers || []).map((lineNumber, index) => ({
        id: lineNumber.id || `line-number-${index}`,
        type: 'lineNumber',
        layer: CANVAS_RENDER_LAYERS.content,
        pageIndex: Number(lineNumber.pageIndex || 0) || 0,
        blockId: lineNumber.blockId || '',
        sectionId: lineNumber.sectionId || '',
        text: lineNumber.text || '',
        x: lineNumber.x,
        y: lineNumber.y,
        baseline: lineNumber.baseline,
        width: lineNumber.width,
        height: lineNumber.height,
        style: lineNumber.style || {},
        columnIndex: Number(lineNumber.columnIndex || 0) || 0,
        sequence: index,
    }));
}

function buildBodyCommands(textLayout, options = {}, stats = null) {
    const commands = [];
    let localSequence = 0;
    const contentControlRenderMode = normalizeContentControlRenderMode(options.contentControlRenderMode);
    // Per-block display-command cache (Phase 4): a paragraph fragment's commands are a pure function
    // of the fragment object. The layout cache (Phase 3) returns the SAME fragment instance for
    // unchanged blocks, so a keystroke re-assembles commands only for the edited block and reuses the
    // rest — making the whole layout -> display-list pipeline O(changed blocks) instead of O(document).
    const commandCache = options.commandCache instanceof WeakMap ? options.commandCache : null;

    for (const block of textLayout.blocks || []) {
        if (block.type === 'table') {
            commands.push({
                id: `${block.blockId || `block-${localSequence}`}-table`,
                type: 'tableBox',
                layer: CANVAS_RENDER_LAYERS.content,
                pageIndex: Number(block.pageIndex) || 0,
                blockId: block.blockId || '',
                x: block.rect.x,
                y: block.rect.y,
                width: block.rect.width,
                height: block.rect.height,
                stroke: '#cbd5e1',
                fill: 'rgba(241, 245, 249, 0.42)',
                sequence: localSequence++,
            });
            for (const cell of block.table?.cells || []) {
                commands.push({
                    id: `${cell.tableId || block.blockId || 'table'}-${cell.cellId || `${cell.rowIndex}-${cell.columnIndex}`}-p${Number(cell.pageIndex ?? block.pageIndex) || 0}-cell${cell.isRepeatedHeader ? '-repeat' : ''}`,
                    type: 'tableCell',
                    layer: CANVAS_RENDER_LAYERS.content,
                    pageIndex: Number(cell.pageIndex ?? block.pageIndex) || 0,
                    blockId: block.blockId || '',
                    tableId: cell.tableId || block.blockId || '',
                    cellId: cell.cellId || '',
                    rowIndex: Number(cell.rowIndex || 0) || 0,
                    columnIndex: Number(cell.columnIndex || 0) || 0,
                    x: cell.rect.x,
                    y: cell.rect.y,
                    width: cell.rect.width,
                    height: cell.rect.height,
                    stroke: cell.borderColor || '#94a3b8',
                    fill: cell.backgroundColor || 'rgba(255, 255, 255, 0.96)',
                    lineWidth: cell.isHeader ? 1.25 : 1,
                    isRepeatedHeader: cell.isRepeatedHeader === true,
                    isTotal: cell.isTotal === true,
                    bandedRow: cell.bandedRow === true,
                    bandedColumn: cell.bandedColumn === true,
                    sequence: localSequence++,
                });
            }
            continue;
        }

        if (block.type === 'image') {
            const imageCommands = imageDisplayCommands(block, localSequence, {
                ...options,
                objectLayouts: textLayout.objectLayouts || [],
            });
            commands.push(...imageCommands);
            localSequence += imageCommands.length;
            continue;
        }

        const cachedBlockCommands = reuseParagraphCommands(commandCache, block, contentControlRenderMode);
        if (cachedBlockCommands) {
            if (stats) {
                stats.hits += 1;
            }

            for (const command of cachedBlockCommands) {
                commands.push(command);
            }

            continue;
        }

        if (stats) {
            stats.misses += 1;
        }

        const blockCommands = buildParagraphBlockCommands(block, options, contentControlRenderMode);
        storeParagraphCommands(commandCache, block, contentControlRenderMode, blockCommands);
        for (const command of blockCommands) {
            commands.push(command);
        }
    }

    for (const label of textLayout.listLabels || []) {
        commands.push({
            id: label.id,
            type: 'listLabel',
            layer: CANVAS_RENDER_LAYERS.content,
            pageIndex: Number(label.pageIndex) || 0,
            blockId: label.blockId || '',
            runId: label.id,
            text: label.text,
            x: label.x,
            y: label.y,
            baseline: label.baseline,
            width: label.width,
            height: label.height,
            style: label.style,
            sequence: localSequence++,
        });
    }

    return commands;
}

function reuseParagraphCommands(cache, block, contentControlRenderMode) {
    if (!cache || !block || typeof block !== 'object') {
        return null;
    }

    const entry = cache.get(block);
    return entry && entry.mode === contentControlRenderMode ? entry.commands : null;
}

function storeParagraphCommands(cache, block, contentControlRenderMode, commands) {
    if (cache && block && typeof block === 'object') {
        cache.set(block, { mode: contentControlRenderMode, commands });
    }
}

// Builds the display commands for a single paragraph fragment. Pure with respect to the fragment:
// uses a local sequence counter (overwritten by the global pass in buildDisplayList) and a local
// command-id disambiguation map (run ids are unique per block, so this is identical to a shared map).
function buildParagraphBlockCommands(block, options, contentControlRenderMode) {
    const commands = [];
    let localSequence = 0;
    const seenTextCommandIds = new Map();

    commands.push({
        id: `${block.blockId || `block-${localSequence}`}-box-${block.pageIndex}`,
        type: 'paragraphBox',
        layer: CANVAS_RENDER_LAYERS.content,
        pageIndex: Number(block.pageIndex) || 0,
        blockId: block.blockId || '',
        x: block.rect.x,
        y: block.rect.y,
        width: block.rect.width,
        height: block.rect.height,
        sequence: localSequence++,
    });

    for (const line of block.lines || []) {
        for (const leader of line.tabLeaders || []) {
            commands.push({
                id: leader.id || `${block.blockId || 'block'}-tab-leader-${localSequence}`,
                type: 'tabLeader',
                layer: CANVAS_RENDER_LAYERS.content,
                pageIndex: Number(leader.pageIndex ?? line.pageIndex ?? block.pageIndex) || 0,
                blockId: block.blockId || leader.blockId || '',
                leader: leader.leader || 'dots',
                alignment: leader.alignment || 'left',
                x: Number(leader.x || 0) || 0,
                y: Number(leader.y || 0) || 0,
                baseline: Number(leader.baseline || line.baseline || 0) || 0,
                width: Math.max(0, Number(leader.width || 0) || 0),
                height: Math.max(1, Number(leader.height || line.rect?.height || 1) || 1),
                style: leader.style || {},
                sequence: localSequence++,
            });
        }

        const lineSegments = positionedLineSegments(line);
        for (const segment of lineSegments) {
            // A signing-field run is an inline object too — it must be checked BEFORE the drawing
            // branch, which would otherwise claim it (`inlineObject === true && !!segment.object`,
            // with object falling back to a truthy `{}`) and emit a bogus imageObject command.
            if (isSigningFieldSegment(segment)) {
                commands.push(signingFieldCommandForSegment(segment, line, block, localSequence++));
                continue;
            }

            if (isDrawingSegment(segment)) {
                const rect = segment.objectRect || segment.rect || {};
                const imageCommands = imageDisplayCommands({
                    blockId: block.blockId || '',
                    runId: segment.runId || '',
                    objectId: segment.objectId || segment.object?.objectId || '',
                    role: 'drawingRun',
                    pageIndex: Number(segment.pageIndex ?? line.pageIndex ?? block.pageIndex) || 0,
                    rect: {
                        x: Number(rect.x || 0) || 0,
                        y: Number(rect.y || 0) || 0,
                        width: Math.max(1, Number(rect.width || segment.object?.width || 1) || 1),
                        height: Math.max(1, Number(rect.height || segment.object?.height || 1) || 1),
                    },
                    object: segment.object || {},
                }, localSequence, options);
                commands.push(...imageCommands);
                localSequence += imageCommands.length;
                continue;
            }

            if (isMathSegment(segment)) {
                const command = mathCommandForSegment(segment, line, block, options, localSequence++);
                commands.push(command);
                for (const annotation of annotationCommandsForRun(command, segment)) {
                    commands.push({ ...annotation, sequence: localSequence++ });
                }
                continue;
            }

            if (isContentControlSegment(segment)) {
                const command = contentControlCommandForSegment(
                    segment,
                    line,
                    block,
                    seenTextCommandIds,
                    localSequence++,
                    contentControlRenderMode);
                commands.push(command);
                for (const annotation of annotationCommandsForRun(command, segment)) {
                    commands.push({ ...annotation, sequence: localSequence++ });
                }
                continue;
            }

            if (!segment.text && segment.type !== 'space') {
                continue;
            }

            const commandType = segment.kind === 'field' || segment.type === 'field' ? 'field' : 'textRun';
            const commandId = stableTextCommandId(segment, seenTextCommandIds);
            const command = {
                id: commandId,
                type: commandType,
                layer: CANVAS_RENDER_LAYERS.content,
                pageIndex: Number(segment.pageIndex ?? line.pageIndex ?? block.pageIndex) || 0,
                blockId: block.blockId || '',
                runId: segment.runId || '',
                text: segment.text || '',
                x: segment.rect.x,
                y: segment.rect.y,
                baseline: line.baseline + (Number(segment.style?.baselineShift || 0) || 0),
                width: segment.rect.width,
                height: segment.rect.height,
                style: segment.style || {},
                marks: Array.isArray(segment.marks) ? segment.marks : [],
                hyphenated: segment.hyphenated === true,
                hyphenation: segment.hyphenation || null,
                start: Number(segment.start || 0) || 0,
                end: Number(segment.end || 0) || 0,
                sequence: localSequence++,
            };
            commands.push(command);
            for (const annotation of annotationCommandsForRun(command, segment)) {
                commands.push({ ...annotation, sequence: localSequence++ });
            }
        }
    }

    return commands;
}

function isDrawingSegment(segment) {
    return String(segment?.kind || segment?.type || '').toLowerCase() === 'drawing'
        || (segment?.inlineObject === true && !!segment?.object);
}

function isMathSegment(segment) {
    return String(segment?.kind || segment?.type || '').toLowerCase() === 'math' || !!segment?.math;
}

function isSigningFieldSegment(segment) {
    return String(segment?.kind || '').toLowerCase() === 'signingfield' || !!segment?.signingField;
}

function signingFieldCommandForSegment(segment, line, block, sequence) {
    const field = segment.signingField || {};
    return {
        id: `${block.blockId || ''}-${String(segment.runId || field.uuid || 'signing')}-signing`,
        type: 'signingField',
        layer: CANVAS_RENDER_LAYERS.content,
        pageIndex: Number(segment.pageIndex ?? line.pageIndex ?? block.pageIndex) || 0,
        blockId: block.blockId || '',
        runId: segment.runId || '',
        fieldUuid: String(field.uuid || ''),
        fieldType: String(field.fieldType || 'text'),
        submitterUuid: String(field.submitterUuid || ''),
        required: field.required === true,
        label: String(field.label || ''),
        options: Array.isArray(field.options) ? field.options : [],
        signingField: field,
        roleColor: '',
        x: segment.rect.x,
        y: segment.rect.y,
        width: segment.rect.width,
        height: segment.rect.height,
        style: segment.style || {},
        sequence,
    };
}

function isContentControlSegment(segment) {
    return String(segment?.kind || segment?.type || '').replace(/[\s_-]/g, '').toLowerCase() === 'contentcontrol'
        || !!segment?.contentControl;
}

function contentControlCommandForSegment(segment, line, block, seenTextCommandIds, sequence, contentControlRenderMode) {
    const normalized = normalizeContentControl(segment?.contentControl?.control || segment?.contentControl || {}, {
        fallbackId: segment.runId || segment.id || 'sdt',
    });
    const validation = validateContentControl(normalized);
    const renderState = buildContentControlRenderState(normalized, { mode: contentControlRenderMode });
    const commandId = stableTextCommandId(segment, seenTextCommandIds);
    return {
        id: commandId,
        type: 'formControl',
        layer: CANVAS_RENDER_LAYERS.content,
        pageIndex: Number(segment.pageIndex ?? line.pageIndex ?? block.pageIndex) || 0,
        blockId: block.blockId || '',
        runId: segment.runId || '',
        controlId: normalized.controlId,
        controlKind: normalized.kind,
        text: segment.text || contentControlDisplayText(normalized),
        x: segment.rect.x,
        y: segment.rect.y,
        baseline: line.baseline + (Number(segment.style?.baselineShift || 0) || 0),
        width: Math.max(10, Number(segment.rect.width || 0) || 10),
        height: Math.max(14, Number(segment.rect.height || 0) || 14),
        style: segment.style || {},
        marks: Array.isArray(segment.marks) ? segment.marks : [],
        start: Number(segment.start || 0) || 0,
        end: Number(segment.end || 0) || 0,
        contentControl: normalized,
        renderState,
        renderMode: renderState.mode,
        designTag: renderState.tagLabel,
        isPlaceholder: normalized.isPlaceholder === true,
        isRequired: normalized.isRequired === true,
        isLocked: normalized.lockContent === true,
        validation,
        sequence,
    };
}

function mathCommandForSegment(segment, line, block, options, sequence) {
    const math = normalizeMathRun({
        id: segment.runId || segment.id || '',
        math: segment.math || {
            content: {
                elements: [{
                    type: 'run',
                    text: segment.text || '□',
                    style: 'normal',
                }],
            },
        },
    });
    const mathLayout = layoutMathRun(math, {
        style: segment.style || {},
        metrics: options.fontMetrics || null,
    });
    const fallbackWidth = Math.max(1, Number(segment.rect?.width || 0) || 1);
    const fallbackHeight = Math.max(1, Number(segment.rect?.height || 0) || 1);
    const height = Math.max(fallbackHeight, mathLayout.height);
    const width = Math.max(fallbackWidth, mathLayout.width);
    const segmentTop = Number(segment.rect?.y || 0) || 0;
    const top = segmentTop + Math.max(0, (height - mathLayout.height) / 2);
    const baseline = top + mathLayout.ascent;
    return {
        id: stableMathCommandId(segment),
        type: 'mathEquation',
        layer: CANVAS_RENDER_LAYERS.content,
        pageIndex: Number(segment.pageIndex ?? line.pageIndex ?? block.pageIndex) || 0,
        blockId: block.blockId || '',
        runId: segment.runId || '',
        mathId: math.mathId,
        displayMode: math.displayMode,
        text: math.altText || mathToAccessibleText(math),
        x: Number(segment.rect?.x || 0) || 0,
        y: top,
        baseline,
        width,
        height,
        style: segment.style || {},
        marks: Array.isArray(segment.marks) ? segment.marks : [],
        start: Number(segment.start || 0) || 0,
        end: Number(segment.end || 0) || 0,
        mathLayout,
        sequence,
    };
}

function stableMathCommandId(segment) {
    return `${String(segment.runId || segment.id || 'math')}-math`;
}

function stableTextCommandId(segment, seenTextCommandIds) {
    const base = String(segment.runId || segment.id || 'text');
    const count = seenTextCommandIds.get(base) || 0;
    seenTextCommandIds.set(base, count + 1);
    return count === 0 ? base : `${base}-${count + 1}`;
}

function annotationCommandsForRun(textCommand, run) {
    const commands = [];
    for (const mark of Array.isArray(run?.marks) ? run.marks : []) {
        const type = normalizeMarkType(mark?.type);
        if (type === 'commentanchor') {
            commands.push({
                id: `${textCommand.id}-comment`,
                type: 'commentAnchor',
                layer: CANVAS_RENDER_LAYERS.annotations,
                pageIndex: textCommand.pageIndex,
                blockId: textCommand.blockId,
                runId: textCommand.runId,
                x: textCommand.x,
                y: textCommand.y,
                width: textCommand.width,
                height: textCommand.height,
                start: textCommand.start,
                end: textCommand.end,
                commentId: mark.commentAnchor?.commentId || '',
            });
        }

        if (type === 'revision') {
            commands.push({
                id: `${textCommand.id}-revision`,
                type: 'revisionAnchor',
                layer: CANVAS_RENDER_LAYERS.annotations,
                pageIndex: textCommand.pageIndex,
                blockId: textCommand.blockId,
                runId: textCommand.runId,
                x: textCommand.x,
                width: textCommand.width,
                height: textCommand.height,
                revisionId: mark.revisionId || '',
            });
        }
    }

    return commands;
}

function positionedLineSegments(line) {
    const segments = (line.segments || []).map(segment => ({
        ...segment,
        rect: { ...(segment.rect || {}) },
    }));
    const justify = line.justify || {};
    const range = (justify.ranges || [justify]).find(candidate => candidate && candidate.enabled) || null;
    const extra = Number(range?.extraSpacePerGap || 0) || 0;
    if (!range || extra <= 0) {
        return segments;
    }

    let shift = 0;
    return segments
        .sort((left, right) => (Number(left.rect?.x) || 0) - (Number(right.rect?.x) || 0))
        .map(segment => {
            segment.rect.x += shift;
            if (segment.type === 'space') {
                segment.rect.width += extra;
                shift += extra;
            }

            return segment;
        });
}

function withSequence(command, sequence) {
    const normalized = {
        ...command,
        sequence,
    };
    normalized.layer = normalized.layer || layerForDisplayCommand(normalized);
    return normalized;
}

function compareDisplayCommands(left, right) {
    const page = (Number(left.pageIndex) || 0) - (Number(right.pageIndex) || 0);
    if (page !== 0) {
        return page;
    }

    const sequence = (Number(left.sequence) || 0) - (Number(right.sequence) || 0);
    if (sequence !== 0) {
        return sequence;
    }

    return String(left.id || '').localeCompare(String(right.id || ''));
}
