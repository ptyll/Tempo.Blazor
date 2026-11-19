// notion-editor.js: the focus hand-back of the floating menus (mention / token / slash / status).
//
// When a menu closes, the block it was opened from gets the focus back. On a mobile sheet the page
// behind the menu is still `inert` while the close handlers run, so a plain focus() is a silent
// no-op and the removed search input would drop the focus on <body>; restoreBlockFocus therefore
// re-asserts for a bounded number of frames — but ONLY while the focus is still homeless (body, a
// detached node, or a control inside THE menu that is closing). A deliberate move onto any other
// real element (another dialog included) is never overridden, and focus() is not even called then.
//
// The DOM here is a tiny fake: just enough of focus / contains / closest / rAF to drive the real
// notion-editor.js through its public entry points (initKeyboardHandler → the trigger input events).
import test from 'node:test';
import assert from 'node:assert/strict';

// ── Fake DOM ─────────────────────────────────────────────────────────────────

const env = { inert: false, frames: [], selected: null, lastInserted: null, blocks: new Map(), chips: [] };

function matchesSimple(el, token) {
    token = token.trim();
    if (token.startsWith('.')) return el.classes.has(token.slice(1));
    const m = /^\[([\w-]+)(?:="([^"]*)")?\]$/.exec(token);
    if (m) return m[2] === undefined ? el.attrs[m[1]] !== undefined : el.attrs[m[1]] === m[2];
    return false;
}

class FakeEl {
    constructor(name, { classes = [], attrs = {}, parent = null, focusable = true } = {}) {
        this.name = name;
        this.classes = new Set(classes);
        this.attrs = { ...attrs };
        this.dataset = {};
        this.parent = parent;
        this.focusable = focusable;
        this.focusCalls = 0;
        this.listeners = {};
        this.detached = false;
    }
    get isConnected() {
        if (this.detached) return false;
        let n = this;
        while (n) {
            if (n === document.body) return true;
            if (n.detached) return false;
            n = n.parent;
        }
        return false;
    }
    get isContentEditable() { return this.attrs.contenteditable === 'true'; }
    contains(other) {
        for (let n = other; n; n = n.parent) if (n === this) return true;
        return false;
    }
    closest(selector) {
        for (let n = this; n; n = n.parent) {
            if (n === document.body) return null;
            if (selector.split(',').some(s => matchesSimple(n, s))) return n;
        }
        return null;
    }
    focus() {
        this.focusCalls++;
        // A focus() against an inert page (or a detached node) is a silent no-op.
        if (env.inert || !this.isConnected || !this.focusable) return;
        document.activeElement = this;
    }
    addEventListener(type, fn) { (this.listeners[type] ??= []).push(fn); }
    dispatchEvent() {}
    removeEventListener(type, fn) { this.listeners[type] = (this.listeners[type] ?? []).filter(f => f !== fn); }
    fire(type, event = {}) { for (const fn of this.listeners[type] ?? []) fn(event); }
    querySelectorAll(selector) { return (this.children ?? []).filter(c => matchesSimple(c, selector)); }
    getBoundingClientRect() { return { top: 0, left: 0, width: 10, height: 10, bottom: 10 }; }
}

let currentText = '';
const fakeRange = {
    startContainer: { nodeType: 1 },
    startOffset: 1,
    cloneRange: () => ({ collapse() {}, getBoundingClientRect: () => ({ width: 1, height: 1, bottom: 0, left: 0 }) }),
};

