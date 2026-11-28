import { createCommandDispatcher } from './command-dispatcher.mjs';
import {
    applyInlineFormatCommand,
    createInlineFormatState,
    linkAtPosition,
    marksForInsertion,
    queryInlineFormattingState,
} from './inline-format.mjs';
import {
    applyParagraphCommand,
    canonicalCommandId,
    createParagraphCommandState,
    isCanvasViewCommand,
    queryParagraphCommandState,
} from './paragraph-commands.mjs';
import { applyCanvasTextEdit } from '../input/text-editing.mjs';
import {
    applyTableCommand,
    canonicalTableCommandId,
    isTableCommand,
    queryTableCommandState,
} from '../tables/table-ops.mjs';
import {
    applyImageCommand,
    canonicalImageCommandId,
    isImageCommand,
    queryImageCommandState,
} from '../objects/image-commands.mjs';
import {
    applyFieldCommand,
    canonicalFieldCommandId,
    isFieldCommand,
    queryFieldCommandState,
} from './fields.mjs';
import { canEditRestrictedSelection } from '../annotations/restricted-editing.mjs';
import {
    applyMathCommand,
    canonicalMathCommandId,
    isMathCommand,
    queryMathCommandState,
} from './math-commands.mjs';
import {
    applyContentControlCommand,
    canonicalContentControlCommandId,
    isContentControlCommand,
    queryContentControlCommandState,
} from '../controls/forms-mode.mjs';
import {
    applySigningFieldCommand,
    canonicalSigningFieldCommandId,
    isSigningFieldCommand,
    querySigningFieldCommandState,
} from './signing-field-commands.mjs';
import {
    applyFormatPainterCommand,
    canonicalFormatPainterCommandId,
    createFormatPainterState,
    isFormatPainterCommand,
    queryFormatPainterCommandState,
} from './format-painter.mjs';
import { applyFormattingRevision } from '../annotations/track-changes.mjs';
import {
    applyInsertSymbolCommand,
    canonicalInsertSymbolCommandId,
    isInsertSymbolCommand,
    queryInsertSymbolCommandState,
} from './insert-symbol.mjs';
import {
    applyCanvasViewCommand,
    createCanvasViewState,
    isCanvasViewModeCommand,
    queryCanvasViewCommandState,
} from '../view/view-modes.mjs';
import {
    findCanvasMatches,
    matchRange,
    normalizeSearchOptions,
    replaceAllCanvasMatches,
    replaceCanvasMatch,
} from '../search/search-engine.mjs';
import { applyBookmarkToSelection, findBookmark, listBookmarks } from '../navigation/bookmarks.mjs';
import { extractCanvasOutline, findOutlineTarget } from '../navigation/outline.mjs';
import { insertTableOfContents, updateTableOfContents } from '../navigation/toc-generator.mjs';

const INLINE_COMMANDS = [
    'bold',
    'italic',
    'underline',
    'strikethrough',
    'strike',
    'superscript',
    'subscript',
    'smallcaps',
    'allcaps',
    'doublestrikethrough',
    'doublestrike',
    'characterspacing',
    'setcharacterspacing',
    'characterscale',
    'setcharacterscale',
    'kerning',
    'togglekerning',
    'changecase',
    'increasefontsize',
    'decreasefontsize',
    'clearcharacterformatting',
    'fontfamily',
    'fontsize',
    'textcolor',
    'highlight',
    'clearformatting',
    'link',
    'removelink',
];

const PARAGRAPH_COMMANDS = [
    'align',
    'alignleft',
    'aligncenter',
    'alignright',
    'alignjustify',
    'linespacing',
    'spacingbefore',
    'spacingafter',
    'increaseindent',
    'decreaseindent',
    'setparagraphindents',
    'setparagraphindent',
    'settabstop',
    'addtabstop',
    'movetabstop',
    'updatetabstop',
    'cleartabstops',
    'cleartabs',
    'setdefaulttabwidth',
    'bulletlist',
    'numberedlist',
    'togglebulletlist',
    'togglenumberedlist',
    'increaselistlevel',
    'decreaselistlevel',
    'setlistformat',
    'numberformat',
    'setnumberformat',
    'restartnumbering',
    'continuenumbering',
    'setnumberingvalue',
    'defineliststyle',
    'setliststyle',
    'blockstyle',
    'setparagraphstyle',
    'applystyle',
    'modifystyle',
    'updatestyle',
    'definestyle',
    'createstyle',
    'createstylefromselection',
    'newstylefromselection',
    'deletestyle',
    'removestyle',
    'renamestyle',
    'resetstyleformatting',
    'heading1',
    'heading2',
    'heading3',
    'heading4',
    'heading5',
    'heading6',
    'quotestyle',
    'showruler',
    'showblocks',
    'togglenonprintingcharacters',
];

