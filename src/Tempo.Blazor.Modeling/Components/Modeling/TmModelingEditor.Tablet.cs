using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// The Fáze 6 tablet half of <see cref="TmModelingEditor"/> (kept beside the main code-behind to
/// respect the 1200-line source ceiling): the resolved-layout tracking, the tablet overlay rules
/// — at most ONE side panel open, enforced by the editor's own handlers so the rule holds even
/// before the shell's conflict pass — and the "Panels" tabs as the tablet visibility switch.
/// </summary>
public partial class TmModelingEditor
{
    // ── Resolved layout ──────────────────────────────────────────────────────

    /// <summary>Tracks the resolved layout (forced modes in OnInitialized/OnParametersSet, Auto via the shell).</summary>
    private void ApplyResolvedLayout(TmLayoutMode mode)
    {
        if (_resolvedLayout == mode)
        {
            return;
        }

        var wasTablet = _resolvedLayout == TmLayoutMode.Tablet;
        _resolvedLayout = mode;
        if (wasTablet && mode != TmLayoutMode.Tablet)
        {
            // Leaving the tablet drops its sheets: the desktop dock shows every region again
            // (the shell owns which columns are expanded through the same flags).
            _shellLeftOpen = true;
            _shellRightOpen = true;
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
        if (open)
        {
            _shellLeftOpen = true;
            if (_resolvedLayout == TmLayoutMode.Tablet) _shellRightOpen = false;
            return Task.CompletedTask;
        }

        _shellLeftOpen = false;
        return Task.CompletedTask;
    }

    private Task OnShellRightOpenChanged(bool open)
    {
        if (open)
        {
            _shellRightOpen = true;
            if (_resolvedLayout == TmLayoutMode.Tablet) _shellLeftOpen = false;
            return Task.CompletedTask;
        }

        _shellRightOpen = false;
        return Task.CompletedTask;
    }

    // ── The "Panels" tabs as the tablet visibility switch ────────────────────

    /// <summary>Toggles the tree drawer (the inspector sheet yields) — the tablet Tree tab.</summary>
    private Task ToggleTreeSheetAsync()
        => ToggleSheetAsync(open => OnShellLeftOpenChanged(open), _shellLeftOpen);

    /// <summary>Toggles the inspector side sheet (the tree drawer yields) — the tablet Inspector tab.</summary>
    private Task ToggleInspectorSheetAsync()
        => ToggleSheetAsync(open => OnShellRightOpenChanged(open), _shellRightOpen);

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
        if (_shellLeftOpen) await OnShellLeftOpenChanged(false);
        if (_shellRightOpen) await OnShellRightOpenChanged(false);
    }
}
