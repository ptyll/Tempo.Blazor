// FZE-10 review fix: the RTE caret bookmark must be a NON-MUTATING Range bookmark (a plain
// {startContainer,startOffset,endContainer,endOffset} record keyed per editor element), not a
// DOM marker span inserted into the edited content — the marker leaked into getHtml()/saved
// Value and collapsed every selection to a caret. richEditor.js now mirrors the Range bookmark
// markdownEditor.js established; both modules are loaded here through `vm` with minimal DOM /
// Selection / Range stubs (no jsdom, per the phase spec).
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const jsRoot = path.join(here, '..');

// ── minimal DOM stubs ────────────────────────────────────────────────────────

class FakeTextNode {
    constructor(text) {
        this.nodeType = 3;
        this.textContent = text;
    }
}

/** An editor surface whose containment is a plain Set, so a test can "replace the content"
    by clearing it and prove restore refuses to fire into detached nodes. */
class FakeEditor {
    constructor({ hasLegacyMarker = false } = {}) {
        this.descendants = new Set([this]); // DOM contains(self) === true
        this.innerHTML = '<p>hello brave world</p>';
        this.focusCalls = 0;
        this.hasLegacyMarker = hasLegacyMarker;
    }
    contains(node) { return node != null && this.descendants.has(node); }
    focus() { this.focusCalls++; }
    add(node) { this.descendants.add(node); }
    clearContent() { this.descendants.clear(); }
    querySelector(selector) {
        if (selector !== '#tm-cursor-marker' || !this.hasLegacyMarker) return null;
        // A leftover legacy marker (content edited with an older build): getHtml must strip it.
        const editor = this;
        return {
            remove() {
                editor.hasLegacyMarker = false;
                editor.innerHTML = '<p>hello world</p>';
            },
        };
    }
}

function fakeRange(startContainer, startOffset, endContainer, endOffset, commonAncestor) {
    return {
        startContainer, startOffset, endContainer, endOffset,
        commonAncestorContainer: commonAncestor ?? startContainer,
    };
}

/** The window.getSelection() stub: hands out the live range and records what the module
    pushes back into it. */
function fakeSelection(initialRange) {
    const selection = {
        rangeCount: initialRange ? 1 : 0,
        _ranges: initialRange ? [initialRange] : [],
        added: [],
        removedAll: 0,
        getRangeAt(index) { return this._ranges[index]; },
        removeAllRanges() { this.removedAll++; },
        addRange(range) { this.added.push(range); },
    };
    return selection;
}

function loadModule(file, selection) {
    const createdRanges = [];
    const sandbox = {
        window: { getSelection: () => selection },
        document: {
            createRange() {
                const range = {
                    setStart(node, offset) { range.startContainer = node; range.startOffset = offset; },
                    setEnd(node, offset) { range.endContainer = node; range.endOffset = offset; },
                };
                createdRanges.push(range);
                return range;
            },
        },
        Node: { TEXT_NODE: 3 },
        NodeFilter: { SHOW_TEXT: 4 },
    };
    const code = readFileSync(path.join(jsRoot, file), 'utf8');
    vm.runInNewContext(code, sandbox, { filename: file });
    return {
        createdRanges,
        rich: sandbox.window.tmRichEditor ?? null,
        markdown: sandbox.window.tmMarkdownEditor ?? null,
    };
}

// ═══ richEditor.js — the Range bookmark ══════════════════════════════════════

test('richEditor.saveCursorPosition outside the element is a no-op (no bookmark, no mutation)', () => {
    const editor = new FakeEditor();
    const elsewhere = new FakeTextNode('elsewhere');
    const selection = fakeSelection(fakeRange(elsewhere, 0, elsewhere, 3, elsewhere));
    const { rich } = loadModule('richEditor.js', selection);

    const saved = rich.saveCursorPosition(editor);

    assert.equal(saved, false, 'a selection outside the editor must not be bookmarked');
    assert.equal(rich.restoreCursorPosition(editor), false, 'nothing was saved, nothing restores');
    assert.equal(editor.focusCalls, 0, 'a failed restore must not steal focus');
    assert.equal(selection.added.length, 0, 'a failed restore must not touch the selection');
});

