// The host pushes the SAME document back after a panel edit (the C# mirror refresh): the push restarts the progressive first layout, and the
// selection controller then resolves the caret against a partial layout - which lands it at the start of the document. These helpers decide
// whether the caret the engine had can be given back. They are asked ONLY when the host says the push is such a mirror refresh
// (`replaceModel(handle, json, { preserveSelection: true })`): an accept / reject, a template, an import or a version preview / restore
// replaces the document on purpose and must start from the engine's own selection.

function walkBlocks(blocks, visit) {
    for (const block of blocks || []) {
        if (visit(block) === true) {
            return true;
        }

        for (const row of block?.content?.table?.rows || []) {
            for (const cell of row?.cells || []) {
                if (walkBlocks(cell?.blocks, visit)) {
                    return true;
                }
            }
        }
    }

    return false;
}

export function findModelBlock(model, blockId) {
    const id = String(blockId || '');
    if (!id) {
        return null;
    }

    let found = null;
    walkBlocks(model?.body?.blocks, block => {
        if (String(block?.id || '') === id) {
            found = block;
            return true;
        }

        return false;
    });
    return found;
}

export function modelHasBlock(model, blockId) {
    return findModelBlock(model, blockId) !== null;
}

// Same offset space as the clipboard controller's clampOffset: a text run counts its characters, any other inline run counts one.
function blockLength(block) {
    const runs = Array.isArray(block?.content?.runs) ? block.content.runs : [];
    return runs.reduce((sum, run) => sum + (String(run?.type || '').toLowerCase() === 'text' ? String(run?.text || '').length : 1), 0);
}

function clampOffset(block, offset) {
    return Math.max(0, Math.min(blockLength(block), Number(offset || 0) || 0));
}

function samePoint(left, right) {
    return String(left?.blockId || '') === String(right?.blockId || '') && Number(left?.offset || 0) === Number(right?.offset || 0);
}

// An object or a math slot is kept only while its id can still be found in its block of the new model.
function blockMentions(block, id) {
    return !!block && !!id && JSON.stringify(block).includes(String(id));
}

function restorableObject(object, nextModel, fallbackBlockId) {
    if (!object) {
        return null;
    }

    const block = findModelBlock(nextModel, object.blockId || fallbackBlockId);
    return blockMentions(block, object.objectId) ? object : null;
}

function restorableMath(math, nextModel, fallbackBlockId) {
    if (!math) {
        return null;
    }

    const block = findModelBlock(nextModel, math.blockId || fallbackBlockId);
    return blockMentions(block, math.mathId) ? math : null;
}

/**
 * @returns the selection to put back (offsets clamped to the new text, an object / math selection dropped when its id is gone), or null when
 * there is nothing to do: the push is not a mirror refresh, a different document, a block that no longer exists, or a selection the refresh
 * left where it was.
 */
export function selectionToRestoreAfterModelRefresh(previousModel, previousSelection, nextModel, selectionAfter, options = {}) {
    if (options?.preserveSelection !== true) {
        return null;
    }

    if (!previousSelection?.anchor || !previousSelection?.focus || !previousModel?.documentId) {
        return null;
    }

    if (String(previousModel.documentId) !== String(nextModel?.documentId || '')) {
        return null;
    }

    const anchorBlock = findModelBlock(nextModel, previousSelection.anchor.blockId);
    const focusBlock = findModelBlock(nextModel, previousSelection.focus.blockId);
    if (!anchorBlock || !focusBlock) {
        return null;
    }

    const restored = {
        ...previousSelection,
        anchor: { ...previousSelection.anchor, offset: clampOffset(anchorBlock, previousSelection.anchor.offset) },
        focus: { ...previousSelection.focus, offset: clampOffset(focusBlock, previousSelection.focus.offset) },
    };
    const object = restorableObject(previousSelection.object, nextModel, previousSelection.focus.blockId);
    const math = restorableMath(previousSelection.math, nextModel, previousSelection.focus.blockId);
    if (object) {
        restored.object = object;
    } else {
        delete restored.object;
    }

    if (math) {
        restored.math = math;
    } else {
        delete restored.math;
    }

    const sameObject = String(selectionAfter?.object?.objectId || '') === String(restored.object?.objectId || '');
    if (sameObject && samePoint(selectionAfter?.anchor, restored.anchor) && samePoint(selectionAfter?.focus, restored.focus)) {
        return null;
    }

    return restored;
}
