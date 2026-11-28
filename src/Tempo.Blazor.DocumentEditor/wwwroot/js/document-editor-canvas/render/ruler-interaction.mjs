const POINTS_TO_CSS_PIXELS = 96 / 72;
const CSS_PIXELS_TO_POINTS = 72 / 96;
const TAB_ALIGNMENTS = ['left', 'center', 'right', 'decimal', 'bar'];
const TAB_LEADERS = ['none', 'dots', 'dash', 'underline'];

export function createRulerInteraction(options = {}) {
    const root = options.root;
    const executeCommand = typeof options.executeCommand === 'function' ? options.executeCommand : null;
    const getState = typeof options.getState === 'function' ? options.getState : () => ({});
    if (!root || !executeCommand) {
        return {
            destroy() {},
        };
    }

    let tabAlignmentIndex = 0;
    let tabLeaderIndex = 0;
    let drag = null;
    let lastTabClick = null;

    const onClick = event => {
        const picker = event.target?.closest?.('[data-testid="document-canvas-ruler-tab-picker"]');
        if (picker) {
            tabAlignmentIndex = (tabAlignmentIndex + 1) % TAB_ALIGNMENTS.length;
            updatePicker(root, tabAlignmentIndex, tabLeaderIndex);
            return;
        }

        const leaderPicker = event.target?.closest?.('[data-testid="document-canvas-ruler-leader-picker"]');
        if (leaderPicker) {
            tabLeaderIndex = (tabLeaderIndex + 1) % TAB_LEADERS.length;
            updatePicker(root, tabAlignmentIndex, tabLeaderIndex);
            return;
        }

        const tabStopClick = event.target?.closest?.('[data-ruler-tab-stop]');
        if (tabStopClick) {
            const position = Number(tabStopClick.getAttribute?.('data-tab-position') || 0) || 0;
            const now = event.timeStamp || performance.now?.() || Date.now();
            if (lastTabClick
                && now - lastTabClick.time < 650
                && Math.abs(position - lastTabClick.position) < 2) {
                openTabsDialog(root.ownerDocument, root, executeCommand, {
                    position,
                    alignment: tabStopClick.getAttribute?.('data-tab-alignment') || TAB_ALIGNMENTS[tabAlignmentIndex],
                    leader: tabStopClick.getAttribute?.('data-tab-leader') || TAB_LEADERS[tabLeaderIndex],
                });
                lastTabClick = null;
            } else {
                lastTabClick = { position, time: now };
            }

            return;
        }

        const track = event.target?.closest?.('.tm-document-canvas-ruler__track');
        if (!track || event.target?.closest?.('[data-ruler-marker]') || event.target?.closest?.('[data-ruler-tab-stop]')) {
            return;
        }

        const state = getState();
        const position = positionFromClientX(event.clientX, track, state);
        executeCommand('setTabStop', {
            position,
            alignment: TAB_ALIGNMENTS[tabAlignmentIndex],
            leader: TAB_LEADERS[tabLeaderIndex],
        });
    };

    const onDoubleClick = event => {
        const target = event.target?.closest?.('[data-ruler-tab-stop], .tm-document-canvas-ruler__track');
        root.setAttribute('data-canvas-ruler-last-dblclick-target', target ? (target.getAttribute?.('data-testid') || target.className || 'target') : '');
        if (!target) {
            return;
        }

        const state = getState();
        const position = Number(target.getAttribute?.('data-tab-position') || target.getAttribute?.('data-ruler-document-position') || NaN);
        openTabsDialog(root.ownerDocument, root, executeCommand, {
            position: Number.isFinite(position) ? position : positionFromClientX(event.clientX, state.track || target, state),
            alignment: target.getAttribute?.('data-tab-alignment') || TAB_ALIGNMENTS[tabAlignmentIndex],
            leader: target.getAttribute?.('data-tab-leader') || TAB_LEADERS[tabLeaderIndex],
        });
    };

    const onPointerDown = event => {
        const tabStop = event.target?.closest?.('[data-ruler-tab-stop]');
        const marker = event.target?.closest?.('[data-ruler-marker="first-line"], [data-ruler-marker="left-indent"], [data-ruler-marker="right-indent"]');
        root.setAttribute('data-canvas-ruler-last-pointerdown-target', tabStop
            ? 'tab-stop'
            : marker ? `marker:${marker.getAttribute?.('data-ruler-marker') || ''}` : '');
        root.setAttribute('data-canvas-ruler-last-pointerdown-detail', String(Number(event.detail || 0)));
        if (!tabStop && !marker) {
            return;
        }

        if (tabStop && Number(event.detail || 0) >= 2) {
            openTabsDialog(root.ownerDocument, root, executeCommand, {
                position: Number(tabStop.getAttribute?.('data-tab-position') || 0) || 0,
                alignment: tabStop.getAttribute?.('data-tab-alignment') || TAB_ALIGNMENTS[tabAlignmentIndex],
                leader: tabStop.getAttribute?.('data-tab-leader') || TAB_LEADERS[tabLeaderIndex],
            });
            event.preventDefault?.();
            return;
        }

        const state = getState();
        const track = root.querySelector?.('.tm-document-canvas-ruler__track');
        if (!track) {
            return;
        }

        drag = {
            pointerId: event.pointerId,
            kind: tabStop ? 'tab' : 'indent',
            marker: marker?.getAttribute?.('data-ruler-marker') || '',
            fromPosition: Number(tabStop?.getAttribute?.('data-tab-position') || NaN),
            alignment: tabStop?.getAttribute?.('data-tab-alignment') || TAB_ALIGNMENTS[tabAlignmentIndex],
            leader: tabStop?.getAttribute?.('data-tab-leader') || TAB_LEADERS[tabLeaderIndex],
            startClientX: Number(event.clientX || 0),
            state,
            track,
        };
        try {
            event.target?.setPointerCapture?.(event.pointerId);
        } catch {
            root.setAttribute('data-canvas-ruler-pointer-capture', 'unavailable');
        }
        event.preventDefault?.();
    };

    const onPointerMove = event => {
        if (!drag || event.pointerId !== drag.pointerId) {
            return;
        }

        const position = positionFromClientX(event.clientX, drag.track, drag.state);
        root.setAttribute('data-canvas-ruler-drag-position', String(Math.round(position * 100) / 100));
        event.preventDefault?.();
    };

    const onPointerUp = event => {
        if (!drag || event.pointerId !== drag.pointerId) {
            return;
        }

        const position = positionFromClientX(event.clientX, drag.track, drag.state);
        root.setAttribute('data-canvas-ruler-last-pointerup-kind', drag.kind);
        root.setAttribute('data-canvas-ruler-last-pointerup-marker', drag.marker || '');
        root.setAttribute('data-canvas-ruler-last-pointerup-position', String(Math.round(position * 100) / 100));
        if (drag.kind === 'tab') {
            const now = event.timeStamp || performance.now?.() || Date.now();
            const moved = Math.abs(Number(event.clientX || 0) - Number(drag.startClientX || 0));
            if (moved < 4
                && lastTabClick
                && now - lastTabClick.time < 650
                && Math.abs(drag.fromPosition - lastTabClick.position) < 2) {
                openTabsDialog(root.ownerDocument, root, executeCommand, {
                    position: drag.fromPosition,
                    alignment: drag.alignment,
                    leader: drag.leader,
                });
                lastTabClick = null;
                root.removeAttribute('data-canvas-ruler-drag-position');
                try {
                    event.target?.releasePointerCapture?.(event.pointerId);
                } catch {
                    root.setAttribute('data-canvas-ruler-pointer-release', 'unavailable');
                }
                drag = null;
                event.preventDefault?.();
                return;
            }

            if (moved < 4) {
                lastTabClick = { position: drag.fromPosition, time: now };
                root.removeAttribute('data-canvas-ruler-drag-position');
                try {
                    event.target?.releasePointerCapture?.(event.pointerId);
                } catch {
                    root.setAttribute('data-canvas-ruler-pointer-release', 'unavailable');
                }
                drag = null;
                event.preventDefault?.();
                return;
            }

            executeCommand('moveTabStop', {
                fromPosition: drag.fromPosition,
                position,
                alignment: drag.alignment,
                leader: drag.leader,
            });
        } else {
            const payload = indentPayloadForDrag(drag.marker, position, drag.state);
            root.setAttribute('data-canvas-ruler-last-indent-payload', JSON.stringify(payload));
            executeCommand('setParagraphIndents', payload);
        }

        root.removeAttribute('data-canvas-ruler-drag-position');
        try {
            event.target?.releasePointerCapture?.(event.pointerId);
        } catch {
            root.setAttribute('data-canvas-ruler-pointer-release', 'unavailable');
        }
        drag = null;
        event.preventDefault?.();
    };

    root.addEventListener?.('click', onClick);
    root.addEventListener?.('dblclick', onDoubleClick);
    root.addEventListener?.('pointerdown', onPointerDown);
    root.addEventListener?.('pointermove', onPointerMove);
    root.addEventListener?.('pointerup', onPointerUp);
    root.addEventListener?.('pointercancel', onPointerUp);
    updatePicker(root, tabAlignmentIndex, tabLeaderIndex);

    return {
        destroy() {
            root.removeEventListener?.('click', onClick);
            root.removeEventListener?.('dblclick', onDoubleClick);
            root.removeEventListener?.('pointerdown', onPointerDown);
            root.removeEventListener?.('pointermove', onPointerMove);
            root.removeEventListener?.('pointerup', onPointerUp);
            root.removeEventListener?.('pointercancel', onPointerUp);
        },
    };
}

