using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DocumentEditor.Features;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;

namespace Tempo.Blazor.Components.DocumentEditor;

// Redesign 146c7e1b, phase 6 (DE-2.2 .. DE-2.5): the contextual inspector (an image, a table or find replaces the tab bar of the desktop
// inspector), the anchor text of an image, the default preview of a revision, and where find lives on each layout.
public partial class TmDocumentEditor
{
    private const int AnchorTextLength = 40;
    private const int RevisionPreviewLength = 160;

    // The floating find panel hangs under the ribbon (always on screen - it is sticky); without a ribbon the editor root is the anchor.
    private ElementReference FindPanelAnchor => _toolbar is { } toolbar && toolbar.RibbonAnchor.Id is not null ? toolbar.RibbonAnchor : _rootElement;

    // The context the reader dismissed with the back arrow or the X, keyed by the object it was dismissed for. A different object, or no
    // object, starts afresh; the tab the reader stands on is never changed by a context coming and going.
    private string? _inspectorContextDismissedKey;
    private bool _findOpenedTheSidePanel;
    private InspectorContext? _inspectorContext;

    private sealed record InspectorContext(string Kind, string Key, string Title, string Icon);

    // The canvas reports an EMPTY selection whenever it loses focus - which is what clicking a field of the inspector does, and what every
    // panel edit (it hands the focus back to the canvas) briefly causes. The table inspector must not vanish after its first edit, so the
    // last table cell the reader stood in is remembered until the reader selects something else in the document.
    private string? _stickyTableCellId;

    private string? EffectiveActiveTableCellId =>
        _selectionContext.ActiveTableCellId ?? _selection.ActiveTableCellId ?? _stickyTableCellId;

    private void TrackStickyTableCell(WysiwygSelectionSnapshot snapshot)
        => TrackStickyTableCell(
            snapshot.ActiveTableCellId,
            snapshot.AnchorBlockId,
            snapshot.FocusBlockId,
            SelectionTargetsDocumentContent(snapshot));

    private void TrackStickyTableCell(string? cellId, string? anchorBlockId, string? focusBlockId, bool targetsDocumentContent)
    {
        if (!string.IsNullOrWhiteSpace(cellId))
        {
            _stickyTableCellId = cellId;
        }
        else if (targetsDocumentContent && !SelectionIsInsideStickyTable(anchorBlockId, focusBlockId))
        {
            // The reader selected something else in the document: the table is left for good (and so is its dismissal).
            _stickyTableCellId = null;
            ForgetDismissedContext("table:");
        }
    }

    // A dismissal belongs to ONE object: when the selection moves to another object (or off every object) the dismissal is forgotten, so
    // coming back to the first one opens its context again.
    private void ForgetDismissedContext(string kindPrefix)
    {
        if (_inspectorContextDismissedKey?.StartsWith(kindPrefix, StringComparison.Ordinal) == true)
        {
            _inspectorContextDismissedKey = null;
        }
    }

    /// <summary>
    /// The document under the selection was swapped (loaded, imported, a version previewed / returned from / restored): whatever the inspector
    /// remembered about the old selection - the sticky table cell, a dismissed context, the selection context - belongs to a document that is
    /// gone. NOT called for the engine's empty selection report (a blur) - that is exactly what the sticky cell survives.
    /// </summary>
    private void ResetInspectorSelectionMemory()
    {
        _stickyTableCellId = null;
        _inspectorContextDismissedKey = null;
        _selectionContext = DocumentEditorSelectionContext.Empty;
        // The selected image belongs to the old document too: an object id may repeat in the next one, and "the same object" must not
        // keep a freshly loaded document's image from opening the inspector.
        _activeCanvasImageObjectId = null;
        _activeCanvasImageBlockId = null;
        _activeCanvasImageRunId = null;
        _canvasActiveImage = null;
    }

