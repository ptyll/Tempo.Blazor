import assert from 'node:assert/strict';
import test from 'node:test';
import { createCanvasCommandRuntime } from '../../commands/dispatcher.mjs';
import { layoutCanvasDocument } from '../../layout/pagination.mjs';
import { buildDisplayList } from '../../render/display-list.mjs';
import { queryImageCommandState } from '../image-commands.mjs';
import {
    OBJECT_CONNECTOR_END_HANDLE_NAME,
    OBJECT_CONNECTOR_START_HANDLE_NAME,
    OBJECT_ROTATE_HANDLE_NAME,
    imageObjectAtPoint,
    objectHandleRects,
    objectInteractionHandleRects,
    resizeRectFromHandle,
    rotationFromPointer,
} from '../object-handles.mjs';

test('canvas image render resolves URLs, captions, alt warnings and inline drawing objects', () => {
    const model = createImageModel();
    const layout = layoutCanvasDocument(model, { fontMetrics: metrics() });
    const display = buildDisplayList(model, layout, { fontMetrics: metrics() });
    const imageCommands = display.commands.filter(command => command.type === 'imageObject');
    // B4: a caption now wraps into one display command per line, so join them to recover the full text.
    const captionText = display.commands.filter(command => command.type === 'imageCaption').map(command => command.text).join(' ').trim();
    const selected = imageObjectAtPoint(layout, imageCommands[0].pageIndex, imageCommands[0].x + 4, imageCommands[0].y + 4);

    assert.equal(imageCommands.length, 2);
    assert.equal(imageCommands[0].url.startsWith('data:image/png;base64,'), true);
    assert.equal(imageCommands.some(command => command.role === 'drawingRun'), true);
    assert.equal(captionText, 'Canvas image caption');
    assert.equal(imageCommands[1].altText, '');
    assert.equal(imageCommands[1].stroke, '#f59e0b');
    assert.equal(selected.objectId, 'phase15-image-block');
    assert.equal(objectHandleRects({ rect: selected.rect }).length, 8);
    assert.equal(objectInteractionHandleRects({ rect: selected.rect }).length, 9);
    assert.equal(objectInteractionHandleRects({ rect: selected.rect }).at(-1).name, OBJECT_ROTATE_HANDLE_NAME);
});

test('object interaction handles expose deterministic rotate angles without changing resize compatibility', () => {
    const rect = { x: 100, y: 120, width: 160, height: 90 };
    const resizeHandles = objectHandleRects({ rect });
    const interactionHandles = objectInteractionHandleRects({ rect });
    const rotate = interactionHandles.find(handle => handle.name === OBJECT_ROTATE_HANDLE_NAME);
    const centerX = rect.x + rect.width / 2;
    const centerY = rect.y + rect.height / 2;

    assert.equal(resizeHandles.length, 8);
    assert.equal(interactionHandles.length, 9);
    assert.ok(rotate.rect.y < rect.y);
    assert.equal(rotationFromPointer(rect, { x: centerX, y: rect.y - 24 }, { x: rect.x + rect.width + 10, y: centerY }, 0, true), 90);
});

test('connector object handles expose endpoint handles without replacing resize and rotate handles', () => {
    const rect = { x: 80, y: 100, width: 220, height: 96 };
    const handles = objectInteractionHandleRects({
        rect,
        object: { kind: 'connector' },
        connector: {
            routing: 'elbow',
            start: { x: 108, y: 148 },
            end: { x: 276, y: 196 },
            points: [
                { x: 108, y: 148 },
                { x: 192, y: 148 },
                { x: 192, y: 196 },
                { x: 276, y: 196 },
            ],
        },
    });

    assert.equal(handles.length, 11);
    assert.equal(handles.some(handle => handle.name === OBJECT_ROTATE_HANDLE_NAME), true);
    assert.equal(handles.some(handle => handle.name === OBJECT_CONNECTOR_START_HANDLE_NAME), true);
    assert.equal(handles.some(handle => handle.name === OBJECT_CONNECTOR_END_HANDLE_NAME), true);
    assert.deepEqual(handles.find(handle => handle.name === OBJECT_CONNECTOR_START_HANDLE_NAME).point, { x: 108, y: 148 });
});