globalThis.Node = { TEXT_NODE: 3 };
globalThis.NodeFilter = { SHOW_TEXT: 4 };
globalThis.window = {
    addEventListener() {},
    removeEventListener() {},
    getSelection: () => ({ rangeCount: 1, getRangeAt: () => fakeRange, removeAllRanges() {}, addRange(r) { env.selected = r; } }),
};
globalThis.document = {
    body: null,
    activeElement: null,
    addEventListener() {},
    removeEventListener() {},
    querySelectorAll: selector => selector.includes('data-tm-status-editing') ? env.chips.filter(c => c.dataset.tmStatusEditing === 'true') : [],
    // A block is found by its id like the real `[data-block-id="…"] [contenteditable="true"]` lookup; the
    // status chips (opened via openChipEdit) are found by their `data-tm-status-editing` flag.
    querySelector: selector => {
        const id = /data-block-id="([^"]*)"/.exec(selector);
        if (id) return env.blocks.get(id[1]) ?? null;
        if (selector.includes('data-tm-status-editing')) return env.chips.find(c => c.dataset.tmStatusEditing === 'true') ?? null;
        return null;
    },
    createTreeWalker: () => ({ nextNode: () => false }),
    // `template.innerHTML = html` yields one new chip node as `content.lastChild` (an empty html → nothing).
    createElement: tag => {
        assert.equal(tag, 'template');
        const template = { content: { lastChild: null } };
        Object.defineProperty(template, 'innerHTML', {
            set(html) {
                const node = html ? new FakeEl('inserted-chip', { classes: ['tm-notion-status'] }) : null;
                env.lastInserted = node;
                template.content = { lastChild: node };
            },
        });
        return template;
    },
    // A range that can really replace / insert a node: selectNode + deleteContents + insertNode rewire the
    // fake tree (the caret range keeps `startAfter` / `toStart` / `contents` for the assertions).
    createRange: () => {
        const r = {
            selectNodeContents(n) { r.contents = n; r.insParent = n; r.insAtEnd = true; },
            selectNode(n) { r.node = n; r.insParent = n.parent; r.insIndex = (n.parent?.children ?? []).indexOf(n); },
            deleteContents() {
                if (!r.node || !r.node.parent) return;
                const siblings = r.node.parent.children ?? [];
                const at = siblings.indexOf(r.node);
                if (at >= 0) siblings.splice(at, 1);
                r.node.parent = null;
            },
            insertNode(fragment) {
                const node = fragment.lastChild;
                if (!node || !r.insParent) return;
                r.insParent.children ??= [];
                node.parent = r.insParent;
                r.insParent.children.splice(r.insAtEnd ? r.insParent.children.length : r.insIndex, 0, node);
            },
            setEnd() {},
            setStartAfter(n) { r.startAfter = n; },
            collapse(toStart) { r.toStart = toStart; },
            toString: () => currentText,
        };
        return r;
    },
};
globalThis.CSS = { escape: s => s };
document.body = new FakeEl('body', { focusable: false });
document.activeElement = document.body;
globalThis.requestAnimationFrame = cb => { env.frames.push(cb); return env.frames.length; };

const api = await import('../notion-editor.js').then(() => globalThis.window.tmNotionEditor);

/** Runs `n` animation frames (callbacks queued by earlier frames run in the next one). */
function flushFrames(n) {
    for (let i = 0; i < n; i++) {
        const batch = env.frames.splice(0);
        if (batch.length === 0) return;
        batch.forEach(cb => cb());
    }
}

const dotNetRef = { invokeMethodAsync: () => Promise.resolve() };

function makeBlock(id, parent = document.body) {
    const el = new FakeEl(`block-${id}`, {
        classes: ['tm-notion-paragraph'],
        attrs: { contenteditable: 'true', 'data-block-id': id },
        parent,
    });
    el.dataset.blockId = id;
    el.children = [];
    env.blocks.set(id, el);
    api.initKeyboardHandler(el, dotNetRef);
    return el;
}

function makeMenu(menuClass = 'tm-notion-floating-menu') {
    const root = new FakeEl('menu', { classes: [menuClass], parent: document.body, focusable: false });
    const input = new FakeEl('menu-input', { parent: root });
    return { root, input };
}

/** Types the trigger text into the block, exactly as the browser's input event would. */
function type(block, text) {
    currentText = text;
    block.fire('input');
}

function reset() {
    env.inert = false;
    env.frames.length = 0;
    env.lastInserted = null;
    env.chips.length = 0;
    currentText = '';
    api.cancelStatusEdit(); // kills any live focus loop + clears the shared slash / chip state
    env.frames.length = 0;
    document.activeElement = document.body;
}

// The four close paths share one contract; each one is armed by its own trigger text.
const closers = [
    { name: 'mention (cancelMentionTrigger)', trigger: '@', close: () => api.cancelMentionTrigger() },
    { name: 'token (cancelTokenTrigger)', trigger: '{{', close: () => api.cancelTokenTrigger() },
    { name: 'slash (refocusSlashElement)', trigger: '/', close: () => api.refocusSlashElement() },
];

