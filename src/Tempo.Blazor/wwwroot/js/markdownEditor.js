/**
 * Tempo Blazor Markdown Editor — Block-Based Implementation
 *
 * Architecture (Notion-inspired):
 *   .tm-mde-content[contenteditable]
 *     .tm-b[data-t="p|h1|h2|h3|ul|ol|bq|cf|hr|table"]  ← one div per logical block
 *       inline HTML: <b><em><s><code><a>                ← real semantic elements, NO markdown syntax
 *
 * getValue()  → serialize DOM to markdown string
 * setValue()  → parse markdown string → rebuild DOM
 *
 * Inline formatting uses document.execCommand for bold/italic/strike (universally supported,
 * still works in all browsers despite "deprecated" label), manual DOM manipulation for code/link.
 */
window.tmMarkdownEditor = {

    // ══════════════════════════════════════════════════════════════════════════
    // Public API — called from Blazor via JS interop
    // ══════════════════════════════════════════════════════════════════════════

    getValue(el) {
        if (!el) return '';
        return this._serializeToMarkdown(el);
    },

    setValue(el, markdown) {
        if (!el) return;
        const sel = window.getSelection();
        const hadFocus = el.contains(document.activeElement) ||
                         (sel && sel.rangeCount > 0 && el.contains(sel.getRangeAt(0).startContainer));

        el.innerHTML = '';
        const blocks = this._parseMarkdown(markdown ?? '');
        for (const b of blocks) {
            el.appendChild(this._buildBlock(b));
        }
        this._ensureBlock(el);

        if (hadFocus) {
            const last = el.lastElementChild;
            if (last && last.dataset.t !== 'hr') {
                this._setCursorAtEnd(last);
            }
        }
    },

    /**
     * Initialises event listeners. Replaces the old initKeyboardShortcuts.
     */
    init(el) {
        if (!el || el._tmMdeInit) return;
        el._tmMdeInit = true;
        const self = this;

        el._tmMdeKeydown = (e) => self._onKeydown(el, e);

        el.addEventListener('keydown', el._tmMdeKeydown);

        this._ensureBlock(el);
    },

    destroy(el) {
        if (!el) return;
        if (el._tmMdeKeydown) {
            el.removeEventListener('keydown', el._tmMdeKeydown);
            delete el._tmMdeKeydown;
        }
        delete el._tmMdeInit;
    },

    /** No-op — DOM is already the rendered state in block mode. */
    renderHighlighting(el) { /* intentional no-op */ },

    getActiveFormats(el) {
        if (!el) return [];
        const sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return [];
        const range = sel.getRangeAt(0);

        const block = this._getCaretBlock(el);
        if (!block) return [];

        const formats = [];

        // Block type
        const t = block.dataset.t || 'p';
        if (t !== 'p') formats.push(t);

        // Inline: walk ancestors of cursor up to block
        let node = range.commonAncestorContainer;
        while (node && node !== block) {
            if (node.nodeType === Node.ELEMENT_NODE) {
                const tag = node.tagName.toLowerCase();
                if (tag === 'b' || tag === 'strong') formats.push('bold');
                if (tag === 'i' || tag === 'em')     formats.push('italic');
                if (tag === 's' || tag === 'del' || tag === 'strike') formats.push('strikethrough');
                if (tag === 'code') formats.push('code');
            }
            node = node.parentNode;
        }

        return [...new Set(formats)];
    },

    /**
     * Toggles an inline format on the current selection.
     * bold/italic/strikethrough use execCommand; code uses manual DOM wrap.
     */
    toggleFormat(el, format) {
        if (!el) return;
        el.focus();
        switch (format) {
            case 'bold':         document.execCommand('bold');        break;
            case 'italic':       document.execCommand('italic');      break;
            case 'strikethrough': document.execCommand('strikeThrough'); break;
            case 'code':         this._toggleCode(el);                break;
        }
    },

    /**
     * Changes (or toggles) the block type of the block containing the cursor.
     * Calling with the current type reverts to paragraph.
     */
    setBlockType(el, type) {
        if (!el) return;
        el.focus();
        const block = this._getCaretBlock(el);
        if (!block) return;

        const current = block.dataset.t || 'p';
        const next = current === type ? 'p' : type;
        block.dataset.t = next;

        // Code fences: strip any inline HTML (only plain text allowed)
        if (next === 'cf') {
            block.textContent = block.textContent;
        }

        this._setCursorAtEnd(block);
    },

    insertHr(el) {
        if (!el) return;
        el.focus();
        const block = this._getCaretBlock(el) || el.lastElementChild;
        if (!block) return;

        const hrBlock = this._buildBlock({ type: 'hr' });
        block.after(hrBlock);

        // Ensure a paragraph follows the HR so the user can continue typing
        if (!hrBlock.nextElementSibling) {
            const p = this._buildBlock({ type: 'p', md: '' });
            hrBlock.after(p);
        }
        const next = hrBlock.nextElementSibling;
        if (next) this._setCursorAtStart(next);
        el.dispatchEvent(new Event('input', { bubbles: true }));
    },

    insertCodeBlock(el) {
        if (!el) return;
        el.focus();
        const block = this._getCaretBlock(el) || el.lastElementChild;
        if (!block) return;

        const cf = this._buildBlock({ type: 'cf', md: '' });
        block.after(cf);
        this._setCursorAtStart(cf);
        el.dispatchEvent(new Event('input', { bubbles: true }));
    },

    insertTable(el, rows, cols) {
        if (!el) return;
        el.focus();
        const block = this._getCaretBlock(el) || el.lastElementChild;
        if (!block) return;

        const tableData = {
            type: 'table',
            header: Array.from({ length: cols }, (_, i) => `Col ${i + 1}`),
            rows: Array.from({ length: Math.max(0, rows - 1) }, () =>
                Array.from({ length: cols }, () => '')
            )
        };

        const tableBlock = this._buildBlock(tableData);
        block.after(tableBlock);

        // Focus first cell
        const firstCell = tableBlock.querySelector('th, td');
        if (firstCell) {
            this._focusCell(firstCell);
        }

        el.dispatchEvent(new Event('input', { bubbles: true }));
    },

    insertLink(el, url, linkText) {
        if (!el) return;
        el.focus();
        const sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return;

        const range = sel.getRangeAt(0);
        if (!el.contains(range.commonAncestorContainer)) return;

        const a = document.createElement('a');
        a.href = url;
        a.textContent = linkText || range.toString() || url;

        range.deleteContents();
        range.insertNode(a);

        // Cursor after link
        const r2 = document.createRange();
        r2.setStartAfter(a);
        r2.collapse(true);
        sel.removeAllRanges();
        sel.addRange(r2);

        el.dispatchEvent(new Event('input', { bubbles: true }));
    },

    getSelectedText(el) {
        if (!el) return '';
        const sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return '';
        const range = sel.getRangeAt(0);
        if (!el.contains(range.commonAncestorContainer)) return '';
        return range.toString();
    },

    // ══════════════════════════════════════════════════════════════════════════
    // Internal — Event handlers
    // ══════════════════════════════════════════════════════════════════════════

    _onKeydown(el, e) {
        const ctrl = e.ctrlKey || e.metaKey;

        // ── Ctrl shortcuts ────────────────────────────────────────────────────
        if (ctrl) {
            switch (e.key.toLowerCase()) {
                case 'b':
                    e.preventDefault();
                    this.toggleFormat(el, 'bold');
                    el.dispatchEvent(new Event('input', { bubbles: true }));
                    return;
                case 'i':
                    e.preventDefault();
                    this.toggleFormat(el, 'italic');
                    el.dispatchEvent(new Event('input', { bubbles: true }));
                    return;
                case 'k':
                    e.preventDefault();
                    el.dispatchEvent(new CustomEvent('linkShortcut', { bubbles: true }));
                    return;
                case 'enter':
                    e.preventDefault();
                    el.dispatchEvent(new CustomEvent('submitShortcut', { bubbles: true }));
                    return;
            }
        }

        const cell = this._getCaretCell(el);
        if (cell) {
            // ── Tab navigation in tables ──────────────────────────────────────
            if (e.key === 'Tab') {
                e.preventDefault();
                this._handleTableTab(el, cell, e.shiftKey);
                el.dispatchEvent(new Event('input', { bubbles: true }));
                return;
            }

            // ── Enter in table cell ───────────────────────────────────────────
            if (e.key === 'Enter' && !ctrl) {
                e.preventDefault();
                this._handleTableEnter(el, cell);
                el.dispatchEvent(new Event('input', { bubbles: true }));
                return;
            }

            // ── Backspace in first cell of empty row ──────────────────────────
            if (e.key === 'Backspace' && !ctrl) {
                const tr = cell.closest('tr');
                const tbody = cell.closest('tbody');
                if (tbody && tr && cell.textContent.trim() === '' && tr.cells[0] === cell) {
                    e.preventDefault();
                    this._handleTableBackspace(el, tr);
                    el.dispatchEvent(new Event('input', { bubbles: true }));
                    return;
                }
            }

            // Let other keys (arrows, typing) behave natively inside the cell
            return;
        }

        // ── Enter — split block ───────────────────────────────────────────────
        if (e.key === 'Enter' && !ctrl) {
            const block = this._getCaretBlock(el);
            // Code fences: allow native newline
            if (block && block.dataset.t === 'cf') return;

            e.preventDefault();
            this._handleEnter(el);
            el.dispatchEvent(new Event('input', { bubbles: true }));
            return;
        }

        // ── Backspace at block start — merge with previous ────────────────────
        if (e.key === 'Backspace' && !ctrl) {
            const block = this._getCaretBlock(el);
            if (block && this._isAtBlockStart(block)) {
                const prev = block.previousElementSibling;
                if (prev) {
                    e.preventDefault();
                    this._mergeBlockIntoPrev(el, block);
                    el.dispatchEvent(new Event('input', { bubbles: true }));
                }
            }
        }
    },

    // ══════════════════════════════════════════════════════════════════════════
    // Internal — Table helpers
    // ══════════════════════════════════════════════════════════════════════════

    _getCaretCell(el) {
        const sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return null;
        const node = sel.getRangeAt(0).startContainer;
        const cell = node.nodeType === Node.ELEMENT_NODE
            ? (node.closest ? node.closest('th, td') : null)
            : (node.parentElement ? node.parentElement.closest('th, td') : null);
        if (cell && el.contains(cell)) return cell;
        return null;
    },

    _focusCell(cell) {
        if (!cell) return;
        cell.focus();
        const sel = window.getSelection();
        if (!sel) return;
        const range = document.createRange();
        range.selectNodeContents(cell);
        range.collapse(false);
        sel.removeAllRanges();
        sel.addRange(range);
    },

    _handleTableTab(el, cell, shift) {
        const row = cell.closest('tr');
        const table = cell.closest('table');
        if (!row || !table) return;

        const rows = [...table.querySelectorAll('tr')];
        const cells = [...row.querySelectorAll('th, td')];
        const rowIndex = rows.indexOf(row);
        const colIndex = cells.indexOf(cell);

        let nextRow = rowIndex;
        let nextCol = colIndex + (shift ? -1 : 1);

        if (nextCol >= cells.length) {
            nextCol = 0;
            nextRow++;
        }
        if (nextCol < 0) {
            nextCol = cells.length - 1;
            nextRow--;
        }

        // If past last row, append a new row
        if (nextRow >= rows.length) {
            this._insertTableRow(table, rows.length);
            const newRows = [...table.querySelectorAll('tr')];
            const nextCell = newRows[nextRow]?.cells[nextCol];
            if (nextCell) this._focusCell(nextCell);
            return;
        }

        const nextCell = rows[nextRow]?.cells[nextCol];
        if (nextCell) this._focusCell(nextCell);
    },

    _handleTableEnter(el, cell) {
        const row = cell.closest('tr');
        const tbody = cell.closest('tbody');
        const table = cell.closest('table');
        if (!row || !table) return;

        const rows = tbody ? [...tbody.querySelectorAll('tr')] : [...table.querySelectorAll('tbody tr')];
        const rowIndex = rows.indexOf(row);

        // Insert new row after current row
        const newRow = this._insertTableRow(table, rowIndex + 1);
        const firstCell = newRow.cells[0];
        if (firstCell) this._focusCell(firstCell);
    },

    _handleTableBackspace(el, row) {
        const tbody = row.closest('tbody');
        const table = row.closest('table');
        const block = table?.closest('.tm-b');
        if (!tbody || !table || !block) return;

        const rows = [...tbody.querySelectorAll('tr')];
        if (rows.length > 1) {
            const prevRow = row.previousElementSibling;
            row.remove();
            const focusCell = prevRow ? prevRow.cells[prevRow.cells.length - 1] : null;
            if (focusCell) this._focusCell(focusCell);
        } else {
            // Remove the entire table block and replace with empty paragraph
            const p = this._buildBlock({ type: 'p', md: '' });
            block.replaceWith(p);
            this._setCursorAtStart(p);
        }
    },

    _insertTableRow(table, index) {
        const thead = table.querySelector('thead');
        const tbody = table.querySelector('tbody') || table;
        const refRow = thead?.querySelector('tr');
        const colCount = refRow ? refRow.cells.length : (tbody.querySelector('tr')?.cells.length || 1);

        const tr = document.createElement('tr');
        for (let i = 0; i < colCount; i++) {
            const td = document.createElement('td');
            td.setAttribute('contenteditable', 'true');
            td.textContent = '';
            tr.appendChild(td);
        }

        const rows = [...tbody.querySelectorAll('tr')];
        if (index >= 0 && index < rows.length) {
            rows[index].before(tr);
        } else {
            tbody.appendChild(tr);
        }
        return tr;
    },

    _parseTableRow(line) {
        return line.split('|')
            .map(c => c.trim())
            .filter((c, i, arr) => {
                if (i === 0 && c === '') return false;
                if (i === arr.length - 1 && c === '') return false;
                return true;
            });
    },

    _isTableSeparator(line) {
        if (!line.includes('|')) return false;
        const parts = this._parseTableRow(line);
        return parts.length > 0 && parts.every(p => /^[-:]+$/.test(p.replace(/\s/g, '')));
    },

    // ══════════════════════════════════════════════════════════════════════════
    // Internal — Block splitting / merging
    // ══════════════════════════════════════════════════════════════════════════

    _handleEnter(el) {
        const sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return;

        const block = this._getCaretBlock(el);
        if (!block) return;

        const type = block.dataset.t || 'p';

        // HR: move to next block (or create paragraph)
        if (type === 'hr') {
            const next = block.nextElementSibling || this._appendParagraph(el);
            this._setCursorAtStart(next);
            return;
        }

        const range = sel.getRangeAt(0);

        // Collapse selection (delete selected content first)
        if (!range.collapsed) range.deleteContents();

        // Check: is block empty and a list/blockquote? → convert to paragraph
        if (!block.textContent.trim() && ['ul', 'ol', 'bq'].includes(type)) {
            block.dataset.t = 'p';
            this._setCursorAtEnd(block);
            return;
        }

        // Extract content from cursor to end of block
        const afterRange = document.createRange();
        afterRange.setStart(range.startContainer, range.startOffset);
        if (block.lastChild) {
            afterRange.setEnd(block, block.childNodes.length);
        } else {
            afterRange.setEnd(block, 0);
        }
        const fragment = afterRange.extractContents();

        // New block: headings always produce paragraph; lists/bq continue same type
        const newType = ['h1', 'h2', 'h3'].includes(type) ? 'p' : type;
        const newBlock = document.createElement('div');
        newBlock.className = 'tm-b';
        newBlock.dataset.t = newType;
        newBlock.appendChild(fragment);

        this._stripEmptyInlines(newBlock);
        this._stripEmptyInlines(block);

        block.after(newBlock);
        this._setCursorAtStart(newBlock);
    },

    _mergeBlockIntoPrev(el, block) {
        const prev = block.previousElementSibling;
        if (!prev) return;

        // If prev is HR, just remove the HR or skip
        if (prev.dataset.t === 'hr') {
            prev.remove();
            el.dispatchEvent(new Event('input', { bubbles: true }));
            return;
        }

        // Remember char count of prev for cursor placement
        const joinOffset = prev.textContent.length;

        // Move all child nodes from block to end of prev
        while (block.firstChild) {
            prev.appendChild(block.firstChild);
        }
        block.remove();

        this._setCursorAtOffset(prev, joinOffset);
    },

    _appendParagraph(el) {
        const p = this._buildBlock({ type: 'p', md: '' });
        el.appendChild(p);
        return p;
    },

    _ensureBlock(el) {
        // Wrap stray text nodes in paragraph divs
        for (const child of [...el.childNodes]) {
            if (child.nodeType === Node.TEXT_NODE) {
                const p = document.createElement('div');
                p.className = 'tm-b';
                p.dataset.t = 'p';
                p.textContent = child.textContent;
                el.replaceChild(p, child);
            }
        }
        // Wrap non-block children
        for (const child of [...el.children]) {
            if (!child.dataset.t) {
                child.classList.add('tm-b');
                child.dataset.t = 'p';
            }
        }
        // At least one block always exists
        if (el.children.length === 0) {
            el.appendChild(this._buildBlock({ type: 'p', md: '' }));
        }
    },

    _stripEmptyInlines(block) {
        for (const el of [...block.querySelectorAll('b,i,em,strong,s,del,code')]) {
            if (!el.textContent) el.remove();
        }
    },

    // ══════════════════════════════════════════════════════════════════════════
    // Internal — Inline code toggle (execCommand doesn't support code)
    // ══════════════════════════════════════════════════════════════════════════

    _toggleCode(el) {
        const sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return;
        const range = sel.getRangeAt(0);
        if (!el.contains(range.commonAncestorContainer)) return;

        // Check if cursor/selection is already inside a <code>
        let node = range.commonAncestorContainer;
        while (node && node !== el) {
            if (node.nodeType === Node.ELEMENT_NODE && node.tagName.toLowerCase() === 'code') {
                // Unwrap
                const parent = node.parentNode;
                while (node.firstChild) parent.insertBefore(node.firstChild, node);
                parent.removeChild(node);
                return;
            }
            node = node.parentNode;
        }

        // Wrap selection
        if (!range.collapsed) {
            const code = document.createElement('code');
            try {
                range.surroundContents(code);
            } catch {
                const frag = range.extractContents();
                code.appendChild(frag);
                range.insertNode(code);
            }
        }
    },

    // ══════════════════════════════════════════════════════════════════════════
    // Internal — Cursor helpers
    // ══════════════════════════════════════════════════════════════════════════

    _getCaretBlock(el) {
        const sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return el.firstElementChild || null;

        let node = sel.getRangeAt(0).startContainer;
        while (node && node !== el) {
            if (node.nodeType === Node.ELEMENT_NODE &&
                node.parentElement === el &&
                node.dataset.t !== undefined) {
                return node;
            }
            node = node.parentNode;
        }
        return el.firstElementChild || null;
    },

    _isAtBlockStart(block) {
        const sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return false;
        const range = sel.getRangeAt(0);
        if (!range.collapsed) return false;

        let chars = 0;
        const walker = document.createTreeWalker(block, NodeFilter.SHOW_TEXT, null);
        while (walker.nextNode()) {
            const n = walker.currentNode;
            if (n === range.startContainer) return chars + range.startOffset === 0;
            chars += n.textContent.length;
        }
        return range.startContainer === block && range.startOffset === 0;
    },

    _setCursorAtStart(block) {
        if (!block) return;
        const sel = window.getSelection();
        if (!sel) return;
        const range = document.createRange();

        const walker = document.createTreeWalker(block, NodeFilter.SHOW_TEXT, null);
        if (walker.nextNode()) {
            range.setStart(walker.currentNode, 0);
        } else {
            range.setStart(block, 0);
        }
        range.collapse(true);
        sel.removeAllRanges();
        sel.addRange(range);
        block.focus();
    },

    _setCursorAtEnd(block) {
        if (!block) return;
        const sel = window.getSelection();
        if (!sel) return;
        const range = document.createRange();
        range.selectNodeContents(block);
        range.collapse(false);
        sel.removeAllRanges();
        sel.addRange(range);
        block.focus();
    },

    _setCursorAtOffset(block, charOffset) {
        if (!block) return;
        let remaining = charOffset;
        const walker = document.createTreeWalker(block, NodeFilter.SHOW_TEXT, null);
        let lastNode = null;
        while (walker.nextNode()) {
            const n = walker.currentNode;
            lastNode = n;
            if (remaining <= n.textContent.length) {
                const sel = window.getSelection();
                if (!sel) return;
                const range = document.createRange();
                range.setStart(n, remaining);
                range.collapse(true);
                sel.removeAllRanges();
                sel.addRange(range);
                return;
            }
            remaining -= n.textContent.length;
        }
        // Fallback: end of last text node
        if (lastNode) {
            const sel = window.getSelection();
            if (!sel) return;
            const range = document.createRange();
            range.setStart(lastNode, lastNode.textContent.length);
            range.collapse(true);
            sel.removeAllRanges();
            sel.addRange(range);
        }
    },

    // ══════════════════════════════════════════════════════════════════════════
    // Internal — Serialization: DOM → Markdown
    // ══════════════════════════════════════════════════════════════════════════

    _serializeToMarkdown(el) {
        const parts = [];
        for (const block of el.children) {
            parts.push(this._serializeBlock(block));
        }
        return parts.join('\n');
    },

    _serializeBlock(block) {
        const t = block.dataset.t || 'p';
        switch (t) {
            case 'h1': return '# '  + this._serializeInlines(block);
            case 'h2': return '## ' + this._serializeInlines(block);
            case 'h3': return '### '+ this._serializeInlines(block);
            case 'ul': return '- '  + this._serializeInlines(block);
            case 'ol': return '1. ' + this._serializeInlines(block);
            case 'bq': return '> '  + this._serializeInlines(block);
            case 'hr': return '---';
            case 'cf': return '```\n' + block.textContent + '\n```';
            case 'table': return this._serializeTable(block);
            default:   return this._serializeInlines(block);
        }
    },

    _serializeTable(block) {
        const table = block.querySelector('table');
        if (!table) return '';
        const lines = [];

        const headerRow = table.querySelector('thead tr');
        if (headerRow) {
            const cells = [...headerRow.querySelectorAll('th, td')].map(td => td.textContent);
            lines.push('| ' + cells.join(' | ') + ' |');
            lines.push('|' + cells.map(() => ' --- ').join('|') + '|');
        }

        for (const tr of table.querySelectorAll('tbody tr')) {
            const cells = [...tr.querySelectorAll('td, th')].map(td => td.textContent);
            lines.push('| ' + cells.join(' | ') + ' |');
        }

        return lines.join('\n');
    },

    _serializeInlines(node) {
        let out = '';
        for (const child of node.childNodes) {
            if (child.nodeType === Node.TEXT_NODE) {
                out += child.textContent;
            } else if (child.nodeType === Node.ELEMENT_NODE) {
                const tag = child.tagName.toLowerCase();
                const inner = this._serializeInlines(child);
                switch (tag) {
                    case 'b': case 'strong':
                        out += `**${inner}**`; break;
                    case 'i': case 'em':
                        out += `_${inner}_`; break;
                    case 's': case 'del': case 'strike':
                        out += `~~${inner}~~`; break;
                    case 'code':
                        out += `\`${inner}\``; break;
                    case 'a':
                        out += `[${inner}](${child.getAttribute('href') || ''})`; break;
                    case 'br':
                        out += '\n'; break;
                    default:
                        out += inner;
                }
            }
        }
        return out;
    },

    // ══════════════════════════════════════════════════════════════════════════
    // Internal — Parsing: Markdown → DOM blocks
    // ══════════════════════════════════════════════════════════════════════════

    _parseMarkdown(markdown) {
        if (!markdown) return [{ type: 'p', md: '' }];

        const lines = markdown.split('\n');
        const blocks = [];
        let i = 0;

        while (i < lines.length) {
            const line = lines[i];

            // Code fence (``` ... ```)
            if (/^`{3}/.test(line)) {
                let code = '';
                i++;
                while (i < lines.length && !/^`{3}/.test(lines[i])) {
                    code += (code !== '' ? '\n' : '') + lines[i];
                    i++;
                }
                blocks.push({ type: 'cf', md: code });
                i++; // skip closing ```
                continue;
            }

            // Horizontal rule
            if (/^(---|\*\*\*|___)$/.test(line.trim())) {
                blocks.push({ type: 'hr' });
                i++; continue;
            }

            // Headings
            const h3 = line.match(/^### (.*)$/);
            if (h3) { blocks.push({ type: 'h3', md: h3[1] }); i++; continue; }
            const h2 = line.match(/^## (.*)$/);
            if (h2) { blocks.push({ type: 'h2', md: h2[1] }); i++; continue; }
            const h1 = line.match(/^# (.*)$/);
            if (h1) { blocks.push({ type: 'h1', md: h1[1] }); i++; continue; }

            // Table
            if (line.includes('|') && (i + 1) < lines.length && this._isTableSeparator(lines[i + 1])) {
                const header = this._parseTableRow(line);
                i += 2; // skip header and separator
                const rows = [];
                while (i < lines.length && lines[i].includes('|')) {
                    rows.push(this._parseTableRow(lines[i]));
                    i++;
                }
                blocks.push({ type: 'table', header, rows });
                continue;
            }

            // Bullet list
            const ul = line.match(/^[-*+] (.*)$/);
            if (ul) { blocks.push({ type: 'ul', md: ul[1] }); i++; continue; }

            // Ordered list
            const ol = line.match(/^\d+\. (.*)$/);
            if (ol) { blocks.push({ type: 'ol', md: ol[1] }); i++; continue; }

            // Blockquote
            const bq = line.match(/^> (.*)$/);
            if (bq) { blocks.push({ type: 'bq', md: bq[1] }); i++; continue; }

            // Paragraph (including empty lines)
            blocks.push({ type: 'p', md: line });
            i++;
        }

        return blocks;
    },

    _buildBlock(data) {
        const div = document.createElement('div');
        div.className = 'tm-b';
        div.dataset.t = data.type;

        if (data.type === 'hr') {
            div.setAttribute('contenteditable', 'false');
            div.setAttribute('tabindex', '-1');
            return div;
        }

        if (data.type === 'cf') {
            div.textContent = data.md ?? '';
            return div;
        }

        if (data.type === 'table') {
            const table = document.createElement('table');
            table.className = 'tm-mde-table';

            const thead = document.createElement('thead');
            const headerTr = document.createElement('tr');
            for (const h of (data.header || [])) {
                const th = document.createElement('th');
                th.setAttribute('contenteditable', 'true');
                th.textContent = h;
                headerTr.appendChild(th);
            }
            thead.appendChild(headerTr);
            table.appendChild(thead);

            const tbody = document.createElement('tbody');
            for (const row of (data.rows || [])) {
                const tr = document.createElement('tr');
                for (const cell of row) {
                    const td = document.createElement('td');
                    td.setAttribute('contenteditable', 'true');
                    td.textContent = cell;
                    tr.appendChild(td);
                }
                tbody.appendChild(tr);
            }
            table.appendChild(tbody);
            div.appendChild(table);
            return div;
        }

        // Parse inline markdown into DOM nodes
        const nodes = this._parseInlines(data.md ?? '');
        for (const n of nodes) div.appendChild(n);

        return div;
    },

    /**
     * Parses inline markdown text into an array of DOM nodes.
     * Handles: **bold**, _italic_, ~~strike~~, `code`, [link](url)
     */
    _parseInlines(text) {
        if (!text) return [document.createTextNode('')];

        const nodes = [];
        let i = 0;
        const len = text.length;
        let plain = '';

        const flushPlain = () => {
            if (plain) { nodes.push(document.createTextNode(plain)); plain = ''; }
        };

        while (i < len) {
            const c = text[i];

            // Inline code `...`
            if (c === '`') {
                const close = text.indexOf('`', i + 1);
                if (close !== -1) {
                    flushPlain();
                    const code = document.createElement('code');
                    code.textContent = text.substring(i + 1, close);
                    nodes.push(code);
                    i = close + 1;
                    continue;
                }
            }

            // Bold+italic ***...***
            if (c === '*' && text[i+1] === '*' && text[i+2] === '*') {
                const close = text.indexOf('***', i + 3);
                if (close !== -1) {
                    flushPlain();
                    const b = document.createElement('b');
                    const em = document.createElement('em');
                    em.textContent = text.substring(i + 3, close);
                    b.appendChild(em);
                    nodes.push(b);
                    i = close + 3;
                    continue;
                }
            }

            // Bold **...**
            if (c === '*' && text[i+1] === '*' && text[i+2] !== '*') {
                const close = text.indexOf('**', i + 2);
                if (close !== -1) {
                    flushPlain();
                    const b = document.createElement('b');
                    b.textContent = text.substring(i + 2, close);
                    nodes.push(b);
                    i = close + 2;
                    continue;
                }
            }

            // Italic *...*
            if (c === '*' && text[i+1] !== '*') {
                const close = text.indexOf('*', i + 1);
                if (close !== -1 && text[close+1] !== '*') {
                    flushPlain();
                    const em = document.createElement('em');
                    em.textContent = text.substring(i + 1, close);
                    nodes.push(em);
                    i = close + 1;
                    continue;
                }
            }

            // Italic _..._
            if (c === '_' && text[i+1] !== '_') {
                const close = text.indexOf('_', i + 1);
                if (close !== -1 && text[close+1] !== '_') {
                    flushPlain();
                    const em = document.createElement('em');
                    em.textContent = text.substring(i + 1, close);
                    nodes.push(em);
                    i = close + 1;
                    continue;
                }
            }

            // Strikethrough ~~...~~
            if (c === '~' && text[i+1] === '~') {
                const close = text.indexOf('~~', i + 2);
                if (close !== -1) {
                    flushPlain();
                    const s = document.createElement('s');
                    s.textContent = text.substring(i + 2, close);
                    nodes.push(s);
                    i = close + 2;
                    continue;
                }
            }

            // Link [...](...)
            if (c === '[') {
                const closeLabel = text.indexOf(']', i + 1);
                if (closeLabel !== -1 && text[closeLabel + 1] === '(') {
                    const closeUrl = text.indexOf(')', closeLabel + 2);
                    if (closeUrl !== -1) {
                        flushPlain();
                        const a = document.createElement('a');
                        a.textContent = text.substring(i + 1, closeLabel);
                        a.href = text.substring(closeLabel + 2, closeUrl);
                        nodes.push(a);
                        i = closeUrl + 1;
                        continue;
                    }
                }
            }

            plain += c;
            i++;
        }

        flushPlain();
        return nodes.length > 0 ? nodes : [document.createTextNode('')];
    },

    // Legacy shims — kept so that any lingering Blazor calls don't throw
    initKeyboardShortcuts(el) { this.init(el); },
    wrapSelection(el, before, after) { /* replaced by toggleFormat */ },
    insertAtLineStart(el, prefix) { /* replaced by setBlockType */ },
    insertAtCursor(el, text, offset) { /* replaced by insertHr / insertCodeBlock */ },

    // ── Caret bookmark across the link/table dialogs ────────────────────────────
    // The dialogs are TmModals (FZE-10): opening one moves focus into the dialog and the
    // browser selection leaves the editor, so the bookmark is taken before the dialog opens
    // and replayed on close — on cancel, and right before the insertion on confirm.

    _savedCaretBookmark: null,

    saveCursorBookmark(el) {
        if (!el) return null;
        const sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return null;
        const range = sel.getRangeAt(0);
        if (!el.contains(range.commonAncestorContainer)) return null;
        this._savedCaretBookmark = {
            startContainer: range.startContainer,
            startOffset: range.startOffset,
            endContainer: range.endContainer,
            endOffset: range.endOffset
        };
        return this._savedCaretBookmark;
    },

    restoreCursorBookmark(el, bm) {
        const bookmark = bm || this._savedCaretBookmark;
        this._savedCaretBookmark = null;
        if (!el || !bookmark) return false;
        try {
            if (!el.contains(bookmark.startContainer) || !el.contains(bookmark.endContainer)) return false;
            const range = document.createRange();
            range.setStart(bookmark.startContainer, bookmark.startOffset);
            range.setEnd(bookmark.endContainer, bookmark.endOffset);
            el.focus();
            const sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(range);
            return true;
        } catch (e) {
            return false;
        }
    },
};