test('connector path hit-test wins over overlapping group bounds', () => {
    const layout = {
        blocks: [
            {
                type: 'image',
                objectId: 'overlapping-group',
                pageIndex: 0,
                sequence: 20,
                rect: { x: 80, y: 100, width: 280, height: 180 },
                object: { kind: 'group', zIndex: 20 },
            },
            {
                type: 'image',
                objectId: 'routed-connector',
                pageIndex: 0,
                sequence: 10,
                rect: { x: 96, y: 120, width: 260, height: 120 },
                object: { kind: 'connector', zIndex: 5 },
                connector: {
                    routing: 'elbow',
                    points: [
                        { x: 116, y: 144 },
                        { x: 220, y: 144 },
                        { x: 220, y: 220 },
                        { x: 340, y: 220 },
                    ],
                },
            },
        ],
    };

    assert.equal(imageObjectAtPoint(layout, 0, 220, 180).objectId, 'routed-connector');
    assert.equal(imageObjectAtPoint(layout, 0, 104, 260).objectId, 'overlapping-group');
});

test('square wrapped images exclude text intervals without overlapping the object', () => {
    const model = createImageModel();
    const layout = layoutCanvasDocument(model, { fontMetrics: metrics() });
    const image = layout.blocks.find(block => block.type === 'image' && block.object?.wrapMode === 'Square');
    const paragraph = layout.blocks.find(block => block.blockId === 'phase15-after-wrap');
    const firstSegment = paragraph.lines[0].segments.find(segment => segment.type !== 'space');

    assert.ok(firstSegment.rect.x >= image.rect.x + image.rect.width + image.object.distanceRight - 0.1);
    assertBlockTextDoesNotOverlapRect(layout, 'phase15-after-wrap', image.captionRect);
});

test('square wrapped image resize keeps the following paragraph below wrapped text', () => {
    const model = createImageModel();
    const image = model.body.blocks.find(block => block.id === 'phase15-image-block').content.image;
    image.layout.position.x = 42;
    image.layout.position.y = 58;
    image.layout.transform.width = 224;
    image.layout.transform.height = 128;
    const followUp = paragraph(
        'phase15-after-followup',
        'Saving the document after a mouse resize and move must preserve readable flow below the wrapped image.');
    followUp.order = 4;
    const drawingBlock = model.body.blocks.find(block => block.id === 'phase15-drawing-block');
    drawingBlock.order = 5;
    model.body.blocks.splice(3, 0, followUp);

    const layout = layoutCanvasDocument(model, { fontMetrics: metrics() });
    const wrappedBottom = blockTextBottom(layout, 'phase15-after-wrap');
    const followUpTop = blockTextTop(layout, 'phase15-after-followup');
    const followUpBottom = blockTextBottom(layout, 'phase15-after-followup');
    const drawingLayout = layout.blocks.find(block => block.blockId === 'phase15-drawing-block');

    assert.ok(followUpTop >= wrappedBottom - 0.01, `follow-up top ${followUpTop} must be below wrapped bottom ${wrappedBottom}`);
    assert.ok(drawingLayout.rect.y >= followUpBottom - 0.01, `inline drawing top ${drawingLayout.rect.y} must be below follow-up bottom ${followUpBottom}`);
    assertBlockTextDoesNotOverlapRect(layout, 'phase15-after-wrap', layout.blocks.find(block => block.blockId === 'phase15-image-block').captionRect);
    assertNoTextOverlap(layout.textRects);
});

test('image command state honors object selection before drawing fallback', () => {
    const model = createImageModel();
    const textSelection = {
        anchor: { blockId: 'phase15-anchor', offset: 0 },
        focus: { blockId: 'phase15-anchor', offset: 0 },
    };
    const objectSelection = {
        ...textSelection,
        object: {
            objectId: 'phase15-image-block',
            blockId: 'phase15-image-block',
            runId: '',
            wrapMode: 'Square',
        },
    };

    assert.equal(queryImageCommandState(model, textSelection).image, null);
    const state = queryImageCommandState(model, objectSelection);
    assert.equal(state.image.objectId, 'phase15-image-block');
    assert.equal(state.image.wrapMode, 'Square');
});