export function createCanvasCommandRuntime(options = {}) {
    const dispatcher = options.dispatcher || createCommandDispatcher();
    const getModel = requiredFunction(options.getModel, 'Canvas command runtime requires getModel.');
    const getSelection = requiredFunction(options.getSelection, 'Canvas command runtime requires getSelection.');
    const commit = requiredFunction(options.commit, 'Canvas command runtime requires commit.');
    const getLayout = typeof options.getLayout === 'function' ? options.getLayout : () => null;
    const history = options.history || null;
    const openUrl = typeof options.openUrl === 'function' ? options.openUrl : () => false;
    const getViewportMetrics = typeof options.getViewportMetrics === 'function' ? options.getViewportMetrics : () => ({});
    const getTrackChangesState = typeof options.getTrackChangesState === 'function'
        ? options.getTrackChangesState
        : () => ({ enabled: false, author: null });

    let formatState = createInlineFormatState();
    // Caret position where the current sticky (pending) formatting was established. Pending marks persist while
    // typing advances forward in the same block, but are discarded once the caret navigates elsewhere (Word
    // behaviour) — see reconcilePendingMarks.
    let pendingAnchor = null;
    let paragraphState = createParagraphCommandState(options.paragraphState);
    let formatPainterState = createFormatPainterState();
    let canvasViewState = createCanvasViewState(options.viewState);
    let searchState = createSearchState();
    let revision = 0;
    let lastCommand = null;

    for (const command of INLINE_COMMANDS) {
        dispatcher.register(command, payload => executeInlineCommand(command, payload));
    }
    for (const command of PARAGRAPH_COMMANDS) {
        dispatcher.register(command, payload => executeParagraphCommand(command, payload));
    }
    for (const command of [
        'inserttable',
        'deletetable',
        'toggletableheaderrow',
        'toggleheaderrow',
        'addtablerow',
        'inserttablerow',
        'inserttablerowafter',
        'inserttablerowbefore',
        'insertrow',
        'insertrowbefore',
        'insertrowafter',
        'addtablecolumn',
        'inserttablecolumn',
        'inserttablecolumnafter',
        'inserttablecolumnbefore',
        'insertcolumn',
        'insertcolumnbefore',
        'insertcolumnafter',
        'deletetablerow',
        'deleterow',
        'deletetablecolumn',
        'deletecolumn',
        'mergetablecells',
        'mergecells',
        'splittablecell',
        'splitcell',
        'resizetablecolumn',
        'resizecolumn',
        'settablecellformat',
        'setcellformat',
        'settableproperties',
        'setcellproperties',
        'navigatetablecell',
        'sorttable',
        'settableformula',
        'tableformula',
        'setcellmargins',
        'settablecellmargins',
        'setcellborders',
        'settablecellborders',
        'converttabletotext',
        'converttexttotable',
    ]) {
        dispatcher.register(command, payload => executeTableCommand(command, payload));
    }
    for (const command of [
        'insertimage',
        'insertimageurl',
        'insertdrawing',
        'insertshape',
        'insertautoshape',
        'inserttextbox',
        'insertline',
        'insertconnector',
        'insertchart',
        'updateimagelayout',
        'moveimage',
        'resizeimage',
        'setimagewrapmode',
        'setimagesize',
        'setimageposition',
        'setimageobjectposition',
        'setimageanchormode',
        'setimagemetadata',
        'setimagealttext',
        'setimagecaption',
        'setimagedecorative',
        'setimageurl',
        'toggleimagecaption',
        'setimagezorder',
        'bringimageforward',
        'sendimagebackward',
        'updatechartdata',
        'setchartdata',
        'editchartdata',
        'activatetextboxedit',
        'entertextboxedit',
        'focustextbox',
        'exittextboxedit',
        'inserttextboxtext',
        'typetextboxtext',
        'inserttextboxparagraph',
        'inserttextboxlinebreak',
        'deletetextboxtextbackward',
        'backspacetextboxtext',
        'deletetextboxtextforward',
        'settextboxtext',
        'replacetextboxtext',
        'settextboxtextalignment',
        'setdrawingtextalignment',
        'settextboxtextstyle',
        'setdrawingtextstyle',
        'setdrawingtextformat',
        'updateconnectorendpoint',
        'setconnectorendpoint',
        'moveconnectorendpoint',
        'bringtofront',
        'sendtoback',
        'groupobjects',
        'groupdrawings',
        'ungroupobjects',
        'ungroupdrawings',
        'alignobjects',
        'aligndrawingobjects',
        'distributeobjects',
        'distributedrawingobjects',
        'deleteimage',
        'deletedrawing',
        'deleteobject',
        'removeobject',
    ]) {
        dispatcher.register(command, payload => executeImageCommand(command, payload));
    }
    for (const command of [
        'insertfield',
        'insertpagenumber',
        'insertpagecount',
        'insertpagexofy',
        'insertdatefield',
        'insertdocumenttitlefield',
        'insertauthorfield',
        'inserttimefield',
        'insertfilenamefield',
        'insertstylereffield',
        'insertcrossreference',
        'crossreference',
        'gotoreference',
        'insertcaption',
        'inserttableoffigures',
        'inserttableoffiguresfield',
        'insertbibliography',
        'insertcitation',
        'updatefield',
        'updatefields',
        'updateallfields',
        'insertfootnote',
        'insertendnote',
        'insertpagebreak',
        'deletepagebreak',
        'inserttoken',
        'setpagesettings',
        'setpagesetup',
        'differentfirstpage',
        'togglefirstpageheaderfooter',
        'differentoddeven',
        'toggleoddevenheaderfooter',
    ]) {
        dispatcher.register(command, payload => executeFieldCommand(command, payload));
    }
    for (const command of [
        'insertequation',
        'insertmath',
        'insertlinearmath',
        'insertmathlinear',
        'insertmathsymbol',
        'insertequationsymbol',
        'insertfraction',
        'insertradical',
        'insertsquareroot',
        'insertsuperscript',
        'insertsubscript',
        'insertnary',
        'insertsum',
        'insertproduct',
        'insertdelimiter',
        'insertparentheses',
        'insertlimit',
        'insertaccent',
        'insertbar',
        'insertborderbox',
        'insertmatrix',
        'setmathdisplaymode',
        'togglemathdisplaymode',
        'deactivatemathslot',
        'exitmathslot',
        'activatemathslot',
        'selectmathslot',
        'focusmathslot',
        'selectmathslotrange',
        'selectmathrange',
        'movemathslot',
        'nextmathslot',
        'previousmathslot',
        'insertmathslottext',
        'insertmathslotlinear',
        'insertmathslotsymbol',
        'deletemathslotbackward',
        'backspacemathslot',
        'deletemathslotforward',
        'deletemathslot',
        'addmathmatrixrow',
        'insertmathmatrixrow',
        'addmathmatrixcolumn',
        'insertmathmatrixcolumn',
    ]) {
        dispatcher.register(command, payload => executeMathCommand(command, payload));
    }
    for (const command of [
        'setcontentcontrolvalue',
        'setcontentcontroltext',
        'setcontentcontroldate',
        'setcontentcontroldatevalue',
        'setcontentcontrolpicture',
        'insertcontentcontrolpicture',
        'setcontentcontrolcombotext',
        'togglecontentcontrol',
        'togglecontentcontrolcheckbox',
        'selectcontentcontroloption',
        'navigatecontentcontrol',
        'nextcontentcontrol',
        'previouscontentcontrol',
        'focuscontentcontrol',
        'addrepeatingsectionitem',
        'removerepeatingsectionitem',
        'removecontentcontrolrepeatingitem',
    ]) {
        dispatcher.register(command, payload => executeContentControlCommand(command, payload));
    }
    for (const command of [
        'insertsigningfield',
        'addsigningfield',
        'updatesigningfield',
        'setsigningfield',
        'removesigningfield',
        'deletesigningfield',
    ]) {
        dispatcher.register(command, payload => executeSigningFieldCommand(command, payload));
    }
    for (const command of [
        'copyformat',
        'copyformatting',
        'formatpainter',
        'lockformatpainter',
        'pasteformat',
        'pasteformatting',
        'applyformat',
        'applyformatting',
        'cancelformatpainter',
        'clearformatpainter',
    ]) {
        dispatcher.register(command, payload => executeFormatPainterCommand(command, payload));
    }
    for (const command of [
        'insertsymbol',
        'insertspecialcharacter',
        'insertemoji',
        'insertemdash',
        'insertendash',
        'insertnonbreakingspace',
        'insertnbsp',
        'insertoptionalhyphen',
    ]) {
        dispatcher.register(command, payload => executeInsertSymbolCommand(command, payload));
    }
    for (const command of [
        'setviewmode',
        'viewmode',
        'printlayout',
        'readingmode',
        'readmode',
        'weblayout',
        'webmode',
        'outlineview',
        'outlinemode',
        'setzoom',
        'customzoom',
        'zoomin',
        'zoomout',
        'fitpage',
        'fitwidth',
        'zoompagewidth',
        'multiplepages',
        'twopages',
        'ctrlwheelzoom',
        'pinchzoom',
        'applyautofit',
        'setautofit',
        'resetzoom',
        'openprintpreview',
        'printpreview',
        'closeprintpreview',
        'printdocument',
        'print',
    ]) {
        dispatcher.register(command, payload => executeCanvasViewCommand(command, payload));
    }

    dispatcher.register('undo', () => undo());
    dispatcher.register('redo', () => redo());
    dispatcher.register('setselection', payload => setSelectionCommand(payload));
    dispatcher.register('setcaret', payload => setSelectionCommand(payload));
    dispatcher.register('openlink', payload => openLink(payload));
    dispatcher.register('replacerange', payload => replaceRange(payload));
    for (const command of [
        'find',
        'findnext',
        'findprev',
        'gotosearchresult',
        'clearfind',
        'replacecurrent',
        'replaceone',
        'replaceall',
        'gotoheading',
        'navigateheading',
        'gotobookmark',
        'insertbookmark',
        'addbookmark',
        'inserttableofcontents',
        'inserttoc',
        'updatetableofcontents',
        'updatetoc',
    ]) {
        dispatcher.register(command, payload => executeNavigationCommand(command, payload));
    }

    function execCommand(commandId, argument = null) {
        const normalized = normalizeCommandId(commandId);
        const result = dispatcher.execute(normalized, argument);
        return {
            handled: result.handled,
            commandId: normalized,
            result: result.result || null,
            formattingState: queryCommandState(),
            history: history?.snapshot?.() || null,
        };
    }

    function queryCommand(commandId) {
        const normalized = normalizeCommandId(commandId);
        const state = queryCommandState();
        if (!normalized) {
            return state;
        }

        const stateKey = commandStateKey(normalized);
        return state.commands?.[stateKey] || {
            disabled: !dispatcher.listCommands().includes(normalized),
            active: false,
            mixed: false,
            value: null,
            state: 'inactive',
        };
    }

    function queryCommandState(options) {
        // Outline + bookmark extraction walks the whole document. The toolbar formatting readback (polled
        // after every typing burst) does NOT need navigation, so callers can opt out to keep it O(selection).
        const includeNavigation = !(options && options.includeNavigation === false);
        // A toolbar/state poll happens on selection change too — reconcile so a caret that moved away from the
        // sticky-format anchor clears pending before the pressed-state is read.
        reconcilePendingMarks();
        const inline = queryInlineFormattingState(getModel(), getSelection(), formatState);
        const paragraph = queryParagraphCommandState(getModel(), getSelection(), paragraphState);

        // Fast path for the toolbar UI snapshot (buildUiState / getFormattingStateJson): it only consumes the
        // inline + paragraph + image + view groups, so skip the table/field/math/forms/format-painter/symbol/
        // search/navigation queries — each of which walks the model — that the full state would also compute
        // (perf phase 2.3). This is the per-toolbar-command hot path.
        if (options && options.formattingOnly === true) {
            const imageOnly = queryImageCommandState(getModel(), getSelection());
            const canvasViewOnly = queryCanvasViewCommandState(canvasViewState);
            return {
                ...inline,
                commands: {
                    ...(inline.commands || {}),
                    ...(paragraph.commands || {}),
                    ...(imageOnly.commands || {}),
                    ...(canvasViewOnly.commands || {}),
                },
                paragraph: paragraph.paragraph,
                image: imageOnly.image,
                view: {
                    ...(paragraph.view || {}),
                    ...(canvasViewOnly.view || {}),
                },
                revision,
                lastCommand,
            };
        }

        const table = queryTableCommandState(getModel(), getSelection());
        const image = queryImageCommandState(getModel(), getSelection());
        const fields = queryFieldCommandState(getModel(), getSelection());
        const math = queryMathCommandState(getModel(), getSelection());
        const forms = queryContentControlCommandState(getModel(), getSelection());
        const signing = querySigningFieldCommandState(getModel(), getSelection());
        const formatPainter = queryFormatPainterCommandState(getModel(), getSelection(), formatPainterState);
        const symbols = queryInsertSymbolCommandState(getModel(), getSelection());
        const canvasView = queryCanvasViewCommandState(canvasViewState);
        return {
            ...inline,
            commands: {
                ...(inline.commands || {}),
                ...(paragraph.commands || {}),
                ...(table.commands || {}),
                ...(image.commands || {}),
                ...(fields.commands || {}),
                ...(math.commands || {}),
                ...(forms.commands || {}),
                ...(signing.commands || {}),
                ...(formatPainter.commands || {}),
                ...(symbols.commands || {}),
                ...(canvasView.commands || {}),
            },
            paragraph: paragraph.paragraph,
            table: table.table,
            image: image.image,
            fields: fields.fields,
            math: math.math,
            forms: forms.forms,
            formatPainter: formatPainter.formatPainter,
            search: clone(searchState),
            navigation: includeNavigation
                ? {
                    outline: extractCanvasOutline(getModel(), getLayout()),
                    bookmarks: listBookmarks(getModel()),
                }
                : null,
            view: {
                ...(paragraph.view || {}),
                ...(canvasView.view || {}),
            },
            history: history?.snapshot?.() || null,
            revision,
            lastCommand,
        };
    }

    function getPendingMarks() {
        return marksForInsertion(formatState);
    }

    // Raw tri-state pending overrides (add + remove entries) for the input pipeline to merge onto the inherited
    // run marks at the insertion point. Reconciles first so stale pending from a different caret is dropped.
    function getPendingMarkOverrides() {
        reconcilePendingMarks();
        return (formatState.pendingMarks || []).map(mark => clone(mark));
    }

    // Discards sticky pending formatting when the caret has navigated away from where it was set. Typing
    // forward in the same block keeps it (and advances the anchor); a jump to another block or backwards
    // clears it. Cheap, so it can run on every pending read and command/state query.
    function reconcilePendingMarks() {
        const pending = formatState.pendingMarks || [];
        if (pending.length === 0) {
            pendingAnchor = null;
            return;
        }

        const focus = getSelection()?.focus;
        if (!pendingAnchor || !focus) {
            return;
        }

        const sameBlock = String(focus.blockId || '') === String(pendingAnchor.blockId || '');
        const forward = sameBlock && Number(focus.offset || 0) >= Number(pendingAnchor.offset || 0);
        if (!forward) {
            formatState = createInlineFormatState();
            pendingAnchor = null;
            return;
        }

        pendingAnchor = { blockId: String(focus.blockId || ''), offset: Number(focus.offset || 0) };
    }

    function getViewState() {
        // The view group is composed of paragraph.view + canvasView.view in BOTH query paths, so the
        // formattingOnly fast path returns the identical view object while skipping the
        // whole-document walks (tables/fields/math/forms/outline/bookmarks) the full query also
        // runs. getViewState is called from every render, so it must stay O(selection).
        return queryCommandState({ formattingOnly: true }).view || {};
    }

    function getSearchState() {
        return clone(searchState);
    }

    function executeInlineCommand(commandId, argument) {
        // Restricted editing (phase 8): formatting is a mutation like typing — veto it outside
        // the editable markers while the document is protected.
        const inlineGuard = canEditRestrictedSelection(getModel(), getSelection());
        if (!inlineGuard.allowed) {
            return {
                changed: false,
                commandId: normalizeCommandId(commandId),
                blocked: 'protected',
                readonlyReason: inlineGuard.reason,
            };
        }

        const before = captureSnapshot();
        const result = applyInlineFormatCommand(getModel(), getSelection(), commandId, argument, formatState);
        formatState = result.state;
        // Anchor the sticky formatting to the caret it was set at so it survives forward typing but is dropped
        // once the caret navigates away (reconcilePendingMarks).
        const pendingFocus = getSelection()?.focus;
        pendingAnchor = (formatState.pendingMarks || []).length > 0 && pendingFocus
            ? { blockId: String(pendingFocus.blockId || ''), offset: Number(pendingFocus.offset || 0) }
            : null;
        revision += 1;
        lastCommand = {
            id: normalizeCommandId(commandId),
            changed: result.changed,
            revision,
        };

        if (result.changed) {
            const tracking = getTrackChangesState();
            if (tracking.enabled === true) {
                const tracked = applyFormattingRevision(result.model, getSelection(), commandId, { author: tracking.author || null });
                result.model = tracked.model;
                result.dirtyBlockIds = unique([...(result.dirtyBlockIds || []), ...(tracked.dirtyBlockIds || [])]);
            }

            const after = {
                model: result.model,
                selection: result.selection,
                formatState,
                paragraphState: createParagraphCommandState(paragraphState),
            };
            pushHistory({
                id: `canvas-format-${revision}`,
                kind: 'inline-format',
                commandId: normalizeCommandId(commandId),
                before,
                after,
            });
            commit({
                model: result.model,
                selection: result.selection,
                result,
                command: lastCommand,
            });
        }

        return {
            changed: result.changed,
            commandId: normalizeCommandId(commandId),
            formattingState: result.formattingState,
        };
    }

    function executeParagraphCommand(commandId, argument) {
        const canonical = canonicalCommandId(commandId);
        // Restricted editing (phase 8): veto paragraph MUTATIONS outside the editable markers —
        // canvas view commands (zoom, ruler, show blocks…) are not mutations and stay allowed.
        if (!isCanvasViewCommand(canonical)) {
            const paragraphGuard = canEditRestrictedSelection(getModel(), getSelection());
            if (!paragraphGuard.allowed) {
                return {
                    changed: false,
                    commandId: normalizeCommandId(commandId),
                    blocked: 'protected',
                    readonlyReason: paragraphGuard.reason,
                };
            }
        }

        const effectiveArgument = /^heading[1-6]$/i.test(String(commandId || ''))
            ? { styleName: `Heading ${String(commandId).match(/[1-6]/)?.[0] || 1}` }
            : argument;
        const before = captureSnapshot();
        const result = applyParagraphCommand(getModel(), getSelection(), canonical, effectiveArgument, paragraphState);
        paragraphState = createParagraphCommandState(result.state);
        revision += 1;
        lastCommand = {
            id: normalizeCommandId(commandId),
            changed: result.changed === true,
            viewChanged: result.viewChanged === true,
            revision,
        };

        if (result.changed || result.viewChanged) {
            if (result.changed && !isCanvasViewCommand(canonical)) {
                const after = {
                    model: result.model,
                    selection: result.selection,
                    formatState: createInlineFormatState(formatState),
                    paragraphState,
                };
                pushHistory({
                    id: `canvas-paragraph-${revision}`,
                    kind: 'paragraph-format',
                    commandId: normalizeCommandId(commandId),
                    before,
                    after,
                });
            }

            commit({
                model: result.model,
                selection: result.selection,
                result,
                command: lastCommand,
            });
        }

        return {
            changed: result.changed,
            viewChanged: result.viewChanged === true,
            commandId: normalizeCommandId(commandId),
            dirtyBlockIds: result.dirtyBlockIds || [],
            formattingState: result.formattingState,
        };
    }

    function executeCanvasViewCommand(commandId, argument) {
        if (!isCanvasViewModeCommand(commandId)) {
            return { changed: false, viewChanged: false, commandId: normalizeCommandId(commandId) };
        }

        const metrics = {
            ...(getViewportMetrics() || {}),
            ...(argument?.metrics || {}),
        };
        const previousZoom = canvasViewState.zoom;
        const result = applyCanvasViewCommand(canvasViewState, commandId, argument, metrics);
        canvasViewState = createCanvasViewState(result.state || canvasViewState);
        // A change of the automatic-fit MODE alone (same preset, same scale) repaints nothing: the state is updated and reported, the page is not drawn again.
        const zoomModeOnly = result.operation === 'zoom'
            && result.viewChanged === true
            && previousZoom?.preset === canvasViewState.zoom.preset
            && Math.abs((Number(previousZoom?.scale) || 1) - canvasViewState.zoom.scale) < 0.0001;
        revision += 1;
        lastCommand = {
            id: normalizeCommandId(commandId),
            changed: false,
            viewChanged: result.viewChanged === true,
            printRequested: result.printRequested === true,
            revision,
        };

        if ((result.viewChanged && !zoomModeOnly) || result.printRequested) {
            commit({
                model: getModel(),
                selection: getSelection(),
                result: {
                    changed: false,
                    viewChanged: result.viewChanged === true,
                    operation: result.operation || 'view',
                    printRequested: result.printRequested === true,
                    view: queryCanvasViewCommandState(canvasViewState).view,
                },
                command: lastCommand,
            });
        }

        return {
            changed: false,
            viewChanged: result.viewChanged === true,
            commandId: normalizeCommandId(commandId),
            printRequested: result.printRequested === true,
            view: queryCanvasViewCommandState(canvasViewState).view,
        };
    }

    function executeTableCommand(commandId, argument) {
        if (!isTableCommand(commandId)) {
            return { changed: false, commandId: normalizeCommandId(commandId) };
        }

        const before = captureSnapshot();
        const result = applyTableCommand(getModel(), getSelection(), commandId, argument);
        revision += 1;
        lastCommand = {
            id: normalizeCommandId(commandId),
            changed: result.changed === true,
            selectionChanged: result.selection && JSON.stringify(result.selection) !== JSON.stringify(before.selection),
            revision,
        };

        if (result.changed || lastCommand.selectionChanged) {
            if (result.changed) {
                const after = {
                    model: result.model,
                    selection: result.selection,
                    formatState: createInlineFormatState(formatState),
                    paragraphState: createParagraphCommandState(paragraphState),
                };
                pushHistory({
                    id: `canvas-table-${revision}`,
                    kind: 'table',
                    commandId: normalizeCommandId(commandId),
                    before,
                    after,
                });
            }

            commit({
                model: result.model,
                selection: result.selection,
                result,
                command: lastCommand,
            });
        }

        return {
            changed: result.changed === true,
            selectionChanged: lastCommand.selectionChanged === true,
            commandId: normalizeCommandId(commandId),
            canonicalCommandId: canonicalTableCommandId(commandId),
            operation: result.operation || canonicalTableCommandId(commandId),
            dirtyBlockIds: result.dirtyBlockIds || [],
            table: result.table || null,
        };
    }

    function executeImageCommand(commandId, argument) {
        if (!isImageCommand(commandId)) {
            return { changed: false, commandId: normalizeCommandId(commandId) };
        }

        const before = captureSnapshot();
        const result = applyImageCommand(getModel(), getSelection(), commandId, argument);
        revision += 1;
        lastCommand = {
            id: normalizeCommandId(commandId),
            changed: result.changed === true,
            selectionChanged: result.selection && JSON.stringify(result.selection) !== JSON.stringify(before.selection),
            revision,
        };

        if (result.changed || lastCommand.selectionChanged) {
            if (result.changed) {
                const after = {
                    model: result.model,
                    selection: result.selection,
                    formatState: createInlineFormatState(formatState),
                    paragraphState: createParagraphCommandState(paragraphState),
                };
                pushHistory({
                    id: `canvas-image-${revision}`,
                    kind: 'image',
                    commandId: normalizeCommandId(commandId),
                    before,
                    after,
                });
            }

            commit({
                model: result.model,
                selection: result.selection,
                result,
                command: lastCommand,
            });
        }

        return {
            changed: result.changed === true,
            selectionChanged: lastCommand.selectionChanged === true,
            commandId: normalizeCommandId(commandId),
            canonicalCommandId: canonicalImageCommandId(commandId),
            operation: result.operation || canonicalImageCommandId(commandId),
            dirtyBlockIds: result.dirtyBlockIds || [],
            object: result.object || null,
        };
    }

    function executeFieldCommand(commandId, argument) {
        if (!isFieldCommand(commandId)) {
            return { changed: false, commandId: normalizeCommandId(commandId) };
        }

        const before = captureSnapshot();
        const result = applyFieldCommand(getModel(), getSelection(), commandId, argument);
        revision += 1;
        lastCommand = {
            id: normalizeCommandId(commandId),
            changed: result.changed === true,
            selectionChanged: result.selection && JSON.stringify(result.selection) !== JSON.stringify(before.selection),
            revision,
        };

        if (result.changed || lastCommand.selectionChanged) {
            if (result.changed) {
                const after = {
                    model: result.model,
                    selection: result.selection || before.selection,
                    formatState: createInlineFormatState(formatState),
                    paragraphState: createParagraphCommandState(paragraphState),
                };
                pushHistory({
                    id: `canvas-fields-${revision}`,
                    kind: 'fields-notes-page',
                    commandId: normalizeCommandId(commandId),
                    before,
                    after,
                });
            }

            commit({
                model: result.model,
                selection: result.selection || before.selection,
                result,
                command: lastCommand,
            });
        }

        return {
            changed: result.changed === true,
            selectionChanged: lastCommand.selectionChanged === true,
            commandId: normalizeCommandId(commandId),
            canonicalCommandId: canonicalFieldCommandId(commandId),
            operation: result.operation || canonicalFieldCommandId(commandId),
            dirtyBlockIds: result.dirtyBlockIds || [],
            insertedRunIds: result.insertedRunIds || [],
            noteId: result.noteId || null,
        };
    }

    function executeMathCommand(commandId, argument) {
        if (!isMathCommand(commandId)) {
            return { changed: false, commandId: normalizeCommandId(commandId) };
        }

        const before = captureSnapshot();
        const result = applyMathCommand(getModel(), getSelection(), commandId, argument);
        revision += 1;
        lastCommand = {
            id: normalizeCommandId(commandId),
            changed: result.changed === true,
            selectionChanged: result.selection && JSON.stringify(result.selection) !== JSON.stringify(before.selection),
            revision,
        };

        if (result.changed || lastCommand.selectionChanged) {
            if (result.changed) {
                const after = {
                    model: result.model,
                    selection: result.selection || before.selection,
                    formatState: createInlineFormatState(formatState),
                    paragraphState: createParagraphCommandState(paragraphState),
                };
                pushHistory({
                    id: `canvas-math-${revision}`,
                    kind: 'math-equation',
                    commandId: normalizeCommandId(commandId),
                    before,
                    after,
                });
            }

            commit({
                model: result.model,
                selection: result.selection || before.selection,
                result,
                command: lastCommand,
            });
        }

        return {
            changed: result.changed === true,
            selectionChanged: lastCommand.selectionChanged === true,
            commandId: normalizeCommandId(commandId),
            canonicalCommandId: canonicalMathCommandId(commandId),
            operation: result.operation || canonicalMathCommandId(commandId),
            dirtyBlockIds: result.dirtyBlockIds || [],
            insertedRunIds: result.insertedRunIds || [],
            mathId: result.mathId || null,
            mathSlot: result.mathSlot || null,
            announcement: result.announcement || null,
            viewChanged: result.viewChanged === true,
        };
    }

    function executeContentControlCommand(commandId, argument) {
        if (!isContentControlCommand(commandId)) {
            return { changed: false, commandId: normalizeCommandId(commandId) };
        }

        const before = captureSnapshot();
        const result = applyContentControlCommand(getModel(), getSelection(), commandId, argument);
        revision += 1;
        lastCommand = {
            id: normalizeCommandId(commandId),
            changed: result.changed === true,
            selectionChanged: result.selection && JSON.stringify(result.selection) !== JSON.stringify(before.selection),
            revision,
        };

        if (result.changed || lastCommand.selectionChanged) {
            const after = {
                model: result.model,
                selection: result.selection || before.selection,
                formatState: createInlineFormatState(formatState),
                paragraphState: createParagraphCommandState(paragraphState),
            };
            if (result.changed) {
                pushHistory({
                    id: `canvas-content-control-${revision}`,
                    kind: 'content-control-form-fill',
                    commandId: normalizeCommandId(commandId),
                    before,
                    after,
                });
            }

            commit({
                model: result.model,
                selection: result.selection || before.selection,
                result,
                command: lastCommand,
            });
        }

        return {
            changed: result.changed === true,
            selectionChanged: lastCommand.selectionChanged === true,
            commandId: normalizeCommandId(commandId),
            canonicalCommandId: canonicalContentControlCommandId(commandId),
            operation: result.operation || canonicalContentControlCommandId(commandId),
            reason: result.reason || '',
            controlId: result.controlId || null,
            control: result.control || null,
            validation: result.validation || null,
            selection: result.selection || null,
            repeatingSection: result.repeatingSection || null,
            dirtyBlockIds: result.dirtyBlockIds || [],
        };
    }

    function executeSigningFieldCommand(commandId, argument) {
        if (!isSigningFieldCommand(commandId)) {
            return { changed: false, commandId: normalizeCommandId(commandId) };
        }

        const before = captureSnapshot();
        const result = applySigningFieldCommand(getModel(), getSelection(), commandId, argument);
        revision += 1;
        lastCommand = {
            id: normalizeCommandId(commandId),
            changed: result.changed === true,
            selectionChanged: result.selection && JSON.stringify(result.selection) !== JSON.stringify(before.selection),
            revision,
        };

        if (result.changed || lastCommand.selectionChanged) {
            const after = {
                model: result.model,
                selection: result.selection || before.selection,
                formatState: createInlineFormatState(formatState),
                paragraphState: createParagraphCommandState(paragraphState),
            };
            if (result.changed) {
                pushHistory({
                    id: `canvas-signing-field-${revision}`,
                    kind: 'signing-field',
                    commandId: normalizeCommandId(commandId),
                    before,
                    after,
                });
            }

            commit({
                model: result.model,
                selection: result.selection || before.selection,
                result,
                command: lastCommand,
            });
        }

        return {
            changed: result.changed === true,
            selectionChanged: lastCommand.selectionChanged === true,
            commandId: normalizeCommandId(commandId),
            canonicalCommandId: canonicalSigningFieldCommandId(commandId),
            operation: result.operation || canonicalSigningFieldCommandId(commandId),
            fieldUuid: result.fieldUuid || null,
            selection: result.selection || null,
            dirtyBlockIds: result.dirtyBlockIds || [],
        };
    }

    function executeFormatPainterCommand(commandId, argument) {
        if (!isFormatPainterCommand(commandId)) {
            return { changed: false, commandId: normalizeCommandId(commandId) };
        }

        const before = captureSnapshot();
        const result = applyFormatPainterCommand(getModel(), getSelection(), commandId, argument, formatPainterState);
        formatPainterState = createFormatPainterState(result.state);
        revision += 1;
        lastCommand = {
            id: normalizeCommandId(commandId),
            changed: result.changed === true,
            stateChanged: JSON.stringify(before.formatPainterState) !== JSON.stringify(formatPainterState),
            revision,
        };

        if (result.changed) {
            const after = {
                model: result.model,
                selection: result.selection || before.selection,
                formatState: createInlineFormatState(formatState),
                paragraphState: createParagraphCommandState(paragraphState),
                formatPainterState: createFormatPainterState(formatPainterState),
            };
            pushHistory({
                id: `canvas-format-painter-${revision}`,
                kind: 'format-painter',
                commandId: normalizeCommandId(commandId),
                before,
                after,
            });
            commit({
                model: result.model,
                selection: result.selection || before.selection,
                result,
                command: lastCommand,
            });
        }

        return {
            changed: result.changed === true,
            stateChanged: lastCommand.stateChanged === true,
            commandId: normalizeCommandId(commandId),
            canonicalCommandId: canonicalFormatPainterCommandId(commandId),
            operation: result.operation || canonicalFormatPainterCommandId(commandId),
            dirtyBlockIds: result.dirtyBlockIds || [],
            formatPainter: queryFormatPainterCommandState(getModel(), getSelection(), formatPainterState).formatPainter,
        };
    }

    function executeInsertSymbolCommand(commandId, argument) {
        if (!isInsertSymbolCommand(commandId)) {
            return { changed: false, commandId: normalizeCommandId(commandId) };
        }

        const before = captureSnapshot();
        const result = applyInsertSymbolCommand(getModel(), getSelection(), commandId, argument);
        revision += 1;
        lastCommand = {
            id: normalizeCommandId(commandId),
            changed: result.changed === true,
            revision,
        };

        if (result.changed) {
            const after = {
                model: result.model,
                selection: result.selection,
                formatState: createInlineFormatState(formatState),
                paragraphState: createParagraphCommandState(paragraphState),
                formatPainterState: createFormatPainterState(formatPainterState),
            };
            pushHistory({
                id: `canvas-symbol-${revision}`,
                kind: 'insert-symbol',
                commandId: normalizeCommandId(commandId),
                before,
                after,
            });
            commit({
                model: result.model,
                selection: result.selection,
                result,
                command: lastCommand,
            });
        }

        return {
            changed: result.changed === true,
            commandId: normalizeCommandId(commandId),
            canonicalCommandId: canonicalInsertSymbolCommandId(commandId),
            operation: result.operation || canonicalInsertSymbolCommandId(commandId),
            insertedText: result.insertedText || '',
            dirtyBlockIds: result.dirtyBlockIds || [],
        };
    }

    function undo() {
        const transaction = history?.undo?.();
        if (!transaction?.before) {
            return { changed: false };
        }

        formatState = createInlineFormatState(transaction.before.formatState);
        paragraphState = createParagraphCommandState(transaction.before.paragraphState);
        formatPainterState = createFormatPainterState(transaction.before.formatPainterState);
        revision += 1;
        lastCommand = { id: 'undo', changed: true, revision };
        commit({
            model: transaction.before.model,
            selection: transaction.before.selection,
            result: { changed: true, dirtyBlockIds: allBlockIds(transaction.before.model), operation: 'undo' },
            command: lastCommand,
        });
        return { changed: true, transactionId: transaction.id };
    }

    function redo() {
        const transaction = history?.redo?.();
        if (!transaction?.after) {
            return { changed: false };
        }

        formatState = createInlineFormatState(transaction.after.formatState);
        paragraphState = createParagraphCommandState(transaction.after.paragraphState);
        formatPainterState = createFormatPainterState(transaction.after.formatPainterState);
        revision += 1;
        lastCommand = { id: 'redo', changed: true, revision };
        commit({
            model: transaction.after.model,
            selection: transaction.after.selection,
            result: { changed: true, dirtyBlockIds: allBlockIds(transaction.after.model), operation: 'redo' },
            command: lastCommand,
        });
        return { changed: true, transactionId: transaction.id };
    }

    function openLink(payload = null) {
        const position = payload?.position || getSelection()?.focus || null;
        const link = linkAtPosition(getModel(), position);
        if (!link?.href) {
            return { opened: false };
        }

        openUrl(link.href);
        revision += 1;
        lastCommand = { id: 'openlink', changed: false, revision, href: link.href };
        return { opened: true, href: link.href };
    }

    function replaceRange(payload = null) {
        const blockId = String(payload?.blockId || '');
        const start = Math.max(0, Number(payload?.start || 0) || 0);
        const end = Math.max(start, Number(payload?.end || start) || start);
        const text = String(payload?.text ?? '');
        if (!blockId || start === end && text.length === 0) {
            return { changed: false, commandId: 'replacerange' };
        }

        const before = captureSnapshot();
        const result = applyCanvasTextEdit(getModel(), getSelection(), {
            type: 'replaceRange',
            range: {
                anchor: { blockId, offset: start },
                focus: { blockId, offset: end },
            },
            text,
            source: 'spellcheck',
        });
        revision += 1;
        lastCommand = {
            id: 'replacerange',
            changed: result.changed === true,
            revision,
        };

        if (result.changed) {
            const after = {
                model: result.model,
                selection: result.selection,
                formatState: createInlineFormatState(formatState),
                paragraphState: createParagraphCommandState(paragraphState),
            };
            pushHistory({
                id: `canvas-spellcheck-${revision}`,
                kind: 'spellcheck-replace',
                commandId: 'replacerange',
                before,
                after,
            });
            commit({
                model: result.model,
                selection: result.selection,
                result,
                command: lastCommand,
            });
        }

        return {
            changed: result.changed === true,
            commandId: 'replacerange',
            operation: result.operation || 'replaceRange',
            dirtyBlockIds: result.dirtyBlockIds || [],
        };
    }

    function executeNavigationCommand(commandId, argument) {
        const normalized = normalizeCommandId(commandId);
        if (normalized === 'find') {
            searchState = createSearchState(getModel(), argument);
            revision += 1;
            lastCommand = { id: 'find', changed: false, revision };
            return { changed: false, search: clone(searchState) };
        }

        if (normalized === 'clearfind') {
            searchState = createSearchState(getModel());
            revision += 1;
            lastCommand = { id: 'clearfind', changed: false, revision };
            return { changed: false, search: clone(searchState) };
        }

        if (normalized === 'findnext' || normalized === 'findprev') {
            const delta = normalized === 'findprev' ? -1 : 1;
            moveSearch(delta);
            revision += 1;
            lastCommand = { id: normalized, changed: false, revision };
            return { changed: false, search: clone(searchState), selectionChanged: selectActiveSearchMatch() };
        }

        if (normalized === 'gotosearchresult') {
            const index = Math.max(0, Number(argument?.index ?? argument) || 0);
            if (searchState.matches.length > 0) {
                searchState.activeIndex = Math.min(searchState.matches.length - 1, index);
            }
            revision += 1;
            lastCommand = { id: normalized, changed: false, revision };
            return { changed: false, search: clone(searchState), selectionChanged: selectActiveSearchMatch() };
        }

        if (normalized === 'replacecurrent' || normalized === 'replaceone') {
            return executeReplaceCurrent(argument);
        }

        if (normalized === 'replaceall') {
            return executeReplaceAll(argument);
        }

        if (normalized === 'gotoheading' || normalized === 'navigateheading') {
            return navigateToBlock(argument?.blockId || argument?.targetBlockId || argument);
        }

        if (normalized === 'gotobookmark') {
            const bookmark = findBookmark(getModel(), argument?.name || argument);
            return navigateToBlock(bookmark?.blockId || '');
        }

        if (normalized === 'insertbookmark' || normalized === 'addbookmark') {
            return insertBookmark(argument);
        }

        if (normalized === 'inserttableofcontents' || normalized === 'inserttoc') {
            return executeTocCommand('insertTableOfContents', argument, insertTableOfContents);
        }

        if (normalized === 'updatetableofcontents' || normalized === 'updatetoc') {
            return executeTocCommand('updateTableOfContents', argument, updateTableOfContents);
        }

        return { changed: false, commandId: normalized };
    }

    function executeReplaceCurrent(argument) {
        const active = searchState.matches[searchState.activeIndex] || null;
        if (!active) {
            return { changed: false, commandId: 'replacecurrent', search: clone(searchState) };
        }

        const before = captureSnapshot();
        const result = replaceCanvasMatch(getModel(), getSelection(), active, argument?.replacement ?? argument?.text ?? argument ?? '');
        revision += 1;
        lastCommand = { id: 'replacecurrent', changed: result.changed === true, revision };
        if (result.changed) {
            const after = {
                model: result.model,
                selection: result.selection,
                formatState: createInlineFormatState(formatState),
                paragraphState: createParagraphCommandState(paragraphState),
            };
            pushHistory({
                id: `canvas-find-replace-${revision}`,
                kind: 'find-replace',
                commandId: 'replacecurrent',
                before,
                after,
            });
            commit({ model: result.model, selection: result.selection, result, command: lastCommand });
            refreshSearchAfterReplace();
        }

        return {
            changed: result.changed === true,
            commandId: 'replacecurrent',
            dirtyBlockIds: result.dirtyBlockIds || [],
            search: clone(searchState),
        };
    }

    function executeReplaceAll(argument) {
        const query = argument?.query || searchState.query;
        const options = argument?.options || searchState.options;
        const replacement = argument?.replacement ?? argument?.text ?? '';
        const matches = query ? findCanvasMatches(getModel(), { query, options }) : searchState.matches;
        if (matches.length === 0) {
            return { changed: false, commandId: 'replaceall', replaceCount: 0 };
        }

        const before = captureSnapshot();
        const result = replaceAllCanvasMatches(getModel(), getSelection(), matches, replacement);
        revision += 1;
        lastCommand = { id: 'replaceall', changed: result.changed === true, revision };
        if (result.changed) {
            const after = {
                model: result.model,
                selection: result.selection,
                formatState: createInlineFormatState(formatState),
                paragraphState: createParagraphCommandState(paragraphState),
            };
            pushHistory({
                id: `canvas-find-replace-all-${revision}`,
                kind: 'find-replace',
                commandId: 'replaceall',
                before,
                after,
            });
            commit({ model: result.model, selection: result.selection, result, command: lastCommand });
            searchState = createSearchState(getModel());
        }

        return {
            changed: result.changed === true,
            commandId: 'replaceall',
            replaceCount: result.replaceCount || 0,
            dirtyBlockIds: result.dirtyBlockIds || [],
            search: clone(searchState),
        };
    }

    function executeTocCommand(commandId, argument, operation) {
        const before = captureSnapshot();
        const result = operation(getModel(), getSelection(), getLayout(), argument || {});
        revision += 1;
        lastCommand = { id: normalizeCommandId(commandId), changed: result.changed === true, revision };
        if (result.changed) {
            const after = {
                model: result.model,
                selection: result.selection || before.selection,
                formatState: createInlineFormatState(formatState),
                paragraphState: createParagraphCommandState(paragraphState),
            };
            pushHistory({
                id: `canvas-toc-${revision}`,
                kind: 'table-of-contents',
                commandId: normalizeCommandId(commandId),
                before,
                after,
            });
            commit({ model: result.model, selection: result.selection || before.selection, result, command: lastCommand });
        }

        return {
            changed: result.changed === true,
            commandId: normalizeCommandId(commandId),
            operation: result.operation || commandId,
            entryCount: result.entryCount || 0,
            dirtyBlockIds: result.dirtyBlockIds || [],
        };
    }

    function navigateToBlock(blockId) {
        const id = String(blockId || '');
        if (!id) {
            return { changed: false, selectionChanged: false };
        }

        const outline = extractCanvasOutline(getModel(), getLayout());
        const target = findOutlineTarget(outline, id) || { blockId: id };
        const selection = {
            anchor: { blockId: target.blockId, offset: 0 },
            focus: { blockId: target.blockId, offset: 0 },
        };
        revision += 1;
        lastCommand = { id: 'gotoheading', changed: false, selectionChanged: true, revision };
        commit({
            model: getModel(),
            selection,
            result: { changed: false, selectionChanged: true, target },
            command: lastCommand,
        });
        return { changed: false, selectionChanged: true, target };
    }

    function setSelectionCommand(argument) {
        const requested = argument?.selection || argument?.Selection || argument || {};
        const selection = normalizeExplicitSelection(requested);
        if (!selection) {
            return { changed: false, selectionChanged: false };
        }

        revision += 1;
        lastCommand = { id: 'setselection', changed: false, selectionChanged: true, revision };
        commit({
            model: getModel(),
            selection,
            result: { changed: false, selectionChanged: true },
            command: lastCommand,
        });
        return { changed: false, selectionChanged: true, selection };
    }

    function insertBookmark(argument) {
        const name = String(argument?.name || argument || '').trim();
        const selection = explicitSelection(argument) || getSelection();
        if (!name || !selection?.focus?.blockId) {
            return { changed: false, bookmarkCount: listBookmarks(getModel()).length };
        }

        const before = captureSnapshot();
        const result = applyBookmarkToSelection(getModel(), selection, name, argument || {});
        if (!result.changed) {
            return { changed: false, bookmarkCount: listBookmarks(getModel()).length };
        }

        revision += 1;
        lastCommand = { id: 'insertbookmark', changed: true, revision };
        const after = {
            model: result.model,
            selection: result.selection,
            formatState: createInlineFormatState(formatState),
            paragraphState: createParagraphCommandState(paragraphState),
        };
        pushHistory({
            id: `canvas-bookmark-${revision}`,
            kind: 'bookmark',
            commandId: 'insertbookmark',
            before,
            after,
        });
        commit({ model: result.model, selection: result.selection, result, command: lastCommand });
        return { changed: true, bookmarkCount: listBookmarks(result.model).length, bookmark: result.bookmark };
    }

    function explicitSelection(argument) {
        const blockId = String(argument?.blockId || argument?.targetBlockId || '');
        if (!blockId) {
            return null;
        }

        const start = Math.max(0, Number(argument?.start ?? argument?.startOffset ?? 0) || 0);
        const end = Math.max(start, Number(argument?.end ?? argument?.endOffset ?? start) || start);
        return {
            anchor: { blockId, offset: start },
            focus: { blockId, offset: end },
        };
    }

    function normalizeExplicitSelection(argument) {
        const direct = explicitSelection(argument);
        if (direct) {
            return direct;
        }

        const anchorBlockId = String(argument?.anchor?.blockId || argument?.Anchor?.BlockId || '');
        const focusBlockId = String(argument?.focus?.blockId || argument?.Focus?.BlockId || anchorBlockId);
        if (!anchorBlockId || !focusBlockId) {
            return null;
        }

        const anchorOffset = Math.max(0, Number(argument?.anchor?.offset ?? argument?.Anchor?.Offset ?? 0) || 0);
        const focusOffset = Math.max(0, Number(argument?.focus?.offset ?? argument?.Focus?.Offset ?? anchorOffset) || 0);
        return {
            anchor: { blockId: anchorBlockId, offset: anchorOffset },
            focus: { blockId: focusBlockId, offset: focusOffset },
        };
    }

    function moveSearch(delta) {
        if (searchState.matches.length === 0) {
            searchState.activeIndex = 0;
            return;
        }

        const next = searchState.activeIndex + delta;
        searchState.activeIndex = (next + searchState.matches.length) % searchState.matches.length;
    }

    function selectActiveSearchMatch() {
        const match = searchState.matches[searchState.activeIndex] || null;
        if (!match) {
            return false;
        }

        commit({
            model: getModel(),
            selection: matchRange(match),
            result: { changed: false, selectionChanged: true, match },
            command: lastCommand,
        });
        return true;
    }

    function refreshSearchAfterReplace() {
        if (!searchState.query) {
            searchState = createSearchState();
            return;
        }

        searchState = createSearchState(getModel(), { query: searchState.query, options: searchState.options });
    }

    function captureSnapshot() {
        // The model is referenced, not cloned: the canvas command pipeline never mutates an existing model in
        // place (every mutator clones first) and the live model is only ever swapped out by setModel, so this
        // reference is a stable immutable "before" snapshot. History persists it via pushHistory (which opts out
        // of re-cloning), so the previous per-command full clone here was pure waste (perf phase 2.3).
        return {
            model: getModel(),
            selection: clone(getSelection()),
            formatState: createInlineFormatState(formatState),
            paragraphState: createParagraphCommandState(paragraphState),
            formatPainterState: createFormatPainterState(formatPainterState),
        };
    }

    // Pushes a transaction whose before/after snapshots are already immutable (see captureSnapshot + the fresh
    // result.model each command produces), so history need not defensively re-clone them.
    function pushHistory(transaction) {
        return history && history.push ? history.push(transaction, { cloneSnapshots: false }) : undefined;
    }

    return {
        register(commandId, handler) {
            return dispatcher.register(commandId, handler);
        },
        execute(commandId, payload) {
            return dispatcher.execute(commandId, payload);
        },
        listCommands() {
            return dispatcher.listCommands();
        },
        execCommand,
        queryCommand,
        queryCommandState,
        getPendingMarks,
        getPendingMarkOverrides,
        getSearchState,
        getViewState,
        openLinkAtPosition(position) {
            return openLink({ position }).opened;
        },
    };
}

