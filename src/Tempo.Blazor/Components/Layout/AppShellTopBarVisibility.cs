namespace Tempo.Blazor.Components.Layout;

/// <summary>
/// On which layouts an <see cref="TmAppShell"/> renders its top bar region (2.12.0). The rule governs the
/// whole bar — the built-in <c>TmTopBar</c> and a host <see cref="TmAppShell.TopBar"/> slot alike — and
/// the bar is left out of the DOM, not hidden by CSS, so no empty banner landmark remains.
/// </summary>
public enum AppShellTopBarVisibility
{
    /// <summary>
    /// The bar renders on every layout whenever there is bar content (the behaviour before 2.12.0, and the
    /// default).
    /// </summary>
    Always = 0,

    /// <summary>
    /// The bar renders only on a compact layout — a tablet or a phone container — and is left out on a
    /// desktop one. A desktop whose brand and account already live in the docked sidebar's header and footer
    /// needs no second bar, so the host no longer hides the frame's header with its own CSS. The phone
    /// hamburger is unaffected: a phone is compact, so its bar (and the opener inside it) keeps rendering.
    /// </summary>
    CompactOnly = 1,
}