test('image commands insert, resize, move, metadata and z-order through undoable runtime transactions', () => {
    let model = createImageModel();
    let selection = { anchor: { blockId: 'phase15-anchor', offset: 0 }, focus: { blockId: 'phase15-anchor', offset: 0 } };
    const runtime = createCanvasCommandRuntime({
        getModel: () => model,
        getSelection: () => selection,
        commit(change) {
            model = change.model;
            selection = change.selection;
        },
        history: createHistory(),
    });

    assert.equal(runtime.execCommand('insertImage', { id: 'phase15-inserted', url: tinyPng(), width: 96, height: 54, altText: 'Inserted' }).result.changed, true);
    assert.equal(model.body.blocks.some(block => block.id === 'phase15-inserted'), true);
    assert.equal(runtime.execCommand('updateImageLayout', { objectId: 'phase15-image-block', x: 48, y: 32, width: 180, height: 102 }).result.changed, true);
    let image = model.body.blocks.find(block => block.id === 'phase15-image-block').content.image;
    assert.equal(image.layout.transform.width, 180);
    assert.equal(image.layout.position.x, 48);
    assert.equal(runtime.execCommand('setImageMetadata', { objectId: 'phase15-image-block', altText: 'Updated alt', caption: 'Updated caption' }).result.changed, true);
    image = model.body.blocks.find(block => block.id === 'phase15-image-block').content.image;
    assert.equal(image.altText, 'Updated alt');
    assert.equal(runtime.execCommand('bringImageForward', { objectId: 'phase15-image-block' }).result.changed, true);
    image = model.body.blocks.find(block => block.id === 'phase15-image-block').content.image;
    assert.equal(image.layout.stacking.zIndex, 2);
    assert.equal(runtime.execCommand('undo').result.changed, true);
    image = model.body.blocks.find(block => block.id === 'phase15-image-block').content.image;
    assert.equal(image.layout.stacking.zIndex, 1);

    const resized = resizeRectFromHandle({ x: 10, y: 10, width: 100, height: 50 }, 'se', 50, 4, true);
    assert.equal(Math.round(resized.width / resized.height * 10) / 10, 2);
});

test('image commands support provider assets and inspector command aliases', () => {
    let model = createImageModel();
    model.assets = [{
        id: 'provider-asset-1',
        url: tinyPng(),
        altText: 'Provider asset alt',
        caption: 'Provider asset caption',
    }];
    let selection = { anchor: { blockId: 'phase15-anchor', offset: 0 }, focus: { blockId: 'phase15-anchor', offset: 0 } };
    const runtime = createCanvasCommandRuntime({
        getModel: () => model,
        getSelection: () => selection,
        commit(change) {
            model = change.model;
            selection = change.selection;
        },
        history: createHistory(),
    });

    const insert = runtime.execCommand('insertImage', {
        id: 'phase15-provider-image',
        assetId: 'provider-asset-1',
        width: 144,
        height: 81,
        wrapMode: 'Square',
    });
    assert.equal(insert.result.changed, true);
    let image = model.body.blocks.find(block => block.id === 'phase15-provider-image').content.image;
    assert.equal(image.source, 1);
    assert.equal(image.assetId, 'provider-asset-1');
    assert.equal(image.layout.anchor.blockId, 'phase15-anchor');

    const display = buildDisplayList(model, layoutCanvasDocument(model, { fontMetrics: metrics() }), { fontMetrics: metrics() });
    const rendered = display.commands.find(command => command.objectId === 'phase15-provider-image');
    assert.equal(rendered.url, tinyPng());

    assert.equal(runtime.execCommand('setImageWrapMode', { objectId: 'phase15-provider-image', wrapMode: 'Tight' }).result.changed, true);
    assert.equal(runtime.execCommand('setImageSize', { objectId: 'phase15-provider-image', width: 180, height: 100 }).result.changed, true);
    assert.equal(runtime.execCommand('setImageObjectPosition', { objectId: 'phase15-provider-image', x: 24, y: 44 }).result.changed, true);
    assert.equal(runtime.execCommand('setImageUrl', { objectId: 'phase15-provider-image', url: 'https://example.test/image.png' }).result.changed, true);
    assert.equal(runtime.execCommand('setImageDecorative', { objectId: 'phase15-provider-image', isDecorative: true }).result.changed, true);
    assert.equal(runtime.execCommand('toggleImageCaption', { objectId: 'phase15-provider-image', caption: 'Caption' }).result.changed, true);
    assert.equal(runtime.execCommand('setImageZOrder', { objectId: 'phase15-provider-image', direction: 'Backward' }).result.changed, true);
    image = model.body.blocks.find(block => block.id === 'phase15-provider-image').content.image;
    assert.equal(image.layout.wrap.mode, 2);
    assert.equal(image.layout.transform.width, 180);
    assert.equal(image.layout.position.x, 24);
    assert.equal(image.url, 'https://example.test/image.png');
    assert.equal(image.isDecorative, true);
    assert.equal(image.caption, 'Caption');
    assert.equal(image.layout.stacking.zIndex, -1);
});

