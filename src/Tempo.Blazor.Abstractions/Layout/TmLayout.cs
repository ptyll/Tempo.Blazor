namespace Tempo.Blazor.Abstractions.Layout;

/// <summary>
/// Resolves which layout a component renders. The order is fixed so a host, an overlay and a test
/// all reach the same answer: an explicit mode, then a forced ancestor, then this component's own
/// measurement, then an Auto ancestor's resolved mode, then the initial mode. An overlay measures
/// nothing, so it passes the viewport-scope context and a null measurement.
/// </summary>
public static class TmLayout
{
    /// <summary>
    /// The layout to render.
    /// </summary>
    /// <param name="explicitMode">The component's own <c>LayoutMode</c> parameter.</param>
    /// <param name="parent">The cascaded context, when one exists.</param>
    /// <param name="measured">The mode this component last measured. Null before the first report.</param>
    /// <param name="initial">The mode rendered before a measurement. Desktop unless the host says otherwise.</param>
    public static TmLayoutMode Resolve(
        TmLayoutMode explicitMode,
        TmLayoutContext? parent,
        TmLayoutMode? measured,
        TmLayoutMode initial)
    {
        if (explicitMode != TmLayoutMode.Auto)
            return explicitMode;

        // A forced ancestor is the host's decision. An ancestor that is itself Auto has not decided
        // anything, so this component keeps measuring — unless it cannot measure (an overlay), in
        // which case the ancestor's resolved mode is the app-level fallback.
        if (parent is { Mode: not TmLayoutMode.Auto })
            return parent.Resolved;

        if (measured is not null)
            return measured.Value;

        if (parent is not null)
            return parent.Resolved;

        return initial;
    }
}
