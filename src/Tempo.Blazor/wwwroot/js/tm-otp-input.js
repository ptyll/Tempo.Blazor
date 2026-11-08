// Input handling for TmOtpInput.
//
// Blazor cannot intercept beforeinput, keep IME composition out of the .NET circuit or move focus
// between cells without a round-trip, so this module owns every per-cell DOM event. The component
// owns the model (one character per cell) and exposes two [JSInvokable] entry points; the module
// calls them through the DotNetObjectReference passed to attach():
//
//   inputMethod     (index, value)  — a single cell changed to `value` (one accepted character or
//                                     an empty string when the cell was cleared). Fired for typed
//                                     characters, autofill replacements and delete* input types.
//   distributeMethod(index, raw)    — a multi-character insert (paste, autofill, drop) starting at
//                                     `index`. Phase 2 owns the exact paste semantics; the C# side
//                                     already distributes the accepted characters from the index on.
//
// Filtering happens here, before the browser applies the edit: disallowed characters per Kind are
// dropped with preventDefault(), Alphanumeric input is uppercased and a single typed character
// always replaces the cell content (the INSERTED character wins, not the last one). Composition
// sessions are left alone while they run (isComposing input is skipped) and committed on
// compositionend. Arrow keys, Home/End, Backspace-step-back and the redirect-to-first-empty-cell
// guard all run locally — no circuit traffic for pure navigation.
//
// After any C# correction the component pushes the authoritative cell value back through
// setCellValue(). No maxlength anywhere: length is enforced by the model, not the DOM.
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

        const inputType = event.inputType ?? '';
        if (!inputType.startsWith('insert')) {
            // Deletes are reported by the input listener after the browser applied them.
            return;
        }

        const filtered = filterData(event.data, kindOf(root));
        if (filtered.length === 0) {
            event.preventDefault?.();
            return;
        }

        event.preventDefault?.();

        // Typing into a later cell while an earlier one is empty fills the FIRST empty cell.
        const index = Math.min(indexOf(cell), firstEmptyIndex(root));
        if (filtered.length === 1) {
            // The INSERTED character wins (caret-before-insert included): a filled cell is
            // replaced by the new character, not concatenated.
            setCellValueInternal(cellAt(root, index), filtered);
            invoke(dotNetRef, inputMethod, index, filtered);
            focusCellInternal(root, index + 1, true);
        } else {
            // Multi-character insert (paste / autofill / drop) — distribute from the index.
            invoke(dotNetRef, distributeMethod, index, filtered);
        }
    };

    const onInput = (event) => {
        const cell = event.currentTarget ?? event.target;
        if (!cell || composing.has(cell)) {
            return;
        }

        const inputType = event.inputType ?? '';
        if (inputType.startsWith('delete')) {
            invoke(dotNetRef, inputMethod, indexOf(cell), cell.value ?? '');
        }
    };

    const onCompositionStart = (event) => {
        composing.add(event.currentTarget ?? event.target);
    };

    const onCompositionEnd = (event) => {
        const cell = event.currentTarget ?? event.target;
        composing.delete(cell);
        if (!cell) {
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

export function focusCell(root, index) {
    focusCellInternal(root, index, false);
}

export function selectCell(root, index) {
    focusCellInternal(root, index, true);
}