test('richEditor round-trip restores the FULL non-collapsed range into the element', () => {
    const editor = new FakeEditor();
    const startNode = new FakeTextNode('hello ');
    const endNode = new FakeTextNode('world');
    editor.add(startNode);
    editor.add(endNode);
    const live = fakeRange(startNode, 2, endNode, 4, editor);
    const selection = fakeSelection(live);
    const { rich, createdRanges } = loadModule('richEditor.js', selection);

    assert.equal(rich.saveCursorPosition(editor), true);

    // Simulate the dialog: the selection left the editor.
    selection.rangeCount = 0;
    selection._ranges = [];

    assert.equal(rich.restoreCursorPosition(editor), true);
    assert.equal(createdRanges.length, 1, 'the restore builds exactly one range');
    const replayed = createdRanges[0];
    assert.equal(replayed.startContainer, startNode, 'the FULL range start is replayed');
    assert.equal(replayed.startOffset, 2, 'the FULL range start offset is replayed');
    assert.equal(replayed.endContainer, endNode, 'the FULL range end is replayed — not collapsed to a caret');
    assert.equal(replayed.endOffset, 4, 'the FULL range end offset is replayed');
    assert.ok(editor.focusCalls >= 1, 'the editor is focused before the range is applied');
    assert.equal(selection.added.length, 1, 'the replayed range lands in the selection');
    assert.equal(rich.restoreCursorPosition(editor), false, 'the bookmark is single-use (entry deleted)');
});

test('richEditor restore after the content was replaced returns false and fires nothing', () => {
    const editor = new FakeEditor();
    const startNode = new FakeTextNode('hello ');
    const endNode = new FakeTextNode('world');
    editor.add(startNode);
    editor.add(endNode);
    const selection = fakeSelection(fakeRange(startNode, 0, endNode, 5, editor));
    const { rich, createdRanges } = loadModule('richEditor.js', selection);

    assert.equal(rich.saveCursorPosition(editor), true);

    editor.clearContent(); // e.g. setHtml wiped the DOM the bookmark pointed into

    assert.equal(rich.restoreCursorPosition(editor), false,
        'a bookmark into removed nodes must be dropped, never restored');
    assert.equal(createdRanges.length, 0, 'no range is built from detached nodes');
    assert.equal(editor.focusCalls, 0);
    assert.equal(selection.added.length, 0);
});

test('richEditor bookmarks are per element — one editor cannot restore another one bookmark', () => {
    const editorA = new FakeEditor();
    const editorB = new FakeEditor();
    const nodeA = new FakeTextNode('alpha');
    const nodeB = new FakeTextNode('beta');
    editorA.add(nodeA);
    editorB.add(nodeB);
    const selection = fakeSelection(fakeRange(nodeA, 1, nodeA, 3, editorA));
    const { rich } = loadModule('richEditor.js', selection);

    assert.equal(rich.saveCursorPosition(editorA), true);
    assert.equal(rich.restoreCursorPosition(editorB), false,
        'a bookmark saved for editor A must not replay into editor B');
    assert.equal(rich.restoreCursorPosition(editorA), true);
});

test('richEditor.getHtml strips a legacy #tm-cursor-marker and never returns one', () => {
    const editor = new FakeEditor({ hasLegacyMarker: true });
    editor.innerHTML = '<p>hello <span id="tm-cursor-marker"></span>world</p>';
    const selection = fakeSelection(null);
    const { rich } = loadModule('richEditor.js', selection);

    const html = rich.getHtml(editor);

    assert.equal(html, '<p>hello world</p>');
    assert.ok(!html.includes('tm-cursor-marker'), 'getHtml must never leak the legacy marker');
});

test('richEditor.getHtml without a marker returns the HTML untouched', () => {
    const editor = new FakeEditor();
    const selection = fakeSelection(null);
    const { rich } = loadModule('richEditor.js', selection);

    assert.equal(rich.getHtml(editor), '<p>hello brave world</p>');
    assert.equal(rich.getHtml(null), '');
});

// ═══ markdownEditor.js — the reference implementation stays green ════════════

test('markdownEditor round-trip keeps the full range (the reference contract)', () => {
    const editor = new FakeEditor();
    const startNode = new FakeTextNode('hello ');
    const endNode = new FakeTextNode('world');
    editor.add(startNode);
    editor.add(endNode);
    const selection = fakeSelection(fakeRange(startNode, 1, endNode, 3, editor));
    const { markdown, createdRanges } = loadModule('markdownEditor.js', selection);

    const bookmark = markdown.saveCursorBookmark(editor);
    assert.ok(bookmark, 'the bookmark is returned to the caller');
    assert.equal(bookmark.startContainer, startNode);
    assert.equal(bookmark.endOffset, 3);

    selection.rangeCount = 0;
    selection._ranges = [];
    assert.equal(markdown.restoreCursorBookmark(editor), true);
    assert.equal(createdRanges.length, 1);
    assert.equal(createdRanges[0].startOffset, 1);
    assert.equal(createdRanges[0].endOffset, 3, 'the full non-collapsed range is replayed');
});

test('markdownEditor save outside the element returns null', () => {
    const editor = new FakeEditor();
    const elsewhere = new FakeTextNode('elsewhere');
    const selection = fakeSelection(fakeRange(elsewhere, 0, elsewhere, 2, elsewhere));
    const { markdown } = loadModule('markdownEditor.js', selection);

    assert.equal(markdown.saveCursorBookmark(editor), null);
    assert.equal(markdown.restoreCursorBookmark(editor), false);
});
