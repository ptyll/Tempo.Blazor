namespace Tempo.Blazor.Abstractions.Layout;

/// <summary>
/// The two widths that decide a container's <see cref="TmLayoutMode"/>: below <see cref="Sm"/> it is
/// mobile, from <see cref="Lg"/> up it is desktop, in between it is tablet. <see cref="Default"/> is
/// <see cref="TmBreakpoints.Sm"/> / <see cref="TmBreakpoints.Lg"/>; a component that owns a layout
/// observer (an editor shell, say) can pass its own pair when its content needs different
/// thresholds than the shared ones. The CSS conventions stay on <see cref="TmBreakpoints"/> — a
/// per-instance pair only moves the markup branch, never a stylesheet literal.
/// </summary>
public sealed record TmLayoutBreakpoints
{
    /// <summary>The thresholds every component shares: 640 / 1024.</summary>
    public static TmLayoutBreakpoints Default { get; } = new(TmBreakpoints.Sm, TmBreakpoints.Lg);

    /// <summary>Creates a threshold pair.</summary>
    /// <param name="sm">Below this width a container is <see cref="TmLayoutMode.Mobile"/>.</param>
    /// <param name="lg">From this width up a container is <see cref="TmLayoutMode.Desktop"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown unless <c>0 &lt; sm &lt; lg</c>.</exception>
    public TmLayoutBreakpoints(int sm, int lg)
    {
        if (sm <= 0)
            throw new ArgumentOutOfRangeException(nameof(sm), sm, "The mobile threshold must be a positive number of CSS pixels.");
        if (lg <= sm)
            throw new ArgumentOutOfRangeException(nameof(lg), lg, "The desktop threshold must be greater than the mobile threshold.");

        Sm = sm;
        Lg = lg;
    }

    /// <summary>Below this width a container is <see cref="TmLayoutMode.Mobile"/>.</summary>
    public int Sm { get; }

    /// <summary>From this width up a container is <see cref="TmLayoutMode.Desktop"/>.</summary>
    public int Lg { get; }

    /// <summary>The layout a measured container width resolves to under these thresholds (half-open, like <see cref="TmBreakpoints.Classify"/>).</summary>
    /// <param name="widthPx">The container's inline size in CSS pixels.</param>
    public TmLayoutMode Classify(double widthPx)
    {
        if (!double.IsFinite(widthPx) || widthPx < 0)
            throw new ArgumentOutOfRangeException(nameof(widthPx), widthPx, "A container width must be a finite, non-negative number of CSS pixels.");

        if (widthPx < Sm) return TmLayoutMode.Mobile;
        if (widthPx < Lg) return TmLayoutMode.Tablet;
        return TmLayoutMode.Desktop;
    }
}
