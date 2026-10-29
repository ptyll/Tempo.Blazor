window.tmSpreadsheetGrid = window.tmSpreadsheetGrid || {};
window.tmSpreadsheetFormulaBar = window.tmSpreadsheetFormulaBar || {};

window.tmSpreadsheetGrid.observeViewport = function (grid, dotNetRef) {
    if (!grid || !dotNetRef) return;

    if (typeof grid.__tmSpreadsheetViewportCleanup === "function") {
        grid.__tmSpreadsheetViewportCleanup();
    }

    let frame = 0;
    const notify = () => {
        if (frame) return;
        frame = requestAnimationFrame(() => {
            frame = 0;
            dotNetRef.invokeMethodAsync(
                "OnSpreadsheetViewportChanged",
                grid.scrollLeft || 0,
                grid.clientWidth || 0
            ).catch(() => {
                // Component was disposed before the queued viewport update ran.
            });
        });
    };

    const resizeObserver = typeof ResizeObserver !== "undefined"
        ? new ResizeObserver(notify)
        : null;

    grid.addEventListener("scroll", notify, { passive: true });
    if (resizeObserver) {
        resizeObserver.observe(grid);
    }

    grid.__tmSpreadsheetViewportCleanup = () => {
        grid.removeEventListener("scroll", notify);
        if (resizeObserver) {
            resizeObserver.disconnect();
        }
        if (frame) {
            cancelAnimationFrame(frame);
            frame = 0;
        }
        delete grid.__tmSpreadsheetViewportCleanup;
    };

    notify();
};

window.tmSpreadsheetGrid.disposeViewportObserver = function (grid) {
    if (!grid || typeof grid.__tmSpreadsheetViewportCleanup !== "function") return;
    grid.__tmSpreadsheetViewportCleanup();
};

window.tmSpreadsheetGrid.ensureCellVisible = function (grid, cell, options) {
    if (!grid || !cell) return;

    const rowHeaderWidth = options?.rowHeaderWidth ?? options?.RowHeaderWidth ?? 40;
    const columnHeaderHeight = options?.columnHeaderHeight ?? options?.ColumnHeaderHeight ?? 20;

    const left = cell.left ?? cell.Left ?? 0;
    const top = cell.top ?? cell.Top ?? 0;
    const right = cell.right ?? cell.Right ?? left;
    const bottom = cell.bottom ?? cell.Bottom ?? top;
    const frozenRow = cell.frozenRow ?? cell.FrozenRow ?? false;
    const frozenColumn = cell.frozenColumn ?? cell.FrozenColumn ?? false;

    let nextScrollLeft = grid.scrollLeft;
    let nextScrollTop = grid.scrollTop;

    if (!frozenColumn) {
        const visibleLeft = grid.scrollLeft + rowHeaderWidth;
        const visibleRight = grid.scrollLeft + grid.clientWidth;

        if (left < visibleLeft) {
            nextScrollLeft = left - rowHeaderWidth;
        } else if (right > visibleRight) {
            nextScrollLeft = right - grid.clientWidth;
        }
    }

    if (!frozenRow) {
        const visibleTop = grid.scrollTop + columnHeaderHeight;
        const visibleBottom = grid.scrollTop + grid.clientHeight;

        if (top < visibleTop) {
            nextScrollTop = top - columnHeaderHeight;
        } else if (bottom > visibleBottom) {
            nextScrollTop = bottom - grid.clientHeight;
        }
    }

    nextScrollLeft = Math.max(0, nextScrollLeft);
    nextScrollTop = Math.max(0, nextScrollTop);

    if (nextScrollLeft !== grid.scrollLeft || nextScrollTop !== grid.scrollTop) {
        grid.scrollTo({
            left: nextScrollLeft,
            top: nextScrollTop,
            behavior: "auto"
        });
    }
};

