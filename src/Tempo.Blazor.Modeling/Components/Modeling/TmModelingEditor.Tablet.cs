using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// The Fáze 6 tablet half of <see cref="TmModelingEditor"/> (kept beside the main code-behind to
/// respect the 1200-line source ceiling): the resolved-layout tracking, the tablet overlay rules
/// — at most ONE side panel open, enforced by the editor's own handlers so the rule holds even
/// before the shell's conflict pass — and the "Panels" tabs as the tablet visibility switch.
/// Review fix: the tablet sheets carry their OWN flags (starting closed) so entering the tablet
/// band never forces the desktop dock open, and leaving it never resets the desktop flags.
/// </summary>
public partial class TmModelingEditor
{
    // ── Resolved layout ──────────────────────────────────────────────────────

    /// <summary>The tablet band's own sheet flags: BOTH start closed (the preview is alone until
    /// the user opens a sheet — the same defaults the wireframe editor uses), and the issues dock
    /// starts collapsed. The desktop dock flags are untouched by any of it.</summary>
    private bool _tabletTreeOpen;
    private bool _tabletInspectorOpen;
    private bool _tabletIssuesCollapsed = true;

    /// <summary>Whether the editor's own container currently resolves the tablet band.</summary>
    private bool IsTablet => _resolvedLayout == TmLayoutMode.Tablet;

    /// <summary>Whether the tree drawer is open: the tablet flag on the tablet band, the desktop
    /// dock flag everywhere else (the shell's LeftOpen parameter).</summary>
    private bool TreeSheetOpen => IsTablet ? _tabletTreeOpen : _shellLeftOpen;

    /// <summary>Whether the inspector sheet is open: the tablet flag on the tablet band, the
    /// desktop dock flag everywhere else (the shell's RightOpen parameter).</summary>
    private bool InspectorSheetOpen => IsTablet ? _tabletInspectorOpen : _shellRightOpen;

    /// <summary>Whether the issues dock renders collapsed: the tablet band starts collapsed (its
    /// own flag); the desktop dock keeps the user's choice.</summary>
    private bool IssuesCollapsed => IsTablet ? _tabletIssuesCollapsed : _issuesCollapsed;

    /// <summary>Tracks the resolved layout (the seed in <see cref="OnInitialized"/> plus a forced
    /// flip in <see cref="OnParametersSetAsync"/>, the Auto editors through the outer observer —
    /// and the shell's idempotent duplicate raise). Entering the tablet band resets ITS flags
    /// (sheets closed, dock collapsed); leaving it drops them without touching the desktop dock
    /// flags, which keep whatever the user had before entering the band.</summary>
    private void ApplyResolvedLayout(TmLayoutMode mode)
    {
        if (_resolvedLayout == mode)
        {
            return;
        }

        var wasTablet = _resolvedLayout == TmLayoutMode.Tablet;
        var wasMobile = _resolvedLayout == TmLayoutMode.Mobile;
        _resolvedLayout = mode;
        if (mode == TmLayoutMode.Tablet && !wasTablet)
        {
            _tabletTreeOpen = false;
            _tabletInspectorOpen = false;
            _tabletIssuesCollapsed = true;
        }
        else if (wasTablet && mode != TmLayoutMode.Tablet)
        {
            _tabletTreeOpen = false;
            _tabletInspectorOpen = false;
        }

        // Fáze 7: entering the mobile band resets ITS surfaces — the change-view sheet is closed
        // and the Issues tab shows the whole panel expanded. Leaving mobile keeps nothing (the
        // band's state is re-seeded on the next entry, like the tablet's sheets).
        if (mode == TmLayoutMode.Mobile && !wasMobile)
        {
            _changeViewOpen = false;
            _mobileIssuesCollapsed = false;
        }
    }

    private Task OnShellResolvedLayoutChanged(TmLayoutMode mode)
    {
        ApplyResolvedLayout(mode);
        return Task.CompletedTask;
    }

    // ── One sheet at a time (the shell's Open callbacks) ─────────────────────

    private Task OnShellLeftOpenChanged(bool open)
    {
        if (IsTablet)
        {
            _tabletTreeOpen = open;
            if (open) _tabletInspectorOpen = false;
            return Task.CompletedTask;
        }

        _shellLeftOpen = open;
        return Task.CompletedTask;
    }

    private Task OnShellRightOpenChanged(bool open)
    {
        if (IsTablet)
        {
            _tabletInspectorOpen = open;
            if (open) _tabletTreeOpen = false;
            return Task.CompletedTask;
        }

        _shellRightOpen = open;
        return Task.CompletedTask;
    }

    // ── The "Panels" tabs as the tablet visibility switch ────────────────────

