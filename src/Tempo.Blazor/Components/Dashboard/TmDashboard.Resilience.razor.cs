using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Icons;

namespace Tempo.Blazor.Components.Dashboard;

/// <summary>
/// Partial of <see cref="TmDashboard"/> (F7b): the edit-mode action-bar items (F5) and the
/// resilience members — the provider-failure alert state (UX §5.8) and the mobile rename
/// dialog. Kept beside <c>TmDashboard.razor.cs</c> so the code-behind stays under the
/// N270/F13 1200-line source ceiling.
/// </summary>
public partial class TmDashboard
{
    private IReadOnlyList<TmActionItem> _editModeActions = [];

    // F7b (UX §5.8): provider failures no longer crash the host app. The load path (the list,
    // the default dashboard, a switcher selection) and the save path each surface a localized
    // TmAlert above the grid with a "Try again" action that re-runs the failed operation.
    private bool _loadError;
    private bool _saveError;

    // F7b: the compact (mobile) edit mode reaches rename through the action bar — the desktop
    // toolbar's inline editor is hidden there, so the dialog form carries the Enter-to-apply
    // contract instead (docs/keyboard-activation-convention.md).
    private bool _showMobileRenameDialog;
    private readonly string _mobileRenameInputId = $"tm-dashboard-mobile-rename-{Guid.NewGuid():N}";

    // X14: the edit actions are rebuilt per parameter set (not once in OnInitialized), so a
    // culture change re-renders the labels in the new language.
    protected override void OnParametersSet() => BuildEditModeActions();

    private void BuildEditModeActions()
    {
        // The mobile edit-mode action bar (F5) renders the same actions as the desktop
        // toolbar. The EventCallback receiver is the dashboard, so its own markup re-renders on
        // activation; Priority is only an overflow rank — the bar renders in Items order.
        _editModeActions =
        [
            new TmActionItem
            {
                Id = "add-widget",
                Label = Loc["TmDashboard_AddWidget"],
                Icon = IconNames.Plus,
                Priority = 100,
                OnClick = EventCallback.Factory.Create(this, OpenWidgetSelector),
            },
            new TmActionItem
            {
                Id = "save-layout",
                // Y11: the bar tile gets the SHORT mobile label — "Enregistrer les modifications"
                // clamped illegibly at 320 fr (review round 2, m2). The desktop toolbar keeps the
                // full wording.
                Label = Loc["TmDashboard_SaveChangesShort"],
                Icon = IconNames.Save,
                Priority = 90,
                OnClick = EventCallback.Factory.Create(this, SaveLayout),
            },
            new TmActionItem
            {
                Id = "rename-dashboard",
                // F7b: rename must stay reachable on a mobile layout, where the desktop
                // toolbar (the inline editor's host) is hidden — the action opens the
                // rename dialog below.
                Label = Loc["TmDashboard_Rename"],
                Icon = IconNames.Edit,
                Priority = 70,
                OnClick = EventCallback.Factory.Create(this, OpenMobileRename),
            },
            new TmActionItem
            {
                Id = "cancel-edit",
                Label = Loc["TmDashboard_CancelEdit"],
                Icon = IconNames.X,
                Priority = 60,
                OnClick = EventCallback.Factory.Create(this, CancelEdit),
            },
        ];
    }

    // ── F7b: error recovery and the mobile rename dialog ─────────────

    /// <summary>"Try again" on the error alert: re-runs the failed operation (load or save).</summary>
    private async Task RetryFailedOperation()
    {
        if (_saveError)
        {
            await SaveLayout();
            return;
        }

        // The load retry re-runs the WHOLE sequence (list + dashboard/default) through the shared
        // guard: when the first load failed, _currentDashboard is null and the raw
        // LoadDefaultDashboard path would let a second provider failure escape to the host.
        await LoadAllAsync();
        StateHasChanged();
    }

    /// <summary>The mobile action bar's Rename entry: opens the rename dialog (the desktop
    /// toolbar that hosts the inline editor is hidden on a mobile layout).</summary>
    private void OpenMobileRename()
    {
        _editingName = _currentDashboard?.Name ?? "";
        _showMobileRenameDialog = true;
        StateHasChanged();
    }

    /// <summary>The rename dialog's form is the Enter-to-apply contract (implicit submission).</summary>
    private void ConfirmMobileRename()
    {
        if (_currentDashboard is not null && !string.IsNullOrWhiteSpace(_editingName))
        {
            _currentDashboard.Name = _editingName.Trim();
        }
        _showMobileRenameDialog = false;
        _editingName = "";
        StateHasChanged();
    }

    private void CancelMobileRename()
    {
        _showMobileRenameDialog = false;
        _editingName = "";
        StateHasChanged();
    }
}
