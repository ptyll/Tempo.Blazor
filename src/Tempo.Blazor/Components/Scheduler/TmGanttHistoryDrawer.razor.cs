using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Abstractions.WorkItems;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// Drawer showing the Gantt audit log (the shared <see cref="TmGanttHistoryList"/>) with time-travel and a confirmed
/// rollback. It sits on the right edge of the Gantt component (<see cref="Placement"/> restores the viewport edge);
/// Escape closes it.
/// </summary>
public partial class TmGanttHistoryDrawer
{
    private ElementReference _drawer;
    private bool _wasOpen;
    private TmActivityEntry? _pendingRollback;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>Activity history entries to display.</summary>
    [Parameter] public IReadOnlyList<TmActivityEntry>? History { get; set; }

    /// <summary>Tasks the entries refer to: resolves the task titles shown (and linked) in each entry.</summary>
    [Parameter] public IReadOnlyList<TmWorkItem> Tasks { get; set; } = [];

    /// <summary>Whether the drawer is visible.</summary>
    [Parameter] public bool IsOpen { get; set; }

    /// <summary>What the drawer is positioned against: the Gantt component (default) or the viewport.</summary>
    [Parameter] public GanttHistoryDrawerPlacement Placement { get; set; } = GanttHistoryDrawerPlacement.Component;

    /// <summary>Fires when the user requests time-travel to a specific timestamp.</summary>
    [Parameter] public EventCallback<DateTime> OnTimeTravelRequested { get; set; }

    /// <summary>Fires when the user confirms the rollback of a specific history entry (the drawer asks first).</summary>
    [Parameter] public EventCallback<TmActivityEntry> OnRollbackRequested { get; set; }

    /// <summary>Fires when the user clicks the task of an entry (arg = task id).</summary>
    [Parameter] public EventCallback<string> OnTaskSelected { get; set; }

    /// <summary>Fires when the drawer should close (close button or Escape).</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>Zone the entry timestamps are shown in. Defaults to the machine's local zone (<see cref="TimeZoneInfo.Local"/>); <see cref="TmGantt"/> passes the user's display zone.</summary>
    [Parameter] public TimeZoneInfo? TimeZone { get; set; }

    private string DrawerClass => Placement == GanttHistoryDrawerPlacement.Viewport
        ? "tm-gantt__history-drawer tm-gantt__history-drawer--viewport"
        : "tm-gantt__history-drawer";

    private string RollbackMessage => _pendingRollback is null
        ? string.Empty
        : Loc["TmGantt_RollbackConfirmMessage",
            Tasks.FirstOrDefault(t => t.Id == _pendingRollback.EntityRef.EntityId)?.Title ?? _pendingRollback.EntityRef.EntityId];

    private Task RequestRollbackAsync(TmActivityEntry entry)
    {
        _pendingRollback = entry;
        return Task.CompletedTask;
    }

    private async Task ConfirmRollbackAsync(bool? confirmed)
    {
        var entry = _pendingRollback;
        _pendingRollback = null;
        if (confirmed == true && entry is not null)
        {
            await OnRollbackRequested.InvokeAsync(entry);
        }
    }

    private async Task OnKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            await CloseAsync();
        }
    }

    /// <summary>Close button / Escape: focus goes back to the control that opened the drawer first (the drawer unmounts with the close).</summary>
    private async Task CloseAsync()
    {
        await InvokeJsAsync("tmGantt.restoreOpener");
        await OnClose.InvokeAsync();
    }

    private async Task InvokeJsAsync(string identifier)
    {
        try
        {
            await JS.InvokeVoidAsync(identifier, _drawer);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or InvalidOperationException or JSException)
        {
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // A drawer that just opened takes focus, so Escape (and the keyboard) work without a click first.
        if (IsOpen && !_wasOpen)
        {
            // Remember who had focus (the History button that opened it) before the drawer takes it.
            await InvokeJsAsync("tmGantt.noteOpener");
            try
            {
                await _drawer.FocusAsync();
            }
            catch (Exception ex) when (ex is JSDisconnectedException or InvalidOperationException or JSException)
            {
            }
        }

        _wasOpen = IsOpen;
    }
}