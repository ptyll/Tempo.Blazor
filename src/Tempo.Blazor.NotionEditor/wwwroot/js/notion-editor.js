/**
 * Tempo Blazor — Notion Editor JS Interop
 *
 * Sections:
 *   26.1  Block lifecycle      — initBlock / destroyBlock / getHtml / setHtml / focus*
 *   26.2  Selection/format     — getSelectionRange / getSelectionRect / applyFormat / …
 *   26.3  Drag & drop          — initDragDrop / destroyDragDrop  (ghost + drop-indicator)
 *   26.4  Slash menu           — getCaretCoords / getTextBeforeCaret
 *   26.5  Keyboard handler     — initKeyboardHandler  (Enter / Backspace / Tab / markdown)
 *   26.6  Inline math          — renderEquation / renderInlineMath  (KaTeX or fallback)
 *   26.7  Clipboard            — handlePaste / copyBlocksToClipboard / initBlockPaste / destroyBlockPaste
 *   26.8  Resize handle        — initResizeHandle / destroyResizeHandle
 *   26.9  Scroll / nav         — scrollToBlock / initSmoothScrollSpy / destroyScrollSpy
 *   30.1  Cover drag           — startCoverDrag
 */
window.tmNotionEditor = (function () {
    'use strict';

    // ── Internal registries ────────────────────────────────────────────────────
    const _blocks          = new WeakMap(); // element → { dotNetRef, listeners: [] }
    const _dragContainers  = new WeakMap();
    const _resizeHandles   = new WeakMap();
    const _scrollSpies     = new WeakMap();
    const _columnResizers  = new WeakMap();
    let   _pageDotNetRef     = null; // TmNotionPage DotNet ref for comment mark clicks

    // ── Slash menu state ───────────────────────────────────────────────────────
    let _slashElement    = null; // contenteditable that triggered the slash menu
    let _slashAnchorNode = null; // text node position just before the '/'
    let _slashAnchorOff  = 0;

    // ── Mention / page-link menu state ─────────────────────────────────────────
    let _mentionElement    = null; // contenteditable that triggered the mention
    let _mentionAnchorNode = null; // text node position just before the trigger
    let _mentionAnchorOff  = 0;
    let _mentionTriggerLen = 1;    // 1 for '@', 2 for '[['

    // ── Token menu state ────────────────────────────────────────────────────────
    let _tokenElement    = null; // contenteditable that triggered the token menu
    let _tokenAnchorNode = null; // text node position just before '{{'
    let _tokenAnchorOff  = 0;
    let _chipBeingEdited = null; // chip span being replaced via click-to-edit
    let _statusChipBeingEdited = null; // inline status chip being replaced via click-to-edit

    // ── Shared helpers ─────────────────────────────────────────────────────────

    function _on(el, type, fn, opts) {
        el.addEventListener(type, fn, opts);
        return { el, type, fn, opts };
    }

    function _offAll(list) {
        for (const l of list) l.el.removeEventListener(l.type, l.fn, l.opts);
        list.length = 0;
    }

    function _range() {
        const sel = window.getSelection();
        return sel && sel.rangeCount > 0 ? sel.getRangeAt(0) : null;
    }

    function _applyRange(r) {
        const sel = window.getSelection();
        sel.removeAllRanges();
        sel.addRange(r);
    }

    function _setCursorAtEnd(el) {
        el.focus();
        const r = document.createRange();
        r.selectNodeContents(el);
        r.collapse(false);
        _applyRange(r);
    }

    function _setCursorAtStart(el) {
        el.focus();
        const r = document.createRange();
        r.setStart(el, 0);
        r.collapse(true);
        _applyRange(r);
    }

    function _setCursorAtOffset(el, offset) {
        el.focus();
        const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
        let rem = offset;
        let node;
        while ((node = walker.nextNode())) {
            if (rem <= node.length) {
                const r = document.createRange();
                r.setStart(node, rem);
                r.collapse(true);
                _applyRange(r);
                return;
            }
            rem -= node.length;
        }
        _setCursorAtEnd(el);
    }

    function _isEmpty(el) {
        return !el.textContent.trim() && !el.querySelector('img');
    }

    /** True when the caret sits before every character of the element and nothing is selected. */
    function _caretAtStart(el) {
        const selection = window.getSelection();
        if (!selection || selection.rangeCount === 0 || !selection.isCollapsed) return false;

        const range = selection.getRangeAt(0);
        if (!el.contains(range.startContainer)) return false;

        const probe = range.cloneRange();
        probe.selectNodeContents(el);
        probe.setEnd(range.startContainer, range.startOffset);
        return probe.toString().length === 0;
    }

    function _escHtml(s) {
        return String(s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function _truncateInlineChipText(value, maxLength = 56) {
        const text = String(value ?? '').trim();
        const chars = Array.from(text);
        if (chars.length <= maxLength) return text;
        return chars.slice(0, Math.max(1, maxLength - 1)).join('').trimEnd() + '\u2026';
    }

    /**
     * Block-level markdown shortcuts, ordered most-specific first so that "- [x] " never
     * matches the bullet "- ". Keep in sync with MarkdownShortcutDetector.cs.
     */
    const _BLOCK_SHORTCUTS = [
        [/^### $/,        'heading3'],
        [/^## $/,         'heading2'],
        [/^# $/,          'heading1'],
        [/^- \[[xX]\] $/, 'todoDone'],
        [/^- \[ ?\] $/,   'todo'],
        [/^\[[xX]\] $/,   'todoDone'],
        [/^\[ ?\] $/,     'todo'],
        [/^[*\-+] $/,     'bullet'],
        [/^\d+\. $/,      'numbered'],
        [/^> $/,          'quote'],
    ];

    /** Returns { key, triggerLength } or null. */
    function _detectMarkdownShortcut(text) {
        for (const [pattern, key] of _BLOCK_SHORTCUTS) {
            if (pattern.test(text)) return { key, triggerLength: text.length };
        }
        const trimmed = text.trim();
        if (/^```$/.test(trimmed)) return { key: 'code', triggerLength: text.length };
        if (/^---$/.test(trimmed)) return { key: 'divider', triggerLength: text.length };
        return null;
    }

    /** Inline markdown applied as soon as its closing delimiter is typed. */
    const _INLINE_SHORTCUTS = [
        [/\*\*([^*\s](?:[^*]*[^*\s])?)\*\*$/, 'strong'],
        [/~~([^~\s](?:[^~]*[^~\s])?)~~$/,     's'],
        [/`([^`\s](?:[^`]*[^`\s])?)`$/,       'code'],
    ];

    /** Deletes `count` characters immediately before the caret, leaving the rest of the block alone. */
    function _deleteBeforeCaret(element, count) {
        if (count <= 0) return;
        const selection = window.getSelection();
        if (!selection || selection.rangeCount === 0) return;

        const caret = selection.getRangeAt(0);
        const range = document.createRange();
        range.selectNodeContents(element);
        range.setEnd(caret.startContainer, caret.startOffset);

        // Walk back `count` plain-text characters from the caret.
        const text = range.toString();
        const keep = Math.max(0, text.length - count);

        const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
        let remaining = keep;
        let startNode = element;
        let startOffset = 0;
        let node = walker.nextNode();
        while (node) {
            if (remaining <= node.textContent.length) {
                startNode = node;
                startOffset = remaining;
                break;
            }
            remaining -= node.textContent.length;
            node = walker.nextNode();
        }

        const kill = document.createRange();
        kill.setStart(startNode, startOffset);
        kill.setEnd(caret.startContainer, caret.startOffset);
        kill.deleteContents();

        selection.removeAllRanges();
        const collapsed = document.createRange();
        collapsed.setStart(startNode, Math.min(startOffset, startNode.textContent?.length ?? 0));
        collapsed.collapse(true);
        selection.addRange(collapsed);
    }

    /**
     * Replaces a just-closed inline pattern (**bold**, ~~strike~~, `code`) with its element.
     * Returns true when something was replaced.
     */
    function _applyInlineShortcut(element) {
        const selection = window.getSelection();
        if (!selection || selection.rangeCount === 0) return false;

        const caret = selection.getRangeAt(0);
        const node = caret.startContainer;
        if (node.nodeType !== Node.TEXT_NODE) return false;

        // Only look at the text node holding the caret, so we never reformat across existing markup.
        const before = node.textContent.slice(0, caret.startOffset);

        for (const [pattern, tag] of _INLINE_SHORTCUTS) {
            const match = pattern.exec(before);
            if (!match) continue;

            const start = caret.startOffset - match[0].length;
            const range = document.createRange();
            range.setStart(node, start);
            range.setEnd(node, caret.startOffset);
            range.deleteContents();

            const wrapper = document.createElement(tag);
            wrapper.textContent = match[1];
            range.insertNode(wrapper);

            // Park the caret after the new element so typing continues unformatted.
            const spacer = document.createTextNode('\u200B');
            wrapper.after(spacer);
            selection.removeAllRanges();
            const after = document.createRange();
            after.setStart(spacer, 1);
            after.collapse(true);
            selection.addRange(after);

            element.dispatchEvent(new Event('input', { bubbles: true }));
            return true;
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 26.1 — Block lifecycle
    // ═══════════════════════════════════════════════════════════════════════════

    function initBlock(element, dotNetRef) {
        if (!element) return;
        if (_blocks.has(element)) destroyBlock(element);
        _blocks.set(element, { dotNetRef, listeners: [] });
    }

    function destroyBlock(element) {
        if (!element || !_blocks.has(element)) return;
        const s = _blocks.get(element);
        _offAll(s.listeners);
        _blocks.delete(element);
    }

    function getHtml(element) {
        return element ? element.innerHTML : '';
    }

    /**
     * Reads the live contenteditable value of a block by its id.
     * Conversions triggered from the toolbar or the slash menu have no element
     * reference, so they look the editable up through the block's data-block-id.
     * Returns null when the block has no editable (image, divider, table…).
     */
    function getEditableHtml(blockId) {
        if (!blockId) return null;
        const block = document.querySelector(`[data-block-id="${CSS.escape(blockId)}"]`);
        if (!block) return null;

        // A container block (toggle, column, table) nests other blocks, each with its own
        // editable. Only the editable that belongs to THIS block may be read.
        const editable = Array
            .from(block.querySelectorAll('[contenteditable="true"]'))
            .find(el => el.closest('[data-block-id]') === block);

        return editable ? editable.innerHTML : null;
    }

    /** The block's own editable element, or null. Shared by the caret helpers below. */
    function _ownEditable(blockId) {
        if (!blockId) return null;
        const block = document.querySelector(`[data-block-id="${CSS.escape(blockId)}"]`);
        if (!block) return null;
        return Array
            .from(block.querySelectorAll('[contenteditable="true"]'))
            .find(el => el.closest('[data-block-id]') === block) ?? null;
    }

    /** Caret offset in plain-text characters from the start of the block's editable. */
    function getCaretOffset(blockId) {
        const editable = _ownEditable(blockId);
        if (!editable) return null;

        const selection = window.getSelection();
        if (!selection || selection.rangeCount === 0) return null;

        const range = selection.getRangeAt(0);
        if (!editable.contains(range.startContainer)) return null;

        const probe = range.cloneRange();
        probe.selectNodeContents(editable);
        probe.setEnd(range.startContainer, range.startOffset);
        return probe.toString().length;
    }

    /** Places the caret at a plain-text offset, clamped to the block's text length. */
    function setCaretOffset(blockId, offset) {
        const editable = _ownEditable(blockId);
        if (!editable) return false;

        editable.focus();

        const walker = document.createTreeWalker(editable, NodeFilter.SHOW_TEXT);
        let remaining = Math.max(0, offset ?? 0);
        let node = walker.nextNode();
        let target = null;
        let targetOffset = 0;

        while (node) {
            if (remaining <= node.textContent.length) {
                target = node;
                targetOffset = remaining;
                break;
            }
            remaining -= node.textContent.length;
            node = walker.nextNode();
        }

        const range = document.createRange();
        if (target) {
            range.setStart(target, targetOffset);
        } else {
            // Empty block, or offset past the end — collapse to the end of the content.
            range.selectNodeContents(editable);
            range.collapse(false);
        }
        range.collapse(true);

        const selection = window.getSelection();
        selection.removeAllRanges();
        selection.addRange(range);
        return true;
    }

    function setHtml(element, html) {
        if (element) element.innerHTML = html ?? '';
    }

    function focus(element) {
        element?.focus();
    }

    function initEditorKeyHandler(element, dotNetRef) {
        if (!element || !dotNetRef) return;
        destroyEditorKeyHandler(element);

        const onKeyDown = (event) => {
            if (event.key !== 'Escape') return;
            const mode = element.getAttribute('data-view-mode');
            if (mode !== 'Reading' && mode !== 'Presentation') return;

            dotNetRef.invokeMethodAsync('OnEditorEscapeAsync').catch(console.error);
        };

        document.addEventListener('keydown', onKeyDown, true);
        element.__tmNotionEditorKeyHandler = { onKeyDown };
    }

    function destroyEditorKeyHandler(element) {
        const state = element?.__tmNotionEditorKeyHandler;
        if (!state) return;

        document.removeEventListener('keydown', state.onKeyDown, true);
        delete element.__tmNotionEditorKeyHandler;
    }

    function focusAtEnd(element) {
        if (element) _setCursorAtEnd(element);
    }

    function focusAtStart(element) {
        if (element) _setCursorAtStart(element);
    }

    function focusAtOffset(element, offset) {
        if (element) _setCursorAtOffset(element, offset);
    }

    const _focusTraps = new WeakMap();

    function _getFocusableElements(container) {
        if (!container) return [];
        return Array.from(container.querySelectorAll([
            'a[href]',
            'button:not([disabled])',
            'input:not([disabled])',
            'select:not([disabled])',
            'textarea:not([disabled])',
            '[tabindex]:not([tabindex="-1"])'
        ].join(','))).filter(el => {
            const style = window.getComputedStyle(el);
            return style.visibility !== 'hidden' && style.display !== 'none';
        });
    }

    function initFocusTrap(container) {
        if (!container) return;
        destroyFocusTrap(container);

        const onKeyDown = (event) => {
            if (event.key !== 'Tab') return;

            const focusable = _getFocusableElements(container);
            if (focusable.length === 0) {
                event.preventDefault();
                container.focus({ preventScroll: true });
                return;
            }

            const currentIndex = focusable.indexOf(document.activeElement);
            const nextIndex = event.shiftKey
                ? (currentIndex <= 0 ? focusable.length - 1 : currentIndex - 1)
                : (currentIndex < 0 || currentIndex >= focusable.length - 1 ? 0 : currentIndex + 1);

            event.preventDefault();
            focusable[nextIndex].focus({ preventScroll: true });
        };

        container.addEventListener('keydown', onKeyDown);
        _focusTraps.set(container, onKeyDown);

        const initialFocus = _getFocusableElements(container)[0] || container;
        initialFocus.focus({ preventScroll: true });
    }

    function destroyFocusTrap(container) {
        const onKeyDown = _focusTraps.get(container);
        if (!container || !onKeyDown) return;
        container.removeEventListener('keydown', onKeyDown);
        _focusTraps.delete(container);
    }

    // APG menu-button pattern: when a submenu trigger opens its panel (ArrowRight,
    // Enter), focus lands on the panel's first item — an aria-expanded flip alone
    // leaves the user outside the submenu they just opened.
    function focusFirstMenuItem(panel) {
        if (!panel) return;
        const first = panel.querySelector('button:not([disabled])');
        if (first) first.focus({ preventScroll: true });
    }

    // Positions a fixed overlay menu at its anchor's viewport rect. The menu is
    // position:fixed (see .tm-notion-ctx) so that scrolling the notion-main column
    // cannot drag it out from under the pointer — an absolute menu inside the
    // scroll container gets pulled away during scroll-reveal actions, which fires
    // a phantom mouseleave that collapses open submenus mid-interaction.
    function positionContextMenu(menuEl) {
        if (!menuEl) return;
        const anchor = menuEl.parentElement;
        if (anchor) {
            const r  = anchor.getBoundingClientRect();
            const vw = document.documentElement.clientWidth;
            const vh = document.documentElement.clientHeight;
            const mw = menuEl.offsetWidth;
            const mh = menuEl.offsetHeight;
            let left = r.left;
            let top  = r.bottom + 4;
            if (left + mw > vw - 8) left = Math.max(8, vw - mw - 8);
            if (top + mh > vh - 8 && r.top - mh - 4 >= 8) top = r.top - mh - 4;
            menuEl.style.top  = top + 'px';
            menuEl.style.left = left + 'px';
        }
        menuEl.classList.add('tm-notion-ctx--positioned');
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 26.2 — Selection & formatting
    // ═══════════════════════════════════════════════════════════════════════════

    function getSelectionRange() {
        const sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return null;
        const r = sel.getRangeAt(0);
        const anchor = r.startContainer.nodeType === Node.TEXT_NODE
            ? r.startContainer.parentElement
            : r.startContainer;
        const blockElement = anchor?.closest?.('[data-notion-block]') ?? null;
        return {
            blockElement,
            startOffset: r.startOffset,
            endOffset: r.endOffset,
            text: sel.toString()
        };
    }

    function getSelectionRect() {
        const r = _range();
        if (!r) return null;
        const rect = r.getBoundingClientRect();
        return { top: rect.top, left: rect.left, width: rect.width, height: rect.height };
    }

    function applyFormat(command, value, blockId) {
        if (command === 'unlink') {
            if (_savedRange) {
                _applyRange(_savedRange);
            }

            let changedEditable = null;
            const anchorsToUnwrap = new Set();
            const elementFromNode = (node) =>
                node?.nodeType === Node.TEXT_NODE ? node.parentElement :
                node?.nodeType === Node.ELEMENT_NODE ? node : null;

            const addClosestAnchor = (node) => {
                const anchor = elementFromNode(node)?.closest?.('a');
                if (anchor) anchorsToUnwrap.add(anchor);
            };

            const addIntersectingAnchors = (range, root) => {
                if (!range || !root) return;
                for (const anchor of root.querySelectorAll('a')) {
                    try {
                        if (range.intersectsNode(anchor)) {
                            anchorsToUnwrap.add(anchor);
                        }
                    } catch {
                        anchorsToUnwrap.add(anchor);
                    }
                }
            };

            const sel = window.getSelection();
            if (sel && sel.rangeCount > 0) {
                const range = sel.getRangeAt(0);
                let container = range.commonAncestorContainer;
                if (container.nodeType === Node.TEXT_NODE) {
                    container = container.parentElement;
                }

                addClosestAnchor(range.startContainer);
                addClosestAnchor(range.endContainer);
                addClosestAnchor(container);

                const candidateRoot = elementFromNode(container);
                if (candidateRoot) {
                    changedEditable = candidateRoot.closest?.('[contenteditable="true"], .tm-notion-editable') ?? null;
                    addIntersectingAnchors(range, candidateRoot);
                }
            }

            const savedRoot = _savedRange
                ? elementFromNode(_savedRange.commonAncestorContainer)?.closest?.('[contenteditable="true"], .tm-notion-editable') ?? null
                : null;
            if (savedRoot) {
                changedEditable ??= savedRoot;
                addClosestAnchor(_savedRange.startContainer);
                addClosestAnchor(_savedRange.endContainer);
                addIntersectingAnchors(_savedRange, savedRoot);
            }

            if (anchorsToUnwrap.size === 0 && value) {
                const targetHref = String(value);
                const normalizeHref = (href) => {
                    const probe = document.createElement('a');
                    probe.href = href;
                    return probe.href;
                };
                const normalizedTargetHref = normalizeHref(targetHref);
                const roots = [];
                if (changedEditable) roots.push(changedEditable);
                if (savedRoot && !roots.includes(savedRoot)) roots.push(savedRoot);

                if (blockId) {
                    const blockRoot = Array
                        .from(document.querySelectorAll('[data-block-id]'))
                        .find(el => el.dataset?.blockId === blockId);
                    const blockEditable = blockRoot?.querySelector?.('[contenteditable="true"], .tm-notion-editable') ?? null;
                    if (blockEditable && !roots.includes(blockEditable)) roots.push(blockEditable);
                }

                for (const root of roots) {
                    for (const anchor of root.querySelectorAll('a')) {
                        const rawHref = anchor.getAttribute('href') ?? '';
                        if (rawHref === targetHref ||
                            anchor.href === targetHref ||
                            anchor.href === normalizedTargetHref ||
                            normalizeHref(rawHref) === normalizedTargetHref) {
                            anchorsToUnwrap.add(anchor);
                        }
                    }
                }
            }

            if (anchorsToUnwrap.size === 0) {
                document.execCommand('unlink', false, null);
            }

            for (const anchor of anchorsToUnwrap) {
                const parent = anchor.parentNode;
                if (!parent) continue;
                changedEditable ??= anchor.closest?.('[contenteditable="true"], .tm-notion-editable') ?? null;
                while (anchor.firstChild) {
                    parent.insertBefore(anchor.firstChild, anchor);
                }
                parent.removeChild(anchor);
            }

            if (changedEditable) {
                const event = typeof InputEvent === 'function'
                    ? new InputEvent('input', { bubbles: true, inputType: 'formatRemove' })
                    : new Event('input', { bubbles: true });
                changedEditable.dispatchEvent(event);
            }
            _savedRange = null;
            return;
        }
        document.execCommand(command, false, value ?? null);
    }

    function queryFormatState(command) {
        return document.queryCommandState(command);
    }

    function insertHtml(html) {
        document.execCommand('insertHTML', false, html);
    }

    function insertLink(url, text) {
        const label = _escHtml(text || url);
        const href  = _escHtml(url);
        document.execCommand('insertHTML', false,
            `<a href="${href}" target="_blank" rel="noopener noreferrer">${label}</a>`);
    }

    function getBlockBoundingRect(blockId) {
        const el = document.querySelector(`[data-block-id="${blockId}"]`);
        if (!el) return null;
        const rect = el.getBoundingClientRect();
        return { top: rect.top, left: rect.left, width: rect.width, height: rect.height };
    }

    function wrapSelectionWithComment(commentId, blockId, dotNetRef, callbackName) {
        const sel = window.getSelection();
        if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return;
        const r = sel.getRangeAt(0);
        const mark = document.createElement('mark');
        mark.className = 'tm-notion-comment-highlight';
        mark.dataset.commentId = String(commentId);
        mark.dataset.blockId    = String(blockId || '');
        try {
            r.surroundContents(mark);
        } catch {
            const frag = r.extractContents();
            mark.appendChild(frag);
            r.insertNode(mark);
        }

        _notifyInput(mark);
        sel.removeAllRanges();

        if (dotNetRef && callbackName) {
            const blockEl = r.commonAncestorContainer.nodeType === Node.TEXT_NODE
                ? r.commonAncestorContainer.parentElement?.closest('[data-notion-block]')
                : r.commonAncestorContainer.closest?.('[data-notion-block]');
            const actualBlockId = blockId || blockEl?.dataset?.notionBlock || '';
            const text = mark.textContent || '';
            const start = r.startOffset;
            const end = r.endOffset;
            const rect = mark.getBoundingClientRect();
            // Text-comment panel is position:fixed → viewport coords (no scroll offsets).
            const top = rect.top;
            const left = rect.left;
            dotNetRef.invokeMethodAsync(callbackName, actualBlockId, commentId, text, start, end, top, left)
                .catch(() => {});
        }
    }

    function unwrapCommentHighlight(commentId) {
        const marks = document.querySelectorAll(`mark.tm-notion-comment-highlight[data-comment-id="${commentId}"]`);
        marks.forEach(mark => {
            const parent = mark.parentNode;
            if (!parent) return;
            while (mark.firstChild) {
                parent.insertBefore(mark.firstChild, mark);
            }
            parent.removeChild(mark);
            // Normalize adjacent text nodes
            parent.normalize();
        });
    }

    function setCommentHighlightActive(commentId, isActive) {
        const marks = document.querySelectorAll(`mark.tm-notion-comment-highlight[data-comment-id="${commentId}"]`);
        marks.forEach(mark => {
            mark.classList.toggle('tm-notion-comment-highlight--active', isActive);
        });
    }

    function registerPageDotNetRef(ref) {
        _pageDotNetRef = ref;
    }

    // Click on a comment-highlight mark → reopen the text-comment panel
    (function _initCommentMarkClick() {
        document.addEventListener('click', (e) => {
            const mark = e.target.closest('mark.tm-notion-comment-highlight');
            if (!mark || !_pageDotNetRef) return;
            // Don't trigger if the user is selecting text (mouse down + drag)
            const sel = window.getSelection();
            if (sel && !sel.isCollapsed) return;
            e.preventDefault();
            e.stopPropagation();
            const rect = mark.getBoundingClientRect();
            const commentId = mark.dataset.commentId || '';
            const blockId   = mark.dataset.blockId || '';
            // Text-comment panel is position:fixed → viewport coords (no scroll offsets).
            const top       = rect.top;
            const left      = rect.left;
            _pageDotNetRef.invokeMethodAsync('OnTextCommentMarkClicked', commentId, blockId, top, left)
                .catch(() => {});
        });
    })();

    // Token chip interactions — delete (×) and click-to-edit
    (function _initTokenChipInteraction() {
        document.addEventListener('mousedown', (e) => {
            // × delete button
            const del = e.target.closest('.tm-notion-token__delete');
            if (del) {
                e.preventDefault();
                e.stopPropagation();
                const chip = del.closest('.tm-notion-token');
                if (!chip) return;
                const inputEl = chip.closest('[contenteditable="true"]');
                chip.remove();
                if (inputEl) inputEl.dispatchEvent(new Event('input', { bubbles: true }));
                return;
            }

            // Chip body click → open dropdown for replacement
            const chip = e.target.closest('.tm-notion-token');
            if (!chip || !_pageDotNetRef) return;
            if (!chip.closest('[contenteditable="true"]')) return;
            e.preventDefault();
            _chipBeingEdited = chip;
            const rect = chip.getBoundingClientRect();
            _pageDotNetRef.invokeMethodAsync(
                'OnTokenChipClicked',
                chip.dataset.key || '',
                rect.bottom + 4,
                rect.left
            ).catch(console.error);
        });
    })();

    // ═══════════════════════════════════════════════════════════════════════════
    // 26.3 — Drag & drop
    // ═══════════════════════════════════════════════════════════════════════════

    function initDragDrop(containerElement, dotNetRef) {
        if (!containerElement) return;
        if (_dragContainers.has(containerElement)) destroyDragDrop(containerElement);

        let dragSrc    = null;
        let dragGhost  = null;
        let indicator  = null;
        let dropTarget = null;

        function _indicator() {
            if (indicator) return indicator;
            indicator = document.createElement('div');
            indicator.className = 'tm-notion-drop-indicator';
            indicator.style.cssText =
                'position:fixed;height:3px;background:var(--tm-color-primary,var(--tm-primary));border-radius:999px;' +
                'box-shadow:0 0 0 3px color-mix(in srgb,var(--tm-color-primary,var(--tm-primary)) 18%,transparent);' +
                'pointer-events:none;z-index:9999;display:none;transition:top .06s;';
            document.body.appendChild(indicator);
            return indicator;
        }

        function _blocks(container) {
            return Array.from(container.children).filter(child => child.matches?.('[data-notion-block]'));
        }

        function _blockAt(y) {
            return _blocks(containerElement).find(b => {
                const r = b.getBoundingClientRect();
                return y >= r.top && y <= r.bottom;
            }) ?? null;
        }

        function _indexOf(b) {
            return _blocks(containerElement).indexOf(b);
        }

        function _onDragStart(e) {
            if (!e.target.closest('[data-notion-drag-handle]')) return;
            const b = e.target.closest('[data-notion-block]');
            if (!b) return;
            if (!_blocks(containerElement).includes(b)) return;
            e.stopPropagation();
            dragSrc = b;
            window.__tmNotionBlockDrag = {
                blockId: b.getAttribute('data-block-id') || '',
                sourceParentBlockId: containerElement.getAttribute('data-parent-block-id') || '',
                sourcePageId: containerElement.getAttribute('data-page-id') || '',
                sourceContainer: containerElement,
                sourceDotNetRef: dotNetRef,
                sourceBlock: b
            };
            dragSrc.classList.add('tm-notion-dragging');
            dragGhost = b.cloneNode(true);
            Object.assign(dragGhost.style, {
                position: 'fixed', top: '-9999px', left: '-9999px',
                width: b.offsetWidth + 'px', opacity: '0.88',
                pointerEvents: 'none', boxShadow: '0 4px 20px rgba(0,0,0,.22)',
                borderRadius: '4px', background: 'var(--tm-bg, #fff)', zIndex: '10000'
            });
            document.body.appendChild(dragGhost);
            e.dataTransfer.setDragImage(dragGhost, 24, 24);
            e.dataTransfer.effectAllowed = 'move';
        }

        function _onDragOver(e) {
            const activeDrag = window.__tmNotionBlockDrag;
            if (!activeDrag?.blockId) return; // not a block drag — let diagram/wireframe stencil drops work
            e.preventDefault();
            e.dataTransfer.dropEffect = 'move';
            const b = _blockAt(e.clientY);
            if (!b || b === activeDrag.sourceBlock) { _indicator().style.display = 'none'; return; }
            e.stopPropagation();
            dropTarget = b;
            const rect  = b.getBoundingClientRect();
            const after = e.clientY > rect.top + rect.height / 2;
            const ind   = _indicator();
            const y     = after ? rect.bottom : rect.top;
            Object.assign(ind.style, {
                display: 'block',
                top:   y - 1 + 'px',
                left:  rect.left + 'px',
                width: rect.width + 'px'
            });
        }

        function _onDragLeave(e) {
            if (!containerElement.contains(e.relatedTarget)) {
                _indicator().style.display = 'none';
            }
        }

        function _onDrop(e) {
            const activeDrag = window.__tmNotionBlockDrag;
            if (!activeDrag?.blockId) return; // not a block drag — let diagram/wireframe stencil drops work
            e.preventDefault();
            _indicator().style.display = 'none';
            if (!dropTarget || dropTarget === activeDrag.sourceBlock) return;
            e.stopPropagation();
            const src = _indexOf(activeDrag.sourceBlock);
            const rect = dropTarget.getBoundingClientRect();
            const after = e.clientY > rect.top + rect.height / 2;
            let dst  = _indexOf(dropTarget);
            if (after) dst++;
            if (activeDrag.sourceContainer === containerElement && src !== -1 && dst !== -1 && src !== dst) {
                dotNetRef.invokeMethodAsync('OnBlockReordered', src, dst).catch(console.error);
            } else if (activeDrag.sourceContainer !== containerElement && dst !== -1) {
                const targetPageId = containerElement.getAttribute('data-page-id') || activeDrag.sourcePageId || '';
                const targetParentBlockId = containerElement.getAttribute('data-parent-block-id') || '';
                // Take the block out of the source list first. Adding it to the target before the
                // source lets go would show the same block in two lists until the move resolves.
                // The target reloads from the server, and it refreshes the page on failure, so the
                // source never ends up permanently missing a block the move did not actually take.
                const dropped = () => dotNetRef.invokeMethodAsync(
                    'OnExternalBlockDropped',
                    activeDrag.blockId,
                    targetPageId,
                    activeDrag.sourceParentBlockId || null,
                    targetParentBlockId || null,
                    dst
                );

                const sourceRef = activeDrag.sourceDotNetRef;
                Promise.resolve(sourceRef?.invokeMethodAsync('OnExternalBlockRemoved', activeDrag.blockId))
                    .then(dropped, dropped)
                    .catch(console.error);
            }
        }

        function _onDragEnd() {
            dragSrc?.classList.remove('tm-notion-dragging');
            window.__tmNotionBlockDrag?.sourceBlock?.classList?.remove('tm-notion-dragging');
            dragGhost?.parentNode?.removeChild(dragGhost);
            if (indicator) indicator.style.display = 'none';
            if (window.__tmNotionBlockDrag?.sourceContainer === containerElement)
                window.__tmNotionBlockDrag = null;
            dragSrc = dragGhost = dropTarget = null;
        }

        function _onKeyDown(e) {
            if (e.key !== 'Escape' || !window.__tmNotionBlockDrag?.blockId) return;
            window.__tmNotionBlockDrag.sourceBlock?.classList?.remove('tm-notion-dragging');
            dragGhost?.parentNode?.removeChild(dragGhost);
            if (indicator) indicator.style.display = 'none';
            window.__tmNotionBlockDrag = null;
            dragSrc = dragGhost = dropTarget = null;
        }

        containerElement.addEventListener('dragstart',  _onDragStart);
        containerElement.addEventListener('dragover',   _onDragOver);
        containerElement.addEventListener('dragleave',  _onDragLeave);
        containerElement.addEventListener('drop',       _onDrop);
        containerElement.addEventListener('dragend',    _onDragEnd);
        document.addEventListener('keydown', _onKeyDown);

        _dragContainers.set(containerElement, {
            cleanup() {
                containerElement.removeEventListener('dragstart',  _onDragStart);
                containerElement.removeEventListener('dragover',   _onDragOver);
                containerElement.removeEventListener('dragleave',  _onDragLeave);
                containerElement.removeEventListener('drop',       _onDrop);
                containerElement.removeEventListener('dragend',    _onDragEnd);
                document.removeEventListener('keydown', _onKeyDown);
                indicator?.parentNode?.removeChild(indicator);
                indicator = null;
            }
        });
    }

    function destroyDragDrop(containerElement) {
        if (!containerElement || !_dragContainers.has(containerElement)) return;
        _dragContainers.get(containerElement).cleanup();
        _dragContainers.delete(containerElement);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 26.4 — Slash menu positioning
    // ═══════════════════════════════════════════════════════════════════════════

    function getCaretCoords() {
        const r = _range();
        if (!r) return { top: 0, left: 0 };
        const c = r.cloneRange();
        c.collapse(true);
        let rect = c.getBoundingClientRect();
        if (rect.width === 0 && rect.height === 0) {
            const span = document.createElement('span');
            span.textContent = '\u200b';
            c.insertNode(span);
            rect = span.getBoundingClientRect();
            span.parentNode?.removeChild(span);
        }
        // getBoundingClientRect() is already viewport-relative. The slash / mention /
        // page-link / token menus are position:fixed, so they must receive viewport
        // coordinates — adding window.scrollY/scrollX pushed them below the fold
        // whenever the host page scrolled the window.
        return {
            top:  rect.bottom,
            left: rect.left
        };
    }

    function getTextBeforeCaret(element) {
        if (!element) return '';
        const r = _range();
        if (!r) return '';
        const pre = document.createRange();
        pre.selectNodeContents(element);
        pre.setEnd(r.startContainer, r.startOffset);
        return pre.toString();
    }

    let _smartLinkMenu = null;
    let _smartLinkState = null;

    function isSmartLinkCandidate(text) {
        const value = String(text || '').trim();
        if (!value || /\s/.test(value)) return false;
        try {
            const explicitScheme = /^https?:\/\//i.test(value);
            const url = explicitScheme ? new URL(value) : new URL(`https://${value}`);
            if (url.protocol !== 'http:' && url.protocol !== 'https:') return false;

            // Without a scheme the host must look like a domain. `new URL('https://bold')` parses,
            // so any single pasted word would otherwise be mistaken for a link and lose its markup.
            return explicitScheme || /\.[a-z]{2,}$/i.test(url.hostname);
        } catch {
            return false;
        }
    }

    function normalizeSmartLinkUrl(text) {
        const value = String(text || '').trim();
        return /^https?:\/\//i.test(value) ? value : `https://${value.replace(/^\/+/, '')}`;
    }

    function escapeHtml(value) {
        return String(value || '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#39;');
    }

    function restoreSmartLinkRange(element) {
        let range = _smartLinkState?.range;
        const selection = window.getSelection?.();
        if (!element || !range || !selection) return false;
        if (!element.contains(range.startContainer) || !element.contains(range.endContainer)) return false;

        if (range.collapsed &&
            range.startContainer === element &&
            range.startOffset === 0 &&
            element.textContent?.length > 0) {
            range = document.createRange();
            range.selectNodeContents(element);
            range.collapse(false);
            _smartLinkState.range = range;
        }

        selection.removeAllRanges();
        selection.addRange(range);
        return true;
    }

    function insertHtmlAtCurrentRange(element, html) {
        if (!element) return;
        element.focus();
        restoreSmartLinkRange(element);

        const selection = window.getSelection?.();
        let range = selection && selection.rangeCount ? selection.getRangeAt(0) : null;
        if (!range || !element.contains(range.commonAncestorContainer)) {
            range = document.createRange();
            range.selectNodeContents(element);
            range.collapse(false);
            selection?.removeAllRanges();
            selection?.addRange(range);
        }

        range.deleteContents();
        const template = document.createElement('template');
        template.innerHTML = html;
        const fragment = template.content;
        const last = fragment.lastChild;
        range.insertNode(fragment);

        if (last) {
            range = document.createRange();
            range.setStartAfter(last);
            range.collapse(true);
            selection?.removeAllRanges();
            selection?.addRange(range);
        }

        element.dispatchEvent(new Event('input', { bubbles: true }));
    }

    function insertPlainTextAtCurrentRange(element, text) {
        insertHtmlAtCurrentRange(element, escapeHtml(text).replace(/\n/g, '<br>'));
    }

    function sanitizePastedHtml(html) {
        const template = document.createElement('template');
        template.innerHTML = String(html || '');

        const allowedInlineTags = new Set(['A', 'B', 'BR', 'CODE', 'EM', 'I', 'MARK', 'S', 'SPAN', 'STRONG', 'U']);
        const blockTags = new Set(['ADDRESS', 'ARTICLE', 'ASIDE', 'BLOCKQUOTE', 'DIV', 'FIGCAPTION', 'FIGURE', 'FOOTER', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'HEADER', 'LI', 'MAIN', 'P', 'PRE', 'SECTION', 'TD', 'TH', 'TR']);
        const rejectedTags = new Set(['IFRAME', 'LINK', 'META', 'OBJECT', 'SCRIPT', 'STYLE']);

        function clean(node) {
            if (node.nodeType === Node.TEXT_NODE) {
                return document.createTextNode(node.textContent || '');
            }

            if (node.nodeType !== Node.ELEMENT_NODE) {
                return document.createDocumentFragment();
            }

            const tag = node.tagName.toUpperCase();
            const fragment = document.createDocumentFragment();
            if (rejectedTags.has(tag)) {
                return fragment;
            }

            const target = allowedInlineTags.has(tag)
                ? document.createElement(tag.toLowerCase())
                : fragment;

            if (tag === 'A') {
                const href = node.getAttribute('href') || '';
                if (/^https?:\/\//i.test(href) || /^mailto:/i.test(href)) {
                    target.setAttribute('href', href);
                    target.setAttribute('target', '_blank');
                    target.setAttribute('rel', 'noopener noreferrer');
                }
            }

            node.childNodes.forEach(child => target.appendChild(clean(child)));

            if (blockTags.has(tag) && fragment.childNodes.length > 0) {
                fragment.appendChild(document.createElement('br'));
            }

            return target;
        }

        const cleanFragment = document.createDocumentFragment();
        template.content.childNodes.forEach(child => cleanFragment.appendChild(clean(child)));
        const wrapper = document.createElement('div');
        wrapper.appendChild(cleanFragment);
        return wrapper.innerHTML.replace(/(<br>\s*)+$/i, '');
    }

    function insertPlainSmartLink(element, rawUrl) {
        const url = normalizeSmartLinkUrl(rawUrl);
        restoreSmartLinkRange(element);
        const prefix = shouldPrefixInlineInsertWithSpace(element) ? ' ' : '';
        insertHtmlAtCurrentRange(
            element,
            `${prefix}<a href="${escapeHtml(url)}" target="_blank" rel="noopener noreferrer">${escapeHtml(url)}</a>`
        );
    }

    function insertSmartLinkChip(element, rawUrl, title, faviconUrl, providerName) {
        const url = normalizeSmartLinkUrl(rawUrl);
        const safeTitle = escapeHtml(title || url);
        const safeUrl = escapeHtml(url);
        const domain = providerName || hostFromUrl(url);
        const domainMarkup = domain
            ? `<span class="tm-notion-smart-link__domain">${escapeHtml(domain)}</span>`
            : '';
        restoreSmartLinkRange(element);
        const prefix = shouldPrefixInlineInsertWithSpace(element) ? ' ' : '';
        const favicon = faviconUrl
            ? `<img class="tm-notion-smart-link__favicon" src="${escapeHtml(faviconUrl)}" alt="" loading="lazy" />`
            : '<span class="tm-notion-smart-link__favicon" aria-hidden="true"></span>';

        insertHtmlAtCurrentRange(
            element,
            `${prefix}<a class="tm-notion-smart-link" href="${safeUrl}" target="_blank" rel="noopener noreferrer" contenteditable="false">${favicon}<span class="tm-notion-smart-link__title">${safeTitle}</span>${domainMarkup}</a>`
        );
    }

    function hostFromUrl(rawUrl) {
        try {
            return new URL(normalizeSmartLinkUrl(rawUrl)).host;
        } catch {
            return '';
        }
    }

    function shouldPrefixInlineInsertWithSpace(element) {
        const selection = window.getSelection();
        if (!selection || selection.rangeCount === 0) return false;

        const range = selection.getRangeAt(0);
        const before = range.cloneRange();
        before.selectNodeContents(element);
        try {
            before.setEnd(range.startContainer, range.startOffset);
        } catch {
            return false;
        }

        const text = before.toString();
        return text.length > 0 && !/\s$/.test(text);
    }

    function closeSmartLinkMenu() {
        if (!_smartLinkMenu) return;
        document.removeEventListener('keydown', onSmartLinkMenuKeyDown, true);
        document.removeEventListener('pointerdown', onSmartLinkMenuPointerDown, true);
        _smartLinkMenu.remove();
        _smartLinkMenu = null;
        _smartLinkState = null;
    }

    function onSmartLinkMenuKeyDown(event) {
        if (event.key === 'Escape') {
            event.preventDefault();
            closeSmartLinkMenu();
        }
    }

    function onSmartLinkMenuPointerDown(event) {
        if (_smartLinkMenu && !_smartLinkMenu.contains(event.target)) {
            closeSmartLinkMenu();
        }
    }

    function smartLinkLabel(element, name) {
        const value = element?.dataset?.[name];
        return value && !/^\[.+\]$/.test(value) ? value : name;
    }

    function showSmartLinkMenu(element, dotNetRef, rawUrl, anchorRange) {
        closeSmartLinkMenu();

        _smartLinkState = { element, url: rawUrl, range: anchorRange };
        const menu = document.createElement('div');
        menu.className = 'tm-notion-smart-link-menu';
        menu.setAttribute('role', 'menu');

        const actions = [
            ['Inline', smartLinkLabel(element, 'smartLinkInlineLabel')],
            ['Card', smartLinkLabel(element, 'smartLinkCardLabel')],
            ['Plain', smartLinkLabel(element, 'smartLinkPlainLabel')]
        ];

        for (const [mode, label] of actions) {
            const item = document.createElement('button');
            item.type = 'button';
            item.className = 'tm-notion-smart-link-menu__item';
            item.setAttribute('role', 'menuitem');
            item.textContent = label;
            item.addEventListener('click', async event => {
                event.preventDefault();
                event.stopPropagation();

                if (mode === 'Plain') {
                    insertPlainSmartLink(element, rawUrl);
                } else {
                    menu.classList.add('tm-notion-smart-link-menu--loading');
                    menu.setAttribute('aria-busy', 'true');
                    for (const button of menu.querySelectorAll('.tm-notion-smart-link-menu__item')) {
                        button.disabled = true;
                    }
                    item.classList.add('tm-notion-smart-link-menu__item--loading');
                    restoreSmartLinkRange(element);
                    await dotNetRef.invokeMethodAsync('OnSmartLinkPasteRequested', rawUrl, mode);
                }

                closeSmartLinkMenu();
            });
            menu.appendChild(item);
        }

        document.body.appendChild(menu);
        const rect = anchorRange?.getBoundingClientRect?.();
        const hostRect = element.getBoundingClientRect();
        const top = Math.max(8, (rect?.bottom || hostRect.bottom) + window.scrollY + 6);
        const left = Math.max(8, Math.min((rect?.left || hostRect.left) + window.scrollX, window.scrollX + document.documentElement.clientWidth - 260));
        menu.style.top = `${top}px`;
        menu.style.left = `${left}px`;

        _smartLinkMenu = menu;
        setTimeout(() => {
            document.addEventListener('keydown', onSmartLinkMenuKeyDown, true);
            document.addEventListener('pointerdown', onSmartLinkMenuPointerDown, true);
        }, 0);
    }

    const _PASTE_BLOCK_TAGS = 'p, div, h1, h2, h3, h4, h5, h6, ul, ol, li, blockquote, pre, table, hr, figure';

    /** Elements that have no inline representation, so pasting one always builds a real block. */
    const _PASTE_STANDALONE_TAGS = 'table, pre, hr';

    /**
     * Does the clipboard carry block structure worth preserving? Word and Google Docs wrap
     * everything in a <div> or an <html><body>, so wrappers are not counted — one paragraph inside
     * three of them still reads as one block, while a list of five items reads as five.
     */
    function _countPastedBlocks(html) {
        const template = document.createElement('template');
        template.innerHTML = String(html || '');

        // A table, a code block or a rule cannot live inside a paragraph: always go structured.
        if (template.content.querySelector(_PASTE_STANDALONE_TAGS)) return 2;

        return Array.from(template.content.querySelectorAll(_PASTE_BLOCK_TAGS)).filter(el => {
            if (el.matches('li')) return true;
            if (el.matches('ul, ol')) return false;      // counted through their <li> children
            return !el.querySelector(_PASTE_BLOCK_TAGS); // a wrapper is not a block of its own
        }).length;
    }

    async function handleSmartLinkPaste(element, dotNetRef, event) {
        const cd = event.clipboardData || window.clipboardData;
        const text = cd?.getData?.('text/plain') || '';
        const html = cd?.getData?.('text/html') || '';
        if (!text && !html) return false;

        event.preventDefault();
        const range = _range()?.cloneRange();
        const trimmed = text.trim();

        if (html.trim() && !isSmartLinkCandidate(trimmed)) {
            if (_countPastedBlocks(html) > 1) {
                // Several block elements: let C# import them into real page blocks. Flattening
                // them into this one would turn a pasted article into a single paragraph.
                dotNetRef.invokeMethodAsync('OnHtmlPasted', html).catch(console.error);
                return true;
            }

            insertHtmlAtCurrentRange(element, sanitizePastedHtml(html));
            return true;
        }

        if (!isSmartLinkCandidate(trimmed) || element.dataset.smartLinkEnabled !== 'true') {
            insertPlainTextAtCurrentRange(element, text);
            return true;
        }

        const url = normalizeSmartLinkUrl(trimmed);
        let hasProvider = false;
        try {
            hasProvider = await dotNetRef.invokeMethodAsync('HasSmartLinkProvider');
        } catch {
            hasProvider = false;
        }

        if (hasProvider) {
            showSmartLinkMenu(element, dotNetRef, url, range);
        } else {
            insertPlainSmartLink(element, url);
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 26.5 — Keyboard handler
    // ═══════════════════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════════════════
    // Undo / redo — editor history vs the browser's own contenteditable history
    // ═══════════════════════════════════════════════════════════════════════════

    /** Editables the user has typed into since they last got focus. */
    const _dirtyEditables = new WeakSet();

    function _markEditableDirty(element) { _dirtyEditables.add(element); }
    function _clearEditableDirty(element) { _dirtyEditables.delete(element); }

    /**
     * True when the browser should keep Ctrl+Z for itself: the caret is inside a block the user is
     * still typing into, and undoing character by character is what they expect. Once the block is
     * committed the editor's own history takes over and undoes whole structural edits.
     */
    function _nativeUndoOwnsTheCaret() {
        const editable = document.activeElement?.closest?.('[contenteditable="true"]');
        return !!editable && _dirtyEditables.has(editable);
    }

    function initUndoRedo(dotNetRef) {
        const onKeyDown = (e) => {
            const key = e.key?.toLowerCase();
            if (!(e.ctrlKey || e.metaKey) || (key !== 'z' && key !== 'y')) return;
            if (_nativeUndoOwnsTheCaret()) return;

            e.preventDefault();
            const redo = key === 'y' || (key === 'z' && e.shiftKey);
            dotNetRef.invokeMethodAsync(redo ? 'RedoAsync' : 'UndoAsync').catch(console.error);
        };

        document.addEventListener('keydown', onKeyDown, true);
        return () => document.removeEventListener('keydown', onKeyDown, true);
    }

    function initKeyboardHandler(element, dotNetRef) {
        if (!element) { console.warn('initKeyboardHandler: element is null'); return; }
        if (!_blocks.has(element)) _blocks.set(element, { dotNetRef, listeners: [] });
        const state = _blocks.get(element);
        state.dotNetRef = dotNetRef;
        _offAll(state.listeners);

        function _htmlAroundCaret() {
            const r = _range();
            if (!r) return { before: element.innerHTML, after: '' };
            function _fragHtml(fr) {
                const d = document.createElement('div');
                d.appendChild(fr);
                return d.innerHTML;
            }
            const beforeR = document.createRange();
            beforeR.selectNodeContents(element);
            beforeR.setEnd(r.startContainer, r.startOffset);
            const afterR = document.createRange();
            afterR.selectNodeContents(element);
            afterR.setStart(r.endContainer, r.endOffset);
            return {
                before: _fragHtml(beforeR.cloneContents()),
                after:  _fragHtml(afterR.cloneContents())
            };
        }

        const onKeyDown = (e) => {
            if (e.key === 'Backspace' && !e.shiftKey && !e.ctrlKey && !e.metaKey) {
                const chip = _statusChipBeforeCaret(element);
                if (chip) {
                    e.preventDefault();
                    chip.remove();
                    element.dispatchEvent(new Event('input', { bubbles: true }));
                    return;
                }
            }

            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                const { before, after } = _htmlAroundCaret();
                dotNetRef.invokeMethodAsync('OnEnterPressed', before, after).catch(console.error);
                return;
            }
            if (e.key === 'Backspace' && _isEmpty(element)) {
                e.preventDefault();
                dotNetRef.invokeMethodAsync('OnBackspaceOnEmpty').catch(console.error);
                return;
            }
            if (e.key === 'Backspace' && _caretAtStart(element)) {
                // Non-empty block with the caret at the very start: merge into the previous block
                // instead of deleting a character that is not there.
                e.preventDefault();
                dotNetRef.invokeMethodAsync('OnBackspaceAtStart', element.innerHTML).catch(console.error);
                return;
            }
            if (e.key === 'Tab') {
                e.preventDefault();
                dotNetRef.invokeMethodAsync('OnTabPressed', e.shiftKey).catch(console.error);
                return;
            }
            if (e.key === 'ArrowUp'   && !e.shiftKey && !e.ctrlKey && !e.metaKey) {
                dotNetRef.invokeMethodAsync('OnArrowUp').catch(console.error);
            }
            if (e.key === 'ArrowDown' && !e.shiftKey && !e.ctrlKey && !e.metaKey) {
                dotNetRef.invokeMethodAsync('OnArrowDown').catch(console.error);
            }
        };

        const onInput = () => {
            const text = getTextBeforeCaret(element);

            const shortcut = _detectMarkdownShortcut(text);
            if (shortcut) {
                // Remove only the trigger. Clearing the whole block would throw away anything the
                // user had typed after the caret; the conversion then reads the live DOM.
                _deleteBeforeCaret(element, shortcut.triggerLength);
                dotNetRef.invokeMethodAsync('OnMarkdownShortcut', shortcut.key).catch(console.error);
                return;
            }

            if (_applyInlineShortcut(element)) {
                return;
            }

            const lastChar = text[text.length - 1];
            const prevChar = text[text.length - 2];
            const atWordBoundary = !prevChar || prevChar === ' ';

            if (lastChar === '/' && atWordBoundary) {
                // Store the element and anchor position (just before '/')
                _slashElement = element;
                const r = _range();
                if (r) {
                    _slashAnchorNode = r.startContainer;
                    _slashAnchorOff  = Math.max(0, r.startOffset - 1);
                }
                const c = getCaretCoords();
                dotNetRef.invokeMethodAsync('OnSlashTriggered', c.top, c.left).catch(console.error);
            } else if (lastChar === '@' && atWordBoundary) {
                _mentionElement    = element;
                _mentionTriggerLen = 1;
                const r1 = _range();
                if (r1) {
                    _mentionAnchorNode = r1.startContainer;
                    _mentionAnchorOff  = Math.max(0, r1.startOffset - 1);
                }
                const c = getCaretCoords();
                dotNetRef.invokeMethodAsync('OnMentionTriggered', c.top, c.left).catch(console.error);
            } else if (text.endsWith('[[')) {
                _mentionElement    = element;
                _mentionTriggerLen = 2;
                const r2 = _range();
                if (r2) {
                    _mentionAnchorNode = r2.startContainer;
                    _mentionAnchorOff  = Math.max(0, r2.startOffset - 2);
                }
                const c = getCaretCoords();
                dotNetRef.invokeMethodAsync('OnPageLinkTriggered', c.top, c.left).catch(console.error);
            } else if (text.endsWith('{{')) {
                _tokenElement    = element;
                const r3 = _range();
                if (r3) {
                    _tokenAnchorNode = r3.startContainer;
                    _tokenAnchorOff  = Math.max(0, r3.startOffset - 2);
                }
                const c = getCaretCoords();
                dotNetRef.invokeMethodAsync('OnTokenTriggered', c.top, c.left).catch(console.error);
            }
        };

        const onClick = (e) => {
            const chip = e.target?.closest?.('.tm-notion-status');
            if (!chip || !element.contains(chip)) return;

            e.preventDefault();
            e.stopPropagation();
            document.querySelectorAll('.tm-notion-status[data-tm-status-editing="true"]')
                .forEach(existing => delete existing.dataset.tmStatusEditing);
            chip.dataset.tmStatusEditing = 'true';
            _statusChipBeingEdited = chip;

            const rect = chip.getBoundingClientRect();
            const block = chip.closest('[data-block-id]');
            const chipIndex = Array.from(element.querySelectorAll('.tm-notion-status')).indexOf(chip);
            const color = chip.dataset.statusColor || _statusColorFromClass(chip) || 'gray';
            const label = chip.dataset.statusLabel || chip.textContent?.trim() || '';

            _pageDotNetRef?.invokeMethodAsync(
                'OnInlineStatusClicked',
                block?.dataset?.blockId || '',
                label,
                color,
                { top: rect.top, left: rect.left, width: rect.width, height: rect.height },
                chipIndex
            ).catch(console.error);
        };

        state.listeners.push(
            _on(element, 'keydown', onKeyDown),
            _on(element, 'input',   () => { _markEditableDirty(element); onInput(); }),
            _on(element, 'focus',   () => _clearEditableDirty(element)),
            _on(element, 'blur',    () => _clearEditableDirty(element)),
            _on(element, 'click',   onClick),
            _on(element, 'paste',   event => { handleSmartLinkPaste(element, dotNetRef, event).catch(console.error); })
        );
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 26.6 — Inline math (KaTeX with plain-text fallback)
    // ═══════════════════════════════════════════════════════════════════════════

    /**
     * Returns the code as highlighted HTML. Prism is optional, exactly like KaTeX: without it the
     * code comes back HTML-escaped but untokenised, so the block still renders, just without colour.
     * Returns a string the caller renders — it never writes the DOM, so Blazor stays the sole owner
     * of the code element and cannot wipe what was painted.
     */
    function highlightToHtml(code, prismLanguage) {
        const text = String(code ?? '');
        const grammar = prismLanguage && window.Prism?.languages?.[prismLanguage];
        if (grammar) {
            try {
                // A trailing newline needs a spacer or the last line of the layer collapses.
                return window.Prism.highlight(text, grammar, prismLanguage) + (text.endsWith('\n') ? '\n' : '');
            } catch { /* fall through to escaped plain text */ }
        }
        return _escHtml(text);
    }

    function renderEquation(element, latex) {
        if (!element) return;
        if (!latex || !latex.trim()) { element.innerHTML = ''; return; }
        if (window.katex) {
            try {
                element.innerHTML = window.katex.renderToString(latex, {
                    displayMode: true,
                    throwOnError: false,
                    output: 'html'
                });
                return;
            } catch { /* fall through */ }
        }
        element.textContent = latex;
        element.dataset.latex = latex;
    }

    function renderInlineMath(element, latex) {
        if (!element) return;
        if (window.katex) {
            try {
                element.innerHTML = window.katex.renderToString(latex, {
                    displayMode: false,
                    throwOnError: false,
                    output: 'html'
                });
                return;
            } catch { /* fall through */ }
        }
        element.textContent = latex;
        element.dataset.latex = latex;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 26.7 — Clipboard
    // ═══════════════════════════════════════════════════════════════════════════

    function handlePaste(element, dotNetRef) {
        if (!element) return;
        if (!_blocks.has(element)) _blocks.set(element, { dotNetRef, listeners: [] });
        const state = _blocks.get(element);

        const onPaste = (e) => {
            e.preventDefault();
            const cd = e.clipboardData || window.clipboardData;

            // Image first
            const imgItem = Array.from(cd.items || []).find(i => i.type.startsWith('image/'));
            if (imgItem) {
                const file = imgItem.getAsFile();
                if (file) {
                    const fr = new FileReader();
                    fr.onload = () => dotNetRef.invokeMethodAsync(
                        'OnImagePasted', fr.result, file.type, file.name || 'pasted-image'
                    ).catch(console.error);
                    fr.readAsDataURL(file);
                    return;
                }
            }

            // HTML
            const html = cd.getData('text/html');
            if (html?.trim()) {
                dotNetRef.invokeMethodAsync('OnHtmlPasted', html).catch(console.error);
                return;
            }

            // Plain text
            const text = cd.getData('text/plain');
            if (text) dotNetRef.invokeMethodAsync('OnTextPasted', text).catch(console.error);
        };

        state.listeners.push(_on(element, 'paste', onPaste));
    }

    // ── Paste on media blocks (image/pdf/file empty-state wrapper) ────────────

    const _pasteBlocks = new WeakMap(); // element → { listeners: [] }

    function initBlockPaste(element, dotNetRef) {
        if (!element) return;
        destroyBlockPaste(element);
        const listeners = [];

        const onPaste = (e) => {
            const cd = e.clipboardData || window.clipboardData;
            const imgItem = Array.from(cd.items || []).find(i => i.type.startsWith('image/'));
            if (!imgItem) return;

            e.preventDefault();
            const file = imgItem.getAsFile();
            if (!file) return;

            const fr = new FileReader();
            fr.onload = () => dotNetRef.invokeMethodAsync(
                'OnImagePasted', fr.result, file.type, file.name || 'pasted-image'
            ).catch(console.error);
            fr.readAsDataURL(file);
        };

        listeners.push(_on(element, 'paste', onPaste));
        _pasteBlocks.set(element, { listeners });
    }

    function destroyBlockPaste(element) {
        const state = _pasteBlocks.get(element);
        if (!state) return;
        _offAll(state.listeners);
        _pasteBlocks.delete(element);
    }

    // ── File drop on media blocks (image/pdf/file empty-state wrapper) ─────────

    const _dropBlocks = new WeakMap(); // element → { listeners: [] }

    function initBlockDropZone(element, dotNetRef) {
        if (!element) return;
        destroyBlockDropZone(element);
        const listeners = [];

        const onDrop = (e) => {
            const files = Array.from(e.dataTransfer?.files || []);
            if (files.length === 0) return; // block-reorder drag — let it bubble to container
            e.preventDefault();
            e.stopPropagation();
            const file = files[0];
            const fr = new FileReader();
            fr.onload = () => dotNetRef.invokeMethodAsync(
                'OnFileDropped', fr.result, file.type, file.name || 'dropped-file'
            ).catch(console.error);
            fr.readAsDataURL(file);
        };

        listeners.push(_on(element, 'drop', onDrop));
        _dropBlocks.set(element, { listeners });
    }

    function destroyBlockDropZone(element) {
        const state = _dropBlocks.get(element);
        if (!state) return;
        _offAll(state.listeners);
        _dropBlocks.delete(element);
    }

    function copyBlocksToClipboard(blocksJson) {
        const text = blocksJson ?? '';
        if (navigator.clipboard?.writeText) {
            navigator.clipboard.writeText(text).catch(console.error);
        } else {
            const ta = Object.assign(document.createElement('textarea'), {
                value: text
            });
            Object.assign(ta.style, { position: 'fixed', opacity: '0' });
            document.body.appendChild(ta);
            ta.select();
            document.execCommand('copy');
            document.body.removeChild(ta);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 26.8 — Resize handle
    // ═══════════════════════════════════════════════════════════════════════════

    function initResizeHandle(element, dotNetRef) {
        if (!element) return;
        if (_resizeHandles.has(element)) _resizeHandles.get(element).cleanup();

        const handle = document.createElement('div');
        handle.className = 'tm-notion-resize-handle';
        Object.assign(handle.style, {
            position: 'absolute', right: '0', top: '0', bottom: '0',
            width: '6px', cursor: 'ew-resize', zIndex: '10',
            background: 'transparent', borderRadius: '0 3px 3px 0'
        });

        let isDown = false, startX = 0, startW = 0;
        let _iframeStyles = [];

        const disableIframes = () => {
            _iframeStyles = [];
            element.querySelectorAll('iframe').forEach(iframe => {
                _iframeStyles.push({ el: iframe, original: iframe.style.pointerEvents });
                iframe.style.pointerEvents = 'none';
            });
        };
        const restoreIframes = () => {
            _iframeStyles.forEach(item => { item.el.style.pointerEvents = item.original; });
            _iframeStyles = [];
        };

        const onDown = (e) => {
            e.preventDefault();
            isDown  = true;
            startX  = e.clientX;
            startW  = element.offsetWidth;
            document.body.style.cursor     = 'ew-resize';
            document.body.style.userSelect = 'none';
            element.querySelectorAll('img').forEach(img => img.style.pointerEvents = 'none');
            disableIframes();
        };
        const onMove = (e) => {
            if (!isDown) return;
            element.style.width = Math.max(80, startW + e.clientX - startX) + 'px';
        };
        const onUp = () => {
            if (!isDown) return;
            isDown = false;
            document.body.style.cursor = document.body.style.userSelect = '';
            element.querySelectorAll('img').forEach(img => img.style.pointerEvents = '');
            restoreIframes();
            dotNetRef.invokeMethodAsync('OnResize', element.offsetWidth, element.offsetHeight)
                     .catch(console.error);
        };

        handle.addEventListener('mousedown', onDown);
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup',   onUp);

        if (getComputedStyle(element).position === 'static') element.style.position = 'relative';
        element.appendChild(handle);

        _resizeHandles.set(element, {
            cleanup() {
                handle.removeEventListener('mousedown', onDown);
                document.removeEventListener('mousemove', onMove);
                document.removeEventListener('mouseup',   onUp);
                element.querySelectorAll('img').forEach(img => img.style.pointerEvents = '');
                restoreIframes();
                handle.parentNode?.removeChild(handle);
            }
        });
    }

    function destroyResizeHandle(element) {
        if (!element || !_resizeHandles.has(element)) return;
        _resizeHandles.get(element).cleanup();
        _resizeHandles.delete(element);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 54.0 — Column width helper
    // ═══════════════════════════════════════════════════════════════════════════

    function setColumnWidth(element, widthPercent) {
        if (!element) return;
        element.style.flexBasis = widthPercent.toFixed(2) + '%';
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 54.1 — Column resize
    // ═══════════════════════════════════════════════════════════════════════════

    function initColumnResize(containerElement, dotNetRef) {
        if (!containerElement) return;
        destroyColumnResize(containerElement);

        const cleanups = [];

        const attachDivider = (divider) => {
            const idx = parseInt(divider.dataset.colDivider, 10);

            const onDown = (e) => {
                e.preventDefault();

                const cols     = Array.from(containerElement.querySelectorAll('[data-col-index]'))
                    .sort((a, b) => parseInt(a.dataset.colIndex) - parseInt(b.dataset.colIndex));
                const leftCol  = cols[idx];
                const rightCol = cols[idx + 1];
                if (!leftCol || !rightCol) return;

                const startX     = e.clientX;
                const totalW     = containerElement.offsetWidth;
                const leftStart  = leftCol.offsetWidth;
                const rightStart = rightCol.offsetWidth;
                const minW       = Math.max(parseFloat(getComputedStyle(leftCol).minWidth) || 120, totalW * 0.1);

                // Disable iframes during drag so they don't steal mouse events
                let _iframeStyles = [];
                const disableIframes = () => {
                    _iframeStyles = [];
                    containerElement.querySelectorAll('iframe').forEach(iframe => {
                        _iframeStyles.push({ el: iframe, original: iframe.style.pointerEvents });
                        iframe.style.pointerEvents = 'none';
                    });
                };
                const restoreIframes = () => {
                    _iframeStyles.forEach(item => { item.el.style.pointerEvents = item.original; });
                    _iframeStyles = [];
                };

                document.body.style.cursor     = 'col-resize';
                document.body.style.userSelect = 'none';
                divider.classList.add('tm-notion-column-list__divider--active');
                disableIframes();

                const onMove = (e2) => {
                    const delta    = e2.clientX - startX;
                    const newLeft  = Math.max(minW, Math.min(leftStart + delta, leftStart + rightStart - minW));
                    const newRight = (leftStart + rightStart) - newLeft;
                    leftCol.style.flexBasis  = (newLeft  / totalW * 100).toFixed(2) + '%';
                    rightCol.style.flexBasis = (newRight / totalW * 100).toFixed(2) + '%';
                };

                const onUp = () => {
                    document.body.style.cursor     = '';
                    document.body.style.userSelect = '';
                    divider.classList.remove('tm-notion-column-list__divider--active');
                    document.removeEventListener('mousemove', onMove);
                    document.removeEventListener('mouseup',   onUp);
                    restoreIframes();

                    const allCols   = Array.from(containerElement.querySelectorAll('[data-col-index]'))
                        .sort((a, b) => parseInt(a.dataset.colIndex) - parseInt(b.dataset.colIndex));
                    const totalWidth = containerElement.offsetWidth;
                    const widths     = allCols.map(c => parseFloat((c.offsetWidth / totalWidth * 100).toFixed(2)));

                    dotNetRef.invokeMethodAsync('OnColumnResized', widths).catch(console.error);
                };

                document.addEventListener('mousemove', onMove);
                document.addEventListener('mouseup',   onUp);
            };

            divider.addEventListener('mousedown', onDown);
            cleanups.push(() => divider.removeEventListener('mousedown', onDown));
        };

        containerElement.querySelectorAll('[data-col-divider]').forEach(attachDivider);

        _columnResizers.set(containerElement, { cleanup() { cleanups.forEach(fn => fn()); } });
    }

    function destroyColumnResize(containerElement) {
        if (!containerElement || !_columnResizers.has(containerElement)) return;
        _columnResizers.get(containerElement).cleanup();
        _columnResizers.delete(containerElement);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 26.9 — Scroll & navigation
    // ═══════════════════════════════════════════════════════════════════════════

    function scrollToBlock(blockId) {
        const el = document.querySelector(`[data-block-id="${CSS.escape(blockId)}"]`);
        setActiveTocItem(blockId);
        el?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }

    function setActiveTocItem(blockId) {
        document.querySelectorAll('.tm-toc__item--active, .tm-toc__item[aria-current="true"]').forEach(item => {
            item.classList.remove('tm-toc__item--active');
            item.removeAttribute('aria-current');
        });
        if (!blockId) return;

        const item = document.querySelector(`.tm-toc__item[data-toc-target-id="${CSS.escape(String(blockId))}"]`);
        if (!item) return;

        item.classList.add('tm-toc__item--active');
        item.setAttribute('aria-current', 'true');
    }

    // ── 91.1  Collaboration cursor overlays ──────────────────────────────────

    function updateCollabCursors(cursors) {
        // Clear previous markers
        document.querySelectorAll('[data-collab-user]').forEach(el => {
            el.classList.remove('tm-collab-active', 'tm-collab-active--overlap');
            el.removeAttribute('data-collab-user');
            el.removeAttribute('data-collab-count');
            el.style.removeProperty('--collab-color');
        });
        if (!cursors || !cursors.length) return;
        const byBlock = new Map();
        cursors.forEach(cursor => {
            if (!cursor || !cursor.blockId) return;
            const key = String(cursor.blockId);
            if (!byBlock.has(key)) byBlock.set(key, []);
            byBlock.get(key).push(cursor);
        });
        byBlock.forEach((blockCursors, blockId) => {
            const blockEl = document.querySelector(`[data-block-id="${CSS.escape(blockId)}"]`);
            if (!blockEl) return;
            const names = blockCursors
                .map(cursor => String(cursor.displayName || '').trim())
                .filter(Boolean);
            const first = blockCursors[0] || {};
            blockEl.classList.add('tm-collab-active');
            if (blockCursors.length > 1) blockEl.classList.add('tm-collab-active--overlap');
            blockEl.setAttribute('data-collab-user', names.join(', '));
            blockEl.setAttribute('data-collab-count', String(blockCursors.length));
            blockEl.style.setProperty('--collab-color', first.color || 'var(--tm-color-secondary)');
        });
    }

    function clearCollabCursors() {
        document.querySelectorAll('[data-collab-user]').forEach(el => {
            el.classList.remove('tm-collab-active', 'tm-collab-active--overlap');
            el.removeAttribute('data-collab-user');
            el.removeAttribute('data-collab-count');
            el.style.removeProperty('--collab-color');
        });
    }

    function _isEditableShortcutTarget(target) {
        const el = target instanceof Element ? target : target?.parentElement;
        return !!el?.closest('input, textarea, select, [contenteditable="true"], [contenteditable=""], [role="textbox"]');
    }

    function registerShortcuts(dotNetRef) {
        unregisterShortcuts();
        const handler = event => {
            if ((event.key !== '?' && event.key !== 'Escape') || _isEditableShortcutTarget(event.target)) return;
            if (event.key === '?') {
                event.preventDefault();
                dotNetRef.invokeMethodAsync('OnNotionShortcutKey', '?').catch(console.error);
                return;
            }
            dotNetRef.invokeMethodAsync('OnNotionShortcutKey', 'Escape').catch(console.error);
        };
        document.addEventListener('keydown', handler, true);
        document._tmNotionShortcutsHandler = handler;
    }

    function unregisterShortcuts() {
        const handler = document._tmNotionShortcutsHandler;
        if (!handler) return;
        document.removeEventListener('keydown', handler, true);
        delete document._tmNotionShortcutsHandler;
    }

    function initSmoothScrollSpy(containerElement, dotNetRef) {
        if (!containerElement) return;
        if (_scrollSpies.has(containerElement)) _scrollSpies.get(containerElement).cleanup();

        let ticking    = false;
        let activeId   = null;
        const OFFSET   = 80;

        const onScroll = () => {
            if (ticking) return;
            ticking = true;
            requestAnimationFrame(() => {
                ticking = false;
                const headings = Array.from(
                    containerElement.querySelectorAll('[data-notion-heading][data-block-id]')
                );
                let current = null;
                for (const h of headings) {
                    if (h.getBoundingClientRect().top <= OFFSET) current = h;
                }
                const newId = current?.dataset.blockId ?? null;
                if (newId !== activeId) {
                    activeId = newId;
                    setActiveTocItem(newId);
                    dotNetRef.invokeMethodAsync('OnScrollSpyBlockChanged', newId).catch(console.error);
                }
            });
        };

        const scrollTargets = new Set([
            containerElement,
            containerElement.closest('[data-notion-scroll-root]'),
            window
        ].filter(Boolean));

        scrollTargets.forEach(target => target.addEventListener('scroll', onScroll, { passive: true }));
        onScroll();

        _scrollSpies.set(containerElement, {
            cleanup() {
                scrollTargets.forEach(target => target.removeEventListener('scroll', onScroll));
            }
        });
    }

    function destroyScrollSpy(containerElement) {
        if (!containerElement || !_scrollSpies.has(containerElement)) return;
        _scrollSpies.get(containerElement).cleanup();
        _scrollSpies.delete(containerElement);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 30.1 — Cover image drag repositioning
    // ═══════════════════════════════════════════════════════════════════════════

    function startCoverDrag(coverElement, dotNetRef, startClientY, startPositionY) {
        if (!coverElement) return;

        let currentPos = startPositionY ?? 50;

        const onMouseMove = (e) => {
            const rect  = coverElement.getBoundingClientRect();
            const delta = e.clientY - startClientY;
            currentPos  = Math.max(0, Math.min(100, startPositionY - (delta / rect.height * 100)));
            coverElement.style.backgroundPositionY = currentPos.toFixed(1) + '%';
        };

        const onMouseUp = () => {
            document.removeEventListener('mousemove', onMouseMove);
            document.removeEventListener('mouseup',   onMouseUp);
            document.body.style.cursor = '';
            dotNetRef.invokeMethodAsync('OnCoverDragEnded', currentPos).catch(console.error);
        };

        document.body.style.cursor = 'ns-resize';
        document.addEventListener('mousemove', onMouseMove);
        document.addEventListener('mouseup',   onMouseUp);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 43.1 — Slash menu helpers
    // ═══════════════════════════════════════════════════════════════════════════

    const SLASH_RECENT_KEY  = 'tm-notion-slash-recent';
    const SLASH_RECENT_MAX  = 5;

    function getRecentSlashItems() {
        try {
            const raw = localStorage.getItem(SLASH_RECENT_KEY);
            return raw ? JSON.parse(raw) : [];
        } catch {
            return [];
        }
    }

    function addRecentSlashItem(blockTypeInt) {
        try {
            const existing = getRecentSlashItems().filter(v => v !== blockTypeInt);
            existing.unshift(blockTypeInt);
            localStorage.setItem(SLASH_RECENT_KEY, JSON.stringify(existing.slice(0, SLASH_RECENT_MAX)));
        } catch { /* private browsing / storage full */ }
    }

    function _slashTriggerRange() {
        if (!_slashElement) return null;
        const range = document.createRange();
        if (_slashAnchorNode && _slashElement.contains(_slashAnchorNode)) {
            range.setStart(_slashAnchorNode, _slashAnchorOff);
            if (_slashAnchorNode.nodeType === Node.TEXT_NODE) {
                range.setEnd(_slashAnchorNode, Math.min(_slashAnchorOff + 1, _slashAnchorNode.textContent.length));
            } else {
                const child = _slashAnchorNode.childNodes[_slashAnchorOff];
                if (child) range.setEndAfter(child);
                else range.setEnd(_slashAnchorNode, _slashAnchorOff);
            }
        } else {
            const walker = document.createTreeWalker(_slashElement, NodeFilter.SHOW_TEXT);
            let slashNode = null;
            let slashOffset = -1;
            while (walker.nextNode()) {
                const idx = walker.currentNode.textContent.lastIndexOf('/');
                if (idx >= 0) {
                    slashNode = walker.currentNode;
                    slashOffset = idx;
                }
            }

            if (slashNode) {
                range.setStart(slashNode, slashOffset);
                range.setEnd(slashNode, slashOffset + 1);
            } else {
                range.selectNodeContents(_slashElement);
                range.collapse(false);
            }
        }
        return range;
    }

    function _fragmentFromHtml(html) {
        const template = document.createElement('template');
        template.innerHTML = html || '';
        return template.content;
    }

    function _replaceRangeWithHtml(range, html) {
        const fragment = _fragmentFromHtml(html);
        const last = fragment.lastChild;
        range.deleteContents();
        range.insertNode(fragment);
        if (last) {
            const caret = document.createRange();
            caret.setStartAfter(last);
            caret.collapse(true);
            _applyRange(caret);
        }
        return last;
    }

    function insertSlashHtml(html) {
        if (!_slashElement) return;
        try {
            _slashElement.focus();
            const range = _slashTriggerRange();
            if (range) {
                _replaceRangeWithHtml(range, html);
                _slashElement.dispatchEvent(new Event('input', { bubbles: true }));
            }
        } catch { /* ignore edge cases */ }
        _slashElement = null;
        _slashAnchorNode = null;
        _slashAnchorOff = 0;
    }

    function clearSlashQuery() {
        if (!_slashElement) return;
        try {
            const sel   = window.getSelection();
            const range = _slashTriggerRange();
            if (!range) return;
            sel.removeAllRanges();
            sel.addRange(range);
            document.execCommand('delete');
        } catch { /* ignore edge cases */ }
        _slashElement    = null;
        _slashAnchorNode = null;
        _slashAnchorOff  = 0;
    }

    function _statusColorFromClass(chip) {
        const match = Array.from(chip.classList || [])
            .map(cls => cls.match(/^tm-notion-status--(gray|blue|green|yellow|red|purple)$/i))
            .find(Boolean);
        return match ? match[1].toLowerCase() : null;
    }

    function _previousMeaningfulSibling(node) {
        let current = node;
        while (current) {
            current = current.previousSibling;
            if (!current) return null;
            if (current.nodeType === Node.TEXT_NODE && current.textContent.length === 0) continue;
            return current;
        }
        return null;
    }

    function _statusChipBeforeCaret(element) {
        const range = _range();
        if (!range || !range.collapsed || !element.contains(range.startContainer)) return null;

        let candidate = null;
        if (range.startContainer.nodeType === Node.TEXT_NODE) {
            if (range.startOffset > 0) return null;
            candidate = _previousMeaningfulSibling(range.startContainer);
        } else {
            candidate = range.startContainer.childNodes[range.startOffset - 1] || null;
        }

        return candidate?.classList?.contains('tm-notion-status') ? candidate : null;
    }

    function replaceActiveStatusChip(html, blockId, chipIndex) {
        let chip = _statusChipBeingEdited
            || document.querySelector('.tm-notion-status[data-tm-status-editing="true"]');
        if ((!chip || !chip.isConnected) && blockId && Number.isInteger(chipIndex) && chipIndex >= 0) {
            const block = document.querySelector(`[data-block-id="${CSS.escape(blockId)}"] [contenteditable="true"]`);
            chip = block?.querySelectorAll('.tm-notion-status')?.[chipIndex] || null;
        }
        if (!chip) return;
        const editor = chip.closest('[contenteditable="true"]');
        if (!editor) return;

        try {
            const range = document.createRange();
            range.selectNode(chip);
            const inserted = _replaceRangeWithHtml(range, html);
            if (inserted) {
                const caret = document.createRange();
                caret.setStartAfter(inserted);
                caret.collapse(true);
                _applyRange(caret);
            }
            editor.dispatchEvent(new Event('input', { bubbles: true }));
        } catch { /* ignore edge cases */ }

        _statusChipBeingEdited = null;
        document.querySelectorAll('.tm-notion-status[data-tm-status-editing="true"]')
            .forEach(existing => delete existing.dataset.tmStatusEditing);
    }

    function cancelStatusEdit() {
        _statusChipBeingEdited = null;
        document.querySelectorAll('.tm-notion-status[data-tm-status-editing="true"]')
            .forEach(existing => delete existing.dataset.tmStatusEditing);
    }

    function refocusSlashElement() {
        if (_slashElement) {
            _setCursorAtEnd(_slashElement);
        }
        _slashElement    = null;
        _slashAnchorNode = null;
        _slashAnchorOff  = 0;
    }

    function insertMentionChip(mentionType, mentionId, displayText) {
        if (!_mentionElement) return;
        try {
            _mentionElement.focus();

            const sel   = window.getSelection();
            const range = document.createRange();
            if (_mentionAnchorNode && _mentionElement.contains(_mentionAnchorNode)) {
                range.setStart(_mentionAnchorNode, _mentionAnchorOff);
            } else {
                range.selectNodeContents(_mentionElement);
                range.setStart(_mentionElement, 0);
            }
            const endRange = document.createRange();
            endRange.selectNodeContents(_mentionElement);
            range.setEnd(endRange.endContainer, endRange.endOffset);

            sel.removeAllRanges();
            sel.addRange(range);
            document.execCommand('delete');

            const chip = document.createElement('span');
            chip.contentEditable = 'false';
            chip.className = 'tm-notion-mention tm-notion-mention--' + mentionType;
            chip.dataset.type = mentionType;
            chip.dataset.id   = String(mentionId);
            chip.textContent  = _truncateInlineChipText(displayText);
            chip.title        = String(displayText ?? '');
            chip.setAttribute('aria-label', String(displayText ?? ''));

            const curSel   = window.getSelection();
            const curRange = curSel.getRangeAt(0);
            curRange.insertNode(chip);
            curRange.setStartAfter(chip);
            curRange.collapse(true);
            curSel.removeAllRanges();
            curSel.addRange(curRange);

            document.execCommand('insertText', false, ' ');

            // Notify Blazor that content changed so it saves on next blur
            _mentionElement.dispatchEvent(new Event('input', { bubbles: true }));
        } catch { /* ignore edge cases */ }

        _mentionElement    = null;
        _mentionAnchorNode = null;
        _mentionAnchorOff  = 0;
        _mentionTriggerLen = 1;
    }

    function cancelMentionTrigger() {
        if (_mentionElement) {
            _mentionElement.focus();
            _setCursorAtEnd(_mentionElement);
        }
        _mentionElement    = null;
        _mentionAnchorNode = null;
        _mentionAnchorOff  = 0;
        _mentionTriggerLen = 1;
    }

    // ── Token insertion ────────────────────────────────────────────────────────

    function _createTokenChip(key, displayName, colorClass) {
        const chip = document.createElement('span');
        chip.contentEditable = 'false';
        chip.className = 'tm-notion-token' + (colorClass ? ' ' + colorClass : '');
        chip.dataset.key = key;
        if (!colorClass) {
            chip.style.background = 'var(--tm-color-primary-700)';
            chip.style.borderColor = 'var(--tm-color-primary-800)';
            chip.style.color = 'var(--tm-color-white)';
        }

        const text = document.createElement('span');
        text.className = 'tm-notion-token__text';
        text.textContent = '{{ ' + displayName + ' }}';

        const del = document.createElement('span');
        del.className = 'tm-notion-token__delete';
        del.setAttribute('aria-label', 'Remove token');
        del.setAttribute('role', 'button');
        del.textContent = '×';

        chip.appendChild(text);
        chip.appendChild(del);
        return chip;
    }

    function insertNotionToken(key, displayName, colorClass) {
        if (!_tokenElement) return;
        try {
            _tokenElement.focus();

            const sel   = window.getSelection();
            const range = document.createRange();
            if (_tokenAnchorNode && _tokenElement.contains(_tokenAnchorNode)) {
                range.setStart(_tokenAnchorNode, _tokenAnchorOff);
            } else {
                range.selectNodeContents(_tokenElement);
                range.setStart(_tokenElement, 0);
            }
            const endRange = document.createRange();
            endRange.selectNodeContents(_tokenElement);
            range.setEnd(endRange.endContainer, endRange.endOffset);

            sel.removeAllRanges();
            sel.addRange(range);
            document.execCommand('delete');

            const chip = _createTokenChip(key, displayName, colorClass);

            const curSel   = window.getSelection();
            const curRange = curSel.getRangeAt(0);
            curRange.insertNode(chip);
            curRange.setStartAfter(chip);
            curRange.collapse(true);
            curSel.removeAllRanges();
            curSel.addRange(curRange);

            document.execCommand('insertText', false, ' ');

            _tokenElement.dispatchEvent(new Event('input', { bubbles: true }));
        } catch { /* ignore edge cases */ }

        _tokenElement    = null;
        _tokenAnchorNode = null;
        _tokenAnchorOff  = 0;
    }

    function replaceNotionToken(key, displayName, colorClass) {
        if (!_chipBeingEdited) return;
        const old = _chipBeingEdited;
        _chipBeingEdited = null;
        const chip = _createTokenChip(key, displayName, colorClass);
        old.parentNode?.replaceChild(chip, old);
        const inputEl = chip.closest('[contenteditable="true"]');
        if (inputEl) inputEl.dispatchEvent(new Event('input', { bubbles: true }));
    }

    function cancelChipEdit() {
        _chipBeingEdited = null;
    }

    function cancelTokenTrigger() {
        if (_tokenElement) {
            _tokenElement.focus();
            _setCursorAtEnd(_tokenElement);
        }
        _tokenElement    = null;
        _tokenAnchorNode = null;
        _tokenAnchorOff  = 0;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 26.11 — Mention click handler (delegated)
    // ═══════════════════════════════════════════════════════════════════════════

    const _mentionClickHandlers = new WeakMap();

    function initMentionClickHandler(containerElement, dotNetRef) {
        if (!containerElement) return;
        destroyMentionClickHandler(containerElement);
        const handler = (e) => {
            const mention = e.target.closest('.tm-mention');
            if (!mention) return;
            const userId = mention.getAttribute('data-user-id');
            if (!userId) return;
            e.stopPropagation();
            dotNetRef.invokeMethodAsync('OnMentionClicked', userId).catch(() => {});
        };
        containerElement.addEventListener('click', handler);
        _mentionClickHandlers.set(containerElement, handler);
    }

    function destroyMentionClickHandler(containerElement) {
        if (!containerElement) return;
        const handler = _mentionClickHandlers.get(containerElement);
        if (handler) {
            containerElement.removeEventListener('click', handler);
            _mentionClickHandlers.delete(containerElement);
        }
    }

    function getMentionDropdownPosition(element) {
        if (!element) return { top: 0, left: 0, width: 0 };
        const rect = element.getBoundingClientRect();
        return {
            top: rect.bottom,
            left: rect.left,
            width: rect.width
        };
    }

    function clampFixedElementToViewport(element, margin) {
        if (!element) return;

        const spacing = Number.isFinite(Number(margin)) ? Number(margin) : 8;
        const viewportWidth = window.innerWidth || document.documentElement.clientWidth || 0;
        const viewportHeight = window.innerHeight || document.documentElement.clientHeight || 0;
        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        element.style.maxHeight = `${Math.max(120, viewportHeight - spacing * 2)}px`;

        const rect = element.getBoundingClientRect();
        const width = rect.width || element.offsetWidth || 0;
        const height = rect.height || element.offsetHeight || 0;
        const currentTop = Number.parseFloat(element.style.top);
        const currentLeft = Number.parseFloat(element.style.left);
        const rawTop = Number.isFinite(currentTop) ? currentTop : rect.top;
        const rawLeft = Number.isFinite(currentLeft) ? currentLeft : rect.left;

        const maxTop = Math.max(spacing, viewportHeight - height - spacing);
        const maxLeft = Math.max(spacing, viewportWidth - width - spacing);
        const nextTop = Math.min(Math.max(spacing, rawTop), maxTop);
        const nextLeft = Math.min(Math.max(spacing, rawLeft), maxLeft);

        element.style.top = `${nextTop}px`;
        element.style.left = `${nextLeft}px`;
    }

    function adjustSlashMenuPosition(menuEl) {
        if (!menuEl) return;

        const vw        = window.innerWidth;
        const vh        = window.innerHeight;
        const margin    = 8;
        const anchorGap = 28;

        menuEl.style.maxHeight = '';

        const initialRect = menuEl.getBoundingClientRect();
        const anchorTop   = Number.parseFloat(menuEl.style.top) || initialRect.top || margin;
        let top           = anchorTop;
        let left          = Number.parseFloat(menuEl.style.left) || initialRect.left || margin;
        const height      = Math.min(initialRect.height || 0, vh - (margin * 2));
        const below       = Math.max(96, vh - anchorTop - margin);
        const above       = Math.max(96, anchorTop - margin - anchorGap);
        let maxHeight     = Math.min(height, below);

        if (anchorTop + height > vh - margin && above > below) {
            maxHeight = Math.min(height, above);
            top = Math.max(margin, anchorTop - maxHeight - anchorGap);
        } else if (anchorTop + height > vh - margin) {
            top = Math.max(margin, vh - margin - maxHeight);
        }

        if (initialRect.right > vw - margin) {
            left = vw - initialRect.width - margin;
        }

        top = Math.max(margin, top);
        left = Math.max(margin, left);
        maxHeight = Math.max(96, Math.min(maxHeight, vh - top - margin));

        const listEl = menuEl.querySelector('.tm-nmm__list, .tm-notion-token-dropdown__list');
        if (listEl) {
            menuEl.style.maxHeight = `${maxHeight}px`;
            menuEl.style.setProperty('--tm-floating-menu-max-height', `${maxHeight}px`);

            const chromeHeight = Array.from(menuEl.children)
                .filter(child => child !== listEl)
                .reduce((total, child) => total + child.getBoundingClientRect().height, 0);
            const listMaxHeight = Math.max(64, maxHeight - chromeHeight);
            listEl.style.maxHeight = `${listMaxHeight}px`;
            listEl.style.overflowY = 'auto';
        } else {
            menuEl.style.maxHeight = '';
            menuEl.style.removeProperty('--tm-floating-menu-max-height');
        }

        menuEl.style.top = `${top}px`;
        menuEl.style.left = `${left}px`;
        menuEl.style.setProperty('--tm-nmm-anchor-top', `${top}px`);
    }

    // 47.1
    function adjustTypeSwitcherPosition(panelEl) {
        if (!panelEl) return;
        const rect   = panelEl.getBoundingClientRect();
        const vw     = window.innerWidth;
        const vh     = window.innerHeight;
        const margin = 8;

        let top  = parseFloat(panelEl.style.top)  || 0;
        let left = parseFloat(panelEl.style.left) || 0;

        if (rect.bottom > vh - margin) {
            top = top - rect.height - 4;
            panelEl.style.top = Math.max(margin, top) + 'px';
        }
        if (rect.right > vw - margin) {
            left = vw - rect.width - margin;
            panelEl.style.left = Math.max(margin, left) + 'px';
        }
    }

    // 46.1
    function getRecentEmojis() {
        try {
            const raw = localStorage.getItem('tm-notion-emoji-recent');
            return raw ? JSON.parse(raw) : [];
        } catch { return []; }
    }

    function addRecentEmoji(emoji) {
        try {
            const recent = getRecentEmojis().filter(e => e !== emoji);
            recent.unshift(emoji);
            localStorage.setItem('tm-notion-emoji-recent', JSON.stringify(recent.slice(0, 16)));
        } catch { }
    }

    function adjustEmojiPickerPosition(pickerEl) {
        if (!pickerEl) return;
        const rect   = pickerEl.getBoundingClientRect();
        const vw     = window.innerWidth;
        const vh     = window.innerHeight;
        const margin = 8;

        let top  = parseFloat(pickerEl.style.top)  || 0;
        let left = parseFloat(pickerEl.style.left) || 0;

        if (rect.bottom > vh - margin) {
            top = top - rect.height - 4;
            pickerEl.style.top = Math.max(margin, top) + 'px';
        }
        if (rect.right > vw - margin) {
            left = vw - rect.width - margin;
            pickerEl.style.left = Math.max(margin, left) + 'px';
        }
    }

    // 45.1
    function adjustColorPickerPosition(pickerEl) {
        if (!pickerEl) return;
        const rect   = pickerEl.getBoundingClientRect();
        const vw     = window.innerWidth;
        const vh     = window.innerHeight;
        const margin = 8;

        let top  = parseFloat(pickerEl.style.top)  || 0;
        let left = parseFloat(pickerEl.style.left) || 0;

        if (rect.bottom > vh - margin) {
            top = top - rect.height - 4;
            pickerEl.style.top = Math.max(margin, top) + 'px';
        }
        if (rect.right > vw - margin) {
            left = vw - rect.width - margin;
            pickerEl.style.left = Math.max(margin, left) + 'px';
        }
    }

    function scrollSlashItemIntoView(listEl, flatIndex) {
        if (!listEl) return;
        const el = listEl.querySelector(`[data-slash-idx="${flatIndex}"]`);
        el?.scrollIntoView({ block: 'nearest' });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 44.1 — Inline formatting toolbar
    // ═══════════════════════════════════════════════════════════════════════════

    const _selectionWatchers = new WeakMap(); // pageEl → { dotNetRef, listeners: [] }

    // Saved selection range for link insertion (focus moves to URL input, losing selection)
    let _savedRange = null;

    // Prevent toolbar buttons from stealing focus so the editor selection is preserved
    document.addEventListener('mousedown', function (e) {
        const toolbarEl = document.querySelector('.tm-notion-inline-toolbar');
        if (!toolbarEl || !toolbarEl.contains(e.target)) return;
        // Allow the link URL input to receive focus normally
        if (e.target.closest('.tm-notion-inline-toolbar__link-input')) return;
        e.preventDefault();
        if (e.target.closest('.tm-notion-inline-toolbar__link-remove')) return;
        saveSelection();
    }, true);

    function initSelectionWatcher(pageEl, dotNetRef) {
        if (!pageEl) return;
        if (_selectionWatchers.has(pageEl)) destroySelectionWatcher(pageEl);

        const listeners = [];
        let _lastToolbarMouseDown = 0;

        function _notify() {
            // Don't clear toolbar while user interacts with it (e.g. link input)
            const toolbarEl = document.querySelector('.tm-notion-inline-toolbar');
            if (toolbarEl && toolbarEl.contains(document.activeElement)) return;

            // Ignore selection changes for 500ms after a toolbar mousedown
            // (browser hasn't moved focus yet when selectionchange fires)
            if (Date.now() - _lastToolbarMouseDown < 500) return;

            const sel = window.getSelection();
            if (!sel || sel.isCollapsed || sel.rangeCount === 0) {
                dotNetRef.invokeMethodAsync('OnToolbarSelectionCleared').catch(() => {});
                return;
            }
            const range = sel.getRangeAt(0);
            if (!pageEl.contains(range.commonAncestorContainer)) {
                dotNetRef.invokeMethodAsync('OnToolbarSelectionCleared').catch(() => {});
                return;
            }
            const selectionHost = range.commonAncestorContainer.nodeType === Node.TEXT_NODE
                ? range.commonAncestorContainer.parentElement?.closest('[contenteditable="true"]')
                : range.commonAncestorContainer.closest?.('[contenteditable="true"]');
            if (selectionHost?.closest?.('.tm-notion-table-block')) {
                dotNetRef.invokeMethodAsync('OnToolbarSelectionCleared').catch(() => {});
                return;
            }
            const selectionRects = Array.from(range.getClientRects())
                .filter(r => r.width > 0 && r.height > 0);
            let rect = selectionRects[0] ?? range.getBoundingClientRect();
            if (rect.width === 0 && selectionHost && (sel.toString() || selectionHost.textContent || '').trim().length > 0) {
                rect = selectionHost.getBoundingClientRect();
            }
            if (rect.width === 0) {
                dotNetRef.invokeMethodAsync('OnToolbarSelectionCleared').catch(() => {});
                return;
            }

            const blockEl = range.commonAncestorContainer.nodeType === Node.TEXT_NODE
                ? range.commonAncestorContainer.parentElement?.closest('[data-notion-block]')
                : range.commonAncestorContainer.closest?.('[data-notion-block]');

            const blockId = blockEl?.dataset?.blockId || blockEl?.dataset?.notionBlock || '';
            const isBold          = document.queryCommandState('bold');
            const isItalic        = document.queryCommandState('italic');
            const isUnderline     = document.queryCommandState('underline');
            const isStrikeThrough = document.queryCommandState('strikeThrough');
            // Robust link detection: works when anchorNode is inside <a> or when
            // the selection spans an element that contains an <a>.
            let linkEl = null;
            if (sel.anchorNode) {
                const node = sel.anchorNode.nodeType === Node.TEXT_NODE ? sel.anchorNode.parentElement : sel.anchorNode;
                linkEl = node?.closest?.('a');
            }
            if (!linkEl && range) {
                const container = range.commonAncestorContainer;
                if (container.nodeType === Node.ELEMENT_NODE) {
                    linkEl = container.querySelector?.('a');
                }
            }
            const currentHref = linkEl?.href ?? '';

            // Detect inline code by checking if selection is within a <code> element.
            const codeEl   = _closestInlineCode(sel.anchorNode);
            const isCode   = !!codeEl && !codeEl.closest('pre');

            // Toolbar appears just above the selection
            const top  = rect.top - 40;
            const left = rect.left + rect.width / 2 - 160;

            dotNetRef.invokeMethodAsync('OnToolbarSelectionChanged',
                top, left, isBold, isItalic, isUnderline, isStrikeThrough, isCode,
                currentHref, blockId, sel.toString() || ''
            ).catch(() => {});
        }

        let _timer = 0;
        const onUp = () => { clearTimeout(_timer); _timer = setTimeout(_notify, 10); };

        listeners.push(
            _on(document, 'mousedown', (e) => {
                const toolbarEl = document.querySelector('.tm-notion-inline-toolbar');
                if (toolbarEl && toolbarEl.contains(e.target)) {
                    _lastToolbarMouseDown = Date.now();
                }
            }),
            _on(document, 'mouseup',  onUp),
            _on(document, 'keyup',    onUp),
            _on(document, 'selectionchange', onUp)
        );

        _selectionWatchers.set(pageEl, { dotNetRef, listeners, notify: _notify });
    }

    function destroySelectionWatcher(pageEl) {
        if (!pageEl) return;
        const state = _selectionWatchers.get(pageEl);
        if (!state) return;
        _offAll(state.listeners);
        _selectionWatchers.delete(pageEl);
    }

    function hasSelectionWatcher(pageEl) {
        return !!pageEl && _selectionWatchers.has(pageEl);
    }

    function notifySelectionChanged(pageEl) {
        const state = pageEl ? _selectionWatchers.get(pageEl) : null;
        if (state?.notify) state.notify();
    }

    async function forceInlineToolbarForSelection(pageEl) {
        window.__tmNotionLastToolbarError = null;
        const dotNetRef = (pageEl ? _selectionWatchers.get(pageEl)?.dotNetRef : null) ?? _pageDotNetRef;
        if (!dotNetRef) return false;

        const sel = window.getSelection();
        if (!sel || sel.isCollapsed || sel.rangeCount === 0) return false;

        const range = sel.getRangeAt(0);
        const host = _selectionElement(range.commonAncestorContainer)?.closest?.('[contenteditable="true"]');
        const rects = Array.from(range.getClientRects())
            .filter(r => r.width > 0 && r.height > 0);
        let rect = rects[0] ?? range.getBoundingClientRect();
        if ((!rect || rect.width === 0) && host) {
            rect = host.getBoundingClientRect();
        }

        if (!rect) return false;

        const blockEl = _selectionElement(range.commonAncestorContainer)?.closest?.('[data-notion-block]');
        const blockId = blockEl?.dataset?.blockId || blockEl?.dataset?.notionBlock || '';
        const linkEl = _selectionClosest(sel, range, 'a') ?? (
            range.commonAncestorContainer.nodeType === Node.ELEMENT_NODE
                ? range.commonAncestorContainer.querySelector?.('a')
                : null
        );
        const codeEl = _selectionClosest(sel, range, 'code');
        const selectedText = sel.toString() || '';
        const top = rect.top - 40;
        const left = rect.left + rect.width / 2 - 160;

        try {
            await dotNetRef.invokeMethodAsync(
                'OnToolbarSelectionChanged',
                top,
                left,
                _selectionHasFormat(sel, range, 'strong,b', 'bold'),
                _selectionHasFormat(sel, range, 'em,i', 'italic'),
                _selectionHasFormat(sel, range, 'u', 'underline'),
                _selectionHasFormat(sel, range, 's,strike,del', 'line-through'),
                !!codeEl && !codeEl.closest('pre'),
                linkEl?.href ?? '',
                blockId,
                selectedText
            );
            return true;
        } catch (error) {
            window.__tmNotionLastToolbarError = String(error?.message || error || 'Unknown toolbar interop error');
            return false;
        }
    }

    function _selectionElement(node) {
        if (!node) return null;
        return node.nodeType === Node.TEXT_NODE ? node.parentElement : node;
    }

    function _selectionClosest(sel, range, selector) {
        return _selectionElement(sel.anchorNode)?.closest?.(selector)
            ?? _selectionElement(sel.focusNode)?.closest?.(selector)
            ?? _selectionElement(range.commonAncestorContainer)?.closest?.(selector);
    }

    function _selectionHasFormat(sel, range, selector, command) {
        try {
            if (command === 'bold' && document.queryCommandState('bold')) return true;
            if (command === 'italic' && document.queryCommandState('italic')) return true;
            if (command === 'underline' && document.queryCommandState('underline')) return true;
            if (command === 'line-through' && document.queryCommandState('strikeThrough')) return true;
        } catch { }

        const formatted = _selectionClosest(sel, range, selector);
        if (formatted) return true;

        const host = _selectionElement(sel.anchorNode);
        if (!host) return false;
        const style = getComputedStyle(host);
        if (command === 'bold') {
            const weight = Number.parseInt(style.fontWeight || '400', 10);
            return Number.isFinite(weight) && weight >= 600;
        }
        if (command === 'italic') return style.fontStyle === 'italic';
        if (command === 'underline') return style.textDecorationLine.includes('underline');
        if (command === 'line-through') return style.textDecorationLine.includes('line-through');
        return false;
    }

    function saveSelection() {
        _savedRange = _range() ? _range().cloneRange() : null;
    }

    function restoreSavedSelection() {
        if (_savedRange) {
            _applyRange(_savedRange);
        }
    }

    function insertLinkOnSavedSelection(url) {
        if (!_savedRange) return;
        _applyRange(_savedRange);
        _savedRange = null;
        const label = _escHtml(window.getSelection()?.toString() || url);
        const href  = _escHtml(url);
        document.execCommand('insertHTML', false,
            `<a href="${href}" target="_blank" rel="noopener noreferrer">${label}</a>`);
    }

    function _closestInlineCode(node) {
        if (!node) return null;
        const element = node.nodeType === Node.TEXT_NODE
            ? node.parentElement
            : (node.nodeType === Node.ELEMENT_NODE ? node : null);
        return element?.closest?.('code') ?? null;
    }

    /**
     * Strips one colour property from every element touching the selection, and unwraps the
     * element when nothing else is left on it. execCommand('removeFormat') would also strip
     * bold, italic and links, which is not what "Default" means.
     */
    function _clearInlineColor(property) {
        const selection = window.getSelection();
        if (!selection || selection.rangeCount === 0) return;

        const range = selection.getRangeAt(0);
        const root = range.commonAncestorContainer.nodeType === Node.ELEMENT_NODE
            ? range.commonAncestorContainer
            : range.commonAncestorContainer.parentElement;
        if (!root) return;

        const host = root.closest('[contenteditable="true"]') ?? root;
        const candidates = [host, ...host.querySelectorAll('*')]
            .filter(el => el.nodeType === Node.ELEMENT_NODE && el !== host && range.intersectsNode(el));

        const cssProperty = property === 'color' ? 'color' : 'background-color';

        for (const el of candidates) {
            if (!el.style || !el.style.getPropertyValue(cssProperty)) continue;

            el.style.removeProperty(cssProperty);
            if (!el.getAttribute('style')) el.removeAttribute('style');

            // A span that carried nothing but the colour has no reason to exist any more.
            if (el.tagName === 'SPAN' && el.attributes.length === 0) {
                const parent = el.parentNode;
                while (el.firstChild) parent.insertBefore(el.firstChild, el);
                parent.removeChild(el);
            }
        }

        host.normalize();
        host.dispatchEvent(new Event('input', { bubbles: true }));
    }

    function applyInlineColor(scope, colorValue) {
        // Without this the browser emits the deprecated <font color> element, which the block
        // sanitizer drops on save — the colour would vanish on the next reload.
        document.execCommand('styleWithCSS', false, true);

        if (scope === 'text') {
            if (!colorValue) {
                _clearInlineColor('color');
            } else {
                document.execCommand('foreColor', false, colorValue);
            }
        } else {
            if (!colorValue) {
                _clearInlineColor('backgroundColor');
            } else {
                document.execCommand('hiliteColor', false, colorValue);
            }
        }
    }

    /**
     * Blazor only marks a block dirty from its `oninput` handler, and DOM surgery done from
     * JS fires no such event. Without this the change is silently dropped on blur.
     */
    function _notifyInput(node) {
        const host = (node?.nodeType === 1 ? node : node?.parentElement)
            ?.closest('[contenteditable="true"]');
        host?.dispatchEvent(new Event('input', { bubbles: true }));
    }

    function toggleInlineCode() {
        const sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return;
        const anchor = sel.anchorNode;
        const r = sel.getRangeAt(0);
        const codeEl = _closestInlineCode(sel.anchorNode);
        if (codeEl && !codeEl.closest('pre')) {
            // Unwrap: replace <code> with its text content
            const parent = codeEl.parentNode;
            while (codeEl.firstChild) parent.insertBefore(codeEl.firstChild, codeEl);
            parent.removeChild(codeEl);
        } else {
            // Wrap selection in <code>
            const code = document.createElement('code');
            try {
                r.surroundContents(code);
            } catch {
                const frag = r.extractContents();
                code.appendChild(frag);
                r.insertNode(code);
            }
        }

        _notifyInput(anchor);
    }

    function insertInlineMath() {
        const sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return;
        const anchor = sel.anchorNode;
        const r     = sel.getRangeAt(0);
        const expr  = sel.toString().trim() || 'x';
        const span  = document.createElement('span');
        span.className = 'tm-notion-inline-math';
        span.dataset.expr = expr;
        span.textContent  = expr;
        try {
            r.deleteContents();
            r.insertNode(span);
        } catch { /* ignore */ }

        _notifyInput(anchor);
    }

    function adjustInlineToolbarPosition(toolbarEl) {
        if (!toolbarEl) return;
        const rect   = toolbarEl.getBoundingClientRect();
        const vw     = window.innerWidth;
        const vh     = window.innerHeight;
        const margin = 8;
        if (rect.right > vw - margin) {
            const shift = rect.right - (vw - margin);
            toolbarEl.style.left = (parseFloat(toolbarEl.style.left) - shift) + 'px';
        }
        if (rect.left < margin) {
            toolbarEl.style.left = margin + 'px';
        }
        let nextRect = toolbarEl.getBoundingClientRect();
        const selectionTop = getSelectionViewportTop();
        if (Number.isFinite(selectionTop) && nextRect.bottom > selectionTop - margin) {
            const aboveSelectionTop = selectionTop - nextRect.height - margin;
            toolbarEl.style.top = Math.max(margin, aboveSelectionTop) + 'px';
            nextRect = toolbarEl.getBoundingClientRect();
        }
        if (nextRect.bottom > vh - margin) {
            const shift = nextRect.bottom - (vh - margin);
            toolbarEl.style.top = (parseFloat(toolbarEl.style.top) - shift) + 'px';
        }
        if (toolbarEl.getBoundingClientRect().top < margin) {
            toolbarEl.style.top = margin + 'px';
        }
    }

    function getSelectionViewportTop() {
        const sel = window.getSelection();
        if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return Number.NaN;

        const range = sel.getRangeAt(0);
        const rects = Array.from(range.getClientRects())
            .filter(r => r.width > 0 && r.height > 0);
        if (rects.length > 0) {
            return Math.min(...rects.map(r => r.top));
        }

        const rect = range.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0 ? rect.top : Number.NaN;
    }

    function getSelectedText() {
        const sel = window.getSelection();
        return sel ? (sel.toString() || '') : '';
    }

    function replaceSavedSelectionWithHtml(html) {
        if (!_savedRange || typeof html !== 'string') return;

        const range = _savedRange.cloneRange();
        range.deleteContents();

        const template = document.createElement('template');
        template.innerHTML = html;
        const fragment = template.content;
        const lastNode = fragment.lastChild;
        range.insertNode(fragment);

        const sel = window.getSelection();
        if (sel) {
            sel.removeAllRanges();
            const nextRange = document.createRange();
            if (lastNode) {
                nextRange.setStartAfter(lastNode);
            } else {
                nextRange.setStart(range.endContainer, range.endOffset);
            }
            nextRange.collapse(true);
            sel.addRange(nextRange);
        }

        const block = range.startContainer.nodeType === Node.TEXT_NODE
            ? range.startContainer.parentElement?.closest?.('[data-notion-block]')
            : range.startContainer.closest?.('[data-notion-block]');
        block?.dispatchEvent(new Event('input', { bubbles: true }));
        _savedRange = null;
    }

    // ── 32.1 Copy block link ───────────────────────────────────────────────────

    function copyBlockLink(fragment) {
        const url = window.location.href.split('#')[0] + fragment;
        navigator.clipboard.writeText(url).catch(() => {});
    }

    // ── 62.1 Copy plain text to clipboard (synced block sync ID) ──────────────

    function copyText(text) {
        if (navigator.clipboard?.writeText) {
            navigator.clipboard.writeText(String(text)).catch(() => {});
        }
    }

    // ── 37.1 Code block keyboard handler ──────────────────────────────────────

    const CODE_TAB_SIZE = 4;

    function _autoResizeTextarea(ta) {
        ta.style.height = 'auto';
        ta.style.height = ta.scrollHeight + 'px';
    }

    function initCodeKeyboardHandler(textarea, dotNetRef) {
        if (!textarea) return;
        if (_blocks.has(textarea)) destroyBlock(textarea);
        _blocks.set(textarea, { dotNetRef, listeners: [] });
        const state = _blocks.get(textarea);

        const onKeyDown = (e) => {
            if (e.key === 'Tab') {
                e.preventDefault();
                const start = textarea.selectionStart;
                const end   = textarea.selectionEnd;
                const val   = textarea.value;

                if (e.shiftKey) {
                    const lineStart = val.lastIndexOf('\n', start - 1) + 1;
                    const spaces    = val.slice(lineStart).match(/^ {1,4}/)?.[0] ?? '';
                    if (spaces.length > 0) {
                        textarea.value = val.slice(0, lineStart) + val.slice(lineStart + spaces.length);
                        textarea.selectionStart = textarea.selectionEnd = Math.max(lineStart, start - spaces.length);
                    }
                } else {
                    const indent = ' '.repeat(CODE_TAB_SIZE);
                    textarea.value = val.slice(0, start) + indent + val.slice(end);
                    textarea.selectionStart = textarea.selectionEnd = start + CODE_TAB_SIZE;
                }
                textarea.dispatchEvent(new Event('input', { bubbles: true }));
                _autoResizeTextarea(textarea);
                return;
            }

            if (e.key === 'Backspace' && !textarea.value.trim()) {
                e.preventDefault();
                dotNetRef.invokeMethodAsync('OnBackspaceOnEmpty').catch(console.error);
                return;
            }
        };

        const onInput = () => _autoResizeTextarea(textarea);

        state.listeners.push(
            _on(textarea, 'keydown', onKeyDown),
            _on(textarea, 'input',   onInput)
        );
    }

    function getCode(textarea) {
        return textarea ? textarea.value : '';
    }

    function setCode(textarea, code) {
        if (!textarea) return;
        textarea.value = code ?? '';
        _autoResizeTextarea(textarea);
    }

    // ── 58.1 Table block — cell keyboard handler & focus ──────────────────────

    function initTableRowKeyboardHandler(rowEl, dotNetRef, columnCount) {
        if (!rowEl) return;
        if (_blocks.has(rowEl)) destroyBlock(rowEl);
        _blocks.set(rowEl, { dotNetRef, listeners: [], columnCount });

        const onKeyDown = (e) => {
            if (e.key !== 'Tab') return;
            const cell = e.target.closest('[data-tm-col]');
            if (!cell) return;
            const colIdx = parseInt(cell.dataset.tmCol, 10);
            const count  = _blocks.get(rowEl)?.columnCount ?? columnCount;
            e.preventDefault();
            if (e.shiftKey) {
                if (colIdx === 0) {
                    dotNetRef.invokeMethodAsync('InvokeShiftTabFromFirstCell').catch(console.error);
                } else {
                    const prev = rowEl.querySelector(`[data-tm-col="${colIdx - 1}"] [contenteditable]`);
                    if (prev) { prev.focus(); _setCursorAtEnd(prev); }
                }
            } else {
                if (colIdx === count - 1) {
                    dotNetRef.invokeMethodAsync('InvokeTabFromLastCell').catch(console.error);
                } else {
                    const next = rowEl.querySelector(`[data-tm-col="${colIdx + 1}"] [contenteditable]`);
                    if (next) { next.focus(); _setCursorAtStart(next); }
                }
            }
        };

        _blocks.get(rowEl).listeners.push(_on(rowEl, 'keydown', onKeyDown));
    }

    function destroyTableRowKeyboardHandler(rowEl) {
        destroyBlock(rowEl);
    }

    function tableFocusCell(tableEl, rowIdx, colIdx) {
        if (!tableEl) return;
        const td = tableEl.querySelector(`[data-tm-row="${rowIdx}"][data-tm-col="${colIdx}"]`);
        const editable = td?.querySelector('[contenteditable]');
        if (editable) { editable.focus(); _setCursorAtEnd(editable); }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 80.1 — Sidebar resize
    // ═══════════════════════════════════════════════════════════════════════════

    const _sidebarResizes = new WeakMap();
    const TM_SIDEBAR_WIDTH_KEY = 'tm-notion-sidebar-width';

    function initSidebarResize(handleEl, dotNetRef, minWidth, maxWidth) {
        if (!handleEl) return;
        if (_sidebarResizes.has(handleEl)) _sidebarResizes.get(handleEl).cleanup();

        const aside = handleEl.closest('.tm-notion-sidebar');
        if (!aside) return;

        const saved = parseInt(localStorage.getItem(TM_SIDEBAR_WIDTH_KEY), 10);
        if (saved >= minWidth && saved <= maxWidth) aside.style.width = saved + 'px';

        let active = false, startX = 0, startW = 0;

        const onDown = (e) => {
            e.preventDefault();
            active  = true;
            startX  = e.clientX;
            startW  = aside.offsetWidth;
            document.body.style.cursor     = 'ew-resize';
            document.body.style.userSelect = 'none';
        };

        const onMove = (e) => {
            if (!active) return;
            const w = Math.min(maxWidth, Math.max(minWidth, startW + e.clientX - startX));
            aside.style.width = w + 'px';
        };

        const onUp = () => {
            if (!active) return;
            active = false;
            document.body.style.cursor     = '';
            document.body.style.userSelect = '';
            const w = aside.offsetWidth;
            localStorage.setItem(TM_SIDEBAR_WIDTH_KEY, w);
            dotNetRef?.invokeMethodAsync('OnSidebarResized', w).catch(() => {});
        };

        handleEl.addEventListener('mousedown', onDown);
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup',   onUp);

        _sidebarResizes.set(handleEl, {
            cleanup() {
                handleEl.removeEventListener('mousedown', onDown);
                document.removeEventListener('mousemove', onMove);
                document.removeEventListener('mouseup',   onUp);
            }
        });
    }

    function destroySidebarResize(handleEl) {
        if (!handleEl || !_sidebarResizes.has(handleEl)) return;
        _sidebarResizes.get(handleEl).cleanup();
        _sidebarResizes.delete(handleEl);
    }

    // ── 87.1 Page Search (Ctrl+P / Cmd+P) ────────────────────────────────────

    let _pageSearchDotNet   = null;
    let _pageSearchListener = null;

    function registerPageSearch(dotNetRef) {
        destroyPageSearch();
        _pageSearchDotNet = dotNetRef;
        _pageSearchListener = function (e) {
            if ((e.ctrlKey || e.metaKey) && e.key === 'p' && !e.shiftKey && !e.altKey) {
                e.preventDefault();
                _pageSearchDotNet.invokeMethodAsync('OpenPageSearch').catch(console.error);
            }
        };
        document.addEventListener('keydown', _pageSearchListener, true);
    }

    function destroyPageSearch() {
        if (_pageSearchListener) {
            document.removeEventListener('keydown', _pageSearchListener, true);
            _pageSearchListener = null;
        }
        _pageSearchDotNet = null;
    }

    // ── 88.1 Page Settings Helpers ────────────────────────────────────────────

    async function downloadFileStream(fileName, contentStreamRef, mimeType) {
        const buf  = await contentStreamRef.arrayBuffer();
        const blob = new Blob([buf], { type: mimeType || 'application/octet-stream' });
        const url  = URL.createObjectURL(blob);
        const a    = document.createElement('a');
        a.href     = url;
        a.download = fileName;
        a.style.display = 'none';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        setTimeout(() => URL.revokeObjectURL(url), 15000);
    }

    async function copyToClipboard(text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            await navigator.clipboard.writeText(text);
        } else {
            const el = document.createElement('textarea');
            el.value = text;
            el.style.position = 'fixed';
            el.style.opacity  = '0';
            document.body.appendChild(el);
            el.select();
            document.execCommand('copy');
            document.body.removeChild(el);
        }
    }

    function getPageUrl(pageId) {
        return window.location.origin + window.location.pathname + '#' + pageId;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 26.10 — Scroll to first unresolved comment (header badge click)
    // ═══════════════════════════════════════════════════════════════════════════

    function scrollToFirstUnresolvedComment() {
        const first = document.querySelector('.tm-notion-block__comment-thread');
        if (!first) return;
        const block = first.closest('[data-notion-block]');
        if (!block) return;
        block.scrollIntoView({ behavior: 'smooth', block: 'center' });
        // Add a brief highlight flash
        block.classList.add('tm-notion-block--highlight-flash');
        setTimeout(() => block.classList.remove('tm-notion-block--highlight-flash'), 1200);
    }

    function isNarrowViewport(maxWidth) {
        const width = Number(maxWidth) || 1024;
        return window.matchMedia(`(max-width: ${width}px)`).matches;
    }

    // ── Public API ─────────────────────────────────────────────────────────────
    return {
        // 26.1
        initBlock, destroyBlock, getHtml, getEditableHtml, getCaretOffset, setCaretOffset, setHtml,
        focus, focusAtEnd, focusAtStart, focusAtOffset,
        initFocusTrap, destroyFocusTrap, positionContextMenu, focusFirstMenuItem,
        initEditorKeyHandler, destroyEditorKeyHandler,
        // 26.2
        getSelectionRange, getSelectionRect, applyFormat,
        queryFormatState, insertHtml, insertLink, wrapSelectionWithComment,
        unwrapCommentHighlight, setCommentHighlightActive, registerPageDotNetRef,
        getBlockBoundingRect,
        // 26.3
        initDragDrop, destroyDragDrop,
        // 26.4
        getCaretCoords, getTextBeforeCaret,
        // 26.5
        initKeyboardHandler, insertSmartLinkChip, insertPlainSmartLink,
        // 26.6
        renderEquation, renderInlineMath,
        // 26.7
        handlePaste, copyBlocksToClipboard, initUndoRedo,
        highlightToHtml,
        initBlockPaste, destroyBlockPaste,
        initBlockDropZone, destroyBlockDropZone,
        // 26.8
        initResizeHandle, destroyResizeHandle,
        // 54.0
        setColumnWidth,
        // 54.1
        initColumnResize, destroyColumnResize,
        // 26.9
        scrollToBlock, initSmoothScrollSpy, destroyScrollSpy,
        scrollToFirstUnresolvedComment, isNarrowViewport,
        // 91.1
        updateCollabCursors, clearCollabCursors,
        registerShortcuts, unregisterShortcuts,
        // 30.1
        startCoverDrag,
        // 32.1
        copyBlockLink,
        // 62.1
        copyText,
        // 37.1
        initCodeKeyboardHandler, getCode, setCode,
        // 58.1
        initTableRowKeyboardHandler, destroyTableRowKeyboardHandler, tableFocusCell,
        // 43.1
        getRecentSlashItems, addRecentSlashItem,
        clearSlashQuery, refocusSlashElement, insertSlashHtml,
        replaceActiveStatusChip, cancelStatusEdit,
        insertMentionChip, cancelMentionTrigger,
        insertNotionToken, replaceNotionToken, cancelTokenTrigger, cancelChipEdit,
        adjustSlashMenuPosition, scrollSlashItemIntoView,
        // 44.1
        initSelectionWatcher, destroySelectionWatcher,
        hasSelectionWatcher, notifySelectionChanged, forceInlineToolbarForSelection,
        saveSelection, restoreSavedSelection, insertLinkOnSavedSelection,
        applyInlineColor, toggleInlineCode,
        insertInlineMath, adjustInlineToolbarPosition,
        getSelectedText, replaceSavedSelectionWithHtml,
        // 45.1
        adjustColorPickerPosition,
        // 46.1
        getRecentEmojis, addRecentEmoji, adjustEmojiPickerPosition,
        // 47.1
        adjustTypeSwitcherPosition,
        // 80.1
        initSidebarResize, destroySidebarResize,
        // 87.1
        registerPageSearch, destroyPageSearch,
        // 88.1
        downloadFileStream, copyToClipboard, getPageUrl,
        // 26.11
        initMentionClickHandler, destroyMentionClickHandler,
        getMentionDropdownPosition, clampFixedElementToViewport
    };
})();

// ═══════════════════════════════════════════════════════════════════════════
// 52.5 — PDF block (tmNotionPdf)
//
// Uses PDF.js v5 (ES modules) via dynamic import — no <script> tag needed.
// Requires Tempo.Blazor.PdfViewer static web assets.
// ═══════════════════════════════════════════════════════════════════════════

window.tmNotionPdf = (function () {
    'use strict';

    // Capture script directory while document.currentScript is still set.
    const _scriptDir = (() => {
        const src = document.currentScript?.src ?? '';
        return src ? src.substring(0, src.lastIndexOf('/') + 1) : '_content/Tempo.Blazor/js/';
    })();

    const _pdfScriptDir = (() => {
        const marker = '_content/';
        const markerIndex = _scriptDir.indexOf(marker);
        if (markerIndex >= 0) {
            return _scriptDir.substring(0, markerIndex + marker.length) + 'Tempo.Blazor.PdfViewer/js/';
        }

        return '_content/Tempo.Blazor.PdfViewer/js/';
    })();

    // canvasEl → { pdfDoc, currentPage, scale, dotNetRef }
    const _docs = new WeakMap();
    let _lib = null;

    async function _ensureLib() {
        if (_lib) return _lib;
        const mod = await import(_pdfScriptDir + 'pdf.min.mjs');
        mod.GlobalWorkerOptions.workerSrc = _pdfScriptDir + 'pdf.worker.min.mjs';
        _lib = mod;
        return _lib;
    }

    function isAvailable() {
        return true;
    }

    async function init(canvasEl, url, dotNetRef) {
        if (!canvasEl || !url) return;
        destroy(canvasEl);
        try {
            const pdfjs  = await _ensureLib();
            const pdfDoc = await pdfjs.getDocument(url).promise;
            _docs.set(canvasEl, { pdfDoc, currentPage: 1, scale: 1.0, dotNetRef });
            await renderPage(canvasEl, 1, 1.0);
            dotNetRef.invokeMethodAsync('OnPdfLoaded', pdfDoc.numPages).catch(console.error);
        } catch (err) {
            dotNetRef.invokeMethodAsync('OnPdfLoadError', String(err?.message ?? err))
                     .catch(console.error);
        }
    }

    async function renderPage(canvasEl, pageNum, scale) {
        const state = _docs.get(canvasEl);
        if (!state) return;
        state.currentPage = pageNum;
        state.scale       = scale;
        try {
            const page     = await state.pdfDoc.getPage(pageNum);
            const viewport = page.getViewport({ scale });
            canvasEl.width  = viewport.width;
            canvasEl.height = viewport.height;
            await page.render({ canvasContext: canvasEl.getContext('2d'), viewport }).promise;
        } catch (err) {
            console.error('tmNotionPdf.renderPage', err);
        }
    }

    function getTotalPages(canvasEl) {
        return _docs.get(canvasEl)?.pdfDoc?.numPages ?? 0;
    }

    async function setScale(canvasEl, scale) {
        const state = _docs.get(canvasEl);
        if (!state) return;
        await renderPage(canvasEl, state.currentPage, scale);
    }

    function destroy(canvasEl) {
        const state = _docs.get(canvasEl);
        if (!state) return;
        try { state.pdfDoc.destroy(); } catch { }
        _docs.delete(canvasEl);
    }

    return { isAvailable, init, renderPage, getTotalPages, setScale, destroy };
})();

// ── Database utilities ────────────────────────────────────────────────────────

window.tmDb = window.tmDb || {};

// Board drag-and-drop: fully JS-driven. Blazor drag events are async (WASM interop)
// so preventDefault/setData never fire in time. All drag logic lives here; Blazor
// is called back via [JSInvokable] only for state mutations.
window.tmDb.initBoardDrag = function (element, dotNetRef) {
    if (!element || element._tmBoardDragInit) return;
    element._tmBoardDragInit = true;
    console.log('[tmDb] initBoardDrag called, element:', element, 'dotNetRef:', dotNetRef);

    let isDragging = false;
    let lastEnteredCol = null;
    let draggedRecordId = '';
    let draggedFromGroup = '';
    let dropHandled = false;

    element.addEventListener('dragstart', function (e) {
        const card = e.target.closest('[data-record-id]');
        console.log('[tmDb] dragstart fired, target:', e.target, 'card found:', card);
        if (!card) return;
        isDragging = true;
        lastEnteredCol = null;
        draggedRecordId = card.dataset.recordId || '';
        draggedFromGroup = card.dataset.fromGroup || '';
        dropHandled = false;
        e.dataTransfer.effectAllowed = 'move';
        e.dataTransfer.setData('text/plain', draggedRecordId);
        console.log('[tmDb] dragstart: recordId=' + draggedRecordId + ' fromGroup=' + draggedFromGroup);
        dotNetRef.invokeMethodAsync('JsDragStart', draggedRecordId, draggedFromGroup);
    }, true);

    element.addEventListener('dragover', function (e) {
        if (!isDragging) return;
        const col = e.target.closest('[data-group-value]');
        if (col) e.preventDefault();
    }, true);

    element.addEventListener('dragenter', function (e) {
        if (!isDragging) return;
        const col = e.target.closest('[data-group-value]');
        if (col && col !== lastEnteredCol) {
            lastEnteredCol = col;
            e.preventDefault();
            console.log('[tmDb] dragenter col: groupValue=' + col.dataset.groupValue);
            dotNetRef.invokeMethodAsync('JsDragEnter', col.dataset.groupValue);
        }
    }, true);

    element.addEventListener('drop', function (e) {
        const col = e.target.closest('[data-group-value]');
        console.log('[tmDb] drop fired, isDragging=' + isDragging + ', col:', col);
        if (!isDragging) return;
        isDragging = false;
        lastEnteredCol = null;
        dropHandled = true;
        e.preventDefault();
        if (col) {
            console.log('[tmDb] drop: groupValue=' + col.dataset.groupValue);
            dotNetRef.invokeMethodAsync('JsDrop', col.dataset.groupValue, draggedRecordId, draggedFromGroup);
        } else {
            console.log('[tmDb] drop: outside column, calling JsDragEnd');
            draggedRecordId = '';
            draggedFromGroup = '';
            dropHandled = false;
            dotNetRef.invokeMethodAsync('JsDragEnd');
        }
    }, true);

    element.addEventListener('dragend', function (e) {
        console.log('[tmDb] dragend fired, isDragging was=' + isDragging);
        isDragging = false;
        lastEnteredCol = null;
        draggedRecordId = '';
        draggedFromGroup = '';
        if (dropHandled) {
            dropHandled = false;
            return;
        }
        dotNetRef.invokeMethodAsync('JsDragEnd');
    }, true);
};

window.tmDb.downloadFileFromStream = async function (fileName, contentStreamRef) {
    const arrayBuffer = await contentStreamRef.arrayBuffer();
    const blob        = new Blob([arrayBuffer], { type: 'text/csv;charset=utf-8;' });
    const url         = URL.createObjectURL(blob);
    const anchor      = document.createElement('a');
    anchor.href       = url;
    anchor.download   = fileName;
    anchor.style.display = 'none';
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    setTimeout(() => URL.revokeObjectURL(url), 10000);
};