function positionFromClientX(clientX, track, state = {}) {
    const rect = track?.getBoundingClientRect?.() || { left: 0 };
    const marginLeftPx = Number(state.marginLeftPx || 0) || 0;
    const leftIndentPx = Number(state.leftIndentPx || 0) || 0;
    // The track is as wide as the SCALED page (a fitted page): the pointer offset is back in page pixels before the margins are taken off.
    const scale = Math.max(0.01, Number(state.scale) || 1);
    const x = Math.max(0, Number(clientX || 0) - Number(rect.left || 0)) / scale;
    return Math.max(0, (x - marginLeftPx - leftIndentPx) * CSS_PIXELS_TO_POINTS);
}

function indentPayloadForDrag(marker, positionPoints, state = {}) {
    const leftIndent = Number(state.leftIndentPoints || 0) || 0;
    const rightIndent = Number(state.rightIndentPoints || 0) || 0;
    const firstLineIndent = Number(state.firstLineIndentPoints || 0) || 0;
    const pageBodyWidthPoints = Math.max(1, Number(state.pageBodyWidthPoints || 1) || 1);
    if (marker === 'right-indent') {
        return {
            leftIndent,
            firstLineIndent,
            rightIndent: Math.max(0, pageBodyWidthPoints - positionPoints),
        };
    }

    if (marker === 'first-line') {
        return {
            leftIndent,
            rightIndent,
            firstLineIndent: positionPoints - leftIndent,
        };
    }

    return {
        leftIndent: positionPoints,
        rightIndent,
        firstLineIndent,
    };
}

