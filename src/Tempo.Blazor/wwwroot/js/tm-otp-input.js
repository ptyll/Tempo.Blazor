// Input handling for TmOtpInput.
//
// Blazor cannot intercept beforeinput, keep IME composition out of the .NET circuit, read the
// pasted text (ClipboardEventArgs carries no clipboard data across the Blazor boundary) or move
// focus between cells without a round-trip, so this module owns every per-cell DOM event. The
// component owns the model (one character per cell) and exposes two [JSInvokable] entry points;
// the module calls them through the DotNetObjectReference passed to attach():
//
//   inputMethod     (index, value)  — a single cell changed to `value` (one accepted character or
//                                     an empty string when the cell was cleared). Fired for typed
//                                     characters, autofill replacements and delete* input types.
//   distributeMethod(index, raw)    — a multi-character insert (paste, autofill, drop) starting at
//                                     `index`. The accepted characters are distributed over the
//                                     consecutive cells from that index on; a paste longer than the
//                                     remaining cells is truncated to them.
//
// Filtering happens here, before the browser applies the edit: disallowed characters per Kind are
// dropped with preventDefault() (paste text with spaces/dashes is cleaned the same way),
// Alphanumeric input is uppercased and a single typed character always replaces the cell content
// (the INSERTED character wins, not the last one). Paste is handled on the `paste` event itself —
// readPastedText() pulls text/plain out of the clipboard event, the default insertion is cancelled
// and the code is distributed from the cell that received the paste. Browser autofill (e.g. iOS
// one-time-code) writes the whole code in one input event without beforeinput; the input listener
// detects a multi-character value and distributes it the same way.
//
// Composition sessions are left alone while they run (isComposing input is skipped) and committed
// on compositionend. Arrow keys, Home/End, Backspace-step-back and the redirect-to-first-empty-cell
// guard all run locally — no circuit traffic for pure navigation. Disabled and readOnly (Verified /
// ReadOnly states) cells are ignored everywhere: the browser already blocks their edits, and the
// module must not synthesize any.
//
// After any C# correction the component pushes the authoritative cell values back through
// setCellValue() (single edit) or the batched setCellValues() (distribute / clear). No maxlength
// anywhere: length is enforced by the model, not the DOM. Autofill replacements
// (insertReplacementText) and drops (insertFromDrop) carry their text on dataTransfer when
// event.data is null; a payload-less beforeinput is left to the browser so the input listener
// distributes the value the browser actually wrote. Paste prefers the LAST run of accepted
// characters whose length equals the remaining cell count ("Your code: AB12CD" → "AB12CD").
//
// State is one session per root element in a WeakMap (listeners + dotnet ref); detach() removes
// every listener so no session outlives the component. Every entry point is null-safe and
// tolerates a DOM that is already gone. No eval, no inline handlers — CSP needs no 'unsafe-inline'
// for this file. Node tests: __tests__/tm-otp-input.test.mjs.

/** Live sessions, keyed by the component root element. */
const sessions = new WeakMap();

/** Cells in DOM (== index) order. */
function cells(root) {
    return Array.from(root?.querySelectorAll?.('input[data-tm-otp-index]') ?? []);
}

function cellAt(root, index) {
    return cells(root)[index] ?? null;
}

function indexOf(cell) {
    return Number(cell?.getAttribute?.('data-tm-otp-index') ?? -1);
}

function kindOf(root) {
    return root?.dataset?.tmOtpKind === 'alphanumeric' ? 'alphanumeric' : 'numeric';
}

