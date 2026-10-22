using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Buttons;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.EmailTemplates.Abstractions.Editing;
using Tempo.Blazor.EmailTemplates.Abstractions.Import;
using Tempo.Blazor.EmailTemplates.Abstractions.Layout;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Registry;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;
using Tempo.Blazor.EmailTemplates.Services;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.EmailTemplates.Components;

// Editing: document mutations, undo/redo, clipboard, the inspector header actions, the keyboard
// chords and the helpers they share.
public partial class TmEmailTemplateEditor
{
    private async Task AddBlockAsync(BlockDescriptor descriptor)
    {
        if (ReadOnly) return;
        var block = Registry.CreateInstance(descriptor.Type);
        ResolveTargetColumn().Blocks.Add(block);
        _selectedId = block.Id;
        await CommitAsync();
        // Mobile: the panels are covering the canvas, so the block the user just inserted has to be
        // revealed — column click-to-add inserts AFTER the selected block, which can be anywhere. The
        // Tabs presentation has no sheet to close: it switches to the CANVAS tab instead, which is the
        // only surface that can show the new block.
        await RevealCanvasAfterInsertAsync();
        _focusBlockAfterRender = block.Id;
    }

    private async Task AddSectionAsync(LayoutPreset preset)
    {
        if (ReadOnly) return;
        var section = LayoutPresets.Create(preset);
        _document.Sections.Add(section);
        _selectedId = section.Id;
        await CommitAsync();
        await RevealCanvasAfterInsertAsync();
        _focusBlockAfterRender = section.Id;
    }

    private async Task HandleDropAsync((Guid ColumnId, int Index) target)
    {
        if (ReadOnly) return;
        switch (_dragPayload)
        {
            case BlockDescriptor descriptor:
                var block = Registry.CreateInstance(descriptor.Type);
                if (_document.AddBlock(target.ColumnId, block, target.Index)) _selectedId = block.Id;
                break;
            case Guid blockId:
                _document.MoveBlock(blockId, target.ColumnId, target.Index);
                _selectedId = blockId;
                break;
            default:
                return;
        }

        _dragPayload = null;
        await CommitAsync();
    }

    private Task OnStructuralChangeAsync() => ReadOnly ? Task.CompletedTask : CommitAsync();

    private async Task ApplyImportAsync(EmailTemplateDocument imported)
    {
        if (ReadOnly) return;
        _document = imported;
        _selectedId = null;
        _showImport = false;
        await CommitAsync();
    }

    private async Task OnSelectionChangedAsync(Guid? id)
    {
        _selectedId = id;
        // A selected block is edited in the inspector, so on a phone layout the sheet follows the
        // selection (EM3) instead of leaving the user on the palette.
        if (id is not null) await ShowMobilePanelAsync(EditorShellPanel.Right);
        StateHasChanged();
    }

    /// <summary>
    /// “Go to” of a validation finding (Fáze 10 review, UX 10): the dialog closes, the target is selected and the
    /// canvas moves to it through the SAME focus path every structural change uses (a focused element is scrolled
    /// into view by the browser), and on a phone the Properties sheet follows the selection.
    /// <para>
    /// A finding whose path is the DOCUMENT has no block to jump to — its target is the e-mail settings, i.e. the
    /// inspector with no block selected. Without that arm the dialog simply closed and the user was left on a
    /// canvas with nothing selected.
    /// </para>
    /// </summary>
    private async Task OnValidationNavigateAsync(Guid? id)
    {
        _showValidation = false;

        if (id is { } target && target == _document.Id)
        {
            _selectedId = null;
            await ShowMobilePanelAsync(EditorShellPanel.Right);
            StateHasChanged();
            return;
        }

        _selectedId = id;
        _focusBlockAfterRender = id;
        await ShowMobilePanelAsync(EditorShellPanel.Right);
        StateHasChanged();
    }

    private Task OnPropertyChangeAsync() => ReadOnly ? Task.CompletedTask : CommitAsync(coalesceKey: "props");

    // ── Undo / redo ─────────────────────────────────────────────────────────────────────────