function createSearchState(model = null, argument = null) {
    const query = String(argument?.query ?? argument?.text ?? '');
    const options = normalizeSearchOptions(argument?.options || argument || {});
    const matches = query ? findCanvasMatches(model, { query, options }) : [];
    return {
        query,
        options,
        matches,
        activeIndex: matches.length > 0 ? 0 : 0,
        matchCount: matches.length,
    };
}

function allBlockIds(model) {
    return (model?.body?.blocks || []).map(block => String(block.id || '')).filter(Boolean);
}

function findBlock(model, blockId) {
    const id = String(blockId || '');
    const stack = [...(model?.body?.blocks || [])];
    while (stack.length > 0) {
        const block = stack.shift();
        if (String(block?.id || '') === id) {
            return block;
        }

        if (String(block?.type || '').toLowerCase() === 'table') {
            for (const row of block?.content?.table?.rows || []) {
                for (const cell of row?.cells || []) {
                    stack.push(...(cell?.blocks || []));
                }
            }
        }
    }

    return null;
}

function normalizeCommandId(commandId) {
    return String(commandId == null ? '' : commandId).replace(/[\s_-]/g, '').toLowerCase();
}

function commandStateKey(commandId) {
    const canonical = canonicalCommandId(commandId);
    if (canonical === 'lineSpacing') return 'lineSpacing';
    if (canonical === 'spacingBefore') return 'spacingBefore';
    if (canonical === 'spacingAfter') return 'spacingAfter';
    if (canonical === 'increaseIndent') return 'increaseIndent';
    if (canonical === 'decreaseIndent') return 'decreaseIndent';
    if (canonical === 'bulletList') return 'bulletList';
    if (canonical === 'numberedList') return 'numberedList';
    if (canonical === 'blockStyle') return 'blockStyle';
    if (canonical === 'quoteStyle') return 'quoteStyle';
    if (canonical === 'showRuler') return 'showRuler';
    if (canonical === 'showBlocks') return 'showBlocks';
    if (canonical === 'toggleNonPrintingCharacters') return 'toggleNonPrintingCharacters';
    return commandId;
}

function requiredFunction(value, message) {
    if (typeof value !== 'function') {
        throw new Error(message);
    }

    return value;
}

function unique(values) {
    return Array.from(new Set((values || []).map(value => String(value || '')).filter(Boolean)));
}

function clone(value) {
    if (typeof structuredClone === 'function') {
        return structuredClone(value);
    }

    return JSON.parse(JSON.stringify(value ?? null));
}