for (const { name, trigger, close } of closers) {
    // (a) Escape from the menu's own search input hands the focus to the block.
    test(`${name}: Escape with the focus in the menu input gives the focus to the block`, () => {
        reset();
        const block = makeBlock('a');
        type(block, trigger);
        const { input } = makeMenu();
        input.focus();
        assert.equal(document.activeElement, input);

        close();

        assert.equal(document.activeElement, block, 'the block the menu was opened from owns the focus again');
    });

    // (b) A deliberate focus on another real element is never touched — focus() is not even called.
    test(`${name}: focus already on another real element → block.focus() is never called`, () => {
        reset();
        const block = makeBlock('b');
        type(block, trigger);
        const { input } = makeMenu();
        input.focus();
        const other = new FakeEl('toolbar-button', { parent: document.body });
        other.focus(); // the user clicked a real control while the menu was closing
        block.focusCalls = 0;

        close();
        flushFrames(60);

        assert.equal(block.focusCalls, 0, 'focus() must never be called on the block');
        assert.equal(document.activeElement, other, 'the deliberate focus stays');
    });
}

// (c) An inert page swallows every focus() — the bounded retry loop must still end.
test('inert page: the retry loop gives up after 40 frames', () => {
    reset();
    const block = makeBlock('c');
    type(block, '@');
    const { input } = makeMenu();
    input.focus();
    env.inert = true; // the mobile sheet has not released the page yet

    api.cancelMentionTrigger();
    assert.equal(document.activeElement, input, 'inert: the focus() was a no-op, the input still owns the focus');
    assert.equal(block.focusCalls, 1, 'one immediate attempt');

    flushFrames(40);
    assert.equal(block.focusCalls, 41, 'the immediate attempt plus 40 retried frames');
    flushFrames(1);
    assert.equal(env.frames.length, 0, 'the loop is finished — nothing is queued any more');

    flushFrames(100);
    assert.equal(block.focusCalls, 41, 'no attempt after the budget');
});

// (d) The focus moved into ANOTHER dialog while the loop was running: it must not be taken back.
test('focus moves into another dialog → no later attempt takes it back', () => {
    reset();
    const block = makeBlock('d');
    type(block, '@');
    const { input } = makeMenu();
    input.focus();
    env.inert = true;

    api.cancelMentionTrigger();
    flushFrames(3);
    const before = block.focusCalls;
    assert.ok(before > 1, 'the loop was running');

    // A different dialog (not the closing menu) takes the focus, and the page is usable again.
    env.inert = false;
    const dialog = new FakeEl('other-dialog', { attrs: { role: 'dialog' }, parent: document.body, focusable: false });
    const dialogButton = new FakeEl('dialog-button', { parent: dialog });
    dialogButton.focus();
    assert.equal(document.activeElement, dialogButton);

    flushFrames(60);

    assert.equal(block.focusCalls, before, 'not a single further attempt');
    assert.equal(document.activeElement, dialogButton, 'the other dialog keeps its focus');
});

test('a floating-menu-like class OUTSIDE the closing menu does not count as homeless', () => {
    reset();
    const block = makeBlock('d2');
    type(block, '@');
    const { input } = makeMenu();
    input.focus();
    env.inert = true;
    api.cancelMentionTrigger();
    flushFrames(2);
    const before = block.focusCalls;

    // ANOTHER floating menu (a different root than the one being closed) owns the focus now.
    env.inert = false;
    const second = makeMenu();
    second.input.focus();
    flushFrames(60);

    assert.equal(block.focusCalls, before, 'only THIS menu root counts as "still on the closing menu"');
    assert.equal(document.activeElement, second.input);
});

// (e) The next menu's focus loop supersedes a restore that is still running.
test('focusMenuInput cancels a running restore loop', () => {
    reset();
    const block = makeBlock('e');
    type(block, '@');
    const first = makeMenu();
    first.input.focus();
    env.inert = true;
    api.cancelMentionTrigger(); // restore loop is now armed against the inert page
    flushFrames(2);

    // The same menu's input re-asserts its focus (the next menu loop) while the restore loop is
    // still pending and the focus still sits inside THAT menu — only a cancelled restore leaves it be.
    env.inert = false;
    api.focusMenuInput(first.input);
    const next = first;
    assert.equal(document.activeElement, next.input);
    const before = block.focusCalls;

    flushFrames(60);

    assert.equal(block.focusCalls, before, 'the dead restore loop never touches the block again');
    assert.equal(document.activeElement, next.input, 'the menu input keeps the focus');
});