function updatePicker(root, alignmentIndex, leaderIndex) {
    const picker = root.querySelector?.('[data-testid="document-canvas-ruler-tab-picker"]');
    if (picker) {
        const alignment = TAB_ALIGNMENTS[alignmentIndex] || 'left';
        picker.setAttribute('data-ruler-tab-type', alignment);
        picker.textContent = { left: 'L', center: 'C', right: 'R', decimal: '.', bar: '|' }[alignment] || 'L';
    }

    const leaderPicker = root.querySelector?.('[data-testid="document-canvas-ruler-leader-picker"]');
    if (leaderPicker) {
        const leader = TAB_LEADERS[leaderIndex] || 'none';
        leaderPicker.setAttribute('data-ruler-tab-leader', leader);
        leaderPicker.textContent = { none: '·', dots: '••', dash: '—', underline: '_' }[leader] || '·';
    }
}

function openTabsDialog(doc, root, executeCommand, initial) {
    if (!doc?.createElement) {
        return;
    }

    root.querySelector?.('[data-testid="document-canvas-tabs-dialog"]')?.remove?.();
    const dialog = doc.createElement('div');
    dialog.className = 'tm-document-canvas-ruler__dialog';
    dialog.setAttribute('data-testid', 'document-canvas-tabs-dialog');
    dialog.setAttribute('role', 'dialog');

    const position = numberInput(doc, 'document-canvas-tabs-position', initial.position);
    const alignment = select(doc, 'document-canvas-tabs-alignment', TAB_ALIGNMENTS, initial.alignment);
    const leader = select(doc, 'document-canvas-tabs-leader', TAB_LEADERS, initial.leader);
    const set = button(doc, 'document-canvas-tabs-set', '+');
    const clear = button(doc, 'document-canvas-tabs-clear', '×');
    const close = button(doc, 'document-canvas-tabs-close', '✓');

    set.addEventListener('click', () => executeCommand('setTabStop', {
        position: Number(position.value) || 0,
        alignment: alignment.value,
        leader: leader.value,
    }));
    clear.addEventListener('click', () => executeCommand('clearTabStops', { position: Number(position.value) || 0 }));
    close.addEventListener('click', () => dialog.remove());

    dialog.append(position, alignment, leader, set, clear, close);
    root.appendChild(dialog);
    position.focus?.();
}

function numberInput(doc, testId, value) {
    const input = doc.createElement('input');
    input.type = 'number';
    input.min = '0';
    input.step = '1';
    input.value = String(Math.round((Number(value) || 0) * 100) / 100);
    input.setAttribute('data-testid', testId);
    return input;
}

function select(doc, testId, values, selected) {
    const item = doc.createElement('select');
    item.setAttribute('data-testid', testId);
    for (const value of values) {
        const option = doc.createElement('option');
        option.value = value;
        option.textContent = value;
        item.appendChild(option);
    }

    item.value = values.includes(selected) ? selected : values[0];
    return item;
}

function button(doc, testId, text) {
    const item = doc.createElement('button');
    item.type = 'button';
    item.textContent = text;
    item.setAttribute('data-testid', testId);
    return item;
}

export function pointsToCssPixels(value) {
    return (Number(value) || 0) * POINTS_TO_CSS_PIXELS;
}
