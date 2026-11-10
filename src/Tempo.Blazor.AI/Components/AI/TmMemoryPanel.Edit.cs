using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The editing half of <see cref="TmMemoryPanel"/> (plan 7ba95ed6, Fáze 5), split out of the component file so neither
/// file grows past the source-size budget.
/// <para>
/// It owns the inline editor: which row is open, the three drafts (text, category, source), the fields
/// <see cref="TmMemoryPanel.EditableFields"/> actually allows, and the ONE commit that turns them into a
/// <see cref="MemoryEdit"/>. The editor is a single-row modal state: opening another row's editor discards the first
/// draft (never a save), Escape rolls the whole draft back and hands the focus to the row's Edit button, and a save hands
/// it to the ROW — a focused button would be activated again by the Enter key sequence still in flight.
/// </para>
/// </summary>
public partial class TmMemoryPanel
{
    // ── Inline editing ───────────────────────────────────────────────────────

    private Task BeginEditAsync(MemoryItem item)
    {
        if (!_enabled || !CanEdit || _editingId == item.Id)
        {
            return Task.CompletedTask;
        }

        // Only ONE row edits at a time: opening another draft discards the previous one, which is never a save.
        _editingId = item.Id;
        _draft = item.Text;
        _draftCategory = item.Category;
        _draftSource = item.Source ?? string.Empty;
        _focusEditBoxAfterRender = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task OnDraftInputAsync(ChangeEventArgs args)
    {
        _draft = args.Value?.ToString() ?? string.Empty;
        return Task.CompletedTask;
    }

    private Task OnDraftCategoryInputAsync(ChangeEventArgs args)
    {
        _draftCategory = args.Value?.ToString() ?? string.Empty;
        return Task.CompletedTask;
    }

    private Task OnDraftSourceInputAsync(ChangeEventArgs args)
    {
        _draftSource = args.Value?.ToString() ?? string.Empty;
        return Task.CompletedTask;
    }

    private async Task OnEditKeyDownAsync(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "Enter" when !args.ShiftKey:
                await CommitEditAsync();
                break;
            case "Escape":
                CancelEdit();
                break;
        }
    }

    /// <summary>
    /// Commits the draft: a commit that moved no editable field closes the editor without reporting a second time.
    /// <para>
    /// The values the reader could NOT change are taken from the row, so a field the host owns can never be reported as
    /// cleared by an editor that never showed it. A host with <see cref="OnEditItem"/> receives ONE record naming the
    /// fields that moved; a host on the legacy <see cref="OnEdit"/> receives the edited item, exactly as before, and only
    /// for a TEXT change.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the change has been reported to the host.</returns>
    public async Task CommitEditAsync()
    {
        if (_editingId is not { } itemId)
        {
            return;
        }

        var item = _items.FirstOrDefault(candidate => candidate.Id == itemId);
        var text = item is null || CanEditText ? _draft : item.Text;
        var category = item is null || CanEditCategory ? _draftCategory : item.Category;
        var source = item is null || CanEditSource ? _draftSource : item.Source;

        // A SAVE does not queue the Edit button as the focus target: the saved row takes the focus (see CloseEditor).
        CloseEditor(itemId, returnFocusToEditButton: false);

        if (item is null)
        {
            StateHasChanged();
            return;
        }

        var changed = MemoryEditableFields.None;
        if (CanEditText && !string.Equals(item.Text, text, StringComparison.Ordinal))
        {
            changed |= MemoryEditableFields.Text;
        }

        if (CanEditCategory && !string.Equals(item.Category, category, StringComparison.Ordinal))
        {
            changed |= MemoryEditableFields.Category;
        }

        if (CanEditSource && !string.Equals(item.Source, source, StringComparison.Ordinal))
        {
            changed |= MemoryEditableFields.Source;
        }

        if (changed == MemoryEditableFields.None)
        {
            StateHasChanged();
            return;
        }

        var edited = item with { Text = text, Category = category, Source = source };
        var edit = new MemoryEdit(itemId, text, category, source, changed);

        // The save runs its OWN busy/error cycle instead of the shared row-action plumbing: a commit that happens while
        // another request holds the row must still be attempted (the reader pressed Enter on their own draft), and only a
        // real FAILURE reopens the editor.
        _busy.Add(itemId);
        _error = null;
        StateHasChanged();

        var saved = false;
        try
        {
            if (OnEditItem.HasDelegate)
            {
                await OnEditItem.InvokeAsync(edit);
            }
            else if ((changed & MemoryEditableFields.Text) != 0 && OnEdit.HasDelegate)
            {
                await OnEdit.InvokeAsync(edited);
            }

            _overrides[itemId] = edited;
            MergeItems();
            Announce(Loc["TmMemoryPanel_AnnounceEdited"]);
            _pendingRowFocusId = itemId;
            saved = true;
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
        finally
        {
            _busy.Remove(itemId);
            StateHasChanged();
        }

        if (!saved)
        {
            // A failed save must not throw the reader's work away: the draft comes back in the editor, with the error
            // already printed beside the row.
            _editingId = itemId;
            _draft = text;
            _draftCategory = category;
            _draftSource = source ?? string.Empty;
            _focusEditBoxAfterRender = true;
            StateHasChanged();
        }
    }

    /// <summary>Closes the editor without reporting anything and hands the focus back to the Edit button.</summary>
    public void CancelEdit()
    {
        if (_editingId is not { } itemId)
        {
            return;
        }

        CloseEditor(itemId);
        StateHasChanged();
    }

    /// <summary>
    /// Closes the editor. A CANCEL queues the row's Edit button as the focus target; a SAVE queues the ROW instead, because
    /// the browser still has the Enter key sequence in flight and would ACTIVATE a button that takes the focus before the
    /// keypress is dispatched — the click would reopen the editor the reader just closed.
    /// </summary>
    private void CloseEditor(string itemId, bool returnFocusToEditButton = true)
    {
        _editingId = null;
        _draft = string.Empty;
        _draftCategory = string.Empty;
        _draftSource = string.Empty;

        if (returnFocusToEditButton)
        {
            _pendingEditButtonId = itemId;
        }
    }
}