// (f) cancelStatusEdit: the edited chip's block wins over the slash element.
function openChipEdit(chipBlock) {
    const chip = new FakeEl('chip', { classes: ['tm-notion-status'], parent: chipBlock });
    chip.dataset.statusColor = 'gray';
    chip.dataset.statusLabel = 'Done';
    chipBlock.children = [chip];
    env.chips.push(chip);
    chipBlock.fire('click', { target: chip, preventDefault() {}, stopPropagation() {} });
    return chip;
}

test('cancelStatusEdit prefers the edited chip\'s block over the slash element', () => {
    reset();
    const slashBlock = makeBlock('slash-block');
    type(slashBlock, '/'); // leaves a connected _slashElement behind
    const chipBlock = makeBlock('chip-block');
    openChipEdit(chipBlock);
    const picker = makeMenu();
    picker.input.focus();
    slashBlock.focusCalls = 0;

    api.cancelStatusEdit();

    assert.equal(document.activeElement, chipBlock, 'the chip\'s own block gets the focus');
    assert.equal(slashBlock.focusCalls, 0, 'the slash element is not touched');
});

test('cancelStatusEdit falls back to the slash element when no chip is being edited', () => {
    reset();
    const slashBlock = makeBlock('slash-only');
    type(slashBlock, '/');
    const picker = makeMenu();
    picker.input.focus();

    api.cancelStatusEdit();

    assert.equal(document.activeElement, slashBlock);
});

test('cancelStatusEdit falls back to the slash element when the edited chip was disconnected', () => {
    reset();
    const slashBlock = makeBlock('slash-fallback');
    type(slashBlock, '/');
    const chipBlock = makeBlock('chip-gone');
    const chip = openChipEdit(chipBlock);
    chip.detached = true;
    const picker = makeMenu();
    picker.input.focus();
    chipBlock.focusCalls = 0;

    api.cancelStatusEdit();

    assert.equal(document.activeElement, slashBlock);
    assert.equal(chipBlock.focusCalls, 0);
});

// (g) A block that lives INSIDE the closing menu root (the focus never left it) must not be
// "restored": there is nothing homeless, so no focus() call and no queued frame.
test('block inside a .tm-drawer that already owns the focus: close → no focus() call, no scheduled frame', () => {
    reset();
    const drawer = new FakeEl('drawer', { classes: ['tm-drawer'], parent: document.body, focusable: false });
    const block = makeBlock('in-drawer', drawer);
    type(block, '@');
    block.focus();
    assert.equal(document.activeElement, block);
    block.focusCalls = 0;
    env.frames.length = 0;

    api.cancelMentionTrigger();

    assert.equal(block.focusCalls, 0, 'the block already owns the focus — focus() must not be called');
    assert.equal(env.frames.length, 0, 'no animation frame may be left scheduled');
    flushFrames(60);
    assert.equal(block.focusCalls, 0);
    assert.equal(document.activeElement, block);
});

test('focus on a descendant of the block: close → the block is left alone', () => {
    reset();
    const drawer = new FakeEl('drawer', { classes: ['tm-drawer'], parent: document.body, focusable: false });
    const block = makeBlock('in-drawer-child', drawer);
    const child = new FakeEl('inline-control', { parent: block });
    block.children = [child];
    type(block, '/');
    child.focus();
    block.focusCalls = 0;
    env.frames.length = 0;

    api.refocusSlashElement();

    assert.equal(block.focusCalls, 0);
    assert.equal(env.frames.length, 0);
    assert.equal(document.activeElement, child);
});

// (h) cancelStatusEdit on a click-to-edit chip puts the caret right AFTER the chip, not at the
// end of the block.
test('cancelStatusEdit: the caret returns right after the edited chip', () => {
    reset();
    const chipBlock = makeBlock('chip-caret');
    const chip = openChipEdit(chipBlock);
    const picker = makeMenu();
    picker.input.focus();
    env.selected = null;

    api.cancelStatusEdit();

    assert.equal(document.activeElement, chipBlock);
    assert.equal(env.selected?.startAfter, chip, 'the range starts after the chip');
    assert.equal(env.selected?.toStart, true, 'the caret is collapsed');
});

test('cancelStatusEdit: the caret retry on an inert page keeps targeting the spot after the chip', () => {
    reset();
    const chipBlock = makeBlock('chip-caret-inert');
    const chip = openChipEdit(chipBlock);
    const picker = makeMenu();
    picker.input.focus();
    env.inert = true;

    api.cancelStatusEdit();
    env.inert = false;
    env.selected = null;
    flushFrames(1);

    assert.equal(document.activeElement, chipBlock);
    assert.equal(env.selected?.startAfter, chip);
});