    // DE-INSPECTOR-OPENS-ON-NEW-OR-DELIBERATE-SELECTION: the inspector opens for a NEW object (SyncCanvasActiveImage) or for a deliberate
    // selection of the one already selected - the engine stamps an object selection report "pointer" (a click on the object that did not
    // drag) or "keyboard" (object cycling); every other report ("layout": a re-layout, a drag preview, the state re-render) is a re-report
    // that never reopens a panel the reader closed. An empty report or a caret move never does either.
    private void NoteCanvasSelectionReport(WysiwygMiniToolbarRequest? request)
    {
        if (!string.Equals(request?.Reason, "canvas-object-selection", StringComparison.Ordinal)
            || !IsDeliberateObjectSelection(request))
        {
            return;
        }

        ForgetDismissedContext("image:");
        if (HasImageInspector && !IsMobileLayout && (!_sidePanelOpen || _activeSidePanelTab != DocumentSidePanelTab.Properties))
        {
            // A closed panel opens, and an open one standing on another tab (comments, revisions ...) moves to Properties: the reader asked
            // for THIS object. The inspector context of a desktop dock replaces the tabs anyway; the tab is what returns after its X.
            OpenSidePanel(DocumentSidePanelTab.Properties);
        }
    }

    internal static bool IsDeliberateObjectSelection(WysiwygMiniToolbarRequest? request) =>
        string.Equals(request?.ObjectSelectionSource, "pointer", StringComparison.Ordinal)
        || string.Equals(request?.ObjectSelectionSource, "keyboard", StringComparison.Ordinal);

    // A panel edit re-lays the table out and the engine may then report the caret in a cell paragraph WITHOUT the cell id: that is still the
    // same table, not a decision to leave it.
    private bool SelectionIsInsideStickyTable(string? anchorBlockId, string? focusBlockId)
    {
        if (string.IsNullOrWhiteSpace(_stickyTableCellId))
        {
            return false;
        }

        var table = DisplayedDocument?.Blocks
            .Select(block => block.Content as TableBlockContent)
            .FirstOrDefault(content => content is not null && TableContainsCell(content, _stickyTableCellId));
        if (table is null)
        {
            return false;
        }

        var cellBlocks = table.Rows.SelectMany(row => row.Cells).SelectMany(cell => cell.Blocks).ToList();
        return FindBlock(cellBlocks, anchorBlockId) is not null
            || FindBlock(cellBlocks, focusBlockId) is not null;
    }

    private bool HasImageInspector =>
        (UsingCanvasEngine && _canvasActiveImage is not null) || ActiveImageContent is not null;

    // On a desktop layout the object inspector is the context; once the reader dismissed it, the Properties tab shows it instead (a dismissed
    // inspector must not leave the selected object without one). Tablet and phone never have a context, so their tab always shows it.
    private bool ShowImageInspectorInTab => HasImageInspector && (IsMobileLayout || _inspectorContext is null);

    private bool ShowTableInspectorInTab => HasTableInspector && (IsMobileLayout || _inspectorContext is null);

    private bool HasTableInspector => ActiveTableContent is not null && ActiveTableCell is not null;

    /// <summary>
    /// The contextual inspector of the current selection, or null. A desktop dock and a tablet side sheet have one; a phone keeps the tabs
    /// (its object inspector is a short bottom sheet of its own, see ContextSheetKind). Find wins over an object - it is a deliberate request - and a dismissed context stays dismissed for the same object.
    /// </summary>
    private InspectorContext? ResolveInspectorContext()
    {
        if (IsMobileLayout)
        {
            return null;
        }

        if (_findPanelOpen)
        {
            return new InspectorContext("find", "find", Loc["TmDocumentEditor_FindPanelTitle"], "search");
        }

        InspectorContext? context = null;
        if (UsingCanvasEngine && _canvasActiveImage is not null)
        {
            context = new InspectorContext("image", "image:" + _activeCanvasImageObjectId, Loc["TmDocumentEditor_ImageTools"], "image");
        }
        else if (ActiveImageContent is not null)
        {
            context = new InspectorContext("image", "image:" + ActiveImageInspectorObjectId, Loc["TmDocumentEditor_ImageTools"], "image");
        }
        else if (HasTableInspector)
        {
            context = new InspectorContext("table", "table:" + ActiveTableBlock?.Id, Loc["TmDocumentEditor_TableInspector"], "table");
        }

        // Pure: it only reads. A dismissal is forgotten where the selection changes (SyncCanvasActiveImage, TrackStickyTableCell), never here.
        if (context is null)
        {
            return null;
        }

        return string.Equals(context.Key, _inspectorContextDismissedKey, StringComparison.Ordinal) ? null : context;
    }

    // Find has no tabs to go back to - it simply closes - so its header carries the X only (no back arrow is rendered without a handler).
    private EventCallback InspectorBackCallback => _inspectorContext?.Kind == "find"
        ? default
        : EventCallback.Factory.Create(this, BackFromInspectorContextAsync);