test('setImageAnchorMode with the inspector PascalCase payload flips the anchor lock (canvas inspector parity)', () => {
    let model = createImageModel();
    let selection = { anchor: { blockId: 'phase15-anchor', offset: 0 }, focus: { blockId: 'phase15-anchor', offset: 0 } };
    const runtime = createCanvasCommandRuntime({
        getModel: () => model,
        getSelection: () => selection,
        commit(change) {
            model = change.model;
            selection = change.selection;
        },
        history: createHistory(),
    });
    runtime.execCommand('insertImage', { id: 'lock-image', url: tinyPng(), width: 120, height: 80, wrapMode: 'Square' });
    const read = () => model.body.blocks.find(block => block.id === 'lock-image').content.image.layout.anchor.lockAnchor;
    assert.notEqual(read(), true);

    // The Blazor inspector (TmDocumentEditor.SetActiveImageLockAnchorFromPanelAsync) sends exactly this shape.
    const locked = runtime.execCommand('setImageAnchorMode', { ObjectId: 'lock-image', BlockId: 'lock-image', RunId: '', LockAnchor: true });
    assert.equal(locked.result.changed, true);
    assert.equal(read(), true);

    const unlocked = runtime.execCommand('setImageAnchorMode', { ObjectId: 'lock-image', LockAnchor: false });
    assert.equal(unlocked.result.changed, true);
    assert.equal(read(), false);
});

test('image wrap modes choose the expected text exclusion and paint layers', () => {
    const expected = [
        ['Inline', false, 'objects'],
        ['Square', true, 'objects'],
        ['Tight', true, 'objects'],
        ['Through', true, 'objects'],
        ['TopBottom', false, 'objects'],
        ['BehindText', false, 'page-background'],
        ['InFrontOfText', false, 'objects'],
    ];

    for (const [mode, excludesText, layer] of expected) {
        const model = createImageModel();
        const image = model.body.blocks.find(block => block.id === 'phase15-image-block').content.image;
        image.layout.wrap.mode = mode;
        image.layout.kind = mode === 'Inline' ? 0 : 1;
        const layout = layoutCanvasDocument(model, { fontMetrics: metrics() });
        const display = buildDisplayList(model, layout, { fontMetrics: metrics() });
        const command = display.commands.find(item => item.objectId === 'phase15-image-block' && item.type === 'imageObject');
        const paragraph = layout.blocks.find(block => block.blockId === 'phase15-after-wrap');
        const firstSegment = paragraph.lines[0].segments.find(segment => segment.type !== 'space');

        assert.equal(command.layer, layer, mode);
        assert.equal(firstSegment.rect.x > command.x + command.width, excludesText, mode);
    }
});

function createImageModel() {
    return {
        documentId: 'phase15-image-render-test',
        pageSettings: { width: 720, height: 960, marginTop: 72, marginRight: 72, marginBottom: 72, marginLeft: 72 },
        body: {
            blocks: [
                paragraph('phase15-anchor', 'Anchor paragraph before the image.'),
                {
                    id: 'phase15-image-block',
                    type: 'image',
                    order: 2,
                    content: {
                        type: 'image',
                        image: {
                            objectId: 'phase15-image-block',
                            source: 0,
                            url: tinyPng(),
                            altText: 'Canvas image',
                            caption: 'Canvas image caption',
                            size: { width: 120, height: 72, lockAspectRatio: true },
                            naturalSize: { width: 120, height: 72, lockAspectRatio: true },
                            alignment: 1,
                            layout: {
                                kind: 1,
                                position: { x: 0, y: 0 },
                                wrap: { mode: 1, distanceLeft: 12, distanceRight: 12, distanceTop: 8, distanceBottom: 8 },
                                transform: { width: 120, height: 72, lockAspectRatio: true },
                                stacking: { zIndex: 1 },
                            },
                        },
                    },
                },
                paragraph('phase15-after-wrap', 'Wrapped text should start to the right of the square image and continue without crossing the object footprint.'),
                {
                    id: 'phase15-drawing-block',
                    type: 'paragraph',
                    order: 4,
                    content: {
                        type: 'paragraph',
                        runs: [{
                            id: 'phase15-drawing-run',
                            type: 'drawing',
                            drawing: {
                                objectId: 'phase15-inline-drawing',
                                source: 0,
                                url: tinyPng(),
                                altText: '',
                                caption: '',
                                size: { width: 90, height: 54, lockAspectRatio: true },
                                naturalSize: { width: 90, height: 54, lockAspectRatio: true },
                                layout: {
                                    kind: 0,
                                    position: { x: 0, y: 0 },
                                    wrap: { mode: 0 },
                                    transform: { width: 90, height: 54, lockAspectRatio: true },
                                    stacking: { zIndex: 0 },
                                },
                            },
                        }],
                    },
                },
            ],
        },
    };
}