function isAccepted(ch, kind) {
    if (kind === 'numeric') {
        return ch >= '0' && ch <= '9';
    }

    return (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z');
}

/** Keeps only Kind-accepted characters; Alphanumeric normalizes to uppercase. */
function filterData(data, kind) {
    let out = '';
    for (const ch of data ?? '') {
        if (isAccepted(ch, kind)) {
            out += kind === 'alphanumeric' ? ch.toUpperCase() : ch;
        }
    }

    return out;
}

/** First empty cell index, or cells.length when the code is complete. */
function firstEmptyIndex(root) {
    for (const cell of cells(root)) {
        if (cell.value === '') {
            return indexOf(cell);
        }
    }

    return cells(root).length;
}

/** Fire-and-forget .NET callback; a dropped circuit must not surface as an unhandled error. */
function invoke(dotNetRef, method, ...args) {
    try {
        const result = dotNetRef?.invokeMethodAsync?.(method, ...args);
        if (result && typeof result.catch === 'function') {
            result.catch(() => { });
        }
    } catch {
        // Same case, synchronously.
    }
}

/** Editing is module-driven; disabled and readOnly (Verified / ReadOnly) cells opt out of it. */
function isEditable(cell) {
    return !!cell && !cell.disabled && !cell.readOnly;
}

/**
 * Reads the pasted plain text out of a ClipboardEvent. Blazor's ClipboardEventArgs carries no
 * clipboard data, so the paste event is consumed here (in the module) instead of round-tripping.
 */
export function readPastedText(event) {
    try {
        return event?.clipboardData?.getData?.('text/plain') ?? '';
    } catch {
        // A host that revoked clipboard access gets the browser no-op instead.
        return '';
    }
}

function setCellValueInternal(cell, value) {
    if (cell) {
        cell.value = value ?? '';
    }
}

function focusCellInternal(root, index, select) {
    const cell = cellAt(root, index);
    if (cell && !cell.disabled) {
        cell.focus();
        if (select) {
            cell.select?.();
        }
    }
}

/** Writes `text` into the consecutive cells from `index` on (truncated at the last cell). */
function writeDistributed(root, index, text) {
    const all = cells(root);
    for (let i = 0; i < (text?.length ?? 0) && index + i < all.length; i++) {
        all[index + i].value = text[i];
    }
}

/** Filtered text clipped to the cells that remain from `index` on. */
function remainingText(root, index, filtered) {
    return filtered.slice(0, Math.max(0, cells(root).length - index));
}

/**
 * Paste heuristic: split the raw text on non-accepted characters and prefer the LAST run whose
 * length is EXACTLY the remaining cell count. A copied "Your code: AB12CD" pasted into six free
 * Alphanumeric cells must distribute "AB12CD", not the truncated "YOURCO" a flat filter produces.
 * Returns null when no run matches — the caller then truncates the flat filter.
 */
function bestRun(text, kind, remaining) {
    const runs = [];
    let current = '';
    for (const ch of text ?? '') {
        if (isAccepted(ch, kind)) {
            current += kind === 'alphanumeric' ? ch.toUpperCase() : ch;
        } else if (current.length > 0) {
            runs.push(current);
            current = '';
        }
    }

    if (current.length > 0) {
        runs.push(current);
    }

    for (let i = runs.length - 1; i >= 0; i--) {
        if (runs[i].length === remaining) {
            return runs[i];
        }
    }

    return null;
}

/** Filtered insert text clipped to the remaining cells, with the last-exact-run paste heuristic. */
function distributeText(root, index, raw) {
    const kind = kindOf(root);
    const remaining = Math.max(0, cells(root).length - index);
    const filtered = filterData(raw, kind);
    if (filtered.length <= remaining) {
        return filtered;
    }

    return bestRun(raw, kind, remaining) ?? filtered.slice(0, remaining);
}

/**
 * Wires every cell inside `root`. `options` carries the [JSInvokable] method names:
 * { inputMethod, distributeMethod }.
 */
export function attach(root, dotNetRef, options) {
    if (!root) {
        return;
    }

    detach(root);

    const inputMethod = options?.inputMethod ?? 'OnCellInputAsync';
    const distributeMethod = options?.distributeMethod ?? 'DistributeFromAsync';
    const composing = new Set();
    const listeners = [];

    const on = (target, type, handler) => {
        target.addEventListener(type, handler);
        listeners.push([target, type, handler]);
    };

    const onBeforeInput = (event) => {
        const cell = event.currentTarget ?? event.target;
        if (!cell || event.isComposing || event.inputType === 'insertCompositionText') {
            // IME session — compositionend commits it.
            return;
        }

        if (!isEditable(cell)) {
            return;
        }

        const inputType = event.inputType ?? '';
        if (!inputType.startsWith('insert')) {
            // Deletes are reported by the input listener after the browser applied them.
            return;
        }

        // Autofill (insertReplacementText) and drops (insertFromDrop) may carry the text on
        // dataTransfer instead of data; with no payload at all the insert is left to the browser
        // (no preventDefault) — the input listener distributes whatever landed, which is how iOS
        // one-time-code autofill reaches the cells.
        const raw = event.data ?? event.dataTransfer?.getData?.('text/plain') ?? null;
        if (raw == null) {
            return;
        }

        const filtered = filterData(raw, kindOf(root));
        if (filtered.length === 0) {
            event.preventDefault?.();
            return;
        }

        event.preventDefault?.();

        if (filtered.length === 1) {
            // Typing into a later cell while an earlier one is empty fills the FIRST empty cell;
            // the INSERTED character wins (caret-before insert included).
            const index = Math.min(indexOf(cell), firstEmptyIndex(root));
            setCellValueInternal(cellAt(root, index), filtered);
            invoke(dotNetRef, inputMethod, index, filtered);
            focusCellInternal(root, index + 1, true);
        } else {
            // Multi-character insert (paste fallback, autofill, drop) — distribute from the cell
            // that received it (paste semantics), truncated at the last cell.
            const index = indexOf(cell);
            const text = distributeText(root, index, raw);
            writeDistributed(root, index, text);
            invoke(dotNetRef, distributeMethod, index, text);
        }
    };

    const onInput = (event) => {
        const cell = event.currentTarget ?? event.target;
        if (!cell || composing.has(cell) || !isEditable(cell)) {
            return;
        }

        const inputType = event.inputType ?? '';
        const kind = kindOf(root);
        if (inputType.startsWith('delete')) {
            invoke(dotNetRef, inputMethod, indexOf(cell), cell.value ?? '');
            return;
        }

        const value = cell.value ?? '';
        if (value.length > 1) {
            // Autofill (e.g. iOS one-time-code) writes the whole code at once with no
            // beforeinput — distribute it from the cell it landed in.
            const filtered = distributeText(root, indexOf(cell), value);
            if (filtered.length === 0) {
                setCellValueInternal(cell, '');
                invoke(dotNetRef, inputMethod, indexOf(cell), '');
                return;
            }

            const index = indexOf(cell);
            writeDistributed(root, index, filtered);
            invoke(dotNetRef, distributeMethod, index, filtered);
            return;
        }

        if (inputType.startsWith('insert')) {
            // Single-character autofill without beforeinput.
            const filtered = filterData(value, kind);
            if (filtered.length === 0) {
                setCellValueInternal(cell, '');
                invoke(dotNetRef, inputMethod, indexOf(cell), '');
                return;
            }

            const index = Math.min(indexOf(cell), firstEmptyIndex(root));
            if (index !== indexOf(cell)) {
                // The character landed past a gap — move it to the first empty cell.
                setCellValueInternal(cell, '');
            }
            setCellValueInternal(cellAt(root, index), filtered);
            invoke(dotNetRef, inputMethod, index, filtered);
            focusCellInternal(root, index + 1, true);
        }
    };

    const onPaste = (event) => {
        const cell = event.target;
        if (!cell || typeof cell.getAttribute !== 'function'
            || cell.getAttribute('data-tm-otp-index') === null) {
            return;
        }

        if (!isEditable(cell)) {
            // readOnly/disabled cells already no-op; do not fight the browser.
            return;
        }

        // ClipboardEventArgs carries no clipboard text, so the paste is consumed here: the default
        // insertion is cancelled and the (cleaned) code distributes from the pasted-into cell.
        event.preventDefault?.();
        const filtered = distributeText(root, indexOf(cell), readPastedText(event));
        if (filtered.length === 0) {
            return;
        }

        const index = indexOf(cell);
        writeDistributed(root, index, filtered);
        invoke(dotNetRef, distributeMethod, index, filtered);
    };

    const onCompositionStart = (event) => {
        composing.add(event.currentTarget ?? event.target);
    };

    const onCompositionEnd = (event) => {
        const cell = event.currentTarget ?? event.target;
        composing.delete(cell);
        if (!cell || !isEditable(cell)) {
            return;
        }

        const filtered = filterData(event.data ?? cell.value, kindOf(root));
        const index = indexOf(cell);
        if (filtered.length === 0) {
            setCellValueInternal(cell, '');
            invoke(dotNetRef, inputMethod, index, '');
            return;
        }

        if (filtered.length === 1) {
            const target = Math.min(index, firstEmptyIndex(root));
            setCellValueInternal(cellAt(root, target), filtered);
            invoke(dotNetRef, inputMethod, target, filtered);
            focusCellInternal(root, target + 1, true);
        } else {
            invoke(dotNetRef, distributeMethod, index, filtered);
        }
    };

    const onKeyDown = (event) => {
        const cell = event.currentTarget ?? event.target;
        if (!cell) {
            return;
        }

        const index = indexOf(cell);
        const last = cells(root).length - 1;
        switch (event.key) {
            case 'Backspace':
                if (!isEditable(cell)) {
                    break;
                }
                // Filled cell: the browser deletes the character and the input listener reports
                // it. Empty cell: step back — clear the previous cell and focus it.
                if (cell.value === '' && index > 0) {
                    event.preventDefault?.();
                    setCellValueInternal(cellAt(root, index - 1), '');
                    invoke(dotNetRef, inputMethod, index - 1, '');
                    focusCellInternal(root, index - 1, false);
                }
                break;
            case 'ArrowLeft':
                if (index > 0) {
                    event.preventDefault?.();
                    focusCellInternal(root, index - 1, false);
                }
                break;
            case 'ArrowRight':
                if (index < last) {
                    event.preventDefault?.();
                    focusCellInternal(root, index + 1, false);
                }
                break;
            case 'Home':
                if (index > 0) {
                    event.preventDefault?.();
                    focusCellInternal(root, 0, false);
                }
                break;
            case 'End':
                if (index < last) {
                    event.preventDefault?.();
                    focusCellInternal(root, last, false);
                }
                break;
        }
    };

    // Redirect POINTER-driven focus (tap/click past a gap) to the first empty cell. Keyboard and
    // programmatic focus are left alone — the redirect must not fight arrow-key navigation.
    let pointerFocus = false;
    const onPointerDown = (event) => {
        if (typeof event.target?.getAttribute === 'function'
            && event.target.getAttribute('data-tm-otp-index') !== null) {
            pointerFocus = true;
        }
    };

    const onFocusIn = (event) => {
        const cell = event.target;
        if (!cell || typeof cell.getAttribute !== 'function'
            || cell.getAttribute('data-tm-otp-index') === null) {
            return;
        }

        const index = indexOf(cell);
        const first = firstEmptyIndex(root);
        if (pointerFocus && first < index) {
            focusCellInternal(root, first, false);
        }

        pointerFocus = false;
    };

    for (const cell of cells(root)) {
        on(cell, 'beforeinput', onBeforeInput);
        on(cell, 'input', onInput);
        on(cell, 'compositionstart', onCompositionStart);
        on(cell, 'compositionend', onCompositionEnd);
        on(cell, 'keydown', onKeyDown);
    }

    on(root, 'focusin', onFocusIn);
    on(root, 'pointerdown', onPointerDown);
    on(root, 'paste', onPaste);

    sessions.set(root, { listeners, dotNetRef });
}

/** Removes every listener the attach() call registered on this root. */
export function detach(root) {
    const session = sessions.get(root);
    if (!session) {
        return;
    }

    sessions.delete(root);
    for (const [target, type, handler] of session.listeners) {
        target.removeEventListener?.(type, handler);
    }
}

/** Pushes a C#-corrected cell value into the DOM (no events raised). */
export function setCellValue(root, index, value) {
    setCellValueInternal(cellAt(root, index), value);
}

/**
 * Pushes the whole model array into the cells in ONE batched write (no events raised). Used by
 * DistributeFromAsync / ClearAsync instead of a per-cell round-trip.
 */
export function setCellValues(root, values) {
    const all = cells(root);
    for (let i = 0; i < (values?.length ?? 0) && i < all.length; i++) {
        all[i].value = values[i] ?? '';
    }
}

/** Restarts the one-shot invalid shake: class off → forced reflow → class on. */
export function restartShake(root) {
    if (!root || !root.classList) {
        return;
    }

    root.classList.remove('tm-otp-input--invalid');
    void root.offsetWidth; // reflow: without it the re-added class does not restart the animation
    root.classList.add('tm-otp-input--invalid');
}

export function focusCell(root, index) {
    focusCellInternal(root, index, false);
}

export function selectCell(root, index) {
    focusCellInternal(root, index, true);
}
