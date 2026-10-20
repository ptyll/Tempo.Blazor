namespace Tempo.Blazor.Components.Dashboard;

/// <summary>
/// Chrome of the <c>TmDashboard</c> root surface.
/// </summary>
public enum DashboardAppearance
{
    /// <summary>
    /// The reference look (default): the dashboard paints no wrapping card of its own — the root
    /// sits directly on the workspace background and the widget cards are the only surfaces.
    /// </summary>
    Plain,

    /// <summary>
    /// The pre-2.9.1 look: the root renders as a rounded surface card
    /// (<c>.tm-dashboard--framed</c>) for hosts that embed the dashboard on a contrasting page
    /// background.
    /// </summary>
    Card,
}
