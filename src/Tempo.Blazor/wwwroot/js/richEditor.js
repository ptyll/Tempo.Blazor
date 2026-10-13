/**
 * Tempo Blazor Rich Text Editor JavaScript Interop
 * Provides contentEditable manipulation via document.execCommand and custom utilities
 */
window.tmRichEditor = {
    /**
     * Execute a formatting command on the current selection
     * @param {string} command - The command to execute (bold, italic, insertUnorderedList, etc.)
     * @param {string} value - Optional value for the command (e.g., URL for createLink)
     */
    execCommand: function (command, value = null) {
        // Ensure editor has focus before executing command
        const activeElement = document.activeElement;
        const isContentEditable = activeElement && activeElement.contentEditable === 'true';
        
        if (!isContentEditable) {
            // Try to find and focus the last active editor
            const editors = document.querySelectorAll('[contenteditable="true"]');
            if (editors.length > 0) {
                editors[0].focus();
            }
        }

        // Map simplified command names to execCommand names
        const commandMap = {
            'ul': 'insertUnorderedList',
            'ol': 'insertOrderedList',
            'h1': 'formatBlock',
            'h2': 'formatBlock',
            'h3': 'formatBlock',
            'h4': 'formatBlock',
            'blockquote': 'formatBlock',
            'code': 'formatBlock',
            'strikethrough': 'strikeThrough',
            'removeFormat': 'removeFormat',
            'undo': 'undo',
            'redo': 'redo',
            'indent': 'indent',
            'outdent': 'outdent',
            'superscript': 'superscript',
            'subscript': 'subscript'
        };

        const execCommand = commandMap[command] || command;
        
        // Handle formatBlock commands that need value
        let execValue = value;
        if (command === 'h1' || command === 'h2' || command === 'h3' || command === 'h4') {
            execValue = command.toUpperCase();
        } else if (command === 'blockquote') {
            execValue = 'BLOCKQUOTE';
        } else if (command === 'code') {
            execValue = 'PRE';
        }

        try {
            document.execCommand(execCommand, false, execValue);
        } catch (e) {
            console.warn('execCommand failed:', e);
        }

        // Return focus to editor
        this._restoreFocus();
    },

    /**
     * Insert a link at the current selection of the given editor element
     * @param {HTMLElement} element - The editor element
     * @param {string} url - The URL
     * @param {string} text - The display text (optional, uses selection if empty)
     */
    insertLink: function (element, url, text) {
        this._focusEditor(element);

        if (text && text.trim()) {
            // Replace selection with link text first
            const selection = window.getSelection();
            if (selection.rangeCount > 0) {
                const range = selection.getRangeAt(0);
                range.deleteContents();
                const textNode = document.createTextNode(text);
                range.insertNode(textNode);

                // Select the inserted text
                range.selectNode(textNode);
                selection.removeAllRanges();
                selection.addRange(range);
            }
        }

        document.execCommand('createLink', false, url);
        this._focusEditor(element);
    },

    /**
     * Remove link at current selection
     */
    unlink: function () {
        this._ensureFocus();
        document.execCommand('unlink', false, null);
        this._restoreFocus();
    },

    /**
     * Insert a mention chip
     * @param {string} userName - The username (e.g., @john)
     * @param {string} displayName - The display name (e.g., John Doe)
     * @param {string} userId - The user ID
     */
    insertMention: function (userName, displayName, userId) {
        this._ensureFocus();
        
        // Find and delete the @query text that triggered the mention
        const selection = window.getSelection();
        if (selection.rangeCount > 0) {
            const range = selection.getRangeAt(0);
            const textNode = range.startContainer;
            const offset = range.startOffset;
            

            
            // Get the text content and find the last @ before cursor
            const textContent = textNode.textContent || '';
            const textBeforeCursor = textContent.substring(0, offset);
            const lastAtIndex = textBeforeCursor.lastIndexOf('@');
            

            
            if (lastAtIndex >= 0) {
                // Create a range from @ to cursor and delete it
                const deleteRange = document.createRange();
                deleteRange.setStart(textNode, lastAtIndex);
                deleteRange.setEnd(textNode, offset);
                deleteRange.deleteContents();

            }
        }
        
        const mentionHtml = `<span class="tm-mention" data-user-id="${this._escapeHtml(userId)}" contenteditable="false">@${this._escapeHtml(displayName)}</span>&nbsp;`;

        
        document.execCommand('insertHTML', false, mentionHtml);
        this._restoreFocus();
    },

    /**
     * Insert an image into the given editor element
     * @param {HTMLElement} element - The editor element
     * @param {string} url - Image URL
     * @param {string} alt - Alt text
     */
    insertImage: function (element, url, alt = '') {
        this._focusEditor(element);
        const imgHtml = `<img src="${this._escapeHtml(url)}" alt="${this._escapeHtml(alt)}" class="tm-rte-image" />`;
        document.execCommand('insertHTML', false, imgHtml);
        this._focusEditor(element);
    },

    /**
     * Insert a table into the given editor element
     * @param {HTMLElement} element - The editor element
     * @param {number} rows - Number of rows
     * @param {number} cols - Number of columns
     */
    insertTable: function (element, rows, cols) {
        this._focusEditor(element);
        
        let tableHtml = '<table class="tm-rte-table"><tbody>';
        for (let i = 0; i < rows; i++) {
            tableHtml += '<tr>';
            for (let j = 0; j < cols; j++) {
                tableHtml += '<td>&nbsp;</td>';
            }
            tableHtml += '</tr>';
        }
        tableHtml += '</tbody></table><p><br></p>';
        
        document.execCommand('insertHTML', false, tableHtml);
        this._focusEditor(element);
    },

    /**
     * Insert a horizontal rule
     */
    insertHorizontalRule: function () {
        this._ensureFocus();
        document.execCommand('insertHorizontalRule', false, null);
        this._restoreFocus();
    },

    /**
     * Insert HTML content
     * @param {string} html - HTML to insert
     */
    insertHtml: function (html) {
        this._ensureFocus();
        document.execCommand('insertHTML', false, html);
        this._restoreFocus();
    },

    /**
     * Insert emoji at cursor position
     * @param {string} emoji - The emoji character
     */
    insertEmoji: function (emoji) {
        this._ensureFocus();
        document.execCommand('insertText', false, emoji);
        this._restoreFocus();
    },

    /**
     * Insert a video embed (iframe) into the given editor element
     * @param {HTMLElement} element - The editor element
     * @param {string} embedUrl - The embed URL
     * @param {string} title - Video title
     */
    insertVideo: function (element, embedUrl, title = 'Video') {
        this._focusEditor(element);
        const videoHtml = `<div class="tm-rte-video-wrapper"><iframe src="${this._escapeHtml(embedUrl)}" title="${this._escapeHtml(title)}" frameborder="0" allowfullscreen></iframe></div><p><br></p>`;
        document.execCommand('insertHTML', false, videoHtml);
        this._focusEditor(element);
    },

    /**
     * Toggle task/check list item
     * Converts current line to/from task list item
     */
    toggleTaskList: function () {
        this._ensureFocus();
        
        const selection = window.getSelection();
        if (selection.rangeCount === 0) return;
        
        const range = selection.getRangeAt(0);
        let node = range.commonAncestorContainer;
        if (node.nodeType === Node.TEXT_NODE) {
            node = node.parentElement;
        }
        
        // Find the closest li element
        let li = node.closest ? node.closest('li') : null;
        if (!li) {
            // Check parents
            while (node && node !== document.body) {
                if (node.tagName === 'LI') {
                    li = node;
                    break;
                }
                node = node.parentElement;
            }
        }
        
        if (li && li.classList.contains('tm-task-item')) {
            // Remove task item - convert to regular li
            const checkbox = li.querySelector('.tm-task-checkbox');
            if (checkbox) checkbox.remove();
            li.classList.remove('tm-task-item');
        } else if (li) {
            // Add task item to existing li
            this._makeTaskItem(li);
        } else {
            // Create new task list
            document.execCommand('insertUnorderedList', false, null);
            // Now find the newly created li and convert it
            setTimeout(() => {
                const newSelection = window.getSelection();
                if (newSelection.rangeCount > 0) {
                    let newNode = newSelection.getRangeAt(0).commonAncestorContainer;
                    if (newNode.nodeType === Node.TEXT_NODE) newNode = newNode.parentElement;
                    const newLi = newNode.closest ? newNode.closest('li') : null;
                    if (newLi) this._makeTaskItem(newLi);
                }
            }, 0);
        }
        
        this._restoreFocus();
    },

    /**
     * Convert a li element to task item
     * @param {HTMLElement} li - The list item element
     */
    _makeTaskItem: function (li) {
        li.classList.add('tm-task-item');
        const checkbox = document.createElement('span');
        checkbox.className = 'tm-task-checkbox';
        checkbox.contentEditable = 'false';
        checkbox.innerHTML = '☐ ';
        checkbox.onclick = function (e) {
            e.preventDefault();
            const isChecked = this.innerHTML.includes('☑');
            this.innerHTML = isChecked ? '☐ ' : '☑ ';
            this.classList.toggle('tm-task-checked', !isChecked);
        };
        
        if (!li.querySelector('.tm-task-checkbox')) {
            li.insertBefore(checkbox, li.firstChild);
        }
    },

    /**
     * Get currently selected text
     * @returns {string} The selected text
     */
    getSelectedText: function () {
        const selection = window.getSelection();
        return selection.toString();
    },

    /**
     * Get HTML content of an editor element. Content saved by older builds may still carry a
     * legacy #tm-cursor-marker span — it never belongs to the document and is stripped here.
     * @param {HTMLElement} element - The editor element
     * @returns {string} The inner HTML
     */
    getHtml: function (element) {
        if (!element) return '';
        const legacyMarker = element.querySelector('#tm-cursor-marker');
        if (legacyMarker) legacyMarker.remove();
        return element.innerHTML;
    },

    /**
     * Get text content before cursor position
     * @param {HTMLElement} element - The editor element
     * @param {number} maxLength - Maximum length of text to return
     * @returns {string} The text before cursor
     */
    getTextBeforeCursor: function (element, maxLength = 100) {
        if (!element) return '';
        
        const selection = window.getSelection();
        if (selection.rangeCount === 0) return '';
        
        const range = selection.getRangeAt(0);
        const cursorNode = range.startContainer;
        const cursorOffset = range.startOffset;
        
        // Get all text content up to cursor
        let textBeforeCursor = '';
        
        // Create a range from start of element to cursor
        const preCursorRange = document.createRange();
        preCursorRange.setStart(element, 0);
        preCursorRange.setEnd(cursorNode, cursorOffset);
        
        // Extract text from this range
        const fragment = preCursorRange.cloneContents();
        const tempDiv = document.createElement('div');
        tempDiv.appendChild(fragment);
        textBeforeCursor = tempDiv.textContent || '';
        
        // Return last maxLength characters
        if (textBeforeCursor.length > maxLength) {
            textBeforeCursor = textBeforeCursor.substring(textBeforeCursor.length - maxLength);
        }
        
        return textBeforeCursor;
    },

    /**
     * Set HTML content of an editor element
     * @param {HTMLElement} element - The editor element
     * @param {string} html - The HTML to set
     */
    setHtml: function (element, html) {
        if (element) {
            element.innerHTML = html;
        }
    },

    /**
     * Query the current state of a command
     * @param {string} command - The command to query
     * @returns {boolean} Whether the command is active
     */
    queryCommandState: function (command) {
        const commandMap = {
            'ul': 'insertUnorderedList',
            'ol': 'insertOrderedList',
            'h1': 'formatBlock',
            'h2': 'formatBlock',
            'h3': 'formatBlock',
            'h4': 'formatBlock',
            'blockquote': 'formatBlock',
            'strikethrough': 'strikeThrough'
        };
        
        try {
            const execCommand = commandMap[command] || command;
            return document.queryCommandState(execCommand);
        } catch (e) {
            return false;
        }
    },

    /**
     * Query the current value of a command (e.g., current block format)
     * @param {string} command - The command to query
     * @returns {string} The current value
     */
    queryCommandValue: function (command) {
        try {
            return document.queryCommandValue(command);
        } catch (e) {
            return '';
        }
    },

    /**
     * Find and replace text in the given editor element. Operates on TEXT NODES only — a
     * replace over the innerHTML string can match inside tags and attribute values and corrupt
     * the markup. Known limitation: matches are per text node; text split across inline
     * formatting (e.g. foo<b>bar</b>) is not matched.
     * @param {HTMLElement} element - The editor element
     * @param {string} find - Text to find
     * @param {string} replace - Text to replace with
     * @param {boolean} replaceAll - Whether to replace all occurrences
     * @returns {number} Number of replacements made
     */
    findAndReplace: function (element, find, replace, replaceAll = false) {
        if (!element || !find) return 0;

        const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT, null);
        const textNodes = [];
        while (walker.nextNode()) textNodes.push(walker.currentNode);

        const regex = new RegExp(this._escapeRegExp(find), 'g');
        let count = 0;

        for (const node of textNodes) {
            const text = node.textContent || '';
            regex.lastIndex = 0;
            if (!regex.test(text)) continue;

            regex.lastIndex = 0;
            const pieces = [];
            let lastIndex = 0;
            let match;
            while ((match = regex.exec(text)) !== null) {
                count++;
                pieces.push(document.createTextNode(text.substring(lastIndex, match.index)));
                pieces.push(document.createTextNode(replace));
                lastIndex = match.index + match[0].length;
                if (!replaceAll) break;
            }
            pieces.push(document.createTextNode(text.substring(lastIndex)));

            const parent = node.parentNode;
            if (!parent) continue;
            parent.insertBefore(pieces[0], node);
            for (let i = 1; i < pieces.length; i++) {
                parent.insertBefore(pieces[i], pieces[i - 1].nextSibling);
            }
            parent.removeChild(node);

            if (!replaceAll) break;
        }

        return count;
    },

    // ── Caret bookmark across the RTE dialogs ──────────────────────────────────
    // The dialogs are TmModals (FZE-10): opening one moves focus into the dialog and the
    // browser selection leaves the editor. The bookmark is a NON-MUTATING Range record (the
    // same shape markdownEditor.js uses) keyed per editor element in a WeakMap — the former
    // hidden #tm-cursor-marker span mutated the edited document, leaked into getHtml()/saved
    // Value and collapsed every selection to a caret.

    _caretBookmarks: new WeakMap(),

    /**
     * Save the current selection of the editor element as a Range bookmark so it can be
     * replayed later. No-op when the selection lives outside the element.
     * @param {HTMLElement} element - The editor element
     * @returns {boolean} true when a bookmark was saved
     */
    saveCursorPosition: function (element) {
        if (!element) return false;
        const selection = window.getSelection();
        if (!selection || selection.rangeCount === 0) return false;
        const range = selection.getRangeAt(0);
        if (!element.contains(range.commonAncestorContainer)) return false;
        this._caretBookmarks.set(element, {
            startContainer: range.startContainer,
            startOffset: range.startOffset,
            endContainer: range.endContainer,
            endOffset: range.endOffset
        });
        return true;
    },

    /**
     * Restore the saved (full, non-collapsed) selection into the editor element and delete the
     * bookmark. Refuses when either endpoint left the element (e.g. the content was replaced
     * while the dialog was open).
     * @param {HTMLElement} element - The editor element
     * @returns {boolean} true if the selection was restored
     */
    restoreCursorPosition: function (element) {
        if (!element) return false;
        const bookmark = this._caretBookmarks.get(element);
        this._caretBookmarks.delete(element);
        if (!bookmark) return false;
        try {
            if (!element.contains(bookmark.startContainer) || !element.contains(bookmark.endContainer)) return false;
            const range = document.createRange();
            range.setStart(bookmark.startContainer, bookmark.startOffset);
            range.setEnd(bookmark.endContainer, bookmark.endOffset);
            element.focus();
            const selection = window.getSelection();
            selection.removeAllRanges();
            selection.addRange(range);
            return true;
        } catch (e) {
            return false;
        }
    },

    /**
     * Focus the given editor element, replaying its caret bookmark first when one exists.
     * @param {HTMLElement|null} element - The editor element (falls back to the shared
     *   heuristic when the caller has no element reference)
     */
    _focusEditor: function (element) {
        if (element) {
            if (!this.restoreCursorPosition(element)) {
                element.focus();
            }
        } else {
            this._ensureFocus();
        }
    },

    /**
     * Ensure editor is focused
     */
    _ensureFocus: function () {
        const activeElement = document.activeElement;
        if (!activeElement || activeElement.contentEditable !== 'true') {
            const editors = document.querySelectorAll('[contenteditable="true"]');
            if (editors.length > 0) {
                editors[0].focus();
            }
        }
    },

    /**
     * Restore focus to the last active editor
     */
    _restoreFocus: function () {
        const activeElement = document.activeElement;
        const isContentEditable = activeElement && activeElement.contentEditable === 'true';
        
        // Only restore focus if no editor is currently focused
        if (!isContentEditable) {
            const editors = document.querySelectorAll('[contenteditable="true"]');
            if (editors.length > 0) {
                editors[0].focus();
            }
        }
    },

    /**
     * Get the currently active editor
     * @returns {HTMLElement|null}
     */
    _getActiveEditor: function () {
        const activeElement = document.activeElement;
        if (activeElement && activeElement.contentEditable === 'true') {
            return activeElement;
        }
        const editors = document.querySelectorAll('[contenteditable="true"]');
        return editors.length > 0 ? editors[0] : null;
    },

    /**
     * Escape HTML special characters
     * @param {string} text 
     * @returns {string}
     */
    _escapeHtml: function (text) {
        if (!text) return '';
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    },

    /**
     * Escape special regex characters
     * @param {string} string 
     * @returns {string}
     */
    _escapeRegExp: function (string) {
        return string.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    },

    /**
     * Insert a token chip into the editor (without trigger deletion, e.g. from toolbar button)
     * @param {HTMLElement} element - The editor element
     * @param {string} key - The token key (e.g. "user.email")
     * @param {string} displayName - The display name (e.g. "User Email")
     * @param {object} [meta] - Optional metadata: { description, icon, colorClass, typeLabel }
     */
    insertToken: function (element, key, displayName, meta) {
        // Restore the saved caret bookmark first (e.g. after a dialog closed); focus otherwise
        this._focusEditor(element);

        const tokenSpan = this._createTokenSpan(key, displayName, meta);

        const selection = window.getSelection();
        if (selection.rangeCount > 0) {
            const range = selection.getRangeAt(0);
            range.deleteContents();
            range.insertNode(tokenSpan);

            // Place cursor after the inserted token
            const cursorRange = document.createRange();
            cursorRange.setStartAfter(tokenSpan);
            cursorRange.collapse(true);
            selection.removeAllRanges();
            selection.addRange(cursorRange);
        }
    },

    /**
     * Find the {{ trigger in the editor, delete it along with query text, and insert a token chip.
     * Combined into one operation to avoid selection/focus loss between JS interop calls.
     * @param {HTMLElement} element - The editor element
     * @param {string} key - The token key
     * @param {string} displayName - The display name
     * @param {object} [meta] - Optional metadata: { description, icon, colorClass, typeLabel }
     */
    replaceTokenTrigger: function (element, key, displayName, meta) {
        if (!element) return;
        element.focus();

        const selection = window.getSelection();
        if (selection.rangeCount === 0) return;

        const range = selection.getRangeAt(0);
        let textNode = range.startContainer;
        let offset = range.startOffset;

        // If cursor is not in a text node, try to find one
        if (textNode.nodeType !== Node.TEXT_NODE) {
            const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT, null);
            let lastTextNode = null;
            while (walker.nextNode()) {
                const content = walker.currentNode.textContent || '';
                if (content.includes('{{')) {
                    lastTextNode = walker.currentNode;
                }
            }
            if (lastTextNode) {
                textNode = lastTextNode;
                offset = textNode.textContent.length;
            } else {
                return;
            }
        }

        const textContent = textNode.textContent || '';
        const textBeforeCursor = textContent.substring(0, offset);

        // Find the last {{ before cursor
        const lastTriggerIndex = textBeforeCursor.lastIndexOf('{{');
        if (lastTriggerIndex >= 0) {
            // Delete the {{ and any query text after it
            const deleteRange = document.createRange();
            deleteRange.setStart(textNode, lastTriggerIndex);
            deleteRange.setEnd(textNode, offset);
            deleteRange.deleteContents();

            // Create token element via DOM (not execCommand which can break out of parent divs)
            const tokenSpan = this._createTokenSpan(key, displayName, meta);

            // Insert at the deletion point
            const insertRange = document.createRange();
            insertRange.setStart(textNode, lastTriggerIndex);
            insertRange.collapse(true);
            insertRange.insertNode(tokenSpan);

            // Place cursor after the inserted token
            const cursorRange = document.createRange();
            cursorRange.setStartAfter(tokenSpan);
            cursorRange.collapse(true);
            selection.removeAllRanges();
            selection.addRange(cursorRange);
        }
    },

    /**
     * Delete the token trigger text ({{ and any typed query) before cursor
     * @param {HTMLElement} element - The editor element
     */
    deleteTokenTrigger: function (element) {
        if (!element) return;
        element.focus();

        const selection = window.getSelection();
        if (selection.rangeCount > 0) {
            const range = selection.getRangeAt(0);
            const textNode = range.startContainer;
            const offset = range.startOffset;

            const textContent = textNode.textContent || '';
            const textBeforeCursor = textContent.substring(0, offset);

            // Find the last {{ before cursor
            const lastTriggerIndex = textBeforeCursor.lastIndexOf('{{');
            if (lastTriggerIndex >= 0) {
                const deleteRange = document.createRange();
                deleteRange.setStart(textNode, lastTriggerIndex);
                deleteRange.setEnd(textNode, offset);
                deleteRange.deleteContents();
            }
        }

        // Save cursor position so it can be restored after dialog closes
        this.saveCursorPosition(element);
    },

    /**
     * Get text typed after the {{ token trigger
     * @param {HTMLElement} element - The editor element
     * @param {string} trigger - The trigger string (default "{{")
     * @returns {string|null} The text after trigger, or null if no trigger found
     */
    getTextBeforeCursorForToken: function (element, trigger) {
        if (!element) return null;
        trigger = trigger || '{{';

        const selection = window.getSelection();
        if (selection.rangeCount === 0) return null;

        const range = selection.getRangeAt(0);
        const cursorNode = range.startContainer;
        const cursorOffset = range.startOffset;

        const preCursorRange = document.createRange();
        preCursorRange.setStart(element, 0);
        preCursorRange.setEnd(cursorNode, cursorOffset);

        const fragment = preCursorRange.cloneContents();
        const tempDiv = document.createElement('div');
        tempDiv.appendChild(fragment);
        // Remove existing token chips so their {{DisplayName}} text doesn't trigger false matches
        tempDiv.querySelectorAll('.tm-token').forEach(el => el.remove());
        let textBeforeCursor = tempDiv.textContent || '';

        if (textBeforeCursor.length > 100) {
            textBeforeCursor = textBeforeCursor.substring(textBeforeCursor.length - 100);
        }

        const lastTriggerIndex = textBeforeCursor.lastIndexOf(trigger);
        if (lastTriggerIndex < 0) return null;

        const textAfterTrigger = textBeforeCursor.substring(lastTriggerIndex + trigger.length);

        if (textAfterTrigger.includes(' ') || textAfterTrigger.includes('\n')) {
            return null;
        }

        return textAfterTrigger;
    },

    /**
     * Initialize keyboard shortcuts on an editor element
     * @param {HTMLElement} element - The editor element
     */
    initKeyboardShortcuts: function (element) {
        if (!element) return;

        // Remove previous handler if re-initializing
        if (element._tmKeydownHandler) {
            element.removeEventListener('keydown', element._tmKeydownHandler);
        }

        const handler = (e) => {
            // Ctrl/Cmd + B - Bold
            if ((e.ctrlKey || e.metaKey) && e.key === 'b') {
                e.preventDefault();
                this.execCommand('bold');
            }
            // Ctrl/Cmd + I - Italic
            else if ((e.ctrlKey || e.metaKey) && e.key === 'i') {
                e.preventDefault();
                this.execCommand('italic');
            }
            // Ctrl/Cmd + U - Underline
            else if ((e.ctrlKey || e.metaKey) && e.key === 'u') {
                e.preventDefault();
                this.execCommand('underline');
            }
            // Ctrl/Cmd + K - Link
            else if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
                e.preventDefault();
                // This will be handled by Blazor component
                element.dispatchEvent(new CustomEvent('linkShortcut'));
            }
            // Ctrl/Cmd + Z - Undo
            else if ((e.ctrlKey || e.metaKey) && e.key === 'z' && !e.shiftKey) {
                e.preventDefault();
                this.execCommand('undo');
            }
            // Ctrl/Cmd + Shift + Z or Ctrl/Cmd + Y - Redo
            else if (((e.ctrlKey || e.metaKey) && e.shiftKey && e.key === 'z') ||
                     ((e.ctrlKey || e.metaKey) && e.key === 'y')) {
                e.preventDefault();
                this.execCommand('redo');
            }
        };

        element._tmKeydownHandler = handler;
        element.addEventListener('keydown', handler);
    },

    /**
     * Create a token <span> chip with optional metadata stored as data attributes.
     * @param {string} key
     * @param {string} displayName
     * @param {object} [meta] - { description, icon, colorClass, typeLabel }
     * @returns {HTMLSpanElement}
     */
    _createTokenSpan: function (key, displayName, meta) {
        const span = document.createElement('span');
        span.className = 'tm-token';
        if (meta && meta.colorClass) {
            span.classList.add(meta.colorClass);
        }
        span.setAttribute('data-token-key', key);
        span.setAttribute('data-token-display', displayName);
        if (meta) {
            if (meta.description) span.setAttribute('data-token-description', meta.description);
            if (meta.icon) span.setAttribute('data-token-icon', meta.icon);
            if (meta.typeLabel) span.setAttribute('data-token-type-label', meta.typeLabel);
        }
        span.contentEditable = 'false';

        // Build visible chip: optional icon + display text
        if (meta && meta.icon) {
            const iconSpan = document.createElement('span');
            iconSpan.className = 'tm-token-chip-icon';
            iconSpan.textContent = meta.icon;
            span.appendChild(iconSpan);
            span.appendChild(document.createTextNode(`{{${displayName}}}`));
        } else {
            span.textContent = `{{${displayName}}}`;
        }

        return span;
    },

    /**
     * Initialize token hover callbacks on an editor element.
     * On mouseenter over a .tm-token chip, calls dotNetRef.invokeMethodAsync('OnTokenHoverEnter', args).
     * On mouseleave, calls dotNetRef.invokeMethodAsync('OnTokenHoverLeave').
     * The Blazor component is responsible for rendering any tooltip/preview.
     * @param {HTMLElement} element - The editor element
     * @param {DotNetObjectReference} dotNetRef - Reference to the Blazor component
     */
    initTokenHoverPreview: function (element, dotNetRef) {
        if (!element || element._tmTokenHoverHandler) return;

        const showHandler = (e) => {
            const token = e.target.closest('.tm-token');
            if (!token) return;

            const rect = token.getBoundingClientRect();
            const args = {
                key: token.getAttribute('data-token-key') || '',
                displayName: token.getAttribute('data-token-display') || '',
                description: token.getAttribute('data-token-description') || null,
                icon: token.getAttribute('data-token-icon') || null,
                colorClass: token.getAttribute('data-token-color-class') || null,
                typeLabel: token.getAttribute('data-token-type-label') || null,
                rectLeft: rect.left,
                rectTop: rect.top,
                rectRight: rect.right,
                rectBottom: rect.bottom,
                rectWidth: rect.width,
                rectHeight: rect.height
            };

            dotNetRef.invokeMethodAsync('OnTokenHoverEnter', args).catch(() => {});
        };

        const hideHandler = (e) => {
            const token = e.target.closest('.tm-token');
            if (!token) return;
            // Only fire leave when moving away from the token chip itself
            if (!token.contains(e.relatedTarget)) {
                dotNetRef.invokeMethodAsync('OnTokenHoverLeave').catch(() => {});
            }
        };

        element.addEventListener('mouseover', showHandler);
        element.addEventListener('mouseout', hideHandler);
        element._tmTokenHoverHandler = { show: showHandler, hide: hideHandler };
    },

    /**
     * Clean up an editor element (remove event listeners)
     * @param {HTMLElement} element - The editor element
     */
    destroy: function (element) {
        if (!element) return;
        if (element._tmKeydownHandler) {
            element.removeEventListener('keydown', element._tmKeydownHandler);
            delete element._tmKeydownHandler;
        }
        if (element._tmTokenHoverHandler) {
            element.removeEventListener('mouseover', element._tmTokenHoverHandler.show);
            element.removeEventListener('mouseout', element._tmTokenHoverHandler.hide);
            delete element._tmTokenHoverHandler;
        }
    }
};
