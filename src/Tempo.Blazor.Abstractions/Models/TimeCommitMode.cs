namespace Tempo.Blazor.Models;

/// <summary>
/// When a time-selector interaction writes <c>ValueChanged</c>: every step (live) or only on confirm
/// (draft). <see cref="Auto"/> follows the overlay (sheet = confirm, popover = live).
/// </summary>
public enum TimeCommitMode
{
    /// <summary>Sheet commits on confirm; popover commits live. The default.</summary>
    Auto = 0,

    /// <summary>Every step, grid tap and preset raises <c>ValueChanged</c> immediately.</summary>
    Live = 1,

    /// <summary>
    /// Edits accumulate as a draft; Done commits and Escape discards. Used by a bottom sheet.
    /// </summary>
    OnConfirm = 2,
}
