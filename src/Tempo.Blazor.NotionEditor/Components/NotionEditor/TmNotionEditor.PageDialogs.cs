using Microsoft.AspNetCore.Components;
using Tempo.Blazor.NotionEditor.Enums;

namespace Tempo.Blazor.Components.NotionEditor;

/// <summary>
/// The editor-owned page dialogs (share, restrictions, history). They render beside the shortcuts
/// panel, outside the shell canvas, so a band flip (NOTION-EDITOR-SHELL-CANVAS-REMOUNT) cannot
/// dispose them while open. Split out of TmNotionEditor.razor.cs to stay under the source-file
/// length budget (N270/F13).
/// </summary>
public partial class TmNotionEditor
{
    private bool                       _shareDialogOpen;
    private bool                       _restrictionsDialogOpen;
    private bool                       _historyDialogOpen;
    private Guid?                      _pageDialogPageId;
    private Page.TmNotionPage?         _pageComponent;

    private Task OnShareVisibleChanged(bool visible)
    {
        _shareDialogOpen = visible;
        return Task.CompletedTask;
    }

    /// <summary>
    /// The page asks the editor to open a dialog. The dialog renders beside the shortcuts panel,
    /// outside the shell canvas, so a band flip (NOTION-EDITOR-SHELL-CANVAS-REMOUNT) cannot
    /// dispose it while it is open.
    /// </summary>
    private void OpenPageDialogFromPage(NotionPageDialog dialog, Guid pageId)
    {
        _pageDialogPageId = pageId;
        if (dialog == NotionPageDialog.Share)
            _shareDialogOpen = true;
        else if (dialog == NotionPageDialog.Restrictions)
            _restrictionsDialogOpen = true;
        else if (dialog == NotionPageDialog.History)
            _historyDialogOpen = true;
        _ = InvokeAsync(StateHasChanged);
    }

    private Task OnHistoryVisibleChanged()
    {
        _historyDialogOpen = false;
        return Task.CompletedTask;
    }

    /// <summary>
    /// The history dialog restored a version: the session snapshot is stale server-side, so reload
    /// it and refresh the open page (same work the page did when it owned the dialog).
    /// </summary>
    private async Task OnHistoryRestoredAsync(string pageId)
    {
        _historyDialogOpen = false;
        if (_aggregateSession is { } session)
            await session.ReloadAsync();
        if (_pageComponent is not null)
            await _pageComponent.RefreshAsync();
        StateHasChanged();
    }

    private Task OnRestrictionsVisibleChanged(bool visible)
    {
        _restrictionsDialogOpen = visible;
        return Task.CompletedTask;
    }

    private Task OnRestrictionsSavedAsync() => OnPagePermissionsChangedAsync();
}