    private async Task UndoAsync()
    {
        if (ReadOnly) return;
        var state = _history.Undo();
        if (state is null) return;
        _document = state;
        await AfterHistoryAsync();
    }

    private async Task RedoAsync()
    {
        if (ReadOnly) return;
        var state = _history.Redo();
        if (state is null) return;
        _document = state;
        await AfterHistoryAsync();
    }

    private async Task AfterHistoryAsync()
    {
        _revision++;
        RecomputeDirty();
        await EmitDocumentChangedAsync();
        ScheduleAutoSave();
        StateHasChanged();
    }

    // ── Clipboard ───────────────────────────────────────────────────────────────────────────

    private void CopySelected()
    {
        if (_selectedId is Guid id && _document.FindBlock(id) is { } block) _clipboard.Copy(block);
    }

    private async Task CutSelectedAsync()
    {
        if (ReadOnly) return;
        if (_selectedId is not Guid id || _document.FindBlock(id) is not { } block) return;
        _clipboard.Copy(block);
        _document.RemoveBlock(id);
        _selectedId = null;
        await CommitAsync();
    }

    private async Task PasteAsync()
    {
        if (ReadOnly) return;
        var copy = _clipboard.Paste();
        if (copy is null) return;

        if (_selectedId is Guid id && _document.FindParentColumn(id) is { } column)
        {
            var index = IndexOf(column.Blocks, b => b.Id == id);
            column.Blocks.Insert(index + 1, copy);
        }
        else
        {
            ResolveTargetColumn().Blocks.Add(copy);
        }

        _selectedId = copy.Id;
        await CommitAsync();
    }

    // ── Inspector header actions ────────────────────────────────────────────────────────────

    /// <summary>
    /// The collapsed state the palette renders: the host's parameter while a
    /// <see cref="ToolboxCollapsedChanged"/> handler is bound (controlled), otherwise the state the palette
    /// itself reported, seeded from the parameter (uncontrolled).
    /// </summary>
    private bool ToolboxCollapsedEffective
        => ToolboxCollapsedChanged.HasDelegate ? ToolboxCollapsed : (_toolboxCollapsedInternal ?? ToolboxCollapsed);

    /// <summary>Duplicates the selected block as ONE history step (the inspector's header action).</summary>
    private async Task DuplicateSelectedAsync(Guid blockId)
    {
        if (ReadOnly) return;
        if (_document.DuplicateBlock(blockId) is not { } copy) return;
        _selectedId = copy.Id;
        await CommitAsync();
    }

    /// <summary>
    /// Deletes the selected block as ONE history step (the inspector's danger action). The deleted block took
    /// the focused Delete button with it, so focus is handed back into the canvas (a neighbouring block when
    /// one is left, the canvas root otherwise) and the polite live region names the outcome with the undo
    /// chord — a silent disappearance is indistinguishable from a crash.
    /// </summary>
    private async Task DeleteSelectedAsync(Guid blockId)
    {
        if (ReadOnly) return;
        if (_document.FindBlock(blockId) is null) return;
        var neighbour = NeighbourOf(blockId);
        _selectedId = neighbour;
        _document.RemoveBlock(blockId);
        _announcement = Loc["EmailEditor_Announce_BlockDeleted"];
        await CommitAsync();
        await FocusAfterDeleteAsync(neighbour);
    }

    /// <summary>
    /// The block that visually follows the deleted one in its column, else the one before it. Returns null
    /// when the column becomes empty (or the block lived in a non-column container).
    /// </summary>
    private Guid? NeighbourOf(Guid blockId)
    {
        var column = _document.FindParentColumn(blockId);
        if (column is null) return null;

        var index = column.Blocks.ToList().FindIndex(block => block.Id == blockId);
        if (index < 0) return null;
        if (index + 1 < column.Blocks.Count) return column.Blocks[index + 1].Id;
        return index > 0 ? column.Blocks[index - 1].Id : null;
    }

    /// <summary>
    /// Moves focus back into the canvas after a structural delete. Focus runs after the re-render so the
    /// element reference points at a live node; the canvas focuses the neighbouring block or its own root.
    /// </summary>
    private Task FocusAfterDeleteAsync(Guid? neighbour) => FocusCanvasAsync(neighbour);