    /// <summary>Toggles the tree drawer (the inspector sheet yields) — the tablet Tree tab.</summary>
    private Task ToggleTreeSheetAsync()
        => ToggleSheetAsync(OnShellLeftOpenChanged, TreeSheetOpen);

    /// <summary>Toggles the inspector side sheet (the tree drawer yields) — the tablet Inspector tab.</summary>
    private Task ToggleInspectorSheetAsync()
        => ToggleSheetAsync(OnShellRightOpenChanged, InspectorSheetOpen);

    private static async Task ToggleSheetAsync(Func<bool, Task> setOpen, bool isOpen)
    {
        if (isOpen)
        {
            await setOpen(false);
        }
        else
        {
            await setOpen(true);
        }
    }

    /// <summary>The Preview tab: close every sheet — the preview alone.</summary>
    private async Task ShowPreviewOnlyAsync()
    {
        if (TreeSheetOpen) await OnShellLeftOpenChanged(false);
        if (InspectorSheetOpen) await OnShellRightOpenChanged(false);
    }

    // ── Tree filter + cached child callbacks (Fáze 6 review fix, M26) ────────

    /// <summary>
    /// The tree filter's two-way storage (Fáze 6 review fix): a plain holder the tree's
    /// <see cref="FilterTextChanged"/> callback writes into. The value is read on the NEXT
    /// editor render (as the tree's <c>FilterText</c> seed), so a tree remount — a layout flip —
    /// cannot lose the filter. The editor never re-renders for the write itself.
    /// </summary>
    private sealed class TreeFilterHolder { public string? Value; }

    private readonly TreeFilterHolder _treeFilterHolder = new();

    /// <summary>
    /// A receiver that is deliberately NOT a component (the factory rejects null): a component
    /// receiver would run its <see cref="Microsoft.AspNetCore.Components.IHandleEvent"/> path on
    /// every raise — a full editor re-render per keystroke, which blew the M26 tree-filter budget
    /// by ~170ms. The callback is also built ONCE (a per-render closure would change the tree's
    /// parameter on every editor render and re-render the 200-row tree with it).
    /// </summary>
    private static readonly object TreeFilterReceiver = new();

    private EventCallback<string?>? _treeFilterChangedCallback;

    private EventCallback<string?> TreeFilterChanged
    {
        get
        {
            if (!_treeFilterChangedCallback.HasValue)
            {
                var holder = _treeFilterHolder;
                _treeFilterChangedCallback = EventCallback.Factory.Create<string?>(TreeFilterReceiver, value =>
                {
                    holder.Value = value;
                    return Task.CompletedTask;
                });
            }

            return _treeFilterChangedCallback.Value;
        }
    }

    // ── Cached tree/preview callbacks (Fáze 6 review fix, M26) ───────────────
    // A method-group parameter (OnElementSelected="…") builds a NEW delegate on every editor
    // render; the child's parameter diff then always sees a change and re-renders the 200-row
    // tree (and the preview's editor) on every parent pass. Building each callback ONCE keeps
    // the parameters reference-stable, so a parent re-render diffs to "no change".

    private EventCallback<ModelingElementDto>? _onElementSelectedCallback;
    private EventCallback<ModelingElementDto>? _onElementDragStartedCallback;
    private EventCallback? _onElementDragEndedCallback;
    private EventCallback<ModelingElementDto>? _onInsertIntoPreviewCallback;
    private EventCallback<IReadOnlyCollection<string>>? _collapsedGroupsChangedCallback;

    /// <summary>The tree's callbacks, built once per editor instance.</summary>
    private (EventCallback<ModelingElementDto> Selected,
             EventCallback<ModelingElementDto> DragStarted,
             EventCallback DragEnded,
             EventCallback<ModelingElementDto> Insert,
             EventCallback<IReadOnlyCollection<string>> CollapsedChanged) TreeCallbacks
    {
        get
        {
            _onElementSelectedCallback ??= EventCallback.Factory.Create<ModelingElementDto>(this, SelectElementAsync);
            _onElementDragStartedCallback ??= EventCallback.Factory.Create<ModelingElementDto>(this, HandleElementDragStartedAsync);
            _onElementDragEndedCallback ??= EventCallback.Factory.Create(this, HandleElementDragEndedAsync);
            _onInsertIntoPreviewCallback ??= EventCallback.Factory.Create<ModelingElementDto>(this, HandleInsertIntoPreviewAsync);
            _collapsedGroupsChangedCallback ??= EventCallback.Factory.Create<IReadOnlyCollection<string>>(this, value =>
            {
                _collapsedGroups = value;
                return Task.CompletedTask;
            });
            return (_onElementSelectedCallback!.Value,
                    _onElementDragStartedCallback!.Value,
                    _onElementDragEndedCallback!.Value,
                    _onInsertIntoPreviewCallback!.Value,
                    _collapsedGroupsChangedCallback!.Value);
        }
    }
}
