namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// Whether <see cref="TmScheduler"/> renders the mobile action bar. The bar is opt-in: an existing host
/// keeps <see cref="None"/> and renders no bar.
/// </summary>
public enum TmSchedulerMobileActionBar
{
    /// <summary>No action bar, at any layout. The default.</summary>
    None,

    /// <summary>The bar renders on a mobile layout and stays hidden on tablet and desktop.</summary>
    Auto,
}