test('cancelStatusEdit from a slash picker keeps the caret at the end of the block', () => {
    reset();
    const slashBlock = makeBlock('slash-caret');
    type(slashBlock, '/');
    const picker = makeMenu();
    picker.input.focus();
    env.selected = null;

    api.cancelStatusEdit();

    assert.equal(env.selected?.contents, slashBlock);
    assert.equal(env.selected?.toStart, false, 'collapsed to the end');
    assert.equal(env.selected?.startAfter, undefined);
});
// (i) Inserting a status with Enter (replaceActiveStatusChip / insertSlashHtml) closes the picker too:
// the picker input that owned the focus is removed right after, and on a mobile sheet the page behind
// it is still `inert`, so the hand-back must retry through restoreBlockFocus like every other close.
test('insertSlashHtml: Enter-insert with the focus in the picker input gives the focus to the block', () => {
    reset();
    const block = makeBlock('ins-1');
    type(block, '/');
    const picker = makeMenu();
    picker.input.focus();

    api.insertSlashHtml('<span class="tm-notion-status"></span>');

    assert.equal(document.activeElement, block, 'the block the picker was opened from owns the focus again');
    assert.ok(env.lastInserted, 'the chip was really inserted');
    assert.ok(block.contains(env.lastInserted), 'the chip lives in the block');
    assert.equal(env.selected?.startAfter, env.lastInserted, 'the caret sits right behind the inserted chip');
});

test('insertSlashHtml: an inert mobile sheet keeps the focus on the picker → the block is retried until the page is released', () => {
    reset();
    const block = makeBlock('ins-inert');
    type(block, '/');
    const picker = makeMenu('tm-overlay-panel-sheet');
    picker.input.focus();
    env.inert = true;

    api.insertSlashHtml('<span class="tm-notion-status"></span>');
    assert.equal(document.activeElement, picker.input, 'inert: the immediate focus() was a no-op');

    flushFrames(3);
    assert.equal(document.activeElement, picker.input, 'still inert');

    env.inert = false; // the sheet released the page
    env.selected = null;
    flushFrames(1);
    assert.equal(document.activeElement, block, 'the retry loop landed the focus on the block');
    assert.ok(env.lastInserted && block.contains(env.lastInserted), 'the chip was really inserted into the block');
    assert.equal(env.selected?.startAfter, env.lastInserted, 'the retry puts the caret right behind the inserted chip');
});

test('insertSlashHtml: a deliberate focus on another real element is never taken back', () => {
    reset();
    const block = makeBlock('ins-other');
    type(block, '/');
    const picker = makeMenu();
    picker.input.focus();
    env.inert = true;
    api.insertSlashHtml('<span class="tm-notion-status"></span>');
    flushFrames(2);

    env.inert = false;
    const other = new FakeEl('toolbar-button', { parent: document.body });
    other.focus();
    const before = block.focusCalls;
    flushFrames(60);

    assert.equal(block.focusCalls, before);
    assert.equal(document.activeElement, other);
});

test('insertSlashHtml: clears the slash state (a later refocusSlashElement is a no-op)', () => {
    reset();
    const block = makeBlock('ins-state');
    type(block, '/');
    api.insertSlashHtml('<span></span>');
    block.focusCalls = 0;
    document.activeElement = document.body;

    api.refocusSlashElement();

    assert.equal(block.focusCalls, 0);
});

test('replaceActiveStatusChip: Enter-insert with the focus in the picker input gives the focus to the chip\'s block', () => {
    reset();
    const chipBlock = makeBlock('rep-1');
    const oldChip = openChipEdit(chipBlock);
    const picker = makeMenu();
    picker.input.focus();

    api.replaceActiveStatusChip('<span class="tm-notion-status"></span>', '', -1);

    assert.equal(document.activeElement, chipBlock);
    assert.ok(env.lastInserted && chipBlock.contains(env.lastInserted), 'the new chip replaced the old one in the block');
    assert.ok(!chipBlock.contains(oldChip), 'the old chip is gone from the block');
    assert.equal(env.selected?.startAfter, env.lastInserted, 'the caret sits right behind the new chip');
    assert.equal(oldChip.dataset.tmStatusEditing, undefined, 'the editing flag is cleared');

    // The edit is over: a later cancel must not target the old chip (nor put the caret behind it).
    document.activeElement = document.body;
    chipBlock.focusCalls = 0;
    env.selected = null;
    api.cancelStatusEdit();
    assert.equal(chipBlock.focusCalls, 0, 'no stale chip → no focus hand-back to its block');
    assert.equal(env.selected, null);
});