function paragraph(id, text) {
    const order = id === 'phase15-anchor' ? 1 : (id === 'phase15-after-wrap' ? 3 : Number(id.match(/\d+$/)?.[0] || 1));
    return {
        id,
        type: 'paragraph',
        order,
        paragraphProperties: {},
        content: { type: 'paragraph', runs: [{ id: `${id}-run`, type: 'text', text, marks: [] }] },
    };
}

function tinyPng() {
    return 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=';
}

function metrics() {
    return {
        measureRun(request) {
            const fontSize = Number(request.fontSize) || 16;
            const text = String(request.text || '');
            return {
                width: Math.max(1, text.length * fontSize * 0.5),
                ascent: fontSize * 0.8,
                descent: fontSize * 0.2,
                lineHeight: Math.ceil(fontSize * 1.25),
            };
        },
    };
}

function blockTextTop(layout, blockId) {
    return Math.min(...layout.textRects
        .filter(item => item.blockId === blockId)
        .map(item => Number(item.rect?.y || 0) || 0));
}

function blockTextBottom(layout, blockId) {
    return Math.max(...layout.textRects
        .filter(item => item.blockId === blockId)
        .map(item => (Number(item.rect?.y || 0) || 0) + (Number(item.rect?.height || 0) || 0)));
}

function assertNoTextOverlap(rects) {
    const ordered = rects
        .map(item => ({ id: item.id, blockId: item.blockId, pageIndex: item.pageIndex, ...item.rect }))
        .filter(rect => rect.width > 0 && rect.height > 0);
    for (let i = 0; i < ordered.length; i++) {
        for (let j = i + 1; j < ordered.length; j++) {
            const left = ordered[i];
            const right = ordered[j];
            if (left.pageIndex !== right.pageIndex) {
                continue;
            }

            const overlapX = Math.min(left.x + left.width, right.x + right.width) - Math.max(left.x, right.x);
            const overlapY = Math.min(left.y + left.height, right.y + right.height) - Math.max(left.y, right.y);
            assert.ok(overlapX <= 0.01 || overlapY <= 0.01, `${left.id} (${left.blockId}) overlaps ${right.id} (${right.blockId})`);
        }
    }
}

function assertBlockTextDoesNotOverlapRect(layout, blockId, rect) {
    assert.ok(rect, 'caption rect must exist for overlap assertion');
    for (const item of layout.textRects.filter(candidate => candidate.blockId === blockId)) {
        const textRect = item.rect || {};
        const overlapX = Math.min(
            Number(textRect.x || 0) + Number(textRect.width || 0),
            Number(rect.x || 0) + Number(rect.width || 0))
            - Math.max(Number(textRect.x || 0), Number(rect.x || 0));
        const overlapY = Math.min(
            Number(textRect.y || 0) + Number(textRect.height || 0),
            Number(rect.y || 0) + Number(rect.height || 0))
            - Math.max(Number(textRect.y || 0), Number(rect.y || 0));
        assert.ok(overlapX <= 0.01 || overlapY <= 0.01, `${item.id} (${blockId}) overlaps image caption`);
    }
}

function createHistory() {
    const undo = [];
    const redo = [];
    return {
        push(transaction) {
            undo.push(transaction);
            redo.length = 0;
        },
        undo() {
            const transaction = undo.pop();
            if (transaction) {
                redo.push(transaction);
            }

            return transaction || null;
        },
        redo() {
            const transaction = redo.pop();
            if (transaction) {
                undo.push(transaction);
            }

            return transaction || null;
        },
        snapshot() {
            return { undoDepth: undo.length, redoDepth: redo.length };
        },
    };
}