(function () {
    const cellRefPattern = /^\$?[A-Za-z]{1,3}\$?\d{1,7}$/;
    const functionBoundaryPattern = /[=+\-*/^&(,;<>:\s]/;
    const defaultFormulaArgumentSeparator = ",";
    const defaultFormulaDecimalSeparator = ".";

    function clampPosition(value, text) {
        const length = String(text || "").length;
        const numeric = Number(value);
        if (!Number.isFinite(numeric)) return length;
        return Math.max(0, Math.min(length, Math.floor(numeric)));
    }

    function normalizeCellRef(token) {
        return String(token || "").replace(/\$/g, "").toUpperCase();
    }

    function normalizeFormulaLocaleOptions(scopeOrOptions) {
        let source = scopeOrOptions;
        if (source instanceof Element) {
            const host = getSpreadsheetHost(source);
            source = host?.dataset || null;
        }

        const argumentSeparator = source?.argumentSeparator
            || source?.formulaArgumentSeparator
            || source?.formulaSeparator
            || defaultFormulaArgumentSeparator;
        const decimalSeparator = source?.decimalSeparator
            || source?.formulaDecimalSeparator
            || defaultFormulaDecimalSeparator;

        const normalizedArgumentSeparator = argumentSeparator === ";" ? ";" : defaultFormulaArgumentSeparator;
        const normalizedDecimalSeparator = decimalSeparator === "," ? "," : defaultFormulaDecimalSeparator;
        return {
            argumentSeparator: normalizedArgumentSeparator,
            decimalSeparator: normalizedDecimalSeparator
        };
    }

    function getAlternateArgumentSeparator(localeOptions) {
        return localeOptions.argumentSeparator === ";" ? "," : ";";
    }

    function isDigit(ch) {
        return /\d/.test(ch || "");
    }

    function isDecimalSeparatorAt(value, index, localeOptions) {
        const ch = value[index] || "";
        if (ch !== localeOptions.decimalSeparator) {
            return false;
        }

        const previous = value[index - 1] || "";
        const next = value[index + 1] || "";
        return isDigit(previous) || isDigit(next);
    }

    function isArgumentSeparatorAt(value, index, localeOptions) {
        const ch = value[index] || "";
        if (ch === localeOptions.argumentSeparator) {
            return !isDecimalSeparatorAt(value, index, localeOptions);
        }

        const alternate = getAlternateArgumentSeparator(localeOptions);
        if (ch === alternate) {
            return !isDecimalSeparatorAt(value, index, localeOptions);
        }

        return false;
    }

    function readFormulaNumberToken(value, start, localeOptions) {
        let index = start;
        let sawDigits = false;
        let sawDecimal = false;

        if (value[index] === localeOptions.decimalSeparator && isDigit(value[index + 1])) {
            sawDecimal = true;
            index += 1;
        }

        while (index < value.length) {
            const ch = value[index];
            if (isDigit(ch)) {
                sawDigits = true;
                index += 1;
                continue;
            }

            if (!sawDecimal
                && ch === localeOptions.decimalSeparator
                && sawDigits
                && isDigit(value[index + 1])) {
                sawDecimal = true;
                index += 1;
                continue;
            }

            break;
        }

        return sawDigits
            ? { type: "number", text: value.slice(start, index), start, end: index }
            : null;
    }

    function columnLettersToIndex(letters) {
        let result = 0;
        const value = String(letters || "").toUpperCase();
        for (let index = 0; index < value.length; index += 1) {
            result = result * 26 + (value.charCodeAt(index) - 64);
        }
        return Math.max(0, result - 1);
    }

    function parseCellRef(ref) {
        const match = /^([A-Z]{1,3})(\d{1,7})$/.exec(normalizeCellRef(ref));
        if (!match) return null;
        return {
            row: Math.max(0, Number(match[2]) - 1),
            col: columnLettersToIndex(match[1])
        };
    }

    function parseReferenceToken(token) {
        const raw = normalizeCellRef(token);
        const parts = raw.split(":");
        if (parts.length < 1 || parts.length > 2) return null;
        const start = parseCellRef(parts[0]);
        const end = parseCellRef(parts[1] || parts[0]);
        if (!start || !end) return null;
        return {
            raw,
            startRow: Math.min(start.row, end.row),
            startCol: Math.min(start.col, end.col),
            endRow: Math.max(start.row, end.row),
            endCol: Math.max(start.col, end.col)
        };
    }

    function isFormulaIdentifierBoundary(ch) {
        return !ch || !/[A-Za-z0-9_$]/.test(ch);
    }

    function readFormulaCellReference(value, start) {
        let index = start;
        if (value[index] === "$") index += 1;

        const lettersStart = index;
        while (index < value.length && /[A-Za-z]/.test(value[index]) && index - lettersStart < 3) {
            index += 1;
        }

        const lettersLength = index - lettersStart;
        if (lettersLength < 1 || lettersLength > 3) return null;

        if (value[index] === "$") index += 1;

        const digitsStart = index;
        while (index < value.length && /\d/.test(value[index]) && index - digitsStart < 7) {
            index += 1;
        }

        const digitsLength = index - digitsStart;
        if (digitsLength < 1 || digitsLength > 7) return null;

        return {
            text: value.slice(start, index),
            start,
            end: index
        };
    }

    function readFormulaReferenceToken(value, start) {
        const first = readFormulaCellReference(value, start);
        if (!first) return null;

        let end = first.end;
        let text = first.text;
        let type = "reference";
        if (value[end] === ":") {
            const second = readFormulaCellReference(value, end + 1);
            if (second) {
                end = second.end;
                text = value.slice(start, end);
                type = "range";
            }
        }

        const before = value[start - 1] || "";
        const after = value[end] || "";
        if (!isFormulaIdentifierBoundary(before) || !isFormulaIdentifierBoundary(after)) {
            return null;
        }

        return { type, text, start, end };
    }

    function tokenizeFormula(text, scopeOrOptions) {
        const value = String(text || "");
        const localeOptions = normalizeFormulaLocaleOptions(scopeOrOptions);
        const tokens = [];
        let index = 0;

        while (index < value.length) {
            const ch = value[index];

            if (/\s/.test(ch)) {
                const start = index;
                index += 1;
                while (index < value.length && /\s/.test(value[index])) index += 1;
                tokens.push({ type: "whitespace", text: value.slice(start, index), start, end: index });
                continue;
            }

            if (ch === "\"") {
                const start = index;
                index += 1;
                while (index < value.length) {
                    if (value[index] === "\"") {
                        if (value[index + 1] === "\"") {
                            index += 2;
                            continue;
                        }
                        index += 1;
                        break;
                    }
                    index += 1;
                }
                tokens.push({ type: "string", text: value.slice(start, index), start, end: index });
                continue;
            }

            if (isArgumentSeparatorAt(value, index, localeOptions)) {
                tokens.push({ type: "separator", text: ch, start: index, end: index + 1 });
                index += 1;
                continue;
            }

            if (ch === "(" || ch === ")") {
                tokens.push({ type: "paren", text: ch, start: index, end: index + 1 });
                index += 1;
                continue;
            }

            if ((ch === "<" || ch === ">") && value[index + 1] === "=") {
                tokens.push({ type: "operator", text: value.slice(index, index + 2), start: index, end: index + 2 });
                index += 2;
                continue;
            }

            if (ch === "<" && value[index + 1] === ">") {
                tokens.push({ type: "operator", text: "<>", start: index, end: index + 2 });
                index += 2;
                continue;
            }

            if (/[=+\-*/^&<>:%]/.test(ch)) {
                tokens.push({ type: "operator", text: ch, start: index, end: index + 1 });
                index += 1;
                continue;
            }

            const numberToken = (isDigit(ch) || (ch === localeOptions.decimalSeparator && isDigit(value[index + 1] || "")))
                ? readFormulaNumberToken(value, index, localeOptions)
                : null;
            if (numberToken) {
                tokens.push(numberToken);
                index = numberToken.end;
                continue;
            }

            const referenceToken = (ch === "$" || /[A-Za-z]/.test(ch))
                ? readFormulaReferenceToken(value, index)
                : null;
            if (referenceToken) {
                tokens.push(referenceToken);
                index = referenceToken.end;
                continue;
            }

            if (/[A-Za-z_]/.test(ch)) {
                const start = index;
                index += 1;
                while (index < value.length && /[A-Za-z0-9_.]/.test(value[index])) index += 1;
                const end = index;
                let probe = index;
                while (probe < value.length && /\s/.test(value[probe])) probe += 1;
                tokens.push({
                    type: probe < value.length && value[probe] === "(" ? "function" : "identifier",
                    text: value.slice(start, end),
                    start,
                    end
                });
                continue;
            }

            tokens.push({ type: "unknown", text: ch, start: index, end: index + 1 });
            index += 1;
        }

        return tokens;
    }

    function parseFormulaReferences(text, scopeOrOptions) {
        const refs = [];
        for (const token of tokenizeFormula(text, scopeOrOptions)) {
            if (token.type !== "reference" && token.type !== "range") continue;
            const parsed = parseReferenceToken(token.text);
            if (!parsed) continue;
            refs.push({
                text: token.text,
                start: token.start,
                end: token.end,
                colorIndex: refs.length % 6,
                startRow: parsed.startRow,
                startCol: parsed.startCol,
                endRow: parsed.endRow,
                endCol: parsed.endCol
            });
        }
        return refs;
    }

    function getReferenceAtCaret(refs, caret) {
        const position = Math.max(0, Number(caret) || 0);
        let previous = null;
        for (const ref of refs || []) {
            if (position >= ref.start && position <= ref.end) return ref;
            if (ref.end <= position) previous = ref;
        }
        return previous && previous.end === position ? previous : null;
    }

    function getReferenceSelection(refs, selectionStart, selectionEnd, text) {
        const normalizedText = String(text || "");
        const start = clampPosition(selectionStart, normalizedText);
        const end = clampPosition(selectionEnd, normalizedText);
        const caretToken = getReferenceAtCaret(refs, start);
        let selectionToken = null;
        if (end > start) {
            selectionToken = (refs || []).find(ref => start >= ref.start && start <= ref.end && end >= ref.start && end <= ref.end)
                || (refs || []).find(ref => start < ref.end && end > ref.start)
                || (refs || []).find(ref => start >= ref.start && start <= ref.end)
                || null;
        }
        const activeToken = selectionToken || caretToken;
        const activeTokenIndex = activeToken
            ? (refs || []).findIndex(ref => ref.start === activeToken.start && ref.end === activeToken.end)
            : -1;
        return { selectionStart: start, selectionEnd: end, activeToken, activeTokenIndex };
    }

    function cycleSingleAbsoluteReference(cellRef) {
        const match = /^(\$?)([A-Za-z]{1,3})(\$?)(\d{1,7})$/i.exec(String(cellRef || ""));
        if (!match) return String(cellRef || "");
        const colAbs = match[1] === "$";
        const col = match[2].toUpperCase();
        const rowAbs = match[3] === "$";
        const row = match[4];
        let nextColAbs;
        let nextRowAbs;
        if (!colAbs && !rowAbs) {
            nextColAbs = true;
            nextRowAbs = true;
        } else if (colAbs && rowAbs) {
            nextColAbs = false;
            nextRowAbs = true;
        } else if (!colAbs && rowAbs) {
            nextColAbs = true;
            nextRowAbs = false;
        } else {
            nextColAbs = false;
            nextRowAbs = false;
        }
        return `${nextColAbs ? "$" : ""}${col}${nextRowAbs ? "$" : ""}${row}`;
    }

    function cycleAbsoluteReferenceToken(token) {
        const value = String(token || "");
        const parts = value.split(":");
        if (parts.length === 2) {
            return `${cycleSingleAbsoluteReference(parts[0])}:${cycleSingleAbsoluteReference(parts[1])}`;
        }
        return cycleSingleAbsoluteReference(value);
    }

    function isCellReferenceLike(token) {
        return cellRefPattern.test(String(token || "").replace(/\$/g, ""));
    }

    function findFunctionPrefix(text, caret) {
        const value = String(text || "");
        const position = clampPosition(caret, value);
        let start = position;
        while (start > 0 && /[A-Za-z]/.test(value[start - 1])) {
            start -= 1;
        }
        if (start === position) return null;
        const prefix = value.slice(start, position).toUpperCase();
        if (!prefix) return null;
        if (isCellReferenceLike(prefix)) return null;
        const boundary = start > 0 ? value[start - 1] : "=";
        if (start > 0 && !functionBoundaryPattern.test(boundary)) return null;
        if (value[position] === "(") return null;
        return { prefix, start, end: position };
    }

    function findActiveFunctionContext(text, caret, scopeOrOptions) {
        const value = String(text || "");
        const localeOptions = normalizeFormulaLocaleOptions(scopeOrOptions);
        const limit = clampPosition(caret, value);
        const stack = [];
        let index = 0;
        while (index < limit) {
            const ch = value[index];
            if (ch === "\"") {
                index += 1;
                while (index < limit) {
                    if (value[index] === "\"") {
                        if (index + 1 < limit && value[index + 1] === "\"") {
                            index += 2;
                            continue;
                        }
                        index += 1;
                        break;
                    }
                    index += 1;
                }
                continue;
            }

            if (/[A-Za-z_]/.test(ch)) {
                const start = index;
                index += 1;
                while (index < limit && /[A-Za-z0-9_]/.test(value[index])) {
                    index += 1;
                }
                const identifier = value.slice(start, index).toUpperCase();
                if (!isCellReferenceLike(identifier)) {
                    let probe = index;
                    while (probe < limit && /\s/.test(value[probe])) {
                        probe += 1;
                    }
                    if (probe < limit && value[probe] === "(") {
                        stack.push({ name: identifier, argIndex: 0 });
                        index = probe + 1;
                        continue;
                    }
                }
                continue;
            }

            if (stack.length > 0 && isArgumentSeparatorAt(value, index, localeOptions)) {
                stack[stack.length - 1].argIndex += 1;
                index += 1;
                continue;
            }

            if (ch === ")" && stack.length > 0) {
                stack.pop();
                index += 1;
                continue;
            }

            index += 1;
        }

        return stack.length > 0 ? stack[stack.length - 1] : null;
    }

    const functionCatalog = [
        { name: "ABS", signature: "ABS(number)", summary: "Returns the absolute value.", arguments: ["number"] },
        { name: "ADDRESS", signature: "ADDRESS(row, column, [abs], [a1], [sheet])", summary: "Builds a cell address from row and column numbers.", arguments: ["row", "column", "abs", "a1", "sheet"] },
        { name: "AND", signature: "AND(logical1, [logical2], ...)", summary: "Returns TRUE when all conditions are TRUE.", arguments: ["logical1", "logical2"] },
        { name: "AREAS", signature: "AREAS(reference)", summary: "Returns the number of referenced areas.", arguments: ["reference"] },
        { name: "AVERAGE", signature: "AVERAGE(number1, [number2], ...)", summary: "Returns the arithmetic mean.", arguments: ["number1", "number2"] },
        { name: "CHOOSE", signature: "CHOOSE(index, value1, [value2], ...)", summary: "Returns the value at the chosen index.", arguments: ["index", "value1", "value2"] },
        { name: "COLUMN", signature: "COLUMN([reference])", summary: "Returns the column number for a reference.", arguments: ["reference"] },
        { name: "COLUMNS", signature: "COLUMNS(array)", summary: "Returns the number of columns in an array or range.", arguments: ["array"] },
        { name: "CONCATENATE", signature: "CONCATENATE(text1, [text2], ...)", summary: "Joins multiple text values together.", arguments: ["text1", "text2"] },
        { name: "COUNT", signature: "COUNT(value1, [value2], ...)", summary: "Counts numeric values.", arguments: ["value1", "value2"] },
        { name: "DATE", signature: "DATE(year, month, day)", summary: "Builds a serial date from year, month, and day.", arguments: ["year", "month", "day"] },
        { name: "DATEDIF", signature: "DATEDIF(start_date, end_date, unit)", summary: "Returns the difference between two dates.", arguments: ["start_date", "end_date", "unit"] },
        { name: "DATEVALUE", signature: "DATEVALUE(text)", summary: "Parses text into a date serial value.", arguments: ["text"] },
        { name: "DAYS", signature: "DAYS(end_date, start_date)", summary: "Returns the number of days between two dates.", arguments: ["end_date", "start_date"] },
        { name: "EDATE", signature: "EDATE(start_date, months)", summary: "Shifts a date by a number of months.", arguments: ["start_date", "months"] },
        { name: "EOMONTH", signature: "EOMONTH(start_date, months)", summary: "Returns the last day of a month offset.", arguments: ["start_date", "months"] },
        { name: "FALSE", signature: "FALSE()", summary: "Returns the logical value FALSE.", arguments: [] },
        { name: "FIND", signature: "FIND(find_text, within_text, [start])", summary: "Finds text using a case-sensitive search.", arguments: ["find_text", "within_text", "start"] },
        { name: "HLOOKUP", signature: "HLOOKUP(value, table, row_index, [exact])", summary: "Looks up a value across the first row of a table.", arguments: ["value", "table", "row_index", "exact"] },
        { name: "HOUR", signature: "HOUR(serial)", summary: "Returns the hour component of a time.", arguments: ["serial"] },
        { name: "IF", signature: "IF(test, value_if_true, value_if_false)", summary: "Returns one value when a condition is TRUE and another when FALSE.", arguments: ["test", "value_if_true", "value_if_false"] },
        { name: "IFERROR", signature: "IFERROR(value, fallback)", summary: "Returns a fallback when the value is an error.", arguments: ["value", "fallback"] },
        { name: "INDEX", signature: "INDEX(array, row, [column])", summary: "Returns a value from a row and column within an array.", arguments: ["array", "row", "column"] },
        { name: "INDIRECT", signature: "INDIRECT(reference_text)", summary: "Resolves a text address into a reference.", arguments: ["reference_text"] },
        { name: "ISEVEN", signature: "ISEVEN(number)", summary: "Returns TRUE when the number is even.", arguments: ["number"] },
        { name: "ISBLANK", signature: "ISBLANK(value)", summary: "Returns TRUE when the value is blank.", arguments: ["value"] },
        { name: "ISERROR", signature: "ISERROR(value)", summary: "Returns TRUE when the value is an error.", arguments: ["value"] },
        { name: "ISLOGICAL", signature: "ISLOGICAL(value)", summary: "Returns TRUE when the value is TRUE or FALSE.", arguments: ["value"] },
        { name: "ISNUMBER", signature: "ISNUMBER(value)", summary: "Returns TRUE when the value is numeric.", arguments: ["value"] },
        { name: "ISODD", signature: "ISODD(number)", summary: "Returns TRUE when the number is odd.", arguments: ["number"] },
        { name: "ISTEXT", signature: "ISTEXT(value)", summary: "Returns TRUE when the value is text.", arguments: ["value"] },
        { name: "LEFT", signature: "LEFT(text, [count])", summary: "Returns the leftmost characters from text.", arguments: ["text", "count"] },
        { name: "LEN", signature: "LEN(text)", summary: "Returns the text length.", arguments: ["text"] },
        { name: "LOWER", signature: "LOWER(text)", summary: "Converts text to lowercase.", arguments: ["text"] },
        { name: "MATCH", signature: "MATCH(value, lookup_array, [match_type])", summary: "Returns the relative position of a lookup value.", arguments: ["value", "lookup_array", "match_type"] },
        { name: "MAX", signature: "MAX(number1, [number2], ...)", summary: "Returns the maximum numeric value.", arguments: ["number1", "number2"] },
        { name: "MID", signature: "MID(text, start, count)", summary: "Returns characters from the middle of text.", arguments: ["text", "start", "count"] },
        { name: "MIN", signature: "MIN(number1, [number2], ...)", summary: "Returns the minimum numeric value.", arguments: ["number1", "number2"] },
        { name: "MINUTE", signature: "MINUTE(serial)", summary: "Returns the minute component of a time.", arguments: ["serial"] },
        { name: "MOD", signature: "MOD(number, divisor)", summary: "Returns the remainder after division.", arguments: ["number", "divisor"] },
        { name: "MONTH", signature: "MONTH(serial)", summary: "Returns the month number from a date.", arguments: ["serial"] },
        { name: "NOT", signature: "NOT(logical)", summary: "Reverses a logical value.", arguments: ["logical"] },
        { name: "NOW", signature: "NOW()", summary: "Returns the current date and time.", arguments: [] },
        { name: "OFFSET", signature: "OFFSET(reference, rows, cols, [height], [width])", summary: "Returns a reference offset from another reference.", arguments: ["reference", "rows", "cols", "height", "width"] },
        { name: "OR", signature: "OR(logical1, [logical2], ...)", summary: "Returns TRUE when any condition is TRUE.", arguments: ["logical1", "logical2"] },
        { name: "PI", signature: "PI()", summary: "Returns the value of pi.", arguments: [] },
        { name: "POWER", signature: "POWER(number, exponent)", summary: "Raises a number to a power.", arguments: ["number", "exponent"] },
        { name: "PROPER", signature: "PROPER(text)", summary: "Capitalizes each word in text.", arguments: ["text"] },
        { name: "RAND", signature: "RAND()", summary: "Returns a random number between 0 and 1.", arguments: [] },
        { name: "RANDBETWEEN", signature: "RANDBETWEEN(bottom, top)", summary: "Returns a random integer within a range.", arguments: ["bottom", "top"] },
        { name: "REPT", signature: "REPT(text, number_times)", summary: "Repeats text a number of times.", arguments: ["text", "number_times"] },
        { name: "RIGHT", signature: "RIGHT(text, [count])", summary: "Returns the rightmost characters from text.", arguments: ["text", "count"] },
        { name: "ROUND", signature: "ROUND(number, digits)", summary: "Rounds a number to a number of digits.", arguments: ["number", "digits"] },
        { name: "ROUNDDOWN", signature: "ROUNDDOWN(number, digits)", summary: "Rounds a number down toward zero.", arguments: ["number", "digits"] },
        { name: "ROUNDUP", signature: "ROUNDUP(number, digits)", summary: "Rounds a number up away from zero.", arguments: ["number", "digits"] },
        { name: "ROW", signature: "ROW([reference])", summary: "Returns the row number for a reference.", arguments: ["reference"] },
        { name: "ROWS", signature: "ROWS(array)", summary: "Returns the number of rows in an array or range.", arguments: ["array"] },
        { name: "SEARCH", signature: "SEARCH(find_text, within_text, [start])", summary: "Finds text using a case-insensitive search.", arguments: ["find_text", "within_text", "start"] },
        { name: "SECOND", signature: "SECOND(serial)", summary: "Returns the second component of a time.", arguments: ["serial"] },
        { name: "SQRT", signature: "SQRT(number)", summary: "Returns the square root.", arguments: ["number"] },
        { name: "SUBSTITUTE", signature: "SUBSTITUTE(text, old_text, new_text, [instance])", summary: "Replaces existing text with new text.", arguments: ["text", "old_text", "new_text", "instance"] },
        { name: "SUM", signature: "SUM(number1, [number2], ...)", summary: "Adds numeric values together.", arguments: ["number1", "number2"] },
        { name: "TEXT", signature: "TEXT(value, format)", summary: "Formats a value using a number format string.", arguments: ["value", "format"] },
        { name: "TIME", signature: "TIME(hour, minute, second)", summary: "Builds a serial time value.", arguments: ["hour", "minute", "second"] },
        { name: "TIMEVALUE", signature: "TIMEVALUE(text)", summary: "Parses text into a time serial value.", arguments: ["text"] },
        { name: "TODAY", signature: "TODAY()", summary: "Returns the current date.", arguments: [] },
        { name: "TRIM", signature: "TRIM(text)", summary: "Removes extra spaces from text.", arguments: ["text"] },
        { name: "TRUE", signature: "TRUE()", summary: "Returns the logical value TRUE.", arguments: [] },
        { name: "UPPER", signature: "UPPER(text)", summary: "Converts text to uppercase.", arguments: ["text"] },
        { name: "VALUE", signature: "VALUE(text)", summary: "Converts text into a numeric value.", arguments: ["text"] },
        { name: "VLOOKUP", signature: "VLOOKUP(value, table, column_index, [exact])", summary: "Looks up a value down the first column of a table.", arguments: ["value", "table", "column_index", "exact"] },
        { name: "WEEKDAY", signature: "WEEKDAY(serial, [return_type])", summary: "Returns the day of week number.", arguments: ["serial", "return_type"] },
        { name: "WEEKNUM", signature: "WEEKNUM(serial, [return_type])", summary: "Returns the week number for a date.", arguments: ["serial", "return_type"] },
        { name: "YEAR", signature: "YEAR(serial)", summary: "Returns the year from a date serial.", arguments: ["serial"] }
    ];

    function buildFunctionSuggestions(prefix) {
        if (!prefix) return [];
        const normalized = String(prefix || "").trim().toUpperCase();
        if (!normalized) return [];
        return functionCatalog
            .filter(fn => fn.name.startsWith(normalized))
            .sort((a, b) => a.name.length - b.name.length || a.name.localeCompare(b.name))
            .slice(0, 8)
            .map(fn => ({ ...fn, arguments: [...(fn.arguments || [])] }));
    }

    function buildFunctionHint(name, activeArgumentIndex) {
        if (!name) return null;
        const fn = functionCatalog.find(candidate => candidate.name === String(name).toUpperCase());
        if (!fn) return null;
        return {
            function: { ...fn, arguments: [...(fn.arguments || [])] },
            activeArgumentIndex: Math.max(0, Number(activeArgumentIndex) || 0)
        };
    }

    function analyzeFormulaSession(text, selectionStart, selectionEnd, scopeOrOptions) {
        const value = String(text || "");
        const refs = value.startsWith("=") ? parseFormulaReferences(value, scopeOrOptions) : [];
        const selection = getReferenceSelection(refs, selectionStart, selectionEnd, value);
        const prefix = value.startsWith("=") && !selection.activeToken
            ? findFunctionPrefix(value, selection.selectionStart)
            : null;
        const activeFunction = value.startsWith("=")
            ? findActiveFunctionContext(value, selection.selectionStart, scopeOrOptions)
            : null;
        return {
            text: value,
            selectionStart: selection.selectionStart,
            selectionEnd: selection.selectionEnd,
            isFormula: value.startsWith("="),
            isReferencePickingMode: value.startsWith("="),
            activeReferenceToken: selection.activeToken || null,
            activeReferenceTokenIndex: selection.activeTokenIndex,
            referenceTokens: refs,
            functionPrefix: prefix ? prefix.prefix : null,
            functionPrefixStart: prefix ? prefix.start : -1,
            functionPrefixEnd: prefix ? prefix.end : -1,
            suggestions: prefix ? buildFunctionSuggestions(prefix.prefix) : [],
            activeFunctionHint: activeFunction ? buildFunctionHint(activeFunction.name, activeFunction.argIndex) : null
        };
    }

    function getSpreadsheetHost(scope) {
        return scope?.closest?.(".tm-spreadsheet") || null;
    }

    function readFormulaBarCellRef(host) {
        const ref = host?.querySelector?.(".tm-spreadsheet-formula-bar__ref");
        if (!ref) return "";
        if (ref instanceof HTMLInputElement || ref instanceof HTMLTextAreaElement) {
            return String(ref.value || ref.getAttribute("value") || ref.textContent || "").trim();
        }
        return String(ref.textContent || ref.getAttribute("value") || "").trim();
    }

    function readLiveFormulaBarSession(host) {
        const input = host?.querySelector?.(".tm-spreadsheet-formula-bar__input");
        if (!(input instanceof HTMLInputElement) || input.offsetParent === null) {
            return null;
        }

        const cellRef = readFormulaBarCellRef(host);
        const text = String(input.value || "");
        return {
            owner: "formulaBar",
            cellRef,
            text,
            selectionStart: clampPosition(input.selectionStart, text),
            selectionEnd: clampPosition(input.selectionEnd, text),
            isFormula: text.startsWith("="),
            updatedAt: Date.now()
        };
    }

    function readLiveInlineSession(host) {
        const grid = host?.querySelector?.(".tm-spreadsheet-canvas-grid");
        const input = grid?.querySelector?.(".tm-spreadsheet-canvas-grid__editor");
        const state = grid?.__tmSpreadsheetCanvas;
        const editor = state?.editor;
        if (!(input instanceof HTMLInputElement) || input.offsetParent === null || !editor) {
            return null;
        }

        const text = String(input.value || "");
        const cellRef = String(state?.sheetState?.activeCell?.ref || "");
        return {
            owner: "inline",
            cellRef,
            text,
            selectionStart: clampPosition(input.selectionStart, text),
            selectionEnd: clampPosition(input.selectionEnd, text),
            isFormula: text.startsWith("="),
            updatedAt: Date.now()
        };
    }

    function setHostFormulaSession(scope, session) {
        const host = getSpreadsheetHost(scope);
        if (!host) return null;
        if (!session) {
            delete host.__tmSpreadsheetFormulaSession;
            return null;
        }
        const next = {
            owner: String(session.owner || ""),
            cellRef: String(session.cellRef || ""),
            text: String(session.text || ""),
            selectionStart: clampPosition(session.selectionStart, session.text || ""),
            selectionEnd: clampPosition(session.selectionEnd, session.text || ""),
            isFormula: !!session.isFormula,
            updatedAt: Date.now()
        };
        host.__tmSpreadsheetFormulaSession = next;
        return next;
    }

    function getHostFormulaSession(scope) {
        const host = getSpreadsheetHost(scope);
        if (!host) {
            return null;
        }

        return host.__tmSpreadsheetFormulaSession
            || readLiveFormulaBarSession(host)
            || readLiveInlineSession(host)
            || null;
    }

    function isHostFormulaPointMode(scope) {
        const host = getSpreadsheetHost(scope);
        if (!host) {
            return false;
        }

        const session = getHostFormulaSession(host);
        if (session?.isFormula) {
            return true;
        }

        if (host.dataset?.formulaPointMode === "true") {
            return true;
        }

        return false;
    }

    function clearHostFormulaSession(scope, owner) {
        const host = getSpreadsheetHost(scope);
        if (!host?.__tmSpreadsheetFormulaSession) return;
        const current = host.__tmSpreadsheetFormulaSession;
        if (owner && current.owner && current.owner !== owner) return;
        delete host.__tmSpreadsheetFormulaSession;
    }

    window.tmSpreadsheetFormulaRuntime = {
        functionCatalog,
        parseFormulaReferences,
        tokenizeFormula,
        getReferenceSelection,
        analyzeSession: analyzeFormulaSession,
        replaceReferenceAtSelection(text, selectionStart, selectionEnd, refText) {
            const value = String(text || "=");
            const refs = parseFormulaReferences(value);
            const selection = getReferenceSelection(refs, selectionStart, selectionEnd, value);
            let start = selection.activeToken ? selection.activeToken.start : selection.selectionStart;
            let end = selection.activeToken ? selection.activeToken.end : selection.selectionEnd;
            if (start < 1) {
                start = value.length <= 1 ? 1 : Math.max(1, selection.selectionStart);
            }
            if (end < start) {
                end = start;
            }
            const replacement = String(refText || "");
            const nextValue = value.slice(0, start) + replacement + value.slice(end);
            const nextCaret = start + replacement.length;
            return {
                value: nextValue,
                selectionStart: nextCaret,
                selectionEnd: nextCaret
            };
        },
        cycleReferenceAtSelection(text, selectionStart, selectionEnd) {
            const value = String(text || "");
            if (!value.startsWith("=")) {
                return {
                    value,
                    selectionStart: clampPosition(selectionStart, value),
                    selectionEnd: clampPosition(selectionEnd, value),
                    changed: false
                };
            }

            const refs = parseFormulaReferences(value);
            const selection = getReferenceSelection(refs, selectionStart, selectionEnd, value);
            const token = selection.activeToken;
            if (!token) {
                return {
                    value,
                    selectionStart: selection.selectionStart,
                    selectionEnd: selection.selectionEnd,
                    changed: false
                };
            }

            const replacement = cycleAbsoluteReferenceToken(token.text);
            const nextValue = value.slice(0, token.start) + replacement + value.slice(token.end);
            const offsetWithinToken = Math.max(0, Math.min(token.text.length, selection.selectionStart - token.start));
            const nextCaret = token.start + Math.min(replacement.length, offsetWithinToken);
            return {
                value: nextValue,
                selectionStart: nextCaret,
                selectionEnd: nextCaret,
                changed: true
            };
        },
        acceptFunctionSuggestion(text, selectionStart, selectionEnd, functionName) {
            const value = String(text || "=");
            const replacement = `${String(functionName || "").toUpperCase()}(`;
            const prefix = findFunctionPrefix(value, selectionStart);
            const start = prefix ? prefix.start : clampPosition(selectionStart, value);
            const end = prefix ? prefix.end : clampPosition(selectionEnd, value);
            const nextValue = value.slice(0, start) + replacement + value.slice(end);
            const nextCaret = start + replacement.length;
            return {
                value: nextValue,
                selectionStart: nextCaret,
                selectionEnd: nextCaret
            };
        },
        setHostFormulaPointMode,
        setHostFormulaSession,
        getHostFormulaSession,
        clearHostFormulaSession,
        isHostFormulaPointMode
    };

    window.tmSpreadsheetFormulaBar.getSelection = function (input) {
        if (!input) {
            return { selectionStart: 0, selectionEnd: 0 };
        }
        return {
            selectionStart: Number(input.selectionStart || 0),
            selectionEnd: Number(input.selectionEnd || 0)
        };
    };

    function buildLiveFormulaBarSession(scope, input) {
        const host = getSpreadsheetHost(scope || input);
        if (!host || !(input instanceof HTMLInputElement)) {
            return null;
        }

        const cellRef = readFormulaBarCellRef(host);
        const text = String(input.value || "");
        return {
            owner: "formulaBar",
            cellRef,
            text,
            selectionStart: clampPosition(input.selectionStart, text),
            selectionEnd: clampPosition(input.selectionEnd, text),
            isFormula: text.startsWith("=")
        };
    }

    function markRecentFormulaSession(host, isFormula) {
        if (!host || !isFormula) {
            return;
        }

        host.__tmSpreadsheetFormulaRecentUntil = performance.now() + 1500;
    }

    function ensureHostFormulaGridGuard(host) {
        if (!host || host.__tmSpreadsheetFormulaGridGuardInstalled) {
            return;
        }

        const preserveInputFocus = () => {
            const input = host.querySelector?.(".tm-spreadsheet-formula-bar__input");
            if (!(input instanceof HTMLInputElement)) {
                return;
            }

            const value = String(input.value || "");
            const start = clampPosition(input.selectionStart, value);
            const end = clampPosition(input.selectionEnd, value);
            setTimeout(() => {
                try {
                    input.focus({ preventScroll: true });
                    input.setSelectionRange(start, end);
                } catch {
                    // Best effort only.
                }
            }, 0);
        };

        const hasFormulaSession = () => {
            if (performance.now() < Number(host.__tmSpreadsheetFormulaRecentUntil || 0)) {
                return true;
            }

            if (performance.now() < Number(host.__tmSpreadsheetFormulaNonPrimaryUntil || 0)) {
                return true;
            }

            if (host.dataset?.formulaGuardActive === "true" || host.dataset?.formulaPointMode === "true") {
                return true;
            }

            const live = readLiveFormulaBarSession(host);
            if (live?.isFormula) {
                markRecentFormulaSession(host, true);
                setHostFormulaSession(host, live);
                return true;
            }

            const session = host.__tmSpreadsheetFormulaSession;
            return !!(session?.isFormula || String(session?.text || "").startsWith("="));
        };

        const guard = ev => {
            const target = ev.target;
            if (!(target instanceof Element)) {
                return;
            }

            if (target.closest(".tm-spreadsheet") !== host || !target.closest(".tm-spreadsheet-canvas-grid")) {
                return;
            }

            const button = Number(ev.button || 0);
            if (button === 0 && ev.type !== "contextmenu") {
                return;
            }

            if (!hasFormulaSession()) {
                return;
            }

            host.__tmSpreadsheetFormulaNonPrimaryUntil = performance.now() + 1500;
            ev.preventDefault();
            ev.stopPropagation();
            ev.stopImmediatePropagation?.();
            preserveInputFocus();
        };

        document.addEventListener("pointerdown", guard, true);
        document.addEventListener("mousedown", guard, true);
        document.addEventListener("contextmenu", guard, true);
        document.addEventListener("auxclick", guard, true);
        host.__tmSpreadsheetFormulaGridGuardInstalled = true;
    }

    function installGlobalFormulaGridGuard() {
        if (window.__tmSpreadsheetGlobalFormulaGridGuardInstalled) {
            return;
        }

        const shouldGuard = ev => {
            const target = ev.target;
            if (!(target instanceof Element)) {
                return null;
            }

            if (!target.closest(".tm-spreadsheet-canvas-grid")) {
                return null;
            }

            const host = target.closest(".tm-spreadsheet");
            if (!host) {
                return null;
            }

            const button = Number(ev.button || 0);
            if (button === 0 && ev.type !== "contextmenu") {
                return null;
            }

            if (performance.now() < Number(host.__tmSpreadsheetFormulaRecentUntil || 0)
                || performance.now() < Number(host.__tmSpreadsheetFormulaNonPrimaryUntil || 0)
                || host.dataset?.formulaGuardActive === "true"
                || host.dataset?.formulaPointMode === "true") {
                return host;
            }

            const live = readLiveFormulaBarSession(host);
            if (live?.isFormula) {
                markRecentFormulaSession(host, true);
                setHostFormulaSession(host, live);
                return host;
            }

            const session = host.__tmSpreadsheetFormulaSession;
            return session?.isFormula || String(session?.text || "").startsWith("=")
                ? host
                : null;
        };

        const guard = ev => {
            const host = shouldGuard(ev);
            if (!host) {
                return;
            }

            host.__tmSpreadsheetFormulaNonPrimaryUntil = performance.now() + 1500;
            ev.preventDefault();
            ev.stopPropagation();
            ev.stopImmediatePropagation?.();

            const input = host.querySelector?.(".tm-spreadsheet-formula-bar__input");
            if (input instanceof HTMLInputElement) {
                const value = String(input.value || "");
                const start = clampPosition(input.selectionStart, value);
                const end = clampPosition(input.selectionEnd, value);
                setTimeout(() => {
                    try {
                        input.focus({ preventScroll: true });
                        input.setSelectionRange(start, end);
                    } catch {
                        // Best effort only.
                    }
                }, 0);
            }
        };

        document.addEventListener("pointerdown", guard, true);
        document.addEventListener("mousedown", guard, true);
        document.addEventListener("contextmenu", guard, true);
        document.addEventListener("auxclick", guard, true);
        window.__tmSpreadsheetGlobalFormulaGridGuardInstalled = true;
    }

    function setHostFormulaPointMode(scope, active, value) {
        const host = scope?.closest?.(".tm-spreadsheet");
        if (!host) return;
        const formulaActive = !!active && String(value || "").startsWith("=");
        if (formulaActive) {
            host.dataset.formulaPointMode = "true";
            markRecentFormulaSession(host, true);
            ensureHostFormulaGridGuard(host);
        } else {
            delete host.dataset.formulaPointMode;
            clearHostFormulaSession(scope, "formulaBar");
        }
        const grid = host.querySelector?.(".tm-spreadsheet-canvas-grid");
        if (grid && window.tmSpreadsheetCanvas?.setExternalFormulaPointMode) {
            window.tmSpreadsheetCanvas.setExternalFormulaPointMode(grid, formulaActive);
        }
    }

    installGlobalFormulaGridGuard();

    function syncHostFormulaRuntimeState(scope, input) {
        const session = buildLiveFormulaBarSession(scope, input);
        if (session) {
            markRecentFormulaSession(getSpreadsheetHost(scope || input), session.isFormula);
            setHostFormulaSession(scope, session);
            setHostFormulaPointMode(scope, true, session.text);
            return session;
        }

        setHostFormulaPointMode(scope || input, true, input?.value || "");
        return null;
    }

    window.tmSpreadsheetFormulaBar.setHostFormulaPointMode = function (scope, active, value) {
        setHostFormulaPointMode(scope, !!active, value);
    };

    window.tmSpreadsheetFormulaBar.syncHostFormulaPointModeFromInput = function (input) {
        if (!input) return;
        syncHostFormulaRuntimeState(input, input);
    };

    window.tmSpreadsheetFormulaBar.bindHostFormulaPointMode = function (scope, input) {
        if (!scope || !input) return;
        if (typeof input.__tmSpreadsheetFormulaHostSyncCleanup === "function") {
            input.__tmSpreadsheetFormulaHostSyncCleanup();
        }

        const host = getSpreadsheetHost(scope) || scope;
        const sync = () => syncHostFormulaRuntimeState(scope, input);
        const preserveInputFocus = () => {
            try {
                const value = String(input.value || "");
                const start = clampPosition(input.selectionStart, value);
                const end = clampPosition(input.selectionEnd, value);
                input.focus({ preventScroll: true });
                input.setSelectionRange(start, end);
            } catch {
                // Best effort only.
            }
        };
        const shouldProtectGridGesture = ev => {
            const target = ev.target;
            if (!(target instanceof Element)) {
                return false;
            }

            if (target.closest(".tm-spreadsheet") !== host) {
                return false;
            }

            if (!target.closest(".tm-spreadsheet-canvas-grid")) {
                return false;
            }

            if (performance.now() < Number(host.__tmSpreadsheetFormulaRecentUntil || 0)) {
                return true;
            }

            if (host.dataset?.formulaGuardActive === "true") {
                return true;
            }

            if (host.dataset?.formulaPointMode === "true") {
                return true;
            }

            const currentInput = host.querySelector?.(".tm-spreadsheet-formula-bar__input");
            const currentSession = host.__tmSpreadsheetFormulaSession;
            const value = String(
                currentSession?.text
                || (currentInput instanceof HTMLInputElement ? currentInput.value : "")
                || input.value
                || "");
            if (!value.startsWith("=")) {
                return false;
            }

            return true;
        };
        const nonPrimaryGridGuard = ev => {
            if (!shouldProtectGridGesture(ev)) {
                return;
            }

            const button = Number(ev.button || 0);
            if (button === 0 && ev.type !== "contextmenu") {
                return;
            }

            ev.preventDefault();
            ev.stopPropagation();
            preserveInputFocus();
            sync();
        };
        const keyGuard = ev => {
            const value = String(input.value || "");
            if (!value.startsWith("=")) {
                return;
            }

            const suggestions = scope.querySelector?.("[data-testid='tm-spreadsheet-formula-bar-suggestions']");
            const suggestionsVisible = !!(suggestions && suggestions.offsetParent !== null);

            if (ev.key === "ArrowUp" || ev.key === "ArrowDown") {
                ev.preventDefault();
                if (!suggestionsVisible) {
                    ev.stopPropagation();
                }
                return;
            }

            if (ev.key === "PageUp" || ev.key === "PageDown") {
                ev.preventDefault();
            }
        };
        input.addEventListener("input", sync);
        input.addEventListener("change", sync);
        input.addEventListener("keydown", keyGuard, true);
        document.addEventListener("pointerdown", nonPrimaryGridGuard, true);
        document.addEventListener("mousedown", nonPrimaryGridGuard, true);
        document.addEventListener("contextmenu", nonPrimaryGridGuard, true);
        document.addEventListener("auxclick", nonPrimaryGridGuard, true);
        input.__tmSpreadsheetFormulaHostSyncCleanup = () => {
            input.removeEventListener("input", sync);
            input.removeEventListener("change", sync);
            input.removeEventListener("keydown", keyGuard, true);
            document.removeEventListener("pointerdown", nonPrimaryGridGuard, true);
            document.removeEventListener("mousedown", nonPrimaryGridGuard, true);
            document.removeEventListener("contextmenu", nonPrimaryGridGuard, true);
            document.removeEventListener("auxclick", nonPrimaryGridGuard, true);
            delete input.__tmSpreadsheetFormulaHostSyncCleanup;
        };
        sync();
    };

    window.tmSpreadsheetFormulaBar.shouldRetainFocusAfterBlur = function (scope) {
        const host = getSpreadsheetHost(scope);
        if (!host) return true;
        const session = host.__tmSpreadsheetFormulaSession;
        const active = document.activeElement;
        if (session?.owner === "inline") return false;
        if (active instanceof Element && active.closest(".tm-spreadsheet-canvas-grid")) return false;
        if (active instanceof Element && active.closest(".tm-spreadsheet-canvas-grid__editor")) return false;
        return true;
    };

    window.tmSpreadsheetFormulaBar.setValueAndSelection = function (input, value, selectionStart, selectionEnd) {
        if (!input) return;
        input.value = String(value || "");
        const start = clampPosition(selectionStart, input.value);
        const end = clampPosition(selectionEnd, input.value);
        input.setSelectionRange(start, end);
        input.focus({ preventScroll: true });
    };

    window.tmSpreadsheetFormulaBar.syncNameBoxText = function (input, value) {
        if (!input) return;
        const next = String(value || input.value || "");
        input.textContent = next;
        input.setAttribute("data-active-ref", next);
    };

    window.tmSpreadsheetFormulaBar.analyzeSession = function (scopeOrText, textOrSelectionStart, selectionStartOrSelectionEnd, selectionEnd) {
        if (scopeOrText instanceof Element) {
            return window.tmSpreadsheetFormulaRuntime.analyzeSession(
                textOrSelectionStart,
                selectionStartOrSelectionEnd,
                selectionEnd,
                scopeOrText);
        }

        return window.tmSpreadsheetFormulaRuntime.analyzeSession(
            scopeOrText,
            textOrSelectionStart,
            selectionStartOrSelectionEnd);
    };

    window.tmSpreadsheetFormulaBar.replaceReferenceAtSelection = function (text, selectionStart, selectionEnd, refText) {
        return window.tmSpreadsheetFormulaRuntime.replaceReferenceAtSelection(text, selectionStart, selectionEnd, refText);
    };

    window.tmSpreadsheetFormulaBar.cycleReferenceAtSelection = function (text, selectionStart, selectionEnd) {
        return window.tmSpreadsheetFormulaRuntime.cycleReferenceAtSelection(text, selectionStart, selectionEnd);
    };

    window.tmSpreadsheetFormulaBar.acceptFunctionSuggestion = function (text, selectionStart, selectionEnd, functionName) {
        return window.tmSpreadsheetFormulaRuntime.acceptFunctionSuggestion(text, selectionStart, selectionEnd, functionName);
    };

    window.tmSpreadsheetFormulaBar.setHostFormulaSession = function (scope, session) {
        return window.tmSpreadsheetFormulaRuntime.setHostFormulaSession(scope, session);
    };

    window.tmSpreadsheetFormulaBar.getHostFormulaSession = function (scope) {
        return window.tmSpreadsheetFormulaRuntime.getHostFormulaSession(scope);
    };

    window.tmSpreadsheetFormulaBar.clearHostFormulaSession = function (scope, owner) {
        window.tmSpreadsheetFormulaRuntime.clearHostFormulaSession(scope, owner);
    };
})();

// ── Sheet tab strip (redesign phase 5) ────────────────────────────────────────
// The ribbon's sheet tabs report whether they overflow (the arrows are a measured affordance) and
// scroll by a page. Everything stays a plain DOM read/write: no state, no listeners left behind.
window.tmSpreadsheetSheetTabs = window.tmSpreadsheetSheetTabs || {};

window.tmSpreadsheetSheetTabs.hasOverflow = function (scroller) {
    if (!scroller) return false;
    return scroller.scrollWidth > scroller.clientWidth + 1;
};

window.tmSpreadsheetSheetTabs.scrollTabs = function (scroller, direction) {
    if (!scroller) return;
    const step = Math.max(scroller.clientWidth * 0.6, 120);
    const left = direction === "left" ? -step : step;
    if (typeof scroller.scrollBy === "function") {
        scroller.scrollBy({ left: left, behavior: "smooth" });
    } else {
        scroller.scrollLeft += left;
    }
};

// ── Ribbon tabs (redesign phase 5 review) ─────────────────────────────────────
// Roving tabindex needs focus to follow the selected tab; Blazor cannot focus a sibling element
// without a per-tab ElementReference, so the toolbar asks for the id it just activated.
window.tmSpreadsheetToolbar = window.tmSpreadsheetToolbar || {};

window.tmSpreadsheetToolbar.focusTab = function (id) {
    if (!id) return;
    const tab = document.getElementById(String(id));
    if (tab && typeof tab.focus === "function") {
        tab.focus({ preventScroll: true });
    }
};

// ── Formula bar name box (redesign phase 5 review) ────────────────────────────
// True when the focus is still inside the name-box wrapper after a focusout (moving between the
// input, the chevron and a list entry must not close the list; leaving it must).
window.tmSpreadsheetFormulaBar.isFocusWithin = function (element) {
    if (!element) return false;
    return element.contains(document.activeElement);
};