test('replaceActiveStatusChip: an inert mobile sheet → the block is retried until the page is released', () => {
    reset();
    const chipBlock = makeBlock('rep-inert');
    const oldChip = openChipEdit(chipBlock);
    const picker = makeMenu('tm-overlay-panel-sheet');
    picker.input.focus();
    env.inert = true;

    api.replaceActiveStatusChip('<span class="tm-notion-status"></span>', '', -1);
    assert.equal(document.activeElement, picker.input);

    env.inert = false;
    env.selected = null;
    flushFrames(1);
    assert.equal(document.activeElement, chipBlock);
    assert.ok(env.lastInserted && chipBlock.contains(env.lastInserted), 'the new chip replaced the old one');
    assert.equal(env.selected?.startAfter, env.lastInserted, 'the retry puts the caret right behind the new chip');
    assert.equal(oldChip.dataset.tmStatusEditing, undefined, 'the editing flag is cleared');
});

test('replaceActiveStatusChip: a deliberate focus on another real element is never taken back', () => {
    reset();
    const chipBlock = makeBlock('rep-other');
    openChipEdit(chipBlock);
    const picker = makeMenu();
    picker.input.focus();
    env.inert = true;
    api.replaceActiveStatusChip('<span class="tm-notion-status"></span>', '', -1);
    flushFrames(2);

    env.inert = false;
    const other = new FakeEl('toolbar-button', { parent: document.body });
    other.focus();
    const before = chipBlock.focusCalls;
    flushFrames(60);

    assert.equal(chipBlock.focusCalls, before);
    assert.equal(document.activeElement, other);
});

// (j) cancelStatusEdit nulls ALL three slash fields — a leftover anchor would point the next
// slash menu at a stale node.
test('cancelStatusEdit nulls the slash element: a later refocusSlashElement does not touch the old block', () => {
    reset();
    const slashBlock = makeBlock('cancel-nulls');
    type(slashBlock, '/');
    api.cancelStatusEdit();
    slashBlock.focusCalls = 0;
    document.activeElement = document.body;

    api.refocusSlashElement();

    assert.equal(slashBlock.focusCalls, 0, '_slashElement was cleared by cancelStatusEdit');
});

// (k) replaceActiveStatusChip when the chip is already gone (re-rendered away / detached while the
// picker was open): the picker still closes, so the block named by blockId gets the focus back and the
// click-to-edit state is dropped — no early return that leaves the focus on the removed picker input.
test('replaceActiveStatusChip: the chip is no longer connected → the block found by blockId gets the focus, state is cleared', () => {
    reset();
    const chipBlock = makeBlock('rep-gone');
    const oldChip = openChipEdit(chipBlock);
    const picker = makeMenu();
    picker.input.focus();
    oldChip.detached = true; // the block was re-rendered while the picker was open
    chipBlock.children = [];
    chipBlock.focusCalls = 0;
    env.lastInserted = null;

    api.replaceActiveStatusChip('<span class="tm-notion-status"></span>', 'rep-gone', 0);

    assert.equal(document.activeElement, chipBlock, 'the block named by blockId owns the focus');
    assert.equal(env.lastInserted, null, 'nothing was inserted — there was no chip to replace');
    assert.equal(oldChip.dataset.tmStatusEditing, undefined, 'the editing flag is cleared');

    // The stale chip is forgotten: a later cancel has nothing to target.
    document.activeElement = document.body;
    chipBlock.focusCalls = 0;
    api.cancelStatusEdit();
    assert.equal(chipBlock.focusCalls, 0, 'no stale chip reference survives');
});

test('replaceActiveStatusChip: no chip and no block → the state is still cleared and nothing throws', () => {
    reset();
    const chipBlock = makeBlock('rep-none');
    const oldChip = openChipEdit(chipBlock);
    oldChip.detached = true;
    chipBlock.children = [];
    env.blocks.delete('rep-none');

    api.replaceActiveStatusChip('<span class="tm-notion-status"></span>', 'rep-none', 0);

    assert.equal(oldChip.dataset.tmStatusEditing, undefined);
    chipBlock.focusCalls = 0;
    api.cancelStatusEdit();
    assert.equal(chipBlock.focusCalls, 0);
});