    /// <summary>
    /// Moves focus into the canvas (the block when it is still rendered, the canvas root otherwise) and
    /// lets the browser scroll it into view. Used after a structural change that removed or added the
    /// focused element — focus must never fall to <c>&lt;body&gt;</c>.
    /// </summary>
    private async Task FocusCanvasAsync(Guid? id)
    {
        if (_canvas is null) return;
        try
        {
            await _canvas.FocusAsync(id);
        }
        catch (JSDisconnectedException)
        {
            // A disconnected circuit has no focus to move.
        }
        catch (TaskCanceledException)
        {
            // The render (or the navigation) was cancelled while the focus call was in flight. A
            // cancelled batch has no focus to move, and rethrowing would tear down the caller's work.
        }
    }

    private static int IndexOf<T>(IList<T> items, Predicate<T> predicate)
    {
        for (var i = 0; i < items.Count; i++)
            if (predicate(items[i]))
                return i;

        return -1;
    }

    // ── Keyboard ────────────────────────────────────────────────────────────────────────────

    private async Task HandleEditorKeyDownAsync(KeyboardEventArgs e)
    {
        if (!e.CtrlKey && !e.MetaKey) return;

        switch (e.Key)
        {
            case "s" or "S" when !e.AltKey:
                // The browser's own "save page" default is cancelled for exactly this chord by the
                // package's JS module; the save itself runs here, in the component. A read-only editor
                // has nothing to save, so the chord is a silent no-op (no error badge for a keystroke the
                // user did not aim at a broken action) — SaveAsync() still reports Failed to a host.
                if (!ReadOnly) await RequestSaveAsync(EmailSaveTrigger.Shortcut);
                break;
            case "z" or "Z" when e.ShiftKey:
                await RedoAsync();
                break;
            case "z" or "Z":
                await UndoAsync();
                break;
            case "y" or "Y":
                await RedoAsync();
                break;
            case "c" or "C":
                CopySelected();
                break;
            case "x" or "X":
                await CutSelectedAsync();
                break;
            case "v" or "V":
                await PasteAsync();
                break;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    private EmailColumn ResolveTargetColumn()
    {
        if (_selectedId is Guid selected)
        {
            var parentOfBlock = _document.FindParentColumn(selected);
            if (parentOfBlock is not null) return parentOfBlock;

            var selectedColumn = DocumentTree.AllColumns(_document).FirstOrDefault(c => c.Id == selected);
            if (selectedColumn is not null) return selectedColumn;

            var selectedSection = _document.Sections.FirstOrDefault(s => s.Id == selected);
            if (selectedSection is { Columns.Count: > 0 }) return selectedSection.Columns[0];
        }

        var lastSection = _document.Sections.LastOrDefault(s => s.Columns.Count > 0);
        if (lastSection is not null) return lastSection.Columns[0];

        var created = LayoutPresets.Create(LayoutPreset.Single);
        _document.Sections.Add(created);
        return created.Columns[0];
    }

    private Task StartBlockDragAsync(BlockDescriptor descriptor)
    {
        if (ReadOnly) return Task.CompletedTask;
        _dragPayload = descriptor;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task StartExistingBlockDragAsync(Guid blockId)
    {
        if (ReadOnly) return Task.CompletedTask;
        _dragPayload = blockId;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task CommitAsync(string? coalesceKey = null)
    {
        if (ReadOnly) return;

        _revision++;
        _document.UpdatedAt = TimeProvider.GetUtcNow().UtcDateTime;
        _history.Push(_document, coalesceKey);
        RecomputeDirty();
        await EmitDocumentChangedAsync();
        ScheduleAutoSave();
        StateHasChanged();
    }

    /// <summary>
    /// Hands the current document to the host, remembering it as the editor's own emission: a host that
    /// stores the value and passes it back is echoing, not reloading, and must not reset the undo history.
    /// </summary>
    private Task EmitDocumentChangedAsync()
    {
        _emittedDocument = _document;
        return DocumentChanged.InvokeAsync(_document);
    }
}