    private RenderFragment? InspectorContextContent => _inspectorContext?.Kind switch
    {
        "image" => ImageInspector(false),
        "table" => TableInspector(false),
        "find" => FindInspector,
        _ => null
    };

    // The back arrow returns to the tabs; the X closes the context. For an object both dismiss the context for as long as that object stays
    // selected (focus goes back to the tab or to the document); for find both close find - it has no other state to return to.
    private async Task BackFromInspectorContextAsync()
    {
        if (_inspectorContext?.Kind == "find")
        {
            await CloseFindPanelAsync();
            return;
        }

        _inspectorContextDismissedKey = _inspectorContext?.Key;
        _focusSidePanelOnRender = true;
        await InvokeAsync(StateHasChanged);
    }

    private async Task CloseInspectorContextAsync()
    {
        if (_inspectorContext?.Kind == "find")
        {
            await CloseFindPanelAsync();
            return;
        }

        _inspectorContextDismissedKey = _inspectorContext?.Key;
        _focusDocumentOnRender = true;
        await InvokeAsync(StateHasChanged);
    }

    private async Task FocusFindPanelAsync()
    {
        if (_findPanel is not null)
        {
            await _findPanel.FocusAsync();
        }
    }

    // ── Image anchor ───────────────────────────────────────────────────────

    /// <summary>The first ~40 characters of the paragraph the active image is anchored to, so the inspector names it instead of printing a block id.</summary>
    private string? ActiveImageAnchorText
    {
        get
        {
            var blockId = ActiveImageCommandBlockId ?? ActiveImageInspectorBlockId;
            var block = FindBlock(DisplayedDocument?.Blocks, blockId);
            if (block is null)
            {
                return null;
            }

            var text = DocumentTextDiffHelper.ExtractBlockPlainText(block).Trim();
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            return text.Length <= AnchorTextLength ? text : text[..AnchorTextLength].TrimEnd() + "…";
        }
    }

    private static DocumentBlock? FindBlock(IEnumerable<DocumentBlock>? blocks, string? blockId)
    {
        if (blocks is null || string.IsNullOrWhiteSpace(blockId))
        {
            return null;
        }

        foreach (var block in blocks)
        {
            if (string.Equals(block.Id, blockId, StringComparison.Ordinal))
            {
                return block;
            }

            var nested = block.Content switch
            {
                TableBlockContent table => FindBlock(table.Rows.SelectMany(row => row.Cells).SelectMany(cell => cell.Blocks), blockId),
                ContentControlBlockContent control => FindBlock(control.Blocks, blockId),
                _ => null
            };
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    // ── Revision preview ───────────────────────────────────────────────────

    /// <summary>
    /// The default preview resolver of the revision panel: the text a revision touches, read from the document by the revision's range when
    /// the revision itself carries none. The canvas engine's revisions name a block and offsets; the text lives in the document. What it
    /// cannot read it leaves to the panel (the revision's own plain-text payload, a description of a formatting change).
    /// </summary>
    private DocumentRevisionPreview? ResolveRevisionPreview(DocumentRevision revision)
    {
        if (revision.Type is DocumentRevisionType.Insertion or DocumentRevisionType.Deletion
            && !string.IsNullOrWhiteSpace(revision.PayloadJson)
            && !LooksLikeJson(revision.PayloadJson))
        {
            return null;
        }

        var text = ReadRevisionRangeText(revision.Range);
        return string.IsNullOrWhiteSpace(text) ? null : new DocumentRevisionPreview { Text = text };
    }

    private static bool LooksLikeJson(string payload)
    {
        var trimmed = payload.AsSpan().Trim();
        return trimmed.Length >= 2 && (trimmed[0] == '{' || trimmed[0] == '[');
    }

    private string? ReadRevisionRangeText(DocumentRevisionRange range)
    {
        var block = FindBlock(DisplayedDocument?.Blocks, range.BlockId);
        if (block is null)
        {
            return null;
        }

        var text = GetBlockText(block);
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (range.StartOffset is { } start && range.EndOffset is { } end && start >= 0 && end > start && start < text.Length)
        {
            text = text.Substring(start, Math.Min(end, text.Length) - start);
        }

        text = text.Trim();
        return text.Length <= RevisionPreviewLength ? text : text[..RevisionPreviewLength].TrimEnd() + "…";
    }
}
