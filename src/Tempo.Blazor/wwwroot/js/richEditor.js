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
     * Insert a link at current selection
     * @param {string} url - The URL
     * @param {string} text - The display text (optional, uses selection if empty)
     */
    insertLink: function (url, text) {
        this._ensureFocus();
        
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
        this._restoreFocus();
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
        console.log('[DEBUG] selection.rangeCount:', selection.rangeCount);
        
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
     * Insert an image
     * @param {string} url - Image URL
     * @param {string} alt - Alt text
     */
    insertImage: function (url, alt = '') {
        this._ensureFocus();
        const imgHtml = `<img src="${this._escapeHtml(url)}" alt="${this._escapeHtml(alt)}" class="tm-rte-image" />`;
        document.execCommand('insertHTML', false, imgHtml);
        this._restoreFocus();
    },

    /**
     * Insert a table
     * @param {number} rows - Number of rows
     * @param {number} cols - Number of columns
     */
    insertTable: function (rows, cols) {
        this._ensureFocus();
        
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
        this._restoreFocus();
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
     * Insert video embed (iframe)
     * @param {string} embedUrl - The embed URL
     * @param {string} title - Video title
     */
    insertVideo: function (embedUrl, title = 'Video') {
        this._ensureFocus();
        const videoHtml = `<div class="tm-rte-video-wrapper"><iframe src="${this._escapeHtml(embedUrl)}" title="${this._escapeHtml(title)}" frameborder="0" allowfullscreen></iframe></div><p><br></p>`;
        document.execCommand('insertHTML', false, videoHtml);
        this._restoreFocus();
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
     * Get HTML content of an editor element
     * @param {HTMLElement} element - The editor element
     * @returns {string} The inner HTML
     */
    getHtml: function (element) {
        return element ? element.innerHTML : '';
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
     * Find and replace text in the editor
     * @param {string} find - Text to find
     * @param {string} replace - Text to replace with
     * @param {boolean} replaceAll - Whether to replace all occurrences
     * @returns {number} Number of replacements made
     */
    findAndReplace: function (find, replace, replaceAll = false) {
        if (!find) return 0;
        
        const selection = window.getSelection();
        const range = selection.getRangeAt(0);
        const editor = this._getActiveEditor();
        
        if (!editor) return 0;
        
        let html = editor.innerHTML;
        const escapedFind = this._escapeRegExp(find);
        const regex = new RegExp(escapedFind, replaceAll ? 'g' : '');
        
        let count = 0;
        if (replaceAll) {
            const matches = html.match(regex);
            count = matches ? matches.length : 0;
            html = html.replace(regex, replace);
        } else {
            html = html.replace(regex, function(match) {
                count++;
                return replace;
            });
        }
        
        editor.innerHTML = html;
        return count;
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
     * @param {string} key - The token key (e.g. "user.email")
     * @param {string} displayName - The display name (e.g. "User Email")
     */
    insertToken: function (key, displayName) {
        this._ensureFocus();

        const tokenHtml = `<span class="tm-token" data-token-key="${this._escapeHtml(key)}" contenteditable="false">{{${this._escapeHtml(displayName)}}}</span>&nbsp;`;
        document.execCommand('insertHTML', false, tokenHtml);
        this._restoreFocus();
    },

    /**
     * Find the {{ trigger in the editor, delete it along with query text, and insert a token chip.
     * Combined into one operation to avoid selection/focus loss between JS interop calls.
     * @param {HTMLElement} element - The editor element
     * @param {string} key - The token key
     * @param {string} displayName - The display name
     */
    replaceTokenTrigger: function (element, key, displayName) {
        if (!element) return;
        element.focus();

        const selection = window.getSelection();
        if (selection.rangeCount === 0) return;

        const range = selection.getRangeAt(0);
        let textNode = range.startContainer;
        let offset = range.startOffset;

        // If cursor is not in a text node, try to find one
        if (textNode.nodeType !== Node.TEXT_NODE) {
            // Walk backwards through child nodes to find the text node with {{
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

            // Place cursor at deletion point and insert token
            const insertRange = document.createRange();
            insertRange.setStart(textNode, lastTriggerIndex);
            insertRange.collapse(true);
            selection.removeAllRanges();
            selection.addRange(insertRange);

            const tokenHtml = `<span class="tm-token" data-token-key="${this._escapeHtml(key)}" contenteditable="false">{{${this._escapeHtml(displayName)}}}</span>&nbsp;`;
            document.execCommand('insertHTML', false, tokenHtml);
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
     * Clean up an editor element (remove event listeners)
     * @param {HTMLElement} element - The editor element
     */
    destroy: function (element) {
        if (!element) return;
        if (element._tmKeydownHandler) {
            element.removeEventListener('keydown', element._tmKeydownHandler);
            delete element._tmKeydownHandler;
        }
    }
};
